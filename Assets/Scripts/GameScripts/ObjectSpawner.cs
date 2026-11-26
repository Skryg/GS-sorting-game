using UnityEngine;

[RequireComponent(typeof(CategoriesPicker))]
public class ObjectSpawner : MonoBehaviour
{
    public GameObject spawnPoint;

    [Tooltip("How far from the hand items can be grabbed (metres). The hand rays are shortened to the same length.")]
    public float grabReach = 1.2f;

    [Tooltip("Random spawn offset across the belt (metres, each way).")]
    public float spreadAcross = 0.12f;
    [Tooltip("Random spawn offset along the belt (metres, each way). Large values let consecutive items end up in one spot.")]
    public float spreadAlong = 0.03f;

    [Header("Special items (chances per spawned item, together at most 1)")]
    [Range(0f, 1f), Tooltip("A fake that crumbles when picked up.")]
    public float mockChance = 0.08f;
    [Range(0f, 1f), Tooltip("Ice that melts once picked up.")]
    public float iceChance = 0.08f;
    [Range(0f, 1f), Tooltip("A bomb that has to go into any bin before it explodes.")]
    public float bombChance = 0.05f;

    public float SpawnIntervalInSeconds { get; set; } = float.PositiveInfinity;

    private CategoriesPicker categoriesPicker;
    private float timeSinceSpawn;

    private void Awake()
    {
        categoriesPicker = GetComponent<CategoriesPicker>();
    }

    private void Start()
    {
        if (spawnPoint == null) spawnPoint = GameObject.Find("SpawnPoint");
        if (spawnPoint == null) Debug.LogError("No spawn point: assign it in the inspector or add a GameObject named 'SpawnPoint'.", this);

        gameObject.AddComponent<GrabReach>().reach = grabReach;
    }

    private void Update()
    {
        timeSinceSpawn += Time.deltaTime;
        if (timeSinceSpawn <= SpawnIntervalInSeconds) return;
        timeSinceSpawn = 0f;

        var category = categoriesPicker.SelectedCategories.GetRandomOrNull();
        if (category != null && spawnPoint != null) Spawn(category);
    }

    private void Spawn(ObjectCategory category)
    {
        var prefab = categoriesPicker.GetPrefabsForCategory(category).GetRandomOrNull();
        if (prefab == null)
        {
            Debug.LogWarning($"No prefabs found for category: {category.name}");
            return;
        }

        var offset = new Vector3(Random.Range(-spreadAcross, spreadAcross), 0f, Random.Range(-spreadAlong, spreadAlong));
        var item = Instantiate(prefab, spawnPoint.transform.position + offset, Quaternion.identity);
        item.AddComponent<FallDetector>();
        NearGrabOnly.Apply(item, grabReach);
        HeldItemBlocker.Apply(item);
        item.AddComponent<ShootableItem>();
        MakeSpecial(item);
    }

    private void MakeSpecial(GameObject item)
    {
        float roll = Random.value;
        if ((roll -= mockChance) < 0f) item.AddComponent<MockItem>();
        else if ((roll -= iceChance) < 0f) item.AddComponent<IceItem>();
        else if ((roll -= bombChance) < 0f) item.AddComponent<BombItem>();
    }
}
