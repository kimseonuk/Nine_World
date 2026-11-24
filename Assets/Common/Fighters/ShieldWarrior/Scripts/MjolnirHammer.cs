using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Muspelheim's hammer (묠니르) and the viking shield warrior who carries it, as the Muspelheim project built them: its
// PlayerController's swing and dodge and its PlayerSkills (numbers from its scene), on the game's player. The model plays
// the project's own legacy clips (idle, jog, run, attack, throw, slam) and its HammerHold keeps the hammer in the fist.
//  클릭 — one heavy swing          Space — 회피: a quick dash with a moment of invulnerability, leaving afterimages
//  우클릭 — 회전 베기 (one full turn)  Q — 묠니르 (thrown, flies back)  E — 천둥 강타 (lightning, stun)  R — 휠윈드
// The project dealt flat damage to its own monsters (swing 10, throw 12, thunder 15, whirlwind 6 a tick, spin 12); here
// each blow is that many tenths of the game's attack (runes, crits and the forge count as for every weapon).
public class MjolnirHammer : ArmoryWeapon
{
    [Header("Attack (PlayerController)")]
    [SerializeField] private float attackRange = 1.4f;
    [SerializeField] private float attackRadius = 2.4f;
    [SerializeField] private float attackDamage = 1f;          // (10)
    [SerializeField] private string idleClip = "idle", walkClip = "jog", runClip = "run", attackClip = "attack";
    [SerializeField] private float crossFade = 0.15f, stopCrossFade = 0.06f;
    [SerializeField] private float walkClipSpeed = 1.16f, runClipSpeed = 2.31f, attackClipSpeed = 1.5f;
    [SerializeField, Range(0f, 1f)] private float attackHitPoint = 0.46f;

    [Header("Dodge (Space)")]
    [SerializeField] private float dodgeDistance = 5f;
    [SerializeField] private float dodgeTime = 0.32f;
    [SerializeField] private float dodgeCooldown = 0.45f;
    [SerializeField] private float dodgeInvulnerable = 0.28f;
    [SerializeField] private Material afterimageMaterial;
    [SerializeField] private float afterimageInterval = 0.06f;

    [Header("Mjolnir throw (Q)")]
    [SerializeField] private float throwRange = 9f;
    [SerializeField] private float throwOutTime = 0.38f;
    [SerializeField] private float throwReturnSpeed = 24f;
    [SerializeField] private float throwCooldown = 6f;
    [SerializeField] private float throwHitRadius = 1.4f;
    [SerializeField] private float throwDamage = 1.2f;         // (12)
    [SerializeField] private float throwSpinSpeed = 1440f;
    [SerializeField] private float throwHeight = 1.2f;
    [SerializeField] private string throwClip = "throw";
    [SerializeField] private float throwClipSpeed = 1.2f;
    [SerializeField, Range(0f, 1f)] private float throwReleasePoint = 0.32f;
    [SerializeField] private float throwMoveScale = 0.2f;

    [Header("Thunder strike (E)")]
    [SerializeField] private float thunderCooldown = 10f;
    [SerializeField] private float thunderRadius = 4.5f;
    [SerializeField] private float thunderDamage = 1.5f;       // (15)
    [SerializeField] private float thunderStun = 1.5f;
    [SerializeField] private float thunderReach = 1.2f;
    [SerializeField] private string thunderClip = "slam";
    [SerializeField] private float thunderClipSpeed = 1.5f;
    [SerializeField, Range(0f, 1f)] private float thunderImpactPoint = 0.57f;
    [SerializeField] private Material lightningMaterial;

    [Header("Whirlwind (R)")]
    [SerializeField] private float whirlwindDuration = 2.5f;
    [SerializeField] private float whirlwindCooldown = 8f;
    [SerializeField] private float whirlwindRadius = 3f;
    [SerializeField] private float whirlwindTick = 0.3f;
    [SerializeField] private float whirlwindDamage = 0.6f;     // (6)
    [SerializeField] private float whirlwindSpinSpeed = 900f;
    [SerializeField] private float whirlwindMoveScale = 0.55f;
    [SerializeField] private Material trailMaterial;
    [SerializeField] private float trailInterval = 0.035f;

    [Header("Spin slash (right mouse)")]
    [SerializeField] private float spinSlashTime = 0.45f;
    [SerializeField] private float spinSlashCooldown = 2.5f;
    [SerializeField] private float spinSlashRadius = 3.2f;
    [SerializeField] private float spinSlashDamage = 1.2f;     // (12)
    [SerializeField, Range(0f, 1f)] private float spinSlashHitPoint = 0.5f;
    [SerializeField] private float spinSlashMoveScale = 0.3f;

