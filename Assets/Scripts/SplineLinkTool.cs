using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections.Generic;

public class SplineLinkTool : MonoBehaviour
{
    [SerializeField] private SplineContainer container;
    [Header("Links knots if the distance falls below or equals the value")]
    [SerializeField] private float connectThreshold = 1.5f;

    // void Start()
    // {
    //     if (container != null)
    //     {
    //         ConnectAllInternalSplines();
    //     }
    // }

    public void ConnectAllInternalSplines()
    {
        var splines = container.Splines;

        // Wir vergleichen jeden Spline mit jedem (i = erster, j = zweiter)
        for (int i = 0; i < splines.Count; i++)
        {
            for (int j = i + 1; j < splines.Count; j++)
            {
                CompareAndConnect(i, j);
            }
        }
    }

    private void CompareAndConnect(int indexA, int indexB)
    {
        var splineA = container.Splines[indexA];
        var splineB = container.Splines[indexB];

        for (int knotIdxA = 0; knotIdxA < splineA.Count; knotIdxA++)
        {
            float3 posA = splineA[knotIdxA].Position;

            for (int knotIdxB = 0; knotIdxB < splineB.Count; knotIdxB++)
            {
                float3 posB = splineB[knotIdxB].Position;

                if (math.distance(posA, posB) <= connectThreshold)
                {
                    // Mittelpunkt berechnen (da im selben Container, keine Transform-Umrechnung nötig)
                    float3 midPoint = (posA + posB) * 0.5f;

                    // Positionen angleichen
                    var knotA = splineA[knotIdxA];
                    knotA.Position = midPoint;
                    splineA[knotIdxA] = knotA;

                    var knotB = splineB[knotIdxB];
                    knotB.Position = midPoint;
                    splineB[knotIdxB] = knotB;

                    // Logische Verknüpfung im Container erstellen
                    SplineKnotIndex kA = new SplineKnotIndex(indexA, knotIdxA);
                    SplineKnotIndex kB = new SplineKnotIndex(indexB, knotIdxB);

                    container.LinkKnots(kA, kB);


                    Debug.Log($"Connected: Spline {indexA} (Knot {knotIdxA}) with Spline {indexB} (Knot {knotIdxB})");
                }
            }
        }
    }
}