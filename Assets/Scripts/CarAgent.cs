using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Collections;

public class CarAgent : MonoBehaviour
{
    [Header("Commute Goals")]
    public TrafficNode homeNode;
    public TrafficNode workNode;
    private bool headingToWork = true;

    [Header("Movement Settings")]
    public float baseSpeed = 5f;
    public float currentSpeed;

    [Header("Two-Way Lane Settings")]
    [Tooltip("How far left/right of the spline centerline to drive. " +
             "Positive = right lane (towards work), negative = left lane (towards home). " +
             "Set to 0 to disable lane offset.")]
    public float laneOffset = 0.4f;

    [Header("References")]
    public TrafficNetwork network;
    public SplineContainer splineContainer; // SOURCE centerline container

    // Path state
    private List<TrafficNode> currentPath = new List<TrafficNode>();
    private TrafficNode currentTarget;

    // Spline-smooth movement
    private int activeSplineIndex = -1;
    private float travelT = 0f;
    private float tStart, tEnd;
    private bool useSplineMovement = false;
    private bool travellingForward = true;

    // Direct/projected movement
    private TrafficNode directFrom;

    private bool isWaiting = false;
    public float stopDuration = 5f;

    // FIX: cache nodesPerSpline so SetupSegment never calls FindObjectOfType every frame
    private int cachedNodesPerSpline = 12;

    public void InitializeAgent()
    {
        // Cache network reference and nodesPerSpline ONCE at init
        if (network == null) network = FindObjectOfType<TrafficNetwork>();
        if (network != null) cachedNodesPerSpline = network.nodesPerSpline;

        if (homeNode != null)
        {
            transform.position = GetLanePosition(homeNode.transform.position, Vector3.forward);
            RecalculatePath();
        }
        else Debug.LogError($"{gameObject.name} has no homeNode assigned!");
    }

    void Update()
    {
        if (isWaiting || currentTarget == null) return;
        currentSpeed = baseSpeed / (1f + currentTarget.congestionPenalty);

        if (useSplineMovement) MoveAlongSplineSegment();
        else MoveDirectly();
    }

    // ── Lane offset helper ────────────────────────────────────────────────────
    /// <summary>
    /// Returns a position offset perpendicular to the travel direction by laneOffset.
    /// headingToWork = right side, headingToHome = left side (two-way road).
    /// </summary>
    private Vector3 GetLanePosition(Vector3 centerPos, Vector3 forwardDir)
    {
        if (laneOffset == 0f) return centerPos;
        Vector3 right = Vector3.Cross(Vector3.up, forwardDir).normalized;
        // headingToWork drives on the right, returning drives on the left
        float side = headingToWork ? laneOffset : -laneOffset;
        return centerPos + right * side;
    }

    // ── Smooth spline movement ────────────────────────────────────────────────
    private void MoveAlongSplineSegment()
    {
        if (splineContainer == null || activeSplineIndex < 0 ||
            activeSplineIndex >= splineContainer.Splines.Count)
        { useSplineMovement = false; return; }

        float fullLength = splineContainer.Splines[activeSplineIndex].GetLength();
        float segmentLength = Mathf.Abs(tEnd - tStart) * fullLength;
        if (segmentLength < 0.01f) { AdvancePath(); return; }

        travelT += (currentSpeed * Time.deltaTime) / segmentLength;
        travelT = Mathf.Clamp01(travelT);

        float worldT = Mathf.Lerp(tStart, tEnd, travelT);
        float3 pos, fwd, up;
        splineContainer.Evaluate(activeSplineIndex, worldT, out pos, out fwd, out up);

        if (!travellingForward) fwd = -fwd;

        Vector3 centerPos = (Vector3)pos;
        Vector3 fwdV = (Vector3)fwd;

        transform.position = GetLanePosition(centerPos, fwdV);

        if (math.lengthsq(fwd) > 0.001f)
            transform.rotation = Quaternion.LookRotation(fwdV, (Vector3)up);

        if (travelT >= 1f) AdvancePath();
    }

    // ── Direct movement with spline projection for same-spline hops ──────────
    private void MoveDirectly()
    {
        // If both nodes are on the same spline, follow the curve even for non-adjacent hops
        if (directFrom != null
            && directFrom.splineIndex == currentTarget.splineIndex
            && directFrom.splineIndex != -1
            && splineContainer != null
            && directFrom.splineIndex < splineContainer.Splines.Count)
        {
            MoveDirectlyAlongSpline();
            return;
        }

        // True cross-spline hop: straight line
        Vector3 targetPos = GetLanePosition(currentTarget.transform.position,
                                            (currentTarget.transform.position - transform.position).normalized);
        transform.position = Vector3.MoveTowards(transform.position, targetPos, currentSpeed * Time.deltaTime);

        Vector3 dir = targetPos - transform.position;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(dir), Time.deltaTime * 10f);

