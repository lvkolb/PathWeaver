// Dateiname: CityHUD_v2.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CityHUD_v2 : MonoBehaviour
{
    [Header("── Rohwerte (vom Kollegen gesetzt) ───────────────")]
    [Tooltip("Fahrzeuge die ihr Ziel erreicht haben, 0..100%")]
    [Range(0f, 100f)] public float vehiclesReached = 62f;

    [Tooltip("Aktive Traffic Jams, 0..100")]
    [Range(0f, 100f)] public float trafficJams = 18f;

    [Tooltip("Kartennetzwerk-Abdeckung, 0..100%")]
    [Range(0f, 100f)] public float mapCoverage = 70f;

    // Set in MultiSplineDrawer Script
    // [Tooltip("Verbleibendes Road Budget in Metern")]
    // public int roadBudget = 340;

    [Tooltip("Team Balance: -1 = Jammers führen, +1 = Weavers führen")]
    [Range(-1f, 1f)] public float teamBalance = 0f;

    [Header("── Timer ────────────────────────────────────────────")]
    public float totalTime = 300f;

    [Header("── Phasen ───────────────────────────────────────────")]
    [Tooltip("Aktuelle Phase (0=Building, 1=Jamming)")]
    public int currentPhase = 0;

    [Header("── Glow ─────────────────────────────────────────────")]
    public float borderIntensity = 2.5f;

    // ── Formel-Konstanten ─────────────────────────────────────────────────────
    // Score = (vehiclesReached × 0.5) + ((100 − trafficJams) × 0.3) + (mapCoverage × 0.2)
    // Weavers win: > 65 | Draw: 35–65 | Jammers win: < 35
    // nchanged it to 04 04 02 for more competition potential
    const float W_VEHICLES = 0.4f;
    const float W_TRAFFIC = 0.4f;
    const float W_COVERAGE = 0.2f;
    const float THRESHOLD_WIN = 65f;
    const float THRESHOLD_LOSE = 35f;

    static readonly Color C_POS = new Color(0.161f, 0.969f, 0.753f);
    static readonly Color C_NEG = new Color(1f, 0.2f, 0.306f);
    const float TRACK_W = 300f;

    // Cached refs
    TextMeshProUGUI _txtScore, _txtTimer, _txtBudget;
    TextMeshProUGUI _valTp, _valCg, _valNc;
    RectTransform _fillTp, _fillCg, _fillNc;
    RectTransform _balCursor;
    Image _balFillRed, _balFillGreen;
    TextMeshProUGUI _lblNeutral, _lblJammers, _lblWeavers;
    Image _borderTop, _borderBot, _borderLeft, _borderRight;

    // Phase display
    TextMeshProUGUI _lblPhase;

    float _displayScore, _timeLeft;
    bool _running = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        _timeLeft = totalTime;
        _displayScore = CalcScore() / 100f;
        CacheRefs();
        Refresh();
    }

    void Update()
    {
        float targetScore = CalcScore() / 100f;
        _displayScore = Mathf.Lerp(_displayScore, targetScore, Time.deltaTime * 2.5f);
        if (_running) _timeLeft = Mathf.Max(0f, _timeLeft - Time.deltaTime);
        Refresh();
    }

    // ── Score Formel ──────────────────────────────────────────────────────────
    float CalcScore()
    {
        return (vehiclesReached * W_VEHICLES)
             + ((100f - trafficJams) * W_TRAFFIC)
             + (mapCoverage * W_COVERAGE);
    }

    // ── UI Update ─────────────────────────────────────────────────────────────
    void Refresh()
    {
        float scoreRaw = CalcScore(); // 0..100
        Color scoreCol = Color.Lerp(C_NEG, C_POS, _displayScore);

        // Score number
        if (_txtScore != null)
        {
            _txtScore.text = Mathf.RoundToInt(scoreRaw).ToString();
            _txtScore.color = scoreCol;
        }

        // Budget
        // if (_txtBudget != null)
        //     _txtBudget.text = roadBudget.ToString();

        // Timer
        if (_txtTimer != null)
        {
            int m = Mathf.FloorToInt(_timeLeft / 60f);
            int s = Mathf.FloorToInt(_timeLeft % 60f);
            _txtTimer.text = m + ":" + s.ToString("D2");
            _txtTimer.color = _timeLeft < 60f ? C_NEG : Color.white;
        }

        // ── Metric bars — jede Farbe basiert auf eigenem Wert ────────────────

        // Throughput: vehiclesReached% → niedrig=rot, hoch=grün
        Color colTp = Color.Lerp(C_NEG, C_POS, vehiclesReached / 100f);
        SetBar(_valTp, _fillTp, vehiclesReached, false, colTp);

        // Congestion: trafficJams% → 0 Staus=grün, viele Staus=rot
        Color colCg = Color.Lerp(C_POS, C_NEG, trafficJams / 100f);
        // Wert negativ anzeigen damit es wie eine Penalty aussieht
        SetBar(_valCg, _fillCg, -trafficJams, true, colCg);

        // Coverage: mapCoverage% → niedrig=rot, hoch=grün
        Color colNc = Color.Lerp(C_NEG, C_POS, mapCoverage / 100f);
        SetBar(_valNc, _fillNc, mapCoverage, false, colNc);

        // ── Phase display ─────────────────────────────────────────────────────
        if (_lblPhase != null)
        {
            string[] phaseNames = { "BUILDING PHASE", "JAMMING PHASE" };
            _lblPhase.text = currentPhase < phaseNames.Length
                ? phaseNames[currentPhase] : "PHASE " + currentPhase;
            // Phase label leuchtet in Score-Farbe
            _lblPhase.color = scoreCol;
        }



        // ── Team balance / Tug-of-War ─────────────────────────────────────────
        float bal = Mathf.Clamp(teamBalance, -1f, 1f);
        float cursorX = bal * 170f;

        if (_balCursor != null)
            _balCursor.anchoredPosition = new Vector2(cursorX, 0f);

        // Jammers (Red) fill outward from Center to Left
        if (_balFillRed != null)
        {
            RectTransform rt = _balFillRed.GetComponent<RectTransform>();
            rt.pivot = new Vector2(1f, 0.5f); // Anchor pivot to the right edge of this image
            rt.anchoredPosition = new Vector2(0f, 0f); // Lock position to the dead center

            // Only give it width if Jammers are leading (bal < 0)
            float redW = bal < 0f ? Mathf.Abs(bal) * 170f : 0f;
            rt.sizeDelta = new Vector2(Mathf.Max(2f, redW), rt.sizeDelta.y);

            // Solidify the color so it's clearly visible
            _balFillRed.color = new Color(C_NEG.r, C_NEG.g, C_NEG.b, 0.8f);
        }

        // Weavers (Green) fill outward from Center to Right
        if (_balFillGreen != null)
        {
            RectTransform rt = _balFillGreen.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0f, 0.5f); // Anchor pivot to the left edge of this image
            rt.anchoredPosition = new Vector2(0f, 0f); // Lock position to the dead center

            // Only give it width if Weavers are leading (bal > 0)
            float greenW = bal > 0f ? bal * 170f : 0f;
            rt.sizeDelta = new Vector2(Mathf.Max(2f, greenW), rt.sizeDelta.y);

            // Solidify the color so it's clearly visible
            _balFillGreen.color = new Color(C_POS.r, C_POS.g, C_POS.b, 0.8f);
        }

        // Team labels
        if (_lblJammers != null)
            _lblJammers.color = new Color(C_NEG.r, C_NEG.g, C_NEG.b,
                                          bal < -0.1f ? 0.5f + Mathf.Max(0f, -bal) * 0.5f : 0.35f);
        if (_lblWeavers != null)
            _lblWeavers.color = new Color(C_POS.r, C_POS.g, C_POS.b,
                                          bal > 0.1f ? 0.5f + Mathf.Max(0f, bal) * 0.5f : 0.35f);

        // Zone label — nur Balance anzeigen, kein Win/Lose während des Spiels
        if (_lblNeutral != null)
        {
            if (Mathf.Abs(bal) < 0.15f) _lblNeutral.text = "NEUTRAL";
            else if (Mathf.Abs(bal) < 0.40f) _lblNeutral.text = "DRAW ZONE";
            else _lblNeutral.text = bal < 0f ? "JAMMERS LEAD" : "WEAVERS LEAD";
        }

        // ── Borders: HDR gradient nach Score ─────────────────────────────────
        Color glowMain = scoreCol * borderIntensity;
        glowMain.a = 1f;
        Color glowLeft = Color.Lerp(C_NEG, C_POS, _displayScore) * borderIntensity;
        glowLeft.a = 1f;

        if (_borderTop != null) _borderTop.color = glowMain;
        if (_borderRight != null) _borderRight.color = glowMain;
        if (_borderLeft != null) _borderLeft.color = glowLeft;
        if (_borderBot != null)
        {
            Color bot = glowMain; bot.a = 0.4f;
            _borderBot.color = bot;
        }
    }

    // ── Bar helper ────────────────────────────────────────────────────────────
    void SetBar(TextMeshProUGUI label, RectTransform fill, float val, bool signed, Color barColor)
    {
        if (label != null)
            label.text = signed
                ? (val >= 0f ? "+" : "") + Mathf.RoundToInt(val) + "%"
                : Mathf.RoundToInt(val) + "%";

        if (fill != null)
        {
            float w = Mathf.Abs(val) / 100f * TRACK_W;
            w = Mathf.Max(w, 2f);
            fill.sizeDelta = new Vector2(w, fill.sizeDelta.y);
            Image img = fill.GetComponent<Image>();
            if (img) img.color = barColor;
        }
    }

    // ── Ref-Suche ─────────────────────────────────────────────────────────────
    void CacheRefs()
    {
        _txtScore = FindTMP("Txt_Score");
        _txtTimer = FindTMP("Txt_Timer");
        _txtBudget = FindTMP("Txt_Budget");
        _valTp = FindTMP("Val_Throughput");
        _valCg = FindTMP("Val_Congestion");
        _valNc = FindTMP("Val_Coverage");
        _fillTp = FindRT("Fill_Throughput");
        _fillCg = FindRT("Fill_Congestion");
        _fillNc = FindRT("Fill_Coverage");
        _lblNeutral = FindTMP("Lbl_Neutral");
        _lblJammers = FindTMP("Lbl_Jammers");
        _lblWeavers = FindTMP("Lbl_Weavers");
        _lblPhase = FindTMP("Lbl_Phase");

        Transform balBG = FindTransform("BalBG");
        if (balBG != null)
        {
            _balCursor = FindChildRT(balBG, "BalCursor");
            Transform red = FindChild(balBG, "BalFillRed");
            Transform grn = FindChild(balBG, "BalFillGreen");
            if (red) _balFillRed = red.GetComponent<Image>();
            if (grn) _balFillGreen = grn.GetComponent<Image>();
        }

        Transform left = transform.Find("Panel_Left");
        if (left != null)
        {
            _borderTop = FindChildImg(left, "Border_Top");
            _borderBot = FindChildImg(left, "Border_Bot");
            _borderLeft = FindChildImg(left, "Border_Left");
            _borderRight = FindChildImg(left, "Border_Right");
        }

        Debug.Log("[CityHUD] Score:" + (_txtScore != null ? "OK" : "MISS") +
                  " FillTp:" + (_fillTp != null ? "OK" : "MISS") +
                  " FillCg:" + (_fillCg != null ? "OK" : "MISS") +
                  " FillNc:" + (_fillNc != null ? "OK" : "MISS") +
                  " BalCursor:" + (_balCursor != null ? "OK" : "MISS") +
                  " Phase:" + (_lblPhase != null ? "OK" : "MISS (optional)"));
    }

    TextMeshProUGUI FindTMP(string n)
    {
        TextMeshProUGUI[] all = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name == n) return all[i];
        return null;
    }

    RectTransform FindRT(string n)
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name == n) return all[i].GetComponent<RectTransform>();
        return null;
    }

    Transform FindTransform(string n)
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name == n) return all[i];
        return null;
    }

    Transform FindChild(Transform p, string n)
    {
        for (int i = 0; i < p.childCount; i++)
            if (p.GetChild(i).name == n) return p.GetChild(i);
        return null;
    }

    RectTransform FindChildRT(Transform p, string n)
    { Transform t = FindChild(p, n); return t ? t.GetComponent<RectTransform>() : null; }

    Image FindChildImg(Transform p, string n)
    { Transform t = FindChild(p, n); return t ? t.GetComponent<Image>() : null; }

    // ── Public API für deinen Kollegen ────────────────────────────────────────

    /// <summary>
    /// Hauptaufruf. Score wird automatisch berechnet.
    /// vehiclesReached: 0-100 (% Fahrzeuge die Ziel erreicht haben)
    /// trafficJams:     0-100 (Anzahl/Intensität aktiver Staus)
    /// mapCoverage:     0-100 (% Kartennetzabdeckung)
    /// budget:          Meter übrig
    /// balance:         -1 (Jammers) bis +1 (Weavers)
    /// vehicles:        Anzahl aktiver Fahrzeuge gerade
    /// phase:           0=Spawn, 1=Peak, 2=Endphase
    /// </summary>
    public void UpdateData(float vehiclesReached, float trafficJams, float mapCoverage,
                           int budget, float balance, int phase = 0)
    {
        this.vehiclesReached = vehiclesReached;
        this.trafficJams = trafficJams;
        this.mapCoverage = mapCoverage;
        // this.roadBudget      = budget;
        this.teamBalance = balance;
        this.currentPhase = phase;
    }

    public void StartTimer() => _running = true;
    public void PauseTimer() => _running = false;
    public void ResetTimer() { _timeLeft = totalTime; _running = true; }

    /// <summary>Gibt den aktuell berechneten Score zurück (0-100)</summary>
    public float GetScore() => CalcScore();
}
