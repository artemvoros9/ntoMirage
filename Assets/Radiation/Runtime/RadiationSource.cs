using System.Collections.Generic;
using UnityEngine;

namespace RadiationSimulation
{
    public enum RadiationFlowShape { Omnidirectional, Cone, Ray }

    public sealed class RadiationSource : MonoBehaviour
    {
        [Header("Emission")]
        [SerializeField] private RadiationParticle particlePrefab;
        [SerializeField, Min(0f)] private float emissionRate = 40f;
        [SerializeField, Min(1)] private int maxParticles = 500;
        [SerializeField, Min(0.01f)] private float particleSpeed = 8f;
        [SerializeField, Min(0.01f)] private float lifetime = 6f;
        [SerializeField, Range(0f, 1f)] private float lifetimeVariation = 0.25f;
        [SerializeField, Min(0f)] private float initialIntensity = 1f;
        [SerializeField, Min(0.0001f)] private float referenceDistance = 1f;
        [SerializeField] private RadiationFlowShape flowShape = RadiationFlowShape.Omnidirectional;
        [SerializeField, Range(0f, 180f)] private float coneAngle = 30f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField, Min(0.0001f)] private float minimumIntensity = 0.001f;
        [SerializeField, Min(0.0001f)] private float collisionSkin = 0.002f;
        [SerializeField, Min(0.001f)] private float minParticleSize = 0.015f;
        [SerializeField, Min(0.001f)] private float maxParticleSize = 0.06f;
        private readonly Queue<RadiationParticle> pool = new();
        private readonly HashSet<RadiationParticle> activeParticles = new();
        private float emissionAccumulator;
        private bool warnedMissingPrefab;
        public float InitialIntensity => initialIntensity;
        public float ReferenceDistance => referenceDistance;
        public float SampleLifetime()
        {
            return lifetime * Mathf.Lerp(1f - lifetimeVariation, 1f + lifetimeVariation, Random.value);
        }
        public float MinimumIntensity => minimumIntensity;
        public float CollisionSkin => collisionSkin;
        public float MinParticleSize => minParticleSize;
        public float MaxParticleSize => maxParticleSize;
        public LayerMask CollisionMask => collisionMask;
        public float EmissionRate => emissionRate;
        public int MaxParticles => maxParticles;
        public float ParticleSpeed => particleSpeed;
        public float Lifetime => lifetime;
        public float LifetimeVariation => lifetimeVariation;
        public float ConeAngle => coneAngle;
        public RadiationFlowShape FlowShape => flowShape;
        public int ActiveParticleCount => activeParticles.Count;
        public void ConfigureRuntime(float rate, float speed, float life, float variation, float intensity, float cone, RadiationFlowShape shape)
        {
            emissionRate = Mathf.Max(0f, rate);
            particleSpeed = Mathf.Max(0.01f, speed);
            lifetime = Mathf.Max(0.01f, life);
            lifetimeVariation = Mathf.Clamp01(variation);
            initialIntensity = Mathf.Max(0f, intensity);
            coneAngle = Mathf.Clamp(cone, 0f, 180f);
            flowShape = shape;
        }
        [SerializeField, Min(0.0001f)] private float particleRadius = 0.01f;
        public float ParticleRadius => particleRadius;

        public void CopyConfigurationFrom(RadiationSource other)
        {
            if (other == null)
            {
                return;
            }
            particlePrefab = other.particlePrefab;
            emissionRate = other.emissionRate;
            maxParticles = other.maxParticles;
            particleSpeed = other.particleSpeed;
            lifetime = other.lifetime;
            lifetimeVariation = other.lifetimeVariation;
            initialIntensity = other.initialIntensity;
            referenceDistance = other.referenceDistance;
            flowShape = other.flowShape;
            coneAngle = other.coneAngle;
            collisionMask = other.collisionMask;
            minimumIntensity = other.minimumIntensity;
            collisionSkin = other.collisionSkin;
            minParticleSize = other.minParticleSize;
            maxParticleSize = other.maxParticleSize;
            particleRadius = other.particleRadius;
        }

        public void SetEmission(float rate, float intensity, RadiationFlowShape shape)
        {
            emissionRate = Mathf.Max(0f, rate);
            initialIntensity = Mathf.Max(0f, intensity);
            flowShape = shape;
        }

        public void ConfigureEmissionProperties(float rate, float speed, float life, float variation, float intensity)
        {
            emissionRate = Mathf.Max(0f, rate);
            particleSpeed = Mathf.Max(.01f, speed);
            lifetime = Mathf.Max(.01f, life);
            lifetimeVariation = Mathf.Clamp01(variation);
            initialIntensity = Mathf.Max(0f, intensity);
        }

