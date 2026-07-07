using UnityEngine;

public class DrawingSourceTag : MonoBehaviour
{
    public static System.Collections.Generic.HashSet<Transform> ActiveSources = new System.Collections.Generic.HashSet<Transform>();

    private void OnEnable() => ActiveSources.Add(transform);
    private void OnDisable() => ActiveSources.Remove(transform);
}