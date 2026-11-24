using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

// Nidhogg, the dragon gnawing at Yggdrasil's roots: the mid-boss of Niflheim's depths. Its model has no skeleton,
// so NidhoggRig bends the mesh (legs, neck, wings, tail, chest) and this script poses it beat by beat.
// Three telegraphed moves - a lunging bite, a line of frost breath and a spinning jet of frost all around it -
// and at half health a shriek that calls up the dead.
[RequireComponent(typeof(CharacterController))]
public class NidhoggBoss : Enemy
{
    [SerializeField] private Transform visual;
    [SerializeField] private int maxHealth = 150;
    [SerializeField] private float damage = 26f;
    [SerializeField] private float moveSpeed = 2.4f;
    [SerializeField] private float turnSpeed = 90f;
    [SerializeField] private float reach = 3.5f;        // roughly from its centre to the tip of its jaws
    [SerializeField] private float mouthHeight = 3.4f;  // where breath and frost leave its jaws
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private Color hitTint = new Color(1f, 0.45f, 0.5f);

    private static readonly Color Frost = new Color(0.45f, 0.9f, 1f);

    private CharacterController controller;
    private NidhoggRig rig;
    private Transform player;
    private PlayerStats playerStats;
    private int health;
    private bool dead, busy, roared;
    private float cooldown = 1.5f;
    private float stunnedUntil;
    private float verticalVelocity;
    private Vector3 visualPosition;
    private Quaternion visualRotation;
    private Quaternion pose = Quaternion.identity;   // the whole body rearing / lunging
    private float lift;                              // the whole body raised (rearing on its hind legs)
    private Coroutine action;
    private GameObject warning;
    private bool flying;               // up in the air: it can't be hit and doesn't walk
    private bool phaseTwo;             // awakened (BossAwakening): ice meteors fall
    private float flightTimer = FlightEvery;   // seconds until it next takes to the air
    private const float FlightEvery = 40f;
    private float floorY;              // the arena floor, for anything that lands on it
    private float idleTimer = 4f;      // until its next idle flourish
    private float flourishUntil;       // while a flourish plays, the stalking pose leaves the rig alone

