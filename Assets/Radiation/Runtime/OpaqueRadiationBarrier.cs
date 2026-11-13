using UnityEngine;
namespace RadiationSimulation
{
    [RequireComponent(typeof(Collider))]
    public sealed class OpaqueRadiationBarrier : MonoBehaviour, IRadiationInteraction
    {
        public RadiationInteractionResult ProcessRadiation(RadiationParticle particle, RaycastHit hit) => RadiationInteractionResult.Blocked;
    }
}
