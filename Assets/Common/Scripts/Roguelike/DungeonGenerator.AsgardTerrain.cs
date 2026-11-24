using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ── Asgard terrain ──
// The palace's fight halls grew more shapes (a temple plaza, a ruin, linked platforms, a round altar court, an
// asymmetric temple, a concourse of corridors), the open cosmos between a hall's floors is now a real drop with the
// temple's stepped foundations hanging into it, the marble floor is laid tile by tile (rotated, tinted in patches,
// carved less often, cracked where the temple has fallen) instead of one repeating sheet, and broken columns,
// rubble and a landmark give each hall its own middle.
//
// The fighting floor itself stays at one height (y = 0): attack ranges and boss warnings are drawn flat at ground
// level, so a walkable step would hide them. Height comes from what stands on the floor (stepped daises, stumps,
// fallen columns), the drop into the cosmos, and shrines floating above or below it.
public partial class DungeonGenerator
{
    private const float AbyssDepth = 12f;

    // VARCO ruin pieces (Resources/Ruins) and other realm pieces reused as landmarks; simple shapes stand in for any missing
    private static GameObject fallenColumnPrefab, brokenColumnPrefab, rubblePrefab, odinStatuePrefab, giantSwordPrefab, fenrirPrefab, hofundPrefab;
    private static bool ruinPropsLoaded;

    private bool centreTaken;   // a hall whose middle holds its own landmark: the dressing plan leaves the middle alone
    private bool ruinedRim;     // the ruin's balustrade is broken: stumps and rubble where columns and rails fell
    private bool lowRim;        // a balustrade facing the camera: low posts instead of tall columns

