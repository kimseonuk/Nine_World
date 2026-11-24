using UnityEngine;

// One kind of enemy a realm can send, with its realm look and toughness
public class EnemyEntry
{
    public Enemy prefab;
    public string name;
    public int cost = 1;            // share of a room's budget
    public int weight = 1;
    public float size = 1f;
    public float health = 1f;
    public float damage = 1f;
    public Color tint = Color.white;
    public int gold = 2;
    public int group = 1;           // how many come at once (the cost covers the whole group)
    public string altName;          // every other member of a group (Huginn and Muninn)
    public Color altTint = Color.white;
    public bool boss;               // a realm's guardian: sized by the realm, not by MobScale
    public int minDepth;            // zones from the start before this kind shows up
    public bool leader;             // once it shows up, one leads every wave (bought before the rest)

    // What it does in a fight, for realms whose waves are composed by role (Realm.composedWaves): the front line that
    // closes in, the bows and casters behind it, and the specialists that fence the floor, empower or multiply
    public enum Role { Front, Tank, Heavy, Ranged, Zone, Support, Splitter }
    public Role role = Role.Front;

    // Ordinary monsters stand this much taller than their models were built
    public const float MobScale = 1.6f;
}

// A floor of the run: one of the Nine Realms, with its look, enemies and guardian boss.
public class Realm
{
    // The journey has four stages; Asgard is the last of them. It is tuned for a player arriving there with three
    // stages behind them, so a run that starts straight in it is handed what those stages would have given.
    public const int StageCount = 4;

    // Scenery styles (DungeonGenerator): Asgard 0, Jotunheim 1, Muspelheim 2, Niflheim 4 (the numbers the realm projects used)
    public const int StyleAsgard = 0, StyleJotunheim = 1, StyleMuspelheim = 2, StyleNiflheim = 4;

    public float groundTint = 0.3f;  // how strongly the ground texture takes the realm's floor colour

    // A realm its team built as a scene of its own (rooms, monsters and boss arena as they made them): played as that
    // scene with this game's player, camera and UI, instead of zones from the generator (see StageHost)
    public string scenePath;
    public HumanoidEnemy.AreaAttack[] bossMoves;   // area moves the guardian adds to its slam (Niflheim: Hel's soul lances and death aura)

    // A second guardian fought at the same time (Niflheim's twin giants), if the realm has one
    public Enemy boss2Prefab;
    public string boss2Name;
    public int boss2Health;
    public float boss2Damage;
    public float boss2Size;
    public Color boss2Tint = Color.white;
    public HumanoidEnemy.AreaAttack[] boss2Moves;

    public string name;
    public string subtitle;
    public int stage = 1;           // which of the journey's stages this realm is
    public int style;               // scenery: 0 golden groves (also the village)
    public bool palace;             // Asgard: marble halls, colonnades and carpets instead of open land
    public Color wallColor, floorColor, doorColor, accentColor;
    public Color lightColor = Color.white;
    public float lightIntensity = 1.2f;
    public Color ambient = new Color(0.25f, 0.25f, 0.28f);
    public Color background = new Color(0.05f, 0.05f, 0.07f);
    public int rooms = 8;
    public float linearity;         // 0: zones branch anywhere; 1: they mostly chain on from the newest (a long way to the boss)
    public int roomBudget = 4;      // enemy cost per wave, grows with distance from the start
    public int[] extraWaveDepths = { 3 };   // a fight zone this many zones in (or further) brings one more wave per entry
    // Waves made to a recipe by role (front line, ranged, specialists — see Room.SpawnComposedWave) instead of bought
    // at random from the budget; the next wave comes in while the last of the one before still stands
    public bool composedWaves;
    public EnemyEntry[] enemies;

    public Enemy bossPrefab;
    public string bossName;
    public int bossHealth;
    public float bossDamage;
    public float bossSize;
    public Color bossTint = Color.white;
    public EnemyEntry bossAdds;     // called in when the boss roars
    public EnemyEntry midBoss;      // waits in the hall before the guardian's arena (Asgard: Týr); none skips the hall

