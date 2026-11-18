using UnityEngine;

namespace RadiationSimulation
{
    [RequireComponent(typeof(Collider))]
    public sealed class CriticalRadiationObject : MonoBehaviour, IRadiationInteraction
    {
        [SerializeField, Min(.0001f)] private float criticalDose=5f;
        [SerializeField, Min(0f)] private float dosePerIntensitySecond=1f;
        [SerializeField] private RadiationSource activatedSource;
        [SerializeField, Min(0f)] private float activatedEmissionRate=25f;
        [SerializeField, Min(0f)] private float activatedIntensity=2f;
        [SerializeField] private bool destroyIncomingParticles;
        private float accumulatedDose; private Collider ownCollider;
        public float AccumulatedDose=>accumulatedDose; public float CriticalDose=>criticalDose; public bool IsActivated=>activatedSource!=null&&activatedSource.isActiveAndEnabled;
        public void Configure(RadiationSource source,float threshold,float rate,float intensity)
        { activatedSource=source; criticalDose=Mathf.Max(.0001f,threshold); activatedEmissionRate=Mathf.Max(0,rate); activatedIntensity=Mathf.Max(0,intensity); if(activatedSource!=null)activatedSource.gameObject.SetActive(false); }
        private void Awake(){ownCollider=GetComponent<Collider>();if(ownCollider!=null)ownCollider.enabled=true;if(activatedSource!=null)activatedSource.gameObject.SetActive(false);}
        public RadiationInteractionResult ProcessRadiation(RadiationParticle particle,RaycastHit hit){if(!IsActivated){accumulatedDose+=particle!=null?particle.Intensity*dosePerIntensitySecond*Mathf.Max(0,Time.fixedDeltaTime):0;if(accumulatedDose>=criticalDose)Activate();}return new RadiationInteractionResult(1,1,false,destroyIncomingParticles);}
        private void Activate(){if(activatedSource==null)return;activatedSource.transform.SetParent(transform,false);activatedSource.transform.localPosition=Vector3.zero;activatedSource.SetEmission(activatedEmissionRate,activatedIntensity,RadiationFlowShape.Omnidirectional);activatedSource.gameObject.SetActive(true);accumulatedDose=criticalDose;if(ownCollider!=null)ownCollider.enabled=true;}
        private void OnDrawGizmosSelected(){Gizmos.color=IsActivated?Color.red:Color.yellow;Gizmos.DrawWireSphere(transform.position,.35f);}
    }
}
