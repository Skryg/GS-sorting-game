using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Start menu: main panel plus sub panels (how to play, highscores, settings, credits).
// The extra panels and buttons live in the scene; BuildMenu only generates them at startup when the scene has none.
// Button actions are always connected at runtime, so building never depends on serialized listeners.
public class StartMenuController : MonoBehaviour
{
    public string gameSceneName = "MainGame";
    public CanvasGroup menuGroup;
    public GameObject mainPanel;
    public GameObject setupPanel;
    public GameSetupMenu setupMenu;

    [System.Serializable]
    public class BuiltMenu
    {
        public GameObject highscoresPanel;
        public GameObject settingsPanel;
        public GameObject howToPlayPanel;
        public GameObject creditsPanel;
        public TextMeshProUGUI highscoresTable;
        public Button howToPlayButton;
        public Button highscoresButton;
        public Button settingsButton;
        public Button creditsButton;
        public List<Button> backButtons = new();

        public IEnumerable<GameObject> Panels => new[] { highscoresPanel, settingsPanel, howToPlayPanel, creditsPanel };
    }

    [Tooltip("Panels and buttons in the scene. Leave empty to build the menu at startup.")]
    [SerializeField] private BuiltMenu built = new();

    private const float SubPanelContentHeight = 400f;
    private const float SubPanelWidth = 1000f;
    private const float WideSubPanelWidth = 1900f;
    private const float WideSubPanelContentHeight = 640f;
    private const float WideBackButtonHeight = 120f;
    private const float WideSubPanelOffsetY = -50f;
    private const float MusicFadeOutSeconds = 1.5f;

    private bool isLoading = false;

    private bool IsMenuBuilt => built.highscoresPanel != null;

    private void Start()
    {
        if (!IsMenuBuilt) BuildMenu();
        RefreshCredits();
        ConnectButtons();
        ShowMain();
    }

    // A menu saved in the scene keeps the credits from the time it was built.
    private void RefreshCredits()
    {
        if (built.creditsPanel == null) return;
        foreach (var column in built.creditsPanel.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (column.name == "Left") column.text = CreditsText.LeftColumn;
            else if (column.name == "Right") column.text = CreditsText.RightColumn;
        }
    }

    public void ShowMain()
    {
        ShowOnly(mainPanel);
    }

    public void ShowSetup()
    {
        ShowOnly(setupPanel);
    }

    public void ShowHighscores()
    {
        built.highscoresTable.text = HighscoreTableText.Build(Highscores.Load());
        ShowOnly(built.highscoresPanel);
    }

    public void ShowSettings()
    {
        ShowOnly(built.settingsPanel);
    }

    public void ShowHowToPlay()
    {
        ShowOnly(built.howToPlayPanel);
    }

    public void ShowCredits()
    {
        ShowOnly(built.creditsPanel);
    }

    private void ShowOnly(GameObject panel)
    {
        mainPanel.SetActive(panel == mainPanel);
        setupPanel.SetActive(panel == setupPanel);
        foreach (var subPanel in built.Panels)
        {
            if (subPanel != null) subPanel.SetActive(subPanel == panel);
        }
    }

    public void StartGame()
    {
        if (setupMenu != null)
        {
            setupMenu.ApplyToGameSetup();
        }
        LoadScene(gameSceneName);
    }

    public void LoadScene(string sceneName)
    {
        if (!BeginLeaving()) return;
        StartCoroutine(LoadAfterMusicFade(sceneName));
    }

    public void QuitGame()
    {
        if (!BeginLeaving()) return;
        StartCoroutine(QuitAfterMusicFade());
    }

    private bool BeginLeaving()
    {
        if (isLoading) return false;
        isLoading = true;

        if (menuGroup != null)
        {
            menuGroup.interactable = false;
        }
        AudioManager.Instance.StopMusic(MusicFadeOutSeconds);
        return true;
    }

    // The scene loads in the background while the music fades out and is shown only afterwards.
    private IEnumerator LoadAfterMusicFade(string sceneName)
    {
        var loading = SceneManager.LoadSceneAsync(sceneName);
        loading.allowSceneActivation = false;
        yield return new WaitUntil(() => AudioManager.Instance.IsMusicSilent);
        loading.allowSceneActivation = true;
    }

