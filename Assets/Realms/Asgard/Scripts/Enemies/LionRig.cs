using UnityEngine;

// Gulltoppr's bones (added to the VARCO mesh in the editor, see the skinning on Heimdall.prefab) and the motion
// that drives them: a rotary gallop when running (each leg swings and folds at the knee in turn, the back rocks,
// the body bounds, the head nods against it, the tail streams out, dust kicks up at every footfall and the golden
// mane sheds sparks), leaning into turns; a living stand when still (breathing, the head following the player,
// the tail swishing, now and then a paw stamp, a shake of the mane, a snort or a roar).
public class LionRig : MonoBehaviour
{
    public Transform body, chest, hips, head, tail;
    public Transform[] upperLegs = new Transform[4];   // front left, front right, hind left, hind right
    public Transform[] lowerLegs = new Transform[4];

    [SerializeField] private float swing = 34f;        // degrees each leg reaches forward and back at a full gallop
    [SerializeField] private float kneeFold = 60f;     // how far a leg folds as it is carried forward
    [SerializeField] private float backRock = 7f;      // chest and hips pitching against each other
    [SerializeField] private float headNod = 9f;
    [SerializeField] private float bound = 0.08f;      // extra rise of the body in each bound (metres), on top of the whole lion's bob
    [SerializeField] private float lean = 9f;          // most it banks into a turn
    [SerializeField] private float lookRange = 40f;    // how far the head turns to follow the player

    // Rotary gallop: the legs land one after another (hind, hind, fore, fore), not in pairs
    private static readonly float[] LegPhase = { 0.55f, 0.7f, 0f, 0.15f };
    private static readonly Color Gold = new Color(1f, 0.82f, 0.4f);

    private Quaternion[] upperRest, lowerRest;
    private Quaternion chestRest, hipsRest, headRest, tailRest;
    private Vector3 bodyRest;
    private bool captured;

    private Transform player;
    private float lastYaw, turnLean, headYaw;
    private float[] lastSin = new float[4];
    private ParticleSystem maneSparks;

    // Idle flourishes: what it is doing, since when, and when the next one comes
    private enum Act { None, Stamp, Shake, Roar }
    private Act act;
    private float actStart, nextAct, nextSnort;
    private int stampLeg;

    private void Capture()
    {
        upperRest = new Quaternion[4];
        lowerRest = new Quaternion[4];
        for (int i = 0; i < 4; i++)
        {
            upperRest[i] = upperLegs[i].localRotation;
            lowerRest[i] = lowerLegs[i].localRotation;
        }
        chestRest = chest.localRotation;
        hipsRest = hips.localRotation;
        headRest = head.localRotation;
        tailRest = tail != null ? tail.localRotation : Quaternion.identity;
        bodyRest = body.localPosition;
        lastYaw = transform.eulerAngles.y;
        nextAct = Time.time + Random.Range(2f, 4f);
        nextSnort = Time.time + Random.Range(1f, 3f);
        var stats = FindAnyObjectByType<PlayerStats>();
        player = stats != null ? stats.transform : null;
        // The golden mane leaves a trail of sparks while it runs
        maneSparks = Vfx.Sparkle(head.gameObject, Gold, 2.5f, 0.16f);
        captured = true;
    }

