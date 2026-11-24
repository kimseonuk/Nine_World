using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

// 그림자 쌍검사's twin blades: burst damage and mobility. A quick three-blow combo on the attack button, and five skills:
//  난무 (Q)       — rooted for a moment, a storm of cuts in front of the player
//  광폭화 (R)     — for a while: more damage, move speed and attack speed
//  처형 (right-click, hold) — charge, then a crimson sword wave that finishes off every enemy below a health threshold
//  돌진 (E)       — dash forward cutting everything on the way; a hit gives back part of its cooldown
//  회피 (Space)   — roll clear, untouchable, leaving a shadow clone that strikes where the player stood
// The effects are VARCO flipbooks, 4x4 frames each (Resources/Vfx: slashsheet, spinsheet, hitsheet, dashsheet, flurrysheet,
// roarsheet, auraflame, chargesheet, marksheet, executesheet, shadowsmoke), a dashstreak band and icons (Resources/UI/dual_*).
// The combo's five blows are clips cut from VARCO motions (Animations/ShadowBlade/Blow*.anim). The combo's 2nd to 5th
// blows, the five skills and their upgrades are all learned in the skill tree (DualBladeSkills, [K]).
[RequireComponent(typeof(PlayerMiner))]
public class DualBlades : MonoBehaviour
{
    private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
    public static readonly Color Crimson = new Color(1f, 0.22f, 0.28f);
    public static readonly Color Violet = new Color(0.62f, 0.32f, 1f);

    public enum Skill { Flurry, Berserk, Execute, Dash, Dodge }

    // A floating word over the player or an enemy (처형!, 광폭화!): GameManager draws it
    public static event Action<Vector3, string, Color> Announce;
    public static void Say(Vector3 at, string text, Color color) => Announce?.Invoke(at, text, color);

    // One blow of the combo. Each plays a clip cut from a VARCO motion (windup to follow-through, its facing drift
    // baked out so every blow starts and ends square to the front: Animations/ShadowBlade/Blow*.anim), so the blows
    // chain without the body snapping round. Fractions of the clip: when the blades land, when the next blow may cut
    // in (cancel), and the stretch over which the body carries forward (lunge metres from lungeFrom to lungeTo).
    private enum Cut3 { Slash, Stab, Spin, Leap }

    private struct Blow
    {
        public string state;
        public Cut3 kind;
        public float pace, cancel, damage, arc, reach, lunge, lungeFrom, lungeTo;
        public float[] impacts;
        public float[] tilts;
    }

    private static readonly Blow[] Blows =
    {
        // sword_slash: one wide cut across
        new Blow { state = "Blow1_Slash", kind = Cut3.Slash, pace = 1.7f, impacts = new[] { 0.56f }, tilts = new[] { -20f }, cancel = 0.72f,
            damage = 1f, arc = 160f, lunge = 0.45f, lungeFrom = 0.4f, lungeTo = 0.56f },
        // double_slash: a cut, a turn, a cut back
        new Blow { state = "Blow2_Double", kind = Cut3.Slash, pace = 2.6f, impacts = new[] { 0.3f, 0.8f }, tilts = new[] { 20f, -30f }, cancel = 0.88f,
            damage = 0.95f, arc = 150f, lunge = 0.35f, lungeFrom = 0.18f, lungeTo = 0.3f },
        // boxing_punch: three thrusts of the blades
        new Blow { state = "Blow3_Stabs", kind = Cut3.Stab, pace = 2.4f, impacts = new[] { 0.3f, 0.75f }, tilts = new[] { 0f, 8f }, cancel = 0.9f,
            damage = 0.95f, arc = 70f, reach = 0.7f, lunge = 0.5f, lungeFrom = 0.6f, lungeTo = 0.82f },
        // punch_and_kick: a full turn, blades out, all round
        new Blow { state = "Blow4_SpinKick", kind = Cut3.Spin, pace = 2f, impacts = new[] { 0.45f }, tilts = new[] { 0f }, cancel = 0.78f,
            damage = 2.3f, arc = 360f, reach = 0.5f, lunge = 0.3f, lungeFrom = 0.3f, lungeTo = 0.58f },
        // jump_stab_land: the finisher, a leap and both blades driven into the ground
        new Blow { state = "Blow5_Leap", kind = Cut3.Leap, pace = 1.9f, impacts = new[] { 0.68f }, tilts = new[] { 0f }, cancel = 1f,
            damage = 3.5f, arc = 360f, reach = 1.2f, lunge = 1.6f, lungeFrom = 0.24f, lungeTo = 0.66f },
    };

    [Header("Combo")]
    [SerializeField] private float reach = 1.9f;
    [SerializeField] private float comboWindow = 0.6f;

    [Header("난무")]
    [SerializeField] private float flurryTime = 1.6f;
    [SerializeField] private float flurryTick = 0.11f;
    [SerializeField] private float flurryRange = 3.2f;
    [SerializeField] private float flurryDamage = 0.45f;
    [SerializeField] private float flurryCooldown = 9f;

    [Header("광폭화")]
    [SerializeField] private float berserkTime = 6f;
    [SerializeField] private float berserkDamage = 1.4f;
    [SerializeField] private float berserkMoveSpeed = 1.3f;
    [SerializeField] private float berserkAttackSpeed = 1.35f;
    [SerializeField] private float berserkCooldown = 18f;

    [Header("처형")]
    [SerializeField] private float executeCharge = 0.6f;
    [SerializeField] private float executeThreshold = 0.3f;
    [SerializeField] private float executeBossThreshold = 0.08f;
    [SerializeField] private float executeDamage = 2.5f;
    [SerializeField] private float executeRange = 14f;
    [SerializeField] private float executeWidth = 3.6f;
    [SerializeField] private float executeCooldown = 10f;

    [Header("돌진")]
    [SerializeField] private float dashDistance = 7f;
    [SerializeField] private float dashTime = 0.22f;
    [SerializeField] private float dashDamage = 1.2f;
    [SerializeField] private float dashRefund = 0.5f;
    [SerializeField] private float dashCooldown = 5f;

    [Header("회피")]
    [SerializeField] private float dodgeDistance = 4.5f;
    [SerializeField] private float dodgeTime = 0.38f;
    [SerializeField] private float dodgeCooldown = 1.1f;
    [SerializeField] private float cloneStrikeDelay = 0.45f;
    [SerializeField] private float cloneRadius = 2.4f;
    [SerializeField] private float cloneDamage = 0.8f;

    private PlayerMiner miner;
    private PlayerStats stats;
    private CharacterController controller;
    private ClickToDig aim;
    private Animator animator;
    private CharacterProfile profile;
    private readonly Dictionary<string, float> clipLengths = new Dictionary<string, float>();
    private readonly Dictionary<Skill, float> readyAt = new Dictionary<Skill, float>();
    private readonly Dictionary<Skill, float> cooldownOf = new Dictionary<Skill, float>();

    private bool active;
    private bool inBlow, queued, casting;
    private Vector3 queuedAim;
    private int comboStep;
    private float comboExpires;
    private float invulnerableUntil, berserkUntil;
    private int frenzyStacks;                 // 피의 광기
    private float berserkExtended;            // 살육 본능: seconds added so far
    private float exhaustedUntil;             // 폭주: after it ends
    private int dashChainLeft;                // 삼연귀참
    private float dashChainUntil;
    private Coroutine running;
    private GameObject berserkAura;
    private VfxPiece berserkRing, berserkFlames, berserkFlamesInk;
    private TrailRenderer[] bladeTrails = new TrailRenderer[0];

    public bool IsActive => active && isActiveAndEnabled;
    public bool Rooted { get; private set; }
    public bool Invulnerable => Time.time < invulnerableUntil;
    public bool Berserk => Time.time < berserkUntil;
    private bool Exhausted => Time.time < exhaustedUntil;
    private const float ExhaustSeconds = 5f;      // 폭주's exhaustion
    private bool movingFlurry;                    // 질풍난무: the flurry while walking
    // (그림자 광폭화 calls up a clone instead: the boon itself doesn't apply)
    private bool BerserkBoon => Berserk && Sp('R') != 2;
    public float MoveMultiplier => movingFlurry ? 0.8f
        : BerserkBoon ? (Sp('R') == 3 ? 1f : berserkMoveSpeed) : Exhausted ? 0.45f : 1f;

    private float AttackSpeed => (stats != null ? stats.AttackSpeed : 1f) * PlayerEffects.Slow
        * (BerserkBoon ? (Sp('R') == 3 ? 1.75f : berserkAttackSpeed) : Exhausted ? 0.5f : 1f) * (DualBladeSkills.Has(DualBladeSkills.Mastery) ? 1.1f : 1f);

    // 삼연귀참: three charges, each coming back on its own 5 seconds after it was spent
    private const int DashCharges = 3;
    private int dashCharges = DashCharges;
    private readonly Queue<float> dashRecharge = new Queue<float>();
    private int lastDashSpecial = -2;
    public int DashChargesLeft => dashCharges;

    // 그림자 광폭화's clone, while it's out
    private ShadowAlly ally;

    // ── 특수 강화 (기획서 5장): four versions per skill, chosen at a boss — the index is the table's row ──
    public static readonly string[][] SpecialNames =
    {
        new[] { "회전난무", "그림자 분신", "질풍난무", "집중 난무" },          // Q 난무
        new[] { "삼연귀참", "일섬", "잔영참", "회전섬" },                      // E 귀신베기
        new[] { "피의 광기", "살육 본능", "그림자 광폭화", "폭주" },           // R 광폭화
        new[] { "속사 검기", "월광참", "추적 검기", "폭발 검기" },             // 우클릭 검기
    };
    public static readonly string[][] SpecialDescs =
    {
        new[] { "제자리에서 회전하며 주변의 모든 적을 연속 공격합니다 (방향 지정 없음)",
                "난무 중 분신이 나타나 반대 방향의 적을 함께 공격합니다 (분신 피해는 본체보다 낮음)",
                "움직이면서 난무를 휘두릅니다 (피해 감소, 이동속도 약간 감소)",
                "범위가 좁아지는 대신 한 대상에게 공격을 집중하고, 마지막 타격이 강력한 마무리가 됩니다" },
        new[] { "귀신베기를 3회까지 충전해 씁니다 — 쓴 횟수마다 5초씩 따로 회복됩니다 (이동 거리·피해 감소)",
                "E를 누르고 있으면 기를 모읍니다. 끝까지 모으면 시간이 느려지고 화면이 흑백이 되며, 일직선의 모든 적을 베는 강력한 일섬을 날립니다",
                "돌진한 경로에 잔상이 남아 잠시 뒤 같은 경로를 다시 벱니다",
                "돌진이 끝난 자리에서 회전 베기로 주변의 적을 함께 공격합니다" },
        new[] { "공격할 때마다 광기 중첩이 쌓여 공격력이 오르고, 피격 시 중첩 일부를 잃습니다",
                "광폭화 중 적을 처치하면 지속시간이 늘어납니다 (최대 +5초)",
                "광폭화 대신 그림자 분신을 소환합니다. 분신은 스스로 적을 찾아 기본 공격을 하고, 내가 쓰는 스킬을 기본형으로 따라 씁니다 (광폭화 강화 효과 없음)",
                "공격력·공격속도가 크게 오르지만 이동속도 증가가 없고, 끝나면 5초간 탈진해 공격속도·이동속도가 크게 떨어집니다" },
        new[] { "차징 없이 바로 작은 검기를 날립니다 (재사용 대기시간 감소, 피해 감소)",
                "차징이 길어지는 대신 넓고 거대한 초승달 검기로 여러 적을 벱니다",
                "검기가 가장 가까운 적을 향해 휘어집니다 (직선보다 피해 감소)",
                "검기가 적중한 지점에서 폭발해 주변 적에게도 피해를 줍니다" },
    };

    // This fighter's special version for a skill key (Q E R M), -1 when none
    private int Sp(char key) => profile != null ? SkillSystem.SpecialOf(profile.name + "/" + key) : -1;

    private void Awake()
    {
        SkillSystem.RegisterSpecials("Model_Shadow", SpecialNames, SpecialDescs);
        miner = GetComponent<PlayerMiner>();
        stats = GetComponent<PlayerStats>();
        controller = GetComponent<CharacterController>();
        aim = FindAnyObjectByType<ClickToDig>();
        cooldownOf[Skill.Flurry] = flurryCooldown;
        cooldownOf[Skill.Berserk] = berserkCooldown;
        cooldownOf[Skill.Execute] = executeCooldown;
        cooldownOf[Skill.Dash] = dashCooldown;
        cooldownOf[Skill.Dodge] = dodgeCooldown;
        if (stats != null) stats.Damaged += OnDamaged;
    }

