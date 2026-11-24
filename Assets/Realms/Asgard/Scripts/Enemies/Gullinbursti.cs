using System.Collections;
using UnityEngine;

// 굴린부르스티: Freyr's golden boar, gone wild in Asgard's halls. It backs off to get a run-up and charges in a
// straight line, leaving a golden blur behind its hooves. A charge that misses carries it on and leaves it
// winded; one that ends in a wall stuns it — the moment to strike. Up close it tusks and stamps.
//   직선 돌진       aims, paws the floor, and charges along a marked line
//   엄니 올려치기   at close range, a toss of the head that throws the player back
//   지면 강타       rears up and stamps: a ring of force round it
//   연속 돌진       charges twice, re-aiming in between (more often once it's hurt)
// The VARCO boar has no skeleton, so it moves by code: Gait bobs and rocks it with its pace, Model takes the
// poses of its moves (head down, tusk toss, rearing up) on top of that.
public class Gullinbursti : HumanoidEnemy
{

    [Header("Charge")]
    [SerializeField] private float chargeMinRange = 4f;
    [SerializeField] private float chargeMaxRange = 15f;
    [SerializeField] private float chargeWindup = 0.85f;
    [SerializeField] private float chargeSpeed = 15f;
    [SerializeField] private float chargeOvershoot = 4f;    // it runs on past where the player stood
    [SerializeField] private float chargeDamageMultiplier = 1.3f;
    [SerializeField] private float chargeCooldown = 4f;
    [SerializeField] private float wallStun = 1.8f;
    [SerializeField] private float missRecover = 1f;
    [SerializeField, Range(0f, 1f)] private float doubleChance = 0.35f;
    [SerializeField, Range(0f, 1f)] private float alwaysDoubleBelow = 0.5f;

    [Header("Tusk toss")]
    [SerializeField] private float tuskRange = 2.3f;
    [SerializeField] private float tuskWindup = 0.45f;
    [SerializeField] private float tuskThrow = 8f;

    [Header("Stamp")]
    [SerializeField] private float stampRadius = 3.2f;
    [SerializeField] private float stampWindup = 0.9f;
    [SerializeField] private float stampDamageMultiplier = 1.2f;
    [SerializeField] private float stampCooldown = 5f;

    [Header("Gait (procedural)")]
    [SerializeField] private Transform gait;
    [SerializeField] private Transform model;
    [SerializeField] private float strideRate = 3.2f;    // bounds per metre
    [SerializeField] private float bobHeight = 0.07f;
    [SerializeField] private float rockAngle = 5f;

    private float chargeTimer, stampTimer;
    private GameObject dizzy;
    private Vector3 lastPos;
    private float stride, pace, deathRoll;

    protected override void Awake()
    {
        base.Awake();
        chargeTimer = Random.Range(0.8f, 2f);
        stampTimer = Random.Range(1f, stampCooldown);
        if (gait == null)
            gait = transform.Find("Gait");
        if (model == null && gait != null)
            model = gait.Find("Model");
        lastPos = transform.position;
    }

    // Trots with a bounce and a rock of the body that follow its pace; tipped over on its side when it dies
    private void LateUpdate()
    {
        if (gait == null)
            return;
        if (dead)
        {
            deathRoll = Mathf.MoveTowards(deathRoll, 1f, Time.deltaTime * 2.5f);
            gait.localRotation = Quaternion.Euler(0f, 0f, 85f * Mathf.SmoothStep(0f, 1f, deathRoll));
            gait.localPosition = Vector3.up * 0.25f * Mathf.Sin(deathRoll * Mathf.PI) / Mathf.Max(0.01f, transform.localScale.y);
            return;
        }
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float speed = Flat(transform.position - lastPos).magnitude / dt;
        lastPos = transform.position;
        pace = Mathf.Lerp(pace, speed / Mathf.Max(0.01f, transform.localScale.x), 10f * dt);
        stride += pace * strideRate * dt;
        float k = Mathf.Clamp01(pace / 2f);
        float gallop = Mathf.Clamp01((pace - 4f) / 6f);    // a charge: longer, harder bounds
        float bob = Mathf.Abs(Mathf.Sin(stride * Mathf.PI)) * bobHeight * (k + gallop * 1.5f);
        float rock = Mathf.Sin(stride * Mathf.PI * 2f) * rockAngle * (k + gallop);
        float sway = Mathf.Sin(stride * Mathf.PI) * 2f * k;
        // At rest it breathes
        float breathe = (1f - k) * Mathf.Sin(Time.time * 2.2f) * 0.012f;
        gait.localPosition = Vector3.up * (bob + breathe);
        gait.localRotation = Quaternion.Euler(rock, 0f, sway);
    }

