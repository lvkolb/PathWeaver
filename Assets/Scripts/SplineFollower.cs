using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class SplineFollower : MonoBehaviour
{
    [Header("Navigation State")]
    public RandomSplineRoadArchitect architect;
    public int homeSplineIndex;
    public int workSplineIndex;
    public bool isHeadingToWork = true;

    [Header("Driving Settings")]
    public float maxSpeed = 10.0f;
    public float acceleration = 5.0f;
    public float brakingForce = 10.0f;

    [Header("Safety (ACC)")]
    public float detectionRange = 8.0f;
    public float minSafeDistance = 3.0f;
    public LayerMask obstacleLayer;


    private SplineContainer pathToWork;
    private SplineContainer pathToHome;
    private float pathToWorkLength;
    private float pathToHomeLength;

    public float currentSpeed;
    private double distanceTraveled;
    private float splineLength;
    public SplineContainer targetContainer;

    void Start()
    {
        //targetContainer = GetComponent<SplineContainer>();
        
    }

    void Update()
    {
        if (targetContainer == null || targetContainer.Splines.Count == 0) return;

        HandleSpeed();
        MoveAlongSpline();
    }

    public void RefreshPath()
    {
        if (architect == null || targetContainer == null) return;

        // Bake BOTH directions once
        pathToWork = targetContainer; // reuse the assigned container for A->B

        // Create a second container for B->A on the same object
        pathToHome = targetContainer.gameObject.AddComponent<SplineContainer>();

        architect.GeneratePathForVehicle(pathToWork, homeSplineIndex, workSplineIndex);
        architect.GeneratePathForVehicle(pathToHome, workSplineIndex, homeSplineIndex);

        pathToWorkLength = pathToWork.Splines[0].GetLength();
        pathToHomeLength = pathToHome.Splines[0].GetLength();

        // Snap to start of first leg
        distanceTraveled = 0;
        currentSpeed = maxSpeed;
        SnapToCurrentPathStart();
    }
    private void SnapToCurrentPathStart()
    {
        var container = isHeadingToWork ? pathToWork : pathToHome;
        if (container == null) return;
        float3 startPos, fwd, up;
        container.Evaluate(0, 0f, out startPos, out fwd, out up);
        transform.position = (Vector3)startPos;
    }

    private void MoveAlongSpline()
    {
        var container = isHeadingToWork ? pathToWork : pathToHome;
        float splineLen = isHeadingToWork ? pathToWorkLength : pathToHomeLength;

        if (container == null || container.Splines.Count == 0 || splineLen < 0.1f) return;

        distanceTraveled += currentSpeed * Time.deltaTime;

        if (distanceTraveled >= splineLen)
        {
            // Flip direction, no regen needed
            isHeadingToWork = !isHeadingToWork;
            distanceTraveled = 0;
            return; // next frame picks up the other container
        }

        float t = Mathf.Clamp01((float)(distanceTraveled / splineLen));
        float3 pos, fwd, up;
        container.Evaluate(0, t, out pos, out fwd, out up);
        transform.position = (Vector3)pos;

        if (math.lengthsq(fwd) > 0.001f)
            transform.rotation = Quaternion.LookRotation(fwd, up);
    }

    private void HandleSpeed()
    {
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        bool obstacle = Physics.Raycast(rayOrigin, transform.forward, out RaycastHit hit, detectionRange, obstacleLayer);
        float targetSpeed = obstacle ? (hit.distance <= minSafeDistance ? 0 : maxSpeed * ((hit.distance - minSafeDistance) / (detectionRange - minSafeDistance))) : maxSpeed;
        float lerpSpeed = (targetSpeed < currentSpeed) ? brakingForce : acceleration;
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, Time.deltaTime * lerpSpeed);
    }

    // Visualization of the sensors in the Unity Editor
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Vector3 rayOrigin = transform.position + Vector3.up * 0.5f;
        Gizmos.DrawRay(rayOrigin, transform.forward * detectionRange);
    }
}