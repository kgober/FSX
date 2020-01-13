// Volume.cs
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


// Future Improvements / To Do
// implement Block.GetInt32, GetUInt32 (incl. pdp-endian versions)
// provide a way to pad an image with leading zeros
// support volume partitioning (more efficiently than ClusteredVolume)
// allow CHSVolume.this to lazily create/grow Tracks as needed
// add missing Sector header fields (track number, head number)


using System;

namespace FSX
{
    abstract class Block
    {
        public abstract Int32 Size { get; }
        public abstract Boolean IsDirty { get; set; }
        public abstract Byte this[Int32 offset] { get; set; }
        public abstract Int32 CopyTo(Byte[] targetBuffer, Int32 targetOffset);
        public abstract Int32 CopyTo(Byte[] targetBuffer, Int32 targetOffset, Int32 blockOffset, Int32 count);
        public abstract Int32 CopyFrom(Byte[] sourceBuffer, Int32 sourceOffset, Int32 blockOffset, Int32 count);
        public abstract Byte GetByte(Int32 offset);
        public abstract Byte GetByte(ref Int32 offset);
        public abstract Int16 GetInt16B(Int32 offset);
        public abstract Int16 GetInt16B(ref Int32 offset);
        public abstract Int16 GetInt16L(Int32 offset);
        public abstract Int16 GetInt16L(ref Int32 offset);
        public abstract UInt16 GetUInt16B(Int32 offset);
        public abstract UInt16 GetUInt16B(ref Int32 offset);
        public abstract UInt16 GetUInt16L(Int32 offset);
        public abstract UInt16 GetUInt16L(ref Int32 offset);
    }


    abstract class Volume
    {
        public abstract Volume Base { get; }
        public abstract String Source { get; }
        public abstract String Info { get; }
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


    // A Record is a basic implementation of a Block.

    class Record : Block
    {
        private Byte[] mData;
        private Boolean mDirty;

        public Record(Int32 size)
        {
            mData = new Byte[size];
        }

        public Record(Int32 size, Byte value) : this(size)
        {
            for (Int32 i = 0; i < size; i++) mData[i] = value;
        }

        public Record(Int32 size, Byte[] data, Int32 offset) : this(size)
        {
            for (Int32 i = 0; i < size; i++) mData[i] = data[offset++];
        }

        public override Int32 Size
        {
            get { return mData.Length; }
        }

        public override Boolean IsDirty
        {
            get { return mDirty; }
            set { mDirty = value; }
        }

        public override Byte this[Int32 offset]
        {
            get { return mData[offset]; }
            set { mDirty |= (mData[offset] != value); mData[offset] = value; }
        }

        public override Int32 CopyTo(Byte[] targetBuffer, Int32 targetOffset)
        {
            return Buffer.Copy(mData, 0, targetBuffer, targetOffset, mData.Length);
        }

        public override Int32 CopyTo(Byte[] targetBuffer, Int32 targetOffset, Int32 blockOffset, Int32 count)
        {
            return Buffer.Copy(mData, blockOffset, targetBuffer, targetOffset, count);
        }

        public override Int32 CopyFrom(Byte[] sourceBuffer, Int32 sourceOffset, Int32 blockOffset, Int32 count)
        {
            Int32 n = sourceBuffer.Length - sourceOffset;
            if (count > n) count = n;
            n = mData.Length - blockOffset;
            if (count > n) count = n;
            if (!mDirty)
            {
                for (Int32 i = 0; i < count; i++)
                {
                    if (mData[blockOffset + i] != sourceBuffer[sourceOffset + i])
                    {
                        mDirty = true;
                        break;
                    }
                }
            }
            return Buffer.Copy(sourceBuffer, sourceOffset, mData, blockOffset, count);
        }

        public override Byte GetByte(Int32 offset)
        {
            return Buffer.GetByte(mData, offset);
        }

        public override Byte GetByte(ref Int32 offset)
        {
            return Buffer.GetByte(mData, ref offset);
        }

