using UnityEngine;
using System.Collections.Generic;

public class ComputerVRManager : MonoBehaviour
{
    [Header("On = Playmode for VR; OFF = Playmode for mouse/pc")]
    public bool isVR = false;

    [Header("List of GameObjects")]
    public List<GameObject> gameObjectsForComputer = new();
    public List<GameObject> gameObjectsForVR = new();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        foreach (GameObject obj in gameObjectsForComputer)
        {
            if (obj != null)
            {
                if (isVR)
                {
                    obj.SetActive(false);
                }
                else
                {
                    obj.SetActive(true);

                }
            }
        }

        foreach (GameObject obj in gameObjectsForVR)
        {
            if (obj != null)
            {
                if (isVR)
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
