using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;

public class TrafficNetwork : MonoBehaviour
{
    [Header("References")]
    public SplineContainer splineContainer;

    [Header("Generation Settings")]
    public int intersectionSamples = 80;
    public float nodeSpacing = 2f;
    public float snapRadius = 0.5f;

    [HideInInspector] public List<TrafficNode> allNodes = new List<TrafficNode>();

    [ContextMenu("Generate network")]
    public void RebuildGraph()
    {
        // 1. Delete all
        foreach (var n in allNodes) if (n != null) DestroyImmediate(n.gameObject);
        allNodes.Clear();

        if (splineContainer == null || splineContainer.Splines.Count == 0) return;

        // 2. Place all normal nodes on the splines
        for (int i = 0; i < splineContainer.Splines.Count; i++)
        {
            if (splineContainer.Splines[i].GetLength() > 0.1f)
                PlaceNodesOnSpline(i);
        }

        // 3. Find all actual physical crossings and incorporate them
        for (int a = 0; a < splineContainer.Splines.Count; a++)
            for (int b = a + 1; b < splineContainer.Splines.Count; b++)
                InsertCrossingNodes(a, b);

        // 4. We correctly identify the types
        PostProcessIntersectionTypes();

        Debug.Log($"Traffic Network Rebuilt: {allNodes.Count} nodes generated.");
    }

    [ContextMenu("Clear network")]
    public void ClearNetwork()
    {
        foreach (var n in allNodes) if (n != null) DestroyImmediate(n.gameObject);
        allNodes.Clear();
    }

    private void PostProcessIntersectionTypes()
    {
        // List of all detected junctions, so that we can then find their neighbours
        List<TrafficNode> detectedIntersections = new List<TrafficNode>();

        // PART 1: Find all the intersections
        foreach (var node in allNodes)
        {
            if (node == null) continue;

            // Every newly generated node starts out as a normal road
            node.nodeType = TrafficNode.NodeType.Road;

            // If the name starts with "X_" (intersection) OR the node has more than 2 connections
            // OR another spline is snapped to it (name contains "Converted")
            if (node.name.StartsWith("X_") || node.outgoing.Count > 2 || node.incoming.Count > 2)
            {
                node.nodeType = TrafficNode.NodeType.Intersection;
                detectedIntersections.Add(node);
            }
        }

        // PART 2: Find all the immediate neighbours of these junctions
        foreach (var intersection in detectedIntersections)
        {
            // Iterate through all the nodes that this junction leads to
            foreach (var neighbor in intersection.outgoing)
            {
                // Only change this if it's a normal road (we won't overwrite any other junctions!)
                if (neighbor != null && neighbor.nodeType == TrafficNode.NodeType.Road)
                {
                    neighbor.nodeType = TrafficNode.NodeType.PreIntersection;
                    if (!neighbor.name.StartsWith("PreIntersection_")) neighbor.name = $"PreIntersection_{neighbor.name}";
                }
            }

            // Iterate through all the nodes leading to this junction
            foreach (var neighbor in intersection.incoming)
            {
                if (neighbor != null && neighbor.nodeType == TrafficNode.NodeType.Road)
                {
                    neighbor.nodeType = TrafficNode.NodeType.PreIntersection;
                    if (!neighbor.name.StartsWith("PreIntersection_")) neighbor.name = $"PreIntersection_{neighbor.name}";
                }
            }
        }
    }

    private void PlaceNodesOnSpline(int splineIdx)
    {
        var spline = splineContainer.Splines[splineIdx];
        float length = spline.GetLength();
        int count = Mathf.Max(2, Mathf.RoundToInt(length / nodeSpacing));
        TrafficNode prev = null;

        for (int i = 0; i <= count; i++)
        {
            float dist = (i / (float)count) * length;
            float t = SplineUtility.GetNormalizedInterpolation(spline, dist, PathIndexUnit.Distance);

            splineContainer.Evaluate(splineIdx, t, out float3 pos, out _, out _);
            Vector3 worldPos = splineContainer.transform.TransformPoint((Vector3)pos);

            TrafficNode node = FindNearbyNode(worldPos);
            if (node == null)
                node = CreateNode($"Node_S{splineIdx}_{i}", worldPos, splineIdx, t);

            if (prev != null)
            {
                prev.ConnectTo(node);
                node.ConnectTo(prev);
            }
            prev = node;
        }
    }

