using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Reflection;
using System.Collections.Generic;
using Unity.Netcode;

public class UISliderLinker : NetworkBehaviour
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

            if (config.stepSize <= 0)
            {
                config.stepSize = 2;
            }

            // Cache reflection data locally on all instances
            config.targetComponent = config.targetGameObject.GetComponent(config.scriptName);
            if (config.targetComponent == null)
            {
                Debug.LogError($"UISliderLinker [{i}]: Component '{config.scriptName}' not found on target!", this);
                continue;
            }

            config.targetField = config.targetComponent.GetType().GetField(config.variableName, BindingFlags.Public | BindingFlags.Instance);
            if (config.targetField == null)
            {
                config.targetProperty = config.targetComponent.GetType().GetProperty(config.variableName, BindingFlags.Public | BindingFlags.Instance);
            }

            if (config.targetField == null && config.targetProperty == null)
            {
                Debug.LogError($"UISliderLinker [{i}]: Variable/Property '{config.variableName}' not found!", this);
                continue;
            }

            // Setup slider limits
            config.valueSlider.minValue = config.minValue;
            config.valueSlider.maxValue = config.maxValue;
            config.valueSlider.wholeNumbers = true;

            // Explicit cast from float to int
            ApplyLocalValueChanges(config, Mathf.RoundToInt(config.valueSlider.value));

            // Hook up the network event route
            int index = i;
            config.valueSlider.onValueChanged.AddListener((val) => OnSliderMovedByUser(index, val));
        }
    }

    /// <summary>
    /// Triggered when a user interacts with the UI Slider element.
    /// </summary>
    private void OnSliderMovedByUser(int configIndex, float rawValue)
    {
        SliderLinkConfiguration config = sliderLinks[configIndex];
        int roundedValue = Mathf.RoundToInt(rawValue);
        int steps = Mathf.RoundToInt((float)roundedValue / config.stepSize);
        int finalValue = Mathf.Clamp(steps * config.stepSize, config.minValue, config.maxValue);

        // Network check: Forward to network logic if multiplayer session is active
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                SyncSliderValueClientRpc(configIndex, finalValue);
            }
            else if (IsClient)
            {
                SubmitSliderValueServerRpc(configIndex, finalValue);
            }
        }
        else
        {
            // Offline fallback
            ApplyLocalValueChanges(config, finalValue);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SubmitSliderValueServerRpc(int configIndex, int finalValue)
    {
        SyncSliderValueClientRpc(configIndex, finalValue);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void SyncSliderValueClientRpc(int configIndex, int finalValue)
    {
        if (configIndex < 0 || configIndex >= sliderLinks.Count) return;

        SliderLinkConfiguration config = sliderLinks[configIndex];
        ApplyLocalValueChanges(config, finalValue);
    }

    /// <summary>
    /// Applies the synchronized value to the UI components and the reflected target variable.
    /// </summary>
    private void ApplyLocalValueChanges(SliderLinkConfiguration config, int finalValue)
    {
        // Update UI text display
        if (config.valueText != null)
        {
            config.valueText.text = finalValue.ToString();
        }

        // Snap slider handle visually without triggering infinite event loops
        if (Mathf.RoundToInt(config.valueSlider.value) != finalValue)
        {
            int index = sliderLinks.IndexOf(config);

            // Temporarily decouple to prevent loop
            config.valueSlider.onValueChanged.RemoveAllListeners();

            config.valueSlider.value = finalValue;

            // Re-hook listener after value assignment
            config.valueSlider.onValueChanged.AddListener((val) => OnSliderMovedByUser(index, val));
        }

        // Apply synchronized value to the target script via reflection
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

    public override void OnDestroy()
    {
        foreach (var config in sliderLinks)
        {
            if (config?.valueSlider != null)
            {
                config.valueSlider.onValueChanged.RemoveAllListeners();
            }
        }

        // Always invoke the base class clean up when overriding NetworkBehaviour lifecycles
        base.OnDestroy();
    }
}