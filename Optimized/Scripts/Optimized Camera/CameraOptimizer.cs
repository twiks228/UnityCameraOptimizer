using UnityEngine;
using System.Collections.Generic;
using System.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(Camera))]
public class CameraOptimizer : MonoBehaviour
{
    [System.Serializable]
    public class LODSettings
    {
        [Tooltip("Distance at which to switch to this LOD level")]
        public float distanceThreshold = 50f;
        [Tooltip("Quality multiplier (0-1) for shaders and effects")]
        [Range(0f, 1f)] public float qualityMultiplier = 1f;
    }

    [System.Serializable]
    public class FadeSettings
    {
        [Tooltip("Duration of fade-in effect (seconds)")]
        [Min(0)] public float fadeDuration = 0.5f;
        [Tooltip("Minimum distance from camera to trigger fade-in")]
        [Min(0)] public float minFadeDistance = 5f;
        [Tooltip("Fade animation curve (Linear, EaseIn, etc.)")]
        public AnimationCurve fadeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Minimum LOD level during fade-in")]
        [Range(0, 10)] public int minLODLevelDuringFade = 0;
    }

    [System.Serializable]
    public class GizmoSettings
    {
        [Tooltip("Show render distance sphere")]
        public bool showRenderSphere = true;
        [Tooltip("Show fade distance sphere")]
        public bool showFadeSphere = true;
        [Tooltip("Show camera frustum")]
        public bool showFrustum = true;
        [Tooltip("Show occlusion rays")]
        public bool showOcclusionRays = true;
        [Tooltip("Show visibility labels")]
        public bool showLabels = true;
        [Tooltip("Maximum number of objects to show Gizmos for")]
        [Min(1)] public int maxGizmoObjects = 50;
        [Tooltip("Color for visible objects")]
        public Color visibleColor = Color.green;
        [Tooltip("Color for occluded objects")]
        public Color occludedColor = Color.red;
        [Tooltip("Label size multiplier")]
        [Min(0.1f)] public float labelSize = 1f;
    }

    // Camera reference
    private Camera mainCamera;

    // Culling settings
    [Header("Culling Settings")]
    [Tooltip("Maximum distance for rendering objects")]
    [SerializeField, Min(0)] private float maxRenderDistance = 100f;
    [Tooltip("Interval between culling updates (seconds)")]
    [SerializeField, Min(0.01f)] private float updateInterval = 0.1f;
    [Tooltip("Layers to include in culling")]
    [SerializeField] private LayerMask cullingLayers;
    [Tooltip("Layers to exclude from culling")]
    [SerializeField] private LayerMask excludeLayers;

    // Occlusion settings
    [Header("Occlusion Culling")]
    [Tooltip("Use multiple rays for large objects to improve occlusion accuracy")]
    [SerializeField] private bool useMultiRayOcclusion = true;
    [Tooltip("Number of rays for large objects")]
    [SerializeField, Min(1)] private int multiRayCount = 3;
    [Tooltip("Use bounding sphere for occlusion pre-check")]
    [SerializeField] private bool useBoundingSpherePreCheck = true;

    // Fade settings
    [Header("Fade Settings")]
    [Tooltip("Settings for object fade-in effect")]
    [SerializeField] private FadeSettings fadeSettings = new FadeSettings();

    // LOD settings
    [Header("LOD Settings")]
    [Tooltip("LOD levels with distance thresholds and quality settings")]
    [SerializeField] private List<LODSettings> lodSettings = new List<LODSettings> { new LODSettings() };
    [Tooltip("Enable pyramid optimization for LOD transitions")]
    [SerializeField] private bool usePyramidOptimization = true;

    // Gizmo settings
    [Header("Gizmo Settings")]
    [Tooltip("Customization options for Gizmos")]
    [SerializeField] private GizmoSettings gizmoSettings = new GizmoSettings();

    // Object cache
    private List<Renderer> renderersToCull;
    private Dictionary<Renderer, float> rendererFadeProgress;
    private Dictionary<Renderer, bool> rendererVisibilityCache;
    private Dictionary<Renderer, float> rendererOcclusionCache;
    private float lastUpdateTime;
    private Plane[] frustumPlanes;

    void Awake()
    {
        InitializeCamera();
        CacheRenderers();
        rendererFadeProgress = new Dictionary<Renderer, float>();
        rendererVisibilityCache = new Dictionary<Renderer, bool>();
        rendererOcclusionCache = new Dictionary<Renderer, float>();
    }