    private Animation anim;
    private HammerHold hammerHold;
    private Transform skeletonRoot;
    private Vector3 lastPosition;

    // PlayerController's state
    private float attackDuration = 0.75f, attackHitTime = 0.36f;
    private float attackTimer;
    private bool hitApplied;
    private float dodgeTimer = -1f, dodgeCooldownTimer, afterimageTimer;
    private Vector3 dodgeDir;
    private float speedScale = 1f;
    private bool skillBusy;
    private string skillClip;

    // PlayerSkills' state
    private float appliedAngle, spinAngle;
    private float whirlwindTimer = -1f, whirlwindCooldownTimer, tickTimer, trailTimer;
    private enum ThrowState { Held, WindUp, Out, Returning }
    private ThrowState throwState;
    private float throwTimer, throwClipTimer = -1f, throwCooldownTimer, throwSpin, throwTrailTimer;
    private Vector3 throwStart, throwEnd, throwDir, hammerPos;
    private readonly HashSet<Enemy> throwHits = new HashSet<Enemy>();
    private float spinSlashTimer = -1f, spinSlashCooldownTimer;
    private bool spinSlashHit;
    private float thunderTimer = -1f, thunderCooldownTimer;
    private bool thunderLanded;
    private GUIStyle keyStyle, nameStyle, timeStyle;

    public override CharacterProfile.WeaponKind Kind => CharacterProfile.WeaponKind.Hammer;
    public override bool OwnsSpace => true;
    public override bool Rooted => IsDodging;   // the dodge moves the player itself
    public override bool Invulnerable => IsDodging && dodgeTimer < dodgeInvulnerable;
    public override float MoveMultiplier => IsAttacking ? 0f : speedScale;
    // Q E R are the 번개 망치 kit (StormSkills): the bouncing hammer throw, the charged chain lightning, the lightning
    // whirlwind. This keeps the hammer's own swing, dodge and spin slash, and lends the kit its model and clips.
    public override string Hint => "[클릭] 공격  [Space] 회피  [우클릭] 질풍 회전  [Q] 망치 투척  [E 꾹] 번개 방출  [R] 휠윈드";

    // ── The 번개 망치 kit driving this model (StormSkills) ──
    private string externalClip;
    private float externalUntil;
    public bool ExternalHammerAway { get; set; }   // the kit has thrown the hammer: the fist lets go of it
    public bool ExternalSpin { get; set; }         // the kit's whirlwind: the arm swings out wide
    public bool ExternalBusy => Time.time < externalUntil || ExternalHammerAway || ExternalSpin;
    public Transform HeldHammer => hammerHold != null ? hammerHold.hammer : null;
    public float DodgeCooldownLeft => Mathf.Max(0f, dodgeCooldownTimer);
    public float SpinSlashCooldownLeft => Mathf.Max(0f, spinSlashCooldownTimer);

    // plays one of the model's clips for a while (the kit's throw, slam...), over the legs' own clips
    public void ExternalPlay(string clip, float speed, float from, float seconds)
    {
        if (!HasClip(clip)) return;
        externalClip = clip;
        externalUntil = Time.time + seconds;
        var s = anim[clip];
        s.wrapMode = WrapMode.ClampForever;
        s.speed = speed;
        s.time = from;
        anim.CrossFade(clip, 0.08f);
    }

    // holds a clip still at 'at' seconds until ExternalStop (the hammer raised while the lightning charges)
    public void ExternalHold(string clip, float at)
    {
        ExternalPlay(clip, 0f, at, 999f);
    }

    public void ExternalStop()
    {
        externalUntil = 0f;
        if (!string.IsNullOrEmpty(externalClip) && HasClip(externalClip)) anim[externalClip].speed = 1f;
        externalClip = null;
    }
    public override SkillTree Tree => HammerSkills.Tree;
    public override string TreeName => "번개 망치 스킬트리";

