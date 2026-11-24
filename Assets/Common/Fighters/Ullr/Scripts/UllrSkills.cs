using static SkillTree;

// 울르의 사냥꾼's skill tree (울르의 활), laid out like the spear-keeper's: a trunk of basics, then three branches —
// 사격 (the combo and the charged shot, left), 사냥 기술 (Q, E and R, centre) and 사냥꾼 (the hunter's passives and the
// mark, right). At the start he has only his first shot and the game's dash; the combo's later shots and every skill
// are learned here, each skill with an upgrade above it. At the very top: 울르의 활 awakens.
public static class UllrSkills
{
    // trunk
    public const string Combo2 = "u_combo2";        // 2타 연속 사격
    public const string Charge = "u_charge";        // 예리한 화살촉 (was 정조준 사격, the hold-to-draw, taken out)
    // 사격
    public const string Combo3 = "u_combo3";        // 3타 삼연 확산
    public const string Combo4 = "u_combo4";        // 4타 바람 관통시
    public const string Combo5 = "u_combo5";        // 5타 울르의 일격
    public const string ChargeUp = "u_chargeUp";    // 얼음 화살촉: the 4th shot bursts in frost where it stops
    public const string Volley = "u_volley";        // 다발 사격: the 3rd shot looses five arrows
    // 사냥 기술
    public const string Roll = "u_roll";            // 바람의 서약 [Q]
    public const string RollUp = "u_rollUp";
    public const string Trap = "u_trap";            // 울르의 돌풍함정 [E]
    public const string TrapUp = "u_trapUp";
    public const string Rain = "u_rain";            // 바람의 추적 화살 [R]
    public const string RainUp = "u_rainUp";
    public const string SkillMastery = "u_skillMastery";
    // 사냥꾼
    public const string Mark = "u_mark";            // 바람의 매 [우클릭] (was 울르의 늑대, 사냥꾼의 표식; the id kept so saves still hold it)
    public const string MarkUp = "u_markUp";
    // the crown
    public const string Awaken = "u_awaken";        // ★ 울르의 활 각성

    // A skill or upgrade with a short label in its medallion (no painted icon yet)
    private static Node Active(string id, string name, string effect, string parent, Branch branch, int row, float x, string glyph, string icon = null) =>
        new Node { id = id, name = name, effect = effect, parent = parent, branch = branch, kind = Kind.Skill, row = row, x = x, glyph = glyph,
                   icon = icon != null ? "UllrIcons/" + icon : null };

    private static Node Passive(string id, string name, string effect, string parent, Branch branch, int row, float x, string glyph) =>
        new Node { id = id, name = name, effect = effect, parent = parent, branch = branch, kind = Kind.Upgrade, row = row, x = x, glyph = glyph };

    private const Branch T = Branch.Trunk, A = Branch.Attack, S = Branch.Survival, M = Branch.Speed;

