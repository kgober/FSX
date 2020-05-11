// Unix.cs
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


// Unix File System Structure
//
// https://www.tuhs.org/Archive/Distributions/Research/Dennis_v5/v5man.pdf
// https://archive.org/download/v6-manual/v6-manual.pdf
// http://web.cuzuco.com/~cuzuco/v7/v7vol1.pdf
// http://web.cuzuco.com/~cuzuco/v7/v7vol2b.pdf
//
// The Unix v5 file system format is described by the "FILE SYSTEM(V)" man page.
// The v6 format is similar, except that the maximum file size increases from
// 1048576 to 16777216 by changing the last indirect block to doubly indirect.
// The formats are identical as long as no file is larger than 917504 bytes.  If
// the largest file is between 917505 and 1048576 bytes inclusive, the formats
// can be (mostly) differentiated by looking at the last indirect block.
//
// The Unix v7 file system format increases block pointers to 24 bits, and
// increases the size of an i-node to contain additional block pointers.  The
// 'large file' flag is removed; i-node block pointers are interpreted the same
// way for all files: 10 direct pointers, then 1 indirect, 1 double-indirect,
// and 1 triple-indirect pointer.  Each indirect block contains 128 4-byte
// pointers, so the maximum file size increases to:
//   512 * (10 + 128 + 128*128 + 128*128*128) = 1082201087 bytes
//
// 2.8BSD doubles the block size to 1024 bytes.  As a result, each indirect
// block contains 256 4-byte pointers.
//
// 2.11BSD changes the i-node format to contain 4 direct pointers rather than
// 10, but the size pointers (both direct and indirect) are increased from 3
// bytes each to 4.  Additionally, 2.11BSD changes the format of directories
// to support file names up to 63 characters.


// Improvements / To Do
// in Test, check for duplicate names in a directory
// relax link count check for inodes (file systems are readable despite this)
// relax block allocation checks (file systems are readable despite this)
// support 2.8BSD+ (v7 with 1k blocks)
// support BSD Fast File System (FFS)
// allow files to be written/deleted in images


