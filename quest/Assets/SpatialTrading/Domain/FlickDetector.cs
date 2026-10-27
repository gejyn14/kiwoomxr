using System;
using System.Collections.Generic;

namespace SpatialTrading.Domain
{
    /// <summary>
    /// Decides whether releasing a grabbed panel was a deliberate flick (dismiss gesture).
    /// Horizontal = x and z; y is vertical and ignored. Only samples within the last
    /// <see cref="WindowSeconds"/> before the release time count.
    /// </summary>
    public sealed class FlickDetector
    {
        public const float WindowSeconds = 0.15f;
        public const float MinSpeed = 1.2f;     // m/s
        public const float MinDistance = 0.15f; // m
        const float Eps = 1e-4f;

        readonly List<(float t, float x, float z)> _samples = new List<(float, float, float)>();

        public void Begin(float t, float x, float y, float z)
        {
            _samples.Clear();
            _samples.Add((t, x, z));
        }

        public void Sample(float t, float x, float y, float z) => _samples.Add((t, x, z));

        public bool ReleaseIsFlick(float t)
        {
            bool found = false;
            (float t, float x, float z) first = default, last = default;
            foreach (var s in _samples)
            {
                if (s.t < t - WindowSeconds - Eps || s.t > t + Eps) continue;
                if (!found) { first = s; found = true; }
                last = s;
            }
            if (!found) return false;
            float dt = last.t - first.t;
            if (dt <= 0f) return false;
            float dx = last.x - first.x, dz = last.z - first.z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            return dist >= MinDistance - Eps && dist / dt >= MinSpeed - Eps;
        }
    }
}
