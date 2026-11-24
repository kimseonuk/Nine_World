using UnityEngine;

// Follows the target from a fixed quarter-view angle.
// Inside the boss arena the camera locks onto the boss: it sits behind the player on the boss-to-player line
// and tilts up so the giant stays in frame. The first time the player walks in, a short intro shot reveals the boss.
public class QuarterViewCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0f, 11f, -9f);
    public float followSmooth = 8f;
    [Tooltip("Metres the view is shifted ahead of the player (away from the camera), so more of the path in front is on screen.")]
    public float lookAhead = 0f;

    [Header("Boss arena")]
    public Transform boss;
    public Vector3 arenaCenter = new Vector3(0f, 0f, 92f);
    public float arenaRadius = 24f;
    [Tooltip("Offset in the boss-facing frame: z < 0 is behind the player, away from the boss.")]
    public Vector3 bossOffset = new Vector3(0f, 9f, -15f);
    public float bossLookHeight = 12f;
    [Range(0f, 1f)] public float bossLookBias = 0.35f;
    public float blendSpeed = 2f;
    public float bossFov = 60f;
    [Tooltip("How far past the arena edge the boss camera may go before it is pulled in (cliffs start just beyond).")]
    public float cameraArenaMargin = 5f;
    [Tooltip("Height gained per metre the camera is pulled in from the cliffs.")]
    public float pulledInLift = 1.4f;
    [Tooltip("Half width of the open entrance corridor behind the arena, where the camera may back out.")]
    public float entranceHalfWidth = 7f;
    [Tooltip("Minimum camera height in the entrance corridor, so it looks over the gate ring.")]
    public float entranceCameraHeight = 21f;
    [Tooltip("The boss camera never backs out past this z in the entrance corridor, so it stays in front of the gate ring.")]
    public float entranceMinZ = 61f;
    [Tooltip("Degrees the player is kept above the bottom edge of the screen during the fight.")]
    public float playerFrameMargin = 9f;

    [Header("Boss intro")]
    public bool playIntro = true;
    public float introDuration = 4.5f;
    public string bossTitle = "무스펠헤임의 불꽃 거인";
    public string bossName = "수르트";

    static bool introSeen;   // once per play session, not again after every retry

    // statics survive between editor play sessions when domain reload is turned off
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetIntro() => introSeen = false;

    Camera cam;
    float baseFov;
    float bossBlend;
    float introTimer = -1f;
    float bossBottom, bossHeight;
    Vector3 introStartPos;
    PlayerController player;
    GUIStyle titleStyle, nameStyle;
    float shakeStrength, shakeTime, shakeTimer;
    Vector3 shakeOffset;   // added on top of the follow position each frame and taken off again the next

    void Awake()
    {
        cam = GetComponent<Camera>();
        baseFov = cam.fieldOfView;
        if (target != null) player = target.GetComponent<PlayerController>();
    }

    void LateUpdate()
    {
        if (target == null) return;
        transform.position -= shakeOffset;
        shakeOffset = Vector3.zero;

        bool inArena = boss != null &&
            Vector2.Distance(new Vector2(target.position.x, target.position.z), new Vector2(arenaCenter.x, arenaCenter.z)) < arenaRadius;

        if (inArena && playIntro && !introSeen && introTimer < 0f) StartIntro();
        if (introTimer >= 0f) { UpdateIntro(); return; }

        bossBlend = Mathf.MoveTowards(bossBlend, inArena ? 1f : 0f, blendSpeed * Time.deltaTime);
        float w = Smooth(bossBlend);

        Vector3 normalPos = target.position + offset + Ahead();
        Vector3 desired = w > 0f ? Vector3.Lerp(normalPos, FightPosition(), w) : normalPos;
        transform.position = Vector3.Lerp(transform.position, desired, followSmooth * Time.deltaTime);
        cam.fieldOfView = Mathf.Lerp(baseFov, bossFov, w);
        Vector3 look = Vector3.Lerp(PlayerLook() + Ahead() * (1f - w), BossLook(), w * bossLookBias);
        transform.LookAt(w > 0f ? KeepPlayerInFrame(transform.position, look, cam.fieldOfView) : look);

        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            float k = Mathf.Clamp01(shakeTimer / shakeTime);
            shakeOffset = Random.insideUnitSphere * shakeStrength * k * k;
            transform.position += shakeOffset;
        }
    }

    // A short jolt for heavy impacts: strength in metres, fading out over the given time.
    public void Shake(float strength, float time)
    {
        shakeStrength = strength;
        shakeTime = Mathf.Max(time, 0.01f);
        shakeTimer = shakeTime;
    }

    // Jumps straight to the follow position, e.g. after the player is teleported back to the start.
    public void SnapToTarget()
    {
        if (target == null || introTimer >= 0f) return;
        bossBlend = 0f;
        cam.fieldOfView = baseFov;
        transform.position = target.position + offset + Ahead();
        transform.LookAt(PlayerLook() + Ahead());
    }

    // Shift along the camera's flat viewing direction, so the angle stays the same and the player sits lower on screen.
    Vector3 Ahead()
    {
        Vector3 forward = new Vector3(-offset.x, 0f, -offset.z);
        return forward.sqrMagnitude > 0.001f ? forward.normalized * lookAhead : Vector3.zero;
    }

    // ---------- boss intro ----------

    void StartIntro()
    {
        introTimer = 0f;
        introStartPos = transform.position;
        var r = boss.GetComponentInChildren<Renderer>();
        bossBottom = r != null ? r.bounds.min.y : boss.position.y;
        bossHeight = r != null ? r.bounds.size.y : 20f;
        if (player != null) player.movementLocked = true;
    }

    void UpdateIntro()
    {
        introTimer += Time.deltaTime;
        float k = Mathf.Clamp01(introTimer / introDuration);

        // shot 1: low behind the player, tilting up the giant from the lava to his face
        Vector3 lowPos = target.position + BossFrame(new Vector3(1.5f, 2.2f, -5.5f));
        Vector3 knees = new Vector3(boss.position.x, bossBottom + bossHeight * 0.3f, boss.position.z);
        Vector3 face = new Vector3(boss.position.x, bossBottom + bossHeight * 0.85f, boss.position.z);
        // shot 2: rise and pull back into the fight framing
        Vector3 fightPos = FightPosition();
        Vector3 fightLook = Vector3.Lerp(PlayerLook(), BossLook(), bossLookBias);

        Vector3 pos, look;
        float fov;
        if (k < 0.45f)
        {
            float a = Smooth(k / 0.45f);
            pos = Vector3.Lerp(Vector3.Lerp(introStartPos, lowPos, Smooth(Mathf.Clamp01(k / 0.12f))), lowPos + Vector3.up * 1.5f, a);
            look = Vector3.Lerp(knees, face, a);
            fov = Mathf.Lerp(baseFov, 60f, a);
        }
        else
        {
            float b = Smooth((k - 0.45f) / 0.55f);
            pos = Vector3.Lerp(lowPos + Vector3.up * 1.5f, fightPos, b);
            look = Vector3.Lerp(face, fightLook, b);
            fov = Mathf.Lerp(60f, bossFov, b);
        }
        transform.position = pos;
        cam.fieldOfView = fov;
        transform.LookAt(k < 0.45f ? look : KeepPlayerInFrame(pos, look, fov));

        if (k >= 1f)
        {
            introTimer = -1f;
            introSeen = true;
            bossBlend = 1f;
            var health = player != null ? player.GetComponent<PlayerHealth>() : null;
            if (player != null && (health == null || !health.IsDead)) player.movementLocked = false;
        }
    }

    void OnGUI()
    {
        if (introTimer < 0f) return;
        float k = introTimer / introDuration;
        float alpha = Mathf.Clamp01((k - 0.2f) / 0.1f) * Mathf.Clamp01((0.9f - k) / 0.1f);
        if (alpha <= 0f) return;
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 56, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }
        titleStyle.normal.textColor = new Color(1f, 0.75f, 0.45f, alpha);
        nameStyle.normal.textColor = new Color(1f, 0.55f, 0.2f, alpha);
        float y = Screen.height - 130f;   // bottom of the screen, clear of the giant
        GUI.Label(new Rect(0f, y, Screen.width, 30f), bossTitle, titleStyle);
        GUI.Label(new Rect(0f, y + 28f, Screen.width, 70f), bossName, nameStyle);
    }

    // ---------- helpers ----------

    // Rotates an offset so -z points from the boss through the player (lock-on framing).
    Vector3 BossFrame(Vector3 o)
    {
        Vector3 toBoss = boss.position - target.position;
        toBoss.y = 0f;
        if (toBoss.sqrMagnitude < 0.01f) toBoss = Vector3.forward;
        return Quaternion.LookRotation(toBoss.normalized) * o;
    }

    // Lock-on position. Out the entrance side it rises and stops in front of the gate ring; elsewhere it stays in
    // front of the cliffs and climbs by however much it was pulled in, so the player and the giant both stay in frame.
    Vector3 FightPosition()
    {
        Vector3 p = target.position + BossFrame(bossOffset);
        Vector3 flat = new Vector3(p.x - arenaCenter.x, 0f, p.z - arenaCenter.z);
        bool entranceSide = Mathf.Abs(flat.x) < entranceHalfWidth && flat.z < -arenaRadius;
        if (entranceSide)
        {
            p.y = Mathf.Max(p.y, entranceCameraHeight);
            p.z = Mathf.Max(p.z, entranceMinZ);
            return p;
        }
        float limit = arenaRadius + cameraArenaMargin;
        if (flat.magnitude > limit)
        {
            float pulled = flat.magnitude - limit;
            flat = flat.normalized * limit;
            p.y += pulled * pulledInLift;
        }
        return new Vector3(arenaCenter.x + flat.x, p.y, arenaCenter.z + flat.z);
    }

    // Tilts the aim down when the player would drop out of the bottom of the frame.
    Vector3 KeepPlayerInFrame(Vector3 camPos, Vector3 look, float fov)
    {
        Vector3 toLook = look - camPos, toPlayer = target.position + Vector3.up * 0.3f - camPos;
        float flatLook = new Vector2(toLook.x, toLook.z).magnitude;
        float pitchLook = Mathf.Atan2(toLook.y, flatLook) * Mathf.Rad2Deg;
        float pitchPlayer = Mathf.Atan2(toPlayer.y, new Vector2(toPlayer.x, toPlayer.z).magnitude) * Mathf.Rad2Deg;
        float lowest = pitchLook - (fov * 0.5f - playerFrameMargin);   // lowest pitch still on screen with margin
        if (pitchPlayer >= lowest) return look;
        float pitch = pitchPlayer + (fov * 0.5f - playerFrameMargin);
        return camPos + new Vector3(toLook.x, 0f, toLook.z).normalized * flatLook + Vector3.up * Mathf.Tan(pitch * Mathf.Deg2Rad) * flatLook;
    }

    Vector3 PlayerLook() => target.position + Vector3.up * 1.2f;
    Vector3 BossLook() => boss.position + Vector3.up * bossLookHeight;

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
}
