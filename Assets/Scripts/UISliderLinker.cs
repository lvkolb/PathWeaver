using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Reflection;
using System.Collections.Generic;

public class UISliderLinker : MonoBehaviour
{
    [System.Serializable]
    public class SliderLinkConfiguration
    {
        [Header("UI References")]
        public Slider valueSlider;
        public TextMeshProUGUI valueText;

        [Header("Target Script Settings")]
        [Tooltip("Drag the GameObject that has your target script attached here.")]
        public GameObject targetGameObject;

        [Tooltip("The exact name of the script/component (e.g., 'GameManager').")]
        public string scriptName;

        [Tooltip("The exact name of the variable inside that script (must be public).")]
        public string variableName;

        [Header("Slider Settings")]
        public int minValue = 0;
        public int maxValue = 100;
        [Tooltip("The step interval for the slider (e.g., 10 or 20). Must be an even number.")]
        public int stepSize = 10;

        // Runtime cached reflection data
        [HideInInspector] public Component targetComponent;
        [HideInInspector] public FieldInfo targetField;
        [HideInInspector] public PropertyInfo targetProperty;
    }

    [Header("Slider Configurations")]
    [SerializeField] private List<SliderLinkConfiguration> sliderLinks = new List<SliderLinkConfiguration>();

    private void Start()
    {
        for (int i = 0; i < sliderLinks.Count; i++)
        {
            SliderLinkConfiguration config = sliderLinks[i];

            if (config.valueSlider == null || config.valueText == null || config.targetGameObject == null)
            {
                Debug.LogError($"UISliderLinker: Please assign all references for element at index {i}!", this);
                continue;
            }

            // Fallback guard to prevent division by zero or negative steps
            if (config.stepSize <= 0)
            {
                config.stepSize = 2;
            }

            // Try to find the component by its string name
            config.targetComponent = config.targetGameObject.GetComponent(config.scriptName);
            if (config.targetComponent == null)
            {
                Debug.LogError($"UISliderLinker [{i}]: Component '{config.scriptName}' not found on the target GameObject!", this);
                continue;
            }

            // Look for a public field
            config.targetField = config.targetComponent.GetType().GetField(config.variableName, BindingFlags.Public | BindingFlags.Instance);

            // If not found, look for a public property
            if (config.targetField == null)
            {
                config.targetProperty = config.targetComponent.GetType().GetProperty(config.variableName, BindingFlags.Public | BindingFlags.Instance);
            }

            if (config.targetField == null && config.targetProperty == null)
            {
                Debug.LogError($"UISliderLinker [{i}]: Variable or Property '{config.variableName}' not found in script '{config.scriptName}'!", this);
                continue;
            }

            // Setup slider limits
            config.valueSlider.minValue = config.minValue;
            config.valueSlider.maxValue = config.maxValue;
            config.valueSlider.wholeNumbers = true;

            // Initialize values
            UpdateValue(config, config.valueSlider.value);

            // We utilize a local copy variable capturing the index context for the delegate registration
            int index = i;
            config.valueSlider.onValueChanged.AddListener((val) => UpdateValue(sliderLinks[index], val));
        }
    }

    private void UpdateValue(SliderLinkConfiguration config, float rawValue)
    {
        // Calculate the closest step (e.g., if stepSize is 20 and rawValue is 24, it snaps to 20)
        int roundedValue = Mathf.RoundToInt(rawValue);
        int steps = Mathf.RoundToInt((float)roundedValue / config.stepSize);
        int finalValue = steps * config.stepSize;

        // Ensure the value stays within min/max bounds
        finalValue = Mathf.Clamp(finalValue, config.minValue, config.maxValue);

        // Update Text
        if (config.valueText != null)
        {
            config.valueText.text = finalValue.ToString();
        }

        // Visually snap the slider handle to the stepped value
        if (config.valueSlider.value != finalValue)
        {
            config.valueSlider.value = finalValue;
        }

        // Overwrite the value in your target script
        if (config.targetComponent != null)
        {
            if (config.targetField != null)
            {
                config.targetField.SetValue(config.targetComponent, finalValue);
            }
            else if (config.targetProperty != null)
            {
                config.targetProperty.SetValue(config.targetComponent, finalValue, null);
            }
        }
    }

    private void OnDestroy()
    {
        foreach (var config in sliderLinks)
        {
            if (config?.valueSlider != null)
            {
                config.valueSlider.onValueChanged.RemoveAllListeners();
            }
        }
    }
}