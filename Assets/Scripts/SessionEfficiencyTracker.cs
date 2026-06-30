using UnityEngine;
using System.Collections.Generic;

public class SessionEfficiencyTracker : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private TrafficNetwork trafficNetwork;
    [SerializeField] private VehicleManager vehicleManager;

    [Header("Score Weights (Must total 1.0)")]
    [Range(0f, 1f)] public float throughputWeight = 0.5f;
    [Range(0f, 1f)] public float congestionWeight = 0.3f;
    [Range(0f, 1f)] public float utilizationWeight = 0.2f;

    [Header("Live Metrics Output")]
    [SerializeField] private float finalEfficiencyScore = 100f;
    public float FinalScore => finalEfficiencyScore;

    private int _tripsInCurrentWindow = 0;
    private float _windowTimer = 0f;
    private float _coverageTimer = 0f;
    private float _rollingThroughputScore = 100f;
    // Add these to SessionEfficiencyTracker.cs to feed the UI
    public float GetNormalizedThroughput() => _rollingThroughputScore / 100f;

    // Store these values as private variables so the UI can access them
    private float _lastCongestionPct = 0f;
    private float _lastUtilizationPct = 0f;

    // Update the helper methods to return the stored values
    public float GetNormalizedCongestion() => (100f - _lastCongestionPct) / 100f;
    public float GetNormalizedCongestionSlider() => _lastCongestionPct / 100f;
    public float GetNormalizedUtilization() => _lastUtilizationPct / 100f;
    // Helper to avoid redundant calculations in UI
    private float GetCurrentCongestionPct() { /* Return the calculated congestion % */ return 0f; }
    private float GetUtilizationPct() { /* Return the calculated utilization % */ return 0f; }
    // Retrieve active cars dynamically via reflection or direct tracking setup
    private List<CarAgent> GetActiveAgents()
    {
        List<CarAgent> agents = new List<CarAgent>();
        // Grabs active agents from scene hierarchies managed by VehicleManager
        var vehicles = GameObject.FindGameObjectsWithTag("Vehicle"); // Or find via components
        foreach (var v in vehicles)
        {
            var agent = v.GetComponent<CarAgent>();
            if (agent != null) agents.Add(agent);
        }
        return agents;
    }

    private void Start()
    {
        // Initialize listening for trip cycles across all spawned units
        InvokeRepeating(nameof(HookAgentListeners), 1f, 3f);
    }

    private void HookAgentListeners()
    {
        foreach (var agent in GetActiveAgents())
        {
            agent.OnTripCycleCompleted -= RegisterTrip;
            agent.OnTripCycleCompleted += RegisterTrip;
        }
    }

    private void RegisterTrip() => _tripsInCurrentWindow++;

    private void Update()
    {
        _windowTimer += Time.deltaTime;
        _coverageTimer += Time.deltaTime;

        // Calculate rolling throughput window once every 10 seconds to avoid twitchy data spikes
        if (_windowTimer >= 10f)
        {
            CalculateRollingThroughput();
        }
        if (_coverageTimer >= 45f)
        {
            ResetNodesOnly();
            _coverageTimer = 0f;
        }
        EvaluateMasterScore();
    }
    private void ResetNodesOnly()
    {
        // Safety check to prevent null reference errors
        if (trafficNetwork == null || trafficNetwork.allNodes == null) return;

        foreach (var node in trafficNetwork.allNodes)
        {
            if (node != null)
            {
                node.wasVisited = false;
            }
        }
    }
    private float GetDynamicPathLength()
    {
        // Fallback in case the network isn't initialized yet
        if (trafficNetwork == null || trafficNetwork.allNodes.Count == 0) return 20f;

        Vector3 minBounds = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 maxBounds = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        foreach (var node in trafficNetwork.allNodes)
        {
            // Assuming your node object has a Transform component
            if (node != null)
            {
                Vector3 pos = node.transform.position;
                minBounds = Vector3.Min(minBounds, pos);
                maxBounds = Vector3.Max(maxBounds, pos);
            }
        }

        // Calculate the diagonal distance of the entire city grid
        float cityDiagonal = Vector3.Distance(minBounds, maxBounds);

        // Rule of thumb: An average trip inside a closed network is about 45% of the max diagonal.
        // We enforce a minimum length of 20f to prevent math errors on tiny networks.
        return Mathf.Max(20f, cityDiagonal * 0.45f);
    }

    private void CalculateRollingThroughput()
    {
        List<CarAgent> activeAgents = GetActiveAgents();

        // Safety check: Reset to 0 if there are no cars, rather than just returning
        if (activeAgents.Count == 0)
        {
            _rollingThroughputScore = 0f;
            return;
        }

        float totalFleetSpeed = 0f;
        foreach (var agent in activeAgents) totalFleetSpeed += agent.baseSpeed;
        float avgSpeed = totalFleetSpeed / activeAgents.Count;

        // 1. DYNAMIC PATH LENGTH
        // Replaces the hardcoded 20f with our responsive city-size calculation
        float estimatedAvgPathLength = GetDynamicPathLength();

        float idealTripDuration = estimatedAvgPathLength / (avgSpeed > 0 ? avgSpeed : 1f);
        float idealTripsPerMinutePerCar = 60f / idealTripDuration;
        float expectedFleetTripsPerMin = activeAgents.Count * idealTripsPerMinutePerCar;

        // 2. ACTUAL PERFORMANCE
        // Extrapolate our 10-second sample window to 60 seconds
        float actualTripsPerMinute = (_tripsInCurrentWindow / _windowTimer) * 60f;
        float rawThroughput = (actualTripsPerMinute / expectedFleetTripsPerMin) * 100f;

        // 3. THE CONGESTION CAP ("Jammer Tax")
        // Ensure the throughput score can NEVER exceed the available unblocked roads
        float maxPossibleThroughput = 100f - _lastCongestionPct;

        // Apply the score, clamped to the physical reality of the traffic jam
        _rollingThroughputScore = Mathf.Clamp(rawThroughput, 0f, maxPossibleThroughput);

        // Reset tracking window
        _tripsInCurrentWindow = 0;
        _windowTimer = 0f;
    }

    private void EvaluateMasterScore()
    {
        List<CarAgent> activeAgents = GetActiveAgents();
        if (activeAgents.Count == 0 || trafficNetwork.allNodes.Count == 0) return;

        // 1. Calculate Congestion Ratio
        int stuckCount = 0;
        foreach (var agent in activeAgents)
        {
            // If vehicle is immobilized for more than a brief reaction moment
            if (agent.GetComponent<Rigidbody>() != null && agent.transform.forward.magnitude > 0)
            {
                // Accessing underlying speed metrics
                if (agent.transform.InverseTransformDirection(agent.GetComponent<Rigidbody>().linearVelocity).z <= 0.05f && !Mathf.Approximately(agent.baseSpeed, 0f))
                {
                    stuckCount++;
                }
            }
            else // Backup check directly leveraging internal state timers
            {
                // Using exposed variables inside your submitted code
                System.Type type = agent.GetType();
                var field = type.GetField("timeStuck", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                float val = field != null ? (float)field.GetValue(agent) : 0f;
                if (val > 0.2f) stuckCount++;
            }
        }
        _lastCongestionPct = ((float)stuckCount / activeAgents.Count) * 100f;

        int visitedNodes = 0;
        foreach (var node in trafficNetwork.allNodes)
        {
            if (node != null && node.wasVisited) visitedNodes++;
        }
        _lastUtilizationPct = ((float)visitedNodes / trafficNetwork.allNodes.Count) * 100f;

        // 3. Mathematical Formula Synthesis
        float tScore = _rollingThroughputScore * throughputWeight;
        float cScore = (100f - _lastCongestionPct) * congestionWeight;
        float uScore = _lastUtilizationPct * utilizationWeight;

        finalEfficiencyScore = Mathf.Clamp(tScore + cScore + uScore, 0f, 100f);
    }

    public void ResetNetworkTrackingData()
    {
        foreach (var node in trafficNetwork.allNodes)
        {
            if (node != null) node.wasVisited = false;
        }
        _tripsInCurrentWindow = 0;
        _windowTimer = 0f;
        _rollingThroughputScore = 100f;
    }
}