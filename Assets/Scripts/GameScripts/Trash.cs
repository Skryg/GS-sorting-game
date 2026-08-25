using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class Trash : MonoBehaviour
{
    public Color color;

    public void Collect(ObjectIdentifier identifier)
    {
        GameService.Instance.CollectTrashObject(identifier, color);
    }
}
