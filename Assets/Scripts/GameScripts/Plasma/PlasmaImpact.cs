using System.Collections.Generic;
using UnityEngine;

// Short splash where a plasma bolt hits: a flash, sparks thrown away from the surface and a fading light.
// Built in code from the bolt's own glow sprite, so no extra assets are needed.
public class PlasmaImpact : MonoBehaviour
{
    private const float Lifetime = 0.6f;
    private const float LightDuration = 0.18f;
    private const float LightIntensity = 2.5f;

    private static readonly Dictionary<Texture, Material> materials = new();

    private Light flashLight;
    private float age;

    public static void Spawn(Vector3 position, Vector3 normal, SpriteRenderer glow)
    {
        Color color = glow != null ? glow.color : new Color(0.43f, 0.76f, 0.97f);
        color.a = 1f;
        Material material = GetMaterial(glow);

        // inactive while configuring, otherwise the particle systems start with default settings
        var go = new GameObject("PlasmaImpact");
        go.SetActive(false);
        go.transform.SetPositionAndRotation(position + normal * 0.01f, Quaternion.LookRotation(normal));

        var impact = go.AddComponent<PlasmaImpact>();
        if (material != null)
        {
            CreateFlash(go.transform, material, color);
            CreateSparks(go.transform, material, color);
        }
        impact.flashLight = go.AddComponent<Light>();
        impact.flashLight.type = LightType.Point;
        impact.flashLight.color = color;
        impact.flashLight.range = 1.2f;
        impact.flashLight.intensity = LightIntensity;
        impact.flashLight.shadows = LightShadows.None;

        go.SetActive(true);
        Destroy(go, Lifetime);
    }

    private static Material GetMaterial(SpriteRenderer glow)
    {
        if (glow == null || glow.sprite == null || glow.sharedMaterial == null) return null;
        var texture = glow.sprite.texture;
        if (!materials.TryGetValue(texture, out var material) || material == null)
        {
            material = new Material(glow.sharedMaterial) { mainTexture = texture };
            materials[texture] = material;
        }
        return material;
    }

    private static ParticleSystem CreateSystem(Transform parent, string name, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.1f;
        main.loop = false;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        return ps;
    }

    private static void CreateFlash(Transform parent, Material material, Color color)
    {
        var ps = CreateSystem(parent, "Flash", material);
        var main = ps.main;
        main.startLifetime = 0.15f;
        main.startSpeed = 0f;
        main.startSize = 0.18f;
        main.startColor = color;

        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        var shape = ps.shape;
        shape.enabled = false;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
        SetFade(ps, color);
    }

    private static void CreateSparks(Transform parent, Material material, Color color)
    {
        var ps = CreateSystem(parent, "Sparks", material);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.01f, 0.025f);
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
        main.gravityModifier = 0.6f;

        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });

        // the system faces along the surface normal, so a wide cone sprays away from the hit surface
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 70f;
        shape.radius = 0.01f;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.04f;
        renderer.lengthScale = 1f;
        SetFade(ps, color);
    }

    private static void SetFade(ParticleSystem ps, Color color)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(color, 0.3f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = gradient;
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (flashLight != null)
            flashLight.intensity = LightIntensity * Mathf.Clamp01(1f - age / LightDuration);
    }
}
