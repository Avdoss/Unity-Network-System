using System;
using System.Threading;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Multithreading;

namespace Transport
{
    public unsafe struct SegmentationLayer : ILayer
    {
        private static readonly int INT_SIZE = UnsafeUtility.SizeOf<int>();
        private static readonly int INT_BITS = INT_SIZE * 8;
        private static readonly int LENGTH_SIZE = UnsafeUtility.SizeOf<uint>();
        private static readonly int ID_SIZE = UnsafeUtility.SizeOf<uint>();
        private static readonly int SEGMENT_SIZE = UnsafeUtility.SizeOf<ushort>();
        private static readonly int HEAD_SIZE = LENGTH_SIZE + ID_SIZE + SEGMENT_SIZE;
        private static readonly float LIFETIME_DEFAULT = 5.0f;

        internal struct TimeKey
        {
            public PackageKey key;
            public float timestamp;
        }

        internal struct BigPackage
        {
            public IntPtr pBuf;
            public IntPtr pLocks;
            public uint length;
            public uint bytes_received;
            public BigPackage(uint length)
            {
                this.pBuf = IntPtr.Zero;
                this.pLocks = IntPtr.Zero;
                this.length = length;
                this.bytes_received = 0;
            }
        }

        internal struct BigPackageFinalizer: IFinalizer<PackageKey, BigPackage>
        {
            public void Release(ref PackageKey key, ref BigPackage value)
            {
                if (value.pBuf != IntPtr.Zero && value.bytes_received != value.length)
                {
                    UnsafeUtility.Free(value.pBuf.ToPointer(), Allocator.Persistent);
                    value.pBuf = IntPtr.Zero;
                }
                if (value.pLocks != IntPtr.Zero)
                {
                    UnsafeUtility.Free(value.pLocks.ToPointer(), Allocator.Persistent);
                    value.pLocks = IntPtr.Zero;
                }
            }
        }

        internal struct LifetimeCondition : ConcurrentQueue<TimeKey>.ICondition
        {
            public float current_time;
            public float lifetime;
            public bool execute(ref TimeKey value)
            {
                return current_time - value.timestamp >= lifetime;
            }
        }

        internal struct Task : ConcurrentDictionary<PackageKey, BigPackage, BigPackageFinalizer>.ITask
        {
            public bool is_first;
            public bool is_last;
            public InputPackage package;
            public int data_offset;
            public int max_data_size;
            public Task(InputPackage package, int data_offset, int max_data_size)
            {
                is_first = true;
                is_last = false;
                this.package = package;
                this.data_offset = data_offset;
                this.max_data_size = max_data_size;
            }
            public void execute(ref BigPackage current_value, ref BigPackage new_value)
            {
                is_first = false;
                if(current_value.bytes_received < current_value.length)
                {
                    if(current_value.pBuf == IntPtr.Zero)
                        TryMallocArray(ref current_value.pBuf, current_value.length);
                    if (current_value.pLocks == IntPtr.Zero)
                        TryMallocArray(ref current_value.pLocks, ((current_value.length - data_offset + max_data_size * INT_BITS - 1) / (max_data_size * INT_BITS)) * INT_SIZE);
                    ushort offset = package.buffer.ReadUShort();
                    int index = offset / INT_BITS;
                    int mask = 1 << (offset % INT_BITS);
                    ref int location = ref UnsafeUtility.ArrayElementAsRef<int>(current_value.pLocks.ToPointer(), index);
                    int tmp_value = location;
                    int old_value;
                    do
                    {
                        if ((tmp_value & mask) != 0)
                            return;
                        old_value = tmp_value;
                        tmp_value |= mask;
                    }
                    while ((tmp_value=Interlocked.CompareExchange(ref location, tmp_value, old_value)) != old_value);
                    byte* src = package.buffer.Pointer;
                    byte* dst = (byte*)current_value.pBuf.ToPointer();
                    int size = package.buffer.Length;
                    if (offset != 0)
                    {
                        src += data_offset;
                        dst += data_offset + max_data_size * offset;
                        size -= data_offset;
                    }
                    UnsafeUtility.MemCpy(dst, src, size);
                    if (Interlocked.Add(ref UnsafeUtility.As<uint, int>(ref current_value.bytes_received), size) == current_value.length)
                        is_last = true;
                }
            }
            private static void TryMallocArray(ref IntPtr ptr, long size)
            {
                IntPtr new_ptr = new IntPtr(UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<byte>(), Allocator.Persistent));
                UnsafeUtility.MemClear(new_ptr.ToPointer(), size);
                if (Interlocked.CompareExchange(ref ptr, new_ptr, IntPtr.Zero) != IntPtr.Zero)
                    UnsafeUtility.Free(new_ptr.ToPointer(), Allocator.Persistent);
            }
        }

        private int LENGTH_OFFSET;
        private int ID_OFFSET;
        private int SEGMENT_OFFSET;
        private int DATA_OFFSET;
        private int MAX_DATA_SIZE;
        private uint identity_counter;
        private LifetimeCondition lifetimeCondition;
        private ConcurrentDictionary<PackageKey, BigPackage, BigPackageFinalizer> packages;
        private ConcurrentQueue<TimeKey> time_keys;
        private bool IsCreate { get; set; }

        public unsafe void* NextLayer { get; set; }
        public unsafe void* PrevLayer { get; set; }
        public int HeadBegin { get; set; }
        public int HeadSize { get { return HEAD_SIZE; } }
        public HostData CommonData { get; set; }
        public float Lifetime { get { return lifetimeCondition.lifetime; } set { lifetimeCondition.lifetime = value; } }

