using UnityEngine;

// A slow laid on any enemy (울르의 늑대's bite): for a while it moves and acts at a fraction of its speed. Enemies have
// no shared notion of speed, so this works from outside: whatever ground it covered this frame is cut back to the
// fraction, and its animators are played slower (taking up whatever speed its own script last gave them).
[DefaultExecutionOrder(1000)]
public class EnemySlow : MonoBehaviour
{
    private float until, factor = 1f;
    private Vector3 last;
    private Animator[] animators;
    private float[] baseSpeeds, setSpeeds;
    private GameObject frost;

    private Color? tint;   // (the glow round its feet: frost by default; a bleed pool's slow is red)

    public static void Apply(Enemy enemy, float factor, float seconds, Color? tint = null)
    {
        if (enemy == null || enemy.IsDead)
            return;
        EnemySlow slow = enemy.GetComponent<EnemySlow>();
        if (slow == null)
        {
            slow = enemy.gameObject.AddComponent<EnemySlow>();
            slow.enabled = false;
        }
        if (!slow.Active) slow.tint = tint;
        slow.factor = Mathf.Min(slow.Active ? slow.factor : 1f, Mathf.Clamp01(factor));
        slow.until = Mathf.Max(slow.until, Time.time + seconds);
        slow.enabled = true;
    }

    public bool Active => Time.time < until;
    public float Remaining => Mathf.Max(0f, until - Time.time);

    private void OnEnable()
    {
        last = transform.position;
        animators = GetComponentsInChildren<Animator>();
        baseSpeeds = new float[animators.Length];
        setSpeeds = new float[animators.Length];
        for (int i = 0; i < animators.Length; i++)
            baseSpeeds[i] = setSpeeds[i] = animators[i].speed;
        if (frost == null)
        {
            // frost clinging round its feet while it lasts
            float r = 0.8f;
            Collider c = GetComponent<Collider>();
            if (c != null)
                r = Mathf.Max(c.bounds.extents.x, c.bounds.extents.z) + 0.3f;
            frost = Vfx.Glow(transform, Vector3.up * 0.08f, r * 2f, tint ?? new Color(0.55f, 0.85f, 1f, 0.8f), "runes", 60f);
        }
    }

    // (before the animators run: the speed its own script set this frame, scaled down)
    private void Update()
    {
        for (int i = 0; i < animators.Length; i++)
        {
            Animator a = animators[i];
            if (a == null)
                continue;
            if (!Mathf.Approximately(a.speed, setSpeeds[i]))
                baseSpeeds[i] = a.speed;   // its own script changed it
            setSpeeds[i] = a.speed = baseSpeeds[i] * (Active ? factor : 1f);
        }
    }

    private void LateUpdate()
    {
        Enemy enemy = GetComponent<Enemy>();
        if (!Active || enemy == null || enemy.IsDead)
        {
            enabled = false;
            return;
        }
        // the ground it covered this frame, cut back (height left alone, so it still falls and lands)
        Vector3 p = transform.position, step = p - last;
        if (step.sqrMagnitude < 4f)   // (a teleport or knock-back is let through)
        {
            step.x *= factor;
            step.z *= factor;
            Vector3 to = last + step;
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null && cc.enabled)
            {
                cc.enabled = false;
                transform.position = to;
                cc.enabled = true;
            }
            else
                transform.position = to;
        }
        last = transform.position;
    }

    private void OnDisable()
    {
        if (animators != null)
            for (int i = 0; i < animators.Length; i++)
                if (animators[i] != null)
                    animators[i].speed = baseSpeeds[i];
        if (frost != null)
            Destroy(frost);
        frost = null;
        factor = 1f;
    }
}
