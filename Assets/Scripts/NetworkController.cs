using System;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

public class NetworkController : MonoBehaviour
{
    private VisualElement _ui;
    private Button _hostButton;
    private Button _clientButton;
    private TextField _joinCodeInput;
    private Label _codeDisplayLabel;

    private void Awake()
    {
        _ui = GetComponent<UIDocument>().rootVisualElement;
    }

    private async void Start()
    {
        // Setup simple UI hooks
        _hostButton = _ui.Q<Button>("host");
        _clientButton = _ui.Q<Button>("client");
        _joinCodeInput = _ui.Q<TextField>("joinCodeInput");
        _codeDisplayLabel = _ui.Q<Label>("codeDisplay");

        if (_hostButton != null) _hostButton.clicked += Create;
        if (_clientButton != null) _clientButton.clicked += Join;

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
            if (_codeDisplayLabel != null) _codeDisplayLabel.text = "Error starting Services.";
        }
    }

    private void OnDisable()
    {
        if (_hostButton != null) _hostButton.clicked -= Create;
        if (_clientButton != null) _clientButton.clicked -= Join;
    }

    public async void Create()
    {
        if (_codeDisplayLabel != null) _codeDisplayLabel.text = "Creating session...";

        try
        {
            // Create options and force it to use the Relay Network
            var options = new SessionOptions { MaxPlayers = 4 }.WithRelayNetwork();

            // Allocates Relay and starts the Host automatically
            var session = await MultiplayerService.Instance.CreateSessionAsync(options);

            Debug.Log($"[Netcode] Session created! Code: {session.Code}");
            if (_codeDisplayLabel != null) _codeDisplayLabel.text = $"Join Code: {session.Code}";
        }
        catch (Exception e)
        {
            Debug.LogError($"[Netcode] Create Session failed: {e.Message}");
            if (_codeDisplayLabel != null) _codeDisplayLabel.text = "Failed to create room.";
        }
    }

    public async void Join()
    {
        if (_joinCodeInput == null) return;
        string code = _joinCodeInput.value.Trim();

        if (string.IsNullOrEmpty(code) || code.Length != 6)
        {
            if (_codeDisplayLabel != null) _codeDisplayLabel.text = "Enter a 6-digit code!";
            return;
        }

        if (_codeDisplayLabel != null) _codeDisplayLabel.text = "Joining session...";

        try
        {
            // Join the session by its code
            var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);

            Debug.Log("[Netcode] Successfully joined via Relay!");
            if (_codeDisplayLabel != null) _codeDisplayLabel.text = $"Joined Room: {code}";
        }
        catch (Exception e)
        {
            Debug.LogError($"[Netcode] Join Session failed: {e.Message}");
            if (_codeDisplayLabel != null) _codeDisplayLabel.text = "Failed to join.";
        }
    }

    // FIXED: Added the missing helper method back into the script context
    private void SetUIInteractable(bool state)
    {
        if (_hostButton != null) _hostButton.SetEnabled(state);
        if (_clientButton != null) _clientButton.SetEnabled(state);
        if (_joinCodeInput != null) _joinCodeInput.SetEnabled(state);
    }
}