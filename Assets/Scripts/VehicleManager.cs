using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using System.Collections;

public class VehicleManager : MonoBehaviour
{
    [Header("Settings")]
    public GameObject[] vehiclePrefabs;
    public int amountToSpawn = 5;

    [Header("Lane Container")]
    [Tooltip("Assign the SplineContainer that holds your hand-drawn road splines.")]
    public SplineContainer laneContainer;

    [Header("Organization")]
    [Tooltip("All generated paths will be stored under this object")]
    public Transform pathParent;

    [Header("Spawn Settings")]
    public float spawnDelay = 0.5f;

    private List<GameObject> activeVehicles = new List<GameObject>();

    [ContextMenu("Spawn Random Traffic")]
    public void SpawnTraffic()
    {
        StartCoroutine(SpawnTrafficCoroutine());
    }

    private IEnumerator SpawnTrafficCoroutine()
    {
        TrafficNetwork network = FindObjectOfType<TrafficNetwork>();
        if (network == null) { Debug.LogError("No TrafficNetwork found!"); yield break; }

        if (network.allNodes.Count == 0)
        {
            Debug.Log("Network empty! Rebuilding now...");
            network.RebuildGraph();
        }

        if (network.allNodes.Count < 2) { Debug.LogError("Network has fewer than 2 nodes!"); yield break; }
        if (laneContainer == null) { Debug.LogError("No laneContainer assigned!"); yield break; }
        if (pathParent == null) pathParent = new GameObject("TrafficData").transform;

        Debug.Log($"Spawning {amountToSpawn} vehicles across {network.allNodes.Count} nodes.");

        for (int i = 0; i < amountToSpawn; i++)
        {
            GameObject vehicle = Instantiate(vehiclePrefabs[Random.Range(0, vehiclePrefabs.Length)]);
            vehicle.name = $"Car_{activeVehicles.Count}";
            vehicle.transform.SetParent(pathParent);

            CarAgent agent = vehicle.GetComponent<CarAgent>();
            if (agent != null)
            {
                agent.network = network;
                agent.splineContainer = laneContainer;
                agent.baseSpeed = Random.Range(8f, 12f);

                agent.homeNode = network.allNodes[Random.Range(0, network.allNodes.Count)];
                TrafficNode selectedWork;
                do
                {
                    selectedWork = network.allNodes[Random.Range(0, network.allNodes.Count)];
                } while (selectedWork == agent.homeNode);
                agent.workNode = selectedWork;

                agent.InitializeAgent();
            }
            else
            {
                Debug.LogWarning($"{vehicle.name} has no CarAgent component!");
            }

            if (vehicle.TryGetComponent(out Renderer r))
                r.material.color = Random.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 1f);

            activeVehicles.Add(vehicle);
            yield return new WaitForSeconds(spawnDelay);
        }
    }

    public void RefreshAllPaths()
    {
        Debug.Log("<color=orange>Map Update:</color> Telling all cars to check for shortcuts.");
        foreach (GameObject vehicle in activeVehicles)
        {
            if (vehicle == null) continue;
            CarAgent agent = vehicle.GetComponent<CarAgent>();
            if (agent != null) agent.RecalculatePath();
        }
    }

    public void RecalculateAllVehiclePaths()
    {
        Debug.Log("<color=cyan>Network changed!</color> Cars are looking for shortcuts...");
        foreach (GameObject vehicle in activeVehicles)
        {
            if (vehicle == null) continue;
            CarAgent agent = vehicle.GetComponent<CarAgent>();
            if (agent != null && !agent.isWaiting) agent.RecalculatePath();
        }
    }

    [ContextMenu("Clear All Traffic")]
    public void ClearTraffic()
    {
        foreach (var v in activeVehicles) if (v != null) DestroyImmediate(v);
        activeVehicles.Clear();
        Debug.Log("<color=red>VehicleManager:</color> All vehicles cleared.");
    }
}