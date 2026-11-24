using System;
using UnityEngine;
using Random = UnityEngine.Random;

// The player's combat stats for this run (base values plus runes) and current health.
public class PlayerStats : MonoBehaviour
{
    [SerializeField] private float baseMaxHealth = 100f;
    [SerializeField] private int baseDamage = 2;
    [SerializeField] private float maxAttackSpeed = 3f;

    private float knownMaxHealth;
    private int wardsLeft;
    private PlayerMiner miner; // a charging greatsword changes how hits land
    private DualBlades blades; // a dodge with the twin blades slips through hits

    public float Health { get; private set; }
    public bool IsDead => Health <= 0f;

    // Base values, plus the forge's upgrades, plus runes, plus any test-mode adjustments
    public float MaxHealth => Mathf.Max(1f, baseMaxHealth + MetaProgress.MaxHealthBonus + RunState.Stats.maxHealthBonus + DebugCheats.MaxHealthBonus);
    // (a skill's boon — 축복, 광폭화, 바람의 가호 — raises every blow while it lasts)
    // DamageExact keeps the fraction: every +% counts at once (rounded to a whole number, a base of 2 ignored the first
    // +24% of damage altogether). Damage is the rounded figure, for what still wants a whole number.
    public float DamageExact => Mathf.Max(1f, (baseDamage + MetaProgress.DamageBonus + RunState.Stats.damageBonus + DebugCheats.DamageBonus) * RunState.Stats.damageMultiplier * SkillSystem.DamageBuff);
    public int Damage => Mathf.Max(1, Mathf.RoundToInt(DamageExact));

    // A blow's damage to a whole number without losing its worth on average: 1.35 comes out as 1 (65%) or 2 (35%)
    public static int RoundHit(float damage)
    {
        int whole = Mathf.FloorToInt(damage);
        return Mathf.Max(1, whole + (UnityEngine.Random.value < damage - whole ? 1 : 0));
    }
    public float AttackSpeed => Mathf.Min(RunState.Stats.attackSpeedMultiplier * MetaProgress.AttackSpeedMultiplier, maxAttackSpeed) * DebugCheats.AttackSpeedMultiplier;
    public int WardsLeft => wardsLeft;

    public event Action Damaged;
    public event Action<string> Avoided; // "회피" / "보호막"
    public event Action Died;

    private void Awake()
    {
        miner = GetComponent<PlayerMiner>();
        blades = GetComponent<DualBlades>();
        RestoreHealth();
    }

    private void OnEnable()
    {
        RunState.Changed += RefreshMaxHealth;
        MetaProgress.Changed += RefreshMaxHealth;
    }

    private void OnDisable()
    {
        RunState.Changed -= RefreshMaxHealth;
        MetaProgress.Changed -= RefreshMaxHealth;
    }

    // More max health (a rune, the test panel) comes with that much health right away
    public void RefreshMaxHealth()
    {
        float max = MaxHealth;
        if (max > knownMaxHealth && !IsDead)
            Health += max - knownMaxHealth;
        Health = Mathf.Min(Health, max);
        knownMaxHealth = max;
    }

    public void RestoreHealth()
    {
        Health = knownMaxHealth = MaxHealth;
    }

    public void Heal(float amount)
    {
        if (!IsDead)
            Health = Mathf.Min(MaxHealth, Health + amount);
    }

    // Each fight starts with fresh Heimdall wards
    public void ResetWards()
    {
        wardsLeft = RunState.Stats.wardsPerRoom;
    }

    // A price paid in health (a blood skill): never lethal, untouched by armour or dodges
    public void TakeDamageRaw(float amount)
    {
        if (IsDead || !GameManager.IsRunActive) return;
        Health = Mathf.Max(1f, Health - amount);
        Damaged?.Invoke();
    }

    public void TakeDamage(float amount, Enemy source = null)
    {
        if (IsDead || !GameManager.IsRunActive || DebugCheats.Invincible)
            return;

        if (Random.value < RunState.Stats.dodgeChance)
        {
            Avoided?.Invoke("회피!");
            return;
        }
        if (wardsLeft > 0)
        {
            wardsLeft--;
            Avoided?.Invoke("보호막!");
            return;
        }
        if (miner != null && miner.Guarding)
        {
            Avoided?.Invoke("단절!");
            return;
        }
        if (blades != null && blades.IsActive && blades.Invulnerable)
        {
            Avoided?.Invoke("회피!");
            return;
        }
        ArmoryWeapon armory = ArmoryWeapon.Current != null && ArmoryWeapon.Current.IsActive ? ArmoryWeapon.Current : null;
        if (armory != null && armory.Invulnerable)
        {
            Avoided?.Invoke("회피!");
            return;
        }

        if (source != null)
            amount *= source.OutgoingDamageMultiplier;
        float taken = amount * RunState.Stats.damageTakenMultiplier * MetaProgress.DamageTakenMultiplier * DebugCheats.DamageTakenMultiplier
                      * (miner != null ? miner.IncomingDamageMultiplier : 1f) * (armory != null ? armory.IncomingDamageMultiplier : 1f)
                      * SkillSystem.DamageTakenBuff;
        Health = Mathf.Max(0f, Health - taken);
        // a monster's blow lands: a spark on the player and a split-second freeze
        // (a blow with no monster behind it — a trap, a projectile left behind — lands just the same)
        if (taken > 0f)
            EnemyFeel.PlayerHit(this, source, taken);
        if (source != null && RunState.Stats.thornsDamage > 0f)
            source.TakeHit(transform.position, Mathf.RoundToInt(RunState.Stats.thornsDamage));
        Damaged?.Invoke();
        if (IsDead)
        {
            Died?.Invoke();
            return;
        }
        if (miner != null)
            miner.OnHurt(taken);
        if (armory != null)
            armory.OnHurt(taken);
    }

    // ── Poison (the mist giant's poison mist): a fixed loss of health every second for a while, then it wears off
    // completely. A new dose while poisoned doesn't stack or stretch it; it simply runs its course ──
    public bool Poisoned => Time.time < poisonUntil;
    public float PoisonLeft => Mathf.Max(0f, poisonUntil - Time.time);
    private float poisonUntil, poisonPerSecond, poisonTick;

    public void ApplyPoison(float perSecond, float seconds)
    {
        if (IsDead || !GameManager.IsRunActive || DebugCheats.Invincible || Poisoned)
            return;
        poisonPerSecond = perSecond;
        poisonUntil = Time.time + seconds;
        poisonTick = 1f;
        Avoided?.Invoke("중독!");
    }

    public void CurePoison() => poisonUntil = 0f;

    private void Update()
    {
        if (poisonUntil <= 0f)
            return;
        if (IsDead || !GameManager.IsRunActive)
        {
            poisonUntil = 0f;
            return;
        }
        poisonTick -= Time.deltaTime;
        if (poisonTick <= 0f && Time.time <= poisonUntil + 0.05f)
        {
            poisonTick += 1f;
            if (!DebugCheats.Invincible)
            {
                Health = Mathf.Max(0f, Health - poisonPerSecond);
                Damaged?.Invoke();
                if (IsDead)
                {
                    poisonUntil = 0f;
                    Died?.Invoke();
                    return;
                }
            }
        }
        if (Time.time > poisonUntil + 0.05f)
            poisonUntil = 0f;   // worn off entirely
    }
}
