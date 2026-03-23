using UnityEngine;
using UnityEngine.XR;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_XR_HANDS
using UnityEngine.XR.Hands;
#endif

public class HandRadialVirtualSceneMenu : MonoBehaviour
{
    [Header("References")]
    public SceneGroupManager sceneGroupManager;
    public GameObject menuRoot;

    [Header("Targets (10)")]
    [Tooltip("Array di 10 target (index 0-9).")]
    public GameObject[] targets = new GameObject[10];
    public bool autoPopulateTargets = true;

    [Header("Input")]
#if ENABLE_INPUT_SYSTEM
    [Tooltip("Se assegnato, usa questa InputAction per toggle del menu.")]
    public InputActionReference toggleAction;
#endif
    [Tooltip("Fallback: nodo XR da cui leggere i pulsanti se non c'e' InputAction.")]
    public XRNode fallbackNode = XRNode.RightHand;
    public bool usePrimaryButton = true;
    public bool useMenuButton = true;
    [Tooltip("Fallback extra: utile per hand-tracking quando il pinch e' esposto come trigger.")]
    public bool useTriggerButton = true;
    [Tooltip("Fallback extra: utile per hand-tracking quando il pinch/grab e' esposto come grip.")]
    public bool useGripButton = false;
    [Range(0.1f, 0.95f)]
    [Tooltip("Soglia per input analogici trigger/grip se i bool non sono disponibili.")]
    public float analogPressThreshold = 0.65f;

    [Header("Left Anchor Follow")]
    [Tooltip("Se true, il menu segue dinamicamente l'anchor sinistro, privilegiando la mano quando disponibile.")]
    public bool followLeftAnchor = true;
    [Tooltip("Anchor controller sinistro (opzionale, auto-discovery se nullo).")]
    public Transform leftControllerAnchor;
    [Tooltip("Anchor mano sinistra (opzionale, auto-discovery se nullo).")]
    public Transform leftHandAnchor;
    [Tooltip("Se true prova ad auto-assegnare gli anchor cercando nomi contenenti left/controller/hand.")]
    public bool autoFindLeftAnchors = true;
    [Tooltip("Se gli anchor non sono assegnati, usa la pose XR node come fallback.")]
    public bool useXRNodePoseFallbackForFollow = true;
    [Tooltip("Converte le pose XR fallback (node/subsystem) da tracking-space a world-space.")]
    public bool convertFallbackPoseToWorld = true;
    [Tooltip("Override opzionale del tracking origin root usato per la conversione fallback.")]
    public Transform trackingOriginOverride;
    [Min(0.1f)]
    [Tooltip("Intervallo (s) per ri-cercare anchor sinistri quando diventano inattivi/non validi.")]
    public float anchorRediscoveryInterval = 0.5f;
    [Tooltip("Se true, il menu resta orientato verso la camera mentre segue la mano sinistra.")]
    public bool faceMainCameraWhileFollowing = true;
    [Tooltip("Se true, quando la mano sinistra e' disponibile ha priorita' sul controller anche se entrambi risultano tracciati.")]
    public bool preferTrackedHandOverController = true;
    [Min(0f)] public float followSmoothing = 20f;
    public Vector3 followPositionOffset = Vector3.zero;
    public Vector3 followEulerOffset = Vector3.zero;

    [Header("Pose Reliability")]
    [Tooltip("Se true, invalida anchor sinistri duplicati o chiaramente incoerenti prima di usarli.")]
    public bool invalidateDuplicateLeftAnchors = true;
    [Tooltip("In modalita' wrist menu usa prima la wrist pose reale da XR Hands e solo dopo eventuali fallback di anchor/controller.")]
    public bool prioritizeXRHandsSubsystemForWristPose = true;

    [Header("Behavior")]
    public bool startOpen = true;
    [Tooltip("Se true, il menu resta sempre aperto e ignora il toggle.")]
    public bool forceAlwaysOpen = true;
    [Tooltip("Se true, quando scegli una scena il pannello menu si chiude subito dopo l'attivazione.")]
    public bool closeMenuAfterSceneActivation = true;
    [Range(0.05f, 1.0f)]
    public float activationCooldown = 6.0f;
    public bool verboseLogs = true;
    public bool warnNullTargetsInEditorOnly = true;

    [Header("Editor Keyboard Skip")]
    [Tooltip("Abilita skip scene da tastiera (solo in Editor, Play Mode).")]
    public bool enableEditorKeyboardSkip = true;
    [Tooltip("Richiede che la Game View abbia focus per leggere i tasti.")]
    public bool requireGameViewFocusHint = true;

    [Header("World-Space Visibility Fix")]
    [Tooltip("Corregge automaticamente scala/raggio quando il menu e' un Canvas in World Space.")]
    public bool autoFixWorldSpaceMenu = true;
    [Tooltip("Scala locale target del Canvas world-space quando troppo grande.")]
    [Range(0.0005f, 0.02f)]
    public float targetCanvasLocalScale = 0.001f;
    [Tooltip("Raggio target in metri (distanza pulsanti dal centro del menu).")]
    [Range(0.05f, 0.5f)]
    public float targetWorldRadiusMeters = 0.12f;
    [Tooltip("Se la scala locale supera questa soglia, viene normalizzata automaticamente.")]
    [Range(0.01f, 1.0f)]
    public float maxCanvasScaleBeforeFix = 0.05f;

    [Header("Hand-Only Wrist Menu")]
    [Tooltip("Converte il radial in un pannello 2x5 attaccato al player con toggle sul polso sinistro.")]
    public bool handOnlyWristGridMode = true;
    [Tooltip("Mantiene il pannello menu sotto il player invece che sulla mano.")]
    public bool keepMenuAttachedToPlayer = true;
    public Vector3 playerMenuLocalPosition = new Vector3(-0.18f, 0.02f, 0.46f);
    public Vector3 playerMenuLocalEuler = new Vector3(8f, 18f, 0f);
    public Vector2 playerMenuCanvasSize = new Vector2(720f, 320f);
    [Range(1, 5)] public int gridColumns = 5;
    public Vector2 gridCellSize = new Vector2(126f, 88f);
    public Vector2 gridSpacing = new Vector2(16f, 16f);
    [Min(1)] public int gridLabelFontSize = 18;
    public Color menuPanelColor = new Color(0.06f, 0.09f, 0.12f, 0.94f);
    public Color menuButtonColor = new Color(0.17f, 0.22f, 0.28f, 0.97f);
    public Color menuButtonHighlightColor = new Color(0.24f, 0.38f, 0.48f, 1f);
    public Color menuButtonPressedColor = new Color(0.12f, 0.19f, 0.24f, 1f);
    [Min(10)] public int gridLabelMinFontSize = 14;
    [Min(0f)] public float menuLabelHorizontalPadding = 12f;
    [Min(0f)] public float menuLabelVerticalPadding = 10f;
    [Range(0.5f, 1f)] public float menuLabelWrapWidthRatio = 0.8f;
    public Color menuLabelColor = new Color(0.98f, 0.96f, 0.9f, 1f);
    public Color menuButtonBorderColor = new Color(0.77f, 0.66f, 0.45f, 0.55f);
    public Color menuButtonShadowColor = new Color(0f, 0f, 0f, 0.32f);
    public Color menuPanelBorderColor = new Color(0.77f, 0.66f, 0.45f, 0.38f);
    public Color menuPanelShadowColor = new Color(0f, 0f, 0f, 0.3f);
    public bool createMenuDragHandle = true;
    public Vector2 dragHandleSize = new Vector2(190f, 40f);
    public string dragHandleLabel = "SPOSTA";
    public Color dragHandleColor = new Color(0.15f, 0.43f, 0.55f, 0.98f);
    public Vector3 wristButtonLocalPosition = new Vector3(-0.045f, 0.035f, 0.03f);
    [Tooltip("Offset extra applicato al bottone polso per tenerlo un po' piu' staccato dalla mano.")]
    public Vector3 wristButtonExtraLocalOffset = new Vector3(-0.010f, 0.004f, 0.020f);
    public Vector3 wristButtonLocalEuler = new Vector3(8f, 190f, 76f);
    public Vector2 wristButtonCanvasSize = new Vector2(140f, 70f);
    [Range(0.0002f, 0.01f)] public float wristButtonCanvasScale = 0.00065f;
    public string wristButtonLabel = "MENU";
    public Color wristButtonColor = new Color(0.15f, 0.43f, 0.55f, 0.98f);
    [Tooltip("Padding extra del collider del bottone da polso per facilitarne l'interazione senza allargare troppo la grafica.")]
    public Vector2 wristButtonColliderPadding = new Vector2(20f, 12f);
    [Min(0.001f)] public float wristButtonColliderDepth = 14f;
    [Min(0.01f)] public float wristButtonPressDebounce = 0.08f;
    [Min(0.005f)] public float wristButtonPinchPressDistance = 0.04f;
    [Min(0.005f)] public float wristButtonPinchReleaseDistance = 0.055f;
    [Min(0f)] public float wristButtonPinchTargetPadding = 0.055f;
    [Tooltip("Se true il bottone da polso ruota per guardare la camera. Di default resta orientato dal polso per sembrare davvero attaccato alla mano.")]
    public bool wristButtonFaceCamera = false;

