using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

// One zone of a floor: an open field the player walks into through an exit.
// Fighting zones seal their exits when the player arrives and open them once every enemy is down.
public class Room : MonoBehaviour
{
    // MidBoss: the hall before the guardian's arena where the realm's mid-boss (Týr in Asgard) waits
    // Forge: 강화방, where equipped skills are upgraded (added last, so the older values keep their numbers)
    // Supports / Heal / Vault / Exit: Asgard's route (갈림길) rooms - the rune altar (보조마법), the healing spring,
    // the gold vault, and the waystone hall at the end of each area where [C] opens the route map
    public enum Kind { Start, Combat, Treasure, Shop, Boss, MidBoss, Forge, Supports, Heal, Vault, Exit, Vigor, Ascend, Fate, Essence }
    private enum State { Idle, Fighting, Cleared }

    // Every wave brings this many times the enemies it would (the per-role caps grow with it; bosses and their
    // summoned adds don't).
    public const int EnemyMultiplier = 3;

    // Every kind comes from the very first fight on (the realm's minDepth is ignored), titans and wave leaders too,
    // so the early halls are as varied as the deep ones
    private int MinDepthOf(EnemyEntry e) => 0;

    private readonly List<GameObject> barriers = new List<GameObject>();
    private readonly List<Enemy> spawned = new List<Enemy>();
    private State state;
    private int wavesLeft;
    private int budget;
    private Realm realm;
    private Transform player;

    public Kind RoomKind { get; private set; }
    public Vector2Int Grid { get; private set; }
    public int Depth { get; private set; }        // zones away from the start
    public Vector2 Size { get; private set; }     // walkable width (x) and depth (z) in metres
    public float Radius { get; private set; }     // a round zone (the guardian's arena); 0 for a rectangle
    public bool Visited { get; private set; }
    public bool IsFighting => state == State.Fighting;
    public bool Cleared => state == State.Cleared;
    public Enemy Boss { get; private set; }
    public Enemy SecondBoss { get; private set; }   // a second guardian fought at the same time (Niflheim's twin giants)
    // Asgard's route: an ordinary fight hall the mid-boss also joins (its usual waves come all the same)
    public bool HasMidBoss { get; private set; }
    public bool IsMidBossFight => RoomKind == Kind.MidBoss || HasMidBoss;
    public void AddMidBoss() => HasMidBoss = true;
    public readonly List<Room> Neighbours = new List<Room>();
    public readonly Dictionary<Vector2Int, Room> Exits = new Dictionary<Vector2Int, Room>();

    // The zones shown right now (only the player's, usually)
    public static readonly List<Room> Shown = new List<Room>();
    private void OnEnable() => Shown.Add(this);
    private void OnDisable() => Shown.Remove(this);

    public static event Action<Room> FightStarted;
    public static event Action<Room> RoomCleared;

    public void Init(Kind kind, Vector2Int grid, int depth, Vector2 size, Realm realmData, Transform playerTransform)
    {
        RoomKind = kind;
        Grid = grid;
        Depth = depth;
        Size = size;
        realm = realmData;
        player = playerTransform;
        // Zones without a fight are open from the start
        state = kind == Kind.Combat || kind == Kind.Boss || kind == Kind.MidBoss ? State.Idle : State.Cleared;
        wavesLeft = kind == Kind.Boss || kind == Kind.MidBoss ? 1 : realm.WavesAt(depth);
        budget = realm.roomBudget + 1 + depth / 2;
    }

    // A bigger zone (Asgard's large fight maps) buys bigger waves
    public void AddBudget(int points) => budget += points;

    public void AddBarrier(GameObject barrier)
    {
        barrier.SetActive(false);
        barriers.Add(barrier);
    }

    public void SetRound(float radius) => Radius = radius;

    // A shaped fight hall (a long gallery, a cross, a cloister round an open court, a bridge...): its walkable floor as
    // rectangles in local x/z, and the open cosmos left between them. Null floor: the whole Size rectangle is floor.
    public List<Rect> Floor { get; private set; }
    public List<Rect> Voids { get; private set; }
    public string Shape { get; private set; } = "";

