using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using static SkillTree;

// The plain fighter's (the pickaxe character's) skills. Each starts bare — the motion and the damage — and the
// 번개 스킬트리 ([K], its own tab) builds it up, step by step, into its lightning form:
//   Q 망치 투척  — throws a hammer that bounces between nearby enemies and flies back.
//                 2 enemies → (튕기는 망치) 3 → (Q 마스터: 뇌신의 망치) 4, with lightning on every hit.
//   E 번개 방출  — hold to charge (the longer, the harder, up to a cap), let go: it strikes the nearest enemy and
//                 jumps on. 1 enemy → (연쇄 방전, 3 ranks) 4 → (E 마스터: 천둥의 심판) 6, with great bolts.
//   R 휠윈드     — spin, striking everything round about. (감전) jolts what it hits with sparks →
//                 (R 마스터: 뇌전 회오리) electric blasts on every enemy struck, in a whirling storm.
//   Space        — the dash (PlayerMovement), as before.
// The lightning is blue with a fifth of gold (StormFx).
public class StormSkills : MonoBehaviour
{
    // ── The tree (번개 망치 스킬트리: the plain fighter's and 묠니르's, one tree, laid out like the greatsword's) ──
    // The skills, locked until learned
    public const string Dash = "st_dash";        // 대시 [Space] — the trunk's 2nd node
    public const string QSkill = "q_throw";      // 망치 투척 [Q]
    public const string ESkill = "e_bolt";       // 번개 방출 [E]
    public const string RSkill = "r_whirl";      // 휠윈드 [R]
    public const string Blink = "st_blink";      // 번개 걸음: a longer dash
    public const string Tempo = "st_tempo";      // 천둥의 리듬: Q E R come round 20% sooner
    // Q
    public const string QGrip = "q_grip";        // 단단한 손잡이: Q +15% a rank (3)
    public const string QBounce = "q_bounce";    // 튕기는 망치: one more enemy
    public const string QWeight = "q_weight";    // 묵직한 망치: +20%, and it strikes on the way back too
    public const string QMaster = "q_master";    // 뇌신의 망치: one more enemy, lightning, lightning damage
    // E
    public const string EChain = "e_chain";      // 연쇄 방전: one more enemy a rank (3)
    public const string ECharge = "e_charge";    // 과충전: a longer, harder charge a rank (2)
    public const string EQuick = "e_quick";      // 빠른 축전: charges 30% faster
    public const string EMaster = "e_master";    // 천둥의 심판: two more enemies, great bolts, +40%
    // R
    public const string RSpin = "r_spin";        // 회전력: R +15% a rank (3)
    public const string RShock = "r_shock";      // 감전: what it hits is jolted, sparks fly
    public const string RLong = "r_long";        // 폭풍의 눈: longer and wider
    public const string RMaster = "r_master";    // 뇌전 회오리: electric blasts, lightning damage, the storm ring

    private const Branch T = Branch.Trunk, A = Branch.Attack, S = Branch.Survival, M = Branch.Speed;
    private static void None(RunStats s, int r) { }

