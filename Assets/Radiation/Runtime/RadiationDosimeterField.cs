using System.Collections.Generic;
using UnityEngine;

namespace RadiationSimulation
{
    /// <summary>Configurable grid of physical dose-monitoring spheres.</summary>
    public sealed class RadiationDosimeterField : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float sphereSize = 0.18f;
        [SerializeField, Min(0.01f)] private float spacing = 0.75f;
        [SerializeField] private Vector3 gridSize = new Vector3(10f, 5f, 10f);
        [SerializeField] private Vector3 gridCenter;
        [SerializeField] private bool useGrid = true;
        [SerializeField, Min(1)] private int dosimeterCount = 81;
        [SerializeField, Min(1)] private int maximumDosimeters = 100000;
        [SerializeField, Min(0.0001f)] private float doseToRed = 10f;
        [SerializeField, Min(0.01f)] private float colorSmoothTime = 0.25f;
        [SerializeField] private Gradient doseGradient;
        [SerializeField] private Material sphereMaterial;
        private readonly List<RadiationDosimeter> dosimeters = new();
        private readonly List<float> displayedDose = new();
        private readonly List<float> colorVelocity = new();
        private readonly List<Renderer> renderers = new();
        public int Count => dosimeters.Count;
        public int DosimeterCount => dosimeterCount;
        public float SphereSize => sphereSize;
        public float Spacing => spacing;
        public Vector3 GridSize => gridSize;
        public Vector3 GridCenter => gridCenter;
        public bool UseGrid => useGrid;
        public float DoseToRed => doseToRed;
        public float ColorSmoothTime => colorSmoothTime;
        public void Configure(int newCount, float newSize, float newSpacing)
        {
            dosimeterCount = Mathf.Clamp(newCount, 1, maximumDosimeters);
            sphereSize = Mathf.Max(.01f, newSize);
            spacing = Mathf.Max(.01f, newSpacing);
            Rebuild();
        }

        public void ConfigureGrid(Vector3 newGridSize, Vector3 center, float newSpacing, float newSize)
        {
            gridSize = new Vector3(Mathf.Max(.01f, newGridSize.x), .01f, Mathf.Max(.01f, newGridSize.z));
            gridCenter = center;
            spacing = Mathf.Max(.01f, newSpacing);
            sphereSize = Mathf.Max(.01f, newSize);
            SnapToDemoFloor();
            Rebuild();
        }

        public void ConfigureCount(int newCount)
        {
            dosimeterCount = Mathf.Clamp(newCount, 1, maximumDosimeters);
            Rebuild();
        }

