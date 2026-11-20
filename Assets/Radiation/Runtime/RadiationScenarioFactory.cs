using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RadiationSimulation
{
    public static class RadiationScenarioFactory
    {
        private static readonly string[] SceneNames = {
            "Scenario_ConcreteAttenuation", "Scenario_MirrorMagnet", "Scenario_SecondaryEmission"
        };
        private static int pendingPreset = -1;

        public static void Load(int index)
        {
            if (index <= 0) { Time.timeScale = 1f; SceneManager.LoadScene("RadiationDemo"); return; }
            pendingPreset = Mathf.Clamp(index - 1, 0, SceneNames.Length - 1);
            SceneManager.sceneLoaded -= ConfigureLoadedScene;
            SceneManager.sceneLoaded += ConfigureLoadedScene;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneNames[pendingPreset]);
        }

        private static void ConfigureLoadedScene(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= ConfigureLoadedScene;
            if (pendingPreset == 0) ConfigureConcrete();
            else if (pendingPreset == 1) ConfigureMirror();
            else ConfigureSecondary();
            pendingPreset = -1;
        }

        private static RadiationSource MainSource()
        {
            var source = Find<RadiationSource>("Radiation Source - Cone");
            if (source == null) source = UnityEngine.Object.FindObjectOfType<RadiationSource>();
            return source;
        }

        private static void ConfigureConcrete()
        {
            var source = MainSource();
            if (source != null) { source.gameObject.SetActive(true); source.ConfigureRuntime(55f, 8f, 7f, .25f, 2f, 24f, RadiationFlowShape.Cone); }
            var barriers = new List<AttenuatingRadiationBarrier>();
            barriers.AddRange(UnityEngine.Object.FindObjectsOfType<AttenuatingRadiationBarrier>());
            if (barriers.Count == 0) barriers.Add(CreateBarrier("Concrete Thin", new Vector3(2f, 1.2f, 0.35f), new Vector3(2f, 1.2f, 0.35f), 1.0f));
            if (barriers.Count == 1) barriers.Add(CreateBarrier("Concrete Thick", new Vector3(4f, 1.2f, 0.35f), new Vector3(3f, 1.8f, 0.7f), 2.8f));
            for (int i = 0; i < barriers.Count; i++) { barriers[i].density = 1.0f + i * 1.2f; barriers[i].attenuationCoefficient = 1.4f; barriers[i].normalThickness = Mathf.Max(.1f, barriers[i].transform.localScale.z); }
            foreach (var mirror in UnityEngine.Object.FindObjectsOfType<RadiationMirror>()) mirror.gameObject.SetActive(false);
        }

        private static void ConfigureMirror()
        {
            var source = MainSource();
            if (source != null) { source.gameObject.SetActive(true); source.ConfigureRuntime(45f, 7f, 8f, .2f, 1.8f, 0f, RadiationFlowShape.Ray); }
            var mirror = Find<RadiationMirror>("Mirror");
            if (mirror == null) mirror = CreatePrimitive("Mirror", PrimitiveType.Cube, new Vector3(1.8f, 1.5f, .12f), new Vector3(2f, 1.5f, 1f)).AddComponent<RadiationMirror>();
            mirror.reflectivity = 1f; mirror.transform.rotation = Quaternion.Euler(0f, 35f, 0f); mirror.gameObject.SetActive(true);
            var root = new GameObject("Closed Radiation Box");
            CreateBoxPart(root, "Box Back", new Vector3(0f, 1.2f, 3f), new Vector3(5f, 2.5f, .18f));
            CreateBoxPart(root, "Box Left", new Vector3(-2.4f, 1.2f, 1.5f), new Vector3(.18f, 2.5f, 3f));
            CreateBoxPart(root, "Box Right", new Vector3(2.4f, 1.2f, 1.5f), new Vector3(.18f, 2.5f, 3f));
            CreateBoxPart(root, "Box Roof", new Vector3(0f, 2.4f, 1.5f), new Vector3(5f, .18f, 3f));
            var magnets = UnityEngine.Object.FindObjectsOfType<RadiationMagnet>();
            for (int i = 0; i < magnets.Length; i++) magnets[i].gameObject.SetActive(i == 0);
            for (int i = magnets.Length; i < 3; i++)
            {
                var go = new GameObject("Magnet " + (i + 1)); go.transform.position = new Vector3(-1.5f + i * 1.5f, 1f, 1.5f); go.AddComponent<RadiationMagnet>().enabled = (i == 0);
            }
        }

        private static void ConfigureSecondary()
        {
            var source = MainSource();
            if (source != null) { source.gameObject.SetActive(true); source.ConfigureRuntime(50f, 7f, 7f, .2f, 2f, 18f, RadiationFlowShape.Cone); }
            var critical = Find<CriticalRadiationObject>("Critical");
            if (critical == null)
            {
                var go = CreatePrimitive("Critical Graphite Rod", PrimitiveType.Cylinder, new Vector3(0f, 1f, 2.5f), new Vector3(.35f, 1.4f, .35f));
                critical = go.AddComponent<CriticalRadiationObject>();
            }
            var collider = critical.GetComponent<Collider>(); if (collider != null) collider.enabled = true;
            var activated = Find<RadiationSource>("Activated Source");
            if (activated == null) { var go = new GameObject("Activated Source"); activated = go.AddComponent<RadiationSource>(); }
            activated.gameObject.SetActive(false);
            SetPrivate(critical, "activatedSource", activated); SetPrivate(critical, "criticalDose", 7f);
            var reducer = Find<RadiationSpeedReducer>("Speed Reducer Volume");
            if (reducer == null) { var go = CreatePrimitive("Speed Reducer Gas", PrimitiveType.Cube, new Vector3(0f, 1f, 1.5f), new Vector3(4f, 2f, 3f)); reducer = go.AddComponent<RadiationSpeedReducer>(); }
            var box = reducer.GetComponent<BoxCollider>(); if (box != null) box.isTrigger = true; reducer.speedMultiplier = .3f; reducer.intensityLossPerUnit = .08f;
        }

        private static T Find<T>(string exactName) where T : Component
        {
            foreach (var c in UnityEngine.Object.FindObjectsOfType<T>()) if (c.name == exactName || c.transform.root.name == exactName) return c;
            return null;
        }
        private static GameObject CreatePrimitive(string name, PrimitiveType type, Vector3 position, Vector3 scale)
        { var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.position = position; go.transform.localScale = scale; return go; }
        private static GameObject CreateBoxPart(GameObject root, string name, Vector3 position, Vector3 scale)
        { var go = CreatePrimitive(name, PrimitiveType.Cube, position, scale); go.transform.SetParent(root.transform, true); var barrier = go.AddComponent<AttenuatingRadiationBarrier>(); barrier.density = 1f; barrier.attenuationCoefficient = 1f; barrier.normalThickness = Mathf.Max(.05f, scale.z); return go; }
        private static AttenuatingRadiationBarrier CreateBarrier(string name, Vector3 position, Vector3 scale, float density)
        { var go = CreatePrimitive(name, PrimitiveType.Cube, position, scale); var b = go.AddComponent<AttenuatingRadiationBarrier>(); b.density = density; return b; }
        private static void SetPrivate<T>(object target, string name, T value)
        { var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); if (f != null) f.SetValue(target, value); }
    }
}
