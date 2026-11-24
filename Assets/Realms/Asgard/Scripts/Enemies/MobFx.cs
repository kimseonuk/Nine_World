using System.Collections;
using UnityEngine;

// Lasting attacks Asgard's monsters leave on the floor. Each lives on its own object, so it still goes off
// after the monster that cast it has died, and ends with the run.
public static class MobFx
{
    public static readonly Color RuneBlue = new Color(0.45f, 0.75f, 1f);
    public static readonly Color SeidrViolet = new Color(0.72f, 0.32f, 1f);
    public static readonly Color BoarGold = new Color(1f, 0.82f, 0.3f);

    // A ring marked on the floor that blows up when it has filled (폭발 화살, 룬 폭발, 룬 지대). skyArrow: a streak
    // of light drops out of the sky onto it just before.
    public static void DelayedBlast(Vector3 center, float radius, float delay, float damage, Enemy source, Color color, bool skyArrow = false)
    {
        var go = new GameObject("MobBlast");
        go.AddComponent<Runner>().StartCoroutine(Blast(go, center, radius, delay, damage, source, color, skyArrow));
    }

    // A pool of curse on the floor that hurts the player every half second they stand in it (저주 영역)
    public static void CurseField(Vector3 center, float radius, float seconds, float damagePerTick, Enemy source)
    {
        var go = new GameObject("CurseField");
        go.AddComponent<Runner>().StartCoroutine(Field(go, center, radius, seconds, damagePerTick, source));
    }

    // Chains of ghost-light burst up from a marked ring: whoever is caught is hurt and bound (영혼의 사슬)
    public static void SoulChains(Vector3 center, float radius, float delay, float damage, float bindSeconds, Enemy source)
    {
        var go = new GameObject("SoulChains");
        go.AddComponent<Runner>().StartCoroutine(Chains(go, center, radius, delay, damage, bindSeconds, source));
    }

    private static bool RunOver => GameManager.Instance != null
        && (GameManager.Instance.CurrentState == GameManager.State.Dead || GameManager.Instance.CurrentState == GameManager.State.Victory);

    private static IEnumerator Blast(GameObject go, Vector3 center, float radius, float delay, float damage, Enemy source, Color color, bool skyArrow)
    {
        center.y = 0f;
        GameObject warn = Telegraph.Circle(center, radius, delay);
        warn.transform.SetParent(go.transform, true);
        VfxPiece sigil = Vfx.Decal(center + Vector3.up * 0.06f, radius * 2.2f, color * 0.6f, "runes", 60f);
        sigil.transform.SetParent(go.transform, true);
        bool dropped = false;
        for (float t = 0f; t < delay; t += Time.deltaTime)
        {
            if (RunOver)
            {
                Object.Destroy(go);
                yield break;
            }
            sigil.Set(color * (0.6f + t / delay), radius * 2.2f, 0.5f + 0.5f * t / delay);
            if (skyArrow && !dropped && t >= delay - 0.22f)
            {
                dropped = true;
                FallingStreak(center, color, 0.22f);
            }
            yield return null;
        }
        Object.Destroy(warn);
        Object.Destroy(sigil.gameObject);
        Vfx.Flash(center + Vector3.up * 0.5f, radius * 2f, color, 0.3f);
        Vfx.GroundRing(center, radius, color, 0.4f, "shockring");
        Vfx.Burst(center + Vector3.up * 0.3f, color, 18, 7f, 0.2f);
        EnemyFeel.Impact(center, radius, color, 0.16f);
        PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
        if (stats != null && FlatDistance(stats.transform.position, center) <= radius)
            stats.TakeDamage(damage, source);
        Object.Destroy(go, 0.1f);
    }

    // An arrow of light falling straight down onto a spot
    private static void FallingStreak(Vector3 center, Color color, float seconds)
    {
        var go = new GameObject("SkyArrow");
        go.transform.position = center + Vector3.up * 9f;
        Vfx.Glow(go.transform, Vector3.zero, 0.9f, color * 1.5f);
        Vfx.Trail(go, color, 0.35f, 0.2f);
        go.AddComponent<Runner>().StartCoroutine(Fall(go, center, seconds));
    }

