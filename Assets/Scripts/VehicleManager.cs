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
    public SplineContainer laneContainer;

    [Header("Organization")]
    public Transform pathParent;

    [Header("Spawn Settings")]
    public float spawnDelay = 0.5f;

    private List<GameObject> activeVehicles = new List<GameObject>();

    [ContextMenu("Spawn Random Traffic")]
    public void SpawnTraffic()
    {
        StartCoroutine(SpawnTrafficCoroutine());
    }
    // Add this struct at the top of the class
    private struct CarSnapshot
    {
        public CarAgent agent;
        public Vector3 homePos;
        public Vector3 workPos;
        public Vector3 currentTargetPos;
        public bool hadTarget;
        public bool headingToWork;
    }

    private List<CarSnapshot> _snapshots = new List<CarSnapshot>();

    /// <summary>
    /// Call this BEFORE RebuildGraph(). Saves world positions while nodes still exist.
    /// </summary>
    public void SnapshotVehiclePositions()
    {
        _snapshots.Clear();
        foreach (GameObject vehicle in activeVehicles)
        {
            if (vehicle == null) continue;
            CarAgent agent = vehicle.GetComponent<CarAgent>();
            if (agent == null) continue;

            _snapshots.Add(new CarSnapshot
            {
                agent = agent,
                homePos = agent.homeNode != null ? agent.homeNode.transform.position : agent.transform.position,
                workPos = agent.workNode != null ? agent.workNode.transform.position : agent.transform.position,
                currentTargetPos = agent.currentTarget != null ? agent.currentTarget.transform.position : agent.transform.position,
                hadTarget = agent.currentTarget != null,
                headingToWork = agent.headingToWork
            });
        }
    }
    private IEnumerator SpawnTrafficCoroutine()
    {
        TrafficNetwork network = Object.FindAnyObjectByType<TrafficNetwork>();
        if (network == null) { Debug.LogError("No TrafficNetwork found!"); yield break; }

        if (network.allNodes.Count == 0)
        {
            Debug.Log("Network empty! Rebuilding now...");
            network.RebuildGraph();
        }

        if (network.allNodes.Count < 2)
        {
            Debug.LogError("Network has fewer than 2 nodes! Draw some roads first, then spawn traffic.");
            yield break;
        }
        if (laneContainer == null) { Debug.LogError("No laneContainer assigned!"); yield break; }

        if (pathParent == null) pathParent = new GameObject("TrafficData").transform;

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
                agent.baseSpeed = Random.Range(0.5f, 1f);

                // Assign distinct random start and end positions
                TrafficNode startNode = network.allNodes[Random.Range(0, network.allNodes.Count)];
                TrafficNode workNode;
                do
                {
                    workNode = network.allNodes[Random.Range(0, network.allNodes.Count)];
                } while (workNode == startNode);

                agent.InitializeAgent(startNode, workNode);
            }

            Renderer carRenderer = vehicle.GetComponentInChildren<Renderer>();
            if (carRenderer != null)
                carRenderer.material.color = Random.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 1f);

            activeVehicles.Add(vehicle);
            yield return new WaitForSeconds(spawnDelay);
        }
    }

    /*public void RecalculateAllVehiclePaths()
    {
        foreach (GameObject vehicle in activeVehicles)
        {
            if (vehicle == null) continue;
            CarAgent agent = vehicle.GetComponent<CarAgent>();
            if (agent != null) agent.RecalculatePath();
        }
    }*/
    /// <summary>
    /// Call this AFTER RebuildGraph(). Restores cars using the cached positions.
    /// </summary>
    public void RecalculateAllVehiclePaths()
    {
        TrafficNetwork net = FindAnyObjectByType<TrafficNetwork>();
        if (net == null) return;

        foreach (var snap in _snapshots)
        {
            if (snap.agent == null) continue;
            snap.agent.RemapFromSnapshot(net, snap.homePos, snap.workPos,
                                         snap.currentTargetPos, snap.hadTarget,
                                         snap.headingToWork);
        }
        _snapshots.Clear();
    }
    [ContextMenu("Clear Traffic")]
    public void ClearTraffic()
    {
        foreach (var v in activeVehicles)
        {
            if (v != null) Destroy(v);
        }
        activeVehicles.Clear();
    }
}