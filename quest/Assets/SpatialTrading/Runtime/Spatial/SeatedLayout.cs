using UnityEngine;
using UnityEngine.XR;

namespace SpatialTrading.Spatial
{
    /// <summary>Places a world-stable workspace once. Head pose is never an eye-gaze input.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class SeatedLayout : MonoBehaviour
    {
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _chart;
        [SerializeField] private Transform _controls;
        [SerializeField] private Transform _secondary;
        private bool _placed;
        private XRInputSubsystem _input;
        private int _readyAfterFrame;

        public void Configure(Transform head, Transform chart, Transform controls, Transform secondary)
        { _head = head; _chart = chart; _controls = controls; _secondary = secondary; }

        private void LateUpdate()
        {
            var input = OVRManager.GetCurrentInputSubsystem();
            if (input != _input)
            {
                if (_input != null) _input.trackingOriginUpdated -= OnTrackingOriginUpdated;
                _input = input;
                if (_input != null) _input.trackingOriginUpdated += OnTrackingOriginUpdated;
                OnTrackingOriginUpdated(_input);
            }
            // OpenXR starts in device space before floor space becomes available. HMD
            // presence alone is not a fresh pose. Wait for floor origin and a tracked
            // pose already applied to the rig, including after origin changes.
            if (_placed || _head == null || _input == null || !_input.running || Time.frameCount < _readyAfterFrame ||
                _input.GetTrackingOriginMode() != TrackingOriginModeFlags.Floor) return;
            var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) || !tracked ||
                !device.TryGetFeatureValue(CommonUsages.devicePosition, out var position) ||
                Vector3.Distance(_head.localPosition, position) > 0.001f) return;
            Recenter();
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem input)
        { _placed = false; _readyAfterFrame = Time.frameCount + 2; }

        private void OnDestroy()
        { if (_input != null) _input.trackingOriginUpdated -= OnTrackingOriginUpdated; }

        public void Recenter()
        {
            if (_head == null) return;
            var forward = Vector3.ProjectOnPlane(_head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) return;
            var rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            Place(_chart, new Vector3(0, -0.10f, 0.95f), rotation);
            Place(_controls, new Vector3(0, -0.38f, 0.57f), rotation * Quaternion.Euler(25, 0, 0));
            Place(_secondary, new Vector3(0.61f, -0.16f, 0.78f), rotation * Quaternion.Euler(0, 25, 0));
            _placed = true;
        }

        private void Place(Transform surface, Vector3 localOffset, Quaternion rotation)
        {
            var facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized);
            surface.SetPositionAndRotation(_head.position + facing * localOffset, rotation);
            surface.localScale = Vector3.one;
        }
    }
}
