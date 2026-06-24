// Dateiname muss exakt sein: HUDBuilder.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[ExecuteInEditMode]
public class HUDBuilder : MonoBehaviour
{
    [Header("Hakel an um HUD zu bauen:")]
    public bool BuildNow = false;

    static readonly Color C_POS      = new Color(0.161f, 0.969f, 0.753f, 1f);
    static readonly Color C_NEG      = new Color(1.000f, 0.200f, 0.306f, 1f);
    static readonly Color C_WHITE    = Color.white;
    static readonly Color C_DIM      = new Color(1f, 1f, 1f, 0.38f);
    static readonly Color C_VERY_DIM = new Color(1f, 1f, 1f, 0.18f);
    static readonly Color C_GLASS    = new Color(0.04f, 0.09f, 0.18f, 0.72f);
    static readonly Color C_DIVIDER  = new Color(0.161f, 0.969f, 0.753f, 0.15f);

    void OnValidate()
    {
        if (!BuildNow) return;
        BuildNow = false;
        Build();
    }

    void Build()
    {
        // Clear old children
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        BuildLeftPanel();
        BuildRightPanel();

        Debug.Log("[HUDBuilder] Fertig!");
    }

    void BuildLeftPanel()
    {
        GameObject panel = MakePanel("Panel_Left", transform,
            new Vector2(-230f, 0f), new Vector2(400f, 740f));
        AddBorders(panel, C_NEG, C_POS);

        // Efficiency Score label
        MakeTMP("Lbl_EffScore", panel.transform, "EFFICIENCY SCORE",
            16f, C_DIM, TextAlignmentOptions.Center,
            new Vector2(0f, 322f), new Vector2(360f, 28f));

        // Big number
        MakeTMP("Txt_Score", panel.transform, "50",
            100f, C_POS, TextAlignmentOptions.Center,
            new Vector2(0f, 228f), new Vector2(360f, 118f));

        // Thin glow line
        MakeImage("ScoreLine", panel.transform,
            new Vector2(0f, 165f), new Vector2(300f, 2f),
            new Color(C_POS.r, C_POS.g, C_POS.b, 0.35f));

        // Metrics
        float y = 108f;
        BuildMetric(panel.transform, "THROUGHPUT RATE",   "62%",  false, ref y);
        BuildMetric(panel.transform, "CONGESTION PENALTY","-18%", true,  ref y);
        BuildMetric(panel.transform, "NETWORK COVERAGE",  "70%",  false, ref y);

        // Divider
        MakeImage("Div1", panel.transform,
            new Vector2(0f, y - 8f), new Vector2(340f, 1f), C_DIVIDER);

        // Road Budget
        float by = y - 34f;
        MakeTMP("Lbl_Budget", panel.transform, "ROAD BUDGET",
            14f, C_DIM, TextAlignmentOptions.Center,
            new Vector2(0f, by), new Vector2(360f, 22f));
        MakeTMP("Txt_Budget", panel.transform, "340",
            78f, C_WHITE, TextAlignmentOptions.Center,
            new Vector2(0f, by - 62f), new Vector2(360f, 85f));
        MakeTMP("Lbl_Meter", panel.transform, "METER LEFT",
            12f, C_VERY_DIM, TextAlignmentOptions.Center,
            new Vector2(0f, by - 112f), new Vector2(360f, 20f));

        // Divider 2
        float d2y = by - 140f;
        MakeImage("Div2", panel.transform,
            new Vector2(0f, d2y), new Vector2(340f, 1f),
            new Color(1f, 1f, 1f, 0.07f));

        // Team labels
        float ty = d2y - 26f;
        MakeTMP("Lbl_Jammers", panel.transform, "JAMMERS \u25ba",
            14f, C_NEG, TextAlignmentOptions.Left,
            new Vector2(-110f, ty), new Vector2(150f, 22f));
        MakeTMP("Lbl_DrawZone", panel.transform, "DRAW ZONE",
            10f, C_VERY_DIM, TextAlignmentOptions.Center,
            new Vector2(0f, ty), new Vector2(110f, 22f));
        MakeTMP("Lbl_Weavers", panel.transform, "\u25c4 WEAVERS",
            14f, C_POS, TextAlignmentOptions.Right,
            new Vector2(110f, ty), new Vector2(150f, 22f));

        // Balance bar
        float bary = ty - 22f;
        GameObject balBG = MakeImage("BalBG", panel.transform,
            new Vector2(0f, bary), new Vector2(340f, 5f),
            new Color(1f, 1f, 1f, 0.06f));
        MakeImage("BalFillRed", balBG.transform,
            new Vector2(-85f, 0f), new Vector2(170f, 5f),
            new Color(C_NEG.r, C_NEG.g, C_NEG.b, 0.3f));
        MakeImage("BalFillGreen", balBG.transform,
            new Vector2(85f, 0f), new Vector2(170f, 5f),
            new Color(C_POS.r, C_POS.g, C_POS.b, 0.3f));
        MakeImage("BalCursor", balBG.transform,
            new Vector2(0f, 0f), new Vector2(10f, 10f), C_WHITE);

        MakeTMP("Lbl_Neutral", panel.transform, "NEUTRAL",
            10f, C_VERY_DIM, TextAlignmentOptions.Center,
            new Vector2(0f, bary - 18f), new Vector2(200f, 16f));
    }

