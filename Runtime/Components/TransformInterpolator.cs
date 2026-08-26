using System.Collections.Generic;
using UnityEngine;

namespace Network
{
    public class TimelineBuffer<T>
    {
        protected class Node
        {
            public T data;
            public float time;
        }

        protected LinkedList<Node> buffer;
        protected int MaxOldNodes { get; }
        protected int OldNodesNum { get; set; }
        protected float MinTimeLimit { get; set; }
        protected LinkedListNode<Node> Context { get; set; }
        public int Length { get { return buffer.Count; } }

        public TimelineBuffer(int maxOldNodes = 0)
        {
            buffer = new LinkedList<Node>();
            Context = null;
            MaxOldNodes = maxOldNodes;
            OldNodesNum = 0;
            MinTimeLimit = 0.0f;
        }

        public virtual void AddNode(T data, float time)
        {
            if (time > MinTimeLimit)
            {
                Node point = new Node();
                point.data = data;
                point.time = time;

                LinkedListNode<Node> node = buffer.Last;
                while (true)
                {
                    if (node == null)
                    {
                        buffer.AddFirst(point);
                        break;
                    }
                    else if (point.time >= node.Value.time)
                    {
                        buffer.AddAfter(node, point);
                        break;
                    }
                    else
                        node = node.Previous;
                }
                if (Context != null && point.time < Context.Value.time)
                    if (OldNodesNum < MaxOldNodes)
                        OldNodesNum += 1;
                    else
                        buffer.RemoveFirst();
            }
        }

        public bool TryOldest(float time, out T data)
        {
            if (buffer.Count != 0)
            {
                LinkedListNode<Node> next_node;
                next_node = Context != null ? Context.Next : buffer.First;
                if (next_node != null)
                {
                    if (time >= next_node.Value.time)
                    {
                        data = next_node.Value.data;
                        MinTimeLimit = next_node.Value.time;
                        if (MaxOldNodes > 0)
                            Context = next_node;

                        if (OldNodesNum < MaxOldNodes)
                            OldNodesNum += 1;
                        else
                            buffer.RemoveFirst();
                        return true;
                    }
                }
            }
            data = default;
            return false;
        }

        public bool TryLatest(float time, out T data)
        {
            data = default;
            bool result = false;
            if (buffer.Count != 0)
            {
                LinkedListNode<Node> next_node;
                next_node = buffer.First;
                while (true)
                {
                    if (next_node == null)
                        break;
                    else
                    {
                        if (time >= next_node.Value.time)
                        {
                            data = next_node.Value.data;
                            result = true;
                            MinTimeLimit = next_node.Value.time;
                            next_node = next_node.Next;
                            buffer.RemoveFirst();
                        }
                        else
                            break;
                    }
                }
            }
            return result;
        }

        public void Clear()
        {
            buffer.Clear();
            Context = null;
            OldNodesNum = 0;
            MinTimeLimit = 0.0f;
        }
    }

    public struct ValueInfo<T>
    {
        public T value;
        public bool isCommit;
        public ValueInfo(T value, bool isCommit)
        {
            this.value = value;
            this.isCommit = isCommit;
        }
    }

    public abstract class TimelineInterpolator<T> : TimelineBuffer<ValueInfo<T>>
    {
        public abstract bool Interpolate(float time, T current, out T data);
        public TimelineInterpolator(int maxOldNodes) : base(maxOldNodes) { }
    }

    public class NoneInterpolator<T> : TimelineInterpolator<T>
    {
        public NoneInterpolator() : base(0) { }
        public override bool Interpolate(float time, T current, out T data)
        {
            ValueInfo<T> node;
            node.value = default;
            bool result = TryLatest(time, out node);
            data = node.value;
            return result;
        }
    }

    public class Vector3LinearInterpolator : TimelineInterpolator<Vector3>
    {

        public enum InterpolateType
        {
            LINEAR = 0,
            ACCELERATED = 1,
        }

        private float timestep;
        private float timestamp;

        public float epsilon { get; set; } = 0.001f;
        public float correctionSpeed { get; set; } = 5.0f;

        public InterpolateType Mode { get; set; } = InterpolateType.LINEAR;

        public Vector3LinearInterpolator(float timestep = 0.0f) : base(3)
        {
            this.timestep = timestep;
            this.timestamp = 0.0f;
        }

