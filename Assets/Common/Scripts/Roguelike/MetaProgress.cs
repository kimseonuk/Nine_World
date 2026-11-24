using System;
using UnityEngine;

// What carries over between runs: tokens (one per level reached when a run ends — they stay through every death)
// and the upgrades Brokkr forges with them. Saved in PlayerPrefs.
// The first time this version runs every upgrade starts fully bought (as asked, for testing); 초기화 (Reset) takes
// them all back and returns the tokens they cost.
// Permanent upgrades never raise damage, attack speed, move speed or damage taken — a run's strength comes from the run
// (skill tree, supports, upgrades). They only give a little more health and stamina, and the start/run helps.
public static class MetaProgress
{
    public enum Group { Stats, Start, Run, Weapons }
    public static readonly string[] GroupNames = { "능력치", "시작 지원", "여정 보너스", "무기 해금" };

    public class Upgrade
    {
        public string id;
        public string name;
        public string effect;   // per level, e.g. "공격력 +1"
        public int maxLevel;
        public int baseCost;    // a level costs baseCost × (level + 1)
        public Group group;
    }

    public static readonly Upgrade[] Upgrades =
    {
        // ── 능력치 ──
        new Upgrade { id = "mail",    name = "사슬 갑옷",        effect = "최대 체력 +5",        maxLevel = MailLevels, baseCost = 2, group = Group.Stats },
        new Upgrade { id = "stamina", name = "강철 폐",          effect = "최대 기력 +5",        maxLevel = StaminaLevels, baseCost = 2, group = Group.Stats },
        // ── 시작 지원 ──
        new Upgrade { id = "purse",   name = "드베르그의 주머니", effect = "시작 황금 +20",      maxLevel = 5,  baseCost = 1, group = Group.Start },
        new Upgrade { id = "sreroll", name = "운명의 실",        effect = "시작 보상 리롤 +1",   maxLevel = 5,  baseCost = 2, group = Group.Start },
        new Upgrade { id = "ssupport",name = "견습 룬술사",      effect = "시작 보조스킬 +1",    maxLevel = 3,  baseCost = 4, group = Group.Start },
        new Upgrade { id = "sticket", name = "장인의 인장",      effect = "시작 확정 강화권 +1", maxLevel = 3,  baseCost = 3, group = Group.Start },
        new Upgrade { id = "shield",  name = "수호 룬",          effect = "파괴 방어권 +1",      maxLevel = 3,  baseCost = 3, group = Group.Start },
        new Upgrade { id = "sspecial",name = "오딘의 눈",        effect = "특별 강화 1회권 +1",  maxLevel = 2,  baseCost = 6, group = Group.Start },
        // ── 여정 보너스 ──
        new Upgrade { id = "discount",name = "단골 손님",        effect = "강화 비용 -5%",       maxLevel = 6,  baseCost = 2, group = Group.Run },
        new Upgrade { id = "chance",  name = "숙련된 망치질",    effect = "강화 성공 확률 +3%",  maxLevel = 5,  baseCost = 3, group = Group.Run },
        new Upgrade { id = "shopre",  name = "상인의 친구",      effect = "상점 리롤 +1",        maxLevel = 3,  baseCost = 2, group = Group.Run },
        new Upgrade { id = "choice",  name = "넓은 시야",        effect = "보상 선택지 +1",      maxLevel = 2,  baseCost = 5, group = Group.Run },
        new Upgrade { id = "heal",    name = "치유사의 축복",    effect = "회복 아이템 +10%",    maxLevel = 5,  baseCost = 2, group = Group.Run },
        new Upgrade { id = "gold",    name = "황금 손",          effect = "금화 획득 +10%",      maxLevel = 5,  baseCost = 2, group = Group.Run },
        new Upgrade { id = "spreroll",name = "노른의 실타래",    effect = "특별 강화 리롤 +1",   maxLevel = 3,  baseCost = 3, group = Group.Run },
    };