    // What the hammer's tree has made of each skill (HammerSkills)
    private static float CD => HammerSkills.CooldownScale;
    private float DodgeDistance => dodgeDistance * (HammerSkills.Has(HammerSkills.Blink) ? 1.3f : 1f);
    private float ThunderRadius => thunderRadius * (HammerSkills.Has(HammerSkills.Storm) ? 1.3f : 1f);
    private float ThunderDamage => thunderDamage * (HammerSkills.Has(HammerSkills.Wrath) ? 1.4f : 1f);
    private float ThunderStun => thunderStun + (HammerSkills.Has(HammerSkills.Wrath) ? 0.75f : 0f);
    private float ThrowRange => throwRange * (HammerSkills.Has(HammerSkills.Homing) ? 1.3f : 1f);
    private float ThrowDamage => throwDamage * (HammerSkills.Has(HammerSkills.Homing) ? 1.3f : 1f);
    private float WhirlDuration => whirlwindDuration * (HammerSkills.Has(HammerSkills.Tempest) ? 1.3f : 1f);
    private float WhirlRadius => whirlwindRadius + (HammerSkills.Has(HammerSkills.Tempest) ? 0.8f : 0f);
    private float SpinRadius => spinSlashRadius + (HammerSkills.Has(HammerSkills.Cyclone) ? 0.8f : 0f);
    private float SpinDamage => spinSlashDamage * (HammerSkills.Has(HammerSkills.Cyclone) ? 1.3f : 1f);

    // Read by HammerHold
    public bool IsAttacking => attackTimer > 0f;
    public float AttackProgress => 1f - attackTimer / attackDuration;
    public bool UsesAttackClip => HasClip(attackClip);
    public bool IsDead => stats != null && stats.IsDead;
    public bool IsDodging => dodgeTimer >= 0f;
    public bool IsWhirlwinding => whirlwindTimer >= 0f;
    public bool IsSpinSlashing => spinSlashTimer >= 0f;
    public bool HammerAway => throwState == ThrowState.Out || throwState == ThrowState.Returning || ExternalHammerAway;
    public bool IsThrowing => throwState == ThrowState.WindUp;
    public bool IsSlamming => thunderTimer >= 0f;
    public float GripWeight => IsThrowing ? Mathf.Clamp01(throwTimer / 0.1f)
        : IsSlamming ? Mathf.Clamp01(thunderTimer / 0.1f) * Mathf.Clamp01((ThunderClipLength - thunderTimer) / 0.15f) : 0f;
    public float SpinWeight { get; private set; }
    public Quaternion SpinRotation => Quaternion.AngleAxis(spinAngle, Vector3.up);

    private float ThrowClipLength => HasClip(throwClip) ? anim.GetClip(throwClip).length / throwClipSpeed : 0f;
    private float ThunderClipLength => HasClip(thunderClip) ? anim.GetClip(thunderClip).length / thunderClipSpeed : 0.6f;
    // (the skills' own lock-outs, as PlayerController.movementLocked: a menu or a bind has the controls)
    private bool Locked => !GameManager.IsRunActive || Time.timeScale <= 0f || PlayerEffects.Bind <= 0f;

    protected override void Equipped(bool on)
    {
        anim = on ? profile.GetComponentInChildren<Animation>(true) : null;
        hammerHold = on ? profile.GetComponentInChildren<HammerHold>(true) : null;
        skeletonRoot = on ? FindDeep(profile.transform, "Root") : null;
        if (hammerHold != null)
            hammerHold.wielder = this;
        if (anim == null)
            return;
        if (HasClip(walkClip)) anim[walkClip].speed = walkClipSpeed;
        if (HasClip(runClip)) anim[runClip].speed = runClipSpeed;
        if (UsesAttackClip)
        {
            AnimationState state = anim[attackClip];
            state.speed = attackClipSpeed;
            state.wrapMode = WrapMode.ClampForever;
            attackDuration = state.length / attackClipSpeed;
            attackHitTime = attackDuration * attackHitPoint;
        }
        if (HasClip(throwClip)) { anim[throwClip].speed = throwClipSpeed; anim[throwClip].wrapMode = WrapMode.ClampForever; }
        if (HasClip(thunderClip)) { anim[thunderClip].speed = thunderClipSpeed; anim[thunderClip].wrapMode = WrapMode.ClampForever; }
        lastPosition = transform.position;
    }

    public override void ResetCombat()
    {
        if (appliedAngle != 0f && skeletonRoot != null)
            SpinSkeleton(-appliedAngle);
        appliedAngle = spinAngle = 0f;
        attackTimer = 0f;
        dodgeTimer = -1f;
        dodgeCooldownTimer = whirlwindCooldownTimer = throwCooldownTimer = thunderCooldownTimer = spinSlashCooldownTimer = 0f;
        whirlwindTimer = spinSlashTimer = thunderTimer = throwClipTimer = -1f;
        throwState = ThrowState.Held;
        speedScale = 1f;
        skillBusy = false;
        skillClip = null;
        SpinWeight = 0f;
        externalUntil = 0f;
        externalClip = null;
        ExternalHammerAway = false;
        ExternalSpin = false;
    }

