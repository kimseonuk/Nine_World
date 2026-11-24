using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 별의 거인 흐림투르: a titan two or three times a man's height, white stone bound in gold, blue light running through
// the cracks of its body and shards of stars drifting about its shoulders.
//   지면 강타   raises its fists and hammers the floor in front of it: a shockwave and glowing cracks
//   별의 응집   stops dead and draws power in (super armour) — its cracks blaze, stones and light float up round it and a
//               great star forms over its head...
//   천체 폭발   ...then bursts out over a wide ring round it
//   운석 낙하   marks several spots round the player and drops meteors on them one after another
// Model: VARCO rigged mesh + VARCO Animate clips (Resources/Mobs/Hrimthyr); effects: VARCO sheets Resources/Vfx/hrim_*.
public class StarGiant : HumanoidEnemy
{
    public static readonly Color StarBlue = new Color(0.45f, 0.8f, 1f);
    public static readonly Color StarGold = new Color(1f, 0.85f, 0.45f);

    [Header("지면 강타")]
    [SerializeField] private float slamRange = 5.6f;          // starts the slam when the player is this close (world metres)
    [SerializeField] private float giantSlamForward = 2.9f;
    [SerializeField] private float giantSlamRadius = 3.4f;
    [SerializeField] private float slamWindup = 1.2f;         // the Slam state (speed 2.2) is entered 1.8 s in: fists come down 1.2 s later
    [SerializeField] private float slamRecover = 1f;
    [SerializeField] private float slamCooldown = 2.2f;

    [Header("별의 응집 → 천체 폭발")]
    [SerializeField] private float gatherTime = 2.6f;
    [SerializeField] private float novaRadius = 7.5f;
    [SerializeField] private float novaDamageMultiplier = 2f;
    [SerializeField] private float novaCooldown = 13f;
    [SerializeField] private float novaRange = 8.5f;          // only when the player is about this close
    [SerializeField] private float burstOffset = 2.5f;        // the Burst clip (speed 1.5) is entered here...
    [SerializeField] private float burstLead = 0.32f;         // ...and its arms fling wide this long after

    [Header("운석 낙하")]
    [SerializeField] private int meteors = 5;
    [SerializeField] private float meteorRadius = 1.8f;
    [SerializeField] private float meteorFirstDelay = 1.2f;
    [SerializeField] private float meteorGap = 0.32f;
    [SerializeField] private float meteorSpread = 4.5f;
    [SerializeField] private float meteorDamageMultiplier = 0.9f;
    [SerializeField] private float meteorCooldown = 9f;
    [SerializeField] private float meteorMinRange = 4.5f;
    [SerializeField] private float meteorMaxRange = 18f;

    private float slamTimer, novaTimer, meteorTimer;
    private bool gathering;
    private float glow = 1f, glowGoal = 1f;    // how bright the cracks burn (emission multiplier)

    private Renderer[] bodyRenderers;
    private MaterialPropertyBlock glowBlock;
    private Color baseEmission = new Color(0.55f, 1f, 1.5f);
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    // star shards drifting about its shoulders
    private readonly List<VfxPiece> shards = new List<VfxPiece>();
    private float shardPhase;

    // what 별의 응집 leaves to clean up if it is cut short
    private readonly List<Object> chargeFx = new List<Object>();

    private const float SigilLift = 0.7f;   // the star over its head (kept low enough to stay in the quarter-view frame)
    private float Height => controller != null ? controller.height * transform.localScale.y : 4f;

