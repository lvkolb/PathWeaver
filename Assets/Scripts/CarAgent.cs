using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using System.Collections;
using System.Collections.Generic;

public class CarAgent : MonoBehaviour
{
    [Header("Movement Settings")]
    public float baseSpeed = 5f;
    public float stopDuration = 2f;

    [Tooltip("Distance from the center of the spline to the center of the lane")]
    public float laneOffset = 0.1f;

    [Header("Collision Avoidance")]
    public float detectionDistance = 3f;
    public float minStoppingDistance = 0.8f;
    public float sphereRadius = 0.5f; // How "fat" the detection ray is
    public LayerMask vehicleLayer;
    public LayerMask jammerLayer;
    private float currentSpeed;

    [Header("References")]
    public TrafficNetwork network;
    public SplineContainer splineContainer;

    [Header("Navigation Goals")]
    public TrafficNode homeNode;
    public TrafficNode workNode;
    public bool headingToWork = true;

    [Header("Audio Settings (Only for Collision/Braking)")]
    [Tooltip("The AudioSource component used to play the braking/stopping sounds.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("List of audio clips. The script will pick a random one when a car blocks the road.")]
    [SerializeField] private List<AudioClip> stopSounds = new List<AudioClip>();
    [Tooltip("How many seconds the car must be fully stopped before the sound triggers for the FIRST time.")]
    [SerializeField] private float audioDelayThreshold = 2f;
    [Tooltip("How many seconds to wait before repeating the sound if the car remains completely stopped.")]
    [SerializeField] private float audioRepeatInterval = 2f;
    [Tooltip("Shows in real-time how long the car has been completely standing still due to a roadblock.")]
    [SerializeField] private float timeStuck = 0f;

    private List<TrafficNode> currentPath = new List<TrafficNode>();
    public TrafficNode currentTarget;

    // Internal Spline State tracking
    private bool useSpline = false;
    private float travelT = 0f;
    private int splineIdx = -1;
    private float tStart, tEnd;
    private bool isWaiting = false;

    // Track audio state to manage repeating intervals properly
    private bool isSoundActive = false;
    private float soundRepeatTimer = 0f;

    public System.Action OnTripCycleCompleted;
    public void InitializeAgent(TrafficNode start, TrafficNode destination)
    {
        homeNode = start;
        workNode = destination;

        if (network == null) network = Object.FindAnyObjectByType<TrafficNetwork>();
        if (splineContainer == null && network != null) splineContainer = network.splineContainer;

        // Automatically assign AudioSource if left empty in the Inspector
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        // --- RANDOM AUDIO INITIALIZATION ONCE ---
        // Pick a random sound from the list right at initialization and pre-assign it to the AudioSource
        if (audioSource != null && stopSounds != null && stopSounds.Count > 0)
        {
            int randomIndex = UnityEngine.Random.Range(0, stopSounds.Count);
            audioSource.clip = stopSounds[randomIndex];

            // Turn off the loop so we can control the repetition precisely!
            audioSource.loop = false;
        }

        transform.position = start.transform.position;
        headingToWork = true;
        isWaiting = false;
        timeStuck = 0f;
        soundRepeatTimer = 0f;
        isSoundActive = false;

        RecalculatePath();
    }

    void Update()
    {
        // SAFETY FIRST: If the car is naturally waiting at its destination or has no target,
        // it is NOT in a traffic jam collision. Stop the collision sound immediately!
        if (isWaiting || currentTarget == null)
        {
            timeStuck = 0f;
            soundRepeatTimer = 0f;
            HandleStopAudio(false);
            return;
        }

        CalculateDynamicSpeed();

        // THE ULTIMATE BACKUP FIX:
        // If the collision script somehow glitched or the car is moving faster than light,
        // but the sound engine is still trapped playing, force-kill the audio right here!
        if (currentSpeed > 0.01f && isSoundActive)
        {
            timeStuck = 0f;
            soundRepeatTimer = 0f;
            HandleStopAudio(false);
        }

        if (useSpline) MoveAlongSpline();
        else MoveDirectly();
    }

    /// <summary>
    /// Safely manages state transitions to play or stop the braking/idle sound.
    /// </summary>
    private void HandleStopAudio(bool shouldPlay)
    {
        if (audioSource == null || audioSource.clip == null) return;

        if (shouldPlay)
        {
            if (!isSoundActive)
            {
                isSoundActive = true;
                audioSource.Play();
                // Set the timer directly to the interval so that the next sound plays after X seconds
                soundRepeatTimer = audioRepeatInterval;
            }
            else
            {
                // If the sound is to remain active, we count down the time until we play it again
                soundRepeatTimer -= Time.deltaTime;
                if (soundRepeatTimer <= 0f)
                {
                    // Play again (even if the previous sound is still playing or has already finished)
                    audioSource.Play();
                    soundRepeatTimer = audioRepeatInterval;
                }
            }
        }
        else
        {
            if (isSoundActive)
            {
                isSoundActive = false;
                soundRepeatTimer = 0f;
                audioSource.Stop();
            }
        }
    }

    public void RemapFromSnapshot(TrafficNetwork net,
                               Vector3 homePos, Vector3 workPos,
                               Vector3 lastTargetPos, bool hadTarget,
                               bool wasHeadingToWork)
    {
        headingToWork = wasHeadingToWork;

        homeNode = net.FindNearbyNode(homePos);
        workNode = net.FindNearbyNode(workPos);
        currentTarget = hadTarget ? net.FindNearbyNode(lastTargetPos)
                                  : (headingToWork ? homeNode : workNode);

        currentPath.Clear();
        useSpline = false;
        isWaiting = false;
        timeStuck = 0f;
        soundRepeatTimer = 0f;
        isSoundActive = false;
        RecalculatePath();
    }

    private void CalculateDynamicSpeed()
    {
        currentSpeed = baseSpeed;
        bool isYieldingAtIntersection = false;
        if (currentTarget != null && currentTarget.nodeType == TrafficNode.NodeType.Intersection)
        {
            // We are at the PreIntersection, trying to enter the Intersection. Is it free?
            if (currentTarget.occupyingVehicle == null)
            {
                // It is free! Claim it so nobody else drives in.
                currentTarget.occupyingVehicle = this;
            }
            else if (currentTarget.occupyingVehicle != this)
            {
                // Someone else is in the intersection. We must yield and wait.
                isYieldingAtIntersection = true;
            }
        }
        // Elevate the ray so it shoots out of the windshield/grill, not the floor
        Vector3 rayOrigin = transform.position + (Vector3.up * 0.05f);

        // COMBINE LAYERS: Search for both vehicles AND jammers in the same physical check
        LayerMask combinedDetectionMask = vehicleLayer | jammerLayer;

        // Get EVERYTHING the sphere hits in front of the car
        RaycastHit[] hits = Physics.SphereCastAll(rayOrigin, sphereRadius, transform.forward, detectionDistance, combinedDetectionMask);

        float closestValidDistance = float.MaxValue;
        bool carDetectedInFront = false;

        foreach (RaycastHit hit in hits)
        {
            // 1. Skip self
            if (hit.collider.transform.IsChildOf(this.transform) || hit.collider.transform == this.transform) continue;

            // 2. Spatial check: Is the object physically in front of us?
            Vector3 toOther = hit.collider.transform.position - transform.position;
            float dotSpatial = Vector3.Dot(transform.forward, toOther.normalized);
            if (dotSpatial < 0.5f) continue; // Skip if it's behind or too far to the side

            // Check if the hit object is on the Jammer layer
            bool isJammer = ((1 << hit.collider.gameObject.layer) & jammerLayer) != 0;

            // 3. Direction check (ONLY for other vehicles, Jammers always block!)
            if (!isJammer)
            {
                // We look at the other vehicle's forward vector compared to ours.
                Vector3 otherForward = hit.collider.transform.forward;
                float dotHeading = Vector3.Dot(transform.forward, otherForward);

                // If dotHeading is less than 0.3, they are either oncoming (negative) 
                // or perpendicular/crossing (near 0). We ignore oncoming traffic.
                if (dotHeading < 0.3f) continue;
            }

            // If we reach this point, it's either a Jammer in front of us, or a vehicle driving the same way
            if (hit.distance < closestValidDistance)
            {
                closestValidDistance = hit.distance;
                carDetectedInFront = true;
            }
        }

        if (isYieldingAtIntersection)
        {
            // Hard stop. Do not play the crash audio, just idle at the intersection line.
            currentSpeed = 0f;
            timeStuck = 0f;
            soundRepeatTimer = 0f;
            HandleStopAudio(false);
        }
        else if (carDetectedInFront)
        {
            // Map the distance to a speed multiplier (0 when touching min stopping distance, 1 when at edge of detection)
            float speedMultiplier = Mathf.InverseLerp(minStoppingDistance, detectionDistance, closestValidDistance);
            currentSpeed = baseSpeed * speedMultiplier;

            // Draw a RED line in the Scene view to show it is braking
            Debug.DrawRay(rayOrigin, transform.forward * closestValidDistance, Color.red);

            // --- COLLISION AUDIO TRIGGER ---
            // If a car is detected and speed drops to zero (or near zero), play the sound
            if (currentSpeed <= 0.01f)
            {
                // Accumulate the time the car has spent standing still
                timeStuck += Time.deltaTime;

                // Trigger playing the unique pre-initialized stop sound only if the threshold has been reached
                if (timeStuck >= audioDelayThreshold)
                {
                    HandleStopAudio(true);
                }
            }
            else
            {
                // Reset timer and turn off sound if the car is just braking but still crawling forward
                timeStuck = 0f;
                soundRepeatTimer = 0f;
                HandleStopAudio(false);
            }
        }
        else
        {
            // Draw a GREEN line in the Scene view to show clear roads
            Debug.DrawRay(rayOrigin, transform.forward * detectionDistance, Color.green);

            // No car in front -> clean roads -> reset timer and turn off sound immediately
            timeStuck = 0f;
            soundRepeatTimer = 0f;
            HandleStopAudio(false);
        }
    }

    private void MoveAlongSpline()
    {
        if (splineContainer == null) return;

        float length = splineContainer.Splines[splineIdx].GetLength();
        float segmentLen = Mathf.Abs(tEnd - tStart) * length;

        if (segmentLen < 0.1f) { Advance(); return; }

        travelT += (currentSpeed * Time.deltaTime) / segmentLen;
        float worldT = Mathf.Lerp(tStart, tEnd, Mathf.Clamp01(travelT));

        splineContainer.Evaluate(splineIdx, worldT, out float3 pos, out float3 fwd, out _);

        Vector3 forward = splineContainer.transform.TransformDirection((Vector3)fwd);
        if (tEnd < tStart) forward = -forward;

        ApplyPositionAndRotation(splineContainer.transform.TransformPoint((Vector3)pos), forward);

        if (travelT >= 1f) Advance();
    }

    private void MoveDirectly()
    {
        Vector3 dir = (currentTarget.transform.position - transform.position).normalized;
        Vector3 targetPos = currentTarget.transform.position;

        transform.position = Vector3.MoveTowards(transform.position, targetPos + (Vector3.Cross(Vector3.up, dir) * laneOffset), currentSpeed * Time.deltaTime);

        if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);
        if (Vector3.Distance(transform.position, targetPos) < 0.3f) Advance();
    }

    private void ApplyPositionAndRotation(Vector3 centerPos, Vector3 forward)
    {
        // Right-hand traffic offset
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        transform.position = centerPos + (right * laneOffset);
        if (forward.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(forward);
    }

    /// <summary>
    /// Called by VehicleManager after TrafficNetwork.RebuildGraph() destroys and recreates all nodes.
    /// Remaps stale node references to the nearest live equivalents, then recalculates the path.
    /// </summary>
    public void RemapAfterRebuild(TrafficNetwork net)
    {
        // Cache world positions BEFORE references go stale
        // (Destroyed Unity objects still return their last position)
        if (homeNode != null)
            homeNode = net.FindNearbyNode(homeNode.transform.position);

        if (workNode != null)
            workNode = net.FindNearbyNode(workNode.transform.position);

        // currentTarget may be destroyed — snap to nearest live node from its last known pos
        if (currentTarget != null)
            currentTarget = net.FindNearbyNode(currentTarget.transform.position);
        else
            currentTarget = headingToWork ? homeNode : workNode;
        if (currentTarget != null && currentTarget.occupyingVehicle == this)
        {
            currentTarget.occupyingVehicle = null;
        }
        // Clear the now-invalid path and recalculate fresh
        currentPath.Clear();
        useSpline = false;
        isWaiting = false;
        timeStuck = 0f;
        soundRepeatTimer = 0f;
        isSoundActive = false;
        RecalculatePath();
    }

    private void Advance()
    {
        if (currentPath.Count > 0)
        {
            TrafficNode from = currentTarget != null ? currentTarget : (headingToWork ? homeNode : workNode);

            if (from != null && from.nodeType == TrafficNode.NodeType.Intersection)
            {
                if (from.occupyingVehicle == this)
                {
                    from.occupyingVehicle = null;
                }
            }
            currentTarget = currentPath[0];
            if (currentTarget != null) currentTarget.wasVisited = true;
            currentPath.RemoveAt(0);

            if (from.splineIndex == currentTarget.splineIndex && from.splineIndex != -1)
            {
                splineIdx = from.splineIndex;
                tStart = from.tValue;
                tEnd = currentTarget.tValue;
                travelT = 0f;
                useSpline = true;
            }
            else useSpline = false;
        }
        else
        {
            OnTripCycleCompleted?.Invoke();
            StartCoroutine(WaitAndReturn());
        }
    }

    private IEnumerator WaitAndReturn()
    {
        isWaiting = true;
        yield return new WaitForSeconds(stopDuration);

        headingToWork = !headingToWork;
        isWaiting = false;
        timeStuck = 0f;
        soundRepeatTimer = 0f;
        isSoundActive = false;
        RecalculatePath();
    }

    public void RecalculatePath()
    {
        TrafficNode start = currentTarget != null ? currentTarget : (headingToWork ? homeNode : workNode);
        TrafficNode destination = headingToWork ? workNode : homeNode;

        currentPath = FindShortestPath(start, destination);
        if (currentPath.Count > 0) Advance();
    }

    private List<TrafficNode> FindShortestPath(TrafficNode start, TrafficNode end)
    {
        var dist = new Dictionary<TrafficNode, float>();
        var prev = new Dictionary<TrafficNode, TrafficNode>();
        var queue = new List<TrafficNode> { start };
        dist[start] = 0;

        while (queue.Count > 0)
        {
            queue.Sort((a, b) => dist[a].CompareTo(dist[b]));
            TrafficNode curr = queue[0];
            queue.RemoveAt(0);

            if (curr == end) break;

            foreach (var next in curr.outgoing)
            {
                if (next == null) continue;
                float d = Vector3.Distance(curr.transform.position, next.transform.position);
                if (!dist.ContainsKey(next) || dist[curr] + d < dist[next])
                {
                    dist[next] = dist[curr] + d;
                    prev[next] = curr;
                    if (!queue.Contains(next)) queue.Add(next);
                }
            }
        }

        var path = new List<TrafficNode>();
        var t = end;
        while (t != null && t != start && prev.ContainsKey(t))
        {
            path.Add(t);
            t = prev[t];
        }
        path.Reverse();
        return path;
    }
}