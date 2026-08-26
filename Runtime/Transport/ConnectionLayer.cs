using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Burst;
using Multithreading;

namespace Transport
{
    [StructLayout(LayoutKind.Explicit)]
    public struct ChannelInfo
    {
        [FieldOffset(0)] public int channel;
        [FieldOffset(4)] public MSG_TYPE msg_type;
        [FieldOffset(5)] public ERR_CODE err_code;
        [FieldOffset(6)] public ADDR_FAMILY af;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct ChannelInfoIPv4
    {
        [FieldOffset(0)]  public int channel;
        [FieldOffset(4)]  public MSG_TYPE msg_type;
        [FieldOffset(5)]  public ERR_CODE err_code;
        [FieldOffset(6)]  public ADDR_FAMILY af;
        [FieldOffset(8)]  public ipv4 ip;
        [FieldOffset(12)] public ushort port;
    }

    public enum MSG_TYPE : byte
    {
        CONNECT = 0,
        DISCONNECT = 1,
        ACCEPT = 2,
        RELIABLE = 3,
        NON_RELIABLE = 4
    }

    public enum ERR_CODE: byte
    {
        NONE = 0,
        TIMEOUT = 1,
        ACCEPT_DISABLE = 2,
        MAX_CHANNEL_LIMIT = 3
    }

    public enum PKG_TYPE : byte
    {
        CONNECT = 0,
        DISCONNECT = 1,
        ANSWER = 2,
        PING = 3,
        RELIABLE = 4,
        NON_RELIABLE = 5,
        NOTICE = 6
    }

    public unsafe struct ConnectionLayer : ILayer
    {
        internal enum STATUS: short
        {
            DISCONNECTED = 0,
            INIT = 1,
            SYNC = 2,
            ACCEPT = 3,
            PREPARE = 4,
            WAIT = 5,
            CONNECTING= 6,
            CONNECTED = 7
        }
        internal enum IN_PKG_STATUS : int
        {
            NONE = 0,
            INIT = 1,
            READY = 2,
            PROCESSED = 3
        }
        internal enum OUT_PKG_STATUS: int
        {
            NONE = 0,
            INIT = 1,
            WAIT_NOTICE = 2,
            PROCESSED = 3,
            DELETING = 4
        }
        [StructLayout(LayoutKind.Explicit)]
        internal struct InPackageInfo
        {
            [FieldOffset(0)] public long value;
            [FieldOffset(0)] public IN_PKG_STATUS status;
            [FieldOffset(4)] public int id;
        }
        [StructLayout(LayoutKind.Explicit)]
        internal struct OutPackageInfo
        {
            [FieldOffset(0)] public long value;
            [FieldOffset(0)] public OUT_PKG_STATUS status;
            [FieldOffset(4)] public int id;
            [FieldOffset(8)] public float timestamp;
        }
        internal struct InReleablePackage
        {
            public InPackageInfo info;
            public InputPackage package;
        }
        internal struct OutReleablePackage
        {
            public OutPackageInfo info;
            public OutputPackage package;
        }
        [StructLayout(LayoutKind.Explicit)]
        internal struct State
        {
            [FieldOffset(0)] public int value;
            [FieldOffset(0)] public STATUS status;
            [FieldOffset(2)] public short references;
        }
        internal struct Channel
        {
            public State state;
            public int next_input_package;
            public int next_output_package;
            public short input_connection_desc;
            public short output_connection_desc;
            public int output_channel;
            public float receive_timestamp;
            public float send_timestamp;
            public bool send_enable;
            public SharedObject<Address> addr;
            public InputPackage report_pkg;
            public InReleablePackage* input_buffer;
            public OutReleablePackage* output_buffer;
            public ConcurrentQueue<OutputPackage> output_queue;
        }
        internal struct ConnectionFinalizer : IFinalizer<SharedObject<Address>, bool>
        {
            public void Release(ref SharedObject<Address> key, ref bool value)
            {
                key.Dispose();
            }
        }
        internal struct AddConnTask : ConcurrentDictionary<SharedObject<Address>, bool, ConnectionFinalizer>.ITask
        {
            public bool is_add;
            public void execute(ref bool current_value, ref bool new_value)
            {
                is_add = false;
            }
        }
        internal struct ChangeConnTask : ConcurrentDictionary<SharedObject<Address>, bool, ConnectionFinalizer>.ITask
        {
            public void execute(ref bool current_value, ref bool new_value)
            {
                current_value = new_value;
            }
        }
        private static readonly int TYPE_SIZE = UnsafeUtility.SizeOf<byte>();
        private static readonly int CHANNEL_SIZE = UnsafeUtility.SizeOf<int>();
        private static readonly int CONN_DESC_SIZE = UnsafeUtility.SizeOf<short>();
        private static readonly int ERR_CODE_SIZE = UnsafeUtility.SizeOf<byte>();
        private static readonly int NEXT_PKG_SIZE = UnsafeUtility.SizeOf<int>();
        private static readonly int HEAD_SIZE = TYPE_SIZE + CHANNEL_SIZE + CONN_DESC_SIZE + ERR_CODE_SIZE + NEXT_PKG_SIZE;
        private static readonly int HEAD_SIZE_PING = TYPE_SIZE + CHANNEL_SIZE + CONN_DESC_SIZE;
        private static readonly int MAX_CHANNELS_DEFAULT = 100;
        private static readonly int INPUT_BUFFER_SIZE_DEFAULT = 320;
        private static readonly int OUTPUT_BUFFER_SIZE_DEFAULT = 320;
        private static readonly float TIMEOUT_DEFAULT = 10.0f;
        private static readonly float CONNECT_TIMEOUT_DEFAULT = 10.0f;
        private static readonly float PING_PERIOD_DEFAULT = 2.0f;
        private static readonly float RELIABLE_REPEAT_PERIOD_DEFAULT = 3.0f;
        private static readonly int JOBS_DEFAULT = 4;

        private int TYPE_OFFSET;
        private int CHANNEL_OFFSET;
        private int CONN_DESC_OFFSET;
        private int ERR_CODE_OFFSET;
        private int NEXT_PKG_OFFSET;
        private int max_channels;
        private int channels_counter;
        private int input_buffer_size;
        private int output_buffer_size;
        private float timeout;
        private float connect_timeout;
        private float ping_period;
        private float reliable_repeat_period;
        private float current_time;
        private Channel* pChannels;
        private ConcurrentDictionary<SharedObject<Address>, bool, ConnectionFinalizer> connections;
        private ConcurrentQueue<OutputPackage> output_queue;
        private bool send_enable;
        private int update_counter;
        private int jobs;
        private bool IsCreate { get; set; }

