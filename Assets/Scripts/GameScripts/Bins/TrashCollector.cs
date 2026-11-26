using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Trigger in a bin opening. Takes released items and hands them to the Trash for scoring.
public class TrashCollector : MonoBehaviour
{
    private const float VanishDelay = 1f;
    // Long items touch the opening while their center is still well above it or next to its edge.
    private const float OpeningHeight = 0.4f;
    private const float OpeningMargin = 0.08f;

    private Trash trash;
    private Collider[] openings;

    private void Start()
    {
        trash = GetComponentInParent<Trash>();
        openings = GetComponents<Collider>();
    }

    private void OnTriggerEnter(Collider other) => TryCollect(other);

    // an item lowered into the bin by hand is collected once it is let go
    private void OnTriggerStay(Collider other) => TryCollect(other);

    private void TryCollect(Collider other)
    {
        var item = other.gameObject;
        if (item.IsOutOfPlay() || !item.TryGetComponent<ObjectIdentifier>(out var identifier)) return;
        if (item.TryGetComponent<XRGrabInteractable>(out var grab) && grab.isSelected) return;

        // an item that only sticks through a wall from outside does not count
        Vector3 center = other.attachedRigidbody != null ? other.attachedRigidbody.worldCenterOfMass : other.bounds.center;
        if (!IsOverOpening(center)) return;

        if (item.TryGetComponent<MockItem>(out _))
        {
            ItemCrumble.Start(item);
            return;
        }

        item.TakeOutOfPlay();
        this.FadeOutAndDestroyGaussianObject(item, VanishDelay);
        if (item.TryGetComponent<BombItem>(out var bomb)) bomb.Dispose();
        else trash.Collect(identifier);
    }

    // Only the top opening is wider than its trigger, below it the item has to be inside the shaft.
    private bool IsOverOpening(Vector3 point)
    {
        float top = float.NegativeInfinity;
        foreach (var opening in openings)
            top = Mathf.Max(top, opening.bounds.max.y);

        foreach (var opening in openings)
        {
            var bounds = opening.bounds;
            float margin = bounds.max.y >= top - 0.001f ? OpeningMargin : 0f;
            if (point.y >= bounds.min.y && point.y <= top + OpeningHeight &&
                point.x >= bounds.min.x - margin && point.x <= bounds.max.x + margin &&
                point.z >= bounds.min.z - margin && point.z <= bounds.max.z + margin)
                return true;
        }
        return false;
    }
}
