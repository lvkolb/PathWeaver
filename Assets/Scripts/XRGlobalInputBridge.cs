using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class GlobalInputBridge : MonoBehaviour
{
    // The bridge itself so the prefab can find it instantly
    public static GlobalInputBridge Instance { get; private set; }

    [System.Serializable]
    public class SceneActionMapping
    {
        [Header("Action Configuration")]
        public string actionName = "New XR action";
        public InputAction xrAction;

        [Header("Events (Press & Release)")]
        public UnityEvent onXRHoldStart;
        public UnityEvent onXRHoldCancel;

        [Header("Click / Trigger")]
        public UnityEvent onXRClick;

        [Header("Special Release")]
        public UnityEvent onXRUpOnlyFirstTime;

        [HideInInspector] public bool hasExecutedFirstTime = false;
    }

    [Header("Configure Scene Inputs & Target Functions Here")]
    public List<SceneActionMapping> inputMappings = new List<SceneActionMapping>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        DisableAllInputs();
    }

    private void OnEnable()
    {
        RegisterEvents();
    }

    private void OnDisable()
    {
        UnregisterEvents();
    }

    private void RegisterEvents()
    {
        foreach (var mapping in inputMappings)
        {
            if (mapping.xrAction == null) continue;

            // Bind the Unity Input System callbacks to our UnityEvents
            mapping.xrAction.started += ctx => mapping.onXRHoldStart?.Invoke();
            mapping.xrAction.performed += ctx => HandlePerformed(mapping);
            mapping.xrAction.canceled += ctx => mapping.onXRHoldCancel?.Invoke();
        }
    }

    private void UnregisterEvents()
    {
        foreach (var mapping in inputMappings)
        {
            if (mapping.xrAction == null) continue;

            mapping.xrAction.started -= ctx => mapping.onXRHoldStart?.Invoke();
            mapping.xrAction.performed -= ctx => HandlePerformed(mapping);
            mapping.xrAction.canceled -= ctx => mapping.onXRHoldCancel?.Invoke();
        }
    }

    private void HandlePerformed(SceneActionMapping mapping)
    {
        // Trigger the standard single-click action
        mapping.onXRClick?.Invoke();

        // Handle the special first-time release logic if required
        if (!mapping.hasExecutedFirstTime)
        {
            mapping.onXRUpOnlyFirstTime?.Invoke();
            mapping.hasExecutedFirstTime = true;
        }
    }

    public void EnableAllInputs()
    {
        foreach (var mapping in inputMappings) mapping.xrAction.Enable();
    }

    public void DisableAllInputs()
    {
        foreach (var mapping in inputMappings) mapping.xrAction.Disable();
    }
}