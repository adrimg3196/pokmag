using UnityEngine;

namespace HoloTable.Tracking
{
    public enum TrackingStatus
    {
        Tracked = 0,
        Limited = 1,
        Lost = 2,
    }

    /// <summary>
    /// SDK-agnostic view of one physical card / miniature seen by the camera.
    /// Created by a tracking adapter (AR Foundation, Vuforia, Quest QR/MRUK, custom CV…).
    /// </summary>
    public sealed class TrackedTarget
    {
        /// <summary>Default short side of a standard TCG card (63 mm).</summary>
        public const float DefaultCardShortSide = 0.063f;

        public TrackedTarget(string instanceId, string referenceName, Transform anchor, Vector2 physicalSize)
        {
            InstanceId = instanceId;
            ReferenceName = referenceName;
            Anchor = anchor;
            PhysicalSize = physicalSize;
            Status = TrackingStatus.Tracked;
            FirstSeenTime = Time.time;
            LastSeenTime = Time.time;
        }

        /// <summary>Unique per physical instance (trackableId, observer id…).</summary>
        public string InstanceId { get; }

        /// <summary>Name of the reference image in the image library / Vuforia database.</summary>
        public string ReferenceName { get; }

        /// <summary>Pose provided by the SDK. Y = card normal, Z = card "up" edge.</summary>
        public Transform Anchor { get; private set; }

        /// <summary>Printed size in metres (width, height).</summary>
        public Vector2 PhysicalSize { get; private set; }

        public TrackingStatus Status { get; private set; }
        public float FirstSeenTime { get; }
        public float LastSeenTime { get; private set; }
        public float LostTime { get; private set; } = -1f;

        public bool IsTracked => Status == TrackingStatus.Tracked;
        public bool HasAnchor => Anchor != null;
        public Vector3 Position => Anchor != null ? Anchor.position : Vector3.zero;

        public float ShortSide
        {
            get
            {
                float s = Mathf.Min(PhysicalSize.x, PhysicalSize.y);
                return s > 0.001f ? s : DefaultCardShortSide;
            }
        }

        internal void MarkSeen(Transform anchor, Vector2 physicalSize)
        {
            if (anchor != null) Anchor = anchor;
            if (physicalSize.sqrMagnitude > 0f) PhysicalSize = physicalSize;
            Status = TrackingStatus.Tracked;
            LastSeenTime = Time.time;
            LostTime = -1f;
        }

        internal void MarkLimited() => Status = TrackingStatus.Limited;

        internal void MarkLost()
        {
            Status = TrackingStatus.Lost;
            LostTime = Time.time;
        }
    }
}
