using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
#if UNITY_XR_HANDS
using UnityEngine.XR.Hands;
#endif

public class XRHandsControllerRuntimeFix : MonoBehaviour
{
    private static readonly List<InputDevice> s_ControllerDevices = new List<InputDevice>(8);

    private enum HandSide
    {
        Left,
        Right
    }

    private enum SideMode
    {
        Unknown,
        Hands,
        Controllers
    }

    private sealed class SideBindings
    {
        public readonly HandSide side;
        public readonly List<Renderer> handRenderers = new List<Renderer>(32);
        public readonly List<Renderer> controllerRenderers = new List<Renderer>(32);
        public readonly List<Behaviour> handBehaviours = new List<Behaviour>(32);
        public readonly List<Behaviour> controllerBehaviours = new List<Behaviour>(32);

        public SideBindings(HandSide side)
        {
            this.side = side;
        }

        public void Clear()
        {
            handRenderers.Clear();
            controllerRenderers.Clear();
            handBehaviours.Clear();
            controllerBehaviours.Clear();
        }
    }

    [Header("Polling")]
    [Min(0.05f)] public float pollIntervalSeconds = 0.2f;
    [Min(0.25f)] public float rediscoverIntervalSeconds = 2f;
    public bool verboseLogs = false;

    [Header("Behavior")]
    [Tooltip("Quando mano tracciata e controller non tracciato, mostra le mani (per lato).")]
    public bool preferHandsWhenTracked = true;
    [Tooltip("Se true, disabilita i behaviour legati ai controller quando i controller non sono tracciati.")]
    public bool gateControllerInputBehaviours = true;
    [Tooltip("Lascia attive le logiche mano anche in modalita controller per non perdere pinch/poke in fallback.")]
    public bool keepHandInputBehavioursAlwaysEnabled = true;

    private readonly SideBindings _left = new SideBindings(HandSide.Left);
    private readonly SideBindings _right = new SideBindings(HandSide.Right);
    private bool _hasDiscoveredSideRoots;
    private float _nextPollTime;
    private float _nextDiscoverTime;
    private float _nextNativeManagerProbeTime;
    private bool _nativeManagerActive;
    private bool _hasLoggedNativeManagerBypass;
    private SideMode _leftMode = SideMode.Unknown;
    private SideMode _rightMode = SideMode.Unknown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        XRHandsControllerRuntimeFix existing = FindFirstObjectByType<XRHandsControllerRuntimeFix>();
        if (existing != null)
            return;

