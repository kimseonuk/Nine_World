using System.Collections;
using UnityEngine;

// 룬 정령: a shapeless knot of blue magic round a glowing rune core, small rune shards circling it like moons.
// It floats out of reach, keeps its distance, blinks away from blows, and — once its core cracks below
// 30% health — sheds three motes that hang in the air and then hunt the player.
//   마력탄       one bolt of magic
//   삼중 발사    three bolts at once, fanned out
//   룬 폭발      the air round it marked, then a burst (when the player gets close)
//   순간이동     a short blink away
//   룬 분열      (special, once) three motes split from the core and fly at the player
public class RuneWisp : Enemy
{
    [SerializeField] private int maxHealth = 5;
    [SerializeField] private float hoverHeight = 1.9f;
    [SerializeField] private float preferredDistance = 7f;
    [SerializeField] private float driftSpeed = 1.8f;
    [SerializeField] private float boltDamage = 7f;

    [Header("Attacks")]
    [SerializeField] private float attackInterval = 2.2f;
    [SerializeField] private float tripleChance = 0.4f;
    [SerializeField] private float burstRange = 3.5f;
    [SerializeField] private float burstRadius = 2.8f;
    [SerializeField] private float burstDelay = 1f;
    [SerializeField] private float burstCooldown = 5f;

    [Header("Blink")]
    [SerializeField] private float blinkDistance = 5f;
    [SerializeField] private float blinkCooldown = 3f;

    [Header("Rune split (below this much health, once)")]
    [SerializeField, Range(0f, 1f)] private float splitAt = 0.3f;
    [SerializeField] private int splitMotes = 3;
    [SerializeField] private float splitHang = 1.3f;

    [Header("Getting hit")]
    [SerializeField] private Color hitTint = new Color(1f, 0.6f, 0.6f);

    private Transform player;
    private int health;
    private bool dead, busy, split;
    private float attackTimer, burstTimer, blinkTimer, stunnedUntil, phase;
    private Vector3 knockback;

    private Transform body;           // everything visible, bobbing and wobbling
    private Transform core;
    private Transform shards;         // the ring of rune shards
    private GameObject coreGlow, coreRune, cloud;
    private Light glowLight;

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;
    private float HealthFraction => (float)Health / Mathf.Max(1, maxHealth);

