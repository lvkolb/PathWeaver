using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;

public class TrafficNetwork : MonoBehaviour
{
    [Header("References")]
    public SplineContainer splineContainer;

    [Header("Settings")]
    [Tooltip("How many nodes to place per spline segment")]
    public int nodesPerSpline = 8;
    public float intersectionSnapRadius = 2f;

    [HideInInspector] public List<TrafficNode> allNodes = new List<TrafficNode>();

    // Call this whenever RandomSplineRoadArchitect finishes drawing
    public void RebuildGraph()
    {
        // 1. Destroy old nodes
        foreach (var n in allNodes)
            if (n != null) DestroyImmediate(n.gameObject);
        allNodes.Clear();

        if (splineContainer == null) return;

        // 2. Place nodes along each spline
        for (int s = 0; s < splineContainer.Splines.Count; s++)
        {
            var spline = splineContainer.Splines[s];
            float length = spline.GetLength();
            if (length < 0.1f) continue;

            TrafficNode prev = null;
            for (int i = 0; i <= nodesPerSpline; i++)
            {
                float t = i / (float)nodesPerSpline;
                float3 pos, fwd, up;
                splineContainer.Evaluate(s, t, out pos, out fwd, out up);

                // Check if a node already exists nearby (intersection)
                TrafficNode node = FindNearbyNode((Vector3)pos);
                if (node == null)
                {
                    GameObject go = new GameObject($"Node_S{s}_T{i}");
                    go.transform.SetParent(transform);
                    go.transform.position = (Vector3)pos;
                    node = go.AddComponent<TrafficNode>();
                    node.splineIndex = s;
                    node.tValue = t;
                    allNodes.Add(node);
                }

                // Connect to previous node on this spline (one-directional)
                if (prev != null && !prev.neighbors.Contains(node))
                {
                    prev.neighbors.Add(node);
                    node.neighbors.Add(prev); // Add this line to make it two-way!
                }

                prev = node;
            }
        }

        Debug.Log($"<color=cyan>TrafficNetwork:</color> Built {allNodes.Count} nodes across {splineContainer.Splines.Count} splines.");
    }

    TrafficNode FindNearbyNode(Vector3 pos)
    {
        foreach (var n in allNodes)
            if (Vector3.Distance(n.transform.position, pos) < intersectionSnapRadius)
                return n;
        return null;
    }
    public void RegisterNode(TrafficNode newNode)
{
    if (!allNodes.Contains(newNode)) allNodes.Add(newNode);
}
    // Call from RandomSplineRoadArchitect after generating
    // or hook up via event
    void OnDrawGizmos()
    {
        if (allNodes == null) return;
        Gizmos.color = Color.cyan;
        foreach (var n in allNodes)
        {
            if (n == null) continue;
            Gizmos.DrawSphere(n.transform.position, 0.3f);
            foreach (var nb in n.neighbors)
                if (nb != null) Gizmos.DrawLine(n.transform.position, nb.transform.position);
        }
    }
}