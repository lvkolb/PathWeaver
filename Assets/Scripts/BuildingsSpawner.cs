using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;
using Unity.Mathematics;

public class BuildingsSpawner : MonoBehaviour
{
    [Header("Prefabs & quantity")]
    public List<GameObject> prefabsToSpawn = new List<GameObject>();
    public int amount = 50;

    [Header("Avoid spline (road)")]
    public SplineContainer roadSpline;
    [Tooltip("The 'Safe Zone' from the road center. House size will be added to this!")]
    public float roadWidth = 4f;

    [Header("Avoid collision")]
    public LayerMask avoidanceLayers;
    [Header("Additional spacing buffer around the rectangular bounds/ buildings")]
    public float extraSpacing = 0.5f;

    [Header("Area Reference")]
    public Transform areaObject;

    void Start()
    {
        if (roadSpline == null || prefabsToSpawn.Count == 0 || areaObject == null) return;
        SpawnObjects();
    }

    public void SpawnObjects()
    {
        float halfWidth = (areaObject.localScale.x * 10f) / 2f;
        float halfLength = (areaObject.localScale.z * 10f) / 2f;

        for (int i = 0; i < amount; i++)
        {
            GameObject selectedPrefab = prefabsToSpawn[UnityEngine.Random.Range(0, prefabsToSpawn.Count)];
            BoxCollider col = selectedPrefab.GetComponent<BoxCollider>();
            if (col == null) continue;

            Vector3 prefabScale = selectedPrefab.transform.localScale;

            // --- DIE NEUE ROBUSTE BERECHNUNG ---
            // Wir berechnen die halbe Größe (Extents) basierend auf Scale und Collider-Size
            Vector3 finalExtents = Vector3.Scale(col.size, prefabScale) * 0.5f;
            finalExtents.x += extraSpacing;
            finalExtents.z += extraSpacing;
            finalExtents.y += 0.5f;

            // Wir ermitteln, wie weit das Haus maximal von seiner Mitte aus "ausladend" ist
            float houseSafetyRadius = Mathf.Max(finalExtents.x, finalExtents.z);

            bool validPosFound = false;
            Vector3 finalPos = Vector3.zero;
            // Quaternion finalRot = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0);
            Quaternion finalRot = Quaternion.Euler(0, 0, 0);
            int attempts = 0;

            while (!validPosFound && attempts < 100)
            {
                attempts++;

                float randomX = UnityEngine.Random.Range(-halfWidth, halfWidth);
                float randomZ = UnityEngine.Random.Range(-halfLength, halfLength);
                Vector3 testPos = areaObject.position + new Vector3(randomX, 0.5f, randomZ);

                // --- CHECK A: ROAD WITH SIZE CONSIDERATION ---
                // Wir addieren den Haus-Radius zur roadWidth!
                float totalRequiredDistance = roadWidth + houseSafetyRadius;

                if (IsPositionFarFromAllSplines(testPos, totalRequiredDistance))
                {
                    // --- CHECK B: OBJECT COLLISION ---
                    if (!Physics.CheckBox(testPos, finalExtents, finalRot, avoidanceLayers))
                    {
                        validPosFound = true;
                        finalPos = testPos;
                        finalPos.y = areaObject.position.y;
                    }
                }
            }

            if (validPosFound)
            {
                Instantiate(selectedPrefab, finalPos, finalRot, transform);
            }
        }
    }

    private bool IsPositionFarFromAllSplines(Vector3 worldPos, float requiredDist)
    {
        float3 localPos = roadSpline.transform.InverseTransformPoint(worldPos);

        foreach (var spline in roadSpline.Splines)
        {
            SplineUtility.GetNearestPoint(spline, localPos, out float3 nearestLocal, out float t);
            Vector3 nearestWorld = roadSpline.transform.TransformPoint(nearestLocal);

            // Hier nutzen wir nun die dynamisch berechnete Distanz (requiredDist)
            if (Vector3.Distance(worldPos, nearestWorld) < requiredDist)
            {
                return false;
            }
        }
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (areaObject != null)
        {
            Gizmos.color = Color.cyan;
            Vector3 size = new Vector3(areaObject.localScale.x * 10, 0.1f, areaObject.localScale.z * 10);
            Gizmos.DrawWireCube(areaObject.position, size);
        }
    }
}