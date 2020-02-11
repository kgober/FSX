// LZSS.cs
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


// LZSS-Huffman encoded data is recorded as a series of code words, where each
// code word is Adaptive Huffman encoded -- using a variable number of bits,
// with code words assigned so that no code is a prefix of another, and more
// frequent symbols get codes with fewer bits.  The assignment of codes to
// symbols is dynamic, and is updated after each symbol is decoded.
//
// If a decoded symbol is less than 256, it represents the corresponding byte.
// Otherwise the symbol represents the 'length' part of a (position,length) pair
// and the 'position' part immediately follows.  Pairs are only encoded if length
// is greater than 2, so a symbol value of 256 represents a length of 3.
//
// Positions are 12-bit numbers encoded using a variable-length (9-14 bit) code.
// The total number of encoded bits will be known after reading the first 4 bits:
//   0000   -> 0000000 followed by next 5 bits
//   0001   -> 0000001 followed by next 5 bits
//   0010   -> 000001 followed by next 6 bits
//   0011   -> 000010 followed by next 6 bits
//   0100   -> 000011 followed by next 6 bits
//   0101   -> 00010 followed by next 7 bits
//   0110   -> 00011 followed by next 7 bits
//   0111   -> 00100 followed by next 7 bits
//   1000   -> 00101 followed by next 7 bits
//   1001   -> 0011 followed by next 8 bits
//   1010   -> 0100 followed by next 8 bits
//   1011   -> 0101 followed by next 8 bits
//   1100   -> 011 followed by next 9 bits
//   1101   -> 100 followed by next 9 bits
//   1110   -> 101 followed by next 9 bits
//   1111   -> 11 followed by next 10 bits
//
// Code words are read most-significant-bit first; when a code word spans more than
// one byte, the most significant bits are in the first byte.


using System;

namespace FSX
{
    class LZSS
    {
        public class Decompressor
        {
            private struct Node
            {
                public Int32 F; // frequency
                public Int32 P; // parent node
                public Int32 L; // left child
                public Int32 R; // right child (or symbol, if L == -1)
            }

            private const Int32 N = 4096;                   // window size
            private const Int32 F = 60;                     // lookahead buffer size (and max length for pairs)
            private const Int32 N_SYM = 256 + F - 2;        // number of symbols (256 byte values, and lengths > 2)
            private const Int32 T = N_SYM + (N_SYM - 1);    // size of code tree (leaves + interior nodes)

            private Byte[] mData;   // compressed data
            private Int32 mOffset;  // where to start reading
            private Int32 mSize;    // uncompressed size
            private Node[] mTree;   // Huffman code tree
            private Int32[] mLeaf;  // leaf node for a symbol

            public Decompressor(Byte[] data, Int32 offset)
            {
                mData = data;
                mOffset = offset;
                mSize = -2;
            }

            public Int32 GetByteCount()
            {
                if (mSize != -2) return mSize;
                mTree = new Node[T];
                mLeaf = new Int32[N_SYM];
                InitTree();
                BitReaderB R = new BitReaderB(mData, mOffset);
                Int32 ct = 0;
                Int32 n;
                while ((n = GetSymbol(R)) != -1)
                {
                    UpdateNode(n);
                    if (n < 256)
                    {
                        ct++;
                    }
                    else
                    {
                        ct += n - (256 - 3);
                        if (GetPosition(R) == -1) return (mSize = -1);
                    }
                }
                return (mSize = ct);
            }

            public Byte[] GetBytes()
            {
                Int32 q = GetByteCount();
                if (q == -1) return null;
                Byte[] data = new Byte[q];
                q = 0;

                Byte[] buf = new Byte[N];
                Int32 p = N - F;
                for (Int32 i = 0; i < p; i++) buf[i] = 32;

                InitTree();
                BitReaderB R = new BitReaderB(mData, mOffset);
                Int32 n;
                while ((n = GetSymbol(R)) != -1)
                {
                    UpdateNode(n);
                    if (n < 256)
                    {
                        data[q++] = (Byte)n;
                        buf[p++] = (Byte)n;
                        if (p >= buf.Length) p = 0;
                    }
                    else
                    {
                        n -= (256 - 3);
                        Int32 m = GetPosition(R);
                        Int32 k = p - 1 - m;
                        if (k < 0) k += buf.Length;
                        for (Int32 i = 0; i < n; i++)
                        {
                            data[q++] = buf[k];
                            buf[p++] = buf[k++];
                            if (p >= buf.Length) p = 0;
                            if (k >= buf.Length) k = 0;
                        }
                    }
                }
                return data;
            }

            private Int32 GetSymbol(BitReaderB reader)
            {
                Int32 code = 0, len = 0;
                Int32 p = T - 1;            // start at root
                while (mTree[p].L != -1)
                {
                    Int32 bit = reader.Next(1);
                    if (bit == -1) return -1;
                    p = (bit == 0) ? mTree[p].L : mTree[p].R;
                    code <<= 1;
                    code += bit;
                    len++;
                }
                return mTree[p].R;
            }

