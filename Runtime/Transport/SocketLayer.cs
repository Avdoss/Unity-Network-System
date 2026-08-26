#if UNITY_STANDALONE_WIN
#if !UNITY_64
using socket_t = System.UInt32;
#else
using socket_t = System.UInt64;
#endif
using bufsize_t = System.Int32;
using addrsize_t = System.Int32;
using msgsize_t = System.Int32;
#elif UNITY_STANDALONE_LINUX
#elif UNITY_ANDROID
using socket_t = System.Int32;
#if !UNITY_64
using bufsize_t = System.UInt32;
#else
using bufsize_t = System.UInt64;
#endif
using addrsize_t = System.Int32;
using msgsize_t = System.Int32;
#endif

using System;
using System.Threading;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace Transport
{
    public unsafe struct SocketLayer : ILayer
    {
        private static readonly int RECV_BUF_SIZE_DEFAULT = 10000 * 1024;
        private static readonly int SEND_BUF_SIZE_DEFAULT = 10000 * 1024;
        private static readonly int JOBS_DEFAULT = 4;

        private socket_t socket;
        private IPEndPoint addr;
        private int recv_buf_size;
        private int send_buf_size;
        private int jobs;

        private bool receive_enable;

        private bool IsCreate { get; set; }
        public bool Async { get; set; }
        public ushort Port { get { return addr.Port; } set { addr.Port = value; } }
        public uint IPAddress { get { return addr.Address; } set { addr.Address = value; } }
        public int RecvBufSize { get { return recv_buf_size; } set { recv_buf_size = value; } }
        public int SendBufSize { get { return send_buf_size; } set { send_buf_size = value; } }
        public int Jobs { get { return jobs; } set { jobs = value; } }

        public unsafe void* NextLayer { get; set; }
        public unsafe void* PrevLayer { get; set; }
        public int HeadBegin { get; set; }
        public int HeadSize { get { return 0; } }
        public HostData CommonData { get; set; }

        [BurstCompile]
        private struct ReceiveJob<T>: IJob where T: struct, ILayerIdentity
        {
            [NativeDisableUnsafePtrRestriction]
            public SocketLayer* ptr;
            [BurstCompile]
            public void Execute()
            {
                while (ptr->receive_enable)
                {
                    if(!ptr->Receive<T>())
                        ptr->receive_enable = false;
                }
            }
        }
        private bool Receive<T>() where T: struct, ILayerIdentity
        {
            InputPackage package = CommonData.in_package_alloc.Alloc();
            addrsize_t addrsize = package.address.Capacity;
            msgsize_t msgsize = SocketLib.recv_from(socket, new IntPtr(package.buffer.Pointer), (bufsize_t)package.buffer.Capacity, 0, new IntPtr(package.address.Pointer), ref addrsize);
            if (msgsize <= 0)
            {
                package.Release();
                return false;
            }
            package.buffer.Length = msgsize;
            package.address.Length = addrsize;
            return LayerManager<T>.Instance.NextReceive(package, ref this);
        }

        public bool Initialize()
        {
            if (recv_buf_size == 0)
                recv_buf_size = RECV_BUF_SIZE_DEFAULT;
            if (send_buf_size == 0)
                send_buf_size = SEND_BUF_SIZE_DEFAULT;
            if (jobs == 0)
                jobs = JOBS_DEFAULT;
            int result = SocketLib.initialize();
            if(result != 0)
            {
                Debug.Log("Native sockets initialization error");
                return false;
            }
            socket = SocketLib.create_sock((int)ADDR_FAMILY.AF_INET, (int)SOCK_TYPE.SOCK_DGRAM, 0);
            if (socket + 1 == 0)
            {
                Debug.Log("Create socket error");
                SocketLib.release();
                return false;
            }
            if (!SocketLib.non_blocking_mode(socket, true))
            {
                Debug.Log("Set socket non-blocking mode error");
                SocketLib.close_sock(socket);
                SocketLib.release();
                return false;
            }
            result = SocketLib.set_recv_buf(socket, recv_buf_size);
            if (result!=0)
            {
                Debug.Log("Set receive buffer size error");
                SocketLib.close_sock(socket);
                SocketLib.release();
                return false;
            }
            result = SocketLib.set_send_buf(socket, send_buf_size);
            if (result != 0)
            {
                Debug.Log("Set send buffer size error");
                SocketLib.close_sock(socket);
                SocketLib.release();
                return false;
            }
            addr.AF = ADDR_FAMILY.AF_INET;
            IPEndPoint ep = addr;
            result = SocketLib.bind_sock(socket, new IntPtr(&ep), UnsafeUtility.SizeOf<IPEndPoint>());
            if(result < 0)
            {
                Debug.Log("Bind socket error");
                SocketLib.close_sock(socket);
                SocketLib.release();
                return false;
            }
            IsCreate = true;
            Debug.Log("Socket layer initialize successuly");
            return true;
        }
        public bool Receive<T>(InputPackage package) where T : struct, ILayerIdentity
        {
            //Debug.Log("socket layer receive");
            return true;
        }
        public bool Send<T>(OutputPackage package) where T : struct, ILayerIdentity
        {
            msgsize_t msgsize = SocketLib.send_to(socket, new IntPtr(package.buffer.Pointer), (bufsize_t)package.buffer.Length, 0, new IntPtr(package.address.Pointer), package.address.Length);
            package.Release();
            return msgsize > 0;
        }
        public void Update<T>() where T : struct, ILayerIdentity
        {
            //Debug.Log("socket layer update");
            if (Async)
            {
                receive_enable = true;
                JobHandle[] handles = new JobHandle[jobs];
                for (int i = 0; i < jobs; i++)
                {
                    ReceiveJob<T> job = new ReceiveJob<T>();
                    job.ptr = (SocketLayer*)UnsafeUtility.AddressOf(ref this);
                    handles[i] = job.Schedule();
                }
                for (int i = 0; i < jobs; i++)
                    handles[i].Complete();
            }
            else
                while (Receive<T>()) ;
        }

        public void Dispose()
        {
            if (IsCreate)
            {
                SocketLib.close_sock(socket);
                SocketLib.release();
                IsCreate = false;
            }
            Debug.Log("Socket layer dispose");
        }
    }
}