        GameObject go = new GameObject("XRHandsControllerRuntimeFix");
        DontDestroyOnLoad(go);
        go.AddComponent<XRHandsControllerRuntimeFix>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        MarkBindingsDirty();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene _, LoadSceneMode __)
    {
        MarkBindingsDirty();
    }

    private void Update()
    {
        if (HasActiveNativeModalityManager())
        {
            if (verboseLogs && !_hasLoggedNativeManagerBypass)
            {
                Debug.Log("[XRHandsControllerRuntimeFix] XRInputModalityManager attivo: bypass fix custom.");
                _hasLoggedNativeManagerBypass = true;
            }

            return;
        }

        _hasLoggedNativeManagerBypass = false;

        if (Time.unscaledTime < _nextPollTime)
            return;

        _nextPollTime = Time.unscaledTime + pollIntervalSeconds;

        if (!_hasDiscoveredSideRoots || Time.unscaledTime >= _nextDiscoverTime)
            DiscoverSideRoots();

        GetHandTrackedState(out bool leftHandTracked, out bool rightHandTracked);
        bool leftControllerTracked = IsControllerTracked(true);
        bool rightControllerTracked = IsControllerTracked(false);

        SideMode desiredLeft = ResolveDesiredMode(leftHandTracked, leftControllerTracked);
        SideMode desiredRight = ResolveDesiredMode(rightHandTracked, rightControllerTracked);

        ApplySideMode(_left, desiredLeft, ref _leftMode);
        ApplySideMode(_right, desiredRight, ref _rightMode);
    }

    private bool HasActiveNativeModalityManager()
    {
        if (Time.unscaledTime < _nextNativeManagerProbeTime)
            return _nativeManagerActive;

        _nextNativeManagerProbeTime = Time.unscaledTime + 1f;
        _nativeManagerActive = false;

        XRInputModalityManager[] managers =
            FindObjectsByType<XRInputModalityManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            XRInputModalityManager manager = managers[i];
            if (manager != null && manager.isActiveAndEnabled)
            {
                _nativeManagerActive = true;
                break;
            }
        }

        return _nativeManagerActive;
    }

    private void MarkBindingsDirty()
    {
        _hasDiscoveredSideRoots = false;
        _nextDiscoverTime = 0f;
        _left.Clear();
        _right.Clear();
        _leftMode = SideMode.Unknown;
        _rightMode = SideMode.Unknown;
    }

    private SideMode ResolveDesiredMode(bool handTracked, bool controllerTracked)
    {
        if (controllerTracked)
            return SideMode.Controllers;

        if (preferHandsWhenTracked && handTracked)
            return SideMode.Hands;

        if (handTracked)
            return SideMode.Hands;

        return SideMode.Unknown;
    }

    private void DiscoverSideRoots()
    {
        _left.Clear();
        _right.Clear();

        HashSet<int> leftHandRendererIds = new HashSet<int>();
        HashSet<int> leftControllerRendererIds = new HashSet<int>();
        HashSet<int> rightHandRendererIds = new HashSet<int>();
        HashSet<int> rightControllerRendererIds = new HashSet<int>();
        HashSet<int> leftHandBehaviourIds = new HashSet<int>();
        HashSet<int> leftControllerBehaviourIds = new HashSet<int>();
        HashSet<int> rightHandBehaviourIds = new HashSet<int>();
        HashSet<int> rightControllerBehaviourIds = new HashSet<int>();

        Transform[] all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.gameObject == null)
                continue;
            if (!t.gameObject.scene.IsValid())
                continue;

            string nameLower = t.name.ToLowerInvariant();
            string pathLower = GetTransformPathLower(t, 7);

            if (pathLower.Contains("handmenu"))
                continue;

            bool isLeft = pathLower.Contains("left");
            bool isRight = pathLower.Contains("right");
            if (isLeft == isRight)
                continue;

            bool looksController = nameLower.Contains("controller") || pathLower.Contains("/controller");
            bool looksHand = nameLower.Contains("hand") || nameLower.Contains("wrist") || nameLower.Contains("palm")
                || pathLower.Contains("/hand") || pathLower.Contains("/wrist") || pathLower.Contains("/palm");

            if (!looksController && !looksHand)
                continue;

            bool hasRenderers = Has3DRendererInChildren(t);
            if (!hasRenderers && !HasInputBehaviourInChildren(t))
                continue;

            SideBindings side = isLeft ? _left : _right;
            if (looksController)
            {
                AddRenderers(t, side.controllerRenderers, isLeft ? leftControllerRendererIds : rightControllerRendererIds);
                AddInputBehaviours(t, side.controllerBehaviours, isLeft ? leftControllerBehaviourIds : rightControllerBehaviourIds);
            }

            if (looksHand && !looksController)
            {
                AddRenderers(t, side.handRenderers, isLeft ? leftHandRendererIds : rightHandRendererIds);
                AddInputBehaviours(t, side.handBehaviours, isLeft ? leftHandBehaviourIds : rightHandBehaviourIds);
            }
        }

        _hasDiscoveredSideRoots = true;
        _nextDiscoverTime = Time.unscaledTime + Mathf.Max(0.25f, rediscoverIntervalSeconds);

        if (verboseLogs)
        {
            Debug.Log(
                $"[XRHandsControllerRuntimeFix] Side roots discovered. " +
                $"L(handR={_left.handRenderers.Count}, ctrlR={_left.controllerRenderers.Count}, handB={_left.handBehaviours.Count}, ctrlB={_left.controllerBehaviours.Count}) " +
                $"R(handR={_right.handRenderers.Count}, ctrlR={_right.controllerRenderers.Count}, handB={_right.handBehaviours.Count}, ctrlB={_right.controllerBehaviours.Count})");
        }
    }

    private void ApplySideMode(SideBindings side, SideMode desiredMode, ref SideMode currentMode)
    {
        if (desiredMode == currentMode)
            return;

        bool showHands = desiredMode == SideMode.Hands;
        bool showControllers = desiredMode == SideMode.Controllers;

        SetRenderersEnabled(side.handRenderers, showHands);
        SetRenderersEnabled(side.controllerRenderers, showControllers);

        if (gateControllerInputBehaviours)
            SetBehavioursEnabled(side.controllerBehaviours, showControllers);

        if (!keepHandInputBehavioursAlwaysEnabled)
            SetBehavioursEnabled(side.handBehaviours, showHands);

        currentMode = desiredMode;

        if (verboseLogs)
            Debug.Log($"[XRHandsControllerRuntimeFix] {side.side} -> {desiredMode}");
    }

    private static void SetRenderersEnabled(List<Renderer> renderers, bool enabled)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer r = renderers[i];
            if (r != null)
                r.enabled = enabled;
        }
    }

    private static void SetBehavioursEnabled(List<Behaviour> behaviours, bool enabled)
    {
        for (int i = 0; i < behaviours.Count; i++)
        {
            Behaviour b = behaviours[i];
            if (b != null)
                b.enabled = enabled;
        }
    }

    private static void AddRenderers(Transform root, List<Renderer> destination, HashSet<int> dedupe)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;

            int id = r.GetInstanceID();
            if (!dedupe.Add(id))
                continue;

            destination.Add(r);
        }
    }

    private static void AddInputBehaviours(Transform root, List<Behaviour> destination, HashSet<int> dedupe)
    {
        Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour b = behaviours[i];
            if (b == null)
                continue;
            if (!IsInputBehaviourCandidate(b))
                continue;

            int id = b.GetInstanceID();
            if (!dedupe.Add(id))
                continue;

            destination.Add(b);
        }
    }

    private static bool IsInputBehaviourCandidate(Behaviour behaviour)
    {
        string typeName = behaviour.GetType().Name.ToLowerInvariant();

        if (typeName.Contains("interactor") || typeName.Contains("interaction"))
            return true;
        if (typeName.Contains("controller") || typeName.Contains("trackedpose"))
            return true;
        if (typeName.Contains("input") || typeName.Contains("poke") || typeName.Contains("nearfar"))
            return true;
        if (typeName.Contains("ray") && typeName.Contains("xr"))
            return true;

        return false;
    }

    private static bool Has3DRendererInChildren(Transform t)
    {
        Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true);
        return renderers != null && renderers.Length > 0;
    }

    private static bool HasInputBehaviourInChildren(Transform t)
    {
        Behaviour[] behaviours = t.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            Behaviour b = behaviours[i];
            if (b != null && IsInputBehaviourCandidate(b))
                return true;
        }

        return false;
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

    private static void GetHandTrackedState(out bool leftTracked, out bool rightTracked)
    {
        leftTracked = false;
        rightTracked = false;

#if UNITY_XR_HANDS
        List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
        SubsystemManager.GetSubsystems(subsystems);
        for (int i = 0; i < subsystems.Count; i++)
        {
            XRHandSubsystem s = subsystems[i];
            if (s == null || !s.running)
                continue;

            leftTracked |= s.leftHand.isTracked;
            rightTracked |= s.rightHand.isTracked;
        }
#endif

        if (!leftTracked)
            leftTracked = IsTrackedNode(XRNode.LeftHand);

        if (!rightTracked)
            rightTracked = IsTrackedNode(XRNode.RightHand);
    }

    private static bool IsTrackedNode(XRNode node)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        return IsTrackedDevice(device);
    }

    private static bool IsControllerTracked(bool leftSide)
    {
        InputDeviceCharacteristics side = leftSide ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right;
        return HasTrackedController(side | InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand)
            || HasTrackedController(side | InputDeviceCharacteristics.Controller);
    }

    private static bool HasTrackedController(InputDeviceCharacteristics characteristics)
    {
        s_ControllerDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(characteristics, s_ControllerDevices);

        for (int i = 0; i < s_ControllerDevices.Count; i++)
        {
            if (IsTrackedDevice(s_ControllerDevices[i]))
                return true;
        }

        return false;
    }

    private static bool IsTrackedDevice(InputDevice device)
    {
        if (!device.isValid)
            return false;

        if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
            return true;

        const UnityEngine.XR.InputTrackingState poseTracked =
            UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation;
        if (device.TryGetFeatureValue(CommonUsages.trackingState, out UnityEngine.XR.InputTrackingState trackingState))
            return (trackingState & poseTracked) == poseTracked;

        return false;
    }
}
