using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Netcode;

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

    private int processedSplineCount = 0;
    private List<NetworkObject> spawnedNetworkDecoObjects = new List<NetworkObject>();

    // Structure to cache gizmo data for visualization in the Editor scene view
    private struct GizmoDebugData
    {
        public Vector3 position;
        public float radius;
        public Color color;
    }
    private List<GizmoDebugData> gizmoVisuals = new List<GizmoDebugData>();

    private void Start()
    {
        // Execute initial spawning for pre-existing splines if we are the server/host
        if (Application.isPlaying)
        {
            if (IsServer)
            {
                SpawnObjectsForPreExistingSplines();
            }
        }
        else
        {
            // Fallback for Editor mode configuration
            if (splineContainer != null)
            {
                processedSplineCount = splineContainer.Splines.Count;
            }
        }
    }

    private void SpawnObjectsForPreExistingSplines()
    {
        if (splineContainer == null) return;

        System.Random rng = new System.Random();

        // 1. First clear any overlapping decoration assets that might block the road path
        for (int i = 0; i < splineContainer.Splines.Count; i++)
        {
            DemolishObjectsInWayOfSpline(i);
        }

        // 2. Spawn objects for ALL splines currently existing in the container setup
        for (int i = 0; i < splineContainer.Splines.Count; i++)
        {
            foreach (var group in spawnGroups)
            {
                SpawnGroupForSingleSpline(i, group, rng);
            }
        }

        // 3. Lock down the counter so subsequent runtime drawing actions only evaluate brand new splines
        processedSplineCount = splineContainer.Splines.Count;

        Debug.Log($"[Spawner] Initialized pre-existing layout. Generated assets for {processedSplineCount} starter splines.");
    }

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

    [ContextMenu("Check for new Splines and Spawn")]
    public void CheckForNewSplinesAndSpawn()
    {
        if (Application.isPlaying && multiSplineDrawer != null && multiSplineDrawer.IsDrawingActive)
        {
            return;
        }

        if (Application.isPlaying && !IsServer) return;
        if (splineContainer == null) return;

        System.Random rng = new System.Random();

        for (int i = 0; i < splineContainer.Splines.Count; i++)
        {
            DemolishObjectsInWayOfSpline(i);
        }

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
                        NetworkObject netObj = col.GetComponentInParent<NetworkObject>();

                        if (netObj != null)
                        {
                            if (spawnedNetworkDecoObjects.Contains(netObj))
                            {
                                spawnedNetworkDecoObjects.Remove(netObj);
                            }

                            demolishCount++;

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
                }
            }
            currentDistance += checkStep;
        }

        if (demolishCount > 0)
        {
            Debug.Log($"<color=red>[Demolition]</color> {demolishCount} objects removed from the path of road {splineIndex}!");
        }
    }

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
                    TryPlaceSideObject(splineIndex, (Vector3)worldPos, rightVector, true, group, rng);
                }

                if (group.spawnOnLeftSide)
                {
                    TryPlaceSideObject(splineIndex, (Vector3)worldPos, rightVector, false, group, rng);
                }
            }

            float randomness = (float)(rng.NextDouble() * (group.spawnIntervalRandomness * 2) - group.spawnIntervalRandomness);
            float nextStep = group.spawnInterval + randomness;

            if (nextStep < 0.001f) nextStep = 0.001f;

            currentDistance += nextStep;
        }
    }

    private void TryPlaceSideObject(int currentSplineIndex, Vector3 roadCenter, Vector3 rightDirection, bool isRightSide, SpawnGroupConfiguration group, System.Random rng)
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

        // ---------------------------------------------------------------------------------
        // ULTIMATE SPLINE-DISTANCE CHECK (Bypasses Physics/Collider Bugs entirely)
        // ---------------------------------------------------------------------------------
        // Calculate the 4 world-space base corners of the actual house prefab
        Vector3 objExtents = (col.size * microScaleFactor) * 0.5f;
        Vector3 objCenterOffset = Vector3.Scale(col.center, targetScale);
        Vector3 finalCenter = spawnPosition + spawnRotation * objCenterOffset;

        Vector3[] houseCheckPoints = new Vector3[9];
        houseCheckPoints[0] = finalCenter;
        houseCheckPoints[1] = finalCenter + spawnRotation * new Vector3(objExtents.x, 0, objExtents.z);
        houseCheckPoints[2] = finalCenter + spawnRotation * new Vector3(-objExtents.x, 0, objExtents.z);
        houseCheckPoints[3] = finalCenter + spawnRotation * new Vector3(objExtents.x, 0, -objExtents.z);
        houseCheckPoints[4] = finalCenter + spawnRotation * new Vector3(-objExtents.x, 0, -objExtents.z);
        houseCheckPoints[5] = finalCenter + spawnRotation * new Vector3(objExtents.x, 0, 0);
        houseCheckPoints[6] = finalCenter + spawnRotation * new Vector3(-objExtents.x, 0, 0);
        houseCheckPoints[7] = finalCenter + spawnRotation * new Vector3(0, 0, objExtents.z);
        houseCheckPoints[8] = finalCenter + spawnRotation * new Vector3(0, 0, -objExtents.z);

        for (int s = 0; s < splineContainer.Splines.Count; s++)
        {
            // TARGETED FIX: Ignore the road this house is actually supposed to sit next to!
            if (s == currentSplineIndex) continue;

            var targetSpline = splineContainer.Splines[s];

            foreach (Vector3 point in houseCheckPoints)
            {
                float3 localPoint = splineContainer.transform.InverseTransformPoint(point);

                SplineUtility.GetNearestPoint(targetSpline, localPoint, out float3 nearestLocalPos, out float t);
                Vector3 nearestWorldPos = splineContainer.transform.TransformPoint(nearestLocalPos);

                float distanceToRoadCenter = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(nearestWorldPos.x, nearestWorldPos.z));

                if (distanceToRoadCenter < (halfRoadWidth + 0.3f))
                {
                    // Hit an INTERSECTING road (not its own!). Skip spawning.
                    return;
                }
            }
        }

        // ---------------------------------------------------------------------------------
        // BEIBEHALTEN: METHODE 2 – Dynamic clearance check against other spawned objects
        // ---------------------------------------------------------------------------------
        if (Application.isPlaying)
        {
            float scaledX = col.size.x * microScaleFactor;
            float scaledZ = col.size.z * microScaleFactor;
            float prefabDiagonal = Mathf.Sqrt(scaledX * scaledX + scaledZ * scaledZ);
            float dynamicSafetyRadius = (prefabDiagonal * 0.5f) * 1.05f;

            NetworkObject[] allNetObjects = FindObjectsByType<NetworkObject>(FindObjectsInactive.Exclude);

            foreach (var netObj in allNetObjects)
            {
                if (netObj == this.NetworkObject || netObj.gameObject == splineContainer.gameObject)
                    continue;

                bool isSameGroup = false;
                if (group.objectsFolder != null && netObj.transform.IsChildOf(group.objectsFolder))
                {
                    isSameGroup = true;
                }
                else
                {
                    foreach (var prefab in group.objectPrefabs)
                    {
                        if (netObj.gameObject.name.StartsWith(prefab.name))
                        {
                            isSameGroup = true;
                            break;
                        }
                    }
                }

                if (isSameGroup && Vector3.Distance(spawnPosition, netObj.transform.position) < dynamicSafetyRadius)
                {
                    return; // Space occupied by another deco object, skip spawning
                }
            }
        }

        // Final environment collision check (avoidance layers)
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

            // Caching for Editor preview
            float sX = col.size.x * microScaleFactor;
            float sZ = col.size.z * microScaleFactor;
            float diag = Mathf.Sqrt(sX * sX + sZ * sZ);
            float finalRadius = (diag * 0.5f) * 1.05f;

            Color groupColor = Color.cyan;
            if (!string.IsNullOrEmpty(group.groupName))
            {
                float hue = Mathf.Abs(group.groupName.GetHashCode() % 100) / 100f;
                groupColor = Color.HSVToRGB(hue, 0.8f, 0.9f);
            }

            gizmoVisuals.Add(new GizmoDebugData { position = spawnPosition, radius = finalRadius, color = groupColor });
        }
    }
    [ContextMenu("Clear All Spawned Objects")]
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
        gizmoVisuals.Clear(); // Clear the gizmos
        processedSplineCount = 0;
        Debug.Log("All multi-pool objects generated by this spawner have been successfully cleared!");

    }

    [ContextMenu("Clear and Check")]
    public void ClearAndCheck()
    {
        ClearAllSpawnedObjects();
        CheckForNewSplinesAndSpawn();
    }


    // =================================================================================
    // GIZMOS VISUALIZATION
    // =================================================================================
    private void OnDrawGizmosSelected()
    {
        if (gizmoVisuals == null || gizmoVisuals.Count == 0) return;

        foreach (var debugData in gizmoVisuals)
        {
            Gizmos.color = debugData.color;

            // Draw the outer safety radius boundary as a flat circle
            int segments = 24;
            Vector3 lastPoint = debugData.position + new Vector3(debugData.radius, 0, 0);

            for (int i = 1; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                Vector3 nextPoint = debugData.position + new Vector3(Mathf.Cos(angle) * debugData.radius, 0, Mathf.Sin(angle) * debugData.radius);

                Gizmos.DrawLine(lastPoint, nextPoint);
                lastPoint = nextPoint;
            }

            // Draw a semi-transparent solid core in the center of the zone
            Gizmos.color = new Color(debugData.color.r, debugData.color.g, debugData.color.b, 0.2f);
            Gizmos.DrawSphere(debugData.position, debugData.radius * 0.15f);
        }
    }
}