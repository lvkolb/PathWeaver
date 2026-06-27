using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Reflection;

public class UISliderLinker : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Slider valueSlider;
    [SerializeField] private TextMeshProUGUI valueText;

    [Header("Target Script Settings")]
    [Tooltip("Drag the GameObject that has your target script attached here.")]
    [SerializeField] private GameObject targetGameObject;

    [Tooltip("The exact name of the script/component (e.g., 'GameManager').")]
    [SerializeField] private string scriptName;

    [Tooltip("The exact name of the variable inside that script (must be public).")]
    [SerializeField] private string variableName;

    [Header("Slider Settings")]
    [SerializeField] private int minValue = 0;
    [SerializeField] private int maxValue = 100;
    [Tooltip("The step interval for the slider (e.g., 10 or 20). Must be an even number.")]
    [SerializeField] private int stepSize = 10;

    private Component targetComponent;
    private FieldInfo targetField;
    private PropertyInfo targetProperty;

    private void Start()
    {
        if (valueSlider == null || valueText == null || targetGameObject == null)
        {
            Debug.LogError("UISliderLinker: Please assign all references in the inspector!", this);
            return;
        }

        // Fallback guard to prevent division by zero or negative steps
        if (stepSize <= 0)
        {
            stepSize = 2;
        }

        // Try to find the component by its string name
        targetComponent = targetGameObject.GetComponent(scriptName);
        if (targetComponent == null)
        {
            Debug.LogError($"UISliderLinker: Component '{scriptName}' not found on the target GameObject!", this);
            return;
        }

        // Look for a public field
        targetField = targetComponent.GetType().GetField(variableName, BindingFlags.Public | BindingFlags.Instance);

        // If not found, look for a public property
        if (targetField == null)
        {
            targetProperty = targetComponent.GetType().GetProperty(variableName, BindingFlags.Public | BindingFlags.Instance);
        }

        if (targetField == null && targetProperty == null)
        {
            Debug.LogError($"UISliderLinker: Variable or Property '{variableName}' not found in script '{scriptName}'!", this);
            return;
        }

        // Setup slider limits
        valueSlider.minValue = minValue;
        valueSlider.maxValue = maxValue;
        valueSlider.wholeNumbers = true;

        // Initialize values
        UpdateValue(valueSlider.value);
        valueSlider.onValueChanged.AddListener(UpdateValue);
    }

    private void UpdateValue(float rawValue)
    {
        // Calculate the closest step (e.g., if stepSize is 20 and rawValue is 24, it snaps to 20)
        int roundedValue = Mathf.RoundToInt(rawValue);
        int steps = Mathf.RoundToInt((float)roundedValue / stepSize);
        int finalValue = steps * stepSize;

        // Ensure the value stays within min/max bounds
        finalValue = Mathf.Clamp(finalValue, minValue, maxValue);

        // Update Text
        if (valueText != null)
        {
            valueText.text = finalValue.ToString();
        }

        // Visually snap the slider handle to the stepped value
        if (valueSlider.value != finalValue)
        {
            valueSlider.value = finalValue;
        }

        // Overwrite the value in your target script
        if (targetComponent != null)
        {
            if (targetField != null)
            {
                targetField.SetValue(targetComponent, finalValue);
            }
            else if (targetProperty != null)
            {
                targetProperty.SetValue(targetComponent, finalValue, null);
            }
        }
    }

    private void OnDestroy()
    {
        if (valueSlider != null)
        {
            valueSlider.onValueChanged.RemoveListener(UpdateValue);
        }
    }
}