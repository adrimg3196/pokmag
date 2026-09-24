using System.Collections.Generic;
using HoloTable.Games.Warhammer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HoloTable.Adapters
{
    /// <summary>
    /// Phone / tablet AR fallback for the dice: touch and drag to hold the dice in front
    /// of the camera, flick to throw. Works with mouse in the Editor too.
    /// </summary>
    public sealed class ScreenSwipeDiceThrower : MonoBehaviour
    {
        [SerializeField] private DiceTray tray;
        [SerializeField] private Camera arCamera;
        [SerializeField, Min(0.1f)] private float holdDistance = 0.35f;
        [SerializeField, Min(0.1f)] private float flickMultiplier = 1.4f;
        [SerializeField, Min(0f)] private float forwardBoost = 0.6f;

        private readonly Queue<(float time, Vector3 position)> _history = new Queue<(float, Vector3)>();
        private bool _dragging;

        private void Update()
        {
            if (tray == null || !tray.IsAwaitingThrow) return;

            Pointer pointer = Pointer.current;
            Camera cam = arCamera != null ? arCamera : Camera.main;
            if (pointer == null || cam == null) return;

            Vector2 screen = pointer.position.ReadValue();
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, holdDistance));

            if (pointer.press.wasPressedThisFrame)
            {
                _dragging = true;
                _history.Clear();
            }

            if (!_dragging) return;

            tray.HoldAt(world, cam.transform.rotation);
            _history.Enqueue((Time.time, world));
            while (_history.Count > 0 && Time.time - _history.Peek().time > 0.1f) _history.Dequeue();

            if (!pointer.press.wasReleasedThisFrame) return;

            _dragging = false;
            (float time, Vector3 position) first = _history.Peek();
            float dt = Mathf.Max(0.001f, Time.time - first.time);
            Vector3 velocity = (world - first.position) / dt * flickMultiplier + cam.transform.forward * forwardBoost;
            tray.Throw(velocity);
        }
    }
}
