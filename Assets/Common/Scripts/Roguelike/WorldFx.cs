using System.Collections.Generic;
using UnityEngine;

// Plain coloured URP materials made at runtime, cached by name
public static class Mats
{
    private static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

    public static Material Get(string name, Color color, float smoothness = 0.2f, Color? emission = null)
    {
        if (cache.TryGetValue(name, out Material existing) && existing != null)
            return existing;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        cache[name] = mat;
        return mat;
    }

    // A primitive with this material and no collider unless asked for
    public static GameObject Shape(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 scale, Material mat, bool collider = false, string name = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        if (name != null) go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider)
            Object.Destroy(go.GetComponent<Collider>());
        return go;
    }
}

// Ground warnings for boss attacks: a dark ring that fills up red until the hit lands
public static class Telegraph
{
    public static GameObject Circle(Vector3 center, float radius, float seconds)
    {
        var root = new GameObject("Telegraph");
        root.transform.position = new Vector3(center.x, 0.15f, center.z);   // (clear of raised floors, like the carved floor of Nidhogg's lair)
        Mats.Shape(PrimitiveType.Cylinder, root.transform, Vector3.zero, new Vector3(radius * 2f, 0.01f, radius * 2f),
            Mats.Get("fx_warn_ring", new Color(0.25f, 0.05f, 0.05f)));
        GameObject fill = Mats.Shape(PrimitiveType.Cylinder, root.transform, Vector3.up * 0.01f, Vector3.zero,
            Mats.Get("fx_warn_fill", new Color(0.9f, 0.15f, 0.1f), 0.2f, new Color(0.8f, 0.1f, 0.05f)));
        root.AddComponent<TelegraphFill>().Init(fill.transform, radius * 2f, seconds);
        return root;
    }

    // A strip on the ground along a charge's path, filling from the start toward the end
    public static GameObject Line(Vector3 start, Vector3 direction, float length, float width, float seconds)
    {
        direction.y = 0f;
        var root = new GameObject("TelegraphLine");
        root.transform.position = new Vector3(start.x, 0.15f, start.z);
        root.transform.rotation = Quaternion.LookRotation(direction.normalized);
        Mats.Shape(PrimitiveType.Cube, root.transform, new Vector3(0f, 0f, length / 2f), new Vector3(width, 0.01f, length),
            Mats.Get("fx_warn_ring", new Color(0.25f, 0.05f, 0.05f)));
        GameObject fill = Mats.Shape(PrimitiveType.Cube, root.transform, Vector3.up * 0.01f, new Vector3(width * 0.8f, 0.012f, 0f),
            Mats.Get("fx_warn_fill", new Color(0.9f, 0.15f, 0.1f), 0.2f, new Color(0.8f, 0.1f, 0.05f)));
        root.AddComponent<TelegraphLineFill>().Init(fill.transform, length, seconds);
        return root;
    }

    // A golden strip on the ground marking a safe lane (the gaps in Gjallarhorn's sonic ring)
    public static GameObject SafeLane(Vector3 start, Vector3 direction, float length, float width, float seconds)
    {
        direction.y = 0f;
        var root = new GameObject("TelegraphSafeLane");
        root.transform.position = new Vector3(start.x, 0.155f, start.z);
        root.transform.rotation = Quaternion.LookRotation(direction.normalized);
        Mats.Shape(PrimitiveType.Cube, root.transform, new Vector3(0f, 0f, length / 2f), new Vector3(width, 0.01f, length),
            Mats.Get("fx_safe_lane", new Color(1f, 0.85f, 0.35f), 0.4f, new Color(0.9f, 0.7f, 0.2f)));
        Object.Destroy(root, seconds + 0.1f);
        return root;
    }

    // A spot an attack can't reach (Heimsight's blind spots): a dark hole in a Circle's red fill with a
    // turning golden ward of runes over it
    public static GameObject SafeSpot(Vector3 center, float radius, float seconds)
    {
        var root = new GameObject("TelegraphSafeSpot");
        root.transform.position = new Vector3(center.x, 0.17f, center.z);
        Mats.Shape(PrimitiveType.Cylinder, root.transform, Vector3.zero, new Vector3(radius * 2f, 0.01f, radius * 2f),
            Mats.Get("fx_safe_hole", new Color(0.07f, 0.06f, 0.1f), 0.3f));
        VfxPiece ward = Vfx.Decal(root.transform.position + Vector3.up * 0.02f, radius * 2.4f, Color.white * 0.9f, "ward", 25f);
        ward.transform.SetParent(root.transform, true);
        Object.Destroy(root, seconds + 0.1f);
        return root;
    }
}

public class TelegraphLineFill : MonoBehaviour
{
    private Transform fill;
    private float length;
    private float seconds;
    private float t;

    public void Init(Transform bar, float fullLength, float duration)
    {
        fill = bar;
        length = fullLength;
        seconds = Mathf.Max(0.01f, duration);
    }

