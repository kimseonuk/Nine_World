using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Runs the skills: one routine per kind (SkillKind), shaped by the skill's numbers, its level (일반 강화), its boss
// special (보스 특수 강화) and the synergies of what's equipped (스킬 시너지). Every effect is a VARCO sheet (sk_*) in white,
// tinted by the skill's element, with a dark ink twin beneath so it reads on the palace's white marble.
public static class SkillCast
{
    // ── Effects ──
    private static Color InkOf(Color c, float a)
    {
        float m = Mathf.Max(0.001f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
        Color k = new Color(c.r / m, c.g / m, c.b / m) * 0.28f;
        k.a = a;
        return k;
    }

    // (support skills draw no dark under-layer any more — on a wide skill it blacked out the map — and no effect is
    // drawn bigger than MaxFxSize, whatever the skill's reach: the hit area stays, only the picture is held in)
    public const float MaxFxSize = 7f;

    public static VfxPiece Fx(string tex, Vector3 at, float size, Color color, float seconds, Quaternion? lying = null, bool sheet = true,
        float ink = 0.65f, float s0 = 0.92f, float s1 = 1.06f, float spin = 0f)
    {
        // (counting the picture's own swell; a thin beam keeps its length so it still matches the line it hits)
        if (tex != "sk_beam") size = Mathf.Min(size, MaxFxSize / Mathf.Max(1f, s0, s1));
        VfxPiece under = null;
        VfxPiece over = Vfx.Sprite(tex, at, size, color * 1.9f, seconds, s0, s1, spin, lying);
        if (sheet)
        {
            under?.Sheet(4, 4);
            over.Sheet(4, 4);
        }
        if (under != null && seconds <= 0f) under.transform.SetParent(over.transform, true);
        return over;
    }

    private static Quaternion Flat(float yaw) => Quaternion.Euler(90f, yaw, 0f);
    private static Quaternion FlatRandom => Flat(Random.Range(0f, 360f));
    private static float Yaw(Vector3 d) => d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d).eulerAngles.y : 0f;
    private static Vector3 FlatV(Vector3 v) { v.y = 0f; return v; }
    private static Vector3 Ground(Vector3 p) => new Vector3(p.x, 0.06f, p.z);

    // ── Aim: the floor point under the cursor (or straight ahead) ──
    public static Vector3? AimOverride;   // (tests: aim here instead of at the cursor)

    public static Vector3 AimPoint(Transform player, float range)
    {
        if (AimOverride.HasValue)
        {
            Vector3 o = FlatV(AimOverride.Value - player.position);
            return o.magnitude > range ? player.position + o.normalized * range : AimOverride.Value;
        }
        Camera cam = Camera.main;
        Vector3 fallback = player.position + player.forward * range * 0.6f;
        if (cam == null || Mouse.current == null) return fallback;
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        var plane = new Plane(Vector3.up, new Vector3(0f, player.position.y, 0f));
        if (!plane.Raycast(ray, out float d)) return fallback;
        Vector3 p = ray.GetPoint(d);
        Vector3 off = FlatV(p - player.position);
        if (off.magnitude > range) p = player.position + off.normalized * range;
        return p;
    }

    // ── Damage: level, special, buffs, synergies, element effects ──
    public static int Damage(SkillInstance s, PlayerStats stats, float scale = 1f)
    {
        float special = s.SpecialDamage * (s.echo ? 0.6f : 1f);
        float d = stats.DamageExact * s.def.power * s.LevelDamage * special * scale;   // (boons are already in stats.Damage)
        if (s.def.Has(SkillTag.Explosion) && SkillSystem.SynergyOn(0)) d *= 1.35f;
        if (SkillSystem.SynergyOn(9)) d *= 1.15f;   // 원소 조화
        return PlayerStats.RoundHit(d);
    }

