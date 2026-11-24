using System;
using System.Reflection;
using UnityEngine;

// Lets this game's weapons fight a monster written for another team's player (Muspelheim's): the proxy sits on it as
// an Enemy — so the blades and greatsword find it in Enemy.Alive and the HUD can show its health — and passes our hits
// on to the monster's own TakeHit(int) / Stun(float). It reads the monster's health from its own fields, and when that
// runs out it reports the death once (gold, experience, the room's count) the way our enemies do.
public class SceneEnemyProxy : Enemy
{
    private MonoBehaviour target;       // whose health this is (the monster, or the boss behind a weak point)
    private Action<int> hit;            // where our blows go
    private FieldInfo healthField, maxField;
    private MethodInfo stun;
    private int maxHealth = 1;
    private bool reported;

    public MonoBehaviour Target => target;

    // Attaches to host (the monster itself, or a weak point that forwards to it); health is read from target
    public static SceneEnemyProxy Attach(GameObject host, MonoBehaviour target, string name, Action<int> hit = null, int gold = 3, int xp = 3)
    {
        var proxy = host.AddComponent<SceneEnemyProxy>();
        proxy.target = target;
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        // (called on the monster itself: a SendMessage would also reach this proxy's own TakeHit)
        MethodInfo takeHit = target.GetType().GetMethod("TakeHit", any, null, new[] { typeof(int) }, null);
        proxy.hit = hit ?? (damage => takeHit?.Invoke(target, new object[] { damage }));
        proxy.stun = target.GetType().GetMethod("Stun", any, null, new[] { typeof(float) }, null);
        proxy.healthField = target.GetType().GetField("health", any);
        proxy.maxField = target.GetType().GetField("maxHealth", any);
        proxy.maxHealth = proxy.maxField != null ? Convert.ToInt32(proxy.maxField.GetValue(target)) : 1;
        proxy.DisplayName = name;
        proxy.GoldDrop = gold;
        proxy.XpDrop = xp;
        return proxy;
    }

    public override int Health => target != null && healthField != null ? Mathf.Max(0, Convert.ToInt32(healthField.GetValue(target))) : 0;
    public override int MaxHealth => target != null && maxField != null ? Convert.ToInt32(maxField.GetValue(target)) : maxHealth;

    public override void TakeHit(Vector3 from, int damage)
    {
        if (!reported && target != null)
            hit(Mathf.Max(1, damage));
    }

    // Knocked down by the greatsword's shockwave: the monster's own daze
    public override void Knockdown(float seconds)
    {
        if (target != null && stun != null)
            stun.Invoke(target, new object[] { seconds });
    }

    // Its toughness is the monster's own (tuned by its team)
    public override void Scale(float healthMultiplier, float damageMultiplier) { }

    private void Update()
    {
        // A proxy only there to show a boss's health (disabled) never reports; the hittable ones report the death once
        if (!reported && enabled && target != null && healthField != null && Health <= 0)
        {
            reported = true;
            RaiseDied();
        }
    }
}
