using HoloTable.Tracking;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace HoloTable.Adapters
{
    /// <summary>
    /// AR Foundation (ARKit / ARCore / visionOS / OpenXR image tracking) → HoloSpawnDirector.
    /// Add next to the ARTrackedImageManager on the XR Origin. No tracked-image prefab is
    /// needed: holograms are spawned by the director and follow the ARTrackedImage pose.
    ///
    /// Tracking states: Tracking → Found, Limited → Lost (ARKit/ARCore report Limited when
    /// the image leaves the camera or is covered), None → Lost. Removal → Lost.
    /// </summary>
    [RequireComponent(typeof(ARTrackedImageManager))]
    public sealed class ARFoundationTrackingAdapter : MonoBehaviour
    {
        [Tooltip("On most devices Limited means 'not visible'. Disable to freeze holograms instead of dissolving them.")]
        [SerializeField] private bool treatLimitedAsLost = true;

        private readonly System.Collections.Generic.HashSet<string> _reported = new System.Collections.Generic.HashSet<string>();
        private ARTrackedImageManager _manager;

        private void Awake() => _manager = GetComponent<ARTrackedImageManager>();

        private void OnEnable()
        {
#if HOLO_ARF6
            _manager.trackablesChanged.AddListener(OnTrackablesChanged);
#else
            _manager.trackedImagesChanged += OnTrackedImagesChanged;
#endif
        }

        private void OnDisable()
        {
#if HOLO_ARF6
            _manager.trackablesChanged.RemoveListener(OnTrackablesChanged);
#else
            _manager.trackedImagesChanged -= OnTrackedImagesChanged;
#endif
            // No more updates will arrive: release every card we reported, or its hologram freezes forever.
            HoloSpawnDirector director = HoloSpawnDirector.Instance;
            if (director != null)
            {
                foreach (string id in _reported) director.ReportLost(id);
            }

            _reported.Clear();
        }

#if HOLO_ARF6
        private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
            foreach (ARTrackedImage image in args.added) Process(image);
            foreach (ARTrackedImage image in args.updated) Process(image);
            foreach (var removed in args.removed) Lost(removed.Key);
        }
#else
        private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
        {
            foreach (ARTrackedImage image in args.added) Process(image);
            foreach (ARTrackedImage image in args.updated) Process(image);
            foreach (ARTrackedImage image in args.removed) Lost(image.trackableId);
        }
#endif

        private void Process(ARTrackedImage image)
        {
            HoloSpawnDirector director = HoloSpawnDirector.ForAdapter(this);
            if (director == null) return;

            string id = image.trackableId.ToString();
            switch (image.trackingState)
            {
                case TrackingState.Tracking:
                    _reported.Add(id);
                    director.ReportFound(id, image.referenceImage.name, image.transform, image.size);
                    break;
                case TrackingState.Limited when !treatLimitedAsLost:
                    director.ReportLimited(id);
                    break;
                default:
                    director.ReportLost(id);
                    break;
            }
        }

        private void Lost(TrackableId id)
        {
            string key = id.ToString();
            _reported.Remove(key);
            HoloSpawnDirector director = HoloSpawnDirector.Instance;
            if (director != null) director.ReportLost(key);
        }
    }
}
