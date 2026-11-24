using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 룬 회전체 솔라리스: a celestial machine with no body — a blue energy core with golden rune discs circling it. The discs
// turn slowly at rest and whirl up when it attacks, blue light building on them and their paths left glowing behind.
// Badly hurt, their orbit wobbles off its axis.
//   회전 충돌       closes on the player and spins its discs up to a blur round itself
//   룬 원판 발사    flings one disc straight at the player; it flies out, then comes back to the core (hurting both ways)
//   연속 원판 발사  two or three discs in turn — each one thrown leaves fewer round the core (the moment to close in)
//   확장 회전       the discs swing out to a wide orbit, sweep round it, then draw back in
// Models: VARCO core (Body/Core) + VARCO disc (Resources/Mobs/SolarisDisc, flat, 1 m across). Effects: Resources/Vfx/sol_*.
public class SolarisRotor : Enemy
{
    public static readonly Color SolBlue = new Color(0.45f, 0.8f, 1f);
    public static readonly Color SolGold = new Color(1f, 0.84f, 0.42f);

    [SerializeField] private int maxHealth = 9;
    [SerializeField] private float hoverHeight = 1.4f;
    [SerializeField] private float preferredDistance = 5.5f;
    [SerializeField] private float moveSpeed = 2.4f;
    [SerializeField] private float damage = 10f;
    [SerializeField] private int discCount = 4;
    [SerializeField] private float discSize = 0.55f;          // disc diameter, metres before spawn scaling
    [SerializeField] private float orbitRadius = 1.1f;

    [Header("Timing")]
    [SerializeField] private float ramCooldown = 5f;
    [SerializeField] private float launchCooldown = 3.5f;
    [SerializeField] private float volleyCooldown = 8f;
    [SerializeField] private float expandCooldown = 9f;
    [SerializeField] private float throwRange = 12f;
    [SerializeField] private float throwSpeed = 15f;
    [SerializeField] private float expandRadius = 4.2f;

    private enum DiscState { Orbit, Out, Back }
    private class Disc
    {
        public Transform t;
        public DiscState state;
        public Vector3 dir, from;
        public float travelled, spin, hitCooldown, glow;
        public TrailRenderer trail;
        public VfxPiece aura;
    }

    private readonly List<Disc> discs = new List<Disc>();
    private Transform player;
    private PlayerStats playerStats;
    private int health;
    private bool dead, busy;
    private float stunnedUntil, phase, orbitAngle, orbitSpeed = 50f, orbitGoal = 50f, radius, radiusGoal, charge;
    private float ramTimer, launchTimer, volleyTimer, expandTimer, gap;
    private Vector3 knockback, moveExtra;
    private Coroutine action;
    private Transform body, core;
    private GameObject coreGlow;
    private Light coreLight;

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;
    private float HealthFraction => (float)Health / Mathf.Max(1, maxHealth);
    private float Size => Mathf.Max(0.1f, transform.lossyScale.x);
    private bool Hot => orbitSpeed > 260f;                 // whirling fast enough that touching a disc hurts

