using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The lightning gauntlets (번개 건틀릿) and the Norse brawler who wears them: Muspelheim's second weapon, picked on the
// way in instead of 묠니르. Fast fists where the hammer is one heavy blow.
//  클릭  — a punch
//  Q     — 뇌격 도약: a short leap forward, both fists driven into the floor; lightning bursts round the landing
//  E     — 천둥 연타: a flurry of punches in front
//  R     — 뇌신 강림: becomes the thunder god for a while — a halo of lightning at his back, fists and body crackling,
//          Q and E stronger, quicker on his feet. R again ends it at once in a blast of lightning over everything around.
//  Space — the game's dash (learned in the tree)
// Every skill starts bare and is built up in GauntletSkills (번개 건틀릿 스킬트리). Blue lightning with a fifth of gold.
public class ThunderGauntlets : ArmoryWeapon
{
    [Header("Clips (legacy Animation on the model)")]
    [SerializeField] private string idleClip = "idle", runClip = "run", punchClip = "punch", comboClip = "combo",
        leapClip = "leap", roarClip = "roar", burstClip = "burst";
    [SerializeField] private float runClipSpeed = 1.15f, crossFade = 0.12f;
    // Where in each clip the blows land (seconds into the clip, read off its hands and hips)
    [SerializeField] private Vector2 jab = new Vector2(0.85f, 1.1f);       // the left jab: from, lands
    [SerializeField] private Vector2 cross = new Vector2(2.05f, 2.2f);     // the right cross
    [SerializeField] private Vector2 uppercut = new Vector2(1.0f, 1.2f);   // (combo clip)
    [SerializeField] private Vector2 leapJump = new Vector2(3.0f, 4.0f);   // take-off, landing (leap clip)
    [SerializeField] private Vector2 roarSmash = new Vector2(0.5f, 1.2f);  // (roar clip)
    [SerializeField] private Vector2 burstSlam = new Vector2(3.6f, 4.5f);  // (burst clip)

    [Header("Punch (click)")]
    [SerializeField] private float punchDamage = 0.9f;
    [SerializeField] private float punchReach = 1.3f, punchRadius = 1.7f;
    [SerializeField] private float punchClipSpeed = 1.7f;
    [SerializeField] private float punchRecover = 0.16f;

    [Header("Q 뇌격 도약")]
    [SerializeField] private float leapCooldown = 5f;
    [SerializeField] private float leapDistance = 4.5f;
    [SerializeField] private float leapTime = 0.42f;
    [SerializeField] private float leapHeight = 1.8f;
    [SerializeField] private float leapRadius = 3f;
    [SerializeField] private float leapDamage = 1.6f;

    [Header("E 천둥 연타")]
    [SerializeField] private float barrageCooldown = 6f;
    [SerializeField] private int barrageHits = 6;
    [SerializeField] private float barrageInterval = 0.15f;
    [SerializeField] private float barrageDamage = 0.38f;
    [SerializeField] private float barrageReach = 2.5f;
    [SerializeField] private float barrageAngle = 110f;
    [SerializeField] private float finisherDamage = 1.4f;
    [SerializeField] private float boltReach = 8f;

    [Header("R 뇌신 강림")]
    [SerializeField] private float avatarCooldown = 20f;
    [SerializeField] private float avatarTime = 8f;
    [SerializeField] private float avatarScale = 1.15f;
    [SerializeField] private float avatarMove = 1.2f;
    [SerializeField] private float releaseDamage = 2.5f;
    [SerializeField] private float releasePerSecondLeft = 0.12f;
    [SerializeField] private float releaseRadius = 40f;

    private Animation anim;
    private Transform leftHand, rightHand;
    private Vector3 modelBasePos, modelBaseScale;
    private Vector3 lastPosition;
    private PlayerMovement movement;

    private enum Act { None, Punch, Leap, Barrage, Roar, Burst, Spin }
    private Act act;
    private float leapReadyAt, barrageReadyAt, avatarReadyAt, spinReadyAt;
    private Quaternion modelBaseRot = Quaternion.identity;
    private bool avatar;
    private float avatarStart, avatarEnd;
    private readonly List<GameObject> avatarLook = new List<GameObject>();
    private readonly List<GameObject> runeGlow = new List<GameObject>();
    private float nextCrackle, nextSmite;
    private float screenFlash;
    private bool swiftBarrage;
    private GUIStyle timeStyle;

    public override CharacterProfile.WeaponKind Kind => CharacterProfile.WeaponKind.Gauntlets;
    public override SkillTree Tree => GauntletSkills.Tree;
    public override string TreeName => "번개 건틀릿 스킬트리";
    public override string Hint => avatar
        ? "[클릭] 펀치  [우클릭] 질풍 회전  [Q] 뇌격 도약  [E] 천둥 연타  [R] 변신 해제 · 전기 폭발  [Space] 대시"
        : "[클릭] 펀치  [우클릭] 질풍 회전  [Q] 뇌격 도약  [E] 천둥 연타  [R] 뇌신 강림  [Space] 대시";
    public override bool Rooted => act == Act.Leap || act == Act.Spin;
    public override float MoveMultiplier =>
        act == Act.Leap || act == Act.Roar || act == Act.Burst || act == Act.Spin ? 0f
        : act == Act.Barrage ? (swiftBarrage ? 0.35f : 0f)
        : act == Act.Punch ? 0.3f
        : avatar ? avatarMove : 1f;
    public override float IncomingDamageMultiplier => avatar && GauntletSkills.Has(GauntletSkills.AvatarBody) ? 0.75f : 1f;
    public override bool CanDash => GauntletSkills.Has(GauntletSkills.Dash) && act != Act.Leap && act != Act.Roar && act != Act.Burst;
    public override float DashScale => GauntletSkills.DashScale;

