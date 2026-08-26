using System;
using System.Runtime.InteropServices;

namespace Transport
{
    public enum SOCK_TYPE
    {
        SOCK_STREAM = 1,
        SOCK_DGRAM = 2,
        SOCK_RAW = 3,
        SOCK_RDM = 4,
        SOCK_SEQPACKET = 5
    }
#if UNITY_STANDALONE_WIN
    public enum ADDR_FAMILY: short
    {
        AF_UNSPEC = 0,
        AF_INET = 2,
        AF_IPX = 6,
        AF_APPLETALK = 16,
        AF_INET6 = 23,
        AF_IRDA = 26,
    }
#elif UNITY_STANDALONE_LINUX
#elif UNITY_ANDROID
    public enum ADDR_FAMILY: short
    {
        AF_UNSPEC = 0,
        AF_INET = 2,
        AF_IPX = 4,
        AF_APPLETALK = 5,
        AF_INET6 = 10,
        AF_IRDA = 23
    }
#endif

#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_ANDROID
    [StructLayout(LayoutKind.Explicit)]
    public struct EndPoint
    {
        [FieldOffset(0)] private ADDR_FAMILY af;
        public ADDR_FAMILY AF { set { af = value; } get { return af; } }
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct IPEndPoint
    {
        [FieldOffset(0)] private ADDR_FAMILY af;
        [FieldOffset(2)] private ushort port;
        [FieldOffset(4)] private ipv4 addr;
        [FieldOffset(8)] private fixed byte zero[8];

        public ADDR_FAMILY AF { set { af = value; } get { return af; } }
        public ushort Port { set { port = NetworkUtils.htons(value); } get { return NetworkUtils.ntohs(port); } }
        public uint Address { set { addr.addr = value; } get { return addr.addr; } }
        public byte AddrByte0 { set { addr.addr0 = value; } get { return addr.addr0; } }
        public byte AddrByte1 { set { addr.addr1 = value; } get { return addr.addr1; } }
        public byte AddrByte2 { set { addr.addr2 = value; } get { return addr.addr2; } }
        public byte AddrByte3 { set { addr.addr3 = value; } get { return addr.addr3; } }
    }
#endif

    [StructLayout(LayoutKind.Explicit)]
    public struct ipv4
    {
        [FieldOffset(0)] public uint addr;
        [FieldOffset(0)] public byte addr0;
        [FieldOffset(1)] public byte addr1;
        [FieldOffset(2)] public byte addr2;
        [FieldOffset(3)] public byte addr3;
    }
}
