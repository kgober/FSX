// FAT.cs
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


// FAT File System Structure
//
// http://www.ecma-international.org/publications/files/ECMA-ST/Ecma-107.pdf


// Future Improvements / To Do
// improve FAT12 ListDir
// implement FAT12 ChangeDir
// implement FAT12 ReadFile/ListFile/DumpFile
// add support for FAT16


using System;
using System.IO;
using System.Text;

namespace FSX
{
    partial class FAT12 : FileSystem
    {
        private Volume mVol;
        private String mType;
        private String mDir;
        private Int32 mFAT1;
        private Int32 mFAT2;
        private Int32 mRoot;
        private Int32 mData;

        public FAT12(Volume volume)
        {
            mVol = volume;
            mType = "FAT12";
            mDir = "\\";
            Block B = volume[0];
            Int32 n = B.GetUInt16L(11); // logical sector size
            Int32 m = B.GetUInt16L(19); // logical sector count
            // get parameters from BPB (if it appears to be valid/present)
            if ((n == volume.BlockSize) && (m == volume.BlockCount))
            {
                n = B.GetByte(16); // number of FATs
                m = B.GetUInt16L(22); // logical sectors per FAT
                mFAT1 = B.GetUInt16L(14); // reserved logical sectors (includes boot sector)
                mFAT2 = (n == 1) ? -1 : mFAT1 + m;
                mRoot = (n == 1) ? mFAT1 + m : mFAT2 + m;
                mData = mRoot + B.GetUInt16L(17) * 32 / volume.BlockSize;
                return;
            }
            // otherwise use defaults based on volume size:
            switch (volume.BlockCount)
            {
                case 320: // 160KB SSDD
                    mFAT1 = 1;
                    mFAT2 = 2;
                    mRoot = 3;
                    mData = 7;
                    break;
                //case 360: // 180KB SSDD
                //    mFAT1 = 1;
                //    mFAT2 = 3;
                //    mRoot = 5;
                //    mData = 12;
                //    break;
                default:
                    Debug.WriteLine(1, "FAT12: No BPB and volume size {0:D0}x{1:D0} not recognized", volume.BlockCount, volume.BlockSize);
                    return;
            }
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
            Int32 p = mRoot;
            while (p < mData)
            {
                Block B = mVol[p++];
                for (Int32 bp = 0; bp < B.Size; bp += 32)
                {
                    Byte b = B.GetByte(bp);
                    if ((b == 0) || (b == 0xe5)) continue; // unused directory entry
                    String fnam = B.GetString(bp, 8, Encoding.ASCII);
                    String fext = B.GetString(bp + 8, 3, Encoding.ASCII);
                    Byte attr = B.GetByte(bp + 11);
                    UInt16 w = B.GetUInt16L(bp + 22);
                    Int32 th = (w & 0x0000f800) >> 11;
                    Int32 tm = (w & 0x000007e0) >> 5;
                    Int32 ts = (w & 0x0000001f) << 1;
                    String ftim = (w == 0) ? "        " : String.Format("{0:D2}:{1:D2}:{2:D2}", th, tm, ts);
                    w = B.GetUInt16L(bp + 24);
                    Int32 dy = (w & 0x0000fe00) >> 9;
                    Int32 dm = (w & 0x000001e0) >> 5;
                    Int32 dd = (w & 0x0000001f);
                    String fdat = (dd == 0) ? "          " : String.Format("{0:D2}/{1:D2}/{2:D4}", dm, dd, 1980 + dy);
                    Int32 fptr = B.GetUInt16L(bp + 26);
                    Int32 flen = B.GetUInt16L(bp + 28) | (B.GetUInt16L(bp + 30) << 16);
                    output.WriteLine("{0,-8} {1,-3}  {2,13:N0}  {3} {4} [{5,4:D}]", fnam, fext, flen, fdat, ftim, fptr);
                }
            }
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
        }

        public override void DumpFile(String fileSpec, TextWriter output)
        {
        }

        public override String FullName(String fileSpec)
        {
            throw new NotImplementedException();
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            throw new NotImplementedException();
        }

        public override Boolean SaveFS(String fileName, String format)
        {
            throw new NotImplementedException();
        }
    }

    partial class FAT12 : IFileSystemAuto
    {
        public static TestDelegate GetTest()
        {
            return FAT12.Test;
        }

        public static Boolean Test(Volume volume, Int32 level, out Int32 size, out Type type)
        {
            // level 0 - check basic volume parameters (return required block size and volume type)
            size = 512;
            type = typeof(Volume);
            if (volume == null) return false;
            Block B = volume[0];
            Int32 n = B.GetUInt16L(11); // logical sector size
            Int32 m = B.GetUInt16L(19); // logical sector count
            if ((n == volume.BlockSize) && (m == volume.BlockCount))
            {
                // BPB appears to be valid
                size = n;
            }
            else
            {
                // no BPB, check for common sizes
                size = 512;
                if (volume.BlockSize != size) return Debug.WriteLine(false, 1, "FAT12.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
                switch (volume.BlockCount)
                {
                    case 320:
                        break;
                    default:
                        return Debug.WriteLine(false, 1, "FAT12.Test: no BPB and volume size {0:D0}x{1:D0} not recognized", volume.BlockCount, volume.BlockSize);
                }
            }
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            size = volume.BlockCount;
            if (level == 1)
            {
                return true;
            }

            // level 2 - check volume descriptor (aka home/super block) (return volume size and type)
            type = typeof(FAT12);
            if (level == 2)
            {
                return true;
            }

            // level 3 - check file headers (aka inodes) (return volume size and type)
            if (level == 3) return true;

            // level 4 - check directory structure (return volume size and type)
            if (level == 4) return true;

            // level 5 - check file header allocation (return volume size and type)
            if (level == 5) return true;

            // level 6 - check data block allocation (return volume size and type)
            if (level == 6) return true;

            return false;
        }
    }
}
