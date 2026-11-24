using System.Collections;
using UnityEngine;

// 발키리: a winged spear-maiden. Darts at the player in a string of dashes, stabs at close range,
// and once badly hurt charges up and hurls homing spears.
public class Valkyrie : HumanoidEnemy
{
    private static readonly int ThrowHash = Animator.StringToHash("Throw");

    [Header("Dash string")]
    [SerializeField] private float dashMinRange = 3f;
    [SerializeField] private float dashMaxRange = 10f;
    [SerializeField] private int dashes = 3;
    [SerializeField] private float dashDistance = 4.5f;
    [SerializeField] private float dashTime = 0.28f;
    [SerializeField] private float dashPause = 0.3f;
    [SerializeField] private float dashDamageMultiplier = 0.7f;
    [SerializeField] private float dashCooldown = 3.5f;

    [Header("Piercing dash (in place of a plain thrust)")]
    [SerializeField] private float pierceRange = 4.5f;
    [SerializeField] private float pierceWindup = 0.5f;
    [SerializeField] private float pierceOvershoot = 2.5f;     // she runs on through the player by this much
    [SerializeField] private float pierceRecover = 0.5f;

    [Header("Homing spear (below this much health)")]
    [SerializeField, Range(0f, 1f)] private float enrageAt = 0.4f;
    [SerializeField] private float chargeTime = 1.2f;
    [SerializeField] private float spearDamageMultiplier = 1.6f;
    [SerializeField] private float spearCooldown = 5f;
    [SerializeField] private int spearsPerThrow = 1;
    [SerializeField] private GameObject heldSpear;   // the spear in her hand: gone while a thrown one is in the air

    private float dashTimer;
    private float spearTimer;
    private float spearBackAt;

    protected override void Awake()
    {
        base.Awake();
        dashTimer = Random.Range(0.5f, dashCooldown);
    }

    // As a boss: longer dash strings and a fan of three spears
    public override void MakeBoss(string bossName, int bossHealth, float bossDamage, float size)
    {
        base.MakeBoss(bossName, bossHealth, bossDamage, size);
        dashes = 4;
        dashDistance *= 1.3f;
        spearsPerThrow = 3;
        enrageAt = 0.7f;
    }

    protected override Vector3 Think(Vector3 toPlayer, float distance)
    {
        dashTimer -= Time.deltaTime;
        spearTimer -= Time.deltaTime;
        // A fresh spear of light forms in her hand a moment after each throw
        if (heldSpear != null && !heldSpear.activeSelf && Time.time >= spearBackAt)
        {
            heldSpear.SetActive(true);
            Vfx.Burst(heldSpear.transform.position, new Color(1f, 0.85f, 0.4f), 8, 2f, 0.1f);
        }
        if (cooldown > 0f)
            return distance > pierceRange ? toPlayer / distance * moveSpeed : Vector3.zero;

        if (HealthFraction <= enrageAt && spearTimer <= 0f && distance > 2.5f)
        {
            spearTimer = spearCooldown;
            StartAction(HomingSpear(), attackCooldown);
            return Vector3.zero;
        }
        if (dashTimer <= 0f && distance >= dashMinRange && distance <= dashMaxRange)
        {
            dashTimer = dashCooldown;
            StartAction(DashString(), attackCooldown);
            return Vector3.zero;
        }
        if (distance <= pierceRange)
        {
            StartAction(PiercingDash(), attackCooldown + 0.4f);
            return Vector3.zero;
        }
        return toPlayer / distance * moveSpeed;
    }

    // her glints and streaks in spear-light gold
    protected override Color FeelColor => new Color(1f, 0.9f, 0.6f);

    // Spear levelled, she shoots straight through where the player stands
    private IEnumerator PiercingDash()
    {
        FacePlayer();
        Vector3 to = player != null ? Flat(player.position - transform.position) : transform.forward;
        float length = to.magnitude + pierceOvershoot;
        warning = Telegraph.Line(transform.position, to, length, 1f, pierceWindup);
        EnemyFeel.Windup(transform, transform.position + (Vector3.up * 1.2f + to.normalized * 0.9f) * FeelSize, FeelColor, FeelSize);
        yield return new WaitForSeconds(pierceWindup);
        if (warning != null)
            Destroy(warning);
        if (animator != null)
            animator.SetTrigger(AttackHash);
        yield return Lunge(to, length, 0.25f, attackDamage, 1f, 6f);
        yield return new WaitForSeconds(pierceRecover);
    }

    // Several quick dashes, re-aiming at the player between each
    private IEnumerator DashString()
    {
        for (int i = 0; i < dashes && !dead; i++)
        {
            Vector3 to = player != null ? Flat(player.position - transform.position) : transform.forward;
            warning = Telegraph.Line(transform.position, to, dashDistance, 1f, dashPause);
            EnemyFeel.Coil(transform, dashPause, 0.07f);
            yield return new WaitForSeconds(dashPause);
            if (warning != null)
                Destroy(warning);
            yield return Lunge(to, dashDistance, dashTime, attackDamage * dashDamageMultiplier, 1f, 4f);
        }
        yield return new WaitForSeconds(0.4f);
    }

    // Charge up with a glowing ring, then hurl spears that home in on the player
    private IEnumerator HomingSpear()
    {
        EnemyVoice.Of(this)?.Skill();   // (its own big-move sound)
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(ThrowHash);
        // Kept as the warning, so an interrupted charge cleans it up too
        warning = Mats.Shape(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.05f, 0f), new Vector3(1.6f, 0.01f, 1.6f),
            Mats.Get("valkyrie_charge", new Color(1f, 0.85f, 0.4f), 0.5f, new Color(1.6f, 1.3f, 0.5f)));
        for (float t = 0f; t < chargeTime; t += Time.deltaTime)
        {
            FacePlayer();
            float pulse = 1.6f + Mathf.Sin(t * 18f) * 0.15f + t;
            warning.transform.localScale = new Vector3(pulse, 0.01f, pulse);
            yield return null;
        }
        Destroy(warning);

        Vector3 from = transform.position + Vector3.up * 1.4f * transform.localScale.y + transform.forward * 0.6f;
        EnemyFeel.Launch(transform, from, FeelColor);
        EnemyFeel.Shake(transform.position, 0.12f, 0.08f);
        for (int i = 0; i < spearsPerThrow; i++)
        {
            float spread = spearsPerThrow > 1 ? (i - (spearsPerThrow - 1) / 2f) * 25f : 0f;
            Projectile.Spear(from, Quaternion.Euler(0f, spread, 0f) * transform.forward, this, attackDamage * spearDamageMultiplier);
        }
        if (heldSpear != null)
        {
            heldSpear.SetActive(false);
            spearBackAt = Time.time + 1.2f;
        }
        yield return new WaitForSeconds(0.6f);
    }
}
