using UnityEngine;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────────────────────
// TrafficNode  —  one point on the road network
//
// Think of this like a dot on a map. Roads are made of many dots placed close
// together. Each dot knows which dots come AFTER it (outgoing) and which dots
// came BEFORE it (incoming). This lets us enforce one-way traffic rules:
// a car can only follow outgoing connections, never go backwards.
//
// New concepts vs the old version:
//   • outgoing  — dots this node leads TO   (the car may travel this way)
//   • incoming  — dots that lead TO this one (used for intersection logic)
//   • laneId    — which lane this dot belongs to (0 = right, 1 = left, etc.)
//   • nodeType  — is this a normal road point, or a junction where roads meet?
//   • stopLine  — should cars stop and yield here (red light / give way)?
// ─────────────────────────────────────────────────────────────────────────────

public class TrafficNode : MonoBehaviour
{
    // ── What kind of node is this? ────────────────────────────────────────────
    public enum NodeType
    {
        Road,           // a normal point along a lane
        Intersection,   // where two or more roads cross
        StopLine        // where a car must yield or wait for a green light
    }

    [Header("Node Type")]
    public NodeType nodeType = NodeType.Road;

    // ── Connections (DIRECTED — one-way!) ─────────────────────────────────────
    // outgoing: the nodes a car at THIS node is allowed to drive towards.
    // incoming: the nodes that point HERE (maintained automatically by Connect).
    // Using two separate lists instead of one "neighbors" list means a car
    // can never accidentally drive the wrong way down a one-way road.
    [Header("Connections")]
    public List<TrafficNode> outgoing = new List<TrafficNode>();
    [HideInInspector] public List<TrafficNode> incoming = new List<TrafficNode>();

    // ── Lane identity ─────────────────────────────────────────────────────────
    // laneId tells the car which lane it's on.
    // 0 = right lane (towards work), 1 = left lane (towards home).
    // Cars must only pathfind along nodes that share their laneId,
    // except at intersections where lane changes are permitted.
    [Header("Lane Info")]
    public int laneId = 0;

    // ── Spline reference ──────────────────────────────────────────────────────
    // Which spline this node lives on, and where along it (0 = start, 1 = end).
    // CarAgent uses these to smoothly follow the curve between nodes.
    [Header("Spline Reference")]
    public int splineIndex = -1;
    [HideInInspector] public float tValue = 0f;

    // ── Traffic state ─────────────────────────────────────────────────────────
    // congestionPenalty: pathfinding adds this cost when passing through here.
    // A high value makes cars prefer alternate routes — like a traffic jam.
    //
    // isBlocked: set to true by a TrafficLight to make cars stop here.
    // Cars check this every frame in CarAgent.Update before moving.
    [Header("Traffic State")]
    public float congestionPenalty = 0f;
    public bool isBlocked = false;

    // ── Intersection yield tracking ───────────────────────────────────────────
    // How many cars are currently waiting to cross this intersection node.
    // TrafficLight or an IntersectionController will read/write this.
    [HideInInspector] public int waitingCars = 0;

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: add a one-way connection FROM this node TO 'target'.
    // Always use this method instead of adding to outgoing directly —
    // it keeps both outgoing and incoming lists in sync.
    // ─────────────────────────────────────────────────────────────────────────
    public void ConnectTo(TrafficNode target)
    {
        if (target == null || target == this) return;
        if (!outgoing.Contains(target))
        {
            outgoing.Add(target);
            target.incoming.Add(this);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: remove a connection FROM this node TO 'target'.
    // ─────────────────────────────────────────────────────────────────────────
    public void DisconnectFrom(TrafficNode target)
    {
        if (target == null) return;
        outgoing.Remove(target);
        target.incoming.Remove(this);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Draw this node in the Scene view so you can see the graph while editing.
    // Yellow sphere  = intersection node
    // Red sphere     = stop line node
    // Cyan sphere    = normal road node
    // Arrows show direction of travel.
    // ─────────────────────────────────────────────────────────────────────────
    void OnDrawGizmos()
    {
        // Pick colour by node type
        switch (nodeType)
        {
            case NodeType.Intersection: Gizmos.color = Color.yellow; break;
            case NodeType.StopLine: Gizmos.color = Color.red; break;
            default: Gizmos.color = Color.cyan; break;
        }

        float radius = nodeType == NodeType.Intersection ? 0.3f : 0.15f;
        Gizmos.DrawSphere(transform.position, radius);

        // Draw arrows for each outgoing connection so direction is visible
        Gizmos.color = Color.white;
        foreach (TrafficNode next in outgoing)
        {
            if (next == null) continue;
            Vector3 from = transform.position;
            Vector3 to = next.transform.position;
            Gizmos.DrawLine(from, to);

            // Draw a small arrowhead at the midpoint
            Vector3 mid = Vector3.Lerp(from, to, 0.6f);
            Vector3 dir = (to - from).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, dir) * 0.2f;
            Gizmos.DrawLine(mid, mid - dir * 0.3f + right);
            Gizmos.DrawLine(mid, mid - dir * 0.3f - right);
        }
    }
}