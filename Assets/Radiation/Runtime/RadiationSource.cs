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
        [Header("GPU Instanced Visualization")]
        [SerializeField] private Mesh particleMesh;
        [SerializeField] private Material particleMaterial;
        [SerializeField] private bool useGpuInstancing = true;

        private readonly Queue<RadiationParticle> pool = new();
        private readonly List<Matrix4x4> instanceMatrices = new(1023);
        private readonly List<Vector4> instanceColors = new(1023);
        private readonly HashSet<RadiationParticle> activeParticles = new();
        private float emissionAccumulator;
        private bool warnedMissingPrefab;
        public float InitialIntensity => initialIntensity;
        public float ReferenceDistance => referenceDistance;
        public float SampleLifetime() => lifetime * Mathf.Lerp(1f - lifetimeVariation, 1f + lifetimeVariation, Random.value);
        public float MinimumIntensity => minimumIntensity;
        public float CollisionSkin => collisionSkin;
        public float MinParticleSize => minParticleSize;
        public float MaxParticleSize => maxParticleSize;
        public LayerMask CollisionMask => collisionMask;
        public bool UseGpuInstancing => useGpuInstancing;
        public Mesh ParticleMesh => particleMesh;
        public Material ParticleMaterial => particleMaterial;
        [SerializeField, Min(0.0001f)] private float particleRadius = 0.01f;
        public float ParticleRadius => particleRadius;

        private void Update()
        {
            if (emissionRate <= 0f) return;
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
            for (int i = 0; i < count; i++) Emit();
        }

        private void LateUpdate()
        {
            if (!useGpuInstancing || particleMesh == null || particleMaterial == null || activeParticles.Count == 0) return;
            if (!particleMaterial.enableInstancing) particleMaterial.enableInstancing = true;
            instanceMatrices.Clear();
            instanceColors.Clear();
            foreach (var particle in activeParticles)
            {
                if (particle == null || !particle.gameObject.activeSelf) continue;
                float alpha = Mathf.Clamp01(particle.Intensity / Mathf.Max(0.0001f, initialIntensity));
                instanceMatrices.Add(Matrix4x4.TRS(particle.transform.position, particle.transform.rotation, particle.transform.lossyScale));
                instanceColors.Add(new Vector4(1f, 0.18f, 0.02f, alpha));
                if (instanceMatrices.Count == 1023)
                {
                    DrawInstances(0, 1023);
                    instanceMatrices.Clear(); instanceColors.Clear();
                }
            }
            if (instanceMatrices.Count > 0) DrawInstances(0, instanceMatrices.Count);
        }

        private void DrawInstances(int offset, int count)
        {
            var props = new MaterialPropertyBlock();
            props.SetVectorArray("_Color", instanceColors.GetRange(offset, count).ToArray());
            var batch = instanceMatrices.GetRange(offset, count).ToArray();
            Graphics.DrawMeshInstanced(particleMesh, 0, particleMaterial, batch, count, props,
                UnityEngine.Rendering.ShadowCastingMode.Off, false, gameObject.layer);
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
            RadiationParticle particle = pool.Count > 0 ? pool.Dequeue() : Instantiate(particlePrefab, transform);
            particle.transform.SetPositionAndRotation(transform.position, transform.rotation);
            activeParticles.Add(particle);
            particle.Initialize(this, SampleDirection(), particleSpeed, SampleLifetime(), initialIntensity);
        }

        private Vector3 SampleDirection()
        {
            if (flowShape == RadiationFlowShape.Ray) return transform.forward;
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

        public float ComputeIntensityAtDistance(float distance, float currentIntensity) => RadiationMath.InverseSquare(currentIntensity, distance, referenceDistance);

        public void Release(RadiationParticle particle)
        {
            if (particle == null) return;
            activeParticles.Remove(particle);
            if (!pool.Contains(particle)) pool.Enqueue(particle);
        }

        private void OnDisable()
        {
            foreach (var particle in activeParticles)
                if (particle != null) particle.gameObject.SetActive(false);
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
            if (flowShape == RadiationFlowShape.Ray) Gizmos.DrawRay(transform.position, transform.forward * 2f);
            else if (flowShape == RadiationFlowShape.Cone) Gizmos.DrawRay(transform.position, transform.forward * 2f);
        }
    }
}
