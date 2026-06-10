using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public class NureLanguageSelectionButton : MonoBehaviour
{
    public SceneGroupManager sceneGroupManager;
    public SceneGroupManager.NureLanguage language;
    public Transform trackingOrigin;

    [Min(0f)] public float pressDebounceSeconds = 0.12f;
    public bool useXRHandsPinch = true;
    public bool useLeftHandPinch = true;
    public bool useRightHandPinch = true;
    [Min(0.005f)] public float pinchPressDistance = 0.04f;
    [Min(0.005f)] public float pinchReleaseDistance = 0.06f;
    [Min(0f)] public float pinchTargetPadding = 0.08f;
    public Vector3 worldColliderSize = new Vector3(0.42f, 0.16f, 0.04f);
    public Vector2 colliderPadding = new Vector2(42f, 28f);
    [Min(0.001f)] public float colliderDepth = 24f;

    [Header("Hand Fallback")]
    public bool enableHandHoverFallback = true;
    [Min(0f)] public float handHoverActivationDelay = 0.14f;

    private Button _button;
    private XRBaseInteractable _xrInteractable;
    private Collider _xrCollider;
    private float _lastPressTime = -10f;
    private XRHandsPinchUtility.HandSide? _activePinchSide;
    private bool _handHoverActive;
    private float _handHoverStartTime;
    private IXRHoverInteractor _handHoverInteractor;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnEnable()
    {
        CacheComponents();

        if (_button != null)
            _button.onClick.AddListener(OnPressed);

        if (_xrInteractable != null)
        {
            _xrInteractable.selectEntered.AddListener(OnXRSelected);
            _xrInteractable.activated.AddListener(OnXRActivated);
            _xrInteractable.hoverEntered.AddListener(OnXRHoverEntered);
            _xrInteractable.hoverExited.AddListener(OnXRHoverExited);
        }
    }

    private void OnDisable()
    {
        if (_button != null)
            _button.onClick.RemoveListener(OnPressed);

        if (_xrInteractable != null)
        {
            _xrInteractable.selectEntered.RemoveListener(OnXRSelected);
            _xrInteractable.activated.RemoveListener(OnXRActivated);
            _xrInteractable.hoverEntered.RemoveListener(OnXRHoverEntered);
            _xrInteractable.hoverExited.RemoveListener(OnXRHoverExited);
        }

        _activePinchSide = null;
        _handHoverActive = false;
        _handHoverInteractor = null;
    }

    private void Update()
    {
        UpdateHandHoverFallback();
        UpdatePinchInput();
    }

    public void Configure(SceneGroupManager manager, SceneGroupManager.NureLanguage targetLanguage, Transform root)
    {
        sceneGroupManager = manager;
        language = targetLanguage;
        trackingOrigin = root;
    }

    public void RefreshInteractableShape()
    {
        CacheComponents();
        SyncInteractableCollider();
    }

    public void OnPressed()
    {
        TryPress();
    }

    public void PressNow()
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

        if (sceneGroupManager == null)
            sceneGroupManager = FindFirstObjectByType<SceneGroupManager>();

        if (sceneGroupManager == null)
        {
            Debug.LogWarning("[NureLanguageSelectionButton] SceneGroupManager non assegnato.");
            return;
        }

        sceneGroupManager.SelectLanguage(language);
    }

    private void OnXRSelected(SelectEnterEventArgs _)
    {
        TryPress();
    }

    private void OnXRActivated(ActivateEventArgs _)
    {
        TryPress();
    }

    private void OnTriggerEnter(Collider other)
    {
        TryPressFromCollider(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryPressFromCollider(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryPressFromCollider(collision != null ? collision.collider : null);
    }

    private void OnCollisionStay(Collision collision)
    {
        TryPressFromCollider(collision != null ? collision.collider : null);
    }

    private void TryPressFromCollider(Collider other)
    {
        if (!IsHandLikeCollider(other))
            return;

        TryPress();
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
            bc.size = new Vector3(
                Mathf.Max(0.01f, worldColliderSize.x),
                Mathf.Max(0.01f, worldColliderSize.y),
                Mathf.Max(0.001f, worldColliderSize.z));
            bc.isTrigger = false;
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

    private static bool IsHandLikeCollider(Collider other)
    {
        if (other == null || other.transform == null)
            return false;

        string path = GetTransformPathLower(other.transform, 8);
        return path.Contains("hand")
            || path.Contains("finger")
            || path.Contains("index")
            || path.Contains("thumb")
            || path.Contains("poke")
            || path.Contains("controller")
            || path.Contains("interactor");
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
}
