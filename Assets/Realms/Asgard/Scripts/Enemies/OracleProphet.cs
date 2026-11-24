using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 오라클 — 천상의 예언자: a floating construct with a golden crest for a head, blue light leaking from its single eye,
// a golden halo behind it and two hands that hover free of its body. It reads where the player is going and strikes
// there, keeping its distance; after every prophecy it is spent for a moment — the time to hit back.
//   예언의 낙인   a magic circle where the player is about to be; it bursts a moment later
//   미래의 장벽   a wall of light springs up across the player's path and blocks it for a few seconds
//   운명의 교차   two lines crossing in an X on the player's predicted spot, then blades of light along both
//   예언의 붕괴   when badly hurt: many circles round the player, bursting one after another
// Models: VARCO body (Body/Model) + VARCO hand (Body/HandL, HandR mirrored). Effects: Resources/Vfx/oracle_*.
public class OracleProphet : Enemy
{
    public static readonly Color OracleGold = new Color(1f, 0.86f, 0.45f);
    public static readonly Color OracleBlue = new Color(0.45f, 0.82f, 1f);

    [SerializeField] private int maxHealth = 9;
    [SerializeField] private float hoverHeight = 0.55f;
    [SerializeField] private float preferredDistance = 8f;
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float damage = 11f;
    [SerializeField] private float bodyHeight = 1.6f;          // metres before spawn scaling (set by the builder)

    [Header("Prophecies")]
    [SerializeField] private float sigilRadius = 1.8f;
    [SerializeField] private float sigilDelay = 1f;
    [SerializeField] private float sigilCooldown = 3.2f;
    [SerializeField] private float wallLength = 6.5f;
    [SerializeField] private float wallSeconds = 3.5f;
    [SerializeField] private float wallCooldown = 9f;
    [SerializeField] private float crossLength = 10f;
    [SerializeField] private float crossDelay = 0.9f;
    [SerializeField] private float crossCooldown = 6f;
    [SerializeField, Range(0f, 1f)] private float collapseAt = 0.4f;
    [SerializeField] private float collapseCooldown = 11f;

    [Header("Spent after casting")]
    [SerializeField] private float spentSeconds = 1.2f;
    [SerializeField] private float spentDamageTaken = 1.5f;

    private Transform player;
    private PlayerStats playerStats;
    private int health;
    private bool dead, busy, collapseReady;
    private float stunnedUntil, spentUntil, phase, sigilTimer, wallTimer, crossTimer, collapseTimer = 0f, gap;
    private Vector3 knockback, lastPlayerPos, playerVel;
    private Coroutine action;
    private readonly List<GameObject> marks = new List<GameObject>();

    private Transform body, model, handL, handR;
    private Vector3 handLRest, handRRest, handTarget;
    private float handRaise;
    private GameObject halo, eye;
    private Light eyeLight;

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;
    private float HealthFraction => (float)Health / Mathf.Max(1, maxHealth);
    private float Size => Mathf.Max(0.1f, transform.lossyScale.x);
    private bool Spent => Time.time < spentUntil;

