using System;
using System.IO;
using System.IO.Compression;

namespace CSystemArc
{
    internal static class BcdCompression
    {
        public static byte[] Decompress(Stream stream)
        {
            int uncompressedSize = Bcd.Read(stream);
            int compressedSize = Bcd.Read(stream);

            long offset = stream.Position;

            byte[] decompressed = new byte[uncompressedSize];
            using LzssStream lzss = new LzssStream(stream, CompressionMode.Decompress, true);
            lzss.Read(decompressed, 0, uncompressedSize);

            stream.Position = offset + compressedSize;
            return decompressed;
        }

        public static void Compress(byte[] uncompressedData, Stream stream)
        {
            Compress(new ArraySegment<byte>(uncompressedData), stream);
        }

        public static void Compress(ArraySegment<byte> uncompressedData, Stream stream)
        {
            using MemoryStream compressedStream = new MemoryStream();
            using LzssStream lzss = new LzssStream(compressedStream, CompressionMode.Compress);
            lzss.Write(uncompressedData.Array, uncompressedData.Offset, uncompressedData.Count);

            Bcd.Write(stream, uncompressedData.Count);
            Bcd.Write(stream, (int)compressedStream.Length);
            compressedStream.TryGetBuffer(out ArraySegment<byte> compressedData);
            stream.Write(compressedData.Array, compressedData.Offset, compressedData.Count);
        }
    }
}
