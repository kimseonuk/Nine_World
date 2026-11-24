using System.Collections;
using UnityEngine;

// Muspelheim's fire effects, built from the painted VARCO sprites in Resources/Vfx (mus_*: drawn additively, so their
// black backgrounds vanish) layered with particles and a flash of light. Every boss and monster skill draws from here,
// so the realm's fire looks the same everywhere. The sprites keep their own colours; 'heat' scales their brightness.
public static class MuspelFx
{
    public static readonly Color OrangeFire = new Color(1f, 0.42f, 0.1f);
    public static readonly Color BlueFire = new Color(0.25f, 0.6f, 1f);
    // The fire colour of the moment: blue while Surtr's fight is on (his blue flames read better over the red lava)
    public static Color Fire => Blue ? BlueFire : OrangeFire;
    public static readonly Color Gold = new Color(1f, 0.8f, 0.35f);
    static Color White(float heat) => new Color(heat, heat, heat, 1f);

    // Every sprite's size is multiplied by this: Surtr raises it for his fight, where the camera stands far back
    public static float SizeScale = 1f;

    // While true every effect is drawn in blue fire: the mus_* sprites switch to their mus_*_blue versions and the
    // particles, sparks and lights turn blue. Surtr turns it on for his fight.
    public static bool Blue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetScale() { SizeScale = 1f; Blue = false; }

    // the sprite to draw: its blue version while the blue fire is on
    static string T(string tex) => Blue && tex.StartsWith("mus_") ? tex + "_blue" : tex;
    public static string Tex(string tex) => T(tex);

    // ───────────── Single pieces ─────────────

