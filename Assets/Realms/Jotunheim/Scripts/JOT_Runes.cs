using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace NineWorlds.Jotunheim
{
    // One of Jotunheim's runes. Stat changes go through the shared rune system (RunState.AddRune, its public
    // entry point), so movement, attack speed and the like work exactly as elsewhere. Life steal, which the
    // shared stats don't have, is handled here.
    public class JOT_Rune
    {
        public string id;
        public string name;
        public string description;
        public God god;
        public Action<RunStats> stats;
        public float lifeSteal;   // share of each blow's damage healed back
    }

    public static class JOT_RuneLibrary
    {
        public static readonly JOT_Rune[] All =
        {
            new JOT_Rune { id = "jot_blood", name = "흡혈의 룬", god = God.Freyja, description = "준 피해의 50%만큼 HP 회복", lifeSteal = 0.5f },
            new JOT_Rune { id = "jot_gale", name = "질풍의 룬", god = God.Loki, description = "공격 속도 +35%", stats = s => s.attackSpeedMultiplier *= 1.35f },
            new JOT_Rune { id = "jot_stride", name = "발키리의 걸음", god = God.Freyja, description = "이동 속도 +25%", stats = s => s.moveSpeedMultiplier *= 1.25f },
            new JOT_Rune { id = "jot_slayer", name = "거인 사냥꾼", god = God.Thor, description = "공격력 +2", stats = s => s.damageBonus += 2 },
            new JOT_Rune { id = "jot_stoneskin", name = "바위 피부", god = God.Tyr, description = "받는 피해 -25%", stats = s => s.damageTakenMultiplier *= 0.75f },
            new JOT_Rune { id = "jot_hawk", name = "매의 눈", god = God.Odin, description = "치명타 확률 +25%", stats = s => s.critChance += 0.25f },
            new JOT_Rune { id = "jot_heartwood", name = "거목의 심장", god = God.Tyr, description = "최대 HP +40", stats = s => s.maxHealthBonus += 40f },
            new JOT_Rune { id = "jot_thorns", name = "가시 덩굴", god = God.Tyr, description = "나를 때린 적에게 피해 4 반사", stats = s => s.thornsDamage += 4f },
            new JOT_Rune { id = "jot_wind", name = "바람 걸음", god = God.Loki, description = "15% 확률로 공격 회피", stats = s => s.dodgeChance += 0.15f },
        };

        public static readonly List<JOT_Rune> Owned = new List<JOT_Rune>();

        public static void ResetOwned() => Owned.Clear();

        // A few different runes the player doesn't have yet
        public static List<JOT_Rune> Draw(int count) =>
            All.Where(r => !Owned.Contains(r)).OrderBy(_ => Random.value).Take(count).ToList();

        public static void Take(JOT_Rune rune)
        {
            if (rune == null || Owned.Contains(rune))
                return;
            Owned.Add(rune);
            if (rune.stats != null)
                RunState.AddRune(new Rune { id = rune.id, name = rune.name, god = rune.god, description = rune.description, apply = rune.stats });
            if (rune.lifeSteal > 0f)
            {
                PlayerMiner miner = UnityEngine.Object.FindAnyObjectByType<PlayerMiner>();
                if (miner != null)
                    JOT_LifeSteal.Add(miner, rune.lifeSteal);
            }
        }
    }

    // Heals the player for a share of every blow they land on a real enemy
    public class JOT_LifeSteal : MonoBehaviour
    {
        private PlayerMiner miner;
        private PlayerStats stats;
        private float share;

        public float Share => share;

        public static void Add(PlayerMiner miner, float amount)
        {
            JOT_LifeSteal steal = miner.GetComponent<JOT_LifeSteal>();
            if (steal == null)
            {
                steal = miner.gameObject.AddComponent<JOT_LifeSteal>();
                steal.miner = miner;
                steal.stats = miner.GetComponent<PlayerStats>();
                miner.HitEnemy += steal.OnHit;
            }
            steal.share += amount;
        }

        private void OnHit(Enemy enemy, int damage, bool critical) => Credit(enemy, damage);

        // Also called for Jotunheim's own blows, which don't go through PlayerMiner.HitEnemy
        public void Credit(Enemy enemy, int damage)
        {
            if (stats == null || enemy == null || enemy is JOT_HittableProp)
                return;
            stats.Heal(damage * share);
        }

        private void OnDestroy()
        {
            if (miner != null)
                miner.HitEnemy -= OnHit;
        }
    }
}
