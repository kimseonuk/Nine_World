using System.Collections.Generic;
using UnityEngine;

// The living scenery: water that runs, flames that burn, cloth and leaves that stir in the wind, dust and light
// drifting through every zone, birds wheeling over the village, crystals floating.
// All of it is decoration: it has no gameplay and lives and dies with the zone it was built into.
// The models it adds (VARCO) are loaded from Resources/Ambient; anything missing is simply left out.
public static class Ambient
{
    private static readonly Color[] Rainbow =
    {
        new Color(1f, 0.3f, 0.3f), new Color(1f, 0.65f, 0.25f), new Color(1f, 0.95f, 0.35f),
        new Color(0.35f, 1f, 0.5f), new Color(0.35f, 0.65f, 1f), new Color(0.75f, 0.4f, 1f),
    };

    private static readonly Dictionary<string, GameObject> models = new Dictionary<string, GameObject>();

    // A model from Resources/Ambient, or null when it isn't in the project
    public static GameObject Model(string name)
    {
        if (!models.TryGetValue(name, out GameObject model))
            models[name] = model = Resources.Load<GameObject>("Ambient/" + name);
        return model;
    }

    // ????????????? Whole zones ?????????????

    // Brings a zone's existing scenery to life, picked out by name: trees and banners sway, fires burn,
    // the fountain runs, crystals glint. Called once the zone is fully built.
    // Props out in the haze past the edge still sway, but only those near the play area get their own particles.
    public static void Animate(Transform zone, int style, bool palace, Vector2 size)
    {
        var pulsed = new HashSet<Material>();
        bool Near(Transform t)
        {
            Vector3 p = zone.InverseTransformPoint(t.position);
            return Mathf.Abs(p.x) < size.x / 2f + 3f && Mathf.Abs(p.z) < size.y / 2f + 3f;
        }
        foreach (Transform child in zone.GetComponentsInChildren<Transform>(true))
        {
            string n = child.name;
            if (n.StartsWith("GoldenTree"))
                Tree(child, 1f, Near(child) ? new Color(1f, 0.82f, 0.35f) : Color.clear, true);
            else if (n.StartsWith("Banner"))
            {
                if (WindSway.Add(child.gameObject, WindSway.Kind.Banner) == null)
                    child.gameObject.AddComponent<TiltSway>().Init(1.5f, 1.3f);
            }
            else if (n.StartsWith("BifrostFountain"))
                Fountain(child);
            else if (n.StartsWith("HealPool") || n.StartsWith("BazaarFont"))
                GameAudio.Loop(child.gameObject, "fountain_loop", 0.5f, 12f);
            else if (n.StartsWith("BifrostObelisk"))
                ObeliskShards(child);
            else if (n.StartsWith("Candelabra"))
                CandleFlames(child);
            else if (n.StartsWith("Brazier"))
                Flame(child.parent, child.localPosition + Vector3.up * 1.0f * child.localScale.y, 1f);
            else if (n.StartsWith("TreasureChest"))
                Glints(child, new Color(1f, 0.85f, 0.4f));
            else if (n.StartsWith("BifrostGate"))
                GateShimmer(child);
            else if (n.StartsWith("Himinbjorg"))
                Beacon(child);
            // (Midgard's smithy building only: the upgrade room's Forge* props have no chimney)
            else if (n == "Forge" || n.StartsWith("Forge(") || n.StartsWith("Longhouse"))
                Chimney(child, n.StartsWith("Forge"));
            else if (n == "Seal" || n == "Path")
                Pulse(child.GetComponent<Renderer>(), pulsed, n == "Seal" ? 2.2f : 0.9f, n == "Seal" ? 0.35f : 0.25f);
            else
            {
                var r = child.GetComponent<Renderer>();
                if (r == null || r.sharedMaterial == null)
                    continue;
                string m = r.sharedMaterial.name;
                if (m == "star_crystal" || m == "glint_gold" || m == "glint_cyan")
                    Pulse(r, pulsed, 1.6f, 0.5f);
            }
        }
    }

    // What drifts through the air of a zone: dust and light in Asgard; leaves, butterflies and ravens over the village
    public static void Weather(Transform zone, int style, bool palace, Vector2 size)
    {
        var area = new Vector3(size.x + 12f, 0f, size.y + 12f);
        float acres = area.x * area.z;
        if (palace)
        {
            Drift(zone, area, "Amb_GoldDust", "glow", true, new Color(1f, 0.85f, 0.5f) * 1.4f, acres / 60f, 0.05f, 0.11f, 0.3f, 4f, 7f, 10f);
            Drift(zone, area, "Amb_Prism", "flare", true, new Color(0.8f, 0.85f, 1f), acres / 220f, 0.12f, 0.22f, 0.6f, 3f, 5f, 6f, rainbow: true);
            // No ravens circling here: in Asgard the only ravens are Huginn and Muninn themselves
            return;
        }
        Falling(zone, area, "Amb_Leaves", "leaf", false, new Color(0.55f, 0.7f, 0.3f), new Color(0.9f, 0.6f, 0.2f), acres / 90f, 0.16f, 0.3f, 0.7f, 8f);
        Drift(zone, area, "Amb_Pollen", "glow", true, new Color(1f, 0.95f, 0.6f), acres / 70f, 0.04f, 0.08f, 0.2f, 2.5f, 5f, 8f);
        Butterflies(zone, size, 6);
        Flock(zone, size, 4);
    }

    // Landmarks that only exist to move: the wall fountains and rune lanterns of Asgard's halls.
    public static void Features(Room room, int style, bool palace, List<(Vector3 p, float r)> placed)
    {
        // Only Asgard's halls are left in the run
        if (palace)
            PalaceLife(room, placed);
    }

    // ───────────── Asgard's halls ─────────────

