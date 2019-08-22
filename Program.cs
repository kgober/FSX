// Program.cs
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


// File System Exchange (FSX) Program Structure
//
// A FileSystem is a file/directory structure and resides on an IVolume (HostFS excepted).
// An IVolume is a logical block interface to an underlying Disk (or a portion of one).
// A Disk is a collection of sectors and usually resides in an image file.  Some Disks
// represent a transformation of an underlying Disk (e.g. clustering or interleaving).


using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FSX
{
    class Program
    {
        public struct VDE
        {
            public String Key;
            public FileSystem FS;

            public VDE(String key)
            {
                Key = key;
                FS = null;
            }

            public VDE(String key, FileSystem fileSystem)
            {
                Key = key;
                FS = fileSystem;
            }
        }

        static public TextWriter Out = Console.Out;
        static public VDE Vol;
        static public Int32 Verbose = 0;

        static private Dictionary<String, VDE> VolMap = new Dictionary<String, VDE>(StringComparer.OrdinalIgnoreCase);

        static void Main(String[] args)
        {
            // parse command-line arguments
            // (none yet)

            // import host volumes
            MountHostVolumes();

            // command loop
            while (true)
            {
                Console.Error.Write("\nFSX>");
                String cmdline = Console.In.ReadLine();
                cmdline = cmdline.TrimStart(' ', '\t');
                Int32 p = cmdline.IndexOf(' ');
                String cmd = (p == -1) ? cmdline : cmdline.Substring(0, p);
                String arg = (p == -1) ? String.Empty : cmdline.Substring(p + 1);
                if ((cmd == "exit") || (cmd == "quit"))
                {
                    break;
                }
                else if (cmd == "help")
                {
                    ShowHelp();
                }
                else if ((cmd == "verb") || (cmd == "verbose"))
                {
                    Int32.TryParse(arg, out Verbose);
                }
                else if ((cmd == "vols") || (cmd == "volumes"))
                {
                    foreach (VDE v in VolMap.Values)
                    {
                        Out.WriteLine("{0}:\t{1}\t{2}", v.Key, v.FS.Type, v.FS.Source);
                    }
                }
                else if ((cmd == "load") || (cmd == "mount"))
                {
                    p = arg.IndexOf(' ');
                    String s = (p == -1) ? arg : arg.Substring(0, p);       // volume name
                    arg = (p == -1) ? String.Empty : arg.Substring(p + 1);  // volume source
                    if (s.EndsWith(@"\")) s = s.Substring(0, s.Length - 1);
                    if (s.EndsWith(@":")) s = s.Substring(0, s.Length - 1);
                    if (s.Length == 1) s = s.ToUpperInvariant();
                    FileSystem fs = LoadFS(arg);
                    if (fs != null)
                    {
                        VDE v = new VDE(s, fs);
                        VolMap[v.Key] = v;
                        Console.Error.WriteLine("{0}: = {1} [{2}]", v.Key, v.FS.Source, v.FS.Type);
                        if ((v.FS.GetType() == typeof(HostFS)) && (!v.FS.Source.StartsWith(s))) v.FS.ChangeDir(@"\");
                    }
                }
                else if ((cmd == "save") || (cmd == "write"))
                {
                    p = arg.IndexOf(' ');
                    String s = (p == -1) ? arg : arg.Substring(0, p);       // source volume/file
                    arg = (p == -1) ? String.Empty : arg.Substring(p + 1);  // target file
                    VDE src = ParseVol(ref s);
                    if (s.Length == 0)
                    {
                        // save volume
                        if (arg.Length == 0) arg = String.Concat(src.Key, ".", src.FS.Type, ".img");
                        if (src.FS.SaveFS(arg, null)) Console.Error.WriteLine("{0}: => {1}", src.Key, arg);
                    }
                    else
                    {
                        // save file
                        if (arg.Length == 0) arg = s;
                        String sfn = src.FS.FullName(s);
                        if (sfn != null)
                        {
                            File.WriteAllBytes(arg, src.FS.ReadFile(s));
                            Console.Error.WriteLine("{0}:{1} => {2}", src.Key, sfn, arg);
                        }
                    }
                }
                else if ((cmd == "unload") || (cmd == "unmount") || (cmd == "umount"))
                {
                    p = arg.IndexOf(':');
                    String k = (p == -1) ? arg : arg.Substring(0, p);
                    if (VolMap.ContainsKey(k))
                    {
                        VDE v = VolMap[k];
                        if (v.Key != Vol.Key)
                        {
                            VolMap.Remove(k);
                        }
                        else
                        {
                            Console.Error.WriteLine("Cannot unmount current volume.  Change to another volume first.");
                        }
                    }
                }
                else if (cmd == "dirs")
                {
                    foreach (VDE v in VolMap.Values)
                    {
                        Out.WriteLine("{0}:\t{1}", v.Key, v.FS.Dir);
                    }
                }
                else if (cmd == "pwd")
                {
                    Out.WriteLine("{0}:{1}", Vol.Key, Vol.FS.Dir);
                }
                else if (cmd == "cd")
                {
                    VDE v = ParseVol(ref arg);
                    v.FS.ChangeDir(arg);
                }
                else if ((cmd == "dir") || (cmd == "ls"))
                {
                    VDE v = ParseVol(ref arg);
                    v.FS.ListDir(arg, Out);
                }
                else if (cmd == "dumpdir")
                {
                    VDE v = ParseVol(ref arg);
                    v.FS.DumpDir(arg, Out);
                }
                else if ((cmd == "type") || (cmd == "cat"))
                {
                    VDE v = ParseVol(ref arg);
                    v.FS.ListFile(arg, v.FS.DefaultEncoding, Out);
                }
                else if ((cmd == "dump") || (cmd == "od"))
                {
                    VDE v = ParseVol(ref arg);
                    v.FS.DumpFile(arg, Out);
                }
                else if (cmd.EndsWith(":"))
                {
                    String k = cmd.Substring(0, cmd.Length - 1);
                    if (VolMap.ContainsKey(k)) Vol = VolMap[k];
                }
                else
                {
                    Console.Error.WriteLine("Command not recognized: {0}", cmd);
                }
            }
        }

        static void MountHostVolumes()
        {
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.IsReady)
                {
                    Console.Error.WriteLine("{0} type={1}/{2}", d.Name, d.DriveType.ToString(), d.DriveFormat);
                    String s = d.Name;
                    if (s.EndsWith(@"\")) s = s.Substring(0, s.Length - 1);
                    if (s.EndsWith(@":")) s = s.Substring(0, s.Length - 1);
                    VDE v = new VDE(s, new HostFS(d.Name, d.DriveFormat));
                    VolMap.Add(v.Key, v);
                    if (Vol.Key == null) Vol = v;
                }
            }
        }

        static VDE ParseVol(ref String pathSpec)
        {
            Int32 p = pathSpec.IndexOf(':');
            if (p == -1) return Vol;
            String k = pathSpec.Substring(0, p);
            if (!VolMap.ContainsKey(k)) return Vol;
            pathSpec = pathSpec.Substring(p + 1);
            return VolMap[k];
        }
        
        static FileSystem LoadFS(String source)
        {
            if (source == null) return null;

            // always attempt HostFS then HostPath for "X:" (where X is a single character)
            if ((source.Length == 2) && (source[1] == ':'))
            {
                String name = String.Concat(source.ToUpperInvariant(), @"\");
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.Name == name) return new HostFS(d.Name, d.DriveFormat);
                }
                // fall through in case HostPath is able to resolve source
                if (Directory.Exists(name)) return new HostPath(name);
            }

            // identify source volume
            VDE vol = new VDE();
            String path = source;
            Int32 p = path.IndexOf(':');
            if (p == -1)
            {
                vol = Vol;
            }
            else
            {
                String k = path.Substring(0, p);
                foreach (VDE v in VolMap.Values)
                {
                    if (String.Compare(v.Key, k, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        vol = v;
                        path = path.Substring(p + 1);
                        break;
                    }
                }
            }

            // use HostPath if source is a HostFS or HostPath directory
            if ((vol.FS is HostFS) || (vol.FS is HostPath))
            {
                String dir = vol.FS.Source;
                if (vol.FS is HostPath) dir = dir.Substring(0, dir.Length - 1); // remove trailing backslash
                dir = String.Concat(dir, (path.StartsWith(@"\")) ? String.Empty : vol.FS.Dir, path, (path.EndsWith(@"\")) ? String.Empty : @"\");
                if (Directory.Exists(dir))
                {
                    DirectoryInfo di = new DirectoryInfo(dir);
                    if (di.FullName.StartsWith(vol.FS.Source)) return new HostPath(di.FullName);
                }
            }

            // otherwise source must be a file
            String s = source;
            String opts = String.Empty;
            p = path.IndexOf('<');
            if (p != -1)
            {
                opts = path.Substring(p);
                path = path.Substring(0, p).TrimEnd(' ');
                s = s.Substring(0, s.Length - opts.Length).TrimEnd(' ');
            }
            if (((vol.FS == null) && (!File.Exists(s))) || ((vol.FS != null) && (vol.FS.FullName(path) == null) && (!File.Exists(s))))
            {
                Console.Error.WriteLine("File Not Found: {0}", s);
                return null;
            }
            Byte[] data = ((vol.FS != null) && (vol.FS.FullName(path) != null)) ? vol.FS.ReadFile(path) : File.ReadAllBytes(s);
            if ((vol.FS != null) && (vol.FS.FullName(path) != null)) s = String.Concat(vol.Key, ":", vol.FS.FullName(path));

            // process options
            while (opts.Length != 0)
            {
                p = opts.IndexOf('>');
                if (p != -1)
                {
                    String opt = opts.Substring(1, p - 1);
                    opts = opts.Substring(p + 1);
                    if ((opts.Length != 0) && ((p = opts.IndexOf('<')) != -1)) opts = opts.Substring(p);
                    if (opt.StartsWith("skip=", StringComparison.OrdinalIgnoreCase))
                    {
                        opt = opt.Substring("skip=".Length);
                        Int32 n = ParseNum(opt, 0);
                        if (n >= data.Length)
                        {
                            data = new Byte[0];
                            s = String.Format("{0} [Skip={1}]", s, FormatNum(n));
                        }
                        else if (n > 0)
                        {
                            Byte[] old = data;
                            data = new Byte[old.Length - n];
                            for (Int32 i = 0; i < data.Length; i++) data[i] = old[n++];
                            s = String.Format("{0} [Skip={1}]", s, FormatNum(n));
                        }
                    }
                    else if (opt.StartsWith("pad=", StringComparison.OrdinalIgnoreCase))
                    {
                        Int32 n = ParseNum(opt.Substring("pad=".Length), 0);
                        if (n > 0)
                        {
                            Byte[] old = data;
                            data = new Byte[old.Length + n];
                            for (Int32 i = 0; i < old.Length; i++) data[i] = old[i];
                            s = String.Format("{0} [{1}{2}]", s, (n >= 0)? "+" : null, FormatNum(n));
                        }
                    }
                }
            }

            if (data.Length == 0)
            {
                Console.Error.WriteLine("Empty File: {0}", s);
                return null;
            }

            // check to see if file content needs to be pre-processed
            if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                // gzip compressed data
                data = DecompressGZip(data);
                path = path.Substring(0, path.Length - 3);
            }
            if ((path.EndsWith(".imd", StringComparison.OrdinalIgnoreCase)) && (IndexOf(Encoding.ASCII, "IMD ", data, 0, 4) == 0))
            {
                // ImageDisk .IMD image file
                CHSDisk d = CHSDisk.LoadIMD(s, data);
                if (d != null) return LoadFS(s, d);
            }
            if ((path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) && ((data.Length % 2048) == 0))
            {
                if (IndexOf(Encoding.ASCII, "DECRT11A    ", data, 512, 512) == 0x3f0)
                {
                    // RT-11 .ISO image file (e.g. RT11DV10.ISO)
                    LBADisk d = new LBADisk(s, data, 512);
                    if (RT11.CheckVTOC(d, 2) == 2) return new RT11(d); // RT-11 .ISO special format fails level 3 check
                }
                // TODO: add actual ISO9660 image file support
            }
            if (path.EndsWith(".d64", StringComparison.OrdinalIgnoreCase))
            {
                CHSDisk d = CHSDisk.LoadD64(s, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
            }
            if (path.EndsWith(".d67", StringComparison.OrdinalIgnoreCase))
            {
                CHSDisk d = CHSDisk.LoadD64(s, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
            }
            if (path.EndsWith(".d80", StringComparison.OrdinalIgnoreCase))
            {
                CHSDisk d = CHSDisk.LoadD80(s, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
            }
            if (path.EndsWith(".d82", StringComparison.OrdinalIgnoreCase))
            {
                CHSDisk d = CHSDisk.LoadD82(s, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
            }

            // attempt to identify the disk type based on the image file data
            return LoadFS(s, data);
        }

        static FileSystem LoadFS(String source, Byte[] data)
        {
            if (data.Length == 174848) // 35 tracks, 683 blocks (Commodore 1541/4040)
            {
                CHSDisk d = CHSDisk.LoadD64(source, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
                return LoadFS(source, d);
            }
            if (data.Length == 175531) // 35 tracks, 683 blocks, with error bytes (Commodore 1541)
            {
                CHSDisk d = CHSDisk.LoadD64(source, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
                return LoadFS(source, d);
            }
            if (data.Length == 176640) // 35 tracks, 690 blocks (Commodore 2040)
            {
                CHSDisk d = CHSDisk.LoadD64(source, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
                return LoadFS(source, d);
            }
            if (data.Length == 196608) // 40 tracks, 768 blocks (Commodore 1541)
            {
                CHSDisk d = CHSDisk.LoadD64(source, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
                return LoadFS(source, d);
            }
            if (data.Length == 197376) // 40 tracks, 768 blocks, with error bytes (Commodore 1541)
            {
                CHSDisk d = CHSDisk.LoadD64(source, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
                return LoadFS(source, d);
            }
            if (data.Length == 205312) // 42 tracks, 802 blocks (Commodore 1541)
            {
                CHSDisk d = CHSDisk.LoadD64(source, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
                return LoadFS(source, d);
            }
            if (data.Length == 206114) // 42 tracks, 802 blocks, with error bytes (Commodore 1541)
            {
                CHSDisk d = CHSDisk.LoadD64(source, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
                return LoadFS(source, d);
            }
            if (data.Length == 252928) // 76 tracks of 26 128-byte sectors (DEC RX01)
            {
                CHSDisk d = new CHSDisk(source, data, 128, 76, 1, 26);
                FileSystem fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 256256) // 77 tracks of 26 128-byte sectors (IBM 3740)
            {
                CHSDisk d = new CHSDisk(source, data, 128, 77, 1, 26);
                FileSystem fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 266240) // 80 tracks of 26 128-byte sectors (raw 8" SSSD diskette)
            {
                CHSDisk d = new CHSDisk(source, data, 128, 80, 1, 26);
                FileSystem fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 295936) // 578 512-byte blocks (DEC TU56 DECTape standard format)
            {
                LBADisk d = new LBADisk(source, data, 512);
                FileSystem fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 409600) // 80 tracks of 10 512-byte sectors (DEC RX50)
            {
                CHSDisk d = new CHSDisk(source, data, 512, 80, 1, 10);
                FileSystem fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 505856) // 76 tracks of 26 256-byte sectors (DEC RX02)
            {
                CHSDisk d = new CHSDisk(source, data, 256, 76, 1, 26);
                FileSystem fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 512512) // 77 tracks of 26 256-byte sectors (IBM 3740)
            {
                CHSDisk d = new CHSDisk(source, data, 256, 77, 1, 26);
                FileSystem fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 532480) // 80 tracks of 26 256-byte sectors (raw 8" SSDD diskette)
            {
                CHSDisk d = new CHSDisk(source, data, 256, 80, 1, 26);
                FileSystem fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 533248) // 77 tracks, 2083 blocks (Commodore 8050)
            {
                CHSDisk d = CHSDisk.LoadD80(source, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
                return LoadFS(source, d);
            }
            else if (data.Length == 1066496) // 154 tracks, 4166 blocks (Commodore 8250)
            {
                CHSDisk d = CHSDisk.LoadD82(source, data);
                if (d != null)
                {
                    FileSystem fs = CBMDOS.Try(d);
                    if (fs != null) return fs;
                }
                return LoadFS(source, d);
            }
            else if (data.Length == 1228800) // 4800 256-byte sectors (DEC RK02, 3 spare tracks)
            {
                CHSDisk d = new CHSDisk(source, data, 256, 200, 2, 12);
                FileSystem fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 1247232) // 4872 256-byte sectors (DEC RK02, all tracks used)
            {
                CHSDisk d = new CHSDisk(source, data, 256, 203, 2, 12);
                FileSystem fs = Unix.Try(d);
                if (fs != null) return fs;
                fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 2457600) // 4800 512-byte sectors (DEC RK03, 3 spare tracks)
            {
                CHSDisk d = new CHSDisk(source, data, 512, 200, 2, 12);
                FileSystem fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length == 2494464) // 4872 512-byte sectors (DEC RK03, all tracks used)
            {
                CHSDisk d = new CHSDisk(source, data, 512, 203, 2, 12);
                FileSystem fs = Unix.Try(d);
                if (fs != null) return fs;
                fs = TryDEC(d);
                if (fs != null) return fs;
                return LoadFS(source, d);
            }
            else if (data.Length % 512 == 0) // some number of 512-byte blocks
            {
                LBADisk d = new LBADisk(source, data, 512);
                return LoadFS(source, d);
            }

            return null;
        }

        static FileSystem LoadFS(String source, Disk image)
        {
            FileSystem fs = Unix.Try(image);
            if (fs != null) return fs;

            fs = TryDEC(image);
            if (fs != null) return fs;

            if (image is CHSDisk) fs = CBMDOS.Try(image as CHSDisk);
            if (fs != null) return fs;

            return null;
        }

        static FileSystem TryDEC(Disk disk)
        {
            if (Program.Verbose > 1) Console.Error.WriteLine("TryDEC: {0}", disk.Source);

            // check basic disk parameters
            if ((disk is CHSDisk) && (disk.BlockSize != 512) && ((512 % disk.BlockSize) == 0))
            {
                if ((disk.BlockSize == 128) && (disk.MaxSector(0, 0) == 26) && (disk.BlockCount == 1976))
                {
                    // 76 tracks, probably an RX01 image with track 0 skipped
                    Boolean b8 = IsASCIIText(disk[0, 0, 8], 0x58, 24); // look for volume label in track 0, sector 8 (no interleave)
                    Boolean b15 = IsASCIIText(disk[0, 0, 15], 0x58, 24); // look for volume label in track 0, sector 15 (2:1 'soft' interleave)
                    FileSystem fs = null;
                    if (b8 && !b15) fs = TryDEC(new ClusteredDisk(disk, 4, 0));
                    else if (b15 && !b8) fs = TryDEC(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 4, 0));
                    if (fs == null) fs = TryDEC(new ClusteredDisk(disk, 4, 0));
                    return (fs != null) ? fs : TryDEC(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 4, 0));
                }
                if ((disk.BlockSize == 128) && (disk.MaxSector(0, 0) == 26) && ((disk.BlockCount == 2002) || (disk.BlockCount == 2080)))
                {
                    // 77 or 80 tracks, probably a full RX01 image including track 0
                    Boolean b8 = IsASCIIText(disk[1, 0, 8], 0x58, 24); // look for volume label in track 1, sector 8 (no interleave)
                    Boolean b15 = IsASCIIText(disk[1, 0, 15], 0x58, 24); // look for volume label in track 1, sector 15 (2:1 'soft' interleave)
                    FileSystem fs = null;
                    if (b8 && !b15) fs = TryDEC(new ClusteredDisk(disk, 4, 26));
                    else if (b15 && !b8) fs = TryDEC(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 26), 4, 26));
                    if (fs == null) fs = TryDEC(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 26), 4, 26));
                    return (fs != null) ? fs : TryDEC(new ClusteredDisk(disk, 4, 26));
                }
                if ((disk.BlockSize == 256) && (disk.MaxSector(0, 0) == 26) && (disk.BlockCount == 1976))
                {
                    // 76 tracs, probably an RX02 image with track 0 skipped
                    Boolean b4 = IsASCIIText(disk[0, 0, 4], 0x58, 24); // look for volume label in track 0, sector 4 (no interleave)
                    Boolean b7 = IsASCIIText(disk[0, 0, 7], 0x58, 24); // look for volume label in track 0, sector 7 (2:1 'soft' interleave)
                    FileSystem fs = null;
                    if (b4 && !b7) fs = TryDEC(new ClusteredDisk(disk, 2, 0));
                    else if (b7 && !b4) fs = TryDEC(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 2, 0));
                    if (fs == null) fs = TryDEC(new ClusteredDisk(disk, 2, 0));
                    return (fs != null) ? fs : TryDEC(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 0), 2, 0));
                }
                if ((disk.BlockSize == 256) && (disk.MaxSector(0, 0) == 26) && ((disk.BlockCount == 2002) || (disk.BlockCount == 2080)))
                {
                    // 77 or 80 tracks, probably a full RX02 image including track 0
                    Boolean b4 = IsASCIIText(disk[1, 0, 4], 0x58, 24); // look for volume label in track 1, sector 4 (no interleave)
                    Boolean b7 = IsASCIIText(disk[1, 0, 7], 0x58, 24); // look for volume label in track 1, sector 7 (2:1 'soft' interleave)
                    FileSystem fs = null;
                    if (b4 && !b7) fs = TryDEC(new ClusteredDisk(disk, 2, 26));
                    else if (b7 && !b4) fs = TryDEC(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 26), 2, 26));
                    if (fs == null) fs = TryDEC(new ClusteredDisk(new InterleavedDisk(disk as CHSDisk, 2, 0, 6, 26), 2, 26));
                    return (fs != null) ? fs : TryDEC(new ClusteredDisk(disk, 2, 26));
                }
                return TryDEC(new ClusteredDisk(disk, 512 / disk.BlockSize, 0));
            }
            else if ((disk is CHSDisk) && (disk.BlockSize == 512))
            {
                if ((disk.MaxSector(0, 0) == 10) && (disk.BlockCount == 800))
                {
                    // probably an RX50 image
                    Boolean b2 = IsASCIIText(disk[0, 0, 2], 0x1d8, 24); // look for volume label in track 0, sector 2 (no interleave)
                    Boolean b3 = IsASCIIText(disk[0, 0, 3], 0x1d8, 24); // look for volume label in track 0, sector 3 (2:1 'soft' interleave)
                    FileSystem fs = null;
                    if (b3 && !b2) fs = TryDEC(new InterleavedDisk(disk as CHSDisk, 2, 0, 2, 0));
                    if (fs != null) return fs;
                }
            }
            else if ((disk.BlockSize != 512) && ((512 % disk.BlockSize) == 0))
            {
                return TryDEC(new ClusteredDisk(disk, 512 / disk.BlockSize, 0));
            }
            else if (disk.BlockSize != 512)
            {
                if (Program.Verbose > 1) Console.Error.WriteLine("Volume block size = {0:D0} (must be 512)", disk.BlockSize);
                return null;
            }

            // check disk structure
            if (ODS1.CheckVTOC(disk, 1) == 1) return new ODS1(disk);
            Int32 size = RT11.CheckVTOC(disk, 3);
            if (size < 3) return null;
            else if (size != disk.BlockCount) return new RT11(new PaddedDisk(disk, size - disk.BlockCount));
            else return new RT11(disk);
        }

        static Byte[] DecompressGZip(Byte[] data)
        {
            GZipStream i = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            MemoryStream o = new MemoryStream();
            Byte[] buf = new Byte[4096];
            Int32 n;
            while ((n = i.Read(buf, 0, 4096)) != 0) o.Write(buf, 0, n);
            return o.ToArray();
        }

        static void ShowHelp()
        {
            Out.WriteLine("Commands:");
            Out.WriteLine("  load|mount id pathname[ <opts>] - mount file 'pathname' as volume 'id:'");
            Out.WriteLine("  save|write id pathname - export image of volume 'id:' to file 'pathname'");
            Out.WriteLine("  unload|unmount|umount id - unmount volume 'id:'");
            Out.WriteLine("  vols|volumes - show mounted volumes");
            Out.WriteLine("  dirs - show current working directory for each mounted volume");
            Out.WriteLine("  pwd - show current working directory on current volume");
            Out.WriteLine("  id: - change current volume to 'id:'");
            Out.WriteLine("  cd [id:]dir - change current directory");
            Out.WriteLine("  dir|ls [id:]pattern - show directory");
            Out.WriteLine("  dumpdir [id:]pattern - show directory with more detail");
            Out.WriteLine("  type|cat [id:]file - show file as text");
            Out.WriteLine("  dump|od [id:]file - show file as a hex dump");
            Out.WriteLine("  save|write [id:]file pathname - export image of file 'file' to file 'pathname'");
            Out.WriteLine("  verb|verbose n - set verbosity level (default 0)");
            Out.WriteLine("  help - show this text");
            Out.WriteLine("  exit|quit - exit program");
            Out.WriteLine();
            Out.WriteLine("load/mount options:");
            Out.WriteLine("  <skip=num> - skip first 'num' bytes of 'pathname'");
            Out.WriteLine("  <pad=num> - pad end of 'pathname' with 'num' zero bytes");
        }

        static Int32 ParseNum(String value, Int32 defaultValue)
        {
            Int32 m = 1;
            if (value.EndsWith("K", StringComparison.OrdinalIgnoreCase))
            {
                m = 1024;
                value = value.Substring(0, value.Length - 1);
            }
            else if (value.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
            {
                m = 1024;
                value = value.Substring(0, value.Length - 2);
            }
            else if (value.EndsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                m = 1024 * 1024;
                value = value.Substring(0, value.Length - 1);
            }
            else if (value.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
            {
                m = 1024 * 1024;
                value = value.Substring(0, value.Length - 2);
            }
            Int32 n = 0;
            if (!Int32.TryParse(value, out n)) return defaultValue;
            return n * m;
        }

        public static String FormatNum(Int32 value)
        {
            String suffix = null;
            if ((value % 1073741824) == 0)
            {
                suffix = "GB";
                value /= 1073741824;
            }
            if ((value % 1048576) == 0)
            {
                suffix = "MB";
                value /= 1048576;
            }
            else if ((value % 1024) == 0)
            {
                suffix = "KB";
                value /= 1024;
            }
            return String.Format("{0:D0}{1}", value, suffix);
        }

        public static String FormatNum(Int64 value)
        {
            String suffix = null;
            if ((value % 1073741824) == 0)
            {
                suffix = "GB";
                value /= 1073741824;
            }
            if ((value % 1048576) == 0)
            {
                suffix = "MB";
                value /= 1048576;
            }
            else if ((value % 1024) == 0)
            {
                suffix = "KB";
                value /= 1024;
            }
            return String.Format("{0:D0}{1}", value, suffix);
        }

        static Int32 IndexOf(Encoding encoding, String pattern, Byte[] buffer, Int32 offset, Int32 count)
        {
            Byte[] P = encoding.GetBytes(pattern);
            for (Int32 i = offset; i <= offset + count - P.Length; i++)
            {
                Boolean f = false;
                for (Int32 j = 0; j < P.Length; j++)
                {
                    if (buffer[i + j] != P[j])
                    {
                        f = true;
                        break;
                    }
                }
                if (!f) return i;
            }
            return -1;
        }

        static Int32 IndexOf(Encoding encoding, String pattern, Block block)
        {
            Byte[] P = encoding.GetBytes(pattern);
            for (Int32 i = 0; i <= block.Size - P.Length; i++)
            {
                Boolean f = false;
                for (Int32 j = 0; j < P.Length; j++)
                {
                    if (block[i + j] != P[j])
                    {
                        f = true;
                        break;
                    }
                }
                if (!f) return i;
            }
            return -1;
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

        [Flags]
        public enum DumpOptions : uint
        {
            Default = 0,
            None = 0,
            NoASCII = 1,
            Radix50 = 2,
            EBCDIC = 4,
            PETSCII0 = 8,
            PETSCII1 = 16,
        }

        static public void Dump(String prefix, Byte[] data, TextWriter output, DumpOptions options)
        {
            for (Int32 i = 0; i < data.Length; i += 16)
            {
                output.Write("{0}{1:X4} ", prefix, i);
                for (Int32 j = 0; j < 16; j++) output.Write(" {0}", (i + j < data.Length) ? data[i + j].ToString("x2") : "  ");
                if ((options & DumpOptions.NoASCII) != DumpOptions.NoASCII)
                {
                    output.Write("  ");
                    for (Int32 j = 0; j < 16; j++) output.Write((i + j < data.Length) ? (((data[i + j] >= 32) && (data[i + j] < 127)) ? (Char)(data[i + j]) : '.') : ' ');
                }
                if ((options & DumpOptions.Radix50) == DumpOptions.Radix50)
                {
                    output.Write("  ");
                    for (Int32 j = 0; j < 16; j += 2)
                    {
                        String s = "   ";
                        if (i + j + 1 < data.Length)
                        {
                            UInt16 n = BitConverter.ToUInt16(data, i + j);
                            if (n < 64000U) s = Radix50.Convert(n);
                        }
                        output.Write(s);
                    }
                }
                if ((options & DumpOptions.EBCDIC) == DumpOptions.EBCDIC)
                {
                    Encoding E = Encoding.GetEncoding("IBM037", EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
                    output.Write("  ");
                    for (Int32 j = 0; j < 16; j++)
                    {
                        Char c = ' ';
                        if (i + j < data.Length)
                        {
                            Char[] C = E.GetChars(data, i + j, 1);
                            if (C.Length != 0) c = C[0];
                            if (!Char.IsLetterOrDigit(c) && !Char.IsSymbol(c) && !Char.IsPunctuation(c) && (c != ' ')) c = '.';
                        }
                        output.Write(c);
                    }
                }
                if ((options & DumpOptions.PETSCII0) == DumpOptions.PETSCII0)
                {
                    Encoding E = PETSCII0.Encoding;
                    output.Write("  ");
                    for (Int32 j = 0; j < 16; j++)
                    {
                        Char c = ' ';
                        if (i + j < data.Length)
                        {
                            Char[] C = E.GetChars(data, i + j, 1);
                            if (C.Length != 0) c = C[0];
                            if (!Char.IsLetterOrDigit(c) && !Char.IsSymbol(c) && !Char.IsPunctuation(c) && (c != ' ')) c = '.';
                        }
                        output.Write(c);
                    }
                }
                if ((options & DumpOptions.PETSCII1) == DumpOptions.PETSCII1)
                {
                    Encoding E = PETSCII1.Encoding;
                    output.Write("  ");
                    for (Int32 j = 0; j < 16; j++)
                    {
                        Char c = ' ';
                        if (i + j < data.Length)
                        {
                            Char[] C = E.GetChars(data, i + j, 1);
                            if (C.Length != 0) c = C[0];
                            if (!Char.IsLetterOrDigit(c) && !Char.IsSymbol(c) && !Char.IsPunctuation(c) && (c != ' ')) c = '.';
                        }
                        output.Write(c);
                    }
                }
                output.WriteLine();
            }
        }
    }
}
