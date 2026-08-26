using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Transport;
using UnityEditor;

#if UNITY_EDITOR
using UnityEditor.Animations;
#endif

namespace Network
{
    [RequireComponent(typeof(Animator))]
    public class NetworkAnimator : NetworkBehaviour, ISerializationCallbackReceiver
    {
        [Serializable]
        internal class TransitionInfo
        {
            public bool isExit;
            public int nameHash;
            public int layer;
            public int srcState;
            public int dstState;
            public float duration;
            public float offset;
        }

        [Serializable]
        public class ParameterInterpolationInfo
        {
            public AnimatorControllerParameterType paramType;
            public string name;
            public NetworkAnimatorParameter.InterpolateMethod method;
            public float correctionSpeed;
        }
        [Serializable]
        public class ParametersInterpolationInfo
        {
            public List<ParameterInterpolationInfo> parameters;
            public int hashCode;
        }

        [SerializeField]
        private float synchronizePeriod = 0.05f;
        [SerializeField]
        private bool useAnimationTimeSync = true;
        [SerializeField]
        private ParametersInterpolationInfo parametersInterpolationInfo;
        public Animator animator { get; private set; }
        private UpdateNetworkAnimatorMessage message;
        private UpdateNetworkAnimatorMessage initMessage;
        private bool isChanged;
        private float lastUpdateTime;

        [SerializeField, HideInInspector]
        internal List<TransitionInfo> transitions;
        internal List<Dictionary<int, Dictionary<int, int>>> srcToDstTransitionTable;
        internal List<Dictionary<int, int>> hashToIdTransitionTable;

#if UNITY_EDITOR
        private static float GetStateLength(AnimatorState state)
        {
            //TODO fixme for blend tree
            return state.motion.averageDuration / state.speed;
        }
        private void ParseStateMachine(int layerID, AnimatorControllerLayer layer)
        {
            AnimatorStateMachine stateMachine = layer.stateMachine;
            for (int i = 0; i < stateMachine.states.Length; i++)
            {
                AnimatorState state = stateMachine.states[i].state;
                for (int j = 0; j < state.transitions.Length; j++)
                {
                    AnimatorStateTransition transition = state.transitions[j];
                    TransitionInfo transitionInfo = new TransitionInfo();
                    transitionInfo.layer = layerID;
                    transitionInfo.srcState = Animator.StringToHash(string.Format("{0}.{1}", layer.name, state.name));
                    if (transition.hasFixedDuration)
                        transitionInfo.duration = transition.duration;
                    else
                        transitionInfo.duration = GetStateLength(state) * transition.duration;
                    transitionInfo.isExit = transition.isExit;

                    string dstStateName;
                    if (!transition.isExit)
                    {
                        AnimatorState nextState;
                        if (transition.destinationStateMachine != null)
                        {
                            nextState = transition.destinationStateMachine.defaultState;
                            dstStateName = transition.destinationStateMachine.name;
                        }
                        else
                        {
                            nextState = transition.destinationState;
                            dstStateName = nextState.name;
                        }
                        transitionInfo.dstState = Animator.StringToHash(string.Format("{0}.{1}", layer.name, nextState.name));
                        transitionInfo.offset = GetStateLength(nextState) * transition.offset;
                    }
                    else
                    {
                        dstStateName = "Exit";
                        transitionInfo.dstState = 0;
                    }
                    string transitionName = string.Format("{0}.{1} -> {2}.{3}", layer.name, state.name, layer.name, dstStateName);
                    transitionInfo.nameHash = Animator.StringToHash(transitionName);
                    transitions.Add(transitionInfo);
                }
            }
            for (int i = 0; i < stateMachine.stateMachines.Length; i++)
                ParseStateMachine(layerID, layer);
        }
#endif

        public void OnBeforeSerialize()
        {
#if UNITY_EDITOR
            if (EditorApplication.isUpdating)
                return;
            Animator animator_tmp = transform.GetComponent<Animator>();
            if (animator_tmp == null)
                return;
            AnimatorController controller = animator_tmp.runtimeAnimatorController as AnimatorController;
            if (controller == null)
                return;

            transitions = new List<TransitionInfo>();
            for (int i = 0; i < controller.layers.Length; i++)
                ParseStateMachine(i, controller.layers[i]);
#endif
        }

