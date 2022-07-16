using System.IO;

namespace CSystemArc
{
    internal class ArchiveEntry
    {
        public int Version
        {
            get;
            set;
        }

        public int Index
        {
            get;
            set;
        }

        public int ContentArchiveIndex
        {
            get;
            set;
        }

        public int Id
        {
            get;
            set;
        }

        public int Offset
        {
            get;
            set;
        }

        public int UncompressedSize
        {
            get;
            set;
        }

        public int CompressedSize
        {
            get;
            set;
        }

        public char Type
        {
            get;
            set;
        }

        public char SubType
        {
            get;
            set;
        }

        public void Read(BinaryReader reader)
        {
            Version = reader.ReadInt32();
            Id = reader.ReadInt32();
            UncompressedSize = reader.ReadInt32();
            CompressedSize = reader.ReadInt32();
            Offset = reader.ReadInt32();
            Type = (char)reader.ReadByte();
            SubType = (char)reader.ReadByte();
            reader.ReadInt32();
            if (Version > 0x16)
                ContentArchiveIndex = reader.ReadByte();
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Version);
            writer.Write(Id);
            writer.Write(UncompressedSize);
            writer.Write(CompressedSize);
            writer.Write(Offset);
            writer.Write((byte)Type);
            writer.Write((byte)SubType);
            writer.Write(-1);
            if (Version > 0x16)
                writer.Write((byte)ContentArchiveIndex);
        }

        public override string ToString()
        {
            return $"{Id}:{ContentArchiveIndex} (type {Type}{SubType})";
        }
    }
}
