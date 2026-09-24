// Minimal API surface for the HoloTable compile check. Compile-check only.
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
