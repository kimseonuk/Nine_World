using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 분열하는 빛 루멘: a celestial creature of translucent crystal and light round a small blue core, shards of light
// drifting about it, a faint trail behind as it floats. Hurt enough, it tears in two — and each half again — so the
// harder it is pressed the more of it there is. Each split is smaller, paler and quicker; the last are little blobs
// of light that dart at the player and burst when they are put out.
//   빛의 탄환     small orbs of energy shot at the player (body and first splits)
//   분열          at half health it splits into two smaller, weaker copies (twice: 1 → 2 → 4)
//   분열체 돌진   the splits dart straight at the player and hurt on contact
//   최종 폭발     a final split that runs out of health bursts after a flicker: a small blast, several may go at once
// Model: VARCO crystal body (Body/Model) + VARCO crystal shards (Resources/Mobs/LumenShard). Effects: Resources/Vfx/lumen_*.
public class LumenSplitter : Enemy
{
    public static readonly Color LumenBlue = new Color(0.5f, 0.75f, 1f);
    public static readonly Color LumenViolet = new Color(0.75f, 0.55f, 1f);
    public const int FinalGeneration = 2;

    [SerializeField] private int maxHealth = 10;
    [SerializeField] private float hoverHeight = 1.5f;
    [SerializeField] private float preferredDistance = 6f;
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float damage = 9f;
    [SerializeField, Range(0f, 1f)] private float splitAt = 0.5f;

    [Header("빛의 탄환")]
    [SerializeField] private float boltInterval = 1.8f;
    [SerializeField] private float boltSpeed = 9f;

    [Header("분열체 돌진")]
    [SerializeField] private float dashInterval = 2.6f;
    [SerializeField] private float dashSpeed = 14f;
    [SerializeField] private float dashTime = 0.45f;

    [Header("최종 폭발")]
    [SerializeField] private float blastRadius = 1.7f;
    [SerializeField] private float blastDelay = 0.45f;

    private int generation;
    private Transform player;
    private PlayerStats playerStats;
    private int health;
    private bool dead, busy, splitting;
    private float stunnedUntil, phase, boltTimer, dashTimer, shake;
    private Vector3 knockback, wander;
    private Coroutine action;

    private Transform body, model;
    private Quaternion modelRest = Quaternion.identity;
    private GameObject coreGlow, blob;
    private readonly List<Transform> shards = new List<Transform>();
    private readonly List<Vector3> shardOrbit = new List<Vector3>();
    private TrailRenderer trail;
    private Light coreLight;
    private Renderer[] bodyRenderers;
    private MaterialPropertyBlock block;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private Color baseEmission = new Color(0.6f, 1f, 1.6f);

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;
    public int Generation => generation;
    private float HealthFraction => (float)Health / Mathf.Max(1, maxHealth);
    private bool Final => generation >= FinalGeneration;
    private float Size => Mathf.Max(0.1f, transform.lossyScale.x);

