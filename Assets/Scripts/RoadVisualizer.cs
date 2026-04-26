using UnityEngine;

public class RoadVisualizer : MonoBehaviour
{
    private TrafficNode node;
    public Material roadMaterial; // Assign a material in the inspector!

    void Start()
    {
        node = GetComponent<TrafficNode>();
    }

    // This draws the lines in the GAME view
    void OnPostRender()
    {
        if (node == null || node.neighbors.Count == 0) return;

        GL.Begin(GL.LINES);
        roadMaterial.SetPass(0);
        GL.Color(Color.gray);

        foreach (var neighbor in node.neighbors)
        {
            if (neighbor == null) continue;
            GL.Vertex(transform.position);
            GL.Vertex(neighbor.transform.position);
        }
        GL.End();
    }

    // This draws the lines in the SCENE view so you can see them while building
    void OnDrawGizmos()
    {
        TrafficNode n = GetComponent<TrafficNode>();
        if (n == null) return;

        Gizmos.color = Color.cyan;
        foreach (var neighbor in n.neighbors)
        {
            if (neighbor != null)
                Gizmos.DrawLine(transform.position, neighbor.transform.position);
        }
    }
}