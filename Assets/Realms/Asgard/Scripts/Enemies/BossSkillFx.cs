using System.Collections.Generic;
using UnityEngine;

// Shared pieces for Heimdall's and Týr's newer patterns (VARCO art in Resources/Vfx: hm_chain, hm_orb, hm_brand,
// hm_cross, hm_wideslash, hm_quake, hm_ragnawave, hm_safe, hm_mark_beam, hm_chaincircle, ty_runechain, ty_wolfspirit;
// VARCO models in Resources/Celestial: GoldenSwordHm, TyrRuneSword):
//  GoldChain   — a glowing chain of golden links drawn along any path (a textured line)
//  ChainHead   — a chain's head racing out from an anchor, bending toward the player; it binds them on contact
//  LightBolt   — a bolt of light flying out, optionally splitting in two or bending toward the player
//  SplitOrb    — a great orb that splits in three, the three swinging round to converge on one spot
//  WorldDim    — the arena's lights sinking (and coming back) for a dramatic beat
//  FlyingBlade — a summoned sword (model) flung point-first along a line
public static class BossSkillFx
{
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // A golden sword model from VARCO, or the spectral Hofund if it isn't in
    public static GameObject SwordModel(string name, float height)
    {
        GameObject prefab = Resources.Load<GameObject>("Celestial/" + name);
        if (prefab == null)
            prefab = Resources.Load<GameObject>("Props/SpectralHofund");
        if (prefab == null)
            return null;
        GameObject go = Object.Instantiate(prefab);
        foreach (Collider c in go.GetComponentsInChildren<Collider>())
            Object.Destroy(c);
        // the Celestial prefabs are 1 unit tall, standing on their base (the point, for a sword set point-down)
        go.transform.localScale = Vector3.one * height;
        return go;
    }

    // A flat effect lying on the floor (it fades over `seconds`; 0 keeps it until destroyed). On the arena's pale marble
    // additive light alone washes out, so a dark copy of the same painting is laid under it (it follows and fades with it)
    public static VfxPiece Floor(string texture, Vector3 at, float size, Color color, float seconds, float s0 = 1f, float s1 = 1f, float spin = 0f, float yaw = -1f)
    {
        Quaternion lying = Quaternion.Euler(90f, yaw < 0f ? Random.Range(0f, 360f) : yaw, 0f);
        VfxPiece p = Vfx.Sprite(texture, new Vector3(at.x, 0.09f, at.z), size, color, seconds, s0, s1, spin, lying);
        if (seconds <= 0f)
            p.Hold();
        InkUnder(p, texture, color);
        return p;
    }

    // The dark copy under a glowing floor mark: the painting's own shape (its alpha), in a deep shade of its colour
    public static void InkUnder(VfxPiece glow, string texture, Color color)
    {
        if (glow == null) return;
        var ink = new GameObject("Ink");
        ink.transform.SetParent(glow.transform, false);
        ink.transform.localPosition = new Vector3(0f, 0f, 0.002f);
        ink.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
        var r = ink.AddComponent<MeshRenderer>();
        r.sharedMaterial = Vfx.Material(texture, false);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        float m = Mathf.Max(color.r, Mathf.Max(color.g, color.b), 0.01f);
        Color shade = new Color(color.r / m * 0.45f, color.g / m * 0.28f, color.b / m * 0.12f, 0.75f);
        if (color.b > color.r) shade = new Color(0.05f, 0.18f, 0.35f, 0.7f);
        ink.AddComponent<InkFollow>().Init(r, glow.GetComponent<Renderer>(), shade);
    }

    // A column of light standing on the floor, turned to face the camera about its own upright axis
    public static void Beam(string texture, Vector3 at, float width, float height, Color color, float seconds)
    {
        var go = new GameObject("LightBeam");
        go.transform.position = new Vector3(at.x, height / 2f, at.z);
        go.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Vfx.Material(texture, true);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.localScale = new Vector3(width, height, 1f);
        go.AddComponent<BeamFade>().Init(r, color, seconds);
    }

