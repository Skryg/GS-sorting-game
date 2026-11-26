#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

// Replaces XRI's auto-instantiate (turned off in XRDeviceSimulatorSettings): that one runs from a static
// constructor, so with domain reload disabled it misses every Play after the first, and it ignores a connected headset.
static class EditorXRSimulatorBootstrap
{
    // Samples/XR Interaction Toolkit/3.0.8/XR Device Simulator/XR Device Simulator.prefab
    const string PrefabGuid = "18ddb545287c546e19cc77dc9fbb2189";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (IsHeadsetActive())
        {
            RemoveSimulators();
            return;
        }
        if (XRDeviceSimulator.instance != null) return;

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(PrefabGuid));
        if (prefab == null)
        {
            Debug.LogWarning("XR Device Simulator prefab not found, reimport the XR Device Simulator sample of XR Interaction Toolkit.");
            return;
        }

        var simulator = Object.Instantiate(prefab);
        simulator.name = prefab.name;
        Object.DontDestroyOnLoad(simulator);
        // the headset session can come up a few frames after the first scene loads
        simulator.AddComponent<RemoveWhenHeadsetStarts>();
    }

    // OpenXR creates its display subsystem only when a runtime accepts the instance; with no headset it fails.
    internal static bool IsHeadsetActive()
    {
        if (XRSettings.isDeviceActive) return true;
        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        return displays.Count > 0;
    }

    internal static void RemoveSimulators()
    {
        foreach (var simulator in Object.FindObjectsByType<XRDeviceSimulator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.Destroy(simulator.gameObject);
    }

    class RemoveWhenHeadsetStarts : MonoBehaviour
    {
        const float WatchSeconds = 10f;

        void Update()
        {
            if (IsHeadsetActive())
            {
                Debug.Log("Headset detected, removing XR Device Simulator.");
                RemoveSimulators();
            }
            else if (Time.unscaledTime > WatchSeconds)
            {
                Destroy(this);
            }
        }
    }
}
#endif
