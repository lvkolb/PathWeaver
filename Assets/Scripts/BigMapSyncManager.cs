using UnityEngine;
using System.Collections.Generic;

public class BigMapSyncManager : MonoBehaviour
{
    [Header("Map Planes (Transforms)")]
    [Tooltip("The mini plane with its own scale (e.g., 2, 1, 2)")]
    [SerializeField] private Transform miniMapPlane;
    [Tooltip("The big plane with its own scale (e.g., 10, 1, 10). Must be a child of this Manager.")]
    [SerializeField] private Transform bigMapPlane;

    [Header("Object Visual Size")]
    [Tooltip("Adjust the slider to change the scale of the holograms on the big map.")]
    [Range(0.05f, 20f)]
    [SerializeField] private float objectVisualScale = 5f;

    [Header("Objects to Duplicate & Sync")]
    [SerializeField] private List<GameObject> objectsToSync = new List<GameObject>();

    private struct SyncPair
    {
        public Transform MiniTransform;
        public Transform BigTransform;
    }

    private List<SyncPair> synchronizedPairs = new List<SyncPair>();
    private HashSet<Transform> knownTransforms = new HashSet<Transform>();
    private float calculatedPositionFactor = 1f;

    private void Start()
    {
        if (transform.localScale != Vector3.one)
        {
            Debug.LogWarning($"The Manager GameObject '{name}' should have a scale of (1,1,1) to prevent hologram distortion! Resetting it now.");
            transform.localScale = Vector3.one;
        }

        if (miniMapPlane == null || bigMapPlane == null)
        {
            Debug.LogError("Please assign both Mini Map Plane and Big Map Plane in the inspector!");
            return;
        }

        if (miniMapPlane.localScale.x > 0)
        {
            calculatedPositionFactor = bigMapPlane.localScale.x / miniMapPlane.localScale.x;
        }

        ScanAndSetupObjects();
    }

    private void LateUpdate()
    {
        if (miniMapPlane == null || bigMapPlane == null) return;

        ScanAndSetupObjects();

        int count = synchronizedPairs.Count;
        for (int i = count - 1; i >= 0; i--)
        {
            SyncPair pair = synchronizedPairs[i];

            if (pair.MiniTransform == null)
            {
                if (pair.BigTransform != null) Destroy(pair.BigTransform.gameObject);
                synchronizedPairs.RemoveAt(i);
                continue;
            }

            // 1. POSITION MAPPING
            Vector3 localPos = miniMapPlane.InverseTransformPoint(pair.MiniTransform.position);
            Vector3 worldPosOnBigPlane = bigMapPlane.TransformPoint(localPos);
            pair.BigTransform.position = worldPosOnBigPlane;

            // 2. ROTATION MAPPING
            Quaternion localRot = Quaternion.Inverse(miniMapPlane.rotation) * pair.MiniTransform.rotation;
            pair.BigTransform.rotation = bigMapPlane.rotation * localRot;

            // 3. LIVE OBJECT SCALE
            pair.BigTransform.localScale = pair.MiniTransform.localScale * objectVisualScale;
        }
    }

    private void ScanAndSetupObjects()
    {
        foreach (GameObject sourceGo in objectsToSync)
        {
            if (sourceGo == null) continue;

            foreach (Transform miniTransform in sourceGo.transform)
            {
                if (miniTransform == miniMapPlane || knownTransforms.Contains(miniTransform))
                    continue;

                DuplicateAndRegister(miniTransform);
                knownTransforms.Add(miniTransform);
            }
        }
    }

    private void DuplicateAndRegister(Transform miniTransform)
    {
        GameObject bigObjectGo = Instantiate(miniTransform.gameObject, transform);

        // Auto-detect if this object is a Spline system
        bool isSpline = bigObjectGo.GetComponentInChildren<UnityEngine.Splines.SplineContainer>() != null;

        if (!isSpline)
        {
            // Regular Objects: Remove all scripts, physics and colliders
            Rigidbody[] rbs = bigObjectGo.GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody rb in rbs) Destroy(rb);

            Collider[] colliders = bigObjectGo.GetComponentsInChildren<Collider>();
            foreach (Collider col in colliders) Destroy(col);

            MonoBehaviour[] scripts = bigObjectGo.GetComponentsInChildren<MonoBehaviour>();
            foreach (MonoBehaviour script in scripts)
            {
                if (script != null && script != this)
                    Destroy(script);
            }
        }
        else
        {
            // Splines: Keep scripts active so they can render, but disable physics/colliders safely for VR
            Collider[] childColliders = bigObjectGo.GetComponentsInChildren<Collider>();
            foreach (Collider col in childColliders)
            {
                col.enabled = false;
            }

            Rigidbody[] childRbs = bigObjectGo.GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody rb in childRbs)
            {
                rb.isKinematic = true;
            }
        }

        bigObjectGo.transform.localScale = miniTransform.localScale * objectVisualScale;

        SyncPair pair = new SyncPair
        {
            MiniTransform = miniTransform,
            BigTransform = bigObjectGo.transform
        };

        synchronizedPairs.Add(pair);
    }
}