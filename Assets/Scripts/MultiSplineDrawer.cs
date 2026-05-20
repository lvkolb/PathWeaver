using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using UnityEngine.InputSystem;

public class MultiSplineDrawer : MonoBehaviour
{
    [Header("Drawing Source")]
    public Transform drawingSource;
    public GameObject targetSpline;
    public float streetHeight = 0f;

    [Header("Distance from the previous knot until a new knot can be created")]
    [SerializeField] private float minDistance = 0.1f;
    [Header("The distance when several knots are linked")]
    [SerializeField] private float connectThreshold = 0.09f;

    [Header("Road Width Generation")]
    public float splineWidth = 0.2f;

    [Header("Live Infrastructure Updates (Defaults)")]
    [SerializeField] private TrafficNetwork trafficNetwork;
    [SerializeField] private VehicleManager vehicleManager;

    private SplineContainer splineContainer;
    private Spline activeSpline;
    private List<Vector3> currentPoints = new List<Vector3>();
    private bool isHolding;
    private Component[] widthComponents;

    private void Awake()
    {
        splineContainer = targetSpline.GetComponent<SplineContainer>();
        widthComponents = targetSpline.GetComponents<Component>();
    }

    public void StartDrawing()
    {
        isHolding = true;
        StartNewSpline();
    }

    public void StopDrawing()
    {
        isHolding = false;
        currentPoints.Clear();

        ConnectAllInternalSplines();

        // Run defaults immediately when stopped
        DefaultNetworkAndVehicleUpdates();
    }

    private void Update()
    {
        if (!isHolding) return;

        if (drawingSource == null)
        {
            Debug.LogWarning("Please assign a 'drawingSource' GameObject in the Inspector!");
            return;
        }

        // 1. Retrieve the object's 3D position
        Vector3 worldPos = drawingSource.position;

        // 2. Set the position to your desired street level
        worldPos.y = streetHeight;

        // 3. Distance check
        if (currentPoints.Count == 0 || Vector3.Distance(currentPoints[^1], worldPos) > minDistance)
        {
            currentPoints.Add(worldPos);
            UpdateSpline();
        }
    }
    /// <summary>
    /// This method holds the original hardcoded updating logic.
    /// It is registered as a permanent listener to onMouseUpEvent inside Awake().
    /// </summary>
    private void DefaultNetworkAndVehicleUpdates()
    {
        // Notify the infrastructure network to bake new nodes
        if (trafficNetwork == null) trafficNetwork = FindAnyObjectByType<TrafficNetwork>();
        if (trafficNetwork != null) trafficNetwork.RebuildGraph();

        // Force all vehicles to find potential shortcuts on the new road
        if (vehicleManager == null) vehicleManager = FindAnyObjectByType<VehicleManager>();
        if (vehicleManager != null) vehicleManager.RecalculateAllVehiclePaths();
    }

    private void StartNewSpline()
    {
        activeSpline = new Spline();
        splineContainer.AddSpline(activeSpline);
        PatchAllWidthComponents(splineContainer.Splines.Count - 1);
    }

    private void UpdateSpline()
    {
        if (activeSpline == null) return;
        activeSpline.Clear();

        foreach (Vector3 worldPos in currentPoints)
        {
            Vector3 localPos = splineContainer.transform.InverseTransformPoint(worldPos);
            activeSpline.Add(new BezierKnot(localPos));
        }
        RebuildAllRoadComponents();
    }

    public void ConnectAllInternalSplines()
    {
        var splines = splineContainer.Splines;
        for (int i = 0; i < splines.Count; i++)
        {
            for (int j = i + 1; j < splines.Count; j++)
            {
                CompareAndConnect(i, j);
            }
        }
    }

