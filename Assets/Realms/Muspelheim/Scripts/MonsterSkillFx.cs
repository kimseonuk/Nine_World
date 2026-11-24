using System.Collections;
using UnityEngine;

// A Muspelheim monster's attack patterns and their effects, driven by its attack phases (EnemyMonster.Phase).
// The effects fit what each monster is: no magic on a skeleton, only blades, bone, weight and dust.
//
// Skeletons come in five roles, so a room of them attacks from every range and angle at once:
//   Swordsman   - the plain fighter. Special (every 3rd): a long dashing cleave ending in a wide double cut.
//   Shieldbearer- slow and twice as tough. Special (every 2nd): a shield charge that knocks the player flying.
//   Thrower     - hangs back at range and hurls bone spears; a red aim line shows where the throw will go.
//   Berserker   - fast and light. Special (every 2nd): a whirl of three quick cuts all round it.
//   Leaper      - circles at middle range, marks where the player stands, then leaps in and smashes down there.
// Skeleton dragons breathe a fiery bite (fire is their own). Any monster bursts apart as it dies.
[RequireComponent(typeof(EnemyMonster))]
public class MonsterSkillFx : MonoBehaviour
{
    public enum Role { Auto, Swordsman, Shieldbearer, Thrower, Berserker, Leaper }
    enum Kind { Skeleton, SkeletonDragon, Wraith, Other }

    [Tooltip("Auto picks by the skeleton's place among its room's monsters, so every room gets a mix.")]
    public Role role = Role.Auto;

    [Header("Thrower")]
    public float throwRange = 8f;
    public float spearSpeed = 13f;
    public int spearDamage = 5;

    [Header("Berserker")]
    public int whirlDamage = 4;

    [Header("Leaper")]
    public float leapRange = 6.5f;
    public float slamRadius = 2.2f;
    public int slamDamage = 7;

    [Header("Shieldbearer")]
    public float bashKnockback = 6f;

    [Header("All skeletons")]
    [Tooltip("Their plain blows hit for this share of the monster's set damage.")]
    [Range(0.1f, 1f)] public float damageScale = 0.6f;

    static readonly Color Blade = new Color(0.85f, 0.9f, 1f);      // pale steel
    static readonly Color Bone = new Color(0.92f, 0.88f, 0.78f);
    static readonly Color Stone = new Color(0.42f, 0.33f, 0.28f, 0.7f);
    static readonly Color Warn = new Color(1f, 0.15f, 0.08f);

    EnemyMonster monster;
    Kind kind;
    Transform player;
    Vector3 leapTarget;
    LineRenderer aimLine;
    VfxPiece leapMark;

    public Role ActiveRole => role;

    void Awake()
    {
        monster = GetComponent<EnemyMonster>();
        kind = name.Contains("SkeletonDragon") ? Kind.SkeletonDragon : name.Contains("Skeleton") ? Kind.Skeleton
            : name.Contains("FireWraith") ? Kind.Wraith : Kind.Other;
        if (kind != Kind.Skeleton) { role = Role.Swordsman; return; }
        if (role == Role.Auto)
        {
            // spread the roles through the room: every fifth one alike
            int i = transform.GetSiblingIndex();
            role = (Role)(1 + i % 5);
        }
    }

    void Start()
    {
        if (kind == Kind.Wraith) { WraithSetup(); return; }
        if (kind != Kind.Skeleton) return;
        monster.attackDamage = Mathf.Max(1, Mathf.RoundToInt(monster.attackDamage * damageScale));
        monster.specialDamageScale = 1.2f;
        switch (role)
        {
            case Role.Swordsman:
                monster.specialEvery = 3;
                monster.specialLungeScale = 2.6f;
                break;
            case Role.Shieldbearer:
                monster.SetMaxHealth(monster.maxHealth * 2);
                monster.SetTempo(0.8f);
                monster.moveSpeed *= 0.8f;
                monster.specialEvery = 2;
                monster.specialLungeScale = 2.8f;
                monster.specialWindUpScale = 1.8f;
                break;
            case Role.Thrower:
                monster.attackRange = throwRange;
                monster.lungeDistance = 0.3f;
                monster.specialEvery = 0;
                monster.SetTempo(0.7f);   // a long, readable aim before each throw
                monster.SetMaxHealth(Mathf.RoundToInt(monster.maxHealth * 0.7f));
                break;
            case Role.Berserker:
                monster.SetTempo(1.2f);
                monster.moveSpeed *= 1.3f;
                monster.SetMaxHealth(Mathf.RoundToInt(monster.maxHealth * 0.8f));
                monster.specialEvery = 2;
                monster.specialLungeScale = 1.2f;
                break;
            case Role.Leaper:
                monster.attackRange = leapRange;
                monster.specialEvery = 1;           // every attack is the leap
                monster.specialWindUpScale = 1.9f;  // a long crouch: time to step off the mark
                break;
        }
    }

