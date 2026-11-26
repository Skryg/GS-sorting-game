using UnityEngine;

public class PlasmaProjectile : MonoBehaviour
{
    [Header("Flight")]
    public float speed = 25f;
    [Tooltip("Self-destructs after this many seconds if it hits nothing")]
    public float lifeTime = 4f;

    [Header("Damage")]
    public int damage = 10;
    [Tooltip("Radius used to catch enemies right at the muzzle")]
    public float hitRadius = 0.15f;
    [Tooltip("Spiders and items this close to the bolt's path are hit as well (metres)")]
    public float targetRadius = 0.08f;

    [Header("Impact (optional)")]
    [Tooltip("Particle prefab spawned on hit, the default flash is used when empty")]
    public GameObject impactEffect;

    private static readonly RaycastHit[] targetHits = new RaycastHit[16];

    private Vector3 velocity;
    private bool consumed;

    // called by the weapon right after spawning
    public void Launch(Vector3 direction)
    {
        velocity = direction.normalized * speed;
        Destroy(gameObject, lifeTime);
        CheckOverlapHit();
    }

    private void Update()
    {
        float step = speed * Time.deltaTime;
        Vector3 direction = velocity.normalized;

        // raycast ahead so fast projectiles don't tunnel through thin colliders
        bool blocked = Physics.Raycast(transform.position, direction, out RaycastHit hit, step);

        // targets are found with a thicker sweep, so thin ones like the wrench are not missed
        if (FindTarget(direction, blocked ? hit.distance : step, out RaycastHit target))
        {
            transform.position += direction * target.distance;
            HitSomething(target.collider, target.distance > 0f ? target.normal : -direction);
        }
        else if (blocked)
        {
            transform.position = hit.point;
            HitSomething(hit.collider, hit.normal);
        }
        else
        {
            transform.position += velocity * Time.deltaTime;
        }
    }

    private bool FindTarget(Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        nearest.distance = float.PositiveInfinity;
        int count = Physics.SphereCastNonAlloc(transform.position, targetRadius, direction, targetHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (targetHits[i].distance < nearest.distance && targetHits[i].collider.GetComponentInParent<IDamageable>() != null)
                nearest = targetHits[i];
        }
        return !float.IsPositiveInfinity(nearest.distance);
    }

    private void CheckOverlapHit()
    {
        foreach (var col in Physics.OverlapSphere(transform.position, hitRadius))
        {
            if (col.GetComponentInParent<IDamageable>() != null)
            {
                HitSomething(col, -velocity.normalized);
                return;
            }
        }
    }

    private void HitSomething(Collider col, Vector3 normal)
    {
        if (consumed) return;
        consumed = true;

        col.GetComponentInParent<IDamageable>()?.TakeDamage(damage);

        if (impactEffect != null)
            Instantiate(impactEffect, transform.position, Quaternion.identity);
        else
            PlasmaImpact.Spawn(transform.position, normal, GetComponentInChildren<SpriteRenderer>());

        Destroy(gameObject);
    }
}