    public static void Hit(Enemy e, int amount, Vector3 from, SkillInstance s, PlayerStats stats, bool primary = true)
    {
        if (e == null || e.IsDead) return;
        EnemyStatus st = EnemyStatus.Of(e);
        // 빙결 + 강타: frozen enemies take half again from a smash
        if (st != null && st.Frozen && s.def.Has(SkillTag.Smash) && SkillSystem.SynergyOn(1))
            amount = Mathf.RoundToInt(amount * 1.5f);
        // 선혈의 낙인: a bleeding enemy takes more from every skill
        if (st != null && st.Bleeding && SkillSystem.SynergyOn(7))
            amount = Mathf.RoundToInt(amount * 1.15f);
        e.TakeHit(from, amount);
        Vfx.Sprite("foe_hitspark", e.transform.position + Vector3.up * 1f, 1.2f, s.def.Color * 1.6f, 0.25f, 0.9f, 1.1f, Random.Range(0f, 360f)).Sheet(4, 4);
        switch (s.def.element)
        {
            case SkillElement.Fire: st?.Burn(Mathf.Max(1f, amount * 0.12f), 3f); break;
            case SkillElement.Ice: st?.Chill(s.def.Has(SkillTag.Freeze) ? 0.55f : 0.25f); break;
            case SkillElement.Lightning:
                st?.Shock(2f);
                // 번개 + 연쇄: the bolt leaps on to one more enemy nearby
                if (primary && SkillSystem.SynergyOn(2))
                {
                    Enemy next = Nearest(e.transform.position, 6f, e);
                    if (next != null)
                    {
                        Vfx.Bolt(e.transform.position + Vector3.up, next.transform.position + Vector3.up, s.def.Color, 0.3f, 0.2f);
                        Hit(next, Mathf.Max(1, amount / 2), e.transform.position, s, stats, false);
                    }
                }
                break;
        }
        // 대지 + 강타: a smash knocks ordinary enemies down
        if (s.def.Has(SkillTag.Smash) && SkillSystem.SynergyOn(4) && !e.IsBoss && !e.IsDead)
            e.Knockdown(0.8f);
        // 출혈: wounds that keep bleeding (선혈 파열 bursts them instead of adding more)
        if (s.def.element == SkillElement.Blood && st != null && !e.IsDead)
        {
            int stacks = s.def.id == "rend" ? 3 : s.def.id == "bloodneedle" ? 2 : s.def.id == "rupture" ? 0 : 1;
            if (stacks > 0) st.Bleed(stacks, BleedPerStack(s, stats));
        }
    }

    // One stack's bleeding each half second: a share of the player's damage, growing with the skill's level
    public static float BleedPerStack(SkillInstance s, PlayerStats stats) =>
        Mathf.Max(1f, stats.DamageExact * 0.15f * s.LevelDamage * s.SpecialDamage);

    // The art for a bleed skill: its own VARCO sheet when it's in, otherwise the shared one
    private static string BleedFx(SkillDef d, string fallback) => d.id switch
    {
        "rend" => Vfx.Pick("fx_bleed_slash", fallback),
        "bloodneedle" => Vfx.Pick("fx_bleed_needle", fallback),
        "rupture" => Vfx.Pick("fx_bleed_burst", fallback),
        "bloodpool" => Vfx.Pick("fx_bleed_pool", fallback),
        _ => fallback,
    };

    public static Enemy Nearest(Vector3 at, float within, Enemy except = null, ICollection<Enemy> skip = null)
    {
        Enemy best = null;
        float bestD = within;
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead || e == except || (skip != null && skip.Contains(e))) continue;
            float d = FlatV(e.transform.position - at).magnitude;
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    public static List<Enemy> InCircle(Vector3 at, float radius)
    {
        var list = new List<Enemy>();
        foreach (Enemy e in Enemy.Alive)
            if (e != null && !e.IsDead && FlatV(e.transform.position - at).magnitude <= radius + 0.5f)
                list.Add(e);
        return list;
    }

