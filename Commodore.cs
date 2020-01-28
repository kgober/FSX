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


    // .D64 image file format (Commodore 4040/1540/1541)
    // (also recognizes .D67 files, in case they're misnamed)
    class D64
    {
        public static Boolean IsValid(Byte[] data)
        {
            if (data.Length == 174848) return true; // standard format
            if (data.Length == 175531) return true; // with error bytes
            if (data.Length == 196608) return true; // 40 tracks
            if (data.Length == 197376) return true; // 40 tracks with error bytes
            if (data.Length == 205312) return true; // 42 tracks
            if (data.Length == 206114) return true; // 42 tracks with error bytes
            if (data.Length == 176640) return true; // .D67 format
            if (data.Length == 177330) return true; // .D67 format with error bytes
            return false;
        }

        public static CHSVolume Load(String source, Byte[] data)
        {
            if (!IsValid(data)) return null;
            Int32 z1 = 21, z2 = 19, z3 = 18, z4 = 17;
            Int32 nt = 35;
            if ((data.Length == 196608) || (data.Length == 197376)) nt = 40;
            else if ((data.Length == 205312) || (data.Length == 206114)) nt = 42;
            else if ((data.Length == 176640) || (data.Length == 177330)) z2 = 20;
            CHSVolume V = new CHSVolume(source, source, 256, 1, nt, 0, 1, 0);
            for (Int32 t = 1; t <= 17; t++) V[t, 0] = new Track(z1);
            for (Int32 t = 18; t <= 24; t++) V[t, 0] = new Track(z2);
            for (Int32 t = 25; t <= 30; t++) V[t, 0] = new Track(z3);
            for (Int32 t = 31; t <= nt; t++) V[t, 0] = new Track(z4);
            Int32 p = 0;
            Int32 q = (data.Length == V.BlockCount * 257) ? V.BlockCount * 256 : -1;
            for (Int32 t = 1; t <= nt; t++)
            {
                Track T = V[t, 0];
                for (Int32 s = 0; s < T.Length; s++)
                {
                    Sector S = new Sector(s, 256, data, p);
                    p += 256;
                    if (q != -1) S.ErrorCode = data[q++];
                    T[s] = S;
                }
            }
            return V;
        }
    }

    // .D67 image file format (Commodore 2040/3040 with DOS 1 ROMs)
    class D67
    {
        public static Boolean IsValid(Byte[] data)
        {
            if (data.Length == 176640) return true; // standard format
            if (data.Length == 177330) return true; // with error bytes
            return false;
        }

        public static CHSVolume Load(String source, Byte[] data)
        {
            return D64.Load(source, data);
        }
    }

    // .D80 image file format (Commodore 8050)
    class D80
    {
        public static Boolean IsValid(Byte[] data)
        {
            if (data.Length == 533248) return true; // standard format
            if (data.Length == 535331) return true; // with error bytes
            return false;
        }

        public static CHSVolume Load(String source, Byte[] data)
        {
            if (!IsValid(data)) return null;
            Int32 z1 = 29, z2 = 27, z3 = 25, z4 = 23;
            Int32 nt = 77;
            CHSVolume V = new CHSVolume(source, source, 256, 1, nt, 0, 1, 0);
            for (Int32 t = 1; t <= 39; t++) V[t, 0] = new Track(z1);
            for (Int32 t = 40; t <= 53; t++) V[t, 0] = new Track(z2);
            for (Int32 t = 54; t <= 64; t++) V[t, 0] = new Track(z3);
            for (Int32 t = 65; t <= nt; t++) V[t, 0] = new Track(z4);
            Int32 p = 0;
            Int32 q = (data.Length == V.BlockCount * 257) ? V.BlockCount * 256 : -1;
            for (Int32 t = 1; t <= nt; t++)
            {
                Track T = V[t, 0];
                for (Int32 s = 0; s < T.Length; s++)
                {
                    Sector S = new Sector(s, 256, data, p);
                    p += 256;
                    if (q != -1) S.ErrorCode = data[q++];
                    T[s] = S;
                }
            }
            return V;
        }
    }

    // .D82 image file format (Commodore 8250)
    class D82
    {
        public static Boolean IsValid(Byte[] data)
        {
            if (data.Length == 1066496) return true; // standard format
            if (data.Length == 1070662) return true; // with error bytes
            return false;
        }

        public static CHSVolume Load(String source, Byte[] data)
        {
            if (!IsValid(data)) return null;
            Int32 z1 = 29, z2 = 27, z3 = 25, z4 = 23;
            Int32 nt = 154;
            CHSVolume V = new CHSVolume(source, source, 256, 1, nt, 0, 1, 0);
            for (Int32 t = 1; t <= 39; t++) V[t, 0] = new Track(z1);
            for (Int32 t = 40; t <= 53; t++) V[t, 0] = new Track(z2);
            for (Int32 t = 54; t <= 64; t++) V[t, 0] = new Track(z3);
            for (Int32 t = 65; t <= 77; t++) V[t, 0] = new Track(z4);
            for (Int32 t = 78; t <= 116; t++) V[t, 0] = new Track(z1);
            for (Int32 t = 117; t <= 130; t++) V[t, 0] = new Track(z2);
            for (Int32 t = 131; t <= 141; t++) V[t, 0] = new Track(z3);
            for (Int32 t = 142; t <= nt; t++) V[t, 0] = new Track(z4);
            Int32 p = 0;
            Int32 q = (data.Length == V.BlockCount * 257) ? V.BlockCount * 256 : -1;
            for (Int32 t = 1; t <= nt; t++)
            {
                Track T = V[t, 0];
                for (Int32 s = 0; s < T.Length; s++)
                {
                    Sector S = new Sector(s, 256, data, p);
                    p += 256;
                    if (q != -1) S.ErrorCode = data[q++];
                    T[s] = S;
                }
            }
            return V;
        }
    }
}
