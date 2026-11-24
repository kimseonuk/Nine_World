using UnityEngine;

// Nidhogg's model has no skeleton, so this bends its mesh directly every frame: the four legs step in a
// diagonal gait, the neck bends (pitch / yaw / reach), the wings fold and spread, the tail sways and the
// chest swells. Body regions are worked out from the mesh's own bounds (head at +z, feet at the bottom).
// NidhoggBoss sets the targets below; the rig eases towards them.
public class NidhoggRig : MonoBehaviour
{
    [Header("Pose targets (set by the boss)")]
    public float headPitch;     // degrees, + = nose down
    public float headYaw;       // degrees, + = to its right
    public float neckReach;     // metres the head is thrust forward (- = drawn back)
    public float wingSpread;    // degrees, + = wings raised and opened, - = folded down
    public float wingFlap;      // amplitude of a beating flap, degrees
    public float tailSway;      // extra degrees of tail swing
    public float tailLift;      // degrees, + = tail raised, - = lowered
    public float chestSwell;    // 0..0.1, a deep breath
    public float gait;          // 0 = standing, 1 = full stride
    public float crouch;        // metres the body sinks on bent legs
    public float jawOpen;       // degrees, + = gaping wider than modelled, - = closing (about -38 = shut)
    public float ease = 8f;     // how quickly the pose follows its targets
    public float jawEase = 14f; // the jaw has its own speed, so it can snap shut on a bite

    [Header("Motion feel")]
    public float legSwing = 26f;    // degrees each leg swings fore and aft about its hip
    public float legSplay = 14f;    // degrees a lifted leg swings out to the side
    public float legLift = 0.4f;    // metres a stepping foot rises
    public float kneeTuck = 0.18f;  // metres a lifted foot tucks back under the knee
    public float bodyRoll = 4f;     // degrees the body rolls onto its planted legs
    public float spineTwist = 6f;   // degrees shoulders and hips twist against each other
    public float tailWave = 14f;    // degrees of the idle wave running down the tail
    public float tailWaveSpeed = 1.8f;

    private Transform visual;
    private Mesh mesh;
    private Vector3[] baseVerts, baseNormals, verts, normals;
    private float[] head, jaw, wing, leg, tail;
    private float[] tailAlong;  // 0 at the tail's root .. 1 at its tip
    private float[] spine;      // -1 hips .. +1 shoulders, for the twist
    private sbyte[] side;       // -1 left, +1 right of the body's centre line
    private bool[] front;       // front leg (vs hind leg)
    private Matrix4x4 toRoot, fromRoot;
    private Vector3 neckPivot, tailPivot, wingRootL, wingRootR, chestCentre, jawHinge;
    private readonly Vector3[] hips = new Vector3[4];   // per leg: front-left, front-right, hind-left, hind-right

    // the current (eased) pose
    private float pitch, yaw, reach, spread, flap, sway, lift, swell, stride, sink, jawAngle;
    private float gaitPhase, flapPhase;

    public const float JawShut = -38f;   // teeth together
    public const float JawRest = -24f;   // mouth just parted, a snarl

    public bool Ready => mesh != null;