    private IEnumerator QuitAfterMusicFade()
    {
        yield return new WaitUntil(() => AudioManager.Instance.IsMusicSilent);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Play and Quit keep the listeners set in the scene; everything generated gets its action here.
    private void ConnectButtons()
    {
        Connect(built.howToPlayButton, ShowHowToPlay);
        Connect(built.highscoresButton, ShowHighscores);
        Connect(built.settingsButton, ShowSettings);
        Connect(built.creditsButton, ShowCredits);
        foreach (var back in built.backButtons)
        {
            Connect(back, ShowMain);
        }
    }

    private static void Connect(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) button.onClick.AddListener(action);
    }

    // Sub panels are copies of the main panel, so it is rearranged last.
    private void BuildMenu()
    {
        var buttons = mainPanel.transform.Find("Buttons");
        var quitButton = buttons != null ? buttons.Find("Button_Quit") : null;
        var playButton = buttons != null ? buttons.Find("Button_Play") : null;
        if (quitButton == null || playButton == null)
        {
            Debug.LogWarning("Main Panel/Buttons/Button_Play or Button_Quit not found, the extra menus are not available.", this);
            return;
        }

        built = new BuiltMenu();

        built.highscoresPanel = CreateSubPanel("Highscores Panel", "Highscores", $"Top {Highscores.MaxEntries} scores", out var highscoresContent);
        built.highscoresTable = WorldPanelBuilder.CreateText("Table", highscoresContent, "", 54f, Vector2.zero, Vector2.zero);
        built.highscoresTable.rectTransform.anchorMin = Vector2.zero;
        built.highscoresTable.rectTransform.anchorMax = Vector2.one;
        built.highscoresTable.alignment = TextAlignmentOptions.TopLeft;
        built.highscoresTable.fontStyle = FontStyles.Normal;
        built.highscoresTable.margin = new Vector4(150f, 0f, 150f, 0f);

        built.settingsPanel = CreateSubPanel("Settings Panel", "Settings", "Volume", out var settingsContent);
        VolumeSliders.Create(settingsContent, new Vector2(0f, VolumeSliders.RowHeight / 2f), 800f);

        built.howToPlayPanel = CreateSubPanel("How To Play Panel", "How to play", "", out var howToPlayContent, true);
        HowToPlayPanel.Build(howToPlayContent, playButton);

        built.creditsPanel = CreateSubPanel("Credits Panel", "Credits", "Thanks to the authors of these assets", out var creditsContent, true);
        BuildCredits(creditsContent);

        built.howToPlayButton = AddMainButton(quitButton, "Button_HowToPlay", "How to play");
        built.highscoresButton = AddMainButton(quitButton, "Button_Highscores", "Highscores");
        built.settingsButton = AddMainButton(quitButton, "Button_Settings", "Settings");
        built.creditsButton = AddMainButton(quitButton, "Button_Credits", "Credits");
        LayoutMainButtons((RectTransform)buttons, playButton,
            new Transform[] { built.howToPlayButton.transform, built.highscoresButton.transform, built.settingsButton.transform },
            built.creditsButton.transform, quitButton);
    }

    // One big Play button, a row of secondary menus below it and the less used Credits/Quit at the bottom.
    private static void LayoutMainButtons(RectTransform buttons, Transform play, Transform[] middleRow, Transform credits, Transform quit)
    {
        if (buttons.TryGetComponent<VerticalLayoutGroup>(out var layout))
        {
            layout.enabled = false;
            WorldPanelBuilder.DestroyAnywhere(layout);
        }
        buttons.sizeDelta = new Vector2(1500f, 760f);

        Place(play, new Vector2(0f, 210f), new Vector2(1000f, 200f), 96f);

        const float tileWidth = 440f, tileGap = 40f;
        for (int i = 0; i < middleRow.Length; i++)
        {
            float x = (i - (middleRow.Length - 1) / 2f) * (tileWidth + tileGap);
            Place(middleRow[i], new Vector2(x, -10f), new Vector2(tileWidth, 150f), 56f);
            SetColors(middleRow[i], new Color(0.17f, 0.27f, 0.33f));
        }

        Place(credits, new Vector2(-(tileWidth + tileGap) / 2f, -200f), new Vector2(tileWidth, 110f), 46f);
        SetColors(credits, WorldPanelBuilder.Neutral);
        Place(quit, new Vector2((tileWidth + tileGap) / 2f, -200f), new Vector2(tileWidth, 110f), 46f);
    }

