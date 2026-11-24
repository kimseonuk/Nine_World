using static SkillTree;

// The spear-keeper's (창지기) skill tree: ten steps, laid out like the twin blades' — a trunk of basics, then three
// branches that fork wider as they climb: 근접 창술 (the combo and the thrusts, left), 창 기술 (the Q, E and R skills,
// centre) and 투척 (the throws, right). At the start he has only his first thrust and the plain dash [Space]; the
// combo's later blows and every skill are learned here, and each skill has an upgrade of its own above it. At the very
// top stands 궁니르: the spear awakens as Odin's own. One point per level-up; a node needs the one below it and its row
// opens as points go into the tree. Everything learned is lost on death. (Shown in the game's [K] panel.)
public static class SpearSkills
{
    // trunk
    public const string Combo2 = "combo2";          // 2타 내지르기
    public const string Throw = "throw";            // 창 투척 (hold the button)
    public const string DashUp = "dashUp";          // 질풍 돌진 (the dash's upgrade)
    // 근접 창술
    public const string Combo3 = "combo3";          // 3타 회전 베기
    public const string Combo4 = "combo4";          // 4타 이연 찌르기
    public const string Combo5 = "combo5";          // 5타 관통 일격
    public const string Pierce = "pierce";          // 관통 창격 [우클릭]
    public const string PierceUp = "pierceUp";      // 관통 창격 강화
    public const string Flurry = "flurry";          // 연속 찌르기: the 2nd blow strikes hard
    public const string Impale = "impale";          // 꿰뚫기
    public const string Mastery = "mastery";        // 창술의 극의
    // 창 기술
    public const string Sweep = "sweep";            // 회전 창격 [Q]
    public const string SweepUp = "sweepUp";        // 회오리 창격 (Q's upgrade)
    public const string Spikes = "spikes";          // 서리창 군열 [E]
    public const string SpikesUp = "spikesUp";      // 세 갈래 군열 (E's upgrade)
    public const string Rain = "rain";              // 창우 [R]
    public const string RainUp = "rainUp";          // 창우 강화
    public const string SkillMastery = "skillMastery";   // 기술 마스터리
    // 투척
    public const string Recall = "recall";          // 귀환의 창
    public const string FrostThrow = "frostThrow";  // 속박 투척
    public const string Gungnir = "gungnir";        // 궁니르의 비 (three spears at full charge)
    public const string ThrowUp = "throwUp";        // 투척 강화
    public const string Ravens = "ravens";          // 오딘의 까마귀 (throws seek their prey)
    // the crown
    public const string Awaken = "awaken";          // ★ 궁니르: the spear awakens

    // A skill or a skill's upgrade, drawn with its own picture
    private static Node Active(string id, string name, string effect, string parent, Branch branch, int row, float x) =>
        new Node { id = id, name = name, effect = effect, parent = parent, branch = branch, kind = Kind.Skill, row = row, x = x, icon = "SpearTree/" + id };

    private static Node Passive(string id, string name, string effect, string parent, Branch branch, int row, float x) =>
        new Node { id = id, name = name, effect = effect, parent = parent, branch = branch, kind = Kind.Upgrade, row = row, x = x, icon = "SpearTree/" + id };

    private const Branch T = Branch.Trunk, A = Branch.Attack, S = Branch.Survival, M = Branch.Speed;

