using System.Collections;
using UnityEngine;

// 얼음 감시탑 (Nidhogg's lair): an obelisk of black stone grown over with ice, a cold core floating in its crown. While
// the dragon is fought, the sentinels take turns waking: the core brightens and swells, then either a shard of ice is
// loosed down a marked line at the player, or a burst of frost breaks out all round the sentinel. They can't be broken.
public class IceSentinel : MonoBehaviour
{
    public static float NextFireAt;   // the sentinels share one rhythm, so they fire one at a time
    private const float Interval = 4.5f, Charge = 1.2f;
    private const float ShardRange = 15f, ShardWidth = 1.4f, PulseRadius = 3.6f, Damage = 14f;

    private Room room;
    private Transform core;
    private Light lamp;
    private Vector3 coreHome;
    private bool firing;
    private static readonly Color Frost = new Color(0.55f, 0.88f, 1f);

    public static IceSentinel Build(Transform parent, Vector3 at, Room room, Material ice, Material deepIce, Material stone)
    {
        var root = new GameObject("IceSentinel");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = at;
        root.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        Transform t = root.transform;
        // the black stone footing and plinth
        Mats.Shape(PrimitiveType.Cylinder, t, new Vector3(0f, 0.25f, 0f), new Vector3(2.4f, 0.25f, 2.4f), stone, true, "SentinelBase");
        Mats.Shape(PrimitiveType.Cube, t, new Vector3(0f, 1.2f, 0f), new Vector3(1f, 2f, 1f), stone, true, "SentinelPlinth");
        // the ice climbing it: a tall spire on each side and smaller crystals round the foot
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f + 45f;
            Vector3 off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.55f;
            DungeonGenerator.Crystal6(t, off, Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(12f, 0f, 0f), 0.32f, i % 2 == 0 ? 3.6f : 2.8f, i % 2 == 0 ? ice : deepIce);
        }
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f + Random.Range(-15f, 15f);
            Vector3 off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * Random.Range(0.9f, 1.25f);
            DungeonGenerator.Crystal6(t, off, Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(Random.Range(25f, 45f), 0f, 0f), 0.18f, Random.Range(0.7f, 1.3f), ice);
        }
        var s = root.AddComponent<IceSentinel>();
        s.room = room;
        // the floating core and its light
        s.core = Mats.Shape(PrimitiveType.Sphere, t, new Vector3(0f, 4.2f, 0f), Vector3.one * 0.55f,
            Mats.Get("sentinel_core", new Color(0.7f, 0.95f, 1f), 0.9f, new Color(0.6f, 1.4f, 2.2f))).transform;
        s.core.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        s.coreHome = s.core.localPosition;
        var glow = new GameObject("SentinelLight");
        glow.transform.SetParent(s.core, false);
        s.lamp = glow.AddComponent<Light>();
        s.lamp.type = LightType.Point;
        s.lamp.color = Frost;
        s.lamp.range = 7f;
        s.lamp.intensity = 1.2f;
        s.lamp.shadows = LightShadows.None;
        var col = root.AddComponent<CapsuleCollider>();
        col.center = Vector3.up * 1.6f;
        col.height = 3.2f;
        col.radius = 1f;
        return s;
    }

    private bool Active
    {
        get
        {
            var gm = GameManager.Instance;
            return gm != null && GameManager.IsRunActive && gm.CurrentRoom == room && room.IsFighting && room.Boss != null && !room.Boss.IsDead
                && !BossCutscene.Playing;
        }
    }

    private void Update()
    {
        // the core turns slowly and bobs
        if (core != null && !firing)
        {
            core.localPosition = coreHome + Vector3.up * Mathf.Sin(Time.time * 1.6f + transform.position.x) * 0.15f;
            core.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
        }
        if (!Active || firing || Time.time < NextFireAt)
            return;
        NextFireAt = Time.time + Interval;
        StartCoroutine(Fire());
    }

    private IEnumerator Fire()
    {
        firing = true;
        var gm = GameManager.Instance;
        Transform player = FindAnyObjectByType<PlayerMovement>()?.transform;
        if (player == null)
        {
            firing = false;
            yield break;
        }
        Vector3 ground = new Vector3(transform.position.x, transform.position.y + 0.02f, transform.position.z);
        float toPlayer = Vector3.Distance(Flat(player.position), Flat(transform.position));
        bool pulse = toPlayer < PulseRadius + 1.5f || Random.value < 0.3f;

        // the warning, and the core waking: swelling and brightening, light gathering into it
        GameObject warning;
        Vector3 dir = Flat(player.position - transform.position).normalized;
        if (pulse)
            warning = IceVfx.Frosted(Telegraph.Circle(ground, PulseRadius, Charge));
        else
            warning = IceVfx.Frosted(Telegraph.Line(ground, dir, ShardRange, ShardWidth, Charge));
        Vfx.Gather(core.position, 3f, Frost, Charge, 50f);
        for (float t = 0f; t < Charge; t += Time.deltaTime)
        {
            float k = t / Charge;
            core.localScale = Vector3.one * Mathf.Lerp(0.55f, 0.95f, k) * (1f + Mathf.Sin(t * 30f) * 0.05f);
            lamp.intensity = Mathf.Lerp(1.2f, 5f, k);
            yield return null;
        }
        if (warning != null)
            Destroy(warning);

        if (pulse)
        {
            IceVfx.BlizzardBurst(ground, PulseRadius);
            IceVfx.Flash(ground + Vector3.up * 2f, PulseRadius * 3f, 4f, 0.5f);
            Shockwave.Spawn(ground, PulseRadius, Frost, 16);
            CameraShake.Shake(0.15f, 0.1f);
            if (gm != null && Vector3.Distance(Flat(player.position), Flat(ground)) <= PulseRadius)
                Hit(player, Flat(player.position - ground).normalized * 7f);
        }
        else
            yield return Shard(ground, dir, player);

        // settles again
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            core.localScale = Vector3.one * Mathf.Lerp(0.95f, 0.55f, t / 0.4f);
            lamp.intensity = Mathf.Lerp(5f, 1.2f, t / 0.4f);
            yield return null;
        }
        firing = false;
    }

    // A long shard of ice shot from the core down the marked line, hitting the player once if they're still in it
    private IEnumerator Shard(Vector3 ground, Vector3 dir, Transform player)
    {
        Vector3 from = core.position;
        Vector3 to = ground + dir * ShardRange + Vector3.up * 1f;
        GameObject shard = DungeonGenerator.Crystal6(null, from, Quaternion.LookRotation(to - from) * Quaternion.Euler(90f, 0f, 0f), 0.22f, 1.6f,
            Mats.Get("sentinel_shard", new Color(0.6f, 0.88f, 1f), 0.95f, new Color(0.2f, 0.6f, 1f)));
        Vfx.Trail(shard, Frost, 0.4f, 0.25f);
        bool hit = false;
        const float flight = 0.35f;
        for (float t = 0f; t < flight && shard != null; t += Time.deltaTime)
        {
            shard.transform.position = Vector3.Lerp(from, to, t / flight);
            if (!hit && player != null)
            {
                Vector3 d = Flat(player.position - ground);
                float along = Vector3.Dot(d, dir);
                if (along > 0f && along <= ShardRange * (t / flight) + 1f && Mathf.Abs(Vector3.Dot(d, Vector3.Cross(dir, Vector3.up))) <= ShardWidth / 2f + 0.3f)
                {
                    hit = true;
                    Hit(player, dir * 5f);
                }
            }
            yield return null;
        }
        if (shard != null)
            Destroy(shard);
        IceVfx.Impact(new Vector3(to.x, ground.y, to.z), 1.2f, 2.4f, false);
    }

    private static void Hit(Transform player, Vector3 shove)
    {
        player.GetComponent<PlayerStats>()?.TakeDamage(Damage);
        player.GetComponent<PlayerMovement>()?.Push(shove);
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}

// Fog drifting off the black water of Hvergelmir: banks of the mist art rising and rolling slowly away
public class SpringMist : MonoBehaviour
{
    private float width = 10f, depth = 6f, timer;

    public void Init(float w, float d)
    {
        width = w;
        depth = d;
    }

    private void Update()
    {
        if ((timer -= Time.deltaTime) > 0f)
            return;
        timer = 0.45f;
        Vector3 at = transform.position + new Vector3(Random.Range(-width, width) * 0.5f, Random.Range(0.3f, 1.2f), Random.Range(-depth, depth) * 0.5f);
        MistVfx.Bank(at, new Vector3(Random.Range(-0.6f, 0.6f), 0.15f, Random.Range(-0.8f, -0.2f)), Random.Range(3.5f, 6f), 1.2f, 2f, 2f, 0.45f, new Color(0.75f, 0.85f, 0.95f));
    }
}
