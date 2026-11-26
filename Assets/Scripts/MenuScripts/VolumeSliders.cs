using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Music and sound effects sliders, shared by the main menu settings and the pause panel.
// The sliders are plain UI; this component hooks them up when the game runs, so they can also be built into a scene.
public class VolumeSliders : MonoBehaviour
{
    public const float RowHeight = 190f;
    private const float FontSize = 54f;

    [SerializeField] private Slider musicSlider;
    [SerializeField] private TextMeshProUGUI musicValue;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private TextMeshProUGUI sfxValue;

    // position is the centre of the first row; the second row goes below it.
    public static VolumeSliders Create(Transform parent, Vector2 position, float width)
    {
        var view = parent.gameObject.AddComponent<VolumeSliders>();
        view.musicSlider = CreateRow("Music", parent, position, width, out view.musicValue);
        view.sfxSlider = CreateRow("Sound effects", parent, position - new Vector2(0f, RowHeight), width, out view.sfxValue);
        return view;
    }

    // Start, not Awake: at runtime Create fills the references only after AddComponent has run Awake.
    private void Start()
    {
        Bind(musicSlider, musicValue, AudioManager.MusicVolume, AudioManager.SetMusicVolume, false);
        Bind(sfxSlider, sfxValue, AudioManager.SfxVolume, AudioManager.SetSfxVolume, true);
    }

    private static Slider CreateRow(string label, Transform parent, Vector2 position, float width, out TextMeshProUGUI percent)
    {
        var labelPosition = position + new Vector2(0f, 40f);
        var title = WorldPanelBuilder.CreateText(label, parent, label, FontSize, labelPosition, new Vector2(width, 70f));
        title.alignment = TextAlignmentOptions.Left;
        percent = WorldPanelBuilder.CreateText(label + " Value", parent, ToPercent(1f), FontSize, labelPosition, new Vector2(width, 70f));
        percent.alignment = TextAlignmentOptions.Right;

        var slider = WorldPanelBuilder.CreateSlider(label + " Slider", parent, position + new Vector2(0f, -40f), new Vector2(width, 50f), 1f, null);
        slider.gameObject.AddComponent<SliderRelease>();
        return slider;
    }

    private static void Bind(Slider slider, TextMeshProUGUI percent, float value, System.Action<float> setVolume, bool previewOnRelease)
    {
        if (slider == null) return;
        slider.SetValueWithoutNotify(value);
        if (percent != null) percent.text = ToPercent(value);

        slider.onValueChanged.AddListener(newValue =>
        {
            setVolume(newValue);
            if (percent != null) percent.text = ToPercent(newValue);
        });

        // Saving and the sound preview wait for the release, otherwise every drag step would trigger them.
        var release = slider.GetComponent<SliderRelease>();
        if (release == null) release = slider.gameObject.AddComponent<SliderRelease>();
        release.Released = () =>
        {
            AudioManager.SaveVolumeSettings();
            if (previewOnRelease) AudioManager.Play2D(Sfx.Click);
        };
    }

    private static string ToPercent(float value)
    {
        return $"{Mathf.RoundToInt(value * 100f)}%";
    }
}
