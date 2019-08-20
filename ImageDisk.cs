// ImageDisk.cs
// Copyright © 2019 Kenneth Gober
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
    partial class CHSDisk
    {
        // Load ImageDisk .IMD image file
        public static CHSDisk LoadIMD(String source, Byte[] data)
        {
            Int32 p, n;
            Int32[] SS = new Int32[0];

            // determine disk geometry
            Int32 nc = -1;
            Int32 nh = -1;
            Int32 ss = -1;
            for (p = 0; data[p++] != 0x1a; ) ; // skip ASCII header
            while (p < data.Length)
            {
                // track header
                p++; // skip mode
                Int32 c = data[p++]; // cylinder num
                if (c > nc) nc = c;
                Int32 h = data[p++];  // head num
                Boolean cm = ((h & 0x80) != 0);
                Boolean hm = ((h & 0x40) != 0);
                h &= 0x3f;
                if (h > nh) nh = h;
                Int32 ns = data[p++]; // sectors
                ss = data[p++]; // sector size
                ss = (ss == 0xff) ? -1 : (128 << ss);
                p += ns; // skip sector numbering map
                if (cm) p += ns; // skip cylinder map
                if (hm) p += ns; // skip head map
                SS = new Int32[ns];
                if (ss == -1) // sector size table
                {
                    for (Int32 i = 0; i < ns; i++)
                    {
                        n = data[p++];
                        SS[i] = n + (data[p++] << 8);
                    }
                }
                // sector data
                for (Int32 s = 0; s < ns; s++)
                {
                    switch (data[p++]) // sector data type
                    {
                        case 1:
                        case 3:
                        case 5:
                        case 7:
                            p += (ss == -1) ? SS[s] : ss;
                            break;
                        case 2:
                        case 4:
                        case 6:
                        case 8:
                            p++;
                            break;
                    }
                }
            }

            // read image (use last track's largest sector as default volume sector size)
            if (ss == -1) for (Int32 i = 0; i < SS.Length; i++) if (SS[i] > ss) ss = SS[i];
            CHSDisk image = new CHSDisk(source, ss, ++nc, ++nh);
            for (p = 0; data[p++] != 0x1a; ) ; // skip ASCII header
            while (p < data.Length)
            {
                // track header
                p++; // skip mode
                Int32 c = data[p++]; // cylinder num
                Int32 h = data[p++];  // head num
                Boolean cm = ((h & 0x80) != 0);
                Boolean hm = ((h & 0x40) != 0);
                h &= 0x3f;
                Int32 ns = data[p++]; // sectors
                Track t = new Track(ns);
                ss = data[p++]; // sector size
                ss = (ss == 0xff) ? -1 : (128 << ss);
                Int32[] SM = new Int32[ns];
                for (Int32 i = 0; i < ns; i++) SM[i] = data[p++]; // sector numbering map
                // TODO: don't skip these
                if (cm) p += ns; // skip cylinder map
                if (hm) p += ns; // skip head map
                SS = new Int32[ns];
                if (ss == -1) // sector size table
                {
                    for (Int32 i = 0; i < ns; i++)
                    {
                        n = data[p++];
                        SS[i] = n + (data[p++] << 8);
                    }
                }
                // sector data
                for (Int32 s = 0; s < ns; s++)
                {
                    switch (data[p++]) // sector data type
                    {
                        case 1:
                        case 3:
                        case 5:
                        case 7:
                            n = (ss == -1) ? SS[s] : ss;
                            t.Set(s, new Sector(SM[s], data, p, n));
                            p += n;
                            break;
                        case 2:
                        case 4:
                        case 6:
                        case 8:
                            n = (ss == -1) ? SS[s] : ss;
                            t.Set(s, new Sector(SM[s], ss, data[p++]));
                            break;
                    }
                }
                image.mData[c, h] = t;
            }

            return image;
        }
    }
}
