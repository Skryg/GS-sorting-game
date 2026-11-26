using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Setup screen before a round: assigns categories to the four bin colors and turns the monsters on or off.
// The player picks a color slot, then a category from the list.
public class GameSetupMenu : MonoBehaviour
{
    [System.Serializable]
    public class ColorSlot
    {
        public BinColor color;
        public Button button;
        public Image icon;
        public TextMeshProUGUI label;
        public GameObject selection;
        [System.NonSerialized] public ObjectCategory category;
    }

    private const float LabelMargin = 12f;
    private const float SlotLabelMaxSize = 28f;
    private const float TileLabelMaxSize = 25f;

    public List<ColorSlot> slots = new();
    public CategoryButtonView categoryButtonTemplate;
    public Toggle monstersToggle;

    private List<ObjectCategory> availableCategories = new();
    private readonly List<CategoryButtonView> categoryButtons = new();
    private ColorSlot activeSlot;

    private void Start()
    {
        LoadAvailableCategories();

        foreach (var slot in slots)
        {
            FitLabel(slot.label, SlotLabelMaxSize);
            var s = slot;
            s.button.onClick.AddListener(() => SelectSlot(s));
        }
        CreateCategoryButtons();

        monstersToggle.isOn = GameSetup.MonstersEnabled;

        if (GameSetup.HasCategorySelection)
        {
            foreach (var slot in slots)
            {
                GameSetup.CategoryByColor.TryGetValue(slot.color, out slot.category);
                // A remembered category may have lost all its objects (e.g. a removed prefab).
                if (slot.category != null && !availableCategories.Contains(slot.category))
                {
                    slot.category = null;
                }
            }
        }
        else
        {
            Randomize();
        }

        SelectSlot(slots[0]);
    }

    // Categories come from the catalog, so the menu does not load any splat prefabs.
    private void LoadAvailableCategories()
    {
        var catalog = GameObjectCatalog.Load();
        availableCategories = catalog == null
            ? new List<ObjectCategory>()
            : catalog.Categories.OrderBy(category => category.name).ToList();
    }

    private void CreateCategoryButtons()
    {
        categoryButtonTemplate.gameObject.SetActive(false);
        FitLabel(categoryButtonTemplate.nameLabel, TileLabelMaxSize);
        foreach (var category in availableCategories)
        {
            var view = Instantiate(categoryButtonTemplate, categoryButtonTemplate.transform.parent);
            view.name = $"Category_{category.name}";
            view.category = category;
            view.icon.sprite = category.objectIcon;
            view.icon.enabled = category.objectIcon != null;
            view.nameLabel.text = category.name;
            view.button.onClick.AddListener(() => AssignToActiveSlot(view.category));
            view.gameObject.SetActive(true);
            categoryButtons.Add(view);
        }
    }

    // Keeps long category names inside the tile frame.
    private static void FitLabel(TextMeshProUGUI label, float maxSize)
    {
        if (label == null) return;
        label.margin = new Vector4(LabelMargin, 0f, LabelMargin, 0f);
        label.enableAutoSizing = true;
        label.fontSizeMax = maxSize;
        label.fontSizeMin = Mathf.Min(label.fontSizeMin, 14f);
    }

    public void SelectSlot(ColorSlot slot)
    {
        activeSlot = slot;
        Refresh();
    }

    // If another color already has the category, the two colors swap categories.
    public void AssignToActiveSlot(ObjectCategory category)
    {
        if (activeSlot == null || activeSlot.category == category) return;

        var other = slots.FirstOrDefault(s => s.category == category);
        if (other != null)
        {
            other.category = activeSlot.category;
        }
        activeSlot.category = category;
        Refresh();
    }

    public void Randomize()
    {
        var shuffled = availableCategories.OrderBy(_ => Random.value).ToList();
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i].category = i < shuffled.Count ? shuffled[i] : null;
        }
        Refresh();
    }

    public void ApplyToGameSetup()
    {
        GameSetup.CategoryByColor.Clear();
        foreach (var slot in slots)
        {
            if (slot.category != null)
            {
                GameSetup.CategoryByColor[slot.color] = slot.category;
            }
        }
        GameSetup.MonstersEnabled = monstersToggle.isOn;
    }

    private void Refresh()
    {
        foreach (var slot in slots)
        {
            bool hasCategory = slot.category != null;
            slot.icon.enabled = hasCategory && slot.category.objectIcon != null;
            if (hasCategory)
            {
                slot.icon.sprite = slot.category.objectIcon;
            }
            slot.label.text = hasCategory ? slot.category.name : "-";
            slot.selection.SetActive(slot == activeSlot);
        }

        foreach (var view in categoryButtons)
        {
            var owner = slots.FirstOrDefault(s => s.category == view.category);
            view.colorBadge.enabled = owner != null;
            if (owner != null)
            {
                view.colorBadge.color = ToUnityColor(owner.color);
            }
            view.selection.SetActive(owner != null && owner == activeSlot);
        }
    }

    // The same colors as on the LegendBoard in MainGame.
    public static Color ToUnityColor(BinColor color)
    {
        switch (color)
        {
            case BinColor.Red: return new Color(0.874f, 0.263f, 0.135f);
            case BinColor.Green: return new Color(0.256f, 0.799f, 0.158f);
            case BinColor.Yellow: return new Color(0.975f, 0.837f, 0.101f);
            case BinColor.Blue: return new Color(0.172f, 0.306f, 0.925f);
            default: return Color.white;
        }
    }
}