    // 피의 광기: a blow taken costs part of the madness
    private void OnDamaged()
    {
        if (frenzyStacks > 0) frenzyStacks = Mathf.Max(0, frenzyStacks - 5);
    }

    // Called when the player's character changes: the blades only work for the character that carries them
    public void SetCharacter(CharacterProfile character)
    {
        ResetCombat();
        profile = character;
        active = character != null && character.Weapon == CharacterProfile.WeaponKind.DualBlades;
        DualBladeSkills.Active = active;   // the tree's stats only count while the blades are in hand
        animator = character != null ? character.Animator : null;
        clipLengths.Clear();
        if (animator != null && animator.runtimeAnimatorController != null)
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                clipLengths[clip.name] = clip.length;
        var trails = new List<TrailRenderer>();
        if (active)
            foreach (Transform t in character.GetComponentsInChildren<Transform>(true))
                if (t.name == "BladeTip")
                {
                    TrailRenderer trail = t.GetComponent<TrailRenderer>();
                    if (trail == null)
                        trail = Vfx.Trail(t.gameObject, Violet, 0.22f, 0.14f);
                    trail.emitting = false;
                    trails.Add(trail);
                }
        bladeTrails = trails.ToArray();
    }

    public void ResetCombat()
    {
        StopAllCoroutines();
        running = null;
        inBlow = queued = casting = false;
        Rooted = false;
        comboStep = 0;
        invulnerableUntil = 0f;
        frenzyStacks = 0;
        exhaustedUntil = 0f;
        dashChainLeft = 0;
        dashCharges = DashCharges;
        dashRecharge.Clear();
        movingFlurry = false;
        if (ally != null) Destroy(ally.gameObject);
        ally = null;
        IaidoEnd();
        CinematicFx.Clear();
        readyAt.Clear();
        EndBerserk();
        SetTrails(false);
        PassThroughEnemies(false);
        ClearExecuteMarks();
        if (executePreview != null)
            Destroy(executePreview);
        if (animator != null && animator.isActiveAndEnabled)
        {
            animator.SetFloat(AttackSpeedHash, 1f);
            animator.CrossFadeInFixedTime("Locomotion", 0.1f);
        }
    }

    // ───────────── Input ─────────────

    private void Update()
    {
        if (!IsActive)
            return;
        UpdateBerserk();
        UpdateDashCharges();
        UpdateExhaustion();
        if (!GameManager.IsRunActive || Time.timeScale <= 0f)
            return;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null)
            return;

        // (held fast by a boss's chains: no roll and no dash strike until it wears off)
        bool rooted = PlayerEffects.Rooted;
        if (SkillSystem.DodgeDown(keyboard))
        {
            if (!rooted)
                TryStart(Skill.Dodge, Dodge(MoveInput(keyboard)), interrupt: true);
        }
        else if (SkillSystem.KeyDown(keyboard, 'Q'))
            TryStart(Skill.Flurry, Flurry());
        else if (SkillSystem.KeyDown(keyboard, 'R'))
            TryStart(Skill.Berserk, BerserkRoar());
        else if (SkillSystem.KeyDown(keyboard, 'E') && !rooted)
        {
            // 일섬: hold E to gather it; 삼연귀참: a charge spent, its own cooldown runs (neither starts the usual one)
            if (Sp('E') == 1) TryStart(Skill.Dash, IssenCharge(), cooldownLater: true);
            else TryStart(Skill.Dash, DashStrike(Cursor()), cooldownLater: Sp('E') == 0);
        }
        else if (SkillSystem.RightPressed(mouse))
        {
            // 속사 검기: no charge, straight out
            if (Sp('M') == 0) TryStart(Skill.Execute, QuickWave());
            else TryStart(Skill.Execute, Execute(), cooldownLater: true);
        }
    }

    // 삼연귀참's charges coming back one by one; any other version uses the plain cooldown
    private void UpdateDashCharges()
    {
        int sp = Sp('E');
        if (sp != lastDashSpecial)
        {
            lastDashSpecial = sp;
            dashCharges = DashCharges;
            dashRecharge.Clear();
            if (sp == 0) readyAt.Remove(Skill.Dash);
        }
        if (sp != 0) return;
        while (dashRecharge.Count > 0 && dashRecharge.Peek() <= Time.time)
        {
            dashRecharge.Dequeue();
            dashCharges = Mathf.Min(DashCharges, dashCharges + 1);
        }
        if (dashCharges > 0) readyAt.Remove(Skill.Dash);
        else if (dashRecharge.Count > 0) readyAt[Skill.Dash] = dashRecharge.Peek();
    }

    // 폭주's exhaustion shows: grey breath at the feet now and then
    private float nextExhaustPuff;
    private void UpdateExhaustion()
    {
        if (!Exhausted || Time.time < nextExhaustPuff) return;
        nextExhaustPuff = Time.time + 0.45f;
        Vfx.Sprite("shadowsmoke", transform.position + Vector3.up * Random.Range(-0.3f, 0.6f), 1.6f, new Color(0.45f, 0.45f, 0.5f, 0.75f),
            0.6f, 0.7f, 1.2f, 0f, additive: false).Sheet(4, 4);
    }

    // Each skill is learned in the skill tree before it can be used
    public static string NodeOf(Skill skill) => skill switch
    {
        Skill.Flurry => DualBladeSkills.Flurry,
        Skill.Berserk => DualBladeSkills.Berserk,
        Skill.Execute => DualBladeSkills.Execute,
        Skill.Dash => DualBladeSkills.Dash,
        _ => DualBladeSkills.Dodge,
    };

    public static bool Learned(Skill skill) => DualBladeSkills.Has(NodeOf(skill));

    public bool Ready(Skill skill) => !readyAt.TryGetValue(skill, out float at) || Time.time >= at;

#if UNITY_EDITOR
    // (editor tests, e.g. DpsProbe: use a skill as its key would, aimed wherever SkillCast.AimOverride points)
    public bool EditorTestBusy => casting || inBlow;
    public bool EditorTestLearned(Skill skill) => Learned(skill);
    public void EditorTestUse(Skill skill)
    {
        switch (skill)
        {
            case Skill.Dodge: TryStart(Skill.Dodge, Dodge(transform.forward), interrupt: true); break;
            case Skill.Flurry: TryStart(Skill.Flurry, Flurry()); break;
            case Skill.Berserk: TryStart(Skill.Berserk, BerserkRoar()); break;
            case Skill.Dash: TryStart(Skill.Dash, DashStrike(Cursor()), cooldownLater: Sp('E') == 0); break;
            case Skill.Execute: break;   // (the charged 처형 needs the button held: not driven here)
        }
    }
