using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Swings the pickaxe at a target: turns to face it, plays the mining swing
// (blocks) or the attack (monsters), and applies the hit at the moment of impact.
// The greatsword (characters with a charged attack): clicks chain a three-blow combo, and its skill 차징 강타 is held
// on the right button to charge a slam (on a short cooldown). Q 휠윈드 spins through everything around (the player
// can walk while spinning), E 검기 sends a blade wave racing along the ground, R 도약 강타 leaps at the cursor and slams
// down; Space is the dash (PlayerMovement), or 돌진 while charging. Its effects are VARCO flipbooks (Resources/Vfx/gs_*)
// and its skill motions VARCO clips baked for the viking (Animations/Viking/GS_*.anim).
public class PlayerMiner : MonoBehaviour
{
    private static readonly int SwingHash = Animator.StringToHash("Swing");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
    private static readonly int ChargeHash = Animator.StringToHash("Charge");
    private static readonly int ChargeSpeedHash = Animator.StringToHash("ChargeSpeed");

    [SerializeField] private Animator animator;
    [SerializeField] private PlayerStats stats;

    [Header("Mining (blocks)")]
    [SerializeField] private float impactDelay = 0.55f;
    [SerializeField] private float swingDuration = 1.1f;

    [Header("Attacking (monsters)")]
    [SerializeField] private float attackImpactDelay = 0.7f;  // slash lands ~1.05s into the clip, played at 1.5x
    [SerializeField] private float attackDuration = 1.0f;
    [SerializeField] private float frontArc = 140f;           // degrees in front of the player a free swing covers

    [SerializeField] private float digRange = 1.5f; // from the player's center to the target's nearest surface, ignoring height
    [SerializeField] private float chainRange = 4f; // Thor's lightning jumps this far

    [Header("차징 강타 (characters whose profile allows it)")]
    [SerializeField] private float chargeCooldown = 3f;      // from the end of one slam until the next charge can start
    [SerializeField] private float stageOneTime = 0.8f;      // held time for the first charge stage
    [SerializeField] private float stageTwoTime = 1.7f;      // held time for the full charge
    [SerializeField] private float raiseSpeed = 0.6f;        // the raise plays slower than a swing: it's heavy
    [SerializeField] private float[] stageDamage = { 1.3f, 2f, 3.2f };
    [SerializeField] private float shockRadius = 3.5f;
    [SerializeField] private float shockDamage = 1f;
    [SerializeField] private float shockRadiusFull = 5f;
    [SerializeField] private float shockDamageFull = 1.8f;
    [SerializeField] private float knockdownSeconds = 2f;

    [Header("Greatsword skills")]
    [SerializeField] private float plungeDamageBonus = 1.3f;  // 낙하: full-charge blade damage
    [SerializeField] private float comboDamage = 0.7f;        // 연속 강타: the second slam
    [SerializeField] private float rushDistance = 6f;         // 돌진
    [SerializeField] private float rushTime = 0.25f;
    [SerializeField] private float rushDamage = 0.8f;
    [SerializeField] private float resolveThreshold = 15f;    // 결의: hits weaker than this don't break the charge

    [Header("Q 휠윈드")]
    [SerializeField] private float whirlwindCooldown = 8f;     // counted from the end of the spin
    [SerializeField] private float whirlwindTime = 2.4f;       // three turns of the clip
    [SerializeField] private float whirlwindRadius = 2.6f;
    [SerializeField] private float whirlwindTick = 0.3f;
    [SerializeField] private float whirlwindDamage = 0.45f;    // per tick
    [SerializeField] private float whirlwindMoveSpeed = 0.6f;  // walking pace while spinning

    [Header("E 검기")]
    [SerializeField] private float waveCooldown = 4f;
    [SerializeField] private float waveRange = 13f;
    [SerializeField] private float waveSpeed = 22f;
    [SerializeField] private float waveWidth = 3f;
    [SerializeField] private float waveDamage = 1.6f;

    [Header("R 도약 강타")]
    [SerializeField] private float leapCooldown = 10f;
    [SerializeField] private float leapRange = 7f;
    [SerializeField] private float leapRadius = 4f;
    [SerializeField] private float leapDamage = 2.2f;
    [SerializeField] private float leapKnockdown = 1.5f;
    [SerializeField] private float leapLiftAt = 0.22f;         // GS_LeapSlam: leaves the ground...
    [SerializeField] private float leapLandAt = 0.95f;         // ...and the blade hits it (clip seconds)

    [Header("Greatsword combo (taps)")]
    [SerializeField] private float comboWindow = 0.5f;        // after a blow, how long the next tap still continues the combo
    [SerializeField] private float lungeDistance = 0.45f;     // each blow steps into it
    [SerializeField] private float blowGap = 0.25f;           // pause after each blow before the next can start, so mashing doesn't machine-gun

    [Header("Greatsword weight")]
    [SerializeField] private float windupPace = 0.7f;         // the blade is heavy to lift: the wind-up plays slower...
    [SerializeField] private float strikePace = 1.4f;         // ...and the swing itself comes down faster
    [SerializeField] private float strikeLead = 0.18f;        // how long before a landing the swing speeds up (animation seconds)
    [SerializeField] private float hitStop = 0.07f;           // the blade bites: the swing freezes this long when it hits something
    [SerializeField] private float finisherHitStop = 0.12f;
    [SerializeField] private float slamHitStop = 0.1f;

    private static readonly Color StageOneColor = new Color(1f, 0.6f, 0.15f);
    private static readonly Color StageTwoColor = new Color(0.55f, 0.85f, 1f);

    // One blow of the greatsword's tap combo: its animator state, the clip's length and the state's own speed,
    // when the blade lands (fractions of the clip), damage per landing, and how wide it sweeps
    private struct ComboBlow
    {
        public string state;
        public float length, stateSpeed;
        public float[] impacts;
        public float damage, arc, extraReach;
    }

    private static readonly ComboBlow[] Combo =
    {
        new ComboBlow { state = "Combo1", length = 1.45f, stateSpeed = 1.8f, impacts = new[] { 0.655f }, damage = 1f, arc = 150f },               // 1: a forward cut
        new ComboBlow { state = "Combo2", length = 2f, stateSpeed = 1.8f, impacts = new[] { 0.225f }, damage = 1f, arc = 150f },                 // 2: a forward cut (the back-swing)
        new ComboBlow { state = "Combo1", length = 1.45f, stateSpeed = 1.9f, impacts = new[] { 0.655f }, damage = 1.05f, arc = 150f },           // 3: a forward cut
        new ComboBlow { state = "Combo3", length = 2f, stateSpeed = 1.5f, impacts = new[] { 0.475f }, damage = 2.1f, arc = 200f, extraReach = 0.5f }, // 4: the heavy overhead blow
    };

    private int comboStep;          // the next blow in the combo
    private float comboExpires;     // a tap after this starts the combo over
    private bool comboQueued;       // tapped during a blow: the next one follows straight on
    private Vector3 comboAim;
    private string recoveringState; // a finished blow whose follow-through can be cut short by moving
    private float nextBlowTime;     // no attack starts before this
    private Coroutine queuedBlow;   // a tap made during the gap, waiting to go

    private bool chargeEnabled;
    private float holdPoint = 0.58f, impactPoint = 0.78f, swingSeconds = 0.93f;
    private bool pressing;
    private bool slamRequested;
    private float pressStart;
    private float heldAtRelease;
    private Vector3 chargeAim;
    private float chargeReadyAt;    // 차징 강타's cooldown
    private float whirlwindReadyAt, waveReadyAt, leapReadyAt;
    private VfxPiece whirlGlow, whirlInk, whirlUnder;
    private ClickToDig aim;
    // While charging: a rune circle turning at the feet, an aura spiralling up round the body (a dark ink copy under it
    // so it reads on white marble) and a glow
    private VfxPiece chargeCircle, chargeAura, chargeAuraInk, chargeGlow, chargeGather;
    private Coroutine chargeRoutine;
    private bool rushedThisCharge;
    private AttackRangeFx rangeFx;

    public bool IsSwinging { get; private set; }
    public bool IsCharging { get; private set; }
    public bool Guarding { get; private set; }   // 단절: incoming hits are blocked
    public bool CanCharge => chargeEnabled;
    public float ChargeHeld => pressing ? Time.time - pressStart : IsCharging ? heldAtRelease : 0f;
    // The charge is quicker than the serialized times (they felt long): ~0.5s to stage one, ~1.05s to full.
    // 분쇄 일격 has a single stage (straight to full); 대지의 심판 gathers a third time (StageThreeTime) for its room-wide blow.
    public float StageOneTime => Sp('M') == 0 ? stageTwoTime * 0.5f : stageOneTime * 0.62f;
    public float StageTwoTime => Sp('M') == 0 ? stageTwoTime * 0.5f : stageTwoTime * 0.62f;
    public float StageThreeTime => StageTwoTime + 0.9f;
    public float ChargeFullTime => Sp('M') == 1 ? StageThreeTime : StageTwoTime;
    private bool thirdStage;   // 대지의 심판: the third gathering reached
    public bool JudgementCharge => Sp('M') == 1;
    public int ChargeStage => !IsCharging ? 0 : ChargeHeld >= StageTwoTime ? 2 : ChargeHeld >= StageOneTime ? 1 : 0;
    public float ChargeCooldown => ChargeCooldownScaled;
    public float ChargeCooldownLeft => Mathf.Max(0f, chargeReadyAt - Time.time);
    public bool Whirling { get; private set; }  // 휠윈드: the player walks (slowly) while spinning
    public float WhirlwindMoveSpeed => whirlwindMoveSpeed;

    // A hit landed on an enemy: (enemy, damage, critical)
    public event Action<Enemy, int, bool> HitEnemy;
    public event Action<int> ChargeStageReached;
    public event Action ChargeBroken;

    private float Reach => digRange + RunState.Stats.rangeBonus;

    public bool CanReach(Component target) => CanReach(target, Reach);

    private bool CanReach(Component target, float reach)
    {
        if (target == null)
            return false;

        Vector3 origin = transform.position;
        Collider targetCollider = target.GetComponent<Collider>();
        Vector3 nearest = targetCollider != null ? targetCollider.ClosestPoint(origin) : target.transform.position;
        Vector2 offset = new Vector2(nearest.x - origin.x, nearest.z - origin.z);
        return offset.magnitude <= reach;
    }

    // ── 특수 강화 (대검 기획서 4~7장): four versions per skill, by the table's rows ──
    public static readonly string[][] SpecialNames =
    {
        new[] { "회오리 일격", "중력 회전", "폭풍 회전", "최후의 일격" },    // Q 휠윈드
        new[] { "삼중 검기", "관통 검기", "대지 절단", "압축 검기" },        // E 검기
        new[] { "연속 도약", "지진 강타", "낙성", "균열 폭발" },             // R 도약 강타
        new[] { "분쇄 일격", "대지의 심판", "회전 강타", "투신의 각오" },    // 우클릭 차징 강타
    };
    public static readonly string[][] SpecialDescs =
    {
        new[] { "계속 도는 대신 한 번 강하게 회전해 주변을 크게 벱니다",
                "회전하는 동안 주변 적을 중심으로 끌어당깁니다",
                "회전이 일정 횟수 진행될 때마다 주변으로 강한 충격파를 발산합니다",
                "끝나는 순간 검을 내려쳐 땅을 가르는 큰 충격을 일으킵니다 (회전 중 공격 횟수 감소)" },
        new[] { "세 갈래 검기를 동시에 발사합니다 (각각 좁고 피해 감소)",
                "검기가 적을 관통하며, 관통할 때마다 피해가 줄어듭니다",
                "검을 땅에 내리쳐 앞으로 땅을 갈라 놓고, 잠시 후 균열을 따라 폭발합니다",
                "응축된 작고 빠른 검기가 첫 번째로 맞은 적에게 피해를 집중합니다" },
        new[] { "착지 후 짧은 시간 안에 한 번 더 도약합니다 (최대 2회, 각 피해 감소)",
                "착지 지점에서 지진파가 둥글게 퍼져 나가며 닿는 적을 칩니다",
                "내려찍힌 적이 넘어져 잠시 움직이지도 공격하지도 못합니다",
                "착지 지점에 균열이 남고 잠시 후 크게 폭발합니다 (착지 직후 바로 행동 가능)" },
        new[] { "차징이 1단계뿐이며, 모은 힘을 실어 강력하게 한 번 내려칩니다",
                "기를 3번 모으면 방 한가운데 거대한 검이 꽂히고, 검을 중심으로 충격파가 퍼져 방 안의 모든 적을 칩니다",
                "회전 공격으로 바뀝니다 (1단계 빠른 1회전, 2단계 묵직한 2회전) · 마지막 회전에 맞은 적을 바깥으로 밀어냅니다",
                "모은 힘을 몸에 담아 공격력이 오르고 받는 피해가 줄어듭니다 (1단계 +25% / -20% 8초, 2단계 +50% / -35% 10초)" },
    };

    private string fighterName;
    private int Sp(char key) => chargeEnabled && fighterName != null ? SkillSystem.SpecialOf(fighterName + "/" + key) : -1;
    private int leapChainLeft;
    private float leapChainUntil;

    private void Awake()
    {
        SkillSystem.RegisterSpecials("Model_Viking", SpecialNames, SpecialDescs);
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (stats == null)
            stats = GetComponent<PlayerStats>();
    }

    // Made on first use (also after a script reload in Play, which forgets it)
    private AttackRangeFx RangeFx => rangeFx != null ? rangeFx : rangeFx = AttackRangeFx.Create(transform, frontArc);

