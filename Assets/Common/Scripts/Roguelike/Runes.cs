using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public enum God { Odin, Thor, Freyja, Loki, Tyr, Heimdall }

// A rune carved with a god's blessing. Picked up during a run, lost on death.
public class Rune
{
    public string id;
    public string name;
    public God god;
    public string description;
    public int price;                 // in the shop
    public Action<RunStats> apply;
}

// Everything the runes add up to. Rebuilt from scratch whenever the rune set changes.
public class RunStats
{
    public float maxHealthBonus;
    public int damageBonus;
    public float damageMultiplier = 1f;
    public float attackSpeedMultiplier = 1f;
    public float moveSpeedMultiplier = 1f;
    public float rangeBonus;
    public float critChance = 0.05f;
    public float critMultiplier = 2f;
    public float damageTakenMultiplier = 1f;
    public float dodgeChance;
    public float healOnKill;
    public float healOnRoomClear;
    public float thornsDamage;
    public int chainTargets;          // Thor: each hit arcs to this many more enemies
    public float chainDamageFraction = 0.5f;
    public float goldMultiplier = 1f;
    public int wardsPerRoom;          // Heimdall: hits blocked outright in each fight
}

public static class RuneLibrary
{
    public const int SetSize = 3; // runes of one god for its set blessing

    public static readonly string[] GodNames = { "오딘", "토르", "프레이야", "로키", "티르", "헤임달" };
    public static readonly Color[] GodColors =
    {
        new Color(0.55f, 0.6f, 0.95f),  // Odin: grey-blue
        new Color(1f, 0.85f, 0.25f),    // Thor: lightning gold
        new Color(0.95f, 0.5f, 0.75f),  // Freyja: rose
        new Color(0.45f, 0.85f, 0.35f), // Loki: green
        new Color(0.9f, 0.35f, 0.25f),  // Tyr: red
        new Color(0.95f, 0.95f, 0.95f), // Heimdall: white
    };

    public static readonly string[] SetBlessings =
    {
        "오딘의 지혜: 치명타 피해 ×3",
        "토르의 분노: 번개가 2명 더 튀고 피해 100%",
        "프레이야의 축복: 최대 HP +50, 처치 시 회복 +4",
        "로키의 속임수: 회피 확률 +20%",
        "티르의 용기: 받는 피해 -25%, 공격력 +2",
        "헤임달의 경계: 방마다 보호막 +2",
    };

    public static readonly Rune[] All =
    {
        new Rune { id = "huginn", name = "후긴의 눈", god = God.Odin, price = 30, description = "치명타 확률 +15%", apply = s => s.critChance += 0.15f },
        new Rune { id = "muninn", name = "무닌의 기억", god = God.Odin, price = 25, description = "방을 정리할 때마다 HP 8 회복", apply = s => s.healOnRoomClear += 8f },
        new Rune { id = "gungnir", name = "궁니르", god = God.Odin, price = 35, description = "공격 범위 +0.6", apply = s => s.rangeBonus += 0.6f },

        new Rune { id = "mjolnir", name = "묠니르의 파편", god = God.Thor, price = 30, description = "공격력 +2", apply = s => s.damageBonus += 2 },
        new Rune { id = "megingjord", name = "메긴교르드", god = God.Thor, price = 40, description = "공격력 ×1.3", apply = s => s.damageMultiplier *= 1.3f },
        new Rune { id = "thunder", name = "천둥의 룬", god = God.Thor, price = 40, description = "공격이 주변 적 1명에게 번개로 튐 (50%)", apply = s => s.chainTargets += 1 },

        new Rune { id = "brisingamen", name = "브리싱가멘", god = God.Freyja, price = 30, description = "최대 HP +30", apply = s => s.maxHealthBonus += 30f },
        new Rune { id = "seidr", name = "세이드", god = God.Freyja, price = 30, description = "적 처치 시 HP 4 회복", apply = s => s.healOnKill += 4f },
        new Rune { id = "valkyrie", name = "발키리의 날개", god = God.Freyja, price = 25, description = "이동 속도 +15%", apply = s => s.moveSpeedMultiplier *= 1.15f },

        new Rune { id = "trickster", name = "장난꾼의 룬", god = God.Loki, price = 35, description = "20% 확률로 피해 회피", apply = s => s.dodgeChance += 0.2f },
        new Rune { id = "shapeshift", name = "변신의 룬", god = God.Loki, price = 35, description = "공격 속도 +20%", apply = s => s.attackSpeedMultiplier *= 1.2f },
        new Rune { id = "greed", name = "로키의 탐욕", god = God.Loki, price = 20, description = "얻는 황금 +50%", apply = s => s.goldMultiplier *= 1.5f },

        new Rune { id = "oath", name = "티르의 맹세", god = God.Tyr, price = 35, description = "받는 피해 -15%", apply = s => s.damageTakenMultiplier *= 0.85f },
        new Rune { id = "gleipnir", name = "글레이프니르", god = God.Tyr, price = 30, description = "나를 때린 적에게 피해 3 반사", apply = s => s.thornsDamage += 3f },
        new Rune { id = "tiwaz", name = "티와즈", god = God.Tyr, price = 30, description = "공격력 +1, 최대 HP +15", apply = s => { s.damageBonus += 1; s.maxHealthBonus += 15f; } },

        new Rune { id = "gjallarhorn", name = "걀라르호른", god = God.Heimdall, price = 35, description = "방마다 첫 피격 1회 무효", apply = s => s.wardsPerRoom += 1 },
        new Rune { id = "bifrost", name = "비프로스트", god = God.Heimdall, price = 30, description = "공격 속도 +10%, 이동 속도 +10%", apply = s => { s.attackSpeedMultiplier *= 1.1f; s.moveSpeedMultiplier *= 1.1f; } },
        new Rune { id = "watch", name = "파수꾼의 룬", god = God.Heimdall, price = 25, description = "치명타 확률 +8%, 받는 피해 -8%", apply = s => { s.critChance += 0.08f; s.damageTakenMultiplier *= 0.92f; } },
    };

