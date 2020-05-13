// CPM.cs
// Copyright © 2020 Kenneth Gober
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


// CP/M 1.4
// http://www.seasip.info/Cpm/format14.html


using System;
using System.IO;
using System.Text;

namespace FSX
{
    partial class CPM : FileSystem
    {
        private Volume mVol;
        private String mType;
        private ClusteredVolume mBlocks;

        public CPM(Volume volume)
        {
            mVol = volume;
            mType = "CP/M";
            mBlocks = new ClusteredVolume(volume, 8, 52);
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
            for (Int32 bn = 0; bn < 2; bn++)
            {
                Block B = mBlocks[bn];
                for (Int32 bp = 0; bp < B.Size; bp += 32)
                {
                    Byte b = B[bp + 0];
                    if ((b != 0) && (b != 128)) continue;
                    String stat = (b == 0) ? String.Empty : "[HID]";
                    b = B[bp + 12];
                    if (b != 0) continue;
                    String name = B.GetString(bp + 1, 8, DefaultEncoding);
                    String ext = B.GetString(bp + 9, 3, DefaultEncoding);
                    output.WriteLine("{0} {1} {2}", name, ext, stat);
                }
            }
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            for (Int32 bn = 0; bn < 2; bn++)
            {
                Block B = mBlocks[bn];
                for (Int32 bp = 0; bp < B.Size; bp += 32)
                {
                    Byte b = B[bp + 0];
                    String stat = (b == 0) ? "[   ]" : (b == 0x80) ? "[HID]" : (b == 0xe5) ? "[DEL]" : String.Format("[{0:x2}h]", b);
                    String name = B.GetString(bp + 1, 8, DefaultEncoding);
                    String type = B.GetString(bp + 9, 3, DefaultEncoding);
                    Byte ext = B[bp + 12];
                    Byte ns = B[bp + 15];
                    output.WriteLine("{0} {1} {2} ({3:D2}) {4:D0}", stat, name, type, ext, ns);
                }
            }
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
            if ((fileName == null) || (fileName.Length == 0)) return false;
            FileStream f = new FileStream(fileName, FileMode.Create);
            Byte[] buf = new Byte[mVol.BlockSize];
            for (Int32 i = 0; i < mVol.BlockCount; i++)
            {
                mVol[i].CopyTo(buf, 0);
                f.Write(buf, 0, buf.Length);
            }
            f.Close();
            return true;
        }
    }

    partial class CPM : IFileSystemAuto
    {
        public static TestDelegate xGetTest()
        {
            return CPM.Test;
        }

        public static Boolean Test(Volume vol, Int32 level, out Int32 size, out Type type)
        {
            // level 0 - check basic volume parameters (return required block size and volume type)
            size = 128;
            type = typeof(CHSVolume);
            if (vol == null) return false;
            if (!(vol is CHSVolume)) return Debug.WriteLine(false, 1, "CPM.Test: volume must be track-oriented (e.g. 'CHSVolume')");
            CHSVolume volume = vol as CHSVolume;
            if (volume.BlockSize != size) return Debug.WriteLine(false, 1, "CPM.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (volume.MinHead != volume.MaxHead) return Debug.WriteLine(false, 1, "CPM.Test: volume must be logically single-sided");
            if (volume.MinCylinder != 0) return Debug.WriteLine(false, 1, "CPM.Test: volume track numbering must start at 0 (is {0:D0})", volume.MinCylinder);
            if (volume.MinSector() != 1) return Debug.WriteLine(false, 1, "CPM.Test: volume sector numbering must start at 1 (is {0:D0})", volume.MinSector());
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            size = volume.BlockCount;
            if (level == 1)
            {
                return true;
            }

            // level 2 - check volume descriptor (aka home/super block) (return volume size and type)
            type = typeof(CPM);
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
