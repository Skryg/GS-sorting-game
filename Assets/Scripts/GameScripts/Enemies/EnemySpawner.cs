using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Spawns enemies as a Poisson process at the child transforms of this object.
// The rate is driven by GameStaging, so it grows with each stage.
public class EnemySpawner : MonoBehaviour
{
    public Enemy enemyPrefab;
    public Transform player;
    public int maxAliveEnemies = 3;

    // Expected number of spawns per minute (lambda of the Poisson process).
    public float SpawnRatePerMinute { get; set; } = 0f;

    // Set by GameMusicDirector during urgent music phases.
    public float RateMultiplier { get; set; } = 1f;

    private readonly List<Enemy> aliveEnemies = new();
    private readonly List<Transform> spawnPoints = new();

    private void Start()
    {
        if (!GameSetup.MonstersEnabled)
        {
            enabled = false;
            return;
        }

        if (player == null && Camera.main != null)
        {
            player = Camera.main.transform;
        }

        foreach (Transform child in transform)
        {
            spawnPoints.Add(child);
        }

        if (enemyPrefab == null || spawnPoints.Count == 0)
        {
            Debug.LogError("EnemySpawner needs an enemy prefab and at least one child spawn point.");
            enabled = false;
        }
    }

    private void Update()
    {
        // Probability of at least one arrival within dt; exact for a piecewise constant rate.
        float lambdaPerSecond = SpawnRatePerMinute * RateMultiplier / 60f;
        float spawnProbability = 1f - Mathf.Exp(-lambdaPerSecond * Time.deltaTime);
        if (Random.value >= spawnProbability) return;

        aliveEnemies.RemoveAll(enemy => enemy == null || enemy.IsDead);
        if (aliveEnemies.Count < maxAliveEnemies)
        {
            SpawnEnemy();
        }
    }

    private void SpawnEnemy()
    {
        var spawnPoint = spawnPoints.GetRandomOrNull();
        if (!NavMesh.SamplePosition(spawnPoint.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            Debug.LogWarning($"Spawn point {spawnPoint.name} is not on the NavMesh.");
            return;
        }

        var enemy = Instantiate(enemyPrefab, hit.position, spawnPoint.rotation);
        enemy.player = player;
        aliveEnemies.Add(enemy);
    }
}