#endif
    public float CooldownLeft(Skill skill) => readyAt.TryGetValue(skill, out float at) ? Mathf.Max(0f, at - Time.time) : 0f;
    // Read the serialized fields directly: the dictionary is lost when scripts reload during Play
    private float BaseCooldown(Skill skill) => skill switch
    {
        Skill.Flurry => flurryCooldown,
        Skill.Berserk => berserkCooldown,
        Skill.Execute => executeCooldown,
        Skill.Dash => dashCooldown,
        _ => dodgeCooldown,
    };

    public float CooldownOf(Skill skill) => (cooldownOf.TryGetValue(skill, out float cd) ? cd : BaseCooldown(skill)) *(skill == Skill.Dodge && DualBladeSkills.Has(DualBladeSkills.Shadowstep) ? 0.7f : 1f)
        * (skill == Skill.Execute && Sp('M') == 0 ? 0.4f : 1f);   // 속사 검기: much sooner again

    private void StartCooldown(Skill skill, float fraction = 1f) => readyAt[skill] = Time.time + CooldownOf(skill) * fraction;

    // A skill goes if it's ready and nothing else holds the player (a dodge may cut a combo blow or a flurry short)
    private float lockedNoticeAt;

    private void TryStart(Skill skill, IEnumerator routine, bool interrupt = false, bool cooldownLater = false)
    {
        if (!Learned(skill))
        {
            if (Time.time >= lockedNoticeAt)
            {
                lockedNoticeAt = Time.time + 1f;
                Announce?.Invoke(transform.position + Vector3.up * 2f, "아직 쓸 수 없습니다", new Color(0.75f, 0.75f, 0.8f));
            }
            return;
        }
        if (!Ready(skill))
            return;
        if (!interrupt && (casting || inBlow))
            return;
        if (interrupt && (running != null || inBlow))
            CancelCurrent();
        if (!cooldownLater)
            StartCooldown(skill);
        running = StartCoroutine(Run(routine));
    }

    private IEnumerator Run(IEnumerator routine)
    {
        casting = true;
        yield return routine;
        casting = false;
        movingFlurry = false;
        Rooted = false;
        running = null;
        SetTrails(false);
        PassThroughEnemies(false);
    }

    private void CancelCurrent()
    {
        StopAllCoroutines();
        running = null;
        inBlow = queued = casting = false;
        Rooted = false;
        movingFlurry = false;
        IaidoEnd();
        CinematicFx.Clear();
        SetTrails(false);
        PassThroughEnemies(false);
        ClearExecuteMarks();
        if (executePreview != null)
            Destroy(executePreview);
    }

    // (SkillCast.AimOverride: a fixed aim for tests; null in play)
    private Vector3 Cursor() => SkillCast.AimOverride ?? (aim != null ? aim.CursorGroundPoint() : transform.position + transform.forward * 3f);

    private Vector3 MoveInput(Keyboard keyboard)
    {
        var v = new Vector3((keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f), 0f,
            (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
        if (CameraModeController.FirstPersonActive)
            v = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * v;
        return v.sqrMagnitude > 0.01f ? v.normalized : transform.forward;
    }

    private void Face(Vector3 point, float degreesPerSecond = 9999f)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), degreesPerSecond * Time.deltaTime);
    }

    // start: where in the clip to begin (a fraction), skipping the VARCO clip's lead-in
    private void Play(string state, float speed, float fade = 0.06f, float start = 0f)
    {
        if (animator == null)
            return;
        animator.SetFloat(AttackSpeedHash, speed);
        animator.CrossFadeInFixedTime(state, fade, 0, start * ClipLength(state));
    }

    // Where each skill's motion begins in its clip and how fast it plays (measured from the VARCO clips)
    private const float DashStart = 0.12f, ReleaseStart = 0.12f, ChargeStart = 0.08f, ChargeHold = 0.35f,
        BerserkStart = 0.44f, DodgeStart = 0.1f, FlurryStart = 0.05f;

    private float ClipLength(string state) => clipLengths.TryGetValue(state, out float l) ? l : 1f;

    // A painted cut that reads on any floor: a dark ink copy underneath (the painting's brightness as its shape,
    // so it shows on Asgard's white marble) and the additive glow on top (so it shines in the dark)
    public static VfxPiece Cut(string texture, Vector3 at, float size, Color color, float seconds, float startScale, float endScale,
        float spin, Quaternion? lying = null, int sheet = 0, Color? inkColor = null)
    {
        float peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b, 0.01f));
        Color hue = color / peak;
        var ink = Vfx.Sprite(texture + "_ink", at, size, inkColor ?? new Color(hue.r * 0.3f, hue.g * 0.12f, hue.b * 0.38f, 0.8f),
            seconds, startScale, endScale, spin, lying, additive: false);
        var glow = Vfx.Sprite(texture, at, size, color, seconds, startScale, endScale, spin, lying);
        glow.GetComponent<Renderer>().sortingOrder = 1;
        if (sheet > 0)
        {
            ink.Sheet(sheet, sheet);
            glow.Sheet(sheet, sheet);
        }
        return glow;
    }

    private void SetTrails(bool on)
    {
        foreach (TrailRenderer trail in bladeTrails)
            if (trail != null)
            {
                trail.emitting = on;
                trail.startColor = Berserk ? Crimson * 1.6f : Violet * 1.6f;
            }
    }

    // ───────────── The combo ─────────────

    public void Tap(Vector3 aimPoint)
    {
        if (!IsActive || !GameManager.IsRunActive)
            return;
        if (inBlow || casting)
        {
            if (inBlow)
            {
                queued = true;
                queuedAim = aimPoint;
            }
            return;
        }
        // Tapping again during a blow's follow-through carries straight on into the next one
        StartCoroutine(ComboBlow(aimPoint, chained: comboStep != 0 && Time.time <= comboExpires));
    }

    private int blowSerial;   // which blow is current, so a finished blow's recovery never overrides the next one

    private IEnumerator ComboBlow(Vector3 aimPoint, bool chained = false)
    {
        int serial = ++blowSerial;
        inBlow = true;
        Rooted = true;
        queued = false;
        if ((!chained && Time.time > comboExpires) || comboStep >= DualBladeSkills.ComboLength)
            comboStep = 0;
        Blow blow = Blows[comboStep];
        Quaternion aim = Quaternion.LookRotation(FlatTo(aimPoint));
        float speed = AttackSpeed * blow.pace;
        // From stance the blow eases in; chained, it takes over from the last one's follow-through
        Play(blow.state, speed, chained ? 0.12f : 0.08f);
        SetTrails(true);
        float seconds = ClipLength(blow.state) / speed;
        float lungeSpeed = blow.lunge / Mathf.Max(0.05f, (blow.lungeTo - blow.lungeFrom) * seconds);
        int landed = 0;
        for (float t = 0f; t < seconds * blow.cancel; t += Time.deltaTime)
        {
            // Square up to the aim while winding up, then commit to the cut
            if (landed == 0)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, aim, 900f * Time.deltaTime);
            float u = t / seconds;
            if (controller != null && u >= blow.lungeFrom && u < blow.lungeTo)
                controller.Move(transform.forward * lungeSpeed * Time.deltaTime);
            if (landed < blow.impacts.Length && u >= blow.impacts[landed])
            {
                LandBlow(blow, landed);
                landed++;
            }
            yield return null;
        }
        while (landed < blow.impacts.Length)
            LandBlow(blow, landed++);
        // The combo runs as far as the skill tree has opened it, then starts over
        comboStep = (comboStep + 1) % DualBladeSkills.ComboLength;
        inBlow = false;
        if (queued)
        {
            StartCoroutine(ComboBlow(queuedAim, chained: true));
            yield break;
        }

        // Nothing queued: the follow-through plays out while the next blow can still be called, then back to stance.
        // Moving cuts the recovery short.
        Rooted = false;
        SetTrails(false);
        float rest = seconds * (1f - blow.cancel);
        comboExpires = Time.time + rest + comboWindow;
        for (float t = 0f; t < rest; t += Time.deltaTime)
        {
            if (serial != blowSerial || casting)
                yield break;
            if (Moving())
                break;
            yield return null;
        }
        if (serial == blowSerial && !inBlow && !casting)
            Play("Locomotion", 1f, Moving() ? 0.15f : 0.25f);
    }

    private static bool Moving()
    {
        Keyboard k = Keyboard.current;
        return k != null && (k.wKey.isPressed || k.aKey.isPressed || k.sKey.isPressed || k.dKey.isPressed);
    }

    // The painted VARCO flipbooks (Resources/Vfx/*sheet, 4x4 frames) that draw each kind of cut
    private void LandBlow(Blow blow, int landing)
    {
        bool lastOfCombo = comboStep == DualBladeSkills.ComboLength - 1 && DualBladeSkills.ComboLength > 1;
        GameAudio.Play(lastOfCombo && landing == 0 ? "dual_finisher" : blow.kind == Cut3.Spin || blow.arc >= 360f ? "dual_spin"
            : (landing + comboStep) % 2 == 0 ? "dual_slash1" : "dual_slash2");
        bool spin = blow.arc >= 360f;
        Color color = Berserk ? Crimson : Color.Lerp(Violet, Crimson, landing * 0.35f);
        Vector3 at = transform.position, fwd = transform.forward;
        Vector3 chest = at + Vector3.up * 0.3f, feet = new Vector3(at.x, at.y - 0.92f, at.z);
        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        float tilt = blow.tilts[Mathf.Min(landing, blow.tilts.Length - 1)];
        float size = reach + blow.reach;
        switch (blow.kind)
        {
            case Cut3.Slash:
            {
                // A crescent sweeping through the swing's plane, bulging toward the enemy; every other cut runs the other way
                Quaternion plane = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 0f, tilt) * Quaternion.Euler(90f, 0f, 0f)
                    * Quaternion.Euler(0f, landing % 2 == 0 ? 0f : 180f, 0f) * Quaternion.Euler(0f, 0f, -90f);
                Cut("slashsheet", chest + fwd * 0.8f, size * 2.6f, color * 1.6f, 0.3f, 0.9f, 1.1f, 0f, plane, sheet: 4);
                break;
            }
            case Cut3.Stab:
                // A thrust: a streak driven straight out ahead at chest height
                Cut("dashsheet", chest + fwd * (size * 0.75f) + Vector3.up * (tilt * 0.01f), size * 2.2f, color * 1.6f, 0.26f, 0.8f, 1.15f, 0f,
                    Quaternion.Euler(90f, yaw - 90f + tilt, 0f), sheet: 4);
                break;
            case Cut3.Spin:
                // The turn: a ring of curved cuts whirling round the player
                Cut("spinsheet", new Vector3(at.x, at.y - 0.4f, at.z), size * 2.7f, color * 1.6f, 0.36f, 0.85f, 1.1f, landing % 2 == 0 ? 220f : -220f,
                    Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), sheet: 4);
                CameraShake.Shake(0.12f, 0.1f);
                break;
            case Cut3.Leap:
                // The landing: a shockwave out across the ground and a whirl of cuts, smoke kicked up
                Cut("roarsheet", feet + Vector3.up * 0.05f, size * 3.4f, Crimson * 1.5f, 0.6f, 0.5f, 1.15f, 0f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), sheet: 4);
                Cut("spinsheet", feet + Vector3.up * 0.4f, size * 2.6f, color * 1.6f, 0.4f, 0.8f, 1.15f, 260f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), sheet: 4);
                Vfx.Sprite("shadowsmoke", feet + Vector3.up * 0.5f, 3.6f, new Color(0.55f, 0.35f, 0.8f, 0.85f), 0.6f, 0.7f, 1.3f, 0f, additive: false).Sheet(4, 4);
                CameraShake.Shake(0.28f, 0.2f);
                break;
        }
        float mastery = spin && DualBladeSkills.Has(DualBladeSkills.Mastery) ? 1.2f : 1f;
        List<Enemy> struck = HitArc(at, fwd, size, blow.arc, blow.damage * mastery);
        CameraShake.Shake(0.06f, struck.Count > 0 ? 0.08f : 0.04f);
        miner.ChainFrom(struck);
    }

    // ───────────── Hitting ─────────────

    // 광폭화's boon on top of the skill boons already in the base damage. The support 광폭화 and this one don't stack
    // (the stronger counts), and all boons together stop at SkillSystem.MaxBurst.
    private float DamageMultiplier
    {
        get
        {
            if (!BerserkBoon) return 1f;
            float own = (Sp('R') == 3 ? 1.85f : berserkDamage) * (Sp('R') == 0 ? 1f + 0.03f * frenzyStacks : 1f);
            if (SkillSystem.SupportBerserk) own = Mathf.Max(1f, own / SkillSystem.SupportBerserkDamage);
            return Mathf.Min(own, Mathf.Max(1f, SkillSystem.MaxBurst / SkillSystem.DamageBuff));
        }
    }

    // 그림자 광폭화's clone landing a blow (its own swings, or a skill it copies)
    public List<Enemy> AllyHit(Vector3 from, Vector3 forward, float radius, float arc, float multiplier) =>
        HitArc(from, forward, radius, arc, multiplier);

    // Every enemy within the radius and the arc in front (360 = all round), at this multiple of a normal hit
    private List<Enemy> HitArc(Vector3 from, Vector3 forward, float radius, float arc, float multiplier, HashSet<Enemy> skip = null)
    {
        var struck = new List<Enemy>();
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead || (skip != null && skip.Contains(enemy)))
                continue;
            Vector3 to = Nearest(enemy, from) - from;
            to.y = 0f;
            if (to.magnitude > radius)
                continue;
            if (arc < 360f && to.sqrMagnitude > 0.25f && Vector3.Angle(forward, to) > arc / 2f)
                continue;
            Hit(enemy, multiplier);
            struck.Add(enemy);
            skip?.Add(enemy);
        }
        return struck;
    }

    private static Vector3 Nearest(Enemy enemy, Vector3 from)
    {
        Collider c = enemy.GetComponent<Collider>();
        return c != null ? c.ClosestPoint(from) : enemy.transform.position;
    }

    private readonly Dictionary<Enemy, float> lastSpark = new Dictionary<Enemy, float>();

    private void Hit(Enemy enemy, float multiplier)
    {
        int damage = PlayerStats.RoundHit(miner.RollWeaponDamage(out bool critical) * multiplier * DamageMultiplier);
        miner.StrikeEnemy(enemy, damage, critical);
        // The cut bursting on the enemy (at most every few frames per enemy, so the flurry doesn't pile them up)
        if (!lastSpark.TryGetValue(enemy, out float last) || Time.time - last > 0.08f)
        {
            lastSpark[enemy] = Time.time;
            Vector3 body = enemy.transform.position + Vector3.up * (0.9f * enemy.transform.localScale.y) - transform.forward * 0.3f;
            Vfx.Sprite("hitsheet", body, critical ? 2.4f : 1.8f, (Berserk || critical ? Crimson : Color.Lerp(Violet, Crimson, 0.5f)) * 1.6f,
                0.3f, 0.85f, 1.15f, Random.Range(-60f, 60f)).Sheet(4, 4);
        }
        if (enemy.IsDead && Berserk && DualBladeSkills.Has(DualBladeSkills.Bloodlust) && stats != null)
            stats.Heal(stats.MaxHealth * 0.04f);
        if (Berserk && Sp('R') == 0 && frenzyStacks < 20) frenzyStacks++;
        if (Berserk && Sp('R') == 1 && enemy.IsDead && berserkExtended < 5f)
        {
            float add = Mathf.Min(1f, 5f - berserkExtended);
            berserkExtended += add;
            berserkUntil += add;
            Announce?.Invoke(transform.position + Vector3.up * 2.2f, $"+{add:0.#}초", Crimson);
        }
    }

    // The dash and the dodge pass through enemies instead of stopping against them
    private readonly List<Collider> passedThrough = new List<Collider>();

    private void PassThroughEnemies(bool on)
    {
        if (on)
            foreach (Enemy enemy in Enemy.Alive)
                if (enemy != null && enemy.TryGetComponent(out Collider body))
                {
                    Physics.IgnoreCollision(controller, body, true);
                    passedThrough.Add(body);
                }
        if (!on)
        {
            foreach (Collider body in passedThrough)
                if (body != null)
                    Physics.IgnoreCollision(controller, body, false);
            passedThrough.Clear();
        }
    }

    private Vector3 FlatTo(Vector3 point)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        return to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
    }

    // ───────────── 난무 ─────────────

    private IEnumerator Flurry()
    {
        GameAudio.Play("dual_flurry");
        int sp = Sp('Q');
        // 질풍난무: the player keeps walking while the blades whirl (weaker, a little slower on foot)
        Rooted = sp != 2;
        movingFlurry = sp == 2;
        float time = flurryTime + (DualBladeSkills.Has(DualBladeSkills.Tempest) ? 0.8f : 0f);
        float range = flurryRange + (DualBladeSkills.Has(DualBladeSkills.Tempest) ? 1f : 0f);
        float arc = 130f, dmg = flurryDamage;
        if (sp == 0) { arc = 360f; range *= 0.9f; }                 // 회전난무: all round, no aim
        if (sp == 2) { arc = 170f; dmg *= 0.65f; }                  // 질풍난무: on the move, weaker
        if (sp == 3) { arc = 45f; range += 1f; dmg *= 1.3f; }       // 집중 난무: one narrow line
        Play("Flurry", AttackSpeed * 3f, 0.06f, FlurryStart);
        SetTrails(true);
        Announce?.Invoke(transform.position + Vector3.up * 2f, sp >= 0 ? SpecialNames[0][sp] + "!" : "난무!", Violet);
        if (ally != null) ally.Mimic(ShadowAlly.Move.Flurry);

        // 그림자 분신: a clone stands back to back with the player, cutting the other way
        ShadowClone back = null;
        if (sp == 1)
        {
            // (it turns with the player and swings as they do, facing the other way just behind them)
            back = ShadowClone.Spawn(this, profile, time + 0.2f, false, Violet);
            if (back != null) back.Follow(180f, new Vector3(0f, 0f, -1.4f));
        }
        float nextTick = 0f, nextFx = 0f;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            // (질풍난무 cuts the way the player walks; the others turn to the cursor)
            if (sp != 0 && sp != 2) Face(Cursor(), 300f);
            if (t >= nextFx)
            {
                nextFx = t + 0.07f;
                if (sp == 0)
                    Cut("spinsheet", new Vector3(transform.position.x, transform.position.y - 0.4f, transform.position.z), range * 2.4f,
                        (Random.value < 0.5f ? Violet : Crimson) * 1.6f, 0.25f, 0.9f, 1.1f, Random.value < 0.5f ? 300f : -300f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), sheet: 4);
                else
                {
                    float spread = sp == 3 ? 0.12f : 0.45f;
                    Vector3 at = transform.position + transform.forward * Random.Range(0.8f, range * 0.8f)
                        + transform.right * Random.Range(-range * spread, range * spread) + Vector3.up * Random.Range(-0.3f, 0.6f);
                    Cut("flurrysheet", at, Random.Range(1.8f, 2.8f) * (sp == 3 ? 0.8f : 1f), (Random.value < 0.5f ? Violet : Crimson) * 1.8f, 0.35f, 0.9f, 1.1f, Random.Range(-90f, 90f), sheet: 4);
                    Quaternion plane = Quaternion.LookRotation(transform.forward) * Quaternion.Euler(0f, 0f, Random.Range(-70f, 70f))
                        * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, Random.value < 0.5f ? 0f : 180f, 0f) * Quaternion.Euler(0f, 0f, -90f);
                    Cut("slashsheet", transform.position + Vector3.up * Random.Range(0f, 0.6f) + transform.forward * Random.Range(1f, range * 0.7f),
                        range * Random.Range(1.1f, 1.6f) * (sp == 3 ? 0.7f : 1f), (Random.value < 0.5f ? Violet : Crimson) * 1.6f, 0.24f, 0.9f, 1.1f, 0f, plane, sheet: 4);
                    if (sp == 1)
                        Cut("flurrysheet", transform.position - transform.forward * Random.Range(1.2f, range * 0.8f) + transform.right * Random.Range(-1f, 1f),
                            Random.Range(1.6f, 2.4f), Violet * 1.5f, 0.3f, 0.9f, 1.1f, Random.Range(-90f, 90f), sheet: 4);
                }
            }
            if (t >= nextTick)
            {
                nextTick = t + flurryTick / Mathf.Max(0.5f, AttackSpeed);
                List<Enemy> struck = HitArc(transform.position, transform.forward, range, arc, dmg);
                if (sp == 1)
                    struck.AddRange(HitArc(transform.position - transform.forward * 0.9f, -transform.forward, range, 130f, dmg * 0.55f));
                if (struck.Count > 0)
                    CameraShake.Shake(0.05f, 0.06f);
            }
            yield return null;
        }
        // 집중 난무: the last blow, a heavy finishing cut on the one in front
        if (sp == 3)
        {
            Play("Blow1_Slash", AttackSpeed * 2f, 0.05f, 0.3f);
            yield return new WaitForSeconds(0.12f);
            Quaternion plane = Quaternion.LookRotation(transform.forward) * Quaternion.Euler(0f, 0f, -25f) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, -90f);
            Cut("slashsheet", transform.position + transform.forward * 1.4f + Vector3.up * 0.3f, range * 2.4f, Crimson * 2f, 0.35f, 0.9f, 1.2f, 0f, plane, sheet: 4);
            HitArc(transform.position, transform.forward, range + 0.5f, 50f, 3.2f);
            CameraShake.Shake(0.2f, 0.2f);
        }
        movingFlurry = false;
        Play("Locomotion", 1f, 0.12f);
    }
    // ───────────── 광폭화 ─────────────

    private IEnumerator BerserkRoar()
    {
        GameAudio.Play("dual_berserk");
        Rooted = true;
        Play("Berserk", 1.4f, 0.08f, BerserkStart);
        yield return new WaitForSeconds(0.35f);
        berserkUntil = Time.time + berserkTime + (DualBladeSkills.Has(DualBladeSkills.Bloodlust) ? 3f : 0f);
        berserkExtended = 0f;
        frenzyStacks = 0;
        int sp = Sp('R');
        Announce?.Invoke(transform.position + Vector3.up * 2.2f, sp >= 0 ? SpecialNames[2][sp] + "!" : "광폭화!", Crimson);
        Vector3 feet = new Vector3(transform.position.x, transform.position.y - 0.95f, transform.position.z);
        // 그림자 광폭화: no boon — a shadow of the player steps out of the smoke beside them and fights on its own
        if (sp == 2)
        {
            if (ally != null) Destroy(ally.gameObject);
            Vector3 spot = transform.position + transform.right * 1.6f;
            ally = ShadowAlly.Summon(this, profile, spot, berserkUntil - Time.time);
            CameraShake.Shake(0.2f, 0.15f);
            yield return new WaitForSeconds(0.3f);
            Play("Locomotion", 1f, 0.15f);
            yield break;
        }
        // A shockwave of blood-red fire rolls out across the ground, light flares, the ground trembles
        Cut("roarsheet", feet + Vector3.up * 0.05f, 8f, Crimson * 1.7f, 0.75f, 0.45f, 1.2f, 0f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), sheet: 4);
        HeimdallFx.Impact(transform.position + Vector3.up * 0.2f, 3f, Crimson);
        CameraShake.Shake(0.3f, 0.18f);
        // While it lasts: the shockwave keeps pulsing at the feet, painted flames rise behind the body, a red light
        berserkRing = Vfx.Decal(feet + Vector3.up * 0.04f, 3.2f, Crimson * 1.2f, "roarsheet", 0f).Sheet(4, 4);
        berserkAura = Ambient.Flame(transform, Vector3.up * -0.6f, 1.1f, false, Crimson);
        berserkFlamesInk = Vfx.Sprite("auraflame_ink", transform.position, 3.3f, new Color(0.3f, 0.02f, 0.02f, 0.85f), 0f, 1f, 1f, 0f, additive: false).Sheet(4, 4).OpenUp(0.3f);
        berserkFlames = Vfx.Sprite("auraflame", transform.position, 3.3f, Crimson * 1.8f, 0f, 1f, 1f, 0f).Sheet(4, 4).OpenUp(0.3f);
        berserkFlames.GetComponent<Renderer>().sortingOrder = 1;
        var lamp = new GameObject("BerserkLight").AddComponent<Light>();
        lamp.transform.SetParent(berserkAura.transform, false);
        lamp.transform.localPosition = Vector3.up * 1.2f;
        lamp.type = LightType.Point;
        lamp.color = Crimson;
        lamp.range = 5f;
        lamp.intensity = 2f;
        lamp.shadows = LightShadows.None;
        yield return new WaitForSeconds(0.35f);
        Play("Locomotion", 1f, 0.15f);
    }

    private void UpdateBerserk()
    {
        if (berserkRing == null)
            return;
        if (!Berserk)
        {
            // 폭주: spent, a short spell of exhaustion
            if (Sp('R') == 3)
            {
                // spent: five seconds of exhaustion — slow on foot and slow with the blades
                exhaustedUntil = Time.time + ExhaustSeconds;
                Announce?.Invoke(transform.position + Vector3.up * 2.2f, "탈진… (공격·이동 속도 감소)", new Color(0.7f, 0.7f, 0.75f));
                Vfx.Sprite("shadowsmoke", transform.position, 3f, new Color(0.4f, 0.4f, 0.45f, 0.85f), 0.8f, 0.7f, 1.3f, 0f, additive: false).Sheet(4, 4);
            }
            frenzyStacks = 0;
            EndBerserk();
            return;
        }
        float left = berserkUntil - Time.time;
        float fade = left < 1f ? left : 1f;
        berserkRing.transform.position = new Vector3(transform.position.x, transform.position.y - 0.91f, transform.position.z);
        berserkRing.Set(Crimson * 1.6f, 3.2f, fade);
        if (berserkFlames != null)
        {
            // Just behind the body as the camera sees it, so the flames frame the player instead of covering them
            Vector3 back = Camera.main != null ? Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized : Vector3.zero;
            berserkFlames.transform.position = transform.position + Vector3.up * 0.35f + back * 0.6f;
            float flicker = 3.3f * (1f + Mathf.Sin(Time.time * 7f) * 0.04f);
            berserkFlames.Set(Crimson * 1.8f, flicker, fade);
            if (berserkFlamesInk != null)
            {
                berserkFlamesInk.transform.position = berserkFlames.transform.position + back * 0.05f;
                berserkFlamesInk.Set(new Color(0.3f, 0.02f, 0.02f, 0.85f), flicker, fade * 0.9f);
            }
        }
    }

    private void EndBerserk()
    {
        berserkUntil = 0f;
        if (berserkRing != null)
            Destroy(berserkRing.gameObject);
        if (berserkAura != null)
            Destroy(berserkAura);
        if (berserkFlames != null)
            Destroy(berserkFlames.gameObject);
        if (berserkFlamesInk != null)
            Destroy(berserkFlamesInk.gameObject);
        berserkRing = null;
        berserkAura = null;
        berserkFlames = berserkFlamesInk = null;
    }

    // ───────────── 처형 ─────────────

    private GameObject executePreview;
    private VfxPiece executeGlow;   // the charged orb, burning until the wave goes
    private readonly List<(Enemy enemy, VfxPiece mark)> executeMarks = new List<(Enemy, VfxPiece)>();

    private float ThresholdFor(Enemy enemy)
    {
        bool headsman = DualBladeSkills.Has(DualBladeSkills.Headsman);
        // (a guardian's last phase is never skipped: at most its last 8%, 12% with 처형인)
        return enemy.IsBoss ? (headsman ? 0.12f : executeBossThreshold) : (headsman ? 0.4f : executeThreshold);
    }

    private bool Executable(Enemy enemy) => enemy != null && !enemy.IsDead && enemy.Health <= enemy.MaxHealth * ThresholdFor(enemy);

    private IEnumerator Execute()
    {
        GameAudio.Play("dual_exec_charge");
        int sp = Sp('M');
        float charge = executeCharge * (sp == 1 ? 1.8f : 1f);   // 월광참: a longer charge
        Rooted = true;
        Play("ExecuteCharge", 1.2f, 0.06f, ChargeStart);
        Mouse mouse = Mouse.current;
        float held = 0f;
        bool charged = false;
        executePreview = PreviewStrip();
        Vfx.Gather(transform.position + Vector3.up * 0.4f, 1.6f, Crimson, 0.8f, 40f);
        VfxPiece gathering = Vfx.Sprite("chargesheet", transform.position + Vector3.up * 0.3f + transform.forward * 0.4f, 2.4f, Crimson * 1.5f,
            charge + 0.15f, 0.9f, 1.05f, 0f).Sheet(4, 4);
        gathering.transform.SetParent(transform, true);
        while (SkillSystem.RightHeld(mouse) && GameManager.IsRunActive)
        {
            held += Time.deltaTime;
            Face(Cursor(), 540f);
            if (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsName("ExecuteCharge") && animator.GetCurrentAnimatorStateInfo(0).normalizedTime > ChargeHold)
                animator.SetFloat(AttackSpeedHash, 0f);
            if (!charged && held >= charge)
            {
                charged = true;
                Vfx.Flash(transform.position + Vector3.up * 0.6f, 2f, Crimson, 0.25f);
                Vfx.Sprite("hitsheet", transform.position + Vector3.up * 0.3f + transform.forward * 0.4f, 2.6f, Crimson * 1.6f, 0.3f, 0.9f, 1.2f, 0f).Sheet(4, 4);
                executeGlow = Vfx.Sprite("glow", transform.position + Vector3.up * 0.3f + transform.forward * 0.4f, 1.6f, Crimson * 1.4f, 0f, 1f, 1f, 0f);
                executeGlow.transform.SetParent(transform, true);
                Announce?.Invoke(transform.position + Vector3.up * 2f, sp >= 0 ? SpecialNames[3][sp] + " 준비" : "검기 준비", Crimson);
            }
            if (executeGlow != null)
                executeGlow.Set(Crimson * 1.4f, 1.6f * (1f + Mathf.Sin(Time.time * 14f) * 0.12f));
            UpdatePreview(charged);
            yield return null;
        }
        if (executePreview != null)
            Destroy(executePreview);
        if (gathering != null)
            Destroy(gathering.gameObject);
        ClearExecuteMarks();
        if (!charged)
        {
            Play("Locomotion", 1f, 0.12f);
            yield break;
        }
        StartCooldown(Skill.Execute);
        Play("ExecuteRelease", 1.8f, 0.05f, ReleaseStart);
        GameAudio.Play("dual_exec_wave");
        SetTrails(true);
        yield return new WaitForSeconds(0.25f);
        Vector3 from = transform.position + Vector3.up * 0.1f;
        if (ally != null) ally.Mimic(ShadowAlly.Move.Wave);
        switch (sp)
        {
            case 1:   // 월광참: one huge crescent moon, its own VARCO art turned the way it flies
                ExecuteWave.Fire(this, from, transform.forward, executeRange * 1.3f, executeWidth * 2.2f, 1.6f, moon: true);
                CameraShake.Shake(0.3f, 0.25f);
                break;
            case 2:   // 추적 검기: bends toward the nearest enemy
                ExecuteWave.Fire(this, from, transform.forward, executeRange * 1.2f, executeWidth, 0.75f, homing: true);
                CameraShake.Shake(0.2f, 0.18f);
                break;
            case 3:   // 폭발 검기: bursts where it strikes
                ExecuteWave.Fire(this, from, transform.forward, executeRange, executeWidth, 1f, explode: true);
                CameraShake.Shake(0.2f, 0.18f);
                break;
            default:
                ExecuteWave.Fire(this, from, transform.forward, executeRange, executeWidth);
                CameraShake.Shake(0.2f, 0.18f);
                break;
        }
        yield return new WaitForSeconds(0.3f);
        Play("Locomotion", 1f, 0.15f);
    }
    // 속사 검기: no charge — a small, quick wave straight away (shorter cooldown, weaker)
    private IEnumerator QuickWave()
    {
        GameAudio.Play("dual_exec_wave");
        Rooted = true;
        Face(Cursor());
        Play("ExecuteRelease", 2.4f, 0.04f, ReleaseStart);
        SetTrails(true);
        Announce?.Invoke(transform.position + Vector3.up * 2f, SpecialNames[3][0], Crimson);
        if (ally != null) ally.Mimic(ShadowAlly.Move.Wave);
        yield return new WaitForSeconds(0.12f);
        ExecuteWave.Fire(this, transform.position + Vector3.up * 0.1f, transform.forward, executeRange * 0.85f, executeWidth * 0.7f, 0.55f);
        CameraShake.Shake(0.08f, 0.08f);
        yield return new WaitForSeconds(0.18f);
        Play("Locomotion", 1f, 0.12f);
    }

    private GameObject PreviewStrip()
    {
        var go = new GameObject("ExecutePreview");
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = Vfx.Material("beam", additive: true);
        line.positionCount = 2;
        line.widthMultiplier = executeWidth;
        line.alignment = LineAlignment.TransformZ;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    private void UpdatePreview(bool charged)
    {
        if (executePreview == null)
            return;
        var line = executePreview.GetComponent<LineRenderer>();
        Vector3 from = new Vector3(transform.position.x, transform.position.y - 0.9f, transform.position.z);
        line.SetPosition(0, from);
        line.SetPosition(1, from + transform.forward * executeRange);
        float glow = charged ? 0.35f + 0.1f * Mathf.Sin(Time.time * 12f) : 0.12f;
        line.startColor = line.endColor = new Color(Crimson.r, Crimson.g, Crimson.b, glow);
    }

    // The mark of death over each enemy in the path that the wave would finish
    private void UpdateExecuteMarks()
    {
        foreach (Enemy enemy in Enemy.Alive)
        {
            if (!Executable(enemy) || !InWavePath(enemy) || executeMarks.Exists(m => m.enemy == enemy))
                continue;
            // A burning skull sigil with its ring of runes turning (a looping VARCO flipbook)
            VfxPiece mark = Vfx.Sprite("marksheet", enemy.transform.position, 1.8f, Crimson * 1.6f, 0f, 1f, 1f, 0f).Sheet(4, 4).OpenUp(0.25f);
            executeMarks.Add((enemy, mark));
        }
        for (int i = executeMarks.Count - 1; i >= 0; i--)
        {
            var (enemy, mark) = executeMarks[i];
            if (!Executable(enemy) || !InWavePath(enemy))
            {
                if (mark != null)
                    Destroy(mark.gameObject);
                executeMarks.RemoveAt(i);
                continue;
            }
            float h = 2.4f * enemy.transform.localScale.y;
            mark.transform.position = enemy.transform.position + Vector3.up * h;
            mark.Set(Crimson * 1.6f, 1.8f * (1f + Mathf.Sin(Time.time * 10f) * 0.06f));
        }
    }

    private void ClearExecuteMarks()
    {
        foreach (var (_, mark) in executeMarks)
            if (mark != null)
                Destroy(mark.gameObject);
        executeMarks.Clear();
        if (executeGlow != null)
            Destroy(executeGlow.gameObject);
        executeGlow = null;
    }

    private bool InWavePath(Enemy enemy)
    {
        Vector3 rel = enemy.transform.position - transform.position;
        rel.y = 0f;
        float along = Vector3.Dot(rel, transform.forward);
        return along > -0.5f && along < executeRange && Mathf.Abs(Vector3.Dot(rel, transform.right)) < executeWidth / 2f + 0.6f;
    }

    // The wave reaches an enemy: a heavy cut; 폭발 검기 bursts there and the wave is spent
    public bool WaveHit(Enemy enemy, ExecuteWave wave)
    {
        if (enemy == null || enemy.IsDead)
            return false;
        Vector3 at = enemy.transform.position + Vector3.up * 1.2f * enemy.transform.localScale.y;
        Hit(enemy, executeDamage * (wave != null ? wave.Scale : 1f));
        if (enemy.IsDead) GameAudio.Play("dual_exec_kill", enemy.transform.position);   // (처형)
        Vfx.Burst(at, Crimson, 14, 7f, 0.16f);
        Vfx.Sprite("hitsheet", at, 2.6f, Crimson * 1.6f, 0.3f, 0.85f, 1.15f, Random.Range(-40f, 40f)).Sheet(4, 4);
        if (wave != null && wave.Explode)
        {
            Vector3 ground = new Vector3(enemy.transform.position.x, enemy.transform.position.y + 0.05f, enemy.transform.position.z);
            Cut("roarsheet", ground, 7f, Crimson * 1.8f, 0.5f, 0.5f, 1.2f, 0f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), sheet: 4);
            HeimdallFx.Impact(at, 3.2f, Crimson);
            var skip = new HashSet<Enemy> { enemy };
            HitArc(enemy.transform.position, transform.forward, 3.2f, 360f, executeDamage * 0.8f, skip);
            CameraShake.Shake(0.22f, 0.2f);
            return true;   // (the wave ends here)
        }
        return false;
    }
    // ───────────── 돌진 ─────────────

    // 귀신베기: a long dash at the cursor that cuts the first enemy in the way and stops there (no piercing)
    private IEnumerator DashStrike(Vector3 target)
    {
        GameAudio.Play("dual_dash");
        int sp = Sp('E');
        // 삼연귀참: one of three charges spent; it comes back on its own 5 seconds from now
        if (sp == 0)
        {
            dashCharges = Mathf.Max(0, dashCharges - 1);
            dashRecharge.Enqueue(Time.time + CooldownOf(Skill.Dash));
            if (dashCharges <= 0) readyAt[Skill.Dash] = dashRecharge.Peek();
        }
        if (ally != null) ally.Mimic(ShadowAlly.Move.Dash);
        Rooted = true;
        Vector3 dir = FlatTo(target);
        transform.rotation = Quaternion.LookRotation(dir);
        float distance = dashDistance * (DualBladeSkills.Has(DualBladeSkills.Gale) ? 1.4f : 1f) * (sp == 0 ? 0.55f : sp == 1 ? 0.6f : 1f);
        float damage = dashDamage * (sp == 0 ? 0.7f : 1f);
        Play("Dash", 2.1f, 0.05f, DashStart);
        SetTrails(true);
        if (sp >= 0) Announce?.Invoke(transform.position + Vector3.up * 2f, SpecialNames[1][sp], Violet);
        Vector3 start = transform.position;
        var streak = DashStreak.Spawn(start - Vector3.up * 0.3f);
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        Cut("dashsheet", start + dir * (distance * 0.5f) - Vector3.up * 0.45f, distance * 1.25f, Violet * 1.6f, dashTime + 0.35f, 0.9f, 1.05f, 0f,
            Quaternion.Euler(90f, yaw - 90f, 0f), sheet: 4);
        Vfx.Sprite("shadowsmoke", start - Vector3.up * 0.4f, 2f, new Color(0.5f, 0.3f, 0.7f, 0.8f), 0.5f, 0.8f, 1.2f, 0f, additive: false).Sheet(4, 4);
        Enemy struck = null;
        float nextGhost = 0f;
        float time = dashTime * Mathf.Max(0.5f, distance / dashDistance);
        for (float t = 0f; t < time && struck == null; t += Time.deltaTime)
        {
            controller.Move(dir * (distance / time) * Time.deltaTime);
            streak.Reach(transform.position - Vector3.up * 0.3f);
            if (t >= nextGhost)
            {
                nextGhost = t + 0.05f;
                ShadowClone.Spawn(this, profile, 0.3f, false, Violet);
            }
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy.IsDead) continue;
                Vector3 off = enemy.transform.position - transform.position;
                off.y = 0f;
                if (off.magnitude > 1.5f || Vector3.Dot(off, dir) < -0.3f) continue;
                struck = enemy;   // the first in the way: cut, and the dash stops here
                Hit(enemy, damage);
                Quaternion across = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, Random.Range(-30f, 30f)) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, -90f);
                Cut("slashsheet", enemy.transform.position + Vector3.up * 0.9f, 2.8f, Crimson * 1.3f, 0.26f, 0.85f, 1.15f, 0f, across, sheet: 4);
                break;
            }
            yield return null;
        }
        Vector3 end = transform.position;
        Quaternion behind = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, 10f) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(0f, 0f, -90f);
        Cut("slashsheet", end + Vector3.up * 0.2f + dir * 0.6f, 4f, Crimson * 1.5f, 0.3f, 0.9f, 1.1f, 0f, behind, sheet: 4);
        switch (sp)
        {
            case 2:   // 잔영참: the path is cut again by the afterimage a moment later
                StartCoroutine(Afterimage(start, end));
                break;
            case 3:   // 회전섬: a whirl where the dash ends
                Cut("spinsheet", new Vector3(end.x, end.y - 0.4f, end.z), 8.5f, Violet * 1.7f, 0.4f, 0.8f, 1.15f, 280f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), sheet: 4);
                HitArc(end, dir, 3.3f, 360f, 1.4f);
                CameraShake.Shake(0.15f, 0.14f);
                break;
        }
        if (struck != null)
        {
            if (sp != 0 && sp != 1)
            {
                float refund = DualBladeSkills.Has(DualBladeSkills.Gale) ? 0.65f : dashRefund;
                readyAt[Skill.Dash] = Mathf.Min(readyAt.TryGetValue(Skill.Dash, out float at) ? at : 0f, Time.time + dashCooldown * (1f - refund));
            }
            CameraShake.Shake(0.12f, 0.12f);
            miner.ChainFrom(new List<Enemy> { struck });
        }
        yield return new WaitForSeconds(0.12f);
        Play("Locomotion", 1f, 0.12f);
    }

    // ── 일섬: hold E to gather; let go to draw. Fully gathered, time slows and the world goes grey for the instant of
    // the draw: one blinding line through every enemy ahead. Let go early: a quick, weaker cut. ──
    private const float IssenFull = 1.1f, IssenRange = 9f, IssenSlow = 0.3f;
    private IaidoPose iaido;
    private AnimatorUpdateMode iaidoUpdateMode;
    private bool iaidoOn;

    private void IaidoBegin()
    {
        iaido = IaidoPose.On(animator);
        if (iaido != null) iaido.Target = 1f;
        if (animator != null && !iaidoOn)
        {
            iaidoUpdateMode = animator.updateMode;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }
        iaidoOn = true;
    }

    private void IaidoEnd()
    {
        if (iaido != null) iaido.Target = 0f;
        if (animator != null && iaidoOn) animator.updateMode = iaidoUpdateMode;
        iaidoOn = false;
    }
    private IEnumerator IssenCharge()
    {
        GameAudio.Play("dual_iaido_charge");
        Rooted = true;
        Play("ExecuteCharge", 1.2f, 0.06f, ChargeStart);
        // 발도술 자세 from the first instant, and the world slowed and grey for as long as the draw is held back
        // (the fighter's own body keeps real time)
        IaidoBegin();
        Keyboard kb = Keyboard.current;
        float held = 0f;
        bool full = false;
        string chargeArt = Vfx.Pick("fx_issen_charge", "");
        Vector3 gatherAt = transform.position + Vector3.up * 0.3f + transform.forward * 0.3f;
        VfxPiece gathering = chargeArt != ""
            ? Cut(chargeArt, gatherAt, 2.6f, Violet * 1.6f, IssenFull + 0.1f, 0.9f, 1.05f, 0f, sheet: 4)
            : Vfx.Sprite("chargesheet", gatherAt, 2.4f, Violet * 1.5f, IssenFull + 0.1f, 0.9f, 1.05f, 0f).Sheet(4, 4);
        gathering.transform.SetParent(transform, true);
        Vfx.Gather(transform.position + Vector3.up * 0.4f, 1.8f, Violet, IssenFull, 40f);
        GameObject line = PreviewStrip();
        while (SkillSystem.KeyHeld(kb, 'E') && GameManager.IsRunActive && held < 4f)
        {
            CinematicFx.Hold(IssenSlow);
            if (Time.timeScale > 0f) held += Time.unscaledDeltaTime;
            Face(Cursor(), 540f / Mathf.Max(0.05f, Time.timeScale));
            if (animator != null && animator.GetCurrentAnimatorStateInfo(0).IsName("ExecuteCharge") && animator.GetCurrentAnimatorStateInfo(0).normalizedTime > ChargeHold)
                animator.SetFloat(AttackSpeedHash, 0f);
            if (!full && held >= IssenFull)
            {
                full = true;
                Vfx.Flash(transform.position + Vector3.up * 0.6f, 2.4f, Color.white, 0.25f);
                Vfx.Sprite("hitsheet", transform.position + Vector3.up * 0.4f, 2.8f, Violet * 1.8f, 0.3f, 0.9f, 1.2f, 0f).Sheet(4, 4);
                Announce?.Invoke(transform.position + Vector3.up * 2f, "일섬 준비", Color.white);
            }
            if (line != null)
            {
                var lr = line.GetComponent<LineRenderer>();
                Vector3 f = new Vector3(transform.position.x, transform.position.y - 0.9f, transform.position.z);
                lr.SetPosition(0, f);
                lr.SetPosition(1, f + transform.forward * IssenRange);
                lr.widthMultiplier = full ? 1.2f : 0.5f;
                float a = full ? 0.4f + 0.15f * Mathf.Sin(Time.time * 14f) : 0.1f + 0.2f * held / IssenFull;
                lr.startColor = lr.endColor = new Color(0.85f, 0.75f, 1f, a);
            }
            yield return null;
        }
        if (line != null) Destroy(line);
        if (gathering != null) Destroy(gathering.gameObject);
        IaidoEnd();
        // drawn early: time comes straight back (fully drawn, the draw's own grey and slow carry on from here)
        if (held < IssenFull) CinematicFx.Release();
        StartCooldown(Skill.Dash);
        if (ally != null) ally.Mimic(ShadowAlly.Move.Dash);
        float k = Mathf.Clamp01(held / IssenFull);
        Vector3 dir = transform.forward, start = transform.position;
        if (!full)
        {
            // drawn early: a short quick cut ahead, its strength growing with the charge
            Play("Blow1_Slash", AttackSpeed * 2.4f, 0.04f, 0.3f);
            yield return new WaitForSeconds(0.1f);
            Quaternion plane = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, -12f) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, -90f);
            Cut("slashsheet", start + Vector3.up * 0.3f + dir * 1.3f, 4f, Violet * 1.8f, 0.3f, 0.85f, 1.15f, 0f, plane, sheet: 4);
            HitArc(start, dir, 3f, 90f, 1.2f + 1.3f * k);
            CameraShake.Shake(0.1f, 0.1f);
            yield return new WaitForSeconds(0.15f);
            Play("Locomotion", 1f, 0.12f);
            yield break;
        }
        // Fully drawn: the world slows and greys — the draw — then everything ahead is cut at once
        CinematicFx.Grey(0.9f);
        CinematicFx.SlowMotion(0.18f, 0.55f);
        Play("Dash", 2.6f, 0.03f, DashStart);
        GameAudio.Play("dual_iaido_slash");
        PassThroughEnemies(true);
        Vector3 to = start + dir * IssenRange;
        float dashT = 0.12f;
        for (float t = 0f; t < dashT; t += Time.unscaledDeltaTime)
        {
            controller.Move(dir * (IssenRange / dashT) * Time.unscaledDeltaTime);
            if (Mathf.FloorToInt(t / 0.03f) != Mathf.FloorToInt((t - Time.unscaledDeltaTime) / 0.03f))
                ShadowClone.Spawn(this, profile, 0.45f, false, Color.white);
            yield return null;
        }
        PassThroughEnemies(false);
        Vector3 end = transform.position;
        Vector3 mid = (start + end) * 0.5f;
        float len = Mathf.Max(2f, Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(end.x, 0f, end.z)));
        // the line itself: the VARCO 일섬 flash laid along the path (falls back to the dash streak)
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        string flash = Vfx.Pick("fx_issen", "dashsheet");
        Cut(flash, mid + Vector3.up * 0.2f, len * 1.5f, Color.white * 2.2f, 0.7f, 0.9f, 1.1f, 0f, Quaternion.Euler(90f, yaw - 90f, 0f), sheet: 4,
            inkColor: new Color(0.05f, 0.02f, 0.1f, 0.9f));
        yield return new WaitForSecondsRealtime(0.18f);
        // the cut lands on everything along the line, a beat after the blade has passed
        foreach (Enemy e in Enemy.Alive.ToArray())
        {
            if (e == null || e.IsDead) continue;
            Vector3 rel = e.transform.position - start;
            rel.y = 0f;
            float along = Vector3.Dot(rel, dir);
            if (along < -0.5f || along > len + 1.5f || (rel - dir * along).magnitude > 2f) continue;
            Hit(e, 5f);
            Vector3 at = e.transform.position + Vector3.up * 1f;
            Quaternion across = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, Random.Range(-20f, 20f)) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, -90f);
            Cut("slashsheet", at, 3.6f, Color.white * 2f, 0.35f, 0.85f, 1.2f, 0f, across, sheet: 4, inkColor: new Color(0.1f, 0.02f, 0.12f, 0.9f));
            Vfx.Burst(at, Violet, 16, 8f, 0.2f);
        }
        HeimdallFx.Impact(end, 3f, Violet);
        CameraShake.Shake(0.35f, 0.3f);
        Announce?.Invoke(end + Vector3.up * 2.4f, "일섬!", Color.white);
        yield return new WaitForSeconds(0.2f);
        Play("Locomotion", 1f, 0.12f);
    }

    // 잔영참: afterimages along the dash's path, then one cut down the whole of it
    private IEnumerator Afterimage(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        d.y = 0f;
        float len = d.magnitude;
        if (len < 0.5f) yield break;
        Vector3 dir = d / len;
        yield return new WaitForSeconds(0.55f);
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        Cut("dashsheet", from + dir * ((len + 1.5f) * 0.5f) - Vector3.up * 0.4f, (len + 1.5f) * 1.3f, Violet * 2f, 0.45f, 0.9f, 1.1f, 0f, Quaternion.Euler(90f, yaw - 90f, 0f), sheet: 4);
        for (float s = 0f; s <= len + 1.5f; s += 1.6f)
            Cut("slashsheet", from + dir * s + Vector3.up * 0.4f, 2.6f, Violet * 1.6f, 0.3f, 0.9f, 1.1f, 0f,
                Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, Random.Range(-40f, 40f)) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, -90f), sheet: 4);
        foreach (Enemy e in Enemy.Alive.ToArray())
        {
            if (e == null || e.IsDead) continue;
            Vector3 rel = e.transform.position - from;
            rel.y = 0f;
            float along = Vector3.Dot(rel, dir);
            if (along < -0.5f || along > len + 2.2f) continue;   // (the dash stops just short of the enemy it cut: that one too)
            if ((rel - dir * along).magnitude > 1.8f) continue;
            Hit(e, 1.1f);
        }
        CameraShake.Shake(0.1f, 0.1f);
    }
    // ───────────── 회피 ─────────────

    private IEnumerator Dodge(Vector3 dir)
    {
        GameAudio.Play("dual_dodge");
        Rooted = true;
        transform.rotation = Quaternion.LookRotation(dir);
        // The shadow clone stays where the player stood and strikes there a moment later
        ShadowClone.Spawn(this, profile, cloneStrikeDelay + 0.6f, true, Violet);
        Vfx.Sprite("shadowsmoke", transform.position - Vector3.up * 0.3f, 2.4f, new Color(0.55f, 0.35f, 0.8f, 0.85f), 0.55f, 0.7f, 1.3f, 0f, additive: false).Sheet(4, 4);
        invulnerableUntil = Time.time + dodgeTime + 0.08f;
        Play("Dodge", 4.5f, 0.05f, DodgeStart);
        // 그림자 회수: each dodge gives back part of the other skills' cooldowns
        if (DualBladeSkills.Has(DualBladeSkills.Recall))
            foreach (Skill s in new[] { Skill.Flurry, Skill.Berserk, Skill.Execute, Skill.Dash })
                if (readyAt.TryGetValue(s, out float at) && at > Time.time)
                    readyAt[s] = Time.time + (at - Time.time) * 0.85f;
        PassThroughEnemies(true);
        float distance = dodgeDistance * (DualBladeSkills.Has(DualBladeSkills.Shadowstep) ? 1.25f : 1f);
        for (float t = 0f; t < dodgeTime; t += Time.deltaTime)
        {
            controller.Move(dir * (distance / dodgeTime) * Time.deltaTime);
            yield return null;
        }
        PassThroughEnemies(false);
        Play("Locomotion", 1f, 0.1f);
    }

    // The clone's strike: a crossing cut all round where the player stood
    public void CloneStrike(Vector3 at)
    {
        GameAudio.Play("dual_clone", at, 0.8f);
        // The clone's whirl: a ring of cuts all round where the player stood, and a crossing flash at its heart
        Cut("spinsheet", new Vector3(at.x, at.y - 0.6f, at.z), cloneRadius * 2.6f, Violet * 1.6f, 0.36f, 0.8f, 1.1f, 240f,
            Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), sheet: 4);
        Vfx.Sprite("hitsheet", at + Vector3.up * 0.2f, cloneRadius * 1.2f, Violet * 1.5f, 0.3f, 0.85f, 1.15f, 30f).Sheet(4, 4);
        List<Enemy> struck = HitArc(at, transform.forward, cloneRadius, 360f, cloneDamage);
        if (struck.Count > 0)
            CameraShake.Shake(0.1f, 0.1f);
    }

    // ───────────── Skill bar ─────────────

    private static readonly (Skill skill, string key, string name, string icon)[] Bar =
    {
        (Skill.Flurry, "Q", "난무", "dual_flurry"),
        (Skill.Dash, "E", "귀신베기", "dual_dash"),
        (Skill.Berserk, "R", "광폭화", "dual_berserk"),
        (Skill.Execute, "우클릭", "검기", "dual_execute"),
        (Skill.Dodge, "Space", "회피", "dual_dodge"),
    };

    private readonly SkillBar.Slot[] barSlots = new SkillBar.Slot[5];

    private void OnGUI()
    {
        if (!IsActive || SkillBar.Hidden)
            return;
        for (int i = 0; i < Bar.Length; i++)
        {
            var (skill, key, name, iconName) = Bar[i];
            bool berserkOn = skill == Skill.Berserk && Berserk;
            int sp = i < 4 ? Sp(SkillSystem.WeaponKeys[i == 0 ? 0 : i == 1 ? 1 : i == 2 ? 2 : 3]) : -1;
            int row = skill == Skill.Flurry ? 0 : skill == Skill.Dash ? 1 : skill == Skill.Berserk ? 2 : 3;
            if (sp >= 0) name = SpecialNames[row][sp];
            barSlots[i] = new SkillBar.Slot
            {
                key = key, name = name, icon = SkillBar.Icon("UI/" + iconName), learned = Learned(skill),
                left = CooldownLeft(skill), total = CooldownOf(skill), lit = berserkOn, litColor = Crimson,
                note = berserkOn ? (berserkUntil - Time.time).ToString("0.0") + "s" + (Sp('R') == 0 ? $" ×{frenzyStacks}" : "")
                    : skill == Skill.Dash && Sp('E') == 0 ? $"{dashCharges}/{DashCharges}회" : skill == Skill.Berserk && Exhausted ? "탈진" : null,
            };
        }
        SkillBar.Draw(barSlots);
    }
}

