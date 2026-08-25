using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class Utils
{
    public static T GetRandomOrNull<T>(this IEnumerable<T> enumerable)
    {
        if (enumerable == null) return default(T);

        int count = enumerable.Count();
        if (count == 0) return default(T);

        int randomIndex = Random.Range(0, count);
        return enumerable.ElementAt(randomIndex);
    }
}

public enum Color
{
    Red,
    Green,
    Yellow,
    Blue
}