    // Switching characters swaps the animator and the swing timings
    public void SetCharacter(CharacterProfile profile)
    {
        animator = profile.Animator;
        fighterName = profile.name;
        impactDelay = profile.ImpactDelay;
        swingDuration = profile.SwingDuration;
        attackImpactDelay = profile.AttackImpactDelay;
        attackDuration = profile.AttackDuration;
        chargeEnabled = profile.ChargeAttack;
        GreatswordSkills.Active = chargeEnabled;   // the greatsword tree's stats only count while it is in hand
        holdPoint = profile.ChargeHoldPoint;
        impactPoint = profile.ChargeImpactPoint;
        swingSeconds = profile.SwingStateSeconds;
    }

    private float AttackSpeed => (stats != null ? stats.AttackSpeed : 1f) * PlayerEffects.Slow;

    // Base damage from the runes, with a chance to crit
    // (kept exact: each weapon rounds its blow once, at the end)
    private float RollDamage(out bool critical)
    {
        float damage = stats != null ? stats.DamageExact : 1f;
        critical = UnityEngine.Random.value < RunState.Stats.critChance;
        if (critical)
            damage *= RunState.Stats.critMultiplier;
        return damage;
    }

    public void SwingAt(CaveBlock block)
    {
        SwingAt(block, SwingHash, impactDelay, swingDuration, () => block.Hit(Mathf.Max(1, Mathf.RoundToInt(RollDamage(out _)))));
    }

    // Attack toward a point even with no target: whatever enemy is in reach in front gets hit
    public void SwingFree(Vector3 aimPoint)
    {
        // The greatsword chains taps into its three-blow combo
        if (chargeEnabled)
        {
            ComboAttack(aimPoint);
            return;
        }
        if (IsSwinging || Time.time < nextBlowTime)
            return;

        float speed = AttackSpeed;
        nextBlowTime = Time.time + (attackDuration + blowGap) / speed;
        if (animator != null)
            animator.SetFloat(AttackSpeedHash, speed);
        RangeFx.Windup(Reach, attackImpactDelay / speed);
        StartCoroutine(Swing(aimPoint, AttackHash, attackImpactDelay / speed, attackDuration / speed, () =>
        {
            RangeFx.Flash(Reach, AttackRangeFx.SwingColor);
            ChainLightning(HitFront(1f));
        }));
    }

    // ── Greatsword combo: tap, tap, tap = sweep, double slash, overhead finisher ──

    private void ComboAttack(Vector3 aimPoint)
    {
        // Mid-blow or in the gap after one: remember the tap, it goes as soon as the gap is over
        if (IsSwinging || Time.time < nextBlowTime)
        {
            comboQueued = true;
            comboAim = aimPoint;
            if (!IsSwinging && queuedBlow == null)
                queuedBlow = StartCoroutine(QueuedBlow());
            return;
        }
        if (Time.time > comboExpires || comboStep >= Combo.Length)
            comboStep = 0;
        StartCoroutine(ComboSwing(aimPoint, comboStep));
    }

    // The tap made during a blow (or the gap after it) plays once the gap is over
    private IEnumerator QueuedBlow()
    {
        while (Time.time < nextBlowTime)
            yield return null;
        queuedBlow = null;
        if (!comboQueued || IsSwinging)
            yield break;
        comboQueued = false;
        ComboAttack(comboAim);
    }

    private IEnumerator ComboSwing(Vector3 aimPoint, int step)
    {
        GameAudio.Play(step == 0 ? "gs_combo1" : step == 1 ? "gs_combo2" : "gs_finisher");   // (the greatsword's three blows, each its own)
        ComboBlow blow = Combo[step];
        IsSwinging = true;
        comboQueued = false;
        recoveringState = null;

        Vector3 toAim = aimPoint - transform.position;
        toAim.y = 0f;
        if (toAim.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(toAim);

        float speed = AttackSpeed;
        float seconds = blow.length / (blow.stateSpeed * speed);
        float firstImpact = blow.impacts[0] * seconds;
        float lastImpact = blow.impacts[blow.impacts.Length - 1] * seconds;
        // The next blow may start just after this one's last landing (the finisher commits a little longer)
        float free = lastImpact + (step == Combo.Length - 1 ? 0.3f : 0.1f) / speed;
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, speed * windupPace);
            animator.CrossFadeInFixedTime(blow.state, 0.06f, 0, 0f);
        }
        float lead = Mathf.Min(strikeLead / speed, firstImpact);
        RangeFx.Windup(Reach + blow.extraReach, (firstImpact - lead) / windupPace + lead / strikePace);

        var controller = GetComponent<CharacterController>();
        bool finisher = step == Combo.Length - 1;
        int landed = 0;
        float stopLeft = 0f;
        // t runs in animation time: slow through the wind-up, fast through the strike, frozen for a moment on a hit
        for (float t = 0f; t < free; )
        {
            float nextLanding = landed < blow.impacts.Length ? blow.impacts[landed] * seconds : -1f;
            float pace = stopLeft > 0f ? 0f
                : nextLanding < 0f ? 1f
                : t < nextLanding - lead ? windupPace
                : strikePace;
            stopLeft -= Time.deltaTime;
            if (animator != null)
                animator.SetFloat(AttackSpeedHash, speed * pace);
            float advance = Time.deltaTime * pace;

            // Step into each blow over the moment before it lands
            if (controller != null && nextLanding > 0f && t > nextLanding - 0.15f)
                controller.Move(transform.forward * (lungeDistance / 0.15f) * advance);
            if (landed < blow.impacts.Length && t >= nextLanding)
            {
                landed++;
                if (LandComboBlow(blow, step, landed - 1) > 0)
                    stopLeft = finisher ? finisherHitStop : hitStop;
            }
            t += advance;
            yield return null;
        }
        if (animator != null)
            animator.SetFloat(AttackSpeedHash, speed);

