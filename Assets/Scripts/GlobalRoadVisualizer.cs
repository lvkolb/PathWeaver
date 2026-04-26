using UnityEngine;
using System.Collections.Generic;

public class GlobalRoadVisualizer : MonoBehaviour
{
    [Header("Visual Settings")]
    public float roadWidth = 0.4f;
    public Color roadColor = Color.gray;
    public Material roadMaterial; // Create a Material (Standard) and drag it here!

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    void Start()
    {
        // Add components if they don't exist
        meshFilter = gameObject.GetComponent<MeshFilter>();
        if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();

        meshRenderer = gameObject.GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();

        // Ensure we have a material so it's not pink
        if (roadMaterial == null)
            meshRenderer.material = new Material(Shader.Find("Sprites/Default"));
        else
            meshRenderer.material = roadMaterial;
    }

    void LateUpdate()
    {
        UpdateRoadMesh();
    }

    void UpdateRoadMesh()
    {
        TrafficNode[] allNodes = Object.FindObjectsOfType<TrafficNode>();
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        HashSet<(int, int)> drawnConnections = new HashSet<(int, int)>();

        foreach (var node in allNodes)
        {
            if (node == null) continue;

            foreach (var neighbor in node.neighbors)
            {
                if (neighbor == null) continue;

                // Unique ID for the pair so we don't draw the same road twice
                int id1 = node.gameObject.GetInstanceID();
                int id2 = neighbor.gameObject.GetInstanceID();
                var pair = id1 < id2 ? (id1, id2) : (id2, id1);

                if (drawnConnections.Contains(pair)) continue;
                drawnConnections.Add(pair);

                // Create a "quad" (rectangle) for the road segment
                AddRoadSegment(node.transform.position, neighbor.transform.position, verts, tris);
            }
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;
    }

    void AddRoadSegment(Vector3 start, Vector3 end, List<Vector3> verts, List<int> tris)
    {
        Vector3 dir = (end - start).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up) * (roadWidth / 2f);

        int vCount = verts.Count;

        // The 4 corners of the road rectangle
        verts.Add(start + side + Vector3.up * 0.05f); // 0
        verts.Add(start - side + Vector3.up * 0.05f); // 1
        verts.Add(end + side + Vector3.up * 0.05f);   // 2
        verts.Add(end - side + Vector3.up * 0.05f);   // 3

        // Triangle 1
        tris.Add(vCount + 0);
        tris.Add(vCount + 2);
        tris.Add(vCount + 1);

        // Triangle 2
        tris.Add(vCount + 1);
        tris.Add(vCount + 2);
        tris.Add(vCount + 3);

        //Color roadColor = Color.Lerp(Color.gray, Color.red, penalty / 100f);
    }
}