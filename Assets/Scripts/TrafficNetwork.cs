using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;

// ─────────────────────────────────────────────────────────────────────────────
// TrafficNetwork  —  builds the navigation graph from spline geometry
//
// HOW IT WORKS (plain English):
//   1. For every spline in the SplineContainer, walk along it in equal
//      WORLD-SPACE steps (not equal t steps — this is the key fix over the
//      old version) and place a TrafficNode at each step.
//
//   2. Connect those nodes with directed (one-way) edges so cars can only
//      travel in the spline's forward direction by default.
//
//   3. Scan every pair of splines for geometric crossings (where they
//      physically cross in the XZ plane). At each crossing, insert a shared
//      intersection node and rewire the chains on both splines through it.
//
//   4. The resulting graph is what CarAgent's pathfinder (Dijkstra) walks.
//
// WHY WORLD-SPACE STEPS MATTER:
//   A spline's t parameter (0..1) is NOT linear in world distance. A tight
//   curve and a long straight both go from t=0 to t=1, but the curve covers
//   far less distance. Sampling at equal t intervals gives bunched-up nodes
//   on curves and sparse nodes on straights — causing the "off-road" and
//   "stuck" bugs. Sampling at equal WORLD-SPACE steps fixes this.
// ─────────────────────────────────────────────────────────────────────────────

