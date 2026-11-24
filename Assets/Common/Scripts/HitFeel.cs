using System.Collections.Generic;
using UnityEngine;

// 타격감: what every blow the player lands on a monster does, whatever monster it is and whatever dealt it (a sword,
// an arrow, a spell): the body flashes white and jolts, sparks burst where it was struck, the camera kicks the way the
// blow went, a damage number pops out of it, and the hit sounds. A blow well above the player's usual (a crit, a charged
// swing, a finisher) is a heavy hit: bigger sparks, a gold number, a deeper sound and a split-second freeze.
// Added to every monster by Enemy.OnEnable; it watches the monster's health, so no monster script needs changing.
public class HitFeel : MonoBehaviour
{
    private Enemy enemy;
    private int last = int.MinValue;
    private float height = 1.6f, size = 1f;

    // the player's usual blow, learnt as they play (heavy = well above it)
    private static float usualHit;
    private static float lastStop = -10f, lastKick = -10f;

    public static void Attach(Enemy e)
    {
        if (e != null && e.GetComponent<HitFeel>() == null)
            e.gameObject.AddComponent<HitFeel>();
    }

    private void Start()
    {
        enemy = GetComponent<Enemy>();
        Bounds b = default;
        bool any = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r.name.StartsWith("Vfx") || r.name.StartsWith("Sprite_")) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (any)
        {
            height = Mathf.Clamp(b.max.y - transform.position.y, 0.6f, 8f);
            size = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) / 0.6f, 0.7f, 3f);
        }
    }

    private void LateUpdate()
    {
        if (enemy == null) return;
        int h = enemy.Health;
        if (last == int.MinValue || h > last) { last = h; return; }   // (spawn scaling and heals aren't hits)
        if (h < last)
        {
            int dmg = last - h;
            last = h;
            if (h > 0) Struck(dmg);     // (the killing blow is the death burst's, through Enemy.AnyDied)
        }
    }

    private Vector3 Chest => transform.position + Vector3.up * height * 0.55f;

    private static Transform player;
    private static Transform Player
    {
        get
        {
            if (player == null)
            {
                var p = FindAnyObjectByType<PlayerStats>();
                if (p != null) player = p.transform;
            }
            return player;
        }
    }

    private Vector3 BlowDirection()
    {
        Vector3 d = Player != null ? transform.position - Player.position : -transform.forward;
        d.y = 0f;
        return d.sqrMagnitude > 0.001f ? d.normalized : Vector3.forward;
    }

    private void Struck(int dmg)
    {
        bool crit = critMarks.TryGetValue(enemy.GetInstanceID(), out float markedAt) && Time.unscaledTime - markedAt < 0.25f;
        if (crit) critMarks.Remove(enemy.GetInstanceID());
        float rel = dmg / Mathf.Max(1f, usualHit <= 0f ? dmg : usualHit);
        if (!crit) usualHit = usualHit <= 0f ? dmg : Mathf.Lerp(usualHit, dmg, 0.08f);
        bool heavy = crit || (rel >= 2.2f && dmg >= 6);
        bool tick = rel < 0.35f;     // a burn or poison tick: just the number
        Vector3 at = Chest;
        Vector3 dir = BlowDirection();

        DamagePopups.Show(at + Vector3.up * height * 0.35f, dmg, heavy ? DamagePopups.Style.Heavy : tick ? DamagePopups.Style.Tick : DamagePopups.Style.Normal);
        if (tick) return;

        enemy.HitFlash(heavy ? 0.09f : 0.06f);
        EnemyVoice voice = EnemyVoice.Of(enemy);
        voice?.Hurt();
        EnemyFeel.Coil(transform, heavy ? 0.22f : 0.15f, heavy ? 0.18f : 0.11f);
        EnemyFeel.EnemyStruck(at - dir * 0.25f * size, dir, heavy, size);
        float loud = heavy ? 1f : Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(rel));
        GameAudio.Play(heavy ? "hitheavy" : "hit", transform.position, loud * 0.7f);
        if (heavy) GameAudio.Play("hit", transform.position, 0.45f, 0.8f);
        // the weapon that landed (a greatsword's chop, an arrow's thunk, a gauntlet's zap)...
        string weapon = PlayerMoveAudio.WeaponKey;
        if (weapon != null) GameAudio.Play("w_" + weapon + "_hit", transform.position, loud * 0.85f);
        // ...and what it landed on: plate rings, stone cracks, crystal chimes, a straw dummy thumps
        string mat = voice != null ? voice.material : "flesh";
        GameAudio.Play(mat == "dummy" ? "dummy_hit" : "hit_" + mat, transform.position, loud * 0.6f);
        if (crit) GameAudio.Play("crit_hit", transform.position, 0.9f);

        float near = EnemyFeel.Near(transform.position);
        if (near < 0.3f) return;
        float now = Time.unscaledTime;
        if (now - lastKick > 0.045f)
        {
            lastKick = now;
            CameraShake.Kick(dir, heavy ? 0.22f : 0.07f);
        }
        if (heavy && now - lastStop > 0.18f)
        {
            lastStop = now;
            HitStop.Freeze(enemy.IsBoss ? 0.05f : 0.075f);
            CameraShake.Shake(0.12f, 0.1f);
        }
        else if (!heavy && now - lastStop > 0.11f)
        {
            lastStop = now;
            HitStop.Freeze(0.028f);
        }
    }

    // A critical hit is told by the fighter (PlayerMiner.HitEnemy): the blow it made is drawn as a heavy one
    private static readonly Dictionary<int, float> critMarks = new Dictionary<int, float>();
    public static void MarkCrit(Enemy e)
    {
        if (e == null) return;
        if (critMarks.Count > 64) critMarks.Clear();
        critMarks[e.GetInstanceID()] = Time.unscaledTime;
    }

    // A monster falls: a burst and a ring on the floor, its kill sound, the camera kicked and the action held a beat
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Wire()
    {
        Enemy.AnyDied -= OnDied;
        Enemy.AnyDied += OnDied;
        usualHit = 0f;
        GameManager.HideHitNumbers = true;   // (these damage numbers stand in for the old floaters)
    }

    private static void OnDied(Enemy e)
    {
        if (e == null) return;
        var feel = e.GetComponent<HitFeel>();
        float h = feel != null ? feel.height : 1.6f, s = feel != null ? feel.size : 1f;
        Vector3 at = e.transform.position + Vector3.up * h * 0.5f;
        Vector3 dir = Player != null ? e.transform.position - Player.position : Vector3.forward;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
        EnemyFeel.EnemyKilled(e.transform.position, at, s * (e.IsBoss ? 2f : 1f), e.BaseTint);
        GameAudio.Play("kill", e.transform.position, e.IsBoss ? 1f : 0.85f, e.IsBoss ? 0.8f : 1f);
        EnemyVoice.Of(e)?.Die();
        if (e.IsBoss) GameAudio.Play("boss_death");
        if (EnemyFeel.Near(e.transform.position) < 0.3f) return;
        CameraShake.Kick(dir, e.IsBoss ? 0.4f : 0.16f);
        CameraShake.Shake(e.IsBoss ? 0.5f : 0.14f, e.IsBoss ? 0.35f : 0.08f);
        if (Time.unscaledTime - lastStop > 0.1f)
        {
            lastStop = Time.unscaledTime;
            HitStop.Freeze(e.IsBoss ? 0.2f : 0.06f);
        }
    }
}

