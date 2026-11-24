using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Builds one floor as a set of open zones (a random tree on a grid). Each zone is a wide field
// whose edge is invisible: the land and scenery carry on into the fog. Paths lead out to the
// neighbouring zones. Only the zone the player is in is shown.
public partial class DungeonGenerator : MonoBehaviour
{
    private const float Spacing = 200f;     // zones are far apart in the world; travel is a fade + teleport
    private const float ExitWidth = 4f;
    private const float ArenaRadius = 17f;  // the guardian's round arena
    private const float GiantsArenaRadius = 25f;   // Niflheim's giants: the wide frozen river Elivagar
    private const int ArenaPillars = 12;

    private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    [Header("Asgard props (VARCO models; simple shapes stand in for any that are missing)")]
    [SerializeField] private GameObject gatePrefab;
    [SerializeField] private GameObject runeStonePrefab;
    [SerializeField] private GameObject ruinPillarPrefab;
    [SerializeField] private GameObject brazierPrefab;
    [SerializeField] private GameObject goldenTreePrefab;
    [SerializeField] private Texture2D groundTexture;       // cosmic star-stone; plain coloured ground without it

    [Header("Boss arena (VARCO; simple shapes stand in for any that are missing)")]
    [SerializeField] private Texture2D arenaFloorTexture;   // a round rune-carved floor filling the image
    [SerializeField] private GameObject arenaPillarPrefab;
    [SerializeField] private GameObject arenaWallPrefab;
    [SerializeField] private float groundTileSize = 7f;     // metres per texture repeat

    [Header("Asgard palace halls (VARCO; the realm keeps its open look without the floor texture)")]
    [SerializeField] private Texture2D palaceFloorTexture;  // marble tiles with gold joints, a 4x4 grid per image
    [SerializeField] private float palaceTileSize = 10f;    // metres per image repeat
    [SerializeField] private Texture2D carpetTexture;       // a crimson runner running down the image
    [SerializeField] private GameObject palaceColumnPrefab;
    [SerializeField] private GameObject statuePrefab;
    [SerializeField] private GameObject bannerPrefab;
    [SerializeField] private GameObject palaceWallPrefab;   // a tall arched-window wall section, one module
    [SerializeField] private Texture2D palaceWindowTexture; // the starry sky seen through doorways
    [SerializeField] private GameObject candelabraPrefab;
    [SerializeField] private GameObject thronePrefab;

    [Header("Asgard hall centrepieces and shop (VARCO; halls and shop keep their old look without them)")]
    [SerializeField] private GameObject fountainPrefab;      // a rainbow-water fountain for the fountain court
    [SerializeField] private GameObject obeliskPrefab;       // a Bifrost crystal on a rune pedestal
    [SerializeField] private GameObject shopCounterPrefab;   // the merchant's marble counter
    [SerializeField] private GameObject shopkeeperPrefab;    // who stands behind it (Brokkr)
    [SerializeField] private GameObject treasureChestPrefab;
    [SerializeField] private GameObject weaponRackPrefab;

    [Header("Heimdall's arena backdrop (VARCO; Asgard only)")]
    [SerializeField] private GameObject himinbjorgPrefab;    // Heimdall's watchtower, standing beyond the far wall
    [SerializeField] private GameObject bifrostGatePrefab;   // the gate of the Bifrost before it


    private readonly List<Room> rooms = new List<Room>();
    private Material floorMat, patchMat, outsideMat, outsidePatchMat, rockMat, rockMat2, oreMat, accentMat, barrierMat, pathMat;
    private int realmIndex;
    private bool palace;

    // The hall art by name, for the opening (null when this project doesn't have that piece)
    public GameObject Prop(string name) => name switch
    {
        "tree" => goldenTreePrefab, "pillar" => ruinPillarPrefab, "runestone" => runeStonePrefab, "brazier" => brazierPrefab,
        "gate" => gatePrefab, "column" => palaceColumnPrefab, "statue" => statuePrefab, "banner" => bannerPrefab,
        "throne" => thronePrefab, "tower" => himinbjorgPrefab, "bifrost" => bifrostGatePrefab, "fountain" => fountainPrefab,
        "obelisk" => obeliskPrefab, _ => null,
    };
    private Texture2D ground;      // the cosmic star-stone the halls float over

    public IReadOnlyList<Room> Rooms => rooms;
    public Room StartRoom => rooms.Count > 0 ? rooms[0] : null;

    public static Vector3 WorldOf(Vector2Int grid) => new Vector3(grid.x * Spacing, 0f, grid.y * Spacing);

