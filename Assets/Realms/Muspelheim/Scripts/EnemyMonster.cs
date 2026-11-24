using UnityEngine;

// Melee monster with smoothed motion: stands idle on the field (or, with startOnField off, hides under the floor
// and rises up) until the player comes close, chases with acceleration/deceleration, does an eased wind-up and lunge, reacts to hits with a squash
// and knockback that ease out, and tips over and sinks on death. Stun stops it in place, dazed and wobbling.
// The mesh should be a child ("visual") so the procedural body motion does not move the controller.
// Rigged monsters play legacy idle / walk / attack clips; the walk clip speed follows the actual move speed
// so the feet keep pace, and the procedural bob and sway are skipped. Flying monsters bank into their turns.
[RequireComponent(typeof(CharacterController))]
public class EnemyMonster : MonoBehaviour
{
    enum State { Hidden, Appearing, Chase, WindUp, Lunge, Recover, Dead }

    [Header("Stats")]
    public int maxHealth = 30;
    public int attackDamage = 10;

    [Header("Ranges")]
    [Tooltip("The monster notices the player (or rises from the floor) inside this range.")]
    public float appearRange = 12f;
    [Tooltip("Stand visible on the field from the start instead of rising out of the floor when the player comes close.")]
    public bool startOnField = true;
    public float attackRange = 2.2f;
    public float hitRadius = 1.4f;

    [Header("Movement")]
    public float moveSpeed = 2.5f;
    public float acceleration = 8f;
    public float turnSmoothTime = 0.18f;
    public float hoverHeight = 0f;     // > 0 for flying monsters
    public float bobAmount = 0.12f;
    public float strideLength = 1.6f;  // metres per step cycle, ties the bob to the actual speed

    [Header("Attack timing")]
    public float windUpTime = 0.55f;
    public float lungeTime = 0.3f;
    public float lungeDistance = 2.2f;
    public float recoverTime = 0.7f;

    [Header("Animation (rigged monsters)")]
    public Animation anim;
    public string idleClip = "idle";
    public string walkClip = "walk";
    public string attackClip = "attack";
    [Tooltip("Ground speed (m/s) the walk clip covers at playback speed 1; the clip is sped up or slowed to match.")]
    public float walkClipGroundSpeed = 1.5f;
    [Tooltip("Point in the attack clip (0..1) where the blow lands; the wind-up lasts until then.")]
    [Range(0f, 1f)] public float attackHitPoint = 0.45f;
    public float attackClipSpeed = 1f;
    public float crossFade = 0.2f;

    [Header("Flying")]
    public float bankAmount = 0.12f;   // degrees of roll per degree/second of turning

    [Header("Special attack")]
    [Tooltip("Every this many attacks is its special one (0 = never): a longer wind-up, a longer lunge, harder hit.")]
    public int specialEvery = 3;
    public float specialWindUpScale = 1.6f;
    public float specialLungeScale = 1.8f;
    public float specialDamageScale = 1.5f;

    // Attack phases, for the skill effects: "windup", "strike", "hit", "recover" (each prefixed "special-" on the
    // special attack) and "death"
    public event System.Action<EnemyMonster, string> Phase;
    public bool Special => special;
    int attackCount;
    bool special;

    // Only a few monsters attack at once; the rest wait their turn a step back, so a crowd doesn't all swing together
    public static int maxAttackers = 3;
    static readonly System.Collections.Generic.HashSet<EnemyMonster> attackers = new System.Collections.Generic.HashSet<EnemyMonster>();
    [Tooltip("Seconds it waits after an attack before it may attack again (picked at random between these).")]
    public Vector2 attackCooldown = new Vector2(0.8f, 1.8f);
    float nextAttackTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAttackers() => attackers.Clear();

    bool TryTakeSlot()
    {
        attackers.RemoveWhere(m => m == null || m.IsDead || !m.isActiveAndEnabled);
        if (attackers.Contains(this)) return true;
        if (attackers.Count >= maxAttackers) return false;
        attackers.Add(this);
        return true;
    }

