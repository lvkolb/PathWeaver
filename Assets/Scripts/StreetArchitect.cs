using UnityEngine;
using System.Collections.Generic;

public class StreetArchitect : MonoBehaviour
{
    [Header("Settings")]
    public GameObject nodePrefab;
    public float minDistance = 2.0f; // Alle 2 Meter ein neuer Knoten
    public float snapDistance = 1.5f;

    [Header("State")]
    public bool isDrawingMode = false;
    private TrafficNode lastNode = null;
    private MouseFollower follower;
    private TrafficNetwork network;
    private VehicleManager vehicleManager;
    [Header("Curvature Settings")]
    public float angleThreshold = 20f; // Ab 20 Grad Abweichung wird ein Punkt gesetzt
    private Vector3 lastDirection = Vector3.zero;

    void Start()
    {
        follower = FindObjectOfType<MouseFollower>();
        network = FindObjectOfType<TrafficNetwork>();
        vehicleManager = FindObjectOfType<VehicleManager>();
    }

    void Update()
    {
        // S-Taste schaltet den Baumodus um
        if (Input.GetKeyDown(KeyCode.S))
        {
            isDrawingMode = !isDrawingMode;
            if (!isDrawingMode) lastNode = null;
        }

        if (!isDrawingMode || follower == null) return;

        // AUTO-DRAW LOGIK:
        // Wir zeichnen, solange eine Taste gedrückt ist (z.B. Leertaste oder Mausklick)
        // ODER du lässt das 'Input'-Teil weg, wenn er IMMER zeichnen soll.
        if (Input.GetKey(KeyCode.Space) || Input.GetMouseButton(0))
        {
            HandleAutoDrawing();
        }
        else
        {
            // Wenn wir die Taste loslassen, fangen wir beim nächsten Mal eine neue Straße an
            lastNode = null;
        }
    }

    void HandleAutoDrawing()
    {
        Vector3 currentPos = follower.transform.position;

        // Wenn wir noch keinen Startpunkt haben oder weit genug gelaufen sind
        if (lastNode == null || Vector3.Distance(lastNode.transform.position, currentPos) > minDistance)
        {
            // 1. Prüfen, ob wir einen bestehenden Knoten "snappen" (Kreuzung nutzen)
            TrafficNode nearbyNode = FindNearbyNode(currentPos);
            TrafficNode currentNode;

            if (nearbyNode != null && nearbyNode != lastNode)
            {
                currentNode = nearbyNode;
            }
            else
            {
                // 2. Neuen Knoten spawnen
                GameObject obj = Instantiate(nodePrefab, currentPos + Vector3.up * 0.1f, Quaternion.identity);
                currentNode = obj.GetComponent<TrafficNode>();
                LinkToExistingNetwork(currentNode);
                network.RegisterNode(currentNode);
            }
           
            // 3. Verbindung ziehen
            if (lastNode != null && lastNode != currentNode)
            {
                // Kreuzungs-Check (verhindert Geisterstraßen, die sich überlagern)
                bool intersected = CheckForIntersections(lastNode, currentNode);
                if (!intersected)
                {
                    ConnectNodes(lastNode, currentNode);
                    LinkToExistingNetwork(currentNode);
                    vehicleManager.RecalculateAllVehiclePaths();
                }
            }

            lastNode = currentNode;
            FindObjectOfType<VehicleManager>().RefreshAllPaths();
        }
    
    }

