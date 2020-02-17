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
// increases the size of an i-node to contain additional block pointers.  In
// place of a 'large file' flag, the i-node block pointers are treated the
// same for all files: 10 direct pointers, then 1 indirect, 1 double-indirect,
// and 1 triple-indirect pointer.  Each indirect block contains 128 4-byte
// pointers, so the maximum file size increases to:
//   512 * (10 + 128 + 128*128 + 128*128*128) = 1082201087 bytes
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
//
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


// Improvements / To Do
// in ListDir, handle directory paths not ending in '/'
// in ListDir, show file dates
// in Test, check for duplicate names in a directory
// relax link count check for inodes (file systems are readable despite this)
// finish support for Unix v7
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
    partial class UnixV5 : FileSystem
    {
        private const Int32 INOPB = 16;
        private const Int32 ISIZE = 32;
        private const Int32 ROOT_INUM = 1;

        protected class Inode
        {
            public Int32 iNum;
            public UInt16 flags;
            public Byte nlinks;
            public Byte uid;
            public Byte gid;
            public Int32 size;
            public UInt16[] addr;
            public Int32 actime;
            public Int32 modtime;

            public Inode(Int32 iNumber)
            {
                iNum = iNumber;
            }

            public UInt16 this[Int32 index]
            {
                get { return addr[index]; }
            }

            public static Inode Get(Volume volume, Int32 iNum)
            {
                Int32 block = 2 + (iNum - 1) / INOPB;
                Int32 offset = ((iNum - 1) % INOPB) * ISIZE;
                Block B = volume[block];
                Inode I = new Inode(iNum);
                I.flags = B.GetUInt16L(ref offset);
                I.nlinks = B.GetByte(ref offset);
                I.uid = B.GetByte(ref offset);
                I.gid = B.GetByte(ref offset);
                I.size = B.GetByte(ref offset) << 16;
                I.size += B.GetUInt16L(ref offset);
                I.addr = new UInt16[8];
                for (Int32 i = 0; i < 8; i++) I.addr[i] = B.GetUInt16L(ref offset);
                I.actime = B.GetInt32P(ref offset);
                I.modtime = B.GetInt32P(ref offset);
                return I;
            }
        }

        private Volume mVol;
        protected String mType;
        private Int32 mRoot;
        private Inode mDirNode;
        private String mDir;

        public UnixV5(Volume volume)
        {
            mVol = volume;
            mType = "Unix/V5";
            mRoot = ROOT_INUM;
            mDirNode = Inode.Get(volume, ROOT_INUM);
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
            if (!FindFile(mDirNode, mDir, dirSpec, out dirNode, out dirName))
            {
                Console.Error.WriteLine("Not found: {0}", dirSpec);
                return;
            }
            if ((dirNode.flags & 0x6000) != 0x4000)
            {
                Console.Error.WriteLine("Not a directory: {0}", dirName);
                return;
            }
            mDirNode = dirNode;
            mDir = dirName;
        }

        public override void ListDir(String fileSpec, TextWriter output)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) fileSpec = "*";

            Inode dirNode = mDirNode;
            if (fileSpec[0] == '/')
            {
                dirNode = Inode.Get(mVol, mRoot);
                while ((fileSpec.Length != 0) && (fileSpec[0] == '/')) fileSpec = fileSpec.Substring(1);
                if (fileSpec.Length == 0) fileSpec = "*";
            }

            String name;
            Int32 p = fileSpec.LastIndexOf('/');
            if (p != -1)
            {
                if ((!FindFile(mDirNode, mDir, fileSpec.Substring(0, p), out dirNode, out name)) || ((dirNode.flags & 0x6000) != 0x4000))
                {
                    Console.Error.WriteLine("Not found: {0}", fileSpec);
                    return;
                }
                fileSpec = fileSpec.Substring(p + 1);
                if (fileSpec.Length == 0) fileSpec = "*";
            }
            // TODO: handle directory paths not ending in '/' such as "dev" or "/usr/source"

            // count number of blocks used
            Int32 n = 0;
            Regex RE = Regex(fileSpec);
            Byte[] data = ReadFile(mVol, dirNode);
            for (Int32 bp = 0; bp < data.Length; bp += 16)
            {
                Int32 iNum = Buffer.GetUInt16L(data, bp);
                if (iNum == 0) continue;
                name = Buffer.GetCString(data, bp + 2, 14, Encoding.ASCII);
                if (!RE.IsMatch(name)) continue;
                Inode iNode = Inode.Get(mVol, iNum);
                n += (iNode.size + 511) / 512;
            }

            // show directory listing
            output.WriteLine("total {0:D0}", n);
            for (Int32 bp = 0; bp < data.Length; bp += 16)
            {
                Int32 iNum = Buffer.GetUInt16L(data, bp);
                if (iNum == 0) continue;
                name = Buffer.GetCString(data, bp + 2, 14, Encoding.ASCII);
                if (!RE.IsMatch(name)) continue;
                Inode iNode = Inode.Get(mVol, iNum);
                p = (iNode.flags & 0x6000) >> 13;
                Char ft = (p == 0) ? '-' : (p == 2) ? 'd' : (p == 1) ? 'c' : 'b';
                String op = Perm((iNode.flags & 0x01c0) >> 6, iNode.flags & 0x0800);
                String gp = Perm((iNode.flags & 0x0038) >> 3, iNode.flags & 0x0400);
                String wp = Perm(iNode.flags & 0x0007, 0);
                // TODO: show file date
                switch (ft)
                {
                    case '-':
                    case 'd':
                        output.WriteLine("{0,5:D0} {1}{2}{3}{4} {5,2:D0} {6,-5} {7,7:D0} {8,-14}", iNum, ft, op, gp, wp, iNode.nlinks, iNode.uid.ToString(), iNode.size, name);
                        break;
                    case 'b':
                    case 'c':
                        output.WriteLine("{0,5:D0} {1}{2}{3}{4} {5,2:D0} {6,-5} {7,3:D0},{8,3:D0} {9,-14}", iNum, ft, op, gp, wp, iNode.nlinks, iNode.uid.ToString(), iNode[0] >> 8, iNode[0] & 0x00ff, name);
                        break;
                }
            }
        }

        private String Perm(Int32 permBits, Int32 isSUID)
        {
            Char[] C = new Char[3];
            C[0] = ((permBits & 4) == 0) ? '-' : 'r';
            C[1] = ((permBits & 2) == 0) ? '-' : 'w';
            C[2] = ((permBits & 1) == 0) ? '-' : 'x';
            if (isSUID != 0) C[2] = 's';
            return new String(C);
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            ListDir(fileSpec, output);
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
            Inode iNode;
            String name;
            if (!FindFile(mDirNode, mDir, fileSpec, out iNode, out name)) return;
            String buf = encoding.GetString(ReadFile(mVol, iNode));
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
            if (!FindFile(mDirNode, mDir, fileSpec, out iNode, out name)) return;
            Program.Dump(null, ReadFile(mVol, iNode), output, 16, 512, Program.DumpOptions.ASCII);
        }

        public override String FullName(String fileSpec)
        {
            Inode iNode;
            String name;
            if (!FindFile(mDirNode, mDir, fileSpec, out iNode, out name)) return null;
            return name;
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            Inode iNode;
            String name;
            if (!FindFile(mDirNode, mDir, fileSpec, out iNode, out name)) return new Byte[0];
            return ReadFile(mVol, iNode);
        }

        public override Boolean SaveFS(String fileName, String format)
        {
            throw new NotImplementedException();
        }

        private Boolean FindFile(Inode dirNode, String dirName, String pathSpec, out Inode iNode, out String pathName)
        {
            iNode = null;
            pathName = null;
            if ((pathSpec == null) || (pathSpec.Length == 0)) return false;

            // start at root directory for absolute paths
            if (pathSpec[0] == '/')
            {
                dirNode = Inode.Get(mVol, mRoot);
                dirName = "/";
                while ((pathSpec.Length != 0) && (pathSpec[0] == '/')) pathSpec = pathSpec.Substring(1);
            }

            // find directory containing file
            Int32 p;
            while ((p = pathSpec.IndexOf('/')) != -1)
            {
                String name;
                if (!FindFile(dirNode, dirName, pathSpec.Substring(0, p), out iNode, out name)) return false;
                if ((iNode.flags & 0x6000) != 0x4000) return false; // all path prefix components must be dirs
                dirNode = iNode;
                dirName = name;
                pathSpec = pathSpec.Substring(p + 1);
                while ((pathSpec.Length != 0) && (pathSpec[0] == '/')) pathSpec = pathSpec.Substring(1);
            }

            // if no file component, the last directory found is what we're looking for
            if (pathSpec.Length == 0)
            {
                iNode = dirNode;
                pathName = dirName;
                return true;
            }

            // find the file in the final directory
            Regex RE = Regex(pathSpec);
            Byte[] data = ReadFile(mVol, dirNode);
            for (Int32 bp = 0; bp < data.Length; bp += 16)
            {
                Int32 iNum = Buffer.GetUInt16L(data, bp);
                if (iNum == 0) continue;
                String name = Buffer.GetCString(data, bp + 2, 14, Encoding.ASCII);
                if (RE.IsMatch(name))
                {
                    iNode = Inode.Get(mVol, iNum);
                    if (name == ".")
                    {
                        pathName = dirName;
                    }
                    else if (name == "..")
                    {
                        Int32 q = dirName.Substring(0, dirName.Length - 1).LastIndexOf('/');
                        pathName = (q == -1) ? "/" : dirName.Substring(0, q + 1);
                    }
                    else
                    {
                        pathName = String.Concat(dirName, name, ((iNode.flags & 0x6000) == 0x4000) ? "/" : null);
                    }
                    return true;
                }
            }
            return false;
        }
    }

    partial class UnixV5
    {
        private static Byte[] ReadFile(Volume volume, Inode iNode)
        {
            Byte[] buf = new Byte[iNode.size];
            if ((iNode.flags & 0x1000) == 0)
            {
                // inode links to up to 8 direct blocks
                for (Int32 p = 0; p < iNode.size; p += 512)
                {
                    Int32 b = iNode[p / 512]; // direct block
                    if ((b == 0) || (b >= volume.BlockCount)) continue;
                    Int32 c = iNode.size - p;
                    volume[b].CopyTo(buf, p, 0, (c > 512) ? 512 : c);
                }
            }
            else
            {
                // inode links to up to 8 indirect blocks
                for (Int32 p = 0; p < iNode.size; p += 512)
                {
                    Int32 b = p / 512;
                    Int32 i = iNode[b / 256]; // indirect block
                    if ((i == 0) || (i >= volume.BlockCount)) continue;
                    b = volume[i].GetUInt16L((b % 256) * 2); // direct block
                    if ((b == 0) || (b >= volume.BlockCount)) continue;
                    Int32 c = iNode.size - p;
                    volume[b].CopyTo(buf, p, 0, (c > 512) ? 512 : c);
                }
            }
            return buf;
        }

        private static Regex Regex(String pattern)
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
            // level 0 - check basic volume parameters (return required block size and volume type)
            size = 512;
            type = typeof(Volume);
            if (volume == null) return false;
            if (volume.BlockSize != size) return Debug.WriteLine(false, 1, "UnixV5.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            size = -1; // Unix V5 doesn't support disk labels
            if (volume.BlockCount < 1) return Debug.WriteLine(false, 1, "UnixV5.Test: volume too small to contain boot block");
            if (level == 1) return true;

            // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
            type = null;
            if (volume.BlockCount < 2) return Debug.WriteLine(false, 1, "UnixV5.Test: volume too small to contain super-block");
            Block SB = volume[1]; // super-block
            Int32 isize = SB.GetUInt16L(0); // number of blocks used for inodes
            Int32 n = SB.GetUInt16L(2); // file system size (in blocks)
            if (isize + 2 > n) return Debug.WriteLine(false, 1, "UnixV5.Test: super-block i-list exceeds volume size ({0:D0} > {1:D0})", isize + 2, n);
            Int32 p = SB.GetInt16L(4);  // number of blocks in super-block free block list
            if ((p < 1) || (p > 100)) return Debug.WriteLine(false, 1, "UnixV5.Test: super-block free block count invalid (is {0:D0}, require 1 <= n <= 100)", p);
            Int32 l = 2 * p;
            for (Int32 i = 0; i < l; i += 2)
            {
                if (((p = SB.GetUInt16L(6 + i)) < isize + 2) || (p >= n)) return Debug.WriteLine(false, 1, "UnixV5.Test: super-block free block {0:D0} invalid (is {1:D0}, require {2:D0} <= n < {3:D0})", i / 2, p, isize + 2, n);
            }
            p = SB.GetUInt16L(206); // number of inodes in super-block free inode list
            if (p > 100) return Debug.WriteLine(false, 1, "UnixV5.Test: super-block free i-node count invalid (is {0:D0}, require n <= 100)", p);
            l = 2 * p;
            for (Int32 i = 0; i < l; i += 2)
            {
                if (((p = SB.GetUInt16L(208 + i)) < 1) || (p > isize * 16)) return Debug.WriteLine(false, 1, "UnixV5.Test: super-block free i-number {0:D0} invalid (is {1:D0}, require 1 <= n < {2:D0})", i / 2, p, isize * 16);
            }
            size = n;
            type = typeof(UnixV5);
            if (level == 2) return Debug.WriteInfo(true, "UnixV5.Test: file system size: {0:D0} blocks ({1:D0} i-nodes in blocks 2-{2:D0}, data in blocks {3:D0}-{4:D0}", size, isize * INOPB, isize + 1, isize + 2, size - 1);

            // level 3 - check file headers (aka inodes) (return file system size and type)
            Inode iNode;
            for (UInt16 iNum = 1; iNum <= isize * 16; iNum++)
            {
                iNode = Inode.Get(volume, iNum);
                // sometimes a free i-node has 'nlinks' -1. or other values.
                //if (((iNode.flags & 0x8000) == 0) && (iNode.nlinks != 0) && (iNode.nlinks != 255)) return Debug.WriteLine(false, 1, "UnixV5.Test: i-node {0:D0} is free but has non-zero link count {1:D0}", iNum, iNode.nlinks);
                if (((iNode.flags & 0x8000) != 0) && (iNode.nlinks == 0)) return Debug.WriteLine(false, 1, "UnixV5.Test: i-node {0:D0} is used but has zero link count", iNum);
                if (((iNode.flags & 0x1000) == 0) && (iNode.size > 4096)) return Debug.WriteLine(false, 1, "UnixV5.Test: i-node {0:D0} size exceeds small file limit (is {1:D0}, require n <= 4096)", iNum, iNode.size);
                if (((iNode.flags & 0x1000) != 0) && (iNode.size > 1048576)) return Debug.WriteLine(false, 1, "UnixV5.Test: i-node {0:D0} size exceeds large file limit (is {1:D0}, require n <= 1048576)", iNum, iNode.size);
            }
            if (level == 3) return true;

            // level 4 - check directory structure (return file system size and type)
            iNode = Inode.Get(volume, 1);
            if ((iNode.flags & 0xe000) != 0xc000) return Debug.WriteLine(false, 1, "UnixV5.Test: root directory i-node type/used flags invalid (is 0x{0:x4}, require 0xc000)", iNode.flags & 0xe000);
            UInt16[] IMap = new UInt16[isize * 16 + 1]; // inode usage map
            Queue<Int32> DirList = new Queue<Int32>(); // queue of directories to examine
            DirList.Enqueue((1 << 16) + 1); // parent inum in high word, directory inum in low word (root is its own parent)
            while (DirList.Count != 0)
            {
                Int32 dNum = DirList.Dequeue();
                Int32 pNum = dNum >> 16;
                dNum &= 0xffff;
                if (IMap[dNum] != 0) return Debug.WriteLine(false, 1, "UnixV5.Test: directory i-node {0:D0} appears more than once in directory structure", dNum);
                IMap[dNum]++; // assume a link to this directory from its parent (root directory gets fixed later)
                Byte[] buf = ReadFile(volume, Inode.Get(volume, dNum));
                Boolean df = false;
                Boolean ddf = false;
                for (Int32 bp = 0; bp < buf.Length; bp += 16)
                {
                    UInt16 iNum = Buffer.GetUInt16L(buf, bp);
                    if (iNum == 0) continue;
                    String name = Buffer.GetCString(buf, bp + 2, 14, Encoding.ASCII);
                    if (String.Compare(name, ".") == 0)
                    {
                        if (iNum != dNum) return Debug.WriteLine(false, 1, "UnixV5.Test: in directory i={0:D0}, entry \".\" does not refer to itself (is {1:D0}, require {2:D0})", dNum, iNum, dNum);
                        IMap[iNum]++;
                        df = true;
                    }
                    else if (String.Compare(name, "..") == 0)
                    {
                        if (iNum != pNum) return Debug.WriteLine(false, 1, "UnixV5.Test: in directory i={0:D0}, entry \"..\" does not refer to parent (is {1:D0}, require {2:D0})", dNum, iNum, pNum);
                        IMap[iNum]++;
                        ddf = true;
                    }
                    else if ((iNum < 2) || (iNum > isize * 16))
                    {
                        return Debug.WriteLine(false, 1, "UnixV5.Test: in directory i={0:D0}, entry \"{1}\" has invalid i-number (is {2:D0}, require 2 <= n <= {3:D0})", dNum, name, iNum, isize * 16);
                    }
                    else if (((iNode = Inode.Get(volume, iNum)).flags & 0x6000) == 0x4000) // directory
                    {
                        DirList.Enqueue((dNum << 16) + iNum);
                    }
                    else // non-directory
                    {
                        IMap[iNum]++;
                    }
                }
                if (!df) return Debug.WriteLine(false, 1, "UnixV5.Test: in directory i={0:D0}, entry \".\" is missing", dNum);
                if (!ddf) return Debug.WriteLine(false, 1, "UnixV5.Test: in directory i={0:D0}, entry \"..\" is missing", dNum);
            }
            IMap[1]--; // root directory has no parent, so back out the implied parent link
            if (level == 4) return true;

            // level 5 - check file header allocation (return file system size and type)
            for (UInt16 iNum = 1; iNum <= isize * 16; iNum++)
            {
                iNode = Inode.Get(volume, iNum);
                if (((iNode.flags & 0x8000) == 0) && (IMap[iNum] != 0)) return Debug.WriteLine(false, 1, "UnixV5.Test: i-node {0:D0} is marked free but has {1:D0} link(s)", iNum, IMap[iNum]);
                if (((iNode.flags & 0x8000) != 0) && (IMap[iNum] == 0)) return Debug.WriteLine(false, 1, "UnixV5.Test: i-node {0:D0} is marked used but has no links", iNum);
                if (((iNode.flags & 0x8000) != 0) && (IMap[iNum] != iNode.nlinks)) return Debug.WriteLine(false, 1, "UnixV5.Test: i-node {0:D0} link count mismatch (is {1:D0}, expect {2:D0})", iNum, iNode.nlinks, IMap[iNum]);
            }
            // also check super-block free list
            p = SB.GetInt16L(206);
            for (Int32 i = 0; i < 2 * p; i += 2)
            {
                UInt16 iNum = SB.GetUInt16L(208 + i);
                if (IMap[iNum] != 0) return Debug.WriteLine(false, 1, "UnixV5.Test: i-node {0:D0} is in super-block free list, but has {1:D0} link(s)", iNum, IMap[iNum]);
            }
            if (level == 5) return true;

            // level 6 - check data block allocation (return file system size and type)
            UInt16[] BMap = new UInt16[size]; // block usage map
            for (UInt16 iNum = 1; iNum <= isize * 16; iNum++)
            {
                iNode = Inode.Get(volume, iNum);
                if ((iNode.flags & 0x8000) == 0) continue; // unused i-nodes have no blocks
                if ((iNode.flags & 0x2000) != 0) continue; // device i-nodes have no blocks
                n = (iNode.size + 511) / 512; // number of blocks required for file
                if ((iNode.flags & 0x1000) == 0)
                {
                    // i-node links to up to 8 direct blocks
                    for (p = 0; p < n; p++)
                    {
                        Int32 b = iNode[p]; // direct block
                        if (b == 0) continue;
                        if ((b < isize + 2) || (b >= size)) return Debug.WriteLine(false, 1, "UnixV5.Test: block {0:D0} of i-node {1:D0} falls outside volume block range (is {2:D0}, require {3:D0} <= n < {4:D0}", p, iNum, b, isize + 2, size);
                        if (b > volume.BlockCount) Debug.WriteLine(1, "UnixV5.Test: WARNING: block {0:D0} of i-node {1:D0} falls outside image block range (is {2:D0}, expect n < {3:D0})", p, iNum, b, volume.BlockCount);
                        if (BMap[b] != 0) return Debug.WriteLine(false, 1, "UnixV5.Test: block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[b]);
                        BMap[b] = iNum;
                    }
                }
                else
                {
                    // i-node links to up to 8 indirect blocks
                    for (p = 0; p < n; p++)
                    {
                        Int32 i = iNode[p / 256]; // indirect block
                        if (i == 0) continue;
                        if ((i < isize + 2) || (i >= size)) return Debug.WriteLine(false, 1, "UnixV5.Test: indirect block {0:D0} of i-node {1:D0} falls outside volume block range (is {2:D0}, require {3:D0} <= n < {4:D0}", p / 256, iNum, i, isize + 2, size);
                        if (i > volume.BlockCount) return Debug.WriteLine(false, 1, "UnixV5.Test: indirect block {0:D0} of i-node {1:D0} falls outside image block range (is {2:D0}, expect n < {3:D0})", p / 256, iNum, i, volume.BlockCount);
                        Int32 b = volume[i].GetUInt16L((p % 256) * 2); // direct block
                        if (b == 0) continue;
                        if ((b < isize + 2) || (b >= size)) return Debug.WriteLine(false, 1, "UnixV5.Test: block {0:D0} of i-node {1:D0} falls outside volume block range (is {2:D0}, require {3:D0} <= n < {4:D0}", p, iNum, b, isize + 2, size);
                        if (b > volume.BlockCount) Debug.WriteLine(1, "UnixV5.Test: WARNING: block {0:D0} of i-node {1:D0} falls outside image block range (is {2:D0}, expect n < {3:D0})", p, iNum, b, volume.BlockCount);
                        if (BMap[b] != 0) return Debug.WriteLine(false, 1, "UnixV5.Test: block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[b]);
                        BMap[b] = iNum;
                    }
                    for (p = 0; p < n; p += 256)
                    {
                        Int32 i = iNode[p / 256]; // indirect block
                        if (i == 0) continue;
                        if (BMap[i] != 0) return Debug.WriteLine(false, 1, "UnixV5.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", i, iNum, BMap[i]);
                        BMap[i] = iNum;
                    }
                }
            }
            // mark used blocks with 1
            for (Int32 i = 0; i < isize + 2; i++) BMap[i] = 1;
            for (Int32 i = isize + 2; i < size; i++) if (BMap[i] != 0) BMap[i] = 1;
            // mark free blocks with 2
            n = SB.GetInt16L(4); // number of blocks in super-block free block list
            for (Int32 i = 0; i < 2 * n; i += 2)
            {
                p = SB.GetUInt16L(6 + i);
                if (p == 0) continue;
                if ((p < isize + 2) || (p >= size)) return Debug.WriteLine(false, 1, "UnixV5.Test: block {0:D0} in super-block free list falls outside volume block range (require {1:D0} <= n < {2:D0})", p, isize + 2, size);
                if (BMap[p] != 0) return Debug.WriteLine(false, 1, "UnixV5.Test: block {0:D0} in super-block free list is allocated", p);
                BMap[p] = 2;
            }
            p = SB.GetUInt16L(6);
            while (p != 0)
            {
                if ((p < isize + 2) || (p >= size)) return Debug.WriteLine(false, 1, "UnixV5.Test: link block {0:D0} in free block chain falls outside volume block range (require {1:D0} <= n < {2:D0})", p, isize + 2, size);
                if (p >= volume.BlockCount) return Debug.WriteLine(false, 1, "UnixV5.Test: link block {0:D0} in free block chain falls outside image block range (expect n < {1:D0})", p, volume.BlockCount);
                Block B = volume[p];
                n = B.GetUInt16L(0);
                for (Int32 i = 0; i < 2 * n; i += 2)
                {
                    p = B.GetUInt16L(2 + i);
                    if (p == 0) continue;
                    if ((p < isize + 2) || (p >= size)) return Debug.WriteLine(false, 1, "UnixV5.Test: block {0:D0} in free block chain falls outside volume block range (require {1:D0} <= n < {2:D0})", p, isize + 2, size);
                    if (BMap[p] != 0) return Debug.WriteLine(false, 1, "UnixV5.Test: block {0:D0} in free block chain is allocated", p);
                    BMap[p] = 2;
                }
                p = B.GetUInt16L(2);
            }
            // unmarked blocks are lost
            for (Int32 i = 0; i < size; i++)
            {
                if (BMap[i] == 0) return Debug.WriteLine(false, 1, "UnixV5.Test: block {0:D0} is not allocated and not in free list", i);
            }
            if (level == 6) return true;

            return false;
        }
    }


    partial class UnixV6 : UnixV5
    {
        public UnixV6(Volume volume) : base(volume)
        {
            mType = "Unix/V6";
        }
    }

    partial class UnixV6
    {
        private static Byte[] ReadFile(Volume volume, Inode iNode)
        {
            Byte[] buf = new Byte[iNode.size];
            if ((iNode.flags & 0x1000) == 0)
            {
                // inode links to up to 8 direct blocks
                for (Int32 p = 0; p < iNode.size; p += 512)
                {
                    Int32 b = iNode[p / 512]; // direct block
                    if ((b == 0) || (b >= volume.BlockCount)) continue;
                    Int32 c = iNode.size - p;
                    volume[b].CopyTo(buf, p, 0, (c > 512) ? 512 : c);
                }
            }
            else
            {
                // inode links to up to 7 indirect blocks and 1 double-indirect
                for (Int32 p = 0; p < iNode.size; p += 512)
                {
                    Int32 b = p / 512;
                    Int32 i = b / 256;
                    if (i >= 7)
                    {
                        Int32 d = iNode[7]; // double-indirect block
                        if ((d == 0) || (d >= volume.BlockCount)) continue;
                        i = volume[d].GetUInt16L((i - 7) * 2); // indirect block
                    }
                    else
                    {
                        i = iNode[i]; // indirect block
                    }
                    if ((i == 0) || (i >= volume.BlockCount)) continue;
                    b = volume[i].GetUInt16L((b % 256) * 2); // direct block
                    if ((b == 0) || (b >= volume.BlockCount)) continue;
                    Int32 c = iNode.size - p;
                    volume[b].CopyTo(buf, p, 0, (c > 512) ? 512 : c);
                }
            }
            return buf;
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
            // level 0 - check basic volume parameters (return required block size and volume type)
            size = 512;
            type = typeof(Volume);
            if (volume == null) return false;
            if (volume.BlockSize != size) return Debug.WriteLine(false, 1, "UnixV6.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            if (level == 1)
            {
                // Unix V5 doesn't support disk labels
                size = -1;
                type = typeof(Volume);
                if (volume.BlockCount < 1) return Debug.WriteLine(false, 1, "UnixV6.Test: volume too small to contain boot block");
                return true;
            }

            // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
            size = -1;
            type = null;
            if (volume.BlockCount < 2) return Debug.WriteLine(false, 1, "UnixV6.Test: volume too small to contain super-block");
            Block SB = volume[1]; // super-block
            Int32 isize = SB.GetUInt16L(0); // number of blocks used for inodes
            Int32 n = SB.GetUInt16L(2); // file system size (in blocks)
            if (isize + 2 > n) return Debug.WriteLine(false, 1, "UnixV6.Test: super-block i-list exceeds volume size ({0:D0} > {1:D0})", isize + 2, n);
            Int32 p = SB.GetInt16L(4);  // number of blocks in super-block free block list
            if ((p < 1) || (p > 100)) return Debug.WriteLine(false, 1, "UnixV6.Test: super-block free block count invalid (is {0:D0}, require 1 <= n <= 100)", p);
            Int32 l = 2 * p;
            for (Int32 i = 0; i < l; i += 2)
            {
                if (((p = SB.GetUInt16L(6 + i)) < isize + 2) || (p >= n)) return Debug.WriteLine(false, 1, "UnixV6.Test: super-block free block {0:D0} invalid (is {1:D0}, require {2:D0} <= n < {3:D0})", i / 2, p, isize + 2, n);
            }
            p = SB.GetUInt16L(206); // number of inodes in super-block free inode list
            if (p > 100) return Debug.WriteLine(false, 1, "UnixV6.Test: super-block free i-node count invalid (is {0:D0}, require n <= 100)", p);
            l = 2 * p;
            for (Int32 i = 0; i < l; i += 2)
            {
                if (((p = SB.GetUInt16L(208 + i)) < 1) || (p > isize * 16)) return Debug.WriteLine(false, 1, "UnixV6.Test: super-block free i-number {0:D0} invalid (is {1:D0}, require 1 <= n < {2:D0})", i / 2, p, isize * 16);
            }
            size = n;
            type = typeof(UnixV6);
            if (level == 2) return true;

            // level 3 - check file headers (aka inodes) (return file system size and type)
            Inode iNode;
            for (UInt16 iNum = 1; iNum <= isize * 16; iNum++)
            {
                iNode = Inode.Get(volume, iNum);
                // sometimes a free i-node has 'nlinks' -1. or other values.
                //if (((iNode.flags & 0x8000) == 0) && (iNode.nlinks != 0) && (iNode.nlinks != 255)) return Debug.WriteLine(false, 1, "UnixV6.Test: i-node {0:D0} is free but has non-zero link count {1:D0}", iNum, iNode.nlinks);
                if (((iNode.flags & 0x8000) != 0) && (iNode.nlinks == 0)) return Debug.WriteLine(false, 1, "UnixV6.Test: i-node {0:D0} is used but has zero link count", iNum);
                if (((iNode.flags & 0x1000) == 0) && (iNode.size > 4096)) return Debug.WriteLine(false, 1, "UnixV6.Test: i-node {0:D0} size exceeds small file limit (is {1:D0}, require n <= 4096)", iNum, iNode.size);
                // since iNode.size is a 24-bit number, the below test can't actually ever fail
                if (((iNode.flags & 0x1000) != 0) && (iNode.size > 16777215)) return Debug.WriteLine(false, 1, "UnixV6.Test: i-node {0:D0} size exceeds large file limit (is {1:D0}, require n <= 16777215)", iNum, iNode.size);
            }
            if (level == 3) return true;

            // level 4 - check directory structure (return file system size and type)
            iNode = Inode.Get(volume, 1);
            if ((iNode.flags & 0xe000) != 0xc000) return Debug.WriteLine(false, 1, "UnixV6.Test: root directory i-node type/used flags invalid (is 0x{0:x4}, require 0xc000)", iNode.flags & 0xe000);
            UInt16[] IMap = new UInt16[isize * 16 + 1]; // inode usage map
            Queue<Int32> DirList = new Queue<Int32>(); // queue of directories to examine
            DirList.Enqueue((1 << 16) + 1); // parent inum in high word, directory inum in low word (root is its own parent)
            while (DirList.Count != 0)
            {
                Int32 dNum = DirList.Dequeue();
                Int32 pNum = dNum >> 16;
                dNum &= 0xffff;
                if (IMap[dNum] != 0) return Debug.WriteLine(false, 1, "UnixV6.Test: directory i-node {0:D0} appears more than once in directory structure", dNum);
                IMap[dNum]++; // assume a link to this directory from its parent (root directory gets fixed later)
                Byte[] buf = ReadFile(volume, Inode.Get(volume, dNum));
                Boolean df = false;
                Boolean ddf = false;
                for (Int32 bp = 0; bp < buf.Length; bp += 16)
                {
                    UInt16 iNum = Buffer.GetUInt16L(buf, bp);
                    if (iNum == 0) continue;
                    String name = Buffer.GetCString(buf, bp + 2, 14, Encoding.ASCII);
                    if (String.Compare(name, ".") == 0)
                    {
                        if (iNum != dNum) return Debug.WriteLine(false, 1, "UnixV6.Test: in directory i={0:D0}, entry \".\" does not refer to itself (is {1:D0}, require {2:D0})", dNum, iNum, dNum);
                        IMap[iNum]++;
                        df = true;
                    }
                    else if (String.Compare(name, "..") == 0)
                    {
                        if (iNum != pNum) return Debug.WriteLine(false, 1, "UnixV6.Test: in directory i={0:D0}, entry \"..\" does not refer to parent (is {1:D0}, require {2:D0})", dNum, iNum, pNum);
                        IMap[iNum]++;
                        ddf = true;
                    }
                    else if ((iNum < 2) || (iNum > isize * 16))
                    {
                        return Debug.WriteLine(false, 1, "UnixV6.Test: in directory i={0:D0}, entry \"{1}\" has invalid i-number (is {2:D0}, require 2 <= n <= {3:D0})", dNum, name, iNum, isize * 16);
                    }
                    else if (((iNode = Inode.Get(volume, iNum)).flags & 0x6000) == 0x4000) // directory
                    {
                        DirList.Enqueue((dNum << 16) + iNum);
                    }
                    else // non-directory
                    {
                        IMap[iNum]++;
                    }
                }
                if (!df) return Debug.WriteLine(false, 1, "UnixV6.Test: in directory i={0:D0}, entry \".\" is missing", dNum);
                if (!ddf) return Debug.WriteLine(false, 1, "UnixV6.Test: in directory i={0:D0}, entry \"..\" is missing", dNum);
            }
            IMap[1]--; // root directory has no parent, so back out the implied parent link
            if (level == 4) return true;

            // level 5 - check file header allocation (return file system size and type)
            for (UInt16 iNum = 1; iNum <= isize * 16; iNum++)
            {
                iNode = Inode.Get(volume, iNum);
                if (((iNode.flags & 0x8000) == 0) && (IMap[iNum] != 0)) return Debug.WriteLine(false, 1, "UnixV6.Test: i-node {0:D0} is marked free but has {1:D0} link(s)", iNum, IMap[iNum]);
                if (((iNode.flags & 0x8000) != 0) && (IMap[iNum] == 0)) return Debug.WriteLine(false, 1, "UnixV6.Test: i-node {0:D0} is marked used but has no links", iNum);
                if (((iNode.flags & 0x8000) != 0) && (IMap[iNum] != iNode.nlinks)) return Debug.WriteLine(false, 1, "UnixV6.Test: i-node {0:D0} link count mismatch (is {1:D0}, expect {2:D0})", iNum, iNode.nlinks, IMap[iNum]);
            }
            // also check super-block free list
            p = SB.GetInt16L(206);
            for (Int32 i = 0; i < 2 * p; i += 2)
            {
                UInt16 iNum = SB.GetUInt16L(208 + i);
                if (IMap[iNum] != 0) return Debug.WriteLine(false, 1, "UnixV6.Test: i-node {0:D0} is in super-block free list, but has {1:D0} link(s)", iNum, IMap[iNum]);
            }
            if (level == 5) return true;

            // level 6 - check data block allocation (return file system size and type)
            UInt16[] BMap = new UInt16[size]; // block usage map
            for (UInt16 iNum = 1; iNum <= isize * 16; iNum++)
            {
                iNode = Inode.Get(volume, iNum);
                if ((iNode.flags & 0x8000) == 0) continue; // unused i-nodes have no blocks
                if ((iNode.flags & 0x2000) != 0) continue; // device i-nodes have no blocks
                n = (iNode.size + 511) / 512; // number of blocks required for file
                if ((iNode.flags & 0x1000) == 0)
                {
                    // i-node links to up to 8 direct blocks
                    for (p = 0; p < n; p++)
                    {
                        Int32 b = iNode[p]; // direct block
                        if (b == 0) continue;
                        if ((b < isize + 2) || (b >= size)) return Debug.WriteLine(false, 1, "UnixV6.Test: block {0:D0} of i-node {1:D0} falls outside volume block range (is {2:D0}, require {3:D0} <= n < {4:D0}", p, iNum, b, isize + 2, size);
                        if (b > volume.BlockCount) Debug.WriteLine(1, "UnixV6.Test: WARNING: block {0:D0} of i-node {1:D0} falls outside image block range (is {2:D0}, expect n < {3:D0})", p, iNum, b, volume.BlockCount);
                        if (BMap[b] != 0) return Debug.WriteLine(false, 1, "UnixV6.Test: block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[b]);
                        BMap[b] = iNum;
                    }
                }
                else
                {
                    // i-node links to up to 7 indirect blocks and 1 double-indirect
                    for (p = 0; p < n; p++)
                    {
                        Int32 i = p / 256;
                        if (i >= 7)
                        {
                            Int32 d = iNode[7]; // double-indirect block
                            if (d == 0) continue;
                            if ((d < isize + 2) || (d >= size)) return Debug.WriteLine(false, 1, "UnixV6.Test: double-indirect block of i-node {0:D0} falls outside volume block range (is {1:D0}, require {2:D0} <= n < {3:D0}", iNum, d, isize + 2, size);
                            if (d > volume.BlockCount) return Debug.WriteLine(false, 1, "UnixV6.Test: double-indirect block of i-node {0:D0} falls outside image block range (is {1:D0}, expect n < {2:D0})", iNum, d, volume.BlockCount);
                            i = volume[d].GetUInt16L((i - 7) * 2); // indirect block
                        }
                        else
                        {
                            i = iNode[i]; // indirect block
                        }
                        if (i == 0) continue;
                        if ((i < isize + 2) || (i >= size)) return Debug.WriteLine(false, 1, "UnixV6.Test: indirect block {0:D0} of i-node {1:D0} falls outside volume block range (is {2:D0}, require {3:D0} <= n < {4:D0}", p / 256, iNum, i, isize + 2, size);
                        if (i > volume.BlockCount) return Debug.WriteLine(false, 1, "UnixV6.Test: indirect block {0:D0} of i-node {1:D0} falls outside image block range (is {2:D0}, expect n < {3:D0})", p / 256, iNum, i, volume.BlockCount);
                        Int32 b = volume[i].GetUInt16L((p % 256) * 2); // direct block
                        if (b == 0) continue;
                        if ((b < isize + 2) || (b >= size)) return Debug.WriteLine(false, 1, "UnixV6.Test: block {0:D0} of i-node {1:D0} falls outside volume block range (is {2:D0}, require {3:D0} <= n < {4:D0}", p, iNum, b, isize + 2, size);
                        if (b > volume.BlockCount) Debug.WriteLine(1, "UnixV6.Test: WARNING: block {0:D0} of i-node {1:D0} falls outside image block range (is {2:D0}, expect n < {3:D0})", p, iNum, b, volume.BlockCount);
                        if (BMap[b] != 0) return Debug.WriteLine(false, 1, "UnixV6.Test: block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", p, iNum, BMap[b]);
                        BMap[b] = iNum;
                    }
                    Boolean f = false;
                    for (p = 0; p < n; p += 256)
                    {
                        Int32 i = p / 256;
                        if (i >= 7)
                        {
                            Int32 d = iNode[7]; // double-indirect block
                            if (d == 0) continue;
                            i = volume[d].GetUInt16L((i - 7) * 2); // indirect block
                            f = true;
                        }
                        else
                        {
                            i = iNode[i]; // indirect block
                        }
                        if (i == 0) continue;
                        if (BMap[i] != 0) return Debug.WriteLine(false, 1, "UnixV6.Test: indirect block {0:D0} of i-node {1:D0} is also allocated to i-node {2:D0}", i, iNum, BMap[i]);
                        BMap[i] = iNum;
                    }
                    if (f)
                    {
                        Int32 i = iNode[7];
                        if (BMap[i] != 0) return Debug.WriteLine(false, 1, "UnixV6.Test: double-indirect block of i-node {0:D0} is also allocated to i-node {1:D0}", iNum, BMap[i]);
                        BMap[i] = iNum;
                    }
                }
            }
            // mark used blocks with 1
            for (Int32 i = 0; i < isize + 2; i++) BMap[i] = 1;
            for (Int32 i = isize + 2; i < size; i++) if (BMap[i] != 0) BMap[i] = 1;
            // mark free blocks with 2
            n = SB.GetInt16L(4); // number of blocks in super-block free block list
            for (Int32 i = 0; i < 2 * n; i += 2)
            {
                p = SB.GetUInt16L(6 + i);
                if (p == 0) continue;
                if ((p < isize + 2) || (p >= size)) return Debug.WriteLine(false, 1, "UnixV6.Test: block {0:D0} in super-block free list falls outside volume block range (require {1:D0} <= n < {2:D0})", p, isize + 2, size);
                if (BMap[p] != 0) return Debug.WriteLine(false, 1, "UnixV6.Test: block {0:D0} in super-block free list is allocated", p);
                BMap[p] = 2;
            }
            p = SB.GetUInt16L(6);
            while (p != 0)
            {
                if ((p < isize + 2) || (p >= size)) return Debug.WriteLine(false, 1, "UnixV6.Test: link block {0:D0} in free block chain falls outside volume block range (require {1:D0} <= n < {2:D0})", p, isize + 2, size);
                if (p >= volume.BlockCount) return Debug.WriteLine(false, 1, "UnixV6.Test: link block {0:D0} in free block chain falls outside image block range (expect n < {1:D0})", p, volume.BlockCount);
                Block B = volume[p];
                n = B.GetUInt16L(0);
                for (Int32 i = 0; i < 2 * n; i += 2)
                {
                    p = B.GetUInt16L(2 + i);
                    if (p == 0) continue;
                    if ((p < isize + 2) || (p >= size)) return Debug.WriteLine(false, 1, "UnixV6.Test: block {0:D0} in free block chain falls outside volume block range (require {1:D0} <= n < {2:D0})", p, isize + 2, size);
                    if (BMap[p] != 0) return Debug.WriteLine(false, 1, "UnixV6.Test: block {0:D0} in free block chain is allocated", p);
                    BMap[p] = 2;
                }
                p = B.GetUInt16L(2);
            }
            // unmarked blocks are lost
            for (Int32 i = 0; i < size; i++)
            {
                if (BMap[i] == 0) return Debug.WriteLine(false, 1, "UnixV6.Test: block {0:D0} is not allocated and not in free list", i);
            }
            if (level == 6) return true;

            return false;
        }
    }

    partial class UnixV7 : FileSystem
    {
        private const Int32 INOPB = 8;
        private const Int32 ISIZE = 64;
        private const Int32 ROOT_INUM = 2;

        protected class Inode
        {
            private Volume mVol;
            public Int32 iNum;
            public UInt16 di_mode;
            public Int16 di_nlink;
            public Int16 di_uid;
            public Int16 di_gid;
            public Int32 di_size;
            public Int32[] di_addr;
            public Int32 di_atime;
            public Int32 di_mtime;
            public Int32 di_ctime;

            private Inode(Volume volume, Int32 iNumber)
            {
                mVol = volume;
                iNum = iNumber;
            }

            public Volume Volume
            {
                get { return mVol; }
            }

            public Int32 this[Int32 index]
            {
                get
                {
                    if (index < 10)
                    {
                        return di_addr[index]; // desired block pointer is in i-node
                    }
                    else
                    {
                        Int32 i;
                        if ((index -= 10) < 128)
                        {
                            i = di_addr[10]; // desired block pointer is in block i
                        }
                        else
                        {
                            Int32 d;
                            if ((index -= 128) < 128 * 128)
                            {
                                d = di_addr[11]; // indirect block pointer i is in block d
                            }
                            else
                            {
                                Int32 t = di_addr[12]; // double-indirect block pointer d is in block t
                                if (t == 0) return 0;
                                d = mVol[t].GetInt32P(4 * (index -= 128 * 128) / (128 * 128));
                                index %= 128 * 128;
                            }
                            if (d == 0) return 0;
                            i = mVol[d].GetInt32P(4 * index / 128);
                            index %= 128;
                        }
                        if (i == 0) return 0;
                        return mVol[i].GetInt32P(4 * index);
                    }
                }
            }

            public static Inode Get(Volume volume, Int32 iNumber)
            {
                Block B = volume[2 + (iNumber - 1) / INOPB];
                Int32 offset = ((iNumber - 1) % INOPB) * ISIZE;
                Inode I = new Inode(volume, iNumber);
                I.di_mode = B.GetUInt16L(ref offset);
                I.di_nlink = B.GetInt16L(ref offset);
                I.di_uid = B.GetInt16L(ref offset);
                I.di_gid = B.GetInt16L(ref offset);
                I.di_size = B.GetInt32P(ref offset);
                I.di_addr = new Int32[13];
                for (Int32 i = 0; i < 13; i++)
                {
                    I.di_addr[i] = B.GetByte(ref offset) << 16;
                    I.di_addr[i] |= B.GetUInt16L(ref offset);
                }
                offset++;
                I.di_atime = B.GetInt32P(ref offset);
                I.di_mtime = B.GetInt32P(ref offset);
                I.di_ctime = B.GetInt32P(ref offset);
                return I;
            }
        }

        private Volume mVol;
        private String mType;
        private Int32 mRoot;
        private Inode mDirNode;
        private String mDir;

        public UnixV7(Volume volume)
        {
            mVol = volume;
            mType = "Unix/V7";
            mRoot = ROOT_INUM;
            mDirNode = Inode.Get(volume, ROOT_INUM);
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
        }

        public override void ListDir(String fileSpec, TextWriter output)
        {
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
        }

        public override void DumpFile(String fileSpec, TextWriter output)
        {
            Int32 n;
            if (Int32.TryParse(fileSpec, out n))
            {
                Inode I = Inode.Get(mVol, n);
                Debug.WriteDiag("Inode {0:D0}: size={1:D0} mode={2}{3} nlink={4:D0} uid={5:D0} gid={6:D0}", I.iNum, I.di_size, (I.di_mode == 0) ? null : "0", Convert.ToString(I.di_mode, 8), I.di_nlink, I.di_uid, I.di_gid);
                for (Int32 i = 0; i < 13; i++) Debug.WriteDiag("di_addr[{0:D0}]: {1:D0}", i, I.di_addr[i]);
                Program.Dump(null, ReadFile(Inode.Get(mVol, n)), output);
            }
        }

        public override String FullName(String fileSpec)
        {
            throw new NotImplementedException();
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            throw new NotImplementedException();
        }

        public override bool SaveFS(String fileName, String format)
        {
            throw new NotImplementedException();
        }
    }

    partial class UnixV7
    {
        private static Byte[] ReadFile(Inode iNode)
        {
            Byte[] buf = new Byte[iNode.di_size];
            Int32 bp = 0;
            while (bp < buf.Length)
            {
                Int32 n = bp / 512; // file block number
                Int32 b = iNode[n]; // volume block number
                if (b != 0) iNode.Volume[b].CopyTo(buf, bp);
                bp += 512;
            }
            return buf;
        }
    }

    partial class UnixV7 : IFileSystemAuto
    {
        public static TestDelegate GetTest()
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
        public static Boolean Test(Volume volume, Int32 level, out Int32 size, out Type type)
        {
            // level 0 - check basic volume parameters (return required block size and volume type)
            size = 512;
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
            Int32 n = SB.GetInt16L(ref bp); // SB[6] - number of blocks in super-block free block list
            if ((n < 0) || (n > 50)) return Debug.WriteInfo(false, "UnixV7.Test: super-block free block count invalid (is {0:D0}, require 0 <= n <= 50)", n);
            Int32 p = SB.GetInt32P(bp); // SB[8] - free block chain next pointer
            if ((p != 0) && ((p < s_isize) || (p >= s_fsize))) return Debug.WriteInfo(false, "UnixV7.Test: super-block free chain pointer invalid (is {0:D0}, require {1:D0} <= n < {2:D0})", p, s_isize, s_fsize);
            for (Int32 i = 1; i < n; i++) // SB[10] - free block list
            {
                if (((p = SB.GetInt32P(bp + 4 * i)) < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: super-block free block {0:D0} invalid (is {1:D0}, require {2:D0} <= n < {3:D0})", i, p, s_isize, s_fsize);
            }
            bp += 50 * 4;
            n = SB.GetUInt16L(ref bp); // SB[208] - number of inodes in super-block free inode list
            if (n > 100) return Debug.WriteInfo(false, "UnixV7.Test: super-block free i-node count invalid (is {0:D0}, require n <= 100)", n);
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
                iNode = Inode.Get(volume, iNum);
                n = (iNode.di_mode & 0xf000) >> 12;
                if ((n != 0) && (n != 8) && (n != 4) && ((n & 10) != 2)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} mode 0{1} (octal) invalid", iNum, Convert.ToString(iNode.di_mode, 8));
                if (iNode.di_nlink < 0) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} link count invalid (is {1:D0}, require n >= 0)", iNum, iNode.di_nlink);
                // special case: i-node 1 is used but not linked
                if ((n != 0) && (iNode.di_nlink == 0) && (iNum > 1)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is used but has zero link count", iNum);
                if ((n == 0) && (iNode.di_nlink != 0)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is free but has non-zero link count {1:D0}", iNum, iNode.di_nlink);
                if ((iNode.di_size < 0) || (iNode.di_size > 1082201088)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} size invalid (is {1:D0}, require 0 <= n <= 1082201088)", iNum, iNode.di_size);
                if ((n == 0) && (iNode.di_size != 0)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is free but has non-zero size {1:D0}", iNum, iNode.di_size);
                n = (iNode.di_size + 511) / 512; // number of blocks in file
                if (n > 0)
                {
                    // verify validity of i-node direct block pointers
                    for (Int32 i = 0; i < 10; i++)
                    {
                        if (i >= n) break;
                        if ((p = iNode.di_addr[i]) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                    }
                }
                p = iNode.di_addr[10];
                if ((n > 10) && (p != 0))
                {
                    // verify validity of indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 128; i++)
                    {
                        if (10 + i >= n) break;
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                    }
                }
                p = iNode.di_addr[11];
                if ((n > 10 + 128) && (p != 0))
                {
                    // verify validity of double-indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 128; i++)
                    {
                        if (10 + 128 + 128 * i >= n) break;
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                        Block B2 = volume[p];
                        for (Int32 j = 0; j < 128; j++)
                        {
                            if (10 + 128 + 128 * i + j >= n) break;
                            if ((p = B2.GetInt32P(4 * j)) == 0) continue;
                            if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                            if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} data block {1:D0}", iNum, p);
                        }
                    }
                }
                p = iNode.di_addr[12];
                if ((n > 10 + 128 + 128 * 128) && (p != 0))
                {
                    // verify validity of triple-indirect block pointers
                    if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                    if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                    Block B1 = volume[p];
                    for (Int32 i = 0; i < 128; i++)
                    {
                        if (10 + 128 + 128 * 128 + 128 * 128 * i >= n) break;
                        if ((p = B1.GetInt32P(4 * i)) == 0) continue;
                        if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                        if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                        Block B2 = volume[p];
                        for (Int32 j = 0; j < 128; j++)
                        {
                            if (10 + 128 + 128 * 128 + 128 * 128 * i + 128 * j >= n) break;
                            if ((p = B2.GetInt32P(4 * j)) == 0) continue;
                            if ((p < s_isize) || (p >= s_fsize)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} contains invalid block pointer (is {1:D0}, require {2:D0} <= n < {3:D0})", iNum, p, s_isize, s_fsize);
                            if ((p < 0) || (p >= volume.BlockCount)) return Debug.WriteInfo(false, "UnixV7.Test: volume too small to contain i-node {0:D0} indirect block {1:D0}", iNum, p);
                            Block B3 = volume[p];
                            for (Int32 k = 0; k < 128; k++)
                            {
                                if (10 + 128 + 128 * 128 + 128 * 128 * i + 128 * j + k >= n) break;
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
            iNode = Inode.Get(volume, ROOT_INUM);
            if ((iNode.di_mode & 0xf000) != 0x4000) return Debug.WriteInfo(false, "UnixV7.Test: root directory i-node mode invalid (is 0x{0:x4}, require 0x4nnn)", iNode.di_mode);
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
                Byte[] buf = ReadFile(Inode.Get(volume, dNum));
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
                    else if (((iNode = Inode.Get(volume, iNum)).di_mode & 0xf000) == 0x4000) // directory
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
                iNode = Inode.Get(volume, iNum);
                n = (iNode.di_mode & 0xf000) >> 12;
                if ((n == 0) && (IMap[iNum] != 0)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is free but has {1:D0} link(s)", iNum, IMap[iNum]);
                if ((n != 0) && (IMap[iNum] == 0)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} is used but has no links", iNum);
                if ((n != 0) && (IMap[iNum] != iNode.di_nlink)) return Debug.WriteInfo(false, "UnixV7.Test: i-node {0:D0} link count mismatch (is {1:D0}, expect {2:D0})", iNum, iNode.di_nlink, IMap[iNum]);
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
                iNode = Inode.Get(volume, iNum);
                n = (iNode.di_mode & 0xf000) >> 12;
                if (n == 0) continue; // unused i-nodes have no blocks allocated
                if ((n & 2) != 0) continue; // neither do device i-nodes
                n = (iNode.di_size + 511) / 512; // number of blocks required for file
                // mark data blocks used
                for (Int32 i = 0; i < n; i++)
                {
                    p = iNode[i];
                    if (p == 0) continue;
                    if (BMap[p] != 0) return Debug.WriteInfo(false, "UnixV7.Test: data block #{0:D0} ({1:D0}) of i-node {2:D0} is also allocated to i-node {3:D0}", i, p, iNum, BMap[p]);
                    BMap[p] = iNum;
                }
                // mark indirect blocks used
                for (Int32 i = 10; i < 13; i++)
                {
                    p = iNode.di_addr[i];
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
                for (Int32 i = 1; i < 128; i++)
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
}