    private static readonly Node[] NodeList =
    {
        // ── 기본기 (trunk) ──
        ComboBlow(Combo2, "연속 사격", "연속 공격에 2타(한쪽 무릎을 꿇고 빠르게 두 발)가 이어집니다", null, T, 0, 0f, "2타"),
        Passive(Charge, "예리한 화살촉", "평타 화살 피해 +10% · 버튼을 누르고 있으면 연속 사격이 계속 이어집니다", Combo2, T, 1, 0f, "촉"),
        Stat("u_basics", "사냥 기초", "최대 체력 +5, 공격력 +2%", Charge, T, 2, 0f, "기", 3,
            (s, r) => { s.maxHealthBonus += 5f * r; s.damageMultiplier *= 1f + 0.02f * r; }),
        Stat("u_feet", "설원의 발걸음", "이동 속도 +3%", "u_basics", T, 3, 0f, "발", 2, (s, r) => s.moveSpeedMultiplier *= 1f + 0.03f * r),

        // ── 사격 (left) ──
        Stat("u_string", "팽팽한 시위", "공격력 +4%", "u_feet", A, 4, -0.2f, "시", 3, (s, r) => s.damageMultiplier *= 1f + 0.04f * r),
        ComboBlow(Combo3, "삼연 확산", "연속 공격에 3타(몸을 틀며 뒤로 뛰어 화살 세 발을 부채꼴로)가 이어집니다", "u_string", A, 5, -0.24f, "3타"),
        Stat("u_vital", "급소 사격", "치명타 확률 +3%", Combo3, A, 6, -0.4f, "급", 3, (s, r) => s.critChance += 0.03f * r),
        ComboBlow(Combo4, "바람 관통시", "연속 공격에 4타(반걸음 물러서며 활을 끝까지 당겨 적을 모두 꿰뚫는 화살)가 이어집니다", Combo3, A, 7, -0.24f, "4타"),
        Passive(Volley, "다발 사격", "3타가 화살 다섯 발을 넓게 퍼뜨립니다", Combo3, A, 7, -0.52f, "다발"),
        ComboBlow(Combo5, "울르의 일격", "연속 공격에 5타(하늘로 쏜 큰 화살이 떨어진 자리에 회오리바람이 터져 적을 넘어뜨림)가 이어집니다", Combo4, A, 8, -0.24f, "5타"),
        Stat("u_reach", "긴 사거리", "공격 범위 +0.2m", Volley, A, 8, -0.5f, "사", 2, (s, r) => s.rangeBonus += 0.2f * r),
        Passive(ChargeUp, "돌풍 화살촉", "4타 바람 관통시가 멈춘 자리에서 돌풍이 터집니다 (주변 피해 150%)", Combo5, A, 10, -0.26f, "촉"),
        Stat("u_quick", "빠른 손", "공격 속도 +5%", "u_reach", A, 10, -0.5f, "손", 2, (s, r) => s.attackSpeedMultiplier *= 1f + 0.05f * r),
        Stat("u_deadeye", "백발백중", "공격력 +8%, 치명타 피해 +20%", ChargeUp, A, 12, -0.34f, "백", 1,
            (s, r) => { s.damageMultiplier *= 1f + 0.08f * r; s.critMultiplier += 0.2f * r; }),

        // ── 사냥 기술 (centre) ──
        Stat("u_body", "단련된 몸", "최대 체력 +8", "u_feet", S, 4, 0f, "체", 3, (s, r) => s.maxHealthBonus += 8f * r),
        Active(Roll, "바람의 서약", "[Q] 바람을 타고 커서 방향으로 굴러 나가며(구르는 동안 무적) 일어나서 커서 쪽으로 바람 화살 다섯 발을 부채꼴로 쏩니다 (7초)", "u_body", S, 5, 0f, "Q", "Roll"),
        Passive(RollUp, "질풍의 서약", "바람의 서약 화살 5 → 8발, 구르는 거리 +30%", Roll, S, 7, -0.1f, "질풍"),
        Active(Trap, "울르의 돌풍함정", "[E] 뒤로 물러나며 발밑에 돌풍함정을 놓습니다. 적이 밟으면 회오리바람이 터져 날려 넘어뜨립니다 (10초)", Roll, S, 7, 0.1f, "E", "Trap"),
        Stat("u_guard", "설원의 감각", "받는 피해 -3%", RollUp, S, 8, -0.1f, "감", 3, (s, r) => s.damageTakenMultiplier *= 1f - 0.03f * r),
        Passive(TrapUp, "돌풍함정 사냥꾼", "돌풍함정을 두 개까지 깔 수 있고, 터지는 범위 +40%", Trap, S, 8, 0.1f, "덫"),
        Active(Rain, "바람의 추적 화살", "[R] 하늘로 바람 화살 15발을 흩뿌리면, 화살이 공중에서 방향을 틀어 주변 16m의 몬스터를 유도 미사일처럼 쫓아가 꽂힙니다 (커서에 가까운 몬스터부터, 발당 피해 75%) (18초)", TrapUp, S, 10, 0f, "R", "Rain"),
        Passive(RainUp, "추적 강화", "추적 화살 15 → 24발, 꽂힌 자리에서 돌풍이 터져 주변 1.2m 적에게도 피해", Rain, S, 11, 0.08f, "추"),
        Stat(SkillMastery, "기술 마스터리", "Q · E · R · 우클릭 쿨타임 -20%", Rain, S, 11, -0.1f, "기", 1, (s, r) => { }),

        // ── 사냥꾼 (right) ──
        Stat("u_eye", "늑대의 눈", "치명타 피해 +15%", "u_feet", M, 4, 0.22f, "눈", 3, (s, r) => s.critMultiplier += 0.15f * r),
        Active(Mark, "바람의 매", "[우클릭] 화살이 바람의 매가 되어 커서 위로 날아가 5초 동안 맴돌며, 0.8초마다 아래의 몬스터에게 내리꽂힙니다 (피해 85%) · 맞은 적에게 사냥꾼의 표식(6초 동안 울르의 화살에 30% 더 아프게 맞음) (9초)", "u_eye", M, 5, 0.3f, "매", "Hawk"),
        Stat("u_wind", "바람 읽기", "치명타 확률 +2%", Mark, M, 6, 0.42f, "풍", 2, (s, r) => s.critChance += 0.02f * r),
        Passive(MarkUp, "사냥매", "바람의 매가 8초 동안 머물고 0.5초마다 내리꽂힙니다 (피해 110%) · 표식 추가 피해 30% → 50%, 표식 시간 6 → 9초", Mark, M, 7, 0.3f, "사냥"),
        Stat("u_skadi", "스카디의 가호", "피해 회피 확률 +4%", MarkUp, M, 8, 0.5f, "가", 2, (s, r) => s.dodgeChance += 0.04f * r),
        Stat("u_hunt", "사냥의 수확", "적 처치 시 HP 1 회복", MarkUp, M, 10, 0.34f, "수", 3, (s, r) => s.healOnKill += 1f * r),
        Stat("u_gold", "사냥꾼의 몫", "치명타 확률 +5%, 얻는 황금 +15%", "u_hunt", M, 11, 0.46f, "몫", 1,
            (s, r) => { s.critChance += 0.05f * r; s.goldMultiplier *= 1f + 0.15f * r; }),
        Stat("u_winter", "겨울의 숨결", "방을 정리할 때마다 HP 6 회복", "u_gold", M, 13, 0.5f, "숨", 2, (s, r) => s.healOnRoomClear += 6f * r),

        // ── ★ 울르의 활 (the crown) ──
        new Node
        {
            id = Awaken, name = "울르의 활", kind = Kind.Skill, branch = S, row = 13, x = 0f, parent = RainUp, glyph = "울르",
            effect = "최종 · 활이 겨울의 신 울르의 활로 각성합니다. 모든 공격 피해 +25%, 치명타 확률 +8%. " +
                     "평타 화살이 모두 관통하고, 5타의 회오리바람이 두 배로 커집니다",
            stats = (s, r) => { s.damageMultiplier *= 1.25f; s.critChance += 0.08f; },
        },
    };

    public static readonly SkillTree Tree = new SkillTree("울르의 활", NodeList, Combo2, Combo3, Combo4, Combo5)
    {
        BranchLabels = new[] { "사격", "사냥 기술", "사냥꾼" },
        TopRow = 13,
        OpenRule = row => row <= TrunkRows ? 0 : 2 * (row - TrunkRows),
    };

    public static bool Has(string id) => Tree.Has(id);
    public static int ComboLength => Tree.ComboLength;
    public static float CooldownFactor => Has(SkillMastery) ? 0.8f : 1f;
    public static bool Awakened => Has(Awaken);
}