    [Header("Reachability Tuning")]
    [Tooltip("Quando il pannello si apre lo riposiziona davanti alla testa, invece di lasciarlo solo nella posa locale del rig.")]
    public bool snapMenuNearHeadOnOpen = true;
    [Tooltip("Offset del pannello rispetto alla testa usando solo lo yaw della camera: X laterale, Y verticale, Z frontale.")]
    public Vector3 headRelativeMenuOffset = new Vector3(0.18f, -0.10f, 0.34f);
    [Tooltip("Quando true, il pannello viene ruotato per guardare il visore quando viene aperto.")]
    public bool faceHeadWhenOpeningMenu = true;
    [Min(0.005f)] public float dragPinchGrabDistance = 0.055f;
    [Min(0.005f)] public float dragPinchReleaseDistance = 0.08f;
    [Min(0f)] public float dragPinchTargetPadding = 0.12f;
    [Min(0f)] public float dragTopBandWorldPadding = 0.12f;
    [Min(0.1f)] public float dragMinHeadDistance = 0.18f;
    [Min(0.2f)] public float dragMaxHeadDistance = 0.68f;

    [Header("Optional Modal / Blocker")]
    public CanvasGroup menuCanvasGroup;
    [Tooltip("Componenti da disabilitare quando il menu e' aperto (es. XRRayInteractor del mondo).")]
    public Behaviour[] disableWhileOpen;

    [Header("XR UI Safety")]
    [Tooltip("Se true, assicura runtime setup UI XR (InputModule + raycaster world-space).")]
    public bool ensureXRUIRuntimeSetup = true;

    [Header("Hierarchy Safety")]
    [Tooltip("Se true, al risveglio sgancia il menu da parent hand/controller dinamici per evitare sparizioni in hand-only.")]
    public bool detachFromDynamicParentOnAwake = true;

    private bool _menuOpen;
    private float _lastToggleTime;
    private float _lastActivateTime;
    private bool _prevPrimary;
    private bool _prevMenu;
    private bool _prevTrigger;
    private bool _prevGrip;
    private float _nextAnchorRediscoveryTime;
    private Transform _preferredLeftWristAnchor;

    private VirtualSceneMenuButton[] _cachedButtons;
    private static readonly System.Collections.Generic.List<UnityEngine.XR.InputDevice> s_ControllerDevices =
        new System.Collections.Generic.List<UnityEngine.XR.InputDevice>(8);
#if UNITY_XR_HANDS
    private static readonly System.Collections.Generic.List<XRHandSubsystem> s_HandSubsystems =
        new System.Collections.Generic.List<XRHandSubsystem>(4);
#endif

#if ENABLE_INPUT_SYSTEM
    private InputAction _toggleAction;
#endif
    private RectTransform _gridContainerRect;
    private RectTransform _wristToggleCanvasRect;

    private void Awake()
    {
        EnsureArraySize();
        AutoPopulateTargetsIfNeeded();
        NormalizeLeftAnchorsIfNeeded();

        if (menuRoot == null)
            menuRoot = gameObject;

        DetachFromDynamicParentIfNeeded();
        TryAutoFindLeftAnchors();

        if (menuCanvasGroup == null && menuRoot != null)
            menuCanvasGroup = menuRoot.GetComponent<CanvasGroup>();

        EnsureXRUISetupIfNeeded();
        ConfigureHandOnlyWristMenuIfNeeded();
        AutoFixWorldSpaceMenuIfNeeded();
        CacheButtons();
        ApplyButtonStates();
        SetMenuOpen(startOpen, true);
    }

    private void OnEnable()
    {
        BindAction();
        BindSceneGroupManagerEvents();
    }

    private void OnDisable()
    {
        UnbindAction();
        UnbindSceneGroupManagerEvents();
    }

    private void Update()
    {
        UpdateFollowAnchor();
        UpdateWristTogglePose();
        HandleEditorKeyboardSkip();

        if (HasToggleAction())
            return;

        PollXRButtons();
    }

    public void ToggleMenu()
    {
        if (forceAlwaysOpen)
        {
            SetMenuOpen(true, false);
            return;
        }

        if (Time.unscaledTime - _lastToggleTime < activationCooldown)
            return;

        _lastToggleTime = Time.unscaledTime;
        SetMenuOpen(!_menuOpen, false);
    }

    public void ActivateIndex(int i)
    {
        int index = NormalizeIndex(i);
        if (index < 0 || index >= targets.Length)
        {
            if (verboseLogs)
                Debug.LogWarning($"[HandRadialVirtualSceneMenu] ActivateIndex: index fuori range ({i}).");
            return;
        }

        if (Time.unscaledTime - _lastActivateTime < activationCooldown)
            return;

        _lastActivateTime = Time.unscaledTime;

        if (sceneGroupManager == null)
        {
            Debug.LogWarning("[HandRadialVirtualSceneMenu] SceneGroupManager non assegnato.");
            return;
        }

        GameObject target = targets[index];
        if (target == null)
        {
            WarnNullTarget(index);
            return;
        }

        sceneGroupManager.ActivateScene(target);

        if (closeMenuAfterSceneActivation)
            SetMenuOpen(false, false);

        if (verboseLogs)
            Debug.Log($"[HandRadialVirtualSceneMenu] ActivateIndex -> {index + 1} ({target.name})");
    }

    public void RefreshButtons()
    {
        CacheButtons();
        ApplyButtonStates();
    }

    private void BindSceneGroupManagerEvents()
    {
        if (sceneGroupManager == null)
            sceneGroupManager = FindFirstObjectByType<SceneGroupManager>();

        if (sceneGroupManager == null)
            return;

        sceneGroupManager.SceneActivated -= HandleSceneActivated;
        sceneGroupManager.SceneActivated += HandleSceneActivated;
    }

    private void UnbindSceneGroupManagerEvents()
    {
        if (sceneGroupManager == null)
            return;

        sceneGroupManager.SceneActivated -= HandleSceneActivated;
    }

    private void HandleSceneActivated(SceneGroupManager.VirtualScene _)
    {
        if (!closeMenuAfterSceneActivation || forceAlwaysOpen || !_menuOpen)
            return;

        SetMenuOpen(false, false);
    }

    private void SetMenuOpen(bool open, bool instant)
    {
        if (forceAlwaysOpen)
            open = true;

        _menuOpen = open;

        if (menuRoot != null)
            menuRoot.SetActive(open);

        if (open)
            SnapMenuNearHeadIfNeeded();

        if (menuCanvasGroup != null)
        {
            menuCanvasGroup.interactable = open;
            menuCanvasGroup.blocksRaycasts = open;
        }

        if (disableWhileOpen != null)
        {
            for (int i = 0; i < disableWhileOpen.Length; i++)
            {
                if (disableWhileOpen[i] != null)
                    disableWhileOpen[i].enabled = !open;
            }
        }

        if (verboseLogs && !instant)
            Debug.Log($"[HandRadialVirtualSceneMenu] Menu {(open ? "OPEN" : "CLOSE")}");
    }

    private void PollXRButtons()
    {
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(fallbackNode);
        if (!device.isValid)
            return;

        if (usePrimaryButton)
        {
            bool primary;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out primary))
            {
                if (primary && !_prevPrimary)
                    ToggleMenu();
                _prevPrimary = primary;
            }
        }

        if (useMenuButton)
        {
            bool menu;
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out menu))
            {
                if (menu && !_prevMenu)
                    ToggleMenu();
                _prevMenu = menu;
            }
        }

        if (useTriggerButton)
        {
            bool triggerPressed = false;
            bool hasTrigger = false;

            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool triggerBool))
            {
                triggerPressed = triggerBool;
                hasTrigger = true;
            }
            else if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float triggerAxis))
            {
                triggerPressed = triggerAxis >= analogPressThreshold;
                hasTrigger = true;
            }

            if (hasTrigger)
            {
                if (triggerPressed && !_prevTrigger)
                    ToggleMenu();
                _prevTrigger = triggerPressed;
            }
        }

        if (useGripButton)
        {
            bool gripPressed = false;
            bool hasGrip = false;

            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool gripBool))
            {
                gripPressed = gripBool;
                hasGrip = true;
            }
            else if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float gripAxis))
            {
                gripPressed = gripAxis >= analogPressThreshold;
                hasGrip = true;
            }

            if (hasGrip)
            {
                if (gripPressed && !_prevGrip)
                    ToggleMenu();
                _prevGrip = gripPressed;
            }
        }
    }

    private void HandleEditorKeyboardSkip()
    {
#if UNITY_EDITOR
        if (!enableEditorKeyboardSkip || !Application.isPlaying)
            return;

        int numeric = ReadEditorNumericIndex();
        if (numeric >= 0)
        {
            ActivateIndex(numeric);
            return;
        }

        if (IsKeyDownCompat(KeyCode.Home))
        {
            ActivateIndex(0);
            return;
        }

        if (IsKeyDownCompat(KeyCode.RightArrow) || IsKeyDownCompat(KeyCode.PageDown))
        {
            ActivateRelative(+1);
            return;
        }

        if (IsKeyDownCompat(KeyCode.LeftArrow) || IsKeyDownCompat(KeyCode.PageUp))
            ActivateRelative(-1);
#endif
    }

    private void ActivateRelative(int delta)
    {
        if (sceneGroupManager == null || sceneGroupManager.scenes == null || sceneGroupManager.scenes.Count == 0)
            return;

        int count = sceneGroupManager.scenes.Count;
        int current = FindCurrentActiveSceneIndex();
        int next = current < 0 ? 0 : (current + delta + count) % count;
        ActivateIndex(next);
    }

    private int FindCurrentActiveSceneIndex()
    {
        if (sceneGroupManager == null || sceneGroupManager.scenes == null)
            return -1;

        for (int i = 0; i < sceneGroupManager.scenes.Count; i++)
        {
            var cfg = sceneGroupManager.scenes[i];
            if (cfg != null && cfg.root != null && cfg.root.activeInHierarchy)
                return i;
        }

        return -1;
    }

    private static int ReadEditorNumericIndex()
    {
        if (IsKeyDownCompat(KeyCode.Alpha1) || IsKeyDownCompat(KeyCode.Keypad1)) return 0;
        if (IsKeyDownCompat(KeyCode.Alpha2) || IsKeyDownCompat(KeyCode.Keypad2)) return 1;
        if (IsKeyDownCompat(KeyCode.Alpha3) || IsKeyDownCompat(KeyCode.Keypad3)) return 2;
        if (IsKeyDownCompat(KeyCode.Alpha4) || IsKeyDownCompat(KeyCode.Keypad4)) return 3;
        if (IsKeyDownCompat(KeyCode.Alpha5) || IsKeyDownCompat(KeyCode.Keypad5)) return 4;
        if (IsKeyDownCompat(KeyCode.Alpha6) || IsKeyDownCompat(KeyCode.Keypad6)) return 5;
        if (IsKeyDownCompat(KeyCode.Alpha7) || IsKeyDownCompat(KeyCode.Keypad7)) return 6;
        if (IsKeyDownCompat(KeyCode.Alpha8) || IsKeyDownCompat(KeyCode.Keypad8)) return 7;
        if (IsKeyDownCompat(KeyCode.Alpha9) || IsKeyDownCompat(KeyCode.Keypad9)) return 8;
        if (IsKeyDownCompat(KeyCode.Alpha0) || IsKeyDownCompat(KeyCode.Keypad0)) return 9;
        return -1;
    }

    private static bool IsKeyDownCompat(KeyCode key)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(key))
            return true;
