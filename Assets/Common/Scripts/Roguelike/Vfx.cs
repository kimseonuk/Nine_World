using System.Collections.Generic;
using UnityEngine;

// Skill and projectile effects: glowing sprites, ground decals, sword slashes, spark bursts, lightning and trails.
// Textures come from Resources/Vfx (painted in VARCO on black, drawn additively so the black disappears);
// any that are missing are drawn procedurally so every effect still works.
public static class Vfx
{
    private static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    private static Mesh quad;

    // ───────────── One-shot effects ─────────────

    // A crescent of light along a sword's path. angle tilts the slash plane (0 = level sweep, 90 = overhead chop);
    // sweep is how far it turns as it flashes (sign = swing direction).
    public static void Slash(Vector3 center, Vector3 forward, float size, Color color, float angle = 0f, float sweep = 50f, float seconds = 0.24f)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        // The slash lies in a plane through the blade's path: level for a sweep, standing for a chop
        Quaternion plane = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, 0f, angle) * Quaternion.Euler(90f, 0f, 0f);
        // A wide coloured crescent with a thinner white-hot core
        Piece("slash", additive: true, center, plane, size, color * 2.6f, seconds, 0.85f, 1.15f, sweep, spinAroundNormal: true);
        Piece("slash", additive: true, center, plane, size * 0.82f, Color.Lerp(color, Color.white, 0.75f) * 2.2f, seconds * 0.8f, 0.85f, 1.1f, sweep, spinAroundNormal: true);
    }

    // A ring of force racing out across the floor
    public static void GroundRing(Vector3 center, float radius, Color color, float seconds = 0.45f, string texture = "ring")
    {
        var at = new Vector3(center.x, center.y + 0.17f, center.z);
        Piece(texture, additive: true, at, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f), radius * 2.2f, color * 1.5f, seconds, 0.15f, 1f, 30f, spinAroundNormal: true);
    }

    // A quick star of light where something lands
    public static void Flash(Vector3 at, float size, Color color, float seconds = 0.16f)
    {
        Piece("flare", additive: true, at, Quaternion.identity, size, color * 1.6f, seconds, 0.5f, 1.2f, Random.Range(-90f, 90f), billboard: true);
        Piece("glow", additive: true, at, Quaternion.identity, size * 0.9f, color, seconds * 1.3f, 0.6f, 1.3f, 0f, billboard: true);
    }

    // Sparks flying out from a hit, stretched along their flight
    public static void Burst(Vector3 at, Color color, int count = 14, float speed = 6f, float size = 0.18f, float gravity = 1.2f)
    {
        var ps = NewSystem("Sparks", at, "spark", additive: true, out ParticleSystemRenderer renderer);
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.06f;
        renderer.lengthScale = 1.5f;
        var main = ps.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startColor = color * 1.8f;
        main.gravityModifier = gravity;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        Emit(ps, count);
    }

    // A puff of dust kicked up from the floor
    public static void Dust(Vector3 at, float radius = 1.5f, int count = 10, Color? tint = null)
    {
        var ps = NewSystem("Dust", new Vector3(at.x, at.y + 0.2f, at.z), "smoke", additive: false, out _);
        var main = ps.main;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.2f, radius * 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = tint ?? new Color(0.85f, 0.8f, 0.72f, 0.55f);
        main.gravityModifier = -0.05f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius * 0.3f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.15f;
        limit.limit = 0.5f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
        Emit(ps, count);
    }

    // A crackling bolt of lightning between two points
    public static void Bolt(Vector3 from, Vector3 to, Color color, float width = 0.35f, float seconds = 0.2f)
    {
        var go = new GameObject("VfxBolt");
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = Material("bolt", additive: true);
        line.textureMode = LineTextureMode.Stretch;
        // The painted bolt fills about a third of its strip, so the strip is drawn wider than the bolt
        line.widthMultiplier = width * 3f;
        line.startColor = line.endColor = color * 2.4f;
        go.AddComponent<VfxBolt>().Init(line, from, to, seconds);
        Flash(to, width * 4f, color);
    }

    // ───────────── Lasting pieces ─────────────

    // A glowing sprite that stays (an orb's light, a spear tip); destroyed with its parent. spin turns it on screen.
    public static GameObject Glow(Transform parent, Vector3 local, float size, Color color, string texture = "glow", float spin = 0f)
    {
        var go = Piece(texture, additive: true, parent.position, Quaternion.identity, size, color * 1.5f, 0f, 1f, 1f, spin, billboard: true);
        go.transform.SetParent(parent, true);
        go.transform.localPosition = local;
        return go;
    }

    // A glowing disc lying on the floor (the charge's rune circle); resize and recolour it through the returned piece
    public static VfxPiece Decal(Vector3 at, float size, Color color, string texture, float spin = 40f)
    {
        var go = Piece(texture, additive: true, at, Quaternion.Euler(90f, 0f, 0f), size, color * 1.5f, 0f, 1f, 1f, spin, spinAroundNormal: true);
        return go.GetComponent<VfxPiece>();
    }

    // A streak of light behind a moving object
    public static TrailRenderer Trail(GameObject go, Color color, float width, float time = 0.25f, string texture = "trail")
    {
        var trail = go.AddComponent<TrailRenderer>();
        trail.sharedMaterial = Material(texture, additive: true);
        trail.textureMode = LineTextureMode.Stretch;
        trail.time = time;
        trail.startWidth = width;
        trail.endWidth = 0f;
        trail.startColor = color * 1.5f;
        trail.endColor = new Color(color.r, color.g, color.b, 0f);
        trail.numCapVertices = 2;
        // (a light streak casts no shadow: with the low sun a trail's shadow lay across the floor as a long dark band)
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        return trail;
    }

    // Sparkles shed along a path while the object moves (an orb, a sword wave)
    public static ParticleSystem Sparkle(GameObject go, Color color, float rateOverDistance = 6f, float size = 0.18f)
    {
        var ps = NewSystem("Sparkle", go.transform.position, "glow", additive: true, out _);
        ps.transform.SetParent(go.transform, true);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.stopAction = ParticleSystemStopAction.None;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = color * 1.6f;
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverDistance = rateOverDistance;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;
        ps.Play();
        return ps;
    }

    // Flames and embers pouring from a point along a direction for a while (fire breath)
    public static ParticleSystem Fire(Vector3 at, Vector3 direction, float reach, float halfAngle, float seconds)
    {
        var ps = NewSystem("Fire", at, "flame", additive: true, out ParticleSystemRenderer renderer);
        ps.transform.rotation = Quaternion.LookRotation(direction);
        var main = ps.main;
        main.duration = seconds;
        main.startLifetime = 0.55f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(reach / 0.6f * 0.8f, reach / 0.6f * 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), new Color(1f, 0.4f, 0.1f));
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 70f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = halfAngle;
        shape.radius = 0.3f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
        ps.Play();
        return ps;
    }

    // Motes of light streaming in from all around to a point for a while (power being gathered)
    public static ParticleSystem Gather(Vector3 at, float radius, Color color, float seconds, float rate = 60f)
    {
        var ps = NewSystem("Gather", at, "spark", additive: true, out ParticleSystemRenderer renderer);
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.08f;
        renderer.lengthScale = 1f;
        const float life = 0.55f;
        var main = ps.main;
        main.duration = seconds;
        main.startLifetime = life;
        // Born on a sphere and flying at its centre, arriving as they fade
        main.startSpeed = -radius / life;
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = color * 2f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius;
        shape.radiusThickness = 0f;
        var fade = ps.colorOverLifetime;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0.2f, 1f) });
        fade.color = gradient;
        ps.Play();
        return ps;
    }

    // A lasting aura on an awakened boss: a turning rune circle at its feet, a glow about its body,
    // embers rising off it and a light of its colour. Lives as long as the boss does.
    public static GameObject Aura(Transform owner, float height, Color color)
    {
        var root = new GameObject("Vfx_Aura");
        root.transform.SetParent(owner, false);
        // Sizes are in world units, whatever size the boss has grown to
        root.transform.localScale = Vector3.one / Mathf.Max(0.01f, owner.lossyScale.x);
        var circle = Decal(owner.position + Vector3.up * 0.05f, height * 1.4f, color * 0.9f, "runes", 50f);
        circle.transform.SetParent(root.transform, true);
        Glow(root.transform, Vector3.up * height * 0.55f, height * 1.3f, color * 0.35f);

        var ps = NewSystem("Embers", owner.position + Vector3.up * height * 0.3f, "glow", additive: true, out _);
        ps.transform.SetParent(root.transform, true);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.stopAction = ParticleSystemStopAction.None;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor = color * 2f;
        main.gravityModifier = -0.25f;
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 24f;
        var shape = ps.shape;
        // Off a ring round its body, drifting outwards and rising
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = height * 0.3f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        ps.Play();

        var lightGo = new GameObject("AuraLight");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = Vector3.up * height * 0.6f;
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = height * 3f;
        light.intensity = 2.5f;
        return root;
    }

    // Any one-shot sprite: facing the camera, or lying at the given rotation (turning about its own normal);
    // grows from startScale to endScale of its size and fades over its life. Add .Sheet() to play a flipbook.
    public static VfxPiece Sprite(string texture, Vector3 at, float size, Color color, float seconds, float startScale, float endScale,
        float spin, Quaternion? lying = null, bool additive = true)
    {
        var go = Piece(texture, additive, at, lying ?? Quaternion.identity, size, color, seconds, startScale, endScale, spin,
            billboard: !lying.HasValue, spinAroundNormal: lying.HasValue);
        return go.GetComponent<VfxPiece>();
    }

    // ───────────── Plumbing ─────────────

    private static GameObject Piece(string texture, bool additive, Vector3 at, Quaternion rotation, float size, Color color,
        float seconds, float startScale, float endScale, float spin, bool billboard = false, bool spinAroundNormal = false)
    {
        var go = new GameObject("Vfx_" + texture);
        go.transform.SetPositionAndRotation(at, rotation);
        go.AddComponent<MeshFilter>().sharedMesh = Quad;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Material(texture, additive);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        go.AddComponent<VfxPiece>().Init(renderer, color, size, seconds, startScale, endScale, spin, billboard, spinAroundNormal);
        return go;
    }

    private static ParticleSystem NewSystem(string name, Vector3 at, string texture, bool additive, out ParticleSystemRenderer renderer)
    {
        var go = new GameObject("Vfx_" + name);
        go.transform.position = at;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.maxParticles = 200;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        // Fade out over each particle's life
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = Material(texture, additive);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return ps;
    }

    private static void Emit(ParticleSystem ps, int count)
    {
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        ps.Play();
    }

    public static Mesh QuadMesh => Quad;

    // A square in the XY plane facing -Z, white vertex colours (the particle shader multiplies by them)
    private static Mesh Quad
    {
        get
        {
            if (quad != null)
                return quad;
            quad = new Mesh
            {
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) },
                uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
                colors = new[] { Color.white, Color.white, Color.white, Color.white },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
            };
            quad.RecalculateNormals();
            return quad;
        }
    }

    // Unlit particle material: additive (glows, black vanishes) or alpha-faded (smoke), two-sided, no depth writes
    public static Material Material(string texture, bool additive)
    {
        string key = (additive ? "add_" : "fade_") + texture;
        if (materials.TryGetValue(key, out Material cached) && cached != null)
            return cached;
        var mat = Resources.Load<Material>("Vfx/" + key);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = key };
            mat.SetTexture("_BaseMap", Texture(texture));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        materials[key] = mat;
        return mat;
    }

    // A painted effect texture (Resources/Vfx) when the project has it, otherwise the given stand-in's name - so a move
    // can ask for its own art and still look right before that art is in (the spear's and Ullr's effects use it)
    private static readonly Dictionary<string, bool> present = new Dictionary<string, bool>();

    public static string Pick(string preferred, string fallback)
    {
        if (!present.TryGetValue(preferred, out bool has))
            present[preferred] = has = Resources.Load<Texture2D>("Vfx/" + preferred) != null;
        return has ? preferred : fallback;
    }

    private static Texture2D Texture(string name)
    {
        if (textures.TryGetValue(name, out Texture2D cached) && cached != null)
            return cached;
        Texture2D tex = Resources.Load<Texture2D>("Vfx/" + name) ?? Procedural(name);
        textures[name] = tex;
        return tex;
    }

    // Stand-ins drawn in code, white on black (or white with alpha for smoke); additive ones keep alpha 1 so SrcAlpha doesn't dim them twice
    private static Texture2D Procedural(string name)
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "proc_" + name, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        var rng = new System.Random(name.GetHashCode());
        float[] jag = new float[n];
        for (int i = 0, y = n / 2; i < n; i++) { y += rng.Next(-4, 5); y = Mathf.Clamp(y, n / 4, n * 3 / 4); jag[i] = y; }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = 0f;
                switch (name)
                {
                    case "glow": a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f); break;
                    case "flare":
                        a = Mathf.Pow(Mathf.Clamp01(1f - r), 3f) + Mathf.Clamp01(1f - r) * (Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u) * 12f), 2f) + Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v) * 12f), 2f)) * 0.8f;
                        break;
                    case "ring": a = Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.07f, 2f)) + Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.2f, 2f)) * 0.3f; break;
                    case "ripple": a = Mathf.Exp(-Mathf.Pow((r - 0.85f) / 0.05f, 2f)) + Mathf.Exp(-Mathf.Pow((r - 0.65f) / 0.04f, 2f)) * 0.5f + Mathf.Exp(-Mathf.Pow((r - 0.45f) / 0.03f, 2f)) * 0.25f; break;
                    case "runes":
                        a = Mathf.Exp(-Mathf.Pow((r - 0.85f) / 0.035f, 2f)) + Mathf.Exp(-Mathf.Pow((r - 0.62f) / 0.03f, 2f));
                        float ang = Mathf.Atan2(v, u) * 12f / Mathf.PI;
                        if (r > 0.66f && r < 0.81f && Mathf.Abs(ang - Mathf.Round(ang)) < 0.12f) a += 0.8f;
                        break;
                    case "spark": a = Mathf.Exp(-v * v * 120f) * Mathf.Clamp01(1f - Mathf.Abs(u)); break;
                    case "trail": a = Mathf.Exp(-v * v * 6f) * (u * 0.5f + 0.5f); break;
                    // An even band of light along its length, soft across it (beams, walls of sound)
                    case "beam": a = Mathf.Exp(-v * v * 7f) * 0.7f + Mathf.Exp(-v * v * 60f) * 0.6f; break;
                    case "bolt": a = Mathf.Exp(-Mathf.Pow((y - jag[x]) / 2.5f, 2f)) + Mathf.Exp(-Mathf.Pow((y - jag[x]) / 9f, 2f)) * 0.35f; break;
                    case "slash":
                    {
                        // A crescent bowing toward +v: inside a circle, outside one shifted up, brightest along its front edge
                        float outer = Mathf.Sqrt(u * u + (v + 0.35f) * (v + 0.35f));
                        float inner = Mathf.Sqrt(u * u + (v + 0.75f) * (v + 0.75f));
                        float band = Mathf.Clamp01((inner - 1.05f) / 0.25f) * Mathf.Clamp01((1.3f - outer) / 0.12f);
                        a = band * Mathf.Clamp01(1f - Mathf.Abs(u) * 0.9f) * (0.5f + 0.5f * Mathf.Clamp01((outer - 1f) / 0.3f));
                        break;
                    }
                    case "feather": a = Mathf.Exp(-u * u * 30f) * Mathf.Clamp01(1f - Mathf.Abs(v)) + Mathf.Exp(-(u * u * 4f + v * v * 0.6f)) * 0.4f; break;
                    // A pointed leaf, solid inside (alpha-faded: tinted by the particle colour)
                    case "leaf": a = Mathf.Clamp01((1f - (u * u / 0.16f + v * v / 0.85f)) * 5f) * Mathf.Clamp01((0.95f - v) * 6f); break;
                    // A soft round flake (snow, ash)
                    case "flake": a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.2f); break;
                    // A very soft, edgeless puff (fog banks, blown snow, spray)
                    case "fog": a = Mathf.Exp(-r * r * 4f) * Mathf.Clamp01((1f - r) * 3f) * (0.75f + 0.25f * Mathf.PerlinNoise(x * 0.05f, y * 0.05f)); break;
                    case "flame":
                    case "smoke":
                    {
                        float noise = Mathf.PerlinNoise(x * 0.06f + 3f, y * 0.06f + 7f);
                        a = Mathf.Clamp01(1f - r * (0.9f + noise * 0.6f));
                        a = Mathf.Pow(a, 1.5f);
                        break;
                    }
                    default: a = Mathf.Clamp01(1f - r); break;
                }
                a = Mathf.Clamp01(a);
                px[y * n + x] = name == "smoke" || name == "leaf" || name == "flake" || name == "fog" ? new Color(1f, 1f, 1f, a) : new Color(a, a, a, 1f);
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }
}

