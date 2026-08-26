using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine;
using Transport;
using System.Text;
using System;
using static Network.NetworkAnimator;

namespace Network
{
    public class SyncSceneMessage : INetworkMessage
    {


        public int scene_id;
        public SYNC_ERR_CODE err_code;
        public LoadSceneMode mode;
        public List<IdMap> IdMaps;
        public void Deserialize(ReadBuffer buffer)
        {
            scene_id = buffer.ReadInt();
            err_code = (SYNC_ERR_CODE)buffer.ReadByte();
            if (err_code == SYNC_ERR_CODE.NONE)
            {
                mode = (LoadSceneMode)buffer.ReadInt();
                int count = buffer.ReadInt();
                IdMaps = new List<IdMap>(count);
                for (int i = 0; i < count; i++)
                {
                    IdMap map = new IdMap();
                    map.src_begin = buffer.ReadInt();
                    map.dst_begin = buffer.ReadInt();
                    map.count = buffer.ReadInt();
                    IdMaps.Add(map);
                }
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(scene_id);
            buffer.Write((byte)err_code);
            if (err_code == SYNC_ERR_CODE.NONE)
            {
                buffer.Write((int)mode);
                buffer.Write(IdMaps.Count);
                foreach (IdMap map in IdMaps)
                {
                    buffer.Write(map.src_begin);
                    buffer.Write(map.dst_begin);
                    buffer.Write(map.count);
                }
            }
        }
    }

    public class DesyncSceneMessage : INetworkMessage
    {
        public int scene_id;
        public bool unload;
        public void Deserialize(ReadBuffer buffer)
        {
            scene_id = buffer.ReadInt();
            unload = buffer.ReadBool();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(scene_id);
            buffer.Write(unload);
        }
    }

    public class UpdateNetworkTransformMessage : INetworkMessage
    {
        private readonly byte COMMIT_BITS = (byte)(STATE_MASK.COMMIT_POSITION | STATE_MASK.COMMIT_ROTATION | STATE_MASK.COMMIT_SCALE);
        private readonly byte UPDATE_BITS = (byte)(STATE_MASK.UPDATE_POSITION | STATE_MASK.UPDATE_ROTATION | STATE_MASK.UPDATE_SCALE);
        public enum STATE_MASK : byte
        {
            IS_INITIALIZE = 0x01,
            COMMIT_POSITION = 0x02,
            COMMIT_ROTATION = 0x04,
            COMMIT_SCALE = 0x08,
            UPDATE_POSITION = 0x10,
            UPDATE_ROTATION = 0x20,
            UPDATE_SCALE = 0x40
        }

        public byte status;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;