        public override Int16 GetInt16B(Int32 offset)
        {
            return Buffer.GetInt16B(mData, offset);
        }

        public override Int16 GetInt16B(ref Int32 offset)
        {
            return Buffer.GetInt16B(mData, ref offset);
        }

        public override Int16 GetInt16L(Int32 offset)
        {
            return Buffer.GetInt16L(mData, offset);
        }

        public override Int16 GetInt16L(ref Int32 offset)
        {
            return Buffer.GetInt16L(mData, ref offset);
        }

        public override UInt16 GetUInt16B(Int32 offset)
        {
            return Buffer.GetUInt16B(mData, offset);
        }

        public override UInt16 GetUInt16B(ref Int32 offset)
        {
            return Buffer.GetUInt16B(mData, ref offset);
        }

        public override UInt16 GetUInt16L(Int32 offset)
        {
            return Buffer.GetUInt16L(mData, offset);
        }

        public override UInt16 GetUInt16L(ref Int32 offset)
        {
            return Buffer.GetUInt16L(mData, ref offset);
        }
    }


    // A Sector is a Record with a corresponding sector header.

    class Sector : Record
    {
        private Int32 mID;      // range: 0-2147483647
        private Int32 mErr;

        public Sector(Int32 id, Int32 size) : base(size)
        {
            if (id < 0) throw new ArgumentOutOfRangeException("id");
            mID = id;
        }

        public Sector(Int32 id, Int32 size, Byte value) : base(size, value)
        {
            if (id < 0) throw new ArgumentOutOfRangeException("id");
            mID = id;
        }

        public Sector(Int32 id, Int32 size, Byte[] data, Int32 offset) : base(size, data, offset)
        {
            if (id < 0) throw new ArgumentOutOfRangeException("id");
            mID = id;
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
    }


    // A Cluster is a group of equal-sized Blocks treated as a single larger Block.

    class Cluster : Block
    {
        private Block[] mData;
        private Int32 mBlockSize;
        private Boolean mDirty;

        public Cluster(Block[] blocks)
        {
            for (Int32 i = 1; i < blocks.Length; i++) if (blocks[0].Size != blocks[i].Size) throw new ArgumentOutOfRangeException("blocks");
            mData = blocks;
            mBlockSize = blocks[0].Size;
        }

        public override Int32 Size
        {
            get { return mData.Length * mBlockSize; }
        }

        public override Boolean IsDirty
        {
            get { return mDirty; }
            set { mDirty = value; }
        }

        public override Byte this[Int32 offset]
        {
            get { return mData[offset / mBlockSize][offset % mBlockSize]; }
            set { mDirty |= (mData[offset / mBlockSize][offset % mBlockSize] != value); mData[offset / mBlockSize][offset % mBlockSize] = value; }
        }

        public override Int32 CopyTo(Byte[] targetBuffer, Int32 targetOffset)
        {
            Int32 ct = 0;
            for (Int32 i = 0; i < mData.Length; i++)
            {
                ct += mData[i].CopyTo(targetBuffer, targetOffset);
                targetOffset += mBlockSize;
            }
            return ct;
        }

        public override Int32 CopyTo(Byte[] targetBuffer, Int32 targetOffset, Int32 blockOffset, Int32 count)
        {
            Int32 ct = 0;
            Int32 i = blockOffset / mBlockSize;
            blockOffset %= mBlockSize;
            while ((count > 0) && (i < mData.Length))
            {
                Int32 n = mBlockSize - blockOffset; // number of bytes available to copy in block
                if (n > count) n = count;
                ct += mData[i++].CopyTo(targetBuffer, targetOffset, blockOffset, n);
                targetOffset += n;
                blockOffset = 0;
                count -= n;
            }
            return ct;
        }

