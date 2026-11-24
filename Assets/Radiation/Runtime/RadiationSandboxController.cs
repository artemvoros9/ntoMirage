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
        private static RadiationSandboxController instance;
        private Component selectedObject;
        private GameObject selectionMarker;
        private bool dragging, paused, showPanel = true;
        private bool interactionCollidersReady;
        private bool cameraInitialized;
        private Vector3 cameraPivot;
        private float cameraDistance, cameraYaw, cameraPitch;
        private Vector2 previousPointer;
        private float simulationSpeed = 1f, dragDepth, height;
        private Vector3 dragOffset;
        private Vector2 scrollPos;
        private GUIStyle panelStyle, cardStyle, titleStyle, sectionStyle, labelStyle, mutedStyle;
        private GUIStyle buttonStyle, activeButtonStyle, sliderStyle, sliderThumbStyle, scrollbarStyle, scrollbarThumbStyle;
        private Texture2D panelBackgroundTex;
        private readonly List<Texture2D> guiTextures = new();
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
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            lastCpuTicks = Process.GetCurrentProcess().TotalProcessorTime.Ticks;
            lastCpuSeconds = Time.realtimeSinceStartupAsDouble;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ClearMarker();
            ClearGuiTextures();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            selectedObject = null;
            ClearMarker();
            dragging = false;
            paused = false;
            interactionCollidersReady = false;
            cameraInitialized = false;
            Time.timeScale = simulationSpeed;
            EnsureDosimeterFieldForDemoScene();
        }

        private static void EnsureDosimeterFieldForDemoScene()
        {
            if (FindObjectOfType<RadiationDosimeterField>() != null) return;
            GameObject floor = GameObject.Find("Demo Floor");
            if (floor == null) return;
            Bounds bounds = new Bounds(floor.transform.position, Vector3.one);
            Renderer floorRenderer = floor.GetComponent<Renderer>();
            if (floorRenderer != null) bounds = floorRenderer.bounds;
            else
            {
                Collider floorCollider = floor.GetComponent<Collider>();
                if (floorCollider != null) bounds = floorCollider.bounds;
            }
            GameObject fieldObject = new GameObject("Radiation Dosimeter 3D Grid");
            RadiationDosimeterField field = fieldObject.AddComponent<RadiationDosimeterField>();
            field.ConfigureGrid(new Vector3(Mathf.Max(1f, bounds.size.x - 2f), .01f, Mathf.Max(1f, bounds.size.z - 2f)),
                new Vector3(bounds.center.x, bounds.max.y + .12f, bounds.center.z), 2f, .22f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            fpsTimer += dt; frames++;
            if (fpsTimer >= 0.25f)
            {
                fps = frames / fpsTimer;
                frames = 0;
                fpsTimer = 0f;
            }
            timingTimer += dt;
            if (timingTimer >= 0.5f)
            {
                UpdateTimings(timingTimer);
                timingTimer = 0f;
            }
            sceneCountersTimer += dt;
            if (sceneCountersTimer >= 1f)
            {
                sceneParticleCount = FindObjectsOfType<RadiationParticle>().Length;
                sceneColliderCount = FindObjectsOfType<Collider>().Length;
                sceneCountersTimer = 0f;
            }
            if (KeyTogglePanel()) showPanel = !showPanel;
            EnsureInteractionColliders();
            HandleCameraNavigation();
            HandleSelectionAndDragging();
        }

        private void EnsureInteractionColliders()
        {
            if (interactionCollidersReady) return;
            var selectable = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var component in selectable)
            {
                if (!(component is RadiationDosimeter) && !(component is RadiationDosimeterField) && !(component is RadiationSource) && !(component is RadiationMagnet) && !(component is AttenuatingRadiationBarrier) && !(component is RadiationMirror) && !(component is RadiationSpeedReducer) && !(component is CriticalRadiationObject)) continue;
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

        private void HandleCameraNavigation()
        {
            Camera cam = GetActiveCamera();
            if (cam == null || PointerOverPanel()) return;
            if (!cameraInitialized)
            {
                cameraPivot = cam.transform.position + cam.transform.forward * 8f;
                cameraDistance = Mathf.Max(1f, Vector3.Distance(cam.transform.position, cameraPivot));
                Vector3 angles = cam.transform.eulerAngles; cameraYaw = angles.y; cameraPitch = angles.x;
                cameraInitialized = true;
            }
            Vector2 pointer = PointerPosition();
            Vector2 delta = pointer - previousPointer;
            previousPointer = pointer;
            bool middle = MiddleHeld();
            if (middle)
            {
                bool shift = ShiftHeld();
                if (shift)
                {
                    Vector3 right = cam.transform.right, up = cam.transform.up;
                    float pan = cameraDistance * 0.0025f;
                    cameraPivot -= (right * delta.x + up * delta.y) * pan;
                }
                else
                {
                    cameraYaw += delta.x * 0.35f;
                    cameraPitch = Mathf.Clamp(cameraPitch - delta.y * 0.35f, -89f, 89f);
                }
            }
            float wheel = MouseWheel();
            if (Mathf.Abs(wheel) > 0.001f) cameraDistance = Mathf.Clamp(cameraDistance * Mathf.Pow(0.8f, wheel), 0.5f, 200f);
            Quaternion rotation = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
            cam.transform.SetPositionAndRotation(cameraPivot - rotation * Vector3.forward * cameraDistance, rotation);
        }

        private static bool MiddleHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.middleButton.isPressed;
#else
            return Input.GetMouseButton(2);
#endif
        }
        private static bool ShiftHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
#else
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        }
        private static float MouseWheel()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return 0f;
            // Input System versions/devices may report either wheel ticks (1) or
            // legacy-style units (120). Normalize both to a single tick value.
            float value = Mouse.current.scroll.ReadValue().y;
            return Mathf.Abs(value) > 10f ? value / 120f : value;
