using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class XRInputManager : MonoBehaviour
{
    // The System.Serializable class bundles the action and its events for the Inspector
    [Serializable]
    public class XRActionMapping
    {
        [Header("Action Configuration")]
        public string actionName = "New XR action";
        public InputAction xrAction;

        [Header("Events (Press & Release)")]
        public UnityEvent onXRHoldStart;
        public UnityEvent onXRHoldCancel;

        [Header("Special Release (Only the very first time)")]
        public UnityEvent onXRUpOnlyFirstTime;

        // Each action manages its own "First Time" status independently of the others
        [HideInInspector] public bool hasExecutedFirstTime = false;
    }

    [Header("XR Input Configurations")]
    [SerializeField] private List<XRActionMapping> inputMappings = new List<XRActionMapping>();

    private void Awake()
    {
        // We go through all the mappings created in the Inspector and wire them up
        foreach (var mapping in inputMappings)
        {
            // To ensure that we access the correct mapping in the lambda expressions (+= _ =>),
            // we need to create a local copy of the reference (C# safety)
            var currentMapping = mapping;

            currentMapping.xrAction.started += _ =>
            {
                if (currentMapping.onXRHoldStart != null)
                    currentMapping.onXRHoldStart.Invoke();
            };

            currentMapping.xrAction.canceled += _ =>
            {
                // 1. Trigger a normal release event
                if (currentMapping.onXRHoldCancel != null)
                    currentMapping.onXRHoldCancel.Invoke();

                // 2. Execute the one time event if it hasn't already run for this key
                if (!currentMapping.hasExecutedFirstTime)
                {
                    if (currentMapping.onXRUpOnlyFirstTime != null)
                        currentMapping.onXRUpOnlyFirstTime.Invoke();

                    currentMapping.hasExecutedFirstTime = true;
                }
            };
        }
    }

    // Loop to activate all registered actions at once
    private void OnEnable()
    {
        foreach (var mapping in inputMappings)
        {
            mapping.xrAction.Enable();
        }
    }

    // Loop for bulk deactivation
    private void OnDisable()
    {
        foreach (var mapping in inputMappings)
        {
            mapping.xrAction.Disable();
        }
    }

    // Resets the first-time trigger for ALL actions.
    public void ResetAllFirstTimeTriggers()
    {
        foreach (var mapping in inputMappings)
        {
            mapping.hasExecutedFirstTime = false;
        }
    }

    // Resets the first-time trigger for a specific action by its name.
    public void ResetFirstTimeTriggerByName(string nameOfAction)
    {
        foreach (var mapping in inputMappings)
        {
            if (mapping.actionName == nameOfAction)
            {
                mapping.hasExecutedFirstTime = false;
                break;
            }
        }
    }
}