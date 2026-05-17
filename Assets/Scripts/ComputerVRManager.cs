using UnityEngine;
using System.Collections.Generic;

public class ComputerXRManager : MonoBehaviour
{
    [Header("On = Playmode for XR; OFF = Playmode for mouse/pc")]
    public bool useXR = false;

    [Header("List of GameObjects")]
    public List<GameObject> gameObjectsForComputer = new();
    public List<GameObject> gameObjectsForXR = new();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        foreach (GameObject obj in gameObjectsForComputer)
        {
            if (obj != null)
            {
                if (useXR)
                {
                    obj.SetActive(false);
                }
                else
                {
                    obj.SetActive(true);

                }
            }
        }

        foreach (GameObject obj in gameObjectsForXR)
        {
            if (obj != null)
            {
                if (useXR)
                {
                    obj.SetActive(true);
                }
                else
                {
                    obj.SetActive(false);

                }
            }
        }


    }

    // Update is called once per frame
    void Update()
    {

    }
}