    public static List<Enemy> InLine(Vector3 from, Vector3 dir, float length, float width)
    {
        var list = new List<Enemy>();
        dir = FlatV(dir).normalized;
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead) continue;
            Vector3 rel = FlatV(e.transform.position - from);
            float along = Vector3.Dot(rel, dir);
            if (along >= -0.5f && along <= length && Vector3.Cross(dir, rel).magnitude <= width / 2f + 0.5f)
                list.Add(e);
        }
        return list;
    }

    // A blast: hits everyone in it, with an explosion sheet; 충격파 adds a second one a beat later
    public static void Blast(MonoBehaviour host, Vector3 at, float radius, SkillInstance s, PlayerStats stats, float scale = 1f, bool allowAftershock = true)
    {
        int dmg = Damage(s, stats, scale);
        Fx("sk_burst", Ground(at) + Vector3.up * 0.05f, radius * 2.4f, s.def.Color, 0.55f, FlatRandom, ink: 0.7f);
        foreach (Enemy e in InCircle(at, radius))
            Hit(e, dmg, at, s, stats);
        EnemyFeel.Shake(at, 0.15f, 0.08f + radius * 0.015f);
        if (allowAftershock && s.special == SkillSpecial.Aftershock && host != null)
            host.StartCoroutine(Aftershock(host, at, radius * 1.3f, s, stats, scale * 0.5f));
    }

    private static IEnumerator Aftershock(MonoBehaviour host, Vector3 at, float radius, SkillInstance s, PlayerStats stats, float scale)
    {
        yield return new WaitForSeconds(0.35f);
        Fx("sk_nova", Ground(at) + Vector3.up * 0.07f, radius * 2.2f, s.def.Color, 0.5f, FlatRandom, ink: 0.6f);
        Blast(host, at, radius, s, stats, scale, false);
    }

    // ═══════════ The casts ═══════════
    public static IEnumerator Cast(MonoBehaviour host, SkillInstance s, Transform player, PlayerStats stats)
    {
        SkillDef d = s.def;
        float wide = s.special == SkillSpecial.Wide ? 1.6f : 1f;
        Vector3 aim = AimPoint(player, d.range > 0f ? d.range : 10f);
        Vector3 dir = FlatV(aim - player.position);
        if (dir.sqrMagnitude < 0.01f) dir = player.forward;
        dir.Normalize();
        player.rotation = Quaternion.LookRotation(dir);
        Vector3 chest = player.position + Vector3.up * 1f;
        GameAudio.Play("skill");
        // and the sound of what it is: thunder, a blast, the earth, wind, a step through shadow, stone
        string sid = d.id ?? "";
        if (sid.Contains("lightning") || sid == "judgment") GameAudio.Play("lightning", null, 0.9f);
        else if (sid == "meteor" || sid == "holyburst" || sid == "rupture") GameAudio.Play("explosion", null, 0.8f);
        else if (sid.StartsWith("earth")) GameAudio.Play("slam", null, 0.8f);
        else if (sid == "gust" || sid.Contains("tornado")) GameAudio.Play("whoosh_big", null, 0.9f);
        else if (sid == "shadowstep") GameAudio.Play("dash", null, 1f, 0.8f);
        else if (sid == "stoneshield") GameAudio.Play("block", null, 0.8f, 0.8f);

        switch (d.kind)
        {
            case SkillKind.Projectile:
            {
                int count = d.count + (s.special == SkillSpecial.Split ? 2 : 0);
                float spread = d.count > 1 ? 14f : 12f;
                for (int i = 0; i < count; i++)
                {
                    float a = (i - (count - 1) / 2f) * spread;
                    SkillProjectile.Fire(host, s, stats, chest + dir * 0.6f, Quaternion.Euler(0f, a, 0f) * dir);
                }
                Fx("foe_glint", chest + dir * 0.8f, 1.1f, d.Color, 0.25f, ink: 0.4f);
                break;
            }
            case SkillKind.Strike:
            {
                var spots = new List<Vector3>();
                if (d.id == "judgment")
                {
                    var hit = new List<Enemy>();
                    for (int i = 0; i < d.count; i++)
                    {
                        Enemy e = Nearest(aim, d.range, null, hit);
                        if (e == null) break;
                        hit.Add(e);
                        spots.Add(e.transform.position);
                    }
                    if (spots.Count == 0) spots.Add(aim);
                }
                else
                {
                    spots.Add(aim);
                    for (int i = 1; i < d.count; i++)
                        spots.Add(aim + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(1.5f, 3.2f));
                }
                if (s.special == SkillSpecial.Split)
                    for (int i = 0; i < 2; i++)
                        spots.Add(aim + Quaternion.Euler(0f, 90f + 180f * i, 0f) * dir * 3.2f);
                foreach (Vector3 p in spots)
                    host.StartCoroutine(StrikeAt(host, s, stats, p, d.radius * wide));
                break;
            }
            case SkillKind.Nova:
            {
                float r = d.radius * wide;
                Fx("sk_nova", Ground(player.position) + Vector3.up * 0.06f, r * 2.3f, d.Color, 0.55f, FlatRandom, ink: 0.7f);
                Fx(d.fx == "sk_nova" ? "sk_burst" : BleedFx(d, d.fx), Ground(player.position) + Vector3.up * 0.08f, r * 1.6f, d.Color, 0.45f, FlatRandom, ink: 0.5f);
                int dmg = Damage(s, stats);
                foreach (Enemy e in InCircle(player.position, r))
                {
                    // 선혈 파열: the wounds burst — all the bleeding still to come, at once
                    if (d.id == "rupture")
                    {
                        EnemyStatus est = EnemyStatus.Of(e);
                        int burst = est != null ? est.BurstBleed() : 0;
                        if (burst > 0 && !e.IsDead)
                        {
                            e.TakeHit(player.position, burst);
                            Fx(BleedFx(d, "sk_burst"), e.transform.position + Vector3.up * 1f, 2.6f, d.Color, 0.5f, ink: 0.75f);
                            if (TrainingRoom.Active) DualBlades.Say(e.transform.position + Vector3.up * 2.6f, $"파열 {burst}", d.Color);
                        }
                    }
                    Hit(e, dmg, player.position, s, stats);
                    if (d.id == "gust" && !e.IsBoss && !e.IsDead) e.Knockdown(0.9f);
                }
                if (d.id == "holyburst") stats.Heal(stats.MaxHealth * 0.06f);
                EnemyFeel.Shake(player.position, 0.2f, 0.12f);
                break;
            }
            case SkillKind.Smash:
            {
                float r = d.radius * wide;
                Vector3 c = player.position + dir * r * 0.75f;
                yield return new WaitForSeconds(0.12f);
                if (d.fx == "sk_spike")
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 p = c + Quaternion.Euler(0f, i * 60f + Random.Range(-15f, 15f), 0f) * Vector3.forward * Random.Range(0.3f, r * 0.8f);
                        Fx("sk_spike", new Vector3(p.x, 0.9f, p.z), 2f, d.Color, 0.6f, ink: 0.55f);
                    }
                else if (d.id == "rend" && BleedFx(d, "") != "")
                    Fx(BleedFx(d, ""), Ground(c) + Vector3.up * 0.9f, r * 2.4f, d.Color, 0.4f, Flat(Yaw(dir)), ink: 0.75f);
                else if (d.fx == "sk_blade")
                    Fx("foe_slash", Ground(c) + Vector3.up * 0.9f, r * 2.2f, d.Color, 0.35f, Flat(Yaw(dir)), ink: 0.7f);
                Blast(host, c, r, s, stats);
                if (d.id == "earthsmash")
                    foreach (Enemy e in InCircle(c, r)) if (!e.IsBoss && !e.IsDead) e.Knockdown(0.7f);
                EnemyFeel.Shake(c, 0.2f, 0.15f);
                break;
            }
            case SkillKind.Line:
            {
                float length = d.range * (s.special == SkillSpecial.ChainPierce ? 1.5f : 1f), width = d.radius * 2f;
                Vector3 from = player.position;
                int dmg = Damage(s, stats);
                if (d.fx == "sk_beam")
                {
                    // (the beam texture is a thin band across a square: drawn as long as the line, it's a beam that long)
                    Fx("sk_beam", Ground(from + dir * length / 2f) + Vector3.up * 0.9f, length, d.Color, 0.35f, Flat(Yaw(dir) - 90f), false, 0.6f, 0.95f, 1f);
                    foreach (Enemy e in InLine(from, dir, length, width)) Hit(e, dmg, from, s, stats);
                }
                else if (d.fx == "sk_spike")
                {
                    var hitOnce = new HashSet<Enemy>();
                    for (float t = 1.2f; t <= length; t += 1.4f)
                    {
                        Vector3 p = from + dir * t;
                        Fx("sk_spike", new Vector3(p.x, 0.9f, p.z), 2.2f, d.Color, 0.6f, ink: 0.55f);
                        foreach (Enemy e in InCircle(p, d.radius))
                            if (hitOnce.Add(e)) Hit(e, dmg, from, s, stats);
                        yield return new WaitForSeconds(0.035f);
                    }
                }
                else
                {
                    // a blade of wind racing along the line
                    SkillProjectile.Fire(host, s, stats, from + Vector3.up * 0.9f + dir * 0.5f, dir, lineBlade: true, lineLength: length);
                }
                if (s.special == SkillSpecial.Aftershock)
                    host.StartCoroutine(Aftershock(host, from + dir * length * 0.7f, d.radius * 2f, s, stats, 0.5f));
                break;
            }
            case SkillKind.Field:
            {
                Vector3 at = d.id == "earthquake" ? player.position : aim;
                float duration = s.Duration + (SkillSystem.SynergyOn(8) ? 2f : 0f);
                SkillField.Spawn(s, stats, Ground(at), d.radius * wide, duration, d.id == "earthquake" ? player : null);
                break;
            }
            case SkillKind.Dash:
            {
                float length = d.range * (s.special == SkillSpecial.ChainPierce ? 1.4f : 1f);
                var cc = player.GetComponent<CharacterController>();
                Vector3 start = player.position;
                var hitOnce = new HashSet<Enemy>();
                int dmg = Damage(s, stats);
                EnemyFeel.Lunge(start, dir, d.Color, 1.1f);
                if (d.id == "shadowstep")
                    Fx("sk_burst", Ground(start) + Vector3.up * 0.06f, 3f, d.Color, 0.4f, FlatRandom, ink: 0.7f);
                const float time = 0.18f;
                for (float t = 0f; t < time; t += Time.deltaTime)
                {
                    Vector3 step = dir * length * Time.deltaTime / time;
                    if (cc != null && cc.enabled) cc.Move(step); else player.position += step;
                    if (d.id != "shadowstep")
                        foreach (Enemy e in InCircle(player.position, d.radius))
                            if (hitOnce.Add(e)) Hit(e, dmg, player.position - dir, s, stats);
                    yield return null;
                }
                if (d.id == "lightningdash")
                    for (int i = 0; i < 4; i++)
                        Fx("sk_bolt", Vector3.Lerp(start, player.position, (i + 0.5f) / 4f) + Vector3.up * 1.2f, 2.6f, d.Color, 0.3f, ink: 0.5f);
                if (d.id == "shadowstep" || SkillSystem.SynergyOn(6) && d.element == SkillElement.Shadow)
                    Blast(host, player.position, d.radius, s, stats);
                else if (SkillSystem.SynergyOn(6))
                    Blast(host, player.position, 2f, s, stats, 0.5f);
                else if (s.special == SkillSpecial.Aftershock)
                    Blast(host, player.position, d.radius * 1.4f, s, stats, 0.5f, false);
                break;
            }
            case SkillKind.Chain:
            {
                int jumps = d.count * (s.special == SkillSpecial.ChainPierce ? 2 : 1) + (d.element == SkillElement.Lightning && SkillSystem.SynergyOn(2) ? 1 : 0);
                var hit = new List<Enemy>();
                Vector3 from = chest;
                Enemy target = Nearest(aim, 6f) ?? Nearest(player.position, d.range);
                int dmg = Damage(s, stats);
                float healed = 0f;
                for (int i = 0; i < jumps && target != null; i++)
                {
                    Vector3 to = target.transform.position + Vector3.up * 1f;
                    Vfx.Bolt(from, to, d.Color, d.element == SkillElement.Blood ? 0.45f : 0.35f, 0.25f);
                    Fx(d.element == SkillElement.Blood ? "sk_burst" : "sk_bolt", d.element == SkillElement.Blood ? to : to + Vector3.up * 0.6f,
                        d.element == SkillElement.Blood ? 1.6f : 2.4f, d.Color, 0.3f, ink: 0.5f);
                    Hit(target, dmg, from, s, stats, i == 0);
                    healed += dmg;
                    hit.Add(target);
                    from = to;
                    target = Nearest(target.transform.position, 7f, null, hit);
                    yield return new WaitForSeconds(0.06f);
                }
                break;
            }
            case SkillKind.Orbit:
            {
                int count = d.count + (s.special == SkillSpecial.Split ? 2 : 0);
                SkillOrbit.Spawn(s, stats, player, count, d.radius * wide, s.Duration);
                break;
            }
            case SkillKind.Buff:
            {
                SkillSystem.StartBuff(s);
                // the boon's effect plays once as it's cast; how long is left shows in the buff row (top left)
                var aura = Fx(d.id == "stoneshield" ? "sk_shield" : "sk_aura", player.position + Vector3.up * 1.1f, 2.8f, d.Color, 0.9f);
                aura.transform.SetParent(player, true);
                // 신성 + 강화: a boon heals as it's cast
                if (SkillSystem.SynergyOn(5)) stats.Heal(stats.MaxHealth * 0.1f);
                break;
            }
        }
    }

    private static IEnumerator StrikeAt(MonoBehaviour host, SkillInstance s, PlayerStats stats, Vector3 at, float radius)
    {
        SkillDef d = s.def;
        float delay = d.id == "meteor" ? 0.8f : d.id == "judgment" ? 0.45f : 0.3f;
        GameObject mark = Telegraph.Circle(Ground(at), radius, delay);
        if (d.id == "meteor")
        {
            // the meteor falls in from high up
            VfxPiece rock = Vfx.Sprite("sk_meteor", at + new Vector3(6f, 14f, 0f), 6f, d.Color * 2f, delay, 1f, 1f, 0f);
            for (float t = 0f; t < delay && rock != null; t += Time.deltaTime)
            {
                rock.transform.position = Vector3.Lerp(at + new Vector3(6f, 14f, 0f), at + Vector3.up * 1f, t / delay);
                yield return null;
            }
        }
        else
            yield return new WaitForSeconds(delay);
        if (mark != null) Object.Destroy(mark);
        if (d.fx == "sk_pillar")
            Fx("sk_pillar", new Vector3(at.x, radius * 1.6f, at.z), radius * 3.4f, d.Color, 0.7f, ink: 0.55f);
        else if (d.fx == "sk_bolt")
            Fx("sk_bolt", new Vector3(at.x, 2.4f, at.z), 5f, d.Color, 0.35f, ink: 0.55f);
        Blast(host, at, radius, s, stats);
        if (d.id == "meteor") EnemyFeel.Impact(at, radius, d.Color, 0.3f);
    }
}

