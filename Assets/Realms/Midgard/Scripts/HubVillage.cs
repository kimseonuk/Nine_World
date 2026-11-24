using System;
using UnityEngine;
using Random = UnityEngine.Random;

// The village in Midgard where every journey starts and ends: Brokkr's forge, a few longhouses
// round a campfire, and the Bifrost gate north of the green that opens the run.
// It sits far from the dungeon's zones and is built once, then shown or hidden.
public class HubVillage : MonoBehaviour
{
    private static readonly Vector3 Origin = new Vector3(5000f, 0f, 5000f);
    private const float HalfX = 22f, HalfZ = 17f;
    private const float ForgeReach = 3.2f;

    [Header("VARCO models (simple shapes stand in for any that are missing)")]
    [SerializeField] private GameObject forgePrefab;
    [SerializeField] private GameObject anvilPrefab;
    [SerializeField] private GameObject longhousePrefab;
    [SerializeField] private GameObject smithPrefab;
    [SerializeField] private GameObject gatePrefab;
    [SerializeField] private GameObject brazierPrefab;
    [SerializeField] private Texture2D grassTexture;

    // The village's art by name, for the opening
    public GameObject Prop(string name) => name switch
    {
        "longhouse" => longhousePrefab, "forge" => forgePrefab, "anvil" => anvilPrefab, "smith" => smithPrefab, _ => null,
    };
    [SerializeField] private float grassTileSize = 6f;

    private Transform root;
    private Portal portal;
    private Vector3 anvilSpot, gateSpot;

    public Vector3 SpawnPosition => Origin + new Vector3(0f, 1.1f, -9f);
    public Quaternion SpawnRotation => Quaternion.identity;

    public bool NearForge(Vector3 p) => root != null && root.gameObject.activeSelf && Flat(p - anvilSpot) < ForgeReach;
    public bool NearGate(Vector3 p) => root != null && root.gameObject.activeSelf && Flat(p - gateSpot) < 5f;
    public bool NearArmory(Vector3 p) => root != null && root.gameObject.activeSelf && hasArmory && Flat(p - armorySpot) < 3.2f;
    public bool NearTraining(Vector3 p) => root != null && root.gameObject.activeSelf && Flat(p - trainingSpot) < 3f;

    private Vector3 trainingSpot;

    // 훈련장 portal on the east of the green, between the longhouses, facing the campfire: the VARCO rune-stone gate
    // (Resources/Props/TrainingGate) when it's in, otherwise a pair of standing stones with a lintel; blue runes turn
    // on the ground in front and a cold light marks it from across the green
    private void TrainingGate()
    {
        var spot = new Vector3(9.5f, 0f, -1f);
        const float yaw = -90f;   // (its face toward -x: the green)
        trainingSpot = Origin + spot + new Vector3(-2f, 0f, 0f);
        GameObject model = Resources.Load<GameObject>("Props/TrainingGate");
        if (model != null)
        {
            GameObject gate = Place(model, spot, yaw, true);
            gate.name = "TrainingGate";
        }
        else
        {
            var g = new GameObject("TrainingGate").transform;
            g.SetParent(root, false);
            g.localPosition = spot;
            g.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Material stone = Mats.Get("hub_runestone", new Color(0.5f, 0.52f, 0.56f), 0.2f);
            Mats.Shape(PrimitiveType.Cube, g, new Vector3(-1.3f, 1.4f, 0f), new Vector3(0.7f, 2.8f, 0.7f), stone, true);
            Mats.Shape(PrimitiveType.Cube, g, new Vector3(1.3f, 1.4f, 0f), new Vector3(0.7f, 2.8f, 0.7f), stone, true);
            Mats.Shape(PrimitiveType.Cube, g, new Vector3(0f, 3f, 0f), new Vector3(3.6f, 0.55f, 0.85f), stone, true);
            Material glow = Mats.Get("hub_trainportal", new Color(0.35f, 0.7f, 1f), 0.5f, new Color(0.4f, 0.8f, 1.6f));
            Mats.Shape(PrimitiveType.Quad, g, new Vector3(0f, 1.35f, 0f), new Vector3(1.9f, 2.6f, 1f), glow, false);
        }
        VfxPiece runes = Vfx.Decal(Origin + spot + new Vector3(-2f, 0.05f, 0f), 3.2f, new Color(0.45f, 0.8f, 1f), "runes", 25f);
        runes.transform.SetParent(root, true);
        FireLight(spot + new Vector3(-1.2f, 2.2f, 0f), new Color(0.5f, 0.8f, 1f), 7f, 1.8f);
    }

