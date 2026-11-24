using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Random = UnityEngine.Random;

// 티르, god of war and oaths: Asgard's mid-boss, waiting in the oath hall before Heimdall's arena. One-handed sword,
// every attack an area blow or a charge, every one marked on the floor before it lands, one after another with little
// pause between them. Each phase has its own full health bar: emptying phase 1's starts phase 2 with a new one.
//  Phase 1: 심판의 강타 (overhead slam, a wide ring of force), 대지의 참격 (a huge fan in front),
//    전쟁의 돌진 (a straight charge that leaves fire behind), 전장의 회오리 (a spinning blade storm chasing the player),
//    심판의 균열 (seven fissures of fire fanning out across the hall).
//  Phase 2 (a fresh bar): 천벌의 낙하 (leaps onto the player, a blast and then a wider ring), 맹세의 연속 돌진 (four charges,
//    re-aimed each time, their trails burning, the last ending in a slam), 신성한 폭발 (charges up, a huge blast round him,
//    then a ring of pillars beyond it), 전쟁의 파동 (a blade storm, then sword waves in every direction, three times),
//    최후의 맹세 (gathers his power, a blinding charge, a colossal explosion where it ends). He casts faster, barely rests
//    and follows his big moves with quick blows.
//  Both phases (the user's notes, TyrBoss.Oath): 끊어진 맹세 (rune chains that hold the player for a slam), 늑대의 굴레
//    (the spectral wolf charges, Týr runs the lane after it), 승리의 룬 (rune swords flung one by one, the last by his hand).
// The model, sword and motions are VARCO (Models/Tyr, Animations/Tyr); the effects are VARCO sheets (Vfx/tyr_*).
[RequireComponent(typeof(CharacterController))]
public partial class TyrBoss : Enemy
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static int StateOf(string name) => Animator.StringToHash("Base Layer." + name);
    private static readonly int LocoState = StateOf("Locomotion"), SlamState = StateOf("Slam"), CleaveState = StateOf("Cleave"),
        DashState = StateOf("Dash"), WhirlState = StateOf("Whirl"), RiftState = StateOf("Rift"), LeapState = StateOf("Leap"),
        ChargeState = StateOf("Charge"), BlastState = StateOf("Blast"), DragState = StateOf("Drag"), FinalState = StateOf("FinalSlam"),
        HitState = StateOf("Hit"), DieState = StateOf("Die"), RoarState = StateOf("Roar");

    // When each motion's blow lands, in seconds from the state's start (measured on the cut clips)
    private const float SlamHit = 0.95f, CleaveHit = 0.62f, RiftHit = 0.85f, BlastHit = 0.75f, FinalHit = 0.45f;

    public static readonly Color Crimson = new Color(1f, 0.32f, 0.18f);
    public static readonly Color Gold = new Color(1f, 0.78f, 0.38f);

    [Header("Parts")]
    [SerializeField] private Animator animator;

    [Header("Combat")]
    [SerializeField] private int health = 150;      // each phase's bar (phase 2 starts with a full one)
    [SerializeField] private float damage = 17f;
    [SerializeField] private float walkSpeed = 2.8f;
    [SerializeField] private float runSpeed = 6.5f;
    // Every windup and pause takes this fraction of its designed length, by phase (lower = faster)
    [SerializeField, Range(0.4f, 1f)] private float phaseOneCast = 0.85f;
    [SerializeField, Range(0.4f, 1f)] private float phaseTwoCast = 0.7f;

    private CharacterController controller;
    private Transform player;
    private PlayerStats playerStats;
    private PlayerMovement playerMovement;
    private int maxHealth;
    private int phase = 1;
    private bool busy, dead, dashing;
    private float invulnerableUntil;
    private Coroutine action;
    private readonly List<GameObject> warnings = new List<GameObject>();
    private Vector3 actionMove;
    private float verticalVelocity, restTimer = 0.8f, approachTime;
    private int patternIndex;
    private GameObject aura;

    private enum Pattern { Slam, Cleave, Charge, Whirl, Rift, Skyfall, OathCharges, HolyBlast, WarWave, FinalOath, BrokenOath, Wolf, Rune }
    // (끊어진 맹세, 늑대의 굴레 and 승리의 룬 from the user's notes: see TyrBoss.Oath)
    private static readonly Pattern[] PhaseOne = { Pattern.Cleave, Pattern.Charge, Pattern.Rune, Pattern.Slam, Pattern.Rift, Pattern.Wolf, Pattern.Whirl,
        Pattern.Charge, Pattern.Cleave, Pattern.BrokenOath, Pattern.Slam, Pattern.Whirl, Pattern.Rune, Pattern.Rift };
    private static readonly Pattern[] PhaseTwo = { Pattern.Skyfall, Pattern.BrokenOath, Pattern.OathCharges, Pattern.Wolf, Pattern.WarWave, Pattern.Rune,
        Pattern.HolyBlast, Pattern.Skyfall, Pattern.FinalOath, Pattern.Wolf, Pattern.OathCharges, Pattern.BrokenOath, Pattern.HolyBlast, Pattern.Rune,
        Pattern.WarWave, Pattern.FinalOath };

    public event Action<string, string> PhaseChanged; // title, subtitle
    public event Action<string> Warned;

    public int Phase => phase;
    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;

    private float S => transform.localScale.x;
    public float IntroHeight => 2.6f * S;
    // Phase 2 reaches further still
    private float Reach => phase == 2 ? 1.15f : 1f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        maxHealth = health;
        IsBoss = true;
        DisplayName = "티르";
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null)
        {
            player = movement.transform;
            playerMovement = movement;
            playerStats = movement.GetComponent<PlayerStats>();
        }
        DisplayName = "티르";
        GoldDrop = 40;
    }

    public override void Scale(float healthMultiplier, float damageMultiplier)
    {
        maxHealth = health = Mathf.Max(1, Mathf.RoundToInt(health * healthMultiplier));
        damage *= damageMultiplier;
    }

    // The hall's doors seal behind the player: the camera takes him in as he roars, his war seal blazing under him
    public IEnumerator IntroScene()
    {
        busy = true;
        FacePlayer();
        yield return new WaitForSeconds(0.4f);
        Play(RoarState, 0.2f);
        GameObject seal = TyrFx.Seal(transform.position, 7f * S, Crimson, 40f);
        yield return new WaitForSeconds(0.5f);
        BossCutscene.Title("티르", "전쟁과 맹세의 신 · 한 손의 검으로 맹세를 지킨다", Crimson);
        BossCutscene.Dolly(0.85f);
        TyrFx.Blast(transform.position, 5f);
        Shockwave.Spawn(transform.position, 5f, Gold, 10);
        EnemyFeel.Roar(transform, Crimson, 6f, 0.35f);
        yield return new WaitForSeconds(1.5f);
        if (seal != null)
            Destroy(seal);
        restTimer = 0.5f;
        busy = false;
    }

    private Vector3 Center => HomeRoom != null ? HomeRoom.transform.position : transform.position;

    private void Update()
    {
        if (dead)
            return;
        float dt = Time.deltaTime;
        if (!busy)
            restTimer -= dt;

        Vector3 move = Vector3.zero;
        if (!busy && player != null && GameManager.IsRunActive)
        {
            Vector3 to = Flat(player.position - transform.position);
            float distance = to.magnitude;
            if (distance > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 480f * dt);
            move = Think(to, distance);
        }
        move += actionMove;

        if (controller.enabled)
        {
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            verticalVelocity += -9.81f * dt;
            controller.Move((move + Vector3.up * verticalVelocity) * dt);
        }
        if (animator != null)
            animator.SetFloat(SpeedHash, Flat(move).magnitude / Mathf.Max(0.01f, S), 0.1f, dt);
    }

    // ───────────── Pattern cycle ─────────────

    private Vector3 Think(Vector3 to, float distance)
    {
        if (restTimer > 0f)
            return Reposition(to, distance);

        Pattern[] cycle = phase == 1 ? PhaseOne : PhaseTwo;
        Pattern next = cycle[patternIndex % cycle.Length];
        // The close blows need him near: he runs in first (going ahead anyway after a moment)
        float range = next == Pattern.Cleave ? 7f : next == Pattern.Slam || next == Pattern.Whirl ? 5.5f
            : next == Pattern.Rift ? 12f : next == Pattern.HolyBlast ? 7f : next == Pattern.BrokenOath ? 11f : float.MaxValue;
        if (distance > range && (approachTime += Time.deltaTime) < 1.5f)
            return to / distance * runSpeed * (phase == 2 ? 1.2f : 1f);
        approachTime = 0f;
        patternIndex++;

        Omen(next);
        switch (next)
        {
            case Pattern.Slam: Act(JudgmentSlam(1f)); break;
            case Pattern.Cleave: Act(EarthCleave(1f)); break;
            case Pattern.Charge: Act(WarCharge()); break;
            case Pattern.Whirl: Act(Whirlwind(1f)); break;
            case Pattern.Rift: Act(JudgmentRift()); break;
            case Pattern.Skyfall: Act(Skyfall()); break;
            case Pattern.OathCharges: Act(OathCharges()); break;
            case Pattern.HolyBlast: Act(HolyBlast()); break;
            case Pattern.WarWave: Act(WarWave()); break;
            case Pattern.FinalOath: Act(FinalOath()); break;
            case Pattern.BrokenOath: Act(BrokenOath()); break;
            case Pattern.Wolf: Act(WolfFetter()); break;
            case Pattern.Rune: Act(VictoryRune()); break;
        }
        return Vector3.zero;
    }

    // Between patterns (barely a moment): closing to striking distance, circling there
    private Vector3 Reposition(Vector3 to, float distance)
    {
        Vector3 dir = to / Mathf.Max(0.01f, distance);
        float pace = walkSpeed * (phase == 2 ? 1.3f : 1f);
        if (distance > 4.5f)
            return dir * pace;
        if (distance < 2.2f)
            return -dir * pace * 0.7f;
        return Vector3.Cross(Vector3.up, dir) * pace * 0.5f;
    }

    // A windup or pause as he takes it
    private float Cast(float seconds) => seconds * (phase == 2 ? phaseTwoCast : phaseOneCast);

    // In phase 2 his big moves are followed straight away by a quick blow if the player is in reach
    private IEnumerator FollowUp()
    {
        if (phase != 2 || dead || player == null)
            yield break;
        float distance = Flat(player.position - transform.position).magnitude;
        if (distance < 7.5f)
            yield return Random.value < 0.5f ? EarthCleave(0.9f) : JudgmentSlam(0.85f);
        else if (distance < 14f)
            yield return ChargeRun(Cast(0.45f), 28f, 3f * S, damage * 1.1f, true);
    }

    // ───────────── Phase 1 ─────────────

    // 심판의 강타: the sword raised high and brought down on the floor: the stone shatters in a wide ring of force,
    // fire pillars bursting up round its edge
    private IEnumerator JudgmentSlam(float scale)
    {
        GameAudio.Play("tyr_slam", transform.position, 1.0f);   // (this move's own sound)
        FacePlayer();
        float radius = 6f * Reach * scale;
        float windup = Cast(SlamHit + 0.15f);
        Vector3 at = transform.position;
        warnings.Add(Telegraph.Circle(at, radius, windup));
        yield return Strike(SlamState, SlamHit, windup);
        ClearWarnings();
        at = transform.position;
        TyrFx.Blast(at, radius);
        Shockwave.Spawn(at, radius, Crimson, 10);
        CameraShake.Shake(0.3f, 0.25f);
        if (InCircle(at, radius))
            HitPlayer(damage * 1.3f, Away(at) * 10f);
        yield return new WaitForSeconds(Cast(0.4f));
    }

    // 대지의 참격: one great sweep of the sword: a huge wedge of fire roars across the floor in front of him
    private IEnumerator EarthCleave(float scale)
    {
        GameAudio.Play("tyr_cleave", transform.position, 1.0f);   // (this move's own sound)
        FacePlayer();
        float reach = 9f * Reach * scale, half = 75f;   // the fire wedge's own 150°
        float windup = Cast(CleaveHit + 0.38f);
        Vector3 fwd = transform.forward;
        warnings.Add(SectorTelegraph.Spawn(transform.position, fwd, 0.6f, reach, half, windup));
        yield return Strike(CleaveState, CleaveHit, windup);
        ClearWarnings();
        Vector3 at = transform.position;
        // one sweep of fire across the fan (no second arc, light burst, bloom or rock spikes)
        TyrFx.Cleave(at, fwd, reach, 1.6f);
        TyrFx.Scorch(at + fwd * reach * 0.55f, reach * 0.8f, 4f);
        CameraShake.Shake(0.2f, 0.2f);
        if (InSector(at, fwd, reach, half))
            HitPlayer(damage * 1.15f, fwd * 8f);
        yield return new WaitForSeconds(Cast(0.35f));
    }

    // 전쟁의 돌진: a crouch as he takes aim, then a straight charge through the player and well past, fire left burning
    private IEnumerator WarCharge()
    {
        yield return ChargeRun(Cast(0.75f), 26f, 3f * S, damage * 1.2f, false);
        yield return new WaitForSeconds(Cast(0.3f));
    }

    // One charge: aimed at the player and marked on the floor for `windup`, then run at `speed` (walls stop it).
    // It leaves a strip of fire along its path (in phase 2, or on a fiery charge, the fire burns whoever stands in it).
    private IEnumerator ChargeRun(float windup, float speed, float width, float hit, bool fiery)
    {
        GameAudio.Play("tyr_charge", transform.position, 1.0f);   // (this move's own sound)
        FacePlayer();
        Vector3 from = Flat(transform.position);
        Vector3 dir = transform.forward;
        float length = Mathf.Clamp(Flat(player.position - transform.position).magnitude + 7f, 10f, 22f);
        Vector3 to = InRoom(from + dir * length, 1.5f);
        length = Flat(to - from).magnitude;
        warnings.Add(Telegraph.Line(from, dir, length, width, windup));
        TyrFx.Gather(transform, Vector3.up * 1.2f * S, 1.6f, windup);
        Play(LocoState, 0.1f);
        yield return new WaitForSeconds(windup);
        ClearWarnings();

        Play(DashState, 0.06f);
        TyrFx.Pillar(transform.position, 3.5f * S);
        Vfx.Flash(transform.position + Vector3.up * S, 3f * S, Crimson, 0.2f);
        var streak = new GameObject("TyrStreak");
        streak.transform.SetParent(transform, false);
        streak.transform.localPosition = Vector3.up;
        Vfx.Trail(streak, fiery ? Gold : Crimson, 1.6f, 0.4f);
        warnings.Add(streak);
        Vector3 start = transform.position;
        yield return Dash(from, to, speed, width, hit, 10f);
        Destroy(streak, 0.4f);
        Play(LocoState, 0.15f);
        TyrFireTrail.Spawn(start, transform.position, width, phase == 2 ? 3.5f : 1.8f,
            phase == 2 || fiery ? damage * 0.25f : 0f, this, playerStats);
        TyrFx.Impact(transform.position, 2.5f * S, 0.6f);
        TyrFx.Quake(transform.position, 2.8f * S);
        Vfx.Dust(transform.position, 1.8f, 14);
        CameraShake.Shake(0.2f, 0.2f);
    }

    // A dash straight down the marked lane from `from` to `to`: placed directly with the controller off, so neither the
    // player nor anything else stops him short of the lane's marked end. It strikes the player once if it passes over
    // any part of them (the lane's half-width plus their body), throwing them aside.
    private IEnumerator Dash(Vector3 from, Vector3 to, float speed, float width, float hit, float throwSpeed)
    {
        Vector3 dir = Flat(to - from).normalized;
        float y = transform.position.y, length = Flat(to - from).magnitude;
        float time = Mathf.Max(0.05f, length / speed);
        controller.enabled = false;
        dashing = true;
        bool landed = false;
        for (float t = 0f; t < time && !dead; t += Time.deltaTime)
        {
            Vector3 p = Vector3.Lerp(from, to, Mathf.Clamp01(t / time));
            transform.position = new Vector3(p.x, y, p.z);
            if (!landed && DistanceToSegment(Flat(player.position), from, p) <= width / 2f + Body)
            {
                landed = true;
                Vector3 across = Vector3.Cross(Vector3.up, dir);
                HitPlayer(hit, (across * Mathf.Sign(Vector3.Dot(Flat(player.position) - p, across)) + dir) * throwSpeed);
            }
            yield return null;
        }
        transform.position = new Vector3(to.x, y, to.z);
        controller.enabled = true;
        dashing = false;
    }

    // 전장의 회오리: he spins with his sword held out, a storm of blades whirling round him, chasing after the player
    private IEnumerator Whirlwind(float scale)
    {
        GameAudio.Play("tyr_whirl", transform.position, 1.0f);   // (this move's own sound)
        float radius = 5f * Reach * scale;
        Warned?.Invoke("전장의 회오리! 회전하는 칼날 폭풍에서 멀리 떨어지세요");
        float windup = Cast(0.65f);
        warnings.Add(Telegraph.Circle(transform.position, radius, windup));
        yield return new WaitForSeconds(windup);
        ClearWarnings();

        // the blade storm alone shows the danger (no firelight carried round, no extra arcs flashing on top)
        Play(WhirlState, 0.08f);
        GameObject storm = TyrFx.Vortex(transform, radius);
        warnings.Add(storm);   // (so it goes with him if he's cut off)
        float tick = 0f, spin = 0f;
        float duration = phase == 2 ? 3.2f : 2.8f;
        for (float t = 0f; t < duration && !dead; t += Time.deltaTime)
        {
            // It hunts the player at a brisk walk
            Vector3 to = Flat(player.position - transform.position);
            actionMove = to.magnitude > 0.5f ? to.normalized * walkSpeed * 1.25f : Vector3.zero;
            spin += 900f * Time.deltaTime;
            if ((tick -= Time.deltaTime) <= 0f)
            {
                tick = 0.3f;
                if (InCircle(transform.position, radius))
                    HitPlayer(damage * 0.4f, Away(transform.position) * 5f);
            }
            yield return null;
        }
        actionMove = Vector3.zero;
        if (storm != null)
            Destroy(storm);
        Play(LocoState, 0.2f);
        yield return new WaitForSeconds(Cast(0.35f));
    }

    // 심판의 균열: the sword driven into the floor, seven fissures of fire racing out from it in a fan, stone and
    // flame bursting up along each, a pillar of fire at each end
    private IEnumerator JudgmentRift()
    {
        GameAudio.Play("tyr_rift", transform.position, 1.0f);   // (this move's own sound)
        FacePlayer();
        var angles = new List<float>();
        for (int i = -3; i <= 3; i++)
            angles.Add(i * 18f);
        float length = 20f * Reach, width = 2.3f;
        float windup = Cast(RiftHit + 0.35f);
        Vector3 fwd = transform.forward;
        Vector3 origin = Flat(transform.position);
        foreach (float a in angles)
            warnings.Add(Telegraph.Line(origin, Quaternion.Euler(0f, a, 0f) * fwd, length, width, windup));
        yield return Strike(RiftState, RiftHit, windup);
        ClearWarnings();
        TyrFx.Blast(origin + fwd, 3f);
        CameraShake.Shake(0.25f, 0.22f);
        // The fissures run out from his sword; anyone they pass under is struck once
        bool struck = false;
        float run = 0f, next = 1f;
        const float speed = 30f;
        while (run < length && !dead)
        {
            run += speed * Time.deltaTime;
            while (next <= run && next <= length)
            {
                // (sparser rock along each crack: one every 4 m instead of every 1.6 m)
                foreach (float a in angles)
                    TyrFx.Spike(origin + Quaternion.Euler(0f, a, 0f) * fwd * next, Random.Range(1.6f, 2f));
                next += 4f;
            }
            if (!struck)
                foreach (float a in angles)
                {
                    Vector3 d = Quaternion.Euler(0f, a, 0f) * fwd;
                    Vector3 rel = Flat(player.position) - origin;
                    float along = Vector3.Dot(rel, d);
                    if (along > -Body && along < run && Vector3.Cross(d, rel).magnitude <= width / 2f + Body)
                    {
                        struck = true;
                        HitPlayer(damage * 1.05f, Vector3.up);
                        break;
                    }
                }
            yield return null;
        }
        // The cracks keep smouldering a moment, and each ends in a pillar of fire
        foreach (float a in angles)
        {
            Vector3 d = Quaternion.Euler(0f, a, 0f) * fwd;
            Vector3 end = InRoom(origin + d * length, 0.5f);
            TyrFireTrail.Spawn(origin + d * 1.2f, end, width * 0.8f, 1.4f, 0f, this, playerStats, lit: false);
        }
        yield return new WaitForSeconds(Cast(0.4f));
    }

    // ───────────── Phase 2 ─────────────

    // 천벌의 낙하: he leaps high and follows the player from the air, then comes down where the mark settles:
    // a blast where he lands and, a beat later, a wider ring of pillars round it
    private IEnumerator Skyfall()
    {
        GameAudio.Play("tyr_skyfall", transform.position, 1.0f);   // (this move's own sound)
        Warned?.Invoke("천벌의 낙하! 표식이 멈춘 곳에 떨어집니다");
        FacePlayer();
        float inner = 6.5f, outer = 11.5f;
        const float height = 10f;
        Play(LeapState, 0.08f);
        yield return new WaitForSeconds(0.2f);

        Vector3 from = transform.position;
        float y = from.y;
        controller.enabled = false;
        dashing = true;
        invulnerableUntil = float.MaxValue;
        // Up, the mark on the floor following the player (turning slowly: it's the warning, not a show)
        Vector3 mark = InRoom(Flat(player.position), 2f);
        GameObject sigil = TyrFx.Seal(mark, inner * 2.15f, Crimson, 40f);
        warnings.Add(sigil);
        float track = Cast(0.85f);
        for (float t = 0f; t < track && !dead; t += Time.deltaTime)
        {
            mark = Vector3.MoveTowards(mark, InRoom(Flat(player.position), 2f), 11f * Time.deltaTime);
            float k = Mathf.Clamp01(t / 0.35f);
            transform.position = Vector3.Lerp(from, new Vector3(mark.x, y, mark.z), t / track) + Vector3.up * height * (1f - (1f - k) * (1f - k));
            if (sigil != null)
                sigil.transform.position = mark + Vector3.up * 0.06f;
            yield return null;
        }
        // The mark settles and fills, then he comes down on it
        float settle = Cast(0.5f);
        warnings.Add(Telegraph.Circle(mark, inner, settle));
        Vector3 top = transform.position;
        for (float t = 0f; t < settle && !dead; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(top, new Vector3(mark.x, y + height, mark.z), t / settle);
            yield return null;
        }
        Play(FinalState, 0.03f);
        for (float t = 0f; t < 0.16f; t += Time.deltaTime)
        {
            transform.position = new Vector3(mark.x, y, mark.z) + Vector3.up * height * (1f - t / 0.16f);
            yield return null;
        }
        transform.position = new Vector3(mark.x, y, mark.z);
        controller.enabled = true;
        dashing = false;
        invulnerableUntil = 0f;
        ClearWarnings();

        TyrFx.Blast(mark, inner);
        Shockwave.Spawn(mark, inner, Crimson, 10);
        CameraShake.Shake(0.35f, 0.3f);
        if (InCircle(mark, inner))
            HitPlayer(damage * 1.4f, Away(mark) * 11f);
        // The wider ring a beat later
        float ringWarn = Cast(0.55f);
        warnings.Add(BandTelegraph.Spawn(mark, inner, outer, ringWarn));
        yield return new WaitForSeconds(ringWarn);
        ClearWarnings();
        // the outer ring: one ring of force rolling outward
        HeimdallFx.ShockRing(mark, outer, Crimson, 0.5f);
        if (InBand(mark, inner, outer))
            HitPlayer(damage * 1.05f, Away(mark) * 9f);
        CameraShake.Shake(0.2f, 0.2f);
        yield return new WaitForSeconds(Cast(0.25f));
        yield return FollowUp();
    }

    // 맹세의 연속 돌진: four charges, each re-aimed with less warning, their trails left burning, the last ending in a slam
    private IEnumerator OathCharges()
    {
        GameAudio.Play("tyr_oath", transform.position, 1.0f);   // (this move's own sound)
        Warned?.Invoke("맹세의 연속 돌진! 네 번 돌진하고, 지나간 자리는 불탑니다");
        float[] windups = { 0.7f, 0.45f, 0.45f, 0.45f };
        for (int i = 0; i < windups.Length && !dead; i++)
        {
            yield return ChargeRun(Cast(windups[i]), 28f, 3f * S, damage * 1.1f, true);
            yield return new WaitForSeconds(0.08f);
        }
        if (!dead)
            yield return JudgmentSlam(0.9f);
    }

    // 신성한 폭발: he raises his sword and gathers holy power into it, a pillar of light pouring down onto him, then
    // lets it burst: a huge blast round him, and a beat later a ring of fire pillars beyond it (its middle then safe)
    private IEnumerator HolyBlast()
    {
        GameAudio.Play("tyr_holy", transform.position, 1.0f);   // (this move's own sound)
        Warned?.Invoke("신성한 폭발! 멀리 벗어나세요 — 곧 바깥쪽 고리도 터집니다");
        FacePlayer();
        const float radius = 10f, ring = 16f;
        float charge = Cast(1.9f);
        Vector3 at = transform.position;
        warnings.Add(Telegraph.Circle(at, radius, charge));
        Play(ChargeState, 0.15f);
        // the charge-up: his golden aura only (the strobing flashes on the sword are gone)
        warnings.Add(TyrFx.Aura(transform, 2.8f * S, Gold));
        for (float t = 0f; t < charge && !dead; t += Time.deltaTime)
            yield return null;
        // The sword comes down: the blast as it strikes the floor
        yield return Strike(BlastState, BlastHit, Cast(BlastHit));
        ClearWarnings();
        TyrFx.Blast(at, radius);
        Shockwave.Spawn(at, radius, Gold, 12);
        CameraShake.Shake(0.4f, 0.32f);
        if (InCircle(at, radius))
            HitPlayer(damage * 1.6f, Away(at) * 12f);
        float ringWarn = Cast(0.65f);
        warnings.Add(BandTelegraph.Spawn(at, radius, ring, ringWarn));
        yield return new WaitForSeconds(ringWarn);
        ClearWarnings();
        HeimdallFx.ShockRing(at, ring, Crimson, 0.55f);
        if (InBand(at, radius, ring))
            HitPlayer(damage * 1.15f, Away(at) * 9f);
        CameraShake.Shake(0.2f, 0.2f);
        yield return new WaitForSeconds(Cast(0.3f));
        yield return FollowUp();
    }

    // 전쟁의 파동: a storm of blades whirls up round him as he drags his sword through the floor, then sword waves fly
    // out in every direction (one always at the player); in phase 2 three rings of waves, each turned to fill the gaps
    private IEnumerator WarWave()
    {
        GameAudio.Play("tyr_wave", transform.position, 1.0f);   // (this move's own sound)
        Warned?.Invoke("전쟁의 파동! 검기 사이의 틈으로 피하세요");
        Play(DragState, 0.1f);
        float drag = Cast(1f);
        GameObject storm = TyrFx.Vortex(transform, 3.2f);
        warnings.Add(storm);
        for (float t = 0f; t < drag && !dead; t += Time.deltaTime)
            yield return null;
        if (storm != null)
            Destroy(storm);
        int volleys = phase == 2 ? 3 : 2;
        const int count = 16;
        for (int v = 0; v < volleys && !dead; v++)
        {
            FacePlayer();
            Vector3 aim = transform.forward;
            float offset = v * 360f / count / volleys;
            float warn = Cast(0.5f);
            for (int i = 0; i < count; i++)
                warnings.Add(Telegraph.Line(transform.position, Quaternion.Euler(0f, offset + i * 360f / count, 0f) * aim, 22f, 2f, warn));   // (the wave's width)
            yield return new WaitForSeconds(warn);
            ClearWarnings();
            for (int i = 0; i < count; i++)
                TyrWave.Fire(this, transform.position + Vector3.up * 0.6f, Quaternion.Euler(0f, offset + i * 360f / count, 0f) * aim, 22f, 16f, 2f, damage * 0.8f);
            TyrFx.Blast(transform.position, 3f);
            CameraShake.Shake(0.15f, 0.15f);
            yield return new WaitForSeconds(Cast(0.35f));
        }
        Play(LocoState, 0.2f);
        yield return new WaitForSeconds(Cast(0.3f));
    }

    // 최후의 맹세: he raises his sword and swears his oath, power pouring into it, then a blinding charge at the
    // player, its path left ablaze, and a colossal explosion where it ends
    private IEnumerator FinalOath()
    {
        GameAudio.Play("boss_warn"); GameAudio.Play("tyr_final", transform.position, 1.0f);   // (this move's own sound)
        Warned?.Invoke("최후의 맹세! 돌진 경로와 끝 지점의 대폭발을 피하세요");
        const float blastRadius = 8.5f, width = 4f;
        Play(ChargeState, 0.15f);
        warnings.Add(TyrFx.Aura(transform, 3.2f * S, Crimson));
        float charge = Cast(1.3f), lockOn = Cast(0.42f);
        // The aim follows the player while he gathers, then locks for a moment before he goes
        GameObject aimLine = null;
        for (float t = 0f; t < charge && !dead; t += Time.deltaTime)
        {
            FacePlayer();
            if (aimLine != null)
                Destroy(aimLine);
            aimLine = TyrFx.AimLine(transform.position, transform.forward, 22f, width);
            yield return null;
        }
        if (aimLine != null)
            Destroy(aimLine);
        Vector3 from = Flat(transform.position);
        Vector3 dir = transform.forward;
        Vector3 to = InRoom(from + dir * 22f, 2f);
        float length = Flat(to - from).magnitude;
        warnings.Add(Telegraph.Line(from, dir, length, width, lockOn));
        warnings.Add(Telegraph.Circle(to, blastRadius, lockOn));
        yield return new WaitForSeconds(lockOn);
        ClearWarnings();

        Play(DashState, 0.04f);
        Vfx.Flash(transform.position + Vector3.up, 5f, Color.white, 0.25f);
        var streak = new GameObject("TyrStreak");
        streak.transform.SetParent(transform, false);
        streak.transform.localPosition = Vector3.up;
        Vfx.Trail(streak, Crimson, 2.4f, 0.5f);
        warnings.Add(streak);
        Vector3 start = transform.position;
        yield return Dash(from, to, 42f, width, damage * 1.3f, 9f);
        Destroy(streak, 0.5f);
        TyrFireTrail.Spawn(start, transform.position, width, 4f, damage * 0.3f, this, playerStats);
        Play(FinalState, 0.04f);
        yield return new WaitForSeconds(FinalHit);
        // Exactly where the circle was marked (he always reaches the lane's end now)
        Vector3 at = new Vector3(to.x, transform.position.y, to.z);
        TyrFx.Blast(at, blastRadius);
        Shockwave.Spawn(at, blastRadius, Crimson, 12);
        CameraShake.Shake(0.45f, 0.35f);
        if (InCircle(at, blastRadius))
            HitPlayer(damage * 1.8f, Away(at) * 13f);
        yield return new WaitForSeconds(Cast(0.5f));
        yield return FollowUp();
    }

    // ───────────── Phase change ─────────────

    private void Awaken()
    {
        phase = 2;
        patternIndex = 0;
        StopAction();
        Act(AwakenScene(), true);
    }

    // He roars and the oath's fire wreathes him while his health bar fills up again; the blast throws the player back
    // (it does no harm)
    private IEnumerator AwakenScene()
    {
        invulnerableUntil = float.MaxValue;
        DisplayName = "티르 · 맹세의 분노";
        FacePlayer();
        Play(RoarState, 0.15f);
        PhaseChanged?.Invoke("맹세의 분노", "티르가 다시 일어나 전쟁의 맹세를 해방한다 — 공격이 거세집니다");
        GameObject seal = TyrFx.Seal(transform.position, 12f, Crimson, 40f);
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            health = Mathf.Max(1, Mathf.RoundToInt(maxHealth * t / 0.8f));
            yield return null;
        }
        health = maxHealth;
        if (seal != null)
            Destroy(seal);
        aura = TyrFx.Aura(transform, 2.8f * S, Crimson * 0.8f);
        TyrFx.Blast(transform.position, 7f);
        Shockwave.Spawn(transform.position, 7f, Gold, 12);
        EnemyFeel.Roar(transform, Crimson, 7f, 0.4f);
        if (InCircle(transform.position, 7f))
            playerMovement?.Push(Away(transform.position) * 13f);
        yield return new WaitForSeconds(1f);
        invulnerableUntil = 0f;
    }

    // ───────────── Animation, aim and hit tests ─────────────

    private Vector3 SwordPoint => transform.position + (transform.forward * 0.6f + Vector3.up * 1.6f) * S;

    private void Play(int state, float fade)
    {
        if (animator != null && animator.HasState(0, state))
            animator.CrossFadeInFixedTime(state, fade);
    }

    // Plays a motion so that its blow lands `windup` seconds from now: held back when there's time to spare,
    // played faster when there isn't (the quicker casts)
    private IEnumerator Strike(int state, float hitTime, float windup)
    {
        if (windup > hitTime)
            yield return new WaitForSeconds(windup - hitTime);
        float lead = Mathf.Min(windup, hitTime);
        Play(state, 0.06f);
        if (animator != null)
            animator.speed = hitTime / Mathf.Max(0.05f, lead);
        yield return new WaitForSeconds(lead);
        if (animator != null)
            animator.speed = 1f;
    }

    private void FacePlayer()
    {
        if (player == null)
            return;
        Vector3 to = Flat(player.position - transform.position);
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(to);
    }

    private void HitPlayer(float amount, Vector3 push)
    {
        if (playerStats == null)
            return;
        playerStats.TakeDamage(amount, this);
        playerMovement?.Push(push);
    }

    private Vector3 Away(Vector3 from)
    {
        Vector3 d = Flat(player.position - from);
        return d.sqrMagnitude > 0.01f ? d.normalized : transform.forward;
    }

    // Every hit test counts the player struck as soon as any part of their body is inside the marked shape
    private const float Body = 0.4f;

    private bool InCircle(Vector3 center, float radius) => player != null && Flat(player.position - center).magnitude <= radius + Body;

    private bool InBand(Vector3 center, float inner, float outer)
    {
        float r = Flat(player.position - center).magnitude;
        return r >= inner - Body && r <= outer + Body;
    }

    private bool InSector(Vector3 center, Vector3 forward, float radius, float halfAngle)
    {
        Vector3 rel = Flat(player.position - center);
        float d = rel.magnitude;
        if (d > radius + Body)
            return false;
        // The body's width counts at the fan's straight edges too
        return d < Body * 2f || Vector3.Angle(Flat(forward), rel) <= halfAngle + Mathf.Atan2(Body, d) * Mathf.Rad2Deg;
    }

    // A point held inside the hall, this far in from its walls
    private Vector3 InRoom(Vector3 p, float margin)
    {
        Vector3 c = Flat(Center);
        if (HomeRoom == null)
            return Flat(p);
        // A round (octagonal) battleground: held inside its circle
        if (HomeRoom.Radius > 0f)
        {
            Vector3 off = Flat(p) - c;
            float limit = HomeRoom.Radius - margin;
            return off.magnitude > limit ? c + off.normalized * limit : Flat(p);
        }
        float hx = HomeRoom.Size.x / 2f - margin, hz = HomeRoom.Size.y / 2f - margin;
        return new Vector3(Mathf.Clamp(p.x, c.x - hx, c.x + hx), 0f, Mathf.Clamp(p.z, c.z - hz, c.z + hz));
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return (p - (a + ab * k)).magnitude;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    // ───────────── Plumbing ─────────────

    private void Act(IEnumerator routine, bool force = false)
    {
        if (busy && !force)
            return;
        action = StartCoroutine(RunAction(routine));
    }

    private IEnumerator RunAction(IEnumerator routine)
    {
        busy = true;
        // Step through nested routines here, so stopping this one coroutine stops all of them
        var stack = new Stack<IEnumerator>();
        stack.Push(routine);
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            if (!top.MoveNext())
            {
                stack.Pop();
                continue;
            }
            if (top.Current is IEnumerator nested)
                stack.Push(nested);
            else
                yield return top.Current;
        }
        ClearWarnings();
        actionMove = Vector3.zero;
        busy = false;
        action = null;
        Rest();
    }

    private void StopAction()
    {
        if (action != null)
            StopCoroutine(action);
        action = null;
        ClearWarnings();
        actionMove = Vector3.zero;
        if (animator != null)
            animator.speed = 1f;
        if (dashing)
        {
            // Cut off in the air: back on the floor where he is
            dashing = false;
            transform.position = new Vector3(transform.position.x, Center.y, transform.position.z);
            controller.enabled = true;
        }
        if (invulnerableUntil == float.MaxValue)
            invulnerableUntil = 0f;
        busy = false;
        Rest();
    }

    // Barely a breath between patterns
    private void Rest() => restTimer = testHold ? 999f : phase == 1 ? 0.45f : 0.15f;
    private bool testHold;

    // The warning sound each pattern gives before it starts (VARCO: tyw_*), so the ear hears what's coming
    private void Omen(Pattern p)
    {
        string key = p switch
        {
            Pattern.Slam => "tyw_slam",
            Pattern.Cleave => "tyw_cleave",
            Pattern.Charge => "tyw_wave",
            Pattern.Whirl => "tyw_whirl",
            Pattern.Rift => "tyw_rift",
            Pattern.Skyfall => "tyw_skyfall",
            Pattern.OathCharges => "tyw_wave",
            Pattern.HolyBlast => "tyw_holy",
            Pattern.WarWave => "tyw_wave",
            Pattern.FinalOath => "tyw_final",
            Pattern.BrokenOath => "tyw_oath",
            Pattern.Wolf => "tyw_wolf",
            Pattern.Rune => "tyw_rune",
            _ => null
        };
        GameAudio.Play(key, transform.position, 1f);
        GameAudio.Play("boss_warn", transform.position, 0.5f);
    }   // (editor tests: stand still between the patterns a test starts)

    private void ClearWarnings()
    {
        foreach (GameObject w in warnings)
            if (w != null)
                Destroy(w);
        warnings.Clear();
    }

    public override void TakeHit(Vector3 from, int amount)
    {
        if (dead || Time.time < invulnerableUntil)
            return;
        StartCoroutine(Flash());
        health -= Mathf.Max(1, amount);
        if (health > 0)
            return;
        // Phase 1's bar emptied: he rises again in a fury with a new one
        if (phase == 1)
        {
            health = 1;   // (not dead: the room mustn't count him beaten)
            Awaken();
            return;
        }
        Die();
    }

    // Too sure-footed to be knocked over; a heavy blow between his attacks only makes him stagger back
    public override void Knockdown(float seconds)
    {
        if (dead || busy || Time.time < invulnerableUntil)
            return;
        Act(Staggered(), true);
    }

    private IEnumerator Staggered()
    {
        GameAudio.Play("boss_stagger", transform.position, 1.0f);   // (this move's own sound)
        Play(HitState, 0.05f);
        yield return new WaitForSeconds(0.5f);
        Play(LocoState, 0.2f);
    }

    private IEnumerator Flash()
    {
        FlashTint(new Color(1f, 0.6f, 0.6f));
        yield return new WaitForSeconds(0.1f);
        ClearFlash();
    }

    private void Die()
    {
        dead = true;
        StopAllCoroutines();
        ClearWarnings();
        actionMove = Vector3.zero;
        controller.enabled = false;
        if (aura != null)
            Destroy(aura);
        Play(DieState, 0.1f);
        TyrFx.Blast(transform.position, 4f);
        RaiseDied();
        Destroy(gameObject, 4f);
    }
}