        /*private Vector3 Smooth(Vector3 position, Vector3 propagation, Vector3 velocity)
        {
            Vector3 maxCorrection = velocity * Time.deltaTime * SmoothFactor;
            Vector3 correction = propagation - position;
            if (correction.sqrMagnitude > maxCorrection.sqrMagnitude)
            {
                correction = correction.normalized * maxCorrection.magnitude;
                return position + correction;
            }
            else
                return propagation;

        }*/

        private bool IsDifferentVectors(Vector3 vec1, Vector3 vec2)
        {
            return Mathf.Abs(vec1.x - vec2.x) > epsilon ||
                   Mathf.Abs(vec1.y - vec2.y) > epsilon ||
                   Mathf.Abs(vec1.z - vec2.z) > epsilon;
        }

        private Vector3 Smooth(Vector3 position, Vector3 propagation, float dt)
        {
            Vector3 deltaCorrection = propagation - position;
            if (IsDifferentVectors(deltaCorrection, Vector3.zero))
            {
                Vector3 correction = deltaCorrection * correctionSpeed * dt;
                if (correction.sqrMagnitude > deltaCorrection.sqrMagnitude)
                    correction = deltaCorrection;
                return position + correction;
            }
            else
                return propagation;
        }

        private Vector3 InterpolateLinerMotion(Node a, Node b, float t)
        {
            float dt1 = b.time - a.time;
            float dt = t - a.time;
            return Vector3.LerpUnclamped(a.data.value, b.data.value, dt / dt1);
        }

        private Vector3 InterpolateAcceleratedMotion(Node a, Node b, Node c, float t)
        {
            Vector3 r1 = b.data.value - a.data.value;
            Vector3 r2 = c.data.value - b.data.value;
            float dt1 = b.time - a.time;
            float dt2 = c.time - b.time;
            Vector3 accelerate = ((r2 / dt2) - (r1 / dt1)) / ((dt1 + dt2) / 2);
            Vector3 v1 = (r1 / dt1) - (accelerate * dt1 / 2);
            float dt = t - a.time;
            return a.data.value + (v1 * dt) + (accelerate * (dt * dt / 2));
        }

        public override bool Interpolate(float time, Vector3 current, out Vector3 data)
        {
            data = default;
            bool result = false;
            float dt = time - timestamp;
            timestamp = time;
            if (buffer.Count != 0)
            {
                LinkedListNode<Node> next_node;
                next_node = Context != null ? Context.Next : buffer.First;
                while (true)
                {
                    if (next_node == null)
                    {
                        if (Context != null) //Extrapolation
                        {
                            if (OldNodesNum == 1 && timestep != 0.0f)
                            {
                                Node point = new Node();
                                point.data.value = result ? data : current;
                                point.time = Context.Value.time - timestep;
                                point.data.isCommit = false;
                                buffer.AddBefore(Context, point);
                                OldNodesNum += 1;
                            }
                            Vector3 propagation;
                            if (Mode == InterpolateType.ACCELERATED && OldNodesNum >= 3)
                                propagation = InterpolateAcceleratedMotion(Context.Previous.Previous.Value, Context.Previous.Value, Context.Value, time);
                            else if (OldNodesNum >= 2)
                                propagation = InterpolateLinerMotion(Context.Previous.Value, Context.Value, time);
                            else
                                break;

                            //Vector3 velocity = (Context.Value.data.value - Context.Previous.Value.data.value) / (Context.Value.time - Context.Previous.Value.time);
                            data = Smooth(current, propagation, dt);
                            return true;
                        }
                        else
                            break;
                    }
                    else
                    {
                        if (time >= next_node.Value.time)
                        {
                            if (next_node.Value.data.isCommit)
                            {
                                data = next_node.Value.data.value;
                                result = true;
                                Context = null;
                                MinTimeLimit = next_node.Value.time;
                                OldNodesNum = 0;
                                buffer.RemoveAllBefore(next_node);
                                buffer.RemoveFirst();
                                next_node = buffer.First;
                            }
                            else
                            {
                                Context = next_node;
                                next_node = next_node.Next;
                                if (OldNodesNum < MaxOldNodes)
                                    OldNodesNum += 1;
                                else
                                    buffer.RemoveFirst();
                            }
                        }
                        else
                        {
                            if (Context != null) //Interpolation
                            {
                                Vector3 propagation;
                                if (Mode == InterpolateType.ACCELERATED && OldNodesNum >= 2)
                                    propagation = InterpolateAcceleratedMotion(Context.Previous.Value, Context.Value, Context.Next.Value, time);
                                else
                                    propagation = InterpolateLinerMotion(Context.Value, next_node.Value, time);

                                //Vector3 velocity = (next_node.Value.data.value - Context.Value.data.value) / (next_node.Value.time - Context.Value.time);
                                data = Smooth(current, propagation, dt);
                                return true;
                            }
                            else
                                break;
                        }
                    }
                }
            }
            return result;
        }
    }

