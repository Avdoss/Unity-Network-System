using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Multithreading;

namespace Network
{
    public class IdMap
    {
        public int src_begin;
        public int dst_begin;
        public int count;
    }
    public partial class NetworkSceneManager : MonoBehaviour
    {
        private class NetworkScene
        {
            public static readonly Vector3 Vector3Min = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            public static readonly Vector3 Vector3Max = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            public static readonly Vector3Int Vector3IntMin = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            public static readonly Vector3Int Vector3IntMax = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);

            // The structure contains information about synchronization of the scene with another host
            private class SyncSessionInfo : IDisposable
            {
                public int player_id;
                public SYNC_SESSION_STATE state;
                public bool init;
                public bool unload;
                public Vector3 posPrev;
                public Vector3Int chunk;
                public Vector3Int chunkPrev;
                public List<NetworkOwnerObjectInfo> linking_objects;
                public List<NetworkOwnerObjectInfo> linked_objects;
                public List<NetworkOwnerObjectInfo> unlinking_objects;
                public ConcurrentObject<SyncUpdateData> prev_sync_vrivate_messages;
                public ConcurrentObject<SyncUpdateData> curr_sync_vrivate_messages;

                public SyncSessionInfo(int id, SYNC_SESSION_STATE state)
                {
                    this.player_id = id;
                    this.state = state;
                    this.init = false;
                    this.unload = false;
                    this.posPrev = Vector3Max;
                    this.chunk = Vector3IntMax;
                    this.chunkPrev = Vector3IntMax;
                    linking_objects = new List<NetworkOwnerObjectInfo>();
                    linked_objects = new List<NetworkOwnerObjectInfo>();
                    unlinking_objects = new List<NetworkOwnerObjectInfo>();
                    prev_sync_vrivate_messages = new ConcurrentObject<SyncUpdateData>() { Value = null };
                    curr_sync_vrivate_messages = new ConcurrentObject<SyncUpdateData>() { Value = null };
                }
                public void Dispose()
                {
                    if (prev_sync_vrivate_messages.Value != null)
                    {
                        prev_sync_vrivate_messages.Value.Dispose();
                        prev_sync_vrivate_messages.Value = null;
                    }
                    if (curr_sync_vrivate_messages.Value != null)
                    {
                        curr_sync_vrivate_messages.Value.Dispose();
                        curr_sync_vrivate_messages.Value = null;
                    }
                }
            }
            private interface ISerializableCache : IDisposable
            {
                public int id { get; set; }
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> init_cache { get; }
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> reliable_cache { get; }
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> non_reliable_cache { get; }

                public void SerializeUpdateData(SyncData syncData, SYNC_TYPE sync_type)
                {
                    ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> cache = null;
                    switch (sync_type)
                    {
                        case SYNC_TYPE.INIT: cache = init_cache; break;
                        case SYNC_TYPE.RELIABLE: cache = reliable_cache; break;
                        case SYNC_TYPE.NON_RELIABLE: cache = non_reliable_cache; break;
                    }
                    if (!cache.HasValue)
                    {
                        cache.Lock();
                        if (!cache.HasValue)
                        {
                            ManagedWrapper<Transport.WriteBuffer> data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                            Singleton.objects[id].inst.NetworkSerialize(data.Value, sync_type);
                            if (data.Value.Length == 0)
                            {
                                data.Dispose();
                                data = null;
                            }
                            cache.Set(data);
                        }
                        cache.Unlock();
                    }
                    if (cache.Value != null)
                    {
                        switch (sync_type)
                        {
                            case SYNC_TYPE.INIT: syncData.AddUpdatedObjectsReliableData(cache.Value); break;
                            case SYNC_TYPE.RELIABLE: syncData.AddUpdatedObjectsReliableData(cache.Value); break;
                            case SYNC_TYPE.NON_RELIABLE: syncData.AddUpdatedObjectsNonReliableData(cache.Value); break;
                        }
                    }
                }
            }


            private interface IPrivate
            {
                public List<int> observers { get; } // sid
            }

            private class NetworkObjectInfo
            {
                public NetworkObject inst;
                public NetworkObjectInfo(NetworkObject inst)
                {
                    this.inst = inst;
                }
            }
            private class NetworkOwnerObjectInfo : NetworkObjectInfo // PUBLIC_GLOBAL
            {
                public enum SYNC_STATUS
                {
                    DISABLE = 0,
                    INIT = 1,
                    ENABLE = 2
                }
                public int id { get; set; }
                public VISIBLE_TYPE visible_type;
                public SYNC_STATUS status;
                public NetworkOwnerObjectInfo(NetworkObject inst) : base(inst)
                {
                    this.id = inst.id;
                    this.visible_type = inst.VisibleType;
                    this.status = SYNC_STATUS.INIT;
                }
            }
            private class NetworkOwnerPrivateObjectInfo : NetworkOwnerObjectInfo, ISerializableCache, IPrivate // PRIVATE_GLOBAL
            {
                public List<int> observers { get; } //sid
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> init_cache { get; }
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> reliable_cache { get; }
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> non_reliable_cache { get; }
                public NetworkOwnerPrivateObjectInfo(NetworkObject inst) : base(inst)
                {
                    this.observers = new List<int>();
                    this.init_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.reliable_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.non_reliable_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                }
                public void Dispose()
                {
                    init_cache.Dispose();
                    reliable_cache.Dispose();
                    non_reliable_cache.Dispose();
                }
            }
            private class NetworkOwnerLocalObjectInfo : NetworkOwnerObjectInfo // PUBLIC_LOCAL
            {
                public Vector3 posPrev;
                public NetworkOwnerLocalObjectInfo(NetworkObject inst) : base(inst)
                {
                    this.posPrev = Vector3Max;
                }
            }
            private class NetworkOwnerPrivateLocalObjectInfo : NetworkOwnerLocalObjectInfo, ISerializableCache, IPrivate // PRIVATE_LOCAL
            {
                public List<int> observers { get; } //sid
                public Vector3Int chunk;
                public Vector3Int chunkPrev;
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> init_cache { get; }
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> reliable_cache { get; }
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> non_reliable_cache { get; }
                public NetworkOwnerPrivateLocalObjectInfo(NetworkObject inst) : base(inst)
                {
                    this.observers = new List<int>();
                    this.chunk = Vector3IntMax;
                    this.chunkPrev = Vector3IntMax;
                    this.init_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.reliable_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.non_reliable_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                }
                public void Dispose()
                {
                    init_cache.Dispose();
                    reliable_cache.Dispose();
                    non_reliable_cache.Dispose();
                }
            }


            public NetworkScene(int id, string path)
            {
                this.id = id;
                this.path = path;
                this.sessions = new List<SyncSessionInfo>();
            }

            private int id;
            private string path;
            private List<IdMap> IdMaps;
            private List<NetworkObjectInfo> objects; // all network objects (owner, non-owner, scene placed and dynamic) in this scene
            private List<NetworkOwnerObjectInfo> deleted_objects;
            private List<SyncSessionInfo> sessions; // synchronizations this scene with other players
            private ManagedWrapper<Transport.WriteBuffer> load_message; // cashed initial message (with scene placed objects) for new sync requests

            private int init_sessions_count;
            private int active_sessions_count;
            private int active_sessions_with_attach_object_count;
            private int public_local_objects_count;
            private int public_global_objects_count;
            private int private_local_objects_count;
            private int private_global_objects_count;

            private int session_counter;

            public int Id { get { return id; } }
            public string Path { get { return path; } }
            public bool IsLoad { get; private set; }

