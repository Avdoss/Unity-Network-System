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
using UnityEngine;
using System.Runtime.InteropServices;

namespace Transport
{
#if UNITY_STANDALONE_WIN
    enum SOCKET_ERRORS
    {
        EWOULDBLOCK = 10035,
    }
#elif UNITY_STANDALONE_LINUX
#elif UNITY_ANDROID
    enum SOCKET_ERRORS
    {
        EWOULDBLOCK = 11,
    }
#endif
    public static class SocketLib
    {
#if UNITY_IOS && !UNITY_EDITOR
        const string dllName = "__Internal";
#else
        const string dllName = "NativeTransport";
#endif
        private static int references = 0;

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "initialize")]
        private static extern int _initialize();

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern socket_t create_sock(int af, int type, int protocol);

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int bind_sock(socket_t fd, IntPtr addr, addrsize_t addrlen);

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern msgsize_t recv_from(socket_t fd, IntPtr buf, bufsize_t len, int flags, IntPtr addr, ref addrsize_t addrlen);

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern msgsize_t send_to(socket_t fd, IntPtr buf, bufsize_t len, int flags, IntPtr addr, addrsize_t addrlen);

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool non_blocking_mode(socket_t fd, [MarshalAs(UnmanagedType.U1)] bool value);

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int set_recv_buf(socket_t fd, int size);

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int set_send_buf(socket_t fd, int size);

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int close_sock(socket_t fd);

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "release")]
        private static extern void _release();

        [DllImport(dllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int get_last_error();

        public static int initialize()
        {
            references++;
            if (references == 1)
                return _initialize();
            return 0;
        }
        public static void release()
        {
            references--;
            if (references == 0)
                _release();
        }
    }
}