    private void Update()
    {
        t += Time.deltaTime;
        float l = Mathf.Clamp01(t / seconds) * length;
        Vector3 s = fill.localScale;
        fill.localScale = new Vector3(s.x, s.y, l);
        fill.localPosition = new Vector3(0f, 0.01f, l / 2f);
    }
}

// Thor's chain lightning: a crackling golden bolt from one foe to the next
public static class LightningFx
{
    public static void Bolt(Vector3 from, Vector3 to)
    {
        Vfx.Bolt(from, to, new Color(1f, 0.9f, 0.45f), 0.8f, 0.22f);
        Vfx.Burst(to, new Color(0.7f, 0.85f, 1f), 8, 5f, 0.12f);
    }
}

// A ring of force racing outward along the ground, with sparks thrown off it (slams, staggers, level-ups)
public static class Shockwave
{
    public static void Spawn(Vector3 center, float radius, Color color, int shards)
    {
        var ground = new Vector3(center.x, center.y > 1.5f ? center.y - 1f : 0f, center.z);
        Vfx.GroundRing(ground, radius, color, Mathf.Lerp(0.3f, 0.55f, Mathf.InverseLerp(1f, 6f, radius)));
        Vfx.GroundRing(ground, radius * 0.6f, Color.Lerp(color, Color.white, 0.5f), 0.25f, "glow");
        Vfx.Burst(ground + Vector3.up * 0.3f, color, shards, radius * 2.2f, 0.16f, 0.6f);
    }
}

// Specks of starlight drifting in the air over a zone: they bob slowly and twinkle
public class StarMotes : MonoBehaviour
{
    private Transform[] motes;
    private Vector3[] home;
    private float[] phase;
    private float[] size;

    public void Init(float halfX, float halfZ, int count)
    {
        Material[] mats =
        {
            Mats.Get("mote_white", Color.white, 0.5f, new Color(1.8f, 1.8f, 2.2f)),
            Mats.Get("mote_violet", new Color(0.75f, 0.6f, 1f), 0.5f, new Color(1.2f, 0.8f, 2.2f)),
            Mats.Get("mote_gold", new Color(1f, 0.85f, 0.5f), 0.5f, new Color(2f, 1.5f, 0.6f)),
        };
        motes = new Transform[count];
        home = new Vector3[count];
        phase = new float[count];
        size = new float[count];
        for (int i = 0; i < count; i++)
        {
            home[i] = new Vector3(Random.Range(-halfX, halfX), Random.Range(0.4f, 3.5f), Random.Range(-halfZ, halfZ));
            phase[i] = Random.Range(0f, 10f);
            size[i] = Random.Range(0.05f, 0.11f);
            motes[i] = Mats.Shape(PrimitiveType.Sphere, transform, home[i], Vector3.one * size[i], mats[Random.Range(0, mats.Length)], false, "Mote").transform;
            motes[i].GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    private void Update()
    {
        if (motes == null)
            return;
        float time = Time.time;
        for (int i = 0; i < motes.Length; i++)
        {
            float p = phase[i] + time * 0.35f;
            motes[i].localPosition = home[i] + new Vector3(Mathf.Sin(p) * 0.4f, Mathf.Sin(p * 1.7f) * 0.25f, Mathf.Cos(p * 0.8f) * 0.4f);
            float twinkle = 0.55f + 0.45f * Mathf.Sin(phase[i] * 3f + time * 2.5f);
            motes[i].localScale = Vector3.one * size[i] * twinkle;
        }
    }
}

// Fire: the light's strength wavers a little, never in step with its neighbours
public class FlickerLight : MonoBehaviour
{
    private Light lamp;
    private float baseIntensity;
    private float seed;

    private void Awake()
    {
        lamp = GetComponent<Light>();
        baseIntensity = lamp.intensity;
        seed = Random.Range(0f, 100f);
    }

    private void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * 3f);
        lamp.intensity = baseIntensity * (0.75f + 0.5f * n);
    }
}

// Something adrift in the open cosmos (a cloister's Bifrost crystal): it rises and sinks slowly and turns
public class FloatBob : MonoBehaviour
{
    private Vector3 home;
    private float seed;

    private void Start()
    {
        home = transform.localPosition;
        seed = Random.Range(0f, 10f);
    }

    private void Update()
    {
        transform.localPosition = home + Vector3.up * (0.6f + Mathf.Sin(Time.time * 0.8f + seed) * 0.35f);
        transform.Rotate(0f, 12f * Time.deltaTime, 0f, Space.World);
    }
}

public class TelegraphFill : MonoBehaviour
{
    private Transform fill;
    private float size;
    private float seconds;
    private float t;

    public void Init(Transform fillDisc, float diameter, float duration)
    {
        fill = fillDisc;
        size = diameter;
        seconds = Mathf.Max(0.01f, duration);
    }

    private void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / seconds) * size;
        fill.localScale = new Vector3(k, 0.012f, k);
    }
}
