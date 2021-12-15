using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CSystemArc
{
    internal class ArchiveReader : IDisposable
    {
        private readonly IList<Stream> _contentStreams;
        private readonly bool _leaveOpen;

        private readonly Dictionary<char, List<ArchiveEntry>> _entries = new Dictionary<char, List<ArchiveEntry>>();

        public ArchiveReader(string indexFilePath, IList<string> contentFilePaths)
        {
            _contentStreams = new List<Stream>();
            foreach (string contentFilePath in contentFilePaths)
            {
                _contentStreams.Add(File.OpenRead(contentFilePath));
            }

            _leaveOpen = false;

            using Stream indexStream = File.OpenRead(indexFilePath);
            ReadEntries(indexStream);
        }

        public ArchiveReader(Stream indexStream, IList<Stream> contentStreams, bool leaveOpen = false)
        {
            _contentStreams = contentStreams;
            _leaveOpen = leaveOpen;

            ReadEntries(indexStream);
            if (!leaveOpen)
                indexStream.Dispose();
        }

        private void ReadEntries(Stream compressedIndexStream)
        {
            byte[] index = BcdCompression.Decompress(compressedIndexStream);

            using Stream indexStream = new MemoryStream(index);
            BinaryReader indexReader = new BinaryReader(indexStream);

            int prevId = -1;

            while (indexStream.Position < indexStream.Length)
            {
                ArchiveEntry entry = new ArchiveEntry();
                entry.Read(indexReader);

                List<ArchiveEntry> entriesOfType = _entries.FetchValue(entry.Type, () => new List<ArchiveEntry>());
                entry.Index = entriesOfType.Count;
                entriesOfType.Add(entry);

                //if (entry.Id <= prevId)
                //    throw new InvalidDataException("Entries not sorted by ID");

                prevId = entry.Id;
            }
        }

        public IEnumerable<ArchiveEntry> Entries => _entries.SelectMany(p => p.Value);

        public ArchiveEntry GetEntry(char type, int index)
        {
            return _entries[type][index];
        }

        public byte[] GetEntryContent(ArchiveEntry entry)
        {
            Stream contentStream = _contentStreams[entry.ContentArchiveIndex];
            contentStream.Position = entry.Offset;

            byte[] data = new byte[entry.UncompressedSize];
            if (entry.CompressedSize == entry.UncompressedSize)
            {
                contentStream.Read(data, 0, data.Length);
            }
            else
            {
                using LzssStream lzss = new LzssStream(contentStream, CompressionMode.Decompress, true);
                lzss.Read(data, 0, data.Length);
            }
            return data;
        }

        public void Dispose()
        {
            if (!_leaveOpen)
            {
                foreach (Stream contentStream in _contentStreams)
                {
                    contentStream.Dispose();
                }
            }
            _contentStreams.Clear();
        }
    }
}
