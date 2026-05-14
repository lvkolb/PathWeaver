using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;

public class CarAgent : MonoBehaviour
{
    [Header("Settings")]
    public float baseSpeed = 10f;
    public float laneOffset = 0.6f;
    public float stopDuration = 2f;

    [Header("References")]
    public TrafficNetwork network;
    public SplineContainer splineContainer;

    [Header("Navigation Goals")]
    public TrafficNode homeNode;
    public TrafficNode workNode;
    private bool headingToWork = true;

    private List<TrafficNode> currentPath = new List<TrafficNode>();
    private TrafficNode currentTarget;

    // Internal Spline State tracking
    private bool useSpline = false;
    private float travelT = 0f;
    private int splineIdx = -1;
    private float tStart, tEnd;
    private bool isWaiting = false;

    public void InitializeAgent(TrafficNode start, TrafficNode destination)
    {
        homeNode = start;
        workNode = destination;

        if (network == null) network = Object.FindAnyObjectByType<TrafficNetwork>();
        if (splineContainer == null && network != null) splineContainer = network.splineContainer;

        transform.position = start.transform.position;
        headingToWork = true;
        isWaiting = false;

        RecalculatePath();
    }

    void Update()
    {
        if (currentTarget == null || isWaiting) return;

        if (useSpline) MoveAlongSpline();
        else MoveDirectly();
    }

    private void MoveAlongSpline()
    {
        if (splineContainer == null) return;

        float length = splineContainer.Splines[splineIdx].GetLength();
        float segmentLen = Mathf.Abs(tEnd - tStart) * length;

        if (segmentLen < 0.1f) { Advance(); return; }

        travelT += (baseSpeed * Time.deltaTime) / segmentLen;
        float worldT = Mathf.Lerp(tStart, tEnd, Mathf.Clamp01(travelT));

        splineContainer.Evaluate(splineIdx, worldT, out float3 pos, out float3 fwd, out _);

        Vector3 forward = splineContainer.transform.TransformDirection((Vector3)fwd);
        if (tEnd < tStart) forward = -forward; // Invert direction vector if moving backwards (1 -> 0)

        ApplyPositionAndRotation(splineContainer.transform.TransformPoint((Vector3)pos), forward);

        if (travelT >= 1f) Advance();
    }

    private void MoveDirectly()
    {
        Vector3 dir = (currentTarget.transform.position - transform.position).normalized;
        Vector3 targetPos = currentTarget.transform.position;

        // Apply lane offset during straight cross-spline adjustments
        transform.position = Vector3.MoveTowards(transform.position, targetPos + (Vector3.Cross(Vector3.up, dir) * laneOffset), baseSpeed * Time.deltaTime);

        if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);
        if (Vector3.Distance(transform.position, targetPos) < 0.3f) Advance();
    }

    private void ApplyPositionAndRotation(Vector3 centerPos, Vector3 forward)
    {
        // Cross product guarantees the car shifts right relative to its current movement direction
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        transform.position = centerPos + (right * laneOffset);
        if (forward.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(forward);
    }

    private void Advance()
    {
        if (currentPath.Count > 0)
        {
            TrafficNode from = currentTarget != null ? currentTarget : (headingToWork ? homeNode : workNode);
            currentTarget = currentPath[0];
            currentPath.RemoveAt(0);

            // If next node shares the same spline, switch to curve-following mode
            if (from.splineIndex == currentTarget.splineIndex && from.splineIndex != -1)
            {
                splineIdx = from.splineIndex;
                tStart = from.tValue;
                tEnd = currentTarget.tValue;
                travelT = 0f;
                useSpline = true;
            }
            else useSpline = false;
        }
        else
        {
            StartCoroutine(WaitAndReturn());
        }
    }

    private IEnumerator WaitAndReturn()
    {
        isWaiting = true;
        yield return new WaitForSeconds(stopDuration);

        // Turn around logic: flip state and find shortest path back
        headingToWork = !headingToWork;
        isWaiting = false;
        RecalculatePath();
    }

    public void RecalculatePath()
    {
        TrafficNode start = currentTarget != null ? currentTarget : (headingToWork ? homeNode : workNode);
        TrafficNode destination = headingToWork ? workNode : homeNode;

        currentPath = FindShortestPath(start, destination);
        if (currentPath.Count > 0) Advance();
    }

    private List<TrafficNode> FindShortestPath(TrafficNode start, TrafficNode end)
    {
        var dist = new Dictionary<TrafficNode, float>();
        var prev = new Dictionary<TrafficNode, TrafficNode>();
        var queue = new List<TrafficNode> { start };
        dist[start] = 0;

        // Standard Dijkstra shortest path finder based on physical distance
        while (queue.Count > 0)
        {
            queue.Sort((a, b) => dist[a].CompareTo(dist[b]));
            TrafficNode curr = queue[0];
            queue.RemoveAt(0);

            if (curr == end) break;

            foreach (var next in curr.outgoing)
            {
                if (next == null) continue;
                float d = Vector3.Distance(curr.transform.position, next.transform.position);
                if (!dist.ContainsKey(next) || dist[curr] + d < dist[next])
                {
                    dist[next] = dist[curr] + d;
                    prev[next] = curr;
                    if (!queue.Contains(next)) queue.Add(next);
                }
            }
        }

        var path = new List<TrafficNode>();
        var t = end;
        while (t != null && t != start && prev.ContainsKey(t))
        {
            path.Add(t);
            t = prev[t];
        }
        path.Reverse();
        return path;
    }
}