    private static IEnumerator Fall(GameObject go, Vector3 center, float seconds)
    {
        Vector3 from = go.transform.position;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            go.transform.position = Vector3.Lerp(from, center, t / seconds);
            yield return null;
        }
        Object.Destroy(go, 0.25f);
    }

    private static IEnumerator Field(GameObject go, Vector3 center, float radius, float seconds, float damagePerTick, Enemy source)
    {
        center.y = 0f;
        // It spreads out from a point, festers, and shrinks away at the end
        VfxPiece pool = Vfx.Decal(center + Vector3.up * 0.05f, 0.1f, SeidrViolet * 0.8f, "tyr_vortex", -35f);
        VfxPiece runes = Vfx.Decal(center + Vector3.up * 0.07f, 0.1f, SeidrViolet, "runes", 25f);
        pool.transform.SetParent(go.transform, true);
        runes.transform.SetParent(go.transform, true);
        var smoke = Vfx.Gather(center + Vector3.up * 0.4f, radius * 0.8f, SeidrViolet * 0.6f, seconds, 18f);
        smoke.transform.SetParent(go.transform, true);
        PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
        float tick = 0.3f;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (RunOver)
                break;
            float grow = Mathf.Clamp01(t / 0.35f) * Mathf.Clamp01((seconds - t) / 0.4f);
            float pulse = 1f + Mathf.Sin(t * 6f) * 0.05f;
            pool.Set(SeidrViolet * 0.8f, radius * 2.3f * grow * pulse, 0.8f);
            runes.Set(SeidrViolet * 1.2f, radius * 2f * grow, 0.9f);
            if ((tick -= Time.deltaTime) <= 0f)
            {
                tick = 0.5f;
                if (stats != null && FlatDistance(stats.transform.position, center) <= radius * grow)
                {
                    stats.TakeDamage(damagePerTick, source);
                    Vfx.Burst(stats.transform.position, SeidrViolet, 5, 2.5f, 0.12f, -0.4f);
                }
            }
            yield return null;
        }
        Object.Destroy(go);
    }

    private static IEnumerator Chains(GameObject go, Vector3 center, float radius, float delay, float damage, float bindSeconds, Enemy source)
    {
        center.y = 0f;
        GameObject warn = Telegraph.Circle(center, radius, delay);
        warn.transform.SetParent(go.transform, true);
        VfxPiece seal = Vfx.Decal(center + Vector3.up * 0.06f, radius * 2.4f, SeidrViolet * 0.5f, "tyr_seal", 90f);
        seal.transform.SetParent(go.transform, true);
        for (float t = 0f; t < delay; t += Time.deltaTime)
        {
            if (RunOver)
            {
                Object.Destroy(go);
                yield break;
            }
            seal.Set(SeidrViolet * (0.5f + t / delay), radius * 2.4f, 0.5f + 0.5f * t / delay);
            yield return null;
        }
        Object.Destroy(warn);
        Object.Destroy(seal.gameObject);

        PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
        bool caught = stats != null && FlatDistance(stats.transform.position, center) <= radius;
        Vector3 anchor = caught ? new Vector3(stats.transform.position.x, 0f, stats.transform.position.z) : center;
        // Ghostly chains lash up out of the floor from all round the ring to the middle
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.PI * 2f / 5f + Random.Range(-0.2f, 0.2f);
            Vector3 root = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 1.1f;
            Vfx.Bolt(root, anchor + Vector3.up * 0.9f, SeidrViolet, 0.18f, caught ? 0.5f : 0.25f);
        }
        Vfx.Flash(anchor + Vector3.up * 0.8f, 1.6f, SeidrViolet, 0.25f);
        if (caught)
        {
            stats.TakeDamage(damage, source);
            PlayerEffects.ApplyBind(0.25f, bindSeconds);
            // The chains keep their hold for a while: a turning seal at the player's feet
            VfxPiece hold = Vfx.Decal(anchor + Vector3.up * 0.06f, 1.6f, SeidrViolet, "tyr_seal", 120f);
            hold.transform.SetParent(go.transform, true);
            for (float t = 0f; t < bindSeconds && !RunOver; t += Time.deltaTime)
            {
                Vector3 p = stats.transform.position;
                hold.transform.position = new Vector3(p.x, 0.06f, p.z);
                yield return null;
            }
        }
        Object.Destroy(go, 0.1f);
    }

    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    // Hosts a floor attack's coroutine
    private class Runner : MonoBehaviour { }
}

// The violet rune of 주술 강화 over an empowered monster's head, with a glow about it, gone when the buff ends
public class EmpowerMark : MonoBehaviour
{
    private Enemy owner;
    private GameObject fx;

    public static void Attach(Enemy enemy)
    {
        var mark = enemy.GetComponent<EmpowerMark>();
        if (mark == null)
        {
            mark = enemy.gameObject.AddComponent<EmpowerMark>();
            mark.owner = enemy;
            mark.Build();
        }
        Vfx.Flash(enemy.transform.position + Vector3.up * mark.Height * 0.6f, mark.Height * 1.2f, MobFx.SeidrViolet, 0.3f);
    }

    private float Height
    {
        get
        {
            float top = transform.position.y + 1.5f;
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
                if (r is SkinnedMeshRenderer || r is MeshRenderer && !r.name.StartsWith("Vfx"))
                    top = Mathf.Max(top, r.bounds.max.y);
            return Mathf.Clamp(top - transform.position.y, 0.8f, 4f);
        }
    }

    private void Build()
    {
        float h = Height;
        fx = new GameObject("Vfx_Empower");
        fx.transform.SetParent(transform, false);
        fx.transform.localScale = Vector3.one / Mathf.Max(0.01f, transform.lossyScale.x);
        // fx is scaled back to world size, so these offsets are in metres
        Vfx.Glow(fx.transform, Vector3.up * (h + 0.45f), 0.9f, MobFx.SeidrViolet * 1.4f, "runes", 120f);
        Vfx.Glow(fx.transform, Vector3.up * h * 0.5f, h * 1.4f, MobFx.SeidrViolet * 0.3f);
        VfxPiece ring = Vfx.Decal(transform.position + Vector3.up * 0.05f, h * 1.2f, MobFx.SeidrViolet * 0.8f, "runes", 70f);
        ring.transform.SetParent(fx.transform, true);
    }

    private void Update()
    {
        if (owner == null || owner.IsDead || !owner.Empowered)
        {
            if (fx != null)
                Destroy(fx);
            Destroy(this);
        }
    }
}