        IsSwinging = false;
        recoveringState = blow.state;
        // All four blows, then the combo starts over
        comboStep = (step + 1) % Combo.Length;
        nextBlowTime = Time.time + blowGap / speed;
        comboExpires = nextBlowTime + comboWindow;
        // A tap came in during the blow: it follows after the gap
        if (comboQueued && queuedBlow == null)
            queuedBlow = StartCoroutine(QueuedBlow());
    }

    // How each landing's slash of light lies: tilt of its plane (0 level, 90 overhead) and which way it sweeps
    private static readonly (float tilt, float sweep)[][] ComboSlashes =
    {
        new[] { (0f, 70f) },                    // level sweep, left to right
        new[] { (15f, -70f) },                  // back across
        new[] { (-10f, 70f) },                  // forward again
        new[] { (60f, 50f) },                   // down from on high (not fully upright, so the camera above still sees it)
    };

    // Returns how many enemies the blow struck
    private int LandComboBlow(ComboBlow blow, int step, int landing)
    {
        bool finisher = step == Combo.Length - 1;
        Color color = finisher ? StageOneColor : AttackRangeFx.SwingColor;
        RangeFx.Flash(Reach + blow.extraReach, color);
        var (tilt, sweep) = ComboSlashes[step][Mathf.Min(landing, ComboSlashes[step].Length - 1)];
        Cleave(tilt, sweep < 0f, (Reach + blow.extraReach) * (finisher ? 4.2f : 3.8f), color * 2f, finisher ? 0.36f : 0.3f);
        List<Enemy> struck = HitFront(blow.damage, blow.arc, blow.extraReach);
        if (finisher)
        {
            // The overhead blow cracks the floor: a small shockwave catches whatever the blade missed
            Vector3 impact = transform.position + transform.forward * 1.2f;
            Quake(impact, 4.2f, StageOneColor * 1.6f, 0.5f);
            Vfx.Dust(new Vector3(impact.x, Feet, impact.z), 1.8f, 12);
            Vfx.Flash(new Vector3(impact.x, Feet + 0.3f, impact.z), 2.4f, StageOneColor);
            CameraShake.Shake(0.26f, 0.2f);
            Wave(2.6f, 0.6f, struck, false);
        }
        // A blow that bites shakes harder than one that cuts air
        else if (struck.Count > 0)
            CameraShake.Shake(0.14f, 0.1f);
        else
            CameraShake.Shake(0.06f, 0.05f);
        ChainLightning(struck);
        return struck.Count;
    }

    // ── The greatsword's painted VARCO effects ──

    private static readonly Color InkColor = new Color(0.16f, 0.08f, 0.02f, 0.75f);
    private float Feet => transform.position.y - 0.92f;

    // Until a greatsword sheet is in Resources/Vfx, the nearest of the twin blades' VARCO sheets stands in
    // (null: nothing fits, the effect is left out) — a missing texture would otherwise draw as a plain square
    private static readonly Dictionary<string, string> StandIns = new Dictionary<string, string>
    {
        { "gs_cleave", "slashsheet" }, { "gs_quake", "roarsheet" }, { "gs_aura", "auraflame" },
        { "gs_runecircle", null }, { "gs_pillar", null },
    };
    // (Resources.Load caches loaded textures itself; a miss isn't remembered, so a sheet added later is picked up)
    private static string SheetName(string name) => Resources.Load<Texture2D>("Vfx/" + name) != null ? name : StandIns[name];

    // A heavy crescent of light sweeping through the swing's plane (tilt 0 level, 90 upright), bulging toward the enemy;
    // mirrored for a swing running the other way
    private void Cleave(float tilt, bool mirror, float size, Color color, float seconds)
    {
        Vector3 fwd = transform.forward;
        Quaternion plane = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 0f, tilt) * Quaternion.Euler(90f, 0f, 0f)
            * Quaternion.Euler(0f, mirror ? 180f : 0f, 0f) * Quaternion.Euler(0f, 0f, -90f);
        DualBlades.Cut(SheetName("gs_cleave"), transform.position + Vector3.up * 0.3f + fwd * (size * 0.28f), size, color, seconds, 0.9f, 1.1f, 0f,
            plane, sheet: 4, inkColor: InkColor);
    }

    // The floor cracking open in a ring where the blade lands
    private void Quake(Vector3 at, float size, Color color, float seconds)
    {
        DualBlades.Cut(SheetName("gs_quake"), new Vector3(at.x, Feet + 0.06f, at.z), size, color, seconds, 0.55f, 1.15f, 0f,
            Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f), sheet: 4, inkColor: InkColor);
    }

    // ── The greatsword's own VARCO sheets (2026-11-19, white on black, tinted here) ──
    private static bool HasSheet(string name) => Resources.Load<Texture2D>("Vfx/" + name) != null;

    // A painted effect from its own sheet (or the fallback while it's missing): `ink` > 0 lays a faint dark copy under it so it
    // reads on the white marble (kept light: a big one mustn't darken the map). Returns the glowing piece (null when nothing fits).
    private VfxPiece GsFx(string sheet, string fallback, Vector3 at, float size, Color color, float seconds, float s0, float s1,
        float spin = 0f, Quaternion? lying = null, float ink = 0f)
    {
        string tex = HasSheet(sheet) ? sheet : fallback;
        if (tex == null || !HasSheet(tex))
            return null;
        if (ink > 0f && HasSheet(tex + "_ink"))
            return DualBlades.Cut(tex, at, size, color, seconds, s0, s1, spin, lying, 4, new Color(InkColor.r, InkColor.g, InkColor.b, ink));
        return Vfx.Sprite(tex, at, size, color, seconds, s0, s1, spin, lying).Sheet(4, 4);
    }

    private static Quaternion Flat(float yaw) => Quaternion.Euler(90f, yaw, 0f);
    private static Quaternion FlatRandom => Quaternion.Euler(90f, UnityEngine.Random.Range(0f, 360f), 0f);
    private static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    private Vector3 Floor(Vector3 p, float lift = 0.06f) => new Vector3(p.x, Feet + lift, p.z);

    // A heavy blow landing on the floor: the crater, a dust shockwave behind it, a flash and a shake
    private void HeavyImpact(Vector3 at, float size, Color color, float shake, float ink = 0.45f)
    {
        GsFx("gs_crater", "gs_quake", Floor(at), size, color * (ink > 0.6f ? 2.8f : 2.4f), 0.65f, 0.7f, 1.05f, 0f, FlatRandom, ink);
        GsFx("gs_shockdust", null, Floor(at, 0.09f), size * 1.25f, Color.Lerp(color, Color.white, 0.45f) * 1.6f, 0.75f, 0.45f, 1.1f, 0f, FlatRandom,
            ink > 0.6f ? 0.5f : 0f);
        Vfx.Flash(Floor(at, 0.5f), size * 0.45f, color);
        Vfx.Dust(Floor(at, 0f), size * 0.3f, 18);
        CameraShake.Shake(shake, shake * 0.85f);
    }

    // The ground bursting upward on the spot (the eruption stands up, the rupture lies on the floor)
    private void Eruption(Vector3 at, float size, Color color)
    {
        GsFx("gs_rupture", "gs_quake", Floor(at, 0.08f), size * 1.3f, color * 2.2f, 0.7f, 0.6f, 1.1f, 0f, FlatRandom, 0.4f);
        GsFx("gs_eruption", "hd_burst", Floor(at, size * 0.4f), size, color * 2f, 0.75f, 0.85f, 1.1f);
        Vfx.Burst(Floor(at, 0.4f), color, 16, 9f, 0.22f, 2.5f);
        Vfx.Dust(Floor(at, 0f), size * 0.35f, 10);
    }

    // 낙성: struck enemies are stunned — no moving or attacking — with stars circling over their heads while it lasts
    private void StunEnemy(Enemy e, float seconds)
    {
        if (e == null || e.IsDead)
            return;
        e.Knockdown(seconds);
        float head = 2.1f * Mathf.Max(0.6f, e.transform.localScale.y);
        var stars = GsFx("gs_stun", "marksheet", e.transform.position + Vector3.up * head, 1.5f, new Color(1f, 0.9f, 0.45f) * 2f, seconds, 1f, 1f);
        if (stars == null)
            return;
        stars.Hold();
        stars.transform.SetParent(e.transform, true);
    }

    // A pillar of lightning crashing down on the spot (full charge)
    private void Pillar(Vector3 at, float size)
    {
        if (SheetName("gs_pillar") == null)
            return;
        DualBlades.Cut("gs_pillar", new Vector3(at.x, Feet + size * 0.45f, at.z), size, StageTwoColor * 2f, 0.45f, 0.95f, 1.05f, 0f,
            sheet: 4, inkColor: new Color(0.02f, 0.06f, 0.14f, 0.7f));
    }

    // ── Q 휠윈드 · E 검기 · R 도약 강타 ──

    public float WhirlwindCooldownLeft => Mathf.Max(0f, whirlwindReadyAt - Time.time);
    public float WaveCooldownLeft => Mathf.Max(0f, waveReadyAt - Time.time);
    public float LeapCooldownLeft => Mathf.Max(0f, leapReadyAt - Time.time);
    public float WhirlwindCooldown => whirlwindCooldown * GreatswordSkills.CooldownScale;
    public float WaveCooldown => waveCooldown * GreatswordSkills.CooldownScale;
    public float LeapCooldown => leapCooldown * GreatswordSkills.CooldownScale;
    private float ChargeCooldownScaled => chargeCooldown * GreatswordSkills.CooldownScale;

    // Each skill is learned in the skill tree first; pressing a locked one says where to learn it
    private float lockedNoticeAt;

    private bool Learned(string node)
    {
        if (GreatswordSkills.Has(node))
            return true;
        if (Time.time >= lockedNoticeAt)
        {
            lockedNoticeAt = Time.time + 1f;
            DualBlades.Say(transform.position + Vector3.up * 2f, "아직 쓸 수 없습니다", new Color(0.75f, 0.75f, 0.8f));
        }
        return false;
    }

    private Vector3 CursorPoint()
    {
        if (aim == null)
            aim = FindAnyObjectByType<ClickToDig>();
        return SkillCast.AimOverride ?? (aim != null ? aim.CursorGroundPoint() : transform.position + transform.forward * 3f);
    }

    private void SkillKeys()
    {
        Keyboard keyboard = Keyboard.current;
        // (Q / E / R are skill orbs now: fired from whichever slot holds them)
        if (keyboard == null || !GameManager.IsRunActive || Time.timeScale <= 0f || IsSwinging || IsCharging)
            return;
        if (SkillSystem.KeyDown(keyboard, 'Q') && Learned(GreatswordSkills.Whirlwind) && WhirlwindCooldownLeft <= 0f)
            StartCoroutine(Whirlwind());
        else if (SkillSystem.KeyDown(keyboard, 'E') && Learned(GreatswordSkills.Wave) && WaveCooldownLeft <= 0f)
            StartCoroutine(SwordWave(CursorPoint()));
        else if (SkillSystem.KeyDown(keyboard, 'R') && Learned(GreatswordSkills.Leap) && LeapCooldownLeft <= 0f)
            StartCoroutine(LeapSlam(CursorPoint()));
    }

    // Starting a skill ends the combo
    private void BeginSkill()
    {
        IsSwinging = true;
        recoveringState = null;
        comboStep = 0;
        comboQueued = false;
        if (queuedBlow != null)
        {
            StopCoroutine(queuedBlow);
            queuedBlow = null;
        }
    }

    // Q: spin with the blade held out, cutting everything around every tick; WASD still walks (slowly)
    // The whirlwind's blades keep singing while it spins
    private IEnumerator WhirlSound()
    {
        while (Whirling)
        {
            GameAudio.Play("gs_whirl", null, 0.9f);
            yield return new WaitForSeconds(1.15f);
        }
    }

    private IEnumerator Whirlwind()
    {
        BeginSkill();
        int sp = Sp('Q');
        if (sp == 0)
        {
            yield return SingleSpin();
            yield break;
        }
        Whirling = true;
        StartCoroutine(WhirlSound());
        // 피의 회오리: wider and longer, and every bite heals
        bool bloodspin = GreatswordSkills.Has(GreatswordSkills.Bloodspin);
        float radius = whirlwindRadius + (bloodspin ? 0.8f : 0f);
        float spinTime = whirlwindTime * (bloodspin ? 1.25f : 1f);
        whirlwindReadyAt = Time.time + spinTime + WhirlwindCooldown;
        float speed = AttackSpeed;
        // 최후의 일격: fewer cuts, then one great blow
        float tickEvery = whirlwindTick * (sp == 3 ? 2f : 1f);
        float tickDamage = whirlwindDamage * (sp == 3 ? 0.6f : 1f);
        int ticks = 0;
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, speed);
            animator.CrossFadeInFixedTime("Whirlwind", 0.12f);
        }
        // (a warm gold, not the deep orange: over its faint shadow that turned pink)
        Color spinColor = bloodspin ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.78f, 0.38f);
        // A storm of heavy blade light turning at the feet (the VARCO spin sheet, the old swirl faint under it), and a burst
        // of dust as it starts
        Vector3 at = new Vector3(transform.position.x, Feet + 0.3f, transform.position.z);
        float size = radius * 2.5f;
        string spinSheet = HasSheet("gs_spin2") ? "gs_spin2" : "gs_whirlwind";
        whirlInk = Vfx.Sprite(HasSheet(spinSheet + "_ink") ? spinSheet + "_ink" : "gs_whirlwind_ink", at, size, new Color(InkColor.r, InkColor.g, InkColor.b, 0.45f), 0f, 1f, 1f, 540f,
            Quaternion.Euler(90f, 0f, 0f), additive: false).Sheet(4, 4).OpenUp(0.15f);
        whirlGlow = Vfx.Sprite(spinSheet, at, size, spinColor * 2.1f, 0f, 1f, 1f, 600f, Quaternion.Euler(90f, 0f, 0f)).Sheet(4, 4).OpenUp(0.15f);
        whirlGlow.GetComponent<Renderer>().sortingOrder = 1;
        whirlUnder = spinSheet != "gs_whirlwind"
            ? Vfx.Sprite("gs_whirlwind", at, size * 0.85f, spinColor * 1.1f, 0f, 1f, 1f, 420f, Quaternion.Euler(90f, 0f, 0f)).Sheet(4, 4).OpenUp(0.15f) : null;
        GsFx("gs_shockdust", null, Floor(transform.position, 0.08f), radius * 2.6f, Color.Lerp(spinColor, Color.white, 0.4f) * 1.4f, 0.55f, 0.4f, 1f, 0f, FlatRandom);
        float nextTick = 0.1f;
        for (float t = 0f; t < spinTime / speed; t += Time.deltaTime)
        {
            at = new Vector3(transform.position.x, Feet + 0.3f, transform.position.z);
            whirlGlow.transform.position = whirlInk.transform.position = at;
            if (whirlUnder != null) whirlUnder.transform.position = at + Vector3.down * 0.02f;
            // 중력 회전: everything nearby is dragged in toward the blade
            if (sp == 1)
                foreach (Enemy e in Enemy.Alive)
                {
                    if (e == null || e.IsDead || e.IsBoss) continue;
                    Vector3 pull = transform.position - e.transform.position;
                    pull.y = 0f;
                    if (pull.magnitude < radius + 3.5f && pull.magnitude > 1.2f)
                        e.transform.position += pull.normalized * Mathf.Min(4.5f * Time.deltaTime, pull.magnitude - 1.1f);
                }
            if (t >= nextTick)
            {
                nextTick += tickEvery / speed;
                ticks++;
                var struck = new List<Enemy>();
                Wave(radius, tickDamage, struck, false);
                // 폭풍 회전: every fourth turn a storm shockwave bursts out — it hits harder and throws enemies back
                if (sp == 2 && ticks % 4 == 0)
                {
                    float reach = radius + 2.6f;
                    GsFx("gs_stormring", "gs_quake", Floor(transform.position, 0.12f), reach * 2.4f, StageOneColor * 2.4f, 0.6f, 0.35f, 1.1f, 160f, FlatRandom, 0.35f);
                    GsFx("gs_shockdust", null, Floor(transform.position, 0.09f), reach * 2.2f, new Color(1f, 0.9f, 0.7f) * 1.5f, 0.7f, 0.3f, 1.15f, 0f, FlatRandom);
                    Shockwave.Spawn(transform.position, reach, StageOneColor, 26);
                    Vfx.Dust(Floor(transform.position, 0f), reach * 0.5f, 14);
                    var shocked = new List<Enemy>();
                    Wave(reach, 1.1f, shocked, false);
                    foreach (Enemy e in shocked)
                    {
                        if (e == null || e.IsDead || e.IsBoss) continue;
                        Vector3 away = e.transform.position - transform.position;
                        away.y = 0f;
                        if (away.sqrMagnitude > 0.01f) e.transform.position += away.normalized * 1.2f;
                    }
                    CameraShake.Shake(0.24f, 0.2f);
                }
                if (struck.Count > 0)
                {
                    CameraShake.Shake(0.08f, 0.06f);
                    if (bloodspin && stats != null)
                        stats.Heal(1f);
                }
                ChainLightning(struck);
                Vfx.Dust(new Vector3(at.x, Feet, at.z), radius * 0.6f, 4);
            }
            yield return null;
        }
        EndWhirlwind();
        Quake(transform.position, radius * 2f, StageOneColor * 1.5f, 0.4f);
        // 최후의 일격: the blade raised and brought down at the end — a vertical cut of light, the ground torn open in a star
        // of cracks, eruptions round it
        if (sp == 3)
        {
            if (animator != null) { animator.SetFloat(AttackSpeedHash, 1.4f); animator.CrossFadeInFixedTime("Combo3", 0.05f, 0, 0.3f); }
            yield return new WaitForSeconds(0.3f);
            Vector3 hitAt = transform.position + transform.forward * 1.2f;
            GsFx("gs_downslash", "slashsheet", Floor(hitAt, 1.6f), 4.2f, Color.white * 2.4f, 0.4f, 0.9f, 1.05f);
            GsFx("gs_finalblow", "gs_quake", Floor(hitAt, 0.1f), 10f, StageOneColor * 2.6f, 1.1f, 0.6f, 1.05f, 0f, FlatRandom, 0.5f);
            HeavyImpact(hitAt, 6f, StageOneColor, 0.42f);
            Pillar(hitAt, 4.6f);
            for (int i = 0; i < 5; i++)
            {
                float a = (i * 72f + UnityEngine.Random.Range(-15f, 15f)) * Mathf.Deg2Rad;
                Eruption(hitAt + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3.2f, 2.4f, StageOneColor);
            }
            Shockwave.Spawn(transform.position, 5.5f, StageOneColor, 32);
            var big = new List<Enemy>();
            Wave(5.5f, 3.2f, big, true);
            ChainLightning(big);
            yield return new WaitForSeconds(0.35f);
        }
        if (animator != null)
            animator.CrossFadeInFixedTime("Locomotion", 0.2f);
        IsSwinging = false;
    }

    private void EndWhirlwind()
    {
        Whirling = false;
        if (whirlGlow != null) Destroy(whirlGlow.gameObject);
        if (whirlInk != null) Destroy(whirlInk.gameObject);
        if (whirlUnder != null) Destroy(whirlUnder.gameObject);
    }

    // 회오리 일격: no walking spin — the blade wound back and swung round once, hard: one great ring cut round the player
    private IEnumerator SingleSpin()
    {
        GameAudio.Play("gs_whirl");
        float speed = AttackSpeed;
        whirlwindReadyAt = Time.time + 0.8f + WhirlwindCooldown;
        bool bloodspin = GreatswordSkills.Has(GreatswordSkills.Bloodspin);
        float radius = whirlwindRadius + 1.2f + (bloodspin ? 0.8f : 0f);
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, speed * 0.55f);   // (a heavy wind-up...)
            animator.CrossFadeInFixedTime("Whirlwind", 0.08f, 0, 0f);
        }
        Vfx.Dust(Floor(transform.position, 0f), 1.2f, 8);
        yield return new WaitForSeconds(0.18f / speed);
        if (animator != null) animator.SetFloat(AttackSpeedHash, speed * 1.5f);   // (...then the turn tears round)
        Vector3 at = transform.position;
        GsFx("gs_heavyspin", "spinsheet", Floor(at, 0.35f), radius * 2.3f, StageOneColor * 2.4f, 0.45f, 0.85f, 1.1f, 360f, FlatRandom, 0.4f);
        GsFx("gs_spinburst", "gs_whirlwind", Floor(at, 0.3f), radius * 2.6f, Color.Lerp(StageOneColor, Color.white, 0.35f) * 2.2f, 0.6f, 0.6f, 1.12f, 0f, FlatRandom, 0.35f);
        GsFx("gs_shockdust", null, Floor(at, 0.08f), radius * 2.8f, new Color(1f, 0.9f, 0.7f) * 1.5f, 0.7f, 0.4f, 1.1f, 0f, FlatRandom);
        Shockwave.Spawn(at, radius, StageOneColor, 26);
        Vfx.Dust(Floor(at, 0f), radius * 0.6f, 16);
        CameraShake.Shake(0.3f, 0.24f);
        var struck = new List<Enemy>();
        Wave(radius, 2.6f, struck, false);
        foreach (Enemy e in struck)
        {
            if (e == null || e.IsDead || e.IsBoss) continue;
            Vector3 away = e.transform.position - at;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f) e.transform.position += away.normalized * 1.4f;
        }
        if (bloodspin && stats != null && struck.Count > 0) stats.Heal(2f * struck.Count);
        ChainLightning(struck);
        yield return new WaitForSeconds(0.4f / speed);
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, speed);
            animator.CrossFadeInFixedTime("Locomotion", 0.2f);
        }
        IsSwinging = false;
    }

    // E: one big slash that throws a wave of blade light along the ground; it cuts through every enemy in its path
    private IEnumerator SwordWave(Vector3 aimPoint)
    {
        GameAudio.Play("gs_wave");
        BeginSkill();
        waveReadyAt = Time.time + WaveCooldown;
        float rift = GreatswordSkills.Has(GreatswordSkills.Rift) ? 1.3f : 1f;   // 대지 가르기: further and harder
        Face(aimPoint, 99999f);
        float speed = AttackSpeed;
        int sp = Sp('E');
        // 대지 절단: its own motion — the greatsword hauled overhead and driven into the floor, the ground split open ahead
        if (sp == 2)
        {
            yield return EarthSplitter(speed, rift);
            yield break;
        }
        // the wave's own swing (the original slash)
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, speed);
            animator.CrossFadeInFixedTime("Attack", 0.08f, 0, 0f);
        }
        RangeFx.Windup(Reach, attackImpactDelay / speed);
        yield return new WaitForSeconds(attackImpactDelay / speed);
        Vector3 fwd = transform.forward;
        Cleave(10f, false, Reach * 3.6f, StageOneColor * 2f, 0.3f);
        ChainLightning(HitFront(1f));
        Vector3 from = transform.position + fwd * 0.9f;
        switch (sp)
        {
            case 0:   // 삼중 검기: three narrower waves fanned out, each flaring as it leaves the blade
                foreach (float a in new[] { -22f, 0f, 22f })
                {
                    Vector3 d = Quaternion.Euler(0f, a, 0f) * fwd;
                    GreatswordWave.Fire(this, from, Feet + 0.55f, d, waveRange * rift, waveWidth * 0.75f, waveSpeed * 1.1f, waveDamage * rift * 0.6f);
                    Vfx.Flash(Floor(from + d * 0.8f, 0.5f), 1.6f, StageOneColor, 0.15f);
                }
                break;
            case 1:   // 관통 검기: through every enemy in the line, weaker after each
                GreatswordWave.Fire(this, from, Feet + 0.55f, fwd, waveRange * rift * 1.2f, waveWidth * 0.8f, waveSpeed, waveDamage * rift, pierce: true, decay: 0.8f);
                break;
            case 3:   // 압축 검기: small, fast, everything on the first one hit — its own condensed blade
                GreatswordWave.Fire(this, from, Feet + 0.55f, fwd, waveRange * rift * 1.3f, waveWidth * 0.6f, waveSpeed * 1.9f, waveDamage * rift * 2.4f, look: "gs_compress");
                break;
            default:  // (the plain wave stops at the first enemy it cuts: 관통 is its special)
                GreatswordWave.Fire(this, from, Feet + 0.55f, fwd, waveRange * rift, waveWidth, waveSpeed, waveDamage * rift);
                break;
        }
        CameraShake.Shake(0.14f, 0.1f);
        yield return new WaitForSeconds(Mathf.Max(0.1f, attackDuration - attackImpactDelay) / speed * 0.6f);
        IsSwinging = false;
        recoveringState = "Attack";
    }

    // 대지 절단: the blade raised overhead and driven down into the floor; from where it bites, the ground cracks open
    // straight ahead — a jagged fissure running out, catching what it passes under — then bursts all along its length.
    // (no wave of light: the crack itself is the blow)
    private IEnumerator EarthSplitter(float speed, float rift)
    {
        const float raise = 2.4f, from0 = 0.1f;
        float toImpact = Mathf.Max(0.12f, (impactPoint - from0) * swingSeconds / (raise * speed));
        if (animator != null)
        {
            animator.SetFloat(ChargeSpeedHash, raise * speed);
            animator.CrossFadeInFixedTime("Charge", 0.06f, 0, from0 * swingSeconds / 1.5f);
        }
        RangeFx.Windup(Reach, toImpact);
        yield return new WaitForSeconds(toImpact);
        if (animator != null) animator.SetFloat(ChargeSpeedHash, 0f);   // (the blade bites the floor for a beat)
        Vector3 fwd = transform.forward;
        Vector3 landAt = transform.position + fwd * 1.2f;
        GsFx("gs_crater", "gs_quake", Floor(landAt), 3.4f, StageOneColor * 2f, 0.5f, 0.6f, 1.05f, 0f, FlatRandom, 0.7f);
        Vfx.Dust(Floor(landAt, 0f), 1.4f, 12);
        CameraShake.Shake(0.22f, 0.16f);
        ChainLightning(HitFront(1f));
        StartCoroutine(Fissure(landAt, fwd, waveRange * rift * 0.85f, waveDamage * rift * 0.8f));
        yield return new WaitForSeconds(0.1f);
        if (animator != null) animator.SetFloat(ChargeSpeedHash, 1.6f * speed);
        yield return new WaitForSeconds(0.22f / speed);
        if (animator != null) animator.CrossFadeInFixedTime("Locomotion", 0.2f);
        IsSwinging = false;
    }

    // The crack running out along the floor, length by length, then the burst along it
    private IEnumerator Fissure(Vector3 start, Vector3 dir, float length, float damage)
    {
        GameAudio.Play("gs_fissure", start, 0.9f);
        const float step = 1.5f, pace = 0.045f;
        var cut = new HashSet<Enemy>();
        float hold = length / step * pace + 0.75f;
        for (float s = 0f; s <= length; s += step)
        {
            Vector3 p = start + dir * s;
            LaySplit(p, dir, 1.6f, hold - s / step * pace + 0.2f);
            foreach (Enemy e in Enemy.Alive.ToArray())
            {
                if (e == null || e.IsDead || cut.Contains(e)) continue;
                Vector3 off = e.transform.position - p;
                off.y = 0f;
                if (off.magnitude > 1.6f) continue;
                cut.Add(e);
                Strike(e, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool c) * damage)), c);
            }
            CameraShake.Shake(0.05f, 0.05f);
            yield return new WaitForSeconds(pace);
        }
        yield return CrackBurst(start, start + dir * length, 1.3f, 1.3f, 0.6f, alreadySplit: true);
    }

    // 압축 검기 meeting its target: the condensed blade bursts on it
    public void CompressBurst(Vector3 at)
    {
        GsFx("gs_finalblow", "gs_quake", Floor(at, 0.1f), 3.4f, StageOneColor * 2.6f, 0.5f, 0.5f, 1.05f, 0f, FlatRandom, 0.35f);
        GsFx("gs_spinburst", null, at + Vector3.up * 0.2f, 2.6f, Color.white * 2.2f, 0.35f, 0.4f, 1.1f);
        Vfx.Burst(at + Vector3.up * 0.8f, StageOneColor, 20, 10f, 0.2f);
        CameraShake.Shake(0.18f, 0.14f);
    }

    // 대지 절단: one length of the split ground laid behind the wave as it passes (held open until the burst)
    public void LaySplit(Vector3 at, Vector3 dir, float width, float seconds)
    {
        // the crack itself drawn dark into the stone (a deep fissure, glowing only faintly inside), the old chasm under it
        string crack = HasSheet("gs_fissure") ? "gs_fissure" : "gs_groundsplit";
        // (wide and dark: broken stone first, only a dim ember glow down in the cracks). The sheet tears open quickly, then
        // its last, fully split frame stays (a sheet held for seconds would spend them on its thin opening frames)
        Quaternion lie = Quaternion.Euler(90f, YawOf(dir) + UnityEngine.Random.Range(-8f, 8f), 0f);
        Color ember = new Color(1f, 0.42f, 0.1f) * 0.8f;
        Color dark = new Color(InkColor.r, InkColor.g, InkColor.b, 0.97f);
        float size = width * 2.9f;
        const float open = 0.22f;
        GsFx(crack, "gs_quake", Floor(at, 0.07f), size, ember, open, 0.9f, 1f, 0f, lie, 0.97f);
        if (HasSheet("gs_fissure_full"))
        {
            var held = DualBlades.Cut("gs_fissure_full", Floor(at, 0.071f), size, ember, Mathf.Max(open, seconds), 1f, 1f, 0f, lie, 0, dark);
            held.Hold();
        }
        if (crack != "gs_groundsplit")
        {
            var under = GsFx("gs_groundsplit", null, Floor(at, 0.06f), width * 2.1f, new Color(1f, 0.45f, 0.12f) * 0.6f, seconds, 0.85f, 1f, 0f, Flat(YawOf(dir)), 0.85f);
            if (under != null) under.Hold();
        }
        Vfx.Dust(Floor(at, 0f), 0.7f, 5, new Color(0.55f, 0.45f, 0.35f, 0.6f));
    }

    // R: leap toward the cursor (up to leapRange) and bring the blade down: a crater, a shockwave (only 낙성 knocks down)
    private IEnumerator LeapSlam(Vector3 aimPoint)
    {
        GameAudio.Play("gs_leap");
        BeginSkill();
        int sp = Sp('R');
        leapReadyAt = Time.time + LeapCooldown;
        // 연속 도약: two in a row — the cooldown starts after the second (or once the window closes)
        if (sp == 0)
        {
            if (leapChainLeft <= 0) leapChainLeft = 2;
            leapChainLeft--;
            leapChainUntil = Time.time + 2.6f;
            leapReadyAt = leapChainLeft > 0 ? 0f : Time.time + LeapCooldown;
        }
        // 하늘 가르기: a further leap and a wider landing
        bool skyfall = GreatswordSkills.Has(GreatswordSkills.Skyfall);
        float leapRadius = (this.leapRadius + (skyfall ? 1f : 0f)) * (sp == 0 ? 0.85f : 1f);
        Vector3 to = aimPoint - transform.position;
        to.y = 0f;
        Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
        float distance = Mathf.Min(to.magnitude, leapRange * (skyfall ? 1.4f : 1f));
        transform.rotation = Quaternion.LookRotation(dir);
        Vector3 landing = transform.position + dir * distance;
        // (the leap plays faster than the clip: it reaches the floor sooner)
        const float leapPace = 1.45f;
        float liftAt = leapLiftAt / leapPace, landAt = leapLandAt / leapPace;
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, leapPace);
            animator.CrossFadeInFixedTime("LeapSlam", 0.08f, 0, 0f);
        }
        // Where it will land: the rune circle opening on the floor
        Vfx.Sprite("gs_runecircle", new Vector3(landing.x, Feet + 0.06f, landing.z), leapRadius * 2f, StageOneColor * 2.2f,
            landAt, 0.4f, 1f, 90f, Quaternion.Euler(90f, 0f, 0f)).Sheet(4, 4);
        var controller = GetComponent<CharacterController>();
        PassThroughEnemies(true);
        bool lifted = false;
        for (float t = 0f; t < landAt; t += Time.deltaTime)
        {
            if (t >= liftAt)
            {
                if (!lifted)
                {
                    lifted = true;
                    Vfx.Dust(new Vector3(transform.position.x, Feet, transform.position.z), 1.2f, 8);
                }
                if (controller != null)
                    controller.Move(dir * (distance / (landAt - liftAt)) * Time.deltaTime);
            }
            yield return null;
        }
        PassThroughEnemies(false);
        if (animator != null) animator.SetFloat(AttackSpeedHash, 1f);

        // The landing: the blade driven into the floor — a cut of light coming down, a shattered crater, a dust shockwave,
        // the ring of light under it — all drawn deep and solid (a darker shadow under each, brighter light)
        Vector3 impact = transform.position + transform.forward * 0.6f;
        GameAudio.Play("gs_slam");   // (the leap comes down)
        float leapDamageNow = leapDamage * (sp == 0 ? 0.7f : sp == 2 ? 1.1f : sp == 3 ? 0.5f : 1f);
        GsFx("gs_downslash", "slashsheet", Floor(impact, 1.5f), 3.8f, StageOneColor * 2.8f, 0.3f, 0.9f, 1.05f, 0f, null, 0.6f);
        HeavyImpact(impact, leapRadius * 2.3f, StageOneColor, 0.38f, 0.85f);
        Quake(impact, leapRadius * 2.2f, StageOneColor * 2.8f, 0.65f);
        Vfx.Flash(new Vector3(impact.x, Feet + 0.4f, impact.z), 4.5f, StageOneColor);
        Shockwave.Spawn(transform.position, leapRadius, StageOneColor, 30);
        var struck = new List<Enemy>();
        Wave(leapRadius, leapDamageNow, struck, false);
        // Only 낙성 knocks anything down (for a long while); the plain leap and the other specials just strike
        if (sp == 2)
            foreach (Enemy enemy in struck)
                if (enemy != null && !enemy.IsDead)
                    enemy.Knockdown(2.6f);
        ChainLightning(struck);
        if (sp == 1) StartCoroutine(Tremors(impact));                 // 지진 강타
        if (sp == 3) StartCoroutine(CrackBurst(impact, impact, this.leapRadius * 1.1f, 1.4f, 1.0f));   // 균열 폭발

        yield return new WaitForSeconds(sp == 3 ? 0.12f : 0.45f);   // (균열 폭발: free to act straight after landing)
        if (animator != null)
            animator.CrossFadeInFixedTime("Locomotion", 0.25f);
        IsSwinging = false;
    }

    // 지진 강타: the quake rolls out from the landing as rings of heaving ground — two waves, each catching an enemy once
    // as its edge passes
    private IEnumerator Tremors(Vector3 at)
    {
        const float reach = 7.5f, travel = 0.75f;
        for (int wave = 0; wave < 2; wave++)
        {
            var hitBy = new HashSet<Enemy>();
            // the painted ripple grows with the edge (its sheet expands from the middle on its own)
            GsFx("gs_quakering", "gs_quake", Floor(at, 0.07f + wave * 0.01f), reach * 2.3f, StageOneColor * (2.8f - wave * 0.4f), travel + 0.2f, 0.25f, 1.05f,
                0f, FlatRandom, 0.75f);
            GsFx("gs_shockdust", null, Floor(at, 0.1f), reach * 2.1f, new Color(1f, 0.85f, 0.6f) * 1.5f, travel + 0.15f, 0.2f, 1.05f, 0f, FlatRandom, 0.45f);
            for (float t = 0f; t < travel; t += Time.deltaTime)
            {
                float edge = Mathf.Lerp(0.8f, reach, t / travel);
                foreach (Enemy e in Enemy.Alive.ToArray())
                {
                    if (e == null || e.IsDead || hitBy.Contains(e)) continue;
                    Vector3 off = e.transform.position - at;
                    off.y = 0f;
                    if (off.magnitude > edge || off.magnitude < edge - 1.6f) continue;
                    hitBy.Add(e);
                    Strike(e, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool c) * (wave == 0 ? 1.1f : 0.8f))), c);
                    Vfx.Dust(Floor(e.transform.position, 0f), 0.6f, 5);
                }
                yield return null;
            }
            CameraShake.Shake(0.12f, 0.1f);
            yield return new WaitForSeconds(0.12f);
        }
    }

    // A crack in the ground from a to b (a point when they match) that bursts after a delay: a line splits open along its
    // length and erupts all along it; a point cracks into a glowing star and blows up (its own explosion)
    public IEnumerator CrackBurst(Vector3 a, Vector3 b, float width, float damage, float delay, bool alreadySplit = false)
    {
        Vector3 d = b - a;
        d.y = 0f;
        float len = d.magnitude;
        Vector3 dir = len > 0.01f ? d / len : Vector3.forward;
        bool line = len > 0.5f;
        if (line && !alreadySplit)
            for (float s = 0f; s <= len + 0.01f; s += 1.6f)
                LaySplit(a + dir * s, dir, width, delay + 0.25f);
        else if (!line)
        {
            var star = GsFx("gs_finalblow", "gs_quake", Floor(a, 0.05f), width * 2.4f, new Color(1f, 0.45f, 0.1f) * 1.6f, delay + 0.15f, 0.5f, 0.9f, 0f, FlatRandom, 0.4f);
            if (star != null) star.Hold();
        }
        yield return new WaitForSeconds(delay);
        if (line)
        {
            for (float s = 0f; s <= len + 0.01f; s += 1.8f)
            {
                Eruption(a + dir * s, width * 1.6f, StageOneColor);
                yield return new WaitForSeconds(0.03f);
            }
        }
        else
        {
            // 균열 폭발's own explosion: a fireball blast standing up out of the cracked ground over a dark scorched crater
            // (kept to about the blast's own reach: it mustn't wash over the whole room)
            float blast = Mathf.Min(6.5f, width * 1.6f);
            if (HasSheet("gs_blast"))
            {
                GsFx("gs_blast", null, Floor(a, blast * 0.42f), blast, new Color(1f, 0.62f, 0.25f) * 2.4f, 0.95f, 0.9f, 1.08f, 0f, null, 0.7f);
                Vfx.Burst(Floor(a, 0.6f), new Color(1f, 0.6f, 0.2f), 26, 11f, 0.24f, 2.5f);
            }
            else
                Eruption(a, Mathf.Min(5f, width * 1.3f), StageOneColor);
            HeavyImpact(a, Mathf.Min(7f, width * 1.8f), StageOneColor, 0.34f, 0.85f);
            GsFx("gs_rupture", null, Floor(a, 0.12f), Mathf.Min(8f, width * 2.1f), new Color(1f, 0.7f, 0.35f) * 2f, 0.5f, 0.4f, 1.1f, 0f, FlatRandom, 0.6f);
        }
        Vfx.Flash(new Vector3(b.x, Feet + 0.4f, b.z), 2.5f + width, StageOneColor);
        CameraShake.Shake(0.26f, 0.22f);
        foreach (Enemy e in Enemy.Alive.ToArray())
        {
            if (e == null || e.IsDead) continue;
            Vector3 rel = e.transform.position - a;
            rel.y = 0f;
            float along = Mathf.Clamp(Vector3.Dot(rel, dir), 0f, len);
            if ((rel - dir * along).magnitude > width + 0.5f) continue;
            Strike(e, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool c) * damage)), c);
        }
    }

    private void PassThroughEnemies(bool on)
    {
        var controller = GetComponent<CharacterController>();
        if (controller == null)
            return;
        foreach (Enemy enemy in Enemy.Alive)
            if (enemy != null)
                foreach (Collider col in enemy.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(controller, col, on);
    }

    // ── 차징 강타 (right button): hold to raise the blade and charge through two stages, let go to slam ──

    // Left click: the tap combo (or a plain swing for characters without it)
    public void PressAttack(Vector3 aimPoint)
    {
        if (IsCharging)
            return;
        if (IsSwinging)
        {
            // Mid-combo: remember the tap, the next blow follows as soon as this one allows
            if (chargeEnabled)
            {
                comboQueued = true;
                comboAim = aimPoint;
            }
            return;
        }
        SwingFree(aimPoint);
    }

    // Right button down / still down: the charge starts as soon as the blade is free and the skill is off cooldown
    public void HoldCharge(Vector3 aimPoint)
    {
        chargeAim = aimPoint;
        if (!chargeEnabled || IsCharging || IsSwinging || ChargeCooldownLeft > 0f || !Learned(GreatswordSkills.Charge))
            return;
        comboQueued = false;
        comboStep = 0;
        if (queuedBlow != null)
        {
            StopCoroutine(queuedBlow);
            queuedBlow = null;
        }
        recoveringState = null;
        pressing = true;
        pressStart = Time.time;
        chargeRoutine = StartCoroutine(Charge());
    }

    // Right button let go: a charge in progress comes down as a slam
    public void ReleaseCharge(Vector3 aimPoint)
    {
        if (!pressing)
            return;
        heldAtRelease = Time.time - pressStart;
        pressing = false;
        chargeAim = aimPoint;
        if (IsCharging)
            slamRequested = true;
    }

    // 돌진: while charging, dash in a straight line toward the cursor, once per charge
    public bool CanRush => IsCharging && !slamRequested && !rushedThisCharge && GreatswordSkills.Has(GreatswordSkills.Rush);

    public void Rush(Vector3 aimPoint)
    {
        if (!CanRush)
            return;
        rushedThisCharge = true;
        StartCoroutine(RushForward(aimPoint));
    }

    private IEnumerator RushForward(Vector3 aimPoint)
    {
        GameAudio.Play("gs_rush");
        Vector3 dir = aimPoint - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(dir);
        var controller = GetComponent<CharacterController>();
        var hit = new HashSet<Enemy>();
        Shockwave.Spawn(transform.position, 1.2f, StageOneColor, 8);
        Vfx.Dust(transform.position, 1f, 6);
        // Speed lines tearing along the path (the same VARCO streak as the twin blades' dash, in gold)
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        DualBlades.Cut("dashsheet", transform.position + dir * (rushDistance * 0.5f) + Vector3.down * 0.45f, rushDistance * 1.25f,
            StageOneColor * 1.7f, rushTime + 0.35f, 0.9f, 1.05f, 0f, Quaternion.Euler(90f, yaw - 90f, 0f), sheet: 4, inkColor: InkColor);
        Quake(transform.position, 2.4f, StageOneColor * 1.4f, 0.4f);
        // A streak of light behind the dash
        var streak = new GameObject("RushStreak");
        streak.transform.SetParent(transform, false);
        streak.transform.localPosition = Vector3.up * 0.9f;
        Vfx.Trail(streak, StageOneColor, 1.4f, 0.3f);
        Destroy(streak, rushTime + 0.35f);
        for (float t = 0f; t < rushTime && IsCharging; t += Time.deltaTime)
        {
            controller.Move(dir * (rushDistance / rushTime) * Time.deltaTime);
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy.IsDead || hit.Contains(enemy))
                    continue;
                Vector3 offset = enemy.transform.position - transform.position;
                offset.y = 0f;
                if (offset.magnitude > 1.3f)
                    continue;
                hit.Add(enemy);
                Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * rushDamage)), critical);
            }
            yield return null;
        }
    }

    // A new run: drop whatever swing or charge was in progress when the last one ended
    public void ResetCombat()
    {
        StopAllCoroutines();
        chargeRoutine = null;
        IsCharging = false;
        IsSwinging = false;
        Guarding = false;
        pressing = false;
        slamRequested = false;
        chargeReadyAt = whirlwindReadyAt = waveReadyAt = leapReadyAt = 0f;
        EndWhirlwind();
        PassThroughEnemies(false);
        comboStep = 0;
        comboQueued = false;
        queuedBlow = null;
        nextBlowTime = 0f;
        recoveringState = null;
        if (animator != null)
        {
            animator.SetFloat(ChargeSpeedHash, 1f);
            animator.CrossFade("Locomotion", 0.1f);
        }
    }

    // 결의 softens hits taken while charging
    public float IncomingDamageMultiplier => IsCharging && GreatswordSkills.Has(GreatswordSkills.Resolve) ? 0.75f : 1f;

    // Getting hit while charging breaks the charge, unless 결의 shrugs off a light hit
    public void OnHurt(float damage)
    {
        if (!IsCharging || slamRequested)
            return;
        if (GreatswordSkills.Has(GreatswordSkills.Resolve) && damage < resolveThreshold)
            return;

        if (chargeRoutine != null)
            StopCoroutine(chargeRoutine);
        chargeRoutine = null;
        IsCharging = false;
        pressing = false;
        slamRequested = false;
        if (animator != null)
        {
            animator.SetFloat(ChargeSpeedHash, 1f);
            animator.CrossFade("Locomotion", 0.15f);
        }
        chargeReadyAt = Time.time + ChargeCooldownScaled * 0.5f;
        ChargeBroken?.Invoke();
        StartCoroutine(Staggered(0.4f));
    }

    private IEnumerator Staggered(float seconds)
    {
        IsSwinging = true;
        yield return new WaitForSeconds(seconds);
        IsSwinging = false;
    }

    private IEnumerator Charge()
    {
        IsCharging = true;
        IsSwinging = true;
        slamRequested = false;
        rushedThisCharge = false;
        int stageShown = 0;
        thirdStage = false;
        float speed = AttackSpeed;
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, speed);
            // (the raise keeps pace with the shorter charge: up by the time the first stage lands)
            animator.SetFloat(ChargeSpeedHash, raiseSpeed * 1.6f);
            animator.SetTrigger(ChargeHash);
        }
        // Power gathers into the blade as it rises (the gathering keeps drawing in while the charge is held: Update)
        Vfx.Flash(transform.position + Vector3.up * 1.4f, 1.4f, StageOneColor, 0.2f);

        // Raise the weapon slowly, then hold it up until the button is released
        while (!slamRequested)
        {
            Face(chargeAim, 540f);
            if (animator != null)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Charge") && state.normalizedTime >= holdPoint)
                    animator.SetFloat(ChargeSpeedHash, 0f);
            }
            int stage = ChargeStage;
            // 대지의 심판: none of the plain charge's bursts — its three-cell gauge shows each gathering, with a small flash
            if (stage > stageShown && Sp('M') == 1)
            {
                stageShown = stage;
                Vfx.Flash(transform.position + Vector3.up * 1.8f, 1.2f, stage == 2 ? StageTwoColor : StageOneColor, 0.15f);
                ChargeStageReached?.Invoke(stage);
            }
            else if (stage > stageShown)
            {
                stageShown = stage;
                Color stageColor = stage == 2 ? StageTwoColor : StageOneColor;
                Shockwave.Spawn(transform.position, 1.2f, stageColor, 10);
                Vfx.Flash(transform.position + Vector3.up * 1.8f, stage == 2 ? 2.4f : 1.6f, stageColor, 0.22f);
                // Each stage bursts out of the blade, the full charge with a crack of lightning
                Vfx.Sprite("hitsheet", transform.position + Vector3.up * 1.2f, stage == 2 ? 3f : 2.2f, stageColor * 1.8f, 0.35f, 0.8f, 1.2f, 0f).Sheet(4, 4);
                Quake(transform.position, stage == 2 ? 3.6f : 2.6f, stageColor * 1.4f, 0.45f);
                if (stage == 2)
                    Pillar(transform.position, 3.4f);
                ChargeStageReached?.Invoke(stage);
            }
            // 대지의 심판: held on past the full charge, a third gathering — the blow that strikes the whole room
            if (!thirdStage && Sp('M') == 1 && ChargeHeld >= StageThreeTime)
            {
                thirdStage = true;
                Vfx.Flash(transform.position + Vector3.up * 1.8f, 1.8f, new Color(1f, 0.9f, 0.55f), 0.2f);
                CameraShake.Shake(0.12f, 0.1f);
            }
            yield return null;
        }

        // Release: finish the swing from where it is and land the blow.
        // 단절: a full-charge slam guards the player from release until it is over.
        int finalStage = ChargeStage;
        Guarding = finalStage == 2 && GreatswordSkills.Has(GreatswordSkills.Sever);
        Face(chargeAim, 9999f);
        float progress = 0f;
        if (animator != null)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            progress = state.IsName("Charge") ? Mathf.Min(state.normalizedTime, impactPoint) : 0f;
            animator.SetFloat(ChargeSpeedHash, speed);
        }
        float stateSeconds = swingSeconds / speed;
        yield return new WaitForSeconds((impactPoint - progress) * stateSeconds);
        int spM = Sp('M');
        if (spM >= 0)
            yield return SlamVariant(spM, finalStage, speed);
        else
        {
            Slam(finalStage, 1f);
            yield return SlamStop(finalStage, speed);
        }

        // 연속 강타: snap the sword back up and bring it down once more
        if (spM < 0 && GreatswordSkills.Has(GreatswordSkills.Combo))
        {
            yield return new WaitForSeconds(0.12f / speed);
            const float comboSpeed = 1.3f;
            if (animator != null)
            {
                animator.Play("Charge", 0, holdPoint);
                animator.SetFloat(ChargeSpeedHash, speed * comboSpeed);
            }
            yield return new WaitForSeconds((impactPoint - holdPoint) * stateSeconds / comboSpeed);
            Slam(finalStage, comboDamage);
            yield return SlamStop(finalStage, speed * comboSpeed);
        }
        yield return new WaitForSeconds((1f - impactPoint) * stateSeconds * 0.8f);

        Guarding = false;
        IsCharging = false;
        IsSwinging = false;
        chargeRoutine = null;
        chargeReadyAt = Time.time + ChargeCooldownScaled;
    }

    // The blade comes down. What else happens depends on the charge stage and the skills learned:
    // 강타 adds a shockwave, 낙하 a second, bigger one that knocks enemies down, 단절 breaks their attacks.
    private void Slam(int stage, float multiplier)
    {
        GameAudio.Play(stage == 2 ? "gs_charge_slam" : "gs_slam", null, stage == 2 ? 1f : 0.8f);
        bool full = stage == 2;
        bool plunge = full && GreatswordSkills.Has(GreatswordSkills.Plunge);
        Color color = full ? StageTwoColor : stage == 1 ? StageOneColor : AttackRangeFx.SwingColor;
        RangeFx.Flash(Reach, color);
        // The greatsword comes straight down in a blaze of light, cracking the floor where it lands
        Vector3 impact = transform.position + transform.forward * 1.3f;
        Cleave(70f, false, Reach * (2.8f + stage * 0.5f), color * 1.8f, 0.32f);
        Quake(impact, 3.4f + stage * 1.6f, color * 1.7f, 0.5f + stage * 0.1f);
        if (full)
            Pillar(impact, 4.6f);
        Vfx.Flash(new Vector3(impact.x, Feet + 0.4f, impact.z), 1.8f + stage, color);
        Vfx.Dust(new Vector3(impact.x, Feet, impact.z), 1f + stage * 0.6f, 6 + stage * 5);
        List<Enemy> struck = HitFront(stageDamage[stage] * multiplier * (plunge ? plungeDamageBonus : 1f));
        CameraShake.Shake(full ? 0.3f : 0.15f, full ? 0.25f : 0.12f);

        if (stage >= 1 && GreatswordSkills.Has(GreatswordSkills.Smash))
        {
            float radius = full ? shockRadiusFull : shockRadius;
            float shockMultiplier = (full ? shockDamageFull : shockDamage) * multiplier;
            Shockwave.Spawn(transform.position, radius, full ? StageTwoColor : StageOneColor, 24);
            Quake(transform.position, radius * 2.2f, (full ? StageTwoColor : StageOneColor) * 1.5f, 0.6f);
            Wave(radius, shockMultiplier, struck, false);
        }
        if (plunge)
            StartCoroutine(PlungeWave(multiplier, struck));
        if (full && GreatswordSkills.Has(GreatswordSkills.Sever))
            foreach (Enemy enemy in struck)
                if (enemy != null && !enemy.IsDead)
                    enemy.Interrupt();
        ChainLightning(struck);
    }

    // 차징 강타's special versions (the charge stage: 0 barely raised, 1, 2 full)
    private IEnumerator SlamVariant(int sp, int stage, float speed)
    {
        GameAudio.Play("gs_charge_slam", null, stage == 2 ? 1f : 0.75f);
        bool full = stage == 2;
        Color color = full ? StageTwoColor : StageOneColor;
        Vector3 fwd = transform.forward;
        switch (sp)
        {
            case 0:   // 분쇄 일격: one stage — all the gathered weight brought straight down in a single crushing blow
            {
                Vector3 at = transform.position + fwd * 1.5f;
                float radius = full ? 3.4f : 2.2f;
                GsFx("gs_downslash", "slashsheet", Floor(at, 1.7f), full ? 4.6f : 3.4f, Color.white * 2.5f, 0.35f, 0.9f, 1.05f);
                Cleave(80f, false, Reach * 3.2f, color * 2.2f, 0.32f);
                HeavyImpact(at, radius * 2.6f, color, full ? 0.45f : 0.22f);
                if (full)
                {
                    GsFx("gs_finalblow", "gs_quake", Floor(at, 0.1f), radius * 3f, color * 2.4f, 0.9f, 0.55f, 1.05f, 0f, FlatRandom, 0.45f);
                    Eruption(at, 2.6f, StageOneColor);
                    Pillar(at, 4.6f);
                }
                var struck = HitFront(stageDamage[stage] * (full ? 1.6f : 1.1f), 100f, 0.6f);
                foreach (Enemy e in Enemy.Alive.ToArray())
                {
                    if (e == null || e.IsDead || struck.Contains(e)) continue;
                    Vector3 off = e.transform.position - at;
                    off.y = 0f;
                    if (off.magnitude > radius) continue;
                    Strike(e, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool c) * stageDamage[stage] * 0.9f)), c);
                    struck.Add(e);
                }
                if (full)
                    foreach (Enemy e in struck)
                        if (e != null && !e.IsDead) e.Knockdown(1.2f);
                ChainLightning(struck);
                yield return SlamStop(stage, speed);
                break;
            }
            case 1:   // 대지의 심판: gathered three times, the blow comes down on the whole room; short of that, a plain slam
            {
                if (!thirdStage)
                {
                    Slam(stage, 1f);
                    yield return SlamStop(stage, speed);
                    break;
                }
                // (no slam of its own: the blade comes down into the floor and the sword from the sky does the rest)
                Vfx.Dust(Floor(transform.position + fwd * 1.2f, 0f), 1.2f, 10);
                yield return SlamStop(2, speed);
                StartCoroutine(Judgement());
                break;
            }
            case 2:   // 회전 강타: one quick turn at stage 1, two heavy ones at stage 2 — a crushing arc of blade light round the body
            {
                int turns = full ? 2 : 1;
                float radius = full ? 4.2f : 3.2f;
                for (int k = 0; k < turns; k++)
                {
                    if (animator != null) { animator.SetFloat(AttackSpeedHash, full ? 1.2f : 2f); animator.CrossFadeInFixedTime("Whirlwind", 0.05f); }
                    Vector3 at = transform.position;
                    GsFx("gs_heavyspin", "spinsheet", Floor(at, 0.4f), radius * 2.4f, color * 2.3f, full ? 0.5f : 0.4f, 0.85f, 1.12f, full ? 220f : 360f, FlatRandom, 0.4f);
                    GsFx("gs_spinburst", null, Floor(at, 0.3f), radius * 2.5f, Color.Lerp(color, Color.white, 0.3f) * 1.9f, 0.5f, 0.6f, 1.1f, 0f, FlatRandom);
                    GsFx("gs_shockdust", null, Floor(at, 0.08f), radius * 2.6f, new Color(1f, 0.9f, 0.7f) * 1.4f, 0.6f, 0.4f, 1.1f, 0f, FlatRandom);
                    Vfx.Dust(Floor(at, 0f), radius * 0.5f, 12);
                    var struck = new List<Enemy>();
                    Wave(radius, full ? 2f : 1.2f, struck, false);
                    // the last turn throws everything it cut outward, away from the blade
                    if (k == turns - 1)
                    {
                        foreach (Enemy e in struck)
                            if (e != null && !e.IsDead && !e.IsBoss)
                                StartCoroutine(Shove(e, transform.position, full ? 4.5f : 3f, 0.28f));
                        GsFx("gs_stormring", null, Floor(at, 0.12f), radius * 2.6f, color * 2.2f, 0.45f, 0.4f, 1.15f, 200f, FlatRandom, 0.35f);
                    }
                    ChainLightning(struck);
                    CameraShake.Shake(full ? 0.24f : 0.16f, full ? 0.2f : 0.14f);
                    yield return new WaitForSeconds(full ? 0.4f : 0.15f);
                }
                if (animator != null) animator.CrossFadeInFixedTime("Locomotion", 0.2f);
                break;
            }
            case 3:   // 투신의 각오: the gathered power taken into the body — more damage dealt and less taken for a while
            {
                Resolve(stage);
                yield return SlamStop(stage, speed);
                break;
            }
        }
    }

    // 천검 낙하: a rune circle opens where it will land, the great sword of light drops out of the sky and buries its point
    // there — a crater, a star of cracks, eruptions round it, everything near knocked down — then it stands a moment and fades
    // An enemy thrown back from a point, sliding to a stop over `seconds` (회전 강타's last turn)
    private IEnumerator Shove(Enemy e, Vector3 from, float distance, float seconds)
    {
        Vector3 away = e.transform.position - from;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward;
        float moved = 0f;
        for (float t = 0f; t < seconds && e != null && !e.IsDead; t += Time.deltaTime)
        {
            float k = 1f - t / seconds;                       // fast at first, easing to a stop
            float step = Mathf.Min(distance - moved, distance * 2f * k * Time.deltaTime / seconds);
            e.transform.position += away * step;
            moved += step;
            yield return null;
        }
        if (e != null && !e.IsDead) Vfx.Dust(Floor(e.transform.position, 0f), 0.6f, 5);
    }

    // 투신의 각오 (우클릭 4번째 특수 강화): the stronger the charge, the stronger the boon (stage 0 / 1 / 2)
    private static readonly float[] ResolveDamage = { 1.12f, 1.25f, 1.5f };
    private static readonly float[] ResolveGuard = { 0.9f, 0.8f, 0.65f };      // damage taken ×
    private static readonly float[] ResolveSeconds = { 6f, 8f, 10f };
    private SkillDef resolveShown;

    private void Resolve(int stage)
    {
        stage = Mathf.Clamp(stage, 0, 2);
        resolveShown ??= new SkillDef { id = "gs_resolve", name = SpecialNames[3][3], iconTex = Icon("gs_charge") };
        resolveShown.desc = $"공격력 +{(ResolveDamage[stage] - 1f) * 100f:0}% · 받는 피해 -{(1f - ResolveGuard[stage]) * 100f:0}% ({Mathf.Max(1, stage)}단계 차지)";
        SkillSystem.ClassBoon(resolveShown, ResolveSeconds[stage], ResolveDamage[stage], ResolveGuard[stage]);
        // the boon's own effect plays once: the power bursting up round the body (its time left shows in the buff row)
        Color c = stage == 2 ? StageTwoColor : StageOneColor;
        GsFx("gs_charge", "chargesheet", transform.position + Vector3.up * 1.4f, 3f + stage * 0.6f, c * 2.2f, 0.7f, 0.8f, 1.2f);
        GsFx("gs_spinburst", null, Floor(transform.position, 0.2f), 4f + stage, Color.Lerp(c, Color.white, 0.3f) * 2f, 0.6f, 0.4f, 1.1f, 0f, FlatRandom);
        Vfx.Flash(transform.position + Vector3.up * 1.2f, 2.4f + stage, c, 0.3f);
        Shockwave.Spawn(transform.position, 2.5f + stage, c, 20);
        CameraShake.Shake(0.15f + stage * 0.05f, 0.12f);
    }

    // 대지의 심판: a giant sword drops into the middle of the room, then a shockwave rolls out from it to the walls,
    // striking every enemy in the room as its edge passes (outside a room — the training arena — round the player instead)
    private IEnumerator Judgement()
    {
        Room room = GameManager.Instance != null ? GameManager.Instance.CurrentRoom : null;
        bool inRoom = room != null && !TrainingRoom.Active && room.Contains(transform.position, 2f);
        Vector3 centre = inRoom ? room.transform.position : transform.position + transform.forward * 4f;
        float reach = inRoom ? (room.Radius > 0f ? room.Radius : Mathf.Max(room.Size.x, room.Size.y) * 0.6f) + 2f : 16f;
        centre.y = transform.position.y;
        yield return DropSword(centre);
        Color holy = new Color(1f, 0.85f, 0.45f);
        CameraShake.Shake(0.5f, 0.4f);
        const float travel = 0.9f;
        // the ring rolling out to the walls (its sheet grows from the middle on its own)
        string ring = HasSheet("gs_bigshock") ? "gs_bigshock" : "gs_quakering";
        GsFx(ring, "gs_quake", Floor(centre, 0.1f), reach * 2.2f, holy * 2.6f, travel + 0.3f, 0.1f, 1.02f, 0f, FlatRandom, 0.6f);
        GsFx("gs_shockdust", null, Floor(centre, 0.12f), reach * 2.1f, new Color(1f, 0.9f, 0.7f) * 1.5f, travel + 0.25f, 0.1f, 1.02f, 0f, FlatRandom, 0.4f);
        Shockwave.Spawn(centre, reach, holy, 48);
        var hit = new HashSet<Enemy>();
        for (float t = 0f; t < travel; t += Time.deltaTime)
        {
            float edge = Mathf.Lerp(1f, reach, t / travel);
            foreach (Enemy e in Enemy.Alive.ToArray())
            {
                if (e == null || e.IsDead || hit.Contains(e)) continue;
                Vector3 off = e.transform.position - centre;
                off.y = 0f;
                if (off.magnitude > edge) continue;
                hit.Add(e);
                Strike(e, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool c) * 3f)), c);
                if (!e.IsDead) e.Knockdown(1.5f);
                Eruption(e.transform.position, 2f, StageOneColor);
            }
            yield return null;
        }
        ChainLightning(new List<Enemy>(hit));
    }

    // 대지의 심판's sword: the VARCO 3D greatsword (Resources/Props/JudgementSword) dropped point-first out of the sky,
    // a light riding down with it, burying its point in the floor in a crater of cracks — it stands there while the
    // shockwave rolls out, then sinks back into the ground. (the painted sword stands in if the model is missing)
    private IEnumerator DropSword(Vector3 at)
    {
        GameObject prefab = Resources.Load<GameObject>("Props/JudgementSword");
        if (prefab == null)
        {
            yield return SkySword(at, false, 10f);
            yield break;
        }
        const float height = 22f, fall = 0.32f, bury = 1.3f;
        Vector3 top = Floor(at, height), planted = Floor(at, -bury);
        // (its broad face turned to the camera — edge-on it would be a thin line)
        Camera view = Camera.main;
        float yaw = view != null ? view.transform.eulerAngles.y : 0f;
        GameObject sword = Instantiate(prefab, top, Quaternion.Euler(0f, yaw, 0f));
        var glow = new GameObject("JudgementGlow").AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.85f, 0.5f);
        glow.range = 14f;
        glow.intensity = 6f;
        glow.transform.SetParent(sword.transform, false);
        glow.transform.localPosition = Vector3.up * 3f;
        Vfx.Trail(sword, new Color(1f, 0.85f, 0.5f), 1.4f, 0.25f);
        // where it will land: a ring of light opening on the floor
        GsFx("gs_runecircle", null, Floor(at, 0.05f), 9f, new Color(1f, 0.85f, 0.45f) * 2f, fall + 0.3f, 0.4f, 1f, 90f, Flat(0f));
        for (float t = 0f; t < fall; t += Time.deltaTime)
        {
            float k = t / fall;
            sword.transform.position = Vector3.LerpUnclamped(top, planted, k * k);
            yield return null;
        }
        sword.transform.position = planted;
        // the point bites: cracks star out round it, a crater, dust thrown up
        Color gold = new Color(1f, 0.8f, 0.4f);
        GsFx("gs_finalblow", "gs_quake", Floor(at, 0.1f), 11f, gold * 2.4f, 1.6f, 0.5f, 1.05f, 0f, FlatRandom, 0.85f);
        HeavyImpact(at, 8f, gold, 0.55f, 0.85f);
        Vfx.Flash(Floor(at, 2f), 6f, gold, 0.3f);
        Vfx.Dust(Floor(at, 0f), 3f, 26);
        StartCoroutine(SwordLinger(sword, glow, 2.2f));
    }

    private IEnumerator SwordLinger(GameObject sword, Light glow, float seconds)
    {
        if (sword == null) yield break;
        // the light settles, then the blade slides back down into the earth
        for (float t = 0f; t < seconds && sword != null; t += Time.deltaTime)
        {
            if (glow != null) glow.intensity = Mathf.Lerp(6f, 2f, t / seconds);
            yield return null;
        }
        if (sword == null) yield break;
        Vector3 from = sword.transform.position, to = from + Vector3.down * 9f;
        for (float t = 0f; t < 0.6f && sword != null; t += Time.deltaTime)
        {
            float k = t / 0.6f;
            sword.transform.position = Vector3.Lerp(from, to, k * k);
            if (glow != null) glow.intensity = Mathf.Lerp(2f, 0f, k);
            yield return null;
        }
        if (sword != null) Destroy(sword);
    }

    private IEnumerator SkySword(Vector3 at, bool blast = true, float swordSize = 7.5f)
    {
        GameAudio.Play("gs_skysword", at);
        const float radius = 5f, fall = 0.28f;
        Color gold = Color.Lerp(StageOneColor, Color.white, 0.25f);
        GsFx("gs_runecircle", null, Floor(at, 0.05f), radius * 2f, StageTwoColor * 1.8f, fall + 0.25f, 0.5f, 1f, 90f, Flat(0f));
        VfxPiece sword = HasSheet("gs_giantsword")
            ? Vfx.Sprite("gs_giantsword", Floor(at, 14f), swordSize, gold * 2.4f, fall + (blast ? 1.4f : 2.2f), 1f, 1f, 0f).Hold()
            : null;
        if (sword != null) sword.GetComponent<Renderer>().sortingOrder = 3;
        Vector3 top = Floor(at, 14f), planted = Floor(at, swordSize * 0.38f);
        for (float t = 0f; t < fall; t += Time.deltaTime)
        {
            float k = t / fall;
            if (sword != null) sword.transform.position = Vector3.Lerp(top, planted, k * k);
            yield return null;
        }
        if (sword != null) sword.transform.position = planted;

        GsFx("gs_finalblow", "gs_quake", Floor(at, 0.1f), radius * 2.4f, gold * 2.6f, 1.2f, 0.55f, 1.05f, 0f, FlatRandom, 0.5f);
        HeavyImpact(at, radius * 2f, StageTwoColor, 0.5f);
        GsFx("gs_rupture", null, Floor(at, 0.12f), radius * 2.2f, StageTwoColor * 2f, 0.7f, 0.5f, 1.1f, 0f, FlatRandom);
        Pillar(at, 5.2f);
        for (int i = 0; i < 6; i++)
        {
            float a = (i * 60f + UnityEngine.Random.Range(-12f, 12f)) * Mathf.Deg2Rad;
            Eruption(at + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius * 0.7f, 2.2f, StageOneColor);
        }
        Shockwave.Spawn(at, radius, StageTwoColor, 34);
        if (!blast)
            yield break;   // (대지의 심판: its shockwave does the striking)
        var struck = new List<Enemy>();
        foreach (Enemy e in Enemy.Alive.ToArray())
        {
            if (e == null || e.IsDead) continue;
            Vector3 off = e.transform.position - at;
            off.y = 0f;
            if (off.magnitude > radius) continue;
            float centre = off.magnitude < 1.8f ? 1.4f : 1f;   // (the heart of it hits hardest)
            Strike(e, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool c) * 3.4f * centre)), c);
            struck.Add(e);
            if (!e.IsDead) e.Knockdown(knockdownSeconds);
        }
        ChainLightning(struck);
    }

    // The blade sticks in the ground for a beat after a slam (longer the fuller the charge), then the swing carries on
    private IEnumerator SlamStop(int stage, float resumeSpeed)
    {
        if (animator != null)
            animator.SetFloat(ChargeSpeedHash, 0f);
        yield return new WaitForSeconds(slamHitStop * (1f + stage * 0.5f));
        if (animator != null)
            animator.SetFloat(ChargeSpeedHash, resumeSpeed);
    }

    // 낙하: a second, wider shockwave a moment later that knocks everything off its feet
    private IEnumerator PlungeWave(float multiplier, List<Enemy> alreadyStruck)
    {
        yield return new WaitForSeconds(0.22f);
        float radius = shockRadiusFull + 1.5f;
        Shockwave.Spawn(transform.position, radius, StageTwoColor, 30);
        Quake(transform.position, radius * 2.3f, StageTwoColor * 1.8f, 0.7f);
        if (SheetName("gs_runecircle") != null)
            Vfx.Sprite("gs_runecircle", new Vector3(transform.position.x, Feet + 0.08f, transform.position.z), radius * 1.6f, StageTwoColor * 1.8f,
                0.7f, 0.6f, 1.1f, 60f, Quaternion.Euler(90f, 0f, 0f)).Sheet(4, 4);
        else
            Vfx.GroundRing(transform.position, radius * 0.8f, new Color(0.7f, 0.9f, 1f), 0.6f, "runes");
        // Lightning comes down round the ring
        for (int i = 0; i < 4; i++)
        {
            float a = (i * 90f + UnityEngine.Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
            Pillar(transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * radius * 0.6f, 3.6f);
        }
        Vfx.Dust(transform.position, radius * 0.6f, 18, new Color(0.75f, 0.8f, 0.9f, 0.5f));
        CameraShake.Shake(0.35f, 0.3f);
        var struck = new List<Enemy>();
        Wave(radius, shockDamageFull * 0.8f * multiplier, struck, true);
        if (GreatswordSkills.Has(GreatswordSkills.Sever))
            foreach (Enemy enemy in struck)
                if (enemy != null && !enemy.IsDead)
                    enemy.Interrupt();
    }

    // Damage every enemy within the radius that isn't in the list yet (and add it); optionally knock them all down
    private void Wave(float radius, float damageMultiplier, List<Enemy> struck, bool knockdown)
    {
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead)
                continue;
            Vector3 offset = enemy.transform.position - transform.position;
            offset.y = 0f;
            if (offset.magnitude > radius)
                continue;
            if (!struck.Contains(enemy))
            {
                Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * damageMultiplier)), critical);
                struck.Add(enemy);
            }
            if (knockdown && !enemy.IsDead)
                enemy.Knockdown(knockdownSeconds);
        }
    }

    // A glow at the feet while charging: dim while building up, orange at stage one, ice-blue at full charge
    private void Update()
    {
        if (chargeEnabled && ArmoryWeapon.Current == null)   // (an armory weapon's Q/E/R are its own)
            SkillKeys();
        // 연속 도약: the chain runs out (or its window closes) — the cooldown starts then
        if (leapChainLeft > 0 && Time.time > leapChainUntil && !IsSwinging)
        {
            leapChainLeft = 0;
            leapReadyAt = Time.time + LeapCooldown;
        }

        // Walking off after a combo blow cuts its follow-through short, so the legs don't slide in the pose
        if (recoveringState != null && !IsSwinging && animator != null)
        {
            bool stillInBlow = animator.GetCurrentAnimatorStateInfo(0).IsName(recoveringState);
            var controller = GetComponent<CharacterController>();
            Vector3 v = controller != null ? controller.velocity : Vector3.zero;
            v.y = 0f;
            if (!stillInBlow)
                recoveringState = null;
            else if (v.sqrMagnitude > 0.5f)
            {
                animator.CrossFadeInFixedTime("Locomotion", 0.12f);
                recoveringState = null;
            }
        }

        if (!IsCharging)
        {
            if (chargeCircle != null)
                ShowChargeFx(false);
            return;
        }
        if (chargeCircle == null || chargeAura == null || chargeAuraInk == null || chargeGlow == null)
        {
            if (chargeCircle != null) Destroy(chargeCircle.gameObject);
            if (chargeAura != null) Destroy(chargeAura.gameObject);
            if (chargeAuraInk != null) Destroy(chargeAuraInk.gameObject);
            if (chargeGlow != null) Destroy(chargeGlow.gameObject);
            chargeCircle = SheetName("gs_runecircle") != null
                ? Vfx.Decal(transform.position, 2f, Color.white, "gs_runecircle", 30f).Sheet(4, 4)
                : Vfx.Decal(transform.position, 2f, Color.white, "runes", 90f);
            string aura = SheetName("gs_aura");
            chargeAuraInk = Vfx.Sprite(aura + "_ink", transform.position, 3f, InkColor, 0f, 1f, 1f, 0f, additive: false).Sheet(4, 4);
            chargeAuraInk.transform.SetParent(transform, true);
            chargeAura = Vfx.Sprite(aura, transform.position, 3f, Color.white, 0f, 1f, 1f, 0f).Sheet(4, 4);
            chargeAura.transform.SetParent(transform, true);
            chargeAura.GetComponent<Renderer>().sortingOrder = 1;
            chargeGlow = Vfx.Glow(transform, Vector3.up * 1f, 2f, Color.white).GetComponent<VfxPiece>();
        }
        if (!chargeCircle.gameObject.activeSelf)
            ShowChargeFx(true);

        int stage = ChargeStage;
        Color color = stage == 2 ? StageTwoColor : stage == 1 ? StageOneColor : new Color(0.45f, 0.3f, 0.15f);

        // Preview what releasing now would hit: the blade's fan, plus 강타's and 낙하's shockwaves
        if (!slamRequested)
        {
            float shock = stage >= 1 && GreatswordSkills.Has(GreatswordSkills.Smash) ? (stage == 2 ? shockRadiusFull : shockRadius) : 0f;
            float plunge = stage == 2 && GreatswordSkills.Has(GreatswordSkills.Plunge) ? shockRadiusFull + 1.5f : 0f;
            RangeFx.Charging(Reach, stage == 0 ? AttackRangeFx.SwingColor : color, shock, plunge);
        }
        // Energy drawn in toward the raised blade the whole time it's held, brighter at each stage
        if (chargeGather == null && HasSheet("gs_charge"))
        {
            chargeGather = Vfx.Sprite("gs_charge", transform.position, 2.6f, Color.white, 0f, 1f, 1f, 0f).Sheet(4, 4).OpenUp(0.2f);
            chargeGather.GetComponent<Renderer>().sortingOrder = 2;
        }
        if (chargeGather != null)
        {
            chargeGather.gameObject.SetActive(!slamRequested);
            chargeGather.transform.position = transform.position + Vector3.up * 1.55f + transform.forward * 0.15f;
            chargeGather.Set(color * (1.6f + stage * 0.5f), 2.4f + stage * 0.5f, stage == 0 ? 0.6f : 1f);
        }
        float pulse = 1f + Mathf.Sin(Time.time * (6f + stage * 4f)) * 0.06f;
        chargeCircle.transform.position = new Vector3(transform.position.x, Feet + 0.05f, transform.position.z);
        chargeCircle.Set(color * 1.8f, (2.6f + stage * 0.9f) * pulse, stage == 0 ? 0.55f : 1f);
        // The aura stands just behind the body as the camera sees it, so the sword arm stays in front of it
        Camera cam = Camera.main;
        Vector3 back = cam != null ? Vector3.ProjectOnPlane(transform.position - cam.transform.position, Vector3.up).normalized * 0.4f : Vector3.zero;
        Vector3 auraAt = transform.position + Vector3.up * 0.45f + back;
        chargeAura.transform.position = chargeAuraInk.transform.position = auraAt;
        float auraSize = (2.4f + stage * 0.7f) * pulse;
        chargeAura.Set(color * (1.4f + stage * 0.4f), auraSize, stage == 0 ? 0.45f : 1f);
        chargeAuraInk.Set(InkColor, auraSize, stage == 0 ? 0.4f : 0.8f);
        chargeGlow.Set(color, (1.6f + stage * 0.6f) * pulse, stage == 0 ? 0.25f : 0.6f);
    }

    private void ShowChargeFx(bool on)
    {
        if (!on && chargeGather != null) chargeGather.gameObject.SetActive(false);
        chargeCircle.gameObject.SetActive(on);
        if (chargeAura != null) chargeAura.gameObject.SetActive(on);
        if (chargeAuraInk != null) chargeAuraInk.gameObject.SetActive(on);
        if (chargeGlow != null) chargeGlow.gameObject.SetActive(on);
    }

    // ── The greatsword's skill bar, laid out like the twin blades': Q E R, right button, Space ──

    private static readonly (string key, string name, string icon)[] Bar =
    {
        ("Q", "휠윈드", "gs_whirlwind"),
        ("E", "검기", "gs_wave"),
        ("R", "도약 강타", "gs_leap"),
        ("우클릭", "차징 강타", "gs_charge"),
        ("Space", "대시", "gs_rush"),
    };

    // The skill tree node that unlocks each slot of the bar
    private static readonly string[] BarNodes =
    {
        GreatswordSkills.Whirlwind, GreatswordSkills.Wave, GreatswordSkills.Leap, GreatswordSkills.Charge, GreatswordSkills.Dash,
    };

    private PlayerMovement movement;

    private (float left, float total) Cooldown(int slot)
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
        switch (slot)
        {
            case 0: return (Whirling ? 0f : WhirlwindCooldownLeft, WhirlwindCooldown);
            case 1: return (WaveCooldownLeft, WaveCooldown);
            case 2: return (LeapCooldownLeft, LeapCooldown);
            case 3: return (ChargeCooldownLeft, ChargeCooldownScaled);
            default: return (movement != null ? movement.DashCooldownLeft : 0f, movement != null ? movement.DashCooldown : 1f);
        }
    }

    private readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
    private GUIStyle keyStyle, timeStyle, badgeStyle;

    private Texture2D Icon(string name)
    {
        if (!icons.TryGetValue(name, out Texture2D icon))
            icons[name] = icon = Resources.Load<Texture2D>("UI/" + name);
        return icon;
    }

    private void OnGUI()
    {
        if (!chargeEnabled || SkillBar.Hidden)
            return;
        int stage = ChargeStage;
        Color stageColor = stage == 2 ? StageTwoColor : stage == 1 ? StageOneColor : new Color(0.85f, 0.75f, 0.55f);
        for (int i = 0; i < Bar.Length; i++)
        {
            var (key, name, iconName) = Bar[i];
            bool charge = i == 3, dash = i == 4;
            int spi = i < 4 ? Sp(SkillSystem.WeaponKeys[i]) : -1;
            if (spi >= 0) name = SpecialNames[i][spi];
            // While charging, Space is 돌진 (once learned)
            if (dash && IsCharging && GreatswordSkills.Has(GreatswordSkills.Rush))
                name = "돌진";
            var (left, total) = Cooldown(i);
            string note = i == 2 && leapChainLeft > 0 ? $"{leapChainLeft}회" : null;
            barSlots[i] = new SkillBar.Slot
            {
                note = note,
                key = key, name = name, icon = Icon(iconName), learned = GreatswordSkills.Has(BarNodes[i]), left = left, total = total,
                lit = (i == 0 && Whirling) || (charge && IsCharging) || (dash && CanRush), litColor = stageColor,
                fill = charge && IsCharging ? Mathf.Clamp01(ChargeHeld / ChargeFullTime) : 0f, fillColor = stageColor,
            };
        }
        SkillBar.Draw(barSlots);
    }

    private readonly SkillBar.Slot[] barSlots = new SkillBar.Slot[5];

    private void Face(Vector3 point, float degreesPerSecond)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), degreesPerSecond * Time.deltaTime);
    }

    // Blade hits on every enemy in reach in front, at this multiple of a normal hit
    // (a wider arc or a longer reach for the combo's sweeping blows)
    private List<Enemy> HitFront(float damageMultiplier, float arc = 0f, float extraReach = 0f)
    {
        if (arc <= 0f)
            arc = frontArc;
        var struck = new List<Enemy>();
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead || !CanReach(enemy, Reach + extraReach))
                continue;

            Vector3 toEnemy = enemy.transform.position - transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude > 0.25f && Vector3.Angle(transform.forward, toEnemy) > arc / 2f)
                continue;
            int damage = Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * damageMultiplier));
            Strike(enemy, damage, critical);
            struck.Add(enemy);
        }
        return struck;
    }

    private void Strike(Enemy enemy, int damage, bool critical)
    {
        // Sparks off the body where the blade bites (a critical blow bursts gold)
        Vector3 at = enemy.transform.position + Vector3.up * (1f * enemy.transform.localScale.y);
        Vector3 toward = (at - transform.position).normalized;
        Vfx.Flash(at - toward * 0.3f, critical ? 1.6f : 1f, critical ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.95f, 0.85f), 0.12f);
        Vfx.Burst(at, critical ? new Color(1f, 0.75f, 0.25f) : new Color(1f, 0.9f, 0.7f), critical ? 18 : 9, critical ? 8f : 6f, 0.14f);
        // The greatsword's blow bursts on the body in a painted flash (the twin blades draw their own)
        if (chargeEnabled)
            Vfx.Sprite("hitsheet", at - toward * 0.2f, critical ? 2.6f : 1.9f, (critical ? new Color(1f, 0.75f, 0.25f) : StageOneColor) * 1.7f,
                0.3f, 0.85f, 1.15f, UnityEngine.Random.Range(-40f, 40f)).Sheet(4, 4);
        enemy.TakeHit(transform.position, damage);
        HitEnemy?.Invoke(enemy, damage, critical);
    }

    // For other weapons on the same player (the twin blades): the same damage roll, hit sparks, damage numbers and
    // Thor's chain lightning as the greatsword's
    public float RollWeaponDamage(out bool critical) => RollDamage(out critical);
    public void StrikeEnemy(Enemy enemy, int damage, bool critical) => Strike(enemy, damage, critical);
    public void ChainFrom(List<Enemy> struck) => ChainLightning(struck);

    // Thor's runes: the blow arcs on to the nearest enemies that weren't hit
    private void ChainLightning(List<Enemy> struck)
    {
        int jumps = RunState.Stats.chainTargets;
        if (jumps <= 0 || stats == null || struck.Count == 0)
            return;

        int damage = Mathf.Max(1, Mathf.RoundToInt(stats.Damage * RunState.Stats.chainDamageFraction));
        Vector3 from = struck[0].transform.position;
        for (int i = 0; i < jumps; i++)
        {
            Enemy next = null;
            float best = chainRange;
            foreach (Enemy enemy in Enemy.Alive)
            {
                if (enemy == null || enemy.IsDead || struck.Contains(enemy)) continue;
                float d = Vector3.Distance(from, enemy.transform.position);
                if (d < best) { best = d; next = enemy; }
            }
            if (next == null)
                return;

            LightningFx.Bolt(from + Vector3.up * 0.8f, next.transform.position + Vector3.up * 0.8f);
            Strike(next, damage, false);
            struck.Add(next);
            from = next.transform.position;
        }
    }

    private void SwingAt(Component target, int trigger, float impact, float duration, Action onImpact)
    {
        if (IsSwinging || !CanReach(target))
            return;

        // Attack speed shortens both the animation and the hit timing
        float speed = AttackSpeed;
        if (animator != null)
            animator.SetFloat(AttackSpeedHash, speed);
        // The target may have died or moved out of reach during the wind-up
        StartCoroutine(Swing(target.transform.position, trigger, impact / speed, duration / speed, () =>
        {
            if (target != null && CanReach(target))
                onImpact();
        }));
    }

    private IEnumerator Swing(Vector3 aimPoint, int trigger, float impact, float duration, Action onImpact)
    {
        IsSwinging = true;

        Vector3 toTarget = aimPoint - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);

        if (animator != null)
            animator.SetTrigger(trigger);

        yield return new WaitForSeconds(impact);
        onImpact();

        yield return new WaitForSeconds(Mathf.Max(0f, duration - impact));
        IsSwinging = false;
    }
}