// The sword wave (검기): a crimson crescent racing along the ground, reaching every enemy in its path once.
// Scale: its damage; Homing (추적 검기): it bends toward the nearest enemy; Explode (폭발 검기): it bursts on the first hit.
public class ExecuteWave : MonoBehaviour
{
    private DualBlades owner;
    private Vector3 dir;
    private float travelled, range, width;
    private const float Speed = 30f;
    private const float WaveScale = 1.7f;   // the painted crescent's size against the wave's width
    private readonly HashSet<Enemy> reached = new HashSet<Enemy>();
    private Transform sprite, ink;
    public float Scale { get; private set; } = 1f;
    public bool Explode { get; private set; }
    private bool homing;

    public static void Fire(DualBlades owner, Vector3 from, Vector3 direction, float range, float width, float scale = 1f, bool homing = false, bool explode = false,
        bool moon = false)
    {
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
        var go = new GameObject("ExecuteWave");
        go.transform.SetPositionAndRotation(new Vector3(from.x, from.y - 0.6f, from.z), Quaternion.LookRotation(direction));
        var wave = go.AddComponent<ExecuteWave>();
        wave.owner = owner;
        wave.dir = direction;
        wave.range = range;
        wave.width = width;
        wave.Scale = scale;
        wave.homing = homing;
        wave.Explode = explode;
        // The crescent bulges forward (the painted arc bulges down, so it's turned round), standing up a little
        // The painted crescent (a looping VARCO flipbook, its arrow drawn pointing up and to the right) laid along the
        // ground and turned so the arrow points the way it flies
        Quaternion lying = Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, 45f);
        string art = "executesheet";
        // 월광참: the crescent-moon flipbook (VARCO, drawn bulging toward the top of the sheet) — laid flat with the
        // sheet's top pointing the way it flies, so the moon's belly leads and its horns trail
        string moonArt = moon ? Vfx.Pick("fx_moonwave", "") : "";
        if (moonArt != "")
        {
            art = moonArt;
            lying = Quaternion.LookRotation(direction) * Quaternion.Euler(90f, 0f, 0f);
        }
        float artSize = width * WaveScale * (moonArt != "" ? 1.15f : 1f);
        wave.artSize = artSize;
        VfxPiece ink = Vfx.Sprite(art + "_ink", go.transform.position, artSize, new Color(0.28f, 0.01f, 0.04f, 1f), 0f, 1f, 1f, 0f, lying, additive: false).Sheet(4, 4);
        VfxPiece piece = Vfx.Sprite(art, go.transform.position, artSize, (moonArt != "" ? new Color(1f, 0.55f, 0.65f) : DualBlades.Crimson) * 2.2f, 0f, 1f, 1f, 0f, lying).Sheet(4, 4);
        piece.GetComponent<Renderer>().sortingOrder = 1;
        ink.transform.SetParent(go.transform, true);
        piece.transform.SetParent(go.transform, true);
        wave.sprite = piece.transform;
        wave.ink = ink.transform;
        // (the trail kept narrow: across a huge 월광참 a quarter of its width smeared a red band over the art)
        Vfx.Trail(go, DualBlades.Crimson, Mathf.Min(width * 0.25f, 0.9f), 0.18f);
        HeimdallFx.Impact(from, 2.5f, DualBlades.Crimson);
    }

    private float artSize;

    private void Update()
    {
        // 추적 검기: turn toward the nearest enemy not yet cut
        if (homing)
        {
            Enemy best = null;
            float bestD = 12f;
            foreach (Enemy e in Enemy.Alive)
            {
                if (e == null || e.IsDead || reached.Contains(e)) continue;
                Vector3 off = e.transform.position - transform.position;
                off.y = 0f;
                if (off.magnitude < bestD) { bestD = off.magnitude; best = e; }
            }
            if (best != null)
            {
                Vector3 want = best.transform.position - transform.position;
                want.y = 0f;
                Vector3 nd = Vector3.RotateTowards(dir, want.normalized, 4f * Time.deltaTime, 0f);
                transform.rotation = Quaternion.FromToRotation(dir, nd) * transform.rotation;
                dir = nd;
            }
        }
        float step = Speed * (homing ? 0.7f : 1f) * Time.deltaTime;
        transform.position += dir * step;
        travelled += step;
        Vector3 pulse = Vector3.one * (artSize > 0f ? artSize : width * WaveScale) * (1f + Mathf.Sin(Time.time * 30f) * 0.05f);
        if (sprite != null)
            sprite.localScale = pulse;
        if (ink != null)
            ink.localScale = pulse;
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead || reached.Contains(enemy))
                continue;
            Vector3 rel = enemy.transform.position - transform.position;
            rel.y = 0f;
            if (Mathf.Abs(Vector3.Dot(rel, dir)) < 1.2f && Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, dir))) < width / 2f + 0.6f)
            {
                reached.Add(enemy);
                if (owner != null && owner.WaveHit(enemy, this))
                {
                    Destroy(gameObject);
                    return;
                }
            }
        }
        if (travelled >= range)
        {
            Vfx.Sprite("shadowsmoke", transform.position + Vector3.up * 0.5f, 2.5f, new Color(0.6f, 0.25f, 0.4f, 0.8f), 0.5f, 0.8f, 1.2f, 0f, additive: false).Sheet(4, 4);
            Destroy(gameObject);
        }
    }
}

