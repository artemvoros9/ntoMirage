using System.Collections.Generic;
using UnityEngine;

namespace RadiationSimulation
{
    /// <summary>Configurable grid of dose-monitoring spheres.</summary>
    public sealed class RadiationDosimeterField : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField, Min(1)] private int count = 25;
        [SerializeField, Min(0.01f)] private float sphereSize = 0.18f;
        [SerializeField, Min(0.01f)] private float spacing = 0.75f;
        [SerializeField] private Vector3 gridCenter;
        [SerializeField] private bool useGrid = true;
        [Header("Dose")]
        [SerializeField, Min(0.0001f)] private float doseToRed = 10f;
        [SerializeField, Min(0.01f)] private float colorSmoothTime = 0.25f;
        [SerializeField] private Gradient doseGradient;
        [SerializeField] private Material sphereMaterial;

        private readonly List<Transform> spheres = new();
        private readonly List<float> doses = new();
        private readonly List<float> displayedDose = new();
        private readonly List<Vector3> colorVelocities = new();
        private readonly List<Renderer> renderers = new();
        private readonly List<RadiationSource> sources = new();

        private void Reset()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.05f, 0.25f, 1f), 0f), new GradientColorKey(Color.cyan, 0.3f), new GradientColorKey(Color.yellow, 0.65f), new GradientColorKey(Color.red, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            doseGradient = gradient;
        }

        private void Start() => Rebuild();

        private void Update()
        {
            if (spheres.Count == 0) return;
            sources.Clear();
            sources.AddRange(FindObjectsOfType<RadiationSource>());
            for (int i = 0; i < spheres.Count; i++)
            {
                if (spheres[i] == null) continue;
                float rate = 0f;
                foreach (var source in sources)
                {
                    if (source == null || !source.isActiveAndEnabled) continue;
                    rate += source.ComputeIntensityAtDistance(Vector3.Distance(spheres[i].position, source.transform.position), source.InitialIntensity);
                }
                doses[i] += rate * Time.deltaTime;
                float normalized = Mathf.Clamp01(doses[i] / doseToRed);
                float velocity = colorVelocities[i].x;
                displayedDose[i] = Mathf.SmoothDamp(displayedDose[i], normalized, ref velocity, colorSmoothTime);
                colorVelocities[i] = new Vector3(velocity, 0f, 0f);
                Color color = doseGradient != null ? doseGradient.Evaluate(displayedDose[i]) : Color.Lerp(Color.blue, Color.red, displayedDose[i]);
                var block = new MaterialPropertyBlock();
                renderers[i].GetPropertyBlock(block);
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * (0.15f + displayedDose[i] * 1.5f));
                renderers[i].SetPropertyBlock(block);
            }
        }

        public void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            spheres.Clear(); doses.Clear(); displayedDose.Clear(); colorVelocities.Clear(); renderers.Clear();
            int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            for (int i = 0; i < count; i++)
            {
                int x = i % columns, z = i / columns;
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Dosimeter_{i:00}";
                go.transform.SetParent(transform, false);
                go.transform.localPosition = useGrid ? gridCenter + new Vector3((x - (columns - 1) * 0.5f) * spacing, 0f, (z - (columns - 1) * 0.5f) * spacing) : gridCenter;
                go.transform.localScale = Vector3.one * sphereSize;
                if (sphereMaterial != null) go.GetComponent<Renderer>().sharedMaterial = sphereMaterial;
                Destroy(go.GetComponent<Collider>());
                spheres.Add(go.transform); doses.Add(0f); displayedDose.Add(0f); colorVelocities.Add(Vector3.zero); renderers.Add(go.GetComponent<Renderer>());
            }
        }

        private void OnValidate() { count = Mathf.Max(1, count); sphereSize = Mathf.Max(0.01f, sphereSize); spacing = Mathf.Max(0.01f, spacing); doseToRed = Mathf.Max(0.0001f, doseToRed); }
    }
}