// ── A flying skill: an orb, a shard, a blade or a tornado ──
public class SkillProjectile : MonoBehaviour
{
    private SkillInstance s;
    private PlayerStats stats;
    private Vector3 dir;
    private float speed, life, travelled, tick;
    private int pierceLeft;
    private bool explodes, lingering, lineBlade;
    private readonly HashSet<Enemy> struck = new HashSet<Enemy>();
    private readonly Dictionary<Enemy, float> nextTick = new Dictionary<Enemy, float>();

    public static void Fire(MonoBehaviour host, SkillInstance s, PlayerStats stats, Vector3 from, Vector3 dir, bool lineBlade = false, float lineLength = 0f)
    {
        SkillDef d = s.def;
        var go = new GameObject("Skill_" + d.id);
        go.transform.position = from;
        var p = go.AddComponent<SkillProjectile>();
        p.s = s;
        p.stats = stats;
        dir.y = 0f;
        p.dir = dir.normalized;
        p.lineBlade = lineBlade;
        p.lingering = d.id == "tornado";
        bool windy = SkillSystem.SynergyOn(3);
        p.speed = (p.lingering ? 6f : lineBlade ? 32f : 20f) * (windy && !p.lingering ? 1.3f : 1f);
        p.life = p.lingering ? s.Duration : (lineBlade ? lineLength : d.range) / p.speed;
        p.explodes = d.Has(SkillTag.Explosion);
        p.pierceLeft = s.special == SkillSpecial.ChainPierce || d.Has(SkillTag.Pierce) || lineBlade ? 999 : (windy ? 1 : 0);
        float yaw = Quaternion.LookRotation(p.dir).eulerAngles.y;
        VfxPiece look;
        string needle = d.id == "bloodneedle" ? Vfx.Pick("fx_bleed_needle", "") : "";
        switch (d.fx)
        {
            // 피바늘: its own needle flipbook (drawn pointing up the sheet: laid along the flight)
            case "sk_shard" when needle != "": look = SkillCast.Fx(needle, from, 1.9f, d.Color, 0f, Quaternion.Euler(90f, yaw, 0f), true, 0.6f, 1f, 1f); break;
            case "sk_shard": look = SkillCast.Fx("sk_shard", from, 2.2f, d.Color, 0f, Quaternion.Euler(90f, yaw - 90f, 0f), false, 0.5f, 1f, 1f); break;
            case "sk_blade": look = SkillCast.Fx("sk_blade", from, lineBlade ? 3.2f : 1.6f, d.Color, 0f, Quaternion.Euler(90f, yaw, 0f), true, 0.5f, 1f, 1f); break;
            case "sk_tornado": look = SkillCast.Fx("sk_tornado", from + Vector3.up * 0.6f, 3.2f, d.Color, 0f, null, true, 0.45f, 1f, 1f); break;
            default: look = SkillCast.Fx("sk_orb", from, 1.6f, d.Color, 0f, null, true, 0.45f, 1f, 1f); break;
        }
        look.transform.SetParent(go.transform, true);
        Vfx.Trail(go, d.Color, p.lingering ? 0.5f : 0.35f, 0.18f);
    }