    public void Clear()
    {
        rooms.Clear();
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            // (hidden at once: the next area is built in the same place before the old one is gone)
            transform.GetChild(i).gameObject.SetActive(false);
            Destroy(transform.GetChild(i).gameObject);
        }
    }

    // Shows only the given zone (and its enemies)
    public void Activate(Room room)
    {
        // (the others hidden first: a zone with its own sky (HeavenSky) puts the old one back as it goes, before the
        // next one takes it over)
        foreach (Room r in rooms)
            if (r != room)
                r.gameObject.SetActive(false);
        if (room != null && rooms.Contains(room))
            room.gameObject.SetActive(true);
    }

    // ── Asgard's route (갈림길): one area at a time, its halls in a winding chain (see Route) ──
    private int areaStep;   // which area of the route this is (0: the starting area, with Odin's high seat)
    private int areaGold;   // the vault's hoard for this area

    // midBossHall: the hall of the chain (an ordinary fight hall) that the mid-boss also joins, or -1
    public void GenerateArea(Realm realm, int floorIndex, Transform player, IList<Room.Kind> chain, int step, int midBossHall = -1)
    {
        Prepare(realm, floorIndex);
        areaStep = step;
        areaGold = 180 + step * 30;
        // A self-avoiding walk on the grid (each new hall touches only the one before it), so the small map reads as a path
        List<Vector2Int> cells = null;
        for (int attempt = 0; attempt < 60 && cells == null; attempt++)
        {
            var walk = new List<Vector2Int> { Vector2Int.zero };
            var used = new HashSet<Vector2Int> { Vector2Int.zero };
            while (walk.Count < chain.Count)
            {
                Vector2Int at = walk[walk.Count - 1];
                var options = Directions.Where(d => !used.Contains(at + d) && Directions.Count(e => used.Contains(at + d + e)) == 1).ToList();
                if (options.Count == 0) break;
                // the way mostly heads on (up the map), now and then turning
                Vector2Int pick = options.Contains(Vector2Int.up) && Random.value < 0.45f ? Vector2Int.up : options[Random.Range(0, options.Count)];
                walk.Add(at + pick);
                used.Add(at + pick);
            }
            if (walk.Count == chain.Count) cells = walk;
        }
        if (cells == null)
            cells = Enumerable.Range(0, chain.Count).Select(i => new Vector2Int(0, i)).ToList();

        // The way through is cells[0..chain.Count-1]; side branches (short dead ends of fight halls) hang off the fight
        // halls along it, so the map from the entrance to the waystone isn't a single corridor
        var allCells = new List<Vector2Int>(cells);
        var kinds = new List<Room.Kind>(chain);
        var parent = Enumerable.Range(0, chain.Count).Select(i => i - 1).ToList();   // the hall each one is reached from
        if (!chain.Contains(Room.Kind.Boss))
        {
            var used = new HashSet<Vector2Int>(cells);
            int branches = Random.Range(1, 3);
            var hosts = Enumerable.Range(1, Mathf.Max(0, chain.Count - 2))
                .Where(i => chain[i] == Room.Kind.Combat && i != midBossHall).OrderBy(_ => Random.value).ToList();
            foreach (int host in hosts)
            {
                if (branches == 0) break;
                int at = host, grown = 0, length = Random.Range(1, 3);
                for (int k = 0; k < length; k++)
                {
                    Vector2Int from = allCells[at];
                    // (each new hall touches only the one it opens from, like the way through)
                    var options = Directions.Where(d => !used.Contains(from + d) && Directions.Count(e => used.Contains(from + d + e)) == 1).ToList();
                    if (options.Count == 0) break;
                    Vector2Int to = from + options[Random.Range(0, options.Count)];
                    used.Add(to);
                    allCells.Add(to);
                    kinds.Add(Room.Kind.Combat);
                    parent.Add(at);
                    at = allCells.Count - 1;
                    grown++;
                }
                if (grown > 0) branches--;
            }
        }

        var depths = new List<int>();
        for (int i = 0; i < allCells.Count; i++)
        {
            var exits = new List<Vector2Int>();
            for (int j = 0; j < allCells.Count; j++)
                if (parent[j] == i || parent[i] == j)
                    exits.Add(allCells[j] - allCells[i]);
            // deeper areas fight harder (the guardian's halls keep their own strength); a side branch as hard as one
            // hall further on from where it opens
            int depth = i < chain.Count ? Mathf.Min(step + i / 2, 7) : Mathf.Min(depths[parent[i]] + 1, 7);
            depths.Add(depth);
            // the fight hall Týr joins is built as his own battleground (the octagon with the giant swords and Fenrir)
            warHall = i == midBossHall && kinds[i] == Room.Kind.Combat && WarArena;
            rooms.Add(BuildZone(allCells[i], kinds[i], depth, exits, realm, player));
            warHall = false;
            if (i == midBossHall)
                rooms[rooms.Count - 1].AddMidBoss();
        }
        for (int i = 1; i < rooms.Count; i++)
        {
            Room a = rooms[parent[i]], b = rooms[i];
            a.Exits[allCells[i] - allCells[parent[i]]] = b; a.Neighbours.Add(b);
            b.Exits[allCells[parent[i]] - allCells[i]] = a; b.Neighbours.Add(a);
        }
        Activate(StartRoom);
    }

    public void Generate(Realm realm, int floorIndex, Transform player)
    {
        Prepare(realm, floorIndex);
        areaStep = 0;
        BuildTree(realm, player);
    }

    private void Prepare(Realm realm, int floorIndex)
    {
        Clear();
        realmIndex = realm.style;
        palace = realm.palace && palaceFloorTexture != null;
        // Asgard floats in the cosmos
        ground = groundTexture;
        string tag = "realm" + floorIndex;
        floorMat = Mats.Get(tag + "_floor", realm.floorColor, 0.05f);
        patchMat = Mats.Get(tag + "_patch", Color.Lerp(realm.floorColor, Color.black, 0.18f), 0.05f);
        outsideMat = Mats.Get(tag + "_outside", Color.Lerp(realm.floorColor, Color.black, 0.35f), 0.05f);
        outsidePatchMat = Mats.Get(tag + "_outside_patch", Color.Lerp(realm.floorColor, Color.black, 0.48f), 0.05f);
        // Exit paths glow faintly, like a strip of the Bifrost leading on
        pathMat = Mats.Get(tag + "_path", Color.Lerp(realm.floorColor, realm.accentColor, 0.5f), 0.3f, realm.accentColor * 0.35f);
        rockMat = Mats.Get(tag + "_rock", realm.wallColor, 0.1f);
        rockMat2 = Mats.Get(tag + "_rock2", Color.Lerp(realm.wallColor, Color.black, 0.25f), 0.1f);
        accentMat = Mats.Get(tag + "_accent", realm.accentColor, 0.5f, realm.accentColor * 0.8f);
        barrierMat = Mats.Get(tag + "_barrier", realm.accentColor, 0.8f, realm.accentColor * 2f);
        oreMat = Mats.Get("ore_gold", new Color(0.62f, 0.52f, 0.3f), 0.6f, new Color(0.25f, 0.18f, 0.02f));
    }

    private void BuildTree(Realm realm, Transform player)
    {
        // Grow a tree of zones: each new zone touches exactly one existing zone. A linear realm wants its far end at
        // least this many zones from the start, and grows the tree again (a few tries) until it is.
        int wantDepth = Mathf.RoundToInt(realm.rooms * realm.linearity * 0.75f);
        Dictionary<Vector2Int, Vector2Int?> layout = null;
        List<Vector2Int> order = null;
        Dictionary<Vector2Int, int> depth = null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            layout = new Dictionary<Vector2Int, Vector2Int?> { [Vector2Int.zero] = null };
            order = new List<Vector2Int> { Vector2Int.zero };
            int guard = 2000;
            while (order.Count < realm.rooms && guard-- > 0)
            {
                // A linear realm mostly grows on from its newest zones, so the way to its guardian runs long with
                // short side branches; otherwise any zone may sprout the next
                Vector2Int from = Random.value < realm.linearity
                    ? order[Mathf.Max(0, order.Count - 1 - Random.Range(0, 3))]
                    : order[Random.Range(0, order.Count)];
                Vector2Int to = from + Directions[Random.Range(0, 4)];
                if (layout.ContainsKey(to) || Directions.Count(d => layout.ContainsKey(to + d)) != 1)
                    continue;
                layout[to] = from;
                order.Add(to);
            }

            // Depth: exits from the start
            depth = new Dictionary<Vector2Int, int>();
            foreach (Vector2Int cell in order)
                depth[cell] = layout[cell].HasValue ? depth[layout[cell].Value] + 1 : 0;
            if (depth.Values.Max() >= wantDepth)
                break;
        }

        // Depth and dead ends decide the special zones
        var leaves = order.Where(c => c != Vector2Int.zero && !order.Any(o => layout[o] == c))
            .OrderByDescending(c => depth[c]).ToList();

        var kinds = order.ToDictionary(c => c, c => Room.Kind.Combat);
        kinds[Vector2Int.zero] = Room.Kind.Start;
        // (every floor has an upgrade room, 강화방, as well)
        var specials = new Queue<Room.Kind>(new[] { Room.Kind.Boss, Room.Kind.Treasure, Room.Kind.Shop, Room.Kind.Forge });
        foreach (Vector2Int leaf in leaves)
            if (specials.Count > 0) kinds[leaf] = specials.Dequeue();
        foreach (Vector2Int cell in order.OrderByDescending(c => depth[c]))
            if (specials.Count > 0 && kinds[cell] == Room.Kind.Combat && depth[cell] > 1) kinds[cell] = specials.Dequeue();

        // The realm's mid-boss holds the zone right before the guardian's arena, so the way to the boss runs through it
        // (a treasure room or shop standing there moves to another fight zone)
        Vector2Int bossCell = kinds.FirstOrDefault(k => k.Value == Room.Kind.Boss).Key;
        if (realm.HasMidBoss && kinds.ContainsKey(bossCell) && layout[bossCell] is Vector2Int before && kinds[before] != Room.Kind.Start)
        {
            Room.Kind displaced = kinds[before];
            kinds[before] = Room.Kind.MidBoss;
            if (displaced != Room.Kind.Combat)
            {
                Vector2Int spare = order.Where(c => kinds[c] == Room.Kind.Combat).OrderByDescending(c => depth[c]).FirstOrDefault();
                if (kinds.TryGetValue(spare, out Room.Kind k) && k == Room.Kind.Combat)
                    kinds[spare] = displaced;
            }
        }

        var byCell = new Dictionary<Vector2Int, Room>();
        foreach (Vector2Int cell in order)
        {
            var exits = Directions.Where(d => layout.ContainsKey(cell + d) && (layout[cell + d] == cell || layout[cell] == cell + d)).ToList();
            Room room = BuildZone(cell, kinds[cell], depth[cell], exits, realm, player);
            byCell[cell] = room;
            rooms.Add(room);
        }
        foreach (Room room in rooms)
            foreach (Vector2Int d in Directions)
                if (byCell.TryGetValue(room.Grid + d, out Room other) && (layout[other.Grid] == room.Grid || layout[room.Grid] == other.Grid))
                {
                    room.Exits[d] = other;
                    room.Neighbours.Add(other);
                }
        Activate(StartRoom);
    }

    private Vector2 SizeFor(Room.Kind kind)
    {
        switch (kind)
        {
            case Room.Kind.Start: return new Vector2(24f, 20f);
            case Room.Kind.Treasure:
            case Room.Kind.Forge:
            case Room.Kind.Supports:
            case Room.Kind.Heal:
            case Room.Kind.Vault:
            case Room.Kind.Vigor:
            case Room.Kind.Ascend:
            case Room.Kind.Fate:
            case Room.Kind.Essence:
            case Room.Kind.Shop: return new Vector2(Random.Range(22f, 26f), Random.Range(18f, 22f));
            case Room.Kind.Exit: return new Vector2(22f, 20f);
            case Room.Kind.Boss: return new Vector2(ArenaRadius * 2f, ArenaRadius * 2f); // the round arena's bounding square
            // The mid-boss's battleground: the octagon's bounding square (a hall instead if its models are missing)
            case Room.Kind.Combat when warHall: return Vector2.one * WarArenaRadius * 2f;
            case Room.Kind.MidBoss: return WarArena ? Vector2.one * WarArenaRadius * 2f
                : realmIndex == Realm.StyleNiflheim ? new Vector2(ArenaRadius * 2f, ArenaRadius * 2f)   // the frost giants' round arena
                : new Vector2(32f, 28f);
            // Fighting zones: big enough to dodge in, small enough that the fight stays on screen
            default: return new Vector2(Random.Range(26f, 32f), Random.Range(22f, 26f));
        }
    }

    private Room BuildZone(Vector2Int cell, Room.Kind kind, int depth, List<Vector2Int> exits, Realm realm, Transform player)
    {
        ruinedRim = false;
        var root = new GameObject($"Zone_{cell.x}_{cell.y}_{kind}");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = WorldOf(cell);
        Vector2 size = SizeFor(kind);
        // Asgard's fight halls come in different shapes (a long gallery, a cross, a cloister, a bridge...)
        HallShape shape = HallShape.Classic;
        List<Rect> floorRects = null, voidRects = null;
        // Asgard's fight halls are built as one of eight maps (see DungeonGenerator.HallThemes), each on its own floor plan
        hallTheme = HallTheme.None;
        if (kind == Room.Kind.Combat && ground != null && palace && !warHall)
        {
            hallTheme = PickTheme();
            shape = ThemeShape(hallTheme);
            if (shape != HallShape.Classic)
            {
                // (the maps are big: the shaped plans grown to match)
                ShapeLayout(shape, exits, out size, out floorRects, out voidRects);
                ScaleLayout(ThemeShapeScale, ref size, floorRects, voidRects);
            }
            else
                size = ThemeSize(hallTheme);
            root.name += "_" + shape + "_" + hallTheme;
        }
        // (Niflheim's fight zones too: frozen ground broken by crevasses instead of halls over the cosmos)
        else if (kind == Room.Kind.Combat && ground != null && (palace || realmIndex == Realm.StyleNiflheim) && !warHall)
        {
            shape = PickShape(exits);
            if (shape != HallShape.Classic)
                ShapeLayout(shape, exits, out size, out floorRects, out voidRects);
            root.name += "_" + shape;
        }
        // Asgard's very first hall: the celestial gate over the sunset clouds (see DungeonGenerator.CelestialStart)
        else if (CelestialStartZone(kind))
        {
            LoadThemeProps();
            LoadHallProps();
            LoadHeaven();
            LoadCelestial();
            hallTheme = HallTheme.CelestialGate;
            size = CelestialSize;
            root.name += "_Celestial";
        }
        var room = root.AddComponent<Room>();
        room.Init(kind, cell, depth, size, realm, player);
        room.SetFloor(floorRects, voidRects, shape.ToString());
        if (hallTheme != HallTheme.None)
            room.AddBudget(ThemeBudget(size));
        float hx = size.x / 2f, hz = size.y / 2f;
        Transform t = root.transform;

        // Ground running far past the edge into the fog. The playable field is a shade lighter than the land
        // around it, so the edge reads without a wall; darker patches keep either from looking like a flat sheet.
        const float beyond = 30f;
        Vector2 groundSize = new Vector2(size.x + beyond * 2f, size.y + beyond * 2f);
        // The room's floor runs right up to its walls (the arena's round floor sits on top of it)
        // (a themed map floating in the sky has nothing past what you can walk on)
        float fieldRim = OpenAir ? FloorLip : WallSetback;
        Vector2 fieldSize = new Vector2(size.x + fieldRim * 2f, size.y + fieldRim * 2f);
        if (ground != null)
        {
            // The ground texture, tiled at the same scale inside and out, so only its brightness marks the edge
            GameObject groundPiece = Mats.Shape(PrimitiveType.Cube, t, new Vector3(0f, -0.11f, 0f), new Vector3(groundSize.x, 0.2f, groundSize.y), TiledGround(realm, groundSize, false), true, "Ground");
            // Heimdall's arena is no hall at all: a round altar floating in the daylit sky (see HeavenArena)
            if (palace && kind == Room.Kind.Boss)
            {
                HeavenArena(room, exits, realm, player, groundPiece);
                return FinishZone(room, kind, depth, exits);
            }
            // The mid-boss's battleground is an open arena of its own, out in the cosmos with no hall round it
            bool warArena = IsWarZone(kind);
            // In Asgard the field is a marble hall floor floating in the cosmos; the stars stay outside it
            if (!warArena)
            {
                GameObject field = Mats.Shape(PrimitiveType.Cube, t, new Vector3(0f, -0.095f, 0f), new Vector3(fieldSize.x, 0.2f, fieldSize.y),
                    palace ? PalaceFloor(fieldSize) : TiledGround(realm, fieldSize, true), false, "Field");
                // (Asgard's marble is laid tile by tile over it; the guardian's arena keeps its own round floor;
                // a themed map has its own painted floor)
                if (palace && kind != Room.Kind.Boss)
                {
                    if (ThemeFloor != null)
                        PaintedFloor(room, fieldSize, field, ThemeFloor);
                    else
                        PalaceTiles(room, fieldSize, field);
                }
            }
            // (an open-air map is day over the clouds: no ground out there, no stars)
            if (OpenAir)
                groundPiece.GetComponent<Renderer>().enabled = false;
            else
            {
                StarGlints(t, hx + beyond, hz + beyond, (size.x + beyond) * (size.y + beyond) / 5f,
                    warArena ? Vector2.one * (WarArenaRadius + 2.5f) : palace ? fieldSize / 2f : Vector2.zero);
                t.gameObject.AddComponent<StarMotes>().Init(hx + 12f, hz + 12f, 70);
            }
            if (warArena)
            {
                BuildWarArena(room, exits, player);
                return FinishZone(room, kind, depth, exits);
            }
            if (kind == Room.Kind.Boss || GiantsArenaZone(kind))
                BuildArena(room, exits, realm, player);
            else if (room.Floor != null)
            {
                if (palace)
                {
                    // (the open cosmos is a real drop now: the ground is cut away under it; over the clouds,
                    // only the floor's footing hangs below)
                    if (!OpenAir)   // (a map in the sky: only its own floor slab, the clouds below)
                        OpenAbyss(room);
                    CutGround(room, groundPiece, groundSize, realm);
                }
                else
                    FrozenAbyss(room);
                BuildShapedEdge(room, exits, player);
            }
            else
                BuildEdge(room, exits, hx, hz, player);
            if (OpenAir)
            {
                zoneExits = exits;
                OpenAirFrame(room, exits);
            }
            else
            {
                RoomWalls(room, exits, realm);
                OutsideScenery(room, exits);
            }
            return FinishZone(room, kind, depth, exits);
        }
        Mats.Shape(PrimitiveType.Cube, t, new Vector3(0f, -0.11f, 0f), new Vector3(groundSize.x, 0.2f, groundSize.y), outsideMat, true, "Ground");
        Mats.Shape(PrimitiveType.Cube, t, new Vector3(0f, -0.095f, 0f), new Vector3(fieldSize.x, 0.2f, fieldSize.y), floorMat, false, "Field");
        int patches = Mathf.RoundToInt((size.x + beyond) * (size.y + beyond) / 70f);
        for (int i = 0; i < patches; i++)
        {
            var p = new Vector3(Random.Range(-hx - beyond, hx + beyond), 0.008f, Random.Range(-hz - beyond, hz + beyond));
            bool inside = Mathf.Abs(p.x) < hx && Mathf.Abs(p.z) < hz;
            if (inside)
            {
                // Keep inside patches off the edge line
                p.x = Mathf.Clamp(p.x, -hx + 3f, hx - 3f);
                p.z = Mathf.Clamp(p.z, -hz + 3f, hz - 3f);
            }
            else if (Mathf.Abs(p.x) < hx + 3f && Mathf.Abs(p.z) < hz + 3f)
                continue;
            GameObject patch = Mats.Shape(PrimitiveType.Cylinder, t, p, new Vector3(Random.Range(2f, 6f), 0.005f, Random.Range(2f, 5f)), inside ? patchMat : outsidePatchMat);
            patch.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
        }

        if (kind == Room.Kind.Boss || GiantsArenaZone(kind))
            BuildArena(room, exits, realm, player);
        else
            BuildEdge(room, exits, hx, hz, player);
        RoomWalls(room, exits, realm);
        OutsideScenery(room, exits);
        return FinishZone(room, kind, depth, exits);
    }

    private Room FinishZone(Room room, Room.Kind kind, int depth, List<Vector2Int> exits)
    {
        var placed = new List<(Vector3 p, float r)>();
        zoneExits = exits;
        centreTaken = false;
        foreach (Vector2Int d in exits)
            placed.Add((room.EntrancePoint(d) - room.transform.position, 4.5f)); // keep the paths in clear
        // (a themed map places its own centrepiece)
        if (hallTheme == HallTheme.None)
            placed.Add((Vector3.zero, kind == Room.Kind.Combat ? 2f : 4f));
        // A shaped hall: nothing is set down out over its open cosmos, and its own landmarks go in first
        if (room.Floor != null)
        {
            for (float x = -room.Size.x / 2f; x <= room.Size.x / 2f; x += 1.5f)
                for (float z = -room.Size.y / 2f; z <= room.Size.y / 2f; z += 1.5f)
                    if (!room.OnFloorLocal(new Vector2(x, z), -0.3f))
                        placed.Add((new Vector3(x, 0f, z), 1.1f));
            ShapeLandmarks(room, placed);
        }
        // Asgard's halls: broken columns and rubble out on the floor (the fight halls' middle ground, the quiet halls' age)
        if (palace && !IsWarZone(kind) && hallTheme != HallTheme.CelestialGate)
            ScatterRuins(room, kind, exits, placed);

        // (the shop lays its wares on a medallion in the middle instead)
        bool palaceShop = (kind == Room.Kind.Shop || kind == Room.Kind.Forge) && shopCounterPrefab != null;
        // Fight halls keep their middle for the centrepiece: the carpets begin at its gold ring
        bool centrepiece = kind == Room.Kind.Combat && (fountainPrefab != null || obeliskPrefab != null);
        bool warArena = IsWarZone(kind);
        // (the rune sanctum's ritual circle and the market's rug take the floor instead)
        bool ownFloor = (kind == Room.Kind.Supports && Route.Prop("RuneMonolith") != null) || (kind == Room.Kind.Shop && Route.Prop("BazaarStall") != null)
            || kind == Room.Kind.Vigor || kind == Room.Kind.Ascend || kind == Room.Kind.Fate || kind == Room.Kind.Essence
            || (kind == Room.Kind.Heal && Route.Prop("HealPool") != null) || (kind == Room.Kind.Vault && Route.Prop("GoldPile") != null);
        if (palace && kind != Room.Kind.Boss && !palaceShop && !warArena && !ownFloor && hallTheme == HallTheme.None)
            Carpets(room, exits, kind == Room.Kind.MidBoss ? 5.8f : centrepiece ? 3.9f : 0f);   // (the oath hall's gold band)
        zoneExits = exits;
        Decorate(room, kind, depth, placed);
        // Then bring it to life: the realm's moving landmarks, everything that sways, burns or flows, and the air itself
        // (a map in the open sky has no walls or balustrade for the wall fountains and hovering lanterns to belong to)
        if (hallTheme == HallTheme.None)
            Ambient.Features(room, realmIndex, palace, placed);
        Ambient.Animate(room.transform, realmIndex, palace, room.Size);
        Ambient.Weather(room.transform, realmIndex, palace, room.Size);
        return room;
    }

    // The ground texture tiled every groundTileSize metres over a piece of this size, tinted toward the realm
    // (a realm's own ground already has its colours and is only darkened outside the field)
    private Material TiledGround(Realm realm, Vector2 size, bool field)
    {
        Color tint = Color.Lerp(Color.white, realm.floorColor * 2f, realm.groundTint);
        tint *= field ? 1f : 0.55f;
        tint.a = 1f;
        var key = $"ground_{realm.name}_{(field ? "in" : "out")}_{size.x:0.0}x{size.y:0.0}";
        Material mat = Mats.Get(key, tint, 0.15f);
        mat.SetTexture("_BaseMap", ground);
        mat.SetTextureScale("_BaseMap", size / groundTileSize);
        return mat;
    }

    // Marble palace tiles, at a fixed size in metres
    private Material PalaceFloor(Vector2 size)
    {
        Material mat = Mats.Get($"palace_floor_{size.x:0.0}x{size.y:0.0}", new Color(0.95f, 0.95f, 0.97f), 0.55f);
        mat.SetTexture("_BaseMap", palaceFloorTexture);
        mat.SetTextureScale("_BaseMap", size / palaceTileSize);
        return mat;
    }

    // Tiny sparkles lying on the stone, like stars caught in the floor (none inside the given half-size). Each colour
    // is one mesh of little diamonds rather than hundreds of separate objects.
    private static void StarGlints(Transform t, float hx, float hz, float count, Vector2 keepClear)
    {
        Material gold = Mats.Get("glint_gold", new Color(1f, 0.9f, 0.6f), 0.5f, new Color(2f, 1.7f, 0.9f));
        Material cyan = Mats.Get("glint_cyan", new Color(0.6f, 0.9f, 1f), 0.5f, new Color(0.9f, 1.6f, 2.2f));
        var golds = new List<Vector3>();
        var cyans = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            var p = new Vector3(Random.Range(-hx, hx), 0.017f, Random.Range(-hz, hz));
            if (Mathf.Abs(p.x) < keepClear.x && Mathf.Abs(p.z) < keepClear.y)
                continue;
            float s = Random.Range(0.05f, 0.12f);
            (Random.value < 0.6f ? golds : cyans).Add(new Vector3(p.x, s, p.z));   // (y carries the size)
        }
        GlintMesh(t, "Glints_Gold", golds, gold);
        GlintMesh(t, "Glints_Cyan", cyans, cyan);
    }

    private static void GlintMesh(Transform t, string name, List<Vector3> glints, Material mat)
    {
        if (glints.Count == 0)
            return;
        var vertices = new Vector3[glints.Count * 4];
        var triangles = new int[glints.Count * 6];
        for (int i = 0; i < glints.Count; i++)
        {
            Vector3 g = glints[i];
            float r = g.y * 0.707f;   // a square of side s turned 45°: its corners on the axes
            var c = new Vector3(g.x, 0f, g.z);
            vertices[i * 4] = c + new Vector3(0f, 0f, -r);
            vertices[i * 4 + 1] = c + new Vector3(-r, 0f, 0f);
            vertices[i * 4 + 2] = c + new Vector3(r, 0f, 0f);
            vertices[i * 4 + 3] = c + new Vector3(0f, 0f, r);
            int v = i * 4, k = i * 6;
            triangles[k] = v; triangles[k + 1] = v + 1; triangles[k + 2] = v + 2;
            triangles[k + 3] = v + 2; triangles[k + 4] = v + 1; triangles[k + 5] = v + 3;
        }
        var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        FlatMesh(t, name, mesh, 0.017f, mat);
    }

    // Invisible walls on the zone's edge with gaps for the exits, and the exit paths
    private void BuildEdge(Room room, List<Vector2Int> exits, float hx, float hz, Transform player)
    {
        Transform t = room.transform;
        edgePosts.Clear();
        foreach (Vector2Int d in Directions)
        {
            bool exit = exits.Contains(d);
            bool alongX = d.y != 0;                       // north/south edges run along x
            float length = alongX ? room.Size.x : room.Size.y;
            float offset = alongX ? hz : hx;
            Vector3 normal = new Vector3(d.x, 0f, d.y);
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            Vector3 edgeCenter = normal * (offset + 0.5f);

            // Collision
            if (!exit)
                Wall(t, edgeCenter, along, length + 2f);
            else
            {
                float piece = (length + 2f - ExitWidth) / 2f;
                Wall(t, edgeCenter + along * (ExitWidth / 2f + piece / 2f), along, piece);
                Wall(t, edgeCenter - along * (ExitWidth / 2f + piece / 2f), along, piece);
            }

            // (the side toward the camera with low posts, so its columns never hide the fight)
            if (palace)
            {
                lowRim = d == Vector2Int.down;
                PalaceEdge(t, normal * offset, along, length, exit);
                lowRim = false;
            }
            // A low line of pebbles marks the edge (ankle height: a border, not a wall), open at the exit
            else for (float s = -length / 2f; s <= length / 2f; s += Random.Range(0.55f, 0.85f))
            {
                if (exit && Mathf.Abs(s) < ExitWidth / 2f + 0.2f)
                    continue;
                Vector3 p = normal * (offset + Random.Range(0.15f, 0.45f)) + along * s;
                float h = Random.Range(0.12f, 0.26f);
                Mats.Shape(PrimitiveType.Cube, t, p + Vector3.up * h * 0.4f, new Vector3(Random.Range(0.3f, 0.6f), h, Random.Range(0.3f, 0.5f)), Random.value < 0.5f ? rockMat : rockMat2)
                    .transform.localRotation = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0f, 90f), Random.Range(-10f, 10f));
            }

            if (exit)
                BuildExit(room, d, offset, player, true);
        }
    }

    // The path out: a trail running off into the fog under an Asgardian gate, braziers either side (unless the
    // arena's own pillars stand there), and a seal for fights
    private void BuildExit(Room room, Vector2Int d, float offset, Transform player, bool braziers)
    {
        bool alongX = d.y != 0;
        Vector3 normal = new Vector3(d.x, 0f, d.y);
        Vector3 along = alongX ? Vector3.right : Vector3.forward;
        var exitRoot = new GameObject("Exit_" + d);
        exitRoot.transform.SetParent(room.transform, false);
        exitRoot.transform.localPosition = normal * offset;
        // (with a portal the wall closes behind the gate, so the path ends at the gate's back instead of running out
        // through the wall into the void)
        float pathLength = ExitPortal.Available && gatePrefab != null ? 3.4f : 10f;
        Vector3 pathSize = alongX ? new Vector3(ExitWidth, 0.2f, pathLength) : new Vector3(pathLength, 0.2f, ExitWidth);
        Mats.Shape(PrimitiveType.Cube, exitRoot.transform, new Vector3(0f, -0.09f, 0f) + normal * (pathLength / 2f), pathSize, pathMat, true, "Path");
        if (gatePrefab != null)
        {
            // A little wider than the path, low, and set back past the edge, so it frames the way out
            // without hiding the fight; lowest on the south side, between the camera and the player
            GameObject gate = Place(gatePrefab, exitRoot.transform, normal * 1.8f, alongX ? 0f : 90f);
            float height = d == Vector2Int.down ? 0.38f : 0.5f;
            gate.transform.localScale = Vector3.Scale(gate.transform.localScale, new Vector3(1.12f, height, 1f));
            if (ExitPortal.Available)
            {
                // A roof over the gate: from the steep camera its top otherwise shows as a dark hollow between its front
                // and back arches (the portal lives in it now, so it should read as a closed, solid gatehouse)
                float top = 3.52f * height / 0.5f;
                Vector3 roofSize = alongX ? new Vector3(7.2f, 0.3f, 2.5f) : new Vector3(2.5f, 0.3f, 7.2f);
                Mats.Shape(PrimitiveType.Cube, exitRoot.transform, normal * 1.8f + Vector3.up * (top - 0.42f * height / 0.5f), roofSize,
                    Mats.Get("gate_roof", new Color(0.66f, 0.5f, 0.22f), 0.6f), false, "GateRoof");
            }
        }
        else
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 post = along * side * (ExitWidth / 2f + 0.4f);
                Mats.Shape(PrimitiveType.Cube, exitRoot.transform, post + Vector3.up * 1.1f, new Vector3(0.6f, 2.2f, 0.6f), rockMat2, true, "Post");
                Mats.Shape(PrimitiveType.Cube, exitRoot.transform, post + Vector3.up * 1.6f + normal * 0.31f, new Vector3(0.25f, 0.6f, 0.02f), accentMat);
            }
        if (braziers)
            foreach (float side in new[] { -1f, 1f })
                Brazier(exitRoot.transform, -normal * 1.6f + along * side * (ExitWidth / 2f + 2f));
        Vector3 sealSize = alongX ? new Vector3(ExitWidth, 2.4f, 0.2f) : new Vector3(0.2f, 2.4f, ExitWidth);
        GameObject seal = Mats.Shape(PrimitiveType.Cube, exitRoot.transform, Vector3.up * 1.2f, sealSize, barrierMat, true, "Seal");
        room.AddBarrier(seal);
        // The way out shows as a swirling portal: red while the fight seals it, blue and turning once it's open. The
        // seal itself goes unseen then (its collider still bars the way). Lower and fainter on the camera's side.
        if (ExitPortal.Available)
        {
            seal.GetComponent<Renderer>().enabled = false;
            bool south = d == Vector2Int.down;
            if (gatePrefab != null)
            {
                // Filling the gate's arch: its opening is ~3.5 m across and 2.6 m tall at the usual height (less on the
                // south side, where the gate is lower), and the rim ring sits ~0.38 of the quad out from its middle.
                // Just inside the frame's room-side face (the frame runs 0.3 to 3.3 out): the posts and lintel still
                // overlap the outer wisps, but the steep game camera isn't looking at it past the whole lintel.
                float k = (south ? 0.38f : 0.5f) / 0.5f;
                float openingTop = 2.6f * k;
                ExitPortal.Create(exitRoot.transform, room, normal, normal * 0.75f + Vector3.up * (openingTop / 2f),
                    1.75f / 0.38f, openingTop / 2f / 0.38f, south ? 0.6f : 1f);
            }
            else
            {
                float h = south ? 2.8f : 5.2f;
                ExitPortal.Create(exitRoot.transform, room, normal, normal * 1.2f + Vector3.up * (h * 0.42f),
                    south ? ExitWidth * 0.9f : ExitWidth * 1.1f, h, south ? 0.55f : 1f);
            }
        }
        exitRoot.AddComponent<ZoneExit>().Init(room, d, player);
    }

    // ───────────── Room walls ─────────────

    public const float WallSetback = 2.6f;   // how far beyond the zone's edge the walls stand
    public const float WallHeight = 6.5f;

    // Tall walls close in the far and side edges of every zone, so the camera only ever sees the room.
    // The camera's (south) side stays open; the camera itself never looks past it. Doorways are filled by
    // a backdrop: the starry cosmos in Asgard, the realm's darkness elsewhere.
    private void RoomWalls(Room room, List<Vector2Int> exits, Realm realm)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        float module = palace && palaceWallPrefab != null ? palaceWallPrefab.GetComponent<BoxCollider>().size.x : 8f;
        const float opening = ExitWidth / 2f + 1.5f;   // half the doorway's width

        Material stone = Mats.Get("roomwall_" + realm.name, Color.Lerp(realm.wallColor, Color.black, 0.15f), 0.15f);
        Material trim = Mats.Get("roomwall_trim_" + realm.name, Color.Lerp(realm.wallColor, realm.accentColor, 0.5f), 0.4f, realm.accentColor * 0.25f);
        Material backdrop;
        Texture2D sky = palace ? palaceWindowTexture : null;
        if (sky != null)
        {
            backdrop = Mats.Get("doorway_cosmos", Color.white, 0.1f, Color.white);
            backdrop.SetTexture("_BaseMap", sky);
            backdrop.SetTexture("_EmissionMap", sky);
        }
        else
            backdrop = Mats.Get("doorway_" + realm.name, realm.background, 0f, realm.background * 0.6f);

        foreach (Vector2Int d in Directions)
        {
            if (d == Vector2Int.down)
                continue;
            bool alongX = d.y != 0;
            float half = alongX ? hx : hz, offset = (alongX ? hz : hx) + WallSetback;
            Vector3 normal = new Vector3(d.x, 0f, d.y), along = alongX ? Vector3.right : Vector3.forward;
            bool door = exits.Contains(d);

            // Centred on the side, or, round a doorway, working outward from either edge of the opening;
            // on past the corners so the sides meet the far wall
            float first = door ? opening + module / 2f : 0f;
            for (float s = first; s <= half + WallSetback + module; s += module)
                foreach (float sign in s == 0f ? new[] { 1f } : new[] { -1f, 1f })
                    WallModule(t, normal * offset + along * s * sign, normal, module, stone, trim);

            if (door && ExitPortal.Available)
            {
                // The way out is the portal in its gate now, so the wall runs on unbroken behind it: a section narrowed
                // to the opening closes the gap (no notch in the wall's top, nothing open over the gate)
                float gap = opening * 2f + 0.1f;
                if (palace && palaceWallPrefab != null)
                {
                    GameObject fill = Place(palaceWallPrefab, t, normal * offset, Quaternion.LookRotation(normal).eulerAngles.y, 1f, false);
                    fill.name = "DoorwayWall";
                    fill.transform.localScale = Vector3.Scale(fill.transform.localScale, new Vector3(gap / module, 1f, 1f));
                }
                else
                    WallModule(t, normal * offset, normal, gap, stone, trim);
            }
            else if (door)
            {
                var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(plane.GetComponent<Collider>());
                plane.name = "DoorwayBackdrop";
                plane.transform.SetParent(t, false);
                plane.transform.localPosition = normal * (offset + 0.3f) + Vector3.up * WallHeight / 2f;
                plane.transform.localRotation = Quaternion.LookRotation(normal);   // a quad shows its -z side: facing in
                plane.transform.localScale = new Vector3(opening * 2f + 0.6f, WallHeight, 1f);
                plane.GetComponent<Renderer>().sharedMaterial = backdrop;
            }
        }
    }

    private void WallModule(Transform parent, Vector3 local, Vector3 outward, float width, Material stone, Material trim)
    {
        if (palace && palaceWallPrefab != null)
        {
            Place(palaceWallPrefab, parent, local, Quaternion.LookRotation(outward).eulerAngles.y, 1f, false);   // its face is its -z side
            return;
        }
        // Elsewhere: a plain wall of the realm's stone, with a band of its accent along the top and a plinth
        var wall = new GameObject("RoomWall").transform;
        wall.SetParent(parent, false);
        wall.localPosition = local;
        wall.localRotation = Quaternion.LookRotation(outward);
        Mats.Shape(PrimitiveType.Cube, wall, new Vector3(0f, WallHeight / 2f, 0.3f), new Vector3(width + 0.05f, WallHeight, 0.8f), stone);
        Mats.Shape(PrimitiveType.Cube, wall, new Vector3(0f, WallHeight - 0.25f, -0.15f), new Vector3(width + 0.05f, 0.35f, 0.3f), trim);
        Mats.Shape(PrimitiveType.Cube, wall, new Vector3(0f, 0.4f, -0.2f), new Vector3(width + 0.05f, 0.8f, 0.5f), stone);
    }

    // ───────────── Asgard palace halls ─────────────

    private readonly List<Vector3> edgePosts = new List<Vector3>();

    // A balustrade along one side of the hall: marble columns every few metres (and either side of an exit),
    // gold-railed low walls between them
    private void PalaceEdge(Transform t, Vector3 edgeCenter, Vector3 along, float length, bool exit)
    {
        var posts = new List<float>();
        // (a short stretch, such as a bridge's or a platform's side, gets a column at either end only)
        int n = Mathf.Max(length < 8f ? 1 : 2, Mathf.RoundToInt(length / 9f));
        for (int i = 0; i <= n; i++)
        {
            float s = -length / 2f + length * i / n;
            if (!exit || Mathf.Abs(s) > ExitWidth / 2f + 2f)
                posts.Add(s);
        }
        if (exit)
        {
            posts.Add(-(ExitWidth / 2f + 0.9f));
            posts.Add(ExitWidth / 2f + 0.9f);
        }
        posts.Sort();

        for (int i = 0; i < posts.Count; i++)
        {
            Vector3 p = edgeCenter + along * posts[i];
            // Corners are shared by two sides: one column each
            if (!edgePosts.Any(e => (e - p).sqrMagnitude < 0.5f))
            {
                edgePosts.Add(p);
                // (a ruin's balustrade: some of its columns stand broken off; the door posts stay whole)
                bool doorPost = exit && Mathf.Abs(Mathf.Abs(posts[i]) - (ExitWidth / 2f + 0.9f)) < 0.01f;
                if (ruinedRim && !doorPost && Random.value < 0.45f)
                    Stump(t, p, lowRim ? Random.Range(0.7f, 1.1f) : Random.Range(0.8f, 1.8f), true);
                else if (lowRim)
                    LowPost(t, p);
                else
                    PalaceColumn(t, p, 0.9f, true);
            }
            if (i + 1 < posts.Count)
            {
                bool opening = exit && posts[i] < 0f && posts[i + 1] > 0f;
                Vector3 from = p + along * 0.8f, to = edgeCenter + along * posts[i + 1] - along * 0.8f;
                // (and stretches of its rail have fallen: a heap of rubble where they lay; the invisible wall still holds)
                if (!opening && ruinedRim && Random.value < 0.4f)
                    Rubble(t, (from + to) / 2f, Mathf.Min(2.4f, Vector3.Distance(from, to) * 0.6f), false);
                else if (!opening)
                    ArenaWall(t, from, to);
            }
        }
    }

    private void PalaceColumn(Transform parent, Vector3 local, float scale, bool solid)
    {
        if (palaceColumnPrefab != null)
            Place(palaceColumnPrefab, parent, local, Random.Range(0, 4) * 90f, scale, solid);
        else
            Mats.Shape(PrimitiveType.Cylinder, parent, local + Vector3.up * 1.8f * scale, new Vector3(1f, 1.8f, 1f) * scale,
                Mats.Get("palace_marble", new Color(0.92f, 0.92f, 0.95f), 0.6f), solid, "Column");
    }

    // Crimson runners from the middle of the hall (or this far from it) out through every exit, as if laid for a king's entrance
    private void Carpets(Room room, List<Vector2Int> exits, float start = 0f)
    {
        if (carpetTexture == null)
            return;
        Material carpet = Mats.Get("palace_carpet", Color.white, 0.1f);
        carpet.SetTexture("_BaseMap", carpetTexture);
        const float width = 3.2f;
        for (int i = 0; i < exits.Count; i++)
        {
            Vector2Int d = exits[i];
            float edge = (d.x != 0 ? room.Size.x : room.Size.y) / 2f;
            // (in a shaped hall a carpet starts where the floor does: not out over a cloister's open court)
            float from = start;
            if (room.Floor != null)
            {
                float f = 0f;
                while (f < edge && !room.OnFloorLocal(new Vector2(d.x, d.y) * f, 0f))
                    f += 0.25f;
                if (f > 0f)
                    from = Mathf.Max(start, f + 0.3f);
            }
            float start0 = start;
            start = from;
            var go = new GameObject("Carpet");
            go.transform.SetParent(room.transform, false);
            var dir = new Vector3(d.x, 0f, d.y);
            go.transform.localPosition = dir * start + Vector3.up * (0.012f + i * 0.002f);   // a hair apart where they cross
            go.transform.localRotation = Quaternion.LookRotation(dir);
            // on out over the exit path: to the gate's back where a portal fills it (the wall closes behind), else far out
            float beyond = ExitPortal.Available && gatePrefab != null ? 3.3f : 10f;
            go.AddComponent<MeshFilter>().sharedMesh = CarpetMesh(width, edge + beyond - start);
            go.AddComponent<MeshRenderer>().sharedMaterial = carpet;
            start = start0;
        }
    }

    // A strip from the origin along +z; the texture's gold borders sit on its long edges and it repeats down its length
    private static Mesh CarpetMesh(float width, float length)
    {
        const float u0 = 0.17f, u1 = 0.83f;
        float repeat = width / (u1 - u0);   // metres of carpet per image
        float v = length / repeat;
        var mesh = new Mesh
        {
            vertices = new[] { new Vector3(-width / 2f, 0f, 0f), new Vector3(width / 2f, 0f, 0f), new Vector3(-width / 2f, 0f, length), new Vector3(width / 2f, 0f, length) },
            uv = new[] { new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u0, v), new Vector2(u1, v) },
            triangles = new[] { 0, 2, 1, 1, 2, 3 },
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Inside the hall: a colonnade to fight around, golden valkyries in the corners, banners along the walls
    private void PalaceHall(Room room, List<(Vector3 p, float r)> placed, float amount, bool centrepiece = true)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        // Fight halls keep their floor clear so nothing gets in the way of the fight: no columns or crystals on it,
        // and what stands by the walls has no collision. Only a fountain may stand in the way. Small rooms get a few columns.
        bool grand = amount >= 1f;
        bool solid = !grand;
        // A themed fight hall dresses itself entirely (see DungeonGenerator.HallThemes)
        if (grand && centrepiece && hallTheme != HallTheme.None)
        {
            DressTheme(room, placed);
            return;
        }
        // Fight halls are built around one of several plans; the themed ones dress their own walls too
        // (a cloister's middle is its open court: it keeps the plain palace dressing round it)
        bool laidOut = grand && centrepiece && !centreTaken && (fountainPrefab != null || obeliskPrefab != null) && room.OnFloorLocal(Vector2.zero, 3f);
        if (laidOut && HallLayout(room, placed) >= HallPlan.FeastHall)
            return;

        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                // Columns stand off the carpets (which run along the hall's middle lines)
                if (!grand)
                {
                    var c = new Vector3(sx * hx * 0.55f, 0f, sz * hz * 0.5f);
                    if (placed.All(o => Vector3.Distance(o.p, c) > o.r + 1f))
                    {
                        PalaceColumn(t, c, 0.85f, true);
                        placed.Add((c, 1.2f));
                    }
                }

                // A valkyrie statue in each corner, looking over the hall
                var corner = new Vector3(sx * (hx - 2.6f), 0f, sz * (hz - 2.6f));
                if (statuePrefab != null && (grand || sx == sz) && room.OnFloorLocal(new Vector2(corner.x, corner.z), 0.8f))
                {
                    Place(statuePrefab, t, corner, Quaternion.LookRotation(-corner).eulerAngles.y, 1f, solid);
                    placed.Add((corner, 1.8f));
                }
            }

        // Banners stand against the side walls, facing in
        if (bannerPrefab != null)
            foreach (float s in new[] { -1f, 1f })
                foreach (float k in new[] { -0.55f, 0.55f })
                {
                    var p = new Vector3(s * (hx - 1f), 0f, k * hz);
                    if (placed.All(o => Vector3.Distance(o.p, p) > o.r + 1f))
                        Place(bannerPrefab, t, p, s > 0f ? -90f : 90f, 1f, solid);
                }

        // Golden candelabras against every wall, each casting warm light over the marble
        var candles = new[]
        {
            new Vector3(-(hx - 1.2f), 0f, 0f), new Vector3(hx - 1.2f, 0f, 0f),
            new Vector3(-hx * 0.3f, 0f, hz - 1.2f), new Vector3(hx * 0.3f, 0f, hz - 1.2f),
            new Vector3(-hx * 0.3f, 0f, -(hz - 1.2f)), new Vector3(hx * 0.3f, 0f, -(hz - 1.2f)),
        };
        foreach (Vector3 p in candles)
        {
            if (candelabraPrefab == null || placed.Any(o => Vector3.Distance(o.p, p) < o.r + 0.8f))
                continue;
            Place(candelabraPrefab, t, p, Random.Range(0f, 360f), 1f, solid);
            placed.Add((p, 0.8f));
            CandleLight(t, p + Vector3.up * 2.7f);
        }

        // A rune medallion set into the floor where the carpets cross (the laid-out halls place their own)
        if (grand && !laidOut && !centreTaken && arenaFloorTexture != null && room.OnFloorLocal(Vector2.zero, 3f))
        {
            Material medallion = Mats.Get("palace_medallion", Color.white, 0.5f);
            medallion.SetTexture("_BaseMap", arenaFloorTexture);
            FlatMesh(t, "Medallion", AnnulusMesh(0f, 3.4f, 64), 0.009f, medallion);
            Ambient.RuneHalo(t, Vector3.zero, 3.2f, new Color(1f, 0.8f, 0.4f) * 0.7f);
        }
    }

    // The mid-boss's hall (Týr's oath hall): the palace hall's statues, banners and candelabras by the walls with
    // nothing on the floor but the rune medallion where the carpets cross, ringed in a wide band of gold, and braziers
    // burning either side of the far wall
    private void OathHall(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hz = room.Size.y / 2f;
        if (palace)
            PalaceHall(room, placed, 1f, false);
        FlatMesh(t, "OathRing", AnnulusMesh(5.4f, 5.8f, 96), 0.011f, GoldInlay);
        Ambient.RuneHalo(t, Vector3.zero, 5.2f, new Color(1f, 0.5f, 0.35f) * 0.7f);
        foreach (float side in new[] { -1f, 1f })
            Brazier(t, new Vector3(side * 5f, 0f, hz - 1.4f), false);
        SoftLight(t, Vector3.up * 5f, new Color(1f, 0.7f, 0.5f), 14f, 1.6f);
    }

    private static void CandleLight(Transform parent, Vector3 local)
    {
        var glow = new GameObject("Candlelight");
        glow.transform.SetParent(parent, false);
        glow.transform.localPosition = local;
        var light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.45f);
        light.range = 9f;
        light.intensity = 1.8f;
        light.shadows = LightShadows.None;
        glow.AddComponent<FlickerLight>();
    }

    // Odin's high seat on the first wall (north, then the sides) that has no doorway, facing into the hall,
    // with a valkyrie standing guard either side. Returns false when every wall has a door.
    private bool Throne(Room room, List<(Vector3 p, float r)> placed)
    {
        if (thronePrefab == null)
            return false;
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        foreach (Vector2Int d in new[] { Vector2Int.up, Vector2Int.left, Vector2Int.right })
        {
            Vector3 normal = new Vector3(d.x, 0f, d.y);
            Vector3 spot = normal * ((d.y != 0 ? hz : hx) - 3.2f);
            if (placed.Any(o => Vector3.Distance(o.p, spot) < o.r + 2.5f))
                continue;
            float yaw = Quaternion.LookRotation(-normal).eulerAngles.y;
            Place(thronePrefab, t, spot, yaw);
            placed.Add((spot, 3f));
            Vector3 side = Vector3.Cross(Vector3.up, normal);
            if (statuePrefab != null)
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 guard = spot + side * s * 3.6f + normal * 0.6f;
                    Place(statuePrefab, t, guard, yaw);
                    placed.Add((guard, 1.6f));
                }
            CandleLight(t, spot - normal * 1.5f + Vector3.up * 3f);
            return true;
        }
        return false;
    }

    // ───────────── Asgard fight-hall shapes ─────────────

    // The floor plan of a fight hall, on top of whichever dressing plan it gets:
    //   Classic    the plain rectangle
    //   Gallery    긴 회랑: a long narrow hall, east-west or north-south, columns down both sides
    //   Cross      십자 전당: two crossing arms with the cosmos open in the four corners, golden trees out in them
    //   Cloister   별빛 회랑: a ring of floor round an open court where a Bifrost crystal floats in the stars
    //   Bridge     무지개 다리: two platforms joined by a narrow rainbow bridge over the void (exits on one axis only)
    //   Colonnade  기둥의 숲: a wide hall with a grid of great columns to fight around and break line of sight
    // Asgard only (see DungeonGenerator.AsgardTerrain):
    //   Plaza      신전 광장: a wide square, corners broken off, the Allfather's statue on a dais at its heart
    //   Ruins      무너진 유적: a hall whose edges crumbled away, pits in its floor, a god's sword driven into the rubble
    //   Platforms  공중 제단군: a middle platform with smaller ones off it, joined by bridges over the drop
    //   Altar      원형 제단: a court round a stepped altar inside a circle of columns
    //   Asymmetric 비대칭 신전: two offset wings, a shrine floating in the far open corner
    //   Concourse  회랑과 광장: narrow corridors from each door into a wide hall, a statue in an alcove
    private enum HallShape { Classic, Gallery, Cross, Cloister, Bridge, Colonnade, Plaza, Ruins, Platforms, Altar, Asymmetric, Concourse }

    // Dealt from a shuffled bag like the dressing plans, so a floor's fight halls show every shape before any repeats
    private readonly List<HallShape> shapeBag = new List<HallShape>();

    private HallShape PickShape(List<Vector2Int> exits)
    {
        if (shapeBag.Count == 0)
        {
            shapeBag.AddRange((HallShape[])System.Enum.GetValues(typeof(HallShape)));
            if (!palace)
            {
                shapeBag.Remove(HallShape.Classic);   // Niflheim's fight zones are never plain rectangles
                shapeBag.RemoveAll(IsAsgardShape);    // (and keep their own five shapes)
            }
            for (int i = shapeBag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (shapeBag[i], shapeBag[j]) = (shapeBag[j], shapeBag[i]);
            }
        }
        for (int i = shapeBag.Count - 1; i >= 0; i--)
        {
            HallShape s = shapeBag[i];
            // A bridge only runs between doors on its own axis
            if (s == HallShape.Bridge && !exits.All(d => d.x != 0) && !exits.All(d => d.y != 0))
                continue;
            shapeBag.RemoveAt(i);
            return s;
        }
        return HallShape.Classic;
    }

    private static Rect R(float x0, float z0, float x1, float z1) => Rect.MinMaxRect(x0, z0, x1, z1);

    // The hall's size and its floor and open cosmos as rectangles (local x/z). Every exit sits in the middle of a side,
    // and every shape keeps floor there.
    private static void ShapeLayout(HallShape shape, List<Vector2Int> exits, out Vector2 size, out List<Rect> floor, out List<Rect> voids)
    {
        floor = new List<Rect>();
        voids = new List<Rect>();
        bool ew = exits.Any(d => d.x != 0), ns = exits.Any(d => d.y != 0);
        switch (shape)
        {
            case HallShape.Gallery:
            {
                // Long the way its doors lead (either way when they lead both ways)
                bool eastWest = ew && !ns || !(ns && !ew) && Random.value < 0.5f;
                size = eastWest ? new Vector2(Random.Range(46f, 52f), 13f) : new Vector2(14f, Random.Range(40f, 44f));
                floor.Add(R(-size.x / 2f, -size.y / 2f, size.x / 2f, size.y / 2f));
                break;
            }
            case HallShape.Cross:
            {
                size = new Vector2(38f, 34f);
                const float a = 6.5f;
                floor.Add(R(-19f, -a, 19f, a));
                floor.Add(R(-a, -17f, a, 17f));
                voids.Add(R(-19f, a, -a, 17f));
                voids.Add(R(a, a, 19f, 17f));
                voids.Add(R(-19f, -17f, -a, -a));
                voids.Add(R(a, -17f, 19f, -a));
                break;
            }
            case HallShape.Cloister:
            {
                size = new Vector2(34f, 30f);
                floor.Add(R(-17f, 5f, 17f, 15f));
                floor.Add(R(-17f, -15f, 17f, -5f));
                floor.Add(R(-17f, -15f, -7f, 15f));
                floor.Add(R(7f, -15f, 17f, 15f));
                voids.Add(R(-7f, -5f, 7f, 5f));
                break;
            }
            case HallShape.Bridge:
            {
                if (exits.All(d => d.x != 0))
                {
                    size = new Vector2(46f, 22f);
                    floor.Add(R(-23f, -11f, -8f, 11f));
                    floor.Add(R(8f, -11f, 23f, 11f));
                    floor.Add(R(-8f, -3.5f, 8f, 3.5f));
                    voids.Add(R(-8f, 3.5f, 8f, 11f));
                    voids.Add(R(-8f, -11f, 8f, -3.5f));
                }
                else
                {
                    size = new Vector2(22f, 46f);
                    floor.Add(R(-11f, -23f, 11f, -8f));
                    floor.Add(R(-11f, 8f, 11f, 23f));
                    floor.Add(R(-3.5f, -8f, 3.5f, 8f));
                    voids.Add(R(-11f, -8f, -3.5f, 8f));
                    voids.Add(R(3.5f, -8f, 11f, 8f));
                }
                break;
            }
            case HallShape.Colonnade:
                size = new Vector2(36f, 28f);
                floor.Add(R(-18f, -14f, 18f, 14f));
                break;
            default:   // Asgard's own plans
                AsgardLayout(shape, exits, out size, floor, voids);
                break;
        }
    }

    // The open cosmos between a shaped hall's floors: the starry deep, glittering, running out to the walls
    // where it meets the zone's edge
    private void OpenCosmos(Room room, Realm realm)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Material gold = Mats.Get("glint_gold", new Color(1f, 0.9f, 0.6f), 0.5f, new Color(2f, 1.7f, 0.9f));
        Material cyan = Mats.Get("glint_cyan", new Color(0.6f, 0.9f, 1f), 0.5f, new Color(0.9f, 1.6f, 2.2f));
        foreach (Rect v in room.Voids)
        {
            float x0 = v.xMin <= -hx + 0.01f ? v.xMin - WallSetback : v.xMin;
            float x1 = v.xMax >= hx - 0.01f ? v.xMax + WallSetback : v.xMax;
            float z0 = v.yMin <= -hz + 0.01f ? v.yMin - WallSetback : v.yMin;
            float z1 = v.yMax >= hz - 0.01f ? v.yMax + WallSetback : v.yMax;
            var size = new Vector2(x1 - x0, z1 - z0);
            Material deep = Mats.Get($"void_cosmos_{size.x:0.0}x{size.y:0.0}", new Color(0.4f, 0.44f, 0.72f), 0.5f, new Color(0.035f, 0.045f, 0.13f));
            deep.SetTexture("_BaseMap", ground);
            deep.SetTextureScale("_BaseMap", size / groundTileSize);
            Mats.Shape(PrimitiveType.Cube, t, new Vector3((x0 + x1) / 2f, -0.093f, (z0 + z1) / 2f), new Vector3(size.x, 0.2f, size.y), deep, false, "OpenCosmos");
            var golds = new List<Vector3>();
            var cyans = new List<Vector3>();
            for (int i = 0; i < size.x * size.y / 2.5f; i++)
            {
                var p = new Vector3(Random.Range(x0, x1), Random.Range(0.05f, 0.14f), Random.Range(z0, z1));
                (Random.value < 0.55f ? golds : cyans).Add(p);
            }
            GlintMesh(t, "VoidGlints_Gold", golds, gold);
            GlintMesh(t, "VoidGlints_Cyan", cyans, cyan);
        }
    }

    // A shaped hall's rim: wherever floor meets the cosmos or the zone's edge, an invisible wall and the palace
    // balustrade, open at the doors. Worked out from the floor itself, a quarter-metre at a time.
    private void BuildShapedEdge(Room room, List<Vector2Int> exits, Transform player)
    {
        edgePosts.Clear();
        ruinedRim = palace && room.Shape == nameof(HallShape.Ruins);
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        // Rectangles may overlap along a side (a cloister's bands): each stretch of rim is built once
        var built = new Dictionary<(Vector2Int, int), List<Vector2>>();
        bool Built(Vector2Int d, float line, float s) =>
            built.TryGetValue((d, Mathf.RoundToInt(line * 100f)), out var spans) && spans.Any(sp => s > sp.x && s < sp.y);
        lowRim = false;
        foreach (Rect r in room.Floor)
            foreach (Vector2Int d in Directions)
            {
                bool alongX = d.y != 0;
                float line = d.x > 0 ? r.xMax : d.x < 0 ? r.xMin : d.y > 0 ? r.yMax : r.yMin;
                float a0 = alongX ? r.xMin : r.yMin, a1 = alongX ? r.xMax : r.yMax;
                int n = Mathf.Max(1, Mathf.RoundToInt((a1 - a0) / 0.25f));
                float step = (a1 - a0) / n;
                Vector2 Probe(float s, float outward) => alongX ? new Vector2(s, line + d.y * outward) : new Vector2(line + d.x * outward, s);
                int runStart = -1;
                for (int i = 0; i <= n; i++)
                {
                    float mid = a0 + (i + 0.5f) * step;
                    bool open = i < n && !room.OnFloorLocal(Probe(mid, 0.3f)) && !Built(d, line, mid);
                    if (open && runStart < 0)
                        runStart = i;
                    else if (!open && runStart >= 0)
                    {
                        float s0 = a0 + runStart * step, s1 = a0 + i * step;
                        // Out past a convex corner the wall runs on a little to close it; at an inner corner it stops
                        bool cap0 = !room.OnFloorLocal(Probe(s0 - 0.3f, 0.3f)), cap1 = !room.OnFloorLocal(Probe(s1 + 0.3f, 0.3f));
                        RimSegment(room, exits, player, d, line, s0, s1, cap0, cap1, hx, hz);
                        var key = (d, Mathf.RoundToInt(line * 100f));
                        if (!built.ContainsKey(key))
                            built[key] = new List<Vector2>();
                        built[key].Add(new Vector2(s0, s1));
                        runStart = -1;
                    }
                }
            }
    }

    private void RimSegment(Room room, List<Vector2Int> exits, Transform player, Vector2Int d, float line, float s0, float s1, bool cap0, bool cap1, float hx, float hz)
    {
        Transform t = room.transform;
        bool alongX = d.y != 0;
        Vector3 normal = new Vector3(d.x, 0f, d.y), along = alongX ? Vector3.right : Vector3.forward;
        Vector3 basis = alongX ? new Vector3(0f, 0f, line) : new Vector3(line, 0f, 0f);
        // The zone's own edge (where the doors are) or the rim of an open court
        bool zoneEdge = Mathf.Abs(Mathf.Abs(line) - (alongX ? hz : hx)) < 0.01f;
        bool door = zoneEdge && exits.Contains(d) && s0 < 0f && s1 > 0f;
        string wallName = zoneEdge ? "EdgeWall" : "VoidEdge";

        void WallPiece(float from, float to, bool capFrom, bool capTo)
        {
            if (capFrom) from -= 1f;
            if (capTo) to += 1f;
            if (to - from < 0.05f)
                return;
            var wall = new GameObject(wallName);
            wall.transform.SetParent(t, false);
            wall.transform.localPosition = basis + along * ((from + to) / 2f) + normal * 0.5f + Vector3.up * 2f;
            wall.AddComponent<BoxCollider>().size = along * (to - from) + new Vector3(along.z, 0f, along.x) + Vector3.up * 4f;
        }

        // Asgard rims its halls with the palace balustrade, Niflheim with a ridge of rime and ice
        // (a rim facing the camera gets low posts instead of tall columns, so it never hides the fight)
        void Rim(Vector3 c, float len, bool exit)
        {
            if (palace)
            {
                lowRim = d == Vector2Int.down;
                PalaceEdge(t, c, along, len, exit);
                lowRim = false;
            }
            else
                IceRim(t, c, along, normal, len, exit, !zoneEdge);
        }

        if (!door)
        {
            WallPiece(s0, s1, cap0, cap1);
            Rim(basis + along * ((s0 + s1) / 2f), s1 - s0, false);
            return;
        }
        float gap = ExitWidth / 2f;
        WallPiece(s0, -gap, cap0, false);
        WallPiece(gap, s1, false, cap1);
        if (Mathf.Abs((s0 + s1) / 2f) < 0.3f)
            Rim(basis + along * ((s0 + s1) / 2f), s1 - s0, true);
        else
        {
            // (a doorway off the middle of its stretch: the two sides of the balustrade built apart)
            float post = gap + 0.9f;
            Rim(basis + along * ((s0 - post) / 2f), -post - s0, false);
            Rim(basis + along * ((post + s1) / 2f), s1 - post, false);
        }
        BuildExit(room, d, alongX ? hz : hx, player, true);
    }

    // What makes each shape itself, set down before the dressing plan so the plan works round it
    private void ShapeLandmarks(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        if (!palace)
        {
            FrostLandmarks(room, placed);
            return;
        }
        switch (room.Shape)
        {
            case nameof(HallShape.Gallery):
            {
                // A colonnade down both long sides, the middle left open
                bool eastWest = room.Size.x > room.Size.y;
                float length = eastWest ? hx : hz, width = eastWest ? hz : hx;
                for (float s = -length + 4.5f; s <= length - 4.5f; s += 7.5f)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 p = eastWest ? new Vector3(s, 0f, side * (width - 2.2f)) : new Vector3(side * (width - 2.2f), 0f, s);
                        if (placed.Any(o => Vector3.Distance(o.p, p) < o.r + 1f))
                            continue;
                        PalaceColumn(t, p, 0.8f, true);
                        placed.Add((p, 1.3f));
                    }
                SoftLight(t, Vector3.up * 5f + (eastWest ? Vector3.right : Vector3.forward) * length * 0.5f, new Color(1f, 0.85f, 0.6f), 16f, 1.2f);
                SoftLight(t, Vector3.up * 5f - (eastWest ? Vector3.right : Vector3.forward) * length * 0.5f, new Color(1f, 0.85f, 0.6f), 16f, 1.2f);
                break;
            }
            case nameof(HallShape.Colonnade):
            {
                // Great columns in two rows, clear of the lanes to the doors, to fight round and hide behind
                foreach (float x in new[] { -12f, -6f, 6f, 12f })
                    foreach (float z in new[] { -7f, 7f })
                    {
                        var p = new Vector3(x, 0f, z);
                        PalaceColumn(t, p, 1.15f, true);
                        placed.Add((p, 1.8f));
                        FlatMesh(t, "ColumnRing", AnnulusMesh(1.3f, 1.5f, 48), 0.011f, GoldInlay).transform.localPosition = p + Vector3.up * 0.011f;
                    }
                break;
            }
            case nameof(HallShape.Cloister):
            {
                // A Bifrost crystal floats in the open court, wreathed in turning runes
                if (obeliskPrefab != null)
                {
                    GameObject crystal = Place(obeliskPrefab, t, Vector3.up * 0.4f, Random.Range(0f, 360f), 1.3f, false);
                    crystal.AddComponent<FloatBob>();
                }
                Ambient.RuneHalo(t, Vector3.zero, 4.2f, new Color(0.55f, 0.75f, 1f) * 0.8f);
                SoftLight(t, Vector3.up * 3.5f, new Color(0.65f, 0.8f, 1f), 14f, 2.2f);
                break;
            }
            case nameof(HallShape.Cross):
            {
                // A golden tree out in each open corner, lit from below
                foreach (Rect v in room.Voids)
                {
                    var c = new Vector3(v.center.x, 0f, v.center.y);
                    GameObject prefab = goldenTreePrefab != null ? goldenTreePrefab : runeStonePrefab;
                    if (prefab == null)
                        continue;
                    // (each on a little island over the drop; the near corners' trees are small and sunk low, so they
                    // don't stand between the camera and the fight)
                    Transform island = VoidIsland(t, c, new Vector2(4.6f, 4.2f), c.z < 0f ? -1.6f : 0.4f);
                    Place(prefab, island, Vector3.zero, Random.Range(0f, 360f), c.z < 0f ? 0.6f : 1.1f, false);
                    SoftLight(t, c + Vector3.up * 1.5f, new Color(1f, 0.82f, 0.45f), 8f, 1.4f);
                }
                break;
            }
            case nameof(HallShape.Bridge):
            {
                // The bridge is the Bifrost: bands of rainbow light run its length and glow up off it
                Rect b = room.Floor.OrderBy(r => r.width * r.height).First();
                bool eastWest = b.width > b.height;
                float length = (eastWest ? b.width : b.height) + 3f, width = eastWest ? b.height : b.width;
                Color[] rainbow = { new Color(1f, 0.3f, 0.3f), new Color(1f, 0.65f, 0.25f), new Color(1f, 0.95f, 0.35f), new Color(0.35f, 1f, 0.5f), new Color(0.35f, 0.65f, 1f), new Color(0.75f, 0.4f, 1f) };
                float band = (width - 1.2f) / rainbow.Length;
                for (int i = 0; i < rainbow.Length; i++)
                {
                    float off = -width / 2f + 0.6f + band * (i + 0.5f);
                    Vector3 at = (eastWest ? new Vector3(0f, 0f, off) : new Vector3(off, 0f, 0f)) + Vector3.up * 0.013f;
                    Vector3 sz = eastWest ? new Vector3(length, 0.01f, band * 0.9f) : new Vector3(band * 0.9f, 0.01f, length);
                    Mats.Shape(PrimitiveType.Cube, t, at, sz, Mats.Get("bifrost_band_" + i, rainbow[i], 0.6f, rainbow[i] * 0.9f), false, "BifrostBand");
                }
                SoftLight(t, Vector3.up * 2.5f, new Color(0.9f, 0.85f, 1f), 12f, 1.8f);
                break;
            }
            default:
                AsgardLandmarks(room, placed);
                break;
        }
    }

    // ───────────── Niflheim shaped zones ─────────────

    private static Material NiflDeepIce => Mats.Get("nifl_crystal_deep", new Color(0.28f, 0.5f, 0.88f), 0.95f, new Color(0.07f, 0.22f, 0.58f));
    private static Material NiflRime => Mats.Get("nifl_rime", new Color(0.72f, 0.84f, 0.95f), 0.8f, new Color(0.05f, 0.1f, 0.16f));
    private static Material NiflStone => Mats.Get("nifl_blackstone", new Color(0.16f, 0.18f, 0.23f), 0.3f);

    // Where a Niflheim zone has no floor, the ice has split open: a black-blue crevasse glowing faintly from far below,
    // its lips crusted with rime and shards
    private void FrozenAbyss(Room room)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Material deep = Mats.Get("nifl_abyss", new Color(0.02f, 0.04f, 0.08f), 0.9f, new Color(0.01f, 0.05f, 0.12f));
        Material glow = Mats.Get("nifl_abyss_glow", new Color(0.4f, 0.75f, 1f), 0.5f, new Color(0.5f, 1.3f, 2.2f));
        foreach (Rect v in room.Voids)
        {
            float x0 = v.xMin <= -hx + 0.01f ? v.xMin - WallSetback : v.xMin;
            float x1 = v.xMax >= hx - 0.01f ? v.xMax + WallSetback : v.xMax;
            float z0 = v.yMin <= -hz + 0.01f ? v.yMin - WallSetback : v.yMin;
            float z1 = v.yMax >= hz - 0.01f ? v.yMax + WallSetback : v.yMax;
            var size = new Vector2(x1 - x0, z1 - z0);
            Mats.Shape(PrimitiveType.Cube, t, new Vector3((x0 + x1) / 2f, -0.09f, (z0 + z1) / 2f), new Vector3(size.x, 0.2f, size.y), deep, false, "FrozenAbyss");
            // a faint glow far down, and drifting motes of light in the dark
            var glints = new List<Vector3>();
            for (int i = 0; i < size.x * size.y / 3f; i++)
                glints.Add(new Vector3(Random.Range(x0, x1), Random.Range(0.02f, 0.1f), Random.Range(z0, z1)));
            GlintMesh(t, "AbyssGlints", glints, glow);
            // shards of ice jutting from the dark
            int shards = Mathf.RoundToInt(size.x * size.y / 28f);
            for (int i = 0; i < shards; i++)
            {
                var p = new Vector3(Random.Range(x0 + 1f, x1 - 1f), -0.2f, Random.Range(z0 + 1f, z1 - 1f));
                // (low near the camera's side, so they never hide the fight)
                float h = p.z < 0f ? Random.Range(0.5f, 1.2f) : Random.Range(1f, 2.4f);
                IceCluster(t, p, Random.Range(0f, 360f), Random.value < 0.4f ? NiflDeepIce : NiflIce, h, false);
            }
            SoftLight(t, new Vector3((x0 + x1) / 2f, 0.6f, (z0 + z1) / 2f), new Color(0.35f, 0.7f, 1f), Mathf.Max(size.x, size.y) * 0.8f, 1.1f);
        }
    }

    // A Niflheim zone's rim: a low ridge of rime-crusted stone and ice crystals (ankle-to-knee high, so it marks the
    // edge without hiding anything); over a crevasse the crystals stand thicker and taller
    private void IceRim(Transform t, Vector3 edgeCenter, Vector3 along, Vector3 outward, float length, bool exit, bool overAbyss)
    {
        for (float s = -length / 2f; s <= length / 2f; s += Random.Range(0.7f, 1.1f))
        {
            if (exit && Mathf.Abs(s) < ExitWidth / 2f + 0.6f)
                continue;
            Vector3 p = edgeCenter + along * s + outward * Random.Range(0.15f, 0.45f);
            bool near = p.z < -1f && outward.z < -0.5f;   // the side toward the camera stays low
            if (Random.value < (overAbyss ? 0.55f : 0.35f))
            {
                float h = near ? Random.Range(0.35f, 0.6f) : overAbyss ? Random.Range(0.7f, 1.5f) : Random.Range(0.45f, 0.9f);
                IceCluster(t, p, Random.Range(0f, 360f), Random.value < 0.3f ? NiflDeepIce : NiflIce, h, false);
            }
            else
            {
                float h = Random.Range(0.18f, 0.38f);
                GameObject rock = Mats.Shape(PrimitiveType.Cube, t, p + Vector3.up * h * 0.4f,
                    new Vector3(Random.Range(0.45f, 0.85f), h, Random.Range(0.4f, 0.7f)), Random.value < 0.5f ? NiflStone : NiflRime);
                rock.transform.localRotation = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0f, 90f), Random.Range(-10f, 10f));
            }
        }
    }

    // What makes each Niflheim zone itself
    //   Gallery    얼음 협곡: a long frozen gorge lined with ice spires
    //   Cross      갈라진 빙원: four crevasses cut the ice into a cross, a frost cairn in each
    //   Cloister   빙결 심연: a ring of ice round a deep hole, a great crystal rising from it
    //   Bridge     얼음 다리: two ice floes joined by a frozen span over the abyss
    //   Colonnade  얼음 기둥 숲: a wide field of great ice pillars to fight round
    private void FrostLandmarks(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Color cold = new Color(0.45f, 0.75f, 1f);
        switch (room.Shape)
        {
            case nameof(HallShape.Gallery):
            {
                bool eastWest = room.Size.x > room.Size.y;
                float length = eastWest ? hx : hz, width = eastWest ? hz : hx;
                for (float s = -length + 4f; s <= length - 4f; s += 6f)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 p = eastWest ? new Vector3(s + Random.Range(-1f, 1f), 0f, side * (width - 1.6f)) : new Vector3(side * (width - 1.6f), 0f, s + Random.Range(-1f, 1f));
                        if (placed.Any(o => Vector3.Distance(o.p, p) < o.r + 1f))
                            continue;
                        bool near = p.z < -width + 3f && eastWest;
                        IceCluster(t, p, Random.Range(0f, 360f), Random.value < 0.35f ? NiflDeepIce : NiflIce, near ? Random.Range(1f, 1.6f) : Random.Range(2.6f, 3.8f), true);
                        placed.Add((p, 1.4f));
                    }
                SoftLight(t, Vector3.up * 4f + (eastWest ? Vector3.right : Vector3.forward) * length * 0.5f, cold, 16f, 1.2f);
                SoftLight(t, Vector3.up * 4f - (eastWest ? Vector3.right : Vector3.forward) * length * 0.5f, cold, 16f, 1.2f);
                break;
            }
            case nameof(HallShape.Colonnade):
            {
                // great pillars of ice in two rows, each on a frozen pool, clear of the lanes to the doors
                foreach (float x in new[] { -12f, -6f, 6f, 12f })
                    foreach (float z in new[] { -7f, 7f })
                    {
                        var p = new Vector3(x + Random.Range(-0.6f, 0.6f), 0f, z + Random.Range(-0.6f, 0.6f));
                        FlatMesh(t, "FrozenPool", AnnulusMesh(0f, 1.8f, 32), 0.012f, NiflRime).transform.localPosition = p + Vector3.up * 0.012f;
                        IceCluster(t, p, Random.Range(0f, 360f), (x + z) % 2 == 0 ? NiflDeepIce : NiflIce, z < 0f ? 2.2f : 4.2f, true);
                        placed.Add((p, 2f));
                    }
                SoftLight(t, Vector3.up * 4f, cold, 20f, 1.4f);
                break;
            }
            case nameof(HallShape.Cloister):
            {
                // a great crystal rises out of the hole in the middle, ringed by smaller ones
                Crystal6(t, new Vector3(0f, -0.5f, 1f), Quaternion.Euler(4f, 0f, -3f), 1.1f, 6.5f, NiflDeepIce);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI * 2f / 6f;
                    IceCluster(t, new Vector3(Mathf.Cos(a) * 4.2f, -0.2f, Mathf.Sin(a) * 2.8f + 0.5f), Random.Range(0f, 360f), NiflIce, Mathf.Sin(a) < 0f ? 1f : 2.2f, false);
                }
                SoftLight(t, Vector3.up * 3.5f, new Color(0.5f, 0.8f, 1f), 16f, 2.4f);
                break;
            }
            case nameof(HallShape.Cross):
            {
                // a cairn of ice and black stone out in each crevasse
                foreach (Rect v in room.Voids)
                {
                    var c = new Vector3(v.center.x, -0.2f, v.center.y);
                    bool near = c.z < 0f;
                    IceCluster(t, c, Random.Range(0f, 360f), NiflDeepIce, near ? 1.6f : 3.6f, false);
                    for (int i = 0; i < 3; i++)
                        IceCluster(t, c + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f)), Random.Range(0f, 360f), NiflIce, near ? 0.8f : Random.Range(1.4f, 2.2f), false);
                }
                SoftLight(t, Vector3.up * 3f, cold, 18f, 1.3f);
                break;
            }
            case nameof(HallShape.Bridge):
            {
                // the span is frozen solid: a pale band of rime down its middle and ice posts along its edges
                Rect b = room.Floor.OrderBy(r => r.width * r.height).First();
                bool eastWest = b.width > b.height;
                float length = eastWest ? b.width : b.height, width = eastWest ? b.height : b.width;
                Vector3 sz = eastWest ? new Vector3(length + 2f, 0.012f, width - 2f) : new Vector3(width - 2f, 0.012f, length + 2f);
                Mats.Shape(PrimitiveType.Cube, t, Vector3.up * 0.01f, sz, Mats.Get("nifl_span", new Color(0.32f, 0.42f, 0.62f), 0.9f, new Color(0.03f, 0.08f, 0.16f)), false, "FrozenSpan");
                for (float s = -length / 2f + 1f; s <= length / 2f - 1f; s += 3.2f)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 p = eastWest ? new Vector3(s, 0f, side * (width / 2f - 0.3f)) : new Vector3(side * (width / 2f - 0.3f), 0f, s);
                        IceCluster(t, p, Random.Range(0f, 360f), NiflIce, p.z < -1f ? 0.6f : 1.3f, false);
                    }
                SoftLight(t, Vector3.up * 2.5f, cold, 12f, 1.8f);
                break;
            }
        }
    }

    // ───────────── Asgard fight-hall layouts ─────────────

    // The first three keep the palace's statues, banners and candelabras along the walls; from FeastHall on,
    // each hall dresses its walls in its own way
    private enum HallPlan { FountainCourt, Nave, Sanctum, FeastHall, TrainingYard, Observatory, Garden }

    // Plans are dealt from a shuffled bag, so a floor's fight halls run through them all before any repeats
    private readonly List<HallPlan> planBag = new List<HallPlan>();
    private HallPlan lastPlan = (HallPlan)(-1);

    private HallPlan HallLayout(Room room, List<(Vector3 p, float r)> placed)
    {
        if (planBag.Count == 0)
        {
            LoadHallProps();
            if (fountainPrefab != null) planBag.Add(HallPlan.FountainCourt);
            planBag.Add(HallPlan.Nave);
            if (obeliskPrefab != null) planBag.Add(HallPlan.Sanctum);
            if (feastTable != null) planBag.Add(HallPlan.FeastHall);
            if (weaponRackPrefab != null || shieldRack != null) planBag.Add(HallPlan.TrainingYard);
            planBag.Add(HallPlan.Observatory);
            if (goldenTreePrefab != null) planBag.Add(HallPlan.Garden);
            for (int i = planBag.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (planBag[i], planBag[j]) = (planBag[j], planBag[i]);
            }
            // (and the new bag doesn't open with the plan the last one ended on)
            if (planBag.Count > 1 && planBag[planBag.Count - 1] == lastPlan)
                (planBag[0], planBag[planBag.Count - 1]) = (planBag[planBag.Count - 1], planBag[0]);
        }
        HallPlan plan = planBag[planBag.Count - 1];
        planBag.RemoveAt(planBag.Count - 1);
        lastPlan = plan;
        // A shaped hall's floor may not suit every plan: the fountain would block a narrow middle, and the rays and
        // constellations run out on the diagonals, which a cross leaves open to the cosmos
        if (plan == HallPlan.FountainCourt && !room.OnFloorLocal(Vector2.zero, 6.6f))
            plan = HallPlan.Nave;
        if ((plan == HallPlan.Sanctum || plan == HallPlan.Observatory) && !room.OnFloorLocal(new Vector2(6f, 6f), 0.5f))
            plan = HallPlan.Nave;
        switch (plan)
        {
            case HallPlan.FountainCourt: FountainCourt(room, placed); break;
            case HallPlan.Nave: Nave(room, placed); break;
            case HallPlan.Sanctum: Sanctum(room, placed); break;
            case HallPlan.FeastHall: FeastHall(room, placed); break;
            case HallPlan.TrainingYard: TrainingYard(room, placed); break;
            case HallPlan.Observatory: Observatory(room, placed); break;
            default: Garden(room, placed); break;
        }
        return plan;
    }

    private static Material GoldInlay => Mats.Get("palace_inlay_gold", new Color(1f, 0.8f, 0.4f), 0.7f, new Color(0.7f, 0.5f, 0.15f));

    // Puts a prop down where nothing else stands yet; false when the spot is taken
    private bool PlaceFree(GameObject prefab, Room room, List<(Vector3 p, float r)> placed, Vector3 p, float radius, float yaw, float scale = 1f)
    {
        if (prefab == null || placed.Any(o => Vector3.Distance(o.p, p) < o.r + radius))
            return false;
        Place(prefab, room.transform, p, yaw, scale);
        placed.Add((p, radius));
        return true;
    }

    private static void SoftLight(Transform parent, Vector3 local, Color color, float range, float intensity)
    {
        var go = new GameObject("HallLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    // A rainbow-water fountain in the middle ringed in gold (the one thing in a fight hall that stands in the way)
    private void FountainCourt(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        Place(fountainPrefab, t, Vector3.zero, Random.Range(0, 4) * 90f);
        placed.Add((Vector3.zero, 3.8f));
        FlatMesh(t, "FountainRing", AnnulusMesh(3.5f, 3.9f, 64), 0.011f, GoldInlay);
        SoftLight(t, Vector3.up * 2.5f, new Color(0.7f, 0.85f, 1f), 9f, 1.4f);
    }

    // An open nave: a rune medallion set in the floor at its heart, a rune circle turning over it
    private void Nave(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        if (arenaFloorTexture != null)
        {
            Material medallion = Mats.Get("palace_medallion", Color.white, 0.5f);
            medallion.SetTexture("_BaseMap", arenaFloorTexture);
            FlatMesh(t, "Medallion", AnnulusMesh(0f, 3.4f, 64), 0.009f, medallion);
        }
        FlatMesh(t, "MedallionRing", AnnulusMesh(3.4f, 3.7f, 64), 0.011f, GoldInlay);
        Ambient.RuneHalo(t, Vector3.zero, 3.2f, new Color(1f, 0.8f, 0.4f) * 0.7f);
        SoftLight(t, Vector3.up * 4f, new Color(1f, 0.85f, 0.6f), 10f, 1.4f);
    }

    // A rune medallion with rainbow light running out across the floor from it, all of it set into the floor
    private void Sanctum(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        if (arenaFloorTexture != null)
        {
            Material medallion = Mats.Get("palace_medallion", Color.white, 0.5f);
            medallion.SetTexture("_BaseMap", arenaFloorTexture);
            FlatMesh(t, "Medallion", AnnulusMesh(0f, 3.4f, 64), 0.009f, medallion);
        }
        FlatMesh(t, "MedallionRing", AnnulusMesh(3.4f, 3.7f, 64), 0.011f, GoldInlay);
        Color[] rainbow = { new Color(1f, 0.3f, 0.3f), new Color(1f, 0.65f, 0.25f), new Color(1f, 0.95f, 0.35f), new Color(0.35f, 1f, 0.5f), new Color(0.35f, 0.65f, 1f), new Color(0.75f, 0.4f, 1f) };
        float reach = Mathf.Min(hx, hz) * 0.75f;
        for (int i = 0; i < 8; i++)
        {
            // Diagonal-ish rays only, so none runs down a carpet
            float angle = 22.5f + i * 45f;
            Color c = rainbow[i % rainbow.Length];
            var ray = new GameObject("BifrostRay");
            ray.transform.SetParent(t, false);
            ray.transform.localPosition = Vector3.up * 0.012f;
            ray.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
            Mats.Shape(PrimitiveType.Cube, ray.transform, new Vector3(0f, 0f, 3.7f + (reach - 3.7f) / 2f), new Vector3(0.14f, 0.01f, reach - 3.7f),
                Mats.Get("bifrost_ray_" + (i % rainbow.Length), c, 0.6f, c * 1.6f));
        }
        Ambient.RayPulses(t, 3.7f, reach, 8);
        Ambient.RuneHalo(t, Vector3.zero, 3.2f, new Color(0.8f, 0.7f, 1f) * 0.7f);
        SoftLight(t, Vector3.up * 4f, new Color(0.8f, 0.75f, 1f), 12f, 2f);
    }

    // ── The themed halls. Everything they stand along the walls has no collision, so the floor stays open to the fight. ──

    private static GameObject feastTable, meadKegs, shieldRack, hallWarBanner;
    private static bool hallPropsLoaded;

    private static void LoadHallProps()
    {
        if (hallPropsLoaded)
            return;
        hallPropsLoaded = true;
        feastTable = Resources.Load<GameObject>("Halls/FeastTable");
        meadKegs = Resources.Load<GameObject>("Halls/MeadKegs");
        shieldRack = Resources.Load<GameObject>("Halls/ShieldRack");
        hallWarBanner = Resources.Load<GameObject>("TyrArena/WarBanner");
    }

    // A wall prop where nothing stands yet, facing the given way: sized by height, or (span) by its longer side
    private bool Dress(GameObject prefab, Room room, List<(Vector3 p, float r)> placed, Vector3 p, float radius, float yaw, float size, bool span = false)
    {
        if (prefab == null || placed.Any(o => Vector3.Distance(o.p, p) < o.r + radius))
            return false;
        if (span)
            PlaceSpan(prefab, room.transform, p, yaw, size);
        else
            PlaceSized(prefab, room.transform, p, yaw, size, false);
        placed.Add((p, radius));
        return true;
    }

    // Scaled so its longer side is this long, measured before it is turned
    private static GameObject PlaceSpan(GameObject prefab, Transform parent, Vector3 local, float yaw, float length)
    {
        GameObject go = Place(prefab, parent, local, 0f, 1f, false);
        Bounds b = BoundsAround(go);
        float s = length / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
        go.transform.localScale *= s;
        go.transform.localPosition += Vector3.up * (-b.min.y * s);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return go;
    }

    private static float FacingIn(Vector3 p) => Quaternion.LookRotation(new Vector3(-p.x, 0f, -p.z)).eulerAngles.y;

    private static float FacingAwayFrom(float side) => side > 0f ? -90f : 90f;   // on the east (+1) or west (-1) wall, facing in

    private static void HallFlicker(Transform t, Vector3 local, Color color, float range, float intensity)
    {
        var go = new GameObject("HearthLight");
        go.transform.SetParent(t, false);
        go.transform.localPosition = local;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        go.AddComponent<FlickerLight>();
    }

    // 발할라 연회장: Odin's feast hall. A hearth of embers glows in the floor at its heart; long tables laid with the
    // feast line the side walls under hanging candlelight, barrels of mead stand in the corners and the warriors'
    // shields hang on racks along the far wall.
    private void FeastHall(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Material embers = Mats.Get("hall_embers", new Color(0.18f, 0.05f, 0.03f), 0.2f, new Color(0.55f, 0.12f, 0.02f));
        FlatMesh(t, "Hearth", AnnulusMesh(0f, 2.6f, 64), 0.009f, embers);
        FlatMesh(t, "HearthRing", AnnulusMesh(2.6f, 3f, 64), 0.011f, GoldInlay);
        FlatMesh(t, "HearthRingOuter", AnnulusMesh(3.5f, 3.7f, 64), 0.011f, GoldInlay);
        Ambient.RuneHalo(t, Vector3.zero, 3.3f, new Color(1f, 0.5f, 0.2f) * 0.8f);
        HallFlicker(t, Vector3.up * 1.2f, new Color(1f, 0.55f, 0.25f), 10f, 2.4f);
        Ambient.HallAir(t, room.Size, 0);

        foreach (float side in new[] { -1f, 1f })
            foreach (float k in new[] { -0.42f, 0.42f })
            {
                var p = new Vector3(side * (hx - 2.1f), 0f, k * hz);
                if (Dress(feastTable, room, placed, p, 2f, FacingAwayFrom(side), 4.4f, true))
                {
                    // (its two ends too, so nothing is set down across the table)
                    placed.Add((p + Vector3.forward * 1.8f, 1f));
                    placed.Add((p - Vector3.forward * 1.8f, 1f));
                    CandleLight(t, p + Vector3.up * 3.4f);
                }
            }
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var corner = new Vector3(sx * (hx - 2f), 0f, sz * (hz - 2f));
                Dress(meadKegs, room, placed, corner, 1.5f, FacingIn(corner), 1.9f);
            }
        foreach (float x in new[] { -0.45f, 0.45f })
            Dress(shieldRack, room, placed, new Vector3(x * hx, 0f, hz - 1.4f), 1.8f, 180f, 3.4f, true);
        SoftLight(t, Vector3.up * 5f, new Color(1f, 0.72f, 0.45f), 18f, 1.5f);
    }

    // 에인헤랴르 훈련장: where the chosen dead train for Ragnarök. A sand duelling circle in the middle, racks of
    // weapons and shields along the walls, war banners in the corners with braziers burning beside them.
    private void TrainingYard(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Material sand = Mats.Get("yard_sand", new Color(0.74f, 0.6f, 0.42f), 0.05f);
        Material scored = Mats.Get("yard_line", new Color(0.6f, 0.12f, 0.08f), 0.2f, new Color(0.9f, 0.15f, 0.05f));
        FlatMesh(t, "DuelCircle", AnnulusMesh(0f, 3.6f, 72), 0.009f, sand);
        FlatMesh(t, "DuelRing", AnnulusMesh(3.6f, 3.9f, 72), 0.011f, GoldInlay);
        FlatMesh(t, "DuelLine", AnnulusMesh(1.5f, 1.62f, 64), 0.011f, scored);
        foreach (float a in new[] { 45f, 135f })
        {
            var mark = Mats.Shape(PrimitiveType.Cube, t, Vector3.up * 0.011f, new Vector3(0.12f, 0.01f, 6.8f), scored, false, "DuelMark");
            mark.transform.localRotation = Quaternion.Euler(0f, a, 0f);
        }
        Ambient.RuneHalo(t, Vector3.zero, 3.4f, new Color(1f, 0.35f, 0.2f) * 0.7f);
        Ambient.HallAir(t, room.Size, 1);

        foreach (float side in new[] { -1f, 1f })
        {
            foreach (float k in new[] { -0.45f, 0.45f })
            {
                var p = new Vector3(side * (hx - 1.5f), 0f, k * hz);
                if (weaponRackPrefab != null && !placed.Any(o => Vector3.Distance(o.p, p) < o.r + 1.4f))
                {
                    Place(weaponRackPrefab, t, p, FacingAwayFrom(side), 1f, false);
                    placed.Add((p, 1.4f));
                }
            }
            Dress(shieldRack, room, placed, new Vector3(side * (hx - 1.5f), 0f, 0f), 1.8f, FacingAwayFrom(side), 3.4f, true);
        }
        foreach (float x in new[] { -0.45f, 0.45f })
            Dress(shieldRack, room, placed, new Vector3(x * hx, 0f, hz - 1.4f), 1.8f, 180f, 3.4f, true);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var corner = new Vector3(sx * (hx - 1.8f), 0f, sz * (hz - 1.8f));
                if (Dress(hallWarBanner, room, placed, corner, 1.2f, FacingIn(corner), 4.2f))
                    Brazier(t, corner - corner.normalized * 1.6f, false);
            }
        SoftLight(t, Vector3.up * 5f, new Color(1f, 0.62f, 0.38f), 18f, 1.6f);
    }

    // 별빛 천문대: a hall open to the stars. A disc of the cosmos itself set into the floor, constellations traced
    // across the marble in lines of light, the astrolabe of Asgard by the far wall and star crystals hanging in the air.
    private void Observatory(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Material sky = Mats.Get("hall_starfield", new Color(0.55f, 0.6f, 1f), 0.7f, new Color(0.08f, 0.1f, 0.25f));
        if (ground != null)
            sky.SetTexture("_BaseMap", ground);
        FlatMesh(t, "StarDisc", AnnulusMesh(0f, 3.6f, 72), 0.009f, sky);
        FlatMesh(t, "StarRing", AnnulusMesh(3.6f, 3.85f, 72), 0.011f, GoldInlay);
        Ambient.RuneHalo(t, Vector3.zero, 3.4f, new Color(0.55f, 0.65f, 1f) * 0.8f);

        // Constellations in the four quarters between the carpets, and a small one in the disc
        Material line = Mats.Get("constellation_line", new Color(0.45f, 0.6f, 1f), 0.6f, new Color(0.9f, 1.3f, 2.6f));
        Material star = Mats.Get("constellation_star", Color.white, 0.6f, new Color(2.6f, 2.6f, 3.2f));
        float reach = Mathf.Min(hx, hz) - 2.5f;
        for (int q = 0; q < 5; q++)
        {
            Vector3 c = q < 4 ? Quaternion.Euler(0f, 45f + q * 90f, 0f) * Vector3.forward * Mathf.Lerp(5.2f, reach, 0.55f) : Vector3.zero;
            float spread = q < 4 ? 3f : 1.6f;
            var stars = new List<Vector3>();
            for (int i = 0; i < (q < 4 ? 6 : 4); i++)
            {
                Vector2 r = Random.insideUnitCircle * spread;
                stars.Add(c + new Vector3(r.x, 0f, r.y));
            }
            for (int i = 0; i < stars.Count; i++)
            {
                float size = i % 3 == 0 ? 0.42f : 0.28f;
                Mats.Shape(PrimitiveType.Cylinder, t, stars[i] + Vector3.up * 0.013f, new Vector3(size, 0.005f, size), star, false, "Star");
                Vfx.Decal(t.TransformPoint(stars[i] + Vector3.up * 0.02f), size * 4f, new Color(0.6f, 0.75f, 1f) * 0.6f, "ring", 0f).transform.SetParent(t, true);
                if (i == 0)
                    continue;
                Vector3 a = stars[i - 1], b = stars[i];
                var seg = Mats.Shape(PrimitiveType.Cube, t, (a + b) / 2f + Vector3.up * 0.012f, new Vector3(0.1f, 0.01f, Vector3.Distance(a, b)), line, false, "StarLine");
                seg.transform.localRotation = Quaternion.LookRotation(b - a);
            }
        }

        // The astrolabe against the first wall without a door, the crystals hanging over the corners
        foreach (Vector3 spot in new[] { new Vector3(0f, 0f, hz - 2.2f), new Vector3(-(hx - 2.2f), 0f, 0f), new Vector3(hx - 2.2f, 0f, 0f) })
            if (placed.All(o => Vector3.Distance(o.p, spot) > o.r + 2f) && Ambient.Astrolabe(t, spot, 0.9f))
            {
                placed.Add((spot, 2f));
                break;
            }
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var corner = new Vector3(sx * (hx - 2.4f), 0f, sz * (hz - 2.4f));
                if (placed.All(o => Vector3.Distance(o.p, corner) > o.r + 1f))
                {
                    Ambient.FrostCrystal(t, corner, Random.Range(2.6f, 3.2f), Random.Range(0.8f, 1f));
                    placed.Add((corner, 1f));
                }
            }
        Ambient.HallAir(t, room.Size, 2);
        SoftLight(t, Vector3.up * 5f, new Color(0.6f, 0.68f, 1f), 18f, 1.8f);
    }

    // 이둔의 황금 정원: Idun's garden, where the golden apples of youth grow. Golden apple trees stand in the corners
    // shedding their leaves, apples lie fallen on the floor beneath them, and a living medallion glows green-gold
    // at the garden's heart.
    private void Garden(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        if (arenaFloorTexture != null)
        {
            Material medallion = Mats.Get("garden_medallion", new Color(0.75f, 1f, 0.7f), 0.5f, new Color(0.05f, 0.12f, 0.03f));
            medallion.SetTexture("_BaseMap", arenaFloorTexture);
            FlatMesh(t, "Medallion", AnnulusMesh(0f, 3.4f, 64), 0.009f, medallion);
        }
        Material moss = Mats.Get("garden_moss", new Color(0.35f, 0.55f, 0.2f), 0.15f, new Color(0.05f, 0.12f, 0.02f));
        FlatMesh(t, "MossRing", AnnulusMesh(3.4f, 3.75f, 64), 0.011f, moss);
        FlatMesh(t, "GoldRing", AnnulusMesh(3.75f, 3.9f, 64), 0.012f, GoldInlay);
        Ambient.RuneHalo(t, Vector3.zero, 3.2f, new Color(0.6f, 1f, 0.45f) * 0.7f);

        Material apple = Mats.Get("golden_apple", new Color(1f, 0.78f, 0.25f), 0.8f, new Color(0.9f, 0.6f, 0.1f));
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                // The trees by the near (south) wall are kept small, so they don't hide the fight from the camera
                bool near = sz < 0f;
                var corner = new Vector3(sx * (hx - (near ? 2f : 2.6f)), 0f, sz * (hz - (near ? 2f : 2.6f)));
                if (placed.Any(o => Vector3.Distance(o.p, corner) < o.r + 2f))
                    continue;
                Place(goldenTreePrefab, t, corner, Random.Range(0f, 360f), near ? Random.Range(0.6f, 0.7f) : Random.Range(1.05f, 1.25f), false);
                placed.Add((corner, 2f));
                FlatMesh(t, "TreeMoss", AnnulusMesh(0f, 2.2f, 40), 0.008f, moss).transform.localPosition = corner + Vector3.up * 0.008f;
                for (int i = 0; i < 4; i++)
                {
                    Vector2 r = Random.insideUnitCircle.normalized * Random.Range(1f, 2.2f);
                    Mats.Shape(PrimitiveType.Sphere, t, corner + new Vector3(r.x, 0.12f, r.y), Vector3.one * 0.24f, apple, false, "GoldenApple");
                }
            }
        // Rune stones carved with Idun's blessing along the side walls
        foreach (float side in new[] { -1f, 1f })
            Dress(runeStonePrefab, room, placed, new Vector3(side * (hx - 1.4f), 0f, Random.Range(-0.3f, 0.3f) * hz), 1.2f, FacingAwayFrom(side), 2.2f);
        Ambient.HallAir(t, room.Size, 3);
        SoftLight(t, Vector3.up * 5f, new Color(0.85f, 1f, 0.6f), 18f, 1.6f);
    }

    // ───────────── Asgard shop ─────────────

    // A merchant's hall: Brokkr behind a marble counter on the wall without a door, the runes for sale on
    // pedestals in an arc on a gold-ringed medallion before him, treasure chests by the counter and weapon
    // racks along the side walls
    // (forge: the same hall as the upgrade room — 강화방 — Brokkr's anvil on the medallion instead of wares)
    private void PalaceShop(Room room, int depth, List<(Vector3 p, float r)> placed, bool forge = false)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Vector3 normal = Vector3.forward;
        foreach (Vector2Int d in new[] { Vector2Int.up, Vector2Int.left, Vector2Int.right, Vector2Int.down })
        {
            var n = new Vector3(d.x, 0f, d.y);
            Vector3 spot = n * ((d.y != 0 ? hz : hx) - 3f);
            if (placed.All(o => Vector3.Distance(o.p, spot) > o.r + 2f))
            {
                normal = n;
                break;
            }
        }
        float depthHalf = normal.z != 0f ? hz : hx, width = normal.z != 0f ? hx : hz;
        Vector3 side = Vector3.Cross(Vector3.up, normal);
        float faceIn = Quaternion.LookRotation(-normal).eulerAngles.y;

        Vector3 counter = normal * (depthHalf - 3f);
        Place(shopCounterPrefab, t, counter, faceIn, 1.2f);
        placed.Add((counter, 3.2f));
        // Brokkr stands at the counter's end on the shop side, where the camera can see him over it
        if (shopkeeperPrefab != null)
        {
            Vector3 keeper = counter + side * 3.4f - normal * 0.9f;
            Place(shopkeeperPrefab, t, keeper, faceIn - 25f, 1.3f);
            placed.Add((keeper, 1f));
        }
        PlaceFree(treasureChestPrefab, room, placed, counter - side * 4.4f + normal * 0.4f, 1.3f, faceIn + 20f);
        PlaceFree(treasureChestPrefab, room, placed, counter + side * 5.4f + normal * 0.9f, 1.3f, faceIn - 20f);

        // The wares: an arc of pedestals on a medallion in front of the counter, lit from above
        Vector3 arc = counter - normal * 4.4f;
        if (arenaFloorTexture != null)
        {
            Material medallion = Mats.Get("palace_medallion", Color.white, 0.5f);
            medallion.SetTexture("_BaseMap", arenaFloorTexture);
            var disc = new GameObject("ShopMedallion");
            disc.transform.SetParent(t, false);
            disc.transform.localPosition = arc;
            FlatMesh(disc.transform, "Medallion", AnnulusMesh(0f, 3.4f, 64), 0.009f, medallion);
            FlatMesh(disc.transform, "MedallionRing", AnnulusMesh(3.4f, 3.7f, 64), 0.011f, GoldInlay);
            Ambient.RuneHalo(disc.transform, Vector3.zero, 3.2f, new Color(1f, 0.8f, 0.4f) * 0.7f);
        }
        if (forge)
        {
            SkillForgeStation.Create(t, arc);
            // the master anvil (VARCO) stands in the forge's rune circle
            RouteProp("ForgeAnvil", t, arc, faceIn, 1.4f, new Color(0.3f, 0.28f, 0.3f));
        }
        else
        {
            // the wares: three skill orbs for sale
            var offered = SkillSystem.EquippedIds();
            for (int i = 0; i < 3; i++)
            {
                SkillDef def = SkillCatalog.Random(offered);
                offered.Add(def.id);
                Vector3 p = arc + Quaternion.AngleAxis((i - 1) * 40f, Vector3.up) * normal * 2.4f;
                SkillOrb.Create(t, p, new SkillInstance(def), 45 + depth * 5);
                placed.Add((p, 1.4f));
            }
            // and a 새로고침 (reroll for the boss special upgrade's choices)
            Vector3 rp = arc + Quaternion.AngleAxis(100f, Vector3.up) * normal * 2.6f;
            SkillToken.Create(t, rp, SkillToken.Kind.Reroll, SkillSystem.RerollPrice);
            placed.Add((rp, 1.2f));
            // and the stone that swaps the wares (상점 리롤, as many times as the token upgrade allows)
            Vector3 sp = arc + Quaternion.AngleAxis(-100f, Vector3.up) * normal * 2.6f;
            SkillToken.Create(t, sp, SkillToken.Kind.ShopReroll, 0);
            placed.Add((sp, 1.2f));
        }
        placed.Add((arc, 3.6f));
        SoftLight(t, arc + Vector3.up * 4f, new Color(1f, 0.85f, 0.6f), 9f, 2.2f);

        // Weapon racks along the side walls, facing in
        foreach (float s in new[] { -1f, 1f })
            foreach (float k in new[] { 0.15f, -0.45f })
                PlaceFree(weaponRackPrefab, room, placed, side * s * (width - 1.6f) + normal * depthHalf * k, 1.3f,
                    Quaternion.LookRotation(-side * s).eulerAngles.y);

        PalaceHall(room, placed, 0.5f);
    }

    // ───────────── The gift halls (생명의 결정 · 치유의 샘 · 황금 보물고 · 승천의 신전 · 노른의 카드 · 정수의 보고) ─────────────
    // Each is laid out for the camera (its back on the far, north side), round one centrepiece the player walks up to.

    // A prop made to a width in metres instead of a height (VARCO props are 1 m tall; their box gives the footprint)
    private static GameObject RoutePropWide(string name, Transform parent, Vector3 local, float yaw, float width, Color stand)
    {
        GameObject prefab = Route.Prop(name);
        float height = width;
        if (prefab != null && prefab.GetComponent<BoxCollider>() is BoxCollider box)
            height = width / Mathf.Max(0.05f, Mathf.Max(box.size.x, box.size.z));
        return RouteProp(name, parent, local, yaw, height, stand);
    }

    // A round floor painting (VARCO texture in Resources/Textures), with a gold rim
    private static void FloorDisc(Transform t, string name, Vector3 local, float radius, string texture, Color fallback, Color? emission = null, bool rim = true)
    {
        var tex = Resources.Load<Texture2D>("Textures/" + texture);
        var disc = new GameObject(name);
        disc.transform.SetParent(t, false);
        disc.transform.localPosition = local;
        Material mat = Mats.Get("disc_" + texture, tex != null ? Color.white : fallback, 0.3f);
        if (tex != null)
        {
            mat.SetTexture("_BaseMap", tex);
            // (painted on a clear background: cut out round the edge)
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.4f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = 2450;
            if (emission != null)
            {
                mat.SetTexture("_EmissionMap", tex);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }
        }
        FlatMesh(disc.transform, name + "Floor", AnnulusMesh(0f, radius, 80), 0.009f, mat);
        if (rim)
            FlatMesh(disc.transform, name + "Rim", AnnulusMesh(radius, radius + 0.28f, 80), 0.011f, GoldInlay);
    }

    // A shaft of light falling from the vault of the hall onto a spot (translucent, breathing)
    private static void LightShaft(Transform t, Vector3 local, Color color, float width, float height = 11f)
    {
        // additive see-through light: a faint wide halo and a thin brighter core, so the beam glows without hiding what stands behind it
        foreach (var (w, a, tag) in new[] { (width * 2.2f, 0.10f, "LightShaftHalo"), (width * 0.55f, 0.30f, "LightShaft") })
        {
            var beam = Mats.Shape(PrimitiveType.Cylinder, t, local + Vector3.up * (height / 2f + 0.3f), new Vector3(w, height / 2f, w), ShaftMaterial(color, a), false, tag);
            var r = beam.GetComponent<Renderer>();
            if (r != null) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
            var col = beam.GetComponent<Collider>();
            if (col != null) Destroy(col);
            ForgeFloat.Add(beam, 0f, 0f, 0f, pulse: true);
        }
        SoftLight(t, local + Vector3.up * 3.5f, color, 9f, 2.6f);
    }

    private static readonly Dictionary<string, Material> shaftMats = new Dictionary<string, Material>();

    private static Material ShaftMaterial(Color color, float strength)
    {
        string key = ColorUtility.ToHtmlStringRGB(color) + strength.ToString("0.00");
        if (shaftMats.TryGetValue(key, out Material cached) && cached != null) return cached;
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var m = new Material(sh) { name = "shaft_" + key };
        Color c = color * strength; c.a = strength;
        m.SetColor("_BaseColor", c);
        m.color = c;
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 2f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        shaftMats[key] = m;
        return m;
    }

    private bool FreeIn(Room room, List<(Vector3 p, float r)> placed, Vector3 p, float r) =>
        room.OnFloorLocal(new Vector2(p.x, p.z), -0.5f) && placed.All(o => Vector3.Distance(o.p, p) > o.r + r);

    private static float FacingOf(Vector3 from, Vector3 to) => Quaternion.LookRotation(new Vector3(to.x - from.x, 0f, to.z - from.z)).eulerAngles.y;

    // 1) 생명의 결정: the life crystal on its golden jade-rimmed dais, lit red, ruby lamps round it and shards about
    private void LifeCrystalHall(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Vector3 c = Vector3.zero;
        Color ruby = new Color(1f, 0.35f, 0.45f);
        FloorDisc(t, "VigorDisc", c, 5.2f, "VigorFloor", new Color(0.8f, 0.65f, 0.35f));
        RoutePropWide("VigorCrystal", t, c, 180f, 4.4f, new Color(0.9f, 0.3f, 0.4f));
        RouteBoon.Create(t, c, RouteBoon.Kind.Vigor, 5f).reach = 3.4f;
        placed.Add((c, 2.6f));
        SoftLight(t, c + Vector3.up * 3.5f, ruby, 11f, 3f);
        Vfx.Glow(t, c + Vector3.up * 2.4f, 3.2f, ruby * 0.8f, "glow", 0f);
        for (int i = 0; i < 6; i++)
        {
            Vector3 p = c + Quaternion.Euler(0f, 30f + i * 60f, 0f) * Vector3.forward * 6.4f;
            if (!FreeIn(room, placed, p, 0.6f)) continue;
            RouteProp("VigorPillar", t, p, FacingOf(p, c), 1.8f, new Color(0.9f, 0.9f, 0.85f));
            SoftLight(t, p + Vector3.up * 2f, ruby, 4f, 1.2f);
            placed.Add((p, 0.7f));
        }
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { 1f, -1f })
            {
                Vector3 p = new Vector3(sx * (hx - 2f), 0f, sz * (hz - 2f));
                if (!FreeIn(room, placed, p, 0.9f)) continue;
                RouteProp("VigorShard", t, p, Random.Range(0f, 360f), 1.5f, ruby);
                SoftLight(t, p + Vector3.up, ruby, 4f, 1.4f);
                placed.Add((p, 1f));
            }
        PalaceHall(room, placed, 0.35f);
    }

    // 2) 치유의 샘: a round pool with a tiered fountain, lily pads, lanterns round its rim, mossy pillars and a shaft of light
    private void HealGrotto(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Vector3 c = Vector3.zero;
        Color aqua = new Color(0.45f, 0.95f, 1f);
        const float poolWidth = 7.2f;
        RoutePropWide("HealPool", t, c, Random.Range(0, 4) * 90f, poolWidth, new Color(0.4f, 0.7f, 0.8f));
        HealSpring.Create(t, c).reach = poolWidth / 2f + 1.3f;
        placed.Add((c, poolWidth / 2f + 0.3f));
        LightShaft(t, c, new Color(0.6f, 0.95f, 1f), 0.3f);
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = c + Quaternion.Euler(0f, 22.5f + i * 45f, 0f) * Vector3.forward * (poolWidth / 2f + 0.9f);
            if (!FreeIn(room, placed, p, 0.3f)) continue;
            RouteProp("HealLantern", t, p, FacingOf(p, c), 0.9f, new Color(0.8f, 0.75f, 0.6f));
            Ambient.Flame(t, p + Vector3.up * 0.75f, 0.22f, false);
            placed.Add((p, 0.4f));
        }
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { 1f, -1f })
            {
                Vector3 p = new Vector3(sx * (hx - 2.6f), 0f, sz * (hz - 2.6f));
                if (!FreeIn(room, placed, p, 0.9f)) continue;
                RouteProp("HealVinePillar", t, p, FacingOf(p, c), 4.6f, new Color(0.5f, 0.55f, 0.5f));
                SoftLight(t, p + Vector3.up * 2.2f - p.normalized * 0.8f, new Color(1f, 0.7f, 0.4f), 5f, 1.4f);
                placed.Add((p, 1f));
            }
        PalaceHall(room, placed, 0.3f);
    }

    // 3) 황금 보물고: the vault's own chest under a shaft of gold, coins all over the floor, heaps and chests round it
    private void GoldHoard(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Vector3 c = Vector3.forward * 0.6f;
        Color gold = new Color(1f, 0.82f, 0.4f);
        FloorDisc(t, "GoldDisc", c, 6f, "GoldFloor", new Color(0.6f, 0.45f, 0.2f), null, false);
        VaultChest chest = VaultChest.Create(t, c, areaGold);
        RouteProp("VaultChest", chest.transform, Vector3.zero, 0f, 1.9f, new Color(0.6f, 0.42f, 0.2f)).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        placed.Add((c, 2.2f));
        LightShaft(t, c, new Color(1f, 0.85f, 0.45f), 0.35f);
        // heaps of coins about the chest
        foreach (float a in new[] { 140f, 220f, 40f, 320f })
        {
            Vector3 p = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 3.6f;
            if (!FreeIn(room, placed, p, 0.8f)) continue;
            RoutePropWide("GoldPile", t, p, Random.Range(0f, 360f), 2.2f, gold);
            placed.Add((p, 1f));
        }
        // stacked and open chests along the walls
        int k = 0;
        foreach (Vector3 p in new[] { new Vector3(-(hx - 2.2f), 0f, hz - 2.4f), new Vector3(hx - 2.2f, 0f, hz - 2.4f), new Vector3(-(hx - 2.2f), 0f, 0.5f),
                     new Vector3(hx - 2.2f, 0f, 0.5f), new Vector3(-(hx - 2.4f), 0f, -(hz - 2.4f)), new Vector3(hx - 2.4f, 0f, -(hz - 2.4f)) })
        {
            if (!FreeIn(room, placed, p, 1f)) continue;
            bool stack = k++ % 3 != 2;
            RouteProp(stack ? "GoldChestStack" : "GoldOpenChest", t, p, FacingOf(p, c) + Random.Range(-15f, 15f), stack ? 2f : 1.1f, new Color(0.45f, 0.3f, 0.2f));
            placed.Add((p, 1.2f));
        }
        PalaceHall(room, placed, 0.3f);
    }

    // 4) 승천의 신전: a golden shrine gate behind the middle, stairs before it, two rows of carved golden pillars and the
    // light of ascension falling on a sun disc where the player stands
    private void AscensionTemple(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Vector3 c = Vector3.back * 1.2f;
        Color sun = new Color(1f, 0.85f, 0.45f);
        FloorDisc(t, "AscendDisc", c, 3.6f, "AscendFloor", new Color(0.9f, 0.75f, 0.4f), sun * 0.35f);
        RouteBoon.Create(t, c, RouteBoon.Kind.Ascend, 4.2f);
        placed.Add((c, 1.4f));
        LightShaft(t, c, sun, 0.45f, 12f);
        Vector3 gate = c + Vector3.forward * 5.4f;
        if (zoneExits.Contains(Vector2Int.up) && gate.z > hz - 5f)
            gate.z = Mathf.Min(gate.z, hz - 5f);
        RoutePropWide("AscendGate", t, gate, 180f, 4.2f, new Color(0.9f, 0.75f, 0.35f));
        placed.Add((gate, 2.4f));
        GameObject stairs = RoutePropWide("AscendStairs", t, gate - Vector3.forward * 2.4f, 180f, 3.6f, new Color(0.85f, 0.7f, 0.4f));
        stairs.transform.localScale = new Vector3(stairs.transform.localScale.x, stairs.transform.localScale.y * 0.4f, stairs.transform.localScale.z);
        foreach (Collider col in stairs.GetComponentsInChildren<Collider>()) Destroy(col);
        SoftLight(t, gate + Vector3.up * 2f - Vector3.forward, sun, 7f, 2.2f);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float z in new[] { 4.6f, 0.6f, -3.4f })
            {
                Vector3 p = new Vector3(sx * Mathf.Min(5.2f, hx - 2.4f), 0f, c.z + z);
                if (!FreeIn(room, placed, p, 0.9f)) continue;
                RouteProp("AscendPillar", t, p, sx < 0 ? 90f : 270f, 5.8f, new Color(0.9f, 0.75f, 0.35f));
                placed.Add((p, 1.1f));
            }
        foreach (float sx in new[] { -1f, 1f })
            Brazier(t, new Vector3(sx * 2.4f, 0f, c.z - 3f));
        PalaceHall(room, placed, 0.3f);
    }

    // 5) 노른의 카드: a round rune table with three gold-framed cards floating over it, sparking with lightning
    private void NornsTable(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Vector3 c = Vector3.zero;
        Color fate = new Color(0.45f, 0.9f, 1f);
        const float tableWidth = 3.4f;
        RoutePropWide("FateTable", t, c, 0f, tableWidth, new Color(0.4f, 0.5f, 0.55f));
        RouteBoon boon = RouteBoon.Create(t, c, RouteBoon.Kind.Fate, 5f);
        boon.reach = tableWidth / 2f + 1.3f;
        placed.Add((c, tableWidth / 2f + 0.3f));
        var cards = new List<GameObject>();
        for (int i = 0; i < 3; i++)
        {
            var root = new GameObject("FateCard" + i);
            root.transform.SetParent(t, false);
            root.transform.localPosition = c + new Vector3((i - 1) * 2.7f, 3.7f + (i == 1 ? 0.35f : 0f), 0.3f);
            root.transform.localRotation = Quaternion.Euler(0f, 180f + (i - 1) * -12f, (i - 1) * -8f);
            GameObject card = RouteProp("FateCard", root.transform, Vector3.zero, 0f, 2.7f, i == 0 ? fate : new Color(1f, 0.85f, 0.45f));
            foreach (Collider col in card.GetComponentsInChildren<Collider>()) Destroy(col);
            card.transform.localPosition = Vector3.down * 1.35f;
            ForgeFloat.Add(root, 0.15f, 1.2f + i * 0.2f, 0f);
            Vfx.Glow(root.transform, Vector3.zero, 3f, (i == 0 ? fate : new Color(1f, 0.85f, 0.45f)) * 0.7f, "glow", 0f);
            cards.Add(root);
        }
        var sparks = cards[1].AddComponent<FateSparks>();
        sparks.cards = cards;
        boon.Used += () => { foreach (GameObject card in cards) if (card != null) card.AddComponent<FateCardFly>(); };
        SoftLight(t, c + Vector3.up * 4f, fate, 10f, 2.6f);
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { 1f, -1f })
            {
                Vector3 p = new Vector3(sx * Mathf.Min(6f, hx - 2.4f), 0f, sz * Mathf.Min(4.6f, hz - 2.4f));
                if (!FreeIn(room, placed, p, 0.9f)) continue;
                RouteProp("FatePillar", t, p, FacingOf(p, c), 5.4f, new Color(0.55f, 0.55f, 0.5f));
                placed.Add((p, 1f));
            }
        PalaceHall(room, placed, 0.3f);
    }

    // 6) 정수의 보고: a golden orb hanging over its pedestal in a pile of treasure, valkyrie statues and urns about it
    private void EssenceTreasury(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        Vector3 c = Vector3.forward * 0.6f;
        Color glow = new Color(1f, 0.78f, 0.4f);
        FloorDisc(t, "EssenceDisc", c, 5.6f, "GoldFloor", new Color(0.6f, 0.45f, 0.2f), null, false);
        RoutePropWide("EssenceOrb", t, c, 180f, 4.4f, glow);
        RouteBoon.Create(t, c, RouteBoon.Kind.Essence, 5.4f).reach = 3.7f;
        placed.Add((c, 2.4f));
        LightShaft(t, c, new Color(1f, 0.85f, 0.5f), 0.35f);
        Vfx.Glow(t, c + Vector3.up * 2.6f, 3.4f, glow * 0.8f, "glow", 0f);
        foreach (float sx in new[] { -1f, 1f })
        {
            Vector3 p = c + new Vector3(sx * Mathf.Min(5.9f, hx - 2.2f), 0f, 1.8f);
            if (!FreeIn(room, placed, p, 0.9f)) continue;
            RouteProp("EssenceStatue", t, p, FacingOf(p, c), 3.6f, new Color(0.95f, 0.9f, 0.8f));
            placed.Add((p, 1f));
        }
        foreach (float a in new[] { 140f, 220f, 60f, 300f })
        {
            Vector3 p = c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 5.3f;
            if (!FreeIn(room, placed, p, 0.8f)) continue;
            RoutePropWide("GoldPile", t, p, Random.Range(0f, 360f), 2f, glow);
            placed.Add((p, 0.9f));
        }
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { 1f, -1f })
            {
                Vector3 p = new Vector3(sx * (hx - 2f), 0f, sz * (hz - 2f));
                if (!FreeIn(room, placed, p, 0.7f)) continue;
                RouteProp("EssenceUrn", t, p, Random.Range(0f, 360f), 1.5f, glow);
                placed.Add((p, 0.8f));
            }
        PalaceHall(room, placed, 0.3f);
    }

    // ───────────── Asgard support altar: the rune sanctum (보조마법의 제단) ─────────────

    // A ritual circle (VARCO props, Resources/Props/Rune*): a round floor of blue stone ringed in gold with a glowing
    // constellation in it; the three gifts on rune lecterns round its middle, the reroll stone at its near edge;
    // stairs at its edge, rune discs by the far stairs, standing stones round it, carved panels of Odin and his ravens
    // on the far wall, crystals in the corners and cyan-fire braziers at the front
    private void RuneSanctum(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        bool northDoor = zoneExits.Contains(Vector2Int.up);
        bool Free(Vector3 p, float r) => room.OnFloorLocal(new Vector2(p.x, p.z), -0.5f) && placed.All(o => Vector3.Distance(o.p, p) > o.r + r);
        Vector3 center = Vector3.back * 0.3f;
        Vector3 At(float angle, float radius) => center + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
        float Facing(Vector3 p) => Quaternion.LookRotation(new Vector3(center.x - p.x, 0f, center.z - p.z)).eulerAngles.y;
        Color cyan = new Color(0.35f, 0.95f, 1f);
        void NoCollider(GameObject go) { foreach (Collider c in go.GetComponentsInChildren<Collider>()) Destroy(c); }

        // The ritual floor
        var floorTex = Resources.Load<Texture2D>("Textures/RuneSanctumFloor");
        var disc = new GameObject("RuneCircle");
        disc.transform.SetParent(t, false);
        disc.transform.localPosition = center;
        Material mat = Mats.Get("rune_sanctum_floor", floorTex != null ? Color.white : new Color(0.15f, 0.25f, 0.5f), 0.3f);
        if (floorTex != null)
        {
            mat.SetTexture("_BaseMap", floorTex);
            mat.SetTexture("_EmissionMap", floorTex);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.35f, 0.55f, 0.6f));
        }
        FlatMesh(disc.transform, "RuneFloor", AnnulusMesh(0f, 6f, 80), 0.009f, mat);
        FlatMesh(disc.transform, "RuneRim", AnnulusMesh(6f, 6.3f, 80), 0.011f, GoldInlay);
        Ambient.RuneHalo(disc.transform, Vector3.zero, 2.2f, cyan * 0.6f);
        SoftLight(t, center + Vector3.up * 4.5f, new Color(0.55f, 0.85f, 1f), 12f, 2.4f);

        // The three gifts on lecterns round the middle (one may be taken), the stone to draw again at the near edge
        var offered = SkillSystem.EquippedIds();
        foreach (float angle in new[] { 90f, 210f, 330f })
        {
            Vector3 p = At(angle, 2.8f);
            RouteProp("RuneLectern", t, p, Facing(p) + 180f, 0.6f, new Color(0.4f, 0.55f, 0.75f));
            SkillDef def = SkillCatalog.Random(offered);
            offered.Add(def.id);
            SkillOrb.Create(t, p + Vector3.up * 0.5f, new SkillInstance(def), 0).altar = true;
            placed.Add((p, 1.3f));
        }
        Vector3 stone = At(270f, 4.6f);
        RouteProp("RuneLectern", t, stone, 0f, 0.6f, new Color(0.4f, 0.55f, 0.75f));
        SkillToken.Create(t, stone + Vector3.up * 0.5f, SkillToken.Kind.AltarReroll, SkillSystem.AltarRerollPrice);
        placed.Add((stone, 1.2f));
        placed.Add((center, 1.6f));

        // Stairs at the circle's edge (low, walked over), rune discs beside the far ones
        foreach (float angle in new[] { 90f, 180f, 0f })
        {
            Vector3 p = At(angle, 6.7f);
            if (!room.OnFloorLocal(new Vector2(p.x, p.z), 0.3f))
                continue;
            GameObject stairs = RouteProp("RuneStairs", t, p, Facing(p) + 180f, 1.1f, new Color(0.35f, 0.5f, 0.7f));
            stairs.transform.localScale = new Vector3(stairs.transform.localScale.x, stairs.transform.localScale.y * 0.45f, stairs.transform.localScale.z);
            NoCollider(stairs);
        }
        foreach (float angle in new[] { 68f, 112f })
        {
            Vector3 p = At(angle, 7f);
            if (!Free(p, 0.9f))
                continue;
            RouteProp("RuneDisc", t, p, Facing(p), 2.2f, new Color(0.35f, 0.5f, 0.75f));
            placed.Add((p, 1f));
        }

        // Standing stones round the circle (the near side left open, where the camera looks in)
        foreach (float angle in new[] { 40f, 140f, 205f, 240f, 300f, 335f })
        {
            Vector3 p = At(angle, 8f);
            if (!Free(p, 0.9f))
                continue;
            RouteProp("RuneMonolith", t, p, Facing(p), 3.4f, new Color(0.35f, 0.5f, 0.75f));
            placed.Add((p, 1.1f));
        }

        // The far wall: carved panels of Odin and his ravens (beside the door when there is one)
        float panelZ = hz - 0.9f;
        float[] xs = northDoor ? new[] { -(ExitWidth + 2.6f), ExitWidth + 2.6f } : new[] { -4.8f, 0f, 4.8f };
        foreach (float x in xs)
        {
            Vector3 p = new Vector3(x, 0f, panelZ);
            if (!Free(p, 1.2f))
                continue;
            RouteProp("RuneWallPanel", t, p, 180f, 5f, new Color(0.3f, 0.4f, 0.6f));
            placed.Add((p, 1.6f));
        }

        // Cyan-fire braziers at the circle's near side, crystals in the corners
        foreach (float angle in new[] { 225f, 315f })
        {
            Vector3 p = At(angle, 7.1f);
            if (!Free(p, 0.6f))
                continue;
            RouteProp("RuneBrazier", t, p, Random.Range(0f, 360f), 1.1f, new Color(0.3f, 0.5f, 0.7f));
            SoftLight(t, p + Vector3.up * 1.6f, cyan, 6f, 2f);
            placed.Add((p, 0.8f));
        }
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { 1f, -1f })
            {
                Vector3 p = new Vector3(sx * (hx - 1.8f), 0f, sz * (hz - 1.8f));
                if (!Free(p, 1f))
                    continue;
                RouteProp("RuneCrystal", t, p, Random.Range(0f, 360f), 1.8f, new Color(0.4f, 0.8f, 1f));
                SoftLight(t, p + Vector3.up * 1.2f, cyan, 5f, 1.6f);
                placed.Add((p, 1.1f));
            }

        PalaceHall(room, placed, 0.35f);
    }

    // ───────────── Asgard shop: the gods' market (상점) ─────────────

    // A market under the palace roof (VARCO props, Resources/Props/Bazaar*): a round star-and-moon rug in the middle
    // with the Bifrost crystal on its golden font; the wares on marble pedestals in an arc behind it; canopied stalls
    // on the side walls (Brokkr keeps one); banners, a potion shelf and tomes along the back wall; barrels, crates and
    // a sack of gold in the corners
    private void PalaceBazaar(Room room, int depth, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        // (laid out for the camera: the wares and the back of the market always on the far, north side)
        Vector3 normal = Vector3.forward, side = Vector3.right;
        float faceIn = 180f;
        bool northDoor = zoneExits.Contains(Vector2Int.up);
        bool Free(Vector3 p, float r) => room.OnFloorLocal(new Vector2(p.x, p.z), -0.5f) && placed.All(o => Vector3.Distance(o.p, p) > o.r + r);
        GameObject Prop(string name, Vector3 p, float yaw, float height, float r, Color stand)
        {
            GameObject go = RouteProp(name, t, p, yaw, height, stand);
            placed.Add((p, r));
            return go;
        }

        // The rug, and the Bifrost crystal on its font in the middle of it
        Vector3 center = Vector3.back * 0.4f;
        var rugTex = Resources.Load<Texture2D>("Textures/BazaarRug");
        var rug = new GameObject("BazaarRug");
        rug.transform.SetParent(t, false);
        rug.transform.localPosition = center;
        Material rugMat = Mats.Get("bazaar_rug", rugTex != null ? Color.white : new Color(0.12f, 0.2f, 0.45f), 0.15f);
        if (rugTex != null)
            rugMat.SetTexture("_BaseMap", rugTex);
        FlatMesh(rug.transform, "Rug", AnnulusMesh(0f, 5.2f, 72), 0.009f, rugMat);
        FlatMesh(rug.transform, "RugRim", AnnulusMesh(5.2f, 5.45f, 72), 0.011f, GoldInlay);
        GameObject font = Prop("BazaarFont", center, 180f, 3.6f, 1.6f, new Color(0.9f, 0.75f, 0.4f));
        foreach (Collider c in font.GetComponentsInChildren<Collider>())
            if (c is BoxCollider box) box.size = new Vector3(box.size.x * 0.6f, box.size.y, box.size.z * 0.6f);
        SoftLight(t, center + Vector3.up * 3.4f, new Color(0.7f, 0.85f, 1f), 8f, 2.4f);
        Vfx.Glow(t, center + Vector3.up * 3f, 2.4f, new Color(0.75f, 0.9f, 1f) * 0.7f, "glow", 0f);

        // The wares: three skill orbs on pedestals in an arc behind the font, the two reroll stones at its ends
        Vector3 arcCenter = center + normal * 0.2f;
        var offered = SkillSystem.EquippedIds();
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = arcCenter + Quaternion.AngleAxis((i - 1) * 40f, Vector3.up) * normal * 3.7f;
            RouteProp("BazaarPedestal", t, p, faceIn, 0.5f, new Color(0.92f, 0.9f, 0.85f));
            SkillDef def = SkillCatalog.Random(offered);
            offered.Add(def.id);
            SkillOrb.Create(t, p + Vector3.up * 0.45f, new SkillInstance(def), 45 + depth * 5);
            placed.Add((p, 1.3f));
        }
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 p = arcCenter + Quaternion.AngleAxis(s * 88f, Vector3.up) * normal * 3.7f;
            RouteProp("BazaarPedestal", t, p, faceIn, 0.5f, new Color(0.92f, 0.9f, 0.85f));
            if (s < 0f)
                SkillToken.Create(t, p + Vector3.up * 0.45f, SkillToken.Kind.Reroll, SkillSystem.RerollPrice);
            else
                SkillToken.Create(t, p + Vector3.up * 0.45f, SkillToken.Kind.ShopReroll, 0);
            placed.Add((p, 1.2f));
        }
        SoftLight(t, arcCenter + normal * 3f + Vector3.up * 4f, new Color(1f, 0.88f, 0.65f), 10f, 2f);

        // The stalls on the side walls (along the wall from a door there), facing in; Brokkr keeps the west one with
        // a sack of gold at his feet, the east one has a stack of tomes
        foreach (float s in new[] { -1f, 1f })
        {
            bool door = zoneExits.Contains(s < 0f ? Vector2Int.left : Vector2Int.right);
            Vector3 stall = side * s * (hx - 2.5f) + normal * (door ? Mathf.Min(ExitWidth + 2f, hz - 2.4f) : 1f);
            if (!room.OnFloorLocal(new Vector2(stall.x, stall.z), -0.5f) || placed.Any(o => Vector3.Distance(o.p, stall) < o.r + 1.2f))
                continue;
            float face = Quaternion.LookRotation(-side * s).eulerAngles.y;
            Prop("BazaarStall", stall, face, 3.4f, 2.1f, new Color(0.3f, 0.45f, 0.8f));
            SoftLight(t, stall - side * s * 1.2f + Vector3.up * 2.6f, new Color(1f, 0.82f, 0.55f), 6f, 1.6f);
            Vector3 front = stall - side * s * 2.2f;
            if (s < 0f && shopkeeperPrefab != null)
            {
                Vector3 keeper = front - normal * 1.6f;
                Place(shopkeeperPrefab, t, keeper, face, 1.3f);
                placed.Add((keeper, 0.9f));
                Vector3 sack = keeper - normal * 1.2f + side * 0.4f;
                if (Free(sack, 0.5f))
                    Prop("BazaarSack", sack, Random.Range(0f, 360f), 0.8f, 0.7f, new Color(0.55f, 0.4f, 0.25f));
            }
            else
            {
                Vector3 tomes = front - normal * 1.7f;
                if (Free(tomes, 0.5f))
                    Prop("BazaarTomes", tomes, face, 0.9f, 0.7f, new Color(0.5f, 0.2f, 0.2f));
            }
        }

        // The north wall: banners flanking the door (or the shelf), the potion shelf beside them
        float backZ = hz - 1.4f;
        float bannerX = northDoor ? ExitWidth + 1.2f : 3.2f;
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 b = side * s * bannerX + normal * backZ;
            if (Free(b, 0.5f))
                Prop("BazaarBanner", b, faceIn, 3.4f, 0.7f, new Color(0.15f, 0.25f, 0.55f));
        }
        Vector3 shelf = northDoor ? -side * (ExitWidth + 4f) + normal * backZ : normal * backZ;
        if (Free(shelf, 1.2f))
            Prop("BazaarShelf", shelf, faceIn, 2.9f, 1.4f, new Color(0.45f, 0.3f, 0.18f));
        if (northDoor)
        {
            Vector3 tomes = side * (ExitWidth + 3.6f) + normal * (backZ - 0.2f);
            if (Free(tomes, 0.6f))
                Prop("BazaarTomes", tomes, faceIn, 0.9f, 0.7f, new Color(0.5f, 0.2f, 0.2f));
        }

        // Barrels and crates in the corners
        int k = 0;
        foreach (float sz in new[] { 1f, -1f })
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 p = side * sx * (hx - 1.8f) + normal * sz * (hz - 1.8f);
                if (!Free(p, 1f))
                    continue;
                Prop((k++ % 2 == 0) ? "BazaarBarrels" : "BazaarCrates", p, Random.Range(0f, 360f), 1.6f, 1.2f, new Color(0.5f, 0.35f, 0.2f));
            }

        PalaceHall(room, placed, 0.35f);
    }

    // ───────────── Asgard upgrade room (강화방) ─────────────

    // The forge of the gods (VARCO props, Resources/Props/Forge*): a lava-veined medallion in the middle with the
    // upgrade altar, a gold ring standing on it and a blade hovering in the ring; a beam of light rising through a
    // broken stone ring that turns overhead; giant swords driven into the corners; candle posts lining the way in;
    // Brokkr's workbench, scrolls and ember crystals along the walls
    private void PalaceForge(Room room, int depth, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        // the back wall: one without a way in, the far (north) one when it can be, so the camera sees the bench
        Vector2Int entry = zoneExits.Count > 0 ? zoneExits[0] : Vector2Int.down;
        Vector2Int wall = new[] { Vector2Int.up, Vector2Int.left, Vector2Int.right, Vector2Int.down }.FirstOrDefault(d => !zoneExits.Contains(d));
        if (wall == Vector2Int.zero)
            wall = Vector2Int.up;
        Vector3 normal = new Vector3(wall.x, 0f, wall.y);
        float depthHalf = normal.z != 0f ? hz : hx, width = normal.z != 0f ? hx : hz;
        Vector3 side = Vector3.Cross(Vector3.up, normal);
        float faceIn = Quaternion.LookRotation(-normal).eulerAngles.y;
        bool Free(Vector3 p, float r) => room.OnFloorLocal(new Vector2(p.x, p.z), -0.5f) && placed.All(o => Vector3.Distance(o.p, p) > o.r + r);
        Color ember = new Color(1f, 0.55f, 0.2f);

        // The medallion: dark basalt veined with lava (VARCO texture), ringed in gold
        Vector3 center = normal * 1.2f;
        var floorTex = Resources.Load<Texture2D>("Textures/ForgeLavaFloor");
        var disc = new GameObject("ForgeMedallion");
        disc.transform.SetParent(t, false);
        disc.transform.localPosition = center;
        Material lava = Mats.Get("forge_lava_floor", Color.white, 0.35f, floorTex != null ? Color.white * 0.9f : ember * 0.3f);
        if (floorTex != null)
        {
            lava.SetTexture("_BaseMap", floorTex);
            lava.SetTexture("_EmissionMap", floorTex);
            lava.EnableKeyword("_EMISSION");
            lava.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.15f) * 0.9f);
        }
        FlatMesh(disc.transform, "LavaFloor", AnnulusMesh(0f, 4.4f, 72), 0.009f, lava);
        FlatMesh(disc.transform, "LavaRing", AnnulusMesh(4.4f, 4.75f, 72), 0.011f, GoldInlay);
        Ambient.RuneHalo(disc.transform, Vector3.zero, 4.1f, ember * 0.7f);

        // The upgrade altar with its gold ring, and the blade that floats in it
        SkillForgeStation.Create(t, center);
        RouteProp("ForgeAltar", t, center, faceIn, 1.1f, new Color(0.45f, 0.32f, 0.2f));
        // (the ring and its blade always face the camera, whichever wall the bench stands on)
        GameObject ring = RouteProp("ForgeRing", t, center + Vector3.up * 1.0f, 180f, 3.6f, new Color(0.9f, 0.7f, 0.3f));
        foreach (Collider c in ring.GetComponentsInChildren<Collider>()) Destroy(c);
        var bladeRoot = new GameObject("ForgeBlade");
        bladeRoot.transform.SetParent(t, false);
        bladeRoot.transform.localPosition = center + Vector3.up * 2.8f;
        bladeRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        GameObject blade = RouteProp("ForgeBlade", bladeRoot.transform, Vector3.zero, 0f, 1.9f, new Color(1f, 0.8f, 0.35f));
        foreach (Collider c in blade.GetComponentsInChildren<Collider>()) Destroy(c);
        blade.transform.localPosition = Vector3.down * 0.95f;
        ForgeFloat.Add(bladeRoot, 0.18f, 1.4f, 25f).axis = Vector3.forward;   // (the blade turns slowly in the ring's plane)
        SoftLight(bladeRoot.transform, Vector3.zero, new Color(1f, 0.6f, 0.25f), 9f, 3.2f);
        placed.Add((center, 3.6f));

        // The beam of light rising out of the ring, through a broken stone ring that turns slowly overhead
        var beam = Mats.Shape(PrimitiveType.Cylinder, t, center + Vector3.up * 9f, new Vector3(0.22f, 7f, 0.22f),
            Mats.Get("forge_beam", new Color(1f, 0.85f, 0.55f), 0.9f, new Color(1f, 0.75f, 0.4f) * 3f), false, "ForgeBeam");
        ForgeFloat.Add(beam, 0f, 0f, 0f, pulse: true);
        // (tipped back to face the camera, its lit face up, the beam through its middle; it rolls slowly round)
        var halo = new GameObject("BrokenRing");
        halo.transform.SetParent(t, false);
        halo.transform.localPosition = center + Vector3.up * 7.8f;
        ForgeFloat.Add(halo, 0.25f, 0.5f, 0f);
        var tilt = new GameObject("Tilt");
        tilt.transform.SetParent(halo.transform, false);
        tilt.transform.localRotation = Quaternion.Euler(-126f, 0f, 0f);
        var roll = new GameObject("Roll");
        roll.transform.SetParent(tilt.transform, false);
        ForgeFloat.Add(roll, 0f, 0f, 8f).axis = Vector3.forward;
        GameObject broken = RouteProp("ForgeBrokenRing", roll.transform, Vector3.zero, 0f, 3.0f, new Color(0.6f, 0.55f, 0.5f));
        foreach (Collider c in broken.GetComponentsInChildren<Collider>()) Destroy(c);
        broken.transform.localPosition = Vector3.down * 1.5f;

        // Braziers flanking the altar
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 b = center + side * s * 3.3f + normal * 1.2f;
            Brazier(t, b);
            placed.Add((b, 0.9f));
        }

        // Giant swords driven into the corners, their cracks glowing
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                Vector3 p = side * sx * (width - 2.4f) + normal * sz * (depthHalf - 2.4f);
                if (!Free(p, 1.4f))
                    continue;
                GameObject sword = RouteProp("ForgeGiantSword", t, p, Random.Range(0f, 360f), 4.6f, new Color(0.25f, 0.22f, 0.22f));
                sword.transform.localRotation *= Quaternion.Euler(Random.Range(-10f, 10f), 0f, Random.Range(-10f, 10f));
                SoftLight(t, p + Vector3.up * 0.8f, new Color(1f, 0.3f, 0.12f), 5f, 1.8f);
                placed.Add((p, 1.6f));
            }

        // Candle posts lining the way in from the door to the medallion
        Vector3 inward = -new Vector3(entry.x, 0f, entry.y);
        Vector3 across = Vector3.Cross(Vector3.up, inward);
        float entryHalf = entry.y != 0 ? hz : hx;
        for (float k = entryHalf - 3f; k > 5.4f; k -= 2.2f)
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 p = -inward * k + across * s * 2.4f;
                // (the doorway's own clearance doesn't keep them out: they line it)
                if (!room.OnFloorLocal(new Vector2(p.x, p.z), -0.5f) || Vector3.Distance(p, center) < 5.4f)
                    continue;
                RouteProp("ForgeCandlePost", t, p, Random.Range(0f, 360f), 1.3f, new Color(0.5f, 0.48f, 0.45f));
                Ambient.Flame(t, p + Vector3.up * 1.32f, 0.35f, false);
                placed.Add((p, 0.7f));
            }

        // Brokkr's workbench against the back wall with the smith himself beside it, scrolls on the other side
        Vector3 bench = normal * (depthHalf - 2.2f) + side * 3.6f;
        if (Free(bench, 1.6f))
        {
            RouteProp("ForgeWorkbench", t, bench, faceIn, 1.3f, new Color(0.45f, 0.3f, 0.18f));
            placed.Add((bench, 1.8f));
            if (shopkeeperPrefab != null)
            {
                Vector3 keeper = bench + side * 2.9f - normal * 0.6f;
                if (Free(keeper, 0.5f) || room.OnFloorLocal(new Vector2(keeper.x, keeper.z), -0.8f))
                {
                    Place(shopkeeperPrefab, t, keeper, faceIn - 25f, 1.3f);
                    placed.Add((keeper, 1f));
                }
            }
        }
        foreach (Vector3 p in new[] { normal * (depthHalf - 2f) - side * 3.8f, center - side * 5.6f - normal * 1.5f })
            if (Free(p, 0.9f))
            {
                RouteProp("ForgeScrolls", t, p, Random.Range(0f, 360f), 0.8f, new Color(0.85f, 0.75f, 0.55f));
                placed.Add((p, 1f));
            }

        // Ember crystals: the forge's ore, glowing along the side walls
        foreach (float s in new[] { -1f, 1f })
            foreach (float k in new[] { 0.25f, -0.35f })
            {
                Vector3 p = side * s * (width - 1.8f) + normal * depthHalf * k + Vector3.Cross(Vector3.up, side) * Random.Range(-0.6f, 0.6f);
                if (!Free(p, 1f))
                    continue;
                RouteProp("ForgeEmberCrystals", t, p, Random.Range(0f, 360f), 1.4f, new Color(0.9f, 0.35f, 0.15f));
                SoftLight(t, p + Vector3.up * 1f, new Color(1f, 0.4f, 0.15f), 4.5f, 1.6f);
                placed.Add((p, 1.2f));
            }

        SoftLight(t, center + Vector3.up * 5f, new Color(1f, 0.75f, 0.5f), 12f, 1.6f);
        PalaceHall(room, placed, 0.4f);
    }

    // Beyond the balustrade the palace goes on: rows of columns and statues fading into the cosmic haze
    private void PalaceOutside(Room room, List<Vector2Int> exits)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        foreach (Vector2Int d in Directions)
        {
            bool alongX = d.y != 0;
            float half = alongX ? hx : hz, offset = alongX ? hz : hx;
            Vector3 normal = new Vector3(d.x, 0f, d.y), along = alongX ? Vector3.right : Vector3.forward;

            // The far and side walls (RoomWalls) hide everything out there but the open south side
            if (d != Vector2Int.down)
                continue;

            // A near row and a sparser far one, staggered, so they read as more halls rather than a forest
            foreach (var (row, step) in new[] { (6f, 9f), (15f, 13f) })
                for (float s = -half + step / 2f; s <= half; s += step)
                {
                    if (exits.Contains(d) && Mathf.Abs(s) < ExitWidth + 2f)
                        continue;
                    PalaceColumn(t, normal * (offset + row) + along * s, 1.1f, false);
                }
        }
        if (statuePrefab != null)
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in palaceWallPrefab != null ? new[] { -1f } : new[] { -1f, 1f })
                {
                    var p = new Vector3(sx * (hx + 8f), 0f, sz * (hz + 8f));
                    Place(statuePrefab, t, p, Quaternion.LookRotation(-p).eulerAngles.y, 1.4f, false);
                }
        if (goldenTreePrefab != null)
            for (int i = 0; i < 6; i++)
            {
                // Out past the open (south) side, where no wall hides them
                float a = Random.Range(Mathf.PI * 1.1f, Mathf.PI * 1.9f);
                var p = new Vector3(Mathf.Cos(a) * (hx + Random.Range(16f, 24f)), 0f, Mathf.Sin(a) * (hz + Random.Range(16f, 24f)));
                Place(goldenTreePrefab, t, p, Random.Range(0f, 360f), Random.Range(0.9f, 1.2f), false);
            }
    }

    // The guardian's round arena: a rune-carved stone floor ringed by fire-crowned pillars and low walls,
    // with a gateway between two smaller pillars where the way out lies
    private void BuildArena(Room room, List<Vector2Int> exits, Realm realm, Transform player)
    {
        Transform t = room.transform;
        const float r = ArenaRadius;
        room.SetRound(r);

        // Floor and a dark stone rim around it
        Material floor;
        if (arenaFloorTexture != null)
        {
            floor = Mats.Get("arena_floor_" + realm.name, Color.Lerp(Color.white, realm.floorColor * 2f, 0.2f), 0.3f);
            floor.SetTexture("_BaseMap", arenaFloorTexture);
        }
        else
            floor = Mats.Get("arena_floor_plain_" + realm.name, Color.Lerp(realm.floorColor, realm.wallColor, 0.4f), 0.25f);
        FlatMesh(t, "ArenaFloor", AnnulusMesh(0f, r + 0.6f, 96), 0.014f, floor);
        FlatMesh(t, "ArenaRim", AnnulusMesh(r + 0.6f, r + 2.4f, 96), 0.018f, Mats.Get("arena_rim_" + realm.name, Color.Lerp(realm.wallColor, Color.black, 0.45f), 0.2f));

        var points = new Vector3[ArenaPillars];
        for (int i = 0; i < ArenaPillars; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2f / ArenaPillars;   // half a step off, so the cardinal exits fall between pillars
            points[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            ArenaPillar(t, points[i], 1f, i % 2 == 0);
        }

        for (int i = 0; i < ArenaPillars; i++)
        {
            Vector3 from = points[i], to = points[(i + 1) % ArenaPillars];
            Vector3 mid = (from + to) / 2f;
            Vector3 dir = (to - from).normalized;
            float chord = Vector3.Distance(from, to);
            var side = new Vector2Int(Mathf.RoundToInt(mid.normalized.x), Mathf.RoundToInt(mid.normalized.z));
            bool cardinal = Mathf.Abs(mid.normalized.x) > 0.99f || Mathf.Abs(mid.normalized.z) > 0.99f;
            bool isExit = cardinal && exits.Contains(side);

            if (!isExit)
            {
                ArenaWall(t, from + dir * 0.9f, to - dir * 0.9f);
                ChordCollider(t, from, to);
                continue;
            }
            // The gateway: two smaller pillars either side of the opening, walls closing the rest
            float gap = ExitWidth / 2f + 0.5f;
            Vector3 left = mid - dir * gap, right = mid + dir * gap;
            ArenaPillar(t, left, 0.75f, false);
            ArenaPillar(t, right, 0.75f, false);
            ChordCollider(t, from, left);
            ChordCollider(t, right, to);
            BuildExit(room, side, r, player, false);
        }

        // In Asgard the guardian's arena lies at the foot of his own hall: Himinbjorg and the Bifrost's gate stand in
        // the far corners between the arena's rim and the hall walls, facing in. (Further out they would be lost:
        // the camera looks steeply down and the haze closes in past 30 m.)
        if (palace)
        {
            float corner = r + WallSetback - 4.5f;
            var gateSpot = new Vector3(-corner, 0f, corner);
            var towerSpot = new Vector3(corner, 0f, corner);
            if (bifrostGatePrefab != null)
                Place(bifrostGatePrefab, t, gateSpot, Quaternion.LookRotation(-gateSpot).eulerAngles.y, 0.6f, false);
            if (himinbjorgPrefab != null)
                Place(himinbjorgPrefab, t, towerSpot, Quaternion.LookRotation(-towerSpot).eulerAngles.y, 0.45f, false);
        }
    }

    // ───────────── The mid-boss's battleground (Asgard: Týr) ─────────────

    public const float WarArenaRadius = 18f;   // to the octagon's corners (its sides stand 16.6 m from the middle)

    // VARCO models and the floor texture, loaded from Resources/TyrArena (so the scene needs no new references)
    private static GameObject warSword, warBanner, warWall, warFenrir;
    // Týr's oath hall round the arena (VARCO, Resources/TyrArena/Hall*): stepped tiers, black-gold pillars, the great
    // door with its two horned sentinels, window walls aglow behind the tiers, ember braziers
    private static GameObject hallPillar, hallGuardian, hallGate, hallStairs, hallBrazier, hallWindow;
    private static Texture2D warFloor;
    private static bool warLoaded;
    private static bool TyrHallReady => hallPillar != null && hallStairs != null && hallGate != null;
    // Týr's battleground belongs to Asgard: other realms' mid-bosses get a hall of their own realm
    private bool WarArena => palace && WarArenaReady;
    // set while the fight hall the mid-boss joins (route mode) is being built: that hall is Týr's battleground too
    private bool warHall;
    private bool IsWarZone(Room.Kind kind) => (kind == Room.Kind.MidBoss || (kind == Room.Kind.Combat && warHall)) && WarArena;

    private static bool WarArenaReady
    {
        get
        {
            if (!warLoaded)
            {
                warLoaded = true;
                warSword = Resources.Load<GameObject>("TyrArena/GiantSword");
                warBanner = Resources.Load<GameObject>("TyrArena/WarBanner");
                warWall = Resources.Load<GameObject>("TyrArena/RuneWall");
                warFenrir = Resources.Load<GameObject>("TyrArena/Fenrir");
                warFloor = Resources.Load<Texture2D>("TyrArena/ArenaFloor");
                hallPillar = Resources.Load<GameObject>("TyrArena/HallPillar");
                hallGuardian = Resources.Load<GameObject>("TyrArena/HallGuardian");
                hallGate = Resources.Load<GameObject>("TyrArena/HallGate");
                hallStairs = Resources.Load<GameObject>("TyrArena/HallStairs");
                hallBrazier = Resources.Load<GameObject>("TyrArena/HallBrazier");
                hallWindow = Resources.Load<GameObject>("TyrArena/HallWindow");
            }
            return warSword != null && warWall != null && warFloor != null;
        }
    }

    // Týr's battleground: an open octagon of dark stone out in the cosmos with his war emblem inlaid in its floor,
    // a colossal sword plunged into each corner with fire burning on its hilt, low rune walls between them, war banners
    // standing outside, and the chained wolf Fenrir — the beast that took his hand — howling beyond the far side.
    // The floor itself stays clear for the fight; the ways out are gaps in the walls on the flat cardinal sides.
    private void BuildWarArena(Room room, List<Vector2Int> exits, Transform player)
    {
        Transform t = room.transform;
        const float r = WarArenaRadius;
        float apothem = r * Mathf.Cos(Mathf.PI / 8f);
        room.SetRound(apothem - 0.6f);

        // The floor and a broken rim of dark rock round it (the octagon turned so its flat sides face the exits)
        Material floor = Mats.Get("war_arena_floor", new Color(0.92f, 0.9f, 0.9f), 0.25f);
        floor.SetTexture("_BaseMap", warFloor);
        Quaternion flatToCardinal = Quaternion.Euler(0f, 22.5f, 0f);
        FlatMesh(t, "WarFloor", AnnulusMesh(0f, r + 0.3f, 8), 0.014f, floor).transform.localRotation = flatToCardinal;
        FlatMesh(t, "WarRim", AnnulusMesh(r + 0.3f, r + 2.6f, 8), 0.018f, Mats.Get("war_arena_rim", new Color(0.12f, 0.1f, 0.1f), 0.2f))
            .transform.localRotation = flatToCardinal;
        // The platform's edge: a thick slab under the rim, so the arena reads as a floating plate of rock
        FlatMesh(t, "WarRimGlow", AnnulusMesh(r + 2.4f, r + 2.75f, 8), 0.02f,
            Mats.Get("war_arena_rim_glow", new Color(0.6f, 0.12f, 0.05f), 0.4f, new Color(1.4f, 0.3f, 0.08f))).transform.localRotation = flatToCardinal;

        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
            corners[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            Vector3 top;
            float faceIn = Quaternion.LookRotation(-corners[i]).eulerAngles.y;
            if (TyrHallReady)
            {
                // The oath hall: a black-gold pillar in each far corner, fire on its crown; the two corners on the
                // camera's side get low ember braziers instead, so nothing tall stands between the camera and the fight
                bool front = corners[i].z < -10f;
                if (front && hallBrazier != null)
                {
                    PlaceSized(hallBrazier, t, corners[i], faceIn, 1.7f, true);
                    top = corners[i] + Vector3.up * 1.75f;
                }
                else
                {
                    PlaceSized(hallPillar, t, corners[i], faceIn + HallPillarYaw, front ? 4f : 9f, true);
                    top = corners[i] + Vector3.up * (front ? 4.1f : 9.1f);
                }
                Ambient.Flame(t, top, front ? 0.7f : 1.1f);
            }
            else
            {
                // A colossal sword in each corner, its hilt crowned with fire
                PlaceSized(warSword, t, corners[i], faceIn, 5.5f, true);
                top = corners[i] + Vector3.up * 5.2f;
                Ambient.Flame(t, top - corners[i].normalized * 0.2f, 1.1f);
            }
            var glow = new GameObject("SwordFire");
            glow.transform.SetParent(t, false);
            glow.transform.localPosition = top - corners[i].normalized * 0.8f;
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.42f, 0.2f);
            light.range = 11f;
            light.intensity = 2.6f;
            light.shadows = LightShadows.None;
            glow.AddComponent<FlickerLight>();
        }

        // The sides: low rune walls between the swords, a gap where a way out lies
        for (int i = 0; i < 8; i++)
        {
            Vector3 from = corners[(i + 7) % 8], to = corners[i];   // the side centred on i * 45°
            Vector3 mid = (from + to) / 2f;
            var side = new Vector2Int(Mathf.RoundToInt(mid.normalized.x), Mathf.RoundToInt(mid.normalized.z));
            bool cardinal = i % 2 == 0;
            Vector3 dir = (to - from).normalized;
            if (cardinal && exits.Contains(side))
            {
                float gap = ExitWidth / 2f + 0.6f;
                RuneWall(t, from + dir * 1.2f, mid - dir * gap);
                RuneWall(t, mid + dir * gap, to - dir * 1.2f);
                ChordCollider(t, from, mid - dir * gap);
                ChordCollider(t, mid + dir * gap, to);
                BuildExit(room, side, apothem, player, false);
                continue;
            }
            RuneWall(t, from + dir * 1.2f, to - dir * 1.2f);
            ChordCollider(t, from, to);
            // War banners standing just outside, two to a side (none on the camera's side, where they'd hide the fight)
            if (warBanner != null && mid.z > -1f && !TyrHallReady)
                foreach (float k in new[] { 0.3f, 0.7f })
                {
                    Vector3 p = Vector3.Lerp(from, to, k) + mid.normalized * 1.6f;
                    PlaceSized(warBanner, t, p, Quaternion.LookRotation(-mid).eulerAngles.y, 4.6f, false);
                }
        }

        if (TyrHallReady)
        {
            BuildOathHall(t, exits, corners, apothem);
            return;
        }

        // Fenrir, chained, howling beyond the far side (or the far corner nearest it if the far side is a way out)
        if (warFenrir != null)
        {
            Vector3 spot = exits.Contains(Vector2Int.up)
                ? Quaternion.Euler(0f, 45f, 0f) * Vector3.forward * (apothem + 7f)
                : Vector3.forward * (apothem + 6.5f);
            PlaceSized(warFenrir, t, spot, Quaternion.LookRotation(-spot).eulerAngles.y, 10f, false);
            var eyes = new GameObject("FenrirGlow");
            eyes.transform.SetParent(t, false);
            eyes.transform.localPosition = spot * 0.85f + Vector3.up * 6f;
            var light = eyes.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.25f, 0.12f);
            light.range = 14f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;
        }
    }

    // Which way each hall model faces once imported (degrees added to "facing the arena's middle")
    private const float HallPillarYaw = 0f, HallStairsYaw = 0f, HallGateYaw = 0f, HallGuardianYaw = 0f, HallWindowYaw = 0f;

    // Týr's oath hall all the way round the arena, after the throne-hall look: on every side broad stone tiers climb
    // away from the rim with window walls glowing behind them, a dark stone floor between, and a tall black-gold pillar
    // at every outer corner where the sides meet. On the far (north) side an approach runs out to the great door with a
    // horned sentinel either side; a way out gets tiers either side of its path and a sentinel pair at its mouth.
    // (The camera's sides are walled too: the camera looks down and ahead, so the tiers and walls behind it stay out of
    // the shot.)
    private void BuildOathHall(Transform t, List<Vector2Int> exits, Vector3[] corners, float apothem)
    {
        const float rimOuter = (WarArenaRadius + 2.75f) * 0.924f;   // the rim's outer edge, measured to a side's middle
        const float wallAt = rimOuter + 7.6f;                        // where the window walls stand (behind the tiers)
        const float cornerStretch = 1.0824f;                         // 1 / cos 22.5°: a side's middle to its corner
        Material approach = Mats.Get("oath_hall_approach", new Color(0.13f, 0.12f, 0.14f), 0.55f);

        // The hall's floor from the rim out to the walls, so no gap between tiers shows the void
        FlatMesh(t, "OathFloor", AnnulusMesh((WarArenaRadius + 2.6f), (wallAt + 1.2f) * cornerStretch, 8), -0.02f, approach)
            .transform.localRotation = Quaternion.Euler(0f, 22.5f, 0f);

        // A tall pillar at each outer corner, where neighbouring tiers and walls meet at an angle
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = corners[i].normalized;
            float yaw = Quaternion.LookRotation(-c).eulerAngles.y + HallPillarYaw;
            PlaceSized(hallPillar, t, c * (rimOuter + 3.6f) * cornerStretch, yaw, 10f, false);
            PlaceSized(hallPillar, t, c * (wallAt + 0.3f) * cornerStretch, yaw, 13.5f, false);
        }

        for (int i = 0; i < 8; i++)
        {
            Vector3 from = corners[(i + 7) % 8], to = corners[i];
            Vector3 dir = (to - from).normalized;
            Vector3 outward = ((from + to) / 2f).normalized;
            var side = new Vector2Int(Mathf.RoundToInt(outward.x), Mathf.RoundToInt(outward.z));
            bool cardinal = i % 2 == 0;
            bool exit = cardinal && exits.Contains(side);
            float faceIn = Quaternion.LookRotation(-outward).eulerAngles.y;
            float sideLength = (from - to).magnitude;

            // The window wall behind, on every side (a way out too: its portal fills the gate, the wall closes behind it)
            if (hallWindow != null)
            {
                // (VARCO made it a block, its face on the front: pressed thin, set back by half its thickness; a little
                // over the side's length, so neighbours meet behind the corner pillars)
                PlaceBox(hallWindow, t, outward * (wallAt + 0.5f), faceIn + HallWindowYaw, (wallAt + 0.5f) * 0.95f, 12f, 1f);
                OathLight(t, outward * (wallAt - 2.5f) + Vector3.up * 6f, new Color(1f, 0.62f, 0.3f), 16f, 2.2f);
            }

            if (exit)
            {
                // A way out: shorter flights of tiers either side of its gate
                foreach (float s in new[] { -1f, 1f })
                    Flight(t, outward * rimOuter + dir * s * (sideLength / 2f - 1.1f), outward, faceIn, 3.6f);
                continue;
            }

            if (side == Vector2Int.up)
            {
                // The far side: a dark stone approach out to the great door, the sentinels and braziers along it
                Mats.Shape(PrimitiveType.Cube, t, outward * ((rimOuter + wallAt) / 2f) + Vector3.down * 0.1f,
                    new Vector3(sideLength * 1.15f, 0.2f, wallAt - rimOuter + 1f), approach, false, "OathApproach");
                PlaceSized(hallGate, t, outward * (wallAt - 1.6f), faceIn + HallGateYaw, 8.5f, false);
                OathLight(t, outward * (wallAt - 4f) + Vector3.up * 4f, new Color(0.75f, 0.85f, 1f), 12f, 2.4f);
                if (hallGuardian != null)
                    foreach (float s in new[] { -1f, 1f })
                        PlaceSized(hallGuardian, t, outward * (wallAt - 2.6f) + dir * s * 4.4f, faceIn + HallGuardianYaw, 5.2f, false);
                if (hallBrazier != null)
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Vector3 spot = outward * (rimOuter + 1.4f) + dir * s * 3.2f;
                        PlaceSized(hallBrazier, t, spot, faceIn, 1.5f, false);
                        Ambient.Flame(t, spot + Vector3.up * 1.55f, 0.6f);
                    }
                continue;
            }

            // Any other side: three flights of tiers climbing away from the rim, overlapping a little along it so the
            // run is unbroken from corner pillar to corner pillar
            for (int k = 0; k < 3; k++)
            {
                Vector3 along = Vector3.Lerp(from, to, (k + 0.5f) / 3f);
                Flight(t, along - outward * Vector3.Dot(along, outward) + outward * rimOuter, outward, faceIn, sideLength * 0.4f);
            }
        }
    }

    // One flight of tiers, `width` wide, its front step on the rim at `spot`, climbing away along `outward`
    private void Flight(Transform t, Vector3 spot, Vector3 outward, float faceIn, float width)
    {
        GameObject flight = PlaceWide(hallStairs, t, spot, faceIn + HallStairsYaw, width, out float front);
        // (turned to face the middle, its own +z, where the front step is, points back along -outward: pushed out by that
        // much so the front step sits on the rim)
        flight.transform.localPosition += outward * front;
    }

    // A prop scaled to a width (its own x, unturned), standing on the floor
    private static GameObject PlaceWide(GameObject prefab, Transform parent, Vector3 local, float yaw, float width) =>
        PlaceWide(prefab, parent, local, yaw, width, out _);

    // (front: how far its own +z face stands from its position, measured unturned and scaled)
    private static GameObject PlaceWide(GameObject prefab, Transform parent, Vector3 local, float yaw, float width, out float front)
    {
        GameObject go = Place(prefab, parent, local, 0f, 1f, false);
        Bounds b = BoundsAround(go);
        float s = width / Mathf.Max(0.01f, b.size.x);
        front = (b.center.z + b.extents.z) * s;
        go.transform.localScale *= s;
        go.transform.localPosition += Vector3.up * (-b.min.y * s);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return go;
    }

    // A prop stretched to a width (its own x, unturned), a height and a thickness (its own z), standing on the floor
    private static GameObject PlaceBox(GameObject prefab, Transform parent, Vector3 local, float yaw, float width, float height, float thickness)
    {
        GameObject go = Place(prefab, parent, local, 0f, 1f, false);
        Bounds b = BoundsAround(go);
        float sx = width / Mathf.Max(0.01f, b.size.x), sy = height / Mathf.Max(0.01f, b.size.y), sz = thickness / Mathf.Max(0.01f, b.size.z);
        Vector3 s = go.transform.localScale;
        go.transform.localScale = new Vector3(s.x * sx, s.y * sy, s.z * sz);
        go.transform.localPosition += Vector3.up * (-b.min.y * sy);
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return go;
    }

    private static void OathLight(Transform parent, Vector3 local, Color color, float range, float intensity)
    {
        var go = new GameObject("OathLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
    }

    // A run of rune-wall sections between two points, each section scaled to share the length evenly
    private void RuneWall(Transform parent, Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float span = d.magnitude;
        if (span < 0.5f)
            return;
        int n = Mathf.Max(1, Mathf.RoundToInt(span / 3.4f));
        for (int k = 0; k < n; k++)
        {
            // Measured unturned (its length runs along its own x), then scaled to its share and turned along the line
            GameObject wall = Place(warWall, parent, Vector3.Lerp(from, to, (k + 0.5f) / n), 0f, 1f, false);
            Bounds b = BoundsAround(wall);
            float s = span / n / Mathf.Max(0.01f, b.size.x);
            wall.transform.localScale *= s;
            wall.transform.localRotation = Quaternion.LookRotation(d.normalized) * Quaternion.Euler(0f, -90f, 0f);
            wall.transform.localPosition += Vector3.up * (-b.min.y * s);
        }
    }

    // A prop scaled to a height, standing on the floor
    private static GameObject PlaceSized(GameObject prefab, Transform parent, Vector3 local, float yaw, float height, bool solid)
    {
        GameObject go = Place(prefab, parent, local, yaw, 1f, solid);
        Bounds b = BoundsAround(go);
        float s = height / Mathf.Max(0.01f, b.size.y);
        go.transform.localScale *= s;
        go.transform.localPosition += Vector3.up * (-b.min.y * s);
        return go;
    }

    // A prop's renderer bounds measured from its own position (zones are never scaled, so world size is local size)
    private static Bounds BoundsAround(GameObject go)
    {
        var b = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        b.center -= go.transform.position;
        return b;
    }

    private void ArenaPillar(Transform parent, Vector3 local, float scale, bool lit)
    {
        float yaw = Quaternion.LookRotation(-local.normalized).eulerAngles.y;   // facing the middle
        float height;
        if (arenaPillarPrefab != null)
        {
            Place(arenaPillarPrefab, parent, local, yaw, scale);
            height = 4.4f * scale;
        }
        else
        {
            height = 4f * scale;
            Mats.Shape(PrimitiveType.Cube, parent, local + Vector3.up * height / 2f, new Vector3(1.2f, height, 1.2f) * scale, rockMat2, true, "ArenaPillar")
                .transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Mats.Shape(PrimitiveType.Cylinder, parent, local + Vector3.up * (height + 0.1f), new Vector3(1.5f, 0.15f, 1.5f) * scale, accentMat);
        }
        if (!lit)
            return;
        Ambient.Flame(parent, local + Vector3.up * (height + 0.05f) - local.normalized * 0.25f, 1.3f);
        var glow = new GameObject("PillarFire");
        glow.transform.SetParent(parent, false);
        glow.transform.localPosition = local + Vector3.up * (height + 0.4f) - local.normalized * 0.6f;
        var light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.62f, 0.3f);
        light.range = 10f;
        light.intensity = 2.4f;
        light.shadows = LightShadows.None;
        glow.AddComponent<FlickerLight>();
    }

    // Low wall modules along a straight stretch between two points
    private void ArenaWall(Transform parent, Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float span = d.magnitude;
        Quaternion rot = Quaternion.LookRotation(Vector3.Cross(Vector3.up, d.normalized));   // local x along the wall, z facing out
        if (arenaWallPrefab == null)
        {
            Mats.Shape(PrimitiveType.Cube, parent, (from + to) / 2f + Vector3.up * 0.55f, new Vector3(span, 1.1f, 0.7f), rockMat2, true, "ArenaWall")
                .transform.localRotation = rot;
            return;
        }
        int n = Mathf.Max(1, Mathf.RoundToInt(span / 3.6f));
        for (int k = 0; k < n; k++)
        {
            GameObject wall = Place(arenaWallPrefab, parent, Vector3.Lerp(from, to, (k + 0.5f) / n), 0f);
            wall.transform.localRotation = rot;
            Vector3 s = wall.transform.localScale;
            wall.transform.localScale = new Vector3(s.x * span / n, s.y, s.z);   // the prefab is one metre long
        }
    }

    private static void ChordCollider(Transform parent, Vector3 from, Vector3 to)
    {
        var wall = new GameObject("ArenaEdge");
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = (from + to) / 2f + Vector3.up * 2f;
        wall.transform.localRotation = Quaternion.LookRotation(to - from);
        wall.AddComponent<BoxCollider>().size = new Vector3(1f, 4f, Vector3.Distance(from, to) + 0.6f);
    }

    private static GameObject FlatMesh(Transform parent, string name, Mesh mesh, float y, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * y;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    // A flat ring (a disc when inner is 0) facing up, its UVs laid flat so a round texture fills the outer circle
    private static Mesh AnnulusMesh(float inner, float outer, int segments)
    {
        var vertices = new Vector3[(segments + 1) * 2];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices[i * 2] = dir * inner;
            vertices[i * 2 + 1] = dir * outer;
            uv[i * 2] = new Vector2(0.5f + dir.x * inner / (2f * outer), 0.5f + dir.z * inner / (2f * outer));
            uv[i * 2 + 1] = new Vector2(0.5f + dir.x * 0.5f, 0.5f + dir.z * 0.5f);
            if (i == segments)
                break;
            int v = i * 2, tri = i * 6;
            triangles[tri] = v; triangles[tri + 1] = v + 3; triangles[tri + 2] = v + 1;
            triangles[tri + 3] = v; triangles[tri + 4] = v + 2; triangles[tri + 5] = v + 3;
        }
        var mesh = new Mesh { vertices = vertices, uv = uv, triangles = triangles };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // An instance of a prop model; scenery outside the zone has no collision
    private static GameObject Place(GameObject prefab, Transform parent, Vector3 local, float yaw, float scale = 1f, bool solid = true)
    {
        GameObject go = Instantiate(prefab, parent);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale *= scale;
        if (!solid)
            foreach (Collider c in go.GetComponentsInChildren<Collider>())
                Destroy(c);
        return go;
    }

    // A fire brazier with real, flickering firelight
    private void Brazier(Transform parent, Vector3 local, bool solid = true)
    {
        if (brazierPrefab != null)
            Place(brazierPrefab, parent, local, Random.Range(0f, 360f), 1f, solid);
        else
            Mats.Shape(PrimitiveType.Cylinder, parent, local + Vector3.up * 0.5f, new Vector3(1f, 0.5f, 1f), rockMat2, solid, "Brazier");
        var glow = new GameObject("Firelight");
        glow.transform.SetParent(parent, false);
        glow.transform.localPosition = local + Vector3.up * 1.6f;
        var light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.62f, 0.28f);
        light.range = 7f;
        light.intensity = 2.2f;
        light.shadows = LightShadows.None;
        glow.AddComponent<FlickerLight>();
    }

    private static void Wall(Transform parent, Vector3 center, Vector3 along, float length)
    {
        var wall = new GameObject("EdgeWall");
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = center + Vector3.up * 2f;
        var box = wall.AddComponent<BoxCollider>();
        box.size = along * length + new Vector3(along.z, 0f, along.x) * 1f + Vector3.up * 4f;
    }

    private List<Vector2Int> zoneExits = new List<Vector2Int>();

    // A route room's VARCO centrepiece (Resources/Props), stood on the floor at this height in metres, facing yaw;
    // a simple stand-in when the model is missing
    private static GameObject RouteProp(string name, Transform parent, Vector3 local, float yaw, float height, Color stand)
    {
        GameObject prefab = Route.Prop(name);
        if (prefab != null)
        {
            GameObject go = Place(prefab, parent, local, yaw);
            go.transform.localScale = prefab.transform.localScale * height;
            return go;
        }
        return Mats.Shape(PrimitiveType.Cylinder, parent, local + Vector3.up * height * 0.25f, new Vector3(1.6f, height * 0.25f, 1.6f),
            Mats.Get("route_" + name, stand, 0.5f, stand * 0.4f), true, name);
    }

    // A wall with no way out, across from the first way in where it can be: the spot inset from it, and the yaw
    // looking back into the hall
    private Vector3 FarSide(Room room, float inset, out float faceIn)
    {
        Vector2Int entry = zoneExits.Count > 0 ? zoneExits[0] : Vector2Int.down;
        Vector2Int wall = -entry;
        if (zoneExits.Contains(wall))
            wall = Directions.Where(d => !zoneExits.Contains(d)).OrderBy(_ => Random.value).FirstOrDefault();
        var outward = new Vector3(wall.x, 0f, wall.y);
        float half = wall.y != 0 ? room.Size.y / 2f : room.Size.x / 2f;
        faceIn = Quaternion.LookRotation(-outward).eulerAngles.y;
        return outward * (half - inset);
    }

    // The route's themed halls (Asgard): the rune altar, the healing spring, the vault, and the waystone hall
    private void DecorateRoute(Room room, Room.Kind kind, int depth, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        switch (kind)
        {
            case Room.Kind.Supports when Route.Prop("RuneMonolith") != null:
                RuneSanctum(room, placed);
                break;
            case Room.Kind.Supports:
            {
                // The rune altar at the back; three free supports before it (one may be taken), and a stone to draw again
                Vector3 back = FarSide(room, 4.2f, out float face);
                RouteProp("RuneAltar", t, back, face, 3.2f, new Color(0.55f, 0.4f, 0.8f));
                placed.Add((back, 2.6f));
                Vector3 toward = -back.normalized;
                Vector3 side = Vector3.Cross(Vector3.up, toward);
                Vector3 row = back + toward * 4.4f;
                var offered = SkillSystem.EquippedIds();
                for (int i = 0; i < 3; i++)
                {
                    SkillDef def = SkillCatalog.Random(offered);
                    offered.Add(def.id);
                    Vector3 p = row + side * (i - 1) * 3.2f;
                    SkillOrb.Create(t, p, new SkillInstance(def), 0).altar = true;
                    placed.Add((p, 1.4f));
                }
                Vector3 stone = row + toward * 3.2f + side * 4.6f;
                SkillToken.Create(t, stone, SkillToken.Kind.AltarReroll, SkillSystem.AltarRerollPrice);
                placed.Add((stone, 1.2f));
                SoftLight(t, row + Vector3.up * 4f, new Color(0.75f, 0.55f, 1f), 10f, 2.2f);
                foreach (float s in new[] { -1f, 1f })
                    Brazier(t, back + side * s * 3.6f + toward * 0.6f);
                PalaceHall(room, placed, 0.5f);
                break;
            }
            case Room.Kind.Heal when Route.Prop("HealPool") != null:
                HealGrotto(room, placed);
                break;
            case Room.Kind.Vault when Route.Prop("GoldPile") != null:
                GoldHoard(room, placed);
                break;
            case Room.Kind.Vigor:
                LifeCrystalHall(room, placed);
                break;
            case Room.Kind.Ascend:
                AscensionTemple(room, placed);
                break;
            case Room.Kind.Fate:
                NornsTable(room, placed);
                break;
            case Room.Kind.Essence:
                EssenceTreasury(room, placed);
                break;
            case Room.Kind.Heal:
            {
                Vector3 mid = Vector3.zero;
                RouteProp("HealSpring", t, mid, Random.Range(0f, 360f), 2.2f, new Color(0.85f, 0.35f, 0.4f));
                HealSpring.Create(t, mid);
                placed.Add((mid, 3.2f));
                SoftLight(t, mid + Vector3.up * 4f, new Color(1f, 0.6f, 0.6f), 9f, 2f);
                PalaceHall(room, placed, 0.5f);
                break;
            }
            case Room.Kind.Vault:
            {
                Vector3 back = FarSide(room, 5f, out float face);
                VaultChest chest = VaultChest.Create(t, back, areaGold);
                RouteProp("VaultChest", chest.transform, Vector3.zero, 0f, 1.7f, new Color(0.6f, 0.42f, 0.2f)).transform.localRotation = Quaternion.Euler(0f, face, 0f);
                placed.Add((back, 2.8f));
                // the hoard spilling round it: the palace's own chests at its sides
                Vector3 side = Vector3.Cross(Vector3.up, -back.normalized);
                PlaceFree(treasureChestPrefab, room, placed, back + side * 3.4f, 1.1f, face + 25f);
                PlaceFree(treasureChestPrefab, room, placed, back - side * 3.4f, 1.1f, face - 25f);
                SoftLight(t, back + Vector3.up * 4f, new Color(1f, 0.85f, 0.5f), 9f, 2.4f);
                PalaceHall(room, placed, 0.5f);
                break;
            }
            case Room.Kind.Exit:
            {
                // The waystone at the far end, its Bifrost ring before it: stand in the ring, [C] for the route map
                Vector3 back = FarSide(room, 3.2f, out float face);
                RouteProp("Waystone", t, back, face, 5.2f, new Color(0.5f, 0.6f, 0.8f));
                placed.Add((back, 3f));
                Vector3 ring = back - back.normalized * 3.6f;
                RouteGate.Create(t, ring, face);
                placed.Add((ring, RouteGate.Reach + 0.5f));
                foreach (float s in new[] { -1f, 1f })
                    Brazier(t, back + Vector3.Cross(Vector3.up, back.normalized) * s * 4f);
                PalaceHall(room, placed, 0.4f);
                break;
            }
        }
    }

    // Realm scenery, gold ore and the zone's special objects
    private void Decorate(Room room, Room.Kind kind, int depth, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        if (hallTheme == HallTheme.CelestialGate)
        {
            CelestialStart(room, placed);
            return;
        }
        if (kind == Room.Kind.Supports || kind == Room.Kind.Heal || kind == Room.Kind.Vault || kind == Room.Kind.Exit
            || kind == Room.Kind.Vigor || kind == Room.Kind.Ascend || kind == Room.Kind.Fate || kind == Room.Kind.Essence)
        {
            DecorateRoute(room, kind, depth, placed);
            return;
        }
        // (a later area of the route begins at a waystone, not at Odin's high seat again)
        if (kind == Room.Kind.Start && areaStep > 0)
        {
            Mats.Shape(PrimitiveType.Cylinder, t, new Vector3(0f, 0.01f, 0f), new Vector3(4f, 0.01f, 4f),
                Mats.Get("start_circle", new Color(1f, 0.82f, 0.45f), 0.6f, new Color(0.9f, 0.65f, 0.25f)));
            // (arrived through it: the waystone stands on the wall away from the way on)
            Vector3 behind = FarSide(room, 3.2f, out float face);
            RouteProp("Waystone", t, behind, face, 4.2f, new Color(0.5f, 0.6f, 0.8f));
            placed.Add((behind, 3f));
            foreach (float side in new[] { -1f, 1f })
                Brazier(t, behind + Vector3.Cross(Vector3.up, behind.normalized) * side * 3.6f);
            Scenery(room, placed, 0.5f);
            return;
        }
        switch (kind)
        {
            case Room.Kind.Start:
                // A small sanctuary: Idunn's golden tree behind the rune circle, rune stones and braziers around it
                Mats.Shape(PrimitiveType.Cylinder, t, new Vector3(0f, 0.01f, 0f), new Vector3(4f, 0.01f, 4f),
                    Mats.Get("start_circle", new Color(1f, 0.82f, 0.45f), 0.6f, new Color(0.9f, 0.65f, 0.25f)));
                // In the palace the journey starts before Odin's empty high seat
                if (palace && Throne(room, placed))
                {
                    foreach (float side in new[] { -1f, 1f })
                        Brazier(t, new Vector3(side * 3f, 0f, 1.5f));
                    Scenery(room, placed, 0.5f);
                    break;
                }
                if (goldenTreePrefab != null)
                    Place(goldenTreePrefab, t, new Vector3(0f, 0f, 5f), Random.Range(0f, 360f));
                if (runeStonePrefab != null)
                    foreach (float side in new[] { -1f, 1f })
                        Place(runeStonePrefab, t, new Vector3(side * 5f, 0f, 3f), side * -25f + 180f);
                foreach (float side in new[] { -1f, 1f })
                    Brazier(t, new Vector3(side * 3f, 0f, 1.5f));
                placed.Add((new Vector3(0f, 0f, 3f), 6f));
                Scenery(room, placed, 0.5f);
                break;

            case Room.Kind.Treasure:
                // a free skill orb (runes are set aside for now)
                SkillOrb.Create(t, Vector3.zero, new SkillInstance(SkillCatalog.Random(SkillSystem.EquippedIds())), 0);
                if (palace)
                    Throne(room, placed);
                else if (runeStonePrefab != null)
                    foreach (float side in new[] { -1f, 1f })
                        Place(runeStonePrefab, t, new Vector3(side * 2.6f, 0f, 1f), 180f + side * -20f);
                Scenery(room, placed, 0.5f);
                if (!palace)
                    AddOre(room, placed, 3);
                break;

            case Room.Kind.Shop when palace && Route.Prop("BazaarStall") != null:
                PalaceBazaar(room, depth, placed);
                break;

            case Room.Kind.Shop when palace && shopCounterPrefab != null:
                PalaceShop(room, depth, placed);
                break;

            case Room.Kind.Forge when palace:
                PalaceForge(room, depth, placed);
                break;

            case Room.Kind.Forge:
                SkillForgeStation.Create(t, new Vector3(0f, 0f, 1f));
                RouteProp("ForgeAnvil", t, new Vector3(0f, 0f, 1f), 180f, 1.4f, new Color(0.3f, 0.28f, 0.3f));
                Mats.Shape(PrimitiveType.Cube, t, new Vector3(0f, 0.6f, -2.5f), new Vector3(3f, 1.2f, 1f), Mats.Get("shop_counter", new Color(0.45f, 0.3f, 0.18f)), true, "Counter");
                foreach (float side in new[] { -1f, 1f })
                    Brazier(t, new Vector3(side * 2.6f, 0f, -2.5f));
                Scenery(room, placed, 0.5f);
                break;

            case Room.Kind.Shop:
            {
                var offered = SkillSystem.EquippedIds();
                for (int i = 0; i < 3; i++)
                {
                    SkillDef def = SkillCatalog.Random(offered);
                    offered.Add(def.id);
                    SkillOrb.Create(t, new Vector3((i - 1) * 3.5f, 0f, 1.5f), new SkillInstance(def), 45 + depth * 5);
                }
                SkillToken.Create(t, new Vector3(0f, 0f, 4f), SkillToken.Kind.Reroll, SkillSystem.RerollPrice);
                SkillToken.Create(t, new Vector3(-4f, 0f, 4f), SkillToken.Kind.ShopReroll, 0);
                Mats.Shape(PrimitiveType.Cube, t, new Vector3(0f, 0.6f, -2.5f), new Vector3(3f, 1.2f, 1f), Mats.Get("shop_counter", new Color(0.45f, 0.3f, 0.18f)), true, "Counter");
                foreach (float side in new[] { -1f, 1f })
                    Brazier(t, new Vector3(side * 2.6f, 0f, -2.5f));
                Scenery(room, placed, 0.5f);
                break;
            }

            case Room.Kind.Boss:
                // The arena itself is built with the zone (BuildArena): its floor is left clear for the fight.
                // Niflheim's is Nidhogg's lair at the spring Hvergelmir.
                if (realmIndex == Realm.StyleNiflheim)
                    NidhoggLair(room, placed);
                break;

            case Room.Kind.MidBoss:
                // (the battleground is built with the zone, its floor left clear for the fight)
                if (realmIndex == Realm.StyleNiflheim && !palace)
                    GiantsArena(room, placed);
                else if (!palace)
                    Lair(room, placed);
                else if (!WarArena)
                    OathHall(room, placed);
                break;

            case Room.Kind.Combat:
                if (warHall)   // (Týr's battleground keeps its floor clear)
                    break;
                Scenery(room, placed, 1f);
                if (!palace)   // no rough ore veins on the palace's marble
                    AddOre(room, placed, Random.Range(3, 7));
                break;
        }
    }

    // ───────────── Niflheim's boss maps ─────────────

    private static bool Blocked(List<(Vector3 p, float r)> placed, Vector3 p, float radius) =>
        placed.Any(o => Vector3.Distance(o.p, p) < o.r + radius);

    // Niflheim's giants hold a round arena of their own, built like the guardian's (VARCO pillars, walls and rune floor)
    private bool GiantsArenaZone(Room.Kind kind) => kind == Room.Kind.MidBoss && realmIndex == Realm.StyleNiflheim && !palace;

    // The frost giants' arena (the mid-boss): the round rune arena gone over to frost - a band of snow round its rim,
    // rime creeping in over the floor, faceted ice crystals growing between the pillars, the two giants' high seats
    // facing each other across it (frosted, the mist giant's pale and the frost giant's blue) and braziers of cold fire
    private void GiantsArena(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float r = room.Radius > 0f ? room.Radius : room.Size.x / 2f;
        Material snow = Mats.Get("garena_snow", new Color(0.82f, 0.88f, 0.95f), 0.45f, new Color(0.04f, 0.07f, 0.1f));
        Material rime = Mats.Get("garena_rime", new Color(0.62f, 0.8f, 0.95f), 0.85f, new Color(0.04f, 0.12f, 0.2f));
        Material ice = Mats.Get("garena_crystal", new Color(0.55f, 0.85f, 1f), 0.95f, new Color(0.12f, 0.45f, 0.75f));
        Material deepIce = Mats.Get("garena_crystal_deep", new Color(0.3f, 0.55f, 0.9f), 0.95f, new Color(0.08f, 0.25f, 0.6f));

        // snow drifted against the rim, and a ring of rime creeping in over the rune floor (smooth, many-sided rings)
        FlatMesh(t, "SnowRim", AnnulusMesh(r - 2.2f, r + 0.6f, 128), 0.022f, snow);
        FlatMesh(t, "RimeRing", AnnulusMesh(r - 4.2f, r - 2.2f, 128), 0.02f, rime);

        // faceted ice crystals between the pillars, leaving the ways in clear
        for (int i = 0; i < ArenaPillars; i++)
        {
            float a = i * Mathf.PI * 2f / ArenaPillars + Random.Range(-0.06f, 0.06f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r - 1.6f);
            if (Blocked(placed, p, 1.6f))
                continue;
            IceCluster(t, p, -a * Mathf.Rad2Deg, i % 3 == 0 ? deepIce : ice, Random.Range(2.6f, 4.2f), true);
            placed.Add((p, 1.8f));
        }
        // and a few small ones out on the floor
        for (int i = 0; i < 6; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(r * 0.45f, r - 4.5f);
            if (Blocked(placed, p, 1.2f))
                continue;
            IceCluster(t, p, Random.Range(0f, 360f), ice, Random.Range(0.9f, 1.5f), true);
            placed.Add((p, 1.2f));
        }

        // the giants' high seats, frosted over, facing each other across the arena
        var mistSeat = new Vector3(-(r - 4.2f), 0f, 0f);
        var frostSeat = new Vector3(r - 4.2f, 0f, 0f);
        GiantSeat(t, mistSeat, new Color(0.78f, 0.8f, 0.9f), Mats.Get("throne_mist", new Color(0.52f, 0.56f, 0.7f), 0.3f), ice);
        GiantSeat(t, frostSeat, new Color(0.55f, 0.72f, 1f), Mats.Get("throne_frost", new Color(0.4f, 0.5f, 0.62f), 0.3f), deepIce);
        placed.Add((mistSeat, 3f));
        placed.Add((frostSeat, 3f));

        // braziers of cold blue fire on the diagonals
        foreach (Vector3 dir in new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f) })
        {
            Vector3 c = dir.normalized * (r - 5f);
            if (Blocked(placed, c, 1f))
                continue;
            ColdFire(t, c);
            placed.Add((c, 1.3f));
        }
    }

    // ── Élivágar, the primordial ice-river where the frost giants were born: the arena floor frozen over as the river,
    // a darker channel of deep ice down its middle split with cracks; the ribs of Ymir, the first giant, rising from
    // beyond the wall and curving in high over the field; two colossal frozen giants half sunk in the ice beyond the
    // north rim; broken rune pillars round the edge; and glacier spires standing away into the fog ──
    private void Elivagar(Room room, List<(Vector3 p, float r)> placed, float r, Material ice, Material deepIce)
    {
        Transform t = room.transform;
        Material riverIce = Mats.Get("elivagar_ice", new Color(0.66f, 0.82f, 0.95f), 0.92f, new Color(0.04f, 0.1f, 0.18f));
        Material channel = Mats.Get("elivagar_channel", new Color(0.24f, 0.42f, 0.66f), 0.97f, new Color(0.03f, 0.1f, 0.22f));
        Material crack = Mats.Get("elivagar_crack", new Color(0.88f, 0.96f, 1f), 0.6f, new Color(0.3f, 0.55f, 0.75f));
        Material bone = Mats.Get("ymir_bone", new Color(0.82f, 0.79f, 0.7f), 0.25f, new Color(0.03f, 0.04f, 0.06f));
        Material runeStone = Mats.Get("elivagar_stone", new Color(0.3f, 0.33f, 0.4f), 0.3f);
        Material runeGlow = Mats.Get("elivagar_rune", new Color(0.5f, 0.85f, 1f), 0.5f, new Color(0.5f, 1.4f, 2.2f));

        // the frozen river over the whole floor, and its deep channel running north to south through the middle
        FlatMesh(t, "ElivagarIce", AnnulusMesh(0f, r - 2f, 128), 0.017f, riverIce);
        float half = r * 0.32f, len = Mathf.Sqrt(Mathf.Max(1f, (r - 2.4f) * (r - 2.4f) - half * half)) * 2f;
        Mats.Shape(PrimitiveType.Cube, t, new Vector3(0f, 0.019f, 0f), new Vector3(half * 2f, 0.004f, len), channel, false, "ElivagarChannel");
        // cracks splitting the ice, branching
        for (int i = 0; i < 22; i++)
        {
            var p = new Vector3(Random.Range(-r * 0.8f, r * 0.8f), 0.022f, Random.Range(-r * 0.8f, r * 0.8f));
            if (p.magnitude > r - 3.5f)
                continue;
            float yaw = Random.Range(0f, 180f), l = Random.Range(1.5f, 4.5f);
            Mats.Shape(PrimitiveType.Cube, t, p, new Vector3(0.07f, 0.003f, l), crack).transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Mats.Shape(PrimitiveType.Cube, t, p + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * l * 0.4f, new Vector3(0.05f, 0.003f, l * 0.5f), crack)
                .transform.localRotation = Quaternion.Euler(0f, yaw + Random.Range(25f, 50f) * (Random.value < 0.5f ? -1f : 1f), 0f);
        }

        // the ribs of Ymir: great curved bones rising from beyond the wall on the east and west, arching in overhead
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Lerp(-0.62f, 0.62f, i / 3f);                       // spread along the side
                Vector3 outward = new Vector3(side * Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 foot = outward * (r + 4f);
                Vector3 peak = outward * (r * 0.42f) + Vector3.up * 19f;          // high over the field, above the follow camera
                Vector3 bend = outward * (r + 2f) + Vector3.up * 13f;
                Rib(t, foot, bend, peak, 1.9f - Mathf.Abs(i - 1.5f) * 0.15f, bone);
            }

        // two colossal giants frozen into the ice beyond the north rim, sunk to the chest, facing the field
        var frozen = new GameObject("FrozenGiants");
        frozen.transform.SetParent(t, false);
        frozen.SetActive(false);   // (built inactive, so the borrowed models' enemy scripts never wake)
        int n = 0;
        foreach (string path in new[] { "Bosses/FrostGiant", "Bosses/MistGiant" })
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
                continue;
            float x = n++ == 0 ? -r * 0.55f : r * 0.55f;
            var at = new Vector3(x, -4.5f, r + 4.5f);   // just past the wall, before the haze swallows it
            GameObject statue = Instantiate(prefab, frozen.transform);
            foreach (var mb in statue.GetComponentsInChildren<MonoBehaviour>(true))
                DestroyImmediate(mb);
            foreach (var cc in statue.GetComponentsInChildren<CharacterController>(true))
                DestroyImmediate(cc);
            foreach (var an in statue.GetComponentsInChildren<Animator>(true))
                DestroyImmediate(an);
            statue.transform.localPosition = at;
            statue.transform.localRotation = Quaternion.LookRotation(new Vector3(-x * 0.2f, 0f, -1f));
            statue.transform.localScale = Vector3.one * 7f;
            var block = new MaterialPropertyBlock();
            foreach (Renderer rd in statue.GetComponentsInChildren<Renderer>(true))
            {
                rd.GetPropertyBlock(block);
                block.SetColor("_BaseColor", new Color(0.58f, 0.66f, 0.78f));   // grey-blue: stone gone to ice
                rd.SetPropertyBlock(block);
            }
            // ice heaped round where it sinks in
            for (int k = 0; k < 4; k++)
                IceCluster(t, new Vector3(x + Random.Range(-5f, 5f), 0f, r + 3.5f + Random.Range(0f, 3f)), Random.Range(0f, 360f), k % 2 == 0 ? ice : deepIce, Random.Range(3f, 5.5f), false);
        }
        frozen.SetActive(true);

        // broken rune pillars round the edge of the field, some fallen
        for (int i = 0; i < 8; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2f / 8f + Random.Range(-0.1f, 0.1f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r - 5.5f);
            if (Blocked(placed, p, 1.6f))
                continue;
            var pillar = new GameObject("RunePillar");
            pillar.transform.SetParent(t, false);
            pillar.transform.localPosition = p;
            pillar.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            bool fallen = i % 3 == 1;
            float h = fallen ? 1f : Random.Range(2.4f, 4.2f);
            if (fallen)
            {
                Mats.Shape(PrimitiveType.Cube, pillar.transform, new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 3.2f), runeStone, true, "FallenPillar");
                Mats.Shape(PrimitiveType.Cube, pillar.transform, new Vector3(0f, 0.92f, 0.4f), new Vector3(0.35f, 0.04f, 0.5f), runeGlow);
            }
            else
            {
                Mats.Shape(PrimitiveType.Cube, pillar.transform, new Vector3(0f, h / 2f, 0f), new Vector3(1f, h, 1f), runeStone, true, "Pillar");
                Mats.Shape(PrimitiveType.Cube, pillar.transform, new Vector3(0.1f, h + 0.25f, 0f), new Vector3(0.8f, 0.5f, 0.8f), runeStone)
                    .transform.localRotation = Quaternion.Euler(0f, 0f, 18f);   // the broken top, slipped
                for (int k = 0; k < 3; k++)
                    Mats.Shape(PrimitiveType.Cube, pillar.transform, new Vector3(0f, 0.6f + k * h * 0.28f, 0.51f), new Vector3(0.38f, 0.32f, 0.02f), runeGlow);
            }
            placed.Add((p, 1.8f));
        }

        // glacier spires standing away into the fog all round
        for (int i = 0; i < 14; i++)
        {
            float a = i * Mathf.PI * 2f / 14f + Random.Range(-0.12f, 0.12f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r + Random.Range(9f, 16f));
            Crystal6(t, p, Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 60f), Random.Range(-8f, 8f)), Random.Range(3f, 5.5f), Random.Range(10f, 20f), i % 3 == 0 ? deepIce : ice);
        }
    }

    // One of Ymir's ribs: a bone tapering along a curve from its foot through `bend` to its tip, built of short segments
    private static void Rib(Transform parent, Vector3 foot, Vector3 bend, Vector3 tip, float thick, Material bone)
    {
        const int segments = 9;
        Vector3 Point(float u) => (1f - u) * (1f - u) * foot + 2f * (1f - u) * u * bend + u * u * tip;
        for (int s = 0; s < segments; s++)
        {
            Vector3 a = Point(s / (float)segments), b = Point((s + 1f) / segments);
            float w = Mathf.Lerp(thick, thick * 0.45f, s / (float)segments);
            GameObject seg = Mats.Shape(PrimitiveType.Cylinder, parent, (a + b) / 2f, new Vector3(w, Vector3.Distance(a, b) / 2f + 0.15f, w), bone, s < 2, "YmirRib");
            seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            seg.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    // A giant's high seat: the VARCO throne, frosted and tinted for its giant, with ice growing over its back
    private void GiantSeat(Transform parent, Vector3 at, Color tint, Material fallbackStone, Material ice)
    {
        float yaw = Quaternion.LookRotation(-at.normalized).eulerAngles.y;   // facing the middle
        if (thronePrefab != null)
        {
            GameObject seat = Place(thronePrefab, parent, at, yaw, 1.35f);
            var block = new MaterialPropertyBlock();
            foreach (Renderer rd in seat.GetComponentsInChildren<Renderer>())
            {
                rd.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                rd.SetPropertyBlock(block);
            }
        }
        else
            GiantThrone(parent, at, fallbackStone, ice);
        // ice crusting the back of the seat
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        IceCluster(parent, at + turn * new Vector3(0f, 0f, -1.6f), yaw, ice, 2.2f, false);
    }

    // A cluster of faceted ice crystals: one tall spire and smaller ones leaning out round it
    private static void IceCluster(Transform parent, Vector3 at, float yaw, Material mat, float height, bool solid)
    {
        var root = new GameObject("IceCluster");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = at;
        root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Crystal6(root.transform, Vector3.zero, Quaternion.Euler(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f)), 0.45f * height / 3f + 0.15f, height, mat);
        int n = Random.Range(3, 6);
        for (int i = 0; i < n; i++)
        {
            float a = i * 360f / n + Random.Range(-20f, 20f);
            Vector3 off = Quaternion.Euler(0f, a, 0f) * Vector3.forward * Random.Range(0.35f, 0.8f) * (height / 3f + 0.3f);
            float h = height * Random.Range(0.3f, 0.65f);
            Crystal6(root.transform, off, Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(Random.Range(15f, 35f), 0f, 0f), 0.25f * height / 3f + 0.08f, h, mat);
        }
        if (solid)
        {
            var col = root.AddComponent<CapsuleCollider>();
            col.center = Vector3.up * height * 0.4f;
            col.height = height * 0.8f;
            col.radius = 0.25f * height / 3f + 0.45f;
        }
    }

    // One six-sided crystal with a pointed tip (a real mesh, so its facets catch the light); also the giants' icicles
    public static GameObject Crystal6(Transform parent, Vector3 local, Quaternion tilt, float radius, float height, Material mat)
    {
        var go = new GameObject("Crystal");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = tilt;
        go.AddComponent<MeshFilter>().sharedMesh = CrystalMesh();
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.transform.localScale = new Vector3(radius, height, radius);
        return go;
    }

    private static Mesh crystalMesh;

    // A unit crystal: a hexagonal prism (radius 1, sides to 0.75) rising to a point at 1, flat-shaded
    private static Mesh CrystalMesh()
    {
        if (crystalMesh != null)
            return crystalMesh;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        const int sides = 6;
        const float shoulder = 0.75f;
        Vector3 Ring(int i, float y, float k) { float a = i * Mathf.PI * 2f / sides; return new Vector3(Mathf.Cos(a) * k, y, Mathf.Sin(a) * k); }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int s = verts.Count; verts.AddRange(new[] { a, b, c, d }); tris.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 }); }
        void Tri(Vector3 a, Vector3 b, Vector3 c) { int s = verts.Count; verts.AddRange(new[] { a, b, c }); tris.AddRange(new[] { s, s + 2, s + 1 }); }
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            Quad(Ring(i, 0f, 1f), Ring(j, 0f, 1f), Ring(j, shoulder, 0.85f), Ring(i, shoulder, 0.85f));   // the side
            Tri(Ring(i, shoulder, 0.85f), Ring(j, shoulder, 0.85f), Vector3.up);                        // the tip
            Tri(Ring(j, 0f, 1f), Ring(i, 0f, 1f), Vector3.zero);                                         // the base
        }
        crystalMesh = new Mesh { name = "Crystal6" };
        crystalMesh.SetVertices(verts);
        crystalMesh.SetTriangles(tris, 0);
        crystalMesh.RecalculateNormals();
        crystalMesh.RecalculateBounds();
        return crystalMesh;
    }

    // A giant's throne of frost-split stone, carved for something three times a man's height
    private static void GiantThrone(Transform parent, Vector3 at, Material stone, Material crown)
    {
        var root = new GameObject("GiantThrone");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = at;
        root.transform.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Sign(at.x), 0f, 0f));   // facing the middle
        Transform r = root.transform;
        Mats.Shape(PrimitiveType.Cube, r, new Vector3(0f, 0.2f, 0f), new Vector3(3.6f, 0.4f, 3f), stone, true, "Dais");
        Mats.Shape(PrimitiveType.Cube, r, new Vector3(0f, 0.95f, -0.1f), new Vector3(2.6f, 1.1f, 2f), stone, true, "Seat");
        Mats.Shape(PrimitiveType.Cube, r, new Vector3(0f, 2.8f, -1.15f), new Vector3(2.8f, 4.6f, 0.7f), stone, true, "Back");
        foreach (float side in new[] { -1f, 1f })
        {
            Mats.Shape(PrimitiveType.Cube, r, new Vector3(side * 1.45f, 1.75f, -0.1f), new Vector3(0.5f, 0.7f, 2f), stone, true, "Arm");
            Mats.Shape(PrimitiveType.Cube, r, new Vector3(side * 1.1f, 5.4f, -1.15f), new Vector3(0.4f, 1.4f, 0.4f), crown)
                .transform.localRotation = Quaternion.Euler(0f, 0f, side * -18f);
        }
        Mats.Shape(PrimitiveType.Cube, r, new Vector3(0f, 5.7f, -1.15f), new Vector3(0.5f, 1.8f, 0.5f), crown);
    }

    // A brazier of pale blue fire with its cold light (the VARCO brazier when it is in the project)
    private void ColdFire(Transform parent, Vector3 at)
    {
        Material bowl = Mats.Get("coldfire_bowl", new Color(0.28f, 0.32f, 0.4f), 0.3f);
        Material flame = Mats.Get("coldfire_flame", new Color(0.6f, 0.9f, 1f), 0.5f, new Color(0.6f, 1.6f, 2.4f));
        if (brazierPrefab != null)
        {
            GameObject b = Place(brazierPrefab, parent, at, Random.Range(0f, 360f));
            var block = new MaterialPropertyBlock();
            foreach (Renderer rd in b.GetComponentsInChildren<Renderer>())
            {
                rd.GetPropertyBlock(block);
                block.SetColor("_BaseColor", new Color(0.7f, 0.85f, 1f));
                rd.SetPropertyBlock(block);
            }
        }
        else
        {
            Mats.Shape(PrimitiveType.Cylinder, parent, at + Vector3.up * 0.5f, new Vector3(0.5f, 0.5f, 0.5f), bowl, true, "ColdFireStand");
            Mats.Shape(PrimitiveType.Cylinder, parent, at + Vector3.up * 1.05f, new Vector3(1.2f, 0.12f, 1.2f), bowl);
        }
        // the cold flame: a few faceted crystals of blue fire
        for (int i = 0; i < 3; i++)
            Crystal6(parent, at + new Vector3(Random.Range(-0.15f, 0.15f), 1.15f, Random.Range(-0.15f, 0.15f)),
                Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 60f), Random.Range(-12f, 12f)), 0.18f - i * 0.03f, 0.7f - i * 0.15f, flame);
        var glow = new GameObject("ColdFirelight");
        glow.transform.SetParent(parent, false);
        glow.transform.localPosition = at + Vector3.up * 1.9f;
        var light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.5f, 0.85f, 1f);
        light.range = 8f;
        light.intensity = 2f;
        light.shadows = LightShadows.None;
        glow.AddComponent<FlickerLight>();
    }

    // Nidhogg's lair at the spring Hvergelmir, inside the round arena: a great root of the world tree arching over
    // the black water, a gnawed root lying by the shore, the bones of what it has eaten, and ice growing round the wall
    private void NidhoggLair(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float r = room.Radius > 0f ? room.Radius : room.Size.x / 2f;
        Material bark = Mats.Get("nifl_root", new Color(0.22f, 0.19f, 0.22f), 0.15f);
        Material rime = Mats.Get("nifl_rime", new Color(0.7f, 0.8f, 0.92f), 0.6f);
        Material water = Mats.Get("hvergelmir_water", new Color(0.03f, 0.06f, 0.12f), 0.98f, new Color(0.02f, 0.08f, 0.16f));
        Material shore = Mats.Get("hvergelmir_shore", new Color(0.62f, 0.72f, 0.84f), 0.6f, new Color(0.05f, 0.18f, 0.3f));
        Material bone = Mats.Get("lair_bone", new Color(0.78f, 0.75f, 0.66f), 0.25f);
        Material snow = Mats.Get("lair_snow", new Color(0.8f, 0.86f, 0.94f), 0.45f, new Color(0.03f, 0.06f, 0.1f));
        Material frostFloor = Mats.Get("lair_rime", new Color(0.5f, 0.66f, 0.85f), 0.9f, new Color(0.03f, 0.1f, 0.2f));
        Material ice = Mats.Get("lair_crystal", new Color(0.55f, 0.85f, 1f), 0.95f, new Color(0.12f, 0.45f, 0.75f));
        Material deepIce = Mats.Get("lair_crystal_deep", new Color(0.25f, 0.45f, 0.85f), 0.95f, new Color(0.06f, 0.2f, 0.55f));
        Material stone = Mats.Get("lair_blackstone", new Color(0.12f, 0.13f, 0.17f), 0.35f);

        // the floor in four rings of carved stone blocks, from the middle out: A rune flagstones, B root-veined bedrock,
        // C Náströnd's bone mosaic, D frost dragon scales up to the wall. Each ring is laid as courses of curved blocks
        // with bevelled edges and gaps between them, and the rings step down from the raised middle to the rim, a band
        // of gold set where each meets the next. The blocks are solid, so everything stands on them.
        float[] edges = { 0f, r * 0.27f, r * 0.56f, r * 0.77f, r + 0.6f };
        string[] tiles = { "NidFloor_A_Runes", "NidFloor_B_Roots", "NidFloor_C_Bones", "NidFloor_D_Scales" };
        float[] metres = { 2.5f, 3.5f, 3f, 1.4f };        // how wide one repeat of each texture lies on the floor
        float[] tops = { 0.13f, 0.10f, 0.07f, 0.045f };    // each ring a step lower than the one inside it
        float[] blockLen = { 1.5f, 2.2f, 2f, 1.6f };       // about how long a block is along its course
        // a dark bed under the blocks, so the gaps between them read deep
        FlatMesh(t, "LairFloorBed", AnnulusMesh(0f, r + 0.6f, 128), 0.015f, Mats.Get("lair_bed", new Color(0.03f, 0.035f, 0.05f), 0.1f));
        for (int i = 0; i < 4; i++)
        {
            var tex = Resources.Load<Texture2D>("Textures/" + tiles[i]);
            var mat = new Material(Mats.Get("lair_ring_base", Color.white, i == 3 ? 0.75f : 0.4f)) { name = "lair_ring_" + i };
            mat.SetColor("_BaseColor", Color.white);
            if (tex != null)
            {
                tex.wrapMode = TextureWrapMode.Repeat;
                mat.SetTexture("_BaseMap", tex);
                if (i == 0)
                {
                    mat.EnableKeyword("_EMISSION");   // the runes glow
                    mat.SetTexture("_EmissionMap", tex);
                    mat.SetColor("_EmissionColor", new Color(0.45f, 0.45f, 0.5f));
                }
            }
            var bump = Resources.Load<Texture2D>("Textures/" + tiles[i] + "_N");
            if (bump != null)
            {
                mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_BumpMap", bump);
                mat.SetFloat("_BumpScale", 1.4f);
            }
            // two courses per ring (the middle of A a single round cap), each course offset half a block like brickwork
            var courses = new List<(float inner, float outer)>();
            float mid = (edges[i] + edges[i + 1]) / 2f;
            if (i == 0)
            {
                courses.Add((0f, 1.1f));
                courses.Add((1.1f, edges[1]));
            }
            else
            {
                courses.Add((edges[i], mid));
                courses.Add((mid, edges[i + 1]));
            }
            var mb = new BlockMesh(1f / metres[i]);
            for (int c = 0; c < courses.Count; c++)
            {
                var (inner, outer) = courses[c];
                if (inner < 0.01f)
                {
                    mb.Sector(0f, outer - 0.04f, 0f, Mathf.PI * 2f, tops[i], 0.05f);   // the round cap
                    continue;
                }
                int count = Mathf.Max(6, Mathf.RoundToInt(Mathf.PI * (inner + outer) / blockLen[i]));
                float step = Mathf.PI * 2f / count, shift = c % 2 == 0 ? 0f : step * 0.5f;
                for (int b = 0; b < count; b++)
                {
                    float gapA = 0.035f / ((inner + outer) / 2f);   // a 7 cm joint between blocks
                    float a0 = shift + b * step + gapA, a1 = shift + (b + 1) * step - gapA;
                    float h = tops[i] - Random.Range(0f, 0.012f);   // laid by hand: not quite level
                    mb.Sector(inner + 0.035f, outer - 0.035f, a0, a1, h, 0.04f);
                }
            }
            GameObject ring = FlatMesh(t, "LairFloor_" + "ABCD"[i], mb.Build(), 0f, mat);
            ring.AddComponent<MeshCollider>().sharedMesh = ring.GetComponent<MeshFilter>().sharedMesh;
        }
        // the gold bands where the rings meet, standing a little proud of the higher ring
        Material gold = Mats.Get("lair_gold", new Color(0.85f, 0.66f, 0.28f), 0.8f, new Color(0.22f, 0.15f, 0.04f));
        var goldMesh = new BlockMesh(1f);
        for (int i = 1; i < 4; i++)
            goldMesh.Sector(edges[i] - 0.09f, edges[i] + 0.09f, 0f, Mathf.PI * 2f, tops[i - 1] + 0.025f, 0.03f);
        GameObject bands = FlatMesh(t, "LairGoldBands", goldMesh.Build(), 0f, gold);
        bands.AddComponent<MeshCollider>().sharedMesh = bands.GetComponent<MeshFilter>().sharedMesh;

        for (int i = 0; i < 6; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(edges[1] + 1f, edges[2] - 1f);
            if (Blocked(placed, p, 1.4f))
                continue;
            float yaw = Random.Range(0f, 360f);
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            // a root arching up out of the rock and back down: two leaning limbs and a frosted crown
            foreach (float side in new[] { -1f, 1f })
                Mats.Shape(PrimitiveType.Cylinder, t, p + turn * new Vector3(side * 0.7f, 0.25f, 0f), new Vector3(0.38f, 0.8f, 0.38f), bark)
                    .transform.localRotation = turn * Quaternion.Euler(0f, 0f, side * 62f);
            Mats.Shape(PrimitiveType.Cube, t, p + Vector3.up * 0.55f, new Vector3(0.5f, 0.06f, 0.3f), rime).transform.localRotation = turn;
            placed.Add((p, 1.2f));
        }
        for (int i = 0; i < 6; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(edges[2] + 0.8f, edges[3] - 0.8f);
            if (Blocked(placed, p, 1.2f))
                continue;
            float yaw = Random.Range(0f, 360f);
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            // a long bone lying half sunk, a skull beside it, a couple of ribs sticking up
            Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.up * 0.08f, new Vector3(0.18f, 0.8f, 0.18f), bone).transform.localRotation = turn * Quaternion.Euler(90f, 0f, 0f);
            Mats.Shape(PrimitiveType.Sphere, t, p + turn * new Vector3(0.7f, 0.12f, 0.9f), new Vector3(0.45f, 0.38f, 0.55f), bone);
            for (int k = 0; k < 2; k++)
                Mats.Shape(PrimitiveType.Cylinder, t, p + turn * new Vector3(-0.4f + k * 0.35f, 0.25f, -0.5f), new Vector3(0.07f, 0.32f, 0.07f), bone)
                    .transform.localRotation = turn * Quaternion.Euler(0f, 0f, 28f);
            placed.Add((p, 1.2f));
        }

        // the spring: black water at the back of the lair, a frozen shore around it, fog rolling off it
        var spring = new Vector3(0f, 0f, r * 0.58f);
        Mats.Shape(PrimitiveType.Cylinder, t, spring + Vector3.up * 0.16f, new Vector3(14f, 0.008f, 9f), shore);   // (over the carved floor)
        Mats.Shape(PrimitiveType.Cylinder, t, spring + Vector3.up * 0.17f, new Vector3(12f, 0.008f, 7.5f), water);
        // ice shelves breaking off into the water, and crystals standing along the shore
        for (int i = 0; i < 7; i++)
        {
            float a = Mathf.PI * (0.05f + i * 0.15f);
            var p = spring + new Vector3(Mathf.Cos(a) * 6.6f, 0f, Mathf.Sin(a) * 4.4f * (i % 2 == 0 ? 1f : -1f));
            Mats.Shape(PrimitiveType.Cube, t, p + Vector3.up * 0.16f, new Vector3(Random.Range(1.2f, 2.2f), 0.12f, Random.Range(0.8f, 1.4f)), frostFloor)
                .transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), Random.Range(-4f, 4f));
            if (i % 2 == 0)
                IceCluster(t, p, Random.Range(0f, 360f), i % 4 == 0 ? deepIce : ice, Random.Range(1.2f, 2f), false);
        }
        var fog = new GameObject("HvergelmirFog");
        fog.transform.SetParent(t, false);
        fog.transform.localPosition = spring;
        fog.AddComponent<SpringMist>().Init(11f, 6f);
        foreach (float side in new[] { -1f, 1f })
        {
            var glow = new GameObject("SpringGlow");
            glow.transform.SetParent(t, false);
            glow.transform.localPosition = spring + new Vector3(side * 3.5f, 1.5f, 0f);
            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.4f, 0.75f, 1f);
            light.range = 10f;
            light.intensity = 1.8f;
            light.shadows = LightShadows.None;
        }
        placed.Add((spring, 6.5f));

        // the great root of Yggdrasil arching over the spring, its feet beyond the arena wall
        GreatRoot(t, new Vector3(0f, 0f, r * 0.85f), 0f, r * 1.05f, 2.4f, 6.5f, bark, rime);
        // two more crossing the sides, unless a way in opens there
        foreach (float side in new[] { -1f, 1f })
        {
            var at = new Vector3(side * (r - 2.5f), 0f, 1.5f);
            if (!Blocked(placed, new Vector3(at.x, 0f, 0f), 1f))
                GreatRoot(t, at, 90f, r * 0.7f, 1.6f, 3.5f, bark, rime);
        }

        // a root lying by the shore, half gnawed through
        var gnawed = spring + new Vector3(-8f, 0f, -3.5f);
        if (!Blocked(placed, gnawed, 2f))
        {
            Mats.Shape(PrimitiveType.Cylinder, t, gnawed + Vector3.up * 0.8f, new Vector3(1.6f, 3.2f, 1.6f), bark, true, "GnawedRoot")
                .transform.localRotation = Quaternion.Euler(0f, 25f, 90f);
            for (int i = 0; i < 4; i++)
                Mats.Shape(PrimitiveType.Cube, t, gnawed + Quaternion.Euler(0f, 25f, 0f) * new Vector3(Random.Range(-1.5f, 1.5f), 1.5f, 0f), new Vector3(0.6f, 0.35f, 0.9f),
                    Mats.Get("gnaw_mark", new Color(0.55f, 0.45f, 0.4f), 0.2f));
            placed.Add((gnawed, 3.5f));
        }

        // bones of the dead: rib cages and skulls strewn round the edges
        for (int n = 0; n < 4; n++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r - 5.5f);
            if (Blocked(placed, p, 2.5f))
                continue;
            RibCage(t, p, Random.Range(0f, 360f), bone);
            placed.Add((p, 2.5f));
        }

        // the ice sentinels: four frost-grown obelisks on the diagonals that wake and attack while the dragon is fought
        foreach (float deg in new[] { 45f, 135f, 225f, 315f })
        {
            float a = deg * Mathf.Deg2Rad;
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r * 0.66f);
            if (Blocked(placed, p, 1.6f))
                p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r * 0.5f);
            IceSentinel.Build(t, p, room, ice, deepIce, stone);
            placed.Add((p, 2f));
        }

        // great formations of ice growing round the wall, tall spires among smaller ones
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI * 2f / 12f + Random.Range(-0.08f, 0.08f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r - 1.8f);
            if (Blocked(placed, p, 1.6f))
                continue;
            IceCluster(t, p, -a * Mathf.Rad2Deg, i % 3 == 0 ? deepIce : ice, Random.Range(2.8f, 4.6f), true);
            placed.Add((p, 1.8f));
        }
        // a few small crystals out on the rime
        for (int i = 0; i < 6; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(r - 4.4f, r - 3f);
            if (Blocked(placed, p, 1f))
                continue;
            IceCluster(t, p, Random.Range(0f, 360f), ice, Random.Range(0.8f, 1.4f), false);
            placed.Add((p, 1f));
        }

        // cold blue fire at the foot of the great root, on each side of the spring
        foreach (float side in new[] { -1f, 1f })
        {
            var c = spring + new Vector3(side * 8.5f, 0f, -1.5f);
            if (Blocked(placed, c, 1f))
                continue;
            ColdFire(t, c);
            placed.Add((c, 1.3f));
        }
    }

    // A root of the world tree as thick as a house: two great limbs leaning together into an arch, frost along the top
    private static void GreatRoot(Transform parent, Vector3 at, float yaw, float halfSpan, float thick, float height, Material bark, Material rime)
    {
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        float tilt = Mathf.Atan2(halfSpan, height) * Mathf.Rad2Deg;
        float len = Mathf.Sqrt(halfSpan * halfSpan + height * height);
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 mid = turn * new Vector3(side * halfSpan * 0.5f, height * 0.5f, 0f);
            Mats.Shape(PrimitiveType.Cylinder, parent, at + mid, new Vector3(thick, len / 2f, thick), bark, true, "GreatRoot")
                .transform.localRotation = turn * Quaternion.Euler(0f, 0f, side * tilt);
            Mats.Shape(PrimitiveType.Cube, parent, at + mid + Vector3.up * thick * 0.45f, new Vector3(len * 0.8f, 0.25f, thick * 0.6f), rime)
                .transform.localRotation = turn * Quaternion.Euler(0f, 0f, side * tilt - side * 90f);
        }
        // rootlets digging into the ground where the arch lands
        foreach (float side in new[] { -1f, 1f })
            for (int i = 0; i < 3; i++)
                Mats.Shape(PrimitiveType.Cylinder, parent, at + turn * new Vector3(side * halfSpan * 0.95f, 0.3f, (i - 1) * thick * 0.6f), new Vector3(thick * 0.35f, thick * 0.6f, thick * 0.35f), bark)
                    .transform.localRotation = turn * Quaternion.Euler(Random.Range(50f, 70f), side * 90f + (i - 1) * 25f, 0f);
    }

    // The remains of something Nidhogg ate: a spine with curved ribs, and a skull
    private static void RibCage(Transform parent, Vector3 at, float yaw, Material bone)
    {
        Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
        Mats.Shape(PrimitiveType.Cylinder, parent, at + Vector3.up * 0.2f, new Vector3(0.22f, 1.5f, 0.22f), bone)
            .transform.localRotation = turn * Quaternion.Euler(90f, 0f, 0f);
        for (int i = 0; i < 5; i++)
            foreach (float side in new[] { -1f, 1f })
                Mats.Shape(PrimitiveType.Cylinder, parent, at + turn * new Vector3(side * 0.45f, 0.55f, -1.1f + i * 0.55f), new Vector3(0.1f, 0.6f, 0.1f), bone)
                    .transform.localRotation = turn * Quaternion.Euler(0f, 0f, side * 35f);
        Mats.Shape(PrimitiveType.Sphere, parent, at + turn * new Vector3(0f, 0.35f, 1.9f), new Vector3(0.7f, 0.6f, 0.85f), bone);
    }

    // A free spot in the zone, clear of exits, the centre and earlier objects
    private static bool TryPlace(Room room, List<(Vector3 p, float r)> placed, float radius, out Vector3 point)
    {
        float hx = room.Size.x / 2f - 1.5f, hz = room.Size.y / 2f - 1.5f;
        for (int i = 0; i < 25; i++)
        {
            var p = new Vector3(Random.Range(-hx, hx), 0f, Random.Range(-hz, hz));
            if (placed.All(o => Vector3.Distance(o.p, p) > o.r + radius))
            {
                placed.Add((p, radius));
                point = p;
                return true;
            }
        }
        point = Vector3.zero;
        return false;
    }

    // Trees, rocks and the realm's own landmarks: obstacles to fight around
    private void Scenery(Room room, List<(Vector3 p, float r)> placed, float amount)
    {
        if (palace)
        {
            PalaceHall(room, placed, amount);
            return;
        }
        Transform t = room.transform;
        float area = room.Size.x * room.Size.y;
        int big = Mathf.RoundToInt(area / 110f * amount);
        int small = Mathf.RoundToInt(area / 40f * amount);

        for (int i = 0; i < big; i++)
        {
            if (!TryPlace(room, placed, 1.4f, out Vector3 p)) break;
            Prop(t, p, true);
        }

        for (int i = 0; i < small; i++)
        {
            float hx = room.Size.x / 2f - 1f, hz = room.Size.y / 2f - 1f;
            SmallDecor(t, new Vector3(Random.Range(-hx, hx), 0f, Random.Range(-hz, hz)));
        }
    }

    // The land goes on past the playable edge: more of the same scenery out there (no collision),
    // fading into the fog, so the zone has no visible border. The exit paths stay clear.
    private void OutsideScenery(Room room, List<Vector2Int> exits)
    {
        if (palace)
        {
            PalaceOutside(room, exits);
            return;
        }
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        const float band = 24f;
        int count = Mathf.RoundToInt(((room.Size.x + 2f * band) * (room.Size.y + 2f * band) - room.Size.x * room.Size.y) / 30f);

        for (int i = 0; i < count; i++)
        {
            var p = new Vector3(Random.Range(-hx - band, hx + band), 0f, Random.Range(-hz - band, hz + band));
            bool inside = Mathf.Abs(p.x) < hx + 1f && Mathf.Abs(p.z) < hz + 1f;
            if (inside || OnExitPath(p, exits, hx, hz))
                continue;
            Prop(t, p, false);
        }
        for (int i = 0; i < count; i++)
        {
            var p = new Vector3(Random.Range(-hx - band, hx + band), 0f, Random.Range(-hz - band, hz + band));
            if (Mathf.Abs(p.x) > hx || Mathf.Abs(p.z) > hz)
                SmallDecor(t, p);
        }
    }

    private static bool OnExitPath(Vector3 p, List<Vector2Int> exits, float hx, float hz)
    {
        foreach (Vector2Int d in exits)
        {
            float along = d.x != 0 ? p.x * d.x : p.z * d.y;
            float across = d.x != 0 ? p.z : p.x;
            if (along > (d.x != 0 ? hx : hz) - 1f && Mathf.Abs(across) < ExitWidth)
                return true;
        }
        return false;
    }

    // One piece of the realm's big scenery: Asgard's ruins and rune stones in every realm, plus the realm's own
    private void Prop(Transform t, Vector3 p, bool solid)
    {
        float roll = Random.value;
        if (ruinPillarPrefab != null && roll < 0.18f)
        {
            Place(ruinPillarPrefab, t, p, Random.Range(0f, 360f), Random.Range(0.85f, 1.2f), solid);
            return;
        }
        if (runeStonePrefab != null && roll < 0.32f)
        {
            Place(runeStonePrefab, t, p, Random.Range(0f, 360f), Random.Range(0.85f, 1.15f), solid);
            return;
        }
        if (goldenTreePrefab != null && realmIndex == 0 && roll < 0.5f)
        {
            Place(goldenTreePrefab, t, p, Random.Range(0f, 360f), Random.Range(0.8f, 1.1f), solid);
            return;
        }

        switch (realmIndex)
        {
            case Realm.StyleNiflheim:
            {
                // Yggdrasil's frozen roots, glowing ice and frost-crusted rock (from the Niflheim project)
                float r = Random.value;
                if (r < 0.45f) FrozenRoot(t, p, solid);
                else if (r < 0.8f) Crystal(t, p, NiflIce, solid);
                else Boulder(t, p, Mats.Get("nifl_frost_rock", new Color(0.62f, 0.7f, 0.8f), 0.4f), solid);
                break;
            }
            default:
                // Simple leafy trees and boulders where the models are missing
                if (Random.value < 0.6f) Tree(t, p, new Color(0.42f, 0.3f, 0.18f), new Color(0.22f, 0.4f, 0.18f), 1, solid);
                else Boulder(t, p, rockMat, solid);
                break;
        }
    }

    // Small things without collision
    private void SmallDecor(Transform t, Vector3 p)
    {
        switch (realmIndex)
        {
            case Realm.StyleNiflheim:
                // Hoarfrost drifts, and now and then a sliver of ice catching the light
                if (Random.value < 0.5f)
                    Mats.Shape(PrimitiveType.Sphere, t, p, new Vector3(Random.Range(1f, 2.4f), 0.15f, Random.Range(0.8f, 1.8f)), Mats.Get("nifl_frost", new Color(0.56f, 0.66f, 0.82f), 0.5f));
                else
                    Mats.Shape(PrimitiveType.Cube, t, p + Vector3.up * 0.15f, new Vector3(0.12f, 0.3f, 0.12f), Mats.Get("nifl_shard", new Color(0.6f, 0.95f, 1f), 0.9f, new Color(0.3f, 1.1f, 1.5f)))
                        .transform.localRotation = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 90f), Random.Range(-25f, 25f));
                break;
            default:
                // Little star-crystals sprouting between the flagstones
                Mats.Shape(PrimitiveType.Cube, t, p + Vector3.up * 0.1f, new Vector3(0.1f, 0.22f, 0.1f), Mats.Get("star_crystal", new Color(0.55f, 0.85f, 1f), 0.8f, new Color(0.3f, 0.7f, 1.1f)))
                    .transform.localRotation = Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 90f), Random.Range(-20f, 20f));
                break;
        }
    }

    private static Material NiflIce => Mats.Get("nifl_ice", new Color(0.55f, 0.85f, 1f), 0.95f, new Color(0.15f, 0.55f, 0.8f));

    // A realm's mid-boss hall outside Asgard (Niflheim: Nidhogg's lair, great roots coiling round a clearing with ice
    // growing between them)
    private void Lair(Room room, List<(Vector3 p, float r)> placed)
    {
        Transform t = room.transform;
        float hx = room.Size.x / 2f, hz = room.Size.y / 2f;
        for (int i = 0; i < 7; i++)
        {
            float a = i * Mathf.PI * 2f / 7f + Random.Range(-0.15f, 0.15f);
            var p = new Vector3(Mathf.Cos(a) * (hx - 4f), 0f, Mathf.Sin(a) * (hz - 3.5f));
            if (placed.Any(o => Vector3.Distance(o.p, p) < o.r + 2f))
                continue;
            if (realmIndex == Realm.StyleNiflheim)
            {
                if (i % 2 == 0) FrozenRoot(t, p, true);
                else Crystal(t, p, NiflIce, true);
            }
            else
                Prop(t, p, true);
            placed.Add((p, 2.5f));
        }
        Scenery(room, placed, 0.35f);
    }

    private static void Crystal(Transform parent, Vector3 p, Material mat, bool solid)
    {
        for (int i = 0; i < 3; i++)
        {
            float h = Random.Range(1f, 2.6f);
            Mats.Shape(PrimitiveType.Cube, parent, p + new Vector3(Random.Range(-0.5f, 0.5f), h * 0.4f, Random.Range(-0.5f, 0.5f)), new Vector3(0.45f, h, 0.45f), mat, solid, "Crystal")
                .transform.localRotation = Quaternion.Euler(Random.Range(-20f, 20f), Random.Range(0f, 90f), Random.Range(-20f, 20f));
        }
    }

    // A root of Yggdrasil arching out of the ice: two thick bent segments that dive back into the ground,
    // a few thin rootlets, and lumps of hoarfrost caught on top
    private static void FrozenRoot(Transform parent, Vector3 p, bool solid)
    {
        Material bark = Mats.Get("nifl_root", new Color(0.22f, 0.19f, 0.22f), 0.15f);
        Material rime = Mats.Get("nifl_rime", new Color(0.7f, 0.8f, 0.92f), 0.6f);
        Quaternion yaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        float len = Random.Range(2.2f, 3.2f), thick = Random.Range(0.6f, 0.85f);
        foreach (float side in new[] { -1f, 1f })
        {
            float tilt = Random.Range(58f, 70f);
            Vector3 mid = yaw * new Vector3(side * len * 0.42f, 0.55f, 0f);
            Mats.Shape(PrimitiveType.Cylinder, parent, p + mid, new Vector3(thick, len / 2f, thick), bark, solid, "FrozenRoot")
                .transform.localRotation = yaw * Quaternion.Euler(0f, 0f, side * tilt);
            Mats.Shape(PrimitiveType.Cube, parent, p + mid + Vector3.up * thick * 0.5f, new Vector3(len * 0.6f, 0.16f, thick * 0.7f), rime)
                .transform.localRotation = yaw * Quaternion.Euler(0f, 0f, -side * (90f - tilt));
        }
        for (int i = 0; i < 3; i++)
            Mats.Shape(PrimitiveType.Cylinder, parent, p + yaw * Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0f, 0.15f, 0.8f), new Vector3(0.22f, Random.Range(0.5f, 0.9f), 0.22f), bark)
                .transform.localRotation = yaw * Quaternion.Euler(Random.Range(60f, 75f), i * 120f, 0f);
    }

    private static void Tree(Transform parent, Vector3 p, Color trunk, Color leaves, int style, bool solid)
    {
        float h = Random.Range(1.8f, 2.8f);
        Material bark = Mats.Get("trunk_" + ColorUtility.ToHtmlStringRGB(trunk), trunk);
        Mats.Shape(PrimitiveType.Cylinder, parent, p + Vector3.up * h / 2f, new Vector3(0.35f, h / 2f, 0.35f), bark, solid, "Tree");
        if (style == 0)
        {
            // Bare branches
            for (int i = 0; i < 3; i++)
                Mats.Shape(PrimitiveType.Cylinder, parent, p + Vector3.up * (h * 0.7f + i * 0.25f), new Vector3(0.12f, 0.6f, 0.12f), bark)
                    .transform.localRotation = Quaternion.Euler(Random.Range(35f, 60f), i * 120f + Random.Range(0f, 40f), 0f);
            return;
        }
        Material leaf = Mats.Get("leaves_" + ColorUtility.ToHtmlStringRGB(leaves), leaves);
        if (style == 1)
            Mats.Shape(PrimitiveType.Sphere, parent, p + Vector3.up * (h + 0.6f), new Vector3(2.2f, 1.8f, 2.2f), leaf);
        else
            for (int i = 0; i < 3; i++)
                Mats.Shape(PrimitiveType.Sphere, parent, p + Vector3.up * (h * 0.6f + i * 0.7f), new Vector3(2f - i * 0.55f, 0.7f, 2f - i * 0.55f), leaf);
    }

    private static void Boulder(Transform parent, Vector3 p, Material mat, bool solid)
    {
        for (int i = 0; i < 3; i++)
        {
            float s = Random.Range(0.8f, 1.6f);
            Mats.Shape(PrimitiveType.Cube, parent, p + new Vector3(Random.Range(-0.6f, 0.6f), s * 0.4f, Random.Range(-0.6f, 0.6f)), Vector3.one * s, mat, solid, "Boulder")
                .transform.localRotation = Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0f, 90f), Random.Range(-15f, 15f));
        }
    }

    // Gold-veined rocks the player can mine for gold
    private void AddOre(Room room, List<(Vector3 p, float r)> placed, int count)
    {
        // Niflheim has none: the yellow ore blocks looked out of place on its ice
        if (realmIndex == Realm.StyleNiflheim)
            return;
        for (int i = 0; i < count; i++)
        {
            if (!TryPlace(room, placed, 0.8f, out Vector3 local)) break;
            GameObject rock = Mats.Shape(PrimitiveType.Cube, room.transform, local + Vector3.up * 0.5f, Vector3.one, oreMat, true, "GoldOre");
            rock.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
            CaveBlock block = rock.AddComponent<CaveBlock>();
            block.MaxHealth = 3;
            block.GoldValue = Random.Range(3, 7);
        }
    }
}
