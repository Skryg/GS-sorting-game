using System;
using System.Collections.Generic;
using UnityEngine;

// Top scores stored locally in PlayerPrefs.
public static class Highscores
{
    public const int MaxEntries = 6;
    private const string PrefsKey = "Highscores";

    [Serializable]
    public struct Entry
    {
        public int score;
        public string date;
    }

    [Serializable]
    private class Table
    {
        public List<Entry> entries = new();
    }

    public static List<Entry> Load()
    {
        string json = PlayerPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) return new List<Entry>();

        var table = JsonUtility.FromJson<Table>(json);
        return table?.entries ?? new List<Entry>();
    }

    // Returns the 1-based place, or 0 when the score did not make it into the table.
    // On a tie the older entry stays above the new one.
    public static int Submit(int score)
    {
        if (score <= 0) return 0;

        var entries = Load();
        int index = entries.FindIndex(entry => score > entry.score);
        if (index < 0) index = entries.Count;
        if (index >= MaxEntries) return 0;

        entries.Insert(index, new Entry { score = score, date = DateTime.Now.ToString("yyyy-MM-dd") });
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(new Table { entries = entries }));
        PlayerPrefs.Save();
        return index + 1;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Clear Highscores")]
    private static void Clear()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
        Debug.Log("Highscores cleared.");
    }
#endif
}
