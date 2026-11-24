using UnityEngine;

namespace RadiationSimulation
{
    /// <summary>Accumulated dose receiver. Dose is added only by particle intersections.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class RadiationDosimeter : MonoBehaviour, IRadiationInteraction
    {
        [SerializeField, Min(0.0001f)] private float doseToRed = 10f;
        [SerializeField] private bool selectable = true;
        private float dose;
        public float Dose => dose;
        public float DoseToRed => doseToRed;
        public bool Selectable => selectable;
        public void Configure(float newDoseToRed, bool newSelectable = true)
        {
            doseToRed = Mathf.Max(.0001f, newDoseToRed);
            selectable = newSelectable;
        }

        public void ResetDose()
        {
            dose = 0f;
        }
        public void RegisterIntersection(RadiationParticle particle, float pathLength)
        {
            if (particle == null || pathLength <= 0f)
            {
                return;
            }
            float speed = Mathf.Max(.001f, particle.Velocity.magnitude);
            dose += Mathf.Max(0f, particle.Intensity) * pathLength / speed;
        }
        public RadiationInteractionResult ProcessRadiation(RadiationParticle particle, RaycastHit hit)
        {
            RegisterIntersection(particle, Mathf.Max(.001f, particle != null ? particle.Velocity.magnitude * Time.fixedDeltaTime : 0f));
            return new RadiationInteractionResult(1f, 1f, false, false);
        }
        private void OnValidate()
        {
            doseToRed = Mathf.Max(.0001f, doseToRed);
        }
    }
}
