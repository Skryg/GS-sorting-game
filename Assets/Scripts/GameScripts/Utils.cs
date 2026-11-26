using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class Utils
{
    // Sorted, fallen, melted and crumbling items are moved to this layer.
    private const string OutOfPlayLayer = "DraggableObjectInactive";

    public static T GetRandomOrNull<T>(this IEnumerable<T> enumerable)
    {
        if (enumerable == null) return default;
        var list = enumerable as IList<T> ?? enumerable.ToList();
        return list.Count == 0 ? default : list[Random.Range(0, list.Count)];
    }

    public static bool IsOutOfPlay(this GameObject item) => item.layer == LayerMask.NameToLayer(OutOfPlayLayer);

    public static void TakeOutOfPlay(this GameObject item) => item.layer = LayerMask.NameToLayer(OutOfPlayLayer);
}

public enum BinColor
{
    Red,
    Green,
    Yellow,
    Blue
}
