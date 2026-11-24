using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Skills on Q / E / R, and the skill bar at the bottom of the screen (Space shows the dodge cooldown).
// Q - Mjolnir throw: an overhand throw clip winds up with the hammer in the fist and lets go at the release frame;
// the hammer flies out ahead, spinning end over end, hitting everything on its way, then homes back to the hand and
// hits again on the way back. Until it is caught the hero fights empty-handed (no attacks).
// E - Thunder strike: a leaping two-handed slam; where the hammer lands, lightning drops on everything around,
// hitting it, knocking it back and stunning it for a moment.
// R - Whirlwind: the hero spins with the hammer held straight out, hitting everything around him a few times a
// second. He can still walk slowly while spinning, and a dodge cuts the spin short.
// Right mouse - Spin slash: one quick full turn with the hammer held out, a single heavy hit on everything around.
[DefaultExecutionOrder(-10)]   // spin the body before HammerHold places the hammer in LateUpdate
public class PlayerSkills : MonoBehaviour
{
    [Header("Mjolnir throw (Q)")]
    public float throwRange = 9f;
    [Tooltip("Seconds to reach full range.")]
    public float throwOutTime = 0.38f;
    public float throwReturnSpeed = 24f;
    public float throwCooldown = 6f;
    public float throwHitRadius = 1.4f;
    public int throwDamage = 12;
    [Tooltip("Degrees per second the hammer tumbles in flight.")]
    public float throwSpinSpeed = 1440f;
    public float throwHeight = 1.2f;
    public string throwClip = "throw";
    public float throwClipSpeed = 1.2f;
    [Tooltip("Fraction of the throw clip where the hand lets go of the hammer.")]
    [Range(0f, 1f)] public float throwReleasePoint = 0.32f;
    [Tooltip("Walking speed during the wind-up, as a fraction of normal (low, but enough to turn and aim).")]
    public float throwMoveScale = 0.2f;

    [Header("Thunder strike (E)")]
    public float thunderCooldown = 10f;
    public float thunderRadius = 4.5f;
    public int thunderDamage = 15;
    public float thunderStun = 1.5f;
    [Tooltip("How far in front of the hero the hammer comes down.")]
    public float thunderReach = 1.2f;
    public string thunderClip = "slam";
    public float thunderClipSpeed = 1.5f;
    [Tooltip("Fraction of the slam clip where the hammer hits the ground.")]
    [Range(0f, 1f)] public float thunderImpactPoint = 0.57f;
    [Tooltip("Additive material for the lightning and the shock ring.")]
    public Material lightningMaterial;

    [Header("Whirlwind (R)")]
    public float whirlwindDuration = 2.5f;
    public float whirlwindCooldown = 8f;
    public float whirlwindRadius = 3f;
    [Tooltip("Seconds between hits on everything in range.")]
    public float whirlwindTick = 0.3f;
    public int whirlwindDamage = 6;
    [Tooltip("Degrees per second.")]
    public float whirlwindSpinSpeed = 900f;
    [Tooltip("Walking speed while spinning, as a fraction of normal.")]
    public float whirlwindMoveScale = 0.55f;
    [Tooltip("Additive material for the hammer trail left around the spin.")]
    public Material trailMaterial;
    public float trailInterval = 0.035f;

    [Header("Spin slash (right mouse)")]
    [Tooltip("Seconds for the single full turn.")]
    public float spinSlashTime = 0.45f;
    public float spinSlashCooldown = 2.5f;
    public float spinSlashRadius = 3.2f;
    public int spinSlashDamage = 12;
    [Tooltip("Fraction of the turn where the hit lands.")]
    [Range(0f, 1f)] public float spinSlashHitPoint = 0.5f;
    public float spinSlashMoveScale = 0.3f;

    [Tooltip("Top of the skeleton; only this spins, so the player object keeps the facing the controller gives it.")]
    public Transform skeletonRoot;

