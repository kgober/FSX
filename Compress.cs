// Compress.cs
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


// .Z compressed file format:
//   header: bytes 0-2
//     0 magic = 0x1f
//     1 magic = 0x9d
//     2 format
//         max_bits = format & 0x1f
//           max_bits is the maximum code size, starting code size is 9 bits.
//         block_mode = format & 0x80
//           in block mode, code 256 is used to clear the dictionary and reset code size to 9
//   compressed data: bytes 3+
//     a number of 9-byte blocks each containing 8 packed 9-bit LZW code words
//     a number of 10-byte blocks each containing 8 packed 10-bit LZW code words
//     a number of 11-byte blocks each containing 8 packed 11-bit LZW code words
//     ...
//     final block of code words ends at the earliest 1-byte boundary (there is no EOF code word)
//     when code size resets or changes, remaining code words in the current block are discarded.
//
// LZW compression/decompression algorithm:
//   https://marknelson.us/posts/1989/10/01/lzw-data-compression.html
//   https://marknelson.us/posts/2011/11/08/lzw-revisited.html
//
// Example: compression of ABABCCC
//   in             add     out     residual
//   +A -> A                        A
//   A+B -> AB      257     A       B
//   B+A -> BA      258     B       A
//   A+B -> AB                      AB (257)
//   AB+C -> ABC    259     257     C
//   C+C -> CC      260     C       C
//   C+C -> CC                      CC (260)
//   CC                     260
//
// Example: decompression of A B 257 C 260
//   in     out     add
//   A      A
//   B      B       257 = AB (A + B[0])
//   257    AB      258 = BA (B + AB[0])
//   C      C       259 = ABC (AB + C[0])
//   260    CC      260 = CC (C + C[0])


using System;

namespace FSX
{
    class Compress
    {
        public static Boolean HasHeader(Byte[] data)
        {
            if (data.Length < 3) return false;
            if (data[0] != 0x1f) return false;
            if (data[1] != 0x9d) return false;
            return true;
        }

        public class Decompressor
        {
            private Byte[] mData;           // compressed data
            private Int32 mSize;            // uncompressed size
            private Int32[] mLength;        // lengths of each dictionary entry
            private Int32[] mPrefixCode;    // prefix of each dictionary entry is another dictionary entry
            private Byte[] mSuffixByte;     // suffix of each dictionary entry is a byte to be appended to prefix

            public Decompressor(Byte[] data)
            {
                mData = data;
                mSize = -2;
            }

            public Int32 GetByteCount()
            {
                if (mSize != -2) return mSize;
                if (!HasHeader(mData)) return (mSize = -1);
                Int32 MAX_BITS = mData[2] & 0x1f;
                if (MAX_BITS > 24) return (mSize = -1); // current BitReader only handles up to 24-bit code words
                if ((mData[2] & 0x60) != 0) return (mSize = -1); // unsupported flag bits
                Boolean BLOCK_MODE = ((mData[2] & 0x80) != 0);

                Int32 n = 1 << MAX_BITS;
                mLength = new Int32[n];
                for (Int32 i = 0; i < 256; i++) mLength[i] = 1;
                Int32 block_offset = 3;
                BitReader R = new BitReader(mData, block_offset);
                Int32 code_size = 9;
                Int32 code_max = (1 << code_size) - 1;
                Int32 next_free = (BLOCK_MODE) ? 257 : 256;
                Int32 code = R.Next(code_size);
                if (code == -1) return (mSize = 0); // valid 0-byte output
                n = 1; // first code is always for 1 byte, so start count at 1
                Int32 prev_code = code;
                while ((code = R.Next(code_size)) != -1)
                {
                    if ((code == 256) && BLOCK_MODE)
                    {
                        // start reading a new block, with code size reset to 9
                        R = new BitReader(mData, block_offset = NextBlockOffset(block_offset, R.Offset, code_size));
                        code_max = (1 << (code_size = 9)) - 1;
                        next_free = 256; // use 256 instead of 257 so next code isn't (usably) added to dictionary
                        continue;
                    }
                    if (code > next_free) return (mSize = -1); // invalid or corrupt input
                    if (next_free <= code_max)
                    {
                        // new dictionary entries are always 1 byte longer than the previously received one.
                        mLength[next_free] = mLength[prev_code] + 1;
                        if ((next_free == code_max) && (code_size < MAX_BITS))
                        {
                            // start reading a new block, with code size increased by 1
                            R = new BitReader(mData, block_offset = NextBlockOffset(block_offset, R.Offset, code_size));
                            code_max = (1 << ++code_size) - 1;
                        }
                        next_free++;
                    }
                    n += mLength[code];
                    prev_code = code;
                }
                return (mSize = n);
            }

