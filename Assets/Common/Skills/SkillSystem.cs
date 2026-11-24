using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// ── The skill system (기획서: 스킬 · 보조스킬 · 강화 · 특별강화) ──
// Each class starts with its own four skills on Q / E / R / right click. Every one of them has two support sockets
// (보조스킬): the 32 element skills (8 elements × 4) go in them. Each time a class skill is used, its supports take
// turns casting alongside it. Every support also raises stats of its own, and supports of the same element together
// raise more (세트). On a level-up the player either upgrades a support or draws a new one. The upgrade room (강화방)
// upgrades supports only, for gold, as often as the gold lasts — and an upgrade can break the support. A boss leaves a
// special upgrade (특별강화) for a class skill itself: two of its four versions on offer, rerolled with 새로고침권.
// (The four versions are still to be designed: their slots are here, empty.) Dodging and skills cost 기력 (stamina).
public class SkillSystem : MonoBehaviour
{
    public static SkillSystem Instance { get; private set; }

    // The class skills keep their own keys (the supports ride on them)
    public static bool OwnsSlots => false;
    // Runes are set aside in this design
    public static bool RunesOff => true;

    public const int Mains = 4;
    public static readonly string[] MainKeys = { "Q", "E", "R", "우클릭" };
    public static readonly char[] WeaponKeys = { 'Q', 'E', 'R', 'M' };
    public static string WeaponKeyName(char k) => k == 'M' ? "우클릭" : k.ToString();
    public static int MainIndex(char k) => k == 'Q' ? 0 : k == 'E' ? 1 : k == 'R' ? 2 : 3;

    public static readonly SkillInstance[] Sockets = new SkillInstance[Mains * 2];   // [main * 2 + 0 / 1]
    public const int BagSize = 12;
    public static readonly SkillInstance[] Bag = new SkillInstance[BagSize];

    public static int Tickets { get; private set; }          // 확정 강화권
    public static int RoomsTowardTicket { get; private set; }
    public static int Shields { get; private set; }          // 방어권: a breaking upgrade is stopped
    public static int SpecialTokens { get; private set; }    // 특별 강화 (from a boss)
    public static int SpecialRerolls { get; private set; }   // 특별 강화 새로고침권
    public static int RewardRerolls { get; private set; }    // 보상 리롤 (level-up draws)
    public static int ShopRerollsLeft { get; private set; }  // 상점 리롤
    public const int RoomsPerTicket = 3;
    public const int TicketPrice = 150;
    public const int RerollPrice = 60;
    // The rune altar's draw-again stone (보조마법 방): dearer each time it is used in a run
    public static int AltarRerollPrice => 40 + altarRerolls * 25;
    private static int altarRerolls;

    public static void AddTicket(int n = 1) => Tickets += n;
    public static void AddSpecialToken(int n = 1) => SpecialTokens += n;
    public static void AddReroll(int n = 1) => SpecialRerolls += n;
    public static void AddShield(int n = 1) => Shields += n;
    public static void AddRewardReroll(int n = 1) => RewardRerolls += n;

    // ═══════════ The class skills (as each class's own skill bar shows them) ═══════════
    public class WeaponSkillInfo
    {
        public string fighter, fighterName, weaponName, name;
        public char key;
        public Texture2D icon;
        public SkillBar.Slot slot;
        public string Key => fighter + "/" + key;
    }
    private static readonly Dictionary<string, WeaponSkillInfo> weaponSkills = new Dictionary<string, WeaponSkillInfo>();
    public static IEnumerable<WeaponSkillInfo> KnownWeaponSkills => weaponSkills.Values;
    public static WeaponSkillInfo WeaponSkill(string key) => key != null && weaponSkills.TryGetValue(key, out var w) ? w : null;
    public static WeaponSkillInfo MainSkill(int main) => CurrentFighter == null ? null : WeaponSkill(CurrentFighter + "/" + WeaponKeys[main]);
    public static string CurrentFighter { get; private set; }
    private static CharacterSwitcher switcher;

    // Each frame a skill bar is drawn: what its class's four skills are called, their icons and cooldowns
    public static List<SkillBar.Slot> Merge(IList<SkillBar.Slot> weapon)
    {
        if (switcher == null) switcher = Object.FindAnyObjectByType<CharacterSwitcher>();
        CharacterProfile prof = switcher != null ? switcher.Current : null;
        if (prof != null)
        {
            CurrentFighter = prof.name;
            foreach (var w in weapon)
            {
                int order = SkillBar.Order(w.key);
                if (order > 3 || string.IsNullOrEmpty(w.name)) continue;
                string key = prof.name + "/" + WeaponKeys[order];
                if (!weaponSkills.TryGetValue(key, out WeaponSkillInfo info))
                    weaponSkills[key] = info = new WeaponSkillInfo { fighter = prof.name, key = WeaponKeys[order] };
                info.name = w.name;
                info.fighterName = prof.DisplayName;
                info.weaponName = prof.WeaponName;
                if (w.icon != null) info.icon = w.icon;
                info.slot = w;
            }
        }
        return new List<SkillBar.Slot>(weapon);
    }

    // ═══════════ Keys: the class skills fire on their own keys; each use costs 기력 and casts a support ═══════════
    private static readonly int[] keyFrame = { -1, -1, -1, -1, -1 };
    private static readonly bool[] keyResult = new bool[5];
    public static bool NoCooldown;      // (the training room: stamina without limit)
    public static string LastCastName;
    public static float LastCastTime = -99f;

    public static bool KeyDown(Keyboard kb, char k)
    {
        if (Blocking || !Raw(kb, k, true)) return false;
        int i = MainIndex(k);
        if (keyFrame[i] == Time.frameCount) return keyResult[i];
        keyFrame[i] = Time.frameCount;
        keyResult[i] = Use(i);
        return keyResult[i];
    }

    public static bool KeyHeld(Keyboard kb, char k) => !Blocking && Raw(kb, k, false);
    public static bool RightPressed(Mouse mouse) => KeyDown(Keyboard.current, 'M');
    public static bool RightHeld(Mouse mouse) => KeyHeld(Keyboard.current, 'M');

    // Space: the dodge, for 기력
    public static bool DodgeDown(Keyboard kb)
    {
        if (Blocking || kb == null || !kb.spaceKey.wasPressedThisFrame) return false;
        if (keyFrame[4] == Time.frameCount) return keyResult[4];
        keyFrame[4] = Time.frameCount;
        keyResult[4] = Spend(DodgeCost);
        return keyResult[4];
    }

    private static bool Raw(Keyboard kb, char k, bool down)
    {
        if (k == 'M')
        {
            Mouse m = Mouse.current;
            return m != null && (down ? m.rightButton.wasPressedThisFrame : m.rightButton.isPressed);
        }
        if (kb == null) return false;
        KeyControl c = k == 'Q' ? kb.qKey : k == 'E' ? kb.eKey : kb.rKey;
        return down ? c.wasPressedThisFrame : c.isPressed;
    }

    // A class skill pressed: if it's ready (as its bar says), 기력 is paid and the next support casts with it
    private static bool Use(int main)
    {
        WeaponSkillInfo info = MainSkill(main);
        bool ready = info == null || (info.slot.learned && info.slot.left <= 0.05f);
        if (!ready) return true;   // (the weapon itself says why: cooling down, or locked)
        // (ending a transformation — 변신 해제 — is free)
        bool cancel = info != null && info.name != null && info.name.Contains("해제");
        if (!cancel && !Spend(CostOf(main))) return false;
        LastCastName = info != null ? info.name : MainKeys[main];
        LastCastTime = Time.time;
        if (Instance != null) Instance.CastSupport(main);
        return true;
    }

    // ═══════════ 기력 (stamina) ═══════════
    public const float DodgeCost = 20f;

    // What a skill costs depends on what it does: a boon most, then a wide-area skill, then a dash or a thrown blow
    // (검기), then a single blow. A class skill pays its own cost plus every support socketed in it (a support always
    // costs less than any class skill).
    public enum CostTier { Buff, Wide, Move, Single }
    public static readonly string[] CostTierNames = { "버프", "광범위", "이동·투사체", "단일·근접" };
    private static readonly float[] ClassCosts = { 35f, 28f, 22f, 16f };
    private static readonly float[] SupportCosts = { 10f, 8f, 6f, 4f };

    public static CostTier TierOf(SkillDef d)
    {
        switch (d.kind)
        {
            case SkillKind.Buff: return CostTier.Buff;
            case SkillKind.Nova: case SkillKind.Field: case SkillKind.Orbit: case SkillKind.Chain: return CostTier.Wide;
            case SkillKind.Dash: case SkillKind.Projectile: case SkillKind.Line: return CostTier.Move;
            default: return CostTier.Single;   // Strike, Smash
        }
    }
    public static float SupportCost(SkillInstance s) => s == null ? 0f : SupportCosts[(int)TierOf(s.def)];

    // Each class's Q, E, R, right click
    private static readonly Dictionary<CharacterProfile.WeaponKind, CostTier[]> ClassTiers = new Dictionary<CharacterProfile.WeaponKind, CostTier[]>
    {
        // 휠윈드 · 검기 · 도약 강타 · 차징 강타
        { CharacterProfile.WeaponKind.Greatsword, new[] { CostTier.Wide, CostTier.Move, CostTier.Wide, CostTier.Single } },
        // 난무 · 귀신베기 · 광폭화 · 검기
        { CharacterProfile.WeaponKind.DualBlades, new[] { CostTier.Wide, CostTier.Move, CostTier.Buff, CostTier.Move } },
        // 회전 창격 · 서리창 군열 · 발키리 창우 · 서리의 갈고리
        { CharacterProfile.WeaponKind.Spear, new[] { CostTier.Wide, CostTier.Wide, CostTier.Wide, CostTier.Move } },
        // 회전베기 · 전사의 포효 · 지진분쇄 · 어깨 돌진
        { CharacterProfile.WeaponKind.Greataxe, new[] { CostTier.Wide, CostTier.Buff, CostTier.Wide, CostTier.Move } },
        // 쌍도 투척 · 사냥꾼의 도약 · 환영 사형집행 (dashes in on one target) · 그림자 사냥 (appears behind one)
        { CharacterProfile.WeaponKind.TwinAxes, new[] { CostTier.Move, CostTier.Move, CostTier.Move, CostTier.Move } },
        // 망치 투척 · 번개 방출 · 휠윈드 · 질풍 회전
        { CharacterProfile.WeaponKind.Hammer, new[] { CostTier.Move, CostTier.Wide, CostTier.Wide, CostTier.Wide } },
        // 뇌격 도약 · 천둥 연타 · 뇌신 강림 · 질풍 회전
        { CharacterProfile.WeaponKind.Gauntlets, new[] { CostTier.Move, CostTier.Single, CostTier.Buff, CostTier.Wide } },
        // 바람의 서약 · 울르의 돌풍함정 · 바람의 추적 화살 · 바람의 매
        { CharacterProfile.WeaponKind.Bow, new[] { CostTier.Move, CostTier.Wide, CostTier.Wide, CostTier.Move } },
    };

    public static CostTier ClassTier(int main)
    {
        if (switcher == null) switcher = Object.FindAnyObjectByType<CharacterSwitcher>();
        CharacterProfile prof = switcher != null ? switcher.Current : null;
        if (prof != null && ClassTiers.TryGetValue(prof.Weapon, out CostTier[] tiers) && main >= 0 && main < tiers.Length)
            return tiers[main];
        return CostTier.Single;
    }
    public static float ClassCost(int main) => ClassCosts[(int)ClassTier(main)];
    public static float CostOf(int main) => ClassCost(main) + SupportCost(Sockets[main * 2]) + SupportCost(Sockets[main * 2 + 1]);
    public static float Stamina { get; private set; } = 100f;
    private static float staminaUsedAt = -99f, staminaWarnAt = -99f, staminaFromSupports;
    public static float StaminaMax => 100f + MetaProgress.StaminaBonus + staminaFromSupports;

