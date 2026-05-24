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
    [SerializeField] private float spacingFromRoad = 0f;
    [Tooltip("The intervals (in meters) at which the script checks for building slots along the spline.")]
    [SerializeField] private float spawnInterval = 0.1f;
    [Tooltip("Maximum random variation added or subtracted from the spawn interval.")]
    [SerializeField] private float spawnIntervalRandomness = 0f;

    [Header("Collision Layers")]
    [Tooltip("LayerMask for objects (e.g., Buildings, Street Light) to check for collisions and demolish blocking objects.")]
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

        float microScaleFactor = 0.04f;
        Vector3 targetScale = new Vector3(microScaleFactor, microScaleFactor, microScaleFactor);

        // Calculate the orientation (rotation) of the house in advance
        Vector3 lookDir = isRightSide ? -rightDirection : rightDirection;
        Quaternion spawnRotation = Quaternion.LookRotation(lookDir);

        // ============================================================================
        // ROTATION FIX:
        // ============================================================================
        // We mentally transform the unscaled corners of the BoxCollider into the 
        // final rotation and scaling. From this, we project the exact width 
        // that the rotated house occupies in the direction of the road (rightDirection).
        Matrix4x4 localToWorldMatrix = Matrix4x4.TRS(Vector3.zero, spawnRotation, targetScale);

        Vector3 halfSize = col.size * 0.5f;
        Vector3 localCenter = col.center;

        // We test the outer corner points of the collider whilst it is rotating
        Vector3 p1 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(halfSize.x, 0, halfSize.z));
        Vector3 p2 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(-halfSize.x, 0, halfSize.z));
        Vector3 p3 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(halfSize.x, 0, -halfSize.z));
        Vector3 p4 = localToWorldMatrix.MultiplyPoint3x4(localCenter + new Vector3(-halfSize.x, 0, -halfSize.z));

        // We measure the maximum deviation along the road direction vector
        float proj1 = Vector3.Dot(p1, rightDirection);
        float proj2 = Vector3.Dot(p2, rightDirection);
        float proj3 = Vector3.Dot(p3, rightDirection);
        float proj4 = Vector3.Dot(p4, rightDirection);

        float maxProj = Mathf.Max(Mathf.Abs(proj1), Mathf.Abs(proj2), Mathf.Abs(proj3), Mathf.Abs(proj4));

        // That is exactly half the width of the rotated house facing the street!
        float rotatedObjectHalfWidth = maxProj;

        float halfRoadWidth = (multiSplineDrawer != null) ? multiSplineDrawer.splineWidth * 0.5f : 0f;

        // UNIFIED FORMULA: Half the road width + desired distance + exactly half the width of the house
        float totalOffset = halfRoadWidth + spacingFromRoad + rotatedObjectHalfWidth;

        Vector3 spawnDirection = isRightSide ? rightDirection : -rightDirection;
        Vector3 spawnPosition = roadCenter + (spawnDirection * totalOffset);
        spawnPosition.y = splineContainer.transform.position.y;

        // Work out the exact dimensions for the test box in the room
        Vector3 unrotatedExtents = (col.size * microScaleFactor) * 0.5f;
        Vector3 checkBoxExtents = unrotatedExtents;
        checkBoxExtents.x += spacingFromRoad;
        checkBoxExtents.z += spawnInterval;
        checkBoxExtents.y += 0.5f;

        if (!Physics.CheckBox(spawnPosition, checkBoxExtents, spawnRotation, avoidanceLayers))
        {
            GameObject newObj = Instantiate(randomPrefab, spawnPosition, spawnRotation);
            newObj.transform.localScale = targetScale;

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