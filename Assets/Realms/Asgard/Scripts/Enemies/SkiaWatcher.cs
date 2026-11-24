using System.Collections;
using UnityEngine;

// 빛의 감시자 스키아: a great blue eye in a golden, rune-carved frame, floating with no body beneath it. It keeps its
// distance, turns its gaze on the player and burns a line through the hall.
//   목표 조준     it always faces the player; a faint line of light shows where it is looking
//   직선 레이저   a short charge (the runes round its frame light one after another, the core opens), then a straight beam
//   추적 조준     the aim line follows the player for a while before it locks and fires
//   과부하 레이저 below 40% health: the charge is shorter and it fires twice in a row
// Model: VARCO mesh (Resources/Mobs/Skia → child "Model"); all its effects are VARCO sheets in Resources/Vfx/skia_*.
public class SkiaWatcher : Enemy
{
    public static readonly Color EyeBlue = new Color(0.4f, 0.8f, 1f);
    public static readonly Color RuneGold = new Color(1f, 0.82f, 0.4f);

    [SerializeField] private int maxHealth = 9;
    [SerializeField] private float hoverHeight = 2.3f;
    [SerializeField] private float preferredDistance = 8.5f;
    [SerializeField] private float driftSpeed = 1.6f;
    [SerializeField] private float laserDamage = 14f;

    [Header("Laser")]
    [SerializeField] private float attackInterval = 3.4f;
    [SerializeField] private float chargeTime = 1.1f;
    [SerializeField] private float trackTime = 1.6f;          // 추적 조준: how long the aim follows the player
    [SerializeField, Range(0f, 1f)] private float trackChance = 0.45f;
    [SerializeField] private float lockTime = 0.35f;          // the aim stops moving this long before it fires
    [SerializeField] private float beamRange = 20f;
    [SerializeField] private float beamWidth = 1.1f;          // hits within half of this from the beam's line
    [SerializeField] private float beamSeconds = 0.45f;
    [SerializeField] private float aimTurnRate = 70f;         // degrees a second the tracking aim swings

    [Header("Overload (below this much health)")]
    [SerializeField, Range(0f, 1f)] private float overloadAt = 0.4f;
    [SerializeField] private float overloadChargeScale = 0.55f;

    [Header("Model")]
    [SerializeField] private Transform model;
    [SerializeField] private float modelSize = 1.6f;          // how tall the frame stands, in metres (set by the builder)

    private const float IrisRest = 0.24f;    // the iris art at rest, a little larger than the painted eye (× modelSize)

    private Transform player;
    private PlayerStats playerStats;
    private int health;
    private bool dead, busy, overloaded;
    private float attackTimer, stunnedUntil, phase;
    private Vector3 knockback;
    private Coroutine action;

    private Transform body;               // bobs and tilts; carries the model and all its lights
    private Transform eye;                // the core's centre, at the front of the frame
    private EyeSprite iris, irisGlow, ringBack;
    private readonly GameObject[] runeLights = new GameObject[8];
    private Light glowLight;
    private AimLine aimLine;
    private Vector3 aimDir;

    public override int Health => Mathf.Max(0, health);
    public override int MaxHealth => maxHealth;
    private float HealthFraction => (float)Health / Mathf.Max(1, maxHealth);

    private void Awake()
    {
        health = maxHealth;
        phase = Random.Range(0f, 10f);
        attackTimer = Random.Range(1.2f, attackInterval);
        if (model == null)
            model = transform.Find("Body/Model") ?? transform.Find("Model");
        if (model != null)
            modelRest = model.localRotation;
        Build();
    }