    // cycle: the gallop's phase in turns (advance it with speed); moving: 0 standing .. 1 full gallop
    public void Pose(float cycle, float moving)
    {
        if (!captured)
            Capture();
        float t = Time.time, dt = Time.deltaTime;
        float still = 1f - moving;

        // Turning: bank into the turn, the faster it runs the more
        float yaw = transform.eulerAngles.y;
        float yawRate = dt > 0f ? Mathf.DeltaAngle(lastYaw, yaw) / dt : 0f;
        lastYaw = yaw;
        turnLean = Mathf.Lerp(turnLean, Mathf.Clamp(-yawRate * 0.08f, -lean, lean) * Mathf.Max(moving, 0.3f), dt * 5f);
        // (the boss sets the whole lion's bob and pitch each frame just before this; the bank goes on top)
        transform.localRotation *= Quaternion.Euler(0f, 0f, turnLean);

        IdleActs(t, moving);
        float actK = act == Act.None ? 0f : Mathf.Sin(Mathf.Clamp01((t - actStart) / ActLength) * Mathf.PI);

        for (int i = 0; i < 4; i++)
        {
            float a = (cycle + LegPhase[i]) * Mathf.PI * 2f;
            // The bones sit in the mesh's frame: its x axis runs across the body, so turning about it swings a leg fore and aft
            float upper = Mathf.Sin(a) * swing * moving;
            // The knee folds while the leg travels forward (the half of the swing where the hoof is off the ground)
            float fold = Mathf.Max(0f, Mathf.Cos(a)) * kneeFold * moving;
            // A paw raised and brought down hard
            if (act == Act.Stamp && i == stampLeg)
            {
                upper += 28f * actK;
                fold += 50f * actK;
            }
            bool hind = i >= 2;
            upperLegs[i].localRotation = upperRest[i] * Quaternion.Euler(upper, 0f, 0f);
            lowerLegs[i].localRotation = lowerRest[i] * Quaternion.Euler(hind ? -fold : fold, 0f, 0f);

            // A footfall: the leg passes under the body moving back, the hoof is down; kick up dust
            float s = Mathf.Sin(a);
            if (moving > 0.5f && lastSin[i] > 0f && s <= 0f)
                Vfx.Dust(new Vector3(lowerLegs[i].position.x, transform.position.y + 0.05f, lowerLegs[i].position.z), 0.6f, 4, new Color(1f, 0.92f, 0.75f));
            lastSin[i] = s;
        }
        if (act == Act.Stamp && t - actStart > ActLength * 0.5f && t - actStart - dt <= ActLength * 0.5f)
        {
            Vector3 paw = lowerLegs[stampLeg].position;
            Vfx.Dust(new Vector3(paw.x, transform.position.y + 0.05f, paw.z), 1f, 8, new Color(1f, 0.92f, 0.75f));
            CameraShake.Shake(0.1f, 0.06f);
        }

        float stride = cycle * Mathf.PI * 2f;
        float breathe = Mathf.Sin(t * 1.6f) * still;
        // Each bound lifts the body twice per stride (fore and hind pairs), settling with the breath when still
        Vector3 up = body.parent != null ? body.parent.InverseTransformVector(Vector3.up) : Vector3.up;   // world up, in the bone's parent frame
        body.localPosition = bodyRest + up * (Mathf.Abs(Mathf.Sin(stride)) * bound * moving + breathe * bound * 0.25f);
        chest.localRotation = chestRest * Quaternion.Euler(Mathf.Sin(stride) * backRock * moving + breathe * 1.5f
            - (act == Act.Roar ? 6f * actK : 0f), 0f, 0f);
        hips.localRotation = hipsRest * Quaternion.Euler(-Mathf.Sin(stride) * backRock * moving
            + (act == Act.Roar ? 4f * actK : 0f), 0f, 0f);

        // The head: nods against the chest at the gallop; standing, it follows the player
        float wantYaw = 0f;
        if (player != null)
        {
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                wantYaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, to, Vector3.up), -lookRange, lookRange);
        }
        headYaw = Mathf.Lerp(headYaw, wantYaw * Mathf.Lerp(1f, 0.35f, moving), dt * 4f);
        float shake = act == Act.Shake ? Mathf.Sin((t - actStart) * 28f) * 22f * actK : 0f;
        float roarLift = act == Act.Roar ? -26f * actK : 0f;   // head thrown up and back
        head.localRotation = headRest * Quaternion.Euler(-Mathf.Sin(stride + 0.6f) * headNod * moving + breathe * 2.5f + roarLift,
            0f, -headYaw + Mathf.Sin(t * 0.5f) * 5f * still + shake);
        if (tail != null)
            tail.localRotation = tailRest * Quaternion.Euler(Mathf.Sin(stride * 0.5f) * 14f * moving - 18f * moving
                + (act == Act.Roar ? -20f * actK : 0f),
                0f, Mathf.Sin(t * (1.3f + still * 0.9f)) * (18f + 10f * still));

        // More sparks off the mane at a gallop
        if (maneSparks != null)
        {
            var emission = maneSparks.emission;
            emission.rateOverDistance = Mathf.Lerp(0.5f, 4f, moving);
        }

        // Breath: a puff from the muzzle every few seconds while it stands
        if (t >= nextSnort)
        {
            nextSnort = t + Random.Range(2.5f, 4.5f);
            if (moving < 0.3f)
                Vfx.Dust(head.position + transform.forward * 0.6f * transform.lossyScale.x, 0.35f, 4, new Color(1f, 0.95f, 0.85f, 0.6f));
        }
    }

    private const float ActLength = 1.1f;

    // Standing still for a while, it does something now and then: stamps, shakes its mane, or roars
    private void IdleActs(float t, float moving)
    {
        if (act != Act.None && t - actStart >= ActLength)
            act = Act.None;
        if (act != Act.None || t < nextAct)
            return;
        nextAct = t + Random.Range(3f, 6f);
        if (moving > 0.25f)
            return;
        float roll = Random.value;
        act = roll < 0.4f ? Act.Stamp : roll < 0.75f ? Act.Shake : Act.Roar;
        actStart = t;
        stampLeg = Random.Range(0, 2);
        if (act == Act.Roar)
        {
            // A golden breath of light out of its jaws, and a ring of force round its feet
            Vector3 mouth = head.position + transform.forward * 0.8f * transform.lossyScale.x;
            Vfx.Flash(mouth, 1.6f * transform.lossyScale.x, Gold, 0.3f);
            Vfx.Burst(mouth, Gold, 12, 4f, 0.12f);
            Vfx.GroundRing(new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z), 2.5f * transform.lossyScale.x, Gold, 0.5f);
            CameraShake.Shake(0.25f, 0.08f);
        }
        else if (act == Act.Shake)
            Vfx.Burst(head.position, Gold, 10, 2.5f, 0.1f);
    }
}