    // 훈련장's cooldown reset: 기력 full, and the supports' own cooldowns and charges back
    public static void ReadyAllForTraining()
    {
        Stamina = StaminaMax;
        foreach (var s in Sockets)
            if (s != null) { s.cooldownLeft = 0f; s.charges = s.MaxCharges; }
        foreach (var s in Bag)
            if (s != null) s.cooldownLeft = 0f;
    }

    private static bool Spend(float cost)
    {
        if (NoCooldown) return true;
        if (Stamina < cost)
        {
            if (Time.time - staminaWarnAt > 1f) { staminaWarnAt = Time.time; Toast("기력이 부족합니다", new Color(1f, 0.75f, 0.35f)); }
            return false;
        }
        Stamina -= cost;
        staminaUsedAt = Time.time;
        return true;
    }

    // ═══════════ Buff supports' boons ═══════════
    private static float windUntil, shieldUntil, blessUntil, berserkUntil;
    private static float blessHealTick;
    // Boons multiply, but all of them together (with a class's own, see DualBlades) never raise a blow past MaxBurst
    public const float MaxBurst = 2.5f;
    public static float DamageBuff => Mathf.Min(MaxBurst, (Time.time < windUntil ? 1.35f : 1f) * (Time.time < blessUntil ? 1.5f : 1f) * (Time.time < berserkUntil ? 1.6f : 1f)
        * (Time.time < classBoonUntil ? classBoonDamage : 1f));
    // The support 광폭화's boon is running (a class's own 광폭화 doesn't stack on it: the stronger of the two counts)
    public static bool SupportBerserk => Time.time < berserkUntil;
    public const float SupportBerserkDamage = 1.6f;
    public static float DamageTakenBuff => (Time.time < shieldUntil ? 0.5f : 1f) * (Time.time < berserkUntil ? 1.25f : 1f)
        * (Time.time < classBoonUntil ? classBoonGuard : 1f);

    // A class skill's own boon (대검 '투신의 각오'): more damage dealt and less taken while it lasts, shown in the buff row
    private static float classBoonUntil, classBoonDamage = 1f, classBoonGuard = 1f;
    public static void ClassBoon(SkillDef shown, float seconds, float damageMultiplier, float damageTakenMultiplier)
    {
        classBoonUntil = Time.time + seconds;
        classBoonDamage = damageMultiplier;
        classBoonGuard = damageTakenMultiplier;
        if (shown != null) activeBuffs[shown] = classBoonUntil;
    }
    public static float MoveBuff => Time.time < windUntil ? 1.3f : 1f;

    // The boons running now (skill → when it ends): shown as icons with their seconds left in the buff row at the top
    // left — the boon's effect plays once when cast, nothing stays drawn round the player
    private static readonly Dictionary<SkillDef, float> activeBuffs = new Dictionary<SkillDef, float>();

    public static void StartBuff(SkillInstance s)
    {
        float until = Time.time + s.Duration * (1f + 0.05f * s.level);
        switch (s.def.id)
        {
            case "windblessing": windUntil = until; break;
            case "stoneshield": shieldUntil = until; break;
            case "blessing": blessUntil = until; break;
            case "berserk": berserkUntil = until; break;
        }
        activeBuffs[s.def] = until;
    }

    // ═══════════ What the supports raise (고유 스텟, 세트) ═══════════
    public enum Stat { Damage, AttackSpeed, MoveSpeed, MaxHealth, Crit, DamageTaken, HealOnKill, Stamina }
    public static string StatName(Stat s) => s switch
    {
        Stat.Damage => "공격력", Stat.AttackSpeed => "공격 속도", Stat.MoveSpeed => "이동 속도", Stat.MaxHealth => "최대 체력",
        Stat.Crit => "치명타 확률", Stat.DamageTaken => "받는 피해 감소", Stat.HealOnKill => "처치 시 회복", _ => "최대 기력",
    };
    public static bool StatIsPercent(Stat s) => s != Stat.MaxHealth && s != Stat.HealOnKill && s != Stat.Stamina;
    public static string StatText(Stat s, float v) => StatIsPercent(s) ? $"{StatName(s)} +{v * 100f:0.#}%" : $"{StatName(s)} +{v:0.#}";

    // each element's own stat, and a smaller one by what kind of skill it is
    public static (Stat stat, float value) ElementStat(SkillElement e) => e switch
    {
        SkillElement.Fire => (Stat.Damage, 0.06f),
        SkillElement.Ice => (Stat.DamageTaken, 0.04f),
        SkillElement.Lightning => (Stat.AttackSpeed, 0.05f),
        SkillElement.Wind => (Stat.MoveSpeed, 0.05f),
        SkillElement.Earth => (Stat.MaxHealth, 12f),
        SkillElement.Holy => (Stat.HealOnKill, 1.5f),
        SkillElement.Shadow => (Stat.Crit, 0.04f),
        _ => (Stat.Damage, 0.04f),
    };

    public static (Stat stat, float value) KindStat(SkillDef d) => d.kind switch
    {
        SkillKind.Projectile => (Stat.Crit, 0.02f),
        SkillKind.Strike => (Stat.Damage, 0.03f),
        SkillKind.Nova => (Stat.MaxHealth, 8f),
        SkillKind.Smash => (Stat.MaxHealth, 8f),
        SkillKind.Line => (Stat.Crit, 0.02f),
        SkillKind.Field => (Stat.DamageTaken, 0.02f),
        SkillKind.Dash => (Stat.MoveSpeed, 0.03f),
        SkillKind.Chain => (Stat.AttackSpeed, 0.03f),
        SkillKind.Orbit => (Stat.HealOnKill, 1f),
        _ => (Stat.Stamina, 10f),
    };

    public static float LevelScale(SkillInstance s) => 1f + 0.2f * s.level;

    public static List<(Stat stat, float value)> StatsOf(SkillInstance s)
    {
        var e = ElementStat(s.def.element);
        var k = KindStat(s.def);
        float m = LevelScale(s);
        return new List<(Stat, float)> { (e.stat, e.value * m), (k.stat, k.value * m) };
    }

    // 세트: only inside one class skill — when both of its supports share an element, that element's stat rises again.
    // Each class skill can make one set; two skills with the same set stack.
    public const float SetMultiplier = 3f;
    public static float SetBonus(SkillElement e) => ElementStat(e).value * SetMultiplier;

    // The element a class skill's two supports share, or -1
    public static int SetOf(int main)
    {
        SkillInstance a = Sockets[main * 2], b = Sockets[main * 2 + 1];
        return a != null && b != null && a.def.element == b.def.element ? (int)a.def.element : -1;
    }

    // How many class skills hold a set of each element
    public static int[] SetCounts()
    {
        var c = new int[8];
        for (int m = 0; m < Mains; m++) { int e = SetOf(m); if (e >= 0) c[e]++; }
        return c;
    }

    // Everything above, into the run's stats (RunState.Recompute calls this)
    public static void ApplyStats(RunStats st)
    {
        var sum = new float[8];
        foreach (var s in Sockets)
            if (s != null)
                foreach (var (stat, v) in StatsOf(s)) sum[(int)stat] += v;
        int[] sets = SetCounts();
        for (int e = 0; e < 8; e++)
            sum[(int)ElementStat((SkillElement)e).stat] += SetBonus((SkillElement)e) * sets[e];
        st.damageMultiplier *= 1f + sum[(int)Stat.Damage];
        st.attackSpeedMultiplier *= 1f + sum[(int)Stat.AttackSpeed];
        st.moveSpeedMultiplier *= 1f + sum[(int)Stat.MoveSpeed];
        st.maxHealthBonus += sum[(int)Stat.MaxHealth];
        st.critChance += sum[(int)Stat.Crit];
        st.damageTakenMultiplier *= Mathf.Max(0.3f, 1f - sum[(int)Stat.DamageTaken]);
        st.healOnKill += sum[(int)Stat.HealOnKill];
        staminaFromSupports = sum[(int)Stat.Stamina];
        st.goldMultiplier *= MetaProgress.GoldMultiplier;
    }

    // ── Synergies (element + function tags) among the socketed supports ──
    private static readonly bool[] synergyOn = new bool[16];
    public static bool SynergyOn(int index) => index < synergyOn.Length && synergyOn[index];
    public static List<SkillInstance> Socketed()
    {
        var list = new List<SkillInstance>();
        foreach (var s in Sockets) if (s != null) list.Add(s);
        return list;
    }

    // The sockets changed: synergies, stats, health
    public static void RefreshSynergies()
    {
        var list = Socketed();
        for (int i = 0; i < SkillCatalog.Synergies.Count; i++)
            synergyOn[i] = SkillCatalog.Synergies[i].active(list);
        RunState.Refresh();
        if (Player != null) Player.RefreshMaxHealth();
    }

    // ═══════════ Upgrades (일반 강화: supports only) ═══════════
    public static int UpgradeCost(SkillInstance s) => Mathf.Max(5, Mathf.RoundToInt((25 + 20 * s.level) * MetaProgress.UpgradeCostMultiplier));
    public static float SuccessChance(SkillInstance s) => Mathf.Min(0.98f, Mathf.Max(0.3f, 0.95f - 0.07f * s.level) + MetaProgress.UpgradeChanceBonus);
    public static float DestroyChance(SkillInstance s) => s.level < 3 ? 0f : Mathf.Min(0.3f, 0.04f * (s.level - 2));
    public static int SalvageValue(SkillInstance s) => 20 + 15 * s.level;

    // The roll: up a level, nothing, or broken (a 방어권 stops the break). Returns the line to show.
    public static string RollUpgrade(SkillInstance s, out Color color)
    {
        float roll = Random.value, ok = SuccessChance(s), bad = DestroyChance(s);
        if (roll < ok)
        {
            s.level++;
            color = new Color(0.6f, 1f, 0.6f);
            RefreshSynergies();
            return $"강화 성공! {s.Title}";
        }
        if (roll < ok + bad)
        {
            if (Shields > 0)
            {
                Shields--;
                color = new Color(0.6f, 0.85f, 1f);
                return $"파괴될 뻔했지만 방어권이 막았습니다 ({s.Title}, 방어권 {Shields} 남음)";
            }
            Remove(s);
            color = new Color(1f, 0.35f, 0.35f);
            return $"강화 실패 — {s.Title} 이(가) 부서졌습니다";
        }
        color = new Color(1f, 0.75f, 0.5f);
        return $"강화 실패 — 단계 유지 ({s.Title})";
    }

    private static void Remove(SkillInstance s)
    {
        for (int i = 0; i < Sockets.Length; i++) if (Sockets[i] == s) Sockets[i] = null;
        for (int i = 0; i < Bag.Length; i++) if (Bag[i] == s) Bag[i] = null;
        RefreshSynergies();
    }

