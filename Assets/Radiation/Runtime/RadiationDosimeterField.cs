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
        [SerializeField, Tooltip("Prefab модели дозиметра; если не назначен, используется сфера.")]
        private GameObject dosimeterPrefab;
        [SerializeField, Tooltip("Путь от корня модели до дочернего объекта с Renderer. Пусто — Renderer корня или первый дочерний Renderer.")]
        private string colorTargetPath = "";
        [SerializeField, Tooltip("Необязательно: выберите дочерний Transform внутри prefab вместо указания пути.")]
        private Transform colorTarget;
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
        public GameObject DosimeterPrefab => dosimeterPrefab;
        public string ColorTargetPath => colorTargetPath;
        public Transform ColorTarget => colorTarget;

        public void ConfigureVisuals(GameObject prefab, Transform target, string path)
        {
            if (target != null && (prefab == null || !target.IsChildOf(prefab.transform)))
            {
                Debug.LogWarning("Цветовой объект должен принадлежать prefab дозиметра.", this);
                return;
            }
            path ??= "";
            if (dosimeterPrefab == prefab && colorTarget == target && colorTargetPath == path) return;
            dosimeterPrefab = prefab;
            colorTarget = target;
            colorTargetPath = path;
            Rebuild();
        }

        private Renderer ResolveColorRenderer(Transform root)
        {
            Transform target = null;
            if (dosimeterPrefab != null && colorTarget != null && colorTarget.IsChildOf(dosimeterPrefab.transform))
            {
                string relativePath = RelativePath(dosimeterPrefab.transform, colorTarget);
                target = relativePath.Length == 0 ? root : root.Find(relativePath);
            }
            if (target == null && !string.IsNullOrWhiteSpace(colorTargetPath))
                target = root.Find(colorTargetPath.Trim());
            if (target != null)
            {
                Renderer selected = target.GetComponent<Renderer>();
                if (selected != null) return selected;
            }
            return root.GetComponent<Renderer>() ?? root.GetComponentInChildren<Renderer>(true);
        }

        private static string RelativePath(Transform root, Transform child)
        {
            if (child == root) return "";
            var names = new List<string>();
            for (Transform current = child; current != null && current != root; current = current.parent)
                names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }
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
            // Scene children may have been generated before the prefab was assigned.
            if (dosimeters.Count == 0 || dosimeterPrefab != null) Rebuild();
        }

        private void SnapToDemoFloor()
        {
            GameObject floor = GameObject.Find("Demo Floor");
            if (floor == null) return;
            Bounds bounds = new Bounds(floor.transform.position, Vector3.one);
            Collider collider = floor.GetComponent<Collider>();
            Renderer renderer = floor.GetComponent<Renderer>();
            if (collider != null) bounds = collider.bounds;
            else if (renderer != null) bounds = renderer.bounds;
            Vector3 worldCenter = transform.TransformPoint(gridCenter);
            // The grid specifies X/Z only; the actual height depends on each model's geometry.
            worldCenter.y = bounds.max.y;
            gridCenter = transform.InverseTransformPoint(worldCenter);
        }
        private void ClampDosimetersToDemoFloor()
        {
            GameObject floor = GameObject.Find("Demo Floor");
            if (floor == null) return;
            Collider floorCollider = floor.GetComponent<Collider>();
            Renderer floorRenderer = floor.GetComponent<Renderer>();
            Bounds bounds = floorCollider != null ? floorCollider.bounds : floorRenderer != null ? floorRenderer.bounds : new Bounds(floor.transform.position, Vector3.one);
            float margin = Mathf.Max(0.01f, sphereSize * .5f);
            float minX = bounds.min.x + margin, maxX = bounds.max.x - margin;
            float minZ = bounds.min.z + margin, maxZ = bounds.max.z - margin;
            for (int i = 0; i < dosimeters.Count; i++)
            {
                if (dosimeters[i] == null) continue;
                Transform root = dosimeters[i].transform;
                Vector3 position = root.position;
                position.x = Mathf.Clamp(position.x, minX, maxX);
                position.z = Mathf.Clamp(position.z, minZ, maxZ);
                root.position = position;
                PlaceOnFloor(root, bounds.max.y);
            }
        }

        private static void PlaceOnDemoFloor(Transform root)
        {
            GameObject floor = GameObject.Find("Demo Floor");
            if (floor == null) return;
            Renderer floorRenderer = floor.GetComponent<Renderer>();
            Collider floorCollider = floor.GetComponent<Collider>();
            float height = floorCollider != null ? floorCollider.bounds.max.y
                : floorRenderer != null ? floorRenderer.bounds.max.y : floor.transform.position.y;
            PlaceOnFloor(root, height);
        }

        private static void PlaceOnFloor(Transform root, float floorHeight)
        {
            // Align the lowest point of the actual model, not its pivot or a size slider.
            // Ignore particle-trigger colliders: they may deliberately extend beyond the mesh.
            Renderer[] meshes = root.GetComponentsInChildren<Renderer>(true);
            float bottom = float.PositiveInfinity;
            foreach (Renderer mesh in meshes)
                if (mesh != null) bottom = Mathf.Min(bottom, mesh.bounds.min.y);
            if (float.IsPositiveInfinity(bottom))
            {
                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                foreach (Collider collider in colliders)
                    if (collider != null && !collider.isTrigger)
                        bottom = Mathf.Min(bottom, collider.bounds.min.y);
            }
            if (!float.IsPositiveInfinity(bottom))
                root.position += Vector3.up * (floorHeight - bottom);
        }

        private void CacheExistingDosimeters()
        {
            dosimeters.Clear(); displayedDose.Clear(); colorVelocity.Clear(); renderers.Clear();
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                RadiationDosimeter dosimeter = child.GetComponent<RadiationDosimeter>();
                Renderer renderer = ResolveColorRenderer(child);
                if (dosimeter == null) continue;
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
            for (int i=transform.childCount-1; i>=0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                child.transform.SetParent(null);
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
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
                int z = i / xCount;
                var go = dosimeterPrefab != null
                    ? Instantiate(dosimeterPrefab, transform)
                    : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Dosimeter_{i:000}";
                go.transform.SetParent(transform, false);
                go.transform.localPosition = useGrid
                    ? origin + new Vector3(x * spacing, 0f, z * spacing)
                    : gridCenter;
                go.transform.localScale = (dosimeterPrefab != null ? dosimeterPrefab.transform.localScale : Vector3.one) * sphereSize;
                Renderer colorRenderer = ResolveColorRenderer(go.transform);
                if (dosimeterPrefab == null && sphereMaterial != null && colorRenderer != null)
                    colorRenderer.sharedMaterial = sphereMaterial;

                // The receiver and its collider stay on the instance root so particle
                // intersections work even when only the visual mesh is nested.
                if (go.GetComponent<Collider>() == null) go.AddComponent<SphereCollider>();
                var d = go.GetComponent<RadiationDosimeter>();
                if (d == null) d = go.AddComponent<RadiationDosimeter>();
                d.Configure(doseToRed);
                dosimeters.Add(d);
                displayedDose.Add(0f);
                colorVelocity.Add(0f);
                renderers.Add(colorRenderer);
                PlaceOnDemoFloor(go.transform);
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