    // Twin mid-guardians fought together in an arena of their own before the guardian's (Niflheim's two giants);
    // grown into bosses like a realm guardian, each with its own area move
    public class Guardian
    {
        public Enemy prefab;
        public string name;
        public int health;
        public float damage, size = 1f;
        public Color tint = Color.white;
        public HumanoidEnemy.AreaAttack[] moves;
    }
    public Guardian[] midTwins;

    public bool HasMidBoss => midBoss != null || (midTwins != null && midTwins.Length > 0);
    public string MidBossTitle => midTwins != null && midTwins.Length > 0
        ? string.Join(" · ", System.Array.ConvertAll(midTwins, g => g.name))
        : midBoss != null ? midBoss.name : "";

    // What a player would carry into this stage from the ones before it (given when a run starts here)
    public int arrivalSkillPoints;
    public int arrivalRunes;
    public int arrivalGold;

    // The waves a fight zone this deep brings
    public int WavesAt(int depth)
    {
        int waves = 1;
        foreach (int d in extraWaveDepths)
            if (depth >= d) waves++;
        return waves;
    }

    // The journey's four stages, in order: 니플헤임 → 요툰헤임 → 무스펠헤임 → 아스가르드. Each realm is built by its own
    // method and left out while its content isn't in the project yet, so a run starts at the first stage that is.
    public static Realm[] CreateAll(HumanoidEnemy einherjar, HumanoidEnemy valkyrie, Enemy raven, Enemy heimdall, Enemy tyr = null)
    {
        var realms = new System.Collections.Generic.List<Realm>();
        // A region's own copy of the game (title, story, the village, F1 — all the same) builds and plays only its own
        // realm: the other realms' content isn't in that copy
        int? only = OnlyStyle;
        if (only == null || only == StyleNiflheim)
            realms.AddRange(NiflheimFloors());
        if (only == null || only == StyleJotunheim)
            AddIf(realms, Jotunheim());
        if (only == null || only == StyleMuspelheim)
            AddIf(realms, Muspelheim());
        if (only == null || only == StyleAsgard)
            AddIf(realms, Asgard(einherjar, valkyrie, raven, heimdall, tyr));
        return realms.ToArray();
    }

    private static void AddIf(System.Collections.Generic.List<Realm> realms, Realm realm)
    {
        if (realm != null)
            realms.Add(realm);
    }

    // Resources/RegionOnly.txt holds the realm's name (Niflheim, Jotunheim, Muspelheim or Asgard); none = every realm
    public static int? OnlyStyle
    {
        get
        {
            var file = Resources.Load<TextAsset>("RegionOnly");
            if (file == null) return null;
            switch (file.text.Trim().ToLowerInvariant())
            {
                case "niflheim": return StyleNiflheim;
                case "jotunheim": return StyleJotunheim;
                case "muspelheim": return StyleMuspelheim;
                case "asgard": return StyleAsgard;
                default: return null;
            }
        }
    }

