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

        public class Decompressor
        {
            private Byte[] mData;   // compressed data
            private Int32 mSize;    // uncompressed size
            private Byte[] mCache;  // uncompressed data

            public Decompressor(Byte[] data)
            {
                mData = data;
                mSize = -2;
            }

            public Int32 GetByteCount()
            {
                if (mSize != -2) return mSize;
                if (!HasHeader(mData)) return (mSize = -1);
                try
                {
                    GZipStream i = new GZipStream(new MemoryStream(mData), CompressionMode.Decompress);
                    MemoryStream o = new MemoryStream();
                    Byte[] buf = new Byte[4096];
                    Int32 n;
                    while ((n = i.Read(buf, 0, 4096)) != 0) o.Write(buf, 0, n);
                    mCache = o.ToArray();
                    return (mSize = mCache.Length);
                }
                catch
                {
                    return (mSize = -1);
                }
            }

            public Byte[] GetBytes()
            {
                Int32 n = GetByteCount();
                if (n == -1) return null;
                Byte[] buf = new Byte[n];
                if (n == 0) return buf;
                Buffer.Copy(mCache, 0, buf, 0, n);
                return buf;
            }
        }
    }
}