    private static void Place(Transform target, Vector2 position, Vector2 size, float fontSize)
    {
        var rect = (RectTransform)target;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        target.GetComponentInChildren<TextMeshProUGUI>().fontSize = fontSize;
    }

    private static void SetColors(Transform target, Color color)
    {
        var button = target.GetComponent<Button>();
        var colors = button.colors;
        colors.normalColor = color;
        colors.selectedColor = color;
        colors.highlightedColor = Color.Lerp(color, WorldPanelBuilder.Accent, 0.6f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.35f);
        button.colors = colors;
    }

    private static void BuildCredits(RectTransform content)
    {
        AddCreditsColumn(content, "Left", CreditsText.LeftColumn, 0f, 0.5f);
        AddCreditsColumn(content, "Right", CreditsText.RightColumn, 0.5f, 1f);
    }

    private static void AddCreditsColumn(RectTransform content, string name, string text, float minX, float maxX)
    {
        var column = WorldPanelBuilder.CreateText(name, content, text, 30f, Vector2.zero, Vector2.zero);
        var rect = column.rectTransform;
        rect.anchorMin = new Vector2(minX, 0f);
        rect.anchorMax = new Vector2(maxX, 1f);
        rect.offsetMin = new Vector2(40f, 0f);
        rect.offsetMax = new Vector2(-40f, 0f);
        column.alignment = TextAlignmentOptions.Top;
        column.fontStyle = FontStyles.Normal;
        column.lineSpacing = 0f;
    }

    // A copy of the main panel keeps the scene's title and button style; only the Quit button stays, as Back.
    // Wide panels (tutorial, credits) use most of the canvas and a smaller Back button.
    private GameObject CreateSubPanel(string panelName, string title, string subtitle, out RectTransform content, bool wide = false)
    {
        var panel = Instantiate(mainPanel, mainPanel.transform.parent);
        panel.name = panelName;
        SetLabel(panel.transform.Find("Title"), title);
        SetLabel(panel.transform.Find("Subtitle"), subtitle);

        var buttons = panel.transform.Find("Buttons");
        var toRemove = new List<GameObject>();
        foreach (Transform child in buttons)
        {
            if (child.name != "Button_Quit") toRemove.Add(child.gameObject);
        }
        foreach (var child in toRemove)
        {
            child.SetActive(false);
            WorldPanelBuilder.DestroyAnywhere(child);
        }

        var backButton = buttons.Find("Button_Quit");
        backButton.name = "Button_Back";
        SetupButton(backButton, "Back");
        built.backButtons.Add(backButton.GetComponent<Button>());

        content = WorldPanelBuilder.CreateRect("Content", buttons, Vector2.zero, Vector2.zero);
        var contentLayout = content.gameObject.AddComponent<LayoutElement>();
        contentLayout.preferredHeight = wide ? WideSubPanelContentHeight : SubPanelContentHeight;
        content.SetSiblingIndex(0);

        if (wide)
        {
            var buttonsRect = (RectTransform)buttons;
            var layout = buttons.GetComponent<VerticalLayoutGroup>();
            buttonsRect.sizeDelta = new Vector2(WideSubPanelWidth, WideSubPanelContentHeight + layout.spacing + WideBackButtonHeight);
            buttonsRect.anchoredPosition += new Vector2(0f, WideSubPanelOffsetY);
            // the content spans the panel, the back button keeps a normal width
            layout.childForceExpandWidth = false;
            contentLayout.preferredWidth = WideSubPanelWidth;
            var backLayout = backButton.GetComponent<LayoutElement>();
            backLayout.preferredHeight = WideBackButtonHeight;
            backLayout.preferredWidth = SubPanelWidth * 0.6f;
            backButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 60f;
        }

        panel.SetActive(false);
        return panel;
    }

    private static Button AddMainButton(Transform template, string name, string label)
    {
        var button = Instantiate(template, template.parent);
        button.name = name;
        button.SetSiblingIndex(template.GetSiblingIndex());
        SetupButton(button, label);
        return button.GetComponent<Button>();
    }

    private static void SetLabel(Transform target, string text)
    {
        if (target != null && target.TryGetComponent<TextMeshProUGUI>(out var label))
        {
            label.text = text;
        }
    }

    // Drops the persistent listeners copied from the template button; ConnectButtons adds the real action.
    private static void SetupButton(Transform target, string label)
    {
        var button = target.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        target.GetComponentInChildren<TextMeshProUGUI>().text = label;
    }
}