            public Byte[] GetBytes()
            {
                Int32 n = GetByteCount();
                if (n == -1) return null;
                Byte[] buf = new Byte[n];
                if (n == 0) return buf;

                Int32 MAX_BITS = mData[2] & 0x1f;
                Boolean BLOCK_MODE = ((mData[2] & 0x80) != 0);
                n = 1 << MAX_BITS;
                mPrefixCode = new Int32[n];
                mSuffixByte = new Byte[n];
                for (Int32 i = 0; i < 256; i++)
                {
                    mPrefixCode[i] = -1;
                    mSuffixByte[i] = (Byte)i;
                }

                Int32 block_offset = 3;
                BitReader R = new BitReader(mData, block_offset);
                Int32 code_size = 9;
                Int32 code_max = (1 << code_size) - 1;
                Int32 next_free = (BLOCK_MODE) ? 257 : 256;
                Int32 code = R.Next(code_size);
                n = 0;
                Byte b = Put(buf, ref n, code); // first code gets output without adding anything to dictionary
                Int32 prev_code = code;
                while ((code = R.Next(code_size)) != -1)
                {
                    if ((code == 256) && BLOCK_MODE)
                    {
                        // start reading a new block, with code size reset to 9
                        R = new BitReader(mData, block_offset = NextBlockOffset(block_offset, R.Offset, code_size));
                        code_max = (1 << (code_size = 9)) - 1;
                        next_free = 256; // use 256 instead of 257 so next code isn't (usably) added to dictionary
                        continue;
                    }
                    if (code == next_free)
                    {
                        // special case: the last byte of this code is the same as the first byte of the previous code
                        mLength[code] = mLength[prev_code] + 1;
                        mPrefixCode[code] = prev_code;
                        mSuffixByte[code] = b;
                    }
                    b = Put(buf, ref n, code);
                    if (next_free <= code_max)
                    {
                        // add a new code to dictionary for prev_code plus the first byte of current code
                        mLength[next_free] = mLength[prev_code] + 1;
                        mPrefixCode[next_free] = prev_code;
                        mSuffixByte[next_free] = b;
                        if ((next_free == code_max) && (code_size < MAX_BITS))
                        {
                            // start reading a new block, with code size increased by 1
                            R = new BitReader(mData, block_offset = NextBlockOffset(block_offset, R.Offset, code_size));
                            code_max = (1 << ++code_size) - 1;
                        }
                        next_free++;
                    }
                    prev_code = code;
                }
                return buf;
            }

            // write the bytes encoded by a given code word, and return the first byte
            private Byte Put(Byte[] buffer, ref Int32 offset, Int32 code)
            {
                offset += mLength[code];
                Int32 p = offset;
                while (code != -1)
                {
                    buffer[--p] = mSuffixByte[code];
                    code = mPrefixCode[code];
                }
                return buffer[p];
            }

            // find the starting offset of the next block of code words
            private Int32 NextBlockOffset(Int32 startOffset, Int32 currentOffset, Int32 blockSize)
            {
                Int32 n = (currentOffset - startOffset) % blockSize;
                return (n == 0) ? currentOffset : currentOffset + blockSize - n;
            }
        }

        private class BitReader
        {
            Byte[] mData;   // byte array to read from
            Int32 mPtr;     // index of next byte to read
            Int32 mBuf;     // bit buffer
            Int32 mBits;    // number of bits currently held in buffer

            public BitReader(Byte[] data)
            {
                mData = data;
            }

            public BitReader(Byte[] data, Int32 startOffset) : this(data)
            {
                mPtr = startOffset;
            }

            public Int32 Offset
            {
                get { return mPtr; }
            }

            public Int32 Next(Int32 bits)
            {
                while (mBits < bits)
                {
                    if (mPtr >= mData.Length) return -1;
                    mBuf |= mData[mPtr++] << mBits; // add bits at the left
                    mBits += 8;
                }
                Int32 n = mBuf & ((1 << bits) - 1); // remove bits from the right
                mBuf >>= bits;
                mBits -= bits;
                return n;
            }
        }
    }
}
