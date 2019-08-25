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


using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FSX
{
    partial class CBMDOS
    {
        public static FileSystem Try(CHSDisk disk)
        {
            Program.Debug(1, "CBMDOS.Try: {0}", disk.Source);

            if (CheckVTOC(disk, 2) < 2) return null;
            else return new CBMDOS(disk);
        }

        // level 0 - check basic disk parameters
        // level 1 - check directory length
        // level 2 - check directory consistency
        public static Int32 CheckVTOC(CHSDisk disk, Int32 level)
        {
            if (disk == null) throw new ArgumentNullException("disk");
            if ((level < 0) || (level > 2)) throw new ArgumentOutOfRangeException("level");

            // level 0 - check basic disk parameters
            if (disk.BlockSize != 256)
            {
                Program.Debug(1, "Disk block size = {0:D0} (must be 256)", disk.BlockSize);
                return -1;
            }
            if (disk.MinHead != disk.MaxHead)
            {
                Program.Debug(1, "Disk must be logically single-sided");
                return -1;
            }
            if (disk.MinCylinder < 1)
            {
                Program.Debug(1, "Disk track numbering must start at 1 (is {0:D0})", disk.MinCylinder);
                return -1;
            }
            if (disk.MinSector() != 0)
            {
                Program.Debug(1, "Disk sector numbering must start at 0 (is {0:D0})", disk.MinSector());
                return -1;
            }
            if (disk.MaxCylinder < 18)
            {
                Program.Debug(1, "Disk too small to contain directory track");
                return -1;
            }
            if ((disk.MaxCylinder <= 42) && (disk.MinCylinder > 18))
            {
                Program.Debug(1, "Disk too small to contain directory track 18");
                return -1;
            }
            if ((disk.MaxCylinder > 42) && (disk.MinCylinder > 39))
            {
                Program.Debug(1, "Disk too small to contain directory track 39");
                return -1;
            }
            Int32 ms = -1;
            for (Int32 i = disk.MinCylinder; i <= disk.MaxCylinder; i++) if (disk[i, 0].Length > ms) ms = disk[i, 0].Length;
            if (level == 0) return 0;

            // level 1 - check directory length
            Int32[,] SS = new Int32[disk.MaxCylinder + 1, ms + 1];
            Int32 sc = 0; // segment count
            Int32 t = (disk.MaxCylinder <= 42) ? 18 : 39;
            Int32 s = 0;
            while (t != 0)
            {
                if ((t < disk.MinCylinder) || (t > disk.MaxCylinder))
                {
                    Program.Debug(1, "Invalid directory segment {0:D0}/{1:D0}: track not present", t, s);
                    return 0;
                }
                if ((s < disk[t, 0].MinSector) || (s > disk[t, 0].MaxSector))
                {
                    Program.Debug(1, "Invalid directory segment {0:D0}/{1:D0}: sector not present", t, s);
                    return 0;
                }
                if (SS[t, s] != 0)
                {
                    Program.Debug(1, "Invalid directory segment chain: segment {0:D0}/{1:D0} repeated", t, s);
                    return 0;
                }
                SS[t, s] = ++sc;
                Block B = disk[t, 0, s];
                t = B[0];
                s = B[1];
            }
            if (level == 1) return 1;

            // level 2 - check directory consistency
            t = (disk.MaxCylinder <= 42) ? 18 : 39;
            s = 0;
            Int32 ver = -1;
            Int32 bc = -1; // blocks preceding first directory block
            Int32 bt = 1; // expected start track in next BAM block
            while (t != 0)
            {
                Block B = disk[t, 0, s];
                Int32 l = (B[0] == 0) ? B[1] + 1 : B.Size;
                Boolean f = false;
                if (ver == -1)
                {
                    ver = B[2];
                    if (ver == 1)
                    {
                        bc = 1; // BAM/Header 
                        if ((B[165] != 160) || (B[166] != 160)) // 2 shifted spaces
                        {
                            Program.Debug(1, "Unrecognized DOS version/format in directory header (expected 0x2020, was 0x{0:X2}{1:X2})", B[165], B[166]);
                            return 1;
                        }
                    }
                    else if (ver == 65)
                    {
                        bc = 1; // BAM/Header
                        if ((B[165] != 50) || (B[166] != 65)) // "2A"
                        {
                            Program.Debug(1, "Unrecognized DOS version/format in directory header (expected 0x3241 \"2A\", was 0x{0:X2}{1:X2})", B[165], B[166]);
                            return 1;
                        }
                    }
                    else if (ver == 67)
                    {
                        f = true;
                        bc = (disk.MaxCylinder > 77) ? 5 : 3; // Header block and 2 or 4 BAM blocks
                        if ((B[27] != 50) || (B[28] != 67)) // "2C"
                        {
                            Program.Debug(1, "Unrecognized DOS version/format in directory header (expected 0x3243 \"2C\", was 0x{0:X2}{1:X2})", B[27], B[28]);
                            return 1;
                        }
                    }
                    else
                    {
                        Program.Debug(1, "Unrecognized BAM format {0:D0} (expected 1, 65, or 67)", ver);
                        return 1;
                    }
                }
                if (bc > 0)
                {
                    bc--;
                    if (!f)
                    {
                        // this is a BAM block
                        if (B[2] != ver)
                        {
                            Program.Debug(1, "BAM format mismatch in {0:D0}/{1:D0} (is {2:D0}, expected {3:D0})", t, s, B[2], ver);
                            return 1;
                        }
                        if (ver == 67) // track start/limit only present in DOS 2.5 / 2.7
                        {
                            if (B[4] != bt)
                            {
                                Program.Debug(1, "BAM coverage error in {0:D0}/{1:D0} (start track is {2:D0}, expected {3:D0})", t, s, B[4], bt);
                                return 1;
                            }
                            if (B[5] <= bt)
                            {
                                Program.Debug(1, "BAM coverage error in {0:D0}/{1:D0} (limit track is {2:D0}, expected n > {3:D0})", t, s, B[5], bt);
                                return 1;
                            }
                            bt = B[5];
                        }
                    }
                }
                else
                {
                    // this is a directory block
                    for (Int32 bp = 2; bp < l; bp += 32)
                    {
                        Byte b = B[bp]; // file type
                        if (b == 0) continue; // directory entry not in use
                        Boolean fClosed = ((b & 0x80) != 0);
                        Boolean fLocked = ((b & 0x40) != 0);
                        Boolean fSaveAt = ((b & 0x20) != 0);
                        Int32 fType = b & 0x1f;
                        if (fType > 4)
                        {
                            Program.Debug(1, "Illegal file type in directory entry 0x{0:X2} of {1:D0}/{2:D0} (is {3:D0}, expected 0 <= n <= 4)", bp, t, s, fType);
                            return 1;
                        }
                        Int32 ft = B[bp + 1]; // track of first block
                        if ((ft < disk.MinCylinder) || (ft > disk.MaxCylinder))
                        {
                            Program.Debug(1, "Illegal start track in directory entry 0x{0:X2} of {1:D0}/{2:D0} (is {3:D0}, expected {4:D0} <= n <= {5:D0})", bp, t, s, ft, disk.MinCylinder, disk.MaxCylinder);
                            return 1;
                        }
                        Int32 fs = B[bp + 2]; // sector of first block
                        if ((fs < disk.MinSector(ft, 0)) || (fs > disk.MaxSector(ft, 0)))
                        {
                            Program.Debug(1, "Illegal start sector in directory entry 0x{0:X2} of {1:D0}/{2:D0} (is {3:D0}, expected {4:D0} <= n <= {5:D0})", bp, t, s, fs, disk.MinSector(ft, 0), disk.MaxSector(ft, 0));
                            return 1;
                        }
                        Int32 n = B.ToUInt16(bp + 28); // file block count
                        if (n > (disk.BlockCount - sc))
                        {
                            Program.Debug(1, "Illegal block count in directory entry 0x{0:X2} of {1:D0}/{2:D0} (is {3:D0}, expected 0 <= n <= {4:D0})", bp, t, s, n, disk.BlockCount - sc);
                            return 1;
                        }
                    }
                }
                if ((B[0] == 0) && (ver == 67) && (bt != disk.MaxCylinder + 1))
                {
                    Program.Debug(1, "BAM coverage error in {0:D0}/{1:D0} (limit track is {2:D0}, expected {3:D0})", t, s, bt, disk.MaxCylinder + 1);
                    return 1;
                }
                t = B[0];
                s = B[1];
            }
            return 2;

            // level 3 - check directory entries
            // TODO

            // level 4 - check block allocations
            // TODO
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
}
