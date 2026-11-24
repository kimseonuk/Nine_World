using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 신검 파편 글라디온: a holy sword with no wielder, hanging in the air. Its point always turns to the player as it drifts
// about restlessly; small shards of light circle it. Gathering for a blow, light runs up the blade and its runes blaze;
// struck, its light shudders and flakes of metal fly.
//   돌진 베기   aims, then darts along a straight line, cutting through
//   회전 베기   up close, lays itself flat and whirls round on the spot
//   추적 검격   hunts the player with a lock-on mark for a while, fixes the line, then drives through it hard
//   연속 검격   two or three darts in quick succession, re-aiming between them (three once it's hurt)
// Model: VARCO mesh (Body/Model, point along +Z). Effects: VARCO sheets Resources/Vfx/glad_*.
public class Gladion : Enemy
{
    public static readonly Color HolyBlue = new Color(0.45f, 0.82f, 1f);
    public static readonly Color HolyGold = new Color(1f, 0.85f, 0.45f);

    [SerializeField] private int maxHealth = 7;
    [SerializeField] private float hoverHeight = 1.3f;
    [SerializeField] private float preferredDistance = 5f;
    [SerializeField] private float driftSpeed = 3.2f;
    [SerializeField] private float damage = 12f;
    [SerializeField] private float bladeLength = 1.6f;      // the model's length, in metres before spawn scaling (set by the builder)

    [Header("돌진 베기")]
    [SerializeField] private float dashAim = 0.6f;
    [SerializeField] private float dashSpeed = 22f;
    [SerializeField] private float dashMax = 12f;
    [SerializeField] private float dashOvershoot = 3f;
    [SerializeField] private float dashHitRadius = 1.1f;

    [Header("회전 베기")]
    [SerializeField] private float spinRadius = 3f;
    [SerializeField] private float spinWindup = 0.6f;
    [SerializeField] private float spinCooldown = 4f;

    [Header("추적 검격")]
    [SerializeField] private float trackTime = 1.5f;
    [SerializeField] private float trackLock = 0.35f;
    [SerializeField] private float trackSpeed = 28f;
    [SerializeField] private float trackDamageMultiplier = 1.5f;

    [Header("Cooldowns")]
    [SerializeField] private float attackInterval = 2.4f;

    private Transform player;
    private PlayerStats playerStats;
    private int health;
    private bool dead, busy;
    private float stunnedUntil, phase, attackTimer, spinTimer, shake;
    private Vector3 knockback, wander;
    private Coroutine action;
    private GameObject warning;
    private VfxPiece lockMark;

    private Transform body, model;
    private Quaternion modelRest = Quaternion.identity;
    private Light glowLight;
    private TrailRenderer trail;
    private GameObject runeGlow;
    private float charge;                      // 0..1: light gathered on the blade
    private readonly List<VfxPiece> motes = new List<VfxPiece>();

    private Renderer[] bodyRenderers;
    private MaterialPropertyBlock glowBlock;
    private Color baseEmission = new Color(0.6f, 1.1f, 1.6f);
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;
    private float HealthFraction => (float)Health / Mathf.Max(1, maxHealth);
    private float Size => Mathf.Max(0.1f, transform.lossyScale.x);
    private Vector3 Tip => body.position + body.forward * bladeLength * 0.5f * Size;

