// Tar.cs
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


// tar file format references:
//   v7 / 2.11BSD source code
//   pdtar (comp.sources.unix Volume 12, Issues 68-70)
//   POSIX 1003
//   https://www.gnu.org/software/tar/manual/html_node/Standard.html
//
// Unix v7 tar file header format:
//     0 name (null-terminated null-padded string) - file name
//   100 mode ("%6o \0") - 12-bit file mode
//   108 uid  ("%6o \0") - 16-bit user id
//   116 gid  ("%6o \0") - 16-bit group id
//   124 size ("%11lo ") - 32-bit file size
//   136 mtime ("%11lo ") - 32-bit modification time (seconds since epoch)
//   148 checksum ("%6o\0 ") - 16-bit sum of all header bytes (with checksum bytes filled with spaces)
//   156 link flag ('1' if set, '\0' otherwise) - indicates whether file is hard-linked to a previous file in archive
//   157 link name (null-terminated null-padded string) - the name of the file this entry is a link to
//   257 (end of header)
//   512 (end of block)
//
// BSD tar file header format adds:
//   link flag = '2' and size = 0 for symbolic links
//   archives may include entries for directories (names end with '/', size = 0)
//
// pdtar (ustar) file header format adds:
//   link flag values '3' through '7', and '0' for '\0'
//   257 magic (null-terminated null-padded string) "ustar  \0"
//   265 uname (null-terminated null-padded string)
//   297 gname (null-terminated null-padded string)
//   329 major (octal)
//   337 minor (octal)
//   345 (end of header)
//
// gnu and posix add:
//   checksum format "%6o \0"
//   magic "ustar\0"+"vv" (vv=version)
//   additional flag values


// Future Improvements / To Do
// implement ChangeDir
// support fileSpec in ListDir
// support wildcards in FullName fileSpec
// implement SaveFS


using System;
using System.IO;
using System.Text;

namespace FSX
{
    partial class Tar : FileSystem
    {
        private Volume mVol;
        private String mType;
        private String mDir;

        public Tar(Volume volume)
        {
            mVol = volume;
            mType = "tar";
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
            Int32 zbc = 0;
            Int32 lbn = 0;
            while (lbn < mVol.BlockCount)
            {
                Block B = mVol[lbn++];
                if (B[0] == 0)
                {
                    if (++zbc == 2) break;
                    continue;
                }
                zbc = 0;
                String name = B.GetCString(0, 100, Encoding.ASCII);
                Int32 mode, uid, gid, size, mtime;
                ParseOctal(B, 100, 8, out mode);
                ParseOctal(B, 108, 8, out uid);
                ParseOctal(B, 116, 8, out gid);
                ParseOctal(B, 124, 12, out size);
                ParseOctal(B, 136, 12, out mtime);
                Byte flag = B.GetByte(156);
                String lname = B.GetCString(157, 100, Encoding.ASCII);
                String magic = B.GetCString(257, 8, Encoding.ASCII);
                Int32 n = (magic == "ustar  ") ? 1 : (magic == "ustar") ? 2 : 0;
                String uname = String.Format("{0,-8:D0}", uid);
                String gname = String.Format("{0,-8:D0}", gid);
                Int32 major = -1, minor = -1;
                if (n != 0)
                {
                    if (B.GetByte(265) != 0) uname = B.GetCString(265, 32, Encoding.ASCII).PadRight(8);
                    if (B.GetByte(297) != 0) gname = B.GetCString(297, 32, Encoding.ASCII).PadRight(8);
                    ParseOctal(B, 329, 8, out major);
                    ParseOctal(B, 337, 8, out minor);
                }
                String s = null;
                switch (flag)
                {
                    case 0: // regular file
                    case (Byte)'0': // regular file
                    case (Byte)'5': // directory
                    case (Byte)'6': // fifo
                    case (Byte)'7': // contiguous file
                        s = name;
                        if ((size > 0) && (flag != '5') && (!name.EndsWith("/"))) lbn += (size + 511) / 512;
                        break;
                    case (Byte)'1': // hard link
                        s = String.Concat(name, " == ", lname);
                        break;
                    case (Byte)'2': // symbolic link
                        s = String.Concat(name, " -> ", lname);
                        break;
                    case (Byte)'3': // character device
                        s = String.Format("CDEV({0:D0},{1:D0})", major, minor);
                        break;
                    case (Byte)'4': // block device
                        s = String.Format("BDEV({0:D0},{1:D0})", major, minor);
                        break;
                }
                output.WriteLine("{0} {1} {2} {3,10:D0} {4}", B.GetCString(100, 8, Encoding.ASCII), uname, gname, size, s);
            }
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
            String name = FullName(fileSpec);
            if (name == null) return;
            String buf = encoding.GetString(ReadFile(name));
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
            String name = FullName(fileSpec);
            if (name == null) return;
            Program.Dump(null, ReadFile(name), output, 16, 512, Program.DumpOptions.ASCII);
        }