    private void Awake()
    {
        health = maxHealth;
        phase = Random.Range(0f, 10f);
        boltTimer = Random.Range(0.8f, boltInterval);
        dashTimer = Random.Range(1f, dashInterval);
        body = transform.Find("Body");
        if (body == null) { body = new GameObject("Body").transform; body.SetParent(transform, false); }
        model = body.Find("Model");
        if (model != null) modelRest = model.localRotation;
        bodyRenderers = System.Array.FindAll(GetComponentsInChildren<Renderer>(), r => r is MeshRenderer && !r.name.StartsWith("Vfx"));
        foreach (Renderer r in bodyRenderers)
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(EmissionId)) { baseEmission = r.sharedMaterial.GetColor(EmissionId); break; }
        block = new MaterialPropertyBlock();
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null) { player = movement.transform; playerStats = movement.GetComponent<PlayerStats>(); }
        BuildLook();
    }

    // How each generation looks: the body paler and smaller each time, fewer shards; the last is only a blob of light
    private void BuildLook()
    {
        float pale = generation / (float)FinalGeneration;
        coreGlow = Vfx.Glow(body, Vector3.zero, 0.5f, Color.Lerp(LumenBlue, Color.white, pale * 0.5f), Vfx.Pick("lumen_blob", "glow"), 40f);
        var tail = new GameObject("LightTrail");
        tail.transform.SetParent(body, false);
        trail = Vfx.Trail(tail, Color.Lerp(LumenBlue, LumenViolet, 0.4f) * (1f - pale * 0.3f), 0.35f * Size, 0.35f);
        if (Final)
        {
            if (model != null) model.gameObject.SetActive(false);
            // a dark violet core of "ink" under the light, so the little blob still reads on Asgard's white marble
            Ink(body, "lumen_blob_ink", 1.7f * Size);
            blob = Vfx.Glow(body, Vector3.zero, 1.5f * Size, Color.Lerp(LumenBlue, LumenViolet, 0.3f), Vfx.Pick("lumen_blob", "glow"), 120f);
        }
        else
        {
            var shardPrefab = Resources.Load<GameObject>("Mobs/LumenShard");
            int count = generation == 0 ? 6 : 3;
            for (int i = 0; i < count && shardPrefab != null; i++)
            {
                var s = Instantiate(shardPrefab, body);
                s.transform.localScale = Vector3.one * Random.Range(0.18f, 0.32f);
                shards.Add(s.transform);
                shardOrbit.Add(new Vector3(Random.Range(0.55f, 0.85f), Random.Range(-0.35f, 0.35f), Random.Range(0.6f, 1.4f) * (Random.value < 0.5f ? -1f : 1f)));
            }
            Tint();
        }
        var lightGo = new GameObject("CoreLight");
        lightGo.transform.SetParent(body, false);
        coreLight = lightGo.AddComponent<Light>();
        coreLight.type = LightType.Point;
        coreLight.color = LumenBlue;
        coreLight.range = 3f * Size;
        coreLight.intensity = 1.2f;
    }

    // an alpha-blended dark twin of a glow sheet, kept with its parent and turned to the camera
    public static void Ink(Transform parent, string texture, float size)
    {
        string tex = Vfx.Pick(texture, "");
        if (string.IsNullOrEmpty(tex)) return;
        VfxPiece p = Vfx.Sprite(tex, parent.position, size, Color.white * 0.9f, 0f, 1f, 1f, 60f, null, additive: false);
        p.transform.SetParent(parent, true);
        p.transform.localPosition = Vector3.zero;
        var r = p.GetComponent<Renderer>();
        if (r != null) r.sortingOrder = -1;   // drawn before the light that sits on it
    }

    // paler with each split: the crystal washes toward white and glows more
    private void Tint()
    {
        float pale = generation / (float)FinalGeneration;
        foreach (Renderer r in bodyRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, Color.Lerp(Color.white, new Color(0.85f, 0.95f, 1.15f), pale));
            block.SetColor(EmissionId, baseEmission * (1f + pale * 1.5f));
            r.SetPropertyBlock(block);
        }
    }

    public override void Scale(float healthMultiplier, float damageMultiplier)
    {
        maxHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * healthMultiplier));
        health = maxHealth;
        damage *= damageMultiplier;
    }

    // Sets up a fresh split (called right after it is made)
    private void BecomeSplit(int gen, int hp, float dmg, float speed, Room room, int gold)
    {
        generation = gen;
        maxHealth = health = Mathf.Max(1, hp);
        damage = dmg;
        moveSpeed = speed;
        boltInterval *= 0.75f;
        hoverHeight = Final ? 1.1f : hoverHeight * 0.85f;
        preferredDistance = Final ? 1.5f : preferredDistance * 0.7f;
        HomeRoom = room;
        GoldDrop = gold;
        XpDrop = Mathf.Max(1, XpDrop / 2);
        DisplayName = Final ? "빛 조각" : "분열체";
        room?.Adopt(this);
        busy = false;
        boltTimer = Random.Range(0.6f, 1.2f);
        dashTimer = Random.Range(0.4f, 1.2f);
    }

    private void Update()
    {
        if (dead || splitting)
            return;
        phase += Time.deltaTime;
        bool stunned = Time.time < stunnedUntil;
        Vector3 pos = transform.position;
        pos.y = Mathf.MoveTowards(pos.y, stunned ? 0.5f : hoverHeight + Mathf.Sin(phase * 2.3f) * 0.12f, 3f * Time.deltaTime);
        knockback = Vector3.Lerp(knockback, Vector3.zero, 6f * Time.deltaTime);
        Vector3 move = knockback;
        if (!stunned && !busy && player != null && GameManager.IsRunActive)
        {
            Vector3 to = Flat(player.position - transform.position);
            float distance = to.magnitude;
            Vector3 dir = distance > 0.01f ? to / distance : transform.forward;
            if (Random.value < Time.deltaTime * (Final ? 2.5f : 1f))
                wander = Quaternion.Euler(0f, Random.Range(-100f, 100f), 0f) * dir;
            Vector3 want = (distance - preferredDistance) * dir * (Final ? 1.5f : 0.6f) + Vector3.Cross(Vector3.up, dir) * Mathf.Sin(phase * 1.1f) * 1.2f + wander * 1.5f;
            move += Vector3.ClampMagnitude(want, moveSpeed);
            boltTimer -= Time.deltaTime;
            dashTimer -= Time.deltaTime;
            if (generation > 0 && dashTimer <= 0f && distance <= 9f && distance >= 1.5f)
            {
                dashTimer = dashInterval * (Final ? 0.7f : 1f) * Random.Range(0.8f, 1.2f);
                action = StartCoroutine(Run(Dash()));
            }
            else if (!Final && boltTimer <= 0f && distance <= 13f)
            {
                boltTimer = boltInterval * Random.Range(0.85f, 1.15f);
                action = StartCoroutine(Run(Bolt()));
            }
        }
        pos += move * Time.deltaTime;
        transform.position = HomeRoom == null ? pos : HomeRoom.ClampToFloor(pos, 0.8f);
        Animate();
    }

    private IEnumerator Run(IEnumerator routine)
    {
        busy = true;
        yield return routine;
        busy = false;
        action = null;
    }

    // ───────────── 빛의 탄환 ─────────────

    private IEnumerator Bolt()
    {
        Vfx.Sprite(Vfx.Pick("lumen_bolt", "glow"), body.position, 0.9f * Size, LumenBlue * 1.6f, 0.3f, 0.5f, 1.2f, 0f);
        for (float t = 0f; t < 0.3f && !dead; t += Time.deltaTime) { shake = Mathf.Max(shake, 0.3f); yield return null; }
        if (dead || player == null) yield break;
        Vector3 from = body.position;
        Vector3 aim = Flat(player.position - from).normalized;
        int count = generation == 0 ? 1 : 1;
        for (int i = 0; i < count; i++)
            LumenBolt.Fire(from, aim, boltSpeed, damage * OutgoingDamageMultiplier, this, 0.45f * Mathf.Max(1f, Size));
        yield return new WaitForSeconds(0.25f);
    }

    // ───────────── 분열체 돌진 ─────────────

    private IEnumerator Dash()
    {
        // a flicker of warning, then it darts in a straight line
        Vector3 dir = player != null ? Flat(player.position - transform.position).normalized : transform.forward;
        Vfx.Sprite(Vfx.Pick("lumen_bolt", "glow"), body.position, 1.3f * Size, LumenViolet * 1.6f, 0.35f, 1.4f, 0.6f, 0f);
        for (float t = 0f; t < 0.35f && !dead; t += Time.deltaTime)
        {
            transform.position -= dir * 1.2f * Time.deltaTime;
            shake = 0.6f;
            yield return null;
        }
        if (trail != null) trail.widthMultiplier = 2.2f;
        bool hit = false;
        for (float t = 0f; t < dashTime && !dead; t += Time.deltaTime)
        {
            Vector3 next = transform.position + dir * dashSpeed * Time.deltaTime;
            next.y = Mathf.MoveTowards(transform.position.y, 1f, 3f * Time.deltaTime);
            transform.position = HomeRoom == null ? next : HomeRoom.ClampToFloor(next, 0.8f);
            if (!hit && playerStats != null && FlatDistance(player.position, transform.position) <= 0.9f * Mathf.Max(1f, Size))
            {
                hit = true;
                playerStats.TakeDamage(damage * 1.2f * OutgoingDamageMultiplier, this);
                player.GetComponent<PlayerMovement>()?.Push(dir * 5f);
                Vfx.Sprite(Vfx.Pick("lumen_chip", "flare"), player.position + Vector3.up * 0.9f, 1.3f, Color.white, 0.25f, 0.6f, 1.2f, 30f);
            }
            yield return null;
        }
        if (trail != null) trail.widthMultiplier = 1f;
        yield return new WaitForSeconds(0.3f);
    }

    // ───────────── 분열 ─────────────

    private IEnumerator Split()
    {
        splitting = true;
        if (action != null) StopCoroutine(action);
        Vector3 at = body.position;
        Vfx.Sprite(Vfx.Pick("lumen_split", "flare"), at, 2.4f * Size, Color.white * 1.3f, 0.45f, 0.6f, 1.4f, 0f);
        EnemyFeel.Roar(transform, LumenBlue, 2f * Size, 0.1f);
        for (float t = 0f; t < 0.25f; t += Time.deltaTime) { shake = 1.5f; Animate(); yield return null; }

        var prefab = Resources.Load<LumenSplitter>("Mobs/Lumen");
        if (prefab != null)
        {
            Vector3 side = Vector3.Cross(Vector3.up, player != null ? Flat(player.position - transform.position).normalized : transform.forward);
            int childHp = Mathf.Max(1, Mathf.RoundToInt(maxHealth * 0.4f));
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = transform.position + side * (i == 0 ? 0.8f : -0.8f) * Size;
                var child = Instantiate(prefab, p, transform.rotation, transform.parent);
                child.transform.localScale = transform.localScale * (generation + 1 >= FinalGeneration ? 0.6f : 0.72f);
                child.BecomeSplit(generation + 1, childHp, damage * 0.75f, moveSpeed * 1.45f, HomeRoom, Mathf.Max(1, GoldDrop / 2));
                child.knockback = side * (i == 0 ? 5f : -5f);
                child.EmpowerFrom(this);
            }
        }
        // the parent is gone (its halves carry on and count for the room); no loot from it
        dead = true;
        Destroy(gameObject);
    }

    private void EmpowerFrom(LumenSplitter parent)
    {
        if (parent.Empowered)
            Empower(1.4f, 4f);
    }

    // ───────────── 최종 폭발 ─────────────

    private IEnumerator FinalBurst()
    {
        dead = true;
        if (action != null) StopCoroutine(action);
        RaiseDied();
        Vector3 at = Flat(transform.position);
        VfxPiece warn = Vfx.Decal(at + Vector3.up * 0.2f, blastRadius * 2.3f, LumenViolet, Vfx.Pick("lumen_warn", "ring"), 120f);
        for (float t = 0f; t < blastDelay; t += Time.deltaTime)
        {
            float k = t / blastDelay;
            warn.Set(Color.Lerp(LumenViolet, Color.white, k) * (1f + k), blastRadius * 2.3f * (1.1f - 0.1f * k), 0.5f + 0.5f * Mathf.PingPong(t * 12f, 1f));
            if (blob != null) blob.GetComponent<VfxPiece>()?.Set(Color.white * (1.5f + Mathf.PingPong(t * 14f, 1.5f)), 1.5f * Size * (1f + k * 0.4f));
            shake = 1f;
            Animate();
            yield return null;
        }
        Destroy(warn.gameObject);
        Vfx.Sprite(Vfx.Pick("lumen_burst", "flare"), at + Vector3.up * 0.8f, blastRadius * 2.4f, Color.white * 1.3f, 0.35f, 0.5f, 1.3f, 0f);
        Vfx.Burst(at + Vector3.up * 0.8f, LumenBlue, 12, 5f, 0.12f, 0.4f);
        EnemyFeel.Shake(at, 0.12f, 0.06f);
        PlayerStats stats = playerStats != null ? playerStats : FindAnyObjectByType<PlayerStats>();
        if (stats != null && FlatDistance(stats.transform.position, at) <= blastRadius)
            stats.TakeDamage(damage * 1.1f * OutgoingDamageMultiplier, this);
        Destroy(gameObject);
    }

    // ───────────── Body ─────────────

    private void Animate()
    {
        shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 2.5f);
        Vector3 jitter = Random.insideUnitSphere * 0.06f * shake * Size;
        body.localPosition = new Vector3(0f, Mathf.Sin(phase * 3f) * 0.05f, 0f) + jitter / Mathf.Max(0.01f, transform.lossyScale.x);
        if (model != null && model.gameObject.activeSelf)
            model.localRotation = Quaternion.Euler(Mathf.Sin(phase * 1.3f) * 8f, phase * 25f, Mathf.Cos(phase * 1.1f) * 8f) * modelRest;
        for (int i = 0; i < shards.Count; i++)
        {
            if (shards[i] == null) continue;
            Vector3 o = shardOrbit[i];
            float a = phase * o.z + i * 1.7f;
            shards[i].localPosition = new Vector3(Mathf.Cos(a) * o.x, o.y + Mathf.Sin(a * 1.6f) * 0.12f, Mathf.Sin(a) * o.x) + Random.insideUnitSphere * 0.15f * shake;
            shards[i].localRotation = Quaternion.Euler(phase * 60f + i * 40f, phase * 90f, i * 30f);
        }
        float pale = generation / (float)FinalGeneration;
        if (coreGlow != null)
            coreGlow.GetComponent<VfxPiece>()?.Set(Color.Lerp(LumenBlue, Color.white, pale * 0.6f) * (0.7f + Mathf.Sin(phase * 5f) * 0.15f + shake) * (1f - pale * 0.3f), (Final ? 0.45f : 0.28f - pale * 0.06f) * Size);
        if (coreLight != null) coreLight.intensity = 1.2f + shake * 2f;
    }

    public override void TakeHit(Vector3 from, int amount)
    {
        if (dead || splitting)
            return;
        health -= Mathf.Max(1, Mitigate(amount));
        if (!busy) knockback = Flat(transform.position - from).normalized * 4f;
        shake = 1.2f;
        StartCoroutine(Flash());
        // light chips knocked loose
        Vfx.Sprite(Vfx.Pick("lumen_chip", "flare"), body.position, 1.2f * Size, Color.white, 0.25f, 0.6f, 1.2f, Random.Range(-60f, 60f));
        Vfx.Burst(body.position, LumenViolet, 5, 3f, 0.1f, 0.3f);
        if (!Final && health <= maxHealth * splitAt)
        {
            // (alive until its halves are out: a blow past zero would have it counted dead, and the hall cleared,
            // before its halves were adopted)
            health = Mathf.Max(1, health);
            StartCoroutine(Split());
            return;
        }
        if (health <= 0)
            StartCoroutine(FinalBurst());
    }

    public override void Knockdown(float seconds)
    {
        if (dead || splitting) return;
        Interrupt();
        stunnedUntil = Time.time + seconds;
    }

    public override void Interrupt()
    {
        if (dead || splitting || action == null) return;
        StopCoroutine(action);
        busy = false;
        action = null;
        if (trail != null) trail.widthMultiplier = 1f;
    }

    private IEnumerator Flash()
    {
        FlashTint(new Color(1f, 0.7f, 0.75f));
        yield return new WaitForSeconds(0.08f);
        ClearFlash();
        if (!Final) Tint();
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}

