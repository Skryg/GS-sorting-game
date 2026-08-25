using UnityEngine;
using System.Collections;
using TMPro;

public class ScoreManager : MonoBehaviour
{
    public TextMeshProUGUI counterText; 
    private int currentPoints = 0;

    private void Awake()
    {
        if (counterText == null)
        {
            counterText = GetComponent<TextMeshProUGUI>();
        }
    }

    public void UpdatePoints(int newPoints)
    {
        int change = newPoints - currentPoints;
        currentPoints = newPoints;
        counterText.text = currentPoints.ToString();
        var color = change > 0 ? Color.yellow : Color.red;
        StopAllCoroutines(); 
        StartCoroutine(AnimateCounter(color));
    }

    private IEnumerator AnimateCounter(Color color)
    {
        counterText.color = color;
        counterText.transform.localScale = Vector3.one * 1.5f;
        float duration = 0.3f;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float postep = time / duration;

            counterText.color = Color.Lerp(color, Color.white, postep);
            counterText.transform.localScale = Vector3.Lerp(Vector3.one * 1.5f, Vector3.one, postep);

            yield return null; 
        }

        counterText.transform.localScale = Vector3.one;
        counterText.color = Color.white;
    }
}