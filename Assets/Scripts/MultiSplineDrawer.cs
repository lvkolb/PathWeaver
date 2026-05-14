using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;
using Unity.Mathematics;

public class MultiSplineDrawer : MonoBehaviour
{
    public GameObject targetSpline;
    public float streetHeight = 0f;

    [Header("Drawing Constraints")]
    [SerializeField] private float minDistance = 1f;
    [Tooltip("Maximum distance to automatically weld endpoints together.")]
    [SerializeField] private float connectThreshold = 1.5f;

    [Header("Road Width Generation")]
    [SerializeField] private float newSplineWidth = 0.2f;

    [Header("Live Infrastructure Updates")]
    [SerializeField] private TrafficNetwork trafficNetwork;
    [SerializeField] private VehicleManager vehicleManager;

    private SplineContainer splineContainer;
    private Spline activeSpline;
    private List<Vector3> currentPoints = new List<Vector3>();
    private InputAction holdAction;
    private bool isHolding;
    private Component[] widthComponents;

    private void Awake()
    {
        splineContainer = targetSpline.GetComponent<SplineContainer>();
        widthComponents = targetSpline.GetComponents<Component>();

        // Set up input action for left mouse button holding
        holdAction = new InputAction(type: InputActionType.Button, binding: "<Mouse>/leftButton");

        holdAction.started += _ =>
        {
            isHolding = true;
            StartNewSpline();
        };

        holdAction.canceled += _ =>
        {
            isHolding = false;
            currentPoints.Clear();

            // INTEGRATION: Merges intersecting vertices instantly instead of using a separate script
            ConnectAllInternalSplines();

            // Notify the infrastructure network to bake new nodes
            if (trafficNetwork == null) trafficNetwork = FindAnyObjectByType<TrafficNetwork>();
            if (trafficNetwork != null) trafficNetwork.RebuildGraph();

            // Force all vehicles to find potential shortcuts on the new road
            if (vehicleManager == null) vehicleManager = FindAnyObjectByType<VehicleManager>();
            if (vehicleManager != null) vehicleManager.RecalculateAllVehiclePaths();
        };
    }

    private void OnEnable() => holdAction.Enable();
    private void OnDisable() => holdAction.Disable();

    private void Update()
    {
        if (!isHolding) return;

        // Project mouse screen position onto the world space ground plane
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Vector3 worldPos = hit.point;
            worldPos.y = streetHeight;

            // Add point if it is the first one or exceeds the minimum distance threshold
            if (currentPoints.Count == 0 || Vector3.Distance(currentPoints[^1], worldPos) > minDistance)
            {
                currentPoints.Add(worldPos);
                UpdateSpline();
            }
        }
    }

    private void StartNewSpline()
    {
        activeSpline = new Spline();
        splineContainer.AddSpline(activeSpline);

        // Patch the mesh modifier component widths for the new spline index
        PatchAllWidthComponents(splineContainer.Splines.Count - 1);
    }

    private void UpdateSpline()
    {
        if (activeSpline == null) return;
        activeSpline.Clear();

        // Convert world points to local spline container space
        foreach (Vector3 worldPos in currentPoints)
        {
            Vector3 localPos = splineContainer.transform.InverseTransformPoint(worldPos);
            activeSpline.Add(new BezierKnot(localPos));
        }
        RebuildAllRoadComponents();
    }

    /// <summary>
    /// Compares all spline endpoints and welds them together logically if within connectThreshold.
    /// </summary>
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

                // If endpoints are close enough, snap them to their shared midpoint
                if (math.distance(posA, posB) <= connectThreshold)
                {
                    float3 midPoint = (posA + posB) * 0.5f;

                    var knotA = splineA[knotIdxA];
                    knotA.Position = midPoint;
                    splineA[knotIdxA] = knotA;

                    var knotB = splineB[knotIdxB];
                    knotB.Position = midPoint;
                    splineB[knotIdxB] = knotB;

                    // Link the knots logically within the native Unity Spline system
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

            // Handle List-based width configurations (one entry per spline)
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
                        object clone = CloneAndSetDefault(template, newSplineWidth);
                        if (clone != null) list.Add(clone);
                    }
                    catch (Exception e) { Debug.LogWarning($"Width Patch Error: {e.Message}"); }
                }
                return;
            }

            // Handle single SplineData configurations
            if (value.GetType().Name.StartsWith("SplineData"))
            {
                SetSplineDataDefault(value, comp, fieldName, -1);
                return;
            }

            // Handle basic float properties directly
            if (value is float)
            {
                field.SetValue(comp, newSplineWidth);
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
                fi.SetValue(splineDataObj, newSplineWidth);
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
        // Dynamically invoke the Rebuild method on modern road generation components (e.g., SplineExtrude)
        foreach (var comp in widthComponents)
        {
            if (comp == null) continue;
            var rebuild = comp.GetType().GetMethod("Rebuild", BindingFlags.Public | BindingFlags.Instance);
            rebuild?.Invoke(comp, null);
        }
    }
}