#else
            return Input.mouseScrollDelta.y;
#endif
        }

        private void HandleSelectionAndDragging()
        {
            // IMGUI does not create an EventSystem pointer event. Use the actual panel rectangle,
            // otherwise a click on the panel can steal the drag state or block world selection.
            if (PointerOverPanel()) return;
            bool down = PointerDown();
            bool held = PointerHeld();
            if (MiddleHeld())
            {
                dragging = false;
                return;
            }
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
            if (!held)
            {
                dragging = false;
                return;
            }
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
            Vector2 p = PointerPosition();
            if (!showPanel)
                return p.x >= 12f && p.x <= 132f && p.y >= Screen.height - 40f && p.y <= Screen.height - 12f;
            return p.x >= 12f && p.x <= 352f && p.y >= 12f && p.y <= Screen.height - 12f;
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
            Type[] types = { typeof(RadiationDosimeter), typeof(RadiationDosimeterField), typeof(RadiationSource), typeof(RadiationMagnet), typeof(AttenuatingRadiationBarrier), typeof(RadiationMirror), typeof(RadiationSpeedReducer), typeof(CriticalRadiationObject) };
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
            if (selectedObject != null) selectionMarker = SelectionHighlight.Create(selectedObject.gameObject, SelectionColor);
        }
        private void ClearMarker()
        {
            if (selectionMarker != null)
            {
                Destroy(selectionMarker);
            }

            selectionMarker = null;
        }
        private void RefreshSelectionMarker()
        {
            if (selectedObject == null) return;
            ClearMarker();
            selectionMarker = SelectionHighlight.Create(selectedObject.gameObject, SelectionColor);
        }

        private void OnGUI()
        {
            EnsureStyles();
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;

            if (!showPanel)
            {
                if (GUI.Button(new Rect(12, 12, 120, 28), "Показать UI", buttonStyle)) showPanel = true;
                return;
            }

            GUILayout.BeginArea(new Rect(12, 12, 340, Mathf.Max(120f, Screen.height - 24f)), panelStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("RADIATION SANDBOX", titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Скрыть", buttonStyle, GUILayout.Width(72f))) showPanel = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("Сцена: " + SceneManager.GetActiveScene().name, mutedStyle);
            GUILayout.Space(4);

            GUIStyle prevThumb = GUI.skin.verticalScrollbarThumb;
            GUI.skin.verticalScrollbarThumb = scrollbarThumbStyle;
            scrollPos = GUILayout.BeginScrollView(scrollPos, false, false, GUIStyle.none, scrollbarStyle);

            GUILayout.BeginVertical(cardStyle);
            GUILayout.Label($"FPS {fps:0}  |  Frame {FormatMs(frameMs)}  |  GPU {FormatMs(gpuMs)}", labelStyle);
            GUILayout.Label($"CPU {FormatPercent(cpuPercent)}  |  Частицы {sceneParticleCount}  |  Коллайдеры {sceneColliderCount}", mutedStyle);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(cardStyle);
            GUILayout.Label("СИМУЛЯЦИЯ", sectionStyle);
            bool newPaused = GUILayout.Toggle(paused, paused ? "▶  Продолжить" : "Ⅱ  Пауза", buttonStyle);
            if (newPaused != paused)
            {
                paused = newPaused;
                Time.timeScale = paused ? 0f : simulationSpeed;
            }
            GUILayout.Space(4);
            GUILayout.Label($"Скорость: {simulationSpeed:0.00}x", labelStyle);
            float speed = GUILayout.HorizontalSlider(simulationSpeed, 0.1f, 3f, sliderStyle, sliderThumbStyle);
            if (!Mathf.Approximately(speed, simulationSpeed))
            {
                simulationSpeed = speed;

                if (!paused)
                {
                    Time.timeScale = simulationSpeed;
                }
            }
            GUILayout.Space(4);
            if (GUILayout.Button("Перезагрузить сцену", buttonStyle))
            {
                simulationSpeed = 1f;
                paused = false;
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
            GUILayout.EndVertical();

            DrawScenarioButtons();
            DrawDosimeterFieldMenu();
            DrawSelectedInspector();

            GUILayout.EndScrollView();
            GUI.skin.verticalScrollbarThumb = prevThumb;
            GUILayout.EndArea();
        }

        private void DrawDosimeterFieldMenu()
        {
            RadiationDosimeterField field = FindObjectOfType<RadiationDosimeterField>();
            if (field == null) return;
            GUILayout.BeginVertical(cardStyle);
            GUILayout.Label("ДОЗИМЕТРЫ НА DEMO FLOOR", sectionStyle);
            GUILayout.Label($"Сфер: {field.Count} | Размер: {field.SphereSize:0.00} | Расстояние: {field.Spacing:0.00}", labelStyle);
            int count = Mathf.RoundToInt(GUILayout.HorizontalSlider(field.DosimeterCount, 1f, 1000f, sliderStyle, sliderThumbStyle));
            GUILayout.Label($"Количество: {count}", mutedStyle);
            float size = GUILayout.HorizontalSlider(field.SphereSize, .01f, 2f, sliderStyle, sliderThumbStyle);
            GUILayout.Label($"Размер сферы: {size:0.00}", mutedStyle);
            float spacing = GUILayout.HorizontalSlider(field.Spacing, .05f, 10f, sliderStyle, sliderThumbStyle);
            GUILayout.Label($"Расстояние между сферами: {spacing:0.00}", mutedStyle);
            if (count != field.DosimeterCount || !Mathf.Approximately(size, field.SphereSize) || !Mathf.Approximately(spacing, field.Spacing)) field.Configure(count, size, spacing);
            if (GUILayout.Button("Перестроить дозиметры на полу", buttonStyle)) field.Rebuild();
            GUILayout.EndVertical();
        }

        private static string FormatMs(float v) => float.IsNaN(v) ? "N/A" : v.ToString("0.0") + " ms";
        private static string FormatPercent(float v) => float.IsNaN(v) ? "N/A" : v.ToString("0.0") + "%";

        private void DrawScenarioButtons()
        {
            string activeScene = SceneManager.GetActiveScene().name;
            GUILayout.BeginVertical(cardStyle);
            GUILayout.Label("СЦЕНАРИИ", sectionStyle);
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 2; col++)
                {
                    int i = row * 2 + col;
                    GUIStyle style = IsScenarioScene(activeScene, i) ? activeButtonStyle : buttonStyle;
                    string[] names = { "Бетон и затухание", "Коробка: зеркало + магнит", "Вторичная эмиссия" };
                    if (GUILayout.Button(i == 0 ? "Базовая сцена" : names[i - 1], style))
                    {
                        LoadScenarioScene(i);
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private static bool IsScenarioScene(string sceneName, int index) => index switch
        {
            0 => sceneName == "RadiationDemo",
            1 => sceneName == "Scenario_ConcreteAttenuation",
            2 => sceneName == "Scenario_MirrorMagnet",
            3 => sceneName == "Scenario_SecondaryEmission",
            _ => false
        };

        private static void LoadScenarioScene(int index)
        {
            string[] sceneNames =
            {
                "RadiationDemo",
                "Scenario_ConcreteAttenuation",
                "Scenario_MirrorMagnet",
                "Scenario_SecondaryEmission"
            };

            if (index < 0 || index >= sceneNames.Length)
            {
                return;
            }

            SceneManager.LoadScene(sceneNames[index]);
        }

        private void DrawSelectedInspector()
        {
            GUILayout.BeginVertical(cardStyle);
            if (selectedObject == null)
            {
                GUILayout.Label("ИНСПЕКТОР", sectionStyle);
                GUILayout.Label("Кликните по объекту для выбора и перетаскивания.", mutedStyle);
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label("ВЫБРАНО: " + selectedObject.name, sectionStyle);
            GUILayout.Label($"Высота: {height:0.00}", labelStyle);
            height = GUILayout.HorizontalSlider(height, -10f, 10f, sliderStyle, sliderThumbStyle);
            selectedObject.transform.position = new Vector3(selectedObject.transform.position.x, height, selectedObject.transform.position.z);

            GUILayout.Label($"Поворот Y: {selectedObject.transform.eulerAngles.y:0}°", labelStyle);
            float rot = GUILayout.HorizontalSlider(selectedObject.transform.eulerAngles.y, 0f, 360f, sliderStyle, sliderThumbStyle);
            selectedObject.transform.rotation = Quaternion.Euler(0f, rot, 0f);

            Vector3 scale = selectedObject.transform.localScale;
            GUILayout.Label($"Размер: X {scale.x:0.00} | Y {scale.y:0.00} | Z {scale.z:0.00}", labelStyle);
            float sx = GUILayout.HorizontalSlider(scale.x, 0.05f, 10f, sliderStyle, sliderThumbStyle);
            float sy = GUILayout.HorizontalSlider(scale.y, 0.05f, 10f, sliderStyle, sliderThumbStyle);
            float sz = GUILayout.HorizontalSlider(scale.z, 0.05f, 10f, sliderStyle, sliderThumbStyle);
            if (!Mathf.Approximately(sx, scale.x) || !Mathf.Approximately(sy, scale.y) || !Mathf.Approximately(sz, scale.z))
            {
                selectedObject.transform.localScale = new Vector3(sx, sy, sz);
                RefreshSelectionMarker();
            }

            GUILayout.Space(4);
            if (selectedObject is RadiationDosimeter dosimeter)
            {
                GUILayout.Label($"Дозиметр | доза {dosimeter.Dose:0.00} / {dosimeter.DoseToRed:0.00}", labelStyle);
                float doseLimit = GUILayout.HorizontalSlider(dosimeter.DoseToRed, 0.01f, 100f, sliderStyle, sliderThumbStyle);
                bool selectable = GUILayout.Toggle(dosimeter.Selectable, "Доступен для выбора", buttonStyle);
                if (!Mathf.Approximately(doseLimit, dosimeter.DoseToRed) || selectable != dosimeter.Selectable) dosimeter.Configure(doseLimit, selectable);
                if (GUILayout.Button("Сбросить дозу", buttonStyle)) dosimeter.ResetDose();
            }
            else if (selectedObject is RadiationDosimeterField field) DrawField(field);
            else if (selectedObject is RadiationSource source) DrawSource(source);
            else if (selectedObject is RadiationMagnet magnet) DrawMagnet(magnet);
            else if (selectedObject is AttenuatingRadiationBarrier barrier)
            {
                GUILayout.Label($"Плотность: {barrier.density:0.00}", labelStyle);
                barrier.density = GUILayout.HorizontalSlider(barrier.density, 0f, 10f, sliderStyle, sliderThumbStyle);
                GUILayout.Label($"Коэффициент ослабления: {barrier.attenuationCoefficient:0.00}", mutedStyle);
                barrier.attenuationCoefficient = GUILayout.HorizontalSlider(barrier.attenuationCoefficient, 0f, 10f, sliderStyle, sliderThumbStyle);
                barrier.normalThickness = GUILayout.HorizontalSlider(barrier.normalThickness, 0f, 10f, sliderStyle, sliderThumbStyle);
                barrier.opaque = GUILayout.Toggle(barrier.opaque, "Непрозрачная стена", buttonStyle);
            }
            else if (selectedObject is RadiationMirror mirror)
            {
                GUILayout.Label($"Отражение: {mirror.reflectivity:0.00}", labelStyle);
                mirror.reflectivity = GUILayout.HorizontalSlider(mirror.reflectivity, 0f, 1f, sliderStyle, sliderThumbStyle);
            }
            else if (selectedObject is RadiationSpeedReducer reducer)
            {
                GUILayout.Label($"Замедление: {reducer.speedMultiplier:0.00}", labelStyle);
                reducer.speedMultiplier = GUILayout.HorizontalSlider(reducer.speedMultiplier, 0f, 1f, sliderStyle, sliderThumbStyle);
                GUILayout.Label($"Потери интенсивности: {reducer.intensityLossPerUnit:0.00}", mutedStyle);
                reducer.intensityLossPerUnit = GUILayout.HorizontalSlider(reducer.intensityLossPerUnit, 0f, 2f, sliderStyle, sliderThumbStyle);
            }
            else if (selectedObject is CriticalRadiationObject critical)
            {
                GUILayout.Label($"Критический объект | доза {critical.AccumulatedDose:0.00} / {critical.CriticalDose:0.00}", labelStyle);
                float criticalDose = GUILayout.HorizontalSlider(critical.CriticalDose, .01f, 100f, sliderStyle, sliderThumbStyle);
                float emissionRate = GUILayout.HorizontalSlider(critical.ActivatedEmissionRate, 0f, 200f, sliderStyle, sliderThumbStyle);
                float intensity = GUILayout.HorizontalSlider(critical.ActivatedIntensity, 0f, 20f, sliderStyle, sliderThumbStyle);
                float doseRate = GUILayout.HorizontalSlider(critical.DosePerIntensitySecond, 0f, 20f, sliderStyle, sliderThumbStyle);
                critical.DestroyIncomingParticles = GUILayout.Toggle(critical.DestroyIncomingParticles, "Уничтожать входящие частицы", buttonStyle);
                critical.ConfigureAdvanced(criticalDose, doseRate, emissionRate, intensity, critical.DestroyIncomingParticles);
                RadiationSource[] emitters = FindObjectsOfType<RadiationSource>();
                RadiationSource emitter = emitters.Length > 0 ? emitters[0] : null;
                if (critical.ActivatedSource != null && emitter != null && emitter != critical.ActivatedSource && GUILayout.Button("Синхронизировать частицы с эмиттером", buttonStyle))
                    critical.SyncActivatedSourceFrom(emitter);
            }
            GUILayout.EndVertical();
        }

        private void DrawField(RadiationDosimeterField f)
        {
            GUILayout.Label($"3D поле дозиметров | {f.Count} сфер | Size {f.GridSize} | Spacing {f.Spacing:0.00}", labelStyle);
            int count = Mathf.RoundToInt(GUILayout.HorizontalSlider(f.DosimeterCount, 1f, 1000f, sliderStyle, sliderThumbStyle));
            Vector3 gridSize = f.GridSize;
            gridSize.x = GUILayout.HorizontalSlider(gridSize.x, .1f, 100f, sliderStyle, sliderThumbStyle);
            gridSize.y = GUILayout.HorizontalSlider(gridSize.y, .1f, 100f, sliderStyle, sliderThumbStyle);
            gridSize.z = GUILayout.HorizontalSlider(gridSize.z, .1f, 100f, sliderStyle, sliderThumbStyle);
            float size = GUILayout.HorizontalSlider(f.SphereSize, .01f, 2f, sliderStyle, sliderThumbStyle);
            float spacing = GUILayout.HorizontalSlider(f.Spacing, .01f, 10f, sliderStyle, sliderThumbStyle);
            float dose = GUILayout.HorizontalSlider(f.DoseToRed, .01f, 100f, sliderStyle, sliderThumbStyle);
            float smooth = GUILayout.HorizontalSlider(f.ColorSmoothTime, .01f, 5f, sliderStyle, sliderThumbStyle);
            bool grid = GUILayout.Toggle(f.UseGrid, "Использовать 3D сетку", buttonStyle);
            if (count != f.DosimeterCount) f.ConfigureCount(count);
            if (gridSize != f.GridSize || !Mathf.Approximately(size, f.SphereSize) || !Mathf.Approximately(spacing, f.Spacing)) f.ConfigureGrid(gridSize, f.GridCenter, spacing, size);
            if (grid != f.UseGrid || !Mathf.Approximately(dose, f.DoseToRed) || !Mathf.Approximately(smooth, f.ColorSmoothTime)) f.ConfigureAdvanced(f.GridCenter, grid, dose, smooth);
            if (GUILayout.Button("Перестроить поле", buttonStyle)) f.Rebuild();
        }

        private void DrawSource(RadiationSource s)
        {
            GUILayout.Label($"Источник | active {s.ActiveParticleCount} / {s.MaxParticles}", labelStyle);
            float rate = GetFloat(s, "EmissionRate", 40f), speed = GetFloat(s, "ParticleSpeed", 8f), life = GetFloat(s, "Lifetime", 6f), variation = GetFloat(s, "LifetimeVariation", .25f), cone = GetFloat(s, "ConeAngle", 30f);
            GUILayout.Label($"Rate {rate:0.0}  |  Speed {speed:0.0}  |  Life {life:0.0}", mutedStyle);
            float newRate = GUILayout.HorizontalSlider(rate, 0f, 200f, sliderStyle, sliderThumbStyle);
            float newSpeed = GUILayout.HorizontalSlider(speed, .1f, 50f, sliderStyle, sliderThumbStyle);
            float newLife = GUILayout.HorizontalSlider(life, .1f, 30f, sliderStyle, sliderThumbStyle);
            if (!Mathf.Approximately(newRate, rate) || !Mathf.Approximately(newSpeed, speed) || !Mathf.Approximately(newLife, life))
                s.ConfigureEmissionProperties(newRate, newSpeed, newLife, variation, s.InitialIntensity);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Конус", s.FlowShape == RadiationFlowShape.Cone ? activeButtonStyle : buttonStyle)) s.SetEmission(newRate, s.InitialIntensity, RadiationFlowShape.Cone);
            if (GUILayout.Button("Луч", s.FlowShape == RadiationFlowShape.Ray ? activeButtonStyle : buttonStyle)) s.SetEmission(newRate, s.InitialIntensity, RadiationFlowShape.Ray);
            if (GUILayout.Button("Сфера", s.FlowShape == RadiationFlowShape.Omnidirectional ? activeButtonStyle : buttonStyle)) s.SetEmission(newRate, s.InitialIntensity, RadiationFlowShape.Omnidirectional);
            GUILayout.EndHorizontal();
            GUILayout.Label($"Variation {variation:0.00}  |  Cone {cone:0}°", mutedStyle);
            float newVariation = GUILayout.HorizontalSlider(variation, 0f, 1f, sliderStyle, sliderThumbStyle);
            float newIntensity = GUILayout.HorizontalSlider(s.InitialIntensity, 0f, 20f, sliderStyle, sliderThumbStyle);
            float newCone = GUILayout.HorizontalSlider(cone, 0f, 180f, sliderStyle, sliderThumbStyle);
            float newMaxParticles = GUILayout.HorizontalSlider(s.MaxParticles, 1f, 2000f, sliderStyle, sliderThumbStyle);
            float newReferenceDistance = GUILayout.HorizontalSlider(s.ReferenceDistance, 0.01f, 20f, sliderStyle, sliderThumbStyle);
            float newMinSize = GUILayout.HorizontalSlider(s.MinParticleSize, 0.001f, 0.5f, sliderStyle, sliderThumbStyle);
            float newMaxSize = GUILayout.HorizontalSlider(s.MaxParticleSize, 0.001f, 1f, sliderStyle, sliderThumbStyle);
            float newRadius = GUILayout.HorizontalSlider(s.ParticleRadius, 0.0001f, 0.5f, sliderStyle, sliderThumbStyle);
            s.ConfigureAdvanced(newMaxParticles, newVariation, newIntensity, newReferenceDistance, newCone, newMinSize, newMaxSize, newRadius);
        }

        private void DrawMagnet(RadiationMagnet m)
        {
            GUILayout.Label($"Магнит | radius {m.Radius:0.0} | strength {m.Strength:0.0} | {m.Polarity}", labelStyle);
            float radius = GUILayout.HorizontalSlider(m.Radius, .1f, 20f, sliderStyle, sliderThumbStyle);
            float strength = GUILayout.HorizontalSlider(m.Strength, 0f, 100f, sliderStyle, sliderThumbStyle);
            float falloff = GUILayout.HorizontalSlider(m.Falloff, .1f, 4f, sliderStyle, sliderThumbStyle);
            RadiationMagnetPolarity polarity = m.Polarity;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Притяжение", polarity == RadiationMagnetPolarity.Attract ? activeButtonStyle : buttonStyle)) polarity = RadiationMagnetPolarity.Attract;
            if (GUILayout.Button("Отталкивание", polarity == RadiationMagnetPolarity.Repel ? activeButtonStyle : buttonStyle)) polarity = RadiationMagnetPolarity.Repel;
            GUILayout.EndHorizontal();
            bool outsideRadius = GUILayout.Toggle(m.AffectParticlesOutsideRadius, "Действует вне радиуса", buttonStyle);
            float maxAcceleration = GUILayout.HorizontalSlider(m.MaxAcceleration, 0f, 200f, sliderStyle, sliderThumbStyle);
            float maxParticleSpeed = GUILayout.HorizontalSlider(m.MaxParticleSpeed, 0f, 100f, sliderStyle, sliderThumbStyle);
            m.ConfigureProperties(radius, strength, falloff, polarity, outsideRadius);
            m.ConfigureLimits(maxAcceleration, maxParticleSpeed);
        }

        private static float GetFloat(object o, string name, float fallback)
        {
            PropertyInfo property = o.GetType().GetProperty(name);

            if (property != null && property.PropertyType == typeof(float))
            {
                return (float)property.GetValue(o);
            }

            return fallback;
        }

        private static int GetInt(object o, string name, int fallback)
        {
            PropertyInfo property = o.GetType().GetProperty(name);

            if (property != null && property.PropertyType == typeof(int))
            {
                return (int)property.GetValue(o);
            }

            return fallback;
        }

        private void EnsureStyles()
        {
            // не сносится при смене сцены
            if (panelStyle != null && panelBackgroundTex != null) return;
            ClearGuiTextures();

            panelBackgroundTex = MakeBorderedTexture(new Color(0.055f, 0.082f, 0.125f, 0.94f), new Color(0.15f, 0.90f, 1f, 0.35f));
            panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = panelBackgroundTex },
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(12, 12, 12, 12)
            };

            cardStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeBorderedTexture(new Color(0.085f, 0.125f, 0.185f, 0.92f), new Color(0.70f, 0.88f, 1f, 0.12f)) },
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 3, 3)
            };

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = SelectionColor },
                margin = new RectOffset(0, 0, 2, 0)
            };

            sectionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.55f, 0.86f, 0.96f, 1f) },
                margin = new RectOffset(0, 0, 0, 4)
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.92f, 0.95f, 0.98f, 1f) },
                margin = new RectOffset(0, 0, 1, 1)
            };

            mutedStyle = new GUIStyle(labelStyle)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.65f, 0.74f, 0.84f, 1f) }
            };

            Texture2D btnNormal = MakeBorderedTexture(new Color(0.11f, 0.165f, 0.245f, 1f), new Color(0.45f, 0.72f, 0.95f, 0.28f));
            Texture2D btnHover = MakeBorderedTexture(new Color(0.15f, 0.23f, 0.34f, 1f), new Color(0.15f, 0.90f, 1f, 0.65f));
            Texture2D btnActive = MakeBorderedTexture(new Color(0.18f, 0.30f, 0.44f, 1f), SelectionColor);
            Texture2D btnOn = MakeBorderedTexture(new Color(0.09f, 0.30f, 0.40f, 1f), SelectionColor);

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(10, 10, 6, 6),
                margin = new RectOffset(2, 2, 2, 2),
                normal = { background = btnNormal, textColor = new Color(0.92f, 0.96f, 1f, 1f) },
                hover = { background = btnHover, textColor = Color.white },
                active = { background = btnActive, textColor = Color.white },
                focused = { background = btnNormal, textColor = Color.white },
                onNormal = { background = btnOn, textColor = Color.white },
                onHover = { background = btnHover, textColor = Color.white },
                onActive = { background = btnActive, textColor = Color.white },
                onFocused = { background = btnOn, textColor = Color.white }
            };

            activeButtonStyle = new GUIStyle(buttonStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { background = btnOn, textColor = Color.white },
                hover = { background = btnHover, textColor = Color.white }
            };

            sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 14f,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 2, 4),
                normal = { background = MakeSliderTrackTexture(new Color(0.045f, 0.07f, 0.11f, 1f), new Color(0.30f, 0.60f, 0.80f, 0.45f)) }
            };

            Texture2D thumbNormal = MakeBorderedTexture(SelectionColor, new Color(0.85f, 0.98f, 1f, 0.9f));
            Texture2D thumbHover = MakeBorderedTexture(new Color(0.50f, 0.95f, 1f, 1f), Color.white);
            sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 12f,
                fixedHeight = 14f,
                border = new RectOffset(1, 1, 1, 1),
                normal = { background = thumbNormal },
                hover = { background = thumbHover },
                active = { background = thumbHover },
                focused = { background = thumbNormal }
            };

            scrollbarStyle = new GUIStyle(GUI.skin.verticalScrollbar)
            {
                fixedWidth = 8f,
                border = new RectOffset(0, 0, 0, 0),
                normal = { background = MakeSolidTexture(new Color(0.04f, 0.06f, 0.09f, 0.55f)) }
            };

            scrollbarThumbStyle = new GUIStyle(GUI.skin.verticalScrollbarThumb)
            {
                fixedWidth = 8f,
                border = new RectOffset(0, 0, 0, 0),
                normal = { background = MakeSolidTexture(new Color(0.22f, 0.52f, 0.68f, 0.75f)) },
                hover = { background = MakeSolidTexture(SelectionColor) },
                active = { background = MakeSolidTexture(SelectionColor) }
            };
        }

        private Texture2D MakeSolidTexture(Color color)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            t.SetPixel(0, 0, color);
            t.Apply();
            guiTextures.Add(t);
            return t;
        }

        private Texture2D MakeBorderedTexture(Color fill, Color border)
        {
            var t = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                    t.SetPixel(x, y, x == 1 && y == 1 ? fill : border);
            t.Apply();
            guiTextures.Add(t);
            return t;
        }

        private Texture2D MakeSliderTrackTexture(Color fill, Color border)
        {
            var t = new Texture2D(1, 14, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point
            };
            for (int y = 0; y < 14; y++)
            {
                Color c = Color.clear;
                if (y == 5 || y == 8) c = border;
                else if (y == 6 || y == 7) c = fill;
                t.SetPixel(0, y, c);
            }
            t.Apply();
            guiTextures.Add(t);
            return t;
        }

        private void ClearGuiTextures()
        {
            for (int i = 0; i < guiTextures.Count; i++)
                if (guiTextures[i] != null) Destroy(guiTextures[i]);
            guiTextures.Clear();
            panelBackgroundTex = null;
            panelStyle = null;
        }
    }

    internal static class SelectionHighlight
    {
        private const float LineWidth = 0.025f;
        private const float Padding = 0.03f;

        public static GameObject Create(GameObject target, Color color)
        {
            GameObject marker = new GameObject("Selection Highlight") { hideFlags = HideFlags.DontSave };
            Transform t = target.transform;
            marker.transform.SetPositionAndRotation(t.position, t.rotation);

            Bounds bounds = ComputeLocalBounds(target);
            bounds.Expand(Padding * 2f);
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;

            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material mat = shader != null ? new Material(shader) { hideFlags = HideFlags.DontSave } : null;

            // обводка
            var lines = new List<LineRenderer>(6);
            lines.Add(CreateEdge(marker.transform, "Bottom", mat, color, true,
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z)));
            lines.Add(CreateEdge(marker.transform, "Top", mat, color, true,
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)));
            lines.Add(CreateEdge(marker.transform, "Edge_0", mat, color, false, new Vector3(min.x, min.y, min.z), new Vector3(min.x, max.y, min.z)));
            lines.Add(CreateEdge(marker.transform, "Edge_1", mat, color, false, new Vector3(max.x, min.y, min.z), new Vector3(max.x, max.y, min.z)));
            lines.Add(CreateEdge(marker.transform, "Edge_2", mat, color, false, new Vector3(max.x, min.y, max.z), new Vector3(max.x, max.y, max.z)));
            lines.Add(CreateEdge(marker.transform, "Edge_3", mat, color, false, new Vector3(min.x, min.y, max.z), new Vector3(min.x, max.y, max.z)));

            var follower = marker.AddComponent<SelectionHighlightFollower>();
            follower.Init(t, mat, lines.ToArray());
            return marker;
        }

        private static LineRenderer CreateEdge(Transform parent, string name, Material mat, Color color, bool loop, params Vector3[] points)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = loop;
            lr.widthMultiplier = LineWidth;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (mat != null) lr.sharedMaterial = mat;
            lr.startColor = color;
            lr.endColor = color;
            lr.positionCount = points.Length;
            lr.SetPositions(points);
            return lr;
        }

        private static Bounds ComputeLocalBounds(GameObject target)
        {
            Transform root = target.transform;
            Quaternion invRot = Quaternion.Inverse(root.rotation);
            Vector3 origin = root.position;
            Bounds result = default;
            bool found = false;

            foreach (Renderer r in target.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                    EncapsulateBox(r.transform.localToWorldMatrix, mf.sharedMesh.bounds, origin, invRot, ref result, ref found);
                else if (r is SkinnedMeshRenderer smr)
                    EncapsulateBox(r.transform.localToWorldMatrix, smr.localBounds, origin, invRot, ref result, ref found);
            }

            if (!found)
            {
                foreach (Collider c in target.GetComponentsInChildren<Collider>(false))
                {
                    if (!c.enabled) continue;
                    if (c is BoxCollider box)
                        EncapsulateBox(c.transform.localToWorldMatrix, new Bounds(box.center, box.size), origin, invRot, ref result, ref found);
                    else if (c is SphereCollider sphere)
                        EncapsulateBox(c.transform.localToWorldMatrix, new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f)), origin, invRot, ref result, ref found);
                    else if (c is CapsuleCollider cap)
                    {
                        Vector3 size = Vector3.one * (cap.radius * 2f);
                        size[cap.direction] = Mathf.Max(cap.height, cap.radius * 2f);
                        EncapsulateBox(c.transform.localToWorldMatrix, new Bounds(cap.center, size), origin, invRot, ref result, ref found);
                    }
                    else
                        EncapsulateBox(Matrix4x4.identity, c.bounds, origin, invRot, ref result, ref found);
                }
            }

            return found ? result : new Bounds(Vector3.zero, Vector3.one * 0.4f);
        }

        private static void EncapsulateBox(Matrix4x4 localToWorld, Bounds b, Vector3 origin, Quaternion invRot, ref Bounds result, ref bool found)
        {
            Vector3 min = b.min, max = b.max;
            for (int i = 0; i < 8; i++)
            {
                Vector3 local = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                Vector3 pt = invRot * (localToWorld.MultiplyPoint3x4(local) - origin);
                if (!found) { result = new Bounds(pt, Vector3.zero); found = true; }
                else result.Encapsulate(pt);
            }
        }
    }

    internal sealed class SelectionHighlightFollower : MonoBehaviour
    {
        private Transform target;
        private Material material;
        private LineRenderer[] lines;

        public void Init(Transform targetTransform, Material mat, LineRenderer[] lineRenderers)
        {
            target = targetTransform;
            material = mat;
            lines = lineRenderers;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                Destroy(gameObject);
                return;
            }
            bool active = target.gameObject.activeInHierarchy;
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i] != null && lines[i].enabled != active) lines[i].enabled = active;
            }
            if (active) transform.SetPositionAndRotation(target.position, target.rotation);
        }

        private void OnDestroy()
        {
            if (material != null)
            {
                Destroy(material);
            }
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
            // The controller's sceneLoaded callback is not invoked for the initial scene.
            var floor = GameObject.Find("Demo Floor");
            if (floor != null && UnityEngine.Object.FindObjectOfType<RadiationDosimeterField>() == null)
            {
                var bounds = floor.GetComponent<Renderer>() != null ? floor.GetComponent<Renderer>().bounds : new Bounds(floor.transform.position, Vector3.one);
                var fieldObject = new GameObject("Radiation Dosimeter 3D Grid");
                var field = fieldObject.AddComponent<RadiationDosimeterField>();
                field.ConfigureGrid(new Vector3(Mathf.Max(1f, bounds.size.x - 2f), .01f, Mathf.Max(1f, bounds.size.z - 2f)), new Vector3(bounds.center.x, bounds.max.y + .12f, bounds.center.z), 2f, .22f);
            }
        }
    }
}
