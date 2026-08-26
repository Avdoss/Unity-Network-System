using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Transport;
using System;

namespace Network
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkTransform : NetworkBehaviour
    {
        public enum INTERPOLATION_METHOD : int
        {
            NONE = 0,
            LINEAR = 1,
            ACCELERATED = 2,
        }

        [Serializable]
        public class InterpolateData
        {
            public INTERPOLATION_METHOD method = INTERPOLATION_METHOD.LINEAR;
            public float posCorrectionSpeed = 5.0f;
            public float rotCorrectionSpeed = 5.0f;
            public float scaleCorrectionSpeed = 5.0f;
        }


        //owner
        private byte initStatus;
        private bool isCommit;
        private bool isMoving;
        private bool isRotating;
        private bool isScaling;
        float lastUpdateTime;
        //slave
        private TimelineInterpolator<Vector3> positionInterpolator;
        private TimelineInterpolator<Quaternion> rotationInterpolator;
        private TimelineInterpolator<Vector3> scaleInterpolator;
        //common
        private UpdateNetworkTransformMessage message;

        [SerializeField]
        private float synchronizePeriod = 0.05f;
        [SerializeField]
        private float epsilon = 0.001f;
        [SerializeField]
        InterpolateData interpolateData;

        private bool IsDifferentVectors(Vector3 vec1, Vector3 vec2)
        {
            return Mathf.Abs(vec1.x - vec2.x) > epsilon ||
                   Mathf.Abs(vec1.y - vec2.y) > epsilon ||
                   Mathf.Abs(vec1.z - vec2.z) > epsilon;
        }

        public override void OnNetworkInitialize()
        {
            base.OnNetworkInitialize();
            message = new UpdateNetworkTransformMessage();
            initStatus = (byte)UpdateNetworkTransformMessage.STATE_MASK.IS_INITIALIZE;
            isCommit = false;
            isMoving = false;
            isRotating = false;
            isScaling = false;
            if (!IsOwner)
            {
                switch (interpolateData.method)
                {
                    case INTERPOLATION_METHOD.NONE:
                        positionInterpolator = new NoneInterpolator<Vector3>();
                        rotationInterpolator = new NoneInterpolator<Quaternion>();
                        scaleInterpolator = new NoneInterpolator<Vector3>();
                        break;
                    case INTERPOLATION_METHOD.LINEAR:
                        positionInterpolator = new Vector3LinearInterpolator(synchronizePeriod) { correctionSpeed = interpolateData.posCorrectionSpeed };
                        rotationInterpolator = new QuaternionLinearInterpolator(synchronizePeriod) { correctionSpeed = interpolateData.rotCorrectionSpeed };
                        scaleInterpolator = new Vector3LinearInterpolator(synchronizePeriod) { correctionSpeed = interpolateData.scaleCorrectionSpeed };
                        break;
                    case INTERPOLATION_METHOD.ACCELERATED:
                        positionInterpolator = new Vector3LinearInterpolator(synchronizePeriod) { Mode = Vector3LinearInterpolator.InterpolateType.ACCELERATED, correctionSpeed = interpolateData.posCorrectionSpeed };
                        rotationInterpolator = new QuaternionLinearInterpolator(synchronizePeriod) { correctionSpeed = interpolateData.rotCorrectionSpeed };
                        scaleInterpolator = new Vector3LinearInterpolator(synchronizePeriod) { Mode = Vector3LinearInterpolator.InterpolateType.ACCELERATED, correctionSpeed = interpolateData.scaleCorrectionSpeed };
                        break;
                }
            }
            else
            {
                if (!networkObject.isScenePlaced)
                {
                    if (IsDifferentVectors(transform.localPosition, Vector3.zero))
                        initStatus |= (byte)UpdateNetworkTransformMessage.STATE_MASK.UPDATE_POSITION;
                    if (IsDifferentVectors(transform.localEulerAngles, Vector3.zero))
                        initStatus |= (byte)UpdateNetworkTransformMessage.STATE_MASK.UPDATE_ROTATION;
                    if (IsDifferentVectors(transform.localScale, Vector3.one))
                        initStatus |= (byte)UpdateNetworkTransformMessage.STATE_MASK.UPDATE_SCALE;
                }
                message.position = transform.localPosition;
                message.rotation = transform.localEulerAngles;
                message.scale = transform.localScale;
                lastUpdateTime = 0.0f;
            }
        }

        private void OnTransformChanged()
        {
            if (IsDifferentVectors(transform.localPosition, message.position))
            {
                message.position = transform.localPosition;
                message.IsUpdatePosition = true;
                initStatus |= (byte)UpdateNetworkTransformMessage.STATE_MASK.UPDATE_POSITION;
            }
            if (IsDifferentVectors(transform.localEulerAngles, message.rotation))
            {
                message.rotation = transform.localEulerAngles;
                message.IsUpdateRotation = true;
                initStatus |= (byte)UpdateNetworkTransformMessage.STATE_MASK.UPDATE_ROTATION;
            }
            if (IsDifferentVectors(transform.localScale, message.scale))
            {
                message.scale = transform.localScale;
                message.IsUpdateScale = true;
                initStatus |= (byte)UpdateNetworkTransformMessage.STATE_MASK.UPDATE_SCALE;
            }
            if (isCommit)
            {
                message.IsCommit = true;
                isCommit = false;
            }
        }

        void Update()
        {
            if (!IsOwner)
            {
                float time = NetworkSceneManager.Singleton.GetHostTime(networkObject.host) - NetworkSceneManager.Singleton.streamClientDelay;
                // update position
                Vector3 pdata;
                if (positionInterpolator.Interpolate(time, transform.localPosition, out pdata))
                    transform.localPosition = pdata;
                // update rotation
                Quaternion rdata;
                if (rotationInterpolator.Interpolate(time, transform.localRotation, out rdata))
                    transform.localRotation = rdata;
                // update scale
                Vector3 sdata;
                if (scaleInterpolator.Interpolate(time, transform.localScale, out sdata))
                    transform.localScale = sdata;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
        }
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            positionInterpolator.Clear();
            rotationInterpolator.Clear();
            scaleInterpolator.Clear();
        }

        public override void OnBeforeSendMessage()
        {
            base.OnBeforeSendMessage();
            if (IsOwner)
            {
                if (transform.hasChanged)
                {
                    OnTransformChanged();
                    transform.hasChanged = false;
                }
            }
        }
        public override void OnReceiveUpdateMessage(int id, ReadBuffer buffer, int end, float send_time)
        {
            base.OnReceiveUpdateMessage(id, buffer, end, send_time);
            if (!IsOwner)
            {
                message.Deserialize(buffer);
                if (!message.IsInitialize)
                {
                    if (message.IsUpdatePosition)
                        positionInterpolator.AddNode(new ValueInfo<Vector3>(message.position, message.IsCommitPosition), send_time);
                    if (message.IsUpdateRotation)
                        rotationInterpolator.AddNode(new ValueInfo<Quaternion>(Quaternion.Euler(message.rotation), message.IsCommitRotation), send_time);
                    if (message.IsUpdateScale)
                        scaleInterpolator.AddNode(new ValueInfo<Vector3>(message.scale, message.IsCommitScale), send_time);
                }
            }
        }

        public override void OnSendInitializeMessage(WriteBuffer buffer)
        {
            base.OnSendInitializeMessage(buffer);
            if (IsOwner)
            {
                byte status_copy = message.status;
                message.status = initStatus;
                if (message.IsUpdate)
                    message.Serialize(buffer);
                message.status = status_copy;

                if (NetworkSceneManager.Singleton.lastUpdateTime - lastUpdateTime > synchronizePeriod)
                {
                    message.status = 0;
                    isMoving = false;
                    isRotating = false;
                    isScaling = false;
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
                    if (isMoving && !message.IsUpdatePosition)
                    {
                        message.IsUpdatePosition = true;
                        message.IsCommitPosition = true;
                        isMoving = false;
                    }
                    if (isRotating && !message.IsUpdateRotation)
                    {
                        message.IsUpdateRotation = true;
                        message.IsCommitRotation = true;
                        isRotating = false;
                    }
                    if (isScaling && !message.IsUpdateScale)
                    {
                        message.IsUpdateScale = true;
                        message.IsCommitScale = true;
                        isScaling = false;
                    }
                    if (message.IsCommit)
                    {
                        message.Serialize(buffer);
                        message.status = 0;
                    }
                }
            }
        }

        public override void OnSendUpdateNonReliableMessage(WriteBuffer buffer)
        {
            base.OnSendUpdateNonReliableMessage(buffer);
            if (IsOwner)
            {
                if (NetworkSceneManager.Singleton.time - lastUpdateTime > synchronizePeriod)
                {
                    if (message.IsUpdate && !message.IsCommit)
                    {
                        if (!isMoving && message.IsUpdatePosition)
                            isMoving = true;
                        if (!isRotating && message.IsUpdateRotation)
                            isRotating = true;
                        if (!isScaling && message.IsUpdateScale)
                            isScaling = true;
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
            if (message.IsInitialize)
            {
                if (message.IsUpdatePosition)
                    transform.localPosition = message.position;
                if (message.IsUpdateRotation)
                    transform.localEulerAngles = message.rotation;
                if (message.IsUpdateScale)
                    transform.localScale = message.scale;
            }
        }

        public void Commit()
        {
            isCommit = true;
        }
    }
}
