# Radiation simulation

Add `RadiationSource` to a GameObject and assign a prefab containing `RadiationParticle`, a small Renderer, and a Collider. The source creates and updates each particle as an individual GameObject; Unity's built-in ParticleSystem is not used.

Attach one interaction component to a collider:

- `OpaqueRadiationBarrier`: destroys the particle.
- `AttenuatingRadiationBarrier`: applies `I = I0 * exp(-coefficient * density * thickness)`.
- `RadiationMirror`: reflects the velocity around the hit normal and applies reflectivity.
- `RadiationSpeedReducer`: reduces particle velocity and can apply optional exponential intensity loss.

Use a trigger collider on a speed-reducer volume and include its layer in the source collision mask. Solid barrier colliders should not be triggers. The custom inspector on `RadiationSource` exposes emission, flow shape, lifetime, inverse-square intensity, collision mask, and visual settings.
