using UnityEngine;
using UnityEngine.UI; // For Slider/Text
using TMPro; // For TextMeshPro

public class EfficiencyUI : MonoBehaviour
{
    public SessionEfficiencyTracker tracker; // Drag your tracker here

    [Header("UI Elements")]
    public TextMeshProUGUI bigScoreText;
    public Slider throughputBar;
    public Slider congestionBar;
    public Slider utilizationBar;
    public Image tugOfWarFill;
    [Header("UI Labels")]
    public TextMeshProUGUI throughputLabel;
    public TextMeshProUGUI congestionLabel;
    public TextMeshProUGUI utilizationLabel;
    public TextMeshProUGUI statusLabel;
    void Update()
    {
        if (tracker == null) return;

        // 1. Big Score
        bigScoreText.text = $"City Efficiency: {tracker.FinalScore:F0}";

        // 2. Update Slider Values (Divide by 100 to normalize 0-100 to 0.0-1.0)
        throughputBar.value = tracker.GetNormalizedThroughput(); // This is already 0-1
        congestionBar.value = tracker.GetNormalizedCongestionSlider();
        utilizationBar.value = tracker.GetNormalizedUtilization(); // This is already 0-1

        // 3. Labels (Multiplied by 100 for display)
        // Here we use the slider's current value which is already normalized 0-1
        throughputLabel.text = $"Traffic Flow: {(throughputBar.value * 100):F0}%";
        congestionLabel.text = $"Congestion Level: {(congestionBar.value * 100):F0}%";
        utilizationLabel.text = $"Network Coverage: {(utilizationBar.value * 100):F0}%";

        // 4. Status and Color
        if (tracker.FinalScore < 35)
        {
            statusLabel.text = "Status: Critical (Jammer Winning)";
            tugOfWarFill.color = Color.red;
        }
        else if (tracker.FinalScore > 55)
        {
            statusLabel.text = "Status: Optimal (Weaver Winning)";
            tugOfWarFill.color = Color.green;
        }
        else
        {
            statusLabel.text = "Status: Stable";
            tugOfWarFill.color = Color.yellow;
        }
    }
}