        public unsafe void* NextLayer { get; set; }
        public unsafe void* PrevLayer { get; set; }
        public int HeadBegin { get; set; }
        public int HeadSize { get { return HEAD_SIZE; } }
        public HostData CommonData { get; set; }
        public int MaxChannels { get { return max_channels; } set { max_channels = value; } }
        public int InputBufferSize { get { return input_buffer_size; } set { input_buffer_size = value; } }
        public int OutputBufferSize { get { return output_buffer_size; } set { output_buffer_size = value; } }
        public float ConnectTimeout { get { return connect_timeout; } set { connect_timeout = value; } }
        public float Timeout { get { return timeout; } set { timeout = value; } }
        public float PingPeriod { get { return ping_period; } set { ping_period = value; } }
        public float ReliableRepeatPeriod { get { return reliable_repeat_period; } set { reliable_repeat_period = value; } }
        public bool AcceptQueryEnable { get; set; }
        public bool Async { get; set; }
        public int Jobs { get { return jobs; } set { jobs = value; } }

        private static Address CreateEndPointFromChannelInfo(Address addr, int capacity)
        {
            Address ep_addr;
            ref ChannelInfo ch_info = ref UnsafeUtility.AsRef<ChannelInfo>(addr.Pointer);
            switch (ch_info.af)
            {
                case ADDR_FAMILY.AF_INET:
                    int addr_size = UnsafeUtility.SizeOf<IPEndPoint>();
                    ep_addr = new Address(capacity);
                    ep_addr.Length = addr_size;
                    UnsafeUtility.MemClear(ep_addr.Pointer, addr_size);
                    ref IPEndPoint addr_ipv4 = ref UnsafeUtility.AsRef<IPEndPoint>(ep_addr.Pointer);
                    ref ChannelInfoIPv4 ch_info_ipv4 = ref UnsafeUtility.As<ChannelInfo, ChannelInfoIPv4>(ref ch_info);
                    addr_ipv4.AF = ch_info_ipv4.af;
                    addr_ipv4.Address = ch_info_ipv4.ip.addr;
                    addr_ipv4.Port = ch_info_ipv4.port;
                    break;
                default:
                    throw new NotSupportedException(string.Format("Not supported address family: {0}", ch_info.af));
            }
            return ep_addr;
        }
        private static Address CreateChannelInfoFromEndPoint(Address addr, int capacity)
        {
            Address ch_addr;
            ref EndPoint ep = ref UnsafeUtility.AsRef<EndPoint>(addr.Pointer);
            switch(ep.AF)
            {
                case ADDR_FAMILY.AF_INET:
                    int addr_size = UnsafeUtility.SizeOf<ChannelInfoIPv4>();
                    ch_addr = new Address(capacity);
                    ch_addr.Length = addr_size;
                    UnsafeUtility.MemClear(ch_addr.Pointer, addr_size);
                    ref ChannelInfoIPv4 ch_info_ipv4 = ref UnsafeUtility.AsRef<ChannelInfoIPv4>(ch_addr.Pointer);
                    ref IPEndPoint ep_ipv4 = ref UnsafeUtility.As<EndPoint, IPEndPoint>(ref ep);
                    ch_info_ipv4.af = ep_ipv4.AF;
                    ch_info_ipv4.ip.addr = ep_ipv4.Address;
                    ch_info_ipv4.port = ep_ipv4.Port;
                    break;
                default:
                    throw new NotSupportedException(string.Format("Not supported address family: {0}", ep.AF));
            }
            return ch_addr;
        }
        private bool TryAddNewConnection(ref SharedObject<Address> addr)
        {
            AddConnTask task;
            bool enable;
            bool value = true;
            while(true)
            {
                task.is_add = true;
                addr.Share();
                enable = connections.AddOrUpdate(ref addr, ref value, ref task);
                if (task.is_add)
                    return true;
                if (enable)
                    return false;
                Utils.Yield();
            }
        }
        private void DisableConnection(ref SharedObject<Address> addr)
        {
            ChangeConnTask task;
            bool enable = false;
            addr.Share();
            connections.AddOrUpdate(ref addr, ref enable, ref task);
        }
        private void RemoveConnection(ref SharedObject<Address> addr)
        {
            bool value;
            connections.Remove(ref addr, out value);
        }
        private bool TryIncreaseChannelCounter()
        {
            int ch_counter_old;
            int ch_counter_tmp = channels_counter;
            do
            {
                if (ch_counter_tmp >= max_channels)
                    return false;
                ch_counter_old = ch_counter_tmp;
                ch_counter_tmp += 1;
            }
            while ((ch_counter_tmp = Interlocked.CompareExchange(ref channels_counter, ch_counter_tmp, ch_counter_old)) != ch_counter_old);
            return true;
        }
        private int ReserveChannel(ref SharedObject<Address> addr)
        {
            while (true)
            {
                for (int i = 0; i < max_channels; i++)
                {
                    ref Channel channel = ref pChannels[i];
                    State old_state = channel.state;
                    if (old_state.status == STATUS.DISCONNECTED && old_state.references == 0)
                    {
                        State new_state = new State() { status = STATUS.INIT, references = 2 };
                        if (Interlocked.CompareExchange(ref channel.state.value, new_state.value, old_state.value) == old_state.value)
                        {
                            channel.input_connection_desc += 1;
                            channel.receive_timestamp = current_time;
                            channel.send_timestamp = current_time;
                            channel.addr = addr.Share();
                            channel.report_pkg = new InputPackage();
                            return i;
                        }
                    }   
                }
            }
        }
        private bool IncreaseChannelReferences(int id)
        {
            if (id < 0 || id >= max_channels)
                return false;
            ref Channel channel = ref pChannels[id];
            State old_state;
            State tmp_state = channel.state;
            do
            {
                if (tmp_state.status == STATUS.INIT || tmp_state.status == STATUS.DISCONNECTED)
                    return false;
                old_state = tmp_state;
                tmp_state.references += 1;
            }
            while ((tmp_state.value = Interlocked.CompareExchange(ref channel.state.value, tmp_state.value, old_state.value)) != old_state.value);
            return true;
        }
        private void DecreaseChannelReferences<T>(int id) where T : struct, ILayerIdentity
        {
            ref Channel channel = ref pChannels[id];
            State old_state;
            State tmp_state = channel.state;
            do
            {
                old_state = tmp_state;
                tmp_state.references -= 1;
            }
            while ((tmp_state.value = Interlocked.CompareExchange(ref channel.state.value, tmp_state.value, old_state.value)) != old_state.value);
            if (old_state.status == STATUS.DISCONNECTED && old_state.references == 2)
            {
                InputPackage report_pkg = channel.report_pkg;
                SharedObject<Address> share_addr = channel.addr.Share();
                ResetChannel(id);
                channel.state.references = 0;
                Interlocked.Decrement(ref channels_counter);
                if (report_pkg.address.Pointer != null)
                {
                    DisableConnection(ref share_addr);
                    LayerManager<T>.Instance.NextReceive(report_pkg, ref this);
                }
                RemoveConnection(ref share_addr);
                share_addr.Dispose();
            }
        }
        private bool CheckAndUpdateChannel<T>(int id, short conn_desc, Address addr, bool check_connect) where T : struct, ILayerIdentity
        {
            if (id < 0 || id >= max_channels)
                return false;
            ref Channel channel = ref pChannels[id];
            bool isChannelMatch = channel.input_connection_desc == conn_desc && channel.addr.Object == addr;
            if (isChannelMatch)
                Interlocked.Exchange(ref channel.receive_timestamp, current_time);
            return isChannelMatch && (!check_connect || IsConnectionEstablished<T>(id));
        }
        private bool ChangeChannelStatus(int id, STATUS from, STATUS to)
        {
            ref Channel channel = ref pChannels[id];
            State old_state;
            State tmp_state = channel.state;
            do
            {
                if (tmp_state.status != from)
                    return false;
                old_state = tmp_state;
                tmp_state.status = to;
            }
            while ((tmp_state.value = Interlocked.CompareExchange(ref channel.state.value, tmp_state.value, old_state.value)) != old_state.value);
            return true;
        }

