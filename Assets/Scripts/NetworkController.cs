using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

public class NetworkController : MonoBehaviour
{
    private VisualElement ui;
    private Button hostButton;
    private Button clientButton;

    private UnityTransport transport;

    private void Awake()
    {
        ui = GetComponent<UIDocument>().rootVisualElement;

        if (NetworkManager.Singleton != null)
        {
            transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        }
    }

    private void OnEnable()
    {
        hostButton = ui.Q<Button>("host");
        if (hostButton != null) hostButton.clicked += Create;

        clientButton = ui.Q<Button>("client");
        if (clientButton != null) clientButton.clicked += Join;
    }

    private void OnDisable()
    {
        if (hostButton != null) hostButton.clicked -= Create;
        if (clientButton != null) clientButton.clicked -= Join;
    }

    public void Create()
    {

        Debug.Log($"[Netcode] Clicked: Host. Starting server...");
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.StartHost();
            HideUI();
        }
    }

    public void Join()
    {
        if (NetworkManager.Singleton != null && transport != null)
        {
            NetworkManager.Singleton.Shutdown();

            // Reads the IP address that is currently configured in the UnityTransport inspector fields
            string targetIP = transport.ConnectionData.Address;
            Debug.Log($"[Netcode] Connecting to pre-configured Transport IP: {targetIP}");

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
        if (ui != null) ui.style.display = DisplayStyle.None;
    }

    void Start()
    {
        // Subscribe to connection events to see what happens under the hood
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe to avoid memory leaks
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[NETCODE INFO] Client Connected successfully! ID: {clientId}");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[NETCODE INFO] Client Disconnected/Failed to connect. ID: {clientId}");
    }
}