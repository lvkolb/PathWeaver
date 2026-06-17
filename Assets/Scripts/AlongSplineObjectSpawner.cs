using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Netcode; // Required for Netcode integration

public class AlongSplineObjectSpawner : NetworkBehaviour
{
    [System.Serializable]
    public struct SpawnGroupConfiguration
    {
        [Header("Group Identity")]
        public string groupName;

        [Header("References")]
        [Tooltip("The parent folder gameobject in which objects of this specific group are spawned. MUST have a NetworkObject component at runtime!")]
        public Transform objectsFolder;

        [Header("Spawn Pool")]
        public List<GameObject> objectPrefabs;

        [Header("Placement Settings")]
        [Tooltip("Clear clearance distance measured directly from the generated road edge.")]
        public float spacingFromRoad;
        [Tooltip("The step distance along the spline layout for these objects.")]
        public float spawnInterval;
        [Tooltip("Maximum random variation added or subtracted from the step distance to break grid alignment.")]
        public float spawnIntervalRandomness;

        [Header("Collision Layers")]
        [Tooltip("MUST include the layer your house prefabs are on so they block each other and get demolished!")]
        public LayerMask avoidanceLayers;

        [Header("Side Toggle")]
        public bool spawnOnRightSide;
        public bool spawnOnLeftSide;
    }

    [Header("References")]
    [SerializeField] private SplineContainer splineContainer;
    [SerializeField] private MultiSplineDrawer multiSplineDrawer;

    [Header("Multi-Pool Configurations")]
    [SerializeField] private List<SpawnGroupConfiguration> spawnGroups = new List<SpawnGroupConfiguration>();

    // Tracks how many splines have already been fully processed and decorated
    private int processedSplineCount = 0;

    // Track network objects using NetworkObject references to support network operations and destruction
    private List<NetworkObject> spawnedNetworkDecoObjects = new List<NetworkObject>();

    // =================================================================================
    // MULTIPLAYER SERVER/CLIENT ROUTINE (Unity 6 Compatible)
    // =================================================================================