    // ═══════════ 특별 강화: a class skill itself (four versions, still to be designed) ═══════════
    public const int SpecialVersions = 4;
    // Each class can name its own four versions per skill (names[Q E R M][version]); the rest stay placeholders
    private static readonly Dictionary<string, (string[][] names, string[][] descs)> specialTables = new Dictionary<string, (string[][], string[][])>();
    public static void RegisterSpecials(string fighter, string[][] names, string[][] descs) => specialTables[fighter] = (names, descs);
    public static bool HasSpecials(string key) => key != null && specialTables.ContainsKey(key.Split('/')[0]);
    public static string SpecialVersionName(string key, int v)
    {
        if (key != null && v >= 0 && specialTables.TryGetValue(key.Split('/')[0], out var tb)) return tb.names[MainIndex(key[key.Length - 1])][v];
        return $"특별 강화 {(v == 0 ? "Ⅰ" : v == 1 ? "Ⅱ" : v == 2 ? "Ⅲ" : "Ⅳ")}";
    }
    public static string SpecialVersionDesc(string key, int v)
    {
        if (key != null && v >= 0 && specialTables.TryGetValue(key.Split('/')[0], out var tb)) return tb.descs[MainIndex(key[key.Length - 1])][v];
        return "효과 준비 중 — 기획이 정해지면 채워집니다";
    }
    private static readonly Dictionary<string, int> mainSpecial = new Dictionary<string, int>();     // class skill key → version
    private static readonly Dictionary<string, List<int>> mainOffers = new Dictionary<string, List<int>>();
    public static int SpecialOf(string key) => key != null && mainSpecial.TryGetValue(key, out int v) ? v : -1;
    public static void SetSpecial(string key, int v) { if (v < 0) mainSpecial.Remove(key); else mainSpecial[key] = v; }

    public static List<int> OffersFor(string key)
    {
        if (!mainOffers.TryGetValue(key, out var o) || o.Count == 0)
            mainOffers[key] = o = RollVersions(null);
        return o;
    }

    private static List<int> RollVersions(List<int> avoid)
    {
        var pool = new List<int> { 0, 1, 2, 3 };
        if (avoid != null && pool.Count - avoid.Count >= 2) pool.RemoveAll(avoid.Contains);
        var o = new List<int>();
        while (o.Count < 2 && pool.Count > 0) { int k = Random.Range(0, pool.Count); o.Add(pool[k]); pool.RemoveAt(k); }
        return o;
    }

    public static bool RerollSpecial(string key)
    {
        if (SpecialRerolls <= 0) return false;
        SpecialRerolls--;
        mainOffers[key] = RollVersions(OffersFor(key));
        return true;
    }

    // Every special upgrade of every class skill taken back (the training room's 초기화)
    public static void ClearSpecials()
    {
        mainSpecial.Clear();
        mainOffers.Clear();
    }

    public static bool ApplySpecial(string key, int version)
    {
        if (SpecialTokens <= 0 || SpecialOf(key) >= 0) return false;
        SpecialTokens--;
        mainSpecial[key] = version;
        mainOffers.Remove(key);
        var w = WeaponSkill(key);
        Toast($"{w?.name} — 특수 강화 「{SpecialVersionName(key, version)}」" + (HasSpecials(key) ? "" : " (효과 준비 중)"), new Color(1f, 0.85f, 0.4f));
        return true;
    }

    // ═══════════ The run ═══════════
    public static void ResetRun()
    {
        for (int i = 0; i < Sockets.Length; i++) Sockets[i] = null;
        for (int i = 0; i < Bag.Length; i++) Bag[i] = null;
        Tickets = MetaProgress.StartTickets;
        RoomsTowardTicket = 0;
        Shields = MetaProgress.StartShields;
        SpecialTokens = MetaProgress.StartSpecialTickets;
        SpecialRerolls = MetaProgress.StartSpecialRerolls;
        RewardRerolls = MetaProgress.StartRewardRerolls;
        ShopRerollsLeft = MetaProgress.ShopRerolls;
        altarRerolls = 0;
        mainSpecial.Clear();
        mainOffers.Clear();
        if (Instance != null) for (int i = 0; i < Instance.alt.Length; i++) Instance.alt[i] = 0;
        pendingLevelUps = 0;
        windUntil = shieldUntil = blessUntil = berserkUntil = classBoonUntil = 0f;
        activeBuffs.Clear();
        staminaFromSupports = 0f;
        // 초반 보조 스킬 (a token upgrade): a few to start with, socketed
        var taken = new HashSet<string>();
        for (int i = 0; i < MetaProgress.StartSupports; i++)
        {
            SkillDef d = SkillCatalog.Random(taken);
            taken.Add(d.id);
            Sockets[i * 2 % Sockets.Length] = new SkillInstance(d);
        }
        Stamina = StaminaMax;
        if (Instance != null) { Instance.salvage = null; Instance.selected = null; Instance.selectedMain = -1; }
        var list = Socketed();
        for (int i = 0; i < SkillCatalog.Synergies.Count; i++) synergyOn[i] = SkillCatalog.Synergies[i].active(list);
    }

    // Everything the player holds
    public static IEnumerable<SkillInstance> Owned()
    {
        foreach (var s in Sockets) if (s != null) yield return s;
        foreach (var s in Bag) if (s != null) yield return s;
    }

    public static HashSet<string> EquippedIds()
    {
        var ids = new HashSet<string>();
        foreach (var s in Owned()) ids.Add(s.def.id);
        return ids;
    }

    public static SkillInstance RandomDrop() => new SkillInstance(SkillCatalog.Random(EquippedIds()));

    // Into the first empty socket (Q, E, R, right click in turn), else the bag
    public static bool Take(SkillInstance s, bool quiet = false)
    {
        if (!quiet) GameAudio.Play("orb_pickup");
        for (int i = 0; i < Sockets.Length; i++)
            if (Sockets[i] == null)
            {
                Sockets[i] = s;
                RefreshSynergies();
                if (!quiet) Toast($"{MainKeys[i / 2]} 보조스킬 — {s.Title} 장착", s.def.Color);
                return true;
            }
        for (int i = 0; i < Bag.Length; i++)
            if (Bag[i] == null)
            {
                Bag[i] = s;
                if (!quiet) Toast($"{s.Title} — 가방에 넣었습니다 [F]", s.def.Color);
                return true;
            }
        return false;
    }

    private static bool BagFull()
    {
        foreach (var s in Sockets) if (s == null) return false;
        foreach (var s in Bag) if (s == null) return false;
        return true;
    }

