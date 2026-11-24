using static SkillTree;

// The lightning gauntlets' skill tree (번개 건틀릿 스킬트리), laid out like the greatsword's and the 번개 망치's: the trunk
// holds the basics (the dash is its 2nd node), and each branch holds one skill and builds it up to its master form.
//   공격 — Q 뇌격 도약: a short leap forward, both fists into the floor (lightning damage round the landing)
//   생존 — R 뇌신 강림: becomes the thunder god for a while (Q and E grow stronger); R again ends it early in a blast
//          of lightning over the whole area
//   이속 — E 천둥 연타: a flurry of punches in front
// Each skill starts bare (the motion and the damage) and gains its lightning step by step.
public static class GauntletSkills
{
    // The skills
    public const string Dash = "gt_dash";        // 대시 [Space]
    public const string Leap = "gt_leap";        // 뇌격 도약 [Q]
    public const string Barrage = "gt_barrage";  // 천둥 연타 [E]
    public const string Avatar = "gt_avatar";    // 뇌신 강림 [R]

    // Q
    public const string LeapPower = "gt_qpow";   // 무거운 착지: Q +15% a rank (3)
    public const string LeapFar = "gt_qfar";     // 멀리뛰기: further, wider landing
    public const string Aftershock = "gt_qquake";// 여진: a second shockwave
    public const string LeapMaster = "gt_qmaster"; // 천둥 낙하: bolts from the sky on landing, stun
    // E
    public const string MoreHits = "gt_ehits";   // 연타 수: +2 punches a rank (3)
    public const string Swift = "gt_espeed";     // 질풍권: faster, can walk while punching
    public const string Rising = "gt_efinish";   // 승천권: a closing uppercut
    public const string BarrageMaster = "gt_emaster"; // 뇌권 난무: every punch throws a bolt forward
    // R
    public const string AvatarTime = "gt_rtime"; // 신의 시간: +1.5s a rank (3)
    public const string AvatarBody = "gt_rarmor";// 뇌신의 몸: -25% damage taken while transformed
    public const string Overload = "gt_rblast";  // 방전: the release blast +50%
    public const string AvatarMaster = "gt_rmaster"; // 토르 현신: bolts rain on nearby enemies while transformed
    // misc
    public const string Blink = "gt_blink";      // 번개 걸음: a longer dash
    public const string Tempo = "gt_tempo";      // 천둥의 리듬: Q E R come round 20% sooner

    private const Branch T = Branch.Trunk, A = Branch.Attack, S = Branch.Survival, M = Branch.Speed;
    private static void None(RunStats s, int r) { }

