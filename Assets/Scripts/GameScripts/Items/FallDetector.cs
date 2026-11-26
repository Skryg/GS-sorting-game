using UnityEngine;

// Added to every spawned object, so a fall is reported once per object no matter how many ground colliders it hits.
public class FallDetector : MonoBehaviour
{
    private const float VanishDelay = 1f;

    private void OnCollisionEnter(Collision collision)
    {
        if (gameObject.IsOutOfPlay()) return;

        var zone = collision.collider.GetComponentInParent<FallZone>();
        if (zone == null || !zone.countsAsFall) return;

        if (TryGetComponent<MockItem>(out _))
        {
            ItemCrumble.Start(gameObject);
            return;
        }
        if (TryGetComponent<BombItem>(out var bomb))
        {
            bomb.Explode();
            return;
        }

        gameObject.TakeOutOfPlay();
        GameService.Instance.PunishForObjectFallen();
        this.FadeOutAndDestroyGaussianObject(gameObject, VanishDelay);
    }
}