    // What moves in Odin's halls beyond the centrepieces: wolf-head fountains spilling into basins along the side
    // balustrades, rune lanterns hovering by the walls, a training quintain turning in the fight halls.
    // The arena gets lanterns round its rim.
    private static void PalaceLife(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        // (the mid-boss's open battleground is lit like the guardian's arena: lanterns round its rim)
        bool arena = room.RoomKind == Room.Kind.Boss || room.RoomKind == Room.Kind.MidBoss && room.Radius > 0f;
        bool fight = room.RoomKind == Room.Kind.Combat;

        if (arena)
        {
            // Lanterns hovering over the rim between the pillars, outside the fight
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f + 30f) * Mathf.Deg2Rad;
                RuneLantern(t, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (room.Radius + 1.4f), 3.4f, i);
            }
        }
        else
        {
            // Fountains against the east and west balustrades, facing in, in a gap between the balustrade's columns
            Physics.SyncTransforms();
            if (fight || room.RoomKind == Room.Kind.Start)
                foreach (float side in new[] { -1f, 1f })
                {
                    if (Random.value > (fight ? 0.8f : 0.5f))
                        continue;
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        var p = new Vector3(side * (hx - 1.1f), 0f, Random.Range(-hz * 0.6f, hz * 0.6f));
                        // (the box stops short of the zone's invisible edge wall, which it would otherwise always touch)
                        if (!Clear(placed, p, 2f) || Physics.CheckBox(t.TransformPoint(p + Vector3.up * 1.3f), new Vector3(0.55f, 0.9f, 1f), t.rotation, ~0, QueryTriggerInteraction.Ignore))
                            continue;
                        if (WallFountain(t, p, Quaternion.LookRotation(new Vector3(-side, 0f, 0f)).eulerAngles.y))
                            placed.Add((p, 2f));
                        break;
                    }
                }

            // Rune lanterns along the walls (none over the open south side, where they'd hang before the camera)
            int lanterns = fight ? 5 : 3;
            for (int i = 0; i < lanterns; i++)
            {
                Vector3 p = i % 3 == 0
                    ? new Vector3(Random.Range(-hx * 0.7f, hx * 0.7f), 0f, hz - 1.4f)
                    : new Vector3((i % 2 == 0 ? -1f : 1f) * (hx - 1.4f), 0f, Random.Range(-hz * 0.6f, hz * 0.8f));
                RuneLantern(t, p, Random.Range(2.6f, 3.2f), i);
            }

            // (the training quintain stays out of the fight halls: their floor is kept clear)
        }
    }

    private static bool Clear(List<(Vector3 p, float r)> placed, Vector3 p, float radius)
    {
        foreach (var o in placed)
            if (Vector3.Distance(o.p, p) < o.r + radius)
                return false;
        return true;
    }

    // A wolf's head spouting water in an arc into a marble basin, the basin's surface rippling and glinting
    public static bool WallFountain(Transform parent, Vector3 local, float yaw)
    {
        GameObject model = Model("WallFountain");
        if (model == null)
            return false;
        GameObject fountain = Object.Instantiate(model, parent);
        GameAudio.Loop(fountain, "fountain_loop", 0.25f, 8f);
        fountain.transform.localPosition = local;
        fountain.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Bounds b = WorldBounds(fountain.transform);
        Transform f = fountain.transform;
        float h = b.size.y, depth = Mathf.Min(b.size.x, b.size.z);
        // The spout (the wolf's mouth) and the basin, in the model's own frame: front is +z
        Vector3 back = f.position - f.forward * depth * 0.5f;
        Vector3 mouth = back + f.forward * depth * 0.42f + Vector3.up * h * 0.55f;
        Vector3 basin = back + f.forward * depth * 0.7f + Vector3.up * h * 0.28f;
        Color water = new Color(0.65f, 0.88f, 1f);

        var jet = Emitter(parent, parent.InverseTransformPoint(mouth), "Amb_WallJet", "flake", false, 160);
        jet.transform.rotation = Quaternion.LookRotation(f.forward);
        var main = jet.main;
        main.startLifetime = 0.55f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 1.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
        main.startColor = new Color(0.62f, 0.85f, 1f, 0.85f);   // blended, not added, so it shows on white marble
        main.gravityModifier = 1f;
        var emission = jet.emission;
        emission.rateOverTime = 90f;
        var shape = jet.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 4f;
        shape.radius = 0.05f;
        var jr = jet.GetComponent<ParticleSystemRenderer>();
        jr.renderMode = ParticleSystemRenderMode.Stretch;
        jr.velocityScale = 0.08f;
        jr.lengthScale = 1.2f;
        jet.Play();

        var splash = Emitter(parent, parent.InverseTransformPoint(basin), "Amb_WallSplash", "flake", false, 60);
        main = splash.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
        main.startColor = water;
        main.gravityModifier = 1f;
        emission = splash.emission;
        emission.rateOverTime = 35f;
        shape = splash.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.12f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        splash.Play();

        var rings = Emitter(parent, parent.InverseTransformPoint(basin + Vector3.up * 0.02f), "Amb_WallRipples", "ripple", true, 12);
        main = rings.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.4f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startColor = water * 0.6f;
        emission = rings.emission;
        emission.rateOverTime = 3f;
        shape = rings.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.15f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var grow = rings.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f));
        rings.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        rings.Play();
        return true;
    }

    // A crystal lantern floating by the wall: it bobs and turns, its light slowly running through the Bifrost's colours
    public static void RuneLantern(Transform parent, Vector3 local, float height, int index)
    {
        GameObject model = Model("RuneLantern");
        var holder = new GameObject("RuneLantern").transform;
        holder.SetParent(parent, false);
        holder.localPosition = local + Vector3.up * height;
        if (model != null)
        {
            GameObject lantern = Object.Instantiate(model, holder);
            lantern.transform.localScale *= 1.5f;
            foreach (Collider c in lantern.GetComponentsInChildren<Collider>())
                Object.Destroy(c);
        }
        holder.gameObject.AddComponent<Bob>().Init(0.22f, Random.Range(3f, 4.5f), Random.Range(15f, 30f) * (index % 2 == 0 ? 1f : -1f));
        var lampGo = new GameObject("LanternLight");
        lampGo.transform.SetParent(holder, false);
        var lamp = lampGo.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.range = 6f;
        lamp.intensity = 1.5f;
        lamp.shadows = LightShadows.None;
        GameObject glow = Vfx.Glow(holder, Vector3.zero, 1.4f, Color.white * 0.5f);
        holder.gameObject.AddComponent<RainbowCycle>().Init(lamp, glow.GetComponent<VfxPiece>(), index * 0.17f);
        var ps = Emitter(holder, Vector3.zero, "Motes", "glow", true, 20);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        main.startColor = RandomRainbow(1.8f);
        main.gravityModifier = 0.04f;
        var emission = ps.emission;
        emission.rateOverTime = 4f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;
        ps.Play();
    }

    // The einherjar's practice post: its beam swings round and back as if just struck, the sandbag swaying under it
    public static void Quintain(Transform parent, Vector3 local)
    {
        GameObject model = Model("Quintain");
        if (model == null)
            return;
        GameObject post = Object.Instantiate(model, parent);
        post.transform.localPosition = local;
        post.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        post.AddComponent<Swivel>().Init(Random.Range(35f, 60f), Random.Range(3.5f, 5f));
    }

    // ????????????? Particle plumbing ?????????????

    // A looping emitter, idle until configured and played. Simulated in world space; restarts when its zone is shown again.
    private static ParticleSystem Emitter(Transform parent, Vector3 local, string name, string texture, bool additive, int max = 200)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 4f;
        main.playOnAwake = true;
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        fade.color = FadeInOut(0.15f, 0.65f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = Vfx.Material(texture, additive);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    private static ParticleSystem.MinMaxGradient FadeInOut(float fadeIn, float holdTo, Color? end = null)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(end ?? Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, holdTo), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    private static ParticleSystem.MinMaxGradient RandomRainbow(float brightness)
    {
        var g = new Gradient();
        var keys = new GradientColorKey[Rainbow.Length];
        for (int i = 0; i < Rainbow.Length; i++)
            keys[i] = new GradientColorKey(Rainbow[i] * brightness, i / (Rainbow.Length - 1f));
        g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
    }

    private static void Box(ParticleSystem ps, Vector3 size)
    {
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = size;
    }

    private static void Noise(ParticleSystem ps, float strength, float frequency)
    {
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = strength;
        noise.frequency = frequency;
        noise.scrollSpeed = 0.2f;
    }

    // Motes hanging in the air, wandering slowly
    private static void Drift(Transform zone, Vector3 area, string name, string texture, bool additive, Color color, float rate,
        float minSize, float maxSize, float wander, float lifeMin, float lifeMax, float top, bool rainbow = false)
    {
        var ps = Emitter(zone, Vector3.up * top / 2f, name, texture, additive, 400);
        var main = ps.main;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = rainbow ? RandomRainbow(1.6f) : new ParticleSystem.MinMaxGradient(color);
        var emission = ps.emission;
        emission.rateOverTime = rate;
        Box(ps, new Vector3(area.x, top, area.z));
        Noise(ps, wander, 0.25f);
        ps.Play();
    }

    // The air of a themed fight hall (0 feast hall, 1 training yard, 2 observatory, 3 golden garden)
    public static void HallAir(Transform zone, Vector2 size, int theme)
    {
        var area = new Vector3(size.x, 0f, size.y);
        float acres = area.x * area.z;
        switch (theme)
        {
            case 0: // embers rising off the hearth in the middle
                Rising(zone, new Vector3(4.5f, 0f, 4.5f), "Amb_Hearth", "glow", true, new Color(1f, 0.5f, 0.15f) * 2.4f, 22f, 0.06f, 0.14f, 1.1f);
                break;
            case 1: // sand dust hanging low over the yard
                Drift(zone, area, "Amb_YardDust", "glow", true, new Color(0.4f, 0.32f, 0.2f), acres / 50f, 0.3f, 0.7f, 0.3f, 4f, 7f, 2f);
                break;
            case 2: // motes of starlight
                Drift(zone, area, "Amb_Starlight", "flare", true, new Color(0.7f, 0.8f, 1f) * 1.6f, acres / 45f, 0.06f, 0.14f, 0.2f, 3f, 6f, 7f);
                break;
            default: // golden leaves coming down off Idun's trees
                Falling(zone, area, "Amb_GoldLeaves", "leaf", false, new Color(1f, 0.82f, 0.3f), new Color(0.95f, 0.65f, 0.2f), acres / 70f, 0.16f, 0.3f, 0.7f, 8f);
                break;
        }
    }

    // Things coming down from above: leaves tumbling, snow, ash
    private static void Falling(Transform zone, Vector3 area, string name, string texture, bool additive, Color a, Color b, float rate,
        float minSize, float maxSize, float fallSpeed, float life)
    {
        var ps = Emitter(zone, Vector3.up * 8f, name, texture, additive, 600);
        var main = ps.main;
        main.prewarm = true;
        main.startLifetime = life;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        var emission = ps.emission;
        emission.rateOverTime = rate;
        Box(ps, new Vector3(area.x, 0.5f, area.z));
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        velocity.y = new ParticleSystem.MinMaxCurve(-fallSpeed * 1.2f, -fallSpeed * 0.8f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        var spin = ps.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
        Noise(ps, fallSpeed * 0.6f, 0.4f);
        // Gone at the ground: they live just long enough to fall the 8 m
        main.startLifetime = 8f / fallSpeed;
        ps.Play();
    }

    // Sparks and souls rising off the ground
    private static void Rising(Transform zone, Vector3 area, string name, string texture, bool additive, Color color, float rate,
        float minSize, float maxSize, float riseSpeed)
    {
        var ps = Emitter(zone, Vector3.up * 0.1f, name, texture, additive, 400);
        var main = ps.main;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = color;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        Box(ps, new Vector3(area.x, 0.2f, area.z));
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(riseSpeed * 0.6f, riseSpeed * 1.4f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        Noise(ps, 0.6f, 0.5f);
        ps.Play();
    }

    // ????????????? Fire ?????????????

    // A living flame: licking tongues, a flickering glow, embers drifting up and (optionally) a thread of smoke
    public static GameObject Flame(Transform parent, Vector3 local, float size, bool smoke = true, Color? tint = null)
    {
        var root = new GameObject("Amb_Flame").transform;
        root.SetParent(parent, false);
        root.localPosition = local;
        Color hot = tint ?? new Color(1f, 0.8f, 0.35f), deep = tint.HasValue ? tint.Value * 0.6f : new Color(1f, 0.42f, 0.1f);

        var tongues = Emitter(root, Vector3.zero, "Tongues", "flame", true, 80);
        var main = tongues.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f * size, 1.2f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f * size, 0.65f * size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(hot, deep);
        var emission = tongues.emission;
        emission.rateOverTime = 30f;
        var shape = tongues.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 10f;
        shape.radius = 0.15f * size;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var grow = tongues.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.15f)));
        var spin = tongues.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
        var tongueFade = tongues.colorOverLifetime;
        tongueFade.color = FadeInOut(0.1f, 0.5f, new Color(1f, 0.45f, 0.3f));
        tongues.Play();

        var embers = Emitter(root, Vector3.up * 0.2f * size, "Embers", "glow", true, 40);
        main = embers.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f * size, 1.4f * size);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        main.startColor = hot * 2f;
        emission = embers.emission;
        emission.rateOverTime = 5f * size;
        shape = embers.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.radius = 0.2f * size;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        Noise(embers, 0.6f, 0.8f);
        embers.Play();

        if (smoke)
        {
            var fume = Emitter(root, Vector3.up * 0.6f * size, "Smoke", "smoke", false, 30);
            main = fume.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f * size, 0.7f * size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.22f, 0.2f, 0.2f, 0.28f);
            emission = fume.emission;
            emission.rateOverTime = 3f;
            shape = fume.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            grow = fume.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.8f));
            fume.Play();
        }

        GameObject glow = Vfx.Glow(root, Vector3.up * 0.25f * size, 1.6f * size, deep * 0.55f);
        glow.AddComponent<GlowFlicker>();
        if (size >= 0.6f) GameAudio.Loop(root.gameObject, "fire_loop", 0.35f * Mathf.Min(size, 1.5f), 7f + 3f * size);   // its crackle
        return root.gameObject;
    }

    // Small flames on every candle of a candelabra: its highest points, found once per model
    private static readonly Dictionary<Mesh, List<Vector3>> candleTips = new Dictionary<Mesh, List<Vector3>>();

    private static void CandleFlames(Transform candelabra)
    {
        var mf = candelabra.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
            return;
        if (!candleTips.TryGetValue(mf.sharedMesh, out List<Vector3> tips))
            candleTips[mf.sharedMesh] = tips = FindTips(mf, candelabra);
        foreach (Vector3 tip in tips)
        {
            GameObject f = Flame(candelabra, tip + Vector3.up * 0.05f, 0.4f, false);
            f.transform.localScale = new Vector3(1f / candelabra.localScale.x, 1f / candelabra.localScale.y, 1f / candelabra.localScale.z);
        }
        if (tips.Count == 0)
            Flame(candelabra.parent, candelabra.localPosition + Vector3.up * 2.75f * candelabra.localScale.y, 0.4f, false);
    }

    // The tops of separate prongs: the highest vertices, grouped where they stand close together
    private static List<Vector3> FindTips(MeshFilter mf, Transform root)
    {
        var tips = new List<Vector3>();
        if (!mf.sharedMesh.isReadable)
            return tips;
        Matrix4x4 toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
        Vector3[] verts = mf.sharedMesh.vertices;
        float top = float.MinValue, bottom = float.MaxValue;
        var points = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            points[i] = toRoot.MultiplyPoint3x4(verts[i]);
            top = Mathf.Max(top, points[i].y);
            bottom = Mathf.Min(bottom, points[i].y);
        }
        float cut = top - (top - bottom) * 0.14f;
        var clusters = new List<Vector3>();
        foreach (Vector3 p in points)
        {
            if (p.y < cut)
                continue;
            int found = -1;
            for (int c = 0; c < clusters.Count; c++)
                if (new Vector2(clusters[c].x - p.x, clusters[c].z - p.z).sqrMagnitude < 0.06f) { found = c; break; }
            if (found < 0)
                clusters.Add(p);
            else if (p.y > clusters[found].y)
                clusters[found] = p;
        }
        // The tallest few prongs; a cluster that's lower than the rest is a decoration, not a candle
        clusters.Sort((a, b) => b.y.CompareTo(a.y));
        for (int i = 0; i < clusters.Count && i < 7; i++)
            if (clusters[i].y > top - (top - bottom) * 0.1f)
                tips.Add(clusters[i]);
        return tips;
    }

    // Smoke from a roof (a forge's thicker, with sparks)
    private static void Chimney(Transform building, bool forge)
    {
        Bounds b = WorldBounds(building);
        var ps = Emitter(building.parent, building.parent.InverseTransformPoint(new Vector3(b.center.x, b.max.y - 0.3f, b.center.z)), "Amb_Chimney", "smoke", false, 60);
        var main = ps.main;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = forge ? new Color(0.25f, 0.24f, 0.24f, 0.45f) : new Color(0.75f, 0.75f, 0.78f, 0.3f);
        var emission = ps.emission;
        emission.rateOverTime = forge ? 4f : 2f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 10f;
        shape.radius = 0.3f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 4f));
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        ps.Play();
        if (forge)
            Flame(building.parent, building.parent.InverseTransformPoint(new Vector3(b.center.x, b.max.y - 0.2f, b.center.z)), 0.5f, false);
    }

    // ????????????? Water ?????????????

    // The Bifrost fountain runs: arcs of water from under the eagle into the basin, splashes and spreading rings
    // where they land, a cool mist and rainbow motes rising off the pool, light rippling over its surface
    private static void Fountain(Transform fountain)
    {
        GameAudio.Loop(fountain.gameObject, "fountain_loop", 0.45f, 12f);
        Bounds b = WorldBounds(fountain);
        float h = b.size.y, w = Mathf.Min(b.size.x, b.size.z);
        Transform parent = fountain.parent;
        Vector3 Local(float y, float x = 0f, float z = 0f) => parent.InverseTransformPoint(new Vector3(b.center.x + x, b.min.y + y, b.center.z + z));
        float spoutY = h * 0.6f, poolY = h * 0.16f, poolR = w * 0.4f;
        Color water = new Color(0.65f, 0.88f, 1f);
        // The water itself is drawn blended, not added, so it shows against the white marble
        Color waterBody = new Color(0.62f, 0.85f, 1f, 0.85f);

        // Four arcs falling from the spout ring
        var jets = Emitter(parent, Local(spoutY), "Amb_FountainJets", "flake", false, 400);
        var main = jets.main;
        main.startLifetime = 0.95f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
        main.startColor = waterBody;
        main.gravityModifier = 1f;
        var emission = jets.emission;
        emission.rateOverTime = 160f;
        var shape = jets.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 28f;
        shape.radius = w * 0.06f;
        shape.radiusThickness = 0f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var jetFade = jets.colorOverLifetime;
        jetFade.color = FadeInOut(0.05f, 0.85f);
        var jr = jets.GetComponent<ParticleSystemRenderer>();
        jr.renderMode = ParticleSystemRenderMode.Stretch;
        jr.velocityScale = 0.07f;
        jr.lengthScale = 1.2f;
        jets.Play();

        // Splash where the arcs come down (about 1.3 m out), and rings spreading over the whole pool
        var splash = Emitter(parent, Local(poolY + 0.05f), "Amb_FountainSplash", "flake", false, 200);
        main = splash.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
        main.startColor = waterBody;
        main.gravityModifier = 1f;
        emission = splash.emission;
        emission.rateOverTime = 70f;
        shape = splash.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = w * 0.2f;
        shape.radiusThickness = 0.15f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        splash.Play();

        var rings = Emitter(parent, Local(poolY + 0.02f), "Amb_FountainRipples", "ripple", true, 40);
        main = rings.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startColor = water * 0.6f;
        emission = rings.emission;
        emission.rateOverTime = 7f;
        shape = rings.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = poolR * 0.85f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var grow = rings.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f));
        rings.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        rings.Play();

        var mist = Emitter(parent, Local(poolY + 0.2f), "Amb_FountainMist", "fog", false, 40);
        main = mist.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(1f, 1.8f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.9f, 0.96f, 1f, 0.16f);
        emission = mist.emission;
        emission.rateOverTime = 5f;
        shape = mist.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = w * 0.2f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        mist.Play();

        var motes = Emitter(parent, Local(poolY + 0.1f), "Amb_FountainRainbow", "glow", true, 60);
        main = motes.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
        main.startColor = RandomRainbow(1.8f);
        emission = motes.emission;
        emission.rateOverTime = 8f;
        shape = motes.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = poolR * 0.8f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        Noise(motes, 0.4f, 0.6f);
        motes.Play();

        // Light playing over the water
        Disc(parent, Local(poolY + 0.03f), poolR, "Amb_FountainCaustics", Caustics(), 1.6f);
    }

    // A mill pond: a stone-ringed pond (VARCO) with light rippling over its water and rings spreading where drops
    // fall, a wooden wheel turning at its east edge, and a raised wooden flume bringing water in from the north
    // (behind the wheel, as the camera sees it): the water runs down the trough and pours off its end onto the
    // paddles, splashing into the pond. Plain shapes stand in for any model that's missing.
    public static void MillPond(Transform parent, Vector3 local, float yaw)
    {
        var root = new GameObject("MillPond").transform;
        root.SetParent(parent, false);
        root.localPosition = local;
        root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Color water = new Color(0.7f, 0.9f, 1f);
        Color waterBody = new Color(0.62f, 0.85f, 1f, 0.85f);
        Material timber = Mats.Get("mill_timber", new Color(0.38f, 0.26f, 0.15f));

        // The pond: its water surface (height and reach) is marked in the model
        float waterY = 0.05f, radius = 2.4f;
        GameObject pondModel = Model("VillagePond");
        if (pondModel != null)
        {
            GameObject pond = Object.Instantiate(pondModel, root);
            Transform surface = pond.transform.Find("WaterSurface");
            if (surface != null)
            {
                waterY = surface.localPosition.y;
                radius = surface.localScale.x;
            }
        }
        else
            Disc(root, Vector3.up * waterY, radius, "Water", null, 1f, Mats.Get("mill_water", new Color(0.12f, 0.28f, 0.34f), 0.92f));
        Disc(root, Vector3.up * (waterY + 0.02f), radius * 0.92f, "Caustics", Caustics(), 1.2f, null, new Color(0.75f, 0.95f, 1f, 0.22f));

        // The wheel at the east edge, its axle along x, its lowest paddles in the water
        const float wheelRadius = 1.5f;
        Vector3 hub = new Vector3(radius + 0.2f, waterY + wheelRadius - 0.35f, 0f);
        GameObject wheelModel = Model("Waterwheel");
        if (wheelModel != null)
        {
            GameObject wheel = Object.Instantiate(wheelModel, root);
            wheel.transform.localPosition = hub;
            wheel.transform.localRotation = Quaternion.identity;
            foreach (Collider c in wheel.GetComponentsInChildren<Collider>())
                Object.Destroy(c);
            wheel.AddComponent<Spin>().Init(Vector3.right, -40f);
        }
        foreach (float side in new[] { -1f, 1f })
            Mats.Shape(PrimitiveType.Cube, root, new Vector3(hub.x + side * 0.75f, hub.y / 2f, 0f), new Vector3(0.22f, hub.y + 0.1f, 0.22f), timber);

        // The flume: its low end just over the top of the wheel, running back north and a little up
        Vector3 lip = new Vector3(hub.x, hub.y + wheelRadius + 0.3f, 0.45f);
        Vector3 head = lip + new Vector3(0f, 0.3f, 3.2f);
        GameObject flumeModel = Model("Flume");
        Transform flumeEnd = null, flumeStart = null;
        if (flumeModel != null)
        {
            GameObject flume = Object.Instantiate(flumeModel, root);
            foreach (Collider c in flume.GetComponentsInChildren<Collider>())
                Object.Destroy(c);
            flumeEnd = flume.transform.Find("WaterEnd");
            flumeStart = flume.transform.Find("WaterStart");
            if (flumeEnd != null && flumeStart != null)
            {
                // Turn it so start->end points from the head to the lip, then slide it so its end sits on the lip
                Vector3 modelDir = flumeEnd.localPosition - flumeStart.localPosition;
                // (both looks share world up, so it never rolls over)
                flume.transform.localRotation = Quaternion.LookRotation(lip - head) * Quaternion.Inverse(Quaternion.LookRotation(modelDir));
                flume.transform.localPosition = lip - flume.transform.localRotation * flumeEnd.localPosition;
                head = root.InverseTransformPoint(flumeStart.position);
                // Its trestle legs stand on posts up from the ground
                foreach (Transform leg in flume.transform)
                    if (leg.name.StartsWith("LegFoot"))
                    {
                        Vector3 foot = root.InverseTransformPoint(leg.position);
                        Mats.Shape(PrimitiveType.Cube, root, new Vector3(foot.x, foot.y / 2f, foot.z), new Vector3(0.2f, foot.y, 0.2f), timber);
                    }
            }
        }
        else
        {
            GameObject chute = Mats.Shape(PrimitiveType.Cube, root, (lip + head) / 2f - Vector3.up * 0.1f, new Vector3(0.55f, 0.18f, (head - lip).magnitude), timber);
            chute.transform.localRotation = Quaternion.LookRotation(head - lip);
            Mats.Shape(PrimitiveType.Cube, root, new Vector3(head.x, head.y / 2f, head.z), new Vector3(0.2f, head.y, 0.2f), timber);
        }
        // Water running down the trough: a blue bed with bright streaks streaming toward the wheel
        WaterStrip(root, head, lip, 0.36f);

        // Water spilling off the flume's end onto the paddles
        var pour = Emitter(root, lip, "Pour", "flake", false, 200);
        pour.transform.localRotation = Quaternion.LookRotation(lip - head);
        var main = pour.main;
        main.startLifetime = 0.7f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
        main.startColor = waterBody;
        main.gravityModifier = 1f;
        var emission = pour.emission;
        emission.rateOverTime = 110f;
        var shape = pour.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.3f, 0.02f, 0.05f);
        var r = pour.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.08f;
        r.lengthScale = 1f;
        pour.Play();

        // Spray thrown off the paddles as they come up out of the water, and splashing where the water lands
        var spray = Emitter(root, hub + new Vector3(0f, 0.2f, -wheelRadius * 0.7f), "PaddleSpray", "flake", false, 80);
        main = spray.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        main.startColor = waterBody;
        main.gravityModifier = 1f;
        emission = spray.emission;
        emission.rateOverTime = 30f;
        shape = spray.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.3f;
        shape.rotation = new Vector3(-60f, 180f, 0f);
        spray.Play();

        var splash = Emitter(root, new Vector3(hub.x, waterY + 0.05f, 0.4f), "Splash", "flake", false, 120);
        main = splash.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
        main.startColor = waterBody;
        main.gravityModifier = 1f;
        emission = splash.emission;
        emission.rateOverTime = 45f;
        shape = splash.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.4f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        splash.Play();

        var mist = Emitter(root, new Vector3(hub.x - 0.3f, waterY + 0.3f, 0.2f), "Mist", "fog", false, 20);
        main = mist.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startColor = new Color(0.92f, 0.97f, 1f, 0.25f);
        emission = mist.emission;
        emission.rateOverTime = 3f;
        mist.Play();

        // Rings spreading over the pond, most of them by the wheel
        foreach (var (at, spread, rate) in new[] { (new Vector3(hub.x - 0.4f, 0f, 0.2f), 0.6f, 4f), (Vector3.zero, radius * 0.8f, 2.5f) })
        {
            var rings = Emitter(root, new Vector3(at.x, waterY + 0.03f, at.z), "Ripples", "ripple", true, 30);
            main = rings.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startColor = water * 0.5f;
            emission = rings.emission;
            emission.rateOverTime = rate;
            shape = rings.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = spread;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var grow = rings.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.2f, 1f, 1f));
            rings.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            rings.Play();
        }

        // Dragonflies darting over the water
        var flies = Emitter(root, new Vector3(-0.5f, waterY + 0.6f, 0f), "Dragonflies", "glow", true, 6);
        main = flies.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.1f);
        main.startColor = new Color(0.5f, 1f, 0.9f) * 1.6f;
        emission = flies.emission;
        emission.rateOverTime = 0.8f;
        shape = flies.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * 0.6f;
        Noise(flies, 2.5f, 1.2f);
        flies.Play();

        // One solid block for pond and wheel together
        var block = root.gameObject.AddComponent<BoxCollider>();
        block.center = new Vector3(0.4f, 1f, 0f);
        block.size = new Vector3(radius * 2f + 1.6f, 2f, radius * 2f + 0.4f);
    }

    // A strip of running water from one point to another: a blue bed with bright streaks streaming along it
    private static void WaterStrip(Transform parent, Vector3 from, Vector3 to, float width)
    {
        Vector3 along = to - from;
        float length = along.magnitude;
        var go = new GameObject("FlumeWater");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = from;
        go.transform.localRotation = Quaternion.LookRotation(along);
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-width / 2f, 0f, 0f), new Vector3(width / 2f, 0f, 0f), new Vector3(-width / 2f, 0f, length), new Vector3(width / 2f, 0f, length) },
            uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
            triangles = new[] { 0, 2, 1, 1, 2, 3 },
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Get("flume_water", new Color(0.2f, 0.45f, 0.55f), 0.95f);
        var streaks = new GameObject("FlumeStreaks");
        streaks.transform.SetParent(go.transform, false);
        streaks.transform.localPosition = Vector3.up * 0.01f;
        streaks.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = streaks.AddComponent<MeshRenderer>();
        // The flow texture's streaks run along v: scrolling v moves them down the trough
        r.sharedMaterial = Scrolling("flume_flow", FlowTexture(), new Vector2(0f, -1.1f), new Vector2(1f, length / 0.8f), new Color(0.85f, 0.97f, 1f, 0.6f));
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static Texture2D flowTex;

    // Wavering streaks of light along v, seamless both ways (running water)
    private static Texture2D FlowTexture()
    {
        if (flowTex != null)
            return flowTex;
        flowTex = Tileable("amb_flow", (u, v) =>
        {
            float a = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * (u * 5f + 0.15f * Mathf.Sin(2f * Mathf.PI * (v * 2f + u))))), 10f);
            float b = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * (v * 3f + u * 2f)), 8f);
            float k = a * (0.4f + 0.6f * b) + b * 0.25f;
            return new Color(k, k, k, 1f);
        });
        return flowTex;
    }


    // ????????????? Lights and lanterns ?????????????

    // A carved post with an iron lantern swinging gently from its hook, its light swaying with it
    public static void LanternPost(Transform parent, Vector3 local, float yaw, Color light)
    {
        GameObject model = Model("LanternPost");
        if (model == null)
            return;
        GameObject post = Object.Instantiate(model, parent);
        post.transform.localPosition = local;
        post.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Transform pivot = post.transform.Find("Pivot");
        if (pivot == null)
            return;
        pivot.gameObject.AddComponent<Swing>().Init(Random.Range(5f, 9f), Random.Range(1.8f, 2.6f));
        Transform glowAt = pivot.Find("GlowPoint") ?? pivot;
        var lamp = glowAt.GetComponentInChildren<Light>();
        if (lamp == null)
        {
            lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.transform.SetParent(glowAt, false);
        }
        lamp.type = LightType.Point;
        lamp.color = light;
        lamp.range = 7f;
        lamp.intensity = 1.8f;
        lamp.shadows = LightShadows.None;
        if (lamp.GetComponent<FlickerLight>() == null)
            lamp.gameObject.AddComponent<FlickerLight>();
        Vfx.Glow(glowAt, Vector3.zero, 1.3f, light * 0.6f).AddComponent<GlowFlicker>();
        // Moths (or, in Hel, will-o'-wisps) circling the light
        var moths = Emitter(post.transform, post.transform.InverseTransformPoint(glowAt.position), "Moths", "glow", true, 12);
        var main = moths.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
        main.startColor = light * 1.6f;
        var emission = moths.emission;
        emission.rateOverTime = 2f;
        var shape = moths.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;
        Noise(moths, 1.2f, 1.5f);
        moths.Play();
    }

    // A tiny-sparkle twinkle over a treasure chest or an ice formation
    private static void Glints(Transform prop, Color color)
    {
        Bounds b = WorldBounds(prop);
        var ps = Emitter(prop.parent, prop.parent.InverseTransformPoint(b.center), "Amb_Glints", "flare", true, 20);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startColor = color * 2f;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
        var emission = ps.emission;
        emission.rateOverTime = 2.5f;
        Box(ps, b.size * 0.8f);
        var grow = ps.sizeOverLifetime;
        grow.enabled = true;
        grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f)));
        ps.Play();
    }

    // ????????????? Asgard ?????????????

    // A great tree in the wind; golden and oak trees standing in the play area drop leaves
    private static void Tree(Transform tree, float strength, Color leaves, bool golden)
    {
        if (WindSway.Add(tree.gameObject, WindSway.Kind.Tree, strength) == null)
            tree.gameObject.AddComponent<TiltSway>().Init(1.2f * strength, 0.6f);
        if (leaves.a <= 0f || Random.value > 0.6f)
            return;
        Bounds b = WorldBounds(tree);
        var ps = Emitter(tree.parent, tree.parent.InverseTransformPoint(new Vector3(b.center.x, b.min.y + b.size.y * 0.75f, b.center.z)), "Amb_TreeLeaves", "leaf", false, 40);
        var main = ps.main;
        main.prewarm = true;
        main.startLifetime = 5f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.3f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = golden ? new ParticleSystem.MinMaxGradient(leaves * 1.3f, new Color(1f, 0.65f, 0.2f)) : new ParticleSystem.MinMaxGradient(leaves, new Color(0.85f, 0.6f, 0.2f));
        main.gravityModifier = 0.03f;
        var emission = ps.emission;
        emission.rateOverTime = golden ? 2.5f : 1.5f;
        Box(ps, new Vector3(b.size.x * 0.6f, b.size.y * 0.25f, b.size.z * 0.6f));
        var spin = ps.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
        Noise(ps, 0.8f, 0.5f);
        ps.Play();
        if (golden)
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = Vfx.Material("leaf", false);
    }

    // Shards of Bifrost crystal circling an obelisk's tip, rainbow motes rising round it, its light breathing
    private static void ObeliskShards(Transform obelisk)
    {
        Bounds b = WorldBounds(obelisk);
        var orbit = new GameObject("Amb_ShardOrbit").transform;
        orbit.SetParent(obelisk.parent, false);
        orbit.position = new Vector3(b.center.x, b.min.y + b.size.y * 0.72f, b.center.z);
        orbit.gameObject.AddComponent<Spin>().Init(Vector3.up, Random.value < 0.5f ? 35f : -35f);
        float reach = Mathf.Max(b.size.x, b.size.z) * 0.62f;
        for (int i = 0; i < 3; i++)
        {
            Color c = Rainbow[(i * 2 + Random.Range(0, 2)) % Rainbow.Length];
            float a = i * 120f * Mathf.Deg2Rad;
            var holder = new GameObject("Shard").transform;
            holder.SetParent(orbit, false);
            holder.localPosition = new Vector3(Mathf.Cos(a) * reach, 0f, Mathf.Sin(a) * reach);
            GameObject shard = Mats.Shape(PrimitiveType.Cube, holder, Vector3.zero, new Vector3(0.16f, 0.32f, 0.16f), Mats.Get("bifrost_shard_" + i, c, 0.9f, c * 2.2f));
            shard.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
            shard.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            holder.gameObject.AddComponent<Bob>().Init(0.18f, Random.Range(1.8f, 2.6f), 90f);
            Vfx.Glow(holder, Vector3.zero, 0.7f, c * 0.6f);
        }
        var ps = Emitter(obelisk.parent, obelisk.parent.InverseTransformPoint(new Vector3(b.center.x, b.min.y + 0.2f, b.center.z)), "Amb_ObeliskMotes", "glow", true, 40);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 3.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
        main.startColor = RandomRainbow(1.8f);
        var emission = ps.emission;
        emission.rateOverTime = 6f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = reach * 0.8f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        Noise(ps, 0.3f, 0.6f);
        ps.Play();
    }

    // A rune circle turning slowly over a floor medallion
    public static void RuneHalo(Transform parent, Vector3 local, float radius, Color color)
    {
        VfxPiece halo = Vfx.Decal(parent.TransformPoint(local + Vector3.up * 0.02f), radius * 2f, color, "runes", 7f);
        halo.transform.SetParent(parent, true);
    }

    // Light running outward along the Bifrost rays of a sanctum, again and again
    public static void RayPulses(Transform parent, float start, float reach, int rays)
    {
        var root = new GameObject("Amb_RayPulses").transform;
        root.SetParent(parent, false);
        root.gameObject.AddComponent<RayPulse>().Init(start, reach, rays, Rainbow);
    }

    // The astrolabe of Asgard: a marble pedestal with a crystal orb, three golden rings turning round it on
    // different axes, its light breathing, motes rising off it
    public static bool Astrolabe(Transform parent, Vector3 local, float scale)
    {
        GameObject model = Model("Astrolabe");
        if (model == null)
            return false;
        GameObject pedestal = Object.Instantiate(model, parent);
        pedestal.transform.localPosition = local;
        pedestal.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        pedestal.transform.localScale *= scale;
        Bounds b = WorldBounds(pedestal.transform);
        float h = b.size.y;
        var orb = new Vector3(b.center.x, b.min.y + h * 0.885f, b.center.z);
        var center = new GameObject("Amb_Rings").transform;
        center.SetParent(parent, false);
        center.position = orb;
        Material gold = Mats.Get("astrolabe_gold", new Color(1f, 0.8f, 0.4f), 0.85f, new Color(0.6f, 0.42f, 0.12f));
        float[] radii = { h * 0.26f, h * 0.21f, h * 0.165f };
        Vector3[] tilts = { new Vector3(70f, 0f, 0f), new Vector3(0f, 0f, 60f), new Vector3(25f, 0f, -20f) };
        Vector3[] axes = { Vector3.up, Vector3.right, Vector3.forward };
        float[] speeds = { 22f, -38f, 55f };
        for (int i = 0; i < 3; i++)
        {
            var pivot = new GameObject("RingPivot").transform;
            pivot.SetParent(center, false);
            pivot.localRotation = Quaternion.Euler(tilts[i]);
            pivot.gameObject.AddComponent<Spin>().Init(axes[i], speeds[i]);
            var ring = new GameObject("Ring");
            ring.transform.SetParent(pivot, false);
            ring.AddComponent<MeshFilter>().sharedMesh = Torus(radii[i], h * 0.018f, 64, 8);
            ring.AddComponent<MeshRenderer>().sharedMaterial = gold;
            // A bead of light riding each ring
            Vfx.Glow(ring.transform, new Vector3(radii[i], 0f, 0f), 0.5f, new Color(1f, 0.85f, 0.5f));
        }
        Vfx.Glow(center, Vector3.zero, h * 0.4f, new Color(0.45f, 0.75f, 1f) * 0.6f).AddComponent<GlowFlicker>().Init(0.6f, 1.6f);
        var lightGo = new GameObject("OrbLight");
        lightGo.transform.SetParent(center, false);
        var lamp = lightGo.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = new Color(0.55f, 0.8f, 1f);
        lamp.range = 8f;
        lamp.intensity = 1.6f;
        lamp.shadows = LightShadows.None;
        var ps = Emitter(center, Vector3.zero, "OrbMotes", "glow", true, 40);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        main.startColor = new Color(0.6f, 0.85f, 1f) * 2f;
        var emission = ps.emission;
        emission.rateOverTime = 10f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = h * 0.12f;
        Noise(ps, 0.3f, 0.8f);
        ps.Play();
        RuneHalo(parent, local, h * 0.45f, new Color(0.5f, 0.75f, 1f) * 0.5f);
        return true;
    }

    // The rainbow in the Bifrost gate flows: light runs down through it, sparkles fall, mist rolls at its foot
    private static void GateShimmer(Transform gate)
    {
        Vector3 s = gate.lossyScale;
        // The opening, in the model's own metres (its arch is 0.54 of its width across, 0.66 of its height tall)
        const float width = 7.4f, height = 9.2f, bottom = 0.4f;
        foreach (float side in new[] { -1f, 1f })
        {
            var go = new GameObject("Amb_GateFlow");
            go.transform.SetParent(gate, false);
            go.transform.localPosition = new Vector3(0f, bottom + height / 2f, side * 0.3f);
            go.transform.localRotation = Quaternion.Euler(0f, side > 0f ? 180f : 0f, 0f);
            go.transform.localScale = new Vector3(width, height, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Scrolling("gateflow", GateFlowTexture(), new Vector2(0f, 0.18f), new Vector2(1f, 1f), new Color(1f, 1f, 1f, 0.55f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var ps = Emitter(gate, new Vector3(0f, bottom + height * 0.85f, 0f), "Amb_GateSparkles", "glow", true, 80);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
        main.startColor = RandomRainbow(2f);
        main.gravityModifier = 0.25f;
        var emission = ps.emission;
        emission.rateOverTime = 14f;
        Box(ps, new Vector3(width * 0.8f, 0.5f, 0.6f));
        ps.Play();
        var mist = Emitter(gate, new Vector3(0f, 0.3f, 0f), "Amb_GateMist", "smoke", false, 30);
        main = mist.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 3.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = RandomRainbow(1f);
        var mistFade = mist.colorOverLifetime;
        mistFade.color = FadeInOut(0.3f, 0.5f);
        var mr = mist.GetComponent<ParticleSystemRenderer>();
        mr.sharedMaterial = Vfx.Material("glow", true);
        emission = mist.emission;
        emission.rateOverTime = 5f;
        Box(mist, new Vector3(width * 0.8f, 0.2f, 1.5f));
        mist.Play();
    }

    // Heimdall's watchtower keeps its beacon lit: a turning light at its top, sweeping the dark
    private static void Beacon(Transform tower)
    {
        Bounds b = WorldBounds(tower);
        var top = new GameObject("Amb_Beacon").transform;
        top.SetParent(tower.parent, false);
        top.position = new Vector3(b.center.x, b.max.y - b.size.y * 0.1f, b.center.z);
        top.gameObject.AddComponent<Spin>().Init(Vector3.up, 30f);
        Color gold = new Color(1f, 0.85f, 0.5f);
        foreach (float side in new[] { 0f, 180f })
        {
            var arm = new GameObject("Beam").transform;
            arm.SetParent(top, false);
            arm.localRotation = Quaternion.Euler(0f, side, 0f);
            var quad = new GameObject("Quad");
            quad.transform.SetParent(arm, false);
            quad.transform.localPosition = new Vector3(0f, 0f, 5f);
            quad.transform.localRotation = Quaternion.Euler(90f, 90f, 0f);
            quad.transform.localScale = new Vector3(10f, 1.4f, 1f);
            quad.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
            var r = quad.AddComponent<MeshRenderer>();
            r.sharedMaterial = Vfx.Material("beam", true);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", gold * 0.35f);
            r.SetPropertyBlock(block);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Vfx.Glow(top, Vector3.zero, 3f, gold * 0.8f).AddComponent<GlowFlicker>().Init(0.7f, 1f);
        var lamp = top.gameObject.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = gold;
        lamp.range = 14f;
        lamp.intensity = 2f;
        lamp.shadows = LightShadows.None;
    }

    // ????????????? Jotunheim and Muspelheim ?????????????

    // A cluster of ice crystals hanging in the air: it turns and bobs, glitters, and casts a cold light and
    // a ring of frost on the snow below
    public static void FrostCrystal(Transform parent, Vector3 local, float height, float scale)
    {
        GameObject model = Model("IceCrystal");
        if (model == null)
            return;
        var holder = new GameObject("FrostCrystal").transform;
        holder.SetParent(parent, false);
        holder.localPosition = local + Vector3.up * height;
        GameObject crystal = Object.Instantiate(model, holder);
        crystal.transform.localScale *= scale;
        crystal.transform.localRotation = Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f));
        foreach (Collider c in crystal.GetComponentsInChildren<Collider>())
            Object.Destroy(c);
        holder.gameObject.AddComponent<Bob>().Init(0.3f, Random.Range(3.5f, 5f), Random.Range(10f, 20f) * (Random.value < 0.5f ? 1f : -1f));
        Color ice = new Color(0.6f, 0.85f, 1f);
        Vfx.Glow(holder, Vector3.zero, 2.4f * scale, ice * 0.45f).AddComponent<GlowFlicker>().Init(0.5f, 0.8f);
        var lamp = new GameObject("FrostLight").AddComponent<Light>();
        lamp.transform.SetParent(holder, false);
        lamp.type = LightType.Point;
        lamp.color = ice;
        lamp.range = 6f;
        lamp.intensity = 1.2f;
        lamp.shadows = LightShadows.None;
        var ps = Emitter(holder, Vector3.zero, "Glitter", "flare", true, 30);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
        main.startColor = ice * 2f;
        main.gravityModifier = 0.05f;
        var emission = ps.emission;
        emission.rateOverTime = 6f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.8f * scale;
        ps.Play();
        VfxPiece frost = Vfx.Decal(parent.TransformPoint(local + Vector3.up * 0.03f), 2.6f * scale, ice * 0.35f, "ring", 6f);
        frost.transform.SetParent(parent, true);
    }

    // ????????????? Creatures ?????????????

    // Ravens wheeling high over the zone, flapping and gliding by turns
    private static void Flock(Transform zone, Vector2 size, int count)
    {
        GameObject model = Model("Raven");
        if (model == null)
            return;
        var root = new GameObject("Amb_Flock").transform;
        root.SetParent(zone, false);
        for (int i = 0; i < count; i++)
        {
            GameObject bird = Object.Instantiate(model, root);
            foreach (Collider c in bird.GetComponentsInChildren<Collider>())
                Object.Destroy(c);
            // Low enough to stay well under the camera (8.6 m up), wide enough to keep mostly to the zone's edges
            float reach = Mathf.Min(size.x, size.y) * Random.Range(0.32f, 0.45f);
            bird.AddComponent<Circler>().Init(new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)),
                reach, Random.Range(3.6f, 4.8f), Random.Range(0.25f, 0.4f) * (Random.value < 0.5f ? 1f : -1f));
        }
    }

    // Butterflies fluttering low over Midgard's grass
    private static void Butterflies(Transform zone, Vector2 size, int count)
    {
        Color[] wings = { new Color(1f, 0.7f, 0.2f), new Color(0.4f, 0.65f, 1f), new Color(1f, 0.95f, 0.85f), new Color(0.9f, 0.4f, 0.6f) };
        for (int i = 0; i < count; i++)
        {
            var fly = new GameObject("Amb_Butterfly").transform;
            fly.SetParent(zone, false);
            Color c = wings[Random.Range(0, wings.Length)];
            var flaps = new Transform[2];
            for (int s = 0; s < 2; s++)
            {
                var hinge = new GameObject("Wing").transform;
                hinge.SetParent(fly, false);
                var quad = new GameObject("Quad");
                quad.transform.SetParent(hinge, false);
                quad.transform.localPosition = new Vector3((s == 0 ? -1f : 1f) * 0.09f, 0f, 0f);
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localScale = new Vector3(0.18f, 0.2f, 1f);
                quad.AddComponent<MeshFilter>().sharedMesh = Vfx.QuadMesh;
                var r = quad.AddComponent<MeshRenderer>();
                r.sharedMaterial = Vfx.Material("leaf", false);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", c);
                r.SetPropertyBlock(block);
                flaps[s] = hinge;
            }
            fly.gameObject.AddComponent<Butterfly>().Init(flaps, new Vector2(size.x / 2f - 1f, size.y / 2f - 1f));
        }
    }

    // ????????????? Meshes, textures and materials ?????????????

    private static Bounds WorldBounds(Transform t)
    {
        Renderer[] rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0)
            return new Bounds(t.position, Vector3.one);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs)
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
                b.Encapsulate(r.bounds);
        return b;
    }

    private static float Height(Transform t) => WorldBounds(t).size.y;

    // A flat disc facing up; its texture (if any) tiled every 2 m
    private static GameObject Disc(Transform parent, Vector3 local, float radius, string name, Texture2D overlay, float scrollSpeed,
        Material solid = null, Color? overlayColor = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.AddComponent<MeshFilter>().sharedMesh = Ring(0f, radius, 48);
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Color tint = overlayColor ?? new Color(0.7f, 0.9f, 1f, 0.28f);
        r.sharedMaterial = solid ?? Scrolling("caustics_" + scrollSpeed.ToString("0.0") + "_" + ColorUtility.ToHtmlStringRGBA(tint), overlay, new Vector2(0.04f, 0.03f) * scrollSpeed, Vector2.one * radius,
            tint);
        return go;
    }

    // A flat ring facing up (a disc when inner is 0), its UVs laid out in metres / 2
    private static Mesh Ring(float inner, float outer, int segments)
    {
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices.Add(dir * inner);
            vertices.Add(dir * outer);
            uv.Add(new Vector2(dir.x * inner, dir.z * inner) / (2f * Mathf.Max(outer, 0.01f)) + Vector2.one * 0.5f);
            uv.Add(new Vector2(dir.x, dir.z) * 0.5f + Vector2.one * 0.5f);
            if (i == segments)
                break;
            int v = i * 2;
            triangles.AddRange(new[] { v, v + 3, v + 1, v, v + 2, v + 3 });
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh Torus(float radius, float thickness, int around, int tube)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= around; i++)
        {
            float a = i * Mathf.PI * 2f / around;
            var center = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            for (int j = 0; j <= tube; j++)
            {
                float b = j * Mathf.PI * 2f / tube;
                vertices.Add(center + (center.normalized * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b)) * thickness);
                if (i < around && j < tube)
                {
                    int k = i * (tube + 1) + j, next = k + tube + 1;
                    triangles.AddRange(new[] { k, k + 1, next, next, k + 1, next + 1 });
                }
            }
        }
        var mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static readonly Dictionary<string, Material> scrolling = new Dictionary<string, Material>();

    // An additive, scrolling copy of a particle material (light on water, the flow through the Bifrost gate, Heimdall's beams)
    public static Material Scrolling(string key, Texture2D texture, Vector2 speed, Vector2 tiling, Color color)
    {
        if (scrolling.TryGetValue(key, out Material mat) && mat != null)
            return mat;
        mat = new Material(Vfx.Material("glow", true)) { name = "amb_" + key };
        mat.SetTexture("_BaseMap", texture);
        mat.SetTextureScale("_BaseMap", tiling);
        mat.SetColor("_BaseColor", color);
        scrolling[key] = mat;
        var driver = new GameObject("Amb_Scroll_" + key);
        Object.DontDestroyOnLoad(driver);
        driver.hideFlags = HideFlags.HideInHierarchy;
        driver.AddComponent<ScrollMaterial>().Init(mat, speed);
        return mat;
    }

    private static Texture2D caustics, gateFlow;

    // Bright wavering lines of light, seamless in both directions
    private static Texture2D Caustics()
    {
        if (caustics != null)
            return caustics;
        caustics = Tileable("amb_caustics", (u, v) =>
        {
            float a = Mathf.Sin(2f * Mathf.PI * (u * 3f + 0.35f * Mathf.Sin(2f * Mathf.PI * (v * 2f + u))));
            float b = Mathf.Sin(2f * Mathf.PI * (v * 3f + 0.35f * Mathf.Sin(2f * Mathf.PI * (u * 2f - v))));
            float k = Mathf.Pow(1f - Mathf.Abs(a), 6f) + Mathf.Pow(1f - Mathf.Abs(b), 6f);
            return new Color(k, k, k, 1f) * 0.8f;
        });
        return caustics;
    }

    // Streaks of white light running down an arch-shaped window, so nothing shows over the stone round the arch
    private static Texture2D GateFlowTexture()
    {
        if (gateFlow != null)
            return gateFlow;
        gateFlow = Tileable("amb_gateflow", (u, v) =>
        {
            float streak = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * (u * 9f + 0.25f * Mathf.Sin(2f * Mathf.PI * v * 2f)))), 18f);
            float sheen = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * (v * 3f + u)), 6f);
            float k = streak * 0.8f + sheen * 0.35f;
            return new Color(k, k, k, 1f);
        }, wrapU: false);
        return gateFlow;
    }

    private static Texture2D Tileable(string name, System.Func<float, float, Color> pixel, bool wrapU = true)
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };
        if (!wrapU)
            tex.wrapModeU = TextureWrapMode.Clamp;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = pixel((x + 0.5f) / n, (y + 0.5f) / n);
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    // A material's emission breathing (exit seals, glowing paths, lava cracks, glinting crystals); once per material
    private static void Pulse(Renderer r, HashSet<Material> done, float speed, float depth)
    {
        if (r == null || r.sharedMaterial == null || !done.Add(r.sharedMaterial) || !r.sharedMaterial.IsKeywordEnabled("_EMISSION"))
            return;
        r.gameObject.AddComponent<EmissionPulse>().Init(r.sharedMaterial, speed, depth);
    }
}