using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FSX
{
    class Inode
    {
        protected Volume mVol;
        public Int32 iNum;
        public UInt16 flags;
        public Int16 nlinks;
        public Int16 uid;
        public Int16 gid;
        public Int32 size;
        public Int32[] addr;
        public DateTime atime;
        public DateTime mtime;
        public DateTime ctime;

        public Inode(Volume volume, Int32 iNumber)
        {
            mVol = volume;
            iNum = iNumber;
        }

        public Volume Volume
        {
            get { return mVol; }
        }

        public virtual Char Type
        {
            get { return '\0'; }
        }

        public virtual String Mode
        {
            get { return null; }
        }

        public virtual Int32 this[Int32 blockNum]
        {
            get { return 0; }
        }
    }


    // v5/v6 super block
    //  0   isize - i-list size (in blocks)
    //  2   fsize - file system size
    //  4   nfree - number of free blocks listed in superblock (0-100)
    //  6   free  - free block list
    //  206 ninode - number of fre einodes listed in superblock (0-100)
    //  208 inode - free inode list
    //  408 flock
    //  409 ilock
    //  410 fmod
    //  411 time
    //  415
    //
    // v5/v6 i-node flags
    //  0x8000  allocated / in use
    //  0x6000  file type (file=0x0000 cdev=0x2000 dir=0x4000 bdev=0x6000)
    //  0x1000  large file (addr[] has indirect blocks, instead of direct)
    //  0x0800  setuid
    //  0x0400  setgid
    //  0x01c0  owner permissions (r=0x0100 w=0x0080 x=0x0040)
    //  0x0038  group permissions (r=0x0020 w=0x0010 x=0x0008)
    //  0x0007  world permissions (r=0x0004 w=0x0002 x=0x0001)

    partial class UnixV5 : FileSystem
    {
        protected class InodeV5 : Inode
        {
            public InodeV5(Volume volume, Int32 iNumber) : base(volume, iNumber)
            {
                Block B = volume[2 + (iNumber - 1) / 16];
                Int32 offset = ((iNumber - 1) % 16) * 32;
                flags = B.GetUInt16L(ref offset);
                nlinks = B.GetByte(ref offset);
                uid = B.GetByte(ref offset);
                gid = B.GetByte(ref offset);
                size = B.GetByte(ref offset) << 16;
                size |= B.GetUInt16L(ref offset);
                addr = new Int32[8];
                for (Int32 i = 0; i < 8; i++) addr[i] = B.GetUInt16L(ref offset);
                atime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
                mtime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
            }

            public override Char Type
            {
                get
                {
                    if ((flags & 0xe000) == 0x8000) return '-';
                    if ((flags & 0xe000) == 0xc000) return 'd';
                    if ((flags & 0xe000) == 0xa000) return 'c';
                    if ((flags & 0xe000) == 0xe000) return 'b';
                    return ' ';
                }
            }

            public override String Mode
            {
                get
                {
                    StringBuilder buf = new StringBuilder(9);
                    buf.Append(((flags & 0x0100) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0080) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0040) == 0) ? '-' : ((flags & 0x0800) == 0) ? 'x' : 's');
                    buf.Append(((flags & 0x0020) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0010) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0008) == 0) ? '-' : ((flags & 0x0400) == 0) ? 'x' : 's');
                    buf.Append(((flags & 0x0004) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0002) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0001) == 0) ? '-' : 'x');
                    return buf.ToString();
                }
            }

            public override Int32 this[Int32 blockNum]
            {
                get
                {
                    if ((flags & 0x1000) == 0) return addr[blockNum];
                    Block B = mVol[addr[blockNum / 256]];
                    return B.GetUInt16L(2 * (blockNum % 256));
                }
            }
        }

        protected Int32 BLOCK_SIZE = 512;
        protected Int32 ROOT_INUM = 1;
        protected Int32 INOPB = 16;
        protected Int32 ISIZE = 32;
        protected Volume mVol;
        protected String mType;
        protected Inode mDirNode;
        protected String mDir;


        protected UnixV5()
        {
        }

        public UnixV5(Volume volume)
        {
            mVol = volume;
            mType = "Unix/V5";
            mDirNode = new InodeV5(volume, ROOT_INUM);
            mDir = "/";
        }

        public override String Source
        {
            get { return mVol.Source; }
        }

        public override String Type
        {
            get { return mType; }
        }

        public override String Info
        {
            get { return mVol.Info; }
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
            Inode dirNode;
            String dirName;
            if (!FindFile(dirSpec, out dirNode, out dirName))
            {
                Console.Error.WriteLine("Not found: {0}", dirSpec);
                return;
            }
            if (dirNode.Type != 'd')
            {
                Console.Error.WriteLine("Not a directory: {0}", dirName);
                return;
            }
            mDirNode = dirNode;
            mDir = dirName;
        }

        public override void ListDir(String fileSpec, TextWriter output)
        {
            Int32 p;
            Inode dirNode = mDirNode;
            String dirName = mDir;
            if ((fileSpec == null) || (fileSpec.Length == 0))
            {
                fileSpec = "*";
            }
            else if ((p = fileSpec.LastIndexOf('/')) == 0)
            {
                dirNode = GetInode(ROOT_INUM);
                dirName = "/";
                while ((fileSpec.Length != 0) && (fileSpec[0] == '/')) fileSpec = fileSpec.Substring(1);
                if (fileSpec.Length == 0) fileSpec = "*";
            }
            else if (p != -1)
            {
                if ((!FindFile(fileSpec.Substring(0, p), out dirNode, out dirName)) || (dirNode.Type != 'd'))
                {
                    Console.Error.WriteLine("Not found: {0}", fileSpec);
                    return;
                }
                fileSpec = fileSpec.Substring(p + 1);
                while ((fileSpec.Length != 0) && (fileSpec[0] == '/')) fileSpec = fileSpec.Substring(1);
                if (fileSpec.Length == 0) fileSpec = "*";
            }

            // if fileSpec names a single directory, show the content of that directory
            Inode iNode;
            String pathName;
            if ((FindDirEntry(dirNode, dirName, fileSpec, out iNode, out pathName)) && (iNode.Type == 'd'))
            {
                dirNode = iNode;
                dirName = pathName;
                fileSpec = "*";
                output.WriteLine("{0}:", pathName);
            }

            // count number of blocks used
            Int32 n = 0;
            Regex RE = Regex(fileSpec);
            Byte[] data = ReadFile(dirNode, BLOCK_SIZE);
            for (Int32 bp = 0; bp < data.Length; bp += 16)
            {
                Int32 iNum = Buffer.GetUInt16L(data, bp);
                if (iNum == 0) continue;
                String name = Buffer.GetCString(data, bp + 2, 14, Encoding.ASCII);
                if (!RE.IsMatch(name)) continue;
                iNode = GetInode(iNum);
                n += (iNode.size + 511) / 512;
            }

            // show directory listing
            output.WriteLine("total {0:D0}", n);
            for (Int32 bp = 0; bp < data.Length; bp += 16)
            {
                Int32 iNum = Buffer.GetUInt16L(data, bp);
                if (iNum == 0) continue;
                String name = Buffer.GetCString(data, bp + 2, 14, Encoding.ASCII);
                if (!RE.IsMatch(name)) continue;
                iNode = GetInode(iNum);
                Char ft = iNode.Type;
                switch (ft)
                {
                    case '-':
                    case 'd':
                        output.WriteLine("{0,5:D0} {1}{2} {3,2:D0} {4,-5} {5,7:D0} {6}  {7,-14}", iNum, ft, iNode.Mode, iNode.nlinks, iNode.uid.ToString(), iNode.size, iNode.mtime.ToString("MM/dd/yyyy HH:mm"), name);
                        break;
                    case 'b':
                    case 'c':
                        output.WriteLine("{0,5:D0} {1}{2} {3,2:D0} {4,-5} {5,3:D0},{6,3:D0} {7}  {8,-14}", iNum, ft, iNode.Mode, iNode.nlinks, iNode.uid.ToString(), iNode.addr[0] >> 8, iNode.addr[0] & 0x00ff, iNode.mtime.ToString("MM/dd/yyyy HH:mm"), name);
                        break;
                }
            }
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            ListDir(fileSpec, output);
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
            Inode iNode;
            String name;
            if (!FindFile(fileSpec, out iNode, out name)) return;
            String buf = encoding.GetString(ReadFile(iNode, BLOCK_SIZE));
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
            Inode iNode;
            String name;
            if (!FindFile(fileSpec, out iNode, out name)) return;
            Program.Dump(null, ReadFile(iNode, BLOCK_SIZE), output, 16, BLOCK_SIZE, Program.DumpOptions.ASCII);
        }

        public override String FullName(String fileSpec)
        {
            Inode iNode;
            String name;
            if (!FindFile(fileSpec, out iNode, out name)) return null;
            return name;
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            Inode iNode;
            String name;
            if (!FindFile(fileSpec, out iNode, out name)) return null;
            return ReadFile(iNode, BLOCK_SIZE);
        }

        public override Boolean SaveFS(String fileName, String format)
        {
            if ((fileName == null) || (fileName.Length == 0)) return false;
            FileStream f = new FileStream(fileName, FileMode.Create);
            Byte[] buf = new Byte[BLOCK_SIZE];
            for (Int32 i = 0; i < mVol.BlockCount; i++)
            {
                mVol[i].CopyTo(buf, 0);
                f.Write(buf, 0, BLOCK_SIZE);
            }
            f.Close();
            return true;
        }

        protected virtual Inode GetInode(Int32 iNumber)
        {
            return new InodeV5(mVol, iNumber);
        }

        protected Boolean FindFile(String pathSpec, out Inode iNode, out String pathName)
        {
            iNode = mDirNode;
            pathName = mDir;
            if ((pathSpec == null) || (pathSpec.Length == 0)) return false;
            if (pathSpec[0] == '/')
            {
                iNode = GetInode(ROOT_INUM);
                pathName = "/";
                while (pathSpec[0] == '/')
                {
                    pathSpec = pathSpec.Substring(1);
                    if (pathSpec.Length == 0) return true;
                }
            }

            while (pathSpec.Length != 0)
            {
                String s;
                Int32 p = pathSpec.IndexOf('/');
                if (p == -1)
                {
                    s = pathSpec;
                    pathSpec = String.Empty;
                }
                else
                {
                    s = pathSpec.Substring(0, p);
                    pathSpec = pathSpec.Substring(p);
                }
                if (!FindDirEntry(iNode, pathName, s, out iNode, out pathName)) return false;
                if ((pathSpec.Length != 0) && (iNode.Type != 'd')) return false;
                while ((pathSpec.Length != 0) && (pathSpec[0] == '/')) pathSpec = pathSpec.Substring(1);
            }
            return true;
        }

        protected virtual Boolean FindDirEntry(Inode dirNode, String dirName, String entryName, out Inode iNode, out String pathName)
        {
            iNode = null;
            pathName = null;
            Int32 n = 0;
            Regex RE = Regex(entryName);
            Byte[] dir = ReadFile(dirNode, BLOCK_SIZE);
            for (Int32 dp = 0; dp < dir.Length; dp += 16)
            {
                Int32 iNum = Buffer.GetUInt16L(dir, dp);
                if (iNum == 0) continue;
                String name = Buffer.GetCString(dir, dp + 2, 14, Encoding.ASCII);
                if (RE.IsMatch(name))
                {
                    n++;
                    iNode = GetInode(iNum);
                    if ((name == ".") || ((name == "..") && (dirName == "/")))
                    {
                        pathName = dirName;
                    }
                    else if (name == "..")
                    {
                        pathName = dirName.Substring(0, dirName.Length - 1);
                        pathName = pathName.Substring(0, pathName.LastIndexOf('/') + 1);
                    }
                    else
                    {
                        pathName = String.Concat(dirName, name, (iNode.Type == 'd') ? "/" : null);
                    }
                }
            }
            return (n == 1);
        }
    }

    partial class UnixV5
    {
        protected static Byte[] ReadFile(Inode iNode, Int32 blockSize)
        {
            Byte[] buf = new Byte[iNode.size];
            Int32 bp = 0;
            while (bp < buf.Length)
            {
                Int32 n = bp / blockSize; // file block number
                Int32 b = iNode[n]; // volume block number
                if (b != 0) iNode.Volume[b].CopyTo(buf, bp);
                bp += blockSize;
            }
            return buf;
        }

        protected static Regex Regex(String pattern)
        {
            String p = pattern;
            p = p.Replace(".", "\ufffd");
            p = p.Replace("?", ".").Replace("*", @".*");
            p = p.Replace("\ufffd", "\\.");
            p = String.Concat("^", p, "$");
            Debug.WriteLine(2, "Regex: {0} => {1}", pattern, p);
            return new Regex(p);
        }
    }

    partial class UnixV5 : IFileSystemAuto
    {
        public static TestDelegate GetTest()
        {
            return UnixV5.Test;
        }

        // level 0 - check basic volume parameters (return required block size and volume type)
        // level 1 - check boot block (return volume size and type)
        // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
        // level 3 - check file headers (aka inodes) (return file system size and type)
        // level 4 - check directory structure (return file system size and type)
        // level 5 - check file header allocation (return file system size and type)
        // level 6 - check data block allocation (return file system size and type)
        public static Boolean Test(Volume volume, Int32 level, out Int32 size, out Type type)
        {
            Int32 BLOCK_SIZE = 512;
            Int32 ROOT_INUM = 1;
            Int32 INOPB = 16;

            // level 0 - check basic volume parameters (return required block size and volume type)
            size = BLOCK_SIZE;
            type = typeof(Volume);
            if (volume == null) return false;
            if (volume.BlockSize != size) return Debug.WriteInfo(false, "UnixV5.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            size = -1; // Unix V5 doesn't support disk labels
            if (volume.BlockCount < 1) return Debug.WriteInfo(false, "UnixV5.Test: volume too small to contain boot block");
            if (level == 1) return true;

            // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
            type = null;
            if (volume.BlockCount < 2) return Debug.WriteInfo(false, "UnixV5.Test: volume too small to contain super-block");
            Block SB = volume[1]; // super-block
            Int32 bp = 0;
            Int32 isize = SB.GetUInt16L(ref bp); // SB[0] - number of blocks used for inodes
            if (volume.BlockCount < isize + 2) return Debug.WriteInfo(false, "UnixV5.Test: volume too small to contain i-node list (blocks {0:D0}-{1:D0} > {2:D0})", 2, isize + 1, volume.BlockCount - 1);
            Int32 fsize = SB.GetUInt16L(ref bp); // SB[2] - file system size (in blocks)
            if (isize + 2 > fsize) return Debug.WriteInfo(false, "UnixV5.Test: super-block i-list exceeds file system size ({0:D0} > {1:D0})", isize + 2, fsize);
            Int32 n = SB.GetUInt16L(ref bp); // SB[4] - number of blocks in super-block free block list
            if (n > 100) return Debug.WriteInfo(false, "UnixV5.Test: super-block free block count invalid (is {0:D0}, require 0 <= n <= 100)", n);
            Int32 p = SB.GetUInt16L(bp); // SB[6] - free block chain next pointer
            if ((p != 0) && ((p < isize + 2) || (p >= fsize))) return Debug.WriteInfo(false, "UnixV5.Test: super-block free chain pointer invalid (is {0:D0}, require {1:D0} <= n < {2:D0})", p, isize + 2, fsize);
            for (Int32 i = 1; i < n; i++) // SB[8] - free block list
            {
                if (((p = SB.GetUInt16L(bp + 2 * i)) < isize + 2) || (p >= fsize)) return Debug.WriteInfo(false, "UnixV5.Test: super-block free block {0:D0} invalid (is {1:D0}, require {2:D0} <= n < {3:D0})", i, p, isize + 2, fsize);
            }
            bp += 100 * 2;
            n = SB.GetUInt16L(ref bp); // SB[206] - number of inodes in super-block free inode list
            if (n > 100) return Debug.WriteInfo(false, "UnixV5.Test: super-block free i-node count invalid (is {0:D0}, require 0 <= n <= 100)", n);
            for (Int32 i = 0; i < n; i++)
            {
                if (((p = SB.GetUInt16L(bp + 2 * i)) < 1) || (p > isize * INOPB)) return Debug.WriteInfo(false, "UnixV5.Test: super-block free i-number {0:D0} invalid (is {1:D0}, require 1 <= n < {2:D0})", i, p, isize * INOPB);
            }
            size = fsize;
            type = typeof(UnixV5);
            if (level == 2) return Debug.WriteInfo(true, "UnixV5.Test: file system size: {0:D0} blocks ({1:D0} i-nodes in blocks 2-{2:D0}, data in blocks {3:D0}-{4:D0}", fsize, isize * INOPB, isize + 1, isize + 2, fsize - 1);

            // level 3 - check file headers (aka inodes) (return file system size and type)
            Inode iNode;
            for (UInt16 iNum = 1; iNum <= isize * INOPB; iNum++)
            {
                iNode = new InodeV5(volume, iNum);
                if ((iNode.Type != ' ') && (iNode.nlinks == 0)) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} is used but has zero link count", iNum);
                //if ((iNode.Type == ' ') && (iNode.nlinks != 0)) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} is free but has non-zero link count {1:D0}", iNum, iNode.nlinks);
                if ((iNode.size < 0) || (iNode.size > 1048576)) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} size invalid (is {1:D0}, require 0 <= n <= 1048576)", iNum, iNode.size);
                if ((iNode.Type == ' ') && (iNode.size != 0)) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} is free but has non-zero size {1:D0}", iNum, iNode.size);
                if (((iNode.flags & 0x1000) == 0) && (iNode.size > 4096)) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} size exceeds small file limit (is {1:D0}, require n <= 4096)", iNum, iNode.size);
                if ((iNode.Type != '-') && (iNode.Type != 'd')) continue; // only check blocks for file and directory i-nodes
                for (Int32 i = 0; i < 8; i++)
                {
                    if ((p = iNode.addr[i]) == 0) continue;
                    if ((p < isize + 2) || (p >= fsize)) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, isize + 2, fsize);
                    if (p >= volume.BlockCount) return Debug.WriteInfo(false, "UnixV5.Test: volume too small to contain i-node {0:D0} {1} block {2:D0}", iNum, ((iNode.flags & 0x1000) == 0) ? "data" : "indirect", p);
                    if ((iNode.flags & 0x1000) != 0)
                    {
                        Block B = volume[p];
                        for (Int32 j = 0; j < 256; j++)
                        {
                            if ((p = B.GetUInt16L(2 * j)) == 0) continue;
                            if ((p < isize + 2) || (p >= fsize)) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, isize + 2, fsize);
                            if (p >= volume.BlockCount) return Debug.WriteInfo(false, "UnixV5.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                        }
                    }
                }
            }
            if (level == 3) return true;

            // level 4 - check directory structure (return file system size and type)
            iNode = new InodeV5(volume, ROOT_INUM);
            if (iNode.Type != 'd') return Debug.WriteInfo(false, "UnixV5.Test: root directory i-node mode invalid (is 0x{0:x4}, require 0xcnnn)", iNode.flags);
            UInt16[] IMap = new UInt16[isize * INOPB + 1]; // inode usage map
            Queue<Int32> DirList = new Queue<Int32>(); // queue of directories to examine
            DirList.Enqueue((ROOT_INUM << 16) + ROOT_INUM); // parent inum in high word, directory inum in low word (root is its own parent)
            while (DirList.Count != 0)
            {
                Int32 dNum = DirList.Dequeue();
                Int32 pNum = dNum >> 16;
                dNum &= 0xffff;
                if (IMap[dNum] != 0) return Debug.WriteInfo(false, "UnixV5.Test: directory i-node {0:D0} appears more than once in directory structure", dNum);
                IMap[dNum]++; // assume a link to this directory from its parent (root directory gets fixed later)
                Byte[] buf = ReadFile(new InodeV5(volume, dNum), BLOCK_SIZE);
                Boolean df = false;
                Boolean ddf = false;
                for (bp = 0; bp < buf.Length; bp += 16)
                {
                    UInt16 iNum = Buffer.GetUInt16L(buf, bp);
                    if (iNum == 0) continue;
                    String name = Buffer.GetCString(buf, bp + 2, 14, Encoding.ASCII);
                    if (String.Compare(name, ".") == 0)
                    {
                        if (iNum != dNum) return Debug.WriteInfo(false, "UnixV5.Test: in directory i={0:D0}, entry \".\" does not refer to itself (is {1:D0}, require {2:D0})", dNum, iNum, dNum);
                        IMap[iNum]++;
                        df = true;
                    }
                    else if (String.Compare(name, "..") == 0)
                    {
                        if (iNum != pNum) return Debug.WriteInfo(false, "UnixV5.Test: in directory i={0:D0}, entry \"..\" does not refer to parent (is {1:D0}, require {2:D0})", dNum, iNum, pNum);
                        IMap[iNum]++;
                        ddf = true;
                    }
                    else if ((iNum < 2) || (iNum > isize * INOPB))
                    {
                        return Debug.WriteInfo(false, "UnixV5.Test: in directory i={0:D0}, entry \"{1}\" has invalid i-number (is {2:D0}, require 2 <= n <= {3:D0})", dNum, name, iNum, isize * INOPB);
                    }
                    else if ((iNode = new InodeV5(volume, iNum)).Type == 'd') // directory
                    {
                        DirList.Enqueue((dNum << 16) + iNum);
                    }
                    else // non-directory
                    {
                        IMap[iNum]++;
                    }
                }
                if (!df) return Debug.WriteInfo(false, "UnixV5.Test: in directory i={0:D0}, entry \".\" is missing", dNum);
                if (!ddf) return Debug.WriteInfo(false, "UnixV5.Test: in directory i={0:D0}, entry \"..\" is missing", dNum);
            }
            IMap[ROOT_INUM]--; // root directory has no parent, so back out the assumed parent link
            if (level == 4) return true;

            // level 5 - check file header allocation (return file system size and type)
            for (UInt16 iNum = 1; iNum <= isize * INOPB; iNum++)
            {
                iNode = new InodeV5(volume, iNum);
                if ((iNode.Type == ' ') && (IMap[iNum] != 0)) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} is free but has {1:D0} link(s)", iNum, IMap[iNum]);
                if ((iNode.Type != ' ') && (IMap[iNum] == 0)) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} is used but has no links", iNum);
                if ((iNode.Type != ' ') && (IMap[iNum] != iNode.nlinks)) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} link count mismatch (is {1:D0}, expect {2:D0})", iNum, iNode.nlinks, IMap[iNum]);
            }
            // also check super-block free list
            bp = 206;
            n = SB.GetUInt16L(ref bp);
            for (Int32 i = 0; i < n; i++)
            {
                UInt16 iNum = SB.GetUInt16L(ref bp);
                if (IMap[iNum] != 0) return Debug.WriteInfo(false, "UnixV5.Test: i-node {0:D0} is in super-block free list, but has {1:D0} link(s)", iNum, IMap[iNum]);
            }
            if (level == 5) return true;

            // level 6 - check data block allocation (return file system size and type)
            UInt16[] BMap = new UInt16[fsize]; // block usage map
            for (UInt16 iNum = 1; iNum <= isize * INOPB; iNum++)
            {
                iNode = new InodeV5(volume, iNum);
                if ((iNode.Type != '-') && (iNode.Type != 'd')) continue; // only file and directory i-nodes have blocks allocated
                n = (iNode.size + 511) / 512; // number of blocks required for file
                // mark data blocks used
                for (Int32 i = 0; i < n; i++)
                {
                    if ((p = iNode[i]) == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV5.Test: data block #{0:D0} ({1:D0}) of i-node {2:D0} is also allocated to i-node {3:D0}", i, p, iNum, BMap[p]);
                    BMap[p] = iNum;
                }
                if ((iNode.flags & 0x1000) == 0) continue; // small files have no indirect blocks
                // mark indirect blocks used
                for (Int32 i = 0; i < 8; i++)
                {
                    if ((p = iNode.addr[i]) == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV5.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                    BMap[p] = iNum;
                }
            }
            // mark used blocks with 1
            for (Int32 i = 0; i < isize + 2; i++) BMap[i] = 1;
            for (Int32 i = isize + 2; i < fsize; i++) if (BMap[i] != 0) BMap[i] = 1;
            // mark free blocks with 2
            bp = 4;
            n = SB.GetUInt16L(ref bp); // number of blocks in super-block free block list
            if ((p = SB.GetUInt16L(ref bp)) != 0) // free block chain next pointer
            {
                if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV5.Test: list block {0:D0} in super-block free list is allocated", p);
                BMap[p] = 2;
            }
            for (Int32 i = 1; i < n; i++)
            {
                Int32 q = SB.GetUInt16L(ref bp);
                if (BMap[q] != 0) return Debug.WriteInfo(false, "UnixV5.Test: data block {0:D0} in super-block free list is allocated", q);
                BMap[q] = 2;
            }
            // enumerate blocks in free block chain
            while (p != 0)
            {
                Block B = volume[p];
                bp = 0;
                n = B.GetUInt16L(ref bp); // number of entries listed in this block
                if ((p = B.GetUInt16L(ref bp)) != 0)
                {
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV5.Test: list block {0:D0} in free block chain is allocated", p);
                    BMap[p] = 2;
                }
                for (Int32 i = 1; i < n; i++)
                {
                    Int32 q = B.GetUInt16L(ref bp);
                    if ((q < isize + 2) || (q >= fsize)) return Debug.WriteInfo(false, "UnixV5.Test: data block {0:D0} in free block chain falls outside volume block range (require {1:D0} <= n < {2:D0})", q, isize + 2, fsize);
                    if (BMap[q] != 0) return Debug.WriteInfo(false, "UnixV5.Test: data block {0:D0} in free block chain is allocated", q);
                    BMap[q] = 2;
                }
            }
            // unmarked blocks are lost -- not allocated and not in free list
            for (Int32 i = 0; i < fsize; i++)
            {
                if (BMap[i] == 0) return Debug.WriteInfo(false, "UnixV5.Test: block {0:D0} is not allocated and not in free list", i);
            }
            if (level == 6) return true;

            return false;
        }
    }


    partial class UnixV6 : UnixV5
    {
        protected class InodeV6 : InodeV5
        {
            public InodeV6(Volume volume, Int32 iNumber) : base(volume, iNumber)
            {
            }

            public override Int32 this[Int32 blockNum]
            {
                get
                {
                    if ((flags & 0x1000) == 0) return addr[blockNum];
                    Int32 n = blockNum / 256;
                    if (n < 7) n = addr[n];
                    else if (addr[7] == 0) return 0;
                    else n = mVol[addr[7]].GetUInt16L(2 * (n - 7));
                    return mVol[n].GetUInt16L(2 * (blockNum % 256));
                }
            }
        }

        public UnixV6(Volume volume)
        {
            mVol = volume;
            mType = "Unix/V6";
            mDirNode = new InodeV6(volume, ROOT_INUM);
            mDir = "/";
        }

        protected override Inode GetInode(Int32 iNumber)
        {
            return new InodeV6(mVol, iNumber);
        }
    }

    partial class UnixV6 : IFileSystemAuto
    {
        public static new TestDelegate GetTest()
        {
            return UnixV6.Test;
        }

        // level 0 - check basic volume parameters (return required block size and volume type)
        // level 1 - check boot block (return volume size and type)
        // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
        // level 3 - check file headers (aka inodes) (return file system size and type)
        // level 4 - check directory structure (return file system size and type)
        // level 5 - check file header allocation (return file system size and type)
        // level 6 - check data block allocation (return file system size and type)
        public static new Boolean Test(Volume volume, Int32 level, out Int32 size, out Type type)
        {
            Int32 BLOCK_SIZE = 512;
            Int32 ROOT_INUM = 1;
            Int32 INOPB = 16;

            // level 0 - check basic volume parameters (return required block size and volume type)
            size = BLOCK_SIZE;
            type = typeof(Volume);
            if (volume == null) return false;
            if (volume.BlockSize != size) return Debug.WriteInfo(false, "UnixV6.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            size = -1; // Unix V6 doesn't support disk labels
            if (volume.BlockCount < 1) return Debug.WriteInfo(false, "UnixV6.Test: volume too small to contain boot block");
            if (level == 1) return true;

            // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
            type = null;
            if (volume.BlockCount < 2) return Debug.WriteInfo(false, "UnixV6.Test: volume too small to contain super-block");
            Block SB = volume[1]; // super-block
            Int32 bp = 0;
            Int32 isize = SB.GetUInt16L(ref bp); // SB[0] - number of blocks used for inodes
            if (volume.BlockCount < isize + 2) return Debug.WriteInfo(false, "UnixV6.Test: volume too small to contain i-node list (blocks {0:D0}-{1:D0} > {2:D0})", 2, isize + 1, volume.BlockCount - 1);
            Int32 fsize = SB.GetUInt16L(ref bp); // SB[2] - file system size (in blocks)
            if (isize + 2 > fsize) return Debug.WriteInfo(false, "UnixV6.Test: super-block i-list exceeds file system size ({0:D0} > {1:D0})", isize + 2, fsize);
            Int32 n = SB.GetUInt16L(ref bp); // SB[4] - number of blocks in super-block free block list
            if (n > 100) return Debug.WriteInfo(false, "UnixV6.Test: super-block free block count invalid (is {0:D0}, require 0 <= n <= 100)", n);
            Int32 p = SB.GetUInt16L(bp); // SB[6] - free block chain next pointer
            if ((p != 0) && ((p < isize + 2) || (p >= fsize))) return Debug.WriteInfo(false, "UnixV6.Test: super-block free chain pointer invalid (is {0:D0}, require {1:D0} <= n < {2:D0})", p, isize + 2, fsize);
            for (Int32 i = 1; i < n; i++) // SB[8] - free block list
            {
                if (((p = SB.GetUInt16L(bp + 2 * i)) < isize + 2) || (p >= fsize)) return Debug.WriteInfo(false, "UnixV6.Test: super-block free block {0:D0} invalid (is {1:D0}, require {2:D0} <= n < {3:D0})", i, p, isize + 2, fsize);
            }
            bp += 100 * 2;
            n = SB.GetUInt16L(ref bp); // SB[206] - number of inodes in super-block free inode list
            if (n > 100) return Debug.WriteInfo(false, "UnixV6.Test: super-block free i-node count invalid (is {0:D0}, require 0 <= n <= 100)", n);
            for (Int32 i = 0; i < n; i++)
            {
                if (((p = SB.GetUInt16L(bp + 2 * i)) < 1) || (p > isize * INOPB)) return Debug.WriteInfo(false, "UnixV6.Test: super-block free i-number {0:D0} invalid (is {1:D0}, require 1 <= n < {2:D0})", i, p, isize * INOPB);
            }
            size = fsize;
            type = typeof(UnixV6);
            if (level == 2) return Debug.WriteInfo(true, "UnixV6.Test: file system size: {0:D0} blocks ({1:D0} i-nodes in blocks 2-{2:D0}, data in blocks {3:D0}-{4:D0}", fsize, isize * INOPB, isize + 1, isize + 2, fsize - 1);

            // level 3 - check file headers (aka inodes) (return file system size and type)
            Inode iNode;
            for (UInt16 iNum = 1; iNum <= isize * INOPB; iNum++)
            {
                iNode = new InodeV6(volume, iNum);
                if ((iNode.Type != ' ') && (iNode.nlinks == 0)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} is used but has zero link count", iNum);
                //if ((iNode.Type == ' ') && (iNode.nlinks != 0)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} is free but has non-zero link count {1:D0}", iNum, iNode.nlinks);
                // since iNode.size is a 24-bit number, the below test can't actually ever fail
                if ((iNode.size < 0) || (iNode.size > 16777215)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} size invalid (is {1:D0}, require 0 <= n <= 16777215)", iNum, iNode.size);
                if ((iNode.Type == ' ') && (iNode.size != 0)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} is free but has non-zero size {1:D0}", iNum, iNode.size);
                if (((iNode.flags & 0x1000) == 0) && (iNode.size > 4096)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} size exceeds small file limit (is {1:D0}, require n <= 4096)", iNum, iNode.size);
                if ((iNode.Type != '-') && (iNode.Type != 'd')) continue; // only check blocks for file and directory i-nodes
                for (Int32 i = 0; i < 8; i++)
                {
                    if ((p = iNode.addr[i]) == 0) continue;
                    if ((p < isize + 2) || (p >= fsize)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, isize + 2, fsize);
                    if (p >= volume.BlockCount) return Debug.WriteInfo(false, "UnixV6.Test: volume too small to contain i-node {0:D0} {1} block {2:D0}", iNum, ((iNode.flags & 0x1000) == 0) ? "data" : "indirect", p);
                    if ((iNode.flags & 0x1000) != 0)
                    {
                        Block B = volume[p];
                        for (Int32 j = 0; j < 256; j++)
                        {
                            if ((p = B.GetUInt16L(2 * j)) == 0) continue;
                            if ((p < isize + 2) || (p >= fsize)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, isize + 2, fsize);
                            if (p >= volume.BlockCount) return Debug.WriteInfo(false, "UnixV6.Test: volume too small to contain i-node {0:D0} {1} block {2:D0}", iNum, (i < 7) ? "data" : "indirect", p);
                            if (i == 7)
                            {
                                Block B2 = volume[p];
                                for (Int32 k = 0; k < 256; k++)
                                {
                                    if ((p = B2.GetUInt16L(2 * k)) == 0) continue;
                                    if ((p < isize + 2) || (p >= fsize)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, isize + 2, fsize);
                                    if (p >= volume.BlockCount) return Debug.WriteInfo(false, "UnixV6.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                                }
                            }
                        }
                    }
                }
            }
            if (level == 3) return true;

            // level 4 - check directory structure (return file system size and type)
            iNode = new InodeV6(volume, ROOT_INUM);
            if (iNode.Type != 'd') return Debug.WriteInfo(false, "UnixV6.Test: root directory i-node mode invalid (is 0x{0:x4}, require 0xcnnn)", iNode.flags);
            UInt16[] IMap = new UInt16[isize * INOPB + 1]; // inode usage map
            Queue<Int32> DirList = new Queue<Int32>(); // queue of directories to examine
            DirList.Enqueue((ROOT_INUM << 16) + ROOT_INUM); // parent inum in high word, directory inum in low word (root is its own parent)
            while (DirList.Count != 0)
            {
                Int32 dNum = DirList.Dequeue();
                Int32 pNum = dNum >> 16;
                dNum &= 0xffff;
                if (IMap[dNum] != 0) return Debug.WriteInfo(false, "UnixV6.Test: directory i-node {0:D0} appears more than once in directory structure", dNum);
                IMap[dNum]++; // assume a link to this directory from its parent (root directory gets fixed later)
                Byte[] buf = ReadFile(new InodeV6(volume, dNum), BLOCK_SIZE);
                Boolean df = false;
                Boolean ddf = false;
                for (bp = 0; bp < buf.Length; bp += 16)
                {
                    UInt16 iNum = Buffer.GetUInt16L(buf, bp);
                    if (iNum == 0) continue;
                    String name = Buffer.GetCString(buf, bp + 2, 14, Encoding.ASCII);
                    if (String.Compare(name, ".") == 0)
                    {
                        if (iNum != dNum) return Debug.WriteInfo(false, "UnixV6.Test: in directory i={0:D0}, entry \".\" does not refer to itself (is {1:D0}, require {2:D0})", dNum, iNum, dNum);
                        IMap[iNum]++;
                        df = true;
                    }
                    else if (String.Compare(name, "..") == 0)
                    {
                        if (iNum != pNum) return Debug.WriteInfo(false, "UnixV6.Test: in directory i={0:D0}, entry \"..\" does not refer to parent (is {1:D0}, require {2:D0})", dNum, iNum, pNum);
                        IMap[iNum]++;
                        ddf = true;
                    }
                    else if ((iNum < 2) || (iNum > isize * INOPB))
                    {
                        return Debug.WriteInfo(false, "UnixV6.Test: in directory i={0:D0}, entry \"{1}\" has invalid i-number (is {2:D0}, require 2 <= n <= {3:D0})", dNum, name, iNum, isize * INOPB);
                    }
                    else if ((iNode = new InodeV6(volume, iNum)).Type == 'd') // directory
                    {
                        DirList.Enqueue((dNum << 16) + iNum);
                    }
                    else // non-directory
                    {
                        IMap[iNum]++;
                    }
                }
                if (!df) return Debug.WriteInfo(false, "UnixV6.Test: in directory i={0:D0}, entry \".\" is missing", dNum);
                if (!ddf) return Debug.WriteInfo(false, "UnixV6.Test: in directory i={0:D0}, entry \"..\" is missing", dNum);
            }
            IMap[ROOT_INUM]--; // root directory has no parent, so back out the assumed parent link
            if (level == 4) return true;

            // level 5 - check file header allocation (return file system size and type)
            for (UInt16 iNum = 1; iNum <= isize * INOPB; iNum++)
            {
                iNode = new InodeV6(volume, iNum);
                if ((iNode.Type == ' ') && (IMap[iNum] != 0)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} is free but has {1:D0} link(s)", iNum, IMap[iNum]);
                if ((iNode.Type != ' ') && (IMap[iNum] == 0)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} is used but has no links", iNum);
                if ((iNode.Type != ' ') && (IMap[iNum] != iNode.nlinks)) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} link count mismatch (is {1:D0}, expect {2:D0})", iNum, iNode.nlinks, IMap[iNum]);
            }
            // also check super-block free list
            bp = 206;
            n = SB.GetUInt16L(ref bp);
            for (Int32 i = 0; i < n; i++)
            {
                UInt16 iNum = SB.GetUInt16L(ref bp);
                if (IMap[iNum] != 0) return Debug.WriteInfo(false, "UnixV6.Test: i-node {0:D0} is in super-block free list, but has {1:D0} link(s)", iNum, IMap[iNum]);
            }
            if (level == 5) return true;

            // level 6 - check data block allocation (return file system size and type)
            UInt16[] BMap = new UInt16[fsize]; // block usage map
            for (UInt16 iNum = 1; iNum <= isize * INOPB; iNum++)
            {
                iNode = new InodeV6(volume, iNum);
                if ((iNode.Type != '-') && (iNode.Type != 'd')) continue; // only file and directory i-nodes have blocks allocated
                n = (iNode.size + 511) / 512; // number of blocks required for file
                // mark data blocks used
                for (Int32 i = 0; i < n; i++)
                {
                    if ((p = iNode[i]) == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV6.Test: data block #{0:D0} ({1:D0}) of i-node {2:D0} is also allocated to i-node {3:D0}", i, p, iNum, BMap[p]);
                    BMap[p] = iNum;
                }
                if ((iNode.flags & 0x1000) == 0) continue; // small files have no indirect blocks
                // mark indirect blocks used
                for (Int32 i = 0; i < 8; i++)
                {
                    if ((p = iNode.addr[i]) == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV6.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                    BMap[p] = iNum;
                    if (i == 7)
                    {
                        // mark double-indirect blocks used
                        Block B = volume[p];
                        for (Int32 j = 0; j < 256; j++)
                        {
                            if ((p = B.GetUInt16L(2 * j)) == 0) continue;
                            if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV6.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                            BMap[p] = iNum;
                        }
                    }
                }
            }
            // mark used blocks with 1
            for (Int32 i = 0; i < isize + 2; i++) BMap[i] = 1;
            for (Int32 i = isize + 2; i < fsize; i++) if (BMap[i] != 0) BMap[i] = 1;
            // mark free blocks with 2
            bp = 4;
            n = SB.GetUInt16L(ref bp); // number of blocks in super-block free block list
            if ((p = SB.GetUInt16L(ref bp)) != 0) // free block chain next pointer
            {
                if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV6.Test: list block {0:D0} in super-block free list is allocated", p);
                BMap[p] = 2;
            }
            for (Int32 i = 1; i < n; i++)
            {
                Int32 q = SB.GetUInt16L(ref bp);
                if (BMap[q] != 0) return Debug.WriteInfo(false, "UnixV6.Test: data block {0:D0} in super-block free list is allocated", q);
                BMap[q] = 2;
            }
            // enumerate blocks in free block chain
            while (p != 0)
            {
                Block B = volume[p];
                bp = 0;
                n = B.GetUInt16L(ref bp); // number of entries listed in this block
                if ((p = B.GetUInt16L(ref bp)) != 0)
                {
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV6.Test: list block {0:D0} in free block chain is allocated", p);
                    BMap[p] = 2;
                }
                for (Int32 i = 1; i < n; i++)
                {
                    Int32 q = B.GetUInt16L(ref bp);
                    if ((q < isize + 2) || (q >= fsize)) return Debug.WriteInfo(false, "UnixV6.Test: data block {0:D0} in free block chain falls outside volume block range (require {1:D0} <= n < {2:D0})", q, isize + 2, fsize);
                    if (BMap[q] != 0) return Debug.WriteInfo(false, "UnixV6.Test: data block {0:D0} in free block chain is allocated", q);
                    BMap[q] = 2;
                }
                p = B.GetUInt16L(2);
            }
            // unmarked blocks are lost -- not allocated and not in free list
            for (Int32 i = 0; i < fsize; i++)
            {
                if (BMap[i] == 0) return Debug.WriteInfo(false, "UnixV6.Test: block {0:D0} is not allocated and not in free list", i);
            }
            if (level == 6) return true;

            return false;
        }
    }


    // v7 super block
    //  0   s_isize - reserved size (boot + superblock + inodes)
    //  2   s_fsize - file system size
    //  6   s_nfree - number of free blocks listed in superblock (0-50)
    //  8   s_free  - free block list
    //  208 s_ninode - number of free inodes listed in superblock (0-100)
    //  210 s_ifree - free inode list
    //  410 s_flock
    //  411 s_ilock
    //  412 s_fmod
    //  413 s_ronly
    //  414 s_time
    //  418
    //
    // v7 i-node
    //  0   di_mode     0x0fff mode flags, 0xf000 type mask, types: 8=file, 4=dir, 2=cdev, 6=bdev, 3=mcdev, 7=mbdev
    //  2   di_nlink
    //  4   di_uid
    //  6   di_gid
    //  8   di_size
    //  12  di_addr
    //  52  di_atime
    //  56  di_mtime
    //  60  di_ctime

    partial class UnixV7 : UnixV5
    {
        protected class InodeV7 : Inode
        {
            public InodeV7(Volume volume, Int32 iNumber) : base(volume, iNumber)
            {
                Block B = volume[2 + (iNumber - 1) / 8];
                Int32 offset = ((iNumber - 1) % 8) * 64;
                flags = B.GetUInt16L(ref offset);
                nlinks = B.GetInt16L(ref offset);
                uid = B.GetInt16L(ref offset);
                gid = B.GetInt16L(ref offset);
                size = B.GetInt32P(ref offset);
                addr = new Int32[13];
                for (Int32 i = 0; i < 13; i++)
                {
                    addr[i] = B.GetByte(ref offset) << 16;
                    addr[i] |= B.GetUInt16L(ref offset);
                }
                offset++;
                atime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
                mtime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
                ctime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
            }

            public override Char Type
            {
                get
                {
                    if ((flags & 0xf000) == 0x8000) return '-';
                    if ((flags & 0xf000) == 0x4000) return 'd';
                    if ((flags & 0xe000) == 0x2000) return 'c';
                    if ((flags & 0xe000) == 0x6000) return 'b';
                    if ((flags & 0xf000) == 0x0000) return ' ';
                    return '\0';
                }
            }

            public override string Mode
            {
                get
                {
                    StringBuilder buf = new StringBuilder(9);
                    buf.Append(((flags & 0x0100) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0080) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0040) == 0) ? '-' : ((flags & 0x0800) == 0) ? 'x' : 's');
                    buf.Append(((flags & 0x0020) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0010) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0008) == 0) ? '-' : ((flags & 0x0400) == 0) ? 'x' : 's');
                    buf.Append(((flags & 0x0004) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0002) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0001) == 0) ? '-' : ((flags & 0x0200) == 0) ? 'x' : 't');
                    return buf.ToString();
                }
            }

            public override Int32 this[Int32 blockNum]
            {
                get
                {
                    if (blockNum < 10)
                    {
                        return addr[blockNum]; // desired block pointer is in i-node
                    }
                    else
                    {
                        Int32 i;
                        if ((blockNum -= 10) < 128)
                        {
                            i = addr[10]; // desired block pointer is in block i
                        }
                        else
                        {
                            Int32 d;
                            if ((blockNum -= 128) < 128 * 128)
                            {
                                d = addr[11]; // indirect block pointer i is in block d
                            }
                            else
                            {
                                Int32 t = addr[12]; // double-indirect block pointer d is in block t
                                if (t == 0) return 0;
                                d = mVol[t].GetInt32P(4 * (blockNum -= 128 * 128) / (128 * 128));
                                blockNum %= 128 * 128;
                            }
                            if (d == 0) return 0;
                            i = mVol[d].GetInt32P(4 * blockNum / 128);
                            blockNum %= 128;
                        }
                        if (i == 0) return 0;
                        return mVol[i].GetInt32P(4 * blockNum);
                    }
                }
            }
        }

        public UnixV7(Volume volume)
        {
            ROOT_INUM = 2;
            INOPB = 8;
            ISIZE = 64;
            mVol = volume;
            mType = "Unix/V7";
            mDirNode = new InodeV7(volume, ROOT_INUM);
            mDir = "/";
        }

        protected override Inode GetInode(Int32 iNumber)
        {
            return new InodeV7(mVol, iNumber);
        }
    }

    partial class UnixV7 : IFileSystemAuto
    {
        public static new TestDelegate GetTest()
        {
            return UnixV7.Test;
        }

        // level 0 - check basic volume parameters (return required block size and volume type)
        // level 1 - check boot block (return volume size and type)
        // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
        // level 3 - check file headers (aka inodes) (return file system size and type)
        // level 4 - check directory structure (return file system size and type)
        // level 5 - check file header allocation (return file system size and type)
        // level 6 - check data block allocation (return file system size and type)
        public static new Boolean Test(Volume volume, Int32 level, out Int32 size, out Type type)
        {
            Int32 BLOCK_SIZE = 512;
            Int32 ROOT_INUM = 2;
            Int32 INOPB = 8;

            // level 0 - check basic volume parameters (return required block size and volume type)
            size = BLOCK_SIZE;
            type = typeof(Volume);
            if (volume == null) return false;
            if (volume.BlockSize != size) return Debug.WriteInfo(false, "UnixV7.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            size = -1; // Unix V7 doesn't support disk labels
            if (volume.BlockCount < 1) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain boot block (block 0 > {0:D0})", volume.BlockCount - 1);
            if (level == 1) return true;

            // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
            type = null;
            if (volume.BlockCount < 2) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain super-block (block 1 > {0:D0})", volume.BlockCount - 1);
            Block SB = volume[1]; // super-block
            Int32 bp = 0;
            Int32 s_isize = SB.GetUInt16L(ref bp); // SB[0] - number of reserved non-data blocks (boot, super block, inodes)
            if (volume.BlockCount < s_isize) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node list (blocks {0:D0}-{1:D0} > {2:D0})", 2, s_isize - 1, volume.BlockCount - 1);
            Int32 s_fsize = SB.GetInt32P(ref bp); // SB[2] - file system size (in blocks)
            if (s_isize > s_fsize) return Debug.WriteInfo(false, "UnixV7.Test: super-block i-list exceeds file system size ({0:D0} > {1:D0})", s_isize, s_fsize);
            Int32 n = SB.GetUInt16L(ref bp); // SB[6] - number of blocks in super-block free block list
            if (n > 50) return Debug.WriteInfo(false, "UnixV7.Test: super-block free block count invalid (is {0:D0}, require 0 <= n <= 50)", n);
            Int32 p = SB.GetInt32P(bp); // SB[8] - free block chain next pointer
            if ((p != 0) && ((p < s_isize) || (p >= s_fsize))) return Debug.WriteInfo(false, "UnixV7.Test: super-block free chain pointer invalid (is {0:D0}, require {1:D0} <= n < {2:D0})", p, s_isize, s_fsize);
            for (Int32 i = 1; i < n; i++) // SB[10] - free block list
            {
                if (((p = SB.GetInt32P(bp + 4 * i)) < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: super-block free block {0:D0} invalid (is {1:D0}, require {2:D0} <= n < {3:D0})", i, p, s_isize, s_fsize);
            }
            bp += 50 * 4;
            n = SB.GetUInt16L(ref bp); // SB[208] - number of inodes in super-block free inode list
            if (n > 100) return Debug.WriteInfo(false, "UnixV7.Test: super-block free i-node count invalid (is {0:D0}, require 0 <= n <= 100)", n);
            for (Int32 i = 0; i < n; i++)
            {
                if (((p = SB.GetUInt16L(bp + 2 * i)) < 1) || (p > (s_isize - 2) * INOPB)) return Debug.WriteInfo(false, "UnixV7.Test: super-block free i-number {0:D0} invalid (is {1:D0}, require 1 <= n < {2:D0})", i, p, (s_isize - 2) * INOPB);
            }
            size = s_fsize;
            type = typeof(UnixV7);
            if (level == 2) return Debug.WriteInfo(true, "UnixV7.Test: file system size: {0:D0} blocks ({1:D0} i-nodes in blocks 2-{2:D0}, data in blocks {3:D0}-{4:D0}", s_fsize, (s_isize - 2) * INOPB, s_isize - 1, s_isize, s_fsize - 1);

            // level 3 - check file headers (aka inodes) (return file system size and type)
            Inode iNode;
            for (UInt16 iNum = 1; iNum <= (s_isize - 2) * INOPB; iNum++)
            {
                iNode = new InodeV7(volume, iNum);
                if (iNode.Type == '\0') return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} mode 0{1} (octal) invalid", iNum, Convert.ToString(iNode.flags, 8));
                if (iNode.nlinks < 0) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} link count invalid (is {1:D0}, require n >= 0)", iNum, iNode.nlinks);
                // special case: i-node 1 is used but not linked
                if ((iNode.Type != ' ') && (iNode.nlinks == 0) && (iNum > 1)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is used but has zero link count", iNum);
                if ((iNode.Type == ' ') && (iNode.nlinks != 0)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is free but has non-zero link count {1:D0}", iNum, iNode.nlinks);
                if ((iNode.size < 0) || (iNode.size > 1082201088)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} size invalid (is {1:D0}, require 0 <= n <= 1082201088)", iNum, iNode.size);
                if ((iNode.Type == ' ') && (iNode.size != 0)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is free but has non-zero size {1:D0}", iNum, iNode.size);
                if ((iNode.Type != '-') && (iNode.Type != 'd')) continue; // only check blocks for file and directory i-nodes
                // verify validity of i-node direct block pointers
                for (Int32 i = 0; i < 10; i++)
                {
                    if ((p = iNode.addr[i]) == 0) continue;
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                }
                if ((p = iNode.addr[10]) != 0)
                {
                    // verify validity of indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 128; i++)
                    {
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                    }
                }
                if ((p = iNode.addr[11]) != 0)
                {
                    // verify validity of double-indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 128; i++)
                    {
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                        Block B2 = volume[p];
                        for (Int32 j = 0; j < 128; j++)
                        {
                            if ((p = B2.GetInt32P(4 * j)) == 0) continue;
                            if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                            if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                        }
                    }
                }
                if ((p = iNode.addr[12]) != 0)
                {
                    // verify validity of triple-indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 128; i++)
                    {
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                        Block B2 = volume[p];
                        for (Int32 j = 0; j < 128; j++)
                        {
                            if ((p = B2.GetInt32P(4 * j)) == 0) continue;
                            if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                            if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                            Block B3 = volume[p];
                            for (Int32 k = 0; k < 128; k++)
                            {
                                if ((p = B3.GetInt32P(4 * k)) == 0) continue;
                                if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                                if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                            }
                        }
                    }
                }
            }
            if (level == 3) return true;

            // level 4 - check directory structure (return file system size and type)
            iNode = new InodeV7(volume, ROOT_INUM);
            if (iNode.Type != 'd') return Debug.WriteInfo(false, "UnixV7.Test: root directory i-node mode invalid (is 0x{0:x4}, require 0x4nnn)", iNode.flags);
            UInt16[] IMap = new UInt16[(s_isize - 2) * INOPB + 1]; // inode usage map
            Queue<Int32> DirList = new Queue<Int32>(); // queue of directories to examine
            DirList.Enqueue((ROOT_INUM << 16) + ROOT_INUM); // parent inum in high word, directory inum in low word (root is its own parent)
            while (DirList.Count != 0)
            {
                Int32 dNum = DirList.Dequeue();
                Int32 pNum = dNum >> 16;
                dNum &= 0xffff;
                if (IMap[dNum] != 0) return Debug.WriteInfo(false, "UnixV7.Test: directory i-node {0:D0} appears more than once in directory structure", dNum);
                IMap[dNum]++; // assume a link to this directory from its parent (root directory gets fixed later)
                Byte[] buf = ReadFile(new InodeV7(volume, dNum), BLOCK_SIZE);
                Boolean df = false;
                Boolean ddf = false;
                for (bp = 0; bp < buf.Length; bp += 16)
                {
                    UInt16 iNum = Buffer.GetUInt16L(buf, bp);
                    if (iNum == 0) continue;
                    String name = Buffer.GetCString(buf, bp + 2, 14, Encoding.ASCII);
                    if (String.Compare(name, ".") == 0)
                    {
                        if (iNum != dNum) return Debug.WriteInfo(false, "UnixV7.Test: in directory i={0:D0}, entry \".\" does not refer to itself (is {1:D0}, require {2:D0})", dNum, iNum, dNum);
                        IMap[iNum]++;
                        df = true;
                    }
                    else if (String.Compare(name, "..") == 0)
                    {
                        if (iNum != pNum) return Debug.WriteInfo(false, "UnixV7.Test: in directory i={0:D0}, entry \"..\" does not refer to parent (is {1:D0}, require {2:D0})", dNum, iNum, pNum);
                        IMap[iNum]++;
                        ddf = true;
                    }
                    else if ((iNum < 2) || (iNum > (s_isize - 2) * INOPB))
                    {
                        return Debug.WriteInfo(false, "UnixV7.Test: in directory i={0:D0}, entry \"{1}\" has invalid i-number (is {2:D0}, require 2 <= n <= {3:D0})", dNum, name, iNum, (s_isize - 2) * INOPB);
                    }
                    else if ((iNode = new InodeV7(volume, iNum)).Type == 'd') // directory
                    {
                        DirList.Enqueue((dNum << 16) + iNum);
                    }
                    else // non-directory
                    {
                        IMap[iNum]++;
                    }
                }
                if (!df) return Debug.WriteInfo(false, "UnixV7.Test: in directory i={0:D0}, entry \".\" is missing", dNum);
                if (!ddf) return Debug.WriteInfo(false, "UnixV7.Test: in directory i={0:D0}, entry \"..\" is missing", dNum);
            }
            IMap[ROOT_INUM]--; // root directory has no parent, so back out the assumed parent link
            if (level == 4) return true;

            // level 5 - check file header allocation (return file system size and type)
            for (UInt16 iNum = 1; iNum <= (s_isize - 2) * INOPB; iNum++)
            {
                iNode = new InodeV7(volume, iNum);
                if ((iNode.Type == ' ') && (IMap[iNum] != 0)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is free but has {1:D0} link(s)", iNum, IMap[iNum]);
                if ((iNode.Type != ' ') && (IMap[iNum] == 0)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is used but has no links", iNum);
                if ((iNode.Type != ' ') && (IMap[iNum] != iNode.nlinks)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} link count mismatch (is {1:D0}, expect {2:D0})", iNum, iNode.nlinks, IMap[iNum]);
            }
            // also check super-block free list
            bp = 208;
            n = SB.GetInt16L(ref bp);
            for (Int32 i = 0; i < n; i++)
            {
                UInt16 iNum = SB.GetUInt16L(ref bp);
                if (IMap[iNum] != 0) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is in super-block free list, but has {1:D0} link(s)", iNum, IMap[iNum]);
            }
            if (level == 5) return true;

            // level 6 - check data block allocation (return file system size and type)
            UInt16[] BMap = new UInt16[s_fsize]; // block usage map
            for (UInt16 iNum = 1; iNum <= (s_isize - 2) * INOPB; iNum++)
            {
                iNode = new InodeV7(volume, iNum);
                if ((iNode.Type != '-') && (iNode.Type != 'd')) continue; // only file and directory i-nodes have blocks allocated
                n = (iNode.size + 511) / 512; // number of blocks required for file
                // mark data blocks used
                for (Int32 i = 0; i < n; i++)
                {
                    if ((p = iNode[i]) == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV7.Test: data block #{0:D0} ({1:D0}) of i-node {2:D0} is also allocated to i-node {3:D0}", i, p, iNum, BMap[p]);
                    BMap[p] = iNum;
                }
                // mark indirect blocks used
                for (Int32 i = 10; i < 13; i++)
                {
                    p = iNode.addr[i];
                    if (p == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV7.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                    BMap[p] = iNum;
                    if (i == 10) continue;
                    Block B1 = volume[p];
                    for (Int32 j = 0; j < 128; j++)
                    {
                        p = B1.GetInt32P(4 * j);
                        if (p == 0) continue;
                        if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV7.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                        if (i == 11) continue;
                        Block B2 = volume[p];
                        for (Int32 k = 0; k < 128; k++)
                        {
                            p = B2.GetInt32P(4 * k);
                            if (p == 0) continue;
                            if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV7.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                        }
                    }
                }
            }
            // mark used blocks with 1
            for (Int32 i = 0; i < s_isize; i++) BMap[i] = 1;
            for (Int32 i = s_isize; i < s_fsize; i++) if (BMap[i] != 0) BMap[i] = 1;
            // mark free blocks with 2
            bp = 6;
            n = SB.GetInt16L(ref bp); // number of blocks in super-block free block list
            if ((p = SB.GetInt32P(ref bp)) != 0) // free block chain next pointer
            {
                if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV7.Test: block {0:D0} in super-block free list is allocated", p);
                BMap[p] = 2;
            }
            for (Int32 i = 1; i < n; i++)
            {
                p = SB.GetInt32P(ref bp);
                if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV7.Test: block {0:D0} in super-block free list is allocated", p);
                BMap[p] = 2;
            }
            // enumerate blocks in free block chain
            n = SB.GetInt32P(8);
            while (n != 0)
            {
                Block B = volume[n];
                bp = 0;
                if ((n = B.GetInt32P(ref bp)) != 0)
                {
                    if (BMap[n] != 0) return Debug.WriteInfo(false, "UnixV7.Test: block {0:D0} in super-block free list is allocated", n);
                    BMap[n] = 2;
                }
                for (Int32 i = 1; i < 50; i++)
                {
                    p = B.GetInt32P(ref bp);
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: block {0:D0} in free block chain falls outside volume block range (require {1:D0} <= n < {2:D0})", p, s_isize, s_fsize);
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV7.Test: block {0:D0} in free block chain is allocated", p);
                    BMap[p] = 2;
                }
            }
            // unmarked blocks are lost -- not allocated and not in free list
            for (Int32 i = 0; i < s_fsize; i++)
            {
                if (BMap[i] == 0) return Debug.WriteInfo(false, "UnixV7.Test: block {0:D0} is not allocated and not in free list", i);
            }
            if (level == 6) return true;

            return false;
        }
    }


    // 2.8BSD super block
    //  0   s_isize - reserved size (boot + superblock + inodes)
    //  2   s_fsize - file system size
    //  6   s_nfree - number of free blocks listed in superblock (0-50)
    //  8   s_free  - free block list
    //  208 s_ninode - number of free inodes listed in superblock (0-100)
    //  210 s_ifree - free inode list
    //  410 s_flock
    //  411 s_ilock
    //  412 s_fmod
    //  413 s_ronly
    //  414 s_time
    //  418
    //
    // 2.8BSD i-node
    //  0   di_mode     0x0fff mode flags, 0xf000 type mask, types: 8=file, 4=dir, 2=cdev, 6=bdev, a=symlink, c=socket
    //  2   di_nlink
    //  4   di_uid
    //  6   di_gid
    //  8   di_size
    //  12  di_addr
    //  52  di_atime
    //  56  di_mtime
    //  60  di_ctime

    partial class BSD28 : UnixV5
    {
        protected class InodeBSD28 : Inode
        {
            public InodeBSD28(Volume volume, Int32 iNumber) : base(volume, iNumber)
            {
                Block B = volume[2 + (iNumber - 1) / 16];
                Int32 offset = ((iNumber - 1) % 16) * 64;
                flags = B.GetUInt16L(ref offset);
                nlinks = B.GetInt16L(ref offset);
                uid = B.GetInt16L(ref offset);
                gid = B.GetInt16L(ref offset);
                size = B.GetInt32P(ref offset);
                addr = new Int32[13];
                for (Int32 i = 0; i < 13; i++)
                {
                    addr[i] = B.GetByte(ref offset) << 16;
                    addr[i] |= B.GetUInt16L(ref offset);
                }
                offset++;
                atime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
                mtime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
                ctime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
            }

            public override Char Type
            {
                get
                {
                    if ((flags & 0xf000) == 0x8000) return '-';
                    if ((flags & 0xf000) == 0x4000) return 'd';
                    if ((flags & 0xf000) == 0xa000) return 'l';
                    if ((flags & 0xf000) == 0x2000) return 'c';
                    if ((flags & 0xf000) == 0x6000) return 'b';
                    if ((flags & 0xf000) == 0xc000) return 's';
                    if ((flags & 0xf000) == 0x0000) return ' ';
                    return '\0';
                }
            }

            public override string Mode
            {
                get
                {
                    StringBuilder buf = new StringBuilder(9);
                    buf.Append(((flags & 0x0100) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0080) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0040) == 0) ? '-' : ((flags & 0x0800) == 0) ? 'x' : 's');
                    buf.Append(((flags & 0x0020) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0010) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0008) == 0) ? '-' : ((flags & 0x0400) == 0) ? 'x' : 's');
                    buf.Append(((flags & 0x0004) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0002) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0001) == 0) ? '-' : ((flags & 0x0200) == 0) ? 'x' : 't');
                    return buf.ToString();
                }
            }

            public override Int32 this[Int32 blockNum]
            {
                get
                {
                    if (blockNum < 10)
                    {
                        return addr[blockNum]; // desired block pointer is in i-node
                    }
                    else
                    {
                        Int32 i;
                        if ((blockNum -= 10) < 256)
                        {
                            i = addr[10]; // desired block pointer is in block i
                        }
                        else
                        {
                            Int32 d;
                            if ((blockNum -= 256) < 256 * 256)
                            {
                                d = addr[11]; // indirect block pointer i is in block d
                            }
                            else
                            {
                                Int32 t = addr[12]; // double-indirect block pointer d is in block t
                                if (t == 0) return 0;
                                d = mVol[t].GetInt32P(4 * (blockNum -= 256 * 256) / (256 * 256));
                                blockNum %= 256 * 256;
                            }
                            if (d == 0) return 0;
                            i = mVol[d].GetInt32P(4 * blockNum / 256);
                            blockNum %= 256;
                        }
                        if (i == 0) return 0;
                        return mVol[i].GetInt32P(4 * blockNum);
                    }
                }
            }
        }

        public BSD28(Volume volume)
        {
            BLOCK_SIZE = 1024;
            ROOT_INUM = 2;
            INOPB = 16;
            ISIZE = 64;
            mVol = volume;
            mType = "Unix/2.8BSD";
            mDirNode = new InodeBSD28(volume, ROOT_INUM);
            mDir = "/";
        }

        protected override Inode GetInode(Int32 iNumber)
        {
            return new InodeBSD28(mVol, iNumber);
        }
    }

    partial class BSD28 : IFileSystemAuto
    {
        public static new TestDelegate GetTest()
        {
            return BSD28.Test;
        }

        // level 0 - check basic volume parameters (return required block size and volume type)
        // level 1 - check boot block (return volume size and type)
        // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
        // level 3 - check file headers (aka inodes) (return file system size and type)
        // level 4 - check directory structure (return file system size and type)
        // level 5 - check file header allocation (return file system size and type)
        // level 6 - check data block allocation (return file system size and type)
        public static new Boolean Test(Volume volume, Int32 level, out Int32 size, out Type type)
        {
            Int32 BLOCK_SIZE = 1024;
            Int32 ROOT_INUM = 2;
            Int32 INOPB = 16;

            // level 0 - check basic volume parameters (return required block size and volume type)
            size = BLOCK_SIZE;
            type = typeof(Volume);
            if (volume == null) return false;
            if (volume.BlockSize != size) return Debug.WriteInfo(false, "BSD28.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            size = -1; // 2BSD doesn't support disk labels
            if (volume.BlockCount < 1) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain boot block (block 0 > {0:D0})", volume.BlockCount - 1);
            if (level == 1) return true;

            // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
            type = null;
            if (volume.BlockCount < 2) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain super-block (block 1 > {0:D0})", volume.BlockCount - 1);
            Block SB = volume[1]; // super-block
            Int32 bp = 0;
            Int32 s_isize = SB.GetUInt16L(ref bp); // SB[0] - number of reserved non-data blocks (boot, super block, inodes)
            if (volume.BlockCount < s_isize) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node list (blocks {0:D0}-{1:D0} > {2:D0})", 2, s_isize - 1, volume.BlockCount - 1);
            Int32 s_fsize = SB.GetInt32P(ref bp); // SB[2] - file system size (in blocks)
            if (s_isize > s_fsize) return Debug.WriteInfo(false, "BSD28.Test: super-block i-list exceeds file system size ({0:D0} > {1:D0})", s_isize, s_fsize);
            Int32 n = SB.GetUInt16L(ref bp); // SB[6] - number of blocks in super-block free block list
            if (n > 50) return Debug.WriteInfo(false, "BSD28.Test: super-block free block count invalid (is {0:D0}, require 0 <= n <= 50)", n);
            Int32 p = SB.GetInt32P(bp); // SB[8] - free block chain next pointer
            if ((p != 0) && ((p < s_isize) || (p >= s_fsize))) return Debug.WriteInfo(false, "BSD28.Test: super-block free chain pointer invalid (is {0:D0}, require {1:D0} <= n < {2:D0})", p, s_isize, s_fsize);
            for (Int32 i = 1; i < n; i++) // SB[10] - free block list
            {
                if (((p = SB.GetInt32P(bp + 4 * i)) < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: super-block free block {0:D0} invalid (is {1:D0}, require {2:D0} <= n < {3:D0})", i, p, s_isize, s_fsize);
            }
            bp += 50 * 4;
            n = SB.GetUInt16L(ref bp); // SB[208] - number of inodes in super-block free inode list
            if (n > 100) return Debug.WriteInfo(false, "BSD28.Test: super-block free i-node count invalid (is {0:D0}, require 0 <= n <= 100)", n);
            for (Int32 i = 0; i < n; i++)
            {
                if (((p = SB.GetUInt16L(bp + 2 * i)) < 1) || (p > (s_isize - 2) * INOPB)) return Debug.WriteInfo(false, "BSD28.Test: super-block free i-number {0:D0} invalid (is {1:D0}, require 1 <= n < {2:D0})", i, p, (s_isize - 2) * INOPB);
            }
            size = s_fsize;
            type = typeof(BSD28);
            if (level == 2) return Debug.WriteInfo(true, "BSD28.Test: file system size: {0:D0} blocks ({1:D0} i-nodes in blocks 2-{2:D0}, data in blocks {3:D0}-{4:D0}", s_fsize, (s_isize - 2) * INOPB, s_isize - 1, s_isize, s_fsize - 1);

            // level 3 - check file headers (aka inodes) (return file system size and type)
            Inode iNode;
            for (UInt16 iNum = 1; iNum <= (s_isize - 2) * INOPB; iNum++)
            {
                iNode = new InodeBSD28(volume, iNum);
                if (iNode.Type == '\0') return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} mode 0{1} (octal) invalid", iNum, Convert.ToString(iNode.flags, 8));
                if (iNode.nlinks < 0) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} link count invalid (is {1:D0}, require n >= 0)", iNum, iNode.nlinks);
                // special case: i-node 1 is used but not linked
                if ((iNode.Type != ' ') && (iNode.nlinks == 0) && (iNum > 1)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} is used but has zero link count", iNum);
                if ((iNode.Type == ' ') && (iNode.nlinks != 0)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} is free but has non-zero link count {1:D0}", iNum, iNode.nlinks);
                if ((iNode.size < 0) || (iNode.size > 1082201088)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} size invalid (is {1:D0}, require 0 <= n <= 1082201088)", iNum, iNode.size); // TODO - check 2.8BSD file size limit
                if ((iNode.Type == ' ') && (iNode.size != 0)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} is free but has non-zero size {1:D0}", iNum, iNode.size);
                if ((iNode.Type != '-') && (iNode.Type != 'd')) continue; // only check blocks for file and directory i-nodes
                // verify validity of i-node direct block pointers
                for (Int32 i = 0; i < 10; i++)
                {
                    if ((p = iNode.addr[i]) == 0) continue;
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                }
                if ((p = iNode.addr[10]) != 0)
                {
                    // verify validity of indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 256; i++)
                    {
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                    }
                }
                if ((p = iNode.addr[11]) != 0)
                {
                    // verify validity of double-indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 256; i++)
                    {
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                        Block B2 = volume[p];
                        for (Int32 j = 0; j < 256; j++)
                        {
                            if ((p = B2.GetInt32P(4 * j)) == 0) continue;
                            if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                            if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                        }
                    }
                }
                if ((p = iNode.addr[12]) != 0)
                {
                    // verify validity of triple-indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 256; i++)
                    {
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                        Block B2 = volume[p];
                        for (Int32 j = 0; j < 256; j++)
                        {
                            if ((p = B2.GetInt32P(4 * j)) == 0) continue;
                            if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                            if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                            Block B3 = volume[p];
                            for (Int32 k = 0; k < 256; k++)
                            {
                                if ((p = B3.GetInt32P(4 * k)) == 0) continue;
                                if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                                if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD28.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                            }
                        }
                    }
                }
            }
            if (level == 3) return true;

            // level 4 - check directory structure (return file system size and type)
            iNode = new InodeBSD28(volume, ROOT_INUM);
            if (iNode.Type != 'd') return Debug.WriteInfo(false, "BSD28.Test: root directory i-node mode invalid (is 0x{0:x4}, require 0x4nnn)", iNode.flags);
            UInt16[] IMap = new UInt16[(s_isize - 2) * INOPB + 1]; // inode usage map
            Queue<Int32> DirList = new Queue<Int32>(); // queue of directories to examine
            DirList.Enqueue((ROOT_INUM << 16) + ROOT_INUM); // parent inum in high word, directory inum in low word (root is its own parent)
            while (DirList.Count != 0)
            {
                Int32 dNum = DirList.Dequeue();
                Int32 pNum = dNum >> 16;
                dNum &= 0xffff;
                if (IMap[dNum] != 0) return Debug.WriteInfo(false, "BSD28.Test: directory i-node {0:D0} appears more than once in directory structure", dNum);
                IMap[dNum]++; // assume a link to this directory from its parent (root directory gets fixed later)
                Byte[] buf = ReadFile(new InodeBSD28(volume, dNum), BLOCK_SIZE);
                Boolean df = false;
                Boolean ddf = false;
                for (bp = 0; bp < buf.Length; bp += 16)
                {
                    UInt16 iNum = Buffer.GetUInt16L(buf, bp);
                    if (iNum == 0) continue;
                    String name = Buffer.GetCString(buf, bp + 2, 14, Encoding.ASCII);
                    if (String.Compare(name, ".") == 0)
                    {
                        if (iNum != dNum) return Debug.WriteInfo(false, "BSD28.Test: in directory i={0:D0}, entry \".\" does not refer to itself (is {1:D0}, require {2:D0})", dNum, iNum, dNum);
                        IMap[iNum]++;
                        df = true;
                    }
                    else if (String.Compare(name, "..") == 0)
                    {
                        if (iNum != pNum) return Debug.WriteInfo(false, "BSD28.Test: in directory i={0:D0}, entry \"..\" does not refer to parent (is {1:D0}, require {2:D0})", dNum, iNum, pNum);
                        IMap[iNum]++;
                        ddf = true;
                    }
                    else if ((iNum < 2) || (iNum > (s_isize - 2) * INOPB))
                    {
                        return Debug.WriteInfo(false, "BSD28.Test: in directory i={0:D0}, entry \"{1}\" has invalid i-number (is {2:D0}, require 2 <= n <= {3:D0})", dNum, name, iNum, (s_isize - 2) * INOPB);
                    }
                    else if ((iNode = new InodeBSD28(volume, iNum)).Type == 'd') // directory
                    {
                        DirList.Enqueue((dNum << 16) + iNum);
                    }
                    else // non-directory
                    {
                        IMap[iNum]++;
                    }
                }
                if (!df) return Debug.WriteInfo(false, "BSD28.Test: in directory i={0:D0}, entry \".\" is missing", dNum);
                if (!ddf) return Debug.WriteInfo(false, "BSD28.Test: in directory i={0:D0}, entry \"..\" is missing", dNum);
            }
            IMap[ROOT_INUM]--; // root directory has no parent, so back out the assumed parent link
            if (level == 4) return true;

            // level 5 - check file header allocation (return file system size and type)
            for (UInt16 iNum = 1; iNum <= (s_isize - 2) * INOPB; iNum++)
            {
                iNode = new InodeBSD28(volume, iNum);
                if ((iNode.Type == ' ') && (IMap[iNum] != 0)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} is free but has {1:D0} link(s)", iNum, IMap[iNum]);
                if ((iNode.Type != ' ') && (IMap[iNum] == 0)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} is used but has no links", iNum);
                if ((iNode.Type != ' ') && (IMap[iNum] != iNode.nlinks)) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} link count mismatch (is {1:D0}, expect {2:D0})", iNum, iNode.nlinks, IMap[iNum]);
            }
            // also check super-block free list
            bp = 208;
            n = SB.GetInt16L(ref bp);
            for (Int32 i = 0; i < n; i++)
            {
                UInt16 iNum = SB.GetUInt16L(ref bp);
                if (IMap[iNum] != 0) return Debug.WriteInfo(false, "BSD28.Test: i-node {0:D0} is in super-block free list, but has {1:D0} link(s)", iNum, IMap[iNum]);
            }
            if (level == 5) return true;

            // level 6 - check data block allocation (return file system size and type)
            UInt16[] BMap = new UInt16[s_fsize]; // block usage map
            for (UInt16 iNum = 1; iNum <= (s_isize - 2) * INOPB; iNum++)
            {
                iNode = new InodeBSD28(volume, iNum);
                if ((iNode.Type != '-') && (iNode.Type != 'd')) continue; // only file and directory i-nodes have blocks allocated
                n = (iNode.size + BLOCK_SIZE - 1) / BLOCK_SIZE; // number of blocks required for file
                // mark data blocks used
                for (Int32 i = 0; i < n; i++)
                {
                    if ((p = iNode[i]) == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD28.Test: data block #{0:D0} ({1:D0}) of i-node {2:D0} is also allocated to i-node {3:D0}", i, p, iNum, BMap[p]);
                    BMap[p] = iNum;
                }
                // mark indirect blocks used
                for (Int32 i = 10; i < 13; i++)
                {
                    p = iNode.addr[i];
                    if (p == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD28.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                    BMap[p] = iNum;
                    if (i == 10) continue;
                    Block B1 = volume[p];
                    for (Int32 j = 0; j < 256; j++)
                    {
                        p = B1.GetInt32P(4 * j);
                        if (p == 0) continue;
                        if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD28.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                        if (i == 11) continue;
                        Block B2 = volume[p];
                        for (Int32 k = 0; k < 256; k++)
                        {
                            p = B2.GetInt32P(4 * k);
                            if (p == 0) continue;
                            if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD28.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                        }
                    }
                }
            }
            // mark used blocks with 1
            for (Int32 i = 0; i < s_isize; i++) BMap[i] = 1;
            for (Int32 i = s_isize; i < s_fsize; i++) if (BMap[i] != 0) BMap[i] = 1;
            // mark free blocks with 2
            bp = 6;
            n = SB.GetInt16L(ref bp); // number of blocks in super-block free block list
            if ((p = SB.GetInt32P(ref bp)) != 0) // free block chain next pointer
            {
                if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD28.Test: block {0:D0} in super-block free list is allocated", p);
                BMap[p] = 2;
            }
            for (Int32 i = 1; i < n; i++)
            {
                p = SB.GetInt32P(ref bp);
                if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD28.Test: block {0:D0} in super-block free list is allocated", p);
                BMap[p] = 2;
            }
            // enumerate blocks in free block chain
            n = SB.GetInt32P(8);
            while (n != 0)
            {
                Block B = volume[n];
                bp = 0;
                if ((n = B.GetInt32P(ref bp)) != 0)
                {
                    if (BMap[n] != 0) return Debug.WriteInfo(false, "BSD28.Test: block {0:D0} in super-block free list is allocated", n);
                    BMap[n] = 2;
                }
                for (Int32 i = 1; i < 50; i++)
                {
                    p = B.GetInt32P(ref bp);
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD28.Test: block {0:D0} in free block chain falls outside volume block range (require {1:D0} <= n < {2:D0})", p, s_isize, s_fsize);
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD28.Test: block {0:D0} in free block chain is allocated", p);
                    BMap[p] = 2;
                }
            }
            // unmarked blocks are lost -- not allocated and not in free list
            for (Int32 i = 0; i < s_fsize; i++)
            {
                if (BMap[i] == 0) return Debug.WriteInfo(false, "BSD28.Test: block {0:D0} is not allocated and not in free list", i);
            }
            if (level == 6) return true;

            return false;
        }
    }


    // 2.11BSD super block
    //  0   s_isize - reserved size (boot + superblock + inodes)
    //  2   s_fsize - file system size
    //  6   s_nfree - number of free blocks listed in superblock (0-50)
    //  8   s_free  - free block list
    //  208 s_ninode - number of free inodes listed in superblock (0-100)
    //  210 s_ifree - free inode list
    //  410 s_flock
    //  411 s_ilock
    //  412 s_fmod
    //  413 s_ronly
    //  414 s_time
    //  418
    //
    // 2.11BSD i-node
    //  0   di_mode     0x0fff mode flags, 0xf000 type mask, types: 8=file, 4=dir, 2=cdev, 6=bdev, a=symlink, c=socket
    //  2   di_nlink
    //  4   di_uid
    //  6   di_gid
    //  8   di_size
    //  12  di_addr
    //  40  di_reserved
    //  50  di_flags
    //  52  di_atime
    //  56  di_mtime
    //  60  di_ctime

    partial class BSD211 : UnixV5
    {
        protected class InodeBSD211 : Inode
        {
            public InodeBSD211(Volume volume, Int32 iNumber) : base(volume, iNumber)
            {
                Block B = volume[2 + (iNumber - 1) / 16];
                Int32 offset = ((iNumber - 1) % 16) * 64;
                flags = B.GetUInt16L(ref offset);
                nlinks = B.GetInt16L(ref offset);
                uid = B.GetInt16L(ref offset);
                gid = B.GetInt16L(ref offset);
                size = B.GetInt32P(ref offset);
                addr = new Int32[7];
                for (Int32 i = 0; i < 7; i++)
                {
                    addr[i] = B.GetInt32P(ref offset);
                }
                offset += 10; // skip di_reserved
                offset += 2; // skip di_flags
                atime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
                mtime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
                ctime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(B.GetInt32P(ref offset));
            }

            public override Char Type
            {
                get
                {
                    if ((flags & 0xf000) == 0x8000) return '-';
                    if ((flags & 0xf000) == 0x4000) return 'd';
                    if ((flags & 0xf000) == 0xa000) return 'l';
                    if ((flags & 0xf000) == 0x2000) return 'c';
                    if ((flags & 0xf000) == 0x6000) return 'b';
                    if ((flags & 0xf000) == 0xc000) return 's';
                    if ((flags & 0xf000) == 0x0000) return ' ';
                    return '\0';
                }
            }

            public override string Mode
            {
                get
                {
                    StringBuilder buf = new StringBuilder(9);
                    buf.Append(((flags & 0x0100) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0080) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0040) == 0) ? '-' : ((flags & 0x0800) == 0) ? 'x' : 's');
                    buf.Append(((flags & 0x0020) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0010) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0008) == 0) ? '-' : ((flags & 0x0400) == 0) ? 'x' : 's');
                    buf.Append(((flags & 0x0004) == 0) ? '-' : 'r');
                    buf.Append(((flags & 0x0002) == 0) ? '-' : 'w');
                    buf.Append(((flags & 0x0001) == 0) ? '-' : ((flags & 0x0200) == 0) ? 'x' : 't');
                    return buf.ToString();
                }
            }

            public override Int32 this[Int32 blockNum]
            {
                get
                {
                    if (blockNum < 4)
                    {
                        return addr[blockNum]; // desired block pointer is in i-node
                    }
                    else
                    {
                        Int32 i;
                        if ((blockNum -= 4) < 256)
                        {
                            i = addr[4]; // desired block pointer is in block i
                        }
                        else
                        {
                            Int32 d;
                            if ((blockNum -= 256) < 256 * 256)
                            {
                                d = addr[5]; // indirect block pointer i is in block d
                            }
                            else
                            {
                                Int32 t = addr[6]; // double-indirect block pointer d is in block t
                                if (t == 0) return 0;
                                d = mVol[t].GetInt32P(4 * (blockNum -= 256 * 256) / (256 * 256));
                                blockNum %= 256 * 256;
                            }
                            if (d == 0) return 0;
                            i = mVol[d].GetInt32P(4 * blockNum / 256);
                            blockNum %= 256;
                        }
                        if (i == 0) return 0;
                        return mVol[i].GetInt32P(4 * blockNum);
                    }
                }
            }
        }

        public BSD211(Volume volume)
        {
            BLOCK_SIZE = 1024;
            ROOT_INUM = 2;
            INOPB = 16;
            ISIZE = 64;
            mVol = volume;
            mType = "Unix/2.11BSD";
            mDirNode = new InodeBSD211(volume, ROOT_INUM);
            mDir = "/";
        }

        public override void ListDir(String fileSpec, TextWriter output)
        {
            Int32 p;
            Inode dirNode = mDirNode;
            String dirName = mDir;
            if ((fileSpec == null) || (fileSpec.Length == 0))
            {
                fileSpec = "*";
            }
            else if ((p = fileSpec.LastIndexOf('/')) == 0)
            {
                dirNode = GetInode(ROOT_INUM);
                dirName = "/";
                while ((fileSpec.Length != 0) && (fileSpec[0] == '/')) fileSpec = fileSpec.Substring(1);
                if (fileSpec.Length == 0) fileSpec = "*";
            }
            else if (p != -1)
            {
                if ((!FindFile(fileSpec.Substring(0, p), out dirNode, out dirName)) || (dirNode.Type != 'd'))
                {
                    Console.Error.WriteLine("Not found: {0}", fileSpec);
                    return;
                }
                fileSpec = fileSpec.Substring(p + 1);
                while ((fileSpec.Length != 0) && (fileSpec[0] == '/')) fileSpec = fileSpec.Substring(1);
                if (fileSpec.Length == 0) fileSpec = "*";
            }

            // if fileSpec names a single directory, show the content of that directory
            Inode iNode;
            String pathName;
            if ((FindDirEntry(dirNode, dirName, fileSpec, out iNode, out pathName)) && (iNode.Type == 'd'))
            {
                dirNode = iNode;
                dirName = pathName;
                fileSpec = "*";
                output.WriteLine("{0}:", pathName);
            }

            // count number of blocks used
            Int32 n = 0;
            Regex RE = Regex(fileSpec);
            Byte[] data = ReadFile(dirNode, BLOCK_SIZE);
            Int32 bp = 0;
            while (bp < data.Length)
            {
                p = bp;
                Int32 iNum = Buffer.GetUInt16L(data, p);
                bp += Buffer.GetUInt16L(data, p + 2);
                Int32 len = Buffer.GetUInt16L(data, p + 4);
                if (iNum == 0) continue;
                String name = Buffer.GetCString(data, p + 6, len, Encoding.ASCII);
                if (!RE.IsMatch(name)) continue;
                iNode = GetInode(iNum);
                n += (iNode.size + BLOCK_SIZE - 1) / BLOCK_SIZE;
            }

            // show directory listing
            output.WriteLine("total {0:D0}", n);
            bp = 0;
            while (bp < data.Length)
            {
                p = bp;
                Int32 iNum = Buffer.GetUInt16L(data, p);
                bp += Buffer.GetUInt16L(data, p + 2);
                Int32 len = Buffer.GetUInt16L(data, p + 4);
                if (iNum == 0) continue;
                String name = Buffer.GetCString(data, p + 6, len, Encoding.ASCII);
                if (!RE.IsMatch(name)) continue;
                iNode = GetInode(iNum);
                Char ft = iNode.Type;
                switch (ft)
                {
                    case '-':
                    case 'd':
                        output.WriteLine("{0,5:D0} {1}{2} {3,2:D0} {4,-5} {5,7:D0} {6}  {7}", iNum, ft, iNode.Mode, iNode.nlinks, iNode.uid.ToString(), iNode.size, iNode.mtime.ToString("MM/dd/yyyy HH:mm"), name);
                        break;
                    case 'b':
                    case 'c':
                        output.WriteLine("{0,5:D0} {1}{2} {3,2:D0} {4,-5} {5,3:D0},{6,3:D0} {7}  {8}", iNum, ft, iNode.Mode, iNode.nlinks, iNode.uid.ToString(), iNode.addr[0] >> 8, iNode.addr[0] & 0x00ff, iNode.mtime.ToString("MM/dd/yyyy HH:mm"), name);
                        break;
                }
            }
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            ListDir(fileSpec, output);
        }

        protected override Inode GetInode(Int32 iNumber)
        {
            return new InodeBSD211(mVol, iNumber);
        }

        protected override Boolean FindDirEntry(Inode dirNode, String dirName, String entryName, out Inode iNode, out String pathName)
        {
            iNode = null;
            pathName = null;
            Int32 n = 0;
            Regex RE = Regex(entryName);
            Byte[] dir = ReadFile(dirNode, BLOCK_SIZE);
            Int32 dp = 0;
            while (dp < dir.Length)
            {
                Int32 p = dp;
                Int32 iNum = Buffer.GetUInt16L(dir, p);
                dp += Buffer.GetUInt16L(dir, p + 2);
                Int32 len = Buffer.GetUInt16L(dir, p + 4);
                if (iNum == 0) continue;
                String name = Buffer.GetCString(dir, p + 6, len, Encoding.ASCII);
                if (RE.IsMatch(name))
                {
                    n++;
                    iNode = GetInode(iNum);
                    if ((name == ".") || ((name == "..") && (dirName == "/")))
                    {
                        pathName = dirName;
                    }
                    else if (name == "..")
                    {
                        pathName = dirName.Substring(0, dirName.Length - 1);
                        pathName = pathName.Substring(0, pathName.LastIndexOf('/') + 1);
                    }
                    else
                    {
                        pathName = String.Concat(dirName, name, (iNode.Type == 'd') ? "/" : null);
                    }
                }
            }
            return (n == 1);
        }
    }

    partial class BSD211 : IFileSystemAuto
    {
        public static new TestDelegate GetTest()
        {
            return BSD211.Test;
        }

        // level 0 - check basic volume parameters (return required block size and volume type)
        // level 1 - check boot block (return volume size and type)
        // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
        // level 3 - check file headers (aka inodes) (return file system size and type)
        // level 4 - check directory structure (return file system size and type)
        // level 5 - check file header allocation (return file system size and type)
        // level 6 - check data block allocation (return file system size and type)
        public static new Boolean Test(Volume volume, Int32 level, out Int32 size, out Type type)
        {
            Int32 BLOCK_SIZE = 1024;
            Int32 ROOT_INUM = 2;
            Int32 INOPB = 16;

            // level 0 - check basic volume parameters (return required block size and volume type)
            size = BLOCK_SIZE;
            type = typeof(Volume);
            if (volume == null) return false;
            if (volume.BlockSize != size) return Debug.WriteInfo(false, "BSD211.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            size = -1; // 2BSD doesn't support disk labels
            if (volume.BlockCount < 1) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain boot block (block 0 > {0:D0})", volume.BlockCount - 1);
            if (level == 1) return true;

            // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
            type = null;
            if (volume.BlockCount < 2) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain super-block (block 1 > {0:D0})", volume.BlockCount - 1);
            Block SB = volume[1]; // super-block
            Int32 bp = 0;
            Int32 s_isize = SB.GetUInt16L(ref bp); // SB[0] - number of reserved non-data blocks (boot, super block, inodes)
            if (volume.BlockCount < s_isize) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node list (blocks {0:D0}-{1:D0} > {2:D0})", 2, s_isize - 1, volume.BlockCount - 1);
            Int32 s_fsize = SB.GetInt32P(ref bp); // SB[2] - file system size (in blocks)
            if (s_isize > s_fsize) return Debug.WriteInfo(false, "BSD211.Test: super-block i-list exceeds file system size ({0:D0} > {1:D0})", s_isize, s_fsize);
            Int32 n = SB.GetUInt16L(ref bp); // SB[6] - number of blocks in super-block free block list
            if (n > 50) return Debug.WriteInfo(false, "BSD211.Test: super-block free block count invalid (is {0:D0}, require 0 <= n <= 50)", n);
            Int32 p = SB.GetInt32P(bp); // SB[8] - free block chain next pointer
            if ((p != 0) && ((p < s_isize) || (p >= s_fsize))) return Debug.WriteInfo(false, "BSD211.Test: super-block free chain pointer invalid (is {0:D0}, require {1:D0} <= n < {2:D0})", p, s_isize, s_fsize);
            for (Int32 i = 1; i < n; i++) // SB[10] - free block list
            {
                if (((p = SB.GetInt32P(bp + 4 * i)) < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: super-block free block {0:D0} invalid (is {1:D0}, require {2:D0} <= n < {3:D0})", i, p, s_isize, s_fsize);
            }
            bp += 50 * 4;
            n = SB.GetUInt16L(ref bp); // SB[208] - number of inodes in super-block free inode list
            if (n > 100) return Debug.WriteInfo(false, "BSD211.Test: super-block free i-node count invalid (is {0:D0}, require 0 <= n <= 100)", n);
            for (Int32 i = 0; i < n; i++)
            {
                if (((p = SB.GetUInt16L(bp + 2 * i)) < 1) || (p > (s_isize - 2) * INOPB)) return Debug.WriteInfo(false, "BSD211.Test: super-block free i-number {0:D0} invalid (is {1:D0}, require 1 <= n < {2:D0})", i, p, (s_isize - 2) * INOPB);
            }
            size = s_fsize;
            type = typeof(BSD211);
            if (level == 2) return Debug.WriteInfo(true, "BSD211.Test: file system size: {0:D0} blocks ({1:D0} i-nodes in blocks 2-{2:D0}, data in blocks {3:D0}-{4:D0}", s_fsize, (s_isize - 2) * INOPB, s_isize - 1, s_isize, s_fsize - 1);

            // level 3 - check file headers (aka inodes) (return file system size and type)
            Inode iNode;
            for (UInt16 iNum = 1; iNum <= (s_isize - 2) * INOPB; iNum++)
            {
                iNode = new InodeBSD211(volume, iNum);
                if (iNode.Type == '\0') return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} mode 0{1} (octal) invalid", iNum, Convert.ToString(iNode.flags, 8));
                if (iNode.nlinks < 0) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} link count invalid (is {1:D0}, require n >= 0)", iNum, iNode.nlinks);
                // special case: i-node 1 is used but not linked
                if ((iNode.Type != ' ') && (iNode.nlinks == 0) && (iNum > 1)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} is used but has zero link count", iNum);
                if ((iNode.Type == ' ') && (iNode.nlinks != 0)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} is free but has non-zero link count {1:D0}", iNum, iNode.nlinks);
                if ((iNode.size < 0) || (iNode.size > 1082201088)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} size invalid (is {1:D0}, require 0 <= n <= 1082201088)", iNum, iNode.size); // TODO - check 2.11BSD file size limit
                if ((iNode.Type == ' ') && (iNode.size != 0)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} is free but has non-zero size {1:D0}", iNum, iNode.size);
                if ((iNode.Type != '-') && (iNode.Type != 'd')) continue; // only check blocks for file and directory i-nodes
                // verify validity of i-node direct block pointers
                for (Int32 i = 0; i < 4; i++)
                {
                    if ((p = iNode.addr[i]) == 0) continue;
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                }
                if ((p = iNode.addr[4]) != 0)
                {
                    // verify validity of indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 256; i++)
                    {
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                    }
                }
                if ((p = iNode.addr[5]) != 0)
                {
                    // verify validity of double-indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 256; i++)
                    {
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                        Block B2 = volume[p];
                        for (Int32 j = 0; j < 256; j++)
                        {
                            if ((p = B2.GetInt32P(4 * j)) == 0) continue;
                            if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                            if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                        }
                    }
                }
                if ((p = iNode.addr[6]) != 0)
                {
                    // verify validity of triple-indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 256; i++)
                    {
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                        Block B2 = volume[p];
                        for (Int32 j = 0; j < 256; j++)
                        {
                            if ((p = B2.GetInt32P(4 * j)) == 0) continue;
                            if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                            if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                            Block B3 = volume[p];
                            for (Int32 k = 0; k < 256; k++)
                            {
                                if ((p = B3.GetInt32P(4 * k)) == 0) continue;
                                if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                                if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "BSD211.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                            }
                        }
                    }
                }
            }
            if (level == 3) return true;

            // level 4 - check directory structure (return file system size and type)
            iNode = new InodeBSD211(volume, ROOT_INUM);
            if (iNode.Type != 'd') return Debug.WriteInfo(false, "BSD211.Test: root directory i-node mode invalid (is 0x{0:x4}, require 0x4nnn)", iNode.flags);
            UInt16[] IMap = new UInt16[(s_isize - 2) * INOPB + 1]; // inode usage map
            Queue<Int32> DirList = new Queue<Int32>(); // queue of directories to examine
            DirList.Enqueue((ROOT_INUM << 16) + ROOT_INUM); // parent inum in high word, directory inum in low word (root is its own parent)
            while (DirList.Count != 0)
            {
                Int32 dNum = DirList.Dequeue();
                Int32 pNum = dNum >> 16;
                dNum &= 0xffff;
                if (IMap[dNum] != 0) return Debug.WriteInfo(false, "BSD211.Test: directory i-node {0:D0} appears more than once in directory structure", dNum);
                IMap[dNum]++; // assume a link to this directory from its parent (root directory gets fixed later)
                Byte[] buf = ReadFile(new InodeBSD211(volume, dNum), BLOCK_SIZE);
                Boolean df = false;
                Boolean ddf = false;
                for (bp = 0; bp < buf.Length; bp += 16)
                {
                    UInt16 iNum = Buffer.GetUInt16L(buf, bp);
                    if (iNum == 0) continue;
                    String name = Buffer.GetCString(buf, bp + 2, 14, Encoding.ASCII);
                    if (String.Compare(name, ".") == 0)
                    {
                        if (iNum != dNum) return Debug.WriteInfo(false, "BSD211.Test: in directory i={0:D0}, entry \".\" does not refer to itself (is {1:D0}, require {2:D0})", dNum, iNum, dNum);
                        IMap[iNum]++;
                        df = true;
                    }
                    else if (String.Compare(name, "..") == 0)
                    {
                        if (iNum != pNum) return Debug.WriteInfo(false, "BSD211.Test: in directory i={0:D0}, entry \"..\" does not refer to parent (is {1:D0}, require {2:D0})", dNum, iNum, pNum);
                        IMap[iNum]++;
                        ddf = true;
                    }
                    else if ((iNum < 2) || (iNum > (s_isize - 2) * INOPB))
                    {
                        return Debug.WriteInfo(false, "BSD211.Test: in directory i={0:D0}, entry \"{1}\" has invalid i-number (is {2:D0}, require 2 <= n <= {3:D0})", dNum, name, iNum, (s_isize - 2) * INOPB);
                    }
                    else if ((iNode = new InodeBSD211(volume, iNum)).Type == 'd') // directory
                    {
                        DirList.Enqueue((dNum << 16) + iNum);
                    }
                    else // non-directory
                    {
                        IMap[iNum]++;
                    }
                }
                if (!df) return Debug.WriteInfo(false, "BSD211.Test: in directory i={0:D0}, entry \".\" is missing", dNum);
                if (!ddf) return Debug.WriteInfo(false, "BSD211.Test: in directory i={0:D0}, entry \"..\" is missing", dNum);
            }
            IMap[ROOT_INUM]--; // root directory has no parent, so back out the assumed parent link
            if (level == 4) return true;

            // level 5 - check file header allocation (return file system size and type)
            for (UInt16 iNum = 1; iNum <= (s_isize - 2) * INOPB; iNum++)
            {
                iNode = new InodeBSD211(volume, iNum);
                if ((iNode.Type == ' ') && (IMap[iNum] != 0)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} is free but has {1:D0} link(s)", iNum, IMap[iNum]);
                if ((iNode.Type != ' ') && (IMap[iNum] == 0)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} is used but has no links", iNum);
                if ((iNode.Type != ' ') && (IMap[iNum] != iNode.nlinks)) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} link count mismatch (is {1:D0}, expect {2:D0})", iNum, iNode.nlinks, IMap[iNum]);
            }
            // also check super-block free list
            bp = 208;
            n = SB.GetInt16L(ref bp);
            for (Int32 i = 0; i < n; i++)
            {
                UInt16 iNum = SB.GetUInt16L(ref bp);
                if (IMap[iNum] != 0) return Debug.WriteInfo(false, "BSD211.Test: i-node {0:D0} is in super-block free list, but has {1:D0} link(s)", iNum, IMap[iNum]);
            }
            if (level == 5) return true;

            // level 6 - check data block allocation (return file system size and type)
            UInt16[] BMap = new UInt16[s_fsize]; // block usage map
            for (UInt16 iNum = 1; iNum <= (s_isize - 2) * INOPB; iNum++)
            {
                iNode = new InodeBSD211(volume, iNum);
                if ((iNode.Type != '-') && (iNode.Type != 'd')) continue; // only file and directory i-nodes have blocks allocated
                n = (iNode.size + BLOCK_SIZE - 1) / BLOCK_SIZE; // number of blocks required for file
                // mark data blocks used
                for (Int32 i = 0; i < n; i++)
                {
                    if ((p = iNode[i]) == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD211.Test: data block #{0:D0} ({1:D0}) of i-node {2:D0} is also allocated to i-node {3:D0}", i, p, iNum, BMap[p]);
                    BMap[p] = iNum;
                }
                // mark indirect blocks used
                for (Int32 i = 4; i < 7; i++)
                {
                    p = iNode.addr[i];
                    if (p == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD211.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                    BMap[p] = iNum;
                    if (i == 4) continue;
                    Block B1 = volume[p];
                    for (Int32 j = 0; j < 256; j++)
                    {
                        p = B1.GetInt32P(4 * j);
                        if (p == 0) continue;
                        if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD211.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                        if (i == 5) continue;
                        Block B2 = volume[p];
                        for (Int32 k = 0; k < 256; k++)
                        {
                            p = B2.GetInt32P(4 * k);
                            if (p == 0) continue;
                            if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD211.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[p]);
                        }
                    }
                }
            }
            // mark used blocks with 1
            for (Int32 i = 0; i < s_isize; i++) BMap[i] = 1;
            for (Int32 i = s_isize; i < s_fsize; i++) if (BMap[i] != 0) BMap[i] = 1;
            // mark free blocks with 2
            bp = 6;
            n = SB.GetInt16L(ref bp); // number of blocks in super-block free block list
            if ((p = SB.GetInt32P(ref bp)) != 0) // free block chain next pointer
            {
                if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD211.Test: block {0:D0} in super-block free list is allocated", p);
                BMap[p] = 2;
            }
            for (Int32 i = 1; i < n; i++)
            {
                p = SB.GetInt32P(ref bp);
                if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD211.Test: block {0:D0} in super-block free list is allocated", p);
                BMap[p] = 2;
            }
            // enumerate blocks in free block chain
            n = SB.GetInt32P(8);
            while (n != 0)
            {
                Block B = volume[n];
                bp = 0;
                if ((n = B.GetInt32P(ref bp)) != 0)
                {
                    if (BMap[n] != 0) return Debug.WriteInfo(false, "BSD211.Test: block {0:D0} in super-block free list is allocated", n);
                    BMap[n] = 2;
                }
                for (Int32 i = 1; i < 50; i++)
                {
                    p = B.GetInt32P(ref bp);
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "BSD211.Test: block {0:D0} in free block chain falls outside volume block range (require {1:D0} <= n < {2:D0})", p, s_isize, s_fsize);
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "BSD211.Test: block {0:D0} in free block chain is allocated", p);
                    BMap[p] = 2;
                }
            }
            // unmarked blocks are lost -- not allocated and not in free list
            for (Int32 i = 0; i < s_fsize; i++)
            {
                if (BMap[i] == 0) return Debug.WriteInfo(false, "BSD211.Test: block {0:D0} is not allocated and not in free list", i);
            }
            if (level == 6) return true;

            return false;
        }
    }
}
