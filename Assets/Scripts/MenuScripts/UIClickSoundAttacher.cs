using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Gives every Button and Toggle a click sound, including ones built in code or cloned from templates.
public class UIClickSoundAttacher : MonoBehaviour
{
    private const float ScanIntervalSeconds = 0.1f;

    private readonly HashSet<Selectable> hooked = new();
    private float nextScanTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject(nameof(UIClickSoundAttacher));
        DontDestroyOnLoad(go);
        go.AddComponent<UIClickSoundAttacher>();
    }

    // Unscaled time, so panels shown during the pause are hooked too.
    private void Update()
    {
        if (Time.unscaledTime < nextScanTime) return;
        nextScanTime = Time.unscaledTime + ScanIntervalSeconds;

        hooked.RemoveWhere(selectable => selectable == null);
        foreach (var selectable in Selectable.allSelectablesArray)
        {
            if (!hooked.Add(selectable)) continue;

            if (selectable is Button button)
            {
                button.onClick.AddListener(PlayClick);
            }
            else if (selectable is Toggle)
            {
                // onValueChanged would also fire when a toggle is set from code.
                selectable.gameObject.AddComponent<ToggleClickSound>();
            }
        }
    }

    private static void PlayClick()
    {
        AudioManager.Play2D(Sfx.Click);
    }
}
