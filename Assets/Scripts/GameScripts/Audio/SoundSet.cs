using UnityEngine;

// All clips from one Resources folder; picks a random one, never the same twice in a row.
public class SoundSet
{
    private readonly AudioClip[] clips;
    private int lastIndex = -1;

    public float Volume { get; }
    public float PitchVariance { get; }

    public SoundSet(string resourcesPath, float volume, float pitchVariance)
    {
        clips = Resources.LoadAll<AudioClip>(resourcesPath);
        Volume = volume;
        PitchVariance = pitchVariance;

        if (clips.Length == 0)
        {
            Debug.LogWarning($"No audio clips found in Resources/{resourcesPath}");
        }
    }

    public AudioClip Next()
    {
        if (clips.Length == 0) return null;

        int index;
        if (clips.Length == 1 || lastIndex < 0)
        {
            index = Random.Range(0, clips.Length);
        }
        else
        {
            index = Random.Range(0, clips.Length - 1);
            if (index >= lastIndex) index++;
        }

        lastIndex = index;
        return clips[index];
    }

    public float NextPitch()
    {
        return 1f + Random.Range(-PitchVariance, PitchVariance);
    }
}
