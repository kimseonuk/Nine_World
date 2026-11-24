using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

// 헤임달, guardian of the Bifrost: Asgard's final guardian, fought in three phases. Every pattern is marked before it
// lands (only his chains, brand and splitting orbs follow the player). Each phase has a full health bar of its own; the
// next begins when it is emptied.
//  1. Mounted on his golden lion Gulltoppr: the lion charges, rampages in four charges that leave lanes of fire
//     behind, and breathes fire. Bring the lion down to move on.
//  2. On foot: Gjallarhorn sends out sonic rings (with safe gaps); Heimsight slows the player and draws a star across the
//     arena whose lines he then cuts by teleporting rim to rim; Hofund's beam sweeps the arena from its rim;
//     the Bifrost rush (spinning dashes whose rainbow scars all erupt at the end) and the rings of judgment
//     (bands of ground around him erupting in turn, inside and out).
//  Added from the user's notes (see HeimdallBoss.Judgment): 천리안 reworked (darkness, a stalled moment, cuts round
//     the player, an opening), 빛 3갈래 in three forms, 천상의 낙인, 심판의 사슬, 천상의 십자, 황금의 검진, 빛의 분열,
//     심판의 포효 and, in the last phase, 라그나로크.
//  3. His bar refilled, wreathed in Bifrost energy: everything faster, more of every pattern,
//     and, once he is down to a tenth of this last bar, a long charge that kills the player unless they bring him
//     down (or knock him down) first.
// The rush and the slam are VARCO motions (HD_Rush, HD_GroundSlam); their effects are VARCO sheets (hd_rush, hd_burst, hd_flame).
[RequireComponent(typeof(CharacterController))]
public partial class HeimdallBoss : Enemy
{
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int SlashHash = Animator.StringToHash("Slash");
    private static readonly int HornHash = Animator.StringToHash("Horn");
    private static readonly int CastHash = Animator.StringToHash("Cast");
    private static readonly int ChargingHash = Animator.StringToHash("Charging");
    private static readonly int LandHash = Animator.StringToHash("Land");
    private static readonly int DieHash = Animator.StringToHash("Die");
    private static readonly int LocoRateHash = Animator.StringToHash("LocoRate");
    private static readonly int StaggerState = Animator.StringToHash("Base Layer.Stagger");
    private static readonly int TransformState = Animator.StringToHash("Base Layer.Transform");
    private static readonly int RushState = Animator.StringToHash("Base Layer.Rush");
    private static readonly int GroundSlamState = Animator.StringToHash("Base Layer.GroundSlam");
    private static readonly int LocomotionState = Animator.StringToHash("Base Layer.Locomotion");
    private const float SlamImpact = 0.92f;  // seconds into GroundSlam when Hofund hits the floor
    private const float RunStride = 1.34f;   // ground speed of the run clip, m/s at prefab scale
    private const float CutsceneHeight = 3.2f;

    [Header("Parts")]
    [SerializeField] private Transform lion;          // Gulltoppr's model (no rig: moved by code)
    [SerializeField] private Transform rider;         // Heimdall's model
    [SerializeField] private Animator animator;
    [SerializeField] private Renderer swordRenderer;

    [Header("Health")]
    [SerializeField] private int lionHealth = 60;
    [SerializeField] private int heimdallHealth = 130;   // per phase on foot: phases 2 and 3 each start with a full bar

    [Header("Combat")]
    [SerializeField] private float damage = 16f;
    [SerializeField] private float mountedSpeed = 2.6f;
    [SerializeField] private float footSpeed = 2.1f;
    [SerializeField] private float arenaRadius = 15f;
    [SerializeField] private float bodyScale = 1.35f;   // how much bigger than the prefab he stands in the arena
    // Every windup, warning and pause between patterns takes this fraction of its designed length (lower = faster casting)
    [SerializeField, Range(0.4f, 1f)] private float castTime = 0.7f;

    [Header("Heimsight (천리안)")]
    [SerializeField] private float blinkGap = 0.32f;          // seconds between one teleport slash and the next
    [SerializeField] private float sightFirstWindup = 2.2f;   // time the path shows before he runs it (while slowed)
    [SerializeField] private float sightNextWindup = 1.6f;
    [SerializeField] private float sightSlow = 0.5f;          // the player's pace under his gaze

    [Header("Pillars of light (phases 2 and 3)")]
    [SerializeField] private float pillarRadius = 2f;
    [SerializeField] private float pillarWarn = 0.95f;        // phase 3 marks them a little later

    [Header("Last stand: the Bifrost charge")]
    [SerializeField, Range(0f, 1f)] private float lastStandAt = 0.1f;    // phase 3's bar (of full) at which he begins it, once
    [SerializeField] private float ultimateChargeTime = 6f;
    [SerializeField, Range(0f, 1f)] private float breakFraction = 0.05f; // damage (of max health) that breaks the charge

    private CharacterController controller;
    private Transform player;
    private PlayerStats playerStats;
    private int lionMax, heimdallMax;
    private int phase = 1;
    private bool busy;
    private bool dead;
    private float invulnerableUntil;
    private Coroutine action;
    private GameObject warning;
    private readonly List<GameObject> warnings = new List<GameObject>();
    private float verticalVelocity;
    private Vector3 actionMove;
    private float gallop;
    private float lionMoving;
    private LionRig lionRig;
    private bool lionDown;      // Gulltoppr is falling: its own topple drives it, not the gallop
    private bool dashing;       // Heimsight's dash chain: placed directly, the controller off

    // No plain attacks: Heimdall works through a fixed cycle of patterns, with a short breather between them
    private enum Pattern { LionCharge, FireBreath, Rampage, Horn, Sight, Rush, Rings, Pillars, EdgeBeams,
        Brand, Chains, Cross, Swords, Orb, Roar, Ragnarok }
    private static readonly Pattern[] MountedCycle = { Pattern.LionCharge, Pattern.FireBreath, Pattern.Rampage, Pattern.FireBreath };
    // On foot (the user's design notes, 헤임달 스킬패턴): the old patterns woven together with the new ones
    private static readonly Pattern[] FootCycle = { Pattern.Horn, Pattern.Brand, Pattern.Rush, Pattern.Cross, Pattern.Pillars, Pattern.Chains,
        Pattern.EdgeBeams, Pattern.Sight, Pattern.Orb, Pattern.Rings, Pattern.Swords, Pattern.Roar };
    // The last phase: all of them, faster, and the final horn (라그나로크) once a round
    private static readonly Pattern[] FinalCycle = { Pattern.Roar, Pattern.Chains, Pattern.Cross, Pattern.Swords, Pattern.Sight, Pattern.Brand,
        Pattern.Ragnarok, Pattern.Rush, Pattern.Orb, Pattern.EdgeBeams, Pattern.Pillars, Pattern.Rings, Pattern.Horn };
    private int triMode;

    // The warning sound each pattern gives before it starts (VARCO: hmw_*), so the ear hears what's coming
    private void Omen(Pattern p)
    {
        string key = p switch
        {
            Pattern.LionCharge => "hmw_lion",
            Pattern.Rampage => "hmw_lion",
            Pattern.FireBreath => "hmw_breath",
            Pattern.Horn => "hmw_roar",
            Pattern.Roar => "hmw_roar",
            Pattern.Sight => "hmw_sight",
            Pattern.Rush => "hmw_rush",
            Pattern.Rings => "hmw_rings",
            Pattern.Pillars => "hmw_pillar",
            Pattern.EdgeBeams => "hmw_tri",
            Pattern.Brand => "hmw_brand",
            Pattern.Chains => "hmw_chains",
            Pattern.Cross => "hmw_cross",
            Pattern.Swords => "hmw_swords",
            Pattern.Orb => "hmw_orb",
            Pattern.Ragnarok => "hmw_ragna",
            _ => null
        };
        GameAudio.Play(key, transform.position, 1f);
        GameAudio.Play("boss_warn", transform.position, 0.55f);
    }
    private int patternIndex;
    private float restTimer = 1.5f, approachTime;
    private bool lastStandUsed;
    private float chargeDamage;
    private Light aura;
    private Transform auraRing;

    public event Action<string, string> PhaseChanged; // title, subtitle
    public event Action<string> Warned;

    public int Phase => phase;
    public bool UltimateCharging { get; private set; }
    public float BreakProgress => UltimateCharging ? Mathf.Clamp01(chargeDamage / (heimdallMax * breakFraction)) : 0f;

