using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Hit feel for the player's blows: a short hit-stop freeze, a burst of sparks, a small camera kick and a floating
// damage number. Monsters and bosses call Hit from their TakeHit, so every attack and skill gets it.
public class HitFeedback : MonoBehaviour
{
    public static HitFeedback Instance { get; private set; }

    [Tooltip("Additive particle material for the sparks.")]
    public Material sparkMaterial;
    public Color sparkColor = new Color(1f, 0.6f, 0.2f);
    [Tooltip("Freeze length per point of damage, clamped to the range below.")]
    public float stopPerDamage = 0.006f;
    public Vector2 stopRange = new Vector2(0.03f, 0.09f);
    [Tooltip("Hits closer together than this don't freeze again (whirlwind ticks would stutter).")]
    public float stopCooldown = 0.12f;
    public float numberLifetime = 0.8f;

    struct Number { public Vector3 pos; public string text; public float t; public float size; }
    readonly List<Number> numbers = new List<Number>();
    ParticleSystem sparks;
    float lastStop = -1f;
    bool stopping;
    GUIStyle style;

    void Awake()
    {
        Instance = this;
        sparks = BuildSparks();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (stopping) Time.timeScale = 1f;
    }

    // Called by anything the player damages. 'at' is roughly the middle of the target.
    public static void Hit(Vector3 at, int damage)
    {
        if (Instance != null) Instance.DoHit(at, damage);
    }

    void DoHit(Vector3 at, int damage)
    {
        sparks.transform.position = at;
        sparks.Emit(Mathf.Clamp(6 + damage, 8, 24));
        if (!stopping && Time.unscaledTime - lastStop > stopCooldown)
        {
            lastStop = Time.unscaledTime;
            StartCoroutine(Stop(Mathf.Clamp(damage * stopPerDamage, stopRange.x, stopRange.y)));
            var cam = Camera.main != null ? Camera.main.GetComponent<QuarterViewCamera>() : null;
            if (cam != null) cam.Shake(0.08f + damage * 0.004f, 0.12f);
        }
    }

    // Freezes the game for a few frames, then lets it run on.
    IEnumerator Stop(float time)
    {
        if (Time.timeScale < 0.5f) yield break;   // already paused or slowed by something else
        stopping = true;
        float before = Time.timeScale;
        Time.timeScale = 0.02f;
        yield return new WaitForSecondsRealtime(time);
        Time.timeScale = before;
        stopping = false;
    }

    void Update()
    {
        for (int i = numbers.Count - 1; i >= 0; i--)
        {
            var n = numbers[i];
            n.t += Time.unscaledDeltaTime;
            if (n.t >= numberLifetime) numbers.RemoveAt(i);
            else numbers[i] = n;
        }
    }

    void OnGUI()
    {
        var cam = Camera.main;
        if (cam == null || numbers.Count == 0) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        foreach (var n in numbers)
        {
            float k = n.t / numberLifetime;
            Vector3 s = cam.WorldToScreenPoint(n.pos + Vector3.up * k * 1.2f);
            if (s.z <= 0f) continue;
            // pops in large, settles, then fades while rising
            float pop = k < 0.12f ? Mathf.Lerp(1.6f, 1f, k / 0.12f) : 1f;
            style.fontSize = Mathf.RoundToInt(n.size * pop);
            float alpha = 1f - Mathf.Clamp01((k - 0.6f) / 0.4f);
            var r = new Rect(s.x - 60f, Screen.height - s.y - 20f, 120f, 40f);
            style.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.8f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), n.text, style);
            style.normal.textColor = new Color(1f, 0.92f, 0.55f, alpha);
            GUI.Label(r, n.text, style);
        }
    }

    ParticleSystem BuildSparks()
    {
        var go = new GameObject("HitSparks");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.useUnscaledTime = true;   // sparks fly out during the hit-stop freeze
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startColor = sparkColor;
        main.gravityModifier = 1.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.06f;
        r.lengthScale = 1.5f;
        if (sparkMaterial != null) r.sharedMaterial = sparkMaterial;
        return ps;
    }
}
