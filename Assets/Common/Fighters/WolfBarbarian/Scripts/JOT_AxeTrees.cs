using UnityEngine;
using static SkillTree;

namespace NineWorlds.Jotunheim
{
    // The two axes' skill trees (shown in the full game's [K] panel through JotunAxes.Tree).
    // All five skills of a weapon are there from the start; a tree adds each skill's own upgrade and a few stat nodes.
    //
    // The shared GameManager only resets and feeds the trees it knows (its own and ArmoryWeapon.Trees, which are shared
    // code), so these follow the greatsword's tree, which it always feeds: when that one gains points, so do these; when it
    // is reset for a new run, so are these; when test mode learns it all, these are learned too.
    public static class JOT_AxeTrees
    {
        // ── 양손도끼 ──
        public const string GaMight = "ga_might", GaChargeStun = "ga_charge_stun", GaSpinWide = "ga_spin_wide",
            GaStepQuake = "ga_step_quake", GaHide = "ga_hide", GaCryLong = "ga_cry_long", GaFury = "ga_fury",
            GaSmashGroggy = "ga_smash_groggy", GaUnstoppable = "ga_unstoppable";

        // ── 사슬 쌍도끼 ──
        public const string TwEdge = "tw_edge", TwHuntBleed = "tw_hunt_bleed", TwThrowFar = "tw_throw_far",
            TwDodgeCut = "tw_dodge_cut", TwSwift = "tw_swift", TwLeapReset = "tw_leap_reset", TwFrenzy = "tw_frenzy",
            TwExecMore = "tw_exec_more", TwBloodlust = "tw_bloodlust";

        private static Node S(string id, string name, string effect, string parent, int column, int row, string glyph) =>
            new Node { id = id, name = name, effect = effect, parent = parent, branch = Branch.Trunk, kind = Kind.Upgrade, row = row, x = column * 0.4f, glyph = glyph };

        private static Node T(string id, string name, string effect, string parent, int column, int row, string glyph, int maxRank, System.Action<RunStats, int> stats) =>
            new Node { id = id, name = name, effect = effect, parent = parent, branch = Branch.Trunk, kind = Kind.Stat, row = row, x = column * 0.4f, glyph = glyph, maxRank = maxRank, stats = stats };

        public static readonly SkillTree Greataxe = new SkillTree("양손도끼 기술", new[]
        {
            T(GaMight, "야만의 힘", "공격력 +1 (단계마다)", null, 0, 0, "힘", 3, (s, r) => s.damageBonus += r),
            S(GaChargeStun, "어깨 돌진 강화", "기절 +0.8초, 들이받는 폭 +30%", GaMight, -1, 1, "돌"),
            S(GaSpinWide, "회전베기 강화", "반경 +30%, 피해 +25%", GaMight, 0, 1, "회"),
            S(GaStepQuake, "늑대 발걸음 강화", "내딛는 곳에 충격파 (주변 적 1.2배 피해)", GaMight, 1, 1, "걸"),
            T(GaHide, "두꺼운 가죽", "최대 HP +15 (단계마다)", GaChargeStun, -1, 2, "가", 3, (s, r) => s.maxHealthBonus += 15f * r),
            S(GaCryLong, "전사의 포효 강화", "지속 +3초, 받는 피해 -40% (기본 -30%)", GaSpinWide, 0, 2, "포"),
            T(GaFury, "광전사", "공격 속도 +8% (단계마다)", GaStepQuake, 1, 2, "광", 2, (s, r) => s.attackSpeedMultiplier *= 1f + 0.08f * r),
            S(GaSmashGroggy, "지진분쇄 강화", "반경 +30%, 보스 그로기 +1.5초", GaCryLong, 0, 3, "지"),
            S(GaUnstoppable, "멈추지 않는 자", "어깨 돌진 재사용 -2초, 늑대 발걸음 재사용 -1초", GaHide, -1, 3, "멈"),
        });