// Týr's effects: VARCO sheets played as flipbooks — tyr_cleave (a wedge of fire), tyr_fan (the blade's arc), tyr_quake
// (the floor shattering), tyr_pillar (a pillar of holy fire), tyr_dome (a fiery explosion dome), tyr_vortex (a blade
// storm), tyr_strip (burning ground), tyr_gather (power drawing in), tyr_seal (his war rune), tyr_spike, tyr_holy,
// tyr_wave and tyr_aura
public static class TyrFx
{
    private static Quaternion Lying(Vector3 forward) => Quaternion.Euler(90f, Quaternion.LookRotation(Flat(forward)).eulerAngles.y, 0f);
    private static Quaternion LyingRandom => Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    // The hall's floor is bright marble, where additive fire alone washes out to a pale glow: a solid, deep-orange copy of
    // the same frames goes down under it first (the sheet's "_ink" twin: white, its alpha the fire's brightness), so the
    // fire reads as fire on the stone and the additive layer only adds the glow
    private static readonly Color FlameInk = new Color(0.9f, 0.16f, 0.02f, 1f);
    private static void Ink(string texture, Vector3 at, float size, float seconds, Quaternion lying, float s0, float s1, float alpha = 1f)
    {
        Color c = FlameInk;
        c.a *= alpha;
        Vfx.Sprite(texture + "_ink", at - Vector3.up * 0.01f, size, c, seconds, s0, s1, 0f, lying, additive: false).Sheet(4, 4).Hold();
    }

