using System;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Multithreading;

namespace Transport
{
    public unsafe struct Address: IKey<Address>, IDisposable
    {
        internal struct Data
        {
            public byte* buffer;
            public int length;
            public int capacity;
        }
        private Data* pData;
        private Allocator allocator;

        public int Length { get { return pData != null ?  pData->length : 0; } set { pData->length = value; } }
        public int Capacity { get { return pData != null ? pData->capacity : 0; } }
        public byte* Pointer { get { return pData != null ? pData->buffer : null; } }
        public Allocator Allocator { get { return allocator; } }
        public Address(int size, Allocator alloc = Allocator.Persistent)
        {
            pData = (Data*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<Data>(), UnsafeUtility.AlignOf<Data>(), alloc);
            pData->buffer = (byte*)UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<byte>(), alloc);
            pData->capacity = size;
            pData->length = 0;
            allocator = alloc;
        }
        public static bool operator ==(Address a, Address b)
        {
            return a.Equals(b);
        }
        public static bool operator !=(Address a, Address b)
        {
            return !(a == b);
        }
        public bool Equals(Address other)
        {
            if (pData == null || other.pData == null)
                throw new NullReferenceException();
            if (Length != other.Length)
                return false;
            return UnsafeUtility.MemCmp(Pointer, other.Pointer, Length) == 0;
        }
        public override bool Equals(object obj)
        {
            return obj != null && Equals((Address)obj);
        }
        public override int GetHashCode()
        {
            return GetHashCode(0);
        }
        public int GetHashCode(int key)
        {
            int hash = key;
            // TODO
            return hash;
        }
        public Address Copy()
        {
            if (pData == null)
                throw new NullReferenceException();
            Address addr_copy = new Address(pData->capacity, allocator);
            if (pData->length != 0)
            {
                UnsafeUtility.MemCpy(addr_copy.pData->buffer, pData->buffer, pData->length);
                addr_copy.pData->length = pData->length;
            }
            return addr_copy;
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
    }

    public interface IPackage<T>: IDisposable where T: unmanaged, IPackage<T>
    {
        void Initialize(int buf_size, int addr_size, Allocator alloc, PackageAllocator<T> p_alloc) {}
        int BufSize { get; }
        int AddrSize { get; }
        bool IsReleasable { get; }
        void Release();
    }


    public struct InputPackage: IPackage<InputPackage>
    {
        public Address address;
        public ReadBuffer buffer;
        private PackageAllocator<InputPackage> p_allocator;
        public int BufSize { get { return buffer.Capacity; } }
        public int AddrSize { get { return address.Capacity; } }
        public bool IsReleasable { get; set; }
        public InputPackage(Address address, ReadBuffer buffer, PackageAllocator<InputPackage> p_alloc = new PackageAllocator<InputPackage>())
        {
            this.buffer = buffer;
            this.address = address;
            p_allocator = p_alloc;
            IsReleasable = true;
        }
        public void Initialize(int buf_size, int addr_size, Allocator alloc, PackageAllocator<InputPackage> p_alloc = new PackageAllocator<InputPackage>())
        {
            this.buffer = new ReadBuffer(buf_size, alloc);
            this.address = new Address(addr_size, alloc);
            p_allocator = p_alloc;
            IsReleasable = true;
        }
        public unsafe InputPackage Copy(bool is_releasable)
        {
            InputPackage package = new InputPackage();
            if(p_allocator.IsCreate && address.Capacity <= p_allocator.Addr_size && buffer.Capacity <= p_allocator.Buf_size)
            {
                package = p_allocator.Alloc();
                if (address.Pointer != null)
                {
                    UnsafeUtility.MemCpy(package.address.Pointer, address.Pointer, address.Length);
                    package.address.Length = address.Length;
                }
                if (buffer.Pointer != null)
                {
                    UnsafeUtility.MemCpy(package.buffer.Pointer, buffer.Pointer, buffer.Length);
                    package.buffer.Length = buffer.Length;
                    package.buffer.Context = buffer.Context;
                }
                package.p_allocator = p_allocator;
            }
            else
            {
                if(address.Pointer != null)
                    package.address = address.Copy();
                if(buffer.Pointer != null)
                    package.buffer = buffer.Copy();
            }
            package.IsReleasable = is_releasable;
            return package;
        }
        public void Release()
        {
            if (!IsReleasable)
                return;
            if (p_allocator.IsCreate)
            {
                buffer.Length = 0;
                buffer.Context = 0;
                address.Length = 0;
                p_allocator.Free(this);
            }
            else
                Dispose();
        }
        public void Dispose()
        {
            address.Dispose();
            buffer.Dispose();
        }
    }

