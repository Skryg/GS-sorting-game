using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UColor = UnityEngine.Color;

public class GameOverScreen : MonoBehaviour
{
    public string menuSceneName = "StartMenuScene";

    private static readonly Vector2 PanelSize = new Vector2(1600f, 1000f);
    private static readonly UColor PlacedColor = new UColor(1f, 0.84f, 0.29f);
    private static readonly UColor NotPlacedColor = new UColor(0.7f, 0.74f, 0.8f);

    private GameObject canvasObject;
    private TextMeshProUGUI scoreText;
    private TextMeshProUGUI placementText;
    private TextMeshProUGUI tableText;

    private void Start()
    {
        canvasObject = BuildCanvas();
        canvasObject.SetActive(false);
        GameService.Instance.GameOver += Show;
    }

    private void OnDestroy()
    {
        if (GameService.Instance != null)
        {
            GameService.Instance.GameOver -= Show;
        }
    }

    // The panel appears at this object's position; its blue (Z) axis should point away from the player.
    private void Show()
    {
        canvasObject.transform.SetPositionAndRotation(transform.position, transform.rotation);

        int placement = GameService.Instance.LastPlacement;
        scoreText.text = $"Score: {GameService.Instance.Score}";
        placementText.text = placement > 0
            ? $"New highscore! You placed #{placement}"
            : $"Not in the top {Highscores.MaxEntries}";
        placementText.color = placement > 0 ? PlacedColor : NotPlacedColor;
        tableText.text = HighscoreTableText.Build(Highscores.Load(), placement);

        canvasObject.SetActive(true);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(PanelSize.x, PanelSize.y, 0f) * 0.001f);
        Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.3f);
    }

    public void Restart()
    {
        GameService.Instance.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void BackToMenu()
    {
        GameService.Instance.LoadScene(menuSceneName);
    }

    private GameObject BuildCanvas()
    {
        var canvasRect = WorldPanelBuilder.CreatePanel("Game Over Canvas", PanelSize);

        WorldPanelBuilder.CreateText("Title", canvasRect, "Game Over", 120f, new Vector2(0f, 390f), new Vector2(1500f, 150f));
        scoreText = WorldPanelBuilder.CreateText("Score", canvasRect, "Score: 0", 76f, new Vector2(0f, 270f), new Vector2(1500f, 100f));
        placementText = WorldPanelBuilder.CreateText("Placement", canvasRect, "", 56f, new Vector2(0f, 180f), new Vector2(1500f, 80f));

        var tableTitle = WorldPanelBuilder.CreateText("TableTitle", canvasRect, "Highscores", 56f, new Vector2(-370f, 90f), new Vector2(700f, 80f));
        tableTitle.alignment = TextAlignmentOptions.Left;
        tableText = WorldPanelBuilder.CreateText("Table", canvasRect, "", 52f, new Vector2(-370f, -175f), new Vector2(700f, 450f));
        tableText.alignment = TextAlignmentOptions.TopLeft;
        tableText.fontStyle = FontStyles.Normal;

        WorldPanelBuilder.CreateButton("Button_Restart", canvasRect, "Play Again", WorldPanelBuilder.Accent, new Vector2(400f, -40f), new Vector2(620f, 150f), Restart);
        WorldPanelBuilder.CreateButton("Button_Menu", canvasRect, "Main Menu", WorldPanelBuilder.Neutral, new Vector2(400f, -230f), new Vector2(620f, 130f), BackToMenu);

        return canvasRect.gameObject;
    }
}
