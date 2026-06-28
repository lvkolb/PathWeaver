using UnityEngine;
using TMPro;

/// <summary>
/// Attach to your wall GameObject (must have Renderer + SciFiGlassWall_v6 material).
/// Automatically sets _AspectRatio from the object's world scale.
/// Call SetScore(0..1) or SetScorePercent(0..100) from your city sim.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class SciFiWallController : MonoBehaviour
{
    [Header("Score  (0 = rot / schlecht  ·  1 = grün / gut)")]
    [Range(0f, 1f)] public float efficiencyScore = 0.75f;

    [Header("Übergangsgeschwindigkeit")]
    public float lerpSpeed = 1.5f;

    [Header("Optionales Score-Label")]
    public TextMeshProUGUI scoreLabel;

    // Shader property IDs
    static readonly int ID_Score  = Shader.PropertyToID("_EfficiencyScore");
    static readonly int ID_Aspect = Shader.PropertyToID("_AspectRatio");

    Material _mat;
    float    _current;

    void Awake()
    {
        // Instance material so walls are independent
        _mat     = GetComponent<Renderer>().material;
        _current = efficiencyScore;

        // Auto-set aspect ratio from world scale (width / height)
        // Works for Quads and Planes scaled non-uniformly
        Vector3 s    = transform.lossyScale;
        float   aspect = (s.y > 0.001f) ? Mathf.Abs(s.x / s.y) : 1.777f;
        _mat.SetFloat(ID_Aspect, aspect);

        Apply();
    }

    void Update()
    {
        _current = Mathf.Lerp(_current, efficiencyScore, Time.deltaTime * lerpSpeed);
        Apply();
    }

    void Apply()
    {
        _mat.SetFloat(ID_Score, _current);

        if (scoreLabel != null)
        {
            scoreLabel.text  = $"{_current * 100f:F0}%";
            Color c = Color.Lerp(
                new Color(1f, 0.2f, 0.306f),   // #FF334E
                new Color(0.161f, 0.969f, 0.753f), // #29F7C0
                _current);
            scoreLabel.color = c;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────
    public void SetScore(float value)       => efficiencyScore = Mathf.Clamp01(value);
    public void SetScorePercent(float pct)  => SetScore(pct / 100f);
}
