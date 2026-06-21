using UnityEngine;

public class FollowObject : MonoBehaviour
{
    [Header("Target to Follow")]
    [SerializeField] private Transform targetToFollow;

    [Header("Follow Position")]
    public bool followPositionX = true;
    public bool followPositionY = true;
    public bool followPositionZ = true;

    [Header("Follow Rotation")]
    public bool followRotationX = true;
    public bool followRotationY = true;
    public bool followRotationZ = true;

    void Update()
    {
        if (targetToFollow == null) return;

        // 1. POSITION HANDLING
        Vector3 currentPosition = transform.localPosition;
        Vector3 targetPosition = targetToFollow.localPosition;

        float newPosX = followPositionX ? targetPosition.x : currentPosition.x;
        float newPosY = followPositionY ? targetPosition.y : currentPosition.y;
        float newPosZ = followPositionZ ? targetPosition.z : currentPosition.z;

        transform.localPosition = new Vector3(newPosX, newPosY, newPosZ);

        // 2. ROTATION HANDLING
        Vector3 currentAngles = transform.localEulerAngles;
        Vector3 targetAngles = targetToFollow.localEulerAngles;

        float newRotX = followRotationX ? targetAngles.x : currentAngles.x;
        float newRotY = followRotationY ? targetAngles.y : currentAngles.y;
        float newRotZ = followRotationZ ? targetAngles.z : currentAngles.z;

        transform.localEulerAngles = new Vector3(newRotX, newRotY, newRotZ);
    }
}