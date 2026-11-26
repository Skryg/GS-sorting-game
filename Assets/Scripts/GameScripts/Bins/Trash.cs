using UnityEngine;

public class Trash : MonoBehaviour
{
    public BinColor color;

    public void Collect(ObjectIdentifier identifier)
    {
        GameService.Instance.CollectTrashObject(identifier, color);
    }
}