#endif
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null)
            return false;

        switch (key)
        {
            case KeyCode.Alpha0: return Keyboard.current.digit0Key.wasPressedThisFrame;
            case KeyCode.Alpha1: return Keyboard.current.digit1Key.wasPressedThisFrame;
            case KeyCode.Alpha2: return Keyboard.current.digit2Key.wasPressedThisFrame;
            case KeyCode.Alpha3: return Keyboard.current.digit3Key.wasPressedThisFrame;
            case KeyCode.Alpha4: return Keyboard.current.digit4Key.wasPressedThisFrame;
            case KeyCode.Alpha5: return Keyboard.current.digit5Key.wasPressedThisFrame;
            case KeyCode.Alpha6: return Keyboard.current.digit6Key.wasPressedThisFrame;
            case KeyCode.Alpha7: return Keyboard.current.digit7Key.wasPressedThisFrame;
            case KeyCode.Alpha8: return Keyboard.current.digit8Key.wasPressedThisFrame;
            case KeyCode.Alpha9: return Keyboard.current.digit9Key.wasPressedThisFrame;
            case KeyCode.Keypad0: return Keyboard.current.numpad0Key.wasPressedThisFrame;
            case KeyCode.Keypad1: return Keyboard.current.numpad1Key.wasPressedThisFrame;
            case KeyCode.Keypad2: return Keyboard.current.numpad2Key.wasPressedThisFrame;
            case KeyCode.Keypad3: return Keyboard.current.numpad3Key.wasPressedThisFrame;
            case KeyCode.Keypad4: return Keyboard.current.numpad4Key.wasPressedThisFrame;
            case KeyCode.Keypad5: return Keyboard.current.numpad5Key.wasPressedThisFrame;
            case KeyCode.Keypad6: return Keyboard.current.numpad6Key.wasPressedThisFrame;
            case KeyCode.Keypad7: return Keyboard.current.numpad7Key.wasPressedThisFrame;
            case KeyCode.Keypad8: return Keyboard.current.numpad8Key.wasPressedThisFrame;
            case KeyCode.Keypad9: return Keyboard.current.numpad9Key.wasPressedThisFrame;
            case KeyCode.RightArrow: return Keyboard.current.rightArrowKey.wasPressedThisFrame;
            case KeyCode.LeftArrow: return Keyboard.current.leftArrowKey.wasPressedThisFrame;
            case KeyCode.PageUp: return Keyboard.current.pageUpKey.wasPressedThisFrame;
            case KeyCode.PageDown: return Keyboard.current.pageDownKey.wasPressedThisFrame;
            case KeyCode.Home: return Keyboard.current.homeKey.wasPressedThisFrame;
        }
#endif
        return false;
    }

#if ENABLE_INPUT_SYSTEM
    private void BindAction()
    {
        if (toggleAction == null || toggleAction.action == null)
            return;

        _toggleAction = toggleAction.action;
        _toggleAction.performed += OnToggleAction;
        _toggleAction.Enable();
    }

    private void UnbindAction()
    {
        if (_toggleAction == null)
            return;

        _toggleAction.performed -= OnToggleAction;
        _toggleAction = null;
    }

    private void OnToggleAction(InputAction.CallbackContext ctx)
    {
        ToggleMenu();
    }
