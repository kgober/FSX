// Disk.cs
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


// Future Improvements / To Do
// add Disk.Info to store more detail (e.g. .IMD image descriptions)
// implement Block.ToInt32, ToUInt32
// provide a way to pad an image with leading zeros
// support disk partitioning (more efficiently than ClusteredDisk)


using System;

namespace FSX
{
    abstract class Block
    {
        public abstract Int32 Size { get; }
        public abstract Byte this[Int32 offset] { get; set; }
        public abstract void CopyTo(Byte[] targetBuffer, Int32 targetOffset);
        public abstract void CopyTo(Byte[] targetBuffer, Int32 targetOffset, Int32 blockOffset, Int32 count);
        public abstract Byte ToByte(Int32 startIndex);
        public abstract Byte ToByte(ref Int32 startIndex);
        public abstract Int16 ToInt16(Int32 startIndex);
        public abstract Int16 ToInt16(ref Int32 startIndex);
        public abstract UInt16 ToUInt16(Int32 startIndex);
        public abstract UInt16 ToUInt16(ref Int32 startIndex);
    }

    interface IVolume
    {
        String Source { get; }
        Int32 BlockSize { get; }
        Int32 BlockCount { get; }
        Block this[Int32 lbn] { get; }
    }

    abstract class Disk : IVolume
    {
        public abstract Disk BaseDisk { get; }
        public abstract String Source { get; }
        public abstract Int32 BlockSize { get; }
        public abstract Int32 BlockCount { get; }
        public abstract Int32 MinCylinder { get; }
        public abstract Int32 MaxCylinder { get; }
        public abstract Int32 MinHead { get; }
        public abstract Int32 MaxHead { get; }
        public abstract Int32 MinSector();
        public abstract Int32 MinSector(Int32 cylinder, Int32 head);
        public abstract Int32 MaxSector(Int32 cylinder, Int32 head);
        public abstract Block this[Int32 lbn] { get; }
        public abstract Block this[Int32 cylinder, Int32 head, Int32 sector] { get; }
    }

    class Sector : Block
    {
        private Byte[] mData;
        private Int32 mID;
        private Int32 mErr;

        public Sector(Int32 id, Byte[] data, Int32 index, Int32 count)
        {
            mID = id;
            mData = new Byte[count];
            for (Int32 i = 0; i < count; i++) mData[i] = data[index++];
        }

        public Sector(Int32 id, Int32 size)
        {
            mID = id;
            mData = new Byte[size];
        }

        public Sector(Int32 id, Int32 size, Byte value)
        {
            mID = id;
            mData = new Byte[size];
            for (Int32 i = 0; i < size; i++) mData[i] = value;
        }

        public Int32 ID
        {
            get { return mID; }
        }

        public Int32 ErrorCode
        {
            get { return mErr; }
            set { mErr = value; }
        }

        public override Int32 Size
        {
            get { return mData.Length; }
        }

        public override Byte this[Int32 offset]
        {
            get { return mData[offset]; }
            set { mData[offset] = value; }
        }

        public override void CopyTo(Byte[] targetBuffer, Int32 targetOffset)
        {
            CopyTo(targetBuffer, targetOffset, 0, mData.Length);
        }

        public override void CopyTo(Byte[] targetBuffer, Int32 targetOffset, Int32 blockOffset, Int32 count)
        {
            for (Int32 i = 0; i < count; i++) targetBuffer[targetOffset++] = mData[blockOffset++];
        }

        public override Byte ToByte(Int32 startIndex)
        {
            return mData[startIndex];
        }

        public override Byte ToByte(ref Int32 startIndex)
        {
            return mData[startIndex++];
        }

        public override Int16 ToInt16(Int32 startIndex)
        {
            return BitConverter.ToInt16(mData, startIndex);
        }

        public override Int16 ToInt16(ref Int32 startIndex)
        {
            Int16 n = BitConverter.ToInt16(mData, startIndex);
            startIndex += 2;
            return n;
        }

        public override UInt16 ToUInt16(Int32 startIndex)
        {
            return BitConverter.ToUInt16(mData, startIndex);
        }

        public override UInt16 ToUInt16(ref Int32 startIndex)
        {
            UInt16 n = BitConverter.ToUInt16(mData, startIndex);
            startIndex += 2;
            return n;
        }
    }


    // An LBADisk is a simple array of N fixed-size blocks, numbered from 0 to N-1.  CHS addressing
    // is also possible; when using CHS addressing, specify C=0, H=0, and S ranging from 1 to N.

