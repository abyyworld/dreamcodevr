using UnityEngine;

/// <summary>
/// Minimal smoke test for RuntimeCodeInjector. Attach this to any
/// GameObject and press Play. Check the Console for "SMOKE TEST".
/// </summary>
public class SmokeTestInjector : MonoBehaviour
{
    private const string TestSource = @"
using UnityEngine;

public class InjectedHelloComponent : MonoBehaviour
{
    void Start()
    {
        Debug.Log(""Hello from dynamically injected component!"");
    }
}
";

    void Start()
    {
        var result = RuntimeCodeInjector.InjectScript(TestSource, gameObject);

        if (!result.CompileSucceeded)
        {
            Debug.LogError("SMOKE TEST: compile FAILED\n" + string.Join("\n", result.Diagnostics));
            return;
        }

        if (!result.InjectionSucceeded)
        {
            Debug.LogError("SMOKE TEST: compiled but injection FAILED\n" + string.Join("\n", result.Diagnostics));
            return;
        }

        Debug.Log("SMOKE TEST: SUCCESS - injected " + result.InjectedComponent.GetType().Name);
    }
}
