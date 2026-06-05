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
        // HARDCODED TEST: Replace the numbers below with your exact Host IPv4 address
        string targetIP = "10.75.184.73"; // <-- HIER DEINE ECHTE HOST-IP EINTRAGEN!

        if (_transport != null)
        {
            NetworkManager.Singleton.Shutdown();
            _transport.ConnectionData.Address = targetIP;
            Debug.Log($"[TEST] Forcing connection to hardcoded IP: {_transport.ConnectionData.Address}");
        }

        bool success = NetworkManager.Singleton.StartClient();
        Debug.Log($"[TEST] StartClient called. Success status: {success}");
    }

    private void HideUI()
    {
        if (_ui != null) _ui.style.display = DisplayStyle.None;
    }
}