        public override String FullName(String fileSpec)
        {
            Int32 zbc = 0;
            Int32 lbn = 0;
            while (lbn < mVol.BlockCount)
            {
                Block B = mVol[lbn++];
                if (B[0] == 0)
                {
                    if (++zbc == 2) break;
                    continue;
                }
                zbc = 0;
                String name = B.GetCString(0, 100, Encoding.ASCII);
                Int32 size;
                ParseOctal(B, 124, 12, out size);
                Byte flag = B.GetByte(156);
                if ((size > 0) && (flag != '5') && (!name.EndsWith("/"))) lbn += (size + 511) / 512;
                if (String.Compare(name, fileSpec, StringComparison.Ordinal) != 0) continue;
                if ((flag == 0) || (flag == (Byte)'0')) return name;
                if (flag == '1') return FullName(B.GetCString(157, 100, Encoding.ASCII));
            }
            return null;
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            Int32 zbc = 0;
            Int32 lbn = 0;
            while (lbn < mVol.BlockCount)
            {
                Block B = mVol[lbn++];
                if (B[0] == 0)
                {
                    if (++zbc == 2) break;
                    continue;
                }
                zbc = 0;
                String name = B.GetCString(0, 100, Encoding.ASCII);
                Int32 size;
                ParseOctal(B, 124, 12, out size);
                Byte flag = B.GetByte(156);
                if ((String.Compare(name, fileSpec, StringComparison.Ordinal) != 0) || ((flag != 0) && (flag != '0')))
                {
                    if ((size > 0) && (flag != '5') && (!name.EndsWith("/"))) lbn += (size + 511) / 512;
                    continue;
                }
                Byte[] buf = new Byte[size];
                Int32 p = 0;
                while (size > 0)
                {
                    B = mVol[lbn++];
                    Int32 n = B.Size;
                    if (n > size) n = size;
                    B.CopyTo(buf, p, 0, n);
                    p += n;
                    size -= n;
                }
                return buf;
            }
            return null;
        }

        public override Boolean SaveFS(String fileName, String format)
        {
            throw new NotImplementedException();
        }
    }

    partial class Tar
    {
        // parse bytes representing a printable octal value
        private static Boolean ParseOctal(Block block, Int32 offset, Int32 count, out Int32 value)
        {
            Int32 i = offset;
            return ParseOctal(block, ref i, count, out value);
        }

        private static Boolean ParseOctal(Block block, ref Int32 offset, Int32 count, out Int32 value)
        {
            value = -1;
            if (block.Size - offset < count) return false;
            Int32 p = 0;
            while ((p < count - 1) && (block[offset + p] == ' ')) p++; // skip leading blanks
            Int32 n = 0;
            Int32 m = 1;
            if (block[offset + p] == '-')
            {
                m = -1;
                p++;
            }
            if ((p == count) || (block[offset + p] < '0') || (block[offset + p] > '7')) return false;
            while ((p < count) && (block[offset + p] >= '0') && (block[offset + p] < '8'))
            {
                n *= 8;
                n += block[offset + p++] - '0';
            }
            while ((p < count) && (block[offset + p] == ' ')) p++;
            if ((p < count) && (block[offset + p] == 0)) p++;
            if (p != count) return false;
            offset += p;
            value = m * n;
            return true;
        }
    }

