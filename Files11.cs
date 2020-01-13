// Files11.cs
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


// Files-11 File System Structure
//
// http://www.bitsavers.org/pdf/dec/pdp11/rsx11/Files-11_ODS-1_Spec_Jun75.pdf
// http://www.bitsavers.org/pdf/dec/pdp11/rsx11/Files-11_ODS-1_Spec_Jun77.pdf
// http://www.bitsavers.org/pdf/dec/pdp11/rsx11/Files-11_ODS-1_Spec_Apr81.pdf
// http://www.bitsavers.org/pdf/dec/pdp11/rsx11/Files-11_ODS-1_Spec_Sep86.txt
//
// Home Block
//  0   H.IBSZ  Index File Bitmap Size (blocks, != 0)
//  2   H.IBLB  Index File Bitmap LBN (2 words, high word first, != 0)
//  6   H.FMAX  Maximum Number of Files (!= 0, index file may not start this big)
//  8   H.SBCL  Storage Bitmap Cluster Factor (== 1)
//  10  H.DVTY  Disk Device Type (== 0)
//  12  H.VLEV  Volume Structure Level (== 0x0101)
//  14  H.VNAM  Volume Name (padded with nulls)
//  26          (not used)
//  30  H.VOWN  Volume Owner UIC
//  32  H.VPRO  Volume Protection Code
//  34  H.VCHA  Volume Characteristics
//  36  H.DFPR  Default File Protection
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
//
// File Header Area
//  0   H.IDOF  Ident Area Offset (in words)
//  1   H.MPOF  Map Area Offset (in words)
//  2   H.FNUM  File Number
//  4   H.FSEQ  File Sequence Number
//  6   H.FLEV  File Structure Level (must be 0x0101)
//  8   H.FOWN  File Owner UIC
//  10  H.FPRO  File Protection Code
//  12  H.FCHA  File Characteristics
//  14  H.UFAT  User Attribute Area (32 bytes)
//      +0  F.RTYP  Record Type (R.FIX=1 R.VAR=2)
//      +1  F.RATT  Record Attributes (FD.CR=2)
//      +2  F.RSIZ  Record Size
//      +4  F.HIBK  Highest VBN Allocated
//      +8  F.EFBK  End of File Block
//      +12 F.FFBY  First Free Byte (word)
//
// File Ident Area
//  0   I.FNAM  File Name (9 characters as 3 Radix-50 words)
//  6   I.FTYP  File Type (3 characters as 1 Radix-50 word)
//  8   I.FVER  Version Number (signed)
//  10  I.RVNO  Revision Number
//  12  I.RVDT  Revision Date 'ddMMMyy'
//  19  I.RVTI  Revision Time 'HHmmss'
//  25  I.CRDT  Creation Date 'ddMMMyy'
//  32  I.CRTI  Creation Time 'HHmmss'
//  38  I.EXDT  Expiration Date 'ddMMMyy'
//  45          (1 unused byte to reach a word boundary)
//
// File Map Area
//  0   M.ESQN  Extension Segment Number (numbered from 0)
//  1   M.ERVN  Extension Relative Volume No.
//  2   M.EFNU  Extension File Number (next header file number, or 0)
//  4   M.EFSQ  Extension File Sequence Number (next header sequence number, or 0)
//  6   M.CTSZ  Block Count Field Size (bytes)
//  7   M.LBSZ  LBN Field Size (bytes)
//  8   M.USE   Map Words In Use
//  9   M.MAX   Map Words Available (1 byte)
//  10  M.RTRV  Retrieval Pointers (M.MAX words)


// Future Improvements / To Do
// check file header Ident Area fields
// move check of file 2,2,0 retrieval pointers to level 6
// move check of file header 1/2 allocation to level 5
// check file headers 3+ (currently not implemented by level 3) 
// implement Test level 4 (check directory structure)
// implement Test level 5 (check file header allocation)
// implement Test level 6 (check data block allocation)
// support fixed/variable records in ReadFile (e.g. to allow saving a text file)
// support additional file types/flags (e.g. FD.BLK)
// display size, protection, owner, dates in directory listings
// allow files to be written/deleted in images


