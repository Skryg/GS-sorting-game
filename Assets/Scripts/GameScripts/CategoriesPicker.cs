using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CategoriesPicker : MonoBehaviour
{
    // Start is called before the first frame update
    private Dictionary<ObjectCategory, List<GameObject>> sortedDatabase = new();
    private List<ObjectCategory> selectedCategories = new();
    private Dictionary<ObjectCategory, BinColor> categoryColorMapping = new();
    public IEnumerable<ObjectCategory> SelectedCategories => selectedCategories;

    private const int MaxCategoriesSelected = 4;

    private GameObjectCatalog catalog;

    void Start()
    {
        catalog = GameObjectCatalog.Load();
        SelectCategoriesFromSetup();
        ApplyCategoryColorMappingFromSetup();

        if (selectedCategories is null || selectedCategories.Count == 0)
        {
            Debug.Log("No categories selected. Choosing random categories.");
            SelectMaxCategories();
            AssociateCategoriesToColors();
        }
        LoadSelectedPrefabs();
        AssignSpritesToColorsInBoardManager();
    }

    private void AssignSpritesToColorsInBoardManager()
    {
        var boardManager = GameObject.FindAnyObjectByType<BoardManager>();
        foreach (var pair in categoryColorMapping)
        {
            boardManager.AssignSpriteToColor(pair.Key.objectIcon, pair.Value);
        }
    }


    private void SelectCategoriesFromSetup()
    {
        selectedCategories = GameSetup.CategoryByColor.Values.ToList();
    }

    private void ApplyCategoryColorMappingFromSetup()
    {
        categoryColorMapping = new();
        foreach (var colorCategory in GameSetup.CategoryByColor)
        {
            categoryColorMapping[colorCategory.Value] = colorCategory.Key;       
        }
    }

    public List<GameObject> GetPrefabsForCategory(ObjectCategory category)
    {
        if (sortedDatabase.ContainsKey(category))
        {
            return sortedDatabase[category];
        }

        Debug.LogWarning($"No prefabs found for category: {category.name}");
        return new List<GameObject>();
    }

    public BinColor GetColorForCategory(ObjectCategory category)
    {
        if (categoryColorMapping.ContainsKey(category))
        {
            return categoryColorMapping[category];
        }
        Debug.LogWarning($"No color mapping found for category: {category.name}");
        return BinColor.Red; // Default color
    }

    private void AssociateCategoriesToColors()
    {
        categoryColorMapping = new Dictionary<ObjectCategory, BinColor>();
        for (int i = 0; i < selectedCategories.Count; i++)
        {
            categoryColorMapping[selectedCategories[i]] = (BinColor)i;
        }
    }

    private void SelectMaxCategories()
    {
        selectedCategories = GetNRandomCategories(MaxCategoriesSelected);
    }

    private List<ObjectCategory> GetNRandomCategories(int n)
    {
        List<ObjectCategory> categories = catalog != null ? catalog.Categories : new List<ObjectCategory>();
        List<ObjectCategory> randomCategories = new List<ObjectCategory>();
        for (int i = 0; i < n && categories.Count > 0; i++)
        {

            int index = Random.Range(0, categories.Count);
            randomCategories.Add(categories[index]);
            categories.RemoveAt(index); // Remove to avoid duplicates
        }
        return randomCategories;
    }

    // Only prefabs of the categories in play are loaded; the rest never reach memory.
    private void LoadSelectedPrefabs()
    {
        sortedDatabase = new();
        if (catalog == null) return;

        foreach (var category in selectedCategories)
        {
            var prefabs = catalog.LoadPrefabs(category);
            sortedDatabase[category] = prefabs;
            Debug.Log($"Loaded {prefabs.Count} prefab(s) for category {category.name}");
        }
    }
}
