using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class DynamicLineOpacityPulse : MonoBehaviour
{
    [Header("Positions")]
    [SerializeField] private Transform startTarget;
    [SerializeField] private Transform endTarget;

    [Header("Opacity Settings")]
    [Range(0f, 1f)][SerializeField] private float minOpacity = 0.1f;
    [Range(0f, 1f)][SerializeField] private float maxOpacity = 1.0f;
    [SerializeField] private float pulseSpeed = 1.0f;

    [Header("Color Toggle Settings")]
    [SerializeField] private Color colorA = new Color32(42, 247, 93, 255);
    [SerializeField] private Color colorB = new Color32(255, 51, 78, 255);
    [SerializeField] private float colorTransitionSpeed = 2.0f;

    private LineRenderer lineRenderer;
    private Color targetColor;
    private Color currentColor;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 2;

        // Initialize with colorA
        targetColor = colorA;
        currentColor = colorA;
    }

    void Update()
    {
        // Update positions if targets are assigned
        if (startTarget != null && endTarget != null)
        {
            lineRenderer.SetPosition(0, startTarget.position);
            lineRenderer.SetPosition(1, endTarget.position);
        }


        // Smoothly transition toward the target color
        currentColor = Color.Lerp(currentColor, targetColor, Time.deltaTime * colorTransitionSpeed);

        // Calculate the pulse factor mapped to the opacity range
        float sinValue = (Mathf.Sin(Time.time * pulseSpeed) + 1.0f) / 2.0f;
        float currentOpacity = Mathf.Lerp(minOpacity, maxOpacity, sinValue);

        // Apply the calculated opacity to the current color
        Color finalColor = currentColor;
        finalColor.a = currentOpacity;

        // Apply to the Line Renderer
        lineRenderer.startColor = finalColor;
        lineRenderer.endColor = finalColor;
    }

    // Toggles the target color between ColorA and ColorB.
    public void ToggleColors()
    {
        if (targetColor == colorA)
        {
            targetColor = colorB;
        }
        else
        {
            targetColor = colorA;
        }
    }
}