    private void Awake()
    {
        health = maxHealth;
        phase = Random.Range(0f, 10f);
        sigilTimer = Random.Range(1f, 2.2f);
        wallTimer = Random.Range(3f, 6f);
        crossTimer = Random.Range(2.5f, 4.5f);
        body = transform.Find("Body");
        if (body == null) { body = new GameObject("Body").transform; body.SetParent(transform, false); }
        model = body.Find("Model");
        handL = body.Find("HandL");
        handR = body.Find("HandR");
        if (handL != null) handLRest = handL.localPosition;
        if (handR != null) handRRest = handR.localPosition;
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null) { player = movement.transform; playerStats = movement.GetComponent<PlayerStats>(); lastPlayerPos = player.position; }
        float h = bodyHeight;
        halo = Vfx.Glow(body, new Vector3(0f, h * 0.36f, -0.12f), h * 0.85f, OracleGold * 0.8f, Vfx.Pick("oracle_halo", "ring"), 12f);
        eye = Vfx.Glow(body, new Vector3(0f, h * 0.3f, 0.14f), 0.35f, OracleBlue * 1.4f, "flare", 0f);
        var lightGo = new GameObject("EyeLight");
        lightGo.transform.SetParent(body, false);
        lightGo.transform.localPosition = new Vector3(0f, h * 0.3f, 0.3f);
        eyeLight = lightGo.AddComponent<Light>();
        eyeLight.type = LightType.Point;
        eyeLight.color = OracleBlue;
        eyeLight.range = 3f;
        eyeLight.intensity = 1.2f;
    }

    public override void Scale(float healthMultiplier, float damageMultiplier)
    {
        maxHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * healthMultiplier));
        health = maxHealth;
        damage *= damageMultiplier;
    }

    // ───────────── Reading the player ─────────────

    private Vector3 MoveDir => Flat(playerVel).sqrMagnitude > 0.25f ? Flat(playerVel).normalized
        : player != null ? Flat(player.position - transform.position).normalized : transform.forward;

    // where the player will be in `lead` seconds, kept on the floor
    private Vector3 Predict(float lead)
    {
        if (player == null) return Flat(transform.position);
        Vector3 p = Flat(player.position + Vector3.ClampMagnitude(playerVel, 8f) * lead);
        return HomeRoom != null ? Flat(HomeRoom.ClampToFloor(p, 1f)) : p;
    }

    private void Update()
    {
        if (dead)
            return;
        phase += Time.deltaTime;
        if (player != null)
        {
            Vector3 v = (player.position - lastPlayerPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastPlayerPos = player.position;
            playerVel = Vector3.Lerp(playerVel, Flat(v), 1f - Mathf.Exp(-6f * Time.deltaTime));
        }
        bool stunned = Time.time < stunnedUntil;
        Vector3 pos = transform.position;
        pos.y = Mathf.MoveTowards(pos.y, stunned || Spent ? hoverHeight - 0.35f : hoverHeight + Mathf.Sin(phase * 1.5f) * 0.1f, 1.5f * Time.deltaTime);
        knockback = Vector3.Lerp(knockback, Vector3.zero, 6f * Time.deltaTime);
        Vector3 move = knockback;
        if (!stunned && !busy && !Spent && player != null && GameManager.IsRunActive)
        {
            Vector3 to = Flat(player.position - transform.position);
            float distance = to.magnitude;
            Vector3 dir = distance > 0.01f ? to / distance : transform.forward;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 180f * Time.deltaTime);
            // keeps its distance, drifting sideways
            Vector3 side = Vector3.Cross(Vector3.up, dir) * Mathf.Sin(phase * 0.5f);
            move += Vector3.ClampMagnitude((distance - preferredDistance) * dir * 0.7f + side * 1.3f, moveSpeed);
            sigilTimer -= Time.deltaTime; wallTimer -= Time.deltaTime; crossTimer -= Time.deltaTime; collapseTimer -= Time.deltaTime; gap -= Time.deltaTime;
            if (!collapseReady && HealthFraction <= collapseAt) { collapseReady = true; collapseTimer = 0f; }
            if (gap <= 0f && distance <= 16f)
            {
                if (collapseReady && collapseTimer <= 0f) Act(Collapse(), ref collapseTimer, collapseCooldown);
                else if (wallTimer <= 0f && Flat(playerVel).sqrMagnitude > 1f) Act(Wall(), ref wallTimer, wallCooldown);
                else if (crossTimer <= 0f) Act(Cross(), ref crossTimer, crossCooldown);
                else if (sigilTimer <= 0f) Act(Sigil(), ref sigilTimer, sigilCooldown);
            }
        }
        pos += move * Time.deltaTime;
        transform.position = HomeRoom == null ? pos : HomeRoom.ClampToFloor(pos, 1f);
        Animate();
    }

    private void Act(IEnumerator routine, ref float timer, float cooldown)
    {
        timer = cooldown;
        gap = 0.6f;
        action = StartCoroutine(Run(routine));
    }

    private IEnumerator Run(IEnumerator routine)
    {
        busy = true;
        yield return routine;
        busy = false;
        action = null;
        handRaise = 0f;
        // spent: it sags, its light dims — open to a counterattack
        spentUntil = Time.time + spentSeconds;
    }

    // the casting gesture: hands lift toward the target, the eye flares
    private IEnumerator Gesture(Vector3 target, float seconds)
    {
        handTarget = target;
        Vfx.Sprite("flare", eye.transform.position, 1.2f * Size, OracleBlue * 1.6f, seconds, 0.6f, 1.3f, 60f);
        for (float t = 0f; t < seconds && !dead; t += Time.deltaTime)
        {
            handRaise = Mathf.SmoothStep(0f, 1f, t / seconds);
            Vector3 to = Flat(target - transform.position);
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
            yield return null;
        }
    }

    // ───────────── 예언의 낙인 ─────────────

    private IEnumerator Sigil()
    {
        Vector3 spot = Predict(sigilDelay + 0.35f);
        yield return Gesture(spot, 0.35f);
        OracleFx.Sigil(spot, sigilRadius, sigilDelay, damage * OutgoingDamageMultiplier, this);
        yield return new WaitForSeconds(0.3f);
    }

    // ───────────── 미래의 장벽 ─────────────

    private IEnumerator Wall()
    {
        Vector3 dir = MoveDir;
        Vector3 at = Flat(player.position) + dir * 2.6f;
        if (HomeRoom != null) at = Flat(HomeRoom.ClampToFloor(at, 0.5f));
        yield return Gesture(at, 0.3f);
        OracleFx.Wall(at, dir, wallLength, wallSeconds);
        yield return new WaitForSeconds(0.3f);
    }

    // ───────────── 운명의 교차 ─────────────

    private IEnumerator Cross()
    {
        EnemyVoice.Of(this)?.Skill();   // (its own big-move sound)
        Vector3 spot = Predict(crossDelay + 0.3f);
        Vector3 dir = MoveDir;
        yield return Gesture(spot, 0.3f);
        OracleFx.Cross(spot, dir, crossLength, crossDelay, damage * 1.1f * OutgoingDamageMultiplier, this);
        yield return new WaitForSeconds(crossDelay * 0.6f);
    }

    // ───────────── 예언의 붕괴 ─────────────

    private IEnumerator Collapse()
    {
        EnemyFeel.Roar(transform, OracleGold, 3f * Size, 0.15f);
        yield return Gesture(player != null ? player.position : transform.position, 0.6f);
        float[] leads = { 0.6f, 1.2f, 1.8f };
        int n = 0;
        foreach (float lead in leads)
            OracleFx.Sigil(Predict(lead + 0.9f), sigilRadius * 0.9f, 0.9f + n++ * 0.3f, damage * 0.9f * OutgoingDamageMultiplier, this);
        for (int i = 0; i < 4; i++)
        {
            Vector2 r = Random.insideUnitCircle * 4f;
            Vector3 p = Flat(player.position) + new Vector3(r.x, 0f, r.y);
            if (HomeRoom != null) p = Flat(HomeRoom.ClampToFloor(p, 1f));
            OracleFx.Sigil(p, sigilRadius * 0.9f, 0.9f + n++ * 0.3f, damage * 0.9f * OutgoingDamageMultiplier, this);
        }
        yield return new WaitForSeconds(1.2f);
    }

    // ───────────── Body ─────────────

    private void Animate()
    {
        float h = bodyHeight;
        float spent = Spent ? 1f : 0f;
        body.localRotation = Quaternion.Euler(Mathf.Sin(phase * 1.1f) * 3f + spent * 12f, 0f, Mathf.Sin(phase * 0.8f) * 3f);
        Vector3 local = handTarget.sqrMagnitude > 0f ? body.InverseTransformPoint(handTarget) : Vector3.forward;
        Vector3 reach = new Vector3(0f, h * 0.15f, h * 0.35f) + Vector3.ClampMagnitude(local.normalized, 1f) * 0.1f;
        if (handL != null) handL.localPosition = Vector3.Lerp(handLRest + Vector3.up * (Mathf.Sin(phase * 2f) * 0.04f - spent * 0.15f), reach + Vector3.left * h * 0.18f, handRaise);
        if (handR != null) handR.localPosition = Vector3.Lerp(handRRest + Vector3.up * (Mathf.Sin(phase * 2f + 1f) * 0.04f - spent * 0.15f), reach + Vector3.right * h * 0.18f, handRaise);
        float flicker = Spent ? 0.35f + Mathf.PerlinNoise(phase * 8f, 0f) * 0.3f : 1f;
        if (halo != null) halo.GetComponent<VfxPiece>()?.Set(OracleGold * (0.8f + handRaise * 0.8f) * flicker, h * 0.85f * Size);
        if (eye != null) eye.GetComponent<VfxPiece>()?.Set(OracleBlue * (1.4f + handRaise * 1.2f) * flicker, 0.35f * Size);
        if (eyeLight != null) eyeLight.intensity = (1.2f + handRaise * 2.5f) * flicker;
    }

    public override void TakeHit(Vector3 from, int amount)
    {
        if (dead) return;
        // spent after a prophecy, it takes more
        int dmg = Mathf.Max(1, Mitigate(Spent ? Mathf.RoundToInt(amount * spentDamageTaken) : amount));
        health -= dmg;
        if (!busy) knockback = Flat(transform.position - from).normalized * 3f;
        StartCoroutine(Flash());
        Vfx.Burst(body.position + Vector3.up * bodyHeight * 0.3f * Size, OracleGold, 6, 3f, 0.12f, 0.6f);
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
        busy = false;
        action = null;
        handRaise = 0f;
        spentUntil = Time.time + spentSeconds;
    }

    private IEnumerator Flash()
    {
        FlashTint(new Color(1f, 0.65f, 0.6f));
        yield return new WaitForSeconds(0.1f);
        ClearFlash();
    }

    // its light goes out: the halo shatters, the eye fades, it sinks and comes apart
    private IEnumerator Die()
    {
        dead = true;
        if (action != null) StopCoroutine(action);
        RaiseDied();
        Vector3 at = body.position + Vector3.up * bodyHeight * 0.3f * Size;
        Vfx.Sprite(Vfx.Pick("oracle_halo", "ring"), at, bodyHeight * Size * 1.4f, OracleGold * 1.6f, 0.5f, 1f, 1.8f, 90f);
        Vfx.Burst(at, OracleGold, 20, 6f, 0.16f, 0.8f);
        Vfx.Burst(at, OracleBlue, 12, 5f, 0.12f, 0.4f);
        if (halo != null) Destroy(halo);
        Vector3 p0 = transform.position;
        for (float t = 0f; t < 0.7f; t += Time.deltaTime)
        {
            float k = t / 0.7f;
            transform.position = new Vector3(p0.x, Mathf.Lerp(p0.y, 0f, k), p0.z);
            body.localRotation = Quaternion.Euler(70f * k, 0f, 15f * k);
            if (eyeLight != null) eyeLight.intensity = Mathf.Lerp(2f, 0f, k);
            yield return null;
        }
        Vfx.Dust(Flat(transform.position), 1f, 8);
        Destroy(gameObject);
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}