    private void Start()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null)
        {
            player = movement.transform;
            playerStats = movement.GetComponent<PlayerStats>();
        }
        aimDir = transform.forward;
    }

    private void Build()
    {
        body = transform.Find("Body");
        if (body == null)
        {
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
        }
        if (model != null && model.parent != body)
            model.SetParent(body, true);

        float s = modelSize;
        eye = new GameObject("Eye").transform;
        eye.SetParent(body, false);
        eye.localPosition = new Vector3(0f, 0f, 0.14f * s);

        // The open eye: VARCO iris art over the mesh's own core, and a soft glow round it
        iris = Piece("skia_iris", eye, IrisRest * s, EyeBlue * 0.5f);
        irisGlow = Piece("skia_charge", eye, 0.5f * s, EyeBlue * 0.25f);
        // The rune ring behind the frame: dim until it charges
        ringBack = Piece("skia_runering", body, 1.45f * s, RuneGold * 0.15f, -0.05f * s);
        for (int i = 0; i < runeLights.Length; i++)
        {
            float a = (90f - i * 45f) * Mathf.Deg2Rad;
            Vector3 at = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.62f * s + Vector3.forward * 0.02f;
            runeLights[i] = Vfx.Glow(body, at, 0.32f * s, RuneGold * 0.15f, "flare", 0f);
        }

        var lightGo = new GameObject("EyeLight");
        lightGo.transform.SetParent(eye, false);
        lightGo.transform.localPosition = Vector3.forward * 0.4f;
        glowLight = lightGo.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = EyeBlue;
        glowLight.range = 5f;
        glowLight.intensity = 1.2f;

        aimLine = new AimLine("skia_aimline");
        aimLine.Show(false);
    }

    // A sprite that faces along the body's forward (not the camera), so the eye and its ring turn with the gaze
    private static EyeSprite Piece(string tex, Transform parent, float size, Color color, float z = 0f)
    {
        var p = new EyeSprite(tex, parent, new Vector3(0f, 0f, z));
        p.Set(color, size);
        return p;
    }

    // A flat glowing quad fixed to its parent (unlike VfxPiece, which keeps its own world rotation)
    private class EyeSprite
    {
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private readonly Transform t;
        private readonly Renderer rend;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public EyeSprite(string texture, Transform parent, Vector3 local)
        {
            var go = new GameObject("Vfx_eye_" + texture);
            t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            go.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Vfx.Material(texture, true);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            rend = r;
        }

        public void Set(Color c, float size)
        {
            if (t == null)
                return;
            t.localScale = Vector3.one * size;
            block.SetColor(ColorId, c * 1.5f);
            rend.SetPropertyBlock(block);
        }
    }

    public override void Scale(float healthMultiplier, float damageMultiplier)
    {
        maxHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * healthMultiplier));
        health = maxHealth;
        laserDamage *= damageMultiplier;
    }

    private void Update()
    {
        if (dead)
            return;
        phase += Time.deltaTime;
        bool stunned = Time.time < stunnedUntil;

        Vector3 pos = transform.position;
        float targetHeight = stunned ? 0.8f : hoverHeight + Mathf.Sin(phase * 1.3f) * 0.18f;
        pos.y = Mathf.MoveTowards(pos.y, targetHeight, (stunned ? 6f : 2f) * Time.deltaTime);

        knockback = Vector3.Lerp(knockback, Vector3.zero, 5f * Time.deltaTime);
        Vector3 move = knockback;
        if (!stunned && player != null && GameManager.IsRunActive)
        {
            Vector3 to = Flat(player.position - transform.position);
            float distance = to.magnitude;
            // 목표 조준: always turning its gaze on the player (slowly while a shot is locked)
            if (!busy)
                Face(player.position + Vector3.up * 0.9f, 240f);

            if (!overloaded && HealthFraction <= overloadAt)
                StartOverload();

            if (!busy)
            {
                attackTimer -= Time.deltaTime;
                Vector3 dir = distance > 0.01f ? to / distance : transform.forward;
                Vector3 side = Vector3.Cross(Vector3.up, dir) * Mathf.Sin(phase * 0.5f);
                Vector3 want = (distance - preferredDistance) * dir * 0.5f + side * 1.4f;
                move += Vector3.ClampMagnitude(want, driftSpeed);
                if (attackTimer <= 0f && distance < beamRange - 2f)
                {
                    attackTimer = attackInterval * (overloaded ? 0.8f : 1f);
                    bool track = Random.value < trackChance;
                    action = StartCoroutine(Act(Fire(track, overloaded ? 2 : 1)));
                }
            }
        }
        pos += move * Time.deltaTime;
        transform.position = KeepInZone(pos);
        Animate(stunned);
    }

    private IEnumerator Act(IEnumerator routine)
    {
        busy = true;
        yield return routine;
        EndAct();
    }

    private GameObject warning;   // the charge's strip on the floor
    private readonly BeamFx[] liveBeams = new BeamFx[2];   // a beam being fired (cleared if the shot is cut off)

    private void ClearWarning()
    {
        if (warning != null)
            Destroy(warning);
        warning = null;
        for (int i = 0; i < liveBeams.Length; i++)
        {
            liveBeams[i]?.Destroy();
            liveBeams[i] = null;
        }
    }

    private void EndAct()
    {
        busy = false;
        action = null;
        ClearWarning();
        aimLine.Show(false);
        SetCharge(0f);
    }

    // Turns body and gaze toward a point (yaw on the root, pitch on the body)
    private void Face(Vector3 target, float degreesPerSecond)
    {
        Vector3 to = target - eye.position;
        Vector3 flat = Flat(to);
        if (flat.sqrMagnitude < 0.01f)
            return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), degreesPerSecond * Time.deltaTime);
        float pitch = -Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg;
        body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(Mathf.Clamp(pitch, -35f, 35f), 0f, 0f), 8f * Time.deltaTime);
    }

    // ───────────── The laser ─────────────

    private IEnumerator Fire(bool track, int shots)
    {
        aimDir = player != null ? (player.position + Vector3.up * 0.9f - eye.position).normalized : eye.forward;
        aimLine.Show(true);
        // 추적 조준: the line of its gaze follows the player, lagging a little behind
        if (track)
        {
            for (float t = 0f; t < trackTime && !dead; t += Time.deltaTime)
            {
                TrackAim(aimTurnRate);
                SetCharge(0.25f * t / trackTime);
                aimLine.Set(eye.position, eye.position + aimDir * beamRange, EyeBlue * (0.5f + 0.5f * Mathf.PingPong(t * 4f, 1f)), 0.12f);
                yield return null;
            }
        }
        for (int shot = 0; shot < shots && !dead; shot++)
        {
            float charge = chargeTime * (overloaded ? overloadChargeScale : 1f) * (shot > 0 ? 0.7f : 1f);
            yield return Charge(charge, track);
            if (dead)
                yield break;
            yield return Beam();
            if (shot + 1 < shots)
                yield return new WaitForSeconds(0.15f);
        }
        aimLine.Show(false);
        yield return new WaitForSeconds(0.5f);
    }

    // Runes light one after another round the frame and the light gathers at the core; the aim locks at the end
    private IEnumerator Charge(float seconds, bool track)
    {
        aimLine.Show(true);
        Color col = overloaded ? Color.Lerp(EyeBlue, RuneGold, 0.45f) : EyeBlue;
        VfxPiece gather = Vfx.Sprite("skia_charge", eye.position + eye.forward * 0.15f, 1.6f * modelSize, col * 1.4f, seconds, 1.4f, 0.35f, 140f);
        gather.transform.SetParent(eye, true);
        if (overloaded)
        {
            VfxPiece crackle = Vfx.Sprite("skia_overload", eye.position + eye.forward * 0.1f, 1.1f * modelSize, Color.white * 1.2f, seconds, 0.8f, 1.15f, 60f);
            crackle.transform.SetParent(eye, true);
        }
        if (overloaded) EnemyVoice.Of(this)?.Skill();   // (the overload's own charge sound, before the windup's plain one)
        EnemyFeel.Windup(transform, eye.position, col, 1.2f);
        ClearWarning();
        warning = Telegraph.Line(new Vector3(eye.position.x, 0f, eye.position.z), Flat(aimDir), beamRange * 0.9f, beamWidth, seconds);
        for (float t = 0f; t < seconds && !dead; t += Time.deltaTime)
        {
            float k = t / seconds;
            bool locked = seconds - t <= lockTime;
            if (!locked && (track || t < seconds * 0.5f))
            {
                TrackAim(track ? aimTurnRate * 1.2f : 360f);
                // the warning strip on the floor swings with the aim
                if (warning != null)
                    warning.transform.rotation = Quaternion.LookRotation(Flat(aimDir).sqrMagnitude > 0.001f ? Flat(aimDir) : transform.forward);
            }
            if (warning != null)
                warning.transform.position = new Vector3(eye.position.x, 0.15f, eye.position.z);
            SetCharge(0.25f + 0.75f * k);
            // the line brightens and thins to a needle as the shot locks
            Color c = locked ? Color.Lerp(col, Color.white, 0.5f) * 1.6f : col * (0.6f + 0.6f * k);
            aimLine.Set(eye.position, eye.position + aimDir * beamRange, c, Mathf.Lerp(0.18f, 0.08f, k));
            body.localPosition = new Vector3(Mathf.Sin(t * 60f) * 0.015f * k, 0f, -0.06f * k);
            yield return null;
        }
        body.localPosition = Vector3.zero;
    }

    private void TrackAim(float degreesPerSecond)
    {
        if (player == null)
            return;
        Vector3 want = (player.position + Vector3.up * 0.9f - eye.position).normalized;
        aimDir = Vector3.RotateTowards(aimDir, want, degreesPerSecond * Mathf.Deg2Rad * Time.deltaTime, 0f);
        Face(eye.position + aimDir * 10f, degreesPerSecond * 1.5f);
    }

    // 직선 레이저: a straight beam from the core along the locked aim, to the first wall
    private IEnumerator Beam()
    {
        aimLine.Show(false);
        ClearWarning();
        Vector3 from = eye.position + aimDir * 0.3f;
        float length = beamRange;
        // the beam stops at walls and pillars (not at the player or other monsters)
        if (Physics.Raycast(from, aimDir, out RaycastHit wall, beamRange, ~0, QueryTriggerInteraction.Ignore)
            && wall.collider.GetComponentInParent<Enemy>() == null && wall.collider.GetComponentInParent<PlayerStats>() == null)
            length = wall.distance;
        Vector3 to = from + aimDir * length;
        Color col = overloaded ? Color.Lerp(EyeBlue, RuneGold, 0.35f) : EyeBlue;

        // a wide blue glow with a white-hot core down its middle (the painted strip's core is thin, so it's drawn wide)
        var beam = new BeamFx("skia_laser", from, to, beamWidth * 3f, col, beamSeconds);
        var core = new BeamFx("skia_laser", from, to, beamWidth * 1.3f, Color.Lerp(col, Color.white, 0.7f), beamSeconds);
        liveBeams[0] = beam;
        liveBeams[1] = core;
        Vfx.Sprite("skia_impact", from, 1.8f, col * 1.6f, 0.3f, 0.6f, 1.3f, 40f);
        Vfx.Sprite("skia_impact", to, 2.4f, col * 1.8f, 0.4f, 0.5f, 1.4f, -60f);
        Vfx.Burst(to, col, 14, 6f, 0.16f, 0.3f);
        knockback -= Flat(aimDir) * 2.5f;   // the recoil pushes it back
        EnemyFeel.Shake(from, 0.15f, 0.08f);

        bool hit = false;
        for (float t = 0f; t < beamSeconds; t += Time.deltaTime)
        {
            if (!hit && !dead && playerStats != null && player != null)
            {
                Vector3 p = player.position + Vector3.up * 0.9f;
                float along = Vector3.Dot(p - from, aimDir);
                if (along > 0f && along < length + 0.5f && Vector3.Distance(p, from + aimDir * along) <= beamWidth * 0.5f + 0.35f)
                {
                    hit = true;
                    playerStats.TakeDamage(laserDamage * OutgoingDamageMultiplier, this);
                    Vfx.Sprite("skia_impact", p, 1.6f, col * 1.6f, 0.25f, 0.6f, 1.2f, 90f);
                }
            }
            beam.Update(t / beamSeconds);
            core.Update(t / beamSeconds);
            yield return null;
        }
        beam.Destroy();
        core.Destroy();
        SetCharge(0f);
    }

    // How charged it looks (0..1): runes lit in turn, the ring and core brighten, the iris opens
    private void SetCharge(float k)
    {
        int lit = Mathf.FloorToInt(k * runeLights.Length + 0.001f);
        for (int i = 0; i < runeLights.Length; i++)
        {
            if (runeLights[i] == null) continue;
            bool on = i < lit;
            runeLights[i].GetComponent<VfxPiece>()?.Set(RuneGold * (on ? 2.4f : 0.15f) * 1.5f, (on ? 0.38f : 0.28f) * modelSize);
        }
        Color col = overloaded ? Color.Lerp(EyeBlue, RuneGold, 0.4f) : EyeBlue;
        if (ringBack != null) ringBack.Set(RuneGold * Mathf.Lerp(0.15f, 1.3f, k), 1.45f * modelSize * (1f + 0.08f * k));
        // the core "opens": the iris art swells past the painted eye and blazes
        if (iris != null) iris.Set(Color.Lerp(col, Color.white, 0.4f * k) * Mathf.Lerp(0.5f, 2.4f, k), Mathf.Lerp(IrisRest, 0.42f, k) * modelSize);
        if (irisGlow != null) irisGlow.Set(col * Mathf.Lerp(0.25f, 1.4f, k), Mathf.Lerp(0.5f, 1.2f, k) * modelSize);
        if (glowLight != null) glowLight.intensity = Mathf.Lerp(1.2f, 5f, k);
    }

    // 과부하: once badly hurt the frame crackles with gold, and it charges faster and fires twice
    private void StartOverload()
    {
        overloaded = true;
        Vfx.Sprite("skia_overload", eye.position, 2.2f * modelSize, Color.white * 1.3f, 0.6f, 0.6f, 1.4f, 90f);
        EnemyFeel.Roar(transform, RuneGold, 3.5f, 0.15f);
        attackTimer = Mathf.Min(attackTimer, 0.8f);
        if (glowLight != null)
            glowLight.color = Color.Lerp(EyeBlue, RuneGold, 0.4f);
    }

    private Vector3 KeepInZone(Vector3 pos) => HomeRoom == null ? pos : HomeRoom.ClampToFloor(pos, 1f);

    private void Animate(bool stunned)
    {
        if (!busy)
            body.localPosition = new Vector3(0f, Mathf.Sin(phase * 2.4f) * 0.05f, 0f);
        // a slow roll as it hangs in the air; an idle shimmer of the iris
        if (model != null)
            model.localRotation = Quaternion.Euler(0f, 0f, stunned ? Mathf.Sin(phase * 9f) * 12f : Mathf.Sin(phase * 0.8f) * 4f) * modelRest;
        if (!busy && iris != null)
            iris.Set(EyeBlue * (0.5f + Mathf.Sin(phase * 3f) * 0.12f) * (overloaded ? 1.4f : 1f), IrisRest * modelSize);
    }

    private Quaternion modelRest = Quaternion.identity;   // the VARCO FBX root's own turn, kept under the roll

    public override void TakeHit(Vector3 from, int damage)
    {
        if (dead)
            return;
        health -= Mathf.Max(1, Mitigate(damage));
        knockback = Flat(transform.position - from).normalized * 3f;
        StartCoroutine(Flash());
        Vfx.Burst(eye.position, EyeBlue, 6, 3f, 0.12f, 0f);
        if (health <= 0)
            StartCoroutine(Die());
    }

    public override void Knockdown(float seconds)
    {
        if (dead)
            return;
        Interrupt();
        stunnedUntil = Time.time + seconds;
    }

    // The charge is broken: the light spills out of the core and the runes go dark
    public override void Interrupt()
    {
        if (dead || action == null)
            return;
        StopCoroutine(action);
        EndAct();
        body.localPosition = Vector3.zero;
        Vfx.Burst(eye.position, EyeBlue, 10, 4f, 0.14f, 0.2f);
        attackTimer = Mathf.Max(attackTimer, 1.8f);
    }

    private IEnumerator Flash()
    {
        FlashTint(new Color(1f, 0.6f, 0.6f));
        yield return new WaitForSeconds(0.1f);
        ClearFlash();
    }

    // The eye shatters: a white flash, the frame drops and breaks apart into light
    private IEnumerator Die()
    {
        dead = true;
        if (action != null)
            StopCoroutine(action);
        aimLine.Destroy();
        ClearWarning();
        RaiseDied();
        Vector3 at = eye.position;
        Vfx.Sprite("skia_impact", at, 3.5f, EyeBlue * 2f, 0.45f, 0.4f, 1.6f, 80f);
        Vfx.Sprite("skia_runering", at, 2.8f, RuneGold * 1.5f, 0.6f, 0.6f, 1.8f, 120f);
        Vfx.Burst(at, EyeBlue, 26, 7f, 0.18f, 0.6f);
        Vfx.Burst(at, RuneGold, 14, 5f, 0.14f, 1f);
        EnemyFeel.Impact(new Vector3(at.x, 0f, at.z), 1.5f, EyeBlue, 0.15f);
        Vector3 start = transform.position;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            float k = t / 0.6f;
            transform.position = Vector3.Lerp(start, new Vector3(start.x, 0.3f, start.z), k * k);
            body.localRotation = Quaternion.Euler(60f * k, 0f, 25f * k);
            body.localScale = Vector3.one * Mathf.Lerp(1f, 0.2f, k * k);
            if (glowLight != null) glowLight.intensity = Mathf.Lerp(5f, 0f, k);
            yield return null;
        }
        Vfx.Flash(transform.position, 2f, EyeBlue, 0.25f);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        aimLine?.Destroy();
        ClearWarning();
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    // ───────────── Line effects ─────────────

    // A textured strip from A to B that turns about its own length to face the camera
    public class BeamFx
    {
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private readonly GameObject go;
        private readonly Renderer rend;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private Vector3 a, b;
        private float width, life;
        private Color color;

        public BeamFx(string texture, Vector3 from, Vector3 to, float width, Color color, float seconds)
        {
            go = new GameObject("Vfx_beam_" + texture);
            go.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Vfx.Material(texture, true);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            rend = r;
            this.width = width;
            this.color = color;
            life = seconds;
            Set(from, to, color, width);
            Update(0f);
        }

        public void Set(Vector3 from, Vector3 to, Color c, float w)
        {
            a = from; b = to; color = c; width = w;
            Place(width);
            block.SetColor(ColorId, color);
            rend.SetPropertyBlock(block);
        }

        // k: 0..1 over the beam's life — it flares wide, then thins away
        public void Update(float k)
        {
            if (go == null || life <= 0f)
                return;
            float w = width * (k < 0.15f ? Mathf.Lerp(0.4f, 1.25f, k / 0.15f) : Mathf.Lerp(1.25f, 0.2f, (k - 0.15f) / 0.85f));
            Color c = color * (k < 0.15f ? 2.6f : Mathf.Lerp(2.6f, 0.6f, (k - 0.15f) / 0.85f));
            Place(w);
            block.SetColor(ColorId, c);
            rend.SetPropertyBlock(block);
        }

        private void Place(float w)
        {
            Vector3 dir = b - a;
            float len = dir.magnitude;
            if (len < 0.001f)
                return;
            dir /= len;
            Vector3 view = Camera.main != null ? (Camera.main.transform.position - (a + b) * 0.5f) : Vector3.up;
            Vector3 up = Vector3.Cross(view, dir);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.Cross(Vector3.up, dir);
            up.Normalize();
            Vector3 normal = Vector3.Cross(dir, up);
            // LookRotation(normal, up) puts local +x along dir: the quad's x (the texture's length) runs down the strip
            go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(normal, up));
            go.transform.localScale = new Vector3(len, w, 1f);
        }

        public void Destroy()
        {
            if (go != null)
                Object.Destroy(go);
        }
    }

    // The faint aim line: a thin beam strip that stays until hidden
    private class AimLine
    {
        private BeamFx beam;
        private readonly string texture;
        private bool shown;

        public AimLine(string texture) { this.texture = texture; }

        public void Show(bool on)
        {
            shown = on;
            if (!on && beam != null)
            {
                beam.Destroy();
                beam = null;
            }
        }

        public void Set(Vector3 from, Vector3 to, Color c, float width)
        {
            if (!shown)
                return;
            if (beam == null)
                beam = new BeamFx(texture, from, to, width, c, 0f);
            beam.Set(from, to, c, width);
        }

        public void Destroy() => Show(false);
    }
}
