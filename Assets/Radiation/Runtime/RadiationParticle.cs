using UnityEngine;

namespace RadiationSimulation
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class RadiationParticle : MonoBehaviour
    {
        [SerializeField] private Renderer particleRenderer;
        [SerializeField] private Collider particleCollider;
        private Rigidbody body; private RadiationSource source; private Vector3 velocity;
        private float remainingLifetime, initialIntensity, transmission, traveledDistance;
        private bool initialized;
        private int visualTick;
        private MaterialPropertyBlock visualBlock;
        private RaycastHit[] castBuffer = new RaycastHit[16];
        private Collider[] volumeBuffer = new Collider[16];
        public float Intensity => source == null ? 0f : source.ComputeIntensityAtDistance(traveledDistance, initialIntensity * transmission);
        public float RemainingLifetime => remainingLifetime; public Vector3 Velocity => velocity;
        public void Initialize(RadiationSource owner, Vector3 direction, float speed, float lifetime, float initialValue)
        {
            source=owner; velocity=(direction.sqrMagnitude>.000001f?direction.normalized:Vector3.forward)*Mathf.Max(0,speed); remainingLifetime=Mathf.Max(0,lifetime); initialIntensity=Mathf.Max(0,initialValue); transmission=1; traveledDistance=0; initialized=true;
            if(body==null) body=GetComponent<Rigidbody>(); body.isKinematic=true; body.useGravity=false; if(particleCollider==null) particleCollider=GetComponent<Collider>(); if(particleCollider!=null) particleCollider.enabled=false; gameObject.SetActive(true); if(particleRenderer!=null) particleRenderer.enabled=true; UpdateVisuals();
        }
        public void SetVisuals(Renderer visual, Collider collider){particleRenderer=visual;particleCollider=collider;}
        public void ApplyVolumeEffect(float multiplier,float speedMultiplier){transmission*=Mathf.Clamp01(multiplier); float s=velocity.magnitude*Mathf.Max(0,speedMultiplier); velocity=velocity.sqrMagnitude>.000001f?velocity.normalized*s:Vector3.zero;}
        internal void AddDose(float amount){ transmission *= 1f; }
        private void Awake(){body=GetComponent<Rigidbody>(); particleCollider=particleCollider??GetComponent<Collider>(); particleRenderer=particleRenderer??GetComponentInChildren<Renderer>(); body.isKinematic=true;body.useGravity=false;}
        private void FixedUpdate()
        {
            if(!initialized||source==null)return;
            float remain=Time.fixedDeltaTime; Collider ignored=null; int guard=0;
            while(remain>1e-6f&&initialized&&guard++<8)
            {
                if(remainingLifetime<=0){Despawn();break;}
                float dt=Mathf.Min(remain,remainingLifetime);
                Vector3 a=RadiationMagnet.EvaluateAcceleration(transform.position,velocity,this);
                velocity += a * dt;
                float max=RadiationMagnet.GetMaximumParticleSpeed(); if(!float.IsPositiveInfinity(max)) velocity=Vector3.ClampMagnitude(velocity,max);
                float speedNow=velocity.magnitude; if(speedNow<=1e-6f){Despawn();break;} float distance=speedNow*dt; Vector3 dir=velocity/speedNow;
                int hitCount=Physics.SphereCastNonAlloc(transform.position,source.ParticleRadius,dir,castBuffer,distance+source.CollisionSkin,source.CollisionMask,QueryTriggerInteraction.Collide); RaycastHit hit=default; bool found=false; float near=float.MaxValue;
                for(int hitIndex=0;hitIndex<hitCount;hitIndex++){var h=castBuffer[hitIndex];var c=h.collider;if(c==null||c==particleCollider||c==ignored||c.GetComponent<RadiationParticle>()!=null||(source!=null&&c.transform.IsChildOf(source.transform)))continue;if(c.GetComponent(typeof(IRadiationVolume)) is IRadiationVolume) continue;if(h.distance<near){near=h.distance;hit=h;found=true;}}
                if(!found){MovePath(distance,dt);remain-=dt;continue;}
                float move=Mathf.Clamp(near-source.CollisionSkin,0,distance); float used=move/speedNow; MovePath(move,used); if(!initialized)break;
                var interaction=hit.collider.GetComponent(typeof(IRadiationInteraction)) as IRadiationInteraction ?? hit.collider.GetComponentInParent(typeof(IRadiationInteraction)) as IRadiationInteraction;
                if(interaction==null){Despawn();break;} var result=interaction.ProcessRadiation(this,hit); transmission*=Mathf.Clamp01(result.IntensityMultiplier); if(result.Reflected) velocity=Vector3.Reflect(velocity,hit.normal); else velocity*=Mathf.Max(0,result.SpeedMultiplier); if(result.DestroyParticle){Despawn();break;}
                transform.position += (velocity.sqrMagnitude>1e-8f?velocity.normalized:dir)*source.CollisionSkin; ignored=hit.collider; float consumed=Mathf.Max(used,1e-5f); remain-=consumed;
            }
            if ((++visualTick & 1) == 0) UpdateVisuals();
        }
        private void MovePath(float distance,float time){Vector3 start=transform.position;Vector3 dir=velocity.sqrMagnitude>1e-8f?velocity.normalized:Vector3.zero;transform.position+=dir*distance;traveledDistance+=distance;remainingLifetime-=time;ApplyVolumesAlongSegment(start,transform.position,distance);}
        private void ApplyVolumesAlongSegment(Vector3 start, Vector3 end, float distance)
        {
            if (distance <= 1e-6f) return;
            int colliderCount = Physics.OverlapCapsuleNonAlloc(start, end, source.ParticleRadius,
                volumeBuffer, source.CollisionMask, QueryTriggerInteraction.Collide);
            Vector3 direction = (end - start).normalized;
            for (int colliderIndex = 0; colliderIndex < colliderCount; colliderIndex++)
            {
                var collider = volumeBuffer[colliderIndex];
                var volume = collider.GetComponent(typeof(IRadiationVolume)) as IRadiationVolume;
                if (volume == null) continue;

                // Cast from outside both ends: Unity rays starting inside a collider
                // do not report its exit surface. Clip the interval to this movement.
                float padding = collider.bounds.size.magnitude + distance + 1f;
                float castLength = 2f * padding + distance;
                if (!collider.Raycast(new Ray(start - direction * padding, direction), out var entryHit, castLength)
                    || !collider.Raycast(new Ray(end + direction * padding, -direction), out var exitHit, castLength)) continue;
                float entry = Mathf.Clamp(entryHit.distance - padding, 0f, distance);
                float exit = Mathf.Clamp(distance + padding - exitHit.distance, 0f, distance);
                float path = Mathf.Max(0f, exit - entry);
                if (path <= 1e-6f) continue;

                volume.ProcessVolume(this, path);
                // Keep a stable reduced speed while inside the volume. Do not multiply
                // the already-reduced velocity every physics step, or the particle
                // asymptotically stops at the volume boundary.
                float multiplier = Mathf.Max(0f, volume.GetSpeedMultiplier(this));
                float targetSpeed = source.ParticleSpeed * multiplier;
                velocity = velocity.sqrMagnitude > 1e-8f
                    ? velocity.normalized * targetSpeed
                    : Vector3.zero;
                var dosimeter = collider.GetComponent<RadiationDosimeter>();
                if (dosimeter != null) dosimeter.RegisterIntersection(this, path);
            }
        }
        private void UpdateVisuals(){if(particleRenderer!=null){if(visualBlock==null)visualBlock=new MaterialPropertyBlock();particleRenderer.GetPropertyBlock(visualBlock);visualBlock.SetColor("_Color",new Color(1,1,1,Mathf.Clamp01(Intensity/Mathf.Max(.0001f,initialIntensity))));particleRenderer.SetPropertyBlock(visualBlock);}if(source!=null)transform.localScale=Vector3.one*Mathf.Lerp(source.MinParticleSize,source.MaxParticleSize,Mathf.Clamp01(Intensity/Mathf.Max(.0001f,initialIntensity)));}
        private void Despawn(){initialized=false;if(particleCollider!=null)particleCollider.enabled=false;if(source!=null)source.Release(this);gameObject.SetActive(false);}
    }

    /// <summary>Pure mathematical kernel. No scene objects, rendering, physics queries or frame callbacks.</summary>
    public static class RadiationMath
    {
        public static float InverseSquare(float intensity, float distance, float referenceDistance)
        {
            float reference = Mathf.Max(0.0001f, referenceDistance);
            float d = Mathf.Max(reference, distance);
            return Mathf.Max(0f, intensity) * reference * reference / (d * d);
        }

        public static float Transmission(float coefficient, float density, float length) =>
            Mathf.Exp(-Mathf.Max(0f, coefficient) * Mathf.Max(0f, density) * Mathf.Max(0f, length));

        public static Vector3 Reflect(Vector3 velocity, Vector3 normal) =>
            velocity - 2f * Vector3.Dot(velocity, normal) * normal;

        public static Vector3 Advance(Vector3 position, Vector3 velocity, float deltaTime) => position + velocity * deltaTime;

        public static Vector3 ConeDirection(float angleDegrees, float azimuth01, float elevation01)
        {
            float z = Mathf.Lerp(Mathf.Cos(angleDegrees * Mathf.Deg2Rad), 1f, elevation01);
            float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            float phi = azimuth01 * Mathf.PI * 2f;
            return new Vector3(radial * Mathf.Cos(phi), radial * Mathf.Sin(phi), z);
        }
    }

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
