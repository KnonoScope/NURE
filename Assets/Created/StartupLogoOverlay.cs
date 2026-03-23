using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class StartupLogoOverlay : MonoBehaviour
{
    [Header("Logo Source")]
    public string resourcesTextureName = "LogoNure";

    [Header("Display")]
    [Min(0.1f)] public float distanceFromCamera = 1.35f;
    [Min(0.05f)] public float logoHeightMeters = 0.28f;
    [Min(0.1f)] public float fadeSeconds = 0.35f;
    [Min(0.1f)] public float holdSeconds = 2.8f;
    public bool verboseLogs = false;

    private Camera _targetCamera;
    private Transform _canvasTransform;
    private CanvasGroup _canvasGroup;

    // Startup branding now belongs to the native XR splash. This component only runs if attached manually.
    private void Start()
    {
        StartCoroutine(ShowLogoRoutine());
    }

    private void LateUpdate()
    {
        if (_canvasTransform == null)
            return;

        Camera cam = ResolveCamera();
        if (cam == null)
            return;

        if (_canvasTransform.parent != cam.transform)
            _canvasTransform.SetParent(cam.transform, false);

        _canvasTransform.localPosition = new Vector3(0f, 0f, distanceFromCamera);
        _canvasTransform.localRotation = Quaternion.identity;
    }

    private IEnumerator ShowLogoRoutine()
    {
        Texture2D logo = LoadLogoTexture();
        if (logo == null)
        {
            if (verboseLogs)
                Debug.LogWarning("[StartupLogoOverlay] Logo non trovato in Resources.");
            Destroy(gameObject);
            yield break;
        }

        const float cameraWaitTimeout = 8f;
        float timeoutAt = Time.realtimeSinceStartup + cameraWaitTimeout;
        while (ResolveCamera() == null && Time.realtimeSinceStartup < timeoutAt)
            yield return null;

        if (_targetCamera == null)
        {
            if (verboseLogs)
                Debug.LogWarning("[StartupLogoOverlay] Nessuna camera disponibile per mostrare il logo.");
            Destroy(gameObject);
            yield break;
        }

        BuildCanvas(logo);
        if (_canvasGroup == null)
        {
            Destroy(gameObject);
            yield break;
        }

        yield return Fade(0f, 1f, fadeSeconds);
        yield return new WaitForSecondsRealtime(holdSeconds);
        yield return Fade(1f, 0f, fadeSeconds);

        if (_canvasTransform != null)
            Destroy(_canvasTransform.gameObject);

        Destroy(gameObject);
    }

    private Texture2D LoadLogoTexture()
    {
        Texture2D logo = Resources.Load<Texture2D>(resourcesTextureName);
        if (logo != null)
            return logo;

        logo = Resources.Load<Texture2D>("LOGO NURE");
        return logo;
    }

    private Camera ResolveCamera()
    {
        if (_targetCamera != null && _targetCamera.isActiveAndEnabled)
            return _targetCamera;

        _targetCamera = Camera.main;
        if (_targetCamera != null && _targetCamera.isActiveAndEnabled)
            return _targetCamera;

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera c = cameras[i];
            if (c == null || !c.gameObject.scene.IsValid() || !c.isActiveAndEnabled)
                continue;

            _targetCamera = c;
            return _targetCamera;
        }

        return null;
    }

    private void BuildCanvas(Texture2D logo)
    {
        GameObject canvasGo = new GameObject("StartupLogoCanvas");
        DontDestroyOnLoad(canvasGo);

        _canvasTransform = canvasGo.transform;
        _canvasTransform.SetParent(_targetCamera.transform, false);
        _canvasTransform.localPosition = new Vector3(0f, 0f, distanceFromCamera);
        _canvasTransform.localRotation = Quaternion.identity;
        _canvasTransform.localScale = Vector3.one * 0.001f;

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = _targetCamera;
        canvas.sortingOrder = 5000;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        GraphicRaycaster raycaster = canvasGo.AddComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        _canvasGroup = canvasGo.AddComponent<CanvasGroup>();
        _canvasGroup.alpha = 0f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        float aspect = logo.height > 0 ? (float)logo.width / logo.height : 2f;
        float widthMeters = logoHeightMeters * aspect;
        canvasRect.sizeDelta = new Vector2(widthMeters * 1000f, logoHeightMeters * 1000f);

        GameObject logoGo = new GameObject("Logo");
        logoGo.transform.SetParent(canvasGo.transform, false);

        RectTransform logoRect = logoGo.AddComponent<RectTransform>();
        logoRect.anchorMin = Vector2.zero;
        logoRect.anchorMax = Vector2.one;
        logoRect.offsetMin = Vector2.zero;
        logoRect.offsetMax = Vector2.zero;

        RawImage image = logoGo.AddComponent<RawImage>();
        image.texture = logo;
        image.color = Color.white;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        if (_canvasGroup == null)
            yield break;

        if (duration <= 0f)
        {
            _canvasGroup.alpha = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            _canvasGroup.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }

        _canvasGroup.alpha = to;
    }
}
