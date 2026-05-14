using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;

public class BuildingManager : MonoBehaviour
{
    [Header("Prefabs & quantity")]
    public List<GameObject> prefabsToSpawn = new List<GameObject>();
    public int amount = 100;

    [Header("Road Settings")]
    public GameObject splineManager;
    public SplineContainer roadSpline;

    [Header("Avoid collision")]
    public LayerMask avoidanceLayers;
    [Header("Additional spacing buffer around buildings")]
    public float extraSpacing = 0.5f;

    [Header("Area Reference")]
    public Transform areaObject;

    [Header("Rotation Reference")]
    public GameObject rotationTargetsParent;
    [Tooltip("On: Houses rotate in 90° increments. Off: Houses face directly towards the next TrafficNode.")]
    public bool use90DegreeSnapping = true;
    private List<GameObject> spawnedBuildings = new List<GameObject>();

    private float roadWidth = 0.5f;

    [ContextMenu("Refresh Buidlings")]

    public void RefreshBuildings()
    {
        // First, remove the houses that are currently in the way
        ValidateExistingBuildings();
    }

    [ContextMenu("Fill space with buidlings")]
    public void FillSpaceWithBuildings()
    {
        SpawnObjects();
    }

    [ContextMenu("Clear All Buildings")]
    private void ClearAllBuildings()
    {
        // We iterate through the list backwards to ensure it is deleted
        for (int i = spawnedBuildings.Count - 1; i >= 0; i--)
        {
            GameObject building = spawnedBuildings[i];

            if (building != null)
            {
                // DestroyImmediate is required if you run this in editor mode
                if (Application.isPlaying)
                    Destroy(building);
                else
                    DestroyImmediate(building);
            }
        }

        // Clear list
        spawnedBuildings.Clear();

        // Optional: If there are still any leftover items in the Transform folder (safety check)
        // Useful in case the list was lost due to a script reload
        if (!Application.isPlaying && transform.childCount > 0)
        {
            Debug.Log("Säubere zusätzliche Child-Objekte...");
            // Use a temporary list here, as the child count changes when items are deleted
            List<GameObject> children = new List<GameObject>();
            foreach (Transform child in transform) children.Add(child.gameObject);
            foreach (GameObject child in children) DestroyImmediate(child);
        }

        Debug.Log("All buildings cleared!");
    }

    public void Start()
    {
        roadWidth = GetRoadWidth();
    }

    private float GetRoadWidth()
    {
        if (splineManager != null)
        {
            float width = splineManager.GetComponent<MultiSplineDrawer>().splineWidth;
            Debug.Log("Roadwidth found: " + width);
            return width;
        }
        return 0.5f;
    }

    private void ValidateExistingBuildings()
    {
        for (int i = spawnedBuildings.Count - 1; i >= 0; i--)
        {
            GameObject building = spawnedBuildings[i];
            if (building == null) continue;

            BoxCollider col = building.GetComponent<BoxCollider>();
            Vector3 prefabScale = building.transform.localScale;
            Vector3 halfSize = Vector3.Scale(col.size, prefabScale) * 0.5f;
            float houseSafetyRadius = Mathf.Max(halfSize.x, halfSize.z);

            // Check whether the object is too close to the new road/ spline
            if (!IsPositionFarFromAllSplines(building.transform.position, roadWidth + houseSafetyRadius))
            {
                Destroy(building);
                spawnedBuildings.RemoveAt(i);
            }
        }
    }

    private void SpawnObjects()
    {
        if (roadSpline == null || areaObject == null) return;

        // Calculating the area (the scale for a plane is often 10 units)
        float halfWidth = (areaObject.localScale.x * 10f) / 2f;
        float halfLength = (areaObject.localScale.z * 10f) / 2f;

        int buildingsToCreate = amount - spawnedBuildings.Count;

        for (int i = 0; i < buildingsToCreate; i++)
        {
            GameObject selectedPrefab = prefabsToSpawn[UnityEngine.Random.Range(0, prefabsToSpawn.Count)];
            BoxCollider col = selectedPrefab.GetComponent<BoxCollider>();
            if (col == null) continue;

            Vector3 prefabScale = selectedPrefab.transform.localScale;
            Vector3 finalExtents = Vector3.Scale(col.size, prefabScale) * 0.5f;
            finalExtents.x += extraSpacing;
            finalExtents.z += extraSpacing;
            finalExtents.y += 0.5f;

            float houseSafetyRadius = Mathf.Max(finalExtents.x, finalExtents.z);

            bool validPosFound = false;
            Vector3 finalPos = Vector3.zero;
            Quaternion finalRot = Quaternion.identity;
            int attempts = 0;

            while (!validPosFound && attempts < 50)
            {
                attempts++;
                float randomX = UnityEngine.Random.Range(-halfWidth, halfWidth);
                float randomZ = UnityEngine.Random.Range(-halfLength, halfLength);
                Vector3 testPos = areaObject.position + new Vector3(randomX, 0.5f, randomZ);

                if (IsPositionFarFromAllSplines(testPos, roadWidth + houseSafetyRadius))
                {
                    // Check for collisions with other objects e.g. buildings (avoidanceLayers)
                    if (!Physics.CheckBox(testPos, finalExtents, Quaternion.identity, avoidanceLayers))
                    {
                        // Dynamic rotation
                        if (rotationTargetsParent != null && rotationTargetsParent.transform.childCount > 0)
                        {
                            Transform nearest = GetNearestNode(testPos);
                            if (nearest != null)
                            {
                                Vector3 dir = (nearest.position - testPos).normalized;
                                float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

                                // toggle
                                float finalAngle = use90DegreeSnapping ? Mathf.Round(angle / 90f) * 90f : angle;

                                finalRot = Quaternion.Euler(0, finalAngle, 0);
                            }
                        }

                        finalPos = testPos;
                        finalPos.y = areaObject.position.y;
                        validPosFound = true;
                    }
                }
            }
            if (validPosFound)
            {
                GameObject newBuilding = Instantiate(selectedPrefab, finalPos, finalRot, transform);
                spawnedBuildings.Add(newBuilding);
            }
        }
    }

    private Transform GetNearestNode(Vector3 pos)
    {
        Transform nearest = null;
        float minDist = float.MaxValue;
        foreach (Transform child in rotationTargetsParent.transform)
        {
            float d = Vector3.Distance(pos, child.position);
            if (d < minDist) { minDist = d; nearest = child; }
        }
        return nearest;
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