        private void ResetChannel(int id)
        {
            ref Channel channel = ref pChannels[id];
            channel.addr.Dispose();
            if (channel.input_buffer != null)
            {
                for (int i = 0; i < input_buffer_size; i++)
                {
                    if (channel.input_buffer[i].info.status != IN_PKG_STATUS.NONE)
                    {
                        channel.input_buffer[i].package.Release();
                        channel.input_buffer[i].info.status = IN_PKG_STATUS.NONE;
                    }
                }
            }
            if (channel.output_buffer != null)
            {
                for (int i = 0; i < output_buffer_size; i++)
                {
                    if (channel.output_buffer[i].info.status != OUT_PKG_STATUS.NONE)
                    {
                        channel.output_buffer[i].package.Release();
                        channel.output_buffer[i].info.status = OUT_PKG_STATUS.NONE;
                    }
                }
            }
            if(channel.output_queue.IsCreate)
            {
                OutputPackage package;
                while (channel.output_queue.pop(out package))
                    package.Release();
            }
        }

        private void ReleaseChannel(int id)
        {
            ref Channel channel = ref pChannels[id];
            if(channel.state.status != STATUS.DISCONNECTED)
                ResetChannel(id);
            if (channel.input_buffer != null)
            {
                UnsafeUtility.Free(channel.input_buffer, Allocator.Persistent);
                channel.input_buffer = null;
            }
            if (channel.output_buffer != null)
            {
                UnsafeUtility.Free(channel.output_buffer, Allocator.Persistent);
                channel.output_buffer = null;
            }
            if (channel.output_queue.IsCreate)
                channel.output_queue.Dispose();
        }

        private bool IsConnectionEstablished<T>(int id) where T : struct, ILayerIdentity
        {
            ref Channel channel = ref pChannels[id];
            if (ChangeChannelStatus(id, STATUS.CONNECTING, STATUS.WAIT))
            {
                Address addr = CreateChannelInfoFromEndPoint(channel.addr.Object, CommonData.in_package_alloc.Addr_size);
                ref ChannelInfo info = ref UnsafeUtility.AsRef<ChannelInfo>(addr.Pointer);
                info.channel = id;
                info.msg_type = MSG_TYPE.CONNECT;
                info.err_code = ERR_CODE.NONE;
                InputPackage notice_pkg = new InputPackage();
                notice_pkg.address = addr;
                LayerManager<T>.Instance.NextReceive(notice_pkg, ref this);
                ChangeChannelStatus(id, STATUS.WAIT, STATUS.CONNECTED);
            }
            while (channel.state.status == STATUS.WAIT)
                Multithreading.Utils.Yield();
            return channel.state.status == STATUS.CONNECTED;
        }

