using UnityEngine;
using System.Collections.Generic;

public class GlobalRoadVisualizer : MonoBehaviour
{
    [Header("Visual Settings")]
    public float roadWidth = 0.15f;
    public Color roadColor = Color.gray;
    public Material roadMaterial;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    // FIX: Use Awake so components exist before the first LateUpdate fires.
    // Start() can be called AFTER LateUpdate() on the same frame the object
    // is enabled, which caused the MeshFilter null exception.
    void Awake()
    {
        meshFilter = gameObject.GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
        meshRenderer = gameObject.GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
        meshRenderer.material = roadMaterial != null
            ? roadMaterial
            : new Material(Shader.Find("Sprites/Default"));
        meshRenderer.material.color = roadColor;
    }

    void LateUpdate() => UpdateRoadMesh();

    void UpdateRoadMesh()
    {
        // Guard: components must exist (handles edge cases in editor)
        if (meshFilter == null) Awake();

        TrafficNode[] allNodes = Object.FindObjectsOfType<TrafficNode>();
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var drawn = new HashSet<(int, int)>();

        foreach (var node in allNodes)
        {
            if (node == null) continue;
            foreach (var nb in node.neighbors)
            {
                if (nb == null) continue;
                int a = node.gameObject.GetInstanceID();
                int b = nb.gameObject.GetInstanceID();
                var pair = a < b ? (a, b) : (b, a);
                if (!drawn.Add(pair)) continue;
                AddSegment(node.transform.position, nb.transform.position, verts, tris);
            }
        }

        var mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;
    }

    void AddSegment(Vector3 start, Vector3 end, List<Vector3> verts, List<int> tris)
    {
        Vector3 dir = (end - start).normalized;
        if (dir == Vector3.zero) return;
        Vector3 side = Vector3.Cross(dir, Vector3.up) * (roadWidth * 0.5f);
        int v = verts.Count;
        verts.Add(start + side + Vector3.up * 0.05f);
        verts.Add(start - side + Vector3.up * 0.05f);
        verts.Add(end + side + Vector3.up * 0.05f);
        verts.Add(end - side + Vector3.up * 0.05f);
        tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
        tris.Add(v + 1); tris.Add(v + 2); tris.Add(v + 3);
    }
}