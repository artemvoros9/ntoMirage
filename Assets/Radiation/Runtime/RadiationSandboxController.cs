using System;
using System.Collections.Generic;
using Process = System.Diagnostics.Process;
using Debug = UnityEngine.Debug;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RadiationSimulation
{
    /// <summary>Runtime sandbox UI, selection and XZ dragging. Deliberately self-contained.</summary>
    public sealed class RadiationSandboxController : MonoBehaviour
    {
        private static readonly Color SelectionColor = new Color(0.15f, 0.9f, 1f, 0.95f);
        private Component selectedObject;
        private GameObject selectionMarker;
        private bool dragging, paused, showPanel = true;
        private bool interactionCollidersReady;
        private float simulationSpeed = 1f, dragDepth, height;
        private Vector3 dragOffset;
        private GUIStyle panelStyle, titleStyle, labelStyle, buttonStyle;
        private float fps, fpsTimer, frameMs = float.NaN, gpuMs = float.NaN;
        private int frames;
        private double lastCpuSeconds;
        private long lastCpuTicks;
        private float cpuPercent = float.NaN;
        private float timingTimer;
        private readonly FrameTiming[] frameTimings = new FrameTiming[1];
        private float sceneCountersTimer = 1f;
        private int sceneParticleCount, sceneColliderCount;

        public Component SelectedObject => selectedObject;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            lastCpuTicks = Process.GetCurrentProcess().TotalProcessorTime.Ticks;
            lastCpuSeconds = Time.realtimeSinceStartupAsDouble;
        }

        private void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; ClearMarker(); }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { selectedObject = null; ClearMarker(); dragging = false; }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            fpsTimer += dt; frames++;
            if (fpsTimer >= 0.25f) { fps = frames / fpsTimer; frames = 0; fpsTimer = 0f; }
            timingTimer += dt;
            if (timingTimer >= 0.5f) { UpdateTimings(timingTimer); timingTimer = 0f; }
            sceneCountersTimer += dt;
            if (sceneCountersTimer >= 1f)
            {
                sceneParticleCount = FindObjectsOfType<RadiationParticle>().Length;
                sceneColliderCount = FindObjectsOfType<Collider>().Length;
                sceneCountersTimer = 0f;
            }
            if (KeyTogglePanel()) showPanel = !showPanel;
            EnsureInteractionColliders();
            HandleSelectionAndDragging();
        }

        private void EnsureInteractionColliders()
        {
            if (interactionCollidersReady) return;
            var selectable = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var component in selectable)
            {
                if (!(component is RadiationDosimeter) && !(component is RadiationSource) && !(component is RadiationMagnet) && !(component is AttenuatingRadiationBarrier) && !(component is RadiationMirror) && !(component is RadiationSpeedReducer) && !(component is CriticalRadiationObject)) continue;
                if (component.GetComponentInChildren<Collider>(true) == null)
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marker.name = "Interaction Handle"; marker.transform.SetParent(component.transform, false); marker.transform.localScale = Vector3.one * .35f;
                    marker.GetComponent<Renderer>().enabled = false;
                }
            }
            interactionCollidersReady = true;
        }

        private void UpdateTimings(float interval)
        {
            frameMs = fps > 0.01f ? 1000f / fps : float.NaN;
            try
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, frameTimings) > 0)
                    gpuMs = frameTimings[0].gpuFrameTime > 0 ? (float)frameTimings[0].gpuFrameTime : float.NaN;
                else gpuMs = float.NaN;
            }
            catch { gpuMs = float.NaN; }
            try
            {
                Process p = Process.GetCurrentProcess();
                long nowTicks = p.TotalProcessorTime.Ticks;
                double now = Time.realtimeSinceStartupAsDouble;
                double wall = now - lastCpuSeconds;
                cpuPercent = wall > 0 ? (float)((nowTicks - lastCpuTicks) / (double)TimeSpan.TicksPerSecond / wall / Math.Max(1, Environment.ProcessorCount) * 100.0) : float.NaN;
                lastCpuTicks = nowTicks; lastCpuSeconds = now;
            }
            catch { cpuPercent = float.NaN; }
        }

        private void HandleSelectionAndDragging()
        {
            // IMGUI does not create an EventSystem pointer event. Use the actual panel rectangle,
            // otherwise a click on the panel can steal the drag state or block world selection.
            if (PointerOverPanel()) return;
            bool down = PointerDown();
            bool held = PointerHeld();
            if (down)
            {
                Camera cam = GetActiveCamera();
                if (cam == null) return;
                Ray ray = cam.ScreenPointToRay(PointerPosition());
                RaycastHit[] hits = Physics.RaycastAll(ray, 1000f, ~0, QueryTriggerInteraction.Collide);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                Component candidate = null;
                for (int i = 0; i < hits.Length; i++)
                {
                    if (hits[i].collider.GetComponentInParent<RadiationParticle>() != null) continue;
                    candidate = FindSelectable(hits[i].collider);
                    if (candidate != null) break;
                }
                if (candidate != null)
                {
                    Select(candidate);
                    Plane plane = new Plane(Vector3.up, candidate.transform.position);
                    if (plane.Raycast(ray, out dragDepth))
                    {
                        dragOffset = candidate.transform.position - ray.GetPoint(dragDepth);
                        dragging = true;
                        height = candidate.transform.position.y;
                    }
                }
            }
            if (!held) { dragging = false; return; }
            if (!dragging || selectedObject == null) return;
            Camera activeCamera = GetActiveCamera();
            if (activeCamera == null) return;
            Ray dragRay = activeCamera.ScreenPointToRay(PointerPosition());
            Plane dragPlane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (dragPlane.Raycast(dragRay, out float distance))
            {
                Vector3 p = dragRay.GetPoint(distance) + new Vector3(dragOffset.x, 0f, dragOffset.z);
                selectedObject.transform.position = new Vector3(p.x, height, p.z);
            }
        }

        private bool PointerOverPanel()
        {
            if (!showPanel) return false;
            Vector2 p = PointerPosition();
            // Screen coordinates have their origin at bottom-left; GUI areas use top-left.
            float panelHeight = Mathf.Max(30f, Screen.height - 24f);
            return p.x >= 12f && p.x <= 352f && p.y >= 24f && p.y <= Screen.height - 12f;
        }

        private static Camera GetActiveCamera()
        {
            Camera cam = Camera.main;
            if (cam != null && cam.isActiveAndEnabled) return cam;
            foreach (var candidate in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (candidate.isActiveAndEnabled) return candidate;
            return null;
        }

        private static Component FindSelectable(Collider c)
        {
            Type[] types = { typeof(RadiationDosimeter), typeof(RadiationSource), typeof(RadiationMagnet), typeof(AttenuatingRadiationBarrier), typeof(RadiationMirror), typeof(RadiationSpeedReducer), typeof(CriticalRadiationObject) };
            foreach (Type t in types) { Component x = c.GetComponentInParent(t); if (x != null) return x; }
            return null;
        }

        private static bool IsPointerOverUi()
        {
            if (EventSystem.current == null) return false;
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && EventSystem.current.IsPointerOverGameObject(Mouse.current.deviceId);
#else
            return EventSystem.current.IsPointerOverGameObject();
#endif
        }
        private static Vector2 PointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }
        private static bool KeyTogglePanel()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.F1);
