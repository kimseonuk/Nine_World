using UnityEngine;

// An enemy missile that homes in on the player (raven feathers, a valkyrie's spear).
// It turns at a limited rate, so it can be dodged, and breaks on scenery.
public class Projectile : MonoBehaviour
{
    private Transform target;
    private PlayerStats targetStats;
    private Enemy source;
    private float speed;
    private float turnRate;    // degrees per second
    private float damage;
    private float life;
    private float hitRadius;

    private Color glowColor = Color.white;   // the flash and sparks it bursts into

    // Huginn and Muninn: a spectral feather of shadow, its edges burning violet
    public static Projectile Feather(Vector3 from, Vector3 direction, Enemy source, float damage, Color color)
    {
        var go = Body("Feather", from, direction);
        Color edge = Color.Lerp(color, new Color(0.7f, 0.4f, 1f), 0.6f);
        FlatSprite(go, "feather", new Vector3(1.1f, 1.1f, 1f), edge * 2.2f);
        Vfx.Glow(go.transform, Vector3.zero, 1f, edge);
        Vfx.Trail(go, edge, 0.22f, 0.25f);
        return Launch(go, from, direction, source, 7.5f, 100f, damage, 3.5f, 0.55f, edge);
    }

    // A valkyrie's thrown spear: a golden shaft with a white-hot point and a streak of light behind
    public static Projectile Spear(Vector3 from, Vector3 direction, Enemy source, float damage)
    {
        var go = Body("ValkyrieSpear", from, direction);
        var gold = new Color(1f, 0.85f, 0.4f);
        // The winged golden spear modelled in VARCO; plain shapes if it's missing
        var model = Resources.Load<GameObject>("Props/ValkyrieSpear");
        if (model != null)
            Instantiate(model, go.transform, false);
        else
        {
            Mats.Shape(PrimitiveType.Cube, go.transform, Vector3.zero, new Vector3(0.07f, 0.07f, 1.8f), Mats.Get("spear_shaft", gold, 0.7f, gold * 0.45f));
            Mats.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, 0.95f), new Vector3(0.18f, 0.04f, 0.35f), Mats.Get("spear_tip", Color.white, 0.9f, new Color(1.5f, 1.6f, 2f)));
        }
        Vfx.Glow(go.transform, new Vector3(0f, 0f, 1f), 1.3f, new Color(1f, 0.8f, 0.35f) * 1.6f);
        FlatSprite(go, "spark", new Vector3(3.2f, 1f, 1f), gold * 2.6f, streak: true);
        Vfx.Trail(go, gold, 0.3f, 0.22f);
        return Launch(go, from, direction, source, 11f, 75f, damage, 4f, 0.7f, gold);
    }

    // Heimdall: a sphere of Bifrost light that hunts the player, shedding rainbow sparkles
    public static Projectile BifrostOrb(Vector3 from, Vector3 direction, Enemy source, float damage, Color color)
    {
        var go = Body("BifrostOrb", from, direction);
        Vfx.Glow(go.transform, Vector3.zero, 2.6f, color * 1.1f);
        // The swirling rainbow sphere (VARCO), spinning as it flies, with a turning flare at its heart
        Vfx.Glow(go.transform, Vector3.zero, 1.9f, Color.Lerp(Color.white, color, 0.3f) * 0.9f, "bifrostorb", 420f);
        Vfx.Glow(go.transform, Vector3.zero, 1f, Color.Lerp(color, Color.white, 0.7f), "flare", -160f);
        Vfx.Trail(go, color, 0.45f, 0.35f);
        Vfx.Sparkle(go, Color.Lerp(color, Color.white, 0.3f), 5f, 0.2f);
        return Launch(go, from, direction, source, 6.5f, 70f, damage, 6f, 0.7f, color);
    }

    // Heimdall: a crescent of light flung from Hofund (painted in VARCO), bulging forward, flying straight and wide,
    // shimmering as it goes and shedding a trail of light
    public static Projectile SwordWave(Vector3 from, Vector3 direction, Enemy source, float damage)
    {
        var go = Body("SwordWave", from, direction);
        var color = new Color(1f, 0.88f, 0.6f);
        FlatSprite(go, "hofundslash", new Vector3(4.2f, 3f, 1f), color * 2.2f, flip: true);
        FlatSprite(go, "hofundslash", new Vector3(3.4f, 2.4f, 1f), new Color(1f, 1f, 0.95f) * 1.4f, flip: true);
        go.AddComponent<WaveShimmer>();
        Vfx.Sparkle(go, color, 4f, 0.25f);
        Vfx.Trail(go, color, 1.6f, 0.15f);
        return Launch(go, from, direction, source, 12f, 0f, damage, 2.5f, 1.5f, color);
    }

    // An einherjar's sword wave: a smaller, shorter-lived crescent than Hofund's, tinted to the realm
    public static Projectile MobSlash(Vector3 from, Vector3 direction, Enemy source, float damage, Color color)
    {
        var go = Body("MobSlash", from, direction);
        FlatSprite(go, "slash", new Vector3(2.2f, 1.6f, 1f), color * 2.4f);
        FlatSprite(go, "slash", new Vector3(1.8f, 1.2f, 1f), Color.white * 1.6f);
        Vfx.Sparkle(go, color, 2f, 0.2f);
        return Launch(go, from, direction, source, 9f, 0f, damage, 1.3f, 0.9f, color);
    }

    // Heimdall's Heimsight: a fast straight bolt of light along a marked line
    public static Projectile LightBolt(Vector3 from, Vector3 direction, Enemy source, float damage)
    {
        var go = Body("LightBolt", from, direction);
        var color = new Color(0.85f, 0.92f, 1f);
        FlatSprite(go, "spark", new Vector3(4f, 1.1f, 1f), new Color(0.6f, 0.8f, 1f) * 2.4f, streak: true);
        Vfx.Glow(go.transform, new Vector3(0f, 0f, 1.6f), 0.8f, new Color(0.6f, 0.8f, 1f));
        Vfx.Trail(go, color, 0.5f, 0.15f);
        Vfx.Flash(from, 1.4f, color);
        return Launch(go, from, direction, source, 30f, 0f, damage, 1.5f, 0.75f, color);
    }

    // A rune archer's arrow: a fast straight streak of blue light with a glowing head (no homing: sidestep it)
    public static Projectile RuneArrow(Vector3 from, Vector3 direction, Enemy source, float damage)
    {
        var go = Body("RuneArrow", from, direction);
        var blue = new Color(0.45f, 0.75f, 1f);
        Mats.Shape(PrimitiveType.Cube, go.transform, new Vector3(0f, 0f, -0.25f), new Vector3(0.04f, 0.04f, 0.8f), Mats.Get("rune_arrow_shaft", new Color(0.35f, 0.25f, 0.15f)));
        FlatSprite(go, "spark", new Vector3(2.2f, 0.7f, 1f), blue * 2.4f, streak: true);
        Vfx.Glow(go.transform, new Vector3(0f, 0f, 0.2f), 0.7f, blue * 1.4f);
        Vfx.Trail(go, blue, 0.18f, 0.18f);
        return Launch(go, from, direction, source, 15f, 0f, damage, 2.2f, 0.55f, blue);
    }

    // A rune wisp's bolt of raw magic: a blue orb that bends a little toward the player
    public static Projectile ManaBolt(Vector3 from, Vector3 direction, Enemy source, float damage, float speed = 8f, float turnRate = 35f, float size = 1f)
    {
        var go = Body("ManaBolt", from, direction);
        var blue = new Color(0.4f, 0.7f, 1f);
        Vfx.Glow(go.transform, Vector3.zero, 1.1f * size, blue);
        Vfx.Glow(go.transform, Vector3.zero, 0.55f * size, Color.Lerp(blue, Color.white, 0.6f), "flare", 200f);
        Vfx.Trail(go, blue, 0.3f * size, 0.25f);
        return Launch(go, from, direction, source, speed, turnRate, damage, 4f, 0.55f * Mathf.Max(1f, size), blue);
    }

    // A seidr mage's hex: a slow violet sphere wreathed in runes that hunts the player
    public static Projectile CurseOrb(Vector3 from, Vector3 direction, Enemy source, float damage)
    {
        var go = Body("CurseOrb", from, direction);
        var violet = new Color(0.7f, 0.3f, 1f);
        Vfx.Glow(go.transform, Vector3.zero, 1.5f, violet);
        Vfx.Glow(go.transform, Vector3.zero, 1.2f, violet * 0.9f, "runes", 160f);
        Vfx.Glow(go.transform, Vector3.zero, 0.5f, Color.Lerp(violet, Color.white, 0.6f), "flare", -220f);
        Vfx.Trail(go, violet, 0.4f, 0.35f);
        Vfx.Sparkle(go, violet, 4f, 0.15f);
        return Launch(go, from, direction, source, 6f, 55f, damage, 5f, 0.65f, violet);
    }

    // A glowing sprite lying flat along the flight: the texture's up runs forward (its down, flipped), or its width for a streak
    private static void FlatSprite(GameObject parent, string texture, Vector3 scale, Color color, bool streak = false, bool flip = false)
    {
        var go = new GameObject("Sprite_" + texture);
        go.transform.SetParent(parent.transform, false);
        go.transform.localRotation = Quaternion.Euler(90f, streak ? 90f : 0f, flip ? 180f : 0f);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Vfx.Material(texture, additive: true);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        r.SetPropertyBlock(block);
    }

    // Burst into light where it lands (on the player or a wall)
    private void Shatter()
    {
        Vfx.Flash(transform.position, 1.2f, glowColor);
        Vfx.Burst(transform.position, glowColor, 10, 5f, 0.14f);
        Destroy(gameObject);
    }

    // Created where it's thrown from, so its trail and sparkles start there
    private static GameObject Body(string name, Vector3 from, Vector3 direction)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(direction.sqrMagnitude > 0.001f ? direction : Vector3.forward));
        return go;
    }

    private static Projectile Launch(GameObject go, Vector3 from, Vector3 direction, Enemy source, float speed, float turnRate, float damage, float life, float hitRadius, Color glow)
    {
        var p = go.AddComponent<Projectile>();
        p.glowColor = glow;
        var stats = FindAnyObjectByType<PlayerStats>();
        p.target = stats != null ? stats.transform : null;
        p.targetStats = stats;
        p.source = source;
        p.speed = speed;
        p.turnRate = turnRate;
        p.damage = damage;
        p.life = life;
        p.hitRadius = hitRadius;
        return p;
    }

    private void Update()
    {
        // Gone when it runs out of flight or the run is over (pausing freezes it along with time)
        life -= Time.deltaTime;
        GameManager.State state = GameManager.Instance != null ? GameManager.Instance.CurrentState : GameManager.State.Playing;
        if (life <= 0f || state == GameManager.State.Dead || state == GameManager.State.Victory)
        {
            Destroy(gameObject);
            return;
        }

        // Steer toward the player's chest, a limited number of degrees per second
        if (target != null && turnRate > 0f)
        {
            Vector3 aim = target.position + Vector3.up * 0.3f - transform.position;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(aim), turnRate * Time.deltaTime);
        }

        // Heimsight slows the world around the player, missiles included
        Vector3 step = transform.forward * speed * PlayerEffects.Slow * Time.deltaTime;
        // Scenery stops it (enemies and the player don't block their own shots)
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, step.magnitude, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<Enemy>() == null && hit.collider.GetComponentInParent<PlayerStats>() == null
            && hit.collider.name != "VoidEdge")   // (shots fly on over the low balustrade round an open court)
        {
            transform.position = hit.point;
            Shatter();
            return;
        }
        transform.position += step;

        if (targetStats != null && Vector3.Distance(transform.position, target.position + Vector3.up * 0.3f) <= hitRadius)
        {
            targetStats.TakeDamage(damage, source);
            Shatter();
        }
    }
}
