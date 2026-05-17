using UnityEngine;

public class Player : MonoBehaviour
{

    [Header("VR Camera Rig Reference")]
    public Transform vrCameraRig;
    public float posY = 0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (vrCameraRig == null)
        {
            Debug.Log("No camera rig set. Please set a camera rig");
            return;
        }

        float posX = vrCameraRig.position.x;
        float posZ = vrCameraRig.position.z;

        Vector3 currentPos = new(posX, posY, posZ);

        this.transform.position = currentPos;
    }
}
