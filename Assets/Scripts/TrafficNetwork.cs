using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;

public class TrafficNetwork : MonoBehaviour
{
    [Header("References")]
    public SplineContainer splineContainer;

    [Header("Generation Settings")]
    public float nodeSpacing = 2f;
    public float snapRadius = 0.5f;
    public int intersectionSamples = 80;

    [HideInInspector] public List<TrafficNode> allNodes = new List<TrafficNode>();

    /// <summary>
    /// Wipes the existing network and generates a brand new path graph.
    /// </summary>
    public void RebuildGraph()
    {
        foreach (var n in allNodes)
            if (n != null) DestroyImmediate(n.gameObject);

        allNodes.Clear();
        if (splineContainer == null) return;

        // Step 1: Bake nodes down the center of each spline path
        for (int i = 0; i < splineContainer.Splines.Count; i++)
            PlaceNodesOnSpline(i);

        // Step 2: Scan for physical overlapping roads and create intersection nodes
        for (int a = 0; a < splineContainer.Splines.Count; a++)
            for (int b = a + 1; b < splineContainer.Splines.Count; b++)
                InsertCrossingNodes(a, b);

        Debug.Log($"Traffic Network Rebuilt: {allNodes.Count} nodes generated.");
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
                prev.ConnectTo(node); // Two-way traffic setup
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
                    if (FindNearbyNode(crossPos) != null) continue;

                    float tA = GetTAtFraction(sA, (i + tSeg) / intersectionSamples);
                    float tB = GetTAtFraction(sB, (j + uSeg) / intersectionSamples);

                    TrafficNode crossNode = CreateNode($"X_S{sA}xS{sB}", crossPos, sA, tA);
                    crossNode.nodeType = TrafficNode.NodeType.Intersection;

                    Stitch(crossNode, sA, tA);
                    Stitch(crossNode, sB, tB);
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
                chain[i].ConnectTo(crossNode);
                crossNode.ConnectTo(chain[i + 1]);
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

    public TrafficNode FindNearbyNode(Vector3 pos)
    {
        foreach (var n in allNodes)
            if (Vector3.Distance(n.transform.position, pos) < snapRadius) return n;
        return null;
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