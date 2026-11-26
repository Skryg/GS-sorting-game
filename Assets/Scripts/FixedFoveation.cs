using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

// Lowers the resolution at the edges of the lenses. Fixed foveation, so no eye tracking is needed.
// Does nothing unless the OpenXR "Foveated Rendering" feature is enabled for Android.
public static class FixedFoveation
{
    // Quest levels: 0 = off, up to 1 = strongest
    private const float Level = 0.5f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Apply()
    {
        if (Application.platform != RuntimePlatform.Android) return;

        var displays = new List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var display in displays)
            display.foveatedRenderingLevel = Level;
    }
}
