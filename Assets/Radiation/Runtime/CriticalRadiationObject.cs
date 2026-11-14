using UnityEngine;

namespace RadiationSimulation
{
    /// <summary>Accumulates dose from radiation hits and turns into a local omnidirectional source.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class CriticalRadiationObject : MonoBehaviour, IRadiationInteraction
    {
        [SerializeField, Min(0.0001f)] private float criticalDose = 5f;
        [SerializeField, Min(0f)] private float dosePerIntensitySecond = 1f;
        [SerializeField] private RadiationSource activatedSource;
        [SerializeField, Min(0f)] private float activatedEmissionRate = 25f;
        [SerializeField, Min(0f)] private float activatedIntensity = 2f;
        [SerializeField] private bool destroyIncomingParticles;

        private float accumulatedDose;
        public float AccumulatedDose => accumulatedDose;
        public bool IsActivated => activatedSource != null && activatedSource.isActiveAndEnabled;

        public RadiationInteractionResult ProcessRadiation(RadiationParticle particle, RaycastHit hit)
        {
            if (!IsActivated)
            {
                accumulatedDose += particle != null ? particle.Intensity * dosePerIntensitySecond * Time.fixedDeltaTime : 0f;
                if (accumulatedDose >= criticalDose) Activate();
            }
            return new RadiationInteractionResult(1f, 1f, false, destroyIncomingParticles);
        }

        private void Activate()
        {
            if (activatedSource == null)
            {
                Debug.LogWarning($"{name}: назначьте заранее размещённый RadiationSource в поле 'Activated Source'.", this);
                return;
            }
            activatedSource.transform.SetParent(transform, false);
            activatedSource.transform.localPosition = Vector3.zero;
            activatedSource.SetEmission(activatedEmissionRate, activatedIntensity, RadiationFlowShape.Omnidirectional);
            activatedSource.gameObject.SetActive(true);
            accumulatedDose = criticalDose;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsActivated ? Color.red : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.35f);
        }
    }
}
