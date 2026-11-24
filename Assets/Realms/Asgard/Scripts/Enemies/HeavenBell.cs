using System.Collections;
using UnityEngine;

// 천상의 종: a great golden bell of Asgard's holy metal, runes carved round it, a blue core glowing inside, floating with
// no legs, its clapper hanging beneath. It fights from a distance with rings of sound: every strike is told plainly —
// the runes light one by one, the core flares, the clapper draws back — and every wave can be outrun, dashed through
// (its front is a thin band) or stepped round.
//   천상의 울림   one strike: a ring of force spreads out from it to a set distance
//   삼중 파동     three strikes in a row; the three rings travel at different speeds, so they arrive at different times
//   방향 파동     a strike aimed at the player: a wide arc of force rolls out that way
//   공명 폭발     a long charge, then a great peal: a blast round it and a huge, fast ring
// Models: VARCO bell (Body/Bell) + VARCO clapper (Body/ClapperPivot/Clapper). Effects: Resources/Vfx/bell_*.
public class HeavenBell : Enemy
{
    public static readonly Color BellBlue = new Color(0.45f, 0.82f, 1f);
    public static readonly Color BellGold = new Color(1f, 0.85f, 0.45f);

    [SerializeField] private int maxHealth = 9;
    [SerializeField] private float hoverHeight = 2.1f;
    [SerializeField] private float preferredDistance = 7f;
    [SerializeField] private float driftSpeed = 1.3f;
    [SerializeField] private float damage = 12f;
    [SerializeField] private float bellSize = 1.5f;          // the bell's height, metres before spawn scaling (set by the builder)

    [Header("Waves")]
    [SerializeField] private float ringRadius = 9f;
    [SerializeField] private float ringSpeed = 7f;
    [SerializeField] private float waveBand = 0.9f;
    [SerializeField] private float strikeWindup = 0.9f;
    [SerializeField] private float ringCooldown = 4f;
    [SerializeField] private float tripleCooldown = 9f;
    [SerializeField] private float arcRadius = 12f;
    [SerializeField] private float arcHalfAngle = 50f;
    [SerializeField] private float arcCooldown = 5f;
    [SerializeField] private float resonanceCharge = 2.2f;
    [SerializeField] private float resonanceBlast = 4.5f;
    [SerializeField] private float resonanceCooldown = 14f;

    private Transform player;
    private int health;
    private bool dead, busy;
    private float stunnedUntil, phase, ringTimer, tripleTimer, arcTimer, resonanceTimer, gap;
    private Vector3 knockback;
    private Coroutine action;
    private GameObject warning;

    private Transform body, clapper;
    private float swing, swingVel, sway, swayVel;    // clapper angle / bell rock, sprung
    private float charge;                             // 0..1 runes lit, core flaring
    private GameObject coreGlow;
    private readonly GameObject[] runes = new GameObject[8];
    private Light coreLight;
    private Renderer[] bodyRenderers;
    private MaterialPropertyBlock glowBlock;
    private Color baseEmission = new Color(0.6f, 1.1f, 1.6f);
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;
    private float HealthFraction => (float)Health / Mathf.Max(1, maxHealth);
    private float Size => Mathf.Max(0.1f, transform.lossyScale.x);
    private Vector3 Ground => new Vector3(transform.position.x, 0f, transform.position.z);

