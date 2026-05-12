using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;

public class MultiSplineDrawer : MonoBehaviour
{
    public GameObject targetSpline;
    public float streetHeight = 0f;

    [Header("Minimum distance between drawn points")]
    [SerializeField] private float minDistance = 1f;

    [Header("Road Width for newly drawn splines")]
    [Tooltip("Must match the Default Value shown for your pre-drawn splines (0.2). " +
             "New splines default to 1.0 without this patch.")]
    [SerializeField] private float newSplineWidth = 0.2f;

    [Header("Tools which execute after mouse release")]
    [SerializeField] private SplineLinkTool linkTool;

    [Header("Live Update (auto-found if not assigned)")]
    [SerializeField] private TrafficNetwork trafficNetwork;
    [SerializeField] private VehicleManager vehicleManager;

    private SplineContainer splineContainer;
    private Spline activeSpline;
    private List<Vector3> currentPoints = new List<Vector3>();
    private InputAction holdAction;
    private bool isHolding;

    // Cache all width-bearing components on the spline object once
    private Component[] widthComponents;

    private void Awake()
    {
        splineContainer = targetSpline.GetComponent<SplineContainer>();

        // Grab every component — we'll probe each for a width list
        widthComponents = targetSpline.GetComponents<Component>();

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

            if (linkTool != null) linkTool.ConnectAllInternalSplines();

            if (trafficNetwork == null) trafficNetwork = FindObjectOfType<TrafficNetwork>();
            if (trafficNetwork != null) trafficNetwork.RebuildGraph();

            if (vehicleManager == null) vehicleManager = FindObjectOfType<VehicleManager>();
            if (vehicleManager != null) vehicleManager.RecalculateAllVehiclePaths();
        };
    }

    private void OnEnable() => holdAction.Enable();
    private void OnDisable() => holdAction.Disable();

    private void Update()
    {
        if (!isHolding) return;

        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Vector3 worldPos = hit.point;
            worldPos.y = streetHeight;
            if (currentPoints.Count == 0 ||
                Vector3.Distance(currentPoints[^1], worldPos) > minDistance)
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

        int newIdx = splineContainer.Splines.Count - 1;
        PatchAllWidthComponents(newIdx);
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

        // Tell any road-mesh component to rebuild
        RebuildAllRoadComponents();
    }

    // ── Width patching ────────────────────────────────────────────────────────

    /// <summary>
    /// Iterates every component on the spline GameObject and tries to set the
    /// default width for the newly added spline index.
    /// Handles: SplineExtrude (m_Widths), LoftRoadBehaviour (various field names).
    /// </summary>
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

        // Candidate field names used across SplineExtrude, LoftRoadBehaviour, and
        // other road components in various Unity Splines package versions:
        string[] candidateFields = {
            "m_Widths", "m_Width", "widths", "width",
            "m_RoadWidth", "roadWidth",
            "m_Sizes", "sizes",
        };

        foreach (string fieldName in candidateFields)
        {
            FieldInfo field = compType.GetField(fieldName, flags);
            if (field == null) continue;

            object value = field.GetValue(comp);
            if (value == null) continue;

            // Case 1: It's a List<SplineData<float>> — one entry per spline
            if (value is System.Collections.IList list)
            {
                if (splineIndex < list.Count)
                {
                    SetSplineDataDefault(list[splineIndex], comp, fieldName, splineIndex);
                }
                else
                {
                    // List hasn't grown yet for the new spline — try adding a copy of element 0
                    if (list.Count > 0)
                    {
                        try
                        {
                            // Clone element 0 and set its default value
                            object template = list[0];
                            object clone = CloneAndSetDefault(template, newSplineWidth);
                            if (clone != null)
                            {
                                // Use Add via reflection (IList.Add works for non-generic too)
                                list.Add(clone);
                                Debug.Log($"MultiSplineDrawer: Added width entry on {compType.Name}.{fieldName}");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"MultiSplineDrawer: Could not add entry to {compType.Name}.{fieldName}: {e.Message}");
                        }
                    }
                }
                return; // found the right field, stop searching
            }

            // Case 2: It's a single SplineData<float> (shared across all splines)
            if (value.GetType().Name.StartsWith("SplineData"))
            {
                SetSplineDataDefault(value, comp, fieldName, -1);
                return;
            }

            // Case 3: It's a plain float — just set it directly
            if (value is float)
            {
                field.SetValue(comp, newSplineWidth);
                Debug.Log($"MultiSplineDrawer: Set {compType.Name}.{fieldName} = {newSplineWidth}");
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
                // Write back (required for structs — value types don't mutate in place)
                if (idx >= 0)
                {
                    FieldInfo listField = owner.GetType().GetField(fieldName,
                        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    if (listField?.GetValue(owner) is System.Collections.IList list && idx < list.Count)
                        list[idx] = splineDataObj;
                }
                Debug.Log($"MultiSplineDrawer: Set {owner.GetType().Name}.{fieldName}[{idx}].{df} = {newSplineWidth}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"MultiSplineDrawer: {e.Message}");
            }
            return;
        }
    }

    private object CloneAndSetDefault(object template, float defaultValue)
    {
        if (template == null) return null;
        try
        {
            // SplineData<float> has a copy constructor or is a struct — use MemberwiseClone via reflection
            MethodInfo clone = template.GetType().GetMethod("MemberwiseClone",
                BindingFlags.NonPublic | BindingFlags.Instance);
            object copy = clone != null ? clone.Invoke(template, null) : template;

            // Clear data points list in the clone so it only inherits the default
            var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            foreach (string f in new[] { "m_DataPoints", "dataPoints", "m_Data" })
            {
                FieldInfo fi = copy.GetType().GetField(f, flags);
                if (fi?.GetValue(copy) is System.Collections.IList pts)
                { pts.Clear(); break; }
            }

            SetSplineDataDefault(copy, null, "", -1); // will fail gracefully, use direct set
            // Direct set on the copy
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
            // SplineExtrude
            var rebuild = comp.GetType().GetMethod("Rebuild",
                BindingFlags.Public | BindingFlags.Instance);
            rebuild?.Invoke(comp, null);
        }
    }
}