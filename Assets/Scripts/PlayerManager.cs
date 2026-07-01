using UnityEngine;
using Unity.Netcode;

public class PlayerManager : NetworkBehaviour
{
    public enum PlayerMode { Weaver, Jammer }

    // Synced state across the network. Only the owner can request a change, but everyone updates.
    public NetworkVariable<PlayerMode> currentMode = new NetworkVariable<PlayerMode>(
        PlayerMode.Weaver,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner // Allowed to write if this object belongs to the player
    );

    public GameObject weaver;
    public GameObject jammer;

    public override void OnNetworkSpawn()
    {
        // Hook up the state change listener for everyone
        currentMode.OnValueChanged += OnModeChanged;

        // Initial setup
        UpdateVisuals(currentMode.Value);
    }

    public override void OnNetworkDespawn()
    {
        currentMode.OnValueChanged -= OnModeChanged;
    }

    [ContextMenu("ChangePlayerMode")]
    public void ChangePlayerMode()
    {
        // Only the owner of this player object should toggle the mode
        if (!IsOwner) return;

        PlayerMode nextMode = (currentMode.Value == PlayerMode.Weaver) ? PlayerMode.Jammer : PlayerMode.Weaver;
        currentMode.Value = nextMode;
    }

    private void OnModeChanged(PlayerMode previousMode, PlayerMode newMode)
    {
        UpdateVisuals(newMode);
    }

    // Handles enabling and disabling across all instances (Host & Clients)
    private void UpdateVisuals(PlayerMode mode)
    {
        if (weaver != null) weaver.SetActive(mode == PlayerMode.Weaver);
        if (jammer != null) jammer.SetActive(mode == PlayerMode.Jammer);
    }
}