    private void Update()
    {
        float step = speed * Time.deltaTime;
        transform.position += dir * step;
        travelled += Time.deltaTime;
        SkillDef d = s.def;
        float radius = lingering ? d.radius : lineBlade ? d.radius : Mathf.Max(0.9f, d.radius * 0.5f);
        if (lingering)
        {
            // a tornado: cuts everything inside it over and over
            foreach (Enemy e in SkillCast.InCircle(transform.position, radius))
                if (!nextTick.TryGetValue(e, out float at) || Time.time >= at)
                {
                    nextTick[e] = Time.time + 0.35f;
                    SkillCast.Hit(e, SkillCast.Damage(s, stats), transform.position, s, stats);
                }
        }
        else
        {
            foreach (Enemy e in SkillCast.InCircle(transform.position, radius))
            {
                if (!struck.Add(e)) continue;
                if (explodes)
                {
                    SkillCast.Blast(this, transform.position, d.radius, s, stats);
                    End();
                    return;
                }
                SkillCast.Hit(e, SkillCast.Damage(s, stats), transform.position - dir, s, stats);
                if (s.special == SkillSpecial.Aftershock)
                    SkillCast.Blast(this, transform.position, 1.8f, s, stats, 0.5f, false);
                if (pierceLeft-- <= 0) { End(); return; }
            }
        }
        if (travelled >= life)
        {
            if (explodes) SkillCast.Blast(this, transform.position, d.radius, s, stats);
            End();
        }
    }

