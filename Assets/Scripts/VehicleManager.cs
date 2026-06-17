using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using System.Collections;
using Unity.Netcode;

public class VehicleManager : NetworkBehaviour
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

    // Track instantiated NetworkObjects to handle network destruction cleanly
    private List<NetworkObject> activeNetworkVehicles = new List<NetworkObject>();

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

    // =================================================================================
    // MULTIPLAYER SERVER/CLIENT ROUTINE
    // =================================================================================

    /// <summary>
    /// Call this from a VR Button or UI Event. Works for both Host and Clients!
    /// </summary>
    [ContextMenu("Spawn Random Traffic")]
    public void SpawnTraffic()
    {
        if (Application.isPlaying)
        {
            if (IsServer)
            {
                StartCoroutine(SpawnTrafficCoroutine());
            }
            else if (IsClient)
            {
                RequestSpawnTrafficRpc();
            }
        }
        else
        {
            // Editor execution fallback
            StartCoroutine(SpawnTrafficCoroutine());
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestSpawnTrafficRpc()
    {
        StartCoroutine(SpawnTrafficCoroutine());
    }

    /// <summary>
    /// Safely requests the clearance of all traffic from Host or Client.
    /// </summary>
    [ContextMenu("Clear Traffic")]
    public void ClearTraffic()
    {
        if (Application.isPlaying)
        {
            if (IsServer)
            {
                ClearTrafficInternal();
            }
            else if (IsClient)
            {
                RequestClearTrafficRpc();
            }
        }
        else
        {
            ClearTrafficInternal();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestClearTrafficRpc()
    {
        ClearTrafficInternal();
    }

    private void ClearTrafficInternal()
    {
        for (int i = activeNetworkVehicles.Count - 1; i >= 0; i--)
        {
            NetworkObject netObj = activeNetworkVehicles[i];
            if (netObj != null)
            {
                if (Application.isPlaying)
                {
                    if (netObj.IsSpawned) netObj.Despawn(true);
                    else Destroy(netObj.gameObject);
                }
                else
                {
                    DestroyImmediate(netObj.gameObject);
                }
            }
        }
        activeNetworkVehicles.Clear();
    }

    // =================================================================================
    // CORE LOGIC & REPLICATION
    // =================================================================================

    public void SnapshotVehiclePositions()
    {
        _snapshots.Clear();
        foreach (NetworkObject netVehicle in activeNetworkVehicles)
        {
            if (netVehicle == null) continue;
            CarAgent agent = netVehicle.GetComponent<CarAgent>();
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
            vehicle.name = $"Car_{activeNetworkVehicles.Count}";
            vehicle.transform.SetParent(pathParent);

            // Sync random colors across the network via ClientRpc so everyone sees the same car colors
            Color randomColor = Random.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 1f);

            if (Application.isPlaying)
            {
                NetworkObject netObj = vehicle.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    // 1. Spawn over network so all connected VR players see the car
                    netObj.Spawn(true);
                    activeNetworkVehicles.Add(netObj);

                    // 2. Sync color visually
                    ApplyCarColorClientRpc(netObj.NetworkObjectId, randomColor);
                }
                else
                {
                    Debug.LogError($"Vehicle Prefab is missing a NetworkObject component!");
                    Destroy(vehicle);
                    yield break;
                }
            }

            CarAgent agent = vehicle.GetComponent<CarAgent>();
            if (agent != null)
            {
                agent.network = network;
                agent.splineContainer = laneContainer;
                agent.baseSpeed = Random.Range(0.5f, 1f);

                TrafficNode startNode = network.allNodes[Random.Range(0, network.allNodes.Count)];
                TrafficNode workNode;
                do
                {
                    workNode = network.allNodes[Random.Range(0, network.allNodes.Count)];
                } while (workNode == startNode);

                agent.InitializeAgent(startNode, workNode);
            }

            // Fallback for editor mode instantiation
            if (!Application.isPlaying)
            {
                Renderer carRenderer = vehicle.GetComponentInChildren<Renderer>();
                if (carRenderer != null) carRenderer.material.color = randomColor;

                NetworkObject netObj = vehicle.GetComponent<NetworkObject>();
                if (netObj != null) activeNetworkVehicles.Add(netObj);
            }

            yield return new WaitForSeconds(spawnDelay);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ApplyCarColorClientRpc(ulong networkObjectId, Color color)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            Renderer carRenderer = netObj.GetComponentInChildren<Renderer>();
            if (carRenderer != null)
            {
                carRenderer.material.color = color;
            }
        }
    }

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
}