    partial class LBADisk : Disk
    {
        private String mSource;
        private Int32 mBlockSize;
        private Int32 mBlockCount;
        private Sector[] mData;

        public LBADisk(String source, Int32 sectorSize, Int32 sectorCount)
        {
            mSource = source;
            mBlockSize = sectorSize;
            mBlockCount = sectorCount;
            mData = new Sector[sectorCount];
        }

        public LBADisk(String source, Byte[] data, Int32 sectorSize)
        {
            mSource = source;
            mBlockSize = sectorSize;
            mBlockCount = data.Length / sectorSize;
            mData = new Sector[mBlockCount];
            for (Int32 i = 0, p = 0; i < mBlockCount; i++)
            {
                mData[i] = new Sector(i + 1, data, p, sectorSize);
                p += sectorSize;
            }
        }

        public override Disk BaseDisk
        {
            get { return null; }
        }
        public override String Source
        {
            get { return mSource; }
        }

        public override Int32 BlockSize
        {
            get { return mBlockSize; }
        }

        public override Int32 BlockCount
        {
            get { return mBlockCount; }
        }

        public override Int32 MinCylinder
        {
            get { return 0; }
        }

        public override Int32 MaxCylinder
        {
            get { return 0; }
        }

        public override Int32 MinHead
        {
            get { return 0; }
        }

        public override Int32 MaxHead
        {
            get { return 0; }
        }

        public override Int32 MinSector()
        {
            return 1;
        }

        public override Int32 MinSector(Int32 cylinder, Int32 head)
        {
            return 1;
        }

        public override Int32 MaxSector(Int32 cylinder, Int32 head)
        {
            return mBlockCount;
        }

        public override Block this[Int32 lbn]
        {
            get
            {
                if ((lbn < 0) || (lbn >= mBlockCount)) throw new ArgumentOutOfRangeException("lbn");
                if (mData[lbn] == null) mData[lbn] = new Sector(lbn + 1, mBlockSize);
                return mData[lbn];
            }
        }

        public override Block this[Int32 cylinder, Int32 head, Int32 sector]
        {
            get
            {
                if (cylinder != 0) throw new ArgumentOutOfRangeException("cylinder");
                if (head != 0) throw new ArgumentOutOfRangeException("head");
                if ((sector < 1) || (sector > mBlockCount)) throw new ArgumentOutOfRangeException("sector");
                return mData[sector - 1];
            }
        }
    }


    // A CHSDisk is a track-oriented disk, with each track addressed by a Cylinder and Head number.
    // The number of Cylinders and Heads is fixed, but the number of Sectors per track may vary.
    // Default Cylinder and Head numbering starts at 0, while Sectors within a Track are numbered from 1.
    // A CHSDisk normally has a fixed sector size but can be constructed with variable length sectors.
    // Uninitialized sectors will be created lazily using the default sector size.

    partial class CHSDisk : Disk
    {
        public class Track
        {
            private Sector[] mData;
            private Int32 mMinID = -1;
            private Int32 mMaxID = -1;

            public Track(Int32 length)
            {
                mData = new Sector[length];
            }

            public Int32 Length
            {
                get { return mData.Length; }
            }

            public Int32 MinSector
            {
                get { return mMinID; }
            }

            public Int32 MaxSector
            {
                get { return mMaxID; }
            }

            public Sector this[Int32 id]
            {
                get
                {
                    for (Int32 i = 0; i < mData.Length; i++) if (mData[i].ID == id) return mData[i];
                    return null;
                }
            }

            internal Sector Get(Int32 index)
            {
                return mData[index];
            }

            internal void Set(Int32 index, Sector value)
            {
                mData[index] = value;
                Int32 id = value.ID;
                if ((mMinID == -1) || (id < mMinID)) mMinID = id;
                if (id > mMaxID) mMaxID = id;
            }
        }

        private String mSource;
        private Int32 mBlockSize;
        private Int32 mBlockCount;
        private Int32 mCyls;
        private Int32 mHeads;
        private Int32 mMinCyl;
        private Int32 mMinHead;
        private Int32 mMinSect;
        private Track[,] mData;

