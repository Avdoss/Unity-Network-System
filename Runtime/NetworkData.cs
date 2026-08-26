using System;
using Unity.Collections.LowLevel.Unsafe;
using Transport;

namespace Network
{
    public class ReceiveData : BaseData<InputPackage>
    {
        public ReadBuffer buffer { get { return package.buffer; } }
    }

    public class SendData: BaseData<OutputPackage>
    {
        public WriteBuffer buffer { get { return package.buffer; } }
        public bool IsReleasable { get { return package.IsReleasable; } set { package.IsReleasable = true; } }
    }
}