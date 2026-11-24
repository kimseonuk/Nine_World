using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 2.5f;
    [SerializeField] private float runSpeed = 5f;
    [SerializeField] private float rotationSpeed = 720f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Dash (Space)")]
    [SerializeField] private float dashDistance = 4f;
    [SerializeField] private float dashTime = 0.18f;
    [SerializeField] private float dashCooldown = 0.8f;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private const float MinTurnSpeed = 2400f;   // degrees a second
    // Faint on purpose: the dash's wind should read at a glance without hiding the player or enemies
    private static readonly Color DashColor = new Color(0.7f, 0.85f, 1f, 0.45f);

    private CharacterController controller;
    private Animator animator;
    private PlayerMiner miner;
    private DualBlades blades;      // the twin blades: their skills hold the player still or carry them, Space is their dodge
    private float verticalVelocity;
    private Vector3 push;
    private float dashLeft;       // seconds of the current dash still to go
    private Vector3 dashDirection;
    private float nextDashTime;

    public bool IsDashing => dashLeft > 0f;
    public float DashCooldown => dashCooldown;
    public float DashCooldownLeft => Mathf.Max(0f, nextDashTime - Time.time);

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        FeetOnFloor.Attach(gameObject);   // (feet on the floor, not hovering the controller's skin width above it)
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        miner = GetComponent<PlayerMiner>();
        blades = GetComponent<DualBlades>();
    }

    private void Update()
    {
        // Stand still while swinging the pickaxe
        // ...and while a menu (camp building, etc.) has the controls
        bool bladesActive = blades != null && blades.IsActive;
        // (the greatsword's 휠윈드 walks while it spins)
        bool whirling = miner != null && miner.Whirling;
        // (a weapon from the armory says for itself when it holds the player and whether Space is its own)
        ArmoryWeapon armory = ArmoryWeapon.Current != null && ArmoryWeapon.Current.IsActive ? ArmoryWeapon.Current : null;
        bool held = (miner != null && miner.IsSwinging && !whirling) || (bladesActive && blades.Rooted) || (armory != null && armory.Rooted);
        // (held fast by a boss's chains: no walking, no dash)
        bool rooted = PlayerEffects.Rooted;
        Vector2 input = held || rooted || !GameManager.IsFreeRoam ? Vector2.zero : ReadWasd();
        Vector3 move = new Vector3(input.x, 0f, input.y);
        if (move.sqrMagnitude > 1f)
            move.Normalize();

        // First person: WASD is relative to where the player looks (mouse turns the player)
        bool firstPerson = CameraModeController.FirstPersonActive;
        if (firstPerson)
            move = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * move;

        // Space dashes the way the player is moving (or facing when standing still).
        // While charging Space is the greatsword's 돌진 instead.
        // With the twin blades Space is their dodge (DualBlades) instead
        // (the greatsword learns its dash in the skill tree)
        bool canDash = GameManager.IsFreeRoam && !rooted && !bladesActive && (miner == null || (!miner.IsSwinging && !miner.IsCharging))
            && (miner == null || (armory != null ? armory.CanDash : miner.CanCharge ? GreatswordSkills.Has(GreatswordSkills.Dash) : StormSkills.Has(StormSkills.Dash)))
            && (armory == null || !armory.OwnsSpace);   // (and the plain fighter learns it in the 번개 망치 tree)
        if (canDash && !IsDashing && Time.time >= nextDashTime && DashPressed())
            StartDash(move.sqrMagnitude > 0.001f ? move.normalized : transform.forward);
        if (IsDashing)
        {
            dashLeft -= Time.deltaTime;
            move = dashDirection;
        }

        bool running = IsRunHeld() || IsDashing;
        float speed = (running ? runSpeed : walkSpeed) * RunState.Stats.moveSpeedMultiplier * MetaProgress.MoveSpeedMultiplier * DebugCheats.MoveSpeedMultiplier * PlayerEffects.Slow * PlayerEffects.Bind
                      * SkillSystem.MoveBuff
                      * (bladesActive ? blades.MoveMultiplier : 1f) * (whirling ? miner.WhirlwindMoveSpeed : 1f)
                      * (armory != null ? armory.MoveMultiplier : 1f);
        if (IsDashing)
            speed = dashDistance * (armory != null ? armory.DashScale : miner != null && !miner.CanCharge ? StormSkills.DashScale : 1f) / dashTime;
        if (animator != null)
            animator.speed = PlayerEffects.Slow;

        // Animator Speed: 0 = idle, 0.5 = walk, 1 = run
        if (animator != null)
            animator.SetFloat(SpeedHash, move.magnitude * (running ? 1f : 0.5f), 0.1f, Time.deltaTime);

        // Third person: face exactly the way the keys point — turning round in a blink (a 180 in under a tenth of a
        // second), snapping at once for a dash; only the last few degrees ease in so it never jitters
        if (!firstPerson && move.sqrMagnitude > 0.001f)
        {
            Quaternion target = Quaternion.LookRotation(move, Vector3.up);
            float turn = Mathf.Max(rotationSpeed, MinTurnSpeed) * Time.deltaTime;
            transform.rotation = IsDashing ? target : Quaternion.RotateTowards(transform.rotation, target, turn);
        }

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        // Shoves from enemies (a shield bash) slide the player and fade out quickly
        push = Vector3.Lerp(push, Vector3.zero, 8f * Time.deltaTime);

        Vector3 velocity = move * speed + push + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    public void Push(Vector3 velocity)
    {
        velocity.y = 0f;
        push += velocity;
    }

    public void SetAnimator(Animator characterAnimator)
    {
        animator = characterAnimator;
    }

    private void StartDash(Vector3 direction)
    {
        dashDirection = direction;
        dashLeft = dashTime;
        nextDashTime = Time.time + dashCooldown;
        // A ring of wind on the floor where it starts and two thin streaks of wind low behind the legs,
        // kept below the waist so they never cover the body
        Vfx.GroundRing(transform.position - Vector3.up * 1f, 0.6f, DashColor, 0.3f, "windpuff");
        foreach (var (height, side) in new[] { (0.25f, -0.22f), (0.55f, 0.22f) })
        {
            var streak = new GameObject("DashStreak");
            streak.transform.SetParent(transform, false);
            streak.transform.localPosition = new Vector3(side, height - 1f, 0f);
            Vfx.Trail(streak, DashColor, 0.16f, 0.18f, "wind");
            Destroy(streak, dashTime + 0.25f);
        }
    }

    private static bool DashPressed()
    {
        Keyboard keyboard = Keyboard.current;
        return SkillSystem.DodgeDown(keyboard);   // (costs 기력)
    }

    private static bool IsRunHeld()
    {
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
    }

    private static Vector2 ReadWasd()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return Vector2.zero;

        float x = 0f;
        float y = 0f;
        if (keyboard.aKey.isPressed) x -= 1f;
        if (keyboard.dKey.isPressed) x += 1f;
        if (keyboard.sKey.isPressed) y -= 1f;
        if (keyboard.wKey.isPressed) y += 1f;
        return new Vector2(x, y);
    }
}
