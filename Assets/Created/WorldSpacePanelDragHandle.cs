using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[DisallowMultipleComponent]
public class WorldSpacePanelDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IInitializePotentialDragHandler
{
    public Transform panelRoot;
    public RectTransform panelRect;
    public Transform playerRoot;
    public Transform trackingOrigin;
    public float minHeadDistance = 0.22f;
    public float maxHeadDistance = 0.9f;
    public bool keepPanelFacingHeadYaw = true;
    public bool clampVerticalOffset = true;
    public Vector2 localVerticalClamp = new Vector2(-0.35f, 0.45f);
    public bool useXRHandsPinchDrag = true;
    public bool useRectTransformBoundsAsHandle = true;
    public bool allowLeftHandPinch = true;
    public bool allowRightHandPinch = true;
    [Min(0.005f)] public float pinchGrabDistance = 0.028f;
    [Min(0.005f)] public float pinchReleaseDistance = 0.04f;
    [Min(0f)] public float pinchTargetPadding = 0.03f;
    public bool allowPinchFromPanelTopBand = true;
    [Min(10f)] public float panelTopBandHeight = 58f;
    [Min(0f)] public float panelTopBandWorldPadding = 0.035f;
    [Min(0f)] public float positionFollowSharpness = 0f;
    [Min(0f)] public float rotationFollowSharpness = 0f;

    private XRBaseInteractable _interactable;
    private IXRSelectInteractor _currentInteractor;
    private Collider[] _handleColliders = System.Array.Empty<Collider>();
    private Vector3 _grabOffsetWorld;
    private Vector3 _grabLocalPoint;
    private bool _hasGrabLocalPoint;
    private bool _isPinchDragging;
    private bool _isPointerDragging;
    private XRHandsPinchUtility.HandSide _activePinchSide;

    private void Awake()
    {
        _interactable = GetComponent<XRBaseInteractable>();
        if (_interactable == null)
            _interactable = gameObject.AddComponent<XRSimpleInteractable>();

        RefreshHandleColliders();
    }

    private void OnEnable()
    {
        if (_interactable == null)
            _interactable = GetComponent<XRBaseInteractable>();

        if (_interactable != null)
        {
            _interactable.selectEntered.AddListener(OnSelectEntered);
            _interactable.selectExited.AddListener(OnSelectExited);
        }

        RefreshHandleColliders();
    }

    private void OnDisable()
    {
        if (_interactable != null)
        {
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
            _interactable.selectExited.RemoveListener(OnSelectExited);
        }

        _currentInteractor = null;
        _isPinchDragging = false;
        _isPointerDragging = false;
    }

    private void Update()
    {
        if (panelRoot == null)
            return;

        if (panelRect == null && panelRoot is RectTransform rectTransform)
            panelRect = rectTransform;

        if (UpdatePinchDrag())
            return;

        if (_currentInteractor == null)
            return;

        Transform interactorTransform = _currentInteractor.transform;
        if (interactorTransform == null)
            return;

        ApplyPanelTargetFromGrabPoint(interactorTransform.position);
    }

    private bool UpdatePinchDrag()
    {
        if (!useXRHandsPinchDrag)
            return false;

        if (_handleColliders == null || _handleColliders.Length == 0)
            RefreshHandleColliders();

        if (_isPinchDragging)
        {
            if (!XRHandsPinchUtility.TryGetPinchData(_activePinchSide, ResolveTrackingOrigin(), out XRHandsPinchUtility.PinchData activePinch)
                || activePinch.pinchDistance > pinchReleaseDistance)
            {
                _isPinchDragging = false;
                _hasGrabLocalPoint = false;
                return false;
            }

            ApplyPanelTargetFromGrabPoint(activePinch.pinchWorld);
            return true;
        }

        if (TryStartPinchDrag(XRHandsPinchUtility.HandSide.Right, allowRightHandPinch))
            return true;

        return TryStartPinchDrag(XRHandsPinchUtility.HandSide.Left, allowLeftHandPinch);
    }

