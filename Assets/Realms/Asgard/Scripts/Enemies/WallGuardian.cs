using System.Collections;
using UnityEngine;

// 성벽 수호병: a temple guard of Asgard's walls in white-and-gold plate, a tower shield on one arm, its helm dark inside
// but for two blue eyes. Heavy and slow, it keeps its shield to the front: blows from the front glance off the shield
// (its rune flares) and do little — come at it from the side or behind, or in the opening after it attacks. With other
// monsters about, it plants itself between the player and them.
//   방패 밀치기   a short step-in and a shove with the shield that throws the player back
//   방패 강타     raises the shield and smashes it down: a shockwave fans out in front
//   돌진 방어     charges with the shield set in front (still guarding) and runs the player down
//   수호 반격     after blocking blows, sweeps the shield aside and cuts back hard with its sword
// Model: VARCO rig + Animate clips; shield and sword VARCO props on its hands. Effects: Resources/Vfx/guard_*.
public class WallGuardian : HumanoidEnemy
{
    public static readonly Color GuardGold = new Color(1f, 0.84f, 0.42f);
    public static readonly Color RuneBlue = new Color(0.45f, 0.8f, 1f);

    [Header("Shield")]
    [SerializeField, Range(0f, 180f)] private float guardArc = 70f;        // half-angle in front the shield covers
    [SerializeField, Range(0f, 1f)] private float blockedDamage = 0.15f;   // what gets through the shield
    [SerializeField] private float openAfterAttack = 1.1f;                 // the gap before the shield comes back up
    [SerializeField] private Transform shield;

    [Header("방패 밀치기")]
    [SerializeField] private float pushRange = 2.6f;
    [SerializeField] private float pushWindup = 0.45f;
    [SerializeField] private float pushCooldown = 2.5f;

    [Header("방패 강타")]
    [SerializeField] private float slamReach = 5.5f;
    [SerializeField] private float slamHalfArc = 35f;
    [SerializeField] private float slamWindup = 0.9f;
    [SerializeField] private float slamCooldown = 6f;

    [Header("돌진 방어")]
    [SerializeField] private float chargeMin = 5f;
    [SerializeField] private float chargeMax = 13f;
    [SerializeField] private float chargeSpeed = 9f;
    [SerializeField] private float chargeWindup = 0.7f;
    [SerializeField] private float chargeCooldown = 8f;

    [Header("수호 반격")]
    [SerializeField] private float counterReach = 3.4f;
    [SerializeField] private float counterDamageMultiplier = 1.4f;
    [SerializeField] private int blocksToCounter = 2;

    [Header("Clip timing (state entered at offset, hit after)")]
    [SerializeField] private float pushOffset = 0.2f;
    [SerializeField] private float slamOffset = 0f;
    [SerializeField] private float counterOffset = 0f;
    [SerializeField] private float counterWindup = 0.4f;

    private float pushTimer, slamTimer, chargeTimer, openUntil, lastBlock;
    private int blocks;
    private bool charging, countering;
    private Enemy ward;            // the ally it is standing in front of
    private float wardCheck;

    protected override void Awake()
    {
        base.Awake();
        pushTimer = Random.Range(0.3f, 1f);
        slamTimer = Random.Range(2f, 4f);
        chargeTimer = Random.Range(2f, 5f);
        if (shield == null)
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Shield") { shield = t; break; }
    }

    protected override Color FeelColor => GuardGold;

    private bool Guarding => !dead && (Time.time >= openUntil || charging) && !countering;

    // Is a blow from this point coming at the shield?
    private bool FromFront(Vector3 from)
    {
        Vector3 to = Flat(from - transform.position);
        return to.sqrMagnitude < 0.0001f || Vector3.Angle(Facing, to) <= guardArc;
    }

    protected override Vector3 Think(Vector3 toPlayer, float distance)
    {
        pushTimer -= Time.deltaTime;
        slamTimer -= Time.deltaTime;
        chargeTimer -= Time.deltaTime;
        Vector3 dir = toPlayer / distance;

        // 수호 반격: it has soaked up enough blows and the player is still in reach
        if (blocks >= blocksToCounter && distance <= counterReach + 0.5f && cooldown <= 0.3f)
        {
            blocks = 0;
            StartAction(Counter(), attackCooldown);
            return Vector3.zero;
        }
        if (cooldown > 0f)
            return Hold(toPlayer, distance) * 0.5f;

        if (distance <= pushRange && pushTimer <= 0f)
        {
            pushTimer = pushCooldown;
            StartAction(ShieldPush(), attackCooldown);
            return Vector3.zero;
        }
        if (distance <= slamReach * 0.85f && slamTimer <= 0f)
        {
            slamTimer = slamCooldown;
            StartAction(ShieldSlam(), attackCooldown + 0.2f);
            return Vector3.zero;
        }
        if (distance >= chargeMin && distance <= chargeMax && chargeTimer <= 0f)
        {
            chargeTimer = chargeCooldown;
            StartAction(GuardCharge(), attackCooldown);
            return Vector3.zero;
        }
        return Hold(toPlayer, distance);
    }

