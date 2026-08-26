using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;

namespace Network
{
    public partial class NetworkSceneManager : MonoBehaviour
    {
        private class SyncUpdateData : IDisposable
        {
            protected static readonly int OBJECT_ID_SIZE = sizeof(int);
            protected static readonly int OBJECT_SYNC_ITERATION_SIZE = sizeof(int);
            protected static readonly int OBJECT_SYNC_TYPE_SIZE = sizeof(byte);
            private static readonly int COMPONENTS_COUNT_SIZE = sizeof(byte);
            private static readonly int COMPONENT_ID_SIZE = sizeof(byte);
            private static readonly int COMPONENT_LENGTH_SIZE = sizeof(int);

            protected int scene_id;
            protected int sync_iteration;
            protected float time;
            protected MsgType msgType;
            private List<SendData> update_reliabe_packages;
            private List<SendData> update_non_reliabe_packages;

            public SyncUpdateData(int scene_id, int sync_iteration, float time, MsgType msgType)
            {
                this.scene_id = scene_id;
                this.sync_iteration = sync_iteration;
                this.time = time;
                this.msgType = msgType;
            }

            public static unsafe int GetUpdateUnitSize(byte* pointer, int pos)
            {
                int offset = pos + OBJECT_ID_SIZE + OBJECT_SYNC_ITERATION_SIZE + OBJECT_SYNC_TYPE_SIZE;
                ref byte count = ref UnsafeUtility.AsRef<byte>(pointer + offset);
                offset += COMPONENTS_COUNT_SIZE;
                for (int i = 0; i < count; i++)
                {
                    offset += COMPONENT_ID_SIZE;
                    ref int length = ref UnsafeUtility.AsRef<int>(pointer + offset);
                    offset += COMPONENT_LENGTH_SIZE + length;
                }
                return offset - pos;
            }

            private unsafe void SerializeVariableSizeObjectsData(ManagedWrapper<Transport.WriteBuffer> data, ref List<SendData> packages)
            {
                int pos = 0;
                int size = 0;
                int unit_size = 0;
                if (packages == null)
                {
                    packages = new List<SendData>();
                    size = GetUpdateUnitSize(data.Value.Pointer, 0);
                }
                int last = packages.Count - 1;

                while (pos < data.Value.Length)
                {
                    if (size > 0)
                    {
                        SendData sendData = NetworkManager.Singleton.CreateOutputDataBuffer();
                        sendData.buffer.Write(this.scene_id);
                        sendData.buffer.Write(this.time);
                        sendData.buffer.Write(this.sync_iteration);
                        packages.Add(sendData);
                        last++;
                    }
                    int available_space = packages[last].buffer.Capacity - packages[last].buffer.Length;
                    if (available_space >= data.Value.Length - pos)
                    {
                        packages[last].buffer.Write(data.Value, pos, data.Value.Length - pos);
                        return;
                    }
                    while (size + unit_size <= available_space)
                    {
                        size += unit_size;
                        unit_size = GetUpdateUnitSize(data.Value.Pointer, pos + size);
                    }
                    if (size > 0)
                    {
                        packages[last].buffer.Write(data.Value, pos, size);
                        pos += size;
                    }
                    size = unit_size;
                    unit_size = 0;
                }
            }

            protected static List<SendData> CopyPackages(List<SendData> srcs)
            {
                int size = srcs.Count;
                List<SendData> dsts = new List<SendData>(size);
                foreach (SendData src in srcs)
                {
                    SendData dst = NetworkManager.Singleton.CreateOutputDataBuffer();
                    int head_size = dst.buffer.Length;
                    dst.buffer.Write(src.buffer, head_size, src.buffer.Length - head_size);
                    dsts.Add(dst);
                }
                return dsts;
            }

            protected void Copy(SyncUpdateData destination)
            {
                destination.update_reliabe_packages = this.update_reliabe_packages != null ? CopyPackages(this.update_reliabe_packages) : null;
                destination.update_non_reliabe_packages = this.update_non_reliabe_packages != null ? CopyPackages(this.update_non_reliabe_packages) : null;
            }
            public SyncUpdateData Copy()
            {
                SyncUpdateData syncData = new SyncUpdateData(scene_id, sync_iteration, time, msgType);
                Copy(syncData);
                return syncData;
            }

