using UnityEngine;
using TMPro;

[RequireComponent(typeof(Renderer))]
public class SciFiWallController : MonoBehaviour
{
    [Header("Score (0 = negativ/rot  ·  1 = positiv/grün)")]
    [Range(0f, 1f)] public float efficiencyScore = 0.75f;

    [Header("Wie schnell der Übergang ist")]
    public float lerpSpeed = 1.5f;

    [Header("Optionales Score-Label (TextMeshPro)")]
    public TextMeshProUGUI scoreLabel;

    static readonly Color  COL_POS = new Color(0.161f, 0.969f, 0.753f); // #29F7C0
    static readonly Color  COL_NEG = new Color(1.000f, 0.200f, 0.306f); // #FF334E
    static readonly int    ID_Score = Shader.PropertyToID("_EfficiencyScore");

    Material _mat;
    float    _current;

    void Awake()
    {
        _mat     = GetComponent<Renderer>().material;
        _current = efficiencyScore;
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
            scoreLabel.color = Color.Lerp(COL_NEG, COL_POS, _current);
        }
    }

    // ── Öffentliche API ──────────────────────────────────────────────────────
    public void SetScore(float value)        => efficiencyScore = Mathf.Clamp01(value);
    public void SetScorePercent(float pct)   => SetScore(pct / 100f);

    // Beispiel-Aufruf aus deiner City-Sim:
    // wall.SetScore(cityData.energyEfficiency);   // 0..1
    // wall.SetScorePercent(trafficScore);          // 0..100
}