        private bool SendNoticePackage<T>(ref Channel channel, int pkg_id) where T : struct, ILayerIdentity
        {
            OutputPackage notice_pkg = CommonData.out_package_alloc.Alloc();
            UnsafeUtility.MemCpy(notice_pkg.address.Pointer, channel.addr.Object.Pointer, channel.addr.Object.Length);
            notice_pkg.address.Length = channel.addr.Object.Length;
            notice_pkg.buffer.Write((byte)PKG_TYPE.NOTICE, TYPE_OFFSET);
            notice_pkg.buffer.Write(channel.output_channel, CHANNEL_OFFSET);
            notice_pkg.buffer.Write(channel.output_connection_desc, CONN_DESC_OFFSET);
            notice_pkg.buffer.Write((byte)ERR_CODE.NONE, ERR_CODE_OFFSET);
            notice_pkg.buffer.Write(pkg_id, NEXT_PKG_OFFSET);
            return LayerManager<T>.Instance.NextSend(notice_pkg, ref this);
        }
        private bool SendPingPackage<T>(ref Channel channel) where T : struct, ILayerIdentity
        {
            OutputPackage ping_pkg = CommonData.out_package_alloc.Alloc();
            UnsafeUtility.MemCpy(ping_pkg.address.Pointer, channel.addr.Object.Pointer, channel.addr.Object.Length);
            ping_pkg.address.Length = channel.addr.Object.Length;
            ping_pkg.buffer.Write((byte)PKG_TYPE.PING, TYPE_OFFSET);
            ping_pkg.buffer.Write(channel.output_channel, CHANNEL_OFFSET);
            ping_pkg.buffer.Write(channel.output_connection_desc, CONN_DESC_OFFSET);
            ping_pkg.buffer.Length = HeadBegin + HEAD_SIZE_PING;
            return LayerManager<T>.Instance.NextSend(ping_pkg, ref this);
        }
        public bool Initialize()
        {
            TYPE_OFFSET = HeadBegin;
            CHANNEL_OFFSET = TYPE_OFFSET + TYPE_SIZE;
            CONN_DESC_OFFSET = CHANNEL_OFFSET + CHANNEL_SIZE;
            ERR_CODE_OFFSET = CONN_DESC_OFFSET + CONN_DESC_SIZE;
            NEXT_PKG_OFFSET = ERR_CODE_OFFSET + ERR_CODE_SIZE;
            if (max_channels == 0)
                max_channels = MAX_CHANNELS_DEFAULT;
            if (input_buffer_size == 0)
                input_buffer_size = INPUT_BUFFER_SIZE_DEFAULT;
            if (output_buffer_size == 0)
                output_buffer_size = OUTPUT_BUFFER_SIZE_DEFAULT;
            if (timeout == 0.0f)
                timeout = TIMEOUT_DEFAULT;
            if (connect_timeout == 0.0f)
                connect_timeout = CONNECT_TIMEOUT_DEFAULT;
            if (ping_period == 0.0f)
                ping_period = PING_PERIOD_DEFAULT;
            if (reliable_repeat_period == 0.0f)
                reliable_repeat_period = RELIABLE_REPEAT_PERIOD_DEFAULT;
            if (jobs == 0)
                jobs = JOBS_DEFAULT;
            int channels_size = UnsafeUtility.SizeOf<Channel>() * max_channels;
            pChannels = (Channel*)UnsafeUtility.Malloc(channels_size, UnsafeUtility.AlignOf<Channel>(), Allocator.Persistent);
            UnsafeUtility.MemClear(pChannels, channels_size);
            int in_buffer_size = UnsafeUtility.SizeOf<InReleablePackage>() * input_buffer_size;
            int out_buffer_size = UnsafeUtility.SizeOf<OutReleablePackage>() * output_buffer_size;
            for (int i = 0; i < max_channels; i++)
            {
                pChannels[i].input_buffer = (InReleablePackage*)UnsafeUtility.Malloc(in_buffer_size, UnsafeUtility.AlignOf<InReleablePackage>(), Allocator.Persistent);
                pChannels[i].output_buffer = (OutReleablePackage*)UnsafeUtility.Malloc(out_buffer_size, UnsafeUtility.AlignOf<OutReleablePackage>(), Allocator.Persistent);
                UnsafeUtility.MemClear(pChannels[i].input_buffer, in_buffer_size);
                UnsafeUtility.MemClear(pChannels[i].output_buffer, out_buffer_size);
                pChannels[i].output_queue = new Multithreading.ConcurrentQueue<OutputPackage>(true);
            }
            connections = new ConcurrentDictionary<SharedObject<Address>, bool, ConnectionFinalizer>(16);
            output_queue = new ConcurrentQueue<OutputPackage>(true);
            IsCreate = true;
            Debug.Log("Connection layer initialize successuly");
            return true;
        }
        public bool Receive<T>(InputPackage package) where T : struct, ILayerIdentity
        {
            if (package.buffer.Length < HeadBegin + TYPE_SIZE)
            {
                package.Release();
                return false;
            }
            PKG_TYPE pkg_type = (PKG_TYPE)package.buffer.ReadByte(TYPE_OFFSET);
            if((pkg_type != PKG_TYPE.PING && package.buffer.Length < HeadBegin + HEAD_SIZE) ||
               (pkg_type == PKG_TYPE.PING && package.buffer.Length < HeadBegin + HEAD_SIZE_PING))
            {
                package.Release();
                return false;
            }
            bool result = true;
            switch (pkg_type)
            {
                case PKG_TYPE.CONNECT:
                    {
                        Address addr = package.address.Copy();
                        SharedObject<Address> shared_addr = new SharedObject<Address>(addr);
                        if (TryAddNewConnection(ref shared_addr))
                        {
                            int out_channel = package.buffer.ReadInt(CHANNEL_OFFSET);
                            short out_conn_desc = package.buffer.ReadShort(CONN_DESC_OFFSET);
                            int next_out_package = package.buffer.ReadInt(NEXT_PKG_OFFSET);
                            if (TryIncreaseChannelCounter())
                            {
                                int id = ReserveChannel(ref shared_addr);
                                ref Channel channel = ref pChannels[id];
                                channel.output_channel = out_channel;
                                channel.output_connection_desc = out_conn_desc;
                                channel.next_output_package = next_out_package;
                                if (AcceptQueryEnable)
                                {
                                    ChangeChannelStatus(id, STATUS.INIT, STATUS.ACCEPT);
                                    Address ch_addr = CreateChannelInfoFromEndPoint(addr, CommonData.in_package_alloc.Addr_size);
                                    ref ChannelInfo ch_info = ref UnsafeUtility.AsRef<ChannelInfo>(ch_addr.Pointer);
                                    ch_info.channel = id;
                                    ch_info.msg_type = MSG_TYPE.ACCEPT;
                                    ch_info.err_code = ERR_CODE.NONE;
                                    if (package.IsReleasable)
                                        package.address.Dispose();
                                    package.address = ch_addr;
                                    result = LayerManager<T>.Instance.NextReceive(package, ref this);
                                    if (!package.IsReleasable)
                                        ch_addr.Dispose();
                                }
                                else
                                {
                                    package.Release();
                                    ChangeChannelStatus(id, STATUS.INIT, STATUS.CONNECTING);
                                    OutputPackage answer_pkg = CommonData.out_package_alloc.Alloc();
                                    UnsafeUtility.MemCpy(answer_pkg.address.Pointer, addr.Pointer, addr.Length);
                                    answer_pkg.address.Length = addr.Length;
                                    answer_pkg.buffer.Write((byte)PKG_TYPE.ANSWER, TYPE_OFFSET);
                                    answer_pkg.buffer.Write(out_channel, CHANNEL_OFFSET);
                                    answer_pkg.buffer.Write(out_conn_desc, CONN_DESC_OFFSET);
                                    answer_pkg.buffer.Write((byte)ERR_CODE.NONE, ERR_CODE_OFFSET);
                                    answer_pkg.buffer.Write(0, NEXT_PKG_OFFSET);
                                    answer_pkg.buffer.Length = HeadBegin + HeadSize;
                                    answer_pkg.buffer.Write(id);
                                    answer_pkg.buffer.Write(channel.input_connection_desc);
                                    answer_pkg.buffer.Write(channel.next_input_package);
                                    result = LayerManager<T>.Instance.NextSend(answer_pkg, ref this);
                                }
                                DecreaseChannelReferences<T>(id);
                            }
                            else
                            {
                                package.Release();
                                OutputPackage answer_pkg = CommonData.out_package_alloc.Alloc();
                                UnsafeUtility.MemCpy(answer_pkg.address.Pointer, addr.Pointer, addr.Length);
                                answer_pkg.address.Length = addr.Length;
                                answer_pkg.buffer.Write((byte)PKG_TYPE.ANSWER, TYPE_OFFSET);
                                answer_pkg.buffer.Write(out_channel, CHANNEL_OFFSET);
                                answer_pkg.buffer.Write(out_conn_desc, CONN_DESC_OFFSET);
                                answer_pkg.buffer.Write((byte)ERR_CODE.MAX_CHANNEL_LIMIT, ERR_CODE_OFFSET);
                                answer_pkg.buffer.Write(0, NEXT_PKG_OFFSET);
                                answer_pkg.buffer.Length = HeadBegin + HeadSize;
                                DisableConnection(ref shared_addr);
                                result = LayerManager<T>.Instance.NextSend(answer_pkg, ref this);
                                RemoveConnection(ref shared_addr);
                            }
                        }
                        else
                            package.Release();
                        shared_addr.Dispose();
                    }
                    break;

                case PKG_TYPE.ANSWER:
                    {
                        int id = package.buffer.ReadInt(CHANNEL_OFFSET);
                        short in_conn_desc = package.buffer.ReadShort(CONN_DESC_OFFSET);
                        ERR_CODE err_code = (ERR_CODE)package.buffer.ReadByte(ERR_CODE_OFFSET);
                        int next_in_message = package.buffer.ReadInt(NEXT_PKG_OFFSET);
                        if (err_code == ERR_CODE.NONE && package.buffer.Length < HeadBegin + HeadSize + CHANNEL_SIZE + CONN_DESC_SIZE + NEXT_PKG_SIZE)
                        {
                            package.Release();
                            return false;
                        }
                        if (IncreaseChannelReferences(id))
                        {
                            if (CheckAndUpdateChannel<T>(id, in_conn_desc, package.address, false))
                            {
                                ref Channel channel = ref pChannels[id];
                                STATUS next_status = err_code == ERR_CODE.NONE ? STATUS.PREPARE : STATUS.DISCONNECTED;
                                if (ChangeChannelStatus(id, STATUS.SYNC, next_status))
                                {
                                    Address addr = CreateChannelInfoFromEndPoint(package.address, CommonData.in_package_alloc.Addr_size);
                                    ref ChannelInfo info = ref UnsafeUtility.AsRef<ChannelInfo>(addr.Pointer);
                                    info.channel = id;
                                    info.msg_type = MSG_TYPE.CONNECT;
                                    info.err_code = err_code;

                                    if (err_code == ERR_CODE.NONE)
                                    {
                                        package.buffer.Context = HeadBegin + HeadSize;
                                        channel.output_channel = package.buffer.ReadInt();
                                        channel.output_connection_desc = package.buffer.ReadShort();
                                        channel.next_output_package = package.buffer.ReadInt();
                                        ChangeChannelStatus(id, STATUS.PREPARE, STATUS.CONNECTED);
                                        InputPackage notice_pkg = new InputPackage();
                                        notice_pkg.address = addr;
                                        result = LayerManager<T>.Instance.NextReceive(notice_pkg, ref this);
                                        // send ping message
                                        result = result && SendPingPackage<T>(ref channel);
                                        Interlocked.Exchange(ref channel.send_timestamp, current_time);
                                    }
                                    else
                                        channel.report_pkg.address = addr;
                                }
                                package.Release();
                            }
                            else
                                package.Release();
                            DecreaseChannelReferences<T>(id);
                        }
                        else
                            package.Release();
                    }
                        break;

                case PKG_TYPE.DISCONNECT:
                case PKG_TYPE.RELIABLE:
                    {
                        int id = package.buffer.ReadInt(CHANNEL_OFFSET);
                        short in_conn_desc = package.buffer.ReadShort(CONN_DESC_OFFSET);
                        int pkg_id = package.buffer.ReadInt(NEXT_PKG_OFFSET);
                        if (IncreaseChannelReferences(id))
                        {
                            if (CheckAndUpdateChannel<T>(id, in_conn_desc, package.address, true))
                            {
                                ref Channel channel = ref pChannels[id];
                                int pkg_pos = pkg_id % input_buffer_size;
                                InPackageInfo info = new InPackageInfo { value = Interlocked.Read(ref channel.input_buffer[pkg_pos].info.value)};
                                InPackageInfo new_info = new InPackageInfo { status = IN_PKG_STATUS.INIT, id = pkg_id };
                                int distance = pkg_id - channel.next_input_package;
                                if (distance >= 0 )
                                {
                                    if(distance < input_buffer_size && 
                                        info.status == IN_PKG_STATUS.NONE && 
                                        Interlocked.CompareExchange(ref channel.input_buffer[pkg_pos].info.value, new_info.value, info.value) == info.value)
                                    {
                                        SendNoticePackage<T>(ref channel, pkg_id);
                                        Interlocked.Exchange(ref channel.send_timestamp, current_time);
                                        if (package.IsReleasable)
                                            channel.input_buffer[pkg_pos].package = package;
                                        else
                                            channel.input_buffer[pkg_pos].package = package.Copy(true);
                                        new_info.status = IN_PKG_STATUS.READY;
                                        Interlocked.Exchange(ref channel.input_buffer[pkg_pos].info.value, new_info.value);

                                        int next_input_package = channel.next_input_package;
                                        pkg_pos = next_input_package % input_buffer_size;
                                        info = new InPackageInfo { value = Interlocked.Read(ref channel.input_buffer[pkg_pos].info.value) };
                                        new_info = new InPackageInfo { status = IN_PKG_STATUS.PROCESSED, id = info.id };
                                        while (channel.state.status == STATUS.CONNECTED && 
                                              next_input_package == info.id &&
                                              info.status == IN_PKG_STATUS.READY &&
                                              Interlocked.CompareExchange(ref channel.input_buffer[pkg_pos].info.value, new_info.value, info.value) == info.value)
                                        {
                                            InputPackage next_package = channel.input_buffer[pkg_pos].package;
                                            PKG_TYPE next_pkg_type = (PKG_TYPE)next_package.buffer.ReadByte(TYPE_OFFSET);
                                            ref ChannelInfo ch_info = ref UnsafeUtility.AsRef<ChannelInfo>(next_package.address.Pointer);
                                            ch_info.af = UnsafeUtility.AsRef<IPEndPoint>(channel.addr.Object.Pointer).AF;
                                            ch_info.channel = id;
                                            ch_info.err_code = ERR_CODE.NONE;
                                            if (next_pkg_type == PKG_TYPE.RELIABLE)
                                            {
                                                ch_info.msg_type = MSG_TYPE.RELIABLE;
                                                result = result && LayerManager<T>.Instance.NextReceive(next_package, ref this);
                                            }
                                            else // DISCONNECT
                                            {
                                                if (ChangeChannelStatus(id, STATUS.CONNECTED, STATUS.DISCONNECTED))
                                                {
                                                    ch_info.msg_type = MSG_TYPE.DISCONNECT;
                                                    channel.report_pkg = next_package;
                                                }
                                            }
                                            Interlocked.Increment(ref channel.next_input_package);
                                            new_info = new InPackageInfo { status = IN_PKG_STATUS.NONE, id = channel.input_buffer[pkg_pos].info.id};
                                            Interlocked.Exchange(ref channel.input_buffer[pkg_pos].info.value, new_info.value);
                                            next_input_package++;
                                            pkg_pos = next_input_package % input_buffer_size;
                                            info = new InPackageInfo { value = Interlocked.Read(ref channel.input_buffer[pkg_pos].info.value) };
                                            new_info = new InPackageInfo { status = IN_PKG_STATUS.PROCESSED, id = info.id };
                                        }
                                    }
                                    else
                                        package.Release();
                                }
                                else
                                {
                                    SendNoticePackage<T>(ref channel, pkg_id);
                                    Interlocked.Exchange(ref channel.send_timestamp, current_time);
                                    package.Release();
                                }
                            }
                            else
                                package.Release();
                            DecreaseChannelReferences<T>(id);
                        }
                        else
                            package.Release();
                    }
                    break;

                case PKG_TYPE.NON_RELIABLE:
                    {
                        int id = package.buffer.ReadInt(CHANNEL_OFFSET);
                        short in_conn_desc = package.buffer.ReadShort(CONN_DESC_OFFSET);
                        package.buffer.Context = HeadBegin + HEAD_SIZE;
                        if (IncreaseChannelReferences(id))
                        {
                            if (CheckAndUpdateChannel<T>(id, in_conn_desc, package.address, true))
                            {
                                ref Channel channel = ref pChannels[id];
                                Address addr;
                                if(!package.IsReleasable)
                                {
                                    addr = new Address(package.address.Capacity, package.address.Allocator);
                                    addr.Length = UnsafeUtility.SizeOf<ChannelInfo>();
                                    package.address = addr;
                                }
                                else
                                    addr = package.address;
                                ref ChannelInfo ch_info = ref UnsafeUtility.AsRef<ChannelInfo>(package.address.Pointer);
                                ch_info.af = UnsafeUtility.AsRef<IPEndPoint>(channel.addr.Object.Pointer).AF;
                                ch_info.channel = id;
                                ch_info.msg_type = MSG_TYPE.NON_RELIABLE;
                                ch_info.err_code = ERR_CODE.NONE;
                                result = LayerManager<T>.Instance.NextReceive(package, ref this);
                                if (!package.IsReleasable)
                                    addr.Dispose();
                            }
                            else
                                package.Release();
                            DecreaseChannelReferences<T>(id);
                        }
                        else
                            package.Release();
                    }
                    break;

                case PKG_TYPE.PING:
                    {
                        int id = package.buffer.ReadInt(CHANNEL_OFFSET);
                        short in_conn_desc = package.buffer.ReadShort(CONN_DESC_OFFSET);
                        if (IncreaseChannelReferences(id))
                        {
                            CheckAndUpdateChannel<T>(id, in_conn_desc, package.address, true);
                            DecreaseChannelReferences<T>(id);
                        }
                        package.Release();
                    }
                    break;

                case PKG_TYPE.NOTICE:
                    {
                        int id = package.buffer.ReadInt(CHANNEL_OFFSET);
                        short in_conn_desc = package.buffer.ReadShort(CONN_DESC_OFFSET);
                        int pkg_id = package.buffer.ReadInt(NEXT_PKG_OFFSET);
                        if (IncreaseChannelReferences(id))
                        {
                            if (CheckAndUpdateChannel<T>(id, in_conn_desc, package.address, false))
                            {
                                ref Channel channel = ref pChannels[id];
                                int pkg_pos = pkg_id % output_buffer_size;
                                OutPackageInfo old_pkg_info;
                                OutPackageInfo tmp_pkg_info = channel.output_buffer[pkg_pos].info;
                                do
                                {
                                    if (tmp_pkg_info.id != pkg_id || (tmp_pkg_info.status != OUT_PKG_STATUS.PROCESSED && tmp_pkg_info.status != OUT_PKG_STATUS.WAIT_NOTICE))
                                        break;
                                    old_pkg_info = tmp_pkg_info;
                                    tmp_pkg_info.status = OUT_PKG_STATUS.DELETING;
                                }
                                while ((tmp_pkg_info.value = Interlocked.CompareExchange(ref channel.output_buffer[pkg_pos].info.value, tmp_pkg_info.value, old_pkg_info.value)) != old_pkg_info.value);
                                if (tmp_pkg_info.id == pkg_id && tmp_pkg_info.status == OUT_PKG_STATUS.WAIT_NOTICE)
                                {
                                    channel.output_buffer[pkg_pos].package.Release();
                                    tmp_pkg_info.status = OUT_PKG_STATUS.NONE;
                                    Interlocked.Exchange(ref channel.output_buffer[pkg_pos].info.value, tmp_pkg_info.value);
                                }
                            }
                            DecreaseChannelReferences<T>(id);
                        }
                        package.Release();
                    }
                    break;

                default:
                    package.Release();
                    result = false;
                    break;
            }
            return result;
        }
        public bool Send<T>(OutputPackage package) where T : struct, ILayerIdentity
        {
            ref ChannelInfo info = ref UnsafeUtility.AsRef<ChannelInfo>(package.address.Pointer);
            OutputPackage out_package = package.IsReleasable ? package : package.Copy(true);
            if(info.msg_type == MSG_TYPE.RELIABLE || info.msg_type == MSG_TYPE.DISCONNECT)
            {
                int id = info.channel;
                if (IncreaseChannelReferences(id))
                {
                    ref Channel channel = ref pChannels[id];
                    if (channel.state.status == STATUS.CONNECTED || channel.state.status == STATUS.WAIT)
                        channel.output_queue.push(out_package);
                    else
                        out_package.Release();
                    DecreaseChannelReferences<T>(id);
                }
                else
                    out_package.Release();
            }
            else
                output_queue.push(out_package);
            return true;
        }
        public bool SendNonReliablePackage<T>(OutputPackage package) where T : struct, ILayerIdentity
        {
            ref ChannelInfo info = ref UnsafeUtility.AsRef<ChannelInfo>(package.address.Pointer);
            bool result = true;
            switch(info.msg_type)
            {
                case MSG_TYPE.CONNECT:
                    {
                        Address addr = CreateEndPointFromChannelInfo(package.address, CommonData.out_package_alloc.Addr_size);
                        SharedObject<Address> shared_addr = new SharedObject<Address>(addr);
                        if (TryAddNewConnection(ref shared_addr))
                        {
                            if (TryIncreaseChannelCounter())
                            {
                                int id = ReserveChannel(ref shared_addr);
                                ref Channel channel = ref pChannels[id];
                                ChangeChannelStatus(id, STATUS.INIT, STATUS.SYNC);
                                package.buffer.Write((byte)PKG_TYPE.CONNECT, TYPE_OFFSET);
                                package.buffer.Write(id, CHANNEL_OFFSET);
                                package.buffer.Write(channel.input_connection_desc, CONN_DESC_OFFSET);
                                package.buffer.Write((byte)ERR_CODE.NONE, ERR_CODE_OFFSET);
                                package.buffer.Write(channel.next_input_package, NEXT_PKG_OFFSET);
                                if (package.IsReleasable)
                                    package.address.Dispose();
                                package.address = addr.Copy();
                                result = LayerManager<T>.Instance.NextSend(package, ref this);
                                if (!package.IsReleasable)
                                    addr.Dispose();
                                DecreaseChannelReferences<T>(id);
                            }
                            else
                            {
                                InputPackage report_pkg = new InputPackage();
                                report_pkg.address = package.address.Copy();
                                ref ChannelInfo report_info = ref UnsafeUtility.AsRef<ChannelInfo>(report_pkg.address.Pointer);
                                report_info.err_code = ERR_CODE.MAX_CHANNEL_LIMIT;
                                package.Release();
                                DisableConnection(ref shared_addr);
                                result = LayerManager<T>.Instance.NextReceive(report_pkg, ref this);
                                RemoveConnection(ref shared_addr);
                            }
                        }
                        else
                            package.Release();
                        shared_addr.Dispose();
                    }
                    break;

                case MSG_TYPE.ACCEPT:
                    {
                        int id = info.channel;
                        if (IncreaseChannelReferences(id))
                        {
                            ref Channel channel = ref pChannels[id];
                            STATUS next_status = info.err_code == ERR_CODE.NONE ? STATUS.PREPARE : STATUS.DISCONNECTED;
                            if (ChangeChannelStatus(id, STATUS.ACCEPT, next_status))
                            {
                                if (info.err_code == ERR_CODE.NONE)
                                {
                                    channel.receive_timestamp = current_time;
                                    channel.send_timestamp = current_time;
                                    ChangeChannelStatus(id, STATUS.PREPARE, STATUS.CONNECTING);
                                }
                                package.buffer.Write((byte)PKG_TYPE.ANSWER, TYPE_OFFSET);
                                package.buffer.Write(channel.output_channel, CHANNEL_OFFSET);
                                package.buffer.Write(channel.output_connection_desc, CONN_DESC_OFFSET);
                                package.buffer.Write((byte)info.err_code, ERR_CODE_OFFSET);
                                package.buffer.Write(0, NEXT_PKG_OFFSET);
                                package.buffer.Length = HeadBegin + HeadSize;
                                if (info.err_code == ERR_CODE.NONE)
                                {
                                    package.buffer.Write(id);
                                    package.buffer.Write(channel.input_connection_desc);
                                    package.buffer.Write(channel.next_input_package);
                                }
                                if (package.IsReleasable)
                                {
                                    UnsafeUtility.MemCpy(package.address.Pointer, channel.addr.Object.Pointer, channel.addr.Object.Length);
                                    package.address.Length = channel.addr.Object.Length;
                                }
                                else
                                    package.address = channel.addr.Object;
                                result = LayerManager<T>.Instance.NextSend(package, ref this);
                            }
                            else
                                package.Release();
                            DecreaseChannelReferences<T>(id);
                        }
                        else
                            package.Release();
                    }
                    break;

                case MSG_TYPE.NON_RELIABLE:
                    {
                        int id = info.channel;
                        if (IncreaseChannelReferences(id))
                        {
                            ref Channel channel = ref pChannels[id];
                            if (channel.state.status == STATUS.CONNECTED || channel.state.status == STATUS.WAIT)
                            {
                                package.buffer.Write((byte)PKG_TYPE.NON_RELIABLE, TYPE_OFFSET);
                                package.buffer.Write(channel.output_channel, CHANNEL_OFFSET);
                                package.buffer.Write(channel.output_connection_desc, CONN_DESC_OFFSET);
                                package.buffer.Write((byte)ERR_CODE.NONE, ERR_CODE_OFFSET);
                                package.buffer.Write(0, NEXT_PKG_OFFSET);
                                if (package.IsReleasable)
                                {
                                    UnsafeUtility.MemCpy(package.address.Pointer, channel.addr.Object.Pointer, channel.addr.Object.Length);
                                    package.address.Length = channel.addr.Object.Length;
                                }
                                else
                                    package.address = channel.addr.Object;
                                Interlocked.Exchange(ref channel.send_timestamp, current_time);
                                result = LayerManager<T>.Instance.NextSend(package, ref this);
                            }
                            else
                                package.Release();
                            DecreaseChannelReferences<T>(id);
                        }
                        else
                            package.Release();
                    }
                    break;

                default:
                    package.Release();
                    result = false;
                    break;
            }
            return result;
        }

