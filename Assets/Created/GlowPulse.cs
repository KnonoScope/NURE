using UnityEngine;

public class GlowPulse : MonoBehaviour
{
    Material mat;
    float baseEmission = 3f;

    void Start()
    {
        mat = GetComponent<Renderer>().material;
    }

    void Update()
    {
        float pulse = Mathf.PerlinNoise(Time.time * 0.6f, 0f);
        mat.SetColor("_EmissionColor", Color.yellow * (baseEmission + pulse * 2f));
    }
}