    public class FloatLinearInterpolator : TimelineInterpolator<float>
    {

        public enum InterpolateType
        {
            LINEAR = 0,
            ACCELERATED = 1,
        }

        private float timestep;
        private float timestamp;

        public float epsilon { get; set; } = 0.001f;
        public float correctionSpeed { get; set; } = 10.0f;

        public InterpolateType Mode { get; set; } = InterpolateType.LINEAR;

        public FloatLinearInterpolator(float timestep = 0.0f) : base(3)
        {
            this.timestep = timestep;
            this.timestamp = 0.0f;
        }

        /*private float Smooth(float position, float propagation, float velocity)
        {
            float maxCorrection = velocity * Time.deltaTime * SmoothFactor;
            float correction = propagation - position;
            if (Mathf.Abs(correction) > Mathf.Abs(maxCorrection))
            {
                correction = correction / Mathf.Abs(correction) * Mathf.Abs(maxCorrection);
                return position + correction;
            }
            else
                return propagation;

        }*/

        private bool IsDifferentFloats(float value1, float value2)
        {
            return Mathf.Abs(value1 - value2) > epsilon;
        }

        private float Smooth(float position, float propagation, float dt)
        {
            float deltaCorrection = propagation - position;
            if (IsDifferentFloats(deltaCorrection, 0))
            {
                float correction = deltaCorrection * correctionSpeed * dt;
                if (Mathf.Abs(correction) > Mathf.Abs(deltaCorrection))
                    correction = deltaCorrection;
                return position + correction;
            }
            else
                return propagation;
        }

        private float InterpolateLinerMotion(Node a, Node b, float t)
        {
            float dt1 = b.time - a.time;
            float dt = t - a.time;
            float result = a.data.value + (b.data.value - a.data.value) * (dt / dt1);
            return result;
        }

        private float InterpolateAcceleratedMotion(Node a, Node b, Node c, float t)
        {
            float r1 = b.data.value - a.data.value;
            float r2 = c.data.value - b.data.value;
            float dt1 = b.time - a.time;
            float dt2 = c.time - b.time;
            float accelerate = ((r2 / dt2) - (r1 / dt1)) / ((dt1 + dt2) / 2);
            float v1 = (r1 / dt1) - (accelerate * dt1 / 2);
            float dt = t - a.time;
            return a.data.value + (v1 * dt) + (accelerate * (dt * dt / 2));
        }