    public void SetFloor(List<Rect> floor, List<Rect> voids, string shape)
    {
        Floor = floor;
        Voids = voids ?? new List<Rect>();
        Shape = shape ?? "";
    }

    // Is this local x/z point on the floor, at least margin in from its edge?
    public bool OnFloorLocal(Vector2 p, float margin = 0f)
    {
        if (Floor == null)
            return Mathf.Abs(p.x) < Size.x / 2f - margin && Mathf.Abs(p.y) < Size.y / 2f - margin;
        foreach (Rect r in Floor)
            if (p.x > r.xMin + margin && p.x < r.xMax - margin && p.y > r.yMin + margin && p.y < r.yMax - margin)
                return true;
        return false;
    }

    // The nearest point on the floor (margin in from its edge), keeping the height: flyers stay over the floor, in reach
    public Vector3 ClampToFloor(Vector3 world, float margin)
    {
        Vector3 local = world - transform.position;
        Vector2 best = new Vector2(Mathf.Clamp(local.x, -Size.x / 2f + margin, Size.x / 2f - margin), Mathf.Clamp(local.z, -Size.y / 2f + margin, Size.y / 2f - margin));
        if (Floor != null)
        {
            float bestDist = float.MaxValue;
            foreach (Rect r in Floor)
            {
                float mx = Mathf.Min(margin, r.width / 2f - 0.1f), mz = Mathf.Min(margin, r.height / 2f - 0.1f);
                var c = new Vector2(Mathf.Clamp(local.x, r.xMin + mx, r.xMax - mx), Mathf.Clamp(local.z, r.yMin + mz, r.yMax - mz));
                float dist = (c - new Vector2(local.x, local.z)).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }
        }
        return transform.position + new Vector3(best.x, local.y, best.y);
    }

    public bool Contains(Vector3 point, float margin = 0f)
    {
        Vector3 d = point - transform.position;
        if (Radius > 0f)
            return new Vector2(d.x, d.z).magnitude < Radius - margin;
        // (a shaped hall counts only its floor; a negative margin, "near the zone", still means its whole rectangle)
        if (Floor != null && margin >= 0f)
            return OnFloorLocal(new Vector2(d.x, d.z), margin);
        return Mathf.Abs(d.x) < Size.x / 2f - margin && Mathf.Abs(d.z) < Size.y / 2f - margin;
    }

    // Just inside the exit on that side, facing into the zone
    public Vector3 EntrancePoint(Vector2Int side)
    {
        Vector3 edge = new Vector3(side.x * Size.x / 2f, 0f, side.y * Size.y / 2f);
        return transform.position + edge - new Vector3(side.x, 0f, side.y) * 2.5f;
    }

    // The player arrives: mark it on the map and start the fight if there is one
    public void Enter()
    {
        Visited = true;
        if (state == State.Idle)
            StartFight();
    }

    // An enemy that appeared mid-fight (a splitter's offspring) counts toward this room's wave like the rest
    public void Adopt(Enemy enemy)
    {
        if (enemy != null && !spawned.Contains(enemy))
            spawned.Add(enemy);
    }

    private void Update()
    {
        if (player == null || !GameManager.IsRunActive || state != State.Fighting)
            return;

        spawned.RemoveAll(e => e == null || e.IsDead);
        // composed waves overlap: the next one comes in while the last few of this one still stand (not in a hall the
        // mid-boss holds, where each wave is cleared first)
        if (realm.composedWaves && wavesLeft > 1 && spawned.Count > 0 && spawned.Count <= OverlapAt && Time.time - lastWaveAt > 4f)
        {
            wavesLeft--;
            SpawnWave();
            return;
        }
        if (spawned.Count > 0)
            return;
        // the next wave comes when this one is down (a mid-boss fought among the waves doesn't hold them back);
        // the zone clears once the last wave and every boss in it have fallen
        if (wavesLeft > 1)
        {
            wavesLeft--;
            SpawnWave();
        }
        else if ((Boss == null || Boss.IsDead) && (SecondBoss == null || SecondBoss.IsDead))
        {
            wavesLeft = 0;
            Clear();
        }
    }