        public override Int32 CopyFrom(Byte[] sourceBuffer, Int32 sourceOffset, Int32 blockOffset, Int32 count)
        {
            Int32 ct = 0;
            Int32 i = blockOffset / mBlockSize;
            blockOffset %= mBlockSize;
            while ((count > 0) && (i < mData.Length))
            {
                Int32 n = mBlockSize - blockOffset; // number of bytes available to copy in block
                if (n > count) n = count;
                ct += mData[i].CopyFrom(sourceBuffer, sourceOffset, blockOffset, n);
                mDirty |= mData[i++].IsDirty;
                sourceOffset += n;
                blockOffset = 0;
                count -= n;
            }
            return ct;
        }

        public override Byte GetByte(Int32 offset)
        {
            return mData[offset / mBlockSize][offset % mBlockSize];
        }

        public override Byte GetByte(ref Int32 offset)
        {
            Byte n = mData[offset / mBlockSize][offset % mBlockSize];
            offset++;
            return n;
        }

        public override Int16 GetInt16B(Int32 offset)
        {
            Int32 i = offset;
            return GetInt16B(ref i);
        }

        public override Int16 GetInt16B(ref Int32 offset)
        {
            Int32 p = offset / mBlockSize;
            Int32 q = offset % mBlockSize;
            Int16 n;
            if (q + 1 < mBlockSize)
            {
                n = mData[p].GetInt16B(q);
            }
            else
            {
                Byte[] buf = new Byte[2];
                if (BitConverter.IsLittleEndian)
                {
                    buf[1] = mData[p][q];
                    buf[0] = mData[p + 1][0];
                }
                else
                {
                    buf[0] = mData[p][q];
                    buf[1] = mData[p + 1][0];
                }
                n = BitConverter.ToInt16(buf, 0);
            }
            offset += 2;
            return n;
        }

        public override Int16 GetInt16L(Int32 offset)
        {
            Int32 i = offset;
            return GetInt16L(ref i);
        }

        public override Int16 GetInt16L(ref Int32 offset)
        {
            Int32 p = offset / mBlockSize;
            Int32 q = offset % mBlockSize;
            Int16 n;
            if (q + 1 < mBlockSize)
            {
                n = mData[p].GetInt16L(q);
            }
            else
            {
                Byte[] buf = new Byte[2];
                if (BitConverter.IsLittleEndian)
                {
                    buf[0] = mData[p][q];
                    buf[1] = mData[p + 1][0];
                }
                else
                {
                    buf[1] = mData[p][q];
                    buf[0] = mData[p + 1][0];
                }
                n = BitConverter.ToInt16(buf, 0);
            }
            offset += 2;
            return n;
        }

        public override UInt16 GetUInt16B(Int32 offset)
        {
            Int32 i = offset;
            return GetUInt16B(ref i);
        }

        public override UInt16 GetUInt16B(ref Int32 offset)
        {
            Int32 p = offset / mBlockSize;
            Int32 q = offset % mBlockSize;
            UInt16 n;
            if (q + 1 < mBlockSize)
            {
                n = mData[p].GetUInt16B(q);
            }
            else
            {
                Byte[] buf = new Byte[2];
                if (BitConverter.IsLittleEndian)
                {
                    buf[1] = mData[p][q];
                    buf[0] = mData[p + 1][0];
                }
                else
                {
                    buf[0] = mData[p][q];
                    buf[1] = mData[p + 1][0];
                }
                n = BitConverter.ToUInt16(buf, 0);
            }
            offset += 2;
            return n;
        }

        public override UInt16 GetUInt16L(Int32 offset)
        {
            Int32 i = offset;
            return GetUInt16L(ref i);
        }

        public override UInt16 GetUInt16L(ref Int32 offset)
        {
            Int32 p = offset / mBlockSize;
            Int32 q = offset % mBlockSize;
            UInt16 n;
            if (q + 1 < mBlockSize)
            {
                n = mData[p].GetUInt16L(q);
            }
            else
            {
                Byte[] buf = new Byte[2];
                if (BitConverter.IsLittleEndian)
                {
                    buf[0] = mData[p][q];
                    buf[1] = mData[p + 1][0];
                }
                else
                {
                    buf[1] = mData[p][q];
                    buf[0] = mData[p + 1][0];
                }
                n = BitConverter.ToUInt16(buf, 0);
            }
            offset += 2;
            return n;
        }
    }


