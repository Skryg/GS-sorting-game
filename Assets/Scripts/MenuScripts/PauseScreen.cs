using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Lives on the same object as GameOverScreen, so the pause panel shows up in the same spot.
public class PauseScreen : MonoBehaviour
{
    public string menuSceneName = "StartMenuScene";

    private GameObject canvasObject;
    private TextMeshProUGUI scoreText;
    private InputAction pauseAction;

    private void Awake()
    {
        // The right-hand menu button is reserved by the headset system, so only the left one pauses.
        pauseAction = new InputAction("Pause", InputActionType.Button);
        pauseAction.AddBinding("<XRController>{LeftHand}/{MenuButton}");
        pauseAction.AddBinding("<Keyboard>/escape");
        pauseAction.performed += _ => TogglePause();
    }

    private void Start()
    {
        canvasObject = BuildCanvas();
        canvasObject.SetActive(false);
        GameService.Instance.PauseChanged += OnPauseChanged;
    }

    private void OnEnable() => pauseAction.Enable();

    private void OnDisable() => pauseAction.Disable();

    private void OnDestroy()
    {
        pauseAction.Dispose();
        if (GameService.Instance != null)
        {
            GameService.Instance.PauseChanged -= OnPauseChanged;
        }
    }

    // Opening the system menu or taking the headset off must not leave the game running.
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && !Application.isEditor) Pause();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused && !Application.isEditor) Pause();
    }

    private void TogglePause()
    {
        if (GameService.Instance == null) return;
        GameService.Instance.SetPaused(!GameService.Instance.IsPaused);
    }

    public void Pause()
    {
        if (GameService.Instance != null) GameService.Instance.SetPaused(true);
    }

    public void Resume()
    {
        GameService.Instance.SetPaused(false);
    }

    public void Restart()
    {
        GameService.Instance.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void BackToMenu()
    {
        GameService.Instance.LoadScene(menuSceneName);
    }

    private void OnPauseChanged(bool paused)
    {
        if (paused)
        {
            canvasObject.transform.SetPositionAndRotation(transform.position, transform.rotation);
            scoreText.text = $"Score: {GameService.Instance.Score}";
        }
        canvasObject.SetActive(paused);
    }

    private GameObject BuildCanvas()
    {
        var canvasRect = WorldPanelBuilder.CreatePanel("Pause Canvas", new Vector2(1600f, 1000f));

        WorldPanelBuilder.CreateText("Title", canvasRect, "Paused", 120f, new Vector2(0f, 380f), new Vector2(1500f, 150f));
        scoreText = WorldPanelBuilder.CreateText("Score", canvasRect, "Score: 0", 60f, new Vector2(0f, 270f), new Vector2(1500f, 80f));

        WorldPanelBuilder.CreateButton("Button_Resume", canvasRect, "Resume", WorldPanelBuilder.Accent, new Vector2(-380f, 90f), new Vector2(620f, 150f), Resume);
        WorldPanelBuilder.CreateButton("Button_Restart", canvasRect, "Restart", WorldPanelBuilder.Neutral, new Vector2(-380f, -90f), new Vector2(620f, 130f), Restart);
        WorldPanelBuilder.CreateButton("Button_Menu", canvasRect, "Main Menu", WorldPanelBuilder.Neutral, new Vector2(-380f, -250f), new Vector2(620f, 130f), BackToMenu);

        var volumeTitle = WorldPanelBuilder.CreateText("VolumeTitle", canvasRect, "Volume", 64f, new Vector2(400f, 130f), new Vector2(640f, 90f));
        volumeTitle.alignment = TextAlignmentOptions.Left;
        VolumeSliders.Create(canvasRect, new Vector2(400f, 0f), 640f);

        return canvasRect.gameObject;
    }
}