        public bool IsUpdate
        {
            get { return (status & UPDATE_BITS) != 0; }
            set { if (value) status |= UPDATE_BITS; else status &= (byte)(~UPDATE_BITS); }
        }
        public bool IsUpdatePosition
        {
            get { return (status & (byte)STATE_MASK.UPDATE_POSITION) != 0; }
            set { if (value) status |= (byte)STATE_MASK.UPDATE_POSITION; else status &= (byte)(~STATE_MASK.UPDATE_POSITION); }
        }
        public bool IsUpdateRotation
        {
            get { return (status & (byte)STATE_MASK.UPDATE_ROTATION) != 0; }
            set { if (value) status |= (byte)STATE_MASK.UPDATE_ROTATION; else status &= (byte)(~STATE_MASK.UPDATE_ROTATION); }
        }
        public bool IsUpdateScale
        {
            get { return (status & (byte)STATE_MASK.UPDATE_SCALE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.UPDATE_SCALE; else status &= (byte)(~STATE_MASK.UPDATE_SCALE); }
        }
        public bool IsInitialize
        {
            get { return (status & (byte)STATE_MASK.IS_INITIALIZE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.IS_INITIALIZE; else status &= (byte)(~STATE_MASK.IS_INITIALIZE); }
        }
        public bool IsCommit
        {
            get { return (status & COMMIT_BITS) != 0; }
            set { if (value) status |= COMMIT_BITS; else status &= (byte)(~COMMIT_BITS); }
        }
        public bool IsCommitPosition
        {
            get { return (status & (byte)STATE_MASK.COMMIT_POSITION) != 0; }
            set { if (value) status |= (byte)STATE_MASK.COMMIT_POSITION; else status &= (byte)(~STATE_MASK.COMMIT_POSITION); }
        }
        public bool IsCommitRotation
        {
            get { return (status & (byte)STATE_MASK.COMMIT_ROTATION) != 0; }
            set { if (value) status |= (byte)STATE_MASK.COMMIT_ROTATION; else status &= (byte)(~STATE_MASK.COMMIT_ROTATION); }
        }
        public bool IsCommitScale
        {
            get { return (status & (byte)STATE_MASK.COMMIT_SCALE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.COMMIT_SCALE; else status &= (byte)(~STATE_MASK.COMMIT_SCALE); }
        }
        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(status);
            if (IsUpdatePosition)
            {
                buffer.Write(position.x);
                buffer.Write(position.y);
                buffer.Write(position.z);
            }
            if (IsUpdateRotation)
            {
                buffer.Write(rotation.x);
                buffer.Write(rotation.y);
                buffer.Write(rotation.z);
            }
            if (IsUpdateScale)
            {
                buffer.Write(scale.x);
                buffer.Write(scale.y);
                buffer.Write(scale.z);
            }
        }
        public void Deserialize(ReadBuffer buffer)
        {
            status = buffer.ReadByte();
            if (IsUpdatePosition)
            {
                position.x = buffer.ReadFloat();
                position.y = buffer.ReadFloat();
                position.z = buffer.ReadFloat();
            }
            if (IsUpdateRotation)
            {
                rotation.x = buffer.ReadFloat();
                rotation.y = buffer.ReadFloat();
                rotation.z = buffer.ReadFloat();
            }
            if (IsUpdateScale)
            {
                scale.x = buffer.ReadFloat();
                scale.y = buffer.ReadFloat();
                scale.z = buffer.ReadFloat();
            }
        }
    }

    public class UpdateNetworkObjectMessage : INetworkMessage
    {
        public enum STATE_MASK : byte
        {
            ENABLE_UPDATE = 0x01,
            POSITION_UPDATE = 0x02,
            ROTATION_UPDATE = 0x04,
            SCALE_UPDATE = 0x08,
            PARENT_UPDATE = 0x10,
            ENABLE_VALUE = 0x80
        }

        public byte status;
        public bool enable;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
        public int parent;

        public bool IsUpdate
        {
            get { return (status & (byte)(~STATE_MASK.ENABLE_VALUE)) != 0; }
            set { if (value) status |= (byte)(~STATE_MASK.ENABLE_VALUE); else status &= (byte)(STATE_MASK.ENABLE_VALUE); }
        }
        public bool IsEnableUpdate
        {
            get { return (status & (byte)STATE_MASK.ENABLE_UPDATE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.ENABLE_UPDATE; else status &= (byte)(~STATE_MASK.ENABLE_UPDATE); }
        }
        public bool IsPositionUpdate
        {
            get { return (status & (byte)STATE_MASK.POSITION_UPDATE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.POSITION_UPDATE; else status &= (byte)(~STATE_MASK.POSITION_UPDATE); }
        }
        public bool IsRotationUpdate
        {
            get { return (status & (byte)STATE_MASK.ROTATION_UPDATE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.ROTATION_UPDATE; else status &= (byte)(~STATE_MASK.ROTATION_UPDATE); }
        }
        public bool IsScaleUpdate
        {
            get { return (status & (byte)STATE_MASK.SCALE_UPDATE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.SCALE_UPDATE; else status &= (byte)(~STATE_MASK.SCALE_UPDATE); }
        }
        public bool IsParentUpdate
        {
            get { return (status & (byte)STATE_MASK.PARENT_UPDATE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.PARENT_UPDATE; else status &= (byte)(~STATE_MASK.PARENT_UPDATE); }
        }
        public bool IsEnable
        {
            get { return (status & (byte)STATE_MASK.ENABLE_VALUE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.ENABLE_VALUE; else status &= (byte)(~STATE_MASK.ENABLE_VALUE); }
        }
        public void Serialize(WriteBuffer buffer)
        {
            if (IsEnableUpdate)
                IsEnable = enable;
            buffer.Write(status);
            if (IsPositionUpdate)
            {
                buffer.Write(position.x);
                buffer.Write(position.y);
                buffer.Write(position.z);
            }
            if (IsRotationUpdate)
            {
                buffer.Write(rotation.x);
                buffer.Write(rotation.y);
                buffer.Write(rotation.z);
            }
            if (IsScaleUpdate)
            {
                buffer.Write(scale.x);
                buffer.Write(scale.y);
                buffer.Write(scale.z);
            }
            if (IsParentUpdate)
                buffer.Write(parent);
        }
        public void Deserialize(ReadBuffer buffer)
        {
            status = buffer.ReadByte();
            if (IsEnableUpdate)
                enable = IsEnable;
            if (IsPositionUpdate)
            {
                position.x = buffer.ReadFloat();
                position.y = buffer.ReadFloat();
                position.z = buffer.ReadFloat();
            }
            if (IsRotationUpdate)
            {
                rotation.x = buffer.ReadFloat();
                rotation.y = buffer.ReadFloat();
                rotation.z = buffer.ReadFloat();
            }
            if (IsScaleUpdate)
            {
                scale.x = buffer.ReadFloat();
                scale.y = buffer.ReadFloat();
                scale.z = buffer.ReadFloat();
            }
            if (IsParentUpdate)
                parent = buffer.ReadInt();
        }
    }

    public partial class NetworkAnimatorLayer
    {
        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(data.flags);
            if (IsStateUpdate)
            {
                if (IsTransition)
                {
                    if (IsCrossFade)
                    {
                        buffer.Write(data.nextStateNameHash);
                        buffer.Write(data.duration);
                    }
                    else
                        buffer.Write(data.transition);
                }
                else
                    buffer.Write(data.nextStateNameHash);
                if (HasNormalizedTime)
                    buffer.Write(data.normalizedTime);
            }
            if (IsWeightUpdate)
                buffer.Write(data.weight);
        }

        public void Deserialize(ReadBuffer buffer)
        {
            data.flags = buffer.ReadByte();
            if (IsStateUpdate)
            {
                if (IsTransition)
                {
                    if (IsCrossFade)
                    {
                        data.nextStateNameHash = buffer.ReadInt();
                        data.duration = buffer.ReadFloat();
                    }
                    else
                        data.transition = buffer.ReadInt();
                }
                else
                    data.nextStateNameHash = buffer.ReadInt();
                if (HasNormalizedTime)
                    data.normalizedTime = buffer.ReadFloat();
                else
                    data.normalizedTime = 0.0f;
            }
            if (IsWeightUpdate)
                data.weight = buffer.ReadFloat();
            isChanged = true;
        }

        public void Deserialize(ReadBuffer buffer, float time)
        {
            Deserialize(buffer);
            if (useTimeSync)
                this.buffer.AddNode(this.data, time);
        }
    }

    public partial class ParameterFloat : NetworkAnimatorParameter
    {
        public override void Serialize(WriteBuffer buffer)
        {
            buffer.Write(value);
            if (interpolateMethod == InterpolateMethod.LINEAR || interpolateMethod == InterpolateMethod.ACCELERATED)
            {
                isCommit = false;
                if (isStreaming)
                {
                    if (!isChanged)
                    {
                        isCommit = true;
                        isStreaming = false;
                    }
                }
                else
                    isStreaming = true;
                buffer.Write(isCommit);
            }
        }
        public override void Deserialize(ReadBuffer buffer)
        {
            value = buffer.ReadFloat();
            if (interpolateMethod == InterpolateMethod.LINEAR || interpolateMethod == InterpolateMethod.ACCELERATED)
                isCommit = buffer.ReadBool();
            isChanged = true;
        }
        public override void Deserialize(ReadBuffer buffer, float time)
        {
            Deserialize(buffer);
            if (interpolateMethod != InterpolateMethod.NONE)
                interpolator.AddNode(new ValueInfo<float>(value, isCommit), time);
        }
    }
    public partial class ParameterInt : NetworkAnimatorParameter
    {
        public override void Serialize(WriteBuffer buffer)
        {
            buffer.Write(value);
        }
        public override void Deserialize(ReadBuffer buffer)
        {
            value = buffer.ReadInt();
            isChanged = true;
        }
        public override void Deserialize(ReadBuffer buffer, float time)
        {
            Deserialize(buffer);
            if (interpolateMethod != InterpolateMethod.NONE)
                interpolator.AddNode(new ValueInfo<int>(value, false), time);
        }
    }
    public partial class ParameterBool : NetworkAnimatorParameter
    {
        public override void Serialize(WriteBuffer buffer)
        {
            buffer.Write(value);
        }
        public override void Deserialize(ReadBuffer buffer)
        {
            value = buffer.ReadBool();
            isChanged = true;
        }
        public override void Deserialize(ReadBuffer buffer, float time)
        {
            Deserialize(buffer);
            if (interpolateMethod != InterpolateMethod.NONE)
                interpolator.AddNode(new ValueInfo<bool>(value, false), time);
        }
    }


    public class UpdateNetworkAnimatorMessage : INetworkMessage
    {
        public enum STATE_MASK : byte
        {
            IS_INITIALIZE = 0x01,
        }

        private byte status;
        public List<NetworkAnimatorLayer> layers;
        public List<NetworkAnimatorParameter> parameters;

        public bool IsInitialize
        {
            get { return (status & (byte)STATE_MASK.IS_INITIALIZE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.IS_INITIALIZE; else status &= (byte)(~STATE_MASK.IS_INITIALIZE); }
        }

        public UpdateNetworkAnimatorMessage(NetworkAnimator networkAnimator, bool useAnimationTimeSync, ParametersInterpolationInfo parametersInterpolationInfo, float syncPeriod)
        {
            status = 0;
            Animator animator = networkAnimator.animator;
            int layer_count = animator.layerCount;
            int param_count = animator.parameterCount;
            layers = new List<NetworkAnimatorLayer>(layer_count);
            parameters = new List<NetworkAnimatorParameter>(param_count);
            for (int i = 0; i < layer_count; i++)
                layers.Add(new NetworkAnimatorLayer(i, networkAnimator, useAnimationTimeSync));

            Dictionary<string, ParameterInterpolationInfo> parameters_tmp = new Dictionary<string, ParameterInterpolationInfo>();
            foreach (var param in parametersInterpolationInfo.parameters)
                parameters_tmp.Add(param.name, param);
            for (int i = 0; i < param_count; i++)
            {
                AnimatorControllerParameter param = animator.parameters[i];
                if (!animator.IsParameterControlledByCurve(param.nameHash))
                {
                    switch (param.type)
                    {
                        case AnimatorControllerParameterType.Bool:
                            parameters.Add(new ParameterBool(param.nameHash, networkAnimator, parameters_tmp[param.name].method));
                            break;
                        case AnimatorControllerParameterType.Int:
                            parameters.Add(new ParameterInt(param.nameHash, networkAnimator, parameters_tmp[param.name].method));
                            break;
                        case AnimatorControllerParameterType.Float:
                            parameters.Add(new ParameterFloat(param.nameHash, networkAnimator, parameters_tmp[param.name].method, syncPeriod, parameters_tmp[param.name].correctionSpeed));
                            break;
                    }
                }
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(status);
            int changed_count = 0; // layers
            int begin = buffer.Write(changed_count);
            for (int i = 0; i < layers.Count; i++)
            {
                NetworkAnimatorLayer layerInfo = layers[i];
                if (layerInfo.isChanged)
                {
                    buffer.Write(i);
                    layerInfo.Serialize(buffer);
                    changed_count++;
                }
            }
            if (changed_count != 0)
                buffer.Write(changed_count, begin);

            changed_count = 0; // params
            begin = buffer.Write(changed_count);
            for (int i = 0; i < parameters.Count; i++)
            {
                NetworkAnimatorParameter paramInfo = parameters[i];
                if (paramInfo.isChanged || paramInfo.isStreaming)
                {
                    buffer.Write(i);
                    paramInfo.Serialize(buffer);
                    changed_count++;
                }
            }
            if (changed_count != 0)
                buffer.Write(changed_count, begin);
        }

        public void Deserialize(ReadBuffer buffer)
        {
            Deserialize(buffer, -1.0f);
        }

        public void Deserialize(ReadBuffer buffer, float time)
        {
            bool is_time_set = time >= 0.0f;
            int iter;
            status = buffer.ReadByte();
            if (IsInitialize)
                is_time_set = false;
            int changed_count = buffer.ReadInt(); // layers
            for (int i = 0; i < changed_count; i++)
            {
                iter = buffer.ReadInt();
                if (is_time_set)
                    layers[iter].Deserialize(buffer, time);
                else
                    layers[iter].Deserialize(buffer);
            }
            changed_count = buffer.ReadInt(); // params
            for (int i = 0; i < changed_count; i++)
            {
                iter = buffer.ReadInt();
                if (is_time_set)
                    parameters[iter].Deserialize(buffer, time);
                else
                    parameters[iter].Deserialize(buffer);
            }
        }
    }

    public class NetworkCharacterControllerAttachMessage : INetworkMessage
    {
        public void Serialize(WriteBuffer buffer)
        {
        }

        public void Deserialize(ReadBuffer buffer)
        {
        }
    }
    public class NetworkCharacterControllerDetachMessage : INetworkMessage
    {
        public void Serialize(WriteBuffer buffer)
        {
        }
        public void Deserialize(ReadBuffer buffer)
        {
        }
    }
    public class NetworkCharacterControllerControlMessage : INetworkMessage
    {
        public enum STATE_MASK : short
        {
            POSITION_UPDATE = 0x001,
            ROTATION_UPDATE = 0x002,
            CAM_POSITION_UPDATE = 0x004,
            MOVE_FORWARD_UPDATE = 0x008,
            MOVE_RIGHT_UPDATE = 0x010,
            JUMP_UPDATE = 0x020,
            SPEED_RUN_UPDATE = 0x040,
            SPEED_RUN_VALUE = 0x080,
        }

        public Vector3 position;
        public Vector3 rotation;
        public Vector3 camPosition;
        public short status;
        public float deltaMoveForvard;
        public float deltaMoveRight;
        public float deltaJump;
        public bool speedRun;

        public bool IsUpdate
        {
            get { return (status & (short)(~STATE_MASK.SPEED_RUN_VALUE)) != 0; }
            set { if (value) status |= (short)(~STATE_MASK.SPEED_RUN_VALUE); else status &= (short)(STATE_MASK.SPEED_RUN_VALUE); }
        }
        public bool IsPositionUp
        {
            get { return (status & (short)STATE_MASK.POSITION_UPDATE) != 0; }
            set { if (value) status |= (short)STATE_MASK.POSITION_UPDATE; else status &= (short)(~STATE_MASK.POSITION_UPDATE); }
        }
        public bool IsRotationUp
        {
            get { return (status & (short)STATE_MASK.ROTATION_UPDATE) != 0; }
            set { if (value) status |= (short)STATE_MASK.ROTATION_UPDATE; else status &= (short)(~STATE_MASK.ROTATION_UPDATE); }
        }
        public bool IsCamPositionUp
        {
            get { return (status & (short)STATE_MASK.CAM_POSITION_UPDATE) != 0; }
            set { if (value) status |= (short)STATE_MASK.CAM_POSITION_UPDATE; else status &= (short)(~STATE_MASK.CAM_POSITION_UPDATE); }
        }
        public bool IsMoveForwardUp
        {
            get { return (status & (short)STATE_MASK.MOVE_FORWARD_UPDATE) != 0; }
            set { if (value) status |= (short)STATE_MASK.MOVE_FORWARD_UPDATE; else status &= (short)(~STATE_MASK.MOVE_FORWARD_UPDATE); }
        }
        public bool IsMoveRightUp
        {
            get { return (status & (short)STATE_MASK.MOVE_RIGHT_UPDATE) != 0; }
            set { if (value) status |= (short)STATE_MASK.MOVE_RIGHT_UPDATE; else status &= (short)(~STATE_MASK.MOVE_RIGHT_UPDATE); }
        }
        public bool IsJumpUp
        {
            get { return (status & (short)STATE_MASK.JUMP_UPDATE) != 0; }
            set { if (value) status |= (short)STATE_MASK.JUMP_UPDATE; else status &= (short)(~STATE_MASK.JUMP_UPDATE); }
        }
        public bool IsSpeedRunUp
        {
            get { return (status & (short)STATE_MASK.SPEED_RUN_UPDATE) != 0; }
            set { if (value) status |= (short)STATE_MASK.SPEED_RUN_UPDATE; else status &= (short)(~STATE_MASK.SPEED_RUN_UPDATE); }
        }

        private bool GetSpeedRunValue()
        {
            return (status & (short)STATE_MASK.SPEED_RUN_VALUE) != 0;
        }
        private void SetSpeedRunValue(bool value)
        {
            if (value)
                status |= (short)STATE_MASK.SPEED_RUN_VALUE;
            else
                status &= (short)(~STATE_MASK.SPEED_RUN_VALUE);
        }
        public void Serialize(WriteBuffer buffer)
        {
            if (IsSpeedRunUp)
                SetSpeedRunValue(speedRun);
            buffer.Write(status);
            if (IsPositionUp)
            {
                buffer.Write(position.x);
                buffer.Write(position.y);
                buffer.Write(position.z);
            }
            if (IsRotationUp)
            {
                buffer.Write(rotation.x);
                buffer.Write(rotation.y);
                buffer.Write(rotation.z);
            }
            if (IsCamPositionUp)
            {
                buffer.Write(camPosition.x);
                buffer.Write(camPosition.y);
                buffer.Write(camPosition.z);
            }
            if (IsMoveForwardUp)
                buffer.Write(deltaMoveForvard);
            if (IsMoveRightUp)
                buffer.Write(deltaMoveRight);
            if (IsJumpUp)
                buffer.Write(deltaJump);
        }

        public void Deserialize(ReadBuffer buffer)
        {
            status = buffer.ReadShort();
            if (IsPositionUp)
            {
                position.x = buffer.ReadFloat();
                position.y = buffer.ReadFloat();
                position.z = buffer.ReadFloat();
            }
            if (IsRotationUp)
            {
                rotation.x = buffer.ReadFloat();
                rotation.y = buffer.ReadFloat();
                rotation.z = buffer.ReadFloat();
            }
            if (IsCamPositionUp)
            {
                camPosition.x = buffer.ReadFloat();
                camPosition.y = buffer.ReadFloat();
                camPosition.z = buffer.ReadFloat();
            }
            if (IsMoveForwardUp)
                deltaMoveForvard = buffer.ReadFloat();
            if (IsMoveRightUp)
                deltaMoveRight = buffer.ReadFloat();
            if (IsJumpUp)
                deltaJump = buffer.ReadFloat();
            if (IsSpeedRunUp)
                speedRun = GetSpeedRunValue();
        }
    }
    public class NetworkCharacterControllerCorrectionMessage : INetworkMessage
    {
        public void Serialize(WriteBuffer buffer)
        {
        }

        public void Deserialize(ReadBuffer buffer)
        {
        }
    }

    public class UpdateNetworkTextMessage : INetworkMessage
    {
        public enum STATE_MASK : byte
        {
            TEXT_UPDATE = 0x01,
            COLOR_UPDATE = 0x02,
        }

        public string text;
        public Color color;
        public byte status;

        public bool IsUpdate
        {
            get { return status != 0; }
            set { if (value) status = (byte)(STATE_MASK.TEXT_UPDATE | STATE_MASK.COLOR_UPDATE); else status = 0; }
        }
        public bool IsTextUp
        {
            get { return (status & (byte)STATE_MASK.TEXT_UPDATE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.TEXT_UPDATE; else status &= (byte)(~STATE_MASK.TEXT_UPDATE); }
        }
        public bool IsColorUp
        {
            get { return (status & (byte)STATE_MASK.COLOR_UPDATE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.COLOR_UPDATE; else status &= (byte)(~STATE_MASK.COLOR_UPDATE); }
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(status);
            if (IsTextUp)
            {
                byte[] text_bytes = Encoding.UTF8.GetBytes(text);
                buffer.Write(text_bytes.Length);
                buffer.Write(text_bytes, 0, text_bytes.Length);
            }
            if (IsColorUp)
            {
                buffer.Write(color.r);
                buffer.Write(color.g);
                buffer.Write(color.b);
                buffer.Write(color.a);
            }
        }

        public void Deserialize(ReadBuffer buffer)
        {
            status = buffer.ReadByte();
            if (IsTextUp)
            {
                int size = buffer.ReadInt();
                byte[] text_bytes = buffer.ReadArray(size);
                text = Encoding.UTF8.GetString(text_bytes);
            }
            if (IsColorUp)
            {
                color.r = buffer.ReadFloat();
                color.g = buffer.ReadFloat();
                color.b = buffer.ReadFloat();
                color.a = buffer.ReadFloat();
            }
        }
    }

    public class UpdateNetworkCharacterMessage : NetworkCharacterInfo, INetworkMessage
    {
    }
}