using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Unity.Jobs;
using Unity.Collections;
using Transport;
using Unity.Collections.LowLevel.Unsafe;

namespace Network
{
    public class RegisterException : Exception
    {
        public RegisterException() { }
        public RegisterException(string message) : base(message) { }
        public RegisterException(string message, Exception inner) : base(message, inner) { }
    }
    public class AvailableException : Exception
    {
        public AvailableException() { }
        public AvailableException(string message) : base(message) { }
        public AvailableException(string message, Exception inner) : base(message, inner) { }
    }
    public class SceneExeption : Exception
    {
        public SceneExeption() { }
        public SceneExeption(string message) : base(message) { }
        public SceneExeption(string message, Exception inner) : base(message, inner) { }
    }
    public enum SYNC_SESSION_STATE
    {
        NONE = 0,
        INIT_STATE = 1,
        READY_STATE = 2,
        RELEASE_STATE = 3
    }

    public enum SYNC_ERR_CODE : byte
    {
        NONE = 0,
        NOT_PERMISSIONS = 1,
        SCENE_ALREADY_SYNC = 2,
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    public partial class NetworkSceneManager : MonoBehaviour
    {
        private class PlayerInfo
        {
            public class SyncObjectInfo
            {
                public enum SYNC_STATUS
                {
                    DISABLE = 0,
                    INIT = 1,
                    ENABLE = 2
                }
                public SYNC_STATUS send_status; // used for send answer update message to owner
                public SYNC_STATUS receive_status; // used for receive update message from owner
                public NetworkObject inst;

                public SyncObjectInfo(NetworkObject inst, SYNC_STATUS receive_status)
                {
                    this.inst = inst;
                    this.send_status = SYNC_STATUS.DISABLE;
                    this.receive_status = receive_status;
                }
            }

            public class SyncSceneInfo : IDisposable
            {
                public int scene_id;
                public int sid;  // synchronization id
                public int last_sync_iteration; // last synchronization number for scene received from this player
                public int message_counter; // used for concurrent deserialize update messages
                public int object_counter; // used for concurrent serialize update messages for slaves objects
                public ConcurrentQueue<ReceiveData> update_messages;

                public SyncSceneInfo(int scene_id, int sid)
                {
                    this.scene_id = scene_id;
                    this.sid = sid;
                    this.last_sync_iteration = -1;
                    this.message_counter = 0;
                    this.update_messages = new ConcurrentQueue<ReceiveData>();
                }

                public void Dispose()
                {
                    if (update_messages != null)
                    {
                        ReceiveData data;
                        while (update_messages.TryDequeue(out data))
                            data.Dispose();
                        update_messages = null;
                    }
                }
            }

            public int id;
            public GameObject gameObject; // gameObject attached to player
            public List<SyncObjectInfo> objects; // objects that owned by this player
            public List<SyncSceneInfo> scenes; // scenes that are synchronized with this player
            public int last_iteration; // last synchronization number received from this player used for time sync
            public float time; // time on player side

            public PlayerInfo(int id)
            {
                this.id = id;
                objects = new List<SyncObjectInfo>();
                scenes = new List<SyncSceneInfo>();
                last_iteration = 0;
                time = 0.0f;
            }

            /// <summary>
            /// Concurrent method
            /// Takes messages from the queue and deserializes them to objects owned by the player in scene
            /// </summary>
            /// <param name="scene_index">Synchronized scene index</param>
            public unsafe void DeserializeUpdateMessages(int scene_index)
            {
                int counter_value = scenes[scene_index].message_counter;
                while (counter_value > 0)
                {
                    int counter_value_cmp = counter_value;
                    counter_value = Interlocked.CompareExchange(ref scenes[scene_index].message_counter, counter_value - 1, counter_value_cmp);
                    if (counter_value != counter_value_cmp)
                        continue;

                    ReceiveData data;
                    ReceiveData deferred_data = null;
                    scenes[scene_index].update_messages.TryDequeue(out data);
                    int head_begin = data.buffer.Context;
                    float send_time = data.buffer.ReadFloat();
                    int iteration = data.buffer.ReadInt();
                    int head_size = data.buffer.Context - head_begin;

                    // time sync
                    if (time == 0.0f)
                    {
                        time = send_time;
                        last_iteration = iteration;
                    }
                    else if (iteration > last_iteration)
                    {
                        time += (send_time - time) * TIME_SMOOTHING_FACTOR;
                        last_iteration = iteration;
                    }
                    // ---

                    Transport.ReadBuffer buffer = data.buffer;

                    while (buffer.Context < buffer.Length)
                    {
                        bool is_deferred_object = false;
                        int obj_begin = buffer.Context;
                        int object_id = buffer.ReadInt();
                        int object_sync_iteration = data.buffer.ReadInt();
                        byte object_sync_type = data.buffer.ReadByte();
                        if (objects[object_id] != null &&
                            objects[object_id].inst.createdSyncIteration == object_sync_iteration &&
                            objects[object_id].receive_status != SyncObjectInfo.SYNC_STATUS.DISABLE)
                        {
                            if (objects[object_id].receive_status == SyncObjectInfo.SYNC_STATUS.INIT && (SYNC_TYPE)object_sync_type == SYNC_TYPE.INIT)
                            {
                                Singleton.activate_objects_queue.Enqueue(objects[object_id].inst);
                                objects[object_id].receive_status = SyncObjectInfo.SYNC_STATUS.ENABLE;
                            }
                            if (objects[object_id].receive_status == SyncObjectInfo.SYNC_STATUS.ENABLE)
                            {
                                objects[object_id].inst.NetworkDeserialize(id, buffer, send_time);
                                Singleton.deserialized_objects_queue.Enqueue(objects[object_id].inst);
                                if (objects[object_id].send_status == SyncObjectInfo.SYNC_STATUS.DISABLE)
                                    objects[object_id].send_status = SyncObjectInfo.SYNC_STATUS.INIT;
                            }
                            else if ((SYNC_TYPE)object_sync_type == SYNC_TYPE.RELIABLE_UNSEQUENTIAL)
                                is_deferred_object = true;
                        }
                        else
                        {
                            if (iteration >= scenes[scene_index].last_sync_iteration && (SYNC_TYPE)object_sync_type == SYNC_TYPE.RELIABLE_UNSEQUENTIAL)
                                is_deferred_object = true;
                        }
                        int obj_size = SyncUpdateData.GetUpdateUnitSize(buffer.Pointer, obj_begin);
                        if (is_deferred_object)
                        {
                            if (deferred_data == null) // create deferred package
                            {
                                deferred_data = NetworkManager.Singleton.CreateInputDataBuffer();
                                ReadBuffer tmp_buffer = deferred_data.buffer;
                                UnsafeUtility.MemCpy(tmp_buffer.Pointer, buffer.Pointer + head_begin, head_size); // send_time + iteration
                                tmp_buffer.Length = head_size;
                            }
                            ReadBuffer deferred_buffer = deferred_data.buffer;
                            UnsafeUtility.MemCpy(deferred_buffer.Pointer + deferred_buffer.Length, buffer.Pointer + obj_begin, obj_size); // object update data
                            deferred_buffer.Length += obj_size;
                        }
                        buffer.Context = obj_begin + obj_size;
                    }

                    if (deferred_data != null)
                        scenes[scene_index].update_messages.Enqueue(deferred_data);
                    data.Dispose();
                }
            }

            /// <summary>
            /// Concurrent method
            /// Serializes the states of network objects in scene owned by the player into buffers for subsequent sending to the player.
            /// </summary>
            /// <param name="scene_index">Synchronized scene index</param>
            /// <param name="reliable_data">Buffer for guaranteed delivery</param>
            /// <param name="non_reliable_data">Buffer for not guaranteed delivery</param>
            public void SerializeUpdateMessages(int scene_index, Transport.WriteBuffer reliable_data, Transport.WriteBuffer non_reliable_data)
            {
                int object_id = scenes[scene_index].object_counter;
                while (object_id < objects.Count)
                {
                    // Capture new object
                    int object_id_cmp = object_id;
                    object_id = Interlocked.CompareExchange(ref scenes[scene_index].object_counter, object_id + 1, object_id_cmp);
                    if (object_id != object_id_cmp)
                        continue;

                    SyncObjectInfo obj_info = objects[object_id];
                    if (obj_info != null && obj_info.inst.sceneId == scenes[scene_index].scene_id && obj_info.send_status != SyncObjectInfo.SYNC_STATUS.DISABLE)
                    {
                        switch (obj_info.send_status)
                        {
                            case SyncObjectInfo.SYNC_STATUS.INIT:
                                obj_info.inst.NetworkSerialize(reliable_data, SYNC_TYPE.INIT);
                                obj_info.send_status = SyncObjectInfo.SYNC_STATUS.ENABLE;
                                break;
                            case SyncObjectInfo.SYNC_STATUS.ENABLE:
                                obj_info.inst.NetworkSerialize(reliable_data, SYNC_TYPE.RELIABLE);
                                obj_info.inst.NetworkSerialize(non_reliable_data, SYNC_TYPE.NON_RELIABLE);
                                break;
                        }
                    }
                }
            }
        }

        private class ObjectInfo
        {
            public NetworkObject inst;

            public ObjectInfo(NetworkObject inst)
            {
                this.inst = inst;
            }
        }

        private static readonly float TIME_SMOOTHING_FACTOR = 0.01f;

        public static IEnumerable<T> FindObjectsOfType<T>(Scene scene) where T : MonoBehaviour
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                foreach (T no in go.GetComponentsInChildren<T>(true))
                    yield return no;
        }

