using UnityEngine;

public class MouseFollower : MonoBehaviour
{
    public LayerMask groundLayer;
    private ColorTracker tracker;
    private StreetArchitect architect;
    private Renderer rend;

    [Header("Ground Mapping (5x10)")]
    public float widthX = 50f;
    public float lengthZ = 100f;

    [Header("Visual Settings")]
    public Color observationColor = Color.red;
    public Color architectColor = Color.green;
    public Vector3 observationScale = new Vector3(1f, 1f, 1f);
    public Vector3 architectScale = new Vector3(0.5f, 0.5f, 0.5f);

    [Header("Congestion Settings")]
    public float penaltyRadius = 2.5f;
    public float highPenaltyValue = 100f;

    void Start()
    {
        tracker = FindObjectOfType<ColorTracker>();
        architect = FindObjectOfType<StreetArchitect>();
        rend = GetComponent<Renderer>();
    }

    void Update()
    {
        if (tracker == null || tracker.trackPoint == Vector2.zero) return;

        // --- DIE MAPPING-LOGIK (Synchron mit deinem Setup) ---
        // Achsen tauschen (Hoch/Runter = X, Links/Rechts = Y)
        float xRaw = tracker.trackPoint.y;
        float yRaw = tracker.trackPoint.x;

        // X invertieren (falls Hand links = Punkt rechts)
        float xPercent = 1.0f - xRaw;
        float yPercent = yRaw;

        // Mapping auf den Boden (5x10 Cube)
        float xPos = (xPercent - 0.5f) * widthX;
        float zPos = (yPercent - 0.5f) * lengthZ;

        transform.position = new Vector3(xPos, 0.2f, zPos);

        // --- VISUELLE ANPASSUNG ---
        if (architect != null && architect.isDrawingMode)
        {
            rend.material.color = architectColor;
            transform.localScale = architectScale;
        }
        else
        {
            rend.material.color = observationColor;
            transform.localScale = observationScale;
        }

        ApplyCongestionToNetwork(transform.position);
    }
    void ApplyCongestionToNetwork(Vector3 blockerPos)
    {
        // Find all nodes in the scene
        // (In Unity 6, FindObjectsByType is the faster replacement for FindObjectsOfType)
        TrafficNode[] allNodes = Object.FindObjectsByType<TrafficNode>(FindObjectsSortMode.None);

        foreach (var node in allNodes)
        {
            float distance = Vector3.Distance(blockerPos, node.transform.position);

            // If the follower is near the node, make it "expensive" for the AI
            if (distance < penaltyRadius)
            {
                node.congestionPenalty = highPenaltyValue;
            }
            else
            {
                node.congestionPenalty = 0f;
            }
        }
    }
}