    private void Awake()
    {
        health = maxHealth;
        phase = Random.Range(0f, 10f);
        attackTimer = Random.Range(1f, attackInterval);
        spinTimer = Random.Range(0.5f, 2f);
        body = transform.Find("Body");
        if (body == null)
        {
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
        }
        model = body.Find("Model");
        if (model != null)
            modelRest = model.localRotation;
        bodyRenderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(), r => r is MeshRenderer && !r.name.StartsWith("Vfx"));
        foreach (Renderer r in bodyRenderers)
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(EmissionId)) { baseEmission = r.sharedMaterial.GetColor(EmissionId); break; }
        glowBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null)
        {
            player = movement.transform;
            playerStats = movement.GetComponent<PlayerStats>();
        }
        // the light along the blade (brightens as it gathers for a blow), a trail off its point, shards of light round it
        runeGlow = Vfx.Glow(body, Vector3.zero, 0.1f, HolyBlue * 0.2f, "glow", 0f);
        var tipGo = new GameObject("TipTrail");
        tipGo.transform.SetParent(body, false);
        tipGo.transform.localPosition = Vector3.forward * bladeLength * 0.45f;
        trail = Vfx.Trail(tipGo, HolyBlue, 0.25f, 0.18f);
        for (int i = 0; i < 5; i++)
            motes.Add(Vfx.Sprite(Vfx.Pick("glad_mote", "flare"), transform.position, 0.3f, HolyGold, 0f, 1f, 1f, 0f));
        var lightGo = new GameObject("BladeLight");
        lightGo.transform.SetParent(body, false);
        glowLight = lightGo.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = HolyBlue;
        glowLight.range = 3.5f;
        glowLight.intensity = 0.9f;
    }

    public override void Scale(float healthMultiplier, float damageMultiplier)
    {
        maxHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * healthMultiplier));
        health = maxHealth;
        damage *= damageMultiplier;
    }

    private void Update()
    {
        if (dead)
            return;
        phase += Time.deltaTime;
        bool stunned = Time.time < stunnedUntil;
        knockback = Vector3.Lerp(knockback, Vector3.zero, 6f * Time.deltaTime);

        if (!busy)
        {
            Vector3 pos = transform.position;
            float targetHeight = stunned ? 0.5f : hoverHeight + Mathf.Sin(phase * 2.1f) * 0.15f;
            pos.y = Mathf.MoveTowards(pos.y, targetHeight, 3f * Time.deltaTime);
            Vector3 move = knockback;
            if (!stunned && player != null && GameManager.IsRunActive)
            {
                Vector3 to = Flat(player.position - transform.position);
                float distance = to.magnitude;
                Vector3 dir = distance > 0.01f ? to / distance : transform.forward;
                // restless: it darts about on a jittering course while keeping its distance
                if (Random.value < Time.deltaTime * 1.5f)
                    wander = Quaternion.Euler(0f, Random.Range(-110f, 110f), 0f) * dir * Random.Range(0.4f, 1f);
                Vector3 want = (distance - preferredDistance) * dir * 0.8f + Vector3.Cross(Vector3.up, dir) * Mathf.Sin(phase * 1.3f) * 1.4f + wander * 2f;
                move += Vector3.ClampMagnitude(want, driftSpeed);
                Point(player.position + Vector3.up * 0.9f, 540f);

                attackTimer -= Time.deltaTime;
                spinTimer -= Time.deltaTime;
                if (distance <= spinRadius * 0.8f && spinTimer <= 0f)
                {
                    spinTimer = spinCooldown;
                    attackTimer = Mathf.Max(attackTimer, 1f);
                    action = StartCoroutine(Act(SpinSlash()));
                }
                else if (attackTimer <= 0f && distance <= dashMax + 2f)
                {
                    attackTimer = attackInterval;
                    float roll = Random.value;
                    IEnumerator move2 = roll < 0.4f ? DashSlash(dashAim, dashSpeed, 1f)
                        : roll < 0.7f ? TrackingStrike()
                        : Flurry(HealthFraction < 0.5f ? 3 : 2);
                    action = StartCoroutine(Act(move2));
                }
            }
            pos += move * Time.deltaTime;
            transform.position = Clamp(pos);
        }
        Animate(stunned);
    }

    // points the blade at a target (the tip leads)
    private void Point(Vector3 target, float degreesPerSecond)
    {
        Vector3 to = target - body.position;
        if (to.sqrMagnitude < 0.01f)
            return;
        body.rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(to), degreesPerSecond * Time.deltaTime);
    }

    private Vector3 Clamp(Vector3 p) => HomeRoom == null ? p : HomeRoom.ClampToFloor(p, 0.8f);

    private IEnumerator Act(IEnumerator routine)
    {
        busy = true;
        yield return routine;
        EndAct();
    }

    private void EndAct()
    {
        busy = false;
        action = null;
        ClearMarks();
        charge = 0f;
        if (trail != null) trail.widthMultiplier = 1f;
    }

    private void ClearMarks()
    {
        if (warning != null) Destroy(warning);
        warning = null;
        if (lockMark != null) Destroy(lockMark.gameObject);
        lockMark = null;
    }

    // ───────────── 돌진 베기 ─────────────

    // aim: draws back a little as light runs up the blade; then darts along the marked line
    private IEnumerator DashSlash(float aim, float speed, float damageScale, Vector3? fixedDir = null)
    {
        Vector3 dir = fixedDir ?? (player != null ? Flat(player.position - transform.position).normalized : Flat(body.forward).normalized);
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        float length = player != null ? Mathf.Min(Flat(player.position - transform.position).magnitude + dashOvershoot, dashMax) : dashMax * 0.6f;
        if (aim > 0f)
        {
            ClearMarks();
            warning = Telegraph.Line(Flat(transform.position), dir, length, 1.4f, aim);
            Vfx.Sprite(Vfx.Pick("glad_charge", "glow"), body.position, 1.6f * Size, HolyBlue * 1.5f, aim, 0.6f, 1.2f, 0f);
            Vector3 start = transform.position;
            for (float t = 0f; t < aim && !dead; t += Time.deltaTime)
            {
                float k = t / aim;
                charge = k;
                Point(body.position + dir * 5f + Vector3.down * 0.4f, 900f);
                // the wind-up: drawn back, trembling
                transform.position = Clamp(start - dir * 0.6f * Mathf.SmoothStep(0f, 1f, k) + Random.insideUnitSphere * 0.03f * k);
                yield return null;
            }
            ClearMarks();
        }
        yield return Dart(dir, length, speed, damageScale);
    }

    private IEnumerator Dart(Vector3 dir, float length, float speed, float damageScale)
    {
        charge = 1f;
        body.rotation = Quaternion.LookRotation(dir + Vector3.down * 0.05f);
        if (trail != null) trail.widthMultiplier = 3.5f;
        EnemyFeel.Lunge(transform.position, dir, HolyBlue, 1.2f);
        Vector3 from = transform.position;
        // the speed streak along the path (a stretched strip, not a square sprite)
        GladStreak.Spawn(from, from + dir * length, 1.1f, HolyBlue, 0.4f);
        bool hit = false;
        float travelled = 0f;
        Vector3 last = transform.position;
        float y = Mathf.Max(0.9f, transform.position.y);
        while (travelled < length && !dead)
        {
            Vector3 next = transform.position + dir * speed * Time.deltaTime;
            next.y = Mathf.MoveTowards(transform.position.y, 1f, 4f * Time.deltaTime);
            Vector3 clamped = Clamp(next);
            transform.position = clamped;
            float step = Flat(clamped - last).magnitude;
            travelled += step;
            last = clamped;
            if (!hit && playerStats != null && FlatDistance(player.position, transform.position) <= dashHitRadius * Mathf.Max(1f, Size * 0.7f))
            {
                hit = true;
                playerStats.TakeDamage(damage * damageScale * OutgoingDamageMultiplier, this);
                player.GetComponent<PlayerMovement>()?.Push(dir * 6f + Vector3.Cross(Vector3.up, dir) * 2f);
                Vfx.Sprite(Vfx.Pick("glad_slash", "slash"), player.position + Vector3.up * 0.9f, 2.6f, Color.white * 1.3f, 0.3f, 0.8f, 1.2f, 60f);
                Vfx.Sprite(Vfx.Pick("glad_impact", "flare"), player.position + Vector3.up * 0.9f, 1.8f, Color.white * 1.2f, 0.25f, 0.6f, 1.3f, 30f);
                EnemyFeel.Shake(transform.position, 0.12f, 0.1f);
            }
            if (step < speed * Time.deltaTime * 0.25f)   // ran into a wall
                break;
            yield return null;
        }
        // brakes with a flash and a cut of light where it stops
        Vfx.Sprite(Vfx.Pick("glad_slash", "slash"), transform.position, 2.2f * Size, HolyBlue * 1.2f, 0.25f, 0.8f, 1.15f, -50f);
        Vfx.Burst(transform.position, HolyGold, 8, 4f, 0.12f, 0.3f);
        if (trail != null) trail.widthMultiplier = 1f;
        charge = 0f;
        yield return new WaitForSeconds(0.25f);
    }

    // ───────────── 회전 베기 ─────────────

    private IEnumerator SpinSlash()
    {
        EnemyVoice.Of(this)?.Skill();   // (its own big-move sound)
        Vector3 center = Flat(transform.position);
        ClearMarks();
        warning = Telegraph.Circle(center, spinRadius, spinWindup);
        EnemyFeel.Windup(transform, body.position, HolyBlue, 1.1f);
        Quaternion from = body.rotation;
        for (float t = 0f; t < spinWindup && !dead; t += Time.deltaTime)
        {
            float k = t / spinWindup;
            charge = k;
            // lies down flat, point outward, and starts to turn
            body.rotation = Quaternion.Slerp(from, Quaternion.Euler(0f, body.eulerAngles.y + 200f * Time.deltaTime * k, 0f), k);
            yield return null;
        }
        ClearMarks();
        Quaternion flat = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
        Vfx.Sprite(Vfx.Pick("glad_spin", "ring"), center + Vector3.up * 1f, spinRadius * 2.3f, Color.white * 1.3f, 0.55f, 0.75f, 1.1f, 720f, flat);
        Vfx.GroundRing(center, spinRadius, HolyBlue, 0.4f);
        if (trail != null) trail.widthMultiplier = 4f;
        bool hit = false;
        float yaw = body.eulerAngles.y;
        for (float t = 0f; t < 0.6f && !dead; t += Time.deltaTime)
        {
            yaw += 1800f * Time.deltaTime;   // three turns
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (!hit && t > 0.1f && playerStats != null && FlatDistance(player.position, center) <= spinRadius)
            {
                hit = true;
                playerStats.TakeDamage(damage * OutgoingDamageMultiplier, this);
                player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - center).normalized * 6f);
                Vfx.Sprite(Vfx.Pick("glad_impact", "flare"), player.position + Vector3.up * 0.9f, 1.8f, Color.white * 1.2f, 0.25f, 0.6f, 1.3f, 30f);
            }
            yield return null;
        }
        if (trail != null) trail.widthMultiplier = 1f;
        charge = 0f;
        yield return new WaitForSeconds(0.35f);
    }

    // ───────────── 추적 검격 ─────────────

    private IEnumerator TrackingStrike()
    {
        ClearMarks();
        if (player == null)
            yield break;
        lockMark = Vfx.Decal(Flat(player.position) + Vector3.up * 0.2f, 2.2f, HolyBlue, Vfx.Pick("glad_lock", "ring"), 120f);
        // hunts: stays at the player's back-side, point locked on them, the mark sliding after them
        for (float t = 0f; t < trackTime && !dead; t += Time.deltaTime)
        {
            float k = t / trackTime;
            charge = k * 0.8f;
            Vector3 p = Flat(player.position);
            lockMark.transform.position = Vector3.Lerp(lockMark.transform.position, p + Vector3.up * 0.2f, 1f - Mathf.Exp(-10f * Time.deltaTime));
            lockMark.Set(Color.Lerp(HolyBlue, HolyGold, k) * (1f + k), Mathf.Lerp(2.4f, 1.6f, k), 0.6f + 0.4f * k);
            Vector3 away = Flat(transform.position - player.position);
            Vector3 hold = player.position + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back) * 6f;
            hold.y = hoverHeight + 0.4f;
            transform.position = Clamp(Vector3.MoveTowards(transform.position, hold, 7f * Time.deltaTime));
            Point(player.position + Vector3.up * 0.9f, 900f);
            yield return null;
        }
        // the line is fixed: the mark stops, flashes gold, then it drives through
        Vector3 dir = Flat(lockMark.transform.position - transform.position).normalized;
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        float length = Mathf.Min(Flat(lockMark.transform.position - transform.position).magnitude + dashOvershoot + 1f, dashMax + 2f);
        warning = Telegraph.Line(Flat(transform.position), dir, length, 1.6f, trackLock);
        lockMark.Set(HolyGold * 2.4f, 1.6f, 1f);
        Vfx.Sprite(Vfx.Pick("glad_charge", "glow"), body.position, 2f * Size, HolyGold * 1.8f, trackLock, 0.7f, 1.3f, 0f);
        for (float t = 0f; t < trackLock && !dead; t += Time.deltaTime)
        {
            charge = 1f;
            Point(body.position + dir * 5f, 1200f);
            yield return null;
        }
        ClearMarks();
        yield return Dart(dir, length, trackSpeed, trackDamageMultiplier);
    }

    // ───────────── 연속 검격 ─────────────

    private IEnumerator Flurry(int count)
    {
        for (int i = 0; i < count && !dead; i++)
        {
            float aim = i == 0 ? dashAim * 0.8f : 0.32f;
            yield return DashSlash(aim, dashSpeed * 1.1f, 0.8f);
        }
        yield return new WaitForSeconds(0.3f);
    }

    // ───────────── Body ─────────────

    private void Animate(bool stunned)
    {
        if (model != null)
        {
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 3f);
            Vector3 jitter = Random.insideUnitSphere * 0.04f * shake;
            // a slow roll about its own length; stunned it wobbles
            float roll = phase * (busy ? 0f : 40f) + (stunned ? Mathf.Sin(phase * 14f) * 25f : 0f);
            model.localRotation = Quaternion.Euler(0f, 0f, roll) * modelRest;
            model.localPosition = jitter;
        }
        float glow = Mathf.Lerp(1f, 6f, charge);
        if (glowLight != null) glowLight.intensity = Mathf.Lerp(0.9f, 4f, charge);
        if (runeGlow != null)
        {
            var p = runeGlow.GetComponent<VfxPiece>();
            p?.Set(Color.Lerp(HolyBlue, Color.white, charge * 0.4f) * Mathf.Lerp(0.2f, 1.2f, charge), bladeLength * Size * Mathf.Lerp(0.5f, 0.9f, charge), Mathf.Lerp(0.3f, 1f, charge));
        }
        if (bodyRenderers != null)
        {
            glowBlock ??= new MaterialPropertyBlock();
            foreach (Renderer r in bodyRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(glowBlock);
                glowBlock.SetColor(EmissionId, baseEmission * glow * (1f + Mathf.Sin(phase * 3f) * 0.1f));
                r.SetPropertyBlock(glowBlock);
            }
        }
        // shards of light wheeling round the blade (shaken loose when it's hit)
        for (int i = 0; i < motes.Count; i++)
        {
            VfxPiece m = motes[i];
            if (m == null) continue;
            float a = phase * 1.8f + i * Mathf.PI * 2f / motes.Count;
            Vector3 ring = new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.3f) * 0.4f, Mathf.Sin(a)) * 0.7f * Size;
            m.transform.position = body.position + ring + Random.insideUnitSphere * 0.25f * shake;
            m.Set(HolyGold * (0.9f + charge + Mathf.Sin(phase * 5f + i) * 0.2f), 0.3f * Size);
        }
    }

    public override void TakeHit(Vector3 from, int amount)
    {
        if (dead)
            return;
        health -= Mathf.Max(1, Mitigate(amount));
        if (!busy)
            knockback = Flat(transform.position - from).normalized * 4f;
        shake = 1f;
        StartCoroutine(Flash());
        // flakes of metal and a shudder of its light
        Vfx.Sprite(Vfx.Pick("glad_impact", "flare"), body.position, 1.4f * Size, Color.white, 0.22f, 0.6f, 1.2f, 45f);
        Vfx.Burst(body.position, new Color(0.85f, 0.88f, 0.95f), 8, 5f, 0.1f, 1.2f);
        if (health <= 0)
            StartCoroutine(Die());
    }

    public override void Knockdown(float seconds)
    {
        if (dead)
            return;
        Interrupt();
        stunnedUntil = Time.time + seconds;
    }

    public override void Interrupt()
    {
        if (dead || action == null)
            return;
        StopCoroutine(action);
        EndAct();
        attackTimer = Mathf.Max(attackTimer, 1.5f);
    }

    private IEnumerator Flash()
    {
        FlashTint(new Color(1f, 0.6f, 0.6f));
        yield return new WaitForSeconds(0.1f);
        ClearFlash();
    }

    // The blade cracks: light bursts out, it tumbles to the floor and fades
    private IEnumerator Die()
    {
        dead = true;
        if (action != null) StopCoroutine(action);
        ClearMarks();
        RaiseDied();
        Vector3 at = body.position;
        Vfx.Sprite(Vfx.Pick("glad_impact", "flare"), at, 3f, Color.white * 1.5f, 0.4f, 0.5f, 1.6f, 90f);
        Vfx.Burst(at, HolyBlue, 20, 6f, 0.16f, 0.6f);
        Vfx.Burst(at, new Color(0.85f, 0.88f, 0.95f), 14, 6f, 0.12f, 1.4f);
        foreach (VfxPiece m in motes) if (m != null) Destroy(m.gameObject);
        motes.Clear();
        Quaternion start = body.rotation;
        Vector3 p0 = transform.position;
        for (float t = 0f; t < 0.7f; t += Time.deltaTime)
        {
            float k = t / 0.7f;
            transform.position = new Vector3(p0.x, Mathf.Lerp(p0.y, 0.15f, k * k), p0.z);
            body.rotation = Quaternion.Slerp(start, Quaternion.Euler(80f, start.eulerAngles.y, 30f), k);
            if (glowLight != null) glowLight.intensity = Mathf.Lerp(2f, 0f, k);
            yield return null;
        }
        Vfx.Sprite(Vfx.Pick("glad_impact", "flare"), transform.position, 1.6f, HolyBlue, 0.3f, 0.6f, 1.2f, 0f);
        yield return new WaitForSeconds(0.3f);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        foreach (VfxPiece m in motes) if (m != null) Destroy(m.gameObject);
        if (warning != null) Destroy(warning);
        if (lockMark != null) Destroy(lockMark.gameObject);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}

// A dash's speed streak: a camera-facing strip from A to B that flares and fades on its own
public class GladStreak : MonoBehaviour
{
    private SkiaWatcher.BeamFx beam;
    private float life, t;

    public static void Spawn(Vector3 from, Vector3 to, float width, Color color, float seconds)
    {
        var s = new GameObject("GladStreak").AddComponent<GladStreak>();
        s.beam = new SkiaWatcher.BeamFx(Vfx.Pick("glad_streak", "beam"), from, to, width, color, seconds);
        s.life = seconds;
    }

    private void Update()
    {
        t += Time.deltaTime;
        if (t >= life)
        {
            Destroy(gameObject);
            return;
        }
        beam?.Update(t / life);
    }

    private void OnDestroy() => beam?.Destroy();
}
