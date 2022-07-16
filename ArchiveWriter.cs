using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CSystemArc
{
    internal class ArchiveWriter : IDisposable
    {
        private const int MaxContentArchiveSize = 0x7FFFFFFF;

        private readonly int _version;
        private readonly Stream _indexStream;
        private readonly IList<Stream> _contentStreams;
        private readonly bool _leaveOpen;
        private bool _disposed;

        private readonly List<ArchiveEntry> _entries = new List<ArchiveEntry>();

        public ArchiveWriter(int version, string indexFilePath, IList<string> contentFilePaths)
        {
            _version = version;
            _indexStream = File.Open(indexFilePath, FileMode.Create, FileAccess.Write);
            _contentStreams = new List<Stream>();
            foreach (string contentFilePath in contentFilePaths)
            {
                _contentStreams.Add(File.Open(contentFilePath, FileMode.Create, FileAccess.Write));
            }
            
            _leaveOpen = false;
        }

        public ArchiveWriter(int version, Stream indexStream, IList<Stream> contentStreams, bool leaveOpen = false)
        {
            _version = version;
            _indexStream = indexStream;
            _contentStreams = contentStreams;
            _leaveOpen = leaveOpen;
        }

        public void Write(int id, char type, char subType, byte[] content)
        {
            Write(id, type, subType, new ArraySegment<byte>(content));
        }

        public void Write(int id, char type, char subType, ArraySegment<byte> content)
        {
            int contentArchiveIndex = 0;
            while (_contentStreams[contentArchiveIndex].Length + content.Count > MaxContentArchiveSize)
            {
                contentArchiveIndex++;
            }

            Stream contentStream = _contentStreams[contentArchiveIndex];
            ArchiveEntry entry =
                new ArchiveEntry
                {
                    Version = _version,
                    ContentArchiveIndex = contentArchiveIndex,
                    Id = id,
                    Type = type,
                    SubType = subType,
                    Offset = (int)contentStream.Position,
                    UncompressedSize = content.Count
                };

            bool compressed = (type == 'b' ||
                               type == 'c' ||
                               type == 'e' ||
                               type == 'n' ||
                               type == 'o' ||
                               type == 'p');
            if (compressed)
            {
                using LzssStream lzss = new LzssStream(contentStream, CompressionMode.Compress, true);
                lzss.Write(content.Array, content.Offset, content.Count);
            }
            else
            {
                contentStream.Write(content.Array, content.Offset, content.Count);
            }

            entry.CompressedSize = (int)contentStream.Position - entry.Offset;
            _entries.Add(entry);
        }

        private void WriteIndex()
        {
            using MemoryStream uncompressedIndexStream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(uncompressedIndexStream);
            foreach (ArchiveEntry entry in _entries.OrderBy(e => e.Id))
            {
                entry.Write(writer);
            }

            uncompressedIndexStream.TryGetBuffer(out ArraySegment<byte> uncompressedIndexData);
            BcdCompression.Compress(uncompressedIndexData, _indexStream);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            WriteIndex();

            if (!_leaveOpen)
            {
                foreach (Stream contentStream in _contentStreams)
                {
                    contentStream.Dispose();
                }

                _indexStream?.Dispose();
            }
            _contentStreams.Clear();
            _disposed = true;
        }
    }
}
