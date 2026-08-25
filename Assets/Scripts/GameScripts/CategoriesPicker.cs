using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CategoriesPicker : MonoBehaviour
{
    // Start is called before the first frame update
    private Dictionary<ObjectCategory, List<GameObject>> sortedDatabase = new();
    private List<ObjectCategory> selectedCategories = new();
    private Dictionary<ObjectCategory, Color> categoryColorMapping = new();
    public IEnumerable<ObjectCategory> SelectedCategories => selectedCategories;

    private const int MaxCategoriesSelected = 4;

    void Start()
    {
        BuildDatabase();
        SelectMaxCategories();
        AssociateCategoriesToColors();
        var boardManager = GameObject.FindObjectOfType<BoardManager>();
        foreach (var pair in categoryColorMapping)
        {
            boardManager.AssignSpriteToColor(pair.Key.objectIcon, pair.Value);
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

    public Color GetColorForCategory(ObjectCategory category)
    {
        if (categoryColorMapping.ContainsKey(category))
        {
            return categoryColorMapping[category];
        }
        Debug.LogWarning($"No color mapping found for category: {category.name}");
        return Color.Red; // Default color
    }

    private void AssociateCategoriesToColors()
    {
        categoryColorMapping = new Dictionary<ObjectCategory, Color>();
        for (int i = 0; i < selectedCategories.Count; i++)
        {
            categoryColorMapping[selectedCategories[i]] = (Color)i;
        }
    }

    private void SelectMaxCategories()
    {
        selectedCategories = GetNRandomCategories(MaxCategoriesSelected);
    }

    private List<ObjectCategory> GetNRandomCategories(int n)
    {
        List<ObjectCategory> categories = sortedDatabase.Keys.ToList();
        List<ObjectCategory> randomCategories = new List<ObjectCategory>();
        for (int i = 0; i < n && categories.Count > 0; i++)
        {

            int index = Random.Range(0, categories.Count);
            randomCategories.Add(categories[index]);
            categories.RemoveAt(index); // Remove to avoid duplicates
        }
        return randomCategories;
    }

    private void BuildDatabase()
    {
        GameObject[] allPrefabs = Resources.LoadAll<GameObject>("Prefabs/GameObjects");

        foreach (GameObject prefab in allPrefabs)
        {
            ObjectIdentifier id = prefab.GetComponent<ObjectIdentifier>();

            // Make sure the script exists AND a category is assigned
            if (id != null && id.category != null)
            {
                if (!sortedDatabase.ContainsKey(id.category))
                {
                    sortedDatabase[id.category] = new List<GameObject>();
                }

                sortedDatabase[id.category].Add(prefab);

                // id.category.name reads the actual filename of the ScriptableObject
                Debug.Log($"Registered: {id.typeName} into category {id.category.name}");
            }
        }
    }
}
