using UnityEngine;

namespace RadiationSimulation
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class RadiationSpeedReducer : MonoBehaviour, IRadiationInteraction, IRadiationVolume
    {
        [Min(0f)]
        public float speedMultiplier = 0.35f;

        [Min(0f)]
        public float intensityLossPerUnit;

        public RadiationInteractionResult ProcessRadiation(RadiationParticle particle, RaycastHit hit)
        {
            return new RadiationInteractionResult(1f, 1f, false, false);
        }

        public void ProcessVolume(RadiationParticle particle, float pathLength)
        {
            particle.ApplyVolumeEffect(
                Mathf.Exp(-Mathf.Max(0f, intensityLossPerUnit) * Mathf.Max(0f, pathLength)),
                1f);
        }

        public float GetSpeedMultiplier(RadiationParticle particle)
        {
            return Mathf.Max(0f, speedMultiplier);
        }
    }
}
