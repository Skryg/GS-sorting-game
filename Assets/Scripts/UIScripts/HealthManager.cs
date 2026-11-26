using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Sits on the bar's fill: shrinks it through anchorMax.x so a sliced (rounded) sprite keeps its corners.
[RequireComponent(typeof(Image))]
public class HealthManager : MonoBehaviour
{
    public Image image;
    public float smoothing = 8f;
    public Gradient colorByHealth = new Gradient
    {
        colorKeys = new[]
        {
            new GradientColorKey(new Color(0.85f, 0.20f, 0.15f), 0f),
            new GradientColorKey(new Color(0.95f, 0.80f, 0.10f), 0.5f),
            new GradientColorKey(new Color(0.30f, 0.80f, 0.25f), 1f),
        }
    };

    private RectTransform fill;
    private float targetHealth = 1f;
    private float displayedHealth = 1f;

    void Awake()
    {
        image = GetComponent<Image>();
        fill = (RectTransform)transform;
        Apply(displayedHealth);
    }

    void Update()
    {
        if (Mathf.Approximately(displayedHealth, targetHealth)) return;

        displayedHealth = Mathf.Lerp(displayedHealth, targetHealth, smoothing * Time.deltaTime);
        if (Mathf.Abs(displayedHealth - targetHealth) < 0.001f)
        {
            displayedHealth = targetHealth;
        }
        Apply(displayedHealth);
    }

    public void UpdateHealth(float newHealth)
    {
        targetHealth = Mathf.Clamp01(newHealth);
    }

    private void Apply(float health)
    {
        fill.anchorMax = new Vector2(health, fill.anchorMax.y);
        image.color = colorByHealth.Evaluate(health);
    }
}
