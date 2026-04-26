using UnityEngine;
using UnityEngine.UI;

public class WebcamInput : MonoBehaviour
{
    private WebCamTexture webcamTexture;
    public RawImage display;

    void Start()
    {
        // Get the default camera
        webcamTexture = new WebCamTexture(320, 240); // Low res is faster for tracking!
        display.texture = webcamTexture;
        webcamTexture.Play();
    }

    public Color[] GetPixels() => webcamTexture.GetPixels();
    public int Width => webcamTexture.width;
    public int Height => webcamTexture.height;
}