// ????????????? Small movers ?????????????

// Turns steadily around a local axis
public class Spin : MonoBehaviour
{
    private Vector3 axis = Vector3.up;
    private float speed;

    public void Init(Vector3 localAxis, float degreesPerSecond)
    {
        axis = localAxis;
        speed = degreesPerSecond;
    }

    private void Update() => transform.Rotate(axis, speed * Time.deltaTime, Space.Self);
}

// Floats up and down around where it was put, turning slowly
public class Bob : MonoBehaviour
{
    private Vector3 home;
    private float height, period, turn, phase;
    private bool ready;

    public void Init(float amplitude, float seconds, float degreesPerSecond)
    {
        home = transform.localPosition;
        height = amplitude;
        period = Mathf.Max(0.1f, seconds);
        turn = degreesPerSecond;
        phase = Random.Range(0f, Mathf.PI * 2f);
        ready = true;
    }

    private void Update()
    {
        if (!ready)
            return;
        transform.localPosition = home + Vector3.up * Mathf.Sin(Time.time * Mathf.PI * 2f / period + phase) * height;
        if (turn != 0f)
            transform.Rotate(Vector3.up, turn * Time.deltaTime, Space.World);
    }
}

// Swings like a pendulum (a hanging lantern), a little off-axis so it never looks mechanical
public class Swing : MonoBehaviour
{
    private Quaternion rest;
    private float amplitude, period, phase;

