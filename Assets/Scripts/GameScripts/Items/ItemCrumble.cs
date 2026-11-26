using System.Collections;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Takes an item out of play and lets its splats fall apart, then destroys it. Scoring is up to the caller.
public class ItemCrumble : MonoBehaviour
{
    private const float DefaultDuration = 1.6f;

    public static bool IsCrumbling(GameObject item) => item.TryGetComponent<ItemCrumble>(out _);

    // False when the item is already crumbling.
    public static bool Start(GameObject item, Sfx sound = Sfx.Crumble, float duration = DefaultDuration, float spread = 1f)
    {
        if (IsCrumbling(item)) return false;

        // also drops the item from the hand
        if (item.TryGetComponent<XRGrabInteractable>(out var grab)) grab.enabled = false;
        item.TakeOutOfPlay();
        foreach (var col in item.GetComponentsInChildren<Collider>())
            col.enabled = false;
        if (item.TryGetComponent<Rigidbody>(out var body))
        {
            if (!body.isKinematic) body.linearVelocity = body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        AudioManager.PlayAt(sound, item.transform.position);
        var crumble = item.AddComponent<ItemCrumble>();
        crumble.StartCoroutine(crumble.FallApart(duration, spread));
        return true;
    }

    private IEnumerator FallApart(float duration, float spread)
    {
        var splats = GetComponent<GaussianSplatRenderer>();
        if (splats != null) splats.disintegrationSpread = spread;
        for (float time = 0f; time < duration && splats != null; time += Time.deltaTime)
        {
            splats.disintegration = time / duration;
            yield return null;
        }
        Destroy(gameObject);
    }
}