    // ═══════════ The driver ═══════════
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("SkillSystem");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<SkillSystem>();
    }

    private enum Panel { None, Inventory, Forge, LevelUp }
    private Panel panel;
    private int closedFrame = -1;
    public static bool Blocking => Instance != null && (Instance.panel != Panel.None || Instance.closedFrame == Time.frameCount) || TrainingRoom.PanelOpen;
    public static bool InventoryOpen => Instance != null && Instance.panel == Panel.Inventory;

    private readonly int[] alt = new int[Mains];
    private static int pendingLevelUps;
    public static int PendingLevelUps => pendingLevelUps;
    private static bool LevelUpReady
    {
        get
        {
            Room room = GameManager.Instance != null ? GameManager.Instance.CurrentRoom : null;
            return TrainingRoom.Active || room == null || !room.IsFighting;
        }
    }
    private string forgeResult;
    private Color forgeResultColor;
    private float savedTimeScale = 1f;
    private GUIStyle title, text, small, center, button, prompt, tiny;

    private static PlayerStats player;
    private static PlayerStats Player => player != null ? player : player = FindAnyObjectByType<PlayerStats>();

    private void OnEnable()
    {
        Room.RoomCleared += OnRoomCleared;
        RunState.LeveledUp += OnLeveledUp;
    }

    private void OnDisable()
    {
        Room.RoomCleared -= OnRoomCleared;
        RunState.LeveledUp -= OnLeveledUp;
    }

    private void OnRoomCleared(Room room)
    {
        if (room.RoomKind != Room.Kind.Combat) return;
        if (++RoomsTowardTicket >= RoomsPerTicket)
        {
            RoomsTowardTicket = 0;
            Tickets++;
            Toast("확정 강화권 획득!", new Color(1f, 0.85f, 0.4f));
        }
        // (방 클리어 보조스킬 드랍은 없앰: 보조마법은 레벨 업 보상·보조마법의 제단·상점에서 얻는다)
    }

    private void OnLeveledUp(int level) => pendingLevelUps++;

    // A class skill used: the next of its supports casts with it
    private void CastSupport(int main)
    {
        var list = new List<SkillInstance>();
        if (Sockets[main * 2] != null) list.Add(Sockets[main * 2]);
        if (Sockets[main * 2 + 1] != null) list.Add(Sockets[main * 2 + 1]);
        if (list.Count == 0 || Player == null) return;
        SkillInstance s = list[alt[main]++ % list.Count];
        s.charges = s.MaxCharges;
        StartCoroutine(SkillCast.Cast(this, s, Player.transform, Player));
    }

    private void Update()
    {
        // 기력 comes back a moment after it was last spent
        if (Time.time - staminaUsedAt > 0.6f) Stamina = Mathf.Min(StaminaMax, Stamina + 30f * Time.deltaTime);
        if (Stamina > StaminaMax) Stamina = StaminaMax;
        // 축복: health flows back while it lasts
        if (Time.time < blessUntil && Player != null && (blessHealTick -= Time.deltaTime) <= 0f)
        {
            blessHealTick = 1f;
            Player.Heal(Player.MaxHealth * 0.02f);
        }

        Keyboard kb = Keyboard.current;
        if (panel != Panel.None)
        {
            if (panel == Panel.LevelUp) return;   // (a level-up is chosen, not closed)
            if (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame || kb.iKey.wasPressedThisFrame))
                ClosePanel();
            return;
        }
        if (!GameManager.IsRunActive || Time.timeScale <= 0f || Player == null || Player.IsDead) return;
        if (GameManager.Instance != null && GameManager.Instance.InHub) return;
        if (TrainingRoom.PanelOpen) return;
        AutoPickup();
        if (kb == null) return;
        // a level-up waits: once the room's fight is over, [G] brings up its choices
        if (kb.gKey.wasPressedThisFrame && pendingLevelUps > 0)
        {
            if (LevelUpReady) OpenLevelUp();
            else Toast("전투가 끝난 뒤에 고를 수 있습니다", new Color(1f, 0.6f, 0.5f));
            return;
        }
        if (kb.fKey.wasPressedThisFrame) Interact();
        else if (kb.iKey.wasPressedThisFrame) OpenInventory();
    }

    private void AutoPickup()
    {
        Vector3 p = Player.transform.position;
        foreach (SkillOrb orb in SkillOrb.All.ToArray())
        {
            if (orb == null || orb.price > 0 || orb.altar) continue;
            float d = Flat(orb.transform.position - p);
            if (orb.waitAway) { if (d > 2.6f) orb.waitAway = false; continue; }
            if (d < 1.4f && Take(orb.skill))
                Destroy(orb.gameObject);
        }
        foreach (SkillToken token in SkillToken.All.ToArray())
            if (token != null && token.price == 0 && token.kind != SkillToken.Kind.ShopReroll && Flat(token.transform.position - p) < 1.5f)
                Collect(token);
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    private static void Collect(SkillToken token)
    {
        GameAudio.Play("rune_activate");
        if (token.kind == SkillToken.Kind.Special)
        {
            SpecialTokens++;
            Toast("특별 강화 획득! — [F] 인벤토리에서 강화할 직업 스킬을 누르세요", new Color(1f, 0.85f, 0.4f));
        }
        else
        {
            SpecialRerolls++;
            Toast("특별 강화 새로고침권 획득", new Color(0.6f, 0.9f, 1f));
        }
        Destroy(token.gameObject);
    }

    // [F]: the upgrade anvil, something for sale, the shop's reroll, or else the inventory
    private void Interact()
    {
        Vector3 p = Player.transform.position;
        if (SkillForgeStation.Near(p, 2.8f)) { forgeResult = null; Open(Panel.Forge); return; }
        // the rune altar's gifts: one of the three is taken, the others fade
        SkillOrb gift = SkillOrb.NearestAltar(p, 2.2f);
        if (gift != null)
        {
            if (BagFull()) { Toast("가방이 가득 찼습니다 — 인벤토리에서 정리하세요", new Color(1f, 0.6f, 0.5f)); OpenInventory(); return; }
            Take(gift.skill);
            gift.ClaimAltar();
            return;
        }
        SkillOrb orb = SkillOrb.Nearest(p, 2.2f, true);
        if (orb != null)
        {
            if (BagFull()) { Toast("가방이 가득 찼습니다 — 인벤토리에서 정리하세요", new Color(1f, 0.6f, 0.5f)); OpenInventory(); return; }
            if (!RunState.SpendGold(orb.price)) { Toast("황금이 부족합니다", new Color(1f, 0.5f, 0.4f)); return; }
            Take(orb.skill);
            Destroy(orb.gameObject);
            return;
        }
        SkillToken token = SkillToken.Nearest(p, 2.2f);
        if (token != null && token.kind == SkillToken.Kind.AltarReroll)
        {
            if (!token.HasAltarGifts) { Toast("이미 보조마법을 골랐습니다", new Color(1f, 0.6f, 0.5f)); return; }
            if (!RunState.SpendGold(AltarRerollPrice)) { Toast("황금이 부족합니다", new Color(1f, 0.5f, 0.4f)); return; }
            altarRerolls++;
            token.RerollAltar();
            GameAudio.Play("ui_reroll");
            token.price = AltarRerollPrice;
            Toast($"보조마법을 다시 뽑았습니다 (다음 {AltarRerollPrice} 황금)", new Color(0.8f, 0.65f, 1f));
            return;
        }
        if (token != null && token.kind == SkillToken.Kind.ShopReroll)
        {
            if (ShopRerollsLeft <= 0) { Toast("상점 리롤 횟수가 없습니다 (마을 대장간에서 늘릴 수 있어요)", new Color(1f, 0.6f, 0.5f)); return; }
            ShopRerollsLeft--;
            token.RerollWares();
            GameAudio.Play("ui_reroll");
            Toast($"상점 물건을 바꿨습니다 (남은 리롤 {ShopRerollsLeft})", new Color(0.7f, 0.9f, 1f));
            return;
        }
        if (token != null && token.price > 0)
        {
            if (!RunState.SpendGold(token.price)) { Toast("황금이 부족합니다", new Color(1f, 0.5f, 0.4f)); return; }
            Collect(token);
            return;
        }
        OpenInventory();
    }

    public void OpenInventory()
    {
        selected = null;
        selectedMain = -1;
        Open(Panel.Inventory);
    }

    private void Open(Panel p)
    {
        if (p != Panel.LevelUp) GameAudio.Play(p == Panel.Forge ? "anvil" : "ui_open");
        panel = p;
        savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ClosePanel()
    {
        if (panel == Panel.None) return;
        GameAudio.Play("ui_close");
        if (salvage != null)
        {
            SkillInstance back = salvage;
            salvage = null;
            if (!Take(back, true)) DropToGround(back);
        }
        press = default;
        dragging = false;
        panel = Panel.None;
        closedFrame = Time.frameCount;
        Time.timeScale = savedTimeScale;
    }

    private static void Toast(string msg, Color c)
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        typeof(GameManager).GetMethod("Toast", (System.Reflection.BindingFlags)36)?.Invoke(gm, new object[] { msg, c });
    }

    private static void DropToGround(SkillInstance s)
    {
        if (s == null || Player == null) return;
        Room room = GameManager.Instance != null ? GameManager.Instance.CurrentRoom : null;
        Transform pt = Player.transform;
        Vector3 at = pt.position + Vector3.ProjectOnPlane(pt.forward, Vector3.up).normalized * 1.6f;
        at.y = room != null && !TrainingRoom.Active ? room.transform.position.y : pt.position.y - 1.05f;
        if (room != null && !TrainingRoom.Active) SkillOrb.Create(room.transform, room.transform.InverseTransformPoint(at), s, 0, true);
        else SkillOrb.Create(null, at, s, 0, true);
    }

    // ═══════════ Level-up: upgrade a support, or draw a new one ═══════════
    private enum LevelStep { Choose, Upgrade, Draw, Result }
    private LevelStep levelStep;
    private readonly List<SkillDef> drawOffers = new List<SkillDef>();
    private string levelResult;
    private Color levelResultColor;

    private void OpenLevelUp()
    {
        GameAudio.Play("ui_card");
        pendingLevelUps--;
        levelStep = LevelStep.Choose;
        levelResult = null;
        drawOffers.Clear();
        Open(Panel.LevelUp);
    }

    private void RollDraw()
    {
        GameAudio.Play("ui_card");
        drawOffers.Clear();
        var exclude = EquippedIds();
        int n = 3 + MetaProgress.RewardChoiceBonus;
        for (int i = 0; i < n; i++)
        {
            SkillDef d = SkillCatalog.Random(exclude);
            exclude.Add(d.id);
            drawOffers.Add(d);
        }
    }

    private void FinishLevelUp()
    {
        ClosePanel();
        if (pendingLevelUps > 0) OpenLevelUp();
    }

    // ═══════════ The inventory: four class skills, two sockets each, the bag ═══════════
    private enum Where { None, Socket, Bag, Salvage }
    private struct Cell
    {
        public Where where;
        public int index;
        public Cell(Where w, int i) { where = w; index = i; }
    }

    private SkillInstance salvage;
    private SkillInstance selected;
    private int selectedMain = -1;
    private Cell press;
    private Vector2 pressPos;
    private bool dragging;
    private Rect invBox, salvageRect;
    private readonly Rect[] mainRects = new Rect[Mains];
    private readonly Rect[] socketRects = new Rect[Mains * 2];
    private readonly Rect[] bagRects = new Rect[BagSize];

    private SkillInstance Get(Cell c) => c.where switch
    {
        Where.Socket => Sockets[c.index],
        Where.Bag => Bag[c.index],
        Where.Salvage => salvage,
        _ => null,
    };

    private void Set(Cell c, SkillInstance s)
    {
        switch (c.where)
        {
            case Where.Socket: Sockets[c.index] = s; break;
            case Where.Bag: Bag[c.index] = s; break;
            case Where.Salvage: salvage = s; break;
        }
    }

    private Cell HitTest(Vector2 m)
    {
        for (int i = 0; i < socketRects.Length; i++) if (socketRects[i].Contains(m)) return new Cell(Where.Socket, i);
        for (int i = 0; i < BagSize; i++) if (bagRects[i].Contains(m)) return new Cell(Where.Bag, i);
        if (salvageRect.Contains(m)) return new Cell(Where.Salvage, 0);
        return default;
    }

    private void Drop(Cell from, Vector2 at)
    {
        SkillInstance moving = Get(from);
        if (moving == null) return;
        Cell to = HitTest(at);
        if (to.where != Where.None)
        {
            if (to.where == from.where && to.index == from.index) return;
            SkillInstance there = Get(to);
            Set(to, moving);
            Set(from, there);
        }
        else if (!invBox.Contains(at))
        {
            Set(from, null);
            DropToGround(moving);
            Toast($"{moving.Title} — 바닥에 내려놓았습니다", moving.def.Color);
            if (selected == moving) selected = null;
        }
        RefreshSynergies();
    }

    private void QuickMove(Cell c)
    {
        SkillInstance s = Get(c);
        if (s == null) return;
        if (c.where == Where.Socket)
        {
            int free = System.Array.IndexOf(Bag, null);
            if (free >= 0) { Bag[free] = s; Sockets[c.index] = null; }
        }
        else
        {
            int free = System.Array.IndexOf(Sockets, null);
            if (free >= 0) { Sockets[free] = s; Set(c, null); }
        }
        RefreshSynergies();
    }

    private void DoSalvage()
    {
        if (salvage == null) return;
        int gold = SalvageValue(salvage);
        RunState.AddGold(gold);
        Toast($"{salvage.Title} 분해 — 황금 +{gold}", new Color(1f, 0.85f, 0.4f));
        if (selected == salvage) selected = null;
        salvage = null;
    }

    private void InventoryInput()
    {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1))
        {
            for (int i = 0; i < Mains; i++)
                if (mainRects[i].Contains(e.mousePosition) && e.button == 0) { selectedMain = i; selected = null; e.Use(); return; }
            Cell c = HitTest(e.mousePosition);
            if (c.where == Where.None || Get(c) == null) return;
            if (e.button == 1) { QuickMove(c); e.Use(); return; }
            press = c;
            pressPos = e.mousePosition;
            dragging = false;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && press.where != Where.None)
        {
            if ((e.mousePosition - pressPos).magnitude > 6f) dragging = true;
            e.Use();
        }
        else if (e.type == EventType.MouseUp && e.button == 0 && press.where != Where.None)
        {
            if (dragging) Drop(press, e.mousePosition);
            else { selected = Get(press); selectedMain = -1; }
            press = default;
            dragging = false;
            e.Use();
        }
    }

    // ═══════════ Drawing ═══════════
    private float uiScale = 1f;
    private float VW => Screen.width / uiScale;
    private float VH => Screen.height / uiScale;

    private void Styles()
    {
        if (title != null) return;
        title = UiFont.Style(24, TextAnchor.MiddleCenter, FontStyle.Bold);
        text = UiFont.Style(15, TextAnchor.MiddleLeft, FontStyle.Normal);
        small = UiFont.Style(12, TextAnchor.MiddleLeft, FontStyle.Normal);
        tiny = UiFont.Style(11, TextAnchor.MiddleCenter, FontStyle.Bold);
        center = UiFont.Style(15, TextAnchor.MiddleCenter, FontStyle.Bold);
        prompt = UiFont.Style(17, TextAnchor.MiddleCenter, FontStyle.Bold);
        button = UiArt.ButtonStyle(14);
        text.wordWrap = small.wordWrap = true;
        foreach (var st in new[] { title, text, small, center, prompt, tiny }) st.normal.textColor = Color.white;
    }

    private void OnGUI()
    {
        if (GameManager.Instance == null) return;
        Styles();
        GUI.depth = -60;
        if (panel != Panel.None)
        {
            uiScale = Mathf.Min(1f, Screen.width / 920f, Screen.height / 640f);
            Matrix4x4 keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            UiFont.Fill(new Rect(0f, 0f, VW, VH), new Color(0f, 0f, 0f, 0.6f));
            if (panel == Panel.Inventory) DrawInventory();
            else if (panel == Panel.Forge) DrawForge();
            else DrawLevelUp();
            GUI.matrix = keep;
            return;
        }
        if (SkillBar.Hidden || !GameManager.IsRunActive || Player == null) return;
        if (GameManager.Instance.InHub) return;
        DrawPrompt();
        DrawStatusLine();
    }

    // A soft-edged white disc for the HUD's round marks
    private static Texture2D disc;
    public static Texture2D Disc()
    {
        if (disc != null) return disc;
        const int n = 32;
        disc = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f;
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(n / 2f - Mathf.Sqrt(dx * dx + dy * dy)) * 255f));
            }
        disc.SetPixels32(px);
        disc.Apply();
        return disc;
    }

    public static void DrawDisc(Vector2 c, float radius, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), Disc());
        GUI.color = Color.white;
    }

    // The two supports of a class skill: two dots under its slot on the skill bar, in their elements' colours
    public static void DrawSockets(string key, Rect r)
    {
        int order = SkillBar.Order(key);
        if (order > 3 || GameManager.Instance == null || GameManager.Instance.InHub) return;
        for (int s = 0; s < 2; s++)
        {
            SkillInstance sup = Sockets[order * 2 + s];
            var c = new Vector2(r.center.x + (s == 0 ? -9f : 9f), r.yMax + 10f);
            UiArt.Ring(c, 8f, sup != null ? SkillCatalog.ElementColor(sup.def.element) : (Color?)null);
        }
    }

    // 기력 under the HP bar (GameManager's HUD calls this with the rect)
    public static void DrawStaminaBar(Rect r)
    {
        float k = StaminaMax > 0f ? Mathf.Clamp01(Stamina / StaminaMax) : 0f;
        statusSmall ??= UiFont.Style(11, TextAnchor.MiddleLeft, FontStyle.Bold);
        UiArt.Bar(r, k, k < 0.25f ? new Color(1f, 0.55f, 0.2f) : new Color(0.95f, 0.8f, 0.25f), $"기력 {Stamina:0} / {StaminaMax:0}", statusSmall);
    }

    // 세트 and 시너지: an icon each in a row at the top left; the mouse over one shows what it does
    private static GUIStyle statusSmall, iconLetter, tipStyle;
    public static void DrawSetIcons(Vector2 at)
    {
        iconLetter ??= UiFont.Style(12, TextAnchor.MiddleCenter, FontStyle.Bold);
        tipStyle ??= new GUIStyle(UiFont.Style(12, TextAnchor.UpperLeft, FontStyle.Normal)) { wordWrap = true, richText = true };
        float x = at.x;
        string tip = null;
        Rect tipAt = default;
        Vector2 mouse = Event.current.mousePosition;

        // boons first: the skill's icon in a gold ring, its seconds left over it
        var ended = new List<SkillDef>();
        foreach (var kv in activeBuffs)
        {
            float left = kv.Value - Time.time;
            if (left <= 0f) { ended.Add(kv.Key); continue; }
            Rect tile = new Rect(x, at.y, 30f, 30f);
            UiArt.Ring(tile.center, 15f, new Color(0.08f, 0.07f, 0.1f, 0.95f));
            if (kv.Key.Icon != null) GUI.DrawTexture(new Rect(tile.x + 5f, tile.y + 5f, 20f, 20f), kv.Key.Icon);
            var secs = new Rect(tile.x - 4f, tile.yMax - 4f, tile.width + 8f, 14f);
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(secs.x + 1f, secs.y + 1f, secs.width, secs.height), $"{left:0}초", UiFont.Style(10, TextAnchor.MiddleCenter, FontStyle.Bold));
            GUI.color = left < 2f ? new Color(1f, 0.6f, 0.45f) : Color.white;
            GUI.Label(secs, $"{left:0}초", UiFont.Style(10, TextAnchor.MiddleCenter, FontStyle.Bold));
            GUI.color = Color.white;
            if (tile.Contains(mouse)) { tip = $"<b>{kv.Key.name}</b> — {left:0.0}초 남음\n{kv.Key.desc}"; tipAt = tile; }
            x += 33f;
        }
        foreach (var d in ended) activeBuffs.Remove(d);

        int[] sets = SetCounts();
        for (int e = 0; e < 8; e++)
        {
            if (sets[e] == 0) continue;
            var el = (SkillElement)e;
            Rect tile = new Rect(x, at.y, 30f, 30f);
            UiArt.Emblem(tile, e);
            if (sets[e] > 1)
            {
                var badge = new Rect(tile.xMax - 11f, tile.yMax - 11f, 14f, 14f);
                DrawDisc(badge.center, 7.5f, new Color(0.85f, 0.65f, 0.25f));
                DrawDisc(badge.center, 6f, new Color(0.08f, 0.06f, 0.05f));
                GUI.Label(badge, sets[e].ToString(), UiFont.Style(10, TextAnchor.MiddleCenter, FontStyle.Bold));
            }
            if (tile.Contains(mouse))
            {
                var keys = new List<string>();
                for (int m = 0; m < Mains; m++) if (SetOf(m) == e) keys.Add(MainKeys[m]);
                var (stat, _) = ElementStat(el);
                tip = $"<b>{SkillCatalog.ElementNames[e]} 세트</b>" + (sets[e] > 1 ? $" ×{sets[e]}" : "") +
                      $"\n{string.Join("·", keys)} 스킬의 보조 2칸이 모두 {SkillCatalog.ElementNames[e]}\n{StatText(stat, SetBonus(el) * sets[e])}";
                tipAt = tile;
            }
            x += 33f;
        }
        for (int i = 0; i < SkillCatalog.Synergies.Count; i++)
        {
            if (!synergyOn[i]) continue;
            var syn = SkillCatalog.Synergies[i];
            Rect tile = new Rect(x, at.y, 30f, 30f);
            UiArt.Emblem(tile, -1);
            if (tile.Contains(mouse)) { tip = $"<b>{syn.name}</b>\n{syn.desc}"; tipAt = tile; }
            x += 33f;
        }

        if (tip != null)
        {
            float w = 250f;
            float h = tipStyle.CalcHeight(new GUIContent(tip), w - 36f) + 26f;
            var box = new Rect(tipAt.x, tipAt.yMax + 6f, w, h);
            UiArt.Tip(box);
            tipStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(box.x + 18f, box.y + 12f, w - 36f, h - 18f), tip, tipStyle);
        }
    }

    private void DrawPrompt()
    {
        Vector3 p = Player.transform.position;
        string msg = null;
        SkillOrb orb = SkillOrb.Nearest(p, 2.2f, true);
        SkillToken token = SkillToken.Nearest(p, 2.2f);
        SkillOrb gift = SkillOrb.NearestAltar(p, 2.2f);
        if (SkillForgeStation.Near(p, 2.8f)) msg = "[F] 보조스킬 강화";
        else if (gift != null) msg = $"[F] {gift.skill.Title} 받기 — 셋 중 하나만 (무료)";
        else if (token != null && token.kind == SkillToken.Kind.AltarReroll)
            msg = token.HasAltarGifts ? $"[F] 보조마법 다시 뽑기 — 황금 {AltarRerollPrice}" : "이미 보조마법을 골랐습니다";
        else if (orb != null) msg = $"[F] {orb.skill.Title} 구매 — 황금 {orb.price}";
        else if (token != null && token.kind == SkillToken.Kind.ShopReroll) msg = $"[F] 상점 물건 바꾸기 (남은 리롤 {ShopRerollsLeft})";
        else if (token != null && token.price > 0) msg = $"[F] {token.Label} 구매 — 황금 {token.price}";
        else if (BagFull() && SkillOrb.Nearest(p, 2.2f, false) != null) msg = "가방이 가득 찼습니다 — [F] 인벤토리에서 정리";
        if (msg == null) return;
        var r = new Rect((Screen.width - 480f) / 2f, Screen.height * 0.58f, 480f, 40f);
        UiArt.Tip(r);
        GUI.Label(r, msg, prompt);
    }

    // tickets just above the skill bar (sets and synergies are icons at the top left); a waiting special upgrade above that
    private void DrawStatusLine()
    {
        string line = (pendingLevelUps > 0 ? (LevelUpReady ? $"[G] 레벨 업 보상 고르기 ({pendingLevelUps})   " : $"레벨 업 보상 {pendingLevelUps}개 대기 — 전투가 끝나면 [G]   ") : "") + (Tickets > 0 ? $"확정 강화권 {Tickets}" : "");
        if (line.Length > 0)
        {
            GUI.color = new Color(1f, 0.9f, 0.6f);
            GUI.Label(new Rect(0f, Screen.height - 150f, Screen.width, 20f), line, UiFont.Style(12, TextAnchor.MiddleCenter, FontStyle.Bold));
            GUI.color = Color.white;
        }
        if (SpecialTokens > 0)
        {
            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f);
            string msg = $"★ 특별 강화 {SpecialTokens}개 — [F] 인벤토리에서 강화할 직업 스킬을 누르세요";
            GUIStyle st = UiFont.Style(13, TextAnchor.MiddleCenter, FontStyle.Bold);
            float w = st.CalcSize(new GUIContent(msg)).x + 60f;
            var r = new Rect((Screen.width - w) / 2f, Screen.height - 180f, w, 30f);
            UiArt.Tip(r);
            GUI.color = new Color(1f, 0.85f, 0.35f, pulse);
            GUI.Label(r, msg, st);
            GUI.color = Color.white;
        }
    }

    private Rect Box(float w, float h)
    {
        var r = new Rect((VW - w) / 2f, (VH - h) / 2f, w, h);
        UiArt.Panel(r);
        return r;
    }

    // The window's name on the crimson ribbon over its top edge
    private void Title(Rect box, string name)
    {
        float w = Mathf.Min(box.width * 0.5f, 400f);
        UiArt.Banner(new Rect(box.center.x - w / 2f, box.y - 28f, w, 66f), name, title);
    }

    private static Color SlotTint(SkillInstance s, bool sel) =>
        sel ? new Color(1.25f, 1.2f, 1.1f) : s != null ? Color.Lerp(Color.white, s.def.Color, 0.35f) : new Color(0.55f, 0.52f, 0.5f, 0.9f);

    private void DrawCell(Rect r, SkillInstance s, bool dim, bool sel)
    {
        UiArt.Slot(r, SlotTint(s, sel));
        if (s == null) return;
        GUI.color = dim ? new Color(1f, 1f, 1f, 0.25f) : Color.white;
        if (s.def.Icon != null) GUI.DrawTexture(new Rect(r.x + 6f, r.y + 6f, r.width - 12f, r.height - 12f), s.def.Icon);
        GUI.color = Color.white;
        if (s.level > 0)
        {
            var lv = new Rect(r.xMax - 28f, r.yMax - 16f, 26f, 14f);
            UiFont.Fill(lv, new Color(0f, 0f, 0f, 0.75f));
            GUI.Label(lv, "+" + s.level, tiny);
        }
    }

    private void DrawInventory()
    {
        invBox = Box(890f, 600f);
        Rect box = invBox;
        for (int i = 0; i < Mains; i++)
        {
            mainRects[i] = new Rect(box.x + 30f + i * 126f, box.y + 70f, 112f, 64f);
            for (int s = 0; s < 2; s++)
                socketRects[i * 2 + s] = new Rect(mainRects[i].x + s * 58f, box.y + 142f, 52f, 52f);
        }
        for (int i = 0; i < BagSize; i++)
            bagRects[i] = new Rect(box.x + 30f + (i % 6) * 80f, box.y + 246f + (i / 6) * 80f, 70f, 70f);
        salvageRect = new Rect(box.x + 30f, box.y + 438f, 70f, 70f);
        InventoryInput();

        Title(box, "스킬 · 보조스킬");
        GUI.Label(new Rect(box.x + 20f, box.y + 44f, box.width - 40f, 18f),
            "직업 스킬마다 보조스킬 2칸 · 스킬을 쓸 때마다 번갈아 발동 · 끌어서 장착/교체 · 우클릭 빠른 이동 · 창 밖으로 버리기 · 분해함은 황금", UiFont.Style(12, TextAnchor.MiddleCenter, FontStyle.Normal));

        for (int i = 0; i < Mains; i++)
        {
            Rect r = mainRects[i];
            WeaponSkillInfo w = MainSkill(i);
            bool sel = selectedMain == i;
            bool canSpecial = SpecialTokens > 0 && w != null && SpecialOf(w.Key) < 0;
            Color frame = sel ? new Color(1.25f, 1.2f, 1.1f) : canSpecial ? Color.Lerp(new Color(1f, 0.6f, 0.2f), new Color(1.3f, 1.2f, 0.7f), 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f)) : new Color(0.8f, 0.75f, 0.7f);
            UiArt.Slot(r, frame);
            if (w != null && w.icon != null) GUI.DrawTexture(new Rect(r.x + 6f, r.y + 6f, 52f, 52f), w.icon);
            GUI.Label(new Rect(r.x + 62f, r.y + 2f, r.width - 64f, 18f), MainKeys[i], tiny);
            GUI.Label(new Rect(r.x + 62f, r.y + 20f, r.width - 64f, 40f), w != null ? w.name : "-", UiFont.Style(11, TextAnchor.UpperCenter, FontStyle.Normal));
            if (w != null && SpecialOf(w.Key) >= 0)
            {
                GUI.color = new Color(1f, 0.85f, 0.4f);
                GUI.Label(new Rect(r.x + 2f, r.yMax - 16f, 20f, 14f), "◆", tiny);
                GUI.color = Color.white;
            }
            for (int s = 0; s < 2; s++)
            {
                int idx = i * 2 + s;
                DrawCell(socketRects[idx], Sockets[idx], dragging && press.where == Where.Socket && press.index == idx, Sockets[idx] != null && Sockets[idx] == selected);
                if (Sockets[idx] == null) GUI.Label(socketRects[idx], "보조", tiny);
            }
        }

        int bagCount = 0;
        foreach (var s in Bag) if (s != null) bagCount++;
        GUI.Label(new Rect(box.x + 30f, box.y + 222f, 300f, 18f), $"가방 ({bagCount}/{BagSize})", small);
        for (int i = 0; i < BagSize; i++)
            DrawCell(bagRects[i], Bag[i], dragging && press.where == Where.Bag && press.index == i, Bag[i] != null && Bag[i] == selected);

        GUI.Label(new Rect(salvageRect.x, salvageRect.y - 20f, 200f, 18f), "분해함", small);
        UiArt.Slot(salvageRect, new Color(1.2f, 0.55f, 0.4f));
        if (salvage != null && salvage.def.Icon != null) GUI.DrawTexture(new Rect(salvageRect.x + 6f, salvageRect.y + 6f, salvageRect.width - 12f, salvageRect.height - 12f), salvage.def.Icon);
        if (salvage == null) GUI.Label(salvageRect, "여기로\n끌어오기", tiny);
        else if (UiSound.Button(new Rect(salvageRect.xMax + 14f, salvageRect.y + 6f, 200f, 30f), $"분해 — 황금 +{SalvageValue(salvage)}", button))
            DoSalvage();

        GUI.Label(new Rect(box.x + 330f, box.y + 438f, 210f, 90f),
            $"황금 {RunState.Gold}\n확정 강화권 {Tickets} · 방어권 {Shields}\n특별 강화 {SpecialTokens} · 새로고침권 {SpecialRerolls}\n보상 리롤 {RewardRerolls}", small);

        DrawDetail(new Rect(box.x + 548f, box.y + 70f, 320f, 450f));

        if (UiSound.Button(new Rect(box.xMax - 170f, box.yMax - 46f, 150f, 32f), "닫기 [F / Esc]", button))
            ClosePanel();

        if (dragging && Get(press) != null)
        {
            Vector2 m = Event.current.mousePosition;
            Texture2D icon = Get(press).def.Icon;
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            if (icon != null) GUI.DrawTexture(new Rect(m.x - 30f, m.y - 30f, 60f, 60f), icon);
            GUI.color = Color.white;
            if (!invBox.Contains(m)) GUI.Label(new Rect(m.x - 80f, m.y + 32f, 160f, 18f), "놓으면 바닥에 내려놓기", tiny);
        }
    }

    // A support's stats, or a class skill and its special upgrade
    private void DrawDetail(Rect r)
    {
        UiArt.Inset(r);
        float y = r.y + 10f;
        if (selectedMain >= 0)
        {
            WeaponSkillInfo w = MainSkill(selectedMain);
            if (w == null) return;
            if (w.icon != null) GUI.DrawTexture(new Rect(r.x + 10f, y, 60f, 60f), w.icon);
            GUI.Label(new Rect(r.x + 80f, y, r.width - 90f, 24f), $"{MainKeys[selectedMain]} · {w.name}", text);
            GUI.Label(new Rect(r.x + 80f, y + 26f, r.width - 90f, 30f), $"{w.fighterName}의 직업 스킬", small);
            y += 66f;
            float sup = SupportCost(Sockets[selectedMain * 2]) + SupportCost(Sockets[selectedMain * 2 + 1]);
            GUI.color = new Color(0.95f, 0.82f, 0.35f);
            GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 20f),
                $"기력 {CostOf(selectedMain):0}  ({CostTierNames[(int)ClassTier(selectedMain)]} {ClassCost(selectedMain):0}" + (sup > 0f ? $" + 보조 {sup:0})" : ")"), small);
            GUI.color = Color.white;
            y += 26f;
            int have = SpecialOf(w.Key);
            // 훈련장: every version on offer, free and without limit, changed at will; 초기화 takes them all back
            if (TrainingRoom.Active)
            {
                GUI.color = new Color(1f, 0.85f, 0.4f);
                GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 20f), "★ 특별 강화 (훈련장: 무제한 · 자유 변경)", small);
                GUI.color = Color.white;
                y += 24f;
                for (int v = 0; v < SpecialVersions; v++)
                {
                    var br = new Rect(r.x + 10f, y, r.width - 20f, 54f);
                    bool on = have == v;
                    if (on) UiFont.Fill(new Rect(br.x - 2f, br.y - 2f, br.width + 4f, br.height + 4f), new Color(1f, 0.8f, 0.35f, 0.9f));
                    if (UiSound.Button(br, "", button))
                    {
                        SetSpecial(w.Key, on ? -1 : v);
                        Toast(on ? $"{w.name} — 특별 강화 해제" : $"{w.name} — 특별 강화 「{SpecialVersionName(w.Key, v)}」", new Color(1f, 0.85f, 0.4f));
                    }
                    GUI.color = on ? new Color(1f, 0.95f, 0.6f) : new Color(1f, 0.88f, 0.5f);
                    GUI.Label(new Rect(br.x + 22f, br.y + 3f, br.width - 44f, 20f), (on ? "✔ " : "") + SpecialVersionName(w.Key, v), text);
                    GUI.color = Color.white;
                    var descStyle = UiFont.Style(10, TextAnchor.UpperLeft, FontStyle.Normal);
                    descStyle.wordWrap = true;
                    GUI.Label(new Rect(br.x + 22f, br.y + 22f, br.width - 44f, 32f), SpecialVersionDesc(w.Key, v), descStyle);
                    y += 58f;
                }
                if (UiSound.Button(new Rect(r.x + 10f, y + 2f, r.width - 20f, 30f), "특별 강화 초기화 (모든 직업)", button))
                {
                    ClearSpecials();
                    Toast("특별 강화를 모두 초기화했습니다", new Color(0.7f, 0.85f, 1f));
                }
                return;
            }
            if (have >= 0)
            {
                GUI.color = new Color(1f, 0.85f, 0.45f);
                GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 20f), $"특별 강화 「{SpecialVersionName(w.Key, have)}」", text);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x + 10f, y + 22f, r.width - 20f, 36f), SpecialVersionDesc(w.Key, have), small);
                return;
            }
            if (SpecialTokens <= 0)
            {
                GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 60f), "보스방에서 특별 강화를 얻으면 이 스킬 자체를 강화할 수 있습니다 (한 번에 한 스킬)", small);
                return;
            }
            GUI.color = new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 20f), $"★ 특별 강화 — 방향을 고르세요 (보유 {SpecialTokens})", small);
            GUI.color = Color.white;
            y += 24f;
            List<int> offers = OffersFor(w.Key);
            foreach (int v in offers)
            {
                var br = new Rect(r.x + 10f, y, r.width - 20f, 58f);
                if (UiSound.Button(br, "", button)) { ApplySpecial(w.Key, v); return; }
                GUI.color = new Color(1f, 0.88f, 0.5f);
                GUI.Label(new Rect(br.x + 10f, br.y + 4f, br.width - 20f, 20f), SpecialVersionName(w.Key, v), text);
                GUI.color = Color.white;
                GUI.Label(new Rect(br.x + 10f, br.y + 24f, br.width - 20f, 32f), SpecialVersionDesc(w.Key, v), small);
                y += 64f;
            }
            GUI.enabled = SpecialRerolls > 0;
            if (UiSound.Button(new Rect(r.x + 10f, y + 2f, r.width - 20f, 30f), $"↻ 새로고침 (보유 {SpecialRerolls})", button)) RerollSpecial(w.Key);
            GUI.enabled = true;
            return;
        }
        SkillInstance s = selected;
        bool held = false;
        if (s != null) foreach (var o in Owned()) if (o == s) held = true;
        if (s != null && s == salvage) held = true;
        if (!held) { selected = s = null; }
        if (s == null)
        {
            GUI.Label(new Rect(r.x + 12f, r.y + 12f, r.width - 24f, 80f),
                SpecialTokens > 0 ? "★ 빛나는 직업 스킬을 눌러 특별 강화의 방향을 고르세요" : "보조스킬이나 직업 스킬을 누르면 자세한 정보가 여기에 나옵니다", text);
            return;
        }
        if (s.def.Icon != null) GUI.DrawTexture(new Rect(r.x + 10f, y, 60f, 60f), s.def.Icon);
        GUI.color = s.def.Color;
        GUI.Label(new Rect(r.x + 80f, y, r.width - 90f, 24f), s.Title, text);
        GUI.color = Color.white;
        GUI.Label(new Rect(r.x + 80f, y + 24f, r.width - 90f, 36f), $"{SkillCatalog.ElementNames[(int)s.def.element]} · {SkillCatalog.TagText(s.def.tags)}", small);
        y += 68f;
        GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 40f), "발동: " + s.def.desc, small);
        y += 44f;
        GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 18f), $"피해 배율 ×{s.LevelDamage:0.00}   ·   분해 시 황금 {SalvageValue(s)}", small);
        y += 20f;
        GUI.color = new Color(0.95f, 0.82f, 0.35f);
        GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 18f), $"장착하면 직업 스킬 기력 +{SupportCost(s):0} ({CostTierNames[(int)TierOf(s.def)]})", small);
        GUI.color = Color.white;
        y += 24f;
        GUI.color = new Color(0.7f, 1f, 0.75f);
        GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 18f), "고유 스텟", small);
        GUI.color = Color.white;
        y += 20f;
        foreach (var (stat, v) in StatsOf(s))
        {
            GUI.Label(new Rect(r.x + 18f, y, r.width - 28f, 18f), "· " + StatText(stat, v), small);
            y += 18f;
        }
        y += 8f;
        string en = SkillCatalog.ElementNames[(int)s.def.element];
        GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 36f),
            $"{en} 세트: 한 스킬의 보조 2칸이 모두 {en}이면 {StatText(ElementStat(s.def.element).stat, SetBonus(s.def.element))}", small);
    }

    // ── 강화방: supports only, as many times as the gold lasts ──
    private Vector2 forgeScroll;

    private void DrawForge()
    {
        Rect box = Box(820f, 600f);
        Title(box, "브로크의 모루");
        GUI.Label(new Rect(box.x, box.y + 44f, box.width, 20f),
            $"황금 {RunState.Gold}  ·  확정 강화권 {Tickets}  ·  방어권 {Shields}  ·  방 {RoomsPerTicket - RoomsTowardTicket}개 더 클리어하면 강화권 1장", center);
        var list = new List<SkillInstance>(Owned());
        if (list.Count == 0)
            GUI.Label(new Rect(box.x, box.y + 200f, box.width, 30f), "강화할 보조스킬이 없습니다", center);
        float rowH = 58f;
        var view = new Rect(box.x + 16f, box.y + 72f, box.width - 32f, 420f);
        var content = new Rect(0f, 0f, view.width - 20f, Mathf.Max(view.height, list.Count * rowH));
        forgeScroll = GUI.BeginScrollView(view, forgeScroll, content);
        for (int i = 0; i < list.Count; i++)
        {
            SkillInstance s = list[i];
            var r = new Rect(0f, i * rowH, content.width, rowH - 6f);
            UiArt.Inset(r, 0.3f);
            var iconCell = new Rect(r.x + 6f, r.y + 4f, 44f, 44f);
            UiArt.Slot(iconCell, SlotTint(s, false));
            if (s.def.Icon != null) GUI.DrawTexture(new Rect(iconCell.x + 4f, iconCell.y + 4f, 36f, 36f), s.def.Icon);
            int si = System.Array.IndexOf(Sockets, s);
            GUI.color = s.def.Color;
            GUI.Label(new Rect(r.x + 58f, r.y + 4f, 220f, 22f), s.Title, text);
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 58f, r.y + 26f, 220f, 18f), si >= 0 ? $"{MainKeys[si / 2]} 보조" : "가방", small);
            float ok = SuccessChance(s), bad = DestroyChance(s);
            GUI.Label(new Rect(r.x + 280f, r.y + 6f, 190f, 40f), $"성공 {ok * 100f:0}%  유지 {(1f - ok - bad) * 100f:0}%\n파괴 {bad * 100f:0}%", small);
            if (UiSound.Button(new Rect(r.xMax - 300f, r.y + 8f, 160f, 36f), $"강화 — 황금 {UpgradeCost(s)}", button))
            {
                if (!RunState.SpendGold(UpgradeCost(s))) { forgeResult = "황금이 부족합니다"; forgeResultColor = new Color(1f, 0.5f, 0.4f); }
                else forgeResult = RollUpgrade(s, out forgeResultColor);
            }
            GUI.enabled = Tickets > 0;
            if (UiSound.Button(new Rect(r.xMax - 132f, r.y + 8f, 126f, 36f), "확정 강화권", button))
            {
                Tickets--;
                s.level++;
                RefreshSynergies();
                forgeResult = $"확정 강화 성공! {s.Title}";
                forgeResultColor = new Color(1f, 0.85f, 0.4f);
            }
            GUI.enabled = true;
        }
        GUI.EndScrollView();
        if (forgeResult != null)
        {
            GUI.color = forgeResultColor;
            GUI.Label(new Rect(box.x, box.y + 500f, box.width, 24f), forgeResult, center);
            GUI.color = Color.white;
        }
        if (UiSound.Button(new Rect(box.x + 20f, box.yMax - 48f, 230f, 34f), $"확정 강화권 구매 — 황금 {TicketPrice}", button))
        {
            if (RunState.SpendGold(TicketPrice)) { Tickets++; forgeResult = "확정 강화권을 샀습니다"; forgeResultColor = new Color(1f, 0.85f, 0.4f); }
            else { forgeResult = "황금이 부족합니다"; forgeResultColor = new Color(1f, 0.5f, 0.4f); }
        }
        if (UiSound.Button(new Rect(box.xMax - 170f, box.yMax - 48f, 150f, 34f), "나가기 [Esc]", button))
            ClosePanel();
    }

    // What's written on one of the two level-up choice cards
    private void ChoiceCardBody(Rect r, string name, string desc, SkillElement element, List<SkillInstance> show)
    {
        float y = r.y + r.height * 0.12f;
        if (show != null && show.Count > 0)
        {
            // the supports it could upgrade, in small slots
            int n = Mathf.Min(3, show.Count);
            float cs = 46f, gap = 6f, x = r.center.x - (n * cs + (n - 1) * gap) / 2f;
            for (int i = 0; i < n; i++)
            {
                var cell = new Rect(x + i * (cs + gap), y + 10f, cs, cs);
                UiArt.Slot(cell, SlotTint(show[i], false));
                if (show[i].def.Icon != null) GUI.DrawTexture(new Rect(cell.x + 5f, cell.y + 5f, cs - 10f, cs - 10f), show[i].def.Icon);
            }
        }
        else UiArt.Emblem(new Rect(r.center.x - 34f, y, 68f, 68f), (int)element);
        GUI.color = new Color(1f, 0.86f, 0.5f);
        GUI.Label(new Rect(r.x + 10f, y + 82f, r.width - 20f, 26f), name, UiFont.Style(17, TextAnchor.MiddleCenter, FontStyle.Bold));
        GUI.color = Color.white;
        var st = UiFont.Style(12, TextAnchor.UpperCenter, FontStyle.Normal);
        st.wordWrap = true;
        GUI.Label(new Rect(r.x + 22f, y + 116f, r.width - 44f, r.yMax - y - 140f), desc, st);
    }

    // ── 레벨 업: upgrade one support, or draw a new one ──
    private void DrawLevelUp()
    {
        Rect box = Box(760f, 520f);
        Title(box, $"레벨 업!  Lv {RunState.Level}");
        var owned = new List<SkillInstance>(Owned());
        switch (levelStep)
        {
            case LevelStep.Choose:
            {
                GUI.Label(new Rect(box.x, box.y + 58f, box.width, 22f), "보상을 하나 고르세요", center);
                // two cards: upgrade a support you hold, or draw a new one
                var left = new Rect(box.center.x - 250f, box.y + 96f, 220f, 330f);
                var right = new Rect(box.center.x + 30f, box.y + 96f, 220f, 330f);
                GUI.enabled = owned.Count > 0;
                if (UiArt.Card(left)) levelStep = LevelStep.Upgrade;
                ChoiceCardBody(left, "보조스킬 강화", "가진 보조스킬 하나를 강화합니다\n\n강화방과 같은 확률\n파괴될 수 있음 (방어권 " + Shields + ")", owned.Count > 0 ? owned[0].def.element : (SkillElement)(-1), owned);
                GUI.enabled = true;
                if (UiArt.Card(right)) { RollDraw(); levelStep = LevelStep.Draw; }
                ChoiceCardBody(right, "새 보조스킬 뽑기", $"보조스킬 {3 + MetaProgress.RewardChoiceBonus}개 중\n하나를 고릅니다\n\n보상 리롤 {RewardRerolls}", (SkillElement)(-1), null);
                if (owned.Count == 0)
                    GUI.Label(new Rect(left.x, left.yMax + 4f, left.width, 18f), "가진 보조스킬이 없습니다", UiFont.Style(12, TextAnchor.MiddleCenter, FontStyle.Normal));
                break;
            }
            case LevelStep.Upgrade:
                GUI.Label(new Rect(box.x, box.y + 58f, box.width, 22f), $"강화할 보조스킬을 고르세요  ·  방어권 {Shields}", center);
                for (int i = 0; i < owned.Count && i < 20; i++)
                {
                    SkillInstance s = owned[i];
                    var r = new Rect(box.x + 30f + (i % 4) * 176f, box.y + 90f + (i / 4) * 74f, 166f, 66f);
                    bool hover = r.Contains(Event.current.mousePosition);
                    UiArt.Inset(r, hover ? 0.55f : 0.35f);
                    if (hover) UiFont.Fill(r, new Color(1f, 0.8f, 0.35f, 0.1f));
                    if (UiSound.Button(r, "", GUIStyle.none))
                    {
                        levelResult = RollUpgrade(s, out levelResultColor);
                        levelStep = LevelStep.Result;
                    }
                    var cell = new Rect(r.x + 6f, r.y + 6f, 54f, 54f);
                    UiArt.Slot(cell, SlotTint(s, hover));
                    if (s.def.Icon != null) GUI.DrawTexture(new Rect(cell.x + 5f, cell.y + 5f, 44f, 44f), s.def.Icon);
                    GUI.Label(new Rect(r.x + 64f, r.y + 4f, r.width - 68f, 22f), s.Title, small);
                    GUI.Label(new Rect(r.x + 64f, r.y + 26f, r.width - 68f, 36f), $"성공 {SuccessChance(s) * 100f:0}%\n파괴 {DestroyChance(s) * 100f:0}%", small);
                }
                if (UiSound.Button(new Rect(box.x + 30f, box.yMax - 50f, 120f, 34f), "← 뒤로", button)) levelStep = LevelStep.Choose;
                break;
            case LevelStep.Draw:
                GUI.Label(new Rect(box.x, box.y + 58f, box.width, 22f), "하나를 고르면 빈 보조 칸(없으면 가방)에 들어갑니다", center);
                float cw = Mathf.Min(176f, (box.width - 60f) / Mathf.Max(1, drawOffers.Count) - 12f);
                float startX = box.center.x - (drawOffers.Count * (cw + 12f) - 12f) / 2f;
                for (int i = 0; i < drawOffers.Count; i++)
                {
                    SkillDef d = drawOffers[i];
                    var r = new Rect(startX + i * (cw + 12f), box.y + 92f, cw, Mathf.Max(cw / 0.62f, 320f));
                    if (UiArt.Card(r))
                    {
                        var s = new SkillInstance(d);
                        if (!Take(s)) DropToGround(s);
                        FinishLevelUp();
                        return;
                    }
                    float ix = r.center.x - cw * 0.26f, iw = cw * 0.52f;
                    var cell = new Rect(ix, r.y + r.height * 0.13f, iw, iw);
                    UiArt.Slot(cell, Color.Lerp(Color.white, d.Color, 0.35f));
                    if (d.Icon != null) GUI.DrawTexture(new Rect(cell.x + 6f, cell.y + 6f, cell.width - 12f, cell.height - 12f), d.Icon);
                    float y = cell.yMax + 6f;
                    GUI.color = d.Color;
                    GUI.Label(new Rect(r.x + 10f, y, r.width - 20f, 22f), d.name, center);
                    GUI.color = Color.white;
                    UiArt.Emblem(new Rect(r.center.x - 30f, y + 24f, 16f, 16f), (int)d.element);
                    GUI.Label(new Rect(r.center.x - 12f, y + 22f, 60f, 20f), SkillCatalog.ElementNames[(int)d.element], UiFont.Style(12, TextAnchor.MiddleLeft, FontStyle.Bold));
                    var st = StatsOf(new SkillInstance(d));
                    GUI.color = new Color(0.75f, 1f, 0.8f);
                    GUI.Label(new Rect(r.x + 16f, y + 46f, r.width - 32f, 36f), StatText(st[0].stat, st[0].value) + "\n" + StatText(st[1].stat, st[1].value), UiFont.Style(11, TextAnchor.UpperCenter, FontStyle.Normal));
                    GUI.color = new Color(0.95f, 0.82f, 0.35f);
                    GUI.Label(new Rect(r.x + 16f, y + 82f, r.width - 32f, 18f), $"기력 +{SupportCosts[(int)TierOf(d)]:0}", UiFont.Style(11, TextAnchor.UpperCenter, FontStyle.Bold));
                    GUI.color = Color.white;
                    var descStyle = UiFont.Style(11, TextAnchor.UpperCenter, FontStyle.Normal);
                    descStyle.wordWrap = true;
                    GUI.Label(new Rect(r.x + 16f, y + 102f, r.width - 32f, r.yMax - y - 118f), d.desc, descStyle);
                }
                GUI.enabled = RewardRerolls > 0;
                if (UiSound.Button(new Rect(box.center.x - 130f, box.yMax - 50f, 260f, 34f), $"↻ 다시 뽑기 (보상 리롤 {RewardRerolls})", button))
                {
                    RewardRerolls--;
                    GameAudio.Play("ui_reroll");
                    RollDraw();
                }
                GUI.enabled = true;
                if (UiSound.Button(new Rect(box.x + 30f, box.yMax - 50f, 120f, 34f), "← 뒤로", button)) levelStep = LevelStep.Choose;
                break;
            case LevelStep.Result:
                GUI.color = levelResultColor;
                GUI.Label(new Rect(box.x, box.y + 200f, box.width, 30f), levelResult, center);
                GUI.color = Color.white;
                if (UiSound.Button(new Rect(box.center.x - 80f, box.yMax - 60f, 160f, 36f), "확인", button)) FinishLevelUp();
                break;
        }
    }
}