    // A sprite standing upright and turned toward the camera (pillars, flames, splashes): it doesn't lean back like a
    // full billboard would under the steep camera
    public static VfxPiece Standing(string tex, Vector3 bottom, float size, float seconds, float heat = 1.3f, float s0 = 0.6f, float s1 = 1.15f)
    {
        size *= SizeScale;
        Vector3 toCam = Camera.main != null ? Camera.main.transform.position - bottom : Vector3.back;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 0.01f) toCam = Vector3.back;
        // the quad faces -Z, so look away from the camera to show its face
        Quaternion facing = Quaternion.LookRotation(-toCam.normalized);
        return Vfx.Sprite(T(tex), bottom + Vector3.up * size * 0.5f, size, White(heat), seconds, s0, s1, 0f, facing);
    }

    // A sprite lying flat on the floor (rings, magic circles, cracks), turned to 'yaw'
    public static VfxPiece Lying(string tex, Vector3 at, float size, float seconds, float heat = 1.3f, float s0 = 0.4f, float s1 = 1f, float spin = 0f, float yaw = 0f)
    {
        // (rings and cracks mark where a blow lands, so they grow less than the rest)
        size *= Mathf.Lerp(1f, SizeScale, 0.6f);
        return Vfx.Sprite(T(tex), new Vector3(at.x, at.y + 0.07f, at.z), size, White(heat), seconds, s0, s1, spin, Quaternion.Euler(90f, yaw, 0f));
    }

    // A camera-facing sprite (bursts, fireballs)
    public static VfxPiece Facing(string tex, Vector3 at, float size, float seconds, float heat = 1.4f, float s0 = 0.4f, float s1 = 1.2f, float spin = 0f)
    {
        return Vfx.Sprite(T(tex), at, size * SizeScale, White(heat), seconds, s0, s1, spin);
    }

    // A lasting magic circle on the floor (a telegraph): turns slowly; destroy it (or let 'seconds' run out). Its size
    // is the area it marks, so it never scales.
    public static VfxPiece Circle(Vector3 at, float radius, float seconds, float heat = 1.2f, float spin = 25f)
    {
        var piece = Vfx.Sprite(T("mus_runecircle"), new Vector3(at.x, at.y + 0.08f, at.z), radius * 2.2f, White(heat), seconds, 0.85f, 1f, spin, Quaternion.Euler(90f, 0f, 0f));
        return piece.OpenUp(0.25f);
    }

    // A short burst of warm light
    public static void Light(Vector3 at, float range, float intensity, float seconds, Color? color = null)
    {
        var go = new GameObject("FxLight");
        go.transform.position = at;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color ?? (Blue ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.45f, 0.15f));
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        go.AddComponent<FxLightFade>().Init(l, seconds);
    }

    // ───────────── Composed effects ─────────────

    // A fiery explosion: the blast, a shockwave across the floor, glowing cracks left behind, sparks, smoke and light
    public static void Explosion(Vector3 at, float size, bool cracks = true)
    {
        Vector3 floor = new Vector3(at.x, at.y, at.z);
        Facing("mus_burst", floor + Vector3.up * size * 0.35f, size * 1.6f, 0.55f, 1.5f, 0.3f, 1.25f, Random.Range(-30f, 30f));
        Lying("mus_shockring", floor, size * 2.2f, 0.5f, 1.4f, 0.2f, 1.15f, 40f);
        if (cracks) Lying("mus_crack", floor, size * 1.6f, 1.6f, 1.2f, 0.9f, 1f, 0f, Random.Range(0f, 360f));
        Vfx.Burst(floor + Vector3.up * 0.4f, Fire, Mathf.RoundToInt(14 + size * 4f), 7f + size * 1.5f, 0.22f, 2f);
        Vfx.Dust(floor, size * 0.8f, 10, new Color(0.25f, 0.15f, 0.12f, 0.55f));
        Light(floor + Vector3.up * 1.5f, size * 4f, 6f, 0.45f);
    }

    // A column of fire erupting out of the floor
    public static void Pillar(Vector3 at, float height, float width, bool cracks = true)
    {
        Standing("mus_pillar", at, height, 0.6f, 1.5f, 0.5f, 1.1f);
        var ps = Vfx.Fire(at + Vector3.up * 0.2f, Vector3.up, height * 0.8f, 8f, 0.25f);
        var main = ps.main;
        main.startSize = new ParticleSystem.MinMaxCurve(width * 0.5f, width);
        if (Blue) main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.85f, 1f), BlueFire);
        main.startLifetime = 0.5f;
        var em = ps.emission; em.rateOverTime = 110f;
        Lying("mus_shockring", at, width * 2.6f, 0.4f, 1.2f, 0.3f, 1.1f, 30f);
        if (cracks) Lying("mus_crack", at, width * 2.4f, 1.2f, 1.1f, 0.9f, 1f, 0f, Random.Range(0f, 360f));
        Vfx.Burst(at + Vector3.up * 0.4f, Fire, 10, 9f, 0.22f, 2.5f);
        Light(at + Vector3.up * height * 0.4f, width * 4f, 4f, 0.4f);
    }

    // A fan of molten lava bursting up out of the floor
    public static void Splash(Vector3 at, float size)
    {
        Standing("mus_lavasplash", at, size, 0.5f, 1.4f, 0.4f, 1.2f);
        Vfx.Burst(at + Vector3.up * 0.3f, Blue ? BlueFire : new Color(1f, 0.3f, 0.05f), 12, 8f, 0.3f, 3f);
    }

    // A crescent of fire swept through the air in front of a blow
    public static void Slash(Vector3 center, Vector3 forward, float size, float tilt = 0f)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        float yaw = Quaternion.LookRotation(forward).eulerAngles.y;
        // lying flat (tilted up by 'tilt' degrees for an upward rip), its bow facing the way of the blow
        var rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f - tilt, 0f, 0f) * Quaternion.Euler(0f, 0f, 180f);
        Vfx.Sprite(T("mus_slash"), center, size, White(1.5f), 0.32f, 0.7f, 1.15f, 0f, rot);
        Vfx.Burst(center + forward.normalized * size * 0.3f, Fire, 10, 7f, 0.18f);
    }

    // A ring of fire racing out across the floor
    public static void Ring(Vector3 at, float radius, float seconds = 0.5f)
    {
        Lying("mus_shockring", at, radius * 2.2f, seconds, 1.5f, 0.15f, 1f, 30f);
        Vfx.Dust(at, radius * 0.6f, 12, new Color(0.25f, 0.15f, 0.12f, 0.55f));
    }

    // A tongue of flame licking up off the floor
    public static void Flame(Vector3 at, float size, float seconds = 0.5f)
    {
        Standing("mus_flame", at, size, seconds, 1.3f, 0.5f, 1.1f);
    }

    // A camera-facing fireball sprite that rides along with 'owner' until it is destroyed
    public static GameObject FireballGlow(Transform owner, float size)
    {
        return Vfx.Glow(owner, Vector3.zero, size, White(1.3f), T("mus_fireball"), 120f);
    }
}

// Fades a temporary light out and removes it
public class FxLightFade : MonoBehaviour
{
    Light l;
    float life, t, start;

    public void Init(Light light, float seconds)
    {
        l = light;
        life = Mathf.Max(0.05f, seconds);
        start = light.intensity;
    }

    void Update()
    {
        t += Time.deltaTime;
        if (l != null) l.intensity = start * (1f - t / life) * (1f - t / life);
        if (t >= life) Destroy(gameObject);
    }
}
