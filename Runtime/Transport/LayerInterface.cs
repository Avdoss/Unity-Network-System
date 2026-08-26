using System;

namespace Transport
{
    public interface ILayer: IDisposable
    {
        unsafe void* NextLayer { get; set; }
        unsafe void* PrevLayer { get; set; }
        public int HeadBegin { get; set; }
        public int HeadSize { get; }
        public HostData CommonData { get; set; }
        public bool Receive<T>(InputPackage package) where T : struct, ILayerIdentity;
        public bool Send<T>(OutputPackage package) where T : struct, ILayerIdentity;
        public bool Initialize();
        public void Update<T>() where T : struct, ILayerIdentity;
    }
}