    public struct OutputPackage : IPackage<OutputPackage>
    {
        public Address address;
        public WriteBuffer buffer;
        private PackageAllocator<OutputPackage> p_allocator;
        public int BufSize { get { return buffer.Capacity; } }
        public int AddrSize { get { return address.Capacity; } }
        public bool IsReleasable { get; set; }
        public OutputPackage(Address address, WriteBuffer buffer, PackageAllocator<OutputPackage> p_alloc = new PackageAllocator<OutputPackage>())
        {
            this.buffer = buffer;
            this.address = address;
            p_allocator = p_alloc;
            IsReleasable = true;
        }
        public void Initialize(int buf_size, int addr_size, Allocator alloc, PackageAllocator<OutputPackage> p_alloc = new PackageAllocator<OutputPackage>())
        {
            this.buffer = new WriteBuffer(buf_size, alloc);
            this.address = new Address(addr_size, alloc);
            p_allocator = p_alloc;
            IsReleasable = true;
        }
        public unsafe OutputPackage Copy(bool is_releasable)
        {
            OutputPackage package = new OutputPackage();
            if (p_allocator.IsCreate && address.Capacity <= p_allocator.Addr_size && buffer.Capacity <= p_allocator.Buf_size)
            {
                package = p_allocator.Alloc();
                if (address.Pointer != null)
                {
                    UnsafeUtility.MemCpy(package.address.Pointer, address.Pointer, address.Length);
                    package.address.Length = address.Length;
                }
                if (buffer.Pointer != null)
                {
                    UnsafeUtility.MemCpy(package.buffer.Pointer, buffer.Pointer, buffer.Length);
                    package.buffer.Length = buffer.Length;
                }
                package.p_allocator = p_allocator;
            }
            else
            {
                if (address.Pointer != null)
                    package.address = address.Copy();
                if (buffer.Pointer != null)
                    package.buffer = buffer.Copy();
            }
            package.IsReleasable = is_releasable;
            return package;
        }
        public void Release()
        {
            if (!IsReleasable)
                return;
            if (p_allocator.IsCreate)
            {
                buffer.Length = 0;
                address.Length = 0;
                p_allocator.Free(this);
            }
            else
                Dispose();
        }
        public void Dispose()
        {
            address.Dispose();
            buffer.Dispose();
        }
    }


    public unsafe struct PackageAllocator<T>: IDisposable where T: unmanaged, IPackage<T>
    {
        internal struct Data
        {
            public Multithreading.ConcurrentQueue<T> queue;
            public int buf_size;
            public int addr_size;
            public int count;
            public int max_count;
        }
        private Data* pData;
        private Allocator allocator;
        
        public bool IsCreate { get { return pData != null; } }
        public int Cache { get { return pData->count; } }
        public int Addr_size { get { return pData->addr_size; } }
        public int Buf_size { get { return pData->buf_size; } }

        public PackageAllocator(int buf_size, int addr_size, int max_count, Allocator alloc = Allocator.Persistent)
        {
            pData = (Data*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<Data>(), UnsafeUtility.AlignOf<Data>(), alloc);
            pData->queue = new Multithreading.ConcurrentQueue<T>(true);
            pData->buf_size = buf_size;
            pData->addr_size = addr_size;
            pData->count = 0;
            pData->max_count = max_count;
            allocator = alloc;
        }
        public T Alloc()
        {
            T package;
            if (pData->queue.pop(out package))
                Interlocked.Decrement(ref pData->count);
            else
                package.Initialize(pData->buf_size, pData->addr_size, allocator, this);
            return package;
        }
        public void Free(T package)
        {
            if(pData->buf_size == package.BufSize && pData->addr_size == package.AddrSize)
            {
                int count_old = pData->count;
                while(count_old < pData->max_count)
                {
                    if(count_old == Interlocked.CompareExchange(ref pData->count, count_old + 1, count_old))
                    {
                        pData->queue.push(package);
                        return;
                    }
                    count_old = pData->count;
                }
            }
            package.Dispose();
        }
        public void Dispose()
        {
            if(pData!=null)
            {
                T package;
                while (pData->queue.pop(out package))
                    package.Dispose();
                pData->queue.Dispose();
                UnsafeUtility.Free(pData, allocator);
                pData = null;
            }
        }
    }

    public struct PackageKey : Multithreading.IKey<PackageKey>
    {
        public UInt32 ip;
        public UInt16 port;
        public UInt32 id;

        public static bool operator ==(PackageKey a, PackageKey b)
        {
            return a.Equals(b);
        }
        public static bool operator !=(PackageKey a, PackageKey b)
        {
            return !(a == b);
        }
        public bool Equals(PackageKey other)
        {
            return this.ip == other.ip && this.port == other.port && this.id == other.id;
        }
        public override bool Equals(object obj)
        {
            return obj != null && Equals((PackageKey)obj);
        }
        public override int GetHashCode()
        {
            return GetHashCode(0);
        }
        public int GetHashCode(int key)
        {
            int hash = (int)ip ^ key;
            hash += (hash << 15) ^ (unchecked((int)0xffff0000) | port);
            hash ^= unsigned_right_shift(hash, 10) + (int)id;
            hash += (hash << 3);
            hash ^= unsigned_right_shift(hash, 6);
            hash += (hash << 2) + (hash << 14);
            hash ^= unsigned_right_shift(hash, 16);
            return hash;
        }
        public static int unsigned_right_shift(int value, int pos)
        {
            if (pos == 0)
                return value;
            return (value >> pos) & (unchecked((int)0x7fffffff) >> pos - 1);
        }
        public override string ToString()
        {
            return string.Format("ip: {0}, port: {1}, id: {2}", ip, port, id);
        }
    }
}
