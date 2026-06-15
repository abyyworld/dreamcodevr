using UnityEngine;

/// <summary>
/// Tests the injector's FAILURE path using the spatial_02 code from the
/// benchmark run, which has a malformed brace structure and should not compile.
/// Attach to any GameObject and press Play. Check Console for "BROKEN CODE TEST".
/// </summary>
public class BrokenCodeTestInjector : MonoBehaviour
{
    private const string BrokenSource = @"
using UnityEngine;using UnityEngine.XR;using System.Collections.Generic;public class HandFollower : MonoBehaviour{public float followDistance = 0.5f;private InputDevice rightHandDevice;void Start(){InitializeRightHand();}{void Update(){if (!rightHandDevice.isValid){InitializeRightHand();}if (rightHandDevice.isValid){Vector3 handPosition;Quaternion handRotation;if (rightHandDevice.TryGetFeatureValue(CommonUsages.devicePosition, out handPosition) && rightHandDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out handRotation)){transform.position = handPosition + handRotation * Vector3.forward * followDistance;transform.rotation = handRotation;}}}}{void InitializeRightHand(){List<InputDevice> devices = new List<InputDevice>();InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, devices);if (devices.Count > 0){rightHandDevice = devices[0];Debug.Log(""Right hand device found: "" + rightHandDevice.name);}}}}
";

    void Start()
    {
        var result = RuntimeCodeInjector.InjectScript(BrokenSource, gameObject);

        if (!result.CompileSucceeded)
        {
            Debug.Log("BROKEN CODE TEST: compile correctly FAILED as expected.\nDiagnostics:\n" +
                      string.Join("\n", result.Diagnostics));
            return;
        }

        Debug.LogError("BROKEN CODE TEST: unexpectedly COMPILED - this code should have been invalid!");
    }
}