    public bool IsWhirlwinding => whirlwindTimer >= 0f;
    public bool IsSpinSlashing => spinSlashTimer >= 0f;
    public bool HammerAway => throwState == ThrowState.Out || throwState == ThrowState.Returning;
    public bool IsThrowing => throwState == ThrowState.WindUp;
    public bool IsSlamming => thunderTimer >= 0f;
    // 0..1: how firmly the hammer sits in the fist while a skill clip swings it (read by HammerHold)
    public float GripWeight => IsThrowing ? Mathf.Clamp01(throwTimer / 0.1f)
        : IsSlamming ? Mathf.Clamp01(thunderTimer / 0.1f) * Mathf.Clamp01((ThunderClipLength - thunderTimer) / 0.15f) : 0f;
    // 0..1, eased: how far the arm is out in the spin pose (read by HammerHold)
    public float SpinWeight { get; private set; }
    // the spin currently applied to the skeleton, on top of the player's own rotation
    public Quaternion SpinRotation => Quaternion.AngleAxis(spinAngle, Vector3.up);

    PlayerController controller;
    PlayerHealth health;
    HammerHold hammerHold;
    float appliedAngle;      // spin added to the skeleton last LateUpdate, taken off again before the next animation pass
    float whirlwindTimer = -1f;
    float whirlwindCooldownTimer;
    float tickTimer;
    float trailTimer;
    float spinAngle;
    readonly HashSet<GameObject> hitThisTick = new HashSet<GameObject>();

    enum ThrowState { Held, WindUp, Out, Returning }
    ThrowState throwState;
    float throwTimer;
    float throwClipTimer = -1f;   // counts up while the throw clip plays, -1 otherwise
    float throwCooldownTimer;
    float throwSpin;
    float throwTrailTimer;
    Vector3 throwStart, throwEnd, throwDir, hammerPos;
    readonly HashSet<GameObject> throwHits = new HashSet<GameObject>();   // per leg: each target is hit once going out, once coming back

    float spinSlashTimer = -1f;   // counts up through the single turn, -1 otherwise
    float spinSlashCooldownTimer;
    bool spinSlashHit;