    public void Init(float degrees, float seconds)
    {
        rest = transform.localRotation;
        amplitude = degrees;
        period = seconds;
        phase = Random.Range(0f, 10f);
    }

    private void Update()
    {
        float t = Time.time * Mathf.PI * 2f / period + phase;
        float gust = 0.6f + 0.4f * Mathf.PerlinNoise(phase, Time.time * 0.3f);
        transform.localRotation = rest * Quaternion.Euler(Mathf.Sin(t) * amplitude * gust, 0f, Mathf.Sin(t * 0.63f + 1f) * amplitude * 0.5f * gust);
    }
}

// A whole prop leaning back and forth from its base (for models whose mesh can't be bent)
public class TiltSway : MonoBehaviour
{
    private Quaternion rest;
    private float degrees, speed, seed;

    public void Init(float amplitude, float frequency)
    {
        rest = transform.localRotation;
        degrees = amplitude;
        speed = frequency;
        seed = Random.Range(0f, 10f);
    }

    private void Update()
    {
        float t = Time.time * speed + seed;
        transform.localRotation = rest * Quaternion.Euler(Mathf.Sin(t) * degrees, 0f, Mathf.Sin(t * 0.7f + 2f) * degrees * 0.6f);
    }
}

// A glow sprite that wavers in size and brightness like firelight (or slowly breathes)
public class GlowFlicker : MonoBehaviour
{
    private VfxPiece piece;
    private Color color;
    private float size, depth = 0.35f, speed = 6f, seed;