        public delegate void SyncWithPlayerHandler(int player_id, int scene_id, int sid, SYNC_ERR_CODE err_code);
        public delegate void DesyncWithPlayerHandler(int player_id, int scene_id);
        //public delegate void PostUpdateTask();

        public event SyncWithPlayerHandler OnSceneSyncWithPlayer;
        public event DesyncWithPlayerHandler OnSceneDesyncWithPlayer;

        private static NetworkSceneManager instance = null;

        // --- inspector ----
#if UNITY_EDITOR
        [SerializeField]
        private List<SceneAsset> registeredScenes;
#endif
        [SerializeField]
        private List<NetworkObject> registeredPrefabs;
        [Header("Network Space Parameters")]
        [SerializeField]
        private Vector3 chunkSize = new Vector3(16.0f, 16.0f, 16.0f);
        [SerializeField]
        private Vector3Int viewDistance = new Vector3Int(1, 1, 1);
        [SerializeField]
        private float updatePeriod = 0.05f;
        [SerializeField]
        [Min(2)]
        private int minPlayersToCache = 2;
        [SerializeField]
        public float streamClientDelay = 0.1f;
        [Space]
        public AsyncData asyncMode;
        // --- internal ----
        [SerializeField, HideInInspector]
        private List<string> networkScenesPaths;
        private Dictionary<string, int> sceneIds;
        private List<NetworkScene> networkScenes; // all network scenes
        private ConcurrentQueue<NetworkObject> activate_objects_queue;
        private ConcurrentQueue<NetworkObject> deserialized_objects_queue;
        private List<ObjectInfo> objects;  // network objects with owner flag is true
        private List<PlayerInfo> players; // all connections
        private int update_counter;  // sync scenes iteration counter
        private bool isUpdating;

