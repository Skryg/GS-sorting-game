using UnityEditor;
using UnityEngine;

// Music tracks are long (the title track is 7 minutes), so they are streamed instead of decompressed into memory.
public class MusicImportSettings : AssetPostprocessor
{
    private const string MusicFolder = "Assets/Audio/Resources/Audio/music/";

    private void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(MusicFolder)) return;

        var importer = (AudioImporter)assetImporter;
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.Streaming;
        importer.defaultSampleSettings = settings;
    }
}
