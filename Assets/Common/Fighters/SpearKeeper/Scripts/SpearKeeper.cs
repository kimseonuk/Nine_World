using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

// Niflheim's spear-keeper (바이킹 창지기), as the Niflheim project built him (its PlayerMiner's spear half, numbers from
// its scene): he thrusts instead of swinging, through every enemy in a narrow line; holding the button raises the
// spear through two charge stages and lets it fly (창 투척) — or, before that skill is learned, drives home a strong
// thrust; Space dashes in spear-first (돌진 찌르기). The basic attack is a five-blow combo; right-click, Q, E and R each
// play a motion of their own (SkillPierce / SkillSpin / SkillSlam / SkillThrow). The skills are learned in SpearSkills ([K]).
public class SpearKeeper : ArmoryWeapon
{
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
    private static readonly int ChargeHash = Animator.StringToHash("Charge");
    private static readonly int ChargeSpeedHash = Animator.StringToHash("ChargeSpeed");

    [Header("Reach")]
    [SerializeField] private float digRange = 1.5f;

    [Header("Spear: thrust")]
    [SerializeField] private float thrustExtraReach = 1.8f;   // a spear reaches this much further than a swing
    [SerializeField] private float thrustHalfWidth = 0.7f;    // a thrust hits everything in this narrow strip
    [SerializeField] private float thrustArc = 24f;           // the strip as drawn on the ground
    [SerializeField] private float heavyThrustDamage = 1.8f;  // 연속 찌르기: every third thrust
    [SerializeField] private float thrustChainWindow = 1.5f;
    [SerializeField] private float knockdownSeconds = 2f;     // 꿰뚫기

    [Header("Spear: charged throw (hold the button)")]
    [SerializeField] private float chargeDelay = 0.22f;      // held shorter than this = a thrust
    [SerializeField] private float stageOneTime = 0.8f;      // held time for the first charge stage
    [SerializeField] private float stageTwoTime = 1.7f;      // held time for the full charge
    [SerializeField] private float raiseSpeed = 0.6f;        // the wind-up plays slower than an attack
    [SerializeField] private float[] stageDamage = { 1.3f, 2f, 3.2f };
    [SerializeField] private float[] throwRange = { 9f, 13f, 18f };
    [SerializeField] private float recallDamage = 0.7f;      // 귀환의 창: the spear on its way back
    [SerializeField] private float gungnirSpread = 15f;      // 궁니르의 비: degrees between the three spears

    [Header("Spear: lunge (Space)")]
    [SerializeField] private float lungeDistance = 5.5f;
    [SerializeField] private float lungeTime = 0.22f;
    [SerializeField] private float lungeDamage = 1.4f;
    [SerializeField] private float lungeCooldown = 3f;

    private static readonly Color StageOneColor = new Color(1f, 0.6f, 0.15f);
    private static readonly Color StageTwoColor = new Color(0.55f, 0.85f, 1f);

    private GameObject heldWeapon;
    private bool pressing;
    private bool throwRequested;
    private float pressStart;
    private float heldAtRelease;
    private Vector3 chargeAim;
    private Transform chargeGlow, aimLine;
    private Coroutine chargeRoutine;
    private AttackRangeFx thrustFx;
    private int thrustChain;
    private float lastThrust = -10f;
    private float lungeReadyAt;
    private int spearsOut;
    private ClickToDig aim;

    public override CharacterProfile.WeaponKind Kind => CharacterProfile.WeaponKind.Spear;
    public override SkillTree Tree => SpearSkills.Tree;
    public override string TreeName => "창 기술";
    public override bool Rooted => IsSwinging;
    public override bool OwnsSpace => true;
    public override string Hint => "[클릭] 연속 찌르기 [우클릭·Q·E·R] 기술 [Space] 돌진";

    [Header("Skills: right-click and R (learned in the skill tree)")]
    private const float hookCooldown = 6f;                   // 우클릭 서리의 갈고리: the hooked spear on its chain
    [SerializeField] private float rainCooldown = 18f;       // R 창우: spears rain on the cursor
    [SerializeField] private float rainRadius = 4.2f;
    [SerializeField] private float rainDamage = 0.9f;
    [SerializeField] private int rainSpears = 16;

    private float pierceReadyAt, rainReadyAt;
    private float lungeEndedAt = -10f;   // 창술의 극의: a thrust right after a lunge is a heavy one
    private string activeSkill;          // which slot glows while it plays

    [Header("Skills: Q and E")]
    [SerializeField] private float sweepCooldown = 7f;       // Q 회전 창격: the spear swung round in a full circle
    [SerializeField] private float sweepRadius = 3.6f;
    [SerializeField] private float sweepDamage = 1.5f;
    [SerializeField] private float spikesCooldown = 10f;     // E 서리창 군열: spears thrust up out of the ground in a line
    [SerializeField] private float spikesLength = 9f;
    [SerializeField] private float spikesDamage = 1.8f;
    [SerializeField] private float spikesKnockdown = 1.6f;
    private float sweepReadyAt, spikesReadyAt;

    public float PierceCooldownLeft => Mathf.Max(0f, pierceReadyAt - Time.time);
    public float RainCooldownLeft => Mathf.Max(0f, rainReadyAt - Time.time);
    public float SweepCooldownLeft => Mathf.Max(0f, sweepReadyAt - Time.time);
    public float SpikesCooldownLeft => Mathf.Max(0f, spikesReadyAt - Time.time);
    // the skills' light: Niflheim's frost on the steel, pale ice-blue (the awakened spear's moves stay gold)
    private static readonly Color SkillColor = new Color(0.62f, 0.88f, 1f);
    private static readonly Color IceBright = new Color(0.8f, 0.96f, 1f);   // a heavy blow: near-white ice
    private float LungeCooldown => SpearSkills.Has(SpearSkills.Mastery) ? lungeCooldown * 0.5f : lungeCooldown;

    // A word over the head when a locked skill's key is pressed
    private float lockedNoticeAt;
    private void Locked()
    {
        if (Time.time < lockedNoticeAt)
            return;
        lockedNoticeAt = Time.time + 1f;
        DualBlades.Say(transform.position + Vector3.up * 2f, "아직 쓸 수 없습니다", new Color(0.75f, 0.75f, 0.8f));
    }

    // ── Each skill's own motion: a clip (or the stretch of it that matters) played at the skill's own pace, then back
    // to standing. Every skill used to fire the one generic "Attack" thrust, so Q, E and R all looked alike ──
    // SkillPierce: the raise-and-drive (Swing)   SkillSpin: the staff spin   SkillSlam: the leap and stab into the
    // ground   SkillThrow: the Valkyrie's throw (R throws the spear into the sky)
    private int poseToken;   // which move last took the body (an older move's "back to standing" is dropped)

    private void PlayPose(string state, float clipLength, float from, float speed)
    {
        poseToken++;
        if (animator == null)
            return;
        animator.SetFloat(AttackSpeedHash, speed);
        // (a normalized start: a fixed-time offset is counted in the state's sped-up time, which put each skill's
        // start far past the stretch it was meant to play; and the blend is a fixed 0.06 s - given as a fraction it
        // is of the long idle state, and the whole skill played half-blended with standing still)
        float current = animator.GetCurrentAnimatorStateInfo(0).length;
        animator.CrossFade(state, 0.06f / Mathf.Max(0.06f, current), 0, from);
    }

    private IEnumerator EndPose(float after)
    {
        int token = poseToken;
        if (after > 0f)
            yield return new WaitForSeconds(after);
        if (token == poseToken && !inCombo && !IsCharging && animator != null && animator.isActiveAndEnabled)
            animator.CrossFadeInFixedTime("Locomotion", 0.2f);
    }

    public bool IsSwinging { get; private set; }
    public bool IsCharging { get; private set; }
    public float ChargeHeld => pressing ? Time.time - pressStart : IsCharging ? heldAtRelease : 0f;
    public int ChargeStage => !IsCharging ? 0 : ChargeHeld >= stageTwoTime ? 2 : ChargeHeld >= stageOneTime ? 1 : 0;
    public float LungeCooldownLeft => Mathf.Max(0f, lungeReadyAt - Time.time);

    private float Reach => digRange + RunState.Stats.rangeBonus;
    private float ThrustReach => Reach + thrustExtraReach + (SpearSkills.Has(SpearSkills.Throw) ? 0.5f : 0f);   // (깊은 찌르기)
    private bool CanThrow => SpearSkills.Has(SpearSkills.Throw) && spearsOut == 0;
    private float AttackImpactDelay => profile != null ? profile.AttackImpactDelay : 0.57f;
    private float AttackDuration => profile != null ? profile.AttackDuration : 1.14f;
    private AttackRangeFx ThrustFx => thrustFx != null ? thrustFx : thrustFx = AttackRangeFx.Create(transform, thrustArc);

    protected override void Equipped(bool on)
    {
        if (heldWeapon != null)
        {
            heldWeapon.SetActive(true);
            heldWeapon.transform.localRotation = spearGrip;
            heldWeapon.transform.localPosition = spearGripAt;
        }
        heldWeapon = on ? profile.HeldWeapon : null;
        if (heldWeapon != null)
        {
            spearGrip = heldWeapon.transform.localRotation;
            spearGripAt = heldWeapon.transform.localPosition;
            spearModel = heldWeapon.transform.Find("SpearModel");
            spearPoint = heldWeapon.transform.Find("Point");
            floatReady = false;
        }
    }

    private Vector3 spearGripAt;
    private Quaternion spearGrip = Quaternion.identity;
    private Transform spearModel, spearPoint;

    // ── Gungnir floats: in his hand the long spear kept passing through his body, so it is no longer held - it hangs in
    // the air behind his shoulder, point to the sky, bobbing and trailing after him as he moves; when he attacks it
    // comes round to his side point-first, and every blow it is the spear itself that darts out and strikes (his
    // hand drives it on), going round him with Q's spin. (Still the same object: thrown, it hides, and returns.) ──
    private bool floatReady;
    private Vector3 floatCentre;
    private Quaternion floatTurn;
    private float strikeAt = -10f, strikeReach;
    private Vector3 strikeDir;
    private const float StrikeTime = 0.28f;

    // The floating spear darts out along `dir` to `reach` metres and back (each blow of the combo, the dash, the hook)
    private void StrikeSpear(Vector3 dir, float reach)
    {
        dir.y = 0f;
        strikeDir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        strikeReach = reach;
        strikeAt = Time.time;
    }

