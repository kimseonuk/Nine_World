using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

// A walking, animated enemy (draugr, jotunn): closes in on the player and swings when in reach.
// As a boss it slams the ground in a telegraphed circle and roars for help at health thresholds.
// Enemies with their own attack patterns (einherjar, valkyries) override Think() and build their
// moves from the helpers below (MeleeStrike, Lunge, StartAction).
[RequireComponent(typeof(CharacterController))]
public class HumanoidEnemy : Enemy
{
    protected static readonly int SpeedHash = Animator.StringToHash("Speed");
    protected static readonly int AttackHash = Animator.StringToHash("Attack");
    protected static readonly int DieHash = Animator.StringToHash("Die");
    protected static readonly int RoarHash = Animator.StringToHash("Roar");
    private static readonly int LocoRateHash = Animator.StringToHash("LocoRate");
    private static readonly int HitState = Animator.StringToHash("Base Layer.Hit");
    private static readonly int FallState = Animator.StringToHash("Base Layer.Fall");
    private static readonly int GetUpState = Animator.StringToHash("Base Layer.GetUp");
    private static readonly int TransformState = Animator.StringToHash("Base Layer.Transform");
    private const float GetUpFrom = 2.4f;     // seconds into the get-up clip where it starts to rise
    private const float GetUpTime = 1.6f;

    [SerializeField] protected Animator animator;
    [Tooltip("Ground speed (m/s at prefab scale) of the fastest locomotion clip; faster than this, the clip speeds up")]
    [SerializeField] private float runStride = 1f;
    [SerializeField] private int maxHealth = 6;
    [SerializeField] protected float moveSpeed = 1.6f;
    [SerializeField] private float turnSpeed = 360f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Attack")]
    [SerializeField] protected float attackRange = 1.3f;     // starts a swing when the player is this close
    [SerializeField] protected float attackReach = 1.6f;     // the swing still lands if the player is within this
    [SerializeField] protected float attackDamage = 12f;
    [SerializeField] private float attackWindup = 0.6f;      // trigger -> hit
    [SerializeField] private float attackDuration = 1.3f;    // trigger -> can move again
    [SerializeField] protected float attackCooldown = 0.8f;

    [Header("Boss slam (radius > 0 makes the attack a ground slam)")]
    [SerializeField] private float slamRadius;
    [SerializeField] private float slamForward = 1.5f;       // slam center, in front of the boss
    [SerializeField] private float[] roarAtHealth = { };     // fractions of max health that trigger a roar
    [SerializeField] private float roarDuration = 2f;

    [Header("Getting hit")]
    [SerializeField] protected float knockbackSpeed = 3f;
    [SerializeField] private float knockbackDamping = 10f;
    [SerializeField] private Color hitTint = new Color(1f, 0.4f, 0.4f);
    [SerializeField] private float corpseTime = 2.5f;

    protected CharacterController controller;
    protected Transform player;
    protected PlayerStats playerStats;
    private int health;
    private float verticalVelocity;
    private Vector3 knockback;
    protected float cooldown;
    protected bool busy;   // attacking or roaring
    protected bool dead;
    private int roarsDone;
    protected GameObject warning; // the attack's ground telegraph while it winds up
    private Coroutine attackRoutine;
    protected Vector3 actionMove; // movement driven by an action (a lunge), on top of walking
    protected CollisionFlags lastMoveFlags; // what the last step ran into (a charge hitting a wall)
    private float flinchUntil, nextFlinch;
    private bool awakened;      // a boss past its phase change

    // A boss roar asks the room for reinforcements
    public event Action<HumanoidEnemy> Roared;

    // An extra area move some bosses use every few seconds (Niflheim: the mist and frost giants' bursts, Hel's soul
    // lances and death aura) — ported from the Niflheim project
    public enum AreaAttack { None, MistBurst, FrostNova, SoulLance, DeathAura, IcicleField, IcicleThrow, MistVeil, PoisonMist }
    private class SpecialMove { public AreaAttack kind; public float timer; }
    private readonly List<SpecialMove> specialMoves = new List<SpecialMove>();
    private readonly List<GameObject> areaMarks = new List<GameObject>();
    private static readonly Color MistColor = new Color(0.62f, 0.72f, 1.1f);
    private static readonly Color FrostColor = new Color(0.55f, 0.88f, 1f);

    // The arm can be posed by hand on top of the animation (Niflheim's twin giants' high five)
    private Transform armBone, foreArmBone, handBone;
    private Vector3 reachTarget;
    private float reachWeight;

    // ── The two giants' entrance: they stride up to each other, slap a high five overhead with a crack of
    // frost, then turn on the intruder and roar. About 2.7 s; the boss cutscene plays it on both. ──
    public void PlayHighFive(HumanoidEnemy partner, bool leader)
    {
        if (dead || partner == null)
            return;
        StartCoroutine(HighFive(partner, leader));
    }

    private IEnumerator HighFive(HumanoidEnemy partner, bool leader)
    {
        busy = true;
        float size = transform.localScale.x;
        Vector3 meet = Ground((transform.position + partner.transform.position) / 2f);
        Vector3 toMeet = Flat(meet - transform.position);
        Vector3 facing = toMeet.sqrMagnitude > 0.01f ? toMeet.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(facing);

        // both slap with the hand on the player's side, so the high five faces the camera
        Vector3 viewSide = Vector3.Cross(Vector3.up, Flat(partner.transform.position - transform.position).normalized);
        var mover = player != null ? player : FindAnyObjectByType<PlayerMovement>()?.transform;
        if (mover != null && Vector3.Dot(viewSide, Flat(mover.position - meet)) < 0f)
            viewSide = -viewSide;
        FindArm(Vector3.Dot(transform.right, viewSide) < 0f);

        // stride in until nearly toe to toe
        const float stride = 0.8f;
        float walk = Mathf.Max(0f, toMeet.magnitude - 0.52f * size);
        actionMove = facing * (walk / stride);
        yield return new WaitForSeconds(stride);
        actionMove = Vector3.zero;

        // swing the arm up to a point just above their heads, between them, on the camera's side
        float shoulder = armBone != null ? armBone.position.y - transform.position.y : 1.3f * size;
        Vector3 contact = Ground((transform.position + partner.transform.position) / 2f)
            + viewSide * 0.45f * size + Vector3.up * (shoulder + 0.4f * size);
        reachTarget = contact;
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            reachWeight = Mathf.SmoothStep(0f, 1f, t / 0.3f);
            yield return null;
        }
        reachWeight = 1f;

        // the slap
        if (leader)
        {
            CameraShake.Shake(0.3f, 0.22f);
            Shockwave.Spawn(Ground(contact), 1.6f * size, Color.Lerp(MistColor, FrostColor, 0.5f), 16);
            Material spark = Mats.Get("giant_highfive_spark", Color.white, 0.9f, new Color(0.7f, 0.95f, 1.3f) * 2f);
            for (int i = 0; i < 6; i++)
            {
                GameObject bit = Mats.Shape(PrimitiveType.Cube, null, contact + Random.insideUnitSphere * 0.5f * size,
                    Vector3.one * Random.Range(0.12f, 0.25f) * size, spark);
                bit.transform.rotation = Random.rotation;
                Destroy(bit, Random.Range(0.25f, 0.45f));
            }
            GameObject flash = Mats.Shape(PrimitiveType.Sphere, null, contact, Vector3.one * 0.9f * size, spark);
            Destroy(flash, 0.12f);
        }
        yield return new WaitForSeconds(0.3f);

        // hands down
        for (float t = 0f; t < 0.25f; t += Time.deltaTime)
        {
            reachWeight = 1f - Mathf.SmoothStep(0f, 1f, t / 0.25f);
            yield return null;
        }
        reachWeight = 0f;