    public bool Transformed => avatar;
    private static float CD => GauntletSkills.CooldownScale;
    private bool Locked => !GameManager.IsRunActive || Time.timeScale <= 0f || PlayerEffects.Bind <= 0f;
    private bool Dead => stats != null && stats.IsDead;
    private float AvatarDuration => avatarTime + 1.5f * GauntletSkills.Rank(GauntletSkills.AvatarTime);
    private float LeapCooldownLeft => Mathf.Max(0f, leapReadyAt - Time.time);
    private float BarrageCooldownLeft => Mathf.Max(0f, barrageReadyAt - Time.time);
    private float AvatarCooldownLeft => Mathf.Max(0f, avatarReadyAt - Time.time);
    private float SpinCooldownLeft => Mathf.Max(0f, spinReadyAt - Time.time);

    protected override void Awake()
    {
        base.Awake();
        movement = GetComponent<PlayerMovement>();
    }

    protected override void Equipped(bool on)
    {
        foreach (var g in runeGlow) if (g != null) Destroy(g);
        runeGlow.Clear();
        if (!on)
        {
            EndAvatar(false);
            anim = null;
            return;
        }
        anim = profile.GetComponentInChildren<Animation>(true);
        leftHand = FindDeep(profile.transform, "LeftHand");
        rightHand = FindDeep(profile.transform, "RightHand");
        // the runes on the gauntlets glow softly, always
        foreach (var hand in new[] { leftHand, rightHand })
        {
            if (hand == null) continue;
            runeGlow.Add(Vfx.Glow(hand, Vector3.zero, 0.6f / Scale(hand), StormFx.Blue * 0.55f, "glow", 0f));
            var sparks = new GameObject("GauntletSparks");
            sparks.transform.SetParent(hand, false);
            Vfx.Sparkle(sparks, StormFx.Blue, 1.2f, 0.06f);
            runeGlow.Add(sparks);
        }
        modelBasePos = profile.transform.localPosition;
        modelBaseScale = profile.transform.localScale;
        modelBaseRot = profile.transform.localRotation;
        lastPosition = transform.position;
        if (anim == null) return;
        foreach (AnimationState s in anim)
            s.wrapMode = s.name == idleClip || s.name == runClip ? WrapMode.Loop : WrapMode.ClampForever;
        if (HasClip(runClip)) anim[runClip].speed = runClipSpeed;
        Play(idleClip);
    }

    public override void ResetCombat()
    {
        StopAllCoroutines();
        EndAvatar(false);
        act = Act.None;
        swiftBarrage = false;
        leapReadyAt = barrageReadyAt = avatarReadyAt = spinReadyAt = 0f;
        screenFlash = 0f;
        if (profile != null) { profile.transform.localPosition = modelBasePos; profile.transform.localRotation = modelBaseRot; }
    }

    private void OnDisable()
    {
        EndAvatar(false);
    }

    // ═══════════════ Input ═══════════════

    public override void Press(Vector3 aim)
    {
        if (Locked || Dead || act != Act.None) return;
        StartCoroutine(Punch(aim));
    }

    private void Update()
    {
        Slots(IsActive);
        if (!IsActive) return;
        if (Dead) { if (avatar) EndAvatar(false); return; }
        var kb = Keyboard.current;
        // (Q / E / R are the skill system's now: skills found as orbs)
        if (kb != null && !Locked)
        {
            if (SkillSystem.KeyDown(kb, 'R') && GauntletSkills.Has(GauntletSkills.Avatar))
            {
                if (avatar && act != Act.Burst && Time.time - avatarStart > 0.4f) { StopAllCoroutines(); act = Act.None; StartCoroutine(Release()); }
                else if (!avatar && act == Act.None && AvatarCooldownLeft <= 0f) StartCoroutine(Ascend());
            }
            // (the leap goes the way he faces — the way the keys last turned him — not toward the mouse)
            else if (act == Act.None && SkillSystem.KeyDown(kb, 'Q') && LeapCooldownLeft <= 0f && GauntletSkills.Has(GauntletSkills.Leap))
                StartCoroutine(Leap(transform.position + FacingDir() * 50f));
            else if (act == Act.None && SkillSystem.KeyDown(kb, 'E') && BarrageCooldownLeft <= 0f && GauntletSkills.Has(GauntletSkills.Barrage))
                StartCoroutine(Barrage(Cursor()));
        }
        var mouse = Mouse.current;
        if (!Locked && act == Act.None && SkillSystem.RightPressed(mouse) && SpinCooldownLeft <= 0f)
            StartCoroutine(SpinDash());
        if (avatar) UpdateAvatar();
        if (screenFlash > 0f) screenFlash -= Time.unscaledDeltaTime;

        // the legs: run or stand by how fast the player is actually moving (skills play their own clips)
        Vector3 moved = transform.position - lastPosition; moved.y = 0f;
        lastPosition = transform.position;
        if (act == Act.None)
        {
            float speed = Time.deltaTime > 0f ? moved.magnitude / Time.deltaTime : 0f;
            Play(speed > 0.3f ? runClip : idleClip);
        }
    }

    // ═══════════════ Punch ═══════════════

    private int punchSide;