#endif
        }

        private static bool PointerDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }
        private static bool PointerHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        private void Select(Component component)
        {
            if (selectedObject == component) return;
            selectedObject = component; height = component.transform.position.y;
            ClearMarker();
            // Object selection remains functional without a visual outline.
        }
        private void ClearMarker() { if (selectionMarker != null) Destroy(selectionMarker); selectionMarker = null; }

        private void OnGUI()
        {
            if (panelStyle == null) BuildStyles();
            if (!showPanel) { if (GUI.Button(new Rect(12, 12, 120, 28), "Показать UI", buttonStyle)) showPanel = true; return; }
            GUILayout.BeginArea(new Rect(12, 12, 340, Screen.height - 24), panelStyle);
            GUILayout.Label("RADIATION SANDBOX", titleStyle);
            GUILayout.Label($"FPS {fps:0} | Frame {FormatMs(frameMs)} | GPU {FormatMs(gpuMs)}\nCPU {FormatPercent(cpuPercent)} | Частицы {sceneParticleCount} | Коллайдеры {sceneColliderCount}", labelStyle);
            bool newPaused = GUILayout.Toggle(paused, paused ? "▶  Продолжить" : "Ⅱ  Пауза", buttonStyle);
            if (newPaused != paused) { paused = newPaused; Time.timeScale = paused ? 0f : simulationSpeed; }
            GUILayout.Label($"Скорость: {simulationSpeed:0.00}x", labelStyle);
            float speed = GUILayout.HorizontalSlider(simulationSpeed, 0.1f, 3f);
            if (!Mathf.Approximately(speed, simulationSpeed)) { simulationSpeed = speed; if (!paused) Time.timeScale = simulationSpeed; }
            if (GUILayout.Button("Original — перезагрузить сцену", buttonStyle)) { Time.timeScale = 1f; SceneManager.LoadScene(SceneManager.GetActiveScene().name); }
            DrawScenarioButtons();
            if (GUILayout.Button("Скрыть панель", buttonStyle)) showPanel = false;
            GUILayout.Space(8); DrawSelectedInspector();
            GUILayout.EndArea();
        }

        private static string FormatMs(float v) => float.IsNaN(v) ? "N/A" : v.ToString("0.0") + " ms";
        private static string FormatPercent(float v) => float.IsNaN(v) ? "N/A" : v.ToString("0.0") + "%";

        private void DrawScenarioButtons()
        {
            GUILayout.Label("СЦЕНАРИИ", titleStyle);
            for (int i = 0; i < 4; i++) if (GUILayout.Button(i == 0 ? "Сценарий 0" : "Сценарий " + i, buttonStyle)) InvokeFactoryLoad(i);
        }

        private static void InvokeFactoryLoad(int index)
        {
            Type t = Type.GetType("RadiationSimulation.RadiationScenarioFactory") ?? Type.GetType("RadiationScenarioFactory");
            MethodInfo m = t?.GetMethod("Load", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
            if (m != null) m.Invoke(null, new object[] { index });
            else Debug.LogWarning("RadiationScenarioFactory.Load(int) не найден.");
        }

        private void DrawSelectedInspector()
        {
            if (selectedObject == null) { GUILayout.Label("Кликните по объекту для выбора и перетаскивания.", labelStyle); return; }
            GUILayout.Label("ВЫБРАНО: " + selectedObject.name, titleStyle);
            height = GUILayout.HorizontalSlider(height, -10f, 10f); selectedObject.transform.position = new Vector3(selectedObject.transform.position.x, height, selectedObject.transform.position.z);
            GUILayout.Label($"Высота: {height:0.00} | Поворот Y: {selectedObject.transform.eulerAngles.y:0}°", labelStyle);
            float rot = GUILayout.HorizontalSlider(selectedObject.transform.eulerAngles.y, 0f, 360f); selectedObject.transform.rotation = Quaternion.Euler(0f, rot, 0f);
            if (selectedObject is RadiationDosimeter dosimeter)
            {
                GUILayout.Label($"Дозиметр | доза {dosimeter.Dose:0.00} / {dosimeter.DoseToRed:0.00}", labelStyle);
                if (GUILayout.Button("Сбросить дозу", buttonStyle)) dosimeter.ResetDose();
            }
            else if (selectedObject is RadiationSource source) DrawSource(source);
            else if (selectedObject is RadiationMagnet magnet) DrawMagnet(magnet);
            else if (selectedObject is AttenuatingRadiationBarrier barrier) { GUILayout.Label("Плотность", labelStyle); barrier.density = GUILayout.HorizontalSlider(barrier.density, 0f, 10f); }
            else if (selectedObject is RadiationMirror mirror) mirror.reflectivity = GUILayout.HorizontalSlider(mirror.reflectivity, 0f, 1f);
            else if (selectedObject is RadiationSpeedReducer reducer) reducer.speedMultiplier = GUILayout.HorizontalSlider(reducer.speedMultiplier, 0f, 1f);
        }

        private void DrawSource(RadiationSource s)
        {
            GUILayout.Label($"Источник | active {s.ActiveParticleCount} / {s.MaxParticles}", labelStyle);
            float rate = GetFloat(s, "EmissionRate", 40f), speed = GetFloat(s, "ParticleSpeed", 8f), life = GetFloat(s, "Lifetime", 6f), variation = GetFloat(s, "LifetimeVariation", .25f), cone = GetFloat(s, "ConeAngle", 30f);
            GUILayout.Label($"Rate {rate:0.0}  Speed {speed:0.0}  Life {life:0.0}", labelStyle);
            if (GUILayout.Button("Конус", buttonStyle)) s.SetEmission(rate, s.InitialIntensity, RadiationFlowShape.Cone);
            if (GUILayout.Button("Луч", buttonStyle)) s.SetEmission(rate, s.InitialIntensity, RadiationFlowShape.Ray);
            if (GUILayout.Button("Сфера", buttonStyle)) s.SetEmission(rate, s.InitialIntensity, RadiationFlowShape.Omnidirectional);
            GUILayout.Label($"Variation {variation:0.00}  Cone {cone:0}°", labelStyle);
        }
        private void DrawMagnet(RadiationMagnet m) { GUILayout.Label($"Магнит | radius {m.Radius:0.0} | strength {m.Strength:0.0} | {m.Polarity}", labelStyle); }

        private static float GetFloat(object o, string name, float fallback) { PropertyInfo p = o.GetType().GetProperty(name); return p != null && p.PropertyType == typeof(float) ? (float)p.GetValue(o) : fallback; }
        private static int GetInt(object o, string name, int fallback) { PropertyInfo p = o.GetType().GetProperty(name); return p != null && p.PropertyType == typeof(int) ? (int)p.GetValue(o) : fallback; }

        private void BuildStyles()
        {
            panelStyle = new GUIStyle(GUI.skin.box) { normal = { background = MakeTexture(new Color(.025f, .05f, .08f, .94f)) }, padding = new RectOffset(14, 14, 12, 12) };
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = SelectionColor } };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = Color.white } };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, margin = new RectOffset(0, 0, 2, 2) };
        }
        private static Texture2D MakeTexture(Color color) { Texture2D t = new Texture2D(1, 1); t.SetPixel(0, 0, color); t.Apply(); return t; }
    }

    internal static class SelectionHighlight
    {
        public static GameObject Create(GameObject target, Color color)
        {
            GameObject marker = new GameObject("Selection Highlight"); marker.hideFlags = HideFlags.DontSave; marker.transform.SetParent(target.transform, false); LineRenderer line = marker.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.loop = true; line.widthMultiplier = .025f; line.material = new Material(Shader.Find("Sprites/Default")); line.startColor = color; line.endColor = color;
            Renderer[] rs = target.GetComponentsInChildren<Renderer>(); Bounds b = new Bounds(target.transform.position, Vector3.one * .1f); bool found = false;
            foreach (Renderer r in rs) { if (!found) { b = r.bounds; found = true; } else b.Encapsulate(r.bounds); }
            Vector3 min = target.transform.InverseTransformPoint(b.min), max = target.transform.InverseTransformPoint(b.max); line.positionCount = 8; line.SetPositions(new[] { new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z), new Vector3(max.x,min.y,max.z), new Vector3(min.x,min.y,max.z), new Vector3(min.x,max.y,min.z), new Vector3(max.x,max.y,min.z), new Vector3(max.x,max.y,max.z), new Vector3(min.x,max.y,max.z) });
            return marker;
        }
    }

    /// <summary>Creates a useful default showcase when a scene has radiation sources but no configured field.</summary>
    public static class RadiationFeatureBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateDefaults()
        {
            if (UnityEngine.Object.FindObjectOfType<RadiationSandboxController>() == null)
            {
                var controller = new GameObject("Radiation Sandbox Controller");
                controller.AddComponent<RadiationSandboxController>();
            }

            if (UnityEngine.Object.FindObjectOfType<RadiationSource>() == null || UnityEngine.Object.FindObjectOfType<RadiationDosimeterField>() != null) return;
            var fieldObject = new GameObject("Radiation Dosimeter Field");
            fieldObject.AddComponent<RadiationDosimeterField>();

            // Critical objects and their inactive local sources are authored directly in scenes/prefabs.
        }
    }
}
