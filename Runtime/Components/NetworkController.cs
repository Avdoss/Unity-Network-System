using Network;
using System;
using Transport;

public class NetworkController : NetworkBehaviour
{
    private enum MessageType : byte
    {
        ATTACH = 0,
        DETACH = 1,
        CONTROL = 2,
        CORRECTION = 3
    }

    public enum State
    {
        NONE = 0,
        ATTACHING = 1,
        READY = 3
    }

    //owner
    public int playerId { get; private set; }
    public State state { get; private set; }
    private State statePrev;
    public bool destroyAfterDetach;
    //slave
    public bool isAttached { get; private set; }
    private bool isAttaching;
    private bool isDetaching;

    public virtual void OnControllerAttachedToPlayer()
    {

    }
    public virtual void OnControllerDetachedFromPlayer()
    {

    }
    public virtual void OnBeforeSerialize()
    {

    }
    public virtual void OnAfterDeserialize()
    {

    }
    public virtual void OnNetworkSerializeReliableSequential(WriteBuffer buffer)
    {

    }
    public virtual void OnNetworkSerializeUnreliableUnsequential(WriteBuffer buffer)
    {

    }
    public virtual void OnNetworkDeserialize(ReadBuffer buffer, float send_time)
    {

    }

    protected void Update()
    {
        if (IsOwner)
        {
            if (state != statePrev)
            {
                if (state == State.READY)
                    OnControllerAttachedToPlayer();
                statePrev = state;
            }
        }
    }

    public override void OnNetworkInitialize()
    {
        base.OnNetworkInitialize();
        playerId = -1;
        state = State.NONE;
        statePrev = State.NONE;
        isAttached = false;
        isAttaching = false;
        isDetaching = false;
    }

    private void ChangeState(State value)
    {
        if (state != value)
        {
            switch (value)
            {
                case State.ATTACHING:
                    if (state == State.NONE)
                        NetworkSceneManager.Singleton.OnSceneDesyncWithPlayer += OnSceneDesyncWithPlayer;
                    break;
                case State.NONE:
                    if (NetworkSceneManager.Singleton)
                        NetworkSceneManager.Singleton.OnSceneDesyncWithPlayer -= OnSceneDesyncWithPlayer;
                    break;
            }
            state = value;
        }
    }

    public void AttachControllerToPlayer(int player_id)
    {
        if (!IsOwner || state != State.NONE)
            return;
        if (!NetworkSceneManager.Singleton.IsPlayerSyncWithScene(player_id, networkObject.sceneId))
            throw new AvailableException(string.Format("Player with id {0} dont synchronize with scene {1}", player_id, networkObject.sceneId));
        NetworkSceneManager.Singleton.AttachGameObjectToPlayer(player_id, networkObject.gameObject);
        this.playerId = player_id;
        ChangeState(State.ATTACHING);
    }
    public void DetachControllerFromPlayer()
    {
        if (state != State.NONE)
        {
            bool sendDetachMessage = state == State.READY;
            DetachControllerFromPlayer(true, destroyAfterDetach);
        }
    }
    private void DetachControllerFromPlayer(bool sendDetachMessage, bool destroyInst)
    {
        if (sendDetachMessage)
        {
            SendUpdateMessageTo(playerId, (WriteBuffer buffer) =>
            {
                buffer.Write((byte)MessageType.DETACH);
                NetworkCharacterControllerDetachMessage message = new NetworkCharacterControllerDetachMessage();
                message.Serialize(buffer);
            }, ChannelOpts.Reliable);
        }
        if (NetworkSceneManager.Singleton)
            NetworkSceneManager.Singleton.DetachGameObjectFromPlayer(playerId);
        if (state == State.READY)
            OnControllerDetachedFromPlayer();
        playerId = -1;
        ChangeState(State.NONE);
        if (destroyInst)
            Destroy(networkObject.gameObject);
    }

    public override void OnBeforeSendMessage()
    {
        base.OnBeforeSendMessage();
        if (IsOwner && state == State.READY || !IsOwner && isAttached)
            OnBeforeSerialize();
    }

