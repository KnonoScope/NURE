using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public sealed class NureQuestPerformanceBootstrap : MonoBehaviour
{
    private const int TargetFrameRate = 72;
    private const float OptimizedEyeTextureScale = 0.82f;
    private const float DiagnosticsEyeTextureScale = 0.8f;
    private const float FoveatedRenderingLevel = 1.0f;
    private static bool s_Installed;

    private readonly List<XRDisplaySubsystem> _displaySubsystems = new List<XRDisplaySubsystem>(2);

#if NURE_SHOW_FPS
    private const float HudUpdateInterval = 0.2f;
    private const float SampleLogIntervalSeconds = 3.0f;
    private const float DropLogThresholdSeconds = 1.25f;
    private Transform _hudRoot;
    private Text _hudText;
    private float _hudAccumulatedTime;
    private int _hudAccumulatedFrames;
    private float _smoothedFrameTime;
    private float _minFps = float.PositiveInfinity;
    private float _maxFrameMs;
    private float _belowSixtySeconds;
    private float _nextHudAttachTime;
    private float _nextSampleLogTime;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (s_Installed)
            return;

        s_Installed = true;
        var go = new GameObject(nameof(NureQuestPerformanceBootstrap));
        DontDestroyOnLoad(go);
        go.AddComponent<NureQuestPerformanceBootstrap>();
    }

    private void Awake()
    {
        ApplyPerformanceDefaults();
    }

    private IEnumerator Start()
    {
        for (int i = 0; i < 12; i++)
        {
            ApplyXrDisplayTuning();
            yield return null;
        }
    }

    private void Update()
    {
        ApplyFramePacing();

#if NURE_SHOW_FPS
        UpdateDiagnosticsHud();
#endif
    }

    private static void ApplyPerformanceDefaults()
    {
        ApplyFramePacing();
        Application.backgroundLoadingPriority = ThreadPriority.Low;

        QualitySettings.maxQueuedFrames = 2;
        QualitySettings.pixelLightCount = 0;
        QualitySettings.shadows = ShadowQuality.Disable;
        QualitySettings.realtimeReflectionProbes = false;
        QualitySettings.softParticles = false;
        QualitySettings.particleRaycastBudget = Mathf.Min(QualitySettings.particleRaycastBudget, 64);
        QualitySettings.asyncUploadTimeSlice = Mathf.Min(Mathf.Max(QualitySettings.asyncUploadTimeSlice, 1), 2);
        QualitySettings.asyncUploadBufferSize = Mathf.Max(QualitySettings.asyncUploadBufferSize, 32);
        QualitySettings.streamingMipmapsActive = true;
        QualitySettings.streamingMipmapsMemoryBudget = Mathf.Min(QualitySettings.streamingMipmapsMemoryBudget, 384f);
        QualitySettings.streamingMipmapsRenderersPerFrame = Mathf.Min(QualitySettings.streamingMipmapsRenderersPerFrame, 96);
        QualitySettings.streamingMipmapsMaxFileIORequests = Mathf.Min(QualitySettings.streamingMipmapsMaxFileIORequests, 64);
        QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias, 0.65f);
        QualitySettings.globalTextureMipmapLimit = Mathf.Max(QualitySettings.globalTextureMipmapLimit, 1);

        XRSettings.eyeTextureResolutionScale =
#if NURE_SHOW_FPS
            DiagnosticsEyeTextureScale;
#else
            OptimizedEyeTextureScale;
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidDevice.SetSustainedPerformanceMode(true);
#endif
    }

    private static void ApplyFramePacing()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFrameRate;
    }

    private void ApplyXrDisplayTuning()
    {
        _displaySubsystems.Clear();
        SubsystemManager.GetSubsystems(_displaySubsystems);

        for (int i = 0; i < _displaySubsystems.Count; i++)
        {
            XRDisplaySubsystem display = _displaySubsystems[i];
            if (display == null || !display.running)
                continue;

            display.foveatedRenderingLevel = FoveatedRenderingLevel;
            display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.None;
        }
    }

