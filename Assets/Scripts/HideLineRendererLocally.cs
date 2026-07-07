using Unity.Netcode;
using UnityEngine;

public class HideLineRendererLocally : NetworkBehaviour
{
    [SerializeField] private LineRenderer lineRendererToHide;

    public override void OnNetworkSpawn()
    {
        // If this is the local player who owns this object
        if (IsOwner)
        {
            DisableLineLocally();
        }
    }

    private void DisableLineLocally()
    {
        if (lineRendererToHide != null)
        {
            // Disable the component only for the owner
            lineRendererToHide.enabled = false;
            Debug.Log("LineRenderer disabled for the local owner. Remote players can still see it.");
        }
        else
        {
            Debug.LogWarning("No LineRenderer assigned to hide.");
        }
    }
}