    private void StartFight()
    {
        state = State.Fighting;
        foreach (GameObject barrier in barriers)
            barrier.SetActive(true);
        player.GetComponent<PlayerStats>().ResetWards();

        if (RoomKind == Kind.Boss)
        {
            // Twin guardians (Niflheim's giants) stand apart, side by side across the arena
            bool pair = realm.boss2Prefab != null;
            Vector3 across = transform.right * (pair ? 3.5f : 0f);
            Boss = SpawnGuardian(realm.bossPrefab, realm.bossName, realm.bossTint, realm.bossHealth, realm.bossDamage, realm.bossSize,
                realm.bossMoves, 4f, transform.position - across, pair ? 1 : 2);
            if (pair)
                SecondBoss = SpawnGuardian(realm.boss2Prefab, realm.boss2Name, realm.boss2Tint, realm.boss2Health, realm.boss2Damage, realm.boss2Size,
                    realm.boss2Moves, 8.5f, transform.position + across, 1);
            // Walking in, the camera turns to the guardian and its name comes up
            if (SecondBoss is HumanoidEnemy giantB && Boss is HumanoidEnemy giantA)
                BossCutscene.Play(Boss.transform, BossCutscene.HeightOf(Boss.transform),
                    TwinIntro(giantA, giantB, $"{realm.bossName} · {realm.boss2Name}", realm.name + "의 수호자들"), false);
            else if (Boss is NidhoggBoss dragon)
            {
                // Niflheim's dragon rears up and shrieks as the camera finds it
                dragon.PlayIntro();
                BossCutscene.Play(Boss.transform, BossCutscene.HeightOf(Boss.transform),
                    BossCutscene.NameCard(realm.bossName, "이그드라실의 뿌리를 갉는 자", realm.accentColor), false);
            }
            else if (Boss is HeimdallBoss heimdall)
                BossCutscene.Play(Boss.transform, heimdall.IntroHeight, heimdall.IntroScene(), false);
            else
                BossCutscene.Play(Boss.transform, BossCutscene.HeightOf(Boss.transform),
                    BossCutscene.NameCard(realm.bossName, realm.name + "의 수호자", realm.accentColor), false);
        }
        else if (IsMidBossFight && realm.midTwins != null && realm.midTwins.Length >= 2)
        {
            // Niflheim's twin giants wait in their arena, side by side; they greet each other before the fight
            Realm.Guardian ga = realm.midTwins[0], gb = realm.midTwins[1];
            Vector3 across = transform.right * 3.5f;
            Boss = SpawnGuardian(ga.prefab, ga.name, ga.tint, ga.health, ga.damage, ga.size, ga.moves, 4f, transform.position - across, 1);
            SecondBoss = SpawnGuardian(gb.prefab, gb.name, gb.tint, gb.health, gb.damage, gb.size, gb.moves, 8.5f, transform.position + across, 1);
            Boss.XpDrop = SecondBoss.XpDrop = 40;
            if (Boss is HumanoidEnemy giantA && SecondBoss is HumanoidEnemy giantB)
                BossCutscene.Play(Boss.transform, BossCutscene.HeightOf(Boss.transform),
                    TwinIntro(giantA, giantB, realm.MidBossTitle, "흐베르겔미르로 가는 길의 수문장들"), false);
        }
        else if (IsMidBossFight && realm.midBoss != null)
        {
            // The mid-boss stands at the far end of the hall and steps forward to meet the player
            Boss = GameManager.Instance.SpawnEnemy(realm.midBoss, transform.position + new Vector3(0f, 0f, Size.y * 0.18f), this);
            Boss.IsBoss = true;
            Boss.XpDrop = 40;
            if (realm.style == Realm.StyleNiflheim)
                BossAwakening.Attach(Boss);   // (Niflheim's bosses awaken at 30%; the other stages' bosses keep their own phases)
            if (Boss is TyrBoss tyr)
                BossCutscene.Play(Boss.transform, tyr.IntroHeight, tyr.IntroScene(), false);
            else if (Boss is NidhoggBoss nidhogg)
            {
                // Niflheim's dragon rears up and shrieks as the camera finds it, and calls the dead when it roars
                nidhogg.Roared += _ => SpawnAdds(2);
                nidhogg.PlayIntro();
                BossCutscene.Play(Boss.transform, BossCutscene.HeightOf(Boss.transform),
                    BossCutscene.NameCard(realm.midBoss.name, "이그드라실의 뿌리를 갉는 자", realm.accentColor), false);
            }
            else
                BossCutscene.Play(Boss.transform, BossCutscene.HeightOf(Boss.transform),
                    BossCutscene.NameCard(realm.midBoss.name, realm.name + "의 시련", realm.accentColor), false);
        }
        else
            SpawnWave();
        // a fight hall the mid-boss joins: its usual enemies come with it — except Týr, who fights alone in his own
        // battleground (no waves before, with or after him)
        if (HasMidBoss && RoomKind != Kind.MidBoss && Boss != null)
        {
            if (Boss is TyrBoss)
                wavesLeft = 1;
            else
                SpawnWave();
        }
        FightStarted?.Invoke(this);
    }

