using System.Collections.Generic;
using UnityEngine;

// The cast and sets made in VARCO for the story scenes (Resources/Story/Story_<name>): animated actors (Odin, Thor,
// the frost and fire giants, the blight wraith, the corrupted Heimdall) and set pieces (the world tree whole and
// rotten, Asgard's ruin, the Niflheim altar, a frozen arch, the volcano). Low-poly (5k-14k tris each).
// Actors' animator states: Odin Idle/LookUp/KneelFear/ArmSpread · Thor Idle/Smash/Threat · FrostGiant Walk/Roar/Smash ·
// FireGiant Walk/Rise/Stomp · Wraith Walk/Growl/Threat · HeimdallCorrupt Idle/Talk/Explain/Pain/Stomp/Horn/Threat.
public static class StoryStage
{
    private static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();

    public static GameObject Load(string name)
    {
        if (!cache.TryGetValue(name, out GameObject go) || go == null)
        {
            go = Resources.Load<GameObject>("Story/Story_" + name);
            cache[name] = go;
        }
        return go;
    }

    public static bool Has(string name) => Load(name) != null;

    // An actor standing `height` metres tall at a world position, facing yaw, playing a state
    public static Transform Actor(Transform parent, string name, Vector3 world, float yaw, float height, string state = null)
    {
        GameObject prefab = Load(name);
        if (prefab == null) return null;
        GameObject go = Object.Instantiate(prefab, parent);
        go.name = name;
        go.transform.position = world;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation;
        // (a humanoid stands with its feet at its pivot; its skinned bounds are padded, so don't ground by them)
        Fit(go.transform, height, true, false);
        var animator = go.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            if (state != null) animator.Play(state, 0, Random.Range(0f, 0.2f));
        }
        foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            r.updateWhenOffscreen = true;
        return go.transform;
    }

    // A set piece whose largest side (or height, byHeight) is `size` metres
    public static Transform Piece(Transform parent, string name, Vector3 world, float yaw, float size, bool byHeight = false)
    {
        GameObject prefab = Load(name);
        if (prefab == null) return null;
        GameObject go = Object.Instantiate(prefab, parent);
        go.name = name;
        go.transform.position = world;
        // (VARCO props carry their mesh on a root turned 270° about X: keep that, add the yaw)
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation;
        Fit(go.transform, size, byHeight);
        return go.transform;
    }

    // Change what an actor is doing, blending over `fade` seconds
    public static void Act(Transform actor, string state, float fade = 0.25f, float from = 0f)
    {
        if (actor == null) return;
        var animator = actor.GetComponentInChildren<Animator>();
        if (animator == null || !animator.HasState(0, Animator.StringToHash(state))) return;
        animator.CrossFadeInFixedTime(state, fade, 0, from);
    }

    public static bool HasState(Transform actor, string state)
    {
        var animator = actor != null ? actor.GetComponentInChildren<Animator>() : null;
        return animator != null && animator.HasState(0, Animator.StringToHash(state));
    }

    // True while a staged story scene (not the opening) holds the screen: the game's HUD stays hidden
    public static bool Cinematic { get => cinematic || TitleScreen.Playing; set => cinematic = value; }
    private static bool cinematic;

    public static void Speed(Transform actor, float speed)
    {
        if (actor == null) return;
        var animator = actor.GetComponentInChildren<Animator>();
        if (animator != null) animator.speed = speed;
    }

    // Where an actor's head is (the humanoid head bone, or high on its bounds)
    public static Vector3 Head(Transform actor)
    {
        if (actor == null) return Vector3.zero;
        var animator = actor.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            if (head != null) return head.position;
        }
        Bounds b = Bounds(actor);
        return new Vector3(b.center.x, b.min.y + b.size.y * 0.85f, b.center.z);
    }

    public static Bounds Bounds(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(t.position, Vector3.one);
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers)
            if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
        return b;
    }

    // Tint every material (a darker, colder or burning look) — instanced, so the prefab is untouched
    public static void Tint(Transform t, Color color, Color emission = default)
    {
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            foreach (var m in r.materials)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                if (emission != default && m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", emission);
                    if (m.HasProperty("_EmissionMap") && m.HasProperty("_BaseMap")) m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
                }
            }
        }
    }

    private static void Fit(Transform t, float size, bool byHeight, bool ground = true)
    {
        t.localScale = Vector3.one;
        Bounds b = Bounds(t);
        float measure = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (measure > 0.001f) t.localScale = Vector3.one * (size / measure);
        if (!ground) return;
        // stand it on its base
        b = Bounds(t);
        t.position += Vector3.up * (t.position.y - b.min.y);
    }
}
