﻿using System;
using System.Collections.Generic;

using OpenHome.Os.App;
using OpenHome.Av;
using OpenHome.Net.Core;

namespace TestTopology2
{
    class Program
    {
        class ExceptionReporter : IExceptionReporter
        {
            public void ReportException(Exception e)
            {
                Console.WriteLine(e);
                Environment.Exit(-1);
            }
        }

        class SourceWatcher : IWatcher<ITopology2Source>, IDisposable
        {
            public SourceWatcher()
            {
            }

            public void Dispose()
            {
            }

            public void ItemOpen(string aId, ITopology2Source aValue)
            {
                Console.WriteLine(string.Format("{0}. {1} {2} {3}", aId, aValue.Name, aValue.Type, aValue.Visible));
            }

            public void ItemClose(string aId, ITopology2Source aValue)
            {
            }

            public void ItemUpdate(string aId, ITopology2Source aValue, ITopology2Source aPrevious)
            {
                Console.WriteLine(string.Format("{0}. {1} {2} {3} -> {4} {5} {6}", aId, aPrevious.Name, aPrevious.Type, aPrevious.Visible, aValue.Name, aValue.Type, aValue.Visible));
                Console.WriteLine("");
            }
        }

        class GroupWatcher : ICollectionWatcher<ITopology2Group>, IWatcher<string>, IDisposable
        {
            public GroupWatcher()
            {
                iLock = new object();
                iDisposed = false;

                iWatcher = new SourceWatcher();

                iStringLookup = new Dictionary<string, string>();
                iList = new List<ITopology2Group>();
            }

            public void Dispose()
            {
                lock (iLock)
                {   
                    foreach (ITopology2Group g in iList)
                    {
                        foreach (IWatchable<ITopology2Source> s in g.Sources)
                        {
                            s.RemoveWatcher(iWatcher);
                        }

                        g.Room.RemoveWatcher(this);
                        g.Name.RemoveWatcher(this);
                    }
                    iList = null;

                    iStringLookup = null;

                    iDisposed = true;
                }
            }

            public void CollectionOpen()
            {
            }

            public void CollectionClose()
            {
            }

            public void CollectionInitialised()
            {
            }

            public void CollectionAdd(ITopology2Group aItem, uint aIndex)
            {
                lock (iLock)
                {
                    if (iDisposed)
                    {
                        return;
                    }

                    aItem.Room.AddWatcher(this);
                    aItem.Name.AddWatcher(this);
                    iList.Add(aItem);

                    Console.WriteLine(string.Format("Group Added\t\t{0}:{1}", iStringLookup[string.Format("Room({0})", aItem.Id)], iStringLookup[string.Format("Name({0})", aItem.Id)]));
                    Console.WriteLine("===============================================");

                    foreach (IWatchable<ITopology2Source> s in aItem.Sources)
                    {
                        s.AddWatcher(iWatcher);
                    }

                    Console.WriteLine("===============================================");
                    Console.WriteLine("");
                }
            }

            public void CollectionMove(ITopology2Group aItem, uint aFrom, uint aTo)
            {
                throw new NotImplementedException();
            }

            public void CollectionRemove(ITopology2Group aItem, uint aIndex)
            {
                lock (iLock)
                {
                    if (iDisposed)
                    {
                        return;
                    }

                    aItem.Room.RemoveWatcher(this);
                    aItem.Name.RemoveWatcher(this);
                    iList.Remove(aItem);

                    Console.WriteLine("Group Remove at " + aIndex);
                }
            }

            public void ItemOpen(string aId, string aValue)
            {
                //Console.WriteLine("Open " + aId + " " + aValue);
                iStringLookup.Add(aId, aValue);
            }

            public void ItemClose(string aId, string aValue)
            {
                iStringLookup.Remove(aId);
            }

            public void ItemUpdate(string aId, string aValue, string aPrevious)
            {
                iStringLookup[aId] = aValue;
            }

            private object iLock;
            private bool iDisposed;

            private SourceWatcher iWatcher;
            private List<ITopology2Group> iList;
            private Dictionary<string, string> iStringLookup;
        }

        static void Main(string[] args)
        {
            InitParams initParams = new InitParams();
            Library library = Library.Create(initParams);

            SubnetList subnets = new SubnetList();
            library.StartCp(subnets.SubnetAt(0).Subnet());
            subnets.Dispose();

            ExceptionReporter reporter = new ExceptionReporter();
            WatchableThread thread = new WatchableThread(reporter);
            //MockTopology1 topology1 = new MockTopology1(thread);
            Topology1 topology1 = new Topology1(thread);
            Topology2 topology2 = new Topology2(topology1);

            GroupWatcher watcher = new GroupWatcher();
            topology2.Groups.AddWatcher(watcher);

            Mockable mocker = new Mockable();
            //mocker.Add("topology", topology1);

            MockableStream stream = new MockableStream(Console.In, mocker);
            stream.Start();

            topology2.Groups.RemoveWatcher(watcher);

            topology2.Dispose();

            topology1.Dispose();

            thread.Dispose();

            library.Dispose();
        }
    }
}
