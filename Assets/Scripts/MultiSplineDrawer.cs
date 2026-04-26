using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;

public class MultiSplineDrawer : MonoBehaviour
{
    public GameObject targetSpline;
    public float streetHeight = 0f;
    [Header("Adds point if distance is far enough from last point")]
    [SerializeField] private float minDistance = 1f;

    [Header("Tools which executes after mouse leave")]
    [SerializeField] private SplineLinkTool linkTool;


    // Main container (only ONE in the scene)
    private SplineContainer splineContainer;

    // Currently active spline (stroke)
    private Spline activeSpline;

    // Points of current stroke
    private List<Vector3> currentPoints = new List<Vector3>();

    // Input
    private InputAction holdAction;
    private bool isHolding;

    private void Awake()
    {
        // Create ONE container
        // GameObject obj = new GameObject("Spline Container");

        splineContainer = targetSpline.GetComponent<SplineContainer>();

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

            // prüft das andere Script die Abstände und verbindet sie.
            if (linkTool != null)
            {
                linkTool.ConnectAllInternalSplines();
            }

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
            // Only add point if far enough from last one
            if (currentPoints.Count == 0 ||
                Vector3.Distance(currentPoints[^1], worldPos) > minDistance)
            {
                currentPoints.Add(worldPos);
                UpdateSpline();
            }
        }
    }

    // Create a new spline INSIDE the same container
    private void StartNewSpline()
    {
        activeSpline = new Spline();

        // Add spline to container (multiple splines allowed)
        splineContainer.AddSpline(activeSpline);
    }

    // Rebuild only the active spline
    private void UpdateSpline()
    {
        if (activeSpline == null) return;

        activeSpline.Clear();

        foreach (Vector3 worldPos in currentPoints)
        {
            Vector3 localPos = splineContainer.transform.InverseTransformPoint(worldPos);
            activeSpline.Add(new BezierKnot(localPos));
        }
    }
}