    partial class Tar : IFileSystemAuto
    {
        public static TestDelegate GetTest()
        {
            return Tar.Test;
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
            if (volume.BlockSize != size) return Debug.WriteLine(false, 1, "Tar.Test: invalid block size (is {0:D0}, require {1:D0})", volume.BlockSize, size);
            if (level == 0) return true;

            // level 1 - check boot block (return volume size and type)
            size = -1;
            if (level == 1)
            {
                return true;
            }

            // level 2 - check volume descriptor (aka home/super block) (return file system size and type)
            type = typeof(Tar);
            if (level == 2)
            {
                return true;
            }

            // level 3 - check file headers (aka inodes) (return file system size and type)
            Int32 zbc = 0;
            Int32 lbn = 0;
            while (lbn < volume.BlockCount)
            {
                Block B = volume[lbn++];
                Int32 bp = 0;
                if (B[bp] == 0)
                {
                    if (++zbc == 2) break;
                    continue;
                }
                zbc = 0;
                Int32 sum;
                if (!ParseOctal(B, 148, 7, out sum)) return Debug.WriteLine(false, 1, "Tar.Test: header checksum not valid (at file offset {0:D0})", (lbn - 1) * B.Size + 148);
                Int32 n = 0;
                for (Int32 i = 0; i < B.Size; i++) n += ((i >= 148) && (i < 156)) ? (Byte)' ' : B.GetByte(i);
                if (n > 65535) n &= 0xffff;
                if (n != sum) return Debug.WriteLine(false, 1, "Tar.Test: header checksum mismatch (calculated 0x{0:X4}, recorded 0x{1:X4} at file offset {2:D0})", n, sum, (lbn - 1) * B.Size + 148);
                String name = B.GetCString(bp, 100, Encoding.ASCII);
                bp += 100;
                Int32 mode, uid, gid, len, mtime;
                if (!ParseOctal(B, ref bp, 8, out mode)) return Debug.WriteLine(false, 1, "Tar.Test: file {0} mode not valid (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp);
                if (!ParseOctal(B, ref bp, 8, out uid)) return Debug.WriteLine(false, 1, "Tar.Test: file {0} uid not valid (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp);
                if (!ParseOctal(B, ref bp, 8, out gid)) return Debug.WriteLine(false, 1, "Tar.Test: file {0} gid not valid (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp);
                if (!ParseOctal(B, ref bp, 12, out len)) return Debug.WriteLine(false, 1, "Tar.Test: file {0} size not valid (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp);
                if (!ParseOctal(B, ref bp, 12, out mtime)) return Debug.WriteLine(false, 1, "Tar.Test: file {0} mtime not valid (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp);
                bp += 8;
                Byte flag = B.GetByte(ref bp);
                String lname = B.GetCString(bp, 100, Encoding.ASCII);
                bp += 100;
                String magic = B.GetCString(bp, 8, Encoding.ASCII);
                n = (magic == "ustar  ") ? 1 : (magic == "ustar") ? 2 : 0;
                String ver = (n == 2) ? B.GetString(bp + 6, 2, Encoding.ASCII) : null;
                bp += 8;
                if (B.GetByte(155) != ((n == 2) ? 0 : 32)) return Debug.WriteLine(false, 1, "Tar.Test: file {0} checksum missing trailing {1} (at file offset {2:D0})", name, (n == 2) ? "null" : "blank", (lbn - 1) * B.Size + 155);
                if (n == 0)
                {
                    if (magic.Length != 0) return Debug.WriteLine(false, 1, "Tar.Test: file {0} unrecognized magic {1}", name, magic);
                    if ((flag != 0) && (flag != '1') && (flag != '2')) return Debug.WriteLine(false, 1, "Tar.Test: file {0} link flag not valid (expect 0x00, 0x31, or 0x32, is 0x{1:X2})", name, flag);
                }
                else
                {
                    if ((flag != 0) && ((flag < '0') || (flag > '7'))) return Debug.WriteLine(false, 1, "Tar.Test: file {0} link flag not valid (expect '\0', or '0'-'7', is 0x{1:X2})", name, flag);
                    bp += 64; // skip uname, gname
                    if ((flag == '3') || (flag == '4'))
                    {
                        Int32 major, minor;
                        if (!ParseOctal(B, ref bp, 8, out major)) return Debug.WriteLine(false, 1, "Tar.Test: file {0} major not valid (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp);
                        if (!ParseOctal(B, ref bp, 8, out minor)) return Debug.WriteLine(false, 1, "Tar.Test: file {0} minor not valid (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp);
                    }
                }
                if (name.EndsWith("/")) len = 0;
                if (flag == '0') flag = 0;
                if ((len > 0) && (flag == 0)) lbn += (len + 511) / 512;
                if (lbn > volume.BlockCount) return Debug.WriteLine(false, 1, "Tar.Test: file {0} data size ({1:D0}) exceeds volume size", name, len);
            }
            size = lbn;
            if (level == 3) return true;

            // level 4 - check directory structure (return file system size and type)
            if (level == 4) return true;

            // level 5 - check file header allocation (return file system size and type)
            zbc = 0;
            lbn = 0;
            while (lbn < volume.BlockCount)
            {
                Block B = volume[lbn++];
                Int32 bp = 0;
                if (B[bp] == 0)
                {
                    // first byte zero implies the entire block is zeroed, but check to see if it really is
                    for (Int32 i = 1; i < B.Size; i++)
                    {
                        if (B[i] != 0)
                        {
                            Debug.WriteLine(1, "Tar.Test: zero block isn't fully zeroed (expect 0 at file offset {0:D0}, is {1:D0})", (lbn - 1) * B.Size + i, B[i]);
                            break;
                        }
                    }
                    if (++zbc == 2) break;
                    continue;
                }
                zbc = 0;
                String name = B.GetCString(bp, 100, Encoding.ASCII);
                for (Int32 i = name.Length; i < 100; i++)
                {
                    if (B.GetByte(bp + i) != 0)
                    {
                        Debug.WriteLine(1, "Tar.Test: file {0} name is not null-padded (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp + i);
                        break;
                    }
                }
                bp += 100;
                bp += 24; // skip mode, uid, gid
                Int32 len;
                ParseOctal(B, ref bp, 12, out len);
                bp += 20; // skip mtime, checksum
                Byte flag = B.GetByte(ref bp);
                String lname = B.GetCString(bp, 100, Encoding.ASCII);
                for (Int32 i = lname.Length; i < 100; i++)
                {
                    if (B.GetByte(bp + i) != 0)
                    {
                        Debug.WriteLine(1, "Tar.Test: file {0} link name is not null-padded (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp + i);
                        break;
                    }
                }
                bp += 100;
                String magic = B.GetCString(bp, 8, Encoding.ASCII);
                Int32 n = (magic == "ustar  ") ? 1 : (magic == "ustar") ? 2 : 0;
                String ver = (n == 2) ? B.GetString(bp + 6, 2, Encoding.ASCII) : null;
                bp += 8;
                if (n != 0)
                {
                    String uname = B.GetCString(bp, 32, Encoding.ASCII);
                    for (Int32 i = uname.Length; i < 32; i++)
                    {
                        if (B.GetByte(bp + i) != 0)
                        {
                            Debug.WriteLine(1, "Tar.Test: file {0} uname is not null-padded (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp + i);
                            break;
                        }
                    }
                    bp += 32;
                    String gname = B.GetCString(bp, 32, Encoding.ASCII);
                    for (Int32 i = gname.Length; i < 32; i++)
                    {
                        if (B.GetByte(bp + i) != 0)
                        {
                            Debug.WriteLine(1, "Tar.Test: file {0} gname is not null-padded (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp + i);
                            break;
                        }
                    }
                    bp += 32;
                    bp += 16; // skip major, minor
                }
                // if (n == 2) skip additional posix/gnu fields
                while (bp < B.Size)
                {
                    if (B.GetByte(ref bp) != 0)
                    {
                        Debug.WriteLine(1, "Tar.Test: file {0} header is not null-padded (at file offset {1:D0})", name, (lbn - 1) * B.Size + bp - 1);
                        break;
                    }
                }
                if (name.EndsWith("/")) len = 0;
                if (flag == '0') flag = 0;
                if ((len > 0) && (flag == 0)) lbn += (len + 511) / 512;
            }
            size = lbn;
            if (level == 5) return true;

            // level 6 - check data block allocation (return file system size and type)
            if (level == 6) return true;

            return false;
        }
    }
}
