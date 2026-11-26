using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UColor = UnityEngine.Color;

// Shared look of the in-game world-space panels (game over, pause).
public static class WorldPanelBuilder
{
    public static readonly UColor Accent = new UColor(0.10f, 0.62f, 0.72f);
    public static readonly UColor Neutral = new UColor(0.30f, 0.34f, 0.40f);
    public static readonly Vector2 PanelSize = new Vector2(1200f, 800f);

    public static RectTransform CreatePanel(string name)
    {
        return CreatePanel(name, PanelSize);
    }

    public static RectTransform CreatePanel(string name, Vector2 size)
    {
        var canvasRect = CreateRect(name, null, Vector2.zero, size);
        canvasRect.localScale = Vector3.one * 0.001f;

        var canvas = canvasRect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 100;
        canvasRect.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        canvasRect.gameObject.AddComponent<GraphicRaycaster>();

        CreateImage("Background", canvasRect, Vector2.zero, size, new UColor(0.06f, 0.08f, 0.12f, 0.95f));
        CreateImage("AccentBar", canvasRect, new Vector2(0f, size.y / 2f - 6f), new Vector2(size.x, 12f), Accent);
        return canvasRect;
    }

    // Destroy is not allowed in edit mode.
    public static void DestroyAnywhere(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Object.Destroy(target);
        else Object.DestroyImmediate(target);
    }

    public static RectTransform CreateRect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    public static Image CreateImage(string name, Transform parent, Vector2 position, Vector2 size, UColor color)
    {
        var image = CreateRect(name, parent, position, size).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float fontSize, Vector2 position, Vector2 size)
    {
        var tmp = CreateRect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = UColor.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }

    public static Slider CreateSlider(string name, Transform parent, Vector2 position, Vector2 size, float value, UnityAction<float> onChanged)
    {
        var root = CreateRect(name, parent, position, size);
        var slider = root.gameObject.AddComponent<Slider>();

        var background = CreateImage("Background", root, Vector2.zero, Vector2.zero, new UColor(0.18f, 0.2f, 0.25f));
        Stretch(background.rectTransform, Vector2.zero, Vector2.zero);
        background.raycastTarget = true;

        var fillArea = CreateRect("Fill Area", root, Vector2.zero, Vector2.zero);
        Stretch(fillArea, Vector2.zero, Vector2.zero);
        var fill = CreateImage("Fill", fillArea, Vector2.zero, Vector2.zero, Accent);
        Stretch(fill.rectTransform, Vector2.zero, Vector2.zero);

        // The slider only drives the horizontal anchors, so the handle is stretched vertically and overhangs the bar.
        var handleSize = new Vector2(size.y * 0.7f, size.y * 0.8f);
        var handleArea = CreateRect("Handle Slide Area", root, Vector2.zero, Vector2.zero);
        Stretch(handleArea, new Vector2(handleSize.x / 2f, 0f), new Vector2(-handleSize.x / 2f, 0f));
        var handle = CreateImage("Handle", handleArea, Vector2.zero, Vector2.zero, UColor.white);
        handle.rectTransform.anchorMin = Vector2.zero;
        handle.rectTransform.anchorMax = new Vector2(0f, 1f);
        handle.rectTransform.sizeDelta = handleSize;
        handle.raycastTarget = true;

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = value;
        if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    public static void CreateButton(string name, Transform parent, string label, UColor color, Vector2 position, Vector2 size, UnityAction onClick)
    {
        var image = CreateImage(name, parent, position, size, UColor.white);
        image.raycastTarget = true;

        var button = image.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = UColor.Lerp(color, UColor.white, 0.3f);
        colors.pressedColor = UColor.Lerp(color, UColor.black, 0.35f);
        colors.selectedColor = color;
        button.colors = colors;
        button.onClick.AddListener(onClick);

        var text = CreateText("Label", image.transform, label, 70f, Vector2.zero, Vector2.zero);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
    }
}