    private bool TryStartPinchDrag(XRHandsPinchUtility.HandSide side, bool enabled)
    {
        if (!enabled)
            return false;

        if (!XRHandsPinchUtility.TryGetPinchData(side, ResolveTrackingOrigin(), out XRHandsPinchUtility.PinchData pinch)
            || pinch.pinchDistance > pinchGrabDistance)
            return false;

        if (!IsPointNearDragSurface(pinch.pinchWorld))
            return false;

        _isPinchDragging = true;
        _activePinchSide = side;
        CaptureGrabReference(pinch.pinchWorld);
        _currentInteractor = null;
        return true;
    }

    private bool IsPointNearHandle(Vector3 pointWorld)
    {
        if (useRectTransformBoundsAsHandle
            && transform is RectTransform handleRect
            && IsPointNearRect(handleRect, pointWorld, pinchTargetPadding))
            return true;

        if (_handleColliders == null || _handleColliders.Length == 0)
            return false;

        float maxDistanceSq = pinchTargetPadding * pinchTargetPadding;
        for (int i = 0; i < _handleColliders.Length; i++)
        {
            Collider handleCollider = _handleColliders[i];
            if (handleCollider == null || !handleCollider.enabled)
                continue;

            Vector3 closestPoint = handleCollider.ClosestPoint(pointWorld);
            if ((closestPoint - pointWorld).sqrMagnitude <= maxDistanceSq)
                return true;
        }

        return false;
    }

    private bool IsPointNearDragSurface(Vector3 pointWorld)
    {
        if (IsPointNearHandle(pointWorld))
            return true;

        return IsPointNearPanelTopBand(pointWorld);
    }

    private bool IsPointNearPanelTopBand(Vector3 pointWorld)
    {
        if (!allowPinchFromPanelTopBand || panelRect == null)
            return false;

        Vector3 localPoint3 = panelRect.InverseTransformPoint(pointWorld);
        Rect rect = panelRect.rect;

        float scaleX = Mathf.Max(0.0001f, panelRect.lossyScale.x);
        float scaleY = Mathf.Max(0.0001f, panelRect.lossyScale.y);
        float scaleDepth = GetLargestLossyScaleComponent(panelRect);
        float paddingX = panelTopBandWorldPadding / scaleX;
        float paddingY = panelTopBandWorldPadding / scaleY;
        float paddingDepth = panelTopBandWorldPadding / scaleDepth;

        float minX = rect.xMin - paddingX;
        float maxX = rect.xMax + paddingX;
        float minY = rect.yMax - panelTopBandHeight - paddingY;
        float maxY = rect.yMax + paddingY;

        return localPoint3.x >= minX
            && localPoint3.x <= maxX
            && localPoint3.y >= minY
            && localPoint3.y <= maxY
            && Mathf.Abs(localPoint3.z) <= paddingDepth;
    }

    private static bool IsPointNearRect(RectTransform rectTransform, Vector3 pointWorld, float paddingWorld)
    {
        if (rectTransform == null)
            return false;

        Vector3 localPoint = rectTransform.InverseTransformPoint(pointWorld);
        Rect rect = rectTransform.rect;

        float scaleX = Mathf.Max(0.0001f, Mathf.Abs(rectTransform.lossyScale.x));
        float scaleY = Mathf.Max(0.0001f, Mathf.Abs(rectTransform.lossyScale.y));
        float scaleDepth = GetLargestLossyScaleComponent(rectTransform);
        float paddingX = paddingWorld / scaleX;
        float paddingY = paddingWorld / scaleY;
        float paddingDepth = paddingWorld / scaleDepth;

        return localPoint.x >= rect.xMin - paddingX
            && localPoint.x <= rect.xMax + paddingX
            && localPoint.y >= rect.yMin - paddingY
            && localPoint.y <= rect.yMax + paddingY
            && Mathf.Abs(localPoint.z) <= paddingDepth;
    }

