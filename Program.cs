using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CSystemArc
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            try
            {
                switch (args[0])
                {
                    case "unpack":
                        if (args.Length < 4)
                        {
                            PrintUsage();
                            return;
                        }

                        Unpack(args[1], args.Skip(2).Take(args.Length - 3).ToList(), args.Last());
                        break;

                    case "pack":
                        if (args.Length < 5)
                        {
                            PrintUsage();
                            return;
                        }

                        if (!int.TryParse(args[1], out int version))
                        {
                            Console.WriteLine($"{args[1]} is not a valid version number");
                            break;
                        }

                        Pack(version, args[2], args[3], args.Skip(4).ToList());
                        break;

                    case "readconfig":
                        if (args.Length != 3)
                        {
                            PrintUsage();
                            return;
                        }

                        ReadConfig(args[1], args[2]);
                        break;

                    case "writeconfig":
                        if (args.Length != 3)
                        {
                            PrintUsage();
                            return;
                        }

                        WriteConfig(args[1], args[2]);
                        break;

                    default:
                        PrintUsage();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        private static void Unpack(string indexFilePath, IList<string> contentFilePaths, string folderPath)
        {
            VerifyFileExists(indexFilePath);
            foreach (string contentFilePath in contentFilePaths)
            {
                VerifyFileExists(contentFilePath);
            }
            VerifyFolderExists(folderPath);

            using ArchiveReader reader = new ArchiveReader(indexFilePath, contentFilePaths);
            IEnumerable<ArchiveEntry> entries = reader.Entries;
            List<ArchiveEntry> failedEntries;
            bool versionPrinted = false;
            do
            {
                failedEntries = new List<ArchiveEntry>();
                foreach (ArchiveEntry entry in entries)
                {
                    if (!versionPrinted)
                    {
                        Console.WriteLine($"Archive version: {entry.Version}");
                        versionPrinted = true;
                    }

                    if (!TryUnpackEntry(reader, entry, folderPath))
                        failedEntries.Add(entry);
                }
                entries = failedEntries;
            } while (failedEntries.Count > 0);
        }

        private static bool TryUnpackEntry(ArchiveReader reader, ArchiveEntry entry, string folderPath)
        {
            Console.WriteLine($"Unpacking {entry.Id:d06} (type {entry.Type}{entry.SubType})");
            byte[] content = reader.GetEntryContent(entry);
            switch (entry.Type)
            {
                case 'b':
                    return TryUnpackImage(reader, entry, content, folderPath);

                default:
                    UnpackRaw(entry, content, folderPath);
                    return true;
            }
        }

        private static bool TryUnpackImage(ArchiveReader reader, ArchiveEntry entry, byte[] content, string folderPath)
        {
            CSystemImage image = new CSystemImage();
            image.Read(new MemoryStream(content));

            string fileName = GetImageFileName(entry.Id);
            if (image.BaseIndex == -1 || image.BaseIndex == entry.Index)
            {
                image.SaveAsStandardImage(Path.Combine(folderPath, fileName));
                return true;
            }

            ArchiveEntry baseEntry = reader.GetEntry(entry.Type, image.BaseIndex);
            string baseFolderPath = Path.Combine(folderPath, GetImageFolderName(baseEntry.Id));
            Directory.CreateDirectory(baseFolderPath);

            string baseFileName = GetImageFileName(baseEntry.Id);
            if (File.Exists(Path.Combine(folderPath, baseFileName)))
                File.Move(Path.Combine(folderPath, baseFileName), Path.Combine(baseFolderPath, baseFileName));

            if (!File.Exists(Path.Combine(baseFolderPath, baseFileName)))
                return false;

            CSystemImage baseImage = new CSystemImage();
            baseImage.LoadStandardImageAsCSystem(Path.Combine(baseFolderPath, baseFileName));
            image.ConvertDeltaToFull(baseImage);
            image.SaveAsStandardImage(Path.Combine(baseFolderPath, fileName));
            return true;
        }

        private static void UnpackRaw(ArchiveEntry entry, byte[] content, string folderPath)
        {
            string filePath = Path.Combine(folderPath, GetRawFileName(entry.Id, entry.Type, entry.SubType));
            File.WriteAllBytes(filePath, content);
        }

        private static void Pack(int version, string folderPath, string indexFilePath, IList<string> contentFilePaths)
        {
            VerifyFolderExists(folderPath);

            using ArchiveWriter writer = new ArchiveWriter(version, indexFilePath, contentFilePaths);
            PackRawFiles(folderPath, writer);
            PackImages(folderPath, writer);
        }

        private static void PackRawFiles(string folderPath, ArchiveWriter writer)
        {
            foreach (string filePath in Directory.EnumerateFiles(folderPath))
            {
                if (!TryParseRawFileName(Path.GetFileName(filePath), out int id, out char type, out char subType))
                    continue;

                Console.WriteLine($"Packing {id:d06} (type {type}{subType})");
                byte[] content = File.ReadAllBytes(filePath);
                writer.Write(id, type, subType, content);
            }
        }

        private static void PackImages(string folderPath, ArchiveWriter writer)
        {
            List<int> rootIds = new List<int>();
            rootIds.AddRange(
                Directory.EnumerateFiles(folderPath, "*.png")
                         .Select(f => int.Parse(Path.GetFileNameWithoutExtension(f)))
            );
            rootIds.AddRange(
                Directory.EnumerateDirectories(folderPath)
                         .Select(f => int.Parse(Path.GetFileName(f)))
            );
            rootIds.Sort();

            int index = 0;

            foreach (int id in rootIds)
            {
                Console.WriteLine($"Packing {id:d04} (type b)");

                string deltaFolderPath = Path.Combine(folderPath, GetImageFolderName(id));
                if (Directory.Exists(deltaFolderPath))
                    PackDeltaImages(ref index, deltaFolderPath, writer);
                else
                    PackFullImage(ref index, id, Path.Combine(folderPath, GetImageFileName(id)), writer);
            }
        }

        private static void PackDeltaImages(ref int index, string folderPath, ArchiveWriter writer)
        {
            int baseIndex = 0;
            CSystemImage baseImage = null;
            foreach (string filePath in Directory.EnumerateFiles(folderPath, "*.png"))
            {
                int id = int.Parse(Path.GetFileNameWithoutExtension(filePath));

                if (baseImage == null)
                {
                    baseIndex = index;
                    baseImage = new CSystemImage();
                    baseImage.LoadStandardImageAsCSystem(filePath);

                    PackFullImage(ref index, id, filePath, writer);
                }
                else
                {
                    CSystemImage deltaImage = new CSystemImage(baseIndex);
                    deltaImage.LoadStandardImageAsCSystem(filePath);
                    deltaImage.ConvertFullToDelta(baseImage);
                    PackImage(ref index, id, deltaImage, writer);
                }
            }
        }

        private static void PackFullImage(ref int index, int id, string filePath, ArchiveWriter writer)
        {
            CSystemImage image = new CSystemImage(index);
            image.LoadStandardImageAsWrapper(filePath);
            PackImage(ref index, id, image, writer);
        }

        private static void PackImage(ref int index, int id, CSystemImage image, ArchiveWriter writer)
        {
            MemoryStream contentStream = new MemoryStream();
            image.Write(contentStream);

            contentStream.TryGetBuffer(out ArraySegment<byte> content);
            writer.Write(id, 'b', '0', content);

            index++;
        }

        private static string GetRawFileName(int id, char type, char subType)
        {
            return $"{id:d06}.{type}{subType}";
        }

        private static bool TryParseRawFileName(string fileName, out int id, out char type, out char subType)
        {
            Match match = Regex.Match(fileName, @"^(\d+)\.(\w)(\w)?$");
            if (!match.Success)
            {
                id = 0;
                type = '\0';
                subType = '\0';
                return false;
            }

            id = int.Parse(match.Groups[1].Value);
            type = match.Groups[2].Value[0];
            subType = match.Groups[3].Success ? match.Groups[3].Value[0] : '0';
            return true;
        }

        private static string GetImageFolderName(int id)
        {
            return $"{id:d06}";
        }

        private static string GetImageFileName(int id)
        {
            return $"{id:d06}.png";
        }

        private static void ReadConfig(string datFilePath, string xmlFilePath)
        {
            VerifyFileExists(datFilePath);

            CSystemConfig config = new CSystemConfig();

            byte[] datContent = File.ReadAllBytes(datFilePath);
            config.Read(new MemoryStream(datContent));

            XDocument doc = config.ToXml();
            doc.Save(xmlFilePath);
        }

        private static void WriteConfig(string xmlFilePath, string datFilePath)
        {
            VerifyFileExists(xmlFilePath);

            CSystemConfig config = new CSystemConfig();
            XDocument doc = XDocument.Load(xmlFilePath);
            config.FromXml(doc);

            using Stream datStream = File.Open(datFilePath, FileMode.Create, FileAccess.Write);
            config.Write(datStream);
        }

        private static void VerifyFileExists(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File {filePath} does not exist.");
        }

        private static void VerifyFolderExists(string folderPath)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Folder {folderPath} does not exist.");
        }

        private static void PrintUsage()
        {
            string assembly = Assembly.GetEntryAssembly().GetName().Name;
            Console.WriteLine("Usage:");
            Console.WriteLine($"  {assembly} unpack index.dat content1.dat content2.dat ... folder");
            Console.WriteLine($"  {assembly} pack version folder index.dat content1.dat content2.dat ...");
            Console.WriteLine($"  {assembly} readconfig config.dat config.xml");
            Console.WriteLine($"  {assembly} writeconfig config.xml config.dat");
        }
    }
}
