using UnityEngine;

// The frost giant's spell effects, drawn from the VFX concept art made in Baroque 3D (Resources/VFX): glowing images
// blended additively, so their black backgrounds vanish and only the ice and its light show.
//  - Volley: the ice shard volley, a streak of razor shards and cyan trails laid along the line an icicle flies
//  - Eruption: the ground-impact burst of jagged glacial spikes and frozen cracks, standing where icicles break out
public static class IceVfx
{
    private static Material volley, eruption, blizzard;
    private static Mesh volleyQuad, floorQuad;

    private static Material Volley => volley != null ? volley : volley = Resources.Load<Material>("VFX/IceShardVolley");
    private static Material Eruption => eruption != null ? eruption : eruption = Resources.Load<Material>("VFX/IceEruption");
    private static Material Blizzard => blizzard != null ? blizzard : blizzard = Resources.Load<Material>("VFX/BlizzardBurst");

    public static readonly Color Frost = new Color(0.55f, 0.88f, 1f);
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    // Turns a red ground warning (Telegraph.Circle / Line) into the frost giant's: a deep blue rim filling with cold light
    // tint: another colour for the fill (the spear's and Ullr's own warnings); null is frost
    public static GameObject Frosted(GameObject telegraph, Color? tint = null)
    {
        if (telegraph == null)
            return null;
        var block = new MaterialPropertyBlock();
        Renderer[] parts = telegraph.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < parts.Length; i++)
        {
            bool fill = i > 0;   // the first piece is the rim, the second the part that fills
            parts[i].GetPropertyBlock(block);
            if (tint.HasValue)
            {
                Color t = tint.Value;
                block.SetColor(BaseColorId, fill ? t : t * 0.12f);
                block.SetColor(EmissionId, fill ? t * 1.1f : t * 0.06f);
            }
            else
            {
                block.SetColor(BaseColorId, fill ? new Color(0.35f, 0.75f, 1f) : new Color(0.04f, 0.1f, 0.2f));
                block.SetColor(EmissionId, fill ? new Color(0.15f, 0.55f, 1.2f) : new Color(0.02f, 0.06f, 0.14f));
            }
            parts[i].SetPropertyBlock(block);
        }
        return telegraph;
    }

    // A burst of cold blue light that dies away (an impact, a gathering of frost)
    public static Light Flash(Vector3 at, float range, float intensity, float seconds, float rise = 0f)
    {
        var go = new GameObject("FrostLight");
        go.transform.position = at;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.45f, 0.8f, 1f);
        light.range = range;
        light.shadows = LightShadows.None;
        go.AddComponent<LightPulse>().Init(intensity, rise, seconds);
        return light;
    }

    // Where an icicle lands or the ground breaks: a frost ring racing out, the glacial-spike burst, and a flash
    public static void Impact(Vector3 ground, float radius, float burstSize, bool light = true)
    {
        Vfx.GroundRing(ground, radius, Frost, 0.4f);
        Vfx.GroundRing(ground, radius * 0.55f, Color.Lerp(Frost, Color.white, 0.5f), 0.25f, "glow");
        IceEruption(ground, burstSize);
        if (light)
            Flash(ground + Vector3.up * 1.2f, radius * 2.5f + 3f, 3f, 0.45f);
    }

    // The circular blizzard burst: a swirl of snow, shards and frost spreading over the floor round centre, spinning
    public static GameObject BlizzardBurst(Vector3 centre, float radius)
    {
        if (Blizzard == null)
            return null;
        var go = new GameObject("BlizzardBurst");
        go.transform.position = centre + Vector3.up * 0.18f;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        go.transform.localScale = new Vector3(radius * 2.3f, 1f, radius * 2.3f);   // the swirl fills about 85% of the picture
        if (floorQuad == null)
        {
            floorQuad = new Mesh { name = "IceFloorQuad" };
            floorQuad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            floorQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            floorQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            floorQuad.RecalculateNormals();
            floorQuad.RecalculateBounds();
        }
        go.AddComponent<MeshFilter>().sharedMesh = floorQuad;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Blizzard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.AddComponent<VfxFade>().Init(0.18f, 0.35f, 0.7f, false, -240f, true);
        return go;
    }

    // A streak of shards lying over the ground along dir, length long; moves with the thrown icicle (parent it, or move it)
    public static GameObject ShardVolley(Vector3 position, Vector3 dir, float length, float width)
    {
        if (Volley == null)
            return null;
        var go = new GameObject("IceShardVolley");
        go.transform.position = position;
        go.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(dir, Vector3.up).normalized);
        go.transform.localScale = new Vector3(width, 1f, length);
        go.AddComponent<MeshFilter>().sharedMesh = VolleyQuad();
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Volley;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.AddComponent<VfxFade>().Init(0.08f, 3f, 0.15f, false);   // (the thrower fades it out when the icicle lands)
        return go;
    }

    // The burst of glacial spikes standing up where the ground breaks, facing the camera, gone again in about a second
    public static GameObject IceEruption(Vector3 ground, float size)
    {
        if (Eruption == null)
            return null;
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "IceEruption";
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = Eruption;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.transform.position = ground;
        go.transform.localScale = Vector3.one * size;
        go.AddComponent<VfxFade>().Init(0.12f, 0.45f, 0.45f, true);
        return go;
    }

    // A flat quad in the x/z plane whose picture is turned 45 degrees: the volley art flies toward its lower right
    // corner, and this lays that line of flight along +z (the picture is sampled inside its frame, so nothing clips)
    private static Mesh VolleyQuad()
    {
        if (volleyQuad != null)
            return volleyQuad;
        const float s = 0.7f / 1.41421356f;
        // image axes: flight (1,-1)/√2 -> +z, across (1,1)/√2 -> +x
        Vector2 UvOf(float x, float z) => new Vector2(0.5f + (x + z) * s, 0.5f + (x - z) * s);
        var v = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
        volleyQuad = new Mesh { name = "IceVolleyQuad" };
        volleyQuad.vertices = v;
        volleyQuad.uv = new[] { UvOf(-0.5f, -0.5f), UvOf(0.5f, -0.5f), UvOf(0.5f, 0.5f), UvOf(-0.5f, 0.5f) };
        volleyQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        volleyQuad.RecalculateNormals();
        volleyQuad.RecalculateBounds();
        return volleyQuad;
    }
}

