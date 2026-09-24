// Minimal API surface for the HoloTable compile check. Compile-check only.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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
