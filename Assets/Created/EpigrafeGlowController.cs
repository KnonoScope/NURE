using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public class EpigrafeGlowController : MonoBehaviour
{
    [Header("Glow")]
    public Color glowColor = new Color(1f, 0.83f, 0.36f, 1f);
    [Min(0f)] public float minEmission = 2.2f;
    [Min(0f)] public float maxEmission = 4.8f;
    [Min(0.01f)] public float pulseSpeed = 0.85f;

    [Header("Sparkle")]
    [Range(0f, 2f)] public float sparkleBoost = 0.6f;
    [Min(0.01f)] public float sparkleSpeed = 1.7f;
    [Range(0f, 0.5f)] public float subtleFlicker = 0.12f;
    [Range(0f, 3f)] public float preEntranceSparkleBoost = 1.3f;
    [Min(0.1f)] public float preEntranceSparkleSpeedMultiplier = 2.4f;
    [Min(0f)] public float preEntranceEmissionLift = 0.75f;

    [Header("Surface")]
    [Range(0f, 1f)] public float smoothness = 0.82f;
    [Range(0f, 1f)] public float metallic = 0.18f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
    private static readonly int SpecularHighlightsId = Shader.PropertyToID("_SpecularHighlights");
    private static readonly int EnvironmentReflectionsId = Shader.PropertyToID("_EnvironmentReflections");

    private Material _runtimeMaterial;
    private float _preEntranceSparkleUntil;

    private void Awake()
    {
        Renderer rendererComponent = GetComponent<Renderer>();
        if (rendererComponent == null || rendererComponent.sharedMaterial == null)
            return;

        // Use a per-renderer instance so the animation affects only this epigraph.
        _runtimeMaterial = rendererComponent.material;
        _runtimeMaterial.EnableKeyword("_EMISSION");

        if (_runtimeMaterial.HasProperty(SmoothnessId))
            _runtimeMaterial.SetFloat(SmoothnessId, smoothness);
        if (_runtimeMaterial.HasProperty(MetallicId))
            _runtimeMaterial.SetFloat(MetallicId, metallic);
        if (_runtimeMaterial.HasProperty(SpecularHighlightsId))
            _runtimeMaterial.SetFloat(SpecularHighlightsId, 1f);
        if (_runtimeMaterial.HasProperty(EnvironmentReflectionsId))
            _runtimeMaterial.SetFloat(EnvironmentReflectionsId, 1f);
    }

    private void Update()
    {
        if (_runtimeMaterial == null)
            return;

        float t = Time.time;
        bool preEntranceSparkle = Time.time < _preEntranceSparkleUntil;
        float currentPulseSpeed = pulseSpeed * (preEntranceSparkle ? 1.35f : 1f);
        float currentSparkleSpeed = sparkleSpeed * (preEntranceSparkle ? preEntranceSparkleSpeedMultiplier : 1f);
        float currentSparkleBoost = sparkleBoost + (preEntranceSparkle ? preEntranceSparkleBoost : 0f);

        float pulse01 = 0.5f + 0.5f * Mathf.Sin(t * currentPulseSpeed * Mathf.PI * 2f);
        float pulse = Mathf.Lerp(minEmission, maxEmission, pulse01) + (preEntranceSparkle ? preEntranceEmissionLift : 0f);

        float sparkleNoise = Mathf.PerlinNoise(0.173f, t * currentSparkleSpeed);
        float sparkle = Mathf.SmoothStep(0f, currentSparkleBoost, Mathf.Max(0f, sparkleNoise - 0.75f) * 4f);

        float flickerNoise = Mathf.PerlinNoise(t * 3.7f, 0.619f) - 0.5f;
        float flicker = flickerNoise * 2f * subtleFlicker;

        float finalIntensity = Mathf.Max(0f, pulse + sparkle + flicker);
        _runtimeMaterial.SetColor(EmissionColorId, glowColor * finalIntensity);
    }

    public void PlayPreEntranceSparkle(float duration)
    {
        _preEntranceSparkleUntil = Mathf.Max(_preEntranceSparkleUntil, Time.time + Mathf.Max(0f, duration));
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
    }
}