    private void Awake()
    {
        health = maxHealth;
        phase = Random.Range(0f, 10f);
        ramTimer = Random.Range(2f, 4f);
        launchTimer = Random.Range(1.2f, 2.5f);
        volleyTimer = Random.Range(5f, 8f);
        expandTimer = Random.Range(6f, 9f);
        body = transform.Find("Body");
        if (body == null) { body = new GameObject("Body").transform; body.SetParent(transform, false); }
        core = body.Find("Core");
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null) { player = movement.transform; playerStats = movement.GetComponent<PlayerStats>(); }
        radius = radiusGoal = orbitRadius * Size;
        coreGlow = Vfx.Glow(body, Vector3.zero, 0.6f, SolBlue * 0.7f, Vfx.Pick("sol_core", "glow"), 90f);
        var lightGo = new GameObject("CoreLight");
        lightGo.transform.SetParent(body, false);
        coreLight = lightGo.AddComponent<Light>();
        coreLight.type = LightType.Point;
        coreLight.color = SolBlue;
        coreLight.range = 4f;
        coreLight.intensity = 1.2f;
        var prefab = Resources.Load<GameObject>("Mobs/SolarisDisc");
        for (int i = 0; i < discCount; i++)
        {
            var d = new Disc();
            d.t = prefab != null ? Instantiate(prefab).transform : new GameObject("Disc").transform;
            d.t.name = "SolarisDisc";
            d.t.localScale = Vector3.one * discSize * Size;
            d.trail = Vfx.Trail(d.t.gameObject, SolBlue, 0.35f * Size, 0.25f, Vfx.Pick("sol_trail", "trail"));
            // (kept unparented — the disc is scaled — and moved with it each frame)
            d.aura = Vfx.Sprite(Vfx.Pick("sol_glow", "ring"), d.t.position, discSize * Size * 1.6f, SolBlue * 0.3f, 0f, 1f, 1f, -360f, Quaternion.Euler(90f, 0f, 0f));
            discs.Add(d);
        }
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
        pos.y = Mathf.MoveTowards(pos.y, stunned ? 0.8f : hoverHeight + Mathf.Sin(phase * 1.6f) * 0.12f, 2.5f * Time.deltaTime);
        knockback = Vector3.Lerp(knockback, Vector3.zero, 6f * Time.deltaTime);
        Vector3 move = knockback + moveExtra;
        if (!stunned && !busy && player != null && GameManager.IsRunActive)
        {
            Vector3 to = Flat(player.position - transform.position);
            float distance = to.magnitude;
            Vector3 dir = distance > 0.01f ? to / distance : transform.forward;
            // circles the player at a middling distance, always pressing
            Vector3 side = Vector3.Cross(Vector3.up, dir) * (Mathf.Sin(phase * 0.35f) > 0f ? 1f : -1f);
            move += Vector3.ClampMagnitude((distance - preferredDistance) * dir * 0.8f + side * 1.6f, moveSpeed);
            ramTimer -= Time.deltaTime; launchTimer -= Time.deltaTime; volleyTimer -= Time.deltaTime; expandTimer -= Time.deltaTime; gap -= Time.deltaTime;
            int inOrbit = InOrbit();
            if (gap <= 0f)
            {
                if (expandTimer <= 0f && distance <= expandRadius + 1.5f && inOrbit >= 2)
                    Act(Expand(), ref expandTimer, expandCooldown);
                else if (ramTimer <= 0f && distance <= 7f && inOrbit >= 1)
                    Act(Ram(), ref ramTimer, ramCooldown);
                else if (volleyTimer <= 0f && distance <= throwRange && inOrbit >= 3)
                    Act(Volley(), ref volleyTimer, volleyCooldown);
                else if (launchTimer <= 0f && distance <= throwRange && inOrbit >= 2)
                    Act(Launch(), ref launchTimer, launchCooldown);
            }
        }
        pos += move * Time.deltaTime;
        transform.position = HomeRoom == null ? pos : HomeRoom.ClampToFloor(pos, 1f);
        UpdateDiscs();
        UpdateCore();
    }

    private void Act(IEnumerator routine, ref float timer, float cooldown)
    {
        timer = cooldown;
        gap = 1.2f;
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
        orbitGoal = 50f;
        radiusGoal = orbitRadius * Size;
        charge = 0f;
        moveExtra = Vector3.zero;
    }

    private int InOrbit()
    {
        int n = 0;
        foreach (Disc d in discs) if (d.state == DiscState.Orbit) n++;
        return n;
    }

    // ───────────── The discs ─────────────

    private void UpdateDiscs()
    {
        float dt = Time.deltaTime;
        orbitSpeed = Mathf.MoveTowards(orbitSpeed, orbitGoal, 900f * dt);
        orbitAngle += orbitSpeed * dt;
        radius = Mathf.MoveTowards(radius, radiusGoal, 9f * dt);
        Vector3 center = body.position;
        // badly hurt: the orbit plane wobbles off its axis
        float wob = HealthFraction < 0.4f ? (0.4f - HealthFraction) / 0.4f : 0f;
        Quaternion tilt = Quaternion.Euler(Mathf.Sin(phase * 2.7f) * 35f * wob, 0f, Mathf.Cos(phase * 2.1f) * 35f * wob);
        int orbiting = InOrbit(), k = 0;
        foreach (Disc d in discs)
        {
            if (d.t == null) continue;
            d.hitCooldown -= dt;
            bool hot;
            if (d.state == DiscState.Orbit)
            {
                float a = (orbitAngle + k * 360f / Mathf.Max(1, orbiting)) * Mathf.Deg2Rad;
                k++;
                Vector3 want = center + tilt * new Vector3(Mathf.Cos(a), Mathf.Sin(a * 2f + phase) * 0.06f, Mathf.Sin(a)) * radius;
                d.t.position = Vector3.Lerp(d.t.position, want, 1f - Mathf.Exp(-14f * dt));
                hot = Hot;
            }
            else
            {
                if (d.state == DiscState.Out)
                {
                    d.t.position += d.dir * throwSpeed * dt;
                    d.travelled += throwSpeed * dt;
                    if (HomeRoom != null)
                    {
                        Vector3 c = HomeRoom.ClampToFloor(d.t.position, 0.4f);
                        if (Flat(c - d.t.position).sqrMagnitude > 0.01f) d.travelled = throwRange;   // struck the wall
                    }
                    if (d.travelled >= throwRange) { d.state = DiscState.Back; d.hitCooldown = 0f; Vfx.Sprite(Vfx.Pick("sol_hit", "flare"), d.t.position, 1.2f * Size, SolGold, 0.2f, 0.6f, 1.1f, 0f); }
                }
                else
                {
                    // homes back to the core, a little faster as it closes
                    Vector3 to = center - d.t.position;
                    float step = throwSpeed * 0.85f * dt;
                    if (to.magnitude <= step + 0.3f) { d.state = DiscState.Orbit; d.glow = 0.6f; }
                    else d.t.position += to.normalized * step;
                }
                hot = true;
            }
            d.spin += (hot ? 1400f : 160f) * dt;
            d.t.rotation = tilt * Quaternion.Euler(0f, d.spin, 0f);
            d.glow = Mathf.MoveTowards(d.glow, Mathf.Max(charge, hot ? 1f : 0.2f), 3f * dt);
            if (d.aura != null) d.aura.transform.position = d.t.position;
            if (d.aura != null) d.aura.Set(Color.Lerp(SolBlue, Color.white, d.glow * 0.3f) * Mathf.Lerp(0.3f, 1.8f, d.glow), discSize * Size * Mathf.Lerp(1.4f, 1.9f, d.glow), Mathf.Lerp(0.4f, 1f, d.glow));
            if (d.trail != null) { d.trail.widthMultiplier = Mathf.Lerp(0.6f, 2f, d.glow); d.trail.time = Mathf.Lerp(0.15f, 0.4f, d.glow); }
            // a whirling or flying disc cuts whatever it touches
            if (hot && d.hitCooldown <= 0f && playerStats != null && player != null
                && FlatDistance(player.position, d.t.position) <= discSize * Size * 0.6f + 0.45f && Mathf.Abs(d.t.position.y - (player.position.y + 0.9f)) < 1.6f)
            {
                d.hitCooldown = 0.6f;
                playerStats.TakeDamage(damage * (d.state == DiscState.Orbit ? 0.7f : 1f) * OutgoingDamageMultiplier, this);
                player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - d.t.position).normalized * 5f);
                Vfx.Sprite(Vfx.Pick("sol_hit", "flare"), player.position + Vector3.up * 0.9f, 1.4f, Color.white * 1.2f, 0.22f, 0.6f, 1.2f, 90f);
            }
        }
    }

    private void UpdateCore()
    {
        if (core != null) core.localRotation = Quaternion.Euler(phase * 30f, phase * 70f, 0f);
        if (coreGlow != null) coreGlow.GetComponent<VfxPiece>()?.Set(SolBlue * Mathf.Lerp(0.7f, 2f, charge), Mathf.Lerp(0.6f, 1f, charge) * Size * 0.6f);
        if (coreLight != null) coreLight.intensity = Mathf.Lerp(1.2f, 4f, charge);
    }

    private Disc NextOrbiting()
    {
        // the disc in orbit nearest the player goes first
        Disc best = null; float bd = float.MaxValue;
        foreach (Disc d in discs)
        {
            if (d.state != DiscState.Orbit || d.t == null) continue;
            float dist = player != null ? (d.t.position - player.position).sqrMagnitude : 0f;
            if (dist < bd) { bd = dist; best = d; }
        }
        return best;
    }

    // ───────────── 회전 충돌 ─────────────

    private IEnumerator Ram()
    {
        // spins up with blue light gathering on the discs, then drives in, whirling
        Vfx.Sprite(Vfx.Pick("sol_core", "glow"), body.position, 1.8f * Size, SolBlue * 1.5f, 0.5f, 0.6f, 1.3f, 300f);
        for (float t = 0f; t < 0.5f && !dead; t += Time.deltaTime) { charge = t / 0.5f; orbitGoal = 220f; yield return null; }
        orbitGoal = 720f;
        EnemyFeel.Lunge(transform.position, transform.forward, SolGold, 1f);
        for (float t = 0f; t < 1.8f && !dead && player != null; t += Time.deltaTime)
        {
            Vector3 to = Flat(player.position - transform.position);
            moveExtra = to.magnitude > 1.2f ? to.normalized * Mathf.Min(7f, to.magnitude * 4f) : Vector3.zero;
            if (t > 1.2f) moveExtra *= 0.3f;
            yield return null;
        }
        moveExtra = Vector3.zero;
        charge = 0f;
        yield return new WaitForSeconds(0.4f);
    }

    // ───────────── 룬 원판 발사 ─────────────

    private IEnumerator Launch() { EnemyVoice.Of(this)?.Skill(); yield return ThrowOne(0.45f); yield return new WaitForSeconds(0.4f); }

    private IEnumerator ThrowOne(float windup)
    {
        Disc d = NextOrbiting();
        if (d == null || player == null) yield break;
        orbitGoal = 260f;
        for (float t = 0f; t < windup && !dead; t += Time.deltaTime) { d.glow = 1f; charge = Mathf.Max(charge, t / windup * 0.8f); yield return null; }
        if (dead || d.state != DiscState.Orbit) yield break;
        d.dir = Flat(player.position - d.t.position).normalized;
        d.from = d.t.position;
        d.travelled = 0f;
        d.state = DiscState.Out;
        d.hitCooldown = 0f;
        d.t.position = new Vector3(d.t.position.x, Mathf.Max(1f, player.position.y + 0.9f), d.t.position.z);
        Vfx.Sprite(Vfx.Pick("sol_hit", "flare"), d.t.position, 1.4f * Size, SolBlue * 1.6f, 0.2f, 0.6f, 1.2f, 0f);
        EnemyFeel.Launch(transform, d.t.position, SolGold);
    }

    // ───────────── 연속 원판 발사 ─────────────

    private IEnumerator Volley()
    {
        int count = Mathf.Min(InOrbit() - 1, HealthFraction < 0.5f ? 3 : 2 + (Random.value < 0.5f ? 1 : 0));
        for (int i = 0; i < count && !dead; i++)
            yield return ThrowOne(i == 0 ? 0.5f : 0.3f);
        yield return new WaitForSeconds(0.6f);
    }

    // ───────────── 확장 회전 ─────────────

    private IEnumerator Expand()
    {
        Vector3 center = Flat(transform.position);
        float r = expandRadius;
        VfxPiece ring = Vfx.Decal(center + Vector3.up * 0.2f, r * 2.2f, SolGold * 0.5f, Vfx.Pick("sol_ring", "ring"), 120f);
        for (float t = 0f; t < 0.7f && !dead; t += Time.deltaTime)
        {
            float k = t / 0.7f;
            charge = k;
            orbitGoal = Mathf.Lerp(80f, 300f, k);
            ring.transform.position = Flat(transform.position) + Vector3.up * 0.2f;
            ring.Set(Color.Lerp(SolGold, SolBlue, k) * (0.5f + k), r * 2.2f, 0.4f + 0.6f * k);
            yield return null;
        }
        Destroy(ring.gameObject);
        radiusGoal = r;
        orbitGoal = 520f;
        EnemyFeel.Roar(transform, SolBlue, r, 0.1f);
        yield return new WaitForSeconds(1.8f);
        radiusGoal = orbitRadius * Size;
        orbitGoal = 120f;
        charge = 0f;
        yield return new WaitForSeconds(0.6f);
    }

    // ───────────── Being hit ─────────────

    public override void TakeHit(Vector3 from, int amount)
    {
        if (dead) return;
        health -= Mathf.Max(1, Mitigate(amount));
        if (!busy) knockback = Flat(transform.position - from).normalized * 3f;
        StartCoroutine(Flash());
        Vfx.Sprite(Vfx.Pick("sol_hit", "flare"), body.position, 1f * Size, SolGold, 0.2f, 0.6f, 1.1f, 45f);
        if (health <= 0) StartCoroutine(Die());
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
        gap = 1.5f;
    }

    private IEnumerator Flash()
    {
        FlashTint(new Color(1f, 0.65f, 0.6f));
        yield return new WaitForSeconds(0.1f);
        ClearFlash();
    }

    // the core flickers out; the discs, no longer held, scatter and drop
    private IEnumerator Die()
    {
        dead = true;
        if (action != null) StopCoroutine(action);
        RaiseDied();
        Vector3 at = body.position;
        Vfx.Sprite(Vfx.Pick("sol_core", "glow"), at, 2.6f * Size, Color.white * 1.5f, 0.4f, 0.6f, 1.6f, 200f);
        Vfx.Burst(at, SolBlue, 20, 6f, 0.16f, 0.6f);
        var vel = new List<Vector3>();
        foreach (Disc d in discs) vel.Add(d.t != null ? (Flat(d.t.position - at).normalized * 4f + Vector3.up * 3f) : Vector3.zero);
        if (core != null) core.gameObject.SetActive(false);
        if (coreGlow != null) Destroy(coreGlow);
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            for (int i = 0; i < discs.Count; i++)
            {
                if (discs[i].t == null) continue;
                vel[i] += Vector3.down * 14f * Time.deltaTime;
                discs[i].t.position += vel[i] * Time.deltaTime;
                if (discs[i].t.position.y < 0.05f) { var p = discs[i].t.position; discs[i].t.position = new Vector3(p.x, 0.05f, p.z); vel[i] *= 0.3f; }
                discs[i].t.Rotate(200f * Time.deltaTime, 600f * Time.deltaTime, 0f, Space.Self);
            }
            if (coreLight != null) coreLight.intensity = Mathf.Lerp(3f, 0f, t / 0.8f);
            yield return null;
        }
        foreach (Disc d in discs)
        {
            if (d.t != null) { Vfx.Burst(d.t.position, SolGold, 5, 3f, 0.1f, 0.6f); Destroy(d.t.gameObject); }
            if (d.aura != null) Destroy(d.aura.gameObject);
        }
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        foreach (Disc d in discs)
        {
            if (d.t != null) Destroy(d.t.gameObject);
            if (d.aura != null) Destroy(d.aura.gameObject);
        }
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
