using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Sends an instruction + scene description to the local Python generation
/// server, then feeds the returned code into RuntimeCodeInjector.
/// Displays the result (assumptions, or any error) in a UI Text element.
///
/// Setup:
/// 1. Run generation_server.py on your machine (python generation_server.py)
/// 2. Create a UI Canvas with a Text element (GameObject -> UI -> Canvas,
///    then GameObject -> UI -> Text), and drag that Text into the
///    "Assumptions Text" field on this component.
/// 3. Attach this script to any GameObject
/// 4. Right-click the component header -> "Load Default Scene Description"
///    (or fill in sceneDescription manually to match your actual scene)
/// 5. Set "instruction" to whatever you want generated
/// 6. Press Play, then right-click the component header -> "Generate And Inject"
/// </summary>
public class LiveGenerationClient : MonoBehaviour
{
    [Tooltip("URL of the local Python generation server")]
    public string serverUrl = "http://127.0.0.1:5005/generate";

    [Tooltip("Natural-language instruction to send to the generation server")]
    [TextArea]
    public string instruction = "Make this cube spin continuously";

    [Header("UI")]
    [Tooltip("UI Text element used to display assumptions / errors to the user")]
    public Text assumptionsText;

    [Tooltip("Optional: InputField for typing instructions at runtime. If set, its text overrides the 'instruction' field when Generate is pressed.")]
    public InputField instructionInput;

    private Component lastInjectedComponent;
    private GameObject lastInjectedTarget;

    [System.Serializable]
    public class SceneObjectDescription
    {
        public string name;
        public string[] components;
        public string[] tags;
    }

    [Tooltip("Describes each GameObject the LLM can reference/target")]
    public SceneObjectDescription[] sceneDescription;

    [System.Serializable]
    private class GenerationRequest
    {
        public SceneObjectDescription[] scene;
        public string instruction;
    }

    [System.Serializable]
    private class GenerationResponse
    {
        public string code;
        public string target_object;
        public string[] assumptions;
        public string error;
    }

    [ContextMenu("Load Default Scene Description")]
    public void LoadDefaultSceneDescription()
    {
        sceneDescription = new[]
        {
            new SceneObjectDescription
            {
                name = "Cube_01",
                components = new[] { "Transform", "MeshRenderer", "BoxCollider", "Rigidbody" },
                tags = new[] { "grabbable" }
            },
            new SceneObjectDescription
            {
                name = "Player",
                components = new[] { "XROrigin", "Camera" },
                tags = new string[0]
            }
        };
        Debug.Log("Loaded default scene description (Cube_01, Player).");
    }

    [ContextMenu("Generate And Inject")]
    public void GenerateAndInject()
    {
        StartCoroutine(GenerateAndInjectCoroutine());
    }

    private void DisplayMessage(string message)
    {
        if (assumptionsText != null)
            assumptionsText.text = message;
        Debug.Log(message);
    }

    private void DisplayError(string message)
    {
        if (assumptionsText != null)
            assumptionsText.text = "ERROR:\n" + message;
        Debug.LogError(message);
    }

    private IEnumerator GenerateAndInjectCoroutine()
    {
        if (instructionInput != null && !string.IsNullOrWhiteSpace(instructionInput.text))
        {
            instruction = instructionInput.text;
        }

        DisplayMessage($"Requesting generation for:\n\"{instruction}\"...");

        var requestBody = new GenerationRequest
        {
            scene = sceneDescription,
            instruction = instruction
        };
        string json = JsonUtility.ToJson(requestBody);

        var bodyRaw = Encoding.UTF8.GetBytes(json);
        using var webRequest = new UnityWebRequest(serverUrl, "POST");
        webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
        webRequest.downloadHandler = new DownloadHandlerBuffer();
        webRequest.SetRequestHeader("Content-Type", "application/json");

        yield return webRequest.SendWebRequest();

        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            DisplayError($"Generation request failed: {webRequest.error}\n{webRequest.downloadHandler.text}");
            yield break;
        }

        GenerationResponse response;
        try
        {
            response = JsonUtility.FromJson<GenerationResponse>(webRequest.downloadHandler.text);
        }
        catch (Exception e)
        {
            DisplayError($"Failed to parse response: {e.Message}\nRaw: {webRequest.downloadHandler.text}");
            yield break;
        }

        if (!string.IsNullOrEmpty(response.error))
        {
            DisplayError($"Generation server error: {response.error}");
            yield break;
        }

        GameObject target = GameObject.Find(response.target_object);
        if (target == null)
        {
            DisplayError($"Target object '{response.target_object}' not found in scene.");
            yield break;
        }

        var injectionResult = RuntimeCodeInjector.InjectScript(response.code, target);

        if (!injectionResult.CompileSucceeded)
        {
            DisplayError("Generated code FAILED to compile:\n" + string.Join("\n", injectionResult.Diagnostics));
            yield break;
        }

        if (!injectionResult.InjectionSucceeded)
        {
            DisplayError("Generated code compiled but FAILED to inject:\n" + string.Join("\n", injectionResult.Diagnostics));
            yield break;
        }

        lastInjectedComponent = injectionResult.InjectedComponent;
        lastInjectedTarget = target;

        string assumptionsList = string.Join("\n - ", response.assumptions);
        string summary = $"Applied to: {target.name}\n\nThe AI assumed:\n - {assumptionsList}\n\n[Accept] to keep, [Reject] to undo.";
        DisplayMessage(summary);
    }

    /// <summary>
    /// Called by the "Accept" UI button. Confirms the injected behavior;
    /// nothing to undo, just clears tracking and updates the panel.
    /// </summary>
    public void AcceptCurrent()
    {
        if (lastInjectedComponent == null)
        {
            DisplayMessage("Nothing to accept.");
            return;
        }

        DisplayMessage($"Accepted - {lastInjectedComponent.GetType().Name} kept on {lastInjectedTarget.name}.");
        lastInjectedComponent = null;
        lastInjectedTarget = null;
    }

    /// <summary>
    /// Called by the "Reject" UI button. Removes the most recently
    /// injected component - the core error-recovery action.
    /// </summary>
    public void RejectCurrent()
    {
        if (lastInjectedComponent == null)
        {
            DisplayMessage("Nothing to reject.");
            return;
        }

        string componentName = lastInjectedComponent.GetType().Name;
        string targetName = lastInjectedTarget.name;

        RuntimeCodeInjector.RemoveInjectedComponent(lastInjectedComponent);

        DisplayMessage($"Reverted - removed {componentName} from {targetName}.");
        lastInjectedComponent = null;
        lastInjectedTarget = null;
    }
}