using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Netcode;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class MultiSplineDrawer : NetworkBehaviour
{
    // Encapsulation for multiple sources
    [System.Serializable]
    public class DrawingSession
    {
        public Transform drawingSource;
        [HideInInspector] public Spline activeSpline;
        [HideInInspector] public List<Vector3> currentPoints = new List<Vector3>();
    }

    [Header("Layer Selection Dropdown")]
    [Tooltip("Select the layer of the GameObjects that should act as drawing sources.")]
    public SingleLayer targetLayer; // Weaver

    [Header("Drawing Sessions (Auto-Filled)")]
    public List<DrawingSession> drawingSessions = new List<DrawingSession>();

    [Header("Target Spline Settings")]
    public GameObject targetSpline;
    public float streetHeight = 0f;

    [Header("Distance Thresholds")]
    [SerializeField] private float minDistance = 0.2f;
    [SerializeField] private float connectThreshold = 0.2f;

    [Header("Road Width Generation")]
    public float splineWidth = 0.2f;

    public bool IsDrawingActive => isHolding;

    [Header("Live Infrastructure Updates (Defaults) If not set -> Auto-Filled.")]
    [SerializeField] private TrafficNetwork trafficNetwork;
    [SerializeField] private VehicleManager vehicleManager;

    private SplineContainer splineContainer;
    private bool isHolding;
    private Component[] widthComponents;

    private void Awake()
    {
        splineContainer = targetSpline.GetComponent<SplineContainer>();
        widthComponents = targetSpline.GetComponents<Component>();
    }

    // =================================================================================
    // DYNAMIC SOURCE MANAGEMENT
    // =================================================================================

    /// <summary>
    /// Finds all active GameObjects on the specified layer index and assigns them as drawing sources.
    /// </summary>
    /// <param name="layerIndex">The index of the Unity Layer.</param>
    public void RefreshDrawingSourcesByLayer(int layerIndex)
    {
        if (layerIndex < 0 || layerIndex > 31)
        {
            Debug.LogError("[MultiSplineDrawer] Invalid layer index selected!");
            return;
        }

        if (isHolding)
        {
            StopDrawing();
        }

        drawingSessions.Clear();

        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Exclude);

        foreach (GameObject obj in allObjects)
        {
            if (obj.layer == layerIndex)
            {
                // Prevent decorative objects  
                // or houses that have already been spawned from being registered as drawing pens/sources!
                if (obj.name.Contains("(Clone)"))
                {
                    continue;
                }

                Transform parent = obj.transform.parent;
                bool parentAlreadyHasLayer = false;

                while (parent != null)
                {
                    if (parent.gameObject.layer == layerIndex)
                    {
                        parentAlreadyHasLayer = true;
                        break;
                    }
                    parent = parent.parent;
                }

                if (!parentAlreadyHasLayer)
                {
                    DrawingSession newSession = new DrawingSession
                    {
                        drawingSource = obj.transform
                    };
                    drawingSessions.Add(newSession);
                }
            }
        }

        Debug.Log($"[MultiSplineDrawer] Found and registered {drawingSessions.Count} active drawing sources.");
    }
    // =================================================================================
    // CLEAR ALL SPLINES & ROAD DATA
    // =================================================================================
    [ContextMenu("Clear All Splines")]
    public void ClearAllSplines()
    {
        if (splineContainer == null)
            splineContainer = targetSpline.GetComponent<SplineContainer>();

        if (widthComponents == null || widthComponents.Length == 0)
            widthComponents = targetSpline.GetComponents<Component>();

        isHolding = false;

        // Clear all points and spline references in sessions
        foreach (var session in drawingSessions)
        {
            session.activeSpline = null;
            session.currentPoints.Clear();
        }

        // 1. Safely remove every single spline inside the container
        if (splineContainer != null)
        {
            // Clearing the splines automatically clears their associated knot links
            for (int i = splineContainer.Splines.Count - 1; i >= 0; i--)
            {
                splineContainer.RemoveSplineAt(i);
            }
        }

        // 2. Clear reflection-mapped array lists on internal road generator modules 
        // to match the empty spline container length
        var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        string[] candidateFields = { "m_Widths", "m_Width", "widths", "width", "m_RoadWidth", "roadWidth", "m_Sizes", "sizes" };

        foreach (var comp in widthComponents)
        {
            if (comp == null) continue;
            Type compType = comp.GetType();

            foreach (string fieldName in candidateFields)
            {
                FieldInfo field = compType.GetField(fieldName, flags);
                if (field == null) continue;

                object value = field.GetValue(comp);
                if (value == null) continue;

                if (value is System.Collections.IList list)
                {
                    list.Clear();
                    break;
                }
            }
        }

        // 3. Force live road mesh visual recalculation layout updates immediately
        RebuildAllRoadComponents();

        // 4. Notify structural tracking networks to flatten calculations
        DefaultNetworkAndVehicleUpdates();

        Debug.Log("<color=red>[Spline Drawer]</color> All generated road splines and width maps were successfully cleared in Unity 6!");
    }

    public void StartDrawing()
    {
        // Automatically grab the latest active sources from the dropdown layer before starting
        RefreshDrawingSourcesByLayer(targetLayer.layerIndex);

        isHolding = true;

        // Start a new spline for each active session
        foreach (var session in drawingSessions)
        {
            // Safeguard against objects that were destroyed or disabled after gathering
            if (session.drawingSource == null || !session.drawingSource.gameObject.activeInHierarchy)
                continue;

            session.currentPoints.Clear();
            StartNewSpline(session);
        }
    }

    public void StopDrawing()
    {
        isHolding = false;

        foreach (var session in drawingSessions)
        {
            // ENTFERNE DEN LOKALEN VORSCHAU-SPLINE VOR DEM SYNC
            if (session.activeSpline != null && splineContainer != null)
            {
                splineContainer.RemoveSpline(session.activeSpline);
            }

            if (session.currentPoints.Count > 0)
            {
                if (Application.isPlaying)
                {
                    Vector3[] pointsArray = session.currentPoints.ToArray();
                    if (IsServer)
                    {
                        SyncSplineAndMeshClientRpc(pointsArray);
                    }
                    else if (IsClient)
                    {
                        SubmitSplinePointsServerRpc(pointsArray);
                    }
                }
            }

            session.currentPoints.Clear();
            session.activeSpline = null;
        }

        if (!Application.isPlaying)
        {
            FinalizeLocalRoadGeneration();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitSplinePointsServerRpc(Vector3[] points)
    {
        // The server receives the client's points and forwards them to EVERYONE
        SyncSplineAndMeshClientRpc(points);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncSplineAndMeshClientRpc(Vector3[] points)
    {
        // IMPORTANT: If the host is to spawn the houses, ONLY the server must do so!
        // As this RPC is received by all devices, each one executes it for its own mesh.

        // 1. Create a new spline on this device
        Spline networkSpline = new Spline();
        if (splineContainer == null) splineContainer = targetSpline.GetComponent<SplineContainer>();
        splineContainer.AddSpline(networkSpline);
        PatchAllWidthComponents(splineContainer.Splines.Count - 1);

        // 2. Enter points into the spline
        foreach (Vector3 worldPos in points)
        {
            Vector3 localPos = splineContainer.transform.InverseTransformPoint(worldPos);
            networkSpline.Add(new BezierKnot(localPos));
        }

        // 3. Generate a road mesh on this device
        ConnectAllInternalSplines();
        RebuildAllRoadComponents();
        DefaultNetworkAndVehicleUpdates();

        // 4. ONLY the server now triggers the house spawner, as it now has exactly the same splines!
        if (IsServer)
        {
            AlongSplineObjectSpawner spawner = FindAnyObjectByType<AlongSplineObjectSpawner>();
            if (spawner != null)
            {
                spawner.CheckForNewSplinesAndSpawn(); // Let’s run it straight away, as we’re already on the server!
            }
        }
    }

    private void FinalizeLocalRoadGeneration()
    {
        ConnectAllInternalSplines();
        DefaultNetworkAndVehicleUpdates();
    }

    private void Update()
    {
        if (!isHolding) return;

        bool contentChanged = false;

        foreach (var session in drawingSessions)
        {
            // Safety check: ensure source is still valid and active during runtime
            if (session.drawingSource == null || !session.drawingSource.gameObject.activeInHierarchy || session.activeSpline == null)
                continue;

            // 1. Retrieve the object's 3D position
            Vector3 worldPos = session.drawingSource.position;

            // 2. Set the position to your desired street level
            worldPos.y = streetHeight;

            // 3. Distance check
            if (session.currentPoints.Count == 0 || Vector3.Distance(session.currentPoints[^1], worldPos) > minDistance)
            {
                session.currentPoints.Add(worldPos);
                UpdateSpline(session);
                contentChanged = true;
            }
        }

        // Rebuild visual components once per frame if any spline changed
        if (contentChanged)
        {
            RebuildAllRoadComponents();
        }
    }

    private void DefaultNetworkAndVehicleUpdates()
    {
        if (trafficNetwork == null) trafficNetwork = FindAnyObjectByType<TrafficNetwork>();
        if (vehicleManager == null) vehicleManager = FindAnyObjectByType<VehicleManager>();

        // 1. Snapshot positions BEFORE nodes are destroyed
        if (vehicleManager != null) vehicleManager.SnapshotVehiclePositions();

        // 2. Rebuild — destroys and recreates all nodes
        if (trafficNetwork != null) trafficNetwork.RebuildGraph();

        // 3. Remap cars using the saved positions, not the destroyed nodes
        if (vehicleManager != null) vehicleManager.RecalculateAllVehiclePaths();
    }

    private void StartNewSpline(DrawingSession session)
    {
        session.activeSpline = new Spline();
        splineContainer.AddSpline(session.activeSpline);
        PatchAllWidthComponents(splineContainer.Splines.Count - 1);
    }

    private void UpdateSpline(DrawingSession session)
    {
        session.activeSpline.Clear();

        foreach (Vector3 worldPos in session.currentPoints)
        {
            Vector3 localPos = splineContainer.transform.InverseTransformPoint(worldPos);
            session.activeSpline.Add(new BezierKnot(localPos));
        }
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

// ── Helper Struct for Inspector Dropdown ─────────────────────────────────────

[System.Serializable]
public struct SingleLayer
{
    [SerializeField]
    private int m_LayerIndex;

    public int layerIndex
    {
        get => m_LayerIndex;
        set => m_LayerIndex = value;
    }
}

// Property Drawer for the Dropdown (Editor Rendering)

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(SingleLayer))]
public class SingleLayerPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty layerIndexProp = property.FindPropertyRelative("m_LayerIndex");
        if (layerIndexProp != null)
        {
            layerIndexProp.intValue = EditorGUI.LayerField(position, label, layerIndexProp.intValue);
        }
    }
}
#endif