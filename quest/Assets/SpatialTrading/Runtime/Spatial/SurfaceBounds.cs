using UnityEngine;

namespace SpatialTrading.Spatial
{
    /// <summary>Last-resort comfort bounds. SDK transformers own grabbing and resize gestures.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class SurfaceBounds : MonoBehaviour
    {
        [SerializeField] private Transform _head;
        [SerializeField] private float _minScale = 0.85f;
        [SerializeField] private float _maxScale = 1.35f;
        private Vector3 _lastValidPosition;
        private Quaternion _lastValidRotation;
        public void Configure(Transform head) => _head = head;
        private void OnEnable() { _lastValidPosition = transform.position; _lastValidRotation = transform.rotation; }

        private void LateUpdate()
        {
            if (_head == null) return;
            if (!Finite(transform.position) || !Finite(transform.localScale) || !Finite(transform.rotation))
            {
                transform.SetPositionAndRotation(_lastValidPosition, _lastValidRotation);
                transform.localScale = Vector3.one;
                return;
            }
            var offset = transform.position - _head.position;
            var distance = offset.magnitude;
            if (distance > 1.8f) transform.position = _head.position + offset.normalized * 1.8f;
            else if (distance < 0.5f)
                transform.position = _head.position + (distance > 0.001f ? offset.normalized : _head.forward) * 0.5f;
            var position = transform.position;
            position.y = Mathf.Clamp(position.y, _head.position.y - 0.65f, _head.position.y + 0.35f);
            transform.position = position;
            transform.localScale = Vector3.one * Mathf.Clamp(transform.localScale.x, _minScale, _maxScale);
            _lastValidPosition = transform.position;
            _lastValidRotation = transform.rotation;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
    }
}
