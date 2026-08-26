using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Transport
{
    public unsafe struct WriteBuffer : IDisposable
    {
        internal struct Data
        {
            public byte* buffer;
            public int length;
            public int capacity;
        }
        private Data* pData;
        private Allocator allocator;

        public bool IsCreate { get { return pData != null; } }
        public int Length { get { return pData != null ? pData->length : 0; } set { pData->length = value; } }
        public int Capacity { get { return pData != null ? pData->capacity : 0; } }
        public byte* Pointer { get { return pData != null ? pData->buffer : null; } }
        public Allocator Allocator { get { return allocator; } }
        public WriteBuffer(int size = 16, Allocator alloc = Allocator.Persistent)
        {
            pData = (Data*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<Data>(), UnsafeUtility.AlignOf<Data>(), alloc);
            pData->buffer = (byte*)UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<byte>(), alloc);
            pData->length = 0;
            pData->capacity = size;
            allocator = alloc;
        }

        public int WriteBytes(byte* src, int size, int pos = -1)
        {
            if (pos < 0)
                pos = pData->length;
            checkAvailableSpace(size, pos);
            UnsafeUtility.MemCpy(pData->buffer + pos, src, size);
            int new_length = pos + size;
            if (new_length > pData->length)
                pData->length = new_length;
            return pos;
        }

        public int Write(byte value, int pos = -1)
        {
            return WriteBytes(&value, UnsafeUtility.SizeOf<byte>(), pos);
        }
        public int Write(short value, int pos = -1)
        {
            return WriteBytes((byte*)&value, UnsafeUtility.SizeOf<short>(), pos);
        }
        public int Write(ushort value, int pos = -1)
        {
            return WriteBytes((byte*)&value, UnsafeUtility.SizeOf<ushort>(), pos);
        }
        public int Write(int value, int pos = -1)
        {
            return WriteBytes((byte*)&value, UnsafeUtility.SizeOf<int>(), pos);
        }
        public int Write(uint value, int pos = -1)
        {
            return WriteBytes((byte*)&value, UnsafeUtility.SizeOf<uint>(), pos);
        }
        public int Write(long value, int pos = -1)
        {
            return WriteBytes((byte*)&value, UnsafeUtility.SizeOf<long>(), pos);
        }
        public int Write(ulong value, int pos = -1)
        {
            return WriteBytes((byte*)&value, UnsafeUtility.SizeOf<ulong>(), pos);
        }
        public int Write(float value, int pos = -1)
        {
            return WriteBytes((byte*)&value, UnsafeUtility.SizeOf<float>(), pos);
        }
        public int Write(bool value, int pos = -1)
        {
            return WriteBytes((byte*)&value, UnsafeUtility.SizeOf<bool>(), pos);
        }
        public int Write(WriteBuffer value, int offset, int size, int pos = -1)
        {
            return WriteBytes(value.Pointer + offset, size, pos);
        }
        public int Write(byte[] value, int offset, int size, int pos = -1)
        {
            int res;
            fixed (byte* src = value)
            {
                res = WriteBytes(src + offset * UnsafeUtility.SizeOf<byte>(), size * UnsafeUtility.SizeOf<byte>(), pos);
            }
            return res;
        }
        public WriteBuffer Copy()
        {
            WriteBuffer result = new WriteBuffer(pData->capacity, allocator);
            result.WriteBytes(pData->buffer, pData->length);
            return result;
        }
        public void Clear()
        {
            UnsafeUtility.MemClear(pData->buffer, pData->capacity);
            pData->length = 0;
        }
        public void Dispose()
        {
            if (pData != null)
            {
                if (pData->buffer != null)
                    UnsafeUtility.Free(pData->buffer, allocator);
                UnsafeUtility.Free(pData, allocator);
                pData = null;
            }
        }

        public void Expand(int size)
        {
            if (size > 0)
            {
                int pos = pData->length;
                if (pData->capacity < pos + size)
                    checkAvailableSpace(size, pos);
            }
        }

        private void checkAvailableSpace(int size, int pos)
        {
            if (pData->capacity < pos + size)
            {
                int new_capacity = pData->capacity * 2;
                byte* new_buffer = (byte*)UnsafeUtility.Malloc(new_capacity, UnsafeUtility.AlignOf<byte>(), allocator);
                UnsafeUtility.MemCpy(new_buffer, pData->buffer, pData->length);
                UnsafeUtility.Free(pData->buffer, allocator);
                pData->buffer = new_buffer;
                pData->capacity = new_capacity;
            }
        }
    }

    public unsafe struct ReadBuffer : IDisposable
    {
        internal struct Data
        {
            public byte* buffer;
            public int length;
            public int capacity;
            public int context;
        }
        private Data* pData;
        private Allocator allocator;

        public bool IsCreate { get { return pData != null; } }
        public int Length { get { return pData != null ? pData->length : 0; } set { pData->length = value; } }
        public int Capacity { get { return pData != null ? pData->capacity : 0; } }
        public int Context { get { return pData != null ? pData->context : 0; } set { pData->context = value; } }
        public byte* Pointer { get { return pData != null ? pData->buffer : null; } }
        public Allocator Allocator { get { return allocator; } }
        public ReadBuffer(int size = 16, Allocator alloc = Allocator.Persistent)
        {
            pData = (Data*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<Data>(), UnsafeUtility.AlignOf<Data>(), alloc);
            pData->buffer = (byte*)UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<byte>(), alloc);
            pData->length = 0;
            pData->context = 0;
            pData->capacity = size;
            allocator = alloc;
        }
        public ReadBuffer(void* ptr, int length, int capacity, Allocator alloc = Allocator.Persistent)
        {
            pData = (Data*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<Data>(), UnsafeUtility.AlignOf<Data>(), alloc);
            pData->buffer = (byte*)ptr;
            pData->length = length;
            pData->context = 0;
            pData->capacity = capacity;
            allocator = alloc;
        }
        public void ReadBytes(byte* dst, int size, int pos = -1)
        {
            if (pos < 0)
                pos = pData->context;
            checkAvailableSpace(size, pos);
            UnsafeUtility.MemCpy(dst, pData->buffer + pos, size);
            int new_context = pos + size;
            if (new_context > pData->context)
                pData->context = new_context;
        }
        public byte ReadByte(int pos = -1)
        {
            byte result;
            ReadBytes(&result, UnsafeUtility.SizeOf<byte>(), pos);
            return result;
        }
        public short ReadShort(int pos = -1)
        {
            short result;
            ReadBytes((byte*)&result, UnsafeUtility.SizeOf<short>(), pos);
            return result;
        }
        public ushort ReadUShort(int pos = -1)
        {
            ushort result;
            ReadBytes((byte*)&result, UnsafeUtility.SizeOf<ushort>(), pos);
            return result;
        }
        public int ReadInt(int pos = -1)
        {
            int result;
            ReadBytes((byte*)&result, UnsafeUtility.SizeOf<int>(), pos);
            return result;
        }
        public uint ReadUInt(int pos = -1)
        {
            uint result;
            ReadBytes((byte*)&result, UnsafeUtility.SizeOf<uint>(), pos);
            return result;
        }
        public long ReadLong(int pos = -1)
        {
            long result;
            ReadBytes((byte*)&result, UnsafeUtility.SizeOf<long>(), pos);
            return result;
        }
        public ulong ReadULong(int pos = -1)
        {
            ulong result;
            ReadBytes((byte*)&result, UnsafeUtility.SizeOf<ulong>(), pos);
            return result;
        }
        public float ReadFloat(int pos = -1)
        {
            float result;
            ReadBytes((byte*)&result, UnsafeUtility.SizeOf<float>(), pos);
            return result;
        }
        public bool ReadBool(int pos = -1)
        {
            bool result;
            ReadBytes((byte*)&result, UnsafeUtility.SizeOf<bool>(), pos);
            return result;
        }
        public byte[] ReadArray(int size, int pos = -1)
        {
            byte[] result = new byte[size];
            fixed (byte* dst = result)
            {
                ReadBytes(dst, size * UnsafeUtility.SizeOf<byte>(), pos);
            }
            return result;
        }
        public ReadBuffer Copy()
        {
            ReadBuffer result = new ReadBuffer(pData->capacity, allocator);
            UnsafeUtility.MemCpy(result.Pointer, pData->buffer, pData->length);
            result.Length = pData->length;
            result.Context = pData->context;
            return result;
        }
        public void Clear()
        {
            UnsafeUtility.MemClear(pData->buffer, pData->capacity);
            pData->length = 0;
        }
        public void Dispose()
        {
            if (pData != null)
            {
                if (pData->buffer != null)
                    UnsafeUtility.Free(pData->buffer, allocator);
                UnsafeUtility.Free(pData, allocator);
                pData = null;
            }
        }
        private void checkAvailableSpace(int size, int pos)
        {
            if (pData->length < pos + size)
            {
                throw new MemberAccessException("ReadBuffer: out of range memory");
            }
        }
    }
}