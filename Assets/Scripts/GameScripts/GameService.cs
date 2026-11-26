using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

[RequireComponent(typeof(CategoriesPicker))]
[RequireComponent(typeof(ObjectSpawner))]
public class GameService : MonoBehaviour
{
    public const int PointsForCorrectMatch = 10;
    public const int PointsForInvalidMatch = -20;
    public const int PointsForObjectFallen = -10;
    public const int PointsForObjectShot = -10;
    public const int PointsForMockShot = 5;
    public const int PointsForBombDisposed = 10;
    public const int PointsForBombExploded = -20;

    public const float MaxHealth = 1.0f;
    public const float HealthRegenerationRate = 0.05f;
    public const float HealthForCorrectMatch = 0;
    public const float HealthForInvalidMatch = -0.1f;
    public const float HealthForObjectFallen = -0.1f;
    public const float HealthForObjectShot = -0.1f;
    public const float HealthForBombExploded = -0.25f;

    public const float StartSpawningOffsetInSeconds = 3f;

    public static GameService Instance { get; private set; }

    public event Action GameOver;
    public bool IsGameOver { get; private set; }
    public event Action<bool> PauseChanged;
    public bool IsPaused { get; private set; }
    public int Score => scoreUpdater.Value;
    // 1-based place in the highscore table after game over, 0 when the score did not make it.
    public int LastPlacement { get; private set; }

    private CategoriesPicker categoriesPicker;
    private ObjectSpawner objectSpawner;
    private HealthUpdater healthUpdater;
    private ScoreUpdater scoreUpdater;
    private readonly List<LocomotionProvider> pausedLocomotion = new();

    private class HealthUpdater
    {
        private readonly BoardPainter boardPainter;
        private float health = MaxHealth;

        public event Action Depleted;

        public HealthUpdater(BoardPainter boardPainter)
        {
            this.boardPainter = boardPainter;
        }

        public float Value
        {
            get => health;
            set
            {
                float previous = health;
                health = Mathf.Clamp(value, 0, MaxHealth);
                if (boardPainter != null) boardPainter.HealthUpdate(health);
                if (health <= 0 && previous > 0) Depleted?.Invoke();
            }
        }
    }

    private class ScoreUpdater
    {
        private readonly BoardPainter boardPainter;
        private int score;

        public ScoreUpdater(BoardPainter boardPainter)
        {
            this.boardPainter = boardPainter;
        }

        public int Value
        {
            get => score;
            set
            {
                score = value;
                if (boardPainter != null) boardPainter.UpdateScore(score);
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        categoriesPicker = GetComponent<CategoriesPicker>();
        objectSpawner = GetComponent<ObjectSpawner>();
    }

    private void Start()
    {
        var boardPainter = FindAnyObjectByType<BoardPainter>(FindObjectsInactive.Include);
        if (boardPainter == null) Debug.LogError("No BoardPainter in the scene, score and health will not be shown.");

        healthUpdater = new HealthUpdater(boardPainter);
        healthUpdater.Depleted += EndGame;
        scoreUpdater = new ScoreUpdater(boardPainter);
        gameObject.AddComponent<GameMusicDirector>();
    }

    private void Update()
    {
        if (IsGameOver || IsPaused) return;
        healthUpdater.Value += Time.deltaTime * HealthRegenerationRate;
    }

    // Unlike game over, a pause has to freeze everything mid-motion (physics, belt, enemies, projectiles),
    // so time is stopped. Head tracking and the UI keep working; locomotion is blocked explicitly.
    public void SetPaused(bool paused)
    {
        if (IsGameOver || paused == IsPaused) return;
        IsPaused = paused;

        Time.timeScale = paused ? 0f : 1f;
        AudioListener.pause = paused;

        if (paused)
        {
            foreach (var provider in FindObjectsByType<LocomotionProvider>(FindObjectsSortMode.None))
            {
                provider.enabled = false;
                pausedLocomotion.Add(provider);
            }
        }
        else
        {
            foreach (var provider in pausedLocomotion)
            {
                if (provider != null) provider.enabled = true;
            }
            pausedLocomotion.Clear();
        }

        PauseChanged?.Invoke(paused);
    }

    // Gameplay is stopped explicitly: Time.timeScale = 0 would also freeze time-based XR smoothing and locomotion.
    private void EndGame()
    {
        if (IsGameOver) return;
        IsGameOver = true;

        objectSpawner.enabled = false;
        var gameStaging = GetComponent<GameStaging>();
        if (gameStaging != null)
        {
            gameStaging.enabled = false;
            gameStaging.CancelInvoke();
        }
        foreach (var enemySpawner in FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
        {
            enemySpawner.enabled = false;
        }
        var belt = FindAnyObjectByType<ConveyorBelt>();
        if (belt != null)
        {
            belt.StopAllCoroutines();
            belt.SetConveyorSpeed(0f);
        }

        LastPlacement = Highscores.Submit(Score);
        GameOver?.Invoke();
    }

    // GameService survives scene loads (DontDestroyOnLoad), so it has to go away before a new round starts.
    public void LoadScene(string sceneName)
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Destroy(gameObject);
        SceneManager.LoadScene(sceneName);
    }

    public void CollectTrashObject(ObjectIdentifier identifier, BinColor color)
    {
        if (categoriesPicker.GetColorForCategory(identifier.category) == color) RewardForColorMatch();
        else PunishForColorMismatch();
    }

    public void RewardForColorMatch() => ChangeScore(PointsForCorrectMatch, HealthForCorrectMatch, Sfx.PointsSuccess);

    public void PunishForColorMismatch() => ChangeScore(PointsForInvalidMatch, HealthForInvalidMatch, Sfx.PointsError);

    public void PunishForObjectFallen() => ChangeScore(PointsForObjectFallen, HealthForObjectFallen, Sfx.PointsError);

    public void PunishForObjectShot() => ChangeScore(PointsForObjectShot, HealthForObjectShot, Sfx.PointsError);

    public void RewardForMockShot() => ChangeScore(PointsForMockShot, 0f, Sfx.PointsSuccess);

    public void RewardForBombDisposed() => ChangeScore(PointsForBombDisposed, 0f, Sfx.PointsSuccess);

    public void PunishForBombExploded() => ChangeScore(PointsForBombExploded, HealthForBombExploded, Sfx.PointsError);

    public void PlayerHit(float damage)
    {
        if (IsGameOver || IsPaused) return;
        healthUpdater.Value -= damage;
    }

    private void ChangeScore(int points, float health, Sfx sound)
    {
        if (IsGameOver || IsPaused) return;
        scoreUpdater.Value += points;
        healthUpdater.Value += health;
        AudioManager.Play2D(sound);
    }
}
