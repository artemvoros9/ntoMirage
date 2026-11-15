using UnityEngine;

namespace RadiationSimulation
{
    /// <summary>Independent kinematic sphere solved with custom Physics sphere casts.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class RadiationParticle : MonoBehaviour
    {
        [SerializeField] private Renderer particleRenderer;
        [SerializeField] private Collider particleCollider;
        private Rigidbody body;
        private RadiationSource source;
        private Vector3 velocity;
        private float remainingLifetime, initialIntensity, transmission, traveledDistance;
        private bool initialized;
        public float Intensity => source == null ? 0f : source.ComputeIntensityAtDistance(traveledDistance, initialIntensity * transmission);
        public float RemainingLifetime => remainingLifetime;
        public Vector3 Velocity => velocity;

        public void Initialize(RadiationSource owner, Vector3 direction, float speed, float lifetime, float initialValue)
        {
            source = owner; velocity = direction.sqrMagnitude > 0.000001f ? direction.normalized * Mathf.Max(0f, speed) : Vector3.forward * Mathf.Max(0f, speed);
            remainingLifetime = Mathf.Max(0f, lifetime); initialIntensity = Mathf.Max(0f, initialValue); transmission = 1f; traveledDistance = 0f; initialized = true;
            if (body == null) body = GetComponent<Rigidbody>();
            body.isKinematic = true; body.useGravity = false;
            if (particleCollider == null) particleCollider = GetComponent<Collider>();
            gameObject.SetActive(true);
            if (particleRenderer != null) particleRenderer.enabled = !source.UseGpuInstancing;
            UpdateVisuals();
        }
        public void SetVisuals(Renderer visual, Collider collider) { particleRenderer = visual; particleCollider = collider; }
        public void ApplyVolumeEffect(float multiplier, float speedMultiplier) { transmission *= Mathf.Clamp01(multiplier); velocity = velocity.normalized * (velocity.magnitude * Mathf.Max(0f, speedMultiplier)); }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            particleCollider = particleCollider != null ? particleCollider : GetComponent<Collider>();
            particleRenderer = particleRenderer != null ? particleRenderer : GetComponentInChildren<Renderer>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void FixedUpdate()
        {
            if (!initialized || source == null) return;
            float remaining = Time.fixedDeltaTime;
            while (remaining > 0.000001f && initialized)
            {
                if (remainingLifetime <= 0f || Intensity <= source.MinimumIntensity) { Despawn(); break; }
                float stepTime = Mathf.Min(remaining, remainingLifetime);
                velocity += RadiationMagnet.EvaluateAcceleration(transform.position, velocity, this) * stepTime;
                float magnetSpeedLimit = RadiationMagnet.GetMaximumParticleSpeed();
                if (!float.IsPositiveInfinity(magnetSpeedLimit)) velocity = Vector3.ClampMagnitude(velocity, magnetSpeedLimit);
                float speed = velocity.magnitude; if (speed <= 0.000001f) { Despawn(); break; }
                float distance = speed * stepTime;
                Vector3 direction = velocity / speed; Vector3 origin = transform.position;
                float radius = source.ParticleRadius;
                RaycastHit[] hits = Physics.SphereCastAll(origin, radius, direction, distance + source.CollisionSkin, source.CollisionMask, QueryTriggerInteraction.Collide);
                RaycastHit hit = default; bool found = false; float nearest = float.MaxValue;
                for (int i = 0; i < hits.Length; i++) { var c = hits[i].collider; if (c == null || c == particleCollider || c.GetComponent<RadiationParticle>() != null || (source != null && c.transform.IsChildOf(source.transform))) continue; if (hits[i].distance < nearest) { nearest = hits[i].distance; hit = hits[i]; found = true; } }
                if (!found) { MovePath(distance, stepTime); remaining -= stepTime; continue; }
                float move = Mathf.Clamp(nearest - source.CollisionSkin, 0f, distance); MovePath(move, move / speed); if (!initialized) break;
                var interaction = hit.collider.GetComponent(typeof(IRadiationInteraction)) as IRadiationInteraction;
                 if (interaction != null) { var result = interaction.ProcessRadiation(this, hit); transmission *= Mathf.Clamp01(result.IntensityMultiplier); if (result.Reflected) velocity = Vector3.Reflect(velocity, hit.normal); else velocity *= Mathf.Max(0f, result.SpeedMultiplier); if (result.DestroyParticle || Intensity <= source.MinimumIntensity) { Despawn(); break; } transform.position += velocity.normalized * source.CollisionSkin; remaining -= move / speed; }
                else { Despawn(); break; }
            }
            UpdateVisuals();
        }
        private void MovePath(float distance, float time)
        {
            Vector3 start = transform.position;
            Vector3 direction = velocity.sqrMagnitude > 0.000001f ? velocity.normalized : Vector3.zero;
            transform.position += direction * distance;
            traveledDistance += distance;
            remainingLifetime -= time;
            ApplyVolumesAlongSegment(start, transform.position, distance);
        }
        private void ApplyVolumesAlongSegment(Vector3 start, Vector3 end, float distance)
        {
            if (distance <= 0f) return;
            Vector3 midpoint = Vector3.Lerp(start, end, 0.5f);
            Collider[] cols = Physics.OverlapSphere(midpoint, source.ParticleRadius, source.CollisionMask, QueryTriggerInteraction.Collide);
            foreach (var c in cols)
            {
                var volume = c.GetComponent(typeof(IRadiationVolume)) as IRadiationVolume;
                if (volume != null)
                {
                    volume.ProcessVolume(this, distance);
                    velocity *= Mathf.Max(0f, volume.GetSpeedMultiplier(this));
                }
            }
        }
        private void UpdateVisuals() { if (particleRenderer != null) { var block = new MaterialPropertyBlock(); particleRenderer.GetPropertyBlock(block); block.SetColor("_Color", new Color(1f,1f,1f,Mathf.Clamp01(Intensity/Mathf.Max(0.0001f,initialIntensity)))); particleRenderer.SetPropertyBlock(block); } if (source != null) transform.localScale = Vector3.one * Mathf.Lerp(source.MinParticleSize, source.MaxParticleSize, Mathf.Clamp01(Intensity/Mathf.Max(0.0001f,initialIntensity))); }
        private void Despawn() { initialized = false; if (source != null) source.Release(this); gameObject.SetActive(false); }
    }
}
