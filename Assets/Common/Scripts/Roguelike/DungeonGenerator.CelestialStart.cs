using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ── Asgard's first hall: the celestial gate ──
// The run begins on a platform of white marble floating over a sea of sunset-lit clouds. A grey stone walkway runs from
// the rune circle to a great white-and-gold gate with closed doors at the far side; slender pillars crowned with gold
// braziers flank the way, heavy tiered drums and round colonnaded towers stand round the edges, broken columns and a
// ruined arch lie about, and further out temples and ruins rise from floating islands over the clouds. Gold motes drift
// in the warm air. (Every model is a VARCO piece in Resources/Celestial; anything missing is simply left out.)
public partial class DungeonGenerator
{
    private static readonly Vector2 CelestialSize = new Vector2(30f, 30f);

    private static GameObject celGate, celCrownPillar, celRotunda, celDrum, celTemple, celBroken, celCapital, celScroll,
        celBrazier, celIsle, celRuinArch, celLantern, celCloudBank, celCloudTower, celCloudCluster, celCloudWisp;
    private static Texture2D celFloor;
    private static bool celLoaded;

    private static void LoadCelestial()
    {
        if (celLoaded)
            return;
        celLoaded = true;
        GameObject L(string n) => Resources.Load<GameObject>("Celestial/" + n);
        celGate = L("CelestialGate"); celCrownPillar = L("CrownPillar"); celRotunda = L("RotundaTower"); celDrum = L("DrumTower");
        celTemple = L("TempleRotunda"); celBroken = L("BrokenColumn"); celCapital = L("FallenCapital"); celScroll = L("GoldScroll");
        celBrazier = L("SunBrazier"); celIsle = L("FloatingIsle"); celRuinArch = L("RuinArch"); celLantern = L("GoldLantern");
        celCloudBank = L("CloudBankGold"); celCloudTower = L("CloudTowerGold"); celCloudCluster = L("CloudCluster"); celCloudWisp = L("CloudWispGold");
        celFloor = Resources.Load<Texture2D>("Celestial/CelestialFloor");
        if (celFloor != null)
            themeFloors[HallTheme.CelestialGate] = celFloor;
    }

    // The first hall of every area of the route in Asgard (the run's start and each area reached from the route map)
    private bool CelestialStartZone(Room.Kind kind) => kind == Room.Kind.Start && palace && ground != null;

    private static readonly Color SunGold = new Color(1f, 0.82f, 0.5f);