// The dash's trail: a band of rushing violet light (VARCO) laid along the ground from where the dash began, fading
public class DashStreak : MonoBehaviour
{
    private LineRenderer line;
    private float fadeAt, t;

    public static DashStreak Spawn(Vector3 from)
    {
        var go = new GameObject("DashStreak");
        var streak = go.AddComponent<DashStreak>();
        streak.line = go.AddComponent<LineRenderer>();
        streak.line.sharedMaterial = HeimdallFx.Flow("dashstreak", new Vector2(-4f, 0f));
        streak.line.textureMode = LineTextureMode.Stretch;
        streak.line.positionCount = 2;
        streak.line.SetPosition(0, from);
        streak.line.SetPosition(1, from);
        streak.line.widthMultiplier = 1.3f;
        streak.line.alignment = LineAlignment.TransformZ;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        streak.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        streak.fadeAt = 0.25f;
        return streak;
    }

    public void Reach(Vector3 to)
    {
        if (line != null)
            line.SetPosition(1, to);
        t = 0f;
    }

    private void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01((t - 0.05f) / fadeAt);
        Color c = new Color(1f, 1f, 1f, 1f - k);
        line.startColor = line.endColor = c;
        line.widthMultiplier = 1.3f * (1f - k * 0.5f);
        if (k >= 1f)
            Destroy(gameObject);
    }
}

