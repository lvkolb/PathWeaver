using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class AlongSplineObjectSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SplineContainer splineContainer;
    [Header("The field from which we dynamically read the current road width (splineWidth).")]
    [SerializeField] private MultiSplineDrawer multiSplineDrawer;

    [Header("The gameobject in which all objects are spawned.")]
    [SerializeField] private Transform objectsFolder;

    [Header("Spawn Pool")]
    [SerializeField] private List<GameObject> objectPrefabs = new List<GameObject>();

    [Header("Placement Settings")]
    [Tooltip("The clear distance measured directly from the actual road edge to the house front face.")]
    [SerializeField] private float spacingFromRoad = 0.1f;
    [Tooltip("The intervals (in meters) at which the script checks for building slots along the spline.")]
    [SerializeField] private float spawnInterval = 0.1f;
    [Tooltip("Maximum random variation added or subtracted from the spawn interval.")]
    [SerializeField] private float spawnIntervalRandomness = 0f;

    [Header("Collision Layers")]
    [Tooltip("LayerMask for objects (e.g., Buildings, Strret Light) to check for collisions and demolish blocking objects.")]
    [SerializeField] private LayerMask avoidanceLayers;

    // Stores the references of the splines that have already been processed and decorated
    private HashSet<Spline> processedSplines = new HashSet<Spline>();

    // Internal list to track generated objects so they can be safely removed on clear
    private List<GameObject> spawnedDecoObjects = new List<GameObject>();


    // =================================================================================
    // FUNCTION 1: CHECK FOR NEW SPLINES (DEMOLISH & NEW BUILD)
    // =================================================================================
    [ContextMenu("Check For New Splines And Spawn")]
    public void CheckForNewSplinesAndSpawn()
    {
        if (splineContainer == null) return;

        // Dedicated C# System.Random instance to prevent the Unity seed-locking bug at micro-steps
        System.Random rng = new System.Random();

        for (int i = 0; i < splineContainer.Splines.Count; i++)
        {
            var spline = splineContainer.Splines[i];

            if (!processedSplines.Contains(spline))
            {
                DemolishObjectsInWayOfSpline(i);
                SpawnObjectsForSingleSpline(i, rng);
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

        while (currentDistance <= splineLength)
        {
            float t = Mathf.Clamp01(currentDistance / splineLength);
            splineContainer.Evaluate(splineIndex, t, out float3 worldPos, out float3 worldTangent, out float3 worldUp);

            Vector3 tangent = Vector3.Normalize((Vector3)worldTangent);
            if (tangent != Vector3.zero)
            {
                Quaternion roadRotation = Quaternion.LookRotation(tangent);
                Vector3 centerOfRoad = (Vector3)worldPos;

                Collider[] hitColliders = Physics.OverlapBox(centerOfRoad, roadCheckHalfSize, roadRotation, avoidanceLayers);

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
    // FUNCTION 2: SPAWNING (Left and Right alongside the Spline)
    // =================================================================================
    private void SpawnObjectsForSingleSpline(int splineIndex, System.Random rng)
    {
        if (splineContainer == null || objectPrefabs == null || objectPrefabs.Count == 0) return;

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

                // --- SPAWN RIGHT SIDE ---
                TryPlaceSideObject((Vector3)worldPos, rightVector, true, rng);

                // --- SPAWN LEFT SIDE ---
                TryPlaceSideObject((Vector3)worldPos, rightVector, false, rng);
            }

            float randomness = (float)(rng.NextDouble() * (spawnIntervalRandomness * 2) - spawnIntervalRandomness);
            float nextStep = spawnInterval + randomness;

            if (nextStep < 0.001f) nextStep = 0.001f;

            currentDistance += nextStep;
        }
    }

    private void TryPlaceSideObject(Vector3 roadCenter, Vector3 rightDirection, bool isRightSide, System.Random rng)
    {
        int randomIndex = rng.Next(0, objectPrefabs.Count);
        GameObject randomPrefab = objectPrefabs[randomIndex];

        if (randomPrefab == null) return;
        BoxCollider col = randomPrefab.GetComponent<BoxCollider>();
        if (col == null) return;

        Vector3 prefabScale = randomPrefab.transform.localScale;
        Vector3 finalExtents = Vector3.Scale(col.size, prefabScale) * 0.5f;

        float objectHalfWidth = Mathf.Max(finalExtents.x, finalExtents.z);
        float halfRoadWidth = (multiSplineDrawer != null) ? multiSplineDrawer.splineWidth * 0.5f : 0f;

        // UNIFIED FORMULA: Half road width + single spacing parameter + half width of variable object prefab
        float totalOffset = halfRoadWidth + spacingFromRoad + objectHalfWidth;

        Vector3 spawnDirection = isRightSide ? rightDirection : -rightDirection;
        Vector3 spawnPosition = roadCenter + (spawnDirection * totalOffset);
        spawnPosition.y = splineContainer.transform.position.y;

        Vector3 lookDir = isRightSide ? -rightDirection : rightDirection;
        Quaternion spawnRotation = Quaternion.LookRotation(lookDir);

        // ============================================================================
        // THE MICRO-SCALE FIX FOR THE PHYSICS CHECKBOX
        // ============================================================================
        Vector3 checkBoxExtents = finalExtents;

        // Width buffer (towards/away from road) is safely guarded by spacingFromRoad
        checkBoxExtents.x += spacingFromRoad;

        // LENGTH BUFFER FIX: In driving direction (Z), the box needs a buffer based on 
        // your spawnInterval, not the tiny spacingFromRoad. Otherwise, 1cm steps self-block instantly.
        checkBoxExtents.z += spawnInterval;

        checkBoxExtents.y += 0.5f; // Vertical safety clearance

        if (!Physics.CheckBox(spawnPosition, checkBoxExtents, spawnRotation, avoidanceLayers))
        {
            GameObject newObj = Instantiate(randomPrefab, spawnPosition, spawnRotation);

            if (objectsFolder != null)
                newObj.transform.parent = objectsFolder;
            else
                newObj.transform.parent = this.transform;

            spawnedDecoObjects.Add(newObj);
        }
    }


    // =================================================================================
    // FUNCTION 3: CLEAR / DEMOLISH ALL
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
        Debug.Log("All objects generated by this spawner have been successfully cleared!");
    }

    [ContextMenu("Clear and Check")]
    public void ClearAndCheck()
    {
        ClearAllSpawnedObjects();
        CheckForNewSplinesAndSpawn();
    }

}