using UnityEngine;

public class CityVisualsController : MonoBehaviour
{
    [Header("Backend Data Source")]
    [Tooltip("Drag the object with SessionEfficiencyTracker here")]
    public SessionEfficiencyTracker tracker;

    [Header("Frontend Displays")]
    [Tooltip("Drag the new HUD object here")]
    public CityHUD_v2 cityHUD;

    [Tooltip("Drag the SciFi Wall object here")]
    public SciFiWallController[] wallControllers;

    [Header("Additional Settings")]
    public int currentRoadBudget = 340;
    public int currentPhase = 0; // 0 = Building, 1 = Jamming

    void Update()
    {
        // Safety check: Don't do anything if the tracker is missing
        if (tracker == null) return;

        // 1. Fetch the data from your old tracker
        // Your tracker returns normalized values (0.0 to 1.0), so we multiply by 100 for the HUD
        float throughput = tracker.GetNormalizedThroughput() * 100f;
        float congestion = tracker.GetNormalizedCongestionSlider() * 100f;
        float coverage = tracker.GetNormalizedUtilization() * 100f;
        float overallScore = tracker.FinalScore;

        // 2. Calculate the "Tug-of-War" Balance (-1.0 to 1.0)
        // Based on your old logic: Jammers win < 35, Weavers win > 55
        float balance = 0f;

        if (overallScore <= 35f)
        {
            balance = -1f; // Jammers fully leading
        }
        else if (overallScore >= 55f)
        {
            balance = 1f;  // Weavers fully leading
        }
        else
        {
            // If the score is between 35 and 55, smoothly slide the cursor between -1 and 1
            balance = Mathf.InverseLerp(35f, 55f, overallScore) * 2f - 1f;
        }

        // 3. Send data to the new City HUD
        if (cityHUD != null)
        {
            cityHUD.UpdateData(
                vehiclesReached: throughput,
                trafficJams: congestion,
                mapCoverage: coverage,
                budget: currentRoadBudget,
                balance: balance,
                phase: currentPhase
            );
        }

        // 4. Send data to the SciFi Wall
        foreach (SciFiWallController wall in wallControllers)
        {
            if (wall != null)
            {
                wall.SetScorePercent(overallScore); // Updates the material on this specific wall[cite: 5]
            }
        }
    }
}