        public CHSDisk(String source, Byte[] data, Int32 sectorSize, Int32 numCylinders, Int32 numHeads, Int32 numSectors)
        {
            mSource = source;
            mBlockSize = sectorSize;
            mBlockCount = numCylinders * numHeads * numSectors;
            mCyls = numCylinders;
            mHeads = numHeads;
            mMinCyl = 0;
            mMinHead = 0;
            mMinSect = 1;
            mData = new Track[numCylinders, numHeads];
            Int32 p = 0;
            for (Int32 c = 0; c < numCylinders; c++)
            {
                for (Int32 h = 0; h < numHeads; h++)
                {
                    Track t = new Track(numSectors);
                    for (Int32 s = 0; s < numSectors; s++)
                    {
                        t.Set(s, new Sector(s + 1, data, p, sectorSize));
                        p += sectorSize;
                    }
                    mData[c, h] = t;
                }
            }
        }

        public CHSDisk(String source, Byte[] data, Int32 sectorSize, Int32 minCylinder, Int32 numCylinders, Int32 minHead, Int32 numHeads, Int32 minSector, Int32 numSectors)
        {
            mSource = source;
            mBlockSize = sectorSize;
            mBlockCount = numCylinders * numHeads * numSectors;
            mCyls = numCylinders;
            mHeads = numHeads;
            mMinCyl = minCylinder;
            mMinHead = minHead;
            mMinSect = minSector;
            mData = new Track[numCylinders, numHeads];
            Int32 p = 0;
            for (Int32 c = 0; c < numCylinders; c++)
            {
                for (Int32 h = 0; h < numHeads; h++)
                {
                    Track t = new Track(numSectors);
                    for (Int32 s = 0; s < numSectors; s++)
                    {
                        t.Set(s, new Sector(s + minSector, data, p, sectorSize));
                        p += sectorSize;
                    }
                    mData[c, h] = t;
                }
            }
        }

        public CHSDisk(String source, Int32 sectorSize, Int32 numCylinders, Int32 numHeads)
        {
            mSource = source;
            mBlockSize = sectorSize;
            mBlockCount = -1;
            mCyls = numCylinders;
            mHeads = numHeads;
            mMinCyl = 0;
            mMinHead = 0;
            mMinSect = 1;
            mData = new Track[numCylinders, numHeads];
        }

        public CHSDisk(String source, Int32 sectorSize, Int32 minCylinder, Int32 numCylinders, Int32 minHead, Int32 numHeads, Int32 minSector)
        {
            mSource = source;
            mBlockSize = sectorSize;
            mBlockCount = -1;
            mCyls = numCylinders;
            mHeads = numHeads;
            mMinCyl = minCylinder;
            mMinHead = minHead;
            mMinSect = minSector;
            mData = new Track[numCylinders, numHeads];
        }

        public override Disk BaseDisk
        {
            get { return null; }
        }

        public override String Source
        {
            get { return mSource; }
        }

        public override Int32 BlockSize
        {
            get { return mBlockSize; }
        }

        public override Int32 BlockCount
        {
            get
            {
                if (mBlockCount == -1)
                {
                    Int32 n = 0;
                    for (Int32 c = 0; c < mCyls; c++)
                    {
                        for (Int32 h = 0; h < mHeads; h++)
                        {
                            Track t = mData[c, h];
                            n += t.Length;
                        }
                    }
                    mBlockCount = n;
                }
                return mBlockCount;
            }
        }

        public override Int32 MinCylinder
        {
            get { return mMinCyl; }
        }

        public override Int32 MaxCylinder
        {
            get { return mMinCyl + mCyls - 1; }
        }

        public override Int32 MinHead
        {
            get { return mMinHead; }
        }

        public override Int32 MaxHead
        {
            get { return mMinHead + mHeads - 1; }
        }

        public override Int32 MinSector()
        {
            return mMinSect;
        }

        public override Int32 MinSector(Int32 cylinder, Int32 head)
        {
            return mData[cylinder - mMinCyl, head - mMinHead].MinSector;
        }

        public override Int32 MaxSector(Int32 cylinder, Int32 head)
        {
            return mData[cylinder - mMinCyl, head - mMinHead].MaxSector;
        }

        public override Block this[Int32 lbn]
        {
            get
            {
                for (Int32 c = 0; c < mCyls; c++)
                {
                    for (Int32 h = 0; h < mHeads; h++)
                    {
                        Track t = mData[c, h];
                        if (lbn < t.Length) return t[lbn + mMinSect];
                        lbn -= t.Length;
                    }
                }
                return null;
            }
        }

        public override Block this[Int32 cylinder, Int32 head, Int32 sector]
        {
            get
            {
                cylinder -= mMinCyl;
                head -= mMinHead;
                if (mData[cylinder, head][sector] == null) mData[cylinder, head].Set(sector - mMinSect, new Sector(sector, mBlockSize));
                return mData[cylinder, head][sector];
            }
        }