    private static readonly Node[] NodeList =
    {
        // ── 줄기: the basics ──
        Stat("st_root", "번개 망치술", "공격력 +5%", null, T, 0, 0f, "망", 1, (s, r) => s.damageMultiplier *= 1f + 0.05f * r),
        Skill(Dash, "대시 [Space]", "짧게 앞으로 박차고 나갑니다 (묠니르: 회피)", "st_root", T, 1, 0f, "gs_rush"),
        Stat("st_basics", "기초 단련", "최대 체력 +5, 공격력 +2%", Dash, T, 2, 0f, "기", 3, (s, r) => { s.maxHealthBonus += 5f * r; s.damageMultiplier *= 1f + 0.02f * r; }),
        Stat("st_breath", "호흡", "이동 속도 +2%, 공격 속도 +2%", "st_basics", T, 3, 0f, "호", 2, (s, r) => { s.moveSpeedMultiplier *= 1f + 0.02f * r; s.attackSpeedMultiplier *= 1f + 0.02f * r; }),

        // ── 공격: Q 망치 투척 ──
        Stat("st_heft", "망치의 무게", "공격력 +4%", "st_breath", A, 4, -0.18f, "공", 3, (s, r) => s.damageMultiplier *= 1f + 0.04f * r),
        Skill(QSkill, "망치 투척 [Q]", "망치를 던집니다. 근처 적 최대 2명에게 튕기고 손으로 돌아옵니다", "st_heft", A, 5, -0.28f, "storm_chain"),
        Stat(QGrip, "단단한 손잡이", "망치 투척 피해 +15%", QSkill, A, 6, -0.22f, "손", 3, None),
        Stat("st_volt", "고전압", "공격력 +4%", QSkill, A, 6, -0.44f, "압", 3, (s, r) => s.damageMultiplier *= 1f + 0.04f * r),
        Upgrade(QBounce, "튕기는 망치", "망치가 1명 더 튕깁니다 (최대 3명)", QGrip, A, 7, -0.26f, "튕"),
        Stat("st_bolt", "벼락", "치명타 피해 +15%", "st_volt", A, 7, -0.5f, "벼", 2, (s, r) => s.critMultiplier += 0.15f * r),
        Upgrade(QWeight, "묵직한 망치", "망치 투척 피해 +20%, 돌아오는 길의 적도 때립니다", QBounce, A, 8, -0.3f, "묵"),
        Stat("st_crush", "파쇄", "치명타 확률 +3%", "st_bolt", A, 8, -0.56f, "치", 3, (s, r) => s.critChance += 0.03f * r),
        Upgrade(QMaster, "뇌신의 망치 (Q 마스터)", "망치가 최대 4명까지 튕기고, 번개를 두릅니다: 맞는 적마다 낙뢰와 번개 추가 피해", QWeight, A, 9, -0.36f, "뇌"),
        Stat("st_chain", "연쇄 번개", "공격이 주변 적 1명에게 번개로 튑니다", "st_crush", A, 9, -0.62f, "뇌", 1, (s, r) => s.chainTargets += r),
        Stat("st_fury", "천둥신", "공격력 +6%", QMaster, A, 10, -0.46f, "신", 3, (s, r) => s.damageMultiplier *= 1f + 0.06f * r),

        // ── 생존: R 휠윈드 ──
        Stat("st_sturdy", "강인함", "최대 체력 +8", "st_breath", S, 4, 0f, "체", 3, (s, r) => s.maxHealthBonus += 8f * r),
        Skill(RSkill, "휠윈드 [R]", "망치를 휘두르며 회전해 주변을 계속 때립니다 (묠니르: 우클릭 회전 베기도 함께)", "st_sturdy", S, 5, 0f, "storm_whirl"),
        Stat(RSpin, "회전력", "휠윈드 피해 +15%", RSkill, S, 6, -0.07f, "회", 3, None),
        Stat("st_thirst", "피의 갈증", "적 처치 시 HP 1 회복", RSkill, S, 6, 0.07f, "흡", 3, (s, r) => s.healOnKill += 1f * r),
        Upgrade(RShock, "감전", "휠윈드에 맞은 적이 감전됩니다: 잠깐 경직, 전기가 튑니다", RSpin, S, 7, -0.08f, "감"),
        Stat("st_guard", "단련된 몸", "받는 피해 -3%", "st_thirst", S, 7, 0.08f, "방", 3, (s, r) => s.damageTakenMultiplier *= 1f - 0.03f * r),
        Upgrade(RLong, "폭풍의 눈", "휠윈드 시간 +30%, 범위 +0.6m", RShock, S, 8, -0.1f, "눈"),
        Stat("st_giant", "거인의 심장", "최대 체력 +12", "st_guard", S, 8, 0.1f, "심", 3, (s, r) => s.maxHealthBonus += 12f * r),
        Upgrade(RMaster, "뇌전 회오리 (R 마스터)", "번개 회오리를 두르고, 맞은 적마다 전기 폭발과 번개 추가 피해", RLong, S, 9, -0.08f, "폭"),
        Stat("st_bulwark", "철벽", "전투마다 첫 피격 1회 무효", "st_giant", S, 9, 0.08f, "벽", 1, (s, r) => s.wardsPerRoom += r),
        Stat("st_undying", "불굴", "최대 체력 +20, 받는 피해 -8%", RMaster, S, 10, 0f, "굴", 1, (s, r) => { s.maxHealthBonus += 20f * r; s.damageTakenMultiplier *= 1f - 0.08f * r; }),

        // ── 이속: E 번개 방출 ──
        Stat("st_light", "가벼운 발걸음", "이동 속도 +3%", "st_breath", M, 4, 0.18f, "이", 3, (s, r) => s.moveSpeedMultiplier *= 1f + 0.03f * r),
        Skill(ESkill, "번개 방출 [E]", "꾹 눌러 모으고, 놓으면 가장 가까운 적을 칩니다. 오래 모을수록 강해집니다", "st_light", M, 5, 0.28f, "storm_strike"),
        Stat(EChain, "연쇄 방전", "번개가 1명 더 튕깁니다 (최대 4명)", ESkill, M, 6, 0.22f, "연", 3, None),
        Stat("st_quick", "빠른 손", "공격 속도 +3%", ESkill, M, 6, 0.44f, "속", 3, (s, r) => s.attackSpeedMultiplier *= 1f + 0.03f * r),
        Stat(ECharge, "과충전", "최대 충전 시간 +0.5초, 최대 충전 피해 +25%", EChain, M, 7, 0.26f, "충", 2, None),
        Stat("st_after", "잔상", "피해 회피 확률 +4%", "st_quick", M, 7, 0.5f, "잔", 3, (s, r) => s.dodgeChance += 0.04f * r),
        Upgrade(EQuick, "빠른 축전", "충전 속도 +30%", ECharge, M, 8, 0.3f, "축"),
        Upgrade(Blink, "번개 걸음", "대시(회피) 거리 +30%", "st_after", M, 8, 0.56f, "걸"),
        Upgrade(EMaster, "천둥의 심판 (E 마스터)", "번개가 최대 6명까지 튕기고, 거대한 낙뢰가 떨어집니다. 번개 피해 +40%", EQuick, M, 9, 0.36f, "심"),
        Upgrade(Tempo, "천둥의 리듬", "망치 투척·번개 방출·휠윈드 재사용 시간 -20%", Blink, M, 9, 0.62f, "리"),
        Stat("st_windlord", "폭풍의 화신", "이동 속도 +6%, 공격 속도 +6%", EMaster, M, 10, 0.46f, "화", 1, (s, r) => { s.moveSpeedMultiplier *= 1f + 0.06f * r; s.attackSpeedMultiplier *= 1f + 0.06f * r; }),
    };

    public static readonly SkillTree Tree = new SkillTree("번개 망치 스킬트리", NodeList);
    public static float DashScale => Has(Blink) ? 1.3f : 1f;
    public static bool Has(string id) => Tree.Has(id);
    public static int Rank(string id) => Tree.Rank(id);

    // ── Q 망치 투척 ──
    [Header("Q 망치 투척")]
    public float throwCooldown = 6f;
    public float throwReach = 13f;
    public float throwPick = 5f;          // the enemy nearest the cursor within this, else the nearest in front
    public float throwJump = 7f;
    public float throwSpeed = 24f;
    public float throwDamage = 1.4f;
    public float throwLightning = 0.45f;  // Q 마스터: the extra bolt on each hit