    private void End()
    {
        enabled = false;
        Destroy(gameObject, 0.05f);
    }
}

// ── A lasting zone: fire, blizzard, quake or void ──
public class SkillField : MonoBehaviour
{
    private SkillInstance s;
    private PlayerStats stats;
    private float radius, until, tick;
    private Transform follow;

    public static void Spawn(SkillInstance s, PlayerStats stats, Vector3 at, float radius, float seconds, Transform follow)
    {
        var go = new GameObject("SkillField_" + s.def.id);
        go.transform.position = at;
        var f = go.AddComponent<SkillField>();
        f.s = s; f.stats = stats; f.radius = radius; f.until = Time.time + seconds; f.follow = follow;
        string tex = s.def.id == "earthquake" ? "sk_burst" : s.def.id == "bloodpool" ? Vfx.Pick("fx_bleed_pool", "sk_field") : "sk_field";
        VfxPiece look = SkillCast.Fx(tex, at + Vector3.up * 0.05f, radius * 2.3f, s.def.Color, 0f,
            Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), true, 0.6f, 1f, 1f, s.def.id == "voidfield" ? -60f : 25f);
        look.transform.SetParent(go.transform, true);
        if (s.def.id == "blizzard")
            Vfx.Gather(at + Vector3.up * 2f, radius, s.def.Color, seconds, 30f);
        Destroy(go, seconds);
    }

    private void Update()
    {
        if (follow != null)
            transform.position = new Vector3(follow.position.x, 0.06f, follow.position.z);
        if ((tick -= Time.deltaTime) > 0f) return;
        tick = 0.5f;
        int dmg = SkillCast.Damage(s, stats);
        foreach (Enemy e in SkillCast.InCircle(transform.position, radius))
        {
            SkillCast.Hit(e, dmg, transform.position, s, stats);
            if (s.def.id == "voidfield" && !e.IsBoss)
            {
                // the void drags them in toward its heart
                Vector3 pull = transform.position - e.transform.position;
                pull.y = 0f;
                if (pull.magnitude > 0.6f)
                    e.transform.position += pull.normalized * Mathf.Min(0.6f, pull.magnitude - 0.5f);
            }
            if (s.def.id == "earthquake" && !e.IsBoss && Random.value < 0.25f)
                e.Knockdown(0.5f);
            // 피웅덩이: wading through it is slow (and each tick has already added a wound)
            if (s.def.id == "bloodpool")
                EnemySlow.Apply(e, 0.6f, 0.7f, new Color(0.9f, 0.1f, 0.15f, 0.8f));
        }
        if (s.def.id == "earthquake")
            EnemyFeel.Shake(transform.position, 0.2f, 0.06f);
    }
}

