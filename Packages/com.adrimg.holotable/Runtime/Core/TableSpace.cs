using HoloTable.Domain;
using UnityEngine;

namespace HoloTable.Core
{
    /// <summary>
    /// Defines the physical play surface: its plane, its "north" and which half belongs
    /// to each player. Place it on a detected AR plane / the Quest table anchor, or let it
    /// auto-orient from the first camera pose.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class TableSpace : MonoBehaviour
    {
        [Tooltip("If true, forward is aligned to the camera heading on Start so Player One sits in front of the device.")]
        [SerializeField] private bool autoOrientFromCamera = true;
        [Tooltip("Height offset of the virtual table plane relative to this transform (metres).")]
        [SerializeField] private float surfaceOffset;

        private static TableSpace _instance;

        /// <summary>Active table. Falls back to a world-aligned plane at y=0 when none exists.</summary>
        public static TableSpace Current
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<TableSpace>();
                    if (_instance == null)
                    {
                        _instance = new GameObject("[TableSpace]").AddComponent<TableSpace>();
                        Debug.LogWarning("[HoloTable] No TableSpace in the scene: using a world-aligned plane at the AR session origin. " +
                                         "Add a TableSpace and Align() it to the detected table plane for correct dice, distances and player sides.", _instance);
                    }
                }

                return _instance;
            }
        }

        public Vector3 Normal => transform.up;
        public Vector3 Forward => transform.forward;
        public Vector3 Origin => transform.position + transform.up * surfaceOffset;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[HoloTable] Multiple TableSpace components found; using the first.", this);
                return;
            }

            _instance = this;
        }

        private void Start()
        {
            Camera cam = Camera.main;
            if (!autoOrientFromCamera || cam == null) return;

            Vector3 heading = Vector3.ProjectOnPlane(cam.transform.forward, Normal);
            if (heading.sqrMagnitude > 1e-4f)
            {
                transform.rotation = Quaternion.LookRotation(heading.normalized, Normal);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Re-anchors the table (e.g. from an ARPlane or MRUK table surface).</summary>
        public void Align(Vector3 position, Vector3 normal, Vector3 forward)
        {
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, normal), normal));
        }

        /// <summary>Projects a world point onto the table plane.</summary>
        public Vector3 ProjectOnTable(Vector3 world) =>
            world - Vector3.Dot(world - Origin, Normal) * Normal;

        /// <summary>2D coordinates on the table (x = right, y = forward) in metres.</summary>
        public System.Numerics.Vector2 ToTable2D(Vector3 world)
        {
            Vector3 local = world - Origin;
            return new System.Numerics.Vector2(Vector3.Dot(local, transform.right), Vector3.Dot(local, Forward));
        }

        public float TableDistance(Vector3 a, Vector3 b) =>
            Vector3.ProjectOnPlane(b - a, Normal).magnitude;

        public float HeightAbove(Vector3 world) => Vector3.Dot(world - Origin, Normal);

        /// <summary>Yaw of a tracked card around the table normal, in degrees.</summary>
        public float YawDegrees(Transform anchor)
        {
            Vector3 cardUp = Vector3.ProjectOnPlane(anchor.forward, Normal);
            if (cardUp.sqrMagnitude < 1e-6f)
            {
                cardUp = Vector3.ProjectOnPlane(anchor.up, Normal);
            }

            return Vector3.SignedAngle(Forward, cardUp, Normal);
        }

        /// <summary>Near half (towards the device at start) = Player One, far half = Player Two.</summary>
        public PlayerSide ResolveSide(Vector3 world) =>
            Vector3.Dot(world - Origin, Forward) >= 0f ? PlayerSide.PlayerTwo : PlayerSide.PlayerOne;

        /// <summary>Point at the middle of a player's table edge (used for "face" damage in MTG).</summary>
        public Vector3 PlayerEdge(PlayerSide side, float halfDepth = 0.35f)
        {
            float sign = side == PlayerSide.PlayerTwo ? 1f : -1f;
            return Origin + Forward * (halfDepth * sign) + Normal * 0.05f;
        }
    }
}