public class TrafficNetwork : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The SplineContainer that holds all your road splines.")]
    public SplineContainer splineContainer;

    [Header("Node Spacing")]
    [Tooltip("Place one node every N world units along each spline. " +
             "Smaller = smoother paths but more nodes. 1.5–3 is a good range.")]
    public float nodeSpacing = 2f;

    [Tooltip("Two nodes closer than this distance are treated as the same node " +
             "and merged. Useful at intersections where splines nearly touch.")]
    public float snapRadius = 0.5f;

    [Tooltip("How many line segments to use when scanning for spline crossings. " +
             "Higher = more accurate intersection detection, but slower rebuild.")]
    public int intersectionSamples = 80;

    // All nodes in the network — VehicleManager and CarAgent read this list.
    [HideInInspector] public List<TrafficNode> allNodes = new List<TrafficNode>();

    // How many nodes were placed on spline 0 — CarAgent uses this to decide
    // whether two consecutive nodes are "adjacent" (safe to spline-follow)
    // or "far apart" (should use direct movement instead).
    [HideInInspector] public int nodesPerSpline => Mathf.Max(1, nodesPerSplineEstimate);
    private int nodesPerSplineEstimate = 12;

    // ─────────────────────────────────────────────────────────────────────────
    // RebuildGraph — call this whenever you add or remove road splines.
    // MultiSplineDrawer calls it automatically after every mouse stroke.
    // ─────────────────────────────────────────────────────────────────────────
    public void RebuildGraph()
    {
        // Step 0: destroy all old node GameObjects and clear the list
        foreach (var n in allNodes)
            if (n != null) DestroyImmediate(n.gameObject);
        allNodes.Clear();

        if (splineContainer == null)
        {
            Debug.LogWarning("TrafficNetwork: no SplineContainer assigned.");
            return;
        }

        // Step 1: place nodes along every spline using world-space stepping
        for (int s = 0; s < splineContainer.Splines.Count; s++)
            PlaceNodesOnSpline(s);

        // Step 2: find where splines geometrically cross and insert
        //         shared intersection nodes at those exact points
        for (int a = 0; a < splineContainer.Splines.Count; a++)
            for (int b = a + 1; b < splineContainer.Splines.Count; b++)
                InsertCrossingNodes(a, b);

        // Step 3: record how many nodes are on spline 0 for CarAgent's
        //         adjacency check (used to decide between spline vs direct movement)
        if (splineContainer.Splines.Count > 0)
            nodesPerSplineEstimate = Mathf.Max(1,
                allNodes.FindAll(n => n.splineIndex == 0).Count);

        Debug.Log($"<color=cyan>TrafficNetwork rebuilt:</color> " +
                  $"{allNodes.Count} nodes across {splineContainer.Splines.Count} splines.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PlaceNodesOnSpline
    //
    // Walks along the spline in equal WORLD-SPACE steps and plants a node
    // at each step. Connects them with directed edges (forward only).
    //
    // The key fix here: we use SplineUtility.GetPointAtLinearDistance to
    // advance by a fixed world-space distance, so nodes are evenly spread
    // regardless of how curved the spline is.
    // ─────────────────────────────────────────────────────────────────────────
    private void PlaceNodesOnSpline(int splineIdx)
    {
        var spline = splineContainer.Splines[splineIdx];
        float totalLength = spline.GetLength();

        // Skip splines too short to be useful
        if (totalLength < nodeSpacing * 0.5f) return;

        // Figure out how many nodes to place
        int count = Mathf.Max(2, Mathf.RoundToInt(totalLength / nodeSpacing));

        TrafficNode prev = null;

        for (int i = 0; i <= count; i++)
        {
            // t is calculated from distance, not from index — this is what
            // keeps nodes evenly spaced in WORLD SPACE on curved splines
            float distanceAlongSpline = (i / (float)count) * totalLength;
            float t = SplineUtility.GetNormalizedInterpolation(
                spline, distanceAlongSpline, PathIndexUnit.Distance);

            // Clamp to avoid floating-point overshoot past the spline end
            t = Mathf.Clamp01(t);

            // Get the world position at this t value
            splineContainer.Evaluate(splineIdx, t, out float3 pos, out float3 fwd, out float3 up);
            Vector3 worldPos = splineContainer.transform.TransformPoint((Vector3)pos);

            // Reuse an existing node if one is already very close
            // (this handles splines that share endpoints)
            TrafficNode node = FindNearbyNode(worldPos);
            if (node == null)
                node = CreateNode($"Node_S{splineIdx}_i{i}", worldPos, splineIdx, t);

            // Connect previous node → this node (directed, forward only)
            if (prev != null)
                prev.ConnectTo(node);

            prev = node;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // InsertCrossingNodes
    //
    // Checks whether two splines cross each other geometrically (in the XZ
    // plane — height is ignored). If they do, inserts a shared intersection
    // node exactly at the crossing point and rewires both chains through it.
    //
    // Example: road A runs east-west, road B runs north-south. Where they
    // cross, this method inserts one node that both chains pass through,
    // so cars can turn from A onto B or vice versa.
    // ─────────────────────────────────────────────────────────────────────────
    private void InsertCrossingNodes(int sA, int sB)
    {
        int N = intersectionSamples;

        // Sample both splines into arrays of world-space points
        Vector3[] pA = SampleSplineWorldSpace(sA, N);
        Vector3[] pB = SampleSplineWorldSpace(sB, N);

        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < N; j++)
            {
                // Check if segment i on spline A crosses segment j on spline B
                if (!SegmentsIntersectXZ(pA[i], pA[i + 1], pB[j], pB[j + 1],
                        out float tSeg, out float uSeg))
                    continue;

                // Calculate the exact world position of the crossing
                Vector3 crossPos = Vector3.Lerp(pA[i], pA[i + 1], tSeg);
                crossPos.y = (pA[i].y + pB[j].y) * 0.5f;

                // Skip if we already placed a node very close to this crossing
                if (FindNearbyNode(crossPos) != null) continue;

                // The t-values tell us WHERE along each spline the crossing sits
                float tA = GetTAtWorldDistance(sA, (i + tSeg) / N);
                float tB = GetTAtWorldDistance(sB, (j + uSeg) / N);

                // Create the intersection node (assigned to spline A by convention)
                TrafficNode crossNode = CreateNode(
                    $"Intersection_S{sA}xS{sB}", crossPos, sA, tA);
                crossNode.nodeType = TrafficNode.NodeType.Intersection;

                // Splice this node into both spline chains so cars can turn here
                StitchIntoChain(crossNode, sA, tA);
                StitchIntoChain(crossNode, sB, tB);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // StitchIntoChain
    //
    // Finds the two nodes on a spline that sit on either side of a t-value,
    // then inserts the new node between them:
    //
    //   BEFORE:  nodeA ──► nodeB
    //   AFTER:   nodeA ──► crossNode ──► nodeB
    //
    // The old direct connection (nodeA→nodeB) is removed.
    // ─────────────────────────────────────────────────────────────────────────
    private void StitchIntoChain(TrafficNode crossNode, int splineIdx, float t)
    {
        // Collect all nodes on this spline, sorted by their t position
        var chain = allNodes.FindAll(n => n.splineIndex == splineIdx);
        chain.Sort((a, b) => a.tValue.CompareTo(b.tValue));

        TrafficNode before = null, after = null;

        for (int i = 0; i < chain.Count - 1; i++)
        {
            if (chain[i].tValue <= t && chain[i + 1].tValue >= t)
            {
                before = chain[i];
                after = chain[i + 1];
                break;
            }
        }

        // If we can't find surrounding nodes, nothing to stitch into
        if (before == null || after == null) return;

        // Remove the old direct connection
        before.DisconnectFrom(after);

        // Insert the cross node between them (directed: before → cross → after)
        before.ConnectTo(crossNode);
        crossNode.ConnectTo(after);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: convert a normalised fraction along sampled segments (0..1)
    // to the actual spline t value using world-distance mapping.
    // This corrects the t-value distortion caused by curve geometry.
    // ─────────────────────────────────────────────────────────────────────────
    private float GetTAtWorldDistance(int splineIdx, float fraction)
    {
        var spline = splineContainer.Splines[splineIdx];
        float dist = fraction * spline.GetLength();
        float t = SplineUtility.GetNormalizedInterpolation(
            spline, dist, PathIndexUnit.Distance);
        return Mathf.Clamp01(t);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: sample a spline into N+1 world-space points.
    // Used by the intersection detection loop.
    // ─────────────────────────────────────────────────────────────────────────
    private Vector3[] SampleSplineWorldSpace(int splineIdx, int segments)
    {
        var pts = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            splineContainer.Evaluate(splineIdx, t,
                out float3 pos, out float3 fwd, out float3 up);
            // Transform from spline local space to world space
            pts[i] = splineContainer.transform.TransformPoint((Vector3)pos);
        }
        return pts;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: 2D line segment intersection test (ignores Y / height).
    // Returns true if segments (p1→p2) and (p3→p4) cross.
    // t = how far along the first segment the crossing is (0..1)
    // u = how far along the second segment the crossing is (0..1)
    // ─────────────────────────────────────────────────────────────────────────
    private bool SegmentsIntersectXZ(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4,
                                      out float t, out float u)
    {
        t = 0; u = 0;
        float d1x = p2.x - p1.x, d1z = p2.z - p1.z;
        float d2x = p4.x - p3.x, d2z = p4.z - p3.z;
        float cross = d1x * d2z - d1z * d2x;

        // If cross product is near zero, lines are parallel — no intersection
        if (Mathf.Abs(cross) < 1e-6f) return false;

        float dx = p3.x - p1.x, dz = p3.z - p1.z;
        t = (dx * d2z - dz * d2x) / cross;
        u = (dx * d1z - dz * d1x) / cross;

        // Only count crossings that happen strictly inside both segments
        // (not at endpoints — those are handled by snapRadius merging)
        return t > 0.02f && t < 0.98f && u > 0.02f && u < 0.98f;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Node factory helpers
    // ─────────────────────────────────────────────────────────────────────────

    private TrafficNode CreateNode(string nodeName, Vector3 worldPos,
                                   int splineIdx, float t)
    {
        var go = new GameObject(nodeName);
        go.transform.SetParent(transform);
        go.transform.position = worldPos;
        var node = go.AddComponent<TrafficNode>();
        node.splineIndex = splineIdx;
        node.tValue = t;
        allNodes.Add(node);
        return node;
    }

    // Find an existing node within snapRadius of worldPos.
    // Returns null if none found.
    public TrafficNode FindNearbyNode(Vector3 worldPos)
    {
        TrafficNode best = null;
        float bestD = snapRadius;
        foreach (var n in allNodes)
        {
            if (n == null) continue;
            float d = Vector3.Distance(n.transform.position, worldPos);
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scene view gizmos — drawn in the Unity editor so you can see the graph
    // ─────────────────────────────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        if (allNodes == null) return;
        foreach (var n in allNodes)
        {
            if (n == null) continue;

            switch (n.nodeType)
            {
                case TrafficNode.NodeType.Intersection:
                    Gizmos.color = Color.yellow; Gizmos.DrawSphere(n.transform.position, 0.3f); break;
                case TrafficNode.NodeType.StopLine:
                    Gizmos.color = Color.red; Gizmos.DrawSphere(n.transform.position, 0.25f); break;
                default:
                    Gizmos.color = Color.cyan; Gizmos.DrawSphere(n.transform.position, 0.12f); break;
            }

            // Draw directed edges as lines (arrowheads drawn in TrafficNode.OnDrawGizmos)
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.5f);
            foreach (var nb in n.outgoing)
                if (nb != null) Gizmos.DrawLine(n.transform.position, nb.transform.position);
        }
    }
}