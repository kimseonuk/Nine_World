using System.Collections.Generic;
using UnityEngine;

// ── Heimdall's arena: the round altar of the sky ──
// A disc of white marble inlaid with a golden astrolabe, floating over a sea of clouds in daylight. A low marble
// balustrade with gold-capped posts runs round its rim with urns of pink blossom set along it; golden guardians and
// eagle-crowned pillars stand round the far side on plinths of their own; Himinbjorg and the Bifrost's gate rise
// from islands further out. The fighting floor is the same 17 m circle as before, with the same walls and exit, so
// nothing about the fight itself changes. (Other realms' guardian arenas keep BuildArena.)
public partial class DungeonGenerator
{
    // VARCO pieces in Resources/Heaven; simple shapes stand in for any that are missing
    private static Texture2D heavenFloorTexture, heavenCloudTexture;
    private static GameObject flowerUrnPrefab, guardianStatuePrefab, eaglePillarPrefab;
    private static GameObject[] heavenClouds;   // VARCO cloud banks (a long bank, a tall puff, a low wisp)
    private static bool heavenLoaded;

    private static void LoadHeaven()
    {
        if (heavenLoaded)
            return;
        heavenLoaded = true;
        heavenFloorTexture = Resources.Load<Texture2D>("Heaven/ArenaFloor");
        heavenCloudTexture = Resources.Load<Texture2D>("Heaven/CloudSea");
        flowerUrnPrefab = Resources.Load<GameObject>("Heaven/FlowerUrn");
        guardianStatuePrefab = Resources.Load<GameObject>("Heaven/GuardianStatue");
        eaglePillarPrefab = Resources.Load<GameObject>("Heaven/EaglePillar");
        var clouds = new List<GameObject>();
        foreach (string name in new[] { "CloudBank", "CloudPuff", "CloudWisp" })
        {
            var c = Resources.Load<GameObject>("Heaven/" + name);
            if (c != null) clouds.Add(c);
        }
        heavenClouds = clouds.ToArray();
    }

    private static Material HeavenMarble => Mats.Get("heaven_marble", new Color(0.96f, 0.95f, 0.93f), 0.6f);
    private static Material HeavenMarbleShade => Mats.Get("heaven_marble_shade", new Color(0.82f, 0.8f, 0.8f), 0.45f);
    private static Material HeavenGold => Mats.Get("heaven_gold", new Color(0.95f, 0.75f, 0.38f), 0.8f, new Color(0.18f, 0.12f, 0.03f));