// Damage numbers: pop out of the monster big, settle, float up and fade. Heavy hits are gold and larger.
public class DamagePopups : MonoBehaviour
{
    public enum Style { Normal, Heavy, Tick, Hurt, HurtHeavy }   // (Hurt: what the player lost, in red)

    private struct Popup
    {
        public Vector3 at;
        public string text;
        public Style style;
        public float born;
        public float drift;
    }

    private static DamagePopups instance;
    private readonly List<Popup> popups = new List<Popup>();
    private GUIStyle style;
    private const float Life = 0.85f;

    public static void Show(Vector3 at, int amount, Style s)
    {
        if (instance == null)
        {
            var go = new GameObject("DamagePopups");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DamagePopups>();
        }
        if (instance.popups.Count > 60) instance.popups.RemoveAt(0);
        instance.popups.Add(new Popup
        {
            at = at,
            text = s == Style.Hurt || s == Style.HurtHeavy ? "-" + amount + (s == Style.HurtHeavy ? "!" : "") : s == Style.Heavy ? amount + "!" : amount.ToString(),
            style = s,
            born = Time.unscaledTime,
            drift = Random.Range(-0.5f, 0.5f),
        });
    }

    private void OnGUI()
    {
        if (popups.Count == 0 || Event.current.type != EventType.Repaint) return;
        Camera cam = Camera.main;
        if (cam == null || BossCutscene.Playing) return;
        GUI.depth = 5;   // under the HUD
        style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow, wordWrap = false };
        float scale = Screen.height / 1080f;
        float now = Time.unscaledTime;
        popups.RemoveAll(p => now - p.born > Life);
        foreach (Popup p in popups)
        {
            float age = now - p.born;
            float k = age / Life;
            // rises and drifts aside, eased out
            Vector3 world = p.at + Vector3.up * (1f - (1f - k) * (1f - k)) * 1.1f + cam.transform.right * p.drift * k;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0f) continue;
            float pop = age < 0.1f ? Mathf.Lerp(1.9f, 1f, age / 0.1f) : 1f;
            float baseSize = p.style == Style.Heavy || p.style == Style.HurtHeavy ? 44f : p.style == Style.Tick ? 20f : p.style == Style.Hurt ? 34f : 30f;
            int fs = Mathf.Max(10, Mathf.RoundToInt(baseSize * pop * scale));
            style.fontSize = fs;
            float alpha = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            Color c = p.style == Style.Heavy ? new Color(1f, 0.78f, 0.25f) : p.style == Style.Tick ? new Color(0.85f, 0.8f, 0.75f)
                : p.style == Style.Hurt ? new Color(1f, 0.3f, 0.25f) : p.style == Style.HurtHeavy ? new Color(1f, 0.12f, 0.1f) : Color.white;
            var r = new Rect(sp.x - 120f, Screen.height - sp.y - fs, 240f, fs * 2f);
            // dark outline, then the number
            style.normal.textColor = new Color(0.08f, 0.04f, 0.02f, alpha * 0.9f);
            float o = Mathf.Max(1.5f, fs * 0.07f);
            foreach (var off in new[] { new Vector2(-o, 0), new Vector2(o, 0), new Vector2(0, -o), new Vector2(0, o), new Vector2(o, o) })
                GUI.Label(new Rect(r.x + off.x, r.y + off.y, r.width, r.height), p.text, style);
            c.a = alpha;
            style.normal.textColor = c;
            GUI.Label(r, p.text, style);
        }
    }
}
