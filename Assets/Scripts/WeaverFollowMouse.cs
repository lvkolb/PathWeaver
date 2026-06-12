using UnityEngine;
using UnityEngine.InputSystem;

public class WeaverFollowMouse : MonoBehaviour
{


    public Camera mainCamera;
    public float posY = 0f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }
    // Update is called once per frame
    void Update()
    {

        mouseUpdate();

    }

    private void mouseUpdate()
    {
        if (Mouse.current == null)
        {
            Debug.Log("No mouse found. Please connect a mouse.");
            return;
        }

        if (mainCamera != null)
        {
            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                Vector3 worldPos = hit.point;
                worldPos.y = posY;

                this.transform.position = worldPos;
            }
        }
    }

}