// The Oracle's prophecies on the floor: each lives on its own object, so it comes true even if the Oracle falls
public static class OracleFx
{
    private class Runner : MonoBehaviour { }

    private static bool RunOver => GameManager.Instance != null
        && (GameManager.Instance.CurrentState == GameManager.State.Dead || GameManager.Instance.CurrentState == GameManager.State.Victory);
    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    // 예언의 낙인
    public static void Sigil(Vector3 spot, float radius, float delay, float damage, Enemy source)
    {
        var r = new GameObject("OracleSigil").AddComponent<Runner>();
        r.StartCoroutine(SigilRoutine(r.gameObject, spot, radius, delay, damage, source));
    }

    private static IEnumerator SigilRoutine(GameObject host, Vector3 spot, float radius, float delay, float damage, Enemy source)
    {
        spot.y = 0f;
        GameObject warn = Telegraph.Circle(spot, radius, delay);
        warn.transform.SetParent(host.transform, true);
        VfxPiece sigil = Vfx.Decal(spot + Vector3.up * 0.2f, radius * 2.4f, OracleProphet.OracleGold * 0.5f, Vfx.Pick("oracle_sigil", "runes"), 40f);
        sigil.transform.SetParent(host.transform, true);
        for (float t = 0f; t < delay; t += Time.deltaTime)
        {
            if (RunOver) { Object.Destroy(host); yield break; }
            float k = t / delay;
            sigil.Set(Color.Lerp(OracleProphet.OracleGold, OracleProphet.OracleBlue, k) * (0.5f + 1.2f * k), radius * 2.4f * (1.15f - 0.15f * k), 0.5f + 0.5f * k);
            yield return null;
        }
        Object.Destroy(host);
        Vfx.Sprite(Vfx.Pick("oracle_blast", "glow"), spot + Vector3.up * 1.4f, radius * 1.9f, Color.white * 1.3f, 0.45f, 0.6f, 1.15f, 0f);
        Vfx.GroundRing(spot, radius, OracleProphet.OracleGold, 0.4f);
        Vfx.Burst(spot + Vector3.up * 0.5f, OracleProphet.OracleBlue, 12, 6f, 0.14f, 0.8f);
        EnemyFeel.Impact(spot, radius, OracleProphet.OracleGold, 0.15f);
        PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
        if (stats != null && FlatDistance(stats.transform.position, spot) <= radius)
            stats.TakeDamage(damage, source);
    }

