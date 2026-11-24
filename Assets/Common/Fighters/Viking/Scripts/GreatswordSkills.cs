using static SkillTree;

// The greatsword's skill tree (see SkillTree). Everything the blade can do is learned here: the tap combo starts as a
// single sweep and gains its 2nd and 3rd blows, and its skills (대시, 검기, 차징 강타, 휠윈드, 도약 강타) are locked
// until learned, as are the upgrades to the charged slam (강타, 낙하, 연속 강타, 단절, 결의, 돌진).
// The trunk holds the basics (2타, 대시, training); above it the tree splits into 공격, 생존 and 이속.
public static class GreatswordSkills
{
    // The combo and the skills
    public const string Combo2 = "gs_combo2";     // 연속 베기 2타 (the foot of the trunk)
    public const string Combo3 = "gs_combo3";     // 연속 베기 3타: the overhead finisher
    public const string Wave = "gs_wave";         // 검기 [E]
    public const string Charge = "gs_charge";     // 차징 강타 [right button, hold]
    public const string Whirlwind = "gs_whirl";   // 휠윈드 [Q]
    public const string Dash = "gs_dash";         // 대시 [Space]
    public const string Leap = "gs_leap";         // 도약 강타 [R]

    // Upgrades
    public const string Smash = "smash";          // 강타: charged slams send out a shockwave
    public const string Plunge = "plunge";        // 낙하: full charge adds a second shockwave and more damage
    public const string Rush = "rush";            // 돌진: dash in a straight line while charging
    public const string Combo = "combo";          // 연속 강타: a second slam right after the first
    public const string Sever = "sever";          // 단절: a full-charge slam blocks and breaks enemy attacks
    public const string Resolve = "resolve";      // 결의: light hits don't break the charge
    public const string Rift = "gs_rift";         // 대지 가르기: a longer, harder 검기
    public const string Bloodspin = "gs_bloodspin"; // 피의 회오리: a wider, longer 휠윈드 that heals as it bites
    public const string Skyfall = "gs_skyfall";   // 하늘 가르기: a further leap with a wider landing
    public const string Tempo = "gs_tempo";       // 질풍의 검: shorter cooldowns on Q, E, R and the charge

    private const Branch T = Branch.Trunk, A = Branch.Attack, S = Branch.Survival, M = Branch.Speed;