    private const string TokensKey = "meta_tokens";
    private const string InitKey = "meta_v2_init";
    private const string StatsTrimKey = "meta_v3_stats_trim";
    private const int MailLevels = 5, StaminaLevels = 5;
    // (upgrades taken out: their levels are given back as tokens once, see EnsureInit)
    private static readonly (string id, int baseCost)[] Retired = { ("edge", 2), ("grip", 3), ("boots", 2), ("rim", 3) };
    private static string LevelKey(string id) => "meta_up_" + id;
    private static string UnlockKey(string fighter) => "meta_unlock_" + fighter;
    public const int WeaponUnlockCost = 5;

    public static event Action Changed;

    public static int Tokens => PlayerPrefs.GetInt(TokensKey, 0);
    public static int LevelOf(string id) { EnsureInit(); return PlayerPrefs.GetInt(LevelKey(id), 0); }
    public static int CostOf(Upgrade u) => u.baseCost * (LevelOf(u.id) + 1);
    public static bool IsMaxed(Upgrade u) => LevelOf(u.id) >= u.maxLevel;
    public static bool CanBuy(Upgrade u) => !IsMaxed(u) && Tokens >= CostOf(u);

    // ── What the upgrades add up to ──
    // (no permanent damage, attack speed, move speed or protection any more: kept at no effect for their callers)
    public static int DamageBonus => 0;
    public static float MaxHealthBonus => Mathf.Min(LevelOf("mail"), MailLevels) * 5f;
    public static float StaminaBonus => Mathf.Min(LevelOf("stamina"), StaminaLevels) * 5f;
    public static float AttackSpeedMultiplier => 1f;
    public static float MoveSpeedMultiplier => 1f;
    public static float DamageTakenMultiplier => 1f;
    public static int StartGold => LevelOf("purse") * 20;
    public static int StartRewardRerolls => LevelOf("sreroll");
    public static int StartSupports => LevelOf("ssupport");
    public static int StartTickets => LevelOf("sticket");
    public static int StartShields => LevelOf("shield");
    public static int StartSpecialTickets => LevelOf("sspecial");
    public static float UpgradeCostMultiplier => 1f - LevelOf("discount") * 0.05f;
    public static float UpgradeChanceBonus => LevelOf("chance") * 0.03f;
    public static int ShopRerolls => LevelOf("shopre");
    public static int RewardChoiceBonus => LevelOf("choice");
    public static float HealBonus => LevelOf("heal") * 0.1f;
    public static float GoldMultiplier => 1f + LevelOf("gold") * 0.1f;
    public static int StartSpecialRerolls => LevelOf("spreroll");

    public static string Current(Upgrade u)
    {
        int l = LevelOf(u.id);
        switch (u.id)
        {
            case "mail": return $"최대 체력 +{MaxHealthBonus:0}";
            case "stamina": return $"최대 기력 +{StaminaBonus:0}";
            case "purse": return $"시작 황금 +{StartGold}";
            case "discount": return $"강화 비용 -{l * 5}%";
            case "chance": return $"강화 성공 +{l * 3}%";
            case "heal": return $"회복 아이템 +{l * 10}%";
            case "gold": return $"금화 +{l * 10}%";
            default: return $"+{l}";
        }
    }

    // ── 무기 해금: the first fighter is always open, the rest for tokens ──
    public static bool IsUnlocked(CharacterProfile fighter, int index)
    {
        EnsureInit();
        if (unlockAllPending) UnlockAllIfPending(UnityEngine.Object.FindAnyObjectByType<CharacterSwitcher>());
        return index == 0 || fighter == null || PlayerPrefs.GetInt(UnlockKey(fighter.name), 0) == 1;
    }

    public static bool UnlockWeapon(CharacterProfile fighter)
    {
        if (fighter == null || Tokens < WeaponUnlockCost) return false;
        PlayerPrefs.SetInt(TokensKey, Tokens - WeaponUnlockCost);
        PlayerPrefs.SetInt(UnlockKey(fighter.name), 1);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return true;
    }