        public Track this[Int32 cylinder, Int32 head]
        {
            get { return mData[cylinder - mMinCyl, head - mMinHead]; }
            internal set { mData[cylinder - mMinCyl, head - mMinHead] = value; }
        }
    }


    // InterleavedDisk - implements software sector interleave, head skew and cylinder skew.
    // Note that CHSDisk can accomodate hardware interleave by itself; this class should be
    // used only when software performs 'logical' interleaving of non-interleaved physical
    // sectors, such as DEC RX01 interleave implemented by the RT-11 DX device driver.
    //
    // A perfect interleaving occurs when the number of sectors on a track (SPT) and the 
    // interleave (I) value are relatively prime (i.e. GCD(SPT,I) == 1).  When they
    // are not relatively prime (i.e. N = GCD(SPT,I), N > 1) then N cycles are needed
    // to cover all sectors, with each cycle being a perfect interleaving of SPT/N sectors.
    // The effect of this on interleaved sector numbering is that if N > 1, then every
    // SPT/N sectors there will be a +1 sector offset to shift from the previous perfectly
    // interleaved cycle to the next.

    class InterleavedDisk : Disk
    {
        private CHSDisk mDisk;
        private String mSource;
        private Int32 mInterleave;
        private Int32 mHeadSkew;
        private Int32 mCylSkew;
        private Int32 mStart; // starting sector for interleaving (last non-interleaved sector), 0-based
        private Int32 SPT; // sectors per track
        private Int32 TPC; // tracks per cylinder
        private Int32 SPIC; // sectors per (perfect) interleave cycle

        public InterleavedDisk(CHSDisk disk, Int32 interleave, Int32 headSkew, Int32 cylSkew, Int32 start)
        {
            if (interleave < 0) throw new ArgumentOutOfRangeException("interleave");
            if (interleave == 0) interleave = 1;
            mDisk = disk;
            mSource = String.Format("{0} [I={1:D0},{2:D0},{3:D0}@{4:D0}]", disk.Source, interleave, headSkew, cylSkew, start);
            mInterleave = interleave;
            mHeadSkew = headSkew;
            mCylSkew = cylSkew;
            mStart = start;
            SPT = mDisk.MaxSector(0, 0) - mDisk.MinSector(0, 0) + 1;
            TPC = mDisk.MaxHead - mDisk.MinHead + 1;
            SPIC = SPT / GCD(SPT, interleave);
        }

        public override Disk BaseDisk
        {
            get { return mDisk; }
        }

        public override String Source
        {
            get { return mSource; }
        }

        public override Int32 BlockSize
        {
            get { return mDisk.BlockSize; }
        }

        public override Int32 BlockCount
        {
            get { return mDisk.BlockCount; }
        }

        public override Int32 MinCylinder
        {
            get { return mDisk.MinCylinder; }
        }

        public override Int32 MaxCylinder
        {
            get { return mDisk.MaxCylinder; }
        }

        public override Int32 MinHead
        {
            get { return mDisk.MinHead; }
        }

        public override Int32 MaxHead
        {
            get { return mDisk.MaxHead; }
        }

        public override Int32 MinSector()
        {
            return mDisk.MinSector();
        }

        public override Int32 MinSector(Int32 cylinder, Int32 head)
        {
            return mDisk.MinSector(cylinder, head);
        }

        public override Int32 MaxSector(Int32 cylinder, Int32 head)
        {
            return mDisk.MaxSector(cylinder, head);
        }

        public override Block this[Int32 lbn]
        {
            get
            {
                if (lbn <= mStart) return mDisk[lbn];
                lbn -= mStart;
                Int32 t = lbn / SPT;
                Int32 s = lbn % SPT;
                Int32 c = t / TPC;
                Int32 h = t % TPC;
                Int32 n = s * mInterleave;
                n += s / SPIC;
                n += h * mHeadSkew;
                n += c * mCylSkew;
                n %= SPT;
                return mDisk[mStart + t * SPT + n];
            }
        }

        public override Block this[Int32 cylinder, Int32 head, Int32 sector]
        {
            get { return this[(cylinder * TPC + head) * SPT + sector - 1]; }
        }

        // simplified GCD algorithm for use when a and b are both greater than zero
        private Int32 GCD(Int32 a, Int32 b)
        {
            if (a > b) return GCD(a - b, b);
            else if (b > a) return GCD(a, b - a);
            else return a; // (a == b)
        }
    }