        public void OnAfterDeserialize()
        {
            if (transitions != null)
            {
                srcToDstTransitionTable = new List<Dictionary<int, Dictionary<int, int>>>();
                hashToIdTransitionTable = new List<Dictionary<int, int>>();

                for (int i = 0; i < transitions.Count; i++)
                {
                    TransitionInfo transition = transitions[i];
                    while (srcToDstTransitionTable.Count <= transition.layer)
                    {
                        srcToDstTransitionTable.Add(new Dictionary<int, Dictionary<int, int>>());
                        hashToIdTransitionTable.Add(new Dictionary<int, int>());
                    }
                    var srcDstTable = srcToDstTransitionTable[transition.layer];
                    if (!srcDstTable.ContainsKey(transition.srcState))
                        srcToDstTransitionTable[transition.layer][transition.srcState] = new Dictionary<int, int>();
                    srcDstTable[transition.srcState].Add(transition.dstState, i);
                    hashToIdTransitionTable[transition.layer].Add(transition.nameHash, i);
                }
            }
        }

        public override void OnNetworkInitialize()
        {
            base.OnNetworkInitialize();
            animator = transform.GetComponent<Animator>();
            message = new UpdateNetworkAnimatorMessage(this, useAnimationTimeSync, parametersInterpolationInfo, synchronizePeriod);
            if (IsOwner)
            {
                initMessage = new UpdateNetworkAnimatorMessage(this, useAnimationTimeSync, parametersInterpolationInfo, synchronizePeriod);
                initMessage.IsInitialize = true;
            }
            isChanged = false;
            lastUpdateTime = 0.0f;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
        }
        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            foreach (var layer in message.layers)
                layer.Clear();
            foreach (var parameter in message.parameters)
                parameter.Clear();
        }

        void Update()
        {

            if (!IsOwner)
            {
                float time = message.IsInitialize ? -1.0f : NetworkSceneManager.Singleton.GetHostTime(networkObject.host) - NetworkSceneManager.Singleton.streamClientDelay;
                foreach (var layer in message.layers)
                    layer.Update(time);
                foreach (var param in message.parameters)
                    param.Update(time);
            }
        }

        public override void OnBeforeSendMessage()
        {
            base.OnBeforeSendMessage();
            if (IsOwner)
            {
                foreach (var layer in message.layers)
                    isChanged |= layer.CheckChange();
                foreach (var param in message.parameters)
                    isChanged |= param.CheckChange();
            }
        }

        public override void OnReceiveUpdateMessage(int id, ReadBuffer buffer, int end, float send_time)
        {
            base.OnReceiveUpdateMessage(id, buffer, end, send_time);
            if (!IsOwner)
                message.Deserialize(buffer, send_time);
        }