// ── Orbs wheeling round the player ──
public class SkillOrbit : MonoBehaviour
{
    private SkillInstance s;
    private PlayerStats stats;
    private Transform player;
    private float radius, spin;
    private readonly List<Transform> orbs = new List<Transform>();
    private readonly Dictionary<Enemy, float> nextTick = new Dictionary<Enemy, float>();

    public static void Spawn(SkillInstance s, PlayerStats stats, Transform player, int count, float radius, float seconds)
    {
        var go = new GameObject("SkillOrbit_" + s.def.id);
        var o = go.AddComponent<SkillOrbit>();
        o.s = s; o.stats = stats; o.player = player; o.radius = radius;
        for (int i = 0; i < count; i++)
        {
            var orb = new GameObject("Orb");
            orb.transform.SetParent(go.transform, false);
            SkillCast.Fx("sk_orb", player.position, 1.1f, s.def.Color, 0f, null, true, 0.4f, 1f, 1f).transform.SetParent(orb.transform, false);
            Vfx.Trail(orb, s.def.Color, 0.3f, 0.2f);
            o.orbs.Add(orb.transform);
        }
        Destroy(go, seconds);
    }

    private void Update()
    {
        if (player == null) { Destroy(gameObject); return; }
        spin += 240f * Time.deltaTime;
        for (int i = 0; i < orbs.Count; i++)
        {
            Vector3 p = player.position + Vector3.up * 1f + Quaternion.Euler(0f, spin + i * 360f / orbs.Count, 0f) * Vector3.forward * radius;
            orbs[i].position = p;
            foreach (Enemy e in SkillCast.InCircle(p, 0.8f))
                if (!nextTick.TryGetValue(e, out float at) || Time.time >= at)
                {
                    nextTick[e] = Time.time + 0.45f;
                    SkillCast.Hit(e, SkillCast.Damage(s, stats), p, s, stats);
                }
        }
    }
}