    private void FloatSpear()
    {
        if (!IsActive || heldWeapon == null || spearModel == null || spearPoint == null)
            return;
        Transform spear = heldWeapon.transform;
        // where the spear's middle is, along its own length from the pivot (the model sits behind the pivot)
        float halfAt = (spearModel.localPosition.z + spearPoint.localPosition.z) * 0.5f * spear.lossyScale.z;
        Vector3 fwd = transform.forward, right = transform.right, up = Vector3.up;
        float t = Time.time;
        Vector3 chest = transform.position + up * (0.3f + jumpLift);
        Vector3 centre;
        Quaternion turn;
        float follow;
        float sinceStrike = t - strikeAt;
        if (sinceStrike < StrikeTime)
        {
            // the strike: out fast to the full reach, a beat there, drawn back
            float p = sinceStrike / StrikeTime;
            float outK = p < 0.3f ? 1f - (1f - p / 0.3f) * (1f - p / 0.3f) : p < 0.45f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (p - 0.45f) / 0.55f);
            Vector3 d = strikeDir;
            Vector3 side = Vector3.Cross(up, d);
            float halfLength = Mathf.Abs(spearPoint.localPosition.z - spearModel.localPosition.z) * 0.5f * spear.lossyScale.z;
            centre = chest + side * 0.35f * (1f - outK) + d * Mathf.Lerp(0.5f, Mathf.Max(0.6f, strikeReach - halfLength + 0.2f), outK);
            turn = Quaternion.LookRotation(d, up);
            follow = 60f;
        }
        else if (activeSkill == "sweep")
        {
            // Q: whirled round him, point outward
            Vector3 d = Quaternion.Euler(0f, t * 900f % 360f, 0f) * fwd;
            centre = chest + d * 1.25f;
            turn = Quaternion.LookRotation(d, up);
            follow = 45f;
        }
        else if (IsSwinging || inCombo || activeSkill != null || (miner != null && miner.IsSwinging))
        {
            // a move under way: at his side, level, point toward his aim
            centre = chest + fwd * 0.45f + right * 0.5f + up * 0.1f;
            turn = Quaternion.LookRotation(fwd, up);
            follow = 22f;
        }
        else
        {
            // at rest: hanging upright behind his right shoulder, bobbing and swaying a little
            centre = transform.position + up * (0.62f + jumpLift + Mathf.Sin(t * 2.1f) * 0.07f) - fwd * 0.6f + right * 0.38f;
            Vector3 point = (up - fwd * 0.22f + right * 0.12f).normalized;
            turn = Quaternion.LookRotation(point, fwd) * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.3f) * 6f);
            follow = 9f;
        }
        // eased toward it (so it trails after him), snapping when it first appears
        float k = floatReady ? 1f - Mathf.Exp(-follow * Time.deltaTime) : 1f;
        floatCentre = Vector3.Lerp(floatReady ? floatCentre : centre, centre, k);
        floatTurn = Quaternion.Slerp(floatReady ? floatTurn : turn, turn, k);
        floatReady = true;
        spear.rotation = floatTurn;
        spear.position = floatCentre - (floatTurn * Vector3.forward) * halfAt;
        // a few motes of frost shed by it as it hangs
        if (Random.value < Time.deltaTime * 6f && heldWeapon.activeInHierarchy)
            Vfx.Burst(floatCentre + (floatTurn * Vector3.forward) * Random.Range(-0.6f, 0.9f), SpearSkills.Awakened ? GungnirGold : SkillColor, 1, 0.4f, 0.05f, 0.6f);
    }

    // ── Input: a click is the combo's next blow; holding the button carries on through the combo (the hold-to-charge
    // throw was taken out - the thrown lance lives on in 우클릭 관통 창격 and its upgrades) ──

    public override void Press(Vector3 aimPoint)
    {
        if (IsSwinging)
        {
            // a click during a combo blow is held over: the next blow follows as soon as this one is done
            if (inCombo && activeSkill == null)
            {
                comboQueued = true;
                queuedAim = aimPoint;
            }
            return;
        }
        Thrust(aimPoint);
    }

    public override void Hold(Vector3 aimPoint)
    {
        // held down: the next blow is queued as each one ends, so the combo runs on
        if (IsSwinging && inCombo && activeSkill == null)
        {
            comboQueued = true;
            queuedAim = aimPoint;
        }
        else if (!IsSwinging)
            Thrust(aimPoint);
    }

    public override void Release(Vector3 aimPoint)
    {
        if (!pressing)
            return;
        pressing = false;
    }

    public override void ResetCombat()
    {
        StopAllCoroutines();
        jumpLift = jumpTilt = 0f;
        // (the Valkyries' own motion stopped with the rest: sent away at once)
        foreach (Transform v in valkyries)
            if (v != null) Destroy(v.gameObject);
        valkyries.Clear();
        chargeRoutine = null;
        IsCharging = false;
        IsSwinging = false;
        pressing = false;
        throwRequested = false;
        comboStep = 0;
        comboQueued = false;
        inCombo = false;
        pastImpact = false;
        // trails a cut-off move left hanging on him or his spear
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t != null && (t.name == "LungeStreak" || t.name == "SpearTipTrail"))
                Destroy(t.gameObject);
        spearsOut = 0;
        lungeReadyAt = 0f;
        activeSkill = null;
        if (heldWeapon != null)
            heldWeapon.SetActive(true);
        if (animator != null && animator.isActiveAndEnabled)
        {
            animator.SetFloat(ChargeSpeedHash, 1f);
            animator.CrossFade("Locomotion", 0.1f);
        }
    }

    // Getting hit while charging breaks the charge
    public override void OnHurt(float damage)
    {
        if (!IsCharging || throwRequested)
            return;
        if (chargeRoutine != null)
            StopCoroutine(chargeRoutine);
        chargeRoutine = null;
        IsCharging = false;
        pressing = false;
        throwRequested = false;
        if (animator != null)
        {
            animator.SetFloat(ChargeSpeedHash, 1f);
            animator.CrossFade("Locomotion", 0.15f);
        }
        DualBlades.Say(transform.position + Vector3.up * 2f, "경직!", new Color(1f, 0.5f, 0.4f));
        StartCoroutine(Staggered(0.4f));
    }

    private IEnumerator Staggered(float seconds)
    {
        IsSwinging = true;
        yield return new WaitForSeconds(seconds);
        IsSwinging = false;
    }

    // ── Thrust ──

    // ── The basic attack: a five-blow combo ──
    // 1 찌르기 (a quick thrust)  2 내지르기 (a stepping thrust, longer reach)  3 회전 베기 (the spear swung round, all
    // about him)  4 이연 찌르기 (two quick thrusts)  5 관통 일격 (a deep lunge that runs through the line and throws
    // them off their feet). Clicking again once a blow has landed carries on into the next; a pause starts it over.
    // Each blow lands at the frame its clip drives the spear's point forward (measured off the clips: before, some
    // landed while the point was still swinging behind him).
    private enum Blow { Thrust, Spin, Lunge, Leap }
    private struct ComboStep
    {
        public string state;          // the animator state (its clip, its speed)
        public float clip, speed;     // clip length (s) and the state's base speed
        public float start;           // fraction of the clip the blow starts from (0: the beginning)
        public float impact, end;     // fractions of the clip: the blow lands, the move can be chained/ends
        public float impact2;         // a second thrust in the same blow (0: none)
        public Blow blow;
        public float reach, damage, step;   // extra reach, damage multiplier, metres stepped forward
    }

    private static readonly ComboStep[] Combo =
    {
        new ComboStep { state = "Combo1", clip = 1.45f, speed = 2.8f, impact = 0.72f, end = 0.84f, blow = Blow.Thrust, reach = 0f, damage = 1f, step = 0.25f },
        new ComboStep { state = "Combo2", clip = 2.0f, speed = 2.4f, impact = 0.45f, end = 0.6f, blow = Blow.Thrust, reach = 0.6f, damage = 1.15f, step = 0.45f },
        // 3 도약 내려찌르기: a short leap and the spear driven down into what is ahead (it was a spin, the same as Q's)
        new ComboStep { state = "Combo3", clip = 2.1f, speed = 2.0f, impact = 0.48f, end = 0.7f, blow = Blow.Lunge, reach = 1.4f, damage = 1.35f, step = 0.9f },
        new ComboStep { state = "Combo4", clip = 1.6f, speed = 1.7f, impact = 0.14f, impact2 = 0.6f, end = 0.7f, blow = Blow.Thrust, reach = 0.3f, damage = 0.8f, step = 0.3f },
        // (5타 plays its own motion: the leap and the throw, LeapThrow)
        new ComboStep { state = "SkillThrow", clip = 3.33f, speed = 1f, impact = 0.63f, end = 0.9f, blow = Blow.Leap, reach = 0f, damage = 2.2f, step = 0.8f },
    };
    private const float ComboResetAfter = 0.9f;   // seconds without a click before the combo starts over
    private int comboStep;
    private bool comboQueued;
    private bool pastImpact;
    private Vector3 queuedAim;

    public int ComboIndex => comboStep;

    private bool inCombo;   // a combo blow is playing (not a skill, lunge or charge)

    private void Thrust(Vector3 aimPoint)
    {
        if (IsSwinging)
            return;
        if (Time.time - lastThrust > ComboResetAfter || comboStep >= SpearSkills.ComboLength)
            comboStep = 0;
        comboRoutine = StartCoroutine(ComboBlow(aimPoint));
    }

    private Coroutine comboRoutine;

    private IEnumerator ComboBlow(Vector3 aimPoint)
    {
        ComboStep s = Combo[comboStep];
        float speed = AttackSpeed;
        thrustChain++;
        bool heavy = s.blow == Blow.Lunge || s.blow == Blow.Leap
                     || (SpearSkills.Has(SpearSkills.Flurry) && comboStep == 1)                          // 연속 찌르기: the 2nd blow hits hard too
                     || (SpearSkills.Has(SpearSkills.Mastery) && Time.time - lungeEndedAt < 0.5f);       // 창술의 극의
        IsSwinging = true;
        inCombo = true;
        pastImpact = false;
        comboQueued = false;
        FaceNow(aimPoint);
        if (s.blow == Blow.Leap)
        {
            // 5타 도약 투척: its own motion from start to finish
            yield return LeapThrow(s, aimPoint, heavy);
            EndComboBlow();
            yield break;
        }
        poseToken++;
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, speed);
            if (s.start > 0f)
            {
                // from part-way into its clip (a blend of 0.08 s, counted against the state it leaves)
                float current = animator.GetCurrentAnimatorStateInfo(0).length;
                animator.CrossFade(s.state, 0.08f / Mathf.Max(0.08f, current), 0, s.start);
            }
            else
                animator.CrossFadeInFixedTime(s.state, 0.08f);
        }
        float rate = s.speed * speed;   // clip seconds per real second
        float impactAt = s.clip * (s.impact - s.start) / rate, endAt = s.clip * (s.end - s.start) / rate;
        float reach = ThrustReach + s.reach;
        if (s.blow == Blow.Spin)
            Vfx.GroundRing(Feet, 0.6f, AttackRangeFx.SwingColor, impactAt);
        else
            ThrustFx.Windup(reach, impactAt);

        // the step in: the body follows the spear forward up to the blow
        for (float t = 0f; t < impactAt; t += Time.deltaTime)
        {
            if (s.step > 0f)
                controller.Move(transform.forward * (s.step / impactAt) * Time.deltaTime);
            yield return null;
        }
        LandComboBlow(s, reach, heavy);
        pastImpact = true;
        lastThrust = Time.time;
        float waited = impactAt;
        // 이연 찌르기: the second thrust of the pair
        if (s.impact2 > 0f)
        {
            float secondAt = s.clip * (s.impact2 - s.start) / rate;
            ThrustFx.Windup(reach, secondAt - impactAt);
            yield return new WaitForSeconds(secondAt - impactAt);
            LandComboBlow(s, reach, heavy);
            lastThrust = Time.time;
            waited = secondAt;
        }

        yield return new WaitForSeconds(Mathf.Max(0f, endAt - waited));
        EndComboBlow();
    }

    private void EndComboBlow()
    {
        // on to the next blow learned (the combo is as long as the tree has made it: 1 to 5 blows)
        comboStep = (comboStep + 1) % Mathf.Clamp(SpearSkills.ComboLength, 1, Combo.Length);
        IsSwinging = false;
        inCombo = false;
        pastImpact = false;
        comboRoutine = null;
        if (comboQueued && IsActive)
        {
            comboQueued = false;
            comboRoutine = StartCoroutine(ComboBlow(queuedAim));
        }
        else if (comboStep == 0 && animator != null)
            animator.CrossFadeInFixedTime("Locomotion", 0.2f);
    }

    // Each blow of the combo its own look: 1 the clean thrust, 2 the driving thrust (bigger, gold-edged, a shock at the
    // point), 3 the lunge drilling through, 4 two quick jabs side by side (one look per jab)
    private bool jabFlip;

    private void ComboFx(ComboStep s, float reach, Color color)
    {
        // the floating spear itself darts out to the blow's reach
        if (s.blow != Blow.Leap)
            StrikeSpear(transform.forward, reach + (s.blow == Blow.Lunge ? 0.6f : 0.2f));
        // (ice, and bright: the paintings are mostly deep blue on black, and at the game's distance anything dimmer is
        // lost; every blow also cracks frost into the ground at its point, throws up ice shards and lights the floor)
        Vector3 d = transform.forward;
        switch (s.state)
        {
            case "Combo1":
                SpearFx("sp_thrust", "dashstreak", reach + 0.5f, color * 2.8f, 0.24f, 0.95f, 1.15f);
                SpearFx("sp_thrust", "dashstreak", reach + 0.2f, Color.white * 1.2f, 0.12f, 0.9f, 1.05f);
                TipBurst(reach, 2.1f, color * 2.2f);
                IceAtPoint(reach, 1.1f, color);
                break;
            case "Combo2":
                SpearFx("sp_thrust", "dashstreak", reach + 0.8f, color * 3f, 0.3f, 0.95f, 1.22f);
                SpearFx("sp_thrust", "dashstreak", reach + 1.1f, Color.white * 1.3f, 0.2f, 1f, 1.3f);
                TipBurst(reach + 0.3f, 2.9f, color * 2.4f);
                IceAtPoint(reach + 0.3f, 1.5f, color);
                Shockwave.Spawn(transform.position + d * (reach + 0.3f), 1.5f, color, 14);
                break;
            case "Combo3":
                SpearFx("sp_drill", "dashstreak", reach + 1.2f, color * 3.2f, 0.38f, 0.85f, 1.18f);
                SpearFx("sp_thrust", "dashstreak", reach + 1.6f, Color.white * 1.3f, 0.26f, 1f, 1.28f);
                // a lane of ice frozen into the ground under the lunge
                Vfx.Sprite(Vfx.Pick("Frost_ChargeTrail", "dashstreak"), Feet + Vector3.up * 0.05f + d * ((reach + 1f) * 0.5f), reach + 1.6f, color * 1.6f, 0.6f, 0.9f, 1.05f, 0f,
                    Quaternion.LookRotation(Vector3.down, d) * Quaternion.Euler(0f, 0f, 90f));
                TipBurst(reach + 0.6f, 3.6f, color * 2.6f);
                IceAtPoint(reach + 0.6f, 2f, color);
                Vfx.Dust(Feet, 0.9f, 6, new Color(0.85f, 0.95f, 1f, 0.5f));
                break;
            case "Combo4":
                jabFlip = !jabFlip;
                float side = jabFlip ? 0.22f : -0.22f;
                SpearFx("sp_jabs", "dashstreak", reach + 0.6f, color * 3.2f, 0.2f, 0.95f, 1.12f, side);
                TipBurst(reach, 2f, color * 2.2f, side);
                IceAtPoint(reach, 1f, color);
                break;
        }
    }

    // Where a thrust lands: frost cracks out over the ground at its point, ice shards burst up, a cold light flares
    private void IceAtPoint(float reach, float size, Color color)
    {
        Vector3 at = Feet + transform.forward * (reach + 0.2f);
        Vfx.Sprite(Vfx.Pick("Frost_NovaRing", "ring"), at + Vector3.up * 0.06f, size * 2.2f, color * 1.6f, 0.4f, 0.4f, 1.1f, 60f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        IceVfx.Impact(at, size * 0.5f, size, false);
        IceVfx.Flash(at + Vector3.up * 0.8f, size * 3f, 2.2f, 0.15f);
        Vfx.Burst(at + Vector3.up * 0.4f, Color.Lerp(color, Color.white, 0.5f), 8, 5f, 0.1f, 1.2f);
    }

    private void LandComboBlow(ComboStep s, float reach, bool heavy)
    {
        // (the lunge and the leap are heavy by nature: their damage is already in the table)
        float damage = s.damage * (heavy && s.blow != Blow.Lunge && s.blow != Blow.Leap ? heavyThrustDamage : 1f);
        Color color = heavy ? IceBright : SkillColor;   // (frost-steel, like his skills; a heavy blow white-hot ice)
        var struck = new List<Enemy>();
        ComboFx(s, reach, color);
        switch (s.blow)
        {
            case Blow.Spin:
            {
                // the spear swung round: a ring of steel, everything within the shaft's reach struck
                float radius = ThrustReach * 0.85f;
                Vfx.Slash(transform.position + Vector3.up * 0.2f, transform.forward, radius * 2.1f, color, 0f, 300f, 0.3f);
                Vfx.GroundRing(Feet, radius, color, 0.35f);
                CameraShake.Shake(0.1f, 0.08f);
                foreach (Enemy enemy in Enemy.Alive.ToArray())
                {
                    if (enemy == null || enemy.IsDead)
                        continue;
                    Collider c = enemy.GetComponent<Collider>();
                    Vector3 off = (c != null ? c.ClosestPoint(transform.position) : enemy.transform.position) - transform.position;
                    off.y = 0f;
                    if (off.magnitude > radius)
                        continue;
                    Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * damage)), critical);
                    struck.Add(enemy);
                }
                break;
            }
            case Blow.Leap:
            {
                // 도약 내려찌르기: the spear driven down into the ground ahead - the ice cracks in a ring, frost and shards
                // burst up, and everything in the ring is struck and staggered
                Vector3 at = leapTarget;
                Vfx.Flash(at + Vector3.up * 0.4f, 1.6f, color, 0.14f);
                Vfx.GroundRing(at, LeapRadius, color, 0.35f);
                Vfx.Sprite("ring", at + Vector3.up * 0.06f, LeapRadius * 2.4f, color * 1.3f, 0.3f, 0.3f, 1.1f, Random.Range(-30f, 30f), Quaternion.Euler(90f, 0f, 0f));
                IceVfx.Impact(at, LeapRadius * 0.6f, LeapRadius * 1.2f, true);
                IceVfx.BlizzardBurst(at, LeapRadius);
                Vfx.Sprite(Vfx.Pick("Frost_GlacierImpact", "ring"), at + Vector3.up * 0.07f, LeapRadius * 2.3f, Color.Lerp(color, Color.white, 0.3f) * 1.5f, 0.6f, 0.4f, 1.1f, 20f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
                Vfx.Burst(at + Vector3.up * 0.3f, color, 18, 7f, 0.12f, 1.8f);
                Vfx.Dust(at, 1.2f, 8, new Color(0.85f, 0.9f, 0.95f, 0.45f));
                Shockwave.Spawn(at, LeapRadius, color, 12);
                CameraShake.Shake(0.15f, 0.12f);
                foreach (Enemy enemy in Enemy.Alive.ToArray())
                {
                    if (enemy == null || enemy.IsDead)
                        continue;
                    Collider c = enemy.GetComponent<Collider>();
                    Vector3 off = (c != null ? c.ClosestPoint(at + Vector3.up * 0.5f) : enemy.transform.position) - at;
                    off.y = 0f;
                    if (off.magnitude > LeapRadius)
                        continue;
                    Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * damage)), critical);
                    HitSpark(enemy, color);
                    // 꿰뚫기: the finisher throws them down
                    if (!enemy.IsDead)
                    {
                        if (SpearSkills.Has(SpearSkills.Impale)) enemy.Knockdown(knockdownSeconds);
                        else enemy.Interrupt();
                    }
                    struck.Add(enemy);
                }
                // 궁니르: the finishing blow looses Gungnir's flash - a golden lance far down the line from the strike
                if (SpearSkills.Awakened)
                {
                    const float flash = 14f;
                    StartCoroutine(PierceLance(transform.forward, flash));
                    ThrustStreak(flash, GungnirGold);
                    Shockwave.Spawn(at, LeapRadius * 1.3f, GungnirGold, 16);
                    Vfx.Flash(transform.position + Vector3.up * 0.3f + transform.forward * flash, 3f, GungnirGold, 0.22f);
                    foreach (Enemy enemy in HitLine(flash, damage * 0.8f))
                        if (!struck.Contains(enemy)) struck.Add(enemy);
                }
                break;
            }
            case Blow.Lunge:
            {
                // the driving lunge: a long bright line, a burst at its point, everything on it staggered
                ThrustFx.Flash(reach, color);
                ThrustStreak(reach, color);
                StartCoroutine(PierceLance(transform.forward, reach));
                Shockwave.Spawn(transform.position + transform.forward * reach * 0.5f, 1.6f, color, 12);
                CameraShake.Shake(0.18f, 0.14f);
                struck = HitLine(reach, damage);
                foreach (Enemy enemy in struck)
                    if (enemy != null && !enemy.IsDead) enemy.Interrupt();
                break;
            }
            default:
            {
                ThrustFx.Flash(reach, color);
                ThrustStreak(reach, color);
                Vfx.Flash(transform.position + Vector3.up * 0.3f + transform.forward * reach, 0.9f, color, 0.12f);
                struck = HitLine(reach, damage);
                if (heavy)
                {
                    CameraShake.Shake(0.15f, 0.12f);
                    foreach (Enemy enemy in struck)
                        if (enemy != null && !enemy.IsDead) enemy.Interrupt();
                }
                break;
            }
        }
        miner.ChainFrom(struck);
    }

    private void LandThrust(float multiplier, bool heavy)
    {
        Color color = heavy ? StageOneColor : AttackRangeFx.SwingColor;
        ThrustFx.Flash(ThrustReach, color);
        ThrustStreak(ThrustReach, color);
        List<Enemy> struck = HitLine(ThrustReach, multiplier);
        if (heavy)
        {
            CameraShake.Shake(0.15f, 0.12f);
            foreach (Enemy enemy in struck)
            {
                if (enemy == null || enemy.IsDead) continue;
                if (SpearSkills.Has(SpearSkills.Impale)) enemy.Knockdown(knockdownSeconds);
                else enemy.Interrupt();
            }
        }
        miner.ChainFrom(struck);
    }

    private const float LeapRadius = 3.8f;   // 5타: how wide the thrown spear's blast strikes
    private Vector3 leapTarget;               // where it comes down

    // ── 5타 도약 투척: he leaps high, and at the top of the leap hurls the spear down into the ground ahead (toward
    // the cursor, 3.5-6 m off); it strikes in a burst of ice, and comes back to his hand as he lands. No clip has a
    // leap, so the body is lifted (the model only; he stays on the ground for the game) over the Valkyrie's throw ──
    private IEnumerator LeapThrow(ComboStep s, Vector3 aimPoint, bool heavy)
    {
        float pace = Mathf.Max(0.5f, AttackSpeed);
        float rise = 0.42f / pace, fall = 0.32f / pace, flight = 0.12f;
        Vector3 to = aimPoint - Feet;
        to.y = 0f;
        float distance = Mathf.Clamp(to.magnitude, 3.5f, 6f);
        leapTarget = Feet + transform.forward * distance;
        // the throw's wind-up over the rise, loosed at the top (the clip looses at 63%)
        PlayPose("SkillThrow", 3.33f, 0.12f, 0.51f * 3.33f / rise);
        Vfx.Sprite("ring", leapTarget + Vector3.up * 0.05f, LeapRadius * 2f, SkillColor * 0.6f, rise + flight, 0.4f, 1f, 0f, Quaternion.Euler(90f, 0f, 0f));
        jumpRoutine = StartCoroutine(JumpArc(1.8f, rise, fall + flight));
        // a little way forward as he leaps
        for (float t = 0f; t < rise; t += Time.deltaTime)
        {
            controller.Move(transform.forward * (s.step / rise) * Time.deltaTime);
            yield return null;
        }
        // loosed: the spear leaves his hand and streaks down into the ground
        Vector3 from = transform.position + Vector3.up * (1.5f + jumpLift) + transform.forward * 0.3f;
        bool hid = heldWeapon != null && heldWeapon.activeSelf && spearsOut == 0;
        if (hid)
            heldWeapon.SetActive(false);
        Vector3 dir = (leapTarget - from).normalized;
        GameObject spear = SpearProp(from, dir, 2.1f);
        if (spear != null)
            Vfx.Trail(spear, heavy ? StageOneColor : SkillColor, 0.35f, 0.2f);
        Vfx.Flash(from, 1.4f, SkillColor, 0.12f);
        for (float t = 0f; t < flight && spear != null; t += Time.deltaTime)
        {
            spear.transform.position = Vector3.Lerp(from, leapTarget, t / flight);
            yield return null;
        }
        if (spear != null)
        {
            // stood in the ground where it struck a moment
            spear.transform.position = leapTarget - dir * 0.2f;
            Destroy(spear, 0.45f);
        }
        LandComboBlow(s, 0f, heavy);
        pastImpact = true;
        lastThrust = Time.time;
        yield return new WaitForSeconds(Mathf.Max(0.05f, fall - 0.02f));
        // landed: it comes back to his hand in a flash of light
        if (hid && heldWeapon != null && spearsOut == 0)
        {
            heldWeapon.SetActive(true);
            Vfx.Flash(heldWeapon.transform.position, 1.2f, SkillColor, 0.16f);
        }
        if (spear != null)
            Destroy(spear);
        yield return new WaitForSeconds(0.12f);
    }

    // The leap's lift, laid on the model after the animation has posed it
    private float jumpLift, jumpTilt;
    private Coroutine jumpRoutine;
    private Transform liftedModel;
    private Vector3 modelRest;
    private Quaternion modelRestRot;

    private IEnumerator JumpArc(float height, float up, float down)
    {
        const float dip = 0.06f;
        Vfx.Dust(Feet, 0.7f, 5, new Color(0.85f, 0.9f, 0.95f, 0.5f));
        for (float t = 0f; t < dip; t += Time.deltaTime)
        {
            jumpLift = -0.12f * Mathf.Sin(t / dip * Mathf.PI);
            yield return null;
        }
        float rise = Mathf.Max(0.1f, up - dip);
        for (float t = 0f; t < rise; t += Time.deltaTime)
        {
            float k = t / rise;
            jumpLift = height * (1f - (1f - k) * (1f - k));
            jumpTilt = -8f * k;                      // leaning back, the spear drawn back to throw
            yield return null;
        }
        for (float t = 0f; t < down; t += Time.deltaTime)
        {
            float k = t / down;
            jumpLift = height * (1f - k * k);
            jumpTilt = Mathf.Lerp(18f, 0f, k);       // pitched forward over the throw, then upright to land
            yield return null;
        }
        jumpLift = jumpTilt = 0f;
        Vfx.Dust(Feet, 0.9f, 6, new Color(0.85f, 0.9f, 0.95f, 0.5f));
        Shockwave.Spawn(Feet, 1.2f, SkillColor, 10);
    }

    private void LateUpdate()
    {
        LiftForLeap();
        FloatSpear();
    }

    private void LiftForLeap()
    {
        Transform model = IsActive && animator != null ? animator.transform : null;
        bool lifting = model != null && (jumpLift != 0f || jumpTilt != 0f);
        if (liftedModel != null && (!lifting || model != liftedModel))
        {
            liftedModel.localPosition = modelRest;
            liftedModel.localRotation = modelRestRot;
            liftedModel = null;
        }
        if (!lifting)
            return;
        if (liftedModel == null)
        {
            liftedModel = model;
            modelRest = model.localPosition;
            modelRestRot = model.localRotation;
        }
        model.localPosition = modelRest + Vector3.up * jumpLift;
        model.localRotation = modelRestRot * Quaternion.Euler(jumpTilt, 0f, 0f);
    }

    // ── The spear's own effects (painted for it: a thrust streak, the burst at the point, the drilling lunge, the
    // rapid jabs, the whirled wheel and the burst of points) - laid flat along the thrust at chest height, so from
    // above each reads as a spear driven out, not a blade's arc ──
    private static Quaternion FlatAlong(Vector3 dir) => Quaternion.LookRotation(Vector3.down, dir) * Quaternion.Euler(0f, 0f, 90f);

    private void SpearFx(string texture, string fallback, float length, Color color, float seconds, float startScale = 0.85f,
        float endScale = 1.08f, float lateral = 0f)
    {
        Vector3 d = transform.forward;
        Vector3 centre = transform.position + Vector3.up * 0.25f + d * (length * 0.5f + 0.25f) + transform.right * lateral;
        Vfx.Sprite(Vfx.Pick(texture, fallback), centre, length * 1.08f, color, seconds, startScale, endScale, 0f, FlatAlong(d));
    }

    // The burst of force at the spear's point, facing the camera
    private void TipBurst(float reach, float size, Color color, float lateral = 0f)
    {
        Vector3 at = transform.position + Vector3.up * 0.25f + transform.forward * (reach + 0.2f) + transform.right * lateral;
        Vfx.Sprite(Vfx.Pick("sp_tipburst", "flare"), at, size, color, 0.2f, 0.35f, 1.15f, 120f);
    }

    // A quick bright streak along the thrust: a point of light shot from the spear down the line, leaving a tapering
    // ribbon, with a small burst of force where it stops; and a ghost of the spear itself driven out to the end of the
    // reach - so every thrust reads as a spear going in, not a flash in front of him
    private void ThrustStreak(float length, Color color)
    {
        Vector3 dir = transform.forward;
        Vector3 from = transform.position + Vector3.up * 0.25f + dir * 0.5f;
        StartCoroutine(Streak(from, from + dir * Mathf.Max(0.5f, length - 0.5f), color));
        StartCoroutine(GhostSpear(dir, length, color));
    }

    // The spear's ghost: a pale glowing spear stabbing out to `length` and fading as it is drawn back
    private IEnumerator GhostSpear(Vector3 dir, float length, Color color)
    {
        Vector3 chest = transform.position + Vector3.up * 0.25f;
        Vector3 near = chest + dir * Mathf.Min(1.4f, length * 0.4f), far = chest + dir * length;
        GameObject spear = SpearProp(near, dir, 2.4f);
        if (spear == null)
            yield break;
        Destroy(spear, 0.5f);
        var block = new MaterialPropertyBlock();
        Renderer[] parts = spear.GetComponentsInChildren<Renderer>();
        Material ghost = Vfx.Material("glow", true);
        foreach (Renderer r in parts)
        {
            r.sharedMaterial = ghost;
            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color * 1.1f);
            r.SetPropertyBlock(block);
        }
        Vfx.Trail(spear, color * 0.8f, 0.22f, 0.1f);
        const float shoot = 0.07f, fade = 0.14f;
        for (float t = 0f; t < shoot && spear != null; t += Time.deltaTime)
        {
            spear.transform.position = Vector3.Lerp(near, far, 1f - (1f - t / shoot) * (1f - t / shoot));
            yield return null;
        }
        for (float t = 0f; t < fade && spear != null; t += Time.deltaTime)
        {
            float k = t / fade;
            spear.transform.position = Vector3.Lerp(far, far - dir * 0.6f, k);
            foreach (Renderer r in parts)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color * 1.1f * (1f - k));
                r.SetPropertyBlock(block);
            }
            yield return null;
        }
        if (spear != null)
            Destroy(spear);
    }

    // Where the spear goes into an enemy: a burst of light through it and splinters of frost
    public static void HitSpark(Enemy enemy, Color color)
    {
        if (enemy == null)
            return;
        Collider c = enemy.GetComponent<Collider>();
        Vector3 at = c != null ? c.bounds.center : enemy.transform.position + Vector3.up;
        Vfx.Sprite("hitsheet", at, 2.2f, color * 1.6f, 0.26f, 0.85f, 1.15f, Random.Range(-40f, 40f)).Sheet(4, 4);
        Vfx.Burst(at, color, 6, 5f, 0.09f, 0.8f);
    }

    private static IEnumerator Streak(Vector3 from, Vector3 to, Color color)
    {
        var go = new GameObject("ThrustStreak");
        go.transform.position = from;
        Destroy(go, 0.5f);   // (gone even if the move is cut off mid-flight)
        Vfx.Trail(go, color, 0.32f, 0.12f);
        const float travel = 0.07f;
        for (float t = 0f; t < travel && go != null; t += Time.deltaTime)
        {
            go.transform.position = Vector3.Lerp(from, to, t / travel);
            yield return null;
        }
        if (go == null)
            yield break;
        go.transform.position = to;
        Vfx.Sprite("ring", to, 0.9f, color * 1.2f, 0.16f, 0.3f, 1.2f, 0f, Quaternion.LookRotation(to - from));
        Destroy(go, 0.15f);
    }

    // Every enemy in a strip straight ahead
    private List<Enemy> HitLine(float length, float damageMultiplier)
    {
        var struck = new List<Enemy>();
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead)
                continue;
            Collider c = enemy.GetComponent<Collider>();
            Vector3 nearest = c != null ? c.ClosestPoint(transform.position) : enemy.transform.position;
            Vector3 offset = nearest - transform.position;
            offset.y = 0f;
            float along = Vector3.Dot(offset, transform.forward);
            if (along < -0.3f || along > length || Mathf.Abs(Vector3.Dot(offset, transform.right)) > thrustHalfWidth)
                continue;
            Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * damageMultiplier)), critical);
            HitSpark(enemy, critical ? StageOneColor : SkillColor);
            struck.Add(enemy);
        }
        return struck;
    }

    // ── 돌진 찌르기: dash toward the cursor spear-first, striking everything passed through ──

    private void Update()
    {
        if (IsActive && GameManager.IsRunActive && Time.timeScale > 0f)
        {
            Keyboard keyboard = Keyboard.current;
            // (keys go through the skill system: 기력, the class skill's supports — see SkillSystem.KeyDown)
            if (!pressing && SkillSystem.DodgeDown(keyboard))
                Lunge(Cursor());
            else if (!pressing && !IsSwinging)
            {
                Mouse mouse = Mouse.current;
                if (SkillSystem.RightPressed(mouse))
                {
                    if (!SpearSkills.Has(SpearSkills.Pierce)) Locked();
                    else if (PierceCooldownLeft <= 0f) StartCoroutine(HookSpear(Cursor()));
                }
                else if (SkillSystem.KeyDown(keyboard, 'Q'))
                {
                    if (!SpearSkills.Has(SpearSkills.Sweep)) Locked();
                    else if (SweepCooldownLeft <= 0f) StartCoroutine(SpinSweep());
                }
                else if (SkillSystem.KeyDown(keyboard, 'E'))
                {
                    if (!SpearSkills.Has(SpearSkills.Spikes)) Locked();
                    else if (SpikesCooldownLeft <= 0f) StartCoroutine(GroundSpears(Cursor()));
                }
                else if (SkillSystem.KeyDown(keyboard, 'R'))
                {
                    if (!SpearSkills.Has(SpearSkills.Rain)) Locked();
                    else if (RainCooldownLeft <= 0f) StartCoroutine(SpearRain(Cursor()));
                }
            }
        }
        UpdateChargeFx();
        UpdateGungnir();
    }

    // (test hook, as DebugCheats uses: a skill fired as if its key were pressed - "rmb", "q", "e", "r", "space")
    public void DebugUse(string key, Vector3 aimPoint)
    {
        if (IsSwinging && key != "space")
            return;
        switch (key)
        {
            case "rmb": StartCoroutine(HookSpear(aimPoint)); break;
            case "q": StartCoroutine(SpinSweep()); break;
            case "e": StartCoroutine(GroundSpears(aimPoint)); break;
            case "r": StartCoroutine(SpearRain(aimPoint)); break;
            case "space": Lunge(aimPoint); break;
        }
    }

    private Vector3 Cursor()
    {
        if (aim == null)
            aim = FindAnyObjectByType<ClickToDig>();
        return aim != null ? aim.CursorGroundPoint() : transform.position + transform.forward;
    }

    private Vector3 Feet => new Vector3(transform.position.x, transform.position.y - 0.97f, transform.position.z);

    private void FaceNow(Vector3 point)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(to);
    }

    private void Hit(Enemy enemy, float multiplier)
    {
        if (enemy == null || enemy.IsDead)
            return;
        Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * multiplier)), critical);
    }

    // ── A spear model (the winged Valkyrie spear, plain shapes if it's missing) with its point at the returned object's
    // origin, pointing along `pointDir`, `length` metres long — what 서리창 군열 and 창우 are made of ──
    private static GameObject spearPrefab;
    private static bool spearPrefabLooked;

    private static GameObject SpearProp(Vector3 tipAt, Vector3 pointDir, float length)
    {
        if (!spearPrefabLooked)
        {
            spearPrefabLooked = true;
            // (copies of Gungnir itself; the Valkyrie's spear if it is missing)
            spearPrefab = Resources.Load<GameObject>("Props/GungnirSpear");
            if (spearPrefab == null)
                spearPrefab = Resources.Load<GameObject>("Props/ValkyrieSpear");
        }
        var root = new GameObject("SpearProp");
        GameObject model;
        if (spearPrefab != null)
            model = Instantiate(spearPrefab);
        else
        {
            model = new GameObject("Spear");
            var steel = new Color(0.75f, 0.78f, 0.82f);
            Mats.Shape(PrimitiveType.Cube, model.transform, Vector3.zero, new Vector3(0.06f, 0.06f, 1.7f), Mats.Get("spear_prop_shaft", new Color(0.35f, 0.25f, 0.15f), 0.3f));
            Mats.Shape(PrimitiveType.Cube, model.transform, new Vector3(0f, 0f, 0.95f), new Vector3(0.14f, 0.03f, 0.3f), Mats.Get("spear_prop_tip", steel, 0.85f));
        }
        foreach (Collider c in model.GetComponentsInChildren<Collider>())
            Destroy(c);
        model.transform.SetParent(root.transform, false);
        // measured at the world origin, the model points down +z: scale it to length and slide its point onto the origin
        Renderer[] parts = model.GetComponentsInChildren<Renderer>();
        if (parts.Length > 0)
        {
            Bounds b = parts[0].bounds;
            foreach (Renderer r in parts)
                b.Encapsulate(r.bounds);
            float s = length / Mathf.Max(0.01f, b.size.z);
            model.transform.localScale *= s;
            model.transform.localPosition = new Vector3(-b.center.x * s, -b.center.y * s, -b.max.z * s);
            foreach (Renderer r in parts)
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        root.transform.SetPositionAndRotation(tipAt, Quaternion.LookRotation(pointDir));
        return root;
    }

    // A bright ribbon left by the point of the spear in his hand while a move plays (null if he holds none)
    private GameObject SpearTipTrail(float width, float time)
    {
        if (heldWeapon == null || !heldWeapon.activeInHierarchy)
            return null;
        Renderer[] parts = heldWeapon.GetComponentsInChildren<Renderer>();
        if (parts.Length == 0)
            return null;
        Bounds b = parts[0].bounds;
        foreach (Renderer r in parts)
            b.Encapsulate(r.bounds);
        // the point: whichever end of the spear's long axis is further from his body
        Vector3 e = b.extents;
        Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right * e.x : e.y >= e.z ? Vector3.up * e.y : Vector3.forward * e.z;
        Vector3 a = b.center + axis, c = b.center - axis;
        Vector3 point = (a - transform.position).sqrMagnitude > (c - transform.position).sqrMagnitude ? a : c;
        var go = new GameObject("SpearTipTrail");
        go.transform.SetParent(heldWeapon.transform, true);
        go.transform.position = point;
        Vfx.Trail(go, SkillColor * 0.7f, width, time);
        Destroy(go, 3f);   // (gone even if the move is cut off before it clears its own)
        return go;
    }

    // A spear thrust up out of the ground at `spot` and drawn back down again
    private IEnumerator ThrustUp(Vector3 spot, float length, float tilt, float stay)
    {
        Vector3 dir = Quaternion.Euler(Random.Range(-tilt, tilt), 0f, Random.Range(-tilt, tilt)) * Vector3.up;
        GameObject spear = SpearProp(spot - dir * length, dir, length);
        Vector3 down = spot - dir * length, up = spot + dir * length * 0.75f;
        for (float t = 0f; t < 0.1f && spear != null; t += Time.deltaTime)
        {
            spear.transform.position = Vector3.Lerp(down, up, t / 0.1f);
            yield return null;
        }
        if (spear == null)
            yield break;
        spear.transform.position = up;
        yield return new WaitForSeconds(stay);
        for (float t = 0f; t < 0.2f && spear != null; t += Time.deltaTime)
        {
            spear.transform.position = Vector3.Lerp(up, down, t / 0.2f);
            yield return null;
        }
        if (spear != null)
            Destroy(spear);
    }

    // ── 우클릭 · 서리의 갈고리: the spear hurled on a chain; its hooked point catches the first enemy in the way and drags it
    // back to his feet, stunned. Too big to drag (a boss, a giant), and it is he who is hauled in to it instead.
    // The branch above it: 갈고리 강화 (further, harder), 되감기 일격 (a thrust as it arrives), 서리 사슬 (longer stun
    // and frost-slowed), 세 갈래 갈고리 (three hooks in a fan), 강철 사슬 (harder, and the chain cuts what it passes),
    // 오딘의 까마귀 (hooks turned onto the nearest enemies), 꿰뚫기 (stun +0.5 s) ──
    private int hooksOut;

    private IEnumerator HookSpear(Vector3 aimPoint)
    {
        pierceReadyAt = Time.time + hookCooldown * SpearSkills.CooldownFactor;
        activeSkill = "pierce";
        IsSwinging = true;
        FaceNow(aimPoint);
        // the spear drawn up over the shoulder and hurled (the second half of the raise-and-drive)
        PlayPose("SkillPierce", 1.4f, 0.5f, 1.9f);
        Vector3 dir = transform.forward;
        bool up = SpearSkills.Has(SpearSkills.PierceUp), strong = SpearSkills.Has(SpearSkills.ThrowUp);
        bool awakened = SpearSkills.Awakened;
        float range = (up ? 12f : 8f) * (strong ? 1.15f : 1f);
        float damage = (up ? 2f : 1.4f) * (strong ? 1.3f : 1f);
        const int count = 1;   // (one hook, one line)
        bool seek = SpearSkills.Has(SpearSkills.Ravens) || awakened;
        Vfx.Gather(transform.position + Vector3.up * 1.2f + dir * 0.4f, 1.4f, SkillColor, 0.16f, 30f);
        yield return new WaitForSeconds(0.16f);
        CameraShake.Shake(0.1f, 0.08f);
        var claimed = new HashSet<Enemy>();
        hooksOut = 0;
        if (heldWeapon != null && spearsOut == 0)
            heldWeapon.SetActive(false);   // (the spear itself goes out on the chain)
        for (int i = 0; i < count; i++)
        {
            Vector3 lane = Quaternion.Euler(0f, (i - (count - 1) / 2f) * 20f, 0f) * dir;
            if (seek)
            {
                Enemy prey = NearestAhead(lane, range, awakened ? 55f : 30f, claimed);
                if (prey != null)
                {
                    claimed.Add(prey);
                    Vector3 to = prey.transform.position - transform.position;
                    to.y = 0f;
                    lane = to.normalized;
                }
            }
            hooksOut++;
            StartCoroutine(HookOne(lane, range, damage, claimed, awakened));
        }
        for (float t = 0f; hooksOut > 0 && t < 2f; t += Time.deltaTime)
            yield return null;
        if (heldWeapon != null && spearsOut == 0 && !heldWeapon.activeSelf)
        {
            heldWeapon.SetActive(true);
            Vfx.Flash(heldWeapon.transform.position, 0.9f, SkillColor, 0.12f);
        }
        yield return new WaitForSeconds(0.08f);
        IsSwinging = false;
        activeSkill = null;
        StartCoroutine(EndPose(0.05f));
    }

    private Vector3 HookHand => transform.position + Vector3.up * 0.25f + transform.forward * 0.35f;

    // One hook: out along `lane` until it bites or runs out of chain, then the haul
    private IEnumerator HookOne(Vector3 lane, float range, float damage, HashSet<Enemy> claimed, bool awakened)
    {
        // 서리의 갈고리 is ice through and through - chain, bite, whirl, frozen lane, flashes - awakened or not (gold over
        // the blue paintings had turned them green)
        Color ice = awakened ? Color.Lerp(SkillColor, Color.white, 0.3f) : SkillColor;
        Color color = ice;
        Vector3 start = HookHand;
        GameObject head = SpearProp(start, lane, 1.5f);
        var chainGo = new GameObject("HookChain");
        var chain = chainGo.AddComponent<LineRenderer>();
        chain.positionCount = 2;
        chain.useWorldSpace = true;
        // a chain of ice links (painted for it), repeated along its length; a plain glowing line if it is missing
        string chainTex = Vfx.Pick("ice_chain", "trail");
        bool links = chainTex == "ice_chain";
        chain.widthMultiplier = links ? 0.42f : 0.09f;
        chain.textureMode = links ? LineTextureMode.Tile : LineTextureMode.Stretch;
        chain.textureScale = new Vector2(links ? 0.46f : 1f, 1f);   // (the painting is four links, ~5:1: one repeat per 2.2 m at this width)
        chain.alignment = LineAlignment.View;
        chain.sharedMaterial = Vfx.Material(chainTex, true);
        chain.startColor = ice * 1.6f;
        chain.endColor = Color.Lerp(ice, Color.white, 0.5f) * 1.6f;
        chain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // (never left lying about, whatever cuts the skill short)
        Destroy(chainGo, 3f);
        if (head != null)
        {
            Destroy(head, 3f);
            Vfx.Trail(head, color * 0.8f, 0.25f, 0.12f);
        }
        Vfx.Sprite(Vfx.Pick("sp_tipburst", "flare"), start + lane * 0.4f, 1f, color, 0.15f, 0.4f, 1f, 90f);
        bool steel = SpearSkills.Has(SpearSkills.ThrowUp);
        var cut = new HashSet<Enemy>();
        Enemy caught = null;
        float run = 0f;
        Vector3 at = start;
        // out: the chain paid out behind the point
        while (run < range && caught == null)
        {
            run = Mathf.Min(range, run + 34f * Time.deltaTime);
            at = HookHand + lane * run;
            if (head != null) head.transform.position = at;
            chain.SetPosition(0, HookHand);
            chain.SetPosition(1, at);
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy.IsDead || cut.Contains(enemy))
                    continue;
                Collider c = enemy.GetComponent<Collider>();
                Vector3 near = c != null ? c.ClosestPoint(at) : enemy.transform.position;
                Vector3 off = near - at;
                off.y = 0f;
                if (off.magnitude > 0.75f)
                    continue;
                if (claimed.Contains(enemy) && !IsSeekTarget(enemy, lane))
                    continue;   // (another hook's prey)
                caught = enemy;
                break;
            }
            yield return null;
        }
        if (caught != null)
        {
            claimed.Add(caught);
            // the bite: a burst at the point, the blow
            Vector3 bite = caught.transform.position + Vector3.up * 0.8f;
            Hit(caught, damage);
            HitSpark(caught, color);
            // the hook bites: ice jaws snap shut on it, a whirl of frost sucked in round it, a cold light
            Vfx.Sprite(Vfx.Pick("ice_grip", "sp_tipburst"), bite, 2.8f, ice * 1.8f, 0.24f, 0.5f, 1.15f, 0f);
            Vfx.Sprite(Vfx.Pick("ice_pull", "ring"), new Vector3(bite.x, Feet.y + 0.08f, bite.z), 3.4f, ice * 1.6f, 0.45f, 1.3f, 0.45f, -540f, Quaternion.Euler(90f, 0f, 0f));
            IceVfx.Flash(bite, 6f, 2.6f, 0.2f);
            Vfx.Flash(bite, 1.6f, color, 0.14f);
            CameraShake.Shake(0.12f, 0.1f);
            DualBlades.Say(bite + Vector3.up * 1.1f, "걸렸다!", color);
            if (!caught.IsDead)
            {
                caught.Interrupt();
                Collider body = caught.GetComponent<Collider>();
                bool tooBig = caught.IsBoss || (body != null && Mathf.Max(body.bounds.extents.x, body.bounds.extents.z) > 1.4f);
                float radius = body != null ? Mathf.Max(body.bounds.extents.x, body.bounds.extents.z) : 0.5f;
                const float haul = 0.24f;
                if (tooBig)
                {
                    // too heavy to drag: the chain hauls him in to it instead
                    Vector3 to = caught.transform.position - transform.position;
                    to.y = 0f;
                    float travel = Mathf.Max(0f, to.magnitude - radius - 1.2f);
                    // the whirl of ice at the bite drawing him in, and the lane he is hauled along frozen behind him
                    Vector3 grabAt = new Vector3(caught.transform.position.x, Feet.y + 0.08f, caught.transform.position.z) - to.normalized * radius;
                    Vfx.Sprite(Vfx.Pick("ice_pull", "ring"), grabAt, 3.2f, ice * 1.5f, haul + 0.3f, 1.2f, 0.6f, -720f, Quaternion.Euler(90f, 0f, 0f));
                    if (travel > 0.5f)
                        Vfx.Sprite(Vfx.Pick("Frost_ChargeTrail", "dashstreak"), Feet + Vector3.up * 0.05f + to.normalized * (travel * 0.5f), travel + 1f, ice * 1.4f, 0.7f, 0.9f, 1.05f, 0f,
                            Quaternion.LookRotation(Vector3.down, to.normalized) * Quaternion.Euler(0f, 0f, 90f));
                    for (float t = 0f; t < haul && caught != null; t += Time.deltaTime)
                    {
                        controller.Move(to.normalized * (travel / haul) * Time.deltaTime);
                        Vector3 grip = caught.transform.position + Vector3.up * 0.8f - to.normalized * radius;
                        if (head != null) head.transform.position = grip;
                        chain.SetPosition(0, HookHand);
                        chain.SetPosition(1, grip);
                        yield return null;
                    }
                }
                else
                {
                    // dragged back across the ground to his feet, the chain cutting whatever it is pulled through
                    Vector3 from = caught.transform.position;
                    Vector3 to = transform.position + lane * (1.3f + radius);
                    to.y = from.y;
                    var cc = caught.GetComponent<CharacterController>();
                    // the ground it is dragged over freezes in a lane behind it, and a whirl of ice at his feet draws
                    // it in
                    Vector3 dragMid = (from + to) * 0.5f;
                    float dragLen = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));
                    if (dragLen > 0.5f)
                        Vfx.Sprite(Vfx.Pick("Frost_ChargeTrail", "dashstreak"), new Vector3(dragMid.x, Feet.y + 0.05f, dragMid.z), dragLen + 1f, ice * 1.5f, 0.7f, 0.9f, 1.05f, 0f,
                            Quaternion.LookRotation(Vector3.down, -lane) * Quaternion.Euler(0f, 0f, 90f));
                    Vfx.Sprite(Vfx.Pick("ice_pull", "ring"), new Vector3(to.x, Feet.y + 0.08f, to.z), 3f, ice * 1.4f, haul + 0.25f, 1.2f, 0.6f, -720f, Quaternion.Euler(90f, 0f, 0f));
                    for (float t = 0f; t < haul && caught != null && !caught.IsDead; t += Time.deltaTime)
                    {
                        float k = Mathf.Clamp01((t + Time.deltaTime) / haul);
                        Vector3 want = Vector3.Lerp(from, to, 1f - (1f - k) * (1f - k));
                        Vector3 step = want - caught.transform.position;
                        step.y = 0f;
                        if (cc != null && cc.enabled) cc.Move(step);
                        else caught.transform.position += step;
                        Vector3 grip = caught.transform.position + Vector3.up * 0.8f;
                        if (head != null) head.transform.position = grip;
                        chain.SetPosition(0, HookHand);
                        chain.SetPosition(1, grip);
                        if (steel)
                            ChainCut(HookHand, grip, damage * 0.5f, cut, caught);
                        // shards of ice shaken off the chain as it hauls
                        if (Random.value < 0.6f)
                            Vfx.Burst(Vector3.Lerp(HookHand, grip, Random.value), Color.Lerp(ice, Color.white, 0.4f), 2, 3f, 0.07f, 0.8f);
                        yield return null;
                    }
                    // slammed down at his feet: the ice cracks out under it
                    Vector3 landed = new Vector3(to.x, Feet.y, to.z);
                    IceVfx.Impact(landed, 0.9f, 1.6f, false);
                    Vfx.Sprite(Vfx.Pick("Frost_NovaRing", "ring"), landed + Vector3.up * 0.06f, 2.8f, ice * 1.5f, 0.45f, 0.4f, 1.1f, 60f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
                    Vfx.Dust(landed, 0.8f, 5, new Color(0.85f, 0.95f, 1f, 0.5f));
                }
                if (caught != null && !caught.IsDead)
                {
                    // landed at his feet: stunned (frost-chained longer and slowed, thrown down harder with 꿰뚫기)
                    bool frost = SpearSkills.Has(SpearSkills.FrostThrow);
                    float stun = 1f + (frost ? 1f : 0f) + (SpearSkills.Has(SpearSkills.Impale) ? 0.5f : 0f);
                    caught.Knockdown(stun);
                    if (frost)
                        EnemySlow.Apply(caught, 0.5f, stun + 3f);
                    Shockwave.Spawn(caught.transform.position, 1.1f, color, 10);
                    // 사슬 충격: it lands with a crash that strikes everything round it
                    if (SpearSkills.Has(SpearSkills.Gungnir))
                    {
                        Vector3 land = caught.transform.position;
                        Vfx.Sprite(Vfx.Pick("sp_radial", "runes"), new Vector3(land.x, Feet.y + 0.08f, land.z), 4.4f, color * 1.2f, 0.35f, 0.4f, 1.1f, 40f, Quaternion.Euler(90f, 0f, 0f));
                        Shockwave.Spawn(land, 2.2f, color, 14);
                        foreach (Enemy other in Enemy.Alive.ToArray())
                        {
                            if (other == null || other.IsDead || other == caught)
                                continue;
                            Vector3 off = other.transform.position - land;
                            off.y = 0f;
                            if (off.magnitude > 2.2f)
                                continue;
                            Hit(other, damage * 0.8f);
                            other.Interrupt();
                        }
                    }
                    // 되감기 일격: the spear back in hand and straight into it
                    if (SpearSkills.Has(SpearSkills.Recall) || awakened)
                    {
                        yield return new WaitForSeconds(0.06f);
                        if (caught != null && !caught.IsDead)
                        {
                            FaceNow(caught.transform.position);
                            float reach = Vector3.Distance(transform.position, caught.transform.position);
                            SpearFx("sp_thrust", "dashstreak", reach + 0.4f, color * 1.2f, 0.18f);
                            TipBurst(reach, 1.6f, color);
                            Hit(caught, damage * recallDamage);
                            HitSpark(caught, color);
                        }
                    }
                }
            }
        }
        else
        {
            // nothing caught: the chain reels back in
            Vector3 end = at;
            for (float t = 0f; t < 0.14f; t += Time.deltaTime)
            {
                Vector3 p = Vector3.Lerp(end, HookHand, t / 0.14f);
                if (head != null) head.transform.position = p;
                chain.SetPosition(0, HookHand);
                chain.SetPosition(1, p);
                yield return null;
            }
        }
        if (head != null) Destroy(head);
        Destroy(chainGo);
        hooksOut = Mathf.Max(0, hooksOut - 1);
    }

    // (a seeking hook was aimed at its prey: that one it may take even though it is claimed)
    private bool IsSeekTarget(Enemy enemy, Vector3 lane)
    {
        Vector3 to = enemy.transform.position - transform.position;
        to.y = 0f;
        return Vector3.Angle(to, lane) < 3f;
    }

    // 강철 사슬: whatever the chain is dragged through is cut (each enemy once)
    private void ChainCut(Vector3 a, Vector3 b, float damage, HashSet<Enemy> cut, Enemy caught)
    {
        Vector3 ab = b - a;
        ab.y = 0f;
        float len = ab.magnitude;
        if (len < 0.1f)
            return;
        Vector3 d = ab / len;
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead || enemy == caught || cut.Contains(enemy))
                continue;
            Vector3 off = enemy.transform.position - a;
            off.y = 0f;
            float along = Vector3.Dot(off, d);
            if (along < 0f || along > len || (off - d * along).magnitude > 0.9f)
                continue;
            cut.Add(enemy);
            Hit(enemy, damage);
            HitSpark(enemy, SkillColor);
        }
    }

    // 관통 창격's lance: a spear (the winged Valkyrie spear) shooting out from the chest to the end of the line in a
    // blink, a moment held there with a flash at its point, then drawn back as it fades
    private IEnumerator PierceLance(Vector3 dir, float length)
    {
        const float spearLength = 3.2f, shoot = 0.12f, hold = 0.1f, back = 0.16f;
        Vector3 chest = transform.position + Vector3.up * 0.25f;
        // it leaves from his hands: the point starts just ahead of him with the shaft running back past his grip
        Vector3 near = chest + dir * 1.8f, far = chest + dir * length;
        GameObject spear = SpearProp(near, dir, spearLength);
        Vfx.Flash(near, 1.2f, SkillColor, 0.12f);
        // a ring of force bursting off the point as it leaves, and the air torn along the line
        Vfx.Sprite("ring", near, 1.8f, SkillColor * 1.3f, 0.28f, 0.3f, 1.3f, 0f, Quaternion.LookRotation(dir));
        Vfx.Sprite("ring", chest + dir * (length * 0.55f), 1.3f, SkillColor, 0.22f, 0.4f, 1.2f, 0f, Quaternion.LookRotation(dir));
        var trail = new GameObject("PierceTrail");
        trail.transform.position = near;
        Vfx.Trail(trail, SkillColor, 0.6f, 0.22f);
        for (float t = 0f; t < shoot && spear != null; t += Time.deltaTime)
        {
            Vector3 at = Vector3.Lerp(near, far, 1f - (1f - t / shoot) * (1f - t / shoot));
            spear.transform.position = at;
            trail.transform.position = at;
            yield return null;
        }
        if (spear != null)
            spear.transform.position = far;
        trail.transform.position = far;
        Vfx.Flash(far, 1.6f, SkillColor, 0.18f);
        Vfx.Burst(far, SkillColor, 10, 6f, 0.14f);
        yield return new WaitForSeconds(hold);
        for (float t = 0f; t < back && spear != null; t += Time.deltaTime)
        {
            float k = t / back;
            spear.transform.position = Vector3.Lerp(far, far - dir * 1.2f, k);
            spear.transform.localScale = Vector3.one * (1f - k);
            yield return null;
        }
        if (spear != null)
            Destroy(spear);
        Destroy(trail, 0.3f);
    }

    // ── Q · 회전 창격: the spear swung round in a full circle, striking everything about ──
    private IEnumerator SpinSweep()
    {
        sweepReadyAt = Time.time + sweepCooldown * SpearSkills.CooldownFactor;
        activeSkill = "sweep";
        IsSwinging = true;
        const float spin = 0.4f;
        // 회오리 창격: a second turn that drags everything nearby in to the spear and strikes half again as hard
        int turns = SpearSkills.Has(SpearSkills.SweepUp) ? 2 : 1;
        // the staff spin: the spear whirled round his body (the clip turns him itself - the code no longer spins the
        // whole body on top of it, which had him turning two ways at once); one turn plays its first stretch, two
        // turns the whole of it
        PlayPose("SkillSpin", 5.07f, 0.12f, turns == 2 ? 3.55f / (spin * 2f + 0.2f) : 1.9f / (spin + 0.15f));
        // the spear's point leaves a bright ribbon as it goes round, crescents of steel sweep out with it, and the
        // dust is thrown up in a wheel at his feet
        GameObject tip = SpearTipTrail(0.22f, 0.16f);
        Vfx.Dust(Feet, sweepRadius * 0.7f, 10, new Color(0.8f, 0.78f, 0.72f, 0.45f));
        // the wheel of spear points whirled round him, turning with the spear
        float spinTime = spin * turns + 0.1f;
        Vfx.Sprite(Vfx.Pick("sp_spinwheel", "ring"), transform.position + Vector3.up * 0.2f, sweepRadius * 2.3f, SkillColor * 1.1f,
            spinTime, 0.55f, 1.05f, -900f, Quaternion.Euler(90f, 0f, 0f));
        for (int turn = 0; turn < turns; turn++)
        {
            bool whirl = turn == 1;
            float radius = whirl ? sweepRadius * 1.5f : sweepRadius;
            if (whirl)
            {
                // the whirlwind: wind spiralling in, and every enemy within reach dragged toward him
                Vfx.Sprite("ring", Feet + Vector3.up * 0.1f, radius * 2.2f, SkillColor * 0.6f, 0.35f, 1.1f, 0.3f, 360f, Quaternion.Euler(90f, 0f, 0f));
                foreach (Enemy enemy in Enemy.Alive.ToArray())
                    if (enemy != null && !enemy.IsDead)
                        StartCoroutine(PullIn(enemy, radius, 1.4f, spin));
            }
            // the crescents of steel go round with the spear: three per turn, each a third of the circle on
            float slashEvery = spin / 3f, nextSlash = 0f, heading = 0f;
            for (float t = 0f; t < spin; t += Time.deltaTime)
            {
                if (t >= nextSlash)
                {
                    nextSlash += slashEvery;
                    Vector3 way = Quaternion.Euler(0f, heading, 0f) * transform.forward;
                    heading += 120f;
                    // a spear point driven out as it comes round (thrusts, not a blade's crescents)
                    Vector3 mid = transform.position + Vector3.up * 0.2f + way * (sweepRadius * 0.55f);
                    Vfx.Sprite(Vfx.Pick("sp_thrust", "dashstreak"), mid, sweepRadius * 1.15f, SkillColor, 0.18f, 0.8f, 1.1f, 0f, FlatAlong(way));
                    Vfx.Sprite(Vfx.Pick("sp_tipburst", "flare"), transform.position + Vector3.up * 0.2f + way * sweepRadius, 0.9f, SkillColor, 0.16f, 0.4f, 1.1f, 90f);
                }
                yield return null;
            }
            // the turn's blow: a ring of frost-bright steel through everything within the spear's reach (measured to
            // the body's edge, as the combo's spin is, so a big monster beside him is struck)
            // the turn's end: a burst of ice spear points out of the ground all round him
            Vfx.Sprite(Vfx.Pick("sp_radial", "runes"), Feet + Vector3.up * 0.1f, radius * 2.4f, whirl ? GungnirGold * 0.9f : SkillColor * 1.1f,
                0.4f, 0.45f, 1.1f, 40f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
            Vfx.GroundRing(Feet, sweepRadius, SkillColor, 0.35f);
            Shockwave.Spawn(Feet, sweepRadius, SkillColor, whirl ? 20 : 14);
            Vfx.Burst(transform.position + Vector3.up * 0.3f, SkillColor, 12, 7f, 0.14f, 0.6f);
            CameraShake.Shake(0.2f, whirl ? 0.2f : 0.14f);
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy.IsDead)
                    continue;
                Collider c = enemy.GetComponent<Collider>();
                Vector3 off = (c != null ? c.ClosestPoint(transform.position) : enemy.transform.position) - transform.position;
                off.y = 0f;
                if (off.magnitude > sweepRadius + 0.4f)
                    continue;
                Hit(enemy, whirl ? sweepDamage * 1.5f : sweepDamage);
                enemy.Interrupt();
            }
        }
        if (tip != null)
            Destroy(tip, 0.3f);
        yield return new WaitForSeconds(0.15f);
        IsSwinging = false;
        activeSkill = null;
        StartCoroutine(EndPose(0f));
    }

    // An enemy within `radius` dragged in to `stopAt` metres from him over `seconds` (회오리 창격)
    private IEnumerator PullIn(Enemy enemy, float radius, float stopAt, float seconds)
    {
        Vector3 off = enemy.transform.position - transform.position;
        off.y = 0f;
        if (off.magnitude > radius || off.magnitude <= stopAt)
            yield break;
        var cc = enemy.GetComponent<CharacterController>();
        float travel = off.magnitude - stopAt;
        for (float t = 0f; t < seconds && enemy != null && !enemy.IsDead; t += Time.deltaTime)
        {
            Vector3 step = -off.normalized * (travel / seconds) * Time.deltaTime;
            if (cc != null && cc.enabled) cc.Move(step);
            else enemy.transform.position += step;
            yield return null;
        }
    }

    // ── E · 서리창 군열: the spear driven into the ground; a line of spears thrusts up toward the cursor, throwing enemies down ──
    private IEnumerator GroundSpears(Vector3 aimPoint)
    {
        spikesReadyAt = Time.time + spikesCooldown * SpearSkills.CooldownFactor;
        activeSkill = "spikes";
        IsSwinging = true;
        FaceNow(aimPoint);
        // a turn of the body and the spear driven point-down into the ground (the leap-and-stab it used is the combo's
        // 3rd blow now): the clip from 55% to the stab at 75%, reached as the marks run out
        PlayPose("SkillSlam", 2.0f, 1.1f, 0.2f * 2.0f / 0.45f);
        Vector3 dir = transform.forward;
        // 세 갈래 군열: three lines in a fan instead of one
        float[] fan = SpearSkills.Has(SpearSkills.SpikesUp) ? new[] { -25f, 0f, 25f } : new[] { 0f };
        var marks = new List<GameObject>();
        foreach (float a in fan)
            for (float d = 1.5f; d <= spikesLength; d += 1.5f)
                marks.Add(IceVfx.Frosted(Telegraph.Circle(Feet + Quaternion.Euler(0f, a, 0f) * dir * d, 1.1f, 0.45f), SkillColor));
        yield return new WaitForSeconds(0.45f);
        foreach (GameObject m in marks)
            if (m != null) Destroy(m);
        // the spears run on by themselves; he rises from the stab a beat later
        StartCoroutine(RiseFromStab(0.3f));
        // the spear driven into the ground: a crack of force at his feet
        Shockwave.Spawn(Feet + dir * 0.6f, 1.2f, SkillColor, 10);
        Vfx.Dust(Feet + dir * 0.6f, 1f, 8, new Color(0.75f, 0.7f, 0.62f, 0.5f));
        var hit = new HashSet<Enemy>();
        for (float d = 1.5f; d <= spikesLength; d += 1.5f)
        foreach (float a in fan)
        {
            Vector3 spot = Feet + Quaternion.Euler(0f, a, 0f) * dir * d;
            for (int k = 0; k < 3; k++)
                StartCoroutine(ThrustUp(spot + new Vector3(Random.Range(-0.45f, 0.45f), 0f, Random.Range(-0.45f, 0.45f)), k == 0 ? 2f : 1.5f, 15f, 0.7f));
            // where they break out: the ground splits in a ring, earth and splinters burst up, a flash at the points
            Vfx.Sprite("ring", spot + Vector3.up * 0.06f, 2.2f, SkillColor * 1.3f, 0.5f, 0.3f, 1.1f, Random.Range(-40f, 40f), Quaternion.Euler(90f, 0f, 0f));
            Vfx.Dust(spot, 0.9f, 6, new Color(0.72f, 0.66f, 0.58f, 0.5f));
            Vfx.Burst(spot + Vector3.up * 0.4f, new Color(0.85f, 0.75f, 0.55f), 8, 5f, 0.12f, 2f);
            Vfx.Flash(spot + Vector3.up * 1.3f, 1.1f, SkillColor, 0.12f);
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy.IsDead || hit.Contains(enemy))
                    continue;
                Vector3 off = enemy.transform.position - spot;
                off.y = 0f;
                if (off.magnitude > 1.5f)
                    continue;
                hit.Add(enemy);
                Hit(enemy, spikesDamage);
                if (!enemy.IsDead)
                    enemy.Knockdown(spikesKnockdown);
            }
            // (all the lines break out together, a step further each beat)
            if (a == fan[fan.Length - 1])
                yield return new WaitForSeconds(0.06f);
        }
        CameraShake.Shake(0.25f, 0.14f);
        activeSkill = null;
    }

    private IEnumerator RiseFromStab(float after)
    {
        int token = poseToken;
        yield return new WaitForSeconds(after);
        if (token != poseToken)
            yield break;
        IsSwinging = false;
        StartCoroutine(EndPose(0f));
    }

    // ── R · 창우: the spear raised to the sky; a storm of spears falls on the cursor ──
    private IEnumerator SpearRain(Vector3 aimPoint)
    {
        rainReadyAt = Time.time + rainCooldown * SpearSkills.CooldownFactor;
        activeSkill = "rain";
        IsSwinging = true;
        FaceNow(aimPoint);
        Vector3 centre = new Vector3(aimPoint.x, Feet.y, aimPoint.z);
        Vector3 off0 = centre - Feet;
        if (off0.magnitude > 12f)
            centre = Feet + off0.normalized * 12f;
        // the Valkyrie's throw: wound back and hurled, the spear loosed at 63% of the clip (reached at 0.45 s)
        const float release = 0.45f;
        PlayPose("SkillThrow", 3.33f, 0.1f, 0.53f * 3.33f / release);
        GameObject ring = IceVfx.Frosted(Telegraph.Circle(centre, rainRadius, release + 0.3f), SkillColor);
        // the spear raised to the sky: light gathers at its point and a great glow opens over the target
        Vfx.Gather(transform.position + Vector3.up * 1.6f, 2.5f, SkillColor, release, 60f);
        Vfx.Sprite("glow", centre + Vector3.up * 9f, rainRadius * 3f, SkillColor * 0.6f, 1.8f, 0.4f, 1.1f, 0f);
        Vfx.Sprite("runes", centre + Vector3.up * 0.08f, rainRadius * 2.3f, SkillColor * 0.9f, 1.8f, 0.8f, 1.05f, 60f, Quaternion.Euler(90f, 0f, 0f));
        yield return new WaitForSeconds(release);
        // loosed: the spear leaves his hand and streaks up into the sky over the target, and the storm answers it
        // (it comes back to his hand when the last spear has fallen)
        if (heldWeapon != null)
            heldWeapon.SetActive(false);
        StartCoroutine(SkyThrow(transform.position + Vector3.up * 1.6f + transform.forward * 0.4f, centre + Vector3.up * 11f));
        // and the sky answers: three Valkyries of light come down over the target, spears raised
        SummonValkyries(centre);
        CameraShake.Shake(0.12f, 0.08f);
        StartCoroutine(RiseFromStab(0.3f));
        // the first spears are already falling while the ring fills (each takes 0.25 s to come down), so they land
        // as it closes rather than a beat after
        yield return new WaitForSeconds(0.12f);
        // 창우 강화: half again as many spears, and the great spear's burst wider
        bool up = SpearSkills.Has(SpearSkills.RainUp);
        int count = up ? Mathf.RoundToInt(rainSpears * 1.5f) : rainSpears;
        var throwers = valkyries;   // (the Valkyries summoned as he threw, over the target)
        // the opening volley is aimed: one spear onto every enemy standing in the circle, so the first spears to
        // land are the ones that strike; the rest fall at random over it
        var targets = new List<Vector3>();
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead)
                continue;
            Vector3 at = enemy.transform.position;
            at.y = centre.y;
            if ((at - centre).magnitude <= rainRadius + 0.6f)
                targets.Add(at + new Vector3(Random.Range(-0.3f, 0.3f), 0f, Random.Range(-0.3f, 0.3f)));
        }
        for (int i = 0; i < count; i++)
        {
            Vector3 spot;
            if (i < targets.Count)
                spot = targets[i];
            else
            {
                Vector2 r = Random.insideUnitCircle * rainRadius;
                spot = centre + new Vector3(r.x, 0f, r.y);
            }
            // each spear hurled by one of the Valkyries in turn (or out of the sky if they could not be summoned)
            Transform thrower = throwers.Count > 0 ? throwers[i % throwers.Count] : null;
            Vector3? from = thrower != null ? thrower.position + Vector3.up * 1.6f : (Vector3?)null;
            StartCoroutine(FallingSpear(spot, 1f, from));
            if (thrower != null && i % throwers.Count == 0)
                Vfx.Flash(thrower.position + Vector3.up * 1.6f, 1.4f, SkillColor, 0.12f);
            if (i == 2 && ring != null) Destroy(ring);
            yield return new WaitForSeconds(0.07f);
        }
        if (ring != null) Destroy(ring);
        // the finish: the three raise their spears together and a great spear of light comes down on the middle, the
        // ice bursting out round it and everything near thrown down
        yield return new WaitForSeconds(0.2f);
        foreach (Transform v in throwers)
            if (v != null) Vfx.Flash(v.position + Vector3.up * 2f, 2f, GungnirGold, 0.25f);
        BossCutscene.Flash(0f);
        yield return FallingSpear(centre, 2.8f, centre + Vector3.up * 14f);
        float blast = up ? 4.2f : 3.2f;
        CameraShake.Shake(0.45f, 0.26f);
        IceVfx.BlizzardBurst(centre, blast);
        IceVfx.Impact(centre, blast * 0.6f, blast * 1.4f, true);
        IceVfx.IceEruption(centre, blast * 0.6f);
        Shockwave.Spawn(centre, blast, SkillColor, 24);
        Vfx.GroundRing(centre, blast, SkillColor, 0.5f);
        Vfx.Sprite("runes", centre + Vector3.up * 0.08f, blast * 2.6f, GungnirGold * 0.9f, 0.9f, 0.6f, 1.2f, 90f, Quaternion.Euler(90f, 0f, 0f));
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead)
                continue;
            Collider c = enemy.GetComponent<Collider>();
            Vector3 off = (c != null ? c.ClosestPoint(centre + Vector3.up * 0.5f) : enemy.transform.position) - centre;
            off.y = 0f;
            if (off.magnitude > blast)
                continue;
            Hit(enemy, rainDamage * (up ? 3f : 2.2f));
            if (!enemy.IsDead)
                enemy.Knockdown(knockdownSeconds);
        }
        DismissValkyries();
        // his own spear comes back to his hand in a flash of light (unless thrown spears are still out)
        if (heldWeapon != null && spearsOut == 0 && !heldWeapon.activeSelf)
        {
            heldWeapon.SetActive(true);
            Vfx.Flash(heldWeapon.transform.position, 1.2f, SkillColor, 0.18f);
        }
        activeSkill = null;
    }

    // 창우's own spear hurled from his hand up into the sky over the target, a streak of light behind it and a flare
    // where it vanishes into the glow
    private IEnumerator SkyThrow(Vector3 from, Vector3 to)
    {
        Vector3 dir = (to - from).normalized;
        GameObject spear = SpearProp(from, dir, 1.9f);
        Destroy(spear, 1f);
        Vfx.Trail(spear, SkillColor, 0.3f, 0.25f);
        Vfx.Flash(from, 1.2f, SkillColor, 0.14f);
        const float flight = 0.28f;
        for (float t = 0f; t < flight && spear != null; t += Time.deltaTime)
        {
            spear.transform.position = Vector3.Lerp(from, to, t / flight);
            yield return null;
        }
        Vfx.Flash(to, 3f, SkillColor, 0.3f);
        if (spear != null)
            Destroy(spear);
    }

    // ── 발키리 창우's Valkyries: apparitions of light (the Asgard Valkyrie's body, no mind of its own) that hang over
    // the target while the rain falls, each throwing in turn, then fade back into the sky ──
    private readonly List<Transform> valkyries = new List<Transform>();
    private static GameObject valkyrieGhost;
    private static Shader ghostShader;
    private const float ValkyrieHeight = 3.6f;

    private void SummonValkyries(Vector3 centre)
    {
        DismissValkyries();
        if (valkyrieGhost == null)
            valkyrieGhost = Resources.Load<GameObject>("Props/ValkyrieGhost");
        if (valkyrieGhost == null)
            return;
        Vector3 toward = centre - Feet;
        toward.y = 0f;
        toward = toward.sqrMagnitude > 0.01f ? toward.normalized : transform.forward;
        for (int k = 0; k < 3; k++)
        {
            // on the far side of the circle, spread round it, all facing in (and so towards the camera)
            Vector3 side = Quaternion.Euler(0f, (k - 1) * 65f, 0f) * toward;
            Vector3 at = centre + side * rainRadius * 0.85f + Vector3.up * ValkyrieHeight;
            GameObject v = Instantiate(valkyrieGhost, at + Vector3.up * 4f, Quaternion.LookRotation(-side));
            v.transform.localScale *= 1.5f;
            Ghostly(v, k == 1 ? GungnirGold : SkillColor, 0.75f);
            Animator anim = v.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.applyRootMotion = false;
                anim.speed = 1.5f;
                anim.Play("Throw", 0, 0.08f * k);
            }
            valkyries.Add(v.transform);
            Destroy(v, 8f);   // (never left behind, whatever cuts the skill short)
            StartCoroutine(GhostGlide(v.transform, at, 0.3f + 0.06f * k));
        }
    }

    private void DismissValkyries()
    {
        foreach (Transform v in valkyries)
            if (v != null)
                StartCoroutine(GhostFade(v));
        valkyries.Clear();
    }

    // Every material swapped for a glowing, see-through copy of its own texture
    public static void Ghostly(GameObject go, Color tint, float strength)
    {
        if (ghostShader == null)
            ghostShader = Shader.Find("Niflheim/AdditiveNoFog");
        if (ghostShader == null)
            return;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = new Material(ghostShader);
                Material src = mats[i];
                if (src != null && src.HasProperty("_BaseMap"))
                    m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
                else if (src != null && src.HasProperty("_MainTex"))
                    m.SetTexture("_BaseMap", src.GetTexture("_MainTex"));
                m.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, strength));
                mats[i] = m;
            }
            r.materials = mats;
        }
        // (the copies are its own: thrown away with it)
        if (go.GetComponent<OwnedMaterials>() == null)
            go.AddComponent<OwnedMaterials>();
    }

    private static void GhostAlpha(Transform v, float alpha)
    {
        foreach (Renderer r in v.GetComponentsInChildren<Renderer>())
            foreach (Material m in r.materials)
            {
                Color c = m.GetColor("_BaseColor");
                c.a = alpha;
                m.SetColor("_BaseColor", c);
            }
    }

    // Down out of the sky into place, fading in, then a slow hover
    private IEnumerator GhostGlide(Transform v, Vector3 at, float seconds)
    {
        Vector3 start = v.position;
        Vfx.Flash(at + Vector3.up * 1.4f, 2.6f, SkillColor, 0.3f);
        Vfx.Sprite("glow", at + Vector3.up * 1.3f, 4f, SkillColor * 0.5f, 1.6f, 0.5f, 1.2f, 0f);
        for (float t = 0f; t < seconds && v != null; t += Time.deltaTime)
        {
            float k = 1f - Mathf.Pow(1f - t / seconds, 3f);
            v.position = Vector3.Lerp(start, at, k);
            GhostAlpha(v, 0.75f * k);
            yield return null;
        }
        for (float t = 0f; v != null; t += Time.deltaTime)
        {
            v.position = at + Vector3.up * Mathf.Sin(t * 3f) * 0.12f;
            yield return null;
        }
    }

    // Up and out: they rise back into the sky and fade
    private IEnumerator GhostFade(Transform v)
    {
        const float seconds = 0.6f;
        Vector3 start = v.position;
        for (float t = 0f; t < seconds && v != null; t += Time.deltaTime)
        {
            v.position = start + Vector3.up * 3f * (t / seconds) * (t / seconds);
            GhostAlpha(v, 0.75f * (1f - t / seconds));
            yield return null;
        }
        if (v != null)
            Destroy(v.gameObject);
    }

    // One spear dropping point-first onto `spot`, sticking in the ground a moment (`scale`: the great one); out of
    // the sky, or hurled from `origin` (a Valkyrie's hand)
    private IEnumerator FallingSpear(Vector3 spot, float scale, Vector3? origin = null)
    {
        float length = 1.9f * scale;
        Vector3 fall = origin.HasValue
            ? (spot - origin.Value).normalized
            : Quaternion.Euler(Random.Range(-10f, 10f), 0f, Random.Range(-10f, 10f)) * Vector3.down;
        Vector3 from = origin ?? spot - fall * 10f, stuck = spot + fall * length * 0.25f;
        GameObject spear = SpearProp(from, fall, length);
        // a streak of light behind each spear as it comes down, and a glint where it will land
        if (spear != null)
            Vfx.Trail(spear, SkillColor, 0.18f * scale, 0.12f);
        Vfx.Sprite("glow", spot + Vector3.up * 0.05f, 1.2f * scale, SkillColor * 0.8f, 0.25f, 0.4f, 1f, 0f, Quaternion.Euler(90f, 0f, 0f));
        for (float t = 0f; t < 0.25f && spear != null; t += Time.deltaTime)
        {
            spear.transform.position = Vector3.Lerp(from, stuck, t / 0.25f);
            yield return null;
        }
        if (spear != null)
        {
            spear.transform.position = stuck;
            Destroy(spear, 0.9f * scale);
        }
        // the strike: dust and splinters thrown up, a flash, a ring of force through the ground
        Vfx.Dust(spot, 0.7f * scale, 4, new Color(0.75f, 0.7f, 0.62f, 0.45f));
        Vfx.Burst(spot + Vector3.up * 0.3f, new Color(1f, 0.85f, 0.55f), 6, 5f, 0.1f, 2f);
        Vfx.Flash(spot + Vector3.up * 0.4f, 0.9f * scale, SkillColor, 0.1f);
        Vfx.GroundRing(spot, 1f * scale, SkillColor, 0.25f);
        // a spear strikes whatever it lands on or beside: measured to the body's edge, so a big monster is hit where
        // the spear visibly goes into it, not only near its middle
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead)
                continue;
            Collider c = enemy.GetComponent<Collider>();
            Vector3 near = c != null ? c.ClosestPoint(spot + Vector3.up * 0.8f) : enemy.transform.position;
            Vector3 off = near - spot;
            off.y = 0f;
            if (off.magnitude <= 1.5f * Mathf.Max(1f, scale * 0.6f))
                Hit(enemy, rainDamage);
        }
        CameraShake.Shake(0.05f, 0.06f);
    }

    // The dash is his from the start; 질풍 돌진 lets him dash twice before it cools down (a second charge refills with it)
    private int lungeCharges = 1;

    private void Lunge(Vector3 aimPoint)
    {
        if (IsSwinging && !inCombo)
            return;
        int maxCharges = SpearSkills.Has(SpearSkills.DashUp) ? 2 : 1;
        if (Time.time >= lungeReadyAt)
            lungeCharges = maxCharges;
        if (lungeCharges <= 0)
            return;
        // (a dash cuts a combo blow short)
        if (inCombo)
        {
            if (comboRoutine != null)
                StopCoroutine(comboRoutine);
            comboRoutine = null;
            inCombo = false;
            IsSwinging = false;
            pastImpact = false;
            comboQueued = false;
            // (out of the 5th blow's leap: down at once, and the spear back in hand)
            if (jumpRoutine != null)
                StopCoroutine(jumpRoutine);
            jumpRoutine = null;
            jumpLift = jumpTilt = 0f;
            if (heldWeapon != null && spearsOut == 0 && !heldWeapon.activeSelf)
                heldWeapon.SetActive(true);
        }
        lungeCharges--;
        if (lungeCharges == maxCharges - 1)
            lungeReadyAt = Time.time + LungeCooldown;
        StartCoroutine(LungeForward(aimPoint));
    }

    public int LungeChargesLeft => Time.time >= lungeReadyAt ? (SpearSkills.Has(SpearSkills.DashUp) ? 2 : 1) : lungeCharges;

    private IEnumerator LungeForward(Vector3 aimPoint)
    {
        IsSwinging = true;
        Vector3 dir = aimPoint - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(dir);
        poseToken++;
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, 1.6f);
            animator.SetTrigger(AttackHash);
        }
        var hit = new HashSet<Enemy>();
        Shockwave.Spawn(transform.position, 1.2f, StageTwoColor, 8);
        // pushing off: dust kicked back, a streak behind him and one off the spear's point as he goes
        Vfx.Dust(Feet - dir * 0.4f, 1f, 8, new Color(0.78f, 0.74f, 0.66f, 0.5f));
        var body = new GameObject("LungeStreak");
        body.transform.SetParent(transform, false);
        body.transform.localPosition = Vector3.up * 0.1f;
        Vfx.Trail(body, StageTwoColor, 0.9f, 0.2f);
        GameObject point = SpearTipTrail(0.35f, 0.15f);
        for (float t = 0f; t < lungeTime; t += Time.deltaTime)
        {
            controller.Move(dir * (lungeDistance * (SpearSkills.Has(SpearSkills.DashUp) ? 1.3f : 1f) / lungeTime) * Time.deltaTime);
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy.IsDead || hit.Contains(enemy))
                    continue;
                Vector3 offset = enemy.transform.position - transform.position;
                offset.y = 0f;
                if (offset.magnitude > 1.6f)
                    continue;
                hit.Add(enemy);
                Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * lungeDamage)), critical);
            }
            yield return null;
        }
        ThrustStreak(ThrustReach, StageTwoColor);
        // the point driven home at the end of the dash
        Vector3 end = transform.position + Vector3.up * 0.3f + transform.forward * ThrustReach;
        Vfx.Flash(end, 1.4f, StageTwoColor, 0.14f);
        Vfx.Burst(end, StageTwoColor, 10, 6f, 0.13f);
        Vfx.Sprite("ring", end, 1.6f, StageTwoColor * 1.2f, 0.25f, 0.3f, 1.2f, 0f, Quaternion.LookRotation(transform.forward));
        foreach (Enemy enemy in HitLine(ThrustReach, 1f))
            hit.Add(enemy);
        Destroy(body, 0.25f);
        if (point != null)
            Destroy(point, 0.2f);
        yield return new WaitForSeconds(0.15f);
        IsSwinging = false;
        lungeEndedAt = Time.time;
    }

    // ── Charged throw ──

    private IEnumerator Charge()
    {
        GameAudio.Play("charge_hold");   // (power gathering into the spear)
        IsCharging = true;
        IsSwinging = true;
        throwRequested = false;
        int stageShown = 0;
        float speed = AttackSpeed;
        float holdPoint = profile != null ? profile.ChargeHoldPoint : 0.58f;
        float impactPoint = profile != null ? profile.ChargeImpactPoint : 0.78f;
        float swingSeconds = profile != null ? profile.SwingStateSeconds : 0.93f;
        poseToken++;
        if (animator != null)
        {
            animator.SetFloat(AttackSpeedHash, speed);
            animator.SetFloat(ChargeSpeedHash, raiseSpeed);
            animator.SetTrigger(ChargeHash);
        }

        // Draw the spear back slowly, then hold it there until the button is released
        while (!throwRequested)
        {
            Face(chargeAim, 540f);
            if (animator != null)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("Charge") && state.normalizedTime >= holdPoint)
                    animator.SetFloat(ChargeSpeedHash, 0f);
            }
            int stage = ChargeStage;
            if (stage > stageShown)
            {
                stageShown = stage;
                Shockwave.Spawn(transform.position, 1.2f, stage == 2 ? StageTwoColor : StageOneColor, 10);
            }
            yield return null;
        }

        // Release: finish the motion from where it is and let fly at the moment of impact
        int finalStage = ChargeStage;
        bool throwing = CanThrow;
        Face(chargeAim, 9999f);
        float progress = 0f;
        if (animator != null)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            progress = state.IsName("Charge") ? Mathf.Min(state.normalizedTime, impactPoint) : 0f;
            animator.SetFloat(ChargeSpeedHash, speed);
        }
        float stateSeconds = swingSeconds / speed;
        yield return new WaitForSeconds((impactPoint - progress) * stateSeconds);
        if (throwing)
            ThrowSpears(finalStage);
        else
            LandThrust(stageDamage[finalStage], finalStage == 2);
        yield return new WaitForSeconds((1f - impactPoint) * stateSeconds * 0.8f);

        IsCharging = false;
        IsSwinging = false;
        chargeRoutine = null;
    }

    // 창 투척: the spear flies through a line of enemies; 궁니르의 비 adds two more at full charge,
    // 귀환의 창 brings them back, 꿰뚫기 knocks down whatever a full-charge throw goes through
    private void ThrowSpears(int stage)
    {
        bool full = stage == 2;
        bool awakened = SpearSkills.Awakened;
        // 궁니르: the awakened spear always comes back to his hand
        bool recall = SpearSkills.Has(SpearSkills.Recall) || awakened;
        bool knockdown = full && SpearSkills.Has(SpearSkills.Impale);
        int count = full && SpearSkills.Has(SpearSkills.Gungnir) ? 3 : 1;
        // 투척 강화: harder and further
        bool strong = SpearSkills.Has(SpearSkills.ThrowUp);
        float multiplier = stageDamage[stage] * (strong ? 1.3f : 1f);
        float range = throwRange[stage] * (strong ? 1.25f : 1f);
        // 오딘의 까마귀 (and 궁니르, which never misses): each spear turns toward the nearest enemy ahead
        bool seek = SpearSkills.Has(SpearSkills.Ravens) || awakened;
        CameraShake.Shake(full ? 0.2f : 0.1f, full ? 0.15f : 0.08f);
        if (awakened)
        {
            Vfx.Flash(transform.position + Vector3.up * 1.3f + transform.forward * 0.8f, 2.4f, GungnirGold, 0.2f);
            Shockwave.Spawn(transform.position, 1.6f, GungnirGold, 12);
        }

        var claimed = new HashSet<Enemy>();
        for (int i = 0; i < count; i++)
        {
            float angle = (i - (count - 1) / 2f) * gungnirSpread;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * transform.forward;
            if (seek)
            {
                Enemy prey = NearestAhead(dir, range, awakened ? 70f : 40f, claimed);
                if (prey != null)
                {
                    claimed.Add(prey);
                    Vector3 to = prey.transform.position - transform.position;
                    to.y = 0f;
                    dir = to.normalized;
                }
            }
            Vector3 from = transform.position + Vector3.up * 1.3f + dir * 0.6f;
            ThrownSpear thrown = ThrownSpear.Launch(transform, from, dir, range, recall, (enemy, back) =>
            {
                float m = back ? recallDamage * multiplier : multiplier;
                Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * m)), critical);
                if (knockdown && !back && !enemy.IsDead)
                    enemy.Knockdown(knockdownSeconds);
                // 속박 투척: whatever the spear strikes is pinned in place for a second
                if (!back && SpearSkills.Has(SpearSkills.FrostThrow))
                {
                    if (!enemy.IsDead && !knockdown)
                        enemy.Knockdown(1f);
                }
            });
            spearsOut++;
            thrown.Finished += () =>
            {
                spearsOut = Mathf.Max(0, spearsOut - 1);
                if (spearsOut == 0 && heldWeapon != null)
                    heldWeapon.SetActive(true);
            };
        }
        if (heldWeapon != null)
            heldWeapon.SetActive(false);
    }

    // The nearest living enemy within `range` and `halfAngle` degrees of `dir` (one not already taken by another spear)
    private Enemy NearestAhead(Vector3 dir, float range, float halfAngle, HashSet<Enemy> skip)
    {
        Enemy best = null;
        float bestDistance = float.MaxValue;
        foreach (Enemy enemy in Enemy.Alive)
        {
            if (enemy == null || enemy.IsDead || skip.Contains(enemy))
                continue;
            Vector3 to = enemy.transform.position - transform.position;
            to.y = 0f;
            float d = to.magnitude;
            if (d > range || d < 0.1f || Vector3.Angle(dir, to) > halfAngle)
                continue;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = enemy;
            }
        }
        return best;
    }

    // ── ★ 궁니르: the awakened spear ──
    private static readonly Color GungnirGold = new Color(1f, 0.82f, 0.35f);
    private GameObject gungnirAura;
    private bool gungnirShown;
    private readonly Dictionary<Renderer, Material[]> spearOriginals = new Dictionary<Renderer, Material[]>();

    // The spear in his hand turns to Odin's: a gold sheen and glow on it, motes of light shed as it moves, a light
    // of its own; taken away again if the tree is reset
    private void UpdateGungnir()
    {
        bool awake = SpearSkills.Awakened && IsActive && heldWeapon != null;
        if (awake == gungnirShown)
            return;
        gungnirShown = awake;
        if (heldWeapon == null)
            return;
        // the spear gets gold materials of its own (its parts share materials with his body, which must stay as it is);
        // the originals are kept to put back
        foreach (Renderer r in heldWeapon.GetComponentsInChildren<Renderer>(true))
        {
            if (awake)
            {
                if (!spearOriginals.ContainsKey(r))
                    spearOriginals[r] = r.sharedMaterials;
                var gold = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < gold.Length; i++)
                {
                    Material src = r.sharedMaterials[i];
                    // Odin's spear keeps its own look - steel blade, dark shaft, gold bands - burnished and lit from
                    // within: the texture itself glows (so the gold and the steel shine, the dark wood stays dark), a
                    // faint warmth over the whole, and a polished sheen. (A flat gold tint and glow over everything had
                    // made it one yellow stick.)
                    gold[i] = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    gold[i].name = "Gungnir_" + (src != null ? src.name : "part");
                    gold[i].SetColor("_BaseColor", new Color(1f, 0.96f, 0.88f));
                    gold[i].SetFloat("_Metallic", 0.75f);
                    gold[i].SetFloat("_Smoothness", 0.72f);
                    Texture baseMap = src != null && src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
                    if (baseMap != null)
                        gold[i].SetTexture("_EmissionMap", baseMap);
                    gold[i].EnableKeyword("_EMISSION");
                    gold[i].SetColor("_EmissionColor", baseMap != null ? new Color(0.55f, 0.47f, 0.3f) : GungnirGold * 0.3f);
                }
                r.sharedMaterials = gold;
            }
            else if (spearOriginals.TryGetValue(r, out Material[] original))
                r.sharedMaterials = original;
        }
        if (!awake)
            spearOriginals.Clear();
        if (awake)
        {
            gungnirAura = new GameObject("GungnirAura");
            gungnirAura.transform.SetParent(heldWeapon.transform, false);
            Vfx.Sparkle(gungnirAura, GungnirGold, 10f, 0.16f);
            // (no light of its own: at his size any light on the spear gilds the whole of him; the spear's glow is enough)
            Vfx.Flash(heldWeapon.transform.position, 3f, GungnirGold, 0.4f);
            Shockwave.Spawn(transform.position, 3f, GungnirGold, 24);
            DualBlades.Say(transform.position + Vector3.up * 2.2f, "궁니르 각성!", GungnirGold);
        }
        else if (gungnirAura != null)
            Destroy(gungnirAura);
    }

    // A glow at the feet while charging (dim, then orange, then ice-blue at full charge),
    // and with 창 투척 a line on the ground showing how far the throw would fly
    private void UpdateChargeFx()
    {
        if (!IsCharging)
        {
            if (chargeGlow != null)
                chargeGlow.gameObject.SetActive(false);
            if (aimLine != null)
                aimLine.gameObject.SetActive(false);
            return;
        }
        if (chargeGlow == null)
            chargeGlow = Mats.Shape(PrimitiveType.Cylinder, null, Vector3.zero, Vector3.one, null, false, "ChargeGlow").transform;
        if (aimLine == null)
            aimLine = Mats.Shape(PrimitiveType.Cube, null, Vector3.zero, Vector3.one, null, false, "ThrowAimLine").transform;

        int stage = ChargeStage;
        Color color = stage == 2 ? StageTwoColor : stage == 1 ? StageOneColor : new Color(0.45f, 0.3f, 0.15f);
        Material glow = Mats.Get("charge_glow_" + stage, color, 0.5f, color * (stage == 0 ? 0.6f : 2f));

        if (!throwRequested)
        {
            bool throwing = CanThrow;
            ThrustFx.Charging(throwing ? 0f : ThrustReach, color, 0f, 0f);
            aimLine.gameObject.SetActive(throwing);
            if (throwing)
            {
                float range = throwRange[stage];
                aimLine.GetComponent<Renderer>().sharedMaterial = glow;
                aimLine.SetPositionAndRotation(new Vector3(transform.position.x, transform.position.y - 0.97f, transform.position.z) + transform.forward * range / 2f, transform.rotation);
                aimLine.localScale = new Vector3(0.12f, 0.005f, range);
            }
        }
        else
            aimLine.gameObject.SetActive(false);

        chargeGlow.GetComponent<Renderer>().sharedMaterial = glow;
        chargeGlow.gameObject.SetActive(true);
        float pulse = 1.4f + stage * 0.35f + Mathf.Sin(Time.time * (6f + stage * 4f)) * 0.08f;
        chargeGlow.position = new Vector3(transform.position.x, transform.position.y - 0.98f, transform.position.z);
        chargeGlow.localScale = new Vector3(pulse, 0.005f, pulse);
    }

    // The two-step gauge over the head while the throw is charged (the Niflheim HUD's)
    public override void DrawHud()
    {
        DrawSkillBar();
        Camera cam = Camera.main;
        if (cam == null || !IsCharging)
            return;
        Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.6f);
        if (screen.z <= 0f)
            return;
        const float width = 64f, height = 8f;
        Rect bar = new Rect(screen.x - width / 2f, Screen.height - screen.y - 24f, width, height);
        UiFont.Fill(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f), new Color(0f, 0f, 0f, 0.7f));
        int stage = ChargeStage;
        Color color = stage == 2 ? StageTwoColor : stage == 1 ? StageOneColor : new Color(0.75f, 0.7f, 0.6f);
        UiFont.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(ChargeHeld / stageTwoTime), bar.height), color);
        float tick = bar.x + bar.width * stageOneTime / stageTwoTime;
        UiFont.Fill(new Rect(tick - 1f, bar.y - 2f, 2f, bar.height + 4f), Color.white);
        if (stage > 0)
            GUI.Label(new Rect(bar.x - 20f, bar.y - 20f, bar.width + 40f, 18f), stage == 2 ? "최대 차지!" : "차지 1단계",
                UiFont.Style(12, TextAnchor.MiddleCenter, FontStyle.Bold));
    }

    // ── The skill bar: three icon slots (Space, right-click, R) at the foot of the screen, each darkening from the top
    // while it cools down with the seconds left over it, glowing ice-blue while its skill plays, and greyed until
    // its skill is learned in the [K] tree ──
    private static Texture2D[] icons;

    private void DrawSkillBar()
    {
        if (icons == null)
            icons = new[] { "Lunge", "Pierce", "Sweep", "Erupt", "Rain" }.Select(n => Resources.Load<Texture2D>("UI/SpearIcons/" + n)).ToArray();
        if (SkillBar.Hidden)
            return;
        bool sweep = SpearSkills.Has(SpearSkills.Sweep), spikes = SpearSkills.Has(SpearSkills.Spikes);
        bool pierce = SpearSkills.Has(SpearSkills.Pierce), rain = SpearSkills.Has(SpearSkills.Rain);
        float cd = SpearSkills.CooldownFactor;
        Color lit = SpearSkills.Awakened ? GungnirGold : new Color(1f, 0.85f, 0.5f);
        int charges = LungeChargesLeft, maxCharges = SpearSkills.Has(SpearSkills.DashUp) ? 2 : 1;
        // (the twin blades' order: Q E R, right click, Space; the dash is his from the start)
        barSlots[0] = new SkillBar.Slot { key = "Q", name = SpearSkills.Has(SpearSkills.SweepUp) ? "회오리 창격" : "회전 창격", icon = icons[2], learned = sweep, left = SweepCooldownLeft, total = sweepCooldown * cd, lit = activeSkill == "sweep", litColor = lit };
        barSlots[1] = new SkillBar.Slot { key = "E", name = SpearSkills.Has(SpearSkills.SpikesUp) ? "세 갈래 군열" : "서리창 군열", icon = icons[3], learned = spikes, left = SpikesCooldownLeft, total = spikesCooldown * cd, lit = activeSkill == "spikes", litColor = lit };
        barSlots[2] = new SkillBar.Slot { key = "R", name = "발키리 창우", icon = icons[4], learned = rain, left = RainCooldownLeft, total = rainCooldown * cd, lit = activeSkill == "rain", litColor = lit };
        barSlots[3] = new SkillBar.Slot { key = "우클릭", name = "서리의 갈고리", icon = icons[1], learned = pierce, left = PierceCooldownLeft, total = hookCooldown * cd, lit = activeSkill == "pierce", litColor = lit };
        barSlots[4] = new SkillBar.Slot { key = "Space", name = SpearSkills.Has(SpearSkills.DashUp) ? "질풍 돌진" : "돌진 찌르기", icon = icons[0], learned = true,
            left = charges > 0 ? 0f : LungeCooldownLeft, total = LungeCooldown, note = maxCharges > 1 ? charges + " / " + maxCharges : null };
        SkillBar.Draw(barSlots);
    }

    private readonly SkillBar.Slot[] barSlots = new SkillBar.Slot[5];

    private void Face(Vector3 point, float degreesPerSecond)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), degreesPerSecond * Time.deltaTime);
    }

    private IEnumerator Swing(Vector3 aimPoint, float impact, float duration, System.Action onImpact)
    {
        IsSwinging = true;
        Vector3 toTarget = aimPoint - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(toTarget, Vector3.up);
        if (animator != null)
            animator.SetTrigger(AttackHash);
        yield return new WaitForSeconds(impact);
        onImpact();
        yield return new WaitForSeconds(Mathf.Max(0f, duration - impact));
        IsSwinging = false;
    }
}
