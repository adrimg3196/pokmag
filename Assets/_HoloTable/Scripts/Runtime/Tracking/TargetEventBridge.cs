using UnityEngine;

namespace HoloTable.Tracking
{
    /// <summary>
    /// Zero-code bridge for any SDK that exposes UnityEvents, e.g. Vuforia's
    /// <c>DefaultObserverEventHandler</c> (On Target Found / On Target Lost) or a custom
    /// Quest QR / OpenCV detector. Put it on the target GameObject and wire:
    ///   On Target Found → TargetEventBridge.OnTargetFound
    ///   On Target Lost  → TargetEventBridge.OnTargetLost
    /// </summary>
    public sealed class TargetEventBridge : MonoBehaviour
    {
        [Tooltip("Must match a reference image name in the CardCatalog. Empty = this GameObject's name.")]
        [SerializeField] private string referenceName;
        [Tooltip("Printed size in metres (63 x 88 mm for standard TCG cards).")]
        [SerializeField] private Vector2 physicalSize = new Vector2(0.063f, 0.088f);
        [Tooltip("Pose source. Empty = this transform.")]
        [SerializeField] private Transform anchor;

        private string InstanceId => $"bridge:{GetInstanceID()}";

        public void OnTargetFound()
        {
            HoloSpawnDirector director = HoloSpawnDirector.ForAdapter(this);
            if (director == null) return;

            string reference = string.IsNullOrEmpty(referenceName) ? gameObject.name : referenceName;
            director.ReportFound(InstanceId, reference, anchor != null ? anchor : transform, physicalSize);
        }

        public void OnTargetLost()
        {
            HoloSpawnDirector director = HoloSpawnDirector.Instance;
            if (director != null) director.ReportLost(InstanceId);
        }

        private void OnDisable() => OnTargetLost();
    }
}