    private static readonly Action<RunStats>[] setEffects =
    {
        s => s.critMultiplier = 3f,
        s => { s.chainTargets += 2; s.chainDamageFraction = 1f; },
        s => { s.maxHealthBonus += 50f; s.healOnKill += 4f; },
        s => s.dodgeChance += 0.2f,
        s => { s.damageTakenMultiplier *= 0.75f; s.damageBonus += 2; },
        s => s.wardsPerRoom += 2,
    };

    public static void ApplySet(God god, RunStats stats) => setEffects[(int)god](stats);

    // A random rune the player doesn't have yet (null when they have them all)
    public static Rune RandomUnowned(ICollection<Rune> owned, ICollection<Rune> exclude = null)
    {
        var pool = All.Where(r => !owned.Contains(r) && (exclude == null || !exclude.Contains(r))).ToList();
        return pool.Count == 0 ? null : pool[Random.Range(0, pool.Count)];
    }
}

// The state of the current run: runes, gold and what they add up to. Nothing here survives death.
public static class RunState
{
    public static readonly List<Rune> Runes = new List<Rune>();
    public static RunStats Stats { get; private set; } = new RunStats();
    public static int Gold { get; private set; }
    public static int Kills { get; set; }
    public static int RoomsCleared { get; set; }

    // Experience from slain enemies; every level brings a greatsword skill point
    public static int Level { get; private set; } = 1;
    public static int Xp { get; private set; }
    public static int XpToNext => 20 + 15 * (Level - 1);

    public static event Action Changed;
    public static event Action<int> LeveledUp;

    // Max health from the life crystal halls (생명의 결정) this run
    public static float VigorHealth { get; private set; }
    public static void AddVigorHealth(float amount)
    {
        VigorHealth += amount;
        Recompute();
    }

    public static void Reset()
    {
        Runes.Clear();
        VigorHealth = 0f;
        Level = 1;
        Xp = 0;
        Gold = 0;
        Kills = 0;
        RoomsCleared = 0;
        SkillSystem.ResetRun();   // Q / E / R start empty every run
        Recompute();
    }

    public static void AddRune(Rune rune)
    {
        if (rune == null || Runes.Contains(rune))
            return;
        Runes.Add(rune);
        Recompute();
    }

    public static int CountOf(God god) => Runes.Count(r => r.god == god);
    public static bool HasSet(God god) => CountOf(god) >= RuneLibrary.SetSize;

    public static void AddGold(int amount)
    {
        Gold += Mathf.Max(0, Mathf.RoundToInt(amount * Stats.goldMultiplier));
        Changed?.Invoke();
    }

    public static void AddXp(int amount)
    {
        Xp += Mathf.Max(0, amount);
        while (Xp >= XpToNext)
        {
            Xp -= XpToNext;
            Level++;
            LeveledUp?.Invoke(Level);
        }
        Changed?.Invoke();
    }

    public static bool SpendGold(int amount)
    {
        if (Gold < amount)
        {
            GameAudio.Play("ui_error");
            return false;
        }
        if (amount > 0) GameAudio.Play("ui_buy");
        Gold -= amount;
        Changed?.Invoke();
        return true;
    }

    // Rebuilt whenever runes or the skill tree's stat nodes change
    public static void Refresh() => Recompute();

    private static void Recompute()
    {
        var stats = new RunStats();
        foreach (Rune rune in Runes)
            rune.apply(stats);
        foreach (God god in Enum.GetValues(typeof(God)))
            if (HasSet(god))
                RuneLibrary.ApplySet(god, stats);
        DualBladeSkills.ApplyStats(stats);
        GreatswordSkills.ApplyStats(stats);
        SkillSystem.ApplyStats(stats);   // 보조스킬 stats and sets, the token gold bonus
        stats.maxHealthBonus += VigorHealth;
        Stats = stats;
        Changed?.Invoke();
    }
}
