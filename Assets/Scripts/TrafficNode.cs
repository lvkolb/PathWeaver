using UnityEngine;
using System.Collections.Generic;

public class TrafficNode : MonoBehaviour
{
    public enum NodeType
    {
        Road,
        Intersection
    }

    [Header("Node Configuration")]
    public NodeType nodeType = NodeType.Road;

    [Header("Connections (One-Way)")]
    public List<TrafficNode> outgoing = new List<TrafficNode>();
    [HideInInspector] public List<TrafficNode> incoming = new List<TrafficNode>();

    [Header("Spline Data")]
    public int splineIndex = -1;
    public float tValue = 0f;

    [Header("Gizmo Arrow Settings")]
    [SerializeField] private float arrowWidth = 0.01f;
    [SerializeField] private float arrowLength = 0.015f;

    /// <summary>
    /// Establishes a synchronized one-way connection to a target node.
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

    /// <summary>
    /// Removes a connection and cleans up the target's incoming list.
    /// </summary>
    public void DisconnectFrom(TrafficNode target)
    {
        if (target == null) return;
        outgoing.Remove(target);
        target.incoming.Remove(this);
    }

    private void OnDrawGizmos()
    {
        // Color coding for clear visualization in the Scene View
        Gizmos.color = (nodeType == NodeType.Intersection) ? Color.yellow : Color.cyan;
        float radius = (nodeType == NodeType.Intersection) ? 0.03f : 0.015f;
        Gizmos.DrawSphere(transform.position, radius);

        // Draw path connections with direction arrows
        Gizmos.color = Color.white;
        foreach (TrafficNode next in outgoing)
        {
            if (next == null) continue;
            Vector3 from = transform.position;
            Vector3 to = next.transform.position;
            Gizmos.DrawLine(from, to);

            // Draw a small arrowhead at 60% of the distance
            Vector3 mid = Vector3.Lerp(from, to, 0.6f);
            Vector3 dir = (to - from).normalized;
            if (dir != Vector3.zero)
            {
                Vector3 right = Vector3.Cross(Vector3.up, dir) * arrowWidth;
                Gizmos.DrawLine(mid, mid - dir * arrowLength + right);
                Gizmos.DrawLine(mid, mid - dir * arrowLength - right);
            }
        }
    }
}