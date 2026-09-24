using System;
using HoloTable.Core;
using UnityEngine;

namespace HoloTable.Games.Warhammer
{
    /// <summary>
    /// Physically simulated D6. Reads the face pointing along the table normal once it
    /// settles; a die resting tilted ("cocked") is flagged so the tray re-rolls it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ARDie : MonoBehaviour
    {
        [Serializable]
        private struct Face
        {
            public Vector3 localNormal;
            public int value;

            public Face(Vector3 n, int v)
            {
                localNormal = n;
                value = v;
            }
        }

        [Tooltip("Local direction of each face. Default: +Y=1, +Z=2, +X=3, -X=4, -Z=5, -Y=6 (opposites sum 7).")]
        [SerializeField] private Face[] faces =
        {
            new Face(Vector3.up, 1), new Face(Vector3.forward, 2), new Face(Vector3.right, 3),
            new Face(Vector3.left, 4), new Face(Vector3.back, 5), new Face(Vector3.down, 6),
        };

        [SerializeField, Min(0f)] private float settleLinearSpeed = 0.02f;
        [SerializeField, Min(0f)] private float settleAngularSpeed = 0.3f;
        [SerializeField, Min(0f)] private float settleTime = 0.25f;
        [SerializeField, Min(1f)] private float maxRollTime = 6f;
        [Tooltip("Face alignment below this dot product counts as cocked.")]
        [SerializeField, Range(0.7f, 0.99f)] private float cockedThreshold = 0.93f;

        private Rigidbody _body;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private float _stillFor;
        private float _rollStarted;

        public event Action<ARDie> Settled;

        public bool IsRolling { get; private set; }
        public bool IsCocked { get; private set; }
        public int Result { get; private set; }
        public Rigidbody Body => _body;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _renderers = GetComponentsInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        /// <summary>Kinematic, following a hand / pickup point.</summary>
        public void Hold(Vector3 position, Quaternion rotation)
        {
            IsRolling = false;
            _body.isKinematic = true;
            _body.MovePosition(position);
            _body.MoveRotation(rotation);
        }

        public void Throw(Vector3 velocity, Vector3 angularVelocity)
        {
            _body.isKinematic = false;
#if UNITY_6000_0_OR_NEWER
            _body.linearVelocity = velocity;
#else
            _body.velocity = velocity;
#endif
            _body.angularVelocity = angularVelocity;
            _body.WakeUp();
            IsRolling = true;
            IsCocked = false;
            _stillFor = 0f;
            _rollStarted = Time.time;
            Highlight(Color.white, 0f);
        }

        public void Highlight(Color color, float strength)
        {
            foreach (Renderer r in _renderers)
            {
                r.GetPropertyBlock(_block);
                _block.SetColor("_EmissionColor", color * strength);
                _block.SetColor("_BaseColor", Color.Lerp(Color.white, color, strength * 0.6f));
                _block.SetColor("_Color", Color.Lerp(Color.white, color, strength * 0.6f));
                r.SetPropertyBlock(_block);
            }
        }

        private void FixedUpdate()
        {
            if (!IsRolling) return;

#if UNITY_6000_0_OR_NEWER
            float speed = _body.linearVelocity.magnitude;
#else
            float speed = _body.velocity.magnitude;
#endif
            bool still = speed < settleLinearSpeed && _body.angularVelocity.magnitude < settleAngularSpeed;
            _stillFor = still ? _stillFor + Time.fixedDeltaTime : 0f;

            if (_stillFor >= settleTime || Time.time - _rollStarted > maxRollTime || _body.IsSleeping())
            {
                IsRolling = false;
                ReadFace();
                Settled?.Invoke(this);
            }
        }

        private void ReadFace()
        {
            Vector3 up = TableSpace.Current.Normal;
            float best = -2f;
            int value = 1;
            foreach (Face f in faces)
            {
                float dot = Vector3.Dot(transform.TransformDirection(f.localNormal), up);
                if (dot > best)
                {
                    best = dot;
                    value = f.value;
                }
            }

            Result = value;
            IsCocked = best < cockedThreshold;
        }
    }
}
