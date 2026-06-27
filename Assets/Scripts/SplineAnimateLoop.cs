using UnityEngine;
using UnityEngine.Splines;

public class SplineAnimateLoop : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SplineContainer splineContainer;
    [SerializeField] private GameObject objectToAnimate;

    [Header("Settings")]
    [SerializeField] private float speed = 5f;

    // Normalized time tracking (0.0 to 1.0)
    private float progress = 0f;
    private float splineLength;

    void Start()
    {
        if (splineContainer == null || objectToAnimate == null)
        {
            Debug.LogError($"[{gameObject.name}] Missing references in SplineAnimateLoop!", this);
            enabled = false;
            return;
        }

        // Cache the total length of the spline for accurate speed calculation
        splineLength = splineContainer.CalculateLength();
    }

    void Update()
    {
        if (splineLength <= 0) return;

        // Calculate progress change based on constant speed, independent of spline length
        float progressIncrement = (speed / splineLength) * Time.deltaTime;
        progress += progressIncrement;

        // Loop the progress safely between 0.0 and 1.0
        progress %= 1f;

        // Evaluate position and tangent (direction) on the spline
        // Using local space evaluation to keep it flexible
        Vector3 localPosition = (Vector3)splineContainer.EvaluatePosition(progress);
        Vector3 localForward = (Vector3)splineContainer.EvaluateTangent(progress);

        // Transform local spline coordinates to world space
        Vector3 worldPosition = splineContainer.transform.TransformPoint(localPosition);
        Vector3 worldForward = splineContainer.transform.TransformDirection(localForward);

        // Apply position and rotation to the target object
        objectToAnimate.transform.position = worldPosition;
        if (worldForward != Vector3.zero)
        {
            objectToAnimate.transform.rotation = Quaternion.LookRotation(worldForward, splineContainer.transform.up);
        }
    }
}