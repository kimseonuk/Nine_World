using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ── Asgard fight-hall maps (잡몹방) ──
// Every fight hall in the palace is built as one of eight maps, after the user's reference sheet:
//   ① 황금 광장  GoldenPlaza     a grand golden fountain in a sun-rayed plaza, the palace and its arcades round it
//   ② 신성한 회랑 SacredCorridor  a long hall between arched colonnades hung with crimson banners, the palace gate at its end
//   ③ 정원       Garden          a round garden court with a fountain, golden trees and flowerbeds, a pavilion beyond
//   ④ 파괴된 궁전 RuinedPalace    a shattered hall (the Ruins floor plan), broken temple fronts and rubble all round
//   ⑤ 의식의 재단 RitualAltar     a stepped altar on gold rings with a pillar of light, a colossal golden god beside it
//   ⑥ 다중 광장  MultiPlaza      round plazas on platforms joined by bridges (the Platforms plan), fountains and trees
//   ⑦ 테라스     Terrace         a raised terrace with a balustrade, stairs, a pavilion and an armillary lookout
//   ⑧ 장식 집중  Ornate          the palace interior crowded with statues, vases and candelabras
// The floors are VARCO paintings (Resources/Halls/Floors), the props VARCO models (Resources/Halls).
// Every map floats in a daylit sky over a sea of clouds (HeavenSky): nothing is built past the floor you can walk on
// (the user's call: only the playable area, the clouds and sky below it). The floor stays flat at y = 0; trees,
// statues, columns, arcade pillars and the centrepieces are solid at their foot, so the big maps have cover.
public partial class DungeonGenerator
{
    private enum HallTheme { None, GoldenPlaza, SacredCorridor, Garden, RuinedPalace, RitualAltar, MultiPlaza, Terrace, Ornate, CelestialGate }   // (CelestialGate: the run's first hall only, never dealt from the bag)

    private HallTheme hallTheme;
    private readonly List<HallTheme> themeBag = new List<HallTheme>();
    private HallTheme lastTheme;

    // Every map floats in the open sky: nothing is left but the floor you can walk on, with the clouds below
    private bool OpenAir => hallTheme != HallTheme.None;

    // How far the floor runs on past the invisible walls at its edge
    private const float FloorLip = 0.6f;

    private static GameObject lampPrefab, planterPrefab, altarPrefab, balustradePrefab, angelPrefab, topiaryPrefab,
        armillaryPrefab, brokenArchPrefab, vasePrefab, plazaDaisPrefab, urnPrefab,
        palaceBuildingPrefab, arcadePrefab, grandStairsPrefab, grandFountainPrefab, smallFountainPrefab, pavilionPrefab,
        goldStatuePrefab, ruinFrontPrefab, palaceGatePrefab, bigTreePrefab;
    private static readonly Dictionary<HallTheme, Texture2D> themeFloors = new Dictionary<HallTheme, Texture2D>();
    private static bool themePropsLoaded;

    private static void LoadThemeProps()
    {
        if (themePropsLoaded)
            return;
        themePropsLoaded = true;
        GameObject L(string n) => Resources.Load<GameObject>("Halls/" + n);
        lampPrefab = L("Lamp"); planterPrefab = L("Planter"); altarPrefab = L("Altar"); balustradePrefab = L("Balustrade");
        angelPrefab = L("AngelStatue"); topiaryPrefab = L("Topiary"); armillaryPrefab = L("Armillary"); brokenArchPrefab = L("BrokenArch");
        vasePrefab = L("Vase"); plazaDaisPrefab = L("PlazaDais");
        palaceBuildingPrefab = L("PalaceBuilding"); arcadePrefab = L("Arcade"); grandStairsPrefab = L("GrandStairs");
        grandFountainPrefab = L("GrandFountain"); smallFountainPrefab = L("SmallFountain"); pavilionPrefab = L("Pavilion");
        goldStatuePrefab = L("GoldStatue"); ruinFrontPrefab = L("RuinFront"); palaceGatePrefab = L("PalaceGate"); bigTreePrefab = L("BigTree");
        urnPrefab = Resources.Load<GameObject>("Heaven/FlowerUrn");
        for (HallTheme th = HallTheme.GoldenPlaza; th <= HallTheme.Ornate; th++)
        {
            var tex = Resources.Load<Texture2D>("Halls/Floors/" + th);
            if (tex != null)
                themeFloors[th] = tex;
        }
    }

