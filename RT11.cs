// RT11.cs
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


// RT-11 File System Structure
//
// V01-15 http://www.bitsavers.org/www.computer.museum.uq.edu.au/RT-11/DEC-11-ORPGA-A-D%20RT-11%20Software%20Support%20Manual.pdf
// V02B http://www.bitsavers.org/www.computer.museum.uq.edu.au/RT-11/DEC-11-ORPGA-B-D%20RT-11%20Software%20Support%20Manual.pdf
// V02C http://www.bitsavers.org/www.computer.museum.uq.edu.au/RT-11/DEC-11-ORPGA-B-D,%20DN1%20RT-11%20Software%20Support%20Manual.pdf
// V03B http://www.bitsavers.org/www.computer.museum.uq.edu.au/RT-11/AA-5280B-TC%20RT-11%20Advanced%20Programmer's%20Guide.pdf
// V4.0 http://www.bitsavers.org/pdf/dec/pdp11/rt11/v4.0_Mar80/3b/AA-H379A-TC_RT-11_V4.0_Software_Support_Manual_Mar81.pdf
// V5.0 http://www.bitsavers.org/pdf/dec/pdp11/rt11/v5.0_Mar83/AA-H379B-TC_5.0_SWsuppMar83.pdf
// V5.1 http://www.bitsavers.org/www.computer.museum.uq.edu.au/RT-11/AA-H379B-TC%20RT-11%20Software%20Support%20Manual.pdf
// V5.6 http://www.bitsavers.org/pdf/dec/pdp11/rt11/v5.6_Aug91/AA-PD6PA-TC_RT-11_Volume_and_File_Formats_Manual_Aug91.pdf
// V5.7 http://www.bitsavers.org/pdf/dec/pdp11/rt11/v5.7_Oct98/AA-5286M-TC_RT-11_V5.7_Release_Notes_Oct98.pdf
//
// In RT-11 v1 the content of the home block was not documented, it was simply
// "Reserved for RSX-11D compatibility".  In RT-11 v3 the home block was populated
// with cluster size, directory start and version (V3A) words, as well as the
// 12-byte system, volume, and owner IDs.  The home block structure was documented
// in RT-11 v4 (albeit with an incorrect checksum algorithm).  RT-11 v4 also
// introduced the Protected flag for files.  In RT-11 v5.0, the home block version
// word changed to V05, and the size of the month field in date words was reduced
// from 5 to 4 bits, leaving 2 bits.  RT-11 v5.5 introduced the E.READ flag for
// files, and used the high 2 bits in date words to extend the representable year.
// Home block checksums were populated using the Files-11 algorithm.

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
using System.Collections;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FSX
{
    partial class RT11
    {
        public static RT11 Try(Disk disk)
        {
            if (Program.Verbose > 1) Console.Error.WriteLine("RT11.Try: {0}", disk.Source);

            // check basic disk parameters
            if ((disk is CHSDisk) && (disk.BlockSize != 512) && ((512 % disk.BlockSize) == 0))
            {
                if ((disk.BlockSize == 128) && (disk.MaxSector(0, 0) == 26) && (disk.BlockCount == 1976))
                {
                    Boolean b8 = IsASCIIText(disk[0, 0, 8], 0x58, 24); // look for volume label in track 0, sector 8 (no interleave)
                    Boolean b15 = IsASCIIText(disk[0, 0, 15], 0x58, 24); // look for volume label in track 0, sector 15 (2:1 'soft' interleave)
                    RT11 fs = null;
                    if (b8 && !b15) fs = Try(new ClusteredDisk(disk, 4, 0));
                    else if (b15 && !b8) fs = Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 4, 0));
                    if (fs == null) fs = Try(new ClusteredDisk(disk, 4, 0));
                    return (fs != null) ? fs : Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 4, 0));
                }
                if ((disk.BlockSize == 128) && (disk.MaxSector(0, 0) == 26) && ((disk.BlockCount == 2002) || (disk.BlockCount == 2080)))
                {
                    Boolean b8 = IsASCIIText(disk[1, 0, 8], 0x58, 24); // look for volume label in track 1, sector 8 (no interleave)
                    Boolean b15 = IsASCIIText(disk[1, 0, 15], 0x58, 24); // look for volume label in track 1, sector 15 (2:1 'soft' interleave)
                    RT11 fs = null;
                    if (b8 && !b15) fs = Try(new ClusteredDisk(disk, 4, 26));
                    else if (b15 && !b8) fs = Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 26), 4, 26));
                    if (fs == null) fs = Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 26), 4, 26));
                    return (fs != null) ? fs : Try(new ClusteredDisk(disk, 4, 26));
                }
                if ((disk.BlockSize == 256) && (disk.MaxSector(0, 0) == 26) && (disk.BlockCount == 1976))
                {
                    Boolean b4 = IsASCIIText(disk[0, 0, 4], 0x58, 24); // look for volume label in track 0, sector 4 (no interleave)
                    Boolean b7 = IsASCIIText(disk[0, 0, 7], 0x58, 24); // look for volume label in track 0, sector 7 (2:1 'soft' interleave)
                    RT11 fs = null;
                    if (b4 && !b7) fs = Try(new ClusteredDisk(disk, 2, 0));
                    else if (b7 && !b4) fs = Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 2, 0));
                    if (fs == null) fs = Try(new ClusteredDisk(disk, 2, 0));
                    return (fs != null) ? fs : Try(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 2, 0));
                }
                if ((disk.BlockSize == 256) && (disk.MaxSector(0, 0) == 26) && ((disk.BlockCount == 2002) || (disk.BlockCount == 2080)))
                {
                    Boolean b4 = IsASCIIText(disk[1, 0, 4], 0x58, 24); // look for volume label in track 1, sector 4 (no interleave)
                    Boolean b7 = IsASCIIText(disk[1, 0, 7], 0x58, 24); // look for volume label in track 1, sector 7 (2:1 'soft' interleave)
                    RT11 fs = null;
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
                    Boolean b2 = IsASCIIText(disk[0, 0, 2], 0x1d8, 24); // look for volume label in track 0, sector 2 (no interleave)
                    Boolean b3 = IsASCIIText(disk[0, 0, 3], 0x1d8, 24); // look for volume label in track 0, sector 3 (2:1 'soft' interleave)
                    RT11 fs = null;
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
                if (Program.Verbose > 1) Console.Error.WriteLine("Volume block size = {0:D0} (must be 512)", disk.BlockSize);
                return null;
            }

            // check disk structure
            Int32 size = CheckVTOC(disk, 3);
            if (size < 3) return null;
            else if (size != disk.BlockCount) return new RT11(new PaddedDisk(disk, size - disk.BlockCount));
            else return new RT11(disk);
        }

        // level 0 - check basic disk parameters
        // level 1 - check directory length
        // level 2 - check directory consistency
        // level 3 - check directory entries (and return volume size)
        // level 4 - check block allocations (and return volume size)
        public static Int32 CheckVTOC(Disk disk, Int32 level)
        {
            if (disk == null) throw new ArgumentNullException("disk");
            if ((level < 0) || (level > 4)) throw new ArgumentOutOfRangeException("level");

            // level 0 - check basic disk parameters
            if (disk.BlockSize != 512)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk block size = {0:D0} (must be 512)", disk.BlockSize);
                return -1;
            }

            // ensure disk is at least large enough to contain first directory segment
            Int32 ds = HomeBlockChecksumOK(disk[1]) ? disk[1].ToUInt16(0x1d4) : defaultDirStart; // assume directory start at block 6 unless home block value valid
            if (ds + 1 >= disk.BlockCount)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Disk too small to contain directory segment {0:D0}", 1);
                return -1;
            }
            if (level == 0) return 0;

            // level 1 - check directory length
            ClusteredDisk dir = new ClusteredDisk(disk, 2, ds - 2, 32); // -2 because segments are numbered from 1
            Int32[] SS = new Int32[32]; // segments seen (to detect cycles)
            Int32 sc = 0; // segment count
            Int32 s = 1;
            while (s != 0)
            {
                if (SS[s] != 0)
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Invalid directory segment chain: segment {0:D0} repeated", s);
                    return 0;
                }
                SS[s] = ++sc;
                s = dir[s].ToUInt16(2); // next directory segment
                if (s > 31)
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Invalid directory segment chain: segment {0:D0} invalid", s);
                    return 0;
                }
                if (s >= dir.BlockCount)
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Disk too small to contain directory segment {0:D0}", s);
                    return 0;
                }
            }
            if (level == 1) return 1;

            // level 2 - check directory consistency
            Int32 ns = -1; // total directory segments
            Int32 eb = -1; // extra bytes per directory entry
            Int32 bp = -1; // data block pointer
            s = 1;
            while (s != 0)
            {
                Block seg = dir[s];
                Int32 n = seg.ToUInt16(0); // total directory segments
                if ((ns == -1) && (n >= sc) && (n < 32)) ns = n;
                else if ((s == 1) || ((s != 1) && (n != ns) && (n != 0))) // despite docs, this might be zero in segments other than the first
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Inconsistent directory segment count in segment {0:D0} (is {1:D0}, expected {2:D0}{3})", s, n, sc, (sc == 31) ? null : " <= n <= 31");
                    return 1;
                }
                n = seg.ToUInt16(4); // highest segment in use
                if ((s == 1) && (n != sc))
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Incorrect highest-segment-used pointer in segment {0:D0} (is {1:D0}, expected {2:D0})", s, n, sc);
                    return 1;
                }
                n = seg.ToUInt16(6); // extra bytes per directory entry
                if ((eb == -1) && ((n % 2) == 0)) eb = n;
                else if (n != eb)
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Inconsistent or invalid extra bytes value in segment {0:D0} (is {1:D0}, expected {2})", s, n, (eb == -1) ? "even number" : eb.ToString("D0"));
                    return 1;
                }
                n = seg.ToUInt16(8); // starting data block for this segment
                if ((n < ds + ns * 2) || ((n >= disk.BlockCount) && (disk.BlockCount > ds + ns * 2)))
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Invalid start-of-data pointer in segment {0:D0} (is {1:D0}, expected {2:D0} <= n < {3:D0})", s, n, ds + ns * 2, disk.BlockCount);
                    return 1;
                }
                if (n <= bp)
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Inconsistent start-of-data pointer in segment {0:D0} (is {1:D0}, expected n > {2:D0})", s, n, bp);
                    return 1;
                }
                bp = n;
                Int32 sp = 10;
                E esw;
                while (((esw = (E)seg.ToUInt16(sp)) & E.EOS) == 0) // entry status word
                {
                    if (sp + 14 + eb > 1022)
                    {
                        if (Program.Verbose > 1) Console.Error.WriteLine("Missing end-of-segment marker in segment {0:D0}", s);
                        return 1;
                    }
                    sp = sp + 14 + eb;
                }
                s = seg.ToUInt16(2); // next directory segment
            }
            if (level == 2) return 2;

            // level 3 - check directory entries
            s = 1;
            while (s != 0)
            {
                Block seg = dir[s];
                Int32 n = seg.ToUInt16(8); // starting data block for this segment
                if ((n != bp) && (s != 1))
                {
                    if (Program.Verbose > 1) Console.Error.WriteLine("Inconsistent start-of-data pointer in segment {0:D0} (is {1:D0}, expected {2:D0})", s, n, bp);
                    return 2;
                }
                bp = n;
                Int32 sp = 10;
                E esw;
                while (((esw = (E)seg.ToUInt16(sp)) & E.EOS) == 0) // entry status word
                {
                    UInt16 w = seg.ToUInt16(sp + 14 + eb);
                    Boolean lastfile = (((E)w & E.EOS) == E.EOS) && (seg.ToUInt16(2) == 0);
                    w = seg.ToUInt16(sp + 2); // Radix-50 file name (chars 1-3)
                    if ((w >= 64000) && !(lastfile && (esw & E.MPTY) == E.MPTY))
                    {
                        if (Program.Verbose > 1) Console.Error.WriteLine("Invalid file name in segment {0:D0}", s);
                        return 2;
                    }
                    String fn1 = Radix50.Convert(w);
                    w = seg.ToUInt16(sp + 4); // Radix-50 file name (chars 4-6)
                    if ((w >= 64000) && !(lastfile && (esw & E.MPTY) == E.MPTY))
                    {
                        if (Program.Verbose > 1) Console.Error.WriteLine("Invalid file name in segment {0:D0}", s);
                        return 2;
                    }
                    String fn2 = Radix50.Convert(w);
                    w = seg.ToUInt16(sp + 6); // Radix-50 file type
                    if ((w >= 64000) && !(lastfile && (esw & E.MPTY) == E.MPTY))
                    {
                        if (Program.Verbose > 1) Console.Error.WriteLine("Invalid file name in segment {0:D0}", s);
                        return 2;
                    }
                    String ext = Radix50.Convert(w);
                    String fn = String.Concat(fn1.Trim(), fn2.Trim(), ".", ext.Trim());
                    E e = esw & (E.TENT | E.MPTY | E.PERM);
                    if ((e != E.TENT) && (e != E.MPTY) && (e != E.PERM))
                    {
                        if (Program.Verbose > 1) Console.Error.WriteLine("Invalid file type in segment {0:D0}, file {1}: 0x{2:x4}", s, fn, esw);
                        return 2;
                    }
                    if (((esw & (E.PROT | E.READ)) != 0) && (e != E.PERM))
                    {
                        if (Program.Verbose > 1) Console.Error.WriteLine("Protected/ReadOnly flags not valid for non-permanent file in segment {0:D0}, file {1}: 0x{2:x4}", s, fn, esw);
                        return 2;
                    }
                    n = seg.ToUInt16(sp + 8); // file length (in blocks)
                    if (((bp += n) > disk.BlockCount) && !(lastfile && (esw & E.MPTY) == E.MPTY))
                    {
                        if (Program.Verbose > 1) Console.Error.WriteLine("File allocation exceeds disk size in segment {0:D0}, file {1}", s, fn);
                        return 2;
                    }
                    n = seg.ToUInt16(sp + 12); // creation date
                    if (n != 0)
                    {
                        Int32 m = (n >> 10) & 0x000f;
                        Int32 d = (n >> 5) & 0x001f;
                        Int32 y = (n >> 14) & 0x0003;
                        y = 1972 + 32 * y + (n & 0x001f);
                        if (((m > 12) || (d > 31)) && !(lastfile && (esw & E.MPTY) == E.MPTY))
                        {
                            if (Program.Verbose > 1) Console.Error.WriteLine("Invalid file creation date in segment {0:D0}, file {1} ({2:D4}-{3:D2}-{4:D2})", s, fn, y, m, d);
                            return 1;
                        }
                    }
                    sp = sp + 14 + eb;
                }
                s = seg.ToUInt16(2); // next directory segment
            }
            if (level == 3) return bp;

            // level 4 - check block allocations
            if (bp < disk.BlockCount) bp = disk.BlockCount;
            BitArray BU = new BitArray(bp); // block usage map
            for (Int32 i = 0; i < 6; i++) BU[i] = true; // mark boot block, home block and reserved blocks as used
            for (Int32 i = 0; i < 2 * ns; i++) BU[ds + i] = true; // mark directory segments as used
            s = 1;
            while (s != 0)
            {
                Block seg = dir[s];
                bp = seg.ToUInt16(8); // starting data block for this segment
                Int32 sp = 10;
                E esw;
                while (((esw = (E)seg.ToUInt16(sp)) & E.EOS) == 0) // entry status word
                {
                    UInt16 w = seg.ToUInt16(sp + 14 + eb);
                    Boolean lastfile = (((E)w & E.EOS) == E.EOS) && (seg.ToUInt16(2) == 0);
                    w = seg.ToUInt16(sp + 2); // Radix-50 file name (chars 1-3)
                    String fn1 = (w < 64000) ? Radix50.Convert(w) : String.Empty;
                    w = seg.ToUInt16(sp + 4); // Radix-50 file name (chars 4-6)
                    String fn2 = (w < 64000) ? Radix50.Convert(w) : String.Empty;
                    w = seg.ToUInt16(sp + 6); // Radix-50 file type
                    String ext = (w < 64000) ? Radix50.Convert(w) : String.Empty;
                    String fn = String.Concat(fn1.Trim(), fn2.Trim(), ".", ext.Trim());
                    Int32 n = seg.ToUInt16(sp + 8); // file length (in blocks)
                    for (Int32 i = 0; i < n; i++)
                    {
                        if (BU[bp + i])
                        {
                            if (Program.Verbose > 1)
                            {
                                if (lastfile && (esw & E.MPTY) == E.MPTY) Console.Error.WriteLine("Remaining unused space contains already-allocated block {1:D0}", bp + i);
                                else Console.Error.WriteLine("File allocation contains already-allocated block in file {0}, block {1:D0}=[{2:D0}]", fn, i + 1, bp + i);
                            }
                            return 3;
                        }
                        BU[bp + i] = true;
                    }
                    bp += n;
                    sp = sp + 14 + eb;
                }
                s = seg.ToUInt16(2); // next directory segment
            }
            return bp;
        }

        private static Boolean HomeBlockChecksumOK(Block block)
        {
            Int32 sum = 0;
            for (Int32 p = 0; p < 510; p += 2) sum += block.ToUInt16(p);
            Int32 n = block.ToUInt16(510);
            if (Program.Verbose > 2) Console.Error.WriteLine("Home block checksum {0}: {1:x4} {2}= {3:x4}", ((sum != 0) && ((sum % 65536) == n)) ? "PASS" : "FAIL", sum % 65536, ((sum % 65536) == n) ? '=' : '!', n);
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

        // convert an RT-11 wildcard pattern to a Regex
        private static Regex Regex(String pattern)
        {
            String p = pattern.ToUpperInvariant();
            String np = p;
            String ep = "*";
            Int32 i = p.IndexOf('.');
            if (i != -1)
            {
                np = p.Substring(0, i);
                if (np.Length == 0) np = "*";
                ep = p.Substring(i + 1);
            }
            np = np.Replace("%", "[^ ]").Replace("*", @".*");
            ep = ep.Replace("%", "[^ ]").Replace("*", @".*");
            p = String.Concat("^", np, @" *\.", ep, " *$");
            if (Program.Verbose > 2) Console.Error.WriteLine("Regex: {0} => {1}", pattern, p);
            return new Regex(p);
        }
    }

    partial class RT11 : FileSystem
    {
        private const UInt16 defaultDirStart = 6;

        [Flags]
        private enum E : ushort
        {
            PRE = 0x0010,   // Prefix block indicator
            TENT = 0x0100,  // Tentative file
            MPTY = 0x0200,  // Empty area
            PERM = 0x0400,  // Permanent file
            EOS = 0x0800,   // End-of-segment marker
            READ = 0x4000,  // Protected by monitor from write operations
            PROT = 0x8000,  // Protected permanent file
        }

        class FileEntry
        {
            public String FileName;
            public Int32 StartBlock;
            public Int32 BlockCount;
            public DateTime CreationDate;

            public FileEntry(String fileName, Int32 startBlock, Int32 blockCount, DateTime creationDate)
            {
                FileName = fileName;
                StartBlock = startBlock;
                BlockCount = blockCount;
                CreationDate = creationDate;
            }
        }

        private static readonly String[] MONTHS = { null, "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        private Disk mDisk;
        private Int32 mDirStart;
        private ClusteredDisk mDir;

        public RT11(Disk disk)
        {
            if (disk.BlockSize != 512) throw new ArgumentException("RT11 volume block size must be 512.");
            mDisk = disk;
            mDirStart = HomeBlockChecksumOK(disk[1]) ? disk[1].ToUInt16(0x1d4) : defaultDirStart;
            mDir = new ClusteredDisk(disk, 2, mDirStart - 2, 32);
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
            get { return "RT11"; }
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
            output.WriteLine(DateTime.Today.ToString(" dd-MMM-yyyy"));
            Block B = mDisk[1];
            UInt16 w = B.ToUInt16(0x1d6);
            if ((w < 64000) & IsASCIIText(B, 0x01d8, 36))
            {
                Byte[] buf = new Byte[36];
                B.CopyTo(buf, 0, 0x1d8, 36);
                output.WriteLine(" System ID: {0} {1}", Encoding.ASCII.GetString(buf, 24, 12), Radix50.Convert(w));
                output.WriteLine(" Volume ID: {0}", Encoding.ASCII.GetString(buf, 0, 12));
                output.WriteLine(" Owner    : {0}", Encoding.ASCII.GetString(buf, 12, 12));
            }
            Boolean f = false;
            Int32 bct = 0;
            Int32 fct = 0;
            Int32 zct = 0;
            Int32 s = 1; // current directory segment number
            while (s != 0)
            {
                Block seg = mDir[s];
                Int32 eb = seg.ToUInt16(6);
                Int32 bp = seg.ToUInt16(8);
                Int32 sp = 10;
                E esw;
                while (((esw = (E)seg.ToUInt16(sp)) & E.EOS) == 0)
                {
                    w = seg.ToUInt16(sp + 2);
                    String fn1 = (w < 64000) ? Radix50.Convert(w) : "---";
                    w = seg.ToUInt16(sp + 4);
                    String fn2 = (w < 64000) ? Radix50.Convert(w) : "---";
                    w = seg.ToUInt16(sp + 6);
                    String ext = (w < 64000) ? Radix50.Convert(w) : "---";
                    Int32 len = seg.ToUInt16(sp + 8);
                    String cdt = "           ";
                    w = seg.ToUInt16(sp + 12);
                    if ((w != 0) && ((esw & E.PERM) != 0))
                    {
                        Int32 y = ((w & 0xc000) >> 9) + (w & 0x001f) + 1972;
                        Int32 m = (w & 0x3c00) >> 10;
                        Int32 d = (w & 0x03e0) >> 5;
                        cdt = ((m >= 1) && (m <= 12) && (d >= 1) && (d <= 31)) ? String.Format("{0:D2}-{1}-{2:D4}", d, MONTHS[m], y) : "   -BAD-   ";
                    }
                    String fn = String.Concat(fn1, fn2, ".", ext);
                    if ((esw & E.PERM) != 0)
                    {
                        if (RE.IsMatch(fn))
                        {
                            fct++;
                            bct += len;
                        }
                        else
                        {
                            fn = null;
                        }
                    }
                    else
                    {
                        fn = "< UNUSED >";
                        zct += len;
                    }
                    if (fn != null)
                    {
                        if (f = !f) output.Write("{0} {1,5:D0}{2} {3}", fn, len, ((esw & (E.PERM | E.PROT)) == (E.PERM | E.PROT)) ? 'P' : ' ', cdt);
                        else output.WriteLine("    {0} {1,5:D0}{2} {3}", fn, len, ((esw & (E.PERM | E.PROT)) == (E.PERM | E.PROT)) ? 'P' : ' ', cdt);
                    }
                    bp += len;
                    sp += 14 + eb;
                }
                s = seg.ToUInt16(2);
            }
            if (f) output.WriteLine();
            output.WriteLine(" {0:D0} Files, {1:D0} Blocks", fct, bct);
            output.WriteLine(" {0:D0} Free blocks", zct);
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) fileSpec = "*.*";
            Regex RE = Regex(fileSpec);
            Int32 bct = 0;
            Int32 fct = 0;
            Int32 zct = 0;
            Int32 s = 1; // current directory segment number
            while (s != 0)
            {
                Block B = mDir[s];
                Int32 eb = B.ToUInt16(6);
                Int32 bp = B.ToUInt16(8);
                Int32 sp = 10;
                UInt16 w;
                E esw;
                while (((esw = (E)B.ToUInt16(sp)) & E.EOS) == 0)
                {
                    Char s1 = ((esw & E.PROT) == 0) ? '-' : 'P';
                    Char s2 = ((esw & E.READ) == 0) ? '-' : 'R';
                    Char s3 = ((esw & E.PERM) == 0) ? '-' : 'F';
                    Char s4 = ((esw & E.TENT) == 0) ? '-' : 'T';
                    Char s5 = ((esw & E.MPTY) == 0) ? '-' : 'E';
                    w = B.ToUInt16(sp + 2);
                    String fn1 = (w < 64000) ? Radix50.Convert(w) : "---";
                    w = B.ToUInt16(sp + 4);
                    String fn2 = (w < 64000) ? Radix50.Convert(w) : "---";
                    w = B.ToUInt16(sp + 6);
                    String ext = (w < 64000) ? Radix50.Convert(w) : "---";
                    Int32 len = B.ToUInt16(sp + 8);
                    Int32 chj = B.ToUInt16(sp + 10);
                    String cdt = "           ";
                    w = B.ToUInt16(sp + 12);
                    if (w != 0)
                    {
                        Int32 y = ((w & 0xc000) >> 9) + (w & 0x001f) + 1972;
                        Int32 m = (w & 0x3c00) >> 10;
                        Int32 d = (w & 0x03e0) >> 5;
                        cdt = ((m >= 1) && (m <= 12) && (d >= 1) && (d <= 31)) ? String.Format("{0:D2}-{1}-{2:D4}", d, MONTHS[m], y) : "   -BAD-   ";
                    }
                    String fn = String.Concat(fn1, fn2, ".", ext);
                    if (!RE.IsMatch(fn)) fn = null;
                    if ((esw & E.PERM) != 0)
                    {
                        if (fn != null)
                        {
                            fct++;
                            bct += len;
                        }
                    }
                    else
                    {
                        zct += len;
                    }
                    if (fn != null) Console.Out.WriteLine("{0} {1,5:D0} @ {2,-5:D0} {3}{4}{5}{6}{7} {8}", fn, len, bp, s1, s2, s3, s4, s5, cdt);
                    bp += len;
                    sp += 14 + eb;
                }
                s = B.ToUInt16(2);
            }
            Int32 ns = mDir[1].ToUInt16(0); // number of directory segments
            output.WriteLine(" {0:D0} Files, {1:D0} Blocks, {2:D0} Free blocks", fct, bct, zct);
            output.WriteLine(" {0:D0} Total blocks ({1:D0} Reserved, {2:D0} Directory)", mDirStart + ns * 2 + bct + zct, mDirStart, ns * 2);
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
            Program.Dump(null, ReadFile(fileSpec), output, Program.DumpOptions.Radix50);
        }

        public override String FullName(String fileSpec)
        {
            FileEntry f = FindFile(fileSpec);
            if (f == null) return null;
            return f.FileName;
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            FileEntry f = FindFile(fileSpec);
            if (f == null) return new Byte[0];
            Byte[] buf = new Byte[f.BlockCount * 512];
            Int32 p = 0;
            for (Int32 i = 0; i < f.BlockCount; i++)
            {
                mDisk[f.StartBlock + i].CopyTo(buf, p);
                p += 512;
            }
            return buf;
        }

        public override Boolean SaveFS(String fileName, String format)
        {
            // RX01 and RX02 images should be written as physical images (including track 0)
            // all other images (including RX50) should be written as logical images
            FileStream f = new FileStream(fileName, FileMode.Create);
            Disk d = mDisk;
            Int32 n = CheckVTOC(d, 3);
            if ((n == 494) || (n == 988)) // RX01 and RX02 sizes
            {
                Boolean iFlag = (d is InterleavedDisk);
                while (d.BaseDisk != null)
                {
                    d = d.BaseDisk;
                    if (d is InterleavedDisk) iFlag = true;
                }
                if (iFlag)
                {
                    // the base image is already in physical format
                    if (d.MaxCylinder == 75)
                    {
                        // base image lacks track 0
                        Byte[] buf = new Byte[d.BlockSize];
                        for (Int32 s = 0; s < 26; s++) f.Write(buf, 0, d.BlockSize);
                        for (Int32 t = 0; t < 76; t++)
                        {
                            for (Int32 s = 1; s <= 26; s++)
                            {
                                d[t, 0, s].CopyTo(buf, 0);
                                f.Write(buf, 0, d.BlockSize);
                            }
                        }
                    }
                    else
                    {
                        // base image includes track 0
                        Byte[] buf = new Byte[d.BlockSize];
                        for (Int32 t = 0; t < 77; t++)
                        {
                            for (Int32 s = 1; s <= 26; s++)
                            {
                                d[t, 0, s].CopyTo(buf, 0);
                                f.Write(buf, 0, d.BlockSize);
                            }
                        }
                    }
                }
                else
                {
                    // physical image must be created
                    Int32 SPB = 512 / d.BlockSize;
                    Int32[,] map = new Int32[76, 26];
                    for (Int32 lsn = 0; lsn < n * SPB; lsn++)
                    {
                        Int32 t = lsn / 26;
                        Int32 s = lsn % 26;
                        s *= 2; // 2:1 interleave
                        if (s >= 26) s++; // 2 interleave cycles per track
                        s += t * 6; // skew
                        s %= 26;
                        map[t, s] = lsn;
                    }
                    Byte[] buf = new Byte[d.BlockSize];
                    for (Int32 s = 0; s < 26; s++) f.Write(buf, 0, d.BlockSize);
                    for (Int32 t = 0; t < 76; t++)
                    {
                        for (Int32 s = 0; s < 26; s++)
                        {
                            Int32 lsn = map[t, s];
                            mDisk[lsn / SPB].CopyTo(buf, 0, (lsn % SPB) * d.BlockSize, d.BlockSize);
                            f.Write(buf, 0, d.BlockSize);
                        }
                    }
                }
            }
            else
            {
                Byte[] buf = new Byte[512];
                for (Int32 i = 0; i < n; i++)
                {
                    d[i].CopyTo(buf, 0);
                    f.Write(buf, 0, 512);
                }
            }
            f.Close();
            return true;
        }

        private FileEntry FindFile(String fileSpec)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) return null;
            Int32 p = fileSpec.IndexOf('@');
            if (p != -1)
            {
                Int32 start, count;
                if (!Int32.TryParse(fileSpec.Substring(p + 1).Trim(), out start) || !Int32.TryParse(fileSpec.Substring(0, p).Trim(), out count)) return null;
                return new FileEntry(fileSpec, start, count, DateTime.Now);
            }
            p = fileSpec.IndexOf('-');
            if (p != -1)
            {
                Int32 first, last;
                if (!Int32.TryParse(fileSpec.Substring(p + 1).Trim(), out last) || !Int32.TryParse(fileSpec.Substring(0, p).Trim(), out first)) return null;
                return new FileEntry(fileSpec, first, last - first + 1, DateTime.Now);
            }

            Regex RE = Regex(fileSpec);
            Int32 s = 1;
            while (s != 0)
            {
                Block seg = mDir[s];
                Int32 eb = seg.ToUInt16(6);
                Int32 bp = seg.ToUInt16(8);
                Int32 sp = 10;
                E esw;
                UInt16 w;
                while (((esw = (E)seg.ToUInt16(sp)) & E.EOS) == 0)
                {
                    Int32 len = seg.ToUInt16(sp + 8);
                    if ((esw & E.PERM) != 0)
                    {
                        w = seg.ToUInt16(sp + 2);
                        String fn1 = (w < 64000) ? Radix50.Convert(w) : "---";
                        w = seg.ToUInt16(sp + 4);
                        String fn2 = (w < 64000) ? Radix50.Convert(w) : "---";
                        w = seg.ToUInt16(sp + 6);
                        String ext = (w < 64000) ? Radix50.Convert(w) : "---";
                        String fn = String.Concat(fn1, fn2, ".", ext);
                        if (RE.IsMatch(fn))
                        {
                            w = seg.ToUInt16(sp + 12);
                            DateTime dt = DateTime.Now;
                            if (w != 0)
                            {
                                Int32 y = ((w & 0xc000) >> 9) + (w & 0x001f) + 1972;
                                Int32 m = (w & 0x3c00) >> 10;
                                Int32 d = (w & 0x03e0) >> 5;
                                if ((m >= 1) && (m <= 12) && (d >= 1) && (d <= 31)) dt = new DateTime(y, 1, 1).AddMonths(m - 1).AddDays(d - 1);
                            }
                            return new FileEntry(String.Concat(String.Concat(fn1, fn2).TrimEnd(' '), ".", ext.TrimEnd(' ')), bp, len, dt);
                        }
                    }
                    bp += len;
                    sp += 14 + eb;
                }
                s = seg.ToUInt16(2);
            }
            return null;
        }
    }
}
