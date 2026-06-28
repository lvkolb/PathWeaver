using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class ResetScene : MonoBehaviour
{
    public void HardReset()
    {
        // 1. Check whether NetworkManager exists and is active
        if (NetworkManager.Singleton != null)
        {
            // Trigger shutdown (stops the host, server or client)
            NetworkManager.Singleton.Shutdown();

            // Important: As NetworkManager is often set to "DontDestroyOnLoad", 
            // we destroy the GameObject completely so that, in the new scene, 
            // it is loaded from the prefab completely fresh and clean.
            Destroy(NetworkManager.Singleton.gameObject);
        }

        // 2. Now reload the scene completely from scratch
        // (This will also reset all local scripts)
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