    // A long flat slash of light lying on the floor along a line (the wide slash painting stretched over it)
    public static void SlashLine(Vector3 from, Vector3 dir, float length, float width, Color color, float seconds = 0.35f)
    {
        dir = Flat(dir).normalized;
        Vector3 mid = Flat(from) + dir * length / 2f;
        var go = new GameObject("SlashLine");
        go.transform.position = new Vector3(mid.x, 0.12f, mid.z);
        go.transform.rotation = Quaternion.LookRotation(Vector3.down, dir) * Quaternion.Euler(0f, 0f, 90f);
        go.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Vfx.Material("hm_wideslash", true);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.transform.localScale = new Vector3(length, width * 1.6f, 1f);
        go.AddComponent<SlashLineFade>().Init(r, color, seconds);
    }
}

// Keeps a floor mark's dark copy as bright (well, as present) as the mark itself while it fades
public class InkFollow : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private Renderer self, glow;
    private Color shade;
    private MaterialPropertyBlock mine, theirs;
    private float start = -1f;

    public void Init(Renderer r, Renderer g, Color c)
    {
        self = r; glow = g; shade = c;
        mine = new MaterialPropertyBlock();
        theirs = new MaterialPropertyBlock();
        Apply(1f);
    }

    private void Apply(float k)
    {
        Color c = shade;
        c.a *= Mathf.Clamp01(k);
        mine.SetColor(ColorId, c);
        self.SetPropertyBlock(mine);
    }

    private void LateUpdate()
    {
        if (glow == null || self == null) return;
        glow.GetPropertyBlock(theirs);
        Color g = theirs.GetColor(ColorId);
        float level = Mathf.Max(g.r, Mathf.Max(g.g, g.b)) * (g.a > 0f ? g.a : 1f);
        if (start < 0f && level > 0.01f) start = level;
        Apply(start > 0f ? level / start : 1f);
    }
}

public class SlashLineFade : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private Renderer r;
    private Color color;
    private float seconds, t;
    private MaterialPropertyBlock block;
    private Vector3 size;

    public void Init(Renderer renderer, Color c, float life)
    {
        r = renderer; color = c; seconds = Mathf.Max(0.05f, life);
        block = new MaterialPropertyBlock();
        size = transform.localScale;
        Apply(1f);
    }

    private void Apply(float k)
    {
        block.SetColor(ColorId, color * 2.2f * k);
        r.SetPropertyBlock(block);
    }

    private void Update()
    {
        t += Time.deltaTime;
        float k = t / seconds;
        transform.localScale = new Vector3(size.x * Mathf.Lerp(0.85f, 1.05f, Mathf.Min(1f, k * 3f)), size.y * Mathf.Lerp(1f, 0.4f, k), 1f);
        Apply(1f - k * k);
        if (k >= 1f)
            Destroy(gameObject);
    }
}

public class BeamFade : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private Renderer r;
    private Color color;
    private float seconds, t;
    private MaterialPropertyBlock block;
    private Vector3 size;

    public void Init(Renderer renderer, Color c, float life)
    {
        r = renderer; color = c; seconds = Mathf.Max(0.05f, life);
        block = new MaterialPropertyBlock();
        size = transform.localScale;
    }

    private void LateUpdate()
    {
        t += Time.deltaTime;
        float k = t / seconds;
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 to = cam.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(-to.normalized);
        }
        transform.localScale = new Vector3(size.x * Mathf.Lerp(1.3f, 0.2f, k), size.y * Mathf.Lerp(0.8f, 1.05f, Mathf.Min(1f, k * 4f)), 1f);
        block.SetColor(ColorId, color * 2f * (1f - k * k));
        r.SetPropertyBlock(block);
        if (k >= 1f)
            Destroy(gameObject);
    }
}

// A chain of golden links (VARCO hm_chain) drawn along a path: the texture tiles along it, the links shimmer
public class GoldChain : MonoBehaviour
{
    private LineRenderer line;
    private float width;
    private Color color;
    private float fade = -1f, fadeTime;

    public static GoldChain Make(float width, Color color, string texture = "hm_chain")
    {
        var go = new GameObject("GoldChain");
        var chain = go.AddComponent<GoldChain>();
        chain.width = width;
        chain.color = color;
        var l = go.AddComponent<LineRenderer>();
        l.sharedMaterial = Vfx.Material(texture, true);
        l.textureMode = LineTextureMode.Tile;
        l.alignment = LineAlignment.View;
        l.widthMultiplier = width;
        l.numCapVertices = 0;
        l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        l.receiveShadows = false;
        l.useWorldSpace = true;
        l.positionCount = 0;
        l.startColor = l.endColor = color * 1.6f;
        // one link's length a tile: the painting holds about seven links
        l.textureScale = new Vector2(1f / (width * 5.3f), 1f);
        chain.line = l;
        return chain;
    }

