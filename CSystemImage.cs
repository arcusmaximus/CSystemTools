using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace CSystemArc
{
    internal class CSystemImage
    {
        private const byte AttrValueBase = 0xE9;
        private const byte AttrValueEndMarker = 0xEF;
        private const byte AttrValueZeroMarker = 0xFB;

        private byte[] _mask;
        private byte[] _alpha;
        private byte[] _color;
        private byte[] _standardImage;

        public CSystemImage()
        {
            BaseIndex = -1;
        }

        public CSystemImage(int baseIndex)
        {
            BaseIndex = baseIndex;
        }

        public int BaseIndex
        {
            get;
            private set;
        }

        public int Width
        {
            get;
            private set;
        }

        public int Height
        {
            get;
            private set;
        }

        public void Read(Stream stream)
        {
            BinaryReader reader = new BinaryReader(stream);

            char type = (char)reader.ReadByte();
            switch (type)
            {
                case 'a':       // Custom bitmap (full with alpha, or delta on top of other custom bitmap without alpha)
                case 'd':       // Custom bitmap delta on top of standard image (with alpha)
                    ReadCSystemImage(reader);
                    break;

                case 'c':       // Standard image
                    ReadStandardWrapperImage(reader);
                    break;

                default:
                    throw new NotSupportedException();
            }
        }

        private void ReadCSystemImage(BinaryReader reader)
        {
            reader.BaseStream.Position += 1;

            BaseIndex = ReadAttrValue(reader);
            ReadAttrValue(reader);      // field_C
            ReadAttrValue(reader);      // field_38
            Width = ReadAttrValue(reader);
            ReadAttrValue(reader);      // field_3C
            Height = ReadAttrValue(reader);
            ReadAttrValue(reader);      // field_18
            ReadAttrValue(reader);      // field_34
            ReadAttrValue(reader);      // field_1C
            int alphaSize = ReadAttrValue(reader);
            ReadAttrValue(reader);      // field_2C
            int colorSize = ReadAttrValue(reader);
            ReadAttrValue(reader);      // field_28
            ReadAttrValue(reader);      // flags - 1: has mask, 4: has alpha
            ReadAttrValue(reader);      // field_40
            ReadAttrValue(reader);      // field_44
            ReadAttrValue(reader);      // field_48
            int maskSize = ReadAttrValue(reader);
            ReadAttrValue(reader);      // field_4C
            ReadAttrValue(reader);      // field_8

            if (maskSize > 0)
                _mask = reader.ReadBytes(maskSize);

            if (alphaSize > 0)
                _alpha = reader.ReadBytes(alphaSize);

            if (colorSize > 0)
                _color = reader.ReadBytes(colorSize);
        }

        private void ReadStandardWrapperImage(BinaryReader reader)
        {
            int length = reader.ReadByte();
            length = (length << 8) | reader.ReadByte();
            length = (length << 8) | reader.ReadByte();
            length = (length << 8) | reader.ReadByte();
            _standardImage = reader.ReadBytes(length);
        }

        public void Write(Stream stream)
        {
            BinaryWriter writer = new BinaryWriter(stream);
            if (_standardImage != null)
                WriteStandardWrapperImage(writer);
            else
                WriteCSystemImage(writer);
        }

        private void WriteCSystemImage(BinaryWriter writer)
        {
            int flags = 1;
            if (_alpha != null)
                flags |= 6;

            writer.Write(_mask == null ? (byte)'a' : (byte)'d');
            writer.Write(AttrValueEndMarker);
            WriteAttrValue(writer, BaseIndex);
            WriteAttrValue(writer, 0);      // field_C
            WriteAttrValue(writer, 0);      // field_38
            WriteAttrValue(writer, Width);
            WriteAttrValue(writer, 0);      // field_3C
            WriteAttrValue(writer, Height);
            WriteAttrValue(writer, 0);      // field_18
            WriteAttrValue(writer, 0);      // field_34
            WriteAttrValue(writer, 0);      // field_1C
            WriteAttrValue(writer, _alpha?.Length ?? 0);
            WriteAttrValue(writer, 0);      // field_2C
            WriteAttrValue(writer, _color?.Length ?? 0);
            WriteAttrValue(writer, 0);      // field_28
            WriteAttrValue(writer, flags);
            WriteAttrValue(writer, 0);      // field_40
            WriteAttrValue(writer, 0);      // field_44
            WriteAttrValue(writer, 0);      // field_48
            WriteAttrValue(writer, _mask?.Length ?? 0);
            WriteAttrValue(writer, 0);      // field_4C
            WriteAttrValue(writer, 0);      // field_8

            if (_mask != null)
                writer.Write(_mask);

            if (_alpha != null)
                writer.Write(_alpha);

            if (_color != null)
                writer.Write(_color);
        }

        private void WriteStandardWrapperImage(BinaryWriter writer)
        {
            writer.Write((byte)'c');
            writer.Write((byte)(_standardImage.Length >> 24));
            writer.Write((byte)(_standardImage.Length >> 16));
            writer.Write((byte)(_standardImage.Length >> 8));
            writer.Write((byte)(_standardImage.Length));
            writer.Write(_standardImage);
        }

        public void ConvertDeltaToFull(CSystemImage baseImage)
        {
            if (_mask == null)
                throw new InvalidOperationException("Image is already a full image");

            if (baseImage._mask != null)
                throw new InvalidOperationException("Base image is not a full image");

            if (baseImage._standardImage != null)
                throw new InvalidOperationException("Base image must be a CSystem image");

            Width = baseImage.Width;
            Height = baseImage.Height;

            byte[] mergedColor = new byte[(Width * 3 + (Width & 3)) * Height];
            byte[] mergedAlpha = baseImage._alpha != null ? new byte[mergedColor.Length] : null;

            int fullOffset = 0;
            int maskOffset = 0;
            int maskBit = 1;
            int deltaColorOffset = 0;
            int deltaAlphaOffset = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if ((_mask[maskOffset] & maskBit) != 0)
                    {
                        mergedColor[fullOffset + 0] = _color[deltaColorOffset + 0];
                        mergedColor[fullOffset + 1] = _color[deltaColorOffset + 1];
                        mergedColor[fullOffset + 2] = _color[deltaColorOffset + 2];

                        if (mergedAlpha != null)
                        {
                            byte alpha = _alpha?[deltaAlphaOffset] ?? baseImage._alpha[fullOffset];
                            mergedAlpha[fullOffset + 0] = alpha;
                            mergedAlpha[fullOffset + 1] = alpha;
                            mergedAlpha[fullOffset + 2] = alpha;
                        }

                        deltaColorOffset += 3;
                        deltaAlphaOffset++;
                    }
                    else
                    {
                        mergedColor[fullOffset + 0] = baseImage._color[fullOffset + 0];
                        mergedColor[fullOffset + 1] = baseImage._color[fullOffset + 1];
                        mergedColor[fullOffset + 2] = baseImage._color[fullOffset + 2];

                        if (mergedAlpha != null)
                        {
                            mergedAlpha[fullOffset + 0] = baseImage._alpha[fullOffset + 0];
                            mergedAlpha[fullOffset + 1] = baseImage._alpha[fullOffset + 0];
                            mergedAlpha[fullOffset + 2] = baseImage._alpha[fullOffset + 0];
                        }
                    }

                    fullOffset += 3;

                    maskBit <<= 1;
                    if (maskBit == 0x100)
                    {
                        maskOffset++;
                        maskBit = 1;
                    }
                }
                fullOffset += Width & 3;
            }

            _mask = null;
            _color = mergedColor;
            _alpha = mergedAlpha;
        }

        public void ConvertFullToDelta(CSystemImage baseImage)
        {
            if (_mask != null)
                throw new InvalidOperationException("Image is already a delta image");

            if (baseImage._mask != null)
                throw new InvalidOperationException("Base image is not a full image");

            if (baseImage._standardImage != null)
                throw new InvalidOperationException("Base image must be a CSystem image");

            List<byte> newMask = new List<byte>();
            List<byte> newColor = new List<byte>();
            List<byte> newAlpha = new List<byte>();

            int fullOffset = 0;
            
            int maskBit = 1;
            byte maskByte = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (_color[fullOffset + 0] != baseImage._color[fullOffset + 0] ||
                        _color[fullOffset + 1] != baseImage._color[fullOffset + 1] ||
                        _color[fullOffset + 2] != baseImage._color[fullOffset + 2] ||
                        (_alpha?[fullOffset + 0] ?? 0xFF) != (baseImage._alpha?[fullOffset + 0] ?? 0xFF))
                    {
                        maskByte = (byte)(maskByte | maskBit);
                        newColor.Add(_color[fullOffset + 0]);
                        newColor.Add(_color[fullOffset + 1]);
                        newColor.Add(_color[fullOffset + 2]);
                        newAlpha.Add(_alpha?[fullOffset + 0] ?? 0xFF);
                    }

                    fullOffset += 3;

                    maskBit <<= 1;
                    if (maskBit == 0x100)
                    {
                        newMask.Add(maskByte);
                        maskByte = 0;
                        maskBit = 1;
                    }
                }

                fullOffset += Width & 3;
            }

            if (maskBit != 1)
                newMask.Add(maskByte);

            _mask = newMask.ToArray();
            _color = newColor.ToArray();
            _alpha = newAlpha.ToArray();
        }

        public unsafe void LoadStandardImageAsCSystem(string filePath)
        {
            using Image image = Image.FromFile(filePath);
            using Bitmap bitmap = new Bitmap(image);
            Width = bitmap.Width;
            Height = bitmap.Height;
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            _mask = null;
            _color = new byte[(Width * 3 + (Width & 3)) * Height];
            _alpha = new byte[(Width * 3 + (Width & 3)) * Height];
            _standardImage = null;

            bool alphaNeeded = false;

            byte* pInputRow = (byte*)data.Scan0;
            int outputOffset = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    _color[outputOffset + 0] = pInputRow[4 * x + 0];
                    _color[outputOffset + 1] = pInputRow[4 * x + 1];
                    _color[outputOffset + 2] = pInputRow[4 * x + 2];

                    _alpha[outputOffset + 0] = pInputRow[4 * x + 3];
                    _alpha[outputOffset + 1] = pInputRow[4 * x + 3];
                    _alpha[outputOffset + 2] = pInputRow[4 * x + 3];

                    if (_alpha[outputOffset + 0] != 0xFF)
                        alphaNeeded = true;

                    outputOffset += 3;
                }
                pInputRow += data.Stride;
                outputOffset += Width & 3;
            }

            if (!alphaNeeded)
                _alpha = null;

            bitmap.UnlockBits(data);
        }

        public void LoadStandardImageAsWrapper(string filePath)
        {
            _mask = null;
            _color = null;
            _alpha = null;
            _standardImage = File.ReadAllBytes(filePath);

            using Image image = Image.FromStream(new MemoryStream(_standardImage));
            Width = image.Width;
            Height = image.Height;
        }

        public unsafe void SaveAsStandardImage(string filePath)
        {
            if (_standardImage != null)
            {
                using Image nativeImage = Image.FromStream(new MemoryStream(_standardImage));
                nativeImage.Save(filePath);
                return;
            }

            if (_mask != null)
                throw new InvalidOperationException("Can't save delta images (apply base first)");

            byte[] argb = new byte[Width * 4 * Height];
            int inputOffset = 0;
            int outputOffset = 0;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    argb[outputOffset + 0] = _color[inputOffset + 0];
                    argb[outputOffset + 1] = _color[inputOffset + 1];
                    argb[outputOffset + 2] = _color[inputOffset + 2];
                    argb[outputOffset + 3] = _alpha?[inputOffset + 0] ?? 0xFF;
                    inputOffset += 3;
                    outputOffset += 4;
                }

                inputOffset += Width & 3;
            }

            fixed (byte* pArgb = argb)
            {
                using Image nativeImage = new Bitmap(Width, Height, 4 * Width, PixelFormat.Format32bppArgb, (IntPtr)pArgb);
                nativeImage.Save(filePath);
            }
        }

        private static int ReadAttrValue(BinaryReader reader)
        {
            byte b = reader.ReadByte();
            int units = b != AttrValueZeroMarker ? b : 0;

            int hundreds = 0;
            int tens = 0;
            while (true)
            {
                b = reader.ReadByte();
                if (b == AttrValueEndMarker)
                    break;

                if (b == AttrValueBase)
                    hundreds++;
                else
                    tens = b != AttrValueZeroMarker ? b : 0;
            }

            return (hundreds * AttrValueBase + tens) * AttrValueBase + units;
        }

        private static void WriteAttrValue(BinaryWriter writer, int value)
        {
            int hundreds = Math.DivRem(Math.DivRem(value, AttrValueBase, out int units), AttrValueBase, out int tens);
            writer.Write((byte)(units == 0 ? AttrValueZeroMarker : units));
            if (tens != 0)
                writer.Write((byte)tens);

            for (int i = 0; i < hundreds; i++)
            {
                writer.Write(AttrValueBase);
            }

            writer.Write(AttrValueEndMarker);
        }
    }
}