    // A Track is a sequence of Sectors.  Note that the Sector IDs need not be
    // sequential, or even unique, even though an OS may treat them that way.

    class Track
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

        public Sector this[Int32 index]
        {
            get
            {
                return mData[index];
            }
            set
            {
                mData[index] = value;
                Int32 id = value.ID;
                if ((mMinID == -1) || (id < mMinID)) mMinID = id;
                if (id > mMaxID) mMaxID = id;
            }
        }

        public Sector Sector(Int32 id)
        {
            for (Int32 i = 0; i < mData.Length; i++) if (mData[i].ID == id) return mData[i];
            return null;
        }
    }


    // An LBAVolume is a simple array of N fixed-size blocks, numbered from 0 to N-1.
    // CHS addressing is also possible using C=0, H=0, and S ranging from 1 to N.

    class LBAVolume : Volume
    {
        private String mSource;
        private String mInfo;
        private Int32 mBlockSize;
        private Int32 mBlockCount;
        private Record[] mData;

        public LBAVolume(String source, String info, Int32 blockSize, Int32 blockCount)
        {
            mSource = source;
            mInfo = info;
            mBlockSize = blockSize;
            mBlockCount = blockCount;
            mData = new Record[blockCount];
        }

        public LBAVolume(String source, String info, Byte[] data, Int32 blockSize) : this(source, info, blockSize, data.Length / blockSize)
        {
            for (Int32 i = 0, p = 0; i < mBlockCount; i++, p += blockSize) mData[i] = new Record(blockSize, data, p);
        }

        public override Volume Base
        {
            get { return null; }
        }

        public override String Source
        {
            get { return mSource; }
        }

        public override string Info
        {
            get { return mInfo; }
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
            get { return (mData[lbn] == null) ? mData[lbn] = new Record(mBlockSize) : mData[lbn]; }
        }

        public override Block this[Int32 cylinder, Int32 head, Int32 sector]
        {
            get { return this[sector - 1]; }
        }
    }


    // A CHSVolume is a track-oriented volume, with each track addressed by a Cylinder and Head number.
    // The number of Cylinders and Heads is fixed, but the number of Sectors per track may vary.
    // Default Cylinder and Head numbering starts at 0, while Sectors within a Track are numbered from 1.
    // A CHSVolume normally has a fixed sector size but can be constructed with variable length sectors.
    // Uninitialized sectors will be created lazily using the default sector size.

    class CHSVolume : Volume
    {
        private String mSource;
        private String mInfo;
        private Int32 mBlockSize;
        private Int32 mBlockCount;
        private Int32 mCyls;
        private Int32 mHeads;
        private Int32 mMinCyl;
        private Int32 mMinHead;
        private Int32 mMinSect;
        private Track[,] mData;

        public CHSVolume(String source, String info, Int32 sectorSize, Int32 minCylinder, Int32 numCylinders, Int32 minHead, Int32 numHeads, Int32 minSector)
        {
            mSource = source;
            mInfo = info;
            mBlockSize = sectorSize;
            mBlockCount = -1;
            mCyls = numCylinders;
            mHeads = numHeads;
            mMinCyl = minCylinder;
            mMinHead = minHead;
            mMinSect = minSector;
            mData = new Track[numCylinders, numHeads];
        }

        public CHSVolume(String source, String info, Int32 sectorSize, Int32 numCylinders, Int32 numHeads)
            : this(source, info, sectorSize, 0, numCylinders, 0, numHeads, 1)
        {
        }

        public CHSVolume(String source, String info, Byte[] data, Int32 sectorSize, Int32 minCylinder, Int32 numCylinders, Int32 minHead, Int32 numHeads, Int32 minSector, Int32 numSectors)
            : this(source, info, sectorSize, minCylinder, numCylinders, minHead, numHeads, minSector)
        {
            Int32 p = 0;
            for (Int32 c = 0; c < numCylinders; c++)
            {
                for (Int32 h = 0; h < numHeads; h++)
                {
                    Track t = new Track(numSectors);
                    for (Int32 s = 0; s < numSectors; s++)
                    {
                        t[s] = new Sector(s + minSector, sectorSize, data, p);
                        p += sectorSize;
                    }
                    mData[c, h] = t;
                }
            }
        }