    private void CompareAndConnect(int indexA, int indexB)
    {
        var splineA = splineContainer.Splines[indexA];
        var splineB = splineContainer.Splines[indexB];

        for (int knotIdxA = 0; knotIdxA < splineA.Count; knotIdxA++)
        {
            float3 posA = splineA[knotIdxA].Position;
            for (int knotIdxB = 0; knotIdxB < splineB.Count; knotIdxB++)
            {
                float3 posB = splineB[knotIdxB].Position;

                if (math.distance(posA, posB) <= connectThreshold)
                {
                    float3 midPoint = (posA + posB) * 0.5f;

                    var knotA = splineA[knotIdxA];
                    knotA.Position = midPoint;
                    splineA[knotIdxA] = knotA;

                    var knotB = splineB[knotIdxB];
                    knotB.Position = midPoint;
                    splineB[knotIdxB] = knotB;

                    splineContainer.LinkKnots(new SplineKnotIndex(indexA, knotIdxA), new SplineKnotIndex(indexB, knotIdxB));
                }
            }
        }
    }

    // ── Width Patching Engine via Reflection ──────────────────────────────────

    private void PatchAllWidthComponents(int splineIndex)
    {
        foreach (var comp in widthComponents)
        {
            if (comp == null) continue;
            TryPatchWidthOnComponent(comp, splineIndex);
        }
        RebuildAllRoadComponents();
    }

    private void TryPatchWidthOnComponent(Component comp, int splineIndex)
    {
        Type compType = comp.GetType();
        var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        string[] candidateFields = { "m_Widths", "m_Width", "widths", "width", "m_RoadWidth", "roadWidth", "m_Sizes", "sizes" };

        foreach (string fieldName in candidateFields)
        {
            FieldInfo field = compType.GetField(fieldName, flags);
            if (field == null) continue;

            object value = field.GetValue(comp);
            if (value == null) continue;

            if (value is System.Collections.IList list)
            {
                if (splineIndex < list.Count)
                {
                    SetSplineDataDefault(list[splineIndex], comp, fieldName, splineIndex);
                }
                else if (list.Count > 0)
                {
                    try
                    {
                        object template = list[0];
                        object clone = CloneAndSetDefault(template, splineWidth);
                        if (clone != null) list.Add(clone);
                    }
                    catch (Exception e) { Debug.LogWarning($"Width Patch Error: {e.Message}"); }
                }
                return;
            }

            if (value.GetType().Name.StartsWith("SplineData"))
            {
                SetSplineDataDefault(value, comp, fieldName, -1);
                return;
            }

            if (value is float)
            {
                field.SetValue(comp, splineWidth);
                return;
            }
        }
    }

    private void SetSplineDataDefault(object splineDataObj, Component owner, string fieldName, int idx)
    {
        if (splineDataObj == null) return;
        Type sdType = splineDataObj.GetType();
        var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        string[] defaultFields = { "m_DefaultValue", "defaultValue", "m_Default", "Default" };
        foreach (string df in defaultFields)
        {
            FieldInfo fi = sdType.GetField(df, flags);
            if (fi == null) continue;
            try
            {
                fi.SetValue(splineDataObj, splineWidth);
                if (idx >= 0 && owner != null)
                {
                    FieldInfo listField = owner.GetType().GetField(fieldName, flags);
                    if (listField?.GetValue(owner) is System.Collections.IList list && idx < list.Count)
                        list[idx] = splineDataObj;
                }
            }
            catch (Exception e) { Debug.LogWarning($"SplineData Assignment Error: {e.Message}"); }
            return;
        }
    }

    private object CloneAndSetDefault(object template, float defaultValue)
    {
        if (template == null) return null;
        try
        {
            MethodInfo clone = template.GetType().GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
            object copy = clone != null ? clone.Invoke(template, null) : template;

            var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            foreach (string f in new[] { "m_DataPoints", "dataPoints", "m_Data" })
            {
                FieldInfo fi = copy.GetType().GetField(f, flags);
                if (fi?.GetValue(copy) is System.Collections.IList pts)
                { pts.Clear(); break; }
            }

            foreach (string df in new[] { "m_DefaultValue", "defaultValue" })
            {
                FieldInfo fi = copy.GetType().GetField(df, flags);
                if (fi != null) { fi.SetValue(copy, defaultValue); break; }
            }
            return copy;
        }
        catch { return null; }
    }

    private void RebuildAllRoadComponents()
    {
        foreach (var comp in widthComponents)
        {
            if (comp == null) continue;
            var rebuild = comp.GetType().GetMethod("Rebuild", BindingFlags.Public | BindingFlags.Instance);
            rebuild?.Invoke(comp, null);
        }
    }
}