    public GlowFlicker Init(float amount, float rate)
    {
        depth = amount;
        speed = rate;
        return this;
    }

    private void Start()
    {
        piece = GetComponent<VfxPiece>();
        seed = Random.Range(0f, 50f);
        size = transform.localScale.x;
        var r = GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        color = block.GetColor("_BaseColor");
    }

    private void Update()
    {
        if (piece == null)
            return;
        float n = Mathf.PerlinNoise(seed, Time.time * speed);
        float k = 1f - depth + depth * 2f * n;
        piece.Set(color, size * (0.85f + 0.3f * n), k);
    }
}

// Slides a shared material's texture along, by absolute time so any number of users agree
public class ScrollMaterial : MonoBehaviour
{
    private Material mat;
    private Vector2 speed;
    private string extra;

    public void Init(Material material, Vector2 unitsPerSecond, string alsoProperty = null)
    {
        mat = material;
        speed = unitsPerSecond;
        extra = alsoProperty;
    }

    private void Update()
    {
        if (mat == null)
        {
            Destroy(gameObject);
            return;
        }
        Vector2 offset = speed * Time.time;
        offset.x %= 1f;
        offset.y %= 1f;
        mat.SetTextureOffset("_BaseMap", offset);
        if (!string.IsNullOrEmpty(extra))
            mat.SetTextureOffset(extra, offset);
    }
}

