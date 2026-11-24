using UnityEngine;

// Holds the hammer by the end of its handle and drives the attack swing procedurally.
// Resting: the hammer leans over the right shoulder, gripped at the pommel end, following the animated hand.
// Attacking: the hammer is pulled back to the right, then swept horizontally across the front in a wide arc.
// The right arm is bent with a two-bone IK so the hand stays exactly on the handle end, and the spine twists with the swing.
// When the player has an attack clip, the clip drives the body instead and the hammer just sits in the fist.
// Outside the attack the right hand is pulled up in front of the shoulder and closed around the handle,
// so the hammer rides on the shoulder through idle, walk and run instead of hanging off a loose arm.
// During the whirlwind skill the arm is held straight out to the side so the hammer sweeps a wide circle.
public class HammerHold : MonoBehaviour
{
    [Header("Rig")]
    public Transform body;
    public Transform spine;
    public Transform upperArm;
    public Transform foreArm;
    public Transform hand;
    public Transform hammer;
    public PlayerController player;
    [Tooltip("In the full game: the armory's hammer (MjolnirHammer) drives the hold instead of this stage's own player.")]
    public MjolnirHammer wielder;

    // Where the swing, the skills and death are read from: this stage's player, or the armory's hammer
    bool Attacking => player != null ? player.IsAttacking : wielder != null && wielder.IsAttacking;
    float Progress => player != null ? player.AttackProgress : wielder != null ? wielder.AttackProgress : 0f;
    bool UsesClip => player != null ? player.UsesAttackClip : wielder != null && wielder.UsesAttackClip;
    bool Away => skills != null ? skills.HammerAway : wielder != null && wielder.HammerAway;
    bool Dead => health != null ? health.IsDead : wielder != null && wielder.IsDead;
    bool HasSkills => skills != null || wielder != null;
    float Grip => skills != null ? skills.GripWeight : wielder != null ? wielder.GripWeight : 0f;
    float Spin => skills != null ? skills.SpinWeight : wielder != null ? wielder.SpinWeight : 0f;
    Quaternion SpinRot => skills != null ? skills.SpinRotation : wielder != null ? wielder.SpinRotation : Quaternion.identity;

    [Header("Hammer geometry (hammer local space, unscaled)")]
    public Vector3 handleAxis = Vector3.up;      // pommel -> head
    public Vector3 faceNormal = Vector3.forward; // thin axis of the head
    public Vector3 gripPoint = Vector3.zero;     // point near the pommel end

    [Header("Rest pose (body space)")]
    public Vector3 restDirection = new Vector3(0.3f, 0.75f, -0.55f);

    [Header("Swing (fractions of the attack)")]
    [Range(0f, 1f)] public float windUpEnd = 0.3f;
    [Range(0f, 1f)] public float swingEnd = 0.6f;
    [Range(0f, 1f)] public float followEnd = 0.75f;
    public float windUpYaw = 115f;     // to the right and back
    public float followYaw = -125f;    // across to the left
    public float maxSpineTwist = 35f;

    [Header("Held in the fist (hand space), while an attack clip plays")]
    public Vector3 heldDirection = Vector3.right;   // handle direction out of the fist
    public Vector3 heldUp = Vector3.back;           // where the striking face points
    [Range(0f, 0.5f)] public float heldBlendIn = 0.12f;
    [Range(0f, 0.5f)] public float heldBlendOut = 0.2f;

    [Header("Shoulder carry (outside the attack)")]
    [Tooltip("Where the fist sits, in body space relative to the shoulder joint.")]
    public Vector3 carryOffset = new Vector3(-0.1f, -0.3f, 0.32f);
    [Tooltip("How quickly the hammer follows the body; lower values make it lag and sway more.")]
    public float carryFollow = 14f;

    float previewProgress = -1f;
    Vector3 carryDir;
    PlayerSkills skills;
    PlayerHealth health;
    float dropTimer = -1f;   // counts up once the hero dies and lets go of the hammer
    Vector3 dropCenter, dropDir, dropUp, restCenter, restDir;

    [Header("Dropped on death")]
    public float dropTime = 0.45f;
    [Tooltip("Where the hammer lands, in body space from the player's feet.")]
    public Vector3 dropOffset = new Vector3(-0.8f, 0f, 0.1f);

    void Awake()
    {
        if (player != null)
        {
            skills = player.GetComponent<PlayerSkills>();
            health = player.GetComponent<PlayerHealth>();
        }
    }

