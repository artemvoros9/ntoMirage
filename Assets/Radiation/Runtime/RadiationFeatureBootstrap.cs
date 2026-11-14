using UnityEngine;

namespace RadiationSimulation
{
    /// <summary>Creates a useful default showcase when a scene has radiation sources but no configured field.</summary>
    public static class RadiationFeatureBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateDefaults()
        {
            if (Object.FindObjectOfType<RadiationSource>() == null || Object.FindObjectOfType<RadiationDosimeterField>() != null) return;

            var fieldObject = new GameObject("Radiation Dosimeter Field");
            fieldObject.AddComponent<RadiationDosimeterField>();

            // Critical objects and their inactive local sources are authored directly in scenes/prefabs.
            // Runtime bootstrap only creates the dosimeter field and never spawns radiation objects.
        }
    }
}
