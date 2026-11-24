// Test-mode adjustments to the player, set from the F1 panel. They stay for the editor session
// (a new run keeps them) and are separate from runes, so turning them back to 0 / 1 restores normal play.
public static class DebugCheats
{
    public static float MaxHealthBonus;        // added to max health
    public static int DamageBonus;             // added to base damage
    public static float AttackSpeedMultiplier = 1f;
    public static float MoveSpeedMultiplier = 1f;
    public static float DamageTakenMultiplier = 1f;
    public static bool Invincible;
    public static bool AllSkillsOnStart = true; // every skill tree node already learned when a run (or the village) starts
    public static bool UsedThisRun;            // a one-shot test action (gold, skip to boss...) this run

    // Runs played with any test help don't count toward the best-realm and win records
    public static bool Tainted => UsedThisRun || Active;

    public static bool Active =>
        MaxHealthBonus != 0f || DamageBonus != 0 || AttackSpeedMultiplier != 1f || MoveSpeedMultiplier != 1f
        || DamageTakenMultiplier != 1f || Invincible || AllSkillsOnStart;

    public static void Reset()
    {
        MaxHealthBonus = 0f;
        DamageBonus = 0;
        AttackSpeedMultiplier = 1f;
        MoveSpeedMultiplier = 1f;
        DamageTakenMultiplier = 1f;
        Invincible = false;
    }
}
