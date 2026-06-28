using UnityEngine;
using UnityEngine.SceneManagement;

public class ResetScene : MonoBehaviour
{
    public void HardReset()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
