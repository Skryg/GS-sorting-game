#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

// Logs what is in memory after every scene load, so that rounds can be compared (logcat: "[Round]").
// Numbers that keep growing from one round to the next are leaks. Development builds only.
public class RoundDiagnostics : MonoBehaviour
{
    private const float MeasureSeconds = 3f;

    private int loads;
    private float loadStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject(nameof(RoundDiagnostics));
        DontDestroyOnLoad(go);
        go.AddComponent<RoundDiagnostics>();
    }

    private void Awake()
    {
        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneUnloaded(Scene scene) => loadStarted = Time.realtimeSinceStartup;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StopAllCoroutines();
        StartCoroutine(Measure(scene.name));
    }

    private IEnumerator Measure(string scene)
    {
        loads++;
        float end = Time.realtimeSinceStartup + MeasureSeconds;
        float worstFrame = 0f;
        int slowFrames = 0;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
            worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
            if (Time.unscaledDeltaTime > 0.02f) slowFrames++;
        }

        Debug.Log($"[Round] load {loads} '{scene}': worst frame {worstFrame * 1000f:0} ms, {slowFrames} frames over 20 ms, " +
                  $"{(loadStarted > 0f ? end - MeasureSeconds - loadStarted : 0f):0.00} s since the old scene was unloaded | " +
                  $"managed {Megabytes(Profiler.GetMonoUsedSizeLong())} MB, native {Megabytes(Profiler.GetTotalAllocatedMemoryLong())} MB, " +
                  $"graphics {Megabytes(Profiler.GetAllocatedMemoryForGraphicsDriver())} MB | " +
                  $"splat objects {Count<GaussianSplatRenderer>()}, splat assets {Count<GaussianSplatAsset>()}, meshes {Count<Mesh>()}, " +
                  $"materials {Count<Material>()}, textures {Count<Texture>()}, audio {Count<AudioClip>()}, game objects {Count<GameObject>()}");
    }

    private static string Megabytes(long bytes) => (bytes / 1048576f).ToString("0.0");

    private static int Count<T>() where T : Object => Resources.FindObjectsOfTypeAll<T>().Length;
}
#endif