    // ── What ties a blow into the hall: its fire lights the floor, pillars and walls round it, the screen blooms, rock
    // and dust fly, and the floor keeps a burnt scar for a while after ──

    public static void Impact(Vector3 at, float radius, float power = 1f)
    {
        Vector3 ground = new Vector3(at.x, 0f, at.z);
        Light(ground + Vector3.up * 1.5f, new Color(1f, 0.45f, 0.18f), radius * 2.4f, 9f * power, 0.7f + 0.3f * power);
        TyrBloom.Pulse(Mathf.Min(0.65f, 0.45f * power), 0.5f + 0.3f * power);   // (capped: the biggest blasts mustn't white out the screen)
        Scorch(ground, radius * 1.6f, 6f);
        Vfx.Burst(ground + Vector3.up * 0.4f, new Color(0.25f, 0.2f, 0.18f), Mathf.RoundToInt(10 + 8 * power), 7f + 3f * power, 0.22f, 2.2f);
        Vfx.Burst(ground + Vector3.up * 0.6f, new Color(1f, 0.6f, 0.25f), Mathf.RoundToInt(14 + 10 * power), 9f, 0.12f, 0.6f);
        Vfx.Dust(ground, radius * 0.7f, Mathf.RoundToInt(10 + 8 * power), new Color(0.35f, 0.28f, 0.25f));
    }