    void OnDisable() => attackers.Remove(this);

    // For monster roles (MonsterSkillFx): faster or slower attacks, and a tougher or frailer body
    public void SetTempo(float scale)
    {
        scale = Mathf.Max(0.2f, scale);
        attackClipSpeed *= scale;
        windUpTime /= scale;
        lungeTime /= scale;
        recoverTime /= scale;
        if (Animated && anim.GetClip(attackClip) != null) anim[attackClip].speed = attackClipSpeed;
    }

    public void SetMaxHealth(int hp)
    {
        maxHealth = Mathf.Max(1, hp);
        health = maxHealth;
    }

    [Header("References")]
    public Transform visual;

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    CharacterController controller;
    Transform player;
    Renderer[] renderers;
    MaterialPropertyBlock block;
    Vector3 visualBasePos;
    Vector3 visualBaseScale;
    State state = State.Hidden;
    float stateTimer;
    int health;

    Vector3 velocity;            // smoothed horizontal velocity
    float yawVelocity;           // for SmoothDampAngle
    float verticalVelocity;
    Vector3 knockback;
    float stridePhase;
    float lean, leanVelocity;    // smoothed forward lean in degrees
    float squash;                // 0 = rest, >0 = squashed (hit / lunge)
    float squashVelocity;
    float flashTimer;
    bool hitApplied;
    float bank, bankVelocity, lastYaw;

    bool Animated => anim != null && anim.GetClip(walkClip) != null;
    PlayerHealth playerHealth;
    bool swingCancelled;
    float stunTimer;
    bool PlayerDown => playerHealth != null && playerHealth.IsDead;

