// CBMDOS.cs
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


// CBMDOS - Commodore DOS, as implemented in drives like the 1541, 4040, 8050, etc.
//
// http://www.commodore.ca/manuals/pdfs/commodore_vic_1541_floppy_drive_users_manual.pdf
// http://www.classiccmp.org/cini/pdf/Commodore/CBM%202040-3040-4040-8050%20Disk%20Drive%20Manual.pdf
//
// Model    DOS Ver     Trk Sect    Blocks  SectSz  Bytes   Directory/BAM
// 2040     DOS 1       35  17-21   690     256     176640  18/0
// 3040     DOS 1       35  17-21   690     256     176640  18/0
// 4040     DOS 2       35  17-21   683     256     174848  18/0
// 8050     DOS 2.5     77  23-29   2083    256     533248  39/0
// 1540     DOS 2.6     35  17-21   683     256     174848  18/0
// 1541     DOS 2.6     35  17-21   683     256     174848  18/0
// 8250     DOS 2.7     154 23-29   4166    256     1066496 39/0
//
// 2040/3040    t1-17:s0-20 t18-24:s0-19 t25-30:s0-17 t31-35:s0-16
// 4040         t1-17:s0-20 t18-24:s0-18 t25-30:s0-17 t31-35:s0-16
// 1540/1541    t1-17:s0-20 t18-24:s0-18 t25-30:s0-17 t31-35:s0-16
// 8050/8250    t1-39:s0-28 t40-53:s0-26 t54-64:s0-24 t65-77:s0-22
// 8250 only    78-116:0-28 117-130:0-26 131-141:0-24 142-154:0-22


// Future Improvements / To Do
// complete Test level 4 (check file headers)
// implement Test level 6 (check data block allocation)
// rewrite FindFile to return true/false
// infer missing file extension when saving images
// add support for 1571 format (and .D71 files)
// add support for 1581 format (and .D81 files)
// allow files to be written/deleted in images


