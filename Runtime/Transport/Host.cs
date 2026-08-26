using System;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Transport
{
    public class LayerException : Exception
    {
        public LayerException() { }
        public LayerException(string message) : base(message) { }
        public LayerException(string message, Exception inner) : base(message, inner) { }
    }

    public interface ILayerIdentity
    {
        bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer;
        bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer;
    }

    public struct HostData
    {
        private int mtu;
        private int mss;
        public int MTU { get { return mtu; } set { mtu = value; mss = mtu - 60 - 8; } }
        public int MSS { get { return mss; } }
        public PackageAllocator<InputPackage> in_package_alloc;
        public PackageAllocator<OutputPackage> out_package_alloc;
    }

    public abstract class BaseHost<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : IDisposable
        where T0 : struct, ILayer where T1 : struct, ILayer where T2 : struct, ILayer where T3 : struct, ILayer where T4 : struct, ILayer
        where T5 : struct, ILayer where T6 : struct, ILayer where T7 : struct, ILayer where T8 : struct, ILayer where T9 : struct, ILayer
        where T10 : struct, ILayer
    {
        protected readonly int count = 0;
        protected NativeArray<IntPtr> layers;
        private bool disposed = false;
        protected int head_size = 0;
        protected HostData host_data;

        private static readonly int ALLOC_CACHE_SIZE_DEFAULT = 100;
        private static readonly int ALLOC_ADDR_SIZE_DEFAULT = 16; // TODO support only ipv4
        private static readonly int MTU_DEFAULT = 1500;

        public int MTU { get { return host_data.MTU; } set { host_data.MTU = value; } }
        public int Alloc_in_size { get; set; }
        public int Alloc_out_size { get; set; }
        public int Alloc_addr_size { get; set; }

        internal struct CreateTask : LAYERS.ITask
        {
            private int count;
            private NativeArray<IntPtr> layers;
            public CreateTask(ref NativeArray<IntPtr> layers, int count)
            {
                this.count = count;
                this.layers = layers;
            }
            public unsafe bool Execute<T, U>(int i) where T : struct, ILayer where U : struct, ILayerIdentity
            {
                int size = UnsafeUtility.SizeOf<T>();
                void* ptr = UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<T>(), Allocator.Persistent);
                UnsafeUtility.MemClear(ptr, size);
                layers[i] = new IntPtr(ptr);

                if (i < count - 1)
                    return true;
                return false;
            }
        }

        internal struct LinkTask : LAYERS.ITask
        {
            private int count;
            private NativeArray<IntPtr> layers;
            private IntPtr p_prev;
            public LinkTask(ref NativeArray<IntPtr> layers, int count)
            {
                this.count = count;
                this.layers = layers;
                this.p_prev = IntPtr.Zero;
            }
            public unsafe bool Execute<T, U>(int i) where T : struct, ILayer where U : struct, ILayerIdentity
            {
                ref T layer = ref UnsafeUtility.AsRef<T>(layers[i].ToPointer());
                layer.PrevLayer = p_prev.ToPointer();
                p_prev = layers[i];
                if (i < count - 1)
                {
                    layer.NextLayer = layers[i + 1].ToPointer();
                    return true;
                }
                return false;
            }
        }

        internal struct InitTask : LAYERS.ITask
        {
            private int count;
            private NativeArray<IntPtr> layers;
            private HostData host_data;
            public int head_size;
            public bool result;
            public InitTask(ref NativeArray<IntPtr> layers, int count, HostData host_data)
            {
                this.count = count;
                this.layers = layers;
                this.host_data = host_data;
                this.head_size = 0;
                this.result = true;
            }
            public unsafe bool Execute<T, U>(int i) where T : struct, ILayer where U : struct, ILayerIdentity
            {
                ref T layer = ref UnsafeUtility.AsRef<T>(layers[i].ToPointer());
                layer.HeadBegin = head_size;
                layer.CommonData = host_data;
                result = layer.Initialize();
                head_size += layer.HeadSize;
                if (i < count - 1 && result)
                    return true;
                return false;
            }
        }
        internal struct UpdateTask : LAYERS.ITask
        {
            private int count;
            private NativeArray<IntPtr> layers;
            public UpdateTask(ref NativeArray<IntPtr> layers, int count)
            {
                this.count = count;
                this.layers = layers;
            }
            public unsafe bool Execute<T, U>(int i) where T : struct, ILayer where U : struct, ILayerIdentity
            {
                UnsafeUtility.AsRef<T>(layers[i].ToPointer()).Update<U>();
                if (i < count - 1)
                    return true;
                return false;
            }
        }
        internal struct DeleteTask : LAYERS.ITask
        {
            private int count;
            private NativeArray<IntPtr> layers;
            public DeleteTask(ref NativeArray<IntPtr> layers, int count)
            {
                this.count = count;
                this.layers = layers;
            }
            public unsafe bool Execute<T, U>(int i) where T : struct, ILayer where U : struct, ILayerIdentity
            {
                UnsafeUtility.AsRef<T>(layers[i].ToPointer()).Dispose();
                UnsafeUtility.Free(layers[i].ToPointer(), Allocator.Persistent);
                if (i < count - 1)
                    return true;
                return false;
            }
        }

        public BaseHost(int count)
        {
            this.count = count;
            layers = new NativeArray<IntPtr>(count, Allocator.Persistent);
            CreateTask create_task = new CreateTask(ref layers, count);
            LAYERS.Foreach(ref create_task);
            LinkTask link_task = new LinkTask(ref layers, count);
            LAYERS.Foreach(ref link_task);
            host_data = new HostData();
        }
        ~BaseHost()
        {
            Dispose(false);
        }
        public bool Init()
        {
            if (host_data.MTU == 0)
                host_data.MTU = MTU_DEFAULT;
            if (Alloc_in_size == 0)
                Alloc_in_size = ALLOC_CACHE_SIZE_DEFAULT;
            if (Alloc_out_size == 0)
                Alloc_out_size = ALLOC_CACHE_SIZE_DEFAULT;
            if (Alloc_addr_size == 0)
                Alloc_addr_size = ALLOC_ADDR_SIZE_DEFAULT;
            host_data.in_package_alloc = new PackageAllocator<InputPackage>(host_data.MSS, Alloc_addr_size, Alloc_in_size);
            host_data.out_package_alloc = new PackageAllocator<OutputPackage>(host_data.MSS, Alloc_addr_size, Alloc_out_size);

            InitTask init_task = new InitTask(ref layers, count, host_data);
            LAYERS.Foreach(ref init_task);
            head_size = init_task.head_size;
            return init_task.result;
        }
        public void Update()
        {
            UpdateTask update_task = new UpdateTask(ref layers, count);
            LAYERS.Foreach(ref update_task);
        }

        public T CreateInputData<T>() where T : BaseData<InputPackage>, new()
        {
            InputPackage package = host_data.in_package_alloc.Alloc();
            package.buffer.Length = 0;
            package.buffer.Context = 0;
            T instance = new T();
            instance.InsertPackage(package);
            return instance;
        }

        public T CreateOutputData<T>() where T : BaseData<OutputPackage>, new()
        {
            OutputPackage package = host_data.out_package_alloc.Alloc();
            package.buffer.Length = head_size;
            T instance = new T();
            instance.InsertPackage(package);
            return instance;
        }

        public bool Receive<T>(out T data) where T : BaseData<InputPackage>, new()
        {
            InputPackage package;
            bool result = GetLayer<LastLayer>(this.count - 1).GetPackage(out package);
            data = new T();
            if (result)
                data.InsertPackage(package);
            return result;
        }
        public abstract bool Send<T>(T data) where T : BaseData<OutputPackage>;

        public struct LAYERS
        {
            public interface ITask
            {
                bool Execute<T, U>(int i) where T : struct, ILayer where U : struct, ILayerIdentity;
            }
            public static void Foreach<T>(ref T task) where T : ITask
            {
                LAYER_0.ExecuteTask<T>(ref task);
            }

            public struct LAYER_0 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    if (task.Execute<T0, LAYER_0>(0))
                        LAYER_1.ExecuteTask<T>(ref task);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    ref T1 next_layer = ref UnsafeUtility.AsRef<T1>(layer.NextLayer);
                    return next_layer.Receive<LAYER_1>(package);
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    throw new LayerException("Method NextSend not support for LAYER_0");
                }
            }
            public struct LAYER_1 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    if (task.Execute<T1, LAYER_1>(1))
                        LAYER_2.ExecuteTask<T>(ref task);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    ref T2 next_layer = ref UnsafeUtility.AsRef<T2>(layer.NextLayer);
                    return next_layer.Receive<LAYER_2>(package);
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    ref T0 prev_layer = ref UnsafeUtility.AsRef<T0>(layer.PrevLayer);
                    return prev_layer.Send<LAYER_0>(package);
                }
            }
            public struct LAYER_2 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    if (task.Execute<T2, LAYER_2>(2))
                        LAYER_3.ExecuteTask<T>(ref task);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    ref T3 next_layer = ref UnsafeUtility.AsRef<T3>(layer.NextLayer);
                    return next_layer.Receive<LAYER_3>(package);
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    ref T1 prev_layer = ref UnsafeUtility.AsRef<T1>(layer.PrevLayer);
                    return prev_layer.Send<LAYER_1>(package);
                }
            }
            public struct LAYER_3 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    if (task.Execute<T3, LAYER_3>(3))
                        LAYER_4.ExecuteTask<T>(ref task);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    ref T4 next_layer = ref UnsafeUtility.AsRef<T4>(layer.NextLayer);
                    return next_layer.Receive<LAYER_4>(package);
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    ref T2 prev_layer = ref UnsafeUtility.AsRef<T2>(layer.PrevLayer);
                    return prev_layer.Send<LAYER_2>(package);
                }
            }
            public struct LAYER_4 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    if (task.Execute<T4, LAYER_4>(4))
                        LAYER_5.ExecuteTask<T>(ref task);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    ref T5 next_layer = ref UnsafeUtility.AsRef<T5>(layer.NextLayer);
                    return next_layer.Receive<LAYER_5>(package);
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    ref T3 prev_layer = ref UnsafeUtility.AsRef<T3>(layer.PrevLayer);
                    return prev_layer.Send<LAYER_3>(package);
                }
            }
            public struct LAYER_5 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    if (task.Execute<T5, LAYER_5>(5))
                        LAYER_6.ExecuteTask<T>(ref task);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    ref T6 next_layer = ref UnsafeUtility.AsRef<T6>(layer.NextLayer);
                    return next_layer.Receive<LAYER_6>(package);
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    ref T4 prev_layer = ref UnsafeUtility.AsRef<T4>(layer.PrevLayer);
                    return prev_layer.Send<LAYER_4>(package);
                }
            }
            public struct LAYER_6 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    if (task.Execute<T6, LAYER_6>(6))
                        LAYER_7.ExecuteTask<T>(ref task);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    ref T7 next_layer = ref UnsafeUtility.AsRef<T7>(layer.NextLayer);
                    return next_layer.Receive<LAYER_7>(package);
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    ref T5 prev_layer = ref UnsafeUtility.AsRef<T5>(layer.PrevLayer);
                    return prev_layer.Send<LAYER_5>(package);
                }
            }
            public struct LAYER_7 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    if (task.Execute<T7, LAYER_7>(7))
                        LAYER_8.ExecuteTask<T>(ref task);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    ref T8 next_layer = ref UnsafeUtility.AsRef<T8>(layer.NextLayer);
                    return next_layer.Receive<LAYER_8>(package);
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    ref T6 prev_layer = ref UnsafeUtility.AsRef<T6>(layer.PrevLayer);
                    return prev_layer.Send<LAYER_6>(package);
                }
            }
            public struct LAYER_8 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    if (task.Execute<T8, LAYER_8>(8))
                        LAYER_9.ExecuteTask<T>(ref task);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    ref T9 next_layer = ref UnsafeUtility.AsRef<T9>(layer.NextLayer);
                    return next_layer.Receive<LAYER_9>(package);
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    ref T7 prev_layer = ref UnsafeUtility.AsRef<T7>(layer.PrevLayer);
                    return prev_layer.Send<LAYER_7>(package);
                }
            }
            public struct LAYER_9 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    if (task.Execute<T9, LAYER_9>(9))
                        LAYER_10.ExecuteTask<T>(ref task);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    ref T10 next_layer = ref UnsafeUtility.AsRef<T10>(layer.NextLayer);
                    return next_layer.Receive<LAYER_10>(package);
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    ref T8 prev_layer = ref UnsafeUtility.AsRef<T8>(layer.PrevLayer);
                    return prev_layer.Send<LAYER_8>(package);
                }
            }
            public struct LAYER_10 : ILayerIdentity
            {
                public static void ExecuteTask<T>(ref T task) where T : ITask
                {
                    task.Execute<T10, LAYER_10>(10);
                }
                public unsafe bool NextReceive<T>(InputPackage package, ref T layer) where T : ILayer
                {
                    throw new LayerException("Method NextReceive not support for LAYER_10");
                }
                public unsafe bool NextSend<T>(OutputPackage package, ref T layer) where T : ILayer
                {
                    ref T9 prev_layer = ref UnsafeUtility.AsRef<T9>(layer.PrevLayer);
                    return prev_layer.Send<LAYER_9>(package);
                }
            }
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // free managed resources
                }
                // free unmanaged resources
                DeleteTask delete_task = new DeleteTask(ref layers, count);
                LAYERS.Foreach(ref delete_task);
                layers.Dispose();
                host_data.in_package_alloc.Dispose();
                host_data.out_package_alloc.Dispose();
                disposed = true;
            }
        }

        protected unsafe ref T GetLayer<T>(int i) where T : struct, ILayer
        {
            return ref UnsafeUtility.AsRef<T>(layers[i].ToPointer());
        }

    }

    public struct Dummy : ILayer
    {
        public unsafe void* NextLayer { get { return null; } set { } }
        public unsafe void* PrevLayer { get { return null; } set { } }
        public int HeadBegin { get { return 0; } set { } }
        public int HeadSize { get { return 0; } }
        public HostData CommonData { get; set; }
        public bool Initialize() { return true; }
        public bool Receive<T>(InputPackage package) where T : struct, ILayerIdentity { return false; }
        public bool Send<T>(OutputPackage package) where T : struct, ILayerIdentity { return false; }
        public void Update<T>() where T : struct, ILayerIdentity { }
        public void Dispose() { }
    }

    public class Host<T0> : BaseHost<T0, LastLayer, Dummy, Dummy, Dummy, Dummy, Dummy, Dummy, Dummy, Dummy, Dummy>
        where T0 : struct, ILayer
    {
        public Host() : base(2) { }
        public override unsafe bool Send<T>(T data)
        {
            return GetLayer<T0>(0).Send<LAYERS.LAYER_0>(data.ExtractPackage());
        }
        public ref T0 Layer0 { get { return ref GetLayer<T0>(0); } }
    }
    public class Host<T0, T1> : BaseHost<T0, T1, LastLayer, Dummy, Dummy, Dummy, Dummy, Dummy, Dummy, Dummy, Dummy>
        where T0 : struct, ILayer where T1 : struct, ILayer
    {
        public Host() : base(3) { }
        public override unsafe bool Send<T>(T data)
        {
            return GetLayer<T1>(1).Send<LAYERS.LAYER_1>(data.ExtractPackage());
        }
        public ref T0 Layer0 { get { return ref GetLayer<T0>(0); } }
        public ref T1 Layer1 { get { return ref GetLayer<T1>(1); } }
    }
    public class Host<T0, T1, T2> : BaseHost<T0, T1, T2, LastLayer, Dummy, Dummy, Dummy, Dummy, Dummy, Dummy, Dummy>
        where T0 : struct, ILayer where T1 : struct, ILayer where T2 : struct, ILayer
    {
        public Host() : base(4) { }
        public override unsafe bool Send<T>(T data)
        {
            return GetLayer<T2>(2).Send<LAYERS.LAYER_2>(data.ExtractPackage());
        }
        public ref T0 Layer0 { get { return ref GetLayer<T0>(0); } }
        public ref T1 Layer1 { get { return ref GetLayer<T1>(1); } }
        public ref T2 Layer2 { get { return ref GetLayer<T2>(2); } }
    }
    public class Host<T0, T1, T2, T3> : BaseHost<T0, T1, T2, T3, LastLayer, Dummy, Dummy, Dummy, Dummy, Dummy, Dummy>
        where T0 : struct, ILayer where T1 : struct, ILayer where T2 : struct, ILayer where T3 : struct, ILayer
    {
        public Host() : base(5) { }
        public override unsafe bool Send<T>(T data)
        {
            return GetLayer<T3>(3).Send<LAYERS.LAYER_3>(data.ExtractPackage());
        }
        public ref T0 Layer0 { get { return ref GetLayer<T0>(0); } }
        public ref T1 Layer1 { get { return ref GetLayer<T1>(1); } }
        public ref T2 Layer2 { get { return ref GetLayer<T2>(2); } }
        public ref T3 Layer3 { get { return ref GetLayer<T3>(3); } }
    }
    public class Host<T0, T1, T2, T3, T4> : BaseHost<T0, T1, T2, T3, T4, LastLayer, Dummy, Dummy, Dummy, Dummy, Dummy>
        where T0 : struct, ILayer where T1 : struct, ILayer where T2 : struct, ILayer where T3 : struct, ILayer where T4 : struct, ILayer
    {
        public Host() : base(6) { }
        public override unsafe bool Send<T>(T data)
        {
            return GetLayer<T4>(4).Send<LAYERS.LAYER_4>(data.ExtractPackage());
        }
        public ref T0 Layer0 { get { return ref GetLayer<T0>(0); } }
        public ref T1 Layer1 { get { return ref GetLayer<T1>(1); } }
        public ref T2 Layer2 { get { return ref GetLayer<T2>(2); } }
        public ref T3 Layer3 { get { return ref GetLayer<T3>(3); } }
        public ref T4 Layer4 { get { return ref GetLayer<T4>(4); } }
    }
    public class Host<T0, T1, T2, T3, T4, T5> : BaseHost<T0, T1, T2, T3, T4, T5, LastLayer, Dummy, Dummy, Dummy, Dummy>
        where T0 : struct, ILayer where T1 : struct, ILayer where T2 : struct, ILayer where T3 : struct, ILayer where T4 : struct, ILayer
        where T5 : struct, ILayer
    {
        public Host() : base(7) { }
        public override unsafe bool Send<T>(T data)
        {
            return GetLayer<T5>(5).Send<LAYERS.LAYER_5>(data.ExtractPackage());
        }
        public ref T0 Layer0 { get { return ref GetLayer<T0>(0); } }
        public ref T1 Layer1 { get { return ref GetLayer<T1>(1); } }
        public ref T2 Layer2 { get { return ref GetLayer<T2>(2); } }
        public ref T3 Layer3 { get { return ref GetLayer<T3>(3); } }
        public ref T4 Layer4 { get { return ref GetLayer<T4>(4); } }
        public ref T5 Layer5 { get { return ref GetLayer<T5>(5); } }
    }
    public class Host<T0, T1, T2, T3, T4, T5, T6> : BaseHost<T0, T1, T2, T3, T4, T5, T6, LastLayer, Dummy, Dummy, Dummy>
        where T0 : struct, ILayer where T1 : struct, ILayer where T2 : struct, ILayer where T3 : struct, ILayer where T4 : struct, ILayer
        where T5 : struct, ILayer where T6 : struct, ILayer
    {
        public Host() : base(8) { }
        public override unsafe bool Send<T>(T data)
        {
            return GetLayer<T6>(6).Send<LAYERS.LAYER_6>(data.ExtractPackage());
        }
        public ref T0 Layer0 { get { return ref GetLayer<T0>(0); } }
        public ref T1 Layer1 { get { return ref GetLayer<T1>(1); } }
        public ref T2 Layer2 { get { return ref GetLayer<T2>(2); } }
        public ref T3 Layer3 { get { return ref GetLayer<T3>(3); } }
        public ref T4 Layer4 { get { return ref GetLayer<T4>(4); } }
        public ref T5 Layer5 { get { return ref GetLayer<T5>(5); } }
        public ref T6 Layer6 { get { return ref GetLayer<T6>(6); } }
    }
    public class Host<T0, T1, T2, T3, T4, T5, T6, T7> : BaseHost<T0, T1, T2, T3, T4, T5, T6, T7, LastLayer, Dummy, Dummy>
        where T0 : struct, ILayer where T1 : struct, ILayer where T2 : struct, ILayer where T3 : struct, ILayer where T4 : struct, ILayer
        where T5 : struct, ILayer where T6 : struct, ILayer where T7 : struct, ILayer
    {
        public Host() : base(9) { }
        public override unsafe bool Send<T>(T data)
        {
            return GetLayer<T7>(7).Send<LAYERS.LAYER_7>(data.ExtractPackage());
        }
        public ref T0 Layer0 { get { return ref GetLayer<T0>(0); } }
        public ref T1 Layer1 { get { return ref GetLayer<T1>(1); } }
        public ref T2 Layer2 { get { return ref GetLayer<T2>(2); } }
        public ref T3 Layer3 { get { return ref GetLayer<T3>(3); } }
        public ref T4 Layer4 { get { return ref GetLayer<T4>(4); } }
        public ref T5 Layer5 { get { return ref GetLayer<T5>(5); } }
        public ref T6 Layer6 { get { return ref GetLayer<T6>(6); } }
        public ref T7 Layer7 { get { return ref GetLayer<T7>(7); } }
    }
    public class Host<T0, T1, T2, T3, T4, T5, T6, T7, T8> : BaseHost<T0, T1, T2, T3, T4, T5, T6, T7, T8, LastLayer, Dummy>
        where T0 : struct, ILayer where T1 : struct, ILayer where T2 : struct, ILayer where T3 : struct, ILayer where T4 : struct, ILayer
        where T5 : struct, ILayer where T6 : struct, ILayer where T7 : struct, ILayer where T8 : struct, ILayer
    {
        public Host() : base(10) { }
        public override unsafe bool Send<T>(T data)
        {
            return GetLayer<T8>(8).Send<LAYERS.LAYER_8>(data.ExtractPackage());
        }
        public ref T0 Layer0 { get { return ref GetLayer<T0>(0); } }
        public ref T1 Layer1 { get { return ref GetLayer<T1>(1); } }
        public ref T2 Layer2 { get { return ref GetLayer<T2>(2); } }
        public ref T3 Layer3 { get { return ref GetLayer<T3>(3); } }
        public ref T4 Layer4 { get { return ref GetLayer<T4>(4); } }
        public ref T5 Layer5 { get { return ref GetLayer<T5>(5); } }
        public ref T6 Layer6 { get { return ref GetLayer<T6>(6); } }
        public ref T7 Layer7 { get { return ref GetLayer<T7>(7); } }
        public ref T8 Layer8 { get { return ref GetLayer<T8>(8); } }
    }
    public class Host<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9> : BaseHost<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, LastLayer>
        where T0 : struct, ILayer where T1 : struct, ILayer where T2 : struct, ILayer where T3 : struct, ILayer where T4 : struct, ILayer
        where T5 : struct, ILayer where T6 : struct, ILayer where T7 : struct, ILayer where T8 : struct, ILayer where T9 : struct, ILayer
    {
        public Host() : base(11) { }
        public override unsafe bool Send<T>(T data)
        {
            return GetLayer<T9>(9).Send<LAYERS.LAYER_9>(data.ExtractPackage());
        }
        public ref T0 Layer0 { get { return ref GetLayer<T0>(0); } }
        public ref T1 Layer1 { get { return ref GetLayer<T1>(1); } }
        public ref T2 Layer2 { get { return ref GetLayer<T2>(2); } }
        public ref T3 Layer3 { get { return ref GetLayer<T3>(3); } }
        public ref T4 Layer4 { get { return ref GetLayer<T4>(4); } }
        public ref T5 Layer5 { get { return ref GetLayer<T5>(5); } }
        public ref T6 Layer6 { get { return ref GetLayer<T6>(6); } }
        public ref T7 Layer7 { get { return ref GetLayer<T7>(7); } }
        public ref T8 Layer8 { get { return ref GetLayer<T8>(8); } }
        public ref T9 Layer9 { get { return ref GetLayer<T9>(9); } }
    }

    public static class LayerManager<T> where T : struct, ILayerIdentity
    {
        private static readonly T instance = new T();
        public static ref readonly T Instance { get { return ref instance; } }
    }
}


