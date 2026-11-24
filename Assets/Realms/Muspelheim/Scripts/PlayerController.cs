using UnityEngine;
using UnityEngine.InputSystem;

// WASD movement (Shift to run) relative to the camera, left mouse button to attack, Space to dodge.
// Plays legacy animation clips named idle / walk / run / attack when they exist on the Animation component.
// The dodge is a short eased dash with invincibility frames; it can cancel the recovery after a swing lands.
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 4f;
    public float runSpeed = 8f;
    public float turnSpeed = 720f;
    public float gravity = -20f;

    [Header("Attack")]
    public float attackDuration = 0.9f;
    public float attackHitTime = 0.45f;
    public float attackRange = 2.2f;
    public float attackRadius = 1.6f;
    public int attackDamage = 10;

    [Header("Animation")]
    public Animation anim;
    public string idleClip = "idle";
    public string walkClip = "walk";
    public string runClip = "run";
    public string attackClip = "attack";
    [Tooltip("Played once when health reaches 0: drops to the knees, reaches up in agony and collapses.")]
    public string deathClip = "death";
    public float crossFade = 0.15f;
    [Tooltip("Faster blend into idle, so the legs stop when the body stops instead of running on the spot.")]
    public float stopCrossFade = 0.06f;
    [Tooltip("Playback speed of the walk / run clips, tuned so the planted foot keeps pace with the movement speed.")]
    public float walkClipSpeed = 1f;
    public float runClipSpeed = 1f;
    [Tooltip("With an attack clip, the attack lasts as long as the clip at this speed.")]
    public float attackClipSpeed = 1f;
    [Tooltip("Fraction of the attack clip where the weapon connects.")]
    [Range(0f, 1f)] public float attackHitPoint = 0.45f;

    [Header("Dodge")]
    public float dodgeDistance = 5f;
    public float dodgeTime = 0.32f;
    public float dodgeCooldown = 0.45f;
    [Tooltip("Seconds from the start of the dodge during which hits are ignored.")]
    public float dodgeInvulnerable = 0.28f;
    [Tooltip("Additive material for the afterimages left along the dash.")]
    public Material afterimageMaterial;
    public float afterimageInterval = 0.06f;

    CharacterController controller;
    Transform cam;
    float verticalVelocity;
    float attackTimer;
    bool hitApplied;
    float dodgeTimer;          // counts up while dodging, -1 otherwise
    float dodgeCooldownTimer;
    float afterimageTimer;
    Vector3 dodgeDir;
    HammerHold hammerHold;
    PlayerHealth health;

    [HideInInspector] public bool movementLocked;   // set by the boss chain bind
    // set by PlayerSkills while a skill runs: scales the move speed, blocks the basic attack, overrides the clip
    [HideInInspector] public float speedScale = 1f;
    [HideInInspector] public bool skillBusy;
    [HideInInspector] public string skillClip;

    public float DodgeCooldownLeft => dodgeCooldownTimer;

    public bool IsAttacking => attackTimer > 0f;
    public bool IsDodging => dodgeTimer >= 0f;
    public bool IsInvulnerable => IsDodging && dodgeTimer < dodgeInvulnerable;
    public float AttackProgress => 1f - attackTimer / attackDuration;
    public bool UsesAttackClip => HasClip(attackClip);

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (anim == null) anim = GetComponentInChildren<Animation>();
        if (HasClip(walkClip)) anim[walkClip].speed = walkClipSpeed;
        if (HasClip(runClip)) anim[runClip].speed = runClipSpeed;
        if (UsesAttackClip)
        {
            var state = anim[attackClip];
            state.speed = attackClipSpeed;
            state.wrapMode = WrapMode.ClampForever;
            attackDuration = state.length / attackClipSpeed;
            attackHitTime = attackDuration * attackHitPoint;
        }
        if (HasClip(deathClip)) anim[deathClip].wrapMode = WrapMode.ClampForever;   // stays down on the last frame
        if (Camera.main != null) cam = Camera.main.transform;
        dodgeTimer = -1f;
        hammerHold = GetComponentInChildren<HammerHold>();
        health = GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (health != null && health.IsDead)
        {
            dodgeTimer = -1f;
            attackTimer = 0f;
            verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * Time.deltaTime;
            controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
            Play(deathClip, 0.2f);
            return;
        }

        var kb = Keyboard.current;
        var mouse = Mouse.current;

        Vector2 input = Vector2.zero;
        if (kb != null)
        {
            if (kb.wKey.isPressed) input.y += 1f;
            if (kb.sKey.isPressed) input.y -= 1f;
            if (kb.dKey.isPressed) input.x += 1f;
            if (kb.aKey.isPressed) input.x -= 1f;
        }
        input = Vector2.ClampMagnitude(input, 1f);
        bool running = kb != null && kb.leftShiftKey.isPressed;
        if (movementLocked) input = Vector2.zero;

        Vector3 camForward = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
        Vector3 camRight = Vector3.Cross(Vector3.up, camForward);
        Vector3 inputDir = (camForward * input.y + camRight * input.x).normalized;

        if (dodgeCooldownTimer > 0f) dodgeCooldownTimer -= Time.deltaTime;
        // a dodge may cut off the recovery of a swing that has already landed, but not the wind-up
        bool canDodge = !movementLocked && !IsDodging && dodgeCooldownTimer <= 0f && (!IsAttacking || hitApplied);
        if (canDodge && kb != null && kb.spaceKey.wasPressedThisFrame)
            StartDodge(input.sqrMagnitude > 0.01f ? inputDir : transform.forward);

        if (!movementLocked && !IsDodging && !skillBusy && mouse != null && mouse.leftButton.wasPressedThisFrame && !IsAttacking)
            StartAttack();

        Vector3 move = Vector3.zero;
        if (IsDodging) move = UpdateDodge();
        else if (!IsAttacking && input.sqrMagnitude > 0.01f)
        {
            move = inputDir * (running ? runSpeed : walkSpeed) * input.magnitude * speedScale;
            var targetRot = Quaternion.LookRotation(inputDir);   // turns even when a skill roots the hero (speedScale 0)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }

        verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * Time.deltaTime;
        move.y = verticalVelocity;
        controller.Move(move * Time.deltaTime);

        if (IsDodging) Play(runClip);
        else if (IsAttacking) UpdateAttack();
        else if (!string.IsNullOrEmpty(skillClip)) Play(skillClip);
        else if (input.sqrMagnitude > 0.01f) Play(running ? runClip : walkClip);
        else Play(idleClip, stopCrossFade);
    }

    void StartDodge(Vector3 dir)
    {
        dodgeDir = dir;
        dodgeTimer = 0f;
        afterimageTimer = 0f;
        attackTimer = 0f;   // cancels the swing recovery
        transform.rotation = Quaternion.LookRotation(dir);
    }

    // Returns this frame's velocity: fast out of the start, easing off at the end.
    Vector3 UpdateDodge()
    {
        float t0 = dodgeTimer / dodgeTime;
        dodgeTimer += Time.deltaTime;
        float t1 = Mathf.Min(dodgeTimer / dodgeTime, 1f);
        float step = dodgeDistance * (EaseOut(t1) - EaseOut(t0));

        afterimageTimer -= Time.deltaTime;
        if (afterimageTimer <= 0f && afterimageMaterial != null)
        {
            afterimageTimer = afterimageInterval;
            Afterimage.Spawn(gameObject, afterimageMaterial, hammerHold != null ? hammerHold.hammer : null);
        }

        if (t1 >= 1f)
        {
            dodgeTimer = -1f;
            dodgeCooldownTimer = dodgeCooldown;
        }
        return dodgeDir * step / Mathf.Max(Time.deltaTime, 0.0001f);
    }

    static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    void StartAttack()
    {
        attackTimer = attackDuration;
        hitApplied = false;
        if (HasClip(attackClip))
        {
            anim[attackClip].time = 0f;
            anim.CrossFade(attackClip, 0.1f);
        }
        else
        {
            // the swing itself is driven procedurally (see HammerHold); keep the body in its idle pose
            Play(idleClip);
        }
    }

    void UpdateAttack()
    {
        attackTimer -= Time.deltaTime;
        if (!hitApplied && attackDuration - attackTimer >= attackHitTime)
        {
            hitApplied = true;
            Vector3 center = transform.position + transform.forward * attackRange + Vector3.up;
            foreach (var hit in Physics.OverlapSphere(center, attackRadius))
            {
                if (hit.transform.IsChildOf(transform)) continue;
                hit.SendMessageUpwards("TakeHit", attackDamage, SendMessageOptions.DontRequireReceiver);
            }
        }
    }

    void Play(string clip) => Play(clip, crossFade);

    void Play(string clip, float fade)
    {
        if (HasClip(clip) && !anim.IsPlaying(clip)) anim.CrossFade(clip, fade);
    }

    bool HasClip(string clip) => anim != null && !string.IsNullOrEmpty(clip) && anim.GetClip(clip) != null;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.forward * attackRange + Vector3.up, attackRadius);
    }
}
