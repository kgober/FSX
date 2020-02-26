// TeleDisk.cs
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


// TeleDisk file format information:
// http://www.bitsavers.org/pdf/sydex/Teledisk_1.05_Sep88.pdf
// http://www.classiccmp.org/dunfield/img54306/td0notes.txt


// Future Improvements / To Do
// check other CRCs (currently only header CRC is verified)
// allow source to be taken as a pathname if data is null
// support multi-file images (.TD1, etc.)


using System;
using System.Text;

namespace FSX
{
    class TeleDisk
    {
        public static Boolean HasHeader(Byte[] data)
        {
            if (data.Length < 12) return false;
            if (!((data[0] == 't') && (data[1] == 'd')) && !((data[0] == 'T') && (data[1] == 'D'))) return false;
            if ((data[5] & 0x03) == 3) return false;
            if ((data[5] & 0x7c) != 0) return false;
            if ((data[7] & 0x03) == 3) return false;
            if ((data[7] & 0x7c) != 0) return false;
            Int32 n = Buffer.GetUInt16L(data, 10);
            Int32 k = CRC.CRC16(0xa097, data, 0, 10);
            if (n != k) return Debug.WriteLine(false, 9, "TeleDisk.HasHeader: CRC mismatch (stored={0:x4} calculated={1:x4})", n, k);
            return true;
        }

        public static CHSVolume Load(String source, Byte[] data)
        {
            // header
            if (!HasHeader(data)) return null;
            Int32 p = 0;
            Byte b = data[p++];
            Byte b2 = data[p++];
            Boolean adc = false;
            if ((b == (Byte)'t') && (b2 == (Byte)'d')) adc = true;
            else if ((b != (Byte)'T') || (b2 != (Byte)'D'))
            {
                Debug.WriteLine(1, "File does not appear to be TeleDisk format (no TD header)");
                return null;
            }
            if ((b = data[p++]) != 0)
            {
                Debug.WriteLine(1, "Multi-file images not supported (volume sequence is {0:D0}, expect 0)", b);
                return null;
            }
            p++; // skip check signature byte
            p++; // skip version number byte
            p++; // skip source density byte
            p++; // skip drive type byte
            Int32 stepping = data[p++];
            p++; // skip dos mode byte
            Int32 nh = data[p++]; // number of sides
            p += 2; // header CRC
            if (adc)
            {
                LZSS.Decompressor D = new LZSS.Decompressor(data, p);
                data = D.GetBytes();
                p = 0;
            }

            // comment block
            Int32 n;
            String info = source;
            if ((stepping & 0x80) != 0)
            {
                if (p + 10 >= data.Length) return null;
                p += 2; // CRC
                n = Buffer.GetUInt16L(data, ref p);
                p += 6; // date/time
                StringBuilder buf = new StringBuilder(n);
                for (Int32 i = 0; i < n; i++)
                {
                    b = Buffer.GetByte(data, ref p);
                    buf.Append((b == 0) ? '\n' : (Char)b);
                }
                info = buf.ToString();
            }

            // determine disk geometry
            Int32 q = p;
            Int32 nc = -1;
            Int32[] ct = new Int32[256];
            while (p < data.Length)
            {
                // track header
                Int32 ns = Buffer.GetByte(data, ref p); // number of sectors on this track
                if (ns == 255) break;
                Int32 c = Buffer.GetByte(data, ref p) + 1;
                if (c > nc) nc = c;
                Int32 h = Buffer.GetByte(data, ref p) + 1;
                if (h > nh) nh = h;
                p++; // CRC

                // sectors
                for (Int32 s = 0; s < ns; s++)
                {
                    // sector header
                    p++; // skip cylinder id
                    p++; // skip side id
                    p++; // skip sector id
                    b = Buffer.GetByte(data, ref p); // sector size
                    ct[b]++;
                    Byte f = Buffer.GetByte(data, ref p); // flags
                    p++; // skip CRC
                    if ((f & 0x30) != 0) continue;
                    n = Buffer.GetUInt16L(data, ref p);
                    p += n;
                }
            }
            Int32 ss = -1;
            n = 0;
            for (Int32 i = 0; i < 256; i++)
            {
                if (ct[i] > n)
                {
                    n = ct[i];
                    ss = 128 << i;
                }
            }
            CHSVolume vol = new CHSVolume(source, source, ss, nc, nh);

            // track data
            p = q;
            while (p < data.Length)
            {
                // track header
                Int32 ns = Buffer.GetByte(data, ref p); // number of sectors on this track
                if (ns == 255) break;
                Int32 c = Buffer.GetByte(data, ref p);
                Int32 h = Buffer.GetByte(data, ref p);
                p++; // CRC
                Track T = new Track(ns);
                vol[c, h] = T;

                // sectors
                for (Int32 s = 0; s < ns; s++)
                {
                    // sector header
                    p++; // skip cylinder id
                    p++; // skip side id
                    Int32 id = Buffer.GetByte(data, ref p); // sector id
                    n = 128 << Buffer.GetByte(data, ref p); // sector size
                    Byte f = Buffer.GetByte(data, ref p); // flags
                    p++; // skip CRC
                    if ((f & 0x30) != 0) continue;
                    Int32 l = Buffer.GetUInt16L(data, ref p) - 1;
                    switch (Buffer.GetByte(data, ref p))
                    {
                        case 0:
                            T[s] = new Sector(id, n, data, p);
                            p += n;
                            break;
                        case 1:
                            T[s] = new Sector(id, n);
                            q = 0;
                            while (n > 0)
                            {
                                Int32 k = Buffer.GetUInt16L(data, ref p);
                                b = Buffer.GetByte(data, ref p);
                                b2 = Buffer.GetByte(data, ref p);
                                for (Int32 i = 0; i < k; i++)
                                {
                                    T[s][q++] = b;
                                    T[s][q++] = b2;
                                    n -= 2;
                                }
                            }
                            break;
                        case 2:
                            T[s] = new Sector(id, n);
                            q = 0;
                            while (n > 0)
                            {
                                Int32 k = Buffer.GetByte(data, ref p);
                                if (k == 0)
                                {
                                    k = Buffer.GetByte(data, ref p);
                                    T[s].CopyFrom(data, p, q, k);
                                    p += k;
                                    q += k;
                                    n -= k;
                                }
                                else
                                {
                                    k *= 2;
                                    Int32 z = Buffer.GetByte(data, ref p);
                                    for (Int32 i = 0; i < z; i++)
                                    {
                                        T[s].CopyFrom(data, p, q, k);
                                        q += k;
                                        n -= k;
                                    }
                                    p += k;
                                }
                            }
                            break;
                    }
                }
            }

            return vol;
        }
    }
}
