using System.Collections.Generic;
using UnityEngine;

// Difficulty grows with play time: every stage's values are reached at its start
// and blended smoothly towards the next stage while it lasts.
public class GameStaging : MonoBehaviour
{
    private ConveyorBelt conveyorBelt;
    private ObjectSpawner objectSpawner;
    private EnemySpawner[] enemySpawners;

    [System.Serializable]
    public struct GameStage
    {
        [field: SerializeField] public float StageDuration { get; set; }
        [field: SerializeField] public float SpawningInterval { get; set; }
        [field: SerializeField] public float ConveyorBeltSpeed { get; set; }
        // Expected enemy spawns per minute (Poisson rate).
        [field: SerializeField] public float EnemySpawnRatePerMinute { get; set; }
        [field: SerializeField] public int MaxAliveEnemies { get; set; }
    }

    public List<GameStage> gameStages = new List<GameStage>
    {
        new GameStage { StageDuration = 50f, SpawningInterval = 5f, ConveyorBeltSpeed = 0.7f, EnemySpawnRatePerMinute = 2f, MaxAliveEnemies = 3 },
        new GameStage { StageDuration = 60f, SpawningInterval = 3.5f, ConveyorBeltSpeed = 1f, EnemySpawnRatePerMinute = 4f, MaxAliveEnemies = 4 },
        new GameStage { StageDuration = 90f, SpawningInterval = 2.2f, ConveyorBeltSpeed = 1.4f, EnemySpawnRatePerMinute = 8f, MaxAliveEnemies = 6 },
        new GameStage { StageDuration = float.PositiveInfinity, SpawningInterval = 1.5f, ConveyorBeltSpeed = 1.8f, EnemySpawnRatePerMinute = 12f, MaxAliveEnemies = 8 }
    };

    private int currentStageIndex = 0;
    private float stageTimer = 0f;

    private void Awake()
    {
        objectSpawner = GetComponent<ObjectSpawner>();
    }

    private void Start()
    {
        conveyorBelt = FindAnyObjectByType<ConveyorBelt>();
        enemySpawners = FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None);
        ApplyDifficulty();
    }

    private void Update()
    {
        if (gameStages.Count == 0) return;

        stageTimer += Time.deltaTime;
        while (currentStageIndex < gameStages.Count - 1 && stageTimer >= gameStages[currentStageIndex].StageDuration)
        {
            stageTimer -= gameStages[currentStageIndex].StageDuration;
            currentStageIndex++;
        }
        ApplyDifficulty();
    }

    private void ApplyDifficulty()
    {
        if (gameStages.Count == 0) return;

        var stage = gameStages[currentStageIndex];
        var next = currentStageIndex + 1 < gameStages.Count ? gameStages[currentStageIndex + 1] : stage;
        float t = float.IsInfinity(stage.StageDuration) ? 0f : Mathf.Clamp01(stageTimer / stage.StageDuration);

        objectSpawner.SpawnIntervalInSeconds = Mathf.Lerp(stage.SpawningInterval, next.SpawningInterval, t);
        if (conveyorBelt != null)
            conveyorBelt.SetConveyorSpeed(Mathf.Lerp(stage.ConveyorBeltSpeed, next.ConveyorBeltSpeed, t));

        float enemyRate = Mathf.Lerp(stage.EnemySpawnRatePerMinute, next.EnemySpawnRatePerMinute, t);
        foreach (var enemySpawner in enemySpawners)
        {
            if (enemySpawner == null) continue;
            enemySpawner.SpawnRatePerMinute = enemyRate;
            // a new enemy slot opens only once the next stage starts
            enemySpawner.maxAliveEnemies = stage.MaxAliveEnemies;
        }
    }
}