    void OnEnable() { if (monster != null) monster.Phase += OnPhase; }
    void OnDisable() { if (monster != null) monster.Phase -= OnPhase; ClearAim(); }

    Vector3 Chest => transform.position + Vector3.up * 1.2f;
    Vector3 Front(float d) => transform.position + transform.forward * d;

    void OnPhase(EnemyMonster m, string phase)
    {
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p != null) player = p.transform; }
        bool special = phase.StartsWith("special-");
        string step = special ? phase.Substring(8) : phase;
        if (step == "death") { Death(); return; }
        if (kind == Kind.SkeletonDragon) { Dragon(step, special); return; }
        if (kind == Kind.Wraith) { Wraith(step, special); return; }
        switch (role)
        {
            case Role.Thrower: Thrower(step); break;
            case Role.Leaper: Leaper(step); break;
            case Role.Berserker: Berserker(step, special); break;
            case Role.Shieldbearer: Shieldbearer(step, special); break;
            default: Swordsman(step, special); break;
        }
        if (step == "hit") Vfx.Burst(player != null ? player.position + Vector3.up : Front(1.5f), Blade, 8, 6f, 0.15f);
    }

    // ───────────── Swordsman ─────────────

    void Swordsman(string step, bool special)
    {
        if (step == "windup" && special) Vfx.Dust(transform.position, 1.2f, 6, Stone);
        if (step == "strike")
        {
            Vfx.Slash(Front(1.3f) + Vector3.up * 1.1f, transform.forward, special ? 3.6f : 2.6f, Blade, 15f, 120f, 0.22f);
            if (special) StartCoroutine(Afterimages(0.4f));
        }
        if (step == "recover" && special)
        {
            Vfx.Slash(Front(1.4f) + Vector3.up, transform.forward, 4.2f, Blade, -10f, -140f, 0.25f);
            Vfx.Dust(Front(0.5f), 1.2f, 6, Stone);
        }
    }

    // ───────────── Shieldbearer ─────────────

    void Shieldbearer(string step, bool special)
    {
        if (step == "windup" && special)
        {
            // braces behind the shield, scraping its feet
            Vfx.Dust(transform.position - transform.forward * 0.4f, 1.4f, 8, Stone);
            MuspelFx.Lying("mus_shockring", Front(0.6f), 2.2f, 0.6f, 0.5f, 0.6f, 1f);
        }
        if (step == "strike")
        {
            if (special) StartCoroutine(Afterimages(0.35f));
            else Vfx.Slash(Front(1.2f) + Vector3.up * 1.1f, transform.forward, 2.4f, Blade, 60f, 90f, 0.2f);
        }
        if (step == "hit" && special && player != null)
        {
            // the shield slams into them: flung back
            Vfx.Flash(player.position + Vector3.up, 2.5f, Bone, 0.2f);
            CameraShake.Shake(0.2f, 0.25f);
            StartCoroutine(Knock(player, transform.forward, bashKnockback));
        }
        if (step == "recover" && special) Vfx.Dust(Front(1f), 1.6f, 10, Stone);
    }

    // ───────────── Thrower ─────────────

    void Thrower(string step)
    {
        if (step == "windup")
        {
            // a red line marks the throw while it aims
            ClearAim();
            aimLine = new GameObject("SpearAim").AddComponent<LineRenderer>();
            aimLine.sharedMaterial = Vfx.Material("trail", true);
            aimLine.positionCount = 2;
            aimLine.widthMultiplier = 0.18f;
            aimLine.startColor = aimLine.endColor = Warn * 1.5f;
            StartCoroutine(Aim());
        }
        if (step == "strike")
        {
            Vector3 target = player != null ? player.position + Vector3.up * 1f : Front(8f);
            ClearAim();
            StartCoroutine(Spear(Chest + transform.forward * 0.6f, target));
        }
    }

    IEnumerator Aim()
    {
        while (aimLine != null && player != null)
        {
            Vector3 from = Chest + transform.forward * 0.6f;
            Vector3 to = player.position + Vector3.up;
            aimLine.SetPosition(0, from);
            aimLine.SetPosition(1, to + (to - from).normalized * 1.5f);
            aimLine.widthMultiplier = 0.05f + Mathf.PingPong(Time.time * 1.5f, 0.05f);
            yield return null;
        }
    }

    IEnumerator Spear(Vector3 from, Vector3 target)
    {
        var spear = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(spear.GetComponent<Collider>());
        spear.name = "BoneSpear";
        spear.transform.localScale = new Vector3(0.12f, 0.12f, 1.4f);
        var r = spear.GetComponent<Renderer>();
        r.material.color = Bone;
        Vfx.Trail(spear, Bone, 0.25f, 0.18f);
        Vector3 dir = (target - from).normalized;
        spear.transform.SetPositionAndRotation(from, Quaternion.LookRotation(dir));
        Vfx.Dust(transform.position, 0.8f, 4, Stone);
        for (float t = 0f; t < 1.4f; t += Time.deltaTime)
        {
            spear.transform.position += dir * spearSpeed * Time.deltaTime;
            Vector3 p = spear.transform.position;
            if (player != null && Vector3.Distance(p, player.position + Vector3.up) < 0.9f)
            {
                player.SendMessage("TakeHit", spearDamage, SendMessageOptions.DontRequireReceiver);
                Vfx.Burst(p, Bone, 10, 5f, 0.15f);
                break;
            }
            // stuck in the floor or a wall
            if (p.y < transform.position.y + 0.1f || Physics.Raycast(p - dir * 0.3f, dir, 0.6f, ~0, QueryTriggerInteraction.Ignore) && !Hit(p))
            {
                Vfx.Dust(p, 0.6f, 4, Stone);
                break;
            }
            yield return null;
        }
        Destroy(spear);
    }

    bool Hit(Vector3 p) => player != null && Vector3.Distance(p, player.position) < 1.5f;

    // ───────────── Berserker ─────────────

    void Berserker(string step, bool special)
    {
        if (step == "windup" && special) Vfx.Dust(transform.position, 1f, 5, Stone);
        if (step == "strike")
        {
            Vfx.Slash(Front(1.2f) + Vector3.up * 1.1f, transform.forward, 2.4f, Blade, Random.Range(-20f, 20f), 110f, 0.18f);
            if (special) StartCoroutine(Whirl());
        }
    }

    // three quick cuts all round it
    IEnumerator Whirl()
    {
        for (int i = 0; i < 3 && !monster.IsDead; i++)
        {
            yield return new WaitForSeconds(0.16f);
            float yaw = transform.eulerAngles.y + 120f * (i + 1);
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vfx.Slash(transform.position + dir * 1.2f + Vector3.up * 1.1f, dir, 3f, Blade, 0f, 140f, 0.18f);
            Vfx.Dust(transform.position, 1f, 3, Stone);
            if (player != null && Flat(player.position - transform.position) < 2.6f)
                player.SendMessage("TakeHit", whirlDamage, SendMessageOptions.DontRequireReceiver);
        }
    }

    // ───────────── Leaper ─────────────

    void Leaper(string step)
    {
        if (step == "windup" && player != null)
        {
            // crouches and marks where the player stands: that is where it comes down
            leapTarget = new Vector3(player.position.x, transform.position.y, player.position.z);
            float d = Flat(leapTarget - transform.position);
            monster.specialLungeScale = Mathf.Clamp(d / Mathf.Max(0.1f, monster.lungeDistance), 0.5f, 5f);
            ClearMark();
            leapMark = Vfx.Decal(leapTarget + Vector3.up * 0.06f, slamRadius * 2.2f, Warn, "ring", 0f);
            Vfx.Dust(transform.position, 1f, 6, Stone);
        }
        if (step == "strike")
        {
            Vfx.Dust(transform.position, 1.2f, 8, Stone);
            StartCoroutine(Hop());
        }
        if (step == "recover")
        {
            ClearMark();
            Vector3 c = transform.position;
            Vfx.GroundRing(c, slamRadius, new Color(0.9f, 0.8f, 0.7f), 0.35f);
            MuspelFx.Lying("mus_crack", c, slamRadius * 2.2f, 1.2f, 0.8f, 0.8f, 1f, 0f, Random.Range(0f, 360f));
            Vfx.Dust(c, slamRadius, 14, Stone);
            Vfx.Slash(c + Vector3.up * 0.8f, transform.forward, 3f, Blade, 85f, 60f, 0.2f);
            CameraShake.Shake(0.2f, 0.2f);
            if (player != null && Flat(player.position - c) < slamRadius)
                player.SendMessage("TakeHit", slamDamage, SendMessageOptions.DontRequireReceiver);
        }
    }

    // the leap itself: the body arcs up off the ground while the lunge carries it in
    IEnumerator Hop()
    {
        Transform v = monster.visual;
        if (v == null) yield break;
        const float time = 0.35f;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            v.position += Vector3.up * Mathf.Sin(t / time * Mathf.PI) * 2.2f;
            yield return null;
        }
    }

    // ───────────── Fire wraith (a spirit of fire: its attacks are magic) ─────────────
    //   Fireball  - flames gather in its hands, then a fireball flies at the player, curving after them a little.
    //   Brand     - (every 3rd) a burning rune circle under the player; a pillar of fire bursts out of it.
    //   Blink     - when the player gets close it vanishes in a puff of fire and reappears a few metres away.

    [Header("Fire wraith")]
    public int fireballDamage = 6;
    public float fireballSpeed = 10f;
    public int brandDamage = 8;
    public float brandRadius = 2.2f;
    public float blinkCooldown = 5f;

    GameObject handFlame;
    Vector3 brandAt;
    float nextBlink;

    void WraithSetup()
    {
        monster.attackRange = 9f;
        monster.lungeDistance = 0.2f;
        monster.specialEvery = 3;
        monster.specialWindUpScale = 1.3f;
        // it burns as it drifts: embers off it and a warm light about it
        // (yellow-gold rather than red, so it stands out against the red floors)
        Color gold = new Color(1f, 0.82f, 0.3f);
        if (monster.visual != null)
        {
            Vfx.Sparkle(monster.visual.gameObject, gold, 5f, 0.24f);
            Vfx.Glow(monster.visual, Vector3.up * 0.15f, 2.6f, gold * 0.55f, "glow", 20f);
        }
        var lg = new GameObject("WraithLight");
        lg.transform.SetParent(transform, false);
        lg.transform.localPosition = Vector3.up * 1.2f;
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = gold; l.range = 7f; l.intensity = 4.5f; l.shadows = LightShadows.None;
    }

    void Update()
    {
        if (kind != Kind.Wraith || monster == null || monster.IsDead) return;
        if (player == null) { var p = GameObject.FindWithTag("Player"); if (p != null) player = p.transform; else return; }
        if (Time.time >= nextBlink && Flat(player.position - transform.position) < 3f) Blink();
    }

    void Wraith(string step, bool special)
    {
        if (step == "windup")
        {
            ClearHandFlame();
            handFlame = MuspelFx.FireballGlow(transform, special ? 2.2f : 1.4f);
            handFlame.transform.localPosition = Vector3.up * 1.3f + Vector3.forward * 0.7f;
            Vfx.Gather(Chest + transform.forward * 0.7f, 1.8f, MuspelFx.Fire, 0.6f);
            if (special && player != null)
            {
                brandAt = player.position;
                float gy = GroundY(brandAt);
                if (!float.IsNaN(gy)) brandAt.y = gy;
                MuspelFx.Circle(brandAt, brandRadius, 1.3f, 1.4f, 160f);
            }
        }
        if (step == "strike")
        {
            ClearHandFlame();
            if (special) StartCoroutine(Brand(brandAt));
            else if (player != null) StartCoroutine(Fireball(Chest + transform.forward * 0.8f));
        }
    }

    IEnumerator Fireball(Vector3 from)
    {
        var ball = new GameObject("WraithFireball");
        ball.transform.position = from;
        MuspelFx.FireballGlow(ball.transform, 1.3f);
        Vfx.Trail(ball, MuspelFx.Fire, 0.6f, 0.3f);
        Vector3 dir = ((player.position + Vector3.up) - from).normalized;
        for (float t = 0f; t < 2.2f && ball != null; t += Time.deltaTime)
        {
            // curves after the player a little for the first half second
            if (t < 0.5f && player != null)
                dir = Vector3.RotateTowards(dir, ((player.position + Vector3.up) - ball.transform.position).normalized, 1.6f * Time.deltaTime, 0f);
            ball.transform.position += dir * fireballSpeed * Time.deltaTime;
            Vector3 p = ball.transform.position;
            if (player != null && Vector3.Distance(p, player.position + Vector3.up) < 0.9f)
            {
                player.SendMessage("TakeHit", fireballDamage, SendMessageOptions.DontRequireReceiver);
                MuspelFx.Explosion(new Vector3(p.x, player.position.y, p.z), 1f, false);
                break;
            }
            if (Physics.Raycast(p - dir * 0.3f, dir, 0.5f, ~0, QueryTriggerInteraction.Ignore) && !Hit(p))
            {
                MuspelFx.Explosion(p, 1f, false);
                break;
            }
            yield return null;
        }
        if (ball != null) Destroy(ball);
    }

    IEnumerator Brand(Vector3 at)
    {
        yield return new WaitForSeconds(0.25f);
        if (monster.IsDead) yield break;
        MuspelFx.Pillar(at, 6f, brandRadius);
        if (player != null && Flat(player.position - at) < brandRadius)
            player.SendMessage("TakeHit", brandDamage, SendMessageOptions.DontRequireReceiver);
    }

    void Blink()
    {
        nextBlink = Time.time + blinkCooldown;
        Vector3 from = transform.position;
        float floor = GroundY(from);
        for (int i = 0; i < 12; i++)
        {
            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(6f, 8f);
            Vector3 to = player.position + new Vector3(c.x, 0f, c.y);
            float y = GroundY(to);
            if (float.IsNaN(y) || Mathf.Abs(y - floor) > 0.6f) continue;
            to.y = from.y;
            if (Physics.CheckSphere(to + Vector3.up * 0.2f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) continue;
            Vfx.Flash(from + Vector3.up, 3f, MuspelFx.Fire, 0.25f);
            Vfx.Dust(from, 1f, 8, new Color(0.15f, 0.08f, 0.06f, 0.7f));
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = to;
            if (cc != null) cc.enabled = true;
            MuspelFx.Facing("mus_burst", to + Vector3.up, 2.4f, 0.35f, 1.3f, 0.3f, 1.1f);
            Vfx.Dust(to, 1f, 8, new Color(0.15f, 0.08f, 0.06f, 0.7f));
            return;
        }
    }

    float GroundY(Vector3 p)
    {
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, p.y + 4f, p.z), Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider.GetComponentInParent<EnemyMonster>() == null && (player == null || !h.collider.transform.IsChildOf(player)))
                return h.point.y;
        return float.NaN;
    }

    void ClearHandFlame()
    {
        if (handFlame != null) Destroy(handFlame);
        handFlame = null;
    }

    // ───────────── Skeleton dragon ─────────────

    void Dragon(string step, bool special)
    {
        if (step == "strike") MuspelFx.Facing("mus_fireball", Front(1.6f) + Vector3.up * 1.2f, special ? 3f : 2f, 0.3f, 1.4f, 0.6f, 1.3f);
        if (step == "recover" && special)
        {
            Vector3 at = Front(2.5f);
            MuspelFx.Explosion(at, 2.2f);
            if (player != null && Flat(player.position - at) < 2.6f)
                player.SendMessage("TakeHit", 10, SendMessageOptions.DontRequireReceiver);
        }
    }

    // ───────────── Shared ─────────────

    // ghostly blade arcs trailing a dash
    IEnumerator Afterimages(float time)
    {
        for (float t = 0f; t < time && !monster.IsDead; t += 0.08f)
        {
            Vfx.Dust(transform.position - transform.forward * 0.4f, 0.8f, 3, Stone);
            Vfx.Slash(transform.position + transform.forward * 0.8f + Vector3.up * 1.1f, transform.forward, 2f, Blade * 0.6f, 5f, 40f, 0.15f);
            yield return new WaitForSeconds(0.08f);
        }
    }

    // shoves the player through their CharacterController, so walls still stop them
    static IEnumerator Knock(Transform target, Vector3 dir, float strength)
    {
        var cc = target.GetComponent<CharacterController>();
        if (cc == null) yield break;
        dir.y = 0f; dir.Normalize();
        for (float t = 0f; t < 0.25f; t += Time.deltaTime)
        {
            if (cc == null || !cc.enabled) yield break;
            cc.Move(dir * strength * (1f - t / 0.25f) * Time.deltaTime * 2f);
            yield return null;
        }
    }

    void Death()
    {
        ClearHandFlame();
        ClearAim();
        ClearMark();
        Vector3 at = Chest;
        if (kind == Kind.Skeleton)
        {
            // the bones clatter apart
            Vfx.Burst(at, Bone, 14, 5f, 0.18f, 3f);
            Vfx.Dust(transform.position, 1f, 6, Stone);
        }
        else
        {
            Vfx.Burst(at, MuspelFx.Fire, 22, 6f, 0.2f, -0.3f);
            MuspelFx.Facing("mus_burst", at, 2.4f, 0.45f, 1.3f, 0.4f, 1.1f);
        }
    }

    void ClearAim()
    {
        if (aimLine != null) Destroy(aimLine.gameObject);
        aimLine = null;
    }

    void ClearMark()
    {
        if (leapMark != null) Destroy(leapMark.gameObject);
        leapMark = null;
    }

    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
}
