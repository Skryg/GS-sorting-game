using System.Collections;
using GaussianSplatting.Runtime;
using UnityEngine;

public static class GaussianUtils
{
    // The coroutine runs on runner, so the faded object does not need a MonoBehaviour of its own.
    public static void FadeOutAndDestroyGaussianObject(this MonoBehaviour runner, GameObject gameObject, float duration)
    {
        runner.StartCoroutine(FadeOutAndDestroy(gameObject, duration));
    }

    private static IEnumerator FadeOutAndDestroy(GameObject gameObject, float duration)
    {
        var splats = gameObject.GetComponent<GaussianSplatRenderer>();
        if (splats != null)
        {
            float startOpacity = splats.m_OpacityScale;
            for (float time = 0f; time < duration; time += Time.deltaTime)
            {
                splats.m_OpacityScale = Mathf.Lerp(startOpacity, 0f, time / duration);
                yield return null;
            }
        }
        Object.Destroy(gameObject);
    }
}