// A material's emission swelling and fading
public class EmissionPulse : MonoBehaviour
{
    // Each material's own emission, taken before any pulse touched it (several zones may pulse the same one)
    private static readonly Dictionary<Material, Color> original = new Dictionary<Material, Color>();

    private Material mat;
    private Color baseEmission;
    private float speed, depth;

    public void Init(Material material, float rate, float amount)
    {
        mat = material;
        if (!original.TryGetValue(material, out baseEmission))
            original[material] = baseEmission = material.GetColor("_EmissionColor");
        speed = rate;
        depth = amount;
    }

    private void Update()
    {
        if (mat != null)
            mat.SetColor("_EmissionColor", baseEmission * (1f - depth + depth * (0.5f + 0.5f * Mathf.Sin(Time.time * speed * Mathf.PI))));
    }

    private void OnDestroy()
    {
        if (mat != null)
            mat.SetColor("_EmissionColor", baseEmission);
    }
}

// A log fire's pops: every few seconds a crack sends a shower of sparks up out of it
public class FirePops : MonoBehaviour
{
    private float next;

    private void OnEnable() => next = Time.time + Random.Range(1f, 3f);

    private void Update()
    {
        if (Time.time < next)
            return;
        next = Time.time + Random.Range(1.5f, 4.5f);
        Vfx.Burst(transform.position + Vector3.up * 0.3f, new Color(1f, 0.65f, 0.25f), Random.Range(8, 16), Random.Range(3f, 5f), 0.08f, -0.4f);
        Vfx.Flash(transform.position + Vector3.up * 0.4f, 1.2f, new Color(1f, 0.6f, 0.2f), 0.15f);
    }
}

