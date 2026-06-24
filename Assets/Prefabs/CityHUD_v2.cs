using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Auf HUD_Canvas ziehen NACHDEM HUDBuilder das Layout erstellt hat.
/// Findet alle Elemente automatisch per Name — kein manuelles Verknüpfen nötig!
/// </summary>
public class CityHUD_v2 : MonoBehaviour
{
    [Header("Wird automatisch aus der Sim gesetzt")]
    [Range(0,1)] public float efficiencyScore = 0.5f;
    public float throughput  =  62f;
    public float congestion  = -18f;
    public float coverage    =  70f;
    public int   roadBudget  =  340;
    public float teamBalance =  0f;   // -1 Jammers .. +1 Weavers
    public float totalTime   = 300f;

    static readonly Color C_POS = new Color(0.161f, 0.969f, 0.753f);
    static readonly Color C_NEG = new Color(1f, 0.2f, 0.306f);

    // Cached refs — found automatically
    TextMeshProUGUI _score, _timer, _budget;
    TextMeshProUGUI _valTp, _valCg, _valNc;
    RectTransform   _fillTp, _fillCg, _fillNc;
    RectTransform   _balCursor;
    TextMeshProUGUI _zoneLabel;
    Image           _borderLeft, _borderRight;

    float _displayScore, _timeLeft;
    bool  _running = true;

    void Start()
    {
        _timeLeft = totalTime;
        FindRefs();
        Apply();
    }

    void Update()
    {
        _displayScore = Mathf.Lerp(_displayScore, efficiencyScore, Time.deltaTime * 2f);
        if (_running) _timeLeft = Mathf.Max(0, _timeLeft - Time.deltaTime);
        Apply();
    }

    void FindRefs()
    {
        _score     = Find<TextMeshProUGUI>("Txt_Score");
        _timer     = Find<TextMeshProUGUI>("Txt_Timer");
        _budget    = Find<TextMeshProUGUI>("Txt_Budget");
        _valTp     = Find<TextMeshProUGUI>("Val_THROUGHPUT RATE");
        _valCg     = Find<TextMeshProUGUI>("Val_CONGESTION PENALTY");
        _valNc     = Find<TextMeshProUGUI>("Val_NETWORK COVERAGE");
        _fillTp    = FindRT("Fill_THROUGHPUT RATE");
        _fillCg    = FindRT("Fill_CONGESTION PENALTY");
        _fillNc    = FindRT("Fill_NETWORK COVERAGE");
        _balCursor = FindRT("BalCursor");
        _zoneLabel = Find<TextMeshProUGUI>("Lbl_Neutral");
        _borderLeft  = FindInPanel<Image>("Panel_Left",  "Border_Left");
        _borderRight = FindInPanel<Image>("Panel_Left",  "Border_Right");
    }

    void Apply()
    {
        Color c = Color.Lerp(C_NEG, C_POS, _displayScore);

        if (_score  != null) { _score.text = Mathf.RoundToInt(_displayScore*100).ToString(); _score.color = c; }
        if (_budget != null)   _budget.text = roadBudget.ToString();

        // Timer
        if (_timer != null)
        {
            int m = Mathf.FloorToInt(_timeLeft/60), s = Mathf.FloorToInt(_timeLeft%60);
            _timer.text  = $"{m}:{s:D2}";
            _timer.color = _timeLeft < 60f ? C_NEG : Color.white;
        }

        // Metrics
        SetMetric(_valTp, _fillTp, throughput,  0, 340, false);
        SetMetric(_valCg, _fillCg, congestion, -100, 340, true);
        SetMetric(_valNc, _fillNc, coverage,    0, 340, false);

        // Team balance bar cursor
        if (_balCursor != null)
            _balCursor.anchoredPosition = new Vector2(teamBalance * 170f, 0);

        // Zone label
        if (_zoneLabel != null)
        {
            if (Mathf.Abs(teamBalance) < 0.15f)      _zoneLabel.text = "NEUTRAL";
            else if (Mathf.Abs(teamBalance) < 0.4f)  _zoneLabel.text = "DRAW ZONE";
            else _zoneLabel.text = teamBalance < 0 ? "JAMMERS LEAD" : "WEAVERS LEAD";
        }

        // Border color
        if (_borderLeft  != null) _borderLeft.color  = new Color(C_NEG.r, C_NEG.g, C_NEG.b, 0.6f + _displayScore * -0.3f + 0.3f);
        if (_borderRight != null) _borderRight.color = new Color(c.r, c.g, c.b, 0.7f);
    }

    void SetMetric(TextMeshProUGUI label, RectTransform fill, float val, float min, float maxW, bool signed)
    {
        if (label != null)
            label.text = signed ? $"{(val>=0?"+":"")}{val:F0}%" : $"{val:F0}%";
        if (fill != null)
        {
            float pct = Mathf.Abs(val) / 100f;
            float w   = pct * maxW;
            fill.sizeDelta        = new Vector2(w, fill.sizeDelta.y);
            fill.anchoredPosition = new Vector2(-maxW/2f + w/2f, 0);
            var img = fill.GetComponent<Image>();
            if (img) img.color = val < 0 ? C_NEG : C_POS;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public void UpdateData(float score, float tp, float cg, float nc, int budget, float balance)
    {
        efficiencyScore = Mathf.Clamp01(score);
        throughput      = tp; congestion = cg; coverage = nc;
        roadBudget      = budget; teamBalance = balance;
    }
    public void SetScore(float v)      => efficiencyScore = Mathf.Clamp01(v);
    public void SetScorePercent(float p) => SetScore(p/100f);
    public void StartTimer()  => _running = true;
    public void PauseTimer()  => _running = false;

    // ── Helpers ───────────────────────────────────────────────────────────────
    T Find<T>(string n) where T : Component
        => GetComponentsInChildren<T>(true).Find(x => x.name == n);
    RectTransform FindRT(string n)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == n) return t.GetComponent<RectTransform>();
        return null;
    }
    T FindInPanel<T>(string panel, string child) where T : Component
    {
        var p = transform.Find(panel);
        if (p == null) return null;
        var c = p.Find(child);
        return c != null ? c.GetComponent<T>() : null;
    }
}