    private void Awake()
    {
        health = maxHealth;
        phase = Random.Range(0f, 10f);
        ringTimer = Random.Range(1.5f, 3f);
        tripleTimer = Random.Range(5f, 8f);
        arcTimer = Random.Range(2.5f, 4.5f);
        resonanceTimer = Random.Range(9f, 13f);
        body = transform.Find("Body");
        if (body == null) { body = new GameObject("Body").transform; body.SetParent(transform, false); }
        clapper = body.Find("ClapperPivot");
        bodyRenderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(), r => r is MeshRenderer && !r.name.StartsWith("Vfx"));
        foreach (Renderer r in bodyRenderers)
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(EmissionId)) { baseEmission = r.sharedMaterial.GetColor(EmissionId); break; }
        glowBlock = new MaterialPropertyBlock();
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null) player = movement.transform;
        float h = bellSize;
        // the core inside the bell's mouth, and the ring of runes round its waist that light up in turn
        coreGlow = Vfx.Glow(body, Vector3.up * -h * 0.22f, 0.6f, BellBlue * 0.5f, Vfx.Pick("bell_charge", "glow"), 30f);
        for (int i = 0; i < runes.Length; i++)
        {
            float a = i * Mathf.PI * 2f / runes.Length;
            Vector3 at = new Vector3(Mathf.Cos(a) * h * 0.38f, h * 0.02f, Mathf.Sin(a) * h * 0.38f);
            runes[i] = Vfx.Glow(body, at, 0.28f, BellGold * 0.1f, Vfx.Pick("bell_rune", "flare"), 0f);
        }
        var lightGo = new GameObject("CoreLight");
        lightGo.transform.SetParent(body, false);
        lightGo.transform.localPosition = Vector3.up * -h * 0.3f;
        coreLight = lightGo.AddComponent<Light>();
        coreLight.type = LightType.Point;
        coreLight.color = BellBlue;
        coreLight.range = 4f;
        coreLight.intensity = 1f;
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
        Vector3 pos = transform.position;
        pos.y = Mathf.MoveTowards(pos.y, stunned ? 0.9f : hoverHeight + Mathf.Sin(phase * 1.1f) * 0.12f, 2f * Time.deltaTime);
        knockback = Vector3.Lerp(knockback, Vector3.zero, 5f * Time.deltaTime);
        Vector3 move = knockback;
        if (!stunned && !busy && player != null && GameManager.IsRunActive)
        {
            Vector3 to = Flat(player.position - transform.position);
            float distance = to.magnitude;
            Vector3 dir = distance > 0.01f ? to / distance : transform.forward;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 90f * Time.deltaTime);
            ringTimer -= Time.deltaTime; tripleTimer -= Time.deltaTime; arcTimer -= Time.deltaTime; resonanceTimer -= Time.deltaTime; gap -= Time.deltaTime;
            if (gap <= 0f)
            {
                if (resonanceTimer <= 0f && distance <= 9f)
                    Act(Resonance(), ref resonanceTimer, resonanceCooldown * (HealthFraction < 0.5f ? 0.75f : 1f));
                else if (tripleTimer <= 0f && distance <= ringRadius)
                    Act(Triple(), ref tripleTimer, tripleCooldown);
                else if (ringTimer <= 0f && distance <= ringRadius * 0.85f)
                    Act(Toll(), ref ringTimer, ringCooldown);
                else if (arcTimer <= 0f && distance <= arcRadius)
                    Act(Directed(), ref arcTimer, arcCooldown);
            }
            if (!busy)
            {
                Vector3 side = Vector3.Cross(Vector3.up, dir) * Mathf.Sin(phase * 0.45f);
                move += Vector3.ClampMagnitude((distance - preferredDistance) * dir * 0.5f + side * 1.2f, driftSpeed);
            }
        }
        pos += move * Time.deltaTime;
        transform.position = HomeRoom == null ? pos : HomeRoom.ClampToFloor(pos, 1f);
        Animate();
    }

    private void Act(IEnumerator routine, ref float timer, float cooldown)
    {
        timer = cooldown;
        gap = 1.4f;
        action = StartCoroutine(Run(routine));
    }

    private IEnumerator Run(IEnumerator routine)
    {
        busy = true;
        yield return routine;
        EndAct();
    }

    private void EndAct()
    {
        busy = false;
        action = null;
        charge = 0f;
        if (warning != null) Destroy(warning);
    }

    // ───────────── Striking ─────────────

    // the build-up every strike shares: runes light one after another, the core brightens, the clapper draws back
    private IEnumerator Windup(float seconds, float upTo = 1f)
    {
        EnemyFeel.Windup(transform, transform.position, BellBlue, 1.2f);
        for (float t = 0f; t < seconds && !dead; t += Time.deltaTime)
        {
            charge = Mathf.Lerp(0f, upTo, t / seconds);
            swing = Mathf.Lerp(swing, -28f, 6f * Time.deltaTime);
            yield return null;
        }
    }

    // the clapper hits: the bell rocks, an afterimage of light rings out from it
    private void Strike(float power)
    {
        swingVel = 900f * power;
        swayVel = 160f * power;
        Vfx.Sprite(Vfx.Pick("bell_echo", "glow"), body.position, bellSize * Size * 1.2f, BellBlue * 1.2f * power, 0.45f, 1f, 1.6f, 0f);
        Vfx.Flash(body.position + Vector3.down * bellSize * Size * 0.3f, bellSize * Size * 1.4f, BellBlue, 0.2f);
        EnemyFeel.Shake(transform.position, 0.15f, 0.05f + 0.06f * power);
        charge = 0.2f;
    }

    // 천상의 울림
    private IEnumerator Toll()
    {
        yield return Windup(strikeWindup);
        Strike(1f);
        BellWave.Ring(Ground, ringRadius, ringSpeed, waveBand, damage * OutgoingDamageMultiplier, this);
        yield return new WaitForSeconds(0.6f);
    }

    // 삼중 파동: three rings, each slower than the last, so they reach a spot one after another at uneven gaps
    private IEnumerator Triple()
    {
        yield return Windup(strikeWindup * 0.8f);
        float[] speeds = { 9f, 6.5f, 4.5f };
        for (int i = 0; i < 3 && !dead; i++)
        {
            Strike(0.8f);
            BellWave.Ring(Ground, ringRadius, speeds[i], waveBand * 0.85f, damage * 0.8f * OutgoingDamageMultiplier, this);
            if (i < 2)
                yield return Windup(0.5f, 0.7f);
        }
        yield return new WaitForSeconds(0.6f);
    }

    // 방향 파동: turns on the player and sends a wide arc rolling at them
    private IEnumerator Directed()
    {
        Vector3 dir = player != null ? Flat(player.position - transform.position).normalized : Flat(transform.forward).normalized;
        warning = Telegraph.Line(Ground, dir, arcRadius * 0.85f, 2f * arcRadius * Mathf.Sin(arcHalfAngle * Mathf.Deg2Rad) * 0.45f, strikeWindup * 0.8f);
        for (float t = 0f; t < 0.2f; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 400f * Time.deltaTime);
            yield return null;
        }
        yield return Windup(strikeWindup * 0.7f);
        if (warning != null) Destroy(warning);
        Strike(1f);
        sway = 0f; swayVel = 220f;
        BellWave.Arc(Ground, dir, arcHalfAngle, arcRadius, 10f, waveBand * 1.1f, damage * OutgoingDamageMultiplier, this);
        yield return new WaitForSeconds(0.5f);
    }

    // 공명 폭발: a long charge — the whole bell trembling, light drawn in — then the great peal
    private IEnumerator Resonance()
    {
        warning = Telegraph.Circle(Ground, resonanceBlast, resonanceCharge);
        EnemyFeel.Roar(transform, BellBlue, 3f * Size, 0.15f);
        var gather = Vfx.Gather(body.position, 5f, BellBlue, resonanceCharge, 60f);
        float hum = 0f;
        for (float t = 0f; t < resonanceCharge && !dead; t += Time.deltaTime)
        {
            float k = t / resonanceCharge;
            charge = k;
            swing = Mathf.Lerp(swing, -34f, 4f * Time.deltaTime);
            body.localPosition = Random.insideUnitSphere * 0.03f * k;
            if ((hum -= Time.deltaTime) <= 0f)
            {
                hum = Mathf.Lerp(0.5f, 0.15f, k);
                Vfx.Sprite(Vfx.Pick("bell_wave", "ring"), Ground + Vector3.up * 0.22f, Mathf.Lerp(1.5f, 3f, k) * Size, BellBlue * 0.7f, 0.4f, 1f, 0.3f, 0f, Quaternion.Euler(90f, 0f, 0f));
            }
            yield return null;
        }
        body.localPosition = Vector3.zero;
        if (warning != null) Destroy(warning);
        if (dead) yield break;
        Strike(1.6f);
        charge = 1f;
        Vfx.Sprite(Vfx.Pick("bell_burst", "ring"), Ground + Vector3.up * 0.2f, resonanceBlast * 2.4f, Color.white * 1.3f, 0.7f, 0.3f, 1.1f, 40f, Quaternion.Euler(90f, 0f, 0f));
        Vfx.Burst(body.position, BellBlue, 30, 9f, 0.2f, 0.5f);
        Vfx.Burst(body.position, BellGold, 18, 7f, 0.16f, 0.8f);
        EnemyFeel.Impact(Ground, resonanceBlast, BellBlue, 0.45f);
        PlayerStats stats = player != null ? player.GetComponent<PlayerStats>() : null;
        if (stats != null && FlatDistance(player.position, transform.position) <= resonanceBlast)
        {
            stats.TakeDamage(damage * 1.6f * OutgoingDamageMultiplier, this);
            player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - transform.position).normalized * 10f);
        }
        BellWave.Ring(Ground, 13f, 12f, waveBand * 1.2f, damage * OutgoingDamageMultiplier, this, resonanceBlast);
        yield return new WaitForSeconds(0.9f);
    }

    // ───────────── Its body ─────────────

    private void Animate()
    {
        float dt = Time.deltaTime;
        // the clapper and the bell on springs: struck, they swing and settle
        swingVel += (-swing * 60f - swingVel * 4f) * dt;
        swing += swingVel * dt;
        swayVel += (-sway * 40f - swayVel * 3.5f) * dt;
        sway += swayVel * dt;
        if (clapper != null)
            clapper.localRotation = Quaternion.Euler(Mathf.Clamp(swing, -60f, 60f) + Mathf.Sin(phase * 1.7f) * 3f, 0f, 0f);
        body.localRotation = Quaternion.Euler(Mathf.Clamp(sway, -25f, 25f) * 0.4f + Mathf.Sin(phase * 0.9f) * 2f, 0f, Mathf.Sin(phase * 0.7f) * 2f);

        if (coreGlow != null)
            coreGlow.GetComponent<VfxPiece>()?.Set(Color.Lerp(BellBlue, Color.white, charge * 0.5f) * Mathf.Lerp(0.5f, 2.4f, charge), Mathf.Lerp(0.6f, 1.2f, charge) * bellSize * Size * 0.6f);
        int lit = Mathf.FloorToInt(charge * runes.Length + 0.001f);
        for (int i = 0; i < runes.Length; i++)
            if (runes[i] != null)
                runes[i].GetComponent<VfxPiece>()?.Set(BellGold * (i < lit ? 2.2f : 0.12f) * 1.5f, (i < lit ? 0.34f : 0.26f) * Size);
        if (coreLight != null) coreLight.intensity = Mathf.Lerp(1f, 5f, charge);
        if (bodyRenderers != null)
        {
            glowBlock ??= new MaterialPropertyBlock();
            foreach (Renderer r in bodyRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(glowBlock);
                glowBlock.SetColor(EmissionId, baseEmission * Mathf.Lerp(1f, 5f, charge));
                r.SetPropertyBlock(glowBlock);
            }
        }
    }

    public override void TakeHit(Vector3 from, int amount)
    {
        if (dead)
            return;
        health -= Mathf.Max(1, Mitigate(amount));
        if (!busy) knockback = Flat(transform.position - from).normalized * 2.5f;
        // a struck bell rings: it rocks and the clapper knocks
        swayVel += 90f;
        swingVel += 300f;
        StartCoroutine(Flash());
        Vfx.Burst(body.position, BellGold, 6, 3f, 0.12f, 0.6f);
        if (health <= 0)
            StartCoroutine(Die());
    }

    public override void Knockdown(float seconds)
    {
        if (dead) return;
        Interrupt();
        stunnedUntil = Time.time + seconds;
    }

    public override void Interrupt()
    {
        if (dead || action == null) return;
        StopCoroutine(action);
        EndAct();
        body.localPosition = Vector3.zero;
        gap = 1.5f;
    }

    private IEnumerator Flash()
    {
        FlashTint(new Color(1f, 0.65f, 0.6f));
        yield return new WaitForSeconds(0.1f);
        ClearFlash();
    }

    // its last toll: the core flares and dies, the bell drops with a clang
    private IEnumerator Die()
    {
        dead = true;
        if (action != null) StopCoroutine(action);
        if (warning != null) Destroy(warning);
        RaiseDied();
        Vfx.Sprite(Vfx.Pick("bell_echo", "glow"), body.position, bellSize * Size * 1.4f, BellBlue * 1.5f, 0.6f, 1f, 2f, 0f);
        Vfx.Burst(body.position, BellBlue, 22, 6f, 0.16f, 0.6f);
        Vector3 p0 = transform.position;
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            float k = t / 0.5f;
            transform.position = new Vector3(p0.x, Mathf.Lerp(p0.y, bellSize * Size * 0.45f, k * k), p0.z);
            charge = 1f - k;
            Animate();
            yield return null;
        }
        Vfx.Sprite(Vfx.Pick("bell_wave", "ring"), Ground + Vector3.up * 0.22f, 4f, BellGold, 0.5f, 0.4f, 1.3f, 0f, Quaternion.Euler(90f, 0f, 0f));
        Vfx.Dust(Ground, 1.2f, 10);
        EnemyFeel.Shake(transform.position, 0.2f, 0.12f);
        body.localRotation = Quaternion.Euler(0f, 0f, 70f);
        yield return new WaitForSeconds(0.5f);
        Destroy(gameObject);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}