    // Where it walks: toward the player, but with an ally to protect it plants itself on the line between the two
    private Vector3 Hold(Vector3 toPlayer, float distance)
    {
        if ((wardCheck -= Time.deltaTime) <= 0f)
        {
            wardCheck = 1f;
            ward = null;
            float best = 9f;
            foreach (Enemy e in Enemy.Alive)
            {
                if (e == null || e == this || e is WallGuardian || e.IsDead) continue;
                float d = FlatDistance(e.transform.position, transform.position);
                if (d < best && e.HomeRoom == HomeRoom) { best = d; ward = e; }
            }
        }
        Vector3 dir = toPlayer / Mathf.Max(0.01f, distance);
        if (ward != null && player != null)
        {
            // a couple of metres out from its ward toward the player
            Vector3 wardPos = ward.transform.position;
            Vector3 line = Flat(player.position - wardPos);
            if (line.magnitude > 3f)
            {
                Vector3 post = wardPos + line.normalized * 2.4f;
                Vector3 to = Flat(post - transform.position);
                if (to.magnitude > 0.5f)
                    return Vector3.ClampMagnitude(to, 1f) * moveSpeed;
                return Vector3.zero;
            }
        }
        return distance > pushRange * 0.8f ? dir * moveSpeed : Vector3.zero;
    }

    // ───────────── 방패 밀치기 ─────────────

    private IEnumerator ShieldPush()
    {
        FacePlayer();
        PlayAt("Push", pushOffset);
        pushing = true;
        Vector3 dir = Facing;
        warning = Telegraph.Line(Flat(transform.position), dir, pushRange + 1.2f, 1.8f, pushWindup);
        EnemyFeel.Windup(transform, ShieldPoint, GuardGold, FeelSize);
        ShieldGlow(0.6f);
        yield return new WaitForSeconds(pushWindup);
        if (warning != null) Destroy(warning);
        bool hit = false;
        float dist = 2.2f, secs = 0.22f;
        actionMove = dir * (dist / secs);
        for (float t = 0f; t < secs && !dead; t += Time.deltaTime)
        {
            if (!hit && playerStats != null && FlatDistance(player.position, ShieldPoint) <= 1.4f * FeelSize * 0.7f + 0.6f)
            {
                hit = true;
                playerStats.TakeDamage(attackDamage * OutgoingDamageMultiplier, this);
                player.GetComponent<PlayerMovement>()?.Push(dir * 11f);
                Vfx.Sprite(Vfx.Pick("guard_bash", "flare"), ShieldPoint, 2.4f, Color.white * 1.3f, 0.3f, 0.6f, 1.3f, 0f);
                EnemyFeel.Shake(transform.position, 0.15f, 0.12f);
            }
            yield return null;
        }
        actionMove = Vector3.zero;
        if (!hit)
            Vfx.Sprite(Vfx.Pick("guard_bash", "flare"), ShieldPoint, 1.6f, GuardGold, 0.25f, 0.6f, 1.1f, 0f);
        pushing = false;
        Open();
        yield return new WaitForSeconds(0.55f);
    }

    // ───────────── 방패 강타 ─────────────

