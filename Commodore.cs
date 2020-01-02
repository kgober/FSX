// Commodore.cs
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
using System.Text;

namespace FSX
{
    // PETSCII graphics character set (upper case and graphics)
    class PETSCII0 : Encoding
    {
        public static readonly PETSCII0 Encoding = new PETSCII0();

        protected String mName;
        protected Char[] mMap;

        public PETSCII0()
        {
            mName = "PETSCII-0";
            mMap = new Char[256];
            for (Int32 i = 0; i < mMap.Length; i++) mMap[i] = '\ufffd';
            for (Int32 i = 0; i <= 93; i++) mMap[i] = (Char)i;
            mMap[92] = '£'; // U+00a3 currency symbol pound
            mMap[94] = '↑'; // U+2191 up arrow
            mMap[95] = '←'; // U+2190 left arrow
            mMap[96] = '─'; // U+2500 box drawing horizontal
            mMap[97] = '♠'; // U+2660 card suit spade
            mMap[115] = '♥'; // U+2665 card suit heart
            mMap[120] = '♣'; // U+2663 card suit club
            mMap[122] = '♦'; // U+2666 card suit diamond
            mMap[123] = '┼'; // U+253c box drawing cross
            mMap[125] = '│'; // U+2502 box drawing vertical
            mMap[126] = 'π'; // U+03c0 greek lowercase pi
            mMap[141] = '\u2028'; // U+2028 line separator
            mMap[160] = '\u00a0'; // U+00a0 non-breaking space
            mMap[171] = '├'; // U+251c box drawing tee left
            mMap[173] = '└'; // U+2514 box drawing corner bottom left
            mMap[174] = '┐'; // U+2510 box drawing corner top right
            mMap[176] = '┌'; // U+250c box drawing corner top left
            mMap[177] = '┴'; // U+2534 box drawing tee bottom
            mMap[178] = '┬'; // U+252c box drawing tee top
            mMap[179] = '┤'; // U+2524 box drawing tee right
            mMap[189] = '┘'; // U+2518 box drawing corner bottom right
        }

        public override String EncodingName
        {
            get { return mName; }
        }

        public override Boolean IsSingleByte
        {
            get { return true; }
        }

        public override Int32 GetMaxByteCount(Int32 charCount)
        {
            return charCount;
        }

        public override Int32 GetByteCount(Char[] chars)
        {
            return chars.Length;
        }

        public override Int32 GetByteCount(Char[] chars, Int32 index, Int32 count)
        {
            return count;
        }

        public override Int32 GetByteCount(String s)
        {
            return s.Length;
        }

        public override Byte[] GetBytes(String s)
        {
            return GetBytes(s.ToCharArray());
        }

        public override Byte[] GetBytes(Char[] chars)
        {
            return GetBytes(chars, 0, chars.Length);
        }

        public override Byte[] GetBytes(Char[] chars, Int32 index, Int32 count)
        {
            Byte[] buf = new Byte[count];
            GetBytes(chars, index, count, buf, 0);
            return buf;
        }

        public override Int32 GetBytes(Char[] chars, Int32 charIndex, Int32 charCount, Byte[] bytes, Int32 byteIndex)
        {
            throw new Exception("The method or operation is not implemented.");
        }

        public override Int32 GetMaxCharCount(Int32 byteCount)
        {
            return byteCount;
        }

        public override Int32 GetCharCount(Byte[] bytes)
        {
            return bytes.Length;
        }

        public override Int32 GetCharCount(Byte[] bytes, Int32 index, Int32 count)
        {
            return count;
        }

        public override Char[] GetChars(Byte[] bytes)
        {
            return GetChars(bytes, 0, bytes.Length);
        }

        public override Char[] GetChars(Byte[] bytes, Int32 index, Int32 count)
        {
            Char[] buf = new Char[count];
            GetChars(bytes, index, count, buf, 0);
            return buf;
        }

        public override Int32 GetChars(Byte[] bytes, Int32 byteIndex, Int32 byteCount, Char[] chars, Int32 charIndex)
        {
            Int32 p = byteIndex;
            Int32 q = charIndex;
            for (Int32 i = 0; i < byteCount; i++) chars[q++] = mMap[bytes[p++]];
            return (q - charIndex);
        }

        public override String GetString(Byte[] bytes)
        {
            return GetString(bytes, 0, bytes.Length);
        }

        public override String GetString(Byte[] bytes, Int32 byteIndex, Int32 byteCount)
        {
            return new String(GetChars(bytes, byteIndex, byteCount));
        }
    }


    // PETSCII text character set (lower case and upper case)
    class PETSCII1 : PETSCII0
    {
        public static new readonly PETSCII1 Encoding = new PETSCII1();

