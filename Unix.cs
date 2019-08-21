// Unix.cs
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


// Unix File System Structure
//
// https://www.tuhs.org/Archive/Distributions/Research/Dennis_v5/v5man.pdf
// https://archive.org/download/v6-manual/v6-manual.pdf
//
// The Unix v5 file system format is described by the "FILE SYSTEM(V)" man page.
// The v6 format is similar, except that the maximum file size increases from
// 1048576 to 16777215 by changing the last indirect block to doubly indirect.
// The formats are identical as long as no file is larger than 917504 bytes.  If
// the largest file is between 917505 and 1048576 bytes inclusive, the formats
// can be differentiated by looking at the last indirect block.


using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FSX
{
    partial class Unix
    {
        public static Unix Try(Disk disk)
        {
            if (Program.Verbose > 1) Console.Error.WriteLine("Unix.Try: {0}", disk.Source);

            if ((disk.BlockSize != 512) && ((512 % disk.BlockSize) == 0))
            {
                return Try(new ClusteredDisk(disk, 512 / disk.BlockSize, 0));
            }
            else if (disk.BlockSize != 512)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Volume block size = {0:D0} (must be 512)", disk.BlockSize);
                return null;
            }

            Int32 size = CheckVTOC(disk, 1);
            if (size < 1) return null;
            else if (size != disk.BlockCount) return new Unix(new PaddedDisk(disk, size - disk.BlockCount));
            else return new Unix(disk);
        }

        // level 0 - check basic disk parameters
        // level 1 - check super-block (and return volume size)
        public static Int32 CheckVTOC(Disk disk, Int32 level)
        {
            if (disk == null) throw new ArgumentNullException("disk");
            if ((level < 0) || (level > 1)) throw new ArgumentOutOfRangeException("level");

            // level 0 - check basic disk parameters
            if (disk.BlockSize != 512)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk block size = {0:D0} (must be 512)", disk.BlockSize);
                return -1;
            }

            // ensure disk is at least large enough to contain root directory inode
            if (disk.BlockCount < 3)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk too small to contain root directory inode");
                return -1;
            }
            if (level == 0) return 0;

            // level 1 - check super-block (and return volume size)
            Block B = disk[1];
            Int32 isize = B.ToUInt16(0); // number of blocks used for inodes
            Int32 fsize = B.ToUInt16(2); // file system size (in blocks)
            if (fsize < isize + 2)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("I-list size in super-block exceeds volume size ({0:D0} > {1:D0})", isize + 2, fsize);
                return 0;
            }
            return fsize;

            // level 2 - check directory structure (and return volume size)
            // TODO

            // level 3 - check inode allocation (and return volume size)
            // TODO

            // level 4 - check block allocation (and return volume size)
            // TODO
        }

        private static Regex Regex(String pattern)
        {
            String p = pattern;
            p = p.Replace("?", ".").Replace("*", @".*");
            p = String.Concat("^", p, "$");
            if (Program.Verbose > 2) Console.Error.WriteLine("Regex: {0} => {1}", pattern, p);
            return new Regex(p);
        }
    }

    partial class Unix : FileSystem
    {
        [Flags]
        private enum Iflags : ushort
        {
            Wx = 0x0001,
            Ww = 0x0002,
            Wr = 0x0004,
            Wrwx = 0x0007,
            Gx = 0x0008,
            Gw = 0x0010,
            Gr = 0x0020,
            Grwx = 0x0038,
            Ux = 0x0040,
            Uw = 0x0080,
            Ur = 0x0100,
            Urwx = 0x01c0,
            Sgid = 0x0400,
            Suid = 0x0800,
            LF = 0x1000, // large file
            Tfile = 0x0000,
            Tcdev = 0x2000,
            Tdir = 0x4000,
            Tbdev = 0x6000,
            Tmask = 0x6000,
            AF = 0x8000, // allocated
        }

        private struct Inode
        {
            public Int32 inum;
            public Iflags flags;
            public Byte nlinks;
            public Byte uid;
            public Byte gid;
            public Int32 size;
            public UInt16[] addr;
            // 2 words for actime
            // 2 words for modtime

            public static Inode FromBlock(Block block, Int32 offset)
            {
                Inode I = new Inode();
                I.flags = (Iflags)block.ToUInt16(offset);
                I.nlinks = block[offset += 2];
                I.uid = block[++offset];
                I.gid = block[++offset];
                I.size = block[++offset] << 16;
                I.size += block.ToUInt16(++offset);
                I.addr = new UInt16[8];
                for (Int32 i = 0; i < 8; i++) I.addr[i] = block.ToUInt16(offset += 2);
                return I;
            }
        }

        private Disk mDisk;
        private String mType;
        private Int32 mRoot;
        private Inode iDir;
        private String mDir;

        public Unix(Disk disk)
        {
            mDisk = disk;
            mType = "Unix";
            mRoot = 1;
            iDir = ReadInode(1);
            mDir = "/";
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
            get { return mDir; }
        }

        public override Encoding DefaultEncoding
        {
            get { return Encoding.ASCII; }
        }

        public override void ChangeDir(String dirSpec)
        {
            // TODO: update mDir
            Inode iNode = FindFile(iDir, dirSpec);
            if (iNode.inum == 0) return;
            if ((iNode.flags & Iflags.Tmask) != Iflags.Tdir) return;
            iDir = iNode;
        }

        public override void ListDir(String fileSpec, TextWriter output)
        {
            String path = fileSpec;
            Inode dir = iDir;
            if ((path.Length != 0) && (path[0] == '/'))
            {
                while ((path.Length != 0) && (path[0] == '/')) path = path.Substring(1);
                dir = ReadInode(mRoot);
            }
            Int32 p;
            while ((p = path.IndexOf('/')) != -1)
            {
                String f = path.Substring(0, p);
                dir = FindFile(dir, f);
                if (dir.inum == 0) return;
                path = path.Substring(p + 1);
                while ((path.Length != 0) && (path[0] == '/')) path = path.Substring(1);
            }
            if (path.Length == 0) path = "*";
            Regex RE = Regex(path);
            Byte[] data = ReadFile(dir);
            for (Int32 bp = 0; bp < data.Length; bp += 16)
            {
                Int32 i = BitConverter.ToUInt16(data, bp);
                if (i != 0)
                {
                    String s = Encoding.ASCII.GetString(data, bp + 2, 14);
                    while (s[s.Length - 1] == 0) s = s.Substring(0, s.Length - 1);
                    if (!RE.IsMatch(s)) continue;
                    Inode I = ReadInode(i);
                    Char[] mode = new Char[10];
                    Iflags ftyp = I.flags & Iflags.Tmask;
                    mode[0] = (ftyp == Iflags.Tbdev) ? 'b' : (ftyp == Iflags.Tcdev) ? 'c' : (ftyp == Iflags.Tdir) ? 'd' : '-';
                    mode[1] = ((I.flags & Iflags.Ur) != 0) ? 'r' : '-';
                    mode[2] = ((I.flags & Iflags.Uw) != 0) ? 'w' : '-';
                    mode[3] = ((I.flags & Iflags.Ux) != 0) ? 'x' : '-';
                    mode[4] = ((I.flags & Iflags.Gr) != 0) ? 'r' : '-';
                    mode[5] = ((I.flags & Iflags.Gw) != 0) ? 'w' : '-';
                    mode[6] = ((I.flags & Iflags.Gx) != 0) ? 'x' : '-';
                    mode[7] = ((I.flags & Iflags.Wr) != 0) ? 'r' : '-';
                    mode[8] = ((I.flags & Iflags.Ww) != 0) ? 'w' : '-';
                    mode[9] = ((I.flags & Iflags.Wx) != 0) ? 'x' : '-';
                    output.WriteLine("{0} {1,5:D0} {2,-14} [{3:D0}]", new String(mode), I.size, s, i);
                }
            }
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            ListDir(fileSpec, output);
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
            Inode iNode = FindFile(iDir, fileSpec);
            if (iNode.inum == 0) return;
            String buf = encoding.GetString(ReadFile(iNode));
            Int32 p = 0;
            for (Int32 i = 0; i < buf.Length; i++)
            {
                if (buf[i] != '\n') continue;
                output.WriteLine(buf.Substring(p, i - p));
                p = i + 1;
            }
        }

        public override void DumpFile(String fileSpec, TextWriter output)
        {
            Inode iNode = FindFile(iDir, fileSpec);
            if (iNode.inum == 0) return;
            Program.Dump(null, ReadFile(iNode), output, Program.DumpOptions.Default);
        }

        public override String FullName(String fileSpec)
        {
            throw new NotImplementedException();
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            Inode iNode = FindFile(iDir, fileSpec);
            if (iNode.inum == 0) return new Byte[0];
            return ReadFile(iNode);
        }

        private Byte[] ReadFile(Inode iNode)
        {
            Byte[] buf = new Byte[iNode.size];
            if ((iNode.flags & Iflags.LF) == 0)
            {
                // Unix v5/v6 - inode links to 8 direct blocks
                for (Int32 p = 0; p < iNode.size; p += 512)
                {
                    Int32 b = iNode.addr[p / 512]; // direct block
                    if (b == 0) continue;
                    Int32 c = iNode.size - p;
                    mDisk[b].CopyTo(buf, p, 0, (c > 512) ? 512 : c);
                }
            }
            else
            {
                // Unix v5 - inode links to 8 indirect blocks
                // TODO: handle Unix v6
                for (Int32 p = 0; p < iNode.size; p += 512)
                {
                    Int32 b = p / 512;
                    Int32 i = iNode.addr[b / 256]; // indirect block
                    if (i == 0) continue;
                    b = mDisk[i].ToUInt16((b % 256) * 2); // direct block
                    if (b == 0) continue;
                    Int32 c = iNode.size - p;
                    mDisk[b].CopyTo(buf, p, 0, (c > 512) ? 512 : c);
                }
            }
            return buf;
        }

        public override Boolean SaveFS(String fileName, String format)
        {
            throw new NotImplementedException();
        }

        private Inode FindFile(Inode dirInode, String fileSpec)
        {
            String path = fileSpec;
            Inode dir = dirInode;
            if ((path.Length != 0) && (path[0] == '/'))
            {
                while ((path.Length != 0) && (path[0] == '/')) path = path.Substring(1);
                dir = ReadInode(mRoot);
            }
            Int32 p;
            while ((p = path.IndexOf('/')) != -1)
            {
                String f = path.Substring(0, p);
                dir = FindFile(dir, f);
                if (dir.inum == 0) return new Inode();
                path = path.Substring(p + 1);
                while ((path.Length != 0) && (path[0] == '/')) path = path.Substring(1);
            }
            if (path.Length == 0) return dir;
            Regex RE = Regex(path);
            Byte[] data = ReadFile(dir);
            for (Int32 bp = 0; bp < data.Length; bp += 16)
            {
                Int32 i = BitConverter.ToUInt16(data, bp);
                if (i != 0)
                {
                    String s = Encoding.ASCII.GetString(data, bp + 2, 14);
                    while (s[s.Length - 1] == 0) s = s.Substring(0, s.Length - 1);
                    if (RE.IsMatch(s)) return ReadInode(i);
                }
            }
            return new Inode();
        }

        private Inode ReadInode(Int32 iNum)
        {
            Block B = mDisk[(iNum + 31) / 16]; // block containing inode
            Int32 bp = ((iNum + 31) % 16) * 32; // offset within block
            Inode iNode = Inode.FromBlock(B, bp);
            iNode.inum = iNum;
            return iNode;
        }
    }
}
