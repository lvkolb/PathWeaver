using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;

public class TrafficNetwork : MonoBehaviour
{
    [Header("References")]
    public SplineContainer splineContainer;

    [Header("Node Spacing")]
    [Tooltip("Place one node every N world units along each spline. " +
             "Smaller = smoother paths but more nodes. 1.5–3 is a good range.")]
    public float nodeSpacing = 2f;

    [Tooltip("Two nodes closer than this are merged into one (intersection snap).")]
    public float snapRadius = 0.8f;

    [Tooltip("Sample resolution for cross-spline intersection detection. " +
             "Higher = more accurate but slower rebuild.")]
    public int intersectionSamples = 60;

    [HideInInspector] public List<TrafficNode> allNodes = new List<TrafficNode>();

    // Kept for CarAgent compatibility (cachedNodesPerSpline uses this)
    [HideInInspector] public int nodesPerSpline => Mathf.Max(1, nodesPerSplineEstimate);
    private int nodesPerSplineEstimate = 12;

    public void RebuildGraph()
    {
        // Destroy old nodes
        foreach (var n in allNodes)
            if (n != null) DestroyImmediate(n.gameObject);
        allNodes.Clear();

        if (splineContainer == null) return;

        // Pass 1: place evenly-spaced nodes along each spline
        for (int s = 0; s < splineContainer.Splines.Count; s++)
            PlaceNodesOnSpline(s);

        // Pass 2: detect geometric crossings between all spline pairs and
        // insert intersection nodes exactly where they cross
        for (int a = 0; a < splineContainer.Splines.Count; a++)
            for (int b = a + 1; b < splineContainer.Splines.Count; b++)
                InsertCrossingNodes(a, b);

        // Update estimate for CarAgent compatibility
        if (splineContainer.Splines.Count > 0)
            nodesPerSplineEstimate = Mathf.Max(1,
                allNodes.FindAll(n => n.splineIndex == 0).Count);

        Debug.Log($"<color=cyan>TrafficNetwork:</color> {allNodes.Count} nodes across " +
                  $"{splineContainer.Splines.Count} splines.");
    }

    // ── Pass 1: distance-based node placement ─────────────────────────────────
    private void PlaceNodesOnSpline(int s)
    {
        var spline = splineContainer.Splines[s];
        float length = spline.GetLength();
        if (length < 0.1f) return;

        int count = Mathf.Max(2, Mathf.CeilToInt(length / nodeSpacing));
        TrafficNode prev = null;

        for (int i = 0; i <= count; i++)
        {
            float t = i / (float)count;
            TrafficNode node = GetOrCreateNode(s, t);

            if (prev != null)
                Connect(prev, node);

            prev = node;
        }
    }

