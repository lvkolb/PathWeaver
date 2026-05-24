using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;

public class RandomObjectSpawnerManager : MonoBehaviour
{
    [Header("Prefabs & quantity")]
    public List<GameObject> prefabsToSpawn = new List<GameObject>();
    public int amount = 100;

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

    private List<GameObject> spawnedObjects = new List<GameObject>();

    public void Start()
    {
        if (multiSplineDrawer != null)
        {
            roadWidth = multiSplineDrawer.splineWidth;
        }
    }

    [ContextMenu("Refresh object spawning")]
    public void RefreshObjectSpawning()
    {
        // First, remove the houses that are currently in the way
        ValidateExistingBuildings();
    }


    [ContextMenu("Clear All Objects")]
    public void ClearAllObjects()
    {
        // We iterate through the list backwards to ensure it is safely deleted
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            GameObject building = spawnedObjects[i];

            if (building != null)
            {
                if (Application.isPlaying)
                    Destroy(building);
                else
                    DestroyImmediate(building);
            }
        }

        // Clear list
        spawnedObjects.Clear();

        // Safety check if running in editor mode to clean remaining orphan child transform objects
        if (!Application.isPlaying && transform.childCount > 0)
        {
            Debug.Log("Cleaning up leftover child objects...");
            List<GameObject> children = new List<GameObject>();
            foreach (Transform child in transform) children.Add(child.gameObject);
            foreach (GameObject child in children) DestroyImmediate(child);
        }

        Debug.Log("All buildings cleared!");
    }

    private void ValidateExistingBuildings()
    {
        int startCount = spawnedObjects.Count;
        float halfRoadWidth = roadWidth * 0.5f;

        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            GameObject building = spawnedObjects[i];
            if (building == null) continue;

            BoxCollider col = building.GetComponent<BoxCollider>();
            if (col == null) continue;

            Vector3 prefabScale = building.transform.localScale;
            Vector3 halfSize = Vector3.Scale(col.size, prefabScale) * 0.5f;
            float houseSafetyRadius = Mathf.Max(halfSize.x, halfSize.z);

            // Calculate exact safety clearance threshold relative to your true road borders
            float totalRequiredDistance = halfRoadWidth + spacingFromRoad + houseSafetyRadius;

            // Check whether the object is too close to the road/spline boundaries
            if (!IsPositionFarFromAllSplines(building.transform.position, totalRequiredDistance))
            {
                if (Application.isPlaying)
                    Destroy(building);
                else
                    DestroyImmediate(building);

                spawnedObjects.RemoveAt(i);
            }
        }

        int removedCount = startCount - spawnedObjects.Count;
        Debug.Log($"<color=orange>Validation complete:</color> {removedCount} Buildings removed. " +
                  $"<color=lime>Current total: {spawnedObjects.Count}</color>");
    }

    [ContextMenu("Spawn objects")]
    public void SpawnObjects()
    {
        if (roadSpline == null || areaObject == null || prefabsToSpawn == null || prefabsToSpawn.Count == 0) return;

        if (multiSplineDrawer != null)
        {
            roadWidth = multiSplineDrawer.splineWidth;
        }

        // Calculating the area constraints (Plane standard footprint is 10 units base size)
        float halfWidth = (areaObject.localScale.x * 10f) / 2f;
        float halfLength = (areaObject.localScale.z * 10f) / 2f;
        float halfRoadWidth = roadWidth * 0.5f;

        int buildingsToCreate = amount - spawnedObjects.Count;
        System.Random rng = new System.Random();

        for (int i = 0; i < buildingsToCreate; i++)
        {
            int randomIndex = rng.Next(0, prefabsToSpawn.Count);
            GameObject selectedPrefab = prefabsToSpawn[randomIndex];
            if (selectedPrefab == null) continue;

            BoxCollider col = selectedPrefab.GetComponent<BoxCollider>();
            if (col == null) continue;

            Vector3 prefabScale = selectedPrefab.transform.localScale;
            Vector3 finalExtents = Vector3.Scale(col.size, prefabScale) * 0.5f;

            // Apply single spacing value bounds directly across your check constraints
            float houseSafetyRadius = Mathf.Max(finalExtents.x, finalExtents.z);
            float totalRequiredDistance = halfRoadWidth + spacingFromRoad + houseSafetyRadius;

            bool validPosFound = false;
            Vector3 finalPos = Vector3.zero;
            int attempts = 0;

            // Box dimensions setup including safety clearance borders
            Vector3 checkBoxExtents = finalExtents;
            checkBoxExtents.x += spacingFromRoad;
            checkBoxExtents.z += spacingFromRoad;
            checkBoxExtents.y += 0.5f;

            while (!validPosFound && attempts < 50)
            {
                attempts++;
                float randomX = UnityEngine.Random.Range(-halfWidth, halfWidth);
                float randomZ = UnityEngine.Random.Range(-halfLength, halfLength);
                Vector3 testPos = areaObject.position + new Vector3(randomX, areaObject.position.y, randomZ);

                if (IsPositionFarFromAllSplines(testPos, totalRequiredDistance))
                {
                    // Check for overlap collisions with objects tagged inside avoidanceLayers
                    if (!Physics.CheckBox(testPos, checkBoxExtents, Quaternion.identity, avoidanceLayers))
                    {
                        finalPos = testPos;
                        validPosFound = true;
                    }
                }
            }

            if (validPosFound)
            {
                // Instantiate at the discovered safe point with default rotation identity orientation
                GameObject newBuilding = Instantiate(selectedPrefab, finalPos, Quaternion.identity, transform);
                spawnedObjects.Add(newBuilding);
            }
        }

        Debug.Log($"<color=lime>Done!</color> Random objects created. Current setup size: {spawnedObjects.Count}");
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