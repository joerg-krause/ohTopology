﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using OpenHome;
using OpenHome.Net.Core;
using OpenHome.Av;
using OpenHome.Os;
using OpenHome.Os.App;

namespace BrowseMediaEndpoint
{
    public class Main : IDisposable
    {
        private readonly Library iLibrary;
        private readonly NetworkAdapter iAdapter;

        private readonly WatchableThread iWatchableThread;
        private readonly Network iNetwork;
        
        private InjectorMediaEndpoint iDeviceInjectorMediaEndpoint;
        private IWatchableUnordered<IDevice> iDevices;

        private IProxyMediaEndpoint iMediaEndpoint;
        
        private IMediaEndpointSession iMediaEndpointSession;

        private IEnumerable<IMediaDatum> iData;

        private void ReportException(Exception aException)
        {
            Console.WriteLine(aException);
        }

        public Main(Library aLibrary, NetworkAdapter aAdapter)
        {
            iLibrary = aLibrary;
            iAdapter = aAdapter;

            iLibrary.StartCp(iAdapter.Subnet());

            iWatchableThread = new WatchableThread(ReportException);

            Log log = new Log(new LogConsole());

            iNetwork = new Network(iWatchableThread, 5000, log);

            iNetwork.Execute(() =>
            {
                iDeviceInjectorMediaEndpoint = new InjectorMediaEndpoint(iNetwork, log);
                iDevices = iNetwork.Create<IProxyMediaEndpoint>();
            });
        }

        public void Run()
        {
            while (true)
            {
                var command = Console.ReadLine();

                if (command == null)
                {
                    break;
                }

                var tokens = Tokeniser.Parse(command);

                if (tokens.Any())
                {
                    var token = tokens.First().ToLowerInvariant();

                    switch (token)
                    {
                        case "q":
                        case "x":
                            return;
                        case "b":
                            Browse(tokens.Skip(1));
                            break;
                        case "l":
                            List(tokens.Skip(1));
                            break;
                        case "m":
                            Match(tokens.Skip(1));
                            break;
                        case "s":
                            Search(tokens.Skip(1));
                            break;
                        case "a":
                            All();
                            break;
                        default:
                            Select(tokens);
                            break;
                    }
                }
                else
                {
                    iWatchableThread.Schedule(EnumerateEndpoints);
                }
            }
        }

        private void All()
        {
            if (iMediaEndpoint != null)
            {
                iWatchableThread.Schedule(() =>
                {
                    if (iMediaEndpointSession.Snapshot != null)
                    {
                        AlphaMap(iMediaEndpointSession.Snapshot);

                        iMediaEndpointSession.Snapshot.Read(0, iMediaEndpointSession.Snapshot.Total, (fragment) =>
                        {
                            iNetwork.Schedule(() =>
                            {
                                All(fragment.Data);
                            });
                        });
                    }
                });
            }
        }

        private void AlphaMap(IWatchableSnapshot<IMediaDatum> aSnapshot)
        {
            if (aSnapshot.Alpha != null)
            {
                Console.Write("Alpha:");

                foreach (var entry in aSnapshot.Alpha)
                {
                    Console.Write(" {0}", entry);
                }

                Console.WriteLine();
            }
        }

        private void All(IEnumerable<IMediaDatum> aValue)
        {
            iData = aValue;

            int index = 0;

            foreach (var entry in aValue)
            {
                Console.WriteLine("{0}: {1}", index++, entry.Id);

                All(entry);
            }
        }

        private void All(IMediaDatum aValue)
        {
            Console.WriteLine("Type: {0}", string.Join(":", aValue.Type.Select(t => t.FullName)));

            foreach (var metadatum in aValue)
            {
                All(metadatum);
            }
        }

        private void All(KeyValuePair<ITag, IMediaValue> aValue)
        {
            Console.WriteLine("{0}: {1}", aValue.Key.FullName, string.Join(":", aValue.Value.Values));
        }

        private void List(IEnumerable<string> aTokens)
        {
            if (iMediaEndpoint != null)
            {
                if (aTokens.Any())
                {
                    iWatchableThread.Schedule(() =>
                    {
                        List(aTokens.First());
                    });
                }
            }
        }

        private void List(string aValue)
        {
            var tag = iNetwork.TagManager.Audio[aValue];

            if (tag != null)
            {
                if (iMediaEndpoint.SupportsList())
                {
                    var sw = new Stopwatch();

                    var count = 0;

                    sw.Start();

                    iMediaEndpointSession.List(tag, () =>
                    {
                        if (count++ == 0)
                        {
                            sw.Stop();
                            Console.WriteLine("{0}ms", sw.Milliseconds);
                        }

                        Console.WriteLine("{0}: {1} items", count, iMediaEndpointSession.Snapshot.Total);
                    });
                }
            }
        }

        private void Browse(IEnumerable<string> aTokens)
        {
            if (iMediaEndpoint != null)
            {
                if (aTokens.Any())
                {
                    uint index;

                    if (uint.TryParse(aTokens.First(), out index))
                    {
                        if (iData != null)
                        {
                            if (iData.Count() > index)
                            {
                                iWatchableThread.Schedule(() =>
                                {
                                    Browse(iData.ElementAt((int)index));
                                });
                            }
                        }
                    }
                }
                else
                {
                    iWatchableThread.Schedule(() =>
                    {
                        Browse((IMediaDatum)null);
                    });
                }
            }
        }

