using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RadiationSimulation
{
    /// <summary>Силуэтная обводка реальной геометрии выбранной модели.</summary>
    internal static class SelectionHighlight
    {
        private const string ShaderName = "Radiation/Selection Outline";
        private const float OutlinePixels = 3f;

        public static GameObject Create(GameObject requestedTarget, Color color)
        {
            GameObject target = ResolveVisualTarget(requestedTarget);
            GameObject marker = new GameObject("Selection Outline") { hideFlags = HideFlags.DontSave };

            Shader shader = Shader.Find(ShaderName) ?? Resources.Load<Shader>("SelectionOutline");
            Material material = shader != null ? new Material(shader) { hideFlags = HideFlags.DontSave } : null;
            if (material != null)
            {
                material.SetColor("_OutlineColor", color);
                material.SetFloat("_OutlineWidth", OutlinePixels);
            }

            var parts = new List<SelectionOutlinePartData>();
            if (material != null)
            {
                foreach (Renderer source in target.GetComponentsInChildren<Renderer>(true))
                {
                    if (!source.enabled || source.GetComponent<SelectionOutlinePart>() != null) continue;
                    if (source is ParticleSystemRenderer || source is TrailRenderer || source is LineRenderer) continue;

                    Mesh mesh = null;
                    SkinnedMeshRenderer skinned = source as SkinnedMeshRenderer;
                    if (source is MeshRenderer)
                    {
                        MeshFilter filter = source.GetComponent<MeshFilter>();
                        mesh = filter != null ? filter.sharedMesh : null;
                    }
                    else if (skinned != null)
                    {
                        mesh = new Mesh { name = source.name + " Selection Mesh" };
                        skinned.BakeMesh(mesh);
                    }
                    if (mesh == null) continue;

                    // Копия является дочерним объектом исходного меша. Поэтому
                    // сохраняются повороты/масштабы всех FBX-предков без lossyScale.
                    GameObject copy = new GameObject(source.name + " Selection Outline") { hideFlags = HideFlags.DontSave };
                    copy.transform.SetParent(source.transform, false);
                    copy.transform.localPosition = Vector3.zero;
                    copy.transform.localRotation = Quaternion.identity;
                    copy.transform.localScale = Vector3.one;
                    copy.AddComponent<SelectionOutlinePart>();

                    MeshFilter copyFilter = copy.AddComponent<MeshFilter>();
                    copyFilter.sharedMesh = mesh;
                    MeshRenderer copyRenderer = copy.AddComponent<MeshRenderer>();
                    // FBX-модель может содержать несколько submesh. Назначаем
                    // outline-материал каждому слоту, чтобы контур не пропадал
                    // на отдельных частях модели.
                    int materialSlots = Mathf.Max(1, mesh.subMeshCount);
                    var outlineMaterials = new Material[materialSlots];
                    for (int slot = 0; slot < outlineMaterials.Length; slot++)
                        outlineMaterials[slot] = material;
                    copyRenderer.sharedMaterials = outlineMaterials;
                    copyRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    copyRenderer.receiveShadows = false;
                    copyRenderer.lightProbeUsage = LightProbeUsage.Off;
                    copyRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    parts.Add(new SelectionOutlinePartData(source, skinned, mesh, copy, copyRenderer));
                }
            }

            var follower = marker.AddComponent<SelectionOutlineFollower>();
            follower.Init(requestedTarget.transform, material, parts.ToArray());
            return marker;
        }

        private static GameObject ResolveVisualTarget(GameObject requested)
        {
            if (requested == null) return null;
            if (HasVisualRenderer(requested)) return requested;

            // RadiationSource в демо — пустой дочерний Transform, а FBX-меш
            // находится у ближайшего родителя.
            for (Transform parent = requested.transform.parent; parent != null; parent = parent.parent)
                if (HasVisualRenderer(parent.gameObject)) return parent.gameObject;
            return requested;
        }

        private static bool HasVisualRenderer(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponent<SelectionOutlinePart>() != null) continue;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (r.enabled) return true;
            }
            return false;
        }
    }

    internal sealed class SelectionOutlinePart : MonoBehaviour { }

    internal sealed class SelectionOutlinePartData
    {
        public readonly Renderer Source;
        public readonly SkinnedMeshRenderer Skinned;
        public readonly Mesh Mesh;
        public readonly GameObject Copy;
        public readonly MeshRenderer CopyRenderer;

        public SelectionOutlinePartData(Renderer source, SkinnedMeshRenderer skinned, Mesh mesh, GameObject copy, MeshRenderer copyRenderer)
        {
            Source = source;
            Skinned = skinned;
            Mesh = mesh;
            Copy = copy;
            CopyRenderer = copyRenderer;
        }
    }

    internal sealed class SelectionOutlineFollower : MonoBehaviour
    {
        private Transform target;
        private Material material;
        private SelectionOutlinePartData[] parts;

        public void Init(Transform targetTransform, Material outlineMaterial, SelectionOutlinePartData[] outlineParts)
        {
            target = targetTransform;
            material = outlineMaterial;
            parts = outlineParts;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                Destroy(gameObject);
                return;
            }
            if (parts == null) return;
            foreach (SelectionOutlinePartData part in parts)
            {
                if (part == null || part.Copy == null) continue;
                bool visible = part.Source != null && part.Source.enabled && part.Source.gameObject.activeInHierarchy;
                if (part.Copy.activeSelf != visible) part.Copy.SetActive(visible);
                if (!visible) continue;
                if (part.Skinned != null)
                    part.Skinned.BakeMesh(part.Mesh);
            }
        }

        private void OnDestroy()
        {
            if (parts != null)
            {
                foreach (SelectionOutlinePartData part in parts)
                {
                    if (part == null) continue;
                    if (part.Copy != null) Destroy(part.Copy);
                    if (part.Skinned != null && part.Mesh != null) Destroy(part.Mesh);
                }
            }
            if (material != null) Destroy(material);
        }
    }
}
