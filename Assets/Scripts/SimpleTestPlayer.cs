using UnityEngine;
using UnityEngine.InputSystem;

public class KeyboardFlightController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;

    // References for the dynamically created input actions
    private InputAction _horizontalMoveAction;
    private InputAction _verticalMoveAction;
    private InputAction _flightAction;

    private void Awake()
    {
        // 1. Initialize Horizontal Movement (Left/Right) using J and L
        _horizontalMoveAction = new InputAction("Horizontal", binding: "<Keyboard>/l");
        _horizontalMoveAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/l")
            .With("Negative", "<Keyboard>/j");

        // 2. Initialize Vertical Movement (Forward/Backward) using I and K
        _verticalMoveAction = new InputAction("Vertical", binding: "<Keyboard>/i");
        _verticalMoveAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/i")
            .With("Negative", "<Keyboard>/k");

        // 3. Initialize Flight Movement (Up/Down) using U and O
        _flightAction = new InputAction("Flight", binding: "<Keyboard>/u");
        _flightAction.AddCompositeBinding("1DAxis")
            .With("Positive", "<Keyboard>/u")
            .With("Negative", "<Keyboard>/o");
    }

    private void OnEnable()
    {
        // Enable all actions when the object becomes active
        _horizontalMoveAction.Enable();
        _verticalMoveAction.Enable();
        _flightAction.Enable();
    }

    private void OnDisable()
    {
        // Disable actions to prevent memory leaks or ghost inputs
        _horizontalMoveAction.Disable();
        _verticalMoveAction.Disable();
        _flightAction.Disable();
    }

    private void Update()
    {
        // Read the current values from the keyboard input (-1 to 1)
        float xInput = _horizontalMoveAction.ReadValue<float>();
        float zInput = _verticalMoveAction.ReadValue<float>();
        float yInput = _flightAction.ReadValue<float>();

        // Calculate the movement vector relative to the object's orientation
        Vector3 direction = (transform.right * xInput) + (transform.forward * zInput) + (transform.up * yInput);

        // Apply the movement over time
        transform.position += direction.normalized * (moveSpeed * Time.deltaTime);
    }
}