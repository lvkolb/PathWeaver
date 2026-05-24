using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class AlongSplineObjectSpawner : MonoBehaviour
{
    // Struct to hold individual object pools and their specific placement parameters
    [System.Serializable]
    public struct SpawnGroupConfiguration
    {
        [Header("Group Identity")]
        [Tooltip("Gives the group a clear name in the inspector (e.g., Houses or Streetlamps).")]
        public string groupName;

        [Header("References")]
        [Tooltip("The parent folder gameobject in which objects of this specific group are spawned.")]
        public Transform objectsFolder;

        [Header("Spawn Pool")]
        [Tooltip("The prefabs assigned to this specific group (e.g., various house models or different light variants).")]
        public List<GameObject> objectPrefabs;

        [Header("Placement Settings")]
        [Tooltip("Clear clearance distance measured directly from the generated road edge.")]
        public float spacingFromRoad;
        [Tooltip("The step distance along the spline layout for these objects.")]
        public float spawnInterval;
        [Tooltip("Maximum random variation added or subtracted from the step distance to break grid alignment.")]
        public float spawnIntervalRandomness;

        [Header("Collision Layers")]
        [Tooltip("Which layers block these specific items during the placement query check?")]
        public LayerMask avoidanceLayers;

        [Header("Side Toggle")]
        [Tooltip("Should these specific objects spawn on the right side of the road layout?")]
        public bool spawnOnRightSide;
        [Tooltip("Should these specific objects spawn on the left side of the road layout?")]
        public bool spawnOnLeftSide;
    }

    [Header("References")]
    [SerializeField] private SplineContainer splineContainer;
    [Header("The field from which we dynamically read the current road width (splineWidth).")]
    [SerializeField] private MultiSplineDrawer multiSplineDrawer;

    [Header("Multi-Pool Configurations")]
    [Tooltip("Create custom generation sets here (e.g., Element 0 for residential houses, Element 1 for streetlights).")]
    [SerializeField] private List<SpawnGroupConfiguration> spawnGroups = new List<SpawnGroupConfiguration>();

    // Stores the references of the splines that have already been decorated
    private HashSet<Spline> processedSplines = new HashSet<Spline>();

    // Internal list to track generated objects so they can be safely removed on clear execution
    private List<GameObject> spawnedDecoObjects = new List<GameObject>();


    // =================================================================================
    // FUNCTION 1: CHECK FOR NEW SPLINES (DEMOLISH & NEW BUILD)
    // =================================================================================
    [ContextMenu("Check For New Splines And Spawn")]
    public void CheckForNewSplinesAndSpawn()
    {
        if (splineContainer == null) return;

        System.Random rng = new System.Random();

        for (int i = 0; i < splineContainer.Splines.Count; i++)
        {
            var spline = splineContainer.Splines[i];

            if (!processedSplines.Contains(spline))
            {
                DemolishObjectsInWayOfSpline(i);

                // Process each configuration group sequentially along the newly added spline line
                foreach (var group in spawnGroups)
                {
                    SpawnGroupForSingleSpline(i, group, rng);
                }

                processedSplines.Add(spline);
            }
        }
    }

    private void DemolishObjectsInWayOfSpline(int splineIndex)
    {
        var spline = splineContainer.Splines[splineIndex];
        float4x4 containerMatrix = splineContainer.transform.localToWorldMatrix;
        float splineLength = SplineUtility.CalculateLength(spline, containerMatrix);
        if (splineLength <= 0) return;

        float halfRoadWidth = (multiSplineDrawer != null) ? multiSplineDrawer.splineWidth * 0.5f : 1.0f;

        float checkStep = 0.5f;
        float currentDistance = 0f;
        int demolishCount = 0;

        Vector3 roadCheckHalfSize = new Vector3(halfRoadWidth, 3.0f, checkStep * 0.5f);

        // Combine the layer masks of all configuration groups for the global layout clearance sweep
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
                        if (spawnedDecoObjects.Contains(col.gameObject))
                        {
                            spawnedDecoObjects.Remove(col.gameObject);
                        }

                        demolishCount++;

                        if (Application.isPlaying)
                            Destroy(col.gameObject);
                        else
                            DestroyImmediate(col.gameObject);
                    }
                }
            }
            currentDistance += checkStep;
        }

        if (demolishCount > 0)
        {
            Debug.Log($"<color=red>[Demolition]</color> {demolishCount} blocking objects were flattened by the new road!");
        }
    }


    // =================================================================================
    // FUNCTION 2: SPAWNING FOR A SPECIFIC CONFIGURATION GROUP
    // =================================================================================
    private void SpawnGroupForSingleSpline(int splineIndex, SpawnGroupConfiguration group, System.Random rng)
    {
        if (splineContainer == null || group.objectPrefabs == null || group.objectPrefabs.Count == 0) return;

        var spline = splineContainer.Splines[splineIndex];
        float4x4 containerMatrix = splineContainer.transform.localToWorldMatrix;
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

                // --- SPAWN RIGHT SIDE (IF ENABLED IN GROUP) ---
                if (group.spawnOnRightSide)
                {
                    TryPlaceSideObject((Vector3)worldPos, rightVector, true, group, rng);
                }

                // --- SPAWN LEFT SIDE (IF ENABLED IN GROUP) ---
                if (group.spawnOnLeftSide)
                {
                    TryPlaceSideObject((Vector3)worldPos, rightVector, false, group, rng);
                }
            }

            // Apply group-specific spacing increment parameters
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

        // --- THE ADVANCED ROTATION FIX ---
        // Mathematically project the unscaled bounding box corners into world space orientation
        // using the final target rotation and micro scale dimensions.
        Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(Vector3.zero, spawnRotation, targetScale);

        Vector3 halfSize = col.size * 0.5f;
        Vector3 localCenter = col.center;

        // Extract outer footprint boundary corner vectors
        Vector3 p1 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(halfSize.x, 0, halfSize.z));
        Vector3 p2 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(-halfSize.x, 0, halfSize.z));
        Vector3 p3 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(halfSize.x, 0, -halfSize.z));
        Vector3 p4 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(-halfSize.x, 0, -halfSize.z));

        // Gauge real physical extension limits pointing directly towards the road margin track
        float proj1 = Vector3.Dot(p1, rightDirection);
        float proj2 = Vector3.Dot(p2, rightDirection);
        float proj3 = Vector3.Dot(p3, rightDirection);
        float proj4 = Vector3.Dot(p4, rightDirection);

        float maxProj = Mathf.Max(Mathf.Abs(proj1), Mathf.Abs(proj2), Mathf.Abs(proj3), Mathf.Abs(proj4));
        float rotatedObjectHalfWidth = maxProj;

        float halfRoadWidth = (multiSplineDrawer != null) ? multiSplineDrawer.splineWidth * 0.5f : 0f;

        // MAIN OFFSET FORMULA: Half road width + group side clearance margin + exact projected item boundary depth
        float totalOffset = halfRoadWidth + group.spacingFromRoad + rotatedObjectHalfWidth;

        Vector3 spawnDirection = isRightSide ? rightDirection : -rightDirection;
        Vector3 spawnPosition = roadCenter + (spawnDirection * totalOffset);
        spawnPosition.y = splineContainer.transform.position.y;

        // Calculate custom spatial query dimensions matching current active group rules
        Vector3 unrotatedExtents = (col.size * microScaleFactor) * 0.5f;
        Vector3 checkBoxExtents = unrotatedExtents;
        checkBoxExtents.x += group.spacingFromRoad;
        checkBoxExtents.z += group.spawnInterval;
        checkBoxExtents.y += 0.5f;

        // Query workspace utilizing group-specific layer filter boundaries
        if (!Physics.CheckBox(spawnPosition, checkBoxExtents, spawnRotation, group.avoidanceLayers))
        {
            GameObject newObj = Instantiate(randomPrefab, spawnPosition, spawnRotation);
            newObj.transform.localScale = targetScale;

            // FIX: Uses the folder specified directly inside this configuration element
            if (group.objectsFolder != null)
                newObj.transform.parent = group.objectsFolder;
            else
                newObj.transform.parent = this.transform;

            spawnedDecoObjects.Add(newObj);
        }
    }


    // =================================================================================
    // FUNCTION 3: CLEAR ALL
    // =================================================================================
    [ContextMenu("Clear All Spawned Objects")]
    public void ClearAllSpawnedObjects()
    {
        for (int i = spawnedDecoObjects.Count - 1; i >= 0; i--)
        {
            GameObject obj = spawnedDecoObjects[i];
            if (obj != null)
            {
                if (Application.isPlaying)
                    Destroy(obj);
                else
                    DestroyImmediate(obj);
            }
        }

        spawnedDecoObjects.Clear();
        processedSplines.Clear();
        Debug.Log("All multi-pool objects generated by this spawner have been successfully cleared!");
    }

    [ContextMenu("Clear and Check")]
    public void ClearAndCheck()
    {
        ClearAllSpawnedObjects();
        CheckForNewSplinesAndSpawn();
    }
}