using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum Sfx
{
    Click,
    Gun,
    PointsSuccess,
    PointsError,
    EndBest,
    EndFail,
    SpiderSpawn,
    SpiderAttack,
    SpiderHurt,
    SpiderDie,
    SpiderVent,
    PlayerHurt,
    UrgentAlarm,
    BeltStart,
    Crumble,
    BombBeep,
    BombExplosion
}

// Creates itself before the first scene and survives scene loads, so no scene needs to be set up for audio.
// Clips are loaded from Assets/Audio/Resources/Audio/<folder>; dropping more files into a folder adds variants.
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    private const string Root = "Audio/";
    private const float MusicBaseVolume = 1f;
    private const float MenuMusicFadeSeconds = 4f;
    private const float LoopCrossfadeSeconds = 4f;
    // Short tracks are made as seamless loops, a crossfade would only blur the beat.
    private const float MinCrossfadeLoopSeconds = 60f;
    // Long frames (scene loads, XR start-up) would otherwise finish a whole fade in a single step.
    private const float MaxFadeStepSeconds = 0.05f;
    private const string MusicVolumeKey = "MusicVolume";
    private const string SfxVolumeKey = "SfxVolume";
    private const float MinDistance3D = 1.5f;
    private const float MaxDistance3D = 30f;

    private static readonly (Sfx sfx, string folder, float volume, float pitchVariance)[] SfxDefinitions =
    {
        (Sfx.Click, "interface", 1f, 0f),
        (Sfx.Gun, "gun", 0.45f, 0.05f),
        (Sfx.PointsSuccess, "points/success", 0.7f, 0f),
        (Sfx.PointsError, "points/error", 0.7f, 0f),
        (Sfx.EndBest, "endgame/best", 0.9f, 0f),
        (Sfx.EndFail, "endgame/fail", 0.9f, 0f),
        (Sfx.SpiderSpawn, "spider/spawn", 1f, 0.05f),
        (Sfx.SpiderAttack, "spider/attack", 1f, 0.05f),
        (Sfx.SpiderHurt, "spider/hurt", 0.9f, 0.05f),
        (Sfx.SpiderDie, "spider/die", 1f, 0.05f),
        (Sfx.SpiderVent, "spider/vent", 0.8f, 0.08f),
        (Sfx.PlayerHurt, "player/hurt", 1f, 0.05f),
        (Sfx.UrgentAlarm, "alarm", 0.6f, 0f),
        (Sfx.BeltStart, "belt_start", 0.9f, 0f),
        (Sfx.Crumble, "crumble", 1f, 0.06f),
        (Sfx.BombBeep, "bomb/beep", 0.5f, 0f),
        (Sfx.BombExplosion, "bomb/explosion", 1f, 0.05f),
    };

    public AudioClip TitleMusic { get; private set; }
    public AudioClip PulseMusic { get; private set; }
    public AudioClip UrgentMusic { get; private set; }
    public AudioClip ConveyorLoop { get; private set; }
    public AudioClip AmbienceLoop { get; private set; }

    // Player settings (0..1), saved in PlayerPrefs.
    public static float MusicVolume { get; private set; } = 1f;
    public static float SfxVolume { get; private set; } = 1f;

    private readonly Dictionary<Sfx, SoundSet> sounds = new();
    private MusicChannel[] musicChannels;
    private int activeChannel;
    // Looping track that crossfades its end into its own beginning instead of jumping back.
    private AudioClip loopingClip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        // With domain reload disabled the pause flag survives leaving play mode while paused.
        AudioListener.pause = false;

        var go = new GameObject(nameof(AudioManager));
        DontDestroyOnLoad(go);
        go.AddComponent<AudioManager>();
    }

    private void Awake()
    {
        Instance = this;
        MusicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
        SfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);

        foreach (var (sfx, folder, volume, pitchVariance) in SfxDefinitions)
        {
            sounds[sfx] = new SoundSet(Root + folder, volume, pitchVariance);
        }
        TitleMusic = Resources.Load<AudioClip>(Root + "music/title");
        PulseMusic = Resources.Load<AudioClip>(Root + "music/pulse");
        UrgentMusic = Resources.Load<AudioClip>(Root + "music/urgent");
        ConveyorLoop = Resources.Load<AudioClip>(Root + "conveyor/conveyor_belt");
        AmbienceLoop = Resources.Load<AudioClip>(Root + "ambience/background_rumble");

        musicChannels = new[]
        {
            new MusicChannel(gameObject.AddComponent<AudioSource>()),
            new MusicChannel(gameObject.AddComponent<AudioSource>())
        };

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // sceneLoaded is not raised for the first scene when "Enter Play Mode Options" skip the scene reload.
    private void Start()
    {
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // Fades are frozen together with the music while the game is paused.
    private void Update()
    {
        float deltaTime = AudioListener.pause ? 0f : Mathf.Min(Time.unscaledDeltaTime, MaxFadeStepSeconds);
        foreach (var channel in musicChannels)
        {
            channel.Tick(deltaTime, MusicBaseVolume * MusicVolume);
        }

        var active = musicChannels[activeChannel].Source;
        if (loopingClip != null && active.clip == loopingClip && active.isPlaying
            && active.time >= loopingClip.length - LoopCrossfadeSeconds)
        {
            StartTrack(loopingClip, LoopCrossfadeSeconds);
        }
    }

    public static void SetMusicVolume(float volume)
    {
        MusicVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MusicVolumeKey, MusicVolume);
    }

    public static void SetSfxVolume(float volume)
    {
        SfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
    }

    public static void SaveVolumeSettings()
    {
        PlayerPrefs.Save();
    }

    // Game scenes are driven by GameMusicDirector; every other scene gets the title music.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (GameService.Instance == null)
        {
            PlayMusic(TitleMusic, true, MenuMusicFadeSeconds);
        }
    }

    public void PlayMusic(AudioClip clip, bool loop, float fadeSeconds)
    {
        if (clip == null) return;

        bool crossfadeLoop = loop && clip.length >= MinCrossfadeLoopSeconds;
        loopingClip = crossfadeLoop ? clip : null;

        var current = musicChannels[activeChannel];
        if (loop && current.Source.clip == clip && current.Source.isPlaying)
        {
            current.FadeTo(1f, fadeSeconds, false);
            return;
        }

        StartTrack(clip, fadeSeconds, loop && !crossfadeLoop);
    }

    public bool IsMusicSilent => musicChannels[0].Level <= 0f && musicChannels[1].Level <= 0f;

    public void StopMusic(float fadeSeconds)
    {
        loopingClip = null;
        musicChannels[activeChannel].FadeTo(0f, fadeSeconds, true);
    }

    private void StartTrack(AudioClip clip, float fadeSeconds, bool nativeLoop = false)
    {
        musicChannels[activeChannel].FadeTo(0f, fadeSeconds, true);

        activeChannel = 1 - activeChannel;
        var next = musicChannels[activeChannel];
        next.Source.clip = clip;
        next.Source.loop = nativeLoop;
        next.Level = 0f;
        next.Source.volume = 0f;
        next.Source.Play();
        next.FadeTo(1f, fadeSeconds, false);
    }

    // Returns the length of the played clip in seconds (0 if nothing was played).
    public static float Play2D(Sfx sfx, float delay = 0f)
    {
        return Instance != null ? Instance.Spawn(sfx, Vector3.zero, false, delay) : 0f;
    }

    public static void PlayAt(Sfx sfx, Vector3 position)
    {
        if (Instance != null) Instance.Spawn(sfx, position, true, 0f);
    }

    // For sounds repeated by one object, which would otherwise create an object per play.
    public static void PlayOn(AudioSource source, Sfx sfx, float pitch = 1f)
    {
        if (Instance == null || source == null) return;
        var set = Instance.sounds[sfx];
        var clip = set.Next();
        if (clip == null) return;
        source.pitch = pitch;
        source.PlayOneShot(clip, set.Volume * SfxVolume);
    }

    // A 3D source for PlayOn.
    public static AudioSource AttachSource(GameObject host)
    {
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        Configure3D(source);
        return source;
    }

    public static AudioSource AttachLoop(GameObject host, AudioClip clip, bool spatial = true)
    {
        var source = host.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0f;
        if (spatial) Configure3D(source);
        else source.spatialBlend = 0f;
        if (clip != null) source.Play();
        return source;
    }

    private float Spawn(Sfx sfx, Vector3 position, bool spatial, float delay)
    {
        var set = sounds[sfx];
        var clip = set.Next();
        if (clip == null) return 0f;

        var go = new GameObject($"Sfx_{sfx}");
        go.transform.position = position;
        // 2D sounds are parented to the manager, so a jingle is not cut off by a scene change.
        if (!spatial) go.transform.SetParent(transform, true);

        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = set.Volume * SfxVolume;
        source.pitch = set.NextPitch();
        source.playOnAwake = false;
        // Buttons are clicked in the pause menu, where every other sound is paused.
        source.ignoreListenerPause = sfx == Sfx.Click;
        if (spatial) Configure3D(source);
        else source.spatialBlend = 0f;

        source.PlayDelayed(delay);

        float duration = clip.length / source.pitch;
        Destroy(go, delay + duration + 0.1f);
        return duration;
    }

    private static void Configure3D(AudioSource source)
    {
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = MinDistance3D;
        source.maxDistance = MaxDistance3D;
    }

    // Level is the fade position (0..1); the source volume is the level scaled by the music volume.
    private class MusicChannel
    {
        public readonly AudioSource Source;
        public float Level;
        private float from;
        private float to;
        private float duration;
        private float elapsed;
        private bool stopWhenDone;

        public MusicChannel(AudioSource source)
        {
            Source = source;
            Source.playOnAwake = false;
            Source.spatialBlend = 0f;
            Source.volume = 0f;
        }

        public void FadeTo(float target, float seconds, bool stop)
        {
            from = Level;
            to = target;
            duration = Mathf.Max(seconds, 0.01f);
            elapsed = 0f;
            stopWhenDone = stop;
        }

        public void Tick(float deltaTime, float volume)
        {
            if (elapsed < duration)
            {
                elapsed += deltaTime;
                Level = Mathf.Lerp(from, to, elapsed / duration);
                if (elapsed >= duration && stopWhenDone)
                {
                    Source.Stop();
                }
            }
            Source.volume = Level * volume;
        }
    }
}