    public void SetPath(IList<Vector3> points)
    {
        if (line == null) return;
        line.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
            line.SetPosition(i, points[i]);
    }

    public void SetLine(Vector3 a, Vector3 b)
    {
        if (line == null) return;
        line.positionCount = 2;
        line.SetPosition(0, a);
        line.SetPosition(1, b);
    }

    public void FadeOut(float seconds)
    {
        fade = 0f;
        fadeTime = Mathf.Max(0.05f, seconds);
    }

    private void Update()
    {
        if (line == null) return;
        float shimmer = 1.4f + Mathf.Sin(Time.time * 18f + GetInstanceID()) * 0.25f;
        float k = 1f;
        if (fade >= 0f)
        {
            fade += Time.deltaTime;
            k = 1f - fade / fadeTime;
            if (k <= 0f)
            {
                Destroy(gameObject);
                return;
            }
        }
        line.startColor = line.endColor = color * shimmer * k;
        line.widthMultiplier = width * (0.6f + 0.4f * k);
    }
}

// A chain's head racing out from its anchor and bending toward the player; its path is drawn as a chain behind it.
// It binds whoever it touches (once), then the owner calls Retract.
public class ChainHead : MonoBehaviour
{
    private Vector3 anchor, dir;
    private float speed, turn, life, t;
    private Transform target;
    private GoldChain chain;
    private readonly List<Vector3> path = new List<Vector3>();
    private GameObject head;
    private bool retracting;
    private float retractT;

    public bool Touched { get; private set; }
    public bool Done { get; private set; }
    public Vector3 Tip => transform.position;

    public static ChainHead Launch(Vector3 anchor, Vector3 dir, float speed, float turnDegrees, float life, Transform target, Color color, float height = 0.6f)
    {
        var go = new GameObject("ChainHead");
        go.transform.position = new Vector3(anchor.x, height, anchor.z);
        var h = go.AddComponent<ChainHead>();
        h.anchor = go.transform.position;
        dir.y = 0f;
        h.dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
        h.speed = speed;
        h.turn = turnDegrees;
        h.life = life;
        h.target = target;
        h.chain = GoldChain.Make(0.55f, color);
        h.path.Add(h.anchor);
        h.head = Vfx.Glow(go.transform, Vector3.zero, 1.1f, color * 1.5f, "hm_orb");
        Vfx.Sparkle(go, color, 4f, 0.12f);
        return h;
    }

    public void Retract()
    {
        if (retracting) return;
        retracting = true;
        retractT = 0f;
    }

    private void Update()
    {
        if (retracting)
        {
            retractT += Time.deltaTime;
            float k = Mathf.Clamp01(retractT / 0.35f);
            int keep = Mathf.Max(1, Mathf.RoundToInt(path.Count * (1f - k)));
            if (path.Count > keep)
                path.RemoveRange(keep, path.Count - keep);
            transform.position = path[path.Count - 1];
            chain?.SetPath(path);
            if (k >= 1f)
            {
                Done = true;
                if (chain != null) Destroy(chain.gameObject);
                Destroy(gameObject);
            }
            return;
        }
        t += Time.deltaTime;
        if (t >= life || Touched)
        {
            if (!Touched)
                Done = true;
            return;
        }
        if (target != null)
        {
            Vector3 to = target.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                dir = Vector3.RotateTowards(dir, to.normalized, turn * Mathf.Deg2Rad * Time.deltaTime, 0f);
        }
        transform.position += dir * speed * Time.deltaTime;
        path.Add(transform.position);
        chain?.SetPath(path);
        if (target != null)
        {
            Vector3 d = target.position - transform.position;
            d.y = 0f;
            if (d.magnitude <= 0.85f)
                Touched = true;
        }
    }

    private void OnDestroy()
    {
        if (chain != null) Destroy(chain.gameObject);
    }
}

// A bolt of light (VARCO hm_orb): flies straight; can split in two after a distance, or bend toward the player.
// It hurts the player once on contact, then bursts.
public class LightBolt : MonoBehaviour
{
    private Vector3 dir;
    private float speed, life, t, travelled, radius, damage;
    private float splitAt = -1f, splitAngle;
    private float bendTurn, bendLimit, bent;
    private Transform target;
    private Enemy source;
    private PlayerStats victim;
    private Color color;
    private float size;

