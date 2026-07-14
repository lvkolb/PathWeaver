using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Netcode;
using System.Collections;

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

    [Header("Performance Settings")]
    [Tooltip("Maximum number of objects allowed to spawn in a single frame to prevent CPU spikes.")]
    [SerializeField] private int maxSpawnsPerFrame = 5;

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
            if (IsServer) StartCoroutine(SpawnObjectsInternalRoutine());
            else if (IsClient) TriggerRandomSpawnRpc();
        }
        else
        {
            // Fallback for editor mode (non-async)
            SpawnObjectsImmediateEditor();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TriggerRandomSpawnRpc()
    {
        StartCoroutine(SpawnObjectsInternalRoutine());
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

        if (Application.isPlaying)
        {
            StartCoroutine(SpawnObjectsInternalRoutine());
        }
        else
        {
            SpawnObjectsImmediateEditor();
        }

        Debug.Log("Spawning refresh initiated.");
    }

    [ContextMenu("Clear All Objects")]
    public void ClearAllObjects()
    {
        RequestClearAllObjects();
    }

    private void ClearAllObjectsInternal()
    {
        StopAllCoroutines();

        for (int i = spawnedNetworkObjects.Count - 1; i >= 0; i--)
        {
            NetworkObject netObj = spawnedNetworkObjects[i];
            if (netObj != null)
            {
                if (Application.isPlaying)
                {
                    // 1. SCHRITT: Nur über Netcode despawnen, wenn es wirklich aktiv gespawnt ist!
                    if (netObj.IsSpawned)
                    {
                        if (netObj.IsSceneObject ?? false)
                        {
                            netObj.Despawn(false);
                            netObj.gameObject.SetActive(false);
                        }
                        else
                        {
                            netObj.Despawn(true);
                        }
                    }
                    else
                    {
                        // Fallback: Wenn es ungespawnt in der Liste war, einfach normal lokal zerstören
                        Destroy(netObj.gameObject);
                    }
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

            bool isTooCloseToSpline = !IsPositionFarFromAllSplines(netObj.transform.position, totalRequiredDistance);
            bool isCollidingWithBuilding = false;

            Vector3 checkBoxExtents = halfSize;
            checkBoxExtents.x += spacingBetweenObjects;
            checkBoxExtents.z += spacingBetweenObjects;
            checkBoxExtents.y = 10.0f;
            Vector3 checkCenter = netObj.transform.position + Vector3.up * (checkBoxExtents.y * 0.5f);

            Collider[] hitColliders = Physics.OverlapBox(checkCenter, checkBoxExtents, netObj.transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);

            foreach (var hit in hitColliders)
            {
                if (hit.gameObject == netObj.gameObject || hit.transform.IsChildOf(netObj.transform))
                    continue;

                if (hit.CompareTag("Building") || (hit.transform.parent != null && hit.transform.parent.CompareTag("Building")))
                {
                    isCollidingWithBuilding = true;
                    break;
                }
            }

            if (isTooCloseToSpline || isCollidingWithBuilding)
            {
                if (Application.isPlaying)
                {
                    // 1. SCHRITT: Nur despawnen, wenn es auf dem Netzwerk überhaupt aktiv gespawnt ist!
                    if (netObj.IsSpawned)
                    {
                        if (netObj.IsSceneObject ?? false)
                        {
                            netObj.Despawn(false);
                            netObj.gameObject.SetActive(false);
                        }
                        else
                        {
                            netObj.Despawn(true);
                        }
                    }
                    else
                    {
                        // Fallback: Wenn es noch nicht auf dem Netzwerk war, einfach normal lokal zerstören
                        Destroy(netObj.gameObject);
                    }
                }
                else
                {
                    DestroyImmediate(netObj.gameObject);
                }

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

    // =================================================================================
    // ASYNCHRONOUS PERFORMANCE ROUTINE (Runtime)
    // =================================================================================
    private IEnumerator SpawnObjectsInternalRoutine()
    {
        if (roadSpline == null || areaObject == null || prefabsToSpawn == null || prefabsToSpawn.Count == 0) yield break;

        if (multiSplineDrawer != null)
        {
            roadWidth = multiSplineDrawer.splineWidth;
        }

        float halfWidth = (areaObject.localScale.x * 10f) / 2f;
        float halfLength = (areaObject.localScale.z * 10f) / 2f;
        float halfRoadWidth = roadWidth * 0.5f;

        int objectsSpawnedThisFrame = 0;

        while (spawnedNetworkObjects.Count < amount)
        {
            GameObject selectedPrefab = GetWeightedRandomPrefab();
            if (selectedPrefab == null) yield break;

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

                // Increment frame threshold tracker
                objectsSpawnedThisFrame++;

                if (objectsSpawnedThisFrame >= maxSpawnsPerFrame)
                {
                    objectsSpawnedThisFrame = 0;
                    yield return null; // Distribute workload over multiple frames
                }
            }
            else
            {
                // Break out of the loop if we repeatedly fail to find empty space 
                // to prevent endless loops on small terrains
                yield return null;
            }
        }

        Debug.Log($"<color=lime>Done!</color> Random objects completely generated. Total size: {spawnedNetworkObjects.Count}");
    }

    // =================================================================================
    // IMMEDIATE EDITOR FALLBACK (Non-Coroutines for Unity ContextMenus)
    // =================================================================================
    private void SpawnObjectsImmediateEditor()
    {
        if (roadSpline == null || areaObject == null || prefabsToSpawn == null || prefabsToSpawn.Count == 0) return;

        if (multiSplineDrawer != null) roadWidth = multiSplineDrawer.splineWidth;

        float halfWidth = (areaObject.localScale.x * 10f) / 2f;
        float halfLength = (areaObject.localScale.z * 10f) / 2f;
        float halfRoadWidth = roadWidth * 0.5f;

        int buildingsToCreate = amount - spawnedNetworkObjects.Count;

        for (int i = 0; i < buildingsToCreate; i++)
        {
            GameObject selectedPrefab = GetWeightedRandomPrefab();
            if (selectedPrefab == null) break;

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
                GameObject newBuilding = Instantiate(selectedPrefab, finalPos, spawnRotation, this.transform);
                NetworkObject netObj = newBuilding.GetComponent<NetworkObject>();
                if (netObj != null) spawnedNetworkObjects.Add(netObj);
            }
        }
    }

    private GameObject GetWeightedRandomPrefab()
    {
        float totalWeight = 0f;
        foreach (var item in prefabsToSpawn)
        {
            if (item.prefab != null && item.weight > 0f) totalWeight += item.weight;
        }

        if (totalWeight <= 0f) return null;

        float randomRoll = UnityEngine.Random.Range(0f, totalWeight);
        float currentWeightTracker = 0f;

        foreach (var item in prefabsToSpawn)
        {
            if (item.prefab == null || item.weight <= 0f) continue;

            currentWeightTracker += item.weight;
            if (randomRoll <= currentWeightTracker) return item.prefab;
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