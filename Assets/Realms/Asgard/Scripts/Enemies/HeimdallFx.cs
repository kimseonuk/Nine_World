using System.Collections.Generic;
using UnityEngine;

// Heimdall's skill effects: textures painted in VARCO (Resources/Vfx: hofundslash, bifrostcircle, shockring,
// soundwave, beamflow, bifrostorb and the 16-frame impactsheet) and the spectral Hofund modelled in VARCO
// (Resources/Props/SpectralHofund), all animated here: flipbook explosions, shock rings racing out, rune circles
// turning, beams and sound walls flowing, phantom swords plunging out of the sky.
public static class HeimdallFx
{
    public static readonly Color Gold = new Color(1f, 0.85f, 0.45f);

    // A burst of golden light: the explosion plays out frame by frame, a crackling ring races out along the ground,
    // sparks fly
    public static void Impact(Vector3 at, float size, Color color)
    {
        Vfx.Sprite("impactsheet", at, size, color * 1.15f, 0.55f, 0.9f, 1.15f, Random.Range(-40f, 40f)).Sheet(4, 4);
        ShockRing(at, size * 0.55f, color * 0.8f, 0.5f);
        Vfx.Burst(at, Color.Lerp(color, Color.white, 0.4f), 12, size * 3f, 0.14f, 0.6f);
    }

    // A crackling ring of force running out over the ground, turning as it goes
    public static void ShockRing(Vector3 at, float radius, Color color, float seconds = 0.5f)
    {
        var ground = new Vector3(at.x, (at.y > 1.5f ? at.y - 1f : 0f) + 0.08f, at.z);
        Vfx.Sprite("shockring", ground, radius * 2.3f, color * 1.8f, seconds, 0.15f, 1f, 50f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
    }

    // A rune circle of the Bifrost on the floor, turning; it stays until destroyed (resize/recolour it with Set)
    public static VfxPiece Circle(Vector3 at, float size, Color color, float spin)
    {
        return Vfx.Decal(at + Vector3.up * 0.06f, size, color, "bifrostcircle", spin).OpenUp(0.3f);
    }

    // A flash of the rune circle that opens and fades by itself
    public static void CircleFlash(Vector3 at, float size, Color color, float seconds = 0.7f)
    {
        var ground = new Vector3(at.x, 0.07f, at.z);
        Vfx.Sprite("bifrostcircle", ground, size, color * 1.6f, seconds, 0.5f, 1.1f, 60f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
    }

    // A phantom of Hofund falling point-first out of the sky onto a spot: a streak of light behind it, an impact where
    // it strikes, then it stands glowing in the floor for `linger` seconds and fades away
    public static SpectralSword Sword(Vector3 ground, float scale, float fall, float linger, Color color)
    {
        GameObject model = Resources.Load<GameObject>("Props/SpectralHofund");
        if (model == null)
            return null;
        GameObject go = Object.Instantiate(model);
        foreach (Collider c in go.GetComponentsInChildren<Collider>())
            Object.Destroy(c);
        go.transform.localScale *= scale;
        var sword = go.AddComponent<SpectralSword>();
        sword.Init(new Vector3(ground.x, 0f, ground.z), scale, fall, linger, color);
        return sword;
    }

    // Lazily made scrolling materials for the flowing textures (one each, shared by every beam and sound wall)
    public static Material Flow(string texture, Vector2 speed)
    {
        var tex = Resources.Load<Texture2D>("Vfx/" + texture);
        if (tex == null)
            return Vfx.Material("beam", additive: true);
        return Ambient.Scrolling("heim_" + texture, tex, speed, Vector2.one, Color.white);
    }
}

// The phantom Hofund: falls with a trail of light, strikes with an impact, stands glowing and pulsing, then fades
public class SpectralSword : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");

    private Vector3 ground;
    private float scale, fall, linger, t;
    private Color color;
    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private bool landed;

    public bool Landed => landed;

    // How high it drops from: well below the follow camera (about 10 m up in the fight), so it falls in view
    private float Drop => Mathf.Min(7f * scale, 7.5f);

    public void Init(Vector3 spot, float size, float fallTime, float stay, Color tint)
    {
        ground = spot;
        scale = size;
        fall = Mathf.Max(0.05f, fallTime);
        linger = stay;
        color = tint;
        renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
        transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), Random.Range(-6f, 6f));
        transform.position = ground + Vector3.up * Drop;
        Vfx.Trail(gameObject, color, 0.6f * scale, 0.18f);
        Tint(0.6f);
    }

    private void Tint(float brightness)
    {
        if (block == null)
            return;
        block.SetColor(ColorId, color * 1.8f * brightness);
        foreach (Renderer r in renderers)
            if (r != null)
                r.SetPropertyBlock(block);
    }

    private void Update()
    {
        if (block == null)
        {
            Destroy(gameObject);
            return;
        }
        t += Time.deltaTime;
        if (!landed)
        {
            float k = Mathf.Clamp01(t / fall);
            // Accelerating down, driven a little way into the floor
            transform.position = Vector3.Lerp(ground + Vector3.up * Drop, ground - Vector3.up * 0.35f * scale, k * k);
            Tint(Mathf.Lerp(0.6f, 1.2f, k));
            if (k >= 1f)
            {
                landed = true;
                t = 0f;
                HeimdallFx.Impact(ground + Vector3.up * 0.6f * scale, 2.4f * scale, color);
                HeimdallFx.CircleFlash(ground, 3f * scale, color * 0.8f, 0.6f);
            }
            return;
        }
        // Standing in the floor: a slow pulse, then fading out over the last 0.4 s
        float fade = linger > 0f ? Mathf.Clamp01((linger + 0.4f - t) / 0.4f) : 0f;
        Tint((0.85f + 0.25f * Mathf.Sin(t * 9f)) * fade);
        if (t >= linger + 0.4f)
            Destroy(gameObject);
    }
}

// A sword wave's crescent shimmering in flight: it breathes wider and narrower and flickers
public class WaveShimmer : MonoBehaviour
{
    private Transform[] parts;
    private Vector3[] sizes;
    private float seed;

    private void Start()
    {
        var list = new List<Transform>();
        foreach (Transform child in transform)
            if (child.name.StartsWith("Sprite_"))
                list.Add(child);
        parts = list.ToArray();
        sizes = new Vector3[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            sizes[i] = parts[i].localScale;
        seed = Random.Range(0f, 10f);
    }

    private void Update()
    {
        if (parts == null)
            return;
        for (int i = 0; i < parts.Length; i++)
        {
            float w = 1f + Mathf.Sin(Time.time * 22f + seed + i) * 0.08f;
            float h = 1f + Mathf.Sin(Time.time * 31f + seed * 2f + i) * 0.12f;
            parts[i].localScale = new Vector3(sizes[i].x * w, sizes[i].y * h, sizes[i].z);
        }
    }
}