    // ── Pass 2: geometric crossing detection ─────────────────────────────────
    /// <summary>
    /// Samples both splines at <intersectionSamples> points, finds segments whose
    /// bounding boxes overlap, then does a 2D line intersection test (XZ plane).
    /// When a crossing is found it inserts a shared node at that exact world position
    /// and stitches it into both splines' neighbor chains.
    /// </summary>
    private void InsertCrossingNodes(int sA, int sB)
    {
        int N = intersectionSamples;

        // Sample world positions for both splines
        Vector3[] pA = SampleSpline(sA, N);
        Vector3[] pB = SampleSpline(sB, N);

        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                Vector3 a1 = pA[i], a2 = pA[i + 1];
                Vector3 b1 = pB[j], b2 = pB[j + 1];

                if (!SegmentsIntersectXZ(a1, a2, b1, b2,
                        out float tSeg, out float uSeg)) continue;

                Vector3 crossPos = Vector3.Lerp(a1, a2, tSeg);
                crossPos.y = (a1.y + b1.y) * 0.5f; // average height

                // Don't insert if we already have a node very close
                if (FindNearbyNode(crossPos) != null) continue;

                // t-values on each spline for the crossing
                float tA = (i + tSeg) / N;
                float tB = (j + uSeg) / N;

                // Create the crossing node — it belongs to spline A by convention
                TrafficNode crossNode = CreateNode($"Cross_S{sA}xS{sB}", crossPos, sA, tA);

                // Find the two nodes on spline A that straddle this point and rewire
                StitchNodeIntoSpline(crossNode, sA, tA);
                // Also stitch into spline B
                StitchNodeIntoSpline(crossNode, sB, tB);
            }
        }
    }

    private Vector3[] SampleSpline(int s, int segments)
    {
        var pts = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float3 pos, fwd, up;
            splineContainer.Evaluate(s, t, out pos, out fwd, out up);
            pts[i] = (Vector3)pos;
        }
        return pts;
    }

    /// <summary>
    /// Finds the two adjacent nodes on <splineIdx> that the new node sits between
    /// (by t-value) and inserts crossNode between them in both neighbor lists.
    /// </summary>
    private void StitchNodeIntoSpline(TrafficNode crossNode, int splineIdx, float t)
    {
        // Collect all nodes on this spline sorted by tValue
        var splineNodes = allNodes.FindAll(n => n.splineIndex == splineIdx);
        splineNodes.Sort((a, b) => a.tValue.CompareTo(b.tValue));

        TrafficNode before = null, after = null;
        for (int i = 0; i < splineNodes.Count - 1; i++)
        {
            if (splineNodes[i].tValue <= t && splineNodes[i + 1].tValue >= t)
            {
                before = splineNodes[i];
                after = splineNodes[i + 1];
                break;
            }
        }

        if (before == null || after == null) return;

        // Remove direct connection between before↔after
        before.neighbors.Remove(after);
        after.neighbors.Remove(before);

        // Insert crossNode between them
        Connect(before, crossNode);
        Connect(crossNode, after);

        // Give the cross node proper t-value for this spline if it was assigned to another
        // (We store only one splineIndex per node — it keeps its original assignment)
    }

    // ── 2D segment intersection (XZ plane) ───────────────────────────────────
    private bool SegmentsIntersectXZ(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4,
                                      out float t, out float u)
    {
        t = 0; u = 0;
        float d1x = p2.x - p1.x, d1z = p2.z - p1.z;
        float d2x = p4.x - p3.x, d2z = p4.z - p3.z;
        float cross = d1x * d2z - d1z * d2x;
        if (Mathf.Abs(cross) < 1e-6f) return false; // parallel

        float dx = p3.x - p1.x, dz = p3.z - p1.z;
        t = (dx * d2z - dz * d2x) / cross;
        u = (dx * d1z - dz * d1x) / cross;
        return t > 0.01f && t < 0.99f && u > 0.01f && u < 0.99f;
    }

    // ── Node factory helpers ──────────────────────────────────────────────────
    private TrafficNode GetOrCreateNode(int splineIndex, float t)
    {
        float3 pos, fwd, up;
        splineContainer.Evaluate(splineIndex, t, out pos, out fwd, out up);
        Vector3 worldPos = (Vector3)pos;

        TrafficNode existing = FindNearbyNode(worldPos);
        if (existing != null) return existing;

        return CreateNode($"Node_S{splineIndex}_t{t:F2}", worldPos, splineIndex, t);
    }

    private TrafficNode CreateNode(string name, Vector3 worldPos, int splineIndex, float t)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.position = worldPos;
        var node = go.AddComponent<TrafficNode>();
        node.splineIndex = splineIndex;
        node.tValue = t;
        allNodes.Add(node);
        return node;
    }

    private void Connect(TrafficNode a, TrafficNode b)
    {
        if (a == b) return;
        if (!a.neighbors.Contains(b)) a.neighbors.Add(b);
        if (!b.neighbors.Contains(a)) b.neighbors.Add(a);
    }

    public TrafficNode FindNearbyNode(Vector3 pos)
    {
        TrafficNode best = null;
        float bestD = snapRadius;
        foreach (var n in allNodes)
        {
            if (n == null) continue;
            float d = Vector3.Distance(n.transform.position, pos);
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    public void RegisterNode(TrafficNode newNode)
    {
        if (!allNodes.Contains(newNode)) allNodes.Add(newNode);
    }

    void OnDrawGizmos()
    {
        if (allNodes == null) return;
        foreach (var n in allNodes)
        {
            if (n == null) continue;
            // Color intersection nodes differently
            bool isIntersection = n.name.StartsWith("Cross");
            Gizmos.color = isIntersection ? Color.yellow : Color.cyan;
            Gizmos.DrawSphere(n.transform.position, isIntersection ? 0.25f : 0.15f);
            Gizmos.color = Color.cyan;
            foreach (var nb in n.neighbors)
                if (nb != null) Gizmos.DrawLine(n.transform.position, nb.transform.position);
        }
    }
}