    public bool IsStunned => stunTimer > 0f;
    public bool IsDead => state == State.Dead;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
        visualBasePos = visual.localPosition;
        visualBaseScale = visual.localScale;
        health = maxHealth;
        if (anim == null) anim = GetComponentInChildren<Animation>();
        if (Animated)
        {
            anim[walkClip].wrapMode = WrapMode.Loop;
            if (anim.GetClip(idleClip) != null) anim[idleClip].wrapMode = WrapMode.Loop;
            if (anim.GetClip(attackClip) != null)
            {
                anim[attackClip].wrapMode = WrapMode.ClampForever;
                anim[attackClip].speed = attackClipSpeed;
                // the wind-up lasts until the blow lands in the clip, the lunge covers the strike itself
                float clipTime = anim[attackClip].length / attackClipSpeed;
                windUpTime = clipTime * attackHitPoint;
                lungeTime = Mathf.Max(0.15f, clipTime * 0.15f);
                recoverTime = Mathf.Max(0.3f, clipTime * (1f - attackHitPoint) - lungeTime);
            }
        }
        lastYaw = transform.eulerAngles.y;
        // its skill effects (and the special attack's extra blast), picked by what kind of monster it is
        if (GetComponent<MonsterSkillFx>() == null) gameObject.AddComponent<MonsterSkillFx>();
    }

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        SetHidden(!startOnField);
    }

    // Can't be hurt while rising out of the floor, or hidden under it.
    bool Untouchable => state == State.Dead || state == State.Appearing || (state == State.Hidden && !startOnField);

    void Update()
    {
        float dt = Time.deltaTime;
        stateTimer += dt;
        if (state == State.Dead) { UpdateDeath(dt); return; }
        if (player == null) return;
        // knocked off the path into the lava
        if (transform.position.y < -3f) { Enter(State.Dead); return; }

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;
        Vector3 desiredVelocity = Vector3.zero;
        float targetLean = 0f;

        if (stunTimer > 0f)
        {
            // dazed: no chasing or swinging, just a slow wobble until it comes to
            stunTimer -= dt;
            targetLean = Mathf.Sin(Time.time * 9f) * 9f;
            if (stunTimer <= 0f && state != State.Dead)
            {
                if (Animated && anim.GetClip(idleClip) != null) anim[idleClip].speed = 1f;
                Enter(State.Chase);
            }
        }
        else switch (state)
        {
            case State.Hidden:
                // waiting on the field (idle clip) or under the floor until the player comes near
                if (dist < appearRange)
                {
                    if (startOnField) Enter(State.Chase);
                    else { SetHidden(false); Enter(State.Appearing); }
                }
                break;

            case State.Appearing:
                Face(toPlayer, dt);
                if (stateTimer >= 0.9f) Enter(State.Chase);
                break;

            case State.Chase:
                Face(toPlayer, dt);
                // slow down smoothly as we close in, instead of stopping dead
                float speedScale = Mathf.Clamp01((dist - attackRange * 0.8f) / 1.5f);
                desiredVelocity = toPlayer.normalized * moveSpeed * speedScale;
                targetLean = 8f * (velocity.magnitude / Mathf.Max(moveSpeed, 0.01f));
                // it closes right in either way (into the player's reach); it only swings when one of the few attack
                // slots is free and it has caught its breath since its last blow
                if (dist <= attackRange && Time.time >= nextAttackTime && TryTakeSlot()) Enter(State.WindUp);
                break;

            case State.WindUp:
                Face(toPlayer, dt);
            {
                float windUp = windUpTime * (special ? specialWindUpScale : 1f);
                targetLean = -22f * EaseOut(stateTimer / windUp);
                if (stateTimer >= windUp) { hitApplied = false; Enter(State.Lunge); }
                break;
            }

            case State.Lunge:
            {
                // bell-shaped speed curve: accelerate, then brake, covering lungeDistance
                float t = Mathf.Clamp01(stateTimer / lungeTime);
                float reach = lungeDistance * (special ? specialLungeScale : 1f);
                float speed = reach / lungeTime * Mathf.PI / 2f * Mathf.Sin(t * Mathf.PI);
                velocity = transform.forward * speed;
                desiredVelocity = velocity;
                targetLean = 28f;
                squash = Mathf.Max(squash, 0.12f * Mathf.Sin(t * Mathf.PI));
                if (!hitApplied && !PlayerDown && Vector3.Distance(transform.position + transform.forward, player.position) < hitRadius + 0.5f)
                {
                    hitApplied = true;
                    int damage = special ? Mathf.RoundToInt(attackDamage * specialDamageScale) : attackDamage;
                    player.SendMessage("TakeHit", damage, SendMessageOptions.DontRequireReceiver);
                    Phase?.Invoke(this, special ? "special-hit" : "hit");
                }
                if (t >= 1f) Enter(State.Recover);
                break;
            }

            case State.Recover:
                Face(toPlayer, dt * 0.5f);
                if (stateTimer >= recoverTime) Enter(State.Chase);
                break;
        }

        // ease the velocity toward the target instead of snapping
        if (state != State.Lunge)
            velocity = Vector3.MoveTowards(velocity, desiredVelocity, acceleration * dt);

        if (hoverHeight <= 0f)
            verticalVelocity = controller.isGrounded ? -2f : verticalVelocity - 20f * dt;

        Vector3 move = velocity + knockback;
        move.y = verticalVelocity;
        if (controller.enabled) controller.Move(move * dt);
        knockback = Vector3.Lerp(knockback, Vector3.zero, 1f - Mathf.Exp(-6f * dt));

        // the clips carry the body motion of rigged monsters, so only a hint of lean is added on top
        if (Animated) targetLean *= 0.25f;
        lean = Mathf.SmoothDamp(lean, targetLean, ref leanVelocity, 0.12f);
        squash = Mathf.SmoothDamp(squash, 0f, ref squashVelocity, 0.15f);
        float yaw = transform.eulerAngles.y;
        float yawRate = dt > 0f ? Mathf.DeltaAngle(lastYaw, yaw) / dt : 0f;
        lastYaw = yaw;
        bank = Mathf.SmoothDamp(bank, Mathf.Clamp(-yawRate * bankAmount, -25f, 25f), ref bankVelocity, 0.25f);
        UpdateClips();
        AnimateVisual(dt);
        UpdateFlash(dt);
    }

    // Called by the player's attack.
    public void TakeHit(int damage)
    {
        if (Untouchable) return;
        if (state == State.Hidden) Enter(State.Chase);   // hit from afar while idle: it comes for the player
        health -= damage;
        flashTimer = 0.12f;
        squash = 0.25f;
        HitFeedback.Hit(controller.bounds.center + Vector3.up * controller.bounds.extents.y * 0.3f, damage);
        if (player != null)
        {
            Vector3 away = transform.position - player.position;
            away.y = 0f;
            knockback = away.normalized * 6f;
        }
        if (health <= 0) Enter(State.Dead);
        else if (state == State.WindUp)
        {
            // hits interrupt the wind-up: drop the swing instead of playing it out through the player
            Enter(State.Recover);
            swingCancelled = true;
        }
    }

    // Burns away on the spot (Surtr's altar is his alone: monsters that followed the player in are swallowed by the fire).
    public void Vanish()
    {
        if (state == State.Dead) return;
        health = 0;
        Enter(State.Dead);
    }

    // Called by the player's thunder strike: cancels any swing and holds the monster in place.
    public void Stun(float seconds)
    {
        if (Untouchable) return;
        if (state == State.WindUp || state == State.Lunge) swingCancelled = true;
        Enter(State.Recover);
        stunTimer = Mathf.Max(stunTimer, seconds);
        if (Animated && anim.GetClip(idleClip) != null)
        {
            anim[idleClip].speed = 0.25f;   // the idle slows to a daze
            anim.CrossFade(idleClip, 0.1f);
        }
    }

    void Enter(State next)
    {
        State previous = state;
        state = next;
        stateTimer = 0f;
        // done swinging (or down): its slot goes to the next in line, and it rests a moment before trying again
        if (next == State.Chase || next == State.Dead)
        {
            if (attackers.Remove(this)) nextAttackTime = Time.time + Random.Range(attackCooldown.x, attackCooldown.y);
        }
        if (next == State.Dead)
        {
            controller.enabled = false;
            // freeze the current pose; stopping would snap a rigged model back to its bind pose
            if (anim != null) foreach (AnimationState s in anim) s.speed = 0f;
            if (previous != State.Dead) Phase?.Invoke(this, "death");
            return;
        }
        if (next == State.WindUp)
        {
            swingCancelled = false;
            attackCount++;
            special = specialEvery > 0 && attackCount % specialEvery == 0;
        }
        if (next == State.WindUp && Animated && anim.GetClip(attackClip) != null)
        {
            anim[attackClip].time = 0f;
            // the special wind-up is slower, so the clip is slowed to land its blow at the same moment
            anim[attackClip].speed = attackClipSpeed / (special ? specialWindUpScale : 1f);
            anim.CrossFade(attackClip, 0.1f);
        }
        if (next == State.Lunge && Animated && anim.GetClip(attackClip) != null)
            anim[attackClip].speed = attackClipSpeed;
        string prefix = special ? "special-" : "";
        if (next == State.WindUp) Phase?.Invoke(this, prefix + "windup");
        else if (next == State.Lunge) Phase?.Invoke(this, prefix + "strike");
        else if (next == State.Recover && previous == State.Lunge) Phase?.Invoke(this, prefix + "recover");
    }

    void Face(Vector3 dir, float dt)
    {
        if (dir.sqrMagnitude < 0.01f) return;
        float target = Quaternion.LookRotation(dir).eulerAngles.y;
        float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, target, ref yawVelocity, turnSmoothTime, Mathf.Infinity, dt);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    // Picks the clip for the current state; the walk clip runs at the speed that keeps the feet planted.
    void UpdateClips()
    {
        if (!Animated) return;
        float speed = new Vector2(velocity.x, velocity.z).magnitude;
        bool attacking = state == State.WindUp || state == State.Lunge ||
            (state == State.Recover && !swingCancelled && anim.IsPlaying(attackClip) && anim[attackClip].normalizedTime < 1f);
        if (attacking && anim.GetClip(attackClip) != null) return;   // started in Enter(WindUp)
        if (speed > 0.25f)
        {
            anim[walkClip].speed = Mathf.Clamp(speed / Mathf.Max(walkClipGroundSpeed, 0.01f), 0.5f, 4.5f);
            if (!anim.IsPlaying(walkClip)) anim.CrossFade(walkClip, crossFade);
        }
        else if (anim.GetClip(idleClip) != null && !anim.IsPlaying(idleClip))
            anim.CrossFade(idleClip, crossFade);
    }

    void AnimateVisual(float dt)
    {
        Vector3 pos = visualBasePos;
        float speed = new Vector2(velocity.x, velocity.z).magnitude;

        if (state == State.Appearing)
        {
            float t = Mathf.Clamp01(stateTimer / 0.9f);
            pos += Vector3.down * (1f - EaseOutBack(t)) * 3f;
        }

        // stride bob tied to distance travelled, faded in with speed (the walk clip does this for rigged monsters)
        stridePhase += speed / Mathf.Max(strideLength, 0.01f) * Mathf.PI * 2f * dt;
        float moveAmount = Animated ? 0f : Mathf.Clamp01(speed / Mathf.Max(moveSpeed, 0.01f));
        float bob = (0.5f - 0.5f * Mathf.Cos(stridePhase * 2f)) * bobAmount * moveAmount;
        float sway = Mathf.Sin(stridePhase) * 4f * moveAmount;     // side-to-side roll while walking
        float breathe = Animated ? 0f : Mathf.Sin(Time.time * 2.2f) * 0.02f;   // idle breathing

        if (hoverHeight > 0f)
        {
            // slow uneven glide: two overlapping waves so the bob never looks like a metronome
            pos.y += hoverHeight + Mathf.Sin(Time.time * 1.6f) * 0.22f + Mathf.Sin(Time.time * 2.9f + 1.3f) * 0.08f;
            sway = bank + Mathf.Sin(Time.time * 1.1f) * 3f;      // bank into turns plus a gentle drift
        }
        pos.y += bob;

        visual.localPosition = pos;
        visual.localRotation = Quaternion.Euler(lean, 0f, sway);
        float sy = 1f + breathe - squash;
        float sxz = 1f - breathe * 0.5f + squash * 0.6f;
        visual.localScale = Vector3.Scale(visualBaseScale, new Vector3(sxz, sy, sxz));
    }

    void UpdateFlash(float dt)
    {
        if (flashTimer <= 0f) return;
        flashTimer -= dt;
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(block);
            if (flashTimer > 0f) block.SetColor(EmissionColorId, Color.white * 3f);
            else block.Clear();
            r.SetPropertyBlock(block);
        }
    }

    void UpdateDeath(float dt)
    {
        UpdateFlash(dt);
        // tip over backwards, then shrink and sink
        float tip = Smooth(stateTimer / 0.35f);
        float fade = Smooth((stateTimer - 0.35f) / 0.5f);
        visual.localRotation = Quaternion.Euler(-70f * tip, 0f, 0f);
        visual.localScale = visualBaseScale * (1f - fade);
        visual.localPosition = visualBasePos + Vector3.down * fade * 0.8f;
        transform.position += knockback * dt;
        knockback = Vector3.Lerp(knockback, Vector3.zero, 1f - Mathf.Exp(-6f * dt));
        if (stateTimer >= 0.85f) Destroy(gameObject);
    }

    void SetHidden(bool hidden)
    {
        foreach (var r in renderers) r.enabled = !hidden;
        controller.detectCollisions = !hidden;
    }

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, appearRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
