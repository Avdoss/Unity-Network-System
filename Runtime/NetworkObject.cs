using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Transport;
using Multithreading;

namespace Network
{
    public class CreateObjectException : Exception
    {
        public CreateObjectException() { }
        public CreateObjectException(string message) : base(message) { }
        public CreateObjectException(string message, Exception inner) : base(message, inner) { }
    }

    public class RaceDataException : Exception
    {
        public RaceDataException() { }
        public RaceDataException(string message) : base(message) { }
        public RaceDataException(string message, Exception inner) : base(message, inner) { }
    }

    public enum VISIBLE_TYPE : byte
    {
        PUBLIC_LOCAL = 0,
        PUBLIC_GLOBAL = 1,
        PRIVATE_LOCAL = 2,
        PRIVATE_GLOBAL = 3
    }

    public enum SYNC_TYPE : byte
    {
        INIT = 0,
        RELIABLE = 1,
        NON_RELIABLE = 2,
        RELIABLE_UNSEQUENTIAL = 3
    }

    [Serializable]
    [DisallowMultipleComponent]
    public class NetworkObject : NetworkBehaviour
    {
        public delegate void NetworkObjectHandler(NetworkObject instance);
        public static event NetworkObjectHandler NetworkObjectCreateEvent;
        public static event NetworkObjectHandler NetworkObjectDeleteEvent;
        public static event NetworkObjectHandler NetworkObjectsChangeSceneEvent;


        //[HideInInspector]
        [SerializeField]
        public int scenePos = -1;  //serialize
        public bool isScenePlaced; //serialize

        //[HideInInspector]
        public int id = -1;
        //[HideInInspector]
        public int host = -1;
        //[HideInInspector]
        public int regId = -1;
        //[HideInInspector]
        public int sceneId = -1;
        //[HideInInspector]
        public int createdSyncIteration = -1; // for scene placed objects value is -1

        private UpdateNetworkObjectMessage message;
        private bool is_init = false;
        private bool is_spawned = false;
        private byte scenePlacedInitStatus;
        private byte nonScenePlacedInitStatus;
        private float lastUpdateTime;


        [SerializeField]
        public bool owner;
        [SerializeField]
        private bool dontDestroy;
        [SerializeField]
        public VISIBLE_TYPE visibleType;

        public NetworkObject rootNetworkObject { get; private set; }
        private List<NetworkBehaviour> networkComponents;
        private SpinLock spinLock;

        public bool IsScenePlaced { get { return isScenePlaced; } }
        public new bool IsOwner { get { return owner; } }
        public VISIBLE_TYPE VisibleType { get { return visibleType; } }


#if UNITY_EDITOR
        private void OnValidate()
        {
            if (NetworkSceneEditor.Instance == null && !PrefabUtility.IsPartOfPrefabAsset(gameObject))
                NetworkSceneEditor.Instantiate();
        }
#endif
        private void CheckRegister()
        {
            if (regId == -1)
            {
                Destroy(gameObject);
                throw new CreateObjectException(string.Format("Only registered prefabs can be added to a scene in play mode ({0})", name));
            }
        }
        private NetworkObject CheckAndGetParentNetworkObject()
        {
            if (transform.parent != null)
            {
                NetworkObject inst = transform.parent.GetComponent<NetworkObject>();
                if (inst == null)
                {
                    Destroy(gameObject);
                    throw new CreateObjectException(string.Format("In play mode, NetworkObjects can only be linked to other NetworkObjects ({0})", name));
                }
                if (IsOwner && inst.host != -1)
                {
                    Destroy(gameObject);
                    throw new CreateObjectException(string.Format("In play mode, NetworkObjects can only be linked to owner NetworkObjects ({0})", name));
                }
                return inst;
            }
            return null;
        }

        private void FindRootNetworkObject()
        {
            rootNetworkObject = this;
            Transform tran = transform.parent;
            while (tran != null)
            {
                NetworkObject parent = tran.GetComponentInParent<NetworkObject>();
                if (parent != null)
                {
                    rootNetworkObject = parent;
                    tran = parent.transform.parent;
                }
                else
                    break;
            }
        }

        private void FindChildNetworkComponents()
        {
            networkComponents = new List<NetworkBehaviour>();
            Queue<Transform> queue = new Queue<Transform>();
            queue.Enqueue(transform);
            Transform inst;
            List<NetworkBehaviour> networkComponentsTmp = new List<NetworkBehaviour>();
            while (queue.TryDequeue(out inst))
            {
                inst.GetComponents<NetworkBehaviour>(networkComponentsTmp);
                if (networkComponentsTmp.Count > 0)
                    networkComponents.AddRange(networkComponentsTmp);
                for (int i = 0; i < inst.childCount; i++)
                {
                    Transform child = inst.GetChild(i);
                    if (child.GetComponent<NetworkObject>() == null)
                        queue.Enqueue(child);
                }
            }
        }