    private void CelestialStart(Room room, List<(Vector3 p, float r)> placed)
    {
        LoadCelestial();
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        room.GetComponent<HeavenSky>()?.Sunset();

        // ── Which side the gate stands on: the far side if it is free, else a side wall (never the camera's) ──
        Vector2Int gateSide = Vector2Int.zero;
        foreach (Vector2Int d in new[] { Vector2Int.up, Vector2Int.left, Vector2Int.right })
        {
            if (zoneExits.Contains(d))
                continue;
            gateSide = d;
            break;
        }
        bool hasGate = gateSide != Vector2Int.zero;
        if (!hasGate)
            gateSide = Vector2Int.up;   // (the walkway and pillars still run that way; the gate itself is left out)
        Vector3 n = new Vector3(gateSide.x, 0f, gateSide.y);
        Vector3 s = Vector3.Cross(Vector3.up, n);
        float H = gateSide.y != 0 ? hz : hx;     // half the floor along the walkway
        float W = gateSide.y != 0 ? hx : hz;     // half the floor across it
        float faceIn = Quaternion.LookRotation(-n).eulerAngles.y;
        TurnPaintedFloor(t, gateSide);

        // the rune circle the run starts on: two thin rings of gold laid in the stone
        FlatMesh(t, "StartRing", AnnulusMesh(2.05f, 2.2f, 96), 0.014f, SoftGold);
        FlatMesh(t, "StartRingInner", AnnulusMesh(1.45f, 1.53f, 96), 0.014f, SoftGold);

        // ── The walkway: grey stone slabs edged in gold from the circle to the gate ──
        float walkFrom = 2.4f, walkTo = H - (hasGate ? 1.6f : 0.6f);
        if (celFloor == null)
        {
            var walk = new GameObject("Walkway").transform;
            walk.SetParent(t, false);
            walk.localRotation = Quaternion.LookRotation(n);
            Material slab = Mats.Get("cel_walk", new Color(0.66f, 0.64f, 0.66f), 0.25f);
            for (float z = walkFrom; z < walkTo; z += 1.6f)
                Mats.Shape(PrimitiveType.Cube, walk, new Vector3(0f, 0.012f, z + 0.78f), new Vector3(4.2f, 0.01f, 1.5f), slab, false, "Slab");
            foreach (float side in new[] { -1f, 1f })
                Mats.Shape(PrimitiveType.Cube, walk, new Vector3(side * 2.2f, 0.014f, (walkFrom + walkTo) / 2f), new Vector3(0.14f, 0.01f, walkTo - walkFrom), GoldInlay, false, "WalkGold");
        }
        placed.Add((n * (walkFrom + walkTo) / 2f, 1.2f));   // (the walk itself is kept clear)
        for (float z = walkFrom; z < walkTo; z += 2f)
            placed.Add((n * z, 2.1f));

        // ── The gate, its gold scrolls and its glow ──
        if (hasGate)
        {
            Vector3 gate = n * (H - 1.5f);
            if (celGate != null)
                PlaceSized(celGate, t, gate, faceIn, 9.5f, true);
            else
                PalaceGateFallback(t, gate, faceIn);
            placed.Add((gate, 4.2f));
            Vfx.Glow(t, gate + Vector3.up * 4.5f - n * 0.6f, 9f, SunGold * 0.55f);
            SoftLight(t, gate + Vector3.up * 4f - n * 2.5f, SunGold, 14f, 1.8f);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 p = gate + s * side * 4.7f - n * 0.4f;
                if (celScroll != null)
                    PlaceSized(celScroll, t, p, faceIn + side * 25f, 2.4f, true);
                placed.Add((p, 1.4f));
            }
        }

