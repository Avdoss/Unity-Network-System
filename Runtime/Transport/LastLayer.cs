using System;
using UnityEngine;

namespace Transport
{
    public struct LastLayer : ILayer
    {
        private Multithreading.ConcurrentQueue<InputPackage> input_queue;

        public unsafe void* NextLayer { get; set; }
        public unsafe void* PrevLayer { get; set; }
        public int HeadBegin { get; set; }
        public int HeadSize { get { return 0; } }
        public HostData CommonData { get; set; }
        public bool Initialize()
        {
            input_queue = new Multithreading.ConcurrentQueue<InputPackage>(true);
            Debug.Log("Last layer initialize successuly");
            return true;
        }
        public bool Receive<T>(InputPackage package) where T : struct, ILayerIdentity
        {
            //Debug.Log("Last layer receive m");
            if(package.IsReleasable)
                input_queue.push(package);
            else
                input_queue.push(package.Copy(true));
            return true;
        }
        public bool Send<T>(OutputPackage package) where T : struct, ILayerIdentity
        {
            throw new NotImplementedException("Method Send of struct LastLayer is not implemented");
        }
        public void Update<T>() where T : struct, ILayerIdentity
        {
            //Debug.Log("Last layer update");
        }

        public bool GetPackage(out InputPackage package)
        {
            return input_queue.pop(out package);
        }

        public void Dispose()
        {
            Debug.Log("Last layer dispose");
            if (input_queue.IsCreate)
            {
                InputPackage package;
                while (input_queue.pop(out package))
                    package.Dispose();
                input_queue.Dispose();
            }
        }
    }
}
