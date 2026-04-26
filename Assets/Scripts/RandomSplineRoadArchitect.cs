using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[ExecuteInEditMode]
public class RandomSplineRoadArchitect : MonoBehaviour
{
    public enum LaneSelection { RightLane, LeftLane, BothLanes }

    [Header("Infrastructure")]
    public SplineContainer sourceContainer;
    [Header("Internal - Managed by VehicleManager")]
    public SplineContainer targetContainer;

    [Header("Lane Settings")]
    public LaneSelection selectedLane = LaneSelection.BothLanes;
    public float laneOffset = 0.5f;
    public bool autoAlignSourceFlow = true;

    [Header("Pathfinding Info (Read Only)")]
    [SerializeField] private int startSplineIndex;
    [SerializeField] private int targetSplineIndex;

    [Header("Endpoint Configuration")]
    // This list will hold the indices of the splines that act as Home/Work
    public List<int> destinationSplineIndices = new List<int>();

    // Helper to get a random endpoint for the VehicleManager
    public int GetRandomEndpointIndex()
    {
        if (destinationSplineIndices.Count == 0) return 0;
        return destinationSplineIndices[UnityEngine.Random.Range(0, destinationSplineIndices.Count)];
    }

    private List<Vector3> visualPathNodes = new List<Vector3>();

    [ContextMenu("Generate Random Path & Lanes")]
    public void GenerateRandomRoad()
    {
        if (sourceContainer == null || targetContainer == null)
        {
            Debug.LogError("Please assign Source and Target Containers!");
            return;
        }

        // 1. Clear target container
        while (targetContainer.Splines.Count > 0)
            targetContainer.RemoveSplineAt(0);

        if (sourceContainer.Splines.Count < 2) return;

        // 2. Align source flow if needed
        if (autoAlignSourceFlow) AlignSourceSplines();

        // 3. Pick random start and end
        startSplineIndex = UnityEngine.Random.Range(0, sourceContainer.Splines.Count);
        do
        {
            targetSplineIndex = UnityEngine.Random.Range(0, sourceContainer.Splines.Count);
        } while (targetSplineIndex == startSplineIndex);

        int targetKnotIndex = sourceContainer.Splines[targetSplineIndex].Count - 1;

        // 4. Find the shortest path (Knot indices)
        var path = FindShortestPath(startSplineIndex, 0, targetSplineIndex, targetKnotIndex);

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning("<color=red>No path found between random points!</color>");
            return;
        }

        // 5. Generate lanes based on the found path
        // We treat the found path as a "temporary source"
        GenerateLanesFromPath(path);

        EditorApplyChanges();
        Debug.Log($"<color=lime><b>Success:</b></color> Path from {startSplineIndex} to {targetSplineIndex} generated with {selectedLane}!");