            // Function add network objects with owner flag is true placed in scene to netwotkObjects list
            public void Reset()
            {
                if (IsLoad)
                    DesyncSceneWithAllPlayers(false);
                IdMaps = new List<IdMap>();
                objects = new List<NetworkObjectInfo>();
                deleted_objects = new List<NetworkOwnerObjectInfo>();
                IsLoad = false;
                init_sessions_count = 0;
                active_sessions_count = 0;
                active_sessions_with_attach_object_count = 0;
                public_local_objects_count = 0;
                public_global_objects_count = 0;
                private_local_objects_count = 0;
                private_global_objects_count = 0;
                bbox_prev = new BBox();
                bbox_prev.Reset();
            }

            /// <summary>
            /// Finds all NetworkObject's in loaded scene and serializes them into initial message.
            /// Hides all non-owner objects.
            /// </summary>
            public void OnSceneLoaded()
            {
                //Add scene placed objects
                if (this.id != 0) // Exclude DontDestroyOnLoad scene
                {
                    Scene scene = SceneManager.GetSceneByPath(path);
                    foreach (NetworkObject inst in FindObjectsOfType<NetworkObject>(scene))
                    {
                        AddNetworkObject(inst);
                        inst.Initialize();
                    }

                    foreach (NetworkObjectInfo info in objects)
                    {
                        NetworkObject inst = info.inst;
                        if (inst.IsOwner)
                        {
                            Singleton.RegisterNetworkObject(inst);
                            ((NetworkOwnerObjectInfo)info).id = inst.id;
                            int last = IdMaps.Count - 1;
                            if (last != -1 && IdMaps[last].src_begin + IdMaps[last].count == inst.scenePos && IdMaps[last].dst_begin + IdMaps[last].count == inst.id)
                                IdMaps[last].count += 1;
                            else
                                IdMaps.Add(new IdMap { src_begin = inst.scenePos, dst_begin = inst.id, count = 1 });
                        }
                        else
                            inst.gameObject.SetActive(false);
                        inst.regId = -1;
                        inst.createdSyncIteration = -1;
                    }
                }
                // Create load message (cashe)
                load_message = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16));
                SyncSceneMessage message = new SyncSceneMessage();
                message.scene_id = this.id;
                message.err_code = SYNC_ERR_CODE.NONE;
                message.mode = LoadSceneMode.Single;
                message.IdMaps = this.IdMaps;
                message.Serialize(load_message.Value);
                IsLoad = true;
            }

            /// <summary>
            /// Register new NetworkObject (owner or non-owner) to this scene
            /// </summary>
            /// <param name="instance">Added network object</param>
            public void AddNetworkObject(NetworkObject instance)
            {
                Debug.Log("Create object " + instance.name);
                instance.sceneId = this.id;
                NetworkObjectInfo info;
                if (instance.IsOwner)
                {
                    switch (instance.VisibleType)
                    {
                        case VISIBLE_TYPE.PUBLIC_LOCAL:
                            public_local_objects_count += 1;
                            info = new NetworkOwnerLocalObjectInfo(instance);
                            break;
                        case VISIBLE_TYPE.PUBLIC_GLOBAL:
                            public_global_objects_count += 1;
                            info = new NetworkOwnerObjectInfo(instance);
                            break;
                        case VISIBLE_TYPE.PRIVATE_LOCAL:
                            private_local_objects_count += 1;
                            info = new NetworkOwnerPrivateLocalObjectInfo(instance);
                            break;
                        case VISIBLE_TYPE.PRIVATE_GLOBAL:
                            private_global_objects_count += 1;
                            info = new NetworkOwnerPrivateObjectInfo(instance);
                            break;
                        default:
                            info = null;
                            break;
                    }
                }
                else
                    info = new NetworkObjectInfo(instance);

                if (!IsLoad) // Load scene
                    objects.Add(info, instance.scenePos);
                else
                {
                    int pos = objects.AddOrReplace(info, (instead) => instead == null);
                    instance.scenePos = pos;
                }
            }

            /// <summary>
            /// Unregister NetworkObject (owner or non-owner) from this scene
            /// </summary>
            /// <param name="instance">Deleted network object</param>
            public void DeleteNetworkObject(NetworkObject instance)
            {
                Debug.Log("Delete object " + instance.name);
                NetworkObjectInfo info = objects[instance.scenePos];
                if (instance.IsOwner)
                {
                    if (instance.visibleType == VISIBLE_TYPE.PRIVATE_GLOBAL || instance.visibleType == VISIBLE_TYPE.PRIVATE_LOCAL)
                    {
                        IPrivate private_info = (IPrivate)info;
                        while (private_info.observers.Count > 0)
                            MakeObjectInvisibleToPlayer(private_info.observers[0], instance);
                    }
                    info.inst = null;
                    NetworkOwnerObjectInfo owner_info = (NetworkOwnerObjectInfo)info;
                    owner_info.status = NetworkOwnerObjectInfo.SYNC_STATUS.DISABLE;
                    deleted_objects.Add(owner_info);
                }
                else
                    if (instance.host != -1 && Singleton != null && Singleton.players[instance.host] != null)
                    Singleton.players[instance.host].objects[instance.id] = null;
                objects[instance.scenePos] = null;
            }

            /// <summary>
            /// Get synchronization session id for this scene by player id
            /// </summary>
            /// <param name="player_id">host id</param>
            public int FindSyncSession(int player_id)
            {
                PlayerInfo.SyncSceneInfo info = Singleton.players[player_id].scenes.Find(x => x.scene_id == this.id);
                return info != null ? info.sid : -1;
            }

            /// <summary>
            /// Get synchonization status for this scene by synchronization id
            /// </summary>
            /// <param name="sid">synchronization id</param>
            public SYNC_SESSION_STATE GetSyncSessionState(int sid)
            {
                return sessions[sid] != null ? sessions[sid].state : SYNC_SESSION_STATE.NONE;
            }

            /// <summary>
            /// Synchronize scene with player 
            /// </summary>
            /// <param name="player_id">host id</param>
            /// <param name="mode">load scene mode</param>
            /// <param name="msg_type">SyncSceneMessage for request and SyncSceneAnswerMessage for answer</param>
            public int SyncSceneWithPlayer(int player_id, LoadSceneMode mode, MsgType msg_type = MsgType.SyncSceneMessage)
            {
                int sid = -1;
                sid = CreateSyncSession(player_id);
                SendSyncSceneMessage(player_id, mode, msg_type);
                return sid;
            }

            /// <summary>
            /// Desync scene with player 
            /// </summary>
            /// <param name="sid">synchronization id</param>
            /// <param name="unload">should the player's scene be closed after desync</param>
            /// <param name="msg_type">DesyncSceneMessage for request and DesyncSceneAnswerMessage for answer</param>
            public void DesyncSceneWithPlayer(int sid, bool unload, MsgType msg_type = MsgType.DesyncSceneMessage)
            {
                if (sessions[sid].state == SYNC_SESSION_STATE.INIT_STATE)
                {
                    sessions[sid].state = SYNC_SESSION_STATE.RELEASE_STATE;
                    sessions[sid].unload = unload;
                }
                else if (sessions[sid].state == SYNC_SESSION_STATE.READY_STATE)
                {
                    DesyncSceneObjects(sid);
                    sessions[sid].state = SYNC_SESSION_STATE.RELEASE_STATE;
                    SendDesyncSceneMessage(sid, unload, msg_type);
                }
            }

            public void DesyncSceneWithAllPlayers(bool unload)
            {
                for (int i = 0; i < sessions.Count; i++)
                    if (sessions[i] != null)
                        DesyncSceneWithPlayer(i, unload);
            }

            public void ForceDesyncSceneWithPlayer(int sid)
            {
                if (sessions[sid].state == SYNC_SESSION_STATE.READY_STATE)
                    DesyncSceneObjects(sid);
                OnSceneDesyncWithPlayer(sid);
            }

            public void ReceiveSyncSceneMessage(int player_id, SyncSceneMessage message)
            {
                int sid = SyncSceneWithPlayer(player_id, message.mode, MsgType.SyncSceneAnswerMessage);
                SyncScenePlacedObjects(sid, message);
                OnSceneSyncWithPlayer(player_id, sid, message.err_code);
            }

            public IEnumerator LoadAndReceiveSyncSceneMessage(int player_id, SyncSceneMessage message)
            {
                SceneManager.LoadSceneAsync(path, message.mode);
                while (!IsLoad)
                    yield return null;
                ReceiveSyncSceneMessage(player_id, message);
            }

            public void ReceiveSyncSceneAnswerMessage(int player_id, int sid, SyncSceneMessage message)
            {
                if (sessions[sid].state == SYNC_SESSION_STATE.INIT_STATE)
                {
                    if (message.err_code == SYNC_ERR_CODE.NONE)
                        SyncScenePlacedObjects(sid, message);
                    else
                        DestroySyncSession(sid);
                    OnSceneSyncWithPlayer(player_id, sid, message.err_code);
                }
                else if (sessions[sid].state == SYNC_SESSION_STATE.RELEASE_STATE)
                    SendDesyncSceneMessage(sid, sessions[sid].unload, MsgType.DesyncSceneMessage);
            }

            public void ReceiveDesyncSceneMessage(int player_id, int sid, DesyncSceneMessage message)
            {
                DesyncSceneWithPlayer(sid, message.unload, MsgType.DesyncSceneAnswerMessage);
                OnSceneDesyncWithPlayer(sid);
            }

            public IEnumerator UnloadAndReceiveDesyncSceneMessage(int player_id, int sid, DesyncSceneMessage message)
            {
                SceneManager.UnloadSceneAsync(path);
                while (IsLoad)
                    yield return null;
                ReceiveDesyncSceneMessage(player_id, sid, message);
            }

            public void ReceiveDesyncSceneAnswerMessage(int player_id, int sid, DesyncSceneMessage message)
            {
                if (sessions[sid].state == SYNC_SESSION_STATE.RELEASE_STATE)
                    OnSceneDesyncWithPlayer(sid);
            }

            public void OnAttachGameObjectToPlayer(int sid)
            {
                if (sessions[sid].state == SYNC_SESSION_STATE.READY_STATE)
                    active_sessions_with_attach_object_count += 1;
            }
            public void OnDetachGameObjectToPlayer(int sid)
            {
                if (sessions[sid].state == SYNC_SESSION_STATE.READY_STATE)
                    active_sessions_with_attach_object_count -= 1;
            }

            public void MakeObjectVisibleToPlayer(int sid, NetworkObject inst)
            {
                SyncSessionInfo session = sessions[sid];
                if (session.linked_objects.FindIndex((x) => x.inst == inst) == -1)
                {
                    int index = session.unlinking_objects.FindIndex((x) => x.inst == inst);
                    if (index != -1)
                    {
                        session.linked_objects.Add(session.unlinking_objects[index]);
                        session.unlinking_objects.PlaceBackAndRemoveAt(index);
                        ((IPrivate)objects[inst.scenePos]).observers.Add(sid);
                    }
                    else if (session.linking_objects.FindIndex((x) => x.inst == inst) == -1)
                    {
                        NetworkObjectInfo info = objects[inst.scenePos];
                        if (info != null && info.inst == inst)
                        {
                            session.linking_objects.Add((NetworkOwnerObjectInfo)info);
                            ((IPrivate)info).observers.Add(sid);
                        }
                    }
                }
            }
            public void MakeObjectInvisibleToPlayer(int sid, NetworkObject inst)
            {
                SyncSessionInfo session = sessions[sid];
                int index = session.linked_objects.FindIndex((x) => x.inst == inst);
                if (index != -1)
                {
                    session.unlinking_objects.Add(session.linked_objects[index]);
                    session.linked_objects.PlaceBackAndRemoveAt(index);
                    ((IPrivate)objects[inst.scenePos]).observers.PlaceBackAndRemove(sid);
                }
                else
                {
                    index = session.linking_objects.FindIndex((x) => x.inst == inst);
                    if (index != -1)
                    {
                        session.linking_objects.PlaceBackAndRemoveAt(index);
                        ((IPrivate)objects[inst.scenePos]).observers.PlaceBackAndRemove(sid);
                    }
                }
            }

            public void SendUpdateMessageTo(int sid, int object_id, byte component_id, NetworkBehaviour.SendMessageCallback callback, ChannelOpts channel)
            {
                Transport.WriteBuffer buffer = new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp);
                ManagedWrapper<Transport.WriteBuffer> data = new ManagedWrapper<Transport.WriteBuffer>(buffer);
                buffer.Write(object_id);            // object id
                buffer.Write(Singleton.objects[object_id].inst.createdSyncIteration);
                buffer.Write((byte)(channel == ChannelOpts.Reliable ? SYNC_TYPE.RELIABLE : SYNC_TYPE.NON_RELIABLE));
                buffer.Write((byte)1);              // numbers of components
                buffer.Write(component_id);         // component id
                int size_offset = buffer.Write(0);  // component size
                int data_offset = buffer.Length;
                callback?.Invoke(buffer);
                if (data_offset < buffer.Length)
                {
                    buffer.Write(buffer.Length - data_offset, size_offset);
                    ConcurrentObject<SyncUpdateData> syncData = NetworkSceneManager.Singleton.isUpdating ? sessions[sid].curr_sync_vrivate_messages : sessions[sid].prev_sync_vrivate_messages;
                    syncData.Lock();
                    if (syncData.Value == null)
                        syncData.Value = new SyncUpdateData(this.id, NetworkSceneManager.Singleton.update_counter, NetworkSceneManager.Singleton.time, MsgType.UpdateObjectsFromOwnerMessage);
                    if (channel == ChannelOpts.Reliable)
                        syncData.Value.AddUpdatedObjectsReliableData(data);
                    else
                        syncData.Value.AddUpdatedObjectsNonReliableData(data);
                    syncData.Unlock();
                }
                data.Dispose();
            }

            private void SendSyncSceneMessage(int player_id, LoadSceneMode mode, MsgType msg_type)
            {
                SendData data = NetworkManager.Singleton.CreateOutputDataBuffer();
                int mode_offset = data.buffer.Write(load_message.Value, 0, load_message.Value.Length) + 4 + 1;
                data.buffer.Write((int)mode, mode_offset);
                NetworkManager.Singleton.Send(player_id, data, (short)msg_type, ChannelOpts.Reliable);
            }

            private void SendDesyncSceneMessage(int sid, bool unload, MsgType msg_type)
            {
                DesyncSceneMessage message = new DesyncSceneMessage();
                message.scene_id = this.id;
                message.unload = unload;
                NetworkManager.Singleton.Send(sessions[sid].player_id, message, (short)msg_type, ChannelOpts.Reliable);
            }

            private void SyncScenePlacedObjects(int sid, SyncSceneMessage message)
            {
                int player_id = sessions[sid].player_id;
                foreach (var map in message.IdMaps)
                {
                    for (int pos = map.src_begin; pos < map.src_begin + map.count; pos++)
                    {
                        if (objects[pos] != null)
                        {
                            NetworkObject inst = objects[pos].inst;
                            if (inst.isScenePlaced)
                            {
                                int id = map.dst_begin + pos - map.src_begin;
                                inst.id = id;
                                inst.host = player_id;
                                Singleton.players[player_id].objects.Add(new PlayerInfo.SyncObjectInfo(inst, PlayerInfo.SyncObjectInfo.SYNC_STATUS.DISABLE), id);
                            }
                        }
                    }
                }
                sessions[sid].init = true;
                init_sessions_count += 1;
                active_sessions_count += 1;
                if (Singleton.players[player_id].gameObject != null)
                    active_sessions_with_attach_object_count += 1;

            }

            private void DesyncSceneObjects(int sid)
            {
                SyncSessionInfo session = sessions[sid];
                int player_id = session.player_id;
                for (int i = 0; i < Singleton.players[player_id].objects.Count; i++)
                {
                    if (Singleton.players[player_id].objects[i] != null)
                    {
                        NetworkObject inst = Singleton.players[player_id].objects[i].inst;
                        if (inst.sceneId == this.id)
                        {
                            inst.OnNetworkDestroy();
                            if (inst.IsScenePlaced)
                            {
                                inst.id = -1;
                                inst.host = -1;
                            }
                            else
                                GameObject.Destroy(inst.gameObject);
                            Singleton.players[player_id].objects[i] = null;
                        }
                    }
                }
                if (session.init == true)
                {
                    session.init = false;
                    init_sessions_count -= 1;
                }
                active_sessions_count -= 1;
                if (Singleton.players[player_id].gameObject != null)
                    active_sessions_with_attach_object_count -= 1;

                foreach (NetworkOwnerObjectInfo info in session.linking_objects)
                    ((IPrivate)info).observers.PlaceBackAndRemove(sid);
                foreach (NetworkOwnerObjectInfo info in session.linked_objects)
                    ((IPrivate)info).observers.PlaceBackAndRemove(sid);
            }
            private void OnSceneSyncWithPlayer(int player_id, int sid, SYNC_ERR_CODE err_code)
            {
                if (err_code == SYNC_ERR_CODE.NONE)
                    sessions[sid].state = SYNC_SESSION_STATE.READY_STATE;
                Debug.Log(string.Format("Scene {0} sync with player {1}. SYNC_ERR_CODE: {2}", path, player_id, err_code));
                NetworkSceneManager.Singleton.OnSceneSyncWithPlayer?.Invoke(player_id, this.id - 1, sid, err_code);
            }

            private void OnSceneDesyncWithPlayer(int sid)
            {
                int player_id = sessions[sid].player_id;
                DestroySyncSession(sid);
                Debug.Log(string.Format("Scene {0} desync with player {1}", path, player_id));
                NetworkSceneManager.Singleton.OnSceneDesyncWithPlayer?.Invoke(player_id, this.id - 1);
            }

            private int CreateSyncSession(int player_id)
            {
                int sid = sessions.AddOrReplace(new SyncSessionInfo(player_id, SYNC_SESSION_STATE.INIT_STATE), (instead) => instead == null);
                Singleton.players[player_id].scenes.Add(new PlayerInfo.SyncSceneInfo(this.id, sid));
                return sid;
            }

            private void DestroySyncSession(int sid)
            {
                int player_id = sessions[sid].player_id;
                int ind = Singleton.players[player_id].scenes.FindIndex(x => x.scene_id == this.id);
                Singleton.players[player_id].scenes[ind].Dispose();
                Singleton.players[player_id].scenes.PlaceBackAndRemoveAt(ind);
                sessions[sid].Dispose();
                sessions[sid] = null;
            }

            private bool IsReadySession(SyncSessionInfo session)
            {
                return session != null && session.state == SYNC_SESSION_STATE.READY_STATE;
            }
            private bool IsOwnerLocalObject(NetworkObjectInfo info)
            {
                return info.inst.IsOwner && IsOwnerLocalObject((NetworkOwnerObjectInfo)info);
            }
            private bool IsOwnerLocalObject(NetworkOwnerObjectInfo info)
            {
                return info.visible_type == VISIBLE_TYPE.PRIVATE_LOCAL || info.visible_type == VISIBLE_TYPE.PUBLIC_LOCAL;
            }
            private bool IsOwnerPublicGlobalObject(NetworkObjectInfo info)
            {
                return info.inst.IsOwner && ((NetworkOwnerObjectInfo)info).visible_type == VISIBLE_TYPE.PUBLIC_GLOBAL;
            }

            // ---------- UPDATE ------------ 
            private struct BBox
            {
                private Vector3 min;
                private Vector3 max;

                public Vector3 Min { get { return min; } set { min = value; } }
                public Vector3 Max { get { return max; } set { max = value; } }

                public BBox(Vector3 min, Vector3 max)
                {
                    this.min = min;
                    this.max = max;
                }

                public void Reset()
                {
                    this.min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    this.max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                }
                public void AddPoint(Vector3 point)
                {
                    min.x = Math.Min(min.x, point.x);
                    min.y = Math.Min(min.y, point.y);
                    min.z = Math.Min(min.z, point.z);
                    max.x = Math.Max(max.x, point.x);
                    max.y = Math.Max(max.y, point.y);
                    max.z = Math.Max(max.z, point.z);
                }
            }

            private struct ChangedObject : ISerializableCache, IDisposable
            {
                public int id { get; set; }
                public Vector3Int chunk;
                public Vector3Int chunkPrev;

                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> init_cache { get; }
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> reliable_cache { get; }
                public ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> non_reliable_cache { get; }

                public ChangedObject(int id, Vector3Int chunk, Vector3Int chunkPrev)
                {
                    this.id = id;
                    this.chunk = chunk;
                    this.chunkPrev = chunkPrev;
                    this.init_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.reliable_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.non_reliable_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                }

                public void Dispose()
                {
                    init_cache.Dispose();
                    reliable_cache.Dispose();
                    non_reliable_cache.Dispose();
                }
            }

            private class Chunk : IDisposable
            {
                public List<int> deleted_objects;
                public List<int> created_objects;
                public List<ChangedObject> changed_objects;
                public List<int> unchanged_objects;

                private ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> deleted_cache;
                private ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> created_cache;
                private ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> created_init_cache;
                private ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> unchanged_hidden_cache;
                private ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> unchanged_shown_cache;
                private ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> unchanged_shown_init_cache;
                private ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> unchanged_reliable_cache;
                private ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> unchanged_non_reliable_cache;
                public ConcurrentCache<SyncData> unchanged_sync_data_cache;

                public int unchanged_players_count;
                public Vector3Int coords;

                public Chunk(Vector3Int coords)
                {
                    this.coords = coords;
                    this.deleted_objects = new List<int>();
                    this.created_objects = new List<int>();
                    this.changed_objects = new List<ChangedObject>();
                    this.unchanged_objects = new List<int>();
                    this.unchanged_players_count = 0;
                    this.deleted_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.created_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.created_init_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.unchanged_hidden_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.unchanged_shown_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.unchanged_shown_init_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.unchanged_reliable_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.unchanged_non_reliable_cache = new ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>>();
                    this.unchanged_sync_data_cache = new ConcurrentCache<SyncData>();
                }

                private void SerializeDeleteData(SyncData syncData, List<int> objects, ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> cache)
                {
                    if (objects.Count > 0)
                    {
                        if (!cache.HasValue)
                        {
                            cache.Lock();
                            if (!cache.HasValue)
                            {
                                ManagedWrapper<Transport.WriteBuffer> data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(sizeof(int) * objects.Count, Unity.Collections.Allocator.Temp));
                                foreach (int id in objects)
                                    data.Value.Write(id);
                                cache.Set(data);
                            }
                            cache.Unlock();
                        }
                        syncData.AddDeletedObjectsData(cache.Value);
                    }
                }
                private void SerializeCreateData(SyncData syncData, List<int> objects, ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> cache)
                {
                    if (objects.Count > 0)
                    {
                        if (!cache.HasValue)
                        {
                            cache.Lock();
                            if (!cache.HasValue)
                            {
                                ManagedWrapper<Transport.WriteBuffer> data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(2 * sizeof(int) * objects.Count, Unity.Collections.Allocator.Temp));
                                foreach (int id in objects)
                                {
                                    data.Value.Write(id);
                                    data.Value.Write(Singleton.objects[id].inst.regId);
                                    data.Value.Write(Singleton.objects[id].inst.createdSyncIteration);
                                }
                                cache.Set(data);
                            }
                            cache.Unlock();
                        }
                        syncData.AddCreatedObjectsData(cache.Value);
                    }
                }
                private void SerializeUpdateData(SyncData syncData, List<int> objects, SYNC_TYPE sync_type, ConcurrentCache<ManagedWrapper<Transport.WriteBuffer>> cache)
                {
                    if (objects.Count > 0)
                    {
                        if (!cache.HasValue)
                        {
                            cache.Lock();
                            if (!cache.HasValue)
                            {
                                ManagedWrapper<Transport.WriteBuffer> data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                                foreach (int id in objects)
                                    Singleton.objects[id].inst.NetworkSerialize(data.Value, sync_type);
                                if (data.Value.Length == 0)
                                {
                                    data.Dispose();
                                    data = null;
                                }
                                cache.Set(data);
                            }
                            cache.Unlock();
                        }
                        if (cache.Value != null)
                        {
                            switch (sync_type)
                            {
                                case SYNC_TYPE.INIT: syncData.AddUpdatedObjectsReliableData(cache.Value); break;
                                case SYNC_TYPE.RELIABLE: syncData.AddUpdatedObjectsReliableData(cache.Value); break;
                                case SYNC_TYPE.NON_RELIABLE: syncData.AddUpdatedObjectsNonReliableData(cache.Value); break;
                            }
                        }
                    }
                }

                public void SerializeAsEnterChunk(SyncSessionInfo session, SyncData syncData)
                {
                    SerializeCreateData(syncData, created_objects, created_cache);
                    SerializeUpdateData(syncData, created_objects, SYNC_TYPE.INIT, created_init_cache);
                    SerializeCreateData(syncData, unchanged_objects, unchanged_shown_cache);
                    SerializeUpdateData(syncData, unchanged_objects, SYNC_TYPE.INIT, unchanged_shown_init_cache);

                    ManagedWrapper<Transport.WriteBuffer> created_data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                    foreach (ChangedObject inst in changed_objects)
                    {
                        if (inst.chunk == coords)
                        {
                            ISerializableCache inst_tmp = inst;
                            if (!session.chunkPrev.IsValid() || !IsVisibleChunk(session.chunkPrev, inst.chunkPrev)) // changed shown
                            {
                                created_data.Value.Write(inst.id);
                                created_data.Value.Write(Singleton.objects[inst.id].inst.regId);
                                created_data.Value.Write(Singleton.objects[inst.id].inst.createdSyncIteration);
                                inst_tmp.SerializeUpdateData(syncData, SYNC_TYPE.INIT);
                            }
                            else // changed visible
                            {
                                inst_tmp.SerializeUpdateData(syncData, SYNC_TYPE.RELIABLE);
                                inst_tmp.SerializeUpdateData(syncData, SYNC_TYPE.NON_RELIABLE);
                            }
                        }
                    }
                    if (created_data.Value.Length > 0)
                        syncData.AddCreatedObjectsData(created_data);
                    created_data.Dispose();
                }
                public void SerializeAsLeaveChunk(SyncSessionInfo session, SyncData syncData)
                {
                    SerializeDeleteData(syncData, deleted_objects, deleted_cache);
                    SerializeDeleteData(syncData, unchanged_objects, unchanged_hidden_cache);

                    ManagedWrapper<Transport.WriteBuffer> deleted_data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                    foreach (ChangedObject inst in changed_objects)
                        if (inst.chunkPrev == coords && (!session.chunk.IsValid() || !IsVisibleChunk(session.chunk, inst.chunk))) // changed hidden
                            deleted_data.Value.Write(inst.id);
                    if (deleted_data.Value.Length > 0)
                        syncData.AddDeletedObjectsData(deleted_data);
                    deleted_data.Dispose();
                }
                public void SerializeAsExistChunk(SyncSessionInfo session, SyncData syncData)
                {
                    SerializeDeleteData(syncData, deleted_objects, deleted_cache);
                    SerializeCreateData(syncData, created_objects, created_cache);
                    SerializeUpdateData(syncData, created_objects, SYNC_TYPE.INIT, created_init_cache);
                    SerializeUpdateData(syncData, unchanged_objects, SYNC_TYPE.RELIABLE, unchanged_reliable_cache);
                    SerializeUpdateData(syncData, unchanged_objects, SYNC_TYPE.NON_RELIABLE, unchanged_non_reliable_cache);

                    ManagedWrapper<Transport.WriteBuffer> deleted_data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                    ManagedWrapper<Transport.WriteBuffer> created_data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));

                    foreach (ChangedObject inst in changed_objects)
                    {
                        if (inst.chunk == coords)
                        {
                            ISerializableCache inst_tmp = inst;
                            if (!IsVisibleChunk(session.chunkPrev, inst.chunkPrev)) // changed shown
                            {
                                created_data.Value.Write(inst.id);
                                created_data.Value.Write(Singleton.objects[inst.id].inst.regId);
                                created_data.Value.Write(Singleton.objects[inst.id].inst.createdSyncIteration);
                                inst_tmp.SerializeUpdateData(syncData, SYNC_TYPE.INIT);
                            }
                            else // changed visible
                            {
                                inst_tmp.SerializeUpdateData(syncData, SYNC_TYPE.RELIABLE);
                                inst_tmp.SerializeUpdateData(syncData, SYNC_TYPE.NON_RELIABLE);
                            }
                        }
                        else if (!IsVisibleChunk(session.chunk, inst.chunk)) // changed hidden
                            deleted_data.Value.Write(inst.id);
                    }

                    if (deleted_data.Value.Length > 0)
                        syncData.AddDeletedObjectsData(deleted_data);
                    if (created_data.Value.Length > 0)
                        syncData.AddCreatedObjectsData(created_data);
                    deleted_data.Dispose();
                    created_data.Dispose();
                }


                public void Dispose()
                {
                    foreach (ChangedObject inst in changed_objects)
                        inst.Dispose();
                    deleted_cache.Dispose();
                    created_cache.Dispose();
                    created_init_cache.Dispose();
                    unchanged_hidden_cache.Dispose();
                    unchanged_shown_cache.Dispose();
                    unchanged_shown_init_cache.Dispose();
                    unchanged_reliable_cache.Dispose();
                    unchanged_non_reliable_cache.Dispose();
                    unchanged_sync_data_cache.Dispose();
                }
            }

            private BBox bbox;
            private BBox bbox_prev;
            private Vector3Int bbox_size;
            private List<List<List<Chunk>>> chunks;
            private SyncData public_global_init_sync_data;
            private SyncData public_global_update_sync_data;

            private static float AlignCoord(float value, float step)
            {
                float result = ((int)(value / step)) * step;
                if (result > value)
                    result -= step;
                return result;
            }
            private void UpdateGrid()
            {
                BBox bbox_curr = new BBox();
                bbox_curr.Reset();
                foreach (SyncSessionInfo session in sessions)
                {
                    if (IsReadySession(session))
                    {
                        GameObject go = Singleton.players[session.player_id].gameObject;
                        if (go != null)
                            bbox_curr.AddPoint(go.transform.position);
                    }
                }
                foreach (NetworkObjectInfo info in objects)
                {
                    if (info != null && IsOwnerLocalObject(info))
                        bbox_curr.AddPoint(info.inst.rootNetworkObject.transform.position);
                }
                foreach (NetworkOwnerObjectInfo info in deleted_objects)
                {
                    if (IsOwnerLocalObject(info))
                    {
                        NetworkOwnerLocalObjectInfo local_info = (NetworkOwnerLocalObjectInfo)info;
                        if (local_info.posPrev.IsValid())
                            bbox_curr.AddPoint(local_info.posPrev);
                    }
                }

                bbox = bbox_prev;
                bbox.AddPoint(bbox_curr.Min);
                bbox.AddPoint(bbox_curr.Max);
                bbox_prev = bbox_curr;

                Vector3 origin;
                origin.x = AlignCoord(bbox.Min.x, Singleton.chunkSize.x);
                origin.y = AlignCoord(bbox.Min.y, Singleton.chunkSize.y);
                origin.z = AlignCoord(bbox.Min.z, Singleton.chunkSize.z);
                bbox.Min = origin;
                Vector3Int max_coords = GetChunkCoordsByPos(bbox.Max);
                bbox_size = max_coords + Vector3Int.one;
            }

            private Vector3Int GetChunkCoordsByPos(Vector3 pos)
            {
                Vector3Int coords = new Vector3Int();
                Vector3 v = pos - bbox.Min;
                coords.x = (int)(v.x / Singleton.chunkSize.x);
                coords.y = (int)(v.y / Singleton.chunkSize.y);
                coords.z = (int)(v.z / Singleton.chunkSize.z);
                return coords;
            }

            private bool IsValidChunk(Vector3Int coords)
            {
                return coords.x >= 0 && coords.x < bbox_size.x &&
                       coords.y >= 0 && coords.y < bbox_size.y &&
                       coords.z >= 0 && coords.z < bbox_size.z;
            }
            private static bool IsVisibleChunk(Vector3Int chunk_from, Vector3Int chunk_to)
            {
                Vector3Int distance = chunk_to - chunk_from;
                return Math.Abs(distance.x) <= Singleton.viewDistance.x &&
                       Math.Abs(distance.y) <= Singleton.viewDistance.y &&
                       Math.Abs(distance.z) <= Singleton.viewDistance.z;
            }

            private void AttachNetworkObjectToChunk(NetworkOwnerLocalObjectInfo info)
            {
                NetworkObject inst = info.inst;
                Vector3Int chunk_curr = inst != null ? GetChunkCoordsByPos(inst.rootNetworkObject.transform.position) : Vector3IntMax;
                Vector3Int chunk_prev = info.posPrev.IsValid() ? GetChunkCoordsByPos(info.posPrev) : Vector3IntMax;
                Vector3Int chunk_coords = chunk_curr.IsValid() ? chunk_curr : (chunk_prev.IsValid() ? chunk_prev : Vector3IntMax);
                if (chunk_coords.IsValid())
                {
                    if (info.visible_type == VISIBLE_TYPE.PRIVATE_LOCAL)
                    {
                        ((NetworkOwnerPrivateLocalObjectInfo)info).chunk = chunk_curr;
                        ((NetworkOwnerPrivateLocalObjectInfo)info).chunkPrev = chunk_prev;
                    }
                    else
                    {
                        if (!chunk_curr.IsValid())
                            chunks[chunk_coords.x][chunk_coords.y][chunk_coords.z].deleted_objects.Add(info.id);
                        else
                        {
                            if (chunk_curr == chunk_prev)
                                chunks[chunk_coords.x][chunk_coords.y][chunk_coords.z].unchanged_objects.Add(info.id);
                            else if (chunk_prev.IsValid())
                            {
                                ChangedObject unit = new ChangedObject(info.id, chunk_curr, chunk_prev);
                                chunks[chunk_curr.x][chunk_curr.y][chunk_curr.z].changed_objects.Add(unit);
                                chunks[chunk_prev.x][chunk_prev.y][chunk_prev.z].changed_objects.Add(unit);
                            }
                            else
                                chunks[chunk_coords.x][chunk_coords.y][chunk_coords.z].created_objects.Add(info.id);
                        }
                    }
                    if (inst != null)
                        info.posPrev = inst.rootNetworkObject.transform.position;
                }
            }

            private void UpdateChunks()
            {
                chunks = new List<List<List<Chunk>>>(bbox_size.x);
                for (int x = 0; x < bbox_size.x; x++)
                {
                    chunks.Add(new List<List<Chunk>>(bbox_size.y));
                    for (int y = 0; y < bbox_size.y; y++)
                    {
                        chunks[x].Add(new List<Chunk>(bbox_size.z));
                        for (int z = 0; z < bbox_size.z; z++)
                            chunks[x][y].Add(new Chunk(new Vector3Int(x, y, z)));
                    }
                }

                // Add players
                for (int i = 0; i < sessions.Count; i++)
                {
                    SyncSessionInfo session = sessions[i];
                    if (IsReadySession(session))
                    {
                        GameObject go = Singleton.players[session.player_id].gameObject;
                        session.chunk = go != null ? GetChunkCoordsByPos(go.transform.position) : Vector3IntMax;
                        session.chunkPrev = session.posPrev.IsValid() ? GetChunkCoordsByPos(session.posPrev) : Vector3IntMax;
                        if (session.chunk.IsValid())
                        {
                            if (session.chunk == session.chunkPrev)
                                Interlocked.Increment(ref chunks[session.chunk.x][session.chunk.y][session.chunk.z].unchanged_players_count);
                            session.posPrev = go.transform.position;
                        }
                        else if (session.posPrev.IsValid())
                            session.posPrev = Vector3Max;
                    }
                }
                // Add local objects
                foreach (NetworkObjectInfo info in objects)
                    if (info != null && IsOwnerLocalObject(info))
                        AttachNetworkObjectToChunk((NetworkOwnerLocalObjectInfo)info);
                // Add deleted local objects
                foreach (NetworkOwnerObjectInfo info in deleted_objects)
                    if (IsOwnerLocalObject(info))
                        AttachNetworkObjectToChunk((NetworkOwnerLocalObjectInfo)info);
            }

            public void PreUpdate()
            {
                foreach (NetworkObjectInfo info in objects)
                    if (info != null && info.inst.IsOwner)
                        info.inst.BeforeSendMessage();
                session_counter = 0;
                // PUBLIC_GLOBAL
                if (active_sessions_count > 0 && public_global_objects_count > 0)
                {
                    Transport.WriteBuffer tmp_buffer = new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp);
                    Transport.WriteBuffer init_buffer = new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp);
                    ManagedWrapper<Transport.WriteBuffer> tmp_data = new ManagedWrapper<Transport.WriteBuffer>(tmp_buffer);
                    ManagedWrapper<Transport.WriteBuffer> init_data = new ManagedWrapper<Transport.WriteBuffer>(init_buffer);

                    int update_sessions_count = active_sessions_count - init_sessions_count;
                    if (update_sessions_count > 0)
                    {
                        public_global_update_sync_data = new SyncData(this.id, Singleton.update_counter, Singleton.time);
                        if (deleted_objects.Count > 0) // deleted objects
                        {
                            foreach (NetworkOwnerObjectInfo info in deleted_objects)
                                if (info.visible_type == VISIBLE_TYPE.PUBLIC_GLOBAL)
                                    tmp_data.Value.Write(info.id);
                            if (tmp_data.Value.Length > 0)
                            {
                                public_global_update_sync_data.AddDeletedObjectsData(tmp_data);
                                tmp_data.Value.Length = 0;
                            }
                        }
                        foreach (NetworkObjectInfo info in objects)
                        {
                            if (info != null && IsOwnerPublicGlobalObject(info))
                            {
                                NetworkOwnerObjectInfo owner_info = (NetworkOwnerObjectInfo)info;
                                if (owner_info.status == NetworkOwnerObjectInfo.SYNC_STATUS.INIT)  // created objects
                                {
                                    tmp_data.Value.Write(info.inst.id);
                                    tmp_data.Value.Write(info.inst.regId);
                                    tmp_data.Value.Write(info.inst.createdSyncIteration);
                                    public_global_update_sync_data.AddCreatedObjectsData(tmp_data);
                                    tmp_data.Value.Length = 0;
                                    info.inst.NetworkSerialize(init_data.Value, SYNC_TYPE.INIT);
                                }
                                else if (owner_info.status == NetworkOwnerObjectInfo.SYNC_STATUS.ENABLE) // exists objects
                                {
                                    info.inst.NetworkSerialize(tmp_data.Value, SYNC_TYPE.RELIABLE);
                                    if (tmp_data.Value.Length > 0)
                                    {
                                        public_global_update_sync_data.AddUpdatedObjectsReliableData(tmp_data);
                                        tmp_data.Value.Length = 0;
                                    }
                                    info.inst.NetworkSerialize(tmp_data.Value, SYNC_TYPE.NON_RELIABLE);
                                    if (tmp_data.Value.Length > 0)
                                    {
                                        public_global_update_sync_data.AddUpdatedObjectsNonReliableData(tmp_data);
                                        tmp_data.Value.Length = 0;
                                    }
                                }
                            }
                        }
                        if (init_data.Value.Length > 0)
                            public_global_update_sync_data.AddUpdatedObjectsReliableData(init_data);
                    }

                    if (init_sessions_count > 0)
                    {
                        public_global_init_sync_data = new SyncData(this.id, Singleton.update_counter, Singleton.time);
                        foreach (NetworkObjectInfo info in objects)
                        {
                            if (info != null && IsOwnerPublicGlobalObject(info))
                            {
                                tmp_data.Value.Write(info.inst.id);
                                tmp_data.Value.Write(info.inst.regId);
                                tmp_data.Value.Write(info.inst.createdSyncIteration);
                                public_global_init_sync_data.AddCreatedObjectsData(tmp_data);
                                tmp_data.Value.Length = 0;
                                if (((NetworkOwnerObjectInfo)info).status == NetworkOwnerObjectInfo.SYNC_STATUS.ENABLE ||
                                    (update_sessions_count == 0 && ((NetworkOwnerObjectInfo)info).status == NetworkOwnerObjectInfo.SYNC_STATUS.INIT))
                                {
                                    info.inst.NetworkSerialize(tmp_data.Value, SYNC_TYPE.INIT);
                                    if (tmp_data.Value.Length > 0)
                                    {
                                        public_global_init_sync_data.AddUpdatedObjectsReliableData(tmp_data);
                                        tmp_data.Value.Length = 0;
                                    }
                                }
                            }
                        }
                        if (init_data.Value.Length > 0)
                            public_global_init_sync_data.AddUpdatedObjectsReliableData(init_data);
                    }
                    tmp_data.Dispose();
                    init_data.Dispose();
                }
                // PUBLIC_LOCAL, PRIVATE_LOCAL
                if (public_local_objects_count > 0 || private_local_objects_count > 0)
                {
                    UpdateGrid();
                    UpdateChunks();
                }
                else if (bbox_prev.Min.IsValid())
                {
                    bbox_prev.Reset();
                    foreach (SyncSessionInfo session in sessions)
                        if (IsReadySession(session))
                        {
                            session.posPrev = Vector3Max;
                            session.chunk = Vector3IntMax;
                        }
                    foreach (NetworkObjectInfo info in objects)
                        if (info != null && IsOwnerLocalObject(info))
                            ((NetworkOwnerLocalObjectInfo)info).posPrev = Vector3Max;
                }
            }

            public void Update()
            {
                //bool global_update = (active_sessions_count > 0 && (public_global_objects_count > 0 || private_global_objects_count > 0));
                //bool local_update = (active_sessions_with_attach_object_count > 0 && (public_local_objects_count > 0 || private_local_objects_count > 0));
                if (active_sessions_count > 0)
                {
                    int sid = session_counter;
                    while (sid < sessions.Count)
                    {
                        // Capture new sid
                        int sid_cmp = sid;
                        sid = Interlocked.CompareExchange(ref session_counter, sid + 1, sid_cmp);
                        if (sid != sid_cmp || !IsReadySession(sessions[sid]))
                            continue;

                        SyncSessionInfo session = sessions[sid];
                        // Sending private messages from previous synchronization
                        session.prev_sync_vrivate_messages.Lock();
                        if (session.prev_sync_vrivate_messages.Value != null)
                        {
                            session.prev_sync_vrivate_messages.Value.Send(session.player_id);
                            session.prev_sync_vrivate_messages.Value = null;
                        }
                        session.prev_sync_vrivate_messages.Unlock();
                        // public global update
                        SyncData syncData;
                        bool is_player_chunk_unchanged = session.chunk.IsValid() && session.chunk == session.chunkPrev;
                        if (is_player_chunk_unchanged && chunks[session.chunk.x][session.chunk.y][session.chunk.z].unchanged_sync_data_cache.HasValue)
                            syncData = chunks[session.chunk.x][session.chunk.y][session.chunk.z].unchanged_sync_data_cache.Value.Copy();
                        else
                        {
                            if (session.init == true)
                            {
                                session.init = false;
                                init_sessions_count -= 1;
                                if (public_global_init_sync_data != null)
                                    syncData = public_global_init_sync_data.Copy();
                                else
                                    syncData = new SyncData(this.id, Singleton.update_counter, Singleton.time);
                            }
                            else if (public_global_update_sync_data != null)
                                syncData = public_global_update_sync_data.Copy();
                            else
                                syncData = new SyncData(this.id, Singleton.update_counter, Singleton.time);

                            // public local update
                            if (public_local_objects_count > 0)
                            {
                                Vector3Int center = session.chunk.IsValid() ? session.chunk : (session.chunkPrev.IsValid() ? session.chunkPrev : Vector3IntMax);
                                if (center.IsValid())
                                {
                                    Vector3Int offset = Vector3Int.zero;
                                    for (offset.x = -Singleton.viewDistance.x; offset.x <= Singleton.viewDistance.x; offset.x++)
                                    {
                                        for (offset.y = -Singleton.viewDistance.y; offset.y <= Singleton.viewDistance.y; offset.y++)
                                        {
                                            for (offset.z = -Singleton.viewDistance.z; offset.z <= Singleton.viewDistance.z; offset.z++)
                                            {
                                                Vector3Int coords = center + offset;
                                                if (!session.chunk.IsValid())
                                                {
                                                    if (IsValidChunk(coords)) // leave chunk
                                                        chunks[coords.x][coords.y][coords.z].SerializeAsLeaveChunk(session, syncData);
                                                }
                                                else
                                                {
                                                    if (!session.chunkPrev.IsValid())
                                                    {
                                                        if (IsValidChunk(coords)) // enter chunk
                                                            chunks[coords.x][coords.y][coords.z].SerializeAsEnterChunk(session, syncData);
                                                    }
                                                    else
                                                    {
                                                        if (session.chunk == session.chunkPrev)
                                                        {
                                                            if (IsValidChunk(coords)) // exist chunk
                                                                chunks[coords.x][coords.y][coords.z].SerializeAsExistChunk(session, syncData);
                                                        }
                                                        else
                                                        {
                                                            if (IsValidChunk(coords))
                                                            {
                                                                if (!IsVisibleChunk(session.chunkPrev, coords)) // enter chunk
                                                                    chunks[coords.x][coords.y][coords.z].SerializeAsEnterChunk(session, syncData);
                                                                else // exist chunk
                                                                    chunks[coords.x][coords.y][coords.z].SerializeAsExistChunk(session, syncData);
                                                            }
                                                            Vector3Int diff = session.chunk - session.chunkPrev;
                                                            Vector3Int coords_prev = coords - diff;
                                                            if (IsValidChunk(coords_prev))
                                                            {
                                                                if (!IsVisibleChunk(session.chunk, coords_prev)) // leave chunk
                                                                    chunks[coords_prev.x][coords_prev.y][coords_prev.z].SerializeAsLeaveChunk(session, syncData);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (is_player_chunk_unchanged && chunks[session.chunk.x][session.chunk.y][session.chunk.z].unchanged_players_count >= Singleton.minPlayersToCache)
                                    {
                                        if (!chunks[session.chunk.x][session.chunk.y][session.chunk.z].unchanged_sync_data_cache.HasValue)
                                            if (chunks[session.chunk.x][session.chunk.y][session.chunk.z].unchanged_sync_data_cache.TrySet(syncData))
                                                syncData = chunks[session.chunk.x][session.chunk.y][session.chunk.z].unchanged_sync_data_cache.Value.Copy();
                                    }
                                }
                            }
                        }

                        // private global and local update
                        ManagedWrapper<Transport.WriteBuffer> deleted_data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                        ManagedWrapper<Transport.WriteBuffer> created_data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                        foreach (NetworkOwnerObjectInfo info in session.unlinking_objects)
                        {
                            if (info.visible_type == VISIBLE_TYPE.PRIVATE_GLOBAL ||
                                 (info.visible_type == VISIBLE_TYPE.PRIVATE_LOCAL &&
                                 session.chunkPrev.IsValid() &&
                                 IsVisibleChunk(session.chunkPrev, ((NetworkOwnerPrivateLocalObjectInfo)info).chunkPrev))
                               )
                            {
                                deleted_data.Value.Write(info.id);
                            }
                        }
                        foreach (NetworkOwnerObjectInfo info in session.linking_objects)
                        {
                            if (info.visible_type == VISIBLE_TYPE.PRIVATE_GLOBAL ||
                                 (info.visible_type == VISIBLE_TYPE.PRIVATE_LOCAL &&
                                 session.chunk.IsValid() &&
                                 IsVisibleChunk(session.chunk, ((NetworkOwnerPrivateLocalObjectInfo)info).chunk))
                               )
                            {
                                created_data.Value.Write(info.inst.id);
                                created_data.Value.Write(info.inst.regId);
                                created_data.Value.Write(info.inst.createdSyncIteration);
                                ((ISerializableCache)info).SerializeUpdateData(syncData, SYNC_TYPE.INIT);
                            }
                        }
                        foreach (NetworkOwnerObjectInfo info in session.linked_objects)
                        {
                            if (info.visible_type == VISIBLE_TYPE.PRIVATE_GLOBAL)
                            {
                                ISerializableCache serialize_info = (ISerializableCache)info;
                                serialize_info.SerializeUpdateData(syncData, SYNC_TYPE.RELIABLE);
                                serialize_info.SerializeUpdateData(syncData, SYNC_TYPE.NON_RELIABLE);
                            }
                            else
                            {
                                NetworkOwnerPrivateLocalObjectInfo local_info = (NetworkOwnerPrivateLocalObjectInfo)info;
                                if (session.chunk.IsValid() && IsVisibleChunk(session.chunk, local_info.chunk))
                                {
                                    ISerializableCache serialize_info = (ISerializableCache)info;
                                    if (session.chunkPrev.IsValid() && IsVisibleChunk(session.chunkPrev, local_info.chunkPrev)) // update
                                    {
                                        serialize_info.SerializeUpdateData(syncData, SYNC_TYPE.RELIABLE);
                                        serialize_info.SerializeUpdateData(syncData, SYNC_TYPE.NON_RELIABLE);
                                    }
                                    else // show
                                    {
                                        created_data.Value.Write(info.inst.id);
                                        created_data.Value.Write(info.inst.regId);
                                        created_data.Value.Write(info.inst.createdSyncIteration);
                                        serialize_info.SerializeUpdateData(syncData, SYNC_TYPE.INIT);
                                    }
                                }
                                else
                                {
                                    if (session.chunkPrev.IsValid() && IsVisibleChunk(session.chunkPrev, local_info.chunkPrev)) // hide
                                        deleted_data.Value.Write(info.id);
                                }
                            }
                        }
                        session.linked_objects.AddRange(session.linking_objects);
                        session.linking_objects = new List<NetworkOwnerObjectInfo>();
                        session.unlinking_objects = new List<NetworkOwnerObjectInfo>();

                        if (deleted_data.Value.Length > 0)
                            syncData.AddDeletedObjectsData(deleted_data);
                        if (created_data.Value.Length > 0)
                            syncData.AddCreatedObjectsData(created_data);
                        deleted_data.Dispose();
                        created_data.Dispose();
                        syncData.Send(session.player_id);
                        // Sending private messages from current synchronization
                        session.curr_sync_vrivate_messages.Lock();
                        if (session.curr_sync_vrivate_messages.Value != null)
                        {
                            session.curr_sync_vrivate_messages.Value.Send(session.player_id);
                            session.curr_sync_vrivate_messages.Value = null;
                        }
                        session.curr_sync_vrivate_messages.Unlock();
                    }
                }
            }

            public void PostUpdate()
            {
                if (deleted_objects.Count > 0)
                {
                    foreach (NetworkOwnerObjectInfo info in deleted_objects)
                        switch (info.visible_type)
                        {
                            case VISIBLE_TYPE.PUBLIC_LOCAL:
                                public_local_objects_count -= 1;
                                break;
                            case VISIBLE_TYPE.PUBLIC_GLOBAL:
                                public_global_objects_count -= 1;
                                break;
                            case VISIBLE_TYPE.PRIVATE_LOCAL:
                                ((ISerializableCache)info).Dispose();
                                private_local_objects_count -= 1;
                                break;
                            case VISIBLE_TYPE.PRIVATE_GLOBAL:
                                ((ISerializableCache)info).Dispose();
                                private_global_objects_count -= 1;
                                break;
                        }
                    deleted_objects = new List<NetworkOwnerObjectInfo>();
                }
                foreach (NetworkObjectInfo info in objects)
                {
                    if (info != null && info.inst.IsOwner)
                    {
                        NetworkOwnerObjectInfo owner_info = (NetworkOwnerObjectInfo)info;
                        if (owner_info.status == NetworkOwnerObjectInfo.SYNC_STATUS.INIT)
                            owner_info.status = NetworkOwnerObjectInfo.SYNC_STATUS.ENABLE;
                        if (info.inst.visibleType == VISIBLE_TYPE.PRIVATE_GLOBAL || info.inst.visibleType == VISIBLE_TYPE.PRIVATE_LOCAL)
                            ((ISerializableCache)info).Dispose();
                    }
                }
                if (chunks != null)
                {
                    for (int x = 0; x < bbox_size.x; x++)
                        for (int y = 0; y < bbox_size.y; y++)
                            for (int z = 0; z < bbox_size.z; z++)
                                chunks[x][y][z].Dispose();
                    chunks = null;
                }
                if (public_global_init_sync_data != null)
                {
                    public_global_init_sync_data.Dispose();
                    public_global_init_sync_data = null;
                }
                if (public_global_update_sync_data != null)
                {
                    public_global_update_sync_data.Dispose();
                    public_global_update_sync_data = null;
                }
            }
        }
    }
}