    // Called once with the child holding the mesh; needs the model imported with Read/Write enabled
    public void Bind(Transform model)
    {
        visual = model;
        var filter = model.GetComponentInChildren<MeshFilter>();
        if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
        {
            Debug.LogWarning("NidhoggRig: the model's mesh is missing or not readable (enable Read/Write on the import)");
            return;
        }
        mesh = Instantiate(filter.sharedMesh);
        mesh.MarkDynamic();
        filter.mesh = mesh;

        // mesh space <-> the boss's local space, where regions and motions are easy to describe
        toRoot = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        fromRoot = toRoot.inverse;
        baseVerts = mesh.vertices;
        baseNormals = mesh.normals;
        int n = baseVerts.Length;
        verts = new Vector3[n];
        normals = new Vector3[n];
        var local = new Vector3[n];
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        for (int i = 0; i < n; i++)
        {
            local[i] = toRoot.MultiplyPoint3x4(baseVerts[i]);
            min = Vector3.Min(min, local[i]);
            max = Vector3.Max(max, local[i]);
        }
        Vector3 size = max - min;

        // the head's centre line sets where the body's middle is
        float cx = 0f;
        int headCount = 0;
        foreach (Vector3 p in local)
            if ((p.z - min.z) / size.z > 0.85f && (p.y - min.y) / size.y > 0.5f) { cx += p.x; headCount++; }
        cx = headCount > 0 ? cx / headCount : (min.x + max.x) / 2f;

        neckPivot = new Vector3(cx, min.y + size.y * 0.6f, min.z + size.z * 0.6f);
        tailPivot = new Vector3(cx, min.y + size.y * 0.35f, min.z + size.z * 0.3f);
        wingRootL = new Vector3(cx - size.x * 0.12f, min.y + size.y * 0.68f, 0f);
        wingRootR = new Vector3(cx + size.x * 0.12f, min.y + size.y * 0.68f, 0f);
        chestCentre = new Vector3(cx, min.y + size.y * 0.5f, min.z + size.z * 0.55f);
        // the lower jaw hangs open in the model; it swings about a hinge just behind the corner of the mouth
        jawHinge = new Vector3(cx, min.y + size.y * 0.585f, min.z + size.z * 0.905f);
        Vector3 mouthCorner = new Vector3(cx, min.y + size.y * 0.581f, min.z + size.z * 0.925f);

        head = new float[n]; jaw = new float[n]; wing = new float[n]; leg = new float[n]; tail = new float[n];
        tailAlong = new float[n]; spine = new float[n];
        side = new sbyte[n]; front = new bool[n];
        float halfX = size.x / 2f;
        var hipSum = new Vector3[4];
        var hipCount = new int[4];
        for (int i = 0; i < n; i++)
        {
            Vector3 p = local[i];
            float u = (p.z - min.z) / size.z;   // 0 tail end .. 1 snout
            float v = (p.y - min.y) / size.y;   // 0 feet .. 1 top
            float lateral = Mathf.Abs(p.x - cx) / halfX;
            side[i] = (sbyte)(p.x < cx ? -1 : 1);
            front[i] = u > 0.5f;
            // the whole head (upper and lower jaw alike) turns as one piece; only the neck behind it bends
            head[i] = Smooth(0.55f, 0.85f, u) * Mathf.Max(Smooth(0.42f, 0.6f, v), Smooth(0.8f, 0.88f, u) * Smooth(0.15f, 0.3f, v));
            // lower jaw: below the line splitting the open mouth, at the front of the head
            Vector3 fromCorner = p - mouthCorner;
            float underMouth = -0.8f * fromCorner.z - fromCorner.y;
            jaw[i] = Smooth(-0.04f, 0.1f, underMouth) * Smooth(0.86f, 0.9f, u) * Smooth(0.3f, 0.4f, v);
            wing[i] = Smooth(0.62f, 0.76f, v) * Smooth(0.7f, 0.5f, u) * Smooth(0.12f, 0.4f, lateral);
            leg[i] = Smooth(0.3f, 0.06f, v) * (1f - head[i]);
            tail[i] = Smooth(0.3f, 0.02f, u) * Smooth(0.7f, 0.5f, v) * (1f - leg[i]);
            tailAlong[i] = Mathf.InverseLerp(tailPivot.z, min.z, p.z);
            spine[i] = Mathf.Clamp((u - 0.45f) / 0.3f, -1f, 1f);
            if (leg[i] > 0.5f)
            {
                int g = LegIndex(i);
                hipSum[g] += p;
                hipCount[g]++;
            }
        }
        // each leg swings about its own hip: above the middle of its foot, at the top of the leg region
        for (int g = 0; g < 4; g++)
        {
            Vector3 c = hipCount[g] > 0 ? hipSum[g] / hipCount[g]
                : new Vector3(cx + (g % 2 == 0 ? -1f : 1f) * size.x * 0.25f, 0f, min.z + size.z * (g < 2 ? 0.65f : 0.35f));
            hips[g] = new Vector3(c.x, min.y + size.y * 0.32f, c.z);
        }
        mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size * 1.6f);
        jawOpen = jawAngle = JawRest;
    }

    private static float Smooth(float from, float to, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, x));

    private int LegIndex(int i) => (front[i] ? 0 : 2) + (side[i] < 0 ? 0 : 1);

    private void LateUpdate()
    {
        if (mesh == null)
            return;
        float k = 1f - Mathf.Exp(-ease * Time.deltaTime);
        pitch = Mathf.Lerp(pitch, headPitch, k);
        yaw = Mathf.Lerp(yaw, headYaw, k);
        reach = Mathf.Lerp(reach, neckReach, k);
        spread = Mathf.Lerp(spread, wingSpread, k);
        flap = Mathf.Lerp(flap, wingFlap, k);
        sway = Mathf.Lerp(sway, tailSway, k);
        lift = Mathf.Lerp(lift, tailLift, k);
        swell = Mathf.Lerp(swell, chestSwell, k);
        stride = Mathf.Lerp(stride, gait, k);
        sink = Mathf.Lerp(sink, crouch, k);
        jawAngle = Mathf.Lerp(jawAngle, jawOpen, 1f - Mathf.Exp(-jawEase * Time.deltaTime));

        gaitPhase += Time.deltaTime * Mathf.Lerp(0f, 5.5f, stride);
        flapPhase += Time.deltaTime * 9f;
        Deform(Time.time);
    }

    // Jump straight to the targets (no easing) at a given moment of the gait / flap cycle: for previews and tests
    public void Snap(float gaitAt = 0f, float time = 0f)
    {
        pitch = headPitch; yaw = headYaw; reach = neckReach; spread = wingSpread; flap = wingFlap;
        sway = tailSway; lift = tailLift; swell = chestSwell; stride = gait; sink = crouch; jawAngle = jawOpen;
        gaitPhase = gaitAt;
        flapPhase = gaitAt;
        if (mesh != null)
            Deform(time);
    }

    private void Deform(float t)
    {

        // the idle life underneath every pose: breathing, wings settling
        float breathe = Mathf.Sin(t * 1.6f) * 0.012f;
        float wingAngle = spread + Mathf.Sin(t * 1.1f) * 2f + Mathf.Sin(flapPhase) * flap;

        Quaternion headRot = Quaternion.Euler(pitch, yaw, 0f);
        // a slow working of the jaw while it breathes
        Quaternion jawRot = Quaternion.Euler(jawAngle + Mathf.Sin(t * 1.6f) * 2f, 0f, 0f);
        Quaternion wingRotL = Quaternion.Euler(0f, 0f, -wingAngle);
        Quaternion wingRotR = Quaternion.Euler(0f, 0f, wingAngle);
        float scale = 1f + swell + breathe;

        // diagonal gait: front-left with hind-right, then front-right with hind-left.
        // A leg lifts while it swings forward and is planted while it pushes back.
        float liftA = Mathf.Max(0f, Mathf.Sin(gaitPhase)), liftB = Mathf.Max(0f, Mathf.Sin(gaitPhase + Mathf.PI));
        float strideA = -Mathf.Cos(gaitPhase), strideB = -strideA;
        var legRot = new Quaternion[4];
        var legRaise = new float[4];
        var legTuck = new float[4];
        for (int g = 0; g < 4; g++)
        {
            bool groupA = (g < 2) == (g % 2 == 0);
            float up = (groupA ? liftA : liftB) * stride;
            float swing = (groupA ? strideA : strideB) * stride;
            int s = g % 2 == 0 ? -1 : 1;
            // fore/aft swing about the hip, plus swinging out to the side as it lifts
            legRot[g] = Quaternion.Euler(-swing * legSwing, 0f, s * up * legSplay);
            legRaise[g] = up * legLift;
            legTuck[g] = up * kneeTuck;
        }

        // the body rolls onto the planted diagonal, bobs on each step, and shoulders twist against hips
        float roll = (liftB - liftA) * bodyRoll * stride;
        float bob = (Mathf.Abs(Mathf.Cos(gaitPhase)) - 0.5f) * 0.06f * stride;
        float twist = strideA * spineTwist * stride;

        for (int i = 0; i < baseVerts.Length; i++)
        {
            Vector3 p = toRoot.MultiplyPoint3x4(baseVerts[i]);
            Vector3 nrm = toRoot.MultiplyVector(baseNormals[i]);

            if (jaw[i] > 0f)
            {
                Quaternion r = Quaternion.Slerp(Quaternion.identity, jawRot, jaw[i]);
                p = jawHinge + r * (p - jawHinge);
                nrm = r * nrm;
            }

            // chest swells about its centre (not the feet, which stay planted)
            float body = 1f - leg[i];
            p = chestCentre + (p - chestCentre) * Mathf.Lerp(1f, scale, body);
            // the body sinks on bent legs; feet stay on the ground
            p.y -= sink * body;

            if (leg[i] > 0f)
            {
                int g = LegIndex(i);
                Quaternion r = Quaternion.Slerp(Quaternion.identity, legRot[g], leg[i]);
                p = hips[g] + r * (p - hips[g]);
                nrm = r * nrm;
                // the foot rises most, and folds back under the knee while it is in the air
                p.y += legRaise[g] * leg[i];
                p.z -= legTuck[g] * leg[i] * leg[i];
            }
            if (body > 0f && stride > 0f)
            {
                Quaternion r = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0f, twist * spine[i], roll), body);
                p = chestCentre + r * (p - chestCentre);
                p.y += bob * body;
                nrm = r * nrm;
            }
            if (tail[i] > 0f)
            {
                // a wave travels from root to tip: the further along, the later and wider it swings
                float a = tailAlong[i];
                float yawDeg = Mathf.Sin(t * tailWaveSpeed - a * 3f) * tailWave * a
                    + Mathf.Sin(gaitPhase - a * 2f) * 14f * stride
                    + sway;
                float pitchDeg = Mathf.Sin(t * tailWaveSpeed * 0.6f - a * 2.5f) * tailWave * 0.45f * a + lift;
                Quaternion r = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(pitchDeg, yawDeg, 0f), tail[i]);
                p = tailPivot + r * (p - tailPivot);
                nrm = r * nrm;
            }
            if (wing[i] > 0f)
            {
                Vector3 root = side[i] < 0 ? wingRootL : wingRootR;
                root.z = p.z;
                Quaternion r = Quaternion.Slerp(Quaternion.identity, side[i] < 0 ? wingRotL : wingRotR, wing[i]);
                p = root + r * (p - root);
                nrm = r * nrm;
            }
            if (head[i] > 0f)
            {
                Quaternion r = Quaternion.Slerp(Quaternion.identity, headRot, head[i]);
                p = neckPivot + r * (p - neckPivot) + Vector3.forward * (reach * head[i]);
                nrm = r * nrm;
            }

            verts[i] = fromRoot.MultiplyPoint3x4(p);
            normals[i] = fromRoot.MultiplyVector(nrm).normalized;
        }
        mesh.vertices = verts;
        mesh.normals = normals;
    }

    // Back to standing still with everything at rest
    public void Relax()
    {
        headPitch = headYaw = neckReach = wingSpread = wingFlap = tailSway = tailLift = chestSwell = gait = crouch = 0f;
        jawOpen = JawRest;
        jawEase = 10f;
    }
}
