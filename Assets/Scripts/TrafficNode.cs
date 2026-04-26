using UnityEngine;
using System.Collections.Generic;

public class TrafficNode : MonoBehaviour
{
    public List<TrafficNode> neighbors = new List<TrafficNode>();
    public float congestionPenalty = 0f;
    public int splineIndex;   // which spline this node lives on
    [HideInInspector] public float tValue;       // 0..1 position along that spline
}