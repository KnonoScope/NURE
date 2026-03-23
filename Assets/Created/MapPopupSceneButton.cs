using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class MapPopupSceneButton : MonoBehaviour
{
    [Header("Scene Binding")]
    public SceneGroupManager sceneGroupManager;
    public GameObject sceneRoot;

    [Header("Label")]
    [Tooltip("Nome visualizzato sul bottone. Se vuoto usa il nome della sceneRoot.")]
    public string sceneDisplayName = "";
    public bool autoBindLabelComponents = true;
    public Text uiText;
    public TMP_Text tmpText;
    [Tooltip("Usa sempre UI.Text per il popup (evita artefatti TMP in world-space su alcuni setup).")]
    public bool forceLegacyUILabel = true;
    [Min(1)] public int worldLabelFontSize = 28;
    [Min(10)] public int minWorldLabelFontSize = 16;
    [Min(0f)] public float worldLabelHorizontalPadding = 20f;
    [Min(0f)] public float worldLabelVerticalPadding = 10f;
    public Color labelColor = new Color(0.98f, 0.96f, 0.9f, 1f);
    public Color surfaceColor = new Color(0.05f, 0.08f, 0.11f, 0.9f);
    public Color borderColor = new Color(0.77f, 0.66f, 0.45f, 0.72f);
    public Color shadowColor = new Color(0f, 0f, 0f, 0.32f);

    [Header("Runtime")]
    public bool disableIfSceneMissing = true;

    [Header("Auto Size")]
    [Tooltip("Ridimensiona canvas e bottone in base al testo visualizzato per vedere la dimensione reale dell'etichetta.")]
    public bool autoSizeFromLabel = true;
    [Tooltip("Fit stretto: riduce al minimo gli spazi vuoti intorno al testo.")]
    public bool tightFitLabel = true;
    [Min(0f)] public float tightFitPaddingX = 8f;
    [Min(0f)] public float tightFitPaddingY = 4f;
    [Min(0f)] public float horizontalPadding = 24f;
    [Min(0f)] public float verticalPadding = 12f;
    [Min(1f)] public float minWidth = 80f;
    [Min(1f)] public float minHeight = 32f;
    [Min(1f)] public float maxWidth = 420f;
    [Min(1f)] public float maxHeight = 120f;

    private Button _button;
    private XRBaseInteractable _xrInteractable;
    private Collider _xrCollider;
    private float _lastPressTime = -10f;
    [Min(0f)] public float pressDebounceSeconds = 0.12f;
    [Header("Hand Fallback")]
    [Tooltip("Se true, in assenza di trigger controller consente attivazione con hover di interattori mano/poke.")]
    public bool enableHandHoverFallback = true;
    [Min(0f)] public float handHoverActivationDelay = 0.2f;
    private bool _handHoverActive;
    private float _handHoverStartTime;
    private IXRHoverInteractor _handHoverInteractor;

    private void Awake()
    {
        CacheComponents();
        RefreshVisuals();
    }

    private void OnEnable()
    {
        CacheComponents();

        if (_button != null)
            _button.onClick.AddListener(OnPressed);

        if (_xrInteractable != null)
            _xrInteractable.selectEntered.AddListener(OnXRSelectEntered);

        if (_xrInteractable != null)
            _xrInteractable.activated.AddListener(OnXRActivated);

        if (_xrInteractable != null)
            _xrInteractable.hoverEntered.AddListener(OnXRHoverEntered);

        if (_xrInteractable != null)
            _xrInteractable.hoverExited.AddListener(OnXRHoverExited);

        RefreshVisuals();
    }

    private void OnDisable()
    {
        if (_button != null)
            _button.onClick.RemoveListener(OnPressed);

        if (_xrInteractable != null)
            _xrInteractable.selectEntered.RemoveListener(OnXRSelectEntered);

        if (_xrInteractable != null)
            _xrInteractable.activated.RemoveListener(OnXRActivated);

        if (_xrInteractable != null)
            _xrInteractable.hoverEntered.RemoveListener(OnXRHoverEntered);

        if (_xrInteractable != null)
            _xrInteractable.hoverExited.RemoveListener(OnXRHoverExited);
    }

    public void Configure(SceneGroupManager manager, GameObject targetScene, string label)
    {
        sceneGroupManager = manager;
        sceneRoot = targetScene;
        sceneDisplayName = label;
        RefreshVisuals();
    }

    public void OnPressed()
    {
        TryPress();
    }

    private void TryPress()
    {
        if (Time.unscaledTime - _lastPressTime < pressDebounceSeconds)
            return;

        _lastPressTime = Time.unscaledTime;

        if (sceneGroupManager == null)
        {
            Debug.LogWarning("[MapPopupSceneButton] SceneGroupManager non assegnato.");
            return;
        }

        if (sceneRoot == null)
        {
            Debug.LogWarning("[MapPopupSceneButton] sceneRoot non assegnato.");
            return;
        }

        sceneGroupManager.ActivateScene(sceneRoot);
    }

    private void OnXRActivated(ActivateEventArgs _)
    {
        TryPress();
    }

    private void OnXRSelectEntered(SelectEnterEventArgs _)
    {
        TryPress();
    }

    private void OnXRHoverEntered(HoverEnterEventArgs args)
    {
        if (!enableHandHoverFallback || args.interactorObject == null)
            return;

        if (!IsHandLikeInteractor(args.interactorObject))
            return;

        _handHoverInteractor = args.interactorObject;
        _handHoverActive = true;
        _handHoverStartTime = Time.unscaledTime;
    }

    private void OnXRHoverExited(HoverExitEventArgs args)
    {
        if (args.interactorObject != _handHoverInteractor)
            return;

        _handHoverInteractor = null;
        _handHoverActive = false;
    }

    private void CacheComponents()
    {
        if (_button == null)
            _button = GetComponent<Button>();

        if (_xrCollider == null)
            _xrCollider = GetComponent<Collider>();

        if (_xrInteractable == null)
            _xrInteractable = GetComponent<XRBaseInteractable>();

        EnsureXRInteractableSupport();

        if (autoBindLabelComponents)
        {
            if (uiText == null)
                uiText = GetComponentInChildren<Text>(true);

            if (tmpText == null)
                tmpText = GetComponentInChildren<TMP_Text>(true);
        }

        EnsureLabelRenderer();
    }

    private void Update()
    {
        if (!_handHoverActive)
            return;

        if (Time.unscaledTime - _handHoverStartTime < handHoverActivationDelay)
            return;

        _handHoverActive = false;
        _handHoverInteractor = null;
        TryPress();
    }

    private static bool IsHandLikeInteractor(IXRInteractor interactor)
    {
        if (interactor == null || interactor.transform == null)
            return false;

        string typeName = interactor.GetType().Name.ToLowerInvariant();
        if (typeName.Contains("poke"))
            return true;

        string path = GetTransformPathLower(interactor.transform, 6);
        return path.Contains("hand") || path.Contains("poke");
    }

    private static string GetTransformPathLower(Transform t, int maxDepth)
    {
        if (t == null)
            return string.Empty;

        string path = t.name;
        Transform current = t.parent;
        int depth = 0;
        while (current != null && depth < maxDepth)
        {
            path = current.name + "/" + path;
            current = current.parent;
            depth++;
        }

        return path.ToLowerInvariant();
    }

    private void EnsureXRInteractableSupport()
    {
        if (_xrCollider == null)
            _xrCollider = CreateOrUpdateBoxColliderFromRect();
        else
            UpdateColliderFromRect(_xrCollider as BoxCollider);

        if (_xrInteractable == null)
            _xrInteractable = GetComponent<XRBaseInteractable>();

        if (_xrInteractable == null)
            _xrInteractable = gameObject.AddComponent<XRSimpleInteractable>();
    }

    private Collider CreateOrUpdateBoxColliderFromRect()
    {
        BoxCollider bc = GetComponent<BoxCollider>();
        if (bc == null)
            bc = gameObject.AddComponent<BoxCollider>();

        UpdateColliderFromRect(bc);
        return bc;
    }

    private void UpdateColliderFromRect(BoxCollider bc)
    {
        if (bc == null)
            return;

        RectTransform rt = transform as RectTransform;
        if (rt != null)
        {
            Rect r = rt.rect;
            bc.center = new Vector3(r.center.x, r.center.y, 0f);
            bc.size = new Vector3(Mathf.Max(1f, r.width), Mathf.Max(1f, r.height), 10f);
            bc.isTrigger = true;
        }
        else
        {
            bc.center = Vector3.zero;
            bc.size = Vector3.one * 0.1f;
            bc.isTrigger = true;
        }
    }

    private void RefreshVisuals()
    {
        ApplySurfaceStyle();
        ApplyLabelStyle();

        tightFitPaddingX = Mathf.Max(tightFitPaddingX, 20f);
        tightFitPaddingY = Mathf.Max(tightFitPaddingY, 10f);
        maxWidth = Mathf.Max(maxWidth, 520f);
        minHeight = Mathf.Max(minHeight, 48f);

        string rawLabel = ResolveLabel();
        string displayLabel = rawLabel;

        if (uiText != null)
        {
            int resolvedFontSize = ResolveLegacyFontSize(
                uiText,
                rawLabel,
                worldLabelFontSize,
                minWorldLabelFontSize,
                Mathf.Max(80f, maxWidth - (tightFitPaddingX * 2f)));
            uiText.fontSize = resolvedFontSize;
            displayLabel = BuildWordWrappedLegacyLabel(
                uiText,
                rawLabel,
                resolvedFontSize,
                Mathf.Max(80f, maxWidth - (tightFitPaddingX * 2f)));
            uiText.text = displayLabel;
        }

        if (tmpText != null && !forceLegacyUILabel)
            tmpText.text = displayLabel;

        if (autoSizeFromLabel)
            ResizeFromLabel();

        bool valid = sceneGroupManager != null && sceneRoot != null;

        if (disableIfSceneMissing && _button != null)
            _button.interactable = valid;
    }

    private void ApplyLabelStyle()
    {
        if (uiText == null)
            return;

        uiText.resizeTextForBestFit = false;
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiText.fontSize = Mathf.Max(1, worldLabelFontSize);
        uiText.fontStyle = FontStyle.Bold;
        uiText.alignment = TextAnchor.MiddleCenter;
        uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
        uiText.verticalOverflow = VerticalWrapMode.Overflow;
        uiText.color = labelColor;
        uiText.supportRichText = false;
        ApplyTextRectPadding(uiText.rectTransform, worldLabelHorizontalPadding, worldLabelVerticalPadding);
        ApplyLabelShadow(uiText);
    }

    private void ResizeFromLabel()
    {
        float textWidth = 0f;
        float textHeight = 0f;
        float padX = tightFitLabel ? tightFitPaddingX : horizontalPadding;
        float padY = tightFitLabel ? tightFitPaddingY : verticalPadding;
        float minW = tightFitLabel ? 1f : minWidth;
        float minH = tightFitLabel ? 1f : minHeight;

        if (forceLegacyUILabel && uiText != null)
        {
            TextGenerationSettings s = uiText.GetGenerationSettings(new Vector2(10000f, 10000f));
            textWidth = uiText.cachedTextGeneratorForLayout.GetPreferredWidth(uiText.text, s) / uiText.pixelsPerUnit;
            float measuredWidth = Mathf.Clamp(textWidth + padX, minW, maxWidth);
            float innerWidth = Mathf.Max(1f, measuredWidth - padX);
            s.generationExtents = new Vector2(innerWidth, 10000f);
            textHeight = uiText.cachedTextGeneratorForLayout.GetPreferredHeight(uiText.text, s) / uiText.pixelsPerUnit;
        }
        else if (tmpText != null)
        {
            tmpText.ForceMeshUpdate();
            textWidth = tmpText.preferredWidth;
            float measuredWidth = Mathf.Clamp(textWidth + padX, minW, maxWidth);
            float innerWidth = Mathf.Max(1f, measuredWidth - padX);
            textHeight = tmpText.GetPreferredValues(tmpText.text, innerWidth, 10000f).y;
        }
        else if (uiText != null)
        {
            TextGenerationSettings s = uiText.GetGenerationSettings(Vector2.zero);
            TextGenerator g = new TextGenerator();
            textWidth = g.GetPreferredWidth(uiText.text, s);
            float measuredWidth = Mathf.Clamp(textWidth + padX, minW, maxWidth);
            float innerWidth = Mathf.Max(1f, measuredWidth - padX);
            s.generationExtents = new Vector2(innerWidth, 10000f);
            textHeight = g.GetPreferredHeight(uiText.text, s);
        }

        if (textWidth <= 0f || textHeight <= 0f)
            return;

        float desiredWidth = Mathf.Clamp(textWidth + padX, minW, maxWidth);
        float desiredHeight = Mathf.Clamp(textHeight + padY, minH, maxHeight);

        RectTransform buttonRect = transform as RectTransform;
        RectTransform canvasRect = buttonRect != null ? buttonRect.parent as RectTransform : null;
        if (canvasRect != null)
            canvasRect.sizeDelta = new Vector2(desiredWidth, desiredHeight);

        if (buttonRect != null)
        {
            buttonRect.anchorMin = Vector2.zero;
            buttonRect.anchorMax = Vector2.one;
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;
        }
    }

    private void EnsureLabelRenderer()
    {
        if (!forceLegacyUILabel)
            return;

        if (tmpText != null)
            tmpText.enabled = false;

        if (uiText == null)
        {
            GameObject go = new GameObject("LegacyLabel");
            go.transform.SetParent(transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            uiText = go.AddComponent<Text>();
            uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            uiText.alignment = TextAnchor.MiddleCenter;
            uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            uiText.verticalOverflow = VerticalWrapMode.Overflow;
            uiText.resizeTextForBestFit = false;
            uiText.fontSize = Mathf.Max(1, worldLabelFontSize);
            uiText.raycastTarget = false;
            uiText.color = labelColor;
        }
    }

    private string ResolveLabel()
    {
        if (!string.IsNullOrWhiteSpace(sceneDisplayName))
            return sceneDisplayName;

        return sceneRoot != null ? sceneRoot.name : "Scena";
    }

    private void OnValidate()
    {
        CacheComponents();
        RefreshVisuals();
    }

    private void ApplySurfaceStyle()
    {
        Image image = GetComponent<Image>();
        if (image != null)
        {
            image.color = surfaceColor;
            ApplySurfaceGraphicStyle(image, borderColor, shadowColor, new Vector2(1.5f, -1.5f), new Vector2(0f, -4f));
        }

        if (_button != null)
        {
            ColorBlock colors = _button.colors;
            colors.normalColor = surfaceColor;
            colors.highlightedColor = Color.Lerp(surfaceColor, Color.white, 0.12f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = Color.Lerp(surfaceColor, Color.black, 0.18f);
            colors.disabledColor = new Color(surfaceColor.r, surfaceColor.g, surfaceColor.b, 0.35f);
            _button.colors = colors;
        }
    }

    private static void ApplyTextRectPadding(RectTransform rect, float horizontalPadding, float verticalPadding)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
        rect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);
    }

    private static int ResolveLegacyFontSize(Text label, string rawLabel, int preferredFontSize, int minFontSize, float maxWidth)
    {
        int safePreferred = Mathf.Max(1, preferredFontSize);
        int safeMin = Mathf.Clamp(minFontSize, 1, safePreferred);
        string[] words = NormalizeLabelWhitespace(rawLabel).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

        for (int fontSize = safePreferred; fontSize >= safeMin; fontSize--)
        {
            bool fits = true;
            for (int i = 0; i < words.Length; i++)
            {
                if (MeasureLegacyTextWidth(label, words[i], fontSize) > maxWidth + 0.01f)
                {
                    fits = false;
                    break;
                }
            }

            if (fits)
                return fontSize;
        }

        return safeMin;
    }

    private static string BuildWordWrappedLegacyLabel(Text label, string rawLabel, int fontSize, float maxWidth)
    {
        string normalized = NormalizeLabelWhitespace(rawLabel);
        if (string.IsNullOrEmpty(normalized))
            return string.Empty;

        string[] words = normalized.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return string.Empty;

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        string currentLine = words[0];

        for (int i = 1; i < words.Length; i++)
        {
            string candidate = $"{currentLine} {words[i]}";
            if (MeasureLegacyTextWidth(label, candidate, fontSize) <= maxWidth + 0.01f)
            {
                currentLine = candidate;
                continue;
            }

            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(currentLine);
            currentLine = words[i];
        }

        if (builder.Length > 0)
            builder.Append('\n');

        builder.Append(currentLine);
        return builder.ToString();
    }

    private static float MeasureLegacyTextWidth(Text label, string text, int fontSize)
    {
        if (label == null)
            return 0f;

        TextGenerationSettings settings = label.GetGenerationSettings(new Vector2(10000f, 10000f));
        settings.resizeTextForBestFit = false;
        settings.scaleFactor = 1f;
        settings.fontSize = fontSize;
        settings.generationExtents = new Vector2(10000f, 10000f);
        return label.cachedTextGeneratorForLayout.GetPreferredWidth(text, settings) / Mathf.Max(1f, label.pixelsPerUnit);
    }

    private static string NormalizeLabelWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string[] tokens = value.Replace('\n', ' ').Replace('\r', ' ').Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", tokens);
    }

    private static void ApplySurfaceGraphicStyle(Graphic graphic, Color borderColor, Color shadowColor, Vector2 borderDistance, Vector2 shadowDistance)
    {
        if (graphic == null)
            return;

        Outline outline = graphic.GetComponent<Outline>();
        if (outline == null)
            outline = graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = borderColor;
        outline.effectDistance = borderDistance;
        outline.useGraphicAlpha = true;

        Shadow shadow = GetOrCreateShadowEffect(graphic);
        shadow.effectColor = shadowColor;
        shadow.effectDistance = shadowDistance;
        shadow.useGraphicAlpha = true;
    }

    private static void ApplyLabelShadow(Graphic graphic)
    {
        if (graphic == null)
            return;

        Shadow shadow = GetOrCreateShadowEffect(graphic);
        shadow.effectColor = new Color(0f, 0f, 0f, 0.38f);
        shadow.effectDistance = new Vector2(0f, -1.5f);
        shadow.useGraphicAlpha = true;
    }

    private static Shadow GetOrCreateShadowEffect(Graphic graphic)
    {
        Shadow[] effects = graphic.GetComponents<Shadow>();
        for (int i = 0; i < effects.Length; i++)
        {
            if (effects[i] != null && !(effects[i] is Outline))
                return effects[i];
        }

        return graphic.gameObject.AddComponent<Shadow>();
    }
}