        // ready for battle: both turn on the player and roar
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(RoarHash);
        yield return new WaitForSeconds(0.9f);
        cooldown = 1f;
        busy = false;
    }

    private void FindArm(bool left)
    {
        string side = left ? "Left" : "Right";
        armBone = foreArmBone = handBone = null;
        foreach (Transform t in GetComponentsInChildren<Transform>())
        {
            if (t.name == side + "Arm") armBone = t;
            else if (t.name == side + "ForeArm") foreArmBone = t;
            else if (t.name == side + "Hand") handBone = t;
        }
    }

    // After the animation has posed the body: the giant's move pose on top, then the high five's reach
    private void LateUpdate()
    {
        ApplyBodyPose();
        if (reachWeight <= 0f || armBone == null || foreArmBone == null || handBone == null)
            return;
        AimBone(armBone, foreArmBone.position, reachTarget, reachWeight);
        AimBone(foreArmBone, handBone.position, reachTarget, reachWeight);
    }

    // ───────────── Giant body poses ─────────────
    // The giants' clips are few and alike, so each of their moves is given its own shape by posing the body over the
    // animation: the spine leans and twists, the head tilts, and each hand reaches for a point set relative to its
    // shoulder (giant-local: x right, y up, z forward, in arm lengths), the elbow bending out and back. Poses ease in
    // and out, quickly for a blow and slowly for a wind-up.
    private struct BodyPose
    {
        public Vector3 left, right;
        public float leftWeight, rightWeight, bend, twist, head;
    }

    private Transform spineA, spineB, headBone, lArm, lFore, lHand, rArm, rFore, rHand;
    private bool poseBonesSearched;
    private BodyPose poseGoal, poseNow;
    private float poseEase = 6f, poseWeight, poseWeightGoal;

    private bool PoseBones()
    {
        if (!poseBonesSearched)
        {
            poseBonesSearched = true;
            Transform root = animator != null ? animator.transform : transform;
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                switch (t.name)
                {
                    case "Spine1": spineA = t; break;
                    case "Spine2": spineB = t; break;
                    case "Head": headBone = t; break;
                    case "LeftArm": lArm = t; break;
                    case "LeftForeArm": lFore = t; break;
                    case "LeftHand": lHand = t; break;
                    case "RightArm": rArm = t; break;
                    case "RightForeArm": rFore = t; break;
                    case "RightHand": rHand = t; break;
                }
        }
        return lArm != null && lFore != null && lHand != null && rArm != null && rFore != null && rHand != null;
    }

    // Strikes this pose, easing toward it at `ease` (higher is quicker); a hand left null stays with the animation
    protected void BodyPoseTo(Vector3? left, Vector3? right, float bend, float twist, float head, float ease)
    {
        if (!Heavy || !PoseBones())
            return;
        poseGoal = new BodyPose
        {
            left = left ?? poseNow.left, right = right ?? poseNow.right,
            leftWeight = left.HasValue ? 1f : 0f, rightWeight = right.HasValue ? 1f : 0f,
            bend = bend, twist = twist, head = head,
        };
        poseEase = ease;
        poseWeightGoal = 1f;
    }

    // Lets the animation have the body back
    protected void BodyPoseOff(float ease = 4f)
    {
        poseWeightGoal = 0f;
        poseEase = ease;
    }

    // Strikes a pose after a delay, if the move is still going by then
    private IEnumerator BodyPoseLater(float delay, Vector3? left, Vector3? right, float bend, float twist, float head, float ease)
    {
        yield return new WaitForSeconds(delay);
        if (busy && !dead)
            BodyPoseTo(left, right, bend, twist, head, ease);
    }

    private void ApplyBodyPose()
    {
        if (poseWeight <= 0.001f && poseWeightGoal <= 0f)
            return;
        if (dead || !PoseBones())
            return;
        float k = 1f - Mathf.Exp(-poseEase * Time.deltaTime);
        poseWeight = Mathf.Lerp(poseWeight, poseWeightGoal, k);
        poseNow.left = Vector3.Lerp(poseNow.left, poseGoal.left, k);
        poseNow.right = Vector3.Lerp(poseNow.right, poseGoal.right, k);
        poseNow.leftWeight = Mathf.Lerp(poseNow.leftWeight, poseGoal.leftWeight, k);
        poseNow.rightWeight = Mathf.Lerp(poseNow.rightWeight, poseGoal.rightWeight, k);
        poseNow.bend = Mathf.Lerp(poseNow.bend, poseGoal.bend, k);
        poseNow.twist = Mathf.Lerp(poseNow.twist, poseGoal.twist, k);
        poseNow.head = Mathf.Lerp(poseNow.head, poseGoal.head, k);
        float w = poseWeight;
        if (w <= 0.001f)
            return;

        // the spine first (it carries the shoulders), half the lean in each of its two upper joints
        Vector3 right = transform.right, up = transform.up;
        Quaternion lean = Quaternion.AngleAxis(poseNow.bend * 0.5f * w, right) * Quaternion.AngleAxis(poseNow.twist * 0.5f * w, up);
        if (spineA != null) spineA.rotation = lean * spineA.rotation;
        if (spineB != null) spineB.rotation = lean * spineB.rotation;
        if (headBone != null) headBone.rotation = Quaternion.AngleAxis(poseNow.head * w, right) * headBone.rotation;

        // then the arms
        ReachArm(lArm, lFore, lHand, poseNow.left, -1f, w * poseNow.leftWeight);
        ReachArm(rArm, rFore, rHand, poseNow.right, 1f, w * poseNow.rightWeight);
    }

    private void ReachArm(Transform upper, Transform lower, Transform end, Vector3 local, float side, float weight)
    {
        if (weight <= 0.001f)
            return;
        float length = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, end.position);
        Vector3 shoulder = upper.position;
        Vector3 target = shoulder + transform.rotation * local * length;
        // the elbow bends out to its side, down and back
        Vector3 pole = shoulder + transform.rotation * new Vector3(0.7f * side, -0.5f, -0.6f) * length;
        TwoBoneIK(upper, lower, end, target, pole, weight);
    }

    // Bends a two-bone limb (upper, lower, end) so its end reaches the target, the middle joint toward the pole
    private static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole, float weight)
    {
        float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
        Vector3 toTarget = target - a.position;
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(la - lb) + 0.001f, (la + lb) * 0.999f);
        if (toTarget.sqrMagnitude < 1e-6f)
            return;
        Vector3 dir = toTarget.normalized;
        Vector3 bend = (pole - a.position) - dir * Vector3.Dot(pole - a.position, dir);
        if (bend.sqrMagnitude < 1e-6f)
            bend = Vector3.Cross(dir, Vector3.up);
        bend.Normalize();
        float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
        float angle = Mathf.Acos(cosA);
        Vector3 elbow = a.position + dir * Mathf.Cos(angle) * la + bend * Mathf.Sin(angle) * la;
        Quaternion turnA = Quaternion.FromToRotation(b.position - a.position, elbow - a.position);
        a.rotation = Quaternion.Slerp(Quaternion.identity, turnA, weight) * a.rotation;
        Vector3 reach = a.position + dir * d;
        Quaternion turnB = Quaternion.FromToRotation(c.position - b.position, reach - b.position);
        b.rotation = Quaternion.Slerp(Quaternion.identity, turnB, weight) * b.rotation;
    }

    // Where its right hand is now (where it holds what it throws), or a point before its chest
    private Vector3 RightHandPoint => rHand != null && Heavy ? rHand.position
        : transform.position + Vector3.up * 1.6f * transform.localScale.x + Facing * 0.6f * transform.localScale.x;

    private static void AimBone(Transform bone, Vector3 childPosition, Vector3 target, float weight)
    {
        Vector3 now = childPosition - bone.position, want = target - bone.position;
        if (now.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f)
            return;
        Quaternion turn = Quaternion.FromToRotation(now, want);
        bone.rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * bone.rotation;
    }

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;
    protected float HealthFraction => maxHealth > 0 ? (float)health / maxHealth : 0f;

    protected virtual void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        health = maxHealth;
        cooldown = Random.Range(0.3f, 1f);
    }

    protected virtual void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null)
        {
            player = movement.transform;
            playerStats = movement.GetComponent<PlayerStats>();
        }
    }

    public override void Scale(float healthMultiplier, float damageMultiplier)
    {
        maxHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * healthMultiplier));
        health = maxHealth;
        attackDamage *= damageMultiplier;
    }

    // A bigger body swings a longer arm
    public void ScaleReach(float factor)
    {
        attackRange *= factor;
        attackReach *= factor;
    }

    // Turns this enemy into a realm boss: big, tough, with a telegraphed slam and roars
    public virtual void MakeBoss(string bossName, int bossHealth, float bossDamage, float size)
    {
        IsBoss = true;
        DisplayName = bossName;
        transform.localScale = Vector3.one * size;
        maxHealth = health = bossHealth;
        attackDamage = bossDamage;
        attackRange = 1.2f * size;
        attackReach = 1.5f * size;
        slamRadius = 1.3f * size;
        slamForward = 0.9f * size;
        knockbackSpeed = 0.3f;
        // Calls for help either side of the half-health awakening
        roarAtHealth = new[] { 0.75f, 0.25f };
        GoldDrop = 25;
    }

    private void Update()
    {
        if (dead)
            return;

        cooldown -= Time.deltaTime;
        Vector3 move = Vector3.zero;
        bool heavy = Heavy;
        // A big boss turns to face the player at the start of a move over a moment, not in a single frame
        if (heavy && faceGoal.HasValue)
        {
            float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, faceGoal.Value, ref yawVelocity, 0.12f, 720f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, faceGoal.Value)) < 1f)
                faceGoal = null;
        }
        // A flinch from a hit holds it in place for a moment
        if (!busy && Time.time >= flinchUntil && player != null && GameManager.IsRunActive)
        {
            Vector3 toPlayer = Flat(player.position - transform.position);
            float distance = toPlayer.magnitude;
            if (distance > 0.01f)
            {
                if (heavy)
                {
                    // a giant's weight: it eases into a turn and out of it again
                    faceGoal = null;
                    float want = Quaternion.LookRotation(toPlayer).eulerAngles.y;
                    float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, want, ref yawVelocity, 0.35f, turnSpeed * 0.5f);
                    transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                }
                else
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toPlayer), turnSpeed * Time.deltaTime);
            }
            // The first time it sees the player close: a battle cry before it comes on (bosses have their own entrances)
            if (!criedOut && !IsBoss && distance < 14f && !BossCutscene.Playing)
            {
                criedOut = true;
                StartAction(BattleCry(), 0.15f);
            }
            else
            {
                // A boss's area move comes round every few seconds, in place of its next ordinary attack
                SpecialMove ready = null;
                foreach (SpecialMove m in specialMoves)
                {
                    m.timer -= Time.deltaTime;
                    if (ready == null && cooldown <= 0f && m.timer <= 0f)
                        ready = m;
                }
                if (ready != null)
                {
                    ready.timer = MoveInterval(ready.kind);
                    StartAction(MoveRoutine(ready.kind), attackCooldown + 0.6f);
                }
                else
                    move = Think(toPlayer, distance);
            }
        }
        move += actionMove;

        // A giant gathers speed and slows down over a few strides instead of starting and stopping dead (quicker
        // when a move drives it, so its lunges and charges still bite). Not in a cutscene, where the high five's
        // steps are measured out exactly.
        if (heavy && !BossCutscene.Playing)
        {
            float settle = actionMove.sqrMagnitude > 0.01f ? 0.12f : 0.32f;
            Vector3 flat = Vector3.Lerp(Flat(moveNow), Flat(move), 1f - Mathf.Exp(-Time.deltaTime / settle));
            moveNow = flat;
            move = flat;
        }
        else
            moveNow = Flat(move);
        if (busy || player == null)
            yawVelocity = faceGoal.HasValue ? yawVelocity : Mathf.Lerp(yawVelocity, 0f, 1f - Mathf.Exp(-6f * Time.deltaTime));

        // Locomotion follows the ground speed per unit of body size: the blend picks idle, walk or run, and
        // past the run clip's own pace the clip plays faster, so the feet keep up with the ground
        if (animator != null)
        {
            float pace = Flat(move).magnitude / Mathf.Max(0.01f, transform.localScale.x);
            // a giant turning on the spot shuffles its feet round instead of sliding
            if (heavy && !busy)
                pace = Mathf.Max(pace, Mathf.Clamp01(Mathf.Abs(yawVelocity) / 90f) * 0.35f);
            float damp = heavy ? 0.22f : 0.1f;
            animator.SetFloat(SpeedHash, pace, damp, Time.deltaTime);
            animator.SetFloat(LocoRateHash, Mathf.Clamp(pace / runStride, 1f, 2.5f), damp, Time.deltaTime);
        }

        knockback = Vector3.Lerp(knockback, Vector3.zero, knockbackDamping * Time.deltaTime);
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;
        lastMoveFlags = controller.Move((move + knockback + Vector3.up * verticalVelocity) * Time.deltaTime);
    }

    // The twin giants (and any other plain humanoid boss) move with weight: eased turns and strides, softer blends
    private bool Heavy => IsBoss && GetType() == typeof(HumanoidEnemy);
    private Vector3 moveNow;       // its ground velocity right now, easing toward what it wants
    private float yawVelocity;     // how fast it is turning (degrees a second)
    private float? faceGoal;       // a heading it is swinging round to at the start of a move

    // Where it means to face: the heading it is swinging round to, or the way it faces now. Moves aim along this,
    // so a giant still turning into its attack strikes where it is turning to.
    protected Vector3 Facing => faceGoal.HasValue ? Quaternion.Euler(0f, faceGoal.Value, 0f) * Vector3.forward : transform.forward;

    // Decide what to do this frame (not busy, facing the player). Returns the walking velocity.
    // Default: walk up to the player and swing when in range.
    protected virtual Vector3 Think(Vector3 toPlayer, float distance)
    {
        if (distance <= attackRange)
        {
            if (cooldown <= 0f)
                StartAction(Attack(), attackCooldown);
            return Vector3.zero;
        }
        return toPlayer / distance * moveSpeed;
    }

    // Runs an attack routine: the enemy is busy until it ends, then waits out the cooldown.
    // Interrupt() and Knockdown() cut it off.
    protected void StartAction(IEnumerator action, float cooldownAfter)
    {
        attackRoutine = StartCoroutine(RunAction(action, cooldownAfter));
    }

    private IEnumerator RunAction(IEnumerator action, float cooldownAfter)
    {
        busy = true;
        // Step through nested routines here, so stopping this one coroutine stops all of them
        var stack = new Stack<IEnumerator>();
        stack.Push(action);
        while (stack.Count > 0)
        {
            IEnumerator top = stack.Peek();
            if (!top.MoveNext())
            {
                stack.Pop();
                continue;
            }
            if (top.Current is IEnumerator nested)
                stack.Push(nested);
            else
                yield return top.Current;
        }
        EndAction();
        cooldown = cooldownAfter;
    }

    private void EndAction()
    {
        actionMove = Vector3.zero;
        if (warning != null)
            Destroy(warning);
        ClearAreaMarks();
        reachWeight = 0f;
        BodyPoseOff(4f);
        busy = false;
        attackRoutine = null;
    }

    // Gives a boss its area move; the first comes after firstDelay seconds of fighting
    public void SetAreaAttack(AreaAttack kind, float firstDelay)
    {
        if (kind == AreaAttack.None)
            return;
        specialMoves.RemoveAll(m => m.kind == kind);
        specialMoves.Add(new SpecialMove { kind = kind, timer = firstDelay });
    }

    private static float MoveInterval(AreaAttack kind) => kind switch
    {
        AreaAttack.MistBurst => 9f,
        AreaAttack.FrostNova => 10f,
        AreaAttack.SoulLance => 7f,
        AreaAttack.DeathAura => 15f,
        AreaAttack.IcicleField => 13f,
        AreaAttack.IcicleThrow => 7f,
        AreaAttack.MistVeil => 20f,
        AreaAttack.PoisonMist => 8f,
        _ => 10f,
    };

    private IEnumerator MoveRoutine(AreaAttack kind) => kind switch
    {
        AreaAttack.MistBurst => MistBurst(),
        AreaAttack.FrostNova => FrostNova(),
        AreaAttack.SoulLance => SoulLance(),
        AreaAttack.IcicleField => IcicleField(),
        AreaAttack.IcicleThrow => IcicleThrow(),
        AreaAttack.MistVeil => MistVeilCast(),
        AreaAttack.PoisonMist => PoisonMist(),
        _ => DeathAura(),
    };

    // ── Icicle field (the frost giant): it slams the ground and the floor all around the player splits - circles of
    // frost spread, then icicles burst up through them, in two waves ──
    private IEnumerator IcicleField()
    {
        FacePlayer();
        float r = 1.4f;
        float damage = attackDamage * 0.8f;
        Material ice = IcicleMaterial;
        for (int wave = 0; wave < 2 && !dead; wave++)
        {
            // one circle under the player and a scatter round them and round the giant
            var spots = new List<Vector3> { Ground(player.position) };
            for (int i = 0; i < 9; i++)
            {
                Vector3 around = i % 3 == 0 ? transform.position : player.position;
                spots.Add(Ground(around + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(2.5f, 7f)));
            }
            // it strides round the player, then brings its fists down as the ice breaks out (the slam lands on the burst)
            float walk = wave == 0 ? 0.8f : 0.6f;
            float windup = walk + attackWindup;
            // frost-blue warnings, and the cold gathering in its fists as it raises them
            foreach (Vector3 s in spots)
                areaMarks.Add(IceVfx.Frosted(Telegraph.Circle(s, r, windup)));
            IceVfx.Flash(transform.position + Vector3.up * 2.2f * transform.localScale.x, 9f, 3.5f, windup + 0.2f, windup);
            // one fist raised high as it strides, the other arm out for balance (the right, then the left), then it
            // punches the fist down into the ice
            float fist = wave == 0 ? 1f : -1f;
            Vector3 high = new Vector3(0.25f * fist, 0.95f, 0.05f), balance = new Vector3(-0.85f * fist, 0.05f, 0.25f);
            Vector3 punch = new Vector3(0.1f * fist, -0.85f, 0.6f), brace = new Vector3(-0.6f * fist, -0.4f, 0.5f);
            BodyPoseTo(fist > 0f ? balance : high, fist > 0f ? high : balance, -10f, -20f * fist, -12f, 4f);
            yield return Stride(Circling(wave == 0 ? 1f : -1f, 1.3f), walk);
            StartCoroutine(BodyPoseLater(Mathf.Max(0f, attackWindup - 0.2f), fist > 0f ? brace : punch, fist > 0f ? punch : brace, 40f, 15f * fist, 18f, 24f));
            yield return SwingIn(1.2f, attackWindup, "Ground");   // (the giants: the fist driven into the ice)
            ClearAreaMarks();
            if (dead)
                yield break;
            bool hit = false;
            Vector3 middle = Vector3.zero;
            foreach (Vector3 s in spots)
            {
                // a sheaf of crystal icicles thrusting up out of the ground, inside the glacial burst (Baroque 3D VFX art)
                for (int k = 0; k < 5; k++)
                {
                    float h = k == 0 ? Random.Range(2.2f, 2.8f) : Random.Range(1.1f, 2f);
                    GameObject spike = DungeonGenerator.Crystal6(null, s + new Vector3(Random.Range(-0.55f, 0.55f), 0f, Random.Range(-0.55f, 0.55f)),
                        Quaternion.Euler(Random.Range(-22f, 22f), Random.Range(0f, 360f), Random.Range(-22f, 22f)), k == 0 ? 0.32f : 0.22f, h, ice);
                    StartCoroutine(Erupt(spike.transform, 0.12f, Random.Range(0.8f, 1.1f)));
                    Destroy(spike, 2f);   // (gone even if the giant falls mid-cast)
                }
                IceVfx.Impact(s, r, 3.4f, false);
                middle += s;
                if (!hit && playerStats != null && FlatDistance(player.position, s) <= r)
                {
                    hit = true;
                    playerStats.TakeDamage(damage, this);
                }
            }
            // one wash of cold light over the whole field (a light per spike would be too many)
            IceVfx.Flash(middle / spots.Count + Vector3.up * 2f, 16f, 4f, 0.6f);
            CameraShake.Shake(0.3f, 0.15f);
            yield return new WaitForSeconds(0.35f);
        }
        BodyPoseOff(3f);
        yield return new WaitForSeconds(0.4f);
    }

    // Clear blue ice that catches the light without glowing white
    private static Material IcicleMaterial => Mats.Get("giant_icicle_crystal", new Color(0.55f, 0.8f, 1f), 0.95f, new Color(0.08f, 0.3f, 0.55f));

    // An object shooting up out of the ground: it grows from nothing in a blink, stays a moment, and shatters away
    private static IEnumerator Erupt(Transform t, float rise, float stay)
    {
        if (t == null)
            yield break;
        Vector3 full = t.localScale, end = t.position, start = end - t.up * full.y * 1.6f;
        for (float k = 0f; k < rise && t != null; k += Time.deltaTime)
        {
            t.position = Vector3.Lerp(start, end, k / rise);
            yield return null;
        }
        if (t != null)
            t.position = end;
        yield return new WaitForSeconds(stay);
        for (float k = 0f; k < 0.2f && t != null; k += Time.deltaTime)
        {
            t.localScale = Vector3.Lerp(full, Vector3.zero, k / 0.2f);
            yield return null;
        }
        if (t != null)
            Destroy(t.gameObject);
    }

    // ── Icicle throw (the frost giant): it tears three great icicles out of the frozen ground and hurls them in a
    // fan of straight lines at the player ──
    private IEnumerator IcicleThrow()
    {
        FacePlayer();
        float size = transform.localScale.x;
        float damage = attackDamage * 0.7f;
        const float length = 17f, width = 1.4f, speed = 30f;
        Material ice = IcicleMaterial;
        var dirs = new[] { -16f, 0f, 16f }.Select(a => Quaternion.Euler(0f, a, 0f) * Facing).ToArray();
        Vector3 from = Ground(transform.position) + Facing * 0.8f * size;
        foreach (Vector3 d in dirs)
            areaMarks.Add(IceVfx.Frosted(Telegraph.Line(from, d, length, width, 1.1f)));
        if (animator != null)
            PlayMove("Chain", AttackHash);   // (the giants: a hurling throw)
        // the wind-up: the throwing arm drawn back high behind its head, the other arm pointing the way, body turned
        BodyPoseTo(new Vector3(-0.55f, 0.25f, 0.75f), new Vector3(0.35f, 0.8f, -0.55f), -12f, -28f, -8f, 3.5f);
        // the icicles grow in its hand while it winds up, the cold glowing brighter round them
        Vector3 hand = transform.position + Vector3.up * 1.6f * size + Facing * 0.6f * size;
        IceVfx.Flash(hand, 7f, 3.5f, 1.25f, 1.1f);
        var held = new List<GameObject>();
        foreach (Vector3 d in dirs)
        {
            GameObject spike = DungeonGenerator.Crystal6(null, hand, Quaternion.LookRotation(d) * Quaternion.Euler(90f, 0f, 0f), 0.3f, 0.2f, ice);
            held.Add(spike);
            areaMarks.Add(spike);
        }
        bool thrown = false;
        for (float t = 0f; t < 1.1f; t += Time.deltaTime)
        {
            foreach (GameObject s in held)
            {
                s.transform.localScale = new Vector3(0.3f, Mathf.Lerp(0.2f, 1.9f, t / 1.1f), 0.3f);
                s.transform.position = RightHandPoint;   // (held in its fist as the arm draws back)
            }
            // the throw itself: the body whips round and the arm comes over and through
            if (!thrown && t > 0.9f)
            {
                thrown = true;
                BodyPoseTo(new Vector3(-0.5f, -0.45f, -0.3f), new Vector3(0.1f, 0.1f, 1f), 22f, 28f, 8f, 26f);
            }
            yield return null;
        }
        hand = RightHandPoint;
        ClearAreaMarks();
        if (dead)
            yield break;

        // they fly, dropping to chest height
        var flying = new List<GameObject>();
        var trails = new List<GameObject>();
        foreach (Vector3 d in dirs)
        {
            GameObject spike = DungeonGenerator.Crystal6(null, hand, Quaternion.LookRotation(d) * Quaternion.Euler(90f, 0f, 0f), 0.32f, 2f, ice);
            flying.Add(spike);
            areaMarks.Add(spike);
            // the shard volley streaking along with it (Baroque 3D VFX art)
            trails.Add(IceVfx.ShardVolley(hand, d, 5.5f, 3.2f));
        }
        bool hit = false;
        for (float dist = 0f; dist < length; dist += speed * Time.deltaTime)
        {
            for (int i = 0; i < dirs.Length; i++)
            {
                flying[i].transform.position = Vector3.Lerp(hand, from + Vector3.up, Mathf.Clamp01(dist / 4f)) + dirs[i] * dist;
                if (trails[i] != null)
                    trails[i].transform.position = flying[i].transform.position - dirs[i] * 1.6f - Vector3.up * 0.6f;
                if (!hit && playerStats != null && InStrip(from, dirs[i], dist, width / 2f))
                {
                    hit = true;
                    playerStats.TakeDamage(damage, this);
                    player.GetComponent<PlayerMovement>()?.Push(dirs[i] * 5f);
                }
            }
            yield return null;
        }
        // where each icicle comes down, it shatters into a small glacial burst
        foreach (Vector3 d in dirs)
            IceVfx.Impact(from + d * length, 1.3f, 2.4f, d == dirs[1]);
        foreach (GameObject trail in trails)
            if (trail != null)
                trail.GetComponent<VfxFade>().FadeOutNow();
        ClearAreaMarks();
        BodyPoseOff(3f);
        yield return new WaitForSeconds(0.5f);
    }

    // ── Mist veil (the mist giant): it breathes out a fog that fills the whole arena - the world goes white past a few
    // metres and the cold, wet mist drags at the player's legs for a while ──
    private IEnumerator MistVeilCast()
    {
        FacePlayer();
        Material mist = Mats.Get("giant_mist", MistColor, 0.1f, MistColor * 0.8f);
        float size = transform.localScale.x;
        Vector3 chest = transform.position + Vector3.up * 1.6f * size;
        // it strides round the player with the fog pouring off it, then sweeps its arms out to fling it over the arena
        Vector3 circle = Circling(Random.value < 0.5f ? 1f : -1f, 1.4f);
        for (int i = 0; i < 10; i++)
        {
            // arms raised over its head, swaying as it calls the fog up
            float sway = Mathf.Sin(i * 0.8f);
            BodyPoseTo(new Vector3(-0.35f + sway * 0.2f, 0.9f, 0.15f - sway * 0.2f), new Vector3(0.35f + sway * 0.2f, 0.9f, 0.15f + sway * 0.2f),
                -16f, sway * 12f, -22f, 3f);
            actionMove = circle;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(circle), 300f * Time.deltaTime);
            chest = transform.position + Vector3.up * 1.6f * size;
            Vector3 at = chest + Random.insideUnitSphere * 1.2f;
            if (MistVfx.Bank(at, Random.onUnitSphere * 0.6f, Random.Range(2.5f, 4f), 0.25f, 0.2f, 0.4f, 0.8f) == null)
            {
                GameObject puff = Mats.Shape(PrimitiveType.Sphere, null, at, Vector3.one * Random.Range(1f, 2f), mist);
                Destroy(puff, 0.8f);
            }
            yield return new WaitForSeconds(0.08f);
        }
        actionMove = Vector3.zero;
        if (dead)
            yield break;
        FacePlayer();
        if (animator != null)
            PlayMove("Chant", AttackHash);   // (the giants: arms raised, calling the fog)
        // gathered high together, then flung out wide and low as the fog is thrown over the arena
        BodyPoseTo(new Vector3(-0.1f, 1f, 0.2f), new Vector3(0.1f, 1f, 0.2f), -20f, 0f, -25f, 6f);
        yield return new WaitForSeconds(attackWindup * 0.8f);   // the sweep of its arms throws the fog out
        if (dead)
            yield break;
        BodyPoseTo(new Vector3(-1f, -0.05f, 0.25f), new Vector3(1f, -0.05f, 0.25f), 14f, 0f, 5f, 18f);
        // then breathes it out: a rolling wave of spectral mist surging off it in every direction over the arena
        Vector3 ground = Ground(transform.position);
        for (int i = 0; i < 10; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 36f + Random.Range(-10f, 10f), 0f) * transform.forward;
            MistVfx.Bank(ground + Vector3.up * 1.6f + dir * 2f, dir * Random.Range(9f, 12f), Random.Range(7f, 9f), 0.35f, 0.9f, 1.1f, 0.9f);
        }
        MistVfx.Bank(chest, Vector3.zero, 9f * Mathf.Min(size, 2f), 0.3f, 0.5f, 1.2f, 0.85f);
        MistVeil.Cast(7f, 0.55f);
        CameraShake.Shake(0.3f, 0.08f);
        StartCoroutine(MistExhale(7f));
        yield return new WaitForSeconds(0.45f);
        BodyPoseOff(3f);
        yield return new WaitForSeconds(0.15f);
    }

    // While its fog hangs, the mist giant keeps breathing more of it: banks pour off its body and roll out over the arena
    private IEnumerator MistExhale(float seconds)
    {
        float end = Time.time + seconds;
        float size = transform.localScale.x;
        while (Time.time < end && !dead)
        {
            Vector3 dir = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            Vector3 at = transform.position + Vector3.up * Random.Range(0.8f, 1.8f) * size + dir * 1.2f;
            MistVfx.Bank(at, dir * Random.Range(3f, 6f), Random.Range(4.5f, 7f), 0.4f, 0.8f, 1.2f, 0.85f);
            yield return new WaitForSeconds(0.2f);
        }
    }

    // ── Poison mist (the mist giant): it rears back and blows a long straight stream of green poisoned mist that hangs
    // over the ground for a few seconds, burning anyone inside ──
    private const float PoisonPerSecond = 3f, PoisonSeconds = 7f;

    private IEnumerator PoisonMist()
    {
        FacePlayer();
        float size = transform.localScale.x;
        const float length = 15f, width = 3.2f;
        Vector3 dir = Facing;
        Vector3 from = Ground(transform.position) + dir * 0.8f * size;
        // it steps back to wind up while the line fills, then lunges in with a swing, spraying the venom down the line
        float backTime = Mathf.Max(0.1f, 1.1f + 0.4f - attackWindup);
        warning = Telegraph.Line(from, dir, length, width, backTime + attackWindup * 0.6f);
        float rate = moveSpeed * size * 0.5f;
        // the deep breath: rearing back, chest out and head thrown up, hands drawn in to its chest
        BodyPoseTo(new Vector3(0.35f, 0.05f, 0.4f), new Vector3(-0.35f, 0.05f, 0.4f), -24f, 0f, -28f, 3f);
        for (float t = 0f; t < backTime && !dead; t += Time.deltaTime)
        {
            actionMove = -dir * rate;   // walking backwards, still facing the line
            transform.rotation = Quaternion.LookRotation(dir);
            yield return null;
        }
        actionMove = Vector3.zero;
        if (animator != null)
            PlayMove("Cast", AttackHash);    // (the giants: a two-handed thrust of venom)
        // and blows: lunging forward over the line, head thrust out, arms swept back behind it
        BodyPoseTo(new Vector3(-0.75f, -0.15f, -0.55f), new Vector3(0.75f, -0.15f, -0.55f), 30f, 0f, 12f, 10f);
        for (float t = 0f; t < attackWindup * 0.6f && !dead; t += Time.deltaTime)
        {
            actionMove = dir * rate * 2.2f;
            transform.rotation = Quaternion.LookRotation(dir);
            yield return null;
        }
        actionMove = Vector3.zero;
        if (warning != null)
            Destroy(warning);
        if (dead)
            yield break;

        var poisonColor = new Color(0.45f, 0.95f, 0.35f);
        Material poison = Mats.Get("giant_poison_mist", poisonColor, 0.1f, poisonColor * 0.7f);
        Vector3 mouth = transform.position + Vector3.up * 1.7f * size + dir * 0.6f * size;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        bool art = MistVfx.Venom(mouth, dir * 2f, 4.5f, 0.15f, 0.25f, 0.4f) != null;   // the venom bursting from its mouth
        // the stream rolls out along the line: the giant's own mist, stained green and violet, with venom bursting in it
        for (float d = 0f; d < length; d += 1.2f)
        {
            Vector3 at = Vector3.Lerp(mouth, from + Vector3.up * 0.8f, Mathf.Clamp01(d / 4f)) + dir * d;
            if (art)
            {
                MistVfx.Bank(at + side * Random.Range(-0.6f, 0.6f), dir * 1.5f + side * Random.Range(-0.4f, 0.4f), Random.Range(4.5f, 6f),
                    0.25f, 3.0f, 1f, 0.75f, Random.value < 0.7f ? MistVfx.PoisonTint : MistVfx.VenomTint);
                if (Mathf.Repeat(d, 2.4f) < 1.2f)
                    MistVfx.Venom(at + side * Random.Range(-width, width) * 0.3f + Vector3.up * 0.3f, Vector3.up * 0.3f, Random.Range(2.8f, 3.8f), 0.2f, 0.5f, 0.6f);
            }
            else
                for (int k = 0; k < 2; k++)
                {
                    GameObject cloud = Mats.Shape(PrimitiveType.Sphere, null, at + side * Random.Range(-width, width) * 0.35f,
                        new Vector3(Random.Range(1.4f, 2.2f), Random.Range(0.7f, 1.1f), Random.Range(1.4f, 2.2f)), poison);
                    areaMarks.Add(cloud);
                }
            yield return new WaitForSeconds(0.03f);
        }
        if (playerStats != null && InStrip(from, dir, length, width / 2f))
        {
            playerStats.TakeDamage(attackDamage * 0.6f, this);
            playerStats.ApplyPoison(PoisonPerSecond, PoisonSeconds);
        }
        BodyPoseOff(3f);
        // it lingers: whoever walks into it is poisoned (3 HP a second for 7 s, then it wears off): venom keeps bubbling up
        for (float t = 0f; t < 3.2f && !dead; t += 0.4f)
        {
            if (playerStats != null && InStrip(from, dir, length, width / 2f))
                playerStats.ApplyPoison(PoisonPerSecond, PoisonSeconds);
            if (art && t < 2.6f)
                for (int k = 0; k < 2; k++)
                    MistVfx.Venom(from + dir * Random.Range(1f, length) + side * Random.Range(-width, width) * 0.35f + Vector3.up * 0.4f,
                        Vector3.up * 0.4f, Random.Range(2f, 3f), 0.2f, 0.3f, 0.6f, 0.85f);
            yield return new WaitForSeconds(0.4f);
        }
        ClearAreaMarks();
    }

    private void ClearAreaMarks()
    {
        foreach (GameObject mark in areaMarks)
            if (mark != null)
                Destroy(mark);
        areaMarks.Clear();
    }

    // ── Mist burst (the mist giant): a roar, and the ground under and around the player fills with mist
    // that bursts - then a second, quicker volley wherever the player ran to ──
    private IEnumerator MistBurst()
    {
        FacePlayer();
        if (animator != null)
            PlayMove("Curse", RoarHash);
        float r = Mathf.Min(1.1f * transform.localScale.x, 2.2f);   // ~2 m circles, however big the giant
        float damage = attackDamage * 0.75f;

        // first volley: where the player stands and a ring around it
        Vector3 centre = Ground(player.position);
        var spots = new List<Vector3> { centre };
        float turn = Random.Range(0f, 360f);
        for (int i = 0; i < 5; i++)
            spots.Add(centre + Quaternion.Euler(0f, turn + i * 72f, 0f) * Vector3.forward * r * 1.9f);
        MistHandsToMouth(4f);
        yield return MistVolley(spots, r, 1.3f, damage);

        // second volley: three tighter bursts on the player's new spot
        yield return new WaitForSeconds(0.12f);
        MistHandsToMouth(12f);
        yield return new WaitForSeconds(0.13f);
        centre = Ground(player.position);
        spots = new List<Vector3> { centre };
        turn = Random.Range(0f, 360f);
        for (int i = 0; i < 2; i++)
            spots.Add(centre + Quaternion.Euler(0f, turn + i * 180f, 0f) * Vector3.forward * r * 1.3f);
        yield return MistVolley(spots, r, 0.85f, damage);
        BodyPoseOff(3f);
        yield return new WaitForSeconds(0.5f);
    }

    // the mist giant's hands cupped to its mouth, gathering the mist, head bowed
    private void MistHandsToMouth(float ease) =>
        BodyPoseTo(new Vector3(0.45f, 0.45f, 0.4f), new Vector3(-0.45f, 0.45f, 0.4f), -6f, 0f, 14f, ease);

    private IEnumerator MistVolley(List<Vector3> spots, float radius, float windup, float damage)
    {
        foreach (Vector3 spot in spots)
            areaMarks.Add(Telegraph.Circle(spot, radius, windup));
        // the palms thrust out at the player just as the mist bursts
        yield return new WaitForSeconds(Mathf.Max(0f, windup - 0.18f));
        BodyPoseTo(new Vector3(-0.2f, 0.15f, 1f), new Vector3(0.2f, 0.15f, 1f), 20f, 0f, -4f, 22f);
        yield return new WaitForSeconds(Mathf.Min(windup, 0.18f));
        ClearAreaMarks();
        if (dead)
            yield break;

        Material mist = Mats.Get("giant_mist", MistColor, 0.1f, MistColor * 0.8f);
        bool hit = false;
        foreach (Vector3 spot in spots)
        {
            Shockwave.Spawn(spot, radius, MistColor, 8);
            for (int i = 0; i < 4; i++)
            {
                GameObject puff = Mats.Shape(PrimitiveType.Sphere, null,
                    spot + Random.insideUnitSphere * radius * 0.5f + Vector3.up * Random.Range(0.3f, 1.4f),
                    Vector3.one * Random.Range(0.6f, 1.2f), mist);
                Destroy(puff, Random.Range(0.35f, 0.6f));
            }
            if (!hit && playerStats != null && FlatDistance(player.position, spot) <= radius)
            {
                hit = true;
                playerStats.TakeDamage(damage, this);
            }
        }
        CameraShake.Shake(0.25f, 0.12f);
    }

    // ── Frost nova (the frost giant): it gathers cold with a long roar, then slams the ground and a wave of ice
    // bursts out all around it - get out of the circle before it lands ──
    private IEnumerator FrostNova()
    {
        FacePlayer();
        float radius = Mathf.Min(3.6f * transform.localScale.x, 8.5f);   // ~7-8.5 m: room to run out of it inside the arena
        const float windup = 1.6f;
        // (a giant gathers the cold with its arms spread wide and palms up, head back, instead of roaring)
        if (animator != null)
        {
            if (Heavy)
                PlayMove("Chant", RoarHash);
            else
                animator.SetTrigger(RoarHash);
        }
        BodyPoseTo(new Vector3(-0.95f, 0.3f, 0.2f), new Vector3(0.95f, 0.3f, 0.2f), -16f, 0f, -22f, 3f);
        bool raised = false, smashed = false;
        warning = IceVfx.Frosted(Telegraph.Circle(transform.position, radius, windup));

        // frost gathers around its feet while it roars: crystals of rime pushing up and the cold light swelling
        Material ice = IcicleMaterial;
        Vector3 feet = Ground(transform.position);
        IceVfx.Flash(feet + Vector3.up * 1.5f, radius * 1.6f, 4f, windup + 0.15f, windup);
        for (float t = 0f; t < windup; t += 0.16f)
        {
            if (t >= windup - attackWindup && t < windup - attackWindup + 0.16f && animator != null)
                PlayMove("Ground", AttackHash);   // the slam lands as the circle fills
            // both fists swung up together over its head, then hammered down into the ice
            if (!raised && t >= windup - attackWindup)
            {
                raised = true;
                BodyPoseTo(new Vector3(-0.15f, 1f, 0.15f), new Vector3(0.15f, 1f, 0.15f), -14f, 0f, -18f, 9f);
            }
            if (!smashed && t >= windup - 0.25f)
            {
                smashed = true;
                BodyPoseTo(new Vector3(-0.15f, -0.85f, 0.6f), new Vector3(0.15f, -0.85f, 0.6f), 45f, 0f, 22f, 26f);
            }
            float a = Random.Range(0f, 360f);
            Vector3 at = feet + Quaternion.Euler(0f, a, 0f) * Vector3.forward * Random.Range(1f, 2.4f);
            GameObject shard = DungeonGenerator.Crystal6(null, at, Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(Random.Range(10f, 30f), 0f, 0f),
                0.14f, Random.Range(0.4f, 0.8f) * (0.6f + t / windup), ice);
            StartCoroutine(Erupt(shard.transform, 0.1f, 0.3f));
            Destroy(shard, 1f);
            yield return new WaitForSeconds(0.16f);
        }
        if (warning != null)
            Destroy(warning);
        if (dead)
            yield break;

        // the wave: the blizzard swirls out over the whole circle (Baroque 3D VFX art), a ring of crystal spikes
        // bursts up through it with glacial bursts along its edge, a flash of cold light, a heavy shake - and anyone
        // inside is hit and thrown out
        IceVfx.BlizzardBurst(feet, radius);
        Vfx.GroundRing(feet, radius, IceVfx.Frost, 0.5f);
        Vfx.GroundRing(feet, radius * 0.55f, Color.Lerp(IceVfx.Frost, Color.white, 0.5f), 0.3f, "glow");
        IceVfx.Flash(feet + Vector3.up * 2f, radius * 2.5f, 6f, 0.7f);
        CameraShake.Shake(0.45f, 0.3f);
        for (int i = 0; i < 18; i++)
        {
            float angle = i * 20f + Random.Range(-6f, 6f);
            float along = Random.Range(0.5f, 0.95f) * radius;
            Vector3 at = feet + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * along;
            GameObject spike = DungeonGenerator.Crystal6(null, at, Quaternion.Euler(0f, angle, 0f) * Quaternion.Euler(Random.Range(15f, 35f), 0f, 0f),
                Random.Range(0.25f, 0.4f), Random.Range(1.2f, 2.2f), ice);
            StartCoroutine(Erupt(spike.transform, 0.1f, Random.Range(0.5f, 0.8f)));
            Destroy(spike, 1.5f);
        }
        for (int i = 0; i < 6; i++)
        {
            float angle = i * 60f + Random.Range(-10f, 10f);
            IceVfx.IceEruption(feet + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius * 0.75f, 2.8f);
        }
        if (playerStats != null && FlatDistance(player.position, transform.position) <= radius)
        {
            playerStats.TakeDamage(attackDamage, this);
            Vector3 away = Flat(player.position - transform.position).normalized;
            player.GetComponent<PlayerMovement>()?.Push(away * 8f);
        }
        yield return new WaitForSeconds(0.35f);
        BodyPoseOff(2.5f);   // it straightens slowly out of the crouch
        yield return new WaitForSeconds(0.35f);
    }

    private static readonly Color SoulColor = new Color(0.45f, 1f, 0.75f);
    private static readonly Color DeathColor = new Color(0.4f, 0.2f, 0.6f);

    // ── Soul lance (Hel): she gathers a spear of soul-fire in her hands and hurls it in a long straight line,
    // three times - the first slow and telegraphed, the next two quickly re-aimed ──
    private IEnumerator SoulLance()
    {
        float size = transform.localScale.x;
        float damage = attackDamage * 0.7f;
        const float length = 18f, width = 1.6f, speed = 34f;
        Material soul = Mats.Get("hel_soul_lance", SoulColor, 0.6f, SoulColor * 2.2f);
        for (int volley = 0; volley < 3 && !dead; volley++)
        {
            FacePlayer();
            if (volley == 0 && animator != null)
                animator.SetTrigger(RoarHash);
            float windup = volley == 0 ? 1.1f : 0.6f;
            Vector3 dir = Facing;
            Vector3 from = Ground(transform.position) + dir * 0.6f * size;
            warning = Telegraph.Line(from, dir, length, width, windup);
            Vector3 hand = transform.position + Vector3.up * 1.15f * size + dir * 0.55f * size;
            GameObject orb = Mats.Shape(PrimitiveType.Sphere, null, hand, Vector3.one * 0.15f, soul);
            areaMarks.Add(orb);
            for (float t = 0f; t < windup; t += Time.deltaTime)
            {
                orb.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, 0.7f, t / windup) * (1f + Mathf.Sin(t * 40f) * 0.08f);
                yield return null;
            }
            areaMarks.Remove(orb);
            Destroy(orb);
            if (warning != null)
                Destroy(warning);
            if (dead)
                yield break;

            // the lance flies, dipping to chest height as it goes
            GameObject lance = Mats.Shape(PrimitiveType.Capsule, null, hand, new Vector3(0.3f, 1.3f, 0.3f), soul);
            lance.transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);
            areaMarks.Add(lance);
            Vector3 low = Vector3.up * 1f;
            bool hit = false;
            for (float d = 0f; d < length; d += speed * Time.deltaTime)
            {
                lance.transform.position = Vector3.Lerp(hand, from + low, Mathf.Clamp01(d / 4f)) + dir * d;
                if (!hit && playerStats != null && InStrip(from, dir, d, width / 2f))
                {
                    hit = true;
                    playerStats.TakeDamage(damage, this);
                    player.GetComponent<PlayerMovement>()?.Push(dir * 5f);
                }
                yield return null;
            }
            Shockwave.Spawn(from + dir * length, 1.2f, SoulColor, 6);
            areaMarks.Remove(lance);
            Destroy(lance);
            yield return new WaitForSeconds(0.1f);
        }
        yield return new WaitForSeconds(0.4f);
    }

    // ── Death aura (Hel): she raises the dead around her - souls stream in from a vast circle - then it bursts
    // outward, and a pool of death lingers at her feet for a few seconds ──
    private IEnumerator DeathAura()
    {
        float size = transform.localScale.x;
        float radius = 5f * size;     // ~9 m on Hel
        const float windup = 2.2f;
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(RoarHash);
        warning = Telegraph.Circle(transform.position, radius, windup);
        Material soul = Mats.Get("hel_soul_wisp", SoulColor, 0.5f, SoulColor * 1.6f);
        Material dark = Mats.Get("hel_death_pool", DeathColor, 0.2f, DeathColor * 0.9f);
        bool slammed = false;
        for (float t = 0f; t < windup; t += 0.12f)
        {
            if (!slammed && t >= windup - attackWindup && animator != null)
            {
                slammed = true;
                animator.SetTrigger(AttackHash);   // the slam lands as the circle fills
            }
            // a wisp rising from the edge of the circle
            Vector3 at = transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * radius * Random.Range(0.6f, 0.95f);
            GameObject wisp = Mats.Shape(PrimitiveType.Sphere, null, at + Vector3.up * 0.4f, Vector3.one * Random.Range(0.25f, 0.45f), soul);
            Destroy(wisp, 0.45f);
            yield return new WaitForSeconds(0.12f);
        }
        if (warning != null)
            Destroy(warning);
        if (dead)
            yield break;

        // the burst
        Shockwave.Spawn(transform.position, radius, DeathColor, 36);
        Shockwave.Spawn(transform.position, radius * 0.6f, SoulColor, 18);
        CameraShake.Shake(0.6f, 0.3f);
        if (playerStats != null && FlatDistance(player.position, transform.position) <= radius)
            playerStats.TakeDamage(attackDamage * 1.2f, this);

        // the pool of death that lingers at her feet
        float inner = 2.2f * size;
        GameObject pool = Mats.Shape(PrimitiveType.Cylinder, null, Ground(transform.position) + Vector3.up * 0.03f, new Vector3(inner * 2f, 0.02f, inner * 2f), dark);
        areaMarks.Add(pool);
        for (float t = 0f; t < 3f && !dead; t += 0.5f)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 at = transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * inner * Random.Range(0.2f, 0.9f);
                GameObject wisp = Mats.Shape(PrimitiveType.Sphere, null, at + Vector3.up * 0.3f, Vector3.one * 0.35f, soul);
                Destroy(wisp, 0.4f);
            }
            if (playerStats != null && FlatDistance(player.position, transform.position) <= inner)
                playerStats.TakeDamage(attackDamage * 0.15f, this);
            yield return new WaitForSeconds(0.5f);
        }
        ClearAreaMarks();
    }

    private bool InStrip(Vector3 from, Vector3 dir, float length, float halfWidth)
    {
        if (player == null)
            return false;
        Vector3 d = Flat(player.position - from);
        float along = Vector3.Dot(d, dir);
        return along > -0.5f && along < length && Mathf.Abs(Vector3.Dot(d, Vector3.Cross(dir, Vector3.up))) < halfWidth;
    }

    private Vector3 Ground(Vector3 p)
    {
        p.y = transform.position.y;
        return p;
    }

    private int swingStyle;   // a giant alternates its blows

    // ── How its attacks feel (EnemyFeel): its own tint, and where a blow comes from ──
    protected virtual Color FeelColor => EnemyFeel.Warm;
    protected float FeelSize => Mathf.Max(0.5f, transform.localScale.x);
    protected Vector3 WeaponPoint => transform.position + (Vector3.up * 1.3f + Facing * 0.55f) * FeelSize;

    private IEnumerator Attack()
    {
        bool heavy = Heavy;
        EnemyFeel.Windup(transform, WeaponPoint, FeelColor, FeelSize);
        int style = heavy ? swingStyle++ % 2 : 0;
        if (animator != null)
        {
            if (heavy)
                PlayMove(style == 0 ? "Ground" : "Cast", AttackHash);   // (not its roar-like attack clip)
            else
                animator.SetTrigger(AttackHash);
        }
        // a giant's blow: two fists raised high and hammered down, or a backhand swung across from its far side
        if (style == 0)
            BodyPoseTo(new Vector3(-0.2f, 0.95f, 0.1f), new Vector3(0.2f, 0.95f, 0.1f), -14f, 0f, -14f, 5f);
        else
            BodyPoseTo(new Vector3(-0.75f, -0.1f, -0.15f), new Vector3(-0.55f, 0.35f, 0.55f), 4f, -38f, -6f, 5f);

        Vector3 slamCenter = transform.position + Facing * slamForward;
        if (slamRadius > 0f)
            warning = Telegraph.Circle(slamCenter, slamRadius, attackWindup);

        yield return new WaitForSeconds(Mathf.Max(0f, attackWindup - 0.2f));
        if (style == 0)
            BodyPoseTo(new Vector3(-0.15f, -0.6f, 0.75f), new Vector3(0.15f, -0.6f, 0.75f), 38f, 0f, 16f, 24f);
        else
            BodyPoseTo(new Vector3(0.25f, -0.35f, 0.6f), new Vector3(0.95f, 0.05f, 0.35f), 16f, 38f, 4f, 22f);
        yield return new WaitForSeconds(Mathf.Min(attackWindup, 0.2f));
        if (warning != null)
            Destroy(warning);

        if (!dead && playerStats != null)
        {
            bool hit = slamRadius > 0f
                ? FlatDistance(player.position, slamCenter) <= slamRadius
                : InFront(attackReach, 70f);
            if (hit)
                playerStats.TakeDamage(attackDamage, this);
            if (slamRadius > 0f)
                EnemyFeel.Impact(slamCenter, slamRadius, FeelColor, 0.25f);
            else
                EnemyFeel.Swing(transform.position + Vector3.up * 0.9f * FeelSize, Facing, attackReach, FeelColor);
        }

        float rest = Mathf.Max(0f, attackDuration - attackWindup);
        yield return new WaitForSeconds(Mathf.Min(rest, 0.3f));
        BodyPoseOff(3f);
        yield return new WaitForSeconds(Mathf.Max(0f, rest - 0.3f));
    }

    // ── Helpers for attack patterns ──

    // Is the player within reach and inside the arc in front?
    protected bool InFront(float reach, float halfArc)
    {
        if (player == null)
            return false;
        return FlatDistance(player.position, transform.position) <= reach
               && Vector3.Angle(Facing, Flat(player.position - transform.position)) < halfArc;
    }

    // Play an attack animation, land a hit in front after the windup, then recover
    protected IEnumerator MeleeStrike(int trigger, float windup, float recover, float reach, float halfArc, float damage)
    {
        FacePlayer();
        if (animator != null)
            animator.SetTrigger(trigger);
        EnemyFeel.Windup(transform, WeaponPoint, FeelColor, FeelSize);
        yield return new WaitForSeconds(windup);
        if (!dead)
            EnemyFeel.Swing(transform.position + Vector3.up * 0.9f * FeelSize, Facing, reach, FeelColor);
        if (!dead && playerStats != null && InFront(reach, halfArc))
            playerStats.TakeDamage(damage, this);
        yield return new WaitForSeconds(recover);
    }

    // Charge in a straight line, hurting (and shoving) the player on contact once
    protected IEnumerator Lunge(Vector3 direction, float distance, float seconds, float damage, float hitRadius, float shove)
    {
        direction = Flat(direction).normalized;
        transform.rotation = Quaternion.LookRotation(direction);
        actionMove = direction * (distance / seconds);
        if (distance > 1f)
            EnemyFeel.Lunge(transform.position, direction, FeelColor, FeelSize);
        bool landed = false;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            if (!landed && playerStats != null && FlatDistance(player.position, transform.position) <= hitRadius)
            {
                landed = true;
                playerStats.TakeDamage(damage, this);
                if (shove > 0f)
                    player.GetComponent<PlayerMovement>()?.Push(direction * shove);
            }
            yield return null;
        }
        actionMove = Vector3.zero;
    }

    // Walks (the locomotion clip plays from the ground speed) at `velocity` for `seconds`, turning the way it goes
    protected IEnumerator Stride(Vector3 velocity, float seconds)
    {
        velocity = Flat(velocity);
        for (float t = 0f; t < seconds && !dead; t += Time.deltaTime)
        {
            actionMove = velocity;
            if (velocity.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(velocity), 300f * Time.deltaTime);
            yield return null;
        }
        actionMove = Vector3.zero;
    }

    // Plays a named state if its controller has one (the fuller move sets: Cast, Ground, Chant, Combo, Heavy...),
    // otherwise the given trigger, so a monster without it still swings
    protected void PlayMove(string state, int fallbackTrigger)
    {
        if (animator == null)
            return;
        int hash = Animator.StringToHash("Base Layer." + state);
        if (animator.HasState(0, hash))
            animator.CrossFadeInFixedTime(hash, Heavy ? 0.3f : 0.15f);   // (a giant's body flows into the move)
        else
            animator.SetTrigger(fallbackTrigger);
    }

    // Swings its attack clip at the player while pressing in at `drift` (m/s), for `seconds` (`state`: a fuller move to
    // play instead, when its controller has it)
    protected IEnumerator SwingIn(float drift, float seconds, string state = null)
    {
        FacePlayer();
        if (state != null)
            PlayMove(state, AttackHash);
        else if (animator != null)
            animator.SetTrigger(AttackHash);
        EnemyFeel.Windup(transform, WeaponPoint, FeelColor, FeelSize);
        bool swung = false;
        for (float t = 0f; t < seconds && !dead; t += Time.deltaTime)
        {
            actionMove = Facing * drift;
            // the arc as the swing comes through, a little past the middle of it
            if (!swung && t >= seconds * 0.45f)
            {
                swung = true;
                EnemyFeel.Swing(transform.position + Vector3.up * 0.9f * FeelSize, Facing, Mathf.Max(2f, attackReach), FeelColor);
            }
            yield return null;
        }
        actionMove = Vector3.zero;
    }

    // A step to one side of the player, round them (side +1 / -1), at walking pace times `pace`
    protected Vector3 Circling(float side, float pace)
    {
        Vector3 to = player != null ? Flat(player.position - transform.position).normalized : transform.forward;
        return Quaternion.Euler(0f, 60f * side, 0f) * to * moveSpeed * transform.localScale.x * 0.6f * pace;
    }

    protected void FacePlayer()
    {
        if (player == null)
            return;
        Vector3 to = Flat(player.position - transform.position);
        if (to.sqrMagnitude < 0.01f)
            return;
        // a giant swings round over a moment (Update turns it); others snap
        if (Heavy && !BossCutscene.Playing)
            faceGoal = Quaternion.LookRotation(to).eulerAngles.y;
        else
            transform.rotation = Quaternion.LookRotation(to);
    }

    // The swing is cut off before it lands; a longer wait before the next one
    public override void Interrupt()
    {
        if (dead || attackRoutine == null)
            return;
        StopCoroutine(attackRoutine);
        EndAction();
        if (animator != null)
            animator.CrossFade("Locomotion", Heavy ? 0.25f : 0.1f);
        cooldown = attackCooldown + 1f;
    }

    // A short war cry as it engages: its roar clip if it has one, the war-cry ring at its feet, a light shake
    private bool criedOut;

    private IEnumerator BattleCry()
    {
        yield return new WaitForSeconds(Random.Range(0f, 0.35f));
        FacePlayer();
        if (animator != null)
            foreach (AnimatorControllerParameter p in animator.parameters)
                if (p.nameHash == RoarHash && p.type == AnimatorControllerParameterType.Trigger)
                {
                    animator.SetTrigger(RoarHash);
                    break;
                }
        yield return new WaitForSeconds(0.3f);
        if (!dead)
            EnemyFeel.Roar(transform, FeelColor, 3.2f * FeelSize, 0.16f);
        yield return new WaitForSeconds(0.45f);
    }

    private IEnumerator Roar()
    {
        busy = true;
        if (animator != null)
        {
            if (Heavy)
                PlayMove("Curse", RoarHash);   // a giant summons its kin with raised arms rather than a roar
            else
                animator.SetTrigger(RoarHash);
        }
        BodyPoseTo(new Vector3(-0.7f, 0.7f, 0.35f), new Vector3(0.7f, 0.7f, 0.35f), -18f, 0f, -24f, 4f);
        yield return new WaitForSeconds(0.6f);
        Roared?.Invoke(this);
        EnemyFeel.Roar(transform, FeelColor, 5f * FeelSize, IsBoss ? 0.4f : 0.25f);
        yield return new WaitForSeconds(Mathf.Max(0f, roarDuration - 0.6f) * 0.6f);
        BodyPoseOff(3f);
        yield return new WaitForSeconds(Mathf.Max(0f, roarDuration - 0.6f) * 0.4f);
        busy = false;
    }

    public override void TakeHit(Vector3 from, int damage)
    {
        if (dead)
            return;

        damage = Mitigate(damage);
        health -= Mathf.Max(1, damage);
        knockback = Flat(transform.position - from).normalized * knockbackSpeed;
        StartCoroutine(Flash());

        if (health <= 0)
        {
            Die();
            return;
        }

        // 페이즈 2: a boss at 30% health transforms, in a cutscene
        if (IsBoss && !awakened && health <= maxHealth * BossAwakening.Threshold)
        {
            Awaken();
            return;
        }

        if (roarsDone < roarAtHealth.Length && health <= maxHealth * roarAtHealth[roarsDone] && !busy)
        {
            roarsDone++;
            StartCoroutine(Roar());
        }
        else if (!busy && !IsBoss)
            Flinch();
    }

    // Recoil from a blow (not mid-swing: an attack carries on through it)
    private void Flinch()
    {
        if (animator == null || Time.time < nextFlinch || !animator.HasState(0, HitState))
            return;
        animator.CrossFadeInFixedTime(HitState, 0.05f);
        flinchUntil = Time.time + 0.3f;
        nextFlinch = Time.time + 1.2f;
    }

    // Topples backwards, lies there, then gets up. Bosses only stagger.
    public override void Knockdown(float seconds)
    {
        if (dead || BossCutscene.Playing)
            return;
        StopAllCoroutines();
        EndAction();
        ClearFlash();
        StartCoroutine(KnockedDown(IsBoss ? 0.5f : seconds, !IsBoss));
    }

    private IEnumerator KnockedDown(float seconds, bool fall)
    {
        busy = true;
        if (animator != null)
            animator.SetFloat(SpeedHash, 0f);

        // Animated: thrown down (the death fall, cut short), then up again with the get-up clip
        if (animator != null && animator.HasState(0, FallState) && animator.HasState(0, GetUpState) && animator.HasState(0, HitState))
        {
            if (fall)
            {
                animator.CrossFadeInFixedTime(FallState, 0.08f);
                yield return new WaitForSeconds(Mathf.Max(0.8f, seconds - 0.8f));
                animator.CrossFadeInFixedTime(GetUpState, 0.25f, 0, GetUpFrom);
                yield return new WaitForSeconds(GetUpTime);
            }
            else
            {
                animator.CrossFadeInFixedTime(HitState, 0.05f);
                yield return new WaitForSeconds(seconds);
            }
            cooldown = 0.6f;
            busy = false;
            yield break;
        }

        Transform model = animator != null ? animator.transform : null;
        Quaternion upright = model != null ? model.localRotation : Quaternion.identity;
        Quaternion fallen = upright * Quaternion.Euler(-80f, 0f, 0f);

        if (fall && model != null)
            for (float t = 0f; t < 0.2f; t += Time.deltaTime)
            {
                model.localRotation = Quaternion.Slerp(upright, fallen, t / 0.2f);
                yield return null;
            }
        yield return new WaitForSeconds(seconds);
        if (fall && model != null)
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                model.localRotation = Quaternion.Slerp(fallen, upright, t / 0.35f);
                yield return null;
            }
        if (model != null)
            model.localRotation = upright;
        cooldown = 0.6f;
        busy = false;
    }

    // ── Boss awakening ──

    private void Awaken()
    {
        awakened = true;
        // Whatever it was doing (a swing, a roar, a hit flash) gives way to the transformation
        StopAllCoroutines();
        ClearFlash();
        EndAction();
        busy = true;
        knockback = Vector3.zero;
        BossCutscene.Play(transform, controller.height * transform.localScale.y, AwakenScene());
    }

    // Crouched, it draws power in from all around; then it rises with a roar, bursts with light and grows
    private IEnumerator AwakenScene()
    {
        var (title, subtitle, color) = AwakenedTitle();
        float h = controller.height * transform.localScale.y;
        Vector3 chest = transform.position + Vector3.up * h * 0.5f;
        if (animator != null)
        {
            animator.SetFloat(SpeedHash, 0f);
            if (animator.HasState(0, TransformState))
                animator.CrossFadeInFixedTime(TransformState, 0.25f);
        }

        const float gather = 1.7f;
        Vfx.Gather(chest, h * 1.5f, color, gather, 110f);
        VfxPiece circle = Vfx.Decal(transform.position + Vector3.up * 0.05f, h, color, "runes", 140f);
        Vfx.Dust(transform.position, h * 0.5f, 8);
        float rumble = 0f;
        for (float t = 0f; t < gather; t += Time.deltaTime)
        {
            float k = t / gather;
            circle.Set(color * (1f + k), Mathf.Lerp(h, h * 2.4f, k), Mathf.Lerp(0.3f, 1f, k));
            if ((rumble -= Time.deltaTime) <= 0f)
            {
                rumble = 0.25f;
                CameraShake.Shake(0.25f, 0.03f + 0.07f * k);
            }
            yield return null;
        }
        Destroy(circle.gameObject);

        // The burst
        BossCutscene.Flash(0.85f);
        BossCutscene.Dolly(0.8f);
        BossCutscene.Title(title, subtitle, color);
        CameraShake.Shake(0.7f, 0.35f);
        Shockwave.Spawn(transform.position, h * 2.6f, color, 30);
        Vfx.GroundRing(transform.position, h * 3.2f, color, 0.9f, "runes");
        Vfx.Flash(chest, h * 2.4f, color, 0.45f);
        Vfx.Burst(chest, color, 45, 11f, 0.26f, 0.4f);
        Vfx.Dust(transform.position, h * 0.9f, 22);
        Vfx.Aura(transform, h, color);
        SetBaseTint(Color.Lerp(BaseTint, color, 0.2f));

        // Grows a metre taller into its new strength: attack +30%, defence +30% (BossAwakening's numbers)
        float standing = Mathf.Max(1f, BossCutscene.HeightOf(transform));
        Vector3 from = transform.localScale, to = from * ((standing + BossAwakening.ExtraHeight) / standing);
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            transform.localScale = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / 0.8f));
            yield return null;
        }
        transform.localScale = to;
        PhaseDamageMultiplier = BossAwakening.DamageBonus;
        IncomingDamageMultiplier = BossAwakening.DefenseBonus;
        // attack speed and move speed +30%: the clips play faster, and the swing timings shrink to match them
        float haste = BossAwakening.SpeedBonus;
        moveSpeed *= haste;
        attackWindup /= haste;
        attackDuration /= haste;
        attackCooldown /= haste;
        if (animator != null)
            animator.speed *= haste;
        DisplayName = title;

        yield return new WaitForSeconds(1.8f);
        cooldown = 0.4f;
        busy = false;
    }

    // What each realm's boss becomes
    private (string title, string subtitle, Color color) AwakenedTitle()
    {
        string n = DisplayName ?? "";
        if (n.Contains("드라우그")) return ("무덤의 왕 · 망자의 분노", "식어 버린 심장에 저승의 냉기가 깃든다", new Color(0.5f, 0.85f, 1f));
        if (n.Contains("흐룽니르")) return ("흐룽니르 · 서리의 각성", "돌의 심장이 얼어붙은 분노로 고동친다", new Color(0.6f, 0.9f, 1f));
        if (n.Contains("수르트")) return ("수르트 · 무스펠의 불꽃", "세계를 태울 불길이 깨어난다", new Color(1f, 0.45f, 0.15f));
        if (n.Contains("모드구드")) return ("모드구드 · 헬의 문", "망자의 다리를 지키는 자가 본모습을 드러낸다", new Color(0.5f, 1f, 0.8f));
        if (n.Contains("헬")) return ("죽음의 여왕 헬 · 망자의 군주", "니플헤임의 모든 망자가 그녀의 부름에 일어선다", new Color(0.45f, 1f, 0.75f));
        return (n + " · 각성", "분노한 힘이 깨어난다", new Color(1f, 0.45f, 0.3f));
    }

    private IEnumerator Flash()
    {
        FlashTint(hitTint);
        yield return new WaitForSeconds(0.12f);
        ClearFlash();
    }

    private void Die()
    {
        dead = true;
        StopAllCoroutines();
        EndAction();
        ClearFlash();
        controller.enabled = false;
        if (animator != null)
        {
            animator.SetFloat(SpeedHash, 0f);
            animator.SetTrigger(DieHash);
        }
        RaiseDied();
        Destroy(gameObject, corpseTime);
    }

    protected static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