        public CHSVolume(String source, String info, Byte[] data, Int32 sectorSize, Int32 numCylinders, Int32 numHeads, Int32 numSectors)
            : this(source, info, data, sectorSize, 0, numCylinders, 0, numHeads, 1, numSectors)
        {
        }

        public override Volume Base
        {
            get { return null; }
        }

        public override String Source
        {
            get { return mSource; }
        }

        public override string Info
        {
            get { return mInfo; }
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
                        if (lbn < t.Length) return t.Sector(lbn + mMinSect);
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
                Track t = this[cylinder, head];
                if ((t.Sector(sector) == null) && (t[sector - mMinSect] == null)) t[sector - mMinSect] = new Sector(sector, mBlockSize);
                return t.Sector(sector);
            }
        }

        public Track this[Int32 cylinder, Int32 head]
        {
            get { return mData[cylinder - mMinCyl, head - mMinHead]; }
            set { mData[cylinder - mMinCyl, head - mMinHead] = value; }
        }
    }


    // InterleavedVolume - implements software sector interleave, head skew and cylinder skew.
    // Note that CHSVolume can accomodate hardware interleave by itself; this class should be
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

    class InterleavedVolume : Volume
    {
        private CHSVolume mVol;
        private String mSource;
        private String mInfo;
        private Int32 mInterleave;
        private Int32 mHeadSkew;
        private Int32 mCylSkew;
        private Int32 mStart; // starting sector for interleaving (last non-interleaved sector), 0-based
        private Int32 SPT; // sectors per track
        private Int32 TPC; // tracks per cylinder
        private Int32 SPIC; // sectors per (perfect) interleave cycle

        public InterleavedVolume(CHSVolume volume, Int32 interleave, Int32 headSkew, Int32 cylSkew, Int32 start)
        {
            if (interleave < 0) throw new ArgumentOutOfRangeException("interleave");
            if (interleave == 0) interleave = 1;
            mVol = volume;
            mSource = String.Format("{0} [I={1:D0},{2:D0},{3:D0}@{4:D0}]", volume.Source, interleave, headSkew, cylSkew, start);
            mInfo = String.Format("{0}\nInterleave={1:D0},HeadSkew={2:D0},CylSkew={3:D0},Start={4:D0}", volume.Info, interleave, headSkew, cylSkew, start);
            mInterleave = interleave;
            mHeadSkew = headSkew;
            mCylSkew = cylSkew;
            mStart = start;
            SPT = mVol.MaxSector(0, 0) - mVol.MinSector(0, 0) + 1;
            TPC = mVol.MaxHead - mVol.MinHead + 1;
            SPIC = SPT / GCD(SPT, interleave);
        }

        public override Volume Base
        {
            get { return mVol; }
        }

        public override String Source
        {
            get { return mSource; }
        }

        public override string Info
        {
            get { return mInfo; }
        }

        public override Int32 BlockSize
        {
            get { return mVol.BlockSize; }
        }

        public override Int32 BlockCount
        {
            get { return mVol.BlockCount; }
        }

        public override Int32 MinCylinder
        {
            get { return mVol.MinCylinder; }
        }

        public override Int32 MaxCylinder
        {
            get { return mVol.MaxCylinder; }
        }

        public override Int32 MinHead
        {
            get { return mVol.MinHead; }
        }

        public override Int32 MaxHead
        {
            get { return mVol.MaxHead; }
        }

        public override Int32 MinSector()
        {
            return mVol.MinSector();
        }

        public override Int32 MinSector(Int32 cylinder, Int32 head)
        {
            return mVol.MinSector(cylinder, head);
        }

        public override Int32 MaxSector(Int32 cylinder, Int32 head)
        {
            return mVol.MaxSector(cylinder, head);
        }