    // A realm guardian: an ordinary enemy grown into a boss, with the realm's own area moves on top of its slam
    // (a dedicated boss such as Heimdall sets itself up)
    private Enemy SpawnGuardian(Enemy prefab, string bossName, Color tint, int bossHealth, float bossDamage, float size,
        HumanoidEnemy.AreaAttack[] moves, float firstMove, Vector3 at, int addsPerRoar)
    {
        Enemy boss = GameManager.Instance.SpawnEnemy(new EnemyEntry { prefab = prefab, name = bossName, tint = tint, boss = true }, at, this);
        if (boss is NidhoggBoss nidhogg)
        {
            nidhogg.Setup(bossName, bossHealth, bossDamage, size);
            nidhogg.Roared += _ => SpawnAdds(addsPerRoar);
        }
        else if (boss is HumanoidEnemy humanoid)
        {
            humanoid.MakeBoss(bossName, bossHealth, bossDamage, size);
            humanoid.Roared += _ => SpawnAdds(addsPerRoar);
            if (moves != null)
                for (int i = 0; i < moves.Length; i++)
                    humanoid.SetAreaAttack(moves[i], firstMove + i * 6f);
        }
        boss.IsBoss = true;
        boss.XpDrop = 60;
        if (realm.style == Realm.StyleNiflheim)
            BossAwakening.Attach(boss);   // 페이즈 2 at 30% health (Niflheim's guardians only)
        return boss;
    }

    // The twin giants stride together and slap a high five with a crack of frost before turning on the player
    private IEnumerator TwinIntro(HumanoidEnemy a, HumanoidEnemy b, string title, string subtitle)
    {
        a.PlayHighFive(b, true);
        b.PlayHighFive(a, false);
        yield return BossCutscene.NameCard(title, subtitle, realm.accentColor);
    }

    // A wave is bought from the zone's budget a group at a time, mixing kinds (see PickEnemy), so waves bring two or
    // three kinds rather than one big pack. Each later wave of a zone is a little bigger than the one before.
    private int wavesSpawned;

