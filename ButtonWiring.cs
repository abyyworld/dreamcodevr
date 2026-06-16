using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Auto-wires Accept/Reject/Apply/Regenerate buttons to a LiveGenerationClient
/// via code, avoiding the OnClick() Inspector dropdown entirely.
///
/// Setup:
/// 1. Attach this script to the SAME GameObject as LiveGenerationClient
///    (or any GameObject - it will find LiveGenerationClient automatically
///    via GetComponent / FindObjectOfType).
/// 2. Drag your AcceptButton, RejectButton, ApplySelectedButton, and
///    RegenerateUncheckedButton into the fields that appear in the Inspector.
/// 3. Press Play. Clicking the buttons now calls the corresponding
///    LiveGenerationClient methods directly - no OnClick() menu needed.
/// </summary>
public class ButtonWiring : MonoBehaviour
{
    public Button acceptButton;
    public Button rejectButton;
    public Button generateButton;
    public Button applySelectedButton;
    public Button regenerateUncheckedButton;

    private LiveGenerationClient client;

    void Awake()
    {
        client = GetComponent<LiveGenerationClient>();
        if (client == null)
        {
            client = FindObjectOfType<LiveGenerationClient>();
        }

        if (client == null)
        {
            Debug.LogError("ButtonWiring: no LiveGenerationClient found in scene.");
            return;
        }

        if (acceptButton != null)
            acceptButton.onClick.AddListener(client.AcceptCurrent);

        if (rejectButton != null)
            rejectButton.onClick.AddListener(client.RejectCurrent);

        if (generateButton != null)
            generateButton.onClick.AddListener(client.GenerateAndInject);

        if (applySelectedButton != null)
            applySelectedButton.onClick.AddListener(client.ApplySelected);

        if (regenerateUncheckedButton != null)
            regenerateUncheckedButton.onClick.AddListener(client.RegenerateUnchecked);

        Debug.Log("ButtonWiring: all buttons wired to LiveGenerationClient.");
    }
}