    // The click: one heavy swing (not mid-skill, not without the hammer)
    public override void Press(Vector3 aimPoint)
    {
        if (Locked || IsDodging || skillBusy || IsAttacking || ExternalBusy)
            return;
        attackTimer = attackDuration;
        hitApplied = false;
        if (HasClip(attackClip))
        {
            anim[attackClip].time = 0f;
            anim.CrossFade(attackClip, 0.1f);
        }
        else
            Play(idleClip);
    }

    private void Update()
    {
        if (!IsActive)
            return;
        // undo last frame's spin before the animation pass, so bones the clips don't drive don't keep turning
        if (appliedAngle != 0f && skeletonRoot != null)
        {
            SpinSkeleton(-appliedAngle);
            appliedAngle = 0f;
        }
        if (IsDead)
        {
            attackTimer = 0f;
            dodgeTimer = -1f;
            return;
        }

        float dt = Time.deltaTime;
        Keyboard kb = Keyboard.current;
        Mouse mouse = Mouse.current;
        bool locked = Locked;

        // ── PlayerController: dodge, the swing's hit, the clips ──
        if (dodgeCooldownTimer > 0f) dodgeCooldownTimer -= dt;
        bool canDodge = !locked && !IsDodging && dodgeCooldownTimer <= 0f && (!IsAttacking || hitApplied);
        if (canDodge && kb != null && HammerSkills.Has(HammerSkills.Dodge) && SkillSystem.DodgeDown(kb))
        {
            Vector2 input = MoveInput(kb);
            Vector3 dir = input.sqrMagnitude > 0.01f ? new Vector3(input.x, 0f, input.y).normalized : transform.forward;
            dodgeDir = dir;
            dodgeTimer = 0f;
            afterimageTimer = 0f;
            attackTimer = 0f;   // cancels the swing recovery
            transform.rotation = Quaternion.LookRotation(dir);
        }
        if (IsDodging)
            UpdateDodge(dt);

        // ── PlayerSkills ──
        if (whirlwindCooldownTimer > 0f) whirlwindCooldownTimer -= dt;
        if (throwCooldownTimer > 0f) throwCooldownTimer -= dt;
        if (thunderCooldownTimer > 0f) thunderCooldownTimer -= dt;
        if (spinSlashCooldownTimer > 0f) spinSlashCooldownTimer -= dt;
        bool canAct = !locked && !IsDodging && !IsAttacking && !IsWhirlwinding && !IsSpinSlashing && !IsThrowing && !IsSlamming && !HammerAway && !ExternalBusy;
        if (canAct)
        {
            // (Q E R are the 번개 망치 kit's now — StormSkills; the right-click spin slash stays the hammer's own)
            if (SkillSystem.RightPressed(mouse) && spinSlashCooldownTimer <= 0f) StartSpinSlash();
        }
        var gm = GameManager.Instance;
        if (gm != null && gm.slotCooldowns != null)
        {
            if (gm.slotNames != null)
            {
                gm.slotNames[3] = "질풍 회전"; gm.slotIcons[3] = SkillBar.Icon("UI/gs_whirlwind");
                gm.slotNames[4] = "회피"; gm.slotIcons[4] = SkillBar.Icon("UI/dual_dodge");
            }
            gm.slotCooldowns[3] = SpinSlashCooldownLeft;
            gm.slotCooldowns[4] = DodgeCooldownLeft;
            gm.slotActive[3] = IsSpinSlashing;
        }
        if (IsWhirlwinding) UpdateWhirlwind(dt);
        if (IsSpinSlashing) UpdateSpinSlash(dt);
        UpdateThrowClip(dt);
        if (IsSlamming) UpdateThunder(dt);
        bool armOut = IsWhirlwinding || IsSpinSlashing || ExternalSpin;
        SpinWeight = Mathf.MoveTowards(SpinWeight, armOut ? 1f : 0f, dt / (IsSpinSlashing ? 0.08f : 0.15f));
        skillBusy = armOut || IsThrowing || IsSlamming || HammerAway;

        // The clips: dodge = run, the swing, a skill's clip, or the legs from how fast the player is actually moving
        Vector3 moved = transform.position - lastPosition;
        moved.y = 0f;
        lastPosition = transform.position;
        float speed = dt > 0f ? moved.magnitude / dt : 0f;
        bool running = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        if (IsDodging) Play(runClip);
        else if (IsAttacking) UpdateAttack(dt);
        else if (Time.time < externalUntil) { }   // the kit's clip has the body
        else if (!string.IsNullOrEmpty(skillClip)) Play(skillClip);
        else if (speed > 0.3f) Play(running ? runClip : walkClip);
        else Play(idleClip, stopCrossFade);
    }