// 빛의 탄환: a small orb of light flying straight; it hurts the player on touch and fades out after a while
public class LumenBolt : MonoBehaviour
{
    private Vector3 dir;
    private float speed, dmg, radius, life;
    private Enemy source;
    private PlayerStats stats;

    public static void Fire(Vector3 from, Vector3 dir, float speed, float damage, Enemy source, float radius)
    {
        var b = new GameObject("LumenBolt").AddComponent<LumenBolt>();
        b.transform.position = from;
        b.dir = dir.normalized;
        b.speed = speed;
        b.dmg = damage;
        b.source = source;
        b.radius = radius;
        b.life = 2.4f;
        b.stats = FindAnyObjectByType<PlayerStats>();
        LumenSplitter.Ink(b.transform, "lumen_bolt_ink", 0.8f);
        Vfx.Glow(b.transform, Vector3.zero, 0.7f, LumenSplitter.LumenBlue * 1.4f, Vfx.Pick("lumen_bolt", "glow"), 200f);
        Vfx.Trail(b.gameObject, LumenSplitter.LumenViolet, 0.25f, 0.2f);
    }

    private void Update()
    {
        life -= Time.deltaTime;
        transform.position += dir * speed * Time.deltaTime;
        if (stats != null)
        {
            Vector3 p = stats.transform.position;
            if (new Vector2(p.x - transform.position.x, p.z - transform.position.z).magnitude <= radius + 0.3f)
            {
                stats.TakeDamage(dmg, source);
                Vfx.Sprite(Vfx.Pick("lumen_chip", "flare"), transform.position, 1f, Color.white, 0.2f, 0.6f, 1.2f, 0f);
                Destroy(gameObject);
                return;
            }
        }
        if (life <= 0f)
        {
            Vfx.Flash(transform.position, 0.6f, LumenSplitter.LumenBlue, 0.15f);
            Destroy(gameObject);
        }
    }
}
