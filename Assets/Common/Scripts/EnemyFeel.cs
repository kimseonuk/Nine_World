using System.Collections;
using UnityEngine;

// What makes a monster's attack feel alive, the same way for every monster: the windup glints and the body coils, the
// blow sweeps an arc, a slam cracks the floor and shakes the screen, a roar sends a war-cry ring rolling out and rocks
// the camera, and a hit on the player sparks and freezes the action for a split second.
// The effects are VARCO sheets in white (Resources/Vfx/foe_*) tinted per monster. Shakes fade with distance from the
// player, so a fight across the hall doesn't rattle the screen, and nothing here flashes the whole screen.
public static class EnemyFeel
{
    public static readonly Color Warm = new Color(1f, 0.88f, 0.7f);

    private static PlayerStats player;
    private static PlayerStats Player => player != null ? player : player = Object.FindAnyObjectByType<PlayerStats>();

    // 1 close to the player, fading to nothing 30 m away
    public static float Near(Vector3 at)
    {
        if (Player == null) return 1f;
        Vector3 d = Player.transform.position - at;
        d.y = 0f;
        return Mathf.Clamp01(1f - (d.magnitude - 6f) / 24f);
    }

    public static void Shake(Vector3 at, float seconds, float amount)
    {
        float a = amount * Near(at);
        if (a > 0.02f)
            CameraShake.Shake(seconds, a);
    }

