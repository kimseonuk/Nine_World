using System.Collections;
using UnityEngine;

// 룬 궁수: a Norse archer in a blue rune cloak. Keeps its distance and backs off when the player closes in;
// every shot is aimed first (a line of blue light), so there is always a moment to step aside.
//   룬 화살     one aimed rune arrow
//   연속 사격   three quick arrows, re-aimed each time
//   폭발 화살   an arrow loosed into the sky that comes down on a marked ring and bursts
//   후퇴 사격   with the player close, it rolls back out of reach and shoots as it comes up
public class RuneArcher : HumanoidEnemy
{
    private static readonly int ShootHash = Animator.StringToHash("Shoot");
    private static readonly int ShootUpHash = Animator.StringToHash("ShootUp");
    private static readonly int RollHash = Animator.StringToHash("Roll");

    [Header("Distance")]
    [SerializeField] private float preferredRange = 8f;
    [SerializeField] private float shootRange = 11f;
    [SerializeField] private float tooClose = 4f;          // closer than this: back off

    [Header("Rune arrow")]
    [SerializeField] private float aimTime = 0.75f;
    [SerializeField] private float shotCooldown = 1.6f;

    [Header("Volley")]
    [SerializeField] private int volleyArrows = 3;
    [SerializeField] private float volleyAim = 0.55f;
    [SerializeField] private float volleyGap = 0.24f;
    [SerializeField] private float volleyDamageMultiplier = 0.7f;
    [SerializeField] private float volleyCooldown = 5f;

    [Header("Bursting arrow")]
    [SerializeField] private float burstRadius = 1.9f;
    [SerializeField] private float burstDelay = 1.15f;
    [SerializeField] private float burstDamageMultiplier = 1.4f;
    [SerializeField] private float burstCooldown = 6.5f;

    [Header("Retreating shot")]
    [SerializeField] private float rollDistance = 4.5f;
    [SerializeField] private float rollTime = 0.45f;
    [SerializeField] private float rollCooldown = 4.5f;

    private float volleyTimer, burstTimer, rollTimer;

    protected override void Awake()
    {
        base.Awake();
        volleyTimer = Random.Range(1.5f, volleyCooldown);
        burstTimer = Random.Range(2.5f, burstCooldown);
        rollTimer = Random.Range(0.5f, rollCooldown * 0.5f);
    }

    private Vector3 BowPoint => transform.position + Vector3.up * 0.95f * transform.localScale.y + transform.forward * 0.5f;

    protected override Vector3 Think(Vector3 toPlayer, float distance)
    {
        volleyTimer -= Time.deltaTime;
        burstTimer -= Time.deltaTime;
        rollTimer -= Time.deltaTime;
        Vector3 dir = toPlayer / distance;

        // Too close: roll back and shoot, or just turn and run to get some room
        if (distance < tooClose)
        {
            // (the roll is its escape: it doesn't wait out the shot cooldown)
            if (rollTimer <= 0f)
            {
                rollTimer = rollCooldown;
                StartAction(RetreatShot(), shotCooldown * 0.6f);
                return Vector3.zero;
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(-dir), 720f * Time.deltaTime);
            return -dir * moveSpeed * 1.3f;
        }
        if (cooldown > 0f)
            return Strafe(dir, distance);

        if (distance <= shootRange)
        {
            if (burstTimer <= 0f)
            {
                burstTimer = burstCooldown;
                StartAction(BurstingArrow(), shotCooldown);
            }
            else if (volleyTimer <= 0f)
            {
                volleyTimer = volleyCooldown;
                StartAction(Volley(), shotCooldown);
            }
            else
                StartAction(AimedShot(), shotCooldown);
            return Vector3.zero;
        }
        return dir * moveSpeed;
    }

    // Between shots: drift to the preferred range, sidling a little so it isn't a sitting target
    private Vector3 Strafe(Vector3 dir, float distance)
    {
        float along = Mathf.Clamp(distance - preferredRange, -1f, 1f);
        Vector3 side = Vector3.Cross(Vector3.up, dir) * Mathf.Sin(Time.time * 0.8f + GetInstanceID());
        return (dir * along + side * 0.5f) * moveSpeed * 0.7f;
    }

    // A thin line of blue light from the bow to where it's aimed, brightening until it lets go
    private GameObject AimLine(Vector3 aim, float seconds)
    {
        var line = Telegraph.Line(transform.position, aim, shootRange + 3f, 0.35f, seconds);
        Vfx.Glow(line.transform, Vector3.up * 0.1f, 0.6f, MobFx.RuneBlue);
        return line;
    }

