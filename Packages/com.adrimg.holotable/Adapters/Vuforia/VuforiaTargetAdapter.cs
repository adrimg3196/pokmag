using HoloTable.Tracking;
using UnityEngine;
using Vuforia;

namespace HoloTable.Adapters
{
    /// <summary>
    /// Vuforia Engine (Image / Model / Multi targets) → HoloSpawnDirector, code-only
    /// alternative to wiring DefaultObserverEventHandler UnityEvents by hand.
    /// Add to every ImageTarget / ModelTarget GameObject (or to the prefab you instantiate
    /// from the database). Holograms must NOT be children of the target: the director owns them.
    ///
    /// Status mapping: TRACKED → Found · LIMITED → Limited (freeze) ·
    /// EXTENDED_TRACKED → Lost by default (card removed or covered; for miniatures on
    /// a static table you may prefer to keep it) · NO_POSE → Lost.
    /// </summary>
    [RequireComponent(typeof(ObserverBehaviour))]
    public sealed class VuforiaTargetAdapter : MonoBehaviour
    {
        [Tooltip("Keep holograms alive while Vuforia only extrapolates the pose (Device Tracker).")]
        [SerializeField] private bool extendedTrackingCountsAsFound;
        [Tooltip("Empty = the Vuforia target name from the database.")]
        [SerializeField] private string referenceNameOverride;
        [Tooltip("Used for Model Targets or when the image size is unknown (metres).")]
        [SerializeField] private Vector2 fallbackSize = new Vector2(0.063f, 0.088f);

        private ObserverBehaviour _observer;

        private string InstanceId => $"vuforia:{_observer.GetInstanceID()}";

        private void Awake()
        {
            _observer = GetComponent<ObserverBehaviour>();
            _observer.OnTargetStatusChanged += OnTargetStatusChanged;
        }

        private void OnDisable()
        {
            // Target disabled/destroyed: Vuforia sends no more statuses, so release the hologram.
            HoloSpawnDirector director = HoloSpawnDirector.Instance;
            if (director != null && _observer != null) director.ReportLost(InstanceId);
        }

        private void OnDestroy()
        {
            if (_observer != null) _observer.OnTargetStatusChanged -= OnTargetStatusChanged;
        }

        private void OnTargetStatusChanged(ObserverBehaviour behaviour, TargetStatus targetStatus)
        {
            HoloSpawnDirector director = HoloSpawnDirector.ForAdapter(this);
            if (director == null) return;

            switch (targetStatus.Status)
            {
                case Status.TRACKED:
                    director.ReportFound(InstanceId, ReferenceName(behaviour), behaviour.transform, SizeOf(behaviour));
                    break;
                case Status.EXTENDED_TRACKED when extendedTrackingCountsAsFound:
                    director.ReportFound(InstanceId, ReferenceName(behaviour), behaviour.transform, SizeOf(behaviour));
                    break;
                case Status.LIMITED:
                    director.ReportLimited(InstanceId);
                    break;
                default:
                    director.ReportLost(InstanceId);
                    break;
            }
        }

        private string ReferenceName(ObserverBehaviour behaviour) =>
            string.IsNullOrEmpty(referenceNameOverride) ? behaviour.TargetName : referenceNameOverride;

        private Vector2 SizeOf(ObserverBehaviour behaviour) =>
            behaviour is ImageTargetBehaviour image ? image.GetSize() : fallbackSize;
    }
}