    private void Awake()
    {
        health = maxHealth;
        phase = Random.Range(0f, 10f);
        attackTimer = Random.Range(1f, attackInterval);
        burstTimer = Random.Range(1f, burstCooldown);
        Build();
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null)
            player = movement.transform;
    }

    // The wisp is made of light: a soft blue cloud, the rune-carved core at its heart and orbiting shards
    private void Build()
    {
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        cloud = Vfx.Glow(body, Vector3.zero, 2.2f, MobFx.RuneBlue * 0.55f, "auraflame", 40f);
        Vfx.Glow(body, Vector3.zero, 1.5f, MobFx.RuneBlue * 0.7f);
        core = new GameObject("Core").transform;
        core.SetParent(body, false);
        coreGlow = Vfx.Glow(core, Vector3.zero, 0.75f, Color.Lerp(MobFx.RuneBlue, Color.white, 0.6f), "flare", 90f);
        coreRune = Vfx.Glow(core, Vector3.zero, 0.9f, MobFx.RuneBlue * 1.3f, "runes", -60f);
        shards = new GameObject("Shards").transform;
        shards.SetParent(body, false);
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f;
            // Little rune-cut crystal splinters: a diamond of blue glass and a glow round it
            var shard = Mats.Shape(PrimitiveType.Cube, shards, Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.8f, new Vector3(0.07f, 0.07f, 0.07f),
                Mats.Get("wisp_shard", new Color(0.25f, 0.5f, 0.95f), 0.9f, new Color(0.15f, 0.4f, 1.1f)));
            shard.transform.localRotation = Quaternion.Euler(45f, a, 45f);
            shard.transform.localScale = new Vector3(0.06f, 0.16f, 0.06f);
            Vfx.Glow(shard.transform, Vector3.zero, 4f, MobFx.RuneBlue * 0.6f);
        }
        // Its trail of blue fire as it moves
        var tail = new GameObject("Tail");
        tail.transform.SetParent(body, false);
        Vfx.Trail(tail, MobFx.RuneBlue, 0.6f, 0.35f, "flame");
        Vfx.Sparkle(tail, MobFx.RuneBlue, 3f, 0.15f);
        var lightGo = new GameObject("WispLight");
        lightGo.transform.SetParent(body, false);
        glowLight = lightGo.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = MobFx.RuneBlue;
        glowLight.range = 4f;
        glowLight.intensity = 1.5f;
    }

    public override void Scale(float healthMultiplier, float damageMultiplier)
    {
        maxHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * healthMultiplier));
        health = maxHealth;
        boltDamage *= damageMultiplier;
    }

    private void Update()
    {
        if (dead)
            return;
        phase += Time.deltaTime;
        bool stunned = Time.time < stunnedUntil;

        Vector3 pos = transform.position;
        float targetHeight = stunned ? 0.5f : hoverHeight + Mathf.Sin(phase * 1.7f) * 0.25f;
        pos.y = Mathf.MoveTowards(pos.y, targetHeight, (stunned ? 6f : 2f) * Time.deltaTime);

        knockback = Vector3.Lerp(knockback, Vector3.zero, 6f * Time.deltaTime);
        Vector3 move = knockback;
        if (!stunned && !busy && player != null && GameManager.IsRunActive)
        {
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            float distance = to.magnitude;
            attackTimer -= Time.deltaTime;
            burstTimer -= Time.deltaTime;
            blinkTimer -= Time.deltaTime;

            if (!split && HealthFraction <= splitAt)
            {
                split = true;
                StartCoroutine(Act(RuneSplit()));
            }
            else if (distance <= burstRange && burstTimer <= 0f)
            {
                burstTimer = burstCooldown;
                StartCoroutine(Act(RuneBurst()));
            }
            else if (distance < burstRange * 0.8f && blinkTimer <= 0f)
                Blink(-to.normalized);
            else
            {
                Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * Mathf.Sin(phase * 0.6f);
                Vector3 want = (distance - preferredDistance) * to.normalized * 0.6f + side * 1.6f;
                move += Vector3.ClampMagnitude(want, driftSpeed);
                if (attackTimer <= 0f && distance < preferredDistance + 5f)
                {
                    attackTimer = attackInterval;
                    StartCoroutine(Act(Random.value < tripleChance ? Bolts(3) : Bolts(1)));
                }
            }
        }
        pos += move * Time.deltaTime;
        transform.position = KeepInZone(pos);
        Animate(stunned);
    }

    private IEnumerator Act(IEnumerator action)
    {
        busy = true;
        yield return action;
        busy = false;
    }

    // The core flares as it gathers the shot, then the bolts fly
    private IEnumerator Bolts(int count)
    {
        if (core != null)
            Vfx.Sprite("foe_glint", core.position, 1.2f, MobFx.RuneBlue * 1.6f, 0.5f, 0.8f, 1.1f, 25f).Sheet(4, 4);
        for (float t = 0f; t < 0.5f && !dead; t += Time.deltaTime)
        {
            if (coreGlow != null)
                coreGlow.transform.localScale = Vector3.one * 0.75f * (1f + t * 1.6f);
            yield return null;
        }
        if (dead || player == null)
            yield break;
        Vector3 from = core.position;
        Vector3 aim = player.position + Vector3.up * 0.3f - from;
        for (int i = 0; i < count; i++)
        {
            float spread = count > 1 ? (i - (count - 1) / 2f) * 22f : 0f;
            Projectile.ManaBolt(from, Quaternion.Euler(0f, spread, 0f) * aim, this, boltDamage);
        }
        Vfx.Flash(from, 1.2f, MobFx.RuneBlue, 0.15f);
        EnemyFeel.Launch(transform, from, MobFx.RuneBlue);
        yield return new WaitForSeconds(0.3f);
    }

    // 룬 폭발: the air round it is marked, then it bursts
    private IEnumerator RuneBurst()
    {
        EnemyVoice.Of(this)?.Skill();   // (its own big-move sound)
        Vector3 center = new Vector3(transform.position.x, 0f, transform.position.z);
        MobFx.DelayedBlast(center, burstRadius, burstDelay, boltDamage * 1.4f, this, MobFx.RuneBlue);
        for (float t = 0f; t < burstDelay && !dead; t += Time.deltaTime)
        {
            body.localScale = Vector3.one * (1f + 0.25f * t / burstDelay + Mathf.Sin(t * 40f) * 0.04f);
            yield return null;
        }
        body.localScale = Vector3.one;
        // Hangs in the blast for a breath, then slips away
        yield return new WaitForSeconds(0.2f);
        if (player != null && blinkTimer <= 0f)
            Blink(Flat(transform.position - player.position).normalized);
    }

    // 순간이동: gone in a flash, back a few metres off to the side
    private void Blink(Vector3 away)
    {
        blinkTimer = blinkCooldown;
        Vector3 sideways = Vector3.Cross(Vector3.up, away) * Random.Range(-1f, 1f);
        Vector3 to = transform.position + (away + sideways).normalized * blinkDistance;
        to = KeepInZone(to);
        Vfx.Flash(transform.position, 1.8f, MobFx.RuneBlue, 0.2f);
        Vfx.Burst(transform.position, MobFx.RuneBlue, 10, 4f, 0.14f, 0f);
        transform.position = to;
        Vfx.Flash(to, 1.8f, MobFx.RuneBlue, 0.2f);
        // A blink also clears its body's trail, so it doesn't streak across the room
        foreach (var trail in GetComponentsInChildren<TrailRenderer>())
            trail.Clear();
    }

    // 룬 분열: three motes break from the cracked core, hang about it, then go for the player
    private IEnumerator RuneSplit()
    {
        Vfx.Burst(core.position, MobFx.RuneBlue, 16, 5f, 0.16f, 0f);
        // the core cracking open rings out like a cry
        EnemyFeel.Roar(transform, MobFx.RuneBlue, 3f, 0.16f);
        var motes = new GameObject[splitMotes];
        for (int i = 0; i < splitMotes; i++)
        {
            motes[i] = new GameObject("RuneMote");
            motes[i].transform.position = core.position;
            Vfx.Glow(motes[i].transform, Vector3.zero, 0.6f, MobFx.RuneBlue * 1.4f);
            Vfx.Glow(motes[i].transform, Vector3.zero, 0.5f, Color.white * 0.8f, "runes", 200f);
        }
        for (float t = 0f; t < splitHang && !dead; t += Time.deltaTime)
        {
            for (int i = 0; i < splitMotes; i++)
            {
                float a = (i * 360f / splitMotes + t * 200f) * Mathf.Deg2Rad;
                float r = Mathf.Lerp(0.2f, 1.4f, Mathf.Clamp01(t / 0.4f));
                motes[i].transform.position = core.position + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(t * 5f + i) * 0.2f, Mathf.Sin(a) * r);
            }
            yield return null;
        }
        for (int i = 0; i < splitMotes; i++)
        {
            Vector3 from = motes[i].transform.position;
            Destroy(motes[i]);
            if (player != null)
                Projectile.ManaBolt(from, player.position + Vector3.up * 0.3f - from, this, boltDamage * 0.8f, 9f, 90f, 0.7f);
        }
    }

    private Vector3 KeepInZone(Vector3 pos)
    {
        // Over the floor (not out over a shaped hall's open cosmos, where no blade could reach it)
        return HomeRoom == null ? pos : HomeRoom.ClampToFloor(pos, 1f);
    }

    // Wobbling, the shards wheeling round; its light grows unsteady and the core cracks as it weakens
    private void Animate(bool stunned)
    {
        float hurt = 1f - HealthFraction;
        shards.localRotation = Quaternion.Euler(Mathf.Sin(phase * 0.9f) * 20f, phase * (stunned ? 40f : 120f), 0f);
        core.localRotation = Quaternion.Euler(0f, phase * 60f, 0f);
        body.localPosition = new Vector3(0f, Mathf.Sin(phase * 3.1f) * 0.06f, 0f);
        float flicker = hurt > 0.7f ? Mathf.PerlinNoise(phase * 12f, 0f) : 1f;
        if (glowLight != null)
            glowLight.intensity = 1.5f * Mathf.Lerp(1f, 0.3f + flicker, hurt);
        if (coreRune != null)
            coreRune.transform.localScale = Vector3.one * 0.9f * (hurt > 0.7f ? 0.7f + 0.5f * flicker : 1f);
        if (coreGlow != null && !busy)
            coreGlow.transform.localScale = Vector3.one * 0.75f;
    }

    public override void TakeHit(Vector3 from, int damage)
    {
        if (dead)
            return;
        health -= Mathf.Max(1, damage);
        Vector3 away = transform.position - from;
        away.y = 0f;
        knockback = away.normalized * 4f;
        StartCoroutine(Flash());
        Vfx.Burst(core.position, MobFx.RuneBlue, 6, 3f, 0.12f, 0f);
        if (health <= 0)
        {
            StartCoroutine(Die());
            return;
        }
        // 순간이동: it slips away from a blow when it can
        if (!busy && blinkTimer <= 0f && Random.value < 0.6f)
            Blink(away.normalized);
    }

    public override void Knockdown(float seconds)
    {
        if (!dead)
            stunnedUntil = Time.time + seconds;
    }

    public override void Interrupt()
    {
        attackTimer = Mathf.Max(attackTimer, 1.5f);
    }

    private IEnumerator Flash()
    {
        if (cloud != null)
            cloud.transform.localScale = Vector3.one * 2.6f;
        yield return new WaitForSeconds(0.1f);
        if (cloud != null)
            cloud.transform.localScale = Vector3.one * 2.2f;
    }

    // The core shatters in a burst of blue light and the wisp unravels
    private IEnumerator Die()
    {
        dead = true;
        StopCoroutine(nameof(Act));
        RaiseDied();
        Vector3 at = core.position;
        Vfx.Flash(at, 2.5f, MobFx.RuneBlue, 0.3f);
        Vfx.Burst(at, MobFx.RuneBlue, 24, 6f, 0.18f, 0.3f);
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            body.localScale = Vector3.one * Mathf.Lerp(1f, 0.1f, t / 0.4f);
            if (glowLight != null)
                glowLight.intensity = Mathf.Lerp(1.5f, 0f, t / 0.4f);
            yield return null;
        }
        Destroy(gameObject);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
