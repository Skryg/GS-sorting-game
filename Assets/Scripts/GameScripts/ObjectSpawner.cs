using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Security.Cryptography;

[RequireComponent(typeof(CategoriesPicker))]
public class ObjectSpawner : MonoBehaviour
{
    private CategoriesPicker categoriesPicker;
    public GameObject spawnPoint;
    public float SpawnIntervalInSeconds { get; set; } = float.PositiveInfinity;
    private float timeCounter = 0f;
    
    private void Awake()
    {
        categoriesPicker = GetComponent<CategoriesPicker>();
    }

    private void Start()
    {
        if (spawnPoint == null)
        {
            spawnPoint = GameObject.Find("SpawnPoint");
            if (spawnPoint == null)
                Debug.LogError("SpawnPoint not found in the scene. Please assign it in the inspector or create a GameObject named 'SpawnPoint'.");
        }
    }

    private void Update()
    {
        timeCounter += Time.deltaTime;
        if (timeCounter > SpawnIntervalInSeconds)
        {
            timeCounter = 0f;
            if (categoriesPicker.SelectedCategories.Count() > 0)
            {
                ObjectCategory categoryToSpawn = categoriesPicker.SelectedCategories.ElementAt(Random.Range(0, categoriesPicker.SelectedCategories.Count()));
                SpawnRandomObjectFromCategory(categoryToSpawn);
            }
        }
    }

    private Vector3 GetSpawnPositionWithRandomOffset(GameObject spawnPoint)
    {
        Vector3 randomOffset = new Vector3(Random.Range(-0.2f, 0.2f), 0, Random.Range(-0.2f, 0.2f));
        Vector3 spawnPosition = spawnPoint.transform.position + randomOffset;
        return spawnPosition;
    }

    private void SpawnRandomObjectFromCategory(ObjectCategory category)
    {
        if (spawnPoint == null)
        {
            Debug.LogError("SpawnPoint is not assigned. Please assign it in the inspector or create a GameObject named 'SpawnPoint'.");
            return;
        }


        var position = GetSpawnPositionWithRandomOffset(spawnPoint);
        var candidatePrefabs = categoriesPicker.GetPrefabsForCategory(category);
        var randomObject = candidatePrefabs.GetRandomOrNull();
        if (randomObject != null)
        {
            Instantiate(randomObject, position, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning($"No prefabs found for category: {category.name}");
        }
    }

}
