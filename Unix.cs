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
//
// v5 i-node flags
//  0x8000  allocated / in use
//  0x6000  file type (file=0x0000 cdev=0x2000 dir=0x4000 bdev=0x6000)
//  0x1000  large file (addr[] has indirect blocks, instead of direct)
//  0x0800  setuid
//  0x0400  setgid
//  0x01c0  owner permissions (r=0x0100 w=0x0080 x=0x0040)
//  0x0038  group permissions (r=0x0020 w=0x0010 x=0x0008)
//  0x0007  world permissions (r=0x0004 w=0x0002 x=0x0001)


// Improvements / To Do
// implement CheckVTOC level 3 (check inode allocation)
// implement CheckVTOC level 4 (check block allocation)
// support Unix v6 inode format (and identify when to use it)
// support Unix v7 file system format
// support 2BSD file system format (v7 with 1KB blocks)
// support BSD Fast File System (FFS)
// allow files to be written/deleted in images


using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FSX
{
    partial class Unix
    {
        public static Unix Try(Disk disk)
        {
            Program.Debug(1, "Unix.Try: {0}", disk.Source);

            if ((disk.BlockSize != 512) && ((512 % disk.BlockSize) == 0))
            {
                return Try(new ClusteredDisk(disk, 512 / disk.BlockSize, 0));
            }
            else if (disk.BlockSize != 512)
            {
                Program.Debug(1, "Volume block size = {0:D0} (must be 512)", disk.BlockSize);
                return null;
            }

            Int32 size = CheckVTOC(disk, 2);
            if (size < 2) return null;
            else if (size != disk.BlockCount) return new Unix(new PaddedDisk(disk, size - disk.BlockCount));
            else return new Unix(disk);
        }

        // level 0 - check basic disk parameters
        // level 1 - check super-block (and return volume size)
        // level 2 - check directory structure (and return volume size)
        public static Int32 CheckVTOC(Disk disk, Int32 level)
        {
            if (disk == null) throw new ArgumentNullException("disk");
            if ((level < 0) || (level > 2)) throw new ArgumentOutOfRangeException("level");

            // level 0 - check basic disk parameters
            if (disk.BlockSize != 512)
            {
                Program.Debug(1, "Disk block size = {0:D0} (must be 512)", disk.BlockSize);
                return -1;
            }

            // ensure disk is at least large enough to contain root directory inode
            if (disk.BlockCount < 3)
            {
                Program.Debug(1, "Disk too small to contain root directory inode");
                return -1;
            }
            if (level == 0) return 0;

            // level 1 - check super-block (and return volume size)
            Block B = disk[1];
            Int32 isize = B.ToUInt16(0); // number of blocks used for inodes
            Int32 fsize = B.ToUInt16(2); // file system size (in blocks)
            if (fsize < isize + 2)
            {
                Program.Debug(1, "I-list size in super-block exceeds volume size ({0:D0} > {1:D0})", isize + 2, fsize);
                return 0;
            }
            Int32 p = B.ToInt16(4); // number of blocks in super-block free block list
            if ((p < 1) || (p > 100))
            {
                Program.Debug(1, "Free block count in super-block invalid (is {0:D0}, expected 1 <= n <= 100)", p);
                return 0;
            }
            Int32 n = 2 * p;
            for (Int32 i = 0; i < n; i += 2)
                if (((p = B.ToUInt16(6 + i)) < isize + 2) || (p >= fsize))
                {
                    Program.Debug(1, "Free block {0:D0} in super-block invalid (is {1:D0}, expected {2:D0} <= n < {3:D0})", i / 2, p, isize + 2, fsize);
                    return 0;
                }
            p = B.ToInt16(206); // number of inodes in super-block free inode list
            if (p > 100)
            {
                Program.Debug(1, "Free inode count in super-block invalid (is {0:D0}, expected n <= 100)", p);
                return 0;
            }
            n = 2 * p;
            for (Int32 i = 0; i < n; i += 2)
                if (((p = B.ToUInt16(208 + i)) < 1) || (p > isize * 16))
                {
                    Program.Debug(1, "Free inode {0:D0} in super-block invalid (is {1:D0}, expected 1 <= n <= {2:D0})", i / 2, p, isize * 16);
                    return 0;
                }
            if (level == 1) return fsize;

            // level 2 - check directory structure (and return volume size)
            Inode iNode = Inode.Get(disk, 1);
            if ((iNode.flags & 0xe000) != 0xc000)
            {
                Program.Debug(1, "Root directory inode type/used flags invalid (is 0x{0:X4}, expected 0xC000)", iNode.flags & 0xe000);
                return 1;
            }
            BitArray IMap = new BitArray(isize * 16 + 1, false); // which inodes have been seen already
            Queue<Int32> DirList = new Queue<Int32>(); // which directories need to be looked at
            DirList.Enqueue((1 << 16) + 1);
            while (DirList.Count != 0)
            {
                Int32 dNum = DirList.Dequeue();
                Int32 pNum = dNum >> 16;
                dNum &= 0xffff;
                if (IMap[dNum])
                {
                    Program.Debug(1, "Directory i-number {0:D0} appears more than once in directory structure", dNum);
                    return 1;
                }
                IMap[dNum] = true;
                Byte[] data = ReadFile(disk, Inode.Get(disk, dNum));
                Boolean sf = false;
                Boolean pf = false;
                for (Int32 bp = 0; bp < data.Length; bp += 16)
                {
                    Int32 iNum = BitConverter.ToUInt16(data, bp);
                    if (iNum == 0) continue;
                    for (p = 2; p < 16; p++) if (data[bp + p] == 0) break;
                    String name = Encoding.ASCII.GetString(data, bp + 2, p - 2);
                    if (String.Compare(name, ".") == 0)
                    {
                        if (iNum != dNum)
                        {
                            Program.Debug(1, "In Directory i={0:D0}, entry \".\" does not refer to self (is {0:D0}, expected {1:D0})", dNum, iNum, dNum);
                            return 1;
                        }
                        sf = true;
                    }
                    else if (String.Compare(name, "..") == 0)
                    {
                        if (iNum != pNum)
                        {
                            Program.Debug(1, "In Directory i={0:D0}, entry \"..\" does not refer to parent (is {0:D0}, expected {1:D0})", dNum, iNum, pNum);
                            return 1;
                        }
                        pf = true;
                    }
                    else if ((iNum < 2) || (iNum > isize * 16))
                    {
                        Program.Debug(1, "In Directory i={0:D0}, entry \"{1}\" has invalid i-number (is {2:D0}, expected 2 <= n <= {3:D0})", dNum, name, iNum, isize * 16);
                        return 1;
                    }
                    else if (((iNode = Inode.Get(disk, iNum)).flags & 0x8000) == 0)
                    {
                        Program.Debug(1, "In Directory i={0:D0}, entry \"{1}\" links to unallocated i-node {2:D0}", dNum, name, iNum);
                        return 1;
                    }
                    else if ((iNode.flags & 0x6000) == 0x4000)
                    {
                        DirList.Enqueue((dNum << 16) + iNum);
                    }
                }
                if (!sf || !pf)
                {
                    Program.Debug(1, "In Directory i={0:D0}, entry \".\" or \"..\" is missing", dNum);
                    return 1;
                }
            }
            return fsize;

            // level 3 - check inode allocation (and return volume size)
            // TODO

            // level 4 - check block allocation (and return volume size)
            // TODO
        }

        private static Byte[] ReadFile(Disk disk, Inode iNode)
        {
            Byte[] buf = new Byte[iNode.size];
            if ((iNode.flags & 0x1000) == 0)
            {
                // Unix v5/v6 - inode links to 8 direct blocks
                for (Int32 p = 0; p < iNode.size; p += 512)
                {
                    Int32 b = iNode[p / 512]; // direct block
                    if (b == 0) continue;
                    Int32 c = iNode.size - p;
                    disk[b].CopyTo(buf, p, 0, (c > 512) ? 512 : c);
                }
            }
            else
            {
                // Unix v5 - inode links to 8 indirect blocks
                // TODO: handle Unix v6
                for (Int32 p = 0; p < iNode.size; p += 512)
                {
                    Int32 b = p / 512;
                    Int32 i = iNode[b / 256]; // indirect block
                    if (i == 0) continue;
                    b = disk[i].ToUInt16((b % 256) * 2); // direct block
                    if (b == 0) continue;
                    Int32 c = iNode.size - p;
                    disk[b].CopyTo(buf, p, 0, (c > 512) ? 512 : c);
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
            Program.Debug(2, "Regex: {0} => {1}", pattern, p);
            return new Regex(p);
        }
    }

    partial class Unix : FileSystem
    {
        private class Inode
        {
            public Int32 iNum;
            public UInt16 flags;
            public Byte nlinks;
            public Byte uid;
            public Byte gid;
            public Int32 size;
            public UInt16[] addr;
            // 2 words for actime
            // 2 words for modtime

            public Inode(Int32 iNumber)
            {
                iNum = iNumber;
            }

            public UInt16 this[Int32 index]
            {
                get { return addr[index]; }
            }

            public static Inode Get(Disk disk, Int32 iNum)
            {
                Int32 block = 2 + (iNum - 1) / 16;
                Int32 offset = ((iNum - 1) % 16) * 32;
                return Get(disk[block], offset, iNum);
            }

            public static Inode Get(Block block, Int32 offset, Int32 iNum)
            {
                Inode I = new Inode(iNum);
                I.flags = block.ToUInt16(ref offset);
                I.nlinks = block.ToByte(ref offset);
                I.uid = block.ToByte(ref offset);
                I.gid = block.ToByte(ref offset);
                I.size = block.ToByte(ref offset) << 16;
                I.size += block.ToUInt16(ref offset);
                I.addr = new UInt16[8];
                for (Int32 i = 0; i < 8; i++) I.addr[i] = block.ToUInt16(ref offset);
                return I;
            }
        }

        private Disk mDisk;
        private String mType;
        private Int32 mRoot;
        private Inode mDirNode;
        private String mDir;

        public Unix(Disk disk)
        {
            mDisk = disk;
            mType = "Unix";
            mRoot = 1;
            mDirNode = Inode.Get(disk, 1);
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
            //

            // count number of blocks used
            Int32 n = 0;
            Regex RE = Regex(fileSpec);
            Byte[] data = ReadFile(mDisk, dirNode);
            for (Int32 bp = 0; bp < data.Length; bp += 16)
            {
                Int32 iNum = BitConverter.ToUInt16(data, bp);
                if (iNum == 0) continue;
                for (p = 2; p < 16; p++) if (data[bp + p] == 0) break;
                name = Encoding.ASCII.GetString(data, bp + 2, p - 2);
                if (!RE.IsMatch(name)) continue;
                Inode iNode = Inode.Get(mDisk, iNum);
                n += (iNode.size + 511) / 512;
            }

            // show directory listing
            output.WriteLine("total {0:D0}", n);
            for (Int32 bp = 0; bp < data.Length; bp += 16)
            {
                Int32 iNum = BitConverter.ToUInt16(data, bp);
                if (iNum == 0) continue;
                for (p = 2; p < 16; p++) if (data[bp + p] == 0) break;
                name = Encoding.ASCII.GetString(data, bp + 2, p - 2);
                if (!RE.IsMatch(name)) continue;
                Inode iNode = Inode.Get(mDisk, iNum);
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
            String buf = encoding.GetString(ReadFile(mDisk, iNode));
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
            Program.Dump(null, ReadFile(mDisk, iNode), output, 16, 512, Program.DumpOptions.ASCII);
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
            return ReadFile(mDisk, iNode);
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
                dirNode = Inode.Get(mDisk, mRoot);
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
                dirName = String.Concat(dirName, name, "/");
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
            Byte[] data = ReadFile(mDisk, dirNode);
            for (Int32 bp = 0; bp < data.Length; bp += 16)
            {
                Int32 iNum = BitConverter.ToUInt16(data, bp);
                if (iNum == 0) continue;
                for (p = 2; p < 16; p++) if (data[bp + p] == 0) break;
                String name = Encoding.ASCII.GetString(data, bp + 2, p - 2);
                if (RE.IsMatch(name))
                {
                    iNode = Inode.Get(mDisk, iNum);
                    pathName = String.Concat(dirName, name, ((iNode.flags & 0x6000) == 0x4000) ? "/" : null);
                    return true;
                }
            }
            return false;
        }
    }
}