    private static Vector2 MoveInput(Keyboard kb)
    {
        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed) input.y -= 1f;
        if (kb.dKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed) input.x -= 1f;
        return input;
    }

    // Fast out of the start, easing off at the end
    private void UpdateDodge(float dt)
    {
        float t0 = dodgeTimer / dodgeTime;
        dodgeTimer += dt;
        float t1 = Mathf.Min(dodgeTimer / dodgeTime, 1f);
        float step = DodgeDistance * (EaseOut(t1) - EaseOut(t0));
        controller.Move(dodgeDir * step + Vector3.down * 2f * dt);

        afterimageTimer -= dt;
        if (afterimageTimer <= 0f && afterimageMaterial != null && profile != null)
        {
            afterimageTimer = afterimageInterval;
            Afterimage.Spawn(profile.gameObject, afterimageMaterial, hammerHold != null ? hammerHold.hammer : null);
        }
        if (t1 >= 1f)
        {
            dodgeTimer = -1f;
            dodgeCooldownTimer = dodgeCooldown;
        }
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    private void UpdateAttack(float dt)
    {
        attackTimer -= dt;
        if (!hitApplied && attackDuration - attackTimer >= attackHitTime)
        {
            hitApplied = true;
            List<Enemy> struck = HitSphere(transform.position + transform.forward * attackRange + Vector3.up, attackRadius, attackDamage);
            miner.ChainFrom(struck);
        }
    }

    // ── E: Thunder strike ──

    private void StartThunder()
    {
        thunderCooldownTimer = thunderCooldown * CD;
        thunderTimer = 0f;
        thunderLanded = false;
        speedScale = 0f;
        skillClip = thunderClip;
        if (HasClip(thunderClip))
        {
            anim[thunderClip].time = 0f;
            anim.CrossFade(thunderClip, 0.08f);
        }
    }

    private void UpdateThunder(float dt)
    {
        thunderTimer += dt;
        if (Locked || IsDodging || IsDead)
        {
            EndThunder();
            return;
        }
        if (!thunderLanded && thunderTimer >= ThunderClipLength * thunderImpactPoint)
        {
            thunderLanded = true;
            ThunderImpact();
        }
        if (thunderTimer >= ThunderClipLength) EndThunder();
    }

    private void EndThunder()
    {
        thunderTimer = -1f;
        speedScale = 1f;
        skillClip = null;
    }

    private void ThunderImpact()
    {
        Vector3 center = transform.position + transform.forward * thunderReach;
        var strikes = new List<Vector3>();
        foreach (Enemy enemy in HitSphere(center + Vector3.up, ThunderRadius, ThunderDamage))
        {
            if (!enemy.IsDead)
                enemy.Knockdown(ThunderStun);
            // a bolt for each monster caught
            if (strikes.Count < 6)
                strikes.Add(new Vector3(enemy.transform.position.x, center.y, enemy.transform.position.z));
        }
        if (lightningMaterial != null) ThunderStrikeFX.Spawn(center, ThunderRadius, strikes, lightningMaterial);
        CameraShake.Shake(0.35f, 0.3f);
    }

    // ── Q: Mjolnir throw ──

    private void StartThrow()
    {
        throwCooldownTimer = throwCooldown * CD;
        float clipLength = ThrowClipLength;
        if (clipLength <= 0f)
        {
            Release();
            return;
        }
        throwState = ThrowState.WindUp;
        throwTimer = 0f;
        throwClipTimer = 0f;
        speedScale = throwMoveScale;
        skillClip = throwClip;
        anim[throwClip].time = 0f;
        anim.CrossFade(throwClip, 0.08f);
    }

    private void UpdateThrowClip(float dt)
    {
        if (throwClipTimer < 0f) return;
        throwClipTimer += dt;
        if (IsThrowing)
        {
            throwTimer += dt;
            if (IsDodging || Locked || IsDead)
            {
                throwState = ThrowState.Held;
                EndThrowClip();
                return;
            }
            if (throwClipTimer >= ThrowClipLength * throwReleasePoint) Release();
        }
        if (throwClipTimer >= ThrowClipLength || IsDodging) EndThrowClip();
        else if (HammerAway && throwClipTimer >= ThrowClipLength * throwReleasePoint + 0.1f) speedScale = 1f;
    }

    private void EndThrowClip()
    {
        throwClipTimer = -1f;
        speedScale = 1f;
        skillClip = null;
    }

    private void Release()
    {
        throwDir = transform.forward;
        throwStart = hammerHold.hammer.position;
        Vector3 from = transform.position + Vector3.up * throwHeight;
        float range = ThrowRange;
        // stop short of walls; monsters are hit, not stopped at
        foreach (RaycastHit hit in Physics.RaycastAll(from, throwDir, ThrowRange, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<Enemy>() != null) continue;
            range = Mathf.Min(range, hit.distance - 0.6f);
        }
        throwEnd = from + throwDir * Mathf.Max(range, 1.5f);
        hammerPos = throwStart;
        throwTimer = 0f;
        throwSpin = 0f;
        throwHits.Clear();
        throwState = ThrowState.Out;
    }

    // In LateUpdate, after the animation, so the return can home on the hand where it actually is this frame
    private void UpdateThrow(float dt)
    {
        if (throwState == ThrowState.Out)
        {
            throwTimer += dt;
            float t = Mathf.Clamp01(throwTimer / throwOutTime);
            hammerPos = Vector3.Lerp(throwStart, throwEnd, 1f - (1f - t) * (1f - t));
            if (t >= 1f)
            {
                throwState = ThrowState.Returning;
                throwHits.Clear();
            }
        }
        else
        {
            Vector3 hand = hammerHold.hand.position;
            hammerPos = Vector3.MoveTowards(hammerPos, hand, throwReturnSpeed * dt);
            if (Vector3.Distance(hammerPos, hand) < 0.4f)
            {
                throwState = ThrowState.Held;   // caught: HammerHold takes over again this same frame
                return;
            }
        }

        foreach (Enemy enemy in EnemiesIn(hammerPos, throwHitRadius))
            if (throwHits.Add(enemy))
                StrikeTimes(enemy, ThrowDamage);

        // tumble end over end in the plane of flight
        throwSpin += throwSpinSpeed * dt;
        Vector3 travel = throwState == ThrowState.Out ? throwDir : (hammerHold.hand.position - hammerPos).normalized;
        if (travel.sqrMagnitude < 0.01f) travel = throwDir;
        Vector3 axis = Vector3.Cross(Vector3.up, travel).normalized;
        Quaternion tumble = Quaternion.AngleAxis(throwSpin, axis);
        hammerHold.PlaceFlying(hammerPos, tumble * Vector3.up, tumble * travel);

        throwTrailTimer -= dt;
        if (throwTrailTimer <= 0f && trailMaterial != null)
        {
            throwTrailTimer = trailInterval;
            Afterimage.Spawn(null, trailMaterial, hammerHold.hammer);
        }
    }

    // ── R: Whirlwind ──

    private void StartWhirlwind()
    {
        whirlwindTimer = 0f;
        tickTimer = 0.1f;
        speedScale = whirlwindMoveScale;
        skillClip = idleClip;
    }

    private void UpdateWhirlwind(float dt)
    {
        whirlwindTimer += dt;
        if (IsDodging || Locked || IsDead || whirlwindTimer >= WhirlDuration)
        {
            StopWhirlwind();
            return;
        }
        tickTimer -= dt;
        if (tickTimer <= 0f)
        {
            tickTimer += whirlwindTick;
            HitSphere(transform.position + Vector3.up, WhirlRadius, whirlwindDamage);
        }
        trailTimer -= dt;
        if (trailTimer <= 0f && trailMaterial != null && hammerHold != null && SpinWeight > 0.6f)
        {
            trailTimer = trailInterval;
            Afterimage.Spawn(null, trailMaterial, hammerHold.hammer);
        }
    }

    private void StopWhirlwind()
    {
        whirlwindTimer = -1f;
        whirlwindCooldownTimer = whirlwindCooldown * CD;
        speedScale = 1f;
        skillClip = null;
    }

    // ── Right mouse: Spin slash ──

    // 질풍 회전: a short dash the way he faces, the hammer swung once round at arm's length, lightning dragging the foes
    // nearby in to him — then one blow on the gathered pack (SpinPull)
    private Vector3 spinDashDir;
    private readonly List<Enemy> spinDragged = new List<Enemy>();

    private void StartSpinSlash()
    {
        spinSlashTimer = 0f;
        spinSlashHit = false;
        speedScale = 0f;
        skillClip = runClip;
        spinDashDir = transform.forward; spinDashDir.y = 0f; spinDashDir.Normalize();
        spinDragged.Clear();
        SpinPull.Begin(transform.position + Vector3.down * 0.9f, spinDashDir);
    }

    private void UpdateSpinSlash(float dt)
    {
        spinSlashTimer += dt;
        if (IsDodging || Locked || IsDead)
        {
            StopSpinSlash();
            return;
        }
        float k = Mathf.Clamp01(spinSlashTimer / spinSlashTime);
        if (!spinSlashHit && controller != null && controller.enabled)
            controller.Move(spinDashDir * (SpinPull.Distance / spinSlashTime) * (1.4f - 0.8f * k) * dt + Vector3.down * 0.5f * dt);
        if (!spinSlashHit)
        {
            SpinPull.Drag(transform.position, SpinPull.Radius + (HammerSkills.Has(HammerSkills.Cyclone) ? 1f : 0f), (4f + 14f * k) * dt, spinDragged);
            if (Random.value < 0.6f) SpinPull.Crackle(transform.position, spinDragged, k);
        }
        if (!spinSlashHit && spinSlashTimer >= spinSlashTime * 0.85f)
        {
            spinSlashHit = true;
            HitSphere(transform.position + Vector3.up, Mathf.Max(SpinRadius, SpinPull.HitRadius), Mathf.Max(SpinDamage, SpinPull.Damage));
            SpinPull.Finish(transform.position + Vector3.down * 0.9f, spinDashDir);
        }
        trailTimer -= dt;
        if (trailTimer <= 0f && trailMaterial != null && hammerHold != null && SpinWeight > 0.5f)
        {
            trailTimer = trailInterval;
            Afterimage.Spawn(null, trailMaterial, hammerHold.hammer);
        }
        if (spinSlashTimer >= spinSlashTime) StopSpinSlash();
    }

    private void StopSpinSlash()
    {
        spinSlashTimer = -1f;
        spinSlashCooldownTimer = SpinPull.Cooldown * CD;
        speedScale = 1f;
        skillClip = null;
    }

    private void LateUpdate()
    {
        if (!IsActive)
            return;
        float dt = Time.deltaTime;
        if (HammerAway && hammerHold != null) UpdateThrow(dt);
        if (skeletonRoot == null) return;
        if (IsWhirlwinding)
        {
            float speed = whirlwindSpinSpeed * Mathf.Clamp01(whirlwindTimer / 0.2f + 0.3f);
            spinAngle = Mathf.Repeat(spinAngle - speed * dt, 360f);
        }
        else if (IsSpinSlashing)
        {
            float k = Mathf.Clamp01(spinSlashTimer / spinSlashTime);
            k = 1f - (1f - k) * (1f - k);
            spinAngle = Mathf.Repeat(-360f * k, 360f);
        }
        else if (spinAngle != 0f)
        {
            spinAngle -= whirlwindSpinSpeed * 0.7f * dt;
            if (spinAngle <= 0f) spinAngle = 0f;
        }
        if (spinAngle != 0f)
        {
            appliedAngle = spinAngle;
            SpinSkeleton(appliedAngle);
        }
    }

    private void SpinSkeleton(float angle)
    {
        Quaternion q = Quaternion.AngleAxis(angle, Vector3.up);
        skeletonRoot.localPosition = q * skeletonRoot.localPosition;
        skeletonRoot.localRotation = q * skeletonRoot.localRotation;
    }

    // ── Hitting the game's enemies ──

    // Every enemy whose body is within radius of a point (once each, however many colliders it has)
    private List<Enemy> EnemiesIn(Vector3 center, float radius)
    {
        var list = new List<Enemy>();
        foreach (Collider c in Physics.OverlapSphere(center, radius))
        {
            if (c.transform.IsChildOf(transform)) continue;
            Enemy e = c.GetComponentInParent<Enemy>();
            if (e != null && !e.IsDead && e.isActiveAndEnabled && !list.Contains(e)) list.Add(e);
        }
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead || !e.isActiveAndEnabled || list.Contains(e)) continue;
            Vector3 to = e.transform.position - center;
            to.y = 0f;
            if (to.magnitude <= radius) list.Add(e);
        }
        return list;
    }

    private List<Enemy> HitSphere(Vector3 center, float radius, float multiplier)
    {
        List<Enemy> struck = EnemiesIn(center, radius);
        foreach (Enemy e in struck)
            StrikeTimes(e, multiplier);
        return struck;
    }

    private void StrikeTimes(Enemy enemy, float multiplier)
    {
        float damage = RollDamage(out bool critical);
        Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(damage * multiplier)), critical);
    }

    private void Play(string clip) => Play(clip, crossFade);

    private void Play(string clip, float fade)
    {
        if (HasClip(clip) && !anim.IsPlaying(clip)) anim.CrossFade(clip, fade);
    }

    private bool HasClip(string clip) => anim != null && !string.IsNullOrEmpty(clip) && anim.GetClip(clip) != null;

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name)
                return t;
        return null;
    }

    // The project's skill bar along the bottom of the screen (Space shows the dodge's cooldown)
    // (the skills now sit in the game's round slots: Q E R from StormSkills, right click and Space from here)
    public override void DrawHud() { }

    private void DrawOldSkillBar()
    {
        if (keyStyle == null)
        {
            keyStyle = UiFont.Style(13, TextAnchor.UpperLeft, FontStyle.Bold);
            nameStyle = UiFont.Style(12, TextAnchor.LowerCenter);
            nameStyle.wordWrap = true;
            timeStyle = UiFont.Style(22, TextAnchor.MiddleCenter, FontStyle.Bold);
        }
        const float size = 64f, gap = 10f;
        float x = (Screen.width - (size * 5 + gap * 4)) * 0.5f, y = Screen.height - size - 36f;
        Slot(new Rect(x, y, size, size), "Space", "회피", dodgeCooldownTimer, dodgeCooldown, false, HammerSkills.Dodge);
        Slot(new Rect(x + (size + gap), y, size, size), "우클릭", "회전 베기", spinSlashCooldownTimer, spinSlashCooldown * CD, IsSpinSlashing, HammerSkills.Spin);
        Slot(new Rect(x + (size + gap) * 2, y, size, size), "Q", "묠니르", throwCooldownTimer, throwCooldown * CD, HammerAway, HammerSkills.Throw);
        Slot(new Rect(x + (size + gap) * 3, y, size, size), "E", "천둥 강타", thunderCooldownTimer, thunderCooldown * CD, IsSlamming, HammerSkills.Thunder);
        Slot(new Rect(x + (size + gap) * 4, y, size, size), "R", "휠윈드", whirlwindCooldownTimer, whirlwindCooldown * CD, IsWhirlwinding, HammerSkills.Whirl);
    }

    private void Slot(Rect r, string key, string label, float cooldownLeft, float cooldown, bool active, string skill)
    {
        if (!HammerSkills.Has(skill))
        {
            // not learned yet: a dark slot pointing at the tree
            UiFont.Fill(r, new Color(0f, 0f, 0f, 0.65f));
            UiFont.Fill(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), new Color(0.12f, 0.12f, 0.14f, 0.85f));
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            GUI.Label(new Rect(r.x + 5, r.y + 3, r.width, 18), key, keyStyle);
            GUI.Label(new Rect(r.x, r.y, r.width, r.height - 4), "잠김", nameStyle);
            GUI.color = Color.white;
            return;
        }
        UiFont.Fill(r, new Color(0f, 0f, 0f, 0.65f));
        UiFont.Fill(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), active ? new Color(0.45f, 0.85f, 1f, 0.9f) : new Color(0.2f, 0.45f, 0.65f, 0.85f));
        if (cooldownLeft > 0f)
            UiFont.Fill(new Rect(r.x + 3, r.y + 3, r.width - 6, (r.height - 6) * Mathf.Clamp01(cooldownLeft / cooldown)), new Color(0f, 0f, 0f, 0.7f));
        GUI.Label(new Rect(r.x + 5, r.y + 3, r.width, 18), key, keyStyle);
        GUI.Label(new Rect(r.x, r.y, r.width, r.height - 4), label, nameStyle);
        if (cooldownLeft > 0f)
            GUI.Label(r, cooldownLeft.ToString(cooldownLeft < 1f ? "0.0" : "0"), timeStyle);
    }
}