        public bool Initialize()
        {
            LENGTH_OFFSET = HeadBegin;
            ID_OFFSET = HeadBegin + LENGTH_SIZE;
            SEGMENT_OFFSET = HeadBegin + LENGTH_SIZE + ID_OFFSET;
            DATA_OFFSET = HeadBegin + HeadSize;
            MAX_DATA_SIZE = CommonData.MSS - DATA_OFFSET;
            identity_counter = 0;
            lifetimeCondition.current_time = Time.time;
            if (lifetimeCondition.lifetime == 0.0f)
                lifetimeCondition.lifetime = LIFETIME_DEFAULT;
            packages = new ConcurrentDictionary<PackageKey, BigPackage, BigPackageFinalizer>(16);
            time_keys = new ConcurrentQueue<TimeKey>(true);
            IsCreate = true;
            Debug.Log("Segmentation layer initialize successuly");
            return true;
        }
        public bool Receive<T>(InputPackage package) where T : struct, ILayerIdentity
        {
            if (package.buffer.Length < HeadBegin + HeadSize)
            {
                package.Release();
                return false;
            }
            if (package.buffer.Context != HeadBegin)
                package.buffer.Context = HeadBegin;
            uint length = package.buffer.ReadUInt();
            if (length <= CommonData.MSS)
            {
                package.buffer.Context = HeadBegin + HeadSize;
                return LayerManager<T>.Instance.NextReceive(package, ref this);
            }
            uint id = package.buffer.ReadUInt();
            ref IPEndPoint ep = ref UnsafeUtility.AsRef<IPEndPoint>(package.address.Pointer);
            PackageKey key = new PackageKey() { ip=ep.Address, port=ep.Port, id=id};
            BigPackage curr_big_package;
            BigPackage new_big_package = new BigPackage(length);
            Task task = new Task(package, DATA_OFFSET, MAX_DATA_SIZE); 
            curr_big_package = packages.AddOrUpdate(ref key, ref new_big_package, ref task);
            if (task.is_first == true)
            {
                curr_big_package = packages.AddOrUpdate(ref key, ref new_big_package, ref task);
                time_keys.push(new TimeKey { key = key, timestamp = lifetimeCondition.current_time });
            }
            if(task.is_last == true)
            {
                Address addr = new Address(package.address.Capacity, package.address.Allocator);
                UnsafeUtility.MemCpy(addr.Pointer, package.address.Pointer, package.address.Length);
                addr.Length = package.address.Length;
                ReadBuffer buff = new ReadBuffer(curr_big_package.pBuf.ToPointer(), (int)curr_big_package.length, (int)curr_big_package.length, package.buffer.Allocator);
                buff.Context = HeadBegin + HeadSize;
                InputPackage big_package = new InputPackage(addr, buff);
                package.Release();
                packages.Remove(ref key, out curr_big_package);
                return LayerManager<T>.Instance.NextReceive(big_package, ref this);
            }
            package.Release();
            return true;
        }
        public bool Send<T>(OutputPackage package) where T : struct, ILayerIdentity
        {
            if (package.buffer.Length <= CommonData.MSS)
            {
                package.buffer.Write((uint)package.buffer.Length, LENGTH_OFFSET);
                package.buffer.Write((uint)0, ID_OFFSET);
                package.buffer.Write((ushort)0, SEGMENT_OFFSET);
                return LayerManager<T>.Instance.NextSend(package, ref this);
            }
            uint id = (uint)Interlocked.Increment(ref UnsafeUtility.As<uint, int>(ref identity_counter));
            OutputPackage tmp_package = CommonData.out_package_alloc.Alloc();
            tmp_package.IsReleasable = false;
            UnsafeUtility.MemCpy(tmp_package.address.Pointer, package.address.Pointer, package.address.Length);
            tmp_package.address.Length = package.address.Length;
            tmp_package.buffer.Write((uint)package.buffer.Length, LENGTH_OFFSET);
            tmp_package.buffer.Write(id, ID_OFFSET);
            int offset = DATA_OFFSET;
            int length;
            ushort segment = 0;
            bool result = true;
            while (package.buffer.Length > offset)
            {
                int rest = package.buffer.Length - offset;
                length = Math.Min(MAX_DATA_SIZE, rest);
                UnsafeUtility.MemCpy(tmp_package.buffer.Pointer + DATA_OFFSET, package.buffer.Pointer + offset, length);
                tmp_package.buffer.Length = DATA_OFFSET + length;
                tmp_package.buffer.Write(segment, SEGMENT_OFFSET);
                result = result && LayerManager<T>.Instance.NextSend(tmp_package, ref this);
                offset += length;
                segment++;
            }
            tmp_package.IsReleasable = true;
            tmp_package.Release();
            package.Release();
            return result;
        }
        public void Update<T>() where T : struct, ILayerIdentity
        {
            lifetimeCondition.current_time = Time.time;
            TimeKey time_key;
            BigPackage big_package;
            if (time_keys.pop(out time_key, ref lifetimeCondition))
                packages.Remove(ref time_key.key, out big_package);
        }

        public void Dispose()
        {
            if (IsCreate)
            {
                TimeKey time_key;
                BigPackage package;
                while (time_keys.pop(out time_key))
                    packages.Remove(ref time_key.key, out package);
                packages.Dispose();
                time_keys.Dispose();
                IsCreate = false;
            }
            Debug.Log("Segmentation layer dispose");
        }
    }
}
