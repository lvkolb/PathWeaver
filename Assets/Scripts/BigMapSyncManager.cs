using UnityEngine;
using System.Collections.Generic;

public class BigMapSyncManager : MonoBehaviour
{
    // Singleton Instance for instant O(1) access from other scripts
    public static BigMapSyncManager Instance { get; private set; }

    [Header("Map Planes (Transforms)")]
    [Tooltip("The mini plane with its own scale (e.g., 2, 1, 2)")]
    [SerializeField] private Transform miniMapPlane;
    [Tooltip("The big plane with its own scale (e.g., 10, 1, 10). Must be a child of this Manager.")]
    [SerializeField] private Transform bigMapPlane;

    [Header("Object Visual Size")]
    [Tooltip("Adjust the slider to change the scale of the holograms on the big map.")]
    [Range(0.05f, 20f)]
    [SerializeField] private float objectVisualScale = 5f;

    [Header("Trail Settings")]
    [Tooltip("Adjust the width multiplier specifically for the TrailRenderers on the big map.")]
    [Range(0.05f, 20f)]
    [SerializeField] private float trailWidthMultiplier = 5f;

    [Header("Objects to Duplicate & Sync")]
    [SerializeField] private List<GameObject> objectsToSync = new List<GameObject>();

    [Header("Component Whitelist")]
    [Tooltip("Add full names of components/scripts that should NOT be destroyed (e.g., 'TrailRenderer', 'LineRenderer')")]
    [SerializeField] private List<string> componentsToKeep = new List<string> { "TrailRenderer" };

    private struct SyncPair
    {
        public Transform MiniTransform;
        public Transform BigTransform;
    }

    private List<SyncPair> synchronizedPairs = new List<SyncPair>();
    private HashSet<Transform> knownTransforms = new HashSet<Transform>();
    private HashSet<string> whitelistLookup;
    private float calculatedPositionFactor = 1f;

    private void Awake()
    {
        // Setup Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        whitelistLookup = new HashSet<string>(componentsToKeep);
        CalculateScaleFactor();
    }

    private void Start()
    {
        // Initial scan at startup for pre-existing local objects
        ScanAndSetupObjects();
    }

    /// <summary>
    /// Call this method whenever a new road or object is spawned to refresh the big map.
    /// Usage: BigMapSyncManager.Instance.RegisterNewObjects();
    /// </summary>
    public void RegisterNewObjects()
    {
        ScanAndSetupObjects();
    }

    private void Update()
    {
        // Track and move synchronized targets (e.g., driving cars) every frame
        UpdatePositions();
    }

    private void CalculateScaleFactor()
    {
        if (miniMapPlane == null || bigMapPlane == null) return;
        calculatedPositionFactor = bigMapPlane.localScale.x / miniMapPlane.localScale.x;
    }

    private void ScanAndSetupObjects()
    {
        if (miniMapPlane == null || bigMapPlane == null) return;

        foreach (GameObject sourceGroup in objectsToSync)
        {
            if (sourceGroup == null) continue;

            foreach (Transform child in sourceGroup.transform)
            {
                if (knownTransforms.Contains(child)) continue;

                knownTransforms.Add(child);
                CreateBigMapClone(child);
            }
        }
    }

    private void CreateBigMapClone(Transform miniTransform)
    {
        GameObject bigObjectGo = Instantiate(miniTransform.gameObject, bigMapPlane);
        bigObjectGo.name = "Sync_" + miniTransform.name;

        bool isSpline = miniTransform.GetComponentInParent<UnityEngine.Splines.SplineContainer>() != null;

        if (!isSpline)
        {
            Component[] allComponents = bigObjectGo.GetComponentsInChildren<Component>(true);
            foreach (Component comp in allComponents)
            {
                if (comp != null && !(comp is Transform) && !(comp is MeshFilter) && !(comp is MeshRenderer))
                {
                    if (!ShouldKeepComponent(comp))
                    {
                        Destroy(comp);
                    }
                }
            }
        }
        else
        {
            Collider[] childColliders = bigObjectGo.GetComponentsInChildren<Collider>();
            foreach (Collider col in childColliders)
            {
                if (!ShouldKeepComponent(col)) col.enabled = false;
            }

            Rigidbody[] childRbs = bigObjectGo.GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody rb in childRbs)
            {
                if (!ShouldKeepComponent(rb)) rb.isKinematic = true;
            }
        }

        bigObjectGo.transform.localScale = miniTransform.localScale * objectVisualScale;

        // Clear existing trail history right after instantiation to prevent a visual line snap artifact
        TrailRenderer trail = bigObjectGo.GetComponentInChildren<TrailRenderer>();
        if (trail != null)
        {
            trail.Clear();
        }

        SyncPair pair = new SyncPair
        {
            MiniTransform = miniTransform,
            BigTransform = bigObjectGo.transform
        };

        synchronizedPairs.Add(pair);
    }

    private void UpdatePositions()
    {
        for (int i = synchronizedPairs.Count - 1; i >= 0; i--)
        {
            SyncPair pair = synchronizedPairs[i];

            if (pair.MiniTransform == null || pair.BigTransform == null)
            {
                if (pair.BigTransform != null) Destroy(pair.BigTransform.gameObject);
                synchronizedPairs.RemoveAt(i);
                continue;
            }

            Vector3 localOffset = pair.MiniTransform.position - miniMapPlane.position;
            Vector3 scaledOffset = localOffset * calculatedPositionFactor;

            pair.BigTransform.position = bigMapPlane.position + scaledOffset;
            pair.BigTransform.rotation = pair.MiniTransform.rotation;

            // Live update the Trail width if the clone has a TrailRenderer component
            TrailRenderer trail = pair.BigTransform.GetComponentInChildren<TrailRenderer>();
            if (trail != null)
            {
                trail.widthMultiplier = trailWidthMultiplier;
            }
        }
    }

    private bool ShouldKeepComponent(Component comp)
    {
        if (comp == null) return false;
        string typeName = comp.GetType().Name;
        string fullTypeName = comp.GetType().FullName;
        return whitelistLookup.Contains(typeName) || (!string.IsNullOrEmpty(fullTypeName) && whitelistLookup.Contains(fullTypeName));
    }
}