#if NURE_SHOW_FPS
    private void UpdateDiagnosticsHud()
    {
        float unscaledDelta = Time.unscaledDeltaTime;
        if (unscaledDelta <= 0f)
            return;

        _smoothedFrameTime = _smoothedFrameTime <= 0f
            ? unscaledDelta
            : Mathf.Lerp(_smoothedFrameTime, unscaledDelta, 0.08f);

        float frameMs = unscaledDelta * 1000f;
        float currentFps = 1f / unscaledDelta;
        _minFps = Mathf.Min(_minFps, currentFps);
        _maxFrameMs = Mathf.Max(_maxFrameMs, frameMs);

        _hudAccumulatedFrames++;
        _hudAccumulatedTime += unscaledDelta;

        if (currentFps < 60f)
        {
            _belowSixtySeconds += unscaledDelta;
            if (_belowSixtySeconds >= DropLogThresholdSeconds)
            {
                Debug.LogWarning($"[Nure FPS] FPS sotto 60 per {_belowSixtySeconds:0.0}s in {GetActiveVirtualSceneName()} ({currentFps:0.0} FPS, {frameMs:0.0} ms).");
                _belowSixtySeconds = 0f;
            }
        }
        else
        {
            _belowSixtySeconds = 0f;
        }

        if (_hudRoot == null && Time.unscaledTime >= _nextHudAttachTime)
        {
            _nextHudAttachTime = Time.unscaledTime + 0.5f;
            TryCreateHud();
        }

        if (_hudText == null || _hudAccumulatedTime < HudUpdateInterval)
            return;

        float averageFps = _hudAccumulatedFrames / _hudAccumulatedTime;
        float smoothFps = _smoothedFrameTime > 0f ? 1f / _smoothedFrameTime : averageFps;
        string sceneName = GetActiveVirtualSceneName();

        if (Time.unscaledTime >= _nextSampleLogTime)
        {
            _nextSampleLogTime = Time.unscaledTime + SampleLogIntervalSeconds;
            Debug.Log($"[Nure FPS] sample scene={sceneName} avg={averageFps:0.0} smooth={smoothFps:0.0} frameMs={frameMs:0.0} maxFrameMs={_maxFrameMs:0.0} minFps={_minFps:0.0} target={TargetFrameRate}");
        }

        _hudText.text =
            $"FPS {averageFps:0.0}  smooth {smoothFps:0.0}\n" +
            $"Frame {frameMs:0.0} ms  max {_maxFrameMs:0.0} ms\n" +
            $"Min {_minFps:0.0}  Target {TargetFrameRate}\n" +
            $"Scene {sceneName}\n" +
            $"Time {Time.realtimeSinceStartup:0.0}s";

        _hudAccumulatedFrames = 0;
        _hudAccumulatedTime = 0f;
    }

    private void TryCreateHud()
    {
        Camera targetCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
        if (targetCamera == null)
            return;

        var root = new GameObject("NURE FPS Diagnostics HUD");
        root.transform.SetParent(targetCamera.transform, false);
        root.transform.localPosition = new Vector3(-0.34f, -0.23f, 1.15f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 0.00125f;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = targetCamera;
        canvas.sortingOrder = 9000;

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(430f, 170f);

        var backgroundGo = new GameObject("Background");
        backgroundGo.transform.SetParent(root.transform, false);
        var background = backgroundGo.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.62f);
        var backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(root.transform, false);
        _hudText = textGo.AddComponent<Text>();
        _hudText.font = GetBuiltinFont();
        _hudText.fontSize = 24;
        _hudText.alignment = TextAnchor.UpperLeft;
        _hudText.horizontalOverflow = HorizontalWrapMode.Overflow;
        _hudText.verticalOverflow = VerticalWrapMode.Overflow;
        _hudText.color = Color.white;
        _hudText.text = "FPS ...";

        var textRect = _hudText.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 10f);
        textRect.offsetMax = new Vector2(-10f, -10f);

        _hudRoot = root.transform;
    }

    private static Font GetBuiltinFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private static string GetActiveVirtualSceneName()
    {
        SceneGroupManager manager = FindFirstObjectByType<SceneGroupManager>();
        if (manager == null || manager.scenes == null)
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        for (int i = 0; i < manager.scenes.Count; i++)
        {
            SceneGroupManager.VirtualScene scene = manager.scenes[i];
            if (scene == null || scene.root == null || !scene.root.activeInHierarchy)
                continue;

            if (!string.IsNullOrWhiteSpace(scene.name))
                return scene.name;

            return scene.root.name;
        }

        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }
#endif
}