        public override bool Interpolate(float time, float current, out float data)
        {
            data = default;
            bool result = false;
            float dt = time - timestamp;
            timestamp = time;
            if (buffer.Count != 0)
            {
                LinkedListNode<Node> next_node;
                next_node = Context != null ? Context.Next : buffer.First;
                while (true)
                {
                    if (next_node == null)
                    {
                        if (Context != null) //Extrapolation
                        {
                            if (OldNodesNum == 1 && timestep != 0.0f)
                            {
                                Node point = new Node();
                                point.data.value = result ? data : current;
                                point.time = Context.Value.time - timestep;
                                point.data.isCommit = false;
                                buffer.AddBefore(Context, point);
                                OldNodesNum += 1;
                            }
                            float propagation;
                            if (Mode == InterpolateType.ACCELERATED && OldNodesNum >= 3)
                                propagation = InterpolateAcceleratedMotion(Context.Previous.Previous.Value, Context.Previous.Value, Context.Value, time);
                            else if (OldNodesNum >= 2)
                                propagation = InterpolateLinerMotion(Context.Previous.Value, Context.Value, time);
                            else
                                break;

                            //float velocity = (Context.Value.data.value - Context.Previous.Value.data.value) / (Context.Value.time - Context.Previous.Value.time);
                            data = Smooth(current, propagation, dt);
                            return true;
                        }
                        else
                            break;
                    }
                    else
                    {
                        if (time >= next_node.Value.time)
                        {
                            if (next_node.Value.data.isCommit)
                            {
                                data = next_node.Value.data.value;
                                result = true;
                                Context = null;
                                MinTimeLimit = next_node.Value.time;
                                OldNodesNum = 0;
                                buffer.RemoveAllBefore(next_node);
                                buffer.RemoveFirst();
                                next_node = buffer.First;
                            }
                            else
                            {
                                Context = next_node;
                                next_node = next_node.Next;
                                if (OldNodesNum < MaxOldNodes)
                                    OldNodesNum += 1;
                                else
                                    buffer.RemoveFirst();
                            }
                        }
                        else
                        {
                            if (Context != null) //Interpolation
                            {
                                float propagation;
                                if (Mode == InterpolateType.ACCELERATED && OldNodesNum >= 2)
                                    propagation = InterpolateAcceleratedMotion(Context.Previous.Value, Context.Value, Context.Next.Value, time);
                                else
                                    propagation = InterpolateLinerMotion(Context.Value, next_node.Value, time);

                                //float velocity = (next_node.Value.data.value - Context.Value.data.value) / (next_node.Value.time - Context.Value.time);
                                data = Smooth(current, propagation, dt);
                                return true;
                            }
                            else
                                break;
                        }
                    }
                }
            }
            return result;
        }
    }

    public class QuaternionLinearInterpolator : TimelineInterpolator<Quaternion>
    {
        private float timestep;
        private float timestamp;
        public float correctionSpeed { get; set; } = 5.0f;
        public QuaternionLinearInterpolator(float timestep = 0.0f) : base(2)
        {
            this.timestep = timestep;
            this.timestamp = 0.0f;
        }
        private Quaternion Smooth(Quaternion rotation, Quaternion propagation, float dt)
        {
            return Quaternion.Slerp(rotation, propagation, correctionSpeed * dt);
        }

        private Quaternion InterpolateLinerRotation(Node a, Node b, float t)
        {
            float dt1 = b.time - a.time;
            float dt = t - a.time;
            return Quaternion.SlerpUnclamped(a.data.value, b.data.value, dt / dt1);
        }

        public override bool Interpolate(float time, Quaternion current, out Quaternion data)
        {
            data = default;
            bool result = false;
            float dt = time - timestamp;
            timestamp = time;
            if (buffer.Count != 0)
            {
                LinkedListNode<Node> next_node;
                next_node = Context != null ? Context.Next : buffer.First;
                while (true)
                {
                    if (next_node == null)
                    {
                        if (Context != null) //Extrapolation
                        {
                            if (OldNodesNum == 1 && timestep != 0.0f)
                            {
                                Node point = new Node();
                                point.data.value = result ? data : current;
                                point.time = Context.Value.time - timestep;
                                point.data.isCommit = false;
                                buffer.AddBefore(Context, point);
                                OldNodesNum += 1;
                            }

                            Quaternion propagation;
                            if (OldNodesNum >= 2)
                                propagation = InterpolateLinerRotation(Context.Previous.Value, Context.Value, time);
                            else
                                break;
                            data = Smooth(current, propagation, dt);
                            return true;
                        }
                        else
                            break;
                    }
                    else
                    {
                        if (time >= next_node.Value.time)
                        {
                            if (next_node.Value.data.isCommit)
                            {
                                data = next_node.Value.data.value;
                                result = true;
                                Context = null;
                                MinTimeLimit = next_node.Value.time;
                                OldNodesNum = 0;
                                buffer.RemoveAllBefore(next_node);
                                buffer.RemoveFirst();
                                next_node = buffer.First;
                            }
                            else
                            {
                                Context = next_node;
                                next_node = next_node.Next;
                                if (OldNodesNum < MaxOldNodes)
                                    OldNodesNum += 1;
                                else
                                    buffer.RemoveFirst();
                            }
                        }
                        else
                        {
                            if (Context != null) //Interpolation
                            {
                                Quaternion propagation;
                                propagation = InterpolateLinerRotation(Context.Value, next_node.Value, time);
                                data = Smooth(current, propagation, dt);
                                return true;
                            }
                            else
                                break;
                        }
                    }
                }
            }
            return result;
        }
    }
}