    public override int Health => phase == 1 ? Mathf.Max(1, lionHealth) : Mathf.Max(0, heimdallHealth);
    public override int MaxHealth => phase == 1 ? lionMax : heimdallMax;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (lion != null)
            lionRig = lion.GetComponent<LionRig>();
        lionMax = lionHealth;
        heimdallMax = heimdallHealth;
        IsBoss = true;
        DisplayName = "굴툽프 · 헤임달 탑승";
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null)
        {
            player = movement.transform;
            playerStats = movement.GetComponent<PlayerStats>();
        }
        if (animator != null)
        {
            animator.Play("Mounted");
            animator.SetLayerWeight(1, 1f);
        }
        // The spawner names enemies after their realm entry; the boss keeps its own phase names
        DisplayName = "굴툽프 · 헤임달 탑승";
        GoldDrop = 60;
        // After the spawner has set the scale
        transform.localScale *= bodyScale;
    }

    private float S => transform.localScale.x;   // body size, for offsets measured at prefab scale
    // Played the moment he is spawned, before Start has grown him
    public float IntroHeight => CutsceneHeight * bodyScale * S;

    // The boss-room entrance: he waits astride Gulltoppr while the camera takes him in and his name comes up
    // First the conversation 「신들의 문지기」 (HeimdallDialogue, a visual novel over the arena); it ends as he raises
    // Gjallarhorn, and its blast opens the fight: a flash, the shockwave, his name and his bar
    public IEnumerator IntroScene()
    {
        busy = true;
        yield return new WaitForSeconds(0.7f);
        if (animator != null)
            yield return HeimdallDialogue.Run(animator.gameObject, ArenaCenter);
        var gold = new Color(1f, 0.85f, 0.45f);
        if (animator != null)
            animator.SetTrigger(HornHash);
        yield return new WaitForSeconds(0.35f);
        BossCutscene.Flash(0.9f);
        Vfx.GroundRing(transform.position, 6f * S, gold, 0.9f, "bifrostcircle");
        Shockwave.Spawn(transform.position, 8f * S, gold, 24);
        EnemyFeel.Roar(transform, gold, 7f * S, 0.38f);
        BossCutscene.Title("HEIMDALL", "아스가르드의 수호자", gold);
        BossCutscene.Dolly(0.85f);
        yield return new WaitForSeconds(1.6f);
        restTimer = 1.2f;
        busy = false;
    }

    public override void Scale(float healthMultiplier, float damageMultiplier)
    {
        lionMax = lionHealth = Mathf.Max(1, Mathf.RoundToInt(lionHealth * healthMultiplier));
        heimdallMax = heimdallHealth = Mathf.Max(1, Mathf.RoundToInt(heimdallHealth * healthMultiplier));
        damage *= damageMultiplier;
    }

    private Vector3 ArenaCenter => HomeRoom != null ? HomeRoom.transform.position : transform.position;

    private void Update()
    {
        if (dead)
            return;

        float dt = Time.deltaTime;
        if (!busy) restTimer -= dt;

        Vector3 move = Vector3.zero;
        if (!busy && player != null && GameManager.IsRunActive)
        {
            Vector3 to = Flat(player.position - transform.position);
            float distance = to.magnitude;
            if (distance > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), (phase == 1 ? 160f : 360f) * dt);
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

        if (phase == 1 && !lionDown)
            AnimateLion(move.magnitude);
        else if (animator != null)
        {
            // Ground speed per unit of size picks the gait; beyond the run clip's pace it plays faster
            float pace = Flat(move).magnitude / Mathf.Max(0.01f, transform.localScale.x);
            animator.SetFloat(SpeedHash, pace, 0.1f, dt);
            animator.SetFloat(LocoRateHash, Mathf.Clamp(pace / RunStride, 1f, 2.2f), 0.1f, dt);
        }
        if (phase == 3)
            AnimateAura();
    }

    // ───────────── Pattern cycle ─────────────

    private Vector3 Think(Vector3 to, float distance)
    {
        // Brought this low during a cutscene (when a hit can't start it): begin it now
        if (LastStandDue)
        {
            Act(UltimateCharge());
            return Vector3.zero;
        }
        if (restTimer > 0f)
            return Reposition(to, distance);

        Pattern[] cycle = phase == 1 ? MountedCycle : phase == 2 ? FootCycle : FinalCycle;
        Pattern next = cycle[patternIndex % cycle.Length];
        // Close in first (the fire breath needs to be near, and Heimsight should start on screen). Gulltoppr bounds in
        // at a full sprint (going ahead anyway after a couple of seconds); on foot he blinks straight there.
        float range = next == Pattern.FireBreath ? 9.5f : next == Pattern.Sight ? 9f : float.MaxValue;
        if (phase == 1 && distance > range && (approachTime += Time.deltaTime) < 2f)
            return to / distance * mountedSpeed * 3.5f;
        approachTime = 0f;
        patternIndex++;

        IEnumerator routine = null;
        switch (next)
        {
            case Pattern.LionCharge: routine = LionCharge(to / distance); break;
            case Pattern.FireBreath: if (distance <= 10f) routine = FireBreath(); break;
            case Pattern.Rampage: routine = LionRampage(); break;
            case Pattern.Horn: routine = Gjallarhorn(true); break;
            case Pattern.Sight: routine = Foresight(); break;
            case Pattern.Rush: routine = BifrostRush(); break;
            case Pattern.Rings: routine = RingsOfJudgment(); break;
            case Pattern.Pillars: routine = LightPillars(); break;
            case Pattern.EdgeBeams: routine = EdgeBeams(); break;
            case Pattern.Brand: routine = CelestialBrand(); break;
            case Pattern.Chains: routine = ChainsOfJudgment(); break;
            case Pattern.Cross: routine = CelestialCross(); break;
            case Pattern.Swords: routine = GoldenSwordFormation(); break;
            case Pattern.Orb: routine = SplittingLight(); break;
            case Pattern.Roar: routine = RoarOfJudgment(); break;
            case Pattern.Ragnarok: routine = Ragnarok(); break;
        }
        if (routine == null)
            return Vector3.zero;
        Omen(next);
        if (phase > 1 && distance > range)
            routine = Sequence(BlinkTo(player.position - to / distance * (range - 2.5f)), routine);
        // On foot he chains a quick blow onto the end of a pattern: now and then in phase 2, every time in phase 3
        // (Heimsight and the pillars already end in a slam of their own)
        bool endsInSlam = next == Pattern.Sight || next == Pattern.Pillars || next == Pattern.Chains || next == Pattern.Ragnarok
            || next == Pattern.Roar || next == Pattern.Brand;
        if (phase > 1 && !endsInSlam && (phase == 3 || Random.value < 0.5f))
            routine = Sequence(routine, FollowUp());
        Act(routine);
        return Vector3.zero;
    }

    // The quick blow after a pattern: Hofund slammed down right where he stands if the player is close, otherwise
    // a wave of pillars opened round the player
    private IEnumerator FollowUp()
    {
        if (dead || player == null)
            yield break;
        var gold = HeimdallFx.Gold;
        Vector3 at = Flat(player.position);
        if (Flat(player.position - transform.position).magnitude < 7.5f)
        {
            yield return HofundSlam(gold);
            yield break;
        }
        var spots = new List<Vector3> { at };
        float turn = Random.Range(0f, 360f);
        for (int i = 0; i < 4; i++)
            spots.Add(InArena(at + Quaternion.Euler(0f, turn + i * 90f, 0f) * Vector3.forward * 3.2f, 1f));
        if (animator != null)
            animator.SetTrigger(CastHash);
        float warn = Cast(pillarWarn);
        yield return PillarWave(spots, warn, gold);
        yield return new WaitForSeconds(Cast(0.3f));
    }

    // 호프눈드 강타: the sword brought down on the floor at his feet, the ground round him bursting open
    private IEnumerator HofundSlam(Color gold)
    {
        GameAudio.Play("heim_slam", transform.position, 1.0f);   // (this move's own sound)
        float radius = phase == 3 ? 6.5f : 5.5f;
        FacePlayer();
        Vector3 at = transform.position;
        warnings.Add(Telegraph.Circle(at, radius, SlamImpact));
        warnings.Add(HeimdallFx.Circle(at, radius * 2.2f, gold * 0.9f, 140f).gameObject);
        PlayState(GroundSlamState, 0.1f);
        Vfx.Gather(transform.position + Vector3.up * 2f * S, 2.5f * S, gold, SlamImpact, 70f);
        yield return new WaitForSeconds(SlamImpact);
        ClearWarning();
        EruptBand(at, 0f, radius, gold);
        HeimdallFx.Impact(at + Vector3.up * 0.8f, radius * 1.2f, gold);
        Shockwave.Spawn(at, radius, gold, 24);
        CameraShake.Shake(0.35f, 0.3f);
        Vector3 away = Flat(player.position - at);
        if (playerStats != null && away.magnitude <= radius + 0.3f)
        {
            playerStats.TakeDamage(damage * 1.2f, this);
            player.GetComponent<PlayerMovement>()?.Push((away.sqrMagnitude > 0.01f ? away.normalized : transform.forward) * 8f);
        }
        yield return new WaitForSeconds(Cast(0.4f));
    }

    // Between patterns: keep at a middling range, circling the player rather than closing in
    private Vector3 Reposition(Vector3 to, float distance)
    {
        float speed = phase == 1 ? mountedSpeed : footSpeed;
        Vector3 dir = to / distance;
        if (distance > 7f)
            return dir * speed;
        if (distance < 3.5f)
            return -dir * speed * 0.8f;
        return Vector3.Cross(Vector3.up, dir) * speed * 0.5f;
    }

    // ───────────── Phase 1: mounted on Gulltoppr ─────────────

    private IEnumerator LionCharge(Vector3 direction)
    {
        const float length = 17f;
        transform.rotation = Quaternion.LookRotation(direction);
        warning = Telegraph.Line(transform.position, direction, length, 3.2f * S, Cast(0.75f));
        yield return new WaitForSeconds(Cast(0.75f));
        ClearWarning();
        Vector3 start = transform.position;
        yield return Gallop(direction, length);
        FlameLane.Spawn(start, transform.position, 2.2f * S, 4f, damage * 0.3f, this, playerStats);
        yield return new WaitForSeconds(Cast(0.4f));
    }

    // Gulltoppr's charge itself: a straight run that tramples the player once if it catches them
    private IEnumerator Gallop(Vector3 direction, float length)
    {
        GameAudio.Play("lion_gallop", transform.position, 1.0f);   // (this move's own sound)
        const float time = 0.65f;
        bool landed = false;
        actionMove = direction * (length / time);
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            if (!landed && FlatDistance(player.position, transform.position) <= 2f * S)
            {
                landed = true;
                playerStats.TakeDamage(damage * 1.3f, this);
                player.GetComponent<PlayerMovement>()?.Push(direction * 10f);
            }
            yield return null;
        }
        actionMove = Vector3.zero;
    }

    // Gulltoppr's rampage: three charges in a row, each turned on the player with less warning than the last, and
    // every one leaves a lane of fire burning behind it. The lanes outlast the rampage, so the floor left to fight on
    // shrinks with each charge.
    private IEnumerator LionRampage()
    {
        GameAudio.Play("lion_atk", transform.position, 1.0f);   // (this move's own sound)
        Warned?.Invoke("굴툽프의 연속 돌진! 지나간 자리에 불길이 남습니다");
        var fire = new Color(1f, 0.6f, 0.25f);
        for (int c = 0; c < 4 && !dead; c++)
        {
            Vector3 to = Flat(player.position - transform.position);
            Vector3 direction = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
            float length = Mathf.Clamp(to.magnitude + 6f, 10f, 20f);
            float windup = Cast(c == 0 ? 0.7f : 0.4f);
            transform.rotation = Quaternion.LookRotation(direction);
            warning = Telegraph.Line(transform.position, direction, length, 3.2f * S, windup);
            Vfx.Gather(transform.position + Vector3.up * 1.4f * S, 2f * S, fire, windup, 40f);
            yield return new WaitForSeconds(windup);
            ClearWarning();
            Vector3 start = transform.position;
            Vfx.Flash(start + Vector3.up * S, 3f * S, fire);
            yield return Gallop(direction, length);
            FlameLane.Spawn(start, transform.position, 2.2f * S, 6f, damage * 0.3f, this, playerStats);
            CameraShake.Shake(0.2f, 0.15f);
            yield return new WaitForSeconds(0.1f);
        }
        yield return new WaitForSeconds(Cast(0.4f));
    }

    private IEnumerator FireBreath()
    {
        GameAudio.Play("lion_fire", transform.position, 1.0f);   // (this move's own sound)
        const float reach = 10f, halfAngle = 42f;
        FacePlayer();
        // A fan of warning lines marks the cone
        for (int i = -3; i <= 3; i++)
            warnings.Add(Telegraph.Line(transform.position, Quaternion.Euler(0f, i * halfAngle / 3f, 0f) * transform.forward, reach, 0.6f, Cast(0.65f)));
        yield return new WaitForSeconds(Cast(0.65f));
        ClearWarning();

        // A roaring cone of fire and embers (ended with the action if he's interrupted)
        Vector3 mouth = transform.position + (transform.forward * 2.2f + Vector3.up * 1.6f) * S;
        Vector3 down = Quaternion.AngleAxis(12f, transform.right) * transform.forward;
        warnings.Add(Vfx.Fire(mouth, down, reach, halfAngle * 0.8f, 1.3f).gameObject);
        Vfx.Flash(mouth, 2f, new Color(1f, 0.6f, 0.2f));
        float tick = 0f;
        for (float t = 0f; t < 1.4f; t += Time.deltaTime)
        {
            tick -= Time.deltaTime;
            if (tick <= 0f && FlatDistance(player.position, transform.position) <= reach + 0.3f
                && Vector3.Angle(transform.forward, Flat(player.position - transform.position)) <= halfAngle)
            {
                tick = 0.25f;
                playerStats.TakeDamage(damage * 0.35f, this);
            }
            yield return null;
        }
        yield return new WaitForSeconds(Cast(0.35f));
    }

    // The lion gallops on its rig (legs, back, head, tail) while the whole body rises and dips with each bound
    private void AnimateLion(float speed)
    {
        if (lion == null)
            return;
        gallop += Time.deltaTime * Mathf.Lerp(2f, 9f, Mathf.Clamp01(speed / 8f));
        // Eased so the legs don't snap between standing and running
        lionMoving = Mathf.MoveTowards(lionMoving, Mathf.Clamp01(speed / mountedSpeed), Time.deltaTime * 3f);
        float bob = Mathf.Abs(Mathf.Sin(gallop)) * 0.18f * lionMoving + Mathf.Sin(Time.time * 1.5f) * 0.03f;
        float pitch = Mathf.Sin(gallop * 2f) * 3f * lionMoving;
        lion.localPosition = new Vector3(0f, bob, 0f);
        lion.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        if (lionRig != null)
            lionRig.Pose(gallop / Mathf.PI, lionMoving);   // one stride per bound
    }

    // Gulltoppr falls; Heimdall leaps down, lands, and raises Gjallarhorn — played as a cutscene
    private void BeginDismount()
    {
        StopAction();
        busy = true;
        invulnerableUntil = float.MaxValue;
        BossCutscene.Play(transform, CutsceneHeight * S, Dismount());
    }

    private IEnumerator Dismount()
    {
        GameAudio.Play("heim_dismount", transform.position, 1.0f);   // (this move's own sound)
        busy = true;
        lionDown = true;
        if (animator != null)
            animator.SetLayerWeight(1, 0f);

        // The lion topples onto its side
        Quaternion start = lion.localRotation;
        Vector3 riderStart = rider.position;
        rider.SetParent(transform, true);
        Vector3 landing = transform.position + transform.right * 2f * S;
        if (animator != null)
            animator.SetTrigger(LandHash);
        for (float t = 0f; t < 0.7f; t += Time.deltaTime)
        {
            float k = t / 0.7f;
            lion.localRotation = Quaternion.Slerp(start, Quaternion.Euler(0f, 0f, 80f), k);
            rider.position = Vector3.Lerp(riderStart, landing, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 1.5f * S;
            yield return null;
        }
        CameraShake.Shake(0.3f, 0.25f);
        rider.position = landing;

        // Move the body (and its collider) to where Heimdall stands
        controller.enabled = false;
        transform.position = new Vector3(landing.x, transform.position.y, landing.z);
        rider.localPosition = Vector3.zero;
        controller.radius = 0.55f;
        controller.height = 2.1f;
        controller.center = new Vector3(0f, 1.05f, 0f);
        controller.enabled = true;
        lion.SetParent(null, true);
        StartCoroutine(FadeOut(lion.gameObject, 1.5f));

        phase = 2;
        DisplayName = "헤임달";
        patternIndex = 0;
        Vfx.Dust(transform.position, 2.2f, 18);
        Vfx.GroundRing(transform.position, 3f, new Color(1f, 0.85f, 0.45f), 0.5f);

        // On his feet, he raises the horn of the gods: a blast of sound rolls out (it hurts no one here)
        yield return new WaitForSeconds(0.6f);
        FacePlayer();
        var gold = new Color(1f, 0.85f, 0.45f);
        if (animator != null)
            animator.SetTrigger(HornHash);
        Vfx.Gather(transform.position + Vector3.up * 2.4f * S, 3.5f, gold, 0.9f, 80f);
        yield return new WaitForSeconds(1f);
        BossCutscene.Title("수호신 헤임달", "굴툽프가 쓰러지고, 비프로스트의 파수꾼이 걀라르호른을 든다", gold);
        BossCutscene.Flash(0.4f);
        BossCutscene.Dolly(0.85f);
        SonicRing.Spawn(transform.position, arenaRadius + 2f, 1.4f, new List<float>(), 0f, this, 0f, null);
        Shockwave.Spawn(transform.position, 5f, gold, 26);
        EnemyFeel.Roar(transform, gold, 7f * S, 0.38f);
        yield return new WaitForSeconds(1.4f);

        restTimer = 1f;
        invulnerableUntil = 0f;
        busy = false;
    }

    private static IEnumerator FadeOut(GameObject go, float seconds)
    {
        Vector3 size = go.transform.localScale;
        yield return new WaitForSeconds(1f);
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            go.transform.localScale = size * (1f - t / seconds);
            yield return null;
        }
        Destroy(go);
    }

    // ───────────── Phases 2 and 3: on foot ─────────────

    // Gjallarhorn: a sonic ring rolls out across the arena. Its gaps are the only safe places.
    // A second ring follows with its gaps turned half way between the first's, so the player has to slip
    // across from one gap to the next once the first ring has passed.
    private IEnumerator Gjallarhorn(bool echo)
    {
        GameAudio.Play("heim_horn", transform.position, 1.0f);   // (this move's own sound)
        if (animator != null)
            animator.SetTrigger(HornHash);
        Warned?.Invoke("걀라르호른! 음파의 틈으로 피하세요");

        float gapStart = Random.Range(0f, 90f);
        var gaps = new List<float>();
        for (int i = 0; i < 4; i++)
            gaps.Add(gapStart + i * 90f);
        const float gapHalfWidth = 12f;
        // The safe lanes glow gold on the ground; a rune circle of sound opens under him as he draws breath
        var sonic = new Color(0.75f, 0.92f, 1f);
        foreach (float g in gaps)
            warnings.Add(Telegraph.SafeLane(transform.position, Quaternion.Euler(0f, g, 0f) * Vector3.forward, arenaRadius, 2.2f, Cast(0.95f)));
        warnings.Add(HeimdallFx.Circle(transform.position, 6f * S, sonic * 0.9f, 120f).gameObject);
        Vfx.Gather(transform.position + Vector3.up * 2f * S, 3f, sonic, Cast(0.95f), 50f);
        yield return new WaitForSeconds(Cast(0.95f));
        ClearWarning();

        SonicRing.Spawn(transform.position, arenaRadius + 2f, 1.4f, gaps, gapHalfWidth, this, damage * 1.2f, playerStats);
        // The blast: a burst at the horn and rings of sound rolling out one behind another
        HeimdallFx.Impact(transform.position + Vector3.up * 2f * S, 4f * S, sonic);
        StartCoroutine(HornEchoes(transform.position, sonic));
        EnemyFeel.Roar(transform, sonic, 6f * S, 0.3f);
        if (echo && !dead)
        {
            float echoDelay = Cast(0.85f);
            var turned = new List<float>();
            foreach (float g in gaps)
                turned.Add(g + 45f);
            foreach (float g in turned)
                warnings.Add(Telegraph.SafeLane(transform.position, Quaternion.Euler(0f, g, 0f) * Vector3.forward, arenaRadius, 2.2f, echoDelay));
            if (animator != null)
                animator.SetTrigger(HornHash);
            yield return new WaitForSeconds(echoDelay);
            ClearWarning();
            SonicRing.Spawn(transform.position, arenaRadius + 2f, 1.4f, turned, gapHalfWidth, this, damage * 1.2f, playerStats);
            HeimdallFx.Impact(transform.position + Vector3.up * 2f * S, 4f * S, sonic);
            StartCoroutine(HornEchoes(transform.position, sonic));
            EnemyFeel.Roar(transform, sonic, 6f * S, 0.25f);
        }
        yield return new WaitForSeconds(Cast(0.8f));
    }

    private IEnumerator HornEchoes(Vector3 at, Color color)
    {
        for (int i = 0; i < 4; i++)
        {
            HeimdallFx.ShockRing(at, 4f + i * 3.5f, color * (1f - i * 0.18f), 0.6f + i * 0.1f);
            yield return new WaitForSeconds(0.12f);
        }
        HeimdallFx.CircleFlash(at, 9f, color, 0.9f);
    }

    // ── Bifrost rush (무지개 질주): he dashes through the player in a whirling spin again and again, re-aimed each time
    // with less warning, and every dash leaves a dark scar on the floor. When the last dash is done the scars all
    // fill up and erupt at once, so the arena ends up crossed by every lane he ran: sidestep each dash, then find
    // ground none of them crosses before they go off. ──

    private IEnumerator BifrostRush()
    {
        GameAudio.Play("heim_bifrost", transform.position, 1.0f);   // (this move's own sound)
        Warned?.Invoke("무지개 질주! 돌진을 옆으로 피하고, 지나간 자리가 터지기 전에 빠져나오세요");
        int dashes = phase == 3 ? 5 : 4;
        float width = 2.6f * S;
        float y = transform.position.y;
        var lanes = new List<(Vector3 a, Vector3 b)>();
        for (int d = 0; d < dashes && !dead; d++)
        {
            FacePlayer();
            Vector3 from = Flat(transform.position);
            // Runs on past the player, so stepping back along the line doesn't save them
            Vector3 to = InArena(Flat(player.position) + transform.forward * 4f, 1.5f);
            if (Flat(to - from).magnitude < 4f)
                to = InArena(from + transform.forward * 7f, 1.5f);
            Vector3 leg = to - from;
            float windup = Cast(d == 0 ? 0.65f : phase == 3 ? 0.34f : 0.4f);
            Color tint = RainbowAt(d);
            GameObject line = Telegraph.Line(from, leg, leg.magnitude, width, windup);
            warnings.Add(line);
            Vfx.Gather(transform.position + Vector3.up * S, 1.6f * S, tint, windup, 50f);
            yield return new WaitForSeconds(windup);
            warnings.Remove(line);
            Destroy(line);

            // The dash: a whirling spin straight down the line, placed directly with the controller off
            PlayState(RushState, 0.05f);
            Blink(transform.position, transform.position, tint);
            var streak = new GameObject("RushStreak");
            streak.transform.SetParent(transform, false);
            streak.transform.localPosition = Vector3.up;
            Vfx.Trail(streak, tint, 1.4f, 0.35f);
            controller.enabled = false;
            dashing = true;
            bool hit = false;
            float time = Mathf.Max(0.25f, leg.magnitude / 32f);
            for (float t = 0f; t < time && !dead; t += Time.deltaTime)
            {
                Vector3 p = Vector3.Lerp(from, to, t / time);
                transform.position = new Vector3(p.x, y, p.z);
                if (!hit && playerStats != null && DistanceToSegment(Flat(player.position), from, p) <= width / 2f + 0.3f)
                {
                    hit = true;
                    playerStats.TakeDamage(damage * 1.1f, this);
                    Vector3 across = Vector3.Cross(Vector3.up, leg.normalized);
                    float side = Mathf.Sign(Vector3.Dot(Flat(player.position) - p, across));
                    player.GetComponent<PlayerMovement>()?.Push(across * side * 9f);
                }
                yield return null;
            }
            transform.position = new Vector3(to.x, y, to.z);
            controller.enabled = true;
            dashing = false;
            Destroy(streak, 0.4f);
            PlayState(LocomotionState, 0.12f);
            Vfx.Dust(transform.position, 1.5f * S, 10);
            CameraShake.Shake(0.15f, 0.15f);
            lanes.Add((from, to));
            warnings.Add(LaneScar(from, to, width));
            yield return new WaitForSeconds(0.08f);
        }
        if (dead)
            yield break;

        // The scars fill, then erupt together
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(CastHash);
        float fuse = Cast(phase == 3 ? 0.55f : 0.65f);
        foreach (var (a, b) in lanes)
            warnings.Add(Telegraph.Line(a, b - a, Flat(b - a).magnitude, width, fuse));
        yield return new WaitForSeconds(fuse);
        ClearWarning();
        bool burned = false;
        foreach (var (a, b) in lanes)
        {
            float length = Flat(b - a).magnitude;
            int geysers = Mathf.Max(2, Mathf.CeilToInt(length / 1.8f));
            for (int i = 0; i <= geysers; i++)
            {
                Vector3 at = Vector3.Lerp(a, b, i / (float)geysers);
                float size = width * Random.Range(1.5f, 1.9f);
                // The geyser rises from the bottom of its frame: stand it on the floor
                Vfx.Sprite("hd_rush", at + Vector3.up * size * 0.45f, size, Color.white * 1.15f, 0.75f, 1f, 1.05f, 0f).Sheet(4, 4);
            }
            HeimdallFx.ShockRing(a, width, HeimdallFx.Gold, 0.4f);
            HeimdallFx.ShockRing(b, width, HeimdallFx.Gold, 0.4f);
            if (!burned && DistanceToSegment(Flat(player.position), Flat(a), Flat(b)) <= width / 2f + 0.3f)
                burned = true;
        }
        if (burned && playerStats != null)
            playerStats.TakeDamage(damage * 1.2f, this);
        CameraShake.Shake(0.4f, 0.3f);
        yield return new WaitForSeconds(Cast(0.4f));
    }

    // The scar a dash leaves until it erupts: the plain dark strip of a ground warning (not yet filling)
    private static GameObject LaneScar(Vector3 from, Vector3 to, float width)
    {
        Vector3 dir = Flat(to - from);
        var scar = new GameObject("LaneScar");
        scar.transform.position = new Vector3(from.x, 0.025f, from.z);
        scar.transform.rotation = Quaternion.LookRotation(dir.normalized);
        Mats.Shape(PrimitiveType.Cube, scar.transform, new Vector3(0f, 0f, dir.magnitude / 2f), new Vector3(width, 0.01f, dir.magnitude),
            Mats.Get("fx_warn_ring", new Color(0.25f, 0.05f, 0.05f)));
        scar.AddComponent<GrowIn>().Init(0.25f);
        return scar;
    }

    // The Bifrost's six colours, red to violet (the dashes' glow)
    private static Color RainbowAt(int i)
    {
        Color[] bands = { new Color(1f, 0.25f, 0.25f), new Color(1f, 0.6f, 0.2f), new Color(1f, 0.95f, 0.3f), new Color(0.3f, 1f, 0.45f), new Color(0.3f, 0.6f, 1f), new Color(0.7f, 0.35f, 1f) };
        return bands[i % bands.Length];
    }

    // A point kept on the arena floor, this far in from its edge
    private Vector3 InArena(Vector3 p, float margin)
    {
        Vector3 center = Flat(ArenaCenter);
        Vector3 off = Flat(p) - center;
        float limit = arenaRadius - margin;
        if (off.magnitude > limit)
            off = off.normalized * limit;
        return center + off;
    }

    // ── Rings of judgment (심판의 고리): from the middle of the arena he drives Hofund into the floor and the ground
    // around him erupts in bands, the inner disc and the outer ring together, then the ring between them, back and
    // forth with less warning each time. The next band is only marked once the last has gone off, so the player has
    // to keep stepping in toward him and back out again; the inner turns are the moments to hit him. ──

    private IEnumerator RingsOfJudgment()
    {
        GameAudio.Play("heim_rings", transform.position, 1.0f);   // (this move's own sound)
        Warned?.Invoke("심판의 고리! 안쪽과 바깥쪽이 번갈아 터집니다 — 표시되지 않은 고리로 옮기세요");
        var gold = HeimdallFx.Gold;
        yield return BlinkTo(ArenaCenter);
        FacePlayer();
        PlayState(GroundSlamState, 0.2f);
        Vfx.Gather(transform.position + Vector3.up * 2f * S, 3f * S, gold, SlamImpact, 70f);
        yield return new WaitForSeconds(SlamImpact);

        Vector3 center = transform.position;
        int steps = phase == 3 ? 6 : 5;
        float warn = Cast(phase == 3 ? 0.75f : 0.85f);
        // Hofund stays planted in the floor until the last band has gone off
        HeimdallFx.Sword(center + transform.forward * 1.1f * S, 1.3f, 0.12f, 1.2f + steps * (warn + 0.1f), gold);
        Shockwave.Spawn(center, 3f * S, gold, 20);
        CameraShake.Shake(0.35f, 0.3f);

        const float inner = 5f, middle = 10f;
        float outer = arenaRadius + 2f;
        for (int s = 0; s < steps && !dead; s++)
        {
            var bands = s % 2 == 0 ? new[] { (0f, inner), (middle, outer) } : new[] { (inner, middle) };
            float w = s == 0 ? Cast(1.1f) : warn;
            foreach (var (a, b) in bands)
                warnings.Add(BandTelegraph.Spawn(center, a, b, w));
            warnings.Add(HeimdallFx.Circle(center, inner * 2.2f, gold * 0.7f, s % 2 == 0 ? 120f : -120f).gameObject);
            yield return new WaitForSeconds(w);
            ClearWarning();

            float r = Flat(player.position - center).magnitude;
            bool hit = false;
            foreach (var (a, b) in bands)
            {
                EruptBand(center, a, b, gold);
                hit |= r >= a - 0.3f && r <= b + 0.3f;
            }
            if (hit && playerStats != null)
            {
                playerStats.TakeDamage(damage * 1.2f, this);
                Vector3 away = Flat(player.position - center);
                player.GetComponent<PlayerMovement>()?.Push((away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 6f);
            }
            CameraShake.Shake(0.3f, 0.25f);
            warn = Mathf.Max(Cast(0.5f), warn - Cast(0.1f));
            yield return new WaitForSeconds(0.1f);
        }
        yield return new WaitForSeconds(Cast(0.4f));
    }

    // A band of ground bursting open: VARCO's holy eruption laid round the band, golden geysers of light rising from it
    private static void EruptBand(Vector3 center, float a, float b, Color gold)
    {
        center = new Vector3(center.x, 0f, center.z);
        float width = b - a, mid = (a + b) / 2f;
        if (a <= 0.01f)
        {
            // The disc: one great burst over all of it, the ring of smaller ones round its edge
            Vfx.Sprite("hd_burst", center + Vector3.up * 0.1f, b * 2.3f, Color.white * 1.5f, 0.8f, 0.9f, 1.05f, 30f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Sheet(4, 4);
            mid = b * 0.65f;
            width = b * 0.7f;
        }
        int count = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * mid / (width * 0.75f)), 5, 18);
        float turn = Random.Range(0f, 360f);
        for (int i = 0; i < count; i++)
        {
            Vector3 at = center + Quaternion.Euler(0f, turn + i * 360f / count, 0f) * Vector3.forward * mid;
            Vfx.Sprite("hd_burst", at + Vector3.up * 0.1f, width * 1.6f, Color.white * 1.4f, 0.8f, 0.85f, 1.05f, 30f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Sheet(4, 4);
            if (i % 2 == 0)
            {
                float size = Mathf.Min(width, 5f) * 1.3f;
                Vfx.Sprite("hd_rush", at + Vector3.up * size * 0.45f, size, gold * 1.8f, 0.7f, 1f, 1.05f, 0f).Sheet(4, 4);
            }
        }
        Vfx.GroundRing(center, b, gold, 0.5f);
    }

    private void PlayState(int state, float fade)
    {
        if (animator != null && animator.HasState(0, state))
            animator.CrossFadeInFixedTime(state, fade);
    }

    // Heimsight: the god who sees across the Nine Realms foresees where he will strike. Time drags for the player
    // while a star is drawn across the arena: lines from one point on the rim to another, each filling up just as he
    // takes it. Then he teleports along them one after another, cutting the whole line with Hofund each time.
    // Get to the star's heart or between its points before the slow wears off — then out of the heart again, because
    // he ends by leaping onto it (HeartStrike).
    private IEnumerator Heimsight()
    {
        GameAudio.Play("heim_sight", transform.position, 1.0f);   // (this move's own sound)
        if (animator != null)
            animator.SetTrigger(CastHash);
        Warned?.Invoke("천리안! 시간이 느려집니다 — 별을 그리는 순간이동 참격을 피해 별의 한가운데나 꼭짓점 사이로 가세요");
        var gold = new Color(1f, 0.85f, 0.45f);
        Vfx.Gather(transform.position + Vector3.up * 2.4f * S, 3f, gold, 0.8f, 60f);
        yield return new WaitForSeconds(Cast(0.4f));

        Vector3 center = ArenaCenter;
        int chains = phase == 3 ? 2 : 1;
        // A five-pointed star with its top point at north; in phase 3 an eight-pointed one with points at north, east,
        // south and west and between them, then the same star again turned half a point
        int points = phase == 3 ? 8 : 5, step = phase == 3 ? 3 : 2;
        float gap = phase == 3 ? blinkGap * 0.8f : blinkGap;
        // His all-seeing eye opens above him for as long as he looks
        GameObject eye = Vfx.Glow(transform, Vector3.up * 5.5f, 3.4f, Color.white * 0.9f, "heimdalleye");
        warnings.Add(eye);
        for (int c = 0; c < chains && !dead; c++)
        {
            float windup = Cast(c == 0 ? sightFirstWindup : sightNextWindup);
            List<Vector3> path = StarPath(center, points, step, c * 180f / points);
            int legs = points;
            // He blinks to the star's first point on the rim to begin
            Blink(transform.position, path[0], gold);
            controller.enabled = false;
            transform.position = new Vector3(path[0].x, transform.position.y, path[0].z);
            controller.enabled = true;
            float width = 2.8f * S;
            // Under his gaze time drags for the player: a clock of runes turns at their feet
            PlayerEffects.ApplySlow(sightSlow, windup);
            VfxPiece clock = Vfx.Decal(Flat(player.position) + Vector3.up * 0.06f, 5f, Color.white, "timerune", -35f);
            warnings.Add(clock.gameObject);
            // The path: one strip per leg, each full at the moment he sets off along it, a ring at every turn
            var lines = new List<GameObject>();
            for (int i = 0; i < legs; i++)
            {
                Vector3 leg = path[i + 1] - path[i];
                GameObject line = Telegraph.Line(path[i], leg, leg.magnitude, width, windup + i * gap);
                lines.Add(line);
                warnings.Add(line);
                warnings.Add(HeimdallFx.Circle(path[i + 1], width * 1.8f, gold * 1.1f, 90f).gameObject);
            }
            for (float t = 0f; t < windup; t += Time.deltaTime)
            {
                if (clock != null)
                {
                    clock.transform.position = Flat(player.position) + Vector3.up * 0.06f;
                    clock.Set(Color.white * Mathf.Lerp(1.8f, 1f, t / windup), 5f, Mathf.Lerp(1f, 0.7f, t / windup));
                }
                yield return null;
            }
            if (clock != null)
                Destroy(clock.gameObject);

            // The chain: one teleport slash after another, edge to edge; anyone on the line is cut
            float y = transform.position.y;
            controller.enabled = false;
            dashing = true;
            for (int i = 0; i < legs && !dead; i++)
            {
                Vector3 a = new Vector3(path[i].x, y, path[i].z), b = new Vector3(path[i + 1].x, y, path[i + 1].z);
                Vector3 dir = Flat(b - a).normalized;
                if (lines[i] != null)
                    Destroy(lines[i]);
                Blink(a, b, gold);
                transform.position = b;
                // Turned back across the arena, sword swung through
                transform.rotation = Quaternion.LookRotation(-dir);
                if (animator != null)
                    animator.SetTrigger(SlashHash);
                SwordBeam.Fire(a + Vector3.up * S, dir, Flat(b - a).magnitude, width * 0.6f, gold);
                // A phantom Hofund left standing at each point of the star until the chain is done
                HeimdallFx.Sword(b, 0.75f, 0.1f, (legs - 1 - i) * gap + 0.5f, gold);
                CameraShake.Shake(0.15f, 0.18f);
                if (playerStats != null && DistanceToSegment(Flat(player.position), Flat(a), Flat(b)) <= width / 2f + 0.3f)
                {
                    playerStats.TakeDamage(damage * 1.1f, this);
                    player.GetComponent<PlayerMovement>()?.Push(Vector3.Cross(Vector3.up, dir) * 8f);
                }
                yield return new WaitForSeconds(gap);
            }
            controller.enabled = true;
            dashing = false;
            Shockwave.Spawn(transform.position, 3f * S, gold, 16);
            ClearWarning();
            if (c + 1 < chains)
            {
                eye = Vfx.Glow(transform, Vector3.up * 5.5f, 3.4f, Color.white * 0.9f, "heimdalleye");
                warnings.Add(eye);
                yield return new WaitForSeconds(Cast(0.4f));
            }
        }
        if (!dead)
            yield return HeartStrike(center, gold);
        yield return new WaitForSeconds(Cast(0.5f));
    }

    // Heimsight's finale: from the last point of the star he leaps across and comes down on its heart — the one place
    // the slashes never reached — Hofund first, and a shockwave rolls out from there. Only the outer part of the star,
    // out toward the rim, is beyond its reach.
    private IEnumerator HeartStrike(Vector3 center, Color gold)
    {
        const float waveRadius = 9f, height = 5f;
        Vector3 from = transform.position;
        Vector3 to = new Vector3(center.x, from.y, center.z);
        Warned?.Invoke("천리안의 끝! 별의 한가운데를 내려찍습니다 — 바깥쪽으로 벗어나세요");
        warnings.Add(Telegraph.Circle(to, waveRadius, Cast(0.7f) + SlamImpact));
        warnings.Add(HeimdallFx.Circle(to, 6f * S, gold, 160f).gameObject);
        Vector3 across = Flat(to - from);
        if (across.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(across);
        if (animator != null)
            animator.SetTrigger(CastHash);
        Vfx.Gather(transform.position + Vector3.up * S, 2f * S, gold, Cast(0.7f), 70f);
        yield return new WaitForSeconds(Cast(0.7f));

        // The leap: an arc over the arena, timed so Hofund comes down as he lands
        PlayState(GroundSlamState, 0.1f);
        Vfx.Dust(from, 1.5f * S, 12);
        Shockwave.Spawn(from, 2f * S, gold, 12);
        controller.enabled = false;
        dashing = true;
        for (float t = 0f; t < SlamImpact && !dead; t += Time.deltaTime)
        {
            float k = t / SlamImpact;
            transform.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * height;
            yield return null;
        }
        transform.position = to;
        controller.enabled = true;
        dashing = false;
        ClearWarning();

        // The blow, and the shockwave racing out from it (it catches anyone it passes over)
        Vector3 ground = new Vector3(to.x, 0f, to.z);
        Vfx.Sprite("hd_burst", ground + Vector3.up * 0.1f, 7f * S, Color.white * 1.5f, 0.8f, 0.9f, 1.05f, 30f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Sheet(4, 4);
        HeimdallFx.Impact(to + Vector3.up * 0.8f, 6f * S, gold);
        HeimdallFx.CircleFlash(to, waveRadius * 2f, gold, 0.8f);
        HeimdallFx.ShockRing(to, waveRadius, gold, 0.5f);
        Shockwave.Spawn(to, waveRadius, gold, 32);
        SonicRing.Spawn(to, waveRadius, 0.5f, new List<float>(), 0f, this, damage * 1.3f, playerStats);
        Vfx.Dust(to, 3f, 24);
        CameraShake.Shake(0.5f, 0.4f);
        yield return new WaitForSeconds(Cast(0.4f));
    }

    // Pillars of light: wave after wave, rune sigils open under the player, where they are heading and around them,
    // then light bursts up out of each. Each wave is marked while the last one is still waiting to go off.
    // He leaps up out of sight first, and comes crashing back down once the light has fallen.
    private IEnumerator LightPillars()
    {
        GameAudio.Play("heim_pillar", transform.position, 1.0f);   // (this move's own sound)
        var gold = new Color(1f, 0.85f, 0.45f);
        yield return LeapUp(gold);
        Warned?.Invoke("빛의 기둥! 발밑에 열리는 문양을 피하세요");
        int waves = phase == 3 ? 6 : 5;
        float warn = Cast(phase == 3 ? pillarWarn * 0.8f : pillarWarn);
        var playerBody = player.GetComponent<CharacterController>();
        Vector3 center = ArenaCenter;
        for (int w = 0; w < waves && !dead; w++)
        {
            Vector3 at = Flat(player.position);
            Vector3 heading = playerBody != null ? Flat(playerBody.velocity) : Vector3.zero;
            var spots = new List<Vector3> { at, at + heading * warn };
            for (int i = 0; i < (phase == 3 ? 4 : 3); i++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(3f, 6f);
                spots.Add(at + new Vector3(r.x, 0f, r.y));
            }
            // Keep them on the arena floor
            float limit = arenaRadius - 1f;
            for (int i = 0; i < spots.Count; i++)
            {
                Vector3 off = Flat(spots[i] - center);
                if (off.magnitude > limit)
                    off = off.normalized * limit;
                spots[i] = new Vector3(center.x + off.x, 0f, center.z + off.z);
            }
            StartCoroutine(PillarWave(spots, warn, gold));
            yield return new WaitForSeconds(Cast(0.5f));
        }
        yield return new WaitForSeconds(warn + 0.3f);
        yield return SlamDown(gold);
    }

    // ── The leap: up out of the frame, hidden and untouchable until he lands ──

    private const float LeapHeight = 22f;
    private float groundY;
    private bool airborne;
    private readonly List<Renderer> hiddenParts = new List<Renderer>();

    private IEnumerator LeapUp(Color gold)
    {
        GameAudio.Play("heim_leap", transform.position, 1.0f);   // (this move's own sound)
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(CastHash);
        // A crouch and a gathering glow, then he's gone in a burst of dust
        Vfx.Gather(transform.position + Vector3.up * 1.2f * S, 2.5f * S, gold, 0.5f, 60f);
        yield return new WaitForSeconds(Cast(0.5f));
        airborne = true;
        invulnerableUntil = float.MaxValue;
        groundY = transform.position.y;
        controller.enabled = false;
        Vfx.Dust(transform.position, 2f * S, 16);
        Shockwave.Spawn(transform.position, 3f * S, gold, 14);
        HeimdallFx.Impact(transform.position + Vector3.up * 0.6f * S, 3.5f * S, gold);
        HeimdallFx.CircleFlash(transform.position, 5f * S, gold, 0.6f);
        CameraShake.Shake(0.2f, 0.15f);
        Vector3 from = transform.position;
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            float k = t / 0.4f;
            transform.position = from + Vector3.up * LeapHeight * k * k;
            yield return null;
        }
        transform.position = from + Vector3.up * LeapHeight;
        // Out of the shot: hide him too, so no corner of a high camera catches him hanging there
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            if (r.enabled)
            {
                r.enabled = false;
                hiddenParts.Add(r);
            }
    }

    // Down onto a marked spot near the player: a ring of force where he lands
    private IEnumerator SlamDown(Color gold)
    {
        GameAudio.Play("heim_slam", transform.position, 1.0f);   // (this move's own sound)
        Vector3 center = ArenaCenter;
        Vector2 r = Random.insideUnitCircle.normalized * Random.Range(2.5f, 4f);
        Vector3 spot = Flat(player.position) + new Vector3(r.x, 0f, r.y);
        Vector3 off = Flat(spot - center);
        if (off.magnitude > arenaRadius - 2f)
            off = off.normalized * (arenaRadius - 2f);
        spot = new Vector3(center.x + off.x, groundY, center.z + off.z);
        float radius = 3.4f * S;
        warnings.Add(Telegraph.Circle(spot, radius, Cast(0.8f)));
        warnings.Add(HeimdallFx.Circle(spot, radius * 2.4f, gold * 0.9f, 140f).gameObject);
        yield return new WaitForSeconds(Cast(0.5f));

        transform.position = spot + Vector3.up * LeapHeight;
        Vector3 to = Flat(player.position - spot);
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(to);
        // A giant phantom Hofund comes down with him, striking just behind where he lands
        Vector3 behind = to.sqrMagnitude > 0.01f ? -to.normalized : Vector3.back;
        HeimdallFx.Sword(spot + behind * 1.4f * S, 2f, 0.3f, 0.9f, gold);
        ShowAgain();
        if (animator != null)
            animator.SetTrigger(LandHash);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            float k = t / 0.3f;
            transform.position = spot + Vector3.up * LeapHeight * (1f - k * k);
            yield return null;
        }
        Land();
        ClearWarning();
        Shockwave.Spawn(spot, radius * 1.6f, gold, 26);
        HeimdallFx.Impact(spot + Vector3.up * 0.8f, radius * 2.4f, gold);
        HeimdallFx.CircleFlash(spot, radius * 3f, gold, 0.8f);
        Vfx.Dust(spot, radius, 20);
        CameraShake.Shake(0.45f, 0.35f);
        if (playerStats != null && Flat(player.position - spot).magnitude <= radius + 0.3f)
        {
            playerStats.TakeDamage(damage * 1.2f, this);
            player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - spot).normalized * 9f);
        }
        yield return new WaitForSeconds(Cast(0.6f));
    }

    private void ShowAgain()
    {
        foreach (Renderer r in hiddenParts)
            if (r != null)
                r.enabled = true;
        hiddenParts.Clear();
    }

    // Back on the ground wherever he is (also when the leap is cut short)
    private void Land()
    {
        if (!airborne)
            return;
        airborne = false;
        ShowAgain();
        transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
        controller.enabled = true;
        invulnerableUntil = 0f;
    }

    // ── Edge beams: he blinks to the arena's rim, left or right of the screen, and sweeps Hofund's beam across the
    // whole arena from the top of the screen to the bottom, then blinks to the other rim and looses a salvo of
    // beams as well. ──

    private IEnumerator EdgeBeams()
    {
        Warned?.Invoke("호프눈드의 섬광! 빔이 위에서 아래로 훑고 지나갑니다");
        var gold = new Color(1f, 0.85f, 0.45f);
        Vector3 center = ArenaCenter;
        // Screen directions on the ground: up is away from the camera
        Camera cam = Camera.main;
        Vector3 up = cam != null ? Flat(cam.transform.forward) : Vector3.forward;
        up = up.sqrMagnitude > 0.01f ? up.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, up);
        // The rim on whichever side he is nearer
        float side = Vector3.Dot(transform.position - center, right) >= 0f ? 1f : -1f;
        yield return BlinkTo(center + right * side * (arenaRadius - 2.5f));
        yield return SweepBeam(center, up, gold);
        if (!dead)
        {
            yield return BlinkTo(center - right * side * (arenaRadius - 2.5f));
            // 빛 3갈래: plain and parting in phase 2, bending and parting in the last phase
            int mode = phase == 3 ? (triMode % 2 == 0 ? 2 : 1) : triMode % 2;
            triMode++;
            yield return TriLight(center, gold, mode);
        }
        yield return new WaitForSeconds(Cast(0.4f));
    }

    // One beam swung from pointing up the screen to pointing down it. The fan it will cover is drawn first,
    // each strip filling as the beam will reach it, so the sweep's direction and pace read ahead of time.
    private IEnumerator SweepBeam(Vector3 center, Vector3 up, Color gold)
    {
        GameAudio.Play("heim_beam", transform.position, 0.85f);   // (this move's own sound)
        Vector3 inward = Flat(center - transform.position).normalized;
        transform.rotation = Quaternion.LookRotation(inward);
        float startAngle = Mathf.Sign(Vector3.SignedAngle(inward, up, Vector3.up)) * 78f;
        float endAngle = -startAngle;
        float length = arenaRadius * 2.2f, width = 2.6f;
        float windup = Cast(phase == 3 ? 0.8f : 0.95f);
        float sweep = phase == 3 ? 1.1f : 1.4f;
        const int strips = 9;
        for (int i = 0; i < strips; i++)
        {
            float k = i / (strips - 1f);
            Vector3 dir = Quaternion.Euler(0f, Mathf.Lerp(startAngle, endAngle, k), 0f) * inward;
            warnings.Add(Telegraph.Line(transform.position, dir, length, 0.7f, windup + sweep * k));
        }
        if (animator != null)
            animator.SetTrigger(CastHash);
        Vfx.Gather(transform.position + Vector3.up * 1.6f * S, 2.5f, gold, windup, 60f);
        yield return new WaitForSeconds(windup);

        if (animator != null)
            animator.SetTrigger(SlashHash);
        Vector3 from = transform.position + Vector3.up * 1.4f * S + inward * 0.8f * S;
        Vector3 startDir = Quaternion.Euler(0f, startAngle, 0f) * inward;
        SwordBeam beam = SwordBeam.Hold(from, startDir, length, width, gold);
        warnings.Add(beam.gameObject);
        CameraShake.Shake(sweep, 0.12f);
        bool hit = false;
        for (float t = 0f; t < sweep; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / sweep);
            Vector3 dir = Quaternion.Euler(0f, Mathf.Lerp(startAngle, endAngle, k), 0f) * inward;
            beam.Aim(dir);
            transform.rotation = Quaternion.LookRotation(dir);
            if (!hit && playerStats != null)
            {
                Vector3 rel = Flat(player.position - transform.position);
                float along = Vector3.Dot(rel, dir);
                if (along > 0f && along < length && Vector3.Cross(dir, rel).magnitude <= width / 2f + 0.3f)
                {
                    hit = true;
                    playerStats.TakeDamage(damage * 1.3f, this);
                }
            }
            yield return null;
        }
        warnings.Remove(beam.gameObject);
        beam.Release();
        ClearWarning();
        yield return new WaitForSeconds(Cast(0.5f));
    }

    // Off to where a pattern starts: light gathers round him, he vanishes in a flash and reappears there at once
    // (kept on the arena floor), rather than running across the arena
    private IEnumerator BlinkTo(Vector3 target)
    {
        GameAudio.Play("heim_blink", transform.position, 1.0f);   // (this move's own sound)
        Vector3 spot = InArena(target, 1.5f);
        Vector3 to = spot - Flat(transform.position);
        if (to.magnitude < 0.6f)
            yield break;
        var gold = HeimdallFx.Gold;
        Vfx.Gather(transform.position + Vector3.up * S, 1.6f * S, gold, 0.2f, 70f);
        yield return new WaitForSeconds(0.2f);
        Vector3 dest = new Vector3(spot.x, transform.position.y, spot.z);
        Blink(transform.position, dest, gold);
        Vfx.Bolt(transform.position + Vector3.up * S, dest + Vector3.up * S, gold, 0.5f, 0.18f);
        controller.enabled = false;
        transform.position = dest;
        controller.enabled = true;
        transform.rotation = Quaternion.LookRotation(to);
        Vfx.Dust(dest, 1.2f * S, 8);
        yield return new WaitForSeconds(0.15f);
    }

    // Routines one after another, as one action
    private static IEnumerator Sequence(params IEnumerator[] steps)
    {
        foreach (IEnumerator step in steps)
            yield return step;
    }

    // From the rim, beams fan out across the arena on the diagonals either side of the middle and one straight at
    // the player (two wider ones too in phase 3). Marked first, then they burn along their whole length for a moment.
    private IEnumerator BeamSalvo(Vector3 center, Color gold)
    {
        Vector3 inward = Flat(center - transform.position).normalized;
        transform.rotation = Quaternion.LookRotation(inward);
        var angles = new List<float> { -32f, 32f, Vector3.SignedAngle(inward, Flat(player.position - transform.position), Vector3.up) };
        if (phase == 3)
        {
            angles.Add(-60f);
            angles.Add(60f);
        }
        float length = arenaRadius * 2.2f, width = 2.2f;
        float windup = Cast(phase == 3 ? 0.75f : 0.9f);
        foreach (float a in angles)
            warnings.Add(Telegraph.Line(transform.position, Quaternion.Euler(0f, a, 0f) * inward, length, width, windup));
        if (animator != null)
            animator.SetTrigger(CastHash);
        Vfx.Gather(transform.position + Vector3.up * 1.6f * S, 2.5f, gold, windup, 60f);
        yield return new WaitForSeconds(windup);
        ClearWarning();

        if (animator != null)
            animator.SetTrigger(SlashHash);
        Vector3 from = transform.position + Vector3.up * 1.4f * S + inward * 0.8f * S;
        foreach (float a in angles)
            SwordBeam.Fire(from, Quaternion.Euler(0f, a, 0f) * inward, length, width, gold);
        CameraShake.Shake(0.35f, 0.25f);
        // The beams burn for a moment: anyone on one is hit once
        bool hit = false;
        for (float t = 0f; t < 0.5f && !hit; t += Time.deltaTime)
        {
            foreach (float a in angles)
            {
                Vector3 dir = Quaternion.Euler(0f, a, 0f) * inward;
                Vector3 rel = Flat(player.position - transform.position);
                float along = Vector3.Dot(rel, dir);
                if (along > 0f && along < length && Vector3.Cross(dir, rel).magnitude <= width / 2f + 0.3f)
                    hit = true;
            }
            if (hit && playerStats != null)
                playerStats.TakeDamage(damage * 1.3f, this);
            yield return null;
        }
        yield return new WaitForSeconds(Cast(0.5f));
    }

    private IEnumerator PillarWave(List<Vector3> spots, float warn, Color gold)
    {
        GameAudio.Play("heim_pillar", transform.position, 0.7f);   // (this move's own sound)
        var sigils = new List<VfxPiece>();
        foreach (Vector3 s in spots)
            sigils.Add(Vfx.Decal(s + Vector3.up * 0.06f, pillarRadius * 2.5f, Color.white * 0.4f, "pillarsigil", 90f).OpenUp(0.25f));
        // A phantom Hofund falls into each sigil, striking just as its light bursts up
        const float swordFall = 0.3f;
        bool swords = false;
        for (float t = 0f; t < warn; t += Time.deltaTime)
        {
            float k = t / warn;
            if (!swords && t >= warn - swordFall)
            {
                swords = true;
                foreach (Vector3 s in spots)
                    HeimdallFx.Sword(s, 0.9f, warn - t, 0.45f, gold);
            }
            foreach (VfxPiece sigil in sigils)
                if (sigil != null)
                    sigil.Set(Color.white * Mathf.Lerp(0.4f, 1.6f, k), pillarRadius * 2.5f * Mathf.Lerp(0.7f, 1f, k), Mathf.Lerp(0.5f, 1f, k));
            yield return null;
        }
        foreach (VfxPiece sigil in sigils)
            if (sigil != null)
                Destroy(sigil.gameObject);
        if (dead)
            yield break;

        bool hit = false;
        foreach (Vector3 s in spots)
        {
            LightPillar.Spawn(s, pillarRadius, 11f, gold);
            if (!hit && Flat(player.position - s).magnitude <= pillarRadius)
                hit = true;
        }
        if (hit && playerStats != null)
            playerStats.TakeDamage(damage * 0.8f, this);
        CameraShake.Shake(0.15f, 0.12f);
    }

    private float RimRadius => arenaRadius - 1.5f;

    // The chain of strikes as a star drawn across the arena: its points sit on the rim, the first at north (turned
    // by this many degrees), and each strike skips ahead `step` points, so every line spans the arena and the chain
    // closes back where it began. Safe ground: the star's heart and the rim between its points.
    private List<Vector3> StarPath(Vector3 center, int points, int step, float turn)
    {
        var path = new List<Vector3>();
        for (int i = 0; i <= points; i++)
        {
            float a = (90f - turn - i * step * 360f / points) * Mathf.Deg2Rad;   // 90 degrees is north (+z)
            path.Add(Flat(center) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * RimRadius);
        }
        return path;
    }

    // Gone in a flash here, there in a flash: a teleport between two spots
    private void Blink(Vector3 from, Vector3 to, Color gold)
    {
        HeimdallFx.Impact(from + Vector3.up * S, 3f * S, gold);
        Vfx.Flash(to + Vector3.up * S, 2.5f * S, Color.white, 0.25f);
        HeimdallFx.Impact(to + Vector3.up * S, 3.5f * S, Color.Lerp(gold, Color.white, 0.5f));
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return (p - (a + ab * k)).magnitude;
    }

    // His last stand, once his health falls to a tenth: the long charge. Bring him down, knock him down or hit hard
    // enough before it completes, or the Bifrost beam kills.
    private bool LastStandDue => phase == 3 && !lastStandUsed && !dead && heimdallHealth <= heimdallMax * lastStandAt;

    private IEnumerator UltimateCharge()
    {
        GameAudio.Play("boss_warn"); GameAudio.Play("heim_ult", transform.position, 1.0f);   // (this move's own sound)
        lastStandUsed = true;
        UltimateCharging = true;
        chargeDamage = 0f;
        if (animator != null)
            animator.SetBool(ChargingHash, true);
        Warned?.Invoke("비프로스트 충전! 시간 안에 쓰러뜨리지 못하면 죽습니다");

        warning = Telegraph.Circle(ArenaCenter, arenaRadius + 3f, ultimateChargeTime);
        // A swelling star of Bifrost light above him, a rune circle turning at his feet
        var orb = new GameObject("UltimateOrb");
        orb.transform.SetParent(transform, false);
        orb.transform.localPosition = new Vector3(0f, 3.2f, 0f);
        Vfx.Glow(orb.transform, Vector3.zero, 2.2f, new Color(0.85f, 0.85f, 1f));
        VfxPiece core = Vfx.Glow(orb.transform, Vector3.zero, 1.4f, Color.white, "flare").GetComponent<VfxPiece>();
        warnings.Add(orb);
        VfxPiece circle = Vfx.Decal(transform.position + Vector3.up * 0.05f, 7f, Color.white, "bifrostcircle", 60f).OpenUp(0.4f);
        warnings.Add(circle.gameObject);
        float nextPulse = 0f;
        for (float t = 0f; t < ultimateChargeTime; t += Time.deltaTime)
        {
            // Broken off (the orb is cleared with the other warnings): stop here
            if (!UltimateCharging || orb == null)
                yield break;
            float k = t / ultimateChargeTime;
            float s = Mathf.Lerp(0.3f, 2.2f, k) + Mathf.Sin(t * 20f) * 0.05f;
            orb.transform.localScale = Vector3.one * s;
            Color rainbow = Color.HSVToRGB(Mathf.Repeat(t * 0.6f, 1f), 0.6f, 1f);
            core.Set(Color.Lerp(Color.white, rainbow, 0.5f) * 1.5f, 1.4f);
            circle.Set(rainbow * 1.4f, Mathf.Lerp(5f, 9f, k), Mathf.Lerp(0.5f, 1f, k));
            // Light gathering in: pulses quicken as the beam nears
            if (t >= nextPulse)
            {
                nextPulse = t + Mathf.Lerp(0.6f, 0.15f, k);
                Vfx.Flash(orb.transform.position, 1.5f + 2f * k, rainbow);
            }
            yield return null;
        }
        ClearWarning();
        UltimateCharging = false;
        if (animator != null)
            animator.SetBool(ChargingHash, false);

        // Not stopped: the Bifrost beam
        BifrostBeam.Fire(transform.position + Vector3.up * 3f, player.position);
        CameraShake.Shake(0.8f, 0.5f);
        playerStats.TakeDamage(9999f, this);
        yield return new WaitForSeconds(1f);
    }

    private void BreakCharge()
    {
        if (!UltimateCharging)
            return;
        UltimateCharging = false;
        Warned?.Invoke("헤임달의 충전을 끊었다!");
        StopAction();
        if (animator != null)
            animator.SetBool(ChargingHash, false);
        Shockwave.Spawn(transform.position, 4f, Color.white, 20);
        Act(Staggered(3f), true);
    }

    private IEnumerator Staggered(float seconds)
    {
        GameAudio.Play("boss_stagger", transform.position, 1.0f);   // (this move's own sound)
        if (animator != null && animator.HasState(0, StaggerState))
        {
            animator.CrossFadeInFixedTime(StaggerState, 0.08f);
            yield return new WaitForSeconds(seconds);
            yield break;
        }
        Transform model = rider;
        Quaternion upright = model.localRotation;
        Quaternion fallen = upright * Quaternion.Euler(-70f, 0f, 0f);
        for (float t = 0f; t < 0.2f; t += Time.deltaTime) { model.localRotation = Quaternion.Slerp(upright, fallen, t / 0.2f); yield return null; }
        yield return new WaitForSeconds(seconds);
        for (float t = 0f; t < 0.4f; t += Time.deltaTime) { model.localRotation = Quaternion.Slerp(fallen, upright, t / 0.4f); yield return null; }
        model.localRotation = upright;
    }

    // His phase-2 bar emptied: a cutscene in which he draws the Bifrost's light into himself, his health filling
    // back up as it pours in
    private void BeginPhaseThree()
    {
        StopAction();
        if (UltimateCharging)
        {
            UltimateCharging = false;
            if (animator != null)
                animator.SetBool(ChargingHash, false);
        }
        busy = true;
        invulnerableUntil = float.MaxValue;
        BossCutscene.Play(transform, CutsceneHeight * S, PhaseThreeScene());
    }

    private IEnumerator PhaseThreeScene()
    {
        GameAudio.Play("heim_phase3", transform.position, 1.0f);   // (this move's own sound)
        busy = true;
        FacePlayer();
        if (animator != null)
        {
            animator.SetFloat(SpeedHash, 0f);
            if (animator.HasState(0, TransformState))
                animator.CrossFadeInFixedTime(TransformState, 0.25f);
        }
        // Rainbow light streams in from every side while a rune circle turns faster beneath him
        Vector3 chest = transform.position + Vector3.up * 1.8f * S;
        Color[] rainbow = { new Color(1f, 0.35f, 0.35f), new Color(1f, 0.85f, 0.35f), new Color(0.4f, 1f, 0.55f), new Color(0.45f, 0.65f, 1f), new Color(0.8f, 0.5f, 1f) };
        foreach (Color c in rainbow)
            Vfx.Gather(chest, 5f, c, 1.8f, 30f);
        VfxPiece circle = Vfx.Decal(transform.position + Vector3.up * 0.05f, 4f, Color.white, "bifrostcircle", 160f).OpenUp(0.4f);
        float rumble = 0f;
        int emptied = heimdallHealth;
        for (float t = 0f; t < 1.8f; t += Time.deltaTime)
        {
            float k = t / 1.8f;
            heimdallHealth = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(emptied, heimdallMax, Mathf.SmoothStep(0f, 1f, k))));
            circle.Set(Color.HSVToRGB(Mathf.Repeat(t * 0.8f, 1f), 0.6f, 1f) * (1f + k), Mathf.Lerp(4f, 9f, k), Mathf.Lerp(0.4f, 1f, k));
            if ((rumble -= Time.deltaTime) <= 0f)
            {
                rumble = 0.25f;
                CameraShake.Shake(0.25f, 0.04f + 0.08f * k);
            }
            yield return null;
        }
        Destroy(circle.gameObject);

        // The Bifrost answers: a column of rainbow light strikes him from the sky
        BifrostBeam.Fire(transform.position + Vector3.up * 24f, transform.position);
        BossCutscene.Flash(1f);
        BossCutscene.Dolly(0.78f);
        BossCutscene.Title("비프로스트 해방", "헤임달이 무지개 다리의 힘을 온몸에 두른다", new Color(0.85f, 0.75f, 1f));
        CameraShake.Shake(0.8f, 0.4f);
        Vfx.GroundRing(transform.position, 10f, Color.white, 1f, "bifrostcircle");
        EnterPhaseThree();
        yield return new WaitForSeconds(2.2f);

        restTimer = 1f;
        invulnerableUntil = 0f;
        busy = false;
    }

    private void EnterPhaseThree()
    {
        phase = 3;
        heimdallHealth = heimdallMax;
        DisplayName = "헤임달 · 비프로스트 해방";
        patternIndex = 0;
        // Rainbow light and a turning ring at his feet
        var glow = new GameObject("BifrostAura");
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = Vector3.up * 1.5f;
        aura = glow.AddComponent<Light>();
        aura.type = LightType.Point;
        aura.range = 8f;
        aura.intensity = 3f;
        auraRing = Mats.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.04f, 0f), new Vector3(2.6f, 0.01f, 2.6f),
            Mats.Get("aura_ring", Color.white, 0.7f, new Color(1.5f, 1.5f, 1.5f))).transform;
        if (swordRenderer != null)
            swordRenderer.sharedMaterial = Mats.Get("hofund_bifrost", new Color(1f, 0.95f, 0.85f), 0.9f, new Color(1.6f, 1.4f, 2f));
    }

    private void AnimateAura()
    {
        Color c = Color.HSVToRGB(Mathf.Repeat(Time.time * 0.25f, 1f), 0.7f, 1f);
        if (aura != null)
            aura.color = c;
        if (auraRing != null)
        {
            auraRing.Rotate(0f, 120f * Time.deltaTime, 0f);
            float pulse = 2.6f + Mathf.Sin(Time.time * 4f) * 0.2f;
            auraRing.localScale = new Vector3(pulse, 0.01f, pulse);
        }
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
        ClearWarning();
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
        ClearWarning();
        actionMove = Vector3.zero;
        WorldDim.Release();
        if (animator != null) animator.speed = 1f;
        Land();
        // Cut off mid-dash: back under the controller where he stands
        if (dashing)
        {
            dashing = false;
            controller.enabled = true;
        }
        busy = false;
        Rest();
    }

    // The breather between patterns shortens as the fight goes on
    private void Rest() => restTimer = testHold ? 999f : Cast(phase == 1 ? 0.6f : phase == 2 ? 0.4f : 0.25f);
    private bool testHold;   // (editor tests: stand still between the patterns a test starts)

    // A windup or pause as he actually takes it: its designed length scaled by how quickly he casts (and quicker
    // still once he's on foot, and again in his last phase)
    private float Cast(float seconds) => seconds * castTime * (phase == 1 ? 1f : phase == 2 ? 0.9f : 0.8f);

    private void ClearWarning()
    {
        if (warning != null)
            Destroy(warning);
        foreach (GameObject w in warnings)
            if (w != null) Destroy(w);
        warnings.Clear();
    }

    private void FacePlayer()
    {
        Vector3 to = Flat(player.position - transform.position);
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(to);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    public override void TakeHit(Vector3 from, int amount)
    {
        if (dead || Time.time < invulnerableUntil)
            return;
        StartCoroutine(Flash());

        if (phase == 1)
        {
            lionHealth -= Mathf.Max(1, amount);
            if (lionHealth <= 0)
            {
                lionHealth = 0;
                BeginDismount();
            }
            return;
        }

        // (the opening after 천리안, or a brand dodged at the last moment: his guard is down)
        if (OpenToHits)
            amount = Mathf.RoundToInt(amount * 1.5f);
        heimdallHealth -= Mathf.Max(1, amount);
        if (UltimateCharging)
        {
            chargeDamage += amount;
            if (BreakProgress >= 1f)
                BreakCharge();
        }
        // Each phase on foot has a full bar of its own: emptying phase 2's sends him into phase 3 (which fills it
        // again); emptying phase 3's brings him down
        if (heimdallHealth <= 0 && phase == 2)
        {
            heimdallHealth = 1;
            if (!BossCutscene.Playing)
                BeginPhaseThree();
            return;
        }
        if (heimdallHealth <= 0)
        {
            Die();
            return;
        }
        if (LastStandDue && !BossCutscene.Playing)
        {
            // Whatever he was doing, he drops it and begins the charge at once
            StopAction();
            Act(UltimateCharge(), true);
        }
    }

    // Too big to topple from the saddle; on foot a full-charge knockdown staggers him, and stops the beam outright
    public override void Knockdown(float seconds)
    {
        if (dead || phase == 1 || Time.time < invulnerableUntil)
            return;
        if (UltimateCharging)
        {
            BreakCharge();
            return;
        }
        StopAction();
        Act(Staggered(0.7f), true);
    }

    public override void Interrupt()
    {
        if (dead || UltimateCharging || phase == 1)
            return;
        StopAction();
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
        ClearWarning();
        WorldDim.Release();
        UltimateCharging = false;
        PlayerEffects.Clear();
        controller.enabled = false;
        if (animator != null)
        {
            animator.SetBool(ChargingHash, false);
            animator.SetTrigger(DieHash);
        }
        RaiseDied();
        Destroy(gameObject, 4f);
    }
}