// A light and its glow drifting slowly through the colours of the Bifrost
public class RainbowCycle : MonoBehaviour
{
    private Light lamp;
    private VfxPiece glow;
    private float offset;

    public void Init(Light light, VfxPiece piece, float phase)
    {
        lamp = light;
        glow = piece;
        offset = phase;
    }

    private void Update()
    {
        Color c = Color.HSVToRGB((Time.time * 0.05f + offset) % 1f, 0.45f, 1f);
        if (lamp != null)
            lamp.color = c;
        if (glow != null)
            glow.Set(c * 0.55f, 1.4f, 0.8f + 0.2f * Mathf.Sin(Time.time * 2f + offset * 10f));
    }
}

// Turns back and forth about its post, easing at each end (a training quintain's beam after a blow)
public class Swivel : MonoBehaviour
{
    private Quaternion rest;
    private float degrees, period, phase;

    public void Init(float amplitude, float seconds)
    {
        rest = transform.localRotation;
        degrees = amplitude;
        period = seconds;
        phase = Random.Range(0f, 10f);
    }

    private void Update() =>
        transform.localRotation = rest * Quaternion.Euler(0f, Mathf.Sin(Time.time * Mathf.PI * 2f / period + phase) * degrees, 0f);
}

// Travelling sparks of rainbow light running out along a sanctum's floor rays
public class RayPulse : MonoBehaviour
{
    private readonly List<(Transform t, float angle, float phase)> sparks = new List<(Transform, float, float)>();
    private float from, to;

    public void Init(float start, float reach, int rays, Color[] colors)
    {
        from = start;
        to = reach;
        for (int i = 0; i < rays; i++)
        {
            float angle = 22.5f + i * 360f / rays;
            var holder = new GameObject("Spark").transform;
            holder.SetParent(transform, false);
            Vfx.Glow(holder, Vector3.zero, 0.9f, colors[i % colors.Length] * 1.4f);
            sparks.Add((holder, angle, Random.Range(0f, 1f)));
        }
    }

    private void Update()
    {
        foreach (var (t, angle, phase) in sparks)
        {
            float k = (Time.time * 0.35f + phase) % 1f;
            var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            t.localPosition = dir * Mathf.Lerp(from, to, k) + Vector3.up * 0.06f;
            t.localScale = Vector3.one * Mathf.Sin(k * Mathf.PI);
        }
    }
}

// A bird circling high overhead: banking into the turn, flapping a while then gliding a while
public class Circler : MonoBehaviour
{
    private Vector3 center;
    private float radius, height, speed, angle;
    private WindSway wings;
    private float switchAt;
    private bool gliding;

    public void Init(Vector3 localCenter, float circleRadius, float altitude, float radiansPerSecond)
    {
        center = localCenter;
        radius = circleRadius;
        height = altitude;
        speed = radiansPerSecond;
        angle = Random.Range(0f, Mathf.PI * 2f);
        wings = WindSway.Add(gameObject, WindSway.Kind.Wings, 1f, Random.Range(0, WindSway.FlapGroups));
        switchAt = Time.time + Random.Range(1f, 4f);
        Update();
    }

    private void Update()
    {
        angle += speed * Time.deltaTime;
        var p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        p.y = height + Mathf.Sin(angle * 2.3f) * 0.6f;
        transform.localPosition = p;
        Vector3 ahead = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * Mathf.Sign(speed);
        transform.localRotation = Quaternion.LookRotation(ahead) * Quaternion.Euler(0f, 0f, -Mathf.Sign(speed) * 18f);
        if (Time.time >= switchAt && wings != null)
        {
            gliding = !gliding;
            switchAt = Time.time + (gliding ? Random.Range(2f, 4f) : Random.Range(1.5f, 3f));
            wings.SetGroup(gliding ? WindSway.GlideGroup : Random.Range(0, WindSway.FlapGroups));
        }
    }
}

// A butterfly: wanders over the ground in loose loops, wings beating fast
public class Butterfly : MonoBehaviour
{
    private Transform[] wings;
    private Vector2 half;
    private Vector3 target;
    private float seed, beat;

    public void Init(Transform[] wingHinges, Vector2 halfSize)
    {
        wings = wingHinges;
        half = halfSize;
        seed = Random.Range(0f, 100f);
        beat = Random.Range(14f, 20f);
        transform.localPosition = NewTarget();
        target = NewTarget();
    }