    protected override void Awake()
    {
        base.Awake();
        slamTimer = Random.Range(0.3f, 1f);
        novaTimer = Random.Range(5f, 8f);
        meteorTimer = Random.Range(2.5f, 5f);
        bodyRenderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(), r => r is SkinnedMeshRenderer || r is MeshRenderer && !r.name.StartsWith("Vfx"));
        glowBlock = new MaterialPropertyBlock();
        foreach (Renderer r in bodyRenderers)
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(EmissionId))
            {
                baseEmission = r.sharedMaterial.GetColor(EmissionId);
                break;
            }
        Died += OnDied;
    }

    protected override void Start()
    {
        base.Start();
        for (int i = 0; i < 6; i++)
            shards.Add(Vfx.Sprite("hrim_shard", transform.position, 0.45f, StarGold * 1.2f, 0f, 1f, 1f, 0f));
    }

    protected override Color FeelColor => StarBlue;

    protected override Vector3 Think(Vector3 toPlayer, float distance)
    {
        slamTimer -= Time.deltaTime;
        novaTimer -= Time.deltaTime;
        meteorTimer -= Time.deltaTime;
        Vector3 dir = toPlayer / distance;
        if (cooldown > 0f)
            return distance > slamRange * 0.8f ? dir * moveSpeed * 0.5f : Vector3.zero;

        if (novaTimer <= 0f && distance <= novaRange)
        {
            novaTimer = novaCooldown * (HealthFraction < 0.5f ? 0.75f : 1f);
            StartAction(StarGather(), attackCooldown + 0.6f);
            return Vector3.zero;
        }
        if (distance <= slamRange)
        {
            if (slamTimer <= 0f)
            {
                slamTimer = slamCooldown;
                StartAction(GroundSlam(), attackCooldown);
            }
            return Vector3.zero;
        }
        if (meteorTimer <= 0f && distance >= meteorMinRange && distance <= meteorMaxRange)
        {
            meteorTimer = meteorCooldown;
            StartAction(MeteorFall(), attackCooldown + 0.3f);
            return Vector3.zero;
        }
        return dir * moveSpeed;
    }

    // ───────────── 지면 강타 ─────────────

    private IEnumerator GroundSlam()
    {
        FacePlayer();
        PlayAt("Slam", 1.8f);    // (the clip idles for its first 1.8 s)
        Vector3 center = Flat(transform.position + Facing * giantSlamForward);
        warning = Telegraph.Circle(center, giantSlamRadius, slamWindup);
        EnemyFeel.Windup(transform, transform.position + Vector3.up * Height * 0.95f, StarBlue, FeelSize);
        glowGoal = 2.2f;
        yield return new WaitForSeconds(slamWindup);
        if (warning != null)
            Destroy(warning);
        center = Flat(transform.position + Facing * giantSlamForward);

        Vfx.GroundRing(center, giantSlamRadius * 1.15f, StarBlue, 0.55f, "hrim_shockring");
        Vfx.Sprite("hrim_cracks", center + Vector3.up * 0.06f, giantSlamRadius * 2.3f, StarBlue * 1.3f, 2.4f, 0.85f, 1.05f, 0f,
            Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Hold();
        Vfx.Sprite("hrim_slamburst", center + Vector3.up * 1.1f, 3.6f, Color.white * 1.2f, 0.55f, 0.7f, 1.25f, 0f);
        Shockwave.Spawn(center, giantSlamRadius, StarBlue, 18);
        Vfx.Dust(center, giantSlamRadius * 0.8f, 14, new Color(0.9f, 0.92f, 1f, 0.5f));
        EnemyFeel.Impact(center, giantSlamRadius, StarBlue, 0.35f);
        if (!dead && playerStats != null && FlatDistance(player.position, center) <= giantSlamRadius)
        {
            playerStats.TakeDamage(attackDamage * OutgoingDamageMultiplier, this);
            player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - center).normalized * 7f);
        }
        glowGoal = 1f;
        yield return new WaitForSeconds(slamRecover);
    }

    // Crossfades into one of its move states, starting `offset` seconds into the clip
    private void PlayAt(string state, float offset, float fade = 0.2f)
    {
        if (animator == null)
            return;
        int hash = Animator.StringToHash("Base Layer." + state);
        if (animator.HasState(0, hash))
            animator.CrossFadeInFixedTime(hash, fade, 0, offset);
    }

    // ───────────── 별의 응집 → 천체 폭발 ─────────────

    private IEnumerator StarGather()
    {
        gathering = true;
        FacePlayer();
        PlayAt("Gather", 0f, 0.3f);
        Vector3 center = Flat(transform.position);
        float h = Height;
        warning = Telegraph.Circle(center, novaRadius, gatherTime);
        EnemyFeel.Roar(transform, StarBlue, 4f * FeelSize, 0.2f);

        // the great star forming over its head, a rune-ring of stars on the floor, motes drawn in from all round
        VfxPiece sigil = Track(Vfx.Sprite("hrim_starsigil", center + Vector3.up * (h + SigilLift), 0.5f, StarGold, 0f, 1f, 1f, 25f));
        VfxPiece floorStar = Track(Vfx.Decal(center + Vector3.up * 0.2f, novaRadius * 0.6f, StarBlue * 0.4f, "hrim_meteormark", 30f));
        var gather = Vfx.Gather(center + Vector3.up * h * 0.55f, novaRadius, StarBlue, gatherTime, 70f);
        Track(gather != null ? gather.gameObject : null);
        // stones and shards of light lift off the floor round it and hang in the air
        var floaters = new List<(VfxPiece piece, Vector3 from, float rise, float spin)>();
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI * 2f / 12f + Random.Range(-0.2f, 0.2f);
            float r = Random.Range(1.6f, novaRadius * 0.75f);
            Vector3 at = center + new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a)) * r;
            // two in three are lumps of the floor's stone (glowing with the power that lifts them), the rest star shards
            bool stone = i % 3 != 0;
            VfxPiece p = Track(Vfx.Sprite(stone ? Vfx.Pick("hrim_stone", "hrim_shard") : "hrim_shard", at,
                stone ? Random.Range(0.55f, 0.95f) : Random.Range(0.35f, 0.6f),
                stone ? Color.white * 0.9f : (i % 2 == 0 ? StarBlue : StarGold) * 1.3f, 0f, 1f, 1f, Random.Range(-40f, 40f)));
            floaters.Add((p, at, Random.Range(1.2f, 3.2f), Random.Range(0f, 6f)));
        }
        float column = 0f, rumble = 0f;
        for (float t = 0f; t < gatherTime && !dead; t += Time.deltaTime)
        {
            float k = t / gatherTime;
            glowGoal = Mathf.Lerp(1.5f, 7f, k);
            sigil.transform.position = Flat(transform.position) + Vector3.up * (h + SigilLift + Mathf.Sin(t * 3f) * 0.1f);
            sigil.Set(StarGold * Mathf.Lerp(0.6f, 2.2f, k), Mathf.Lerp(0.8f, 3.6f, Mathf.SmoothStep(0f, 1f, k)), Mathf.Lerp(0.4f, 1f, k));
            floorStar.Set(StarBlue * Mathf.Lerp(0.4f, 1.6f, k), Mathf.Lerp(novaRadius * 0.6f, novaRadius * 2.1f, k), Mathf.Lerp(0.3f, 1f, k));
            foreach (var f in floaters)
            {
                float lift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f));
                f.piece.transform.position = f.from + Vector3.up * (f.rise * lift + Mathf.Sin(t * 2.5f + f.spin) * 0.12f);
            }
            // columns of star energy rising round its body
            if ((column -= Time.deltaTime) <= 0f)
            {
                column = Mathf.Lerp(0.28f, 0.1f, k);
                float a = Random.Range(0f, Mathf.PI * 2f);
                Vector3 at = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(0.6f, 2.2f) + Vector3.up * h * 0.45f;
                Vfx.Sprite("hrim_gather", at, h * Random.Range(0.6f, 0.9f), StarBlue * Mathf.Lerp(0.8f, 1.6f, k), 0.7f, 0.8f, 1.15f, 0f);
            }
            if ((rumble -= Time.deltaTime) <= 0f)
            {
                rumble = 0.3f;
                EnemyFeel.Shake(center, 0.25f, 0.03f + 0.06f * k);
            }
            yield return null;
        }
        if (warning != null)
            Destroy(warning);

        // 천체 폭발
        PlayAt("Burst", burstOffset, 0.12f);
        sigil.Set(StarGold * 3f, 4.2f, 1f);
        yield return new WaitForSeconds(burstLead);   // the arms fling wide
        foreach (var f in floaters)
            if (f.piece != null)
                Vfx.Burst(f.piece.transform.position, StarBlue, 4, 4f, 0.12f, 0.3f);
        ClearChargeFx();
        Vfx.Sprite("hrim_nova", center + Vector3.up * 0.1f, novaRadius * 2.3f, Color.white * 1.3f, 0.8f, 0.25f, 1.05f, 30f, Quaternion.Euler(90f, 0f, 0f));
        Vfx.Sprite("hrim_starsigil", center + Vector3.up * (h + SigilLift), 3.6f, StarGold * 2f, 0.5f, 1f, 2.4f, 60f);
        Vfx.Sprite("hrim_meteorblast", center + Vector3.up * h * 0.5f, h * 1.8f, Color.white * 1.2f, 0.6f, 0.6f, 1.3f, 0f);
        Vfx.GroundRing(center, novaRadius, StarBlue, 0.7f, "hrim_shockring");
        Vfx.Flash(center + Vector3.up * h * 0.5f, novaRadius * 1.4f, StarBlue, 0.35f);
        Vfx.Burst(center + Vector3.up * h * 0.5f, StarBlue, 40, 12f, 0.24f, 0.4f);
        Vfx.Burst(center + Vector3.up * h * 0.5f, StarGold, 24, 9f, 0.18f, 0.6f);
        EnemyFeel.Impact(center, novaRadius, StarBlue, 0.5f);
        if (!dead && playerStats != null && FlatDistance(player.position, center) <= novaRadius)
        {
            playerStats.TakeDamage(attackDamage * novaDamageMultiplier * OutgoingDamageMultiplier, this);
            player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - center).normalized * 11f);
        }
        gathering = false;
        glowGoal = 2.5f;
        yield return new WaitForSeconds(0.9f);
        glowGoal = 1f;
        yield return new WaitForSeconds(0.6f);
    }

    private T Track<T>(T o) where T : Object
    {
        if (o != null)
            chargeFx.Add(o);
        return o;
    }

    private void ClearChargeFx()
    {
        foreach (Object o in chargeFx)
        {
            if (o == null) continue;
            if (o is Component c) Destroy(c.gameObject);
            else Destroy(o);
        }
        chargeFx.Clear();
    }

    // ───────────── 운석 낙하 ─────────────

    private IEnumerator MeteorFall()
    {
        FacePlayer();
        PlayAt("Cast", 0.3f);
        glowGoal = 3f;
        EnemyFeel.Windup(transform, transform.position + Vector3.up * Height * 1.05f, StarGold, FeelSize);
        Vfx.Sprite("hrim_starsigil", Flat(transform.position) + Vector3.up * (Height + SigilLift * 0.7f), 2.4f, StarGold * 1.5f, 0.9f, 0.5f, 1.3f, 90f);
        int count = meteors + (HealthFraction < 0.5f ? 2 : 0);
        Vector3 aim = player != null ? Flat(player.position) : Flat(transform.position + transform.forward * 6f);
        var spots = new List<Vector3> { aim };
        for (int tries = 0; spots.Count < count && tries < 60; tries++)
        {
            Vector2 r = Random.insideUnitCircle * meteorSpread;
            Vector3 p = aim + new Vector3(r.x, 0f, r.y);
            if (HomeRoom != null)
                p = Flat(HomeRoom.ClampToFloor(p, 1f));
            bool clear = true;
            foreach (Vector3 s in spots)
                if (FlatDistance(s, p) < meteorRadius * 1.3f) { clear = false; break; }
            if (clear)
                spots.Add(p);
        }
        for (int i = 0; i < spots.Count; i++)
            StarFx.Meteor(spots[i], meteorRadius, meteorFirstDelay + i * meteorGap, attackDamage * meteorDamageMultiplier * OutgoingDamageMultiplier, this);
        yield return new WaitForSeconds(1.1f);
        glowGoal = 1f;
        yield return new WaitForSeconds(0.4f);
    }

    // ───────────── Its body ─────────────

    // The cracks' glow and the star shards round its shoulders
    private void LateUpdate()
    {
        if (bodyRenderers == null)
            return;
        glowBlock ??= new MaterialPropertyBlock();   // (lost on a script reload mid-play)
        glow = Mathf.Lerp(glow, dead ? 0f : glowGoal, 1f - Mathf.Exp(-6f * Time.deltaTime));
        float pulse = 1f + Mathf.Sin(Time.time * 2.3f) * 0.12f;
        foreach (Renderer r in bodyRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(glowBlock);
            glowBlock.SetColor(EmissionId, baseEmission * glow * pulse);
            r.SetPropertyBlock(glowBlock);
        }

        shardPhase += Time.deltaTime * (gathering ? 2.6f : 0.7f);
        float h = Height;
        for (int i = 0; i < shards.Count; i++)
        {
            VfxPiece s = shards[i];
            if (s == null) continue;
            if (dead) { Destroy(s.gameObject); continue; }
            // three about each shoulder and the back, wheeling slowly, bobbing
            float a = shardPhase + i * Mathf.PI * 2f / shards.Count;
            Vector3 local = new Vector3(Mathf.Cos(a) * h * 0.32f, h * (0.82f + 0.06f * Mathf.Sin(a * 2f + i)), Mathf.Sin(a) * h * 0.18f - h * 0.06f);
            s.transform.position = transform.position + transform.rotation * local;
            s.Set(StarGold * (gathering ? 2f : 1.2f) * (0.8f + 0.2f * Mathf.Sin(Time.time * 4f + i)), gathering ? 0.6f : 0.45f);
        }
    }

    // 별의 응집 can't be broken off (super armour); otherwise a broken move cleans up after itself
    public override void Interrupt()
    {
        if (gathering)
            return;
        base.Interrupt();
    }

    // Too heavy to be thrown down: it staggers for a moment instead (not while gathering)
    public override void Knockdown(float seconds)
    {
        if (dead || gathering)
            return;
        base.Interrupt();
        if (animator != null && animator.HasState(0, Animator.StringToHash("Base Layer.Hit")))
            animator.CrossFadeInFixedTime("Hit", 0.08f);
        cooldown = Mathf.Max(cooldown, Mathf.Min(seconds, 0.9f));
    }

    private void OnDied(Enemy _)
    {
        gathering = false;
        ClearChargeFx();
        foreach (VfxPiece s in shards)
            if (s != null)
            {
                Vfx.Burst(s.transform.position, StarGold, 5, 3f, 0.12f, 0.8f);
                Destroy(s.gameObject);
            }
        shards.Clear();
        StartCoroutine(DeathLight());
    }

    // Its light goes out of it: the cracks flare white, then dim, and the stars it held fall away
    private IEnumerator DeathLight()
    {
        Vector3 chest = transform.position + Vector3.up * Height * 0.55f;
        Vfx.Sprite("hrim_starsigil", chest + Vector3.up * Height * 0.4f, 3f, StarGold * 1.8f, 0.8f, 1f, 0.2f, -120f);
        Vfx.Flash(chest, Height, StarBlue, 0.4f);
        Vfx.Burst(chest, StarBlue, 30, 8f, 0.2f, 1f);
        glow = 8f;
        yield return new WaitForSeconds(1.2f);
        Vector3 ground = Flat(transform.position + transform.forward * Height * 0.4f);
        Vfx.Dust(ground, Height * 0.5f, 18, new Color(0.9f, 0.92f, 1f, 0.5f));
        EnemyFeel.Shake(ground, 0.3f, 0.2f);
        Vfx.Sprite("hrim_cracks", ground + Vector3.up * 0.06f, Height * 1.4f, StarBlue, 1.6f, 0.9f, 1.05f, 0f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
    }

    private void OnDestroy()
    {
        ClearChargeFx();
        foreach (VfxPiece s in shards)
            if (s != null)
                Destroy(s.gameObject);
    }
}

