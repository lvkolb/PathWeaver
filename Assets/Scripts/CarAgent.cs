using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Collections;

// ─────────────────────────────────────────────────────────────────────────────
// CarAgent  —  controls a single car driving between two points on the network
//
// HOW IT WORKS (plain English):
//   1. At startup, Dijkstra pathfinding finds the cheapest route from home
//      to work through the TrafficNode graph.
//   2. The car follows that route node-by-node. Between two nodes that sit
//      on the same spline, it smoothly follows the spline curve. Between
//      nodes on different splines, it walks the curve of whichever spline
//      the FROM node belongs to until it reaches the correct t-value.
//   3. When the car reaches its destination it waits a few seconds, then
//      flips direction (home↔work) and recalculates a new path.
//
// BUGS FIXED vs the old version:
//   • Stuck / not moving: segment length check was firing too early because
//     t was not linear in world space. Fixed by using world-distance for
//     segment length, and a generous arrival threshold.
//   • Cycling nodes: Dijkstra now uses a proper "visited" set so a node
//     can never be re-expanded after it's been settled.
//   • Teleporting: cross-spline hops now always follow the spline curve of
//     the FROM node instead of cutting through empty space in a straight line.
//   • Going off-road: GetLanePosition now samples the spline tangent at the
//     car's actual position instead of using the stored forward direction,
//     so the offset stays perpendicular to the road even on tight curves.
//   • Infinite wait loop: arrival check now uses a world-distance threshold
//     instead of a t-delta threshold, so it can't trigger too early.
//   • Pathfinder returning empty path when start ≈ destination:
//     start node is now snapped to the nearest node in the graph, preventing
//     the case where the stored node reference was destroyed after a rebuild.
// ─────────────────────────────────────────────────────────────────────────────

public class CarAgent : MonoBehaviour
{
    // ── Inspector-visible settings ────────────────────────────────────────────

    [Header("Commute Goals")]
    [Tooltip("The node the car starts at and returns to.")]
    public TrafficNode homeNode;
    [Tooltip("The node the car drives to.")]
    public TrafficNode workNode;

    [Header("Movement Settings")]
    [Tooltip("Base speed in world units per second. Congestion will slow this down.")]
    public float baseSpeed = 5f;
    [Tooltip("How long (seconds) the car waits when it arrives at a destination.")]
    public float stopDuration = 5f;
    [Tooltip("Distance in world units at which the car considers itself 'arrived' at a node.")]
    public float arrivalThreshold = 0.25f;

    [Header("Two-Way Lane Settings")]
    [Tooltip("How far left/right of the spline centreline to drive. " +
             "0 = centre of the road. 0.4 is a good value for two-lane roads.")]
    public float laneOffset = 0.4f;

    [Header("References")]
    [Tooltip("The TrafficNetwork in the scene. Auto-found if left empty.")]
    public TrafficNetwork network;
    [Tooltip("The SplineContainer that holds the road geometry.")]
    public SplineContainer splineContainer;

    // ── Public state (VehicleManager reads isWaiting) ─────────────────────────
    [HideInInspector] public float currentSpeed;
    public bool isWaiting = false;

    // ── Private path state ────────────────────────────────────────────────────

    // The full list of nodes remaining in the current route.
    private List<TrafficNode> currentPath = new List<TrafficNode>();

    // The node we are currently driving TOWARDS.
    private TrafficNode currentTarget;

    // The node we were at when we started the current segment.
    // Used to know which spline to follow when crossing between nodes.
    private TrafficNode segmentFrom;

    // True = driving towards workNode, False = driving back home.
    private bool headingToWork = true;

    // Cached node count for the spline adjacency check.
    // Stored here so we never call FindObjectOfType inside Update().
    private int cachedNodesPerSpline = 12;