    // The first run of this version: every upgrade and every weapon starts bought
    private static bool inited;
    private static void EnsureInit()
    {
        if (inited) return;
        inited = true;
        TrimStats();
        if (PlayerPrefs.GetInt(InitKey, 0) == 1) return;
        foreach (var u in Upgrades)
            PlayerPrefs.SetInt(LevelKey(u.id), Mathf.Max(PlayerPrefs.GetInt(LevelKey(u.id), 0), u.maxLevel));
        var sw = UnityEngine.Object.FindAnyObjectByType<CharacterSwitcher>();
        if (sw != null && sw.Characters != null)
        {
            foreach (var c in sw.Characters)
                if (c != null) PlayerPrefs.SetInt(UnlockKey(c.name), 1);
        }
        else unlockAllPending = true;
        PlayerPrefs.SetInt(InitKey, 1);
        PlayerPrefs.Save();
    }
    private static bool unlockAllPending;

    // Once: the retired stat upgrades, and health/stamina levels past the new caps, are given back as tokens
    private static void TrimStats()
    {
        if (PlayerPrefs.GetInt(StatsTrimKey, 0) == 1) return;
        int refund = 0;
        foreach (var (id, baseCost) in Retired)
        {
            int l = PlayerPrefs.GetInt(LevelKey(id), 0);
            for (int k = 0; k < l; k++) refund += baseCost * (k + 1);
            PlayerPrefs.DeleteKey(LevelKey(id));
        }
        foreach (var (id, cap) in new[] { ("mail", MailLevels), ("stamina", StaminaLevels) })
        {
            int l = PlayerPrefs.GetInt(LevelKey(id), 0);
            for (int k = cap; k < l; k++) refund += 2 * (k + 1);
            if (l > cap) PlayerPrefs.SetInt(LevelKey(id), cap);
        }
        PlayerPrefs.SetInt(TokensKey, PlayerPrefs.GetInt(TokensKey, 0) + refund);
        PlayerPrefs.SetInt(StatsTrimKey, 1);
        PlayerPrefs.Save();
    }

    // (if the roster wasn't there at the first look, open every weapon once it is)
    public static void UnlockAllIfPending(CharacterSwitcher sw)
    {
        if (!unlockAllPending || sw == null || sw.Characters == null) return;
        unlockAllPending = false;
        foreach (var c in sw.Characters) if (c != null) PlayerPrefs.SetInt(UnlockKey(c.name), 1);
        PlayerPrefs.Save();
    }

    // 초기화: every upgrade and unlock taken back, their tokens returned
    public static int ResetAll(CharacterSwitcher sw)
    {
        int refund = 0;
        foreach (var u in Upgrades)
        {
            int l = LevelOf(u.id);
            for (int k = 0; k < l; k++) refund += u.baseCost * (k + 1);
            PlayerPrefs.SetInt(LevelKey(u.id), 0);
        }
        if (sw != null && sw.Characters != null)
            for (int i = 1; i < sw.Characters.Count; i++)
            {
                var c = sw.Characters[i];
                if (c == null || PlayerPrefs.GetInt(UnlockKey(c.name), 0) != 1) continue;
                refund += WeaponUnlockCost;
                PlayerPrefs.SetInt(UnlockKey(c.name), 0);
            }
        PlayerPrefs.SetInt(TokensKey, Tokens + refund);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return refund;
    }

    public static void AddTokens(int amount)
    {
        if (amount <= 0)
            return;
        GameAudio.Play("token");
        PlayerPrefs.SetInt(TokensKey, Tokens + amount);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    public static bool Buy(Upgrade u)
    {
        if (!CanBuy(u))
        {
            GameAudio.Play("ui_error");
            return false;
        }
        GameAudio.Play("anvil");
        GameAudio.Play("upgrade");
        PlayerPrefs.SetInt(TokensKey, Tokens - CostOf(u));
        PlayerPrefs.SetInt(LevelKey(u.id), LevelOf(u.id) + 1);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return true;
    }
}