    // Stage 1: Niflheim, one floor of mist and ice running down to the spring Hvergelmir. The twin giants Hrimgrimnir
    // and Bergelmir hold their arena halfway; Nidhogg gnaws at Yggdrasil's root in its lair at the end. (The Niflheim
    // project had no MobScale: its monsters stood at their prefab size times the entry's size, so the sizes here are
    // divided by MobScale to come out the same.) Hel's model stays in Resources/Bosses for later.
    private static Realm[] NiflheimFloors()
    {
        var draugr = Resources.Load<Enemy>("Mobs/Draugr");
        var slime = Resources.Load<Enemy>("Mobs/Slime");
        var jotunn = Resources.Load<Enemy>("Mobs/Jotunn");
        var nidhogg = Resources.Load<Enemy>("Bosses/Nidhogg");
        var mistGiant = Resources.Load<Enemy>("Bosses/MistGiant");
        var frostGiant = Resources.Load<Enemy>("Bosses/FrostGiant");
        if (draugr == null || slime == null || jotunn == null)
            return new Realm[0];
        var frost = new Color(0.75f, 0.9f, 1.15f);
        var mist = new Color(0.62f, 0.72f, 1.1f);
        const float unscaled = 1f / EnemyEntry.MobScale;
        var niflheim = new Realm
        {
            stage = 1, style = StyleNiflheim, name = "니플헤임", subtitle = "안개와 얼음의 땅 · 흐베르겔미르의 샘",
            wallColor = new Color(0.2f, 0.26f, 0.36f), floorColor = new Color(0.36f, 0.44f, 0.55f),
            doorColor = new Color(0.25f, 0.35f, 0.5f), accentColor = new Color(0.45f, 0.95f, 1f),
            lightColor = new Color(0.7f, 0.85f, 1f), lightIntensity = 1f,
            ambient = new Color(0.26f, 0.3f, 0.38f), background = new Color(0.04f, 0.06f, 0.1f),
            groundTint = 0.7f, rooms = 12, roomBudget = 5,
            enemies = new[]
            {
                new EnemyEntry { prefab = draugr, name = "안개 드라우그", cost = 2, weight = 3, size = unscaled, health = 2.4f, damage = 1.75f, tint = mist, gold = 4 },
                new EnemyEntry { prefab = slime, name = "얼음 슬라임", cost = 1, weight = 2, size = unscaled, health = 3.6f, damage = 1.65f, tint = frost, gold = 2 },
                new EnemyEntry { prefab = jotunn, name = "서리뿌리 거인", cost = 4, weight = 2, size = 1.15f * unscaled, health = 2.2f, damage = 1.7f, tint = mist, gold = 8 },
            },
            // halfway: the mist giant and the frost giant, together in their frozen arena
            // (their own models from Baroque 3D; plain tinted giants stand in if those are missing)
            midTwins = new[]
            {
                new Guardian { prefab = mistGiant != null ? mistGiant : jotunn, name = "안개 거인 흐림그림니르", health = 130, damage = 26f, size = 2.63f,   // 5 m: the models stand 1.9 m at scale 1
                    tint = mistGiant != null ? Color.white : new Color(0.6f, 0.68f, 0.9f), moves = new[] { HumanoidEnemy.AreaAttack.MistBurst, HumanoidEnemy.AreaAttack.PoisonMist, HumanoidEnemy.AreaAttack.MistVeil } },
                new Guardian { prefab = frostGiant != null ? frostGiant : jotunn, name = "서리 거인 베르겔미르", health = 140, damage = 28f, size = 2.63f,   // 5 m
                    tint = frostGiant != null ? Color.white : new Color(0.5f, 0.6f, 0.95f), moves = new[] { HumanoidEnemy.AreaAttack.FrostNova, HumanoidEnemy.AreaAttack.IcicleThrow, HumanoidEnemy.AreaAttack.IcicleField } },
            },
            // the realm's guardian: Nidhogg in its lair at the spring (a giant stands in if its model is missing)
            bossPrefab = nidhogg != null ? nidhogg : jotunn, bossName = "뿌리를 갉는 용 니드호그", bossHealth = 240, bossDamage = 30f,
            bossSize = nidhogg != null ? 1f : 1.95f,
            bossAdds = new EnemyEntry { prefab = draugr, name = "안개 드라우그", cost = 2, size = unscaled, health = 2.2f, damage = 1.7f, tint = mist, gold = 1 },
        };
        return new[] { niflheim };
    }

    // Stage 2: 요툰헤임, the Ironwood (Járnviðr) — the Jotunheim team's dungeon as they built it: nine rooms of fog,
    // moss and rune ruins, a mossy giant halfway, and the Last Guardian's arena behind a gate that has to be found
    public const string JotunheimDungeon = "Assets/Realms/Jotunheim/Scenes/JOT_Dungeon.unity";