    private IEnumerator Punch(Vector3 aim)
    {
        act = Act.Punch;
        Face(aim);
        // jab, cross, jab, cross...
        Vector2 blow = punchSide++ % 2 == 0 ? jab : cross;
        float speed = punchClipSpeed * Mathf.Max(0.5f, AttackSpeed) * (avatar ? 1.25f : 1f);
        Restart(punchClip, speed, blow.x);
        yield return new WaitForSeconds((blow.y - blow.x) / speed);
        Vector3 at = transform.position + transform.forward * punchReach + Vector3.up;
        var struck = HitSphere(at, punchRadius, punchDamage);
        Fist(at, struck.Count > 0, avatar ? 1.3f : 0.8f);
        if (avatar)
        {
            // the thunder god's punches leap on to the nearest foe beyond
            Enemy next = Nearest(at, 6f, struck);
            if (next != null) { StormFx.Arc(at, Chest(next), 0.45f); StrikeTimes(next, 0.45f); }
        }
        miner.ChainFrom(struck);
        yield return new WaitForSeconds(punchRecover / Mathf.Max(0.5f, AttackSpeed));
        act = Act.None;
    }

    // ═══════════════ Q 뇌격 도약 ═══════════════

    private IEnumerator Leap(Vector3 aim)
    {
        act = Act.Leap;
        bool far = GauntletSkills.Has(GauntletSkills.LeapFar), master = GauntletSkills.Has(GauntletSkills.LeapMaster);
        leapReadyAt = Time.time + leapCooldown * CD * (avatar ? 0.5f : 1f);
        Vector3 dir = Flat(aim - transform.position);
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        transform.rotation = Quaternion.LookRotation(dir);
        float reach = leapDistance * (far ? 1.4f : 1f) * (avatar ? 1.2f : 1f);
        float dist = Mathf.Clamp(Flat3(aim - transform.position), 1.5f, reach);
        float radius = leapRadius + (far ? 0.8f : 0f) + (avatar ? 1f : 0f);

        // crouch, fists gathering lightning (the clip's own jump and landing squeezed into the leap)
        PlayClip(leapClip, (leapJump.y - leapJump.x) / (leapTime + 0.12f), leapJump.x);
        Vfx.Gather(transform.position + Vector3.up * 1.2f, 1.6f, StormFx.Blue, 0.15f, 60f);
        yield return new WaitForSeconds(0.12f);

        // the jump: the body moves over the floor, the model rises and falls on an arc above it
        var trails = HandTrails(avatar || master ? 0.5f : 0.25f);
        Vector3 start = transform.position;
        float moved = 0f;
        for (float t = 0f; t < leapTime; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / leapTime);
            float want = dist * k;
            controller.Move(dir * (want - moved) + Vector3.down * 0.5f * Time.deltaTime);
            moved = want;
            if (profile != null) profile.transform.localPosition = modelBasePos + Vector3.up * Mathf.Sin(k * Mathf.PI) * leapHeight;
            yield return null;
        }
        if (profile != null) profile.transform.localPosition = modelBasePos;
        foreach (var tr in trails) if (tr != null) { tr.transform.SetParent(null, true); Destroy(tr, 0.3f); }

