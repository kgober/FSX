// DEC.cs
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


// DEC RX01 Floppy - IBM 3740 format (8" SSSD diskette, 77 tracks, 26 sectors)
//
// DEC operating systems typically do not use track 0, so an RX01 diskette
// has an effective capacity of 76 tracks (2002 sectors).
//
// 'Soft' Interleave imposes a 'logical' sector order on top of the physical
// sector order.  Presumably the reason for this is to gain the performance
// benefits of optimal interleave without reformatting the diskette (e.g. to
// maintain interoperability when using diskettes for foreign data transfer).
// Sector interleave is 2:1, with a 6 sector track-to-track skew.

// DEC RX02 Floppy - DEC RX01 format, but using MFM-encoded sector data (only)
//
// Note that the RX02 uses FM recording for sector headers, just like the RX01.
// This mode switch between the sector header and sector data is atypical and
// makes RX02 diskettes hard to access on other systems.

// DEC RK05 DECpack
//
// DEC operating systems typically reserve the last 3 cylinders for bad block
// handling, leaving an effective capacity of 200 cylinders, each containing
// 2 tracks of 12 512-byte sectors, or a total of 4800 blocks.
//
// Some operating systems (notably Unix) use all 203 cylinders (4872 blocks)
// and therefore require special error-free disk cartridges.


using System;
using System.Text;

