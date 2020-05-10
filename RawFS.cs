// RawFS.cs
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


// RawFS - Raw File System
// The RawFS class allows an unrecognized volume to be examined at the block level.


using System;
using System.IO;
using System.Text;

namespace FSX
{
    class RawFS : FileSystem
    {
        private Volume mVol;

        public RawFS(Volume volume)
        {
            mVol = volume;
        }

        public override String Source
        {
            get { return mVol.Source; }
        }

        public override String Type
        {
            get { return "RawFS"; }
        }

        public override String Info
        {
            get { return mVol.Info; }
        }

        public override String Dir
        {
            get { return null; }
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
            output.WriteLine("Volume Type: {0}", mVol.GetType().Name);
            output.WriteLine("Block Size: {0:D0} bytes", mVol.BlockSize);
            output.WriteLine("LBA: {0:D0}-{1:D0}", 0, mVol.BlockCount - 1);
            output.WriteLine("CHS: C{0:D0}H{1:D0}S{2:D0}-C{3:D0}H{4:D0}S{5:D0}", mVol.MinCylinder, mVol.MinHead, mVol.MinSector(mVol.MinCylinder, mVol.MinHead), mVol.MaxCylinder, mVol.MaxHead, mVol.MaxSector(mVol.MaxCylinder, mVol.MaxHead));
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
        }

        public override void DumpFile(String fileSpec, TextWriter output)
        {
            Byte[] buf = ReadFile(fileSpec);
            if (buf != null)
            {
                Debug.WriteLine(1, "RawFS.DumpFile: {0:D0} bytes", buf.Length);
                Program.Dump(null, buf, output, 16, mVol.BlockSize);
            }
        }

        public override String FullName(String fileSpec)
        {
            return null;
        }

        // valid fileSpec formats:
        // x - logical block number 'x'
        // x-y - block range starting with 'x' and ending with 'y' (inclusive)
        public override Byte[] ReadFile(String fileSpec)
        {
            String s = fileSpec;
            if ((s == null) || (s.Length == 0)) return null;
            String t = null;
            Int32 p = s.IndexOf('-');
            if (p != -1)
            {
                t = s.Substring(p + 1);
                s = s.Substring(0, p);
            }
            if (!Int32.TryParse(s, out p)) return null;
            Int32 q = p;
            if ((t != null) && (!Int32.TryParse(t, out q))) return null;
            Int32 n = q - p + 1;
            if (n < 0) return null;
            Byte[] buf = new Byte[n * mVol.BlockSize];
            n = 0;
            for (Int32 i = p; i <= q; i++) n += mVol[i].CopyTo(buf, n);
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
    }
}