        // At the bottom of GenerateRandomRoad()
        TrafficNetwork net = FindObjectOfType<TrafficNetwork>();
        if (net != null) net.RebuildGraph();
    }

    private void GenerateLanesFromPath(List<SplineKnotIndex> pathIndices)
    {
        // Wir erstellen einen temporären Spline, der den gefundenen Pfad repräsentiert
        // um daraus dann die Lanes mit Offset zu generieren
        Spline pathBase = new Spline();
        foreach (var idx in pathIndices)
        {
            pathBase.Add(sourceContainer.Splines[idx.Spline][idx.Knot]);
        }

        // Rechte Spur
        if (selectedLane == LaneSelection.RightLane || selectedLane == LaneSelection.BothLanes)
        {
            CreateOffsetLane(pathBase, laneOffset, false);
        }

        // Linke Spur
        if (selectedLane == LaneSelection.LeftLane || selectedLane == LaneSelection.BothLanes)
        {
            CreateOffsetLane(pathBase, -laneOffset, true);
        }
    }
    // Add this inside RandomSplineRoadArchitect.cs
    public void GeneratePathForVehicle(SplineContainer vehicleContainer, int startIdx, int targetIdx)
    {
        if (sourceContainer == null) return;

        while (vehicleContainer.Splines.Count > 0) vehicleContainer.RemoveSplineAt(0);

        int targetKnotIndex = sourceContainer.Splines[targetIdx].Count - 1;
        var path = FindShortestPath(startIdx, 0, targetIdx, targetKnotIndex);
        if (path == null || path.Count == 0) return;

        var newLane = vehicleContainer.AddSpline();

        foreach (var idx in path)
        {
            BezierKnot roadKnot = sourceContainer.Splines[idx.Spline][idx.Knot];

            // Convert everything to world space
            float3 rightDir = math.mul(roadKnot.Rotation, new float3(1, 0, 0));
            float3 localOffsetPos = roadKnot.Position + (rightDir * laneOffset);

            // Bake into world space — vehicle container stays at world origin
            BezierKnot worldKnot = new BezierKnot();
            worldKnot.Position = sourceContainer.transform.TransformPoint(localOffsetPos);
            quaternion sourceRot = sourceContainer.transform.rotation; // implicit conversion works
            worldKnot.Rotation = math.mul(sourceRot, roadKnot.Rotation);
            worldKnot.TangentIn = sourceContainer.transform.TransformDirection(roadKnot.TangentIn);
            worldKnot.TangentOut = sourceContainer.transform.TransformDirection(roadKnot.TangentOut);

            newLane.Add(worldKnot);
            newLane.SetTangentMode(newLane.Count - 1, TangentMode.AutoSmooth);
        }
    }
    private void CreateOffsetLane(Spline source, float offset, bool reverseDirection)
    {
        var newLane = targetContainer.AddSpline();
        int knotCount = source.Count;

        for (int i = 0; i < knotCount; i++)
        {
            int index = reverseDirection ? (knotCount - 1 - i) : i;
            BezierKnot sourceKnot = source[index];

            float3 rightDir = math.mul(sourceKnot.Rotation, new float3(1, 0, 0));
            float3 offsetPos = sourceKnot.Position + (rightDir * offset);

            BezierKnot laneKnot = new BezierKnot();
            // Umrechnung in Weltraum und zurück in Target-Lokalraum für Präzision
            Vector3 worldPos = sourceContainer.transform.TransformPoint(offsetPos);
            laneKnot.Position = targetContainer.transform.InverseTransformPoint(worldPos);

            if (reverseDirection)
            {
                laneKnot.Rotation = math.mul(sourceKnot.Rotation, quaternion.RotateY(math.PI));
                laneKnot.TangentIn = -sourceKnot.TangentOut;
                laneKnot.TangentOut = -sourceKnot.TangentIn;
            }
            else
            {
                laneKnot.Rotation = sourceKnot.Rotation;
                laneKnot.TangentIn = sourceKnot.TangentIn;
                laneKnot.TangentOut = sourceKnot.TangentOut;
            }
            newLane.Add(laneKnot);
        }
        newLane.Closed = false; // Pfade sind in der Regel offen
    }

    #region Pathfinding Logic (Dijkstra)
    public List<SplineKnotIndex> FindShortestPath(int sIdx, int sKnot, int tIdx, int tKnot)
    {
        var allNodes = BuildGraph();
        var startKey = (sIdx, sKnot);
        var targetKey = (tIdx, tKnot);

        if (!allNodes.ContainsKey(startKey) || !allNodes.ContainsKey(targetKey)) return null;

        Node startNode = allNodes[startKey];
        Node targetNode = allNodes[targetKey];

        List<Node> unvisited = allNodes.Values.ToList();
        startNode.distanceFromStart = 0;

        while (unvisited.Count > 0)
        {
            Node current = unvisited.OrderBy(n => n.distanceFromStart).First();
            unvisited.Remove(current);

            if (current == targetNode) break;
            if (current.distanceFromStart == float.MaxValue) break;

            foreach (var edge in current.connections)
            {
                float altDist = current.distanceFromStart + edge.weight;
                if (altDist < edge.target.distanceFromStart)
                {
                    edge.target.distanceFromStart = altDist;
                    edge.target.parent = current;
                }
            }
        }
        return ReconstructPath(targetNode, sIdx);
    }

    private Dictionary<(int, int), Node> BuildGraph()
    {
        var nodes = new Dictionary<(int, int), Node>();
        for (int s = 0; s < sourceContainer.Splines.Count; s++)
        {
            for (int k = 0; k < sourceContainer.Splines[s].Count; k++)
                nodes[(s, k)] = new Node(s, k);
        }

        for (int s = 0; s < sourceContainer.Splines.Count; s++)
        {
            var spline = sourceContainer.Splines[s];
            float length = spline.GetLength();
            for (int k = 0; k < spline.Count - 1; k++)
            {
                float weight = length / (spline.Count - 1);
                nodes[(s, k)].connections.Add(new Edge { target = nodes[(s, k + 1)], weight = weight });
                nodes[(s, k + 1)].connections.Add(new Edge { target = nodes[(s, k)], weight = weight });
            }

            for (int k = 0; k < spline.Count; k++)
            {
                var links = sourceContainer.KnotLinkCollection.GetKnotLinks(new SplineKnotIndex(s, k));
                if (links == null) continue;
                foreach (var link in links)
                {
                    if (link.Spline == s && link.Knot == k) continue;
                    nodes[(s, k)].connections.Add(new Edge { target = nodes[(link.Spline, link.Knot)], weight = 0.001f });
                }
            }
        }
        return nodes;
    }

    private List<SplineKnotIndex> ReconstructPath(Node endNode, int sIdx)
    {
        List<SplineKnotIndex> path = new List<SplineKnotIndex>();
        visualPathNodes.Clear();
        Node current = endNode;
        if (current.parent == null && current.splineIndex != sIdx) return null;

        while (current != null)
        {
            path.Add(new SplineKnotIndex(current.splineIndex, current.knotIndex));
            visualPathNodes.Add(sourceContainer.transform.TransformPoint(sourceContainer.Splines[current.splineIndex][current.knotIndex].Position));
            current = current.parent;
        }
        path.Reverse();
        visualPathNodes.Reverse();
        return path;
    }
    #endregion

    private void AlignSourceSplines()
    {
        for (int i = 0; i < sourceContainer.Splines.Count; i++)
        {
            if (sourceContainer.Splines[i][sourceContainer.Splines[i].Count - 1].Position.x < sourceContainer.Splines[i][0].Position.x)
                SplineUtility.ReverseFlow(sourceContainer, i);
        }
    }

    private void EditorApplyChanges()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(targetContainer);
        UnityEditor.SceneView.RepaintAll();
