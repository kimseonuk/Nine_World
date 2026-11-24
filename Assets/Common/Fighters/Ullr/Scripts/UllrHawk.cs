using System.Collections;
using System.Linq;
using UnityEngine;

// 바람의 매: a hawk of wind loosed from 울르의 활. It flies out over the cursor and wheels there, a painted hawk (seen
// from above, wings beating) laid flat in the air a few metres up; every so often it folds its wings and stoops on the
// monster nearest its circle, strikes it (the bow marks it), and climbs back into the circle, which drifts after the
// monsters it hunts. When its time is up it climbs away and fades.
public class UllrHawk : MonoBehaviour
{
    private const float Height = 2.8f, Radius = 2.6f, Size = 2.4f;
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");

    private UllrBow owner;
    private Vector3 centre;
    private bool hunter, leaving;
    private Transform body;
    private Renderer rend;
    private MaterialPropertyBlock block;
    private Vector3 heading = Vector3.forward;
    private float flap, alpha = 1f;

    public static UllrHawk Release(UllrBow owner, Vector3 from, Vector3 centre, bool hunter)
    {
        var go = new GameObject("UllrHawk");
        go.transform.position = from;
        var hawk = go.AddComponent<UllrHawk>();
        hawk.owner = owner;
        hawk.centre = centre;
        hawk.hunter = hunter;
        hawk.Build();
        hawk.StartCoroutine(hawk.Fly());
        Destroy(go, 12f);   // (never left behind)
        return hawk;
    }

    private void Build()
    {
        var b = new GameObject("Body");
        b.transform.SetParent(transform, false);
        b.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
        rend = b.AddComponent<MeshRenderer>();
        rend.sharedMaterial = Vfx.Material(Vfx.Pick("wind_hawk", "glow"), true);
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        block = new MaterialPropertyBlock();
        body = b.transform;
        Vfx.Trail(gameObject, UllrBow.WindColor * 0.8f, 0.5f, 0.35f);
        SetColor();
    }

    private void SetColor()
    {
        rend.GetPropertyBlock(block);
        block.SetColor(ColorId, UllrBow.WindColor * 1.6f * alpha);
        rend.SetPropertyBlock(block);
    }

    // Sent off early (a new hawk loosed while this one hunts)
    public void Leave()
    {
        leaving = true;
    }

    // Laid flat, head along `heading`; the wings (the picture's width) beating
    private void Pose(float wings)
    {
        if (heading.sqrMagnitude < 0.001f)
            heading = Vector3.forward;
        body.rotation = Quaternion.LookRotation(Vector3.down, heading);
        body.localScale = new Vector3(Size * wings, Size, 1f);
    }

    private void Step(Vector3 to, float wings)
    {
        Vector3 move = to - transform.position;
        move.y = 0f;
        if (move.sqrMagnitude > 0.0004f)
            heading = Vector3.Slerp(heading, move.normalized, 0.35f);
        transform.position = to;
        flap += Time.deltaTime * 11f;
        Pose(wings < 0f ? 0.78f + 0.22f * Mathf.Abs(Mathf.Sin(flap)) : wings);
    }