    public static LightBolt Fire(Vector3 from, Vector3 dir, float speed, float life, float size, Color color, Enemy source, PlayerStats victim, float damage)
    {
        var go = new GameObject("LightBolt");
        go.transform.position = new Vector3(from.x, 1.1f, from.z);
        var b = go.AddComponent<LightBolt>();
        dir.y = 0f;
        b.dir = dir.normalized;
        b.speed = speed;
        b.life = life;
        b.size = size;
        b.radius = size * 0.45f;
        b.color = color;
        b.source = source;
        b.victim = victim;
        b.damage = damage;
        b.target = victim != null ? victim.transform : null;
        Vfx.Glow(go.transform, Vector3.zero, size, color * 1.6f, "hm_orb", 120f);
        Vfx.Glow(go.transform, Vector3.zero, size * 0.5f, Color.white * 1.4f, "flare");
        Vfx.Trail(go, color, size * 0.7f, 0.25f);
        return b;
    }

    // after `distance`, the bolt parts into two flying ±angle off its course
    public LightBolt SplitAfter(float distance, float angle) { splitAt = distance; splitAngle = angle; return this; }
    // bends toward the player at `degreesPerSecond`, up to `limit` degrees in all
    public LightBolt Bend(float degreesPerSecond, float limit) { bendTurn = degreesPerSecond; bendLimit = limit; return this; }

    private void Update()
    {
        float dt = Time.deltaTime;
        t += dt;
        if (bendTurn > 0f && target != null && bent < bendLimit)
        {
            Vector3 to = target.position - transform.position;
            to.y = 0f;
            float before = Vector3.SignedAngle(dir, to, Vector3.up);
            float step = Mathf.Min(Mathf.Abs(before), bendTurn * dt, bendLimit - bent);
            dir = Quaternion.Euler(0f, Mathf.Sign(before) * step, 0f) * dir;
            bent += step;
        }
        Vector3 move = dir * speed * dt;
        transform.position += move;
        travelled += move.magnitude;
        if (splitAt > 0f && travelled >= splitAt)
        {
            HeimdallFx.Impact(transform.position, size * 1.4f, color);
            foreach (float a in new[] { -splitAngle, splitAngle })
                Fire(transform.position, Quaternion.Euler(0f, a, 0f) * dir, speed * 1.05f, life - t, size * 0.8f, color, source, victim, damage);
            Destroy(gameObject);
            return;
        }
        if (victim != null)
        {
            Vector3 d = victim.transform.position - transform.position;
            d.y = 0f;
            if (d.magnitude <= radius + 0.4f)
            {
                victim.TakeDamage(damage, source);
                HeimdallFx.Impact(transform.position, size * 1.6f, color);
                Destroy(gameObject);
                return;
            }
        }
        if (t >= life)
        {
            Vfx.Flash(transform.position, size, color, 0.2f);
            Destroy(gameObject);
        }
    }
}

// Heimdall's 빛의 분열: a great orb flies at the player; after a distance it splits in three that fan apart, then
// all three swing round onto the spot the player stood on at that moment and burst together there
public class SplitOrb : MonoBehaviour
{
    private enum Stage { Fly, Fan, Converge, Done }
    private Stage stage;
    private Vector3 dir, focus;
    private float speed, t, splitAt, travelled, damage, burstRadius;
    private Transform[] parts;
    private Vector3[] partDir;
    private Enemy source;
    private PlayerStats victim;
    private Color color;
    private GameObject mark;
    private readonly HashSet<int> struck = new HashSet<int>();
    private bool hitOnce;

    public bool Finished => stage == Stage.Done;

    public static SplitOrb Fire(Vector3 from, Vector3 dir, float speed, float splitAt, float burstRadius, Color color, Enemy source, PlayerStats victim, float damage)
    {
        var go = new GameObject("SplitOrb");
        go.transform.position = new Vector3(from.x, 1.3f, from.z);
        var o = go.AddComponent<SplitOrb>();
        dir.y = 0f;
        o.dir = dir.normalized;
        o.speed = speed;
        o.splitAt = splitAt;
        o.burstRadius = burstRadius;
        o.color = color;
        o.source = source;
        o.victim = victim;
        o.damage = damage;
        o.parts = new[] { Part(go.transform.position, 2.8f, color) };
        o.parts[0].SetParent(go.transform, true);
        return o;
    }

