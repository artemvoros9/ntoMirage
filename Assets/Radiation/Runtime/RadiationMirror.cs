using UnityEngine;
namespace RadiationSimulation
{
    [RequireComponent(typeof(Collider))]
    public sealed class RadiationMirror : MonoBehaviour, IRadiationInteraction
    {
        [Range(0f, 1f)] public float reflectivity = 1f;
        public RadiationInteractionResult ProcessRadiation(RadiationParticle particle, RaycastHit hit) =>
            new RadiationInteractionResult(Mathf.Clamp01(reflectivity), 1f, true, reflectivity <= 0.0001f);
    }
}
