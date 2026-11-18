using System.Collections.Generic;
using UnityEngine;

namespace RadiationSimulation
{
    /// <summary>Configurable grid of physical dose-monitoring spheres.</summary>
    public sealed class RadiationDosimeterField : MonoBehaviour
    {
        [SerializeField, Min(1)] private int count = 25;
        [SerializeField, Min(0.01f)] private float sphereSize = 0.18f;
        [SerializeField, Min(0.01f)] private float spacing = 0.75f;
        [SerializeField] private Vector3 gridCenter;
        [SerializeField] private bool useGrid = true;
        [SerializeField, Min(0.0001f)] private float doseToRed = 10f;
        [SerializeField, Min(0.01f)] private float colorSmoothTime = 0.25f;
        [SerializeField] private Gradient doseGradient;
        [SerializeField] private Material sphereMaterial;
        private readonly List<RadiationDosimeter> dosimeters = new();
        private readonly List<float> displayedDose = new();
        private readonly List<float> colorVelocity = new();
        private readonly List<Renderer> renderers = new();
        public int Count => count;
        public float SphereSize => sphereSize;
        public float Spacing => spacing;
        public void Configure(int newCount, float newSize, float newSpacing) { count = Mathf.Max(1,newCount); sphereSize = Mathf.Max(.01f,newSize); spacing = Mathf.Max(.01f,newSpacing); Rebuild(); }
        private void Reset()
        {
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(.05f,.25f,1f),0f), new GradientColorKey(Color.cyan,.3f), new GradientColorKey(Color.yellow,.65f), new GradientColorKey(Color.red,1f) }, new[] { new GradientAlphaKey(1f,0f), new GradientAlphaKey(1f,1f) }); doseGradient = g;
        }
        private void Start() => Rebuild();
        private void Update()
        {
            for (int i=0; i<dosimeters.Count; i++)
            {
                if (dosimeters[i] == null || renderers[i] == null) continue;
                float normalized = Mathf.Clamp01(dosimeters[i].Dose / doseToRed);
                float velocity = colorVelocity[i];
                displayedDose[i] = Mathf.SmoothDamp(displayedDose[i], normalized, ref velocity, colorSmoothTime); colorVelocity[i] = velocity;
                Color color = doseGradient != null ? doseGradient.Evaluate(displayedDose[i]) : Color.Lerp(Color.blue, Color.red, displayedDose[i]);
                var block = new MaterialPropertyBlock(); renderers[i].GetPropertyBlock(block); block.SetColor("_Color", color); block.SetColor("_BaseColor", color); block.SetColor("_EmissionColor", color * (.15f + displayedDose[i]*1.5f)); renderers[i].SetPropertyBlock(block);
            }
        }
        public void ResetDose() { foreach (var d in dosimeters) if (d != null) d.ResetDose(); }
        public void Rebuild()
        {
            for (int i=transform.childCount-1; i>=0; i--) Destroy(transform.GetChild(i).gameObject);
            dosimeters.Clear(); displayedDose.Clear(); colorVelocity.Clear(); renderers.Clear();
            int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            for (int i=0; i<count; i++)
            {
                int x=i%columns, z=i/columns; var go=GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name=$"Dosimeter_{i:00}"; go.transform.SetParent(transform,false);
                go.transform.localPosition=useGrid ? gridCenter+new Vector3((x-(columns-1)*.5f)*spacing,0f,(z-(columns-1)*.5f)*spacing) : gridCenter; go.transform.localScale=Vector3.one*sphereSize;
                if (sphereMaterial != null) go.GetComponent<Renderer>().sharedMaterial=sphereMaterial;
                var d=go.AddComponent<RadiationDosimeter>(); d.Configure(doseToRed); dosimeters.Add(d); displayedDose.Add(0f); colorVelocity.Add(0f); renderers.Add(go.GetComponent<Renderer>());
            }
        }
        private void OnValidate() { count=Mathf.Max(1,count); sphereSize=Mathf.Max(.01f,sphereSize); spacing=Mathf.Max(.01f,spacing); doseToRed=Mathf.Max(.0001f,doseToRed); }
    }
}
