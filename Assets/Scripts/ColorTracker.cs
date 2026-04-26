using UnityEngine;

public class ColorTracker : MonoBehaviour
{
    public WebcamInput webcam;
    public Color targetColor;
    public float threshold = 0.2f; // Sensitivity
    public RectTransform debugDot;

    [HideInInspector] public Vector2 trackPoint; // 0 to 1 coordinates

    void Update()
    {
        Color[] pixels = webcam.GetPixels();
        int w = webcam.Width;
        int h = webcam.Height;

        if (pixels == null || pixels.Length == 0) return;

        float sumX = 0;
        float sumY = 0;
        int count = 0;

        for (int y = 0; y < h; y += 4)
        {
            for (int x = 0; x < w; x += 4)
            {
                Color c = pixels[y * w + x];

                // NUR RGB vergleichen, Alpha ignorieren!
                float diffR = c.r - targetColor.r;
                float diffG = c.g - targetColor.g;
                float diffB = c.b - targetColor.b;

                // Euklidische Distanz im Farbraum
                float dist = Mathf.Sqrt(diffR * diffR + diffG * diffG + diffB * diffB);

                if (dist < threshold)
                {
                    sumX += x;
                    sumY += y; // Wir probieren hier mal das normale Y
                    count++;
                }
            }
        }

        if (count > 20) // Mindestens 20 Pixel müssen passen
        {
            // Center of Mass berechnen
            trackPoint = new Vector2(sumX / count / w, sumY / count / h);

            // Debug-Hilfe: Zeigt dir in der Console, wie viele Pixel er findet
            // Wenn hier 5000+ steht, ist der Threshold zu hoch!
            // Debug.Log("Pixel gefunden: " + count); 
        }

        if (debugDot != null)
        {
            debugDot.anchorMin = trackPoint;
            debugDot.anchorMax = trackPoint;
            debugDot.anchoredPosition = Vector2.zero;
            debugDot.gameObject.SetActive(count > 20);
        }
    }
}