// ═══════════ World objects ═══════════

// A support skill's orb on the floor (free, or for sale in the shop)
public class SkillOrb : MonoBehaviour
{
    public SkillInstance skill;
    public int price;
    public bool waitAway;
    public bool altar;   // a rune altar's gift (보조마법 방): free, taken with [F], one of three
    private static readonly List<SkillOrb> all = new List<SkillOrb>();

    public static SkillOrb NearestAltar(Vector3 p, float within)
    {
        SkillOrb best = null;
        float bestD = within;
        foreach (var o in all)
        {
            if (o == null || !o.altar) continue;
            Vector3 d = o.transform.position - p;
            d.y = 0f;
            if (d.magnitude < bestD) { bestD = d.magnitude; best = o; }
        }
        return best;
    }

    // Taken: the other gifts on this altar fade away
    public void ClaimAltar()
    {
        Transform room = transform.parent;
        if (room != null)
            foreach (SkillOrb o in room.GetComponentsInChildren<SkillOrb>())
                if (o != this && o.altar)
                    Destroy(o.gameObject);
        Destroy(gameObject);
    }
    public static List<SkillOrb> All => all;
    private Vector3 home;

    public static SkillOrb Create(Transform parent, Vector3 local, SkillInstance skill, int price, bool dropped = false)
    {
        var go = new GameObject("SkillOrb_" + skill.def.id);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var o = go.AddComponent<SkillOrb>();
        o.skill = skill;
        o.price = price;
        o.waitAway = dropped;
        o.home = go.transform.position + Vector3.up * 1.1f;
        Color c = skill.def.Color;
        VfxPiece look = Vfx.Sprite("sk_orbitem", o.home, 1.3f, Color.Lerp(Color.white, c, 0.45f) * 1.4f, 0f, 1f, 1f, 0f);
        look.transform.SetParent(go.transform, true);
        Vfx.Glow(go.transform, Vector3.up * 1.1f, 2.2f, c * 0.7f, "glow", 0f);
        Vfx.Decal(go.transform.position + Vector3.up * 0.05f, 1.6f, c * 0.8f, "runes", 30f).transform.SetParent(go.transform, true);
        return o;
    }

    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    private void Update()
    {
        Transform look = transform.childCount > 0 ? transform.GetChild(0) : null;
        if (look != null) look.position = home + Vector3.up * Mathf.Sin(Time.time * 2f) * 0.12f;
    }

