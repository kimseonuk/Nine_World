using UnityEngine;

// Effects enemies put on the player for a while (Heimdall's Heimsight slows the world around them,
// a seidr mage's soul chains drag at their feet)
public static class PlayerEffects
{
    private static float slowUntil;
    private static float slowFactor = 1f;
    private static float bindUntil;
    private static float bindFactor = 1f;
    private static float rootUntil;

    // 1 = normal; lower slows the player's movement, swings and nearby missiles
    public static float Slow => Time.time < slowUntil ? slowFactor : 1f;
    public static bool Slowed => Time.time < slowUntil;

    // 1 = normal; lower only slows the player's walking (영혼의 사슬)
    public static float Bind => Time.time < bindUntil ? bindFactor : 1f;
    public static bool Bound => Time.time < bindUntil;

    public static void ApplySlow(float factor, float seconds)
    {
        slowFactor = Mathf.Clamp(factor, 0.05f, 1f);
        slowUntil = Time.time + seconds;
    }

    public static void ApplyBind(float factor, float seconds)
    {
        bindFactor = Mathf.Clamp(factor, 0.05f, 1f);
        bindUntil = Mathf.Max(bindUntil, Time.time + seconds);
    }

    // Held fast (Heimdall's chains of judgment, Týr's broken oath): no walking and no dash until it wears off
    public static bool Rooted => Time.time < rootUntil;
    public static float RootLeft => Mathf.Max(0f, rootUntil - Time.time);

    public static void Root(float seconds) => rootUntil = Mathf.Max(rootUntil, Time.time + seconds);

    public static void Clear() => slowUntil = bindUntil = rootUntil = 0f;
}
