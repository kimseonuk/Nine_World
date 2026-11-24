using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 속박의 사슬 바인드: golden chains knotted round a blue rune core, floating through the hall. Its chains writhe like
// snakes as it moves; struck, they come apart for a moment and knot back together.
//   사슬 휘두르기   its chains lash out wide and sweep round it
//   지면 속박       a rune seal under the player — chains burst up and hold them in place for a moment
//   사슬 감옥       a ring of chains rises round the player: stepping out of it hurts and throws them back in
//   꿰뚫기          chains burst up out of the floor one after another along a straight line
// Models: VARCO core (Body/Core) + a VARCO chain piece (Resources/Mobs/BindChainLink, 1 m along +Z) strung into
// strands by code. Effects: VARCO sheets Resources/Vfx/bind_*.
public class ChainBinder : Enemy
{
    public static readonly Color ChainGold = new Color(1f, 0.82f, 0.4f);
    public static readonly Color RuneBlue = new Color(0.4f, 0.8f, 1f);

    [SerializeField] private int maxHealth = 8;
    [SerializeField] private float hoverHeight = 1.8f;
    [SerializeField] private float preferredDistance = 6f;
    [SerializeField] private float driftSpeed = 1.9f;
    [SerializeField] private float damage = 11f;

    [Header("Chains")]
    [SerializeField] private int strands = 5;
    [SerializeField] private int piecesPerStrand = 5;
    [SerializeField] private float pieceLength = 0.42f;
    private const float ChainGirth = 1.7f;

    [Header("사슬 휘두르기")]
    [SerializeField] private float sweepRadius = 4f;
    [SerializeField] private float sweepWindup = 0.8f;
    [SerializeField] private float sweepCooldown = 3.5f;

    [Header("지면 속박")]
    [SerializeField] private float bindRadius = 1.7f;
    [SerializeField] private float bindDelay = 1f;
    [SerializeField] private float bindSeconds = 1.6f;
    [SerializeField] private float bindCooldown = 5.5f;

    [Header("사슬 감옥")]
    [SerializeField] private float prisonRadius = 4.5f;
    [SerializeField] private float prisonSeconds = 5f;
    [SerializeField] private float prisonCooldown = 14f;

    [Header("꿰뚫기")]
    [SerializeField] private float pierceLength = 14f;
    [SerializeField] private float pierceStep = 1.4f;
    [SerializeField] private float pierceWindup = 0.85f;
    [SerializeField] private float pierceCooldown = 6f;

    private Transform player;
    private PlayerStats playerStats;
    private int health;
    private bool dead, busy;
    private float stunnedUntil, phase, knock;
    private float sweepTimer, bindTimer, prisonTimer, pierceTimer, attackGap;
    private Vector3 knockback;
    private Coroutine action;
    private GameObject warning;

    private Transform body, core;
    private GameObject coreGlow;
    private Light glowLight;

    // chain strands: each a row of pieces laid along a writhing curve out from the core
    private readonly List<Transform[]> chains = new List<Transform[]>();
    private float reach = 1f, reachGoal = 1f;          // how far the chains stretch
    private float spin, spinSpeed = 30f;               // the whole knot turning about the core
    private float droop, droopGoal;                    // 1 = chains spread down over the floor (a ground attack)
    private float scatter;                             // 1 = chains blown apart (hit), knitting back
    private readonly List<Vector3> scatterDirs = new List<Vector3>();

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;
    private float HealthFraction => (float)Health / Mathf.Max(1, maxHealth);