#endif

    private bool HasToggleAction()
    {
#if ENABLE_INPUT_SYSTEM
        return toggleAction != null && toggleAction.action != null;
#else
        return false;
#endif
    }

    private int NormalizeIndex(int i)
    {
        if (i >= 1 && i <= 10)
            return i - 1;
        return i;
    }

    private void CacheButtons()
    {
        if (menuRoot == null)
        {
            _cachedButtons = null;
            return;
        }

        _cachedButtons = menuRoot.GetComponentsInChildren<VirtualSceneMenuButton>(true);
        if (_cachedButtons == null)
            return;

        for (int i = 0; i < _cachedButtons.Length; i++)
        {
            if (_cachedButtons[i] != null)
            {
                _cachedButtons[i].SetMenuIfMissing(this);
                _cachedButtons[i].SetTrackingOrigin(ResolveTrackingOrigin());
            }
        }
    }

    private void ApplyButtonStates()
    {
        if (_cachedButtons == null)
            return;

        for (int i = 0; i < _cachedButtons.Length; i++)
        {
            var b = _cachedButtons[i];
            if (b == null) continue;

            int idx = b.ZeroBasedIndex;
            bool valid = idx >= 0 && idx < targets.Length && targets[idx] != null;
            b.SetInteractable(valid);

            if (!valid)
                WarnNullTarget(idx);
        }
    }

    private void WarnNullTarget(int index)
    {
#if UNITY_EDITOR
        if (warnNullTargetsInEditorOnly)
            Debug.LogWarning($"[HandRadialVirtualSceneMenu] Target null per index {index + 1}.");
#else
        if (!warnNullTargetsInEditorOnly)
            Debug.LogWarning($"[HandRadialVirtualSceneMenu] Target null per index {index + 1}.");
#endif
    }

    private void EnsureArraySize()
    {
        if (targets == null || targets.Length != 10)
        {
            var newArr = new GameObject[10];
            if (targets != null)
            {
                int copy = Mathf.Min(targets.Length, newArr.Length);
                for (int i = 0; i < copy; i++)
                    newArr[i] = targets[i];
            }
            targets = newArr;
        }
    }

    private void AutoPopulateTargetsIfNeeded()
    {
        if (!autoPopulateTargets)
            return;

        if (sceneGroupManager == null || sceneGroupManager.scenes == null)
            return;

        int count = Mathf.Min(sceneGroupManager.scenes.Count, targets.Length);
        for (int i = 0; i < count; i++)
        {
            var cfg = sceneGroupManager.scenes[i];
            if (cfg != null && cfg.root != null)
                targets[i] = cfg.root;
        }
    }

    private void OnValidate()
    {
        EnsureArraySize();
        AutoPopulateTargetsIfNeeded();
        TryAutoFindLeftAnchors();
        AutoFixWorldSpaceMenuIfNeeded();
        CacheButtons();
        ApplyButtonStates();
    }

    private enum LeftAnchorKind
    {
        Controller,
        Hand
    }

    private void DetachFromDynamicParentIfNeeded()
    {
        if (!detachFromDynamicParentOnAwake)
            return;

        Transform parent = transform.parent;
        if (parent == null)
            return;

        string parentPathLower = GetTransformPathLower(parent, 10);
        bool looksDynamicHandOrControllerParent =
            parentPathLower.Contains("left hand")
            || parentPathLower.Contains("right hand")
            || parentPathLower.Contains("left controller")
            || parentPathLower.Contains("right controller")
            || parentPathLower.Contains("/left/")
            || parentPathLower.Contains("/right/");

        if (!looksDynamicHandOrControllerParent)
            return;

        transform.SetParent(null, true);

        if (verboseLogs)
            Debug.Log($"[HandRadialVirtualSceneMenu] Reparented from dynamic branch '{parentPathLower}'.");
    }

    private void UpdateFollowAnchor()
    {
        if (!followLeftAnchor)
            return;

        EnsureLeftAnchorsUpToDate();

        if (preferTrackedHandOverController)
        {
            if (TryGetBestTrackedLeftHandPose(out Vector3 handPos, out Quaternion handRot))
            {
                ApplyFollowPose(handPos, handRot);
                return;
            }

            if (TryGetBestTrackedLeftControllerPose(out Vector3 controllerPos, out Quaternion controllerRot))
            {
                ApplyFollowPose(controllerPos, controllerRot);
                return;
            }
        }
        else
        {
            if (TryGetBestTrackedLeftControllerPose(out Vector3 controllerPos, out Quaternion controllerRot))
            {
                ApplyFollowPose(controllerPos, controllerRot);
                return;
            }

            if (TryGetBestTrackedLeftHandPose(out Vector3 handPos, out Quaternion handRot))
                ApplyFollowPose(handPos, handRot);
        }
    }

    private void ApplyFallbackPose(Vector3 position, Quaternion rotation)
    {
        ConvertFallbackPoseToWorldIfNeeded(ref position, ref rotation);
        ApplyFollowPose(position, rotation);
    }

    private void ApplyFollowPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        Quaternion positionReferenceRotation = worldRotation;
        Quaternion visualRotation = worldRotation;
        if (faceMainCameraWhileFollowing && Camera.main != null)
        {
            Vector3 toCamera = Camera.main.transform.position - worldPosition;
            if (toCamera.sqrMagnitude > 0.000001f)
                visualRotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        }

        Quaternion desiredRotation = visualRotation * Quaternion.Euler(followEulerOffset);
        Vector3 desiredPosition = worldPosition + (positionReferenceRotation * followPositionOffset);

        if (!Application.isPlaying || followSmoothing <= 0f)
        {
            transform.SetPositionAndRotation(desiredPosition, desiredRotation);
            return;
        }

        float t = 1f - Mathf.Exp(-followSmoothing * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, t);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, t);
    }

    private void ConfigureHandOnlyWristMenuIfNeeded()
    {
        if (!handOnlyWristGridMode || menuRoot == null)
            return;

        usePrimaryButton = false;
        useMenuButton = false;
        useTriggerButton = false;
        useGripButton = false;
        followLeftAnchor = false;
        faceMainCameraWhileFollowing = false;
        forceAlwaysOpen = false;
        startOpen = false;
        activationCooldown = Mathf.Clamp(activationCooldown, 0.08f, 0.24f);
        targetCanvasLocalScale = Mathf.Clamp(targetCanvasLocalScale, 0.0008f, 0.0016f);
        wristButtonCanvasSize = new Vector2(Mathf.Max(wristButtonCanvasSize.x, 156f), Mathf.Max(wristButtonCanvasSize.y, 82f));
        wristButtonColliderPadding = new Vector2(Mathf.Max(wristButtonColliderPadding.x, 34f), Mathf.Max(wristButtonColliderPadding.y, 20f));
        wristButtonPinchPressDistance = Mathf.Max(wristButtonPinchPressDistance, 0.05f);
        wristButtonPinchReleaseDistance = Mathf.Max(wristButtonPinchReleaseDistance, wristButtonPinchPressDistance + 0.015f);
        wristButtonPinchTargetPadding = Mathf.Max(wristButtonPinchTargetPadding, 0.075f);

        EnsureMenuAttachedToPlayer();
        EnsureMenuPanelVisuals();
        ConfigureButtonsAsGrid();
        RectTransform menuRect = menuRoot.transform as RectTransform;
        Vector2 dragSurfaceTopBandSize = createMenuDragHandle ? GetEffectiveDragHandleSize() : Vector2.zero;

        SetMenuDragHandleVisible(createMenuDragHandle, dragSurfaceTopBandSize);
        if (menuRect != null)
            EnsureMenuFrameDragSurface(menuRect, dragSurfaceTopBandSize);

        EnsureWristToggleButton();
        UpdateWristTogglePose(forceInstant: true);
    }

    private void EnsureMenuAttachedToPlayer()
    {
        Transform playerAnchor = ResolvePlayerAttachAnchor();
        if (keepMenuAttachedToPlayer && playerAnchor != null && transform.parent != playerAnchor)
            transform.SetParent(playerAnchor, false);

        if (keepMenuAttachedToPlayer)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        RectTransform menuRect = menuRoot.transform as RectTransform;
        if (menuRect == null)
            return;

        if (menuRect.parent != transform)
            menuRect.SetParent(transform, false);

        menuRect.anchorMin = new Vector2(0.5f, 0.5f);
        menuRect.anchorMax = new Vector2(0.5f, 0.5f);
        menuRect.pivot = new Vector2(0.5f, 0.5f);
        menuRect.sizeDelta = playerMenuCanvasSize;
        menuRect.localPosition = playerMenuLocalPosition;
        menuRect.localRotation = Quaternion.Euler(playerMenuLocalEuler);
    }

    private Transform ResolvePlayerAttachAnchor()
    {
        if (sceneGroupManager != null && sceneGroupManager.player != null)
            return sceneGroupManager.player;

        if (transform.parent != null)
            return transform.parent;

        return transform;
    }

    private void EnsureMenuPanelVisuals()
    {
        Canvas canvas = menuRoot.GetComponent<Canvas>();
        if (canvas == null)
            canvas = menuRoot.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.WorldSpace;

        if (menuRoot.GetComponent<CanvasScaler>() == null)
            menuRoot.AddComponent<CanvasScaler>();

        Image panelImage = menuRoot.GetComponent<Image>();
        if (panelImage == null)
            panelImage = menuRoot.AddComponent<Image>();

        panelImage.color = menuPanelColor;
        ApplyPanelVisualStyle(panelImage);

        if (menuCanvasGroup == null)
            menuCanvasGroup = menuRoot.GetComponent<CanvasGroup>();

        if (menuCanvasGroup == null)
            menuCanvasGroup = menuRoot.AddComponent<CanvasGroup>();
    }

    private void ConfigureButtonsAsGrid()
    {
        _gridContainerRect = EnsureGridContainerRect();
        if (_gridContainerRect == null)
            return;

        RectTransform menuRect = menuRoot.transform as RectTransform;
        if (menuRect != null)
            menuRect.sizeDelta = playerMenuCanvasSize;

        float topInset = createMenuDragHandle
            ? Mathf.Max(22f, GetEffectiveDragHandleSize().y + 18f)
            : Mathf.Max(22f, GetFrameEdgeThickness(playerMenuCanvasSize) + 4f);

        _gridContainerRect.anchorMin = Vector2.zero;
        _gridContainerRect.anchorMax = Vector2.one;
        _gridContainerRect.offsetMin = new Vector2(22f, 22f);
        _gridContainerRect.offsetMax = new Vector2(-22f, -topInset);
        _gridContainerRect.localScale = Vector3.one;
        _gridContainerRect.localRotation = Quaternion.identity;

        RadialLayout radial = _gridContainerRect.GetComponent<RadialLayout>();
        if (radial != null)
            radial.enabled = false;

        GridLayoutGroup grid = _gridContainerRect.GetComponent<GridLayoutGroup>();
        if (grid == null)
            grid = _gridContainerRect.gameObject.AddComponent<GridLayoutGroup>();

        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, gridColumns);
        grid.cellSize = gridCellSize;
        grid.spacing = gridSpacing;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperLeft;
        grid.padding = new RectOffset(0, 0, 0, 0);

        ContentSizeFitter fitter = _gridContainerRect.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            fitter.enabled = false;

        CacheButtons();
        if (_cachedButtons == null)
            return;

        for (int i = 0; i < _cachedButtons.Length; i++)
        {
            VirtualSceneMenuButton button = _cachedButtons[i];
            if (button == null)
                continue;

            RectTransform buttonRect = button.transform as RectTransform;
            if (buttonRect == null)
                continue;

            buttonRect.SetParent(_gridContainerRect, false);
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = gridCellSize;
            buttonRect.localScale = Vector3.one;
            buttonRect.localRotation = Quaternion.identity;
            button.transform.SetSiblingIndex(Mathf.Clamp(button.index - 1, 0, _cachedButtons.Length - 1));

            StyleMenuButton(button);
        }
    }

    private Vector2 GetEffectiveDragHandleSize()
    {
        Vector2 effectiveHandleSize = dragHandleSize;
        if (handOnlyWristGridMode)
        {
            effectiveHandleSize.x = Mathf.Max(dragHandleSize.x, playerMenuCanvasSize.x - 44f);
            effectiveHandleSize.y = Mathf.Max(dragHandleSize.y, 54f);
        }

        return effectiveHandleSize;
    }

    private static float GetFrameEdgeThickness(Vector2 menuSize)
    {
        return Mathf.Clamp(Mathf.Min(menuSize.x, menuSize.y) * 0.08f, 18f, 30f);
    }

    private void SetMenuDragHandleVisible(bool visible, Vector2 effectiveHandleSize)
    {
        Transform existing = menuRoot.transform.Find("DragHandle");
        if (!visible)
        {
            if (existing != null)
                existing.gameObject.SetActive(false);
            return;
        }

        EnsureMenuDragHandle(effectiveHandleSize);
    }

    private RectTransform EnsureGridContainerRect()
    {
        if (_gridContainerRect != null)
            return _gridContainerRect;

        Transform existing = menuRoot.transform.Find("ButtonsContainer");
        if (existing == null)
        {
            VirtualSceneMenuButton firstButton = menuRoot.GetComponentInChildren<VirtualSceneMenuButton>(true);
            if (firstButton != null && firstButton.transform.parent is RectTransform parentRect)
                existing = parentRect;
        }

        if (existing == null)
        {
            GameObject container = new GameObject("ButtonsContainer", typeof(RectTransform));
            RectTransform rect = container.GetComponent<RectTransform>();
            rect.SetParent(menuRoot.transform, false);
            existing = rect;
        }

        _gridContainerRect = existing as RectTransform;
        return _gridContainerRect;
    }

    private void StyleMenuButton(VirtualSceneMenuButton sceneButton)
    {
        if (sceneButton == null)
            return;

        Button button = sceneButton.GetComponent<Button>();
        if (button != null)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = menuButtonColor;
            colors.highlightedColor = menuButtonHighlightColor;
            colors.selectedColor = menuButtonHighlightColor;
            colors.pressedColor = menuButtonPressedColor;
            colors.disabledColor = new Color(0.18f, 0.18f, 0.18f, 0.45f);
            button.colors = colors;
        }

        Image buttonImage = sceneButton.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = menuButtonColor;
            ApplySurfaceGraphicStyle(buttonImage, menuButtonBorderColor, menuButtonShadowColor, new Vector2(1.5f, -1.5f), new Vector2(0f, -5f));
        }

        BoxCollider collider = sceneButton.GetComponent<BoxCollider>();
        if (collider != null)
        {
            collider.center = Vector3.zero;
            collider.size = new Vector3(gridCellSize.x, gridCellSize.y, 10f);
            collider.isTrigger = true;
        }

        sceneButton.pressDebounceSeconds = Mathf.Clamp(sceneButton.pressDebounceSeconds, 0.06f, 0.14f);
        sceneButton.pinchPressDistance = Mathf.Max(sceneButton.pinchPressDistance, 0.032f);
        sceneButton.pinchReleaseDistance = Mathf.Max(sceneButton.pinchReleaseDistance, sceneButton.pinchPressDistance + 0.012f);
        sceneButton.pinchTargetPadding = Mathf.Max(sceneButton.pinchTargetPadding, 0.035f);
        sceneButton.colliderPadding = new Vector2(Mathf.Max(sceneButton.colliderPadding.x, 20f), Mathf.Max(sceneButton.colliderPadding.y, 16f));
        sceneButton.colliderDepth = Mathf.Max(sceneButton.colliderDepth, 12f);
        sceneButton.enableHandHoverFallback = true;
        sceneButton.handHoverActivationDelay = Mathf.Clamp(sceneButton.handHoverActivationDelay, 0.08f, 0.16f);
        sceneButton.RefreshInteractableShape();

        if (sceneButton.uiText != null)
        {
            float labelWrapWidth = GetMenuLabelWrapWidth();
            sceneButton.ConfigureWordSafeLabel(labelWrapWidth, gridLabelFontSize, gridLabelMinFontSize);
            ApplyWordSafeLegacyLabel(
                sceneButton.uiText,
                GetMenuButtonRawLabel(sceneButton),
                gridLabelFontSize,
                gridLabelMinFontSize,
                labelWrapWidth);
            sceneButton.uiText.alignment = TextAnchor.MiddleCenter;
            sceneButton.uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            sceneButton.uiText.verticalOverflow = VerticalWrapMode.Overflow;
            sceneButton.uiText.resizeTextForBestFit = false;
            sceneButton.uiText.color = menuLabelColor;
            sceneButton.uiText.fontStyle = FontStyle.Bold;
            ApplyTextRectPadding(sceneButton.uiText.rectTransform, menuLabelHorizontalPadding, menuLabelVerticalPadding);
            ApplyLabelShadow(sceneButton.uiText);
        }

        if (sceneButton.tmpText != null)
        {
            float labelWrapWidth = GetMenuLabelWrapWidth();
            sceneButton.ConfigureWordSafeLabel(labelWrapWidth, gridLabelFontSize, gridLabelMinFontSize);
            ApplyWordSafeTmpLabel(
                sceneButton.tmpText,
                GetMenuButtonRawLabel(sceneButton),
                gridLabelFontSize,
                gridLabelMinFontSize,
                labelWrapWidth);
            sceneButton.tmpText.alignment = TextAlignmentOptions.CenterGeoAligned;
            sceneButton.tmpText.color = menuLabelColor;
            ApplyTextRectPadding(sceneButton.tmpText.rectTransform, menuLabelHorizontalPadding, menuLabelVerticalPadding);
            ApplyLabelShadow(sceneButton.tmpText);
        }
    }

    private void EnsureMenuDragHandle(Vector2 effectiveHandleSize)
    {
        RectTransform menuRect = menuRoot.transform as RectTransform;
        if (menuRect == null)
            return;

        Transform existing = menuRoot.transform.Find("DragHandle");
        RectTransform handleRect;
        if (existing == null)
        {
            GameObject go = new GameObject("DragHandle", typeof(RectTransform), typeof(Image));
            handleRect = go.GetComponent<RectTransform>();
            handleRect.SetParent(menuRoot.transform, false);
        }
        else
        {
            handleRect = existing as RectTransform;
        }

        handleRect.gameObject.SetActive(true);
        handleRect.anchorMin = new Vector2(0.5f, 1f);
        handleRect.anchorMax = new Vector2(0.5f, 1f);
        handleRect.pivot = new Vector2(0.5f, 1f);
        handleRect.anchoredPosition = new Vector2(0f, -6f);
        handleRect.sizeDelta = effectiveHandleSize;
        handleRect.localScale = Vector3.one;

        Image image = handleRect.GetComponent<Image>();
        if (image == null)
            image = handleRect.gameObject.AddComponent<Image>();
        image.color = dragHandleColor;

        Text label = GetOrCreateLegacyText(handleRect, "DragHandleLabel");
        if (label != null)
        {
            label.text = dragHandleLabel;
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 18;
            label.color = Color.white;
        }
    }

    private void EnsureMenuFrameDragSurface(RectTransform menuRect, Vector2 topBandSize)
    {
        Transform existing = menuRoot.transform.Find("FrameDragSurface");
        RectTransform frameRect;
        if (existing == null)
        {
            GameObject go = new GameObject(
                "FrameDragSurface",
                typeof(RectTransform),
                typeof(Image),
                typeof(XRSimpleInteractable),
                typeof(WorldSpacePanelDragHandle),
                typeof(BoxCollider),
                typeof(BoxCollider),
                typeof(BoxCollider),
                typeof(BoxCollider));
            frameRect = go.GetComponent<RectTransform>();
            frameRect.SetParent(menuRoot.transform, false);
        }
        else
        {
            frameRect = existing as RectTransform;
        }

        if (frameRect == null)
            return;

        frameRect.anchorMin = Vector2.zero;
        frameRect.anchorMax = Vector2.one;
        frameRect.offsetMin = Vector2.zero;
        frameRect.offsetMax = Vector2.zero;
        frameRect.pivot = new Vector2(0.5f, 0.5f);
        frameRect.localScale = Vector3.one;
        frameRect.localRotation = Quaternion.identity;
        frameRect.SetAsFirstSibling();

        Image frameImage = frameRect.GetComponent<Image>();
        if (frameImage == null)
            frameImage = frameRect.gameObject.AddComponent<Image>();
        frameImage.color = new Color(1f, 1f, 1f, 0f);
        frameImage.raycastTarget = true;

        Rect rect = menuRect.rect;
        float sideThickness = GetFrameEdgeThickness(rect.size);
        float topThickness = Mathf.Max(sideThickness, topBandSize.y + 10f);
        float bottomThickness = sideThickness;
        float sideHeight = Mathf.Max(24f, rect.height - topThickness - bottomThickness);
        float sideCenterY = rect.yMin + bottomThickness + (sideHeight * 0.5f);
        float colliderDepth = 16f;

        BoxCollider[] colliders = EnsureBoxColliderCount(frameRect.gameObject, 4);
        XRBaseInteractable interactable = frameRect.GetComponent<XRBaseInteractable>();

        colliders[0].isTrigger = true;
        colliders[0].center = new Vector3(rect.center.x, rect.yMax - (topThickness * 0.5f), 0f);
        colliders[0].size = new Vector3(rect.width, topThickness, colliderDepth);

        colliders[1].isTrigger = true;
        colliders[1].center = new Vector3(rect.center.x, rect.yMin + (bottomThickness * 0.5f), 0f);
        colliders[1].size = new Vector3(rect.width, bottomThickness, colliderDepth);

        colliders[2].isTrigger = true;
        colliders[2].center = new Vector3(rect.xMin + (sideThickness * 0.5f), sideCenterY, 0f);
        colliders[2].size = new Vector3(sideThickness, sideHeight, colliderDepth);

        colliders[3].isTrigger = true;
        colliders[3].center = new Vector3(rect.xMax - (sideThickness * 0.5f), sideCenterY, 0f);
        colliders[3].size = new Vector3(sideThickness, sideHeight, colliderDepth);

        if (interactable != null)
        {
            interactable.colliders.Clear();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].enabled)
                    interactable.colliders.Add(colliders[i]);
            }

            interactable.distanceCalculationMode = XRBaseInteractable.DistanceCalculationMode.ColliderVolume;
        }

        WorldSpacePanelDragHandle dragHandle = frameRect.GetComponent<WorldSpacePanelDragHandle>();
        if (dragHandle == null)
            dragHandle = frameRect.gameObject.AddComponent<WorldSpacePanelDragHandle>();

        dragHandle.panelRoot = menuRoot.transform;
        dragHandle.panelRect = menuRect;
        dragHandle.playerRoot = ResolvePlayerAttachAnchor();
        dragHandle.trackingOrigin = ResolveTrackingOrigin();
        dragHandle.useXRHandsPinchDrag = true;
        dragHandle.useRectTransformBoundsAsHandle = false;
        dragHandle.allowLeftHandPinch = true;
        dragHandle.allowRightHandPinch = true;
        dragHandle.minHeadDistance = Mathf.Max(0.1f, dragMinHeadDistance);
        dragHandle.maxHeadDistance = Mathf.Max(dragHandle.minHeadDistance + 0.05f, dragMaxHeadDistance);
        dragHandle.pinchGrabDistance = Mathf.Max(0.005f, dragPinchGrabDistance);
        dragHandle.pinchReleaseDistance = Mathf.Max(dragHandle.pinchGrabDistance + 0.005f, dragPinchReleaseDistance);
        dragHandle.allowPinchFromPanelTopBand = createMenuDragHandle;
        dragHandle.panelTopBandHeight = createMenuDragHandle
            ? Mathf.Max(dragHandle.panelTopBandHeight, topBandSize.y + 18f)
            : Mathf.Max(18f, topThickness);
        dragHandle.panelTopBandWorldPadding = Mathf.Max(dragHandle.panelTopBandWorldPadding, dragTopBandWorldPadding);
        dragHandle.pinchTargetPadding = Mathf.Max(dragHandle.pinchTargetPadding, dragPinchTargetPadding);
        dragHandle.clampVerticalOffset = false;
        dragHandle.positionFollowSharpness = 18f;
        dragHandle.rotationFollowSharpness = 14f;
    }

    private static BoxCollider[] EnsureBoxColliderCount(GameObject target, int requiredCount)
    {
        BoxCollider[] colliders = target.GetComponents<BoxCollider>();
        while (colliders.Length < requiredCount)
        {
            target.AddComponent<BoxCollider>();
            colliders = target.GetComponents<BoxCollider>();
        }

        for (int i = requiredCount; i < colliders.Length; i++)
            colliders[i].enabled = false;

        return colliders;
    }

    private void EnsureWristToggleButton()
    {
        Transform existing = transform.Find("WristMenuToggle");
        if (existing == null)
        {
            GameObject root = new GameObject("WristMenuToggle", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _wristToggleCanvasRect = root.GetComponent<RectTransform>();
            _wristToggleCanvasRect.SetParent(transform, false);
        }
        else
        {
            _wristToggleCanvasRect = existing as RectTransform;
        }

        if (_wristToggleCanvasRect == null)
            return;

        Canvas canvas = _wristToggleCanvasRect.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        if (_wristToggleCanvasRect.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            _wristToggleCanvasRect.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        _wristToggleCanvasRect.sizeDelta = wristButtonCanvasSize;
        _wristToggleCanvasRect.localScale = Vector3.one * wristButtonCanvasScale;

        Transform buttonTransform = _wristToggleCanvasRect.Find("Button");
        RectTransform buttonRect;
        if (buttonTransform == null)
        {
            GameObject buttonGo = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(BoxCollider), typeof(XRSimpleInteractable), typeof(WristMenuToggleButton));
            buttonRect = buttonGo.GetComponent<RectTransform>();
            buttonRect.SetParent(_wristToggleCanvasRect, false);
        }
        else
        {
            buttonRect = buttonTransform as RectTransform;
        }

        if (buttonRect == null)
            return;

        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.one;
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        Image buttonImage = buttonRect.GetComponent<Image>();
        if (buttonImage == null)
            buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = wristButtonColor;

        Button button = buttonRect.GetComponent<Button>();
        if (button == null)
            button = buttonRect.gameObject.AddComponent<Button>();

        ColorBlock colors = button.colors;
        colors.normalColor = wristButtonColor;
        colors.highlightedColor = menuButtonHighlightColor;
        colors.selectedColor = menuButtonHighlightColor;
        colors.pressedColor = menuButtonPressedColor;
        colors.disabledColor = new Color(0.15f, 0.15f, 0.15f, 0.4f);
        button.colors = colors;

        BoxCollider collider = buttonRect.GetComponent<BoxCollider>();
        if (collider == null)
            collider = buttonRect.gameObject.AddComponent<BoxCollider>();
        WristMenuToggleButton toggle = buttonRect.GetComponent<WristMenuToggleButton>();
        if (toggle == null)
            toggle = buttonRect.gameObject.AddComponent<WristMenuToggleButton>();
        toggle.menu = this;
        toggle.SetTrackingOrigin(ResolveTrackingOrigin());
        toggle.useLeftHandPinch = true;
        toggle.useRightHandPinch = true;
        toggle.pressDebounceSeconds = wristButtonPressDebounce;
        toggle.colliderPadding = new Vector2(Mathf.Max(wristButtonColliderPadding.x, 34f), Mathf.Max(wristButtonColliderPadding.y, 20f));
        toggle.colliderDepth = wristButtonColliderDepth;
        toggle.pinchPressDistance = Mathf.Max(wristButtonPinchPressDistance, 0.05f);
        toggle.pinchReleaseDistance = Mathf.Max(wristButtonPinchReleaseDistance, toggle.pinchPressDistance + 0.015f);
        toggle.pinchTargetPadding = Mathf.Max(wristButtonPinchTargetPadding, 0.075f);
        toggle.enableHandHoverFallback = true;
        toggle.handHoverActivationDelay = Mathf.Clamp(toggle.handHoverActivationDelay, 0.08f, 0.14f);
        toggle.RefreshInteractableShape();

        collider.isTrigger = true;
        collider.center = Vector3.zero;

        XRBaseInteractable interactable = buttonRect.GetComponent<XRBaseInteractable>();
        if (interactable != null)
        {
            interactable.colliders.Clear();
            interactable.colliders.Add(collider);
            interactable.distanceCalculationMode = XRBaseInteractable.DistanceCalculationMode.ColliderVolume;
        }

        Text label = GetOrCreateLegacyText(buttonRect, "Label");
        if (label != null)
        {
            label.text = wristButtonLabel;
            label.alignment = TextAnchor.MiddleCenter;
            label.fontSize = 22;
            label.color = Color.white;
        }
    }

    private void UpdateWristTogglePose(bool forceInstant = false)
    {
        if (!handOnlyWristGridMode || _wristToggleCanvasRect == null)
            return;

        if (!TryGetHandOnlyWristPose(out Vector3 basePosition, out Quaternion baseRotation))
            return;

        Vector3 wristOffset = wristButtonLocalPosition + wristButtonExtraLocalOffset;
        Vector3 targetPosition = basePosition + (baseRotation * wristOffset);
        Quaternion targetRotation = baseRotation * Quaternion.Euler(wristButtonLocalEuler);

        if (wristButtonFaceCamera && Camera.main != null)
        {
            Vector3 toCamera = Camera.main.transform.position - targetPosition;
            if (toCamera.sqrMagnitude > 0.0001f)
                targetRotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
        }

        if (forceInstant || !Application.isPlaying)
        {
            _wristToggleCanvasRect.SetPositionAndRotation(targetPosition, targetRotation);
            return;
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(8f, followSmoothing) * Time.unscaledDeltaTime);
        _wristToggleCanvasRect.position = Vector3.Lerp(_wristToggleCanvasRect.position, targetPosition, t);
        _wristToggleCanvasRect.rotation = Quaternion.Slerp(_wristToggleCanvasRect.rotation, targetRotation, t);
    }

    private void SnapMenuNearHeadIfNeeded()
    {
        if (!handOnlyWristGridMode || !keepMenuAttachedToPlayer || !snapMenuNearHeadOnOpen || menuRoot == null)
            return;

        RectTransform menuRect = menuRoot.transform as RectTransform;
        Camera cam = Camera.main;
        if (menuRect == null || cam == null)
            return;

        Vector3 flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude <= 0.0001f)
            flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude <= 0.0001f)
            flatForward = Vector3.forward;

        Quaternion headYaw = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        Vector3 targetWorld = cam.transform.position + (headYaw * headRelativeMenuOffset);
        menuRect.position = targetWorld;

        if (faceHeadWhenOpeningMenu)
        {
            Vector3 toHead = cam.transform.position - targetWorld;
            toHead.y = 0f;
            if (toHead.sqrMagnitude > 0.0001f)
                menuRect.rotation = Quaternion.LookRotation(toHead.normalized, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
        }
        else
        {
            menuRect.rotation = headYaw * Quaternion.Euler(playerMenuLocalEuler);
        }
    }

    private bool TryGetHandOnlyWristPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        EnsureLeftAnchorsUpToDate();

        if (TryGetPreferredLeftWristAnchorPose(out position, out rotation))
            return true;

        if (TryGetBestTrackedLeftHandPose(out position, out rotation))
            return true;

        return TryGetBestTrackedLeftControllerPose(out position, out rotation);
    }

    private bool TryGetPreferredLeftWristAnchorPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        Transform wristAnchor = ResolvePreferredLeftWristAnchor();
        if (!IsPreferredLeftWristAnchorAvailable(wristAnchor))
            return false;

        position = wristAnchor.position;
        rotation = wristAnchor.rotation;
        return true;
    }

    private bool TryGetBestTrackedLeftHandPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (IsAnchorAvailableForFollow(leftHandAnchor, LeftAnchorKind.Hand))
        {
            position = leftHandAnchor.position;
            rotation = leftHandAnchor.rotation;
            return true;
        }

        bool leftHandTracked = IsTrackedNode(XRNode.LeftHand);
        if (TryGetLeftHandPoseFromSubsystem(out position, out rotation))
        {
            ConvertFallbackPoseToWorldIfNeeded(ref position, ref rotation);
            return true;
        }

        if (useXRNodePoseFallbackForFollow && leftHandTracked && TryGetNodePose(XRNode.LeftHand, out position, out rotation))
        {
            ConvertFallbackPoseToWorldIfNeeded(ref position, ref rotation);
            return true;
        }

        return false;
    }

    private Transform ResolvePreferredLeftWristAnchor()
    {
        if (IsPreferredLeftWristAnchorAvailable(_preferredLeftWristAnchor))
            return _preferredLeftWristAnchor;

        _preferredLeftWristAnchor = FindBestLeftWristAnchor();
        return _preferredLeftWristAnchor;
    }

    private Transform FindBestLeftWristAnchor()
    {
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (!IsAnchorReferenceValid(candidate))
                continue;

            string pathLower = GetTransformPathLower(candidate, 14);
            int score = ScoreLeftWristAnchor(pathLower);
            if (score <= int.MinValue / 2)
                continue;

            if (candidate.gameObject.activeInHierarchy)
                score += 6;
            else
                score -= 3;

            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private bool TryGetBestTrackedLeftControllerPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        bool leftControllerTracked = IsControllerTracked(true);
        if (leftControllerTracked && IsAnchorAvailableForFollow(leftControllerAnchor, LeftAnchorKind.Controller))
        {
            position = leftControllerAnchor.position;
            rotation = leftControllerAnchor.rotation;
            return true;
        }

        if (leftControllerTracked && TryGetControllerPose(true, out position, out rotation))
        {
            ConvertFallbackPoseToWorldIfNeeded(ref position, ref rotation);
            return true;
        }

        return false;
    }

    private static Text GetOrCreateLegacyText(RectTransform parent, string childName)
    {
        if (parent == null)
            return null;

        Transform existing = parent.Find(childName);
        Text label;
        if (existing == null)
        {
            GameObject go = new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            label = go.GetComponent<Text>();
        }
        else
        {
            label = existing.GetComponent<Text>();
            if (label == null)
                label = existing.gameObject.AddComponent<Text>();
        }

        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.raycastTarget = false;
        return label;
    }

    private void ApplyPanelVisualStyle(Image panelImage)
    {
        if (panelImage == null)
            return;

        panelImage.color = menuPanelColor;
        ApplySurfaceGraphicStyle(panelImage, menuPanelBorderColor, menuPanelShadowColor, new Vector2(2f, -2f), new Vector2(0f, -7f));
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

    private static string GetMenuButtonRawLabel(VirtualSceneMenuButton sceneButton)
    {
        if (sceneButton == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(sceneButton.customLabel))
            return NormalizeLabelWhitespace(sceneButton.customLabel);

        if (sceneButton.uiText != null && !string.IsNullOrWhiteSpace(sceneButton.uiText.text))
            return NormalizeLabelWhitespace(sceneButton.uiText.text);

        if (sceneButton.tmpText != null && !string.IsNullOrWhiteSpace(sceneButton.tmpText.text))
            return NormalizeLabelWhitespace(sceneButton.tmpText.text);

        return sceneButton.index.ToString();
    }

    private float GetMenuLabelWrapWidth()
    {
        float rawWidth = Mathf.Max(48f, gridCellSize.x - (menuLabelHorizontalPadding * 2f));
        float ratio = Mathf.Clamp(menuLabelWrapWidthRatio, 0.5f, 1f);
        return Mathf.Max(42f, rawWidth * ratio);
    }

    private void ApplyWordSafeLegacyLabel(Text label, string rawLabel, int preferredFontSize, int minFontSize, float maxWidth)
    {
        if (label == null)
            return;

        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.supportRichText = false;
        label.resizeTextForBestFit = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.alignment = TextAnchor.MiddleCenter;

        string normalizedLabel = NormalizeLabelWhitespace(rawLabel);
        int resolvedFontSize = ResolveLegacyFontSize(label, normalizedLabel, preferredFontSize, minFontSize, maxWidth);
        label.fontSize = resolvedFontSize;
        label.text = BuildWordWrappedLegacyLabel(label, normalizedLabel, resolvedFontSize, maxWidth);
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

    private void ApplyWordSafeTmpLabel(TMP_Text label, string rawLabel, int preferredFontSize, int minFontSize, float maxWidth)
    {
        if (label == null)
            return;

        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = false;

        string normalizedLabel = NormalizeLabelWhitespace(rawLabel);
        int safePreferred = Mathf.Max(1, preferredFontSize);
        int safeMin = Mathf.Clamp(minFontSize, 1, safePreferred);
        int resolvedFontSize = safeMin;
        string[] words = normalizedLabel.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

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

            if (!fits)
                continue;

            resolvedFontSize = fontSize;
            break;
        }

        label.fontSize = resolvedFontSize;
        label.text = BuildWordWrappedTmpLabel(label, normalizedLabel, resolvedFontSize, maxWidth);
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
        shadow.effectDistance = new Vector2(0f, -1.6f);
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

    private void TryAutoFindLeftAnchors()
    {
        if (!autoFindLeftAnchors)
            return;

        NormalizeLeftAnchorsIfNeeded();

        if (!IsAnchorSuitableForKind(leftControllerAnchor, LeftAnchorKind.Controller))
            leftControllerAnchor = FindBestLeftAnchor(LeftAnchorKind.Controller);

        if (!IsAnchorSuitableForKind(leftHandAnchor, LeftAnchorKind.Hand))
            leftHandAnchor = FindBestLeftAnchor(LeftAnchorKind.Hand);
    }

    private void EnsureLeftAnchorsUpToDate()
    {
        if (!autoFindLeftAnchors)
            return;

        NormalizeLeftAnchorsIfNeeded();

        if (Time.unscaledTime < _nextAnchorRediscoveryTime)
            return;

        bool needsController = !IsAnchorAvailableForFollow(leftControllerAnchor, LeftAnchorKind.Controller);
        bool needsHand = !IsAnchorAvailableForFollow(leftHandAnchor, LeftAnchorKind.Hand);

        Transform prevController = leftControllerAnchor;
        Transform prevHand = leftHandAnchor;

        if (needsController)
            leftControllerAnchor = FindBestLeftAnchor(LeftAnchorKind.Controller);

        if (needsHand)
            leftHandAnchor = FindBestLeftAnchor(LeftAnchorKind.Hand);

        if (verboseLogs && (prevController != leftControllerAnchor || prevHand != leftHandAnchor))
        {
            string ctrl = leftControllerAnchor != null ? GetTransformPathLower(leftControllerAnchor, 8) : "<null>";
            string hand = leftHandAnchor != null ? GetTransformPathLower(leftHandAnchor, 8) : "<null>";
            Debug.Log($"[HandRadialVirtualSceneMenu] Left anchors updated. controller={ctrl} hand={hand}");
        }

        _nextAnchorRediscoveryTime = Time.unscaledTime + Mathf.Max(0.1f, anchorRediscoveryInterval);
    }

    private void NormalizeLeftAnchorsIfNeeded()
    {
        if (!invalidateDuplicateLeftAnchors)
            return;

        if (leftHandAnchor != null && leftControllerAnchor != null && leftHandAnchor == leftControllerAnchor)
        {
            if (verboseLogs)
                Debug.LogWarning("[HandRadialVirtualSceneMenu] Left hand/controller anchor coincidono: reset hand anchor e nuova discovery.");

            leftHandAnchor = null;
        }

        if (leftHandAnchor != null && !IsAnchorSuitableForKind(leftHandAnchor, LeftAnchorKind.Hand))
            leftHandAnchor = null;

        if (leftControllerAnchor != null && !IsAnchorSuitableForKind(leftControllerAnchor, LeftAnchorKind.Controller))
            leftControllerAnchor = null;
    }

    private Transform FindBestLeftAnchor(LeftAnchorKind kind)
    {
        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.gameObject == null)
                continue;
            if (!t.gameObject.scene.IsValid())
                continue;

            string path = GetTransformPathLower(t, 8);
            if (!path.Contains("left"))
                continue;
            if (path.Contains("handmenu"))
                continue;

            int score = ScoreLeftAnchor(path, kind);
            if (score <= int.MinValue / 2)
                continue;

            if (t.gameObject.activeInHierarchy)
                score += 6;
            else
                score -= 3;

            if (score > bestScore)
            {
                best = t;
                bestScore = score;
            }
        }

        return best;
    }

    private static int ScoreLeftAnchor(string pathLower, LeftAnchorKind kind)
    {
        int score = 0;
        bool looksController = pathLower.Contains("controller");
        bool looksHand = pathLower.Contains("hand") || pathLower.Contains("wrist") || pathLower.Contains("palm");
        bool strongHandMarker = pathLower.Contains("left hand") || pathLower.Contains("wrist") || pathLower.Contains("palm");

        if (kind == LeftAnchorKind.Controller)
        {
            if (!looksController)
                return int.MinValue;
            score += 10;

            if (pathLower.Contains("left controller"))
                score += 6;
            if (pathLower.Contains("aim") || pathLower.Contains("attach") || pathLower.Contains("ray"))
                score += 4;
        }
        else
        {
            if (!looksHand)
                return int.MinValue;
            if (looksController && !strongHandMarker)
                return int.MinValue;
            score += 10;

            if (pathLower.Contains("left hand"))
                score += 6;
            if (pathLower.Contains("wrist") || pathLower.Contains("palm"))
                score += 4;
            if (looksController)
                score -= 8;
        }

        if (pathLower.Contains("anchor") || pathLower.Contains("attach") || pathLower.Contains("pose") || pathLower.Contains("aim"))
            score += 4;

        if (pathLower.Contains("pinch") || pathLower.Contains("grab"))
            score += 2;

        if (pathLower.Contains("ray") || pathLower.Contains("line") || pathLower.Contains("visual") || pathLower.Contains("mesh"))
            score -= 2;
        if (pathLower.Contains("callout") || pathLower.Contains("affordance") || pathLower.Contains("tooltip"))
            score -= 8;
        if (pathLower.Contains("menu"))
            score -= 6;
        if (pathLower.Contains("canvas") || pathLower.Contains("ui"))
            score -= 4;

        return score;
    }

    private static int ScoreLeftWristAnchor(string pathLower)
    {
        if (string.IsNullOrEmpty(pathLower) || !pathLower.Contains("left"))
            return int.MinValue;

        if (!pathLower.Contains("left hand"))
            return int.MinValue;

        if (pathLower.Contains("handmenu")
            || pathLower.Contains("controller")
            || pathLower.Contains("interactor")
            || pathLower.Contains("affordance")
            || pathLower.Contains("tooltip")
            || pathLower.Contains("linevisual"))
            return int.MinValue;

        int score = 0;
        bool endsWithWrist = pathLower.EndsWith("/l_wrist") || pathLower.EndsWith("/left wrist");
        bool endsWithPalm = pathLower.EndsWith("/l_palm") || pathLower.EndsWith("/left palm");

        if (pathLower.EndsWith("/left hand"))
            score += 40;
        if (pathLower.Contains("android xr visual"))
            score += 24;
        if (pathLower.Contains("quest visual"))
            score += 18;
        if (endsWithWrist)
            score += 70;
        else if (pathLower.Contains("l_wrist") || pathLower.Contains("/wrist"))
            score += 18;
        if (endsWithPalm)
            score += 36;
        else if (pathLower.Contains("l_palm") || pathLower.Contains("/palm"))
            score += 10;
        if (pathLower.Contains("pinch grab pose") || pathLower.Contains("pinch point") || pathLower.Contains("aim pose"))
            score -= 28;
        if (pathLower.Contains("tip")
            || pathLower.Contains("distal")
            || pathLower.Contains("intermediate")
            || pathLower.Contains("proximal")
            || pathLower.Contains("metacarpal"))
            score -= 18;

        int depthPenalty = Mathf.Max(0, pathLower.Split('/').Length - 8);
        score -= depthPenalty * 2;

        return score;
    }

    private static bool IsPreferredLeftWristAnchorAvailable(Transform anchor)
    {
        return IsAnchorReferenceValid(anchor)
            && anchor.gameObject.activeInHierarchy
            && ScoreLeftWristAnchor(GetTransformPathLower(anchor, 14)) > 0;
    }

    private static bool IsAnchorSuitableForKind(Transform anchor, LeftAnchorKind kind)
    {
        if (!IsAnchorReferenceValid(anchor))
            return false;

        string pathLower = GetTransformPathLower(anchor, 10);
        if (string.IsNullOrEmpty(pathLower) || !pathLower.Contains("left"))
            return false;

        return ScoreLeftAnchor(pathLower, kind) > 0;
    }

    private static bool IsAnchorReferenceValid(Transform anchor)
    {
        return anchor != null && anchor.gameObject != null && anchor.gameObject.scene.IsValid();
    }

    private static bool IsAnchorAvailableForFollow(Transform anchor, LeftAnchorKind kind)
    {
        return IsAnchorSuitableForKind(anchor, kind) && anchor.gameObject.activeInHierarchy;
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

    private void ConvertFallbackPoseToWorldIfNeeded(ref Vector3 position, ref Quaternion rotation)
    {
        if (!convertFallbackPoseToWorld)
            return;

        Transform trackingOrigin = ResolveTrackingOrigin();
        if (trackingOrigin == null)
            return;

        Vector3 convertedPosition = trackingOrigin.TransformPoint(position);
        Quaternion convertedRotation = trackingOrigin.rotation * rotation;

        Transform referenceAnchor = null;
        if (IsAnchorReferenceValid(leftHandAnchor))
            referenceAnchor = leftHandAnchor;
        else if (IsAnchorReferenceValid(leftControllerAnchor))
            referenceAnchor = leftControllerAnchor;

        if (referenceAnchor != null)
        {
            float rawDistance = (position - referenceAnchor.position).sqrMagnitude;
            float convertedDistance = (convertedPosition - referenceAnchor.position).sqrMagnitude;

            // Evita doppia conversione quando il provider restituisce gia' world-space.
            if (convertedDistance > rawDistance)
                return;
        }
        else if (Camera.main != null)
        {
            Vector3 headPosition = Camera.main.transform.position;
            float rawDistance = (position - headPosition).sqrMagnitude;
            float convertedDistance = (convertedPosition - headPosition).sqrMagnitude;

            // Senza anchor di riferimento usa la distanza dalla camera per evitare doppia conversione.
            if (convertedDistance > rawDistance)
                return;
        }

        position = convertedPosition;
        rotation = convertedRotation;
    }

    private Transform ResolveTrackingOrigin()
    {
        if (IsAnchorReferenceValid(trackingOriginOverride))
            return trackingOriginOverride;

        if (sceneGroupManager != null && sceneGroupManager.player != null && IsAnchorReferenceValid(sceneGroupManager.player))
            return sceneGroupManager.player;

        if (transform.parent != null && IsAnchorReferenceValid(transform.parent))
            return transform.parent;

        return null;
    }

    private static bool IsTrackedNode(XRNode node)
    {
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        return IsTrackedDevice(device);
    }

    private static bool IsControllerTracked(bool leftSide)
    {
        return TryGetTrackedControllerDevice(leftSide, out _);
    }

    private static bool TryGetControllerPose(bool leftSide, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (!TryGetTrackedControllerDevice(leftSide, out UnityEngine.XR.InputDevice device))
            return false;

        bool hasPos = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out position);
        bool hasRot = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out rotation);
        return hasPos && hasRot;
    }

    private static bool TryGetTrackedControllerDevice(bool leftSide, out UnityEngine.XR.InputDevice trackedDevice)
    {
        InputDeviceCharacteristics side = leftSide ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right;
        if (TryGetTrackedControllerDevice(side | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand, out trackedDevice))
            return true;

        return TryGetTrackedControllerDevice(side | InputDeviceCharacteristics.Controller, out trackedDevice);
    }

    private static bool TryGetTrackedControllerDevice(InputDeviceCharacteristics characteristics, out UnityEngine.XR.InputDevice trackedDevice)
    {
        trackedDevice = default;
        s_ControllerDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(characteristics, s_ControllerDevices);

        for (int i = 0; i < s_ControllerDevices.Count; i++)
        {
            UnityEngine.XR.InputDevice device = s_ControllerDevices[i];
            if (!IsTrackedDevice(device))
                continue;

            trackedDevice = device;
            return true;
        }

        return false;
    }

    private static bool IsTrackedDevice(UnityEngine.XR.InputDevice device)
    {
        if (!device.isValid)
            return false;

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && tracked)
            return true;

        const UnityEngine.XR.InputTrackingState poseTracked =
            UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trackingState, out UnityEngine.XR.InputTrackingState trackingState))
            return (trackingState & poseTracked) == poseTracked;

        return false;
    }

    private static bool TryGetNodePose(XRNode node, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;

        bool hasPos = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out position);
        bool hasRot = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out rotation);
        return hasPos && hasRot;
    }

    private static bool TryGetLeftHandPoseFromSubsystem(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

#if UNITY_XR_HANDS
        s_HandSubsystems.Clear();
        SubsystemManager.GetSubsystems(s_HandSubsystems);

        for (int i = 0; i < s_HandSubsystems.Count; i++)
        {
            XRHandSubsystem subsystem = s_HandSubsystems[i];
            if (subsystem == null || !subsystem.running)
                continue;

            XRHand left = subsystem.leftHand;
            if (!left.isTracked)
                continue;

            XRHandJoint wrist = left.GetJoint(XRHandJointID.Wrist);
            if (wrist.TryGetPose(out Pose wristPose))
            {
                position = wristPose.position;
                rotation = wristPose.rotation;
                return true;
            }

            XRHandJoint palm = left.GetJoint(XRHandJointID.Palm);
            if (palm.TryGetPose(out Pose palmPose))
            {
                position = palmPose.position;
                rotation = palmPose.rotation;
                return true;
            }
        }
#endif

        return false;
    }

    private void AutoFixWorldSpaceMenuIfNeeded()
    {
        if (!autoFixWorldSpaceMenu || menuRoot == null)
            return;

        Canvas c = menuRoot.GetComponent<Canvas>();
        if (c == null || c.renderMode != RenderMode.WorldSpace)
            return;

        Transform tr = menuRoot.transform;
        float sx = Mathf.Abs(tr.localScale.x);
        float sy = Mathf.Abs(tr.localScale.y);
        float sz = Mathf.Abs(tr.localScale.z);
        float maxAxis = Mathf.Max(sx, Mathf.Max(sy, sz));

        if (maxAxis > maxCanvasScaleBeforeFix)
        {
            tr.localScale = Vector3.one * targetCanvasLocalScale;
            maxAxis = Mathf.Abs(targetCanvasLocalScale);

            if (verboseLogs)
                Debug.Log($"[HandRadialVirtualSceneMenu] Auto-fix scala Canvas -> {targetCanvasLocalScale}");
        }

        if (handOnlyWristGridMode)
            return;

        RadialLayout radial = menuRoot.GetComponentInChildren<RadialLayout>(true);
        if (radial != null)
        {
            float safeScale = Mathf.Max(0.00001f, maxAxis);
            radial.radius = targetWorldRadiusMeters / safeScale;
            radial.ApplyLayout();
        }
    }

    private void EnsureXRUISetupIfNeeded()
    {
        if (!ensureXRUIRuntimeSetup)
            return;

        EventSystem es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            GameObject esGo = new GameObject("EventSystem");
            es = esGo.AddComponent<EventSystem>();
        }

#if ENABLE_INPUT_SYSTEM
        if (es != null && es.GetComponent<InputSystemUIInputModule>() == null)
            es.gameObject.AddComponent<InputSystemUIInputModule>();
#endif

        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null || c.renderMode != RenderMode.WorldSpace)
                continue;
            if (!c.gameObject.scene.IsValid())
                continue;
            if (c.hideFlags != HideFlags.None)
                continue;

            if (c.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                c.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }
    }
}
