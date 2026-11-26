using System.Collections.Generic;
using System.Text;

// Rich text for a TextMeshPro label: one row per place, columns aligned with <pos> tags.
public static class HighscoreTableText
{
    private const string HighlightColor = "#FFD54A";

    public static string Build(IReadOnlyList<Highscores.Entry> entries, int highlightPlace = 0)
    {
        var text = new StringBuilder();
        for (int i = 0; i < Highscores.MaxEntries; i++)
        {
            int place = i + 1;
            string row = i < entries.Count
                ? $"{place}.<pos=18%>{entries[i].score}<pos=55%>{entries[i].date}"
                : $"{place}.<pos=18%>-";

            if (place == highlightPlace)
            {
                row = $"<color={HighlightColor}>{row}</color>";
            }
            text.AppendLine(row);
        }
        return text.ToString();
    }
}
