using UnityEngine;
using System.Collections.Generic;

public class TrafficNode : MonoBehaviour
{
    public enum NodeType
    {
        Road,
        Intersection,
        StopLine
    }

    [Header("Node Configuration")]
    public NodeType nodeType = NodeType.Road;
    public int laneId = 0;

    [Header("Connections (One-Way)")]
    public List<TrafficNode> outgoing = new List<TrafficNode>();
    [HideInInspector] public List<TrafficNode> incoming = new List<TrafficNode>();

    [Header("Spline Data")]
    public int splineIndex = -1;
    public float tValue = 0f;

    [Header("Dynamic Traffic State")]
    public float congestionPenalty = 0f;
    public bool isBlocked = false;
    [HideInInspector] public int waitingCars = 0;

    /// <summary>
    /// Connects this node to a target node, maintaining the one-way relationship.
    /// </summary>
    public void ConnectTo(TrafficNode target)
    {
        if (target == null || target == this) return;
        if (!outgoing.Contains(target))
        {
            outgoing.Add(target);
            if (!target.incoming.Contains(this))
                target.incoming.Add(this);
        }
    }

    public void DisconnectFrom(TrafficNode target)
    {
        if (target == null) return;
        outgoing.Remove(target);
        target.incoming.Remove(this);
    }

    private void OnDrawGizmos()
    {
        switch (nodeType)
        {
            case NodeType.Intersection: Gizmos.color = Color.yellow; break;
            case NodeType.StopLine: Gizmos.color = Color.red; break;
            default: Gizmos.color = Color.cyan; break;
        }

        float radius = (nodeType == NodeType.Intersection) ? 0.3f : 0.15f;
        Gizmos.DrawSphere(transform.position, radius);

        Gizmos.color = Color.white;
        foreach (TrafficNode next in outgoing)
        {
            if (next == null) continue;
            Vector3 from = transform.position;
            Vector3 to = next.transform.position;
            Gizmos.DrawLine(from, to);

            // Draw direction arrow
            Vector3 mid = Vector3.Lerp(from, to, 0.6f);
            Vector3 dir = (to - from).normalized;
            if (dir != Vector3.zero)
            {
                Vector3 right = Vector3.Cross(Vector3.up, dir) * 0.2f;
                Gizmos.DrawLine(mid, mid - dir * 0.3f + right);
                Gizmos.DrawLine(mid, mid - dir * 0.3f - right);
            }
        }
    }
}