using System.Collections;
using UnityEngine;

// Alternates calm (pulse) and urgent music in phases of random length; urgent phases also spawn enemies faster.
// Added at runtime by GameService.
public class GameMusicDirector : MonoBehaviour
{
    private const float MinPhaseSeconds = 30f;
    private const float MaxPhaseSeconds = 60f;
    private const float CrossfadeSeconds = 3f;
    private const float UrgentSpawnMultiplier = 2f;
    private const float GameOverFadeSeconds = 1f;
    private const float TitleFadeInSeconds = 2f;
    private const float AmbienceVolume = 0.8f;

    public bool IsUrgent { get; private set; }

    private GameService gameService;
    private AudioSource ambience;
    private float phaseTimer;

    private void Start()
    {
        gameService = GetComponent<GameService>();
        gameService.GameOver += OnGameOver;
        ambience = AudioManager.AttachLoop(gameObject, AudioManager.Instance.AmbienceLoop, false);
        StartPhase(false);
    }

    private void OnDestroy()
    {
        if (gameService != null) gameService.GameOver -= OnGameOver;
    }

    // Scaled time, so phases stop together with the music during a pause.
    private void Update()
    {
        ambience.volume = AmbienceVolume * AudioManager.SfxVolume;

        if (gameService.IsGameOver) return;

        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f)
        {
            StartPhase(!IsUrgent);
        }
    }

    private void StartPhase(bool urgent)
    {
        IsUrgent = urgent;

        var audio = AudioManager.Instance;
        var clip = urgent ? audio.UrgentMusic : audio.PulseMusic;
        audio.PlayMusic(clip, false, CrossfadeSeconds);
        if (urgent) AudioManager.Play2D(Sfx.UrgentAlarm);

        // Every phase starts its track from the beginning and must end before the track does.
        float maxPhase = clip != null ? Mathf.Min(MaxPhaseSeconds, clip.length - CrossfadeSeconds) : MaxPhaseSeconds;
        phaseTimer = Random.Range(MinPhaseSeconds, Mathf.Max(MinPhaseSeconds, maxPhase));

        foreach (var spawner in FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
        {
            spawner.RateMultiplier = urgent ? UrgentSpawnMultiplier : 1f;
        }
    }

    private void OnGameOver()
    {
        StartCoroutine(GameOverSequence());
    }

    private IEnumerator GameOverSequence()
    {
        var audio = AudioManager.Instance;
        audio.StopMusic(GameOverFadeSeconds);
        yield return new WaitForSecondsRealtime(GameOverFadeSeconds);

        float jingle = AudioManager.Play2D(gameService.LastPlacement > 0 ? Sfx.EndBest : Sfx.EndFail);
        yield return new WaitForSecondsRealtime(jingle);

        audio.PlayMusic(audio.TitleMusic, true, TitleFadeInSeconds);
    }
}