    // A plain, easy-on-the-eyes blow (every skill but his charge uses this): the floor cracks in one ring, a burnt
    // scar stays, a little dust — no flash, no light burst, no screen bloom
    public static void Blast(Vector3 at, float radius)
    {
        Vector3 ground = new Vector3(at.x, 0.11f, at.z);
        Quaternion lying = LyingRandom;
        Ink("tyr_quake", ground, radius * 2.2f, 1f, lying, 0.92f, 1.02f, 0.7f);
        Vfx.Sprite("tyr_quake", ground, radius * 2.2f, Color.white * 1.3f, 0.7f, 0.92f, 1.02f, 0f, lying).Sheet(4, 4).Hold();
        Scorch(ground, radius * 1.4f, 4f);
        Vfx.Dust(new Vector3(at.x, 0f, at.z), radius * 0.5f, 8, new Color(0.35f, 0.28f, 0.25f));
    }

    // A flash of firelight that fades away (it lights whatever is round it, so the blow belongs to the room)
    public static void Light(Vector3 at, Color color, float range, float intensity, float seconds)
    {
        var go = new GameObject("TyrFlashLight");
        go.transform.position = at;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForcePixel;
        go.AddComponent<TyrLightFade>().Init(light, intensity, seconds);
    }

    // A steady firelight carried on him (a child of his, until destroyed)
    public static GameObject Glow(Transform owner, Color color, float range, float intensity)
    {
        var go = new GameObject("TyrGlow");
        go.transform.SetParent(owner, false);
        go.transform.localPosition = Vector3.up * 1.5f;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForcePixel;
        return go;
    }

