// RT11.cs
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


// RT-11 File System Structure
//
// V01-15 http://www.bitsavers.org/www.computer.museum.uq.edu.au/RT-11/DEC-11-ORPGA-A-D%20RT-11%20Software%20Support%20Manual.pdf
// V02B http://www.bitsavers.org/www.computer.museum.uq.edu.au/RT-11/DEC-11-ORPGA-B-D%20RT-11%20Software%20Support%20Manual.pdf
// V02C http://www.bitsavers.org/www.computer.museum.uq.edu.au/RT-11/DEC-11-ORPGA-B-D,%20DN1%20RT-11%20Software%20Support%20Manual.pdf
// V03B http://www.bitsavers.org/www.computer.museum.uq.edu.au/RT-11/AA-5280B-TC%20RT-11%20Advanced%20Programmer's%20Guide.pdf
// V4.0 http://www.bitsavers.org/pdf/dec/pdp11/rt11/v4.0_Mar80/3b/AA-H379A-TC_RT-11_V4.0_Software_Support_Manual_Mar81.pdf
// V5.0 http://www.bitsavers.org/pdf/dec/pdp11/rt11/v5.0_Mar83/AA-H379B-TC_5.0_SWsuppMar83.pdf
// V5.1 http://www.bitsavers.org/www.computer.museum.uq.edu.au/RT-11/AA-H379B-TC%20RT-11%20Software%20Support%20Manual.pdf
// V5.6 http://www.bitsavers.org/pdf/dec/pdp11/rt11/v5.6_Aug91/AA-PD6PA-TC_RT-11_Volume_and_File_Formats_Manual_Aug91.pdf
// V5.7 http://www.bitsavers.org/pdf/dec/pdp11/rt11/v5.7_Oct98/AA-5286M-TC_RT-11_V5.7_Release_Notes_Oct98.pdf
//
// In RT-11 v1 the content of the home block was not documented, it was simply
// "Reserved for RSX-11D compatibility".  In RT-11 v3 the home block was populated
// with cluster size, directory start and version (V3A) words, as well as the
// 12-byte system, volume, and owner IDs.  The home block structure was documented
// in RT-11 v4 (albeit with an incorrect checksum algorithm).  RT-11 v4 also
// introduced the Protected flag for files.  In RT-11 v5.0, the home block version
// word changed to V05, and the size of the month field in date words was reduced
// from 5 to 4 bits, leaving 2 bits.  RT-11 v5.5 introduced the E.READ flag for
// files, and used the high 2 bits in date words to extend the representable year.
// Home block checksums were populated using the Files-11 algorithm.


// Future Improvements / To Do
// allow files to be written/deleted in images


