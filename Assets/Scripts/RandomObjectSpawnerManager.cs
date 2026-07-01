using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;

public class RandomObjectSpawnerManager : NetworkBehaviour
{
    [System.Serializable]
    public class SpawnableItem
    {
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
    public List<SpawnableItem> prefabsToSpawn = new List<SpawnableItem>();
    public int amount = 100;

    [Header("Rotation Settings")]
    public bool useRandomRotation = true;

    [Header("Road Settings")]
    private float roadWidth = 0.5f;
    public MultiSplineDrawer multiSplineDrawer;
    public SplineContainer roadSpline;

    [Header("Avoid collision (Spawn)")]
    [Tooltip("Layers to avoid during the initial spawn process (e.g. Road, Nature, Environment).")]
    public LayerMask avoidanceLayers;
    [Tooltip("The safety buffer space around buildings to prevent overlapping with the road.")]
    public float spacingFromRoad = 0.65f;
    [Tooltip("The safety buffer space between individual spawned objects. Keep low for dense placement.")]
    public float spacingBetweenObjects = 0.1f;

    [Header("Cleanup Settings (Refresh)")]
    [Tooltip("ONLY select the layer of your houses here! If a tree detects this layer during refresh, it gets deleted.")]
    public LayerMask obstaclesToRefreshAgainst;

    [Header("Area Reference")]
    public Transform areaObject;

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

    public void RequestRandomObjectSpawn()
    {
        if (Application.isPlaying)
        {
            if (IsServer) SpawnObjectsInternal();
            else if (IsClient) TriggerRandomSpawnRpc();
        }
        else
        {
            SpawnObjectsInternal();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TriggerRandomSpawnRpc()
    {
        SpawnObjectsInternal();
    }

    public void RequestClearAllObjects()
    {
        if (Application.isPlaying)
        {
            if (IsServer) ClearAllObjectsInternal();
            else if (IsClient) TriggerClearAllRpc();
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

    [ContextMenu("Refresh object spawning")]
    public void RefreshObjectSpawning()
    {
        if (Application.isPlaying && !IsServer) return;

        // Fetch all nested children to safely catch editor-pre-placed trees
        NetworkObject[] childNetObjects = GetComponentsInChildren<NetworkObject>(true);
        foreach (var netObj in childNetObjects)
        {
            if (netObj != this.GetComponent<NetworkObject>() && !spawnedNetworkObjects.Contains(netObj))
            {
                spawnedNetworkObjects.Add(netObj);
            }
        }

        // Force layout sync to ensure the physics engine has processed the micro-scaled house transforms
        Physics.SyncTransforms();

        ValidateExistingBuildings();

        SpawnObjectsInternal();

        Debug.Log("Spawning refreshed and synchronized.");
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
                if (Application.isPlaying) netObj.Despawn(true);
                else DestroyImmediate(netObj.gameObject);
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

            // 1. Check spline distance
            bool isTooCloseToSpline = !IsPositionFarFromAllSplines(netObj.transform.position, totalRequiredDistance);

            // 2. Unfehlbarer Tag Check
            bool isCollidingWithBuilding = false;

            // Scanne großzügig um das Objekt herum (auch nützlich bei winzig skalierten Häusern)
            Collider[] hitColliders = Physics.OverlapSphere(netObj.transform.position, 1.5f, Physics.AllLayers, QueryTriggerInteraction.Collide);

            foreach (var hit in hitColliders)
            {
                if (hit.gameObject == netObj.gameObject || hit.transform.IsChildOf(netObj.transform))
                    continue;

                // Vergleiche Tag der Häuser
                if (hit.CompareTag("Building") || (hit.transform.parent != null && hit.transform.parent.CompareTag("Building")))
                {
                    isCollidingWithBuilding = true;
                    break;
                }
            }

            if (isTooCloseToSpline || isCollidingWithBuilding)
            {
                if (Application.isPlaying)
                    netObj.Despawn(true);
                else
                    DestroyImmediate(netObj.gameObject);

                spawnedNetworkObjects.RemoveAt(i);
            }
        }

        int removedCount = startCount - spawnedNetworkObjects.Count;
        Debug.Log($"<color=orange>Validation complete:</color> {removedCount} Objects removed. " +
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
            checkBoxExtents.x += spacingBetweenObjects;
            checkBoxExtents.z += spacingBetweenObjects;
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
                        BigMapSyncManager.Instance?.RegisterNewObjects();
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