        // the blow
        Vector3 ground = Ground(transform.position + dir * 0.8f);
        float mult = leapDamage * (1f + 0.15f * GauntletSkills.Rank(GauntletSkills.LeapPower)) * (master ? 1.4f : 1f) * (avatar ? 1.5f : 1f);
        var struck = HitSphere(ground + Vector3.up, radius, mult);
        CameraShake.Shake(0.25f, avatar ? 0.35f : 0.22f);
        StormFx.Impact(ground, radius * 2.2f, 0.45f);
        Vfx.Dust(ground, radius * 0.8f, 12, new Color(0.3f, 0.28f, 0.3f, 0.5f));
        Shockwave.Spawn(ground, radius, StormFx.Blue, 10);
        Vfx.Flash(ground + Vector3.up, radius * 1.5f, StormFx.Blue, 0.15f);
        if (master || avatar)
        {
            // 천둥 낙하: bolts out of the sky round the crater, the struck ones stunned
            int bolts = (master ? 3 : 0) + (avatar ? 3 : 0);
            for (int i = 0; i < bolts; i++)
            {
                float a = i * Mathf.PI * 2f / bolts + Random.Range(-0.3f, 0.3f);
                StormFx.Strike(ground + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.7f, 9f, 1.8f);
            }
            StormFx.Spread(ground, radius * 2.6f, 0.45f);
            if (master) foreach (var e in struck) if (e != null && !e.IsDead) e.Knockdown(0.8f);
        }
        miner.ChainFrom(struck);
        if (GauntletSkills.Has(GauntletSkills.Aftershock)) StartCoroutine(Aftershock(ground, radius, mult));
        yield return new WaitForSeconds(0.18f);
        act = Act.None;
    }

    private IEnumerator Aftershock(Vector3 ground, float radius, float mult)
    {
        yield return new WaitForSeconds(0.35f);
        float r = radius * 1.4f;
        HitSphere(ground + Vector3.up, r, mult * 0.6f);
        StormFx.Spread(ground, r * 2.4f, 0.4f);
        Shockwave.Spawn(ground, r, StormFx.Blue, 12);
        CameraShake.Shake(0.15f, 0.15f);
    }

    // ═══════════════ E 천둥 연타 ═══════════════

    private IEnumerator Barrage(Vector3 aim)
    {
        act = Act.Barrage;
        swiftBarrage = GauntletSkills.Has(GauntletSkills.Swift);
        bool master = GauntletSkills.Has(GauntletSkills.BarrageMaster), rising = GauntletSkills.Has(GauntletSkills.Rising);
        barrageReadyAt = Time.time + barrageCooldown * CD * (avatar ? 0.6f : 1f);
        Face(aim);
        int hits = barrageHits + 2 * GauntletSkills.Rank(GauntletSkills.MoreHits) + (avatar ? 4 : 0);
        float interval = barrageInterval / (swiftBarrage ? 1.3f : 1f) / (avatar ? 1.3f : 1f) / Mathf.Max(0.5f, AttackSpeed);
        float mult = barrageDamage * (avatar ? 1.4f : 1f);
        bool electric = avatar || master || GauntletSkills.Rank(GauntletSkills.MoreHits) > 0;
        for (int i = 0; i < hits && !Dead; i++)
        {
            // each punch: the clip snapped back to just before a jab or a cross, the fist flashing out in front
            Vector2 blow = i % 2 == 0 ? jab : cross;
            Restart(punchClip, 2.6f, Mathf.Lerp(blow.x, blow.y, 0.45f));
            if (swiftBarrage) TurnToward(Cursor(), 360f);
            Vector3 fwd = Flat(transform.forward);
            Vector3 side = Vector3.Cross(Vector3.up, fwd) * (i % 2 == 0 ? 0.35f : -0.35f);
            Vector3 fist = transform.position + Vector3.up * 1.1f + fwd * 1.3f + side + Random.insideUnitSphere * 0.15f;
            var struck = HitCone(fwd, barrageReach, barrageAngle, mult);
            Fist(fist, struck.Count > 0, electric ? 0.9f : 0.6f);
            if (electric && struck.Count > 0) StormFx.Arc(fist, Chest(struck[0]), 0.25f);
            if (master || avatar) Bolt(fist, fwd, mult * 0.8f);
            if (i % 3 == 0) CameraShake.Shake(0.05f, 0.05f);
            yield return new WaitForSeconds(interval);
        }
        if (rising && !Dead)
        {
            // 승천권: a rising uppercut that throws them off their feet
            Restart(comboClip, 1.6f, uppercut.x);
            yield return new WaitForSeconds((uppercut.y - uppercut.x) / 1.6f);
            Vector3 fwd = Flat(transform.forward);
            var struck = HitCone(fwd, barrageReach + 0.5f, barrageAngle, finisherDamage * (avatar ? 1.5f : 1f));
            Vector3 at = transform.position + fwd * 1.6f;
            foreach (var e in struck)
            {
                if (e == null || e.IsDead) continue;
                e.Knockdown(0.9f);
                Vfx.Bolt(Ground(e.transform.position), Chest(e) + Vector3.up * 3f, StormFx.Blue, 0.4f, 0.25f);
            }
            StormFx.Strike(Ground(at), 8f, 1.6f);
            Vfx.Slash(at + Vector3.up * 1.4f, fwd, 3.5f, StormFx.Blue, 80f, 120f, 0.22f);
            CameraShake.Shake(0.18f, 0.2f);
            yield return new WaitForSeconds(0.25f);
        }
        swiftBarrage = false;
        act = Act.None;
    }

    // 뇌권 난무: a bolt shot straight out of the fist, through everything in a line
    private void Bolt(Vector3 from, Vector3 fwd, float mult)
    {
        Vector3 to = from + fwd * boltReach;
        Vfx.Bolt(from, to, Random.value < 0.2f ? StormFx.Gold : StormFx.Blue, 0.3f, 0.12f);
        foreach (Enemy e in Enemy.Alive.ToArray())
        {
            if (e == null || e.IsDead) continue;
            Vector3 d = e.transform.position - transform.position; d.y = 0f;
            float along = Vector3.Dot(d, fwd);
            if (along < 0.5f || along > boltReach) continue;
            if ((d - fwd * along).magnitude > 0.9f) continue;
            StrikeTimes(e, mult);
        }
    }

    // ═══════════════ 우클릭 질풍 회전 ═══════════════

    // a short dash the way he faces, turning once round with a fist out (a spinning backfist), lightning dragging the
    // foes about him in — then one blow on the gathered pack (SpinPull). Stronger while transformed.
    private IEnumerator SpinDash()
    {
        act = Act.Spin;
        spinReadyAt = Time.time + SpinPull.Cooldown * CD;
        Vector3 dir = FacingDir();
        var dragged = new List<Enemy>();
        SpinPull.Begin(Ground(transform.position), dir);
        PlayClip(punchClip, 0.35f, cross.x + (cross.y - cross.x) * 0.6f);   // the cross held out as he turns
        var trails = HandTrails(avatar ? 0.55f : 0.4f);
        float reach = SpinPull.Radius + (avatar ? 1.5f : 0f);
        for (float t = 0f; t < SpinPull.Duration && !Dead; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / SpinPull.Duration);
            if (controller != null && controller.enabled)
                controller.Move(dir * (SpinPull.Distance / SpinPull.Duration) * (1.4f - 0.8f * k) * Time.deltaTime + Vector3.down * 0.5f * Time.deltaTime);
            // one full turn, quick at first and settling back to face the way he went
            float turn = 1f - (1f - k) * (1f - k);
            if (profile != null) profile.transform.localRotation = Quaternion.AngleAxis(-360f * turn, Vector3.up) * modelBaseRot;
            SpinPull.Drag(transform.position, reach, (4f + 14f * k) * Time.deltaTime, dragged);
            if (Random.value < 0.6f) SpinPull.Crackle(transform.position, dragged, k);
            yield return null;
        }
        if (profile != null) profile.transform.localRotation = modelBaseRot;
        foreach (var tr in trails) if (tr != null) { tr.transform.SetParent(null, true); Destroy(tr, 0.3f); }

        Vector3 ground = Ground(transform.position);
        float radius = SpinPull.HitRadius + (avatar ? 1f : 0f);
        var struck = HitSphere(ground + Vector3.up, radius, SpinPull.Damage * (avatar ? 1.4f : 1f));
        SpinPull.Finish(ground, dir);
        if (avatar) StormFx.Strike(ground, 10f, 2.2f);
        miner.ChainFrom(struck);
        yield return new WaitForSeconds(0.15f);
        act = Act.None;
    }

    // the way he faces on the floor (the keys turn him)
    private Vector3 FacingDir()
    {
        Vector3 f = Flat(transform.forward);
        return f.sqrMagnitude > 0.01f ? f : Vector3.forward;
    }

    // ═══════════════ R 뇌신 강림 ═══════════════

    private IEnumerator Ascend()
    {
        act = Act.Roar;
        const float roarSpeed = 1.6f;
        PlayClip(roarClip, roarSpeed, roarSmash.x);
        Vector3 ground = Ground(transform.position);
        // fists into the floor — and the sky answers: a bolt drives down into him
        float wind = (roarSmash.y - roarSmash.x) / roarSpeed;
        Vfx.Gather(transform.position + Vector3.up * 1.5f, 3f, StormFx.Blue, wind, 90f);
        yield return new WaitForSeconds(wind);
        StormFx.Strike(ground, 16f, 3.5f);
        StormFx.Impact(ground, 9f, 0.6f);
        StormFx.Spread(ground, 12f, 0.6f);
        Vfx.Flash(ground + Vector3.up * 2f, 14f, Color.white, 0.25f);
        StormFx.Light(ground + Vector3.up * 3f, 24f, 8f, 0.6f);
        screenFlash = 0.18f;
        CameraShake.Shake(0.5f, 0.5f);
        HitSphere(ground + Vector3.up, 4.5f, 0.8f);
        foreach (var e in EnemiesIn(ground, 4.5f)) e.Knockdown(0.5f);
        BecomeAvatar();
        // (he roars, crackling, before he moves again)
        yield return new WaitForSeconds(0.75f);
        act = Act.None;
    }

    private void BecomeAvatar()
    {
        avatar = true;
        avatarStart = Time.time;
        avatarEnd = Time.time + AvatarDuration;
        if (profile == null) return;
        Transform body = profile.transform;
        body.localScale = modelBaseScale * avatarScale;
        // the body becomes light: every part of him blazing white-blue, only a ghost of his own colours left
        GlowBody(true);
        // hair of living lightning, flaring up off his head like flames
        head = FindDeep(profile.transform, "Head");
        hairFlames = HairFire("ThunderHair", "glow", 120f, new Vector2(0.2f, 0.36f), new Vector2(0.32f, 0.5f), new Vector2(2.4f, 3.8f), 0.3f, 55f);
        hairSparks = HairFire("ThunderHairSparks", "spark", 45f, new Vector2(0.12f, 0.22f), new Vector2(0.06f, 0.12f), new Vector2(3.5f, 5.5f), 0.3f, 60f);
        avatarLook.Add(hairFlames.gameObject);
        avatarLook.Add(hairSparks.gameObject);
        // a soft white-blue glow all round him (on the player, not the scaled model: a sprite's size is in its
        // parent's units)
        bodyGlow = Vfx.Glow(transform, Vector3.zero, 3.2f / Scale(transform), new Color(0.25f, 0.55f, 1f, 0.45f), "glow", 0f);
        bodyGlow.transform.position = transform.position + Vector3.up * 1.0f;
        avatarLook.Add(bodyGlow);
        avatarLook.Add(Vfx.Aura(transform, 2.4f, StormFx.Blue));
        // fists of lightning
        foreach (var hand in new[] { leftHand, rightHand })
        {
            if (hand == null) continue;
            avatarLook.Add(Vfx.Glow(hand, Vector3.zero, 1.1f / Scale(hand), StormFx.Blue, "glow", 200f));
            var t = new GameObject("FistTrail"); t.transform.SetParent(hand, false);
            Vfx.Trail(t, StormFx.Blue, 0.35f, 0.15f);
            avatarLook.Add(t);
        }
        var l = new GameObject("AvatarLight").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.localPosition = Vector3.up * 2f;
        l.type = LightType.Point; l.color = StormFx.Blue; l.range = 9f; l.intensity = 3.5f; l.shadows = LightShadows.None;
        avatarLook.Add(l.gameObject);
        nextCrackle = nextSmite = Time.time + 0.5f;
    }

    private void UpdateAvatar()
    {
        // the light in him flickers like a live wire
        float flick = 1.1f + Mathf.PerlinNoise(Time.time * 9f, 0.3f) * 0.8f + (Random.value < 0.06f ? 1.2f : 0f);
        foreach (var m in glowMats) if (m != null) m.SetColor(EmissionId, BodyGlowColor * flick);
        if (bodyGlow != null) bodyGlow.transform.position = transform.position + Vector3.up * 1.0f * avatarScale;
        // now and then a spark leaps up out of the hair
        if (Random.value < 0.08f)
        {
            Vector3 top = HeadTop();
            Vfx.Bolt(top, top + Vector3.up * Random.Range(0.6f, 1.2f) + Random.insideUnitSphere * 0.4f, Color.white, 0.14f, 0.07f);
        }
        // lightning crawls over him
        if (Time.time >= nextCrackle)
        {
            nextCrackle = Time.time + 0.09f;
            Vector3 c = transform.position + Vector3.up * Random.Range(0.6f, 2.2f);
            Vfx.Bolt(c + Random.onUnitSphere * 0.9f, c + Random.onUnitSphere * 0.3f, Random.value < 0.2f ? StormFx.Gold : StormFx.Blue, 0.18f, 0.08f);
        }
        // 토르 현신: bolts fall on the foes round him
        if (GauntletSkills.Has(GauntletSkills.AvatarMaster) && Time.time >= nextSmite)
        {
            nextSmite = Time.time + 1f;
            Enemy e = Nearest(transform.position, 9f, null);
            if (e != null)
            {
                StormFx.Strike(Ground(e.transform.position), 10f, 2f);
                StormFx.Impact(Ground(e.transform.position), 3f, 0.3f);
                StrikeTimes(e, 0.6f);
            }
        }
        // the time runs out: the thunder leaves him
        if (Time.time >= avatarEnd && act != Act.Burst) EndAvatar(true);
    }

    // R again while transformed: the form is spent at once in one great discharge over everything around
    private IEnumerator Release()
    {
        act = Act.Burst;
        float left = Mathf.Max(0f, avatarEnd - Time.time);
        const float burstSpeed = 2.2f;
        PlayClip(burstClip, burstSpeed, burstSlam.x);
        Vector3 ground = Ground(transform.position);
        float wind = (burstSlam.y - burstSlam.x) / burstSpeed;
        Vfx.Gather(transform.position + Vector3.up * 1.5f, 5f, StormFx.Blue, wind, 120f);
        StormFx.Light(transform.position + Vector3.up * 2f, 16f, 6f, wind);
        // a column of lightning gathers into the raised fist
        Transform fist = rightHand != null ? rightHand : transform;
        for (float t = 0f; t < wind; t += 0.06f)
        {
            Vfx.Bolt(fist.position + Vector3.up * 6f + Random.insideUnitSphere, fist.position, StormFx.Blue, 0.35f, 0.08f);
            yield return new WaitForSeconds(0.06f);
        }

        float mult = releaseDamage * (1f + releasePerSecondLeft * left) * (GauntletSkills.Has(GauntletSkills.Overload) ? 1.5f : 1f);
        screenFlash = 0.35f;
        CameraShake.Shake(0.8f, 0.7f);
        StormFx.Spread(ground, 26f, 0.7f);
        StormFx.Impact(ground, 14f, 0.6f);
        StormFx.Strike(ground, 18f, 4f);
        StormFx.Light(ground + Vector3.up * 4f, 50f, 10f, 0.7f);
        var targets = new List<Enemy>();
        foreach (Enemy e in Enemy.Alive.ToArray())
            if (e != null && !e.IsDead && e.isActiveAndEnabled && Flat3(e.transform.position - transform.position) < releaseRadius)
                targets.Add(e);
        targets.Sort((a, b) => Flat3(a.transform.position - transform.position).CompareTo(Flat3(b.transform.position - transform.position)));
        EndAvatar(false);
        avatarReadyAt = Time.time + avatarCooldown * CD;
        StartCoroutine(Discharge(targets, mult, ground));
        yield return new WaitForSeconds(0.5f);
        act = Act.None;
    }

    // the blast rolls outward: every foe in the area is struck from the sky, nearest first
    private IEnumerator Discharge(List<Enemy> targets, float mult, Vector3 ground)
    {
        bool rain = GauntletSkills.Has(GauntletSkills.AvatarMaster);
        foreach (var e in targets)
        {
            if (e == null || e.IsDead) continue;
            Vector3 g = Ground(e.transform.position);
            StormFx.Strike(g, 12f, 2.4f);
            StormFx.Impact(g, 3.5f, 0.35f);
            StormFx.Arc(transform.position + Vector3.up * 1.5f, Chest(e), 0.35f);
            StrikeTimes(e, mult);
            e.Knockdown(0.6f);
            yield return new WaitForSeconds(0.04f);
        }
        if (!rain) yield break;
        // 토르 현신: and lightning keeps raining down round him for a moment
        for (int i = 0; i < 18; i++)
        {
            Vector3 p = ground + new Vector3(Random.Range(-12f, 12f), 0f, Random.Range(-12f, 12f));
            StormFx.Strike(Ground(p), 10f, 1.8f);
            foreach (var e in EnemiesIn(p, 2f)) StrikeTimes(e, 0.4f);
            yield return new WaitForSeconds(0.05f);
        }
    }

    private void EndAvatar(bool fade)
    {
        if (!avatar && avatarLook.Count == 0) return;
        bool was = avatar;
        avatar = false;
        foreach (var go in avatarLook) if (go != null) Destroy(go);
        avatarLook.Clear();
        GlowBody(false);
        hairFlames = hairSparks = null;
        bodyGlow = null;
        if (profile != null) profile.transform.localScale = modelBaseScale;
        if (was && fade)
        {
            Vfx.Burst(transform.position + Vector3.up * 1.5f, StormFx.Blue, 16, 4f, 0.12f, 0.3f);
            Vfx.Flash(transform.position + Vector3.up * 1.5f, 3f, StormFx.Blue, 0.2f);
        }
        if (was && avatarReadyAt < Time.time + 1f) avatarReadyAt = Time.time + avatarCooldown * CD;
    }

    // ═══════════════ The thunder god's look ═══════════════

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Color BodyGlowColor = new Color(0.45f, 0.72f, 1f);
    private readonly Dictionary<Renderer, Material[]> bodyMats = new Dictionary<Renderer, Material[]>();
    private readonly List<Material> glowMats = new List<Material>();
    private Transform head;
    private ParticleSystem hairFlames, hairSparks;
    private GameObject bodyGlow;

    // on: each of his meshes swapped for a copy of its material washed pale blue and burning with light (the bloom does
    // the rest); off: his own materials back
    private void GlowBody(bool on)
    {
        if (!on)
        {
            foreach (var kv in bodyMats) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            bodyMats.Clear();
            foreach (var m in glowMats) if (m != null) Destroy(m);
            glowMats.Clear();
            return;
        }
        if (profile == null || bodyMats.Count > 0) return;
        foreach (var r in profile.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
            if (r.name.StartsWith("Vfx") || r.name.StartsWith("Sprite_") || r.GetComponent<ParticleSystem>() != null) continue;
            var originals = r.sharedMaterials;
            var swapped = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                if (originals[i] == null) continue;
                // (the models come in on glTF shaders, whose names differ — so a plain URP Lit copy carrying his texture,
                // which lights up through its emission: the bright parts of him brightest, his features still faintly there)
                Texture tex = null;
                foreach (var p in new[] { "baseColorTexture", "_BaseMap", "_MainTex" })
                    if (originals[i].HasProperty(p) && originals[i].GetTexture(p) != null) { tex = originals[i].GetTexture(p); break; }
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = originals[i].name + " (뇌신)" };
                m.SetTexture("_BaseMap", tex);
                m.SetColor(BaseColorId, new Color(0.75f, 0.88f, 1f));
                m.SetFloat("_Smoothness", 0.2f);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.SetColor(EmissionId, BodyGlowColor * 3f);
                swapped[i] = m;
                glowMats.Add(m);
            }
            bodyMats[r] = originals;
            r.sharedMaterials = swapped;
        }
    }

    // a fountain off the top of his head: born in a ring round the crown and streaming up, white at the root and blue at
    // the tips, shrinking as they rise — spiky like flames, but lightning
    private ParticleSystem HairFire(string name, string texture, float rate, Vector2 life, Vector2 size, Vector2 speed, float stretch, float spread)
    {
        var go = new GameObject(name);
        go.transform.position = HeadTop();
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = Vfx.Material(texture, true);
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = stretch;
        renderer.lengthScale = 1.2f;
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        // (not too bright: past white the scene's grading tints the highlights warm, and the hair must stay blue)
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.65f, 1f) * 1.25f, new Color(0.15f, 0.42f, 1f) * 1.4f);
        main.gravityModifier = -0.2f;
        main.maxParticles = 300;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = spread;
        shape.radius = 0.38f * avatarScale;
        shape.radiusThickness = 0.6f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.95f, 1f), 0f), new GradientColorKey(new Color(0.45f, 0.72f, 1f), 0.3f), new GradientColorKey(new Color(0.15f, 0.35f, 1f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.75f, 0.12f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1.4f;
        noise.frequency = 2.5f;
        noise.scrollSpeed = 2f;
        ps.Play();
        return ps;
    }

    // the crown of his head (the brawler's head is drawn big — ChibiProportions — so measure it, not guess)
    private Vector3 HeadTop()
    {
        if (head == null) return transform.position + Vector3.up * 2.1f * avatarScale;
        return head.position + Vector3.up * HeadTopOffset * avatarScale;
    }

    [SerializeField] private float HeadTopOffset = 0.6f;

    // after the animation has moved him: the hair stays on his head
    private void LateUpdate()
    {
        if (!avatar) return;
        Vector3 top = HeadTop();
        if (hairFlames != null) hairFlames.transform.position = top;
        if (hairSparks != null) hairSparks.transform.position = top;
    }

    // ═══════════════ Helpers ═══════════════

    private float AttackSpeed => stats != null ? stats.AttackSpeed : 1f;

    [SerializeField] private float haloSize = 2.6f;
    // the halo floats behind his shoulders
    private Vector3 HaloPoint => transform.position + Vector3.up * 1.05f * avatarScale - Flat(transform.forward) * 0.5f;
    private static float Scale(Transform t) => Mathf.Max(0.01f, t.lossyScale.x);

    private Vector3 Cursor()
    {
        var aim = FindAnyObjectByType<ClickToDig>();
        return aim != null ? aim.CursorGroundPoint() : transform.position + transform.forward * 4f;
    }

    private void Face(Vector3 point)
    {
        Vector3 to = point - transform.position; to.y = 0f;
        if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
    }

    private void TurnToward(Vector3 point, float degPerSec)
    {
        Vector3 to = point - transform.position; to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), degPerSec * Time.deltaTime);
    }

    private float Feet => transform.position.y - (controller != null ? controller.height * 0.5f - controller.center.y : 0.9f);
    private Vector3 Ground(Vector3 p) => new Vector3(p.x, Feet, p.z);
    private static Vector3 Chest(Enemy e) => e.transform.position + Vector3.up * (0.9f * Mathf.Max(0.6f, e.transform.localScale.y));
    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.zero; }
    private static float Flat3(Vector3 v) { v.y = 0f; return v.magnitude; }

    // a blow landing: a white-blue flash at the fist and sparks (bigger when it connects)
    private static void Fist(Vector3 at, bool connected, float size)
    {
        Vfx.Flash(at, size * (connected ? 1.6f : 1f), connected ? Color.white : StormFx.Blue, 0.08f);
        Vfx.Burst(at, StormFx.Blue, connected ? 8 : 3, 5f, 0.1f, 0.4f);
        if (connected && Random.value < 0.3f) Vfx.Burst(at, StormFx.Gold, 3, 4f, 0.08f, 0.4f);
    }

    private List<GameObject> HandTrails(float width)
    {
        var list = new List<GameObject>();
        foreach (var hand in new[] { leftHand, rightHand })
        {
            if (hand == null) continue;
            var t = new GameObject("LeapTrail"); t.transform.SetParent(hand, false);
            Vfx.Trail(t, StormFx.Blue, width, 0.2f);
            list.Add(t);
        }
        return list;
    }

    private Enemy Nearest(Vector3 from, float range, List<Enemy> skip)
    {
        Enemy best = null; float bestD = range;
        foreach (Enemy e in Enemy.Alive)
        {
            if (e == null || e.IsDead || !e.isActiveAndEnabled || (skip != null && skip.Contains(e))) continue;
            float d = Vector3.Distance(from, e.transform.position);
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

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
            if (Flat3(e.transform.position - center) <= radius) list.Add(e);
        }
        return list;
    }

    private List<Enemy> HitSphere(Vector3 center, float radius, float multiplier)
    {
        var struck = EnemiesIn(center, radius);
        foreach (Enemy e in struck) StrikeTimes(e, multiplier);
        return struck;
    }

    private List<Enemy> HitCone(Vector3 fwd, float reach, float angle, float multiplier)
    {
        var struck = new List<Enemy>();
        foreach (Enemy e in EnemiesIn(transform.position, reach + 0.6f))
        {
            Vector3 d = e.transform.position - transform.position; d.y = 0f;
            if (d.magnitude > 0.8f && Vector3.Angle(fwd, d) > angle * 0.5f) continue;
            struck.Add(e);
            StrikeTimes(e, multiplier);
        }
        return struck;
    }

    private void StrikeTimes(Enemy enemy, float multiplier)
    {
        if (enemy == null || enemy.IsDead) return;
        float damage = RollDamage(out bool critical);
        Strike(enemy, Mathf.Max(1, Mathf.RoundToInt(damage * multiplier)), critical);
    }

    private bool HasClip(string clip) => anim != null && !string.IsNullOrEmpty(clip) && anim.GetClip(clip) != null;

    private void Play(string clip)
    {
        if (HasClip(clip) && !anim.IsPlaying(clip)) anim.CrossFade(clip, crossFade);
    }

    private void PlayClip(string clip, float speed, float from = 0f)
    {
        if (!HasClip(clip)) return;
        anim[clip].speed = speed;
        anim[clip].time = from;
        anim.CrossFade(clip, 0.06f);
    }

    // restart a clip from 'from' seconds in: rapid punches snap the same strikes again and again
    private void Restart(string clip, float speed, float from = 0f)
    {
        if (!HasClip(clip)) return;
        var s = anim[clip];
        s.speed = speed;
        s.time = from;
        if (!anim.IsPlaying(clip)) anim.CrossFade(clip, 0.04f);
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // ═══════════════ HUD ═══════════════

    // The skills sit in the game's round slots (Q, E, R, right click, Space): an icon once learned, the cooldown over it,
    // the R slot ringed while transformed
    private Texture2D iconLeap, iconBarrage, iconAvatar;
    private bool slotsShown;

    private void Slots(bool on)
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.slotIcons == null || gm.slotCooldowns == null) return;
        if (!on)
        {
            if (!slotsShown) return;
            for (int i = 0; i < 5; i++) { gm.slotIcons[i] = null; gm.slotNames[i] = null; gm.slotLocked[i] = false; gm.slotCooldowns[i] = 0f; gm.slotActive[i] = false; }
            slotsShown = false;
            return;
        }
        iconLeap ??= Resources.Load<Texture2D>("UI/storm_strike");
        iconBarrage ??= Resources.Load<Texture2D>("UI/storm_chain");
        iconAvatar ??= Resources.Load<Texture2D>("Vfx/storm_halo");
        // (the game's skill bar: a slot is greyed until its skill is learned)
        gm.slotIcons[0] = iconLeap; gm.slotNames[0] = "뇌격 도약"; gm.slotLocked[0] = !GauntletSkills.Has(GauntletSkills.Leap);
        gm.slotIcons[1] = iconBarrage; gm.slotNames[1] = "천둥 연타"; gm.slotLocked[1] = !GauntletSkills.Has(GauntletSkills.Barrage);
        gm.slotIcons[2] = iconAvatar; gm.slotNames[2] = avatar ? "변신 해제" : "뇌신 강림"; gm.slotLocked[2] = !GauntletSkills.Has(GauntletSkills.Avatar);
        gm.slotIcons[3] = SkillBar.Icon("UI/gs_whirlwind"); gm.slotNames[3] = "질풍 회전";
        gm.slotIcons[4] = SkillBar.Icon("UI/gs_rush"); gm.slotNames[4] = "대시";
        gm.slotCooldowns[0] = LeapCooldownLeft;
        gm.slotCooldowns[1] = act == Act.Barrage ? 0f : BarrageCooldownLeft;
        gm.slotCooldowns[2] = avatar ? 0f : AvatarCooldownLeft;
        gm.slotCooldowns[3] = 0f;
        gm.slotCooldowns[4] = movement != null ? movement.DashCooldownLeft : 0f;
        gm.slotActive[0] = act == Act.Leap;
        gm.slotActive[1] = act == Act.Barrage;
        gm.slotActive[2] = avatar;
        slotsShown = true;
    }

    // While transformed: what is left of the form, as a bar over the middle of the screen's bottom
    public override void DrawHud()
    {
        if (!avatar) return;
        timeStyle ??= UiFont.Style(14, TextAnchor.MiddleCenter, FontStyle.Bold);
        float k = Mathf.Clamp01((avatarEnd - Time.time) / AvatarDuration);
        var r = new Rect((Screen.width - 320f) / 2f, Screen.height - 74f, 320f, 12f);
        UiFont.Fill(r, new Color(0f, 0f, 0f, 0.7f));
        UiFont.Fill(new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * k, r.height - 4f), k < 0.25f ? StormFx.Gold : StormFx.Blue);
        GUI.Label(new Rect(r.x, r.y - 20f, r.width, 18f), "뇌신 강림 — [R] 해제 · 전기 폭발", timeStyle);
    }

    // the white-blue flash over the whole screen when the thunder god comes down or lets go
    private void OnGUI()
    {
        if (screenFlash <= 0f || !IsActive) return;
        GUI.color = new Color(0.75f, 0.9f, 1f, Mathf.Clamp01(screenFlash / 0.35f) * 0.55f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