using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FSX
{
    partial class RT11 : FileSystem
    {
        private const UInt16 defaultDirStart = 6;

        [Flags]
        private enum E : ushort
        {
            PRE = 0x0010,   // Prefix block indicator
            TENT = 0x0100,  // Tentative file
            MPTY = 0x0200,  // Empty area
            PERM = 0x0400,  // Permanent file
            EOS = 0x0800,   // End-of-segment marker
            READ = 0x4000,  // Protected by monitor from write operations
            PROT = 0x8000,  // Protected permanent file
        }

        class FileEntry
        {
            public String FileName;
            public Int32 StartBlock;
            public Int32 BlockCount;
            public DateTime CreationDate;

            public FileEntry(String fileName, Int32 startBlock, Int32 blockCount, DateTime creationDate)
            {
                FileName = fileName;
                StartBlock = startBlock;
                BlockCount = blockCount;
                CreationDate = creationDate;
            }
        }

        private static readonly String[] MONTHS = { null, "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        private Volume mVol;
        private Int32 mDirStart;
        private ClusteredVolume mDir;

        public RT11(Volume volume)
        {
            if (volume.BlockSize != 512) throw new ArgumentException("RT11 volume block size must be 512.");
            mVol = volume;
            mDirStart = IsChecksumOK(volume[1], 510) ? volume[1].GetUInt16L(0x1d4) : defaultDirStart;
            mDir = new ClusteredVolume(volume, 2, mDirStart - 2, 32);
        }

        public override String Source
        {
            get { return mVol.Source; }
        }

        public override String Type
        {
            get { return "RT11"; }
        }

        public override String Info
        {
            get { return mVol.Info; }
        }

        public override String Dir
        {
            get { return String.Empty; }
        }

        public override Encoding DefaultEncoding
        {
            get { return Encoding.ASCII; }
        }

        public override void ChangeDir(String dirSpec)
        {
        }

        public override void ListDir(String fileSpec, TextWriter output)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) fileSpec = "*.*";
            Regex RE = Regex(fileSpec);
            output.WriteLine(DateTime.Today.ToString(" dd-MMM-yyyy"));
            Block B = mVol[1];
            UInt16 w = B.GetUInt16L(0x1d6);
            if ((w < 64000) & IsASCIIText(B, 0x01d8, 36))
            {
                Byte[] buf = new Byte[36];
                B.CopyTo(buf, 0, 0x1d8, 36);
                output.WriteLine(" System ID: {0} {1}", Encoding.ASCII.GetString(buf, 24, 12), (w == 0x2020) ? null : Radix50.Convert(w));
                output.WriteLine(" Volume ID: {0}", Encoding.ASCII.GetString(buf, 0, 12));
                output.WriteLine(" Owner    : {0}", Encoding.ASCII.GetString(buf, 12, 12));
            }
            Boolean f = false;
            Int32 bct = 0;
            Int32 fct = 0;
            Int32 zct = 0;
            Int32 s = 1; // current directory segment number
            while (s != 0)
            {
                Block seg = mDir[s];
                Int32 eb = seg.GetUInt16L(6);
                Int32 bp = seg.GetUInt16L(8);
                Int32 sp = 10;
                E esw;
                while (((esw = (E)seg.GetUInt16L(sp)) & E.EOS) == 0)
                {
                    String fn1 = "---";
                    Radix50.TryConvert(seg.GetUInt16L(sp + 2), ref fn1);
                    String fn2 = "---";
                    Radix50.TryConvert(seg.GetUInt16L(sp + 4), ref fn2);
                    String ext = "---";
                    Radix50.TryConvert(seg.GetUInt16L(sp + 6), ref ext);
                    Int32 len = seg.GetUInt16L(sp + 8);
                    String cdt = "           ";
                    w = seg.GetUInt16L(sp + 12);
                    if ((w != 0) && ((esw & E.PERM) != 0))
                    {
                        Int32 y = ((w & 0xc000) >> 9) + (w & 0x001f) + 1972;
                        Int32 m = (w & 0x3c00) >> 10;
                        Int32 d = (w & 0x03e0) >> 5;
                        cdt = ((m >= 1) && (m <= 12) && (d >= 1) && (d <= 31)) ? String.Format("{0:D2}-{1}-{2:D4}", d, MONTHS[m], y) : "   -BAD-   ";
                    }
                    String fn = String.Concat(fn1, fn2, ".", ext);
                    if ((esw & E.PERM) != 0)
                    {
                        if (RE.IsMatch(fn))
                        {
                            fct++;
                            bct += len;
                        }
                        else
                        {
                            fn = null;
                        }
                    }
                    else
                    {
                        fn = "< UNUSED >";
                        zct += len;
                    }
                    if (fn != null)
                    {
                        if (f = !f) output.Write("{0} {1,5:D0}{2} {3}", fn, len, ((esw & (E.PERM | E.PROT)) == (E.PERM | E.PROT)) ? 'P' : ' ', cdt);
                        else output.WriteLine("    {0} {1,5:D0}{2} {3}", fn, len, ((esw & (E.PERM | E.PROT)) == (E.PERM | E.PROT)) ? 'P' : ' ', cdt);
                    }
                    bp += len;
                    sp += 14 + eb;
                }
                s = seg.GetUInt16L(2);
            }
            if (f) output.WriteLine();
            output.WriteLine(" {0:D0} Files, {1:D0} Blocks", fct, bct);
            output.WriteLine(" {0:D0} Free blocks", zct);
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) fileSpec = "*.*";
            Regex RE = Regex(fileSpec);
            Int32 bct = 0;
            Int32 fct = 0;
            Int32 zct = 0;
            Int32 s = 1; // current directory segment number
            while (s != 0)
            {
                Block B = mDir[s];
                Int32 eb = B.GetUInt16L(6);
                Int32 bp = B.GetUInt16L(8);
                Int32 sp = 10;
                UInt16 w;
                E esw;
                while (((esw = (E)B.GetUInt16L(sp)) & E.EOS) == 0)
                {
                    Char s1 = ((esw & E.PROT) == 0) ? '-' : 'P';
                    Char s2 = ((esw & E.READ) == 0) ? '-' : 'R';
                    Char s3 = ((esw & E.PERM) == 0) ? '-' : 'F';
                    Char s4 = ((esw & E.TENT) == 0) ? '-' : 'T';
                    Char s5 = ((esw & E.MPTY) == 0) ? '-' : 'E';
                    String fn1 = "---";
                    Radix50.TryConvert(B.GetUInt16L(sp + 2), ref fn1);
                    String fn2 = "---";
                    Radix50.TryConvert(B.GetUInt16L(sp + 4), ref fn2);
                    String ext = "---";
                    Radix50.TryConvert(B.GetUInt16L(sp + 6), ref ext);
                    Int32 len = B.GetUInt16L(sp + 8);
                    Int32 chj = B.GetUInt16L(sp + 10);
                    String cdt = "           ";
                    w = B.GetUInt16L(sp + 12);
                    if (w != 0)
                    {
                        Int32 y = ((w & 0xc000) >> 9) + (w & 0x001f) + 1972;
                        Int32 m = (w & 0x3c00) >> 10;
                        Int32 d = (w & 0x03e0) >> 5;
                        cdt = ((m >= 1) && (m <= 12) && (d >= 1) && (d <= 31)) ? String.Format("{0:D2}-{1}-{2:D4}", d, MONTHS[m], y) : "   -BAD-   ";
                    }
                    String fn = String.Concat(fn1, fn2, ".", ext);
                    if (!RE.IsMatch(fn)) fn = null;
                    if ((esw & E.PERM) != 0)
                    {
                        if (fn != null)
                        {
                            fct++;
                            bct += len;
                        }
                    }
                    else
                    {
                        zct += len;
                    }
                    if (fn != null) Console.Out.WriteLine("{0} {1,5:D0} @ {2,-5:D0} {3}{4}{5}{6}{7} {8}", fn, len, bp, s1, s2, s3, s4, s5, cdt);
                    bp += len;
                    sp += 14 + eb;
                }
                s = B.GetUInt16L(2);
            }
            Int32 ns = mDir[1].GetUInt16L(0); // number of directory segments
            output.WriteLine(" {0:D0} Files, {1:D0} Blocks, {2:D0} Free blocks", fct, bct, zct);
            output.WriteLine(" {0:D0} Total blocks ({1:D0} Reserved, {2:D0} Directory)", mDirStart + ns * 2 + bct + zct, mDirStart, ns * 2);
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
            Byte[] buf = ReadFile(fileSpec);
            Int32 p = buf.Length;
            for (Int32 i = 0; i < buf.Length; i++)
            {
                if (buf[i] == 26) // ^Z
                {
                    p = i;
                    break;
                }
            }
            output.Write(encoding.GetString(buf, 0, p));
        }

        public override void DumpFile(String fileSpec, TextWriter output)
        {
            Program.Dump(null, ReadFile(fileSpec), output, 16, 512, Program.DumpOptions.ASCII|Program.DumpOptions.Radix50);
        }

        public override String FullName(String fileSpec)
        {
            FileEntry f = FindFile(fileSpec);
            if (f == null) return null;
            return f.FileName;
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            FileEntry f = FindFile(fileSpec);
            if (f == null) return new Byte[0];
            Byte[] buf = new Byte[f.BlockCount * 512];
            Int32 p = 0;
            for (Int32 i = 0; i < f.BlockCount; i++)
            {
                mVol[f.StartBlock + i].CopyTo(buf, p);
                p += 512;
            }
            return buf;
        }

        public override Boolean SaveFS(String fileName, String format)
        {
            // RX01 and RX02 images should be written as physical images (including track 0)
            // all other images (including RX50) should be written as logical images
            FileStream f = new FileStream(fileName, FileMode.Create);
            Volume d = mVol;
            Int32 size;
            Type type;
            if (!Test(d, 3, out size, out type)) return false;
            if ((size == 494) || (size == 988)) // RX01 and RX02 sizes
            {
                Boolean iFlag = (d is InterleavedVolume);
                while (d.Base != null)
                {
                    d = d.Base;
                    if (d is InterleavedVolume) iFlag = true;
                }
                if (iFlag)
                {
                    // the base image is already in physical format
                    if (d.MaxCylinder == 75)
                    {
                        // base image lacks track 0
                        Byte[] buf = new Byte[d.BlockSize];
                        for (Int32 s = 0; s < 26; s++) f.Write(buf, 0, d.BlockSize);
                        for (Int32 t = 0; t < 76; t++)
                        {
                            for (Int32 s = 1; s <= 26; s++)
                            {
                                d[t, 0, s].CopyTo(buf, 0);
                                f.Write(buf, 0, d.BlockSize);
                            }
                        }
                    }
                    else
                    {
                        // base image includes track 0
                        Byte[] buf = new Byte[d.BlockSize];
                        for (Int32 t = 0; t < 77; t++)
                        {
                            for (Int32 s = 1; s <= 26; s++)
                            {
                                d[t, 0, s].CopyTo(buf, 0);
                                f.Write(buf, 0, d.BlockSize);
                            }
                        }
                    }
                }
                else
                {
                    // physical image must be created
                    Int32 SPB = 512 / d.BlockSize;
                    Int32[,] map = new Int32[76, 26];
                    for (Int32 lsn = 0; lsn < size * SPB; lsn++)
                    {
                        Int32 t = lsn / 26;
                        Int32 s = lsn % 26;
                        s *= 2; // 2:1 interleave
                        if (s >= 26) s++; // 2 interleave cycles per track
                        s += t * 6; // skew
                        s %= 26;
                        map[t, s] = lsn;
                    }
                    Byte[] buf = new Byte[d.BlockSize];
                    for (Int32 s = 0; s < 26; s++) f.Write(buf, 0, d.BlockSize);
                    for (Int32 t = 0; t < 76; t++)
                    {
                        for (Int32 s = 0; s < 26; s++)
                        {
                            Int32 lsn = map[t, s];
                            mVol[lsn / SPB].CopyTo(buf, 0, (lsn % SPB) * d.BlockSize, d.BlockSize);
                            f.Write(buf, 0, d.BlockSize);
                        }
                    }
                }
            }
            else
            {
                Byte[] buf = new Byte[512];
                for (Int32 i = 0; i < size; i++)
                {
                    d[i].CopyTo(buf, 0);
                    f.Write(buf, 0, 512);
                }
            }
            f.Close();
            return true;
        }

        private FileEntry FindFile(String fileSpec)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) return null;
            Int32 p = fileSpec.IndexOf('@');
            if (p != -1)
            {
                Int32 start, count;
                if (!Int32.TryParse(fileSpec.Substring(p + 1).Trim(), out start) || !Int32.TryParse(fileSpec.Substring(0, p).Trim(), out count)) return null;
                return new FileEntry(fileSpec, start, count, DateTime.Now);
            }
            p = fileSpec.IndexOf('-');
            if (p != -1)
            {
                Int32 first, last;
                if (!Int32.TryParse(fileSpec.Substring(p + 1).Trim(), out last) || !Int32.TryParse(fileSpec.Substring(0, p).Trim(), out first)) return null;
                return new FileEntry(fileSpec, first, last - first + 1, DateTime.Now);
            }

            Regex RE = Regex(fileSpec);
            Int32 s = 1;
            while (s != 0)
            {
                Block seg = mDir[s];
                Int32 eb = seg.GetUInt16L(6);
                Int32 bp = seg.GetUInt16L(8);
                Int32 sp = 10;
                E esw;
                UInt16 w;
                while (((esw = (E)seg.GetUInt16L(sp)) & E.EOS) == 0)
                {
                    Int32 len = seg.GetUInt16L(sp + 8);
                    if ((esw & E.PERM) != 0)
                    {
                        String fn1 = "---";
                        Radix50.TryConvert(seg.GetUInt16L(sp + 2), ref fn1);
                        String fn2 = "---";
                        Radix50.TryConvert(seg.GetUInt16L(sp + 4), ref fn2);
                        String ext = "---";
                        Radix50.TryConvert(seg.GetUInt16L(sp + 6), ref ext);
                        String fn = String.Concat(fn1, fn2, ".", ext);
                        if (RE.IsMatch(fn))
                        {
                            w = seg.GetUInt16L(sp + 12);
                            DateTime dt = DateTime.Now;
                            if (w != 0)
                            {
                                Int32 y = ((w & 0xc000) >> 9) + (w & 0x001f) + 1972;
                                Int32 m = (w & 0x3c00) >> 10;
                                Int32 d = (w & 0x03e0) >> 5;
                                if ((m >= 1) && (m <= 12) && (d >= 1) && (d <= 31)) dt = new DateTime(y, 1, 1).AddMonths(m - 1).AddDays(d - 1);
                            }
                            return new FileEntry(String.Concat(String.Concat(fn1, fn2).TrimEnd(' '), ".", ext.TrimEnd(' ')), bp, len, dt);
                        }
                    }
                    bp += len;
                    sp += 14 + eb;
                }
                s = seg.GetUInt16L(2);
            }
            return null;
        }
    }

    partial class RT11 : IFileSystemAuto
    {
        public static TestDelegate GetTest()
        {
            return RT11.Test;
        }

        // level 0 - check basic volume parameters (return required block size and volume type)
        // level 1 - check boot block (return volume size and type)
        // level 2 - check volume descriptor (aka home/super block) (return volume size and type)
        // level 3 - check directory structure (return volume size and type)
        // level 4 - check file headers (aka inodes) (return volume size and type)
        // level 5 - check file header allocation (return volume size and type)
        // level 6 - check data block allocation (return volume size and type)
        // note: levels 3 and 4 are reversed because this makes more sense for RT-11 volumes
        public static Boolean Test(Volume volume, Int32 level, out Int32 size, out Type type)
        {
            // level 0 - check basic volume parameters (return required block size and volume type)
            size = 512;
            type = typeof(Volume);
            if (volume == null) return false;
            if (volume.BlockSize != size) return Debug.WriteLine(false, 1, "RT11.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            if (level == 1)
            {
                size = -1;
                type = typeof(Volume);
                if (volume.BlockCount < 1) return Debug.WriteLine(false, 1, "RT11.Test: volume too small to contain boot block");
                return true;
            }

            // level 2 - check volume descriptor (aka home/super block) (return volume size and type)
            size = -1;
            type = null;
            if (volume.BlockCount < 2) return Debug.WriteLine(false, 1, "RT11.Test: volume too small to contain home block");
            type = typeof(RT11);
            if (level == 2) return true;

            // level 3 - check directory structure (return volume size and type)
            if (volume.BlockCount < 8) return Debug.WriteLine(false, 1, "RT11.Test: volume too small to contain directory segment {0:D0}", 1);
            ClusteredVolume Dir = new ClusteredVolume(volume, 2, 4, 32); // start at 4 so that segment 1 falls on block 6
            // check for problems with segment chain structure and count segments in use
            Int32[] SS = new Int32[32]; // segments seen (to detect cycles)
            Int32 sc = 0; // segment count
            Int32 s = 1;
            while (s != 0)
            {
                if (SS[s] != 0) return Debug.WriteLine(false, 1, "RT11.Test: invalid directory segment chain: segment {0:D0} repeated", s);
                SS[s] = ++sc;
                s = Dir[s].GetUInt16L(2); // next directory segment
                if (s > 31) return Debug.WriteLine(false, 1, "RT11.Test: invalid directory segment chain: segment {0:D0} invalid", s);
                if (s >= Dir.BlockCount) return Debug.WriteLine(false, 1, "RT11.Test: volume too small to contain directory segment {0:D0}", s);
            }
            // check directory segment consistency (and calculate volume size)
            Int32 ns = -1; // total directory segments
            Int32 eb = -1; // extra bytes per directory entry
            Int32 md = -1; // maximum value of data block pointer
            s = 1;
            while (s != 0)
            {
                Block D = Dir[s];
                Int32 n = D.GetUInt16L(0); // total directory segments
                if ((ns == -1) && (n >= sc) && (n < 32)) ns = n;
                else if ((s == 1) || ((s != 1) && (n != ns) && (n != 0))) return Debug.WriteLine(false, 1, "RT11.Test: inconsistent directory segment count in segment {0:D0} (is {1:D0}, expect {2:D0}{3})", s, n, sc, (sc == 31) ? null : " <= n <= 31");
                n = D.GetUInt16L(4); // highest segment in use
                if ((s == 1) && (n != sc)) return Debug.WriteLine(false, 1, "RT11.Test: inconsistent highest-segment-used pointer in segment {0:D0} (is {1:D0}, expect {2:D0})", s, n, sc);
                n = D.GetUInt16L(6); // extra bytes per directory entry
                if ((eb == -1) && ((n % 2) == 0)) eb = n;
                else if (n != eb) return Debug.WriteLine(false, 1, "RT11.Test: inconsistent or invalid extra bytes value in segment {0:D0} (is {1:D0}, require {2})", s, n, (eb == -1) ? "even number" : eb.ToString("D0"));
                Int32 bp = D.GetUInt16L(8); // starting data block for this segment
                if (bp < 6 + ns * 2) return Debug.WriteLine(false, 1, "RT11.Test: invalid start-of-data pointer in segment {0:D0} (is {1:D0}, require n >= {2:D0})", s, bp, 6 + ns * 2);
                Int32 sp = 10;
                E esw;
                while (((esw = (E)D.GetUInt16L(sp)) & E.EOS) == 0) // entry status word
                {
                    n = D.GetUInt16L(sp + 8); // file length (in blocks)
                    bp += n;
                    if (sp + 14 + eb > 1022) return Debug.WriteLine(false, 1, "RT11.Test: missing end-of-segment marker in segment {0:D0}", s);
                    sp += 14 + eb;
                }
                if (bp > md) md = bp;
                s = D.GetUInt16L(2); // next directory segment
            }
            size = md;
            if (level == 3) return true;

            // level 4 - check file headers (aka inodes) (return volume size and type)
            s = 1;
            while (s != 0)
            {
                Block D = Dir[s];
                Int32 sp = 10;
                E esw;
                while (((esw = (E)D.GetUInt16L(sp)) & E.EOS) == 0) // entry status word
                {
                    UInt16 w = D.GetUInt16L(sp + 14 + eb);
                    Boolean lastfile = (((E)w & E.EOS) == E.EOS) && (D.GetUInt16L(2) == 0);
                    w = D.GetUInt16L(sp + 2); // Radix-50 file name (chars 1-3)
                    if ((w >= 64000) && !(lastfile && (esw & E.MPTY) == E.MPTY)) return Debug.WriteLine(false, 1, "RT11.Test: invalid file name in segment {0:D0}", s);
                    String fn1 = Radix50.Convert(w);
                    w = D.GetUInt16L(sp + 4); // Radix-50 file name (chars 4-6)
                    if ((w >= 64000) && !(lastfile && (esw & E.MPTY) == E.MPTY)) return Debug.WriteLine(false, 1, "RT11.Test: invalid file name in segment {0:D0}", s);
                    String fn2 = Radix50.Convert(w);
                    w = D.GetUInt16L(sp + 6); // Radix-50 file type
                    if ((w >= 64000) && !(lastfile && (esw & E.MPTY) == E.MPTY)) return Debug.WriteLine(false, 1, "RT11.Test: invalid file name in segment {0:D0}", s);
                    String ext = Radix50.Convert(w);
                    String fn = String.Concat(fn1, fn2).TrimEnd(' ');
                    fn = String.Concat(fn, ".", ext.TrimEnd(' '));
                    E e = esw & (E.TENT | E.MPTY | E.PERM);
                    if ((e != E.TENT) && (e != E.MPTY) && (e != E.PERM)) return Debug.WriteLine(false, 1, "RT11.Test: invalid file type in segment {0:D0}, file {1}: 0x{2:x4}", s, fn, esw);
                    if (((esw & (E.PROT | E.READ)) != 0) && (e != E.PERM)) return Debug.WriteLine(false, 1, "RT11.Test: Protected/ReadOnly flags not valid for non-permanent file in segment {0:D0}, file {1}: 0x{2:x4}", s, fn, esw);
                    w = D.GetUInt16L(sp + 12); // creation date
                    if ((w != 0) && !(lastfile && (esw & E.MPTY) == E.MPTY))
                    {
                        Int32 y = ((w & 0xc000) >> 9) + (w & 0x001f) + 1972;
                        Int32 m = (w & 0x3c00) >> 10;
                        Int32 d = (w & 0x03e0) >> 5;
                        if ((m < 1) || (m > 12) || (d < 1) || (d > 31) || (y < 1973)) return Debug.WriteLine(false, 1, "RT11.Test: invalid file creation date in segment {0:D0}, file {1}: {2:D4}-{3:D2}-{4:D2}", s, fn, y, m, d);
                    }
                    sp += 14 + eb;
                }
                s = D.GetUInt16L(2); // next directory segment
            }
            if (level == 4) return true;

            // level 5 - check file header allocation (return volume size and type)
            // RT-11 volumes don't have anything like file header allocation
            if (level == 5) return true;

            // level 6 - check data block allocation (return volume size and type)
            // mark used blocks
            BitArray BMap = new BitArray(size, false);
            for (Int32 i = 0; i < 6 + ns * 2; i++) BMap[i] = true;
            s = 1;
            while (s != 0)
            {
                Block D = Dir[s];
                Int32 bp = D.GetUInt16L(8); // starting data block for this segment
                Int32 sp = 10;
                E esw;
                while (((esw = (E)D.GetUInt16L(sp)) & E.EOS) == 0) // entry status word
                {
                    String fn = String.Concat(Radix50.Convert(D.GetUInt16L(sp + 2)), Radix50.Convert(D.GetUInt16L(sp + 4))).TrimEnd(' ');
                    fn = String.Concat(fn, ".", Radix50.Convert(D.GetUInt16L(sp + 6)).TrimEnd(' '));
                    Int32 n = D.GetUInt16L(sp + 8); // file length (in blocks)
                    for (Int32 i = 0; i < n; i++)
                    {
                        if ((bp + i == volume.BlockCount) & ((esw & E.MPTY) == 0)) Debug.WriteLine(1, "RT11.Test: WARNING: blocks {0:D0} and higher of file \"{1}\" fall outside image block range (is {2:D0}, expect n < {3:D0})", bp + i, fn, volume.BlockCount);
                        if (BMap[bp + i]) return Debug.WriteLine(false, 1, "RT11.Test: block {0:D0} of file \"{1}\" is also allocated to another file", bp + i, fn);
                        BMap[bp + i] = true;
                    }
                    bp += n;
                    sp += 14 + eb;
                }
                s = D.GetUInt16L(2); // next directory segment
            }
            // unmarked blocks are lost
            for (Int32 i = 0; i < size; i++)
            {
                if (!BMap[i]) return Debug.WriteLine(false, 1, "RT11.Test: block {0:D0} is not allocated and not reserved", i);
            }
            if (level == 6) return true;

            return false;
        }
    }

    partial class RT11
    {
        private static Boolean IsChecksumOK(Block block, Int32 checksumOffset)
        {
            Int32 sum = 0;
            for (Int32 p = 0; p < checksumOffset; p += 2) sum += block.GetUInt16L(p);
            Int32 n = block.GetUInt16L(checksumOffset);
            Debug.WriteLine(2, "Block checksum @{0:D0} {1}: {2:x4} {3}= {4:x4}", checksumOffset, ((sum != 0) && ((sum % 65536) == n)) ? "PASS" : "FAIL", sum % 65536, ((sum % 65536) == n) ? '=' : '!', n);
            return ((sum != 0) && ((sum % 65536) == n));
        }

        private static Boolean IsASCIIText(Block block, Int32 offset, Int32 count)
        {
            for (Int32 i = 0; i < count; i++)
            {
                Byte b = block[offset + i];
                if ((b < 32) || (b >= 127)) return false;
            }
            return true;
        }

        // convert an RT-11 wildcard pattern to a Regex
        private static Regex Regex(String pattern)
        {
            String p = pattern.ToUpperInvariant();
            String np = p;
            String ep = "*";
            Int32 i = p.IndexOf('.');
            if (i != -1)
            {
                np = p.Substring(0, i);
                if (np.Length == 0) np = "*";
                ep = p.Substring(i + 1);
            }
            np = np.Replace("%", "[^ ]").Replace("*", @".*");
            ep = ep.Replace("%", "[^ ]").Replace("*", @".*");
            p = String.Concat("^", np, @" *\.", ep, " *$");
            Debug.WriteLine(2, "Regex: {0} => {1}", pattern, p);
            return new Regex(p);
        }
    }
}
