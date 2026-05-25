using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class MapPopupMenuController : MonoBehaviour
{
    [Serializable]
    public class MapPopupPoint
    {
        public string id = "Punto";
        public Transform anchor;
        public GameObject popupRoot;
        [HideInInspector] public MapPopupSceneButton popupButton;
        [HideInInspector] public GameObject sceneRoot;
        [Tooltip("Se vuoto, usa il nome scena.")]
        public string buttonLabel = "";
        [HideInInspector] public LineRenderer lazoLine;
    }

    [Header("References")]
    public SceneGroupManager sceneGroupManager;
    public HandRadialVirtualSceneMenu radialMenu;
    [Tooltip("Se nullo usa Camera.main.")]
    public Transform cameraToFace;

    [Header("Points")]
    [Min(1)] public int expectedPointCount = 9;
    public List<MapPopupPoint> points = new List<MapPopupPoint>(9);
    [Tooltip("Se true, prova ad assegnare automaticamente gli anchor cercando figli tipo 'point 1', 'point1', 'MapPoint_01'.")]
    public bool autoFindAnchorsByName = true;

    [Header("Behavior")]
    public bool autoBindButtons = true;
    public bool keepPopupAttachedToAnchor = true;
    public bool faceCamera = true;
    [Tooltip("Ruota il canvas popup di 180 gradi in Y per mostrare il fronte verso la camera.")]
    public bool flipPopupCanvasY = true;
    [Tooltip("Spinge il popup verso la camera per evitare z-fighting con il modello.")]
    [Min(0f)] public float popupDepthOffsetTowardsCamera = 0.02f;
    public bool constrainUpright = true;
    public bool hidePopupsWhenMissingScene = false;
    [Tooltip("Offset verticale standard del popup rispetto all'anchor.")]
    public float popupHeight = 0.25f;
    [Tooltip("Se true, popupRoot viene trovato o creato automaticamente sotto l'anchor.")]
    public bool autoFindOrCreatePopupRoot = true;

    [Header("Auto Scene Mapping")]
    [Tooltip("Se true, sceneRoot viene preso automaticamente da SceneGroupManager in base all'indice del punto.")]
    public bool autoMapSceneByIndex = true;
    [Tooltip("Offset indice scena su SceneGroupManager.scenes (1 = parte da Scena2).")]
    public int sceneIndexOffset = 1;

    [Header("Label Sync")]
    [Tooltip("Se true, i label dei popup vengono copiati dal radial menu (modifica una volta, vale ovunque).")]
    public bool syncLabelsFromRadialMenu = true;

    [Header("Lazo")]
    public bool autoCreateLazoLines = true;
    public bool autoUpdateLazoLines = true;
    public Material defaultLazoMaterial;
    [Min(0.0005f)] public float lazoWidth = 0.02f;
    public Color lazoColor = Color.white;
    public bool useCurvedLazo = true;
    [Range(4, 32)] public int lazoSegments = 20;
    [Min(0f)] public float lazoCurveAmount = 0.11f;
    [Min(0f)] public float lazoSwayAmplitude = 0.012f;
    [Min(0f)] public float lazoSwaySpeed = 1.0f;

    [Header("Popup Auto Visual")]
    public bool autoCreateDefaultPopupButton = true;
    public Vector2 popupCanvasSize = new Vector2(1000f, 280f);
    [Tooltip("Scala locale del PopupCanvas (come da setup VR richiesto).")]
    public Vector3 popupCanvasLocalScale = new Vector3(0.05092074f, 0.05951776f, 0.9250912f);
    [Tooltip("Se true, usa una scala uniforme in metri-per-pixel per mantenere il fit reale del testo in world-space.")]
    public bool normalizePopupScaleByPixels = false;
    [Min(0.00001f)] public float popupMetersPerPixel = 0.0012f;
    [Min(0.01f)] public float popupWorldScaleMultiplier = 1f;
    public Color popupBackgroundColor = new Color(0f, 0f, 0f, 0.75f);
    public Color popupTextColor = Color.white;
    [Tooltip("Forza un sorting order stabile dei canvas popup per ridurre flicker di trasparenze.")]
    public bool forceCanvasSortingOrder = true;
    public int popupCanvasSortingOrderStart = 200;
    [Header("Popup Text")]
    [Tooltip("Forza la dimensione font dei label popup su tutti i point.")]
    public bool forcePopupFontSize = true;
    [Min(1)] public int popupFontSize = 28;
    [Tooltip("Clamp di sicurezza del font popup per evitare esplosioni da valori serializzati errati.")]
    [Min(1)] public int popupFontSizeMax = 64;

    private void Awake()
    {
        // Manteniamo la scala storica dei popup: niente normalizzazione pixel-based.
        normalizePopupScaleByPixels = false;
        popupWorldScaleMultiplier = 1f;
        forcePopupFontSize = true;
        EnsurePointSlots();
        SyncAll();
    }

    private void LateUpdate()
    {
        SyncTransformsOnly();
    }

    [ContextMenu("Setup/Ensure Point Slots")]
    public void EnsurePointSlots()
    {
        if (expectedPointCount < 1)
            expectedPointCount = 1;

        while (points.Count < expectedPointCount)
        {
            int idx = points.Count + 1;
            points.Add(new MapPopupPoint
            {
                id = $"Punto {idx}",
                buttonLabel = $"Scena {idx + sceneIndexOffset}"
            });
        }
    }

    [ContextMenu("Setup/Create Missing Anchors")]
    public void CreateMissingAnchors()
    {
        EnsurePointSlots();

        Transform anchorsRoot = transform.Find("MapPopupPoints");
        if (anchorsRoot == null)
        {
            var root = new GameObject("MapPopupPoints");
            anchorsRoot = root.transform;
            anchorsRoot.SetParent(transform, false);
        }

        for (int i = 0; i < points.Count; i++)
        {
            MapPopupPoint p = points[i];
            if (p == null)
                continue;

            if (p.anchor == null)
            {
                var anchorGo = new GameObject($"MapPoint_{i + 1:00}");
                p.anchor = anchorGo.transform;
                p.anchor.SetParent(anchorsRoot, false);
                p.anchor.localPosition = new Vector3(i * 0.08f, 0f, 0f);
            }

            if (p.popupRoot == null)
            {
                var popupGo = new GameObject($"Popup_{i + 1:00}");
                p.popupRoot = popupGo;
                p.popupRoot.transform.SetParent(p.anchor, false);
                p.popupRoot.transform.localPosition = Vector3.up * popupHeight;
            }

            if (p.popupButton == null && p.popupRoot != null)
                p.popupButton = p.popupRoot.GetComponentInChildren<MapPopupSceneButton>(true);
        }

        SyncAll();
    }

    [ContextMenu("Setup/Sync Now")]
    public void SyncAll()
    {
        EnsurePointSlots();
        AutoAssignAnchorsIfMissing();
        SyncBindings();
        SyncTransformsOnly();
    }

    private void SyncBindings()
    {
        if (!autoBindButtons)
            return;

        for (int i = 0; i < points.Count; i++)
        {
            MapPopupPoint p = points[i];
            if (p == null)
                continue;

            EnsurePopupRoot(i, p);
            TryAutoAssignSceneRoot(i, p);

            if (autoCreateDefaultPopupButton)
                EnsureDefaultPopupButtonVisual(i, p);

            RefreshPopupVisualScale(p);

            if (p.popupRoot != null && p.popupButton == null)
                p.popupButton = p.popupRoot.GetComponentInChildren<MapPopupSceneButton>(true);

            if (p.popupButton == null)
                continue;

            if (forcePopupFontSize)
            {
                int safeFont = Mathf.Clamp(popupFontSize, 1, Mathf.Max(1, popupFontSizeMax));
                p.popupButton.worldLabelFontSize = safeFont;
            }

            string label = ResolveButtonLabel(i, p);
            p.popupButton.Configure(sceneGroupManager, p.sceneRoot, label);
            ApplyReadableNames(i, p, label);
        }
    }

    private void SyncTransformsOnly()
    {
        Transform cam = ResolveCameraTransform();

        for (int i = 0; i < points.Count; i++)
        {
            MapPopupPoint p = points[i];
            if (p == null || p.anchor == null || p.popupRoot == null)
                continue;

            if (keepPopupAttachedToAnchor)
            {
                p.popupRoot.transform.position = p.anchor.position + (Vector3.up * popupHeight);
            }

            Vector3 toCam = Vector3.zero;
            if (faceCamera && cam != null)
            {
                toCam = cam.position - p.popupRoot.transform.position;
                if (constrainUpright)
                    toCam.y = 0f;

                if (toCam.sqrMagnitude > 0.0001f)
                    p.popupRoot.transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
            }

            if (cam != null && popupDepthOffsetTowardsCamera > 0f)
            {
                if (toCam == Vector3.zero)
                    toCam = cam.position - p.popupRoot.transform.position;

                if (toCam.sqrMagnitude > 0.0001f)
                    p.popupRoot.transform.position += toCam.normalized * popupDepthOffsetTowardsCamera;
            }

            Transform popupCanvas = p.popupRoot.transform.Find("PopupCanvas");
            if (popupCanvas != null)
            {
                popupCanvas.localRotation = Quaternion.Euler(0f, flipPopupCanvasY ? 180f : 0f, 0f);
                Canvas canvas = popupCanvas.GetComponent<Canvas>();
                if (canvas != null && forceCanvasSortingOrder)
                {
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = popupCanvasSortingOrderStart + i;
                }
            }

            if (hidePopupsWhenMissingScene)
                p.popupRoot.SetActive(p.sceneRoot != null);

        if (autoCreateLazoLines && p.lazoLine == null)
            p.lazoLine = CreateDefaultLazoLine(i, p);

            if (autoUpdateLazoLines && p.lazoLine != null)
            {
                if (p.lazoLine.sharedMaterial == null)
                    p.lazoLine.sharedMaterial = defaultLazoMaterial != null ? defaultLazoMaterial : GetRuntimeDefaultLineMaterial();

                p.lazoLine.widthMultiplier = lazoWidth;
                p.lazoLine.startWidth = lazoWidth;
                p.lazoLine.endWidth = lazoWidth;
                p.lazoLine.startColor = lazoColor;
                p.lazoLine.endColor = lazoColor;
                UpdateLazoLine(i, p);
            }
        }
    }

    private void UpdateLazoLine(int pointIndex, MapPopupPoint p)
    {
        if (p == null || p.lazoLine == null || p.anchor == null || p.popupRoot == null)
            return;

        Transform end = p.popupRoot.transform.Find("PopupCanvas");
        Vector3 endPos = end != null ? end.position : p.popupRoot.transform.position;
        Vector3 startPos = p.anchor.position;

        p.lazoLine.useWorldSpace = true;
        p.lazoLine.widthMultiplier = Mathf.Max(0.0005f, p.lazoLine.widthMultiplier);

        if (!useCurvedLazo)
        {
            p.lazoLine.positionCount = 2;
            p.lazoLine.SetPosition(0, startPos);
            p.lazoLine.SetPosition(1, endPos);
            return;
        }

        int segments = Mathf.Clamp(lazoSegments, 4, 32);
        p.lazoLine.positionCount = segments;

        Vector3 mid = (startPos + endPos) * 0.5f;
        Vector3 dir = (endPos - startPos).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        float swayPhase = Time.unscaledTime * lazoSwaySpeed + (pointIndex * 0.7f);
        mid += Vector3.down * lazoCurveAmount;
        mid += side * (Mathf.Sin(swayPhase) * lazoSwayAmplitude);

        for (int i = 0; i < segments; i++)
        {
            float t = i / (segments - 1f);
            // Bezier quadratica: curva morbida tra anchor e popup.
            Vector3 a = Vector3.Lerp(startPos, mid, t);
            Vector3 b = Vector3.Lerp(mid, endPos, t);
            Vector3 pos = Vector3.Lerp(a, b, t);
            p.lazoLine.SetPosition(i, pos);
        }
    }

    private void TryAutoAssignSceneRoot(int pointIndex, MapPopupPoint p)
    {
        if (!autoMapSceneByIndex || p == null || p.sceneRoot != null || sceneGroupManager == null || sceneGroupManager.scenes == null)
            return;

        int sceneIndex = pointIndex + sceneIndexOffset;
        if (sceneIndex < 0 || sceneIndex >= sceneGroupManager.scenes.Count)
            return;

        SceneGroupManager.VirtualScene cfg = sceneGroupManager.scenes[sceneIndex];
        if (cfg != null && cfg.root != null)
        {
            p.sceneRoot = cfg.root;
            if (string.IsNullOrWhiteSpace(p.buttonLabel))
                p.buttonLabel = !string.IsNullOrWhiteSpace(cfg.name) ? cfg.name : cfg.root.name;
        }
    }

    private string ResolveButtonLabel(int pointIndex, MapPopupPoint p)
    {
        if (sceneGroupManager != null && p != null && p.sceneRoot != null)
        {
            string localized = sceneGroupManager.GetLocalizedSceneLabel(p.sceneRoot);
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }

        if (syncLabelsFromRadialMenu && TryGetRadialLabel(pointIndex, p, out string radialLabel))
            return radialLabel;

        if (p != null && !string.IsNullOrWhiteSpace(p.buttonLabel))
            return p.buttonLabel;

        if (p != null && p.sceneRoot != null)
            return p.sceneRoot.name;

        return $"Scena {pointIndex + 1}";
    }

    private bool TryGetRadialLabel(int pointIndex, MapPopupPoint p, out string label)
    {
        label = null;

        HandRadialVirtualSceneMenu menu = radialMenu != null ? radialMenu : FindFirstObjectByType<HandRadialVirtualSceneMenu>();
        if (menu == null)
            return false;

        GameObject root = menu.menuRoot != null ? menu.menuRoot : menu.gameObject;
        VirtualSceneMenuButton[] buttons = root.GetComponentsInChildren<VirtualSceneMenuButton>(true);
        if (buttons == null || buttons.Length == 0)
            return false;

        int sceneIndex = ResolveSceneIndex(pointIndex, p);
        int desiredButtonIndex = sceneIndex + 1; // radial usa index 1-based

        VirtualSceneMenuButton best = null;

        if (p != null && p.sceneRoot != null && menu.targets != null)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                VirtualSceneMenuButton b = buttons[i];
                if (b == null) continue;

                int zi = b.ZeroBasedIndex;
                if (zi >= 0 && zi < menu.targets.Length && menu.targets[zi] == p.sceneRoot)
                {
                    best = b;
                    break;
                }
            }
        }

        if (best == null)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                VirtualSceneMenuButton b = buttons[i];
                if (b != null && b.index == desiredButtonIndex)
                {
                    best = b;
                    break;
                }
            }
        }

        if (best == null)
            return false;

        string resolved = !string.IsNullOrWhiteSpace(best.customLabel)
            ? best.customLabel
            : best.GetResolvedLabel();
        if (string.IsNullOrWhiteSpace(resolved))
            return false;

        label = resolved;
        return true;
    }

    private int ResolveSceneIndex(int pointIndex, MapPopupPoint p)
    {
        // 1) Se il point ha una sceneRoot valida, usa la sua posizione nella lista SceneGroupManager.
        if (sceneGroupManager != null && sceneGroupManager.scenes != null && p != null && p.sceneRoot != null)
        {
            for (int i = 0; i < sceneGroupManager.scenes.Count; i++)
            {
                SceneGroupManager.VirtualScene cfg = sceneGroupManager.scenes[i];
                if (cfg != null && cfg.root == p.sceneRoot)
                    return i;
            }
        }

        // 2) Se buttonLabel contiene un numero (es. "Scena 7"), usa quel numero come indice 1-based.
        if (TryExtractInt(p != null ? p.buttonLabel : null, out int oneBasedSceneIndex) && oneBasedSceneIndex > 0)
            return oneBasedSceneIndex - 1;

        // 3) Fallback storico.
        return pointIndex + sceneIndexOffset;
    }

    private static bool TryExtractInt(string source, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(source))
            return false;

        int end = -1;
        for (int i = source.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(source[i]))
            {
                end = i;
                int start = i;
                while (start > 0 && char.IsDigit(source[start - 1]))
                    start--;

                string number = source.Substring(start, end - start + 1);
                return int.TryParse(number, out value);
            }
        }

        return false;
    }

    private void AutoAssignAnchorsIfMissing()
    {
        if (!autoFindAnchorsByName)
            return;

        for (int i = 0; i < points.Count; i++)
        {
            MapPopupPoint p = points[i];
            if (p == null || p.anchor != null)
                continue;

            int idx = i + 1;
            Transform found =
                transform.Find($"point {idx}") ??
                transform.Find($"point{idx}") ??
                transform.Find($"Point {idx}") ??
                transform.Find($"Point{idx}") ??
                transform.Find($"MapPoint_{idx:00}");

            if (found != null)
                p.anchor = found;
        }
    }

    private LineRenderer CreateDefaultLazoLine(int pointIndex, MapPopupPoint p)
    {
        if (p == null || p.anchor == null)
            return null;

        Transform root = transform.Find("MapPopupLines");
        if (root == null)
        {
            GameObject go = new GameObject("MapPopupLines");
            root = go.transform;
            root.SetParent(transform, false);
        }

        GameObject lineGo = new GameObject($"Lazo_{pointIndex + 1:00}");
        lineGo.transform.SetParent(root, false);
        LineRenderer lr = lineGo.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.widthMultiplier = lazoWidth;
        lr.startWidth = lazoWidth;
        lr.endWidth = lazoWidth;
        lr.alignment = LineAlignment.View;
        lr.startColor = lazoColor;
        lr.endColor = lazoColor;
        lr.numCornerVertices = 4;
        lr.numCapVertices = 4;

        if (defaultLazoMaterial != null)
            lr.sharedMaterial = defaultLazoMaterial;
        else
            lr.sharedMaterial = GetRuntimeDefaultLineMaterial();

        return lr;
    }

    private void EnsureDefaultPopupButtonVisual(int pointIndex, MapPopupPoint p)
    {
        if (p == null || p.popupRoot == null)
            return;

        if (p.popupButton != null)
            return;

        MapPopupSceneButton existing = p.popupRoot.GetComponentInChildren<MapPopupSceneButton>(true);
        if (existing != null)
        {
            p.popupButton = existing;
            return;
        }

        GameObject canvasGo = new GameObject("PopupCanvas");
        canvasGo.transform.SetParent(p.popupRoot.transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = popupCanvasSize;
        canvasGo.transform.localScale = popupCanvasLocalScale;

        GameObject buttonGo = new GameObject("PopupButton");
        buttonGo.transform.SetParent(canvasGo.transform, false);

        RectTransform buttonRect = buttonGo.AddComponent<RectTransform>();
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.one;
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        Image image = buttonGo.AddComponent<Image>();
        image.color = popupBackgroundColor;
        buttonGo.AddComponent<Button>();

        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(buttonGo.transform, false);

        RectTransform labelRect = labelGo.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        Text txt = labelGo.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.alignment = TextAnchor.MiddleCenter;
        txt.resizeTextForBestFit = true;
        txt.resizeTextMinSize = 20;
        txt.resizeTextMaxSize = 120;
        txt.color = popupTextColor;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;

        MapPopupSceneButton popupButton = buttonGo.AddComponent<MapPopupSceneButton>();
        popupButton.autoBindLabelComponents = false;
        popupButton.uiText = txt;

        p.popupButton = popupButton;
    }

    private void RefreshPopupVisualScale(MapPopupPoint p)
    {
        if (p == null || p.popupRoot == null)
            return;

        Transform canvasTr = p.popupRoot.transform.Find("PopupCanvas");
        if (canvasTr != null)
        {
            if (normalizePopupScaleByPixels)
            {
                float s = Mathf.Max(0.00001f, popupMetersPerPixel);
                canvasTr.localScale = new Vector3(s, s, 1f);
            }
            else
            {
                float safeScale = Mathf.Clamp(popupWorldScaleMultiplier, 0.1f, 1.25f);
                canvasTr.localScale = popupCanvasLocalScale * safeScale;
            }
        }
    }

    private void EnsurePopupRoot(int pointIndex, MapPopupPoint p)
    {
        if (!autoFindOrCreatePopupRoot || p == null || p.anchor == null || p.popupRoot != null)
            return;

        string expectedName = $"Popup_{pointIndex + 1:00}";
        Transform byName = p.anchor.Find(expectedName);
        if (byName != null)
        {
            p.popupRoot = byName.gameObject;
            return;
        }

        for (int i = 0; i < p.anchor.childCount; i++)
        {
            Transform child = p.anchor.GetChild(i);
            if (child != null && child.name.StartsWith(expectedName, StringComparison.Ordinal))
            {
                p.popupRoot = child.gameObject;
                return;
            }
        }

        MapPopupSceneButton existingButton = p.anchor.GetComponentInChildren<MapPopupSceneButton>(true);
        if (existingButton != null)
        {
            Transform popupCanvas = existingButton.transform.parent;
            Transform popupRoot = popupCanvas != null ? popupCanvas.parent : existingButton.transform.parent;
            p.popupRoot = popupRoot != null ? popupRoot.gameObject : existingButton.gameObject;
            return;
        }

        GameObject popupGo = new GameObject(expectedName);
        popupGo.transform.SetParent(p.anchor, false);
        popupGo.transform.localPosition = Vector3.up * popupHeight;
        p.popupRoot = popupGo;
    }

    private static void ApplyReadableNames(int pointIndex, MapPopupPoint p, string label)
    {
        if (p == null)
            return;

        string readableLabel = SanitizeHierarchyLabel(label, pointIndex);
        p.id = readableLabel;

        if (p.popupRoot != null)
        {
            string popupRootName = $"Popup_{pointIndex + 1:00} - {readableLabel}";
            if (!string.Equals(p.popupRoot.name, popupRootName, StringComparison.Ordinal))
                p.popupRoot.name = popupRootName;
        }

        if (p.popupButton != null)
        {
            string buttonName = $"Targhetta - {readableLabel}";
            if (!string.Equals(p.popupButton.gameObject.name, buttonName, StringComparison.Ordinal))
                p.popupButton.gameObject.name = buttonName;

            if (p.popupButton.uiText != null)
            {
                string uiLabelName = $"Label - {readableLabel}";
                if (!string.Equals(p.popupButton.uiText.gameObject.name, uiLabelName, StringComparison.Ordinal))
                    p.popupButton.uiText.gameObject.name = uiLabelName;
            }

            if (p.popupButton.tmpText != null)
            {
                string tmpLabelName = $"TMP Label - {readableLabel}";
                if (!string.Equals(p.popupButton.tmpText.gameObject.name, tmpLabelName, StringComparison.Ordinal))
                    p.popupButton.tmpText.gameObject.name = tmpLabelName;
            }
        }
    }

    private static string SanitizeHierarchyLabel(string label, int pointIndex)
    {
        string readableLabel = string.IsNullOrWhiteSpace(label)
            ? $"Scena {pointIndex + 1}"
            : label.Trim();

        return readableLabel.Replace("/", "-");
    }

    private Transform ResolveCameraTransform()
    {
        if (cameraToFace != null)
            return cameraToFace;

        Camera cam = Camera.main;
        return cam != null ? cam.transform : null;
    }

    private void OnValidate()
    {
        normalizePopupScaleByPixels = false;
        popupWorldScaleMultiplier = Mathf.Clamp(popupWorldScaleMultiplier, 0.1f, 1.25f);
        popupFontSize = Mathf.Clamp(popupFontSize, 1, Mathf.Max(1, popupFontSizeMax));
        EnsurePointSlots();
        SyncAll();
    }

    private static Material _runtimeDefaultLineMaterial;

    private static Material GetRuntimeDefaultLineMaterial()
    {
        if (_runtimeDefaultLineMaterial != null)
            return _runtimeDefaultLineMaterial;

        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Unlit/Color");

        if (shader == null)
            return null;

        _runtimeDefaultLineMaterial = new Material(shader)
        {
            name = "Runtime_MapPopupLine_Mat"
        };

        if (_runtimeDefaultLineMaterial.HasProperty("_BaseColor"))
            _runtimeDefaultLineMaterial.SetColor("_BaseColor", Color.white);
        if (_runtimeDefaultLineMaterial.HasProperty("_Color"))
            _runtimeDefaultLineMaterial.SetColor("_Color", Color.white);

        return _runtimeDefaultLineMaterial;
    }
}