    protected override void Start()
    {
        base.Start();
        // The golden blur at its hooves whenever it moves (thickest at a charge)
        var hooves = new GameObject("GoldenHooves");
        hooves.transform.SetParent(transform, false);
        hooves.transform.localPosition = Vector3.up * 0.15f;
        Vfx.Sparkle(hooves, MobFx.BoarGold, 3f, 0.22f);
    }

    protected override Vector3 Think(Vector3 toPlayer, float distance)
    {
        chargeTimer -= Time.deltaTime;
        stampTimer -= Time.deltaTime;
        Vector3 dir = toPlayer / distance;
        if (cooldown > 0f)
            // Backs off for a run-up when the player is close and the charge is nearly ready
            return chargeTimer < 1f && distance < chargeMinRange ? -dir * moveSpeed * 0.6f : dir * moveSpeed * 0.5f;

        if (distance <= tuskRange)
        {
            if (stampTimer <= 0f)
            {
                stampTimer = stampCooldown;
                StartAction(Stamp(), attackCooldown + 0.3f);
            }
            else
                StartAction(TuskToss(), attackCooldown);
            return Vector3.zero;
        }
        if (distance <= stampRadius * 0.85f && stampTimer <= 0f)
        {
            stampTimer = stampCooldown;
            StartAction(Stamp(), attackCooldown + 0.3f);
            return Vector3.zero;
        }
        if (chargeTimer <= 0f && distance >= chargeMinRange && distance <= chargeMaxRange)
        {
            chargeTimer = chargeCooldown;
            bool twice = HealthFraction <= alwaysDoubleBelow || Random.value < doubleChance;
            StartAction(Charges(twice ? 2 : 1), attackCooldown);
            return Vector3.zero;
        }
        // Out of charge range (or waiting on it): trot in, or back off for a run-up
        if (distance < chargeMinRange && chargeTimer < 1.5f)
            return -dir * moveSpeed * 0.7f;
        return dir * moveSpeed;
    }

    protected override Color FeelColor => MobFx.BoarGold;

    private IEnumerator Charges(int count)
    {
        for (int i = 0; i < count && !dead; i++)
        {
            bool stunned = false;
            yield return Charge(i == 0 ? chargeWindup : chargeWindup * 0.6f, hitWall => stunned = hitWall);
            if (stunned)
                yield break;
        }
    }

