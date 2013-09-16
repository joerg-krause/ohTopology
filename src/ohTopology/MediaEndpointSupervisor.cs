﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using System.Text;

using System.Net;

using OpenHome.Os.App;

using OpenHome.Net.ControlPoint;
using OpenHome.Net.ControlPoint.Proxies;


namespace OpenHome.Av
{
    public interface IMediaEndpointClientSnapshot
    {
        uint Total { get; }
        IEnumerable<uint> Alpha { get; } // null if no alpha map
    }

    public interface IMediaEndpointClient : IWatchableThread
    {
        string Create(CancellationToken aCancellationToken);
        void Destroy(CancellationToken aCancellationToken, string aId);
        IMediaEndpointClientSnapshot Browse(CancellationToken aCancellationToken, string aSession, IMediaDatum aDatum);
        IMediaEndpointClientSnapshot List(CancellationToken aCancellationToken, string aSession, ITag aTag);
        IMediaEndpointClientSnapshot Link(CancellationToken aCancellationToken, string aSession, ITag aTag, string aValue);
        IMediaEndpointClientSnapshot Search(CancellationToken aCancellationToken, string aSession, string aValue);
        IEnumerable<IMediaDatum> Read(CancellationToken aCancellationToken, string aSession, IMediaEndpointClientSnapshot aSnapshot, uint aIndex, uint aCount);
    }

    internal class MediaEndpointSupervisorSnapshot : IWatchableSnapshot<IMediaDatum>, IDisposable
    {
        private readonly IMediaEndpointClient iClient;
        private readonly MediaEndpointSupervisorSession iSession;
        private readonly CancellationToken iCancellationToken;
        private readonly IMediaEndpointClientSnapshot iSnapshot;

        private readonly DisposeHandler iDisposeHandler;

        private readonly List<Task> iTasks;

        public MediaEndpointSupervisorSnapshot(IMediaEndpointClient aClient, MediaEndpointSupervisorSession aSession, CancellationToken aCancellationToken, IMediaEndpointClientSnapshot aSnapshot)
        {
            iClient = aClient;
            iSession = aSession;
            iCancellationToken = aCancellationToken;
            iSnapshot = aSnapshot;

            iDisposeHandler = new DisposeHandler();

            iTasks = new List<Task>();
        }

        // IWatchableSnapshot<IMediaDatum>

        public uint Total
        {
            get
            {
                using (iDisposeHandler.Lock)
                {
                    return (iSnapshot.Total);
                }
            }
        }

        public IEnumerable<uint> Alpha
        {
            get
            {
                using (iDisposeHandler.Lock)
                {
                    return (iSnapshot.Alpha);
                }
            }
        }

        private Task<IWatchableFragment<IMediaDatum>> DoRead(uint aIndex, uint aCount)
        {
            var task = Task.Factory.StartNew<IWatchableFragment<IMediaDatum>>(() =>
            {
                iCancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var data = iClient.Read(iCancellationToken, iSession.Id, iSnapshot, aIndex, aCount);

                    return (new WatchableFragment<IMediaDatum>(aIndex, data));
                }
                catch
                {
                }

                throw (new OperationCanceledException());
            });

            lock (iTasks)
            {
                Task completion = null;

                completion = task.ContinueWith((t) =>
                {
                    try
                    {
                        t.Wait();
                    }
                    catch
                    {
                    }

                    lock (iTasks)
                    {
                        iTasks.Remove(completion);
                    }
                });

                iTasks.Add(completion);
            }

            return (task);
        }

        public Task<IWatchableFragment<IMediaDatum>> Read(uint aIndex, uint aCount)
        {
            iClient.Assert(); // Must be called on the watchable thread;

            Do.Assert(aIndex + aCount <= iSnapshot.Total);

            Task<IWatchableFragment<IMediaDatum>> task = null;

            if (!iDisposeHandler.WhenNotDisposed(() => task = DoRead(aIndex, aCount)))
            {
                task = Task.Factory.StartNew<IWatchableFragment<IMediaDatum>>(() =>
                {
                    throw new OperationCanceledException();
                });
            }

            return (task);
        }

        // IDisposable