    // A burnt scar on the floor that stays a while and fades (VARCO's scorch decal, alpha-blended over the stone)
    public static void Scorch(Vector3 at, float size, float seconds)
    {
        VfxPiece scar = Vfx.Sprite("tyr_scorch", new Vector3(at.x, 0.035f, at.z), size, Color.white, 0f, 1f, 1f, 0f, LyingRandom, additive: false);
        scar.gameObject.AddComponent<TyrLinger>().Init(scar, Color.white, size, seconds);
    }

    // A long burnt gash along a line (charges, fissures): ONE crack stretched from end to end — not tiles laid in a row,
    // whose repeated pattern and overlapping tapers showed as seams
    private static int gashLayer;
    public static void Gash(Vector3 from, Vector3 to, float width, float seconds)
    {
        from.y = to.y = 0f;
        Vector3 d = to - from;
        float length = d.magnitude;
        if (length < 0.5f)
            return;
        // The crack runs down the whole frame (98% of it) and fills a fifth of it across: the frame is 4.5 widths
        // square, and a holder stretched along the line draws it out to the line's length
        float size = width * 4.5f;
        float stretch = Mathf.Max(0.35f, length / (size * 0.98f));
        Quaternion lying = Quaternion.Euler(90f, Quaternion.LookRotation(d).eulerAngles.y, 0f);
        // (crossing cracks — fissures fanning out, charge after charge — each a hair higher, so they don't flicker)
        gashLayer = (gashLayer + 1) % 8;
        Vector3 at = (from + to) / 2f + Vector3.up * (0.033f + gashLayer * 0.0015f);
        var holder = new GameObject("TyrGash");
        holder.transform.SetPositionAndRotation(at, lying);
        holder.transform.localScale = new Vector3(1f, stretch, 1f);   // (the quad's up is the crack's length)
        VfxPiece crack = Vfx.Sprite("tyr_gash", at, size, Color.white, 0f, 1f, 1f, 0f, lying, additive: false);
        crack.transform.SetParent(holder.transform, true);
        crack.gameObject.AddComponent<TyrLinger>().Init(crack, Color.white, size, seconds);
        UnityEngine.Object.Destroy(holder, seconds + 0.2f);
    }

