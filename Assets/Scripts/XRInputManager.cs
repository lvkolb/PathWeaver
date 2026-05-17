using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem; // Wichtig für das neue Input System

public class XRInputManager : MonoBehaviour
{
    [Header("XR Input Action")]
    [SerializeField] private InputAction xrAction;

    [Header("Global XR Events (Press and hold & Release)")]
    public UnityEvent onXRHoldStart;
    public UnityEvent onXRHoldCancel;

    [Header("XR Release ONLY the very first time")]
    public UnityEvent onXRUpOnlyFirstTime;

    private bool hasExecutedFirstTime = false;

    private void Awake()
    {

        // .started corresponds to the first frame of the press
        xrAction.started += _ =>
        {
            if (onXRHoldStart != null) onXRHoldStart.Invoke();
        };

        // .canceled corresponds to releasing the button
        xrAction.canceled += _ =>
        {
            // 1. First, trigger the normal release event
            if (onXRHoldCancel != null) onXRHoldCancel.Invoke();

            // 2. Run the only one time event if it hasn't run yet
            if (!hasExecutedFirstTime)
            {
                if (onXRUpOnlyFirstTime != null) onXRUpOnlyFirstTime.Invoke();
                hasExecutedFirstTime = true;
            }
        };
    }

    // Important: The action created in the Inspector must be enabled or disabled
    private void OnEnable() => xrAction.Enable();
    private void OnDisable() => xrAction.Disable();

    public void ResetFirstTimeTrigger()
    {
        hasExecutedFirstTime = false;
    }
}