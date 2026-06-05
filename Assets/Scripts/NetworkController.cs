using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP; // Required to access UnityTransport

public class NetworkController : MonoBehaviour
{
    private VisualElement _ui;
    private Button _hostButton;
    private Button _clientButton;
    private TextField _ipTextField; // Reference for the UI Toolkit TextField

    private UnityTransport _transport;

    private void Awake()
    {
        _ui = GetComponent<UIDocument>().rootVisualElement;

        // Cache the UnityTransport component from the NetworkManager
        if (NetworkManager.Singleton != null)
        {
            _transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        }
    }

    private void OnEnable()
    {
        // Query the buttons from the UI Document
        _hostButton = _ui.Q<Button>("host");
        _hostButton.clicked += Create;

        _clientButton = _ui.Q<Button>("client");
        _clientButton.clicked += Join;

        // Query the TextField. Make sure the name matches the "Name" property in UI Builder!
        _ipTextField = _ui.Q<TextField>("ip-field");
    }

    private void OnDisable()
    {
        // Unsubscribe from events to prevent memory leaks
        if (_hostButton != null) _hostButton.clicked -= Create;
        if (_clientButton != null) _clientButton.clicked -= Join;
    }

    public void Create()
    {
        Debug.Log("Clicked: Host");
        NetworkManager.Singleton.StartHost();
        HideUI();
    }

    public void Join()
    {
        string targetIP = "127.0.0.1";

        // Read the IP from the UI Toolkit TextField if it's not empty
        if (_ipTextField != null && !string.IsNullOrEmpty(_ipTextField.value))
        {
            targetIP = _ipTextField.value.Trim();
        }

        if (_transport != null)
        {
            // Reset any previous connection states and apply the new IP
            NetworkManager.Singleton.Shutdown();
            _transport.ConnectionData.Address = targetIP;

            Debug.Log($"Clicked: Client. Trying to connect to Host IP: {targetIP}");
        }

        NetworkManager.Singleton.StartClient();
        HideUI();
    }

    private void HideUI()
    {
        // Safely hide the UI Document after connecting
        if (_ui != null)
        {
            _ui.style.display = DisplayStyle.None;
        }
    }
}