    public static SkillOrb Nearest(Vector3 p, float within, bool forSale)
    {
        SkillOrb best = null;
        float bestD = within;
        foreach (var o in all)
        {
            if (o == null || (o.price > 0) != forSale) continue;
            Vector3 d = o.transform.position - p;
            d.y = 0f;
            if (d.magnitude < bestD) { bestD = d.magnitude; best = o; }
        }
        return best;
    }

    private void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null || SkillBar.Hidden) return;
        Vector3 sp = cam.WorldToScreenPoint(home + Vector3.up * 0.9f);
        if (sp.z <= 0f) return;
        var style = UiFont.Style(13, TextAnchor.MiddleCenter, FontStyle.Bold);
        style.normal.textColor = Color.Lerp(Color.white, skill.def.Color, 0.5f);
        string label = "보조: " + skill.Title + (price > 0 ? $"  ({price}G)" : "");
        var r = new Rect(sp.x - 100f, Screen.height - sp.y - 10f, 200f, 20f);
        UiFont.Fill(new Rect(r.x + 20f, r.y, r.width - 40f, r.height), new Color(0f, 0f, 0f, 0.5f));
        GUI.Label(r, label, style);
    }
}

// The upgrade room's anvil (강화방)
public class SkillForgeStation : MonoBehaviour
{
    private static readonly List<SkillForgeStation> all = new List<SkillForgeStation>();

