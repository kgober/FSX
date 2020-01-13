// Utility.cs
// Copyright © 2019-2020 Kenneth Gober
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.


using System;

namespace FSX
{
    // Buffer class - utility functions to access data from byte arrays

    class Buffer
    {
        static public Int32 Copy(Byte[] sourceBuffer, Int32 sourceOffset, Byte[] targetBuffer, Int32 targetOffset, Int32 count)
        {
            Int32 n = sourceBuffer.Length - sourceOffset;
            if (count > n) count = n;
            n = targetBuffer.Length - targetOffset;
            if (count > n) count = n;
            for (Int32 i = 0; i < count; i++) targetBuffer[targetOffset++] = sourceBuffer[sourceOffset++];
            return count;
        }

        static public Byte GetByte(Byte[] buffer, Int32 offset)
        {
            return buffer[offset];
        }

        static public Byte GetByte(Byte[] buffer, ref Int32 offset)
        {
            return buffer[offset++];
        }

        static public Int16 GetInt16B(Byte[] buffer, Int32 offset)
        {
            Byte[] buf = (BitConverter.IsLittleEndian) ? Reverse(buffer, offset, 2) : buffer;
            return BitConverter.ToInt16(buf, offset);
        }

        static public Int16 GetInt16B(Byte[] buffer, ref Int32 offset)
        {
            Byte[] buf = (BitConverter.IsLittleEndian) ? Reverse(buffer, offset, 2) : buffer;
            Int16 n = BitConverter.ToInt16(buf, offset);
            offset += 2;
            return n;
        }

        static public Int16 GetInt16L(Byte[] buffer, Int32 offset)
        {
            Byte[] buf = (BitConverter.IsLittleEndian) ? buffer : Reverse(buffer, offset, 2);
            return BitConverter.ToInt16(buf, offset);
        }

        static public Int16 GetInt16L(Byte[] buffer, ref Int32 offset)
        {
            Byte[] buf = (BitConverter.IsLittleEndian) ? buffer : Reverse(buffer, offset, 2);
            Int16 n = BitConverter.ToInt16(buf, offset);
            offset += 2;
            return n;
        }

        static public UInt16 GetUInt16B(Byte[] buffer, Int32 offset)
        {
            Byte[] buf = (BitConverter.IsLittleEndian) ? Reverse(buffer, offset, 2) : buffer;
            return BitConverter.ToUInt16(buf, offset);
        }

        static public UInt16 GetUInt16B(Byte[] buffer, ref Int32 offset)
        {
            Byte[] buf = (BitConverter.IsLittleEndian) ? Reverse(buffer, offset, 2) : buffer;
            UInt16 n = BitConverter.ToUInt16(buf, offset);
            offset += 2;
            return n;
        }

        static public UInt16 GetUInt16L(Byte[] buffer, Int32 offset)
        {
            Byte[] buf = (BitConverter.IsLittleEndian) ? buffer : Reverse(buffer, offset, 2);
            return BitConverter.ToUInt16(buf, offset);
        }

        static public UInt16 GetUInt16L(Byte[] buffer, ref Int32 offset)
        {
            Byte[] buf = (BitConverter.IsLittleEndian) ? buffer : Reverse(buffer, offset, 2);
            UInt16 n = BitConverter.ToUInt16(buf, offset);
            offset += 2;
            return n;
        }

        static private Byte[] Reverse(Byte[] buffer, Int32 offset, Int32 count)
        {
            Byte[] buf = new Byte[count];
            for (Int32 i = count; i > 0; ) buf[--i] = buffer[offset++];
            return buf;
        }
    }

    // Debug class - utility functions to handle debug output

    class Debug
    {
        static public Int32 DebugLevel = 0;

        static public void WriteLine(Int32 messageLevel, String format, params Object[] args)
        {
            if (DebugLevel < messageLevel) return;
            Console.Error.WriteLine(format, args);
        }

        static public Boolean WriteLine(Boolean returnValue, Int32 messageLevel, String format, params Object[] args)
        {
            WriteLine(messageLevel, format, args);
            return returnValue;
        }
    }
}