        public float time { get; private set; }
        public float lastUpdateTime { get; private set; }
        public static NetworkSceneManager Singleton { get { return instance; } }

        private void Awake()
        {
            // -------- ALWAYS -------
            if (instance == null)
                instance = this;
            else
            {
                NetworkManager manager = GetComponent<NetworkManager>();
#if UNITY_EDITOR
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.delayCall += () => DestroyImmediate(this);
                    EditorApplication.delayCall += () => DestroyImmediate(manager);
                }
                else
                {
                    Destroy(this);
                    Destroy(manager);
                }
#else
                Destroy(this);
                Destroy(manager);
#endif
                throw new ComponentException("NetworManager and NetworkSceneManager components can be loaded in a single instance");
            }
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
                return;
#endif
            // ------- PLAY MODE -----

            networkScenes = new List<NetworkScene>(networkScenesPaths.Count);
            sceneIds = new Dictionary<string, int>(networkScenesPaths.Count);
            int counter = 0;
            foreach (var path in networkScenesPaths)
            {
                if (path != "")
                {
                    NetworkScene scene = new NetworkScene(counter, path);
                    networkScenes.Add(scene);
                    sceneIds.Add(path, counter);
                    scene.Reset();
                }
                else
                    networkScenes.Add(default);
                counter++;
            }
            networkScenes[0].OnSceneLoaded(); // Load DontDestroyOnLoad scene

            activate_objects_queue = new ConcurrentQueue<NetworkObject>();
            deserialized_objects_queue = new ConcurrentQueue<NetworkObject>();
            objects = new List<ObjectInfo>();
            players = new List<PlayerInfo>();

            update_counter = -1;
            lastUpdateTime = 0.0f;
            isUpdating = false;

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            NetworkObject.NetworkObjectCreateEvent += OnNetworkObjectCreate;
            NetworkObject.NetworkObjectDeleteEvent += OnNetworkObjectDelete;
            NetworkObject.NetworkObjectsChangeSceneEvent += OnNetworkObjectChangeScene;
            NetworkManager.Singleton.ConnectionEvent += OnPlayerConnected;
            NetworkManager.Singleton.DisconnectionEvent += OnPlayerDisconnected;
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
            this.time = Time.time;
            NetworkManager.Singleton.RegisterHandler((short)MsgType.SyncSceneMessage, OnSyncSceneMessage);
            NetworkManager.Singleton.RegisterHandler((short)MsgType.SyncSceneAnswerMessage, OnSyncSceneAnswerMessage);
            NetworkManager.Singleton.RegisterHandler((short)MsgType.DesyncSceneMessage, OnDesyncSceneMessage);
            NetworkManager.Singleton.RegisterHandler((short)MsgType.DesyncSceneAnswerMessage, OnDesyncSceneAnswerMessage);
            NetworkManager.Singleton.RegisterHandler((short)MsgType.CreateObjectsMessage, OnCreateObjectsMessage);
            NetworkManager.Singleton.RegisterHandler((short)MsgType.DeleteObjectsMessage, OnDeleteObjectsMessage);
            NetworkManager.Singleton.RegisterHandler((short)MsgType.UpdateObjectsFromOwnerMessage, OnUpdateObjectsMessage);
            NetworkManager.Singleton.RegisterHandler((short)MsgType.UpdateObjectsFromSlaveMessage, OnUpdateObjectsAnswerMessage);
        }

        // Update is called once per frame
        void Update()
        {
            // -------- ALWAYS -------
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
                return;
#endif
            // ------- PLAY MODE -----
            this.time = Time.time;
            foreach (PlayerInfo p_info in players)
            {
                if (p_info != null)
                {
                    if (p_info.scenes.Count == 0)
                        p_info.time = 0.0f;
                    else if (p_info.time != 0.0f)
                        p_info.time += Time.deltaTime;
                }
            }
            if (Time.time - lastUpdateTime >= updatePeriod)
            {
                NetworkUpdate();
                lastUpdateTime = Time.time;
            }
        }