    public void RequestSplineSpawn()
    {
        if (Application.isPlaying)
        {
            if (IsServer)
            {
                CheckForNewSplinesAndSpawn();
            }
            else if (IsClient)
            {
                TriggerSplineSpawnRpc();
            }
        }
        else
        {
            CheckForNewSplinesAndSpawn();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void TriggerSplineSpawnRpc()
    {
        CheckForNewSplinesAndSpawn();
    }

    // =================================================================================
    // FUNCTION 1: CHECK FOR NEW SPLINES (DEMOLISH & NEW BUILD)
    // =================================================================================
    [ContextMenu("Check For New Splines And Spawn")]
    public void CheckForNewSplinesAndSpawn()
    {
        if (Application.isPlaying && multiSplineDrawer != null && multiSplineDrawer.IsDrawingActive)
        {
            return;
        }

        if (Application.isPlaying && !IsServer) return;
        if (splineContainer == null) return;

        System.Random rng = new System.Random();

        // STEP 1: RÄUME ALLE STRASSEN AUF
        // Jedes Mal, wenn eine Straße fertig wird, prüfen wir JEDEN Spline auf Kollisionen mit alten Häusern
        for (int i = 0; i < splineContainer.Splines.Count; i++)
        {
            DemolishObjectsInWayOfSpline(i);
        }

        // STEP 2: SPAWNE NEUE HÄUSER NUR AN NEUEN STRASSEN
        for (int i = 0; i < splineContainer.Splines.Count; i++)
        {
            if (i >= processedSplineCount)
            {
                foreach (var group in spawnGroups)
                {
                    SpawnGroupForSingleSpline(i, group, rng);
                }
            }
        }

        // Counter auf den aktuellen Stand bringen
        processedSplineCount = splineContainer.Splines.Count;
    }

    private void DemolishObjectsInWayOfSpline(int splineIndex)
    {
        var spline = splineContainer.Splines[splineIndex];
        Matrix4x4 containerMatrix = splineContainer.transform.localToWorldMatrix;
        float splineLength = SplineUtility.CalculateLength(spline, containerMatrix);
        if (splineLength <= 0) return;

        float halfRoadWidth = (multiSplineDrawer != null) ? multiSplineDrawer.splineWidth * 0.5f : 1.0f;

        float checkStep = 0.5f;
        float currentDistance = 0f;
        int demolishCount = 0;

        // Erhöhte Box-Höhe (Y), um schief stehende VR-Objekte oder komplexe Prefabs sicher zu treffen
        Vector3 roadCheckHalfSize = new Vector3(halfRoadWidth, 10.0f, checkStep * 0.5f);

        LayerMask combinedLayers = 0;
        foreach (var group in spawnGroups) combinedLayers |= group.avoidanceLayers;

        while (currentDistance <= splineLength)
        {
            float t = Mathf.Clamp01(currentDistance / splineLength);
            splineContainer.Evaluate(splineIndex, t, out float3 worldPos, out float3 worldTangent, out float3 worldUp);

            Vector3 tangent = Vector3.Normalize((Vector3)worldTangent);
            if (tangent != Vector3.zero)
            {
                Quaternion roadRotation = Quaternion.LookRotation(tangent);
                Vector3 centerOfRoad = (Vector3)worldPos;

                Collider[] hitColliders = Physics.OverlapBox(centerOfRoad, roadCheckHalfSize, roadRotation, combinedLayers);

                foreach (var col in hitColliders)
                {
                    if (col.gameObject != splineContainer.gameObject)
                    {
                        // FIX 1: Greife das NetworkObject rigoros aus der Root oder den Parents ab
                        NetworkObject netObj = col.GetComponentInParent<NetworkObject>();

                        if (netObj != null)
                        {
                            // Aus der internen Liste austragen, falls es von diesem Spawner stammte
                            if (spawnedNetworkDecoObjects.Contains(netObj))
                            {
                                spawnedNetworkDecoObjects.Remove(netObj);
                            }

                            demolishCount++;

                            if (Application.isPlaying)
                            {
                                if (netObj.IsSpawned)
                                    netObj.Despawn(true); // Über Netcode für alle VR-Brillen weglöschen
                                else
                                    Destroy(netObj.gameObject);
                            }
                            else
                            {
                                DestroyImmediate(netObj.gameObject);
                            }
                        }
                    }
                }
            }
            currentDistance += checkStep;
        }

        if (demolishCount > 0)
        {
            Debug.Log($"<color=red>[Demolition]</color> {demolishCount} objects removed from the path of road {splineIndex}!");
        }
    }

    // =================================================================================
    // FUNCTION 2: SPAWNING FOR A SPECIFIC CONFIGURATION GROUP
    // =================================================================================
    private void SpawnGroupForSingleSpline(int splineIndex, SpawnGroupConfiguration group, System.Random rng)
    {
        if (splineContainer == null || group.objectPrefabs == null || group.objectPrefabs.Count == 0) return;

        var spline = splineContainer.Splines[splineIndex];
        Matrix4x4 containerMatrix = splineContainer.transform.localToWorldMatrix;
        float splineLength = SplineUtility.CalculateLength(spline, containerMatrix);
        if (splineLength <= 0) return;

        float currentDistance = 0f;

        while (currentDistance <= splineLength)
        {
            float t = Mathf.Clamp01(currentDistance / splineLength);
            splineContainer.Evaluate(splineIndex, t, out float3 worldPos, out float3 worldTangent, out float3 worldUp);

            Vector3 tangent = Vector3.Normalize((Vector3)worldTangent);
            Vector3 up = Vector3.Normalize((Vector3)worldUp);

            if (tangent != Vector3.zero)
            {
                Vector3 rightVector = Vector3.Cross(up, tangent).normalized;

                if (group.spawnOnRightSide)
                {
                    TryPlaceSideObject((Vector3)worldPos, rightVector, true, group, rng);
                }

                if (group.spawnOnLeftSide)
                {
                    TryPlaceSideObject((Vector3)worldPos, rightVector, false, group, rng);
                }
            }

            float randomness = (float)(rng.NextDouble() * (group.spawnIntervalRandomness * 2) - group.spawnIntervalRandomness);
            float nextStep = group.spawnInterval + randomness;

            if (nextStep < 0.001f) nextStep = 0.001f;

            currentDistance += nextStep;
        }
    }

    private void TryPlaceSideObject(Vector3 roadCenter, Vector3 rightDirection, bool isRightSide, SpawnGroupConfiguration group, System.Random rng)
    {
        int randomIndex = rng.Next(0, group.objectPrefabs.Count);
        GameObject randomPrefab = group.objectPrefabs[randomIndex];

        if (randomPrefab == null) return;
        BoxCollider col = randomPrefab.GetComponent<BoxCollider>();
        if (col == null) return;

        float microScaleFactor = 0.04f;
        Vector3 targetScale = new Vector3(microScaleFactor, microScaleFactor, microScaleFactor);

        Vector3 lookDir = isRightSide ? -rightDirection : rightDirection;
        Quaternion spawnRotation = Quaternion.LookRotation(lookDir);

        Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(Vector3.zero, spawnRotation, targetScale);

        Vector3 halfSize = col.size * 0.5f;
        Vector3 localCenter = col.center;

        Vector3 p1 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(halfSize.x, 0, halfSize.z));
        Vector3 p2 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(-halfSize.x, 0, halfSize.z));
        Vector3 p3 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(halfSize.x, 0, -halfSize.z));
        Vector3 p4 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(-halfSize.x, 0, -halfSize.z));

        float proj1 = Vector3.Dot(p1, rightDirection);
        float proj2 = Vector3.Dot(p2, rightDirection);
        float proj3 = Vector3.Dot(p3, rightDirection);
        float proj4 = Vector3.Dot(p4, rightDirection);

        float maxProj = Mathf.Max(Mathf.Abs(proj1), Mathf.Abs(proj2), Mathf.Abs(proj3), Mathf.Abs(proj4));
        float rotatedObjectHalfWidth = maxProj;

        float halfRoadWidth = (multiSplineDrawer != null) ? multiSplineDrawer.splineWidth * 0.5f : 0f;

        float totalOffset = halfRoadWidth + group.spacingFromRoad + rotatedObjectHalfWidth;

        Vector3 spawnDirection = isRightSide ? rightDirection : -rightDirection;
        Vector3 spawnPosition = roadCenter + (spawnDirection * totalOffset);
        spawnPosition.y = splineContainer.transform.position.y;

        Vector3 unrotatedExtents = (col.size * microScaleFactor) * 0.5f;
        Vector3 checkBoxExtents = unrotatedExtents;
        checkBoxExtents.x += group.spacingFromRoad;
        checkBoxExtents.z += group.spawnInterval * 0.4f;
        checkBoxExtents.y += 0.5f;

        // FIX 2: GLOBALE DISTANZ-VALIDIERUNG GEGEN ALLE NETCODE OBJEKTE IN DER SZENE
        // Verhindert verlässlicher als Physics.CheckBox, dass Häuser aufeinander klatschen.
        if (Application.isPlaying)
        {
            float spawnSafetyRadius = group.spawnInterval * 0.85f;

            // Hol dir JEDES aktive NetworkObject in der Szene (performant in Unity 6)
            NetworkObject[] allNetObjects = FindObjectsByType<NetworkObject>(FindObjectsInactive.Exclude);

            foreach (var netObj in allNetObjects)
            {
                // Ignoriere den Spawner, Hände oder die Straße selbst
                if (netObj == this.NetworkObject || netObj.gameObject == splineContainer.gameObject)
                    continue;

                // Wenn ein beliebiges Netzwerk-Objekt (Haus) zu nah dran ist -> Nicht bauen!
                if (Vector3.Distance(spawnPosition, netObj.transform.position) < spawnSafetyRadius)
                {
                    return;
                }
            }
        }

        // Physik-Backup-Check gegen statische Umweltobjekte
        if (!Physics.CheckBox(spawnPosition, checkBoxExtents, spawnRotation, group.avoidanceLayers))
        {
            GameObject newObj = Instantiate(randomPrefab, spawnPosition, spawnRotation);
            newObj.transform.localScale = targetScale;

            if (Application.isPlaying)
            {
                NetworkObject netObj = newObj.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    netObj.Spawn(true);
                    spawnedNetworkDecoObjects.Add(netObj);

                    Transform targetFolder = (group.objectsFolder != null) ? group.objectsFolder : this.transform;
                    NetworkObject parentNetObj = targetFolder.GetComponent<NetworkObject>();

                    if (parentNetObj != null && parentNetObj.IsSpawned)
                    {
                        netObj.TrySetParent(targetFolder);
                    }
                    else
                    {
                        newObj.transform.parent = targetFolder;
                    }
                }
                else
                {
                    Debug.LogError($"Prefab {randomPrefab.name} is missing a NetworkObject component!");
                    Destroy(newObj);
                }
            }
            else
            {
                if (group.objectsFolder != null)
                    newObj.transform.parent = group.objectsFolder;
                else
                    newObj.transform.parent = this.transform;

                NetworkObject netObj = newObj.GetComponent<NetworkObject>();
                if (netObj != null) spawnedNetworkDecoObjects.Add(netObj);
            }
        }
    }

    // =================================================================================
    // FUNCTION 3: CLEAR ALL
    // =================================================================================
    public void ClearAllSpawnedObjects()
    {
        if (Application.isPlaying && !IsServer) return;

        for (int i = spawnedNetworkDecoObjects.Count - 1; i >= 0; i--)
        {
            NetworkObject netObj = spawnedNetworkDecoObjects[i];
            if (netObj != null)
            {
                if (Application.isPlaying)
                {
                    if (netObj.IsSpawned)
                        netObj.Despawn(true);
                    else
                        Destroy(netObj.gameObject);
                }
                else
                {
                    DestroyImmediate(netObj.gameObject);
                }
            }
        }

        spawnedNetworkDecoObjects.Clear();
        processedSplineCount = 0;
        Debug.Log("All multi-pool objects generated by this spawner have been successfully cleared!");
    }

    public void ClearAndCheck()
    {
        ClearAllSpawnedObjects();
        CheckForNewSplinesAndSpawn();
    }
}