// A ring (or arc) of sound rolling out across the floor. Only its front hurts — a band about a metre deep — so it can be
// outrun, dashed through or, for an arc, stepped out of. Each lives on its own object.
public class BellWave : MonoBehaviour
{
    private Vector3 center, dir;
    private float maxRadius, speed, band, dmg, halfAngle, r;
    private bool arc, hit;
    private Enemy source;
    private VfxPiece look, look2;
    private PlayerStats stats;

    public static void Ring(Vector3 center, float maxRadius, float speed, float band, float damage, Enemy source, float startRadius = 0.6f)
        => Make(center, Vector3.forward, 181f, maxRadius, speed, band, damage, source, startRadius);

    public static void Arc(Vector3 center, Vector3 dir, float halfAngle, float maxRadius, float speed, float band, float damage, Enemy source)
        => Make(center, dir, halfAngle, maxRadius, speed, band, damage, source, 0.8f);

    private static void Make(Vector3 center, Vector3 dir, float halfAngle, float maxRadius, float speed, float band, float damage, Enemy source, float startRadius)
    {
        var w = new GameObject(halfAngle > 180f ? "BellRing" : "BellArc").AddComponent<BellWave>();
        w.center = new Vector3(center.x, 0f, center.z);
        w.dir = dir.normalized;
        w.halfAngle = halfAngle;
        w.arc = halfAngle <= 180f;
        w.maxRadius = maxRadius;
        w.speed = speed;
        w.band = band;
        w.dmg = damage;
        w.source = source;
        w.r = startRadius;
        w.stats = FindAnyObjectByType<PlayerStats>();
        Quaternion lie = w.arc ? Quaternion.LookRotation(Vector3.down, w.dir) : Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
        string tex = w.arc ? Vfx.Pick("bell_arc", "ring") : Vfx.Pick("bell_wave", "ring");
        w.look = Vfx.Sprite(tex, w.center + Vector3.up * 0.25f, 1f, HeavenBell.BellBlue * 1.4f, 0f, 1f, 1f, 0f, lie);
        w.look.transform.SetParent(w.transform, true);
        if (!w.arc)
        {
            w.look2 = Vfx.Sprite(tex, w.center + Vector3.up * 0.27f, 1f, HeavenBell.BellGold * 0.6f, 0f, 1f, 1f, 0f, lie * Quaternion.Euler(0f, 0f, 45f));
            w.look2.transform.SetParent(w.transform, true);
        }
    }

