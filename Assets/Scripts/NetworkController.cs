using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

public class NetworkController : MonoBehaviour
{
    private VisualElement _ui;
    private Button _hostButton;
    private Button _clientButton;

    private void Awake()
    {
        _ui = GetComponent<UIDocument>().rootVisualElement;
    }

    private void OnEnable()
    {
        _hostButton = _ui.Q<Button>("host");
        if (_hostButton != null) _hostButton.clicked += Create;

        _clientButton = _ui.Q<Button>("client");
        if (_clientButton != null) _clientButton.clicked += Join;
    }

    private void OnDisable()
    {
        if (_hostButton != null) _hostButton.clicked -= Create;
        if (_clientButton != null) _clientButton.clicked -= Join;
    }

    public void Create()
    {
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("[Netcode] Starting Host...");
            NetworkManager.Singleton.StartHost();
            HideUI();
        }
    }

    public void Join()
    {
        if (NetworkManager.Singleton != null)
        {
            Debug.Log("[Netcode] Starting Client using Inspector IP configuration...");

            // Just start the client. Netcode automatically reads the IP from your UnityTransport component!
            bool success = NetworkManager.Singleton.StartClient();
            Debug.Log($"[Netcode] StartClient called. Success status: {success}");

            if (success)
            {
                HideUI();
            }
        }
    }

    private void HideUI()
    {
        if (_ui != null) _ui.style.display = DisplayStyle.None;
    }

    void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[NETCODE INFO] Connected successfully! ID: {clientId}");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[NETCODE INFO] Connection failed or disconnected. ID: {clientId}");
    }
}