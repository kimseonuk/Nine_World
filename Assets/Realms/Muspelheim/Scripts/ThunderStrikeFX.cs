using System.Collections.Generic;
using UnityEngine;

// The flash of the thunder strike: jagged lightning bolts dropping from the sky onto the impact point and onto
// each monster it caught, a ring of light racing out across the ground, and a short blue flash of light.
// Spawns itself, plays for a fraction of a second and cleans up.
public class ThunderStrikeFX : MonoBehaviour
{
    const float BoltTime = 0.32f, RingTime = 0.38f, BoltHeight = 14f;

    static readonly Color BoltColor = new Color(0.75f, 0.9f, 1f, 1f);
    static readonly Color RingColor = new Color(0.4f, 0.75f, 1f, 1f);

    readonly List<LineRenderer> bolts = new List<LineRenderer>();
    readonly List<Vector3> boltTargets = new List<Vector3>();
    LineRenderer ring;
    Light flash;
    float radius, age, rejagTimer;
    Vector3 center;

    public static void Spawn(Vector3 center, float radius, IEnumerable<Vector3> extraStrikes, Material material)
    {
        var go = new GameObject("ThunderStrikeFX");
        go.transform.position = center;
        var fx = go.AddComponent<ThunderStrikeFX>();
        fx.center = center;
        fx.radius = radius;
        fx.AddBolt(center, material, 0.45f);
        foreach (var p in extraStrikes) fx.AddBolt(p, material, 0.28f);

        fx.ring = fx.NewLine("Ring", material, 65, 0.5f);
        fx.ring.loop = true;

        fx.flash = new GameObject("Flash").AddComponent<Light>();
        fx.flash.transform.SetParent(go.transform, false);
        fx.flash.transform.localPosition = Vector3.up * 2f;
        fx.flash.type = LightType.Point;
        fx.flash.color = new Color(0.55f, 0.75f, 1f);
        fx.flash.range = radius * 3f;
        fx.flash.intensity = 40f;
    }

    void AddBolt(Vector3 target, Material material, float width)
    {
        var lr = NewLine("Bolt", material, 12, width);
        bolts.Add(lr);
        boltTargets.Add(target);
        Jag(lr, target);
    }

    LineRenderer NewLine(string name, Material material, int points, float width)
    {
        var lr = new GameObject(name).AddComponent<LineRenderer>();
        lr.transform.SetParent(transform, false);
        lr.sharedMaterial = material;
        lr.positionCount = points;
        lr.widthMultiplier = width;
        lr.useWorldSpace = true;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    // a fresh zigzag from high above down to the target, tapering to a point at the ground
    void Jag(LineRenderer lr, Vector3 target)
    {
        int n = lr.positionCount;
        Vector3 top = target + Vector3.up * BoltHeight + new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
        for (int i = 0; i < n; i++)
        {
            float t = i / (n - 1f);
            Vector3 p = Vector3.Lerp(top, target, t);
            if (i > 0 && i < n - 1) p += new Vector3(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f)) * (1f - t * 0.5f);
            lr.SetPosition(i, p);
        }
        lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0.4f));
    }

    void Update()
    {
        age += Time.deltaTime;

        // bolts flicker: re-jag a few times and fade out
        rejagTimer -= Time.deltaTime;
        if (rejagTimer <= 0f && age < BoltTime * 0.7f)
        {
            rejagTimer = 0.05f;
            for (int i = 0; i < bolts.Count; i++) Jag(bolts[i], boltTargets[i]);
        }
        float boltK = Mathf.Clamp01(1f - age / BoltTime);
        float flicker = 0.7f + 0.3f * Mathf.Sin(age * 90f);
        foreach (var b in bolts)
        {
            var c = BoltColor * (boltK * flicker * 2.2f);
            b.startColor = c; b.endColor = c;
        }

        // ring races out and thins
        float ringT = Mathf.Clamp01(age / RingTime);
        float r = radius * (1f - (1f - ringT) * (1f - ringT));
        for (int i = 0; i < ring.positionCount; i++)
        {
            float a = i / (float)ring.positionCount * Mathf.PI * 2f;
            ring.SetPosition(i, center + new Vector3(Mathf.Cos(a) * r, 0.12f, Mathf.Sin(a) * r));
        }
        ring.widthMultiplier = 0.5f * (1f - ringT);
        var rc = RingColor * ((1f - ringT) * 2f);
        ring.startColor = rc; ring.endColor = rc;

        flash.intensity = 40f * boltK * boltK;

        if (age > Mathf.Max(BoltTime, RingTime)) Destroy(gameObject);
    }
}