    private static Quaternion Lying(Vector3 forward)
    {
        forward.y = 0f;
        float yaw = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward).eulerAngles.y : 0f;
        return Quaternion.Euler(90f, yaw, 0f);
    }

    private static Quaternion LyingRandom => Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);

    // The halls are bright marble, where an additive glow alone washes out: a dark, solid copy of the same frames (the
    // sheet's _ink twin, its alpha the glow's brightness) goes down first, so every effect reads on any floor
    private static Color InkOf(Color c, float alpha)
    {
        float m = Mathf.Max(0.001f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
        Color k = new Color(c.r / m, c.g / m, c.b / m) * 0.28f;
        k.a = alpha;
        return k;
    }

    private static VfxPiece Layered(string tex, Vector3 at, float size, Color glow, float seconds, float s0, float s1, float spin,
        Quaternion? lying, bool sheet = true, float ink = 0.7f)
    {
        // (the ink just under a lying piece, or just behind a facing one, so the glow always draws over it)
        Vector3 back = lying.HasValue ? -Vector3.up * 0.01f : (Camera.main != null ? Camera.main.transform.forward * 0.03f : Vector3.zero);
        VfxPiece under = Vfx.Sprite(tex + "_ink", at + back, size, InkOf(glow, ink), seconds, s0, s1, spin, lying, additive: false);
        VfxPiece over = Vfx.Sprite(tex, at, size, glow, seconds, s0, s1, spin, lying);
        if (sheet)
        {
            under.Sheet(4, 4);
            over.Sheet(4, 4);
        }
        return over;
    }

    // A dust cloud: solid, not a glow (a dusty grey that shows on light stone and dark alike)
    private static void DustCloud(Vector3 at, float size, float seconds = 0.6f)
    {
        Vfx.Sprite("foe_dust_ink", at, size, new Color(0.5f, 0.45f, 0.4f, 0.9f), seconds, 0.9f, 1.15f, 0f, additive: false).Sheet(4, 4);
    }

    // ── The windup: a glint where the blow will come from, and the body coiling for it ──
    public static void Windup(Transform who, Vector3 at, Color color, float size = 1f)
    {
        Layered("foe_glint", at, 2.4f * size, color * 2.6f, 0.5f, 0.7f, 1.2f, 25f, null, ink: 0.8f);
        Coil(who, 0.35f);
        EnemyVoice.Of(who)?.Attack();
        // the threat: it flushes red as it gathers, and a big one sounds a warning close by
        Enemy foe = who != null ? who.GetComponentInParent<Enemy>() : null;
        if (foe != null) foe.WarnFlash(0.35f);
        if (size >= 1.15f || (foe != null && foe.IsBoss))
            GameAudio.Play("boss_warn", at, foe != null && foe.IsBoss ? 0.75f : 0.45f, 1.1f);
    }

    // A quick squash then stretch of the monster's model (not its root, so its collider and size stay)
    public static void Coil(Transform who, float seconds, float amount = 0.08f)
    {
        if (who == null) return;
        Transform body = Body(who);
        if (body == null) return;
        var squash = body.GetComponent<FeelSquash>() ?? body.gameObject.AddComponent<FeelSquash>();
        squash.Play(seconds, amount);
    }

    private static Transform Body(Transform who)
    {
        Animator a = who.GetComponentInChildren<Animator>();
        if (a != null && a.transform != who) return a.transform;
        return who.childCount > 0 ? who.GetChild(0) : null;
    }

    // ── The blow: one arc sweeping the way it swings ──
    public static void Swing(Vector3 at, Vector3 forward, float reach, Color color)
    {
        Vector3 ground = new Vector3(at.x, at.y, at.z);
        Layered("foe_slash", ground, reach * 1.9f, color * 1.5f, 0.32f, 0.92f, 1.04f, 0f, Lying(forward)).Hold();
        Shake(at, 0.1f, 0.08f);
        GameAudio.Play("swing2", at, 0.75f, 0.8f);
        GameAudio.Play("whoosh_big", at, 0.4f, 1.2f);
    }

    // ── A lunge or charge setting off: dust kicked up behind, a streak along the way ──
    public static void Lunge(Vector3 from, Vector3 dir, Color color, float size = 1f)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
        dir.Normalize();
        Vector3 feet = new Vector3(from.x, from.y, from.z);
        DustCloud(feet - dir * 0.6f * size + Vector3.up * 0.8f * size, 2.6f * size, 0.7f);
        Layered("foe_streak", feet + dir * 1.5f * size + Vector3.up * 0.15f, 4f * size, color * 2f, 0.32f, 0.9f, 1.15f, 0f,
            Quaternion.Euler(90f, Quaternion.LookRotation(dir).eulerAngles.y - 90f, 0f), sheet: false, ink: 0.8f);
        Shake(from, 0.1f, 0.06f * size);
        GameAudio.Play("whoosh_big", from, 0.6f, 1.1f);
    }

    // ── A slam landing: a burst on the floor, a crack left behind, the screen jolted ──
    public static void Impact(Vector3 at, float radius, Color color, float shake = 0.2f)
    {
        Vector3 ground = new Vector3(at.x, Mathf.Max(0.05f, at.y + 0.05f), at.z);
        Layered("foe_impact", ground, radius * 2.6f, color * 2.2f, 0.6f, 0.95f, 1.08f, 0f, LyingRandom, ink: 0.85f);
        // the crack stays a moment: dark, the way a crack in stone looks (just its ink, faintly lit by the glow's colour)
        Color crackInk = InkOf(color, 0.95f) * 0.6f;
        crackInk.a = 0.95f;
        Vfx.Sprite("foe_crack_ink", ground - Vector3.up * 0.02f, radius * 2f, crackInk, 2.4f, 0.9f, 1f, 0f, LyingRandom, additive: false).Hold();
        DustCloud(new Vector3(at.x, 0.7f, at.z), radius * 1.6f, 0.8f);
        Shake(at, 0.22f, shake);
        GameAudio.Play("slam", at, Mathf.Clamp(0.5f + radius * 0.15f, 0.5f, 1f));
    }

    // ── A roar: war-cry rings rolling out from its feet, dust blown away, the camera rocked ──
    public static void Roar(Transform who, Color color, float radius = 6f, float shake = 0.32f)
    {
        if (who == null) return;
        Vector3 at = who.position;
        Vector3 ground = new Vector3(at.x, at.y + 0.08f, at.z);
        Layered("foe_warcry", ground, radius * 2.2f, color * 1.5f, 0.7f, 0.85f, 1.1f, 0f, LyingRandom, ink: 0.75f);
        Layered("foe_warcry", at + Vector3.up * 1.6f, radius * 0.9f, color * 1.1f, 0.6f, 0.8f, 1.2f, 0f, null, ink: 0.45f);   // (round its head)
        DustCloud(new Vector3(at.x, 0.7f, at.z), radius * 0.9f, 0.7f);
        Coil(who, 0.5f, 0.1f);
        // its own cry: a god shouts, a beast squeals, a construct whirs up; anything else roars
        EnemyVoice voice = EnemyVoice.Of(who);
        Enemy foe = who.GetComponentInParent<Enemy>();
        if (foe is TyrBoss || foe is HeimdallBoss) GameAudio.Play("god_shout", at, 1f, foe is HeimdallBoss ? 0.92f : 1f);
        else if (voice != null && voice.family != EnemyVoice.Family.None) voice.Skill(1.2f);
        else GameAudio.Play("roar", at, Mathf.Clamp(0.5f + radius * 0.08f, 0.5f, 1f), radius > 5f ? 0.85f : 1.05f);
        // (a pack crying out together rocks the camera once, not once per mouth)
        if (Time.time - lastRoarShake > 0.8f || shake > lastRoarAmount)
        {
            lastRoarShake = Time.time;
            lastRoarAmount = shake;
            Shake(at, 0.55f, shake);
        }
    }

    private static float lastRoarShake = -10f, lastRoarAmount;

    // ── A shot or spell leaving the hand: a glint, a little kick back ──
    public static void Launch(Transform who, Vector3 at, Color color)
    {
        Layered("foe_glint", at, 0.9f, color * 1.5f, 0.3f, 0.9f, 1.2f, 40f, null, ink: 0.5f);
        Coil(who, 0.22f, 0.05f);
        EnemyVoice v = EnemyVoice.Of(who);
        GameAudio.Play("shoot", at, v != null && v.HasOwn("atk") ? 0.25f : 0.55f);
        v?.Attack(0.8f);
    }

    // ── The player's blow landing on a monster (HitFeel): a white-gold spark burst, a glint for a heavy one ──
    public static readonly Color StrikeColor = new Color(1f, 0.88f, 0.62f);

    public static void EnemyStruck(Vector3 at, Vector3 dir, bool heavy, float size = 1f)
    {
        float s = Mathf.Clamp(size, 0.7f, 2.5f);
        Layered("foe_hitspark", at, (heavy ? 2.8f : 1.8f) * s, StrikeColor * (heavy ? 2.8f : 2.1f), heavy ? 0.3f : 0.2f, 0.8f, 1.25f,
            Random.Range(-45f, 45f), null, ink: 0.55f);
        if (heavy)
        {
            Layered("foe_glint", at, 3.4f * s, new Color(1f, 0.8f, 0.45f) * 2.6f, 0.32f, 0.6f, 1.4f, 35f, null, ink: 0.5f);
            Layered("foe_slash", new Vector3(at.x, 0.12f, at.z), 2.6f * s, StrikeColor * 1.8f, 0.25f, 0.9f, 1.1f, 0f, Lying(dir)).Hold();
        }
    }

    // ── A monster falling: a burst at its chest, a ring and dust on the floor ──
    public static void EnemyKilled(Vector3 feet, Vector3 chest, float size, Color tint)
    {
        float s = Mathf.Clamp(size, 0.7f, 4f);
        Color c = Color.Lerp(StrikeColor, tint, 0.25f);
        Layered("foe_hitspark", chest, 3.2f * s, c * 2.6f, 0.38f, 0.7f, 1.4f, Random.Range(0f, 360f), null, ink: 0.6f);
        Layered("foe_glint", chest, 2.6f * s, new Color(1f, 0.9f, 0.6f) * 2.4f, 0.4f, 0.5f, 1.5f, 60f, null, ink: 0.4f);
        Layered("foe_warcry", new Vector3(feet.x, feet.y + 0.08f, feet.z), 3.2f * s, c * 1.4f, 0.45f, 0.6f, 1.2f, 0f, LyingRandom, ink: 0.5f);
        DustCloud(new Vector3(feet.x, feet.y + 0.6f, feet.z), 1.8f * s, 0.6f);
    }

    private static float lastPlayerHit = -10f;

    // ── The player struck: a spark on them and a split-second freeze (hit-stop), stronger for heavier blows ──
    public static void PlayerHit(PlayerStats p, Enemy source, float taken)
    {
        if (p == null) return;
        float weight = Mathf.Clamp01(taken / Mathf.Max(1f, p.MaxHealth * 0.25f));
        // (a quick run of small ticks — standing in fire, a stream of shots — is felt as one: just the red and the number)
        bool tick = weight < 0.15f && Time.unscaledTime - lastPlayerHit < 0.3f;
        lastPlayerHit = Time.unscaledTime;
        if (tick)
        {
            PlayerHurtFeel.Of(p)?.Graze(taken);
            return;
        }
        Vector3 chest = p.transform.position + Vector3.up * 1f;
        if (source != null)
        {
            Vector3 from = source.transform.position - p.transform.position;
            from.y = 0f;
            chest += from.normalized * 0.3f;
        }
        Layered("foe_hitspark", chest, 2.4f + weight * 1.4f, new Color(1f, 0.45f, 0.35f) * 2.4f, 0.34f, 0.9f, 1.2f, Random.Range(-30f, 30f), null, ink: 0.85f);
        if (weight > 0.45f)
            Layered("foe_glint", chest, 3.2f, new Color(1f, 0.35f, 0.25f) * 2.6f, 0.3f, 0.6f, 1.4f, 35f, null, ink: 0.5f);
        CameraShake.Shake(0.2f + 0.15f * weight, 0.14f + 0.22f * weight);
        HitStop.Freeze(0.06f + 0.09f * weight);
        GameAudio.Play("hurt", null, 0.8f + 0.2f * weight);
        // the red at the screen's edges, the shove, the body's flash, the number and the blow's own sounds
        PlayerHurtFeel.Of(p)?.Hit(source, taken, weight);
    }
}

