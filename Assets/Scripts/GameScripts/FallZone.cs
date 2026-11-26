using UnityEngine;

// Marks ground: an object touching any collider under this object counts as fallen (see FallDetector).
// Put it on the root of the room/floor; a child with its own FallZone can opt out (e.g. a table).
public class FallZone : MonoBehaviour
{
    public bool countsAsFall = true;
}