    // ── E 번개 방출 ──
    [Header("E 번개 방출 (charged)")]
    public float dischargeCooldown = 7f;
    public float chargeTime = 1f;         // the base cap; 과충전 adds 0.5s a rank
    public float dischargeMin = 1f;       // damage at a tap...
    public float dischargeMax = 2f;       // ...and at full charge
    public float dischargeReach = 11f;
    public float dischargeJump = 6.5f;

    // ── R 휠윈드 ──
    [Header("R 휠윈드")]
    public float whirlCooldown = 9f;
    public float whirlTime = 2.4f;
    public float whirlRadius = 2.8f;
    public float whirlTick = 0.3f;
    public float whirlDamage = 0.45f;
    public float stormDamage = 0.4f;      // R 마스터: the bolt from the sky on each enemy the head strikes
    public float splashDamage = 0.3f;     // ...and the lightning running over the ground to those near it
    public float splashRadius = 2.5f;
    public int forkCount = 3;             // forks that lash out of the struck enemy at once
    public int forkHops = 2;              // ...and how many times each leaps on

    private PlayerMiner miner;
    private PlayerStats stats;
    private PlayerMovement movement;
    private DualBlades blades;
    private float throwReadyAt, dischargeReadyAt, whirlReadyAt;
    private bool busy, whirling, charging;
    private float chargeStart;
    private VfxPiece whirlRing, chargeGlow;
    private GameObject whirlHead;
    private Transform spinModel;
    private Quaternion spinModelRest;
    private GameObject hammerModel;

    public bool Whirling => whirling;
    public bool Charging => charging;
    public float ThrowCooldownLeft => Mathf.Max(0f, throwReadyAt - Time.time);
    public float DischargeCooldownLeft => Mathf.Max(0f, dischargeReadyAt - Time.time);
    public float WhirlCooldownLeft => Mathf.Max(0f, whirlReadyAt - Time.time);

    private static float CD => Has(Tempo) ? 0.8f : 1f;

    // For the plain fighter, and for 묠니르 (the 번개 망치 kit is its Q E R): the greatsword, the twin blades and the other
    // armory weapons bring their own skills
    public bool Active => miner != null && (blades == null || !blades.IsActive)
                          && (Mjolnir != null || (!miner.CanCharge && (ArmoryWeapon.Current == null || !ArmoryWeapon.Current.IsActive)));

    // 묠니르 in hand: the kit plays on its model (its throw and slam clips, its own hammer flying)
    private static MjolnirHammer Mjolnir => ArmoryWeapon.Current is MjolnirHammer m && m.IsActive ? m : null;

    private void Awake()
    {
        miner = GetComponent<PlayerMiner>();
        stats = GetComponent<PlayerStats>();
        movement = GetComponent<PlayerMovement>();
        blades = GetComponent<DualBlades>();
    }

    private void Update()
    {
        bool on = Active;
        // (묠니르 shares this tree, so its stats count while the hammer is in hand too)
        Tree.Active = on || (ArmoryWeapon.Current != null && ArmoryWeapon.Current.IsActive && ArmoryWeapon.Current.Tree == Tree);
        Hud(on);
        if (!on || !GameManager.IsRunActive || Time.timeScale <= 0f || (stats != null && stats.IsDead))
        {
            if (charging) CancelCharge();
            return;
        }
        var kb = Keyboard.current;
        if (kb == null)
            return;

        // E: held to charge, let go to discharge
        if (charging)
        {
            UpdateCharge();
            if (!SkillSystem.KeyHeld(kb, 'E')) Discharge();
            return;
        }
        // (Q / E / R are skill orbs now: fired from whichever slot holds them)
        if (busy)
            return;
        if (SkillSystem.KeyDown(kb, 'Q') && Has(QSkill) && ThrowCooldownLeft <= 0f) StartCoroutine(ThrowHammer(Cursor()));
        else if (SkillSystem.KeyDown(kb, 'E') && Has(ESkill) && DischargeCooldownLeft <= 0f && !whirling) BeginCharge();
        else if (SkillSystem.KeyDown(kb, 'R') && Has(RSkill) && WhirlCooldownLeft <= 0f && !whirling) StartCoroutine(Whirl());
    }

    // ── The round slots at the bottom right: Q E R, right click (none), Space (the dash) ──
    private Texture2D iconThrow, iconCharge, iconWhirl;
    private bool iconsShown;

