using UnityEngine;

// Lets plasma bolts hit belt items. Bombs and fakes are worth shooting, real items are not.
public class ShootableItem : MonoBehaviour, IDamageable
{
    public void TakeDamage(float damage)
    {
        if (gameObject.IsOutOfPlay() || ItemCrumble.IsCrumbling(gameObject)) return;

        if (TryGetComponent<BombItem>(out var bomb))
        {
            bomb.Defuse();
            return;
        }

        if (TryGetComponent<MockItem>(out _)) GameService.Instance.RewardForMockShot();
        else GameService.Instance.PunishForObjectShot();
        ItemCrumble.Start(gameObject);
    }
}