    public static SkillForgeStation Create(Transform parent, Vector3 local)
    {
        var go = new GameObject("SkillForge");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var f = go.AddComponent<SkillForgeStation>();
        Color ember = new Color(1f, 0.6f, 0.25f);
        Vfx.Decal(go.transform.position + Vector3.up * 0.05f, 3.4f, ember, "runes", 20f).transform.SetParent(go.transform, true);
        SkillCast.Fx("sk_field", go.transform.position + Vector3.up * 0.07f, 4.2f, ember * 0.6f, 0f, Quaternion.Euler(90f, 0f, 0f), true, 0.4f, 1f, 1f, 15f).transform.SetParent(go.transform, true);
        SkillCast.Fx("sk_aura", go.transform.position + Vector3.up * 1.2f, 2.6f, ember * 0.7f, 0f, null, true, 0.3f, 1f, 1f).transform.SetParent(go.transform, true);
        Vfx.Glow(go.transform, Vector3.up * 1.2f, 3f, ember * 0.8f, "glow", 0f);
        return f;
    }

    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    public static bool Near(Vector3 p, float within)
    {
        foreach (var f in all)
        {
            if (f == null) continue;
            Vector3 d = f.transform.position - p;
            d.y = 0f;
            if (d.magnitude < within) return true;
        }
        return false;
    }
}