// A sonic ring rolling outward from Gjallarhorn. It hurts once as it passes, except inside its gaps.
public class SonicRing : MonoBehaviour
{
    private float radius, seconds, t, gapHalfWidth, damage;
    private List<float> gaps;
    private Enemy source;
    private PlayerStats target;
    private bool hit;
    private Transform[] shards;

    public static void Spawn(Vector3 center, float radius, float seconds, List<float> gaps, float gapHalfWidth, Enemy source, float damage, PlayerStats target)
    {
        var go = new GameObject("SonicRing");
        go.transform.position = new Vector3(center.x, 0.3f, center.z);
        var ring = go.AddComponent<SonicRing>();
        ring.radius = radius; ring.seconds = seconds; ring.gaps = gaps; ring.gapHalfWidth = gapHalfWidth;
        ring.source = source; ring.damage = damage; ring.target = target;
        // A wall of sound: bands of rippling sound waves (VARCO) flowing round the ring, broken at the gaps
        Material mat = HeimdallFx.Flow("soundwave", new Vector2(-1.6f, 0f));
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", new Color(0.8f, 0.95f, 1f) * 2.2f);
        var list = new List<Transform>();
        for (float a = 0f; a < 360f; a += 6f)
        {
            if (ring.InGap(a)) continue;
            var shard = new GameObject("SoundBand");
            shard.transform.SetParent(go.transform, false);
            shard.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            shard.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
            var r = shard.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.SetPropertyBlock(block);
            list.Add(shard.transform);
        }
        ring.shards = list.ToArray();
        Vfx.Flash(center + Vector3.up * 1.5f, 4f, new Color(0.75f, 0.92f, 1f));
    }

