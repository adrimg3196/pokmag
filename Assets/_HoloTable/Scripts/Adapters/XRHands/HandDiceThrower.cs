using System.Collections.Generic;
using HoloTable.Games.Warhammer;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace HoloTable.Adapters
{
    /// <summary>
    /// Hand-tracked dice (Meta Quest 3, Apple Vision Pro, HoloLens… via Unity XR Hands).
    /// Pinch near the floating dice to grab them, shake, and release while moving the
    /// hand: the release velocity (averaged over the last ~100 ms) becomes the throw.
    /// </summary>
    public sealed class HandDiceThrower : MonoBehaviour
    {
        private struct Sample
        {
            public float Time;
            public Vector3 Position;
        }

        [SerializeField] private DiceTray tray;
        [Tooltip("XR Origin transform (hand joints are reported in its tracking space).")]
        [SerializeField] private Transform trackingSpace;
        [SerializeField, Min(0.005f)] private float pinchGrabDistance = 0.025f;
        [SerializeField, Min(0.01f)] private float pinchReleaseDistance = 0.045f;
        [SerializeField, Min(0.02f)] private float grabRadius = 0.12f;
        [SerializeField, Min(0.1f)] private float throwMultiplier = 1.2f;
        [SerializeField, Min(0.5f)] private float maxThrowSpeed = 3f;
        [SerializeField, Min(0.03f)] private float velocityWindow = 0.1f;

        private static readonly List<XRHandSubsystem> Subsystems = new List<XRHandSubsystem>();

        private readonly Queue<Sample> _history = new Queue<Sample>();
        private XRHandSubsystem _hands;
        private Handedness _holding = Handedness.Invalid;

        private void Update()
        {
            if (tray == null || !EnsureSubsystem()) return;

            ProcessHand(_hands.leftHand);
            ProcessHand(_hands.rightHand);
        }

        private bool EnsureSubsystem()
        {
            if (_hands != null && _hands.running) return true;

            SubsystemManager.GetSubsystems(Subsystems);
            foreach (XRHandSubsystem s in Subsystems)
            {
                if (!s.running) continue;
                _hands = s;
                return true;
            }

            return false;
        }

        private void ProcessHand(XRHand hand)
        {
            bool isHolder = _holding == hand.handedness;

            if (!hand.isTracked)
            {
                if (isHolder) Release();
                return;
            }

            if (!TryGetWorldPose(hand, XRHandJointID.ThumbTip, out Pose thumb)
                || !TryGetWorldPose(hand, XRHandJointID.IndexTip, out Pose index)
                || !TryGetWorldPose(hand, XRHandJointID.Palm, out Pose palm))
            {
                return;
            }

            float pinch = Vector3.Distance(thumb.position, index.position);
            Vector3 pinchPoint = (thumb.position + index.position) * 0.5f;

            if (_holding == Handedness.Invalid)
            {
                if (tray.IsAwaitingThrow && pinch < pinchGrabDistance && Vector3.Distance(pinchPoint, tray.PickupPoint) < grabRadius)
                {
                    _holding = hand.handedness;
                    _history.Clear();
                }

                return;
            }

            if (!isHolder) return;

            tray.HoldAt(pinchPoint, palm.rotation);
            _history.Enqueue(new Sample { Time = Time.time, Position = pinchPoint });
            while (_history.Count > 0 && Time.time - _history.Peek().Time > velocityWindow) _history.Dequeue();

            if (pinch > pinchReleaseDistance) Release();
        }

        private void Release()
        {
            Vector3 velocity = Vector3.zero;
            if (_history.Count >= 2)
            {
                Sample first = _history.Peek();
                Sample last = default;
                foreach (Sample s in _history) last = s;
                float dt = Mathf.Max(0.001f, last.Time - first.Time);
                velocity = (last.Position - first.Position) / dt;
            }

            _holding = Handedness.Invalid;
            _history.Clear();
            tray.Throw(Vector3.ClampMagnitude(velocity * throwMultiplier, maxThrowSpeed));
        }

        private bool TryGetWorldPose(XRHand hand, XRHandJointID id, out Pose pose)
        {
            if (!hand.GetJoint(id).TryGetPose(out pose)) return false;
            if (trackingSpace != null)
            {
                pose = new Pose(trackingSpace.TransformPoint(pose.position), trackingSpace.rotation * pose.rotation);
            }

            return true;
        }
    }
}