// A shadow of the player frozen in their pose: a dark violet ghost of the character and blades. A clone from a dodge
// strikes all round after a moment, then dissolves into shadow smoke; the dash leaves a string of brief afterimages.
public class ShadowClone : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Dictionary<Texture, Material> ghostMaterials = new Dictionary<Texture, Material>();
    private static readonly Dictionary<Texture, Material> inkMaterials = new Dictionary<Texture, Material>();

    private DualBlades owner;
    private float life, t, strikeAt;
    private bool strikes, struck;
    private Color color;
    // each part: a glowing copy (additive) over a dark silhouette (alpha-blended) — the glow alone vanished on Asgard's
    // pale marble, the dark copy keeps the clone readable on any floor
    private readonly List<Renderer> parts = new List<Renderer>();
    private readonly List<Renderer> inks = new List<Renderer>();
    private MaterialPropertyBlock block, inkBlock;

    // A clone that moves with the player (분신 in 난무 / 광폭화): every frame it takes the player's current pose,
    // turned by `followYaw` round the player and set `followOffset` (in the player's frame) away
    private bool follow;
    private float followYaw;
    private Vector3 followOffset;
    private readonly List<(SkinnedMeshRenderer skin, Transform part, Mesh mesh)> skins = new List<(SkinnedMeshRenderer, Transform, Mesh)>();
    private readonly List<(Transform source, Transform part)> rigid = new List<(Transform, Transform)>();

    public static ShadowClone Spawn(DualBlades owner, CharacterProfile character, float life, bool strikes, Color color)
    {
        if (character == null)
            return null;
        var root = new GameObject(strikes ? "ShadowClone" : "Afterimage");
        var clone = root.AddComponent<ShadowClone>();
        Transform anchor = owner != null ? owner.transform : character.transform;
        root.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        clone.owner = owner;
        clone.life = life;
        clone.strikes = strikes;
        clone.strikeAt = life - 0.6f;
        clone.color = color;
        clone.block = new MaterialPropertyBlock();
        clone.inkBlock = new MaterialPropertyBlock();
        foreach (SkinnedMeshRenderer skin in character.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            // (baked without the renderer's own scale: "with scale" came out at half the player's size on this rig)
            var mesh = new Mesh();
            skin.BakeMesh(mesh, false);
            Transform part = clone.AddPart(mesh, skin.transform.position, skin.transform.rotation, Vector3.one, skin.sharedMaterial, true);
            clone.skins.Add((skin, part, mesh));
        }
        foreach (MeshFilter filter in character.GetComponentsInChildren<MeshFilter>())
        {
            var r = filter.GetComponent<MeshRenderer>();
            if (r == null || !r.enabled || filter.sharedMesh == null)
                continue;
            Transform part = clone.AddPart(filter.sharedMesh, filter.transform.position, filter.transform.rotation, filter.transform.lossyScale, r.sharedMaterial, false);
            clone.rigid.Add((filter.transform, part));
        }
        clone.Tint(1f);
        return clone;
    }

    // Keep moving with the player instead of standing frozen in the pose it was made in
    public ShadowClone Follow(float yaw, Vector3 offset)
    {
        follow = true;
        followYaw = yaw;
        followOffset = offset;
        Pose();
        return this;
    }

    private void LateUpdate()
    {
        if (follow) Pose();
    }

    private void Pose()
    {
        if (owner == null) return;
        Transform p = owner.transform;
        Quaternion turn = p.rotation * Quaternion.Euler(0f, followYaw, 0f);
        transform.SetPositionAndRotation(p.position + p.rotation * followOffset, turn);
        Quaternion inv = Quaternion.Inverse(p.rotation);
        foreach (var (skin, part, mesh) in skins)
        {
            if (skin == null || part == null) continue;
            skin.BakeMesh(mesh, false);
            part.localPosition = p.InverseTransformPoint(skin.transform.position);
            part.localRotation = inv * skin.transform.rotation;
        }
        foreach (var (source, part) in rigid)
        {
            if (source == null || part == null) continue;
            part.localPosition = p.InverseTransformPoint(source.position);
            part.localRotation = inv * source.rotation;
        }
    }

    private Transform AddPart(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale, Material source, bool baked)
    {
        Texture tex = source != null && source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
        var go = new GameObject("Part");
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Ghost(tex);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        parts.Add(r);
        // the dark silhouette under it, a hair smaller so the glow rims it
        var ink = new GameObject("Ink");
        ink.transform.SetParent(go.transform, false);
        ink.transform.localScale = Vector3.one * 0.995f;
        ink.AddComponent<MeshFilter>().sharedMesh = mesh;
        var ir = ink.AddComponent<MeshRenderer>();
        ir.sharedMaterial = Ink(tex);
        ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        inks.Add(ir);
        if (baked)
            go.AddComponent<OwnedMesh>().mesh = mesh;
        return go.transform;
    }

    public static Material GhostMaterial(Texture texture) => Ghost(texture);
    public static Material InkMaterial(Texture texture) => Ink(texture);

    // The character's own painted colours drawn as glowing shadow: additive, tinted
    private static Material Ghost(Texture texture)
    {
        Texture key = texture != null ? texture : Texture2D.whiteTexture;
        if (ghostMaterials.TryGetValue(key, out Material mat) && mat != null)
            return mat;
        mat = new Material(Vfx.Material("glow", additive: true)) { name = "ghost_" + key.name };
        mat.SetTexture("_BaseMap", key);
        ghostMaterials[key] = mat;
        return mat;
    }

    // ...and the same shape as a see-through dark shadow (alpha-blended), drawn first
    private static Material Ink(Texture texture)
    {
        Texture key = texture != null ? texture : Texture2D.whiteTexture;
        if (inkMaterials.TryGetValue(key, out Material mat) && mat != null)
            return mat;
        mat = new Material(Vfx.Material("glow", additive: false)) { name = "ghostink_" + key.name };
        mat.SetTexture("_BaseMap", key);
        mat.renderQueue = 2990;   // (before the additive glow)
        inkMaterials[key] = mat;
        return mat;
    }

    private void Tint(float strength)
    {
        block.SetColor(ColorId, color * 1.4f * strength);
        foreach (Renderer r in parts)
            if (r != null)
                r.SetPropertyBlock(block);
        Color dark = Color.Lerp(Color.black, color, 0.35f);
        dark.a = 0.72f * Mathf.Clamp01(strength);
        inkBlock.SetColor(ColorId, dark);
        foreach (Renderer r in inks)
            if (r != null)
                r.SetPropertyBlock(inkBlock);
    }

    private void Update()
    {
        t += Time.deltaTime;
        if (strikes && !struck && t >= strikeAt)
        {
            struck = true;
            if (owner != null)
                owner.CloneStrike(transform.childCount > 0 ? transform.GetChild(0).position + Vector3.up * 0.9f : transform.position);
            Tint(2.2f);
        }
        float fade = strikes ? (struck ? Mathf.Clamp01(1f - (t - strikeAt) / 0.6f) : 0.75f + 0.25f * Mathf.Sin(t * 14f)) : 1f - t / life;
        Tint(Mathf.Max(0f, fade));
        if (t >= life)
        {
            if (strikes)
            {
                Vector3 at = transform.childCount > 0 ? transform.GetChild(0).position + Vector3.up * 0.9f : transform.position;
                Vfx.Sprite("shadowsmoke", at, 2.4f, new Color(0.55f, 0.35f, 0.8f, 0.85f), 0.6f, 0.7f, 1.3f, 0f, additive: false).Sheet(4, 4);
            }
            Destroy(gameObject);
        }
    }
}

