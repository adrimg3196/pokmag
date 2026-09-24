// Minimal API surfaces of third-party XR SDKs used by the HoloTable adapters.
// They mirror the public signatures of AR Foundation 6, Vuforia 10+, XR Hands 1.x and
// Input System 1.x closely enough to type-check our adapter code. Compile-check only.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEngine.XR.ARSubsystems
{
    public enum TrackingState { None = 0, Limited = 1, Tracking = 2 }

    public readonly struct TrackableId : IEquatable<TrackableId>
    {
        public bool Equals(TrackableId other) => true;
        public override string ToString() => "0-0";
    }

    public readonly struct XRReferenceImage
    {
        public string name => "";
    }
}

namespace UnityEngine.XR.ARFoundation
{
    using UnityEngine.XR.ARSubsystems;

    public class ARTrackedImage : MonoBehaviour
    {
        public TrackableId trackableId => default;
        public TrackingState trackingState => default;
        public XRReferenceImage referenceImage => default;
        public Vector2 size => default;
    }

    public readonly struct ARTrackablesChangedEventArgs<T>
    {
        public IReadOnlyList<T> added => Array.Empty<T>();
        public IReadOnlyList<T> updated => Array.Empty<T>();
        public IReadOnlyList<KeyValuePair<TrackableId, T>> removed => Array.Empty<KeyValuePair<TrackableId, T>>();
    }

    public class ARTrackedImageManager : MonoBehaviour
    {
        public UnityEvent<ARTrackablesChangedEventArgs<ARTrackedImage>> trackablesChanged { get; } =
            new UnityEvent<ARTrackablesChangedEventArgs<ARTrackedImage>>();
    }
}

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

namespace UnityEngine.XR.Hands
{
    public enum Handedness { Invalid = 0, Left = 1, Right = 2 }

    public enum XRHandJointID { Invalid = 0, Wrist = 1, Palm = 2, ThumbTip = 6, IndexTip = 11 }

    public readonly struct XRHandJoint
    {
        public bool TryGetPose(out Pose pose)
        {
            pose = default;
            return false;
        }
    }

    public readonly struct XRHand
    {
        public Handedness handedness => default;
        public bool isTracked => false;
        public XRHandJoint GetJoint(XRHandJointID id) => default;
    }

    public class XRHandSubsystem : ISubsystem
    {
        public bool running => false;
        public XRHand leftHand => default;
        public XRHand rightHand => default;
        public void Start() { }
        public void Stop() { }
        public void Destroy() { }
    }
}

namespace UnityEngine.InputSystem
{
    namespace Controls
    {
        public class ButtonControl
        {
            public bool wasPressedThisFrame => false;
            public bool wasReleasedThisFrame => false;
            public bool isPressed => false;
        }

        public class KeyControl : ButtonControl { }

        public class Vector2Control
        {
            public Vector2 ReadValue() => default;
        }
    }

    public enum Key
    {
        None = 0, Space = 1, Enter = 2, Tab = 3,
        A = 15, C = 17, E = 19, F = 20, H = 22, N = 28, Q = 31, S = 33, V = 36, X = 38,
        Digit1 = 41, Digit2 = 42, Digit3 = 43, Digit4 = 44, Digit5 = 45, Digit6 = 46, Digit7 = 47, Digit8 = 48, Digit9 = 49, Digit0 = 50,
        LeftShift = 51, RightShift = 52, Escape = 60, Delete = 71,
    }

    public class Pointer
    {
        public static Pointer current => null;
        public Controls.ButtonControl press => null;
        public Controls.Vector2Control position => null;
        public Controls.Vector2Control delta => null;
    }

    public class Mouse : Pointer
    {
        public new static Mouse current => null;
        public Controls.ButtonControl leftButton => null;
        public Controls.ButtonControl rightButton => null;
        public Controls.ButtonControl middleButton => null;
        public Controls.Vector2Control scroll => null;
    }

    public class Keyboard
    {
        public static Keyboard current => null;
        public Controls.KeyControl this[Key key] => null;
    }
}