        public void Initialize()
        {
            if (is_init)
                return;
            FindChildNetworkComponents();
            for (int i = 0; i < networkComponents.Count; i++)
            {
                NetworkBehaviour component = networkComponents[i];
                if (i > 255)
                    throw new ComponentException(string.Format("One NetworkObject can have a maximum of 256 NetworkBehaviour components in the hierarchy"));
                component.componentId = (byte)i;
                component.OnNetworkInitialize();
            }
            is_init = true;
        }

        public override void OnNetworkInitialize()
        {
            base.OnNetworkInitialize();
            scenePlacedInitStatus = 0;
            lastUpdateTime = 0.0f;
            spinLock = new SpinLock();
            message = new UpdateNetworkObjectMessage();
            if (!IsScenePlaced)
            {
                CheckRegister();
                NetworkObject p_inst = CheckAndGetParentNetworkObject();
                message.parent = p_inst != null ? p_inst.id : -1;
                message.enable = gameObject.activeSelf;
                nonScenePlacedInitStatus = (byte)UpdateNetworkObjectMessage.STATE_MASK.ENABLE_UPDATE | (byte)UpdateNetworkObjectMessage.STATE_MASK.PARENT_UPDATE;
                if (GetComponent<NetworkTransform>() == null)
                {
                    if (transform.localPosition != Vector3.zero)
                    {
                        message.position = transform.localPosition;
                        nonScenePlacedInitStatus |= (byte)UpdateNetworkObjectMessage.STATE_MASK.POSITION_UPDATE;
                    }
                    if (transform.localEulerAngles != Vector3.zero)
                    {
                        message.rotation = transform.localEulerAngles;
                        nonScenePlacedInitStatus |= (byte)UpdateNetworkObjectMessage.STATE_MASK.ROTATION_UPDATE;
                    }
                    if (transform.localScale != Vector3.one)
                    {
                        message.scale = transform.localScale;
                        nonScenePlacedInitStatus |= (byte)UpdateNetworkObjectMessage.STATE_MASK.SCALE_UPDATE;
                    }
                }
                NetworkObjectCreateEvent?.Invoke(this);
            }
            FindRootNetworkObject();
        }

        void Awake()
        {
            Initialize();
            if (dontDestroy)
                DontDestroyOnLoad(gameObject);
        }
        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            if (IsOwner)
            {
                if (NetworkSceneManager.Singleton.GetScenePathById(sceneId - 1) != gameObject.scene.path) // change scene
                    NetworkObjectsChangeSceneEvent?.Invoke(this);
            }
        }



        protected override void OnEnable()
        {
            base.OnEnable();
            OnActiveChange();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            OnActiveChange();
        }

        private void OnActiveChange()
        {
            if (IsOwner)
            {
                if (message.enable != gameObject.activeSelf)
                {
                    message.enable = gameObject.activeSelf;
                    message.IsEnableUpdate = true;
                    if (isScenePlaced)
                        scenePlacedInitStatus |= (byte)UpdateNetworkObjectMessage.STATE_MASK.ENABLE_UPDATE;
                }
            }
        }

        void OnTransformParentChanged()
        {
            NetworkObject parent_inst = CheckAndGetParentNetworkObject();
            FindRootNetworkObject();
            foreach (NetworkObject inst in GetComponentsInChildren<NetworkObject>(true))
                inst.rootNetworkObject = rootNetworkObject;
            int parent_id = parent_inst != null ? parent_inst.id : -1;
            message.parent = parent_id;
            message.IsParentUpdate = true;
            if (isScenePlaced)
                scenePlacedInitStatus |= (byte)UpdateNetworkObjectMessage.STATE_MASK.PARENT_UPDATE;
        }

        void OnDestroy()
        {
            NetworkObjectDeleteEvent?.Invoke(this);
        }

        public void NetworkCreate()
        {
            foreach (NetworkBehaviour component in networkComponents)
                component.OnNetworkSpawn();
            if (message.enable)
                gameObject.SetActive(true);
        }

        public void OnNetworkDestroy()
        {
            foreach (NetworkBehaviour component in networkComponents)
                component.OnNetworkDespawn();
            gameObject.SetActive(false);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            is_spawned = true;
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            is_spawned = false;
        }

        public void BeforeSendMessage()
        {
            foreach (NetworkBehaviour component in networkComponents)
                if (component.IsEnable)
                    component.OnBeforeSendMessage();

        }

        public override void OnSendInitializeMessage(WriteBuffer buffer)
        {
            base.OnSendInitializeMessage(buffer);
            if (IsOwner)
            {
                byte status_copy = message.status;
                if (isScenePlaced)
                    message.status |= scenePlacedInitStatus;
                else
                    message.status |= nonScenePlacedInitStatus;
                message.Serialize(buffer);
                message.status = status_copy;

                if (NetworkSceneManager.Singleton.lastUpdateTime != lastUpdateTime)
                    message.status = 0;
            }
        }

