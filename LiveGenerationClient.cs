using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class LiveGenerationClient : MonoBehaviour
{
    public string serverUrl = "http://127.0.0.1:5005/generate";
    public string regenerateServerUrl = "http://127.0.0.1:5005/regenerate";

    [TextArea]
    public string instruction = "Make this cube spin continuously";

    [Header("UI")]
    public Text assumptionsText;
    public InputField instructionInput;
    public Transform assumptionsListContainer;
    public Toggle assumptionTogglePrefab;

    [Header("Belief Elicitation UI")]
    public GameObject beliefPromptPanel;       // a small panel containing the question + Yes/No buttons
    public Text beliefQuestionText;            // dedicated text INSIDE the panel, separate from assumptionsText
    public Button beliefYesButton;
    public Button beliefNoButton;

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
    private string[] currentAssumptionsArray;
    private bool currentCompileOk;
    private bool currentInjectOk;
    private float trialStartTime;

    // --- Study instrumentation state ---
    private string[] pendingRejected;
    private string[] pendingKept;
    private string preRegenerateCode;
    private bool disclosureShownThisRegenerate;
    private string beliefAnswer; // "yes", "no", or "" if not answered
    private float beliefPromptStartTime;

    private List<Toggle> activeToggles = new List<Toggle>();
    private System.Random rng = new System.Random();

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

    [System.Serializable]
    private class RegenerationRequest
    {
        public SceneObjectDescription[] scene;
        public string instruction;
        public string previous_code;
        public string[] rejected_assumptions;
        public string[] kept_assumptions;
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
            // Extended header: adds disclosure_shown, belief_answer, belief_correct,
            // pre_regenerate_code so fidelity diffing can be done offline later.
            string header = "timestamp,instruction,target_object,assumptions,compile_ok,inject_ok,decision,decision_time_seconds,disclosure_shown,belief_answer,belief_correct,pre_regenerate_code,code\n";
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

    /// <summary>
    /// Extended logging. belief_correct is computed as:
    /// "yes" answer is correct if the post-regenerate code is identical to
    /// pre-regenerate code outside of what was rejected (we approximate this
    /// with a simple equality/substring check here; full semantic diffing
    /// should be done offline against the logged code columns).
    /// For non-regenerate decisions (accept/reject/compile_failed/etc),
    /// disclosure/belief fields are left blank.
    /// </summary>
    private void LogTrial(string targetObjectName, string decision, float decisionTimeSeconds,
        bool? disclosureShown = null, string beliefAnswerValue = null, string preCode = null)
    {
        try
        {
            EnsureLogFileExists();
            string path = ResolvedLogPath();

            string disclosureField = disclosureShown.HasValue ? disclosureShown.Value.ToString() : "";
            string beliefField = beliefAnswerValue ?? "";
            string beliefCorrectField = "";
            string preCodeField = preCode ?? "";

            if (!string.IsNullOrEmpty(beliefField) && preCode != null)
            {
                bool unrelatedChanged = !string.Equals(
                    NormalizeCode(preCode),
                    NormalizeCode(currentCode),
                    StringComparison.Ordinal
                );
                // "yes" (expected no unrelated change) is correct if code did NOT
                // change beyond what rejection implies; "no" is correct if it DID.
                // This is a coarse proxy (exact-match) - refine offline with a real
                // diff against rejected_assumptions scope if needed.
                bool beliefWasCorrect = (beliefField == "yes" && !unrelatedChanged)
                                      || (beliefField == "no" && unrelatedChanged);
                beliefCorrectField = beliefWasCorrect.ToString();
            }

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
                CsvEscape(disclosureField),
                CsvEscape(beliefField),
                CsvEscape(beliefCorrectField),
                CsvEscape(preCodeField),
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

    private string NormalizeCode(string code)
    {
        if (code == null) return "";
        return code.Replace("\r\n", "\n").Trim();
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

    private void ClearToggles()
    {
        foreach (var t in activeToggles)
        {
            if (t != null) Destroy(t.gameObject);
        }
        activeToggles.Clear();
    }

    private void PopulateAssumptionToggles(string[] assumptions, string targetName)
    {
        ClearToggles();

        if (assumptionsListContainer == null || assumptionTogglePrefab == null)
        {
            string assumptionsList = string.Join("\n - ", assumptions);
            DisplayMessage($"Applied to: {targetName}\n\nThe AI assumed:\n - {assumptionsList}\n\n[Apply Selected] to keep, [Regenerate Unchecked] to fix.");
            return;
        }

        DisplayMessage($"Applied to: {targetName}\n\nReview assumptions below. Uncheck any that are WRONG, then press Regenerate Unchecked. Press Apply Selected when satisfied.");

        foreach (var assumption in assumptions)
        {
            Toggle toggle = Instantiate(assumptionTogglePrefab, assumptionsListContainer);
            toggle.isOn = true;
            Text label = toggle.GetComponentInChildren<Text>();
            if (label != null) label.text = assumption;
            activeToggles.Add(toggle);
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

        Debug.Log($"[RAW RESPONSE] {webRequest.downloadHandler.text}");

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
        currentAssumptionsArray = response.assumptions;
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

        PopulateAssumptionToggles(response.assumptions, target.name);
    }

    public void ApplySelected()
    {
        if (lastInjectedComponent == null)
        {
            DisplayMessage("Nothing to apply.");
            return;
        }

        float decisionTime = Time.time - trialStartTime;
        LogTrial(lastInjectedTarget.name, "accept", decisionTime);

        DisplayMessage($"Applied - {lastInjectedComponent.GetType().Name} kept on {lastInjectedTarget.name}.");
        ClearToggles();
        lastInjectedComponent = null;
        lastInjectedTarget = null;
    }

    /// <summary>
    /// Entry point for the Regenerate Unchecked button. Computes rejected/kept
    /// sets from toggle state, then ALWAYS routes through the belief-elicitation
    /// step before actually calling the server - this is the core study
    /// instrumentation point.
    /// </summary>
    public void RegenerateUnchecked()
    {
        if (lastInjectedComponent == null || currentAssumptionsArray == null)
        {
            DisplayMessage("Nothing to regenerate.");
            return;
        }

        var rejected = new List<string>();
        var kept = new List<string>();

        if (activeToggles.Count == 0)
        {
            rejected.AddRange(currentAssumptionsArray);
        }
        else
        {
            for (int i = 0; i < activeToggles.Count && i < currentAssumptionsArray.Length; i++)
            {
                if (activeToggles[i].isOn)
                    kept.Add(currentAssumptionsArray[i]);
                else
                    rejected.Add(currentAssumptionsArray[i]);
            }

            if (rejected.Count == 0)
            {
                DisplayMessage("Nothing unchecked - nothing to regenerate. Uncheck wrong assumptions first.");
                return;
            }
        }

        pendingRejected = rejected.ToArray();
        pendingKept = kept.ToArray();
        preRegenerateCode = currentCode;

        // Randomize disclosure condition PER TRIAL for proper experimental control.
        disclosureShownThisRegenerate = rng.Next(2) == 0;

        ShowBeliefPrompt();
    }

    /// <summary>
    /// Shows the belief-elicitation question. If no belief UI is wired up
    /// (beliefPromptPanel/buttons null), falls back to skipping straight to
    /// the disclosure message + regenerate, so the build never hard-fails
    /// if you haven't added the panel yet - but for real study trials this
    /// UI MUST be wired up or you lose the belief data for that trial.
    /// </summary>
    private void ShowBeliefPrompt()
    {
        if (beliefPromptPanel == null || beliefYesButton == null || beliefNoButton == null)
        {
            Debug.LogWarning("[STUDY WARNING] Belief prompt UI not wired up - skipping belief elicitation for this trial. Belief data will be blank in the log.");
            beliefAnswer = "";
            ProceedToDisclosureAndRegenerate();
            return;
        }

        beliefPromptStartTime = Time.time;
        beliefPromptPanel.SetActive(true);

        // Wire listeners fresh each time to avoid stacking duplicate calls
        beliefYesButton.onClick.RemoveAllListeners();
        beliefNoButton.onClick.RemoveAllListeners();
        beliefYesButton.onClick.AddListener(() => OnBeliefAnswered("yes"));
        beliefNoButton.onClick.AddListener(() => OnBeliefAnswered("no"));

        string question = "Before regenerating: do you expect the REST of the behavior (the parts you did NOT uncheck) to stay exactly the same?";
        if (beliefQuestionText != null)
            beliefQuestionText.text = question;
        else
            DisplayMessage(question); // fallback if dedicated text not wired up yet
    }

    private void OnBeliefAnswered(string answer)
    {
        beliefAnswer = answer;
        if (beliefPromptPanel != null) beliefPromptPanel.SetActive(false);
        ProceedToDisclosureAndRegenerate();
    }

    private void ProceedToDisclosureAndRegenerate()
    {
        float decisionTime = Time.time - trialStartTime;
        LogTrial(lastInjectedTarget.name, "partial_reject", decisionTime,
            disclosureShown: disclosureShownThisRegenerate,
            beliefAnswerValue: beliefAnswer,
            preCode: preRegenerateCode);

        if (disclosureShownThisRegenerate)
        {
            DisplayMessage("Regenerating based on your feedback...\n\nNote: this correction may also change parts of the behavior you did not flag.");
        }
        else
        {
            DisplayMessage("Regenerating based on your feedback...");
        }

        StartCoroutine(RegenerateCoroutine(pendingRejected, pendingKept));
    }

    private IEnumerator RegenerateCoroutine(string[] rejectedAssumptions, string[] keptAssumptions)
    {
        var requestBody = new RegenerationRequest
        {
            scene = sceneDescription,
            instruction = currentInstruction,
            previous_code = currentCode,
            rejected_assumptions = rejectedAssumptions,
            kept_assumptions = keptAssumptions
        };
        string json = JsonUtility.ToJson(requestBody);

        var bodyRaw = Encoding.UTF8.GetBytes(json);
        using var webRequest = new UnityWebRequest(regenerateServerUrl, "POST");
        webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
        webRequest.downloadHandler = new DownloadHandlerBuffer();
        webRequest.SetRequestHeader("Content-Type", "application/json");

        yield return webRequest.SendWebRequest();

        if (webRequest.result != UnityWebRequest.Result.Success)
        {
            DisplayError($"Regeneration request failed: {webRequest.error}\n{webRequest.downloadHandler.text}");
            yield break;
        }

        Debug.Log($"[RAW RESPONSE] {webRequest.downloadHandler.text}");

        GenerationResponse response = null;
        try
        {
            response = JsonUtility.FromJson<GenerationResponse>(webRequest.downloadHandler.text);
        }
        catch (Exception e)
        {
            DisplayError($"Failed to parse regeneration response: {e.Message}");
            yield break;
        }

        if (response == null || !string.IsNullOrEmpty(response.error))
        {
            DisplayError($"Regeneration server error: {(response != null ? response.error : "empty response")}");
            yield break;
        }

        GameObject target = lastInjectedTarget;
        if (target == null)
        {
            target = GameObject.Find(response.target_object);
        }
        if (target == null)
        {
            DisplayError($"Target object '{response.target_object}' not found in scene.");
            yield break;
        }

        ClearAllInjectedComponents(target);

        var injectionResult = RuntimeCodeInjector.InjectScript(response.code, target);

        currentCode = response.code;
        currentAssumptions = response.assumptions != null ? string.Join(" | ", response.assumptions) : "";
        currentAssumptionsArray = response.assumptions;
        currentCompileOk = injectionResult.CompileSucceeded;
        currentInjectOk = injectionResult.InjectionSucceeded;

        if (!injectionResult.CompileSucceeded)
        {
            DisplayError("Regenerated code FAILED to compile:\n" + string.Join("\n", injectionResult.Diagnostics));
            LogTrial(target.name, "regenerate_compile_failed", 0f);
            yield break;
        }

        if (!injectionResult.InjectionSucceeded)
        {
            DisplayError("Regenerated code compiled but FAILED to inject:\n" + string.Join("\n", injectionResult.Diagnostics));
            LogTrial(target.name, "regenerate_inject_failed", 0f);
            yield break;
        }

        lastInjectedComponent = injectionResult.InjectedComponent;
        lastInjectedTarget = target;
        trialStartTime = Time.time;

        // Log the actual outcome now that we have post-regenerate code, so
        // belief_correct can be computed against the real diff.
        LogTrial(target.name, "regenerate_complete", Time.time - trialStartTime,
            disclosureShown: disclosureShownThisRegenerate,
            beliefAnswerValue: beliefAnswer,
            preCode: preRegenerateCode);

        PopulateAssumptionToggles(response.assumptions, target.name);
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
        ClearToggles();
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
        ClearToggles();
        lastInjectedComponent = null;
        lastInjectedTarget = null;
    }
}