        private void Browse(IMediaDatum aDatum)
        {
            try
            {
                var sw = new Stopwatch();

                var count = 0;

                sw.Start();

                iMediaEndpointSession.Browse(aDatum, () =>
                {
                    if (count++ == 0)
                    {
                        sw.Stop();
                        Console.WriteLine("{0}ms", sw.Milliseconds);
                    }

                    Console.WriteLine("snapshot {0}: {1} items", count, iMediaEndpointSession.Snapshot.Total);
                });
            }
            catch
            {
                Console.WriteLine("Operation failed");
            }
        }

        private void Match(IEnumerable<string> aTokens)
        {
            if (iMediaEndpoint != null)
            {
                if (aTokens.Any())
                {
                    var tag = iNetwork.TagManager.Audio[aTokens.First()];

                    if (tag != null)
                    {
                        Match(tag, aTokens.Skip(1));
                    }
                }
            }
        }

        private void Match(ITag aTag, IEnumerable<string> aTokens)
        {
            if (aTokens.Any())
            {
                iWatchableThread.Schedule(() =>
                {
                    Match(aTag, aTokens.First());
                });
            }
        }

        private void Match(ITag aTag, string aValue)
        {
            try
            {
                var sw = new Stopwatch();

                var count = 0;

                sw.Start();

                iMediaEndpointSession.Match(aTag, aValue, () =>
                {
                    if (count++ == 0)
                    {
                        sw.Stop();
                        Console.WriteLine("{0}ms", sw.Milliseconds);
                    }

                    Console.WriteLine("{0}: {1} items", count, iMediaEndpointSession.Snapshot.Total);
                });
            }
            catch
            {
                Console.WriteLine("Operation failed");
            }
        }

        private void Search(IEnumerable<string> aTokens)
        {
            if (iMediaEndpoint != null)
            {
                if (aTokens.Any())
                {
                    iWatchableThread.Schedule(() =>
                    {
                        Search(aTokens.First());
                    });
                }
            }
        }

        private void Search(string aValue)
        {
            try
            {
                var sw = new Stopwatch();

                var count = 0;

                sw.Start();

                iMediaEndpointSession.Search(aValue, () =>
                {
                    if (count++ == 0)
                    {
                        sw.Stop();
                        Console.WriteLine("{0}ms", sw.Milliseconds);
                    }

                    Console.WriteLine("{0}: {1} items", count, iMediaEndpointSession.Snapshot.Total);
                });
            }
            catch
            {
                Console.WriteLine("Operation failed");
            }
        }

        private void Select(IEnumerable<string> aTokens)
        {
            if (aTokens.Any())
            {
                try
                {
                    uint index = uint.Parse(aTokens.First());

                    iWatchableThread.Schedule(() =>
                    {
                        Select(index);
                    });
                }
                catch
                {
                }
            }
            else
            {
                iWatchableThread.Schedule(Select);
            }
        }

        private void Select()
        {
            if (iMediaEndpoint == null)
            {
                Console.WriteLine("No media endpoint selected");
            }
            else
            {
                Console.WriteLine("Selected: {0}:{1}", iMediaEndpoint.Type, iMediaEndpoint.Name);
            }
        }

        private void Select(uint aIndex)
        {
            uint index = 0;

            foreach (var entry in iDevices.Values)
            {
                entry.Create<IProxyMediaEndpoint>((me) =>
                {
                    if (me.Attributes.Contains("Search"))
                    {
                        if (index++ == aIndex)
                        {
                            if (iMediaEndpoint != null)
                            {
                                iMediaEndpoint.Dispose();

                                if (iMediaEndpointSession != null)
                                {
                                    iMediaEndpointSession.Dispose();
                                    iMediaEndpointSession = null;
                                }
                            }

                            iMediaEndpoint = me;

                            iMediaEndpoint.CreateSession((session) =>
                            {
                                if (iMediaEndpointSession != null)
                                {
                                    iMediaEndpointSession.Dispose();
                                }

                                iMediaEndpointSession = session;

                                Select();
                            });

                            return;
                        }
                    }

                    me.Dispose();
                });
            }
        }

        private void EnumerateEndpoints()
        {
            uint index = 0;

            foreach (var entry in iDevices.Values)
            {
                entry.Create<IProxyMediaEndpoint>((me) =>
                {
                    if (me.Attributes.Contains("Search"))
                    {
                        Console.WriteLine("{0}. {1}:{2}", index++, me.Type, me.Name);
                    }

                    me.Dispose();
                });
            }

            Select();
        }

        // IDisposable

        public void Dispose()
        {
            iWatchableThread.Execute(() =>
            {
                if (iMediaEndpoint != null)
                {
                    iMediaEndpoint.Dispose();

                    if (iMediaEndpointSession != null)
                    {
                        iMediaEndpointSession.Dispose();
                        iMediaEndpointSession = null;
                    }
                }
            });

            iDeviceInjectorMediaEndpoint.Dispose();

            iNetwork.Dispose();
            
            iWatchableThread.Dispose();
        }
    }
}