    void LinkToExistingNetwork(TrafficNode newNode)
    {
        // Look for ANY node in the whole scene within 3 meters
        TrafficNode[] allSceneNodes = Object.FindObjectsOfType<TrafficNode>();

        foreach (var otherNode in allSceneNodes)
        {
            if (otherNode == newNode) continue;

            float distance = Vector3.Distance(newNode.transform.position, otherNode.transform.position);

            // If they are close enough, build a bridge!
            if (distance < 3.0f)
            {
                if (!newNode.neighbors.Contains(otherNode)) newNode.neighbors.Add(otherNode);
                if (!otherNode.neighbors.Contains(newNode)) otherNode.neighbors.Add(newNode);

                Debug.Log($"<color=cyan>Bridge built between {newNode.name} and {otherNode.name}</color>");
            }
        }

        // Also, tell the VehicleManager to tell the cars to look at the map again
        FindObjectOfType<VehicleManager>().RecalculateAllVehiclePaths();
    }
    bool CheckForIntersections(TrafficNode A, TrafficNode B)
    {
        TrafficNode[] allNodes = Object.FindObjectsOfType<TrafficNode>();
        bool foundAny = false;

        foreach (var nodeC in allNodes)
        {
            // Copy list to avoid errors if we modify neighbors while looping
            List<TrafficNode> neighborsCopy = new List<TrafficNode>(nodeC.neighbors);

            foreach (var nodeD in neighborsCopy)
            {
                if (nodeC == A || nodeC == B || nodeD == A || nodeD == B) continue;

                Vector3 intersectPoint;
                if (LineSegmentsIntersection(A.transform.position, B.transform.position,
                                            nodeC.transform.position, nodeD.transform.position,
                                            out intersectPoint))
                {
                    CreateIntersectionNode(A, B, nodeC, nodeD, intersectPoint);
                    foundAny = true;
                    // We return true to tell HandleDrawing NOT to connect A and B directly
                    return true;
                }
            }
        }
        return foundAny;
    }

    bool LineSegmentsIntersection(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, out Vector3 intersection)
    {
        intersection = Vector3.zero;
        float den = (p4.z - p3.z) * (p2.x - p1.x) - (p4.x - p3.x) * (p2.z - p1.z);
        if (Mathf.Abs(den) < 0.0001f) return false;

        float ua = ((p4.x - p3.x) * (p1.z - p3.z) - (p4.z - p3.z) * (p1.x - p3.x)) / den;
        float ub = ((p2.x - p1.x) * (p1.z - p3.z) - (p2.z - p1.z) * (p1.x - p3.x)) / den;

        if (ua > 0.1f && ua < 0.9f && ub > 0.1f && ub < 0.9f)
        {
            intersection = new Vector3(p1.x + ua * (p2.x - p1.x), p1.y, p1.z + ua * (p2.z - p1.z));
            return true;
        }
        return false;
    }

    void CreateIntersectionNode(TrafficNode A, TrafficNode B, TrafficNode C, TrafficNode D, Vector3 pos)
    {
        GameObject hubObj = Instantiate(nodePrefab, pos, Quaternion.identity);
        TrafficNode hub = hubObj.GetComponent<TrafficNode>();
        LinkToExistingNetwork(hub);
        if (FindObjectOfType<TrafficNetwork>() != null)
        {
            FindObjectOfType<TrafficNetwork>().allNodes.Add(hub);
        }
        // Remove the old direct connection between C and D
        C.neighbors.Remove(D);
        D.neighbors.Remove(C);

        // Connect the four "arms" to the new hub
        ConnectNodes(hub, A);
        ConnectNodes(hub, B);
        ConnectNodes(hub, C);
        ConnectNodes(hub, D);
    }

    void ConnectNodes(TrafficNode n1, TrafficNode n2)
    {
        if (n1 == null || n2 == null) return;

        // Add n2 as n1's neighbor
        if (!n1.neighbors.Contains(n2)) n1.neighbors.Add(n2);

        // CRITICAL: Add n1 as n2's neighbor (Two-way street)
        if (!n2.neighbors.Contains(n1)) n2.neighbors.Add(n1);

        Debug.Log($"Connected {n1.name} and {n2.name} bidirectionally.");
    }

    TrafficNode FindNearbyNode(Vector3 point)
    {
        TrafficNode[] allNodes = Object.FindObjectsOfType<TrafficNode>();
        TrafficNode closest = null;
        float minDist = snapDistance;

        foreach (var node in allNodes)
        {
            float dist = Vector3.Distance(point, node.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                closest = node;
            }
        }
        return closest;
    }
}