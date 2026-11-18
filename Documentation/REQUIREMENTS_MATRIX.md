# ТВР — матрица соответствия

| Требование | Реализация | Проверка |
|---|---|---|
| Omni / cone / ray | `RadiationSource` | Runtime inspector / source enum |
| Lifetime variation | `SampleLifetime` | Source fields |
| Inverse-square falloff | `RadiationMath.InverseSquare` | Particle intensity |
| Individual physics objects | `RadiationParticle` + kinematic Rigidbody | Prefab `Sphere` |
| Lorentz force | `RadiationMagnet` | Magnet field test |
| Opaque barrier | `OpaqueRadiationBarrier` | `RadiationInteractionResult.Blocked` |
| Exponential barrier | `AttenuatingRadiationBarrier` | `exp(-coefficient*density*length)` |
| Reflection | `RadiationMirror` | reflected velocity |
| Gas moderator | `RadiationSpeedReducer` | trigger volume |
| Dosimeters | `RadiationDosimeterField` | gradient + dose component |
| Critical secondary source | `CriticalRadiationObject` | threshold activation |
| Selection / outline / drag | `RadiationSandboxController` | mouse + cyan outline |
| Debug telemetry | `RadiationSandboxController` | FPS, CPU, GPU, particles, colliders |
| Three test presets | Scenario scenes + factory | Build Settings |
| Presentation | `Radiation_Sandbox_Presentation.pptx` | 9 editable slides |
| Artbook | `Radiation_Simulator_Artbook.md` | technical/art direction |
