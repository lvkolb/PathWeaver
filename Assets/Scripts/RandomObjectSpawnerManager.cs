using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode; // Required for Netcode

public class RandomObjectSpawnerManager : NetworkBehaviour
{
    // A custom class to pair a Prefab with its custom spawn weight configuration
    [System.Serializable]
    public class SpawnableItem
    {
        // Unity automatically uses the first string field it finds as the element title in the list.
        [HideInInspector] public string prefabName;

        public GameObject prefab;
        [Tooltip("The relative weight of this item. Higher weight means higher spawn chance.")]
        public float weight = 1.0f;

        public void UpdateTitle()
        {
            prefabName = prefab != null ? prefab.name : "None (Prefab)";
        }
    }

    [Header("Prefabs & quantity")]
    [Tooltip("Configure your prefabs and their individual spawn likelihood weights here. E.g. 20, 40, 60, 20")]
    public List<SpawnableItem> prefabsToSpawn = new List<SpawnableItem>();
    public int amount = 100;

    [Header("Rotation Settings")]
    [Tooltip("If enabled, spawned objects will rotate randomly between 0 and 360 degrees around the Y axis.")]
    public bool useRandomRotation = true;

    [Header("Road Settings")]
    private float roadWidth = 0.5f;
    public MultiSplineDrawer multiSplineDrawer;
    public SplineContainer roadSpline;

    [Header("Avoid collision")]
    public LayerMask avoidanceLayers;
    [Tooltip("The safety buffer space around buildings to prevent overlapping.")]
    public float spacingFromRoad = 0.1f;

    [Header("Area Reference")]
    public Transform areaObject;

    // Track network objects using NetworkObject references to support network destruction
    private List<NetworkObject> spawnedNetworkObjects = new List<NetworkObject>();

    private void OnValidate()
    {
        if (prefabsToSpawn == null) return;
        foreach (var item in prefabsToSpawn)
        {
            if (item != null) item.UpdateTitle();
        }
    }

    public void Start()
    {
        if (multiSplineDrawer != null)
        {
            roadWidth = multiSplineDrawer.splineWidth;
        }
    }

    // =================================================================================
    // MULTIPLAYER SERVER/CLIENT ROUTINE (Unity 6 Compatible)
    // =================================================================================

