using UnityEngine;
namespace RadiationSimulation
{
    [RequireComponent(typeof(Collider))]
    public sealed class AttenuatingRadiationBarrier : MonoBehaviour, IRadiationInteraction
    {
        [Tooltip("Полностью блокирует радиацию вместо ослабления.")]
        public bool opaque;
        [Min(0f)] public float density = 1f;
        [Min(0f)] public float attenuationCoefficient = 1f;
        [Tooltip("Optional thickness along the hit normal. If zero, the collider's actual entry/exit thickness is used.")]
        [Min(0f)] public float normalThickness;
        public RadiationInteractionResult ProcessRadiation(RadiationParticle particle, RaycastHit hit)
        {
            if (opaque) return RadiationInteractionResult.Blocked;
            float thickness = normalThickness > 0f ? normalThickness : EstimateThickness(particle, hit);
            float transmission = GetTransmission(thickness);
            return new RadiationInteractionResult(transmission, 1f, false, transmission <= 0.000001f);
        }

        private float EstimateThickness(RadiationParticle particle, RaycastHit hit)
        {
            Vector3 direction = particle.Velocity.sqrMagnitude > 0.000001f ? particle.Velocity.normalized : -hit.normal;
            RaycastHit[] exits = Physics.RaycastAll(hit.point + direction * 0.001f, direction,
                Mathf.Infinity, gameObject.layer >= 0 ? (1 << gameObject.layer) : ~0, QueryTriggerInteraction.Collide);
            float farthest = 0f;
            for (int i = 0; i < exits.Length; i++)
                if (exits[i].collider == hit.collider) farthest = Mathf.Max(farthest, exits[i].distance);
            return farthest > 0f ? farthest : 0f;
        }
        public float GetTransmission(float pathLength) => Mathf.Exp(-Mathf.Max(0f, density * attenuationCoefficient * pathLength));
    }
}
