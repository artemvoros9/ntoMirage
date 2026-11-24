using System.Collections.Generic;
using UnityEngine;

namespace RadiationSimulation
{
    public enum RadiationMagnetPolarity { Attract, Repel }

    /// <summary>Uniform-ish magnetic field whose force is the Lorentz q/m(v x B).</summary>
    public sealed class RadiationMagnet : MonoBehaviour
    {
        private static readonly List<RadiationMagnet> ActiveMagnets = new();
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
        public float Falloff => falloff;
        public float MaxAcceleration => maxAcceleration;
        public float MaxParticleSpeed => maxParticleSpeed;
        public bool AffectParticlesOutsideRadius => affectParticlesOutsideRadius;

        public void Configure(float newRadius, float newStrength, RadiationMagnetPolarity newPolarity)
        {
            radius = Mathf.Max(.01f, newRadius);
            strength = Mathf.Max(0f, newStrength);
            polarity = newPolarity;
        }

        public void ConfigureProperties(float newRadius, float newStrength, float newFalloff, RadiationMagnetPolarity newPolarity, bool outsideRadius)
        {
            radius = Mathf.Max(.01f, newRadius);
            strength = Mathf.Max(0f, newStrength);
            falloff = Mathf.Clamp(newFalloff, .1f, 4f);
            polarity = newPolarity;
            affectParticlesOutsideRadius = outsideRadius;
        }
        public void ConfigureLimits(float acceleration, float particleSpeed)
        {
            maxAcceleration = Mathf.Max(0f, acceleration);
            maxParticleSpeed = Mathf.Max(0f, particleSpeed);
        }

        private void OnEnable()
        {
            if (!ActiveMagnets.Contains(this))
            {
                ActiveMagnets.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveMagnets.Remove(this);
        }

        public static Vector3 EvaluateAcceleration(Vector3 position, Vector3 velocity, RadiationParticle particle)
        {
            Vector3 result = Vector3.zero;
            for (int i = ActiveMagnets.Count - 1; i >= 0; i--)
            {
                var m = ActiveMagnets[i];
                if (m == null || !m.isActiveAndEnabled)
                {
                    ActiveMagnets.RemoveAt(i);
                    continue;
                }
                Vector3 offset = position - m.transform.position;
                float d = offset.magnitude;
                if (!m.affectParticlesOutsideRadius && d > m.radius) continue;
                float n = Mathf.Clamp01(d / m.radius);
                float field = m.strength;
                if (!m.affectParticlesOutsideRadius) field *= Mathf.Pow(Mathf.Max(0f, 1f - n), Mathf.Max(.01f, m.falloff));
                if (d <= 0.0001f) continue;
                // This component is configured as an attractor/repulsor, so use a
                // radial force. A velocity x B force bends particles to one side
                // and cannot attract them toward the magnet center.
                Vector3 radialDirection = offset / d;
                float sign = m.polarity == RadiationMagnetPolarity.Attract ? -1f : 1f;
                result += radialDirection * (field * sign);
            }
            return result;
        }
        public static float GetMaximumParticleSpeed()
        {
            float max = float.PositiveInfinity;
            for (int i = 0; i < ActiveMagnets.Count; i++) if (ActiveMagnets[i] != null && ActiveMagnets[i].isActiveAndEnabled) max = Mathf.Min(max, ActiveMagnets[i].maxParticleSpeed);
            return max;
        }
        private void OnValidate()
        {
            radius = Mathf.Max(.01f, radius);
            strength = Mathf.Max(0f, strength);
            maxAcceleration = Mathf.Max(0f, maxAcceleration);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = polarity == RadiationMagnetPolarity.Attract
                ? new Color(.1f, .4f, 1f, .3f)
                : new Color(1f, .15f, .1f, .3f);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawLine(transform.position, transform.position + transform.up * .5f);
        }
    }
}