    private void InsertCrossingNodes(int sA, int sB)
    {
        Vector3[] pA = SampleSpline(sA, intersectionSamples);
        Vector3[] pB = SampleSpline(sB, intersectionSamples);

        for (int i = 0; i < intersectionSamples; i++)
        {
            for (int j = 0; j < intersectionSamples; j++)
            {
                if (IntersectXZ(pA[i], pA[i + 1], pB[j], pB[j + 1], out float tSeg, out float uSeg))
                {
                    Vector3 crossPos = Vector3.Lerp(pA[i], pA[i + 1], tSeg);

                    float tA = GetTAtFraction(sA, (i + tSeg) / intersectionSamples);
                    float tB = GetTAtFraction(sB, (j + uSeg) / intersectionSamples);

                    TrafficNode crossNode = FindNearbyNode(crossPos);

                    if (crossNode != null)
                    {
                        if (!crossNode.name.StartsWith("X_Converted_"))
                            crossNode.name = $"X_Converted_{crossNode.name}";
                    }
                    else
                    {
                        crossNode = CreateNode($"X_S{sA}xS{sB}", crossPos, sA, tA);
                        Stitch(crossNode, sA, tA);
                        Stitch(crossNode, sB, tB);
                    }
                }
            }
        }
    }

    private void Stitch(TrafficNode crossNode, int sIdx, float t)
    {
        var chain = allNodes.FindAll(n => n.splineIndex == sIdx);
        chain.Sort((a, b) => a.tValue.CompareTo(b.tValue));

        for (int i = 0; i < chain.Count - 1; i++)
        {
            if (chain[i].tValue <= t && chain[i + 1].tValue >= t)
            {
                chain[i].DisconnectFrom(chain[i + 1]);
                chain[i + 1].DisconnectFrom(chain[i]);

                chain[i].ConnectTo(crossNode);
                crossNode.ConnectTo(chain[i + 1]);
                crossNode.ConnectTo(chain[i]);
                chain[i + 1].ConnectTo(crossNode);
                break;
            }
        }
    }

    private TrafficNode CreateNode(string nName, Vector3 pos, int sIdx, float t)
    {
        GameObject go = new GameObject(nName);
        go.transform.SetParent(transform);
        go.transform.position = pos;
        TrafficNode node = go.AddComponent<TrafficNode>();
        node.splineIndex = sIdx;
        node.tValue = t;
        allNodes.Add(node);
        return node;
    }

    public TrafficNode FindNearbyNode(Vector3 worldPos)
    {
        TrafficNode best = null;
        float bestDist = float.MaxValue;
        foreach (var n in allNodes)
        {
            float d = Vector3.Distance(n.transform.position, worldPos);
            if (d < snapRadius && d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }

    private Vector3[] SampleSpline(int idx, int res)
    {
        Vector3[] pts = new Vector3[res + 1];
        for (int i = 0; i <= res; i++)
        {
            splineContainer.Evaluate(idx, i / (float)res, out float3 p, out _, out _);
            pts[i] = splineContainer.transform.TransformPoint((Vector3)p);
        }
        return pts;
    }

    private float GetTAtFraction(int idx, float f)
    {
        return SplineUtility.GetNormalizedInterpolation(splineContainer.Splines[idx], f * splineContainer.Splines[idx].GetLength(), PathIndexUnit.Distance);
    }

    private bool IntersectXZ(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, out float t, out float u)
    {
        t = u = 0;
        float det = (p2.x - p1.x) * (p4.z - p3.z) - (p2.z - p1.z) * (p4.x - p3.x);
        if (Mathf.Abs(det) < 1e-6f) return false;
        t = ((p3.x - p1.x) * (p4.z - p3.z) - (p3.z - p1.z) * (p4.x - p3.x)) / det;
        u = ((p3.x - p1.x) * (p2.z - p1.z) - (p3.z - p1.z) * (p2.x - p1.x)) / det;
        return t > 0.05f && t < 0.95f && u > 0.05f && u < 0.95f;
    }
}