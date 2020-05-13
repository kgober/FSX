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
using System.Text.RegularExpressions;

namespace FSX
{
    partial class CPM : FileSystem
    {
        private const Int32 BLOCK_SIZE = 1024;

        private Volume mVol;
        private String mType;
        private ClusteredVolume mBlocks;
        private Byte[][] mDir;

        public CPM(Volume volume)
        {
            mVol = volume;
            mType = "CP/M";
            mBlocks = new ClusteredVolume(volume, BLOCK_SIZE / volume.BlockSize, 52);
            mDir = new Byte[64][];
            Int32 p = 0;
            for (Int32 bn = 0; bn < 2; bn++)
            {
                Block B = mBlocks[bn];
                for (Int32 bp = 0; bp < B.Size; bp += 32)
                {
                    Byte[] DE = new Byte[32];
                    B.CopyTo(DE, 0, bp, 32);
                    mDir[p++] = DE;
                }
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
            for (Int32 dp = 0; dp < mDir.Length; dp++)
            {
                Byte[] DE = mDir[dp];
                Byte b = DE[0];
                if ((b != 0) && (b != 128)) continue;
                if (DE[12] != 0) continue;
                if (!RE.IsMatch(Buffer.GetString(DE, 1, 11, DefaultEncoding))) continue;
                String stat = (b == 0) ? "   " : "[H]";
                String name = Buffer.GetString(DE, 1, 8, DefaultEncoding);
                String type = Buffer.GetString(DE, 9, 3, DefaultEncoding);
                Int32 nr = DE[15];
                for (Int32 dq = 0; dq < mDir.Length; dq++)
                {
                    DE = mDir[dq];
                    b = DE[0];
                    if ((b != 0) && (b != 128)) continue;
                    if (DE[12] == 0) continue;
                    String nm = Buffer.GetString(DE, 1, 8, DefaultEncoding);
                    if (nm != name) continue;
                    String ty = Buffer.GetString(DE, 9, 3, DefaultEncoding);
                    if (ty != type) continue;
                    nr += DE[15];
                }
                output.WriteLine("{0} {1} {2}  {3:D0}", stat, name, type, nr);
            }
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) fileSpec = "*.*";
            Regex RE = Regex(fileSpec);
            for (Int32 dp = 0; dp < mDir.Length; dp++)
            {
                Byte[] DE = mDir[dp];
                if (!RE.IsMatch(Buffer.GetString(DE, 1, 11, DefaultEncoding))) continue;
                Byte b = DE[0];
                String stat = (b == 0) ? "[   ]" : (b == 0x80) ? "[HID]" : (b == 0xe5) ? "[DEL]" : String.Format("[{0:x2}h]", b);
                String name = Buffer.GetString(DE, 1, 8, DefaultEncoding);
                String type = Buffer.GetString(DE, 9, 3, DefaultEncoding);
                Byte ext = DE[12];
                Byte nr = DE[15];
                output.WriteLine("{0} {1} {2} ({3})  {4:D0}", stat, name, type, (ext <= 31) ? ext.ToString("D2") : "  ", nr);
            }
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
            Program.Dump(null, ReadFile(fileSpec), output, 16, 128, Program.DumpOptions.ASCII);
        }

        public override String FullName(String fileSpec)
        {
            Byte[] DE = FindFile(fileSpec);
            if (DE == null) return null;
            String name = Buffer.GetString(DE, 1, 8, DefaultEncoding).TrimEnd(' ');
            String type = Buffer.GetString(DE, 9, 3, DefaultEncoding).TrimEnd(' ');
            if (type.Length == 0) return name;
            return String.Concat(name, ".", type);
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            Byte[] DE = FindFile(fileSpec);
            if (DE == null) return null;
            String fileName = Buffer.GetString(DE, 1, 11, DefaultEncoding);
            Int32 len = 0;
            for (Int32 ext = 0; ext < 32; ext++)
            {
                DE = FindFile(fileName, ext);
                if (DE != null) len += DE[15];
            }
            Byte[] buf = new Byte[len *= 128];
            Int32 bp = 0;
            for (Int32 ext = 0; ext < 32; ext++)
            {
                DE = FindFile(fileName, ext);
                for (Int32 i = 16; i < 32; i++)
                {
                    if ((DE != null) && (DE[i] != 0)) mBlocks[DE[i]].CopyTo(buf, bp);
                    bp += BLOCK_SIZE;
                }
            }
            return buf;
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

        private Byte[] FindFile(String fileSpec)
        {
            Regex RE = Regex(fileSpec);
            for (Int32 dp = 0; dp < mDir.Length; dp++)
            {
                Byte[] DE = mDir[dp];
                Byte b = DE[0];
                if ((b != 0) && (b != 128)) continue;
                if (DE[12] != 0) continue;
                String fileName = Buffer.GetString(DE, 1, 11, DefaultEncoding);
                if (!RE.IsMatch(fileName)) continue;
                return DE;
            }
            return null;
        }

        private Byte[] FindFile(String fileName, Int32 extentNum)
        {
            for (Int32 dp = 0; dp < mDir.Length; dp++)
            {
                Byte[] DE = mDir[dp];
                Byte b = DE[0];
                if ((b != 0) && (b != 128)) continue;
                if (DE[12] != extentNum) continue;
                if (fileName != Buffer.GetString(DE, 1, 11, DefaultEncoding)) continue;
                return DE;
            }
            return null;
        }
    }

    partial class CPM
    {
        // convert a CP/M wildcard pattern to a Regex
        private static Regex Regex(String pattern)
        {
            String p = pattern.ToUpperInvariant();
            String name = p;
            String type = "   ";
            Int32 i;
            if ((i = p.IndexOf('.')) != -1)
            {
                name = p.Substring(0, i);
                type = p.Substring(i + 1);
            }
            if ((i = name.IndexOf('*')) != -1)
            {
                name = String.Concat(name.Substring(0, i), new String('?', 8 - i));
            }
            if ((i = type.IndexOf('*')) != -1)
            {
                type = String.Concat(type.Substring(0, i), new String('?', 3 - i));
            }
            p = String.Concat("^", name.Replace("?", ".").PadRight(8), type.Replace("?", ".").PadRight(3), "$");
            Debug.WriteLine(Debug.Level.Diag, "CPM.Regex: <{0}> => <{1}>", pattern, p);
            return new Regex(p);
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