        public void ConfigureAdvanced(float newMaxParticles, float newVariation, float newIntensity, float newReferenceDistance,
            float newConeAngle, float newMinParticleSize, float newMaxParticleSize, float newParticleRadius)
        {
            maxParticles = Mathf.Max(1, Mathf.RoundToInt(newMaxParticles));
            lifetimeVariation = Mathf.Clamp01(newVariation);
            initialIntensity = Mathf.Max(0f, newIntensity);
            referenceDistance = Mathf.Max(.0001f, newReferenceDistance);
            coneAngle = Mathf.Clamp(newConeAngle, 0f, 180f);
            minParticleSize = Mathf.Max(.001f, newMinParticleSize);
            maxParticleSize = Mathf.Max(minParticleSize, newMaxParticleSize);
            particleRadius = Mathf.Max(.0001f, newParticleRadius);
        }

        private void Update()
        {
            if (emissionRate <= 0f)
            {
                return;
            }
            if (particlePrefab == null)
            {
                if (!warnedMissingPrefab)
                {
                    Debug.LogError($"{name}: назначьте 'Префаб частицы' с RadiationParticle, Rigidbody и Collider.", this);
                    warnedMissingPrefab = true;
                }
                return;
            }
            emissionAccumulator += emissionRate * Time.deltaTime;
            int available = Mathf.Max(0, maxParticles - activeParticles.Count);
            int count = Mathf.Min(Mathf.FloorToInt(emissionAccumulator), available);
            emissionAccumulator -= count;
            for (int i = 0; i < count; i++)
            {
                Emit();
            }
        }

        private void Emit()
        {
            if (particlePrefab == null)
            {
                if (!warnedMissingPrefab)
                {
                    Debug.LogError($"{name}: Emit остановлен — поле 'Префаб частицы' пустое. Назначьте prefab с RadiationParticle, Rigidbody и Collider.", this);
                    warnedMissingPrefab = true;
                }
                return;
            }
            RadiationParticle particle;
            if (pool.Count > 0)
            {
                particle = pool.Dequeue();
            }
            else
            {
                particle = Instantiate(particlePrefab);
            }
            // Частица должна двигаться в мировом пространстве и не наследовать движение
            // источника (особенно важно для активированного критического объекта).
            particle.transform.SetParent(null, true);
            particle.transform.SetPositionAndRotation(transform.position, transform.rotation);
            activeParticles.Add(particle);
            particle.Initialize(this, SampleDirection(), particleSpeed, SampleLifetime(), initialIntensity);
        }

        private Vector3 SampleDirection()
        {
            if (flowShape == RadiationFlowShape.Ray)
            {
                return transform.forward;
            }
            if (flowShape == RadiationFlowShape.Cone)
            {
                float cos = Mathf.Cos(coneAngle * Mathf.Deg2Rad);
                // Uniformly sample the spherical cap. The radial component must be
                // bounded by the sampled z; using an unconstrained disk would leak
                // directions outside the configured cone.
                float z = Mathf.Lerp(cos, 1f, Random.value);
                float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                Vector2 disk = Random.insideUnitCircle;
                if (disk.sqrMagnitude > 0.000001f) disk = disk.normalized * radial;
                else disk = Vector2.right * radial;
                Vector3 local = new Vector3(disk.x, disk.y, z);
                return transform.TransformDirection(local).normalized;
            }
            return Random.onUnitSphere;
        }

        public float ComputeIntensityAtDistance(float distance, float currentIntensity)
        {
            return RadiationMath.InverseSquare(currentIntensity, distance, referenceDistance);
        }

        public void Release(RadiationParticle particle)
        {
            if (particle == null)
            {
                return;
            }
            activeParticles.Remove(particle);
            pool.Enqueue(particle);
        }

        private void OnDisable()
        {
            foreach (var particle in activeParticles)
            {
                if (particle != null)
                {
                    particle.gameObject.SetActive(false);
                    pool.Enqueue(particle);
                }
            }
            activeParticles.Clear();
        }

        private void OnValidate()
        {
            maxParticles = Mathf.Max(1, maxParticles);
            referenceDistance = Mathf.Max(0.0001f, referenceDistance);
            particleRadius = Mathf.Max(0.001f, particleRadius);
            if (flowShape == RadiationFlowShape.Ray) coneAngle = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, 0.12f);
            if (flowShape == RadiationFlowShape.Ray)
            {
                Gizmos.DrawRay(transform.position, transform.forward * 2f);
            }
            else if (flowShape == RadiationFlowShape.Cone)
            {
                Gizmos.DrawRay(transform.position, transform.forward * 2f);
            }
        }
    }
}
