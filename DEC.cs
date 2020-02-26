// DEC.cs
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


using System;
using System.Text;

namespace FSX
{
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

        public static Boolean TryConvert(UInt16 value, ref String result)
        {
            Int32 v = value;
            if (v >= 64000U) return false;
            StringBuilder buf = new StringBuilder(3);
            buf.Append(T[v / 1600]);
            v = v % 1600;
            buf.Append(T[v / 40]);
            buf.Append(T[v % 40]);
            result = buf.ToString();
            return true;
        }
    }
}