    private void Awake()
    {
        health = maxHealth;
        phase = Random.Range(0f, 10f);
        sweepTimer = Random.Range(0.5f, 2f);
        bindTimer = Random.Range(1.5f, 3f);
        prisonTimer = Random.Range(6f, 10f);
        pierceTimer = Random.Range(2.5f, 5f);
        body = transform.Find("Body");
        if (body == null)
        {
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
        }
        core = body.Find("Core");
        Build();
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null)
        {
            player = movement.transform;
            playerStats = movement.GetComponent<PlayerStats>();
        }
    }

    private void Build()
    {
        GameObject link = BindFx.LinkPrefab;
        for (int s = 0; s < strands; s++)
        {
            var row = new Transform[piecesPerStrand];
            for (int k = 0; k < piecesPerStrand; k++)
            {
                GameObject piece = link != null ? Instantiate(link, body) : new GameObject("ChainPiece");
                piece.name = "Chain_" + s + "_" + k;
                if (link == null) piece.transform.SetParent(body, false);
                row[k] = piece.transform;
                scatterDirs.Add(Random.onUnitSphere);
            }
            chains.Add(row);
        }
        coreGlow = Vfx.Glow(body, Vector3.zero, 0.9f, RuneBlue * 0.8f, "bind_core", 40f);
        var lightGo = new GameObject("CoreLight");
        lightGo.transform.SetParent(body, false);
        glowLight = lightGo.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = RuneBlue;
        glowLight.range = 4f;
        glowLight.intensity = 1.4f;
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
        float targetHeight = stunned ? 0.7f : hoverHeight + Mathf.Sin(phase * 1.4f) * 0.15f;
        pos.y = Mathf.MoveTowards(pos.y, targetHeight, (stunned ? 5f : 2f) * Time.deltaTime);
        knockback = Vector3.Lerp(knockback, Vector3.zero, 6f * Time.deltaTime);
        Vector3 move = knockback;

        if (!stunned && !busy && player != null && GameManager.IsRunActive)
        {
            Vector3 to = Flat(player.position - transform.position);
            float distance = to.magnitude;
            Vector3 dir = distance > 0.01f ? to / distance : transform.forward;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 200f * Time.deltaTime);
            sweepTimer -= Time.deltaTime;
            bindTimer -= Time.deltaTime;
            prisonTimer -= Time.deltaTime;
            pierceTimer -= Time.deltaTime;
            attackGap -= Time.deltaTime;

            if (attackGap <= 0f)
            {
                if (distance <= sweepRadius * 0.85f && sweepTimer <= 0f)
                    Act(ChainSweep(), ref sweepTimer, sweepCooldown);
                else if (distance <= 12f && prisonTimer <= 0f)
                    Act(ChainPrison(), ref prisonTimer, prisonCooldown);
                else if (distance >= 3.5f && distance <= pierceLength && pierceTimer <= 0f)
                    Act(Pierce(), ref pierceTimer, pierceCooldown);
                else if (distance <= 12f && bindTimer <= 0f)
                    Act(GroundBind(), ref bindTimer, bindCooldown);
            }
            if (!busy)
            {
                // keeps a middling distance, weaving side to side like a hunting snake
                Vector3 side = Vector3.Cross(Vector3.up, dir) * Mathf.Sin(phase * 0.7f);
                Vector3 want = (distance - preferredDistance) * dir * 0.6f + side * 1.5f;
                move += Vector3.ClampMagnitude(want, driftSpeed);
            }
        }
        pos += move * Time.deltaTime;
        transform.position = HomeRoom == null ? pos : HomeRoom.ClampToFloor(pos, 1f);
    }

    private void Act(IEnumerator routine, ref float timer, float cooldown)
    {
        timer = cooldown;
        attackGap = 1.2f;
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
        if (warning != null)
            Destroy(warning);
        reachGoal = 1f;
        droopGoal = 0f;
        spinSpeed = 30f;
    }

    // ───────────── The chains' shape ─────────────

    private void LateUpdate()
    {
        if (chains.Count == 0)
            return;
        float dt = Time.deltaTime;
        reach = Mathf.Lerp(reach, reachGoal, 1f - Mathf.Exp(-9f * dt));
        droop = Mathf.Lerp(droop, droopGoal, 1f - Mathf.Exp(-7f * dt));
        scatter = Mathf.MoveTowards(scatter, 0f, dt * 2.2f);
        spin += spinSpeed * dt;
        // the core turns slowly and bobs
        if (core != null)
            core.localRotation = Quaternion.Euler(Mathf.Sin(phase * 0.8f) * 12f, phase * 50f, Mathf.Cos(phase * 0.6f) * 10f);

        float sinkToFloor = transform.position.y - 0.1f;
        int idx = 0;
        for (int s = 0; s < chains.Count; s++)
        {
            Transform[] row = chains[s];
            float baseAngle = s * 360f / chains.Count + spin;
            // each strand leaves the core at its own angle, tilted up or down a little
            Vector3 outDir = Quaternion.Euler(Mathf.Sin(phase * 0.9f + s) * 25f, baseAngle, 0f) * Vector3.forward;
            // a ground attack spreads them down across the floor
            Vector3 downDir = (Quaternion.Euler(0f, baseAngle, 0f) * Vector3.forward * 0.8f + Vector3.down * 0.9f).normalized;
            outDir = Vector3.Slerp(outDir, downDir, droop).normalized;
            Vector3 sideAxis = Vector3.Cross(outDir, Vector3.up).normalized;
            if (sideAxis.sqrMagnitude < 0.01f) sideAxis = Vector3.right;
            Vector3 upAxis = Vector3.Cross(sideAxis, outDir);

            Vector3 prev = Vector3.zero;
            float seg = pieceLength * reach;
            Vector3 p = prev;
            for (int k = 0; k < row.Length; k++)
            {
                float along = (k + 1) * seg;
                // the snake: a travelling wave across the strand, stronger toward its tip
                float wave = Mathf.Sin(phase * 4.5f - k * 0.9f + s * 1.7f) * 0.12f * (k + 1) / row.Length * (1f + reach * 0.3f);
                float wave2 = Mathf.Cos(phase * 3.1f - k * 0.7f + s) * 0.08f * (k + 1) / row.Length;
                Vector3 next = outDir * along + sideAxis * wave + upAxis * wave2;
                // spread across the floor rather than through it
                if (droop > 0.05f)
                {
                    Vector3 world = body.TransformPoint(next);
                    if (world.y < transform.position.y - sinkToFloor + 0.08f)
                    {
                        world.y = Mathf.Max(world.y, 0.08f);
                        next = body.InverseTransformPoint(world);
                    }
                }
                Vector3 mid = (prev + next) * 0.5f;
                if (scatter > 0f)
                    mid += scatterDirs[idx] * scatter * 0.9f;
                Transform piece = row[k];
                Vector3 d = next - prev;
                if (d.sqrMagnitude > 1e-6f)
                    piece.localRotation = Quaternion.LookRotation(d, upAxis) * Quaternion.Euler(0f, 0f, k % 2 == 0 ? 0f : 90f);
                piece.localPosition = mid;
                piece.localScale = new Vector3(seg * ChainGirth, seg * ChainGirth, seg);   // (links drawn thicker than their length, to read from the game camera)
                prev = next;
                idx++;
            }
        }
    }

    // ───────────── 사슬 휘두르기 ─────────────

    private IEnumerator ChainSweep()
    {
        Vector3 center = Flat(transform.position);
        warning = Telegraph.Circle(center, sweepRadius, sweepWindup);
        EnemyFeel.Windup(transform, transform.position, ChainGold, 1.2f);
        // the chains draw in tight and wind up
        reachGoal = 0.6f;
        spinSpeed = -120f;
        yield return new WaitForSeconds(sweepWindup);
        if (warning != null)
            Destroy(warning);
        // then lash out flat and whip round
        reachGoal = sweepRadius / (pieceLength * piecesPerStrand * Mathf.Max(0.1f, transform.lossyScale.x)) * 0.95f;
        droopGoal = 0.25f;
        spinSpeed = 900f;
        Quaternion flat = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
        Vfx.Sprite("bind_sweep", center + Vector3.up * 0.9f, sweepRadius * 2.2f, Color.white * 1.3f, 0.45f, 0.8f, 1.1f, 400f, flat);
        Vfx.Sprite("bind_sweep", center + Vector3.up * 1.1f, sweepRadius * 1.8f, ChainGold * 1.2f, 0.35f, 0.7f, 1.05f, -300f, flat * Quaternion.Euler(0f, 0f, 120f));
        EnemyFeel.Swing(center + Vector3.up, transform.forward, sweepRadius, ChainGold);
        EnemyFeel.Shake(center, 0.15f, 0.08f);
        if (!dead && playerStats != null && FlatDistance(player.position, transform.position) <= sweepRadius)
        {
            playerStats.TakeDamage(damage * OutgoingDamageMultiplier, this);
            player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - transform.position).normalized * 7f);
            Vfx.Sprite("bind_spark", player.position + Vector3.up, 1.6f, Color.white, 0.3f, 0.6f, 1.2f, 60f);
        }
        yield return new WaitForSeconds(0.45f);
        spinSpeed = 30f;
        reachGoal = 1f;
        droopGoal = 0f;
        yield return new WaitForSeconds(0.4f);
    }

    // ───────────── 지면 속박 ─────────────

    private IEnumerator GroundBind()
    {
        EnemyVoice.Of(this)?.Skill();   // (its own big-move sound)
        Vector3 spot = player != null ? Flat(player.position) : Flat(transform.position + transform.forward * 4f);
        // the chains plunge to the floor and a rune seal spreads under the player
        droopGoal = 1f;
        reachGoal = 1.3f;
        EnemyFeel.Windup(transform, transform.position, RuneBlue, 1.1f);
        Vfx.Sprite("bind_runecircle", Flat(transform.position) + Vector3.up * 0.2f, 2.6f, ChainGold, 0.9f, 0.5f, 1.1f, 60f, Quaternion.Euler(90f, 0f, 0f));
        BindFx.GroundBind(spot, bindRadius, bindDelay, damage * 0.7f * OutgoingDamageMultiplier, bindSeconds, this);
        yield return new WaitForSeconds(bindDelay + 0.3f);
    }

    // ───────────── 사슬 감옥 ─────────────

    private IEnumerator ChainPrison()
    {
        Vector3 center = player != null ? Flat(player.position) : Flat(transform.position);
        droopGoal = 1f;
        reachGoal = 1.4f;
        spinSpeed = 200f;
        EnemyFeel.Roar(transform, ChainGold, 3f, 0.12f);
        Vfx.Sprite("bind_runecircle", Flat(transform.position) + Vector3.up * 0.2f, 3f, RuneBlue, 1f, 0.5f, 1.2f, 80f, Quaternion.Euler(90f, 0f, 0f));
        BindFx.Prison(center, prisonRadius, prisonSeconds, damage * 0.4f * OutgoingDamageMultiplier, this);
        yield return new WaitForSeconds(1f);
    }

    // ───────────── 꿰뚫기 ─────────────

    private IEnumerator Pierce()
    {
        Vector3 from = Flat(transform.position);
        Vector3 dir = player != null ? Flat(player.position - transform.position).normalized : transform.forward;
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        transform.rotation = Quaternion.LookRotation(dir);
        warning = Telegraph.Line(from, dir, pierceLength, 1.6f, pierceWindup);
        droopGoal = 1f;
        reachGoal = 1.2f;
        EnemyFeel.Windup(transform, transform.position, ChainGold, 1.1f);
        Vfx.Sprite("bind_runecircle", from + Vector3.up * 0.2f, 2.4f, ChainGold, pierceWindup + 0.3f, 0.6f, 1.1f, 90f, Quaternion.Euler(90f, 0f, 0f));
        yield return new WaitForSeconds(pierceWindup);
        if (warning != null)
            Destroy(warning);
        BindFx.PierceLine(from, dir, pierceLength, pierceStep, damage * OutgoingDamageMultiplier, this);
        EnemyFeel.Lunge(from, dir, ChainGold, 1.2f);
        yield return new WaitForSeconds(0.9f);
    }

    // ───────────── Being hit ─────────────

    public override void TakeHit(Vector3 from, int amount)
    {
        if (dead)
            return;
        health -= Mathf.Max(1, Mitigate(amount));
        knockback = Flat(transform.position - from).normalized * 3.5f;
        // its chains fly loose for a moment, then knit back together
        scatter = 1f;
        for (int i = 0; i < scatterDirs.Count; i++)
            scatterDirs[i] = (Random.onUnitSphere + Flat(transform.position - from).normalized).normalized;
        StartCoroutine(Flash());
        Vfx.Sprite("bind_spark", transform.position, 1.4f, Color.white, 0.3f, 0.6f, 1.2f, 90f);
        if (health <= 0)
            StartCoroutine(Die());
    }

    public override void Knockdown(float seconds)
    {
        if (dead)
            return;
        Interrupt();
        stunnedUntil = Time.time + seconds;
        scatter = 1f;
    }

    public override void Interrupt()
    {
        if (dead || action == null)
            return;
        StopCoroutine(action);
        EndAct();
        attackGap = 1.5f;
    }

    private IEnumerator Flash()
    {
        FlashTint(new Color(1f, 0.6f, 0.6f));
        if (coreGlow != null) coreGlow.GetComponent<VfxPiece>()?.Set(Color.white * 2f, 1.3f);
        yield return new WaitForSeconds(0.1f);
        ClearFlash();
        if (coreGlow != null) coreGlow.GetComponent<VfxPiece>()?.Set(RuneBlue * 1.2f, 0.9f);
    }

    // The core cracks: the chains fly apart in a burst of links and the light goes out
    private IEnumerator Die()
    {
        dead = true;
        if (action != null)
            StopCoroutine(action);
        if (warning != null)
            Destroy(warning);
        RaiseDied();
        Vector3 at = transform.position;
        Vfx.Sprite("bind_spark", at, 3.2f, Color.white * 1.3f, 0.5f, 0.5f, 1.5f, 120f);
        Vfx.Sprite("bind_core", at, 2.4f, RuneBlue * 2f, 0.35f, 1f, 2f, 0f);
        Vfx.Burst(at, ChainGold, 22, 7f, 0.18f, 1.2f);
        Vfx.Burst(at, RuneBlue, 12, 5f, 0.14f, 0.4f);
        // every piece flies off and drops
        var pieces = new List<(Transform t, Vector3 v)>();
        foreach (Transform[] row in chains)
            foreach (Transform t in row)
            {
                t.SetParent(null, true);
                pieces.Add((t, Random.onUnitSphere * 5f + Vector3.up * 3f));
            }
        if (core != null) core.gameObject.SetActive(false);
        if (coreGlow != null) Destroy(coreGlow);
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                var (tr, v) = pieces[i];
                if (tr == null) continue;
                v += Vector3.down * 14f * Time.deltaTime;
                tr.position += v * Time.deltaTime;
                if (tr.position.y < 0.05f) { tr.position = new Vector3(tr.position.x, 0.05f, tr.position.z); v *= 0.3f; }
                tr.Rotate(v * 40f * Time.deltaTime, Space.World);
                tr.localScale *= 1f - Time.deltaTime * 0.8f;
                pieces[i] = (tr, v);
            }
            if (glowLight != null) glowLight.intensity = Mathf.Lerp(1.4f, 0f, t / 0.8f);
            yield return null;
        }
        foreach (var (tr, _) in pieces)
            if (tr != null) Destroy(tr.gameObject);
        Destroy(gameObject);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}