    private Vector3 AimAtPlayer()
    {
        FacePlayer();
        return player != null ? Flat(player.position - transform.position).normalized : transform.forward;
    }

    private void Loose(Vector3 aim, float damage)
    {
        Vector3 from = BowPoint;
        // Level with the player's chest, wherever it's shot from
        Vector3 target = player != null ? player.position + Vector3.up * 0.3f : from + aim;
        Vector3 dir = aim * Mathf.Max(1f, FlatDistance(target, from));
        dir.y = target.y - from.y;
        Projectile.RuneArrow(from, dir, this, damage);
        Vfx.Flash(from, 0.8f, MobFx.RuneBlue, 0.12f);
        EnemyFeel.Launch(transform, from, MobFx.RuneBlue);
    }

    protected override Color FeelColor => MobFx.RuneBlue;

    // 룬 화살: draw, aim along a marked line, loose
    private IEnumerator AimedShot()
    {
        Vector3 aim = AimAtPlayer();
        if (animator != null)
            animator.SetTrigger(ShootHash);
        warning = AimLine(aim, aimTime);
        EnemyFeel.Windup(transform, BowPoint, MobFx.RuneBlue, 0.8f);
        // Tracks the player while drawing, then holds still for the last moment before the shot
        for (float t = 0f; t < aimTime; t += Time.deltaTime)
        {
            if (t < aimTime * 0.6f)
            {
                aim = AimAtPlayer();
                warning.transform.rotation = Quaternion.LookRotation(aim);
            }
            yield return null;
        }
        Destroy(warning);
        Loose(aim, attackDamage);
        yield return new WaitForSeconds(0.35f);
    }

    // 연속 사격: three arrows in quick succession, each re-aimed
    private IEnumerator Volley()
    {
        Vector3 aim = AimAtPlayer();
        if (animator != null)
            animator.SetTrigger(ShootHash);
        warning = AimLine(aim, volleyAim);
        EnemyFeel.Windup(transform, BowPoint, MobFx.RuneBlue, 0.8f);
        yield return new WaitForSeconds(volleyAim);
        Destroy(warning);
        for (int i = 0; i < volleyArrows && !dead; i++)
        {
            if (i > 0)
            {
                aim = AimAtPlayer();
                if (animator != null)
                    animator.SetTrigger(ShootHash);
                yield return new WaitForSeconds(volleyGap);
            }
            Loose(aim, attackDamage * volleyDamageMultiplier);
        }
        yield return new WaitForSeconds(0.4f);
    }

    // 폭발 화살: an arrow shot high into the air; a ring on the floor where the player stands fills, then it lands
    private IEnumerator BurstingArrow()
    {
        EnemyVoice.Of(this)?.Skill();   // (its own big-move sound)
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(ShootUpHash);
        yield return new WaitForSeconds(0.45f);
        Vector3 up = BowPoint;
        Vfx.Flash(up, 1f, MobFx.RuneBlue, 0.15f);
        EnemyFeel.Launch(transform, up, MobFx.RuneBlue);
        var streak = new GameObject("SkyShot");
        streak.transform.position = up;
        Vfx.Glow(streak.transform, Vector3.zero, 0.7f, MobFx.RuneBlue);
        Vfx.Trail(streak, MobFx.RuneBlue, 0.25f, 0.2f);
        StartCoroutine(Rise(streak));
        if (player != null)
            MobFx.DelayedBlast(Flat(player.position), burstRadius, burstDelay, attackDamage * burstDamageMultiplier, this, MobFx.RuneBlue, skyArrow: true);
        yield return new WaitForSeconds(0.5f);
    }

    private static IEnumerator Rise(GameObject go)
    {
        for (float t = 0f; t < 0.35f && go != null; t += Time.deltaTime)
        {
            go.transform.position += Vector3.up * 28f * Time.deltaTime;
            yield return null;
        }
        if (go != null)
            Destroy(go, 0.2f);
    }

    // 후퇴 사격: a roll straight back away from the player, coming up with an arrow already nocked
    private IEnumerator RetreatShot()
    {
        Vector3 away = player != null ? Flat(transform.position - player.position).normalized : -transform.forward;
        if (animator != null)
            animator.SetTrigger(RollHash);
        yield return Lunge(away, rollDistance, rollTime, 0f, 0f, 0f);   // (its dust kick and streak come with the lunge)
        Vector3 aim = AimAtPlayer();
        warning = AimLine(aim, 0.35f);
        yield return new WaitForSeconds(0.35f);
        Destroy(warning);
        Loose(AimAtPlayer(), attackDamage);
        yield return new WaitForSeconds(0.3f);
    }
}