        private void NetworkUpdate()
        {
            isUpdating = true;
            update_counter++;

            // Players pre update 
            foreach (PlayerInfo p_info in players)
                if (p_info != null)
                {
                    foreach (PlayerInfo.SyncSceneInfo s_info in p_info.scenes)
                    {
                        if (networkScenes[s_info.scene_id].GetSyncSessionState(s_info.sid) == SYNC_SESSION_STATE.READY_STATE)
                        {
                            if (!s_info.update_messages.IsEmpty)
                                s_info.message_counter = s_info.update_messages.Count;
                            s_info.object_counter = 0;
                        }
                    }
                    foreach (PlayerInfo.SyncObjectInfo obj_info in p_info.objects)
                        if (obj_info != null && obj_info.receive_status != PlayerInfo.SyncObjectInfo.SYNC_STATUS.DISABLE)
                            obj_info.inst.BeforeSendMessage();
                }

            // Scenes pre update
            foreach (NetworkScene scene in networkScenes)
                if (scene != null && scene.IsLoad)
                    scene.PreUpdate();

            // Update scenes and players
            if (asyncMode.enable)
            {
                JobHandle[] handles = new JobHandle[asyncMode.jobs];
                for (int i = 0; i < asyncMode.jobs; i++)
                {
                    UpdateJob job = new UpdateJob();
                    handles[i] = job.Schedule();
                }
                for (int i = 0; i < asyncMode.jobs; i++)
                    handles[i].Complete();
            }
            else
            {
                UpdateScenes();
                UpdatePlayers();
            }

            // Scenes post update
            foreach (NetworkScene scene in networkScenes)
                if (scene != null && scene.IsLoad)
                    scene.PostUpdate();

            // Players post update
            PostUpdate();
            isUpdating = false;
        }

        private void OnDestroy()
        {
            // -------- ALWAYS -------
            if (instance == this)
                instance = null;
#if UNITY_EDITOR
            if (!EditorApplication.isPlaying)
                return;
#endif
            // ------- PLAY MODE -----
        }
        private struct ReceiveJob : IJob
        {
            public int player_id;
            public int scene_index;
            public void Execute()
            {
                Singleton.players[player_id].DeserializeUpdateMessages(scene_index);
            }
        }

        private struct SendJob : IJob
        {
            public int player_id;
            public int scene_id;
            public int index;
            [ReadOnly]
            public NativeList<Transport.WriteBuffer> reliable_data;
            [ReadOnly]
            public NativeList<Transport.WriteBuffer> non_reliable_data;

            public void Execute()
            {
                Singleton.players[player_id].SerializeUpdateMessages(scene_id, reliable_data[index], non_reliable_data[index]);
            }
        }

        private struct UpdateJob : IJob
        {
            public void Execute()
            {
                Singleton.UpdateScenes();
                Singleton.UpdatePlayers();
            }
        }

        private void UpdateScenes()
        {
            foreach (NetworkScene scene in networkScenes)
                if (scene != null && scene.IsLoad)
                    scene.Update();
        }
        private void UpdatePlayers()
        {
            foreach (PlayerInfo p_info in players)
            {
                if (p_info != null)
                {
                    for (int scene_index = 0; scene_index < p_info.scenes.Count; scene_index++)
                    {
                        PlayerInfo.SyncSceneInfo s_info = p_info.scenes[scene_index];
                        if (networkScenes[s_info.scene_id].GetSyncSessionState(s_info.sid) == SYNC_SESSION_STATE.READY_STATE)
                        {
                            if (s_info.message_counter != 0)
                                p_info.DeserializeUpdateMessages(scene_index);

                            ManagedWrapper<Transport.WriteBuffer> reliable_data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                            ManagedWrapper<Transport.WriteBuffer> unreliable_data = new ManagedWrapper<Transport.WriteBuffer>(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                            p_info.SerializeUpdateMessages(scene_index, reliable_data.Value, unreliable_data.Value);

                            SyncUpdateData syncUpdateData = new SyncUpdateData(s_info.scene_id, s_info.last_sync_iteration, p_info.time, MsgType.UpdateObjectsFromSlaveMessage);
                            if (reliable_data.Value.Length > 0)
                                syncUpdateData.AddUpdatedObjectsReliableData(reliable_data);
                            if (unreliable_data.Value.Length > 0)
                                syncUpdateData.AddUpdatedObjectsNonReliableData(unreliable_data);
                            reliable_data.Dispose();
                            unreliable_data.Dispose();
                            syncUpdateData.Send(p_info.id);
                        }
                    }
                }
            }
        }


        // Function return true if scene is registered or scene is DontDestroyOnLoad
        // If scene is DontDestroyOnLoad reg_id set 0
        private bool TryGetSceneRegisteredId(string path, out int reg_id)
        {
            return sceneIds.TryGetValue(path, out reg_id);
        }
        private int GetObjectSceneRegisteredId(NetworkObject instance)
        {
            int reg_id;
            if (!TryGetSceneRegisteredId(instance.gameObject.scene.path, out reg_id))
            {
                Destroy(instance.gameObject);
                throw new CreateObjectException(string.Format("Network object can only be added to a registered scene ({0})", instance.name));
            }
            return reg_id;
        }
        private void CheckPlayer(int id)
        {
            if (id < 0 || id >= players.Count || players[id] == null)
                throw new AvailableException(string.Format("Player with id: {0} does not exist", id));
        }
        private void CheckScene(int id)
        {
            if (id < 0 || id >= networkScenes.Count || networkScenes[id] == null)
                throw new AvailableException(string.Format("Scene with register id: {0} does not exist", id));
        }
        private int GetPlayerSyncWithSceneIndex(int player_id, int scene_id)
        {
            int index = Singleton.players[player_id].scenes.FindIndex(x => x.scene_id == scene_id);
            if (index != -1)
            {
                PlayerInfo.SyncSceneInfo syncSceneInfo = Singleton.players[player_id].scenes[index];
                if (networkScenes[scene_id].GetSyncSessionState(syncSceneInfo.sid) != SYNC_SESSION_STATE.READY_STATE)
                    index = -1;
            }
            return index;
        }
        private int RegisterNetworkObject(NetworkObject instance)
        {
            int object_id = objects.AddOrReplace(new ObjectInfo(instance), (instead) => instead == null);
            instance.id = object_id;
            return object_id;
        }
        private void UnregisterNetworkObject(NetworkObject instance)
        {
            objects[instance.id] = null;
        }
        private void PostUpdate()
        {
            NetworkObject inst;
            while (deserialized_objects_queue.TryDequeue(out inst))
                inst.AfterReceiveMessage();
            while (activate_objects_queue.TryDequeue(out inst))
                inst.NetworkCreate();
        }
        // ------------------- UNITY ENGINE EVENTS ----------------------
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            int reg_id;
            if (TryGetSceneRegisteredId(scene.path, out reg_id))
                networkScenes[reg_id].OnSceneLoaded();
        }

