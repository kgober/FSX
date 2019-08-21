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
            if (Program.Verbose > 1) Console.Error.WriteLine("CBMDOS.Try: {0}", disk.Source);

            if (CheckVTOC(disk, 1) < 1) return null;
            else return new CBMDOS(disk);
        }

        // level 0 - check basic disk parameters
        // level 1 - check directory length
        public static Int32 CheckVTOC(CHSDisk disk, Int32 level)
        {
            if (disk == null) throw new ArgumentNullException("disk");
            if ((level < 0) || (level > 1)) throw new ArgumentOutOfRangeException("level");

            // level 0 - check basic disk parameters
            if (disk.BlockSize != 256)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk block size = {0:D0} (must be 256)", disk.BlockSize);
                return -1;
            }
            if (disk.MinHead != disk.MaxHead)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk must be logically single-sided");
                return -1;
            }
            if (disk.MinCylinder < 1)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk track numbering must start at 1 (is {0:D0})", disk.MinCylinder);
                return -1;
            }
            if (disk.MinSector() != 0)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk sector numbering must start at 0 (is {0:D0})", disk.MinSector());
                return -1;
            }
            if (disk.MaxCylinder < 18)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk too small to contain directory track");
                return -1;
            }
            if ((disk.MaxCylinder <= 42) && (disk.MinCylinder > 18))
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk too small to contain directory track 18");
                return -1;
            }
            if ((disk.MaxCylinder > 42) && (disk.MinCylinder > 39))
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk too small to contain directory track 39");
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
                    if (Program.Verbose > 1) Console.Error.WriteLine("Invalid directory segment {0:D0}/{1:D0}: track not present", t, s);
                    return 0;
                }
                if ((s < disk[t, 0].MinSector) || (t > disk[t, 0].MaxSector))
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Invalid directory segment {0:D0}/{1:D0}: sector not present", t, s);
                    return 0;
                }
                if (SS[t, s] != 0)
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Invalid directory segment chain: segment {0:D0}/{1:D0} repeated", t, s);
                    return 0;
                }
                SS[t, s] = ++sc;
                Block b = disk[t, 0, s];
                t = b[0];
                s = b[1];
            }
            return 1;

            // level 2 - check directory consistency
            // TODO

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
            if (i != -1) p = p.Substring(0, i + 1);
            p = p.Replace("?", ".").Replace("*", @".*");
            p = String.Concat("^", p, "$");
            if (Program.Verbose > 2) Console.Error.WriteLine("Regex: {0} => {1}", pattern, p);
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

        public CBMDOS(CHSDisk disk)
        {
            mDisk = disk;
            mDirTrack = (disk.MaxCylinder <= 42) ? 18 : 39;
            Byte v = disk[mDirTrack, 0, 0][2];
            if (v == 1) mType = "CBMDOS1";
            else if (v == 65) mType = "CBMDOS2"; // v == 'A'
            else if (v == 67) mType = (disk.MaxCylinder == 77) ? "CBMDOS2.5" : "CBMDOS2.7"; // v == 'C'
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
            Byte v = B[2];
            Byte[] buf = new Byte[24];
            Int32 p = (v == 67) ? 6 : 144;
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
                output.WriteLine(" ");
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
                for (Int32 bp = 0; bp < B.Size; bp += 32)
                {
                    Byte b = B[bp + 2];
                    if (b == 0) continue;
                    B.CopyTo(buf, 0, bp + 5, 24);
                    String fn = PETSCII1.Encoding.GetString(buf, 0, 16);
                    while (fn.EndsWith("\u00a0")) fn = fn.Substring(0, fn.Length - 1);
                    if (RE.IsMatch(fn))
                    {
                        Int32 i = b & 0x0f;
                        String ft = (i < 5) ? FT[i] : "???";
                        Char lf = ((b & 0x40) != 0) ? '>' : ' ';
                        Char cf = ((b & 0x80) != 0) ? ' ' : '*';
                        fn = String.Concat("\"", fn, "\"");
                        Int32 sz = B.ToUInt16(bp + 30);
                        output.WriteLine("{0,-4:D0} {1,-18}{2}{3}{4}", sz, fn, lf, ft, cf);
                    }
                }
                t = B[0];
                s = B[1];
            }
            // TODO: display "{0:D0} blocks free."
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            ListDir(fileSpec, output);
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
            Program.Dump(null, ReadFile(fileSpec, true), output, Program.DumpOptions.NoASCII | Program.DumpOptions.PETSCII1);
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
                for (Int32 bp = 0; bp < B.Size; bp += 32)
                {
                    Byte b = B[bp + 2];
                    if (b == 0) continue;
                    B.CopyTo(buf, 0, bp + 5, 16);
                    String fn = PETSCII1.Encoding.GetString(buf, 0, 16);
                    while (fn.EndsWith("\u00a0")) fn = fn.Substring(0, fn.Length - 1);
                    if (RE.IsMatch(fn)) return new FileEntry(fn, B[bp + 3], B[bp + 4], -1);
                }
                t = B[0];
                s = B[1];
            }
            return null;
        }
    }
}