    // ClusteredDisk - Implements block clustering.  This effectively transforms any
    // Disk into an LBADisk with a larger Block size.

    class ClusteredDisk : Disk
    {
        public class Cluster : Block
        {
            private Block[] mData;
            private Int32 mBlockSize;

            public Cluster(Block[] blocks)
            {
                mData = blocks;
                mBlockSize = blocks[0].Size;
            }

            public override Int32 Size
            {
                get { return mData.Length * mBlockSize; }
            }

            public override Byte this[Int32 offset]
            {
                get { return mData[offset / mBlockSize][offset % mBlockSize]; }
                set { mData[offset / mBlockSize][offset % mBlockSize] = value; }
            }

            public override void CopyTo(Byte[] targetBuffer, Int32 targetOffset)
            {
                for (Int32 i = 0; i < mData.Length; i++)
                {
                    mData[i].CopyTo(targetBuffer, targetOffset);
                    targetOffset += mBlockSize;
                }
            }

            public override void CopyTo(Byte[] targetBuffer, Int32 targetOffset, Int32 blockOffset, Int32 count)
            {
                Int32 i = blockOffset / mBlockSize;
                blockOffset %= mBlockSize;
                while (count > 0)
                {
                    Int32 n = mBlockSize - blockOffset; // number of bytes available to copy in block
                    if (count < n) n = count;
                    mData[i++].CopyTo(targetBuffer, targetOffset, blockOffset, n);
                    targetOffset += n;
                    blockOffset = 0;
                    count -= n;
                }
            }

            public override Byte ToByte(Int32 startIndex)
            {
                return mData[startIndex / mBlockSize][startIndex % mBlockSize];
            }

            public override Byte ToByte(ref Int32 startIndex)
            {
                Byte n = mData[startIndex / mBlockSize][startIndex % mBlockSize];
                startIndex++;
                return n;
            }

            public override Int16 ToInt16(Int32 startIndex)
            {
                Int32 i = startIndex;
                return ToInt16(ref i);
            }

            public override Int16 ToInt16(ref Int32 startIndex)
            {
                Int32 p = startIndex / mBlockSize;
                Int32 q = startIndex % mBlockSize;
                Int16 n;
                if (q + 1 < mBlockSize)
                {
                    n = mData[p].ToInt16(q);
                }
                else
                {
                    Byte[] buf = new Byte[2];
                    buf[0] = mData[p][q];
                    buf[1] = mData[p + 1][0];
                    n = BitConverter.ToInt16(buf, 0);
                }
                startIndex += 2;
                return n;
            }

            public override UInt16 ToUInt16(Int32 startIndex)
            {
                Int32 i = startIndex;
                return ToUInt16(ref i);
            }

            public override UInt16 ToUInt16(ref Int32 startIndex)
            {
                Int32 p = startIndex / mBlockSize;
                Int32 q = startIndex % mBlockSize;
                UInt16 n;
                if (q + 1 < mBlockSize)
                {
                    n = mData[p].ToUInt16(q);
                }
                else
                {
                    Byte[] buf = new Byte[2];
                    buf[0] = mData[p][q];
                    buf[1] = mData[p + 1][0];
                    n = BitConverter.ToUInt16(buf, 0);
                }
                startIndex += 2;
                return n;
            }
        }

        private Disk mDisk;
        private String mSource;
        private Int32 mBPC;
        private Int32 mStart;
        private Int32 mBlockSize;
        private Int32 mBlockCount;
        private Cluster[] mCache;

        public ClusteredDisk(Disk disk, Int32 blocksPerCluster, Int32 startBlock)
        {
            mDisk = disk;
            mBPC = blocksPerCluster;
            mStart = startBlock;
            mBlockSize = disk.BlockSize * blocksPerCluster;
            mBlockCount = (disk.BlockCount - startBlock) / blocksPerCluster;
            mSource = String.Format("{0} [C={1:D0}x{2:D0}@{3:D0}]", disk.Source, blocksPerCluster, mBlockCount, startBlock);
            mCache = new Cluster[mBlockCount];
        }

        public ClusteredDisk(Disk disk, Int32 blocksPerCluster, Int32 startBlock, Int32 clusterCount)
        {
            mDisk = disk;
            mBPC = blocksPerCluster;
            mStart = startBlock;
            mBlockSize = disk.BlockSize * blocksPerCluster;
            mBlockCount = clusterCount;
            mSource = String.Format("{0} [C={1:D0}x{2:D0}@{3:D0}]", disk.Source, blocksPerCluster, mBlockCount, startBlock);
            mCache = new Cluster[mBlockCount];
        }

