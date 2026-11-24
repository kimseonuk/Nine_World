using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

// 울르의 활 (울르의 사냥꾼): Niflheim's second weapon, the bow of Ullr, god of winter and the hunt. The model, rig and
// motions were made in VARCO (flow "니플헤임 작업"): the bow's shot, the shot into the sky, the roll and shot, the dodge.
//
// Click: the five-shot combo "울르의 사냥" - 1 견제 사격 (one arrow) · 2 연속 사격 (two, quick) · 3 삼연 확산 (a fan of
// three) · 4 서리 관통시 (a short draw, an arrow through the whole line) · 5 울르의 일격 (a great arrow loosed into the sky
// that falls where he aimed in a burst of frost, throwing them down). Hold: 정조준 사격, the string drawn through two
// stages and loosed. Right-click 사냥꾼의 표식 · Q 구르며 쏘기 · E 서리 덫 · R 스카디의 겨울비. Space is the game's dash.
// Everything but the first shot is learned in UllrSkills ([K]).
public class UllrBow : ArmoryWeapon
{
    private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
    // (the bow is of the wind now: its arrows, trails and bursts jade-green, not the old ice-blue - the name kept for
    // the many places that use it)
    public static readonly Color Frost = new Color(0.5f, 1f, 0.74f);
    private static readonly Color MarkColor = new Color(0.8f, 1f, 0.55f);
    private static readonly Color UllrGold = new Color(1f, 0.86f, 0.5f);
    private static readonly Color StageOneColor = new Color(0.45f, 0.95f, 0.7f);
    private static readonly Color StageTwoColor = new Color(0.82f, 1f, 0.9f);

    // the clips (seconds): the shot draws from 1.45, is at full draw by 2.3 and looses at 2.95; the sky shot looses at
    // 10.3; the roll is over by 1.3 and its shot looses at 2.95
    private const float ShootLen = 5.07f, ShootRelease = 2.95f, UpLen = 14.43f, UpRelease = 10.3f, RollLen = 5.07f, DodgeLen = 5.07f;

    public override CharacterProfile.WeaponKind Kind => CharacterProfile.WeaponKind.Bow;
    public override SkillTree Tree => UllrSkills.Tree;
    public override string TreeName => "울르의 활";
    public override bool Rooted => IsSwinging;
    public override bool Invulnerable => rolling;
    public override string Hint => "[클릭] 연속 사격 [우클릭·Q·E·R] 기술 [Space] 회피";

    [Header("Shots")]
    [SerializeField] private float arrowRange = 14f;
    [SerializeField] private float arrowSpeed = 34f;
    [SerializeField] private float[] chargeDamage = { 1.4f, 2.4f, 3.6f };
    [SerializeField] private float[] chargeRange = { 12f, 16f, 20f };
    [SerializeField] private float chargeDelay = 0.22f, stageOneTime = 0.6f, stageTwoTime = 1.3f;

    [Header("Skills")]
    [SerializeField] private float rollCooldown = 7f, rollDistance = 3.5f;
    [SerializeField] private float trapCooldown = 10f, trapRadius = 3f;
    [SerializeField] private float rainCooldown = 18f, rainRadius = 4.5f;

    public bool IsSwinging { get; private set; }
    public bool IsCharging { get; private set; }
    public int ComboIndex => comboStep;

    private GameObject heldWeapon;
    private bool rolling;
    private string activeSkill;
    private float markReadyAt, rollReadyAt, trapReadyAt, rainReadyAt;
    public float MarkCooldownLeft => Mathf.Max(0f, markReadyAt - Time.time);
    public float RollCooldownLeft => Mathf.Max(0f, rollReadyAt - Time.time);
    public float TrapCooldownLeft => Mathf.Max(0f, trapReadyAt - Time.time);
    public float RainCooldownLeft => Mathf.Max(0f, rainReadyAt - Time.time);

    private ClickToDig aim;
    private Vector3 Feet => new Vector3(transform.position.x, transform.position.y - 0.97f, transform.position.z);
    private float Range => arrowRange + RunState.Stats.rangeBonus * 4f;

    protected override void Equipped(bool on)
    {
        if (heldWeapon != null)
        {
            heldWeapon.SetActive(true);
            heldWeapon.transform.localRotation = shootGrip;
        }
        heldWeapon = on ? profile.HeldWeapon : null;
        if (heldWeapon != null)
        {
            shootGrip = heldWeapon.transform.localRotation;
            carry = 1f;
        }
    }

    // ── The bow in his hand: the grip it is set at fits the draw (upright, its belly toward the target); while he walks,
    // runs or stands the hand swings round and the same grip left the bow sticking out sideways like a pole, so out of
    // a shot it is eased upright at his side, pointing the way he faces ──
    private Quaternion shootGrip = Quaternion.identity;
    private float carry = 1f;   // 1: carried at his side, 0: in the draw

    private void LateUpdate()
    {
        ApplyJumpLift();
        if (!IsActive || heldWeapon == null)
            return;
        // (mining a block plays the draw too, through the game's own swing)
        bool shooting = IsSwinging || IsCharging || rolling || (miner != null && miner.IsSwinging);
        carry = Mathf.MoveTowards(carry, shooting ? 0f : 1f, Time.deltaTime * (shooting ? 12f : 5f));
        Transform hand = heldWeapon.transform.parent;
        Quaternion draw = hand.rotation * shootGrip;
        // carried: upright, tipped a little forward, its belly toward where he faces
        Quaternion side = Quaternion.LookRotation(transform.forward, Vector3.up) * Quaternion.Euler(12f, 0f, 0f);
        heldWeapon.transform.rotation = Quaternion.Slerp(draw, side, Mathf.SmoothStep(0f, 1f, carry));
    }

    // ── The motions: a clip, or the stretch of it that matters, at its own pace; then back to standing ──
    private int poseToken;

    // blend: seconds the new motion takes over the old one (a snap of 0.06 s read as a pop from standing into the draw)
    private void PlayPose(string state, float clipLength, float fromSeconds, float speed, float blend = 0.12f)
    {
        poseToken++;
        if (animator == null)
            return;
        animator.SetFloat(AttackSpeedHash, speed);
        float current = animator.GetCurrentAnimatorStateInfo(0).length;
        animator.CrossFade(state, blend / Mathf.Max(0.06f, current), 0, fromSeconds / clipLength);
    }

    // back to standing/walking, eased out over a quarter second so the bow comes down instead of snapping away
    private IEnumerator EndPose(float after)
    {
        int token = poseToken;
        if (after > 0f)
            yield return new WaitForSeconds(after);
        if (token == poseToken && !IsCharging && animator != null && animator.isActiveAndEnabled)
        {
            animator.SetFloat(AttackSpeedHash, 1f);
            animator.CrossFadeInFixedTime("Locomotion", 0.25f);
        }
    }

