using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class XRInputManager : NetworkBehaviour
{
    [Serializable]
    public class LocalPrefabMapping
    {
        [Header("Link to Scene Bridge Name")]
        [Tooltip("The exact ActionName that you defined in the GlobalInputBridge within the scene.")]
        public string targetBridgeActionName = "YButtonAction";

        [Header("Local Prefab Events")]
        public UnityEvent onLocalHoldStart;
        public UnityEvent onLocalHoldCancel;
        public UnityEvent onLocalClick;
        public UnityEvent onLocalUpOnlyFirstTime;
    }

    [Header("Local Prefab Input Connections")]
    [Tooltip("Here you can link functions that are attached to this prefab (e.g. PlayerManager).")]
    [SerializeField] private List<LocalPrefabMapping> localMappings = new List<LocalPrefabMapping>();

    private void Start()
    {
        // If the bridge exists in the scene, we link the hardware events
        if (GlobalInputBridge.Instance != null)
        {
            foreach (var sceneMapping in GlobalInputBridge.Instance.inputMappings)
            {
                var currentSceneMapping = sceneMapping;

                // 1. Hardware pressed (started)
                currentSceneMapping.xrAction.started += _ =>
                {
                    if (!IsOwner) return;

                    // A: Executes the function in the scene (if defined there)
                    currentSceneMapping.onXRHoldStart?.Invoke();

                    // B: Runs the relevant functions on your local prefab
                    TriggerLocalEvents(currentSceneMapping.actionName, "start");
                };

                // 2. Hardware click/trigger (performed)
                currentSceneMapping.xrAction.performed += _ =>
                {
                    if (!IsOwner) return;

                    // A: Executes the functions in the scene
                    currentSceneMapping.onXRClick?.Invoke();

                    // B: Runs the relevant functions on your local prefab
                    TriggerLocalEvents(currentSceneMapping.actionName, "click");
                };

                // 3. Hardware released (cancelled)
                currentSceneMapping.xrAction.canceled += _ =>
                {
                    if (!IsOwner) return;

                    // A: Executes the functions in the scene
                    currentSceneMapping.onXRHoldCancel?.Invoke();

                    if (!currentSceneMapping.hasExecutedFirstTime)
                    {
                        currentSceneMapping.onXRUpOnlyFirstTime?.Invoke();
                        currentSceneMapping.hasExecutedFirstTime = true;
                    }

                    // B: Runs the relevant functions on your local prefab
                    TriggerLocalEvents(currentSceneMapping.actionName, "cancel");
                };
            }
        }
    }

    // Hilfsmethode, die deine lokale Prefab-Liste nach dem passenden Namen durchsucht
    private void TriggerLocalEvents(string actionName, string eventType)
    {
        // Searches for an entry in your prefab list with the same name as the action from the scene
        var localMatch = localMappings.Find(m => m.targetBridgeActionName == actionName);

        if (localMatch != null)
        {
            if (eventType == "start")
            {
                localMatch.onLocalHoldStart?.Invoke();
            }
            else if (eventType == "click")
            {
                localMatch.onLocalClick?.Invoke();
            }
            else if (eventType == "cancel")
            {
                localMatch.onLocalHoldCancel?.Invoke();

                // As we are mirroring the scene’s ‘first-time’ status, we use the state of the scene bridge
                var sceneMatch = GlobalInputBridge.Instance.inputMappings.Find(m => m.actionName == actionName);
                if (sceneMatch != null && sceneMatch.hasExecutedFirstTime)
                {
                    // We only trigger the event if it has been cancelled for the very first time in the scene
                    // Unity’s system handles this. If you want to track it independently,
                    // ‘hasExecutedFirstTime’ would need to be included in LocalPrefabMapping.
                    localMatch.onLocalUpOnlyFirstTime?.Invoke();
                }
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner && GlobalInputBridge.Instance != null)
        {
            GlobalInputBridge.Instance.EnableAllInputs();
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && GlobalInputBridge.Instance != null)
        {
            GlobalInputBridge.Instance.DisableAllInputs();
        }
    }
}