// The chains' floor attacks: each lives on its own object, so it plays out even if the binder falls
public static class BindFx
{
    private static GameObject linkPrefab;
    private static bool looked;

    // one chain piece, 1 m long along +Z (a holder round the VARCO chain mesh)
    public static GameObject LinkPrefab
    {
        get
        {
            if (!looked)
            {
                looked = true;
                linkPrefab = Resources.Load<GameObject>("Mobs/BindChainLink");
            }
            return linkPrefab;
        }
    }

    private static bool RunOver => GameManager.Instance != null
        && (GameManager.Instance.CurrentState == GameManager.State.Dead || GameManager.Instance.CurrentState == GameManager.State.Victory);

    private static PlayerStats Stats => Object.FindAnyObjectByType<PlayerStats>();
    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    private class Runner : MonoBehaviour { }

    private static Runner NewRunner(string name) => new GameObject(name).AddComponent<Runner>();

    // A strand of real chain bursting up out of the floor at `spot`, swaying, then sinking back
    public static GameObject RisingChain(Vector3 spot, float height, float seconds, float lean, float yaw, Transform parent = null)
    {
        GameObject link = LinkPrefab;
        var root = new GameObject("RisingChain");
        root.transform.position = spot;
        if (parent != null) root.transform.SetParent(parent, true);
        int count = 7;
        var pieces = new Transform[count];
        for (int i = 0; i < count; i++)
        {
            GameObject p = link != null ? Object.Instantiate(link, root.transform) : new GameObject("piece");
            if (link == null) p.transform.SetParent(root.transform, false);
            pieces[i] = p.transform;
        }
        root.AddComponent<RisingChainMotion>().Init(pieces, height, seconds, lean, yaw);
        return root;
    }