    private static readonly Node[] NodeList =
    {
        // ── The trunk: the basics ──
        ComboBlow(Combo2, "대검술", "연속 베기에 2타(왕복 베기)가 이어집니다", null, T, 0, 0f, "2타"),
        Skill(Dash, "대시 [Space]", "짧게 앞으로 박차고 나갑니다", Combo2, T, 1, 0f, "gs_rush"),
        Stat("gs_basics", "기초 단련", "최대 체력 +5, 공격력 +2%", Dash, T, 2, 0f, "기", 3, (s, r) => { s.maxHealthBonus += 5f * r; s.damageMultiplier *= 1f + 0.02f * r; }),
        Stat("gs_breath_in", "호흡", "이동 속도 +2%, 공격 속도 +2%", "gs_basics", T, 3, 0f, "호", 2, (s, r) => { s.moveSpeedMultiplier *= 1f + 0.02f * r; s.attackSpeedMultiplier *= 1f + 0.02f * r; }),

        // ── 공격 ──
        Stat("gs_heft", "무게 싣기", "공격력 +4%", "gs_breath_in", A, 4, -0.18f, "공", 3, (s, r) => s.damageMultiplier *= 1f + 0.04f * r),
        ComboBlow(Combo3, "내려찍기", "연속 베기에 마지막 3타(머리 위로 내려찍기, 작은 충격파)가 이어집니다", "gs_heft", A, 5, -0.28f, "3타"),
        Skill(Wave, "검기 [E]", "크게 베어 바닥을 따라 나아가는 검기를 날립니다. 길 위의 적을 모두 벱니다", Combo3, A, 6, -0.44f, "gs_wave"),
        Skill(Charge, "차징 강타 [우클릭]", "꾹 눌러 대검을 들어 올려 2단계까지 모은 뒤, 놓으면 내려찍습니다", Combo3, A, 6, -0.2f, "gs_charge"),
        Upgrade(Rift, "대지 가르기", "검기 사거리 +30%, 피해 +30%", Wave, A, 7, -0.58f, "가"),
        Stat("gs_reach", "긴 칼날", "공격 범위 +0.15m", Wave, A, 7, -0.4f, "범", 2, (s, r) => s.rangeBonus += 0.15f * r),
        Upgrade(Smash, "강타", "차징 후 내려치면 주변에 충격파가 퍼집니다", Charge, A, 7, -0.22f, "강"),
        Stat("gs_hone", "칼날 연마", "공격력 +5%, 치명타 피해 +10%", Rift, A, 8, -0.66f, "연", 3, (s, r) => { s.damageMultiplier *= 1f + 0.05f * r; s.critMultiplier += 0.1f * r; }),
        Stat("gs_slayer", "학살자", "치명타 피해 +15%", "gs_reach", A, 8, -0.48f, "학", 2, (s, r) => s.critMultiplier += 0.15f * r),
        Upgrade(Plunge, "낙하", "최대 차징 시 추가 충격파와 공격력 상승, 적을 넘어뜨립니다", Smash, A, 8, -0.3f, "낙"),
        Stat("gs_crush", "파쇄", "치명타 확률 +3%", "gs_hone", A, 9, -0.74f, "치", 3, (s, r) => s.critChance += 0.03f * r),
        Stat("gs_thunder", "번개 칼날", "공격이 주변 적 1명에게 번개로 튑니다", "gs_slayer", A, 9, -0.56f, "뇌", 1, (s, r) => s.chainTargets += r),
        Upgrade(Combo, "연속 강타", "강타 뒤 한 번 더 내려찍습니다 (피해 70%)", Plunge, A, 9, -0.38f, "연"),
        Stat("gs_warmonger", "전쟁광", "공격력 +6%", "gs_crush", A, 10, -0.64f, "광", 3, (s, r) => s.damageMultiplier *= 1f + 0.06f * r),
        Upgrade(Sever, "단절", "최대 차징 공격 중 피해를 막고, 맞은 적의 공격을 끊습니다", Combo, A, 10, -0.44f, "단"),

        // ── 생존 ──
        Stat("gs_sturdy", "강인함", "최대 체력 +8", "gs_breath_in", S, 4, 0f, "체", 3, (s, r) => s.maxHealthBonus += 8f * r),
        Skill(Whirlwind, "휠윈드 [Q]", "대검을 휘두르며 회전해 주변을 계속 벱니다. 회전 중에도 천천히 걸을 수 있습니다", "gs_sturdy", S, 5, 0f, "gs_whirlwind"),
        Stat("gs_hardened", "단련된 몸", "받는 피해 -3%", Whirlwind, S, 6, -0.07f, "방", 3, (s, r) => s.damageTakenMultiplier *= 1f - 0.03f * r),
        Stat("gs_thirst", "피의 갈증", "적 처치 시 HP 1 회복", Whirlwind, S, 6, 0.07f, "흡", 3, (s, r) => s.healOnKill += 1f * r),
        Stat("gs_giant", "거인의 심장", "최대 체력 +12", "gs_hardened", S, 7, -0.08f, "심", 3, (s, r) => s.maxHealthBonus += 12f * r),
        Upgrade(Bloodspin, "피의 회오리", "휠윈드 범위 +0.8m, 시간 +25%, 적을 벨 때마다 HP 1 회복", "gs_thirst", S, 7, 0.08f, "회"),
        Stat("gs_thorns", "가시 갑옷", "나를 때린 적에게 피해 3 반사", "gs_giant", S, 8, -0.1f, "가", 3, (s, r) => s.thornsDamage += 3f * r),
        Stat("gs_breath", "전장의 숨결", "방을 정리할 때마다 HP 6 회복", Bloodspin, S, 8, 0.1f, "숨", 2, (s, r) => s.healOnRoomClear += 6f * r),
        Upgrade(Resolve, "결의", "차징 중 약한 공격에 경직되지 않고, 받는 피해 25% 감소", "gs_thorns", S, 9, -0.08f, "결"),
        Stat("gs_bulwark", "철벽", "전투마다 첫 피격 1회 무효", "gs_breath", S, 9, 0.08f, "벽", 1, (s, r) => s.wardsPerRoom += r),
        Stat("gs_undying", "불굴", "최대 체력 +20, 받는 피해 -8%", Resolve, S, 10, 0f, "굴", 1, (s, r) => { s.maxHealthBonus += 20f * r; s.damageTakenMultiplier *= 1f - 0.08f * r; }),

        // ── 이속 ──
        Stat("gs_light", "가벼운 발걸음", "이동 속도 +3%", "gs_breath_in", M, 4, 0.18f, "이", 3, (s, r) => s.moveSpeedMultiplier *= 1f + 0.03f * r),
        Skill(Leap, "도약 강타 [R]", "커서 쪽으로 뛰어올라 내려찍습니다. 착지 지점의 적을 모두 넘어뜨립니다", "gs_light", M, 5, 0.28f, "gs_leap"),
        Stat("gs_quick", "빠른 손", "공격 속도 +3%", Leap, M, 6, 0.2f, "속", 3, (s, r) => s.attackSpeedMultiplier *= 1f + 0.03f * r),
        Upgrade(Skyfall, "하늘 가르기", "도약 거리 +40%, 착지 범위 +1m", Leap, M, 6, 0.44f, "하"),
        Upgrade(Tempo, "질풍의 검", "휠윈드·검기·도약 강타·차징 강타 재사용 시간 -20%", "gs_quick", M, 7, 0.22f, "질"),
        Stat("gs_after", "잔상", "피해 회피 확률 +4%", Skyfall, M, 7, 0.4f, "잔", 3, (s, r) => s.dodgeChance += 0.04f * r),
        Stat("gs_goldhand", "황금 손", "얻는 황금 +15%", Skyfall, M, 7, 0.58f, "금", 2, (s, r) => s.goldMultiplier *= 1f + 0.15f * r),
        Stat("gs_wind", "바람걸음", "이동 속도 +5%, 공격 속도 +2%", Tempo, M, 8, 0.3f, "풍", 2, (s, r) => { s.moveSpeedMultiplier *= 1f + 0.05f * r; s.attackSpeedMultiplier *= 1f + 0.02f * r; }),
        Upgrade(Rush, "돌진", "차징 중 [Space]로 일직선 돌진, 지나가는 적에게 피해 (차징 강타 필요)", "gs_after", M, 8, 0.48f, "돌", also: Charge),
        Stat("gs_hunter", "사냥꾼", "치명타 확률 +2%", "gs_goldhand", M, 8, 0.66f, "냥", 2, (s, r) => s.critChance += 0.02f * r),
        Stat("gs_sprint", "질주", "이동 속도 +5%", "gs_wind", M, 9, 0.38f, "주", 2, (s, r) => s.moveSpeedMultiplier *= 1f + 0.05f * r),
        Stat("gs_evasion", "회피의 달인", "피해 회피 확률 +3%", Rush, M, 9, 0.56f, "달", 2, (s, r) => s.dodgeChance += 0.03f * r),
        Stat("gs_swift", "날랜 칼끝", "공격 속도 +4%", "gs_hunter", M, 9, 0.74f, "끝", 2, (s, r) => s.attackSpeedMultiplier *= 1f + 0.04f * r),
        Stat("gs_windlord", "바람의 화신", "공격 속도 +6%, 이동 속도 +6%", "gs_sprint", M, 10, 0.46f, "화", 1, (s, r) => { s.attackSpeedMultiplier *= 1f + 0.06f * r; s.moveSpeedMultiplier *= 1f + 0.06f * r; }),
        Stat("gs_stormlord", "폭풍의 군주", "피해 회피 확률 +5%, 이동 속도 +5%", "gs_evasion", M, 10, 0.64f, "군", 1, (s, r) => { s.dodgeChance += 0.05f * r; s.moveSpeedMultiplier *= 1f + 0.05f * r; }),
    };

    public static readonly SkillTree Tree = new SkillTree("대검 스킬트리", NodeList, Combo2, Combo3);

    public static int Points => Tree.Points;
    public static bool Has(string id) => Tree.Has(id);
    public static int ComboLength => Tree.ComboLength;
    public static bool Active { get => Tree.Active; set => Tree.Active = value; }
    public static void Reset(int startingPoints) => Tree.Reset(startingPoints);
    public static void AddPoint() => Tree.AddPoint();
    public static void ApplyStats(RunStats stats) => Tree.ApplyStats(stats);

    // 질풍의 검: the skills come round sooner
    public static float CooldownScale => Has(Tempo) ? 0.8f : 1f;
}