    private static Transform Part(Vector3 at, float size, Color color)
    {
        var p = new GameObject("OrbPart");
        p.transform.position = at;
        Vfx.Glow(p.transform, Vector3.zero, size, color * 1.7f, "hm_orb", 90f);
        Vfx.Glow(p.transform, Vector3.zero, size * 0.45f, Color.white * 1.5f, "flare");
        Vfx.Trail(p, color, size * 0.6f, 0.3f);
        var light = new GameObject("OrbLight").AddComponent<Light>();
        light.transform.SetParent(p.transform, false);
        light.type = LightType.Point;
        light.color = color;
        light.range = size * 3f;
        light.intensity = 2.5f;
        return p.transform;
    }

    private void TouchCheck(Transform part, int id, float r)
    {
        if (victim == null || hitOnce || struck.Contains(id) || part == null) return;
        Vector3 d = victim.transform.position - part.position;
        d.y = 0f;
        if (d.magnitude <= r + 0.4f)
        {
            struck.Add(id);
            hitOnce = true;
            victim.TakeDamage(damage * 0.7f, source);
            HeimdallFx.Impact(part.position, 2f, color);
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        t += dt;
        switch (stage)
        {
            case Stage.Fly:
            {
                Vector3 move = dir * speed * dt;
                parts[0].position += move;
                travelled += move.magnitude;
                TouchCheck(parts[0], 0, 1.2f);
                if (travelled >= splitAt)
                {
                    Vector3 at = parts[0].position;
                    Destroy(parts[0].gameObject);
                    HeimdallFx.Impact(at, 4f, color);
                    GameAudio.Play("hm_orb_split", at, 1f);
                    parts = new Transform[3];
                    partDir = new Vector3[3];
                    float[] angles = { -55f, 0f, 55f };
                    for (int i = 0; i < 3; i++)
                    {
                        parts[i] = Part(at, 1.7f, color);
                        partDir[i] = Quaternion.Euler(0f, angles[i], 0f) * dir;
                    }
                    stage = Stage.Fan;
                    t = 0f;
                }
                break;
            }
            case Stage.Fan:
                for (int i = 0; i < 3; i++)
                {
                    parts[i].position += partDir[i] * speed * 0.8f * dt * Mathf.Lerp(1f, 0.3f, t / 0.75f);
                    TouchCheck(parts[i], i + 1, 0.8f);
                }
                if (t >= 0.75f)
                {
                    // they lock on to where the player stands now, and that spot is marked
                    focus = victim != null ? victim.transform.position : parts[1].position + dir * 5f;
                    focus.y = 0f;
                    mark = Telegraph.Circle(focus, burstRadius, 0.85f);
                    BossSkillFx.Floor("hm_brand", focus, burstRadius * 2.3f, color * 0.9f, 0.9f, 0.6f, 1f, 90f);
                    stage = Stage.Converge;
                    t = 0f;
                    for (int i = 0; i < 3; i++)
                        partDir[i] = parts[i].position;
                }
                break;
            case Stage.Converge:
            {
                float k = Mathf.Clamp01(t / 0.85f);
                float e = k * k;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 start = partDir[i];
                    Vector3 end = new Vector3(focus.x, 1.2f, focus.z);
                    Vector3 side = Vector3.Cross(Vector3.up, (end - start).normalized) * (i - 1) * 2.5f;
                    parts[i].position = Vector3.Lerp(start, end, e) + side * Mathf.Sin(k * Mathf.PI);
                }
                if (k >= 1f)
                {
                    foreach (Transform p in parts)
                        if (p != null) Destroy(p.gameObject);
                    if (mark != null) Destroy(mark);
                    HeimdallFx.Impact(focus + Vector3.up, burstRadius * 1.6f, color);
                    BossSkillFx.Floor("hm_cross", focus, burstRadius * 2.6f, color * 1.2f, 0.6f, 0.6f, 1.2f, 0f);
                    HeimdallFx.ShockRing(focus, burstRadius, color, 0.5f);
                    LightPillar.Spawn(focus, burstRadius * 0.7f, 8f, color);
                    GameAudio.Play("hm_orb_hit", focus, 1f);
                    CameraShake.Shake(0.3f, 0.25f);
                    if (victim != null)
                    {
                        Vector3 d = victim.transform.position - focus;
                        d.y = 0f;
                        if (d.magnitude <= burstRadius + 0.4f)
                            victim.TakeDamage(damage * 1.3f, source);
                    }
                    stage = Stage.Done;
                    Destroy(gameObject, 0.1f);
                }
                break;
            }
        }
    }

