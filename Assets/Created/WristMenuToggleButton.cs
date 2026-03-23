using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public class WristMenuToggleButton : MonoBehaviour
{
    public HandRadialVirtualSceneMenu menu;
    public Transform trackingOrigin;
    [Min(0f)] public float pressDebounceSeconds = 0.08f;
    public bool useXRHandsPinch = true;
    public bool useLeftHandPinch = false;
    public bool useRightHandPinch = true;
    [Min(0.005f)] public float pinchPressDistance = 0.025f;
    [Min(0.005f)] public float pinchReleaseDistance = 0.04f;
    [Min(0f)] public float pinchTargetPadding = 0.02f;
    public Vector2 colliderPadding = new Vector2(20f, 12f);
    [Min(0.001f)] public float colliderDepth = 14f;
    [Header("Hand Fallback")]
    [Tooltip("Permette l'apertura del menu anche con hover/poke sul bottone da polso.")]
    public bool enableHandHoverFallback = true;
    [Min(0f)] public float handHoverActivationDelay = 0.12f;

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
            _xrInteractable.selectEntered.AddListener(OnXRSelected);

        if (_xrInteractable != null)
            _xrInteractable.activated.AddListener(OnXRActivated);

        if (_xrInteractable != null)
            _xrInteractable.hoverEntered.AddListener(OnXRHoverEntered);

        if (_xrInteractable != null)
            _xrInteractable.hoverExited.AddListener(OnXRHoverExited);
    }

    private void OnDisable()
    {
        if (_button != null)
            _button.onClick.RemoveListener(OnPressed);

        if (_xrInteractable != null)
            _xrInteractable.selectEntered.RemoveListener(OnXRSelected);

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
            menu = GetComponentInParent<HandRadialVirtualSceneMenu>();

        if (menu == null)
        {
            Debug.LogWarning("[WristMenuToggleButton] Menu non assegnato.");
            return;
        }

        menu.ToggleMenu();
    }

    private void OnXRSelected(SelectEnterEventArgs _)
    {
        TryPress();
    }

    private void OnXRActivated(ActivateEventArgs _)
    {
        TryPress();
    }

    public void SetTrackingOrigin(Transform root)
    {
        trackingOrigin = root;
    }

    public void RefreshInteractableShape()
    {
        CacheComponents();
        SyncInteractableCollider();
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
            bc.size = new Vector3(0.1f, 0.06f, Mathf.Max(0.001f, colliderDepth));
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

    private void OnValidate()
    {
        CacheComponents();
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
}
