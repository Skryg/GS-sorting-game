using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

// Blinks red faster and faster and explodes when the fuse runs out or when it hits the floor.
// Any bin takes it, shooting it is safe as well.
public class BombItem : MonoBehaviour
{
    public const float FuseSeconds = 15f;
    private const float StartBlinksPerSecond = 0.8f;
    private const float EndBlinksPerSecond = 7f;
    private const float BaseTint = 0.15f;
    private const float BlinkTint = 0.6f;
    private const float BlinkGrowth = 0.06f;
    private static readonly Color Red = new Color(1f, 0.12f, 0.08f, 0f);
    private const float ExplosionSpread = 4f;
    private const float ExplosionDuration = 1.1f;

    private GaussianSplatRenderer splats;
    private AudioSource beeper;
    private float fuse;
    private float phase;
    private bool done;

    private void Awake()
    {
        splats = GetComponent<GaussianSplatRenderer>();
        beeper = AudioManager.AttachSource(gameObject);
    }

    private void Update()
    {
        if (done || gameObject.IsOutOfPlay()) return;
        if (GameService.Instance != null && GameService.Instance.IsGameOver) return;

        fuse += Time.deltaTime;
        float burnt = Mathf.Clamp01(fuse / FuseSeconds);
        float previousPhase = phase;
        phase += Time.deltaTime * Mathf.Lerp(StartBlinksPerSecond, EndBlinksPerSecond, burnt * burnt);
        if (Mathf.Floor(phase) > Mathf.Floor(previousPhase))
            AudioManager.PlayOn(beeper, Sfx.BombBeep, 1f + burnt * 0.3f);

        // short flash at the start of every blink
        float blink = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(phase * Mathf.PI * 2f)), 8f);
        if (splats != null)
        {
            var tint = Red;
            tint.a = BaseTint + BlinkTint * blink;
            splats.tint = tint;
            splats.m_SplatScale = 1f + BlinkGrowth * blink * (0.5f + burnt);
        }

        if (fuse >= FuseSeconds) Explode();
    }

    // Put into a bin.
    public void Dispose()
    {
        if (done) return;
        done = true;
        GameService.Instance.RewardForBombDisposed();
    }

    public void Defuse()
    {
        if (done) return;
        done = true;
        GameService.Instance.RewardForBombDisposed();
        ItemCrumble.Start(gameObject, Sfx.Crumble, ExplosionDuration, ExplosionSpread * 0.5f);
    }

    public void Explode()
    {
        if (done) return;
        done = true;

        if (TryGetComponent<XRGrabInteractable>(out var grab))
        {
            foreach (var interactor in grab.interactorsSelecting)
                (interactor as XRBaseInputInteractor)?.SendHapticImpulse(1f, 0.4f);
        }
        if (splats != null) splats.tint = new Color(1f, 0.55f, 0.15f, 0.7f);
        GameService.Instance.PunishForBombExploded();
        ItemCrumble.Start(gameObject, Sfx.BombExplosion, ExplosionDuration, ExplosionSpread);
    }
}