        // ── Pillars crowned with gold braziers on both sides of the walk ──
        int lit = 0;
        for (float d = 4f; d < H - 3.2f; d += 4.5f)
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 p = n * d + s * side * 3.5f;
                if (!room.OnFloorLocal(new Vector2(p.x, p.z), 0.6f))
                    continue;
                float h = 6.2f;
                if (celCrownPillar != null)
                    PlaceSized(celCrownPillar, t, p, faceIn, h, true);
                else
                    PalaceColumn(t, p, 1f, true);
                placed.Add((p, 1f));
                Vfx.Glow(t, p + Vector3.up * (h * 0.96f), 1.6f, SunGold * 0.9f);
                if (lit++ % 2 == 0)
                    SoftLight(t, p + Vector3.up * (h + 0.3f), SunGold, 8f, 1.3f);
            }

        // ── Behind the circle: two sun braziers ──
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 p = -n * 2.6f + s * side * 3.4f;
            if (!Clear(room, placed, p, 0.9f))
                continue;
            if (celBrazier != null)
                PlaceSized(celBrazier, t, p, faceIn, 2f, true);
            else
                Brazier(t, p);
            placed.Add((p, 1f));
            Vfx.Glow(t, p + Vector3.up * 2.2f, 2.2f, new Color(1f, 0.7f, 0.35f));
            var fire = new GameObject("SunFire");
            fire.transform.SetParent(t, false);
            fire.transform.localPosition = p + Vector3.up * 2.2f;
            var light = fire.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.68f, 0.32f);
            light.range = 7f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
            fire.AddComponent<FlickerLight>();
        }

        // ── Round the edges: heavy tiered drums at the middle of each side, colonnaded towers in the far corners ──
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 drum = s * side * (W - 2.8f) + n * (H * 0.2f);
            if (celDrum != null && Clear(room, placed, drum, 2.2f, 0.2f))
            {
                PlaceWide(celDrum, t, drum, faceIn, 4.4f);
                AddRoundCollider(t, drum, 2f, 4f);
                placed.Add((drum, 2.4f));
            }
            Vector3 tower = s * side * (W - 2.6f) + n * (H - 3f);
            if (celRotunda != null && Clear(room, placed, tower, 2f, 0.2f))
            {
                PlaceSized(celRotunda, t, tower, faceIn, 8.5f, false);
                AddRoundCollider(t, tower, 1.8f, 8f);
                placed.Add((tower, 2.2f));
                SoftLight(t, tower + Vector3.up * 6f - n * 2f, SunGold, 9f, 1f);
            }
        }

        // ── Lanterns along the sides ──
        foreach (float d in new[] { -H * 0.45f, H * 0.55f })
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 p = n * d + s * side * (W - 1.4f);
                if (celLantern == null || !Clear(room, placed, p, 0.7f, 0.2f))
                    continue;
                PlaceSized(celLantern, t, p, faceIn, 3.8f, true);
                placed.Add((p, 0.9f));
                Vfx.Glow(t, p + Vector3.up * 3.2f, 1.4f, new Color(1f, 0.92f, 0.75f));
            }

        // ── Age: broken columns, fallen capitals and a ruined arch near the edges (low ones on the camera's side) ──
        var ruins = new List<(GameObject prefab, float height, float r)>
        {
            (celBroken, 2.4f, 1.1f), (celCapital, 1.3f, 1.2f), (celBroken, 1.8f, 1f), (celCapital, 1.1f, 1.1f), (celBroken, 2.8f, 1.1f),
        };
        foreach (var (prefab, height, r) in ruins)
        {
            if (prefab == null)
                continue;
            for (int tries = 0; tries < 30; tries++)
            {
                var p = new Vector3(Random.Range(-hx + 1.8f, hx - 1.8f), 0f, Random.Range(-hz + 1.8f, hz - 1.8f));
                // (near the edges, not out in the middle of the floor)
                if (Mathf.Abs(p.x) < hx - 5f && Mathf.Abs(p.z) < hz - 5f)
                    continue;
                if (!Clear(room, placed, p, r + 0.6f))
                    continue;
                float h = p.z < -hz * 0.4f ? Mathf.Min(height, 1.6f) : height;
                PlaceSized(prefab, t, p, Random.Range(0f, 360f), h, true);
                placed.Add((p, r));
                break;
            }
        }
        if (celRuinArch != null)
        {
            Vector3 arch = s * (Random.value < 0.5f ? -1f : 1f) * (W - 2.4f) - n * (H * 0.55f);
            if (arch.z > -hz * 0.4f && Clear(room, placed, arch, 2f, 0.2f))
            {
                PlaceSized(celRuinArch, t, arch, faceIn + 90f, 5.2f, true);
                placed.Add((arch, 2.2f));
            }
        }

        // ── Further out, over the clouds: towers behind the gate, temples and ruins on floating islands ──
        bool InView(Vector3 p) => p.z > -hz - 2f;   // (nothing tall on the camera's side)
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 big = n * (H + 9f) + s * side * (W * 0.55f) + Vector3.down * 1.5f;
            if (celRotunda != null && InView(big))
            {
                HeavenIsland(t, big, 4.6f);
                PlaceSized(celRotunda, t, big, faceIn, 18f, false);
            }
            Vector3 temple = s * side * (W + 11f) + n * (H * 0.35f) + Vector3.down * 2.5f;
            if (celTemple != null && InView(temple))
            {
                HeavenIsland(t, temple, 4.4f);
                PlaceSized(celTemple, t, temple, Quaternion.LookRotation(-new Vector3(temple.x, 0f, temple.z)).eulerAngles.y, 9f, false);
            }
            Vector3 drum = n * (H + 6f) + s * side * (W + 5f) + Vector3.down * 1f;
            if (celDrum != null && InView(drum))
            {
                HeavenIsland(t, drum, 3.4f);
                PlaceWide(celDrum, t, drum, faceIn, 5.5f);
            }
        }
        if (celTemple != null)
        {
            Vector3 far = n * (H + 22f) + Vector3.down * 3f;
            HeavenIsland(t, far, 6f);
            PlaceSized(celTemple, t, far, faceIn, 15f, false);
        }
        if (celIsle != null)
            foreach (var (along, across, up, size) in new[] { (H + 16f, -16f, 3f, 9f), (H + 15f, 17f, -1f, 8f), (H * 0.2f, -(W + 19f), 1f, 8.5f), (H * 0.1f, W + 19f, 4f, 7.5f), (H + 30f, 6f, 6f, 11f) })
            {
                Vector3 p = n * along + s * across + Vector3.up * up;
                if (!InView(p))
                    continue;
                GameObject isle = PlaceSized(celIsle, t, p, Random.Range(0f, 360f), size, false);
                isle.transform.localPosition = p;   // (hanging in the air, not stood on the floor)
                isle.AddComponent<SkyDrift>().Init(Random.Range(0.25f, 0.5f), Random.Range(0.12f, 0.2f));
            }
        if (celRuinArch != null)
        {
            Vector3 p = n * (H + 12f) - s * (W * 0.1f) + Vector3.down * 2f;
            if (InView(p))
            {
                HeavenIsland(t, p, 3.6f);
                PlaceSized(celRuinArch, t, p, faceIn, 8f, false);
            }
        }

        CelestialClouds(room, gateSide);
        GoldMotes(t, hx, hz);
        SoftLight(t, new Vector3(0f, 11f, -3f), new Color(1f, 0.86f, 0.68f), 34f, 0.9f);
    }

    // The painted floor is drawn with its walkway running north; turned so it runs toward the gate
    private static void TurnPaintedFloor(Transform t, Vector2Int gateSide)
    {
        if (gateSide == Vector2Int.up)
            return;
        Transform floor = t.Find("PaintedFloor");
        MeshFilter mf = floor != null ? floor.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null)
            return;
        Mesh mesh = mf.sharedMesh;
        var uv = mesh.uv;
        for (int i = 0; i < uv.Length; i++)
        {
            Vector2 c = uv[i] - Vector2.one * 0.5f;
            // the walkway (the texture's v) laid along x: east turns it a quarter one way, west the other
            c = gateSide == Vector2Int.right ? new Vector2(-c.y, c.x) : gateSide == Vector2Int.left ? new Vector2(c.y, -c.x) : -c;
            uv[i] = c + Vector2.one * 0.5f;
        }
        mesh.uv = uv;
    }

    // A round block of collision for a model standing on the floor
    private static void AddRoundCollider(Transform t, Vector3 p, float radius, float height)
    {
        var go = new GameObject("CelestialBlock");
        go.transform.SetParent(t, false);
        go.transform.localPosition = p;
        var col = go.AddComponent<CapsuleCollider>();
        col.radius = radius;
        col.height = height;
        col.center = Vector3.up * height / 2f;
    }

    // (only if the gate's model is missing) a plain white arch with gold doors
    private static void PalaceGateFallback(Transform t, Vector3 p, float yaw)
    {
        var root = new GameObject("GateFallback").transform;
        root.SetParent(t, false);
        root.localPosition = p;
        root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        foreach (float side in new[] { -1f, 1f })
            Mats.Shape(PrimitiveType.Cube, root, new Vector3(side * 2.4f, 4f, 0f), new Vector3(1f, 8f, 1f), HeavenMarble, true, "GatePost");
        Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, 8.4f, 0f), new Vector3(5.8f, 1f, 1.1f), HeavenMarble, true, "GateLintel");
        Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, 3.6f, 0.1f), new Vector3(3.8f, 7.2f, 0.3f), HeavenGold, true, "GateDoors");
    }

    // The sea of sunset clouds: banks pressed round the platform's edges (low on the camera's side, rising behind the
    // gate), more ringing it further down, and great towers of cloud standing far off behind
    private static void CelestialClouds(Room room, Vector2Int gateSide)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        var near = new[] { celCloudBank, celCloudCluster, celCloudWisp, celCloudBank }.Where(c => c != null).ToArray();
        if (near.Length > 0)
        {
            // walking round the edge, a bank every few metres just outside it
            float perimeter = 4f * (hx + hz);
            for (float u = 0f; u < perimeter; u += Random.Range(3.2f, 4.6f))
            {
                Vector3 edge, outward;
                float k = u;
                if (k < 2f * hx) { edge = new Vector3(-hx + k, 0f, hz); outward = Vector3.forward; }
                else if ((k -= 2f * hx) < 2f * hz) { edge = new Vector3(hx, 0f, hz - k); outward = Vector3.right; }
                else if ((k -= 2f * hz) < 2f * hx) { edge = new Vector3(hx - k, 0f, -hz); outward = Vector3.back; }
                else { k -= 2f * hx; edge = new Vector3(-hx, 0f, -hz + k); outward = Vector3.left; }
                bool cameraSide = outward == Vector3.back;
                Vector3 c = edge + outward * Random.Range(cameraSide ? 3f : 2.6f, cameraSide ? 6f : 5.5f);
                GameObject cloud = PlaceWide(near[Random.Range(0, near.Length)], t, c, Random.Range(0f, 360f), Random.Range(6f, 10f));
                Bounds b = BoundsAround(cloud);
                // (their tops: well under the floor toward the camera, at the rail's height at the sides, higher behind)
                float top = cameraSide ? -1.6f : outward == Vector3.forward ? Random.Range(0.6f, 1.8f) : Random.Range(-0.4f, 0.8f);
                cloud.transform.localPosition = new Vector3(c.x, cloud.transform.localPosition.y + top - (b.max.y), c.z);
                cloud.name = "Cloud";
                cloud.AddComponent<SkyDrift>().Init(Random.Range(0.15f, 0.3f), Random.Range(0.06f, 0.14f));
            }
            for (int i = 0; i < 40; i++)
            {
                float a = (i / 40f + Random.Range(-0.01f, 0.01f)) * Mathf.PI * 2f;
                float d = Random.Range(Mathf.Max(hx, hz) + 9f, 60f);
                var c = new Vector3(Mathf.Cos(a) * d, Random.Range(-8f, -3f), Mathf.Sin(a) * d);
                if (c.z < 0f)
                    c.y -= 3f;
                GameObject cloud = PlaceWide(near[Random.Range(0, near.Length)], t, c, Random.Range(0f, 360f), Random.Range(12f, 22f));
                cloud.transform.localPosition = c;
                cloud.name = "Cloud";
            }
        }
        if (celCloudTower != null)
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.Lerp(15f, 165f, (i + Random.value * 0.7f) / 10f) * Mathf.Deg2Rad;   // the far half only
                float d = Random.Range(48f, 72f);
                var c = new Vector3(Mathf.Cos(a) * d, Random.Range(-10f, -4f), Mathf.Sin(a) * d);
                GameObject cloud = PlaceSized(celCloudTower, t, c, Random.Range(0f, 360f), Random.Range(22f, 34f), false);
                cloud.transform.localPosition += Vector3.up * c.y;
                cloud.name = "Cloud";
            }
    }

    // Gold motes drifting slowly up through the warm air over the whole platform
    private static void GoldMotes(Transform t, float hx, float hz)
    {
        var go = new GameObject("GoldMotes");
        go.transform.SetParent(t, false);
        go.transform.localPosition = Vector3.up * 2.5f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 10f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.5f), new Color(1f, 0.95f, 0.8f));
        main.gravityModifier = -0.01f;
        main.maxParticles = 400;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 45f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(hx * 2f + 10f, 5f, hz * 2f + 10f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.3f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = Vfx.Material("glow", true);
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        ps.Play();
    }
}

// A slow drift up and down, for things hanging in the sky (clouds, floating islands)
public class SkyDrift : MonoBehaviour
{
    private float speed, amount, phase;
    private Vector3 home;

    public void Init(float speed, float amount)
    {
        this.speed = speed;
        this.amount = amount;
        phase = Random.Range(0f, Mathf.PI * 2f);
        home = transform.localPosition;
    }

    private void Update()
    {
        transform.localPosition = home + Vector3.up * Mathf.Sin(Time.time * speed * Mathf.PI * 2f * 0.25f + phase) * amount;
    }
}