    void OnDestroy()
    {
        // Clean up materials to avoid memory leaks
        foreach (var renderer in renderersToCull)
        {
            if (renderer != null && renderer.material != null)
            {
                Destroy(renderer.material);
            }
        }
    }

    void InitializeCamera()
    {
        mainCamera = GetComponent<Camera>();
        if (mainCamera == null)
        {
            Debug.LogError("CameraOptimizer requires a Camera component!", this);
            enabled = false;
        }
    }

    void CacheRenderers()
    {
        renderersToCull = new List<Renderer>();
        var allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);

        foreach (var obj in allObjects)
        {
            if (obj == null || !obj.activeInHierarchy) continue;

            if (((1 << obj.layer) & cullingLayers) == 0 || ((1 << obj.layer) & excludeLayers) != 0)
                continue;

            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderersToCull.Add(renderer);
                InitializeFadeMaterial(renderer);
            }
        }

        Debug.Log($"CameraOptimizer: Cached {renderersToCull.Count} renderers for optimization.");
    }

    void InitializeFadeMaterial(Renderer renderer)
    {
        if (renderer.material != null && !renderer.material.HasProperty("_Alpha"))
        {
            Material newMat = new Material(renderer.material);
            newMat.SetFloat("_Alpha", 1f);
            renderer.material = newMat;
        }
    }

    void Update()
    {
        if (Time.time - lastUpdateTime < updateInterval)
            return;

        lastUpdateTime = Time.time;
        OptimizeRendering();
    }

    void OptimizeRendering()
    {
        if (mainCamera == null) return;

        Vector3 cameraPos = mainCamera.transform.position;
        frustumPlanes = GeometryUtility.CalculateFrustumPlanes(mainCamera);

        // Clean up null renderers
        renderersToCull.RemoveAll(r => r == null);

        foreach (var renderer in renderersToCull)
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy)
                continue;

            bool shouldRender = EvaluateRendererVisibility(renderer, cameraPos);
            rendererVisibilityCache[renderer] = shouldRender;
            UpdateRendererVisibility(renderer, shouldRender, cameraPos);
        }
    }

    bool EvaluateRendererVisibility(Renderer renderer, Vector3 cameraPos)
    {
        // Skip if object or its parent is inactive
        if (!renderer.gameObject.activeInHierarchy)
            return false;

        // Distance culling
        float distanceToCamera = Vector3.Distance(cameraPos, renderer.bounds.center);
        if (distanceToCamera > maxRenderDistance)
            return false;

        // Frustum culling
        if (!GeometryUtility.TestPlanesAABB(frustumPlanes, renderer.bounds))
            return false;

        // Occlusion culling
        if (!IsVisibleFromCamera(renderer, distanceToCamera))
            return false;

        return true;
    }

    bool IsVisibleFromCamera(Renderer renderer, float distance)
    {
        // Check if result is cached and still valid
        if (rendererOcclusionCache.ContainsKey(renderer) && rendererOcclusionCache[renderer] == Time.time)
        {
            return rendererVisibilityCache.ContainsKey(renderer) && rendererVisibilityCache[renderer];
        }

        bool isVisible;
        if (useBoundingSpherePreCheck)
        {
            // Pre-check with bounding sphere
            Bounds bounds = renderer.bounds;
            float radius = bounds.extents.magnitude;
            Vector3 sphereCenter = bounds.center;
            Ray ray = new Ray(mainCamera.transform.position, (sphereCenter - mainCamera.transform.position).normalized);

            if (Physics.SphereCast(ray, radius, out RaycastHit hit, distance, ~(cullingLayers | excludeLayers)))
            {
                if (hit.collider.gameObject != renderer.gameObject)
                {
                    isVisible = false;
                    rendererOcclusionCache[renderer] = Time.time;
                    return isVisible;
                }
            }
        }

        if (!useMultiRayOcclusion || multiRayCount <= 1)
        {
            // Single ray occlusion check
            Vector3 direction = (renderer.bounds.center - mainCamera.transform.position).normalized;
            Ray ray = new Ray(mainCamera.transform.position, direction);

            if (Physics.Raycast(ray, out RaycastHit hit, distance, ~(cullingLayers | excludeLayers)))
            {
                isVisible = hit.collider.gameObject == renderer.gameObject;
                rendererOcclusionCache[renderer] = Time.time;
                return isVisible;
            }
            isVisible = true;
            rendererOcclusionCache[renderer] = Time.time;
            return isVisible;
        }
        else
        {
            // Multi-ray occlusion check for large objects
            Bounds bounds = renderer.bounds;
            Vector3[] points = new Vector3[multiRayCount];
            points[0] = bounds.center;

            for (int i = 1; i < multiRayCount; i++)
            {
                points[i] = bounds.center + new Vector3(
                    Random.Range(-bounds.extents.x, bounds.extents.x),
                    Random.Range(-bounds.extents.y, bounds.extents.y),
                    Random.Range(-bounds.extents.z, bounds.extents.z)
                );
            }

            int visiblePoints = 0;
            foreach (var point in points)
            {
                Vector3 direction = (point - mainCamera.transform.position).normalized;
                Ray ray = new Ray(mainCamera.transform.position, direction);

                if (!Physics.Raycast(ray, out RaycastHit hit, distance, ~(cullingLayers | excludeLayers)) ||
                    hit.collider.gameObject == renderer.gameObject)
                {
                    visiblePoints++;
                }
            }

            isVisible = visiblePoints > 0;
            rendererOcclusionCache[renderer] = Time.time;
            return isVisible;
        }
    }

    void UpdateRendererVisibility(Renderer renderer, bool shouldRender, Vector3 cameraPos)
    {
        float distance = Vector3.Distance(cameraPos, renderer.bounds.center);
        bool needsFade = shouldRender && distance < fadeSettings.minFadeDistance && fadeSettings.fadeDuration > 0;

        if (shouldRender)
        {
            if (!renderer.enabled)
            {
                renderer.enabled = true;
                if (needsFade)
                {
                    StartCoroutine(FadeInRenderer(renderer));
                }
                else
                {
                    SetRendererAlpha(renderer, 1f);
                }
            }
        }
        else
        {
            renderer.enabled = false;
            SetRendererAlpha(renderer, 0f);
        }

        if (shouldRender)
        {
            AdjustLOD(renderer.gameObject, distance, needsFade);
        }
    }

    IEnumerator FadeInRenderer(Renderer renderer)
    {
        if (!rendererFadeProgress.ContainsKey(renderer))
            rendererFadeProgress[renderer] = 0f;

        float startAlpha = rendererFadeProgress[renderer];
        float elapsed = 0f;

        while (elapsed < fadeSettings.fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeSettings.fadeDuration;
            float curveValue = fadeSettings.fadeCurve.Evaluate(t);
            float alpha = Mathf.Lerp(startAlpha, 1f, curveValue);
            SetRendererAlpha(renderer, alpha);
            rendererFadeProgress[renderer] = alpha;
            yield return null;
        }

        SetRendererAlpha(renderer, 1f);
        rendererFadeProgress[renderer] = 1f;
    }

    void SetRendererAlpha(Renderer renderer, float alpha)
    {
        if (renderer != null && renderer.material != null && renderer.material.HasProperty("_Alpha"))
        {
            renderer.material.SetFloat("_Alpha", alpha);
        }
    }

    void AdjustLOD(GameObject obj, float distance, bool isFading)
    {
        var lodGroup = obj.GetComponent<LODGroup>();
        if (lodGroup != null)
        {
            return;
        }

        float quality = 1f;
        int lodLevel = 0;
        for (int i = 0; i < lodSettings.Count; i++)
        {
            if (distance < lodSettings[i].distanceThreshold)
            {
                quality = lodSettings[i].qualityMultiplier;
                lodLevel = i;
                break;
            }
        }

        if (isFading && lodLevel < fadeSettings.minLODLevelDuringFade)
        {
            lodLevel = fadeSettings.minLODLevelDuringFade;
            quality = lodSettings[lodLevel].qualityMultiplier;
        }

        if (usePyramidOptimization)
        {
            quality = Mathf.Lerp(quality, 1f, 0.5f);
        }

        ApplyQualitySettings(obj, quality, lodLevel);
    }

    void ApplyQualitySettings(GameObject obj, float quality, int lodLevel)
    {
        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            renderer.material.SetFloat("_Quality", quality);
            renderer.material.SetInt("_LODLevel", lodLevel);
        }

        Transform detailTransform = obj.transform.Find("Detail");
        if (detailTransform != null)
        {
            detailTransform.gameObject.SetActive(quality > 0.5f);
        }
    }

    // Gizmos for visualization
    void OnDrawGizmos()
    {
        if (mainCamera == null) return;

        // Draw render distance sphere
        if (gizmoSettings.showRenderSphere)
        {
            Gizmos.color = new Color(0, 1, 0, 0.05f);
            Gizmos.DrawSphere(mainCamera.transform.position, maxRenderDistance);
            Gizmos.color = new Color(0, 1, 0, 0.4f);
            Gizmos.DrawWireSphere(mainCamera.transform.position, maxRenderDistance);
        }

        // Draw fade distance sphere
        if (gizmoSettings.showFadeSphere)
        {
            Gizmos.color = new Color(1, 1, 0, 0.05f);
            Gizmos.DrawSphere(mainCamera.transform.position, fadeSettings.minFadeDistance);
            Gizmos.color = new Color(1, 1, 0, 0.4f);
            Gizmos.DrawWireSphere(mainCamera.transform.position, fadeSettings.minFadeDistance);
        }

        // Draw frustum
        if (gizmoSettings.showFrustum)
        {
            Gizmos.color = new Color(0, 0, 1, 0.15f);
            Gizmos.matrix = Matrix4x4.TRS(mainCamera.transform.position, mainCamera.transform.rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, mainCamera.fieldOfView, maxRenderDistance, mainCamera.nearClipPlane, mainCamera.aspect);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (mainCamera == null || renderersToCull == null) return;

        int gizmoCount = 0;
        foreach (var renderer in renderersToCull)
        {
            if (gizmoCount >= gizmoSettings.maxGizmoObjects)
                break;

            if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;

            gizmoCount++;
            float distance = Vector3.Distance(mainCamera.transform.position, renderer.bounds.center);
            bool isVisible = rendererVisibilityCache.ContainsKey(renderer) && rendererVisibilityCache[renderer];

            // Draw occlusion rays
            if (gizmoSettings.showOcclusionRays)
            {
                Color startColor = isVisible ? gizmoSettings.visibleColor : gizmoSettings.occludedColor;
                Color endColor = new Color(startColor.r, startColor.g, startColor.b, startColor.a * 0.3f);

                Vector3 start = mainCamera.transform.position;
                Vector3 end = renderer.bounds.center;
#if UNITY_EDITOR
                Handles.color = startColor;
                Handles.DrawLine(start, end);

                // Draw occlusion hit point if occluded
                if (!isVisible)
                {
                    Ray ray = new Ray(mainCamera.transform.position, (renderer.bounds.center - mainCamera.transform.position).normalized);
                    if (Physics.Raycast(ray, out RaycastHit hit, distance, ~(cullingLayers | excludeLayers)))
                    {
                        Handles.color = new Color(1, 0, 0, 0.5f);
                        Handles.DrawWireDisc(hit.point, hit.normal, 0.2f);
                    }
                }
#endif
            }

            // Draw labels
            if (gizmoSettings.showLabels)
            {
#if UNITY_EDITOR
                Vector3 labelPos = renderer.bounds.center + Vector3.up * renderer.bounds.extents.y;
                GUIStyle style = new GUIStyle();
                style.normal.textColor = isVisible ? gizmoSettings.visibleColor : gizmoSettings.occludedColor;
                style.fontSize = Mathf.RoundToInt(12 * gizmoSettings.labelSize);
                Handles.Label(labelPos, isVisible ? "Visible" : "Occluded", style);

                // Draw LOD level
                int lodLevel = 0;
                for (int i = 0; i < lodSettings.Count; i++)
                {
                    if (distance < lodSettings[i].distanceThreshold)
                    {
                        lodLevel = i;
                        break;
                    }
                }
                Handles.Label(labelPos + Vector3.up * 0.5f, $"LOD: {lodLevel}", style);
#endif
            }
        }
    }

    // Inspector utilities
    public void SetMaxRenderDistance(float distance)
    {
        maxRenderDistance = Mathf.Max(0, distance);
    }

    public void SetUpdateInterval(float interval)
    {
        updateInterval = Mathf.Max(0.01f, interval);
    }

    public void AddLODLevel()
    {
        lodSettings.Add(new LODSettings());
    }

    public void RemoveLODLevel(int index)
    {
        if (index >= 0 && index < lodSettings.Count)
            lodSettings.RemoveAt(index);
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(CameraOptimizer))]
    public class CameraOptimizerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            CameraOptimizer optimizer = (CameraOptimizer)target;

            GUILayout.Space(10);
            GUILayout.Label("LOD Management", EditorStyles.boldLabel);

            if (GUILayout.Button("Add LOD Level"))
            {
                optimizer.AddLODLevel();
            }

            for (int i = 0; i < optimizer.lodSettings.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"LOD {i}");
                if (GUILayout.Button("Remove"))
                {
                    optimizer.RemoveLODLevel(i);
                }
                GUILayout.EndHorizontal();
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
#endif
}