        public override Block this[Int32 lbn]
        {
            get
            {
                if (lbn <= mStart) return mVol[lbn];
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
                return mVol[mStart + t * SPT + n];
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


    // ClusteredVolume - Implements block clustering.  This effectively transforms any
    // Volume into an LBAVolume with a larger Block size.

    class ClusteredVolume : Volume
    {
        private Volume mVol;
        private String mSource;
        private String mInfo;
        private Int32 mBPC;
        private Int32 mStart;
        private Int32 mBlockSize;
        private Int32 mBlockCount;
        private Cluster[] mCache;

        public ClusteredVolume(Volume volume, Int32 blocksPerCluster, Int32 startBlock, Int32 clusterCount)
        {
            mVol = volume;
            mBPC = blocksPerCluster;
            mStart = startBlock;
            mBlockSize = volume.BlockSize * blocksPerCluster;
            mBlockCount = (volume.BlockCount - startBlock) / blocksPerCluster;
            if (clusterCount < mBlockCount) mBlockCount = clusterCount;
            mSource = String.Format("{0} [C={1:D0}x{2:D0}@{3:D0}]", volume.Source, blocksPerCluster, mBlockCount, startBlock);
            mInfo = String.Format("{0}\nClusters={1:D0},ClusterSize={2:D0},Start={3:D0}", volume.Info, mBlockCount, blocksPerCluster * volume.BlockSize, startBlock);
            mCache = new Cluster[mBlockCount];
        }

        public ClusteredVolume(Volume volume, Int32 blocksPerCluster, Int32 startBlock) : this(volume, blocksPerCluster, startBlock, Int32.MaxValue)
        {
        }

        public override Volume Base
        {
            get { return mVol; }
        }

        public override String Source
        {
            get { return mSource; }
        }

        public override string Info
        {
            get { return mInfo; }
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
                if (mCache[lbn] == null)
                {
                    Block[] B = new Block[mBPC];
                    Int32 p = lbn * mBPC + mStart;
                    for (Int32 i = 0; i < mBPC; i++) B[i] = mVol[p + i];
                    mCache[lbn] = new Cluster(B);
                }
                return mCache[lbn];
            }
        }

        public override Block this[Int32 cylinder, Int32 head, Int32 sector]
        {
            get { return this[sector - 1]; }
        }
    }


    // PaddedVolume - Pads a volume with zeroed blocks to increase its size.  This effectively
    // transforms any Volume into an LBAVolume with a larger Block count.  This class can also
    // be used to truncate a volume, by specifying a negative padding amount.

    class PaddedVolume : Volume
    {
        private Volume mVol;
        private String mSource;
        private String mInfo;
        private Int32 mBlockCount;
        private Block[] mCache;
        
        public PaddedVolume(Volume volume, Int32 padBlocks)
        {
            mVol = volume;
            Int32 padBytes = padBlocks * volume.BlockSize;
            mSource = String.Format("{0} [{1}{2}]", volume.Source, (padBytes >= 0) ? "+" : null, Program.FormatNum(padBytes));
            mInfo = String.Format("{0}\nPadding={1}{2}", volume.Info, (padBytes >= 0) ? "+" : null, Program.FormatNum(padBytes));
            mBlockCount = volume.BlockCount + padBlocks;
            if (padBlocks > 0) mCache = new Block[padBlocks];
        }

        public override Volume Base
        {
            get { return mVol; }
        }

        public override String Source
        {
            get { return mSource; }
        }

        public override string Info
        {
            get { return mInfo; }
        }

        public override Int32 BlockSize
        {
            get { return mVol.BlockSize; }
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
                if (lbn < mVol.BlockCount) return mVol[lbn];
                Int32 i = lbn - mVol.BlockCount;
                if (mCache[i] == null) mCache[i] = new Sector(lbn + 1, mVol.BlockSize);
                return mCache[i];
            }
        }

        public override Block this[Int32 cylinder, Int32 head, Int32 sector]
        {
            get { return this[sector - 1]; }
        }
    }
}
