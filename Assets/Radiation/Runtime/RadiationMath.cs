using UnityEngine;

namespace RadiationSimulation
{
    /// <summary>Pure mathematical kernel. No scene objects, rendering, physics queries or frame callbacks.</summary>
    public static class RadiationMath
    {
        public static float InverseSquare(float intensity, float distance, float referenceDistance)
        {
            float reference = Mathf.Max(0.0001f, referenceDistance);
            float d = Mathf.Max(reference, distance);
            return Mathf.Max(0f, intensity) * reference * reference / (d * d);
        }

        public static float Transmission(float coefficient, float density, float length) =>
            Mathf.Exp(-Mathf.Max(0f, coefficient) * Mathf.Max(0f, density) * Mathf.Max(0f, length));

        public static Vector3 Reflect(Vector3 velocity, Vector3 normal) =>
            velocity - 2f * Vector3.Dot(velocity, normal) * normal;

        public static Vector3 Advance(Vector3 position, Vector3 velocity, float deltaTime) => position + velocity * deltaTime;

        public static Vector3 ConeDirection(float angleDegrees, float azimuth01, float elevation01)
        {
            float z = Mathf.Lerp(Mathf.Cos(angleDegrees * Mathf.Deg2Rad), 1f, elevation01);
            float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            float phi = azimuth01 * Mathf.PI * 2f;
            return new Vector3(radial * Mathf.Cos(phi), radial * Mathf.Sin(phi), z);
        }
    }
}
