using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

public class NetworkController : MonoBehaviour
{

    public VisualElement ui;
    public Button hostButton;
    public Button clientButton;

    private void Awake()
    {
        ui = GetComponent<UIDocument>().rootVisualElement;
    }

    private void OnEnable()
    {
        hostButton = ui.Q<Button>("host");
        hostButton.clicked += Create;
        clientButton = ui.Q<Button>("client");
        clientButton.clicked += Join;

    }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }


    public void Create()
    {
        NetworkManager.Singleton.StartHost();
        Debug.Log("Clicked: Host");
    }
    public void Join()
    {
        NetworkManager.Singleton.StartClient();
        Debug.Log("Clicked: Client");
    }

}
