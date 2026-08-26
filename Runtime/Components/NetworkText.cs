using Network;
using Transport;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Text))]
public class NetworkText : NetworkBehaviour
{
    private Text text;
    private UpdateNetworkTextMessage message;
    private UpdateNetworkTextMessage initMessage;
    [SerializeField]
    private float synchronizePeriod = 0.05f;
    private float lastUpdateTime;
    public override void OnNetworkInitialize()
    {
        base.OnNetworkInitialize();
        text = GetComponent<Text>();
        message = new UpdateNetworkTextMessage();
        if(IsOwner)
        {
            initMessage = new UpdateNetworkTextMessage();
            if (!networkObject.isScenePlaced)
                initMessage.IsUpdate = true;
            lastUpdateTime = 0.0f;
        }
    }

    public override void OnBeforeSendMessage()
    {
        base.OnBeforeSendMessage();
        if(IsOwner)
        {
            if (text.text != message.text)
            {
                message.text = text.text;
                message.IsTextUp = true;
                initMessage.IsTextUp = true;
            }
            if (text.color != message.color)
            {
                message.color = text.color;
                message.IsColorUp = true;
                initMessage.IsColorUp = true;
            }
        }
    }

    public override void OnReceiveUpdateMessage(int id, ReadBuffer buffer, int end, float send_time)
    {
        base.OnReceiveUpdateMessage(id, buffer, end, send_time);
        if (!IsOwner)
            message.Deserialize(buffer);
    }
    public override void OnSendInitializeMessage(WriteBuffer buffer)
    {
        base.OnSendInitializeMessage(buffer);
        if (IsOwner)
        {
            if (initMessage.IsUpdate)
            {
                if (initMessage.IsTextUp)
                    initMessage.text = message.text;
                if (initMessage.IsColorUp)
                    initMessage.color = message.color;
                initMessage.Serialize(buffer);
            }
        }
    }
    public override void OnSendUpdateReliableMessage(WriteBuffer buffer)
    {
        base.OnSendUpdateReliableMessage(buffer);
        if (IsOwner)
        {
            if (NetworkSceneManager.Singleton.time - lastUpdateTime > synchronizePeriod)
            {
                if (message.IsUpdate)
                {
                    message.Serialize(buffer);
                    message.IsUpdate = false;
                }
                lastUpdateTime = NetworkSceneManager.Singleton.time;
            }
        }
    }
    public override void OnAfterReceiveMessage()
    {
        base.OnAfterReceiveMessage();
        if (!IsOwner)
        {
            if(message.IsTextUp)
                text.text = message.text;
            if(message.IsColorUp)
                text.color = message.color;
            message.IsUpdate = false;
        }
    }
}