// A light that swells to full over `rise` seconds, then fades out over the rest of its life
public class LightPulse : MonoBehaviour
{
    private Light lamp;
    private float peak, rise, life, age;

    public void Init(float intensity, float riseTime, float seconds)
    {
        lamp = GetComponent<Light>();
        peak = intensity; rise = riseTime; life = Mathf.Max(seconds, riseTime + 0.05f);
        lamp.intensity = rise > 0f ? 0f : peak;
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= life)
        {
            Destroy(gameObject);
            return;
        }
        lamp.intensity = age < rise ? peak * age / rise : peak * (1f - (age - rise) / (life - rise));
    }
}

// Grows in, holds, fades out (additive art fades by dimming its colour), and optionally keeps facing the camera
public class VfxFade : MonoBehaviour
{
    private float grow, hold, fade, age, spin;
    private bool billboard, spread;
    private Vector3 full, ground;
    private Renderer rend;
    private MaterialPropertyBlock block;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    // spin: degrees a second about the vertical; spread: grows out across the floor from a third of its size
    public void Init(float growTime, float holdTime, float fadeTime, bool faceCamera, float spinSpeed = 0f, bool spreadOut = false)
    {
        grow = growTime; hold = holdTime; fade = fadeTime; billboard = faceCamera; spin = spinSpeed; spread = spreadOut;
        full = transform.localScale;
        ground = transform.position;
        rend = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        Apply(0f);
    }

    // Ends early: from now on only the fade-out remains
    public void FadeOutNow()
    {
        float shown = grow + hold;
        if (age < shown)
            age = shown;
    }

    private void LateUpdate()
    {
        age += Time.deltaTime;
        if (age >= grow + hold + fade)
        {
            Destroy(gameObject);
            return;
        }
        Apply(age);
    }

    private void Apply(float t)
    {
        float k = t < grow ? Mathf.SmoothStep(0.3f, 1f, t / Mathf.Max(0.001f, grow)) : 1f;
        float a = t < grow ? t / Mathf.Max(0.001f, grow) : t < grow + hold ? 1f : 1f - (t - grow - hold) / Mathf.Max(0.001f, fade);
        if (billboard)
        {
            transform.localScale = full * k;
            Camera cam = Camera.main;
            if (cam != null)
                transform.rotation = cam.transform.rotation;
            // stands on the ground: its bottom edge at the spot
            transform.position = ground + transform.up * transform.localScale.y * 0.42f;
        }
        else if (spread)
        {
            float s = t < grow ? Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.001f, grow))) : 1f + (t - grow) * 0.08f;
            transform.localScale = new Vector3(full.x * s, full.y, full.z * s);
        }
        if (spin != 0f)
            transform.Rotate(0f, spin * Time.deltaTime, 0f, Space.World);
        rend.GetPropertyBlock(block);
        block.SetColor(BaseColor, new Color(a, a, a, a));
        rend.SetPropertyBlock(block);
    }
}