    private void SpawnWave()
    {
        lastWaveAt = Time.time;
        if (realm.composedWaves)
        {
            SpawnComposedWave();
            return;
        }
        int points = (budget + wavesSpawned++ * 2) * EnemyMultiplier;
        int guard = 50;
        var picked = new Dictionary<EnemyEntry, int>();
        while (points > 0 && guard-- > 0)
        {
            // A leader this deep in comes first; the rest of the wave is picked round it
            EnemyEntry entry = null;
            foreach (EnemyEntry e in realm.enemies)
                if (e.leader && e.prefab != null && !TooEarly(e) && e.cost <= points && !picked.ContainsKey(e))
                    entry = e;
            entry = entry ?? PickEnemy(points, picked);
            if (entry == null)
                break;
            points -= entry.cost;
            picked[entry] = picked.TryGetValue(entry, out int n) ? n + 1 : 1;
            // A group arrives together, spread around one spot
            // (each member of a group gets its own spot spread over the floor)
            for (int i = 0; i < Mathf.Max(1, entry.group); i++)
                spawned.Add(GameManager.Instance.SpawnEnemy(entry, SpawnPoint(), this, i % 2 == 1));
        }
    }

    // ── Composed waves (Asgard): a recipe by role, growing with depth ──
    //   front line (근접·탱커·거구)   2, +1 from depth 3, +1 from depth 6, +1 from the third wave on
    //   ranged (원거리)              1, +1 from depth 1, +1 from depth 5
    //   specialists (장판·지원·분열)  1, +1 from depth 2, +1 from depth 5
    //   (all three times EnemyMultiplier; most kinds from the first fight on, see MinDepthOf)
    //   a leader (에인헤랴르 용사) heads every wave once it shows up
    // counted in bodies (a pair is two), and never more alive at once than: 장판 2, 지원 1, 거구 1, 탱커 2, 원거리 4.
    // In the hall the mid-boss holds, the waves are lighter (one less front and ranged) and bring no zoners or titans.
    private float lastWaveAt;
    private readonly Dictionary<Enemy, EnemyEntry.Role> roleOf = new Dictionary<Enemy, EnemyEntry.Role>();
    private int OverlapAt => IsMidBossFight ? 0 : Depth >= 4 ? 2 : 1;

    private static readonly EnemyEntry.Role[] FrontRoles = { EnemyEntry.Role.Front, EnemyEntry.Role.Tank, EnemyEntry.Role.Heavy };
    private static readonly EnemyEntry.Role[] FrontRolesNoHeavy = { EnemyEntry.Role.Front, EnemyEntry.Role.Tank };
    private static readonly EnemyEntry.Role[] RangedRoles = { EnemyEntry.Role.Ranged };
    private static readonly EnemyEntry.Role[] SpecialRoles = { EnemyEntry.Role.Zone, EnemyEntry.Role.Support, EnemyEntry.Role.Splitter };
    private static readonly EnemyEntry.Role[] SpecialRolesNoZone = { EnemyEntry.Role.Support, EnemyEntry.Role.Splitter };

    private void SpawnComposedWave()
    {
        int d = Depth, wave = wavesSpawned++;
        int front = 2 + (d >= 3 ? 1 : 0) + (d >= 6 ? 1 : 0) + (wave >= 2 ? 1 : 0);
        int ranged = 1 + (d >= 1 ? 1 : 0) + (d >= 5 ? 1 : 0);
        // (specialists from the first fight on, for variety)
        int special = 1 + (d >= 2 ? 1 : 0) + (d >= 5 ? 1 : 0);
        bool midBossHall = IsMidBossFight;
        if (midBossHall)
        {
            front = Mathf.Max(1, front - 1);
            ranged = Mathf.Max(1, ranged - 1);
            special = Mathf.Min(special, 1);
        }
        front *= EnemyMultiplier;
        ranged *= EnemyMultiplier;
        special *= EnemyMultiplier;
        var picked = new Dictionary<EnemyEntry, int>();
        // the leader first, standing for one of the front line
        foreach (EnemyEntry e in realm.enemies)
            if (e.leader && e.prefab != null && !TooEarly(e) && !midBossHall)
            {
                SpawnGroup(e, SpawnPoint(7f, 12f));
                picked[e] = 1;
                front--;
                break;
            }
        // (no titan beside the mid-boss: two giants at once leave no room to read either)
        Fill(midBossHall ? FrontRolesNoHeavy : FrontRoles, front, picked, 7f, 13f);
        Fill(RangedRoles, ranged, picked, 11f, 17f);
        Fill(midBossHall ? SpecialRolesNoZone : SpecialRoles, special, picked, 10f, 16f);
    }

