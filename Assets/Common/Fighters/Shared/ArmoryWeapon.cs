using System.Collections.Generic;
using UnityEngine;

// A weapon brought over from one of the other realms' projects (Niflheim's spear, Jotunheim's axes, Muspelheim's hammer),
// taken up at the village armory like the greatsword and the twin blades. Each sits on the player as its own component
// and only works while its fighter is the one in hand; it plays the way it did in its own project. While one is in hand
// the game's own input (ClickToDig), movement (PlayerMovement), damage (PlayerStats), HUD and skill-tree panel ask it
// what to do instead of the greatsword.
[RequireComponent(typeof(PlayerMiner))]
public abstract class ArmoryWeapon : MonoBehaviour
{
    public static ArmoryWeapon Current { get; private set; }

    public abstract CharacterProfile.WeaponKind Kind { get; }
    // Its skill tree, if its project had one (null: its skills come with the weapon)
    public virtual SkillTree Tree => null;
    public virtual string TreeName => null;

    public bool IsActive => Current == this && isActiveAndEnabled;

    // The player stands still (or is moved by the weapon itself)
    public virtual bool Rooted => false;
    // Space is the weapon's own (a lunge, a dodge) instead of the game's dash
    public virtual bool OwnsSpace => false;
    // Whether the game's dash may be used (a weapon whose tree teaches the dash says no until it is learned), and how
    // far it carries
    public virtual bool CanDash => true;
    public virtual float DashScale => 1f;
    public virtual bool Invulnerable => false;
    public virtual float MoveMultiplier => 1f;
    public virtual float IncomingDamageMultiplier => 1f;
    // The controls shown at the bottom of the screen
    public virtual string Hint => "[클릭] 공격";

    // The attack button (a click not spent on digging)
    public virtual void Press(Vector3 aim) { }
    public virtual void Hold(Vector3 aim) { }
    public virtual void Release(Vector3 aim) { }
    public virtual void OnHurt(float damage) { }
    public virtual void ResetCombat() { }
    // Anything over the player it draws itself (a charge gauge, its skill bar); called from GameManager's HUD
    public virtual void DrawHud() { }

    protected PlayerMiner miner;
    protected PlayerStats stats;
    protected CharacterController controller;
    protected CharacterProfile profile;
    protected Animator animator;

    protected virtual void Awake()
    {
        miner = GetComponent<PlayerMiner>();
        stats = GetComponent<PlayerStats>();
        controller = GetComponent<CharacterController>();
    }

    // Called on every armory weapon when the player's fighter changes
    public void SetCharacter(CharacterProfile character)
    {
        bool on = character != null && character.Weapon == Kind;
        if (IsActive || on)
            ResetCombat();
        profile = on ? character : null;
        animator = on ? character.Animator : null;
        if (on)
            Current = this;
        else if (Current == this)
            Current = null;
        if (Tree != null)
            Tree.Active = on;
        Equipped(on);
    }

    protected virtual void Equipped(bool on) { }

    protected virtual void OnDestroy()
    {
        if (Current == this)
            Current = null;
    }

    public static void SetAll(GameObject player, CharacterProfile character)
    {
        foreach (ArmoryWeapon weapon in player.GetComponents<ArmoryWeapon>())
            weapon.SetCharacter(character);
    }

    public static void ResetAll(GameObject player)
    {
        foreach (ArmoryWeapon weapon in player.GetComponents<ArmoryWeapon>())
            weapon.ResetCombat();
    }

    // Every armory weapon's skill tree (reset each run, filled by the test panel)
    public static IEnumerable<SkillTree> Trees
    {
        get
        {
            yield return SpearSkills.Tree;
            yield return UllrSkills.Tree;
            // (묠니르's is the 번개 망치 tree, StormSkills.Tree, already among the game's own)
            yield return GauntletSkills.Tree;
        }
    }

    // Base damage from the runes, with a chance to crit (the same roll the game's weapons use)
    // (kept exact: the blow is rounded once, at the end)
    protected float RollDamage(out bool critical)
    {
        float damage = stats != null ? stats.DamageExact : 1f;
        critical = Random.value < RunState.Stats.critChance;
        if (critical)
            damage *= RunState.Stats.critMultiplier;
        return damage;
    }

    // A blow on an enemy, reported through the game's own hit event (floaters, life steal, chain lightning...)
    protected void Strike(Enemy enemy, int damage, bool critical) => miner.StrikeEnemy(enemy, damage, critical);

    protected float AttackSpeed => (stats != null ? stats.AttackSpeed : 1f) * PlayerEffects.Slow;
}