// 검기: a golden crescent of blade light racing flat along the ground, cutting each enemy in its path once
public class GreatswordWave : MonoBehaviour
{
    private static readonly Color Gold = new Color(1f, 0.6f, 0.15f);

    private PlayerMiner owner;
    private Vector3 dir;
    private float travelled, range, width, speed, damage;
    private readonly HashSet<Enemy> hit = new HashSet<Enemy>();
    private VfxPiece glow, ink;
    private bool pierce, crack;       // 관통 검기 / 대지 절단 (a plain wave stops at the first enemy)
    private float decay = 1f;         // each enemy passed through leaves this much of the damage
    private Vector3 start;

    private bool compress;            // 압축 검기: its own condensed blade, bursting on what it hits
    private float nextMark;           // where the next puff of dust (or length of split ground) is laid

    private static bool Has(string sheet) => Resources.Load<Texture2D>("Vfx/" + sheet) != null;

    public static void Fire(PlayerMiner owner, Vector3 from, float height, Vector3 direction, float range, float width, float speed, float damage,
        bool pierce = false, float decay = 1f, bool crack = false, string look = null)
    {
        var go = new GameObject("GreatswordWave");
        go.transform.position = new Vector3(from.x, height, from.z);
        var wave = go.AddComponent<GreatswordWave>();
        wave.owner = owner;
        wave.dir = direction;
        wave.range = range;
        wave.width = width;
        wave.speed = speed;
        wave.damage = damage;
        wave.pierce = pierce;
        wave.decay = decay;
        wave.crack = crack;
        wave.start = go.transform.position;
        wave.compress = look == "gs_compress";
        // The painted crescent bows toward the bottom of its frame: that edge leads. The heavy VARCO wave (or 압축's own
        // blade) leads, the old golden crescent glows under it.
        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        Quaternion lying = Quaternion.Euler(90f, yaw + 180f, 0f);
        string sheet = look ?? "gs_heavywave";
        if (!Has(sheet)) sheet = "gs_wave";
        float size = width * (wave.compress ? 2.2f : 1.7f);
        wave.size = size;
        wave.inkTint = new Color(0.16f, 0.08f, 0.02f, wave.compress ? 0.55f : 0.65f);
        wave.ink = Vfx.Sprite(Has(sheet + "_ink") ? sheet + "_ink" : "gs_wave_ink", go.transform.position, size, wave.inkTint, 0f, 1f, 1f, 0f, lying, additive: false).Sheet(4, 4).OpenUp(0.08f);
        wave.glow = Vfx.Sprite(sheet, go.transform.position, size, (wave.compress ? Color.Lerp(Gold, Color.white, 0.4f) : Gold) * 2.5f, 0f, 1f, 1f, 0f, lying).Sheet(4, 4).OpenUp(0.08f);
        wave.glow.GetComponent<Renderer>().sortingOrder = 2;
        wave.ink.transform.SetParent(go.transform, true);
        wave.glow.transform.SetParent(go.transform, true);
        if (sheet != "gs_wave" && !wave.compress)
        {
            wave.under = Vfx.Sprite("gs_wave", go.transform.position, size * 0.85f, Gold * 1.3f, 0f, 1f, 1f, 0f, lying).Sheet(4, 4).OpenUp(0.08f);
            wave.under.GetComponent<Renderer>().sortingOrder = 1;
            wave.under.transform.SetParent(go.transform, true);
        }
        Vfx.Trail(go, Gold, width * (wave.compress ? 0.2f : 0.3f), 0.16f);
    }

