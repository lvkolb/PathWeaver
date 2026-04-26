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

    [Header("References")]
    public TrafficNetwork network;          // assign in Inspector
    public SplineContainer splineContainer; // same one TrafficNetwork uses

    // Path state
    private List<TrafficNode> currentPath = new List<TrafficNode>();
    private TrafficNode currentTarget;

    // Spline-smooth movement between two nodes
    private int activeSplineIndex = -1;
    private float travelT = 0f;        // 0..1 along current segment
    private float tStart, tEnd;        // t values of from/to node on their spline
    private bool useSplineMovement = false;

    private bool isWaiting = false;
    public float stopDuration = 5f; // Seconds to wait

    public void InitializeAgent()
    {
        if (homeNode != null)
        {
            transform.position = homeNode.transform.position;
            RecalculatePath();
            Debug.Log($"{gameObject.name} initialized at {homeNode.name}");
        }
        else
        {
            Debug.LogError($"{gameObject.name} has no homeNode assigned!");
        }
    }

    void Update()
    {
        if (isWaiting || currentTarget == null) return;
        if (currentTarget == null) return;
        currentSpeed = baseSpeed / (1f + currentTarget.congestionPenalty);
        if (currentSpeed <= 0.1f)
        {
            Debug.Log($"{gameObject.name} is stuck! Speed is {currentSpeed}. Base Speed is {baseSpeed}.");
        }
        if (useSplineMovement)
            MoveAlongSplineSegment();
        else
            MoveDirectly(); // fallback if nodes are on different splines
    }

    // Smooth movement along the spline between two nodes
    private void MoveAlongSplineSegment()
    {
        if (splineContainer == null || activeSplineIndex < 0) { useSplineMovement = false; return; }

        float segmentLength = Mathf.Abs(tEnd - tStart) * splineContainer.Splines[activeSplineIndex].GetLength();
        if (segmentLength < 0.01f) { AdvancePath(); return; }

        travelT += (currentSpeed * Time.deltaTime) / segmentLength;

        float worldT = Mathf.Lerp(tStart, tEnd, Mathf.Clamp01(travelT));
        float3 pos, fwd, up;
        splineContainer.Evaluate(activeSplineIndex, worldT, out pos, out fwd, out up);

        transform.position = (Vector3)pos;
        if (math.lengthsq(fwd) > 0.001f)
            transform.rotation = Quaternion.LookRotation(fwd, up);

        if (travelT >= 1f) AdvancePath();
    }

    // Fallback: straight-line to node (cross-spline hops)
    private void MoveDirectly()
    {
        Vector3 targetPos = currentTarget.transform.position;
        // Force the car to move TOWARD the node, not just "forward"
        transform.position = Vector3.MoveTowards(transform.position, targetPos, currentSpeed * Time.deltaTime);

        // Rotation
        Vector3 dir = targetPos - transform.position;
        if (dir != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 10f);
        }

        if (Vector3.Distance(transform.position, targetPos) < 0.5f)
        {
            AdvancePath();
        }
    }

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
            // We reached the end! Start the waiting process.
            if (!isWaiting)
            {
                StartCoroutine(WaitAtDestination());
            }
        }
    }

    private IEnumerator WaitAtDestination()
    {
        isWaiting = true;
        currentTarget = null; // Stop movement logic in Update

        Debug.Log($"{gameObject.name} arrived. Waiting for {stopDuration}s...");
        yield return new WaitForSeconds(stopDuration);

        // Flip destination
        headingToWork = !headingToWork;
        isWaiting = false;

        RecalculatePath();
    }

    void SetupSegment(TrafficNode from, TrafficNode to)
    {
        travelT = 0f;
        // Only use spline movement if both nodes are on the same spline AND index is NOT -1
        if (from.splineIndex == to.splineIndex && from.splineIndex != -1 && splineContainer != null)
        {
            activeSplineIndex = from.splineIndex;
            tStart = from.tValue;
            tEnd = to.tValue;
            useSplineMovement = true;
        }
        else
        {
            useSplineMovement = false; // This forces MoveDirectly() for hand-drawn roads
        }
    }

    public void RecalculatePath()
    {
        if (network == null) network = FindObjectOfType<TrafficNetwork>();

        TrafficNode destination = headingToWork ? workNode : homeNode;
        TrafficNode start = currentTarget ?? homeNode;

        if (start == null || destination == null) return;
        if (start == destination)
        {
            destination = network.allNodes[UnityEngine.Random.Range(0, network.allNodes.Count)];
            if (headingToWork) workNode = destination; else homeNode = destination;
        }
        List<TrafficNode> newPath = FindPath(start, destination);

        if (newPath == null || newPath.Count == 0)
        {
            Debug.LogWarning($"{gameObject.name} could NOT find a path from {start.name} to {destination.name}!");
        }
        else
        {
            Debug.Log($"{gameObject.name} found a path with {newPath.Count} nodes.");
        }
        if (newPath != null && newPath.Count > 0)
        {
            currentPath = newPath;
            if (currentTarget == null)
            {
                TrafficNode from = start;
                currentTarget = currentPath[0];
                currentPath.RemoveAt(0);
                SetupSegment(from, currentTarget);
            }
        }
    }
    /*   public void RecalculatePath()
    {
        if (network == null) network = FindObjectOfType<TrafficNetwork>();

        // The goal stays the same (Home or Work)
        TrafficNode destination = headingToWork ? workNode : homeNode;

        // IMPORTANT: The "Start" is the node we are currently heading to.
        // If we are waiting or don't have a target, start from our current position/home.
        TrafficNode start = currentTarget;

        if (start == null || destination == null || start == destination) return;

        List<TrafficNode> newPath = FindPath(start, destination);

        if (newPath != null && newPath.Count > 0)
        {
            // Update the path list with the new shortcut
            currentPath = newPath;
            Debug.Log($"{gameObject.name} found a new shortcut!");
        }
    }*/

    List<TrafficNode> FindPath(TrafficNode start, TrafficNode end)
    {
        if (network != null)
        {
            // Refresh local knowledge if necessary
        }
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
        {
            path.Add(temp);
            temp = cameFrom[temp];
        }
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
            {
                Gizmos.DrawLine(last, n.transform.position);
                last = n.transform.position;
            }
        }
    }
}