    private static Realm Jotunheim()
    {
#if UNITY_EDITOR
        if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(JotunheimDungeon) == null)
            return null;
#endif
        return new Realm
        {
            name = "요툰헤임", subtitle = "철의 숲 · 거인들의 땅", stage = 2, style = StyleJotunheim,
            scenePath = JotunheimDungeon,
            floorColor = new Color(0.2f, 0.24f, 0.18f), wallColor = new Color(0.3f, 0.32f, 0.28f),
            accentColor = new Color(0.35f, 0.9f, 1f), background = new Color(0.11f, 0.14f, 0.13f),
            ambient = new Color(0.18f, 0.22f, 0.2f), lightColor = new Color(0.72f, 0.82f, 0.92f), lightIntensity = 1.15f,
            bossName = "최후의 수호자",
            // What a run starting here would carry from Niflheim
            arrivalSkillPoints = 3, arrivalRunes = 1, arrivalGold = 40,
        };
    }

    // Stage 3: 무스펠헤임 — the Muspelheim team's fire road as they built it: the sanctum and the guardian hall, the
    // temple where the war bull waits, the lava path, and Surtr rising out of the lava pool at its end
    public const string MuspelheimScene = "Assets/Realms/Muspelheim/Scenes/Muspelheim.unity";

    private static Realm Muspelheim()
    {
#if UNITY_EDITOR
        if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(MuspelheimScene) == null)
            return null;