    // Dealt from a shuffled bag, so a floor's fight halls show every map before any repeats
    private HallTheme PickTheme()
    {
        LoadThemeProps();
        LoadHallProps();
        LoadHeaven();
        if (themeBag.Count == 0)
        {
            for (HallTheme th = HallTheme.GoldenPlaza; th <= HallTheme.Ornate; th++)
                themeBag.Add(th);
            for (int i = themeBag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (themeBag[i], themeBag[j]) = (themeBag[j], themeBag[i]);
            }
            if (themeBag[themeBag.Count - 1] == lastTheme)
                (themeBag[0], themeBag[themeBag.Count - 1]) = (themeBag[themeBag.Count - 1], themeBag[0]);
        }
        HallTheme pick = themeBag[themeBag.Count - 1];
        themeBag.RemoveAt(themeBag.Count - 1);
        return lastTheme = pick;
    }

    // The floor plan each map is built on
    private static HallShape ThemeShape(HallTheme th) => th switch
    {
        HallTheme.RuinedPalace => HallShape.Ruins,
        HallTheme.MultiPlaza => HallShape.Platforms,
        _ => HallShape.Classic,
    };

    // The maps are big: the fight sweeps across them (spawns still come within reach of the player, see Room.SpawnPoint)
    private static Vector2 ThemeSize(HallTheme th) => th switch
    {
        HallTheme.SacredCorridor => new Vector2(26f, 58f),
        HallTheme.Terrace => new Vector2(46f, 36f),
        HallTheme.Ornate => new Vector2(44f, 38f),
        _ => new Vector2(46f, 42f),
    };

    // The shaped plans (Ruins, Platforms) grown by this much
    private const float ThemeShapeScale = 1.5f;

    private static void ScaleLayout(float k, ref Vector2 size, List<Rect> floor, List<Rect> voids)
    {
        size *= k;
        foreach (List<Rect> list in new[] { floor, voids })
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                    list[i] = Rect.MinMaxRect(list[i].xMin * k, list[i].yMin * k, list[i].xMax * k, list[i].yMax * k);
    }

    // Bigger waves for a bigger map (an ordinary fight zone is about 28 × 24)
    private static int ThemeBudget(Vector2 size) => Mathf.Clamp(Mathf.RoundToInt((size.x * size.y - 700f) / 600f), 0, 3);

    private Texture2D ThemeFloor => hallTheme != HallTheme.None && themeFloors.TryGetValue(hallTheme, out Texture2D tex) ? tex : null;

    // How much broken stone the map wants scattered on its floor
    private float ThemeRuinFactor() => hallTheme switch
    {
        HallTheme.None => 1f,
        HallTheme.RuinedPalace => 1.7f,
        _ => 0f,   // (the other maps are whole: their reference shows no fallen stone)
    };

    // ───────────── the painted floor ─────────────