        public override void OnReceiveUpdateMessage(int id, ReadBuffer buffer, int end, float send_time)
        {
            base.OnReceiveUpdateMessage(id, buffer, end, send_time);
            if (!IsOwner)
                message.Deserialize(buffer);
        }

        public override void OnSendUpdateReliableMessage(WriteBuffer buffer)
        {
            base.OnSendUpdateReliableMessage(buffer);
            if (IsOwner)
            {
                if (message.IsUpdate)
                {
                    message.Serialize(buffer);
                    message.IsUpdate = false;
                }
                lastUpdateTime = NetworkSceneManager.Singleton.time;
            }
        }

        public void AfterReceiveMessage()
        {
            foreach (var component in networkComponents)
                if (component.IsEnable)
                    component.OnAfterReceiveMessage();
        }
        public override void OnAfterReceiveMessage()
        {
            base.OnAfterReceiveMessage();
            if (!IsOwner)
            {
                if (message.IsUpdate)
                {
                    if (message.IsEnableUpdate && is_spawned)
                        gameObject.SetActive(message.enable);
                    if (message.IsParentUpdate)
                    {
                        Transform parent = message.parent != -1 ? NetworkSceneManager.Singleton.GetNetworkObjectById(message.parent, host).transform : null;
                        transform.parent = parent;
                    }
                    if (message.IsPositionUpdate)
                        transform.localPosition = message.position;
                    if (message.IsRotationUpdate)
                        transform.localEulerAngles = message.rotation;
                    if (message.IsScaleUpdate)
                        transform.localScale = message.scale;
                    message.IsUpdate = false;
                }
            }
        }

        public void NetworkSerialize(WriteBuffer buffer, SYNC_TYPE sync_type)
        {
            //                                           --- PACKET STRUCT ---
            // | OBJECT_ID | SYNC_ETERATOR | SYNC_TYPE | COMPONENT_COUNT [ COMPONENT_ID | COMPONENT_SIZE | DATA... ]...
            // |  4(int)   |    4(int)     |  1(byte)  |     1(byte)     [    1(byte)   |      4(int)    |   ...   ]...


            spinLock.Lock();

            //int BEGIN_OFFSET = buffer.Write(id);
            //buffer.Write(createdSyncIteration);
            //buffer.Write((byte)sync_type);
            //int COMPONENT_COUNT_OFFSET = buffer.Write((byte)0);

            int OBJECT_BEGIN_OFFSET = buffer.Length;

            int counter = 0;
            bool curr_complete;
            bool prev_complete = false;

            buffer.Expand(15);
            buffer.Length += 15;
            int component_data_offset = buffer.Length;

            for (int i = 0; i < networkComponents.Count; i++)
            {
                if (prev_complete)
                {
                    buffer.Expand(5);
                    buffer.Length += 5;
                    component_data_offset = buffer.Length;
                }
                if (networkComponents[i].IsEnable)
                {
                    switch (sync_type)
                    {
                        case SYNC_TYPE.INIT:
                            networkComponents[i].OnSendInitializeMessage(buffer);
                            break;
                        case SYNC_TYPE.RELIABLE:
                            networkComponents[i].OnSendUpdateReliableMessage(buffer);
                            break;
                        case SYNC_TYPE.NON_RELIABLE:
                            networkComponents[i].OnSendUpdateNonReliableMessage(buffer);
                            break;
                    }
                }
                curr_complete = component_data_offset != buffer.Length;
                if (curr_complete)
                {
                    buffer.Write((byte)i, component_data_offset - 5); // COMPONENT_ID
                    buffer.Write(buffer.Length - component_data_offset, component_data_offset - 4); // COMPONENT_SIZE
                    counter += 1;
                }
                prev_complete = curr_complete;
            }

            if (counter > 0)
            {
                if (prev_complete == false)
                    buffer.Length = component_data_offset - 5;
                buffer.Write(id, OBJECT_BEGIN_OFFSET); // OBJECT_ID
                buffer.Write(createdSyncIteration, OBJECT_BEGIN_OFFSET + 4); // SYNC_ITER
                buffer.Write((byte)sync_type, OBJECT_BEGIN_OFFSET + 8); // SYNC_TYPE
                buffer.Write((byte)counter, OBJECT_BEGIN_OFFSET + 9); // COMPONENT COUT
            }
            else
                buffer.Length = OBJECT_BEGIN_OFFSET;

            spinLock.Unlock();
        }

        public void NetworkDeserialize(int id, ReadBuffer buffer, float send_time)
        {
            spinLock.Lock();
            byte count = buffer.ReadByte();
            for (int i = 0; i < count; i++)
            {
                byte component_id = buffer.ReadByte();
                int data_size = buffer.ReadInt();
                int data_begin = buffer.Context;
                int end = data_begin + data_size;
                if (networkComponents[component_id].IsEnable)
                    networkComponents[component_id].OnReceiveUpdateMessage(id, buffer, end, send_time);
                buffer.Context = end;
            }
            spinLock.Unlock();
        }
    }
}