    /// <summary>
    /// Call this method from your VR UI or input script to trigger the spawn for everyone.
    /// </summary>
    public void RequestRandomObjectSpawn()
    {
        if (Application.isPlaying)
        {
            if (IsServer)
            {
                SpawnObjectsInternal();
            }
            else if (IsClient)
            {
                TriggerRandomSpawnRpc();
            }
        }
        else
        {
            // Fallback for Unity Editor mode outside of Play Mode
            SpawnObjectsInternal();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TriggerRandomSpawnRpc()
    {
        SpawnObjectsInternal();
    }

    /// <summary>
    /// Call this method to clear objects from either Server or Client.
    /// </summary>
    public void RequestClearAllObjects()
    {
        if (Application.isPlaying)
        {
            if (IsServer)
            {
                ClearAllObjectsInternal();
            }
            else if (IsClient)
            {
                TriggerClearAllRpc();
            }
        }
        else
        {
            ClearAllObjectsInternal();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TriggerClearAllRpc()
    {
        ClearAllObjectsInternal();
    }

    // =================================================================================
    // INTERNAL CORE LOGIC (Executed on Server at Runtime)
    // =================================================================================

    [ContextMenu("Refresh object spawning")]
    public void RefreshObjectSpawning()
    {
        if (Application.isPlaying && !IsServer) return;
        ValidateExistingBuildings();
    }

    [ContextMenu("Clear All Objects")]
    public void ClearAllObjects()
    {
        RequestClearAllObjects();
    }

    private void ClearAllObjectsInternal()
    {
        for (int i = spawnedNetworkObjects.Count - 1; i >= 0; i--)
        {
            NetworkObject netObj = spawnedNetworkObjects[i];

            if (netObj != null)
            {
                if (Application.isPlaying)
                {
                    netObj.Despawn(true);
                }
                else
                {
                    DestroyImmediate(netObj.gameObject);
                }
            }
        }

        spawnedNetworkObjects.Clear();

        if (!Application.isPlaying && transform.childCount > 0)
        {
            Debug.Log("Cleaning up leftover child objects...");
            List<GameObject> children = new List<GameObject>();
            foreach (Transform child in transform) children.Add(child.gameObject);
            foreach (GameObject child in children) DestroyImmediate(child);
        }

        Debug.Log("All objects cleared!");
    }

    private void ValidateExistingBuildings()
    {
        int startCount = spawnedNetworkObjects.Count;
        float halfRoadWidth = roadWidth * 0.5f;

        for (int i = spawnedNetworkObjects.Count - 1; i >= 0; i--)
        {
            NetworkObject netObj = spawnedNetworkObjects[i];
            if (netObj == null) continue;

            BoxCollider col = netObj.GetComponent<BoxCollider>();
            if (col == null) continue;

            Vector3 prefabScale = netObj.transform.localScale;
            Vector3 halfSize = Vector3.Scale(col.size, prefabScale) * 0.5f;
            float houseSafetyRadius = Mathf.Max(halfSize.x, halfSize.z);

            float totalRequiredDistance = halfRoadWidth + spacingFromRoad + houseSafetyRadius;

            if (!IsPositionFarFromAllSplines(netObj.transform.position, totalRequiredDistance))
            {
                if (Application.isPlaying)
                    netObj.Despawn(true);
                else
                    DestroyImmediate(netObj.gameObject);

                spawnedNetworkObjects.RemoveAt(i);
            }
        }

        int removedCount = startCount - spawnedNetworkObjects.Count;
        Debug.Log($"<color=orange>Validation complete:</color> {removedCount} Buildings removed. " +
                  $"<color=lime>Current total: {spawnedNetworkObjects.Count}</color>");
    }

    [ContextMenu("Spawn objects")]
    public void SpawnObjects()
    {
        RequestRandomObjectSpawn();
    }

    private void SpawnObjectsInternal()
    {
        if (roadSpline == null || areaObject == null || prefabsToSpawn == null || prefabsToSpawn.Count == 0) return;

        if (multiSplineDrawer != null)
        {
            roadWidth = multiSplineDrawer.splineWidth;
        }

        float halfWidth = (areaObject.localScale.x * 10f) / 2f;
        float halfLength = (areaObject.localScale.z * 10f) / 2f;
        float halfRoadWidth = roadWidth * 0.5f;

        int buildingsToCreate = amount - spawnedNetworkObjects.Count;

        for (int i = 0; i < buildingsToCreate; i++)
        {
            GameObject selectedPrefab = GetWeightedRandomPrefab();
            if (selectedPrefab == null) continue;

            BoxCollider col = selectedPrefab.GetComponent<BoxCollider>();
            if (col == null) continue;

            Vector3 prefabScale = selectedPrefab.transform.localScale;
            Vector3 finalExtents = Vector3.Scale(col.size, prefabScale) * 0.5f;

            float houseSafetyRadius = Mathf.Max(finalExtents.x, finalExtents.z);
            float totalRequiredDistance = halfRoadWidth + spacingFromRoad + houseSafetyRadius;

            bool validPosFound = false;
            Vector3 finalPos = Vector3.zero;
            int attempts = 0;

            Vector3 checkBoxExtents = finalExtents;
            checkBoxExtents.x += spacingFromRoad;
            checkBoxExtents.z += spacingFromRoad;
            checkBoxExtents.y += 0.5f;

            Quaternion spawnRotation = Quaternion.identity;
            if (useRandomRotation)
            {
                float randomYAngle = UnityEngine.Random.Range(0f, 360f);
                spawnRotation = Quaternion.Euler(0f, randomYAngle, 0f);
            }

            while (!validPosFound && attempts < 50)
            {
                attempts++;
                float randomX = UnityEngine.Random.Range(-halfWidth, halfWidth);
                float randomZ = UnityEngine.Random.Range(-halfLength, halfLength);
                Vector3 testPos = areaObject.position + new Vector3(randomX, areaObject.position.y, randomZ);

                if (IsPositionFarFromAllSplines(testPos, totalRequiredDistance))
                {
                    if (!Physics.CheckBox(testPos, checkBoxExtents, spawnRotation, avoidanceLayers))
                    {
                        finalPos = testPos;
                        validPosFound = true;
                    }
                }
            }

            if (validPosFound)
            {
                GameObject newBuilding = Instantiate(selectedPrefab, finalPos, spawnRotation, null);

                if (Application.isPlaying)
                {
                    NetworkObject netObj = newBuilding.GetComponent<NetworkObject>();
                    if (netObj != null)
                    {
                        netObj.Spawn(true);
                        spawnedNetworkObjects.Add(netObj);

                        NetworkObject parentNetObj = GetComponent<NetworkObject>();
                        if (parentNetObj != null && parentNetObj.IsSpawned)
                        {
                            netObj.TrySetParent(transform);
                        }
                        else
                        {
                            newBuilding.transform.parent = transform;
                        }
                    }
                    else
                    {
                        Debug.LogError($"Prefab {selectedPrefab.name} is missing a NetworkObject component!");
                        Destroy(newBuilding);
                    }
                }
                else
                {
                    newBuilding.transform.parent = transform;
                    NetworkObject netObj = newBuilding.GetComponent<NetworkObject>();
                    if (netObj != null) spawnedNetworkObjects.Add(netObj);
                }
            }
        }

        Debug.Log($"<color=lime>Done!</color> Random objects created. Current setup size: {spawnedNetworkObjects.Count}");
    }

    private GameObject GetWeightedRandomPrefab()
    {
        float totalWeight = 0f;
        foreach (var item in prefabsToSpawn)
        {
            if (item.prefab != null && item.weight > 0f)
            {
                totalWeight += item.weight;
            }
        }

        if (totalWeight <= 0f) return null;

        float randomRoll = UnityEngine.Random.Range(0f, totalWeight);
        float currentWeightTracker = 0f;

        foreach (var item in prefabsToSpawn)
        {
            if (item.prefab == null || item.weight <= 0f) continue;

            currentWeightTracker += item.weight;
            if (randomRoll <= currentWeightTracker)
            {
                return item.prefab;
            }
        }

        return null;
    }

    private bool IsPositionFarFromAllSplines(Vector3 worldPos, float requiredDist)
    {
        float3 localPos = roadSpline.transform.InverseTransformPoint(worldPos);
        foreach (var spline in roadSpline.Splines)
        {
            SplineUtility.GetNearestPoint(spline, localPos, out float3 nearestLocal, out float t);
            Vector3 nearestWorld = roadSpline.transform.TransformPoint(nearestLocal);
            if (Vector3.Distance(worldPos, nearestWorld) < requiredDist) return false;
        }
        return true;
    }
}