        private void OnSceneUnloaded(Scene scene)
        {
            int reg_id;
            if (TryGetSceneRegisteredId(scene.path, out reg_id))
                networkScenes[reg_id].Reset();
        }

        private void OnNetworkObjectCreate(NetworkObject instance) // without scene placed objects
        {
            int scene_id = GetObjectSceneRegisteredId(instance);
            if (instance.IsOwner)
            {
                RegisterNetworkObject(instance);
                instance.createdSyncIteration = update_counter;
            }
            networkScenes[scene_id].AddNetworkObject(instance);

        }

        private void OnNetworkObjectDelete(NetworkObject instance)
        {
            if (instance.sceneId == -1)
                return;
            networkScenes[instance.sceneId].DeleteNetworkObject(instance);
            if (instance.IsOwner)
                UnregisterNetworkObject(instance);
        }

        private void OnNetworkObjectChangeScene(NetworkObject instance)
        {
            Debug.Log("Change scene object " + instance.name);
            if (instance.IsScenePlaced)
                throw new SceneExeption("Scene placed object cannot change scene");
            int new_scene_id = GetObjectSceneRegisteredId(instance);
            networkScenes[instance.sceneId].DeleteNetworkObject(instance);
            networkScenes[new_scene_id].AddNetworkObject(instance);
        }

        // ------------------- NETWORK EVENTS ----------------------
        private void OnPlayerConnected(int id, ref Transport.ChannelInfo channel)
        {
            players.Add(new PlayerInfo(id), id);
            //networkScenes[0].OnSceneSyncWithPlayer(id); // DontDestoyOnLoad scene
        }
        private void OnPlayerDisconnected(int id, byte err_code, ReceiveData data)
        {
            while (players[id].scenes.Count > 0)
            {
                int scene_id = players[id].scenes[0].scene_id;
                int sid = players[id].scenes[0].sid;
                networkScenes[scene_id].ForceDesyncSceneWithPlayer(sid);
            }
            players[id] = null;
        }
        private void OnSyncSceneMessage(int id, ReceiveData data)
        {
            SyncSceneMessage message = new SyncSceneMessage();
            message.Deserialize(data.buffer);

            int sid = networkScenes[message.scene_id].FindSyncSession(id);
            if (sid == -1) // if session not found
            {
                if (true) //FIXME if true permission
                    if (!networkScenes[message.scene_id].IsLoad)
                        StartCoroutine(networkScenes[message.scene_id].LoadAndReceiveSyncSceneMessage(id, message));
                    else
                        networkScenes[message.scene_id].ReceiveSyncSceneMessage(id, message);
                else // if not permissions
                {
                    SyncSceneMessage answer = new SyncSceneMessage() { scene_id = message.scene_id, err_code = SYNC_ERR_CODE.NOT_PERMISSIONS };
                    NetworkManager.Singleton.Send(id, answer, (short)MsgType.SyncSceneAnswerMessage, ChannelOpts.Reliable);
                }
            }
            else // if session exist
                networkScenes[message.scene_id].ReceiveSyncSceneAnswerMessage(id, sid, message);
        }
        private void OnSyncSceneAnswerMessage(int id, ReceiveData data)
        {
            SyncSceneMessage message = new SyncSceneMessage();
            message.Deserialize(data.buffer);

            int sid = networkScenes[message.scene_id].FindSyncSession(id);
            if (sid != -1)
                networkScenes[message.scene_id].ReceiveSyncSceneAnswerMessage(id, sid, message);
        }
        private void OnDesyncSceneMessage(int id, ReceiveData data)
        {
            DesyncSceneMessage message = new DesyncSceneMessage();
            message.Deserialize(data.buffer);

            int sid = networkScenes[message.scene_id].FindSyncSession(id);
            if (sid != -1)
            {
                if (networkScenes[message.scene_id].GetSyncSessionState(sid) == SYNC_SESSION_STATE.READY_STATE)
                {
                    if (message.unload && networkScenes[message.scene_id].IsLoad && true) //FIXME if true permission
                    {
                        if (SceneManager.sceneCount >= 2 && message.scene_id != 0)
                            StartCoroutine(networkScenes[message.scene_id].UnloadAndReceiveDesyncSceneMessage(id, sid, message));
                        else
                        {
                            networkScenes[message.scene_id].ReceiveDesyncSceneMessage(id, sid, message);
                            networkScenes[message.scene_id].DesyncSceneWithAllPlayers(false);
                        }
                    }
                    else
                        networkScenes[message.scene_id].ReceiveDesyncSceneMessage(id, sid, message);
                }
                else
                    networkScenes[message.scene_id].ReceiveDesyncSceneAnswerMessage(id, sid, message);
            }
        }

