// Pack.cs
// Copyright © 2020 Kenneth Gober
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


// Old Pack (.p) file format:
//   header:
//     0 magic = 0x1f
//     1 magic = 0x1f
//     2 ulen = uncompressed data size (32-bit PDP-11 F format float)
//         if value < 1.0, treat ulen as a 32-bit int
//     6 tlen = Huffman tree size (16-bit PDP-11 int)
//   compressed Huffman tree
//     tlen entries, each entry is either 1 byte or 3 bytes
//       values 0..254 are 1 byte
//       values 255..65535 are 3 bytes: 0xFF followed by a 16-bit PDP-11 int
//   compressed data (Huffman-coded bit stream)
//     ulen variable-bit-length codes
//     bits are stored most-significant-bit first, in PDP-11 16-bit format, so
//       bytes must be read at least 2 at a time (i.e. first bit is the '128'
//       bit of the second byte, sixteenth bit is the '1' bit of the first byte).
//
// Huffman tree format
// The tree is stored as an array, where each node is a pair of entries n and n+1.
// Nodes are ordered so they appear before either of their children.  The root is
// stored in entries 0 and 1.  For interior nodes, the values of each entry are
// taken as positive array offsets from the current node to the child node.  The
// first entry is the offset to the left child and the second entry is the offset
// to the right child.  Since each node is 2 entries the offsets are always even
// numbers.  For leaf nodes, the first entry is 0 and the second entry is the
// symbol encoded by the path to the node from the root, where a left branch
// represents a 0 bit and a right branch represents a 1 bit.


// Future Improvements / To Do
// add support for new Pack (.z) file format


using System;

namespace FSX
{
    class Pack
    {
        public static Boolean HasHeader(Byte[] data)
        {
            if (data.Length < 8) return false;
            if (data[0] != 0x1f) return false;
            if (data[1] != 0x1f) return false;
            return true;
        }

        public class Decompressor
        {
            private Byte[] mData;   // compressed data
            private Int32 mSize;    // uncompressed size
            private UInt16[] mTree; // Huffman code tree
            private Int32 mPtr;     // start of data
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
                Int32 ulen = Buffer.GetInt32P(mData, 2);
                Single f = BitConverter.ToSingle(BitConverter.GetBytes(ulen), 0) / 4.0F; // convert PDP-11 F to IEEE Single
                Debug.WriteLine(Debug.Level.Diag, "Pack.GetByteCount: PDP-11 F 0x{0:X8} -> {1:R}", ulen, f);
                if (f >= 1.0) ulen = (Int32)f;
                if (!GetCodeTree()) return (mSize = -1);
                if (!TestCodes(ulen)) return (mSize = -1);
                return (mSize = ulen);
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

            private Boolean GetCodeTree()
            {
                if (!HasHeader(mData)) return false;
                Int32 p = 6;
                Int32 n = Buffer.GetUInt16L(mData, ref p);
                UInt16[] T = new UInt16[n];
                for (Int32 i = 0; i < n; i += 2)
                {
                    if (p >= mData.Length) return false;
                    UInt16 w1 = Buffer.GetByte(mData, ref p);
                    if (w1 == 255)
                    {
                        if (p + 1 >= mData.Length) return false;
                        w1 = Buffer.GetUInt16L(mData, ref p);
                    }
                    if (p >= mData.Length) return false;
                    UInt16 w2 = Buffer.GetByte(mData, ref p);
                    if (w2 == 255)
                    {
                        if (p + 1 >= mData.Length) return false;
                        w2 = Buffer.GetUInt16L(mData, ref p);
                    }
                    if ((w1 == 0) && (w2 > 255)) return false;
                    if ((w1 != 0) && ((i + w1) >= n)) return false;
                    if ((w1 != 0) && ((i + w2) >= n)) return false;
                    T[i] = w1;
                    T[i + 1] = w2;
                }
                mTree = T;
                mPtr = p;
                Debug.WriteLine(Debug.Level.Diag, "Pack.GetCodeTree: unpacked {0:D0} code tree bytes, data begins at offset 0x{1:X4}", n, p);
                return true;
            }

            private Boolean TestCodes(Int32 outputLength)
            {
                Byte[] buf = new Byte[outputLength];
                Int32 p = mPtr; // next byte to read
                Int32 b = 0; // bit buffer
                Int32 m = 0; // bit buffer mask
                for (Int32 i = 0; i < outputLength; i++)
                {
                    Int32 n = 0;
                    Int32 cs = 0, cl = 0;
                    do
                    {
                        if (m == 0)
                        {
                            // refill bit buffer
                            if (p + 1 >= mData.Length) return false;
                            b = Buffer.GetUInt16L(mData, ref p);
                            m = 0x8000;
                        }
                        n += mTree[((b & m) != 0) ? n + 1 : n];
                        cs <<= 1;
                        if ((b & m) != 0) cs |= 1;
                        cl++;
                        m >>= 1;
                    } while (mTree[n] != 0);
                    buf[i] = (Byte)mTree[n + 1];
                }
                Debug.WriteLine(Debug.Level.Diag, "Pack.TestCodes: output bytes: {0:D0}, residual input bytes: {1:D0}", outputLength, mData.Length - p);
                if (p != mData.Length) return false;
                mCache = buf;
                return true;
            }
        }
    }
}