// The squash-and-stretch of a monster's model: squashed down and widened, then springing up taller, then back
public class FeelSquash : MonoBehaviour
{
    private Vector3 rest;
    private bool resting = true;
    private float t, seconds, amount;

    public void Play(float duration, float strength)
    {
        if (resting) rest = transform.localScale;
        resting = false;
        t = 0f;
        seconds = Mathf.Max(0.05f, duration);
        amount = strength;
    }

    private void LateUpdate()
    {
        if (resting) return;
        t += Time.deltaTime;
        float k = t / seconds;
        if (k >= 1f)
        {
            transform.localScale = rest;
            resting = true;
            return;
        }
        // down for the first 40%, up through the middle, settling at the end
        float s = k < 0.4f ? -Mathf.Sin(k / 0.4f * Mathf.PI) : Mathf.Sin((k - 0.4f) / 0.6f * Mathf.PI) * 0.6f;
        float y = 1f + s * amount, xz = 1f - s * amount * 0.5f;
        transform.localScale = new Vector3(rest.x * xz, rest.y * y, rest.z * xz);
    }

    private void OnDisable()
    {
        if (!resting) transform.localScale = rest;
        resting = true;
    }
}

// A split-second freeze when a blow lands (only from normal speed, and never over a pause or a slow-motion moment)
public class HitStop : MonoBehaviour
{
    private static HitStop instance;
    private Coroutine running;

    public static void Freeze(float seconds)
    {
        if (Time.timeScale < 0.99f) return;
        if (instance == null)
        {
            var go = new GameObject("HitStop");
            Object.DontDestroyOnLoad(go);
            instance = go.AddComponent<HitStop>();
        }
        if (instance.running != null) return;
        instance.running = instance.StartCoroutine(instance.Hold(seconds));
    }

    private IEnumerator Hold(float seconds)
    {
        const float slow = 0.05f;
        Time.timeScale = slow;
        yield return new WaitForSecondsRealtime(seconds);
        if (Mathf.Approximately(Time.timeScale, slow)) Time.timeScale = 1f;   // (unless something else took over the clock)
        running = null;
    }
}