        private void OnDesyncSceneAnswerMessage(int id, ReceiveData data)
        {
            DesyncSceneMessage message = new DesyncSceneMessage();
            message.Deserialize(data.buffer);

            int sid = networkScenes[message.scene_id].FindSyncSession(id);
            if (sid != -1)
                networkScenes[message.scene_id].ReceiveDesyncSceneAnswerMessage(id, sid, message);
        }
        /*private void OnBeginUpdateSceneMessage(int id, ReceiveData data)
        {
            BeginUpdateSceneMessage message = new BeginUpdateSceneMessage();
            message.Deserialize(data.buffer);
            int index = GetPlayerSyncWithSceneIndex(id, message.scene_id);
            if (index != -1)
            {
                PlayerInfo.SyncSceneInfo syncSceneInfo = players[id].scenes[index];
                if (syncSceneInfo.updatable)
                {
                    int messages_count = syncSceneInfo.update_messages.Count;
                    if (messages_count > 0)
                    {
                        players[id].scenes[index].message_counter = messages_count;
                        if (asyncMode.enable)
                        {
                            int jobs_num = Math.Min(messages_count, asyncMode.jobs);
                            JobHandle[] handles = new JobHandle[jobs_num];
                            for (int i = 0; i < jobs_num; i++)
                            {
                                ReceiveJob job = new ReceiveJob() { player_id = id, scene_index = index };
                                handles[i] = job.Schedule();
                            }
                            for (int i = 0; i < jobs_num; i++)
                                handles[i].Complete();
                        }
                        else
                            players[id].DeserializeUpdateMessages(index);
                        PostUpdate();
                    }
                }
                else
                    syncSceneInfo.update_messages.Clear();

                if (players[id].time == 0.0f)
                {
                    players[id].time = message.time;
                    players[id].last_iteration = message.iteration;
                }
                else if (message.iteration != players[id].last_iteration)
                {
                    players[id].time += (message.time - players[id].time) * TIME_SMOOTHING_FACTOR;
                    players[id].last_iteration = message.iteration;
                }
                syncSceneInfo.iteration = message.iteration;
                syncSceneInfo.updatable = false;
            }
        }

        private void OnEndUpdateSceneMessage(int id, ReceiveData data)
        {
            EndUpdateSceneMessage message = new EndUpdateSceneMessage();
            message.Deserialize(data.buffer);
            int index = GetPlayerSyncWithSceneIndex(id, message.scene_id);
            if (index != -1)
            {
                foreach (var info in players[id].objects)
                    if (info != null && info.inst.sceneId == message.scene_id && info.send_status != PlayerInfo.SyncObjectInfo.SYNC_STATUS.DISABLE)
                        info.inst.BeforeSendMessage();

                players[id].object_counter = 0;
                NativeList<Transport.WriteBuffer> reliable_data = new NativeList<Transport.WriteBuffer>(Unity.Collections.Allocator.TempJob);
                NativeList<Transport.WriteBuffer> non_reliable_data = new NativeList<Transport.WriteBuffer>(Unity.Collections.Allocator.TempJob);
                if (asyncMode.enable)
                {
                    for (int i = 0; i < asyncMode.jobs; i++)
                    {
                        reliable_data.Add(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                        non_reliable_data.Add(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                    }
                    JobHandle[] handles = new JobHandle[asyncMode.jobs];
                    for (int i = 0; i < asyncMode.jobs; i++)
                    {
                        SendJob job = new SendJob();
                        job.player_id = id;
                        job.scene_id = message.scene_id;
                        job.index = i;
                        job.reliable_data = reliable_data;
                        job.non_reliable_data = non_reliable_data;
                        handles[i] = job.Schedule();
                    }
                    for (int i = 0; i < asyncMode.jobs; i++)
                        handles[i].Complete();
                }
                else
                {
                    reliable_data.Add(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                    non_reliable_data.Add(new Transport.WriteBuffer(16, Unity.Collections.Allocator.Temp));
                    players[id].SerializeUpdateMessages(message.scene_id, reliable_data[0], non_reliable_data[0]);
                }
                SyncUpdateData syncUpdateData = new SyncUpdateData(message.scene_id, players[id].scenes[index].last_sync_iteration, players[id].time, MsgType.UpdateObjectsFromSlaveMessage);
                foreach (Transport.WriteBuffer buffer in reliable_data)
                {
                    ManagedWrapper<Transport.WriteBuffer> managed_buffer = new ManagedWrapper<Transport.WriteBuffer>(buffer);
                    if (managed_buffer.Value.Length > 0)
                        syncUpdateData.AddUpdatedObjectsReliableData(managed_buffer);
                    managed_buffer.Dispose();
                }
                foreach (Transport.WriteBuffer buffer in non_reliable_data)
                {
                    ManagedWrapper<Transport.WriteBuffer> managed_buffer = new ManagedWrapper<Transport.WriteBuffer>(buffer);
                    if (managed_buffer.Value.Length > 0)
                        syncUpdateData.AddUpdatedObjectsNonReliableData(managed_buffer);
                    managed_buffer.Dispose();
                }

                syncUpdateData.Send(id);
                reliable_data.Dispose();
                non_reliable_data.Dispose();

                players[id].scenes[index].updatable = true;
            }
        }*/

