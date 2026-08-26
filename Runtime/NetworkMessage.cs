using System.Collections.Generic;
using Transport;
using System.Text;
using System;

namespace Network
{
    public enum MsgType : short
    {
        SyncSceneMessage = 0,
        SyncSceneAnswerMessage = 1,
        DesyncSceneMessage = 2,
        DesyncSceneAnswerMessage = 3,
        CreateObjectsMessage = 4,
        DeleteObjectsMessage = 5,
        UpdateObjectsFromOwnerMessage = 6,
        UpdateObjectsFromSlaveMessage = 7
    }
    public interface INetworkMessage
    {
        void Serialize(WriteBuffer buffer);
        void Deserialize(ReadBuffer buffer);
    }
}