using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FSX
{
    partial class CBMDOS : FileSystem
    {
        private static readonly String[] FT = { "del", "seq", "prg", "usr", "rel" };

        class FileEntry
        {
            public String FileName;
            public Int32 Track;
            public Int32 Sector;
            public Int32 BlockCount;

            public FileEntry(String fileName, Int32 track, Int32 sector, Int32 blockCount)
            {
                FileName = fileName;
                Track = track;
                Sector = sector;
                BlockCount = blockCount;
            }
        }

        private Disk mDisk;
        private String mType;
        private Int32 mDirTrack;
        private Int32 mBlocksFree;

        public CBMDOS(CHSDisk disk)
        {
            mDisk = disk;
            mDirTrack = (disk.MaxCylinder <= 42) ? 18 : 39;
            Byte ver = disk[mDirTrack, 0, 0][2];
            if (ver == 1) mType = "CBMDOS1";
            else if (ver == 65) mType = "CBMDOS2";
            else if (ver == 67) mType = (disk.MaxCylinder > 77) ? "CBMDOS2.7" : "CBMDOS2.5";
            mBlocksFree = CountFreeBlocks();
        }

        public override Disk Disk
        {
            get { return mDisk; }
        }

        public override String Source
        {
            get { return mDisk.Source; }
        }

        public override String Type
        {
            get { return mType; }
        }

        public override String Dir
        {
            get { return String.Empty; }
        }

        public override Encoding DefaultEncoding
        {
            get { return PETSCII1.Encoding; }
        }

        public override void ChangeDir(String dirSpec)
        {
        }

        public override void ListDir(String fileSpec, TextWriter output)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) fileSpec = "*";
            Regex RE = Regex(fileSpec);
            Block B = mDisk[mDirTrack, 0, 0];
            Byte ver = B[2];
            Byte[] buf = new Byte[24];
            Int32 p = (ver == 67) ? 6 : 144;
            B.CopyTo(buf, 0, p, 24);
            String nm = PETSCII1.Encoding.GetString(buf, 0, 16);
            String id = PETSCII1.Encoding.GetString(buf, 18, 2);
            String dv = PETSCII1.Encoding.GetString(buf, 21, 2);
            if (output == Console.Out)
            {
                output.Write("0 ");
                ConsoleColor bg = Console.BackgroundColor;
                ConsoleColor fg = Console.ForegroundColor;
                Console.BackgroundColor = fg;
                Console.ForegroundColor = bg;
                output.Write("\"{0,-16}\" {1} {2}", nm, id, dv);
                Console.BackgroundColor = bg;
                Console.ForegroundColor = fg;
                output.WriteLine();
            }
            else
            {
                output.WriteLine("0 \"{0,-16}\" {1} {2}", nm, id, dv);
            }
            Int32 t = mDirTrack;
            Int32 s = 1;
            while (t != 0)
            {
                B = mDisk[t, 0, s];
                for (Int32 bp = 2; bp < B.Size; bp += 32)
                {
                    Byte b = B[bp];
                    if (b == 0) continue;
                    B.CopyTo(buf, 0, bp + 3, 24);
                    String fn = PETSCII1.Encoding.GetString(buf, 0, 16);
                    while (fn.EndsWith("\u00a0")) fn = fn.Substring(0, fn.Length - 1);
                    if (RE.IsMatch(fn))
                    {
                        Int32 i = b & 0x0f;
                        String ft = (i < 5) ? FT[i] : "???";
                        Char lf = ((b & 0x40) != 0) ? '>' : ' ';
                        Char cf = ((b & 0x80) != 0) ? ' ' : '*';
                        fn = String.Concat("\"", fn, "\"");
                        Int32 sz = B.ToUInt16(bp + 28);
                        output.WriteLine("{0,-4:D0} {1,-18}{2}{3}{4}", sz, fn, lf, ft, cf);
                    }
                }
                t = B[0];
                s = B[1];
            }
            output.WriteLine("{0:D0} blocks free.", mBlocksFree);
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            String fn = String.Format("@{0:D0},{1:D0}", mDirTrack, 0);
            Program.Dump(null, ReadFile(fn, true), output, 16, 256, Program.DumpOptions.PETSCII0 | Program.DumpOptions.PETSCII1);
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
            if (FullName(fileSpec) == null) return;
            String buf = encoding.GetString(ReadFile(fileSpec, false));
            Int32 p = 0;
            for (Int32 i = 0; i < buf.Length; i++)
            {
                if (buf[i] != '\r') continue;
                output.WriteLine(buf.Substring(p, i - p));
                p = i + 1;
            }
        }

        public override void DumpFile(String fileSpec, TextWriter output)
        {
            if (FullName(fileSpec) == null) return;
            Program.Dump(null, ReadFile(fileSpec, true), output, 16, 256, Program.DumpOptions.PETSCII0 | Program.DumpOptions.PETSCII1);
        }

        public override String FullName(String fileSpec)
        {
            FileEntry f = FindFile(fileSpec);
            if (f == null) return null;
            return f.FileName;
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            return ReadFile(fileSpec, false);
        }

        private Byte[] ReadFile(String fileSpec, Boolean includeLinkBytes)
        {
            FileEntry f = FindFile(fileSpec);
            if (f == null) return new Byte[0];

            // determine how many bytes to allocate
            Int32 BPS = (includeLinkBytes) ? 256 : 254; // data bytes per sector
            Int32 p = 0;
            Int32 t, s;
            if (f.BlockCount != -1)
            {
                p = f.BlockCount * BPS;
            }
            else
            {
                t = f.Track;
                s = f.Sector;
                while (t != 0)
                {
                    Block B = mDisk[t, 0, s];
                    t = B[0];
                    s = B[1];
                    p += (t != 0) ? BPS : (includeLinkBytes) ? s + 1 : s - 1;
                }
            }

            // read file
            Byte[] buf = new Byte[p];
            p = 0;
            t = f.Track;
            s = f.Sector;
            while (t != 0)
            {
                Block B = mDisk[t, 0, s];
                t = B[0];
                s = B[1];
                Int32 n = (t != 0) ? BPS : (includeLinkBytes) ? s + 1 : s - 1;
                B.CopyTo(buf, p, (includeLinkBytes) ? 0 : 2, n);
                p += n;
            }
            return buf;
        }

        public override Boolean SaveFS(String fileName, String format)
        {
            if ((fileName == null) || (fileName.Length == 0)) return false;
            if (fileName.IndexOf('.') == -1)
            {
                // TODO: infer file extension based on disk size and/or DOS version
            }

            Boolean ef = false; // whether any sector errors need to be recorded
            for (Int32 i = 0; i < mDisk.BlockCount; i++) if ((mDisk[i] is Sector) && ((mDisk[i] as Sector).ErrorCode > 1)) ef = true;
            FileStream f = new FileStream(fileName, FileMode.Create);
            Byte[] buf = new Byte[256];
            for (Int32 i = 0; i < mDisk.BlockCount; i++)
            {
                mDisk[i].CopyTo(buf, 0);
                f.Write(buf, 0, 256);
            }
            if (ef)
            {
                for (Int32 i = 0; i < mDisk.BlockCount; i++)
                {
                    buf[0] = (Byte)((mDisk[i] is Sector) ? (mDisk[i] as Sector).ErrorCode : 1);
                    f.Write(buf, 0, 1);
                }
            }
            f.Close();
            return true;
        }

        private FileEntry FindFile(String fileName)
        {
            if ((fileName == null) || (fileName.Length == 0)) return null;
            Int32 p = fileName.IndexOf('@');
            if (p != -1)
            {
                Int32 count;
                if (!Int32.TryParse(fileName.Substring(0, p), out count)) count = -1;
                String fn = fileName.Substring(p + 1);
                p = fn.IndexOf('/');
                if (p == -1) p = fn.IndexOf(',');
                if (p == -1) return null;
                Int32 tnum, snum;
                if (!Int32.TryParse(fn.Substring(0, p).Trim(), out tnum) || !Int32.TryParse(fn.Substring(p + 1).Trim(), out snum)) return null;
                return new FileEntry(fileName, tnum, snum, count);
            }

            Regex RE = Regex(fileName);
            Byte[] buf = new Byte[16];
            Int32 t = mDirTrack;
            Int32 s = 1;
            while (t != 0)
            {
                Block B = mDisk[t, 0, s];
                for (Int32 bp = 2; bp < B.Size; bp += 32)
                {
                    Byte b = B[bp];
                    if (b == 0) continue;
                    B.CopyTo(buf, 0, bp + 3, 16);
                    String fn = PETSCII1.Encoding.GetString(buf, 0, 16);
                    while (fn.EndsWith("\u00a0")) fn = fn.Substring(0, fn.Length - 1);
                    if (RE.IsMatch(fn)) return new FileEntry(fn, B[bp + 1], B[bp + 2], -1);
                }
                t = B[0];
                s = B[1];
            }
            return null;
        }

        private Int32 CountFreeBlocks()
        {
            Block B = mDisk[mDirTrack, 0, 0];
            if (B[2] != 67) // DOS 1.0 / 2.0
            {
                Int32 n = 0;
                Int32 l = 4 + 35 * 4;
                for (Int32 i = 4; i < l; i += 4) n += B[i];
                return n;
            }
            else // DOS 2.5 / 2.7
            {
                Int32 n = 0;
                Int32 c = (mDisk.MaxCylinder > 77) ? 4 : 2;
                for (Int32 j = 0; j < c; j++)
                {
                    B = mDisk[B[0], 0, B[1]];
                    Int32 l = 6 + (B[5] - B[4]) * 5;
                    for (Int32 i = 6; i < l; i += 5) n += B[i];
                }
                return n;
            }
        }
    }

    partial class CBMDOS : IFileSystemGetTest
    {
        public static TestDelegate GetTest()
        {
            return CBMDOS.Test;
        }

        // level 0 - check basic disk parameters (return required block size and disk type)
        // level 1 - check boot block (return disk size and type)
        // level 2 - check volume descriptor (aka home/super block) (return volume size and type)
        // level 3 - check directory structure (return volume size and type)
        // level 4 - check file headers (aka inodes) (return volume size and type)
        // level 5 - check file header allocation (return volume size and type)
        // level 6 - check data block allocation (return volume size and type)
        // note: levels 3 and 4 are reversed because this makes more sense for CBMDOS volumes
        public static Boolean Test(Disk dsk, Int32 level, out Int32 size, out Type type)
        {
            // level 0 - check basic disk parameters (return required block size and disk type)
            size = 256;
            type = typeof(CHSDisk);
            if (dsk == null) return false;
            if (!(dsk is CHSDisk)) return Program.Debug(false, 1, "CBMDOS.Test: disk must be track-oriented (e.g. 'CHSDisk')");
            CHSDisk disk = dsk as CHSDisk;
            if (disk.BlockSize != size) return Program.Debug(false, 1, "CBMDOS.Test: invalid block size (is {0:D0}, require {1:D0})", disk.BlockSize, size);
            if (disk.MinHead != disk.MaxHead) return Program.Debug(false, 1, "CBMDOS.Test: disk must be logically single-sided");
            if (disk.MinCylinder < 1) return Program.Debug(false, 1, "CBMDOS.Test: disk track numbering must start at 1 (is {0:D0})", disk.MinCylinder);
            if (disk.MinSector() != 0) return Program.Debug(false, 1, "CBMDOS.Test: disk sector numbering must start at 0 (is {0:D0})", disk.MinSector());
            if (level == 0) return true;

            // level 1 - check boot block (return disk size and type)
            if (level == 1)
            {
                size = -1;
                type = typeof(CHSDisk);
                return true;
            }

            // level 2 - check volume descriptor (aka home/super block) (return volume size and type)
            size = -1;
            type = null;
            Int32 t = (disk.MaxCylinder <= 42) ? 18 : 39;
            Int32 s = 0;
            Track T = GetTrack(disk, t);
            if (T == null) return Program.Debug(false, 1, "CBMDOS.Test: disk image does not include directory track {0:D0}", t);
            Block B = T[s];
            if (B == null) return Program.Debug(false, 1, "CBMDOS.Test: disk image does not include directory header block {0:D0}/{1:D0}", t, s);
            Byte fmt = B[2];
            if ((fmt != 0x01) && (fmt != 0x41) && (fmt != 0x43)) return Program.Debug(false, 1, "CBMDOS.Test: BAM format byte invalid (is 0x{0:x2}, expect 0x01, 0x41, or 0x43)", fmt);
            if ((t == 18) && (T.Length == 20) && (fmt != 0x01)) return Program.Debug(false, 1, "CBMDOS.Test: BAM format byte incorrect (is 0x{0:x2}, expect 0x01)", fmt);
            if ((t == 18) && (T.Length == 19) && (fmt != 0x41)) return Program.Debug(false, 1, "CBMDOS.Test: BAM format byte incorrect (is 0x{0:x2}, expect 0x41)", fmt);
            if ((t == 18) && (fmt == 0x43)) return Program.Debug(false, 1, "CBMDOS.Test: BAM format byte incorrect (is 0x43, expect 0x01 or 0x41)");
            if ((t == 39) && (fmt != 0x43)) return Program.Debug(false, 1, "CBMDOS.Test: BAM format byte incorrect (is 0x{0:x2}, expect 0x43)", fmt);
            size = (fmt == 0x01) ? 690 : (fmt == 0x41) ? 683 : (disk.MaxCylinder <= 77) ? 2083 : 4166;
            type = typeof(CBMDOS);
            if (level == 2) return true;

            // level 3 - check directory structure (return volume size and type)
            Int32 dc = (fmt != 0x43) ? 1 : (disk.MaxCylinder <= 77) ? 3 : 5; // number of blocks that precede first directory block
            Int32 bc = (fmt != 0x43) ? 0 : 1; // number of blocks that precede first BAM block
            if ((fmt == 0x01) && (B[165] != 0xa0)) return Program.Debug(false, 1, "CBMDOS.Test: directory header version byte invalid (is 0x{0:x2}, expect 0xa0 ' ')", B[165]);
            if ((fmt == 0x01) && (B[166] != 0xa0)) return Program.Debug(false, 1, "CBMDOS.Test: directory header format byte invalid (is 0x{0:x2}, expect 0xa0 ' ')", B[166]);
            if ((fmt == 0x41) && (B[165] != 0x32)) return Program.Debug(false, 1, "CBMDOS.Test: directory header version byte invalid (is 0x{0:x2}, expect 0x32 '2')", B[165]);
            if ((fmt == 0x41) && (B[166] != 0x41)) return Program.Debug(false, 1, "CBMDOS.Test: directory header format byte invalid (is 0x{0:x2}, expect 0x41 'A')", B[166]);
            if ((fmt == 0x43) && (B[27] != 0x32)) return Program.Debug(false, 1, "CBMDOS.Test: directory header version byte invalid (is 0x{0:x2}, expect 0x32 '2')", B[27]);
            if ((fmt == 0x43) && (B[28] != 0x43)) return Program.Debug(false, 1, "CBMDOS.Test: directory header format byte invalid (is 0x{0:x2}, expect 0x43 'C')", B[28]);
            Int32 maxSect = -1;
            for (Int32 i = disk.MinCylinder; i <= disk.MaxCylinder; i++)
            {
                T = GetTrack(disk, i);
                if ((T != null) && (T.MaxSector > maxSect)) maxSect = T.MaxSector;
            }
            Int32[,] BMap = new Int32[disk.MaxCylinder + 1, maxSect + 1];
            Int32 sc = 0; // sector count within directory chain
            Int32 st = 1; // starting track expected in next BAM block (DOS 2.5 / 2.7 only)
            while (t != 0)
            {
                sc++;
                T = GetTrack(disk, t);
                if (T == null) return Program.Debug(false, 1, "CBMDOS.Test: invalid directory chain at segment {0:D0}: track not found for {1:D0}/{2:D0}", sc, t, s);
                B = T[s];
                if (B == null) return Program.Debug(false, 1, "CBMDOS.Test: invalid directory chain at segment {0:D0}: sector not found for {1:D0}/{2:D0}", sc, t, s);
                if (BMap[t, s] != 0) return Program.Debug(false, 1, "CBMDOS.Test: invalid directory chain at segment {0:D0}: cycle detected at {1:D0}/{2:D0}", sc, t, s);
                BMap[t, s] = sc;
                Int32 l = (B[0] == 0) ? B[1] + 1 : B.Size;
                if (dc > 0)
                {
                    dc--;
                    if (bc > 0) // directory header block
                    {
                        bc--;
                    }
                    else // this is a BAM block
                    {
                        if (B[2] != fmt) return Program.Debug(false, 1, "CBMDOS.Test: invalid directory chain at segment {0:D0}: BAM format byte invalid at {1:D0}/{2:D0} (is 0x{3:x2}, expect 0x{4:x2})", sc, t, s, B[2], fmt);
                        if (fmt == 0x43) // track start/limit only present in DOS 2.5 / 2.7
                        {
                            if (B[4] != st) return Program.Debug(false, 1, "CBMDOS.Test: BAM coverage error at {0:D0}/{1:D0}: start track invalid (is {2:D0}, expect {3:D0})", t, s, B[4], st);
                            if (B[5] <= st) return Program.Debug(false, 1, "CBMDOS.Test: BAM coverage error at {0:D0}/{1:D0}: limit track invalid (is {2:D0}, expect n > {3:D0})", t, s, B[5], st);
                            st = B[5];
                        }
                    }
                }
                if ((B[0] == 0) && (fmt == 0x43) && (st != disk.MaxCylinder + 1)) return Program.Debug(false, 1, "CBMDOS.Test: BAM coverage error at {0:D0}/{1:D0}: limit track invalid (is {2:D0}, expect {3:D0})", t, s, st, disk.MaxCylinder + 1);
                t = B[0];
                s = B[1];
            }
            if (level == 3) return true;

            // level 4 - check file headers (aka inodes) (return volume size and type)
            Int32 fc = 0;
            t = (disk.MaxCylinder <= 42) ? 18 : 39;
            s = 1;
            while (t != 0)
            {
                B = GetTrack(disk, t)[s];
                Int32 l = (B[0] == 0) ? B[1] + 1 : B.Size;
                for (Int32 bp = 2; bp < level; bp += 32)
                {
                    fc += 65536;
                    sc = 0;
                    Byte b = B[bp]; // file type
                    if (b == 0) continue; // directory entry not in use
                    Boolean fClosed = ((b & 0x80) != 0);
                    Boolean fLocked = ((b & 0x40) != 0);
                    Boolean fSaveAt = ((b & 0x20) != 0);
                    Int32 fType = b & 0x1f;
                    if (fType > 4) return Program.Debug(false, 1, "CBMDOS.Test: invalid file type in directory entry {0:D0}/{1:D0} 0x{2:x2} (is {3:D0}, require 0 <= n <= 4)", t, s, bp, fType);
                    Int32 ft = B[bp + 1]; // track of first block
                    if ((T = GetTrack(disk, ft)) == null) return Program.Debug(false, 1, "CBMDOS.Test: invalid start track in directory entry {0:D0}/{1:D0} 0x{2:x2} (is {3:D0}, expect {4:D0} <= n <= {5:D0})", t, s, bp, ft, disk.MinCylinder, disk.MaxCylinder);
                    Int32 fs = B[bp + 2];
                    if (T[fs] == null) return Program.Debug(false, 1, "CBMDOS.Test: invalid start sector in directory entry {0:D0}/{1:D0} 0x{2:x2} (is {3:D0}, expect {4:D0} <= n <= {5:D0})", t, s, bp, fs, T.MinSector, T.MaxSector);
                    Int32 n = B.ToUInt16(bp + 28); // file block count
                    if (n > (size - sc)) return Program.Debug(false, 1, "CBMDOS.Test: invalid block count in directory entry {0:D0}/{1:D0} 0x{2:x2} (is {3:D0}, expect n <= {4:D0})", t, s, bp, n, size - sc);
                    // file size in directory matches number of blocks in data block chain
                    // error if file blocks impossible (i.e. not within volume)
                    // warning if file blocks not valid (i.e. not present in disk image)
                    // file data block chains contain no cycles
                }
                t = B[0];
                s = B[1];
            }
            if (level == 4) return true;

            // level 5 - check file header allocation (return volume size and type)
            if (level == 5) return true;

            // level 6 - check data block allocation (return volume size and type)
            // blocks allocated to at most one file
            // free/used blocks correctly recorded in BAM
            // error if free blocks impossible (i.e. not within volume)
            if (level == 6) return true;

            return false;
        }
    }

    partial class CBMDOS
    {
        private static Track GetTrack(CHSDisk disk, Int32 track)
        {
            if (track < disk.MinCylinder) return null;
            if (track > disk.MaxCylinder) return null;
            return disk[track, disk.MinHead];
        }

        // convert a CBMDOS wildcard pattern to a Regex
        private static Regex Regex(String pattern)
        {
            String p = pattern;
            Int32 i = p.IndexOf('*');
            if (i != -1) p = p.Substring(0, i + 1); // anything after * is irrelevant
            p = p.Replace("?", ".").Replace("*", @".*");
            p = String.Concat("^", p, "$");
            Program.Debug(2, "Regex: {0} => {1}", pattern, p);
            return new Regex(p, RegexOptions.IgnoreCase);
        }
    }
}
