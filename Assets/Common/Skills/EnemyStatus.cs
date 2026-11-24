using UnityEngine;

// What the skills leave on an enemy: burning (damage over time), chilled → frozen (a frozen enemy is held still for a
// moment; a boss isn't held, but still counts as frozen for 빙결 + 강타), shocked (a mark lightning leaps from).
// Added to an enemy the first time a skill touches it.
public class EnemyStatus : MonoBehaviour
{
    private Enemy enemy;
    private float burnUntil, burnTick, burnDamage;
    private float chill;                 // builds up to 1, then it freezes
    private float frozenUntil, shockedUntil;
    private GameObject burnFx, frozenFx;
    // 출혈: stacks of wounds (up to MaxBleed), each bleeding `bleedPerStack` every half second until `bleedUntil`
    public const int MaxBleed = 10;
    public const float BleedSeconds = 5f, BleedTick = 0.5f;
    private int bleedStacks;
    private float bleedUntil, bleedTick, bleedPerStack;
    private GameObject bleedFx;

    public bool Frozen => Time.time < frozenUntil;
    public bool Burning => Time.time < burnUntil;
    public bool Shocked => Time.time < shockedUntil;
    public bool Bleeding => bleedStacks > 0 && Time.time < bleedUntil;
    public int BleedStacks => Bleeding ? bleedStacks : 0;

    // A wound: `stacks` more of 출혈, each bleeding `perStack` a tick (the strongest wound sets the rate)
    public void Bleed(int stacks, float perStack)
    {
        if (enemy == null || enemy.IsDead) return;
        if (!Bleeding) bleedStacks = 0;
        bleedStacks = Mathf.Min(MaxBleed, bleedStacks + stacks);
        bleedPerStack = Mathf.Max(Bleeding ? bleedPerStack : 0f, perStack);
        bleedUntil = Time.time + BleedSeconds;
        if (bleedFx == null)
            bleedFx = Vfx.Glow(transform, Vector3.up * 1.2f, 1.1f, SkillCatalog.ElementColor(SkillElement.Blood) * 0.9f, "glow", 0f);
    }

    // 선혈 파열: every tick still to come, all at once, and the wounds are spent
    public int BurstBleed()
    {
        if (!Bleeding) return 0;
        int ticks = Mathf.CeilToInt((bleedUntil - Time.time) / BleedTick);
        int total = Mathf.RoundToInt(bleedStacks * bleedPerStack * ticks * BleedMultiplier);
        bleedStacks = 0;
        bleedUntil = 0f;
        return Mathf.Max(0, total);
    }

    private static float BleedMultiplier => SkillSystem.SynergyOn(7) ? 1.5f : 1f;   // 선혈의 낙인

    public static EnemyStatus Of(Enemy e)
    {
        if (e == null) return null;
        var s = e.GetComponent<EnemyStatus>();
        if (s == null)
        {
            s = e.gameObject.AddComponent<EnemyStatus>();
            s.enemy = e;
        }
        return s;
    }

    public void Burn(float damagePerTick, float seconds)
    {
        burnDamage = Mathf.Max(burnDamage * (Burning ? 1f : 0f), damagePerTick);
        burnUntil = Mathf.Max(burnUntil, Time.time + seconds);
        if (burnFx == null)
            burnFx = Vfx.Glow(transform, Vector3.up * 1.1f, 1.2f, SkillCatalog.ElementColor(SkillElement.Fire) * 0.9f, "auraflame", 30f);
    }

    public void Chill(float amount)
    {
        if (Frozen) return;
        chill += amount;
        if (chill >= 1f)
        {
            chill = 0f;
            Freeze(enemy != null && enemy.IsBoss ? 1.2f : 1.6f);
        }
    }

    public void Freeze(float seconds)
    {
        frozenUntil = Time.time + seconds;
        if (enemy != null && !enemy.IsBoss && !enemy.IsDead)
            enemy.Knockdown(seconds);
        if (frozenFx == null)
            frozenFx = Vfx.Glow(transform, Vector3.up * 0.9f, 1.8f, SkillCatalog.ElementColor(SkillElement.Ice) * 0.8f, "glow", 0f);
    }

    public void Shock(float seconds) => shockedUntil = Mathf.Max(shockedUntil, Time.time + seconds);

    private void Update()
    {
        if (enemy == null || enemy.IsDead)
        {
            if (burnFx != null) Destroy(burnFx);
            if (frozenFx != null) Destroy(frozenFx);
            if (bleedFx != null) Destroy(bleedFx);
            return;
        }
        if (Bleeding && (bleedTick -= Time.deltaTime) <= 0f)
        {
            bleedTick = BleedTick;
            int dmg = Mathf.Max(1, Mathf.RoundToInt(bleedStacks * bleedPerStack * BleedMultiplier));
            enemy.TakeHit(transform.position + Vector3.up, dmg);
            if (Random.value < 0.5f)
            {
                // drops running from the wound (the VARCO drip sheet, drawn dark under its glow so it reads on marble)
                string drip = Vfx.Pick("fx_bleed_drip", "glow");
                if (drip == "glow")
                    Vfx.Sprite("glow", transform.position + Vector3.up * Random.Range(0.6f, 1.4f), 0.9f, SkillCatalog.ElementColor(SkillElement.Blood) * 1.4f, 0.4f, 0.8f, 1.1f, 0f);
                else
                    SkillCast.Fx(drip, transform.position + Vector3.up * Random.Range(0.7f, 1.4f), 1.3f, SkillCatalog.ElementColor(SkillElement.Blood), 0.5f, ink: 0.7f);
            }
        }
        if (!Bleeding && bleedFx != null) { Destroy(bleedFx); bleedFx = null; }
        if (Burning && (burnTick -= Time.deltaTime) <= 0f)
        {
            burnTick = 0.5f;
            enemy.TakeHit(transform.position + Vector3.up, Mathf.Max(1, Mathf.RoundToInt(burnDamage)));
        }
        if (!Burning && burnFx != null) { Destroy(burnFx); burnFx = null; }
        if (!Frozen && frozenFx != null) { Destroy(frozenFx); frozenFx = null; }
        chill = Mathf.Max(0f, chill - Time.deltaTime * 0.15f);
    }
}
