﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Xml;

using OpenHome.Os.App;

namespace OpenHome.Av
{
    public interface IMediaValue
    {
        string Value { get; }
        IEnumerable<string> Values { get; }
    }

    public interface IMediaMetadata : IEnumerable<KeyValuePair<ITag, IMediaValue>>
    {
        IMediaValue this[ITag aTag] { get; }
    }

    public interface IMediaDatum : IMediaMetadata
    {
        IEnumerable<ITag> Type { get; }
    }

    public interface IMediaPreset : IDisposable
    {
        uint Index { get; }
        uint Id { get; }
        IMediaMetadata Metadata { get; }
        IWatchable<bool> Playing { get; }
        void Play();
    }

    public interface IWatchableFragment<T>
    {
        uint Index { get; }
        IEnumerable<T> Data { get; }
    }

    public interface IWatchableSnapshot<T>
    {
        uint Total { get; }
        IEnumerable<uint> AlphaMap { get; } // null if no alpha map
        Task<IWatchableFragment<T>> Read(uint aIndex, uint aCount);
    }

    public interface IWatchableContainer<T>
    {
        IWatchable<IWatchableSnapshot<T>> Snapshot { get; }
    }

    public class MediaServerValue : IMediaValue
    {
        private readonly string iValue;
        private readonly List<string> iValues;

        public MediaServerValue(string aValue)
        {
            iValue = aValue;
            iValues = new List<string>(new string[] { aValue });
        }

        public MediaServerValue(IEnumerable<string> aValues)
        {
            iValue = aValues.First();
            iValues = new List<string>(aValues);
        }

        // IMediaServerValue

        public string Value
        {
            get { return (iValue); }
        }

        public IEnumerable<string> Values
        {
            get { return (iValues); }
        }
    }

    public class MediaDictionary
    {
        protected Dictionary<ITag, IMediaValue> iMetadata;

        protected MediaDictionary()
        {
            iMetadata = new Dictionary<ITag, IMediaValue>();
        }

        protected MediaDictionary(IMediaMetadata aMetadata)
        {
            iMetadata = new Dictionary<ITag, IMediaValue>(aMetadata.ToDictionary(x => x.Key, x => x.Value));
        }

        public void Add(ITag aTag, string aValue)
        {
            IMediaValue value = null;

            iMetadata.TryGetValue(aTag, out value);

            if (value == null)
            {
                iMetadata[aTag] = new MediaServerValue(aValue);
            }
            else
            {
                iMetadata[aTag] = new MediaServerValue(value.Values.Concat(new string[] { aValue }));
            }
        }

        public void Add(ITag aTag, IMediaValue aValue)
        {
            IMediaValue value = null;

            iMetadata.TryGetValue(aTag, out value);

            if (value == null)
            {
                iMetadata[aTag] = aValue;
            }
            else
            {
                iMetadata[aTag] = new MediaServerValue(value.Values.Concat(aValue.Values));
            }
        }

        public void Add(ITag aTag, IMediaMetadata aMetadata)
        {
            var value = aMetadata[aTag];

            if (value != null)
            {
                Add(aTag, value);
            }
        }

        // IMediaServerMetadata

        public IMediaValue this[ITag aTag]
        {
            get
            {
                IMediaValue value = null;
                iMetadata.TryGetValue(aTag, out value);
                return (value);
            }
        }
    }

    public class MediaMetadata : MediaDictionary, IMediaMetadata
    {
        public MediaMetadata()
        {
        }

        // IEnumerable<KeyValuePair<ITag, IMediaServer>>

        public IEnumerator<KeyValuePair<ITag, IMediaValue>> GetEnumerator()
        {
            return (iMetadata.GetEnumerator());
        }

        // IEnumerable Members

        IEnumerator IEnumerable.GetEnumerator()
        {
            return (iMetadata.GetEnumerator());
        }
    }

    public class MediaDatum : MediaDictionary, IMediaDatum
    {
        private readonly ITag[] iType;

        public MediaDatum(params ITag[] aType)
        {
            iType = aType;
        }

        public MediaDatum(IMediaMetadata aMetadata, params ITag[] aType)
            : base(aMetadata)
        {
            iType = aType;
        }

        // IMediaDatum Members

        public IEnumerable<ITag> Type
        {
            get { return (iType); }
        }

        // IEnumerable<KeyValuePair<ITag, IMediaServer>>

        public IEnumerator<KeyValuePair<ITag, IMediaValue>> GetEnumerator()
        {
            return (iMetadata.GetEnumerator());
        }

        // IEnumerable Members

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }

    public class WatchableFragment<T> : IWatchableFragment<T>
    {
        private readonly uint iIndex;
        private readonly IEnumerable<T> iData;

        public WatchableFragment(uint aIndex, IEnumerable<T> aData)
        {
            iIndex = aIndex;
            iData = aData;
        }

        // IWatchableFragment<T>

        public uint Index
        {
            get { return (iIndex); }
        }

        public IEnumerable<T> Data
        {
            get { return (iData); }
        }
    }

    public static class MediaExtensions
    {
        private static readonly string kNsDidl = "urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/";
        private static readonly string kNsDc = "http://purl.org/dc/elements/1.1/";
        private static readonly string kNsUpnp = "urn:schemas-upnp-org:metadata-1-0/upnp/";

