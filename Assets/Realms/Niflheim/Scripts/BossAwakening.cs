using System.Collections;
using UnityEngine;

// A boss's second phase (페이즈 2 · 각성): when its health falls to 30%, it awakens once. It swells a metre taller, hits
// 30% harder and takes 30% less from every blow. The moment is marked with a roar of red light, a shockwave and a
// "각성!" call over its head, and its name on the HP bar gains "· 각성".
public class BossAwakening : MonoBehaviour
{
    public const float Threshold = 0.3f;     // health fraction that sets it off
    public const float ExtraHeight = 1f;     // metres it grows
    public const float DamageBonus = 1.3f;   // attack +30%
    public const float DefenseBonus = 0.7f;  // takes 70% of each blow (defence +30%)
    public const float SpeedBonus = 1.3f;    // attack speed and move speed +30%
    private const float GrowSeconds = 1.2f;

    private static readonly Color AwakenColor = new Color(1f, 0.25f, 0.2f);

    private Enemy enemy;
    private bool awakened;

    public bool Awakened => awakened;

    public static void Attach(Enemy boss)
    {
        // (humanoid bosses awaken in their own cutscene, with these numbers)
        if (boss == null || boss is HumanoidEnemy || boss.GetComponent<BossAwakening>() != null)
            return;
        boss.gameObject.AddComponent<BossAwakening>();
    }

    private void Awake() => enemy = GetComponent<Enemy>();

    private void Update()
    {
        if (awakened || enemy == null || enemy.IsDead || enemy.MaxHealth <= 0)
            return;
        if (enemy.Health <= enemy.MaxHealth * Threshold)
            StartCoroutine(Awaken());
    }

    private IEnumerator Awaken()
    {
        awakened = true;
        enemy.PhaseDamageMultiplier = DamageBonus;
        enemy.IncomingDamageMultiplier = DefenseBonus;
        if (enemy is NidhoggBoss dragon)
            dragon.EnterPhaseTwo(SpeedBonus);   // (also starts its ice meteors)
        if (!enemy.DisplayName.EndsWith("· 각성"))
            enemy.DisplayName += " · 각성";

        // the moment it awakens
        Vector3 feet = transform.position;
        float height = Mathf.Max(1f, BossCutscene.HeightOf(transform));
        DualBlades.Say(feet + Vector3.up * (height + 0.8f), "각성!", AwakenColor);
        Shockwave.Spawn(feet, 4f + height * 0.4f, AwakenColor, 18);
        Vfx.GroundRing(feet, 3f + height * 0.3f, new Color(1f, 0.55f, 0.3f), 0.5f, "glow");
        CameraShake.Shake(0.45f, 0.25f);
        var glow = new GameObject("AwakenLight");
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = Vector3.up * height * 0.5f / Mathf.Max(0.01f, transform.localScale.y);
        var lamp = glow.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = AwakenColor;
        lamp.range = 6f + height;
        lamp.shadows = LightShadows.None;
        glow.AddComponent<LightPulse>().Init(6f, 0.3f, GrowSeconds + 0.8f);

        // it swells a metre taller (uniformly, so it keeps its build), easing in
        Vector3 from = transform.localScale;
        Vector3 to = from * ((height + ExtraHeight) / height);
        for (float t = 0f; t < GrowSeconds && enemy != null && !enemy.IsDead; t += Time.deltaTime)
        {
            transform.localScale = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / GrowSeconds));
            yield return null;
        }
        if (enemy != null && !enemy.IsDead)
            transform.localScale = to;

        // a lasting ember of red light round it while the second phase lasts
        var ember = new GameObject("AwakenEmber");
        ember.transform.SetParent(transform, false);
        ember.transform.localPosition = glow.transform.localPosition;
        var emberLamp = ember.AddComponent<Light>();
        emberLamp.type = LightType.Point;
        emberLamp.color = AwakenColor;
        emberLamp.range = 4f + height;
        emberLamp.intensity = 1.6f;
        emberLamp.shadows = LightShadows.None;
        ember.AddComponent<FlickerLight>();
    }
}