// 그림자 광폭화's shadow: a full copy of the twin-blade fighter, drawn as a dark crimson ghost, that fights on its own
// for the berserk's span — it finds the nearest enemy, runs to it and cuts with the combo's blows — and when the player
// uses 난무, 귀신베기 or 검기 it does the same (always the plain version, never a special one).
public class ShadowAlly : MonoBehaviour
{
    public enum Move { Flurry, Dash, Wave }

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
    private static readonly string[] Blows = { "Blow1_Slash", "Blow2_Double", "Blow3_Stabs", "Blow4_SpinKick" };
    private static readonly Color Tint = new Color(1f, 0.25f, 0.35f);

    private DualBlades owner;
    private Animator anim;
    private float until, nextBlow, busyUntil, yOffset;
    private int blow;
    private Enemy target;
    private Coroutine move;

    public static ShadowAlly Summon(DualBlades owner, CharacterProfile character, Vector3 at, float seconds)
    {
        if (owner == null || character == null) return null;
        // copied under an inactive holder so none of the model's own scripts wake up before they're stripped
        var holder = new GameObject("ShadowAllyHolder");
        holder.SetActive(false);
        GameObject copy = Instantiate(character.gameObject, holder.transform);
        foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (Light l in copy.GetComponentsInChildren<Light>(true)) DestroyImmediate(l);
        copy.name = "ShadowAlly";
        copy.transform.SetParent(null, false);
        copy.transform.localScale = character.transform.lossyScale;
        float yOff = character.transform.position.y - owner.transform.position.y;
        copy.transform.SetPositionAndRotation(new Vector3(at.x, owner.transform.position.y + yOff, at.z), owner.transform.rotation);
        Destroy(holder);
        // the ghost look: a dark see-through body with a crimson glow over it
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            if (r is TrailRenderer || r is ParticleSystemRenderer) continue;
            Texture tex = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseMap") ? r.sharedMaterial.GetTexture("_BaseMap") : null;
            r.sharedMaterials = new[] { ShadowClone.InkMaterial(tex), ShadowClone.GhostMaterial(tex) };
            var ink = new MaterialPropertyBlock();
            ink.SetColor("_BaseColor", new Color(0.18f, 0.02f, 0.06f, 0.75f));
            r.SetPropertyBlock(ink, 0);
            var glow = new MaterialPropertyBlock();
            glow.SetColor("_BaseColor", Tint * 1.3f);
            r.SetPropertyBlock(glow, 1);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        copy.SetActive(true);
        var ally = copy.AddComponent<ShadowAlly>();
        ally.owner = owner;
        ally.until = Time.time + seconds;
        ally.yOffset = yOff;
        ally.anim = copy.GetComponentInChildren<Animator>();
        if (ally.anim != null)
        {
            ally.anim.applyRootMotion = false;
            ally.anim.CrossFadeInFixedTime("Locomotion", 0f);
        }
        // it steps out of a column of shadow smoke (the VARCO summoning sheet when it's in)
        string summon = Vfx.Pick("fx_clone_summon", "");
        Vector3 feet = new Vector3(at.x, owner.transform.position.y - 0.9f, at.z);
        if (summon != "") DualBlades.Cut(summon, feet + Vector3.up * 1.2f, 3.6f, Tint * 1.5f, 0.9f, 0.9f, 1.1f, 0f, sheet: 4);
        Vfx.Sprite("shadowsmoke", feet + Vector3.up * 0.6f, 3.2f, new Color(0.45f, 0.1f, 0.18f, 0.9f), 0.8f, 0.7f, 1.3f, 0f, additive: false).Sheet(4, 4);
        DualBlades.Say(copy.transform.position + Vector3.up * 2.2f, "그림자 분신!", Tint);
        return ally;
    }

