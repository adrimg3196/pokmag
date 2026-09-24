using System;
using System.Collections;
using System.Collections.Generic;
using HoloTable.Core;
using HoloTable.UI;
using TMPro;
using UnityEngine;

namespace HoloTable.Games.Warhammer
{
    /// <summary>
    /// Physical AR dice. A rules module asks for N dice; they appear hovering at the
    /// pickup point, the player grabs and throws them (hand tracking / swipe adapters
    /// call <see cref="HoldAt"/> + <see cref="Throw"/>) or they are auto-thrown. Dice
    /// bounce on an invisible collider aligned with the real table, cocked dice are
    /// re-rolled, and results are highlighted green/red before being returned.
    /// </summary>
    public sealed class DiceTray : MonoBehaviour
    {
        private sealed class Request
        {
            public int Count;
            public string Prompt;
            public Func<int, bool> IsSuccess;
            public Action<IReadOnlyList<int>> OnComplete;
        }

        [Header("Dice")]
        [SerializeField] private ARDie diePrefab;
        [SerializeField, Min(0.005f)] private float dieSize = 0.016f;
        [SerializeField, Range(1, 40)] private int maxDice = 30;

        [Header("Tray")]
        [Tooltip("Where dice land. Empty = in front of Player One on the table.")]
        [SerializeField] private Transform trayCenter;
        [SerializeField, Min(0.05f)] private float trayRadius = 0.2f;
        [SerializeField, Min(0.01f)] private float wallHeight = 0.08f;
        [SerializeField, Min(0.02f)] private float pickupHeight = 0.12f;
        [SerializeField] private bool buildPhysicsSurface = true;

        [Header("Feedback")]
        [SerializeField] private Color successColor = new Color(0.3f, 1f, 0.4f);
        [SerializeField] private Color failColor = new Color(1f, 0.25f, 0.2f);
        [SerializeField, Min(0f)] private float showResultsSeconds = 2.2f;
        [Tooltip("A die landing cocked is re-rolled at most this many times, then its closest face counts.")]
        [SerializeField, Range(0, 5)] private int maxCockedRerolls = 3;

        private readonly List<ARDie> _pool = new List<ARDie>();
        private readonly List<ARDie> _active = new List<ARDie>();
        private Request _request;
        private Transform _surface;
        private int _rollSerial;
        private bool _warnedFall;

        public bool IsAwaitingThrow { get; private set; }
        public bool IsRolling { get; private set; }
        public IReadOnlyList<ARDie> ActiveDice => _active;

        public Vector3 TrayCenter
        {
            get
            {
                TableSpace table = TableSpace.Current;
                return trayCenter != null
                    ? table.ProjectOnTable(trayCenter.position)
                    : table.Origin - table.Forward * 0.25f + table.transform.right * 0.2f;
            }
        }

        public Vector3 PickupPoint => TrayCenter + TableSpace.Current.Normal * pickupHeight;

        public event Action<string> RollRequested;
        public event Action<IReadOnlyList<int>> RollCompleted;

        private void Start()
        {
            if (buildPhysicsSurface) BuildSurface();
        }

        /// <summary>Asks the player to roll <paramref name="count"/> D6. Returns false if the tray is busy.</summary>
        public bool RequestRoll(int count, string prompt, Func<int, bool> isSuccess, Action<IReadOnlyList<int>> onComplete, bool autoThrow)
        {
            if (IsAwaitingThrow || IsRolling)
            {
                Debug.LogWarning("[HoloTable] DiceTray is busy; request ignored.", this);
                return false;
            }

            ClearDice();
            if (count <= 0)
            {
                onComplete?.Invoke(Array.Empty<int>());
                return true;
            }

            _request = new Request { Count = Mathf.Min(count, maxDice), Prompt = prompt, IsSuccess = isSuccess, OnComplete = onComplete };
            _rollSerial++;
            for (int i = 0; i < _request.Count; i++)
            {
                ARDie die = GetDie();
                die.RerollCount = 0;
                _active.Add(die);
            }

            IsAwaitingThrow = true;
            HoldAt(PickupPoint, Quaternion.identity);
            DamagePopupService.ShowInfo(PickupPoint + TableSpace.Current.Normal * 0.05f, $"{prompt}\n{_request.Count}D6");
            RollRequested?.Invoke(prompt);

            if (autoThrow) StartCoroutine(AutoThrowNextFrame());
            return true;
        }

        /// <summary>Moves the held cluster (hand grab / screen drag).</summary>
        public void HoldAt(Vector3 position, Quaternion rotation)
        {
            if (!IsAwaitingThrow) return;

            int n = _active.Count;
            int perRow = Mathf.CeilToInt(Mathf.Sqrt(n));
            float spacing = dieSize * 1.3f;
            for (int i = 0; i < n; i++)
            {
                int row = i / perRow, col = i % perRow;
                Vector3 local = new Vector3((col - (perRow - 1) * 0.5f) * spacing, (i % 2) * dieSize * 0.3f, (row - (perRow - 1) * 0.5f) * spacing);
                _active[i].Hold(position + rotation * local, rotation * Quaternion.Euler(i * 37f, i * 71f, i * 13f));
            }
        }

        public void Throw(Vector3 velocity)
        {
            if (!IsAwaitingThrow) return;
            IsAwaitingThrow = false;
            IsRolling = true;

            Vector3 flatVelocity = Vector3.ClampMagnitude(velocity, 3f);
            foreach (ARDie die in _active)
            {
                die.Settled += OnDieSettled;
                Vector3 jitter = UnityEngine.Random.insideUnitSphere * 0.15f;
                die.Throw(flatVelocity + jitter, UnityEngine.Random.insideUnitSphere * 25f);
            }
        }