#endif
        return new Realm
        {
            name = "무스펠헤임", subtitle = "불꽃의 땅 · 수르트의 용암길", stage = 3, style = StyleMuspelheim,
            scenePath = MuspelheimScene,
            floorColor = new Color(0.25f, 0.12f, 0.08f), wallColor = new Color(0.2f, 0.12f, 0.1f),
            accentColor = new Color(1f, 0.45f, 0.15f), background = new Color(0.12f, 0.04f, 0.03f),
            ambient = new Color(0.3f, 0.15f, 0.1f), lightColor = new Color(1f, 0.7f, 0.5f), lightIntensity = 1.2f,
            bossName = "수르트",
            // What a run starting here would carry from the two stages before
            arrivalSkillPoints = 6, arrivalRunes = 2, arrivalGold = 80,
        };
    }

    // Stage 4: 아스가르드, built around the enemy models in the project (the einherjar, valkyrie and raven)
    private static Realm Asgard(HumanoidEnemy einherjar, HumanoidEnemy valkyrie, Enemy raven, Enemy heimdall, Enemy tyr)
    {
        if (einherjar == null || valkyrie == null || raven == null)
            return null;

        // Stage 4's monsters: as tough as the old fourth realm's (twice the health, half again the damage). Each kind
        // has its own job, so a wave is a puzzle of who to cut down first: einherjar and rune archers hold the first
        // halls (a front line and the bows behind it); from one zone in valkyries, rune wisps and the golden boar
        // Gullinbursti join (a charger to pin the player, wisps to harry them from the air); from two in the seidr
        // mages (who empower everything round them) and the ravens; and from three in the einherjar champions —
        // bigger, gilded and far tougher — lead the waves.
        // The last stage, tuned to be hard for a fully built run: tough, hard-hitting, and quick to strike (the monsters'
        // own timings are tightened on their prefabs too)
        const float health = 3.5f, damage = 1.9f;
        const int goldBonus = 2;
        var gilded = new Color(1.15f, 0.95f, 0.6f);
        var archer = Resources.Load<Enemy>("Mobs/RuneArcher");
        var boar = Resources.Load<Enemy>("Mobs/Gullinbursti");
        var wisp = Resources.Load<Enemy>("Mobs/RuneWisp");
        var mage = Resources.Load<Enemy>("Mobs/SeidrMage");
        var skia = Resources.Load<Enemy>("Mobs/Skia");
        var hrimthyr = Resources.Load<Enemy>("Mobs/Hrimthyr");
        var bind = Resources.Load<Enemy>("Mobs/Bind");
        var gladion = Resources.Load<Enemy>("Mobs/Gladion");
        var wallGuard = Resources.Load<Enemy>("Mobs/WallGuardian");
        var bell = Resources.Load<Enemy>("Mobs/HeavenBell");
        var lumen = Resources.Load<Enemy>("Mobs/Lumen");
        var solaris = Resources.Load<Enemy>("Mobs/Solaris");
        var oracle = Resources.Load<Enemy>("Mobs/Oracle");
        var list = new System.Collections.Generic.List<EnemyEntry>
        {
            new EnemyEntry { prefab = einherjar, name = "에인헤랴르", cost = 2, weight = 3, group = 2, health = 0.8f * health, damage = 0.7f * damage, gold = 2 + goldBonus, role = EnemyEntry.Role.Front },
            new EnemyEntry { prefab = valkyrie, name = "발키리", cost = 3, weight = 2, health = health, damage = 0.8f * damage, gold = 4 + goldBonus, minDepth = 2, role = EnemyEntry.Role.Front },
            new EnemyEntry { prefab = raven, name = "후긴", cost = 3, weight = 2, group = 2, health = health, damage = 0.8f * damage, gold = 3 + goldBonus,
                altName = "무닌", altTint = new Color(0.22f, 0.2f, 0.28f), minDepth = 2, role = EnemyEntry.Role.Ranged },
            new EnemyEntry { prefab = einherjar, name = "에인헤랴르 용사", cost = 5, weight = 1, size = 1.3f, health = 2.6f * health, damage = 1.0f * damage,
                tint = gilded, gold = 10 + goldBonus, minDepth = 4, leader = true, role = EnemyEntry.Role.Front },
        };
        if (archer != null)
            list.Add(new EnemyEntry { prefab = archer, name = "룬 궁수", cost = 3, weight = 3, group = 2, health = 0.6f * health, damage = 0.7f * damage, gold = 3 + goldBonus, role = EnemyEntry.Role.Ranged });
        if (wisp != null)
            list.Add(new EnemyEntry { prefab = wisp, name = "룬 정령", cost = 3, weight = 2, group = 2, health = 0.7f * health, damage = 0.8f * damage, gold = 3 + goldBonus, minDepth = 2, role = EnemyEntry.Role.Ranged });
        if (boar != null)
            list.Add(new EnemyEntry { prefab = boar, name = "굴린부르스티", cost = 4, weight = 2, health = 1.2f * health, damage = 0.9f * damage, gold = 6 + goldBonus, minDepth = 3, role = EnemyEntry.Role.Front });
        if (mage != null)
            list.Add(new EnemyEntry { prefab = mage, name = "세이드 마도사", cost = 4, weight = 2, health = 0.9f * health, damage = 0.8f * damage, gold = 6 + goldBonus, minDepth = 4, role = EnemyEntry.Role.Support });
        // 빛의 감시자: a floating eye that burns lines through the hall from a distance
        if (skia != null)
            list.Add(new EnemyEntry { prefab = skia, name = "빛의 감시자 스키아", cost = 3, weight = 3, health = 0.8f * health, damage = 0.8f * damage, gold = 4 + goldBonus, minDepth = 1, role = EnemyEntry.Role.Ranged });
        // 별의 거인: a titan two or three times a man's height (prefab 2.5x, so 0.9 here comes to about 5.8 m)
        if (hrimthyr != null)
            // (one titan is a fight of its own: dearer, and only from the later halls, so it doesn't turn every wave into it)
            list.Add(new EnemyEntry { prefab = hrimthyr, name = "별의 거인 흐림투르", cost = 7, weight = 3, size = 0.9f, health = 1.65f * health, damage = 0.9f * damage, gold = 14 + goldBonus, minDepth = 5, role = EnemyEntry.Role.Heavy });
        // 속박의 사슬: floating knot of golden chains that binds, cages and impales from the floor
        if (bind != null)
            list.Add(new EnemyEntry { prefab = bind, name = "속박의 사슬 바인드", cost = 3, weight = 3, health = 0.9f * health, damage = 0.8f * damage, gold = 4 + goldBonus, minDepth = 3, role = EnemyEntry.Role.Zone });
        // 신검 파편: a wielderless holy sword that darts and whirls (melee chaser)
        if (gladion != null)
            list.Add(new EnemyEntry { prefab = gladion, name = "신검 파편 글라디온", cost = 3, weight = 3, health = 0.8f * health, damage = 0.8f * damage, gold = 4 + goldBonus, minDepth = 1, role = EnemyEntry.Role.Front });
        // 성벽 수호병: a shield wall in front of the others — blocks frontal blows, shoves, slams and charges
        if (wallGuard != null)
            list.Add(new EnemyEntry { prefab = wallGuard, name = "성벽 수호병", cost = 4, weight = 3, health = 1.3f * health, damage = 0.8f * damage, gold = 6 + goldBonus, minDepth = 3, role = EnemyEntry.Role.Tank });
        // 천상의 종: rings of sound that fence the player in from a distance
        if (bell != null)
            list.Add(new EnemyEntry { prefab = bell, name = "천상의 종", cost = 3, weight = 3, health = 0.9f * health, damage = 0.8f * damage, gold = 4 + goldBonus, minDepth = 3, role = EnemyEntry.Role.Zone });
        // 분열하는 빛: splits 1 → 2 → 4 as it is hurt; the last little lights dart in and burst
        if (lumen != null)
            list.Add(new EnemyEntry { prefab = lumen, name = "분열하는 빛 루멘", cost = 3, weight = 3, health = 0.9f * health, damage = 0.7f * damage, gold = 8 + goldBonus, minDepth = 4, role = EnemyEntry.Role.Splitter });
        // 룬 회전체: golden rune discs that orbit, whirl, and fly out and back
        if (solaris != null)
            list.Add(new EnemyEntry { prefab = solaris, name = "룬 회전체 솔라리스", cost = 3, weight = 3, health = 0.9f * health, damage = 0.8f * damage, gold = 4 + goldBonus, minDepth = 2, role = EnemyEntry.Role.Front });
        // 오라클: reads the player's movement and casts where they are about to be
        if (oracle != null)
            list.Add(new EnemyEntry { prefab = oracle, name = "오라클", cost = 3, weight = 3, health = 0.9f * health, damage = 0.8f * damage, gold = 4 + goldBonus, minDepth = 4, role = EnemyEntry.Role.Zone });
        var mobs = list.ToArray();

        return new Realm
        {
            name = "아스가르드", subtitle = "신들의 땅 · 마지막 시험", palace = true, stage = 4,
            wallColor = new Color(0.5f, 0.45f, 0.36f), floorColor = new Color(0.36f, 0.33f, 0.42f),
            doorColor = new Color(0.7f, 0.55f, 0.2f), accentColor = new Color(1f, 0.8f, 0.35f),
            lightColor = new Color(1f, 0.94f, 0.82f), lightIntensity = 1.3f,
            ambient = new Color(0.32f, 0.3f, 0.36f), background = new Color(0.06f, 0.05f, 0.1f),
            // Start, shop, treasure, Týr's battleground, Heimdall's arena and six fight halls
            // laid out as a long road with a few side halls, so the fights grow as the way goes on
            rooms = 11, linearity = 0.65f, roomBudget = 6, extraWaveDepths = new[] { 2, 4 },
            enemies = mobs, composedWaves = true,
            // Heimdall when his model is in the project, otherwise a valkyrie captain stands guard
            bossPrefab = heimdall != null ? heimdall : valkyrie,
            bossName = heimdall != null ? "헤임달" : "발키리의 대장 브륀힐드",
            bossHealth = 240, bossDamage = 24f, bossSize = 1.6f,
            bossAdds = new EnemyEntry { prefab = einherjar, name = "에인헤랴르", cost = 2, health = 0.8f * health, damage = 0.7f * damage, gold = 1 },
            // Týr, god of war and oaths, guards the battleground before Heimdall's arena
            midBoss = tyr != null ? new EnemyEntry { prefab = tyr, name = "티르", boss = true, size = 1.4f, gold = 40 } : null,
            // Three stages' worth: about ten levels of skill points, a rune from each guardian, and the gold left over
            arrivalSkillPoints = 10, arrivalRunes = 3, arrivalGold = 120,
        };
    }

}