    private void OnDestroy()
    {
        if (parts != null)
            foreach (Transform p in parts)
                if (p != null) Destroy(p.gameObject);
        if (mark != null) Destroy(mark);
    }
}

// The arena's lights sink for a dramatic beat and come back: every light in the scene and the ambient light are
// scaled down, so glowing effects (all additive) stand out against the dark
public class WorldDim : MonoBehaviour
{
    private static WorldDim current;
    private readonly Dictionary<Light, float> lights = new Dictionary<Light, float>();
    private float ambient, reflections;
    private float depth, fadeIn, hold, fadeOut, t;

    public static void Run(float depth, float fadeIn, float hold, float fadeOut)
    {
        if (current != null)
        {
            current.Extend(depth, hold, fadeOut);
            return;
        }
        var go = new GameObject("WorldDim");
        current = go.AddComponent<WorldDim>();
        current.Begin(depth, fadeIn, hold, fadeOut);
    }

    public static void Release()
    {
        if (current != null)
            current.t = Mathf.Max(current.t, current.fadeIn + current.hold);
    }

    private void Begin(float d, float i, float h, float o)
    {
        depth = d; fadeIn = Mathf.Max(0.01f, i); hold = h; fadeOut = Mathf.Max(0.01f, o);
        foreach (Light l in FindObjectsByType<Light>())
            if (l != null && l.isActiveAndEnabled)
                lights[l] = l.intensity;
        ambient = RenderSettings.ambientIntensity;
        reflections = RenderSettings.reflectionIntensity;
    }

    private void Extend(float d, float h, float o)
    {
        depth = Mathf.Max(depth, d);
        float k = Level();
        fadeIn = 0.01f;
        hold = h;
        fadeOut = o;
        t = fadeIn * k;
    }

    private float Level()
    {
        if (t < fadeIn) return t / fadeIn;
        if (t < fadeIn + hold) return 1f;
        return 1f - Mathf.Clamp01((t - fadeIn - hold) / fadeOut);
    }

    private void Update()
    {
        t += Time.deltaTime;
        float k = Level();
        float scale = Mathf.Lerp(1f, 1f - depth, k);
        foreach (var kv in lights)
            if (kv.Key != null)
                kv.Key.intensity = kv.Value * scale;
        RenderSettings.ambientIntensity = ambient * scale;
        RenderSettings.reflectionIntensity = reflections * scale;
        if (t >= fadeIn + hold + fadeOut)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        foreach (var kv in lights)
            if (kv.Key != null)
                kv.Key.intensity = kv.Value;
        RenderSettings.ambientIntensity = ambient;
        RenderSettings.reflectionIntensity = reflections;
        if (current == this)
            current = null;
    }
}

// A summoned sword flung point-first down a line; it strikes the player once if it passes over them, and sticks in
// the floor (or bursts, for a heavy one) where it ends
public class FlyingBlade : MonoBehaviour
{
    private Vector3 from, to;
    private float time, t, width, damage, burst;
    private Enemy source;
    private PlayerStats victim;
    private Color color;
    private bool hit;
    private float y;

