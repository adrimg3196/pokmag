// Minimal API surface for the HoloTable compile check. Compile-check only.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Vuforia
{
    public enum Status { NO_POSE, LIMITED, TRACKED, EXTENDED_TRACKED }

    public readonly struct TargetStatus
    {
        public Status Status => default;
    }

    public class ObserverBehaviour : MonoBehaviour
    {
        public string TargetName => "";
        public event Action<ObserverBehaviour, TargetStatus> OnTargetStatusChanged;
        protected void Raise(TargetStatus s) => OnTargetStatusChanged?.Invoke(this, s);
    }

    public class ImageTargetBehaviour : ObserverBehaviour
    {
        public Vector2 GetSize() => default;
    }
}
