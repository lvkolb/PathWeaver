using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events; // Important for UnityEvent!
using UnityEngine.InputSystem;
using UnityEngine.Splines;

public class MultiSplineDrawer : MonoBehaviour
{
    public GameObject targetSpline;
    public float streetHeight = 0f;
    public float roadWidth = 4f;
    [Header("Add point if distance is far enough from last point")]
    [SerializeField] private float minDistance = 1f;

    [Header("Link Splines Tool")]
    [SerializeField] private SplineLinkTool linkTool;

    [Header("Events on Mouse Up (Only First Time)")]
    public UnityEvent onMouseUpEventOnlyFirstTime;

    [Header("Events on Mouse Up")]
    public UnityEvent onMouseUpEvent;

    private bool hasExecutedFirstTime = false;

    private SplineContainer splineContainer;
    private Spline activeSpline;
    private List<Vector3> currentPoints = new List<Vector3>();
    private InputAction holdAction;
    private bool isHolding;



    private void Awake()
    {
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

            // 1. Start with the internal tools (LinkTool)
            if (linkTool != null)
            {
                linkTool.ConnectAllInternalSplines();
            }

            // 3. Execute all functions in the list on mouse up First Time
            if (!hasExecutedFirstTime)
            {
                onMouseUpEventOnlyFirstTime.Invoke();

                hasExecutedFirstTime = true; // Flip the switch so it never happens again
            }

            // 3. Execute all functions in the list on mouse up
            if (onMouseUpEvent != null)
            {
                onMouseUpEvent.Invoke();
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
            // Only adds point if far enough from last one
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
    }
}