    // 직선 돌진: paws the floor along a marked line, then thunders down it
    private IEnumerator Charge(float windup, System.Action<bool> hitWall)
    {
        FacePlayer();
        Vector3 to = player != null ? Flat(player.position - transform.position) : transform.forward;
        Vector3 dir = to.normalized;
        float length = Mathf.Min(to.magnitude + chargeOvershoot, chargeMaxRange + chargeOvershoot);
        warning = Telegraph.Line(transform.position, dir, length, 1.6f, windup);
        // Head down, a scrape of the hoof and a snort of dust — a snorting bellow before the first charge
        if (windup >= chargeWindup * 0.9f)
            EnemyFeel.Roar(transform, MobFx.BoarGold, 3f * FeelSize, 0.18f);
        else
            EnemyFeel.Windup(transform, transform.position + (Vector3.up * 0.8f + dir * 1.1f) * FeelSize, MobFx.BoarGold, FeelSize);
        float scrape = 0f;
        for (float t = 0f; t < windup; t += Time.deltaTime)
        {
            if (model != null)
                model.localRotation = Quaternion.Euler(8f + Mathf.Sin(t * 30f) * 2f, 0f, 0f);
            // each scrape of the hoof kicks up dust
            if ((scrape -= Time.deltaTime) <= 0f)
            {
                scrape = 0.3f;
                Vfx.Sprite("foe_dust_ink", transform.position - dir * 0.7f * FeelSize + Vector3.up * 0.4f, 1.1f * FeelSize, new Color(0.6f, 0.52f, 0.4f, 0.7f), 0.5f, 0.9f, 1.1f, 0f, additive: false).Sheet(4, 4);
            }
            yield return null;
        }
        Vfx.Dust(transform.position, 1f, 8, new Color(0.95f, 0.8f, 0.45f, 0.5f));
        EnemyFeel.Lunge(transform.position, dir, MobFx.BoarGold, FeelSize * 1.2f);
        if (warning != null)
            Destroy(warning);
        if (model != null)
            model.localRotation = Quaternion.Euler(5f, 0f, 0f);

        transform.rotation = Quaternion.LookRotation(dir);
        actionMove = dir * chargeSpeed;
        bool landed = false, wall = false;
        float travelled = 0f;
        Vector3 last = transform.position;
        float ghost = 0f, thud = 0f;
        while (travelled < length && !dead)
        {
            yield return null;
            travelled += Flat(transform.position - last).magnitude;
            // Blocked: the step went nowhere because something solid was in the way
            bool stalled = Flat(transform.position - last).magnitude < chargeSpeed * Time.deltaTime * 0.3f;
            last = transform.position;
            // Golden sparks kicked up behind its hooves
            if ((ghost -= Time.deltaTime) <= 0f)
            {
                ghost = 0.06f;
                Vfx.Burst(transform.position + Vector3.up * 0.2f - dir * 0.6f, MobFx.BoarGold, 4, 2.5f, 0.14f, -0.3f);
            }
            // the floor thuds under its hooves as it thunders along
            if ((thud -= Time.deltaTime) <= 0f)
            {
                thud = 0.18f;
                EnemyFeel.Shake(transform.position, 0.1f, 0.05f);
            }
            if (!landed && playerStats != null && FlatDistance(player.position, transform.position) <= 1.4f * transform.localScale.x)
            {
                landed = true;
                playerStats.TakeDamage(attackDamage * chargeDamageMultiplier, this);
                player.GetComponent<PlayerMovement>()?.Push(dir * 10f + Vector3.Cross(Vector3.up, dir) * 3f);
                EnemyFeel.Shake(transform.position, 0.2f, 0.18f);
            }
            if ((lastMoveFlags & CollisionFlags.Sides) != 0 && stalled)
            {
                wall = true;
                break;
            }
        }
        actionMove = Vector3.zero;
        if (model != null)
            model.localRotation = Quaternion.identity;

        if (wall)
        {
            hitWall(true);
            yield return Stunned();
            yield break;
        }
        hitWall(false);
        // Skids to a stop and catches its breath (longer when it missed)
        Vfx.Dust(transform.position, 0.8f, 6);
        yield return new WaitForSeconds(landed ? 0.45f : missRecover);
    }