        public void ConfigureAdvanced(Vector3 center, bool grid, float newDoseToRed, float smoothTime)
        {
            gridCenter = center;
            useGrid = grid;
            doseToRed = Mathf.Max(.0001f, newDoseToRed);
            colorSmoothTime = Mathf.Max(.01f, smoothTime);
            Rebuild();
        }
        private void Reset()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(.05f, .25f, 1f), 0f),
                    new GradientColorKey(Color.cyan, .3f),
                    new GradientColorKey(Color.yellow, .65f),
                    new GradientColorKey(Color.red, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            doseGradient = g;
        }
        private void Start()
        {
            SnapToDemoFloor();
            CacheExistingDosimeters();
            ClampDosimetersToDemoFloor();
            if (dosimeters.Count == 0) Rebuild();
        }

        private void SnapToDemoFloor()
        {
            GameObject floor = GameObject.Find("Demo Floor");
            if (floor == null) return;
            Bounds bounds = new Bounds(floor.transform.position, Vector3.one);
            Renderer renderer = floor.GetComponent<Renderer>();
            if (renderer != null) bounds = renderer.bounds;
            else
            {
                Collider collider = floor.GetComponent<Collider>();
                if (collider != null) bounds = collider.bounds;
            }
            Vector3 worldCenter = transform.TransformPoint(gridCenter);
            worldCenter.y = bounds.max.y + sphereSize * .5f;
            gridCenter = transform.InverseTransformPoint(worldCenter);
        }
        private void ClampDosimetersToDemoFloor()
        {
            GameObject floor = GameObject.Find("Demo Floor");
            if (floor == null) return;
            Bounds bounds = floor.GetComponent<Renderer>() != null ? floor.GetComponent<Renderer>().bounds : floor.GetComponent<Collider>() != null ? floor.GetComponent<Collider>().bounds : new Bounds(floor.transform.position, Vector3.one);
            float radius = sphereSize * .5f;
            float y = bounds.max.y + radius;
            float minX = bounds.min.x + radius, maxX = bounds.max.x - radius;
            float minZ = bounds.min.z + radius, maxZ = bounds.max.z - radius;
            for (int i = 0; i < dosimeters.Count; i++)
            {
                if (dosimeters[i] == null) continue;
                Vector3 position = dosimeters[i].transform.position;
                position.x = Mathf.Clamp(position.x, minX, maxX);
                position.y = y;
                position.z = Mathf.Clamp(position.z, minZ, maxZ);
                dosimeters[i].transform.position = position;
            }
        }

        private void CacheExistingDosimeters()
        {
            dosimeters.Clear(); displayedDose.Clear(); colorVelocity.Clear(); renderers.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                RadiationDosimeter dosimeter = child.GetComponent<RadiationDosimeter>();
                Renderer renderer = child.GetComponent<Renderer>();
                if (dosimeter == null || renderer == null) continue;
                dosimeter.Configure(doseToRed, dosimeter.Selectable);
                dosimeters.Add(dosimeter); displayedDose.Add(0f); colorVelocity.Add(0f); renderers.Add(renderer);
            }
        }
        private void Update()
        {
            ClampDosimetersToDemoFloor();
            for (int i=0; i<dosimeters.Count; i++)
            {
                if (dosimeters[i] == null || renderers[i] == null) continue;
                float normalized = Mathf.Clamp01(dosimeters[i].Dose / doseToRed);
                float velocity = colorVelocity[i];
                displayedDose[i] = Mathf.SmoothDamp(displayedDose[i], normalized, ref velocity, colorSmoothTime);
                colorVelocity[i] = velocity;
                Color color = doseGradient != null
                    ? doseGradient.Evaluate(displayedDose[i])
                    : Color.Lerp(Color.blue, Color.red, displayedDose[i]);
                var block = new MaterialPropertyBlock();
                renderers[i].GetPropertyBlock(block);
                block.SetColor("_Color", color);
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * (.15f + displayedDose[i] * 1.5f));
                renderers[i].SetPropertyBlock(block);
            }
        }
        public void ResetDose()
        {
            foreach (var d in dosimeters)
            {
                if (d != null)
                {
                    d.ResetDose();
                }
            }
        }
        public void Rebuild()
        {
            for (int i=transform.childCount-1; i>=0; i--) Destroy(transform.GetChild(i).gameObject);
            dosimeters.Clear(); displayedDose.Clear(); colorVelocity.Clear(); renderers.Clear();
            SnapToDemoFloor();
            int xCount = Mathf.Max(1, Mathf.FloorToInt(gridSize.x / spacing) + 1);
            int yCount = 1;
            int zCount = Mathf.Max(1, Mathf.FloorToInt(gridSize.z / spacing) + 1);
            int gridCount = xCount * yCount * zCount;
            int count = Mathf.Min(maximumDosimeters, Mathf.Min(dosimeterCount, gridCount));
            Vector3 origin = gridCenter - new Vector3((xCount - 1) * spacing, 0f, (zCount - 1) * spacing) * .5f;
            for (int i = 0; i < count; i++)
            {
                int x = i % xCount;
                int y = 0;
                int z = i / xCount;
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Dosimeter_{i:000}";
                go.transform.SetParent(transform, false);
                go.transform.localPosition = useGrid
                    ? origin + new Vector3(x * spacing, 0f, z * spacing)
                    : gridCenter;
                go.transform.localScale = Vector3.one * sphereSize;
                if (sphereMaterial != null)
                {
                    go.GetComponent<Renderer>().sharedMaterial = sphereMaterial;
                }

                var d = go.AddComponent<RadiationDosimeter>();
                d.Configure(doseToRed);
                dosimeters.Add(d);
                displayedDose.Add(0f);
                colorVelocity.Add(0f);
                renderers.Add(go.GetComponent<Renderer>());
            }
        }
        private void OnValidate()
        {
            sphereSize = Mathf.Max(.01f, sphereSize);
            spacing = Mathf.Max(.01f, spacing);
            gridSize = new Vector3(
                Mathf.Max(.01f, gridSize.x),
                Mathf.Max(.01f, gridSize.y),
                Mathf.Max(.01f, gridSize.z));
            maximumDosimeters = Mathf.Max(1, maximumDosimeters);
            dosimeterCount = Mathf.Clamp(dosimeterCount, 1, maximumDosimeters);
            doseToRed = Mathf.Max(.0001f, doseToRed);
        }
    }
}