        private void OnCreateObjectsMessage(int id, ReceiveData data)
        {
            int scene_id = data.buffer.ReadInt();
            int iteration = data.buffer.ReadInt();
            int index = GetPlayerSyncWithSceneIndex(id, scene_id);
            if (index != -1)
            {
                players[id].scenes[index].last_sync_iteration = iteration;
                Scene active_scene = SceneManager.GetActiveScene();
                bool is_change_scene = false;
                if (scene_id != 0) // exclude DontDestroyOnLoad scene
                {
                    Scene update_scene = SceneManager.GetSceneByPath(networkScenes[scene_id].Path);
                    if (active_scene.path != update_scene.path)
                    {
                        SceneManager.SetActiveScene(update_scene);
                        is_change_scene = true;
                    }
                }
                while (data.buffer.Context < data.buffer.Length)
                {
                    int object_id = data.buffer.ReadInt();
                    int reg_id = data.buffer.ReadInt();
                    int create_sync_iteration = data.buffer.ReadInt();
                    if (reg_id != -1) // dynamic object
                    {
                        NetworkObject inst = GameObject.Instantiate(registeredPrefabs[reg_id]);
                        inst.gameObject.SetActive(false);
                        inst.id = object_id;
                        inst.host = id;
                        inst.createdSyncIteration = create_sync_iteration;
                        players[id].objects.Add(new PlayerInfo.SyncObjectInfo(inst, PlayerInfo.SyncObjectInfo.SYNC_STATUS.INIT), object_id);
                        if (scene_id == 0)
                            DontDestroyOnLoad(inst);
                    }
                    else // scene placed object
                        players[id].objects[object_id].receive_status = PlayerInfo.SyncObjectInfo.SYNC_STATUS.INIT;
                }
                if (is_change_scene)
                    SceneManager.SetActiveScene(active_scene);
            }
        }

        private void OnDeleteObjectsMessage(int id, ReceiveData data)
        {
            int scene_id = data.buffer.ReadInt();
            int iteration = data.buffer.ReadInt();
            int index = GetPlayerSyncWithSceneIndex(id, scene_id);
            if (index != -1)
            {
                // handle update messages
                PlayerInfo.SyncSceneInfo syncSceneInfo = players[id].scenes[index];
                int messages_count = syncSceneInfo.update_messages.Count;
                if (messages_count > 0)
                {
                    players[id].scenes[index].message_counter = messages_count;
                    if (asyncMode.enable)
                    {
                        int jobs_num = Math.Min(messages_count, asyncMode.jobs);
                        JobHandle[] handles = new JobHandle[jobs_num];
                        for (int i = 0; i < jobs_num; i++)
                        {
                            ReceiveJob job = new ReceiveJob() { player_id = id, scene_index = index };
                            handles[i] = job.Schedule();
                        }
                        for (int i = 0; i < jobs_num; i++)
                            handles[i].Complete();
                    }
                    else
                        players[id].DeserializeUpdateMessages(index);
                    PostUpdate();
                }
                // ---------------------

                players[id].scenes[index].last_sync_iteration = iteration;
                while (data.buffer.Context < data.buffer.Length)
                {
                    int object_id = data.buffer.ReadInt();
                    if (players[id].objects[object_id] != null)
                    {
                        NetworkObject inst = players[id].objects[object_id].inst;
                        inst.OnNetworkDestroy();
                        if (!inst.isScenePlaced)
                        {
                            inst.id = -1;
                            inst.host = -1;
                            Destroy(inst.gameObject);
                            players[id].objects[object_id] = null;
                        }
                        else
                        {
                            players[id].objects[object_id].send_status = PlayerInfo.SyncObjectInfo.SYNC_STATUS.DISABLE;
                            players[id].objects[object_id].receive_status = PlayerInfo.SyncObjectInfo.SYNC_STATUS.DISABLE;
                        }
                    }
                }
            }
        }

        private void OnUpdateObjectsMessage(int id, ReceiveData data)
        {
            int scene_id = data.buffer.ReadInt();
            int index = GetPlayerSyncWithSceneIndex(id, scene_id);
            if (index != -1)
            {
                ReceiveData data_moved = new ReceiveData();
                Transport.InputPackage package = data.ExtractPackage();
                data_moved.InsertPackage(package);
                players[id].scenes[index].update_messages.Enqueue(data_moved);
            }
        }

        private void OnUpdateObjectsAnswerMessage(int id, ReceiveData data)
        {
            Transport.ReadBuffer buffer = data.buffer;
            int scene_id = buffer.ReadInt();
            float send_time = buffer.ReadFloat();
            int iteration = buffer.ReadInt();
            int iter_difference = update_counter - iteration;
            while (buffer.Context < buffer.Length)
            {
                int seg_offset = buffer.Context;
                int object_id = buffer.ReadInt();
                int object_sync_iteration = buffer.ReadInt();
                byte object_sync_type = buffer.ReadByte();
                if (objects[object_id] != null && objects[object_id].inst.createdSyncIteration == object_sync_iteration)
                {
                    objects[object_id].inst.NetworkDeserialize(id, buffer, send_time);
                    objects[object_id].inst.AfterReceiveMessage();
                }
                else
                {
                    unsafe
                    {
                        int seg_size = SyncData.GetUpdateUnitSize(buffer.Pointer, seg_offset);
                        buffer.Context = seg_offset + seg_size;
                    }
                }
            }
        }

