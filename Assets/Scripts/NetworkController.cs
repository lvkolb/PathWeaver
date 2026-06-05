using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class NetworkController : MonoBehaviour
{
    private VisualElement _ui;
    private Button _hostButton;
    private Button _clientButton;
    private TextField _ipTextField;

    private UnityTransport _transport;

    private void Awake()
    {
        _ui = GetComponent<UIDocument>().rootVisualElement;

        if (NetworkManager.Singleton != null)
        {
            _transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        }
    }

    private void OnEnable()
    {
        _hostButton = _ui.Q<Button>("host");
        _hostButton.clicked += Create;

        _clientButton = _ui.Q<Button>("client");
        _clientButton.clicked += Join;

        _ipTextField = _ui.Q<TextField>("ip-field");
    }

    private void OnDisable()
    {
        if (_hostButton != null) _hostButton.clicked -= Create;
        if (_clientButton != null) _clientButton.clicked -= Join;
    }

    public void Create()
    {
        Debug.Log("[Netcode] Clicked: Host. Starting server...");
        NetworkManager.Singleton.StartHost();
        HideUI();
    }

    public void Join()
    {
        string targetIP = "127.0.0.1";

        if (_ipTextField != null && !string.IsNullOrEmpty(_ipTextField.value))
        {
            targetIP = _ipTextField.value.Trim();
        }

        if (_transport != null)
        {
            NetworkManager.Singleton.Shutdown(); // Clean up ghost connections
            _transport.ConnectionData.Address = targetIP;
            Debug.Log($"[Netcode] Clicked: Client. Targeting Host IP: {targetIP} on Port: {_transport.ConnectionData.Port}");
        }

        NetworkManager.Singleton.StartClient();
        HideUI();
    }

    private void HideUI()
    {
        if (_ui != null) _ui.style.display = DisplayStyle.None;
    }
}