    private void Update()
    {
        r += speed * Time.deltaTime;
        float k = r / maxRadius;
        if (k >= 1f || (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.State.Dead))
        {
            Destroy(gameObject);
            return;
        }
        float fade = k < 0.75f ? 1f : 1f - (k - 0.75f) / 0.25f;
        // the painted ring sits at about 92% of its sheet's half-width, so the sheet is drawn a little wider than 2r;
        // an arc sheet's band lies across its upper part, so it is drawn round the centre the same way
        float size = r * 2f / 0.92f;
        if (arc && look != null)
        {
            // the arc sheet: its band crests about 0.2 of the sheet above the middle, curving down to the sides —
            // drawn 1.7r wide and pushed ahead so the crest rides at distance r
            size = r * 1.7f;
            look.transform.position = center + dir * (r - 0.2f * size) + Vector3.up * 0.25f;
        }
        look?.Set(HeavenBell.BellBlue * 1.4f, size, fade);
        look2?.Set(HeavenBell.BellGold * 0.6f, size * 0.97f, fade * 0.8f);
        if (!hit && stats != null)
        {
            Vector3 p = stats.transform.position;
            Vector3 to = new Vector3(p.x - center.x, 0f, p.z - center.z);
            float d = to.magnitude;
            bool inBand = d <= r + band * 0.35f && d >= r - band;
            bool inArc = !arc || Vector3.Angle(dir, to) <= halfAngle;
            if (inBand && inArc)
            {
                hit = true;
                stats.TakeDamage(dmg, source);
                stats.GetComponent<PlayerMovement>()?.Push((d > 0.01f ? to / d : dir) * 6f);
                Vfx.Sprite(Vfx.Pick("bell_wave", "ring"), p + Vector3.up * 0.9f, 1.4f, Color.white, 0.25f, 0.5f, 1.2f, 0f);
            }
        }
    }
}