    private static readonly Node[] NodeList =
    {
        // ── 기본기 (trunk) — the first thrust and the plain dash need nothing ──
        ComboBlow(Combo2, "내지르기", "연속 공격에 2타(한 걸음 들어가며 찌르기)가 이어집니다", null, T, 0, 0f, "2타"),
        Passive(Throw, "깊은 찌르기", "평타 찌르기 사거리 +0.5m · 버튼을 누르고 있으면 연속 공격이 계속 이어집니다", Combo2, T, 1, 0f),
        Stat("sp_basics", "창술 기초", "최대 체력 +5, 공격력 +2%", Throw, T, 2, 0f, "기", 3,
            (s, r) => { s.maxHealthBonus += 5f * r; s.damageMultiplier *= 1f + 0.02f * r; }),
        Passive(DashUp, "질풍 돌진", "돌진 찌르기 강화 · 돌진을 연속 2번 쓸 수 있고 거리 +30%, 끝에서 충격파", "sp_basics", T, 3, 0f),

        // ── 근접 창술 (left) ──
        Stat("sp_tip", "날카로운 창끝", "공격력 +4%", DashUp, A, 4, -0.2f, "날", 3, (s, r) => s.damageMultiplier *= 1f + 0.04f * r),
        ComboBlow(Combo3, "돌격 찌르기", "연속 공격에 3타(깊게 파고들며 길게 내지르는 찌르기)가 이어집니다", "sp_tip", A, 5, -0.24f, "3타"),
        Active(Pierce, "서리의 갈고리", "[우클릭] 서리 사슬 달린 창을 던져 처음 걸린 적(8m)을 발밑까지 끌어당기고 1초 기절 (피해 140%, 6초) · 보스처럼 큰 적은 내가 그쪽으로 끌려갑니다", "sp_tip", A, 5, -0.52f),
        Stat("sp_vital", "급소 찌르기", "치명타 확률 +3%", Combo3, A, 7, -0.24f, "급", 3, (s, r) => s.critChance += 0.03f * r),
        Passive(PierceUp, "갈고리 강화", "서리의 갈고리 사거리 8 → 12m, 피해 140% → 200%", Pierce, A, 7, -0.58f),
        ComboBlow(Combo4, "이연 찌르기", "연속 공격에 4타(빠르게 두 번 찌르기)가 이어집니다", "sp_vital", A, 8, -0.24f, "4타"),
        ComboBlow(Combo5, "도약 투척", "연속 공격에 5타(높이 뛰어올라 커서 쪽 3.5~6m 앞 땅에 창을 내리꽂듯 던지는 마무리 · 꽂힌 자리 3.8m 얼음 충격)가 이어집니다", Combo4, A, 9, -0.2f, "5타"),
        Stat("sp_reach", "긴 자루", "공격 범위 +0.2m", PierceUp, A, 8, -0.46f, "자", 2, (s, r) => s.rangeBonus += 0.2f * r),
        Passive(Flurry, "연속 찌르기", "2타가 강한 찌르기가 됩니다 (피해 180%, 적 공격 끊기)", PierceUp, A, 8, -0.7f),
        Passive(Impale, "꿰뚫기", "5타가 적을 넘어뜨리고, 서리의 갈고리의 기절 +0.5초", Combo5, A, 10, -0.26f),
        Stat("sp_footwork", "창잡이 발놀림", "이동 속도 +3%", "sp_reach", A, 10, -0.48f, "발", 2, (s, r) => s.moveSpeedMultiplier *= 1f + 0.03f * r),
        Stat("sp_breath", "전사의 숨결", "적 처치 시 HP 1 회복", Flurry, A, 10, -0.72f, "숨", 3, (s, r) => s.healOnKill += 1f * r),
        Passive(Mastery, "창술의 극의", "돌진 찌르기 쿨타임 절반. 돌진 직후 0.5초 안의 공격은 강한 일격", Impale, A, 11, -0.34f),
        Stat("sp_melee", "근접 마스터리", "공격력 +8%, 치명타 피해 +20%", "sp_breath", A, 11, -0.62f, "근", 1,
            (s, r) => { s.damageMultiplier *= 1f + 0.08f * r; s.critMultiplier += 0.2f * r; }),
        Stat("sp_berserk", "광전사의 창", "공격 속도 +6%", Mastery, A, 13, -0.48f, "광", 2, (s, r) => s.attackSpeedMultiplier *= 1f + 0.06f * r),

        // ── 창 기술 (centre) ──
        Stat("sp_body", "단련된 몸", "최대 체력 +8", DashUp, S, 4, 0f, "체", 3, (s, r) => s.maxHealthBonus += 8f * r),
        Active(Sweep, "회전 창격", "[Q] 창을 크게 휘둘러 주변 3.6m의 적을 모두 벱니다 (7초)", "sp_body", S, 5, 0f),
        Passive(SweepUp, "회오리 창격", "회전 창격 강화 · 두 바퀴 돌며 주변 적을 끌어당기고, 두 번째 회전 피해 +50%", Sweep, S, 7, -0.1f),
        Active(Spikes, "서리창 군열", "[E] 창을 땅에 꽂으면 앞으로 서리창이 군대처럼 줄지어 솟구치며 적을 넘어뜨립니다 (10초)", Sweep, S, 7, 0.1f),
        Stat("sp_guard", "전장의 감각", "받는 피해 -3%", SweepUp, S, 8, -0.1f, "감", 3, (s, r) => s.damageTakenMultiplier *= 1f - 0.03f * r),
        Passive(SpikesUp, "세 갈래 군열", "서리창 군열 강화 · 서리창이 세 갈래 부채꼴로 솟구칩니다", Spikes, S, 8, 0.1f),
        Active(Rain, "발키리 창우", "[R] 하늘에 발키리 셋이 나타나 커서 위치 반경 4.2m에 창 16개를 내던지고, 마지막에 거대한 빛의 창이 떨어져 폭발하며 주변 적을 넘어뜨립니다 (18초)", SpikesUp, S, 10, 0f),
        Passive(RainUp, "창우 강화", "창 16 → 24개, 마지막 폭발 반경 3.2 → 4.2m · 피해 220% → 300%", Rain, S, 11, 0.08f),
        Stat(SkillMastery, "기술 마스터리", "Q · E · R · 우클릭 쿨타임 -20%", Rain, S, 11, -0.1f, "기", 1, (s, r) => { }),

        // ── 서리의 갈고리 (right): shaping the 우클릭 hook ──
        Stat("sp_grip", "사슬 단련", "공격 속도 +3%", DashUp, M, 4, 0.22f, "투", 3, (s, r) => s.attackSpeedMultiplier *= 1f + 0.03f * r),
        Passive(Recall, "되감기 일격", "갈고리로 끌어온 적에게 곧바로 찌르기 한 번 (피해 70%)", "sp_grip", M, 5, 0.3f),
        Stat("sp_hunter", "사냥꾼의 눈", "치명타 피해 +15%", Recall, M, 7, 0.3f, "눈", 2, (s, r) => s.critMultiplier += 0.15f * r),
        Passive(FrostThrow, "서리 사슬", "갈고리에 끌려온 적의 기절 1 → 2초, 이후 3초 동안 둔화", Recall, M, 7, 0.54f),
        Passive(Gungnir, "사슬 충격", "끌려온 적이 떨어지는 자리에서 충격이 터져 주변 2.2m 적에게 피해 (80%)", "sp_hunter", M, 8, 0.32f),
        Passive(ThrowUp, "강철 사슬", "서리의 갈고리 피해 +30%, 사거리 +15%, 끌려오는 사슬에 스친 적도 피해 (50%)", FrostThrow, M, 8, 0.56f),
        Passive(Ravens, "오딘의 까마귀", "갈고리가 앞쪽의 가장 가까운 적을 찾아가 걸립니다", Gungnir, M, 10, 0.34f),
        Stat("sp_valkyrie", "발키리의 가호", "피해 회피 확률 +4%", ThrowUp, M, 10, 0.58f, "발", 2, (s, r) => s.dodgeChance += 0.04f * r),
        Stat("sp_throw", "사슬 마스터리", "치명타 확률 +5%, 얻는 황금 +15%", Ravens, M, 11, 0.46f, "투", 1,
            (s, r) => { s.critChance += 0.05f * r; s.goldMultiplier *= 1f + 0.15f * r; }),
        Stat("sp_harvest", "창우의 수확", "방을 정리할 때마다 HP 6 회복", "sp_throw", M, 13, 0.5f, "수", 2, (s, r) => s.healOnRoomClear += 6f * r),

        // ── the in-between steps (3, 6, 9): a passive on each branch ──
        Stat("sp_edge", "창날 벼리기", "공격력 +3%", Combo3, A, 6, -0.4f, "벼", 3, (s, r) => s.damageMultiplier *= 1f + 0.03f * r),
        Stat("sp_rush", "돌격 본능", "이동 속도 +4%", Combo4, A, 9, -0.4f, "돌", 2, (s, r) => s.moveSpeedMultiplier *= 1f + 0.04f * r),
        Stat("sp_veteran", "백전노장", "최대 체력 +10", Mastery, A, 12, -0.3f, "노", 2, (s, r) => s.maxHealthBonus += 10f * r),
        Stat("sp_focus", "정신 집중", "공격 속도 +3%", Sweep, S, 6, 0f, "집", 2, (s, r) => s.attackSpeedMultiplier *= 1f + 0.03f * r),
        Stat("sp_stance", "굳건한 자세", "받는 피해 -3%", SpikesUp, S, 9, 0.18f, "굳", 2, (s, r) => s.damageTakenMultiplier *= 1f - 0.03f * r),
        Stat("sp_odin", "오딘의 축복", "최대 체력 +10, 공격력 +4%", RainUp, S, 12, 0.2f, "축", 1,
            (s, r) => { s.maxHealthBonus += 10f * r; s.damageMultiplier *= 1f + 0.04f * r; }),
        Stat("sp_wind", "바람 가르기", "치명타 확률 +2%", Recall, M, 6, 0.42f, "풍", 2, (s, r) => s.critChance += 0.02f * r),
        Stat("sp_aim", "정조준", "공격 범위 +0.2m", Gungnir, M, 9, 0.2f, "조", 2, (s, r) => s.rangeBonus += 0.2f * r),
        Stat("sp_einherjar", "에인헤랴르의 긍지", "치명타 피해 +20%", "sp_throw", M, 12, 0.36f, "긍", 2, (s, r) => s.critMultiplier += 0.2f * r),

        // ── ★ 궁니르 (the crown) ──
        new Node
        {
            id = Awaken, name = "궁니르", kind = Kind.Skill, branch = S, row = 13, x = 0f, parent = RainUp, icon = "SpearTree/" + Awaken,
            effect = "최종 · 창이 오딘의 창 궁니르로 각성합니다. 금빛 창, 모든 공격 피해 +25%, 치명타 확률 +8%. " +
                     "서리의 갈고리이 반드시 적을 찾아가 걸고 끌어온 적을 바로 꿰뚫으며, 5타에서 궁니르의 섬광이 뻗어 나갑니다",
            stats = (s, r) => { s.damageMultiplier *= 1.25f; s.critChance += 0.08f; },
        },
    };

    // Ten steps above the trunk (rows 4-13); a step opens once twice its number less one points are in the tree
    // (step 2: 2, step 5: 8, step 10: 18)
    public static readonly SkillTree Tree = new SkillTree("창 기술", NodeList, Combo2, Combo3, Combo4, Combo5)
    {
        BranchLabels = new[] { "근접 창술", "창 기술", "서리의 갈고리" },
        TopRow = 13,
        OpenRule = row => row <= TrunkRows ? 0 : 2 * (row - TrunkRows),
    };

    public static int Points => Tree.Points;
    public static bool Has(string id) => Tree.Has(id);
    public static void Reset(int startingPoints) => Tree.Reset(startingPoints);
    public static void AddPoint() => Tree.AddPoint();

    // How many blows of the five-blow combo are learned (1 to 5)
    public static int ComboLength => Tree.ComboLength;
    // The skills' cooldowns with 기술 마스터리 taken
    public static float CooldownFactor => Has(SkillMastery) ? 0.8f : 1f;
    // The spear has awakened as Gungnir
    public static bool Awakened => Has(Awaken);
}