    private IEnumerator Fly()
    {
        float stay = hunter ? 8f : 5f, every = hunter ? 0.5f : 0.8f, damage = hunter ? 1.1f : 0.85f;
        float angle = Random.Range(0f, Mathf.PI * 2f);
        // out to the circle
        Vector3 start = transform.position;
        Vector3 first = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Radius + Vector3.up * Height;
        float outTime = Mathf.Clamp(Vector3.Distance(start, first) / 22f, 0.15f, 0.6f);
        for (float t = 0f; t < outTime; t += Time.deltaTime)
        {
            Step(Vector3.Lerp(start, first, t / outTime) + Vector3.up * Mathf.Sin(t / outTime * Mathf.PI) * 1.2f, -1f);
            yield return null;
        }
        Vfx.Sprite(Vfx.Pick("wind_vortex", "gs_whirlwind"), new Vector3(centre.x, centre.y + 0.08f, centre.z), Radius * 2.4f, UllrBow.WindColor * 0.8f, 0.5f, 0.5f, 1.1f, 400f, Quaternion.Euler(90f, 0f, 0f));
        float until = Time.time + stay, nextStoop = Time.time + 0.25f;
        while (Time.time < until && !leaving && owner != null)
        {
            // the circle drifts toward the nearest monster in reach of it
            Enemy near = Nearest(centre, 7f);
            if (near != null)
            {
                Vector3 to = new Vector3(near.transform.position.x, centre.y, near.transform.position.z);
                centre = Vector3.MoveTowards(centre, to, 2.5f * Time.deltaTime);
            }
            angle += 1.7f * Time.deltaTime;
            Step(centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Radius + Vector3.up * Height, -1f);
            if (Time.time >= nextStoop)
            {
                nextStoop = Time.time + every;
                Enemy prey = NextPrey(centre, Radius + 3.5f);
                if (prey != null)
                {
                    struckAt[prey] = Time.time;
                    yield return Stoop(prey, damage, angle);
                }
            }
            yield return null;
        }
        // away into the sky, fading
        Vector3 from = transform.position;
        Vector3 away = from + heading * 4f + Vector3.up * 5f;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            Step(Vector3.Lerp(from, away, t / 0.6f), -1f);
            alpha = 1f - t / 0.6f;
            SetColor();
            yield return null;
        }
        Destroy(gameObject);
    }

    // Wings folded, down onto it; the strike; back up into the circle
    private IEnumerator Stoop(Enemy prey, float damage, float angle)
    {
        Vector3 from = transform.position;
        for (float t = 0f; t < 0.17f && prey != null; t += Time.deltaTime)
        {
            float k = t / 0.17f;
            Vector3 at = prey.transform.position + Vector3.up * 0.7f;
            Step(Vector3.Lerp(from, at, k * k), 0.5f);
            yield return null;
        }
        if (prey != null && !prey.IsDead && owner != null)
            owner.HawkStrike(prey, damage);
        Vector3 low = transform.position;
        Vector3 back = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Radius + Vector3.up * Height;
        for (float t = 0f; t < 0.28f; t += Time.deltaTime)
        {
            float k = t / 0.28f;
            Step(Vector3.Lerp(low, back, 1f - (1f - k) * (1f - k)), 1f);
            yield return null;
        }
    }

    // The one in reach it has left alone longest (so the stoops go round the pack), nearest first among equals
    private readonly System.Collections.Generic.Dictionary<Enemy, float> struckAt = new System.Collections.Generic.Dictionary<Enemy, float>();

    private Enemy NextPrey(Vector3 from, float range)
    {
        Enemy best = null;
        float bestLast = float.MaxValue, bestD = float.MaxValue;
        foreach (Enemy e in Enemy.Alive.ToArray())
        {
            if (e == null || e.IsDead)
                continue;
            Vector3 off = e.transform.position - from;
            off.y = 0f;
            float d = off.magnitude;
            if (d > range)
                continue;
            float last = struckAt.TryGetValue(e, out float at) ? at : -1f;
            if (last < bestLast || (Mathf.Approximately(last, bestLast) && d < bestD))
            {
                bestLast = last;
                bestD = d;
                best = e;
            }
        }
        return best;
    }

    private static Enemy Nearest(Vector3 from, float range)
    {
        Enemy best = null;
        float bestD = range;
        foreach (Enemy e in Enemy.Alive.ToArray())
        {
            if (e == null || e.IsDead)
                continue;
            Vector3 off = e.transform.position - from;
            off.y = 0f;
            float d = off.magnitude;
            if (d < bestD)
            {
                bestD = d;
                best = e;
            }
        }
        return best;
    }
}
