using System.Collections.Generic;
using UnityEngine;

// 발도술 자세 (일섬's gathering): laid over whatever the animator plays, after it. Low and wide, the right shoulder turned
// forward, both hands drawn to the left hip and both blades laid back along it as if sheathed — ready to draw.
// Built from the twin-blade rig's own bones (two-bone reaches for the arms and legs), faded in and out by `Target`.
[DefaultExecutionOrder(100)]
public class IaidoPose : MonoBehaviour
{
    public float Target;                  // 0 = the animator's own pose, 1 = full stance
    private float weight;
    private Transform root, hips, spine, spine1, neck, head;
    private Transform lUp, lLeg, lFoot, rUp, rLeg, rFoot;
    private Transform lArm, lFore, lHand, rArm, rFore, rHand;
    private Transform lBlade, lTip, rBlade, rTip;
    private Transform[] bones;
    private readonly List<Quaternion> animated = new List<Quaternion>();
    private bool ready;

    public static IaidoPose On(Animator animator)
    {
        if (animator == null) return null;
        IaidoPose pose = animator.GetComponent<IaidoPose>();
        if (pose == null) pose = animator.gameObject.AddComponent<IaidoPose>();
        return pose;
    }

    private void Awake()
    {
        root = transform;
        var byName = new Dictionary<string, Transform>();
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            if (t.name == "TwinBlade" && t.parent != null)
            {
                if (t.parent.name == "LeftHand") { lBlade = t; lTip = t.Find("BladeTip"); }
                if (t.parent.name == "RightHand") { rBlade = t; rTip = t.Find("BladeTip"); }
            }
        }
        Transform B(string n) => byName.TryGetValue(n, out Transform t) ? t : null;
        hips = B("Hips"); spine = B("Spine"); spine1 = B("Spine1"); neck = B("Neck"); head = B("Head");
        lUp = B("LeftUpLeg"); lLeg = B("LeftLeg"); lFoot = B("LeftFoot");
        rUp = B("RightUpLeg"); rLeg = B("RightLeg"); rFoot = B("RightFoot");
        lArm = B("LeftArm"); lFore = B("LeftForeArm"); lHand = B("LeftHand");
        rArm = B("RightArm"); rFore = B("RightForeArm"); rHand = B("RightHand");
        bones = new[] { hips, spine, spine1, neck, head, lUp, lLeg, lFoot, rUp, rLeg, rFoot, lArm, lFore, lHand, rArm, rFore, rHand };
        ready = System.Array.TrueForAll(bones, b => b != null);
    }

    // What this wrote last frame and what lay under it: a bone the animator doesn't key (this rig's clips never move the
    // hips, for one) still holds last frame's stance, so it's put back first — or the stance would pile up frame on frame
    private Quaternion[] wroteRot, baseRot;
    private Vector3 wroteHips, baseHips;
    private bool wrote;

    private void LateUpdate()
    {
        // (in real time: the world may be slowed round the stance)
        weight = Mathf.MoveTowards(weight, Target, (Target > weight ? 7f : 12f) * Time.unscaledDeltaTime);
        if (!ready) return;
        if (wroteRot == null) { wroteRot = new Quaternion[bones.Length]; baseRot = new Quaternion[bones.Length]; }

        // undo last frame's stance wherever the animator left it standing
        if (wrote)
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i].localRotation == wroteRot[i]) bones[i].localRotation = baseRot[i];
            if (hips.localPosition == wroteHips) hips.localPosition = baseHips;
            wrote = false;
        }
        if (weight <= 0.001f) return;

        animated.Clear();
        foreach (Transform b in bones) animated.Add(b.localRotation);
        Vector3 hipsAnimated = hips.localPosition;

        Pose();

        for (int i = 0; i < bones.Length; i++)
        {
            if (weight < 0.999f) bones[i].localRotation = Quaternion.Slerp(animated[i], bones[i].localRotation, weight);
            baseRot[i] = animated[i];
            wroteRot[i] = bones[i].localRotation;
        }
        if (weight < 0.999f) hips.localPosition = Vector3.Lerp(hipsAnimated, hips.localPosition, weight);
        baseHips = hipsAnimated;
        wroteHips = hips.localPosition;
        wrote = true;
    }

    private void OnDisable()
    {
        if (!wrote) return;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i].localRotation == wroteRot[i]) bones[i].localRotation = baseRot[i];
        if (hips.localPosition == wroteHips) hips.localPosition = baseHips;
        wrote = false;
    }

    private Vector3 At(float x, float y, float z) => root.TransformPoint(new Vector3(x, y, z));

    // a turn in the fighter's own axes (x right, y up, z forward)
    private void Turn(Transform bone, float x, float y, float z) =>
        bone.rotation = root.rotation * Quaternion.Euler(x, y, z) * Quaternion.Inverse(root.rotation) * bone.rotation;

    private void Pose()
    {
        Quaternion lf = lFoot.rotation, rf = rFoot.rotation;
        hips.position += root.TransformVector(new Vector3(0f, -0.07f, 0f));
        Turn(hips, 0f, -30f, 0f);
        Turn(spine, 18f, -10f, 0f);
        Turn(spine1, 10f, 0f, 0f);
        Turn(neck, -24f, 20f, 0f);
        Turn(head, -14f, 16f, 0f);
        // right foot forward, left foot back and turned out
        Reach(rUp, rLeg, rFoot, At(0.11f, 0.04f, 0.17f), At(0.2f, 0.3f, 0.6f));
        Reach(lUp, lLeg, lFoot, At(-0.15f, 0.04f, -0.15f), At(-0.35f, 0.25f, 0.3f));
        lFoot.rotation = root.rotation * Quaternion.Euler(0f, -30f, 0f) * Quaternion.Inverse(root.rotation) * lf;
        rFoot.rotation = rf;
        // the right hand across the body to the left hip, the left hand on the "sheath" beside it
        Reach(rArm, rFore, rHand, At(-0.09f, 0.28f, 0.11f), At(0.35f, 0.35f, 0.15f));
        Reach(lArm, lFore, lHand, At(-0.19f, 0.22f, -0.02f), At(-0.4f, 0.35f, -0.2f));
        // both blades laid back along the hip
        Point(rHand, rBlade, rTip, new Vector3(-0.35f, -0.12f, -1f));
        Point(lHand, lBlade, lTip, new Vector3(-0.3f, -0.18f, -1f));
    }

    private void Point(Transform hand, Transform from, Transform tip, Vector3 dirLocal)
    {
        if (from == null || tip == null) return;
        hand.rotation = Quaternion.FromToRotation(tip.position - from.position, root.TransformDirection(dirLocal)) * hand.rotation;
    }

    private static float Angle(Vector3 a, Vector3 b) => Mathf.Acos(Mathf.Clamp(Vector3.Dot(a.normalized, b.normalized), -1f, 1f));

    // two-bone reach: upper → lower → end lands on the target, the middle joint bending toward the pole
    private static void Reach(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
    {
        float lab = (b.position - a.position).magnitude, lcb = (c.position - b.position).magnitude;
        float lat = Mathf.Clamp((target - a.position).magnitude, 0.01f, lab + lcb - 0.001f);
        Vector3 ac = c.position - a.position, ab = b.position - a.position;
        Vector3 axis = Vector3.Cross(ac, ab);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(ac, pole - a.position);
        if (axis.sqrMagnitude < 1e-8f) return;
        axis.Normalize();
        float ab0 = Angle(ac, ab), ba0 = Angle(a.position - b.position, c.position - b.position);
        float ab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
        float ba1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));
        a.rotation = Quaternion.AngleAxis((ab1 - ab0) * Mathf.Rad2Deg, axis) * a.rotation;
        b.rotation = Quaternion.AngleAxis(-(ba1 - ba0) * Mathf.Rad2Deg, axis) * b.rotation;
        a.rotation = Quaternion.FromToRotation(c.position - a.position, target - a.position) * a.rotation;
        Vector3 n = (target - a.position).normalized;
        Vector3 eb = Vector3.ProjectOnPlane(b.position - a.position, n), ep = Vector3.ProjectOnPlane(pole - a.position, n);
        if (eb.sqrMagnitude > 1e-6f && ep.sqrMagnitude > 1e-6f)
            a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(eb, ep, n), n) * a.rotation;
    }
}