    // Slammed into a wall: dazed, with stars wheeling over its head
    private IEnumerator Stunned()
    {
        // the crash into the wall: a burst and a crack where it struck
        EnemyFeel.Impact(transform.position + transform.forward * 0.8f * FeelSize, 1.6f * FeelSize, MobFx.BoarGold, 0.3f);
        Shockwave.Spawn(transform.position + transform.forward * 0.8f, 1.2f, MobFx.BoarGold, 8);
        Vfx.Burst(transform.position + transform.forward + Vector3.up * 0.6f, MobFx.BoarGold, 14, 5f, 0.18f);
        dizzy = new GameObject("Dizzy");
        dizzy.transform.SetParent(transform, false);
        dizzy.transform.localPosition = Vector3.up * 1.5f;
        for (int i = 0; i < 3; i++)
            Vfx.Glow(dizzy.transform, Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 0.4f, 0.35f, new Color(1f, 0.95f, 0.5f), "flare", 300f);
        for (float t = 0f; t < wallStun && !dead; t += Time.deltaTime)
        {
            dizzy.transform.Rotate(0f, 360f * Time.deltaTime, 0f, Space.Self);
            if (model != null)
                model.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 9f) * 6f);
            yield return null;
        }
        Destroy(dizzy);
        if (model != null)
            model.localRotation = Quaternion.identity;
    }

    // 엄니 올려치기: head down, then a savage upward toss of the tusks
    private IEnumerator TuskToss()
    {
        FacePlayer();
        EnemyFeel.Windup(transform, transform.position + (Vector3.up * 0.7f + transform.forward * 1.1f) * FeelSize, MobFx.BoarGold, FeelSize);
        for (float t = 0f; t < tuskWindup; t += Time.deltaTime)
        {
            if (model != null)
                model.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 14f, t / tuskWindup), 0f, 0f);
            yield return null;
        }
        Vfx.Slash(transform.position + Vector3.up * 0.7f + transform.forward * 1f, transform.forward, 2.2f, MobFx.BoarGold, 80f, -70f);
        EnemyFeel.Swing(transform.position + Vector3.up * 0.6f, transform.forward, tuskRange + 0.3f, MobFx.BoarGold);
        if (!dead && playerStats != null && InFront(tuskRange + 0.3f, 65f))
        {
            playerStats.TakeDamage(attackDamage, this);
            player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - transform.position).normalized * tuskThrow);
        }
        for (float t = 0f; t < 0.18f; t += Time.deltaTime)
        {
            if (model != null)
                model.localRotation = Quaternion.Euler(Mathf.Lerp(14f, -28f, t / 0.18f), 0f, 0f);
            yield return null;
        }
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            if (model != null)
                model.localRotation = Quaternion.Euler(Mathf.Lerp(-28f, 0f, t / 0.35f), 0f, 0f);
            yield return null;
        }
        if (model != null)
            model.localRotation = Quaternion.identity;
        yield return new WaitForSeconds(0.3f);
    }

    // 지면 강타: rears up on its hind legs and brings its forehooves down — a ring of force all round
    private IEnumerator Stamp()
    {
        EnemyVoice.Of(this)?.Skill();   // (its own big-move sound)
        FacePlayer();
        Vector3 center = transform.position;
        warning = Telegraph.Circle(center, stampRadius, stampWindup);
        // Rears up on its hind legs (pivoting about its haunches), hangs there, then drops onto its forehooves
        for (float t = 0f; t < stampWindup; t += Time.deltaTime)
        {
            float k = t / stampWindup;
            float rear = k < 0.75f ? Mathf.SmoothStep(0f, 1f, k / 0.75f) : 1f - Mathf.SmoothStep(0f, 1f, (k - 0.75f) / 0.25f) * 1.15f;
            Pose(-38f * rear);
            yield return null;
        }
        Pose(4f);
        if (warning != null)
            Destroy(warning);
        Shockwave.Spawn(center, stampRadius, MobFx.BoarGold, 16);
        Vfx.GroundRing(center, stampRadius, MobFx.BoarGold, 0.45f, "shockring");
        EnemyFeel.Impact(center, stampRadius, MobFx.BoarGold, 0.3f);
        if (!dead && playerStats != null && FlatDistance(player.position, center) <= stampRadius)
        {
            playerStats.TakeDamage(attackDamage * stampDamageMultiplier, this);
            player.GetComponent<PlayerMovement>()?.Push(Flat(player.position - center).normalized * 6f);
        }
        yield return new WaitForSeconds(0.15f);
        Pose(0f);
        yield return new WaitForSeconds(0.55f);
    }

    // Pitches the body about its hind legs (negative = nose up), so rearing lifts the front off the floor
    private void Pose(float pitch)
    {
        if (model == null)
            return;
        float half = 0.55f;   // the haunches sit about this far behind the middle (model space, before scaling)
        Quaternion q = Quaternion.Euler(pitch, 0f, 0f);
        Vector3 pivot = new Vector3(0f, 0f, -half);
        model.localRotation = q;
        model.localPosition = pivot - q * pivot;
    }

    public override void Interrupt()
    {
        ClearPose();
        base.Interrupt();
    }

    public override void Knockdown(float seconds)
    {
        ClearPose();
        base.Knockdown(seconds);
    }

    // Whatever the cut-off move left behind: the dazed stars and a tilted head
    private void ClearPose()
    {
        if (dizzy != null)
            Destroy(dizzy);
        Pose(0f);
    }
}