        public bool SendReliablePackages<T>(int id) where T : struct, ILayerIdentity
        {
            bool result = true;
            if (IncreaseChannelReferences(id))
            {
                ref Channel channel = ref pChannels[id];
                if (channel.state.status == STATUS.CONNECTED || channel.state.status == STATUS.WAIT)
                {
                    OutPackageInfo new_pkg_info = new OutPackageInfo();
                    OutPackageInfo old_pkg_info = new OutPackageInfo();
                    while (channel.send_enable)
                    {
                        int pkg_id = channel.next_output_package;
                        int pkg_pos = pkg_id % output_buffer_size;
                        OutPackageInfo pkg_info = new OutPackageInfo { value = Interlocked.Read(ref channel.output_buffer[pkg_pos].info.value) };
                        int len_queue = channel.output_queue.Counter;
                        if (pkg_id != channel.next_output_package)
                            continue;
                        if (len_queue == 0 || pkg_info.status == OUT_PKG_STATUS.WAIT_NOTICE || channel.state.status == STATUS.DISCONNECTED)
                        {
                            channel.send_enable = false;
                            break;
                        }
                        if (pkg_info.status == OUT_PKG_STATUS.NONE)
                        {
                            new_pkg_info = new OutPackageInfo { status = OUT_PKG_STATUS.INIT, id = pkg_id };
                            if (Interlocked.CompareExchange(ref channel.output_buffer[pkg_pos].info.value, new_pkg_info.value, pkg_info.value) == pkg_info.value)
                            {
                                OutputPackage out_pkg = channel.output_queue.pop();
                                ref ChannelInfo ch_info = ref UnsafeUtility.AsRef<ChannelInfo>(out_pkg.address.Pointer);
                                if (ch_info.msg_type == MSG_TYPE.DISCONNECT)
                                {
                                    while (channel.state.status == STATUS.WAIT)
                                        Utils.Yield();
                                    if (ChangeChannelStatus(id, STATUS.CONNECTED, STATUS.DISCONNECTED))
                                        channel.report_pkg.address = out_pkg.address.Copy();
                                }
                                Interlocked.Increment(ref channel.next_output_package);
                                PKG_TYPE pkg_type = ch_info.msg_type == MSG_TYPE.RELIABLE ? PKG_TYPE.RELIABLE : PKG_TYPE.DISCONNECT;
                                out_pkg.buffer.Write((byte)pkg_type, TYPE_OFFSET);
                                out_pkg.buffer.Write(channel.output_channel, CHANNEL_OFFSET);
                                out_pkg.buffer.Write(channel.output_connection_desc, CONN_DESC_OFFSET);
                                out_pkg.buffer.Write((byte)ERR_CODE.NONE, ERR_CODE_OFFSET);
                                out_pkg.buffer.Write(pkg_id, NEXT_PKG_OFFSET);
                                UnsafeUtility.MemCpy(out_pkg.address.Pointer, channel.addr.Object.Pointer, channel.addr.Object.Length);
                                out_pkg.address.Length = channel.addr.Object.Length;
                                channel.output_buffer[pkg_pos].package = out_pkg;
                                new_pkg_info.status = OUT_PKG_STATUS.PROCESSED;
                                Interlocked.Exchange(ref channel.output_buffer[pkg_pos].info.value, new_pkg_info.value);
                                out_pkg.IsReleasable = false;
                                result = result && LayerManager<T>.Instance.NextSend(out_pkg, ref this);
                                Interlocked.Exchange(ref channel.send_timestamp, current_time);
                                channel.output_buffer[pkg_pos].info.timestamp = current_time;
                                old_pkg_info = new_pkg_info;
                                new_pkg_info.status = OUT_PKG_STATUS.WAIT_NOTICE;
                                if (Interlocked.CompareExchange(ref channel.output_buffer[pkg_pos].info.value, new_pkg_info.value, old_pkg_info.value) != old_pkg_info.value)
                                {
                                    channel.output_buffer[pkg_pos].package.Release();
                                    new_pkg_info.status = OUT_PKG_STATUS.NONE;
                                    Interlocked.Exchange(ref channel.output_buffer[pkg_pos].info.value, new_pkg_info.value);
                                }
                            }
                        }
                    }
                    
                    for (int i=0; i<output_buffer_size; i++)
                    {
                        old_pkg_info.timestamp = channel.output_buffer[i].info.timestamp;
                        old_pkg_info.value = Interlocked.Read(ref channel.output_buffer[i].info.value);
                        if(old_pkg_info.status == OUT_PKG_STATUS.WAIT_NOTICE && current_time - old_pkg_info.timestamp > reliable_repeat_period)
                        {
                            new_pkg_info = old_pkg_info;
                            new_pkg_info.status = OUT_PKG_STATUS.PROCESSED;
                            if (Interlocked.CompareExchange(ref channel.output_buffer[i].info.value, new_pkg_info.value, old_pkg_info.value) == old_pkg_info.value)
                            {
                                OutputPackage out_pkg = channel.output_buffer[i].package;
                                out_pkg.IsReleasable = false;
                                result = result && LayerManager<T>.Instance.NextSend(out_pkg, ref this);
                                Interlocked.Exchange(ref channel.send_timestamp, current_time);
                                Interlocked.Exchange(ref channel.output_buffer[i].info.timestamp, current_time);
                                old_pkg_info = new_pkg_info;
                                new_pkg_info.status = OUT_PKG_STATUS.WAIT_NOTICE;
                                if (Interlocked.CompareExchange(ref channel.output_buffer[i].info.value, new_pkg_info.value, old_pkg_info.value) != old_pkg_info.value)
                                {
                                    channel.output_buffer[i].package.Release();
                                    new_pkg_info.status = OUT_PKG_STATUS.NONE;
                                    Interlocked.Exchange(ref channel.output_buffer[i].info.value, new_pkg_info.value);
                                }
                            }
                        }
                    }
                }
                DecreaseChannelReferences<T>(id);
            }
            return result;
        }

