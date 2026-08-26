using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Transport;

namespace Network
{
    public class NetworkBehaviour : MonoBehaviour
    {
        /// <summary>
        /// Called when an NetworkObject is first created even if the object is non active.
        /// For scene placed objects this method is called when the scene is loading.
        /// Called on owner and slave side.
        /// </summary>
        public virtual void OnNetworkInitialize() { FindParentNetworkObject(); IsEnable = enabled; }
        /// <summary>
        /// Method called on main thread on slave side before fist render when NetworkObject is spawned.
        /// Used for first synchronize parameters with owner.
        /// Called after OnReceiveUpdateMessage.
        /// </summary>
        public virtual void OnNetworkSpawn() { }
        /// <summary>
        /// Method called on main thread on slave side when NetworkObject is despawned.
        /// </summary>
        public virtual void OnNetworkDespawn() { }
        /// <summary>
        /// Called on owner/slave side before send message
        /// Method called on main thread
        /// </summary>
        public virtual void OnBeforeSendMessage() { }
        /// <summary>
        /// Called on owner/slave side when update data received. 
        /// Called on a other thread (if multithreading is enable) on owner and slave side.
        /// </summary>
        /// <param name="id">Host id (owner or slave)</param>
        /// <param name="buffer">Update data read buffer</param>
        /// <param name="end">End context of message</param>
        /// <param name="send_time">Owner time when update data send</param>
        public virtual void OnReceiveUpdateMessage(int id, ReadBuffer buffer, int end, float send_time) { }
        /// <summary>
        /// Called on owner/slave side when NetworkObject becomes visible to the other side. 
        /// Called on a other thread if multithreading is enable.
        /// Used to send initialize data of this object to other side over a reliable channel.
        /// </summary>
        /// <param name="buffer">Update data write buffer</param>
        public virtual void OnSendInitializeMessage(WriteBuffer buffer) { }
        /// <summary>
        /// Called on owner/slave side when NetworkObject update before OnSendUpdateReliableMessage. 
        /// Called on a other thread if multithreading is enable.
        /// Used to send up-to-date data of this object to other side over a reliable channel.
        /// </summary>
        /// <param name="buffer">Update data write buffer</param>
        public virtual void OnSendUpdateReliableMessage(WriteBuffer buffer) { }
        /// <summary>
        /// Called on owner/slave side when NetworkObject update after OnSendUpdateReliableMessage. 
        /// Called on a other thread if multithreading is enable.
        /// Used to send up-to-date data of this object to slave side over a non-reliable channel.
        /// </summary>
        /// <param name="buffer">Update data write buffer</param>
        public virtual void OnSendUpdateNonReliableMessage(WriteBuffer buffer) { }
        /// <summary>
        /// Called on owner/slave side after receive message
        /// Method called on main thread
        /// </summary>
        public virtual void OnAfterReceiveMessage() { }

        public delegate void SendMessageCallback(WriteBuffer buffer);
        public void SendUpdateMessageTo(int id, SendMessageCallback callback, ChannelOpts channel)
        {
            if (IsOwner)
                NetworkSceneManager.Singleton.SendUpdateMessageTo(id, networkObject.sceneId, networkObject.id, componentId, callback, channel);
            else
                throw new ComponentException(string.Format("Only the owner of the object can send private messages: ({0})", gameObject.name));
        }
        /// <summary>
        /// Network object reference
        /// </summary>
        public NetworkObject networkObject { get; private set; }
        public bool IsOwner { get { return networkObject.IsOwner; } }
        public bool IsEnable { get; private set; }
        public bool IsAsyncMode { get { return !IsOwner && NetworkSceneManager.Singleton.asyncMode.enable; } }
        public byte componentId;

        private void FindParentNetworkObject()
        {
            networkObject = gameObject.GetComponentInParent<NetworkObject>(true);
            if (networkObject == null)
            {
                Destroy(this);
                throw new CreateObjectException(string.Format("Requires a parent NetworkObject for the NetworkBehaviour component ({0})", gameObject.name));
            }
        }

        protected virtual void OnEnable()
        {
            IsEnable = enabled;
        }

        protected virtual void OnDisable()
        {
            IsEnable = enabled;
        }
    }
}
