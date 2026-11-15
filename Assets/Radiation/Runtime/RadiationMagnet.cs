using System.Collections.Generic;
using UnityEngine;

namespace RadiationSimulation
{
    public enum RadiationMagnetPolarity { Attract, Repel }

    /// <summary>Magnetic field that bends radiation-particle trajectories without spawning objects.</summary>
    public sealed class RadiationMagnet : MonoBehaviour
    {
        private static readonly List<RadiationMagnet> ActiveMagnets = new();

        [Header("Field")]
        [SerializeField, Min(0.01f)] private float radius = 3f;
        [SerializeField, Min(0f)] private float strength = 8f;
        [SerializeField] private RadiationMagnetPolarity polarity = RadiationMagnetPolarity.Attract;
        [SerializeField, Range(0.1f, 4f)] private float falloff = 2f;
        [SerializeField, Min(0f)] private float maxAcceleration = 100f;
        [SerializeField, Min(0f)] private float maxParticleSpeed = 50f;
        [SerializeField] private bool affectParticlesOutsideRadius;

        public float Radius => radius;
        public float Strength => strength;
        public RadiationMagnetPolarity Polarity => polarity;

        private void OnEnable()
        {
            if (!ActiveMagnets.Contains(this)) ActiveMagnets.Add(this);
        }

        private void OnDisable() => ActiveMagnets.Remove(this);

        public static Vector3 EvaluateAcceleration(Vector3 position, Vector3 velocity, RadiationParticle particle)
        {
            Vector3 result = Vector3.zero;
            for (int i = ActiveMagnets.Count - 1; i >= 0; i--)
            {
                var magnet = ActiveMagnets[i];
                if (magnet == null || !magnet.isActiveAndEnabled) { ActiveMagnets.RemoveAt(i); continue; }
                Vector3 offset = magnet.transform.position - position;
                float distance = offset.magnitude;
                if (!magnet.affectParticlesOutsideRadius && distance > magnet.radius) continue;
                if (distance <= 0.0001f) continue;
                float normalizedDistance = Mathf.Clamp01(distance / magnet.radius);
                float field = magnet.strength * (magnet.affectParticlesOutsideRadius ? 1f : 1f - normalizedDistance);
                field *= Mathf.Pow(Mathf.Max(0f, 1f - normalizedDistance), magnet.falloff - 1f);
                Vector3 direction = offset / distance;
                if (magnet.polarity == RadiationMagnetPolarity.Repel) direction = -direction;
                result += Vector3.ClampMagnitude(direction * field, magnet.maxAcceleration);
            }
            return Vector3.ClampMagnitude(result, 1000f);
        }

        public static float GetMaximumParticleSpeed()
        {
            float maximum = float.PositiveInfinity;
            for (int i = 0; i < ActiveMagnets.Count; i++)
            {
                if (ActiveMagnets[i] != null && ActiveMagnets[i].isActiveAndEnabled)
                    maximum = Mathf.Min(maximum, ActiveMagnets[i].maxParticleSpeed);
            }
            return maximum;
        }

        private void OnValidate()
        {
            radius = Mathf.Max(0.01f, radius);
            strength = Mathf.Max(0f, strength);
            maxAcceleration = Mathf.Max(0f, maxAcceleration);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = polarity == RadiationMagnetPolarity.Attract ? new Color(0.1f, 0.4f, 1f, 0.3f) : new Color(1f, 0.15f, 0.1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawLine(transform.position, transform.position + transform.up * 0.5f);
        }
    }
}