    float thunderTimer = -1f;   // counts up through the slam clip, -1 otherwise
    float thunderCooldownTimer;
    bool thunderLanded;
    GUIStyle keyStyle, nameStyle, timeStyle;

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        health = GetComponent<PlayerHealth>();
        hammerHold = GetComponentInChildren<HammerHold>();
        if (skeletonRoot == null) skeletonRoot = transform.Find("Root");
        var anim = controller.anim;
        if (anim != null && anim.GetClip(throwClip) != null)
        {
            anim[throwClip].speed = throwClipSpeed;
            anim[throwClip].wrapMode = WrapMode.ClampForever;
        }
        if (anim != null && anim.GetClip(thunderClip) != null)
        {
            anim[thunderClip].speed = thunderClipSpeed;
            anim[thunderClip].wrapMode = WrapMode.ClampForever;
        }
    }

    float ThrowClipLength => controller.anim != null && controller.anim.GetClip(throwClip) != null
        ? controller.anim.GetClip(throwClip).length / throwClipSpeed : 0f;
    float ThunderClipLength => controller.anim != null && controller.anim.GetClip(thunderClip) != null
        ? controller.anim.GetClip(thunderClip).length / thunderClipSpeed : 0.6f;

    void Update()
    {
        // undo last frame's spin before the animation pass, so bones the clips don't drive don't keep turning
        if (appliedAngle != 0f && skeletonRoot != null)
        {
            SpinSkeleton(-appliedAngle);
            appliedAngle = 0f;
        }

        float dt = Time.deltaTime;
        if (whirlwindCooldownTimer > 0f) whirlwindCooldownTimer -= dt;
        if (throwCooldownTimer > 0f) throwCooldownTimer -= dt;
        if (thunderCooldownTimer > 0f) thunderCooldownTimer -= dt;
        if (spinSlashCooldownTimer > 0f) spinSlashCooldownTimer -= dt;

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        bool canAct = !controller.movementLocked && !controller.IsDodging && !controller.IsAttacking && (health == null || !health.IsDead)
            && !IsWhirlwinding && !IsSpinSlashing && !IsThrowing && !IsSlamming && !HammerAway;
        if (canAct)
        {
            if (kb != null && kb.rKey.wasPressedThisFrame && whirlwindCooldownTimer <= 0f) StartWhirlwind();
            else if (kb != null && kb.qKey.wasPressedThisFrame && throwCooldownTimer <= 0f && hammerHold != null) StartThrow();
            else if (kb != null && kb.eKey.wasPressedThisFrame && thunderCooldownTimer <= 0f) StartThunder();
            else if (SkillSystem.RightPressed(mouse) && spinSlashCooldownTimer <= 0f) StartSpinSlash();
        }

        if (IsWhirlwinding) UpdateWhirlwind(dt);
        if (IsSpinSlashing) UpdateSpinSlash(dt);
        UpdateThrowClip(dt);
        if (IsSlamming) UpdateThunder(dt);
        bool armOut = IsWhirlwinding || IsSpinSlashing;
        SpinWeight = Mathf.MoveTowards(SpinWeight, armOut ? 1f : 0f, dt / (IsSpinSlashing ? 0.08f : 0.15f));
        controller.skillBusy = armOut || IsThrowing || IsSlamming || HammerAway;   // no basic attack mid-skill or without the hammer
    }

    // ---------- E: Thunder strike ----------

    void StartThunder()
    {
        thunderCooldownTimer = thunderCooldown;
        thunderTimer = 0f;
        thunderLanded = false;
        controller.speedScale = 0f;   // rooted for the leap; the controller still turns toward the input
        controller.skillClip = thunderClip;
        if (controller.anim != null && controller.anim.GetClip(thunderClip) != null)
        {
            controller.anim[thunderClip].time = 0f;
            controller.anim.CrossFade(thunderClip, 0.08f);
        }
    }

    void UpdateThunder(float dt)
    {
        thunderTimer += dt;
        if (controller.movementLocked || controller.IsDodging || (health != null && health.IsDead))
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

    void EndThunder()
    {
        thunderTimer = -1f;
        controller.speedScale = 1f;
        controller.skillClip = null;
    }

    void ThunderImpact()
    {
        Vector3 center = transform.position + transform.forward * thunderReach;
        var strikes = new List<Vector3>();
        hitThisTick.Clear();
        foreach (var hit in Physics.OverlapSphere(center + Vector3.up, thunderRadius))
        {
            if (hit.transform.IsChildOf(transform)) continue;
            var target = hit.attachedRigidbody != null ? hit.attachedRigidbody.gameObject : hit.gameObject;
            if (!hitThisTick.Add(target)) continue;
            hit.SendMessageUpwards("TakeHit", thunderDamage, SendMessageOptions.DontRequireReceiver);
            hit.SendMessageUpwards("Stun", thunderStun, SendMessageOptions.DontRequireReceiver);
            // a bolt for each monster caught (not for every collider of the boss)
            if (strikes.Count < 6 && hit.GetComponentInParent<EnemyMonster>() != null)
                strikes.Add(new Vector3(hit.transform.position.x, center.y, hit.transform.position.z));
        }
        if (lightningMaterial != null) ThunderStrikeFX.Spawn(center, thunderRadius, strikes, lightningMaterial);
        var cam = Camera.main != null ? Camera.main.GetComponent<QuarterViewCamera>() : null;
        if (cam != null) cam.Shake(0.35f, 0.3f);
    }

    // ---------- Q: Mjolnir throw ----------

    void StartThrow()
    {
        throwCooldownTimer = throwCooldown;
        float clipLength = ThrowClipLength;
        if (clipLength <= 0f)
        {
            Release();   // no clip: let go straight away
            return;
        }
        throwState = ThrowState.WindUp;
        throwTimer = 0f;
        throwClipTimer = 0f;
        controller.speedScale = throwMoveScale;
        controller.skillClip = throwClip;
        controller.anim[throwClip].time = 0f;
        controller.anim.CrossFade(throwClip, 0.08f);
    }

    // Wind-up until the release frame, then let the clip's follow-through play out before handing the legs back.
    void UpdateThrowClip(float dt)
    {
        if (throwClipTimer < 0f) return;
        throwClipTimer += dt;
        if (IsThrowing)
        {
            throwTimer += dt;
            if (controller.IsDodging || controller.movementLocked || (health != null && health.IsDead))
            {
                // interrupted before letting go: the hammer stays in hand, the cooldown keeps running
                throwState = ThrowState.Held;
                EndThrowClip();
                return;
            }
            if (throwClipTimer >= ThrowClipLength * throwReleasePoint) Release();
        }
        if (throwClipTimer >= ThrowClipLength || controller.IsDodging) EndThrowClip();
        else if (HammerAway && throwClipTimer >= ThrowClipLength * throwReleasePoint + 0.1f) controller.speedScale = 1f;
    }

    void EndThrowClip()
    {
        throwClipTimer = -1f;
        controller.speedScale = 1f;
        controller.skillClip = null;
    }

    void Release()
    {
        throwDir = transform.forward;
        throwStart = hammerHold.hammer.position;
        Vector3 from = transform.position + Vector3.up * throwHeight;
        float range = throwRange;
        // stop short of walls; monsters are hit, not stopped at
        foreach (var hit in Physics.RaycastAll(from, throwDir, throwRange, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<EnemyMonster>() != null) continue;
            range = Mathf.Min(range, hit.distance - 0.6f);
        }
        throwEnd = from + throwDir * Mathf.Max(range, 1.5f);
        hammerPos = throwStart;
        throwTimer = 0f;
        throwSpin = 0f;
        throwHits.Clear();
        throwState = ThrowState.Out;
    }

    // Runs in LateUpdate, after the animation, so the return can home on the hand where it actually is this frame.
    void UpdateThrow(float dt)
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

        foreach (var hit in Physics.OverlapSphere(hammerPos, throwHitRadius))
        {
            if (hit.transform.IsChildOf(transform)) continue;
            var target = hit.attachedRigidbody != null ? hit.attachedRigidbody.gameObject : hit.gameObject;
            if (!throwHits.Add(target)) continue;
            hit.SendMessageUpwards("TakeHit", throwDamage, SendMessageOptions.DontRequireReceiver);
        }

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

    // ---------- R: Whirlwind ----------

    void StartWhirlwind()
    {
        whirlwindTimer = 0f;
        tickTimer = 0.1f;   // first hit comes right after the arm is out
        controller.speedScale = whirlwindMoveScale;
        controller.skillClip = controller.idleClip;   // the legs stand still; the whole body turns
    }

    void UpdateWhirlwind(float dt)
    {
        whirlwindTimer += dt;
        if (controller.IsDodging || controller.movementLocked || (health != null && health.IsDead) || whirlwindTimer >= whirlwindDuration)
        {
            StopWhirlwind();
            return;
        }

        tickTimer -= dt;
        if (tickTimer <= 0f)
        {
            tickTimer += whirlwindTick;
            HitAround();
        }

        trailTimer -= dt;
        if (trailTimer <= 0f && trailMaterial != null && hammerHold != null && SpinWeight > 0.6f)
        {
            trailTimer = trailInterval;
            Afterimage.Spawn(null, trailMaterial, hammerHold.hammer);
        }
    }

    void StopWhirlwind()
    {
        whirlwindTimer = -1f;
        whirlwindCooldownTimer = whirlwindCooldown;
        controller.speedScale = 1f;
        controller.skillClip = null;
    }

    void HitAround() => HitAround(whirlwindRadius, whirlwindDamage);

    void HitAround(float radius, int damage)
    {
        hitThisTick.Clear();
        Vector3 center = transform.position + Vector3.up;
        foreach (var hit in Physics.OverlapSphere(center, radius))
        {
            if (hit.transform.IsChildOf(transform)) continue;
            var target = hit.attachedRigidbody != null ? hit.attachedRigidbody.gameObject : hit.gameObject;
            if (!hitThisTick.Add(target)) continue;   // one hit per target per tick, however many colliders it has
            hit.SendMessageUpwards("TakeHit", damage, SendMessageOptions.DontRequireReceiver);
        }
    }

    // ---------- Right mouse: Spin slash ----------

    void StartSpinSlash()
    {
        spinSlashTimer = 0f;
        spinSlashHit = false;
        controller.speedScale = spinSlashMoveScale;
        controller.skillClip = controller.idleClip;   // like the whirlwind: legs still, the whole body turns
    }

    void UpdateSpinSlash(float dt)
    {
        spinSlashTimer += dt;
        if (controller.IsDodging || controller.movementLocked || (health != null && health.IsDead))
        {
            StopSpinSlash();
            return;
        }
        if (!spinSlashHit && spinSlashTimer >= spinSlashTime * spinSlashHitPoint)
        {
            spinSlashHit = true;
            HitAround(spinSlashRadius, spinSlashDamage);
        }
        trailTimer -= dt;
        if (trailTimer <= 0f && trailMaterial != null && hammerHold != null && SpinWeight > 0.5f)
        {
            trailTimer = trailInterval;
            Afterimage.Spawn(null, trailMaterial, hammerHold.hammer);
        }
        if (spinSlashTimer >= spinSlashTime) StopSpinSlash();
    }

    void StopSpinSlash()
    {
        spinSlashTimer = -1f;
        spinSlashCooldownTimer = spinSlashCooldown;
        controller.speedScale = 1f;
        controller.skillClip = null;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (HammerAway) UpdateThrow(dt);
        if (skeletonRoot == null) return;
        if (IsWhirlwinding)
        {
            // spin up over the first moment instead of snapping to full speed
            float speed = whirlwindSpinSpeed * Mathf.Clamp01(whirlwindTimer / 0.2f + 0.3f);
            spinAngle = Mathf.Repeat(spinAngle - speed * dt, 360f);
        }
        else if (IsSpinSlashing)
        {
            // exactly one turn, fast through the middle and easing into the end so he stops facing forward
            float k = Mathf.Clamp01(spinSlashTimer / spinSlashTime);
            k = 1f - (1f - k) * (1f - k);
            spinAngle = Mathf.Repeat(-360f * k, 360f);
        }
        else if (spinAngle != 0f)
        {
            // finish the current turn so the body comes to rest facing forward again
            spinAngle -= whirlwindSpinSpeed * 0.7f * dt;
            if (spinAngle <= 0f) spinAngle = 0f;
        }
        if (spinAngle != 0f)
        {
            appliedAngle = spinAngle;
            SpinSkeleton(appliedAngle);
        }
    }

    // turns the skeleton about the player's own vertical axis (in the player's local space, so moving doesn't matter)
    void SpinSkeleton(float angle)
    {
        var q = Quaternion.AngleAxis(angle, Vector3.up);
        skeletonRoot.localPosition = q * skeletonRoot.localPosition;
        skeletonRoot.localRotation = q * skeletonRoot.localRotation;
    }

    void OnGUI()
    {
        if (keyStyle == null)
        {
            keyStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.LowerCenter, wordWrap = true };
            timeStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }
        const float size = 64f, gap = 10f;
        float x = (Screen.width - (size * 5 + gap * 4)) * 0.5f, y = Screen.height - size - 24f;
        Slot(new Rect(x, y, size, size), "Space", "회피", controller.DodgeCooldownLeft, controller.dodgeCooldown, false, true);
        Slot(new Rect(x + (size + gap), y, size, size), "우클릭", "회전 베기", spinSlashCooldownTimer, spinSlashCooldown, IsSpinSlashing, true);
        Slot(new Rect(x + (size + gap) * 2, y, size, size), "Q", "묠니르", throwCooldownTimer, throwCooldown, HammerAway, true);
        Slot(new Rect(x + (size + gap) * 3, y, size, size), "E", "천둥 강타", thunderCooldownTimer, thunderCooldown, IsSlamming, true);
        Slot(new Rect(x + (size + gap) * 4, y, size, size), "R", "휠윈드", whirlwindCooldownTimer, whirlwindCooldown, IsWhirlwinding, true);
    }

    void Slot(Rect r, string key, string label, float cooldownLeft, float cooldown, bool active, bool filled)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        if (filled)
        {
            GUI.color = active ? new Color(0.45f, 0.85f, 1f, 0.9f) : new Color(0.2f, 0.45f, 0.65f, 0.85f);
            GUI.DrawTexture(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), Texture2D.whiteTexture);
        }
        if (cooldownLeft > 0f)
        {
            // dark shutter that lowers as the skill comes back
            float k = Mathf.Clamp01(cooldownLeft / cooldown);
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(r.x + 3, r.y + 3, r.width - 6, (r.height - 6) * k), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
        keyStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(r.x + 5, r.y + 3, r.width, 18), key, keyStyle);
        nameStyle.normal.textColor = filled ? Color.white : new Color(1f, 1f, 1f, 0.45f);
        GUI.Label(new Rect(r.x, r.y, r.width, r.height - 4), label, nameStyle);
        if (cooldownLeft > 0f)
        {
            timeStyle.normal.textColor = Color.white;
            GUI.Label(r, cooldownLeft.ToString(cooldownLeft < 1f ? "0.0" : "0"), timeStyle);
        }
    }
}