    void BuildRightPanel()
    {
        GameObject panel = MakePanel("Panel_Right", transform,
            new Vector2(246f, 232f), new Vector2(258f, 196f));
        AddBorders(panel, C_POS, C_POS);

        MakeTMP("Lbl_Time", panel.transform, "TIME",
            14f, C_DIM, TextAlignmentOptions.Center,
            new Vector2(0f, 68f), new Vector2(220f, 22f));
        MakeTMP("Txt_Timer", panel.transform, "4:22",
            70f, C_WHITE, TextAlignmentOptions.Center,
            new Vector2(0f, -4f), new Vector2(220f, 84f));
        MakeTMP("Lbl_Remaining", panel.transform, "REMAINING",
            10f, C_VERY_DIM, TextAlignmentOptions.Center,
            new Vector2(0f, -68f), new Vector2(220f, 16f));
    }

    void BuildMetric(Transform parent, string label, string val, bool signed, ref float y)
    {
        MakeTMP("Name_" + label, parent, label,
            11f, C_DIM, TextAlignmentOptions.Left,
            new Vector2(-50f, y), new Vector2(220f, 18f));

        MakeTMP("Val_" + label, parent, val,
            13f, C_WHITE, TextAlignmentOptions.Right,
            new Vector2(145f, y), new Vector2(70f, 18f));

        float pct    = 0f;
        float.TryParse(val.Replace("%", "").Replace("+", ""), out pct);
        float fillW  = Mathf.Abs(pct) / 100f * 300f;
        Color fillC  = signed && pct < 0f ? C_NEG : C_POS;

        GameObject track = MakeImage("Track_" + label, parent,
            new Vector2(0f, y - 17f), new Vector2(300f, 4f),
            new Color(1f, 1f, 1f, 0.07f));
        MakeImage("Fill_" + label, track.transform,
            new Vector2(-150f + fillW * 0.5f, 0f),
            new Vector2(fillW, 4f), fillC);

        y -= 58f;
    }

    // ── Borders ──────────────────────────────────────────────────────────────
    void AddBorders(GameObject panel, Color cLeft, Color cRight)
    {
        RectTransform rt = panel.GetComponent<RectTransform>();
        float w = rt.sizeDelta.x, h = rt.sizeDelta.y;

        MakeImage("Border_Top",   panel.transform, new Vector2(0f,  h/2f), new Vector2(w, 2f), cRight);
        MakeImage("Border_Bot",   panel.transform, new Vector2(0f, -h/2f), new Vector2(w, 1f),
            new Color(cLeft.r, cLeft.g, cLeft.b, 0.25f));
        MakeImage("Border_Left",  panel.transform, new Vector2(-w/2f, 0f), new Vector2(2f, h), cLeft);
        MakeImage("Border_Right", panel.transform, new Vector2( w/2f, 0f), new Vector2(2f, h), cRight);
    }

    // ── Primitives ────────────────────────────────────────────────────────────
    GameObject MakePanel(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        Image img           = go.AddComponent<Image>();
        img.color           = C_GLASS;
        return go;
    }

    GameObject MakeImage(string name, Transform parent, Vector2 pos, Vector2 size, Color col)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        Image img           = go.AddComponent<Image>();
        img.color           = col;
        return go;
    }

    GameObject MakeTMP(string name, Transform parent, string text,
        float size, Color col, TextAlignmentOptions align, Vector2 pos, Vector2 sz)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchoredPosition = pos;
        rt.sizeDelta        = sz;
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text               = text;
        tmp.fontSize           = size;
        tmp.color              = col;
        tmp.alignment          = align;
        tmp.enableWordWrapping = false;
        tmp.overflowMode       = TextOverflowModes.Overflow;
        return go;
    }
}