    private void Hud(bool on)
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.slotCooldowns == null || gm.slotIcons == null) return;
        if (!on)
        {
            if (iconsShown)
            {
                for (int i = 0; i < 5; i++) { gm.slotIcons[i] = null; gm.slotNames[i] = null; gm.slotLocked[i] = false; gm.slotCooldowns[i] = 0f; gm.slotActive[i] = false; }
                iconsShown = false;
            }
            return;
        }
        iconThrow ??= Resources.Load<Texture2D>("UI/storm_chain");
        iconCharge ??= Resources.Load<Texture2D>("UI/storm_strike");
        iconWhirl ??= Resources.Load<Texture2D>("UI/storm_whirl");
        // (the game's skill bar: a slot is greyed until its skill is learned)
        gm.slotIcons[0] = iconThrow; gm.slotNames[0] = "망치 투척"; gm.slotLocked[0] = !Has(QSkill);
        gm.slotIcons[1] = iconCharge; gm.slotNames[1] = "번개 방출"; gm.slotLocked[1] = !Has(ESkill);
        gm.slotIcons[2] = iconWhirl; gm.slotNames[2] = "휠윈드"; gm.slotLocked[2] = !Has(RSkill);
        if (Mjolnir == null) { gm.slotIcons[4] = SkillBar.Icon("UI/gs_rush"); gm.slotNames[4] = "대시"; }
        iconsShown = true;
        gm.slotCooldowns[0] = ThrowCooldownLeft;
        gm.slotCooldowns[1] = charging ? 0f : DischargeCooldownLeft;
        gm.slotCooldowns[2] = whirling ? 0f : WhirlCooldownLeft;
        // (묠니르's Space is its own dodge: it fills that slot itself)
        if (Mjolnir == null) gm.slotCooldowns[4] = movement != null ? movement.DashCooldownLeft : 0f;
        gm.slotActive[1] = charging;
        gm.slotActive[2] = whirling;
    }

    // ── Helpers ──
    private Vector3 Cursor()
    {
        var aim = FindAnyObjectByType<ClickToDig>();
        return aim != null ? aim.CursorGroundPoint() : transform.position + transform.forward * 4f;
    }

    private float Feet => transform.position.y - 0.92f;
    private Vector3 Hand => transform.position + Vector3.up * 1.2f + transform.forward * 0.5f;
    private static Vector3 Chest(Enemy e) => e.transform.position + Vector3.up * (0.9f * Mathf.Max(0.6f, e.transform.localScale.y));
    private Vector3 FloorUnder(Vector3 p) => new Vector3(p.x, Feet, p.z);

    private int Roll(float multiplier, out bool critical)
    {
        int damage = stats != null ? stats.Damage : 1;
        critical = Random.value < RunState.Stats.critChance;
        if (critical) damage = Mathf.RoundToInt(damage * RunState.Stats.critMultiplier);
        return Mathf.Max(1, Mathf.RoundToInt(damage * multiplier));
    }

    private void Hit(Enemy enemy, float multiplier)
    {
        if (enemy == null || enemy.IsDead) return;
        miner.StrikeEnemy(enemy, Roll(multiplier, out bool critical), critical);
    }

    private static void Jolt(Enemy enemy, float seconds)
    {
        if (enemy != null && !enemy.IsDead) enemy.Knockdown(seconds);
    }

    private void Face(Vector3 point)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
    }

    // the body's blow: 묠니르 plays one of its own clips (attack, throw, slam), the plain fighter its Attack trigger
    private void Swing(string mjolnirClip = "attack", float speed = 1.5f, float from = 0f, float seconds = 0.55f)
    {
        var m = Mjolnir;
        if (m != null) { m.ExternalPlay(mjolnirClip, speed, from, seconds); return; }
        var anim = GetComponentInChildren<Animator>();
        if (anim == null) return;
        foreach (var p in anim.parameters)
            if (p.name == "Attack" && p.type == AnimatorControllerParameterType.Trigger) { anim.SetTrigger("Attack"); return; }
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    // The enemy nearest the cursor (within 'pick', and within 'reach' of the player), else the nearest one in front
    private Enemy FirstTarget(Vector3 cursor, float reach, float pick)
    {
        Enemy first = null;
        float best = pick;
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead || Flat(e.transform.position - transform.position) > reach) continue;
            float d = Flat(e.transform.position - cursor);
            if (d < best) { best = d; first = e; }
        }
        if (first != null) return first;
        best = reach;
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead) continue;
            Vector3 to = e.transform.position - transform.position;
            float d = Flat(to);
            if (d < best && Vector3.Angle(transform.forward, new Vector3(to.x, 0f, to.z)) < 75f) { best = d; first = e; }
        }
        return first;
    }

    private static Enemy NextTarget(Vector3 from, float range, List<Enemy> struck)
    {
        Enemy next = null;
        float best = range;
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead || struck.Contains(e)) continue;
            float d = Vector3.Distance(from, e.transform.position);
            if (d < best) { best = d; next = e; }
        }
        return next;
    }

    // ═══════════════ Q: 망치 투척 ═══════════════

    private int ThrowTargets => 2 + (Has(QBounce) ? 1 : 0) + (Has(QMaster) ? 1 : 0);
    private float ThrowMultiplier => throwDamage * (1f + 0.15f * Rank(QGrip)) * (Has(QWeight) ? 1.2f : 1f);

    private GameObject MakeHammer()
    {
        GameObject h;
        // 묠니르: its own hammer leaves the fist (a copy flies, the one in hand hides until it comes back)
        var m = Mjolnir;
        if (m != null && m.HeldHammer != null)
        {
            var held = m.HeldHammer;
            h = Instantiate(held.gameObject, held.position, held.rotation);
            h.transform.localScale = held.lossyScale;
            foreach (var c in h.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var mb in h.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            foreach (var r in held.GetComponentsInChildren<Renderer>()) r.enabled = false;
            m.ExternalHammerAway = true;
            h.name = "ThrownHammer";
            return h;
        }
        if (hammerModel == null) hammerModel = Resources.Load<GameObject>("Weapons/ThrowHammer");
        if (hammerModel != null)
        {
            h = Instantiate(hammerModel);
            foreach (var c in h.GetComponentsInChildren<Collider>()) Destroy(c);
            // about a metre long, whatever the model's own size
            var b = new Bounds(); bool f = false;
            foreach (var r in h.GetComponentsInChildren<Renderer>()) { if (!f) { b = r.bounds; f = true; } else b.Encapsulate(r.bounds); }
            float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (size > 0.01f) h.transform.localScale *= 1.1f / size;
        }
        else
        {
            h = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(h.GetComponent<Collider>());
            h.transform.localScale = new Vector3(0.4f, 0.9f, 0.4f);
        }
        h.name = "ThrownHammer";
        return h;
    }

    // the thrown hammer is back in 묠니르's fist
    private void CatchHammer()
    {
        var m = Mjolnir ?? (ArmoryWeapon.Current as MjolnirHammer);
        if (m == null) return;
        if (m.HeldHammer != null) foreach (var r in m.HeldHammer.GetComponentsInChildren<Renderer>()) r.enabled = true;
        m.ExternalHammerAway = false;
    }

    private IEnumerator ThrowHammer(Vector3 cursor)
    {
        busy = true;
        Face(cursor);
        Swing("throw", 1.3f, 0f, 0.55f);
        bool master = Has(QMaster), weight = Has(QWeight);
        // 뇌신의 망치: the hand gathers lightning before it lets go
        if (master)
        {
            StormFx.Light(Hand, 4f, 3f, 0.25f);
            for (int i = 0; i < 4; i++)
                Vfx.Bolt(Hand + Random.onUnitSphere * 0.9f, Hand, i == 0 ? StormFx.Gold : StormFx.Blue, 0.22f, 0.18f);
            Vfx.Flash(Hand, 1.2f, StormFx.Blue, 0.2f);
        }
        yield return new WaitForSeconds(0.2f);
        busy = false;
        throwReadyAt = Time.time + throwCooldown * CD;

        int targets = ThrowTargets;
        float mult = ThrowMultiplier;
        var hammer = MakeHammer();
        hammer.transform.position = Hand;
        DressHammer(hammer, master, weight);
        if (master) { Vfx.Flash(Hand, 2.2f, Color.white, 0.12f); CameraShake.Shake(0.08f, 0.06f); }

        var struck = new List<Enemy>();
        Enemy current = FirstTarget(cursor, throwReach, throwPick);
        Vector3 Pos() => hammer.transform.position;
        float spin = 0f;
        Vector3 lastAt = Hand;   // where the lightning last ran from (the hand, then each enemy struck)

        // with nothing in reach it flies out toward the cursor and back
        if (current == null)
        {
            Vector3 to = cursor - transform.position; to.y = 0f;
            Vector3 end = Hand + (to.sqrMagnitude > 0.01f ? to.normalized : transform.forward) * Mathf.Min(to.magnitude, 9f);
            while (Vector3.Distance(Pos(), end) > 0.4f)
            {
                hammer.transform.position = Vector3.MoveTowards(Pos(), end, throwSpeed * Time.deltaTime);
                spin += 1300f * Time.deltaTime; hammer.transform.rotation = Quaternion.AngleAxis(spin, transform.right);
                Crackle(Pos(), master, weight);
                yield return null;
            }
        }

        while (current != null && struck.Count < targets)
        {
            // homes in on the enemy, wherever it moves
            float time = 0f;
            while (current != null && !current.IsDead && Vector3.Distance(Pos(), Chest(current)) > 0.6f && time < 1.5f)
            {
                time += Time.deltaTime;
                hammer.transform.position = Vector3.MoveTowards(Pos(), Chest(current), throwSpeed * Time.deltaTime);
                spin += 1300f * Time.deltaTime; hammer.transform.rotation = Quaternion.AngleAxis(spin, transform.right);
                Crackle(Pos(), master, weight);
                yield return null;
            }
            if (current == null) break;
            struck.Add(current);
            Hit(current, mult);
            Vector3 at = Chest(current);
            CameraShake.Shake(0.1f, 0.08f);
            if (master)
            {
                // 뇌신의 망치: lightning races flat out along the hammer's path into each one it strikes, and bursts out
                // of it in a star of short forks over a flash of light on the floor
                StartCoroutine(StormFx.Run(lastAt, at, 0.75f, 5));
                for (int i = 0; i < 5; i++)
                {
                    Vector3 dir = Quaternion.Euler(Random.Range(-15f, 25f), i * 72f + Random.Range(-18f, 18f), 0f) * Vector3.forward;
                    StormFx.Fork(at, at + dir * Random.Range(1.3f, 2.2f), 0.35f);
                }
                StormFx.Impact(FloorUnder(at), 3.2f, 0.4f);
                Vfx.Flash(at, 3f, Color.white, 0.1f);
                Vfx.Burst(at, StormFx.Gold, 8, 7f, 0.12f, 0.5f);
                CameraShake.Shake(0.14f, 0.12f);
                Hit(current, throwLightning);
                Jolt(current, 0.4f);
            }
            else
            {
                // bare: a hard ringing blow (the heavier hammer rings brighter)
                Vfx.Burst(at, new Color(1f, 0.9f, 0.7f), weight ? 16 : 10, 6f, 0.12f, 0.6f);
                Vfx.Flash(at, weight ? 2f : 1.4f, Color.white, 0.1f);
                Vfx.Slash(at, (at - lastAt).normalized, weight ? 2.2f : 1.6f, new Color(1f, 0.93f, 0.8f), Random.Range(-40f, 40f), 70f, 0.18f);
            }
            lastAt = at;
            if (struck.Count >= targets) break;
            current = NextTarget(current.transform.position, throwJump, struck);
        }

        // and back to the hand
        var hitOnReturn = new List<Enemy>(struck);
        while (hammer != null)
        {
            Vector3 hand = Hand;
            hammer.transform.position = Vector3.MoveTowards(Pos(), hand, throwSpeed * 1.15f * Time.deltaTime);
            spin += 1300f * Time.deltaTime; hammer.transform.rotation = Quaternion.AngleAxis(spin, transform.right);
            if (weight)
                foreach (Enemy e in Enemy.Alive.ToArray())
                    if (e != null && !e.IsDead && !hitOnReturn.Contains(e) && Vector3.Distance(Chest(e), Pos()) < 1.3f)
                    {
                        hitOnReturn.Add(e);
                        Hit(e, mult * 0.6f);
                        Vfx.Burst(Chest(e), master ? StormFx.Blue : new Color(1f, 0.9f, 0.7f), 8, 5f, 0.1f, 0.6f);
                    }
            Crackle(Pos(), master, weight);
            if (Vector3.Distance(Pos(), hand) < 0.5f) break;
            yield return null;
        }
        if (hammer != null) Destroy(hammer);
        CatchHammer();
        // caught: the lightning snaps back into the hand
        if (master)
        {
            Vfx.Flash(Hand, 1.8f, StormFx.Blue, 0.14f);
            Vfx.Burst(Hand, StormFx.Blue, 10, 4f, 0.1f, 0.3f);
            Vfx.GroundRing(FloorUnder(transform.position), 1.6f, StormFx.Blue, 0.3f);
        }
    }

    // The hammer in flight: a light-filled glow round its head and a trail; 묵직한 망치 adds a second, golden trail and
    // sparks, 뇌신의 망치 wraps it in crackling electricity (a fat blue trail, a gold strand and a halo of sparks)
    private static void DressHammer(GameObject hammer, bool master, bool weight)
    {
        var trail = new GameObject("Trail"); trail.transform.SetParent(hammer.transform, false);
        Vfx.Trail(trail, master ? StormFx.Blue : new Color(1f, 0.92f, 0.75f), master ? 1f : weight ? 0.5f : 0.35f, master ? 0.32f : 0.22f);
        if (master || weight)
        {
            var second = new GameObject("Trail2"); second.transform.SetParent(hammer.transform, false);
            second.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            Vfx.Trail(second, master ? StormFx.Gold : Color.white, master ? 0.35f : 0.2f, 0.18f);
            Vfx.Sparkle(hammer, master ? StormFx.Blue : new Color(1f, 0.9f, 0.7f), master ? 2.5f : 1.5f, master ? 0.09f : 0.07f);
        }
        Vfx.Glow(hammer.transform, Vector3.zero, master ? 1.6f : 1f, master ? StormFx.Blue : new Color(1f, 0.92f, 0.75f) * 0.6f, "glow", master ? 300f : 0f);
        if (master)
        {
            var l = new GameObject("HammerLight").AddComponent<Light>();
            l.transform.SetParent(hammer.transform, false);
            l.type = LightType.Point; l.color = StormFx.Blue; l.range = 5f; l.intensity = 4f; l.shadows = LightShadows.None;
        }
    }

    // Electricity snapping off the hammer as it flies; at 뇌신의 망치 it reaches out to enemies it passes near
    private static void Crackle(Vector3 at, bool master, bool weight)
    {
        // (rolled per second, not per frame, so it looks the same at any frame rate)
        float dt = Time.deltaTime;
        if (!master)
        {
            if (weight && Random.value < 12f * dt) Vfx.Burst(at, new Color(1f, 0.9f, 0.7f), 2, 3f, 0.07f, 0.2f);
            return;
        }
        if (Random.value < 30f * dt)
            Vfx.Bolt(at, at + Random.onUnitSphere * Random.Range(0.8f, 1.6f), Random.value < 0.2f ? StormFx.Gold : StormFx.Blue, 0.28f, 0.1f);
        if (Random.value < 6f * dt)
            foreach (Enemy e in Enemy.Alive)
                if (e != null && !e.IsDead && Vector3.Distance(Chest(e), at) < 3.2f)
                {
                    Vfx.Bolt(at, Chest(e), StormFx.Blue, 0.22f, 0.12f);
                    break;
                }
    }

    // ═══════════════ E: 번개 방출 (charged) ═══════════════

    private float ChargeCap => chargeTime + 0.5f * Rank(ECharge);
    private float ChargeRate => Has(EQuick) ? 1.3f : 1f;
    private int DischargeTargets => 1 + Rank(EChain) + (Has(EMaster) ? 2 : 0);
    private bool Electric => Rank(EChain) > 0 || Has(EMaster);
    private float ChargeLevel => Mathf.Clamp01((Time.time - chargeStart) * ChargeRate / ChargeCap);

    private void BeginCharge()
    {
        charging = true;
        chargeStart = Time.time;
        // 묠니르: the hammer goes up and stays raised while the lightning gathers
        var m = Mjolnir;
        if (m != null) m.ExternalHold("slam", MjolnirSlamRaise);
        Vector3 at = new Vector3(transform.position.x, Feet + 0.08f, transform.position.z);
        chargeGlow = Electric
            ? Vfx.Sprite("storm_impact", at, 1.2f, StormFx.Blue * 0.8f, 0f, 1f, 1f, 200f, Quaternion.Euler(90f, 0f, 0f))
            : Vfx.Sprite("glow", at, 1.2f, new Color(1f, 0.92f, 0.7f) * 0.7f, 0f, 1f, 1f, 0f, Quaternion.Euler(90f, 0f, 0f));
    }

    private void UpdateCharge()
    {
        float k = ChargeLevel;
        if (chargeGlow != null)
        {
            chargeGlow.transform.position = new Vector3(transform.position.x, Feet + 0.08f, transform.position.z);
            chargeGlow.transform.localScale = Vector3.one * Mathf.Lerp(1.2f, 4f, k);
        }
        // crackles round the body as it fills (the lightning only once the tree has given it)
        if (Electric && Random.value < 0.1f + k * 0.3f)
        {
            Vector3 c = transform.position + Vector3.up * 1f;
            Vfx.Bolt(c, c + Random.onUnitSphere * Mathf.Lerp(0.8f, 1.8f, k), Random.value < 0.2f ? StormFx.Gold : StormFx.Blue, 0.2f, 0.08f);
        }
    }

    // 묠니르's slam clip: the hammer is up at this many seconds in, and comes down soon after
    private const float MjolnirSlamRaise = 0.7f;

    private void CancelCharge()
    {
        bool was = charging;
        charging = false;
        if (chargeGlow != null) Destroy(chargeGlow.gameObject);
        var m = Mjolnir ?? (ArmoryWeapon.Current as MjolnirHammer);
        if (was && m != null) m.ExternalStop();
    }

    private void Discharge()
    {
        float k = ChargeLevel;
        CancelCharge();
        float mult = Mathf.Lerp(dischargeMin, dischargeMax * (1f + 0.25f * Rank(ECharge)), k) * (Has(EMaster) ? 1.4f : 1f);
        StartCoroutine(DischargeChain(mult, k));
    }

    private IEnumerator DischargeChain(float mult, float charge)
    {
        busy = true;
        // (묠니르: the raised hammer comes crashing down)
        Swing("slam", 2f, MjolnirSlamRaise, 0.6f);
        // from the nearest enemy outward
        Enemy current = null;
        float best = dischargeReach;
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead) continue;
            float d = Flat(e.transform.position - transform.position);
            if (d < best) { best = d; current = e; }
        }
        yield return new WaitForSeconds(0.12f);
        busy = false;
        if (current == null)
        {
            // nothing in reach: the charge bursts harmlessly round the player
            dischargeReadyAt = Time.time + 1f;
            if (Electric) StormFx.Impact(FloorUnder(transform.position), 3f, 0.3f);
            else Shockwave.Spawn(transform.position, 2f, new Color(1f, 0.92f, 0.7f), 8);
            yield break;
        }
        dischargeReadyAt = Time.time + dischargeCooldown * CD;
        bool master = Has(EMaster), electric = Electric;
        int targets = DischargeTargets;
        var struck = new List<Enemy>();
        Vector3 from = Hand;
        CameraShake.Shake(0.12f + charge * 0.2f, 0.1f + charge * 0.15f);
        while (current != null && struck.Count < targets)
        {
            struck.Add(current);
            Vector3 at = Chest(current);
            if (master)
            {
                // 천둥의 심판: great bolts out of the sky on each one, thick arcs between them
                StormFx.Arc(from, at, 1f);
                StormFx.Strike(FloorUnder(at), 11f, 2.6f);
                StormFx.Impact(FloorUnder(at), 4.2f, 0.5f);
            }
            else if (electric)
            {
                StormFx.Arc(from, at, 0.55f);
                StormFx.Light(at, 5f, 2.5f, 0.12f);
            }
            else
            {
                // bare: the blow lands as a burst of force, no lightning yet
                Vfx.Burst(at, new Color(1f, 0.92f, 0.7f), 14, 7f, 0.14f, 0.6f);
                Shockwave.Spawn(current.transform.position, 1.6f, new Color(1f, 0.92f, 0.7f), 8);
            }
            Hit(current, mult);
            Jolt(current, 0.3f + charge * 0.4f);
            from = at;
            if (struck.Count >= targets) break;
            current = NextTarget(current.transform.position, dischargeJump, struck);
            yield return new WaitForSeconds(0.06f);
        }
    }

    // ═══════════════ R: 휠윈드 ═══════════════

    private IEnumerator Whirl()
    {
        whirling = true;
        bool shock = Has(RShock), master = Has(RMaster), wide = Has(RLong);
        float radius = whirlRadius + (wide ? 0.6f : 0f);
        float time = whirlTime * (wide ? 1.3f : 1f);
        float mult = whirlDamage * (1f + 0.15f * Rank(RSpin));
        whirlReadyAt = Time.time + time + whirlCooldown * CD;
        var profile = GetComponentInChildren<CharacterProfile>();
        spinModel = profile != null ? profile.transform : null;
        if (spinModel != null) spinModelRest = spinModel.localRotation;
        // 묠니르: the hammer swings out at arm's length as he spins
        var mj = Mjolnir;
        if (mj != null) mj.ExternalSpin = true;
        // The hammer's head, swinging round at the end of the arm: it leaves a trail (white-gold bare, electric blue
        // once 감전 is learned) and it is the head that strikes — an enemy is hit as the head sweeps past it.
        Vector3 start = transform.forward; start.y = 0f;
        if (start.sqrMagnitude < 0.01f) start = Vector3.forward;
        start.Normalize();
        if (whirlHead != null) Destroy(whirlHead);
        whirlHead = new GameObject("WhirlHead");
        Vfx.Trail(whirlHead, shock ? StormFx.Blue : new Color(1f, 0.9f, 0.7f), master ? 0.75f : shock ? 0.55f : 0.4f, 0.18f);
        if (shock) Vfx.Sparkle(whirlHead, master ? StormFx.Gold : StormFx.Blue, 8f, 0.14f);
        Vector3 Head(float angle) => transform.position + Vector3.up * 0.15f + Quaternion.AngleAxis(angle, Vector3.up) * start * (radius - 0.3f);
        whirlHead.transform.position = Head(0f);

        var lastHit = new Dictionary<Enemy, float>();   // each enemy is struck at most once per pass of the head
        var struckBy = new Dictionary<Enemy, float>();  // R 마스터: when the sky last struck it
        var jolted = new List<Enemy>();                  // 감전: locked up once per whirl
        float spin = 0f;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            if (stats != null && stats.IsDead) break;
            float before = spin;
            spin += 900f * Time.deltaTime;
            if (spinModel != null) spinModel.localRotation = Quaternion.AngleAxis(spin, Vector3.up) * spinModelRest;
            whirlHead.transform.position = Head(spin);
            float swept = spin - before;
            // 묠니르: the real hammer's head leads the way — the trail rides on it, and the blows land where it sweeps
            if (mj != null && mj.HeldHammer != null && t > 0.12f)
            {
                Vector3 head = HammerHead(mj.HeldHammer);
                Vector3 hv = head - transform.position; hv.y = 0f;
                float offset = Mathf.DeltaAngle(Mathf.Repeat(spin, 360f), Vector3.SignedAngle(start, hv, Vector3.up));
                before += offset;
                whirlHead.transform.position = head;
                radius = Mathf.Max(radius, hv.magnitude + 0.8f);   // (as far as the hammer actually reaches)
            }

            foreach (Enemy e in Enemy.Alive.ToArray())
            {
                if (e == null || e.IsDead) continue;
                Vector3 to = e.transform.position - transform.position; to.y = 0f;
                if (to.magnitude > radius + 0.5f) continue;
                // did the head pass over it this frame?
                float at = Vector3.SignedAngle(start, to, Vector3.up);
                if (Mathf.Repeat(at - before, 360f) > swept && to.magnitude > 1.1f) continue;   // (right beside the player always counts)
                if (lastHit.TryGetValue(e, out float when) && Time.time - when < whirlTick) continue;
                lastHit[e] = Time.time;
                StrikeWithHead(e, mult, shock, master, jolted, struckBy);
            }
            yield return null;
        }
        EndWhirl();
        if (master) StormFx.Impact(FloorUnder(transform.position), radius * 2f, 0.4f);
    }

    // the far end of a held hammer from the player (its head), read off its mesh
    private Vector3 HammerHead(Transform hammer)
    {
        Vector3 best = hammer.position; float far = -1f;
        foreach (var mf in hammer.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = mf.transform.TransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                float d = Flat(c - transform.position);
                if (d > far) { far = d; best = c; }
            }
        }
        // (a little in from the very corner: the middle of the head)
        return Vector3.Lerp(hammer.position, best, 0.85f);
    }

    // The hammer's head lands on an enemy: bare, a heavy blow; 감전, it crackles and locks up; R 마스터, the sky answers —
    // a bolt falls on it and the lightning runs out over the ground, striking everything near it
    private void StrikeWithHead(Enemy e, float mult, bool shock, bool master, List<Enemy> jolted, Dictionary<Enemy, float> struckBy)
    {
        Vector3 c = Chest(e);
        Hit(e, mult);
        CameraShake.Shake(0.06f, 0.05f);
        if (!shock)
        {
            Vfx.Burst(c, new Color(1f, 0.9f, 0.7f), 8, 5f, 0.1f, 0.6f);
            return;
        }
        // 감전: electricity crawls over it
        for (int i = 0; i < 2; i++)
            Vfx.Bolt(c + Random.onUnitSphere * 0.9f, c + Random.insideUnitSphere * 0.2f, Random.value < 0.2f ? StormFx.Gold : StormFx.Blue, 0.2f, 0.12f);
        Vfx.Flash(c, 1.2f, StormFx.Blue, 0.1f);
        if (!jolted.Contains(e)) { jolted.Add(e); Jolt(e, 0.35f); }
        if (!master || (struckBy.TryGetValue(e, out float last) && Time.time - last < 0.7f))
            return;

        // R 마스터: a bolt from the sky, then the lightning breaks out of it sideways (like a lightning-strike spear):
        // jagged forks lash out from the struck enemy, leaping enemy to enemy
        struckBy[e] = Time.time;
        Vector3 ground = FloorUnder(e.transform.position);
        StormFx.Strike(ground, 9f, 2f);
        StormFx.Impact(ground, 2.4f, 0.3f);
        Hit(e, stormDamage);
        StartCoroutine(StrikeForks(e));
        CameraShake.Shake(0.12f, 0.1f);
    }

    // The forks: from the struck enemy a few bolts lash out at once to the nearest enemies round it, and from each of
    // those it leaps on once more — every leap a crackling sideways arc, a splash of damage where it lands
    private IEnumerator StrikeForks(Enemy origin)
    {
        var struck = new List<Enemy> { origin };
        var wave = new List<Enemy> { origin };
        Vector3 originChest = Chest(origin);
        for (int hop = 0; hop < forkHops && wave.Count > 0; hop++)
        {
            var next = new List<Enemy>();
            foreach (Enemy from in wave)
            {
                Vector3 a = from != null ? Chest(from) : originChest;
                // the first burst fans out to several at once; later leaps go on one at a time
                int fan = hop == 0 ? forkCount : 1;
                for (int i = 0; i < fan; i++)
                {
                    Enemy to = NextTarget(a, splashRadius + 1.5f, struck);
                    if (to == null)
                    {
                        // nobody there: the fork still lashes out at the air and fizzles
                        if (hop == 0)
                        {
                            Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                            StormFx.Fork(a, a + dir * Random.Range(1.8f, 2.8f) + Vector3.down * 0.4f, 0.5f);
                        }
                        continue;
                    }
                    struck.Add(to);
                    next.Add(to);
                    StormFx.Fork(a, Chest(to), 0.75f);
                    Hit(to, splashDamage);
                    Jolt(to, 0.25f);
                }
            }
            wave = next;
            yield return new WaitForSeconds(0.07f);
        }
    }

    private void EndWhirl()
    {
        whirling = false;
        if (whirlHead != null) Destroy(whirlHead);
        if (whirlRing != null) Destroy(whirlRing.gameObject);
        if (spinModel != null) spinModel.localRotation = spinModelRest;
        spinModel = null;
        if (ArmoryWeapon.Current is MjolnirHammer m) m.ExternalSpin = false;
    }

    // ── The charge gauge over the player's head while E is held ──
    private GUIStyle gaugeStyle;

    private void OnGUI()
    {
        if (!charging || Camera.main == null) return;
        Vector3 sp = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 2.4f);
        if (sp.z <= 0f) return;
        float k = ChargeLevel;
        var r = new Rect(sp.x - 40f, Screen.height - sp.y, 80f, 9f);
        UiFont.Fill(r, new Color(0f, 0f, 0f, 0.7f));
        UiFont.Fill(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * k, r.height - 2f), k >= 1f ? StormFx.Gold : StormFx.Blue);
        if (k >= 1f)
        {
            gaugeStyle ??= UiFont.Style(11, TextAnchor.MiddleCenter, FontStyle.Bold);
            GUI.Label(new Rect(r.x - 20f, r.y - 16f, r.width + 40f, 14f), "최대 충전!", gaugeStyle);
        }
    }

    // A new run or a new character: nothing carries over
    public void ResetCombat()
    {
        StopAllCoroutines();
        EndWhirl();
        CancelCharge();
        busy = false;
        throwReadyAt = dischargeReadyAt = whirlReadyAt = 0f;
        foreach (var h in GameObject.FindObjectsByType<Transform>())
            if (h != null && h.name == "ThrownHammer") Destroy(h.gameObject);
        CatchHammer();
    }

    private void OnDisable()
    {
        EndWhirl();
        CancelCharge();
    }
}
