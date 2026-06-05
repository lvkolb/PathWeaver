using Unity.Netcode;
using UnityEngine;

public class VRNetworkPlayer : NetworkBehaviour
{
    [SerializeField] private Transform headTarget;
    [SerializeField] private Transform leftHandTarget;
    [SerializeField] private Transform rightHandTarget;

    private Transform _localHead;
    private Transform _localLeftHand;
    private Transform _localRightHand;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Only the local player should track the physical VR headset hardware
        if (IsOwner)
        {
            // Find the OVRCameraRig in the scene
            OVRCameraRig cameraRig = FindAnyObjectByType<OVRCameraRig>();

            if (cameraRig != null)
            {
                _localHead = cameraRig.centerEyeAnchor;
                _localLeftHand = cameraRig.leftHandAnchor;
                _localRightHand = cameraRig.rightHandAnchor;
            }
        }
    }

    private void Update()
    {
        // If this is the local player, copy the local VR hardware positions to the network prefab
        if (IsOwner)
        {
            MapTarget(headTarget, _localHead);
            MapTarget(leftHandTarget, _localLeftHand);
            MapTarget(rightHandTarget, _localRightHand);
        }
    }

    private void MapTarget(Transform target, Transform source)
    {
        if (target != null && source != null)
        {
            target.position = source.position;
            target.rotation = source.rotation;
        }
    }
}