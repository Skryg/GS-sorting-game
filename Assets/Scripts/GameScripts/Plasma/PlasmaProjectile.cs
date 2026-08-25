using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine;

public class PlasmaProjectile : MonoBehaviour
{
    [Header("Flight")]
    public float speed = 25f;
    public float lifeTime = 4f;   // self-destruct if it hits nothing

    [Header("Damage")]
    public int damage = 10;

    [Header("Impact (optional)")]
    public GameObject impactEffect;  // particle prefab spawned on hit

    private Vector3 velocity;
    private bool consumed;
    [Tooltip("Radius used to catch enemies right at the muzzle")]
    public float hitRadius = 0.15f;

    // called by the weapon right after spawning
    public void Launch(Vector3 direction)
    {
        velocity = direction.normalized * speed;
        Destroy(gameObject, lifeTime);
        CheckOverlapHit();
    }

    void Update()
    {
        float step = speed * Time.deltaTime;

        // raycast ahead so fast projectiles don't tunnel through thin colliders
        if (Physics.Raycast(transform.position, velocity.normalized, out RaycastHit hit, step))
        {
            transform.position = hit.point;
            HitSomething(hit.collider);
            return;
        }

        transform.position += velocity * Time.deltaTime;
    }

    void CheckOverlapHit()
    {
        Collider[] overlaps = Physics.OverlapSphere(transform.position, hitRadius);
        foreach (var col in overlaps)
        {
            if (col.GetComponentInParent<IDamageable>() != null)
            {
                HitSomething(col);
                return;
            }
        }
    }

    void HitSomething(Collider col)
    {
        if (consumed) return;
        consumed = true;

        // damage anything damageable (checks self + parents)
        if (col.TryGetComponent<IDamageable>(out var target))
            target.TakeDamage(damage);
        else if (col.GetComponentInParent<IDamageable>() is IDamageable parentTarget)
            parentTarget.TakeDamage(damage);

        if (impactEffect != null)
            Instantiate(impactEffect, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }
}