    private bool InGap(float angle)
    {
        foreach (float g in gaps)
            if (Mathf.Abs(Mathf.DeltaAngle(angle, g)) <= gapHalfWidth)
                return true;
        return false;
    }

    private void Update()
    {
        t += Time.deltaTime * PlayerEffects.Slow;
        float k = Mathf.Clamp01(t / seconds);
        float r = radius * k;
        foreach (Transform s in shards)
        {
            // shards are tangent to the ring
            Vector3 dir = s.localRotation * Vector3.forward;
            s.localPosition = dir * r;
            s.localRotation = Quaternion.LookRotation(dir);
            s.localScale = new Vector3(Mathf.Max(0.9f, r * 0.11f) * 1.25f, 2.4f * (1f - k * 0.5f), 1f);
        }
        if (!hit && target != null)
        {
            Vector3 d = target.transform.position - transform.position;
            d.y = 0f;
            float angle = Vector3.SignedAngle(Vector3.forward, d, Vector3.up);
            if (Mathf.Abs(d.magnitude - r) < 0.8f && !InGap(angle))
            {
                hit = true;
                target.TakeDamage(damage, source);
            }
        }
        if (k >= 1f)
            Destroy(gameObject);
    }
}

// A lane of fire left burning behind Gulltoppr's charge: VARCO's ground fire standing in a row along it, scorching
// anyone inside every so often, then dying down
public class FlameLane : MonoBehaviour
{
    private const float Tick = 0.4f;

