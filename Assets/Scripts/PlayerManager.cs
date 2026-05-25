using UnityEngine;

public class PlayerManager : MonoBehaviour
{
    public enum PlayerMode { Weaver, Jammer }
    public PlayerMode currentMode;

    public GameObject weaver;
    public GameObject jammer;

    [ContextMenu("ChangePlayerMode")]
    public void ChangePlayerMode()
    {
        // Switch to mode 1 (if Weaver, then Jammer; otherwise, Weaver)
        currentMode = (currentMode == PlayerMode.Weaver) ? PlayerMode.Jammer : PlayerMode.Weaver;

        // 2. Enable/disable GameObjects as required
        UpdateVisuals();

        Debug.Log("PlayerMode changed to: " + currentMode);
    }

    void Start()
    {
        UpdateVisuals();
    }

    // Handles enabling and disabling
    private void UpdateVisuals()
    {
        weaver.SetActive(currentMode == PlayerMode.Weaver);
        jammer.SetActive(currentMode == PlayerMode.Jammer);
    }
}