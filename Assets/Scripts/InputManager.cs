using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    [Header("Input Action Configuration")]
    [SerializeField] private string mouseBinding = "<Mouse>/leftButton";

    [Header("Global Mouse Events (Click and hold & Release)")]
    public UnityEvent onMouseHoldStart;
    public UnityEvent onMouseHoldCancel;

    [Header("Mouse up ONLY the very first time")]
    public UnityEvent onMouseUpOnlyFirstTime;

    private InputAction holdAction;
    private bool hasExecutedFirstTime = false;

    private void Awake()
    {
        holdAction = new InputAction(type: InputActionType.Button, binding: mouseBinding);

        holdAction.started += _ =>
        {
            if (onMouseHoldStart != null) onMouseHoldStart.Invoke();
        };

        holdAction.canceled += _ =>
        {
            // 1. Trigger the actual drawing/functional tool first
            if (onMouseHoldCancel != null) onMouseHoldCancel.Invoke();

            // 2. Execute the single-fire event if it hasn't run yet
            if (!hasExecutedFirstTime)
            {
                if (onMouseUpOnlyFirstTime != null) onMouseUpOnlyFirstTime.Invoke();
                hasExecutedFirstTime = true;
            }

        };
    }

    private void OnEnable() => holdAction.Enable();
    private void OnDisable() => holdAction.Disable();
}