        if (Vector3.Distance(transform.position, targetPos) < 0.15f) AdvancePath();
    }

    private void MoveDirectlyAlongSpline()
    {
        int si = directFrom.splineIndex;
        float tFrom = directFrom.tValue;
        float tTo = currentTarget.tValue;
        bool fwd = (tTo >= tFrom);

        float3 localPos = splineContainer.transform.InverseTransformPoint(transform.position);
        SplineUtility.GetNearestPoint(splineContainer.Splines[si],
                                      localPos, out _, out float currentT);

        float fullLength = splineContainer.Splines[si].GetLength();
        float remainingLen = Mathf.Abs(tTo - currentT) * fullLength;
        if (remainingLen < 0.01f) { AdvancePath(); return; }

        float step = (currentSpeed * Time.deltaTime) / fullLength;
        float nextT = fwd ? Mathf.Min(currentT + step, tTo)
                          : Mathf.Max(currentT - step, tTo);

        float3 pos, forward, up;
        splineContainer.Evaluate(si, nextT, out pos, out forward, out up);

        if (!fwd) forward = -forward;
        Vector3 fwdV = (Vector3)forward;

        transform.position = GetLanePosition((Vector3)pos, fwdV);

        if (math.lengthsq(forward) > 0.001f)
            transform.rotation = Quaternion.LookRotation(fwdV, (Vector3)up);

        if (Mathf.Abs(nextT - tTo) < 0.005f) AdvancePath();
    }

    // ── Path management ───────────────────────────────────────────────────────
    void AdvancePath()
    {
        if (currentPath.Count > 0)
        {
            TrafficNode from = currentTarget;
            currentTarget = currentPath[0];
            currentPath.RemoveAt(0);
            SetupSegment(from, currentTarget);
        }
        else
        {
            if (!isWaiting) StartCoroutine(WaitAtDestination());
        }
    }

    private IEnumerator WaitAtDestination()
    {
        isWaiting = true;
        currentTarget = null;
        yield return new WaitForSeconds(stopDuration);
        headingToWork = !headingToWork;
        isWaiting = false;
        RecalculatePath();
    }

    void SetupSegment(TrafficNode from, TrafficNode to)
    {
        travelT = 0f;
        directFrom = from;

        bool sameSpline = from.splineIndex == to.splineIndex
                       && from.splineIndex != -1
                       && splineContainer != null
                       && from.splineIndex < splineContainer.Splines.Count;

        if (sameSpline)
        {
            // FIX: use cachedNodesPerSpline — never call FindObjectOfType here
            float maxAdjacentT = 2f / cachedNodesPerSpline;
            float tDelta = Mathf.Abs(to.tValue - from.tValue);

            if (tDelta <= maxAdjacentT)
            {
                activeSplineIndex = from.splineIndex;
                tStart = from.tValue;
                tEnd = to.tValue;
                travellingForward = (tEnd >= tStart);
                useSplineMovement = true;
                return;
            }
        }

        useSplineMovement = false;
    }

    public void RecalculatePath()
    {
        if (network == null) network = FindObjectOfType<TrafficNetwork>();
        if (network != null) cachedNodesPerSpline = network.nodesPerSpline;

        TrafficNode destination = headingToWork ? workNode : homeNode;
        TrafficNode start = currentTarget ?? homeNode;

        if (start == null || destination == null) return;
        if (start == destination)
        {
            if (network.allNodes.Count == 0) return;
            destination = network.allNodes[UnityEngine.Random.Range(0, network.allNodes.Count)];
            if (headingToWork) workNode = destination; else homeNode = destination;
        }

        List<TrafficNode> newPath = FindPath(start, destination);
        if (newPath == null || newPath.Count == 0)
        { Debug.LogWarning($"{gameObject.name}: no path from {start.name} to {destination.name}"); return; }

        currentPath = newPath;
        if (currentTarget == null)
        {
            TrafficNode from = start;
            currentTarget = currentPath[0];
            currentPath.RemoveAt(0);
            SetupSegment(from, currentTarget);
        }
    }

    List<TrafficNode> FindPath(TrafficNode start, TrafficNode end)
    {
        if (start == end) return new List<TrafficNode>();

        var cameFrom = new Dictionary<TrafficNode, TrafficNode>();
        var costSoFar = new Dictionary<TrafficNode, float>();
        var frontier = new List<TrafficNode>();
        frontier.Add(start);
        costSoFar[start] = 0f;

        while (frontier.Count > 0)
        {
            frontier.Sort((a, b) => costSoFar[a].CompareTo(costSoFar[b]));
            TrafficNode current = frontier[0];
            frontier.RemoveAt(0);
            if (current == end) break;

            foreach (TrafficNode next in current.neighbors)
            {
                if (next == null) continue;
                float dist = Vector3.Distance(current.transform.position, next.transform.position);
                float newCost = costSoFar[current] + dist + next.congestionPenalty;
                if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next])
                {
                    costSoFar[next] = newCost;
                    cameFrom[next] = current;
                    if (!frontier.Contains(next)) frontier.Add(next);
                }
            }
        }

        var path = new List<TrafficNode>();
        TrafficNode temp = end;
        while (temp != start && cameFrom.ContainsKey(temp))
        { path.Add(temp); temp = cameFrom[temp]; }
        path.Reverse();
        return path;
    }

    void OnDrawGizmos()
    {
        if (currentTarget == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, currentTarget.transform.position);
        if (currentPath != null)
        {
            Gizmos.color = Color.yellow;
            Vector3 last = currentTarget.transform.position;
            foreach (var n in currentPath)
            { Gizmos.DrawLine(last, n.transform.position); last = n.transform.position; }
        }
    }
}