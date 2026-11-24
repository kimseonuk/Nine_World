using static SkillTree;

// The twin blades' skill tree (see SkillTree). Everything the blades can do is learned here: the combo starts as a
// single cut and gains its 2nd to 5th blows, and the five skills (회피, 난무, 처형, 광폭화, 돌진) are locked until learned.
// The trunk holds the basics (2타, 회피, training); above it the tree splits into 공격, 생존 and 이속.
public static class DualBladeSkills
{
    // The combo and the skills
    public const string Combo2 = "combo2";          // 연속 베기 2타 (the foot of the trunk)
    public const string Combo3 = "combo3";
    public const string Combo4 = "combo4";
    public const string Combo5 = "combo5";
    public const string Flurry = "flurry";          // 난무 [Q]
    public const string Execute = "execute";        // 처형 [right-click]
    public const string Berserk = "berserk";        // 광폭화 [R]
    public const string Dodge = "dodge";            // 회피 [Space]
    public const string Dash = "dash";              // 돌진 [E]

    // Upgrades to them
    public const string Mastery = "mastery";        // 칼날 숙련: faster combo, the spins cut harder
    public const string Bloodlust = "bloodlust";    // 피의 축제: longer 광폭화, kills during it heal
    public const string Recall = "recall";          // 그림자 회수: every dodge gives back part of the other skills' cooldowns
    public const string Gale = "gale";              // 질풍: longer 돌진, a hit refunds more of its cooldown
    public const string Headsman = "headsman";      // 처형인: 처형 finishes enemies at higher health
    public const string Tempest = "tempest";        // 칼날 폭풍: longer, wider 난무
    public const string Shadowstep = "shadowstep";  // 그림자 걸음: quicker, longer dodge

    private const Branch T = Branch.Trunk, A = Branch.Attack, S = Branch.Survival, M = Branch.Speed;