        public override Disk BaseDisk
        {
            get { return mDisk; }
        }

        public override String Source
        {
            get { return mSource; }
        }

        public override Int32 BlockSize
        {
            get { return mBlockSize; }
        }

        public override Int32 BlockCount
        {
            get { return mBlockCount; }
        }

        public override Int32 MinCylinder
        {
            get { return 0; }
        }

        public override Int32 MaxCylinder
        {
            get { return 0; }
        }

        public override Int32 MinHead
        {
            get { return 0; }
        }

        public override Int32 MaxHead
        {
            get { return 0; }
        }

        public override Int32 MinSector()
        {
            return 1;
        }

        public override Int32 MinSector(Int32 cylinder, Int32 head)
        {
            return 1;
        }

        public override Int32 MaxSector(Int32 cylinder, Int32 head)
        {
            return mBlockCount;
        }

        public override Block this[Int32 lbn]
        {
            get
            {
                if ((lbn < 0) || (lbn >= mBlockCount)) throw new ArgumentOutOfRangeException("lbn");
                if (mCache[lbn] == null)
                {
                    Block[] B = new Block[mBPC];
                    Int32 p = lbn * mBPC + mStart;
                    for (Int32 i = 0; i < mBPC; i++) B[i] = mDisk[p + i];
                    mCache[lbn] = new Cluster(B);
                }
                return mCache[lbn];
            }
        }

        public override Block this[Int32 cylinder, Int32 head, Int32 sector]
        {
            get
            {
                if (cylinder != 0) throw new ArgumentOutOfRangeException("cylinder");
                if (head != 0) throw new ArgumentOutOfRangeException("head");
                if ((sector < 1) || (sector > mBlockCount)) throw new ArgumentOutOfRangeException("sector");
                return this[sector - 1];
            }
        }
    }


    // PaddedDisk - Pads a disk with zeroed blocks to increase its size.  This effectively
    // transforms any Disk into an LBADisk with a larger Block count.  This class can also
    // be used to truncate a disk, by specifying a negative padding amount.

    class PaddedDisk : Disk
    {
        private Disk mDisk;
        private String mSource;
        private Int32 mBlockCount;
        private Block[] mCache;
        
        public PaddedDisk(Disk disk, Int32 padBlocks)
        {
            mDisk = disk;
            Int32 padBytes = padBlocks * disk.BlockSize;
            mSource = String.Format("{0} [{1}{2}]", disk.Source, (padBytes >= 0) ? "+" : null, Program.FormatNum(padBytes));
            mBlockCount = disk.BlockCount + padBlocks;
            if (padBlocks > 0) mCache = new Block[padBlocks];
        }

        public override Disk BaseDisk
        {
            get { return mDisk; }
        }

        public override String Source
        {
            get { return mSource; }
        }

        public override Int32 BlockSize
        {
            get { return mDisk.BlockSize; }
        }

        public override Int32 BlockCount
        {
            get { return mBlockCount; }
        }

        public override Int32 MinCylinder
        {
            get { return 0; }
        }

        public override Int32 MaxCylinder
        {
            get { return 0; }
        }

        public override Int32 MinHead
        {
            get { return 0; }
        }

        public override Int32 MaxHead
        {
            get { return 0; }
        }

        public override Int32 MinSector()
        {
            return 1;
        }

        public override Int32 MinSector(Int32 cylinder, Int32 head)
        {
            return 1;
        }

        public override Int32 MaxSector(Int32 cylinder, Int32 head)
        {
            return mBlockCount;
        }

        public override Block this[Int32 lbn]
        {
            get
            {
                if ((lbn < 0) || (lbn >= mBlockCount)) throw new ArgumentOutOfRangeException("lbn");
                if (lbn < mDisk.BlockCount) return mDisk[lbn];
                Int32 i = lbn - mDisk.BlockCount;
                if (mCache[i] == null) mCache[i] = new Sector(lbn + 1, mDisk.BlockSize);
                return mCache[i];
            }
        }

        public override Block this[Int32 cylinder, Int32 head, Int32 sector]
        {
            get
            {
                if (cylinder != 0) throw new ArgumentOutOfRangeException("cylinder");
                if (head != 0) throw new ArgumentOutOfRangeException("head");
                if ((sector < 1) || (sector > mBlockCount)) throw new ArgumentOutOfRangeException("sector");
                return this[sector - 1];
            }
        }
    }
}