    // The map's VARCO painting laid once over the whole floor (each piece of a shaped floor takes its part of the picture)
    private void PaintedFloor(Room room, Vector2 fieldSize, GameObject field, Texture2D tex)
    {
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        foreach (Rect r in SolidRects(room, fieldSize))
            AddQuad(verts, uvs, tris, r, 0f, q => new Vector2(q.x / fieldSize.x + 0.5f, q.y / fieldSize.y + 0.5f));
        var mesh = new Mesh { name = "PaintedFloor", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Material mat = Mats.Get("hall_painted_" + tex.name, new Color(0.97f, 0.96f, 0.95f), 0.45f);
        mat.SetTexture("_BaseMap", tex);
        FlatMesh(room.transform, "PaintedFloor", mesh, 0.005f, mat);
        field.GetComponent<Renderer>().enabled = false;
    }

    // ───────────── the open sky round the hall ─────────────

    // Daylight, the sea of clouds below, and the floor itself as a thin floating slab: nothing else past its edge
    private void OpenAirFrame(Room room, List<Vector2Int> exits)
    {
        Transform t = room.transform;
        room.gameObject.AddComponent<HeavenSky>();
        FloorSlab(room);
        Clouds(t);
        SinkClouds(room);
        SoftLight(t, new Vector3(0f, 10f, -2f), new Color(0.96f, 0.97f, 1f), 30f, 0.8f);
    }

    // Heimdall's clouds were laid out for his 17 m altar; these maps are far bigger, so a bank could rise up through
    // the floor or stand on it. Any cloud whose reach overlaps the floor (and the ways out past its edges, and the
    // camera's side) is sunk until its top is well under the floor's slab.
    private static void SinkClouds(Room room)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        // (exits' gates and paths stand ~6 m out; the camera looks over the south edge from up to ~10 m beyond)
        Rect keep = Rect.MinMaxRect(-hx - 7f, -hz - 12f, hx + 7f, hz + 7f);
        const float under = -2.2f;
        foreach (Transform c in t)
        {
            if (c.name != "Cloud")
                continue;
            var renderers = c.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                continue;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers)
                b.Encapsulate(r.bounds);
            Vector3 min = b.min - t.position, max = b.max - t.position;
            bool overlaps = max.x > keep.xMin && min.x < keep.xMax && max.z > keep.yMin && min.z < keep.yMax;
            if (overlaps && max.y > under)
                c.localPosition += Vector3.down * (max.y - under);
        }
    }

    // The walkable floor's own edge: a marble lip under it with a gold band, so from the camera's slant it reads as a
    // slab floating over the clouds rather than a sheet of paper
    private void FloorSlab(Room room)
    {
        Transform t = room.transform;
        Vector2 fieldSize = room.Size + Vector2.one * FloorLip * 2f;
        foreach (Rect r in SolidRects(room, fieldSize))
        {
            Mats.Shape(PrimitiveType.Cube, t, new Vector3(r.center.x, -0.55f, r.center.y), new Vector3(r.width, 0.9f, r.height), MarbleShade, false, "FloorSlab");
            Mats.Shape(PrimitiveType.Cube, t, new Vector3(r.center.x, -0.28f, r.center.y), new Vector3(r.width + 0.08f, 0.12f, r.height + 0.08f), GoldInlay, false, "FloorSlabBand");
        }
    }

    // ───────────── helpers inside the hall ─────────────

    private static bool Clear(Room room, List<(Vector3 p, float r)> placed, Vector3 p, float r, float margin = 0.6f)
        => room.OnFloorLocal(new Vector2(p.x, p.z), margin) && placed.All(o => Vector3.Distance(o.p, p) > o.r + r);

    private static GameObject Put(GameObject prefab, Room room, List<(Vector3 p, float r)> placed, Vector3 p, float r, float yaw, float height, bool solid = false, float margin = 0.6f)
    {
        if (prefab == null || !Clear(room, placed, p, r, margin))
            return null;
        GameObject go = PlaceSized(prefab, room.transform, p, yaw, height, solid);
        placed.Add((p, r));
        return go;
    }

    private static GameObject PutWide(GameObject prefab, Room room, List<(Vector3 p, float r)> placed, Vector3 p, float width, float yaw, bool solid, float margin = 0.6f)
    {
        if (prefab == null || !Clear(room, placed, p, width / 2f, margin))
            return null;
        GameObject go = PlaceWide(prefab, room.transform, p, yaw, width);
        if (!solid)
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
                Destroy(c);
        else
        {
            // (a round centrepiece: a cylinder-ish block of collision, not its whole bounding box)
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
                Destroy(c);
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = width * 0.45f / go.transform.localScale.x;
            col.height = 6f / go.transform.localScale.y;
            col.center = Vector3.up * col.height / 2f;
        }
        placed.Add((p, width / 2f + 0.4f));
        return go;
    }

    private static readonly Color LampColor = new Color(0.75f, 0.92f, 1f);

    private int lampCount;
    private bool Lamp(Room room, List<(Vector3 p, float r)> placed, Vector3 p, float height = 3.6f)
    {
        if (Put(lampPrefab, room, placed, p, 0.7f, FacingIn(p) + 180f, height) == null)
            return false;
        Vfx.Glow(room.transform, p + Vector3.up * (height * 0.8f), 1.1f, LampColor * 0.55f);
        if (lampCount++ % 2 == 0)
            SoftLight(room.transform, p + Vector3.up * (height * 0.8f), LampColor, 7f, 1.2f);
        return true;
    }

    // A prop sized by height standing on whatever height `local.y` is (no collision)
    private static GameObject PlaceSizedAt(GameObject prefab, Transform parent, Vector3 local, float height, float yaw = 180f)
    {
        if (prefab == null)
            return null;
        GameObject go = PlaceSized(prefab, parent, new Vector3(local.x, 0f, local.z), yaw, height, false);
        go.transform.localPosition += Vector3.up * local.y;
        return go;
    }


    // Solid only at its foot: a capsule round the trunk, base or pillar, so a canopy or roof doesn't wall off the floor
    private static void FootCollider(GameObject go, float radius, float height = 3f)
    {
        if (go == null)
            return;
        foreach (Collider c in go.GetComponentsInChildren<Collider>())
            Destroy(c);
        float s = Mathf.Max(0.001f, go.transform.localScale.x);
        var col = go.AddComponent<CapsuleCollider>();
        col.radius = radius / s;
        col.height = Mathf.Max(height, radius * 2f) / s;
        col.center = Vector3.up * col.height / 2f;
    }

    // Something to fight round: placed where nothing stands, solid at its foot
    private static GameObject PutSolid(GameObject prefab, Room room, List<(Vector3 p, float r)> placed, Vector3 p, float footRadius, float clearance, float yaw, float height, float margin = 0.6f)
    {
        GameObject go = Put(prefab, room, placed, p, clearance, yaw, height, false, margin);
        FootCollider(go, footRadius);
        return go;
    }

    // An arcade section standing in the hall: scenery only, with no collision at all, so the player and the monsters
    // walk freely through it and back between the nave and the aisle behind (its pillars, ~1 m apart with the next
    // section's right beside them, used to wall the two off)
    private static GameObject ArcadeInside(Room room, List<(Vector3 p, float r)> placed, Vector3 p, float yaw, float width)
    {
        if (arcadePrefab == null || !room.OnFloorLocal(new Vector2(p.x, p.z), 0.4f) || placed.Any(o => Vector3.Distance(o.p, p) < o.r + 1.2f))
            return null;
        GameObject go = PlaceWide(arcadePrefab, room.transform, p, yaw, width);
        foreach (Collider c in go.GetComponentsInChildren<Collider>())
            Destroy(c);
        Vector3 along = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        for (float k = -0.5f; k <= 0.5f; k += 0.25f)
            placed.Add((p + along * (k * width), 1.1f));
        return go;
    }

    // A golden tree in its planter ring, solid at the trunk (kept smaller toward the camera's side)
    private GameObject Tree(Room room, List<(Vector3 p, float r)> placed, Vector3 p, float height)
    {
        GameObject prefab = bigTreePrefab != null ? bigTreePrefab : goldenTreePrefab;
        return PutSolid(prefab, room, placed, p, 1.3f, 2.4f, Random.Range(0f, 360f), p.z < -room.Size.y * 0.2f ? height * 0.7f : height, 1f);
    }

    private void CandelabraAt(Room room, List<(Vector3 p, float r)> placed, Vector3 p)
    {
        if (candelabraPrefab == null || !Clear(room, placed, p, 0.8f))
            return;
        Place(candelabraPrefab, room.transform, p, 0f, 1f, false);
        placed.Add((p, 0.8f));
        CandleLight(room.transform, p + Vector3.up * 2.7f);
    }

    // ───────────── the maps ─────────────
    // (everything stands on the walkable floor: past its edge there is only sky)

    private void DressTheme(Room room, List<(Vector3 p, float r)> placed)
    {
        switch (hallTheme)
        {
            case HallTheme.GoldenPlaza: GoldenPlaza(room, placed); break;
            case HallTheme.SacredCorridor: SacredCorridor(room, placed); break;
            case HallTheme.Garden: ThemeGarden(room, placed); break;
            case HallTheme.RuinedPalace: RuinedPalace(room, placed); break;
            case HallTheme.RitualAltar: RitualAltar(room, placed); break;
            case HallTheme.MultiPlaza: MultiPlaza(room, placed); break;
            case HallTheme.Terrace: Terrace(room, placed); break;
            default: Ornate(room, placed); break;
        }
    }

    // ① 황금 광장: the grand fountain in the middle of the sun-rayed plaza, fountains and golden trees round it, lamps in
    // a ring, colonnades with crimson banners down the far half of either side, angels guarding the far edge
    private void GoldenPlaza(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        PutWide(grandFountainPrefab != null ? grandFountainPrefab : fountainPrefab, room, placed, Vector3.zero, 10f, 0f, true);
        SoftLight(t, Vector3.up * 3.5f, new Color(0.75f, 0.88f, 1f), 14f, 1.5f);
        for (int i = 0; i < 12; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, 15f + i * 30f, 0f) * Vector3.forward;
            Vector3 p = dir * 10.5f;
            Lamp(room, placed, p, p.z < 0f ? 3.2f : 4f);
        }
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                PutWide(smallFountainPrefab, room, placed, new Vector3(sx * hx * 0.5f, 0f, sz * hz * 0.5f), 4.2f, 0f, true, 1f);
                Tree(room, placed, new Vector3(sx * (hx - 4f), 0f, sz * (hz - 4f)), 8f);
            }
        foreach (float side in new[] { -1f, 1f })
        {
            for (float z = hz * 0.1f; z <= hz - 4f; z += 7.2f)
                ArcadeInside(room, placed, new Vector3(side * (hx - 2.2f), 0f, z + 3.6f), side > 0f ? 270f : 90f, 7.2f);
            PutSolid(angelPrefab, room, placed, new Vector3(side * 5f, 0f, hz - 2.6f), 0.9f, 1.4f, 180f, 5f);
            PutSolid(angelPrefab, room, placed, new Vector3(side * (hx * 0.5f), 0f, -hz + 3f), 0.9f, 1.4f, 0f, 3.4f);
        }
    }

    // ② 신성한 회랑: a long nave between two arcades hung with banners, side aisles behind them, the palace gate standing
    // at the far end of the nave
    private void SacredCorridor(Room room, List<(Vector3 p, float r)> placed)
    {
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        float row = hx - 4.6f;
        bool northDoor = zoneExits != null && zoneExits.Contains(Vector2Int.up);
        if (!northDoor && palaceGatePrefab != null)
        {
            GameObject gate = PutWide(palaceGatePrefab, room, placed, new Vector3(0f, 0f, hz - 1.4f), 9f, 180f, false);
            if (gate != null)
                foreach (float s in new[] { -1f, 1f })
                {
                    var tower = new GameObject("GateTower");
                    tower.transform.SetParent(room.transform, false);
                    tower.transform.localPosition = new Vector3(s * 3.4f, 0f, hz - 1.4f);
                    var col = tower.AddComponent<CapsuleCollider>();
                    col.radius = 1.2f; col.height = 6f; col.center = Vector3.up * 3f;
                }
        }
        foreach (float side in new[] { -1f, 1f })
        {
            for (float z = -hz + 4f; z <= hz - 4f; z += 7f)
                ArcadeInside(room, placed, new Vector3(side * row, 0f, z + 3.5f), side > 0f ? 270f : 90f, 7f);
            for (float z = -hz + 5f; z <= hz - 4f; z += 7f)
            {
                var aisle = new Vector3(side * (hx - 1.6f), 0f, z);
                if (Mathf.RoundToInt(z) % 2 == 0)
                    Put(angelPrefab, room, placed, aisle, 0.9f, side > 0f ? 270f : 90f, z < -hz * 0.4f ? 2.6f : 3.6f);
                else
                    Put(planterPrefab, room, placed, aisle, 0.9f, 90f, 0.9f);
                Lamp(room, placed, new Vector3(side * (row - 1.8f), 0f, z + 1.75f), z < -hz * 0.4f ? 2.8f : 3.6f);
            }
        }
        foreach (float z in new[] { -hz * 0.45f, hz * 0.45f })
            PutWide(smallFountainPrefab, room, placed, new Vector3(0f, 0f, z), 3.6f, 0f, true, 1f);
    }

    // ③ 정원: the round court with its fountain, golden trees in the four great beds, flowerbeds and topiaries along
    // the paths, pavilions either side and at the far edge
    private void ThemeGarden(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        PutWide(grandFountainPrefab != null ? grandFountainPrefab : smallFountainPrefab, room, placed, Vector3.zero, 6.5f, 0f, true);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var bed = new Vector3(sx * hx * 0.6f, 0f, sz * hz * 0.6f);
                Tree(room, placed, bed, 8.5f);
                foreach (float a in new[] { 0f, 90f, 180f, 270f })
                {
                    Vector3 off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 4.2f;
                    PutSpanPlanter(room, placed, bed + off, a % 180f == 0f ? 90f : 0f);
                }
                PutWide(smallFountainPrefab, room, placed, bed - new Vector3(sx * 7.5f, 0f, sz * 6.5f), 3f, 0f, true, 1f);
                Put(topiaryPrefab, room, placed, bed + new Vector3(sx * 5.5f, 0f, -sz * 3f), 1f, Random.Range(0f, 360f), 2.2f, false, 1f);
            }
        foreach (float s in new[] { -1f, 1f })
            PutSolid(pavilionPrefab, room, placed, new Vector3(s * (hx - 4.5f), 0f, 0f), 2.4f, 3.2f, s > 0f ? 270f : 90f, 6f, 1f);
        PutSolid(pavilionPrefab, room, placed, new Vector3(0f, 0f, hz - 4f), 2.4f, 3.2f, 180f, 7f, 1f);
        Ambient.HallAir(t, room.Size, 3);
    }

    private void PutSpanPlanter(Room room, List<(Vector3 p, float r)> placed, Vector3 p, float yaw = 90f)
    {
        if (planterPrefab == null || !Clear(room, placed, p, 1.4f))
            return;
        PlaceSpan(planterPrefab, room.transform, p, yaw, 3.2f);
        placed.Add((p, 1.4f));
    }

    // ④ 파괴된 궁전: broken temple fronts, collapsed arches and a toppled valkyrie among the broken columns and rubble
    // (ScatterRuins laid those first)
    private void RuinedPalace(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        foreach (float s in new[] { -1f, 1f })
        {
            GameObject front = PutWide(ruinFrontPrefab, room, placed, new Vector3(s * hx * 0.42f, 0f, hz * 0.55f), 8f, Random.Range(150f, 210f), false, 1f);
            FootCollider(front, 2.6f);
            PutSolid(brokenArchPrefab, room, placed, new Vector3(s * hx * 0.7f, 0f, -hz * 0.1f), 1.3f, 2.2f, Random.Range(0f, 360f), Random.Range(4.8f, 5.6f));
        }
        PutSolid(brokenArchPrefab, room, placed, new Vector3(0f, 0f, hz - 3f), 1.3f, 2.2f, Random.Range(-15f, 15f), 5.6f);
        Tree(room, placed, new Vector3(-hx * 0.75f, 0f, hz * 0.7f), 7f);
        if (statuePrefab != null)
        {
            var p = new Vector3(Random.Range(-0.2f, 0.2f) * hx, 0f, hz * 0.25f);
            if (Clear(room, placed, p, 2f))
            {
                GameObject fallen = Place(statuePrefab, t, p, 0f, 1.3f, false);
                fallen.transform.localRotation = Quaternion.Euler(0f, Random.Range(70f, 110f), 0f) * Quaternion.Euler(-84f, 0f, 0f);
                fallen.transform.localPosition = p + Vector3.up * 0.45f;
                Rubble(t, p + Vector3.right * 2f, 2.2f, false);
                placed.Add((p, 2.6f));
            }
        }
        Ambient.HallAir(t, room.Size, 1);
    }

    // ⑤ 의식의 재단: the stepped altar and its pillar of light in the middle of the gold rings, braziers round it, a ring of
    // columns further out, the golden god and his angels at the far edge
    private void RitualAltar(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        GameObject altar = PlaceSized(altarPrefab, t, Vector3.zero, Random.Range(0, 4) * 90f, 4.4f, false);
        FootCollider(altar, 3f);
        placed.Add((Vector3.zero, 4.2f));
        float top = BoundsAround(altar).max.y;
        Material beam = Vfx.Material(Vfx.Pick("hall_beam", "glow"), true);
        for (int i = 0; i < 2; i++)
        {
            var sheet = Mats.Shape(PrimitiveType.Quad, t, Vector3.up * (top + 8f), new Vector3(3.4f, 17f, 1f), beam, false, "LightPillar");
            sheet.transform.localRotation = Quaternion.Euler(0f, 45f + i * 90f, 0f);
        }
        Vfx.Glow(t, Vector3.up * (top - 0.5f), 3f, new Color(1f, 0.85f, 0.45f) * 0.8f);
        foreach (float a in new[] { 45f, 135f, 225f, 315f })
        {
            Vector3 p = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 7.5f;
            if (Clear(room, placed, p, 1f))
            {
                Brazier(t, p, true);
                placed.Add((p, 1f));
            }
        }
        float ring = Mathf.Min(hx, hz) - 5f;
        for (int i = 0; i < 16; i++)
        {
            float a = i * 22.5f + 11.25f;
            if (Mathf.Abs(Mathf.DeltaAngle(a, Mathf.Round(a / 90f) * 90f)) < 15f)
                continue;
            Vector3 p = Quaternion.Euler(0f, a, 0f) * Vector3.forward * ring;
            if (!Clear(room, placed, p, 1.1f))
                continue;
            PalaceColumn(t, p, p.z < -hz * 0.3f ? 0.8f : 1.05f, true);
            placed.Add((p, 1.1f));
        }
        PutSolid(goldStatuePrefab, room, placed, new Vector3(0f, 0f, hz - 3f), 1.2f, 2f, 180f, 10f);
        foreach (float s in new[] { -1f, 1f })
        {
            PutSolid(angelPrefab, room, placed, new Vector3(s * 6f, 0f, hz - 3.2f), 0.9f, 1.4f, 180f, 5f);
            PutSolid(goldStatuePrefab, room, placed, new Vector3(s * (hx - 3f), 0f, hz * 0.35f), 1f, 1.8f, s > 0f ? 270f : 90f, 7f);
        }
        Ambient.HallAir(t, room.Size, 0);
        SoftLight(t, Vector3.up * (top + 1.5f), new Color(1f, 0.82f, 0.5f), 18f, 2.6f);
    }

    // ⑥ 다중 광장: every platform its own round plaza; the grand fountain on the middle one, fountains and golden trees on
    // the rest, lamps round each plaza
    private void MultiPlaza(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        if (room.Floor == null)
            return;
        Material disc = Vfx.Material(Vfx.Pick("hall_plaza_disc", "ring"), false);
        int k = 0;
        foreach (Rect r in room.Floor.OrderBy(r => r.center.sqrMagnitude))
        {
            float size = Mathf.Min(r.width, r.height);
            if (size < 9f)
                continue;   // (bridges)
            var c = new Vector3(r.center.x, 0f, r.center.y);
            float radius = Mathf.Min(size / 2f - 0.8f, 12f);
            var d = Mats.Shape(PrimitiveType.Quad, t, c + Vector3.up * 0.012f, Vector3.one * radius * 2f, disc, false, "PlazaDisc");
            d.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            if (k == 0)
                PutWide(grandFountainPrefab != null ? grandFountainPrefab : fountainPrefab, room, placed, c, Mathf.Min(9f, radius), 0f, true);
            else if (k % 2 == 1)
                PutWide(smallFountainPrefab, room, placed, c, 3.6f, 0f, true, 1f);
            else
                Tree(room, placed, c, 8f);
            foreach (float a in new[] { 45f, 135f, 225f, 315f })
                Lamp(room, placed, c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * (radius - 1.2f), c.z < 0f ? 3f : 3.8f);
            k++;
        }
    }

    // ⑦ 테라스: low balustrades closing off lower terraces on the far side with golden trees and pavilions on them, the
    // armillary lookouts, lamps down the sides
    private void Terrace(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        float lip = hz * 0.32f;
        foreach (float s in new[] { -1f, 1f })
        {
            for (float x = 6f; x <= hx - 3f; x += 3.6f)
            {
                var p = new Vector3(s * (x + 1.8f), 0f, lip);
                if (balustradePrefab == null || !Clear(room, placed, p, 1.6f))
                    continue;
                GameObject rail = PlaceSpan(balustradePrefab, t, p, 0f, 3.6f);
                var box = rail.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.6f / rail.transform.localScale.y, 0f);
                box.size = new Vector3(3.6f / rail.transform.localScale.x, 1.2f / rail.transform.localScale.y, 0.5f / rail.transform.localScale.z);
                placed.Add((p, 1.6f));
            }
            Tree(room, placed, new Vector3(s * hx * 0.55f, 0f, (lip + hz) / 2f + 1f), 8f);
            PutSolid(pavilionPrefab, room, placed, new Vector3(s * hx * 0.25f, 0f, hz - 4.5f), 2.4f, 3.2f, 180f, 6.5f, 1f);
            PutSolid(armillaryPrefab, room, placed, new Vector3(s * hx * 0.62f, 0f, -hz * 0.2f), 0.9f, 1.4f, 0f, 3.4f);
            for (float z = -hz + 4f; z <= hz - 4f; z += 8f)
                Lamp(room, placed, new Vector3(s * (hx - 1.5f), 0f, z), z < -hz * 0.4f ? 2.8f : 3.6f);
        }
        PutSolid(armillaryPrefab, room, placed, new Vector3(0f, 0f, hz - 3f), 1f, 1.6f, 180f, 4.2f);
        foreach (float sx in new[] { -1f, 1f })
        {
            var corner = new Vector3(sx * (hx - 1.8f), 0f, -(hz - 1.8f));
            Put(urnPrefab, room, placed, corner, 1f, FacingIn(corner), 1.4f);
        }
        Ambient.HallAir(t, room.Size, 3);
    }

    // ⑧ 장식 집중: the palace's treasures out under the sky: a nave of columns, statues, vases and candelabras all round
    // the painted floor, angels between the columns
    private void Ornate(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        int n = 0;
        for (float x = -hx + 2.4f; x <= hx - 2.4f; x += 2.8f, n++)
        {
            var p = new Vector3(x, 0f, hz - 1.8f);
            if (n % 2 == 0)
                Put(n % 4 == 0 ? angelPrefab : statuePrefab, room, placed, p, 1.2f, 180f, 4.6f);
            else
                Put(vasePrefab, room, placed, p, 0.8f, 180f, 2.2f);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            int k = 0;
            for (float z = -hz + 3f; z <= hz - 3.5f; z += 2.8f, k++)
            {
                var p = new Vector3(side * (hx - 1.5f), 0f, z);
                float yaw = FacingAwayFrom(side);
                if (z < -hz * 0.3f)
                    Put(vasePrefab, room, placed, p, 0.8f, yaw, 1.6f);
                else if (k % 3 == 0)
                    Put(angelPrefab, room, placed, p, 1.2f, yaw, 4f);
                else if (k % 3 == 1)
                    CandelabraAt(room, placed, p);
                else
                    Put(vasePrefab, room, placed, p, 0.8f, yaw, 2.1f);
            }
            foreach (float z in new[] { hz * 0.2f, hz * 0.6f })
            {
                var banner = new Vector3(side * (hx - 1f), 0f, z);
                if (bannerPrefab != null && Clear(room, placed, banner, 0.8f))
                {
                    Place(bannerPrefab, t, banner, side > 0f ? -90f : 90f, 1f, false);
                    placed.Add((banner, 0.8f));
                }
            }
            float row = hx * 0.55f;
            int c = 0;
            for (float z = -hz + 5f; z <= hz - 5f; z += 6f, c++)
            {
                var p = new Vector3(side * row, 0f, z);
                if (!Clear(room, placed, p, 1.1f))
                    continue;
                PalaceColumn(t, p, z < -hz * 0.35f ? 0.75f : 1.05f, true);
                placed.Add((p, 1.1f));
                if (c % 2 == 1)
                    PutSolid(angelPrefab, room, placed, p + new Vector3(side * 3f, 0f, 3f), 0.8f, 1.2f, side > 0f ? 270f : 90f, z < -hz * 0.35f ? 2.6f : 3.6f);
            }
        }
        for (float x = -hx * 0.6f; x <= hx * 0.6f + 0.01f; x += hx * 0.3f)
            Put(vasePrefab, room, placed, new Vector3(x, 0f, -(hz - 1.4f)), 0.8f, 0f, 1.4f);
        foreach (float a in new[] { 45f, 135f, 225f, 315f })
        {
            Vector3 p = Quaternion.Euler(0f, a, 0f) * Vector3.forward * 9f;
            Put(urnPrefab, room, placed, p, 0.9f, FacingIn(p), p.z < 0f ? 1.3f : 1.7f);
        }
        SoftLight(t, Vector3.up * 6f, new Color(1f, 0.82f, 0.55f), 24f, 1.6f);
    }
}