    // A huge wedge of fire filling the swept fan (the sheet's 150° wedge has its point at the bottom centre of its frame
    // and a radius of 0.49 of the frame)
    public static void Cleave(Vector3 at, Vector3 forward, float reach, float brightness = 3.6f)
    {
        Vector3 f = Flat(forward);
        float size = reach / 0.49f;
        Vector3 ground = new Vector3(at.x, 0.13f, at.z) + f * size * 0.5f;
        Ink("tyr_cleave", ground, size, 1f, Lying(f), 0.95f, 1.03f);
        Vfx.Sprite("tyr_cleave", ground, size, Color.white * brightness, 0.85f, 0.95f, 1.03f, 0f, Lying(f)).Sheet(4, 4).Hold();
    }

    // The blade's bright arc on the floor, opening forward from `at` out to `reach`
    // (the sheet's half-disc has its centre a third of the way up its frame and spans 0.9 of the frame)
    public static void Fan(Vector3 at, Vector3 forward, float reach, Color color, float seconds = 0.45f)
    {
        Vector3 f = Flat(forward);
        float size = reach / 0.45f;
        Vector3 ground = new Vector3(at.x, 0.12f, at.z) + f * size * 0.16f;
        Ink("tyr_fan", ground, size, seconds, Lying(f), 0.95f, 1.05f);
        Vfx.Sprite("tyr_fan", ground, size, color * 2.8f, seconds, 0.95f, 1.05f, 0f, Lying(f)).Sheet(4, 4).Hold();
    }