    private int AliveWith(EnemyEntry.Role role)
    {
        int n = 0;
        foreach (Enemy e in spawned)
            if (e != null && !e.IsDead && roleOf.TryGetValue(e, out var r) && r == role) n++;
        return n;
    }

    private static int CapOf(EnemyEntry.Role role) => EnemyMultiplier * role switch
    {
        EnemyEntry.Role.Zone => 2, EnemyEntry.Role.Support => 1, EnemyEntry.Role.Heavy => 1,
        EnemyEntry.Role.Tank => 2, EnemyEntry.Role.Ranged => 4, _ => 99,
    };

    // Bodies of the given roles, picked by weight (a kind already in this wave counts for a quarter for each time)
    private void Fill(EnemyEntry.Role[] roles, int bodies, Dictionary<EnemyEntry, int> picked, float near, float far)
    {
        int guard = 20;
        while (bodies > 0 && guard-- > 0)
        {
            float total = 0f;
            var options = new List<(EnemyEntry e, float w)>();
            foreach (EnemyEntry e in realm.enemies)
            {
                if (e.prefab == null || e.leader || TooEarly(e) || System.Array.IndexOf(roles, e.role) < 0) continue;
                int size = Mathf.Max(1, e.group);
                if (size > bodies && bodies > 1) continue;
                if (AliveWith(e.role) + size > CapOf(e.role)) continue;
                float w = e.weight / Mathf.Pow(4f, picked.TryGetValue(e, out int n) ? n : 0);
                options.Add((e, w));
                total += w;
            }
            if (options.Count == 0) break;
            float roll = Random.Range(0f, total);
            EnemyEntry pick = options[options.Count - 1].e;
            foreach (var (e, w) in options) { roll -= w; if (roll < 0f) { pick = e; break; } }
            picked[pick] = picked.TryGetValue(pick, out int c) ? c + 1 : 1;
            bodies -= Mathf.Max(1, pick.group);
            SpawnGroup(pick, SpawnPoint(near, far));
        }
    }

    private bool TooEarly(EnemyEntry e) => Depth < MinDepthOf(e);

    private void SpawnGroup(EnemyEntry entry, Vector3 center)
    {
        int count = Mathf.Max(1, entry.group);
        for (int i = 0; i < count; i++)
        {
            // (a group no longer arrives in a huddle: each of its members gets its own spot spread over the floor)
            Vector3 at = i == 0 ? FreeSpot(center) : SpawnPoint();
            Enemy enemy = GameManager.Instance.SpawnEnemy(entry, at, this, i % 2 == 1);
            spawned.Add(enemy);
            roleOf[enemy] = entry.role;
        }
    }

    private void SpawnAdds(int count)
    {
        if (realm.bossAdds == null || state != State.Fighting)
            return;
        for (int i = 0; i < count; i++)
            spawned.Add(GameManager.Instance.SpawnEnemy(realm.bossAdds, SpawnPoint(), this));
    }

    // A weighted pick among the enemies that still fit the budget. Kinds not yet in the wave come first; once every
    // kind that fits is in, a kind counts for a quarter of its weight for each time it was already picked.
    // (Kinds that only show up deeper in are left out until then.)
    private EnemyEntry PickEnemy(int points, Dictionary<EnemyEntry, int> picked)
    {
        bool Allowed(EnemyEntry e) => e.prefab != null && e.cost <= points && !TooEarly(e);
        bool newKindFits = false;
        foreach (EnemyEntry e in realm.enemies)
            if (Allowed(e) && !picked.ContainsKey(e))
                newKindFits = true;
        float Weight(EnemyEntry e) => !Allowed(e) || (newKindFits && picked.ContainsKey(e)) ? 0f
            : e.weight / Mathf.Pow(4f, picked.TryGetValue(e, out int n) ? n : 0);
        float total = 0f;
        foreach (EnemyEntry e in realm.enemies)
            total += Weight(e);
        if (total <= 0f)
            return null;
        float roll = Random.Range(0f, total);
        EnemyEntry last = null;
        foreach (EnemyEntry e in realm.enemies)
        {
            float w = Weight(e);
            if (w <= 0f) continue;
            last = e;
            roll -= w;
            if (roll < 0f) return e;
        }
        return last;
    }