// One animated sprite: grows, turns and fades over its life (or stays, when life is 0)
public class VfxPiece : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
    private Renderer rend;
    private int sheetCols, sheetRows;   // a flipbook: its frames play once over the piece's life (looping 12 a second if it stays)
    private Mesh sheetMesh;
    private Vector2[] baseUv, frameUv;
    private int shownFrame = -1;
    private float openTime;             // a lasting piece opening out from small over this long
    private MaterialPropertyBlock block;
    private Color color;
    private float size, life, startScale, endScale, spin, t;
    private bool billboard, spinAroundNormal;
    private Quaternion baseRotation;
    private float alpha = 1f;
    private bool hold;

    public void Init(Renderer r, Color c, float sz, float seconds, float s0, float s1, float spinDegrees, bool faceCamera, bool aroundNormal)
    {
        rend = r;
        color = c;
        size = sz;
        life = seconds;
        startScale = s0;
        endScale = s1;
        spin = spinDegrees;
        billboard = faceCamera;
        spinAroundNormal = aroundNormal;
        baseRotation = transform.rotation;
        block = new MaterialPropertyBlock();
        Apply(0f);
    }

    // Stays at full strength for most of its life instead of fading all the way through (for flipbooks whose own frames
    // already fade out): in over the first 8%, out over the last 25%
    public VfxPiece Hold()
    {
        hold = true;
        return this;
    }

    // Grows out from a fifth of its size over this long as it appears
    public VfxPiece OpenUp(float seconds)
    {
        openTime = seconds;
        return this;
    }

    // Plays the texture as a grid of frames, left to right and top to bottom
    // (URP's particle shaders ignore the texture's tiling, so the frame is cut out with the piece's own mesh UVs)
    public VfxPiece Sheet(int cols, int rows)
    {
        sheetCols = Mathf.Max(1, cols);
        sheetRows = Mathf.Max(1, rows);
        var filter = GetComponent<MeshFilter>();
        if (filter != null && sheetMesh == null)
        {
            sheetMesh = Instantiate(filter.sharedMesh);
            baseUv = sheetMesh.uv;
            frameUv = new Vector2[baseUv.Length];
            filter.sharedMesh = sheetMesh;
        }
        shownFrame = -1;
        Apply(life > 0f ? Mathf.Clamp01(t / life) : 0f);
        return this;
    }

    // For lasting pieces: change colour, size or brightness while they live
    public void Set(Color c, float sz, float brightness = 1f)
    {
        color = c;
        size = sz;
        alpha = brightness;
    }

    private void LateUpdate()
    {
        t += Time.deltaTime;
        // Unset after a script reload mid-play: nothing left to drive, so just go
        if (life > 0f && t >= life || rend == null || block == null)
        {
            Destroy(gameObject);
            return;
        }
        Apply(life > 0f ? t / life : 0f);
    }

    private void OnDestroy()
    {
        if (sheetMesh != null)
            Destroy(sheetMesh);
    }

    private void Apply(float k)
    {
        float ease = 1f - (1f - k) * (1f - k);
        float scale = Mathf.Lerp(startScale, endScale, ease) * size;
        if (openTime > 0f && t < openTime)
        {
            float o = t / openTime;
            scale *= Mathf.Lerp(0.2f, 1f, 1f - (1f - o) * (1f - o));
        }
        transform.localScale = Vector3.one * scale;

        // In fast, out slow
        float a = life <= 0f ? 1f
            : hold ? (k < 0.08f ? k / 0.08f : k > 0.75f ? 1f - (k - 0.75f) / 0.25f : 1f)
            : (k < 0.15f ? k / 0.15f : 1f - (k - 0.15f) / 0.85f);
        a *= alpha;
        Color c = color;
        c.a = Mathf.Clamp01(color.a) * a;
        block.SetColor(ColorId, c);
        if (sheetCols > 0)
        {
            int frames = sheetCols * sheetRows;
            int f = life > 0f ? Mathf.Min(frames - 1, Mathf.FloorToInt(k * frames)) : Mathf.FloorToInt(t * 12f) % frames;
            if (f != shownFrame && sheetMesh != null)
            {
                shownFrame = f;
                int col = f % sheetCols, row = f / sheetCols;
                float w = 1f / sheetCols, h = 1f / sheetRows, x = col * w, y = 1f - (row + 1f) * h;
                for (int i = 0; i < baseUv.Length; i++)
                    frameUv[i] = new Vector2(x + baseUv[i].x * w, y + baseUv[i].y * h);
                sheetMesh.uv = frameUv;
            }
        }
        rend.SetPropertyBlock(block);

        float turn = life > 0f ? spin * (ease - 0.5f) : spin * t;
        if (billboard && Camera.main != null)
            transform.rotation = Camera.main.transform.rotation * Quaternion.Euler(0f, 0f, turn);
        else if (spinAroundNormal)
            transform.rotation = baseRotation * Quaternion.Euler(0f, 0f, turn);
    }
}

// A lightning bolt that re-jags itself every frame until it fades
public class VfxBolt : MonoBehaviour
{
    private LineRenderer line;
    private Vector3 from, to;
    private float seconds, t;
    private Color color;

    public void Init(LineRenderer l, Vector3 a, Vector3 b, float duration)
    {
        line = l;
        from = a;
        to = b;
        seconds = duration;
        color = l.startColor;
        line.positionCount = 9;
        Jag();
    }

    private void Update()
    {
        t += Time.deltaTime;
        if (t >= seconds)
        {
            Destroy(gameObject);
            return;
        }
        Jag();
        Color c = color;
        c.a = 1f - t / seconds;
        line.startColor = line.endColor = c;
    }

    private void Jag()
    {
        Vector3 side = Vector3.Cross((to - from).normalized, Vector3.up);
        for (int i = 0; i < line.positionCount; i++)
        {
            float k = i / (line.positionCount - 1f);
            Vector3 p = Vector3.Lerp(from, to, k);
            if (i > 0 && i < line.positionCount - 1)
                p += side * Random.Range(-0.35f, 0.35f) + Vector3.up * Random.Range(-0.25f, 0.25f);
            line.SetPosition(i, p);
        }
    }
}