    // The floor shattering in a ring of fire and flying rock, leaving dark cracks a moment longer
    public static void Quake(Vector3 at, float radius)
    {
        Quaternion lying = LyingRandom;
        Vector3 ground = new Vector3(at.x, 0.11f, at.z);
        Ink("tyr_quake", ground, radius * 2.2f, 1.4f, lying, 0.9f, 1.05f);
        Vfx.Sprite("tyr_quake", ground, radius * 2.2f, Color.white * 2.6f, 1f, 0.9f, 1.05f, 0f, lying).Sheet(4, 4).Hold();
    }

    // A towering pillar of holy fire standing on the floor
    public static void Pillar(Vector3 at, float height)
    {
        Vfx.Sprite("tyr_pillar", new Vector3(at.x, height * 0.46f, at.z), height, Color.white * 2f, 0.85f, 1f, 1.05f, 0f).Sheet(4, 4).Hold();
    }

    public static void PillarRing(Vector3 center, float radius, int count, float height)
    {
        float turn = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
            Pillar(center + Quaternion.Euler(0f, turn + i * 360f / count, 0f) * Vector3.forward * radius, height * Random.Range(0.85f, 1.15f));
    }

    // A dome of fire bursting up off the floor
    public static void Dome(Vector3 at, float radius)
    {
        float size = radius * 2.2f;
        Vfx.Sprite("tyr_dome", new Vector3(at.x, size * 0.4f, at.z), size, Color.white * 1.8f, 1.1f, 0.95f, 1.08f, 0f).Sheet(4, 4).Hold();
    }

    // Stone bursting up out of a glowing crack, standing on the floor
    public static void Spike(Vector3 at, float size)
    {
        Vfx.Sprite("tyr_spike", new Vector3(at.x, size * 0.42f, at.z), size, Color.white * 1.8f, 0.75f, 1f, 1.05f, 0f).Sheet(4, 4).Hold();
    }

    public static void SpikeRing(Vector3 center, float radius, int count, float size)
    {
        float turn = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
            Spike(center + Quaternion.Euler(0f, turn + i * 360f / count, 0f) * Vector3.forward * radius, size * Random.Range(0.85f, 1.15f));
    }

    // A great round blast of holy fire lying on the floor
    public static void Holy(Vector3 at, float size, Color color, float seconds)
    {
        Quaternion lying = LyingRandom;
        Ink("tyr_holy", new Vector3(at.x, 0.1f, at.z), size, seconds, lying, 0.85f, 1.1f, 0.9f);
        Vfx.Sprite("tyr_holy", new Vector3(at.x, 0.1f, at.z), size, color * 2f, seconds, 0.85f, 1.1f, 20f, lying).Sheet(4, 4).Hold();
        Vfx.Flash(new Vector3(at.x, 1.2f, at.z), size * 0.35f, color, 0.2f);
    }

    // His war rune turning on the floor until destroyed
    public static GameObject Seal(Vector3 at, float size, Color color, float spin)
    {
        return Vfx.Decal(new Vector3(at.x, 0.06f, at.z), size, color * 1.4f, "tyr_seal", spin).OpenUp(0.25f).gameObject;
    }

    // Power drawing in to a point on him (offset from his feet) for `seconds`
    public static void Gather(Transform owner, Vector3 offset, float size, float seconds)
    {
        VfxPiece piece = Vfx.Sprite("tyr_gather", owner.position + offset, size, Color.white * 1.3f, 0f, 1f, 1f, 0f).Sheet(4, 4).Hold();
        piece.transform.SetParent(owner, true);
        UnityEngine.Object.Destroy(piece.gameObject, Mathf.Max(0.15f, seconds));
    }

    // The looping war-fire rising round him (behind him from the camera), until destroyed
    public static GameObject Aura(Transform owner, float height, Color color)
    {
        VfxPiece piece = Vfx.Sprite("tyr_aura", owner.position + Vector3.up * height * 0.42f, height, color * 1.3f, 0f, 1f, 1f, 0f).Sheet(4, 4).Hold();
        piece.transform.SetParent(owner, true);
        piece.gameObject.AddComponent<BehindOwner>().Init(owner, height * 0.42f);
        return piece.gameObject;
    }

    // A storm of blades whirling round him on the floor (a child of his, until destroyed)
    public static GameObject Vortex(Transform owner, float radius)
    {
        VfxPiece storm = Vfx.Sprite("tyr_vortex", owner.position + Vector3.up * 0.15f, radius * 2.3f, Color.white * 1.3f, 0f, 1f, 1f, 0f,
            Quaternion.Euler(90f, 0f, 0f)).Sheet(4, 4).Hold();
        storm.transform.SetParent(owner, true);
        VfxPiece ink = Vfx.Sprite("tyr_vortex_ink", owner.position + Vector3.up * 0.14f, radius * 2.3f, FlameInk, 0f, 1f, 1f, 0f,
            Quaternion.Euler(90f, 0f, 0f), additive: false).Sheet(4, 4).Hold();
        ink.transform.SetParent(storm.transform, true);
        VfxPiece ring = Vfx.Decal(owner.position + Vector3.up * 0.08f, radius * 2.3f, TyrBoss.Crimson * 0.9f, "shockring", 180f);
        ring.transform.SetParent(storm.transform, true);
        return storm.gameObject;
    }

    // The thin line he aims along while gathering his power (redrawn each frame)
    public static GameObject AimLine(Vector3 from, Vector3 dir, float length, float width)
    {
        var go = new GameObject("TyrAim");
        go.transform.position = new Vector3(from.x, 0.04f, from.z);
        go.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
        Mats.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, length / 2f), new Vector3(width, 0.01f, length),
            Mats.Get("tyr_aim", new Color(0.5f, 0.08f, 0.05f), 0.2f, new Color(0.6f, 0.1f, 0.05f)));
        return go;
    }
}

// A strip of burning ground along a charge's path or a fissure: VARCO's fire strip tiled end to end, dying down after
// `seconds`; with `damage` above 0 it burns whoever stands in it every so often
public class TyrFireTrail : MonoBehaviour
{
    private const float Tick = 0.4f;

    private Vector3 from, to;
    private float width, seconds, damage, t, nextTick = 0.3f;
    private Enemy source;
    private PlayerStats target;
    private readonly List<(VfxPiece piece, float size)> tiles = new List<(VfxPiece, float)>();
    private readonly List<(VfxPiece piece, float size)> inks = new List<(VfxPiece, float)>();
    private static readonly Color ScorchInk = new Color(0.95f, 0.24f, 0.05f, 0.85f);

    public static void Spawn(Vector3 from, Vector3 to, float width, float seconds, float damage, Enemy source, PlayerStats target, bool lit = true)
    {
        from.y = to.y = 0f;
        Vector3 d = to - from;
        float length = d.magnitude;
        if (length < 0.5f)
            return;
        var go = new GameObject("TyrFireTrail");
        go.transform.position = from;
        var trail = go.AddComponent<TyrFireTrail>();
        trail.from = from; trail.to = to; trail.width = width; trail.seconds = seconds; trail.damage = damage;
        trail.source = source; trail.target = target;
        // Only the burnt crack scar shows along the path (the red strip of fire over it and its firelight are gone);
        // the scar outlasts the burning, so where it lies is where it may still burn
        TyrFx.Gash(from, to, width * 0.9f, seconds + 4f);
    }

    private void Update()
    {
        t += Time.deltaTime;
        float grow = Mathf.Clamp01(t / 0.2f), fade = Mathf.Clamp01((seconds - t) / 0.8f);
        foreach (var (piece, size) in tiles)
            if (piece != null)
                piece.Set(Color.white * 1.7f * fade, size * Mathf.Lerp(0.6f, 1f, grow), fade);
        foreach (var (piece, size) in inks)
            if (piece != null)
                piece.Set(ScorchInk, size * Mathf.Lerp(0.6f, 1f, grow), fade);
        if (damage > 0f && target != null && t >= nextTick && fade > 0.3f)
        {
            Vector3 p = target.transform.position;
            p.y = 0f;
            Vector3 ab = to - from;
            float k = Mathf.Clamp01(Vector3.Dot(p - from, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            if ((p - (from + ab * k)).magnitude <= width / 2f)
            {
                nextTick = t + Tick;
                target.TakeDamage(damage, source);
            }
        }
        if (t >= seconds)
            Destroy(gameObject);
    }
}

// Fades a flash of firelight out over its life (fast at first, then trailing off), then removes it
public class TyrLightFade : MonoBehaviour
{
    private Light lamp;
    private float start, seconds, t;

    public void Init(Light light, float intensity, float life)
    {
        lamp = light;
        start = intensity;
        seconds = Mathf.Max(0.05f, life);
    }

    private void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / seconds);
        if (lamp != null)
            lamp.intensity = start * (1f - k) * (1f - k) * (1f + 0.15f * Mathf.Sin(t * 60f));
        if (k >= 1f)
            Destroy(gameObject);
    }
}

