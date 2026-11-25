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

        // Модель может состоять из нескольких объектов: например, у критического
        // объекта излучатель лежит дочерним узлом. Выбор хранит всю группу
        // компонентов модели, чтобы клик по родителю открывал меню дочернего
        // излучателя, а клик по дочернему узлу — функции родителя.
        private Transform selectionRoot;
        private readonly List<Component> selectionGroup = new();
        private readonly List<string> selectionTabLabels = new();
        private readonly List<RadiationParticle> particleSnapshot = new();
        private readonly List<RadiationParticle> particleScratch = new();
        private readonly List<RadiationSource> sourceScratch = new();
        private readonly List<string> pathScratch = new();
        private int focusedComponentIndex;
        private int burstAmount = 10;
        private bool showParticleList;
        // Переключение карточек меняет структуру IMGUI. Применяем его только на
        // следующем проходе OnGUI, иначе Layout и Repaint расходятся и Unity
        // пишет "GUILayout mismatch". Компонент запоминаем ссылкой: порядок в
        // selectionGroup после сортировки не совпадает с порядком кликов.
        private Component pendingFocusComponent;
        private const int PendingStateNone = -1, PendingStateHide = 0, PendingStateShow = 1;
        private int pendingParticleListState = PendingStateNone;

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
            selectionRoot = null;
            selectionGroup.Clear();
            selectionTabLabels.Clear();
            focusedComponentIndex = 0;
            showParticleList = false;
            ClearPendingInspectorState();
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
                    marker.name = "Interaction Handle";
                    marker.transform.SetParent(component.transform, false);
                    // Держим постоянный МИРОВОЙ размер .35, иначе при большом масштабе
                    // родителя невидимый коллайдер разрастается и перехватывает клики
                    // по всей сцене.
                    Vector3 lossy = component.transform.lossyScale;
                    marker.transform.localScale = new Vector3(
                        .35f / Mathf.Max(.0001f, Mathf.Abs(lossy.x)),
                        .35f / Mathf.Max(.0001f, Mathf.Abs(lossy.y)),
                        .35f / Mathf.Max(.0001f, Mathf.Abs(lossy.z)));
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
            // Отпускание мыши всегда завершает перетаскивание, даже если курсор
            // оказался над панелью UI. Раньше ранний выход по панели оставлял
            // dragging == true и объект продолжал ехать за курсором.
            if (!PointerHeld() || !Application.isFocused)
            {
                dragging = false;
                return;
            }
            if (MiddleHeld())
            {
                dragging = false;
                return;
            }
            // IMGUI does not create an EventSystem pointer event. Use the actual panel rectangle,
            // otherwise a click on the panel can steal the drag state or block world selection.
            if (PointerOverPanel()) return;
            if (PointerDown())
            {
                Camera cam = GetActiveCamera();
                if (cam == null) return;
                Ray ray = cam.ScreenPointToRay(PointerPosition());
                Component candidate = PickSelectable(ray);
                if (candidate == null)
                {
                    // Клик по пустому месту снимает выбор.
                    ClearSelection();
                    return;
                }
                Select(candidate);
                Transform movementTransform = GetMovementTransform(candidate);
                Plane plane = new Plane(Vector3.up, movementTransform.position);
                if (plane.Raycast(ray, out dragDepth))
                {
                    dragOffset = movementTransform.position - ray.GetPoint(dragDepth);
                    height = movementTransform.position.y;
                    dragging = true;
                }
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
                Transform movementTransform = GetMovementTransform(selectedObject);
                movementTransform.position = new Vector3(p.x, height, p.z);
            }
        }

        private static Component PickSelectable(Ray ray)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, 1000f, ~0, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0) return null;
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null) continue;
                if (hits[i].collider.GetComponentInParent<RadiationParticle>() != null) continue;
                // Модель излучателя состоит из мешей, а сам RadiationSource лежит
                // рядом с ними внутри той же модели. Поэтому ищем источник в корне
                // модели, а не только среди родителей коллайдера: иначе клик по
                // модели выбирал случайный компонент (барьер/зеркало).
                RadiationSource source = FindSourceInModel(hits[i].collider.transform);
                if (source != null) return source;
                Component candidate = FindSelectable(hits[i].collider);
                if (candidate != null) return candidate;
            }
            return null;
        }

        private static RadiationSource FindSourceInModel(Transform hit)
        {
            for (Transform t = hit; t != null; t = t.parent)
            {
                RadiationSource own = t.GetComponent<RadiationSource>();
                if (own != null) return own;
                if (!IsModelRoot(t)) continue;
                RadiationSource[] sources = t.GetComponentsInChildren<RadiationSource>(true);
                if (sources.Length == 1) return sources[0];
            }
            return null;
        }

        private static Transform GetMovementTransform(Component candidate)
        {
            if (candidate == null) return null;
            Transform start = candidate.transform;
            Transform root = start;
            Transform parent = start.parent;
            // Поднимаемся до корня модели излучателя, но не в общий контейнер
            // сцены: если в родителе лежат дозиметры, магниты и т.п., движение
            // ограничивается самим объектом, чтобы не утащить всю сцену.
            while (parent != null && IsModelRoot(parent))
            {
                root = parent;
                parent = parent.parent;
            }
            return root;
        }

        private static bool IsModelRoot(Transform t)
        {
            if (t.GetComponent<Camera>() != null || t.GetComponent<Light>() != null) return false;
            MonoBehaviour[] components = t.GetComponentsInChildren<MonoBehaviour>(true);
            int sources = 0;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is RadiationSource) { sources++; continue; }
                if (components[i] is AttenuatingRadiationBarrier || components[i] is RadiationMirror) continue;
                if (components[i] is RadiationDosimeter || components[i] is RadiationDosimeterField ||
                    components[i] is RadiationMagnet || components[i] is RadiationSpeedReducer ||
                    components[i] is CriticalRadiationObject) return false;
            }
            return sources <= 1;
        }

        private bool PointerOverPanel()
        {
            Vector2 p = PointerPosition();
            // PointerPosition в координатах мыши (y снизу), а GUI рисуется
            // сверху вниз. Без переворота y клики рядом с панелью и по сцене
            // определялись неверно.
            float guiY = Screen.height - p.y;
            if (!showPanel)
                return p.x >= 12f && p.x <= 132f && guiY >= 12f && guiY <= 40f;
            return p.x >= 12f && p.x <= 352f && guiY >= 12f && guiY <= Mathf.Max(120f, Screen.height - 24f);
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
            selectedObject = component;
            Transform movementTransform = GetMovementTransform(component);
            height = movementTransform.position.y;
            BuildSelectionGroup(component);
            ClearMarker();
            if (movementTransform != null) selectionMarker = SelectionHighlight.Create(movementTransform.gameObject, SelectionColor);
        }

        /// <summary>
        /// Собирает все компоненты модели: сам узел, его родительскую модель и
        /// вложенные узлы. Благодаря этому один клик даёт доступ и к функциям
        /// родителя (например CriticalRadiationObject), и к меню частиц
        /// дочернего излучателя.
        /// </summary>
        private void BuildSelectionGroup(Component clicked)
        {
            selectionGroup.Clear();
            selectionTabLabels.Clear();
            focusedComponentIndex = 0;
            showParticleList = false;
            ClearPendingInspectorState();
            selectionRoot = clicked != null ? GetMovementTransform(clicked) : null;
            if (selectionRoot == null)
            {
                return;
            }

            var components = selectionRoot.GetComponentsInChildren<MonoBehaviour>(true);
            var dosimeterField = selectionRoot.GetComponent<RadiationDosimeterField>();
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour behaviour = components[i];
                if (behaviour == null) continue;

                // Дозиметры создаются полем в рантайме: выводить сотню одинаковых
                // карточек бессмысленно, но сам выбранный дозиметр нужен.
                if (behaviour is RadiationDosimeter && behaviour != clicked && dosimeterField != null) continue;
                if (!IsInspectable(behaviour) && behaviour != clicked) continue;

                selectionGroup.Add(behaviour);
                selectionTabLabels.Add(DescribeComponent(behaviour));
                if (behaviour == clicked) focusedComponentIndex = selectionGroup.Count - 1;
            }

            // Порядок карточек: сверху родительские функции, затем излучатели.
            selectionGroup.Sort(CompareInspectionPriority);
        }

        private static bool IsInspectable(MonoBehaviour behaviour) =>
            behaviour is RadiationSource || behaviour is RadiationDosimeter ||
            behaviour is RadiationDosimeterField || behaviour is RadiationMagnet ||
            behaviour is AttenuatingRadiationBarrier || behaviour is RadiationMirror ||
            behaviour is RadiationSpeedReducer || behaviour is CriticalRadiationObject;

        private static int CompareInspectionPriority(Component left, Component right)
        {
            int byDepth = DepthOf(left).CompareTo(DepthOf(right));
            if (byDepth != 0) return byDepth;
            return InspectionPriority(left).CompareTo(InspectionPriority(right));
        }

        private static int DepthOf(Component component)
        {
            int depth = 0;
            for (Transform t = component != null ? component.transform : null; t != null; t = t.parent) depth++;
            return depth;
        }

        private static int InspectionPriority(Component component) => component switch
        {
            CriticalRadiationObject => 0,
            RadiationSource => 1,
            RadiationDosimeter => 2,
            RadiationDosimeterField => 2,
            RadiationMagnet => 3,
            AttenuatingRadiationBarrier => 4,
            RadiationMirror => 5,
            RadiationSpeedReducer => 6,
            _ => 7
        };

        private static string DescribeComponent(Component component) => component switch
        {
            RadiationSource => "Излучатель",
            CriticalRadiationObject => "Критический объект",
            RadiationDosimeter => "Дозиметр",
            RadiationDosimeterField => "Поле дозиметров",
            RadiationMagnet => "Магнит",
            AttenuatingRadiationBarrier barrier => barrier.opaque ? "Непрозрачный барьер" : "Барьер (ослабление)",
            RadiationMirror => "Зеркало",
            RadiationSpeedReducer => "Замедлитель",
            _ => component != null ? component.GetType().Name : "Компонент"
        };

        private Component FocusedComponent =>
            focusedComponentIndex >= 0 && focusedComponentIndex < selectionGroup.Count
                ? selectionGroup[focusedComponentIndex]
                : selectedObject;

        private int CountModelSources()
        {
            int count = 0;
            for (int i = 0; i < selectionGroup.Count; i++) if (selectionGroup[i] is RadiationSource) count++;
            return count;
        }

        private void ClearSelection()
        {
            if (selectedObject == null) return;
            selectedObject = null;
            selectionRoot = null;
            selectionGroup.Clear();
            selectionTabLabels.Clear();
            focusedComponentIndex = 0;
            showParticleList = false;
            dragging = false;
            ClearPendingInspectorState();
            ClearMarker();
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
            selectionMarker = SelectionHighlight.Create(GetMovementTransform(selectedObject).gameObject, SelectionColor);
        }

        /// <summary>
        /// Отложенная смена структуры инспектора. Кнопки GUILayout не могут
        /// менять состав карточек внутри того же прохода OnGUI: Layout и Repaint
        /// разойдутся и Unity напишет "GUILayout mismatch". Поэтому запрос
        /// копится, а применяется в начале следующего прохода.
        /// </summary>
        private void QueueFocusedComponent(Component component)
        {
            if (component == null) return;
            pendingFocusComponent = component;
        }

        private void QueueParticleList(bool visible) =>
            pendingParticleListState = visible ? PendingStateShow : PendingStateHide;

        private void ClearPendingInspectorState()
        {
            pendingFocusComponent = null;
            pendingParticleListState = PendingStateNone;
        }

        private void ApplyPendingInspectorState()
        {
            Component target = pendingFocusComponent;
            pendingFocusComponent = null;
            if (target != null)
            {
                // Ищем перебором: обычный IndexOf использует Equals, а у уничтоженных
                // UnityEngine.Object нужна перегрузка оператора ==.
                for (int i = 0; i < selectionGroup.Count; i++)
                {
                    if (selectionGroup[i] != target) continue;
                    focusedComponentIndex = i;
                    break;
                }
            }

            switch (pendingParticleListState)
            {
                case PendingStateShow: showParticleList = true; break;
                case PendingStateHide: showParticleList = false; break;
            }
            pendingParticleListState = PendingStateNone;
        }

        private void OnGUI()
        {
            ApplyPendingInspectorState();
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
            if (selectionGroup.Count > 1)
                GUILayout.Label($"Модель содержит компонентов: {selectionGroup.Count}. Функции родителя и дочерних узлов собраны ниже.", mutedStyle);

            // Инспектор работает с тем же Transform, что и перетаскивание,
            // иначе высота/поворот/размер менялись у дочернего компонента,
            // а мышью двигалась вся модель.
            Transform target = GetMovementTransform(selectedObject);
            if (target == null)
            {
                GUILayout.EndVertical();
                return;
            }

            DrawTransformControls(target);
            DrawComponentTabs();

            GUILayout.Space(4);
            Component focused = FocusedComponent;
            for (int i = 0; i < selectionGroup.Count; i++)
            {
                Component component = selectionGroup[i];
                if (component == null) continue;
                if (component == focused) DrawInspectionBlock(component, true);
                else DrawInspectionBlock(component, false);
            }

            DrawModelParticleMenu();
            GUILayout.EndVertical();
        }

        private void DrawModelParticleMenu()
        {
            var sources = new List<RadiationSource>();
            for (int i = 0; i < selectionGroup.Count; i++)
                if (selectionGroup[i] is RadiationSource source && source != null) sources.Add(source);

            int active = 0;
            for (int i = 0; i < sources.Count; i++) active += sources[i].ActiveParticleCount;
            if (sources.Count == 0) return;

            GUILayout.Space(4);
            GUILayout.BeginVertical(cardStyle);
            GUILayout.Label("МЕНЮ ЧАСТИЦ", sectionStyle);
            GUILayout.Label($"Излучателей в модели: {sources.Count} · активных частиц: {active}", mutedStyle);

            GUILayout.Label($"Залп: {burstAmount}", labelStyle);
            burstAmount = Mathf.RoundToInt(GUILayout.HorizontalSlider(burstAmount, 1f, 200f, sliderStyle, sliderThumbStyle));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Залп", buttonStyle))
            {
                int emitted = 0;
                for (int i = 0; i < sources.Count; i++) emitted += sources[i].EmitBurst(burstAmount);
                if (emitted == 0) Debug.LogWarning("Залп не выполнен: достигнут MaxParticles или не задан префаб частицы.", this);
            }
            if (GUILayout.Button("Очистить", buttonStyle))
                for (int i = 0; i < sources.Count; i++) sources[i].ClearParticles();
            if (GUILayout.Button("Включить все", buttonStyle))
                for (int i = 0; i < sources.Count; i++) sources[i].gameObject.SetActive(true);
            GUILayout.EndHorizontal();

            // Дочерний излучатель критического объекта по умолчанию выключен и
            // включается только после набора пороговой дозы.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Выключить все", buttonStyle))
                for (int i = 0; i < sources.Count; i++) sources[i].gameObject.SetActive(false);
            if (GUILayout.Button(showParticleList ? "Скрыть список частиц" : "Список частиц", showParticleList ? activeButtonStyle : buttonStyle))
                QueueParticleList(!showParticleList);
            GUILayout.EndHorizontal();

            for (int i = 0; i < sources.Count; i++)
            {
                RadiationSource source = sources[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{DescribePath(source)}: {source.ActiveParticleCount}/{source.MaxParticles}", mutedStyle);
                GUILayout.FlexibleSpace();
                bool enabledNow = source.gameObject.activeSelf;
                if (GUILayout.Button(enabledNow ? "Стоп" : "Пуск", buttonStyle, GUILayout.Width(64f)))
                {
                    if (enabledNow) source.ClearParticles();
                    source.gameObject.SetActive(!enabledNow);
                }
                GUILayout.EndHorizontal();
            }

            if (showParticleList) DrawParticleList(sources);
            GUILayout.EndVertical();
        }

        private void DrawParticleList(List<RadiationSource> sources)
        {
            // Снимок делается один раз за вызов отрисовки: набор активных частиц
            // меняется в FixedUpdate, а живой перечислитель в IMGUI упал бы.
            particleSnapshot.Clear();
            for (int i = 0; i < sources.Count; i++)
            {
                var batch = new List<RadiationParticle>();
                sources[i].CopyActiveParticles(batch);
                particleSnapshot.AddRange(batch);
            }

            GUILayout.Label($"Активные частицы: {particleSnapshot.Count}", labelStyle);
            int shown = Mathf.Min(particleSnapshot.Count, 12);
            for (int i = 0; i < shown; i++)
            {
                RadiationParticle particle = particleSnapshot[i];
                if (particle == null) continue;
                GUILayout.Label(
                    $"#{i:00} {ParticleTypeName(particle.ParticleType)} · I {particle.Intensity:0.000} · t {particle.RemainingLifetime:0.0}с · s {particle.TraveledDistance:0.0} · v {particle.Velocity.magnitude:0.0}",
                    mutedStyle);
            }
            if (particleSnapshot.Count > shown)
                GUILayout.Label($"… и ещё {particleSnapshot.Count - shown}", mutedStyle);
        }

        private string DescribePath(RadiationSource source)
        {
            if (selectionRoot == null || source.transform == selectionRoot) return source.gameObject.name;
            if (!source.transform.IsChildOf(selectionRoot)) return source.gameObject.name;
            var path = new List<string>();
            for (Transform t = source.transform; t != null && t != selectionRoot; t = t.parent) path.Add(t.gameObject.name);
            path.Reverse();
            return string.Join("/", path);
        }

        private void DrawTransformControls(Transform target)
        {
            GUILayout.Label($"Высота: {height:0.00}", labelStyle);
            float newHeight = GUILayout.HorizontalSlider(height, -10f, 10f, sliderStyle, sliderThumbStyle);
            // Применяем только при реальном изменении: раньше позиция и поворот
            // перезаписывались каждый кадр, и при выборе дочернего объекта внутри
            // модели с малым масштабом модель уезжала из вида.
            if (!Mathf.Approximately(newHeight, height))
            {
                height = newHeight;
                Vector3 p = target.position;
                target.position = new Vector3(p.x, height, p.z);
                RefreshSelectionMarker();
            }

            GUILayout.Label($"Поворот Y: {target.eulerAngles.y:0}°", labelStyle);
            float rot = GUILayout.HorizontalSlider(target.eulerAngles.y, 0f, 360f, sliderStyle, sliderThumbStyle);
            if (!Mathf.Approximately(Mathf.DeltaAngle(rot, target.eulerAngles.y), 0f))
            {
                target.rotation = Quaternion.Euler(0f, rot, 0f);
                RefreshSelectionMarker();
            }

            Vector3 scale = target.localScale;
            GUILayout.Label($"Размер: X {scale.x:0.000} | Y {scale.y:0.000} | Z {scale.z:0.000}", labelStyle);
            float sx = GUILayout.HorizontalSlider(scale.x, 0.001f, 100f, sliderStyle, sliderThumbStyle);
            float sy = GUILayout.HorizontalSlider(scale.y, 0.001f, 100f, sliderStyle, sliderThumbStyle);
            float sz = GUILayout.HorizontalSlider(scale.z, 0.001f, 100f, sliderStyle, sliderThumbStyle);
            if (!Mathf.Approximately(sx, scale.x) || !Mathf.Approximately(sy, scale.y) || !Mathf.Approximately(sz, scale.z))
            {
                target.localScale = new Vector3(sx, sy, sz);
                RefreshSelectionMarker();
            }
        }

        /// <summary>Переключатель между компонентами одной модели: родитель ↔ дочерний излучатель.</summary>
        private void DrawComponentTabs()
        {
            if (selectionGroup.Count <= 1) return;
            GUILayout.Space(4);
            GUILayout.Label("КОМПОНЕНТЫ МОДЕЛИ", sectionStyle);
            const int perRow = 2;
            for (int row = 0; row * perRow < selectionGroup.Count; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < perRow; col++)
                {
                    int index = row * perRow + col;
                    if (index >= selectionGroup.Count) break;
                    bool isFocused = index == focusedComponentIndex;
                    if (GUILayout.Button(selectionTabLabels[index], isFocused ? activeButtonStyle : buttonStyle))
                    {
                        QueueFocusedComponent(selectionGroup[index]);
                        QueueParticleList(false);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawInspectionBlock(Component component, bool detailed)
        {
            GUILayout.BeginVertical(cardStyle);
            if (detailed)
            {
                GUILayout.Label(DescribeComponent(component).ToUpperInvariant() + " — " + component.gameObject.name, sectionStyle);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{DescribeComponent(component)} · {component.gameObject.name}", mutedStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Открыть", buttonStyle, GUILayout.Width(72f)))
                {
                    QueueFocusedComponent(component);
                    QueueParticleList(false);
                }
                GUILayout.EndHorizontal();
                GUIContent summary = SummaryOf(component);
                if (summary != null) GUILayout.Label(summary, mutedStyle);
                GUILayout.EndVertical();
                return;
            }

            switch (component)
            {
                case RadiationDosimeter dosimeter:
                    GUILayout.Label($"Доза {dosimeter.Dose:0.00} / {dosimeter.DoseToRed:0.00}", labelStyle);
                    float doseLimit = GUILayout.HorizontalSlider(dosimeter.DoseToRed, 0.01f, 100f, sliderStyle, sliderThumbStyle);
                    bool selectable = GUILayout.Toggle(dosimeter.Selectable, "Доступен для выбора", buttonStyle);
                    if (!Mathf.Approximately(doseLimit, dosimeter.DoseToRed) || selectable != dosimeter.Selectable) dosimeter.Configure(doseLimit, selectable);
                    if (GUILayout.Button("Сбросить дозу", buttonStyle)) dosimeter.ResetDose();
                    break;
                case RadiationDosimeterField field:
                    DrawField(field);
                    break;
                case CriticalRadiationObject critical:
                    DrawCritical(critical);
                    break;
                case RadiationSource source:
                    DrawSource(source);
                    break;
                case RadiationMagnet magnet:
                    DrawMagnet(magnet);
                    break;
                case AttenuatingRadiationBarrier barrier:
                    GUILayout.Label($"Плотность: {barrier.density:0.00}", labelStyle);
                    barrier.density = GUILayout.HorizontalSlider(barrier.density, 0f, 10f, sliderStyle, sliderThumbStyle);
                    GUILayout.Label($"Коэффициент ослабления: {barrier.attenuationCoefficient:0.00}", mutedStyle);
                    barrier.attenuationCoefficient = GUILayout.HorizontalSlider(barrier.attenuationCoefficient, 0f, 10f, sliderStyle, sliderThumbStyle);
                    GUILayout.Label($"Толщина по нормали: {barrier.normalThickness:0.00}", mutedStyle);
                    barrier.normalThickness = GUILayout.HorizontalSlider(barrier.normalThickness, 0f, 10f, sliderStyle, sliderThumbStyle);
                    barrier.opaque = GUILayout.Toggle(barrier.opaque, "Непрозрачная стена (полная блокировка)", buttonStyle);
                    break;
                case RadiationMirror mirror:
                    GUILayout.Label($"Отражение: {mirror.reflectivity:0.00}", labelStyle);
                    mirror.reflectivity = GUILayout.HorizontalSlider(mirror.reflectivity, 0f, 1f, sliderStyle, sliderThumbStyle);
                    break;
                case RadiationSpeedReducer reducer:
                    GUILayout.Label($"Замедление: {reducer.speedMultiplier:0.00}", labelStyle);
                    reducer.speedMultiplier = GUILayout.HorizontalSlider(reducer.speedMultiplier, 0f, 1f, sliderStyle, sliderThumbStyle);
                    GUILayout.Label($"Потери интенсивности: {reducer.intensityLossPerUnit:0.00}", mutedStyle);
                    reducer.intensityLossPerUnit = GUILayout.HorizontalSlider(reducer.intensityLossPerUnit, 0f, 2f, sliderStyle, sliderThumbStyle);
                    break;
            }
            GUILayout.EndVertical();
        }

        private static GUIContent SummaryOf(Component component) => component switch
        {
            RadiationSource source => new GUIContent($"{ParticleTypeName(source.ParticleType)} · эмиссия {source.EmissionRate:0.#}/с · частиц {source.ActiveParticleCount}/{source.MaxParticles} · {ShapeName(source.FlowShape)}"),
            CriticalRadiationObject critical => new GUIContent($"Доза {critical.AccumulatedDose:0.00}/{critical.CriticalDose:0.00} · {(critical.IsActivated ? "активирован" : "ожидает")} · излучатель {(critical.ActivatedSource != null ? "есть" : "нет")}"),
            RadiationMagnet magnet => new GUIContent($"Радиус {magnet.Radius:0.0} · сила {magnet.Strength:0.0} · {PolarityName(magnet.Polarity)}"),
            AttenuatingRadiationBarrier barrier => new GUIContent(barrier.opaque ? "Полностью блокирует радиацию" : $"Плотность {barrier.density:0.00} · коэффициент {barrier.attenuationCoefficient:0.00}"),
            RadiationMirror mirror => new GUIContent($"Отражение {mirror.reflectivity:0.00}"),
            RadiationSpeedReducer reducer => new GUIContent($"Скорость ×{reducer.speedMultiplier:0.00} · потери {reducer.intensityLossPerUnit:0.00}"),
            RadiationDosimeter dosimeter => new GUIContent($"Доза {dosimeter.Dose:0.00} / {dosimeter.DoseToRed:0.00}"),
            RadiationDosimeterField field => new GUIContent($"Сфер {field.Count} · размер {field.SphereSize:0.00} · шаг {field.Spacing:0.00}"),
            _ => null
        };

        private static string ParticleTypeName(RadiationParticleType type) =>
            type == RadiationParticleType.Beta ? "β-бета" : "α-альфа";

        private static string ShapeName(RadiationFlowShape shape) => shape switch
        {
            RadiationFlowShape.Cone => "конус",
            RadiationFlowShape.Ray => "луч",
            _ => "сфера"
        };

        private static string PolarityName(RadiationMagnetPolarity polarity) =>
            polarity == RadiationMagnetPolarity.Attract ? "B +локальная Y" : "B −локальная Y";

        private void DrawCritical(CriticalRadiationObject critical)
        {
            GUILayout.Label($"Критический объект | доза {critical.AccumulatedDose:0.00} / {critical.CriticalDose:0.00}", labelStyle);
            GUILayout.Label($"Порог срабатывания: {critical.CriticalDose:0.00}", mutedStyle);
            float criticalDose = GUILayout.HorizontalSlider(critical.CriticalDose, .01f, 100f, sliderStyle, sliderThumbStyle);
            GUILayout.Label($"Доза в секунду на интенсивность: {critical.DosePerIntensitySecond:0.00}", mutedStyle);
            float doseRate = GUILayout.HorizontalSlider(critical.DosePerIntensitySecond, 0f, 20f, sliderStyle, sliderThumbStyle);
            GUILayout.Label($"Эмиссия после активации: {critical.ActivatedEmissionRate:0.0}/с", mutedStyle);
            float emissionRate = GUILayout.HorizontalSlider(critical.ActivatedEmissionRate, 0f, 200f, sliderStyle, sliderThumbStyle);
            GUILayout.Label($"Интенсивность после активации: {critical.ActivatedIntensity:0.00}", mutedStyle);
            float intensity = GUILayout.HorizontalSlider(critical.ActivatedIntensity, 0f, 20f, sliderStyle, sliderThumbStyle);
            bool destroy = GUILayout.Toggle(critical.DestroyIncomingParticles, "Уничтожать входящие частицы", buttonStyle);
            critical.ConfigureAdvanced(criticalDose, doseRate, emissionRate, intensity, destroy);

            GUILayout.Space(4);
            RadiationSource activated = critical.ActivatedSource;
            GUILayout.Label(activated != null
                ? $"Связанный излучатель: {activated.name} ({(critical.IsActivated ? "активен" : "выключен")})"
                : "Связанный излучатель не назначен", mutedStyle);

            if (GUILayout.Button("Сбросить накопленную дозу", buttonStyle)) critical.ResetAccumulatedDose();

            RadiationSource emitter = FirstOtherEmitter(activated);
            if (emitter != null)
            {
                GUILayout.Label($"Источник-образец: {emitter.name}", mutedStyle);
                if (activated != null && GUILayout.Button("Синхронизировать частицы с эмиттером", buttonStyle))
                    critical.SyncActivatedSourceFrom(emitter);
            }
        }

        private static RadiationSource FirstOtherEmitter(RadiationSource exclude)
        {
            RadiationSource[] emitters = FindObjectsByType<RadiationSource>(FindObjectsSortMode.None);
            for (int i = 0; i < emitters.Length; i++)
            {
                if (emitters[i] != null && emitters[i] != exclude) return emitters[i];
            }
            return null;
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
            GUILayout.Label($"{newRate:0.0}/с  —  0 полностью останавливает эмиссию", mutedStyle);
            float newSpeed = GUILayout.HorizontalSlider(speed, .1f, 50f, sliderStyle, sliderThumbStyle);
            float newLife = GUILayout.HorizontalSlider(life, .1f, 30f, sliderStyle, sliderThumbStyle);
            if (!Mathf.Approximately(newRate, rate) || !Mathf.Approximately(newSpeed, speed) || !Mathf.Approximately(newLife, life))
                s.ConfigureEmissionProperties(newRate, newSpeed, newLife, variation, s.InitialIntensity);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Альфа", s.ParticleType == RadiationParticleType.Alpha ? activeButtonStyle : buttonStyle)) s.SetParticleType(RadiationParticleType.Alpha);
            if (GUILayout.Button("Бета", s.ParticleType == RadiationParticleType.Beta ? activeButtonStyle : buttonStyle)) s.SetParticleType(RadiationParticleType.Beta);
            GUILayout.EndHorizontal();
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
            if (GUILayout.Button("B вверх", polarity == RadiationMagnetPolarity.Attract ? activeButtonStyle : buttonStyle)) polarity = RadiationMagnetPolarity.Attract;
            if (GUILayout.Button("B вниз", polarity == RadiationMagnetPolarity.Repel ? activeButtonStyle : buttonStyle)) polarity = RadiationMagnetPolarity.Repel;
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