        // --------------------- INTERFACE ------------------------
        public void AttachGameObjectToPlayer(int player_id, GameObject go)
        {
            CheckPlayer(player_id);
            if (players[player_id].gameObject == null)
                foreach (PlayerInfo.SyncSceneInfo info in players[player_id].scenes)
                    networkScenes[info.scene_id].OnAttachGameObjectToPlayer(info.sid);
            players[player_id].gameObject = go;
        }
        public void DetachGameObjectFromPlayer(int player_id)
        {
            CheckPlayer(player_id);
            if (players[player_id].gameObject != null)
                foreach (PlayerInfo.SyncSceneInfo info in players[player_id].scenes)
                    networkScenes[info.scene_id].OnDetachGameObjectToPlayer(info.sid);
            players[player_id].gameObject = null;
        }
        public GameObject GetGameObjectAttachedToPlayer(int player_id)
        {
            CheckPlayer(player_id);
            return players[player_id].gameObject;
        }

        public void MakeObjectVisibleToPlayer(int player_id, NetworkObject inst)
        {
            CheckPlayer(player_id);
            if (inst.IsOwner && (inst.visibleType == VISIBLE_TYPE.PRIVATE_GLOBAL || inst.visibleType == VISIBLE_TYPE.PRIVATE_LOCAL))
            {
                int scene_id = inst.sceneId;
                int sid = networkScenes[scene_id].FindSyncSession(player_id);
                if (sid != -1 && networkScenes[scene_id].GetSyncSessionState(sid) == SYNC_SESSION_STATE.READY_STATE)
                    networkScenes[scene_id].MakeObjectVisibleToPlayer(sid, inst);
            }
        }

        public void MakeObjectInvisibleToPlayer(int player_id, NetworkObject inst)
        {
            CheckPlayer(player_id);
            if (inst.IsOwner && (inst.visibleType == VISIBLE_TYPE.PRIVATE_GLOBAL || inst.visibleType == VISIBLE_TYPE.PRIVATE_LOCAL))
            {
                int scene_id = inst.sceneId;
                int sid = networkScenes[scene_id].FindSyncSession(player_id);
                if (sid != -1 && networkScenes[scene_id].GetSyncSessionState(sid) == SYNC_SESSION_STATE.READY_STATE)
                    networkScenes[scene_id].MakeObjectInvisibleToPlayer(sid, inst);
            }
        }

        public void SyncSceneWithPlayer(int player_id, int scene_id, LoadSceneMode mode)
        {
            scene_id = scene_id + 1; // DontDestroyOnLoad scene has id = 0
            CheckPlayer(player_id);
            CheckScene(scene_id);
            if (networkScenes[scene_id].IsLoad)
            {
                if (networkScenes[scene_id].FindSyncSession(player_id) == -1)
                    networkScenes[scene_id].SyncSceneWithPlayer(player_id, mode);
            }
        }

        public void DesyncSceneWithPlayer(int player_id, int scene_id, bool unload)
        {
            scene_id = scene_id + 1; // DontDestroyOnLoad scene has id = 0
            CheckPlayer(player_id);
            CheckScene(scene_id);
            if (networkScenes[scene_id].IsLoad)
            {
                int sid = networkScenes[scene_id].FindSyncSession(player_id);
                if (sid != -1)
                    networkScenes[scene_id].DesyncSceneWithPlayer(sid, unload);
            }
        }
        public bool IsPlayerSyncWithScene(int player_id, int scene_id)
        {
            if (player_id < 0 || player_id >= players.Count || players[player_id] == null ||
                 scene_id < 0 || scene_id >= networkScenes.Count || networkScenes[scene_id] == null ||
                !networkScenes[scene_id].IsLoad)
                return false;
            int sid = networkScenes[scene_id].FindSyncSession(player_id);
            if (sid == -1)
                return false;
            return networkScenes[scene_id].GetSyncSessionState(sid) == SYNC_SESSION_STATE.READY_STATE;

        }

        public void DesyncSceneWithAllPlayers(int scene_id, bool unload)
        {
            scene_id = scene_id + 1; // DontDestroyOnLoad scene has id = 0
            CheckScene(scene_id);
            if (networkScenes[scene_id].IsLoad)
                networkScenes[scene_id].DesyncSceneWithAllPlayers(unload);
        }

        public string GetScenePathById(int scene_id)
        {
            scene_id = scene_id + 1; // DontDestroyOnLoad scene has id = 0
            CheckScene(scene_id);
            return networkScenes[scene_id].Path;
        }

        public SYNC_SESSION_STATE GetSyncSessionState(int scene_id, int sid)
        {
            scene_id = scene_id + 1; // DontDestroyOnLoad scene has id = 0
            CheckScene(scene_id);
            return networkScenes[scene_id].GetSyncSessionState(sid);
        }

        public NetworkObject GetNetworkObjectById(int id, int host = -1)
        {
            if (host != -1)
            {
                CheckPlayer(host);
                return players[host].objects[id].inst;
            }
            return objects[id].inst;
        }

        public float GetHostTime(int id)
        {
            CheckPlayer(id);
            return players[id].time;
        }

        //Function can only be called from a NetworkBehaviour
        public void SendUpdateMessageTo(int player_id, int scene_id, int object_id, byte component_id, NetworkBehaviour.SendMessageCallback callback, ChannelOpts channel)
        {
            CheckPlayer(player_id);
            int sid = networkScenes[scene_id].FindSyncSession(player_id);
            if (sid != -1)
                networkScenes[scene_id].SendUpdateMessageTo(sid, object_id, component_id, callback, channel);
        }
    }
}