    private Vector3 NewTarget() => new Vector3(Random.Range(-half.x, half.x), Random.Range(0.6f, 1.8f), Random.Range(-half.y, half.y));

    private void Update()
    {
        if (wings == null)
            return;
        Vector3 to = target - transform.localPosition;
        if (to.magnitude < 0.5f)
            target = NewTarget();
        // A jittery, fluttering course toward the next spot
        Vector3 jitter = new Vector3(Mathf.PerlinNoise(seed, Time.time * 1.5f) - 0.5f, Mathf.Sin(Time.time * 4f + seed) * 0.4f, Mathf.PerlinNoise(Time.time * 1.5f, seed) - 0.5f) * 2f;
        Vector3 step = (to.normalized * 1.2f + jitter) * Time.deltaTime;
        transform.localPosition += step;
        if (step.sqrMagnitude > 0.000001f)
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.LookRotation(new Vector3(step.x, 0f, step.z) + Vector3.forward * 0.0001f), Time.deltaTime * 4f);
        float flap = Mathf.Sin(Time.time * beat + seed) * 65f;
        wings[0].localRotation = Quaternion.Euler(0f, 0f, -flap);
        wings[1].localRotation = Quaternion.Euler(0f, 0f, flap);
    }
}

// Drifts through the zone along a slow, looping path (Hel's lost souls)
public class Wander : MonoBehaviour
{
    private Vector2 half;
    private float speed, seed;

    public void Init(Vector2 halfSize, float pace)
    {
        half = halfSize;
        speed = pace / Mathf.Max(half.x, half.y);
        seed = Random.Range(0f, 100f);
    }

    private void Update()
    {
        float t = Time.time * speed + seed;
        transform.localPosition = new Vector3(Mathf.Sin(t * 1.3f) * half.x * 0.85f, 1.2f + Mathf.Sin(t * 3.1f) * 0.6f, Mathf.Sin(t * 0.9f + 1.7f) * half.y * 0.85f);
    }
}

// ????????????? Bending meshes ?????????????

// Bends a prop's mesh every frame: tree crowns sway and their leaves flutter, banner cloth ripples, a bird's wings beat.
// Every instance of a model shares one bent copy per phase group, so a forest costs no more than a few trees.
// Needs the model imported with Read/Write enabled; Add returns null (and changes nothing) otherwise.
public class WindSway : MonoBehaviour
{
    public enum Kind { Tree, Banner, Wings }

    private const int Groups = 3;
    // Birds: three flapping phases, then a gliding pose, then three fast-beating phases (a raven darting away)
    public const int FlapGroups = 3;
    public const int GlideGroup = FlapGroups;
    public const int FastGroups = FlapGroups + 1;

    private class Shared
    {
        public Mesh mesh;
        public Vector3[] rest, now;
        public float[] weight, phase;
        public Vector3 swayDir, flutterDir;
        public Kind kind;
        public int group, frame = -1;
    }

    private static readonly Dictionary<(Mesh, Kind, int), Shared> pool = new Dictionary<(Mesh, Kind, int), Shared>();

    private MeshFilter[] filters;
    private Mesh[] sources;
    private Shared[] parts;
    private Kind kind;
    private float strength;

    public static WindSway Add(GameObject go, Kind kind, float strength = 1f, int group = -1)
    {
        MeshFilter[] filters = go.GetComponentsInChildren<MeshFilter>();
        if (filters.Length == 0)
            return null;
        foreach (MeshFilter f in filters)
            if (f.sharedMesh == null || !f.sharedMesh.isReadable)
                return null;
        var sway = go.AddComponent<WindSway>();
        sway.filters = filters;
        sway.sources = new Mesh[filters.Length];
        for (int i = 0; i < filters.Length; i++)
            sway.sources[i] = filters[i].sharedMesh;
        sway.parts = new Shared[filters.Length];
        sway.kind = kind;
        sway.strength = strength;
        sway.SetGroup(group >= 0 ? group : Random.Range(0, Groups));
        return sway;
    }

    public void SetGroup(int group)
    {
        // Unset after a script reload mid-play: the bird just keeps its last pose
        if (filters == null || sources == null || parts == null)
            return;
        for (int i = 0; i < filters.Length; i++)
        {
            parts[i] = Get(sources[i], filters[i].transform, transform, kind, group, strength);
            filters[i].sharedMesh = parts[i].mesh;
        }
    }

    private static Shared Get(Mesh source, Transform part, Transform root, Kind kind, int group, float strength)
    {
        if (pool.TryGetValue((source, kind, group), out Shared shared) && shared.mesh != null)
            return shared;
        Matrix4x4 toRoot = root.worldToLocalMatrix * part.localToWorldMatrix;
        Matrix4x4 toMesh = toRoot.inverse;
        Vector3[] rest = source.vertices;
        var p = new Vector3[rest.Length];
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        for (int i = 0; i < rest.Length; i++)
        {
            p[i] = toRoot.MultiplyPoint3x4(rest[i]);
            min = Vector3.Min(min, p[i]);
            max = Vector3.Max(max, p[i]);
        }
        float h = Mathf.Max(0.01f, max.y - min.y);
        var weight = new float[rest.Length];
        var phase = new float[rest.Length];
        Vector3 sway, flutter;
        switch (kind)
        {
            case Kind.Tree:
                for (int i = 0; i < p.Length; i++)
                {
                    float k = Mathf.Clamp01((p[i].y - min.y) / h);
                    weight[i] = k * k;
                    phase[i] = (p[i].x + p[i].z) * 2.5f + p[i].y * 1.7f;
                }
                sway = new Vector3(1f, 0f, 0.35f).normalized * h * 0.035f * strength;
                flutter = new Vector3(0.6f, 0.45f, 0.5f) * h * 0.008f * strength;
                break;
            case Kind.Banner:
            {
                // The cloth hangs from the crossbar near the top; its lower edge moves most, the stand's base not at all
                float top = min.y + h * 0.88f, hem = min.y + h * 0.28f;
                for (int i = 0; i < p.Length; i++)
                {
                    float k = Mathf.Clamp01((top - p[i].y) / (top - hem)) * Mathf.Clamp01((p[i].y - (hem - h * 0.06f)) / (h * 0.06f));
                    weight[i] = k;
                    phase[i] = (top - p[i].y) / h * 7f + p[i].x * 1.3f;
                }
                sway = new Vector3(0.3f, 0f, 1f) * h * 0.012f * strength;
                flutter = new Vector3(0.25f, 0f, 1f) * h * 0.03f * strength;
                break;
            }
            default:
            {
                // Wingspan along whichever flat axis is longer; the body in the middle stays put
                bool alongX = max.x - min.x >= max.z - min.z;
                float mid = alongX ? (min.x + max.x) / 2f : (min.z + max.z) / 2f;
                float half = Mathf.Max(0.01f, (alongX ? max.x - min.x : max.z - min.z) / 2f);
                for (int i = 0; i < p.Length; i++)
                {
                    float a = Mathf.Abs((alongX ? p[i].x : p[i].z) - mid);
                    weight[i] = Mathf.Pow(Mathf.Clamp01((a - half * 0.14f) / (half * 0.86f)), 1.3f);
                }
                sway = Vector3.zero;
                flutter = Vector3.up * half * 0.55f;
                break;
            }
        }
        var mesh = Instantiate(source);
        mesh.name = source.name + "_" + kind + group;
        mesh.MarkDynamic();
        Bounds b = source.bounds;
        b.Expand(toMesh.MultiplyVector(Vector3.one * h * 0.5f).magnitude);
        mesh.bounds = b;
        shared = new Shared
        {
            mesh = mesh, rest = rest, now = (Vector3[])rest.Clone(), weight = weight, phase = phase,
            swayDir = toMesh.MultiplyVector(sway), flutterDir = toMesh.MultiplyVector(flutter), kind = kind, group = group,
        };
        pool[(source, kind, group)] = shared;
        return shared;
    }

    private void Update()
    {
        if (parts == null)
            return;
        foreach (Shared s in parts)
            if (s != null)
                Bend(s);
    }

    private static void Bend(Shared s)
    {
        int frame = Time.frameCount;
        if (s.frame == frame)
            return;
        // Cloth and leaves at half rate (staggered by group); wings every frame
        if (s.kind != Kind.Wings && ((frame + s.group) & 1) != 0)
            return;
        s.frame = frame;
        float t = Time.time, g = s.group * 2.1f;
        Vector3[] rest = s.rest, now = s.now;
        float[] w = s.weight, ph = s.phase;
        switch (s.kind)
        {
            case Kind.Tree:
            {
                float gust = 0.6f + 0.7f * Mathf.PerlinNoise(t * 0.15f, g);
                float bend = (Mathf.Sin(t * 0.8f + g) * 0.7f + Mathf.Sin(t * 1.9f + g * 1.3f) * 0.3f) * gust;
                Vector3 sway = s.swayDir * bend, flutter = s.flutterDir * gust;
                for (int i = 0; i < rest.Length; i++)
                    now[i] = rest[i] + (sway + flutter * Mathf.Sin(t * 5.5f + ph[i])) * w[i];
                break;
            }
            case Kind.Banner:
            {
                float bend = Mathf.Sin(t * 0.7f + g);
                Vector3 sway = s.swayDir * bend;
                for (int i = 0; i < rest.Length; i++)
                    now[i] = rest[i] + (sway + s.flutterDir * Mathf.Sin(t * 3.4f - ph[i] + g)) * w[i];
                break;
            }
            default:
            {
                float beat = s.group < FlapGroups ? Mathf.Sin(t * 9f + s.group * 2.09f)
                    : s.group >= FastGroups ? Mathf.Sin(t * 17f + (s.group - FastGroups) * 2.09f)
                    : 0.18f;
                Vector3 lift = s.flutterDir * beat;
                for (int i = 0; i < rest.Length; i++)
                    now[i] = rest[i] + lift * w[i];
                break;
            }
        }
        s.mesh.SetVertices(now);
    }
}
