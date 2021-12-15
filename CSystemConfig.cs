using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace CSystemArc
{
    internal class CSystemConfig
    {
        private static readonly Encoding SjisEncoding = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        private List<byte[]> _items;
        private byte[] _data1;
        private byte[] _data2;

        public void Read(Stream stream)
        {
            byte[] stringData = BcdCompression.Decompress(stream);
            _items = UnpackItems(stringData);

            int data1Size = Bcd.Read(stream);
            _data1 = new byte[data1Size];
            stream.Read(_data1, 0, _data1.Length);

            int data2Size = Bcd.Read(stream);
            _data2 = new byte[data2Size];
            stream.Read(_data2, 0, _data2.Length);

            if (stream.Position != stream.Length)
                throw new InvalidDataException();
        }

        public void Write(Stream stream)
        {
            ArraySegment<byte> stringData = PackItems(_items);
            BcdCompression.Compress(stringData, stream);

            Bcd.Write(stream, _data1.Length);
            stream.Write(_data1, 0, _data1.Length);

            Bcd.Write(stream, _data2.Length);
            stream.Write(_data2, 0, _data2.Length);
        }

        public XDocument ToXml()
        {
            if (_items.Count != 8)
                throw new InvalidDataException("Unexpected number of items");

            XElement configElem =
                new XElement(
                    "config",
                    TextItemToXml(_items[0]),
                    DictionaryItemToXml(_items[1]),
                    TextItemToXml(_items[2]),
                    TextItemToXml(_items[3]),
                    TextItemToXml(_items[4]),
                    TextItemToXml(_items[5]),
                    BinaryItemToXml(_items[6]),
                    BinaryItemToXml(_items[7])
                );

            XElement data1Elem =
                new XElement(
                    "data1",
                    BytesToHex(_data1)
                );

            XElement data2Elem =
                new XElement(
                    "data2",
                    BytesToHex(_data2)
                );

            return new XDocument(
                new XElement(
                    "csystem",
                    configElem,
                    data1Elem,
                    data2Elem
                )
            );
        }

        public void FromXml(XDocument doc)
        {
            XElement root = doc.Root;
            if (root.Name != "csystem")
                throw new InvalidDataException("Invalid root element name");

            XElement configElem = root.Element("config");
            if (configElem == null)
                throw new InvalidDataException("<config> element missing");

            _items = new List<byte[]>();
            foreach (XElement itemElem in configElem.Elements("item"))
            {
                _items.Add(ItemFromXml(itemElem));
            }

            XElement data1Elem = root.Element("data1");
            if (data1Elem == null)
                throw new InvalidDataException("<data1> element is missing");

            _data1 = HexToBytes(data1Elem.Value);

            XElement data2Elem = root.Element("data2");
            if (data2Elem == null)
                throw new InvalidDataException("<data2> element is missing");

            _data2 = HexToBytes(data2Elem.Value);
        }

        private static List<byte[]> UnpackItems(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            BinaryReader reader = new BinaryReader(stream);
            List<byte[]> items = new List<byte[]>();
            while (stream.Position < stream.Length)
            {
                int length = reader.ReadInt32();
                byte[] item = reader.ReadBytes(length);

                if (item.Length > 0 && item[0] == (byte)'S')
                {
                    byte[] xorlessItem = new byte[item.Length - 4];
                    xorlessItem[0] = item[0];
                    Array.Copy(item, 5, xorlessItem, 1, item.Length - 5);
                    item = xorlessItem;
                }

                if (item.Length >= 2 && item[item.Length - 2] == 0xFE && item[item.Length - 1] == 0xA)
                {
                    byte[] trimmedItem = new byte[item.Length - 2];
                    Array.Copy(item, trimmedItem, trimmedItem.Length);
                    item = trimmedItem;
                }

                items.Add(item);
            }
            return items;
        }

        private static ArraySegment<byte> PackItems(List<byte[]> items)
        {
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);
            foreach (byte[] item in items)
            {
                byte[] itemToWrite = item;
                if (item[0] == (byte)'S')
                {
                    byte[] xoredItem = new byte[item.Length + 4];
                    xoredItem[0] = item[0];
                    Array.Copy(item, 1, xoredItem, 5, item.Length - 1);
                    itemToWrite = xoredItem;
                }

                writer.Write(itemToWrite.Length);
                writer.Write(itemToWrite);
            }

            stream.TryGetBuffer(out ArraySegment<byte> data);
            return data;
        }

        private static XElement BinaryItemToXml(byte[] item)
        {
            return new XElement(
                "item",
                new XAttribute("type", "binary"),
                BytesToHex(item)
            );
        }

        private static XElement TextItemToXml(byte[] item)
        {
            string text = SjisEncoding.GetString(item);
            return new XElement(
                "item",
                new XAttribute("type", "text"),
                text
            );
        }

        private static XElement DictionaryItemToXml(byte[] item)
        {
            XElement dictElem =
                new XElement(
                    "item",
                    new XAttribute("type", "dict")
                );

            if ((char)item[0] != '#')
                throw new InvalidDataException();

            int offset = 1;
            while (offset < item.Length)
            {
                string key = null;
                while (true)
                {
                    char c = (char)item[offset++];
                    if (c == ':')
                        break;

                    key += c;
                }

                int valueOffset = offset;
                int value = 0;
                while (true)
                {
                    byte b = item[offset++];
                    if (b == 201)
                        break;

                    if (b <= 200)
                        value += b;
                    else if (b == 250)
                        value = 0;
                }

                if (offset == valueOffset + 1)
                    value = -1;

                dictElem.Add(new XElement("entry", new XAttribute("key", key), value.ToString()));
            }

            return dictElem;
        }

        private static byte[] ItemFromXml(XElement elem)
        {
            switch (elem.Attribute("type")?.Value)
            {
                case "binary":
                    return BinaryItemFromXml(elem);

                case "text":
                    return TextItemFromXml(elem);

                case "dict":
                    return DictionaryItemFromXml(elem);

                default:
                    throw new InvalidDataException("Unrecognized type for <item>");
            }
        }

        private static byte[] BinaryItemFromXml(XElement elem)
        {
            return HexToBytes(elem.Value);
        }

        private static byte[] TextItemFromXml(XElement elem)
        {
            return SjisEncoding.GetBytes(elem.Value);
        }

        private static byte[] DictionaryItemFromXml(XElement elem)
        {
            MemoryStream stream = new MemoryStream();
            stream.WriteByte((byte)'#');

            foreach (XElement entry in elem.Elements("entry"))
            {
                string key = entry.Attribute("key").Value;
                foreach (char c in key)
                {
                    stream.WriteByte((byte)c);
                }

                stream.WriteByte((byte)':');

                int value = int.Parse(entry.Value);
                if (value < -1)
                {
                    throw new InvalidDataException("Dictionary values can't be less than -1");
                }
                else if (value == -1)
                {
                }
                else if (value == 0)
                {
                    stream.WriteByte(250);
                }
                else
                {
                    while (value > 200)
                    {
                        stream.WriteByte(200);
                        value -= 200;
                    }
                    stream.WriteByte((byte)value);
                }

                stream.WriteByte(201);
            }

            byte[] item = new byte[stream.Length];
            stream.Position = 0;
            stream.Read(item, 0, item.Length);
            return item;
        }

        private static string BytesToHex(byte[] bytes)
        {
            StringBuilder hex = new StringBuilder();
            foreach (byte b in bytes)
            {
                hex.AppendFormat("{0:X02} ", b);
            }

            if (hex.Length > 0)
                hex.Length--;

            return hex.ToString();
        }

        private static byte[] HexToBytes(string hex)
        {
            hex = hex.Replace(" ", "");
            if (hex.Length % 2 != 0)
                throw new InvalidDataException("Hex string must have an even number of digits");

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber);
            }
            return bytes;
        }
    }
}
