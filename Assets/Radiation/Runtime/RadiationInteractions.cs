using UnityEngine;

namespace RadiationSimulation
{
    public readonly struct RadiationInteractionResult
    {
        public readonly float IntensityMultiplier;
        public readonly float SpeedMultiplier;
        public readonly bool Reflected;
        public readonly bool DestroyParticle;

        public RadiationInteractionResult(float intensityMultiplier, float speedMultiplier, bool reflected, bool destroyParticle)
        {
            IntensityMultiplier = intensityMultiplier;
            SpeedMultiplier = speedMultiplier;
            Reflected = reflected;
            DestroyParticle = destroyParticle;
        }

        public static RadiationInteractionResult Blocked => new RadiationInteractionResult(0f, 0f, false, true);
    }

    public interface IRadiationInteraction
    {
        RadiationInteractionResult ProcessRadiation(RadiationParticle particle, RaycastHit hit);
    }

    /// <summary>Volume effects are evaluated against actual path length, not frame time.</summary>
    public interface IRadiationVolume
    {
        void ProcessVolume(RadiationParticle particle, float pathLength);
        float GetSpeedMultiplier(RadiationParticle particle);
    }
}