    // Turns toward the aim over the wind-up (quick, but a turn rather than a jump), exact by the moment of the loose
    private IEnumerator TurnTo(Vector3 point, float seconds)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f)
            yield break;
        Quaternion from = transform.rotation, target = Quaternion.LookRotation(to);
        float turn = Mathf.Max(0.05f, Mathf.Min(seconds, Quaternion.Angle(from, target) / 1080f));
        for (float t = 0f; t < turn; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(from, target, Mathf.SmoothStep(0f, 1f, t / turn));
            yield return null;
        }
        transform.rotation = target;
    }

    private void FaceNow(Vector3 point)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(to);
    }

    private void Face(Vector3 point, float degreesPerSecond)
    {
        Vector3 to = point - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), degreesPerSecond * Time.deltaTime);
    }

    private Vector3 Cursor()
    {
        if (aim == null)
            aim = FindAnyObjectByType<ClickToDig>();
        return aim != null ? aim.CursorGroundPoint() : transform.position + transform.forward;
    }

    // Where an arrow leaves the bow: the bow in his hand, at chest height, never behind him
    private Vector3 ArrowFrom()
    {
        Vector3 at = transform.position + transform.forward * 0.55f + Vector3.up * 0.25f;
        if (heldWeapon != null && heldWeapon.activeInHierarchy)
        {
            Vector3 bow = heldWeapon.transform.position;
            Vector3 flat = bow - transform.position;
            flat.y = 0f;
            if (Vector3.Dot(flat, transform.forward) > 0.1f)
                at = new Vector3(bow.x, transform.position.y + 0.25f, bow.z);
        }
        return at;
    }

    // ── Damage: the mark makes his arrows hit harder ──
    private readonly Dictionary<Enemy, float> marked = new Dictionary<Enemy, float>();
    private readonly Dictionary<Enemy, GameObject> markGlows = new Dictionary<Enemy, GameObject>();

    private float MarkBonus(Enemy enemy) =>
        marked.TryGetValue(enemy, out float until) && Time.time < until ? (UllrSkills.Has(UllrSkills.MarkUp) ? 1.5f : 1.3f) : 1f;

    private void Hit(Enemy enemy, float multiplier)
    {
        if (enemy == null || enemy.IsDead)
            return;
        multiplier *= MarkBonus(enemy);
        Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(RollDamage(out bool critical) * multiplier)), critical);
    }

    private void MarkEnemy(Enemy enemy)
    {
        if (enemy == null || enemy.IsDead)
            return;
        float seconds = UllrSkills.Has(UllrSkills.MarkUp) ? 9f : 6f;
        marked[enemy] = Time.time + seconds;
        if (markGlows.TryGetValue(enemy, out GameObject old) && old != null)
            Destroy(old);
        float h = 2.2f;
        Collider c = enemy.GetComponent<Collider>();
        if (c != null)
            h = c.bounds.max.y - enemy.transform.position.y + 0.5f;
        GameObject glow = Vfx.Glow(enemy.transform, Vector3.up * h, 1.1f, MarkColor, Vfx.Pick("executemark", "runes"), 60f);
        markGlows[enemy] = glow;
        Destroy(glow, seconds);
        Vfx.Flash(enemy.transform.position + Vector3.up * h, 1.4f, MarkColor, 0.2f);
    }

    // ── Input: tap = the combo's next shot; hold (once 정조준 사격 is learned) = draw and loose ──
    private bool pressing, releaseRequested;
    private float pressStart, heldAtRelease;
    private Vector3 chargeAim;

    public float ChargeHeld => pressing ? Time.time - pressStart - chargeDelay : IsCharging ? heldAtRelease : 0f;
    public int ChargeStage => !IsCharging ? 0 : ChargeHeld >= stageTwoTime ? 2 : ChargeHeld >= stageOneTime ? 1 : 0;

    // a click looses the combo's next shot; holding the button keeps the combo going (the hold-to-draw 정조준 사격 was
    // taken out)
    public override void Press(Vector3 aimPoint)
    {
        if (IsSwinging)
        {
            if (inCombo && activeSkill == null)
            {
                comboQueued = true;
                queuedAim = aimPoint;
            }
            return;
        }
        Shoot(aimPoint);
    }

    public override void Hold(Vector3 aimPoint)
    {
        if (IsSwinging && inCombo && activeSkill == null)
        {
            comboQueued = true;
            queuedAim = aimPoint;
        }
        else if (!IsSwinging)
            Shoot(aimPoint);
    }

    public override void Release(Vector3 aimPoint)
    {
        pressing = false;
    }

    public override void ResetCombat()
    {
        StopAllCoroutines();
        chargeRoutine = null;
        jumpLift = jumpTilt = 0f;
        if (hawk != null)
            Destroy(hawk.gameObject);
        hawk = null;
        IsCharging = IsSwinging = rolling = false;
        pressing = releaseRequested = false;
        comboStep = 0;
        comboQueued = inCombo = false;
        activeSkill = null;
        foreach (UllrTrap trap in traps)
            if (trap != null) Destroy(trap.gameObject);
        traps.Clear();
        foreach (GameObject g in markGlows.Values)
            if (g != null) Destroy(g);
        markGlows.Clear();
        marked.Clear();
        if (aimLine != null)
            aimLine.gameObject.SetActive(false);
        if (chargeGlow != null)
        {
            Destroy(chargeGlow.gameObject);
            chargeGlow = null;
        }
        if (animator != null && animator.isActiveAndEnabled)
        {
            animator.SetFloat(AttackSpeedHash, 1f);
            animator.CrossFade("Locomotion", 0.1f);
        }
    }

    public override void OnHurt(float damage)
    {
        // a hit while drawing breaks the draw
        if (!IsCharging || releaseRequested)
            return;
        if (chargeRoutine != null)
            StopCoroutine(chargeRoutine);
        chargeRoutine = null;
        IsCharging = IsSwinging = false;
        pressing = false;
        StartCoroutine(EndPose(0f));
        DualBlades.Say(transform.position + Vector3.up * 2f, "경직!", new Color(1f, 0.5f, 0.4f));
    }

    // ── The combo: 울르의 사냥 ──
    private enum ShotKind { Single, Double, Fan, Pierce, Sky }
    private struct ComboShot
    {
        public string state;
        public float length, start, release, end, speed;
        public ShotKind kind;
        public float back;                          // metres stepped back over the wind-up
        public string pre;                          // a motion played first (the dodge's twist), then the shot
        public float preLen, preFrom, preTo;
        public float jump;                          // metres he leaps up, loosing at the top
    }

    // Each shot its own motion — 1: the quick standing shot; 2: dropped to one knee (the roll-shot's kneel), two
    // arrows; 3: a twisting hop back (the dodge) straight into the draw, the fan loosed as he lands; 4: a half step
    // back into the full, slow draw, and the piercing arrow; 5: the shot into the sky
    private static readonly ComboShot[] Combo =
    {
        new ComboShot { state = "Shoot", length = ShootLen, start = 2.35f, release = ShootRelease, end = 3.3f, speed = 2.4f, kind = ShotKind.Single },
        new ComboShot { state = "RollShoot", length = RollLen, start = 2.15f, release = 2.95f, end = 3.2f, speed = 2.6f, kind = ShotKind.Double },
        new ComboShot { state = "Shoot", length = ShootLen, start = 2.55f, release = ShootRelease, end = 3.35f, speed = 2.2f, kind = ShotKind.Fan,
            back = 1.3f, pre = "Dodge", preLen = DodgeLen, preFrom = 0.05f, preTo = 0.5f },
        new ComboShot { state = "Shoot", length = ShootLen, start = 1.45f, release = ShootRelease, end = 3.3f, speed = 2.7f, kind = ShotKind.Pierce, back = 0.5f },
        new ComboShot { state = "ShootUp", length = UpLen, start = 8.6f, release = UpRelease, end = 10.9f, speed = 2.8f, kind = ShotKind.Sky },
    };
    private const float ComboResetAfter = 0.9f;
    private int comboStep;
    private bool comboQueued, inCombo;
    private Vector3 queuedAim;
    private float lastShot = -10f;
    private Coroutine comboRoutine, chargeRoutine;

    private void Shoot(Vector3 aimPoint)
    {
        if (IsSwinging)
            return;
        if (Time.time - lastShot > ComboResetAfter || comboStep >= UllrSkills.ComboLength)
            comboStep = 0;
        comboRoutine = StartCoroutine(ComboShotRoutine(aimPoint));
    }

    private IEnumerator ComboShotRoutine(Vector3 aimPoint)
    {
        ComboShot s = Combo[comboStep];
        float rate = s.speed * AttackSpeed;
        // the first shot of a string lifts the bow from the start of the draw; a shot that follows straight on from the
        // last one nocks the next arrow from the half-drawn pose, with a short blend so it reads as one motion
        bool chained = Time.time - lastShot < 0.35f && s.state == "Shoot";
        float start = s.start;
        if (!chained && s.kind == ShotKind.Single)
            start = Mathf.Min(s.start, 2.0f);
        IsSwinging = true;
        inCombo = true;
        comboQueued = false;
        float releaseAt = (s.release - start) / rate, endAt = (s.end - start) / rate;
        if (s.pre != null)
        {
            // the lead-in motion first (turning toward the aim through it), stepping back, then on into the draw
            float preTime = (s.preTo - s.preFrom) / rate;
            PlayPose(s.pre, s.preLen, s.preFrom, rate, 0.08f);
            StartCoroutine(TurnTo(aimPoint, preTime));
            yield return StepBack(s.back, preTime);
            s.back = 0f;
            PlayPose(s.state, s.length, start, rate, 0.1f);
        }
        else
        {
            PlayPose(s.state, s.length, start, rate, chained ? 0.08f : 0.14f);
            StartCoroutine(TurnTo(aimPoint, releaseAt * 0.7f));
        }
        if (s.kind == ShotKind.Double)
        {
            // dropped to one knee: a puff of snow where he goes down
            Vfx.Dust(Feet, 0.5f, 3, new Color(0.85f, 0.9f, 1f, 0.5f));
        }
        float landAt = 0f;
        if (s.jump > 0f)
        {
            // the leap: up over the draw, back as he rises, and down fast after the loose
            const float fall = 0.3f;
            landAt = releaseAt + fall;
            StartCoroutine(JumpArc(s.jump, releaseAt, fall));
            StartCoroutine(StepBack(s.back, landAt));
            s.back = 0f;
            endAt = Mathf.Max(endAt, landAt + 0.12f);
        }
        if (s.back > 0f)
            StartCoroutine(StepBack(s.back, releaseAt * 0.6f));
        bool awakened = UllrSkills.Awakened;
        Vector3 skyTarget = aimPoint;

        if (s.kind == ShotKind.Pierce)
        {
            // the short draw: frost gathers on the arrowhead and at his feet
            Vfx.Gather(ArrowFrom(), 1.4f, Frost, releaseAt, 40f);
            Vfx.GroundRing(Feet, 1.2f, Frost, releaseAt + 0.1f, "runes");
        }
        if (s.kind == ShotKind.Sky)
        {
            // a half step back as he draws toward the sky
            Vfx.Gather(transform.position + Vector3.up * 1.4f, 1.6f, WindColor, releaseAt, 50f);
            Vfx.Sprite(Vfx.Pick("gs_whirlwind", "ring"), Feet + Vector3.up * 0.08f, 2.2f, WindColor * 0.8f, releaseAt + 0.2f, 0.5f, 1f, 500f, Quaternion.Euler(90f, 0f, 0f));
            for (float t = 0f; t < releaseAt; t += Time.deltaTime)
            {
                controller.Move(-transform.forward * (0.8f / releaseAt) * Time.deltaTime);
                yield return null;
            }
        }
        else
            yield return new WaitForSeconds(releaseAt);

        Vector3 dir = transform.forward;
        bool pierceAll = awakened;
        switch (s.kind)
        {
            case ShotKind.Single:
                Loose(dir, 1f, 1f, pierceAll);
                WindShotFx(dir, 3.4f, 1f);
                break;
            case ShotKind.Double:
                Loose(dir, 0.65f, 1f, pierceAll);
                WindShotFx(dir, 3f, 0.9f);
                yield return new WaitForSeconds(0.12f / AttackSpeed);
                Loose(transform.forward, 0.65f, 1f, pierceAll);
                WindShotFx(transform.forward, 3f, 0.9f);
                break;
            case ShotKind.Fan:
            {
                bool volley = UllrSkills.Has(UllrSkills.Volley);
                int n = volley ? 5 : 3;
                float spread = volley ? 14f : 15f;
                for (int i = 0; i < n; i++)
                    Loose(Quaternion.Euler(0f, (i - (n - 1) / 2f) * spread, 0f) * dir, 0.6f, 1f, pierceAll);
                // the fan of wind arrows laid out ahead
                Vfx.Sprite(Vfx.Pick("wind_fan", "gs_wave"), Feet + Vector3.up * 0.25f + dir * 2.4f, 5f, Frost * 1.4f, 0.28f, 0.7f, 1.15f, 0f,
                    Quaternion.LookRotation(Vector3.down, dir));
                break;
            }
            case ShotKind.Pierce:
                // 돌풍 화살촉: where it stops, a gust bursts
                Loose(dir, 1.8f, 1.5f, true, StageTwoColor, -1f,
                    UllrSkills.Has(UllrSkills.ChargeUp) ? (System.Action<Vector3>)(at => FrostBurst(at, 2.5f, 1.5f)) : null);
                WindShotFx(dir, 5.5f, 1.5f);
                Vfx.Sprite(Vfx.Pick("wind_burst", "gs_wave"), Feet + Vector3.up * 0.1f, 2.4f, Frost * 1.3f, 0.3f, 0.4f, 1.1f, 160f, Quaternion.Euler(90f, 0f, 0f));
                if (s.jump > 0f)
                {
                    // loosed from the top of the leap: a flare at the bow and a streak down to where the arrow flies
                    Vector3 bowAt = heldWeapon != null ? heldWeapon.transform.position : ArrowFrom() + Vector3.up * s.jump;
                    Vfx.Flash(bowAt, 2.2f, StageTwoColor, 0.16f);
                    Vfx.Burst(bowAt, StageTwoColor, 10, 4f, 0.1f, 1.4f);
                }
                else
                    Shockwave.Spawn(Feet, 1.2f, Frost, 10);
                CameraShake.Shake(0.12f, 0.08f);
                break;
            case ShotKind.Sky:
                StartCoroutine(SkyArrow(skyTarget, awakened));
                break;
        }
        lastShot = Time.time;
        float spent = s.kind == ShotKind.Double ? releaseAt + 0.12f / AttackSpeed : releaseAt;
        yield return new WaitForSeconds(Mathf.Max(0.05f, endAt - spent));

        comboStep = (comboStep + 1) % Mathf.Clamp(UllrSkills.ComboLength, 1, Combo.Length);
        IsSwinging = false;
        inCombo = false;
        comboRoutine = null;
        if (comboQueued && IsActive)
        {
            comboQueued = false;
            comboRoutine = StartCoroutine(ComboShotRoutine(queuedAim));
        }
        else
            StartCoroutine(EndPose(0f));
    }

    // ── The leap of the 4th shot: no clip has one, so the body is lifted (the model only; he stays on the ground for
    // the game, and the arrow flies at its usual height): a dip, up easing into the top while he draws, a fast drop,
    // and frost thrown up where he lands ──
    private float jumpLift, jumpTilt;
    private Transform liftedModel;
    private Vector3 modelRest;
    private Quaternion modelRestRot;

    private IEnumerator JumpArc(float height, float up, float down)
    {
        const float dip = 0.07f;
        Vfx.Dust(Feet, 0.6f, 4, new Color(0.85f, 0.9f, 1f, 0.5f));
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
            jumpTilt = -10f * k;   // leaning back as he goes up, bow raised
            yield return null;
        }
        for (float t = 0f; t < down; t += Time.deltaTime)
        {
            float k = t / down;
            jumpLift = height * (1f - k * k);
            jumpTilt = -10f * (1f - k);
            yield return null;
        }
        jumpLift = jumpTilt = 0f;
        // the landing
        Shockwave.Spawn(Feet, 1.6f, Frost, 14);
        Vfx.Dust(Feet, 1f, 6, new Color(0.85f, 0.9f, 1f, 0.55f));
        Vfx.GroundRing(Feet, 1.4f, Frost, 0.3f);
        CameraShake.Shake(0.1f, 0.08f);
    }

    // (after the animation has posed him: the lift laid on the model)
    private void ApplyJumpLift()
    {
        Transform model = IsActive && animator != null ? animator.transform : null;
        bool lifting = model != null && (jumpLift != 0f || jumpTilt != 0f);
        // set back down (the leap over, cut short, or the bow put away)
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

    // A quick hop back from the aim, easing out
    private IEnumerator StepBack(float distance, float seconds)
    {
        if (distance <= 0f || seconds <= 0f || controller == null)
            yield break;
        Vector3 back = -transform.forward;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float speed = distance / seconds * 2f * (1f - t / seconds);   // (fast at first, slowing to a stop)
            controller.Move(back * speed * Time.deltaTime);
            yield return null;
        }
    }

    // A gust torn loose with the arrow: a streak of wind laid along the shot, and a puff of air at the bow
    private void WindShotFx(Vector3 dir, float length, float strength)
    {
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        Vector3 mid = new Vector3(transform.position.x, transform.position.y + 0.25f, transform.position.z) + dir * (length * 0.5f + 0.3f);
        Vfx.Sprite(Vfx.Pick("wind_gust", "dashstreak"), mid, length * 1.1f, Frost * (1.2f * strength), 0.2f, 0.8f, 1.12f, 0f,
            Quaternion.LookRotation(Vector3.down, dir) * Quaternion.Euler(0f, 0f, 90f));
        Vfx.Sprite(Vfx.Pick("wind_vortex", "glow"), ArrowFrom(), 0.9f * strength, Frost * strength, 0.16f, 0.4f, 1.1f, 600f);
    }

    // One arrow from the bow down `dir`
    private UllrArrow Loose(Vector3 dir, float damage, float scale, bool pierce, Color? glow = null, float range = -1f, System.Action<Vector3> onEnd = null)
    {
        Color c = glow ?? (UllrSkills.Awakened ? Color.Lerp(Frost, UllrGold, 0.35f) : Frost);
        if (UllrSkills.Has(UllrSkills.Charge))
            damage *= 1.1f;   // 예리한 화살촉
        Vfx.Flash(ArrowFrom(), 0.5f * scale, c, 0.08f);
        return UllrArrow.Launch(ArrowFrom(), dir, range > 0f ? range : Range, arrowSpeed, scale, pierce, c, enemy => Hit(enemy, damage), onEnd);
    }

    // 5타 울르의 일격: a great arrow shot up into the sky comes down on the aimed spot, and frost bursts there
    private IEnumerator SkyArrow(Vector3 aimPoint, bool awakened)
    {
        Vector3 ground = new Vector3(aimPoint.x, Feet.y, aimPoint.z);
        Vector3 off = ground - Feet;
        off.y = 0f;
        float d = Mathf.Clamp(off.magnitude, 3f, 10f);
        ground = Feet + (off.sqrMagnitude > 0.01f ? off.normalized : transform.forward) * d;
        float radius = awakened ? 4f : 2f;   // (was 6 / 3 - a third off, it swept too wide)
        // 5타 울르의 일격, on the wind: the mark a stirring of wind on the ground, the arrow a gust, and where it comes
        // down a whirlwind bursts out and throws everything round it down
        GameObject mark = IceVfx.Frosted(Telegraph.Circle(ground + Vector3.up * 0.02f, radius, 0.55f), WindColor);
        Vfx.Sprite(Vfx.Pick("gs_whirlwind", "ring"), ground + Vector3.up * 0.06f, radius * 1.1f, WindColor * 0.6f, 0.55f, 0.6f, 1f, 360f, Quaternion.Euler(90f, 0f, 0f));
        // up it goes...
        Vector3 from = transform.position + Vector3.up * 1.2f;
        GameObject up = UllrArrow.Model(1.6f, WindColor);
        up.transform.SetPositionAndRotation(from, Quaternion.LookRotation(Vector3.up));
        Vfx.Trail(up, WindColor, 0.3f, 0.25f);
        for (float t = 0f; t < 0.22f && up != null; t += Time.deltaTime)
        {
            up.transform.position = Vector3.Lerp(from, from + Vector3.up * 10f, t / 0.22f);
            yield return null;
        }
        if (up != null)
            Destroy(up);
        yield return new WaitForSeconds(0.08f);
        // ...and down onto the mark
        Vector3 top = ground + Vector3.up * 11f;
        GameObject down = UllrArrow.Model(2.6f, WindColor);
        down.transform.SetPositionAndRotation(top, Quaternion.LookRotation(Vector3.down));
        Vfx.Trail(down, WindColor, 0.5f, 0.22f);
        Vfx.Flash(top, 2.4f, WindColor, 0.15f);
        for (float t = 0f; t < 0.2f && down != null; t += Time.deltaTime)
        {
            down.transform.position = Vector3.Lerp(top, ground + Vector3.up * 0.5f, t / 0.2f);
            yield return null;
        }
        if (mark != null)
            Destroy(mark);
        if (down != null)
            Destroy(down, 0.8f);
        // the whirlwind: two vortices turning against each other on the ground, a wave of wind rolling out, gusts and
        // dust thrown up and spun round
        // (the swirls drawn at half the size they were - they had spread well past the blue ring, the real reach)
        Vfx.Sprite(Vfx.Pick("gs_whirlwind", "ring"), ground + Vector3.up * 0.1f, radius * 1.3f, WindColor * 1.3f, 0.6f, 0.4f, 1.15f, 720f, Quaternion.Euler(90f, 0f, 0f));
        Vfx.Sprite(Vfx.Pick("gs_whirlwind", "ring"), ground + Vector3.up * 0.4f, radius * 0.8f, Color.white * 0.7f, 0.45f, 0.3f, 1.2f, -900f, Quaternion.Euler(90f, 0f, 0f));
        // the crescents of wind rolling out: grown to the blue ring and no further (at 2.4x they spread half again past it)
        Vfx.Sprite(Vfx.Pick("gs_wave", "shockring"), ground + Vector3.up * 0.08f, radius * 1.6f, WindColor, 0.4f, 0.3f, 1.25f, 0f, Quaternion.Euler(90f, 0f, 0f));
        Vfx.Sprite("glow", ground + Vector3.up * 1f, radius * 1.8f, WindColor * 0.8f, 0.3f, 0.5f, 1.3f, 0f);
        Vfx.Flash(ground + Vector3.up * 0.8f, radius, WindColor, 0.2f);
        Vfx.Burst(ground + Vector3.up * 0.5f, WindColor, 24, 9f, 0.12f, 1.4f);
        Vfx.Dust(ground, radius * 0.8f, 12, new Color(0.75f, 0.95f, 0.82f, 0.5f));
        Shockwave.Spawn(ground, radius, WindColor, 18);
        Vfx.GroundRing(ground, radius, WindColor, 0.4f);
        CameraShake.Shake(0.25f, 0.18f);
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead)
                continue;
            Collider c = enemy.GetComponent<Collider>();
            Vector3 near = (c != null ? c.ClosestPoint(ground + Vector3.up * 0.5f) : enemy.transform.position) - ground;
            near.y = 0f;
            if (near.magnitude > radius)
                continue;
            Hit(enemy, 2.5f);
            if (!enemy.IsDead)
                enemy.Knockdown(1.2f);
        }
    }

    // ── 정조준 사격: draw through two stages and loose ──
    private IEnumerator Charge()
    {
        GameAudio.Play("bow_draw"); GameAudio.Play("charge_hold", null, 0.7f);   // (the string drawn, power gathering)
        IsCharging = true;
        IsSwinging = true;
        releaseRequested = false;
        int stageShown = 0;
        PlayPose("Shoot", ShootLen, 1.45f, 1.3f);
        while (!releaseRequested)
        {
            Face(chargeAim, 540f);
            if (animator != null)
            {
                AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
                // hold at full draw
                if (st.IsName("Shoot") && st.normalizedTime * ShootLen >= 2.6f)
                    animator.SetFloat(AttackSpeedHash, 0f);
            }
            int stage = ChargeStage;
            if (stage > stageShown)
            {
                stageShown = stage;
                Shockwave.Spawn(Feet, 1.2f, stage == 2 ? StageTwoColor : StageOneColor, 10);
                Vfx.Flash(ArrowFrom(), 0.9f, stage == 2 ? StageTwoColor : StageOneColor, 0.15f);
            }
            yield return null;
        }
        int finalStage = ChargeStage;
        FaceNow(chargeAim);
        float clipNow = 2.6f;
        if (animator != null)
        {
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsName("Shoot"))
                clipNow = Mathf.Min(st.normalizedTime * ShootLen, ShootRelease);
            animator.SetFloat(AttackSpeedHash, 2.4f);
        }
        yield return new WaitForSeconds(Mathf.Max(0.05f, (ShootRelease - clipNow) / 2.4f));
        bool full = finalStage == 2;
        float scale = 1.2f + 0.35f * finalStage;
        Color c = full ? StageTwoColor : StageOneColor;
        bool burst = full && UllrSkills.Has(UllrSkills.ChargeUp);
        Loose(transform.forward, chargeDamage[finalStage], scale, full || UllrSkills.Awakened, c, chargeRange[finalStage],
            burst ? (System.Action<Vector3>)(at => FrostBurst(at, 2.5f, 1.5f)) : null);
        CameraShake.Shake(full ? 0.18f : 0.1f, full ? 0.14f : 0.07f);
        if (full)
            Shockwave.Spawn(Feet, 1.6f, StageTwoColor, 14);
        yield return new WaitForSeconds(0.25f);
        IsCharging = false;
        IsSwinging = false;
        chargeRoutine = null;
        StartCoroutine(EndPose(0f));
    }

    // Frost bursting at a point: everything within `radius` struck
    private void FrostBurst(Vector3 at, float radius, float damage)
    {
        Vector3 ground = new Vector3(at.x, Feet.y, at.z);
        // (a gust now, not frost)
        Vfx.Sprite(Vfx.Pick("wind_burst", "gs_wave"), ground + Vector3.up * 0.1f, radius * 2.4f, Frost * 1.3f, 0.35f, 0.35f, 1.15f, 140f, Quaternion.Euler(90f, 0f, 0f));
        Vfx.Sprite(Vfx.Pick("wind_vortex", "gs_whirlwind"), ground + Vector3.up * 0.3f, radius * 1.6f, Frost, 0.45f, 0.5f, 1.2f, 800f, Quaternion.Euler(90f, 0f, 0f));
        Shockwave.Spawn(ground, radius, Frost, 14);
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead)
                continue;
            Vector3 off = enemy.transform.position - ground;
            off.y = 0f;
            if (off.magnitude <= radius + 0.4f)
                Hit(enemy, damage);
        }
    }

    // ── Skills ──
    private float lockedNoticeAt;

    private void Locked()
    {
        if (Time.time < lockedNoticeAt)
            return;
        lockedNoticeAt = Time.time + 1f;
        DualBlades.Say(transform.position + Vector3.up * 2f, "아직 쓸 수 없습니다", new Color(0.75f, 0.75f, 0.8f));
    }

    private void Update()
    {
        if (IsActive && GameManager.IsRunActive && Time.timeScale > 0f && !pressing && !IsSwinging)
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            // (keys go through the skill system: 기력, the class skill's supports — see SkillSystem.KeyDown)
            if (SkillSystem.RightPressed(mouse))
            {
                if (!UllrSkills.Has(UllrSkills.Mark)) Locked();
                else if (MarkCooldownLeft <= 0f) StartCoroutine(MarkShot(Cursor()));
            }
            else if (SkillSystem.KeyDown(keyboard, 'Q'))
            {
                if (!UllrSkills.Has(UllrSkills.Roll)) Locked();
                else if (RollCooldownLeft <= 0f) StartCoroutine(RollShot(Cursor()));
            }
            else if (SkillSystem.KeyDown(keyboard, 'E'))
            {
                if (!UllrSkills.Has(UllrSkills.Trap)) Locked();
                else if (TrapCooldownLeft <= 0f) StartCoroutine(SetTrap(Cursor()));
            }
            else if (SkillSystem.KeyDown(keyboard, 'R'))
            {
                if (!UllrSkills.Has(UllrSkills.Rain)) Locked();
                else if (RainCooldownLeft <= 0f) StartCoroutine(WinterRain(Cursor()));
            }
        }
        // the marks that have run out
        if (marked.Count > 0)
            foreach (Enemy e in marked.Keys.ToArray())
                if (e == null || e.IsDead || Time.time >= marked[e])
                {
                    marked.Remove(e);
                    if (e != null && markGlows.TryGetValue(e, out GameObject g) && g != null) Destroy(g);
                    markGlows.Remove(e);
                }
        UpdateChargeFx();
    }

    // (test hook: a skill fired as if its key were pressed - "rmb", "q", "e", "r")
    public void DebugUse(string key, Vector3 aimPoint)
    {
        if (IsSwinging)
            return;
        switch (key)
        {
            case "rmb": StartCoroutine(MarkShot(aimPoint)); break;
            case "q": StartCoroutine(RollShot(aimPoint)); break;
            case "e": StartCoroutine(SetTrap(aimPoint)); break;
            case "r": StartCoroutine(WinterRain(aimPoint)); break;
        }
    }

    // 우클릭 · 바람의 매 (was 울르의 늑대): he looses an arrow into the air and it becomes a hawk of wind. The hawk flies
    // out over the cursor and wheels there, and every moment or so folds its wings and stoops on a monster below (the
    // nearest to its circle), striking it and leaving the hunter's mark (marked, it takes more from his arrows) before
    // climbing back; its circle drifts after the monsters near it. 사냥매: it stays longer and stoops faster, the mark
    // deeper.
    private const float HawkCooldown = 9f;
    private UllrHawk hawk;

    private IEnumerator MarkShot(Vector3 aimPoint)
    {
        markReadyAt = Time.time + HawkCooldown * UllrSkills.CooldownFactor;
        activeSkill = "mark";
        IsSwinging = true;
        const float speed = 2.6f, from = 2.2f;
        PlayPose("Shoot", ShootLen, from, speed, 0.14f);
        float release = (ShootRelease - from) / speed;
        StartCoroutine(TurnTo(aimPoint, release * 0.7f));
        Vfx.Gather(ArrowFrom(), 1.3f, WindColor, release, 40f);
        yield return new WaitForSeconds(release);
        // over the cursor (not past the hawk's reach)
        Vector3 centre = new Vector3(aimPoint.x, Feet.y, aimPoint.z);
        Vector3 off = centre - Feet;
        off.y = 0f;
        if (off.magnitude > 14f)
            centre = Feet + off.normalized * 14f;
        Vfx.Flash(ArrowFrom(), 1.4f, WindColor, 0.14f);
        Vfx.Sprite(Vfx.Pick("wind_vortex", "glow"), ArrowFrom(), 1.6f, WindColor * 1.3f, 0.25f, 0.4f, 1.2f, 700f);
        if (hawk != null)
            hawk.Leave();
        hawk = UllrHawk.Release(this, ArrowFrom(), centre, UllrSkills.Has(UllrSkills.MarkUp));
        yield return new WaitForSeconds(0.15f);
        IsSwinging = false;
        activeSkill = null;
        StartCoroutine(EndPose(0f));
    }

    public static readonly Color WindColor = new Color(0.55f, 1f, 0.78f);   // 바람: the bow's skills

    // The hawk's stoop landing: struck, marked, and a burst of wind where it hits
    public void HawkStrike(Enemy enemy, float damage)
    {
        if (enemy == null || enemy.IsDead)
            return;
        Vector3 at = enemy.transform.position + Vector3.up * 0.8f;
        Hit(enemy, damage);
        if (!enemy.IsDead)
        {
            MarkEnemy(enemy);
            enemy.Interrupt();
        }
        SpearKeeper.HitSpark(enemy, WindColor);
        Vfx.Sprite(Vfx.Pick("wind_burst", "gs_wave"), new Vector3(at.x, Feet.y + 0.1f, at.z), 2.6f, WindColor * 1.4f, 0.3f, 0.35f, 1.15f, 150f, Quaternion.Euler(90f, 0f, 0f));
        Vfx.Flash(at, 1.6f, WindColor, 0.14f);
        Vfx.Burst(at, Color.white, 8, 5f, 0.08f, 1.2f);
        CameraShake.Shake(0.08f, 0.06f);
    }

    // Q · 바람의 서약: a roll toward the cursor on the wind (no harm while rolling), then up and a fan of arrows at it
    private IEnumerator RollShot(Vector3 aimPoint)
    {
        rollReadyAt = Time.time + rollCooldown * UllrSkills.CooldownFactor;
        activeSkill = "roll";
        IsSwinging = true;
        // 바람의 서약: everything goes toward the cursor - the wind carries him rolling that way and he comes up loosing
        // a fan of wind arrows at it. (The motion rolls to his right, so he is turned side-on to the cursor for the roll,
        // then back to face it for the shot.)
        Vector3 toward = aimPoint - transform.position;
        toward.y = 0f;
        toward = toward.sqrMagnitude > 0.01f ? toward.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, -90f, 0f) * toward);
        const float speed = 4f, from = 0.2f;
        PlayPose("RollShoot", RollLen, from, speed);
        bool up = UllrSkills.Has(UllrSkills.RollUp);
        float distance = rollDistance * (up ? 1.3f : 1f);
        float rollStart = (0.25f - from) / speed, rollEnd = (1.3f - from) / speed;
        // the oath: a whirlwind bursting round his feet and a gust laid along the way he goes
        Vfx.Sprite(Vfx.Pick("wind_vortex", "gs_whirlwind"), Feet + Vector3.up * 0.1f, 2.8f, WindColor * 1.2f, 0.45f, 0.5f, 1.2f, -720f, Quaternion.Euler(90f, 0f, 0f));
        Vector3 laneMid = Feet + Vector3.up * 0.25f + toward * (rollDistance * (up ? 1.3f : 1f) * 0.55f);
        Vfx.Sprite(Vfx.Pick("wind_gust", "dashstreak"), laneMid, rollDistance * 1.9f, WindColor * 1.5f, 0.42f, 0.7f, 1.1f, 0f,
            Quaternion.LookRotation(Vector3.down, toward) * Quaternion.Euler(0f, 0f, 90f));
        Vfx.Dust(Feet, 1f, 8, new Color(0.8f, 1f, 0.9f, 0.45f));
        rolling = true;
        var streak = new GameObject("RollStreak");
        streak.transform.SetParent(transform, false);
        streak.transform.localPosition = Vector3.up * 0.1f;
        Vfx.Trail(streak, WindColor * 0.6f, 0.3f, 0.18f);
        for (float t = 0f; t < rollEnd; t += Time.deltaTime)
        {
            if (t >= rollStart)
                controller.Move(toward * (distance / (rollEnd - rollStart)) * Time.deltaTime);
            yield return null;
        }
        rolling = false;
        Destroy(streak, 0.25f);
        Vfx.Dust(Feet, 0.8f, 6, new Color(0.8f, 1f, 0.9f, 0.45f));
        // up on one knee, turned the way the cursor lay when he cast it, and loosed. (Not re-aimed at the cursor's
        // point: when it was nearer than the roll he has rolled past it, and turning to it would shoot backward.)
        transform.rotation = Quaternion.LookRotation(toward);
        yield return new WaitForSeconds(Mathf.Max(0f, (ShootRelease - from) / speed - rollEnd));
        int n = up ? 8 : 5;
        float spread = up ? 9f : 12f;
        for (int i = 0; i < n; i++)
            Loose(Quaternion.Euler(0f, (i - (n - 1) / 2f) * spread, 0f) * toward, 0.8f, 1.1f, UllrSkills.Awakened, WindColor);
        // the fan of wind arrows laid out ahead of him, and the gust he looses them on
        Vfx.Sprite(Vfx.Pick("wind_fan", "gs_wave"), Feet + Vector3.up * 0.2f + toward * 2.4f, 5.2f, WindColor * 1.5f, 0.32f, 0.6f, 1.15f, 0f,
            Quaternion.LookRotation(Vector3.down, toward));
        Vfx.Sprite(Vfx.Pick("wind_burst", "gs_wave"), Feet + Vector3.up * 0.12f, 2.6f, WindColor * 1.2f, 0.3f, 0.4f, 1.1f, 200f, Quaternion.Euler(90f, 0f, 0f));
        Shockwave.Spawn(Feet, 1.4f, WindColor, 10);
        CameraShake.Shake(0.12f, 0.08f);
        yield return new WaitForSeconds(0.18f);
        IsSwinging = false;
        activeSkill = null;
        StartCoroutine(EndPose(0f));
    }

    // E · 울르의 돌풍함정: a hop back from a wind trap laid where he stood
    private readonly List<UllrTrap> traps = new List<UllrTrap>();

    private IEnumerator SetTrap(Vector3 aimPoint)
    {
        trapReadyAt = Time.time + trapCooldown * UllrSkills.CooldownFactor;
        activeSkill = "trap";
        IsSwinging = true;
        FaceNow(aimPoint);
        const float speed = 3f;
        PlayPose("Dodge", DodgeLen, 0f, speed);
        bool up = UllrSkills.Has(UllrSkills.TrapUp);
        int maxTraps = up ? 2 : 1;
        traps.RemoveAll(t => t == null);
        while (traps.Count >= maxTraps)
        {
            if (traps[0] != null) traps[0].Fade();
            traps.RemoveAt(0);
        }
        var trap = new GameObject("UllrTrap").AddComponent<UllrTrap>();
        trap.transform.position = Feet;
        trap.Init(this, trapRadius * (up ? 1.4f : 1f));
        traps.Add(trap);
        Vfx.Flash(Feet + Vector3.up * 0.3f, 1.6f, WindColor, 0.2f);
        Vfx.Sprite(Vfx.Pick("wind_vortex", "gs_whirlwind"), Feet + Vector3.up * 0.08f, 2.6f, WindColor * 1.2f, 0.45f, 0.4f, 1.1f, 600f, Quaternion.Euler(90f, 0f, 0f));
        // the hop back, carried on a gust
        Vector3 back = -transform.forward;
        Vfx.Sprite(Vfx.Pick("wind_gust", "dashstreak"), Feet + Vector3.up * 0.3f + back * 1.3f, 3.2f, WindColor * 1.3f, 0.3f, 0.8f, 1.1f, 0f,
            Quaternion.LookRotation(Vector3.down, back) * Quaternion.Euler(0f, 0f, 90f));
        var hopTrail = new GameObject("TrapHopTrail");
        hopTrail.transform.SetParent(transform, false);
        hopTrail.transform.localPosition = Vector3.up * 0.1f;
        Vfx.Trail(hopTrail, WindColor * 0.7f, 0.3f, 0.18f);
        Destroy(hopTrail, 0.7f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            if (t > 0.05f)
                controller.Move(back * (2.5f / 0.25f) * Time.deltaTime);
            yield return null;
        }
        yield return new WaitForSeconds(0.15f);
        IsSwinging = false;
        activeSkill = null;
        StartCoroutine(EndPose(0f));
    }

    // A trap going off (called by the trap)
    public void TrapBurst(Vector3 at, float radius)
    {
        // 바람 덫: the trap bursts into a whirlwind - blades of wind flung out, the vortex spun up, dust and leaves
        Vfx.Sprite(Vfx.Pick("wind_burst", "gs_wave"), at + Vector3.up * 0.1f, radius * 2.6f, WindColor * 1.4f, 0.4f, 0.3f, 1.15f, 120f, Quaternion.Euler(90f, 0f, 0f));
        Vfx.Sprite(Vfx.Pick("wind_vortex", "gs_whirlwind"), at + Vector3.up * 0.3f, radius * 2f, WindColor * 1.1f, 0.6f, 0.5f, 1.2f, 900f, Quaternion.Euler(90f, 0f, 0f));
        Vfx.Sprite("glow", at + Vector3.up * 0.8f, radius * 1.6f, WindColor * 0.7f, 0.3f, 0.5f, 1.3f, 0f);
        Vfx.Flash(at + Vector3.up * 0.6f, radius, WindColor, 0.18f);
        Vfx.Burst(at + Vector3.up * 0.4f, WindColor, 20, 8f, 0.12f, 1.2f);
        Vfx.Dust(at, radius * 0.8f, 10, new Color(0.75f, 0.95f, 0.82f, 0.5f));
        Shockwave.Spawn(at, radius, WindColor, 16);
        CameraShake.Shake(0.18f, 0.12f);
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead)
                continue;
            Collider c = enemy.GetComponent<Collider>();
            Vector3 off = (c != null ? c.ClosestPoint(at + Vector3.up * 0.5f) : enemy.transform.position) - at;
            off.y = 0f;
            if (off.magnitude > radius)
                continue;
            Hit(enemy, 2f);
            if (!enemy.IsDead)
                enemy.Knockdown(1.6f);
        }
    }

    // R · 바람의 추적 화살 (was 스카디의 겨울비, the arrow rain)
    private IEnumerator WinterRain(Vector3 aimPoint)
    {
        // R · 바람의 추적 화살: the bow raised to the sky and a volley of wind arrows loosed up and out in a spray; each
        // one turns in the air and hunts down a monster (the ones nearest the cursor first, shared round so every
        // monster in reach is hunted), curving in like a seeking missile. 추적 강화: 15 → 24 arrows, each bursting
        // in a gust where it strikes.
        rainReadyAt = Time.time + rainCooldown * UllrSkills.CooldownFactor;
        activeSkill = "rain";
        IsSwinging = true;
        const float speed = 3f, drawFrom = 8.4f;
        float release = (UpRelease - drawFrom) / speed;
        PlayPose("ShootUp", UpLen, drawFrom, speed, 0.18f);
        StartCoroutine(TurnTo(aimPoint, release * 0.6f));
        Vfx.Gather(transform.position + Vector3.up * 1.4f, 2.4f, WindColor, release, 70f);
        Vfx.Sprite(Vfx.Pick("gs_whirlwind", "ring"), Feet + Vector3.up * 0.1f, 3.2f, WindColor * 0.9f, release + 0.4f, 0.5f, 1.1f, 400f, Quaternion.Euler(90f, 0f, 0f));
        yield return new WaitForSeconds(release);

        bool big = UllrSkills.Has(UllrSkills.RainUp);
        int count = big ? 24 : 15;
        // the hunted: every monster within reach, nearest the cursor first
        Vector3 aimFlat = new Vector3(aimPoint.x, transform.position.y, aimPoint.z);
        List<Enemy> prey = Enemy.Alive.Where(e => e != null && !e.IsDead && (e.transform.position - transform.position).magnitude < homingRange)
            .OrderBy(e => (e.transform.position - aimFlat).sqrMagnitude).ToList();
        CameraShake.Shake(0.12f, 0.08f);
        Vfx.Flash(transform.position + Vector3.up * 1.6f, 2.4f, WindColor, 0.2f);
        Vector3 fwd = transform.forward;
        for (int i = 0; i < count; i++)
        {
            Enemy target = prey.Count > 0 ? prey[i % prey.Count] : null;
            // loosed up and out, fanned across the front
            float yaw = Mathf.Lerp(-75f, 75f, count > 1 ? i / (count - 1f) : 0.5f) + Random.Range(-8f, 8f);
            Vector3 launch = (Quaternion.Euler(0f, yaw, 0f) * fwd + Vector3.up * Random.Range(0.7f, 1.1f)).normalized;
            Vector3 fallback = aimFlat + new Vector3(Random.Range(-2.5f, 2.5f), 0f, Random.Range(-2.5f, 2.5f));
            StartCoroutine(HomingArrow(transform.position + Vector3.up * 1.3f + fwd * 0.3f, launch, target, fallback, big));
            yield return new WaitForSeconds(0.035f);
        }
        yield return new WaitForSeconds(0.2f);
        IsSwinging = false;
        activeSkill = null;
        StartCoroutine(EndPose(0f));
    }

    private const float homingRange = 16f;

    // One seeking arrow: out along `launch`, then turning ever harder onto its prey (or another, if its prey falls
    // first), speeding up as it closes; a gust where it strikes
    private IEnumerator HomingArrow(Vector3 from, Vector3 launch, Enemy target, Vector3 fallback, bool burst)
    {
        GameObject arrow = UllrArrow.Model(1f, WindColor);
        arrow.transform.SetPositionAndRotation(from, Quaternion.LookRotation(launch));
        Vfx.Trail(arrow, WindColor * 0.9f, 0.14f, 0.28f);
        Destroy(arrow, 4f);
        Vector3 pos = from, vel = launch * 13f;
        float t = 0f;
        while (arrow != null && t < 3f)
        {
            t += Time.deltaTime;
            if (target == null || target.IsDead)
                target = NearestEnemy(pos, homingRange);
            Vector3 aim = target != null ? target.transform.position + Vector3.up * 0.6f : fallback;
            Vector3 to = aim - pos;
            // steering: loose for the first moment (so the volley fans out), then tighter and faster
            float turn = Mathf.Lerp(2.5f, 14f, Mathf.Clamp01((t - 0.12f) / 0.45f));
            float spd = Mathf.Lerp(13f, 30f, Mathf.Clamp01(t / 0.7f));
            vel = Vector3.RotateTowards(vel, to.normalized * spd, turn * Time.deltaTime, 60f * Time.deltaTime);
            vel = vel.normalized * spd;
            pos += vel * Time.deltaTime;
            arrow.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(vel));
            // struck (measured to the body's edge), or the empty spot reached / the ground hit
            bool hitTarget = false;
            if (target != null)
            {
                Collider c = target.GetComponent<Collider>();
                Vector3 near = c != null ? c.ClosestPoint(pos) : target.transform.position;
                hitTarget = (near - pos).magnitude < 0.6f;
            }
            if (hitTarget || (target == null && to.magnitude < 0.6f) || pos.y < Feet.y)
            {
                if (hitTarget)
                {
                    Hit(target, 0.75f);
                    if (!target.IsDead) target.Interrupt();
                }
                Vfx.Burst(pos, WindColor, 7, 4f, 0.09f, 0.7f);
                Vfx.Flash(pos, 0.8f, WindColor, 0.1f);
                if (burst)
                {
                    // 추적 강화: a gust bursts where it strikes
                    Vfx.Sprite(Vfx.Pick("gs_whirlwind", "ring"), new Vector3(pos.x, Feet.y + 0.08f, pos.z), 2.4f, WindColor, 0.3f, 0.4f, 1.1f, 600f, Quaternion.Euler(90f, 0f, 0f));
                    foreach (Enemy other in Enemy.Alive.ToArray())
                    {
                        if (other == null || other.IsDead || other == target) continue;
                        Vector3 o = other.transform.position - pos;
                        o.y = 0f;
                        if (o.magnitude <= 1.2f) Hit(other, 0.35f);
                    }
                }
                break;
            }
            yield return null;
        }
        if (arrow != null)
        {
            foreach (Renderer r in arrow.GetComponentsInChildren<MeshRenderer>())
                r.enabled = false;
            Destroy(arrow, 0.3f);
        }
    }

    private static Enemy NearestEnemy(Vector3 from, float range)
    {
        Enemy best = null;
        float bestD = range;
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead) continue;
            float d = Vector3.Distance(e.transform.position, from);
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    private static IEnumerator Fly(GameObject go, Vector3 from, Vector3 to, float seconds)
    {
        for (float t = 0f; t < seconds && go != null; t += Time.deltaTime)
        {
            go.transform.position = Vector3.Lerp(from, to, t / seconds);
            yield return null;
        }
        if (go != null)
            Destroy(go);
    }

    // One arrow of the rain, point-first onto `spot`
    private IEnumerator FallingArrow(Vector3 spot)
    {
        Vector3 fall = Quaternion.Euler(Random.Range(-12f, 12f), 0f, Random.Range(-12f, 12f)) * Vector3.down;
        Vector3 from = spot - fall * 9f, stuck = spot - fall * 0.25f;
        GameObject arrow = UllrArrow.Model(1.1f, Frost);
        arrow.transform.SetPositionAndRotation(from, Quaternion.LookRotation(fall));
        Vfx.Trail(arrow, Frost * 0.8f, 0.1f, 0.1f);
        for (float t = 0f; t < 0.22f && arrow != null; t += Time.deltaTime)
        {
            arrow.transform.position = Vector3.Lerp(from, stuck, t / 0.22f);
            yield return null;
        }
        if (arrow != null)
        {
            arrow.transform.position = stuck;
            Destroy(arrow, 0.7f);
        }
        Vfx.Burst(spot + Vector3.up * 0.2f, Frost, 5, 4f, 0.08f, 1.5f);
        Vfx.GroundRing(spot, 0.7f, Frost, 0.22f);
        foreach (Enemy enemy in Enemy.Alive.ToArray())
        {
            if (enemy == null || enemy.IsDead) continue;
            Collider c = enemy.GetComponent<Collider>();
            Vector3 near = (c != null ? c.ClosestPoint(spot + Vector3.up * 0.8f) : enemy.transform.position) - spot;
            near.y = 0f;
            if (near.magnitude <= 1.1f)
                Hit(enemy, 0.55f);
        }
    }

    // ── The draw's glow and aim line, and the HUD ──
    // (a frost rune turning at his feet and a faint line down the aim - they were a lit cylinder and box, which at these
    // pale colours drew as a flat white disc and bar)
    private VfxPiece chargeGlow;
    private Transform aimLine;

    private void UpdateChargeFx()
    {
        if (!IsCharging)
        {
            if (chargeGlow != null) { Destroy(chargeGlow.gameObject); chargeGlow = null; }
            if (aimLine != null) aimLine.gameObject.SetActive(false);
            return;
        }
        int stage = ChargeStage;
        Color color = stage == 2 ? StageTwoColor : stage == 1 ? StageOneColor : new Color(0.35f, 0.5f, 0.65f);
        if (chargeGlow == null)
            chargeGlow = Vfx.Decal(Feet + Vector3.up * 0.04f, 1.6f, color, Vfx.Pick("Frost_NovaRing", "runes"), 90f);
        float pulse = 1.5f + stage * 0.45f + Mathf.Sin(Time.time * (6f + stage * 4f)) * 0.08f;
        chargeGlow.transform.position = Feet + Vector3.up * 0.04f;
        chargeGlow.Set(color * (0.6f + 0.3f * stage), pulse, 0.6f + 0.2f * stage);
        if (aimLine == null)
        {
            aimLine = Mats.Shape(PrimitiveType.Cube, null, Vector3.zero, Vector3.one, null, false, "UllrAimLine").transform;
            aimLine.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        float range = chargeRange[stage];
        aimLine.GetComponent<Renderer>().sharedMaterial = Mats.Get("ullr_aim_" + stage, color * 0.45f, 0.2f, color * (0.4f + 0.3f * stage));
        aimLine.gameObject.SetActive(!releaseRequested);
        aimLine.SetPositionAndRotation(Feet + Vector3.up * 0.02f + transform.forward * range / 2f, transform.rotation);
        aimLine.localScale = new Vector3(0.05f, 0.004f, range);
    }

    private readonly SkillBar.Slot[] barSlots = new SkillBar.Slot[4];
    private static Texture2D[] icons;

    public override void DrawHud()
    {
        if (!SkillBar.Hidden)
        {
            if (icons == null)
                icons = new[] { "Roll", "Trap", "Rain", "Hawk" }.Select(n => Resources.Load<Texture2D>("UI/UllrIcons/" + n)).ToArray();
            float cd = UllrSkills.CooldownFactor;
            Color lit = UllrSkills.Awakened ? UllrGold : Frost;
            barSlots[0] = new SkillBar.Slot { key = "Q", name = UllrSkills.Has(UllrSkills.RollUp) ? "질풍의 서약" : "바람의 서약", icon = icons[0], learned = UllrSkills.Has(UllrSkills.Roll), left = RollCooldownLeft, total = rollCooldown * cd, lit = activeSkill == "roll", litColor = lit };
            barSlots[1] = new SkillBar.Slot { key = "E", name = "울르의 돌풍함정", icon = icons[1], learned = UllrSkills.Has(UllrSkills.Trap), left = TrapCooldownLeft, total = trapCooldown * cd, lit = activeSkill == "trap", litColor = lit };
            barSlots[2] = new SkillBar.Slot { key = "R", name = "바람의 추적 화살", icon = icons[2], learned = UllrSkills.Has(UllrSkills.Rain), left = RainCooldownLeft, total = rainCooldown * cd, lit = activeSkill == "rain", litColor = lit };
            barSlots[3] = new SkillBar.Slot { key = "우클릭", name = UllrSkills.Has(UllrSkills.MarkUp) ? "사냥매" : "바람의 매", icon = icons[3], learned = UllrSkills.Has(UllrSkills.Mark), left = MarkCooldownLeft, total = HawkCooldown * cd, lit = activeSkill == "mark", litColor = WindColor };
            SkillBar.Draw(barSlots);
        }
        Camera cam = Camera.main;
        if (cam == null || !IsCharging)
            return;
        Vector3 screen = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.6f);
        if (screen.z <= 0f)
            return;
        const float width = 64f, height = 8f;
        Rect bar = new Rect(screen.x - width / 2f, Screen.height - screen.y - 24f, width, height);
        UiFont.Fill(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f), new Color(0f, 0f, 0f, 0.7f));
        int st = ChargeStage;
        UiFont.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(ChargeHeld / stageTwoTime), bar.height), st == 2 ? StageTwoColor : st == 1 ? StageOneColor : new Color(0.5f, 0.6f, 0.7f));
        float tick = bar.x + bar.width * stageOneTime / stageTwoTime;
        UiFont.Fill(new Rect(tick - 1f, bar.y - 2f, 2f, bar.height + 4f), Color.white);
        if (st > 0)
            GUI.Label(new Rect(bar.x - 20f, bar.y - 20f, bar.width + 40f, 18f), st == 2 ? "최대로 당김!" : "당김 1단계", UiFont.Style(12, TextAnchor.MiddleCenter, FontStyle.Bold));
    }
}

