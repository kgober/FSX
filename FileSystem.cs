// FileSystem.cs
// Copyright � 2019-2020 Kenneth Gober
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


// Future Improvements / To Do
// eliminate DumpDir (merge functionality into DumpFile)


using System;
using System.IO;
using System.Text;

namespace FSX
{
    abstract partial class FileSystem
    {
        public abstract String Source { get; }                                                  // source where file system was loaded from
        public abstract String Type { get; }                                                    // type of file system
        public abstract String Info { get; }                                                    // additional file system information
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