    private VfxPiece under;
    private float size;
    private Color inkTint;

    private void Update()
    {
        float step = speed * Time.deltaTime;
        transform.position += dir * step;
        travelled += step;
        // It tears along the floor: dust kicked up behind it — and for 대지 절단 the ground split open in its wake, held
        // until the whole length bursts
        if (owner != null && travelled >= nextMark)
        {
            nextMark = travelled + (crack ? 1.6f : 1.3f);
            if (crack)
                owner.LaySplit(transform.position, dir, 1.3f, (range - travelled) / Mathf.Max(1f, speed) + 0.95f);
            else
                Vfx.Dust(new Vector3(transform.position.x, transform.position.y - 0.5f, transform.position.z), width * 0.25f, 3);
        }
        if (owner != null)
        {
            var struck = new List<Enemy>();
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy.IsDead || hit.Contains(enemy))
                    continue;
                Vector3 offset = enemy.transform.position - transform.position;
                offset.y = 0f;
                float along = Vector3.Dot(offset, dir);
                float across = (offset - dir * along).magnitude;
                if (along > 1.2f || along < -step - 0.6f || across > width * 0.5f + 0.4f)
                    continue;
                hit.Add(enemy);
                struck.Add(enemy);
                owner.StrikeEnemy(enemy, Mathf.Max(1, Mathf.RoundToInt(owner.RollWeaponDamage(out bool critical) * damage)), critical);
                damage *= decay;
                if (!pierce) break;
            }
            if (struck.Count > 0)
            {
                CameraShake.Shake(0.08f, 0.06f);
                owner.ChainFrom(struck);
                if (!pierce)
                {
                    Vfx.Flash(transform.position + Vector3.up * 0.4f, 2f, Gold, 0.15f);
                    if (compress) owner.CompressBurst(struck[0].transform.position);
                    Destroy(gameObject);
                    return;
                }
            }
        }
        // Thins out over the last stretch, then it's gone
        float fade = Mathf.Clamp01((range - travelled) / (range * 0.25f));
        Color glowColor = (compress ? Color.Lerp(Gold, Color.white, 0.4f) : Gold) * 2.5f;
        if (glow != null) glow.Set(glowColor, size * Mathf.Lerp(0.7f, 1f, fade), fade);
        if (under != null) under.Set(Gold * 1.3f, size * 0.85f * Mathf.Lerp(0.7f, 1f, fade), fade);
        if (ink != null) ink.Set(inkTint, size * Mathf.Lerp(0.7f, 1f, fade), fade);
        if (travelled >= range)
        {
            if (crack && owner != null)
                owner.StartCoroutine(owner.CrackBurst(start, transform.position, 1.3f, 1.3f, 0.7f, alreadySplit: true));
            Destroy(gameObject);
        }
    }
}
