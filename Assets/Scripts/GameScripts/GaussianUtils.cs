using GaussianSplatting.Runtime;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class GaussianUtils
{
    private static IEnumerator FadeOutAndDestroyGaussianObjectCoroutine(GameObject  gameObject, float vanishDelay)
    {

        var gaussianRenderer = gameObject.GetComponent<GaussianSplatRenderer>();
        float startOpacity = gaussianRenderer.m_OpacityScale;
        float time = 0f;
        while (time < vanishDelay)
        {
            time += Time.deltaTime;
            gaussianRenderer.m_OpacityScale = Mathf.Lerp(startOpacity, 0f, time / vanishDelay);
            yield return null;
        }
        Object.Destroy(gameObject);
    }

    public static void FadeOutAndDestroyGaussianObject(this MonoBehaviour monoBehaviour, GameObject gameObject, float vanishDelay)
    {
        monoBehaviour.StartCoroutine(FadeOutAndDestroyGaussianObjectCoroutine(gameObject, vanishDelay));
    }
}
