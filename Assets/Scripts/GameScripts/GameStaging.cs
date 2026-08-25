using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameStaging : MonoBehaviour
{
    private ConveyorBelt conveyorBelt;
    private ObjectSpawner objectSpawner;

    private const float SpawningChangeDelayInSeconds = 1f;


    public struct GameStage
    {
        public float StageDuration { get; set; }
        public float SpawningInterval { get; set; }
        public float ConveyorBeltSpeed { get; set; }

    }

    public List<GameStage> gameStages = new List<GameStage>
    {
        new GameStage { StageDuration = 30f, SpawningInterval = 2f, ConveyorBeltSpeed = 1f },
        new GameStage { StageDuration = 60f, SpawningInterval = 1.5f, ConveyorBeltSpeed = 1.5f },
        new GameStage { StageDuration = 90f, SpawningInterval = 1f, ConveyorBeltSpeed = 2f },
        new GameStage { StageDuration = float.PositiveInfinity, SpawningInterval = 0.5f, ConveyorBeltSpeed = 2.5f }
    };

    private int currentStageIndex = 0;
    private float stageTimer = 0f;

    private void RunNextGameStageIfPresent()
    {
        if (currentStageIndex < gameStages.Count)
        {
            currentStageIndex++;
            UpdateSpawningIntervalAndConveyorBeltSpeed();
        }
        else
        {
            Debug.LogWarning("No more game stages available.");
        }
    }

    private void UpdateSpawningIntervalAndConveyorBeltSpeed()
    {
        if (currentStageIndex < gameStages.Count)
        {
            GameStage currentStage = gameStages[currentStageIndex];
            UpdateConveyorBeltSpeed();
            Invoke(nameof(UpdateSpawningInterval), SpawningChangeDelayInSeconds);
        }
    }

    private void UpdateConveyorBeltSpeed()
    {
        GameStage currentStage = gameStages[currentStageIndex];
        conveyorBelt.SetConveyorSpeed(currentStage.ConveyorBeltSpeed);
    }

    private void UpdateSpawningInterval()
    {
        GameStage currentStage = gameStages[currentStageIndex];
        objectSpawner.SpawnIntervalInSeconds = currentStage.SpawningInterval;
    }


    private void Awake()
    {
        objectSpawner = GetComponent<ObjectSpawner>();
    }

    // Start is called before the first frame update
    void Start()
    {
        conveyorBelt = FindObjectOfType<ConveyorBelt>();
        UpdateSpawningIntervalAndConveyorBeltSpeed();
    }

    // Update is called once per frame
    void Update()
    {
        stageTimer += Time.deltaTime;

        if (currentStageIndex < gameStages.Count)
        {
            GameStage currentStage = gameStages[currentStageIndex];
            if (stageTimer >= currentStage.StageDuration)
            {
                stageTimer = 0f;
                RunNextGameStageIfPresent();
            }
        }

    }
}