    // Puts the hammer in flight: its middle at 'center', handle along 'dir', striking face toward 'up'.
    public void PlaceFlying(Vector3 center, Vector3 dir, Vector3 up)
    {
        if (localCenter == null)
        {
            var r = hammer.GetComponentInChildren<Renderer>();
            localCenter = r != null ? hammer.InverseTransformPoint(r.bounds.center) : Vector3.zero;
        }
        Quaternion meshFrame = Quaternion.LookRotation(handleAxis, faceNormal);
        hammer.rotation = Quaternion.LookRotation(dir, up) * Quaternion.Inverse(meshFrame);
        hammer.position = center - hammer.rotation * Vector3.Scale(localCenter.Value, hammer.lossyScale);
    }

    Vector3? localCenter;

    // The hand lets go: the hammer falls beside the hero and lies flat on the floor, leaving both arms to the death clip.
    void Drop()
    {
        if (dropTimer < 0f)
        {
            dropTimer = 0f;
            var r = hammer.GetComponentInChildren<Renderer>();
            dropCenter = r != null ? r.bounds.center : hammer.position;
            dropDir = hammer.rotation * handleAxis;
            dropUp = hammer.rotation * faceNormal;
            // half the head's thickness along its face, so it rests on the floor rather than in it
            float half = 0.15f;
            if (r != null)
            {
                Vector3 e = Vector3.Scale(r.localBounds.extents, r.transform.lossyScale);
                Vector3 n = faceNormal.normalized;
                half = Mathf.Abs(n.x) * e.x + Mathf.Abs(n.y) * e.y + Mathf.Abs(n.z) * e.z;
            }
            Transform feet = player != null ? player.transform : body;
            restCenter = feet.position + feet.rotation * dropOffset;
            restCenter.y = feet.position.y + half;
            // head pointing away to the left, so it lands clear of the body falling to the right
            restDir = Vector3.ProjectOnPlane(feet.forward * 0.4f - feet.right, Vector3.up).normalized;
        }
        dropTimer += Time.deltaTime;
        float t = Mathf.Clamp01(dropTimer / dropTime);
        Vector3 center = Vector3.Lerp(dropCenter, restCenter, t * t);   // accelerates like a fall
        PlaceFlying(center, Vector3.Slerp(dropDir, restDir, Smooth(t)), Vector3.Slerp(dropUp, Vector3.up, Smooth(t)));
    }

    void LateUpdate()
    {
        if (previewProgress >= 0f) return;
        if (wielder != null && !wielder.IsActive) return;   // the armory's hammer, not in hand
        if (Away) return;   // thrown: the arm hangs free and PlayerSkills flies the hammer
        if (Dead) { Drop(); return; }
        dropTimer = -1f;
        float progress = Attacking ? Progress : -1f;
        if (UsesClip) ApplyHeld(progress);
        else Apply(progress);
    }

    // The attack clip swings the arm; blend the hammer from its shoulder rest into the fist and back out.
    void ApplyHeld(float progress)
    {
        float weight = 0f;
        if (progress >= 0f)
            weight = Smooth(progress / Mathf.Max(heldBlendIn, 0.001f)) * Smooth((1f - progress) / Mathf.Max(heldBlendOut, 0.001f));
        // the throw and slam clips swing the hammer in the fist too
        if (HasSkills) weight = Mathf.Max(weight, Smooth(Grip));
        // the skeleton may be turned by the whirlwind on top of the body, so poses are built in that turned frame
        float spin = Spin;
        Quaternion frame = SpinRot * body.rotation;
        Vector3 right = frame * Vector3.right, forward = frame * Vector3.forward;

        float carry = 1f - weight;
        if (carry > 0f)
        {
            // the handle direction trails the body a little, so turns and the run bounce give it some weight
            Vector3 restDir = frame * restDirection.normalized;
            carryDir = carryDir == Vector3.zero || previewProgress >= 0f || spin > 0f
                ? restDir
                : Vector3.Slerp(carryDir, restDir, 1f - Mathf.Exp(-carryFollow * Time.deltaTime));

            // raise the fist in front of the shoulder and turn it so it closes around the handle
            Vector3 target = upperArm.position + frame * carryOffset;
            SolveTwoBone(upperArm, foreArm, hand, Vector3.Lerp(hand.position, target, carry));
            Quaternion fist = Quaternion.LookRotation(carryDir, right) * Quaternion.Inverse(Quaternion.LookRotation(heldDirection, heldUp));
            hand.rotation = Quaternion.Slerp(hand.rotation, fist, carry);
        }

        // whirlwind: arm straight out to the side, hammer level with the striking face leading the spin
        if (spin > 0f)
        {
            float reach = Vector3.Distance(upperArm.position, foreArm.position) + Vector3.Distance(foreArm.position, hand.position);
            Vector3 target = upperArm.position + right * reach * 0.95f + Vector3.down * 0.08f;
            SolveTwoBone(upperArm, foreArm, hand, Vector3.Lerp(hand.position, target, spin));
            Vector3 outward = (right + forward * 0.2f + Vector3.down * 0.12f).normalized;
            Quaternion grip = Quaternion.LookRotation(outward, forward) * Quaternion.Inverse(Quaternion.LookRotation(heldDirection, heldUp));
            hand.rotation = Quaternion.Slerp(hand.rotation, grip, spin);
        }
        PlaceHammer(hand.position, hand.rotation * heldDirection, hand.rotation * heldUp);
    }

