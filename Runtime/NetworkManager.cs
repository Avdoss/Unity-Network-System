using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Collections.LowLevel.Unsafe;
using Transport;

namespace Network
{
    public class ComponentException : Exception
    {
        public ComponentException() { }
        public ComponentException(string message) : base(message) { }
        public ComponentException(string message, Exception inner) : base(message, inner) { }
    }

    [Serializable]
    public class AsyncData
    {
        public bool enable;
        [Min(1)]
        public int jobs = 1;
    }

    [ExecuteAlways]
    public class NetworkManager : MonoBehaviour
    {
        private class InternalReceiveData : ReceiveData
        {
            public ref InputPackage Package { get { return ref this.package; } }
            public unsafe ref ChannelInfo Channel { get { return ref UnsafeUtility.AsRef<ChannelInfo>(package.address.Pointer); } }
        }

        private class InternalSendData : SendData
        {
            public ref OutputPackage Package { get { return ref this.package; } }
            public unsafe ref ChannelInfo Channel { get { return ref UnsafeUtility.AsRef<ChannelInfo>(package.address.Pointer); } }
        }

        private static readonly int MSG_TYPE_SIZE = sizeof(short);
        private static NetworkManager instance = null;

        public delegate byte AuthorizationHandler(int id, ref ChannelInfo channel, ReceiveData data);
        public delegate void ConnectionHandler(int id, ref ChannelInfo channel);
        public delegate void DisconnectionHandler(int id, byte err_code, ReceiveData data);
        public delegate void NetworkMessageHandler(int id, ReceiveData data);

        public event AuthorizationHandler AuthorizationEvent;
        public event ConnectionHandler ConnectionEvent;
        public event DisconnectionHandler DisconnectionEvent;

        private Dictionary<short, NetworkMessageHandler> message_handlers;
        private Host<SocketLayer, SegmentationLayer, ConnectionLayer> host;
        private int MSG_TYPE_OFFSET;
        private int DATA_OFFSET;

        public bool authorizationStage = false;
        public ushort port = 0;
        public AsyncData asyncMode;

        public static NetworkManager Singleton { get { return instance; } }

        void Awake()
        {
            // -------- ALWAYS -------
            if (instance == null)
                instance = this;
            else
            {
#if UNITY_EDITOR
                if (!EditorApplication.isPlaying)
                    EditorApplication.delayCall += () => DestroyImmediate(this);
                else
                    Destroy(this);
#else
                Destroy(this);
#endif
                throw new ComponentException("NetworkManager component can be loaded in a single instance");
            }
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
                return;
#endif
            // ------- PLAY MODE -----
            DontDestroyOnLoad(gameObject);

            message_handlers = new Dictionary<short, NetworkMessageHandler>();
            // --- Transport settings ----
            host = new Host<SocketLayer, SegmentationLayer, ConnectionLayer>();
            host.Layer0.Async = asyncMode.enable;
            host.Layer0.Jobs = asyncMode.jobs;
            host.Layer0.Port = port;
            host.Layer2.Async = asyncMode.enable;
            host.Layer2.Jobs = asyncMode.jobs;
            host.Layer2.AcceptQueryEnable = authorizationStage;
            host.Init();
            // ---------------------------
            MSG_TYPE_OFFSET = host.Layer0.HeadSize + host.Layer1.HeadSize + host.Layer2.HeadSize;
            DATA_OFFSET = MSG_TYPE_OFFSET + MSG_TYPE_SIZE;

            // --- Transport settings ----
            /*host = new Host<SocketLayer, EmulationLayer, SegmentationLayer, ConnectionLayer>();
            host.Layer0.Async = asyncMode.enable;
            host.Layer0.Jobs = asyncMode.jobs;
            host.Layer0.Port = port;
            host.Layer1.DropFactor = 0.0f;
            host.Layer1.DoubleFactor = 0.0f;
            host.Layer1.MinDelay = 0.0f;
            host.Layer1.MaxDelay = 0.7f;
            host.Layer3.Async = asyncMode.enable;
            host.Layer3.Jobs = asyncMode.jobs;
            host.Layer3.AcceptQueryEnable = authorizationStage;
            host.Init();
            // ---------------------------
            MSG_TYPE_OFFSET = host.Layer0.HeadSize + host.Layer1.HeadSize + host.Layer2.HeadSize + host.Layer3.HeadSize;
            DATA_OFFSET = MSG_TYPE_OFFSET + MSG_TYPE_SIZE;*/
        }
        public void RegisterHandler(short msg_type, NetworkMessageHandler message_handler)
        {
            if (message_handlers.ContainsKey(msg_type))
                message_handlers[msg_type] += message_handler;
            else
                message_handlers.Add(msg_type, message_handler);
        }

        public void UnregisterHandler(short msg_type, NetworkMessageHandler message_handler)
        {
            if (message_handlers.ContainsKey(msg_type))
            {
                message_handlers[msg_type] -= message_handler;
                if (message_handlers[msg_type] == null)
                    message_handlers.Remove(msg_type);
            }
        }

        public ReceiveData CreateInputDataBuffer()
        {
            InternalReceiveData data = host.CreateInputData<InternalReceiveData>();
            data.Package.buffer.Length = 0;
            data.Package.buffer.Context = 0;
            data.Channel.err_code = ERR_CODE.NONE;
            return data;
        }

        public SendData CreateOutputDataBuffer()
        {
            InternalSendData data = host.CreateOutputData<InternalSendData>();
            data.Package.buffer.Length = DATA_OFFSET;
            data.Channel.err_code = ERR_CODE.NONE;
            return data;
        }