// A token on the floor: a boss's special upgrade (특별 강화), its reroll (새로고침권), or the shop's reroll stone
public class SkillToken : MonoBehaviour
{
    // AltarReroll: the rune altar's stone (보조마법 방) - pay gold to draw the altar's three gifts again
    public enum Kind { Special, Reroll, ShopReroll, AltarReroll }
    public Kind kind;
    public int price;
    private static readonly List<SkillToken> all = new List<SkillToken>();
    public static List<SkillToken> All => all;
    private Vector3 home;

    public string Label => kind == Kind.Special ? "특별 강화" : kind == Kind.Reroll ? "특별 강화 새로고침권" : kind == Kind.AltarReroll ? "보조마법 다시 뽑기" : "상점 물건 바꾸기";

    // The altar's gifts still waiting (none once one was taken)
    public bool HasAltarGifts
    {
        get
        {
            Transform room = transform.parent;
            if (room == null) return false;
            foreach (SkillOrb o in room.GetComponentsInChildren<SkillOrb>())
                if (o.altar) return true;
            return false;
        }
    }

    public void RerollAltar()
    {
        Transform room = transform.parent;
        if (room == null) return;
        var exclude = SkillSystem.EquippedIds();
        foreach (SkillOrb o in room.GetComponentsInChildren<SkillOrb>())
        {
            if (!o.altar) continue;
            SkillDef d = SkillCatalog.Random(exclude);
            exclude.Add(d.id);
            SkillOrb.Create(room, room.InverseTransformPoint(o.transform.position), new SkillInstance(d), 0).altar = true;
            Destroy(o.gameObject);
        }
    }

    public static SkillToken Create(Transform parent, Vector3 local, Kind kind, int price)
    {
        var go = new GameObject("SkillToken_" + kind);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var t = go.AddComponent<SkillToken>();
        t.kind = kind;
        t.price = price;
        t.home = go.transform.position + Vector3.up * 1.1f;
        Color c = kind == Kind.Special ? new Color(1f, 0.82f, 0.4f) : kind == Kind.Reroll ? new Color(0.5f, 0.85f, 1f)
            : kind == Kind.AltarReroll ? new Color(0.78f, 0.55f, 1f) : new Color(0.6f, 1f, 0.7f);
        VfxPiece look = Vfx.Sprite("sk_orbitem", t.home, kind == Kind.Special ? 1.5f : 1.1f, Color.Lerp(Color.white, c, 0.6f) * 1.4f, 0f, 1f, 1f, 90f);
        look.transform.SetParent(go.transform, true);
        Vfx.Glow(go.transform, Vector3.up * 1.1f, 2.6f, c * 0.8f, "glow", 0f);
        Vfx.Decal(go.transform.position + Vector3.up * 0.05f, kind == Kind.Special ? 2.6f : 1.6f, c, "runes", 40f).transform.SetParent(go.transform, true);
        if (kind == Kind.Special)
            SkillCast.Fx("sk_pillar", go.transform.position + Vector3.up * 2.4f, 4.4f, c * 0.7f, 0f, null, true, 0.3f, 1f, 1f).transform.SetParent(go.transform, true);
        return t;
    }

    // The shop's wares (the priced orbs beside this stone) swapped for new ones
    public void RerollWares()
    {
        Transform room = transform.parent;
        if (room == null) return;
        var exclude = SkillSystem.EquippedIds();
        foreach (SkillOrb o in room.GetComponentsInChildren<SkillOrb>())
        {
            if (o.price <= 0) continue;
            SkillDef d = SkillCatalog.Random(exclude);
            exclude.Add(d.id);
            SkillOrb.Create(room, room.InverseTransformPoint(o.transform.position), new SkillInstance(d), o.price);
            Destroy(o.gameObject);
        }
    }

    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    private void Update()
    {
        Transform look = transform.childCount > 0 ? transform.GetChild(0) : null;
        if (look != null) look.position = home + Vector3.up * Mathf.Sin(Time.time * 2.4f) * 0.14f;
    }

    public static SkillToken Nearest(Vector3 p, float within)
    {
        SkillToken best = null;
        float bestD = within;
        foreach (var t in all)
        {
            if (t == null) continue;
            Vector3 d = t.transform.position - p;
            d.y = 0f;
            if (d.magnitude < bestD) { bestD = d.magnitude; best = t; }
        }
        return best;
    }

    private void OnGUI()
    {
        Camera cam = Camera.main;
        if (cam == null || SkillBar.Hidden) return;
        Vector3 sp = cam.WorldToScreenPoint(home + Vector3.up * 0.9f);
        if (sp.z <= 0f) return;
        var style = UiFont.Style(13, TextAnchor.MiddleCenter, FontStyle.Bold);
        style.normal.textColor = kind == Kind.Special ? new Color(1f, 0.88f, 0.5f) : new Color(0.7f, 0.95f, 1f);
        string label = (kind == Kind.Special ? "★ " : "↻ ") + Label + (price > 0 ? $"  ({price}G)" : kind == Kind.ShopReroll ? $"  (남은 {SkillSystem.ShopRerollsLeft})" : "");
        var r = new Rect(sp.x - 110f, Screen.height - sp.y - 10f, 220f, 20f);
        UiFont.Fill(new Rect(r.x + 20f, r.y, r.width - 40f, r.height), new Color(0f, 0f, 0f, 0.5f));
        GUI.Label(r, label, style);
    }
}
