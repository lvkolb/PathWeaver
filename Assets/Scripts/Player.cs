using UnityEngine;
using UnityEngine.InputSystem;

public class Player : MonoBehaviour
{

    [Header("Reference")]
    public ComputerXRManager computerXRManager;
    public Transform xrCameraRig;
    private bool XRisUsed;
    public Camera mainCamera;
    public float posY = 0f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        XRisUsed = computerXRManager.useXR;
    }

    // Update is called once per frame
    void Update()
    {
        if (XRisUsed)
        {
            XRUpdate();
        }
        else
        {
            mouseUpdate();
        }
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


    private void XRUpdate()
    {
        if (xrCameraRig == null)
        {
            Debug.Log("No camera rig set. Please set a camera rig");
            return;
        }

        float posX = xrCameraRig.position.x;
        float posZ = xrCameraRig.position.z;

        Vector3 currentPos = new(posX, posY, posZ);

        this.transform.position = currentPos;
    }
}