    // 지면 속박: a seal spreads under the spot; chains burst up and hold whoever is in it
    public static void GroundBind(Vector3 spot, float radius, float delay, float damage, float bindSeconds, Enemy source)
    {
        var r = NewRunner("BindSeal"); r.StartCoroutine(GroundBindRoutine(r.gameObject, spot, radius, delay, damage, bindSeconds, source));
    }

    private static IEnumerator GroundBindRoutine(GameObject host, Vector3 spot, float radius, float delay, float damage, float bindSeconds, Enemy source)
    {
        GameObject warn = Telegraph.Circle(spot, radius, delay);
        VfxPiece seal = Vfx.Decal(spot + Vector3.up * 0.2f, radius * 2.4f, ChainBinder.ChainGold * 0.5f, "bind_runecircle", 70f);
        for (float t = 0f; t < delay; t += Time.deltaTime)
        {
            if (RunOver) { Object.Destroy(warn); Object.Destroy(seal.gameObject); Object.Destroy(host); yield break; }
            float k = t / delay;
            seal.Set(Color.Lerp(ChainBinder.ChainGold, ChainBinder.RuneBlue, k) * (0.5f + k), radius * 2.4f * (1.2f - 0.2f * k), 0.5f + 0.5f * k);
            yield return null;
        }
        Object.Destroy(warn);
        Object.Destroy(seal.gameObject);
        Vfx.Sprite("bind_crack", spot + Vector3.up * 0.06f, radius * 2.2f, Color.white, 1.6f, 0.9f, 1f, 0f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Hold();
        Vfx.Sprite("bind_erupt", spot + Vector3.up * 1.2f, 3f, Color.white * 1.2f, 0.5f, 0.7f, 1.15f, 0f);
        for (int i = 0; i < 4; i++)
            RisingChain(spot + Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward * radius * 0.7f, 2.2f, 1.1f + bindSeconds * 0.6f, 25f, i * 90f + 225f);
        EnemyFeel.Impact(spot, radius, ChainBinder.ChainGold, 0.15f);
        PlayerStats stats = Stats;
        if (stats != null && FlatDistance(stats.transform.position, spot) <= radius)
        {
            stats.TakeDamage(damage, source);
            PlayerEffects.ApplyBind(0.08f, bindSeconds);
            // the shackle of light at the player's feet while they are held
            VfxPiece shackle = Vfx.Sprite("bind_shackle", stats.transform.position + Vector3.up * 0.8f, 2f, Color.white * 1.2f, 0f, 1f, 1f, 30f);
            for (float t = 0f; t < bindSeconds && !RunOver; t += Time.deltaTime)
            {
                if (stats == null) break;
                shackle.transform.position = stats.transform.position + Vector3.up * 0.8f;
                yield return null;
            }
            Vfx.Sprite("bind_spark", shackle.transform.position, 1.8f, Color.white, 0.3f, 0.6f, 1.3f, 60f);
            Object.Destroy(shackle.gameObject);
        }
        yield return new WaitForSeconds(0.1f);
        Object.Destroy(host);
    }

    // 사슬 감옥: a ring of chains rises round the spot; stepping out of it hurts and throws the player back in
    public static void Prison(Vector3 center, float radius, float seconds, float damage, Enemy source)
    {
        var r = NewRunner("ChainPrison"); r.StartCoroutine(PrisonRoutine(r.gameObject, center, radius, seconds, damage, source));
    }

    private static IEnumerator PrisonRoutine(GameObject host, Vector3 center, float radius, float seconds, float damage, Enemy source)
    {
        var root = new GameObject("PrisonFx");
        root.transform.position = center;
        VfxPiece ring = Vfx.Decal(center + Vector3.up * 0.2f, radius * 2.25f, ChainBinder.ChainGold * 0.3f, "bind_prison", 8f);
        ring.transform.SetParent(root.transform, true);
        // the posts of the cage: chains bursting up all round the ring
        const int posts = 10;
        for (int i = 0; i < posts; i++)
        {
            Vector3 at = center + Quaternion.Euler(0f, i * 360f / posts, 0f) * Vector3.forward * radius;
            RisingChain(at, 2.6f, seconds, 8f, i * 360f / posts + 180f, root.transform);
            Vfx.Sprite("bind_crack", at + Vector3.up * 0.06f, 1.6f, Color.white, seconds, 0.9f, 1f, 0f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Hold();
        }
        Vfx.Sprite("bind_erupt", center + Vector3.up * 1.3f, radius * 1.4f, ChainBinder.ChainGold * 0.9f, 0.5f, 0.8f, 1.1f, 0f);
        EnemyFeel.Impact(center, radius, ChainBinder.ChainGold, 0.18f);
        PlayerStats stats = Stats;
        float tick = 0f;
        for (float t = 0f; t < seconds && !RunOver; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((seconds - t) / 0.4f);
            ring.Set(ChainBinder.ChainGold * (0.6f + 0.6f * k + Mathf.Sin(t * 6f) * 0.1f), radius * 2.25f, k);
            tick -= Time.deltaTime;
            if (stats != null)
            {
                Vector3 p = stats.transform.position;
                float d = FlatDistance(p, center);
                if (d > radius - 0.35f)
                {
                    Vector3 inward = new Vector3(center.x - p.x, 0f, center.z - p.z).normalized;
                    stats.GetComponent<PlayerMovement>()?.Push(inward * 9f);
                    if (tick <= 0f)
                    {
                        tick = 0.6f;
                        stats.TakeDamage(damage, source);
                        Vfx.Sprite("bind_spark", p + Vector3.up * 0.9f, 1.4f, Color.white, 0.3f, 0.6f, 1.2f, 60f);
                    }
                }
            }
            yield return null;
        }
        Object.Destroy(root);
        Object.Destroy(host);
    }

    // 꿰뚫기: chains burst up one after another along a straight line
    public static void PierceLine(Vector3 from, Vector3 dir, float length, float step, float damage, Enemy source)
    {
        var r = NewRunner("ChainPierce"); r.StartCoroutine(PierceRoutine(r.gameObject, from, dir, length, step, damage, source));
    }

    private static IEnumerator PierceRoutine(GameObject host, Vector3 from, Vector3 dir, float length, float step, float damage, Enemy source)
    {
        PlayerStats stats = Stats;
        bool hit = false;
        float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
        for (float d = 1.2f; d <= length; d += step)
        {
            if (RunOver) break;
            Vector3 at = from + dir * d;
            RisingChain(at, 2.4f, 0.7f, 18f, yaw + Random.Range(-30f, 30f));
            RisingChain(at + Vector3.Cross(Vector3.up, dir) * 0.35f, 1.8f, 0.6f, 25f, yaw + 60f);
            Vfx.Sprite("bind_erupt", at + Vector3.up * 1f, 2.4f, Color.white * 1.1f, 0.4f, 0.7f, 1.1f, 0f);
            Vfx.Sprite("bind_crack", at + Vector3.up * 0.06f, 1.8f, Color.white, 1.2f, 0.9f, 1f, 0f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Hold();
            EnemyFeel.Shake(at, 0.08f, 0.04f);
            if (!hit && stats != null && FlatDistance(stats.transform.position, at) <= 1.1f)
            {
                hit = true;
                stats.TakeDamage(damage, source);
                PlayerEffects.ApplyBind(0.2f, 0.6f);
                Vfx.Sprite("bind_spark", stats.transform.position + Vector3.up, 1.6f, Color.white, 0.3f, 0.6f, 1.2f, 60f);
            }
            yield return new WaitForSeconds(0.06f);
        }
        yield return new WaitForSeconds(0.2f);
        Object.Destroy(host);
    }


}

// Drives a RisingChain: it whips up out of the floor, sways while it stands, then sinks back down
public class RisingChainMotion : MonoBehaviour
{
    private Transform[] pieces;
    private float height, life, lean, yaw, t, phase;

    public void Init(Transform[] p, float h, float seconds, float leanDegrees, float yawDegrees)
    {
        pieces = p;
        height = h;
        life = seconds;
        lean = leanDegrees;
        yaw = yawDegrees;
        phase = Random.Range(0f, 6f);
        Pose();
    }

    private void Update()
    {
        t += Time.deltaTime;
        if (t >= life)
        {
            Destroy(gameObject);
            return;
        }
        Pose();
    }

    private void Pose()
    {
        // up fast (0.15 s), stand, down over the last 0.25 s
        float up = Mathf.Clamp01(t / 0.15f);
        float down = Mathf.Clamp01((life - t) / 0.25f);
        float extent = Mathf.SmoothStep(0f, 1f, Mathf.Min(up, down));
        float seg = height / pieces.Length;
        Quaternion tilt = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(lean * (1f - extent * 0.4f), 0f, 0f);
        Vector3 prev = Vector3.zero;
        for (int i = 0; i < pieces.Length; i++)
        {
            float k = (i + 1f) / pieces.Length;
            float sway = Mathf.Sin(Time.time * 6f + phase - i * 0.8f) * 0.18f * k;
            Vector3 next = tilt * new Vector3(sway, k * height * extent, sway * 0.5f * Mathf.Sin(phase));
            Vector3 d = next - prev;
            Transform p = pieces[i];
            p.localPosition = (prev + next) * 0.5f - Vector3.up * height * (1f - extent) * 0.3f;
            if (d.sqrMagnitude > 1e-6f)
                p.localRotation = Quaternion.LookRotation(d) * Quaternion.Euler(0f, 0f, i % 2 == 0 ? 0f : 90f);
            float sc = Mathf.Max(0.01f, seg * Mathf.Max(0.2f, extent));
            p.localScale = new Vector3(sc * 1.9f, sc * 1.9f, sc);
            prev = next;
        }
    }
}
