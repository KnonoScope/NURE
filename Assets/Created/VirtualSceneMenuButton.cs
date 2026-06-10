using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class VirtualSceneMenuButton : MonoBehaviour
{
    [Header("Menu")]
    public HandRadialVirtualSceneMenu menu;

    [Header("Index (1-10)")]
    [Min(1)] public int index = 1;

    [Header("Optional Label")]
    public bool autoSetLabel = true;
    [Tooltip("Se valorizzato, sovrascrive la label automatica (index).")]
    public string customLabel = "";

    // Supporta sia UI.Text che TMP_Text
    public Text uiText;
    public TMP_Text tmpText;
    [Header("Label Layout")]
    public bool useWordSafeWrapping = false;
    [Min(1f)] public float labelWrapWidth = 96f;
    [Min(1)] public int preferredLabelFontSize = 18;
    [Min(1)] public int minLabelFontSize = 14;

    private Button _button;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable _xrInteractable;
    private Collider _xrCollider;
    private float _lastPressTime = -10f;
    [Min(0f)] public float pressDebounceSeconds = 0.12f;
    public Transform trackingOrigin;
    public bool useXRHandsPinch = true;
    public bool useLeftHandPinch = true;
    public bool useRightHandPinch = true;
    [Min(0.005f)] public float pinchPressDistance = 0.025f;
    [Min(0.005f)] public float pinchReleaseDistance = 0.04f;
    [Min(0f)] public float pinchTargetPadding = 0.02f;
    public Vector2 colliderPadding = new Vector2(18f, 14f);
    [Min(0.001f)] public float colliderDepth = 12f;
    [Header("Hand Fallback")]
    [Tooltip("Permette il click anche con hover/poke mano, se l'interazione non arriva perfettamente al pinch.")]
    public bool enableHandHoverFallback = true;
    [Min(0f)] public float handHoverActivationDelay = 0.14f;
    private XRHandsPinchUtility.HandSide? _activePinchSide;
    private bool _handHoverActive;
    private float _handHoverStartTime;
    private IXRHoverInteractor _handHoverInteractor;

    public int ZeroBasedIndex => index - 1;

    private void Awake()
    {
        if (menu == null)
            menu = GetComponentInParent<HandRadialVirtualSceneMenu>();

        _button = GetComponent<Button>();
        _xrInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
        EnsureXRInteractableSupport();

        CacheLabelRefs();
        UpdateLabel();
    }

    private void OnEnable()
    {
        if (_button == null)
            _button = GetComponent<Button>();

        if (_button != null)
            _button.onClick.AddListener(OnPressed);

        EnsureXRInteractableSupport();

        if (_xrInteractable != null)
            _xrInteractable.activated.AddListener(OnXRActivated);

        if (_xrInteractable != null)
            _xrInteractable.hoverEntered.AddListener(OnXRHoverEntered);

        if (_xrInteractable != null)
            _xrInteractable.hoverExited.AddListener(OnXRHoverExited);

        CacheLabelRefs();
        UpdateLabel();
    }

    private void OnDisable()
    {
        if (_button != null)
            _button.onClick.RemoveListener(OnPressed);

        if (_xrInteractable != null)
            _xrInteractable.activated.RemoveListener(OnXRActivated);

        _activePinchSide = null;
        _handHoverActive = false;
        _handHoverInteractor = null;

        if (_xrInteractable != null)
            _xrInteractable.hoverEntered.RemoveListener(OnXRHoverEntered);

        if (_xrInteractable != null)
            _xrInteractable.hoverExited.RemoveListener(OnXRHoverExited);
    }

    public void SetMenuIfMissing(HandRadialVirtualSceneMenu m)
    {
        if (menu == null)
            menu = m;
    }

    public void SetTrackingOrigin(Transform root)
    {
        trackingOrigin = root;
    }

    public void SetInteractable(bool value)
    {
        if (_button == null)
            _button = GetComponent<Button>();

        if (_button != null)
            _button.interactable = value;

        if (!value)
        {
            _handHoverActive = false;
            _handHoverInteractor = null;
        }
    }

    public void RefreshInteractableShape()
    {
        EnsureXRInteractableSupport();
    }

    public void ConfigureWordSafeLabel(float wrapWidth, int preferredFont, int minFont)
    {
        useWordSafeWrapping = true;
        labelWrapWidth = Mathf.Max(1f, wrapWidth);
        preferredLabelFontSize = Mathf.Max(1, preferredFont);
        minLabelFontSize = Mathf.Clamp(minFont, 1, preferredLabelFontSize);
        UpdateLabel();
    }

    public void RefreshLabel()
    {
        CacheLabelRefs();
        UpdateLabel();
    }

    public void OnPressed()
    {
        TryPress();
    }

    private void TryPress()
    {
        if (Time.unscaledTime - _lastPressTime < pressDebounceSeconds)
            return;

        if (_button != null && !_button.interactable)
            return;

        _lastPressTime = Time.unscaledTime;

        if (menu == null)
        {
            Debug.LogWarning("[VirtualSceneMenuButton] Menu non assegnato.");
            return;
        }

        menu.ActivateIndex(index);
    }

    private void OnXRActivated(ActivateEventArgs _)
    {
        TryPress();
    }

    private void Update()
    {
        UpdateHandHoverFallback();
        UpdatePinchInput();
    }

    private void UpdateHandHoverFallback()
    {
        if (!_handHoverActive)
            return;

        if (Time.unscaledTime - _handHoverStartTime < handHoverActivationDelay)
            return;

        _handHoverActive = false;
        _handHoverInteractor = null;
        TryPress();
    }

    private void CacheLabelRefs()
    {
        if (!autoSetLabel) return;

        if (uiText == null)
            uiText = GetComponentInChildren<Text>(true);

        if (tmpText == null)
            tmpText = GetComponentInChildren<TMP_Text>(true);
    }

    private void EnsureXRInteractableSupport()
    {
        if (_xrCollider == null)
            _xrCollider = GetComponent<Collider>();

        if (_xrCollider == null)
            _xrCollider = CreateOrUpdateBoxColliderFromRect();
        else
            UpdateColliderFromRect(_xrCollider as BoxCollider);

        if (_xrInteractable == null)
            _xrInteractable = GetComponent<XRBaseInteractable>();

        if (_xrInteractable == null)
            _xrInteractable = gameObject.AddComponent<XRSimpleInteractable>();

        SyncInteractableCollider();
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
            bc.size = new Vector3(
                Mathf.Max(1f, r.width + colliderPadding.x),
                Mathf.Max(1f, r.height + colliderPadding.y),
                Mathf.Max(0.001f, colliderDepth));
            bc.isTrigger = true;
        }
        else
        {
            bc.center = Vector3.zero;
            bc.size = new Vector3(0.12f, 0.08f, Mathf.Max(0.001f, colliderDepth));
            bc.isTrigger = true;
        }
    }

    private void SyncInteractableCollider()
    {
        if (_xrInteractable == null || _xrCollider == null)
            return;

        _xrInteractable.colliders.Clear();
        _xrInteractable.colliders.Add(_xrCollider);
        _xrInteractable.distanceCalculationMode = XRBaseInteractable.DistanceCalculationMode.ColliderVolume;
    }

    private void UpdateLabel()
    {
        if (!autoSetLabel) return;

        string rawLabel = GetBaseResolvedLabel();
        string formattedLabel = rawLabel;

        if (useWordSafeWrapping)
        {
            if (uiText != null)
            {
                int resolvedFontSize = ResolveLegacyFontSize(uiText, rawLabel, preferredLabelFontSize, minLabelFontSize, labelWrapWidth);
                uiText.fontSize = resolvedFontSize;
                formattedLabel = BuildWordWrappedLegacyLabel(uiText, rawLabel, resolvedFontSize, labelWrapWidth);
            }
            else if (tmpText != null)
            {
                int resolvedFontSize = ResolveTmpFontSize(tmpText, rawLabel, preferredLabelFontSize, minLabelFontSize, labelWrapWidth);
                tmpText.fontSize = resolvedFontSize;
                formattedLabel = BuildWordWrappedTmpLabel(tmpText, rawLabel, resolvedFontSize, labelWrapWidth);
            }
        }

        if (uiText != null)
            uiText.text = formattedLabel;

        if (tmpText != null)
        {
            if (useWordSafeWrapping)
            {
                int resolvedFontSize = ResolveTmpFontSize(tmpText, rawLabel, preferredLabelFontSize, minLabelFontSize, labelWrapWidth);
                tmpText.fontSize = resolvedFontSize;
            }

            tmpText.text = formattedLabel;
        }
    }

    public string GetResolvedLabel()
    {
        if (!string.IsNullOrWhiteSpace(customLabel))
            return customLabel;

        if (tmpText != null && !string.IsNullOrWhiteSpace(tmpText.text))
            return tmpText.text;

        if (uiText != null && !string.IsNullOrWhiteSpace(uiText.text))
            return uiText.text;

        return index.ToString();
    }

    private string GetBaseResolvedLabel()
    {
        return NormalizeLabelWhitespace(GetResolvedLabel());
    }

    private void OnValidate()
    {
        if (index < 1) index = 1;
        EnsureXRInteractableSupport();
        CacheLabelRefs();
        UpdateLabel();
    }

    private void UpdatePinchInput()
    {
        if (!useXRHandsPinch)
            return;

        if (_activePinchSide.HasValue)
        {
            if (XRHandsPinchUtility.TryGetPinchData(_activePinchSide.Value, trackingOrigin, out XRHandsPinchUtility.PinchData activePinch)
                && activePinch.pinchDistance <= pinchReleaseDistance)
                return;

            _activePinchSide = null;
        }

        if (TryPressFromPinch(XRHandsPinchUtility.HandSide.Right, useRightHandPinch))
            return;

        TryPressFromPinch(XRHandsPinchUtility.HandSide.Left, useLeftHandPinch);
    }

    private bool TryPressFromPinch(XRHandsPinchUtility.HandSide side, bool enabled)
    {
        if (!enabled)
            return false;

        if (!XRHandsPinchUtility.TryGetPinchData(side, trackingOrigin, out XRHandsPinchUtility.PinchData pinch)
            || pinch.pinchDistance > pinchPressDistance)
            return false;

        if (!IsPointNearTarget(pinch.pinchWorld))
            return false;

        _activePinchSide = side;
        TryPress();
        return true;
    }

    private bool IsPointNearTarget(Vector3 pointWorld)
    {
        Collider targetCollider = _xrCollider != null ? _xrCollider : GetComponent<Collider>();
        if (targetCollider == null)
            return false;

        Vector3 closestPoint = targetCollider.ClosestPoint(pointWorld);
        return (closestPoint - pointWorld).sqrMagnitude <= pinchTargetPadding * pinchTargetPadding;
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

    private static int ResolveTmpFontSize(TMP_Text label, string rawLabel, int preferredFontSize, int minFontSize, float maxWidth)
    {
        int safePreferred = Mathf.Max(1, preferredFontSize);
        int safeMin = Mathf.Clamp(minFontSize, 1, safePreferred);
        string[] words = NormalizeLabelWhitespace(rawLabel).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

        for (int fontSize = safePreferred; fontSize >= safeMin; fontSize--)
        {
            bool fits = true;
            for (int i = 0; i < words.Length; i++)
            {
                if (MeasureTmpTextWidth(label, words[i], fontSize) > maxWidth + 0.01f)
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

    private static string BuildWordWrappedTmpLabel(TMP_Text label, string rawLabel, float fontSize, float maxWidth)
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
            if (MeasureTmpTextWidth(label, candidate, fontSize) <= maxWidth + 0.01f)
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

    private static float MeasureTmpTextWidth(TMP_Text label, string text, float fontSize)
    {
        if (label == null)
            return 0f;

        float previousFontSize = label.fontSize;
        label.fontSize = fontSize;
        Vector2 preferred = label.GetPreferredValues(text, 10000f, 10000f);
        label.fontSize = previousFontSize;
        return preferred.x;
    }

    private static string NormalizeLabelWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string[] tokens = value.Replace('\n', ' ').Replace('\r', ' ').Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", tokens);
    }
}
