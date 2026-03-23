using UnityEngine;
#if UNITY_XR_HANDS
using System.Collections.Generic;
using UnityEngine.XR.Hands;
#endif

public static class XRHandsPinchUtility
{
    public enum HandSide
    {
        Left,
        Right,
    }

    public struct PinchData
    {
        public bool isTracked;
        public Vector3 thumbTipWorld;
        public Vector3 indexTipWorld;
        public Vector3 pinchWorld;
        public float pinchDistance;
    }

#if UNITY_XR_HANDS
    private static readonly List<XRHandSubsystem> s_HandSubsystems = new List<XRHandSubsystem>(4);
#endif

    public static bool TryGetPinchData(HandSide side, Transform trackingOrigin, out PinchData data)
    {
        data = default;

#if UNITY_XR_HANDS
        s_HandSubsystems.Clear();
        SubsystemManager.GetSubsystems(s_HandSubsystems);

        Transform head = Camera.main != null ? Camera.main.transform : null;
        for (int i = 0; i < s_HandSubsystems.Count; i++)
        {
            XRHandSubsystem subsystem = s_HandSubsystems[i];
            if (subsystem == null || !subsystem.running)
                continue;

            XRHand hand = side == HandSide.Left ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked)
                continue;

            XRHandJoint thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);
            XRHandJoint indexTip = hand.GetJoint(XRHandJointID.IndexTip);
            if (!thumbTip.TryGetPose(out Pose thumbPose) || !indexTip.TryGetPose(out Pose indexPose))
                continue;

            Vector3 thumbWorld = ResolveLikelyWorldPosition(thumbPose.position, trackingOrigin, head);
            Vector3 indexWorld = ResolveLiklyWorldPosition(indexPose.position, trackingOrigin, head);

            data.isTracked = true;
            data.thumbTipWorld = thumbWorld;
            data.indexTipWorld = indexWorld;
            data.pinchWorld = Vector3.Lerp(thumbWorld, indexWorld, 0.5f);
            data.pinchDistance = Vector3.Distance(thumbWorld, indexWorld);
            return true;
        }
#endif

        return false;
    }

    private static Vector3 ResolveLikelyWorldPosition(Vector3 rawPosition, Transform trackingOrigin, Transform head)
    {
        if (trackingOrigin == null)
            return rawPosition;

        Vector3 convertedPosition = trackingOrigin.TransformPoint(rawPosition);
        if (head == null)
            return convertedPosition;

        float rawDistance = (rawPosition - head.position).sqrMagnitude;
        float convertedDistance = (convertedPosition - head.position).sqrMagnitude;
        return convertedDistance + 0.000001f < rawDistance ? convertedPosition : rawPosition;
    }

    private static Vector3 ResolveLiklyWorldPosition(Vector3 rawPosition, Transform trackingOrigin, Transform head)
    {
        return ResolveLikelyWorldPosition(rawPosition, trackingOrigin, head);
    }
}
