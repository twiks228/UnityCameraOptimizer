using UnityEngine;
using System.Collections;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class CameraOptimizer : MonoBehaviour
{
    // =========================================================================
    // SETTINGS CLASSES
    // =========================================================================

    [System.Serializable]
    public class OcclusionSettings
    {
        [Tooltip("Layers that physically block visibility (walls, terrain, etc.)")]
        public LayerMask blockerLayers = ~0;

        [Tooltip("Use multiple ray points on bounding box for better accuracy")]
        public bool useMultiRay = true;

        [Tooltip("Number of sample points per object (1 = center only, 7 = all axes)")]
        [Range(1, 7)]
        public int rayCount = 3;

        [Tooltip("Reuse cached occlusion result for this many frames before retesting")]
        [Min(1)]
        public int cacheFrames = 2;
    }

    [System.Serializable]
    public class FadeSettings
    {
        [Tooltip("Fade-in duration in seconds. Set 0 for instant appear")]
        [Min(0f)]
        public float fadeDuration = 0.5f;

        [Tooltip("Only objects closer than this distance will fade in")]
        [Min(0f)]
        public float minFadeDistance = 5f;

        [Tooltip("Alpha animation curve over fade duration")]
        public AnimationCurve fadeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Shader property name used for alpha (must exist in shader)")]
        public string alphaPropertyName = "_Alpha";
    }

    [System.Serializable]
    public class LODLevel
    {
        [Tooltip("Maximum distance for this LOD level to be active")]
        [Min(0f)]
        public float maxDistance = 50f;

        [Tooltip("Quality multiplier sent to shader via _Quality property (0..1)")]
        [Range(0f, 1f)]
        public float qualityMultiplier = 1f;

        [Tooltip("Shadow casting mode at this LOD level")]
        public UnityEngine.Rendering.ShadowCastingMode shadowMode =
            UnityEngine.Rendering.ShadowCastingMode.On;

        [Tooltip("Whether to receive shadows at this LOD level")]
        public bool receiveShadows = true;
    }

    [System.Serializable]
    public class LODSettings
    {
        [Tooltip("LOD levels, automatically sorted by distance on start")]
        public List<LODLevel> levels = new List<LODLevel>
        {
            new LODLevel
            {
                maxDistance        = 20f,
                qualityMultiplier = 1.0f,
                shadowMode        = UnityEngine.Rendering.ShadowCastingMode.On,
                receiveShadows    = true
            },
            new LODLevel
            {
                maxDistance        = 50f,
                qualityMultiplier = 0.6f,
                shadowMode        = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows    = true
            },
            new LODLevel
            {
                maxDistance        = 100f,
                qualityMultiplier = 0.3f,
                shadowMode        = UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows    = false
            }
        };

        [Tooltip("Child object name toggled by quality threshold")]
        public string detailObjectName = "Detail";

        [Tooltip("Quality value above which the detail child object is enabled")]
        [Range(0f, 1f)]
        public float detailQualityThreshold = 0.5f;
    }

    [System.Serializable]
    public class PredictiveSettings
    {
        [Tooltip("Enable predictive culling based on object velocity")]
        public bool enabled = true;

        [Tooltip("How many seconds ahead to predict object position")]
        [Range(0f, 2f)]
        public float predictionTime = 0.2f;

        [Tooltip("Minimum speed (m/s) for an object to be considered moving")]
        [Min(0f)]
        public float minSpeedThreshold = 0.5f;
    }

    [System.Serializable]
    public class StatisticsSettings
    {
        [Tooltip("Collect and display performance statistics")]
        public bool enabled = true;

        [Tooltip("How often statistics are recalculated (seconds)")]
        [Min(0.1f)]
        public float updateInterval = 0.5f;
    }

    [System.Serializable]
    public class DebugOverlaySettings
    {
        [Tooltip("Show on-screen debug overlay in Play Mode")]
        public bool enabled = true;

        [Tooltip("Screen position of the overlay")]
        public Vector2 position = new Vector2(10f, 10f);

        [Tooltip("Text color of the overlay")]
        public Color textColor = Color.white;

        [Tooltip("Background color of the overlay panel")]
        public Color backgroundColor = new Color(0f, 0f, 0f, 0.6f);
    }

    [System.Serializable]
    public class GizmoSettings
    {
        public bool showRenderDistanceSphere = true;
        public bool showFadeDistanceSphere = true;
        public bool showFrustum = true;
        public bool showOcclusionRays = true;
        public bool showLabels = true;
        public bool showPredictedPositions = true;
        public bool showZones = true;

        [Min(1)]
        public int maxObjectsToVisualize = 50;

        public Color visibleColor = Color.green;
        public Color occludedColor = Color.red;
        public Color predictedColor = Color.cyan;
        public Color zoneColor = new Color(0.8f, 0.4f, 1f, 0.3f);

        [Min(0.1f)]
        public float labelSize = 1f;
    }

    [System.Serializable]
    public class OptimizationZone
    {
        [Tooltip("Zone identifier for debugging")]
        public string zoneName = "Zone";

        [Tooltip("World-space center of the zone")]
        public Vector3 center = Vector3.zero;

        [Tooltip("Radius of the zone")]
        [Min(0f)]
        public float radius = 20f;

        [Tooltip("Max render distance override inside this zone")]
        [Min(0f)]
        public float renderDistanceOverride = 50f;

        [Tooltip("Update interval override inside this zone (seconds)")]
        [Min(0.01f)]
        public float updateIntervalOverride = 0.05f;
    }

    public class RuntimeStatistics
    {
        public int totalRenderers;
        public int visibleRenderers;
        public int occludedRenderers;
        public int culledByDistance;
        public int culledByFrustum;
        public int savedDrawCalls;
        public float averageFps;
        public float averageCullingTimeMs;
        public int activeCoroutines;
        public int occlusionCacheSize;
        public int currentLODLevel;
    }

    // =========================================================================
    // SERIALIZED FIELDS
    // =========================================================================

    [Header("Camera Reference")]
    [Tooltip("Target camera to optimize for.\n" +
             "• Drag any Camera here from the scene\n" +
             "• Leave empty to auto-detect Camera.main\n" +
             "• If this script is ON a Camera, it auto-links to it")]
    [SerializeField]
    private Camera targetCamera;

    [Header("Culling")]
    [Tooltip("Objects beyond this distance are disabled entirely")]
    [SerializeField, Min(0f)]
    private float maxRenderDistance = 100f;

    [Tooltip("Seconds between full culling evaluation passes")]
    [SerializeField, Min(0.01f)]
    private float updateInterval = 0.1f;

    [Tooltip("Seconds between null-renderer cleanup passes")]
    [SerializeField, Min(1f)]
    private float cleanupInterval = 5f;

    [Tooltip("Only renderers on these layers are managed by this optimizer")]
    [SerializeField]
    private LayerMask cullingLayers = ~0;

    [Tooltip("Renderers on these layers are always skipped")]
    [SerializeField]
    private LayerMask excludeLayers = 0;

    [Header("Occlusion")]
    [SerializeField]
    private OcclusionSettings occlusionSettings = new OcclusionSettings();

    [Header("Fade")]
    [SerializeField]
    private FadeSettings fadeSettings = new FadeSettings();

    [Header("LOD")]
    [SerializeField]
    private LODSettings lodSettings = new LODSettings();

    [Header("Predictive Culling")]
    [SerializeField]
    private PredictiveSettings predictiveSettings = new PredictiveSettings();

    [Header("Statistics")]
    [SerializeField]
    private StatisticsSettings statisticsSettings = new StatisticsSettings();

    [Header("Debug Overlay")]
    [SerializeField]
    private DebugOverlaySettings debugOverlay = new DebugOverlaySettings();

    [Header("Optimization Zones")]
    [Tooltip("Areas with custom render distance and update interval overrides")]
    [SerializeField]
    private List<OptimizationZone> optimizationZones = new List<OptimizationZone>();

    [Header("Gizmos")]
    [SerializeField]
    private GizmoSettings gizmoSettings = new GizmoSettings();

    // =========================================================================
    // PRIVATE RUNTIME — all initialized in Awake
    // =========================================================================

    // Resolved camera reference (never null after successful init)
    private Camera _resolvedCamera;

    // Registry
    private List<Renderer> _renderers;
    private HashSet<Renderer> _rendererSet;

    // Culling
    private Plane[] _frustumPlanes;
    private float _maxRenderDistSq;

    // Occlusion
    private Dictionary<Renderer, (int frame, bool visible)> _occlusionCache;
    private Vector3[] _rayPoints;

    // Fade / PropertyBlock
    private MaterialPropertyBlock _propertyBlock;
    private int _alphaPropertyId;
    private int _qualityPropertyId;
    private int _lodLevelPropertyId;

    private Dictionary<Renderer, Coroutine> _fadeCoroutines;
    private Dictionary<Renderer, float> _currentAlpha;

    // Visibility
    private Dictionary<Renderer, bool> _visibilityCache;

    // Predictive
    private Dictionary<Renderer, Vector3> _previousPositions;
    private Dictionary<Renderer, Vector3> _velocities;

    // Statistics
    private RuntimeStatistics _statistics;
    private float _nextStatsUpdateTime;
    private float _lastCullingTimeMs;
    private float _fpsAccumulator;
    private int _fpsFrameCount;
    private int _frameVisible;
    private int _frameOccluded;
    private int _frameCulledDist;
    private int _frameCulledFrustum;

    // Timers
    private float _nextUpdateTime;
    private float _nextCleanupTime;

    // GUI
    private GUIStyle _overlayStyle;
    private GUIStyle _overlayBgStyle;

    // Initialization flag
    private bool _initialized;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================

    private void Awake()
    {
        // Resolve camera with priority chain
        _resolvedCamera = ResolveCamera();

        if (_resolvedCamera == null)
        {
            Debug.LogError(
                "[CameraOptimizer] No camera found!\n" +
                "Options:\n" +
                "  1. Drag a Camera into the 'Target Camera' field in Inspector\n" +
                "  2. Tag your camera as 'MainCamera'\n" +
                "  3. Place this script on a GameObject that has a Camera component",
                this);
            enabled = false;
            return;
        }

        Debug.Log($"[CameraOptimizer] Using camera: '{_resolvedCamera.name}'", _resolvedCamera);

        InitializeRuntime();
    }

    private void Start()
    {
        if (!_initialized) return;
        ScanScene();
    }

    private void Update()
    {
        if (!_initialized) return;

        // Re-validate camera in case it was destroyed at runtime
        if (_resolvedCamera == null)
        {
            _resolvedCamera = ResolveCamera();
            if (_resolvedCamera == null)
            {
                Debug.LogWarning("[CameraOptimizer] Camera lost! Waiting for reassignment...");
                return;
            }
            Debug.Log($"[CameraOptimizer] Camera re-resolved: '{_resolvedCamera.name}'");
        }

        float time = Time.time;

        _fpsAccumulator += Time.unscaledDeltaTime;
        _fpsFrameCount++;

        if (time >= _nextCleanupTime)
        {
            _nextCleanupTime = time + cleanupInterval;
            PerformCleanup();
        }

        if (time >= _nextUpdateTime)
        {
            float effectiveInterval = GetEffectiveUpdateInterval();
            _nextUpdateTime = time + effectiveInterval;
            PerformCullingUpdate();
        }

        if (statisticsSettings.enabled && time >= _nextStatsUpdateTime)
        {
            _nextStatsUpdateTime = time + statisticsSettings.updateInterval;
            RefreshStatistics();
        }
    }

    private void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (!debugOverlay.enabled) return;
        if (_statistics == null) return;

        DrawDebugOverlay();
    }

    private void OnDestroy()
    {
        StopAllFadeCoroutines();
    }

    // =========================================================================
    // CAMERA RESOLUTION — Priority Chain
    // =========================================================================

    /// <summary>
    /// Resolves which camera to use with the following priority:
    /// 1. Explicitly assigned targetCamera field (drag-drop in Inspector)
    /// 2. Camera component on the same GameObject (if script is on camera)
    /// 3. Camera.main (any camera tagged MainCamera in the scene)
    /// 4. First Camera found in the scene via FindAnyObjectByType
    /// Returns null if absolutely no camera exists.
    /// </summary>
    private Camera ResolveCamera()
    {
        // Priority 1: Explicitly assigned in Inspector
        if (targetCamera != null)
            return targetCamera;

        // Priority 2: Camera on the same GameObject
        if (TryGetComponent<Camera>(out var selfCamera))
            return selfCamera;

        // Priority 3: Scene's main camera (tagged MainCamera)
        if (Camera.main != null)
            return Camera.main;

        // Priority 4: Any camera in the scene
        var anyCamera = FindAnyObjectByType<Camera>();
        if (anyCamera != null)
            return anyCamera;

        return null;
    }

    /// <summary>
    /// Assign a different camera at runtime.
    /// Example: after switching between cameras in a cutscene.
    /// </summary>
    public void SetTargetCamera(Camera camera)
    {
        if (camera == null)
        {
            Debug.LogWarning("[CameraOptimizer] SetTargetCamera called with null!");
            return;
        }

        targetCamera = camera;
        _resolvedCamera = camera;
        Debug.Log($"[CameraOptimizer] Camera changed to: '{camera.name}'", camera);
    }

    /// <summary>Returns the currently active camera used for culling.</summary>
    public Camera GetActiveCamera() => _resolvedCamera;

    // =========================================================================
    // INITIALIZATION
    // =========================================================================

    private void InitializeRuntime()
    {
        _renderers = new List<Renderer>(256);
        _rendererSet = new HashSet<Renderer>();

        _frustumPlanes = new Plane[6];
        _rayPoints = new Vector3[7];

        _occlusionCache = new Dictionary<Renderer, (int, bool)>(256);
        _fadeCoroutines = new Dictionary<Renderer, Coroutine>(64);
        _currentAlpha = new Dictionary<Renderer, float>(64);
        _visibilityCache = new Dictionary<Renderer, bool>(256);

        _previousPositions = new Dictionary<Renderer, Vector3>(256);
        _velocities = new Dictionary<Renderer, Vector3>(256);

        // MaterialPropertyBlock MUST be created in Awake or later
        _propertyBlock = new MaterialPropertyBlock();

        _statistics = new RuntimeStatistics();

        CacheShaderPropertyIds();
        UpdateMaxDistanceSq();
        ValidateAndSortLODLevels();

        _initialized = true;
    }

    // =========================================================================
    // PUBLIC API — Registration
    // =========================================================================

    public void RegisterRenderer(Renderer renderer)
    {
        if (!_initialized) return;
        TryAddRenderer(renderer);
    }

    public void UnregisterRenderer(Renderer renderer)
    {
        if (!_initialized || renderer == null) return;

        _rendererSet.Remove(renderer);
        _renderers.Remove(renderer);
        _occlusionCache.Remove(renderer);
        _visibilityCache.Remove(renderer);
        _previousPositions.Remove(renderer);
        _velocities.Remove(renderer);

        StopFadeCoroutine(renderer);
        _currentAlpha.Remove(renderer);
    }

    // =========================================================================
    // PUBLIC API — Configuration
    // =========================================================================

    public void SetMaxRenderDistance(float distance)
    {
        maxRenderDistance = Mathf.Max(0f, distance);
        UpdateMaxDistanceSq();
    }

    public void SetUpdateInterval(float interval)
    {
        updateInterval = Mathf.Max(0.01f, interval);
    }

    public void AddLODLevel(LODLevel level)
    {
        if (level == null) return;
        lodSettings.levels.Add(level);
        ValidateAndSortLODLevels();
    }

    public void RemoveLODLevel(int index)
    {
        if (lodSettings.levels.Count <= 1) return;
        lodSettings.levels.RemoveAt(
            Mathf.Clamp(index, 0, lodSettings.levels.Count - 1));
    }

    public void AddZone(OptimizationZone zone)
    {
        if (zone != null) optimizationZones.Add(zone);
    }

    public void RemoveZone(int index)
    {
        if (index >= 0 && index < optimizationZones.Count)
            optimizationZones.RemoveAt(index);
    }

    /// <summary>
    /// Force an immediate culling update outside of the normal interval.
    /// Useful after teleporting the camera or loading new geometry.
    /// </summary>
    public void ForceUpdate()
    {
        if (_initialized) PerformCullingUpdate();
    }

    /// <summary>
    /// Force a full re-scan of the scene for renderers.
    /// Use after loading new scene content additively.
    /// </summary>
    public void RescanScene()
    {
        if (_initialized) ScanScene();
    }

    public IReadOnlyList<LODLevel> GetLODLevels() => lodSettings.levels;
    public IReadOnlyList<OptimizationZone> GetZones() => optimizationZones;
    public RuntimeStatistics GetStatistics() => _statistics;

    public bool IsRendererVisible(Renderer renderer)
        => _visibilityCache != null
           && _visibilityCache.TryGetValue(renderer, out bool v) && v;

    public int ResolveLODIndex(float distance)
    {
        for (int i = 0; i < lodSettings.levels.Count; i++)
            if (distance <= lodSettings.levels[i].maxDistance)
                return i;
        return lodSettings.levels.Count - 1;
    }

    // =========================================================================
    // REGISTRY
    // =========================================================================

    private void ScanScene()
    {
        _renderers.Clear();
        _rendererSet.Clear();

        var all = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var r in all)
            TryAddRenderer(r);

        Debug.Log($"[CameraOptimizer] Registered {_renderers.Count} / {all.Length} renderers.");
    }

    private bool TryAddRenderer(Renderer renderer)
    {
        if (renderer == null) return false;
        if (_rendererSet.Contains(renderer)) return false;
        if (!renderer.gameObject.activeInHierarchy) return false;

        int layer = renderer.gameObject.layer;
        bool inInclude = (cullingLayers.value & (1 << layer)) != 0;
        bool inExclude = (excludeLayers.value & (1 << layer)) != 0;
        if (!inInclude || inExclude) return false;

        _renderers.Add(renderer);
        _rendererSet.Add(renderer);
        _previousPositions[renderer] = renderer.bounds.center;
        _velocities[renderer] = Vector3.zero;

        return true;
    }

    // =========================================================================
    // CLEANUP
    // =========================================================================

    private void PerformCleanup()
    {
        int removed = 0;
        for (int i = _renderers.Count - 1; i >= 0; i--)
        {
            if (_renderers[i] != null) continue;
            _renderers.RemoveAt(i);
            removed++;
        }

        if (removed > 0)
        {
            _rendererSet.Clear();
            foreach (var r in _renderers)
                _rendererSet.Add(r);
            Debug.Log($"[CameraOptimizer] Cleaned up {removed} destroyed renderers.");
        }
    }

    // =========================================================================
    // CULLING UPDATE
    // =========================================================================

    private void PerformCullingUpdate()
    {
        if (_resolvedCamera == null) return;

        float startTime = Time.realtimeSinceStartup;

        Vector3 camPos = _resolvedCamera.transform.position;
        float effectiveDist = GetEffectiveRenderDistance(camPos);
        float effectiveDistSq = effectiveDist * effectiveDist;

        GeometryUtility.CalculateFrustumPlanes(_resolvedCamera, _frustumPlanes);

        _frameVisible = 0;
        _frameOccluded = 0;
        _frameCulledDist = 0;
        _frameCulledFrustum = 0;

        for (int i = 0; i < _renderers.Count; i++)
        {
            Renderer r = _renderers[i];
            if (!IsRendererAlive(r)) continue;
            if (!r.gameObject.activeInHierarchy) continue;

            UpdateVelocity(r);

            Bounds bounds = r.bounds;
            Vector3 evalPos = GetEvaluationPosition(r, bounds.center);
            float distSq = (evalPos - camPos).sqrMagnitude;
            float distance = Mathf.Sqrt(distSq);

            bool visible = EvaluateVisibility(r, bounds, evalPos,
                                                 distSq, effectiveDistSq, distance);
            bool wasVisible = _visibilityCache.TryGetValue(r, out bool prev) && prev;

            _visibilityCache[r] = visible;

            if (visible) _frameVisible++;
            else _frameOccluded++;

            if (visible != wasVisible || visible)
                ApplyVisibility(r, visible, distance);
        }

        _lastCullingTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;
    }

    // =========================================================================
    // PREDICTIVE
    // =========================================================================

    private void UpdateVelocity(Renderer renderer)
    {
        if (!predictiveSettings.enabled) return;

        Vector3 currentPos = renderer.bounds.center;
        if (_previousPositions.TryGetValue(renderer, out Vector3 prevPos))
        {
            float dt = Time.deltaTime > 0f ? Time.deltaTime : 0.016f;
            _velocities[renderer] = (currentPos - prevPos) / dt;
        }
        _previousPositions[renderer] = currentPos;
    }

    private Vector3 GetEvaluationPosition(Renderer renderer, Vector3 currentCenter)
    {
        if (!predictiveSettings.enabled) return currentCenter;
        if (!_velocities.TryGetValue(renderer, out Vector3 vel)) return currentCenter;
        if (vel.sqrMagnitude < predictiveSettings.minSpeedThreshold
                               * predictiveSettings.minSpeedThreshold)
            return currentCenter;

        return currentCenter + vel * predictiveSettings.predictionTime;
    }

    // =========================================================================
    // VISIBILITY
    // =========================================================================

    private bool EvaluateVisibility(
        Renderer renderer, Bounds bounds, Vector3 evalPosition,
        float distSq, float effectiveDistSq, float distance)
    {
        if (distSq > effectiveDistSq)
        {
            _frameCulledDist++;
            return false;
        }

        if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds))
        {
            _frameCulledFrustum++;
            return false;
        }

        if (!EvaluateOcclusion(renderer, distance))
            return false;

        return true;
    }

    // =========================================================================
    // OCCLUSION
    // =========================================================================

    private bool EvaluateOcclusion(Renderer renderer, float distance)
    {
        int frame = Time.frameCount;
        if (_occlusionCache.TryGetValue(renderer, out var cached))
            if (frame - cached.frame < occlusionSettings.cacheFrames)
                return cached.visible;

        bool visible = PerformRaycastTest(renderer, distance);
        _occlusionCache[renderer] = (frame, visible);
        return visible;
    }

    private bool PerformRaycastTest(Renderer renderer, float distance)
    {
        if (_resolvedCamera == null || renderer == null) return true;

        Vector3 camPos = _resolvedCamera.transform.position;
        int count = BuildSamplePoints(renderer.bounds, occlusionSettings.rayCount);
        int visHits = 0;

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = _rayPoints[i] - camPos;
            float rayLength = dir.magnitude;

            if (rayLength < 0.001f) { visHits++; continue; }
            dir /= rayLength;

            if (Physics.Raycast(new Ray(camPos, dir), out RaycastHit hit,
                    rayLength, occlusionSettings.blockerLayers))
            {
                if (hit.collider != null &&
                    hit.collider.gameObject == renderer.gameObject)
                    visHits++;
            }
            else
            {
                visHits++;
            }
        }

        return visHits > 0;
    }

    private int BuildSamplePoints(Bounds bounds, int requested)
    {
        int count = Mathf.Clamp(requested, 1, 7);
        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;

        _rayPoints[0] = c;
        if (count >= 2) _rayPoints[1] = c + new Vector3(e.x, 0f, 0f);
        if (count >= 3) _rayPoints[2] = c + new Vector3(-e.x, 0f, 0f);
        if (count >= 4) _rayPoints[3] = c + new Vector3(0f, e.y, 0f);
        if (count >= 5) _rayPoints[4] = c + new Vector3(0f, -e.y, 0f);
        if (count >= 6) _rayPoints[5] = c + new Vector3(0f, 0f, e.z);
        if (count >= 7) _rayPoints[6] = c + new Vector3(0f, 0f, -e.z);

        return count;
    }

    // =========================================================================
    // APPLY VISIBILITY
    // =========================================================================

    private void ApplyVisibility(Renderer renderer, bool shouldRender, float distance)
    {
        if (renderer == null) return;

        if (shouldRender)
        {
            bool justEnabled = !renderer.enabled;
            renderer.enabled = true;

            if (justEnabled)
            {
                if (ShouldFade(distance))
                    StartFadeIn(renderer);
                else
                    SetAlphaImmediate(renderer, 1f);
            }

            ApplyLOD(renderer, distance);
        }
        else
        {
            if (renderer.enabled)
            {
                StopFadeCoroutine(renderer);
                SetAlphaImmediate(renderer, 0f);
                renderer.enabled = false;
            }
        }
    }

    // =========================================================================
    // FADE
    // =========================================================================

    private bool ShouldFade(float distance)
        => fadeSettings.fadeDuration > 0f && distance < fadeSettings.minFadeDistance;

    private void StartFadeIn(Renderer renderer)
    {
        StopFadeCoroutine(renderer);
        _fadeCoroutines[renderer] = StartCoroutine(FadeInRoutine(renderer));
    }

    private void StopFadeCoroutine(Renderer renderer)
    {
        if (!_fadeCoroutines.TryGetValue(renderer, out Coroutine c)) return;
        if (c != null) StopCoroutine(c);
        _fadeCoroutines.Remove(renderer);
    }

    private void StopAllFadeCoroutines()
    {
        if (_fadeCoroutines == null) return;
        foreach (var kvp in _fadeCoroutines)
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        _fadeCoroutines.Clear();
    }

    private IEnumerator FadeInRoutine(Renderer renderer)
    {
        float start = _currentAlpha.TryGetValue(renderer, out float p) ? p : 0f;
        float elapsed = 0f;

        while (elapsed < fadeSettings.fadeDuration)
        {
            if (renderer == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeSettings.fadeDuration);
            float alpha = Mathf.Lerp(start, 1f, fadeSettings.fadeCurve.Evaluate(t));
            ApplyAlpha(renderer, alpha);
            yield return null;
        }

        if (renderer != null) ApplyAlpha(renderer, 1f);
        _fadeCoroutines.Remove(renderer);
    }

    private void SetAlphaImmediate(Renderer renderer, float alpha)
    {
        StopFadeCoroutine(renderer);
        ApplyAlpha(renderer, alpha);
    }

    private void ApplyAlpha(Renderer renderer, float alpha)
    {
        if (renderer == null || _propertyBlock == null) return;
        _currentAlpha[renderer] = alpha;
        renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetFloat(_alphaPropertyId, alpha);
        renderer.SetPropertyBlock(_propertyBlock);
    }

    // =========================================================================
    // LOD
    // =========================================================================

    private void ApplyLOD(Renderer renderer, float distance)
    {
        if (renderer == null || _propertyBlock == null) return;
        if (renderer.gameObject.TryGetComponent<LODGroup>(out _)) return;

        int lodIdx = ResolveLODIndex(distance);
        LODLevel level = lodSettings.levels[lodIdx];

        renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetFloat(_qualityPropertyId, level.qualityMultiplier);
        _propertyBlock.SetInt(_lodLevelPropertyId, lodIdx);
        renderer.SetPropertyBlock(_propertyBlock);

        renderer.shadowCastingMode = level.shadowMode;
        renderer.receiveShadows = level.receiveShadows;

        Transform detail = renderer.transform.Find(lodSettings.detailObjectName);
        if (detail != null)
        {
            bool show = level.qualityMultiplier > lodSettings.detailQualityThreshold;
            if (detail.gameObject.activeSelf != show)
                detail.gameObject.SetActive(show);
        }
    }

    // =========================================================================
    // ZONES
    // =========================================================================

    private float GetEffectiveRenderDistance(Vector3 camPos)
    {
        foreach (var zone in optimizationZones)
        {
            if (zone == null) continue;
            if ((zone.center - camPos).sqrMagnitude <= zone.radius * zone.radius)
                return zone.renderDistanceOverride;
        }
        return maxRenderDistance;
    }

    private float GetEffectiveUpdateInterval()
    {
        if (_resolvedCamera == null) return updateInterval;
        Vector3 camPos = _resolvedCamera.transform.position;

        foreach (var zone in optimizationZones)
        {
            if (zone == null) continue;
            if ((zone.center - camPos).sqrMagnitude <= zone.radius * zone.radius)
                return zone.updateIntervalOverride;
        }
        return updateInterval;
    }

    // =========================================================================
    // STATISTICS
    // =========================================================================

    private void RefreshStatistics()
    {
        if (_statistics == null) return;

        _statistics.totalRenderers = _renderers.Count;
        _statistics.visibleRenderers = _frameVisible;
        _statistics.occludedRenderers = _frameOccluded;
        _statistics.culledByDistance = _frameCulledDist;
        _statistics.culledByFrustum = _frameCulledFrustum;
        _statistics.savedDrawCalls = _renderers.Count - _frameVisible;
        _statistics.activeCoroutines = _fadeCoroutines.Count;
        _statistics.occlusionCacheSize = _occlusionCache.Count;
        _statistics.averageCullingTimeMs = _lastCullingTimeMs;

        if (_fpsFrameCount > 0)
        {
            _statistics.averageFps = _fpsFrameCount / _fpsAccumulator;
            _fpsAccumulator = 0f;
            _fpsFrameCount = 0;
        }

        _statistics.currentLODLevel = ResolveLODIndex(
            _resolvedCamera != null
                ? Vector3.Distance(_resolvedCamera.transform.position, Vector3.zero)
                : 0f);
    }

    // =========================================================================
    // DEBUG OVERLAY
    // =========================================================================

    private void DrawDebugOverlay()
    {
        if (_overlayStyle == null)
        {
            _overlayStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            _overlayStyle.normal.textColor = debugOverlay.textColor;
        }

        if (_overlayBgStyle == null)
        {
            _overlayBgStyle = new GUIStyle(GUI.skin.box);
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, debugOverlay.backgroundColor);
            tex.Apply();
            _overlayBgStyle.normal.background = tex;
        }

        var s = _statistics;
        string camName = _resolvedCamera != null ? _resolvedCamera.name : "NONE";

        string text =
            $"=== CameraOptimizer ===\n" +
            $"Camera         : {camName}\n" +
            $"FPS            : {s.averageFps:F1}\n" +
            $"Culling ms     : {s.averageCullingTimeMs:F2}\n" +
            $"Total renderers: {s.totalRenderers}\n" +
            $"Visible        : {s.visibleRenderers}\n" +
            $"Occluded       : {s.occludedRenderers}\n" +
            $"Culled (dist)  : {s.culledByDistance}\n" +
            $"Culled (frust) : {s.culledByFrustum}\n" +
            $"Saved DrawCalls: {s.savedDrawCalls}\n" +
            $"Active Fades   : {s.activeCoroutines}\n" +
            $"Occlusion Cache: {s.occlusionCacheSize}";

        float w = 260f;
        float h = 240f;
        var rect = new Rect(debugOverlay.position.x, debugOverlay.position.y, w, h);

        GUI.Box(rect, GUIContent.none, _overlayBgStyle);
        GUI.Label(rect, text, _overlayStyle);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private void CacheShaderPropertyIds()
    {
        _alphaPropertyId = Shader.PropertyToID(fadeSettings.alphaPropertyName);
        _qualityPropertyId = Shader.PropertyToID("_Quality");
        _lodLevelPropertyId = Shader.PropertyToID("_LODLevel");
    }

    private void UpdateMaxDistanceSq()
    {
        _maxRenderDistSq = maxRenderDistance * maxRenderDistance;
    }

    private void ValidateAndSortLODLevels()
    {
        if (lodSettings.levels == null || lodSettings.levels.Count == 0)
        {
            lodSettings.levels = new List<LODLevel>
            {
                new LODLevel { maxDistance = 20f,  qualityMultiplier = 1.0f },
                new LODLevel { maxDistance = 50f,  qualityMultiplier = 0.6f },
                new LODLevel { maxDistance = 100f, qualityMultiplier = 0.3f }
            };
            Debug.LogWarning("[CameraOptimizer] LOD levels were empty — defaults applied.");
        }
        lodSettings.levels.Sort((a, b) => a.maxDistance.CompareTo(b.maxDistance));
    }

    private static bool IsRendererAlive(Renderer r)
    {
        try { return r != null && r.gameObject != null; }
        catch { return false; }
    }

    // =========================================================================
    // GIZMOS
    // =========================================================================

    /// <summary>
    /// Gets the best available camera for Gizmo drawing.
    /// Works both in Edit Mode and Play Mode.
    /// </summary>
    private Camera GetGizmoCamera()
    {
        if (_resolvedCamera != null) return _resolvedCamera;
        if (targetCamera != null) return targetCamera;

        // Fallback: try same-object camera (works in Edit Mode)
        TryGetComponent<Camera>(out var selfCam);
        if (selfCam != null) return selfCam;

        return Camera.main;
    }

    private void OnDrawGizmos()
    {
        Camera cam = GetGizmoCamera();
        if (cam == null) return;

        Vector3 pos = cam.transform.position;

        if (gizmoSettings.showRenderDistanceSphere)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.04f);
            Gizmos.DrawSphere(pos, maxRenderDistance);
            Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
            Gizmos.DrawWireSphere(pos, maxRenderDistance);
        }

        if (gizmoSettings.showFadeDistanceSphere)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.04f);
            Gizmos.DrawSphere(pos, fadeSettings.minFadeDistance);
            Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
            Gizmos.DrawWireSphere(pos, fadeSettings.minFadeDistance);
        }

        if (gizmoSettings.showFrustum)
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.12f);
            Gizmos.matrix = Matrix4x4.TRS(pos, cam.transform.rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, cam.fieldOfView,
                maxRenderDistance, cam.nearClipPlane, cam.aspect);
            Gizmos.matrix = Matrix4x4.identity;
        }

        if (gizmoSettings.showZones && optimizationZones != null)
        {
            foreach (var zone in optimizationZones)
            {
                if (zone == null) continue;
                Gizmos.color = gizmoSettings.zoneColor;
                Gizmos.DrawSphere(zone.center, zone.radius);
                Gizmos.color = new Color(
                    gizmoSettings.zoneColor.r,
                    gizmoSettings.zoneColor.g,
                    gizmoSettings.zoneColor.b, 0.8f);
                Gizmos.DrawWireSphere(zone.center, zone.radius);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Camera cam = GetGizmoCamera();
        if (cam == null || _renderers == null) return;

        Vector3 camPos = cam.transform.position;
        int drawn = 0;

        for (int i = 0; i < _renderers.Count; i++)
        {
            if (drawn >= gizmoSettings.maxObjectsToVisualize) break;

            Renderer r = _renderers[i];
            if (!IsRendererAlive(r)) continue;
            if (!r.gameObject.activeInHierarchy) continue;

            drawn++;

            bool visible = IsRendererVisible(r);
            Color color = visible ? gizmoSettings.visibleColor : gizmoSettings.occludedColor;
            float distance = Vector3.Distance(camPos, r.bounds.center);

#if UNITY_EDITOR
            if (gizmoSettings.showOcclusionRays)
            {
                Handles.color = color;
                Handles.DrawLine(camPos, r.bounds.center);

                if (!visible)
                {
                    Vector3 dir = (r.bounds.center - camPos).normalized;
                    if (Physics.Raycast(new Ray(camPos, dir), out RaycastHit hit,
                            distance, occlusionSettings.blockerLayers))
                    {
                        Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
                        Handles.DrawWireDisc(hit.point, hit.normal, 0.3f);
                    }
                }
            }

            if (gizmoSettings.showPredictedPositions && predictiveSettings.enabled)
            {
                if (_velocities != null &&
                    _velocities.TryGetValue(r, out Vector3 vel) &&
                    vel.sqrMagnitude > 0.01f)
                {
                    Vector3 predicted = r.bounds.center +
                                        vel * predictiveSettings.predictionTime;
                    Handles.color = gizmoSettings.predictedColor;
                    Handles.DrawDottedLine(r.bounds.center, predicted, 3f);
                    Handles.SphereHandleCap(0, predicted, Quaternion.identity,
                        0.2f, EventType.Repaint);
                }
            }

            if (gizmoSettings.showLabels)
                DrawGizmoLabel(r, visible, distance, color);
#endif
        }
    }

#if UNITY_EDITOR
    private static GUIStyle _gizmoStyle;

    private void DrawGizmoLabel(Renderer r, bool visible, float distance, Color color)
    {
        if (_gizmoStyle == null) _gizmoStyle = new GUIStyle();
        _gizmoStyle.normal.textColor = color;
        _gizmoStyle.fontSize = Mathf.RoundToInt(12 * gizmoSettings.labelSize);

        Vector3 pos = r.bounds.center + Vector3.up * (r.bounds.extents.y + 0.4f);
        int lod = ResolveLODIndex(distance);

        Handles.Label(pos, visible ? "Visible" : "Occluded", _gizmoStyle);
        Handles.Label(pos + Vector3.up * 0.6f,
            $"LOD {lod}  |  {distance:F1} m", _gizmoStyle);

        if (predictiveSettings.enabled &&
            _velocities != null &&
            _velocities.TryGetValue(r, out Vector3 vel))
        {
            Handles.Label(pos + Vector3.up * 1.2f,
                $"vel: {vel.magnitude:F1} m/s", _gizmoStyle);
        }
    }
#endif

    // =========================================================================
    // CUSTOM INSPECTOR
    // =========================================================================

#if UNITY_EDITOR
    [CustomEditor(typeof(CameraOptimizer))]
    public class CameraOptimizerEditor : Editor
    {
        private bool _sCamera = true;
        private bool _sCulling = true;
        private bool _sOcclusion = true;
        private bool _sFade = true;
        private bool _sLOD = true;
        private bool _sPredictive = true;
        private bool _sStats = false;
        private bool _sOverlay = false;
        private bool _sZones = true;
        private bool _sGizmos = false;
        private bool _sRuntime = true;

        private static readonly GUIContent BtnAddLOD = new GUIContent("＋  Add LOD Level");
        private static readonly GUIContent BtnAddZone = new GUIContent("＋  Add Zone");
        private static readonly GUIContent BtnRemove = new GUIContent("✕");
        private static readonly GUIContent BtnForce = new GUIContent("Force Culling Update");
        private static readonly GUIContent BtnRescan = new GUIContent("Rescan Scene");

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var t = (CameraOptimizer)target;

            DrawInfoBox(t);
            DrawCameraSection(t);
            DrawCullingSection();
            DrawOcclusionSection();
            DrawFadeSection();
            DrawLODSection(t);
            DrawPredictiveSection();
            DrawStatsSection();
            DrawOverlaySection();
            DrawZonesSection(t);
            DrawGizmosSection();

            if (Application.isPlaying)
                DrawRuntimeSection(t);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInfoBox(CameraOptimizer t)
        {
            string cameraStatus;
            MessageType msgType;

            if (Application.isPlaying && t._resolvedCamera != null)
            {
                cameraStatus = $"Active camera: {t._resolvedCamera.name}";
                msgType = MessageType.Info;
            }
            else if (t.targetCamera != null)
            {
                cameraStatus = $"Target camera assigned: {t.targetCamera.name}";
                msgType = MessageType.Info;
            }
            else if (t.TryGetComponent<Camera>(out var selfCam))
            {
                cameraStatus = $"Will use Camera on this GameObject: {selfCam.name}";
                msgType = MessageType.Info;
            }
            else
            {
                cameraStatus =
                    "No camera assigned!\n" +
                    "Options:\n" +
                    "  • Drag a Camera into 'Target Camera' below\n" +
                    "  • Place this script on a Camera GameObject\n" +
                    "  • Tag any camera as 'MainCamera'";
                msgType = MessageType.Warning;
            }

            EditorGUILayout.HelpBox(cameraStatus, msgType);
            GUILayout.Space(4);
        }

        private void DrawCameraSection(CameraOptimizer t)
        {
            _sCamera = EditorGUILayout.BeginFoldoutHeaderGroup(_sCamera, "📷  Camera");
            if (_sCamera)
            {
                Indent(() =>
                {
                    EditorGUILayout.PropertyField(P("targetCamera"),
                        new GUIContent("Target Camera",
                            "Drag any Camera from the scene here.\n" +
                            "Leave empty to auto-detect."));

                    if (Application.isPlaying)
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            string name = t._resolvedCamera != null
                                ? t._resolvedCamera.name
                                : "None";
                            EditorGUILayout.TextField("Resolved Camera", name);
                        }
                    }
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawCullingSection()
        {
            _sCulling = EditorGUILayout.BeginFoldoutHeaderGroup(_sCulling, "⚙  Culling");
            if (_sCulling)
            {
                Indent(() =>
                {
                    EditorGUILayout.PropertyField(P("maxRenderDistance"));
                    EditorGUILayout.PropertyField(P("updateInterval"));
                    EditorGUILayout.PropertyField(P("cleanupInterval"));
                    EditorGUILayout.PropertyField(P("cullingLayers"));
                    EditorGUILayout.PropertyField(P("excludeLayers"));
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawOcclusionSection()
        {
            _sOcclusion = EditorGUILayout.BeginFoldoutHeaderGroup(_sOcclusion, "👁  Occlusion");
            if (_sOcclusion)
                Indent(() => EditorGUILayout.PropertyField(
                    P("occlusionSettings"), includeChildren: true));
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawFadeSection()
        {
            _sFade = EditorGUILayout.BeginFoldoutHeaderGroup(_sFade, "🌊  Fade");
            if (_sFade)
                Indent(() => EditorGUILayout.PropertyField(
                    P("fadeSettings"), includeChildren: true));
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawLODSection(CameraOptimizer t)
        {
            _sLOD = EditorGUILayout.BeginFoldoutHeaderGroup(_sLOD, "📊  LOD");
            if (_sLOD)
            {
                Indent(() =>
                {
                    var levels = t.GetLODLevels();
                    for (int i = 0; i < levels.Count; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(
                            $"LOD {i}  ≤ {levels[i].maxDistance:F0} m  " +
                            $"q:{levels[i].qualityMultiplier:F2}  " +
                            $"shadow:{levels[i].shadowMode}",
                            EditorStyles.miniLabel);

                        Colored(new Color(1f, 0.4f, 0.4f), () =>
                        {
                            if (GUILayout.Button(BtnRemove, GUILayout.Width(26)))
                            {
                                Undo.RecordObject(t, "Remove LOD Level");
                                t.RemoveLODLevel(i);
                                EditorUtility.SetDirty(t);
                            }
                        });
                        EditorGUILayout.EndHorizontal();
                    }

                    GUILayout.Space(4);
                    EditorGUILayout.PropertyField(P("lodSettings"),
                        includeChildren: true);
                    GUILayout.Space(4);

                    Colored(new Color(0.6f, 1f, 0.65f), () =>
                    {
                        if (GUILayout.Button(BtnAddLOD))
                        {
                            Undo.RecordObject(t, "Add LOD Level");
                            t.AddLODLevel(new LODLevel
                            {
                                maxDistance = 150f,
                                qualityMultiplier = 0.2f
                            });
                            EditorUtility.SetDirty(t);
                        }
                    });
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawPredictiveSection()
        {
            _sPredictive = EditorGUILayout.BeginFoldoutHeaderGroup(
                _sPredictive, "🎯  Predictive Culling");
            if (_sPredictive)
                Indent(() => EditorGUILayout.PropertyField(
                    P("predictiveSettings"), includeChildren: true));
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawStatsSection()
        {
            _sStats = EditorGUILayout.BeginFoldoutHeaderGroup(_sStats, "📈  Statistics");
            if (_sStats)
                Indent(() => EditorGUILayout.PropertyField(
                    P("statisticsSettings"), includeChildren: true));
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawOverlaySection()
        {
            _sOverlay = EditorGUILayout.BeginFoldoutHeaderGroup(
                _sOverlay, "🖥  Debug Overlay");
            if (_sOverlay)
                Indent(() => EditorGUILayout.PropertyField(
                    P("debugOverlay"), includeChildren: true));
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawZonesSection(CameraOptimizer t)
        {
            _sZones = EditorGUILayout.BeginFoldoutHeaderGroup(
                _sZones, "🗺  Optimization Zones");
            if (_sZones)
            {
                Indent(() =>
                {
                    var zones = t.GetZones();
                    for (int i = 0; i < zones.Count; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(
                            $"Zone {i}: {zones[i].zoneName}  " +
                            $"r:{zones[i].radius:F0}  " +
                            $"dist:{zones[i].renderDistanceOverride:F0}",
                            EditorStyles.miniLabel);

                        Colored(new Color(1f, 0.4f, 0.4f), () =>
                        {
                            if (GUILayout.Button(BtnRemove, GUILayout.Width(26)))
                            {
                                Undo.RecordObject(t, "Remove Zone");
                                t.RemoveZone(i);
                                EditorUtility.SetDirty(t);
                            }
                        });
                        EditorGUILayout.EndHorizontal();
                    }

                    GUILayout.Space(4);
                    EditorGUILayout.PropertyField(P("optimizationZones"),
                        includeChildren: true);
                    GUILayout.Space(4);

                    Colored(new Color(0.6f, 0.8f, 1f), () =>
                    {
                        if (GUILayout.Button(BtnAddZone))
                        {
                            Undo.RecordObject(t, "Add Optimization Zone");
                            t.AddZone(new OptimizationZone
                            {
                                zoneName = $"Zone {zones.Count}",
                                center = Vector3.zero,
                                radius = 20f,
                                renderDistanceOverride = 50f,
                                updateIntervalOverride = 0.05f
                            });
                            EditorUtility.SetDirty(t);
                        }
                    });
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawGizmosSection()
        {
            _sGizmos = EditorGUILayout.BeginFoldoutHeaderGroup(_sGizmos, "🎨  Gizmos");
            if (_sGizmos)
                Indent(() => EditorGUILayout.PropertyField(
                    P("gizmoSettings"), includeChildren: true));
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawRuntimeSection(CameraOptimizer t)
        {
            GUILayout.Space(6);
            _sRuntime = EditorGUILayout.BeginFoldoutHeaderGroup(
                _sRuntime, "⚡  Runtime");
            if (_sRuntime)
            {
                Indent(() =>
                {
                    var s = t.GetStatistics();
                    if (s != null)
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.FloatField("FPS",
                                Mathf.Round(s.averageFps * 10f) / 10f);
                            EditorGUILayout.FloatField("Culling ms",
                                Mathf.Round(s.averageCullingTimeMs * 100f) / 100f);
                            EditorGUILayout.IntField("Total Renderers",
                                s.totalRenderers);
                            EditorGUILayout.IntField("Visible",
                                s.visibleRenderers);
                            EditorGUILayout.IntField("Occluded",
                                s.occludedRenderers);
                            EditorGUILayout.IntField("Culled (dist)",
                                s.culledByDistance);
                            EditorGUILayout.IntField("Culled (frustum)",
                                s.culledByFrustum);
                            EditorGUILayout.IntField("Saved DrawCalls",
                                s.savedDrawCalls);
                            EditorGUILayout.IntField("Active Fades",
                                s.activeCoroutines);
                            EditorGUILayout.IntField("Occlusion Cache",
                                s.occlusionCacheSize);
                        }
                    }

                    GUILayout.Space(6);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(BtnForce)) t.ForceUpdate();
                    if (GUILayout.Button(BtnRescan)) t.RescanScene();
                    EditorGUILayout.EndHorizontal();
                });
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            Repaint();
        }

        // Helpers
        private static void Indent(System.Action draw)
        {
            EditorGUI.indentLevel++;
            draw();
            EditorGUI.indentLevel--;
        }

        private static void Colored(Color color, System.Action draw)
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = color;
            draw();
            GUI.backgroundColor = prev;
        }

        private SerializedProperty P(string name)
            => serializedObject.FindProperty(name);
    }
#endif
}
