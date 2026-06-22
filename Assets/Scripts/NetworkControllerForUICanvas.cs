using System;
using UnityEngine;
using UnityEngine.UI; // Required for standard Canvas Buttons
using TMPro;          // Required for TextMeshPro InputFields and Labels
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

public class NetworkControllerForUICanvas : MonoBehaviour
{
    [Header("UI Canvas References")]
    [SerializeField] private GameObject uiPanel; // The main UI panel containing the connection controls
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button hideUIButton; // Button to manually hide the UI panel
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private TMP_Text codeDisplayLabel;

    [Header("Editor Debug Settings")]
    [Tooltip("Enter the session code here to join directly via the Inspector Context Menu.")]
    [SerializeField] private string editorJoinCode;

    private async void Start()
    {
        // Register button click events (uGUI syntax)
        if (hostButton != null) hostButton.onClick.AddListener(Create);
        if (clientButton != null) clientButton.onClick.AddListener(Join);
        if (hideUIButton != null) hideUIButton.onClick.AddListener(HideUI);

        // Turn off buttons while initializing services
        SetUIInteractable(false);

        try
        {
            // 1. Initialize Core Services
            await UnityServices.InitializeAsync();

            // 2. Sign in anonymously so Unity knows who is creating the session
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log($"[Netcode] Player signed in anonymously. Player ID: {AuthenticationService.Instance.PlayerId}");
            }

            Debug.Log("[Netcode] Unity Services & Authentication Ready.");

            // Re-enable buttons now that everything is ready
            SetUIInteractable(true);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Netcode] Initialization failed: {e.Message}");
            if (codeDisplayLabel != null) codeDisplayLabel.text = "Error starting Services.";
        }
    }

    private void OnDisable()
    {
        // Unregister events to prevent memory leaks
        if (hostButton != null) hostButton.onClick.RemoveListener(Create);
        if (clientButton != null) clientButton.onClick.RemoveListener(Join);
        if (hideUIButton != null) hideUIButton.onClick.RemoveListener(HideUI);
    }

    [ContextMenu("Start Host")]
    public async void Create()
    {
        if (codeDisplayLabel != null) codeDisplayLabel.text = "Creating session...";

        try
        {
            // Create options and force it to use the Relay Network
            var options = new SessionOptions { MaxPlayers = 4 }.WithRelayNetwork();

            // Allocates Relay and starts the Host automatically
            var session = await MultiplayerService.Instance.CreateSessionAsync(options);

            Debug.Log($"[Netcode] Session created! Code: {session.Code}");
            if (codeDisplayLabel != null) codeDisplayLabel.text = $"Join Code: {session.Code}";

            // Hide the UI since the session was successfully created
            // HideUI();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Netcode] Create Session failed: {e.Message}");
            if (codeDisplayLabel != null) codeDisplayLabel.text = "Failed to create room.";
        }
    }

    [ContextMenu("Join Session as Client")]
    public async void Join()
    {
        string code = string.Empty;

        // Check if we are using the UI InputField or the Editor Field
        if (joinCodeInput != null && !string.IsNullOrEmpty(joinCodeInput.text))
        {
            code = joinCodeInput.text.Trim();
        }
        else if (!string.IsNullOrEmpty(editorJoinCode))
        {
            code = editorJoinCode.Trim();
        }

        if (string.IsNullOrEmpty(code) || code.Length != 6)
        {
            Debug.LogWarning("[Netcode] Please enter a valid 6-digit join code!");
            if (codeDisplayLabel != null) codeDisplayLabel.text = "Enter a 6-digit code!";
            return;
        }

        if (codeDisplayLabel != null) codeDisplayLabel.text = "Joining session...";

        try
        {
            // Join the session by its code
            var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);

            Debug.Log($"[Netcode] Successfully joined via Relay! Code: {code}");
            if (codeDisplayLabel != null) codeDisplayLabel.text = $"Joined Room: {code}";

            // Hide the UI since the session was successfully joined
            // HideUI();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Netcode] Join Session failed: {e.Message}");
            if (codeDisplayLabel != null) codeDisplayLabel.text = "Failed to join.";
        }
    }

    private void SetUIInteractable(bool state)
    {
        if (hostButton != null) hostButton.interactable = state;
        if (clientButton != null) clientButton.interactable = state;
        if (hideUIButton != null) hideUIButton.interactable = state;
        if (joinCodeInput != null) joinCodeInput.interactable = state;
    }

    private void HideUI()
    {
        if (uiPanel != null)
        {
            uiPanel.SetActive(false);
        }
    }
}