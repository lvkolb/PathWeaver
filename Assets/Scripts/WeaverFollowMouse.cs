using UnityEngine;
using UnityEngine.InputSystem;

public class WeaverFollowMouse : MonoBehaviour
{
    public Camera mainCamera;

    [Tooltip("Select the 'Ground' layer here in the Inspector")]
    public LayerMask groundLayer;

    void Start()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }

    void Update()
    {
        MouseUpdate();
    }

    private void MouseUpdate()
    {
        if (Mouse.current == null)
        {
            Debug.LogWarning("No mouse found. Please connect a mouse.");
            return;
        }

        if (mainCamera != null)
        {
            // Create a ray from the camera to the mouse position
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

            // Perform the raycast, but ONLY hit colliders on the specified ground layer
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
            {
                // The hit.point is now exactly where the cursor touches your ground plane
                Vector3 worldPos = hit.point;
                // Debug.DrawRay(hit.point, Vector3.up * 5f, Color.red);

                // Apply the position directly to this object
                this.transform.position = worldPos;
            }
        }
    }
}