    private Vector3 armorySpot;
    private bool hasArmory;

    // Show the village and open a fresh Bifrost gate that starts the run when stepped into
    public void Show(Transform player, Action startRun)
    {
        if (root == null)
            Build(player);
        root.gameObject.SetActive(true);
        if (portal != null)
            Destroy(portal.gameObject);
        portal = Portal.Create(gateSpot, player, startRun);
        portal.transform.SetParent(root, true);
    }

    public void Hide()
    {
        if (root != null)
            root.gameObject.SetActive(false);
    }

    // Warm late-afternoon light over the meadow, fading into a soft green-gold haze
    public void ApplyAtmosphere(Light sun, Camera cam)
    {
        var haze = new Color(0.55f, 0.62f, 0.5f);
        if (sun != null)
        {
            sun.color = new Color(1f, 0.9f, 0.75f);
            sun.intensity = 1.35f;
        }
        RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.4f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = haze;
        RenderSettings.fogStartDistance = 16f;
        RenderSettings.fogEndDistance = 38f;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = haze;
        }
    }

    private void Build(Transform player)
    {
        root = new GameObject("MidgardVillage").transform;
        root.SetParent(transform, false);
        root.position = Origin;

        BuildGround();
        BuildEdgeWalls();

        // Brokkr's forge on the west of the green, open side facing the middle; his anvil in front of it
        PlaceOr(forgePrefab, new Vector3(-14f, 0f, 3f), 90f, () => StandInHut(new Vector3(-14f, 0f, 3f), 90f));
        FireLight(new Vector3(-14.2f, 1.2f, 2.2f), new Color(1f, 0.5f, 0.2f), 9f, 3f);
        anvilSpot = Origin + new Vector3(-9f, 0f, 2f);
        PlaceOr(anvilPrefab, new Vector3(-9f, 0f, 2f), 90f,
            () => Mats.Shape(PrimitiveType.Cube, root, new Vector3(-9f, 0.45f, 2f), new Vector3(0.9f, 0.9f, 0.5f), Mats.Get("hub_iron", new Color(0.25f, 0.25f, 0.28f), 0.6f), true, "Anvil"));
        if (smithPrefab != null)
        {
            // He works the anvil from its far side, facing the green
            GameObject smith = Place(smithPrefab, new Vector3(-10.1f, 0f, 2f), 90f, true);
            smith.AddComponent<Blacksmith>().Init(player, anvilSpot + Vector3.up * 1.05f);
        }
        // A smoking brazier and a weapon rack mark the shop from across the green
        Brazier(new Vector3(-8.5f, 0f, 5.5f));
        WeaponRack(new Vector3(-8f, 0f, -1.4f), 0f);

        // Longhouses on the east, and one tucked in the north-west
        PlaceOr(longhousePrefab, new Vector3(14f, 0f, 6f), -90f, () => StandInHut(new Vector3(14f, 0f, 6f), -90f));
        PlaceOr(longhousePrefab, new Vector3(13f, 0f, -8f), -100f, () => StandInHut(new Vector3(13f, 0f, -8f), -100f));
        PlaceOr(longhousePrefab, new Vector3(-15f, 0f, -10f), 70f, () => StandInHut(new Vector3(-15f, 0f, -10f), 70f));

        Campfire(new Vector3(2f, 0f, -1f));

        // The Bifrost gate on the north side of the green, with braziers either side
        gateSpot = Origin + new Vector3(0f, 0f, 12.5f);
        if (gatePrefab != null)
        {
            // Wide and low, like the dungeon's gates, so it frames the way out without towering over the camera
            GameObject gate = Place(gatePrefab, new Vector3(0f, 0f, 13.3f), 0f, false);
            gate.transform.localScale = Vector3.Scale(gate.transform.localScale, new Vector3(1.2f, 0.6f, 1f));
        }
        Brazier(new Vector3(-3.6f, 0f, 11.5f));
        Brazier(new Vector3(3.6f, 0f, 11.5f));
        BifrostRoad();

        Fences();
        Trees();
        Flowers();

        // The armory (VARCO) in the north-west, open side toward the green: F in front of it to change weapons
        GameObject armory = Ambient.Model("Armory");
        if (armory != null)
        {
            var shed = new Vector3(-12.5f, 0f, 11.5f);
            GameObject building = Place(armory, shed, 135f, true);
            armorySpot = Origin + shed + building.transform.forward * 3.2f;
            hasArmory = true;
            FireLight(shed + building.transform.forward * 2.2f + Vector3.up * 2.4f, new Color(1f, 0.75f, 0.45f), 7f, 1.6f);
        }

        TrainingGate();

        // The village lives: a mill pond turning its wheel, lanterns by the path, chimney smoke, leaves, butterflies, ravens
        Ambient.MillPond(root, new Vector3(-5.5f, 0f, -9.5f), 20f);
        Ambient.LanternPost(root, new Vector3(2f, 0f, 6f), 200f, new Color(1f, 0.7f, 0.35f));
        Ambient.LanternPost(root, new Vector3(3.8f, 0f, -7.5f), 160f, new Color(1f, 0.7f, 0.35f));
        Ambient.Animate(root, 0, false, new Vector2(HalfX * 2f, HalfZ * 2f));
        Ambient.Weather(root, 0, false, new Vector2(HalfX * 2f, HalfZ * 2f));
    }

    // ───────────── Ground ─────────────

    private void BuildGround()
    {
        Material grass;
        if (grassTexture != null)
        {
            grass = Mats.Get("hub_grass", new Color(0.95f, 1f, 0.92f), 0.1f);
            grass.SetTexture("_BaseMap", grassTexture);
            grass.SetTextureScale("_BaseMap", new Vector2(130f, 130f) / grassTileSize);
        }
        else
            grass = Mats.Get("hub_grass_plain", new Color(0.36f, 0.55f, 0.27f), 0.1f);
        Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, -0.11f, 0f), new Vector3(130f, 0.2f, 130f), grass, true, "Ground");

        // Flagstone paths: from where the player arrives, past the campfire, up to the gate, and across to the forge
        Material[] flags =
        {
            Mats.Get("hub_flag_a", new Color(0.56f, 0.54f, 0.5f), 0.12f),
            Mats.Get("hub_flag_b", new Color(0.48f, 0.47f, 0.45f), 0.12f),
            Mats.Get("hub_flag_c", new Color(0.6f, 0.56f, 0.48f), 0.12f),
        };
        for (float z = -13f; z <= 12.5f; z += 0.8f)
        {
            float bend = Mathf.Sin(z * 0.22f) * 0.7f;
            Flagstone(new Vector3(bend + Random.Range(-0.55f, -0.25f), 0f, z + Random.Range(-0.1f, 0.1f)), flags);
            Flagstone(new Vector3(bend + Random.Range(0.25f, 0.55f), 0f, z + 0.4f + Random.Range(-0.1f, 0.1f)), flags);
        }
        for (float x = -8.2f; x <= -0.8f; x += 0.8f)
        {
            Flagstone(new Vector3(x, 0f, 1.25f + Random.Range(-0.15f, 0.15f)), flags);
            Flagstone(new Vector3(x + 0.4f, 0f, 2.05f + Random.Range(-0.15f, 0.15f)), flags);
        }
    }

    // One flat, slightly irregular stone set into the grass
    private void Flagstone(Vector3 local, Material[] mats)
    {
        float s = Random.Range(0.55f, 0.8f);
        Mats.Shape(PrimitiveType.Cylinder, root, local + Vector3.up * 0.01f, new Vector3(s, 0.012f, s * Random.Range(0.75f, 1f)), mats[Random.Range(0, mats.Length)])
            .transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
    }

    private void BuildEdgeWalls()
    {
        void Wall(Vector3 center, Vector3 size)
        {
            var wall = new GameObject("EdgeWall");
            wall.transform.SetParent(root, false);
            wall.transform.localPosition = center + Vector3.up * 2f;
            wall.AddComponent<BoxCollider>().size = size;
        }
        Wall(new Vector3(0f, 0f, HalfZ + 0.5f), new Vector3(HalfX * 2f + 2f, 4f, 1f));
        Wall(new Vector3(0f, 0f, -HalfZ - 0.5f), new Vector3(HalfX * 2f + 2f, 4f, 1f));
        Wall(new Vector3(HalfX + 0.5f, 0f, 0f), new Vector3(1f, 4f, HalfZ * 2f + 2f));
        Wall(new Vector3(-HalfX - 0.5f, 0f, 0f), new Vector3(1f, 4f, HalfZ * 2f + 2f));
    }

    // ───────────── Buildings and props ─────────────

    private GameObject Place(GameObject prefab, Vector3 local, float yaw, bool solid)
    {
        GameObject go = Instantiate(prefab, root);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        if (!solid)
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
                Destroy(c);
        return go;
    }

    private void PlaceOr(GameObject prefab, Vector3 local, float yaw, Action standIn)
    {
        if (prefab != null)
            Place(prefab, local, yaw, true);
        else
            standIn();
    }

    // A plain timber hut for when a building model is missing
    private void StandInHut(Vector3 local, float yaw)
    {
        var hut = new GameObject("Hut").transform;
        hut.SetParent(root, false);
        hut.localPosition = local;
        hut.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Mats.Shape(PrimitiveType.Cube, hut, new Vector3(0f, 1.2f, 0f), new Vector3(4f, 2.4f, 7f), Mats.Get("hub_timber", new Color(0.4f, 0.27f, 0.16f)), true);
        Mats.Shape(PrimitiveType.Cube, hut, new Vector3(0f, 2.9f, 0f), new Vector3(3.2f, 1.2f, 7.4f), Mats.Get("hub_turf", new Color(0.3f, 0.5f, 0.22f)), true)
            .transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    private void Brazier(Vector3 local)
    {
        if (brazierPrefab != null)
            Place(brazierPrefab, local, Random.Range(0f, 360f), true);
        else
            Mats.Shape(PrimitiveType.Cylinder, root, local + Vector3.up * 0.5f, new Vector3(0.9f, 0.5f, 0.9f), Mats.Get("hub_iron", new Color(0.25f, 0.25f, 0.28f), 0.6f), true, "Brazier");
        FireLight(local + Vector3.up * 1.6f, new Color(1f, 0.62f, 0.28f), 7f, 2f);
    }

    private void FireLight(Vector3 local, Color color, float range, float intensity)
    {
        var glow = new GameObject("Firelight");
        glow.transform.SetParent(root, false);
        glow.transform.localPosition = local;
        var light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        glow.AddComponent<FlickerLight>();
        GameAudio.Loop(glow, "fire_loop", 0.4f, 9f);
    }

    // A stone ring with crossed logs and a flickering flame, the heart of the village
    private void Campfire(Vector3 local)
    {
        GameObject pit = Ambient.Model("Campfire");
        if (pit != null)
        {
            ModelCampfire(local, pit);
            return;
        }
        Material stone = Mats.Get("hub_stone", new Color(0.5f, 0.5f, 0.52f), 0.15f);
        Material log = Mats.Get("hub_log", new Color(0.36f, 0.24f, 0.14f));
        Material flame = Mats.Get("hub_flame", new Color(1f, 0.55f, 0.15f), 0.2f, new Color(3f, 1.2f, 0.2f));
        Material ember = Mats.Get("hub_ember", new Color(1f, 0.85f, 0.3f), 0.2f, new Color(3f, 2.2f, 0.6f));
        for (int i = 0; i < 10; i++)
        {
            float a = i * 36f * Mathf.Deg2Rad;
            Mats.Shape(PrimitiveType.Cube, root, local + new Vector3(Mathf.Cos(a) * 1.05f, 0.15f, Mathf.Sin(a) * 1.05f), new Vector3(0.45f, 0.3f, 0.35f), stone, true)
                .transform.localRotation = Quaternion.Euler(Random.Range(-8f, 8f), -i * 36f + Random.Range(-10f, 10f), 0f);
        }
        for (int i = 0; i < 4; i++)
            Mats.Shape(PrimitiveType.Cylinder, root, local + Vector3.up * 0.25f, new Vector3(0.18f, 0.6f, 0.18f), log)
                .transform.localRotation = Quaternion.Euler(70f, i * 45f, 0f);
        var fire = new GameObject("Flames").transform;
        fire.SetParent(root, false);
        fire.localPosition = local + Vector3.up * 0.35f;
        Mats.Shape(PrimitiveType.Sphere, fire, Vector3.zero, new Vector3(0.7f, 0.9f, 0.7f), flame);
        Mats.Shape(PrimitiveType.Sphere, fire, Vector3.up * 0.15f, new Vector3(0.38f, 0.6f, 0.38f), ember);
        fire.gameObject.AddComponent<FlameFlicker>();
        FireLight(local + Vector3.up * 1.2f, new Color(1f, 0.6f, 0.25f), 9f, 2.6f);

        // Log benches round the fire, lying across the circle rather than pointing at it
        for (int i = 0; i < 3; i++)
        {
            float deg = i * 120f + 30f;
            float a = deg * Mathf.Deg2Rad;
            Mats.Shape(PrimitiveType.Cylinder, root, local + new Vector3(Mathf.Cos(a) * 2.6f, 0.25f, Mathf.Sin(a) * 2.6f), new Vector3(0.5f, 0.75f, 0.5f), log, true, "Bench")
                .transform.localRotation = Quaternion.Euler(90f, -deg, 0f);
        }
    }

    // The modelled fire pit (VARCO): a roaring fire over its embers, sparks popping off the logs now and then,
    // a warm glow on the ground, and log benches with furs round it
    private void ModelCampfire(Vector3 local, GameObject pit)
    {
        GameObject fire = Place(pit, local, Random.Range(0f, 360f), true);
        Bounds b = fire.GetComponentInChildren<Renderer>().bounds;
        Vector3 top = root.InverseTransformPoint(new Vector3(b.center.x, b.min.y + b.size.y * 0.45f, b.center.z));
        Ambient.Flame(root, top, 1.5f).AddComponent<FirePops>();
        Ambient.Flame(root, top + new Vector3(0.3f, -0.05f, 0.15f), 0.8f, false);
        Ambient.Flame(root, top + new Vector3(-0.25f, -0.05f, -0.2f), 0.7f, false);
        VfxPiece glow = Vfx.Decal(root.TransformPoint(local + Vector3.up * 0.03f), 5f, new Color(1f, 0.5f, 0.15f) * 0.45f, "glow", 0f);
        glow.transform.SetParent(root, true);
        glow.gameObject.AddComponent<GlowFlicker>().Init(0.3f, 5f);
        FireLight(local + Vector3.up * 1.2f, new Color(1f, 0.6f, 0.25f), 10f, 2.8f);

        GameObject bench = Ambient.Model("LogBench");
        for (int i = 0; i < 3; i++)
        {
            float deg = i * 120f + 30f;
            float a = deg * Mathf.Deg2Rad;
            Vector3 p = local + new Vector3(Mathf.Cos(a) * 2.7f, 0f, Mathf.Sin(a) * 2.7f);
            // Lying across the circle, its seat facing the fire
            if (bench != null)
                Place(bench, p, -deg, true);
            else
                Mats.Shape(PrimitiveType.Cylinder, root, p + Vector3.up * 0.25f, new Vector3(0.5f, 0.75f, 0.5f), Mats.Get("hub_log", new Color(0.36f, 0.24f, 0.14f)), true, "Bench")
                    .transform.localRotation = Quaternion.Euler(90f, -deg, 0f);
        }
    }

    private void WeaponRack(Vector3 local, float yaw)
    {
        var rack = new GameObject("WeaponRack").transform;
        rack.SetParent(root, false);
        rack.localPosition = local;
        rack.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Material wood = Mats.Get("hub_log", new Color(0.36f, 0.24f, 0.14f));
        Material steel = Mats.Get("hub_steel", new Color(0.75f, 0.77f, 0.8f), 0.85f);
        foreach (float x in new[] { -0.9f, 0.9f })
            Mats.Shape(PrimitiveType.Cube, rack, new Vector3(x, 0.7f, 0f), new Vector3(0.12f, 1.4f, 0.12f), wood, true);
        Mats.Shape(PrimitiveType.Cube, rack, new Vector3(0f, 1.2f, 0f), new Vector3(2f, 0.1f, 0.12f), wood);
        for (int i = 0; i < 4; i++)
        {
            // Blades leaning on the rail, points up
            var blade = Mats.Shape(PrimitiveType.Cube, rack, new Vector3(-0.6f + i * 0.4f, 0.75f, 0.12f), new Vector3(0.09f, 1.3f, 0.02f), steel);
            blade.transform.localRotation = Quaternion.Euler(-12f, 0f, Random.Range(-4f, 4f));
            Mats.Shape(PrimitiveType.Cube, rack, new Vector3(-0.6f + i * 0.4f, 0.18f, 0.25f), new Vector3(0.28f, 0.05f, 0.05f), wood);
        }
    }

    // A strip of the rainbow bridge leading into the gate
    private void BifrostRoad()
    {
        Color[] bands = { new Color(1f, 0.3f, 0.3f), new Color(1f, 0.65f, 0.25f), new Color(1f, 0.95f, 0.35f), new Color(0.35f, 1f, 0.5f), new Color(0.35f, 0.6f, 1f), new Color(0.75f, 0.4f, 1f) };
        for (int b = 0; b < bands.Length; b++)
            Mats.Shape(PrimitiveType.Cube, root, new Vector3((b - 2.5f) * 0.32f, 0.012f, 15.5f), new Vector3(0.32f, 0.01f, 6f),
                Mats.Get("hub_bifrost_" + b, bands[b], 0.6f, bands[b] * 1.6f));
    }

    // Low split-rail fences round the longhouses' yards
    private void Fences()
    {
        Material wood = Mats.Get("hub_fence", new Color(0.45f, 0.32f, 0.2f));
        void Run(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            int posts = Mathf.Max(1, Mathf.RoundToInt(d.magnitude / 1.6f));
            for (int i = 0; i <= posts; i++)
                Mats.Shape(PrimitiveType.Cube, root, Vector3.Lerp(from, to, i / (float)posts) + Vector3.up * 0.45f, new Vector3(0.14f, 0.9f, 0.14f), wood, true, "FencePost");
            Quaternion rot = Quaternion.LookRotation(d);
            foreach (float h in new[] { 0.35f, 0.7f })
                Mats.Shape(PrimitiveType.Cube, root, (from + to) / 2f + Vector3.up * h, new Vector3(0.07f, 0.09f, d.magnitude), wood)
                    .transform.localRotation = rot;
        }
        Run(new Vector3(9f, 0f, 1f), new Vector3(9f, 0f, 11f));
        Run(new Vector3(9f, 0f, 11f), new Vector3(19f, 0f, 11f));
        Run(new Vector3(8.5f, 0f, -3.5f), new Vector3(8.5f, 0f, -13f));
        Run(new Vector3(-19f, 0f, -5f), new Vector3(-11f, 0f, -5f));
    }

    // Stylized pines all round the green and out into the haze, so the village has no edge
    private void Trees()
    {
        Material bark = Mats.Get("hub_bark", new Color(0.38f, 0.26f, 0.16f));
        Material[] needles =
        {
            Mats.Get("hub_pine_a", new Color(0.18f, 0.38f, 0.2f)),
            Mats.Get("hub_pine_b", new Color(0.22f, 0.45f, 0.22f)),
            Mats.Get("hub_pine_c", new Color(0.15f, 0.32f, 0.2f)),
        };
        for (int i = 0; i < 160; i++)
        {
            var p = new Vector3(Random.Range(-60f, 60f), 0f, Random.Range(-60f, 60f));
            bool inField = Mathf.Abs(p.x) < HalfX - 1f && Mathf.Abs(p.z) < HalfZ - 1f;
            bool onRoad = Mathf.Abs(p.x) < 4f && p.z > 10f;
            if (inField || onRoad)
                continue;
            float s = Random.Range(0.8f, 1.5f);
            bool solid = Mathf.Abs(p.x) < HalfX + 2f && Mathf.Abs(p.z) < HalfZ + 2f;
            Mats.Shape(PrimitiveType.Cylinder, root, p + Vector3.up * 0.7f * s, new Vector3(0.35f, 0.7f, 0.35f) * s, bark, solid, "Pine");
            Material leaf = needles[Random.Range(0, needles.Length)];
            for (int k = 0; k < 3; k++)
                Mats.Shape(PrimitiveType.Sphere, root, p + Vector3.up * (1.4f + k * 0.85f) * s, new Vector3(2.3f - k * 0.6f, 1.1f, 2.3f - k * 0.6f) * s, leaf);
        }
        // A few boulders by the edge
        Material stone = Mats.Get("hub_stone", new Color(0.5f, 0.5f, 0.52f), 0.15f);
        for (int i = 0; i < 18; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            var p = new Vector3(Mathf.Cos(a) * (HalfX + Random.Range(0f, 6f)), 0f, Mathf.Sin(a) * (HalfZ + Random.Range(0f, 6f)));
            // Rounded, half-sunk stones with a smaller one leaning on each
            float s = Random.Range(0.7f, 1.5f);
            Mats.Shape(PrimitiveType.Sphere, root, p + Vector3.up * s * 0.15f, new Vector3(s * 1.2f, s * 0.7f, s), stone)
                .transform.localRotation = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0f, 180f), Random.Range(-10f, 10f));
            Mats.Shape(PrimitiveType.Sphere, root, p + new Vector3(s * 0.55f, s * 0.1f, s * 0.3f), new Vector3(s * 0.55f, s * 0.4f, s * 0.5f), stone);
        }
    }

    // Tufts of flowers on the green, away from the paths
    private void Flowers()
    {
        Material[] petals =
        {
            Mats.Get("hub_flower_w", new Color(0.95f, 0.95f, 0.9f), 0.3f),
            Mats.Get("hub_flower_y", new Color(1f, 0.85f, 0.3f), 0.3f),
            Mats.Get("hub_flower_b", new Color(0.55f, 0.65f, 1f), 0.3f),
        };
        for (int i = 0; i < 90; i++)
        {
            var p = new Vector3(Random.Range(-HalfX, HalfX), 0.06f, Random.Range(-HalfZ, HalfZ));
            if (Mathf.Abs(p.x) < 2.5f || (p.z > 0f && p.z < 3f && p.x < 0f))
                continue;
            Mats.Shape(PrimitiveType.Sphere, root, p, Vector3.one * Random.Range(0.1f, 0.16f), petals[Random.Range(0, petals.Length)]);
        }
    }

    private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;
}

// The campfire's flames sway and pulse
public class FlameFlicker : MonoBehaviour
{
    private Vector3 baseScale;
    private float seed;

    private void Awake()
    {
        baseScale = transform.localScale;
        seed = Random.Range(0f, 10f);
    }

    private void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * 4f);
        transform.localScale = new Vector3(baseScale.x * (0.85f + 0.3f * n), baseScale.y * (0.8f + 0.5f * n), baseScale.z * (0.85f + 0.3f * n));
        transform.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 3f + seed) * 6f, Time.time * 40f, 0f);
    }
}