    public static FlyingBlade Fling(GameObject model, Vector3 from, Vector3 to, float speed, float width, Color color, Enemy source, PlayerStats victim, float damage, float burstRadius = 0f)
    {
        var b = model.AddComponent<FlyingBlade>();
        b.from = from;
        b.to = to;
        b.y = from.y;
        b.time = Mathf.Max(0.08f, Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z)) / speed);
        b.width = width;
        b.color = color;
        b.source = source;
        b.victim = victim;
        b.damage = damage;
        b.burst = burstRadius;
        if (model.GetComponent<TrailRenderer>() == null)
            Vfx.Trail(model, color, width * 0.6f, 0.2f);
        return b;
    }

    private void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / time);
        Vector3 p = Vector3.Lerp(from, to, k);
        Vector3 dir = to - from;
        dir.y = 0f;
        transform.position = new Vector3(p.x, Mathf.Lerp(y, 0.4f, k * k), p.z);
        // point first: the model's base (its point) leads
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.FromToRotation(Vector3.up, -dir.normalized);
        if (!hit && victim != null)
        {
            Vector3 v = victim.transform.position;
            Vector3 a = new Vector3(from.x, 0f, from.z), b = new Vector3(p.x, 0f, p.z);
            Vector3 ab = b - a;
            float s = Mathf.Clamp01(Vector3.Dot(new Vector3(v.x, 0f, v.z) - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            if ((new Vector3(v.x, 0f, v.z) - (a + ab * s)).magnitude <= width / 2f + 0.4f)
            {
                hit = true;
                victim.TakeDamage(damage, source);
                victim.GetComponent<PlayerMovement>()?.Push(dir.normalized * 7f);
            }
        }
        if (k >= 1f)
        {
            Vector3 g = new Vector3(to.x, 0f, to.z);
            if (burst > 0f)
            {
                HeimdallFx.Impact(g + Vector3.up * 0.8f, burst * 1.5f, color);
                HeimdallFx.ShockRing(g, burst, color, 0.45f);
                CameraShake.Shake(0.3f, 0.3f);
                if (victim != null && (new Vector3(victim.transform.position.x, 0f, victim.transform.position.z) - g).magnitude <= burst + 0.4f && !hit)
                {
                    hit = true;
                    victim.TakeDamage(damage, source);
                }
                Destroy(gameObject);
            }
            else
            {
                Vfx.Dust(g, 0.8f, 6);
                Vfx.Flash(g + Vector3.up * 0.5f, 1.2f, color, 0.15f);
                enabled = false;
                Destroy(gameObject, 0.8f);
            }
        }
    }
}

// Swords wheeling round their owner (황금의 검진): they cut whoever they pass through (now and then), until they are
// loosed outward
public class BladeWheel : MonoBehaviour
{
    private readonly List<Transform> blades = new List<Transform>();
    private float radius, spin, angle, nextHit;
    private Enemy source;
    private PlayerStats victim;
    private float damage;

    public IReadOnlyList<Transform> Blades => blades;
    public float Angle => angle;
    public float Radius => radius;

    public static BladeWheel Make(Transform owner, int count, float radius, float spin, float bladeHeight, string model, Color color, Enemy source, PlayerStats victim, float damage)
    {
        var go = new GameObject("BladeWheel");
        go.transform.SetParent(owner, false);
        var w = go.AddComponent<BladeWheel>();
        w.radius = radius; w.spin = spin; w.source = source; w.victim = victim; w.damage = damage;
        for (int i = 0; i < count; i++)
        {
            GameObject b = BossSkillFx.SwordModel(model, bladeHeight);
            if (b == null) continue;
            b.transform.SetParent(go.transform, true);
            Vfx.Trail(b, color, 0.35f, 0.22f);
            Vfx.Glow(b.transform, Vector3.up * 0.5f, 1.2f / Mathf.Max(0.1f, bladeHeight), color, "glow");
            w.blades.Add(b.transform);
        }
        w.Place();
        return w;
    }

    private void Place()
    {
        for (int i = 0; i < blades.Count; i++)
        {
            if (blades[i] == null) continue;
            float a = (angle + i * 360f / blades.Count) * Mathf.Deg2Rad;
            Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            Vector3 world = transform.parent.position + off + Vector3.up * 0.6f;
            blades[i].position = world;
            // blades lying flat, points outward (the point sits on the wheel's rim, the hilt toward him)
            blades[i].rotation = Quaternion.FromToRotation(Vector3.up, -off.normalized);
        }
    }

    private void LateUpdate()
    {
        angle += spin * Time.deltaTime;
        Place();
        if (victim == null || Time.time < nextHit) return;
        foreach (Transform b in blades)
        {
            if (b == null) continue;
            Vector3 d = victim.transform.position - b.position;
            d.y = 0f;
            if (d.magnitude <= 0.9f)
            {
                nextHit = Time.time + 0.7f;
                victim.TakeDamage(damage, source);
                Vector3 away = victim.transform.position - transform.parent.position;
                away.y = 0f;
                victim.GetComponent<PlayerMovement>()?.Push(away.normalized * 7f);
                Vfx.Flash(b.position, 1.2f, HeimdallFx.Gold, 0.15f);
                break;
            }
        }
    }

    // hands the blades over (detached) for flinging; the wheel itself goes
    public List<Transform> Release()
    {
        var list = new List<Transform>(blades);
        foreach (Transform b in list)
            if (b != null) b.SetParent(null, true);
        blades.Clear();
        Destroy(gameObject);
        return list;
    }

    private void OnDestroy()
    {
        foreach (Transform b in blades)
            if (b != null) Destroy(b.gameObject);
    }
}