    private IEnumerator ShieldSlam()
    {
        EnemyVoice.Of(this)?.Skill();   // (its own big-move sound)
        FacePlayer();
        PlayAt("Slam", slamOffset);
        slamming = true;
        Vector3 dir = Facing;
        warning = Telegraph.Line(Flat(transform.position), dir, slamReach, slamReach * Mathf.Tan(slamHalfArc * Mathf.Deg2Rad) * 1.4f, slamWindup);
        EnemyFeel.Windup(transform, transform.position + Vector3.up * 2f * FeelSize, GuardGold, FeelSize);
        ShieldGlow(1f);
        yield return new WaitForSeconds(slamWindup);
        if (warning != null) Destroy(warning);
        Vector3 at = Flat(transform.position + dir * 1.2f * FeelSize);
        Quaternion flat = Quaternion.LookRotation(Vector3.down, dir);   // the sheet's top (where the fan widens) points ahead
        // the wedge of force: the sheet's impact point (its bottom middle) at the shield, spreading out ahead
        Vfx.Sprite(Vfx.Pick("guard_shockwave", "ring"), at + dir * slamReach * 0.45f + Vector3.up * 0.1f, slamReach * 1.1f, Color.white * 1.2f, 0.5f, 0.85f, 1.1f, 0f, flat);
        Vfx.Sprite(Vfx.Pick("guard_crack", "ring"), at + Vector3.up * 0.06f, 3f, Color.white, 1.8f, 0.9f, 1f, 0f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f)).Hold();
        Vfx.Sprite(Vfx.Pick("guard_bash", "flare"), at + Vector3.up * 0.8f, 2.6f, Color.white * 1.2f, 0.35f, 0.6f, 1.3f, 0f);
        Vfx.Dust(at, 2f, 12, new Color(0.9f, 0.88f, 0.8f, 0.5f));
        EnemyFeel.Impact(at, 2.5f, GuardGold, 0.3f);
        if (!dead && playerStats != null && InFront(slamReach, slamHalfArc + 8f))
        {
            playerStats.TakeDamage(attackDamage * 1.2f * OutgoingDamageMultiplier, this);
            player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - transform.position).normalized * 8f);
        }
        slamming = false;
        Open();
        yield return new WaitForSeconds(0.8f);
    }

    // ───────────── 돌진 방어 ─────────────

    private IEnumerator GuardCharge()
    {
        FacePlayer();
        Vector3 dir = player != null ? Flat(player.position - transform.position).normalized : Facing;
        float length = player != null ? Mathf.Min(Flat(player.position - transform.position).magnitude + 2.5f, chargeMax + 2f) : chargeMax;
        warning = Telegraph.Line(Flat(transform.position), dir, length, 2f, chargeWindup);
        PlayAt("Block", 0f);
        ShieldGlow(1f);
        EnemyFeel.Roar(transform, GuardGold, 2.5f * FeelSize, 0.1f);
        yield return new WaitForSeconds(chargeWindup);
        if (warning != null) Destroy(warning);
        PlayAt("Charge", 0f);
        transform.rotation = Quaternion.LookRotation(dir);
        charging = true;
        actionMove = dir * chargeSpeed;
        EnemyFeel.Lunge(transform.position, dir, GuardGold, FeelSize);
        bool hit = false;
        float travelled = 0f, wave = 0f, thud = 0f;
        Vector3 last = transform.position;
        while (travelled < length && !dead && !hit)
        {
            yield return null;
            float step = Flat(transform.position - last).magnitude;
            travelled += step;
            last = transform.position;
            // the bow-wave of force ahead of the shield
            if ((wave -= Time.deltaTime) <= 0f)
            {
                wave = 0.12f;
                Vfx.Sprite(Vfx.Pick("guard_rush", "flare"), ShieldPoint + dir * 0.4f, 2.4f, GuardGold * 1.1f, 0.25f, 0.8f, 1.1f, 0f,
                    Quaternion.LookRotation(Vector3.down, dir) * Quaternion.Euler(0f, 0f, 90f));
            }
            if ((thud -= Time.deltaTime) <= 0f)
            {
                thud = 0.22f;
                Vfx.Dust(transform.position, 0.8f, 3);
                EnemyFeel.Shake(transform.position, 0.08f, 0.05f);
            }
            if (playerStats != null && FlatDistance(player.position, ShieldPoint) <= 1.5f)
            {
                hit = true;
                playerStats.TakeDamage(attackDamage * 1.3f * OutgoingDamageMultiplier, this);
                player.GetComponent<PlayerMovement>()?.Push(dir * 13f + Vector3.Cross(Vector3.up, dir) * 2f);
                Vfx.Sprite(Vfx.Pick("guard_bash", "flare"), ShieldPoint, 3f, Color.white * 1.4f, 0.35f, 0.6f, 1.4f, 0f);
                EnemyFeel.Shake(transform.position, 0.2f, 0.18f);
            }
            if ((lastMoveFlags & CollisionFlags.Sides) != 0 && step < chargeSpeed * Time.deltaTime * 0.3f)
                break;
        }
        actionMove = Vector3.zero;
        charging = false;
        PlayAt("Locomotion", 0f, 0.25f);
        Vfx.Dust(transform.position, 1f, 6);
        Open();
        yield return new WaitForSeconds(0.6f);
    }

    // ───────────── 수호 반격 ─────────────

    private IEnumerator Counter()
    {
        countering = true;
        FacePlayer();
        // the shield sweeps aside with a flash...
        ShieldGlow(1.4f);
        Vfx.Sprite(Vfx.Pick("guard_block", "flare"), ShieldPoint, 2.4f, Color.white * 1.4f, 0.3f, 1f, 1.4f, 0f);
        PlayAt("Counter", counterOffset);
        EnemyFeel.Windup(transform, WeaponPoint, GuardGold, FeelSize);
        yield return new WaitForSeconds(counterWindup);
        // ...and the sword comes round
        Vector3 at = transform.position + Vector3.up * 1.1f * FeelSize + Facing * 1.2f;
        Vfx.Sprite(Vfx.Pick("guard_counter", "slash"), at, counterReach * 1.6f, Color.white * 1.4f, 0.3f, 0.8f, 1.15f, -80f,
            Quaternion.LookRotation(Vector3.down, Facing) * Quaternion.Euler(0f, 0f, 0f));
        EnemyFeel.Swing(transform.position + Vector3.up * FeelSize, Facing, counterReach, GuardGold);
        if (!dead && playerStats != null && InFront(counterReach, 75f))
        {
            playerStats.TakeDamage(attackDamage * counterDamageMultiplier * OutgoingDamageMultiplier, this);
            player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - transform.position).normalized * 7f);
            Vfx.Sprite(Vfx.Pick("guard_bash", "flare"), player.position + Vector3.up, 1.8f, Color.white * 1.2f, 0.25f, 0.6f, 1.2f, 0f);
        }
        countering = false;
        Open();
        yield return new WaitForSeconds(0.6f);
    }

    // ───────────── Shield ─────────────

    // the opening after an attack: the shield is down for a moment
    private void Open() => openUntil = Time.time + openAfterAttack;

    // ── The shield brace: the clips (borrowed motions) carry the shield wherever the forearm goes; while it guards,
    // the shield is held upright in front of its body instead (fully for a charge, a shove or a block, mostly while it
    // stands guard), and left to the clip for the slam, the counter and the opening after an attack.
    private float brace, braceGoal, braceHoldUntil;

    private void LateUpdate()
    {
        if (shield == null)
            return;
        if (dead) braceGoal = 0f;
        else if (charging || pushing || Time.time < braceHoldUntil) braceGoal = 1f;
        else if (countering || slamming || Time.time < openUntil) braceGoal = 0f;
        else braceGoal = 0.8f;
        brace = Mathf.MoveTowards(brace, braceGoal, Time.deltaTime * (braceGoal > brace ? 6f : 3f));
        if (brace <= 0.001f)
            return;
        float s = FeelSize;
        Vector3 anchor = transform.position + Vector3.up * 1.0f * s + transform.forward * 0.5f * s - transform.right * 0.18f * s;
        Quaternion upright = Quaternion.LookRotation(transform.forward, Vector3.up);
        shield.SetPositionAndRotation(Vector3.Lerp(shield.position, anchor, brace), Quaternion.Slerp(shield.rotation, upright, brace));
    }

    private bool pushing, slamming;

    private Vector3 ShieldPoint => shield != null ? shield.position : transform.position + Vector3.up * 1.1f * FeelSize + Facing * 0.6f * FeelSize;

    private void ShieldGlow(float strength)
    {
        Vfx.Sprite(Vfx.Pick("guard_block", "flare"), ShieldPoint + Facing * 0.15f, 1.6f * Mathf.Max(1f, FeelSize * 0.8f), RuneBlue * strength, 0.35f, 0.9f, 1.15f, 0f);
    }

    public override void TakeHit(Vector3 from, int damage)
    {
        if (dead)
            return;
        if (Guarding && FromFront(from))
        {
            // the blow rings off the shield: its rune flares, sparks fly, and only a sliver gets through
            blocks = Time.time - lastBlock < 3f ? blocks + 1 : 1;
            lastBlock = Time.time;
            Vector3 at = ShieldPoint + Flat(from - transform.position).normalized * 0.2f;
            Vfx.Sprite(Vfx.Pick("guard_block", "flare"), at, 1.8f * Mathf.Max(1f, FeelSize * 0.8f), Color.white * 1.2f, 0.25f, 0.85f, 1.2f, 0f);
            Vfx.Burst(at, GuardGold, 8, 5f, 0.12f, 0.6f);
            braceHoldUntil = Time.time + 0.5f;
            int through = Mathf.Max(1, Mathf.RoundToInt(damage * blockedDamage));
            base.TakeHit(transform.position + Facing * 5f, through);   // (from straight ahead: no flinch spin, little knockback)
            return;
        }
        base.TakeHit(from, damage);
    }

    // too heavy to throw down: it staggers, and its guard drops for a moment
    public override void Knockdown(float seconds)
    {
        if (dead)
            return;
        Interrupt();
        if (animator != null && animator.HasState(0, Animator.StringToHash("Base Layer.Hit")))
            animator.CrossFadeInFixedTime("Hit", 0.06f);
        cooldown = Mathf.Max(cooldown, Mathf.Min(seconds, 1.2f));
        openUntil = Time.time + Mathf.Min(seconds, 1.5f);
    }

    public override void Interrupt()
    {
        charging = false;
        countering = false;
        pushing = false;
        slamming = false;
        base.Interrupt();
    }

    private void PlayAt(string state, float offset, float fade = 0.15f)
    {
        if (animator == null)
            return;
        int hash = Animator.StringToHash("Base Layer." + state);
        if (animator.HasState(0, hash))
            animator.CrossFadeInFixedTime(hash, fade, 0, offset);
    }
}