    private Vector3 from, to;
    private float width, seconds, damage, t, nextTick;
    private Enemy source;
    private PlayerStats target;
    private readonly List<(VfxPiece piece, float size)> flames = new List<(VfxPiece, float)>();
    private static readonly Color FireColor = new Color(1f, 0.85f, 0.7f) * 1.5f;

    public static void Spawn(Vector3 from, Vector3 to, float width, float seconds, float damage, Enemy source, PlayerStats target)
    {
        from.y = 0f;
        to.y = 0f;
        var go = new GameObject("FlameLane");
        go.transform.position = from;
        var lane = go.AddComponent<FlameLane>();
        lane.from = from; lane.to = to; lane.width = width; lane.seconds = seconds; lane.damage = damage;
        lane.source = source; lane.target = target;
        lane.nextTick = 0.3f;   // a moment to get clear of the fresh flames
        float length = (to - from).magnitude;
        int count = Mathf.Max(2, Mathf.CeilToInt(length / (width * 0.55f)));
        for (int i = 0; i <= count; i++)
        {
            Vector3 at = Vector3.Lerp(from, to, i / (float)count) + Random.insideUnitSphere.normalized * width * 0.12f;
            float size = width * Random.Range(1f, 1.25f);
            // Looping flipbook standing on the floor (the flames rise from the bottom of the frame)
            VfxPiece flame = Vfx.Sprite("hd_flame", new Vector3(at.x, size * 0.4f, at.z), size, FireColor, 0f, 1f, 1f, 0f).Sheet(4, 4);
            flame.transform.SetParent(go.transform, true);
            lane.flames.Add((flame, size));
        }
        Vfx.Dust(Vector3.Lerp(from, to, 0.5f), length * 0.3f, 12, new Color(0.3f, 0.2f, 0.15f));
    }