        public PETSCII1()
        {
            mName = "PETSCII-1";
            mMap = new Char[256];
            for (Int32 i = 0; i < mMap.Length; i++) mMap[i] = '\ufffd';
            for (Int32 i = 0; i <= 93; i++) mMap[i] = (Char)i;
            for (Int32 i = 65; i <= 90; i++) mMap[i] = (Char)(i + 32); // lower case
            for (Int32 i = 97; i <= 122; i++) mMap[i] = (Char)(i - 32); // upper case
            mMap[92] = '£'; // U+00a3 currency symbol pound
            mMap[94] = '↑'; // U+2191 up arrow
            mMap[95] = '←'; // U+2190 left arrow
            mMap[96] = '─'; // U+2500 box drawing horizontal
            mMap[123] = '┼'; // U+253c box drawing cross
            mMap[125] = '│'; // U+2502 box drawing vertical
            mMap[141] = '\u2028'; // U+2028 line separator
            mMap[160] = '\u00a0'; // U+00a0 non-breaking space
            mMap[171] = '├'; // U+251c box drawing tee left
            mMap[173] = '└'; // U+2514 box drawing corner bottom left
            mMap[174] = '┐'; // U+2510 box drawing corner top right
            mMap[176] = '┌'; // U+250c box drawing corner top left
            mMap[177] = '┴'; // U+2534 box drawing tee bottom
            mMap[178] = '┬'; // U+252c box drawing tee top
            mMap[179] = '┤'; // U+2524 box drawing tee right
            mMap[189] = '┘'; // U+2518 box drawing corner bottom right
        }
    }


    class Commodore
    {
        // load .D64/.D67 image file
        public static CHSVolume LoadD64(String source, Byte[] data)
        {
            CHSVolume d = null;
            Int32 z1 = 21, z2 = 19, z3 = 18, z4 = 17;
            Int32 c = -1;
            if (data.Length == 176640)
            {
                c = 35;
                z2 = 20;
            }
            else if ((data.Length == 174848) || (data.Length == 175531)) c = 35;
            else if ((data.Length == 196608) || (data.Length == 197376)) c = 40;
            else if ((data.Length == 205312) || (data.Length == 206114)) c = 42;
            if (c != -1)
            {
                d = new CHSVolume(source, 256, 1, c, 0, 1, 0);
                for (Int32 t = 1; t <= 17; t++) d[t, 0] = new Track(z1);
                for (Int32 t = 18; t <= 24; t++) d[t, 0] = new Track(z2);
                for (Int32 t = 25; t <= 30; t++) d[t, 0] = new Track(z3);
                for (Int32 t = 31; t <= c; t++) d[t, 0] = new Track(z4);
            }
            if (d != null)
            {
                Int32 p = 0;
                Int32 q = -1;
                if (data.Length == d.BlockCount * 257) q = d.BlockCount * 256;
                for (Int32 t = 1; t <= c; t++)
                {
                    Track T = d[t, 0];
                    for (Int32 s = 0; s < T.Length; s++)
                    {
                        Sector S = new Sector(s, 256, data, p);
                        p += 256;
                        if (q != -1) S.ErrorCode = data[q++];
                        T[s] = S;
                    }
                }
                return d;
            }
            return null;
        }


        // load .D80 image file
        public static CHSVolume LoadD80(String source, Byte[] data)
        {
            CHSVolume d = null;
            Int32 c = -1;
            if (data.Length == 533248) c = 77;
            if (c != -1)
            {
                d = new CHSVolume(source, 256, 1, c, 0, 1, 0);
                for (Int32 t = 1; t <= 39; t++) d[t, 0] = new Track(29);
                for (Int32 t = 40; t <= 53; t++) d[t, 0] = new Track(27);
                for (Int32 t = 54; t <= 64; t++) d[t, 0] = new Track(25);
                for (Int32 t = 65; t <= c; t++) d[t, 0] = new Track(23);
            }
            if (d != null)
            {
                Int32 p = 0;
                Int32 q = -1;
                if (data.Length == d.BlockCount * 257) q = d.BlockCount * 256;
                for (Int32 t = 1; t <= c; t++)
                {
                    Track T = d[t, 0];
                    for (Int32 s = 0; s < T.Length; s++)
                    {
                        Sector S = new Sector(s, 256, data, p);
                        p += 256;
                        if (q != -1) S.ErrorCode = data[q++];
                        T[s] = S;
                    }
                }
                return d;
            }
            return null;
        }


        // load .D82 image file
        public static CHSVolume LoadD82(String source, Byte[] data)
        {
            CHSVolume d = null;
            Int32 c = -1;
            if (data.Length == 1066496) c = 154; // physically 77 with 2 sides
            if (c != -1)
            {
                d = new CHSVolume(source, 256, 1, c, 0, 1, 0);
                for (Int32 t = 1; t <= 39; t++) d[t, 0] = new Track(29);
                for (Int32 t = 40; t <= 53; t++) d[t, 0] = new Track(27);
                for (Int32 t = 54; t <= 64; t++) d[t, 0] = new Track(25);
                for (Int32 t = 65; t <= 77; t++) d[t, 0] = new Track(23);
                for (Int32 t = 78; t <= 116; t++) d[t, 0] = new Track(29);
                for (Int32 t = 117; t <= 130; t++) d[t, 0] = new Track(27);
                for (Int32 t = 131; t <= 141; t++) d[t, 0] = new Track(25);
                for (Int32 t = 142; t <= c; t++) d[t, 0] = new Track(23);
            }
            if (d != null)
            {
                Int32 p = 0;
                Int32 q = -1;
                if (data.Length == d.BlockCount * 257) q = d.BlockCount * 256;
                for (Int32 t = 1; t <= c; t++)
                {
                    Track T = d[t, 0];
                    for (Int32 s = 0; s < T.Length; s++)
                    {
                        Sector S = new Sector(s, 256, data, p);
                        p += 256;
                        if (q != -1) S.ErrorCode = data[q++];
                        T[s] = S;
                    }
                }
                return d;
            }
            return null;
        }
    }
}