// Holds a lasting mark (a scorch, a gash) at full strength for most of its life, then fades it out and removes it
public class TyrLinger : MonoBehaviour
{
    private VfxPiece piece;
    private Color color;
    private float size, seconds, t;

    public void Init(VfxPiece target, Color tint, float sz, float life)
    {
        piece = target;
        color = tint;
        size = sz;
        seconds = life;
        piece.Set(color, size * 0.85f, 0f);
    }

    private void Update()
    {
        t += Time.deltaTime;
        // Spreads out quickly as it appears, then holds and fades over its last third
        float grow = Mathf.Clamp01(t / 0.15f);
        float fade = Mathf.Clamp01((seconds - t) / (seconds / 3f));
        if (piece != null)
            piece.Set(color, size * Mathf.Lerp(0.85f, 1f, grow), grow * fade);
        if (t >= seconds)
            Destroy(gameObject);
    }
}

// A short surge of bloom over the whole screen as a great blow lands (a global volume of its own, its weight fading)
public class TyrBloom : MonoBehaviour
{
    private static TyrBloom instance;
    private Volume volume;
    private float weight, decay;

    public static void Pulse(float strength, float seconds)
    {
        if (instance == null)
        {
            var go = new GameObject("TyrBloom");
            instance = go.AddComponent<TyrBloom>();
            instance.volume = go.AddComponent<Volume>();
            instance.volume.isGlobal = true;
            instance.volume.priority = 50f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1.6f);
            bloom.threshold.Override(0.85f);
            bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(1f, 0.7f, 0.5f));
            instance.volume.sharedProfile = profile;
        }
        instance.weight = Mathf.Max(instance.weight, Mathf.Clamp01(strength));
        instance.decay = instance.weight / Mathf.Max(0.05f, seconds);
    }

    private void Update()
    {
        weight = Mathf.MoveTowards(weight, 0f, decay * Time.deltaTime);
        volume.weight = weight;
    }

    private void OnDestroy()
    {
        if (volume != null && volume.sharedProfile != null)
            Destroy(volume.sharedProfile);
    }
}

// Keeps an effect just behind its owner as the camera sees it, so it never covers him
public class BehindOwner : MonoBehaviour
{
    private Transform owner;
    private float height;

    public void Init(Transform target, float y)
    {
        owner = target;
        height = y;
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (owner == null || cam == null)
            return;
        Vector3 away = owner.position - cam.transform.position;
        away.y = 0f;
        transform.position = owner.position + Vector3.up * height + away.normalized * 0.35f;
    }
}

// A crimson sword wave racing over the floor; it strikes the player once if it passes over them
public class TyrWave : MonoBehaviour
{
    private static readonly Color WaveColor = new Color(1f, 0.55f, 0.45f) * 1.5f;

    private Enemy owner;
    private Vector3 direction;
    private float range, speed, width, damage, travelled;
    private bool hit;
    private PlayerStats target;
    private VfxPiece blade;

    public static void Fire(Enemy owner, Vector3 from, Vector3 direction, float range, float speed, float width, float damage)
    {
        direction.y = 0f;
        direction.Normalize();
        var go = new GameObject("TyrWave");
        go.transform.position = new Vector3(from.x, 0.15f, from.z);
        var wave = go.AddComponent<TyrWave>();
        wave.owner = owner;
        wave.direction = direction;
        wave.range = range;
        wave.speed = speed;
        wave.width = width;
        wave.damage = damage;
        wave.target = UnityEngine.Object.FindAnyObjectByType<PlayerStats>();
        // The crescent bulges toward the bottom of its frame: turned so the bulge leads
        float yaw = Quaternion.LookRotation(direction).eulerAngles.y + 180f;
        wave.blade = Vfx.Sprite("tyr_wave", go.transform.position, width * 1.6f, WaveColor, 0f, 1f, 1f, 0f, Quaternion.Euler(90f, yaw, 0f)).Sheet(4, 4);
        wave.blade.transform.SetParent(go.transform, true);
        Vfx.Trail(go, TyrBoss.Crimson, width * 0.6f, 0.2f);
    }

    private void Update()
    {
        float step = speed * Time.deltaTime;
        transform.position += direction * step;
        travelled += step;
        if (!hit && target != null && !target.IsDead)
        {
            Vector3 rel = target.transform.position - transform.position;
            rel.y = 0f;
            if (Mathf.Abs(Vector3.Dot(rel, direction)) <= 0.9f && Vector3.Cross(direction, rel).magnitude <= width / 2f + 0.4f)
            {
                hit = true;
                target.TakeDamage(damage, owner);
                target.GetComponent<PlayerMovement>()?.Push(direction * 6f);
            }
        }
        float k = travelled / range;
        if (blade != null && k > 0.75f)
            blade.Set(WaveColor * (1f - (k - 0.75f) / 0.25f), width * 1.6f);
        if (travelled >= range)
            Destroy(gameObject);
    }
}

// A fan of floor marked for a sweep: a dark sector that fills red from its inner edge out
public class SectorTelegraph : MonoBehaviour
{
    private const int Segments = 40;

    private Mesh shape, fill;
    private float inner, outer, half, seconds, t;

    public static GameObject Spawn(Vector3 center, Vector3 forward, float inner, float outer, float halfAngle, float seconds)
    {
        var go = new GameObject("TelegraphSector");
        go.transform.position = new Vector3(center.x, 0.03f, center.z);
        go.transform.rotation = Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z));
        var sector = go.AddComponent<SectorTelegraph>();
        sector.inner = inner;
        sector.outer = outer;
        sector.half = halfAngle;
        sector.seconds = Mathf.Max(0.01f, seconds);
        sector.shape = Build(new Mesh(), inner, outer, halfAngle);
        Part(go.transform, sector.shape, Mats.Get("fx_warn_ring", new Color(0.25f, 0.05f, 0.05f)), 0f);
        sector.fill = new Mesh();
        sector.fill.MarkDynamic();
        Part(go.transform, sector.fill, Mats.Get("fx_warn_fill", new Color(0.9f, 0.15f, 0.1f), 0.2f, new Color(0.8f, 0.1f, 0.05f)), 0.01f);
        sector.Update();
        return go;
    }

    private static void Part(Transform parent, Mesh mesh, Material material, float lift)
    {
        var go = new GameObject("Sector");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * lift;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // A flat band between two radii over the angles either side of forward (+z), facing up
    private static Mesh Build(Mesh mesh, float a, float b, float halfAngle)
    {
        var vertices = new Vector3[(Segments + 1) * 2];
        var triangles = new int[Segments * 6];
        for (int i = 0; i <= Segments; i++)
        {
            float angle = Mathf.Lerp(-halfAngle, halfAngle, i / (float)Segments) * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            vertices[i * 2] = dir * a;
            vertices[i * 2 + 1] = dir * b;
            if (i == Segments)
                break;
            int v = i * 2, n = i * 6;
            // Wound to face up (the angle runs clockwise seen from above)
            triangles[n] = v; triangles[n + 1] = v + 1; triangles[n + 2] = v + 3;
            triangles[n + 3] = v; triangles[n + 4] = v + 3; triangles[n + 5] = v + 2;
        }
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void Update()
    {
        t += Time.deltaTime;
        Build(fill, inner, Mathf.Lerp(inner, outer, Mathf.Clamp01(t / seconds)), half);
    }

    private void OnDestroy()
    {
        if (shape != null)
            Destroy(shape);
        if (fill != null)
            Destroy(fill);
    }
}