    private static readonly Node[] NodeList =
    {
        // ── The trunk: the basics ──
        ComboBlow(Combo2, "쌍검술", "연속 베기에 2타(교차 베기)가 이어집니다", null, T, 0, 0f, "2타"),
        Skill(Dodge, "회피 [Space]", "무적 상태로 굴러 빠지고, 선 자리에 그림자 분신이 남아 베어 냅니다", Combo2, T, 1, 0f, "dual_dodge"),
        Stat("basics", "기초 단련", "최대 체력 +5, 공격력 +2%", Dodge, T, 2, 0f, "기", 3, (s, r) => { s.maxHealthBonus += 5f * r; s.damageMultiplier *= 1f + 0.02f * r; }),
        Stat("breath_in", "호흡", "이동 속도 +2%, 공격 속도 +2%", "basics", T, 3, 0f, "호", 2, (s, r) => { s.moveSpeedMultiplier *= 1f + 0.02f * r; s.attackSpeedMultiplier *= 1f + 0.02f * r; }),

        // ── 공격 ──
        Stat("edge", "날 세우기", "공격력 +4%", "breath_in", A, 4, -0.18f, "공", 3, (s, r) => s.damageMultiplier *= 1f + 0.04f * r),
        ComboBlow(Combo3, "찌르기 연타", "연속 베기에 3타(세 번 찌르기)가 이어집니다", "edge", A, 5, -0.28f, "3타"),
        Skill(Flurry, "난무 [Q]", "잠시 멈춰 서서 앞쪽에 칼날 폭풍을 몰아칩니다", Combo3, A, 6, -0.44f, "dual_flurry"),
        Stat("vital", "급소 노리기", "치명타 확률 +3%", Combo3, A, 6, -0.2f, "치", 3, (s, r) => s.critChance += 0.03f * r),
        Upgrade(Tempest, "칼날 폭풍", "난무 시간 +0.8초, 범위 +1m", Flurry, A, 7, -0.58f, "폭"),
        Stat("gust", "칼바람", "공격 속도 +3%", Flurry, A, 7, -0.4f, "풍", 2, (s, r) => s.attackSpeedMultiplier *= 1f + 0.03f * r),
        ComboBlow(Combo4, "회전 베기", "연속 베기에 4타(한 바퀴 회전 베기)가 이어집니다", "vital", A, 7, -0.22f, "4타"),
        Skill(Execute, "처형 [우클릭]", "꾹 눌러 모은 뒤 핏빛 검기로 체력이 낮은 적을 즉사시킵니다", Tempest, A, 8, -0.66f, "dual_execute"),
        Stat("slayer", "학살자", "치명타 피해 +15%", "gust", A, 8, -0.48f, "학", 2, (s, r) => s.critMultiplier += 0.15f * r),
        Stat("hone", "칼날 연마", "공격력 +5%, 치명타 피해 +10%", Combo4, A, 8, -0.3f, "연", 3, (s, r) => { s.damageMultiplier *= 1f + 0.05f * r; s.critMultiplier += 0.1f * r; }),
        Upgrade(Headsman, "처형인", "처형 기준 체력 30% → 40% (보스 8% → 12%)", Execute, A, 9, -0.74f, "인"),
        Stat("thunder", "번개 칼날", "공격이 주변 적 1명에게 번개로 튑니다", "slayer", A, 9, -0.56f, "뇌", 1, (s, r) => s.chainTargets += r),
        ComboBlow(Combo5, "도약 찍기", "연속 베기에 마지막 5타(뛰어올라 내려찍기)가 이어집니다", "hone", A, 9, -0.38f, "5타"),
        Stat("warmonger", "전쟁광", "공격력 +6%", Headsman, A, 10, -0.64f, "광", 3, (s, r) => s.damageMultiplier *= 1f + 0.06f * r),
        Upgrade(Mastery, "칼날 숙련", "연속 베기 공격 속도 +10%, 회전·도약 베기 피해 +20%", Combo5, A, 10, -0.44f, "숙"),

        // ── 생존 ──
        Stat("sturdy", "강인함", "최대 체력 +8", "breath_in", S, 4, 0f, "체", 3, (s, r) => s.maxHealthBonus += 8f * r),
        Skill(Berserk, "광폭화 [R]", "잠시 동안 공격력·공격 속도·이동 속도가 크게 오릅니다", "sturdy", S, 5, 0f, "dual_berserk"),
        Stat("hardened", "단련된 몸", "받는 피해 -3%", Berserk, S, 6, -0.07f, "방", 3, (s, r) => s.damageTakenMultiplier *= 1f - 0.03f * r),
        Stat("thirst", "피의 갈증", "적 처치 시 HP 1 회복", Berserk, S, 6, 0.07f, "흡", 3, (s, r) => s.healOnKill += 1f * r),
        Stat("giant", "거인의 심장", "최대 체력 +12", "hardened", S, 7, -0.08f, "심", 3, (s, r) => s.maxHealthBonus += 12f * r),
        Upgrade(Bloodlust, "피의 축제", "광폭화 지속 +3초, 광폭화 중 처치하면 체력 4% 회복", "thirst", S, 7, 0.08f, "축"),
        Stat("thorns", "가시 갑옷", "나를 때린 적에게 피해 3 반사", "giant", S, 8, -0.1f, "가", 3, (s, r) => s.thornsDamage += 3f * r),
        Stat("breath", "전장의 숨결", "방을 정리할 때마다 HP 6 회복", Bloodlust, S, 8, 0.1f, "숨", 2, (s, r) => s.healOnRoomClear += 6f * r),
        Stat("veil", "그림자 장막", "전투마다 첫 피격 1회 무효", "thorns", S, 9, -0.08f, "막", 1, (s, r) => s.wardsPerRoom += r),
        Stat("tenacity", "질긴 생명", "최대 체력 +15", "breath", S, 9, 0.08f, "생", 2, (s, r) => s.maxHealthBonus += 15f * r),
        Stat("undying", "불굴", "최대 체력 +20, 받는 피해 -8%", "veil", S, 10, 0f, "굴", 1, (s, r) => { s.maxHealthBonus += 20f * r; s.damageTakenMultiplier *= 1f - 0.08f * r; }),

        // ── 이속 ──
        Stat("light", "가벼운 발걸음", "이동 속도 +3%", "breath_in", M, 4, 0.18f, "이", 3, (s, r) => s.moveSpeedMultiplier *= 1f + 0.03f * r),
        Skill(Dash, "돌진 [E]", "앞으로 돌진하며 길 위의 적을 모두 벱니다. 적중하면 재사용 시간 일부 회수", "light", M, 5, 0.28f, "dual_dash"),
        Stat("quick", "빠른 손", "공격 속도 +3%", Dash, M, 6, 0.2f, "속", 3, (s, r) => s.attackSpeedMultiplier *= 1f + 0.03f * r),
        Upgrade(Gale, "질풍", "돌진 거리 +40%, 적중 시 재사용 시간 65% 회수 (기본 50%)", Dash, M, 6, 0.44f, "질"),
        Upgrade(Recall, "그림자 회수", "회피할 때마다 다른 기술의 남은 재사용 시간 15% 회수", "quick", M, 7, 0.22f, "회"),
        Stat("afterimage", "잔상", "피해 회피 확률 +4%", Gale, M, 7, 0.4f, "잔", 3, (s, r) => s.dodgeChance += 0.04f * r),
        Stat("goldhand", "황금 손", "얻는 황금 +15%", Gale, M, 7, 0.58f, "금", 2, (s, r) => s.goldMultiplier *= 1f + 0.15f * r),
        Stat("windstep", "바람걸음", "이동 속도 +5%, 공격 속도 +2%", Recall, M, 8, 0.3f, "풍", 2, (s, r) => { s.moveSpeedMultiplier *= 1f + 0.05f * r; s.attackSpeedMultiplier *= 1f + 0.02f * r; }),
        Upgrade(Shadowstep, "그림자 걸음", "회피 재사용 시간 -30%, 회피 거리 +25%", "afterimage", M, 8, 0.48f, "걸"),
        Stat("hunter", "사냥꾼", "치명타 확률 +2%", "goldhand", M, 8, 0.66f, "냥", 2, (s, r) => s.critChance += 0.02f * r),
        Stat("sprint", "질주", "이동 속도 +5%", "windstep", M, 9, 0.38f, "주", 2, (s, r) => s.moveSpeedMultiplier *= 1f + 0.05f * r),
        Stat("evasion", "회피의 달인", "피해 회피 확률 +3%", Shadowstep, M, 9, 0.56f, "달", 2, (s, r) => s.dodgeChance += 0.03f * r),
        Stat("swift_tip", "날랜 칼끝", "공격 속도 +4%", "hunter", M, 9, 0.74f, "끝", 2, (s, r) => s.attackSpeedMultiplier *= 1f + 0.04f * r),
        Stat("windlord", "바람의 화신", "공격 속도 +6%, 이동 속도 +6%", "sprint", M, 10, 0.46f, "화", 1, (s, r) => { s.attackSpeedMultiplier *= 1f + 0.06f * r; s.moveSpeedMultiplier *= 1f + 0.06f * r; }),
        Stat("shadowlord", "그림자 군주", "피해 회피 확률 +5%, 이동 속도 +5%", "evasion", M, 10, 0.64f, "군", 1, (s, r) => { s.dodgeChance += 0.05f * r; s.moveSpeedMultiplier *= 1f + 0.05f * r; }),
    };

    public static readonly SkillTree Tree = new SkillTree("쌍검 스킬트리", NodeList, Combo2, Combo3, Combo4, Combo5);

    public static int Points => Tree.Points;
    public static bool Has(string id) => Tree.Has(id);
    public static int ComboLength => Tree.ComboLength;
    public static bool Active { get => Tree.Active; set => Tree.Active = value; }
    public static void Reset(int startingPoints) => Tree.Reset(startingPoints);
    public static void AddPoint() => Tree.AddPoint();
    public static void ApplyStats(RunStats stats) => Tree.ApplyStats(stats);
}