            private Int32 GetPosition(BitReaderB reader)
            {
                Int32 pos = -1;
                Int32 i = reader.Next(4), j = 0, l = 0;
                switch (i)
                {
                    case 0: l = 5; j = reader.Next(l); pos = 0x000 | j; break;
                    case 1: l = 5; j = reader.Next(l); pos = 0x020 | j; break;
                    case 2: l = 6; j = reader.Next(l); pos = 0x040 | j; break;
                    case 3: l = 6; j = reader.Next(l); pos = 0x080 | j; break;
                    case 4: l = 6; j = reader.Next(l); pos = 0x0c0 | j; break;
                    case 5: l = 7; j = reader.Next(l); pos = 0x100 | j; break;
                    case 6: l = 7; j = reader.Next(l); pos = 0x180 | j; break;
                    case 7: l = 7; j = reader.Next(l); pos = 0x200 | j; break;
                    case 8: l = 7; j = reader.Next(l); pos = 0x280 | j; break;
                    case 9: l = 8; j = reader.Next(l); pos = 0x300 | j; break;
                    case 10: l = 8; j = reader.Next(l); pos = 0x400 | j; break;
                    case 11: l = 8; j = reader.Next(l); pos = 0x500 | j; break;
                    case 12: l = 9; j = reader.Next(l); pos = 0x600 | j; break;
                    case 13: l = 9; j = reader.Next(l); pos = 0x800 | j; break;
                    case 14: l = 9; j = reader.Next(l); pos = 0xa00 | j; break;
                    case 15: l = 10; j = reader.Next(l); pos = 0xc00 | j; break;
                }
                return pos;
            }

            private void InitTree()
            {
                // initialize leaf nodes
                Int32 i = 0, j = 0;
                while (i < N_SYM)
                {
                    mTree[i].F = 1;     // all leaves start at 1
                    mTree[i].L = -1;    // -1 indicates this is a leaf
                    mTree[i].R = i;     // symbol encoded by path to this leaf
                    mLeaf[i] = i;       // back link to make leaf easy to locate
                    i++;
                }

                // initialize interior nodes
                while (i < T)
                {
                    mTree[i].F = mTree[j].F;    // copy left child's frequency
                    mTree[i].L = j;             // left child
                    mTree[j++].P = i;           // update left child's parent
                    mTree[i].F += mTree[j].F;   // add right child's frequency
                    mTree[i].R = j;             // right child
                    mTree[j++].P = i;           // update right child's parent
                    i++;
                }

                // root has no parent
                mTree[--i].P = -1;
            }

            private void RebuildTree()
            {
                // collect leaf nodes into start of table, adjusting frequency F' = (F+1)/2
                Int32 j = 0; // j = next node to be filled
                for (Int32 i = 0; i < T; i++)
                {
                    if (mTree[i].L == -1)
                    {
                        mTree[j] = mTree[i];
                        mTree[j].F = (mTree[j].F + 1) / 2;
                        j++;
                    }
                }

                // rebuild interior nodes
                for (Int32 i = 0; j < T; )                  // on entry, j == N_SYM (first interior node)
                {
                    Int32 f = mTree[i].F + mTree[i + 1].F;  // f = sum of children frequencies
                    Int32 k = j;                            // k = where *this* node will be inserted
                    while (mTree[k - 1].F > f)              // find position k where new node belongs
                    {
                        mTree[k] = mTree[k - 1];            // making room as we go, moving nodes up
                        k--;
                    }
                    mTree[k].F = f;
                    mTree[k].L = i++;
                    mTree[k].R = i++;
                    j++;
                }

                // relink children to parents
                for (Int32 i = 0; i < T; i++)
                {
                    if ((j = mTree[i].L) != -1)
                    {
                        mTree[j].P = i;
                        j = mTree[i].R;
                        mTree[j].P = i;
                    }
                    else
                    {
                        mLeaf[mTree[i].R] = i;
                    }
                }

                // root has no parent
                mTree[T - 1].P = -1;
            }

            private void UpdateNode(Int32 symbol)
            {
                if (mTree[T - 1].F == 0x8000) RebuildTree();
                Int32 i = mLeaf[symbol];
                while (i != -1)
                {
                    Int32 f = mTree[i].F + 1;
                    Int32 j = i;
                    while ((++j < T) && (f > mTree[j].F)) ;
                    if (--j != i)
                    {
                        // this node needs to move to keep frequencies in order
                        mTree[i].F = mTree[j].F;
                        SwapChildren(i, j);
                        i = j;
                    }
                    mTree[i].F = f;
                    i = mTree[i].P;
                }
            }

            private void SwapChildren(Int32 x, Int32 y)
            {
                // swap children
                Int32 l = mTree[x].L;
                Int32 r = mTree[x].R;
                mTree[x].L = mTree[y].L;
                mTree[x].R = mTree[y].R;
                mTree[y].L = l;
                mTree[y].R = r;

                // reparent children of x
                if (mTree[x].L == -1)
                {
                    // relink symbol to leaf
                    mLeaf[mTree[x].R] = x;
                }
                else
                {
                    // reparent children to x
                    mTree[mTree[x].L].P = x;
                    mTree[mTree[x].R].P = x;
                }

                // reparent children of y
                if (mTree[y].L == -1)
                {
                    // relink symbol to leaf
                    mLeaf[mTree[y].R] = y;
                }
                else
                {
                    // reparent children to y
                    mTree[mTree[y].L].P = y;
                    mTree[mTree[y].R].P = y;
                }
            }
        }
    }
}
