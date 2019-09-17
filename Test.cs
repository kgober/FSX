// Test.cs
// Copyright � 2019 Kenneth Gober
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


// To facilitate accessing disks whose file system type is unknown, each FileSystem
// may provide a test method that can be used to check for the presence of on-disk
// data structures in increasing levels of detail, until the file system type and size
// can be reliably inferred.  Each FileSystem that supports this should implement the
// IFileSystemGetTest interface, with the implementing class containing a public static
// GetTest() method that returns a 'TestDelegate'.  When the program needs to identify
// a file system type, it will invoke TestDelegate as needed:
// Boolean TestDelegate(Disk disk, Int32 level, out Int32 size, out Type type);
//
// To enable comparison of Test results, 'level' should be defined as follows:
//  0 - check basic disk parameters (return required block size and disk type)
//  1 - check boot block (return disk size and type)
//  2 - check volume descriptor (aka home/super block) (return volume size and type)
//  3 - check file headers (aka inodes) (return volume size and type)
//  4 - check directory structure (return volume size and type)
//  5 - check file header allocation (return volume size and type)
//  6 - check data block allocation (return volume size and type)
//
// Each test method should return true if the requirements for the given level (and
// all lower levels) are met by the disk, or false otherwise.  If a test method does
// not implement a given level (but it does implement higher ones) it should return
// true.  Returning true is an invitation to be called again with a higher level;
// returning false indicates that higher levels are unlikely to be useful/possible.
//
// For level 0, each test method also specifies via 'out' parameters the block size
// and disk type required (e.g., Commodore disks must have 256-byte blocks and be
// track/sector addressable).  For level 1, each test method specifies the disk size
// and disk type required, and for levels 2 and higher they specify the volume size
// and volume type that would be most suitable.
//
// For level 1, a size of -1 means that no specific disk size can be determined,
// which is not uncommon for images of disks whose sizes were fixed by the hardware,
// or stored on the disk controller rather than on the disk itself.  For levels 2
// and higher a size of -1 means the size can't be determined without examining the
// disk image at a higher level (e.g., the RT-11 home block doesn't include the
// volume size, so a level 3 check is required).  


using System;
using System.Collections.Generic;
using System.Reflection;

namespace FSX
{
    interface IFileSystemGetTest
    {
    }

    partial class FileSystem
    {
        public delegate Boolean TestDelegate(Disk disk, Int32 level, out Int32 size, out Type type);
    }

    class Test
    {
        private struct Entry
        {
            public FileSystem.TestDelegate Test;
            public Disk Disk;

            public Entry(FileSystem.TestDelegate test, Disk disk)
            {
                Test = test;
                Disk = disk;
            }
        }

        static private List<FileSystem.TestDelegate> sTests = null;

        public static void Init()
        {
            sTests = new List<FileSystem.TestDelegate>();
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type t in a.GetTypes())
                {
                    if ((typeof(IFileSystemGetTest).IsAssignableFrom(t)) && (!t.IsAbstract))
                    {
                        MethodInfo minfo = t.GetMethod("GetTest", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                        if (minfo == null) continue;
                        FileSystem.TestDelegate method = minfo.Invoke(null, null) as FileSystem.TestDelegate;
                        if (method == null) continue;
                        if (!sTests.Contains(method)) sTests.Add(method);
                    }
                }
            }
        }

        public static FileSystem Check(Disk[] images)
        {
            if (sTests == null) Init();

            // try to provide each file system test with a disk having the correct block size
            List<Entry> L = new List<Entry>();
            Int32 size = -1;
            Type type = null;
            Int32 level = 0; // entries in L have passed this level
            foreach (FileSystem.TestDelegate test in sTests)
            {
                foreach (Disk image in images)
                {
                    if (test(image, level, out size, out type))
                    {
                        Program.Debug(2, "Pass: {0} level {1:D0}", test.Method.DeclaringType.Name, level);
                        L.Add(new Entry(test, image));
                        continue;
                    }
                    if ((size != -1) && (size != image.BlockSize) && ((size % image.BlockSize) == 0))
                    {
                        Disk disk = new ClusteredDisk(image, size / image.BlockSize, 0);
                        if (test(disk, level, out size, out type))
                        {
                            Program.Debug(2, "Pass: {0} level {1:D0} (with ClusteredDisk)", test.Method.DeclaringType.Name, level);
                            L.Add(new Entry(test, disk));
                            continue;
                        }
                    }
                }
            }

            // if there were any candidates that passed level 0, continue to try them
            if (L.Count != 0)
            {
                while (true)
                {
                    level++;
                    Disk disk = null;
                    List<Entry> L2 = new List<Entry>();
                    foreach (Entry e in L)
                    {
                        Int32 s;
                        Type t;
                        if (e.Test(e.Disk, level, out s, out t))
                        {
                            Program.Debug(2, "Pass: {0} level {1:D0}", e.Test.Method.DeclaringType.Name, level);
                            disk = e.Disk;
                            size = s;
                            type = t;
                            L2.Add(e);
                        }
                    }
                    if ((level > 1) && (L2.Count == 1))
                    {
                        // if only one test passed (and we got past level 1), choose that type
                        if ((size != -1) && (size != disk.BlockCount)) disk = new PaddedDisk(disk, size - disk.BlockCount);
                        return ConstructFS(type, disk);
                    }
                    else if (L2.Count == 0)
                    {
                        // if no test passed this round, the result is indeterminate
                        level--; // L still has previous round's results
                        break;
                    }
                    L = L2;
                }
            }

            // TODO: if L is non-empty, see if any use can be made of the knowledge
            // that entries in L all passed at least level 'level' tests

            return null;
        }

        // call the constructor for 'type', passing in 'image'
        private static FileSystem ConstructFS(Type type, Disk image)
        {
            Type[] argTypes = new Type[1]; // constructor parameter types
            argTypes[0] = image.GetType();
            ConstructorInfo cinfo = type.GetConstructor(argTypes);
            if (cinfo == null) return null; // this fs type doesn't have a constructor accepting this disk type
            Object[] args = new Object[1]; // constructor arguments
            args[0] = image;
            return cinfo.Invoke(args) as FileSystem;
        }
    }
}
