using UnityEngine;
using TMPro; // If using TextMeshPro

public class EfficiencyDisplay : MonoBehaviour
{
    public TextMeshProUGUI scoreText;

    void Update()
    {
        CarAgent[] cars = FindObjectsOfType<CarAgent>();
        float totalEfficiency = 0f;

        foreach (var car in cars)
        {
            // Efficiency = Current Speed / Max Speed (0 to 1 range)
            totalEfficiency += (car.currentSpeed / car.baseSpeed);
        }

        float avgEfficiency = (totalEfficiency / cars.Length) * 100f;
        // Inside Update(), add:
        bool sMode = FindObjectOfType<StreetArchitect>().isDrawingMode; // Better to cache this, but fine for PoC
        string modeText = sMode ? "<color=yellow>MODE: ARCHITECT (S)</color>" : "<color=white>MODE: OBSERVING</color>";

        // Append this to your existing scoreText
        scoreText.text = $"City Efficiency: {avgEfficiency:F0}%\n{modeText}";

        // UX: Change color based on score
        scoreText.color = Color.Lerp(Color.red, Color.green, avgEfficiency / 100f);
    }
}