    private void Update()
    {
        t += Time.deltaTime;
        // Grows up out of the floor, then sinks and fades over the last second
        float grow = Mathf.Clamp01(t / 0.25f), fade = Mathf.Clamp01((seconds - t) / 1f);
        foreach (var (piece, size) in flames)
            if (piece != null)
                piece.Set(FireColor * fade, size * grow * Mathf.Lerp(0.5f, 1f, fade), fade);
        if (target != null && t >= nextTick && fade > 0.3f)
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

// A band of ground marked for an eruption (the rings of judgment): a dark ring that fills red from its inner edge out
public class BandTelegraph : MonoBehaviour
{
    private const int Segments = 72;

    private Mesh ring, fill;
    private float inner, outer, seconds, t;

    public static GameObject Spawn(Vector3 center, float inner, float outer, float seconds)
    {
        var go = new GameObject("TelegraphBand");
        go.transform.position = new Vector3(center.x, 0.03f, center.z);
        var band = go.AddComponent<BandTelegraph>();
        band.inner = inner;
        band.outer = outer;
        band.seconds = Mathf.Max(0.01f, seconds);
        band.ring = Annulus(new Mesh(), inner, outer);
        Part(go.transform, band.ring, Mats.Get("fx_warn_ring", new Color(0.25f, 0.05f, 0.05f)), 0f);
        band.fill = new Mesh();
        band.fill.MarkDynamic();
        Part(go.transform, band.fill, Mats.Get("fx_warn_fill", new Color(0.9f, 0.15f, 0.1f), 0.2f, new Color(0.8f, 0.1f, 0.05f)), 0.01f);
        band.Update();
        return go;
    }

    private static void Part(Transform parent, Mesh mesh, Material material, float lift)
    {
        var go = new GameObject("Band");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * lift;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // A flat ring between two radii facing up (a disc when the inner radius is 0)
    private static Mesh Annulus(Mesh mesh, float a, float b)
    {
        var vertices = new Vector3[(Segments + 1) * 2];
        var triangles = new int[Segments * 6];
        for (int i = 0; i <= Segments; i++)
        {
            float angle = i * Mathf.PI * 2f / Segments;
            var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            vertices[i * 2] = dir * a;
            vertices[i * 2 + 1] = dir * b;
            if (i == Segments)
                break;
            int v = i * 2, n = 0;
            int[] tri = { v, v + 3, v + 1, v, v + 2, v + 3 };
            foreach (int x in tri)
                triangles[i * 6 + n++] = x;
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
        Annulus(fill, inner, Mathf.Lerp(inner, outer, Mathf.Clamp01(t / seconds)));
    }

    private void OnDestroy()
    {
        if (ring != null)
            Destroy(ring);
        if (fill != null)
            Destroy(fill);
    }
}

// Scales an object up from nothing (Bifrost fissures opening)
public class GrowIn : MonoBehaviour
{
    private float seconds, t;
    private Vector3 size;

    public void Init(float duration)
    {
        seconds = duration;
        size = transform.localScale;
        transform.localScale = new Vector3(size.x, size.y, 0f);
    }

    private void Update()
    {
        t += Time.deltaTime;
        transform.localScale = new Vector3(size.x, size.y, size.z * Mathf.Clamp01(t / seconds));
        if (t >= seconds)
            Destroy(this);
    }
}

// A shaft of light bursting up out of the ground (Heimdall's pillars): a column that faces the camera,
// flares wide and fades, with a flash and a ring of light where it rises
public class LightPillar : MonoBehaviour
{
    private const float Life = 0.75f;
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");

    private Transform column;
    private Renderer rend;
    private MaterialPropertyBlock block;
    private Color color;
    private float width, height, t;

    public static void Spawn(Vector3 at, float radius, float height, Color color)
    {
        var go = new GameObject("LightPillar");
        go.transform.position = new Vector3(at.x, 0f, at.z);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(go.transform, false);
        var renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = Vfx.Material("pillar", additive: true);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var pillar = go.AddComponent<LightPillar>();
        pillar.column = quad.transform;
        pillar.rend = renderer;
        pillar.block = new MaterialPropertyBlock();
        pillar.color = color;
        pillar.width = radius * 2.4f;
        pillar.height = height;
        pillar.LateUpdate();

        Vector3 ground = go.transform.position;
        Vfx.Flash(ground + Vector3.up * 0.6f, radius * 2.6f, color, 0.3f);
        Vfx.GroundRing(ground, radius * 1.4f, color, 0.45f);
        Vfx.Burst(ground + Vector3.up * 0.4f, Color.Lerp(color, Color.white, 0.4f), 14, 8f, 0.16f, -0.8f);
    }

    private void LateUpdate()
    {
        t += Time.deltaTime;
        float k = t / Life;
        if (k >= 1f)
        {
            Destroy(gameObject);
            return;
        }
        // Bursts up wide, then thins out as it fades
        float w = width * (k < 0.15f ? Mathf.Lerp(0.3f, 1.15f, k / 0.15f) : Mathf.Lerp(1.15f, 0.25f, (k - 0.15f) / 0.85f));
        column.localScale = new Vector3(w, height, 1f);
        column.localPosition = Vector3.up * height / 2f;
        // Turn about the vertical to face the camera (a quad shows its -z side)
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 to = cam.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(-to);
        }
        // The painted column is already gold: light it white so its own colours come through
        block.SetColor(ColorId, Color.Lerp(Color.white, color, 0.25f) * 1.8f * (1f - k * k));
        rend.SetPropertyBlock(block);
    }
}

// The Bifrost beam: a thick rainbow column from Heimdall to the target, gone in a moment
// Hofund's beam: a straight shaft of golden light from the sword across the arena, flaring and then thinning away
public class SwordBeam : MonoBehaviour
{
    private LineRenderer line, core;
    private Vector3 origin;
    private float length, width, t;
    private bool held;          // still pouring out of the sword (a sweep): full width until released
    private float sparkTimer;
    private const float Life = 0.7f;

    // A beam that flares and fades on its own
    public static void Fire(Vector3 from, Vector3 direction, float length, float width, Color color)
    {
        SwordBeam beam = Hold(from, direction, length, width, color);
        Vector3 to = beam.line.GetPosition(1);
        for (float d = 3f; d < length; d += 4f)
            Vfx.Burst(Vector3.Lerp(from, to, d / length), color, 3, 3f, 0.12f);
        beam.Release();
    }

    // A beam that stays lit and can be turned (Aim) until Release lets it fade
    public static SwordBeam Hold(Vector3 from, Vector3 direction, float length, float width, Color color)
    {
        var go = new GameObject("SwordBeam");
        var beam = go.AddComponent<SwordBeam>();
        beam.origin = from;
        beam.length = length;
        beam.width = width;
        beam.held = true;
        // The body of the beam is VARCO's flowing light, streaming away from the sword; a white-hot core runs through it
        beam.line = Line(go.transform, from, from, color * 2f, HeimdallFx.Flow("beamflow", new Vector2(-3f, 0f)));
        beam.line.textureMode = LineTextureMode.Tile;
        beam.line.textureScale = new Vector2(1f / Mathf.Max(0.5f, width * 3f), 1f);
        beam.core = Line(go.transform, from, from, Color.white * 2.2f, Vfx.Material("beam", additive: true));
        beam.Aim(direction);
        HeimdallFx.Impact(from, 3f, color);
        return beam;
    }

    public void Aim(Vector3 direction)
    {
        Vector3 to = origin + direction.normalized * length;
        to.y = 0.9f;
        line.SetPosition(1, to);
        core.SetPosition(1, to);
    }

    public void Release()
    {
        held = false;
        t = 0f;
        Destroy(gameObject, Life);
    }

    private static LineRenderer Line(Transform parent, Vector3 from, Vector3 to, Color color, Material material)
    {
        var line = new GameObject("Line").AddComponent<LineRenderer>();
        line.transform.SetParent(parent, false);
        line.sharedMaterial = material;
        line.textureMode = LineTextureMode.Stretch;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        line.SetPropertyBlock(block);
        return line;
    }

    private void Update()
    {
        if (held)
        {
            // A steady roar of light, flickering a little, throwing sparks where it ends
            float hw = width * (1.1f + Mathf.Sin(Time.time * 40f) * 0.08f);
            line.widthMultiplier = hw * 1.6f;
            core.widthMultiplier = hw * 0.35f;
            sparkTimer -= Time.deltaTime;
            if (sparkTimer <= 0f)
            {
                sparkTimer = 0.1f;
                Vfx.Burst(line.GetPosition(1), HeimdallFx.Gold, 4, 5f, 0.14f);
            }
            return;
        }
        t += Time.deltaTime;
        // Snaps out wide, then narrows to nothing
        float k = t / Life;
        float w = width * (k < 0.15f ? Mathf.Lerp(0.3f, 1.3f, k / 0.15f) : Mathf.Lerp(1.3f, 0f, (k - 0.15f) / 0.85f));
        line.widthMultiplier = w * 1.6f;
        core.widthMultiplier = w * 0.35f;
    }
}

public static class BifrostBeam
{
    public static void Fire(Vector3 from, Vector3 to)
    {
        var go = new GameObject("BifrostBeam");
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = Vfx.Material("beam", additive: true);
        line.textureMode = LineTextureMode.Stretch;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to + Vector3.up * 0.5f);
        line.widthMultiplier = 4.5f;
        // Line colours can't go past white, so the glow beyond it comes from the material colour
        var hot = new MaterialPropertyBlock();
        hot.SetColor("_BaseColor", Color.white * 2.2f);
        line.SetPropertyBlock(hot);
        // A white-hot core down the middle of the rainbow
        var core = new GameObject("BeamCore").AddComponent<LineRenderer>();
        core.transform.SetParent(go.transform, false);
        core.sharedMaterial = line.sharedMaterial;
        core.positionCount = 2;
        core.SetPosition(0, from);
        core.SetPosition(1, to + Vector3.up * 0.5f);
        core.widthMultiplier = 1.4f;
        core.startColor = core.endColor = Color.white;
        core.SetPropertyBlock(hot);
        Vfx.Flash(from, 5f, Color.white, 0.4f);
        Vfx.Flash(to + Vector3.up, 7f, new Color(0.9f, 0.85f, 1f), 0.5f);
        Vfx.Burst(to + Vector3.up, new Color(1f, 0.95f, 0.8f), 40, 12f, 0.3f);
        HeimdallFx.Impact(to + Vector3.up, 8f, Color.white);
        HeimdallFx.Impact(from, 5f, new Color(0.9f, 0.85f, 1f));
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.3f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.9f, 0.3f), 0.3f),
                    new GradientColorKey(new Color(0.3f, 1f, 0.5f), 0.55f), new GradientColorKey(new Color(0.35f, 0.6f, 1f), 0.8f),
                    new GradientColorKey(new Color(0.8f, 0.4f, 1f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        line.colorGradient = gradient;
        Shockwave.Spawn(to, 3f, Color.white, 24);
        UnityEngine.Object.Destroy(go, 0.8f);
    }
}