    public override void OnReceiveUpdateMessage(int id, ReadBuffer buffer, int end, float send_time)
    {
        base.OnReceiveUpdateMessage(id, buffer, end, send_time);
        MessageType messageType = (MessageType)buffer.ReadByte();
        if (IsOwner)
        {
            if (id == playerId && state == State.READY)
            {
                if (messageType == MessageType.CONTROL)
                    OnNetworkDeserialize(buffer, send_time);
            }
        }
        else
        {
            switch (messageType)
            {
                case MessageType.ATTACH:
                    if (!isAttached)
                    {
                        isAttaching = true;
                        isDetaching = false;
                    }
                    break;
                case MessageType.DETACH:
                    if (isAttached)
                    {
                        isAttaching = false;
                        isDetaching = true;
                    }
                    break;
                case MessageType.CORRECTION:
                    if (isAttached)
                        OnNetworkDeserialize(buffer, send_time);
                    break;
            }
        }

    }

    public override void OnSendInitializeMessage(WriteBuffer buffer)
    {
        base.OnSendInitializeMessage(buffer);
    }

    public override void OnSendUpdateReliableMessage(WriteBuffer buffer)
    {
        base.OnSendUpdateReliableMessage(buffer);
        if (IsOwner)
        {
            switch (state)
            {
                case State.ATTACHING:
                    SendUpdateMessageTo(playerId, (WriteBuffer buffer) =>
                    {
                        buffer.Write((byte)MessageType.ATTACH);
                        NetworkCharacterControllerAttachMessage message = new NetworkCharacterControllerAttachMessage();
                        message.Serialize(buffer);
                    }, ChannelOpts.Reliable);
                    ChangeState(State.READY);
                    break;
                case State.READY:
                    NetworkSerialize(MessageType.CORRECTION, buffer, OnNetworkSerializeReliableSequential);
                    break;
            }
        }
        else
        {
            if (isAttached)
                NetworkSerialize(MessageType.CONTROL, buffer, OnNetworkSerializeReliableSequential);
        }
    }

    public override void OnSendUpdateNonReliableMessage(WriteBuffer buffer)
    {
        base.OnSendUpdateNonReliableMessage(buffer);
        if (IsOwner)
        {
            if (state == State.READY)
                NetworkSerialize(MessageType.CORRECTION, buffer, OnNetworkSerializeUnreliableUnsequential);
        }
        else
        {
            if (isAttached)
                NetworkSerialize(MessageType.CONTROL, buffer, OnNetworkSerializeUnreliableUnsequential);
        }
    }

    public delegate void NetworkSerializeMethod(WriteBuffer buffer);
    private void NetworkSerialize(MessageType messageType, WriteBuffer buffer, NetworkSerializeMethod serializeMethod)
    {
        int offset = buffer.Write((byte)messageType);
        int begin = buffer.Length;
        serializeMethod(buffer);
        if (buffer.Length == begin)
            buffer.Length = offset;
    }

    public override void OnAfterReceiveMessage()
    {
        if (!IsOwner)
        {
            if (isAttaching)
            {
                OnControllerAttachedToPlayer();
                isAttached = true;
                isAttaching = false;
            }
            if (isDetaching)
            {
                OnControllerDetachedFromPlayer();
                isAttached = false;
                isDetaching = false;
            }
        }
        if (IsOwner && state == State.READY || !IsOwner && isAttached)
            OnAfterDeserialize();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (isAttached)
            OnControllerDetachedFromPlayer();
        isAttached = false;
        isAttaching = false;
        isDetaching = false;
    }

    private void OnSceneDesyncWithPlayer(int player_id, int scene_id)
    {
        if (scene_id + 1 == networkObject.sceneId && player_id == this.playerId)
            DetachControllerFromPlayer(false, destroyAfterDetach);
    }

    protected void OnDestroy()
    {
        if (state != State.NONE)
            DetachControllerFromPlayer(false, false);
    }
}