        public static string ToDidlLite(this ITagManager aTagManager, IMediaMetadata aMetadata)
        {
            if (aMetadata == null)
            {
                return string.Empty;
            }
            if (aMetadata[aTagManager.System.Folder] != null)
            {
                return aMetadata[aTagManager.System.Folder].Value;
            }

            XmlDocument document = new XmlDocument();

            XmlElement didl = document.CreateElement("DIDL-Lite", kNsDidl);

            XmlElement container = document.CreateElement("item", kNsDidl);

            XmlElement title = document.CreateElement("dc", "title", kNsDc);
            title.AppendChild(document.CreateTextNode(aMetadata[aTagManager.Audio.Title].Value));

            container.AppendChild(title);

            XmlElement cls = document.CreateElement("upnp", "class", kNsUpnp);
            cls.AppendChild(document.CreateTextNode("object.item.audioItem.musicTrack"));

            container.AppendChild(cls);

            foreach (var a in aMetadata[aTagManager.Audio.Artwork].Values)
            {
                XmlElement artwork = document.CreateElement("upnp", "albumArtURI", kNsUpnp);
                artwork.AppendChild(document.CreateTextNode(a));
                container.AppendChild(artwork);
            }

            if (aMetadata[aTagManager.Audio.AlbumTitle] != null)
            {
                XmlElement albumtitle = document.CreateElement("upnp", "album", kNsUpnp);
                albumtitle.AppendChild(document.CreateTextNode(aMetadata[aTagManager.Audio.AlbumTitle].Value));
                container.AppendChild(albumtitle);
            }

            foreach(var a in aMetadata[aTagManager.Audio.Artist].Values)
            {
                XmlElement artist = document.CreateElement("upnp", "artist", kNsUpnp);
                artist.AppendChild(document.CreateTextNode(a));
                container.AppendChild(artist);
            }

            if (aMetadata[aTagManager.Audio.AlbumArtist] != null)
            {
                XmlElement albumartist = document.CreateElement("upnp", "artist", kNsUpnp);
                albumartist.AppendChild(document.CreateTextNode(aMetadata[aTagManager.Audio.AlbumArtist].Value));
                XmlAttribute role = document.CreateAttribute("upnp", "role", kNsUpnp);
                role.AppendChild(document.CreateTextNode("albumartist"));
                albumartist.Attributes.Append(role);
                container.AppendChild(albumartist);
            }

            didl.AppendChild(container);

            document.AppendChild(didl);

            return document.OuterXml;
        }

        public static IMediaMetadata FromDidlLite(this ITagManager aTagManager, string aMetadata)
        {
            MediaMetadata metadata = new MediaMetadata();

            if (!string.IsNullOrEmpty(aMetadata))
            {
                XmlDocument document = new XmlDocument();
                XmlNamespaceManager nsManager = new XmlNamespaceManager(document.NameTable);
                nsManager.AddNamespace("didl", kNsDidl);
                nsManager.AddNamespace("upnp", kNsUpnp);
                nsManager.AddNamespace("dc", kNsDc);
                nsManager.AddNamespace("ldl", "urn:linn-co-uk/DIDL-Lite");

                try
                {
                    document.LoadXml(aMetadata);

                    //string c = document.SelectSingleNode("/didl:DIDL-Lite/*/upnp:class", nsManager).FirstChild.Value;
                    string title = document.SelectSingleNode("/didl:DIDL-Lite/*/dc:title", nsManager).FirstChild.Value;
                    metadata.Add(aTagManager.Audio.Title, title);

                    XmlNode res = document.SelectSingleNode("/didl:DIDL-Lite/*/didl:res", nsManager);
                    if (res != null)
                    {
                        string uri = res.FirstChild.Value;
                        metadata.Add(aTagManager.Audio.Uri, uri);
                    }

                    XmlNodeList albumart = document.SelectNodes("/didl:DIDL-Lite/*/upnp:albumArtURI", nsManager);
                    foreach (XmlNode n in albumart)
                    {
                        metadata.Add(aTagManager.Audio.Artwork, n.FirstChild.Value);
                    }

                    XmlNode album = document.SelectSingleNode("/didl:DIDL-Lite/*/upnp:album", nsManager);
                    if (album != null)
                    {
                        metadata.Add(aTagManager.Audio.Album, album.FirstChild.Value);
                    }

                    XmlNode artist = document.SelectSingleNode("/didl:DIDL-Lite/*/upnp:artist", nsManager);
                    if (artist != null)
                    {
                        metadata.Add(aTagManager.Audio.Artist, artist.FirstChild.Value);
                    }

                    XmlNodeList genre = document.SelectNodes("/didl:DIDL-Lite/*/upnp:genre", nsManager);
                    foreach (XmlNode n in genre)
                    {
                        metadata.Add(aTagManager.Audio.Genre, n.FirstChild.Value);
                    }

                    XmlNode albumartist = document.SelectSingleNode("/didl:DIDL-Lite/*/upnp:artist[@role='AlbumArtist']", nsManager);
                    if (albumartist != null)
                    {
                        metadata.Add(aTagManager.Audio.AlbumArtist, albumartist.FirstChild.Value);
                    }
                }
                catch (XmlException) { }
            }
            
            metadata.Add(aTagManager.System.Folder, aMetadata);
            
            return metadata;
        }
    }
}