    // Somewhere free in the zone, at least `near` from the player (who just walked in), spread over the whole floor:
    // of several free spots, the one farthest from the enemies already standing. (`far` no longer limits it: the
    // monsters fill the map instead of crowding round the player.)
    private Vector3 SpawnPoint() => SpawnPoint(7f, 16f);

    private Vector3 SpawnPoint(float near, float far)
    {
        float hx = Size.x / 2f - 2f, hz = Size.y / 2f - 2f;
        Vector3 best = default;
        float bestGap = -1f;
        int found = 0;
        for (int attempt = 0; attempt < 80 && found < 20; attempt++)
        {
            Vector3 p = transform.position + new Vector3(Random.Range(-hx, hx), 0.1f, Random.Range(-hz, hz));
            if (!Contains(p, 2f))
                continue;   // the round arena's corners are outside its walls
            if (Vector3.Distance(p, player.position) < near)
                continue;
            if (Blocked(p))
                continue;
            found++;
            float gap = NearestEnemy(p);
            if (gap > bestGap)
            {
                bestGap = gap;
                best = p;
            }
        }
        if (found > 0)
            return best;
        // (nowhere at that distance: anywhere free on the floor, never the middle, where the fountain stands)
        return FreeSpot(transform.position + new Vector3(Random.Range(-hx, hx), 0.1f, Random.Range(-hz, hz)));
    }

    // How far the nearest monster of this zone stands from p (far off when there are none yet)
    private float NearestEnemy(Vector3 p)
    {
        float nearest = 1000f;
        foreach (Enemy e in spawned)
        {
            if (e == null || e.IsDead) continue;
            Vector3 d = e.transform.position - p;
            d.y = 0f;
            nearest = Mathf.Min(nearest, d.magnitude);
        }
        return nearest;
    }

    private static bool Blocked(Vector3 p) => Physics.CheckSphere(p + Vector3.up * 1f, 0.7f, ~0, QueryTriggerInteraction.Ignore);

    // The nearest free spot on the floor to p (a group member set down inside a fountain, a pillar or out over the
    // void is moved out to where the player can reach it)
    public Vector3 FreeSpot(Vector3 p)
    {
        if (Contains(p, 1.5f) && !Blocked(p))
            return p;
        for (float ring = 1f; ring <= 12f; ring += 1f)
            for (int k = 0; k < 12; k++)
            {
                Vector3 q = p + Quaternion.Euler(0f, k * 30f + ring * 17f, 0f) * Vector3.forward * ring;
                q.y = p.y;
                if (Contains(q, 1.5f) && !Blocked(q))
                    return q;
            }
        return ClampToFloor(p, 1.5f);
    }

    private void Clear()
    {
        state = State.Cleared;
        foreach (GameObject barrier in barriers)
            barrier.SetActive(false);
        RunState.RoomsCleared++;

        PlayerStats stats = player.GetComponent<PlayerStats>();
        if (RunState.Stats.healOnRoomClear > 0f)
            stats.Heal(RunState.Stats.healOnRoomClear);
        // The mid-boss falls (Týr, Niflheim's giants): a good heal before the guardian's arena
        if (IsMidBossFight)
            stats.Heal(stats.MaxHealth * 0.35f);
        // (회복 아이템: the token upgrade makes them come more often and heal more)
        if (RoomKind == Kind.Combat && Random.value < 0.3f + MetaProgress.HealBonus)
            Pickup.SpawnHeal(player.position + player.forward * 1.5f + Vector3.up, Mathf.RoundToInt(20 * (1f + MetaProgress.HealBonus)));
        RoomCleared?.Invoke(this);
    }
}
