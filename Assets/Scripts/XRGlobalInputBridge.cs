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

    public void EnableAllInputs()
    {
        foreach (var mapping in inputMappings) mapping.xrAction.Enable();
    }

    public void DisableAllInputs()
    {
        foreach (var mapping in inputMappings) mapping.xrAction.Disable();
    }
}