// Meteors falling on marked spots: each lives on its own object, so it still lands after the giant has fallen
public static class StarFx
{
    public static void Meteor(Vector3 spot, float radius, float delay, float damage, Enemy source)
    {
        var go = new GameObject("StarMeteor");
        go.AddComponent<Runner>().StartCoroutine(Fall(go, spot, radius, delay, damage, source));
    }

    private static bool RunOver => GameManager.Instance != null
        && (GameManager.Instance.CurrentState == GameManager.State.Dead || GameManager.Instance.CurrentState == GameManager.State.Victory);

    private const float FallTime = 0.5f;

    private static IEnumerator Fall(GameObject go, Vector3 spot, float radius, float delay, float damage, Enemy source)
    {
        spot.y = 0f;
        GameObject warn = Telegraph.Circle(spot, radius, delay);
        warn.transform.SetParent(go.transform, true);
        // (above the telegraph's red fill, which sits at 0.15)
        VfxPiece mark = Vfx.Decal(spot + Vector3.up * 0.2f, radius * 2.4f, StarGiant.StarGold * 0.5f, "hrim_meteormark", 50f);
        mark.transform.SetParent(go.transform, true);
        VfxPiece rock = null;
        Vector3 sky = spot + new Vector3(Random.Range(-1.5f, 1.5f), 13f, Random.Range(-1.5f, 1.5f));
        for (float t = 0f; t < delay; t += Time.deltaTime)
        {
            if (RunOver)
            {
                Object.Destroy(go);
                yield break;
            }
            float k = t / delay;
            mark.Set(Color.Lerp(StarGiant.StarGold, StarGiant.StarBlue, k) * (0.5f + 1.2f * k), radius * 2.4f * (1.15f - 0.15f * k), 0.5f + 0.5f * k);
            float fall = t - (delay - FallTime);
            if (fall >= 0f)
            {
                if (rock == null)
                {
                    rock = Vfx.Sprite("hrim_meteor", sky, 3.2f, Color.white * 1.3f, 0f, 1f, 1f, 0f);
                    rock.transform.SetParent(go.transform, true);
                }
                float f = Mathf.Clamp01(fall / FallTime);
                rock.transform.position = Vector3.Lerp(sky, spot + Vector3.up * 1.2f, f * f);
            }
            yield return null;
        }
        Object.Destroy(go);
        Vfx.Sprite("hrim_meteorblast", spot + Vector3.up * 0.9f, radius * 2.6f, Color.white * 1.2f, 0.5f, 0.6f, 1.25f, 0f);
        Vfx.GroundRing(spot, radius * 1.1f, StarGiant.StarBlue, 0.4f, "hrim_shockring");
        Vfx.Sprite("hrim_cracks", spot + Vector3.up * 0.06f, radius * 2.2f, StarGiant.StarBlue * 1.2f, 1.8f, 0.85f, 1f, 0f,
            Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Hold();
        Vfx.Burst(spot + Vector3.up * 0.4f, StarGiant.StarBlue, 16, 7f, 0.18f, 1f);
        Vfx.Dust(spot, radius * 0.6f, 8, new Color(0.85f, 0.9f, 1f, 0.45f));
        EnemyFeel.Impact(spot, radius, StarGiant.StarBlue, 0.18f);
        PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
        if (stats != null && new Vector2(stats.transform.position.x - spot.x, stats.transform.position.z - spot.z).magnitude <= radius)
            stats.TakeDamage(damage, source);
    }

    private class Runner : MonoBehaviour { }
}