    private void HeavenArena(Room room, List<Vector2Int> exits, Realm realm, Transform player, GameObject groundPiece)
    {
        LoadHeaven();
        Transform t = room.transform;
        const float r = ArenaRadius;
        room.SetRound(r);
        // (the zone's ground stays as the thing underfoot, unseen: the altar's own disc is the floor)
        groundPiece.GetComponent<Renderer>().enabled = false;
        room.gameObject.AddComponent<HeavenSky>();

        // ── The disc: the golden astrolabe, a gold ring, a band of plain marble for the balustrade, a gold lip ──
        Material floor;
        if (heavenFloorTexture != null)
        {
            floor = Mats.Get("heaven_floor", Color.white, 0.55f);
            floor.SetTexture("_BaseMap", heavenFloorTexture);
        }
        else
            floor = HeavenMarble;
        FlatMesh(t, "HeavenFloor", AnnulusMesh(0f, r + 0.6f, 160), 0.014f, floor);
        if (heavenFloorTexture == null)
        {
            // (without the painted floor: rings and spokes of gold laid straight into the marble)
            foreach (float ring in new[] { 2.2f, 4.5f, 8.5f, 12.5f, 16.2f })
                FlatMesh(t, "AstroRing", AnnulusMesh(ring - 0.08f, ring + 0.08f, 128), 0.016f, HeavenGold);
            for (int i = 0; i < 12; i++)
            {
                var spoke = Mats.Shape(PrimitiveType.Cube, t, Vector3.up * 0.016f, new Vector3(0.12f, 0.01f, r * 2f - 4.6f), HeavenGold, false, "AstroSpoke");
                spoke.transform.localRotation = Quaternion.Euler(0f, i * 15f, 0f);
            }
        }
        FlatMesh(t, "HeavenRingGold", AnnulusMesh(r + 0.6f, r + 0.85f, 160), 0.016f, HeavenGold);
        FlatMesh(t, "HeavenRim", AnnulusMesh(r + 0.85f, r + 2.6f, 160), 0.014f, HeavenMarble);
        FlatMesh(t, "HeavenLip", AnnulusMesh(r + 2.6f, r + 2.8f, 160), 0.016f, HeavenGold);

        // ── Its body: a thick round slab with a gold band, then smaller tiers stepping in beneath, floating ──
        float[] radii = { r + 2.8f, r + 1.4f, r - 1.5f, r - 6f };
        float[] heights = { 1.4f, 1.3f, 1.8f, 2.6f };
        float y = 0f;
        for (int i = 0; i < radii.Length; i++)
        {
            Mats.Shape(PrimitiveType.Cylinder, t, new Vector3(0f, y - heights[i] / 2f, 0f), new Vector3(radii[i] * 2f, heights[i] / 2f, radii[i] * 2f),
                i == 0 ? HeavenMarble : HeavenMarbleShade, false, "AltarBody");
            Mats.Shape(PrimitiveType.Cylinder, t, new Vector3(0f, y - 0.35f, 0f), new Vector3(radii[i] * 2f + 0.16f, 0.08f, radii[i] * 2f + 0.16f), HeavenGold, false, "AltarBand");
            y -= heights[i];
        }

        // ── The walls (invisible: the balustrade shows the edge) and the way out, as in every guardian's arena ──
        var points = new Vector3[ArenaPillars];
        for (int i = 0; i < ArenaPillars; i++)
        {
            float a = (i + 0.5f) * Mathf.PI * 2f / ArenaPillars;
            points[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
        }
        var exitAngles = new List<float>();
        for (int i = 0; i < ArenaPillars; i++)
        {
            Vector3 from = points[i], to = points[(i + 1) % ArenaPillars];
            Vector3 mid = (from + to) / 2f, dir = (to - from).normalized;
            var side = new Vector2Int(Mathf.RoundToInt(mid.normalized.x), Mathf.RoundToInt(mid.normalized.z));
            bool cardinal = Mathf.Abs(mid.normalized.x) > 0.99f || Mathf.Abs(mid.normalized.z) > 0.99f;
            if (!(cardinal && exits.Contains(side)))
            {
                ChordCollider(t, from, to);
                continue;
            }
            float gap = ExitWidth / 2f + 0.5f;
            ChordCollider(t, from, mid - dir * gap);
            ChordCollider(t, mid + dir * gap, to);
            exitAngles.Add(Mathf.Atan2(side.y, side.x) * Mathf.Rad2Deg);
            BuildExit(room, side, r, player, false);
            HeavenBridge(t, new Vector3(side.x, 0f, side.y), r);
        }
        bool NearExit(float deg, float half)
        {
            foreach (float e in exitAngles)
                if (Mathf.Abs(Mathf.DeltaAngle(deg, e)) < half)
                    return true;
            return false;
        }

        // ── The balustrade: posts with gold balls, low rails between, an urn of blossom every few posts ──
        float rail = r + 1.5f;
        const int posts = 36;
        float doorHalf = Mathf.Atan2(ExitWidth / 2f + 0.9f, rail) * Mathf.Rad2Deg;
        var postAngles = new List<float>();
        for (int i = 0; i < posts; i++)
        {
            float deg = i * 360f / posts;
            if (!NearExit(deg, doorHalf))
                postAngles.Add(deg);
        }
        foreach (float e in exitAngles)
        {
            // (kept in 0..360 with the rest, so the posts sort round the circle in order)
            postAngles.Add(Mathf.Repeat(e - doorHalf, 360f));
            postAngles.Add(Mathf.Repeat(e + doorHalf, 360f));
        }
        postAngles.Sort();
        Vector3 At(float deg, float radius) => new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), 0f, Mathf.Sin(deg * Mathf.Deg2Rad)) * radius;
        for (int i = 0; i < postAngles.Count; i++)
        {
            float a0 = postAngles[i], a1 = postAngles[(i + 1) % postAngles.Count] + (i + 1 == postAngles.Count ? 360f : 0f);
            BalusterPost(t, At(a0, rail));
            // (no rail across a doorway)
            if (NearExit((a0 + a1) / 2f, doorHalf - 0.1f))
                continue;
            Vector3 p0 = At(a0, rail), p1 = At(a1, rail);
            Vector3 mid = (p0 + p1) / 2f, d = p1 - p0;
            var railGo = new GameObject("HeavenRail").transform;
            railGo.SetParent(t, false);
            railGo.localPosition = mid;
            railGo.localRotation = Quaternion.LookRotation(d.normalized);
            float len = d.magnitude - 0.55f;
            Mats.Shape(PrimitiveType.Cube, railGo, new Vector3(0f, 0.12f, 0f), new Vector3(0.36f, 0.24f, len), HeavenMarbleShade, false, "RailBase");
            Mats.Shape(PrimitiveType.Cube, railGo, new Vector3(0f, 0.72f, 0f), new Vector3(0.3f, 0.12f, len), HeavenMarble, false, "RailTop");
            Mats.Shape(PrimitiveType.Cube, railGo, new Vector3(0f, 0.8f, 0f), new Vector3(0.32f, 0.04f, len), HeavenGold, false, "RailGold");
            for (float s = -len / 2f + 0.25f; s <= len / 2f - 0.2f; s += 0.42f)
                Mats.Shape(PrimitiveType.Cylinder, railGo, new Vector3(0f, 0.45f, s), new Vector3(0.16f, 0.21f, 0.16f), HeavenMarble, false, "Baluster");
        }
        // urns of blossom just inside the rail, on the marble band (outside the fight's walls)
        for (int i = 0; i < 12; i++)
        {
            float deg = i * 30f + 15f;
            if (NearExit(deg, doorHalf + 4f))
                continue;
            Vector3 p = At(deg, r + 0.95f + 0.2f);
            if (flowerUrnPrefab != null)
                PlaceSized(flowerUrnPrefab, t, p, Random.Range(0f, 360f), deg > 200f && deg < 340f ? 0.9f : 1.25f, false);
            else
            {
                Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.up * 0.35f, new Vector3(0.6f, 0.35f, 0.6f), HeavenMarble, false, "Urn");
                Mats.Shape(PrimitiveType.Sphere, t, p + Vector3.up * 0.95f, new Vector3(0.9f, 0.6f, 0.9f), Mats.Get("heaven_blossom", new Color(1f, 0.72f, 0.82f), 0.2f), false, "Blossom");
            }
        }

        // ── Round the far side, on plinths of their own: golden guardians and eagle-crowned pillars ──
        // (nothing tall on the camera's side: from 200° to 340° the rim stays open)
        foreach (float deg in new[] { 45f, 135f, 0f, 180f })
        {
            if (NearExit(deg, 20f))
                continue;
            Vector3 p = At(deg, r + 5.2f);
            Plinth(t, p, 2.2f);
            float face = Quaternion.LookRotation(-p).eulerAngles.y;
            Vector3 onTop = p + Vector3.up * 0.3f;
            if (guardianStatuePrefab != null)
                PlaceSized(guardianStatuePrefab, t, onTop, face, deg == 0f || deg == 180f ? 5.5f : 7f, false);
            else if (statuePrefab != null)
                PlaceSized(statuePrefab, t, onTop, face, 5.5f, false);
            SoftLight(t, p + Vector3.up * 3f - p.normalized * 2f, new Color(1f, 0.88f, 0.65f), 9f, 1.2f);
        }
        foreach (float deg in new[] { 22.5f, 67.5f, 112.5f, 157.5f, 90f })
        {
            if (NearExit(deg, 15f))
                continue;
            Vector3 p = At(deg, r + (deg == 90f ? 6.5f : 4f));
            Plinth(t, p, 1.3f);
            if (eaglePillarPrefab != null)
                PlaceSized(eaglePillarPrefab, t, p + Vector3.up * 0.3f, Quaternion.LookRotation(-p).eulerAngles.y, deg == 90f ? 9f : 7f, false);
            else
                PalaceColumn(t, p + Vector3.up * 0.3f, 1.1f, false);
        }

        // ── Further out: Himinbjorg and the Bifrost's gate on floating islands, the clouds below ──
        if (himinbjorgPrefab != null)
        {
            var spot = new Vector3(19f, -1.5f, 30f);
            HeavenIsland(t, spot, 6f);
            Place(himinbjorgPrefab, t, spot, Quaternion.LookRotation(-new Vector3(spot.x, 0f, spot.z)).eulerAngles.y, 0.75f, false);
        }
        if (bifrostGatePrefab != null)
        {
            var spot = new Vector3(-20f, -1f, 28f);
            HeavenIsland(t, spot, 5f);
            Place(bifrostGatePrefab, t, spot, Quaternion.LookRotation(-new Vector3(spot.x, 0f, spot.z)).eulerAngles.y, 0.8f, false);
        }
        foreach (var (spot, size) in new[] { (new Vector3(-36f, -4f, 8f), 5f), (new Vector3(38f, -5f, 6f), 4.5f), (new Vector3(2f, -3f, 48f), 6.5f) })
        {
            HeavenIsland(t, spot, size);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                PalaceColumn(t, spot + Quaternion.Euler(0f, a, 0f) * Vector3.forward * (size - 1.4f), 0.8f, false);
            }
        }
        Clouds(t);
        SoftLight(t, new Vector3(0f, 9f, -2f), new Color(1f, 0.93f, 0.8f), 26f, 1.4f);
    }

    // The way out as a marble bridge into the clouds: a slab under the exit's path, its top laid in marble, gold
    // along both edges and a deep keel beneath (the path itself stays the thing walked on)
    private static void HeavenBridge(Transform t, Vector3 normal, float r)
    {
        var root = new GameObject("HeavenBridge").transform;
        root.SetParent(t, false);
        root.localPosition = normal * (r + 7f);
        root.localRotation = Quaternion.LookRotation(normal);
        const float length = 13f, width = ExitWidth + 1.2f;
        Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, 0.014f, 0f), new Vector3(ExitWidth, 0.01f, length), HeavenMarble, false, "BridgeTop");
        Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, -0.4f, 0f), new Vector3(width, 0.8f, length), HeavenMarbleShade, false, "BridgeSlab");
        Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, -1.3f, 0f), new Vector3(width * 0.5f, 1f, length), HeavenMarbleShade, false, "BridgeKeel");
        foreach (float s in new[] { -1f, 1f })
        {
            Mats.Shape(PrimitiveType.Cube, root, new Vector3(s * (width / 2f - 0.1f), 0.06f, 0f), new Vector3(0.22f, 0.12f, length), HeavenGold, false, "BridgeEdge");
            for (float z = -length / 2f + 1.5f; z < length / 2f; z += 3f)
                BalusterPost(root, new Vector3(s * (width / 2f - 0.1f), 0f, z));
        }
    }

    // A balustrade post: a marble block with a gold ball on top
    private static void BalusterPost(Transform t, Vector3 p)
    {
        var root = new GameObject("HeavenPost").transform;
        root.SetParent(t, false);
        root.localPosition = p;
        Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, 0.08f, 0f), new Vector3(0.62f, 0.16f, 0.62f), HeavenMarbleShade, false, "PostFoot");
        Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, 0.55f, 0f), new Vector3(0.46f, 0.8f, 0.46f), HeavenMarble, false, "PostShaft");
        Mats.Shape(PrimitiveType.Cube, root, new Vector3(0f, 0.98f, 0f), new Vector3(0.56f, 0.08f, 0.56f), HeavenGold, false, "PostCap");
        Mats.Shape(PrimitiveType.Sphere, root, new Vector3(0f, 1.2f, 0f), Vector3.one * 0.36f, HeavenGold, false, "PostBall");
    }

    // A round marble plinth standing out from the altar's edge, its top level with the floor, stepping in beneath
    private static void Plinth(Transform t, Vector3 p, float radius)
    {
        Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.up * 0.15f, new Vector3(radius * 2f, 0.15f, radius * 2f), HeavenMarble, false, "PlinthTop");
        Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.up * 0.3f, new Vector3(radius * 2f + 0.1f, 0.03f, radius * 2f + 0.1f), HeavenGold, false, "PlinthGold");
        Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.down * 1.2f, new Vector3(radius * 1.6f, 1.2f, radius * 1.6f), HeavenMarbleShade, false, "PlinthBody");
        Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.down * 3f, new Vector3(radius * 1f, 0.8f, radius * 1f), HeavenMarbleShade, false, "PlinthFoot");
    }

    // A small floating island of marble for the far landmarks
    private static void HeavenIsland(Transform t, Vector3 p, float radius)
    {
        Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.down * 0.4f, new Vector3(radius * 2f, 0.4f, radius * 2f), HeavenMarble, false, "IslandTop");
        Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.down * 0.45f, new Vector3(radius * 2f + 0.2f, 0.06f, radius * 2f + 0.2f), HeavenGold, false, "IslandGold");
        Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.down * 2f, new Vector3(radius * 1.5f, 1.2f, radius * 1.5f), HeavenMarbleShade, false, "IslandBody");
        Mats.Shape(PrimitiveType.Cylinder, t, p + Vector3.down * 4f, new Vector3(radius * 0.8f, 1f, radius * 0.8f), HeavenMarbleShade, false, "IslandRoot");
    }

    // The sea of clouds far below, and banks of cloud drifting near the altar's edge
    private static void Clouds(Transform t)
    {
        Material sea;
        if (heavenCloudTexture != null)
        {
            sea = Mats.Get("heaven_cloud_sea", Color.white, 0.05f, new Color(0.45f, 0.43f, 0.42f));
            sea.SetTexture("_BaseMap", heavenCloudTexture);
            sea.SetTexture("_EmissionMap", heavenCloudTexture);
            sea.SetTextureScale("_BaseMap", Vector2.one * 6f);
        }
        else
            sea = Mats.Get("heaven_cloud_flat", new Color(0.95f, 0.94f, 0.95f), 0.05f, new Color(0.4f, 0.4f, 0.42f));
        Mats.Shape(PrimitiveType.Cube, t, new Vector3(0f, -16f, 0f), new Vector3(360f, 0.2f, 360f), sea, false, "CloudSea");

        // VARCO's clouds: banks ringing the altar below its edge, low on the camera's side, and taller ones standing far
        // off behind it so the sky has depth
        if (heavenClouds != null && heavenClouds.Length > 0)
        {
            for (int i = 0; i < 34; i++)
            {
                float a = (i / 34f + Random.Range(-0.012f, 0.012f)) * Mathf.PI * 2f;
                float d = Random.Range(25f, 50f);
                var c = new Vector3(Mathf.Cos(a) * d, Random.Range(-9f, -4f), Mathf.Sin(a) * d);
                if (c.z < 0f)
                    c.y -= 3f;   // (low on the camera's side, so they never come between it and the fight)
                GameObject prefab = heavenClouds[Random.Range(0, heavenClouds.Length)];
                GameObject cloud = PlaceWide(prefab, t, c, 0f, Random.Range(11f, 20f));
                cloud.transform.localPosition = c;   // (centred where asked, not stood on the floor)
                cloud.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                cloud.name = "Cloud";
            }
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Lerp(20f, 160f, (i + Random.value * 0.6f) / 9f) * Mathf.Deg2Rad;   // the far half only
                float d = Random.Range(52f, 70f);
                var c = new Vector3(Mathf.Cos(a) * d, Random.Range(-2f, 6f), Mathf.Sin(a) * d);
                GameObject cloud = PlaceWide(heavenClouds[Random.Range(0, heavenClouds.Length)], t, c, 0f, Random.Range(22f, 34f));
                cloud.transform.localPosition = c;
                cloud.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                cloud.name = "Cloud";
            }
            return;
        }

        Material puff = Mats.Get("heaven_cloud_puff", new Color(1f, 0.99f, 0.98f), 0.02f, new Color(0.35f, 0.34f, 0.36f));
        for (int i = 0; i < 46; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float d = Random.Range(24f, 52f);
            var c = new Vector3(Mathf.Cos(a) * d, Random.Range(-9f, -3.5f), Mathf.Sin(a) * d);
            // (banks are low on the camera's side, so they never come between it and the fight)
            if (c.z < 0f)
                c.y -= 3f;
            for (int k = 0; k < 4; k++)
            {
                Vector3 o = new Vector3(Random.Range(-3f, 3f), Random.Range(-0.5f, 0.8f), Random.Range(-2f, 2f));
                float s = Random.Range(3f, 6.5f);
                Mats.Shape(PrimitiveType.Sphere, t, c + o, new Vector3(s * 1.5f, s * 0.55f, s), puff, false, "CloudPuff");
            }
        }
    }
}