    // ─────────────────────────────────────────────────────────────────────────
    // InitializeAgent  —  called by VehicleManager after assigning homeNode/workNode
    // ─────────────────────────────────────────────────────────────────────────
    public void InitializeAgent()
    {
        // Auto-find the network if it wasn't set in the inspector
        if (network == null) network = FindObjectOfType<TrafficNetwork>();
        if (network != null) cachedNodesPerSpline = network.nodesPerSpline;

        if (homeNode == null)
        {
            Debug.LogError($"{gameObject.name}: no homeNode assigned!");
            return;
        }

        // Place the car at its home position
        transform.position = SampleLanePosition(homeNode.splineIndex, homeNode.tValue);
        RecalculatePath();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Update  —  called every frame by Unity
    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        // Do nothing while waiting at a destination, or if there's no target yet
        if (isWaiting || currentTarget == null) return;

        // Stop at a red light or blocked intersection
        if (currentTarget.isBlocked) return;

        // Slow down based on how congested the target node is
        // (congestionPenalty = 0 → full speed; = 1 → half speed; = 3 → quarter speed)
        currentSpeed = baseSpeed / (1f + currentTarget.congestionPenalty);

        MoveTowardsTarget();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MoveTowardsTarget
    //
    // Moves the car along the spline curve from segmentFrom to currentTarget.
    // Works for both same-spline hops and cross-spline hops.
    // ─────────────────────────────────────────────────────────────────────────
    private void MoveTowardsTarget()
    {
        if (segmentFrom == null || splineContainer == null) return;

        int si = segmentFrom.splineIndex;

        // Safety: if the spline index is invalid, fall back to straight-line movement
        if (si < 0 || si >= splineContainer.Splines.Count)
        {
            MoveStraightToTarget();
            return;
        }

        var spline = splineContainer.Splines[si];
        float length = spline.GetLength();
        if (length < 0.01f) { AdvancePath(); return; }

        // ── Find where the car currently sits on this spline ──────────────────
        // Project the car's world position onto the spline to get the current t.
        // This is robust: even if the car drifted slightly off the curve,
        // it snaps back to the nearest point on it every frame.
        Vector3 localPos = splineContainer.transform.InverseTransformPoint(transform.position);
        SplineUtility.GetNearestPoint(spline, (float3)localPos, out _, out float currentT);

        float targetT = currentTarget.tValue;
        bool goingFwd = targetT >= segmentFrom.tValue;

        // ── How far is the car from the target node in world units? ───────────
        // We use world distance for the arrival check — this fixes the old bug
        // where tiny t-deltas would trigger arrival too early on long splines.
        float remainingWorld = Mathf.Abs(targetT - currentT) * length;

        // ── Arrived? ──────────────────────────────────────────────────────────
        if (remainingWorld <= arrivalThreshold)
        {
            // Snap exactly to the target node position so there's no drift
            transform.position = SampleLanePosition(si, targetT);
            AdvancePath();
            return;
        }

        // ── Advance along the spline by speed × deltaTime ─────────────────────
        float step = (currentSpeed * Time.deltaTime) / length;
        float nextT = goingFwd
            ? Mathf.Min(currentT + step, targetT)
            : Mathf.Max(currentT - step, targetT);

        nextT = Mathf.Clamp01(nextT);

        // ── Sample position and direction at the new t ─────────────────────────
        splineContainer.Evaluate(si, nextT, out float3 pos, out float3 fwd, out float3 up);

        // If we're going backwards, flip the forward direction so the car
        // faces the way it's actually travelling
        if (!goingFwd) fwd = -fwd;

        Vector3 worldPos = splineContainer.transform.TransformPoint((Vector3)pos);
        Vector3 fwdWorld = splineContainer.transform.TransformDirection((Vector3)fwd).normalized;

        // Apply the lane offset so the car drives on the correct side of the road
        transform.position = ApplyLaneOffset(worldPos, fwdWorld);

        // Rotate the car to face the direction of travel
        if (fwdWorld.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(fwdWorld, (Vector3)up);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MoveStraightToTarget  —  fallback when spline data is unavailable.
    // Moves in a straight line; used only for cross-spline hops where the
    // FROM node has no valid spline index (e.g. a freshly-inserted intersection
    // node that hasn't been assigned to either spline yet).
    // ─────────────────────────────────────────────────────────────────────────
    private void MoveStraightToTarget()
    {
        Vector3 targetPos = currentTarget.transform.position;
        Vector3 dir = (targetPos - transform.position).normalized;

        transform.position = Vector3.MoveTowards(
            transform.position, targetPos, currentSpeed * Time.deltaTime);

        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 8f);

        if (Vector3.Distance(transform.position, targetPos) <= arrivalThreshold)
            AdvancePath();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AdvancePath  —  move to the next node in the route, or wait if done
    // ─────────────────────────────────────────────────────────────────────────
    private void AdvancePath()
    {
        if (currentPath.Count > 0)
        {
            // Pull the next node from the front of the route
            segmentFrom = currentTarget;
            currentTarget = currentPath[0];
            currentPath.RemoveAt(0);
        }
        else
        {
            // Route is finished — start waiting
            if (!isWaiting) StartCoroutine(WaitAtDestination());
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WaitAtDestination  —  pause, flip direction, recalculate
    // ─────────────────────────────────────────────────────────────────────────
    private IEnumerator WaitAtDestination()
    {
        isWaiting = true;
        currentTarget = null;
        segmentFrom = null;

        yield return new WaitForSeconds(stopDuration);

        // Swap home↔work so the car now drives back the other way
        headingToWork = !headingToWork;
        isWaiting = false;

        RecalculatePath();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RecalculatePath  —  run Dijkstra and store the result
    //
    // Safe to call at any time (VehicleManager calls it after a road rebuild).
    // If the car is currently mid-segment it keeps moving towards currentTarget
    // and the new path picks up from there.
    // ─────────────────────────────────────────────────────────────────────────
    public void RecalculatePath()
    {
        if (network == null) network = FindObjectOfType<TrafficNetwork>();
        if (network == null) return;

        cachedNodesPerSpline = network.nodesPerSpline;

        // FIX: snap start to the nearest live node in case the old reference
        // was destroyed during a graph rebuild
        TrafficNode rawStart = currentTarget ?? homeNode;
        TrafficNode start = SnapToLiveNode(rawStart);
        TrafficNode dest = headingToWork ? workNode : homeNode;
        dest = SnapToLiveNode(dest);

        if (start == null || dest == null) return;

        // If start == destination, pick a random node as a new destination
        // so the car has somewhere to go
        if (start == dest)
        {
            if (network.allNodes.Count < 2) return;
            do { dest = network.allNodes[UnityEngine.Random.Range(0, network.allNodes.Count)]; }
            while (dest == start);

            if (headingToWork) workNode = dest;
            else homeNode = dest;
        }

        List<TrafficNode> newPath = FindPath(start, dest);

        if (newPath == null || newPath.Count == 0)
        {
            Debug.LogWarning($"{gameObject.name}: no path from {start.name} to {dest.name}");
            return;
        }
        else
        {
            Debug.Log($"{gameObject.name}: Path found with {newPath.Count} nodes.");
        }
        currentPath = newPath;

        // If the car has no current target (e.g. just finished waiting),
        // immediately pull the first node off the new path and start moving
        if (currentTarget == null)
        {
            segmentFrom = start;
            currentTarget = currentPath[0];
            currentPath.RemoveAt(0);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FindPath  —  Dijkstra's algorithm on the directed TrafficNode graph
    //
    // Dijkstra finds the cheapest route from 'start' to 'end' by always
    // expanding the node with the lowest total cost first. It's guaranteed
    // to find the optimal path if all edge weights are non-negative.
    //
    // FIX vs old version: uses a proper 'settled' HashSet so each node is
    // only expanded once — this prevents the cycling/oscillation bug.
    // ─────────────────────────────────────────────────────────────────────────
    private List<TrafficNode> FindPath(TrafficNode start, TrafficNode end)
    {
        if (start == end) return new List<TrafficNode>();

        // costSoFar[node] = cheapest known cost to reach that node from start
        var costSoFar = new Dictionary<TrafficNode, float>();
        // cameFrom[node] = which node we came from on the cheapest path
        var cameFrom = new Dictionary<TrafficNode, TrafficNode>();
        // frontier = nodes we know about but haven't fully explored yet
        var frontier = new List<TrafficNode>();
        // settled = nodes whose cheapest path is finalised (never re-expand these)
        var settled = new HashSet<TrafficNode>();

        costSoFar[start] = 0f;
        frontier.Add(start);

        while (frontier.Count > 0)
        {
            // Pick the frontier node with the lowest cost
            // (A proper priority queue would be faster, but this is fine for
            //  the node counts typical in a small city road network)
            frontier.Sort((a, b) => costSoFar[a].CompareTo(costSoFar[b]));
            TrafficNode current = frontier[0];
            frontier.RemoveAt(0);

            // If we've reached the destination, stop searching
            if (current == end) break;

            // Skip nodes we've already fully processed — this is the key fix
            // that prevents the cycling bug in the old version
            if (settled.Contains(current)) continue;
            settled.Add(current);

            // Explore all outgoing edges from this node
            // (outgoing = directed edges — cars can only travel forward)
            foreach (TrafficNode next in current.outgoing)
            {
                if (next == null || settled.Contains(next)) continue;

                float edgeCost = Vector3.Distance(
                    current.transform.position, next.transform.position);
                float totalCost = costSoFar[current] + edgeCost + next.congestionPenalty;

                // Only update if we found a cheaper way to reach 'next'
                if (!costSoFar.ContainsKey(next) || totalCost < costSoFar[next])
                {
                    costSoFar[next] = totalCost;
                    cameFrom[next] = current;
                    if (!frontier.Contains(next)) frontier.Add(next);
                }
            }
        }

        // Reconstruct the path by walking backwards from end to start
        // via the cameFrom map, then reverse it so it reads start→end
        var path = new List<TrafficNode>();
        TrafficNode step = end;

        // If we never reached 'end', cameFrom won't contain it — return empty
        if (!cameFrom.ContainsKey(end) && end != start) return path;

        while (step != start && cameFrom.ContainsKey(step))
        {
            path.Add(step);
            step = cameFrom[step];
        }

        path.Reverse();
        return path;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SampleLanePosition
    //
    // Samples the spline at t and applies the lane offset so the car sits
    // on the correct side of the road. Used for initial placement and snapping.
    // ─────────────────────────────────────────────────────────────────────────
    private Vector3 SampleLanePosition(int splineIdx, float t)
    {
        if (splineContainer == null || splineIdx < 0 ||
            splineIdx >= splineContainer.Splines.Count)
            return transform.position; // fallback: don't move

        splineContainer.Evaluate(splineIdx, t,
            out float3 pos, out float3 fwd, out float3 up);

        Vector3 worldPos = splineContainer.transform.TransformPoint((Vector3)pos);
        Vector3 fwdWorld = splineContainer.transform.TransformDirection((Vector3)fwd).normalized;

        return ApplyLaneOffset(worldPos, fwdWorld);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ApplyLaneOffset
    //
    // Shifts a point sideways from the road centreline by laneOffset.
    // headingToWork → right side (+offset), headingToHome → left side (-offset).
    //
    // FIX vs old version: offset direction is derived from the spline tangent
    // that was just sampled, not stored separately. This keeps the offset
    // perpendicular to the actual road on every curve.
    // ─────────────────────────────────────────────────────────────────────────
    private Vector3 ApplyLaneOffset(Vector3 centerPos, Vector3 forwardDir)
    {
        if (laneOffset == 0f) return centerPos;

        // "right" is 90 degrees clockwise from forward, in the horizontal plane
        Vector3 right = Vector3.Cross(Vector3.up, forwardDir).normalized;
        float side = headingToWork ? laneOffset : -laneOffset;
        return centerPos + right * side;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SnapToLiveNode
    //
    // After a graph rebuild, stored TrafficNode references may point to
    // destroyed GameObjects. This finds the closest live node in the network
    // so the car can continue without crashing.
    // ─────────────────────────────────────────────────────────────────────────
    private TrafficNode SnapToLiveNode(TrafficNode candidate)
    {
        // If the candidate is still alive and in the network, use it directly
        if (candidate != null && network.allNodes.Contains(candidate))
            return candidate;

        // Otherwise find the closest live node by world position
        if (candidate == null) return null;

        return network.FindNearbyNode(candidate.transform.position)
               ?? (network.allNodes.Count > 0 ? network.allNodes[0] : null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OnDrawGizmos — draw the planned route in the Scene view
    // Red line   = current target
    // Yellow line = rest of the planned path
    // ─────────────────────────────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        if (currentTarget == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, currentTarget.transform.position);

        if (currentPath == null) return;
        Gizmos.color = Color.yellow;
        Vector3 prev = currentTarget.transform.position;
        foreach (var n in currentPath)
        {
            if (n == null) continue;
            Gizmos.DrawLine(prev, n.transform.position);
            prev = n.transform.position;
        }
    }
}