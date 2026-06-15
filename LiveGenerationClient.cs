using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LiveGenerationClient : MonoBehaviour
{
    public string serverUrl = "http://127.0.0.1:5005/generate";

    [TextArea]
    public string instruction = "Make this cube spin continuously";

    [Header("UI")]
    public Text assumptionsText;
    public InputField instructionInput;

    [Header("Logging")]
    public string logFilePath = "~/Desktop/dreamcodevr_study_log.csv";

    private Component lastInjectedComponent;
    private GameObject lastInjectedTarget;
    private Vector3 lastTargetPosition;
    private Quaternion lastTargetRotation;
    private Vector3 lastTargetScale;

    private string currentInstruction;
    private string currentCode;
    private string currentAssumptions;
    private bool currentCompileOk;
    private bool currentInjectOk;
    private float trialStartTime;

    [System.Serializable]
    public class SceneObjectDescription
    {
        public string name;
        public string[] components;
        public string[] tags;
    }

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

    private string ResolvedLogPath()
    {
        string path = logFilePath;
        if (path.StartsWith("~"))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = home + path.Substring(1);
        }
        return path;
    }

    private void EnsureLogFileExists()
    {
        string path = ResolvedLogPath();
        if (!File.Exists(path))
        {
            string header = "timestamp,instruction,target_object,assumptions,compile_ok,inject_ok,decision,decision_time_seconds,code\n";
            File.WriteAllText(path, header);
        }
    }

    private string CsvEscape(string field)
    {
        if (field == null) return "";
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
        {
            field = field.Replace("\"", "\"\"");
            return "\"" + field + "\"";
        }
        return field;
    }

    private void LogTrial(string targetObjectName, string decision, float decisionTimeSeconds)
    {
        try
        {
            EnsureLogFileExists();
            string path = ResolvedLogPath();

            string row = string.Join(",", new string[]
            {
                CsvEscape(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                CsvEscape(currentInstruction),
                CsvEscape(targetObjectName),
                CsvEscape(currentAssumptions),
                CsvEscape(currentCompileOk.ToString()),
                CsvEscape(currentInjectOk.ToString()),
                CsvEscape(decision),
                CsvEscape(decisionTimeSeconds.ToString("F2")),
                CsvEscape(currentCode)
            });

            File.AppendAllText(path, row + "\n");
            Debug.Log($"Logged trial to {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write log: {e.Message}");
        }
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
                name = "Sphere_01",
                components = new[] { "Transform", "MeshRenderer", "SphereCollider", "Rigidbody" },
                tags = new[] { "grabbable" }
            },
            new SceneObjectDescription
            {
                name = "Lever_01",
                components = new[] { "Transform", "MeshRenderer", "BoxCollider", "HingeJoint" },
                tags = new[] { "interactable" }
            },
            new SceneObjectDescription
            {
                name = "Door_01",
                components = new[] { "Transform", "MeshRenderer", "BoxCollider" },
                tags = new[] { "interactable" }
            },
            new SceneObjectDescription
            {
                name = "Button_01",
                components = new[] { "Transform", "MeshRenderer", "BoxCollider" },
                tags = new[] { "interactable" }
            },
            new SceneObjectDescription
            {
                name = "Light_01",
                components = new[] { "Transform", "Light" },
                tags = new string[0]
            },
            new SceneObjectDescription
            {
                name = "Panel_01",
                components = new[] { "Transform", "CanvasRenderer", "RawImage" },
                tags = new string[0]
            },
            new SceneObjectDescription
            {
                name = "Player",
                components = new[] { "XROrigin", "Camera" },
                tags = new string[0]
            }
        };
        Debug.Log("Loaded default scene description (8 objects).");
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

    private void ClearAllInjectedComponents(GameObject target)
    {
        if (target == null) return;

        var allComponents = target.GetComponents<Component>();
        foreach (var comp in allComponents)
        {
            if (comp == null) continue;

            Type t = comp.GetType();
            string ns = t.Namespace ?? "";

            bool isUnityBuiltin = ns.StartsWith("UnityEngine") || t == typeof(Transform);

            if (!isUnityBuiltin)
            {
                Destroy(comp);
            }
        }

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private IEnumerator GenerateAndInjectCoroutine()
    {
        string typedText = instructionInput != null ? instructionInput.text : "";

        if (string.IsNullOrWhiteSpace(typedText) && instructionInput != null)
        {
            foreach (var t in instructionInput.GetComponentsInChildren<Text>())
            {
                if (t.gameObject.name == "Text")
                {
                    typedText = t.text;
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(typedText))
        {
            instruction = typedText;
        }

        Debug.Log($"[DEBUG] final instruction being sent: '{instruction}'");

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

        GenerationResponse response = null;
        Exception parseException = null;
        try
        {
            response = JsonUtility.FromJson<GenerationResponse>(webRequest.downloadHandler.text);
        }
        catch (Exception e)
        {
            parseException = e;
        }

        if (parseException != null)
        {
            DisplayError($"Failed to parse response: {parseException.Message}\nRaw: {webRequest.downloadHandler.text}");
            yield break;
        }

        if (response == null)
        {
            DisplayError($"Server returned an empty or unparseable response.\nRaw: {webRequest.downloadHandler.text}");
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

        ClearAllInjectedComponents(target);

        lastTargetPosition = target.transform.position;
        lastTargetRotation = target.transform.rotation;
        lastTargetScale = target.transform.localScale;

        var injectionResult = RuntimeCodeInjector.InjectScript(response.code, target);

        currentInstruction = instruction;
        currentCode = response.code;
        currentAssumptions = response.assumptions != null ? string.Join(" | ", response.assumptions) : "";
        currentCompileOk = injectionResult.CompileSucceeded;
        currentInjectOk = injectionResult.InjectionSucceeded;

        if (!injectionResult.CompileSucceeded)
        {
            DisplayError("Generated code FAILED to compile:\n" + string.Join("\n", injectionResult.Diagnostics));
            LogTrial(target.name, "compile_failed", 0f);
            yield break;
        }

        if (!injectionResult.InjectionSucceeded)
        {
            DisplayError("Generated code compiled but FAILED to inject:\n" + string.Join("\n", injectionResult.Diagnostics));
            LogTrial(target.name, "inject_failed", 0f);
            yield break;
        }

        lastInjectedComponent = injectionResult.InjectedComponent;
        lastInjectedTarget = target;
        trialStartTime = Time.time;

        string assumptionsList = string.Join("\n - ", response.assumptions);
        string summary = $"Applied to: {target.name}\n\nThe AI assumed:\n - {assumptionsList}\n\n[Accept] to keep, [Reject] to undo.";
        DisplayMessage(summary);
    }

    public void AcceptCurrent()
    {
        if (lastInjectedComponent == null)
        {
            DisplayMessage("Nothing to accept.");
            return;
        }

        float decisionTime = Time.time - trialStartTime;
        LogTrial(lastInjectedTarget.name, "accept", decisionTime);

        DisplayMessage($"Accepted - {lastInjectedComponent.GetType().Name} kept on {lastInjectedTarget.name}.");
        lastInjectedComponent = null;
        lastInjectedTarget = null;
    }

    public void RejectCurrent()
    {
        if (lastInjectedComponent == null)
        {
            DisplayMessage("Nothing to reject.");
            return;
        }

        string componentName = lastInjectedComponent.GetType().Name;
        string targetName = lastInjectedTarget.name;
        GameObject target = lastInjectedTarget;

        float decisionTime = Time.time - trialStartTime;
        LogTrial(targetName, "reject", decisionTime);

        ClearAllInjectedComponents(target);

        target.transform.position = lastTargetPosition;
        target.transform.rotation = lastTargetRotation;
        target.transform.localScale = lastTargetScale;

        DisplayMessage($"Reverted - removed {componentName} from {targetName}.");
        lastInjectedComponent = null;
        lastInjectedTarget = null;
    }
}
