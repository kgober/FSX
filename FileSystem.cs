// FileSystem.cs
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


// Future Improvements / To Do
// eliminate DumpDir (merge functionality into DumpFile)


using System;
using System.IO;
using System.Text;

namespace FSX
{
    interface IFileSystemGetTest
    {
    }

    abstract class FileSystem
    {
        public delegate Boolean TestDelegate(Disk disk, Int32 level, out Int32 size, out Type type);

        public abstract Disk Disk { get; }                                                      // disk where volume is loaded from
        public abstract String Source { get; }                                                  // source where volume is loaded from
        public abstract String Type { get; }                                                    // type of file system on this volume
        public abstract String Dir { get; }                                                     // current directory on this volume
        public abstract Encoding DefaultEncoding { get; }                                       // default encoding of text data

        public abstract void ChangeDir(String dirSpec);                                         // change current directory
        public abstract void ListDir(String fileSpec, TextWriter output);                       // list directory contents
        public abstract void DumpDir(String fileSpec, TextWriter output);                       // dump directory contents
        public abstract void ListFile(String fileSpec, Encoding encoding, TextWriter output);   // list file contents
        public abstract void DumpFile(String fileSpec, TextWriter output);                      // dump file contents
        public abstract String FullName(String fileSpec);                                       // canonical name (if file exists)
        public abstract Byte[] ReadFile(String fileSpec);                                       // read a file 
        public abstract Boolean SaveFS(String fileName, String format);                         // write file system image to file
    }
}