        public override void OnSendInitializeMessage(WriteBuffer buffer)
        {
            base.OnSendInitializeMessage(buffer);
            if (IsOwner)
            {
                bool sendInitMessage = false;
                for (int i = 0; i < message.layers.Count; i++)
                {
                    NetworkAnimatorLayer src_layer = message.layers[i];
                    NetworkAnimatorLayer dst_layer = initMessage.layers[i];
                    dst_layer.data.nextStateNameHash = src_layer.data.nextStateNameHash;
                    dst_layer.IsStateUpdate = true;
                    dst_layer.isChanged = true;
                    sendInitMessage = true;
                    float dt = NetworkSceneManager.Singleton.time - src_layer.lastAnimationChangeTime;
                    float normalizedTime = src_layer.data.duration != 0.0f ? dt / src_layer.data.duration : 0.0f;
                    if (normalizedTime != 0.0f)
                    {
                        dst_layer.HasNormalizedTime = true;
                        dst_layer.data.normalizedTime = normalizedTime;
                    }
                    else
                        dst_layer.HasNormalizedTime = false;
                    if (src_layer.IsTransition && normalizedTime < 1.0f)
                    {
                        dst_layer.IsTransition = true;
                        if (src_layer.IsCrossFade)
                        {
                            dst_layer.IsCrossFade = true;
                            dst_layer.data.duration = src_layer.data.duration;
                        }
                        else
                        {
                            dst_layer.IsCrossFade = false;
                            dst_layer.data.transition = src_layer.data.transition;
                        }
                    }
                    else
                    {
                        dst_layer.IsTransition = false;
                        dst_layer.IsCrossFade = false;
                    }

                    dst_layer.IsWeightUpdate |= src_layer.IsWeightUpdate;
                    dst_layer.data.weight = src_layer.data.weight;
                }
                for (int i = 0; i < message.parameters.Count; i++)
                {
                    NetworkAnimatorParameter src_param = message.parameters[i];
                    NetworkAnimatorParameter dst_param = initMessage.parameters[i];
                    dst_param.isChanged |= dst_param.CheckChange(src_param);
                    if (dst_param.isChanged)
                        sendInitMessage = true;
                }
                if (sendInitMessage)
                    initMessage.Serialize(buffer);

                if (NetworkSceneManager.Singleton.lastUpdateTime - lastUpdateTime > synchronizePeriod && isChanged)
                {
                    foreach (NetworkAnimatorLayer layer in message.layers)
                    {
                        layer.isChanged = false;
                        layer.IsStateUpdate = false;
                        layer.IsWeightUpdate = false;
                    }
                    foreach (var param in message.parameters)
                    {
                        param.isChanged = false;
                        param.isStreaming = false;
                    }
                    isChanged = false;
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
                    if (isChanged)
                    {
                        message.Serialize(buffer);
                        foreach (NetworkAnimatorLayer layer in message.layers)
                        {
                            layer.isChanged = false;
                            layer.IsStateUpdate = false;
                            layer.IsWeightUpdate = false;
                        }
                        isChanged = false;
                        foreach (var param in message.parameters)
                        {
                            param.isChanged = false;
                            isChanged |= param.isStreaming;
                        }
                    }
                    lastUpdateTime = NetworkSceneManager.Singleton.time;
                }
            }
        }
    }

    public partial class NetworkAnimatorLayer
    {
        public struct LayerData
        {
            public byte flags;
            public int nextStateNameHash;
            public int transition;
            public float normalizedTime;
            public float duration;
            public float weight;
        }
        public enum LAYER_STATE_MASK : byte
        {
            STATE_UPDATE = 0x01,
            IS_TRANSITION = 0x02,
            IS_CROSSFADE = 0x04,
            HAS_NORMALIZED_TIME = 0x08,
            WEIGHT_UPDATE = 0x10
        }
        public NetworkAnimatorLayer(int id, NetworkAnimator networkAnimator, bool useTimeSync)
        {
            AnimatorStateInfo state = networkAnimator.animator.GetCurrentAnimatorStateInfo(id);
            this.id = id;
            this.networkAnimator = networkAnimator;
            this.isChanged = false;
            this.lastAnimationChangeTime = 0.0f;
            this.data.flags = 0;
            this.data.nextStateNameHash = state.fullPathHash;
            this.data.transition = 0;
            this.data.normalizedTime = 0.0f;
            this.data.duration = state.length / state.speed;
            this.data.weight = networkAnimator.animator.GetLayerWeight(id);
            this.useTimeSync = useTimeSync;
            if (!networkAnimator.IsOwner && useTimeSync)
                this.buffer = new TimelineBuffer<LayerData>(0);
            else
                this.buffer = null;
        }
        public bool IsStateUpdate
        {
            get { return (data.flags & (byte)LAYER_STATE_MASK.STATE_UPDATE) != 0; }
            set { if (value) data.flags |= (byte)LAYER_STATE_MASK.STATE_UPDATE; else data.flags &= (byte)(~LAYER_STATE_MASK.STATE_UPDATE); }
        }
        public bool IsTransition
        {
            get { return (data.flags & (byte)LAYER_STATE_MASK.IS_TRANSITION) != 0; }
            set { if (value) data.flags |= (byte)LAYER_STATE_MASK.IS_TRANSITION; else data.flags &= (byte)(~LAYER_STATE_MASK.IS_TRANSITION); }
        }
        public bool IsCrossFade
        {
            get { return (data.flags & (byte)LAYER_STATE_MASK.IS_CROSSFADE) != 0; }
            set { if (value) data.flags |= (byte)LAYER_STATE_MASK.IS_CROSSFADE; else data.flags &= (byte)(~LAYER_STATE_MASK.IS_CROSSFADE); }
        }
        public bool HasNormalizedTime
        {
            get { return (data.flags & (byte)LAYER_STATE_MASK.HAS_NORMALIZED_TIME) != 0; }
            set { if (value) data.flags |= (byte)LAYER_STATE_MASK.HAS_NORMALIZED_TIME; else data.flags &= (byte)(~LAYER_STATE_MASK.HAS_NORMALIZED_TIME); }
        }
        public bool IsWeightUpdate
        {
            get { return (data.flags & (byte)LAYER_STATE_MASK.WEIGHT_UPDATE) != 0; }
            set { if (value) data.flags |= (byte)LAYER_STATE_MASK.WEIGHT_UPDATE; else data.flags &= (byte)(~LAYER_STATE_MASK.WEIGHT_UPDATE); }
        }

        private int id;
        private NetworkAnimator networkAnimator;
        private bool useTimeSync;
        private TimelineBuffer<LayerData> buffer;

        public LayerData data;
        public bool isChanged { get; set; }
        public float lastAnimationChangeTime { get; private set; }

        public bool CheckChange()
        {
            Animator animator = networkAnimator.animator;
            if (animator.IsInTransition(id))
            {
                AnimatorTransitionInfo transition = animator.GetAnimatorTransitionInfo(id);
                AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(id);
                if (data.nextStateNameHash != nextState.fullPathHash)
                {
                    data.nextStateNameHash = nextState.fullPathHash;
                    IsStateUpdate = true;
                    IsTransition = true;
                    isChanged = true;
                    lastAnimationChangeTime = Time.time;
                    if (transition.normalizedTime != 0.0f)
                    {
                        data.normalizedTime = transition.normalizedTime;
                        HasNormalizedTime = true;
                    }
                    else
                        HasNormalizedTime = false;
                    if (transition.anyState && transition.fullPathHash == 0) // crossfade
                    {
                        IsCrossFade = true;
                        if (transition.durationUnit == DurationUnit.Fixed)
                            data.duration = transition.duration;
                        else
                        {
                            AnimatorStateInfo currState = animator.GetCurrentAnimatorStateInfo(id);
                            data.duration = (currState.length / currState.speed) * transition.duration;
                        }
                    }
                    else // transition
                    {
                        IsCrossFade = false;
                        int transitionID = networkAnimator.hashToIdTransitionTable[id][transition.fullPathHash];
                        this.data.transition = transitionID;
                        this.data.duration = networkAnimator.transitions[transitionID].duration;
                    }
                }
            }
            else
            {
                AnimatorStateInfo currState = animator.GetCurrentAnimatorStateInfo(id);
                if (data.nextStateNameHash != currState.fullPathHash)
                {
                    data.nextStateNameHash = currState.fullPathHash;
                    IsStateUpdate = true;
                    isChanged = true;
                    IsTransition = false;
                    IsCrossFade = false;
                    lastAnimationChangeTime = Time.time;
                    data.duration = currState.length / currState.speed;
                    if (currState.normalizedTime != 0.0f)
                    {
                        data.normalizedTime = currState.normalizedTime;
                        HasNormalizedTime = true;
                    }
                    else
                        HasNormalizedTime = false;
                }
            }

            float currWeight = animator.GetLayerWeight(id);
            if (data.weight != currWeight)
            {
                data.weight = currWeight;
                IsWeightUpdate = true;
                isChanged = true;
            }
            return isChanged;
        }

        public void Update(float time = -1.0f)
        {
            if (useTimeSync && time >= 0.0f)
            {
                LayerData layerData;
                isChanged = buffer.TryLatest(time, out layerData);
                if (isChanged)
                    this.data = layerData;
            }
            if (!isChanged)
                return;
            Animator animator = networkAnimator.animator;
            if (IsStateUpdate)
            {
                if (IsTransition)
                {
                    int currStateNameHash;
                    if (animator.IsInTransition(id))
                        currStateNameHash = animator.GetNextAnimatorStateInfo(id).fullPathHash;
                    else
                        currStateNameHash = animator.GetCurrentAnimatorStateInfo(id).fullPathHash;
                    if (IsCrossFade)
                    {
                        if (currStateNameHash != data.nextStateNameHash)
                            animator.CrossFadeInFixedTime(data.nextStateNameHash, data.duration, id, 0, data.normalizedTime);
                    }
                    else
                    {
                        NetworkAnimator.TransitionInfo transitionInfo = networkAnimator.transitions[this.data.transition];
                        if (currStateNameHash != transitionInfo.dstState)
                        {
                            if (currStateNameHash == transitionInfo.srcState || !(networkAnimator.srcToDstTransitionTable.Count > id &&
                                networkAnimator.srcToDstTransitionTable[id].ContainsKey(currStateNameHash) &&
                                networkAnimator.srcToDstTransitionTable[id][currStateNameHash].ContainsKey(transitionInfo.dstState))
                               )
                            {
                                animator.CrossFadeInFixedTime(transitionInfo.dstState, transitionInfo.duration, id, transitionInfo.offset, data.normalizedTime);
                            }
                            else
                            {
                                int trID = networkAnimator.srcToDstTransitionTable[id][currStateNameHash][transitionInfo.dstState];
                                transitionInfo = networkAnimator.transitions[trID];
                                animator.CrossFadeInFixedTime(transitionInfo.dstState, transitionInfo.duration, id, transitionInfo.offset, data.normalizedTime);
                            }
                        }
                    }
                }
                else
                    animator.Play(data.nextStateNameHash, id, data.normalizedTime);
            }
            if (IsWeightUpdate)
            {
                if (animator.GetLayerWeight(id) != data.weight)
                    animator.SetLayerWeight(id, data.weight);
            }
            isChanged = false;
        }

        public void Clear()
        {
            if (buffer != null)
                buffer.Clear();
        }
    }

    public abstract class NetworkAnimatorParameter
    {
        public enum InterpolateMethod
        {
            NONE = 0,
            TIME_SYNC_WITHOUT_INTERPOLATION = 1,
            LINEAR = 2,
            ACCELERATED = 3
        }
        protected int id;
        protected NetworkAnimator networkAnimator;
        protected InterpolateMethod interpolateMethod;
        public bool isChanged { get; set; }
        public bool isStreaming { get; set; }

        public NetworkAnimatorParameter(int id, NetworkAnimator networkAnimator, InterpolateMethod interpolateMethod)
        {
            isChanged = false;
            isStreaming = false;
            this.id = id;
            this.networkAnimator = networkAnimator;
            this.interpolateMethod = interpolateMethod;
        }
        public abstract void Serialize(WriteBuffer buffer);
        public abstract void Deserialize(ReadBuffer buffer);
        public abstract void Deserialize(ReadBuffer buffer, float time);
        public abstract bool CheckChange();
        public abstract void Update(float time = 0.0f);
        public abstract bool CheckChange(NetworkAnimatorParameter other);
        public abstract void Clear();
    }

    public partial class ParameterFloat : NetworkAnimatorParameter
    {
        private static float epsilon = 0.01f;
        public float value;
        private bool isCommit;
        private TimelineInterpolator<float> interpolator;
        public ParameterFloat(int id, NetworkAnimator networkAnimator, InterpolateMethod interpolateMethod, float syncPeriod, float correctionSpeed) : base(id, networkAnimator, interpolateMethod)
        {
            isCommit = false;
            value = networkAnimator.animator.GetFloat(id);
            if (!networkAnimator.IsOwner)
            {
                switch (interpolateMethod)
                {
                    case InterpolateMethod.TIME_SYNC_WITHOUT_INTERPOLATION:
                        interpolator = new NoneInterpolator<float>();
                        break;
                    case InterpolateMethod.LINEAR:
                        interpolator = new FloatLinearInterpolator(syncPeriod) { Mode = FloatLinearInterpolator.InterpolateType.LINEAR, correctionSpeed = correctionSpeed };
                        break;
                    case InterpolateMethod.ACCELERATED:
                        interpolator = new FloatLinearInterpolator(syncPeriod) { Mode = FloatLinearInterpolator.InterpolateType.ACCELERATED, correctionSpeed = correctionSpeed };
                        break;
                    default:
                        interpolator = null;
                        break;

                }
            }
        }
        public override bool CheckChange()
        {
            float new_value = networkAnimator.animator.GetFloat(id);
            if (Mathf.Abs(new_value - value) > epsilon)
            {
                value = new_value;
                isChanged = true;
            }
            return isChanged;
        }
        public override void Update(float time = -1.0f)
        {
            if (interpolateMethod != InterpolateMethod.NONE && time >= 0.0f)
            {
                float result;
                isChanged = interpolator.Interpolate(time, networkAnimator.animator.GetFloat(id), out result);
                if (isChanged)
                    value = result;
            }
            if (!isChanged)
                return;

            networkAnimator.animator.SetFloat(id, value);
            isChanged = false;
        }
        public override bool CheckChange(NetworkAnimatorParameter other)
        {
            ParameterFloat other_param = other as ParameterFloat;
            if (Mathf.Abs(other_param.value - value) > epsilon)
            {
                value = other_param.value;
                return true;
            }
            return false;
        }

        public override void Clear()
        {
            if (interpolator != null)
                interpolator.Clear();
        }
    }
    public partial class ParameterInt : NetworkAnimatorParameter
    {
        private int value;
        private TimelineInterpolator<int> interpolator;
        public ParameterInt(int id, NetworkAnimator networkAnimator, InterpolateMethod interpolateMethod) : base(id, networkAnimator, interpolateMethod)
        {
            value = networkAnimator.animator.GetInteger(id);
            if (!networkAnimator.IsOwner && interpolateMethod != InterpolateMethod.NONE)
                interpolator = new NoneInterpolator<int>();
        }
        public override bool CheckChange()
        {
            int new_value = networkAnimator.animator.GetInteger(id);
            if (new_value != value)
            {
                value = new_value;
                isChanged = true;
            }
            return isChanged;
        }
        public override void Update(float time = -1.0f)
        {
            if (interpolateMethod != InterpolateMethod.NONE && time >= 0.0f)
            {
                int result;
                isChanged = interpolator.Interpolate(time, networkAnimator.animator.GetInteger(id), out result);
                if (isChanged)
                    value = result;
            }
            if (!isChanged)
                return;
            networkAnimator.animator.SetInteger(id, value);
            isChanged = false;
        }
        public override bool CheckChange(NetworkAnimatorParameter other)
        {
            ParameterInt other_param = other as ParameterInt;
            if (other_param.value != value)
            {
                value = other_param.value;
                return true;
            }
            return false;
        }
        public override void Clear()
        {
        }
    }
    public partial class ParameterBool : NetworkAnimatorParameter
    {
        public bool value;
        private TimelineInterpolator<bool> interpolator;
        public ParameterBool(int id, NetworkAnimator networkAnimator, InterpolateMethod interpolateMethod) : base(id, networkAnimator, interpolateMethod)
        {
            value = networkAnimator.animator.GetBool(id);
            if (!networkAnimator.IsOwner && interpolateMethod != InterpolateMethod.NONE)
                interpolator = new NoneInterpolator<bool>();
        }
        public override bool CheckChange()
        {
            bool new_value = networkAnimator.animator.GetBool(id);
            if (new_value != value)
            {
                value = new_value;
                isChanged = true;
            }
            return isChanged;
        }
        public override void Update(float time = -1.0f)
        {
            if (interpolateMethod != InterpolateMethod.NONE && time >= 0.0f)
            {
                bool result;
                isChanged = interpolator.Interpolate(time, networkAnimator.animator.GetBool(id), out result);
                if (isChanged)
                    value = result;
            }
            if (!isChanged)
                return;
            networkAnimator.animator.SetBool(id, value);
            isChanged = false;
        }
        public override bool CheckChange(NetworkAnimatorParameter other)
        {
            ParameterBool other_param = other as ParameterBool;
            if (other_param.value != value)
            {
                value = other_param.value;
                return true;
            }
            return false;
        }
        public override void Clear()
        {
        }
    }
}