    private static readonly Node[] NodeList =
    {
        // ── 줄기 ──
        Stat("gt_root", "뇌권술", "공격력 +5%", null, T, 0, 0f, "권", 1, (s, r) => s.damageMultiplier *= 1f + 0.05f * r),
        Skill(Dash, "대시 [Space]", "짧게 앞으로 박차고 나갑니다", "gt_root", T, 1, 0f, "gs_rush"),
        Stat("gt_basics", "기초 단련", "최대 체력 +5, 공격력 +2%", Dash, T, 2, 0f, "기", 3, (s, r) => { s.maxHealthBonus += 5f * r; s.damageMultiplier *= 1f + 0.02f * r; }),
        Stat("gt_breath", "호흡", "이동 속도 +2%, 공격 속도 +2%", "gt_basics", T, 3, 0f, "호", 2, (s, r) => { s.moveSpeedMultiplier *= 1f + 0.02f * r; s.attackSpeedMultiplier *= 1f + 0.02f * r; }),

        // ── 공격: Q 뇌격 도약 ──
        Stat("gt_heft", "강철 주먹", "공격력 +4%", "gt_breath", A, 4, -0.18f, "공", 3, (s, r) => s.damageMultiplier *= 1f + 0.04f * r),
        Skill(Leap, "뇌격 도약 [Q]", "앞으로 짧게 뛰어올라 두 주먹으로 지면을 내려칩니다. 착지 주변에 번개 피해", "gt_heft", A, 5, -0.28f, "storm_strike"),
        Stat(LeapPower, "무거운 착지", "뇌격 도약 피해 +15%", Leap, A, 6, -0.22f, "착", 3, None),
        Stat("gt_volt", "고전압", "치명타 확률 +3%", Leap, A, 6, -0.44f, "압", 3, (s, r) => s.critChance += 0.03f * r),
        Upgrade(LeapFar, "멀리뛰기", "도약 거리 +40%, 착지 범위 +0.8m", LeapPower, A, 7, -0.26f, "멀"),
        Stat("gt_bolt", "벼락", "치명타 피해 +15%", "gt_volt", A, 7, -0.5f, "벼", 2, (s, r) => s.critMultiplier += 0.15f * r),
        Upgrade(Aftershock, "여진", "착지 직후 번개 충격파가 한 번 더 퍼집니다 (피해 60%)", LeapFar, A, 8, -0.3f, "여"),
        Stat("gt_crush", "파쇄", "공격력 +5%", "gt_bolt", A, 8, -0.56f, "쇄", 3, (s, r) => s.damageMultiplier *= 1f + 0.05f * r),
        Upgrade(LeapMaster, "천둥 낙하 (Q 마스터)", "착지하면 하늘에서 낙뢰가 쏟아지고, 맞은 적은 기절합니다. 번개 피해 +40%", Aftershock, A, 9, -0.36f, "낙"),
        Stat("gt_chain", "연쇄 번개", "공격이 주변 적 1명에게 번개로 튑니다", "gt_crush", A, 9, -0.62f, "뇌", 1, (s, r) => s.chainTargets += r),
        Stat("gt_fury", "천둥신", "공격력 +6%", LeapMaster, A, 10, -0.46f, "신", 3, (s, r) => s.damageMultiplier *= 1f + 0.06f * r),

        // ── 생존: R 뇌신 강림 ──
        Stat("gt_sturdy", "강인함", "최대 체력 +8", "gt_breath", S, 4, 0f, "체", 3, (s, r) => s.maxHealthBonus += 8f * r),
        Skill(Avatar, "뇌신 강림 [R]", "번개의 신으로 변신합니다 (뇌격 도약·천둥 연타 강화). 변신 중 R: 즉시 해제하며 주변 전체에 전기 폭발", "gt_sturdy", S, 5, 0f, "storm_whirl"),
        Stat(AvatarTime, "신의 시간", "변신 지속 시간 +1.5초", Avatar, S, 6, -0.07f, "시", 3, None),
        Stat("gt_thirst", "피의 갈증", "적 처치 시 HP 1 회복", Avatar, S, 6, 0.07f, "흡", 3, (s, r) => s.healOnKill += 1f * r),
        Upgrade(AvatarBody, "뇌신의 몸", "변신 중 받는 피해 -25%", AvatarTime, S, 7, -0.08f, "몸"),
        Stat("gt_guard", "단련된 몸", "받는 피해 -3%", "gt_thirst", S, 7, 0.08f, "방", 3, (s, r) => s.damageTakenMultiplier *= 1f - 0.03f * r),
        Upgrade(Overload, "방전", "해제 폭발 피해 +50%", AvatarBody, S, 8, -0.1f, "방"),
        Stat("gt_giant", "거인의 심장", "최대 체력 +12", "gt_guard", S, 8, 0.1f, "심", 3, (s, r) => s.maxHealthBonus += 12f * r),
        Upgrade(AvatarMaster, "토르 현신 (R 마스터)", "변신 중 주변 적에게 1초마다 낙뢰가 떨어지고, 해제 폭발 때 번개 비가 쏟아집니다", Overload, S, 9, -0.08f, "현"),
        Stat("gt_bulwark", "철벽", "전투마다 첫 피격 1회 무효", "gt_giant", S, 9, 0.08f, "벽", 1, (s, r) => s.wardsPerRoom += r),
        Stat("gt_undying", "불굴", "최대 체력 +20, 받는 피해 -8%", AvatarMaster, S, 10, 0f, "굴", 1, (s, r) => { s.maxHealthBonus += 20f * r; s.damageTakenMultiplier *= 1f - 0.08f * r; }),

        // ── 이속: E 천둥 연타 ──
        Stat("gt_light", "가벼운 발걸음", "이동 속도 +3%", "gt_breath", M, 4, 0.18f, "이", 3, (s, r) => s.moveSpeedMultiplier *= 1f + 0.03f * r),
        Skill(Barrage, "천둥 연타 [E]", "앞쪽으로 주먹을 쉴 새 없이 꽂아 넣습니다", "gt_light", M, 5, 0.28f, "storm_chain"),
        Stat(MoreHits, "연타 수", "천둥 연타 +2타", Barrage, M, 6, 0.22f, "타", 3, None),
        Stat("gt_quick", "빠른 손", "공격 속도 +3%", Barrage, M, 6, 0.44f, "속", 3, (s, r) => s.attackSpeedMultiplier *= 1f + 0.03f * r),
        Upgrade(Swift, "질풍권", "연타 속도 +30%, 연타 중에도 천천히 걸을 수 있습니다", MoreHits, M, 7, 0.26f, "질"),
        Stat("gt_after", "잔상", "피해 회피 확률 +4%", "gt_quick", M, 7, 0.5f, "잔", 3, (s, r) => s.dodgeChance += 0.04f * r),
        Upgrade(Rising, "승천권", "연타 끝에 번개 어퍼컷: 큰 피해와 함께 적을 날려 보냅니다", Swift, M, 8, 0.3f, "승"),
        Upgrade(Blink, "번개 걸음", "대시 거리 +30%", "gt_after", M, 8, 0.56f, "걸"),
        Upgrade(BarrageMaster, "뇌권 난무 (E 마스터)", "주먹마다 앞으로 번개가 뻗어 나가 멀리 있는 적까지 꿰뚫습니다", Rising, M, 9, 0.36f, "난"),
        Upgrade(Tempo, "천둥의 리듬", "뇌격 도약·천둥 연타·뇌신 강림 재사용 시간 -20%", Blink, M, 9, 0.62f, "리"),
        Stat("gt_windlord", "폭풍의 화신", "이동 속도 +6%, 공격 속도 +6%", BarrageMaster, M, 10, 0.46f, "화", 1, (s, r) => { s.moveSpeedMultiplier *= 1f + 0.06f * r; s.attackSpeedMultiplier *= 1f + 0.06f * r; }),
    };

    public static readonly SkillTree Tree = new SkillTree("번개 건틀릿 스킬트리", NodeList);
    public static bool Has(string id) => Tree.Has(id);
    public static int Rank(string id) => Tree.Rank(id);
    public static float CooldownScale => Has(Tempo) ? 0.8f : 1f;
    public static float DashScale => Has(Blink) ? 1.3f : 1f;
}
