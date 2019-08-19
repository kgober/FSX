// HostFS.cs
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

// HostFS - Host File System
// The HostFS and HostPath classes provide access to the 'native' host file system,
// mainly for use as a source or destination for load/save operations.

using System;
using System.IO;
using System.Text;

namespace FSX
{
    // HostFS - mount a host volume

    class HostFS : FileSystem
    {
        protected String mSource;
        protected String mType;
        protected DirectoryInfo mCWD;
        protected String mDir;

        protected HostFS()
        {
        }

        public HostFS(String source, String format)
        {
            if (source.EndsWith(@"\")) source = source.Substring(0, source.Length - 1);
            mSource = source;
            mType = String.Concat("Host/", format);
            mCWD = new DirectoryInfo(String.Concat(source, "."));
            String dir = mCWD.FullName.Substring(2);
            if (!dir.EndsWith(@"\")) dir = String.Concat(dir, @"\");
            mDir = dir;
        }

        public override Disk Disk
        {
            get { return null; }
        }

        public override String Source
        {
            get { return mSource; }
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
            get { return Encoding.Default; }
        }

        public override void ChangeDir(String dirSpec)
        {
            String dir = (dirSpec.StartsWith(@"\")) ? dirSpec : String.Concat(mDir, dirSpec);
            if (!IsValidDir(dir)) return;
            mCWD = new DirectoryInfo(dir);
            dir = mCWD.FullName.Substring(2);
            if (!dir.EndsWith(@"\")) dir = String.Concat(dir, @"\");
            mDir = dir;
        }

        public override void ListDir(String fileSpec, TextWriter output)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) fileSpec = "*.*";
            String s = mCWD.FullName.Substring(0, 1);
            DriveInfo di = new DriveInfo(s);
            output.WriteLine(" Volume in drive {0} is {1}", s, di.VolumeLabel);
            output.WriteLine();
            output.WriteLine(" Directory of {0}", mCWD.FullName);
            output.WriteLine();
            Int32 fc = 0;
            Int64 fs = 0;
            Int32 dc = 0;
            if (mCWD.FullName.Length > 3)
            {
                DirectoryInfo i = new DirectoryInfo(".");
                s = "<DIR>         ";
                output.WriteLine("{0:MM/dd/yyyy  hh:mm tt}    {1} {2}", i.LastWriteTime, s, ".");
                output.WriteLine("{0:MM/dd/yyyy  hh:mm tt}    {1} {2}", i.LastWriteTime, s, "..");
                dc = 2;
            }
            foreach (FileSystemInfo e in mCWD.GetFileSystemInfos(fileSpec))
            {
                if ((e.Attributes & (FileAttributes.Directory|FileAttributes.Hidden)) == FileAttributes.Directory)
                {
                    DirectoryInfo i = new DirectoryInfo(e.FullName);
                    s = "<DIR>         ";
                    if ((i.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) s = "<JUNCTION>    ";
                    output.WriteLine("{0:MM/dd/yyyy  hh:mm tt}    {1} {2}", i.LastWriteTime, s, i.Name);
                    dc++;
                }
                else if ((e.Attributes & FileAttributes.Hidden) == 0)
                {
                    FileInfo i = new FileInfo(e.FullName);
                    output.WriteLine("{0:MM/dd/yyyy  hh:mm tt} {1,17:N0} {2}", i.LastWriteTime, i.Length, i.Name);
                    fc++;
                    fs += i.Length;
                }
            }
            output.WriteLine("  {0,14:N0} File(s) {1,14:N0} bytes", fc, fs);
            output.WriteLine("  {0,14:N0} Dir(s)  {1,14:N0} bytes free", dc, di.AvailableFreeSpace);
        }

        public override void DumpDir(String fileSpec, TextWriter output)
        {
            if ((fileSpec == null) || (fileSpec.Length == 0)) fileSpec = "*.*";
            String s = mCWD.FullName.Substring(0, 1);
            DriveInfo di = new DriveInfo(s);
            output.WriteLine(" Volume in drive {0} is {1}", s, di.VolumeLabel);
            output.WriteLine();
            output.WriteLine(" Directory of {0}", mCWD.FullName);
            output.WriteLine();
            Int32 fc = 0;
            Int64 fs = 0;
            Int32 dc = 0;
            if (mCWD.FullName.Length > 3)
            {
                DirectoryInfo i = new DirectoryInfo(".");
                s = "<DIR>         ";
                output.WriteLine("{0:MM/dd/yyyy  hh:mm tt}    {1} {2}", i.LastWriteTime, s, ".");
                output.WriteLine("{0:MM/dd/yyyy  hh:mm tt}    {1} {2}", i.LastWriteTime, s, "..");
                dc = 2;
            }
            foreach (FileSystemInfo e in mCWD.GetFileSystemInfos(fileSpec))
            {
                if ((e.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    DirectoryInfo i = new DirectoryInfo(e.FullName);
                    s = "<DIR>         ";
                    if ((i.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) s = "<JUNCTION>    ";
                    output.WriteLine("{0:MM/dd/yyyy  hh:mm tt}    {1} {2}", i.LastWriteTime, s, i.Name);
                    dc++;
                }
                else
                {
                    FileInfo i = new FileInfo(e.FullName);
                    output.WriteLine("{0:MM/dd/yyyy  hh:mm tt} {1,17:N0} {2}", i.LastWriteTime, i.Length, i.Name);
                    fc++;
                    fs += i.Length;
                }
            }
            output.WriteLine("  {0,14:N0} File(s) {1,14:N0} bytes", fc, fs);
            output.WriteLine("  {0,14:N0} Dir(s)  {1,14:N0} bytes free", dc, di.AvailableFreeSpace);
        }

        public override void ListFile(String fileSpec, Encoding encoding, TextWriter output)
        {
            String fn = (fileSpec.StartsWith(@"\")) ? fileSpec : String.Concat(mCWD.FullName, @"\", fileSpec);
            if (!IsValidFile(fn)) return;
            output.Write(File.ReadAllText(fn, encoding));
        }

        public override void DumpFile(String fileSpec, TextWriter output)
        {
            String fn = (fileSpec.StartsWith(@"\")) ? fileSpec : String.Concat(mCWD.FullName, @"\", fileSpec);
            if (!IsValidFile(fn)) return;
            Program.Dump(null, File.ReadAllBytes(fn), output, Program.DumpOptions.Radix50 | Program.DumpOptions.EBCDIC);
        }

        public override String FullName(String fileSpec)
        {
            String fn = (fileSpec.StartsWith(@"\")) ? fileSpec : String.Concat(mCWD.FullName, @"\", fileSpec);
            if (!IsValidFile(fn)) return null;
            FileInfo i = new FileInfo(fn);
            return i.FullName.Substring(2);
        }

        public override Byte[] ReadFile(String fileSpec)
        {
            String fn = (fileSpec.StartsWith(@"\")) ? fileSpec : String.Concat(mCWD.FullName, @"\", fileSpec);
            if (!IsValidFile(fn)) return new Byte[0];
            return File.ReadAllBytes(fn);
        }

        public override Boolean SaveFS(String fileName, String format)
        {
            return false;
        }

        protected virtual Boolean IsValidDir(String hostPath)
        {
            return Directory.Exists(hostPath);
        }

        protected virtual Boolean IsValidFile(String hostPath)
        {
            return File.Exists(hostPath);
        }
    }


    // HostPath - mount a host directory as a volume (with "chroot" semantics)

    class HostPath : HostFS
    {
        public HostPath(String source)
        {
            String dir = source;
            if (!dir.EndsWith(@"\")) dir = String.Concat(dir, @"\");
            mCWD = new DirectoryInfo(dir);
            mSource = mCWD.FullName;
            mType = "HostPath";
            mDir = @"\";
        }

        public override void ChangeDir(String dirSpec)
        {
            String dir = String.Concat((dirSpec.StartsWith(@"\")) ? mSource : mCWD.FullName, dirSpec);
            if (!dir.EndsWith(@"\")) dir = String.Concat(dir, @"\");
            if (!IsValidDir(dir)) return;
            DirectoryInfo i = new DirectoryInfo(dir);
            mCWD = i;
            mDir = i.FullName.Substring(mSource.Length - 1);
        }

        public override String FullName(String fileSpec)
        {
            String fn = String.Concat((fileSpec.StartsWith(@"\")) ? mSource : mCWD.FullName, fileSpec);
            if (!IsValidFile(fn)) return null;
            FileInfo i = new FileInfo(fn);
            return i.FullName.Substring(mSource.Length - 1);
        }

        protected override Boolean IsValidDir(String hostPath)
        {
            if (!Directory.Exists(hostPath)) return false;
            DirectoryInfo i = new DirectoryInfo(hostPath);
            if (!i.FullName.StartsWith(mSource)) return false;
            return true;
        }

        protected override Boolean IsValidFile(String hostPath)
        {
            if (!File.Exists(hostPath)) return false;
            FileInfo i = new FileInfo(hostPath);
            if (!i.FullName.StartsWith(mSource)) return false;
            return true;
        }
    }
}
