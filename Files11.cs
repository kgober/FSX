// Files11.cs
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


// Files-11 File System Structure
//
// http://bitsavers.trailing-edge.com/pdf/dec/pdp11/rsx11/Files-11_ODS-1_Spec_Jun75.pdf
//
// Home Block
//  0   H.IBSZ  Index File Bitmap Size (blocks, != 0)
//  2   H.IBLB  Index File Bitmap LBN (2 words, high word first, != 0)
//  6   F.FMAX  Maximum Number of Files (!= 0)
//  8   H.SBCL  Storage Bitmap Cluster Factor (== 1)
//  10  H.DVTY  Disk Device Type (== 0)
//  12  H.VLEV  Volume Structure Level (== 0x0101)
//  14  H.VNAM  Volume Name (padded with nulls)
//  26          (not used)
//  30  H.VOWN  Volume Owner UIC
//  32  H.VPRO  Volume Protection Code
//  34  H.VCHA  Volume Characteristics
//  36  H.FPRO  Default File Protection
//  38          (not used)
//  44  H.WISZ  Default Window Size
//  45  H.FIEX  Default File Extend
//  46  H.LRUC  Directory Pre-access Limit
//  47          (not used)
//  58  H.CHK1  First Checksum
//  60  H.VDAT  Volume Creation Date "DDMMMYYHHMMSS"
//  74          (not used)
//  472 H.INDN  Volume Name (padded with spaces)
//  484 H.INDO  Volume Owner (padded with spaces)
//  496 H.INDF  Format Type 'DECFILE11A' padded with spaces
//  508         (not used)
//  510 H.CHK2  Second Checksum


using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FSX
{
    partial class ODS1
    {
        // level 0 - check basic disk parameters
        // level 1 - check home block
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

            // ensure disk is at least large enough to contain home block
            if (disk.BlockCount < 2)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk too small to contain home block");
                return -1;
            }
            if (level == 0) return 0;

            // level 1 - check home block
            Block HB = disk[1];
            if (!HomeBlockChecksumOK(HB, 58))
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Home Block First Checksum invalid");
                return 0;
            }
            if (!HomeBlockChecksumOK(HB, 510))
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Home Block Second Checksum invalid");
                return 0;
            }
            if (HB.ToUInt16(0) == 0)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Home Block Index File Bitmap Size invalid (must not be 0)");
                return 0;
            }
            if ((HB.ToUInt16(2) == 0) && (HB.ToUInt16(4) == 0))
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Home Block Index File Bitmap LBN invalid (must not be 0)");
                return 0;
            }
            if (HB.ToUInt16(6) == 0)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Home Block Maximum Number of Files invalid (must not be 0)");
                return 0;
            }
            if (HB.ToUInt16(8) != 1)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Home Block Storage Bitmap Cluster Factor invalid (must be 1)");
                return 0;
            }
            if (HB.ToUInt16(10) != 0)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Home Block Disk Device Type invalid (must be 0)");
                return 0;
            }
            if (HB.ToUInt16(12) != 0x0101)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Home Block Volume Structure Level invalid (must be 0x0101)");
                return 0;
            }
            return 1;
        }

        private static Boolean HomeBlockChecksumOK(Block block, Int32 checksumOffset)
        {
            Int32 sum = 0;
            for (Int32 p = 0; p < checksumOffset; p += 2) sum += block.ToUInt16(p);
            Int32 n = block.ToUInt16(checksumOffset);
            if (Program.Verbose > 2) Console.Error.WriteLine("Home block checksum @{0:D0} {1}: {2:x4} {3}= {4:x4}", checksumOffset, ((sum != 0) && ((sum % 65536) == n)) ? "PASS" : "FAIL", sum % 65536, ((sum % 65536) == n) ? '=' : '!', n);
            return ((sum != 0) && ((sum % 65536) == n));
        }

        private static Boolean IsASCIIText(Block block, Int32 offset, Int32 count)
        {
            for (Int32 i = 0; i < count; i++)
            {
                Byte b = block[offset + i];
                if ((b < 32) || (b >= 127)) return false;
            }
            return true;
        }
    }

    partial class ODS1 : FileSystem
    {
        private Disk mDisk;

        public ODS1(Disk disk)
        {
            mDisk = disk;
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
            get { return "ODS1"; }
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
            throw new NotImplementedException();
        }

        public override void ListDir(String fileSpec, TextWriter output)
        {
            throw new NotImplementedException();
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            throw new NotImplementedException();
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
            throw new NotImplementedException();
        }

        public override void DumpFile(String fileSpec, TextWriter output)
        {
            throw new NotImplementedException();
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
}
