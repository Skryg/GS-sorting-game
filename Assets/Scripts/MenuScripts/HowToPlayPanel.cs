using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Paged tutorial shown from the start menu. Texts follow the actual rules in GameService, GameStaging and the weapon.
public class HowToPlayPanel : MonoBehaviour
{
    private struct Page
    {
        public string title;
        public string body;
    }

    private static readonly Page[] Pages =
    {
        new Page
        {
            title = "Sort the belt",
            body = "Items ride the conveyor belt toward you.\n" +
                   "Drop every item into the bin whose <color=#5FD3E6>color</color> matches its category.\n" +
                   "The board on the wall shows which category belongs to which color.\n" +
                   "You choose the categories before the game starts."
        },
        new Page
        {
            title = "Grab and drop",
            body = "Point at an item - the laser shows how far you can reach.\n" +
                   "Hold the <color=#5FD3E6>grip</color> button to pick it up, let go above a bin to drop it.\n" +
                   "Items can't be thrown, carry them to the bin.\n" +
                   "Some items are soft and bend while you hold them."
        },
        new Page
        {
            title = "Points and health",
            body = $"Right bin: <color=#6BE38A>+{GameService.PointsForCorrectMatch} points</color>.\n" +
                   $"Wrong bin: <color=#F07B7B>{GameService.PointsForInvalidMatch} points</color> and some health.\n" +
                   $"Item falls on the floor: <color=#F07B7B>{GameService.PointsForObjectFallen} points</color> and some health.\n" +
                   "Health slowly comes back. When it runs out, the game is over.\n" +
                   "The longer you last, the faster the belt and the more items arrive."
        },
        new Page
        {
            title = "Special items",
            body = $"<color=#9FE6FF>Ice</color> (bluish) starts melting once you pick it up. Sort it within {IceItem.MeltSeconds:0} seconds,\n" +
                   $"or it melts away in your hand: <color=#F07B7B>{GameService.PointsForObjectFallen} points</color>, like a fallen item.\n" +
                   $"A <color=#F07B7B>bomb</color> blinks red, faster and faster. Put it into any bin within {BombItem.FuseSeconds:0} seconds: <color=#6BE38A>+{GameService.PointsForBombDisposed} points</color>.\n" +
                   $"If it explodes or hits the floor: <color=#F07B7B>{GameService.PointsForBombExploded} points</color> and a lot of health.\n" +
                   "Some items are <color=#5FD3E6>fakes</color>: they crumble in your hand and cost nothing."
        },
        new Page
        {
            title = "Shooting items",
            body = "Plasma bolts hit the items on the belt too, and the item crumbles.\n" +
                   $"Bomb: <color=#6BE38A>+{GameService.PointsForBombDisposed} points</color>, it is defused.\n" +
                   $"Fake: <color=#6BE38A>+{GameService.PointsForMockShot} points</color>.\n" +
                   $"Any other item, ice too: <color=#F07B7B>{GameService.PointsForObjectShot} points</color> and some health.\n" +
                   "A fake looks just like a real item, so think before you shoot."
        },
        new Page
        {
            title = "Spiders",
            body = "With monsters on, spiders crawl out and attack you.\n" +
                   "Press <color=#5FD3E6>A</color> on the right controller to take out the plasma gun,\n" +
                   "pull the <color=#5FD3E6>trigger</color> to shoot. Two hits kill a spider.\n" +
                   "Press A again to put the gun away and grab items with that hand."
        },
        new Page
        {
            title = "Controls",
            body = "<color=#5FD3E6>Left stick</color> - move around your station\n" +
                   "<color=#5FD3E6>Right stick</color> - turn\n" +
                   "<color=#5FD3E6>Grip</color> - grab items\n" +
                   "<color=#5FD3E6>Trigger</color> - shoot, click menu buttons\n" +
                   "<color=#5FD3E6>Menu button</color> (left controller) - pause"
        }
    };

    private const float NavHeight = 110f;
    private const float NavButtonWidth = 340f;

    // serialized because the panel is saved in the scene
    [SerializeField] private TextMeshProUGUI pageTitle;
    [SerializeField] private TextMeshProUGUI pageBody;
    [SerializeField] private TextMeshProUGUI pageCounter;
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;
    private int pageIndex;

    // buttonTemplate is a scene button, so the arrows share the menu's rounded style. Works in edit mode too.
    public static HowToPlayPanel Build(RectTransform content, Transform buttonTemplate)
    {
        var panel = content.gameObject.AddComponent<HowToPlayPanel>();

        panel.pageTitle = WorldPanelBuilder.CreateText("PageTitle", content, "", 72f, Vector2.zero, new Vector2(0f, 100f));
        Anchor(panel.pageTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        panel.pageTitle.color = WorldPanelBuilder.Accent;

        panel.pageBody = WorldPanelBuilder.CreateText("PageBody", content, "", 42f, Vector2.zero, Vector2.zero);
        var body = panel.pageBody.rectTransform;
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(40f, NavHeight + 20f);
        body.offsetMax = new Vector2(-40f, -110f);
        panel.pageBody.fontStyle = FontStyles.Normal;
        panel.pageBody.lineSpacing = 12f;

        panel.previousButton = CreateNavButton(buttonTemplate, content, "Button_Previous", "< Previous", -1f);
        panel.nextButton = CreateNavButton(buttonTemplate, content, "Button_Next", "Next >", 1f);

        panel.pageCounter = WorldPanelBuilder.CreateText("PageCounter", content, "", 44f, Vector2.zero, new Vector2(300f, NavHeight));
        Anchor(panel.pageCounter.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));

        panel.ShowPage(0);
        return panel;
    }

    // Start, not Awake: at runtime Build fills the references only after AddComponent has run Awake.
    private void Start()
    {
        previousButton.onClick.AddListener(Previous);
        nextButton.onClick.AddListener(Next);
        ShowPage(0);
    }

    private void OnEnable()
    {
        // every visit starts from the first page
        if (pageBody != null) ShowPage(0);
    }

    private void Previous() => ShowPage(pageIndex - 1);

    private void Next() => ShowPage(pageIndex + 1);

    private void ShowPage(int index)
    {
        pageIndex = Mathf.Clamp(index, 0, Pages.Length - 1);
        pageTitle.text = Pages[pageIndex].title;
        pageBody.text = Pages[pageIndex].body;
        pageCounter.text = $"{pageIndex + 1} / {Pages.Length}";
        previousButton.interactable = pageIndex > 0;
        nextButton.interactable = pageIndex < Pages.Length - 1;
    }

    private static Button CreateNavButton(Transform template, RectTransform parent, string name, string label, float side)
    {
        var copy = Instantiate(template, parent);
        copy.name = name;
        if (copy.TryGetComponent<LayoutElement>(out var layout)) WorldPanelBuilder.DestroyAnywhere(layout);

        var rect = (RectTransform)copy;
        var anchor = new Vector2(side < 0f ? 0f : 1f, 0f);
        Anchor(rect, anchor, anchor, anchor);
        rect.sizeDelta = new Vector2(NavButtonWidth, NavHeight);
        rect.anchoredPosition = Vector2.zero;

        // drop the template's persistent listener; Start adds the page handlers
        var button = copy.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        var text = copy.GetComponentInChildren<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 48f;
        return button;
    }

    private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.anchoredPosition = Vector2.zero;
    }
}
