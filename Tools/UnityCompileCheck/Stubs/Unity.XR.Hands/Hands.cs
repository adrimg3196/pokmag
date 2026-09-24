// Minimal API surface for the HoloTable compile check. Compile-check only.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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
