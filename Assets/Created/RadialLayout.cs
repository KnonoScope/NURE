using UnityEngine;

[ExecuteAlways]
public class RadialLayout : MonoBehaviour
{
    [Header("Layout")]
    public float radius = 120f;
    public float startAngleDegrees = 90f;
    public bool clockwise = true;
    public bool includeInactive = false;
    public bool useAnchoredPosition = true;

    private void OnEnable()
    {
        ApplyLayout();
    }

    private void OnValidate()
    {
        ApplyLayout();
    }

    [ContextMenu("Apply Layout")]
    public void ApplyLayout()
    {
        int count = 0;

        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (!includeInactive && !child.gameObject.activeSelf)
                continue;
            count++;
        }

        if (count == 0)
            return;

        float step = 360f / count;
        float dir = clockwise ? -1f : 1f;

        int placed = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (!includeInactive && !child.gameObject.activeSelf)
                continue;

            float angle = startAngleDegrees + (step * placed * dir);
            float rad = angle * Mathf.Deg2Rad;

            Vector3 pos = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;

            var rect = child as RectTransform;
            if (rect != null && useAnchoredPosition)
                rect.anchoredPosition = pos;
            else
                child.localPosition = pos;

            placed++;
        }
    }
}