namespace FSX
{
    static class DEC
    {
        public static FileSystem Try(Disk disk)
        {
            Program.Debug(1, "DEC.Try: {0}", disk.Source);

            // check basic disk parameters
            if ((disk is CHSDisk) && (disk.BlockSize != 512) && ((512 % disk.BlockSize) == 0) && (disk.MinCylinder == 0) && (disk.MinSector() == 1))
            {
                if ((disk.BlockSize == 128) && (disk.MaxSector(0, 0) == 26) && (disk.BlockCount == 1976))
                {
                    // 76 tracks, probably an RX01 image with track 0 skipped
                    Boolean b8 = IsASCIIText(disk[0, 0, 8], 0x58, 24); // look for volume label in track 0, sector 8 (no interleave)
                    Boolean b15 = IsASCIIText(disk[0, 0, 15], 0x58, 24); // look for volume label in track 0, sector 15 (2:1 'soft' interleave)
                    FileSystem fs = null;
                    if (b8 && !b15) fs = Try(new ClusteredDisk(disk, 4, 0));
                    else if (b15 && !b8) fs = Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 4, 0));
                    if (fs == null) fs = Try(new ClusteredDisk(disk, 4, 0));
                    return (fs != null) ? fs : Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 4, 0));
                }
                if ((disk.BlockSize == 128) && (disk.MaxSector(0, 0) == 26) && ((disk.BlockCount == 2002) || (disk.BlockCount == 2080)))
                {
                    // 77 or 80 tracks, probably a full RX01 image including track 0
                    Boolean b8 = IsASCIIText(disk[1, 0, 8], 0x58, 24); // look for volume label in track 1, sector 8 (no interleave)
                    Boolean b15 = IsASCIIText(disk[1, 0, 15], 0x58, 24); // look for volume label in track 1, sector 15 (2:1 'soft' interleave)
                    FileSystem fs = null;
                    if (b8 && !b15) fs = Try(new ClusteredDisk(disk, 4, 26));
                    else if (b15 && !b8) fs = Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 26), 4, 26));
                    if (fs == null) fs = Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 26), 4, 26));
                    return (fs != null) ? fs : Try(new ClusteredDisk(disk, 4, 26));
                }
                if ((disk.BlockSize == 256) && (disk.MaxSector(0, 0) == 26) && (disk.BlockCount == 1976))
                {
                    // 76 tracks, probably an RX02 image with track 0 skipped
                    Boolean b4 = IsASCIIText(disk[0, 0, 4], 0x58, 24); // look for volume label in track 0, sector 4 (no interleave)
                    Boolean b7 = IsASCIIText(disk[0, 0, 7], 0x58, 24); // look for volume label in track 0, sector 7 (2:1 'soft' interleave)
                    FileSystem fs = null;
                    if (b4 && !b7) fs = Try(new ClusteredDisk(disk, 2, 0));
                    else if (b7 && !b4) fs = Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 2, 0));
                    if (fs == null) fs = Try(new ClusteredDisk(disk, 2, 0));
                    return (fs != null) ? fs : Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 2, 0));
                }
                if ((disk.BlockSize == 256) && (disk.MaxSector(0, 0) == 26) && ((disk.BlockCount == 2002) || (disk.BlockCount == 2080)))
                {
                    // 77 or 80 tracks, probably a full RX02 image including track 0
                    Boolean b4 = IsASCIIText(disk[1, 0, 4], 0x58, 24); // look for volume label in track 1, sector 4 (no interleave)
                    Boolean b7 = IsASCIIText(disk[1, 0, 7], 0x58, 24); // look for volume label in track 1, sector 7 (2:1 'soft' interleave)
                    FileSystem fs = null;
                    if (b4 && !b7) fs = Try(new ClusteredDisk(disk, 2, 26));
                    else if (b7 && !b4) fs = Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 26), 2, 26));
                    if (fs == null) fs = Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 26), 2, 26));
                    return (fs != null) ? fs : Try(new ClusteredDisk(disk, 2, 26));
                }
                return Try(new ClusteredDisk(disk, 512 / disk.BlockSize, 0));
            }
            else if ((disk is CHSDisk) && (disk.BlockSize == 512))
            {
                if ((disk.MaxSector(0, 0) == 10) && (disk.BlockCount == 800))
                {
                    // probably an RX50 image
                    Boolean b2 = IsASCIIText(disk[0, 0, 2], 0x1d8, 24); // look for volume label in track 0, sector 2 (no interleave)
                    Boolean b3 = IsASCIIText(disk[0, 0, 3], 0x1d8, 24); // look for volume label in track 0, sector 3 (2:1 'soft' interleave)
                    FileSystem fs = null;
                    if (b3 && !b2) fs = Try(new InterleavedDisk(disk as CHSDisk, 2, 0, 2, 0));
                    if (fs != null) return fs;
                }
            }
            else if ((disk.BlockSize != 512) && ((512 % disk.BlockSize) == 0))
            {
                return Try(new ClusteredDisk(disk, 512 / disk.BlockSize, 0));
            }
            else if (disk.BlockSize != 512)
            {
                Program.Debug(1, "Volume block size = {0:D0} (must be 512)", disk.BlockSize);
                return null;
            }

            // check disk structure
            Int32 size;
            Type type;
            if (ODS1.Test(disk, 6, out size, out type))
            {
                if ((size != -1) && (size != disk.BlockCount)) return new ODS1(new PaddedDisk(disk, size - disk.BlockCount));
                return new ODS1(disk);
            }
            if (RT11.Test(disk, 6, out size, out type))
            {
                if ((size != -1) && (size != disk.BlockCount)) return new RT11(new PaddedDisk(disk, size - disk.BlockCount));
                return new RT11(disk);
            }
            return null;
        }

        static Boolean IsASCIIText(Block block, Int32 offset, Int32 count)
        {
            for (Int32 i = 0; i < count; i++)
            {
                Byte b = block[offset + i];
                if ((b < 32) || (b >= 127)) return false;
            }
            return true;
        }
    }

    // DEC Radix-50 encoding for 16-bit words (PDP-11, VAX)
    static class Radix50
    {
        static Char[] T = {
            ' ', 'A', 'B', 'C', 'D', 'E', 'F', 'G',
            'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O',
            'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W',
            'X', 'Y', 'Z', '$', '.', '%', '0', '1',
            '2', '3', '4', '5', '6', '7', '8', '9'
        };

        public static String Convert(UInt16 value)
        {
            Int32 v = value;
            if (v >= 64000U) throw new ArgumentOutOfRangeException("value");
            StringBuilder buf = new StringBuilder(3);
            buf.Append(T[v / 1600]);
            v = v % 1600;
            buf.Append(T[v / 40]);
            buf.Append(T[v % 40]);
            return buf.ToString();
        }
    }
}
