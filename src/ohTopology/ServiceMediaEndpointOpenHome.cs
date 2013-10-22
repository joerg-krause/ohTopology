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
    public class ServiceMediaEndpointOpenHome : ServiceMediaEndpoint, IMediaEndpointClient
    {
        private readonly string iUri;
        private readonly Action<string, Action<string, uint>> iSessionHandler;

        private readonly Encoding iEncoding;
        private readonly MediaEndpointSupervisor iSupervisor;

        public ServiceMediaEndpointOpenHome(INetwork aNetwork, IInjectorDevice aDevice, string aId, string aType, string aName, string aInfo,
            string aUrl, string aArtwork, string aManufacturerName, string aManufacturerInfo, string aManufacturerUrl,
            string aManufacturerArtwork, string aModelName, string aModelInfo, string aModelUrl, string aModelArtwork,
            DateTime aStarted, IEnumerable<string> aAttributes, string aUri, Action<string, Action<string, uint>> aSessionHandler, ILog aLog)
            : base (aNetwork, aDevice, aId, aType, aName, aInfo, aUrl, aArtwork, aManufacturerName, aManufacturerInfo,
            aManufacturerUrl, aManufacturerArtwork, aModelName, aModelInfo, aModelUrl, aModelArtwork, aStarted, aAttributes, aLog)
        {
            iUri = aUri;
            iSessionHandler = aSessionHandler;

            iEncoding = new UTF8Encoding(false);
            iSupervisor = new MediaEndpointSupervisor(this);
        }

        public override IProxy OnCreate(IDevice aDevice)
        {
            return (new ProxyMediaEndpoint(this, aDevice));
        }

        public override Task<IMediaEndpointSession> CreateSession()
        {
            return (iSupervisor.CreateSession());
        }

        internal string CreateUri(string aFormat, params object[] aArguments)
        {
            var relative = string.Format(aFormat, aArguments);
            return (string.Format("{0}/{1}", iUri, relative));
        }

        internal string ResolveUri(string aValue)
        {
            return (iUri + "/res" + aValue);
        }

        private Task<IMediaEndpointClientSnapshot> GetSnapshot(CancellationToken aCancellationToken, string aFormat, params object[] aArguments)
        {
            var tcs = new TaskCompletionSource<IMediaEndpointClientSnapshot>();

            var uri = CreateUri(aFormat, aArguments);

            var client = new WebClient();

            client.Encoding = iEncoding;

            var registrations = new List<CancellationTokenRegistration>();

            client.DownloadStringCompleted += (sender, args) =>
            {
                lock (registrations)
                {
                    foreach (var registration in registrations)
                    {
                        registration.Dispose();
                    }
                }

                client.Dispose();

                if (aCancellationToken.IsCancellationRequested || args.Error != null)
                {
                    tcs.SetCanceled();
                }
                else
                {
                    try
                    {
                        var json = JsonParser.Parse(args.Result) as JsonObject;
                        var total = GetTotal(json["Total"]);
                        var alpha = GetAlpha(json["Alpha"]);
                        tcs.SetResult(new MediaEndpointSnapshotOpenHome(total, alpha));
                    }
                    catch
                    {
                        tcs.SetCanceled();
                    }
                }
            };

            if (aCancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
            }
            else
            {
                lock (registrations)
                {
                    client.DownloadStringAsync(new Uri(uri));
                    registrations.Add(aCancellationToken.Register(() => client.CancelAsync()));
                }
            }

            return (tcs.Task);
        }

        private uint GetTotal(JsonValue aValue)
        {
            var value = aValue as JsonInteger;
            return ((uint)value.Value);
        }

        private IEnumerable<uint> GetAlpha(JsonValue aValue)
        {
            var value = aValue as JsonArray;

            foreach (var entry in value)
            {
                yield return (GetAlphaElement(entry));
            }
        }

        private uint GetAlphaElement(JsonValue aValue)
        {
            var value = aValue as JsonInteger;
            return ((uint)value.Value);
        }

        private IEnumerable<IMediaDatum> GetData(JsonValue aValue)
        {
            var value = aValue as JsonArray;

            foreach (var entry in value)
            {
                yield return (GetDatum(entry));
            }
        }

        private IMediaDatum GetDatum(JsonValue aValue)
        {
            var value = aValue as JsonObject;

            var id = GetValue(value["Id"]);
            var type = GetType(value["Type"]);

            var datum = new MediaDatum(id, type.ToArray());

            foreach (var entry in value["Metadata"] as JsonArray)
            {
                var values = GetMetadatumValues(entry);
                var tagid = uint.Parse(values.First());
                var tag = iNetwork.TagManager[tagid];
                var resolved = Resolve(tag, values.Skip(1));
                datum.Add(tag, new MediaValue(resolved));
            }

            return (datum);
        }

        private IEnumerable<string> Resolve(ITag aTag, IEnumerable<string> aValues)
        {
            if (aTag == iNetwork.TagManager.Audio.Artwork || aTag == iNetwork.TagManager.Container.Artwork || aTag == iNetwork.TagManager.Audio.Uri)
            {
                return (Resolve(aValues));
            }

            return (aValues);
        }

        private IEnumerable<string> Resolve(IEnumerable<string> aValues)
        {
            foreach (var value in aValues)
            {
                yield return (Resolve(value));
            }
        }

        private string Resolve(string aValue)
        {
            Uri absoluteUri;
            if (Uri.TryCreate(aValue, UriKind.Absolute, out absoluteUri))
            {
                if (!absoluteUri.IsFile)
                {
                    return aValue;
                }
            }
            return ResolveUri(aValue);
        }

        private IEnumerable<string> GetMetadatumValues(JsonValue aValue)
        {
            var value = aValue as JsonArray;

            foreach (var entry in value)
            {
                yield return (GetMetadatumValue(entry));
            }
        }

        private string GetMetadatumValue(JsonValue aValue)
        {
            var value = aValue as JsonString;
            return (value.Value);
        }

        private string GetValue(JsonValue aValue)
        {
            var value = aValue as JsonString;
            return (value.Value);
        }

        private IEnumerable<ITag> GetType(JsonValue aValue)
        {
            var value = aValue as JsonArray;

            foreach (var entry in value)
            {
                yield return (GetTag(entry));
            }
        }

        private ITag GetTag(JsonValue aValue)
        {
            var value = aValue as JsonString;

            var id = uint.Parse(value.Value);

            return (iNetwork.TagManager[id]);
        }


        private string Encode(string aValue)
        {
            var bytes = iEncoding.GetBytes(aValue);
            var value = System.Convert.ToBase64String(bytes);
            return (value);
        }

        // IMediaEndpointClient

        public Task<string> Create(CancellationToken aCancellationToken)
        {
            var tcs = new TaskCompletionSource<string>();

            var uri = CreateUri("create");

            var client = new WebClient();

            client.Encoding = iEncoding;

            var registrations = new List<CancellationTokenRegistration>();

            client.DownloadStringCompleted += (sender, args) =>
            {
                lock (registrations)
                {
                    foreach (var registration in registrations)
                    {
                        registration.Dispose();
                    }
                }

                client.Dispose();

                if (aCancellationToken.IsCancellationRequested || args.Error != null)
                {
                    tcs.SetCanceled();
                }
                else
                {
                    try
                    {
                        var json = JsonParser.Parse(args.Result) as JsonString;

                        var session = json.Value;

                        iSessionHandler("me." + iId + "." + session, (id, seq) =>
                        {
                            iSupervisor.Refresh(session);
                        });

                        tcs.SetResult(session);
                    }
                    catch
                    {
                        tcs.SetCanceled();
                    }
                }
            };

            if (aCancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
            }
            else
            {
                lock (registrations)
                {
                    client.DownloadStringAsync(new Uri(uri));
                    registrations.Add(aCancellationToken.Register(() => client.CancelAsync()));
                }
            }

            return (tcs.Task);
        }

        public Task<string> Destroy(CancellationToken aCancellationToken, string aId)
        {
            var tcs = new TaskCompletionSource<string>();

            var uri = CreateUri("destroy?session={0}", aId);

            var client = new WebClient();

            client.Encoding = iEncoding;

            var registrations = new List<CancellationTokenRegistration>();

            client.DownloadStringCompleted += (sender, args) =>
            {
                lock (registrations)
                {
                    foreach (var registration in registrations)
                    {
                        registration.Dispose();
                    }
                }

                client.Dispose();

                if (aCancellationToken.IsCancellationRequested || args.Error != null)
                {
                    tcs.SetCanceled();
                }
                else
                {
                    tcs.SetResult(aId);
                }
            };

            if (aCancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
            }
            else
            {
                lock (registrations)
                {
                    client.DownloadStringAsync(new Uri(uri));
                    registrations.Add(aCancellationToken.Register(() => client.CancelAsync()));
                }
            }

            return (tcs.Task);
        }

        public Task<IMediaEndpointClientSnapshot> Browse(CancellationToken aCancellationToken, string aSession, IMediaDatum aDatum)
        {
            if (aDatum == null)
            {
                return (GetSnapshot(aCancellationToken, "browse?session={0}&id={1}", aSession, "0"));
            }

            return (GetSnapshot(aCancellationToken, "browse?session={0}&id={1}", aSession, aDatum.Id));
        }

        public Task<IMediaEndpointClientSnapshot> List(CancellationToken aCancellationToken, string aSession, ITag aTag)
        {
            return (GetSnapshot(aCancellationToken, "list?session={0}&tag={1}", aSession, aTag.Id));
        }

        public Task<IMediaEndpointClientSnapshot> Link(CancellationToken aCancellationToken, string aSession, ITag aTag, string aValue)
        {
            return (GetSnapshot(aCancellationToken, "link?session={0}&tag={1}&val={2}", aSession, aTag.Id, Encode(aValue)));
        }

        public Task<IMediaEndpointClientSnapshot> Match(CancellationToken aCancellationToken, string aSession, ITag aTag, string aValue)
        {
            return (GetSnapshot(aCancellationToken, "match?session={0}&tag={1}&val={2}", aSession, aTag.Id, Encode(aValue)));
        }

        public Task<IMediaEndpointClientSnapshot> Search(CancellationToken aCancellationToken, string aSession, string aValue)
        {
            return (GetSnapshot(aCancellationToken, "search?session={0}&val={1}", aSession, Encode(aValue)));
        }

        public Task<IEnumerable<IMediaDatum>> Read(CancellationToken aCancellationToken, string aSession, IMediaEndpointClientSnapshot aSnapshot, uint aIndex, uint aCount)
        {
            var tcs = new TaskCompletionSource<IEnumerable<IMediaDatum>>();

            var uri = CreateUri("read?session={0}&index={1}&count={2}", aSession, aIndex, aCount);

            var client = new WebClient();

            client.Encoding = iEncoding;

            var registrations = new List<CancellationTokenRegistration>();

            client.DownloadStringCompleted += (sender, args) =>
            {
                lock (registrations)
                {
                    foreach (var registration in registrations)
                    {
                        registration.Dispose();
                    }
                }

                client.Dispose();

                if (aCancellationToken.IsCancellationRequested || args.Error != null)
                {
                    tcs.SetCanceled();
                }
                else
                {
                    var json = JsonParser.Parse(args.Result);

                    if (aCancellationToken.IsCancellationRequested)
                    {
                        tcs.SetCanceled();
                    }
                    else
                    {
                        var data = GetData(json);

                        if (aCancellationToken.IsCancellationRequested)
                        {
                            tcs.SetCanceled();
                        }
                        else
                        {
                            tcs.SetResult(data);
                        }
                    }
                }
            };

            if (aCancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
            }
            else
            {
                lock (registrations)
                {
                    client.DownloadStringAsync(new Uri(uri));

                    registrations.Add(aCancellationToken.Register(() =>
                    {
                        try
                        {
                            client.CancelAsync();
                        }
                        catch (WebException)
                        {
                            // we have wittnessed an undocumented and inexplicable WebException here, so throw those away
                        }
                    }));
                }
            }

            return (tcs.Task);
        }

        // IDispose

        public override void Dispose()
        {
            iSupervisor.Cancel();

            base.Dispose();

            iSupervisor.Dispose();
        }
    }

    internal class MediaEndpointSnapshotOpenHome : IMediaEndpointClientSnapshot
    {
        private readonly uint iTotal;
        private readonly IEnumerable<uint> iAlpha;

        public MediaEndpointSnapshotOpenHome(uint aTotal, IEnumerable<uint> aAlpha)
        {
            iTotal = aTotal;
            iAlpha = aAlpha;
        }

        // IMediaEndpointClientSnapshot

        public uint Total
        {
            get
            {
                return (iTotal);
            }
        }

        public IEnumerable<uint> Alpha
        {
            get
            {
                return (iAlpha);
            }
        }
    }
}
