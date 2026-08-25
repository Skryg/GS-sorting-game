using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class PlasmaGun : MonoBehaviour
{
    [Header("Haptics")]
    [Range(0f, 1f)]
    public float hapticAmplitude = 0.5f;   // strength of the buzz
    public float hapticDuration = 0.1f;     // seconds

    [Header("Recoil")]
    public RecoilKick recoil;
    [Header("References")]
    [Tooltip("Empty at the barrel tip, +Z pointing forward")]
    public Transform muzzle;
    [Tooltip("Plasma projectile prefab (needs PlasmaProjectile script)")]
    public GameObject projectilePrefab;
    public ParticleSystem muzzleFlash;

    [Header("Shooting")]
    public float fireRate = 4f;

    [Header("Input")]
    public Hand hand = Hand.Right;
    public enum Hand { Left, Right }

    private float heat;
    private float nextFireTime;

    private void Awake()
    {
        if (recoil == null)
        {
            recoil = GetComponentInChildren<RecoilKick>();
        }
    }

    void Update()
    {
        if (ReadTrigger() && Time.time >= nextFireTime)
        {
            Fire();
            nextFireTime = Time.time + 1f / fireRate;
        }
    }

    void Fire()
    {
        
        if (projectilePrefab != null && muzzle != null)
        {
            GameObject p = Instantiate(projectilePrefab, muzzle.position, muzzle.rotation);
            if (p.TryGetComponent<PlasmaProjectile>(out var proj))
            {
                proj.Launch(muzzle.forward);
                if (muzzleFlash != null) muzzleFlash.Play();
                if (recoil != null) recoil.Kick();
                SendHaptics();
            }
        }
    }

    bool ReadTrigger()
    {
        var device = (hand == Hand.Right)
            ? UnityEngine.InputSystem.XR.XRController.rightHand
            : UnityEngine.InputSystem.XR.XRController.leftHand;

        if (device == null) return false;

        var axis = device.TryGetChildControl<AxisControl>("trigger");
        if (axis != null && axis.ReadValue() > 0.1f)
            return true;

        var pressed = device.TryGetChildControl<ButtonControl>("triggerPressed");
        return pressed != null && pressed.isPressed;
    }

    void SendHaptics()
    {
        // find the XR controller device for the correct hand
        var characteristics = (hand == Hand.Right)
            ? UnityEngine.XR.InputDeviceCharacteristics.Right
            : UnityEngine.XR.InputDeviceCharacteristics.Left;

        var devices = new System.Collections.Generic.List<UnityEngine.XR.InputDevice>();
        UnityEngine.XR.InputDevices.GetDevicesWithCharacteristics(
            characteristics | UnityEngine.XR.InputDeviceCharacteristics.Controller,
            devices);

        foreach (var device in devices)
        {
            if (device.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
            {
                device.SendHapticImpulse(0u, hapticAmplitude, hapticDuration);
            }
        }
    }
}