using HoloTable.Core;
using HoloTable.Domain.Warhammer;
using HoloTable.Entities;
using HoloTable.VFX;
using UnityEngine;

namespace HoloTable.Games.Warhammer
{
    /// <summary>
    /// Red targeting laser from the shooter's weapon to the enemy, backed by a real
    /// visibility test: several rays to points spread over the target's volume are cast
    /// against obstacle layers (virtual terrain + the room mesh from AR meshing / Quest
    /// Scene API = real cover). Result → Clear, PartialCover (benefit of cover), Blocked.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class LineOfSightLaser : MonoBehaviour
    {
        private static readonly Vector3[] SampleOffsets =
        {
            new Vector3(0f, 0.5f, 0f), new Vector3(0f, 0.95f, 0f), new Vector3(0f, 0.1f, 0f),
            new Vector3(0.8f, 0.5f, 0f), new Vector3(-0.8f, 0.5f, 0f),
            new Vector3(0f, 0.5f, 0.8f), new Vector3(0f, 0.5f, -0.8f),
            new Vector3(0.6f, 0.85f, 0f), new Vector3(-0.6f, 0.85f, 0f),
        };

        [Tooltip("Layers that block sight: virtual terrain, AR mesh, Quest scene mesh…")]
        [SerializeField] private LayerMask obstacleLayers = ~0;
        [SerializeField] private Color clearColor = new Color(1f, 0.05f, 0.05f);
        [SerializeField] private Color coverColor = new Color(1f, 0.55f, 0.05f);
        [SerializeField] private Color blockedColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        [SerializeField, Min(0.0005f)] private float width = 0.0025f;
        [SerializeField] private Transform impactMarker;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private LineRenderer _line;
        private Material _ownedMaterial;

        public Visibility LastVisibility { get; private set; } = Visibility.Blocked;
        public int LastVisibleSamples { get; private set; }

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.numCapVertices = 4;
            if (_line.sharedMaterial == null)
            {
                _ownedMaterial = HoloMaterials.CreateUnlitTransparent(Color.white, 3002);
                _line.sharedMaterial = _ownedMaterial;
            }
            _line.enabled = false;
            if (impactMarker != null) impactMarker.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_ownedMaterial != null) Destroy(_ownedMaterial);
        }

        public void Hide()
        {
            _line.enabled = false;
            if (impactMarker != null) impactMarker.gameObject.SetActive(false);
        }

        /// <summary>Evaluates visibility and draws the laser. Call every frame or at ~10 Hz.</summary>
        public Visibility Trace(LivingEntityController shooter, LivingEntityController target)
        {
            if (shooter == null || target == null)
            {
                Hide();
                return Visibility.Blocked;
            }

            Vector3 origin = shooter.MuzzleWorld;
            Vector3 up = TableSpace.Current.Normal;
            Vector3 right = Vector3.Cross(up, (target.CenterWorld - origin).normalized);
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            right.Normalize();
            Vector3 depth = Vector3.Cross(right, up);

            float radius = target.WorldFootprint * 0.5f;
            float height = target.WorldHeight;
            Vector3 basePoint = target.GroundWorld + up * target.HoverHeight;

            int visible = 0;
            Vector3 firstBlock = target.CenterWorld;
            bool centreBlocked = false;

            for (int i = 0; i < SampleOffsets.Length; i++)
            {
                Vector3 o = SampleOffsets[i];
                Vector3 point = basePoint + right * (o.x * radius) + up * (o.y * height) + depth * (o.z * radius);
                if (IsClear(origin, point, shooter.transform, target.transform, out Vector3 block))
                {
                    visible++;
                }
                else if (i == 0)
                {
                    centreBlocked = true;
                    firstBlock = block;
                }
            }

            LastVisibleSamples = visible;
            LastVisibility = CoverRules.Evaluate(visible, SampleOffsets.Length);

            Vector3 end = centreBlocked ? firstBlock : target.CenterWorld;
            Color color = LastVisibility == Visibility.Clear ? clearColor
                : LastVisibility == Visibility.PartialCover ? coverColor
                : blockedColor;

            float pulse = 1f + Mathf.Sin(Time.time * 18f) * 0.25f;
            _line.enabled = true;
            _line.SetPosition(0, origin);
            _line.SetPosition(1, end);
            _line.startColor = color;
            _line.endColor = color;
            _line.widthMultiplier = width * pulse;

            if (impactMarker != null)
            {
                impactMarker.gameObject.SetActive(true);
                impactMarker.position = end;
            }

            return LastVisibility;
        }

        private bool IsClear(Vector3 from, Vector3 to, Transform shooter, Transform target, out Vector3 blockPoint)
        {
            Vector3 dir = to - from;
            float distance = dir.magnitude;
            blockPoint = to;
            if (distance < 1e-4f) return true;

            int count = Physics.RaycastNonAlloc(from, dir / distance, _hits, distance, obstacleLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool blocked = false;
            for (int i = 0; i < count; i++)
            {
                Transform t = _hits[i].transform;
                if (t.IsChildOf(shooter) || t.IsChildOf(target)) continue;
                if (_hits[i].distance < nearest)
                {
                    nearest = _hits[i].distance;
                    blockPoint = _hits[i].point;
                    blocked = true;
                }
            }

            return !blocked;
        }
    }
}