using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FSX
{
    partial class ODS1 : FileSystem
    {
        private Volume mVol;
        private String mDir;
        private UInt16 mDirNum;
        private UInt16 mDirSeq;

        public ODS1(Volume volume)
        {
            mVol = volume;
            mDir = "[0,0]";
            mDirNum = 4;
            mDirSeq = 4;
        }

        public override Volume Volume
        {
            get { return mVol; }
        }

        public override String Source
        {
            get { return mVol.Source; }
        }

        public override String Type
        {
            get { return "ODS1"; }
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
            if ((dirSpec == null) || (dirSpec.Length == 0)) return;

            Int32 p = dirSpec.IndexOf('[');
            if (p != -1)
            {
                dirSpec = dirSpec.Substring(p + 1);
                p = dirSpec.IndexOf(']');
                if (p == -1) return;
                dirSpec = dirSpec.Substring(0, p);
            }
            p = dirSpec.IndexOf(',');
            if (p != -1)
            {
                Byte m, n;
                if (!Byte.TryParse(dirSpec.Substring(0, p), out m) || !Byte.TryParse(dirSpec.Substring(p + 1), out n)) return;
                dirSpec = String.Format("{0:D3}{1:D3}", m, n);
            }

            String dirName;
            UInt16 fnum, fseq;
            if (!FindFile(4, 4, String.Concat(dirSpec, ".DIR;*"), out fnum, out fseq, out dirName)) return;

            p = dirName.IndexOf(".DIR");
            if (p == 6)
            {
                if (IsDigit(dirName[0], 0, 3) && IsDigit(dirName[1], 0, 7) && IsDigit(dirName[2], 0, 7) && IsDigit(dirName[3], 0, 3) && IsDigit(dirName[4], 0, 7) && IsDigit(dirName[5], 0, 7))
                {
                    dirName = String.Format("{0:D0},{1:D0}", Byte.Parse(dirName.Substring(0, 3)), Byte.Parse(dirName.Substring(3, 3)));
                }
            }
            else if (p != -1)
            {
                dirName = dirName.Substring(0, p);
            }
            mDir = String.Concat("[", dirName, "]");
            mDirNum = fnum;
            mDirSeq = fseq;
        }

        public override void ListDir(String fileSpec, TextWriter output)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) fileSpec = "*.*;*";

            String dirSpec = mDir;
            UInt16 dirNum = mDirNum;
            UInt16 dirSeq = mDirSeq;
            Int32 p = fileSpec.IndexOf(']');
            if (p != -1)
            {
                dirSpec = fileSpec.Substring(0, p);
                fileSpec = fileSpec.Substring(p + 1);
                if (fileSpec.Length == 0) fileSpec = "*.*;*";
                p = dirSpec.IndexOf('[');
                if (p == -1) return;
                dirSpec = dirSpec.Substring(p + 1);
                p = dirSpec.IndexOf(',');
                if (p != -1)
                {
                    Byte prj, prg;
                    if (!Byte.TryParse(dirSpec.Substring(0, p), out prj) || !Byte.TryParse(dirSpec.Substring(p + 1), out prg)) return;
                    dirSpec = String.Format("{0:D3}{1:D3}", prj, prg);
                }
                if (!FindFile(4, 4, String.Concat(dirSpec, ".DIR;*"), out dirNum, out dirSeq, out dirSpec)) return;
            }

            Regex RE = Regex(fileSpec);
            Block H = GetFileHeader(dirNum, dirSeq);
            if (H == null) return;
            Byte[] data = ReadFile(dirNum, dirSeq);
            Byte RTYP = H[14];
            Byte RATT = H[15];
            Int32 n = H.GetUInt16L(16);
            Int32 len = (H.GetUInt16L(22) << 16) + H.GetUInt16L(24); // VBN where EOF is located
            len = (len - 1) * 512 + H.GetUInt16L(26);
            if (RTYP == 0) len = data.Length;
            Int32 bp = 0;
            while (bp < len)
            {
                UInt16 fnum = Buffer.GetUInt16L(data, bp);
                if (fnum != 0)
                {
                    String fn1 = Radix50.Convert(Buffer.GetUInt16L(data, bp + 6));
                    String fn2 = Radix50.Convert(Buffer.GetUInt16L(data, bp + 8));
                    String fn3 = Radix50.Convert(Buffer.GetUInt16L(data, bp + 10));
                    String ext = Radix50.Convert(Buffer.GetUInt16L(data, bp + 12));
                    UInt16 ver = Buffer.GetUInt16L(data, bp + 14);
                    String fn = String.Format("{0}{1}{2}.{3};{4:D0}", fn1, fn2, fn3, ext, ver);
                    if (RE.IsMatch(fn))
                    {
                        UInt16 fseq = Buffer.GetUInt16L(data, bp + 2);
                        UInt16 fvol = Buffer.GetUInt16L(data, bp + 4);
                        output.WriteLine("{0} ({1:D0},{2:D0},{3:D0})", fn, fnum, fseq, fvol);
                    }
                }
                bp += n;
            }
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) fileSpec = "*.*;*";

            String dirSpec = mDir;
            UInt16 dirNum = mDirNum;
            UInt16 dirSeq = mDirSeq;
            Int32 p = fileSpec.IndexOf(']');
            if (p != -1)
            {
                dirSpec = fileSpec.Substring(0, p);
                fileSpec = fileSpec.Substring(p + 1);
                if (fileSpec.Length == 0) fileSpec = "*.*;*";
                p = dirSpec.IndexOf('[');
                if (p == -1) return;
                dirSpec = dirSpec.Substring(p + 1);
                p = dirSpec.IndexOf(',');
                if (p != -1)
                {
                    Byte m, n;
                    if (!Byte.TryParse(dirSpec.Substring(0, p), out m) || !Byte.TryParse(dirSpec.Substring(p + 1), out n)) return;
                    dirSpec = String.Format("{0:D3}{1:D3}", m, n);
                }
                if (!FindFile(4, 4, String.Concat(dirSpec, ".DIR;*"), out dirNum, out dirSeq, out dirSpec)) return;
            }

            Byte[] data = ReadFile(dirNum, dirSeq);
            Program.Dump(null, data, output, 16, 512, Program.DumpOptions.ASCII|Program.DumpOptions.Radix50);
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
            String dirSpec = mDir;
            UInt16 dirNum = mDirNum;
            UInt16 dirSeq = mDirSeq;
            Int32 p = fileSpec.IndexOf(']');
            if (p != -1)
            {
                dirSpec = fileSpec.Substring(0, p);
                fileSpec = fileSpec.Substring(p + 1);
                p = dirSpec.IndexOf('[');
                if (p == -1) return;
                dirSpec = dirSpec.Substring(p + 1);
                p = dirSpec.IndexOf(',');
                if (p != -1)
                {
                    Byte prj, prg;
                    if (!Byte.TryParse(dirSpec.Substring(0, p), out prj) || !Byte.TryParse(dirSpec.Substring(p + 1), out prg)) return;
                    dirSpec = String.Format("{0:D3}{1:D3}", prj, prg);
                }
                if (!FindFile(4, 4, String.Concat(dirSpec, ".DIR;*"), out dirNum, out dirSeq, out dirSpec)) return;
            }

            String fileName;
            UInt16 fileNum, fileSeq;
            if (!FindFile(dirNum, dirSeq, fileSpec, out fileNum, out fileSeq, out fileName)) return;
            Block H = GetFileHeader(fileNum, fileSeq);
            if (H == null) return;
            Byte RTYP = H[14];
            Byte RATT = H[15];
            Int32 n = H.GetUInt16L(16);
            Int32 len = (H.GetUInt16L(22) << 16) + H.GetUInt16L(24); // VBN where EOF is located
            len = (len - 1) * 512 + H.GetUInt16L(26);
            Byte[] buf = ReadFile(fileNum, fileSeq);
            if (RTYP == 0)
            {
                p = buf.Length;
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
            else if (RTYP == 1)
            {
                // TODO: handle FD.BLK (records do not cross block boundaries)
                p = 0;
                while (p < len)
                {
                    String s = encoding.GetString(buf, p, n);
                    if ((RATT & 1) != 0) // FD.FTN
                    {
                        Char c = (s.Length != 0) ? s[0] : ' ';
                        if (s.Length != 0) s = s.Substring(1);
                        switch (c)
                        {
                            case '+': break;
                            case '1': output.Write('\f'); break;
                            case '0': output.Write('\n'); goto default;
                            default: output.Write('\n'); break;
                        }
                        output.Write(s);
                        output.Write('\r');
                    }
                    else if ((RATT & 2) != 0) // FD.CR
                    {
                        output.Write('\n');
                        output.Write(s);
                        output.Write('\r');
                    }
                    else
                    {
                        output.WriteLine(s);
                    }
                    p += n;
                }
            }
            else if (RTYP == 2)
            {
                p = 0;
                while (p < len)
                {
                    while (((p += 2) <= len) && ((n = Buffer.GetInt16L(buf, p - 2)) != -1))
                    {
                        String s = (n == 0) ? String.Empty : encoding.GetString(buf, p, n);
                        if ((RATT & 1) != 0) // FD.FTN
                        {
                            Char c = (s.Length != 0) ? s[0] : ' ';
                            if (s.Length != 0) s = s.Substring(1);
                            switch (c)
                            {
                                case '+': break;
                                case '1': output.Write('\f'); break;
                                case '0': output.Write('\n'); goto default;
                                default: output.Write('\n'); break;
                            }
                            output.Write(s);
                            output.Write('\r');
                        }
                        else if ((RATT & 2) != 0) // FD.CR
                        {
                            output.Write('\n');
                            output.Write(s);
                            output.Write('\r');
                        }
                        else
                        {
                            output.WriteLine(s);
                        }
                        if (((p += n) % 2) != 0) p++;
                    }
                    n = p % 512;
                    if (n != 0) p += 512 - n;
                }
            }
        }

        public override void DumpFile(String fileSpec, TextWriter output)
        {
            Program.Dump(null, ReadFile(fileSpec), output, 16, 512, Program.DumpOptions.ASCII|Program.DumpOptions.Radix50);
        }

        public override String FullName(String fileSpec)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) return null;

            String dirName = mDir;
            UInt16 dirNum = mDirNum;
            UInt16 dirSeq = mDirSeq;
            Int32 p = fileSpec.IndexOf(']');
            if (p != -1)
            {
                dirName = fileSpec.Substring(0, p);
                fileSpec = fileSpec.Substring(p + 1);
                if (fileSpec.Length == 0) return null;
                p = dirName.IndexOf('[');
                if (p == -1) return null;
                dirName = dirName.Substring(p + 1);
                p = dirName.IndexOf(',');
                if (p != -1)
                {
                    Byte m, n;
                    if (!Byte.TryParse(dirName.Substring(0, p), out m) || !Byte.TryParse(dirName.Substring(p + 1), out n)) return null;
                    dirName = String.Format("{0:D3}{1:D3}", m, n);
                }
                if (!FindFile(4, 4, String.Concat(dirName, ".DIR;*"), out dirNum, out dirSeq, out dirName)) return null;
            }

            p = dirName.IndexOf(".DIR");
            if (p == 6)
            {
                if (IsDigit(dirName[0], 0, 3) && IsDigit(dirName[1], 0, 7) && IsDigit(dirName[2], 0, 7) && IsDigit(dirName[3], 0, 3) && IsDigit(dirName[4], 0, 7) && IsDigit(dirName[5], 0, 7))
                {
                    dirName = String.Format("{0:D0},{1:D0}", Byte.Parse(dirName.Substring(0, 3)), Byte.Parse(dirName.Substring(3, 3)));
                }
            }
            else if (p != -1)
            {
                dirName = dirName.Substring(0, p);
            }
            dirName = String.Concat("[", dirName, "]");

            String fileName;
            UInt16 fileNum, fileSeq;
            if (!FindFile(dirNum, dirSeq, fileSpec, out fileNum, out fileSeq, out fileName)) return null;

            return String.Concat(dirName, fileName);
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) return new Byte[0];

            String dirSpec = mDir;
            UInt16 dirNum = mDirNum;
            UInt16 dirSeq = mDirSeq;
            Int32 p = fileSpec.IndexOf(']');
            if (p != -1)
            {
                dirSpec = fileSpec.Substring(0, p);
                fileSpec = fileSpec.Substring(p + 1);
                if (fileSpec.Length == 0) return new Byte[0];
                p = dirSpec.IndexOf('[');
                if (p == -1) return new Byte[0];
                dirSpec = dirSpec.Substring(p + 1);
                p = dirSpec.IndexOf(',');
                if (p != -1)
                {
                    Byte m, n;
                    if (!Byte.TryParse(dirSpec.Substring(0, p), out m) || !Byte.TryParse(dirSpec.Substring(p + 1), out n)) return new Byte[0];
                    dirSpec = String.Format("{0:D3}{1:D3}", m, n);
                }
                if (!FindFile(4, 4, String.Concat(dirSpec, ".DIR;*"), out dirNum, out dirSeq, out dirSpec)) return new Byte[0];
            }

            if (!FindFile(dirNum, dirSeq, fileSpec, out dirNum, out dirSeq, out fileSpec)) return new Byte[0];
            return ReadFile(dirNum, dirSeq);
        }

        private Byte[] ReadFile(UInt16 fileNum, UInt16 seqNum)
        {
            if (fileNum == 0) throw new ArgumentOutOfRangeException("fileNum");

            // determine size of file
            Int32 n = 0;
            UInt16 hf = fileNum;
            UInt16 hs = seqNum;
            Block H = GetFileHeader(hf, hs);
            while (H != null)
            {
                Int32 map = H[1] * 2; // map area pointer
                Int32 CTSZ = H[map + 6];
                Int32 LBSZ = H[map + 7];
                Int32 q = map + H[map + 8] * 2 + 10; // end of retrieval pointers
                Int32 p = map + 10; // start of retrieval pointers

                // count blocks referenced by file map
                Int32 ct;
                Int32 lbn;
                while (p < q)
                {
                    if ((CTSZ == 1) && (LBSZ == 3)) // Format 1 (normal format)
                    {
                        lbn = H[p++] << 16;
                        ct = H[p++];
                        lbn += H.GetUInt16L(ref p);
                    }
                    else if ((CTSZ == 2) && (LBSZ == 2)) // Format 2 (not implemented)
                    {
                        ct = H.GetUInt16L(ref p);
                        lbn = H.GetUInt16L(ref p);
                    }
                    else if ((CTSZ == 2) && (LBSZ == 4)) // Format3 (not implemented)
                    {
                        ct = H.GetUInt16L(ref p);
                        lbn = H.GetUInt16L(ref p) << 16;
                        lbn += H.GetUInt16L(ref p);
                    }
                    else // unknown format
                    {
                        throw new InvalidDataException();
                    }
                    ct++;
                    n += ct;
                }

                UInt16 nf = H.GetUInt16L(map + 2);
                UInt16 ns = H.GetUInt16L(map + 4);
                H = (nf == 0) ? null : GetFileHeader(nf, ns);
            }

            // read file
            Byte[] buf = new Byte[n * 512];
            Int32 bp = 0;
            hf = fileNum;
            hs = seqNum;
            H = GetFileHeader(hf, hs);
            while (H != null)
            {
                Int32 map = H[1] * 2; // map area pointer
                Int32 CTSZ = H[map + 6];
                Int32 LBSZ = H[map + 7];
                Int32 q = map + H[map + 8] * 2 + 10; // end of retrieval pointers
                Int32 p = map + 10; // start of retrieval pointers

                // read blocks referenced by file map
                Int32 ct;
                Int32 lbn;
                while (p < q)
                {
                    if ((CTSZ == 1) && (LBSZ == 3)) // Format 1 (normal format)
                    {
                        lbn = H[p++] << 16;
                        ct = H[p++];
                        lbn += H.GetUInt16L(ref p);
                    }
                    else if ((CTSZ == 2) && (LBSZ == 2)) // Format 2 (not implemented)
                    {
                        ct = H.GetUInt16L(ref p);
                        lbn = H.GetUInt16L(ref p);
                    }
                    else if ((CTSZ == 2) && (LBSZ == 4)) // Format3 (not implemented)
                    {
                        ct = H.GetUInt16L(ref p);
                        lbn = H.GetUInt16L(ref p) << 16;
                        lbn += H.GetUInt16L(ref p);
                    }
                    else // unknown format
                    {
                        throw new InvalidDataException();
                    }
                    for (Int32 i = 0; i <= ct; i++)
                    {
                        mVol[lbn + i].CopyTo(buf, bp);
                        bp += 512;
                    }
                }

                UInt16 nf = H.GetUInt16L(map + 2);
                UInt16 ns = H.GetUInt16L(map + 4);
                H = (nf == 0) ? null : GetFileHeader(nf, ns);
            }
            return buf;
        }

        public override Boolean SaveFS(String fileName, String format)
        {
            // RX01 and RX02 images should be written as physical images (including track 0)
            // all other images (including RX50) should be written as logical images
            FileStream f = new FileStream(fileName, FileMode.Create);
            Volume d = mVol;
            Int32 size;
            Type type;
            if (!Test(d, 3, out size, out type)) return false;
            if ((size == 494) || (size == 988)) // RX01 and RX02 sizes
            {
                Boolean iFlag = (d is InterleavedVolume);
                while (d.Base != null)
                {
                    d = d.Base;
                    if (d is InterleavedVolume) iFlag = true;
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
                    for (Int32 lsn = 0; lsn < size * SPB; lsn++)
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
                            mVol[lsn / SPB].CopyTo(buf, 0, (lsn % SPB) * d.BlockSize, d.BlockSize);
                            f.Write(buf, 0, d.BlockSize);
                        }
                    }
                }
            }
            else
            {
                Byte[] buf = new Byte[512];
                for (Int32 i = 0; i < size; i++)
                {
                    d[i].CopyTo(buf, 0);
                    f.Write(buf, 0, 512);
                }
            }
            f.Close();
            return true;
        }

        private Boolean FindFile(UInt16 dirNum, UInt16 dirSeq, String fileSpec, out UInt16 fileNum, out UInt16 fileSeq, out String fileName)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0))
            {
                fileNum = 0;
                fileSeq = 0;
                fileName = null;
                return false;
            }
            Regex RE = Regex(fileSpec);
            Byte[] data = ReadFile(dirNum, dirSeq);
            Int32 bp = 0;
            while (bp < data.Length)
            {
                UInt16 fnum = Buffer.GetUInt16L(data, bp);
                if (fnum != 0)
                {
                    String fn1 = Radix50.Convert(Buffer.GetUInt16L(data, bp + 6));
                    String fn2 = Radix50.Convert(Buffer.GetUInt16L(data, bp + 8));
                    String fn3 = Radix50.Convert(Buffer.GetUInt16L(data, bp + 10));
                    String ext = Radix50.Convert(Buffer.GetUInt16L(data, bp + 12));
                    UInt16 ver = Buffer.GetUInt16L(data, bp + 14);
                    String fn = String.Format("{0}{1}{2}.{3};{4:D0}", fn1, fn2, fn3, ext, ver);
                    if (RE.IsMatch(fn))
                    {
                        fileNum = fnum;
                        fileSeq = Buffer.GetUInt16L(data, bp + 2);
                        fn = String.Concat(fn1, fn2, fn3);
                        while (fn.EndsWith(" ")) fn = fn.Substring(0, fn.Length - 1);
                        while (ext.EndsWith(" ")) ext = ext.Substring(0, ext.Length - 1);
                        fileName = String.Format("{0}.{1};{2:D0}", fn, ext, ver);
                        return true;
                    }
                }
                bp += 16;
            }
            fileNum = 0;
            fileSeq = 0;
            fileName = null;
            return false;
        }

        private Block GetFileHeader(UInt16 fileNum, UInt16 seqNum)
        {
            Block H = GetFileHeader(mVol, fileNum);
            if ((H != null) && (H.GetUInt16L(2) == fileNum) && (H.GetUInt16L(4) == seqNum)) return H;
            return null;
        }
    }

    partial class ODS1 : IFileSystemAuto
    {
        public static TestDelegate GetTest()
        {
            return ODS1.Test;
        }

        // level 0 - check basic volume parameters (return required block size and volume type)
        // level 1 - check boot block (return volume size and type)
        // level 2 - check volume descriptor (aka home/super block) (return volume size and type)
        // level 3 - check file headers (aka inodes) (return volume size and type)
        // level 4 - check directory structure (return volume size and type)
        // level 5 - check file header allocation (return volume size and type)
        // level 6 - check data block allocation (return volume size and type)
        public static Boolean Test(Volume volume, Int32 level, out Int32 size, out Type type)
        {
            if (volume == null) throw new ArgumentNullException("volume");

            // level 0 - check basic volume parameters (return required block size and volume type)
            size = 512;
            type = typeof(Volume);
            if (volume == null) return false;
            if (volume.BlockSize != size) return Debug.WriteLine(false, 1, "ODS1.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            if (level == 1)
            {
                size = -1;
                type = typeof(Volume);
                if (volume.BlockCount < 1) return Debug.WriteLine(false, 1, "ODS1.Test: volume too small to contain boot block");
                return true;
            }

            // level 2 - check volume descriptor (aka home/super block) (return volume size and type)
            size = -1;
            type = null;
            if (volume.BlockCount < 2) return Debug.WriteLine(false, 1, "ODS1.Test: volume too small to contain home block");
            Block HB = volume[1];
            if (!IsChecksumOK(HB, 58)) return Debug.WriteLine(false, 1, "ODS1.Test: home block first checksum invalid");
            if (!IsChecksumOK(HB, 510)) return Debug.WriteLine(false, 1, "ODS1.Test: home block second checksum invalid");
            Int32 FMAX = HB.GetUInt16L(6); // H.FMAX
            if (FMAX < 16) return Debug.WriteLine(false, 1, "ODS1.Test: home block maximum number of files invalid (is {0:D0}, require n >= 16)", FMAX);
            Int32 n = (FMAX + 4095) / 4096;
            Int32 l = HB.GetUInt16L(0); // H.IBSZ, index file bitmap size
            Int32 fLim = l * 4096; // file limit (based on current data structure sizes; FMAX may be higher)
            if (fLim > FMAX) fLim = FMAX;
            if ((l < 1) || (l > n)) return Debug.WriteLine(false, 1, "ODS1.Test: home block index file bitmap size invalid (is {0:D0}, require 1 <= n <= {1:D0})", l, n);
            n = (HB.GetUInt16L(2) << 16) + HB.GetUInt16L(4); // HB.IBLB - index file bitmap LBN
            if ((n <= 1) || (n >= volume.BlockCount - l - 16)) return Debug.WriteLine(false, 1, "ODS1.Test: home block index file bitmap LBN invalid (is {0:D0}, require 1 < n < {1:D0})", n, volume.BlockCount - l - 16);
            if (HB.GetUInt16L(8) != 1) return Debug.WriteLine(false, 1, "ODS1.Test: home block storage bitmap cluster factor invalid (must be 1)");
            if (HB.GetUInt16L(10) != 0) return Debug.WriteLine(false, 1, "ODS1.Test: home block disk device type invalid (must be 0)");
            n = HB.GetUInt16L(12);
            if ((n != 0x0101) && (n != 0x0102)) return Debug.WriteLine(false, 1, "ODS1.Test: home block volume structure level invalid (must be 0x0101 or 0x0102)");
            type = typeof(ODS1);
            if (level == 2) return true;

            // level 3 - check file headers (aka inodes) (return volume size and type)
            // check index file 1,1,0
            UInt16[] HMap = new UInt16[fLim + 1]; // file header allocation
            HMap[1] = 1;
            Block H = GetFileHeader(volume, 1); // index file header
            if (!IsChecksumOK(H, 510)) return Debug.WriteLine(false, 1, "ODS1.Test: index file header checksum invalid");
            n = H.GetUInt16L(2); // H.FNUM
            l = H.GetUInt16L(4); // H.FSEQ
            if ((n != 1) || (l != 1)) return Debug.WriteLine(false, 1, "ODS1.Test: index file file number invalid (is {0:D0},{1:D0}, expect 1,1)", n, l);
            n = H.GetUInt16L(6); // H.FLEV
            if (n != 0x0101) return Debug.WriteLine(false, 1, "ODS1.Test: index file structure level invalid (is 0x{0:x4}, expect 0x0101)", n);
            // TODO: check File Ident Area fields (e.g. file name, file type)
            n = 0; // calculated size of index file (based on map area retrieval pointers)
            while (H != null)
            {
                Int32 map = H[1] * 2; // H.MPOF - map area offset
                Int32 CTSZ = H[map + 6]; // M.CTSZ
                Int32 LBSZ = H[map + 7]; // M.LBSZ
                Int32 p = map + 10; // start of retrieval pointers
                Int32 q = p + H[map + 8] * 2; // add M.USE words to find end of retrieval pointers
                Int32 ct;
                Int32 lbn;
                while (p < q)
                {
                    if ((CTSZ == 1) && (LBSZ == 3)) // Format 1 (normal format)
                    {
                        lbn = H[p++] << 16;
                        ct = H[p++];
                        lbn += H.GetUInt16L(ref p);
                    }
                    else if ((CTSZ == 2) && (LBSZ == 2)) // Format 2 (not implemented)
                    {
                        ct = H.GetUInt16L(ref p);
                        lbn = H.GetUInt16L(ref p);
                    }
                    else if ((CTSZ == 2) && (LBSZ == 4)) // Format3 (not implemented)
                    {
                        ct = H.GetUInt16L(ref p);
                        lbn = H.GetUInt16L(ref p) << 16;
                        lbn += H.GetUInt16L(ref p);
                    }
                    else // unknown format
                    {
                        return Debug.WriteLine(false, 1, "ODS1.Test: index file map area count/LBN field size invalid (is {0:D0},{1:D0}, require 1,3 or 2,2 or 2,4)", CTSZ, LBSZ);
                    }
                    if (lbn + ct >= volume.BlockCount) return Debug.WriteLine(false, 1, "ODS1.Test: index file map retrieval end pointer invalid (is {0:D0}, expect n < {1:D0})", lbn + ct, volume.BlockCount);
                    n += ct + 1;
                }
                UInt16 w = H[map + 2]; // M.EFNU - extension file number
                if (w > fLim) return Debug.WriteLine(false, 1, "ODS1.Test: index file extension chain invalid, header {0:D0} is outside home block limit (expect n <= {1:D0})", w, fLim);
                if (w > n) return Debug.WriteLine(false, 1, "ODS1.Test: index file extension chain invalid, header {0:D0} exceeds retrieval range of previous headers (expect n <= {1:D0}", w, n);
                if (HMap[w] != 0) return Debug.WriteLine(false, 1, "ODS1.Test: index file extension chain invalid, header {0:D0} already used by file {1:D0}", w, HMap[w]);
                if (w != 0) HMap[w] = 1;
                H = GetFileHeader(volume, w);
            }
            l = 2 + HB.GetUInt16L(0); // number of index file blocks not occupied by file headers
            if ((n < l + 16) || (n > l + fLim)) return Debug.WriteLine(false, 1, "ODS1.Test: index file block map length invalid (is {0:D0}, expect {1:D0} <= n <= {1:D0})", n, l + 16, l + fLim);
            n -= l; // number of file headers currently in Index File
            if (n < fLim) fLim = n; // adjust file limit down to fit current Index File size
            // check storage bitmap file 2,2,0
            if (HMap[2] != 0) return Debug.WriteLine(false, 1, "ODS1.Test: storage bitmap file header conflict, header 2 already used by file {0:D0}", HMap[2]);
            HMap[2] = 2;
            H = GetFileHeader(volume, 2); // storage bitmap file header
            n = H.GetUInt16L(2); // H.FNUM
            l = H.GetUInt16L(4); // H.FSEQ
            if ((n != 2) || (l != 2)) return Debug.WriteLine(false, 1, "ODS1.Test: storage bitmap file file number invalid (is {0:D0},{1:D0}, expect 2,2)", n, l);
            n = H.GetUInt16L(6); // H.FLEV
            if (n != 0x0101) return Debug.WriteLine(false, 1, "ODS1.Test: storage bitmap file structure level invalid (is 0x{0:x4}, expect 0x0101)", n);
            // TODO: check File Ident Area fields (e.g. file name, file type)
            n = 0; // calculated size of storage bitmap file (based on map area retrieval pointers)
            Int32 bLim = -1;
            while (H != null)
            {
                Int32 map = H[1] * 2; // H.MPOF - map area offset
                Int32 CTSZ = H[map + 6]; // M.CTSZ
                Int32 LBSZ = H[map + 7]; // M.LBSZ
                Int32 p = map + 10; // start of retrieval pointers
                Int32 q = p + H[map + 8] * 2; // add M.USE words to find end of retrieval pointers
                Int32 ct;
                Int32 lbn;
                while (p < q)
                {
                    if ((CTSZ == 1) && (LBSZ == 3)) // Format 1 (normal format)
                    {
                        lbn = H[p++] << 16;
                        ct = H[p++];
                        lbn += H.GetUInt16L(ref p);
                    }
                    else if ((CTSZ == 2) && (LBSZ == 2)) // Format 2 (not implemented)
                    {
                        ct = H.GetUInt16L(ref p);
                        lbn = H.GetUInt16L(ref p);
                    }
                    else if ((CTSZ == 2) && (LBSZ == 4)) // Format3 (not implemented)
                    {
                        ct = H.GetUInt16L(ref p);
                        lbn = H.GetUInt16L(ref p) << 16;
                        lbn += H.GetUInt16L(ref p);
                    }
                    else // unknown format
                    {
                        return Debug.WriteLine(false, 1, "ODS1.Test: storage bitmap file map area count/LBN field size invalid (is {0:D0},{1:D0}, require 1,3 or 2,2 or 2,4)", CTSZ, LBSZ);
                    }
                    if (lbn < 2) return Debug.WriteLine(false, 1, "ODS1.Test: storage bitmap file map retrieval start pointer invalid (is {0:D0}, expect n >= 2)", lbn);
                    if (lbn + ct >= volume.BlockCount) return Debug.WriteLine(false, 1, "ODS1.Test: storage bitmap file map retrieval end pointer invalid (is {0:D0}, expect n < {1:D0})", lbn + ct, volume.BlockCount);
                    n += ct + 1;
                    if (bLim == -1) // this must be the first extent, so look at the storage control block while we're here
                    {
                        Block B = volume[lbn];
                        l = 4 + B[3] * 4; // offset of size dword
                        bLim = (B.GetUInt16L(l) << 16) + B.GetUInt16L(l + 2); // size of unit in blocks from Storage Control Block
                        l = (bLim + 4095) / 4096; // number of storage bitmap blocks needed
                        if (l != B[3]) return Debug.WriteLine(false, 1, "ODS1.Test: storage bitmap size inconsistent with volume size (bitmap capacity {0:D0}, volume size {1:D0}", B[3] * 4096, bLim);
                    }
                }
                UInt16 w = H[map + 2]; // M.EFNU
                if (w > fLim) return Debug.WriteLine(false, 1, "ODS1.Test: storage bitmap file extension chain invalid, header {0:D0} is outside index file limit (expect n <= {1:D0})", w, fLim);
                if (HMap[w] != 0) return Debug.WriteLine(false, 1, "ODS1.Test: storage bitmap file extension chain invalid, header {0:D0} already used by file {1:D0}", w, HMap[w]);
                if (w != 0) HMap[w] = 2;
                H = GetFileHeader(volume, w);
            }
            if (n != l + 1) return Debug.WriteLine(false, 1, "ODS1.Test: storage bitmap file block map length invalid (is {0:D0}, expect {1:D0})", n, l + 1);
            size = bLim;
            if (level == 3) return true;

            // level 4 - check directory structure (return volume size and type)
            // TODO
            if (level == 4) return true;

            // level 5 - check file header allocation (return volume size and type)
            // TODO
            if (level == 5) return true;

            // level 6 - check data block allocation (return volume size and type)
            // TODO
            if (level == 6) return true;

            return false;
        }
    }

    partial class ODS1
    {
        private static Boolean IsChecksumOK(Block block, Int32 checksumOffset)
        {
            Int32 sum = 0;
            for (Int32 p = 0; p < checksumOffset; p += 2) sum += block.GetUInt16L(p);
            Int32 n = block.GetUInt16L(checksumOffset);
            Debug.WriteLine(2, "Block checksum @{0:D0} {1}: {2:x4} {3}= {4:x4}", checksumOffset, ((sum != 0) && ((sum % 65536) == n)) ? "PASS" : "FAIL", sum % 65536, ((sum % 65536) == n) ? '=' : '!', n);
            return ((sum != 0) && ((sum % 65536) == n));
        }

        private static Block GetFileHeader(Volume volume, UInt16 fileNum)
        {
            if (fileNum == 0) return null;
            Block HB = volume[1]; // home block
            if (fileNum > HB.GetUInt16L(6)) return null; // fileNum exceeds H.FMAX
            // first 16 file headers follow index bitmap (at volume LBN H.IBLB + H.IBSZ)
            if (fileNum <= 16) return volume[(HB.GetUInt16L(2) << 16) + HB.GetUInt16L(4) + HB.GetUInt16L(0) + fileNum - 1];
            // file headers 17+ are at index file VBN H.IBSZ + 19
            return GetFileBlock(volume, 1, HB.GetUInt16L(0) + 2 + fileNum);
        }

        private static Block GetFileBlock(Volume volume, UInt16 fileNum, Int32 vbn)
        {
            // get file header
            Block H = GetFileHeader(volume, fileNum);
            while (H != null)
            {
                Int32 map = H[1] * 2; // map area pointer
                Int32 CTSZ = H[map + 6];
                Int32 LBSZ = H[map + 7];
                Int32 p = map + 10; // start of retrieval pointers
                Int32 q = p + H[map + 8] * 2; // end of retrieval pointers

                // identify location of block in file map
                Int32 ct;
                Int32 lbn;
                while (p < q)
                {
                    if ((CTSZ == 1) && (LBSZ == 3)) // Format 1 (normal format)
                    {
                        lbn = H[p++] << 16;
                        ct = H[p++];
                        lbn += H.GetUInt16L(ref p);
                    }
                    else if ((CTSZ == 2) && (LBSZ == 2)) // Format 2 (not implemented)
                    {
                        ct = H.GetUInt16L(ref p);
                        lbn = H.GetUInt16L(ref p);
                    }
                    else if ((CTSZ == 2) && (LBSZ == 4)) // Format3 (not implemented)
                    {
                        ct = H.GetUInt16L(ref p);
                        lbn = H.GetUInt16L(ref p) << 16;
                        lbn += H.GetUInt16L(ref p);
                    }
                    else // unknown format
                    {
                        throw new InvalidDataException();
                    }
                    ct++;
                    if (vbn <= ct) return volume[lbn + vbn - 1];
                    vbn -= ct;
                }

                // if block wasn't found in this header, fetch next extension header
                H = GetFileHeader(volume, H.GetUInt16L(map + 2));
            }
            return null;
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

        private static Boolean IsDigit(Char value, Int32 minDigit, Int32 maxDigit)
        {
            Int32 n = value - '0';
            if ((n < minDigit) || (n > maxDigit)) return false;
            return true;
        }

        // convert an ODS-1 wildcard pattern to a Regex
        private static Regex Regex(String pattern)
        {
            String p = pattern.ToUpperInvariant();
            String vp = "*";
            Int32 i = p.IndexOf(';');
            if (i != -1)
            {
                vp = p.Substring(i + 1);
                p = p.Substring(0, i);
            }
            String np = p;
            String ep = "*";
            i = p.IndexOf('.');
            if (i != -1)
            {
                np = p.Substring(0, i);
                if (np.Length == 0) np = "*";
                ep = p.Substring(i + 1);
            }
            np = np.Replace("?", "[^ ]").Replace("*", @".*");
            ep = ep.Replace("?", "[^ ]").Replace("*", @".*");
            vp = vp.Replace("*", @".*");
            p = String.Concat("^", np, @" *\.", ep, " *;", vp, "$");
            Debug.WriteLine(2, "Regex: {0} => {1}", pattern, p);
            return new Regex(p);
        }
    }
}