            public void AddUpdatedObjectsReliableData(ManagedWrapper<Transport.WriteBuffer> data)
            {
                SerializeVariableSizeObjectsData(data, ref update_reliabe_packages);
            }
            public void AddUpdatedObjectsNonReliableData(ManagedWrapper<Transport.WriteBuffer> data)
            {
                SerializeVariableSizeObjectsData(data, ref update_non_reliabe_packages);
            }
            public void Send(int player_id)
            {
                if (update_reliabe_packages != null)
                    foreach (SendData data in update_reliabe_packages)
                        NetworkManager.Singleton.Send(player_id, data, (short)msgType, ChannelOpts.Reliable);
                if (update_non_reliabe_packages != null)
                    foreach (SendData data in update_non_reliabe_packages)
                        NetworkManager.Singleton.Send(player_id, data, (short)msgType, ChannelOpts.Unreliable);
            }
            public void Dispose()
            {
                if (update_reliabe_packages != null)
                    foreach (SendData data in update_reliabe_packages)
                        data.Dispose();
                if (update_non_reliabe_packages != null)
                    foreach (SendData data in update_non_reliabe_packages)
                        data.Dispose();
            }
        }

        private class SyncData : SyncUpdateData
        {
            private static readonly int OBJECT_REG_ID_SIZE = sizeof(int);
            private static readonly int DELETE_UNIT_SIZE = OBJECT_ID_SIZE;
            private static readonly int CREATE_UNIT_SIZE = OBJECT_ID_SIZE + OBJECT_REG_ID_SIZE + OBJECT_SYNC_ITERATION_SIZE;

            private List<SendData> delete_packages;
            private List<SendData> create_packages;

            public SyncData(int scene_id, int iteration, float time) : base(scene_id, iteration, time, MsgType.UpdateObjectsFromOwnerMessage)
            {

            }

            private void SerializeConstantSizeObjectsData(ManagedWrapper<Transport.WriteBuffer> data, ref List<SendData> packages, int unit_size)
            {
                int pos = 0;
                int size = 0;
                if (packages == null)
                {
                    packages = new List<SendData>();
                    size = unit_size;
                }
                int last = packages.Count - 1;

                while (pos < data.Value.Length)
                {
                    if (size > 0)
                    {
                        SendData sendData = NetworkManager.Singleton.CreateOutputDataBuffer();
                        sendData.buffer.Write(this.scene_id);
                        sendData.buffer.Write(this.sync_iteration);
                        packages.Add(sendData);
                        last++;
                    }
                    int available_space = packages[last].buffer.Capacity - packages[last].buffer.Length;
                    if (available_space >= data.Value.Length - pos)
                    {
                        packages[last].buffer.Write(data.Value, pos, data.Value.Length - pos);
                        return;
                    }
                    if (available_space > size)
                        size += ((available_space - size) / unit_size) * unit_size;
                    if (size > 0)
                    {
                        packages[last].buffer.Write(data.Value, pos, size);
                        pos += size;
                    }
                    size = unit_size;
                }
            }

            public new SyncData Copy()
            {
                SyncData syncData = new SyncData(scene_id, sync_iteration, time);
                syncData.delete_packages = this.delete_packages != null ? CopyPackages(this.delete_packages) : null;
                syncData.create_packages = this.create_packages != null ? CopyPackages(this.create_packages) : null;
                base.Copy(syncData);
                return syncData;
            }

            public void AddDeletedObjectsData(ManagedWrapper<Transport.WriteBuffer> data)
            {
                SerializeConstantSizeObjectsData(data, ref delete_packages, DELETE_UNIT_SIZE);
            }
            public void AddCreatedObjectsData(ManagedWrapper<Transport.WriteBuffer> data)
            {
                SerializeConstantSizeObjectsData(data, ref create_packages, CREATE_UNIT_SIZE);
            }

            public new void Send(int player_id)
            {
                if (delete_packages != null)
                    foreach (SendData data in delete_packages)
                        NetworkManager.Singleton.Send(player_id, data, (short)MsgType.DeleteObjectsMessage, ChannelOpts.Reliable);
                if (create_packages != null)
                    foreach (SendData data in create_packages)
                        NetworkManager.Singleton.Send(player_id, data, (short)MsgType.CreateObjectsMessage, ChannelOpts.Reliable);
                base.Send(player_id);
            }

            public new void Dispose()
            {
                if (delete_packages != null)
                    foreach (SendData data in delete_packages)
                        data.Dispose();
                if (create_packages != null)
                    foreach (SendData data in create_packages)
                        data.Dispose();
                base.Dispose();
            }
        }
    }
}