    // Edit-mode preview for checking the swing at a given attack progress (0..1), or -1 for the rest pose.
    public void PreviewSwing(float progress)
    {
        previewProgress = progress;
        if (UsesClip) ApplyHeld(progress);
        else Apply(progress);
    }

    void Apply(float progress)
    {
        float weight = 0f;
        float yaw = windUpYaw;
        if (progress >= 0f)
        {
            if (progress < windUpEnd)
            {
                float t = Smooth(progress / windUpEnd);
                weight = t;
                yaw = windUpYaw;
            }
            else if (progress < swingEnd)
            {
                float t = progress - windUpEnd;
                t = EaseInOutCubic(t / (swingEnd - windUpEnd));
                weight = 1f;
                yaw = Mathf.Lerp(windUpYaw, followYaw * 0.85f, t);
            }
            else if (progress < followEnd)
            {
                float t = Smooth((progress - swingEnd) / (followEnd - swingEnd));
                weight = 1f;
                yaw = Mathf.Lerp(followYaw * 0.85f, followYaw, t);
            }
            else
            {
                weight = 1f - Smooth((progress - followEnd) / (1f - followEnd));
                yaw = followYaw;
            }
        }

        if (weight <= 0f)
        {
            PlaceHammer(hand.position, body.rotation * restDirection.normalized, body.right);
            return;
        }

        // Swing direction in the horizontal plane, slightly raised on the wind-up and slightly lowered through the hit.
        float pitch = Mathf.Lerp(12f, -8f, Mathf.InverseLerp(windUpYaw, followYaw, yaw));
        Vector3 swingDir = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, body.right) * body.forward;

        // twist the upper body toward the swing
        if (spine != null)
            spine.rotation = Quaternion.AngleAxis(Mathf.Clamp(yaw * 0.4f, -maxSpineTwist, maxSpineTwist) * weight, Vector3.up) * spine.rotation;

        float armLength = Vector3.Distance(upperArm.position, foreArm.position) + Vector3.Distance(foreArm.position, hand.position);
        Vector3 pivot = upperArm.position + Vector3.down * 0.1f;
        Vector3 ikTarget = pivot + swingDir * armLength * 0.9f;

        Vector3 animatedHand = hand.position;
        SolveTwoBone(upperArm, foreArm, hand, Vector3.Lerp(animatedHand, ikTarget, weight));

        Vector3 restDir = body.rotation * restDirection.normalized;
        Vector3 dir = Vector3.Slerp(restDir, swingDir, weight);
        PlaceHammer(hand.position, dir, Vector3.Slerp(body.right, Vector3.up, weight));
    }

    // Rotate the hammer so the handle points along 'dir' and its head faces 'up', with the grip point at 'gripWorld'.
    void PlaceHammer(Vector3 gripWorld, Vector3 dir, Vector3 up)
    {
        Quaternion meshFrame = Quaternion.LookRotation(handleAxis, faceNormal);
        Quaternion worldFrame = Quaternion.LookRotation(dir, up);
        hammer.rotation = worldFrame * Quaternion.Inverse(meshFrame);
        hammer.position = gripWorld - hammer.rotation * Vector3.Scale(gripPoint, hammer.lossyScale);
    }

    // Analytic two-bone IK keeping the current bend plane.
    static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target)
    {
        Vector3 aPos = a.position, bPos = b.position, cPos = c.position;
        float lab = Vector3.Distance(aPos, bPos);
        float lcb = Vector3.Distance(bPos, cPos);
        float lat = Mathf.Clamp(Vector3.Distance(aPos, target), 0.01f, lab + lcb - 0.001f);

        float acAb0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((cPos - aPos).normalized, (bPos - aPos).normalized), -1f, 1f));
        float baBc0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((aPos - bPos).normalized, (cPos - bPos).normalized), -1f, 1f));
        float acAb1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float baBc1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

        Vector3 axis = Vector3.Cross(cPos - aPos, bPos - aPos);
        if (axis.sqrMagnitude < 1e-6f) axis = Vector3.Cross(cPos - aPos, Vector3.down);
        axis.Normalize();

        a.rotation = Quaternion.AngleAxis((acAb1 - acAb0) * Mathf.Rad2Deg, axis) * a.rotation;
        b.rotation = Quaternion.AngleAxis((baBc1 - baBc0) * Mathf.Rad2Deg, axis) * b.rotation;
        a.rotation = Quaternion.FromToRotation(c.position - a.position, target - a.position) * a.rotation;
    }

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    static float EaseInOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }
}
