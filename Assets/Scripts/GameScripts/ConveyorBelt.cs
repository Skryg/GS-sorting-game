using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ConveyorBelt : MonoBehaviour
{
    public float Speed { get; private set; } = 0.0f;
    public float beltMovingOffset = 0.45f;
    private Renderer objectRenderer;
    private static readonly Vector3 BeltDirection = Vector3.back;
    private readonly Dictionary<Rigidbody, XRGrabInteractable> carried = new Dictionary<Rigidbody, XRGrabInteractable>();
    private readonly List<Rigidbody> dropped = new List<Rigidbody>();
    private Collider beltCollider;

    // how high above the belt an item is still carried, covers items lifted by the curtain or lying on other items
    private const float CarryHeight = 0.4f;

    private const float SoundVolume = 0.3f;
    // The belt is long, so it stays at full volume within a few metres instead of fading right away.
    private const float SoundMinDistance = 3f;
    private const float SlowSpeed = 0.7f;
    private const float FastSpeed = 1.8f;
    // speed units per second, so starting or stopping takes about a second
    private const float Acceleration = 1.5f;
    private float targetSpeed;
    private AudioSource beltSound;

    // Start is called before the first frame update
    void Start()
    {
        objectRenderer = GetComponent<Renderer>();
        beltCollider = GetComponent<Collider>();
        beltSound = AudioManager.AttachLoop(gameObject, AudioManager.Instance.ConveyorLoop);
        beltSound.minDistance = SoundMinDistance;
    }

    // Update is called once per frame
    private void Update()
    {
        UpdateSpeed();
        MoveBeltTexture();
        MoveCollidedObjects();
        UpdateBeltSound();
    }

    // Pitch and volume follow the speed; the belt goes silent when it stops.
    private void UpdateBeltSound()
    {
        float t = Mathf.InverseLerp(SlowSpeed, FastSpeed, Speed);
        beltSound.pitch = Mathf.Lerp(0.9f, 1.25f, t);
        beltSound.volume = SoundVolume * AudioManager.SfxVolume * Mathf.Lerp(0.7f, 1f, t) * Mathf.Clamp01(Speed / SlowSpeed);
    }

    // Can be called every frame; the belt accelerates towards the target instead of jumping.
    public void SetConveyorSpeed(float newSpeed)
    {
        if (targetSpeed <= 0f && newSpeed > 0f)
        {
            AudioManager.PlayAt(Sfx.BeltStart, transform.position);
        }
        targetSpeed = newSpeed;
    }

    private void UpdateSpeed()
    {
        Speed = Mathf.MoveTowards(Speed, targetSpeed, Acceleration * Time.deltaTime);
    }

    private void MoveBeltTexture()
    {
        if (objectRenderer.material is not null)
        {
            objectRenderer.material.mainTextureOffset += beltMovingOffset * Speed * Time.deltaTime * Vector2.up;
        }
    }

    // An item that touched the belt is carried until it is grabbed or leaves the belt. Contact alone is not enough:
    // an item that lost it for a moment stopped moving until the next one pushed it.
    private void FixedUpdate()
    {
        dropped.Clear();
        foreach (var item in carried)
        {
            var rb = item.Key;
            if (rb == null || item.Value == null || item.Value.isSelected || !IsAboveBelt(rb.worldCenterOfMass))
            {
                dropped.Add(rb);
                continue;
            }
            if (Speed <= 0f || rb.isKinematic) continue;

            // only the belt moves items along it, otherwise round ones roll away from their place
            rb.WakeUp();
            rb.linearVelocity -= Vector3.Project(rb.linearVelocity, BeltDirection);
        }
        foreach (var rb in dropped)
            carried.Remove(rb);
    }

    private bool IsAboveBelt(Vector3 point)
    {
        var bounds = beltCollider.bounds;
        return point.x >= bounds.min.x && point.x <= bounds.max.x &&
               point.z >= bounds.min.z && point.z <= bounds.max.z &&
               point.y >= bounds.min.y && point.y <= bounds.max.y + CarryHeight;
    }

    private void MoveCollidedObjects()
    {
        foreach (var item in carried)
        {
            if (item.Key != null && item.Value != null && !item.Value.isSelected)
                item.Key.transform.position += Speed * Time.deltaTime * BeltDirection;
        }
    }

    private void OnCollisionEnter(Collision collision) => RegisterContact(collision);

    private void OnCollisionStay(Collision collision) => RegisterContact(collision);

    private void RegisterContact(Collision collision)
    {
        var rb = collision.rigidbody;
        if (rb == null || carried.ContainsKey(rb) || collision.collider.GetComponentInParent<DragObject>() == null) return;
        var grab = rb.GetComponent<XRGrabInteractable>();
        if (grab != null && !grab.isSelected && IsAboveBelt(rb.worldCenterOfMass))
            carried.Add(rb, grab);
    }
}