    private static float GetLargestLossyScaleComponent(Transform transform)
    {
        Vector3 lossyScale = transform.lossyScale;
        float largestComponent = Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y));
        largestComponent = Mathf.Max(largestComponent, Mathf.Abs(lossyScale.z));
        return Mathf.Max(0.0001f, largestComponent);
    }

    private void ApplyPanelTargetFromGrabPoint(Vector3 grabWorld)
    {
        Quaternion targetRotation = ResolveTargetRotation(panelRoot != null ? panelRoot.position : grabWorld, Camera.main);
        Vector3 targetWorld = ResolvePanelOriginFromGrabPoint(grabWorld, targetRotation);

        Camera cam = Camera.main;
        if (cam != null)
            targetWorld = ClampDistanceToHead(cam.transform, targetWorld);

        if (playerRoot != null && clampVerticalOffset)
        {
            Vector3 local = playerRoot.InverseTransformPoint(targetWorld);
            local.y = Mathf.Clamp(local.y, localVerticalClamp.x, localVerticalClamp.y);
            targetWorld = playerRoot.TransformPoint(local);
        }

        targetRotation = ResolveTargetRotation(targetWorld, cam);
        targetWorld = ResolvePanelOriginFromGrabPoint(grabWorld, targetRotation);

        if (cam != null)
            targetWorld = ClampDistanceToHead(cam.transform, targetWorld);

        if (playerRoot != null && clampVerticalOffset)
        {
            Vector3 local = playerRoot.InverseTransformPoint(targetWorld);
            local.y = Mathf.Clamp(local.y, localVerticalClamp.x, localVerticalClamp.y);
            targetWorld = playerRoot.TransformPoint(local);
        }

        if (!Application.isPlaying || positionFollowSharpness <= 0f)
        {
            panelRoot.position = targetWorld;
        }
        else
        {
            float positionT = 1f - Mathf.Exp(-positionFollowSharpness * Time.unscaledDeltaTime);
            panelRoot.position = Vector3.Lerp(panelRoot.position, targetWorld, positionT);
        }

        if (!keepPanelFacingHeadYaw)
            return;

        if (!Application.isPlaying || rotationFollowSharpness <= 0f)
        {
            panelRoot.rotation = targetRotation;
        }
        else
        {
            float rotationT = 1f - Mathf.Exp(-rotationFollowSharpness * Time.unscaledDeltaTime);
            panelRoot.rotation = Quaternion.Slerp(panelRoot.rotation, targetRotation, rotationT);
        }
    }

    private Quaternion ResolveTargetRotation(Vector3 targetWorld, Camera cam)
    {
        Quaternion targetRotation = panelRoot != null ? panelRoot.rotation : transform.rotation;
        if (!keepPanelFacingHeadYaw || cam == null)
            return targetRotation;

        Vector3 toHead = cam.transform.position - targetWorld;
        toHead.y = 0f;
        if (toHead.sqrMagnitude <= 0.0001f)
            return targetRotation;

        return Quaternion.LookRotation(toHead.normalized, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
    }

    private Vector3 ResolvePanelOriginFromGrabPoint(Vector3 grabWorld, Quaternion targetRotation)
    {
        if (!_hasGrabLocalPoint || panelRoot == null)
            return grabWorld + _grabOffsetWorld;

        Vector3 scaledLocalGrab = Vector3.Scale(_grabLocalPoint, panelRoot.lossyScale);
        return grabWorld - (targetRotation * scaledLocalGrab);
    }

    private void CaptureGrabReference(Vector3 grabWorld)
    {
        if (panelRoot == null)
        {
            _grabOffsetWorld = Vector3.zero;
            _grabLocalPoint = Vector3.zero;
            _hasGrabLocalPoint = false;
            return;
        }

        _grabOffsetWorld = panelRoot.position - grabWorld;
        _grabLocalPoint = panelRoot.InverseTransformPoint(grabWorld);
        _hasGrabLocalPoint = true;
    }

    private Vector3 ClampDistanceToHead(Transform head, Vector3 desiredWorld)
    {
        if (head == null)
            return desiredWorld;

        Vector3 fromHead = desiredWorld - head.position;
        float distance = fromHead.magnitude;
        if (distance <= 0.0001f)
            fromHead = head.forward;
        else
            fromHead /= distance;

        float clampedDistance = Mathf.Clamp(distance, minHeadDistance, maxHeadDistance);
        return head.position + fromHead * clampedDistance;
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (panelRoot == null || args.interactorObject == null)
            return;

        _currentInteractor = args.interactorObject;
        Transform interactorTransform = _currentInteractor.transform;
        CaptureGrabReference(interactorTransform.position);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (args.interactorObject == _currentInteractor)
            _currentInteractor = null;

        if (_currentInteractor == null && !_isPinchDragging && !_isPointerDragging)
            _hasGrabLocalPoint = false;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        if (eventData != null)
            eventData.useDragThreshold = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (panelRoot == null || eventData == null)
            return;

        if (!TryGetPointerWorldPosition(eventData, out Vector3 pointerWorld))
            return;

        _isPointerDragging = true;
        _currentInteractor = null;
        _isPinchDragging = false;
        CaptureGrabReference(pointerWorld);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_isPointerDragging || panelRoot == null || eventData == null)
            return;

        if (!TryGetPointerWorldPosition(eventData, out Vector3 pointerWorld))
            return;

        ApplyPanelTargetFromGrabPoint(pointerWorld);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _isPointerDragging = false;
        if (_currentInteractor == null && !_isPinchDragging)
            _hasGrabLocalPoint = false;
    }

    private void RefreshHandleColliders()
    {
        Collider[] colliders = GetComponents<Collider>();
        if (colliders == null || colliders.Length == 0)
        {
            _handleColliders = System.Array.Empty<Collider>();
            return;
        }

        int validCount = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider != null && collider.enabled)
                validCount++;
        }

        if (validCount <= 0)
        {
            _handleColliders = System.Array.Empty<Collider>();
            return;
        }

        if (validCount == colliders.Length)
        {
            _handleColliders = colliders;
            return;
        }

        Collider[] filtered = new Collider[validCount];
        int index = 0;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
                continue;

            filtered[index++] = collider;
        }

        _handleColliders = filtered;
    }

    private bool TryGetPointerWorldPosition(PointerEventData eventData, out Vector3 worldPosition)
    {
        if (TryGetWorldPositionFromRaycast(eventData.pointerCurrentRaycast, out worldPosition))
            return true;

        if (TryGetWorldPositionFromRaycast(eventData.pointerPressRaycast, out worldPosition))
            return true;

        Transform planeTransform = panelRect != null ? panelRect : panelRoot;
        Camera eventCamera = eventData.pressEventCamera != null ? eventData.pressEventCamera : eventData.enterEventCamera;
        if (planeTransform == null || eventCamera == null)
        {
            worldPosition = Vector3.zero;
            return false;
        }

        Plane dragPlane = new Plane(planeTransform.forward, planeTransform.position);
        Ray dragRay = eventCamera.ScreenPointToRay(eventData.position);
        if (!dragPlane.Raycast(dragRay, out float enter))
        {
            worldPosition = Vector3.zero;
            return false;
        }

        worldPosition = dragRay.GetPoint(enter);
        return true;
    }

    private static bool TryGetWorldPositionFromRaycast(RaycastResult raycast, out Vector3 worldPosition)
    {
        worldPosition = raycast.worldPosition;
        if (raycast.gameObject == null)
            return false;

        return !float.IsNaN(worldPosition.x)
            && !float.IsNaN(worldPosition.y)
            && !float.IsNaN(worldPosition.z);
    }

    private Transform ResolveTrackingOrigin()
    {
        if (trackingOrigin != null)
            return trackingOrigin;

        return playerRoot;
    }
}