#endif
    }

    private void OnDrawGizmos()
    {
        if (targetContainer == null) return;
        for (int i = 0; i < targetContainer.Splines.Count; i++)
        {
            Color c = (selectedLane == LaneSelection.BothLanes) ? (i % 2 == 0 ? Color.cyan : new Color(1f, 0.5f, 0f)) :
                     (selectedLane == LaneSelection.RightLane ? Color.cyan : new Color(1f, 0.5f, 0f));

            Gizmos.color = c;
            var s = targetContainer.Splines[i];
            for (int k = 0; k < s.Count; k++)
            {
                Vector3 p = targetContainer.transform.TransformPoint(s[k].Position);
                Gizmos.DrawSphere(p, 0.15f);
                if (k < s.Count - 1)
                {
                    Vector3 next = targetContainer.transform.TransformPoint(s[k + 1].Position);
                    Gizmos.DrawLine(p, next);
                    Vector3 dir = (next - p).normalized;
                    Vector3 r = Quaternion.LookRotation(dir) * Quaternion.Euler(0, 160, 0) * Vector3.forward;
                    Vector3 l = Quaternion.LookRotation(dir) * Quaternion.Euler(0, -160, 0) * Vector3.forward;
                    Gizmos.DrawRay((p + next) * 0.5f, r * 0.3f);
                    Gizmos.DrawRay((p + next) * 0.5f, l * 0.3f);
                }
            }
        }
    }

    public class Node
    {
        public int splineIndex, knotIndex;
        public float distanceFromStart = float.MaxValue;
        public Node parent;
        public List<Edge> connections = new List<Edge>();
        public Node(int s, int k) { splineIndex = s; knotIndex = k; }
    }
    public class Edge { public Node target; public float weight; }
}