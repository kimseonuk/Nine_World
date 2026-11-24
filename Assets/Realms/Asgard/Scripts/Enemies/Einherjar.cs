using System.Collections;
using UnityEngine;

// 에인헤랴르: Odin's chosen warriors with sword and shield. They come in groups.
// No plain swings: they keep a middling distance and fling sword waves, rush in with the shield,
// and once badly hurt leap onto the player with a heavy blow.
public class Einherjar : HumanoidEnemy
{
    private static readonly int ComboHash = Animator.StringToHash("Combo");
    private static readonly int HeavyHash = Animator.StringToHash("Heavy");

    [Header("Shield charge")]
    [SerializeField] private float bashMinRange = 3f;
    [SerializeField] private float bashMaxRange = 9f;
    [SerializeField] private float bashDistance = 7.5f;
    [SerializeField] private float bashWindup = 0.55f;
    [SerializeField] private float bashDamageMultiplier = 1f;
    [SerializeField] private float bashCooldown = 4f;

    [Header("Sword wave")]
    [SerializeField] private float waveRange = 9f;            // starts a wave when the player is this close
    [SerializeField] private float waveWindup = 0.55f;
    [SerializeField] private float waveCooldown = 2.2f;
    [SerializeField] private float waveDamageMultiplier = 0.8f;

    [Header("Leaping heavy blow (below this much health)")]
    [SerializeField, Range(0f, 1f)] private float enrageAt = 0.4f;
    [SerializeField] private float heavyWindup = 0.85f;
    [SerializeField] private float heavyRadius = 1.8f;
    [SerializeField] private float heavyDamageMultiplier = 2f;

    private float bashTimer;
    private bool heavyNext;

    protected override void Awake()
    {
        base.Awake();
        bashTimer = Random.Range(0.5f, bashCooldown);
    }

    protected override Vector3 Think(Vector3 toPlayer, float distance)
    {
        bashTimer -= Time.deltaTime;
        // Between attacks: close to wave range, but no nearer
        if (cooldown > 0f)
            return distance > waveRange * 0.8f ? toPlayer / distance * moveSpeed : Vector3.zero;

        if (bashTimer <= 0f && distance >= bashMinRange && distance <= bashMaxRange)
        {
            bashTimer = bashCooldown;
            StartAction(ShieldCharge(toPlayer / distance), attackCooldown);
            return Vector3.zero;
        }
        if (distance <= waveRange)
        {
            // Badly hurt: every other attack is the leaping blow
            bool enraged = HealthFraction <= enrageAt;
            if (enraged && heavyNext)
                StartAction(HeavyBlow(), attackCooldown + 0.6f);
            else
                StartAction(SwordWave(), waveCooldown);
            heavyNext = enraged && !heavyNext;
            return Vector3.zero;
        }
        return toPlayer / distance * moveSpeed;
    }

    // its glints, arcs and cracks in the einherjar's gold
    protected override Color FeelColor => BaseTint == Color.white ? new Color(1f, 0.85f, 0.55f) : BaseTint;

    private IEnumerator ShieldCharge(Vector3 direction)
    {
        warning = Telegraph.Line(transform.position, direction, bashDistance, 1.4f, bashWindup);
        // the shield raised and the body coiled before the charge
        EnemyFeel.Windup(transform, transform.position + (Vector3.up * 1.1f + direction * 0.6f) * FeelSize, FeelColor, FeelSize);
        yield return new WaitForSeconds(bashWindup);
        if (warning != null)
            Destroy(warning);
        yield return Lunge(direction, bashDistance, 0.45f, attackDamage * bashDamageMultiplier, 1.1f, 9f);
        yield return new WaitForSeconds(0.35f);
    }

    // A swing that looses a crescent of light along a marked line
    private IEnumerator SwordWave()
    {
        FacePlayer();
        Vector3 aim = transform.forward;
        warning = Telegraph.Line(transform.position, aim, waveRange + 2f, 1.2f, waveWindup);
        if (animator != null)
            animator.SetTrigger(ComboHash);
        EnemyFeel.Windup(transform, WeaponPoint, FeelColor, FeelSize);
        yield return new WaitForSeconds(waveWindup);
        if (warning != null)
            Destroy(warning);
        if (dead)
            yield break;
        Color color = FeelColor;
        EnemyFeel.Swing(transform.position + Vector3.up * 0.9f, aim, 2.4f, color);
        EnemyFeel.Launch(transform, transform.position + Vector3.up * 0.9f + aim * 0.8f, color);
        Projectile.MobSlash(transform.position + Vector3.up * 0.9f + aim * 0.8f, aim, this, attackDamage * waveDamageMultiplier, color);
        yield return new WaitForSeconds(0.6f);
    }

    // Marks the ground under the player, then leaps there and brings the sword down
    private IEnumerator HeavyBlow()
    {
        EnemyVoice.Of(this)?.Skill();   // (its own big-move sound)
        FacePlayer();
        Vector3 center = Flat(player.position);
        warning = Telegraph.Circle(center, heavyRadius, heavyWindup);
        if (animator != null)
            animator.SetTrigger(HeavyHash);
        EnemyFeel.Windup(transform, WeaponPoint + Vector3.up * 0.4f * FeelSize, FeelColor, FeelSize * 1.3f);
        yield return new WaitForSeconds(heavyWindup * 0.55f);
        Vector3 to = Flat(center - transform.position);
        float leap = Mathf.Max(0f, to.magnitude - 0.8f);
        if (leap > 0.1f)
            yield return Lunge(to, leap, heavyWindup * 0.45f, 0f, 0f, 0f);
        else
            yield return new WaitForSeconds(heavyWindup * 0.45f);
        if (warning != null)
            Destroy(warning);
        Shockwave.Spawn(center, heavyRadius, FeelColor, 12);
        EnemyFeel.Impact(center, heavyRadius, FeelColor, 0.28f);
        if (!dead && playerStats != null && FlatDistance(player.position, center) <= heavyRadius)
        {
            playerStats.TakeDamage(attackDamage * heavyDamageMultiplier, this);
            player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - transform.position).normalized * 7f);
        }
        yield return new WaitForSeconds(0.7f);
    }
}
