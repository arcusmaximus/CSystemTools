using System;
using System.IO;

namespace CSystemArc
{
    internal static class Bcd
    {
        public static int Read(Stream stream)
        {
            int value = 0;
            for (int i = 0; i < 8; i++)
            {
                value *= 10;
                byte b = (byte)stream.ReadByte();
                if (b != 0xFF)
                    value += b ^ 0x7F;
            }
            return value;
        }

        public static void Write(Stream stream, int value)
        {
            byte[] bcd = new byte[8];
            for (int i = 7; i >= 0; i--)
            {
                value = Math.DivRem(value, 10, out int remainder);
                bcd[i] = remainder != 0 ? (byte)(remainder ^ 0x7F) : (byte)0xFF;
            }
            stream.Write(bcd, 0, 8);
        }
    }
}