        /// <summary>Tosses the dice towards the tray centre with some randomness.</summary>
        public void AutoThrow()
        {
            Vector3 up = TableSpace.Current.Normal;
            Vector3 toward = (TrayCenter - PickupPoint) + UnityEngine.Random.insideUnitSphere * 0.05f;
            Throw(toward * 2f + up * 0.4f + TableSpace.Current.Forward * 0.3f);
        }

        private IEnumerator AutoThrowNextFrame()
        {
            yield return new WaitForSeconds(0.4f);
            AutoThrow();
        }

        private void OnDieSettled(ARDie die)
        {
            TableSpace table = TableSpace.Current;
            if (table.HeightAbove(die.transform.position) < -0.1f)
            {
                // Fell through / off the table: there is no surface collider where it landed.
                if (!_warnedFall)
                {
                    _warnedFall = true;
                    Debug.LogWarning("[HoloTable] A die fell below the table. Enable 'Build Physics Surface' or align the TableSpace to the real table.", this);
                }

                die.Hold(PickupPoint, Quaternion.identity);
                die.Throw(table.Normal * 0.3f, UnityEngine.Random.insideUnitSphere * 20f);
                return;
            }

            if (die.IsCocked && die.RerollCount < maxCockedRerolls)
            {
                // Cocked dice are re-rolled, as on a real table (bounded, so it can't loop forever).
                die.RerollCount++;
                DamagePopupService.ShowInfo(die.transform.position + table.Normal * 0.03f, "¡Dado montado!");
                die.Throw(table.Normal * 0.6f, UnityEngine.Random.insideUnitSphere * 20f);
                return;
            }

            foreach (ARDie d in _active)
            {
                if (d.IsRolling || (d.IsCocked && d.RerollCount < maxCockedRerolls)) return;
            }

            StartCoroutine(FinishRoll());
        }

        private IEnumerator FinishRoll()
        {
            IsRolling = false;
            int serial = _rollSerial;
            var results = new int[_active.Count];
            for (int i = 0; i < _active.Count; i++)
            {
                ARDie die = _active[i];
                die.Settled -= OnDieSettled;
                die.RefreshResult(); // the face the player sees now, after any late knock-over
                results[i] = die.Result;
                bool ok = _request?.IsSuccess == null || _request.IsSuccess(die.Result);
                die.Highlight(ok ? successColor : failColor, 1f);
            }

            Request request = _request;
            _request = null;
            RollCompleted?.Invoke(results);
            request?.OnComplete?.Invoke(results);

            yield return new WaitForSeconds(showResultsSeconds);
            // Only clear if no newer roll has started meanwhile.
            if (serial == _rollSerial && !IsAwaitingThrow && !IsRolling) ClearDice();
        }

        private ARDie GetDie()
        {
            ARDie die;
            if (_pool.Count > 0)
            {
                die = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
            }
            else
            {
                die = diePrefab != null ? Instantiate(diePrefab, transform) : CreateFallbackDie();
            }

            die.gameObject.SetActive(true);
            return die;
        }

        private void ClearDice()
        {
            foreach (ARDie die in _active)
            {
                die.Settled -= OnDieSettled;
                die.gameObject.SetActive(false);
                _pool.Add(die);
            }

            _active.Clear();
            IsAwaitingThrow = false;
            IsRolling = false;
        }

        private ARDie CreateFallbackDie()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "D6";
            cube.transform.SetParent(transform, false);
            cube.transform.localScale = Vector3.one * dieSize;
            Rigidbody body = cube.AddComponent<Rigidbody>();
            body.mass = 0.004f;

            (Vector3 normal, int value)[] faces =
            {
                (Vector3.up, 1), (Vector3.forward, 2), (Vector3.right, 3),
                (Vector3.left, 4), (Vector3.back, 5), (Vector3.down, 6),
            };
            foreach ((Vector3 normal, int value) in faces)
            {
                var face = new GameObject($"Face{value}");
                face.transform.SetParent(cube.transform, false);
                face.transform.localPosition = normal * 0.51f;
                face.transform.localRotation = Quaternion.LookRotation(-normal, Mathf.Abs(normal.y) > 0.5f ? Vector3.forward : Vector3.up);
                TextMeshPro text = face.AddComponent<TextMeshPro>();
                text.text = value.ToString();
                text.fontSize = 6f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = Color.black;
                face.transform.localScale = Vector3.one * 0.12f;
            }

            return cube.AddComponent<ARDie>();
        }

        private void BuildSurface()
        {
            TableSpace table = TableSpace.Current;
            _surface = new GameObject("[DiceSurface]").transform;
            _surface.SetParent(transform, false);
            _surface.SetPositionAndRotation(TrayCenter, Quaternion.LookRotation(table.Forward, table.Normal));

            BoxCollider floor = _surface.gameObject.AddComponent<BoxCollider>();
            floor.size = new Vector3(2f, 0.02f, 2f);
            floor.center = new Vector3(0f, -0.01f, 0f);

            const int walls = 12;
            float wallLength = 2f * Mathf.PI * trayRadius / walls * 1.1f;
            for (int i = 0; i < walls; i++)
            {
                float a = i / (float)walls * 360f;
                var wall = new GameObject($"Wall{i}");
                wall.transform.SetParent(_surface, false);
                wall.transform.localRotation = Quaternion.Euler(0f, a, 0f);
                wall.transform.localPosition = wall.transform.localRotation * new Vector3(0f, wallHeight * 0.5f, trayRadius);
                BoxCollider c = wall.AddComponent<BoxCollider>();
                c.size = new Vector3(wallLength, wallHeight, 0.01f);
            }
        }
    }
}
