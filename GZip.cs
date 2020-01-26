// GZip.cs
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
using System.IO;
using System.IO.Compression;

namespace FSX
{
    class GZip
    {
        public static Boolean HasHeader(Byte[] data)
        {
            if (data.Length < 18) return false; // minimum file size
            if (data[0] != 0x1f) return false; // ID1
            if (data[1] != 0x8b) return false; // ID2
            if (data[2] != 8) return false; // CM
            if ((data[3] & 0xe0) != 0) return false; // FLG
            return true;
        }

        public static Byte[] Decompress(Byte[] data)
        {
            DateTime t1 = DateTime.Now;
            GZipStream i = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            MemoryStream o = new MemoryStream();
            Byte[] buf = new Byte[4096];
            Int32 n;
            while ((n = i.Read(buf, 0, 4096)) != 0) o.Write(buf, 0, n);
            buf = o.ToArray();
            DateTime t2 = DateTime.Now;
            TimeSpan td = t2 - t1;
            Debug.WriteLine(9, "GZip.Decompress: {0:D0} -> {1:D0} bytes in {2:F3} ms", data.Length, buf.Length, td.TotalMilliseconds);
            return buf;
        }
    }
}
