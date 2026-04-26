using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using System.Collections;

public class VehicleManager : MonoBehaviour
{
    [Header("Settings")]
    public GameObject[] vehiclePrefabs; // Deine Auto-Prefabs
    public RandomSplineRoadArchitect roadArchitect; // Der Architect aus der Szene
    public int amountToSpawn = 5;

    [Header("Organization")]
    [Tooltip("All generated paths will be stored under this object")]
    public Transform pathParent;

    private List<GameObject> activeVehicles = new List<GameObject>();
    private List<GameObject> activePaths = new List<GameObject>();

    [Header("Spawn Settings")]
    public float spawnDelay = 0.5f; // Stagger time in seconds

    [ContextMenu("Spawn Random Traffic")]
    public void SpawnTraffic()
    {
        // Start the Coroutine instead of running a loop in one frame
        StartCoroutine(SpawnTrafficCoroutine());
    }

    private IEnumerator SpawnTrafficCoroutine()
    {
        // Inside SpawnTrafficCoroutine in VehicleManager.cs:
        TrafficNetwork network = FindObjectOfType<TrafficNetwork>();
        if (network == null) { Debug.LogError("No TrafficNetwork found!"); yield break; }

        // FORCE the network to build if it's empty
        if (network.allNodes.Count == 0)
        {
            Debug.Log("Network empty! Rebuilding now...");
            network.RebuildGraph();
        }
        Debug.Log($"STARTING SPAWN: I found {network.allNodes.Count} nodes in the network.");

        if (pathParent == null) pathParent = new GameObject("TrafficData").transform;
        pathParent.position = Vector3.zero;
        pathParent.rotation = Quaternion.identity;
        if (network == null) { Debug.LogError("No TrafficNetwork found!"); yield break; }

        int maxEndpoints = roadArchitect.destinationSplineIndices.Count;
        if (maxEndpoints < 2) { Debug.LogError("Need at least 2 destination splines!"); yield break; }

        for (int i = 0; i < amountToSpawn; i++)
        {
            GameObject vehicle = Instantiate(vehiclePrefabs[Random.Range(0, vehiclePrefabs.Length)]);
            vehicle.name = $"Car_{activeVehicles.Count}";
            vehicle.transform.SetParent(pathParent);

            CarAgent agent = vehicle.GetComponent<CarAgent>();
            if (agent != null)
            {
                agent.network = network;
                agent.splineContainer = roadArchitect.targetContainer;
                agent.baseSpeed = Random.Range(8f, 12f);

                // 1. Get the indices from the architect
                int homeSplineIdx = roadArchitect.destinationSplineIndices[Random.Range(0, maxEndpoints)];
                int workSplineIdx;
                do
                {
                    workSplineIdx = roadArchitect.destinationSplineIndices[Random.Range(0, maxEndpoints)];
                } while (workSplineIdx == homeSplineIdx && maxEndpoints > 1);

                // 2. DECLARE the lists (This is the part that was missing!)
                List<TrafficNode> possibleHomeNodes = network.allNodes.FindAll(n => n.splineIndex == homeSplineIdx);
                List<TrafficNode> possibleWorkNodes = network.allNodes.FindAll(n => n.splineIndex == workSplineIdx);

                // 3. Assign nodes if they exist
                if (possibleHomeNodes.Count > 0 && possibleWorkNodes.Count > 0)
                {
                    agent.homeNode = possibleHomeNodes[Random.Range(0, possibleHomeNodes.Count)];

                    // Pick a work node that isn't the EXACT same object as the home node
                    TrafficNode selectedWork;
                    do
                    {
                        selectedWork = possibleWorkNodes[Random.Range(0, possibleWorkNodes.Count)];
                    } while (selectedWork == agent.homeNode && possibleWorkNodes.Count > 1);

                    agent.workNode = selectedWork;

                    // 4. Start the car!
                    agent.InitializeAgent();
                }
                else
                {
                    Debug.LogWarning($"Could not find nodes for Spline {homeSplineIdx} or {workSplineIdx}!");
                }
            }

            Color randomColor = Random.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 1f);
            if (vehicle.TryGetComponent(out Renderer r)) r.material.color = randomColor;

            activeVehicles.Add(vehicle);

            yield return new WaitForSeconds(spawnDelay);
        }
    }
    public void RefreshAllPaths()
    {
        Debug.Log("<color=orange>Map Update:</color> Telling all cars to check for shortcuts.");
        foreach (GameObject vehicle in activeVehicles)
        {
            if (vehicle != null)
            {
                // Tell each car to run its Dijkstra again
                vehicle.GetComponent<CarAgent>().RecalculatePath();
            }
        }
    }
    public void RecalculateAllVehiclePaths()
    {
        Debug.Log("<color=cyan>Network changed!</color> Cars are looking for shortcuts...");
        foreach (GameObject vehicle in activeVehicles)
        {
            if (vehicle != null)
            {
                CarAgent agent = vehicle.GetComponent<CarAgent>();
                // Only recalculate if they aren't currently waiting at a house
                agent.RecalculatePath();
            }
        }
    }
    [ContextMenu("Clear All Traffic")]
    public void ClearTraffic()
    {
        // Autos löschen
        foreach (var v in activeVehicles) if (v != null) DestroyImmediate(v);
        activeVehicles.Clear();

        // Pfade löschen
        foreach (var p in activePaths) if (p != null) DestroyImmediate(p);
        activePaths.Clear();

        Debug.Log("<color=red>VehicleManager:</color> All vehicles and paths cleared.");
    }
}