        public void Send(int id, SendData data, short msg_type, ChannelOpts opts)
        {
            InternalSendData int_data = (InternalSendData)data;
            int_data.buffer.Write(msg_type, MSG_TYPE_OFFSET);
            int_data.Channel.channel = id;
            int_data.Channel.msg_type = opts == ChannelOpts.Reliable ? MSG_TYPE.RELIABLE : MSG_TYPE.NON_RELIABLE;
            int_data.Package.address.Length = UnsafeUtility.SizeOf<ChannelInfo>();
            host.Send(data);
        }

        public void Send<T>(int id, T message, short msg_type, ChannelOpts opts) where T : INetworkMessage
        {
            SendData data = CreateOutputDataBuffer();
            message.Serialize(data.buffer);
            Send(id, data, msg_type, opts);
        }
        // Start is called before the first frame update
        void Start()
        {
            // -------- ALWAYS -------
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
                return;
#endif
            // ------- PLAY MODE -----
        }

        public void Connect(string ip, ushort port, SendData data)
        {
            InternalSendData int_data = (InternalSendData)data;
            System.Net.IPAddress addr = System.Net.IPAddress.Parse(ip);
            switch (addr.AddressFamily)
            {
                case System.Net.Sockets.AddressFamily.InterNetwork:
                    ref ChannelInfoIPv4 channelIPv4 = ref UnsafeUtility.As<ChannelInfo, ChannelInfoIPv4>(ref int_data.Channel);
                    channelIPv4.af = ADDR_FAMILY.AF_INET;
                    byte[] bytes = addr.GetAddressBytes();
                    channelIPv4.ip = new ipv4() { addr0 = bytes[0], addr1 = bytes[1], addr2 = bytes[2], addr3 = bytes[3] };
                    channelIPv4.port = port;
                    channelIPv4.msg_type = MSG_TYPE.CONNECT;
                    int_data.Package.address.Length = UnsafeUtility.SizeOf<ChannelInfoIPv4>();
                    break;
                default:
                    int_data.Dispose();
                    throw new NotSupportedException(string.Format("Not supported address family: {0}", addr.AddressFamily));
            }
            host.Send(int_data);
        }

        public void Connect(string ip, ushort port)
        {
            SendData data = CreateOutputDataBuffer();
            Connect(ip, port, data);
        }

        public void Connect<T>(string ip, ushort port, T message) where T : INetworkMessage
        {
            SendData data = CreateOutputDataBuffer();
            message.Serialize(data.buffer);
            Connect(ip, port, data);
        }

        public void Disconnect(int id, SendData data, byte err_code = 0)
        {
            InternalSendData int_data = (InternalSendData)data;
            int_data.Channel.channel = id;
            int_data.Channel.msg_type = MSG_TYPE.DISCONNECT;
            int_data.Channel.err_code = (ERR_CODE)err_code;
            int_data.Package.address.Length = UnsafeUtility.SizeOf<ChannelInfo>();
            host.Send(int_data);
        }

        public void Disconnect(int id, byte err_code = 0)
        {
            SendData data = CreateOutputDataBuffer();
            Disconnect(id, data, err_code);
        }

        public void Disconnect<T>(int id, T message, byte err_code = 0) where T : INetworkMessage
        {
            SendData data = CreateOutputDataBuffer();
            message.Serialize(data.buffer);
            Disconnect(id, data, err_code);
        }

        // Update is called once per frame
        void Update()
        {
            // -------- ALWAYS --------
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
                return;
#endif
            // ------- PLAY MODE ------
            host.Update();
            // ----- Read messages ----
            InternalReceiveData data;
            while (host.Receive(out data))
            {
                int id = data.Channel.channel;
                switch (data.Channel.msg_type)
                {
                    case MSG_TYPE.ACCEPT:
                        byte access = (byte)ERR_CODE.ACCEPT_DISABLE;
                        foreach (AuthorizationHandler callback in AuthorizationEvent.GetInvocationList())
                        {
                            data.Package.buffer.Context = DATA_OFFSET;
                            access = callback.Invoke(id, ref data.Channel, data);
                        }
                        InternalSendData answer = (InternalSendData)CreateOutputDataBuffer();
                        answer.Channel.channel = id;
                        answer.Channel.msg_type = MSG_TYPE.ACCEPT;
                        answer.Channel.err_code = (ERR_CODE)access;
                        host.Send(answer);
                        break;
                    case MSG_TYPE.CONNECT:
                        ConnectionEvent?.Invoke(id, ref data.Channel);
                        break;
                    case MSG_TYPE.DISCONNECT:
                        foreach (DisconnectionHandler callback in DisconnectionEvent.GetInvocationList())
                        {
                            if (data.Package.buffer.IsCreate)
                                data.Package.buffer.Context = DATA_OFFSET;
                            callback.Invoke(id, (byte)data.Channel.err_code, data);
                        }
                        break;
                    case MSG_TYPE.RELIABLE:
                    case MSG_TYPE.NON_RELIABLE:
                        if (data.Package.buffer.Length >= DATA_OFFSET)
                        {
                            short msg_type = data.Package.buffer.ReadShort();
                            if (message_handlers.ContainsKey(msg_type))
                            {
                                foreach (NetworkMessageHandler callback in message_handlers[msg_type].GetInvocationList())
                                {
                                    data.Package.buffer.Context = DATA_OFFSET;
                                    callback.Invoke(id, data);
                                }
                            }
                        }
                        break;
                }
                data.Dispose();
            }
            // ----------------------
        }


        void OnDestroy()
        {
            // -------- ALWAYS -------
            if (instance == this)
                instance = null;
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
                return;
#endif
            // ------- PLAY MODE -----
            host.Dispose();
        }

    }

    public enum ChannelOpts
    {
        Reliable = 0,
        Unreliable = 1
    }
}