    // A balustrade post no taller than its rail: a marble plinth, a short shaft and a gold cap
    private void LowPost(Transform t, Vector3 p)
    {
        var root = new GameObject("LowPost");
        root.transform.SetParent(t, false);
        root.transform.localPosition = p;
        Mats.Shape(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(1f, 0.3f, 1f), Marble, true, "Plinth");
        Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.7f, 0f), new Vector3(0.62f, 0.4f, 0.62f), Marble, false, "Shaft");
        Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.36f, 0f), new Vector3(0.72f, 0.05f, 0.72f), GoldInlay, false, "Band");
        Mats.Shape(PrimitiveType.Cube, root.transform, new Vector3(0f, 1.17f, 0f), new Vector3(0.85f, 0.14f, 0.85f), GoldInlay, false, "Cap");
    }

    private static void LoadRuinProps()
    {
        if (ruinPropsLoaded)
            return;
        ruinPropsLoaded = true;
        fallenColumnPrefab = Resources.Load<GameObject>("Ruins/FallenColumn");
        brokenColumnPrefab = Resources.Load<GameObject>("Ruins/BrokenColumn");
        rubblePrefab = Resources.Load<GameObject>("Ruins/Rubble");
        odinStatuePrefab = Resources.Load<GameObject>("Ruins/OdinStatue");
        giantSwordPrefab = Resources.Load<GameObject>("TyrArena/GiantSword");
        fenrirPrefab = Resources.Load<GameObject>("TyrArena/Fenrir");
        hofundPrefab = Resources.Load<GameObject>("Props/SpectralHofund");
    }

    private static Material Marble => Mats.Get("asg_marble", new Color(0.9f, 0.9f, 0.93f), 0.55f);
    private static Material MarbleShade => Mats.Get("asg_marble_shade", new Color(0.66f, 0.67f, 0.76f), 0.4f);
    private static Material MarbleDeep => Mats.Get("asg_marble_deep", new Color(0.4f, 0.41f, 0.55f), 0.3f);
    // Gold set into the floor that should not glow like an attack warning: the wide rings and the daises' edges
    private static Material SoftGold => Mats.Get("asg_inlay_soft", new Color(0.8f, 0.62f, 0.3f), 0.75f, new Color(0.1f, 0.07f, 0.015f));

    private static bool IsAsgardShape(HallShape s) => s >= HallShape.Plaza;

    // ───────────── Floor plans ─────────────

    // Each plan is drawn as a mask on a one-metre grid and cut into rectangles: floor where it is true, open cosmos where
    // it is not. Every side keeps floor at its middle (where a door may be).
    private static void AsgardLayout(HallShape shape, List<Vector2Int> exits, out Vector2 size, List<Rect> floor, List<Rect> voids)
    {
        int w, h;
        bool[,] cells;
        switch (shape)
        {
            case HallShape.Plaza:
            {
                // 신전 광장: a wide square, its corners broken off into the cosmos
                w = 42; h = 32;
                float cx = w / 2f - 7f, cz = h / 2f - 7f;
                cells = Mask(w, h, (x, z) => !(Mathf.Abs(x) > cx && Mathf.Abs(z) > cz));
                break;
            }
            case HallShape.Ruins:
            {
                // 무너진 유적: a broad hall whose edges have crumbled away in bites, with a pit or two where the floor fell in
                w = 38; h = 30;
                cells = Mask(w, h, (x, z) => true);
                int bites = Random.Range(3, 6);
                for (int k = 0; k < bites; k++)
                {
                    Vector2Int side = Directions[Random.Range(0, 4)];
                    bool alongX = side.y != 0;
                    float half = alongX ? w / 2f : h / 2f;
                    float len = Random.Range(5, 10);
                    float bite = Random.Range(2, side == Vector2Int.down ? 4 : 6);
                    // (never at a side's middle, where a door may be)
                    float centre = Random.Range(5.5f + len / 2f, half - len / 2f + 2f) * (Random.value < 0.5f ? -1f : 1f);
                    Carve(cells, w, h, (x, z) =>
                    {
                        float along = alongX ? x : z, across = alongX ? z * side.y : x * side.x;
                        float edge = alongX ? h / 2f : w / 2f;
                        return Mathf.Abs(along - centre) < len / 2f && across > edge - bite;
                    });
                }
                int pits = Random.Range(1, 3);
                for (int k = 0, guard = 0; k < pits && guard < 40; guard++)
                {
                    var c = new Vector2(Random.Range(-w / 2f + 6f, w / 2f - 6f), Random.Range(-h / 2f + 6f, h / 2f - 6f));
                    if (c.magnitude < 7f || Mathf.Abs(c.x) < 5f || Mathf.Abs(c.y) < 4.5f)
                        continue;
                    float px = Random.Range(1.5f, 2.5f), pz = Random.Range(1.5f, 2f);
                    Carve(cells, w, h, (x, z) => Mathf.Abs(x - c.x) < px && Mathf.Abs(z - c.y) < pz);
                    k++;
                }
                break;
            }
            case HallShape.Platforms:
            {
                // 공중 제단군: a middle platform with smaller ones off it, joined by short bridges
                w = 44; h = 40;
                bool[] side = new bool[4];
                for (int i = 0; i < 4; i++)
                    side[i] = exits.Contains(Directions[i]) || Random.value < 0.55f;
                cells = Mask(w, h, (x, z) =>
                {
                    if (Mathf.Abs(x) <= 8f && Mathf.Abs(z) <= 7f) return true;                                  // the middle
                    if (side[0] && Mathf.Abs(x) <= 5f && z >= 13f) return true;                                 // north
                    if (side[0] && Mathf.Abs(x) <= 3f && z >= 7f) return true;
                    if (side[2] && Mathf.Abs(x) <= 5f && z <= -13f) return true;                                // south
                    if (side[2] && Mathf.Abs(x) <= 3f && z <= -7f) return true;
                    if (side[1] && Mathf.Abs(z) <= 5f && x >= 14f) return true;                                 // east
                    if (side[1] && Mathf.Abs(z) <= 3f && x >= 8f) return true;
                    if (side[3] && Mathf.Abs(z) <= 5f && x <= -14f) return true;                                // west
                    if (side[3] && Mathf.Abs(z) <= 3f && x <= -8f) return true;
                    return false;
                });
                break;
            }
            case HallShape.Altar:
            {
                // 원형 제단: a court round a stepped altar, its corners cut
                w = 36; h = 32;
                float cx = w / 2f - 5f, cz = h / 2f - 5f;
                cells = Mask(w, h, (x, z) => !(Mathf.Abs(x) > cx && Mathf.Abs(z) > cz));
                break;
            }
            case HallShape.Asymmetric:
            {
                // 비대칭 신전: two offset wings overlapping in the middle, a far corner and a near corner open to the stars
                w = 40; h = 32;
                float flip = Random.value < 0.5f ? 1f : -1f;
                cells = Mask(w, h, (x, z) =>
                {
                    x *= flip;
                    return (x <= 4f && z <= 4f) || (x >= -6f && z >= -4f);
                });
                break;
            }
            default:
            {
                // 회랑과 광장 (Concourse): narrow colonnaded corridors from each door into a wide hall, an alcove on a blind side
                w = 42; h = 36;
                Vector2Int alcove = Directions.Where(d => !exits.Contains(d) && d != Vector2Int.down).DefaultIfEmpty(Vector2Int.zero).First();
                cells = Mask(w, h, (x, z) =>
                {
                    if (Mathf.Abs(x) <= 11f && Mathf.Abs(z) <= 9f) return true;
                    foreach (Vector2Int d in exits)
                        if (d.x != 0 ? Mathf.Abs(z) <= 3f && x * d.x >= 0f : Mathf.Abs(x) <= 3f && z * d.y >= 0f)
                            return true;
                    if (alcove != Vector2Int.zero)
                        return alcove.x != 0 ? Mathf.Abs(z) <= 4f && x * alcove.x >= 0f && Mathf.Abs(x) <= 15f
                                             : Mathf.Abs(x) <= 4f && z * alcove.y >= 0f && Mathf.Abs(z) <= 13f;
                    return false;
                });
                break;
            }
        }
        size = new Vector2(w, h);
        MaskRects(cells, w, h, true, floor);
        MaskRects(cells, w, h, false, voids);
    }

    // A w x h grid of one-metre cells; the test gets each cell's centre (local x/z)
    private static bool[,] Mask(int w, int h, System.Func<float, float, bool> inside)
    {
        var cells = new bool[w, h];
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
                cells[i, j] = inside(-w / 2f + i + 0.5f, -h / 2f + j + 0.5f);
        return cells;
    }

    private static void Carve(bool[,] cells, int w, int h, System.Func<float, float, bool> cut)
    {
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
                if (cut(-w / 2f + i + 0.5f, -h / 2f + j + 0.5f))
                    cells[i, j] = false;
    }

    // The cells of one value cut greedily into as few rectangles as it takes
    private static void MaskRects(bool[,] cells, int w, int h, bool value, List<Rect> output)
    {
        var used = new bool[w, h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                if (used[i, j] || cells[i, j] != value)
                    continue;
                int i1 = i;
                while (i1 + 1 < w && !used[i1 + 1, j] && cells[i1 + 1, j] == value)
                    i1++;
                int j1 = j;
                while (j1 + 1 < h)
                {
                    bool ok = true;
                    for (int k = i; k <= i1 && ok; k++)
                        ok = !used[k, j1 + 1] && cells[k, j1 + 1] == value;
                    if (!ok) break;
                    j1++;
                }
                for (int a = i; a <= i1; a++)
                    for (int b = j; b <= j1; b++)
                        used[a, b] = true;
                output.Add(R(-w / 2f + i, -h / 2f + j, -w / 2f + i1 + 1, -h / 2f + j1 + 1));
            }
    }

    // ───────────── The drop into the cosmos ─────────────

    // A hall's open cosmos as holes: run out to the walls where they meet the zone's edge, and on out past the open
    // (camera) side so the drop has no near lip
    private List<Rect> HoleRects(Room room)
    {
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        var holes = new List<Rect>();
        foreach (Rect v in room.Voids)
        {
            float x0 = v.xMin <= -hx + 0.01f ? v.xMin - WallSetback : v.xMin;
            float x1 = v.xMax >= hx - 0.01f ? v.xMax + WallSetback : v.xMax;
            float z0 = v.yMin <= -hz + 0.01f ? v.yMin - 14f : v.yMin;
            float z1 = v.yMax >= hz - 0.01f ? v.yMax + WallSetback : v.yMax;
            holes.Add(Rect.MinMaxRect(x0, z0, x1, z1));
        }
        return holes;
    }

    // The floor's rectangles, run out under the walls where they reach the zone's edge (as the old field did)
    private List<Rect> SolidRects(Room room, Vector2 fieldSize)
    {
        if (room.Floor == null)
            return new List<Rect> { R(-fieldSize.x / 2f, -fieldSize.y / 2f, fieldSize.x / 2f, fieldSize.y / 2f) };
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        float rim = (fieldSize.x - room.Size.x) / 2f;   // (the floor runs on past the zone's edge by this much)
        return room.Floor.Select(r => Rect.MinMaxRect(
            r.xMin <= -hx + 0.01f ? r.xMin - rim : r.xMin,
            r.yMin <= -hz + 0.01f ? r.yMin - rim : r.yMin,
            r.xMax >= hx - 0.01f ? r.xMax + rim : r.xMax,
            r.yMax >= hz - 0.01f ? r.yMax + rim : r.yMax)).ToList();
    }

    // Where a shaped hall opens to the cosmos the floor really ends: the starry deep lies far below, the temple's
    // stepped foundations hang down into it, and broken stones drift in the dark
    private void OpenAbyss(Room room)
    {
        Transform t = room.transform;
        var deep = new GameObject("Abyss").transform;
        deep.SetParent(t, false);
        deep.localPosition = Vector3.down * AbyssDepth;
        Material gold = Mats.Get("glint_gold", new Color(1f, 0.9f, 0.6f), 0.5f, new Color(2f, 1.7f, 0.9f));
        Material cyan = Mats.Get("glint_cyan", new Color(0.6f, 0.9f, 1f), 0.5f, new Color(0.9f, 1.6f, 2.2f));
        foreach (Rect v in HoleRects(room))
        {
            // (the deep runs on a little under the floor's edges, so no seam shows looking down past a lip)
            // (dark and plain, the stars scattered over it: it should read as the night far below, not as a floor)
            // (seen down through the hole from the camera's slant, the deep shows well to the north of it: it runs on that way)
            Rect deepRect = Rect.MinMaxRect(v.xMin - 10f, v.yMin - 4f, v.xMax + 10f, v.yMax + 14f);
            Material cosmos = Mats.Get("abyss_night", new Color(0.03f, 0.035f, 0.09f), 0.1f, new Color(0.015f, 0.02f, 0.06f));
            Mats.Shape(PrimitiveType.Cube, deep, new Vector3(deepRect.center.x, -0.1f, deepRect.center.y), new Vector3(deepRect.width, 0.2f, deepRect.height), cosmos, false, "AbyssFloor");
            var golds = new List<Vector3>();
            var cyans = new List<Vector3>();
            for (int i = 0; i < deepRect.width * deepRect.height / 3f; i++)
            {
                var p = new Vector3(Random.Range(deepRect.xMin, deepRect.xMax), Random.Range(0.06f, 0.2f), Random.Range(deepRect.yMin, deepRect.yMax));
                (Random.value < 0.55f ? golds : cyans).Add(p);
            }
            GlintMesh(deep, "AbyssGlints_Gold", golds, gold);
            GlintMesh(deep, "AbyssGlints_Cyan", cyans, cyan);
            // a cold glow rising out of the deep, so the drop reads from above
            SoftLight(t, new Vector3(v.center.x, -3.5f, Mathf.Min(v.center.y, v.yMin + 8f)), new Color(0.45f, 0.55f, 1f), Mathf.Min(Mathf.Max(v.width, v.height), 16f) * 0.5f + 3f, 0.9f);

            // broken stones drifting in the dark, well below the floor
            int stones = Mathf.Clamp(Mathf.RoundToInt(v.width * Mathf.Min(v.height, 16f) / 30f), 1, 5);
            for (int i = 0; i < stones; i++)
            {
                var p = new Vector3(Random.Range(v.xMin + 0.8f, v.xMax - 0.8f), Random.Range(-3.4f, -1.4f), Random.Range(v.yMin + 0.8f, Mathf.Min(v.yMax, v.yMin + 16f) - 0.8f));
                GameObject stone = Mats.Shape(PrimitiveType.Cube, t, p, new Vector3(Random.Range(0.4f, 1.1f), Random.Range(0.3f, 0.7f), Random.Range(0.4f, 1f)),
                    Random.value < 0.6f ? MarbleShade : MarbleDeep, false, "DriftingStone");
                stone.transform.localRotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f));
                stone.AddComponent<FloatBob>();
            }
        }
        Foundations(room);
    }

    // Under every piece of floor, the temple's footing: tiers of marble stepping in as they go down, a gold band under the lip
    private void Foundations(Room room)
    {
        Transform t = room.transform;
        var parts = new GameObject("Foundations").transform;
        parts.SetParent(t, false);
        foreach (Rect r in SolidRects(room, room.Size + Vector2.one * WallSetback * 2f))
        {
            float top = 0f;
            for (int tier = 0; tier < 3; tier++)
            {
                float inset = tier * 1.1f, height = tier == 0 ? 1.3f : 1.1f;
                float wx = r.width - inset * 2f, wz = r.height - inset * 2f;
                if (wx < 1f || wz < 1f)
                    break;
                Mats.Shape(PrimitiveType.Cube, parts, new Vector3(r.center.x, top - height / 2f, r.center.y), new Vector3(wx, height, wz),
                    tier == 0 ? MarbleShade : MarbleDeep, false, "Footing");
                if (tier == 0)
                    Mats.Shape(PrimitiveType.Cube, parts, new Vector3(r.center.x, -0.32f, r.center.y), new Vector3(wx + 0.12f, 0.14f, wz + 0.12f), GoldInlay, false, "FootingBand");
                top -= height;
            }
        }
    }

    // The zone's ground with the holes cut out of it: one tiled mesh (its texture running on unbroken across the pieces)
    // and a collider for each piece
    private void CutGround(Room room, GameObject groundPiece, Vector2 groundSize, Realm realm)
    {
        List<Rect> holes = HoleRects(room);
        if (holes.Count == 0)
            return;
        float gx = groundSize.x / 2f, gz = groundSize.y / 2f;
        var xs = new SortedSet<float> { -gx, gx };
        var zs = new SortedSet<float> { -gz, gz };
        foreach (Rect hRect in holes)
        {
            xs.Add(Mathf.Clamp(hRect.xMin, -gx, gx)); xs.Add(Mathf.Clamp(hRect.xMax, -gx, gx));
            zs.Add(Mathf.Clamp(hRect.yMin, -gz, gz)); zs.Add(Mathf.Clamp(hRect.yMax, -gz, gz));
        }
        float[] xa = xs.ToArray(), za = zs.ToArray();
        var pieces = new List<Rect>();
        for (int j = 0; j < za.Length - 1; j++)
        {
            float run0 = float.NaN;
            for (int i = 0; i <= xa.Length - 1; i++)
            {
                bool solid = i < xa.Length - 1;
                if (solid)
                {
                    var c = new Vector2((xa[i] + xa[i + 1]) / 2f, (za[j] + za[j + 1]) / 2f);
                    solid = !holes.Any(hr => hr.Contains(c));
                }
                if (solid && float.IsNaN(run0))
                    run0 = xa[i];
                else if (!solid && !float.IsNaN(run0))
                {
                    pieces.Add(Rect.MinMaxRect(run0, za[j], xa[i], za[j + 1]));
                    run0 = float.NaN;
                }
            }
        }

        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        var colliders = new GameObject("GroundColliders").transform;
        colliders.SetParent(room.transform, false);
        foreach (Rect p in pieces)
        {
            AddQuad(vertices, uv, triangles, p, 0f, q => q / groundTileSize);
            var box = colliders.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(p.center.x, -0.11f, p.center.y);
            box.size = new Vector3(p.width, 0.2f, p.height);
        }
        var mesh = new Mesh { name = "CutGround", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GameObject cut = FlatMesh(room.transform, "Ground", mesh, -0.01f, TiledGround(realm, Vector2.one * groundTileSize, false));
        if (OpenAir)
            cut.GetComponent<Renderer>().enabled = false;   // (a map over the clouds: the ground holds you, unseen)
        groundPiece.SetActive(false);
        Destroy(groundPiece);
    }

    // An upward-facing quad over a rectangle; uvOf maps a local x/z point to its UV
    private static void AddQuad(List<Vector3> vertices, List<Vector2> uv, List<int> triangles, Rect r, float y, System.Func<Vector2, Vector2> uvOf)
    {
        int v = vertices.Count;
        vertices.Add(new Vector3(r.xMin, y, r.yMin)); uv.Add(uvOf(new Vector2(r.xMin, r.yMin)));
        vertices.Add(new Vector3(r.xMax, y, r.yMin)); uv.Add(uvOf(new Vector2(r.xMax, r.yMin)));
        vertices.Add(new Vector3(r.xMin, y, r.yMax)); uv.Add(uvOf(new Vector2(r.xMin, r.yMax)));
        vertices.Add(new Vector3(r.xMax, y, r.yMax)); uv.Add(uvOf(new Vector2(r.xMax, r.yMax)));
        triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 1);
        triangles.Add(v + 1); triangles.Add(v + 2); triangles.Add(v + 3);
    }

    // ───────────── The marble, tile by tile ─────────────

    // The palace floor laid one tile at a time: each tile takes one of the image's inner tiles (plain marble mostly,
    // a carved knot now and then), turned any of four ways, in patches of warmer, cooler or older stone, cracked where
    // the hall has fallen to ruin. Replaces the single repeating sheet.
    private void PalaceTiles(Room room, Vector2 fieldSize, GameObject field)
    {
        if (palaceFloorTexture == null)
            return;
        float tile = palaceTileSize / 4f;
        float x0 = -fieldSize.x / 2f, z0 = -fieldSize.y / 2f;
        bool ruin = room.Shape == nameof(HallShape.Ruins);
        bool landmarkMiddle = room.Shape == nameof(HallShape.Plaza) || room.Shape == nameof(HallShape.Altar);
        float knotChance = ruin ? 0.1f : 0.16f;
        float crackChance = ruin ? 0.2f : room.RoomKind == Room.Kind.Combat ? 0.05f : 0.03f;
        float agedChance = ruin ? 0.16f : 0.05f;
        Vector2 noise = new Vector2(Random.Range(0f, 100f), Random.Range(0f, 100f));

        // (plain inner tiles of the image, then the carved ones; the image's outer ring of tiles has its frame drawn in)
        Vector2Int[] plain = { new Vector2Int(1, 1), new Vector2Int(2, 2) };
        Vector2Int[] knot = { new Vector2Int(2, 1), new Vector2Int(1, 2) };
        var picks = new Dictionary<Vector2Int, (Vector2Int cell, int turn, int tint, bool crack)>();
        (Vector2Int cell, int turn, int tint, bool crack) Pick(Vector2Int ix)
        {
            if (picks.TryGetValue(ix, out var got))
                return got;
            Vector2 c = new Vector2(x0 + (ix.x + 0.5f) * tile, z0 + (ix.y + 0.5f) * tile);
            bool carved = Random.value < knotChance && !(landmarkMiddle && c.magnitude < 6f);
            Vector2Int cell = carved ? knot[Random.Range(0, 2)] : plain[Random.Range(0, 2)];
            float n = Mathf.PerlinNoise(noise.x + ix.x * 0.21f, noise.y + ix.y * 0.21f);
            int tint = Random.value < agedChance ? 3 : n < 0.33f ? 1 : n > 0.68f ? 2 : 0;
            got = (cell, Random.Range(0, 4), tint, Random.value < crackChance);
            picks[ix] = got;
            return got;
        }

        var verts = new List<Vector3>[4];
        var uvs = new List<Vector2>[4];
        var tris = new List<int>[4];
        for (int k = 0; k < 4; k++) { verts[k] = new List<Vector3>(); uvs[k] = new List<Vector2>(); tris[k] = new List<int>(); }
        var cracks = new List<(Vector2 a, Vector2 b)>();
        var cracked = new HashSet<Vector2Int>();

        foreach (Rect r in SolidRects(room, fieldSize))
        {
            int i0 = Mathf.FloorToInt((r.xMin - x0) / tile + 0.001f), i1 = Mathf.CeilToInt((r.xMax - x0) / tile - 0.001f);
            int j0 = Mathf.FloorToInt((r.yMin - z0) / tile + 0.001f), j1 = Mathf.CeilToInt((r.yMax - z0) / tile - 0.001f);
            for (int i = i0; i < i1; i++)
                for (int j = j0; j < j1; j++)
                {
                    float tx = x0 + i * tile, tz = z0 + j * tile;
                    Rect cut = Rect.MinMaxRect(Mathf.Max(tx, r.xMin), Mathf.Max(tz, r.yMin), Mathf.Min(tx + tile, r.xMax), Mathf.Min(tz + tile, r.yMax));
                    if (cut.width < 0.01f || cut.height < 0.01f)
                        continue;
                    var pick = Pick(new Vector2Int(i, j));
                    Vector2 Uv(Vector2 q)
                    {
                        // where the point lies in its tile, turned, then into that tile of the image
                        float fx = (q.x - tx) / tile, fz = (q.y - tz) / tile;
                        for (int k = 0; k < pick.turn; k++)
                            (fx, fz) = (fz, 1f - fx);
                        return new Vector2((pick.cell.x + fx) * 0.25f, (pick.cell.y + fz) * 0.25f);
                    }
                    AddQuad(verts[pick.tint], uvs[pick.tint], tris[pick.tint], cut, 0f, Uv);
                    if (pick.crack && cracked.Add(new Vector2Int(i, j)) && room.OnFloorLocal(new Vector2(tx + tile / 2f, tz + tile / 2f), 0.5f))
                        Crack(cracks, new Rect(tx, tz, tile, tile));
                }
        }

        // (close to one another: patches of slightly different stone, not a checkerboard)
        Color[] tints = { new Color(0.95f, 0.95f, 0.97f), new Color(0.86f, 0.88f, 0.94f), new Color(1f, 0.95f, 0.87f), new Color(0.9f, 0.87f, 0.81f) };
        var floorRoot = new GameObject("MarbleFloor").transform;
        floorRoot.SetParent(room.transform, false);
        for (int k = 0; k < 4; k++)
        {
            if (verts[k].Count == 0)
                continue;
            var mesh = new Mesh { name = "MarbleTiles" + k, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts[k]);
            mesh.SetUVs(0, uvs[k]);
            mesh.SetTriangles(tris[k], 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Material mat = Mats.Get("asg_tile_" + k, tints[k], k == 3 ? 0.4f : 0.55f);
            mat.SetTexture("_BaseMap", palaceFloorTexture);
            FlatMesh(floorRoot, "Tiles", mesh, 0.005f, mat);
        }
        if (cracks.Count > 0)
            FlatMesh(floorRoot, "Cracks", StripMesh(cracks, 0.045f), 0.0068f, Mats.Get("asg_crack", new Color(0.45f, 0.44f, 0.5f), 0.2f));
        field.GetComponent<Renderer>().enabled = false;
    }

    // A crack across a tile: a jagged line from one edge toward another, a short branch off it now and then
    private static void Crack(List<(Vector2 a, Vector2 b)> cracks, Rect tileRect)
    {
        Vector2 a = new Vector2(Random.Range(tileRect.xMin + 0.3f, tileRect.xMax - 0.3f), tileRect.yMin + 0.05f);
        if (Random.value < 0.5f)
            a = new Vector2(tileRect.xMin + 0.05f, Random.Range(tileRect.yMin + 0.3f, tileRect.yMax - 0.3f));
        Vector2 dir = (tileRect.center - a).normalized;
        int segments = Random.Range(3, 5);
        for (int s = 0; s < segments; s++)
        {
            dir = (Quaternion.Euler(0f, 0f, Random.Range(-40f, 40f)) * dir).normalized;
            Vector2 b = a + dir * Random.Range(0.35f, 0.7f);
            b.x = Mathf.Clamp(b.x, tileRect.xMin + 0.05f, tileRect.xMax - 0.05f);
            b.y = Mathf.Clamp(b.y, tileRect.yMin + 0.05f, tileRect.yMax - 0.05f);
            cracks.Add((a, b));
            if (s == 1 && Random.value < 0.6f)
                cracks.Add((b, b + (Vector2)(Quaternion.Euler(0f, 0f, Random.value < 0.5f ? 55f : -55f) * dir) * 0.35f));
            a = b;
        }
    }

    // Thin flat strips along line segments, as one mesh
    private static Mesh StripMesh(List<(Vector2 a, Vector2 b)> segments, float width)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        foreach (var (a, b) in segments)
        {
            Vector2 d = (b - a).normalized;
            Vector2 n = new Vector2(-d.y, d.x) * width / 2f;
            int v = vertices.Count;
            vertices.Add(new Vector3(a.x - n.x, 0f, a.y - n.y));
            vertices.Add(new Vector3(a.x + n.x, 0f, a.y + n.y));
            vertices.Add(new Vector3(b.x - n.x, 0f, b.y - n.y));
            vertices.Add(new Vector3(b.x + n.x, 0f, b.y + n.y));
            // (either winding, so it faces up whichever way the segment runs)
            triangles.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2, v, v + 2, v + 1, v + 1, v + 2, v + 3 });
        }
        var mesh = new Mesh { name = "Strips" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        // (lit as the floor is: straight up, whichever face shows)
        mesh.SetNormals(Enumerable.Repeat(Vector3.up, vertices.Count).ToList());
        mesh.RecalculateBounds();
        return mesh;
    }

    // ───────────── Ruins and landmarks ─────────────

    // A broken column stump standing on the floor, about this tall
    private GameObject Stump(Transform t, Vector3 p, float height, bool solid)
    {
        LoadRuinProps();
        if (brokenColumnPrefab != null)
            return PlaceSized(brokenColumnPrefab, t, p, Random.Range(0f, 360f), height, solid);
        var root = new GameObject("BrokenColumn");
        root.transform.SetParent(t, false);
        root.transform.localPosition = p;
        Mats.Shape(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(1.3f, 0.3f, 1.3f), MarbleShade, solid, "Plinth");
        Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.3f + (height - 0.3f) / 2f, 0f), new Vector3(0.9f, (height - 0.3f) / 2f, 0.9f), Marble, solid, "Shaft");
        Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.96f, 0.05f, 0.96f), GoldInlay, false, "Band");
        return root;
    }

    // A column lying where it fell, about this long
    private GameObject FallenColumn(Transform t, Vector3 p, float length, float yaw, bool solid)
    {
        LoadRuinProps();
        if (fallenColumnPrefab != null)
        {
            GameObject go = PlaceSpan(fallenColumnPrefab, t, p, yaw, length);
            if (!solid)
                foreach (Collider c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            return go;
        }
        var root = new GameObject("FallenColumn");
        root.transform.SetParent(t, false);
        root.transform.localPosition = p;
        root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        float piece = length * 0.55f;
        GameObject a = Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(-length * 0.22f, 0.42f, 0f), new Vector3(0.84f, piece / 2f, 0.84f), Marble, solid, "Drum");
        a.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        GameObject b = Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(length * 0.3f, 0.42f, 0.25f), new Vector3(0.84f, piece * 0.35f, 0.84f), Marble, solid, "Drum");
        b.transform.localRotation = Quaternion.Euler(0f, 18f, 90f);
        return root;
    }

    // A low heap of broken marble, about this wide
    private GameObject Rubble(Transform t, Vector3 p, float width, bool solid)
    {
        LoadRuinProps();
        if (rubblePrefab != null)
        {
            GameObject go = PlaceSpan(rubblePrefab, t, p, Random.Range(0f, 360f), width);
            if (!solid)
                foreach (Collider c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            return go;
        }
        var root = new GameObject("Rubble");
        root.transform.SetParent(t, false);
        root.transform.localPosition = p;
        for (int i = 0; i < 5; i++)
        {
            Vector2 o = Random.insideUnitCircle * width * 0.32f;
            float s = Random.Range(0.25f, 0.55f) * width * 0.5f;
            GameObject block = Mats.Shape(PrimitiveType.Cube, root.transform, new Vector3(o.x, s * 0.35f, o.y), new Vector3(s, s * 0.7f, s * Random.Range(0.7f, 1.2f)),
                Random.value < 0.6f ? Marble : MarbleShade, solid && i == 0, "Block");
            block.transform.localRotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
        }
        return root;
    }

    // A stepped round dais (its tiers this far apart), solid as one round obstacle; returns the height of its top
    private float Dais(Transform t, Vector3 p, float radius, int tiers, float step = 0.24f)
    {
        float y = 0f;
        for (int i = 0; i < tiers; i++)
        {
            float r = radius - i * 0.55f;
            Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.up * (y + step / 2f), new Vector3(r * 2f, step / 2f, r * 2f), i % 2 == 0 ? Marble : MarbleShade, false, "DaisTier");
            FlatMesh(t, "DaisGold", AnnulusMesh(r - 0.12f, r, 64), 0f, SoftGold).transform.localPosition = p + Vector3.up * (y + step + 0.004f);
            y += step;
        }
        var block = new GameObject("DaisCollider");
        block.transform.SetParent(t, false);
        block.transform.localPosition = p + Vector3.up * 2f;
        var cap = block.AddComponent<CapsuleCollider>();
        cap.radius = radius;
        cap.height = 4f + radius * 2f;
        return y;
    }

    // A raised shrine floating out in the cosmos: a marble island stepping down into the dark, its top above or
    // below the hall's floor
    private Transform VoidIsland(Transform t, Vector3 centre, Vector2 size, float top)
    {
        var root = new GameObject("FloatingShrine").transform;
        root.SetParent(t, false);
        root.localPosition = new Vector3(centre.x, top, centre.z);
        float y = 0f;
        for (int tier = 0; tier < 3; tier++)
        {
            float inset = tier * 0.9f, height = tier == 0 ? 0.6f : 0.9f;
            float wx = size.x - inset * 2f, wz = size.y - inset * 2f;
            if (wx < 0.8f || wz < 0.8f)
                break;
            Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, y - height / 2f, 0f), new Vector3(wx, height, wz), tier == 0 ? Marble : tier == 1 ? MarbleShade : MarbleDeep, false, "ShrineTier");
            if (tier == 0)
                Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, -0.08f, 0f), new Vector3(wx + 0.1f, 0.1f, wz + 0.1f), GoldInlay, false, "ShrineBand");
            y -= height;
        }
        return root;
    }

    // A landmark scaled to a height, standing on something this high
    private GameObject Landmark(GameObject prefab, Transform t, Vector3 p, float yaw, float height)
    {
        if (prefab == null)
            return null;
        GameObject go = PlaceSized(prefab, t, p, yaw, height, false);
        return go;
    }

    // What makes each new hall shape itself (set down before the dressing plan, which works round it)
    private void AsgardLandmarks(Room room, List<(Vector3 p, float r)> placed)
    {
        LoadRuinProps();
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Color warm = new Color(1f, 0.82f, 0.5f);
        switch (room.Shape)
        {
            case nameof(HallShape.Plaza):
            {
                // The Allfather's statue on a stepped dais at the heart of the square, two rings of gold round it and
                // braziers on the diagonals
                float top = Dais(t, Vector3.zero, 3.1f, 3);
                if (Landmark(odinStatuePrefab, t, Vector3.up * top, 180f, 5.4f) == null)
                    Landmark(obeliskPrefab, t, Vector3.up * top, 0f, 3.6f);
                placed.Add((Vector3.zero, 4.2f));
                centreTaken = true;
                FlatMesh(t, "PlazaRing", AnnulusMesh(4.4f, 4.6f, 96), 0.011f, SoftGold);
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                    {
                        var p = new Vector3(sx * 6.2f, 0f, sz * 6.2f);
                        Brazier(t, p, true);
                        placed.Add((p, 1.2f));
                    }
                Ambient.RuneHalo(t, Vector3.up * (top + 0.02f), 2.4f, warm * 0.7f);
                SoftLight(t, new Vector3(0f, 5.5f, -2f), warm, 14f, 1.6f);
                break;
            }
            case nameof(HallShape.Altar):
            {
                // A tall stepped altar ringed by a circle of columns, a relic of the gods hovering over it
                float top = Dais(t, Vector3.zero, 3.4f, 4, 0.22f);
                GameObject relic = Random.value < 0.5f ? hofundPrefab : obeliskPrefab;
                if (Random.value < 0.35f && odinStatuePrefab != null)
                    Landmark(odinStatuePrefab, t, Vector3.up * top, 180f, 5f);
                else if (relic != null)
                {
                    GameObject r = Landmark(relic, t, Vector3.up * (top + 0.2f), Random.Range(0f, 360f), relic == hofundPrefab ? 4.2f : 3.8f);
                    if (r != null) r.AddComponent<FloatBob>();
                }
                placed.Add((Vector3.zero, 4.4f));
                centreTaken = true;
                FlatMesh(t, "AltarRing", AnnulusMesh(4.6f, 4.8f, 96), 0.011f, SoftGold);
                for (int i = 0; i < 8; i++)
                {
                    float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
                    var p = new Vector3(Mathf.Cos(a) * 9.6f, 0f, Mathf.Sin(a) * 8.6f);
                    // (on the camera's side, broken stumps instead of whole columns, so they hide nothing)
                    if (p.z < -2f)
                        Stump(t, p, Random.Range(0.9f, 1.4f), true);
                    else if (Random.value < 0.3f)
                        Stump(t, p, Random.Range(1.6f, 2.4f), true);
                    else
                        PalaceColumn(t, p, 0.85f, true);
                    placed.Add((p, 1.4f));
                }
                Ambient.RuneHalo(t, Vector3.up * (top + 0.02f), 2.6f, new Color(0.6f, 0.8f, 1f) * 0.8f);
                SoftLight(t, new Vector3(0f, 5f, -1f), new Color(0.75f, 0.85f, 1f), 15f, 2f);
                break;
            }
            case nameof(HallShape.Ruins):
            {
                // A god's great sword stands driven into the broken floor, rubble heaped round its blade
                var spot = new Vector3((Random.value < 0.5f ? -1f : 1f) * Random.Range(5.5f, 8f), 0f, Random.Range(2.5f, 5f));
                if (room.OnFloorLocal(new Vector2(spot.x, spot.z), 2.5f))
                {
                    Rubble(t, spot, 3.2f, false);
                    if (Landmark(giantSwordPrefab, t, spot + Vector3.down * 0.4f, Random.Range(-30f, 30f), 5.2f) is GameObject sword)
                        sword.transform.localRotation *= Quaternion.Euler(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f));
                    else
                        Stump(t, spot, 2.2f, false);
                    // (the blade and its heap stand in the way as one round block)
                    var block = new GameObject("SwordCollider");
                    block.transform.SetParent(t, false);
                    block.transform.localPosition = spot + Vector3.up * 2f;
                    var cap = block.AddComponent<CapsuleCollider>();
                    cap.radius = 1.1f;
                    cap.height = 4f;
                    placed.Add((spot, 2.4f));
                    SoftLight(t, spot + Vector3.up * 3.5f, new Color(1f, 0.7f, 0.45f), 9f, 1.6f);
                }
                break;
            }
            case nameof(HallShape.Platforms):
            {
                // The middle platform holds a Bifrost crystal turning over a stepped dais
                if (obeliskPrefab != null)
                {
                    float top = Dais(t, Vector3.zero, 2.6f, 2);
                    GameObject crystal = Landmark(obeliskPrefab, t, Vector3.up * (top + 0.3f), Random.Range(0f, 360f), 3.6f);
                    if (crystal != null) crystal.AddComponent<FloatBob>();
                    FlatMesh(t, "CrystalRing", AnnulusMesh(3.6f, 3.8f, 96), 0.011f, SoftGold);
                    Ambient.RuneHalo(t, Vector3.up * (top + 0.02f), 2f, new Color(0.6f, 0.8f, 1f) * 0.8f);
                    SoftLight(t, new Vector3(0f, 4f, -1f), new Color(0.7f, 0.85f, 1f), 12f, 1.8f);
                    placed.Add((Vector3.zero, 3.6f));
                    centreTaken = true;
                }
                // Gold-edged walks on the bridges, and braziers burning at the outer corners of the side platforms
                // (the bridges run from the middle platform's edge, 8 m out east-west and 7 m north-south, to the side ones)
                foreach (Vector2Int d in Directions)
                {
                    float from = d.x != 0 ? 8f : 7f, to = d.x != 0 ? 14f : 13f;
                    Vector2 mid = new Vector2(d.x, d.y) * (from + to) / 2f;
                    if (!room.OnFloorLocal(mid, 0f))
                        continue;
                    bool alongZ = d.y != 0;
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Vector3 at = alongZ ? new Vector3(s * 2.65f, 0.011f, mid.y) : new Vector3(mid.x, 0.011f, s * 2.65f);
                        Vector3 sz = alongZ ? new Vector3(0.16f, 0.01f, to - from) : new Vector3(to - from, 0.01f, 0.16f);
                        Mats.Shape(PrimitiveType.Cube, t, at, sz, GoldInlay, false, "BridgeEdge");
                    }
                }
                foreach (Vector2Int d in Directions)
                {
                    Vector3 n = new Vector3(d.x, 0f, d.y);
                    Vector3 across = Vector3.Cross(Vector3.up, n);
                    float reach = (d.x != 0 ? hx : hz) - 1.6f;
                    if (!room.OnFloorLocal(new Vector2(n.x, n.z) * reach, 0.5f) || d == Vector2Int.down)
                        continue;
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Vector3 p = n * (reach - 1f) + across * s * 3.6f;
                        if (!room.OnFloorLocal(new Vector2(p.x, p.z), 0.8f) || placed.Any(o => Vector3.Distance(o.p, p) < o.r + 1f))
                            continue;
                        Brazier(t, p, true);
                        placed.Add((p, 1.1f));
                    }
                }
                break;
            }
            case nameof(HallShape.Asymmetric):
            {
                // A shrine floats up out of the far open corner (a valkyrie keeping watch on it); a chained wolf of
                // stone crouches in the big wing's quiet corner
                foreach (Rect v in room.Voids)
                {
                    if (v.center.y < 0f || v.width < 6f || v.height < 6f)
                        continue;
                    var c = new Vector3(v.center.x, 0f, v.center.y + 0.5f);
                    var size = new Vector2(Mathf.Min(v.width - 3f, 8f), Mathf.Min(v.height - 3f, 6.5f));
                    Transform shrine = VoidIsland(t, c, size, 1.3f);
                    if (statuePrefab != null)
                        Place(statuePrefab, shrine, Vector3.zero, 180f, 1.15f, false);
                    foreach (float s in new[] { -1f, 1f })
                        Brazier(shrine, new Vector3(s * (size.x / 2f - 0.9f), 0f, -size.y / 2f + 0.9f), false);
                    SoftLight(t, c + Vector3.up * 4f, warm, 10f, 1.4f);
                }
                foreach (Rect v in room.Voids)
                {
                    if (v.center.y >= 0f || v.width < 6f || v.height < 6f)
                        continue;
                    // the near open corner: a sunken shrine well below the floor, so it hides nothing
                    var c = new Vector3(v.center.x, 0f, v.center.y);
                    Transform low = VoidIsland(t, c, new Vector2(Mathf.Min(v.width - 3f, 7f), Mathf.Min(v.height - 3f, 5f)), -2.4f);
                    if (goldenTreePrefab != null)
                        Place(goldenTreePrefab, low, Vector3.zero, Random.Range(0f, 360f), 0.8f, false);
                }
                // the wolf, in the corner of the floor farthest from every door and lane
                Vector3 best = Vector3.zero;
                float bestScore = -1f;
                for (int k = 0; k < 40; k++)
                {
                    var p = new Vector3(Random.Range(-hx + 3f, hx - 3f), 0f, Random.Range(-2f, hz - 3f));
                    if (!room.OnFloorLocal(new Vector2(p.x, p.z), 2.4f) || Mathf.Abs(p.x) < 4f || Mathf.Abs(p.z) < 4f || placed.Any(o => Vector3.Distance(o.p, p) < o.r + 2.4f))
                        continue;
                    float score = p.magnitude;
                    if (score > bestScore) { bestScore = score; best = p; }
                }
                if (bestScore > 0f && fenrirPrefab != null)
                {
                    float top = Dais(t, best, 2.1f, 2);
                    Landmark(fenrirPrefab, t, best + Vector3.up * top, FacingIn(best), 2.6f);
                    placed.Add((best, 2.6f));
                    SoftLight(t, best + Vector3.up * 3f, new Color(1f, 0.6f, 0.4f), 8f, 1.2f);
                }
                break;
            }
            case nameof(HallShape.Concourse):
            {
                // Candelabras flank the corridor mouths; a valkyrie stands in the alcove looking down the hall
                foreach (Vector2Int d in zoneExits)
                {
                    Vector3 n = new Vector3(d.x, 0f, d.y);
                    Vector3 across = Vector3.Cross(Vector3.up, n);
                    float mouth = (d.x != 0 ? 11f : 9f) - 1.2f;
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Vector3 p = n * mouth + across * s * 4.4f;
                        if (candelabraPrefab == null || placed.Any(o => Vector3.Distance(o.p, p) < o.r + 0.8f))
                            continue;
                        Place(candelabraPrefab, t, p, Random.Range(0f, 360f), 1f, true);
                        placed.Add((p, 0.9f));
                        CandleLight(t, p + Vector3.up * 2.7f);
                    }
                }
                foreach (Vector2Int d in Directions)
                {
                    if (zoneExits.Contains(d) || d == Vector2Int.down)
                        continue;
                    Vector3 n = new Vector3(d.x, 0f, d.y);
                    float back = d.x != 0 ? 14f : 12f;
                    if (!room.OnFloorLocal(new Vector2(n.x, n.z) * back, 0.5f))
                        continue;
                    Vector3 p = n * back;
                    float top = Dais(t, p, 1.5f, 2);
                    if (statuePrefab != null)
                        Place(statuePrefab, t, p + Vector3.up * top, Quaternion.LookRotation(-n).eulerAngles.y, 1.1f, false);
                    placed.Add((p, 2f));
                    SoftLight(t, p + Vector3.up * 3f - n, warm, 8f, 1.4f);
                    break;
                }
                break;
            }
        }
    }

    // Broken columns, fallen drums and heaps of rubble out on the floor: cover to fight round, and something in the
    // middle ground of every hall. Clear of the doors, the carpets, the middle and the centrepiece; on the camera's
    // side only low pieces.
    private void ScatterRuins(Room room, Room.Kind kind, List<Vector2Int> exits, List<(Vector3 p, float r)> placed)
    {
        float amount;
        switch (kind)
        {
            case Room.Kind.Combat:
                amount = room.Shape switch
                {
                    nameof(HallShape.Ruins) => 1.7f,
                    nameof(HallShape.Classic) => 0.7f,
                    nameof(HallShape.Asymmetric) => 0.6f,
                    nameof(HallShape.Colonnade) => 0.5f,
                    nameof(HallShape.Concourse) => 0.5f,
                    nameof(HallShape.Plaza) => 0.5f,
                    nameof(HallShape.Gallery) => 0.3f,
                    nameof(HallShape.Bridge) => 0.25f,
                    _ => 0.4f,
                } * ThemeRuinFactor();
                break;
            case Room.Kind.Start:
            case Room.Kind.Treasure:
            case Room.Kind.Heal:
            case Room.Kind.Vault:
            case Room.Kind.Supports:
            case Room.Kind.Exit:
            case Room.Kind.Vigor:
            case Room.Kind.Ascend:
            case Room.Kind.Fate:
            case Room.Kind.Essence:
                amount = 0.3f;
                break;
            default:
                return;
        }
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        int count = Mathf.RoundToInt(room.Size.x * room.Size.y / 170f * amount);
        bool OnLane(Vector3 p) => exits.Any(d => d.x != 0 ? Mathf.Abs(p.z) < 2.8f && p.x * d.x > 0f : Mathf.Abs(p.x) < 2.8f && p.z * d.y > 0f);

        for (int n = 0, guard = 0; n < count && guard < count * 25; guard++)
        {
            var p = new Vector3(Random.Range(-hx + 2f, hx - 2f), 0f, Random.Range(-hz + 2f, hz - 2f));
            float r = 1.6f;
            if (!room.OnFloorLocal(new Vector2(p.x, p.z), 1.6f) || p.magnitude < 5f || OnLane(p) || placed.Any(o => Vector3.Distance(o.p, p) < o.r + r))
                continue;
            // (the start hall keeps its north wall for Odin's high seat)
            if (kind == Room.Kind.Start && p.z > hz - 7f)
                continue;
            bool nearCamera = p.z < -hz * 0.3f;
            float roll = Random.value;
            if (roll < 0.36f && !nearCamera)
            {
                Stump(t, p, Random.Range(1f, 1.6f), true);
                // a few chips of it lying round its foot
                if (Random.value < 0.6f)
                    Rubble(t, p + new Vector3(Random.Range(-1.1f, 1.1f), 0f, Random.Range(-1.1f, -0.6f)), 1.1f, false);
                r = 1.3f;
            }
            else if (roll < 0.66f)
            {
                // (lying across the view, east-west give or take, so from the camera it reads as fallen, not standing)
                float yaw = Random.Range(0, 2) * 180f + Random.Range(-30f, 30f);
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;   // (the column's length lies along its x)
                if (!room.OnFloorLocal(new Vector2((p + dir * 2.4f).x, (p + dir * 2.4f).z), 0.8f) || !room.OnFloorLocal(new Vector2((p - dir * 2.4f).x, (p - dir * 2.4f).z), 0.8f)
                    || OnLane(p + dir * 2.4f) || OnLane(p - dir * 2.4f))
                    continue;
                FallenColumn(t, p, 4.4f, yaw, true);
                placed.Add((p + dir * 1.6f, 1f));
                placed.Add((p - dir * 1.6f, 1f));
                r = 1.4f;
            }
            else
            {
                // (a low heap: scenery to walk over, not a wall)
                Rubble(t, p, Random.Range(1.8f, 2.6f), false);
                r = 1.4f;
            }
            placed.Add((p, r));
            n++;
        }
    }
}