    private Vector3 Ground(Vector3 p) => new Vector3(p.x, owner.transform.position.y + yOffset, p.z);

    // (the copy came out half the player's height on this rig: once both have posed, match the drawn heights)
    private IEnumerator Start()
    {
        yield return null;
        yield return null;
        float mine = Height(gameObject), theirs = 0f;
        var sw = FindAnyObjectByType<CharacterSwitcher>();
        if (sw != null && sw.Current != null) theirs = Height(sw.Current.gameObject);
        if (mine > 0.05f && theirs > 0.05f && Mathf.Abs(theirs / mine - 1f) > 0.08f)
        {
            float k = theirs / mine;
            Vector3 feet = transform.position;
            transform.localScale *= k;
            transform.position = feet;
        }
    }

    private static float Height(GameObject go)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            lo = Mathf.Min(lo, r.bounds.min.y);
            hi = Mathf.Max(hi, r.bounds.max.y);
        }
        return hi > lo ? hi - lo : 0f;
    }

    private void Update()
    {
        if (owner == null || !owner.IsActive || Time.time >= until)
        {
            Vfx.Sprite("shadowsmoke", transform.position + Vector3.up * 0.8f, 2.6f, new Color(0.45f, 0.1f, 0.18f, 0.85f), 0.6f, 0.7f, 1.3f, 0f, additive: false).Sheet(4, 4);
            Destroy(gameObject);
            return;
        }
        if (Time.time < busyUntil) return;   // (in the middle of a copied skill)
        if (target == null || target.IsDead || Flat(target.transform.position - owner.transform.position) > 14f)
            target = Nearest();
        Vector3 goal;
        if (target != null) goal = target.transform.position;
        else goal = owner.transform.position + owner.transform.right * 1.6f;   // nobody about: at the player's side
        Vector3 to = goal - transform.position;
        to.y = 0f;
        float reach = target != null ? 1.7f : 0.5f;
        if (to.magnitude > reach)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 720f * Time.deltaTime);
            transform.position = Ground(transform.position + to.normalized * Mathf.Min(to.magnitude - reach * 0.8f, 7f * Time.deltaTime));
            SetSpeed(1f);
            if (anim != null && Time.time >= nextBlow - 0.3f && !InState("Locomotion")) anim.CrossFadeInFixedTime("Locomotion", 0.1f);
        }
        else
        {
            SetSpeed(0f);
            if (target != null)
            {
                if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
                if (Time.time >= nextBlow) StartCoroutine(Strike());
            }
        }
    }

    private IEnumerator Strike()
    {
        nextBlow = Time.time + 0.55f;
        string state = Blows[blow++ % Blows.Length];
        if (anim != null)
        {
            anim.SetFloat(AttackSpeedHash, 2f);
            anim.CrossFadeInFixedTime(state, 0.06f);
        }
        yield return new WaitForSeconds(0.2f);
        Vector3 fwd = transform.forward, chest = transform.position + Vector3.up * 0.9f;
        Quaternion plane = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 0f, blow % 2 == 0 ? 20f : -20f) * Quaternion.Euler(90f, 0f, 0f)
            * Quaternion.Euler(0f, blow % 2 == 0 ? 0f : 180f, 0f) * Quaternion.Euler(0f, 0f, -90f);
        DualBlades.Cut("slashsheet", chest + fwd * 0.8f, 4.2f, Tint * 1.5f, 0.28f, 0.9f, 1.1f, 0f, plane, sheet: 4);
        owner.AllyHit(transform.position, fwd, 2.4f, 150f, 0.55f);
    }

    // The player used a skill: the shadow does the same (the plain version)
    public void Mimic(Move what)
    {
        if (move != null) StopCoroutine(move);
        move = StartCoroutine(Copy(what));
    }

    private IEnumerator Copy(Move what)
    {
        Enemy aimAt = target != null && !target.IsDead ? target : Nearest();
        Vector3 dir = aimAt != null ? aimAt.transform.position - transform.position : owner.transform.forward;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(dir);
        SetSpeed(0f);
        switch (what)
        {
            case Move.Flurry:
            {
                busyUntil = Time.time + 1.3f;
                if (anim != null) { anim.SetFloat(AttackSpeedHash, 3f); anim.CrossFadeInFixedTime("Flurry", 0.06f); }
                for (float t = 0f; t < 1.2f; t += 0.12f)
                {
                    Vector3 at = transform.position + transform.forward * Random.Range(0.8f, 2.4f) + transform.right * Random.Range(-1f, 1f) + Vector3.up * 0.8f;
                    DualBlades.Cut("flurrysheet", at, Random.Range(1.8f, 2.6f), Tint * 1.6f, 0.3f, 0.9f, 1.1f, Random.Range(-90f, 90f), sheet: 4);
                    owner.AllyHit(transform.position, transform.forward, 3f, 130f, 0.25f);
                    yield return new WaitForSeconds(0.12f);
                }
                break;
            }
            case Move.Dash:
            {
                busyUntil = Time.time + 0.45f;
                if (anim != null) { anim.SetFloat(AttackSpeedHash, 2.1f); anim.CrossFadeInFixedTime("Dash", 0.05f); }
                Vector3 start = transform.position;
                float len = aimAt != null ? Mathf.Min(6f, Flat(aimAt.transform.position - start) + 0.5f) : 5f;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                DualBlades.Cut("dashsheet", start + dir * (len * 0.5f) + Vector3.up * 0.1f, len * 1.25f, Tint * 1.5f, 0.45f, 0.9f, 1.05f, 0f, Quaternion.Euler(90f, yaw - 90f, 0f), sheet: 4);
                for (float t = 0f; t < 0.2f; t += Time.deltaTime)
                {
                    transform.position = Ground(transform.position + dir * (len / 0.2f) * Time.deltaTime);
                    yield return null;
                }
                owner.AllyHit(transform.position, dir, 2f, 120f, 0.8f);
                break;
            }
            case Move.Wave:
            {
                busyUntil = Time.time + 0.5f;
                if (anim != null) { anim.SetFloat(AttackSpeedHash, 1.8f); anim.CrossFadeInFixedTime("ExecuteRelease", 0.05f); }
                yield return new WaitForSeconds(0.2f);
                ExecuteWave.Fire(owner, transform.position + Vector3.up * 0.9f, dir, 12f, 3f, 0.6f);
                break;
            }
        }
        move = null;
    }

    private bool InState(string state) => anim != null && anim.GetCurrentAnimatorStateInfo(0).IsName(state);

    private void SetSpeed(float s)
    {
        if (anim != null) anim.SetFloat(SpeedHash, s, 0.1f, Time.deltaTime);
    }

    private Enemy Nearest()
    {
        Enemy best = null;
        float bestD = 14f;
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead) continue;
            float d = Flat(e.transform.position - owner.transform.position);
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
}

// Frees a baked mesh with the object that shows it
public class OwnedMesh : MonoBehaviour
{
    public Mesh mesh;

    private void OnDestroy()
    {
        if (mesh != null)
            Destroy(mesh);
    }
}