    // 미래의 장벽: a wall of light across `dir` (blocks movement while it stands)
    public static void Wall(Vector3 center, Vector3 dir, float length, float seconds)
    {
        var r = new GameObject("OracleWall").AddComponent<Runner>();
        r.StartCoroutine(WallRoutine(r.gameObject, center, dir, length, seconds));
    }

    private static IEnumerator WallRoutine(GameObject host, Vector3 center, Vector3 dir, float length, float seconds)
    {
        const float height = 2.6f;
        center.y = 0f;
        host.transform.position = center;
        host.transform.rotation = Quaternion.LookRotation(dir.normalized);   // the wall spans the host's x
        // the light: a vertical quad (two, back to back) textured with the wall sheet
        var face = new GameObject("Vfx_wall");
        face.transform.SetParent(host.transform, false);
        face.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
        var mr = face.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Vfx.Material(Vfx.Pick("oracle_wall", "beam"), true);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        // the barrier itself
        var col = host.AddComponent<BoxCollider>();
        col.size = new Vector3(length, height, 0.5f);
        col.center = new Vector3(0f, height * 0.5f, 0f);
        col.enabled = false;
        Vfx.Sprite(Vfx.Pick("oracle_blast", "glow"), center + Vector3.up * 1.2f, 2.5f, OracleProphet.OracleGold, 0.35f, 0.6f, 1.2f, 0f);
        EnemyFeel.Shake(center, 0.12f, 0.05f);
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (RunOver) break;
            float up = Mathf.Clamp01(t / 0.25f), down = Mathf.Clamp01((seconds - t) / 0.4f);
            float h = height * Mathf.SmoothStep(0f, 1f, up);
            face.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            face.transform.localScale = new Vector3(length, Mathf.Max(0.01f, h), 1f);
            col.enabled = up >= 1f && down > 0.2f;
            block.SetColor("_BaseColor", OracleProphet.OracleGold * 1.5f * down * (0.9f + Mathf.Sin(t * 9f) * 0.1f));
            mr.SetPropertyBlock(block);
            yield return null;
        }
        Object.Destroy(host);
    }

    // 운명의 교차: an X through `spot`, its arms at ±45° to the player's heading
    public static void Cross(Vector3 spot, Vector3 dir, float length, float delay, float damage, Enemy source)
    {
        var r = new GameObject("OracleCross").AddComponent<Runner>();
        r.StartCoroutine(CrossRoutine(r.gameObject, spot, dir, length, delay, damage, source));
    }

    private static IEnumerator CrossRoutine(GameObject host, Vector3 spot, Vector3 dir, float length, float delay, float damage, Enemy source)
    {
        spot.y = 0f;
        Vector3 a = Quaternion.Euler(0f, 45f, 0f) * dir, b = Quaternion.Euler(0f, -45f, 0f) * dir;
        var warnA = Telegraph.Line(spot - a * length * 0.5f, a, length, 1.4f, delay);
        var warnB = Telegraph.Line(spot - b * length * 0.5f, b, length, 1.4f, delay);
        warnA.transform.SetParent(host.transform, true);
        warnB.transform.SetParent(host.transform, true);
        VfxPiece mark = Vfx.Decal(spot + Vector3.up * 0.2f, 3f, OracleProphet.OracleGold * 0.6f, Vfx.Pick("oracle_cross", "runes"), 0f);
        mark.transform.SetParent(host.transform, true);
        mark.transform.rotation = Quaternion.Euler(90f, Quaternion.LookRotation(dir).eulerAngles.y + 45f, 0f);
        for (float t = 0f; t < delay; t += Time.deltaTime)
        {
            if (RunOver) { Object.Destroy(host); yield break; }
            mark.Set(OracleProphet.OracleGold * (0.6f + t / delay), 3f, 0.6f + 0.4f * t / delay);
            yield return null;
        }
        Object.Destroy(warnA); Object.Destroy(warnB); Object.Destroy(mark.gameObject);
        // the blades of light along both arms
        Vector3 y = Vector3.up * 0.9f;
        var beamA = new SkiaWatcher.BeamFx(Vfx.Pick("oracle_beam", "beam"), spot - a * length * 0.5f + y, spot + a * length * 0.5f + y, 1.6f, OracleProphet.OracleGold, 0.35f);
        var beamB = new SkiaWatcher.BeamFx(Vfx.Pick("oracle_beam", "beam"), spot - b * length * 0.5f + y, spot + b * length * 0.5f + y, 1.6f, OracleProphet.OracleGold, 0.35f);
        Vfx.Sprite(Vfx.Pick("oracle_blast", "glow"), spot + Vector3.up * 1.2f, 2.6f, Color.white * 1.2f, 0.35f, 0.6f, 1.2f, 0f);
        EnemyFeel.Shake(spot, 0.15f, 0.08f);
        PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
        if (stats != null)
        {
            Vector3 p = stats.transform.position; p.y = 0f;
            if (LineDist(p, spot, a, length) <= 0.9f || LineDist(p, spot, b, length) <= 0.9f)
                stats.TakeDamage(damage, source);
        }
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            beamA.Update(t / 0.35f); beamB.Update(t / 0.35f);
            yield return null;
        }
        beamA.Destroy(); beamB.Destroy();
        Object.Destroy(host);
    }

    private static float LineDist(Vector3 p, Vector3 mid, Vector3 dir, float length)
    {
        Vector3 d = p - mid;
        float along = Mathf.Clamp(Vector3.Dot(d, dir), -length * 0.5f, length * 0.5f);
        return (d - dir * along).magnitude;
    }
}