        public static readonly SkillTree TwinAxes = new SkillTree("쌍도끼 기술", new[]
        {
            T(TwEdge, "날 선 도끼", "치명타 확률 +5% (단계마다)", null, 0, 0, "날", 3, (s, r) => s.critChance += 0.05f * r),
            S(TwHuntBleed, "그림자 사냥 강화", "출혈 +2, 재사용 -1초", TwEdge, -1, 1, "사"),
            S(TwThrowFar, "쌍도 투척 강화", "사거리 +30%, 출혈 +1", TwEdge, 0, 1, "투"),
            S(TwDodgeCut, "그림자 회피 강화", "지나간 적을 베고 출혈 1", TwEdge, 1, 1, "회"),
            T(TwSwift, "사냥꾼의 발", "이동 속도 +6% (단계마다)", TwHuntBleed, -1, 2, "발", 3, (s, r) => s.moveSpeedMultiplier *= 1f + 0.06f * r),
            S(TwLeapReset, "사냥꾼의 도약 강화", "출혈 중인 적에게 도약하면 재사용 절반", TwThrowFar, 0, 2, "도"),
            T(TwFrenzy, "연타", "공격 속도 +8% (단계마다)", TwDodgeCut, 1, 2, "연", 2, (s, r) => s.attackSpeedMultiplier *= 1f + 0.08f * r),
            S(TwExecMore, "환영 사형집행 강화", "타수 +4", TwLeapReset, 0, 3, "집"),
            S(TwBloodlust, "피의 갈증", "출혈 피해 +50%", TwSwift, -1, 3, "갈"),
        });

        public static bool Has(string id) => Greataxe.Has(id) || TwinAxes.Has(id);

        // Bleeding hurts more with 피의 갈증 (JOT_Bleed reads this)
        public static float BleedMultiplier => TwinAxes.Has(TwBloodlust) ? 1.5f : 1f;

        // ── following the greatsword's tree (see above) ──
        private static int lastTotal = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Follow()
        {
            lastTotal = -1;
            GreatswordSkills.Tree.Changed -= OnGreatswordChanged;
            GreatswordSkills.Tree.Changed += OnGreatswordChanged;
            RunState.Changed -= AddOurStats;
            RunState.Changed += AddOurStats;
        }

        // The shared RunState only adds the greatsword's and twin blades' stat nodes: each time it rebuilds the stats,
        // the axe tree in hand adds its own to the new set (once per set)
        private static RunStats augmented;

        private static void AddOurStats()
        {
            RunStats stats = RunState.Stats;
            if (stats == null || stats == augmented)
                return;
            augmented = stats;
            Greataxe.ApplyStats(stats);   // (only counts while its weapon is in hand)
            TwinAxes.ApplyStats(stats);
        }

        private static void OnGreatswordChanged()
        {
            SkillTree gs = GreatswordSkills.Tree;
            int total = gs.Points + gs.Spent;
            if (gs.Spent == 0 && gs.Points <= 1)
            {
                // A new run (Reset(1)): the axes' trees start over too
                foreach (SkillTree t in new[] { Greataxe, TwinAxes })
                    if (t.Spent > 0 || t.Points != gs.Points)
                        t.Reset(gs.Points);
            }
            else if (FullyLearned(gs))
            {
                // Test mode learned everything
                foreach (SkillTree t in new[] { Greataxe, TwinAxes })
                    if (!FullyLearned(t))
                        t.LearnAll();
            }
            else if (lastTotal >= 0 && total > lastTotal)
            {
                // A level up: the same points here
                foreach (SkillTree t in new[] { Greataxe, TwinAxes })
                    t.AddPoints(total - lastTotal);
            }
            lastTotal = gs.Points + gs.Spent;
        }

        private static bool FullyLearned(SkillTree t)
        {
            foreach (Node n in t.Nodes)
                if (!t.IsMaxed(n))
                    return false;
            return true;
        }
    }
}
