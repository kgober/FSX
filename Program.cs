// Program.cs
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


// File System Exchange (FSX) Program Structure
//
// A FileSystem is a file/directory structure and resides on a Volume (HostFS excepted).
// A Volume is a collection of Blocks and usually resides in an image file.  Some Volumes
// represent a transformation of an underlying Volume (e.g. clustering or interleaving).


// Future Improvements / To Do
// apply consistent style for "public static" and "private static"
// improve exception handling
// improve argument parsing
// improve 'force' mount of damaged or unrecognizable volumes
// allow demand-loading from non-compressed image files rather than pre-loading entire file
// add support for FAT12 volumes
// add support for FAT16 volumes
// add support for CP/M images
// add support for ISO-9660 volumes
// add support for 7-Zip files (.7z)
// add support for ZIP files


using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FSX
{
    class Program
    {
        public struct VDE // Volume Dictionary Entry
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
        static public Int32 Verbose = 1;

        static private Dictionary<String, VDE> VolMap = new Dictionary<String, VDE>(StringComparer.OrdinalIgnoreCase);

        static void Main(String[] args)
        {
            // parse command-line arguments
            Boolean run = true;
            Int32 ap = 0;
            while (ap < args.Length)
            {
                String arg = args[ap++];
                if (arg.Length == 0)
                {
                    Console.Error.WriteLine(@"Unrecognized zero-length command-line option """".");
                    run = false;
                }
                else if ((arg[0] == '-') || (arg[0] == '/'))
                {
                    String opt = arg.Substring(1);
                    if (opt.StartsWith("d", StringComparison.OrdinalIgnoreCase))
                    {
                        opt = opt.Substring(1);
                        if ((opt.Length == 0) && (ap < args.Length)) opt = args[ap++];
                        Int32 n;
                        if ((Int32.TryParse(opt, out n)) && (n >= 0) && (n <= 9)) // 0-9 limit is arbitrary
                        {
                            Debug.DebugLevel = n;
                        }
                        else
                        {
                            Console.Error.WriteLine(@"{0} requires a numeric argument 0-9, not ""{1}"".", arg.Substring(0, 2), opt);
                            run = false;
                        }
                    }
                    else if (opt.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    {
                        opt = opt.Substring(1);
                        if ((opt.Length == 0) && (ap < args.Length)) opt = args[ap++];
                        Int32 n;
                        if ((Int32.TryParse(opt, out n)) && (n >= 0) && (n <= 9)) // 0-9 limit is arbitrary
                        {
                            Verbose = n;
                        }
                        else
                        {
                            Console.Error.WriteLine(@"{0} requires a numeric argument 0-9, not ""{1}"".", arg.Substring(0, 2), opt);
                            run = false;
                        }
                    }
                    else if (opt.StartsWith("?"))
                    {
                        Console.Error.WriteLine(@"File System eXchange - a utility to access data stored in disk images");
                        Console.Error.WriteLine(@"Usage: FSX [options]");
                        Console.Error.WriteLine(@"Options:");
                        Console.Error.WriteLine(@"  -v num - set verbosity level to 'num' (0-9, default=1)");
                        Console.Error.WriteLine(@"  -d num - set debug level to 'num' (0-9, default=0)");
                        Console.Error.WriteLine(@"  -? - display this message");
                        Console.Error.WriteLine(@"At the 'FSX>' prompt, enter ""help"" for more information.");
                        run = false;
                    }
                    else
                    {
                        Console.Error.WriteLine(@"Unrecognized command-line option ""{0}"".", arg);
                        run = false;
                    }
                }
                else
                {
                    Console.Error.WriteLine(@"Unrecognized command-line option ""{0}"".", arg);
                    run = false;
                }
            }
            if (!run) return;

            // import host volumes
            MountHostVolumes();

            // command loop
            CommandLoop(Console.In);
        }

        static void MountHostVolumes()
        {
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (!d.IsReady) continue;
                String s = d.Name;
                if (s.EndsWith(@"\")) s = s.Substring(0, s.Length - 1);
                if (s.EndsWith(@":")) s = s.Substring(0, s.Length - 1);
                VDE v = new VDE(s, new HostFS(d.Name, d.DriveFormat));
                VolMap.Add(v.Key, v);
                if (Vol.Key == null) Vol = v;
                Console.Error.WriteLine("{0}: = {1} [{2}]", s, d.Name, v.FS.Type);
            }
        }

        static void CommandLoop(TextReader input)
        {
            while (true)
            {
                String cmd, arg;
                Console.Error.Write("\nFSX>");
                if (!ReadCommand(input, out cmd, out arg))
                {
                    Console.Error.WriteLine();
                    return;
                }
                else if ((cmd == "exit") || (cmd == "quit"))
                {
                    return;
                }
                else if ((cmd == "source") || (cmd == "."))
                {
                    StreamReader file = new StreamReader(arg);
                    CommandLoop(file); // TODO: prevent endless recursion due to sourcing a file already being sourced
                }
                else if (cmd == "out")
                {
                    if ((Out != Console.Out) && (Out != null)) Out.Close();
                    Out = ((arg == null) || (arg.Length == 0)) ? Console.Out : new StreamWriter(arg, true, Encoding.UTF8);
                }
                else if (cmd == "help")
                {
                    ShowHelp();
                }
                else if ((cmd == "verb") || (cmd == "verbose"))
                {
                    Int32 n;
                    if (Int32.TryParse(arg, out n)) Verbose = n;
                }
                else if ((cmd == "deb") || (cmd == "debug"))
                {
                    Int32 n;
                    if (Int32.TryParse(arg, out n)) Debug.DebugLevel = n;
                }
                else if ((cmd == "vols") || (cmd == "volumes"))
                {
                    String fmt = (Verbose == 0) ? "{0}:" : "{0}:\t{1}\t{2}";
                    foreach (VDE v in VolMap.Values)
                    {
                        Out.WriteLine(fmt, v.Key, v.FS.Type, v.FS.Source);
                    }
                }
                else if (cmd == "info")
                {
                    VDE v = ParseVol(ref arg);
                    Out.WriteLine(v.FS.Info);
                }
                else if (cmd == "test")
                {
                    // allow a volume to be tested without attempting to mount it
                }
                else if ((cmd == "load") || (cmd == "mount"))
                {
                    Int32 p = arg.IndexOf(' ');
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
                    Int32 p = arg.IndexOf(' ');
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
                    Int32 p = arg.IndexOf(':');
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
                else if (cmd != "")
                {
                    Console.Error.WriteLine("Command not recognized: {0}", cmd);
                }
            }
        }

        static Boolean ReadCommand(TextReader input, out String command, out String arg)
        {
            // read one line
            String line = input.ReadLine();
            if (line == null)
            {
                command = null;
                arg = null;
                return false;
            }
            if (input != Console.In) Console.Error.WriteLine(line); // echo if reading from file

            // remove leading white space
            Int32 p = 0;
            while ((p < line.Length) && ((line[p] == ' ') || (line[p] == '\t'))) p++;
            line = line.Substring(p);

            // separate command and arg
            // TODO: return arg array and allow arg quoting
            p = line.IndexOf(' ');
            command = (p == -1) ? line : line.Substring(0, p);
            arg = (p == -1) ? String.Empty : line.Substring(p + 1);
            return true;
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
            // TODO: check file extension (or file length) to see if this file is a candidate for demand-loading
            Byte[] data = ((vol.FS != null) && (vol.FS.FullName(path) != null)) ? vol.FS.ReadFile(path) : File.ReadAllBytes(s);
            if ((vol.FS != null) && (vol.FS.FullName(path) != null)) s = String.Concat(vol.Key, ":", vol.FS.FullName(path));
            if (data.Length == 0)
            {
                Console.Error.WriteLine("Empty File: {0}", s);
                return null;
            }

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
                            for (Int32 i = 0; i < data.Length; i++) data[i] = old[n + i];
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
                    else if (opt.StartsWith("type=", StringComparison.OrdinalIgnoreCase))
                    {
                        // this should probably be split into, e.g. <disk=CHS(77,1,26)> <fs=RT11>
                        String t = opt.Substring("type=".Length).Trim();
                        Int32 size;
                        Type type;
                        if (Auto.GetInfo(t, out size, out type))
                        {
                            if ((type == typeof(LBAVolume)) || (type == typeof(Volume)))
                            {
                                Volume volume = new LBAVolume(s, s, data, size);
                                return Auto.ConstructFS(t, volume);
                            }
                            else if (type == typeof(CHSVolume))
                            {
                                // no good way to guess what C/H/S values to use
                            }
                        }
                    }
                }
            }

            // try to identify storage format based on file extension
            if ((path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)) && (GZip.HasHeader(data)))
            {
                // gzip compressed data
                data = GZip.Decompress(data);
                path = path.Substring(0, path.Length - 3);
            }
            if ((path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) && (GZip.HasHeader(data)))
            {
                // gzip compressed tar file
                data = GZip.Decompress(data);
                path = String.Concat(path.Substring(0, path.Length - 3), "tar");
            }
            if ((path.EndsWith(".Z", StringComparison.OrdinalIgnoreCase)) && (Compress.HasHeader(data)))
            {
                // .Z compressed data
                Compress.Decompressor D = new Compress.Decompressor(data);
                if (D.GetByteCount() != -1)
                {
                    data = D.GetBytes();
                    path = path.Substring(0, path.Length - 2);
                }
            }
            if ((path.EndsWith(".taz", StringComparison.OrdinalIgnoreCase)) && (Compress.HasHeader(data)))
            {
                // .Z compressed tar file
                Compress.Decompressor D = new Compress.Decompressor(data);
                if (D.GetByteCount() != -1)
                {
                    data = D.GetBytes();
                    path = String.Concat(path.Substring(0, path.Length - 3), "tar");
                }
            }
            if ((path.EndsWith(".imd", StringComparison.OrdinalIgnoreCase)) && (ImageDisk.HasHeader(data)))
            {
                // ImageDisk .IMD image file
                CHSVolume d = ImageDisk.Load(s, data);
                if (d != null) return LoadFS(s, d);
            }
            if ((path.EndsWith(".td0", StringComparison.OrdinalIgnoreCase)) && (TeleDisk.HasHeader(data)))
            {
                // TeleDisk .TD0 image file
                CHSVolume d = TeleDisk.Load(s, data);
                if (d != null) return LoadFS(s, d);
            }
            //if ((path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) && ((data.Length % 2048) == 0))
            //{
            //    if (IndexOf(Encoding.ASCII, "DECRT11A    ", data, 512, 512) == 0x3f0)
            //    {
            //        // RT-11 .ISO image file (e.g. RT11DV10.ISO)
            //        LBAVolume d = new LBAVolume(s, data, 512);
            //        Int32 size;
            //        Type type;
            //        if (RT11.Test(d, 5, out size, out type)) return new RT11(d); // RT-11 .ISO special format fails level 6 check
            //    }
            //    // TODO: add actual ISO9660 image file support
            //}
            if ((path.EndsWith(".d64", StringComparison.OrdinalIgnoreCase)) && (D64.IsValid(data)))
            {
                // .D64 image file
                CHSVolume image = D64.Load(s, data);
                return LoadFS(s, image);
            }
            if ((path.EndsWith(".d67", StringComparison.OrdinalIgnoreCase)) && (D67.IsValid(data)))
            {
                // .D67 image file
                CHSVolume image = D67.Load(s, data);
                return LoadFS(s, image);
            }
            if ((path.EndsWith(".d80", StringComparison.OrdinalIgnoreCase)) && (D80.IsValid(data)))
            {
                // .D80 image file
                CHSVolume image = D80.Load(s, data);
                return LoadFS(s, image);
            }
            if ((path.EndsWith(".d82", StringComparison.OrdinalIgnoreCase)) && (D82.IsValid(data)))
            {
                // .D82 image file
                CHSVolume image = D82.Load(s, data);
                return LoadFS(s, image);
            }

            // try to identify storage format based on image data
            return LoadFS(s, data);
        }

        static FileSystem LoadFS(String source, Byte[] data)
        {
            // try to identify storage format based on image data content
            // TODO: look for magic numbers or file headers (incl. above in case extension was lost due to renaming)

            // try to identify storage format based on image data size
            if (data.Length == 174848) // 35 tracks, 683 blocks (Commodore 1541/4040)
            {
                CHSVolume d = D64.Load(source, data);
                if (d != null)
                {
                    Int32 size;
                    Type type;
                    if (CBMDOS.Test(d, 6, out size, out type)) return new CBMDOS(d);
                }
                return LoadFS(source, d);
            }
            if (data.Length == 175531) // 35 tracks, 683 blocks, with error bytes (Commodore 1541)
            {
                CHSVolume d = D64.Load(source, data);
                if (d != null)
                {
                    Int32 size;
                    Type type;
                    if (CBMDOS.Test(d, 6, out size, out type)) return new CBMDOS(d);
                }
                return LoadFS(source, d);
            }
            if (data.Length == 176640) // 35 tracks, 690 blocks (Commodore 2040)
            {
                CHSVolume d = D64.Load(source, data);
                if (d != null)
                {
                    Int32 size;
                    Type type;
                    if (CBMDOS.Test(d, 6, out size, out type)) return new CBMDOS(d);
                }
                return LoadFS(source, d);
            }
            if (data.Length == 196608) // 40 tracks, 768 blocks (Commodore 1541)
            {
                CHSVolume d = D64.Load(source, data);
                if (d != null)
                {
                    Int32 size;
                    Type type;
                    if (CBMDOS.Test(d, 6, out size, out type)) return new CBMDOS(d);
                }
                return LoadFS(source, d);
            }
            if (data.Length == 197376) // 40 tracks, 768 blocks, with error bytes (Commodore 1541)
            {
                CHSVolume d = D64.Load(source, data);
                if (d != null)
                {
                    Int32 size;
                    Type type;
                    if (CBMDOS.Test(d, 6, out size, out type)) return new CBMDOS(d);
                }
                return LoadFS(source, d);
            }
            if (data.Length == 205312) // 42 tracks, 802 blocks (Commodore 1541)
            {
                CHSVolume d = D64.Load(source, data);
                if (d != null)
                {
                    Int32 size;
                    Type type;
                    if (CBMDOS.Test(d, 6, out size, out type)) return new CBMDOS(d);
                }
                return LoadFS(source, d);
            }
            if (data.Length == 206114) // 42 tracks, 802 blocks, with error bytes (Commodore 1541)
            {
                CHSVolume d = D64.Load(source, data);
                if (d != null)
                {
                    Int32 size;
                    Type type;
                    if (CBMDOS.Test(d, 6, out size, out type)) return new CBMDOS(d);
                }
                return LoadFS(source, d);
            }
            if (data.Length == 252928) // 76 tracks of 26 128-byte sectors (DEC RX01)
            {
                CHSVolume d = new CHSVolume(source, source, data, 128, 76, 1, 26);
                return LoadFS(source, d);
            }
            else if (data.Length == 256256) // 77 tracks of 26 128-byte sectors (IBM 3740)
            {
                CHSVolume d = new CHSVolume(source, source, data, 128, 77, 1, 26);
                return LoadFS(source, d);
            }
            else if (data.Length == 266240) // 80 tracks of 26 128-byte sectors (raw 8" SSSD diskette)
            {
                CHSVolume d = new CHSVolume(source, source, data, 128, 80, 1, 26);
                return LoadFS(source, d);
            }
            else if (data.Length == 295936) // 578 512-byte blocks (DEC TU56 DECTape standard format)
            {
                LBAVolume d = new LBAVolume(source, source, data, 512);
                return LoadFS(source, d);
            }
            else if (data.Length == 409600) // 80 tracks of 10 512-byte sectors (DEC RX50)
            {
                CHSVolume d = new CHSVolume(source, source, data, 512, 80, 1, 10);
                return LoadFS(source, d);
            }
            else if (data.Length == 505856) // 76 tracks of 26 256-byte sectors (DEC RX02)
            {
                CHSVolume d = new CHSVolume(source, source, data, 256, 76, 1, 26);
                return LoadFS(source, d);
            }
            else if (data.Length == 512512) // 77 tracks of 26 256-byte sectors
            {
                CHSVolume d = new CHSVolume(source, source, data, 256, 77, 1, 26);
                return LoadFS(source, d);
            }
            else if (data.Length == 532480) // 80 tracks of 26 256-byte sectors (raw 8" SSDD diskette)
            {
                CHSVolume d = new CHSVolume(source, source, data, 256, 80, 1, 26);
                return LoadFS(source, d);
            }
            else if (data.Length == 533248) // 77 tracks, 2083 blocks (Commodore 8050)
            {
                CHSVolume d = D80.Load(source, data);
                if (d != null)
                {
                    Int32 size;
                    Type type;
                    if (CBMDOS.Test(d, 6, out size, out type)) return new CBMDOS(d);
                }
                return LoadFS(source, d);
            }
            else if (data.Length == 1066496) // 154 tracks, 4166 blocks (Commodore 8250)
            {
                CHSVolume d = D82.Load(source, data);
                if (d != null)
                {
                    Int32 size;
                    Type type;
                    if (CBMDOS.Test(d, 6, out size, out type)) return new CBMDOS(d);
                }
                return LoadFS(source, d);
            }
            // DEC RK02/RK03 DECpack
            //
            // DEC operating systems typically reserve the last 3 cylinders for bad block
            // handling, leaving an effective capacity of 200 cylinders, each containing
            // 2 tracks of 12 512-byte sectors, or a total of 4800 blocks.
            //
            // Some operating systems (notably Unix) use all 203 cylinders (4872 blocks)
            // and therefore require special error-free disk cartridges.
            else if (data.Length == 1228800) // 4800 256-byte sectors (DEC RK02, 3 spare tracks)
            {
                CHSVolume d = new CHSVolume(source, source, data, 256, 200, 2, 12);
                return LoadFS(source, d);
            }
            else if (data.Length == 1247232) // 4872 256-byte sectors (DEC RK02, all tracks used)
            {
                CHSVolume d = new CHSVolume(source, source, data, 256, 203, 2, 12);
                return LoadFS(source, d);
            }
            else if (data.Length == 2457600) // 4800 512-byte sectors (DEC RK03, 3 spare tracks)
            {
                CHSVolume d = new CHSVolume(source, source, data, 512, 200, 2, 12);
                return LoadFS(source, d);
            }
            else if (data.Length == 2494464) // 4872 512-byte sectors (DEC RK03, all tracks used)
            {
                CHSVolume d = new CHSVolume(source, source, data, 512, 203, 2, 12);
                return LoadFS(source, d);
            }
            else if ((data.Length % 513 == 0) && (data.Length % 512 == 0))
            {
                // these might be 512-byte blocks with 1 added error/status byte
                LBAVolume d1 = new LBAVolume(source, source, data, 512);
                Int32 n = data.Length / 513;
                LBAVolume d2 = new LBAVolume(source, source, 512, n);
                for (Int32 i = 0; i < n; i++) d2[i].CopyFrom(data, i * 513, 0, 512);
                return Auto.Check(new Volume[] { d1, d2 });
            }
            else if (data.Length % 513 == 0) // assume 512-byte blocks with error/status bytes
            {
                Int32 n = data.Length / 513;
                LBAVolume d = new LBAVolume(source, source, 512, n);
                Int32 p = 0;
                for (Int32 i = 0; i < n; i++)
                {
                    Sector S = d[i] as Sector;
                    S.CopyFrom(data, p, 0, 512);
                    p += 512;
                    S.ErrorCode = data[p++];
                }
                return LoadFS(source, d);
            }
            else if (data.Length % 512 == 0) // some number of 512-byte blocks
            {
                LBAVolume d = new LBAVolume(source, source, data, 512);
                return LoadFS(source, d);
            }

            return null;
        }

        static FileSystem LoadFS(String source, Volume image)
        {
            if (image is CHSVolume)
            {
                CHSVolume volume = image as CHSVolume;
                if ((volume.MaxCylinder < 80) && (volume.MinHead == volume.MaxHead) && (volume.MaxSector(volume.MinCylinder, volume.MinHead) == 26) && (volume.BlockSize == 128 || volume.BlockSize == 256))
                {
                    // DEC RX01 Floppy - IBM 3740 format (8" SSSD diskette, 77 tracks, 26 sectors)
                    // DEC RX02 Floppy - like RX01 format, but using MFM for sector data (not FM)
                    //
                    // DEC operating systems typically do not use track 0, so an RX01/RX02 diskette
                    // has an effective capacity of 76 tracks (2002 sectors).
                    //
                    // 'Soft' Interleave imposes a 'logical' sector order on top of the physical
                    // sector order.  This enables the performance benefits of optimal interleave
                    // using 'standard' 1:1 interleave diskettes, maintaining interoperability for
                    // data transfer with other systems, and removing the need to reformat disks.
                    // 'Soft' sector interleave is 2:1, with a 6 sector track-to-track skew.
                    Debug.WriteLine(2, "RX01/RX02 image, also testing with interleave applied");
                    Int32 n = 512 / volume.BlockSize;
                    Volume d2 = new ClusteredVolume(new InterleavedVolume(volume, 2, 0, 6, 26), n, 26); // if image includes track 0
                    Volume d3 = new ClusteredVolume(new InterleavedVolume(volume, 2, 0, 6, 0), n, 0); // if image starts at track 1
                    return Auto.Check(new Volume[] { volume, d2, d3 });
                }
                if ((volume.MaxCylinder < 80) && (volume.MinHead == volume.MaxHead) && (volume.MaxSector(volume.MinCylinder, volume.MinHead) == 10) && (volume.BlockSize == 512))
                {
                    // DEC RX50 Floppy - 5.25" SSDD diskette, 80 tracks, 10 sectors
                    // 'Soft' sector interleave is 2:1, with a 2 sector track-to-track skew.
                    Debug.WriteLine(2, "RX50 image, also testing with interleave applied");
                    Volume d2 = new InterleavedVolume(volume, 2, 0, 2, 0); // if image starts at track 1
                    Volume d3 = new ClusteredVolume(new InterleavedVolume(volume, 2, 0, 2, 10), 1, 10); // if image includes track 0
                    Volume d4 = new ClusteredVolume(new InterleavedVolume(volume, 2, 0, 3, 10), 1, 10); // if image includes track 0 (skew 3 allows VENIX RX50 floppies to be read)
                    return Auto.Check(new Volume[] { volume, d2, d3, d4 });
                }
            }
            return Auto.Check(new Volume[] { image });
        }

        static void ShowHelp()
        {
            Out.WriteLine("Commands:");
            Out.WriteLine("  load|mount id pathname[ <opts>] - mount file 'pathname' as volume 'id:'");
            Out.WriteLine("  save|write id pathname - export image of volume 'id:' to file 'pathname'");
            Out.WriteLine("  unload|unmount|umount id - unmount volume 'id:'");
            Out.WriteLine("  vols|volumes - show mounted volumes");
            Out.WriteLine("  info [id:] - show volume information");
            Out.WriteLine("  dirs - show current working directory for each mounted volume");
            Out.WriteLine("  pwd - show current working directory on current volume");
            Out.WriteLine("  id: - change current volume to 'id:'");
            Out.WriteLine("  cd [id:]dir - change current directory");
            Out.WriteLine("  dir|ls [id:]pattern - show directory");
            Out.WriteLine("  dumpdir [id:]pattern - show raw directory data");
            Out.WriteLine("  type|cat [id:]file - show file as text");
            Out.WriteLine("  dump|od [id:]file - show file as a hex dump");
            Out.WriteLine("  save|write [id:]file pathname - export image of file 'file' to file 'pathname'");
            Out.WriteLine("  out pathname - redirect output to 'pathname' (omit pathname to reset)");
            Out.WriteLine("  verb|verbose n - set verbosity level (default 0)");
            Out.WriteLine("  deb|debug n - set debug level (default 0)");
            Out.WriteLine("  source|. pathname - read commands from 'pathname'");
            Out.WriteLine("  help - show this text");
            Out.WriteLine("  exit|quit - exit program (or stop reading 'source' commands)");
            Out.WriteLine();
            Out.WriteLine("load/mount options:");
            Out.WriteLine("  <skip=num> - skip first 'num' bytes of 'pathname'");
            Out.WriteLine("  <pad=num> - pad end of 'pathname' with 'num' zero bytes");
            Out.WriteLine("  <type=name> - mount as a file system of type 'name'");
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
                suffix = "G";
                value /= 1073741824;
            }
            if ((value % 1048576) == 0)
            {
                suffix = "M";
                value /= 1048576;
            }
            else if ((value % 1024) == 0)
            {
                suffix = "K";
                value /= 1024;
            }
            return String.Format("{0:D0}{1}", value, suffix);
        }

        public static String FormatNum(Int64 value)
        {
            String suffix = null;
            if ((value % 1073741824) == 0)
            {
                suffix = "G";
                value /= 1073741824;
            }
            if ((value % 1048576) == 0)
            {
                suffix = "M";
                value /= 1048576;
            }
            else if ((value % 1024) == 0)
            {
                suffix = "K";
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

        [Flags]
        public enum DumpOptions : uint
        {
            Default = 1,
            None = 0,
            ASCII = 1,
            DOS = 2,
            ANSI = 4,
            EBCDIC = 8,
            Radix50 = 16,
            PETSCII0 = 32,
            PETSCII1 = 64,
        }

        static public void Dump(String prefix, Byte[] data, TextWriter output)
        {
            Dump(prefix, data, output, 16, 0, DumpOptions.Default);
        }

        static public void Dump(String prefix, Byte[] data, TextWriter output, Int32 bytesPerLine, Int32 bytesPerSection)
        {
            Dump(prefix, data, output, bytesPerLine, bytesPerSection, DumpOptions.Default);
        }

        static public void Dump(String prefix, Byte[] data, TextWriter output, Int32 bytesPerLine, Int32 bytesPerSection, DumpOptions options)
        {
            Int32 bPHL = ((bytesPerLine % 2) == 0) ? bytesPerLine / 2 : -1; // bytesPerHalfLine
            Char HL = (bPHL == -1) ? ' ' : ':'; // Half Line marker
            Int32 bPQL = ((bPHL % 2) == 0) ? bPHL / 2 : -1; // bytesPerQuarterLine
            Char QL = (bPQL == -1) ? ' ' : '.'; // Quarter Line marker
            String fmt = String.Format("{0:x0}", data.Length - 1);
            fmt = String.Concat("{0}{1:X", fmt.Length.ToString("D0"), "} ");
            Int32 n = bytesPerSection;
            for (Int32 i = 0; i < data.Length; )
            {
                Int32 l = (n < bytesPerLine) ? n : bytesPerLine;
                output.Write(fmt, prefix, i);
                for (Int32 j = 0; j < bytesPerLine; j++) output.Write("{0}{1}", (j == bPHL) ? HL : ((j % bPHL) == bPQL) ? QL : ' ', ((i + j < data.Length) && (j < l)) ? data[i + j].ToString("x2") : "  ");
                if ((options & DumpOptions.ASCII) == DumpOptions.ASCII)
                {
                    output.Write("  ");
                    for (Int32 j = 0; j < bytesPerLine; j++)
                    {
                        Char c = ' ';
                        if ((i + j < data.Length) && (j < l))
                        {
                            Byte b = data[i + j];
                            c = ((b >= 32) && (b < 127)) ? (char)b : '.';
                        }
                        output.Write(c);
                    }
                }
                if ((options & DumpOptions.DOS) == DumpOptions.DOS)
                {
                    Encoding E = Encoding.GetEncoding(437, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
                    output.Write("  ");
                    for (Int32 j = 0; j < bytesPerLine; j++)
                    {
                        Char c = ' ';
                        if ((i + j < data.Length) && (j < l))
                        {
                            Char[] C = E.GetChars(data, i + j, 1);
                            if (C.Length != 0) c = C[0];
                            if (!Char.IsLetterOrDigit(c) && !Char.IsSymbol(c) && !Char.IsPunctuation(c) && (c != ' ')) c = '.';
                        }
                        output.Write(c);
                    }
                }
                if ((options & DumpOptions.ANSI) == DumpOptions.ANSI)
                {
                    Encoding E = Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
                    output.Write("  ");
                    for (Int32 j = 0; j < bytesPerLine; j++)
                    {
                        Char c = ' ';
                        if ((i + j < data.Length) && (j < l))
                        {
                            Char[] C = E.GetChars(data, i + j, 1);
                            if (C.Length != 0) c = C[0];
                            if (!Char.IsLetterOrDigit(c) && !Char.IsSymbol(c) && !Char.IsPunctuation(c) && (c != ' ')) c = '.';
                        }
                        output.Write(c);
                    }
                }
                if ((options & DumpOptions.EBCDIC) == DumpOptions.EBCDIC)
                {
                    Encoding E = Encoding.GetEncoding(37, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
                    output.Write("  ");
                    for (Int32 j = 0; j < bytesPerLine; j++)
                    {
                        Char c = ' ';
                        if ((i + j < data.Length) && (j < l))
                        {
                            Char[] C = E.GetChars(data, i + j, 1);
                            if (C.Length != 0) c = C[0];
                            if (!Char.IsLetterOrDigit(c) && !Char.IsSymbol(c) && !Char.IsPunctuation(c) && (c != ' ')) c = '.';
                        }
                        output.Write(c);
                    }
                }
                if ((options & DumpOptions.Radix50) == DumpOptions.Radix50)
                {
                    output.Write("  ");
                    for (Int32 j = 0; j < bytesPerLine; j += 2)
                    {
                        String s = "   ";
                        if ((i + j + 1 < data.Length) && (j + 1 < l))
                        {
                            UInt16 w = Buffer.GetUInt16L(data, i + j);
                            Radix50.TryConvert(w, ref s);
                        }
                        output.Write(s);
                    }
                }
                if ((options & DumpOptions.PETSCII0) == DumpOptions.PETSCII0)
                {
                    Encoding E = PETSCII0.Encoding;
                    output.Write("  ");
                    for (Int32 j = 0; j < bytesPerLine; j++)
                    {
                        Char c = ' ';
                        if ((i + j < data.Length) && (j < l))
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
                    for (Int32 j = 0; j < bytesPerLine; j++)
                    {
                        Char c = ' ';
                        if ((i + j < data.Length) && (j < l))
                        {
                            Char[] C = E.GetChars(data, i + j, 1);
                            if (C.Length != 0) c = C[0];
                            if (!Char.IsLetterOrDigit(c) && !Char.IsSymbol(c) && !Char.IsPunctuation(c) && (c != ' ')) c = '.';
                        }
                        output.Write(c);
                    }
                }
                output.WriteLine();
                i += l;
                n -= l;
                if (n == 0)
                {
                    output.WriteLine();
                    n = bytesPerSection;
                }
            }
        }
    }
}