        private void UpdateChannel<T>(int id) where T : struct, ILayerIdentity
        {
            if (IncreaseChannelReferences(id))
            {
                ref Channel channel = ref pChannels[id];
                STATUS status = channel.state.status;
                switch (status)
                {
                    case STATUS.SYNC:
                    case STATUS.CONNECTING:
                        {
                            if(current_time - channel.receive_timestamp > connect_timeout && ChangeChannelStatus(id, status, STATUS.DISCONNECTED))
                            {
                                Address addr = CreateChannelInfoFromEndPoint(channel.addr.Object, CommonData.in_package_alloc.Addr_size);
                                ref ChannelInfo info = ref UnsafeUtility.AsRef<ChannelInfo>(addr.Pointer);
                                info.channel = id;
                                info.msg_type = MSG_TYPE.CONNECT;
                                info.err_code = ERR_CODE.TIMEOUT;
                                channel.report_pkg.address = addr;
                            }
                        }
                        break;
                    case STATUS.CONNECTED:
                        {
                            if(current_time - channel.send_timestamp > ping_period)
                            {
                                SendPingPackage<T>(ref channel);
                                Interlocked.Exchange(ref channel.send_timestamp, current_time);
                            }
                            if (current_time - channel.receive_timestamp > timeout && ChangeChannelStatus(id, status, STATUS.DISCONNECTED))
                            {
                                Address addr = CreateChannelInfoFromEndPoint(channel.addr.Object, CommonData.in_package_alloc.Addr_size);
                                ref ChannelInfo info = ref UnsafeUtility.AsRef<ChannelInfo>(addr.Pointer);
                                info.channel = id;
                                info.msg_type = MSG_TYPE.DISCONNECT;
                                info.err_code = ERR_CODE.TIMEOUT;
                                channel.report_pkg.address = addr;
                            }
                        }
                        break;
                }
                DecreaseChannelReferences<T>(id);
            }
        }
        [BurstCompile]
        private struct UpdateJob<T> : IJob where T : struct, ILayerIdentity
        {
            [NativeDisableUnsafePtrRestriction]
            public ConnectionLayer* ptr;
            [BurstCompile]
            public void Execute()
            {
                int id;
                while((id = Interlocked.Increment(ref ptr->update_counter) - 1) < ptr->max_channels)
                    ptr->UpdateChannel<T>(id);
            }
        }
        [BurstCompile]
        private struct SendJob<T> : IJob where T : struct, ILayerIdentity
        {
            [NativeDisableUnsafePtrRestriction]
            public ConnectionLayer* ptr;
            [BurstCompile]
            public void Execute()
            {
                //----non-reliable------
                while(ptr->send_enable)
                {
                    OutputPackage package;
                    if (ptr->output_queue.pop(out package))
                        ptr->SendNonReliablePackage<T>(package);
                    else
                        ptr->send_enable = false;
                }
                //-------reliable-------
                for (int i = 0; i < ptr->max_channels; i++)
                    ptr->SendReliablePackages<T>(i);

            }
        }
        public void Update<T>() where T : struct, ILayerIdentity
        {
            current_time = Time.time;
            for (int i = 0; i < max_channels; i++)
                pChannels[i].send_enable = true;
            if (Async)
            {
                send_enable = true;
                JobHandle[] handles = new JobHandle[jobs];
                for (int i = 0; i < jobs; i++)
                {
                    SendJob<T> job = new SendJob<T>();
                    job.ptr = (ConnectionLayer*)UnsafeUtility.AddressOf(ref this);
                    handles[i] = job.Schedule();
                }
                for (int i = 0; i < jobs; i++)
                    handles[i].Complete();

                update_counter = 0;
                handles = new JobHandle[jobs];
                for (int i = 0; i < jobs; i++)
                {
                    UpdateJob<T> job = new UpdateJob<T>();
                    job.ptr = (ConnectionLayer*)UnsafeUtility.AddressOf(ref this);
                    handles[i] = job.Schedule();
                }
                for (int i = 0; i < jobs; i++)
                    handles[i].Complete();
            }
            else
            {
                OutputPackage package;
                while (output_queue.pop(out package))
                    SendNonReliablePackage<T>(package);
                for (int i = 0; i < max_channels; i++)
                {
                    SendReliablePackages<T>(i);
                    UpdateChannel<T>(i);
                }
            }
        }

        public void Dispose()
        {
            if(IsCreate)
            {
                for (int i = 0; i < max_channels; i++)
                    ReleaseChannel(i);
                UnsafeUtility.Free(pChannels, Allocator.Persistent);
                connections.Dispose();
                if (output_queue.IsCreate)
                {
                    OutputPackage package;
                    while (output_queue.pop(out package))
                        package.Dispose();
                    output_queue.Dispose();
                }
                IsCreate = false;
            }
            Debug.Log("Connection layer dispose");
        }
    }
}
