using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using UnityEngine;

[RequireComponent(typeof(CategoriesPicker))]
[RequireComponent(typeof(ObjectSpawner))]
public class GameService : MonoBehaviour
{
    public static GameService Instance { get; private set; }
    private CategoriesPicker categoriesPicker;
    private ObjectSpawner objectSpawner;
    private ConveyorBelt conveyorBelt;

    private BoardPainter boardPainter;

    public const int PointsForCorrectMatch = 10;
    public const int PointsForInvalidMatch = -20;
    public const int PointsForObjectFallen = -30;

    public const float MaxHealth = 1.0f;
    public const float HealthRegenerationRate = 0.05f;
    public const float HealthForCorrectMatch = 0;
    public const float HealthForInvalidMatch = -0.1f;
    public const float HealthForObjectFallen = -0.2f;

    public const float StartSpawningOffsetInSeconds = 3f;

    private class HealthUpdater
    {
        private float health = MaxHealth;
        private readonly BoardPainter _boardPainter;
        public HealthUpdater(BoardPainter boardPainter)
        {
            _boardPainter = boardPainter;
        }

        public float Value
        {
            get { return health; }
            set
            {
                health = Mathf.Clamp(value, 0, MaxHealth);
                if (_boardPainter != null)
                {
                    _boardPainter.HealthUpdate(health);
                }
            }
        }
    }
    private class ScoreUpdater
    {
        private int score = 0;
        private readonly BoardPainter _boardPainter;
        public ScoreUpdater(BoardPainter boardPainter)
        {
            _boardPainter = boardPainter;
        }
        public int Value
        {
            get { return score; }
            set
            {
                score = value;
                if (_boardPainter != null)
                {
                    _boardPainter.UpdateScore(score);
                }
            }
        }
    }

    private HealthUpdater healthUpdater;
    private ScoreUpdater scoreUpdater;


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        categoriesPicker = GetComponent<CategoriesPicker>();
        objectSpawner = GetComponent<ObjectSpawner>();
    }

    // Start is called before the first frame update
    void Start()
    {
        boardPainter = FindObjectOfType<BoardPainter>(true);
        if (boardPainter == null)
        {
            Debug.LogError("BoardPainter not found in the scene. Please ensure there is a BoardPainter component in the scene.");
        }
        healthUpdater = new HealthUpdater(boardPainter);
        scoreUpdater = new ScoreUpdater(boardPainter);
    }


    // Update is called once per frame
    void Update()
    {
        Regenerate();
    }

    public void CollectTrashObject(ObjectIdentifier identifier, Color color)
    {
        var categoryColor = categoriesPicker.GetColorForCategory(identifier.category);
        if (categoryColor == color)
        {
            RewardForColorMatch();
        }
        else
        {
            PunishForColorMismatch();
        }
    }


    private void Regenerate()
    {
        healthUpdater.Value += Time.deltaTime * HealthRegenerationRate;
    }

    public void RewardForColorMatch()
    {
        scoreUpdater.Value += PointsForCorrectMatch;
        healthUpdater.Value += HealthForCorrectMatch;
    }

    public void PunishForColorMismatch()
    {
        scoreUpdater.Value += PointsForInvalidMatch;
        healthUpdater.Value += HealthForInvalidMatch;
    }

    public void PunishForObjectFallen()
    {
        scoreUpdater.Value += PointsForObjectFallen;
        healthUpdater.Value += HealthForObjectFallen;
    }

    public void PlayerHit(float damage)
    {
        healthUpdater.Value -= damage;
    }

}