    // The shriek at half health asks the room for reinforcements
    public event Action<NidhoggBoss> Roared;

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        controller.stepOffset = 0.2f;   // low enough that it can't climb onto the player
        if (visual == null && transform.childCount > 0)
            visual = transform.GetChild(0);
        if (visual != null)
        {
            visualPosition = visual.localPosition;
            visualRotation = visual.localRotation;
            rig = GetComponent<NidhoggRig>();
            if (rig == null)
                rig = gameObject.AddComponent<NidhoggRig>();
            rig.Bind(visual);
        }
        health = maxHealth;
    }

    private void Start()
    {
        floorY = transform.position.y;
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
        damage *= damageMultiplier;
    }

    // Name, toughness and size for the realm it guards
    public void Setup(string bossName, int bossHealth, float bossDamage, float size)
    {
        DisplayName = bossName;
        maxHealth = health = bossHealth;
        damage = bossDamage;
        transform.localScale = Vector3.one * size;
        GoldDrop = 20;
    }

    private void Update()
    {
        if (dead)
            return;
        if (flying)
        {
            Animate(false);
            return;
        }

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity += gravity * Time.deltaTime;

        // the flight comes round every 40 s of the fight, whatever it is doing meanwhile (it takes off at its next opening)
        if (GameManager.IsRunActive && !BossCutscene.Playing)
            flightTimer -= Time.deltaTime;

        Vector3 move = Vector3.zero;
        float turning = 0f;
        float wantSpeed = 0f;
        // between moves the body settles back from its last pose instead of snapping straight
        if (!busy)
        {
            float settle = 1f - Mathf.Exp(-5f * Time.deltaTime);
            pose = Quaternion.Slerp(pose, Quaternion.identity, settle);
            lift = Mathf.Lerp(lift, 0f, settle);
        }
        if (!busy && Time.time >= stunnedUntil && player != null && GameManager.IsRunActive)
        {
            Vector3 toPlayer = Flat(player.position - transform.position);
            float distance = toPlayer.magnitude;
            float offAngle = 0f;
            if (distance > 0.01f)
            {
                // a heavy body: it swings round with weight, easing into and out of the turn
                float want = Quaternion.LookRotation(toPlayer).eulerAngles.y;
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, want, ref yawVelocity, 0.42f, turnSpeed * 1.4f);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                turning = Mathf.Clamp01(Mathf.Abs(yawVelocity) / Mathf.Max(1f, turnSpeed));
                offAngle = Mathf.Abs(Mathf.DeltaAngle(yaw, want));
            }
            bool facing = Vector3.Angle(transform.forward, toPlayer) < 25f;

            cooldown -= Time.deltaTime;
            float r = Reach;
            if (flightTimer <= 0f && (cooldown <= 0f || cooldown < 0.8f))   // (doesn't wait out a long rest for it)
            {
                flightTimer = FlightEvery;
                action = StartCoroutine(SkyAssault());
            }
            else if (cooldown <= 0f && distance < r * 1.3f)
            {
                float roll = Random.value;
                action = StartCoroutine(roll < 0.25f ? FrostSpin() : roll < 0.5f ? Bite() : roll < 0.75f ? TailSwipe() : Stomp());
            }
            else if (cooldown <= 0f && facing && distance < r * 3.2f)
            {
                float roll = Random.value;
                action = StartCoroutine(roll < 0.35f ? Bite() : roll < 0.55f ? FrostBreath() : roll < 0.75f ? WingBuffet() : BreathBeam());
            }
            else if (cooldown <= 0f && facing && distance < r * 5f)
                action = StartCoroutine(Random.value < 0.5f ? FrostBreath() : BreathBeam());
            else if (cooldown <= 0f && facing && distance < 30f && Random.value < 0.5f)
                action = StartCoroutine(BreathBeam());   // far off: the breath that crosses the whole arena
            else if (cooldown <= 0f && distance >= r * 5f)
            {
                cooldown = 1.2f;   // (it walks in for a while before deciding again)
                wantSpeed = moveSpeed;
            }
            else if (distance > r * 1.1f)
                wantSpeed = moveSpeed;
            // it turns to face its prey before striding off, slowing right down for a sharp turn
            wantSpeed *= Mathf.Lerp(1f, 0.2f, Mathf.InverseLerp(30f, 90f, offAngle));

            // now and then, standing between moves, it snarls, stretches its wings or shakes its head
            bool still = speedNow < 0.2f && cooldown > 0.8f;
            if (still && (idleTimer -= Time.deltaTime) <= 0f)
            {
                idleTimer = Random.Range(3.5f, 6.5f);
                StartCoroutine(Flourish(Random.Range(0, 3)));
            }

            // between moves it stalks: head low and turned to follow its prey, wings half folded
            if (!busy && rig != null && Time.time >= flourishUntil)
            {
                float stride = Mathf.Clamp01(speedNow / Mathf.Max(0.1f, moveSpeed));
                float look = Vector3.SignedAngle(transform.forward, toPlayer, Vector3.up);
                rig.headYaw = Mathf.Clamp(look, -35f, 35f);
                rig.headPitch = Mathf.Lerp(4f, 12f, stride) + Mathf.Sin(Time.time * 1.3f) * 2f;   // a slow breath
                rig.neckReach = Mathf.Lerp(0f, 0.3f, stride);
                rig.wingSpread = -6f + Mathf.Sin(Time.time * 0.9f) * 2f;
                rig.crouch = Mathf.Lerp(0f, 0.15f, stride);
                rig.tailSway = Mathf.Clamp(-yawVelocity * 0.25f, -20f, 20f);   // the tail swings out of the turn
            }
        }

        // it gathers speed over a few strides and comes to a stop more quickly, never jumping straight to full pace
        float rate = wantSpeed > speedNow ? moveSpeed * 1.4f : moveSpeed * 3f;
        speedNow = Mathf.MoveTowards(speedNow, busy ? 0f : wantSpeed, rate * Time.deltaTime);
        if (!busy)
            move = transform.forward * speedNow;
        if (busy || player == null)
            yawVelocity = Mathf.Lerp(yawVelocity, 0f, 1f - Mathf.Exp(-6f * Time.deltaTime));

        controller.Move((move + Vector3.up * verticalVelocity) * Time.deltaTime);
        float walk = Mathf.Clamp01(speedNow / Mathf.Max(0.1f, moveSpeed));
        if (rig != null && !busy)
            rig.gait = Mathf.Max(walk, turning * 0.6f);
        Animate(walk);
    }

    private float speedNow;      // its pace right now, easing toward what it wants
    private float yawVelocity;   // how fast it is turning (degrees a second)
    private float stridePhase;   // where it is in its stride
    private float leanNow;       // leaning into a turn

    private float Reach => reach * transform.localScale.x;
    private Vector3 Mouth => transform.position + transform.forward * Reach * 0.9f + Vector3.up * mouthHeight * transform.localScale.y;

    // The whole body: a heavy roll from side to side with each stride, and the rear / lunge pose on top
    private void Animate(bool walking) => Animate(walking ? 1f : 0f);

    // walk 0..1: how much of a full stride it is in. The stride's rhythm follows its pace, the sway and bob fade in
    // and out with it, and it leans into its turns.
    private void Animate(float walk)
    {
        if (visual == null)
            return;
        stridePhase += Time.deltaTime * 2.75f * Mathf.Lerp(0.55f, 1f, walk) * (walk > 0.02f ? 1f : 0f);
        float breathe = Mathf.Sin(Time.time * 1.6f) * 0.03f;
        float bob = Mathf.Abs(Mathf.Sin(stridePhase)) * 0.12f * walk + breathe * (1f - walk);
        float roll = Mathf.Sin(stridePhase) * 3f * walk;
        leanNow = Mathf.Lerp(leanNow, Mathf.Clamp(-yawVelocity * 0.05f, -7f, 7f), 1f - Mathf.Exp(-4f * Time.deltaTime));
        float pitch = walk * 2.5f;   // the head and chest dip forward into the walk
        visual.localPosition = visualPosition + Vector3.up * (bob + lift);
        visual.localRotation = pose * Quaternion.Euler(pitch, 0f, roll + leanNow) * visualRotation;
    }

    // ── Bite: coils back with its head drawn in low, then the neck whips out and the jaws slam down ──
    private IEnumerator Bite()
    {
        busy = true;
        float r = Reach;
        Vector3 dir = transform.forward;
        Vector3 from = transform.position;
        // the snap carries it up to the player, never through or over them
        float lunge = player != null ? Mathf.Clamp(FlatDistance(player.position, from) - r * 0.9f, 0f, r * 1.2f) : r * 1.2f;
        warning = Telegraph.Line(from, dir, r * 2.6f, 3f, 0.8f);

        // coil: crouch on bent legs, neck drawn back, wings tucked, tail lashing; the jaws slowly part in a snarl
        SetRig(pitch: -14f, yaw: 0f, neck: -0.8f, wings: -12f, crouch: 0.45f, tail: -18f, swell: 0.03f, ease: 6f, jaw: 4f, jawEase: 4f);
        yield return Pose(Quaternion.Euler(-6f, 0f, 0f), 0.8f);
        ClearWarning();

        // strike: the neck shoots forward with the jaws flung wide, and they slam shut as the head lands
        const float strike = 0.24f;
        SetRig(pitch: 18f, yaw: 0f, neck: 1.6f, wings: 10f, crouch: 0.05f, tail: 22f, swell: 0f, ease: 26f, jaw: 24f, jawEase: 30f);
        bool landed = false, snapped = false;
        for (float t = 0f; t < strike; t += Time.deltaTime)
        {
            float k = t / strike;
            pose = Quaternion.Euler(Mathf.Lerp(-6f, 10f, k), 0f, 0f);
            controller.Move(dir * (lunge / strike) * Time.deltaTime);
            if (!snapped && k > 0.6f)
            {
                snapped = true;
                rig.jawOpen = NidhoggRig.JawShut;
                rig.jawEase = 45f;
            }
            if (!landed && snapped && InStrip(from, dir, r * 2.6f, 1.6f))
            {
                landed = true;
                Hurt(damage, dir * 6f);
            }
            yield return null;
        }
        CameraShake.Shake(0.25f, 0.12f);
        Shockwave.Spawn(transform.position + dir * r, 1.5f, Frost, 10);

        // hold the bite with teeth clenched, worry it side to side, then let go and draw back
        yield return new WaitForSeconds(0.15f);
        SetRig(pitch: 12f, yaw: 12f, neck: 0.9f, wings: 0f, crouch: 0.1f, tail: 0f, swell: 0f, ease: 12f, jaw: NidhoggRig.JawShut, jawEase: 30f);
        yield return new WaitForSeconds(0.14f);
        rig.headYaw = -12f;
        yield return new WaitForSeconds(0.14f);
        rig.headYaw = 0f;
        rig.jawOpen = -8f;      // the jaws come apart as it lets go
        rig.jawEase = 8f;
        yield return new WaitForSeconds(0.1f);
        RelaxRig(5f);
        yield return Pose(Quaternion.identity, 0.4f);
        EndAction(1.2f);
    }

    // ── Frost breath: rears up drawing a deep breath, frost gathering in its jaws, then pours a line of mist ──
    private IEnumerator FrostBreath()
    {
        busy = true;
        float r = Reach;
        Vector3 dir = transform.forward;
        Vector3 from = transform.position + dir * r * 0.5f;
        const float length = 13f, width = 4f;
        warning = Telegraph.Line(from, dir, length, width, 1.2f);

        // inhale: head thrown back, chest swelling, wings opening, the body rising on its legs
        SetRig(pitch: -24f, yaw: 0f, neck: -0.5f, wings: 30f, crouch: -0.25f, tail: 0f, swell: 0.08f, ease: 4f, jaw: -6f, jawEase: 3f);
        ParticleSystem gather = Keep(Vfx.Gather(Mouth, 3f, Frost, 1.2f, 80f));
        float pulse = 0f;
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / 1.2f);
            pose = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-16f, 0f, 0f), k);
            lift = Mathf.Lerp(0f, 0.4f, k);
            if (gather != null)
                gather.transform.position = Mouth;
            if ((pulse -= Time.deltaTime) <= 0f)
            {
                pulse = 0.08f;
                Vfx.Flash(Mouth, Mathf.Lerp(0.4f, 1.8f, k), Frost, 0.16f);
            }
            yield return null;
        }
        if (gather != null)
            Destroy(gather.gameObject);
        ClearWarning();

        // exhale: head slams forward and down, sweeping a little side to side as a cone of frost and freezing mist
        // pours out along the ground, settling as rime where it passed
        SetRig(pitch: 16f, yaw: 0f, neck: 1.1f, wings: 38f, crouch: 0.15f, tail: 0f, swell: -0.03f, ease: 14f, jaw: 24f, jawEase: 18f);
        lift = 0f;
        CameraShake.Shake(0.8f, 0.08f);
        const float pour = 0.8f;
        ParticleSystem frost = Keep(Vfx.Fire(Mouth, (dir + Vector3.down * 0.18f).normalized, length * 0.85f, 14f, pour));
        if (frost != null)
        {
            var main = frost.main;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.97f, 1f), new Color(0.4f, 0.8f, 1f));
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            var emission = frost.emission;
            emission.rateOverTime = 110f;
        }
        for (float s = 2f; s < length; s += 2.2f)
            Vfx.Sprite("glow", from + dir * s + Vector3.up * 0.2f, Mathf.Lerp(2f, width * 1.1f, s / length), Frost * 0.45f, 2f, 0.6f, 1.1f, 0f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        bool landed = false;
        float mist = 0f;
        for (float t = 0f; t < pour; t += Time.deltaTime)
        {
            rig.headYaw = Mathf.Sin(t * 9f) * 9f;
            pose = Quaternion.Euler(6f + Mathf.Sin(t * 22f) * 1f, 0f, 0f);
            Vector3 aim = Quaternion.Euler(0f, rig.headYaw * 0.6f, 0f) * dir;
            if (frost != null)
            {
                frost.transform.position = Mouth;
                frost.transform.rotation = Quaternion.LookRotation((aim + Vector3.down * 0.18f).normalized);
            }
            if ((mist -= Time.deltaTime) <= 0f)
            {
                mist = 0.06f;
                float along = Random.Range(0.2f, 1f);
                Vector3 at = Vector3.Lerp(Mouth, from + aim * length * along + Vector3.up * 0.6f, along) + side * Random.Range(-1f, 1f) * width * 0.35f * along;
                MistVfx.Bank(at, aim * 4f + side * Random.Range(-1f, 1f), Mathf.Lerp(1.4f, 3f, along), 0.1f, 0.12f, 0.35f, 0.55f, new Color(0.78f, 0.94f, 1f));
            }
            if (!landed && InStrip(from, dir, length, width / 2f))
            {
                landed = true;
                Hurt(damage * 0.8f, dir * 3f);
            }
            yield return null;
        }
        RelaxRig(5f);
        yield return Pose(Quaternion.identity, 0.4f);
        EndAction(1.8f);
    }

    // ── Frost spin: spreads its wings wide and turns a full circle, sweeping a jet of frost all around it ──
    private IEnumerator FrostSpin()
    {
        busy = true;
        float radius = Reach * 2.6f;
        const float spin = 1.6f;
        warning = Telegraph.Circle(transform.position, radius, 1.1f);

        // brace: legs bent wide, wings flung open, head reared back drawing breath
        SetRig(pitch: -24f, yaw: 0f, neck: -0.3f, wings: 45f, crouch: 0.35f, tail: 0f, swell: 0.07f, ease: 5f, jaw: -10f, jawEase: 4f);
        yield return Pose(Quaternion.Euler(-12f, 0f, 0f), 1.1f);
        ClearWarning();

        // spin: neck stretched out low, wings held wide, tail swung out behind, feet stamping round
        SetRig(pitch: 14f, yaw: 0f, neck: 1.3f, wings: 50f, crouch: 0.2f, tail: -30f, swell: -0.02f, ease: 14f, jaw: 20f, jawEase: 16f);
        rig.gait = 0.8f;
        rig.wingFlap = 6f;
        Material mist = Mats.Get("nidhogg_breath", Frost, 0.4f, Frost * 1.8f);
        Quaternion start = transform.rotation;
        bool landed = false;
        pose = Quaternion.Euler(6f, 0f, 0f);
        for (float t = 0f; t < spin; t += Time.deltaTime)
        {
            transform.rotation = start * Quaternion.Euler(0f, 360f * t / spin, 0f);
            Vector3 dir = transform.forward;
            for (int i = 0; i < 2; i++)
            {
                float along = Random.Range(0f, radius - Reach * 0.6f);
                GameObject puff = Mats.Shape(PrimitiveType.Sphere, null, Vector3.Lerp(Mouth, transform.position + dir * (Reach * 0.6f + along) + Vector3.up * 0.7f, along / radius),
                    Vector3.one * Random.Range(0.5f, 1.1f), mist);
                Destroy(puff, 0.4f);
            }
            // the jet catches anyone inside its reach within a narrow wedge ahead
            if (!landed && player != null)
            {
                Vector3 toPlayer = Flat(player.position - transform.position);
                if (toPlayer.magnitude < radius && Vector3.Angle(dir, toPlayer) < 18f)
                {
                    landed = true;
                    Hurt(damage * 0.9f, toPlayer.normalized * 6f);
                }
            }
            yield return null;
        }
        transform.rotation = start;
        Shockwave.Spawn(transform.position, radius, Frost, 20);
        CameraShake.Shake(0.3f, 0.15f);
        RelaxRig(5f);
        yield return Pose(Quaternion.identity, 0.4f);
        EndAction(1.8f);
    }

    private static readonly Color Wind = new Color(0.85f, 0.9f, 0.95f);

    // ── Idle flourishes, between moves: 0 a snarl, 1 a wing stretch, 2 a shake of the head ──
    private IEnumerator Flourish(int kind)
    {
        if (rig == null)
            yield break;
        float length = kind == 1 ? 1.6f : 1.1f;
        flourishUntil = Time.time + length;
        rig.ease = 6f;
        if (kind == 0)
        {
            rig.headPitch = 16f; rig.neckReach = 0.4f; rig.jawOpen = 18f; rig.jawEase = 10f; rig.crouch = 0.2f; rig.chestSwell = 0.04f;
            for (float t = 0f; t < length && !busy; t += Time.deltaTime)
            {
                rig.headYaw = Mathf.Sin(t * 18f) * 4f;
                yield return null;
            }
        }
        else if (kind == 1)
        {
            rig.wingSpread = 55f; rig.wingFlap = 10f; rig.headPitch = -14f; rig.chestSwell = 0.06f; rig.tailLift = 10f;
            yield return new WaitForSeconds(length * 0.7f);
            rig.wingFlap = 0f; rig.wingSpread = -6f; rig.tailLift = 0f;
            yield return new WaitForSeconds(length * 0.3f);
        }
        else
        {
            rig.headPitch = 6f; rig.jawOpen = 4f;
            for (float t = 0f; t < length && !busy; t += Time.deltaTime)
            {
                rig.headYaw = Mathf.Sin(t * 14f) * 22f * (1f - t / length);
                yield return null;
            }
        }
        if (!busy)
        {
            rig.jawOpen = NidhoggRig.JawRest;
            rig.chestSwell = 0f;
            rig.tailLift = 0f;
        }
    }

    // ── Tail swipe: crouches with the tail drawn round, then whips its whole body about so the tail scythes through
    // everything near it, hurling the player away ──
    private IEnumerator TailSwipe()
    {
        busy = true;
        float radius = Reach * 2.3f;
        warning = Telegraph.Circle(transform.position, radius, 0.8f);
        SetRig(pitch: 10f, yaw: -20f, neck: -0.3f, wings: 20f, crouch: 0.4f, tail: -40f, swell: 0.03f, ease: 6f, jaw: 10f, jawEase: 6f);
        Quaternion start = transform.rotation;
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            transform.rotation = start * Quaternion.Euler(0f, -25f * (t / 0.8f), 0f);   // winding up the other way
            yield return null;
        }
        ClearWarning();
        SetRig(pitch: 4f, yaw: 30f, neck: 0.2f, wings: 35f, crouch: 0.15f, tail: 55f, swell: 0f, ease: 18f, jaw: 16f, jawEase: 12f);
        rig.tailLift = 12f;
        Quaternion wound = transform.rotation;
        bool landed = false;
        const float whip = 0.5f;
        for (float t = 0f; t < whip; t += Time.deltaTime)
        {
            transform.rotation = wound * Quaternion.Euler(0f, 300f * Mathf.SmoothStep(0f, 1f, t / whip), 0f);
            if (!landed && t > whip * 0.3f && player != null && FlatDistance(player.position, transform.position) <= radius)
            {
                landed = true;
                Hurt(damage, Flat(player.position - transform.position).normalized * 11f);
            }
            yield return null;
        }
        Vfx.GroundRing(transform.position, radius, Frost, 0.4f);
        Vfx.Dust(transform.position, radius * 0.8f, 14);
        CameraShake.Shake(0.25f, 0.15f);
        rig.tailLift = 0f;
        RelaxRig(6f);
        // turns back round to face its prey
        Quaternion end = transform.rotation;
        Quaternion face = player != null ? Quaternion.LookRotation(Flat(player.position - transform.position).normalized) : start;
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(end, face, t / 0.35f);
            yield return null;
        }
        EndAction(1.4f);
    }

    // ── Stomp: rears up on its hind legs over the ground before it, then crashes both forefeet down; the ground
    // splits and rings of force race out three times ──
    private IEnumerator Stomp()
    {
        busy = true;
        Vector3 spot = transform.position + transform.forward * Reach * 1.1f;
        spot.y = floorY;
        float radius = Reach * 1.3f;
        warning = Telegraph.Circle(spot, radius, 0.85f);
        SetRig(pitch: -24f, yaw: 0f, neck: 0.3f, wings: 50f, crouch: -0.2f, tail: 20f, swell: 0.07f, ease: 6f, jaw: 20f, jawEase: 6f);
        rig.wingFlap = 10f;
        for (float t = 0f; t < 0.85f; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / 0.85f);
            pose = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-30f, 0f, 0f), k);
            lift = Mathf.Lerp(0f, 1.1f, k);
            yield return null;
        }
        ClearWarning();
        SetRig(pitch: 20f, yaw: 0f, neck: 0.6f, wings: 30f, crouch: 0.35f, tail: -10f, swell: -0.02f, ease: 20f, jaw: 24f, jawEase: 16f);
        for (float t = 0f; t < 0.14f; t += Time.deltaTime)
        {
            pose = Quaternion.Slerp(Quaternion.Euler(-30f, 0f, 0f), Quaternion.Euler(8f, 0f, 0f), t / 0.14f);
            lift = Mathf.Lerp(1.1f, 0f, t / 0.14f);
            yield return null;
        }
        CameraShake.Shake(0.5f, 0.3f);
        Vfx.Dust(spot, radius, 22);
        if (player != null && FlatDistance(player.position, spot) <= radius)
            Hurt(damage * 1.2f, Flat(player.position - spot).normalized * 8f);
        // the aftershocks: three rings racing out, each further than the last, each able to catch the player
        for (int i = 1; i <= 3; i++)
        {
            float ring = radius + i * Reach * 0.9f;
            Shockwave.Spawn(spot, ring, Frost, 10 + i * 4);
            if (player != null)
            {
                float d = FlatDistance(player.position, spot);
                if (d <= ring && d > ring - Reach * 0.9f)
                    Hurt(damage * 0.35f, Flat(player.position - spot).normalized * 4f);
            }
            yield return new WaitForSeconds(0.22f);
        }
        RelaxRig(6f);
        yield return Pose(Quaternion.identity, 0.35f);
        EndAction(1.5f);
    }

    // ── Wing buffet: spreads its wings to their widest and beats them three times, each gust a wall of wind down a wide
    // band ahead that shoves the player back and stings ──
    private IEnumerator WingBuffet()
    {
        busy = true;
        Vector3 dir = transform.forward;
        Vector3 from = transform.position;
        from.y = floorY;
        const float length = 11f, width = 7f;
        warning = Telegraph.Line(from, dir, length, width, 0.7f);
        SetRig(pitch: -12f, yaw: 0f, neck: -0.2f, wings: 62f, crouch: 0.1f, tail: 15f, swell: 0.08f, ease: 6f, jaw: 6f, jawEase: 6f);
        for (float t = 0f; t < 0.7f; t += Time.deltaTime)
        {
            pose = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-14f, 0f, 0f), t / 0.7f);
            lift = Mathf.Lerp(0f, 0.5f, t / 0.7f);
            yield return null;
        }
        ClearWarning();
        for (int beat = 0; beat < 3 && !dead; beat++)
        {
            // the downbeat
            rig.ease = 22f;
            rig.wingSpread = -20f;
            CameraShake.Shake(0.15f, 0.1f);
            for (int i = 0; i < 5; i++)
            {
                Vector3 at = from + dir * Random.Range(1f, length * 0.6f) + Vector3.Cross(dir, Vector3.up) * Random.Range(-width, width) * 0.4f + Vector3.up;
                MistVfx.Bank(at, dir * 12f, Random.Range(3f, 4.5f), 0.08f, 0.1f, 0.35f, 0.55f, Wind);
            }
            Vfx.Dust(from + dir * 3f, 2.5f, 10);
            if (InStrip(from, dir, length, width / 2f))
                Hurt(damage * 0.35f, dir * 9f);
            yield return new WaitForSeconds(0.18f);
            // and back up
            rig.ease = 10f;
            rig.wingSpread = 62f;
            yield return new WaitForSeconds(0.3f / haste);
        }
        RelaxRig(6f);
        yield return Pose(Quaternion.identity, 0.35f);
        EndAction(1.6f);
    }
    // ── Sky assault: it beats its wings and takes to the air, circles the arena for about 5 s hurling gusts of wind
    // down at the player (out of reach the whole time: nothing can hit it up there), then dives back down ──
    private IEnumerator SkyAssault()
    {
        busy = true;
        Vector3 ground = transform.position;
        Vector3 centre = HomeRoom != null ? HomeRoom.transform.position : ground;
        centre.y = floorY;
        float height = 2.5f;   // a low glide: the follow camera looks down, so anything much higher leaves the frame
        const float circleRadius = 7f, circleTime = 5f;

        // it stands up like a person: the body swings right up onto its hind legs until it is nearly upright, tail braced
        // on the ground, head level, wings opening wide and starting to beat
        SetRig(pitch: 6f, yaw: 0f, neck: -0.2f, wings: 20f, crouch: 0.35f, tail: 0f, swell: 0.05f, ease: 7f, jaw: NidhoggRig.JawShut, jawEase: 6f);
        yield return Pose(Quaternion.Euler(4f, 0f, 0f), 0.3f);   // gathering itself
        SetRig(pitch: 34f, yaw: 0f, neck: 0.3f, wings: 62f, crouch: -0.45f, tail: 30f, swell: 0.08f, ease: 6f, jaw: 10f, jawEase: 5f);
        rig.tailLift = -20f;
        rig.wingFlap = 22f;
        Quaternion standing = Quaternion.Euler(-60f, 0f, 0f);
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / 0.8f);
            pose = Quaternion.Slerp(Quaternion.Euler(4f, 0f, 0f), standing, k);
            lift = Mathf.Lerp(0f, 1.7f, k);
            yield return null;
        }
        yield return new WaitForSeconds(0.2f);

        // and lifts straight up off its feet, still standing, the downbeats throwing up dust
        flying = true;
        Alive.Remove(this);   // out of reach: no blow, skill or lock-on finds it while it's up
        controller.enabled = false;
        rig.wingFlap = 34f;
        Shockwave.Spawn(ground, Reach * 1.6f, Wind, 18);
        Vfx.Dust(ground, Reach * 1.2f, 18);
        CameraShake.Shake(0.4f, 0.15f);
        Vector3 up = ground + Vector3.up * (height + 1.5f);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(ground, up, Mathf.SmoothStep(0f, 1f, t / 0.6f));
            yield return null;
        }

        // then tips forward into flight: body level, neck stretched out ahead, legs drawn back, wings beating wide
        float angle = Mathf.Atan2(ground.z - centre.z, ground.x - centre.x);
        Vector3 Circle(float a) => centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * circleRadius + Vector3.up * height;
        SetRig(pitch: 4f, yaw: 0f, neck: 1.3f, wings: 66f, crouch: -0.6f, tail: 0f, swell: 0f, ease: 5f, jaw: 4f, jawEase: 6f);
        rig.tailLift = 6f;
        rig.wingFlap = 38f;
        Vector3 start = transform.position;
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / 0.8f);
            Vector3 target = Circle(angle);
            transform.position = Vector3.Lerp(start, target, k);
            Vector3 heading = Flat(target - start);
            if (heading.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(heading), 240f * Time.deltaTime);
            pose = Quaternion.Slerp(standing, Quaternion.Euler(6f, 0f, 0f), k);
            lift = Mathf.Lerp(1.7f, 0f, k);
            yield return null;
        }

        // flying like a dragon: round the arena with the body level and banked into the turn, rising a little on each
        // downbeat, head turned to watch the player, a gust beaten down at them every second
        float gust = 0.4f, speed = (Mathf.PI * 1.3f) / circleTime * haste;
        float bank = 0f;
        for (float t = 0f; t < circleTime; t += Time.deltaTime)
        {
            angle += speed * Time.deltaTime;
            Vector3 next = Circle(angle) + Vector3.up * Mathf.Sin(t * 5.5f) * 0.35f;   // the bob of the wingbeats
            Vector3 heading = next - transform.position;
            transform.position = next;
            if (Flat(heading).sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(Flat(heading).normalized);
            bank = Mathf.MoveTowards(bank, 26f, 40f * Time.deltaTime);
            pose = Quaternion.Euler(6f + Mathf.Sin(t * 5.5f + 1.2f) * 3f, 0f, -bank);
            if (player != null)
                rig.headYaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, Flat(player.position - transform.position), Vector3.up), -55f, 55f);
            rig.tailSway = Mathf.Sin(t * 2.2f) * 12f;
            // midway round, its eyes blaze and it rakes the ground with beams of light; no gusts meanwhile
            if (!lasered && t >= 1.4f && player != null)
            {
                lasered = true;
                StartCoroutine(EyeLaser());
            }
            gust -= Time.deltaTime;
            if (gust <= 0f && player != null && !lasering)
            {
                gust = 1f / haste;
                StartCoroutine(WindGust(new Vector3(player.position.x, floorY, player.position.z)));
            }
            yield return null;
        }
        lasered = false;
        rig.tailLift = 0f;

        // the dive: it marks a spot by the player, hangs over it a moment, and drops onto it
        Vector3 landing = player != null ? new Vector3(player.position.x, floorY, player.position.z) : ground;
        if (FlatDistance(landing, centre) > 12f)
            landing = centre + Flat(landing - centre).normalized * 12f;
        float slamRadius = Reach * 1.4f;
        warning = Telegraph.Circle(landing, slamRadius, 1f);
        Vector3 hover = landing + Vector3.up * height, from = transform.position;
        SetRig(pitch: 20f, yaw: 0f, neck: 0.8f, wings: 58f, crouch: 0f, tail: -10f, swell: 0.04f, ease: 6f, jaw: 20f, jawEase: 6f);
        for (float t = 0f; t < 0.7f; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(from, hover, Mathf.SmoothStep(0f, 1f, t / 0.7f));
            Vector3 face = Flat(landing - centre);
            if (player != null)
                face = Flat(player.position - transform.position);
            if (face.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face), 360f * Time.deltaTime);
            pose = Quaternion.Slerp(pose, Quaternion.Euler(10f, 0f, 0f), Time.deltaTime * 6f);
            yield return null;
        }
        yield return new WaitForSeconds(0.3f);
        rig.wingFlap = 0f;
        rig.wingSpread = -10f;
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            float k = t / 0.3f;
            transform.position = Vector3.Lerp(hover, landing, k * k);
            yield return null;
        }
        transform.position = landing;
        ClearWarning();
        Land();
        CameraShake.Shake(0.6f, 0.3f);
        Shockwave.Spawn(landing, slamRadius * 1.3f, Frost, 26);
        Vfx.Dust(landing, slamRadius, 24);
        if (player != null && FlatDistance(player.position, landing) <= slamRadius)
            Hurt(damage * 1.1f, Flat(player.position - landing).normalized * 9f);
        RelaxRig(6f);
        yield return Pose(Quaternion.identity, 0.4f);
        EndAction(1.6f);
    }

    private bool lasered, lasering;
    private static readonly Color LaserColor = new Color(1f, 0.28f, 0.32f);

    // Where its eyes are: up on the head, a little behind the jaws
    private Vector3 EyePos(float side) =>
        Mouth + Vector3.up * 0.55f * transform.localScale.y - transform.forward * 0.5f * transform.localScale.x + transform.right * side * 0.28f * transform.localScale.x;

    // ── Eye laser (in flight): its eyes flare red, a line marks the ground toward the player, then twin beams burst from
    // its eyes and rake along the ground after them for about 1.6 s, burning whatever they touch ──
    private IEnumerator EyeLaser()
    {
        lasering = true;
        Vector3 under = new Vector3(transform.position.x, floorY, transform.position.z);
        Vector3 target = new Vector3(player.position.x, floorY, player.position.z);
        Vector3 dir = Flat(target - under).normalized;
        float length = Mathf.Clamp(Vector3.Distance(under, target) + 6f, 8f, 26f);
        GameObject mark = Telegraph.Line(under, dir, length, 2f, 0.7f);

        // the eyes kindle
        Material eyeMat = Mats.Get("nidhogg_eye_glow", LaserColor, 0.6f, LaserColor * 4f);
        var eyes = new GameObject[2];
        for (int i = 0; i < 2; i++)
        {
            eyes[i] = Mats.Shape(PrimitiveType.Sphere, null, EyePos(i == 0 ? -1f : 1f), Vector3.one * 0.1f, eyeMat);
            eyes[i].GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Light glare = eyes[0].AddComponent<Light>();
        glare.type = LightType.Point;
        glare.color = LaserColor;
        glare.range = 8f;
        glare.shadows = LightShadows.None;
        for (float t = 0f; t < 0.7f; t += Time.deltaTime)
        {
            float k = t / 0.7f;
            for (int i = 0; i < 2; i++)
            {
                eyes[i].transform.position = EyePos(i == 0 ? -1f : 1f);
                eyes[i].transform.localScale = Vector3.one * Mathf.Lerp(0.1f, 0.45f, k);
            }
            glare.intensity = Mathf.Lerp(0f, 6f, k);
            yield return null;
        }
        if (mark != null)
            Destroy(mark);

        // the beams: from each eye down to a point on the ground that sweeps along the line and then chases the player
        Material core = Mats.Get("nidhogg_laser_core", Color.white, 0.6f, new Color(3f, 1.6f, 1.6f));
        Material sheath = Mats.Get("nidhogg_laser_sheath", LaserColor, 0.5f, LaserColor * 3f);
        var beams = new GameObject[4];
        for (int i = 0; i < 4; i++)
        {
            beams[i] = Mats.Shape(PrimitiveType.Cube, null, Vector3.zero, Vector3.one, i < 2 ? core : sheath);
            beams[i].GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Vector3 spot = under + dir * 2f;
        float tick = 0f, scorch = 0f;
        const float hold = 1.6f;
        for (float t = 0f; t < hold && !dead; t += Time.deltaTime)
        {
            // the burning spot runs out along the marked line, then bends after the player
            Vector3 along = under + dir * Mathf.Lerp(2f, length, Mathf.Clamp01(t / 0.6f));
            Vector3 chase = player != null ? new Vector3(player.position.x, floorY, player.position.z) : along;
            Vector3 aim = t < 0.6f ? along : Vector3.MoveTowards(spot, chase, 6f * Time.deltaTime);
            spot = aim;
            for (int i = 0; i < 2; i++)
            {
                Vector3 eye = EyePos(i == 0 ? -1f : 1f);
                eyes[i].transform.position = eye;
                Vector3 end = spot + transform.right * (i == 0 ? -0.35f : 0.35f) + Vector3.up * 0.05f;
                Vector3 span = end - eye;
                float w = 0.12f + Mathf.Sin(t * 50f + i) * 0.02f;
                beams[i].transform.SetPositionAndRotation(eye + span * 0.5f, Quaternion.LookRotation(span));
                beams[i].transform.localScale = new Vector3(w, w, span.magnitude);
                beams[i + 2].transform.SetPositionAndRotation(eye + span * 0.5f, Quaternion.LookRotation(span));
                beams[i + 2].transform.localScale = new Vector3(w * 3f, w * 3f, span.magnitude);
            }
            glare.transform.position = eyes[0].transform.position;
            // it scorches the ground where it touches
            if ((scorch -= Time.deltaTime) <= 0f)
            {
                scorch = 0.08f;
                Vfx.GroundRing(spot, 0.9f, LaserColor, 0.3f, "glow");
                Vfx.Burst(spot + Vector3.up * 0.2f, new Color(1f, 0.6f, 0.5f), 4, 4f, 0.12f, 1.5f);
            }
            if ((tick -= Time.deltaTime) <= 0f && player != null)
            {
                tick = 0.2f;
                if (FlatDistance(player.position, spot) <= 1.3f)
                    Hurt(damage * 0.25f, Flat(player.position - spot).normalized * 2f);
            }
            CameraShake.Shake(0.05f, 0.04f);
            yield return null;
        }
        foreach (GameObject b in beams)
            Destroy(b);
        foreach (GameObject e in eyes)
            Destroy(e);
        lasering = false;
    }

    // Back on the ground and within reach again
    private void Land()
    {
        flying = false;
        controller.enabled = true;
        verticalVelocity = -2f;
        if (!dead && !Alive.Contains(this))
            Alive.Add(this);
        if (rig != null)
            rig.wingFlap = 0f;
    }

    // One gust of wind beaten down at `spot`: a ring warns, wind streams down from its wings, and it bursts there,
    // hurling the player away
    private IEnumerator WindGust(Vector3 spot)
    {
        const float radius = 2.3f, windup = 0.8f;
        GameObject mark = Telegraph.Circle(spot, radius, windup);
        Vector3 from = Mouth;
        for (float t = 0f; t < windup; t += 0.1f)
        {
            float k = t / windup;
            Vector3 at = Vector3.Lerp(from, spot + Vector3.up * 1.2f, k);
            MistVfx.Bank(at, (spot - from).normalized * 10f, Mathf.Lerp(2f, 3.5f, k), 0.12f, 0.15f, 0.3f, 0.55f, Wind);
            yield return new WaitForSeconds(0.1f);
        }
        if (mark != null)
            Destroy(mark);
        Vfx.GroundRing(spot, radius, Wind, 0.4f);
        Vfx.Dust(spot, radius, 12);
        MistVfx.Bank(spot + Vector3.up, Vector3.zero, 5.5f, 0.12f, 0.15f, 0.45f, 0.75f, Wind);
        CameraShake.Shake(0.15f, 0.08f);
        if (player != null && FlatDistance(player.position, spot) <= radius)
        {
            Vector3 away = Flat(player.position - spot);
            Hurt(damage * 0.55f, (away.sqrMagnitude > 0.01f ? away.normalized : transform.forward) * 8f);
        }
    }

    // ── Breath beam: it rears and draws in a long breath, then lets loose a single straight beam of frost that crosses
    // the whole arena (about 34 m), held for over a second ──
    private IEnumerator BreathBeam()
    {
        busy = true;
        Vector3 dir = transform.forward;
        Vector3 from = transform.position + dir * Reach * 0.6f;
        from.y = floorY;
        const float length = 34f, width = 3f, charge = 1.4f, hold = 1.3f;
        warning = Telegraph.Line(from, dir, length, width, charge);

        // the long inhale: head back, chest swelling, motes of frost and wisps of mist drawn in from all around to a
        // light that grows and pulses in its jaws
        SetRig(pitch: -28f, yaw: 0f, neck: -0.4f, wings: 36f, crouch: -0.2f, tail: 0f, swell: 0.1f, ease: 4f, jaw: -6f, jawEase: 3f);
        ParticleSystem gather = Keep(Vfx.Gather(Mouth, 4f, Frost, charge, 120f));
        float pulse = 0f, wisp = 0f;
        for (float t = 0f; t < charge; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / charge);
            pose = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-18f, 0f, 0f), k);
            lift = Mathf.Lerp(0f, 0.5f, k);
            if (gather != null)
                gather.transform.position = Mouth;
            if ((pulse -= Time.deltaTime) <= 0f)
            {
                pulse = 0.07f;
                Vfx.Flash(Mouth, Mathf.Lerp(0.5f, 2.6f, k), Color.Lerp(Frost, Color.white, k * 0.5f), 0.16f);
            }
            if ((wisp -= Time.deltaTime) <= 0f)
            {
                wisp = 0.12f;
                Vector3 off = Random.onUnitSphere * 3.5f;
                off.y = Mathf.Abs(off.y) * 0.5f;
                MistVfx.Bank(Mouth + off, -off * 2.2f, Random.Range(1.2f, 2f), 0.05f, 0.1f, 0.3f, 0.45f, new Color(0.75f, 0.93f, 1f));
            }
            yield return null;
        }
        ClearWarning();

        // the beam: the head thrust forward and down and the body rocked back by it. A beam of light shoots out of its
        // jaws to the far side of the arena in a blink: a white-hot core in a rippling sheath of frost, rings of cold
        // racing along it, frost bursting where it strikes and a trail of rime left on the floor beneath
        SetRig(pitch: 14f, yaw: 0f, neck: 1.2f, wings: 42f, crouch: 0.15f, tail: 0f, swell: -0.04f, ease: 14f, jaw: 26f, jawEase: 18f);
        lift = 0f;
        Vector3 end = from + dir * length + Vector3.up * 0.7f;
        Vector3 ground = new Vector3(end.x, floorY, end.z);
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        LineRenderer core = Keep(BeamLine("NidhoggBeamCore", Color.white));
        LineRenderer sheath = Keep(BeamLine("NidhoggBeamSheath", new Color(0.55f, 0.9f, 1f)));
        LineRenderer haze = Keep(BeamLine("NidhoggBeamHaze", new Color(0.3f, 0.6f, 1f, 0.75f)));
        IceVfx.Flash(Mouth, 16f, 6f, hold + 0.4f);
        CameraShake.Shake(0.3f, 0.2f);
        Vfx.Flash(Mouth, 4f, Color.white, 0.25f);
        // rime laid along the floor where it passes, fading after
        for (float s = 2f; s < length; s += 2.4f)
            Vfx.Sprite("glow", from + dir * s + Vector3.up * 0.2f, Random.Range(3f, 4f), Frost * 0.55f, 2.4f, 0.7f, 1.1f, 0f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        const float shoot = 0.16f, fade = 0.3f;
        float tick = 0f, mist = 0f, ring = 0f, hit = 0f;
        for (float t = 0f; t < hold + fade; t += Time.deltaTime)
        {
            if (core == null || sheath == null || haze == null)
                break;
            float grow = Mathf.Clamp01(t / shoot);
            float dying = Mathf.Clamp01((t - hold) / fade);
            Vector3 mouth = Mouth;
            Vector3 tip = Vector3.Lerp(mouth, end, 1f - (1f - grow) * (1f - grow));
            Vector3 tail = Vector3.Lerp(mouth, tip, dying * dying);   // at the end the beam leaves its jaws and is gone
            float w = (1f + Mathf.Sin(t * 37f) * 0.08f + Mathf.Sin(t * 13f) * 0.05f) * (1f - dying);
            LayBeam(core, tail, tip, 0.7f * w, 0f, t);
            LayBeam(sheath, tail, tip, 1.8f * w, 0.14f, t);
            LayBeam(haze, tail, tip, 3.6f * w, 0.28f, t * 0.7f);

            if (t < hold)
            {
                // the recoil: it braces against it, rocking and shuddering
                pose = Quaternion.Euler(6f + Mathf.Sin(t * 26f) * 1.2f, 0f, Mathf.Sin(t * 19f) * 0.8f);
                rig.headYaw = Mathf.Sin(t * 7f) * 2f;
                controller.Move(-dir * 0.5f * Time.deltaTime);
                CameraShake.Shake(0.1f, 0.05f);
                if ((ring -= Time.deltaTime) <= 0f)
                {
                    ring = 0.045f;
                    Vector3 at = Vector3.Lerp(mouth, tip, Random.Range(0.08f, 1f));
                    Vfx.Sprite("ring", at, Random.Range(1.6f, 2.6f), Frost * 1.4f, 0.28f, 0.4f, 1.3f, 0f, Quaternion.LookRotation(tip - mouth));
                }
                if ((mist -= Time.deltaTime) <= 0f)
                {
                    mist = 0.08f;
                    MistVfx.Bank(Vector3.Lerp(mouth, tip, Random.value), side * Random.Range(-1.8f, 1.8f) + Vector3.up * 0.4f, Random.Range(2f, 3.4f), 0.1f, 0.12f, 0.4f, 0.4f, new Color(0.72f, 0.92f, 1f));
                }
                if (grow >= 1f && (hit -= Time.deltaTime) <= 0f)
                {
                    // where it strikes: frost bursting off the floor
                    hit = 0.1f;
                    Vfx.Burst(ground + Vector3.up * 0.6f, Frost, 8, 9f, 0.24f, 0.8f);
                    Vfx.Flash(ground + Vector3.up * 0.8f, 3f, Frost, 0.14f);
                    MistVfx.Bank(ground + Vector3.up * 0.5f, (Random.insideUnitSphere + Vector3.up) * 2f, Random.Range(2.5f, 3.5f), 0.08f, 0.1f, 0.4f, 0.5f, new Color(0.8f, 0.95f, 1f));
                    if (Random.value < 0.35f)
                        Vfx.GroundRing(ground, 2.2f, Frost, 0.35f);
                }
                if ((tick -= Time.deltaTime) <= 0f)
                {
                    tick = 0.25f;
                    if (InStrip(from, dir, length, width / 2f))
                        Hurt(damage * 0.3f, dir * 2f);
                }
            }
            yield return null;
        }
        foreach (LineRenderer line in new[] { core, sheath, haze })
            if (line != null)
                Destroy(line.gameObject);
        if (gather != null)
            Destroy(gather.gameObject);
        Shockwave.Spawn(ground, 2.5f, Frost, 14);
        IceVfx.Impact(ground, 2.2f, 2.5f, false);

        // spent: the head sags, jaws hanging open, the chest heaving a few times before it gathers itself
        SetRig(pitch: 22f, yaw: 0f, neck: 0.5f, wings: 12f, crouch: 0.2f, tail: 0f, swell: 0f, ease: 5f, jaw: 12f, jawEase: 5f);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            rig.chestSwell = Mathf.Sin(t * 16f) * 0.04f;
            if (Random.value < 0.15f)
                MistVfx.Bank(Mouth, transform.forward * 1.5f + Vector3.down * 0.5f, 1.2f, 0.05f, 0.1f, 0.3f, 0.35f, new Color(0.8f, 0.95f, 1f));
            yield return null;
        }
        RelaxRig(4f);
        yield return Pose(Quaternion.identity, 0.5f);
        EndAction(2f);
    }

    // ── the breath beam's light: soft-edged additive strips that always face the camera ──
    private static Material beamMaterial;

    private static Material BeamMaterial
    {
        get
        {
            if (beamMaterial != null)
                return beamMaterial;
            // bright down the middle, falling off softly to nothing at the edges
            var tex = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BeamProfile" };
            for (int y = 0; y < 64; y++)
            {
                float v = y / 63f * 2f - 1f;
                float a = Mathf.Exp(-v * v * 4.5f) * (1f - v * v);
                for (int x = 0; x < 4; x++)
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            beamMaterial = new Material(Vfx.Material("glow", true)) { name = "NidhoggBeam" };
            beamMaterial.SetTexture("_BaseMap", tex);
            beamMaterial.SetColor("_BaseColor", new Color(2f, 2f, 2.2f, 1f));   // past white, so it blooms
            return beamMaterial;
        }
    }

    private static LineRenderer BeamLine(string name, Color color)
    {
        var go = new GameObject(name);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = BeamMaterial;
        line.useWorldSpace = true;
        line.positionCount = 24;
        line.numCapVertices = 6;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.startColor = line.endColor = color;
        // narrow where it leaves the jaws, full along its length, flaring a little where it strikes
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.12f, 1f), new Keyframe(0.9f, 1f), new Keyframe(1f, 1.35f));
        return line;
    }

    // Lays a beam strip from a to b; `ripple` makes it wave sideways like streaming frost (none for the core)
    private static void LayBeam(LineRenderer line, Vector3 a, Vector3 b, float width, float ripple, float t)
    {
        line.widthMultiplier = width;
        Vector3 span = b - a;
        Vector3 side = Vector3.Cross(span.normalized, Vector3.up);
        int n = line.positionCount;
        for (int i = 0; i < n; i++)
        {
            float s = i / (n - 1f);
            float wave = ripple * Mathf.Sin(s * 26f - t * 34f) * Mathf.Min(1f, s * 4f);
            float lift = ripple * 0.6f * Mathf.Cos(s * 19f - t * 27f) * Mathf.Min(1f, s * 4f);
            line.SetPosition(i, a + span * s + side * wave + Vector3.up * lift);
        }
    }

    // ── Phase 2 (at 30% health, BossAwakening): faster and harder, and from now on ice meteors keep falling ──
    public void EnterPhaseTwo(float haste)
    {
        Hasten(haste);
        if (phaseTwo || dead)
            return;
        phaseTwo = true;
        // the awakening, in a short cutscene (about 3 s): whatever it was doing gives way to it
        if (!flying)
        {
            if (action != null)
                StopCoroutine(action);
            ClearWarning();
            busy = true;
            BossCutscene.Play(transform, BossCutscene.HeightOf(transform), AwakenScene(), true);
        }
        StartCoroutine(MeteorStorm());
    }

    // ── 페이즈 2 각성: head low and growling, red light gathering in its chest; then it rears right up, wings thrown
    // open, and shrieks as the title comes up, before crashing back down onto its forelegs ──
    private IEnumerator AwakenScene()
    {
        var rage = new Color(1f, 0.3f, 0.22f);
        SetRig(pitch: 18f, yaw: 0f, neck: -0.4f, wings: -12f, crouch: 0.4f, tail: -15f, swell: 0.06f, ease: 6f, jaw: NidhoggRig.JawShut, jawEase: 8f);
        Vfx.Gather(transform.position + Vector3.up * mouthHeight * transform.localScale.y * 0.6f, Reach * 2f, rage, 0.8f, 100f);
        for (float t = 0f; t < 0.8f; t += Time.deltaTime)
        {
            if (rig != null)
                rig.headYaw = Mathf.Sin(t * 26f) * 3f;
            CameraShake.Shake(0.1f, 0.03f + 0.05f * t);
            yield return null;
        }

        // the rear and the shriek
        SetRig(pitch: -34f, yaw: 0f, neck: 0.7f, wings: 60f, crouch: -0.2f, tail: 28f, swell: 0.1f, ease: 7f, jaw: 28f, jawEase: 10f);
        if (rig != null)
            rig.wingFlap = 16f;
        for (float t = 0f; t < 0.45f; t += Time.deltaTime)
        {
            pose = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-28f, 0f, 0f), t / 0.45f);
            lift = Mathf.Lerp(0f, 0.9f, t / 0.45f);
            yield return null;
        }
        BossCutscene.Flash(0.7f);
        BossCutscene.Dolly(0.85f);
        BossCutscene.Title("페이즈 2 · 니드호그 각성", "뿌리를 갉는 분노가 깨어난다 · 얼음 운석이 쏟아진다", rage);
        CameraShake.Shake(0.8f, 0.3f);
        Shockwave.Spawn(transform.position, Reach * 2.4f, rage, 30);
        Vfx.GroundRing(transform.position, Reach * 3f, rage, 0.9f, "runes");
        for (float t = 0f; t < 0.9f; t += Time.deltaTime)
        {
            if (rig != null)
                rig.headYaw = Mathf.Sin(t * 22f) * 6f;
            yield return null;
        }

        // down onto its forelegs, ready
        RelaxRig(8f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            pose = Quaternion.Slerp(Quaternion.Euler(-28f, 0f, 0f), Quaternion.Euler(4f, 0f, 0f), t / 0.3f);
            lift = Mathf.Lerp(0.9f, 0f, t / 0.3f);
            yield return null;
        }
        CameraShake.Shake(0.25f, 0.18f);
        Shockwave.Spawn(transform.position + transform.forward * Reach * 0.6f, 2.5f, rage, 14);
        EndAction(0.8f);
    }

    // A volley of ice meteors about every 5.5 s for the rest of the fight: one on the player, the rest scattered round them
    private IEnumerator MeteorStorm()
    {
        yield return new WaitForSeconds(2.5f);
        while (!dead)
        {
            if (player != null && GameManager.IsRunActive && !BossCutscene.Playing)
                for (int i = 0; i < 4 && !dead; i++)
                {
                    Vector2 r = i == 0 ? Vector2.zero : Random.insideUnitCircle * 5f;
                    StartCoroutine(Meteor(new Vector3(player.position.x + r.x, floorY, player.position.z + r.y)));
                    yield return new WaitForSeconds(0.35f);
                }
            yield return new WaitForSeconds(5.5f);
        }
    }

    // One meteor: a boulder of ice wreathed in frost, streaking down out of the sky at a slant and bursting on `spot`
    private IEnumerator Meteor(Vector3 spot)
    {
        const float radius = 2.4f, warn = 0.6f, fall = 0.6f;
        GameObject mark = IceVfx.Frosted(Telegraph.Circle(spot, radius, warn + fall));
        yield return new WaitForSeconds(warn);
        Vector3 slant = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        Vector3 start = spot + slant * 7f + Vector3.up * 18f;
        GameObject rock = Mats.Shape(PrimitiveType.Sphere, null, start, Vector3.one * 1.8f,
            Mats.Get("nidhogg_meteor_ice", new Color(0.62f, 0.85f, 1f), 0.95f, new Color(0.15f, 0.45f, 0.85f)));
        // lumps of ice stuck to it, so it reads as a rough boulder, and a cold glow inside
        for (int i = 0; i < 4; i++)
            Mats.Shape(PrimitiveType.Sphere, rock.transform, Random.onUnitSphere * 0.38f, Vector3.one * Random.Range(0.45f, 0.65f),
                Mats.Get("nidhogg_meteor_ice", new Color(0.62f, 0.85f, 1f), 0.95f, new Color(0.15f, 0.45f, 0.85f)));
        Vfx.Trail(rock, Frost, 1.2f, 0.35f);
        Light glow = rock.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = Frost;
        glow.range = 7f;
        glow.intensity = 3f;
        glow.shadows = LightShadows.None;
        Vector3 spin = Random.onUnitSphere * 540f;
        for (float t = 0f; t < fall && rock != null; t += Time.deltaTime)
        {
            float k = t / fall;
            rock.transform.position = Vector3.Lerp(start, spot + Vector3.up * 0.6f, k * k);
            rock.transform.Rotate(spin * Time.deltaTime);
            yield return null;
        }
        if (rock != null)
            Destroy(rock);
        if (mark != null)
            Destroy(mark);
        // the impact: a burst of snow and frost light, a ring racing out, the ground shaking
        IceVfx.BlizzardBurst(spot, radius);
        IceVfx.Flash(spot + Vector3.up * 1.5f, radius * 3f, 4f, 0.45f);
        Shockwave.Spawn(spot, radius, Frost, 14);
        Vfx.Burst(spot + Vector3.up * 0.5f, new Color(0.8f, 0.95f, 1f), 16, 7f, 0.22f, 1.4f);
        CameraShake.Shake(0.2f, 0.12f);
        if (player != null && FlatDistance(player.position, spot) <= radius)
        {
            Vector3 away = Flat(player.position - spot);
            Hurt(damage * 0.8f, (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 6f);
        }
    }

    // ── Arrival (the boss cutscene, about 2 s): a low growl with jaws clenched, then it rears up and shrieks ──
    public void PlayIntro()
    {
        if (dead)
            return;
        if (action != null)
            StopCoroutine(action);
        action = StartCoroutine(IntroRoar());
    }

    private IEnumerator IntroRoar()
    {
        busy = true;
        // head low, eyeing the intruder, teeth together
        SetRig(pitch: 14f, yaw: 0f, neck: -0.3f, wings: -10f, crouch: 0.25f, tail: -10f, swell: 0.02f, ease: 5f, jaw: NidhoggRig.JawShut, jawEase: 6f);
        yield return new WaitForSeconds(0.45f);

        // rears up, wings flung open, jaws gaping
        SetRig(pitch: -30f, yaw: 0f, neck: 0.5f, wings: 55f, crouch: 0f, tail: 20f, swell: 0.09f, ease: 5f, jaw: 26f, jawEase: 6f);
        if (rig != null)
            rig.wingFlap = 14f;
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            pose = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-22f, 0f, 0f), t / 0.5f);
            lift = Mathf.Lerp(0f, 0.7f, t / 0.5f);
            yield return null;
        }
        CameraShake.Shake(0.7f, 0.15f);
        Shockwave.Spawn(transform.position, Reach * 1.6f, Frost, 16);
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            if (rig != null)
                rig.headYaw = Mathf.Sin(t * 20f) * 5f;
            yield return null;
        }

        // drops back onto its forelegs, ready
        RelaxRig(7f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            pose = Quaternion.Slerp(Quaternion.Euler(-22f, 0f, 0f), Quaternion.Euler(4f, 0f, 0f), t / 0.3f);
            lift = Mathf.Lerp(0.7f, 0f, t / 0.3f);
            yield return null;
        }
        CameraShake.Shake(0.2f, 0.12f);
        Shockwave.Spawn(transform.position + transform.forward * Reach * 0.6f, 2.5f, Frost, 12);
        EndAction(1.2f);
    }

    // ── Roar: rears onto its hind legs, wings beating, head thrown back, and shrieks ──
    private IEnumerator Roar()
    {
        busy = true;
        SetRig(pitch: -32f, yaw: 0f, neck: 0.6f, wings: 55f, crouch: 0f, tail: 25f, swell: 0.09f, ease: 6f, jaw: 26f, jawEase: 10f);
        if (rig != null)
            rig.wingFlap = 18f;
        for (float t = 0f; t < 0.5f; t += Time.deltaTime)
        {
            pose = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-26f, 0f, 0f), t / 0.5f);
            lift = Mathf.Lerp(0f, 0.8f, t / 0.5f);
            yield return null;
        }
        Roared?.Invoke(this);
        CameraShake.Shake(0.6f, 0.2f);
        Shockwave.Spawn(transform.position, Reach * 2f, Frost, 20);
        for (float t = 0f; t < 1f; t += Time.deltaTime)
        {
            rig.headYaw = Mathf.Sin(t * 20f) * 6f;   // the head shakes with the shriek
            yield return null;
        }
        // crashes back down onto its forelegs
        RelaxRig(8f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            pose = Quaternion.Slerp(Quaternion.Euler(-26f, 0f, 0f), Quaternion.Euler(4f, 0f, 0f), t / 0.3f);
            lift = Mathf.Lerp(0.8f, 0f, t / 0.3f);
            yield return null;
        }
        Shockwave.Spawn(transform.position + transform.forward * Reach * 0.6f, 2.5f, Frost, 14);
        CameraShake.Shake(0.25f, 0.18f);
        yield return Pose(Quaternion.identity, 0.3f);
        EndAction(1f);
    }

    private void SetRig(float pitch, float yaw, float neck, float wings, float crouch, float tail, float swell, float ease,
        float jaw = NidhoggRig.JawRest, float jawEase = 10f)
    {
        if (rig == null)
            return;
        rig.jawOpen = jaw;
        rig.jawEase = jawEase;
        rig.headPitch = pitch;
        rig.headYaw = yaw;
        rig.neckReach = neck;
        rig.wingSpread = wings;
        rig.crouch = crouch;
        rig.tailSway = tail;
        rig.chestSwell = swell;
        rig.ease = ease;
        rig.gait = 0f;
        rig.wingFlap = 0f;
    }

    private void RelaxRig(float ease)
    {
        if (rig == null)
            return;
        rig.Relax();
        rig.ease = ease;
    }

    private IEnumerator Pose(Quaternion target, float seconds)
    {
        Quaternion from = pose;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            pose = Quaternion.Slerp(from, target, t / seconds);
            yield return null;
        }
        pose = target;
    }

    private bool InStrip(Vector3 from, Vector3 dir, float length, float halfWidth)
    {
        if (player == null)
            return false;
        Vector3 d = Flat(player.position - from);
        float along = Vector3.Dot(d, dir);
        return along > -1f && along < length && Mathf.Abs(Vector3.Dot(d, Vector3.Cross(dir, Vector3.up))) < halfWidth;
    }

    private void Hurt(float amount, Vector3 shove)
    {
        if (playerStats == null)
            return;
        playerStats.TakeDamage(amount, this);
        player.GetComponent<PlayerMovement>()?.Push(shove);
    }

    // effects a move keeps alive while it runs (the beam, gathering frost): cleared if the move is cut short
    private readonly System.Collections.Generic.List<GameObject> liveFx = new System.Collections.Generic.List<GameObject>();

    private T Keep<T>(T fx) where T : Component
    {
        if (fx != null)
            liveFx.Add(fx.gameObject);
        return fx;
    }

    private void ClearFx()
    {
        foreach (GameObject fx in liveFx)
            if (fx != null)
                Destroy(fx);
        liveFx.Clear();
    }

    private void OnDisable() => ClearFx();

    private void ClearWarning()
    {
        if (warning != null)
            Destroy(warning);
        warning = null;
    }

    // 페이즈 2 (BossAwakening): it moves, turns and attacks faster
    private float haste = 1f;

    public void Hasten(float factor)
    {
        haste *= factor;
        moveSpeed *= factor;
        turnSpeed *= factor;
    }

    private void EndAction(float wait)
    {
        ClearWarning();
        // (pose and lift ease back to rest in Update, so it doesn't snap upright)
        busy = false;
        action = null;
        cooldown = wait / haste;
        if (rig != null)
            rig.ease = 8f;
    }

    public override void TakeHit(Vector3 from, int amount)
    {
        if (dead || flying)   // (up in the air it's out of reach)
            return;
        amount = Mitigate(amount);
        health -= Mathf.Max(1, amount);
        StartCoroutine(Flash());
        if (health <= 0)
        {
            Die();
            return;
        }
        if (!roared && health <= maxHealth / 2 && !busy)
        {
            roared = true;
            action = StartCoroutine(Roar());
        }
    }

    // A broken-off move leaves it open a little longer
    public override void Interrupt()
    {
        if (dead || flying || action == null)
            return;
        StopCoroutine(action);
        ClearFx();   // a beam or gathering frost cut off with the move
        EndAction(2f);
        RelaxRig(8f);
    }

    // Too big to topple: it staggers, head jerking aside and wings flaring
    public override void Knockdown(float seconds)
    {
        if (dead || flying)
            return;
        Interrupt();
        stunnedUntil = Time.time + 0.5f;
        StartCoroutine(Stagger());
    }

    private IEnumerator Stagger()
    {
        if (rig == null)
            yield break;
        rig.ease = 20f;
        rig.headPitch = 25f;
        rig.headYaw = Random.value < 0.5f ? -25f : 25f;
        rig.neckReach = -0.4f;
        rig.wingSpread = 20f;
        rig.crouch = 0.3f;
        rig.jawOpen = 8f;
        rig.jawEase = 20f;
        yield return new WaitForSeconds(0.35f);
        RelaxRig(6f);
    }

    private IEnumerator Flash()
    {
        FlashTint(hitTint);
        yield return new WaitForSeconds(0.12f);
        if (!dead)
            ClearFlash();
    }

    private void Die()
    {
        dead = true;
        StopAllCoroutines();
        ClearWarning();
        ClearFx();
        ClearFlash();
        controller.enabled = false;
        RaiseDied();
        StartCoroutine(Sink());
    }

    // A last shriek, then it collapses - legs giving way, wings sagging, head dropping - and sinks among the roots
    private IEnumerator Sink()
    {
        if (rig != null)
        {
            SetRig(pitch: -30f, yaw: 0f, neck: 0.3f, wings: 40f, crouch: 0f, tail: 0f, swell: 0.06f, ease: 10f, jaw: 26f, jawEase: 14f);
            yield return new WaitForSeconds(0.4f);
            SetRig(pitch: 50f, yaw: 20f, neck: 0.6f, wings: -30f, crouch: 0.9f, tail: -20f, swell: -0.03f, ease: 4f, jaw: -4f, jawEase: 3f);
        }
        yield return new WaitForSeconds(0.8f);
        Vector3 start = transform.position;
        for (float t = 0f; t < 2f; t += Time.deltaTime)
        {
            float k = t / 2f;
            transform.position = start + Vector3.down * k * k * 3f;
            if (visual != null)
                visual.localRotation = Quaternion.Euler(0f, 0f, 20f * k) * visualRotation;
            yield return null;
        }
        Destroy(gameObject);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