        public void Dispose()
        {
            iDisposeHandler.Dispose();

            Task[] tasks;

            lock (iTasks)
            {
                tasks = iTasks.ToArray();
            }

            Task.WaitAll(tasks);

            lock (iTasks)
            {
                Do.Assert(iTasks.Count == 0);
            }
        }
    }

    internal class MediaEndpointWatchableWatchableSnapshot : IWatchable<IWatchableSnapshot<IMediaDatum>>, IDisposable
    {
        private readonly Watchable<IWatchableSnapshot<IMediaDatum>> iWatchable;

        private List<IWatcher<IWatchableSnapshot<IMediaDatum>>> iWatchers;

        private bool iDisposed;

        public MediaEndpointWatchableWatchableSnapshot(IWatchableThread aWatchableThread, string aId, IWatchableSnapshot<IMediaDatum> aValue)
        {
            iWatchable = new Watchable<IWatchableSnapshot<IMediaDatum>>(aWatchableThread, aId, aValue);
            iWatchers = new List<IWatcher<IWatchableSnapshot<IMediaDatum>>>();
            iDisposed = false;
        }

        public void Update(IWatchableSnapshot<IMediaDatum> aValue)
        {
            iWatchable.Update(aValue);
        }

        // IWatchableThread

        public void Assert()
        {
            iWatchable.Assert();
        }

        public void Execute(Action aAction)
        {
            iWatchable.Execute(aAction);
        }

        public void Schedule(Action aAction)
        {
            iWatchable.Schedule(aAction);
        }

        // IWatchable<IWatchableSnapshot<IMediaDatum>>

        public string Id
        {
            get
            {
                return (iWatchable.Id);
            }
        }

        public void AddWatcher(IWatcher<IWatchableSnapshot<IMediaDatum>> aWatcher)
        {
            iWatchers.Add(aWatcher);
            iWatchable.AddWatcher(aWatcher);
        }

        public void RemoveWatcher(IWatcher<IWatchableSnapshot<IMediaDatum>> aWatcher)
        {
            iWatchers.Remove(aWatcher);
            iWatchable.RemoveWatcher(aWatcher);
        }

        public IWatchableSnapshot<IMediaDatum> Value
        {
            get
            {
                return (iWatchable.Value);
            }
        }

        // IDisposable

        public void Dispose()
        {
        }

        ~MediaEndpointWatchableWatchableSnapshot()
        {
            iWatchable.Schedule(() =>
            {
                foreach (var watcher in iWatchers)
                {
                    iWatchable.RemoveWatcher(watcher);
                }

                iWatchable.Dispose();
            });
        }
    }

    internal class MediaEndpointSupervisorContainer : IWatchableContainer<IMediaDatum>, IDisposable
    {
        private readonly IMediaEndpointClient iClient;
        private readonly MediaEndpointSupervisorSession iSession;
        private readonly Func<IMediaEndpointClientSnapshot> iSnapshotFunction;
        private readonly DisposeHandler iDisposeHandler;
        private readonly CancellationTokenSource iCancellationSource;
        private MediaEndpointSupervisorSnapshot iSnapshot;
        private readonly MediaEndpointWatchableWatchableSnapshot iWatchableSnapshot;

        private Task iTask;

        public MediaEndpointSupervisorContainer(IMediaEndpointClient aClient, MediaEndpointSupervisorSession aSession, Func<IMediaEndpointClientSnapshot> aSnapshotFunction)
        {
            iClient = aClient;
            iSession = aSession;
            iSnapshotFunction = aSnapshotFunction;

            iDisposeHandler = new DisposeHandler();
            iCancellationSource = new CancellationTokenSource();
            iSnapshot = new MediaEndpointSupervisorSnapshot(iClient, iSession, iCancellationSource.Token, iSnapshotFunction());
            iWatchableSnapshot = new MediaEndpointWatchableWatchableSnapshot(iClient, "Snapshot", iSnapshot);
        }

        internal void Refresh()
        {
            // called on the watchable thread

            iDisposeHandler.WhenNotDisposed(() =>
            {
                iTask = Task.Factory.StartNew(() =>
                {
                    iDisposeHandler.WhenNotDisposed(() =>
                    {
                        var snapshot = iSnapshotFunction();

                        iClient.Schedule(() =>
                        {
                            iDisposeHandler.WhenNotDisposed(() =>
                            {
                                iSnapshot.Dispose();
                                iSnapshot = new MediaEndpointSupervisorSnapshot(iClient, iSession, iCancellationSource.Token, snapshot);
                                iWatchableSnapshot.Update(iSnapshot);
                            });
                        });
                    });
                });
            });
        }

        // IWatchableContainer<IMediaDatum>

        public IWatchable<IWatchableSnapshot<IMediaDatum>> Snapshot
        {
            get
            {
                // called on the watchable thread

                return (iWatchableSnapshot);
            }
        }

        // IDisposable

        public void Dispose()
        {
            // called on the watchable thread

            iCancellationSource.Cancel();
            
            iDisposeHandler.Dispose();

            if (iTask != null)
            {
                try
                {
                    iTask.Wait();
                }
                catch
                {
                }
            }

            iWatchableSnapshot.Dispose();

            iSnapshot.Dispose();
            
            iCancellationSource.Dispose();
        }
    }

    internal class MediaEndpointSupervisorSession : IMediaEndpointSession
    {
        private readonly IMediaEndpointClient iClient;
        private readonly CancellationToken iCancellationToken;
        private readonly string iId;
        private readonly Action<string> iDispose;

        private readonly DisposeHandler iDisposeHandler;

        private readonly List<Task> iTasks;

        private uint iSequence;

        private object iLock;

        private MediaEndpointSupervisorContainer iContainer;

        public MediaEndpointSupervisorSession(IMediaEndpointClient aClient, CancellationToken aCancellationToken, string aId, Action<string> aDispose)
        {
            iClient = aClient;
            iCancellationToken = aCancellationToken;
            iId = aId;
            iDispose = aDispose;

            iDisposeHandler = new DisposeHandler();

            iTasks = new List<Task>();

            iSequence = 0;

            iLock = new object();
        }

        internal string Id
        {
            get
            {
                return (iId);
            }
        }

        internal void Refresh()
        {
            // called on the watchable thread

            lock (iLock)
            {
                if (iContainer != null)
                {
                    iContainer.Refresh();
                }
            }
        }

        private Task<IWatchableContainer<IMediaDatum>> UpdateContainer(Func<IMediaEndpointClientSnapshot> aSnapshotFunction)
        {
            // called on the watchable thread

            Task.WaitAll(iTasks.ToArray());

            lock (iLock)
            {
                if (iContainer != null)
                {
                    iContainer.Dispose();
                    iContainer = null;
                }
            }

            uint sequence;
            
            lock (iLock)
            {
                sequence = ++iSequence;
            }

            var task = Task.Factory.StartNew<IWatchableContainer<IMediaDatum>>(() =>
            {
                iCancellationToken.ThrowIfCancellationRequested();

                MediaEndpointSupervisorContainer container;

                try
                {
                    container = new MediaEndpointSupervisorContainer(iClient, this, aSnapshotFunction);
                }
                catch
                {
                    throw (new OperationCanceledException());
                }

                lock (iLock)
                {
                    if (iSequence != sequence)
                    {
                        throw (new OperationCanceledException());
                    }

                    iContainer = container;
                }

                return (container);
            });

            lock (iTasks)
            {
                Task completion = null;

                completion = task.ContinueWith((t) =>
                {
                    try
                    {
                        t.Wait();
                    }
                    catch
                    {
                    }

                    lock (iTasks)
                    {
                        iTasks.Remove(completion);
                    }
                });

                iTasks.Add(completion);
            }

            return (task);
        }

        // IMediaEndpointSession

        public Task<IWatchableContainer<IMediaDatum>> Browse(IMediaDatum aDatum)
        {
            iClient.Assert(); // must be called on the watchable thread

            using (iDisposeHandler.Lock)
            {
                return (UpdateContainer(() => iClient.Browse(iCancellationToken, iId, aDatum)));
            }
        }

        public Task<IWatchableContainer<IMediaDatum>> List(ITag aTag)
        {
            iClient.Assert(); // must be called on the watchable thread

            using (iDisposeHandler.Lock)
            {
                return (UpdateContainer(() => iClient.List(iCancellationToken, iId, aTag)));
            }
        }

        public Task<IWatchableContainer<IMediaDatum>> Link(ITag aTag, string aValue)
        {
            iClient.Assert(); // must be called on the watchable thread

            using (iDisposeHandler.Lock)
            {
                return (UpdateContainer(() => iClient.Link(iCancellationToken, iId, aTag, aValue)));
            }
        }

        public Task<IWatchableContainer<IMediaDatum>> Search(string aValue)
        {
            iClient.Assert(); // must be called on the watchable thread

            using (iDisposeHandler.Lock)
            {
                return (UpdateContainer(() => iClient.Search(iCancellationToken, iId, aValue)));
            }
        }

        // IDisposable Members

        public void Dispose()
        {
            iClient.Assert(); // must be called on the watchable thread

            Task.WaitAll(iTasks.ToArray());

            iDisposeHandler.Dispose();

            lock (iLock)
            {
                if (iContainer != null)
                {
                    iContainer.Dispose();
                    iContainer = null;
                }
            }

            iDispose(iId);
        }
    }

    // MediaEndpointSupervisor provides all the complicated Task and session handling required by a concrete MediaEndpoint client
    // Instead of writing all this again, construc a MediaEndpointSupervisor with an IMediaEndpointClient that expresses you specific implementation
    // Use Refresh(), or preferably Refresh(string aSession), when the MediaEndpoint for which you are a client has changed its contents.

    public class MediaEndpointSupervisor
    {
        private readonly IMediaEndpointClient iClient;
        private readonly DisposeHandler iDisposeHandler;
        private readonly CancellationTokenSource iCancellationSource;
        private readonly List<Task> iCreateTasks;
        private readonly List<Task> iDestroyTasks;
        private readonly Dictionary<string, MediaEndpointSupervisorSession> iSessions;
        
        public MediaEndpointSupervisor(IMediaEndpointClient aClient)
        {
            iClient = aClient;
            iDisposeHandler = new DisposeHandler();
            iCancellationSource = new CancellationTokenSource();
            iCreateTasks = new List<Task>();
            iDestroyTasks = new List<Task>();
            iSessions = new Dictionary<string, MediaEndpointSupervisorSession>();
        }

        public void Refresh()
        {
            iClient.Assert(); // must be called on the watchable thread

            using (iDisposeHandler.Lock)
            {
                lock (iSessions)
                {
                    foreach (var session in iSessions)
                    {
                        session.Value.Refresh();
                    }
                }
            }
        }

        public void Refresh(string aSession)
        {
            iClient.Assert(); // must be called on the watchable thread

            using (iDisposeHandler.Lock)
            {
                lock (iSessions)
                {
                    MediaEndpointSupervisorSession session;

                    if (iSessions.TryGetValue(aSession, out session))
                    {
                        session.Refresh();
                    }
                }
            }
        }

        public void Close()
        {
            iCancellationSource.Cancel();
        }

        public Task<IMediaEndpointSession> CreateSession()
        {
            iClient.Assert(); // must be called on the watchable thread

            using (iDisposeHandler.Lock)
            {
                var task = Task.Factory.StartNew<IMediaEndpointSession>(() =>
                {
                    var token = iCancellationSource.Token;

                    string id;

                    try
                    {
                        id = iClient.Create(token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        throw (new OperationCanceledException());
                    }

                    var session = new MediaEndpointSupervisorSession(iClient, token, id, DestroySession);

                    lock (iSessions)
                    {
                        token.ThrowIfCancellationRequested();

                        iSessions.Add(id, session);
                    }

                    return (session);
                });

                Task completion = null;

                lock (iCreateTasks)
                {
                    completion = task.ContinueWith((t) =>
                    {
                        try
                        {
                            t.Wait();
                        }
                        catch
                        {
                        }

                        lock (iCreateTasks)
                        {
                            iCreateTasks.Remove(completion);
                        }
                    });

                    iCreateTasks.Add(completion);
                }

                return (task);
            }
        }

        private void DestroySession(string aId)
        {
            // called on the watchable thread

            lock (iSessions)
            {
                iSessions.Remove(aId);
            }

            var task = Task.Factory.StartNew(() =>
            {
                var token = iCancellationSource.Token;

                token.ThrowIfCancellationRequested();

                try
                {
                    iClient.Destroy(token, aId);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    throw (new OperationCanceledException());
                }
            });

            Task completion = null;

            lock (iDestroyTasks)
            {
                completion = task.ContinueWith((t) =>
                {
                    try
                    {
                        t.Wait();
                    }
                    catch
                    {
                    }

                    lock (iDestroyTasks)
                    {
                        iDestroyTasks.Remove(completion);
                    }
                });

                iDestroyTasks.Add(completion);
            }
        }

        // IDispose

        public void Dispose()
        {
            // users of the supervisor must close it, then indicate that their endpoint has disappeared, then dispose their supervisor
            // this gives clients the opportunity to dispose all their sessions in advance of the supervisor itself being disposed

            Do.Assert(iCancellationSource.IsCancellationRequested);

            iDisposeHandler.Dispose();

            // now guaranteed that no more sessions are being created

            Task[] tasks;

            lock (iCreateTasks)
            {
                tasks = iCreateTasks.ToArray();
            }

            Task.WaitAll(tasks);

            lock (iCreateTasks)
            {
                Do.Assert(iCreateTasks.Count == 0);
            }

            lock (iSessions)
            {
                Do.Assert(iSessions.Count == 0);
            }

            lock (iDestroyTasks)
            {
                tasks = iDestroyTasks.ToArray();
            }

            Task.WaitAll(tasks);

            lock (iDestroyTasks)
            {
                Do.Assert(iDestroyTasks.Count == 0);
            }

            iCancellationSource.Dispose();
        }
    }
}