// 바람 덫: a wind rune on the ground, a small whirlwind turning in it, that arms after a moment and bursts when an
// enemy steps on it
public class UllrTrap : MonoBehaviour
{
    private UllrBow owner;
    private float radius, armedAt, expires;
    private VfxPiece rune, swirl;
    private GameObject core;

    public void Init(UllrBow bow, float burstRadius)
    {
        owner = bow;
        radius = burstRadius;
        armedAt = Time.time + 0.4f;
        expires = Time.time + 14f;
        rune = Vfx.Decal(transform.position + Vector3.up * 0.04f, 2.2f, UllrBow.WindColor * 0.9f, Vfx.Pick("wind_rune", "runes"), 50f);
        rune.transform.SetParent(transform, true);
        swirl = Vfx.Decal(transform.position + Vector3.up * 0.07f, 1.5f, UllrBow.WindColor * 0.7f, Vfx.Pick("wind_vortex", "gs_whirlwind"), -260f);
        swirl.transform.SetParent(transform, true);
        core = Vfx.Glow(transform, Vector3.up * 0.25f, 0.9f, UllrBow.WindColor);
    }

    public void Fade()
    {
        Destroy(gameObject);
    }

    private void Update()
    {
        if (Time.time >= expires || owner == null)
        {
            Destroy(gameObject);
            return;
        }
        // the rune breathes while it waits
        float k = 1f + Mathf.Sin(Time.time * 4f) * 0.06f;
        if (rune != null)
            rune.Set(UllrBow.WindColor * (Time.time < armedAt ? 0.5f : 0.9f), 2.2f * k, 1f);
        if (swirl != null)
            swirl.Set(UllrBow.WindColor * (Time.time < armedAt ? 0.4f : 0.75f), 1.5f / k, 1f);
        if (Time.time < armedAt)
            return;
        foreach (Enemy enemy in Enemy.Alive)
        {
            if (enemy == null || enemy.IsDead)
                continue;
            Collider c = enemy.GetComponent<Collider>();
            Vector3 off = (c != null ? c.ClosestPoint(transform.position + Vector3.up * 0.5f) : enemy.transform.position) - transform.position;
            off.y = 0f;
            if (off.magnitude > 1.4f)
                continue;
            owner.TrapBurst(transform.position, radius);
            Destroy(gameObject);
            return;
        }
    }
}
