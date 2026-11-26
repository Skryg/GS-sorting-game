using System.Collections.Generic;

// Choices made in the start menu that carry over to MainGame (static fields survive scene loads).
public static class GameSetup
{
    // Empty when nothing was chosen in the menu, the categories are then picked at random.
    public static readonly Dictionary<BinColor, ObjectCategory> CategoryByColor = new();

    public static bool MonstersEnabled = true;

    public static bool HasCategorySelection => CategoryByColor.Count > 0;
}
