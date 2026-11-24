using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

// Runs the roguelike: descend through the realms room by room, collect runes, beat each realm's guardian.
// Every journey starts and ends in the Midgard village. Dying ends the run and everything in it
// (runes, gold, health, level) is lost, but each level reached becomes a forge token for Brokkr's upgrades.
public partial class GameManager : MonoBehaviour
{
    public enum State { Playing, Paused, Dead, Victory }

    private const string BestRealmKey = "rogue_best_realm";
    private const string WinsKey = "rogue_wins";

    public static GameManager Instance { get; private set; }

    // A realm can turn off the floating damage numbers over the monsters while it is being played (Muspelheim does)
    public static bool HideHitNumbers;
    public static bool IsRunActive => Instance == null || (Instance.state == State.Playing && !Instance.travelling && !BossCutscene.Playing && !OpeningCutscene.Playing && !TitleScreen.Playing && !SkillSystem.Blocking);
    public static bool IsFreeRoam => IsRunActive;

    [SerializeField] private PlayerStats player;
    [SerializeField] private DungeonGenerator dungeon;
    [SerializeField] private Light sunLight;
    [SerializeField] private CameraFollow cameraFollow;
    [SerializeField] private HubVillage hub;

    [Header("Enemy models (Asgard)")]
    [SerializeField] private HumanoidEnemy einherjarPrefab;
    [SerializeField] private HumanoidEnemy valkyriePrefab;
    [SerializeField] private Enemy ravenPrefab;
    [SerializeField] private Enemy heimdallPrefab;
    [SerializeField] private Enemy tyrPrefab;          // Asgard's mid-boss, in the hall before Heimdall's arena

    private Realm[] realms;
    private State state;
    private int realmIndex;
    private Room currentRoom;
    private bool travelling;
    private float fade; // black overlay while moving between zones
    private Room bossRoom;
    private bool inHub;               // in the village between runs
    private bool nearForge;
    private int tokensEarned;         // shown on the run-over screen
    private Portal portal;
    private PlayerMiner miner;
    private DualBlades blades;        // the twin blades, when the player carries them
    private CharacterSwitcher switcher;
    private bool nearArmory;
    private bool nearTraining;   // the village's training portal

    // ── 훈련장 from the village: a run is set up behind the scenes, the training room opens, and leaving it comes
    // back to the village (no tokens, nothing carried) ──
    public bool TrainingFromVillage { get; private set; }

    public void EnterTrainingFromVillage()
    {
        if (!inHub || travelling) return;
        StartCoroutine(TrainFromVillage());
    }

    private System.Collections.IEnumerator TrainFromVillage()
    {
        StartRun();
        TrainingFromVillage = true;   // (set after: a fresh run clears it)
        // (wait for the run's arrival — its fade, any cutscene — to settle before stepping into the arena)
        float waited = 0f;
        while ((travelling || fade > 0.01f || BossCutscene.Playing || StoryStage.Cinematic) && waited < 8f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return null;
        DebugCheats.UsedThisRun = true;   // (a training run earns nothing)
        TrainingRoom.Enter();
    }

    public void LeaveTrainingToVillage()
    {
        TrainingFromVillage = false;
        EnterHub();
    }
    private float damageFlash;
    private float hpBarFlash;   // the HP bar blinks red each time health is lost (a hit, a tick of poison)
    private float hpGhost = 1f, hpGhostHold;   // the share of health just lost, held on the bar a moment before it drains
    private const float HpBarFlashTime = 0.45f;
    private bool confirmGiveUp;
    private enum Panel { Menu, Skills, Test, Forge, Map, Armory, Route }
    private Panel panel;              // what the pause screen is showing
    private bool showRunes;
    private RunePedestal nearbyPedestal;
    private string banner, bannerSub;
    private float bannerUntil;
    private readonly List<(string text, Color color, float until)> toasts = new List<(string, Color, float)>();
    private readonly List<(Vector3 world, string text, Color color, float born)> floaters = new List<(Vector3, string, Color, float)>();
    private GUIStyle titleStyle, textStyle, textStyleBold, smallStyle, smallStyleLeft, centerStyle, centerStyleBold, smallCenterStyle, floaterStyle, mapStyle, buttonStyle, skillTextStyle;

    public State CurrentState => state;
    public Realm CurrentRealm => realms[realmIndex];
    public Room CurrentRoom => currentRoom;
    // 0 a run under way, 1 fallen, 2 won (the music follows it)
    public int RunOutcome => state == State.Dead ? 1 : state == State.Victory ? 2 : 0;

    private void Awake()
    {
        Instance = this;
        if (tyrPrefab == null)
            tyrPrefab = Resources.Load<Enemy>("Bosses/Tyr");
        realms = Realm.CreateAll(einherjarPrefab, valkyriePrefab, ravenPrefab, heimdallPrefab, tyrPrefab);
        miner = player.GetComponent<PlayerMiner>();
        blades = player.GetComponent<DualBlades>();
        switcher = player.GetComponent<CharacterSwitcher>();
        // (realm scenes built by other teams find the player by its tag before anything else runs)
        player.tag = "Player";
        if (cameraFollow == null && Camera.main != null)
            cameraFollow = Camera.main.GetComponent<CameraFollow>();
        if (hub == null)
            hub = FindAnyObjectByType<HubVillage>();
    }

    public bool InHub => inHub;

    private void OnEnable()
    {
        Room.RoomCleared += OnRoomCleared;
        Room.FightStarted += OnFightStarted;
        Enemy.AnyDied += OnEnemyDied;
        RunState.Changed += OnRunChanged;
        RunState.LeveledUp += OnLeveledUp;
        DualBlades.Announce += Floater;
    }

    private void OnDisable()
    {
        Room.RoomCleared -= OnRoomCleared;
        Room.FightStarted -= OnFightStarted;
        Enemy.AnyDied -= OnEnemyDied;
        RunState.Changed -= OnRunChanged;
        RunState.LeveledUp -= OnLeveledUp;
        DualBlades.Announce -= Floater;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        Time.timeScale = 1f;
    }

    private void Start()
    {
        // the plain fighter's lightning kit (Q 연쇄 번개, E 번개 강타, R 휠윈드)
        if (player.GetComponent<StormSkills>() == null)
            player.gameObject.AddComponent<StormSkills>();
        player.Died += OnPlayerDied;
        player.Damaged += () =>
        {
            damageFlash = 0.16f;   // (the red at the screen's edges does most of it now: PlayerHurtFeel)
            hpBarFlash = HpBarFlashTime;
            hpGhostHold = Time.unscaledTime + 0.45f;
            Vector3 chest = player.transform.position + Vector3.up * 0.4f;
            Vfx.Flash(chest, 1.4f, new Color(1f, 0.3f, 0.25f), 0.14f);
            Vfx.Burst(chest, new Color(1f, 0.35f, 0.3f), 8, 4f, 0.12f);
        };
        player.Avoided += text => Floater(player.transform.position + Vector3.up * 2f, text, new Color(0.7f, 0.9f, 1f));
        miner.ChargeBroken += () => Floater(player.transform.position + Vector3.up * 2f, "경직!", new Color(1f, 0.5f, 0.4f));
        miner.HitEnemy += (enemy, damage, critical) =>
        {
            if (HideHitNumbers) return;
            Floater(enemy.transform.position + Vector3.up * (1.5f * enemy.transform.localScale.y), critical ? damage + "!" : damage.ToString(),
                critical ? new Color(1f, 0.8f, 0.2f) : Color.white);
        };
        if (hub != null)
            EnterHub();
        else
            StartRun();
        // The title screen 나인 월드 first; 시작하기 goes on to the opening 「이그드라실의 몰락」 (when it's on), whose altar
        // choice starts the journey; 오프닝 보기 always plays it
        TitleScreen.Show(() => { if (OpeningCutscene.AutoPlay) PlayOpening(); }, PlayOpening);
    }

    public void PlayOpening()
    {
        OpeningCutscene.Play(switcher,
            name =>
            {
                GameObject art = dungeon != null ? dungeon.Prop(name) : null;
                return art != null ? art : hub != null ? hub.Prop(name) : null;
            },
            new Enemy[] { einherjarPrefab, valkyriePrefab, ravenPrefab, heimdallPrefab },
            sunLight,
            picked =>
            {
                if (switcher != null) switcher.Equip(picked);
                // the weapon chosen: into the village (forge, armory, the Bifrost gate) rather than straight into a run
                if (!inHub) EnterHub();
            });
    }

    // The village: no run in progress, the forge open, the Bifrost gate waiting to the north
    public void EnterHub()
    {
        foreach (Enemy enemy in Enemy.Alive.ToArray())
            if (enemy != null) Destroy(enemy.gameObject);
        foreach (Pickup pickup in FindObjectsByType<Pickup>())
            Destroy(pickup.gameObject);
        if (portal != null) Destroy(portal.gameObject);
        LeaveSceneRealm();
        dungeon.Clear();
        currentRoom = bossRoom = null;
        travelling = false;
        fade = 0f;

        inHub = true;
        RunState.Reset();
        foreach (SkillTree tree in AllTrees)
            tree.Reset(1);
        LearnAllSkillsForTesting();
        miner.ResetCombat();
        player.GetComponent<StormSkills>()?.ResetCombat();
        if (blades != null) blades.ResetCombat();
        ArmoryWeapon.ResetAll(player.gameObject);
        player.RestoreHealth();
        hub.Show(player.transform, StartRun);
        hub.ApplyAtmosphere(sunLight, Camera.main);
        Teleport(hub.SpawnPosition, hub.SpawnRotation);
        ShowBanner("미드가르드 · 시작의 마을", "브로크의 대장간에서 무기를 벼리고, 북쪽 비프로스트로 여정을 떠나세요");

        state = State.Playing;
        Time.timeScale = 1f;
        RelockCursor();
    }

    // A brand-new run from the first realm with nothing carried over (the forge's upgrades aside)
    public void StartRun() => StartRunAt(0);

    // A new run that begins on a later floor (the test panel's floor buttons): it is handed what the floors before it
    // would have given (GiveArrivalKit), as a run starting there always is
    private void StartRunAt(int startAt)
    {
        startAt = Mathf.Clamp(startAt, 0, realms.Length - 1);
        TrainingFromVillage = false;
        if (hub != null)
            hub.Hide();
        inHub = false;
        RunState.Reset();
        RunState.AddGold(MetaProgress.StartGold);
        DebugCheats.UsedThisRun = false;
        foreach (SkillTree tree in AllTrees)
            tree.Reset(1);   // one point to start with, so the first skill can be learned right away
        miner.ResetCombat();
        player.GetComponent<StormSkills>()?.ResetCombat();
        if (blades != null) blades.ResetCombat();
        ArmoryWeapon.ResetAll(player.gameObject);
        realmIndex = startAt;
        GiveArrivalKit(realms[startAt]);
        foreach (SkillTree tree in AllTrees)
            tree.LearnAll();   // (Q / E / R / right click are all there from the start)
        RunState.Refresh();
        player.RestoreHealth();   // (after the kit and the skills, which may raise the maximum)
        EnterRealm(startAt);
    }

    // Test panel: straight to a floor — from the village a fresh run starting there, mid-run the floor itself
    private void TestGoToFloor(int index)
    {
        if (travelling || index < 0 || index >= realms.Length)
            return;
        Resume();
        Time.timeScale = 1f;
        DebugCheats.UsedThisRun = true;
        if (inHub)
            StartRunAt(index);
        else
            EnterRealm(index);
        DebugCheats.UsedThisRun = true;   // (a fresh run clears the mark: this one used the test panel)
    }

    // Test panel: to the floor (if not already on it), then, once it is up, right at its guardian — the same on every
    // floor: this game's halls travel to the guardian's zone, a realm scene uses its own jump (StageHost)
    // (kind: the guardian's zone, or the mid-boss's)
    private void TestGoToBoss(int index, Room.Kind kind = Room.Kind.Boss)
    {
        if (travelling) return;
        bool there = !inHub && realmIndex == index;
        if (!there) TestGoToFloor(index);
        else { Resume(); Time.timeScale = 1f; DebugCheats.UsedThisRun = true; }
        StartCoroutine(JumpToBossWhenReady(index, kind));
    }

    private System.Collections.IEnumerator JumpToBossWhenReady(int index, Room.Kind kind)
    {
        // (a realm's host sets its hooks a frame or two after its scene has loaded)
        for (float t = 0f; t < 20f; t += Time.unscaledDeltaTime)
        {
            if (!travelling && !inHub && realmIndex == index && (!StageHost.Hosted || StageHost.TestJumpToBoss != null))
            {
                yield return null;
                TravelToZone(kind);
                yield break;
            }
            yield return null;
        }
    }

    // The test panel's row of floor buttons, one per realm in order; the floor the player is on is lit
    private GUIStyle floorButtonStyle;

    private float FloorButtons(Rect box, float y)
    {
        GUI.Label(new Rect(box.x, y, box.width, 20f), inHub ? "층 이동 — 그 층에서 바로 여정 시작" : "층 이동", smallCenterStyle);
        y += 22f;
        float gap = 6f;
        // One column per floor, the same for every floor: the floor itself, and under it straight to its guardian
        int count = realms.Length;
        float w = Mathf.Min(140f, (box.width - 48f - gap * (count - 1)) / count);
        float x = box.center.x - (w * count + gap * (count - 1)) / 2f;
        floorButtonStyle ??= new GUIStyle(buttonStyle);
        for (int i = 0; i < realms.Length; i++)
        {
            bool here = !inHub && i == realmIndex;
            GUI.color = here ? new Color(1f, 0.75f, 0.35f) : Color.white;
            string label = $"{i + 1}. {realms[i].name}";
            // (a long name gets a smaller font so it fits the button)
            floorButtonStyle.fontSize = buttonStyle.fontSize;
            while (floorButtonStyle.fontSize > 9 && floorButtonStyle.CalcSize(new GUIContent(label)).x > w - 4f)
                floorButtonStyle.fontSize--;
            if (UiSound.Button(new Rect(x + i * (w + gap), y, w, 32f), label, floorButtonStyle) && !here)
            {
                GUI.color = Color.white;
                TestGoToFloor(i);
                return y + 78f;
            }
            GUI.color = new Color(1f, 0.6f, 0.45f);
            floorButtonStyle.fontSize = Mathf.Max(9, buttonStyle.fontSize - 2);
            if (UiSound.Button(new Rect(x + i * (w + gap), y + 36f, w, 28f), "→ 수문장", floorButtonStyle))
            {
                GUI.color = Color.white;
                TestGoToBoss(i);
                return y + 110f;
            }
            GUI.color = new Color(1f, 0.85f, 0.5f);
            if (UiSound.Button(new Rect(x + i * (w + gap), y + 68f, w, 28f), "→ 중간보스", floorButtonStyle))
            {
                GUI.color = Color.white;
                TestGoToBoss(i, Room.Kind.MidBoss);
                return y + 110f;
            }
        }
        GUI.color = Color.white;
        return y + 110f;
    }

    // Test mode (F1): both weapons' skill trees start fully learned, every node at its last rank
    private static void LearnAllSkillsForTesting()
    {
        if (!DebugCheats.AllSkillsOnStart)
            return;
        foreach (SkillTree tree in AllTrees)
            tree.LearnAll();
    }

    // A run that starts in a later stage (Asgard is the fourth of four) is handed what the stages before it would
    // have given: skill points for both weapons, a guardian's rune per stage and some gold. Levels aren't granted,
    // so forge tokens still only count the levels reached in this run.
    private void GiveArrivalKit(Realm realm)
    {
        if (realm.stage <= 1)
            return;
        for (int i = 0; i < realm.arrivalSkillPoints; i++)
        {
            foreach (SkillTree tree in AllTrees)
                tree.AddPoint();
        }
        // (runes are set aside while the skill system owns the growth)
        for (int i = 0; i < realm.arrivalRunes && !SkillSystem.RunesOff; i++)
        {
            Rune rune = RuneLibrary.RandomUnowned(RunState.Runes);
            if (rune != null)
                RunState.AddRune(rune);
        }
        RunState.AddGold(realm.arrivalGold);
    }

    private static string StageLabel(Realm realm) => $"스테이지 {realm.stage}/{Realm.StageCount} · {realm.name}";

    private void EnterRealm(int index)
    {
        realmIndex = index;
        Realm realm = CurrentRealm;
        foreach (Enemy enemy in Enemy.Alive.ToArray())
            if (enemy != null) Destroy(enemy.gameObject);
        foreach (Pickup pickup in FindObjectsByType<Pickup>())
            Destroy(pickup.gameObject);
        if (portal != null) Destroy(portal.gameObject);
        bossRoom = null;
        LeaveSceneRealm();
        if (realm.scenePath != null)
        {
            EnterSceneRealm(realm, index);
            return;
        }

        // Asgard is walked as a route of areas (갈림길): the starting area first
        if (Routed(realm))
        {
            Route.Reset();
            dungeon.GenerateArea(realm, index, player.transform, Route.ChainFor(RouteTheme.Start), 0);
        }
        else
            dungeon.Generate(realm, index, player.transform);
        if (sunLight != null)
        {
            sunLight.color = realm.lightColor;
            sunLight.intensity = realm.lightIntensity;
        }
        RenderSettings.ambientLight = realm.ambient;

        // The land fades into a realm-tinted haze, and the sky is that same haze: no edge to the world
        // Asgard hangs in the cosmos: its haze leans toward a deep nebula violet (the other realms keep their own colour)
        Color haze = Color.Lerp(realm.floorColor, realm.background, 0.45f);
        if (realm.palace)
            haze = Color.Lerp(haze, new Color(0.09f, 0.06f, 0.2f), 0.6f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = haze;
        RenderSettings.fogStartDistance = 13f;
        RenderSettings.fogEndDistance = 30f;
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = haze;
        }

        travelling = false;
        fade = 0f;
        currentRoom = dungeon.StartRoom;
        Teleport(currentRoom.transform.position + new Vector3(0f, 1.1f, -2f), Quaternion.identity);
        currentRoom.Enter();
        if (index > 0)
            player.Heal(player.MaxHealth * 0.3f); // a breather between realms
        ShowBanner(StageLabel(realm), realm.subtitle);
        int best = PlayerPrefs.GetInt(BestRealmKey, 0);
        if (index + 1 > best && !DebugCheats.Tainted)
        {
            PlayerPrefs.SetInt(BestRealmKey, index + 1);
            PlayerPrefs.Save();
        }

        state = State.Playing;
        Time.timeScale = 1f;
        RelockCursor();
    }

    // A realm built as its own scene (Jotunheim, Muspelheim): its rooms and bosses as its team made them, our player,
    // camera and HUD (see StageHost). The scene places the player itself, or we put them where its own player stood.
    private void EnterSceneRealm(Realm realm, int index)
    {
        dungeon.Clear();
        currentRoom = null;
        if (sunLight != null)
            sunLight.gameObject.SetActive(false);   // the scene brings its own light
        travelling = true;
        fade = 1f;
        StageHost.Begin(this);
        StageHost.Banner = (title, sub) => ShowBanner(title, sub);
        StageHost.Cleared = OnSceneRealmCleared;
        StageHost.InnerLoaded = () => OnSceneLoaded(CurrentRealm, realmIndex, false);
        StageHost.Load(realm.scenePath, () => OnSceneLoaded(realm, index, true));
        state = State.Playing;
        Time.timeScale = 1f;
        RelockCursor();
    }

    // A realm scene (the first, or the next inside the realm) has been loaded
    private void OnSceneLoaded(Realm realm, int index, bool arriving)
    {
        if (StageHost.Spawn is Vector3 spawn)
            Teleport(spawn + Vector3.up * 0.2f, Quaternion.identity);
        if (cameraFollow != null)
        {
            cameraFollow.Release();
            cameraFollow.Snap();
        }
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fog ? RenderSettings.fogColor : realm.background;
        }
        travelling = false;
        fade = 0f;
        if (!arriving)
            return;
        if (index > 0)
            player.Heal(player.MaxHealth * 0.3f);   // a breather between realms
        ShowBanner(StageLabel(realm), realm.subtitle);
    }

    // The scene realm's guardian has fallen: its rune, and the Bifrost on to the next realm, beside the player
    private void OnSceneRealmCleared()
    {
        bool lastRealm = realmIndex >= realms.Length - 1;
        ShowBanner($"{CurrentRealm.bossName} 격파!", lastRealm ? "마지막 시험을 넘었다 · 비프로스트를 건너 발할라로" : "비프로스트가 다음 세계로 열렸다");
        var spot = new GameObject("RealmSpoils").transform;
        spot.position = new Vector3(player.transform.position.x, player.transform.position.y - 1.1f, player.transform.position.z);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(spot.gameObject, StageHost.Current);
        Rune reward = RuneLibrary.RandomUnowned(RunState.Runes);
        if (reward != null)
            RunePedestal.Create(spot, player.transform.right * -3f + player.transform.forward * 2f, reward, 0);
        portal = Portal.Create(spot.position + player.transform.right * 3f + player.transform.forward * 2f, player.transform, lastRealm ? (System.Action)Win : NextRealm);
    }

    // Out of a realm scene (on to the next realm, back to the village): it is unloaded and the main scene's light returns
    private void LeaveSceneRealm()
    {
        if (!StageHost.Hosted)
            return;
        StageHost.End();
        if (sunLight != null)
            sunLight.gameObject.SetActive(true);
    }

    private void Teleport(Vector3 position, Quaternion rotation)
    {
        var controller = player.GetComponent<CharacterController>();
        controller.enabled = false;
        player.transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;
        Physics.SyncTransforms();
        if (cameraFollow != null)
        {
            // Shut the camera in the zone the player is now in (the village has no walls)
            if (currentRoom != null)
                cameraFollow.Enclose(currentRoom.transform.position, currentRoom.Size.x / 2f, currentRoom.Size.y / 2f,
                    DungeonGenerator.WallSetback, DungeonGenerator.WallHeight - 0.5f);
            else
                cameraFollow.Release();
            cameraFollow.Snap();
        }
    }

    // The training room (TrainingRoom): out to its arena with the camera free, and back into the zone left
    public void TeleportPlayer(Vector3 position, Quaternion rotation, bool freeCamera)
    {
        Teleport(position, rotation);
        if (freeCamera && cameraFollow != null)
        {
            cameraFollow.Release();
            cameraFollow.Snap();
        }
    }

    // Whether the player may step out to the training room now (in a run, between fights, not travelling)
    public bool CanTrain => !inHub && !travelling && !StageHost.Hosted && state != State.Dead && state != State.Victory
        && (currentRoom == null || !currentRoom.IsFighting);

    // Walking off a zone's exit: fade out, swap to the neighbouring zone, arrive at its matching exit, fade in
    public void Travel(Room from, Vector2Int side)
    {
        if (travelling || !from.Exits.TryGetValue(side, out Room to))
            return;
        StartCoroutine(TravelTo(to, -side));
    }

    private IEnumerator TravelTo(Room to, Vector2Int arriveSide)
    {
        travelling = true;
        GameAudio.Play("portal_enter", null, 0.45f, 1.15f);
        for (; fade < 1f; fade += Time.unscaledDeltaTime / 0.25f)
            yield return null;
        fade = 1f;

        dungeon.Activate(to);
        currentRoom = to;
        Vector3 inward = -new Vector3(arriveSide.x, 0f, arriveSide.y);
        Teleport(to.EntrancePoint(arriveSide) + Vector3.up * 1.1f, Quaternion.LookRotation(inward));
        yield return null;

        for (; fade > 0f; fade -= Time.unscaledDeltaTime / 0.3f)
            yield return null;
        fade = 0f;
        travelling = false;
        to.Enter();
    }

    // alternate: the second of a pair (Muninn to Huginn) with its own name and colour
    public Enemy SpawnEnemy(EnemyEntry entry, Vector3 position, Room room, bool alternate = false)
    {
        Enemy enemy = Instantiate(entry.prefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), room.transform);
        float size = entry.size * (entry.boss ? 1f : EnemyEntry.MobScale);
        enemy.transform.localScale = entry.prefab.transform.localScale * size;
        if (!entry.boss && enemy is HumanoidEnemy humanoid)
            humanoid.ScaleReach(size);
        enemy.Scale(entry.health, entry.damage);
        bool alt = alternate && !string.IsNullOrEmpty(entry.altName);
        Color tint = alt ? entry.altTint : entry.tint;
        if (tint != Color.white)
            enemy.SetBaseTint(tint);
        enemy.DisplayName = alt ? entry.altName : entry.name;
        enemy.GoldDrop = entry.gold;
        // Experience follows how much of a wave's budget it takes (a group shares its cost)
        enemy.XpDrop = Mathf.Max(2, Mathf.RoundToInt(entry.cost * 4f / Mathf.Max(1, entry.group)));
        enemy.HomeRoom = room;
        // (zone monsters stay on the floor and out of the scenery, where they can be hit; guardians move as scripted)
        if (!entry.boss)
            ZoneLeash.Attach(enemy);
        return enemy;
    }

    private void OnFightStarted(Room room)
    {
        if (room.IsMidBossFight)
        {
            // the mid-boss's health bar shows like a guardian's; Týr also calls his phase and warns of his big moves
            bossRoom = room;
            if (room.Boss is TyrBoss tyr)
            {
                tyr.PhaseChanged += (title, subtitle) => ShowBanner(title, subtitle);
                tyr.Warned += text => Toast(text, new Color(1f, 0.55f, 0.4f));
            }
            return;
        }
        if (room.RoomKind != Room.Kind.Boss)
            return;
        bossRoom = room;
        // Heimdall announces his phases and warns of his big moves
        if (room.Boss is HeimdallBoss heimdall)
        {
            heimdall.PhaseChanged += (title, subtitle) => ShowBanner(title, subtitle);
            heimdall.Warned += text => Toast(text, new Color(1f, 0.85f, 0.45f));
        }
    }

    private void OnRoomCleared(Room room)
    {
        if (room.IsMidBossFight)
        {
            // The war god's rune, and the way on to the guardian
            ShowBanner($"{CurrentRealm.MidBossTitle} 격파!", (CurrentRealm.palace ? "맹세의 전당이 열렸다" : "길이 열렸다") + " · 수문장이 기다린다");
            // 보스 특수 강화: the beaten boss leaves an altar that changes how one skill works
            SkillToken.Create(room.transform, new Vector3(-1.5f, 0f, -2f), SkillToken.Kind.Special, 0);
            SkillToken.Create(room.transform, new Vector3(1.5f, 0f, -2f), SkillToken.Kind.Reroll, 0);
            return;
        }
        if (room.RoomKind != Room.Kind.Boss)
            return;

        bool lastRealm = realmIndex >= realms.Length - 1;
        ShowBanner(CurrentRealm.boss2Prefab != null ? $"{CurrentRealm.bossName} · {CurrentRealm.boss2Name} 격파!" : $"{CurrentRealm.bossName} 격파!", lastRealm ? "마지막 시험을 넘었다 · 비프로스트를 건너 발할라로" : "비프로스트가 다음 세계로 열렸다");
        // The guardian's special upgrade (보스 특수 강화), and the way on
        SkillToken.Create(room.transform, new Vector3(-3f, 0f, 0f), SkillToken.Kind.Special, 0);
        portal = Portal.Create(room.transform.position + new Vector3(3f, 0f, 0f), player.transform, lastRealm ? (System.Action)Win : NextRealm);
    }

    private void NextRealm() => EnterRealm(realmIndex + 1);

    private void Win()
    {
        if (!DebugCheats.Tainted)
        {
            PlayerPrefs.SetInt(WinsKey, PlayerPrefs.GetInt(WinsKey, 0) + 1);
            PlayerPrefs.Save();
        }
        EndRun(State.Victory);
    }

    private void OnEnemyDied(Enemy enemy)
    {
        RunState.Kills++;
        if (RunState.Stats.healOnKill > 0f)
            player.Heal(RunState.Stats.healOnKill);
    }

    private void OnPlayerDied() => EndRun(State.Dead);

    private void EndRun(State result)
    {
        if (state == State.Dead || state == State.Victory)
            return;
        state = result;
        // Each level reached becomes a forge token (a won journey adds five); runs helped by the test panel earn none
        tokensEarned = DebugCheats.Tainted ? 0 : RunState.Level + (result == State.Victory ? 5 : 0);
        if (tokensEarned > 0) GameAudio.Play("token");
        MetaProgress.AddTokens(tokensEarned);
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private readonly HashSet<Rune> announcedRunes = new HashSet<Rune>();

    // Announce each rune as it arrives, and set blessings as they complete
    private void OnRunChanged()
    {
        announcedRunes.IntersectWith(RunState.Runes); // a new run forgets the old ones
        foreach (Rune rune in RunState.Runes)
        {
            if (!announcedRunes.Add(rune))
                continue;
            Color c = RuneLibrary.GodColors[(int)rune.god];
            Toast($"룬 획득: {rune.name} ({RuneLibrary.GodNames[(int)rune.god]}) — {rune.description}", c);
            if (RunState.CountOf(rune.god) == RuneLibrary.SetSize)
                Toast($"세트 축복! {RuneLibrary.SetBlessings[(int)rune.god]}", c);
        }
    }

    private void Update()
    {
        if (damageFlash > 0f)
            damageFlash -= Time.unscaledDeltaTime;
        if (hpBarFlash > 0f)
            hpBarFlash -= Time.unscaledDeltaTime;
        // (the opening has the screen and the keys)
        if (OpeningCutscene.Playing || TitleScreen.Playing)
            return;
        // (the skill inventory / upgrade room has the keys while it's open)
        if (SkillSystem.Blocking)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        // (the [K] skill tree is gone: every class skill is there from the start, grown by supports and specials)
        if (keyboard.f1Key.wasPressedThisFrame)
        {
            if (state == State.Playing) Pause(Panel.Test);
            else if (state == State.Paused && panel == Panel.Test) Resume();
        }
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (state == State.Playing) Pause(Panel.Menu);
            else if (state == State.Paused) Resume();
        }
        if (keyboard.fKey.wasPressedThisFrame && state == State.Paused && (panel == Panel.Forge || panel == Panel.Armory))
        {
            Resume();
            return;
        }
        // Tab: between fights the map (to warp back to a room already won); mid-fight, held, the rune list
        // (a realm played as its own scene has none of our rooms: there it's calm unless its boss fight is on, and the
        // realm's map itself says which rooms can be travelled to right now)
        bool calm = !travelling && (StageHost.Hosted
            ? StageHost.Boss?.Invoke() == null
            : currentRoom != null && !currentRoom.IsFighting);
        if (keyboard.tabKey.wasPressedThisFrame && !inHub)
        {
            if (state == State.Playing && calm) Pause(Panel.Map);
            else if (state == State.Paused && panel == Panel.Map) Resume();
        }
        showRunes = keyboard.tabKey.isPressed && state == State.Playing && !inHub && !calm;
        // [C] in the waystone's ring: the route map (갈림길)
        if (keyboard.cKey.wasPressedThisFrame && !inHub)
        {
            if (state == State.Playing && calm && AtRouteGate) OpenRouteMap();
            else if (state == State.Paused && panel == Panel.Route) Resume();
        }

        if (state != State.Playing)
            return;

        // In the village: F at Brokkr's anvil opens the forge
        if (inHub)
        {
            nearForge = hub.NearForge(player.transform.position);
            nearArmory = !nearForge && hub.NearArmory(player.transform.position);
            nearTraining = !nearForge && !nearArmory && hub.NearTraining(player.transform.position);
            if (nearForge && keyboard.fKey.wasPressedThisFrame)
                Pause(Panel.Forge);
            else if (nearArmory && keyboard.fKey.wasPressedThisFrame)
                Pause(Panel.Armory);
            else if (nearTraining && keyboard.fKey.wasPressedThisFrame)
                EnterTrainingFromVillage();
            return;
        }

        // Rune stones within reach: F to take (or buy)
        nearbyPedestal = null;
        foreach (RunePedestal pedestal in FindObjectsByType<RunePedestal>())
            if (pedestal.InReach(player.transform.position))
                nearbyPedestal = pedestal;
        if (nearbyPedestal != null && keyboard.fKey.wasPressedThisFrame && !nearbyPedestal.TryTake())
            Toast("황금이 부족합니다", new Color(1f, 0.55f, 0.5f));
    }

    // Pause the run, showing the pause menu, the greatsword skill tree or the test panel
    private void Pause(Panel show)
    {
        GameAudio.Play(show == Panel.Route ? "ui_map" : "ui_open");
        state = State.Paused;
        panel = show;
        confirmGiveUp = false;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Resume()
    {
        if (state == State.Paused) GameAudio.Play("ui_close");
        state = State.Playing;
        panel = Panel.Menu;
        Time.timeScale = 1f;
        RelockCursor();
    }

    // Each level brings a choice (SkillSystem: upgrade a support, or draw a new one) — the skill trees are retired
    private void OnLeveledUp(int level)
    {
        var gold = new Color(1f, 0.8f, 0.35f);
        Toast(currentRoom != null && currentRoom.IsFighting && !TrainingRoom.Active ? $"레벨 업! Lv {level} — 전투가 끝나면 [G]로 보상을 고릅니다" : $"레벨 업! Lv {level} — [G] 보상 고르기", gold);
        Floater(player.transform.position + Vector3.up * 2.4f, "LEVEL UP!", gold);
        Shockwave.Spawn(player.transform.position, 2f, gold, 16);
    }

    private void RelockCursor()
    {
        var cameraMode = FindAnyObjectByType<CameraModeController>();
        if (cameraMode != null)
            cameraMode.SetFirstPerson(CameraModeController.FirstPersonActive); // re-lock the cursor if needed
    }

    private void ShowBanner(string title, string subtitle)
    {
        banner = title;
        bannerSub = subtitle;
        GameAudio.Play("realm_enter", null, 0.7f);
        bannerUntil = Time.unscaledTime + 3f;
    }

    private void Toast(string text, Color color)
    {
        toasts.Add((text, color, Time.unscaledTime + 3.5f));
        GameAudio.Play(text.Contains("부족") || text.Contains("없습니다") ? "ui_error" : "ui_toast");
        if (toasts.Count > 4) toasts.RemoveAt(0);
    }

    private void Floater(Vector3 world, string text, Color color)
    {
        floaters.Add((world, text, color, Time.time));
    }

    // ───────────── UI ─────────────

    private void OnGUI()
    {
        // After a script reload in Play mode the run's non-saved state is gone; draw nothing until restarted
        if (realms == null || dungeon == null || OpeningCutscene.Playing || StoryStage.Cinematic)
            return;
        EnsureStyles();
        switch (state)
        {
            case State.Playing:
                // A boss cutscene has the screen to itself
                if (BossCutscene.Playing)
                    break;
                DrawHud();
                if (showRunes) DrawRunePanel();
                break;
            case State.Paused:
                DrawHud();
                if (panel == Panel.Skills) DrawSkillTree();
                else if (panel == Panel.Test) DrawTestPanel();
                else if (panel == Panel.Forge) DrawForge();
                else if (panel == Panel.Armory) DrawArmory();
                else if (panel == Panel.Map) DrawWorldMap();
                else if (panel == Panel.Route) DrawRouteMap();
                else DrawPause();
                break;
            case State.Dead:
            case State.Victory: DrawRunOver(); break;
        }

        if (fade > 0f)
            UiFont.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, fade));
    }

    private void DrawHud()
    {
        // Health, with Heimdall's wards as small shields
        Rect bar = new Rect(12f, 36f, 260f, 22f);
        float ratio = player.MaxHealth > 0f ? player.Health / player.MaxHealth : 0f;
        // (a blow jolts the bar for an instant)
        if (hpBarFlash > HpBarFlashTime * 0.7f)
            bar.position += new Vector2(Random.Range(-3f, 3f), Random.Range(-2f, 2f));
        UiArt.Bar(bar, ratio, new Color(0.85f, 0.2f, 0.2f), $"HP {Mathf.CeilToInt(player.Health)} / {Mathf.CeilToInt(player.MaxHealth)}", smallStyleLeft);
        // what was just lost stays on the bar as a pale chunk for a moment, then drains away after it
        if (ratio < hpGhost)
        {
            if (Time.unscaledTime > hpGhostHold)
                hpGhost = Mathf.MoveTowards(hpGhost, ratio, Time.unscaledDeltaTime * 0.6f);
            float inner = bar.width - 4f;
            UiFont.Fill(new Rect(bar.x + 2f + inner * ratio, bar.y + 3f, inner * (hpGhost - ratio), bar.height - 6f), new Color(1f, 0.82f, 0.55f, 0.85f));
        }
        else
            hpGhost = ratio;
        // health lost: the bar blinks red twice and its frame flares
        if (hpBarFlash > 0f)
        {
            float k = hpBarFlash / HpBarFlashTime;
            float blink = Mathf.Abs(Mathf.Sin((1f - k) * Mathf.PI * 2f)) * k;
            UiFont.Fill(bar, new Color(1f, 0.15f, 0.1f, 0.75f * blink));
            Color rim = new Color(1f, 0.25f, 0.2f, blink);
            UiFont.Fill(new Rect(bar.x - 3f, bar.y - 3f, bar.width + 6f, 3f), rim);
            UiFont.Fill(new Rect(bar.x - 3f, bar.yMax, bar.width + 6f, 3f), rim);
            UiFont.Fill(new Rect(bar.x - 3f, bar.y, 3f, bar.height), rim);
            UiFont.Fill(new Rect(bar.xMax, bar.y, 3f, bar.height), rim);
        }
        for (int i = 0; i < player.WardsLeft; i++)
            UiFont.Fill(new Rect(bar.xMax + 6f + i * 14f, bar.y + 3f, 10f, 14f), new Color(0.75f, 0.9f, 1f));

        if (inHub)
            DrawHubInfo(bar);
        else
        {
            // 기력 right under the health, then level and experience toward the next one
            Rect stamina = new Rect(bar.x, bar.yMax + 2f, bar.width, 18f);
            SkillSystem.DrawStaminaBar(stamina);
            Rect xp = new Rect(bar.x, stamina.yMax + 2f, bar.width, 18f);
            UiArt.Bar(xp, RunState.Xp / (float)Mathf.Max(1, RunState.XpToNext), new Color(0.35f, 0.85f, 0.6f), $"Lv {RunState.Level}   {RunState.Xp} / {RunState.XpToNext}", smallStyleLeft);

            // Gold and realm
            float top = xp.yMax;
            UiFont.Fill(new Rect(bar.x, top + 8f, 12f, 12f), new Color(1f, 0.78f, 0.2f));
            GUI.Label(new Rect(bar.x + 18f, top + 3f, 200f, 22f), $"황금 {RunState.Gold}", textStyleBold);
            GUI.Label(new Rect(bar.x, top + 28f, 300f, 20f), StageLabel(CurrentRealm), smallStyleLeft);

            if (!SkillSystem.RunesOff)
                DrawRuneRow(new Vector2(bar.x, top + 52f));
            else
                SkillSystem.DrawSetIcons(new Vector2(bar.x, top + 52f));
            DrawMinimap();
            DrawBossBar();
            DrawChargeGauge();
            Armory?.DrawHud();
            DrawPrompts();
            DrawSkillSlots();
        }
        DrawFloaters();

        // Toasts, newest at the bottom
        float y = Screen.height * 0.22f;
        toasts.RemoveAll(t => Time.unscaledTime > t.until);
        foreach (var (text, color, _) in toasts)
        {
            Rect r = new Rect((Screen.width - 560f) / 2f, y, 560f, 28f);
            UiFont.Fill(r, new Color(0f, 0f, 0f, 0.65f));
            GUI.color = color;
            GUI.Label(r, text, centerStyleBold);
            GUI.color = Color.white;
            y += 32f;
        }

        if (Time.unscaledTime < bannerUntil && banner != null)
        {
            float alpha = Mathf.Clamp01((bannerUntil - Time.unscaledTime) / 0.6f);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(0f, Screen.height * 0.36f, Screen.width, 50f), banner, titleStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.36f + 48f, Screen.width, 24f), bannerSub, centerStyle);
            GUI.color = Color.white;
        }

        if (damageFlash > 0f)
            UiFont.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(1f, 0f, 0f, damageFlash));

        bool twin = blades != null && blades.IsActive;
        string attackHint = Armory != null ? Armory.Hint : twin ? "[클릭] 연속 베기" : miner.CanCharge ? "[클릭] 연속 베기  [Q·E·R] 기술  [우클릭 꾹] 차징 강타" : "[클릭] 공격  [Q] 망치 투척  [E 꾹] 번개 방출  [R] 휠윈드  [Space] 대시";
        // (Q / E / R and the right click belong to the skill orbs now)

        string keys = inHub
            ? $"[WASD] 이동  [Shift] 달리기  [F] 대장간·무기고  [V] 시점  [F1] 테스트  [Esc] 일시정지"
            : $"{attackHint}  [Shift] 달리기  [F] 보조스킬  [G] 레벨 업 보상  [Tab] {(currentRoom != null && currentRoom.IsFighting ? "룬 목록" : "지도·이동")}  [V] 시점  [F1] 테스트  [Esc] 일시정지";
        GUI.Label(new Rect(12f, Screen.height - 26f, 760f, 20f), keys, smallStyleLeft);

        // Poisoned: a green edge on the HP bar and the seconds left
        if (player.Poisoned)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            Color venom = new Color(0.45f, 0.95f, 0.3f, 0.55f + 0.35f * pulse);
            UiFont.Fill(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, 2f), venom);
            UiFont.Fill(new Rect(bar.x - 2f, bar.yMax, bar.width + 4f, 2f), venom);
            UiFont.Fill(new Rect(bar.x - 2f, bar.y, 2f, bar.height), venom);
            UiFont.Fill(new Rect(bar.xMax, bar.y, 2f, bar.height), venom);
            UiFont.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.3f, 0.8f, 0.2f, 0.05f + 0.03f * pulse));
            // the poison badge: what it does, the seconds left, and a draining bar of its remaining time
            Rect badge = new Rect(bar.xMax + 12f, bar.y + 22f, 150f, 22f);
            UiFont.Fill(new Rect(badge.x - 4f, badge.y, badge.width + 8f, 28f), new Color(0.05f, 0.15f, 0.03f, 0.7f));
            GUI.color = new Color(0.55f, 1f, 0.4f);
            GUI.Label(badge, $"중독  -3/초  {Mathf.CeilToInt(player.PoisonLeft)}초", textStyleBold);
            GUI.color = Color.white;
            UiFont.Fill(new Rect(badge.x, badge.y + 22f, badge.width, 4f), new Color(0f, 0f, 0f, 0.6f));
            UiFont.Fill(new Rect(badge.x, badge.y + 22f, badge.width * Mathf.Clamp01(player.PoisonLeft / 7f), 4f), new Color(0.5f, 1f, 0.35f));
        }

        if (DebugCheats.Active)
        {
            GUI.color = new Color(1f, 0.6f, 0.3f);
            GUI.Label(new Rect(bar.xMax + 12f + player.WardsLeft * 14f, bar.y - 1f, 200f, 22f), DebugCheats.Invincible ? "테스트 모드 · 무적" : "테스트 모드", textStyleBold);
            GUI.color = Color.white;
        }
    }

    // ── Skill slots: Q, E, R, right click, Space, bottom right, as round buttons ──
    // Cluster: a big round button (right click) with the others on an arc round it, like Genshin / Wuthering Waves.
    // Row: small round buttons side by side with their keys underneath.
    // Drop an icon per slot into slotIcons (same order: Q, E, R, right click, Space) and it is drawn inside its circle;
    // slotCooldowns (seconds left, 0 = ready) dims the slot and shows the time; slotActive rings it in red.
    public enum SlotLayout { Cluster, Row }
    [Header("Skill slots (Q, E, R, right click, Space)")]
    public SlotLayout slotLayout = SlotLayout.Cluster;
    public Texture2D[] slotIcons = new Texture2D[5];
    [HideInInspector] public float[] slotCooldowns = new float[5];
    [HideInInspector] public bool[] slotActive = new bool[5];
    private static readonly string[] SkillKeys = { "Q", "E", "R", "우클릭", "SPACE" };
    private GUIStyle slotKeyStyle, slotGlyphStyle, slotTimeStyle;

    // The fighters that fill the slots above (묠니르, 번개 건틀릿) get the game's one skill bar — the twin blades' row
    // (SkillBar) — like every other fighter: a slot shows once its fighter names it (slotNames), greyed while slotLocked
    [HideInInspector] public string[] slotNames = new string[5];
    [HideInInspector] public bool[] slotLocked = new bool[5];
    private readonly float[] slotTotals = new float[5];
    private readonly List<SkillBar.Slot> slotBar = new List<SkillBar.Slot>(5);

    private void DrawSkillSlots()
    {
        if (slotNames == null || SkillBar.Hidden) return;
        slotBar.Clear();
        for (int i = 0; i < SkillKeys.Length && i < slotNames.Length; i++)
        {
            if (string.IsNullOrEmpty(slotNames[i])) { slotTotals[i] = 0f; continue; }
            float cd = slotCooldowns != null && i < slotCooldowns.Length ? slotCooldowns[i] : 0f;
            // (the full cooldown is the most seen since it last started)
            slotTotals[i] = cd <= 0f ? 0f : Mathf.Max(slotTotals[i], cd);
            slotBar.Add(new SkillBar.Slot
            {
                key = SkillBar.Keys[i], name = slotNames[i], icon = slotIcons != null && i < slotIcons.Length ? slotIcons[i] : null,
                learned = slotLocked == null || i >= slotLocked.Length || !slotLocked[i], left = cd, total = slotTotals[i],
                lit = slotActive != null && i < slotActive.Length && slotActive[i], litColor = new Color(1f, 0.32f, 0.22f),
            });
        }
        SkillBar.Draw(slotBar);
    }

    // (the old round slots, no longer drawn: every fighter uses SkillBar)
    private void DrawRoundSkillSlots()
    {
        if (slotKeyStyle == null)
        {
            slotKeyStyle = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 11 };
            slotGlyphStyle = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            slotTimeStyle = new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }
        if (slotLayout == SlotLayout.Row)
        {
            const float r = 24f, gap = 18f;
            int n = SkillKeys.Length;
            float x = Screen.width - 24f - r - (n - 1) * (2f * r + gap);
            float y = Screen.height - 58f - r;
            for (int i = 0; i < n; i++)
            {
                var c = new Vector2(x + i * (2f * r + gap), y);
                DrawSlot(c, r, i);
                GUI.Label(new Rect(c.x - 30f, c.y + r + 3f, 60f, 16f), SkillKeys[i], slotKeyStyle);
            }
            return;
        }

        // the big button (right click) in the corner, the rest on an arc round its upper left
        float big = 44f, small = 28f, arc = 98f;
        var hub = new Vector2(Screen.width - 30f - big, Screen.height - 46f - big);
        DrawSlot(hub, big, 3);
        KeyTag(hub, big, 3);
        int[] order = { 4, 2, 1, 0 };                 // Space, R, E, Q
        float[] angles = { 92f, 128f, 166f, 204f };   // degrees round the big button, from its right going up and over
        for (int k = 0; k < order.Length; k++)
        {
            float a = angles[k] * Mathf.Deg2Rad;
            var c = hub + new Vector2(Mathf.Cos(a), -Mathf.Sin(a)) * arc;
            DrawSlot(c, small, order[k]);
            KeyTag(c, small, order[k]);
        }
    }

    // one round slot: a soft shadow, a rim, the dark face, the icon (or the key, faintly, while there is none),
    // and over it the cooldown or the "in use" ring
    private void DrawSlot(Vector2 c, float r, int i)
    {
        bool active = slotActive != null && i < slotActive.Length && slotActive[i];
        float cd = slotCooldowns != null && i < slotCooldowns.Length ? slotCooldowns[i] : 0f;
        DrawDisc(c + new Vector2(0f, 2f), r + 3f, new Color(0f, 0f, 0f, 0.35f));
        DrawDisc(c, r, active ? new Color(1f, 0.32f, 0.22f, 0.95f) : new Color(1f, 1f, 1f, 0.55f));
        DrawDisc(c, r - (active ? 3f : 2f), new Color(0.07f, 0.07f, 0.09f, 0.78f));
        Texture2D icon = slotIcons != null && i < slotIcons.Length ? slotIcons[i] : null;
        float s = r * 1.25f;
        if (icon != null)
            GUI.DrawTexture(new Rect(c.x - s / 2f, c.y - s / 2f, s, s), icon, ScaleMode.ScaleToFit);
        else
        {
            slotGlyphStyle.fontSize = Mathf.RoundToInt(r * (SkillKeys[i].Length > 2 ? 0.4f : 0.7f));
            GUI.color = new Color(1f, 1f, 1f, 0.3f);
            GUI.Label(new Rect(c.x - r, c.y - r, 2f * r, 2f * r), SkillKeys[i], slotGlyphStyle);
            GUI.color = Color.white;
        }
        if (cd > 0f)
        {
            DrawDisc(c, r - 2f, new Color(0f, 0f, 0f, 0.55f));
            slotTimeStyle.fontSize = Mathf.RoundToInt(r * 0.55f);
            GUI.Label(new Rect(c.x - r, c.y - r, 2f * r, 2f * r), cd < 10f ? cd.ToString("0.0") : Mathf.CeilToInt(cd).ToString(), slotTimeStyle);
        }
    }

    // the key on a small dark pill at the slot's lower edge
    private void KeyTag(Vector2 c, float r, int i)
    {
        string key = SkillKeys[i];
        float w = Mathf.Max(18f, slotKeyStyle.CalcSize(new GUIContent(key)).x + 10f);
        Rect tag = new Rect(c.x - w / 2f, c.y + r - 8f, w, 15f);
        UiFont.Fill(tag, new Color(0f, 0f, 0f, 0.75f));
        GUI.Label(tag, key, slotKeyStyle);
    }

    // One small coloured tile per rune, grouped by god
    private void DrawRuneRow(Vector2 at)
    {
        float x = at.x;
        foreach (Rune rune in RunState.Runes.OrderBy(r => r.god))
        {
            Rect tile = new Rect(x, at.y, 22f, 22f);
            UiFont.Fill(tile, RuneLibrary.GodColors[(int)rune.god]);
            UiFont.Fill(new Rect(tile.x + 2f, tile.y + 2f, 18f, 18f), new Color(0.12f, 0.1f, 0.1f));
            GUI.color = RuneLibrary.GodColors[(int)rune.god];
            GUI.Label(tile, rune.name.Substring(0, 1), centerStyleBold);
            GUI.color = Color.white;
            x += 25f;
        }
        if (RunState.Runes.Count == 0)
            GUI.Label(new Rect(at.x, at.y, 300f, 20f), "룬 없음 — 보물방·상점·보스에서 얻으세요", smallStyleLeft);
    }

    private void DrawRunePanel()
    {
        Rect panel = new Rect((Screen.width - 560f) / 2f, 70f, 560f, Mathf.Min(Screen.height - 100f, 120f + RunState.Runes.Count * 24f + 6 * 22f));
        UiFont.Fill(panel, new Color(0.08f, 0.07f, 0.07f, 0.92f));
        GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 30f), "새겨진 룬", titleStyle);
        float y = panel.y + 46f;
        foreach (Rune rune in RunState.Runes.OrderBy(r => r.god))
        {
            GUI.color = RuneLibrary.GodColors[(int)rune.god];
            GUI.Label(new Rect(panel.x + 16f, y, 200f, 22f), $"{rune.name} ({RuneLibrary.GodNames[(int)rune.god]})", textStyleBold);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 230f, y, panel.width - 240f, 22f), rune.description, smallStyleLeft);
            y += 24f;
        }
        y += 8f;
        GUI.Label(new Rect(panel.x + 16f, y, panel.width, 20f), $"같은 신의 룬 {RuneLibrary.SetSize}개 = 세트 축복", smallStyleLeft);
        y += 22f;
        for (int g = 0; g < RuneLibrary.GodNames.Length; g++)
        {
            int count = RunState.CountOf((God)g);
            GUI.color = count >= RuneLibrary.SetSize ? RuneLibrary.GodColors[g] : new Color(1f, 1f, 1f, 0.45f);
            GUI.Label(new Rect(panel.x + 16f, y, panel.width - 32f, 20f), $"{Mathf.Min(count, RuneLibrary.SetSize)}/{RuneLibrary.SetSize}  {RuneLibrary.SetBlessings[g]}", smallStyleLeft);
            GUI.color = Color.white;
            y += 22f;
        }
    }

    // Rooms seen so far: visited ones solid, their neighbours as outlines.
    // During a fight it shows the current zone instead, with the enemies on it.
    private void DrawMinimap()
    {
        // A realm played as its own scene: its rooms in the corner, the room's name under them
        if (StageHost.Hosted)
        {
            // In a fight: that room alone, with its monsters (as this game's halls do); all the rooms once it is won
            StageHost.Zone? fight = StageHost.Fight?.Invoke();
            if (fight.HasValue)
            {
                DrawHostedZoneMap(fight.Value);
                return;
            }
            var nodes = StageHost.Map?.Invoke();
            if (nodes == null || nodes.Count == 0)
                return;
            LoadMapArt();
            bool[] shown = VisibleStageNodes(nodes);
            var seen = Enumerable.Range(0, nodes.Count).Where(i => shown[i]).Select(i => nodes[i]).ToList();
            if (seen.Count == 0)
                return;
            int cols = seen.Max(n => n.cell.x) - seen.Min(n => n.cell.x) + 1, rows = seen.Max(n => n.cell.y) - seen.Min(n => n.cell.y) + 1;
            const float stageStep = 24f, stagePad = 6f;
            Rect plate = new Rect(Screen.width - cols * stageStep - stagePad * 2f - 12f, 12f, cols * stageStep + stagePad * 2f, rows * stageStep + stagePad * 2f);
            MapPlate(plate, 0.35f);
            DrawStageNodes(new Rect(plate.x + stagePad, plate.y + stagePad, plate.width - stagePad * 2f, plate.height - stagePad * 2f), 18f, 0.75f, true, out _);
            if (!string.IsNullOrEmpty(StageHost.RoomName))
                GUI.Label(new Rect(plate.x - 60f, plate.yMax + 4f, plate.width + 60f, 16f), StageHost.RoomName, smallCenterStyle);
            return;
        }

        if (currentRoom != null && currentRoom.IsFighting)
        {
            DrawZoneMap(currentRoom);
            return;
        }

        const float cell = 18f, gap = 6f, pad = 6f;
        var known = new HashSet<Room>();
        foreach (Room room in dungeon.Rooms)
            if (room.Visited) { known.Add(room); foreach (Room n in room.Neighbours) known.Add(n); }
        if (known.Count == 0)
            return;
        LoadMapArt();

        int minX = known.Min(r => r.Grid.x), maxX = known.Max(r => r.Grid.x);
        int minY = known.Min(r => r.Grid.y), maxY = known.Max(r => r.Grid.y);
        float width = (maxX - minX + 1) * (cell + gap) - gap + pad * 2f, height = (maxY - minY + 1) * (cell + gap) - gap + pad * 2f;
        Rect area = new Rect(Screen.width - width - 12f, 12f, width, height);
        MapPlate(area, 0.35f);

        Rect CellRect(Room r) => new Rect(area.x + pad + (r.Grid.x - minX) * (cell + gap), area.y + pad + (maxY - r.Grid.y) * (cell + gap), cell, cell);
        foreach (Room room in known)
            foreach (Room n in room.Neighbours)
                if (known.Contains(n) && (room.Visited || n.Visited) && room.GetInstanceID() < n.GetInstanceID())
                    MapPath(CellRect(room).center, CellRect(n).center, 2.5f, room.Visited && n.Visited);
        foreach (Room room in known)
            MapRoom(CellRect(room), room, room == currentRoom, false, true);
    }

    // ── The map's painted dress (VARCO, Resources/UI/map_*): a star chart of Asgard on dark vellum behind it, a gold
    // and carved-wood frame with raven heads, a gold-rimmed marble tile for every hall and a sheet of hall icons ──

    private Texture2D mapBg, mapFrame, mapTile, mapIcons;
    private bool mapArtLoaded;
    private GUIStyle mapFrameStyle, mapPlaqueStyle, mapLegendStyle;

    private void LoadMapArt()
    {
        if (mapArtLoaded)
            return;
        mapArtLoaded = true;
        mapBg = Resources.Load<Texture2D>("UI/map_bg");
        mapFrame = Resources.Load<Texture2D>("UI/map_frame");
        mapTile = Resources.Load<Texture2D>("UI/map_tile");
        mapIcons = Resources.Load<Texture2D>("UI/map_icons");
    }

    // Cells of the icon sheet (reading order): the Bifrost gate, crossed sword and axe, a treasure chest, a coin
    // purse, Týr's shield, Gjallarhorn, a misty rune stone, the compass star, a valkyrie's wing
    private const int IconStart = 0, IconFight = 1, IconTreasure = 2, IconShop = 3, IconOath = 4, IconBoss = 5, IconUnknown = 6, IconHere = 7;

    private static int MapIconOf(Room room) => room.RoomKind switch
    {
        Room.Kind.Start => IconStart,
        Room.Kind.Treasure => IconTreasure,
        Room.Kind.Shop => IconShop,
        Room.Kind.Forge => IconShop,
        Room.Kind.Supports => IconTreasure,
        Room.Kind.Vault => IconTreasure,
        Room.Kind.Heal => IconStart,
        Room.Kind.Vigor => IconStart,
        Room.Kind.Ascend => IconTreasure,
        Room.Kind.Fate => IconTreasure,
        Room.Kind.Essence => IconTreasure,
        Room.Kind.Exit => IconStart,
        Room.Kind.MidBoss => IconOath,
        Room.Kind.Boss => IconBoss,
        _ => room.HasMidBoss ? IconOath : room.Visited ? IconFight : IconUnknown,
    };

    private void DrawMapIcon(int cell, Rect r, Color tint)
    {
        if (mapIcons == null)
            return;
        const float u = 1f / 3f;
        GUI.color = tint;
        GUI.DrawTextureWithTexCoords(r, mapIcons, new Rect(cell % 3 * u, 1f - (cell / 3 + 1) * u, u, u));
        GUI.color = Color.white;
    }

    // A piece of the star chart with a thin gold edge (the small maps in the corner)
    private void MapPlate(Rect area, float dim)
    {
        UiFont.Fill(new Rect(area.x - 3f, area.y - 3f, area.width + 6f, area.height + 6f), new Color(0.05f, 0.04f, 0.06f, 0.85f));
        if (mapBg != null)
        {
            GUI.DrawTexture(area, mapBg, ScaleMode.ScaleAndCrop);
            UiFont.Fill(area, new Color(0.02f, 0.02f, 0.05f, dim));
        }
        else
            UiFont.Fill(area, new Color(0.08f, 0.09f, 0.15f, 0.85f));
        Color gold = new Color(0.95f, 0.75f, 0.35f, 0.9f);
        UiFont.Fill(new Rect(area.x - 2f, area.y - 2f, area.width + 4f, 1.5f), gold);
        UiFont.Fill(new Rect(area.x - 2f, area.yMax + 0.5f, area.width + 4f, 1.5f), gold);
        UiFont.Fill(new Rect(area.x - 2f, area.y - 2f, 1.5f, area.height + 4f), gold);
        UiFont.Fill(new Rect(area.xMax + 0.5f, area.y - 2f, 1.5f, area.height + 4f), gold);
    }

    // A way between two halls: a strand of gold light with a soft glow round it (fainter where it leads into the unknown)
    private static void MapPath(Vector2 a, Vector2 b, float width, bool travelled)
    {
        float alpha = travelled ? 1f : 0.45f;
        DrawBough(a, b, width * 3.2f, new Color(1f, 0.75f, 0.3f, 0.18f * alpha));
        DrawBough(a, b, width, new Color(1f, 0.85f, 0.5f, 0.9f * alpha));
    }

    // A hall on the map: its marble tile (bright where the player stands, dim where it is only glimpsed) and its icon
    private void MapRoom(Rect r, Room room, bool here, bool hover, bool small) =>
        MapTile(r, MapIconOf(room), room.Visited, room.RoomKind != Room.Kind.Combat, here, hover, small);

    // A hall's tile and icon (special: a hall whose kind is known before it is entered, so its icon shows at full strength)
    private void MapTile(Rect r, int icon, bool visited, bool special, bool here, bool hover, bool small)
    {
        if (here)
            DrawDisc(r.center, r.width * (0.85f + 0.08f * Mathf.Sin(Time.unscaledTime * 3f)), new Color(1f, 0.8f, 0.35f, 0.35f));
        else if (hover)
            DrawDisc(r.center, r.width * 0.8f, new Color(1f, 1f, 1f, 0.22f));
        Color tile = here ? new Color(1f, 0.92f, 0.7f) : hover ? Color.white : visited ? new Color(0.78f, 0.76f, 0.74f) : new Color(0.3f, 0.3f, 0.38f, 0.9f);
        if (mapTile != null)
        {
            GUI.color = tile;
            GUI.DrawTexture(r, mapTile);
            GUI.color = Color.white;
        }
        else
            UiFont.Fill(r, tile);

        // A cleared fight hall seen in the small map needs no icon; every other hall shows what it is
        if (small && icon == IconFight)
            return;
        float k = small ? 0.86f : 0.66f;
        var ir = new Rect(r.center.x - r.width * k / 2f, r.center.y - r.height * k / 2f, r.width * k, r.height * k);
        DrawMapIcon(icon, ir, visited ? Color.white : special ? new Color(1f, 1f, 1f, 0.7f) : new Color(1f, 1f, 1f, 0.75f));
        // The compass star turning over the player's hall
        if (here && !small)
        {
            float s = r.width * 0.42f;
            Vector2 c = new Vector2(r.xMax - s * 0.35f, r.y + s * 0.35f);
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(Time.unscaledTime * 40f, c);
            DrawMapIcon(IconHere, new Rect(c.x - s / 2f, c.y - s / 2f, s, s), Color.white);
            GUI.matrix = m;
        }
    }

    // Tab between fights: every room found so far, large. A room already visited can be clicked to warp to its middle.
    private void DrawWorldMap()
    {
        LoadMapArt();
        float w = Mathf.Min(800f, Screen.width - 30f), h = Mathf.Min(600f, Screen.height - 24f);
        Rect panel = CenteredPanel(w, h);
        mapPlaqueStyle ??= UiFont.Style(17, TextAnchor.MiddleCenter, FontStyle.Bold);
        mapLegendStyle ??= UiFont.Style(12, TextAnchor.MiddleLeft);

        // The star chart of Asgard, darkened a little so the halls drawn on it read
        if (mapBg != null)
        {
            GUI.DrawTexture(panel, mapBg, ScaleMode.ScaleAndCrop);
            UiFont.Fill(panel, new Color(0.02f, 0.02f, 0.06f, 0.3f));
        }
        const float frameOut = 8f;
        Rect frame = new Rect(panel.x - frameOut, panel.y - frameOut, panel.width + frameOut * 2f, panel.height + frameOut * 2f);
        float rim = mapFrame != null ? 50f : 12f;   // how far the frame's bars reach in over the panel
        Rect inner = new Rect(panel.x + rim, panel.y + rim + 8f, panel.width - rim * 2f, panel.height - rim * 2f - 22f);
        GUI.Label(new Rect(inner.x, inner.y, inner.width, 20f), "가 본 방을 누르면 그 방 가운데로 이동합니다", smallCenterStyle);

        var known = new HashSet<Room>();
        foreach (Room room in dungeon.Rooms)
            if (room.Visited) { known.Add(room); foreach (Room n in room.Neighbours) known.Add(n); }

        // The legend down the right side
        const float legendW = 128f;
        var legend = new[] { (IconStart, "시작의 방"), (IconFight, "정리한 전투 구역"), (IconUnknown, "아직 모르는 곳"), (IconTreasure, "보물방"), (IconShop, "상점"), (IconOath, MidBossHallName), (IconBoss, "수문장의 방") };
        float ly = inner.y + 30f;
        UiFont.Fill(new Rect(inner.xMax - legendW - 6f, ly - 6f, legendW + 6f, legend.Length * 26f + 10f), new Color(0.02f, 0.02f, 0.05f, 0.45f));
        foreach (var (icon, label) in legend)
        {
            DrawMapIcon(icon, new Rect(inner.xMax - legendW, ly, 22f, 22f), Color.white);
            GUI.Label(new Rect(inner.xMax - legendW + 26f, ly, legendW - 26f, 22f), label, mapLegendStyle);
            ly += 26f;
        }

        Room hovered = null;
        string stageHover = null;
        // A realm played as its own scene: its rooms, as the realm reports them
        if (StageHost.Hosted)
        {
            Rect area = new Rect(inner.x + 6f, inner.y + 28f, inner.width - legendW - 22f, inner.height - 28f - 74f);
            int picked = DrawStageNodes(area, 74f, 0.68f, false, out stageHover);
            if (picked >= 0)
            {
                Resume();
                StageHost.MapJump?.Invoke(picked);
                return;
            }
        }
        else if (known.Count > 0)
        {
            int minX = known.Min(r => r.Grid.x), maxX = known.Max(r => r.Grid.x);
            int minY = known.Min(r => r.Grid.y), maxY = known.Max(r => r.Grid.y);
            Rect area = new Rect(inner.x + 6f, inner.y + 28f, inner.width - legendW - 22f, inner.height - 28f - 74f);
            float cell = Mathf.Min(74f, Mathf.Min(area.width / (maxX - minX + 1), area.height / (maxY - minY + 1)) * 0.68f);
            float step = cell / 0.68f;
            Vector2 origin = new Vector2(area.center.x - (maxX - minX + 1) * step / 2f, area.center.y - (maxY - minY + 1) * step / 2f);
            Rect CellRect(Room r) => new Rect(origin.x + (r.Grid.x - minX) * step + (step - cell) / 2f,
                origin.y + (maxY - r.Grid.y) * step + (step - cell) / 2f, cell, cell);

            // Ways between halls, then the halls
            foreach (Room room in known)
                foreach (Room n in room.Neighbours)
                    if (known.Contains(n) && (room.Visited || n.Visited) && room.GetInstanceID() < n.GetInstanceID())
                        MapPath(CellRect(room).center, CellRect(n).center, Mathf.Max(2.5f, cell * 0.06f), room.Visited && n.Visited);

            Vector2 mouse = Event.current.mousePosition;
            foreach (Room room in known)
            {
                Rect r = CellRect(room);
                bool here = room == currentRoom;
                bool reachable = room.Visited && !here && !room.IsFighting;
                bool hover = reachable && r.Contains(mouse);
                if (hover) hovered = room;
                MapRoom(r, room, here, hover, false);
                if (here)
                {
                    GUI.color = new Color(1f, 0.88f, 0.5f);
                    GUI.Label(new Rect(r.x - 20f, r.yMax + 1f, r.width + 40f, 16f), "현재 위치", smallCenterStyle);
                    GUI.color = Color.white;
                }
                if (reachable && UiSound.Button(r, GUIContent.none, GUIStyle.none))
                {
                    WarpTo(room);
                    return;
                }
            }
        }

        // What the hovered hall is, and the runes carried (Tab shows the map instead of the rune list out of a fight)
        string info = hovered == null ? "" : hovered.RoomKind switch
        {
            Room.Kind.Start => "시작의 방", Room.Kind.Treasure => "보물방", Room.Kind.Shop => "상점", Room.Kind.Forge => "강화방", Room.Kind.Boss => "수문장의 방",
            Room.Kind.Supports => "보조마법의 제단", Room.Kind.Heal => "치유의 샘", Room.Kind.Vault => "황금 보물고", Room.Kind.Exit => "갈림길 (룬 고리에서 [C])",
            Room.Kind.Vigor => "생명의 결정", Room.Kind.Ascend => "승천의 신전", Room.Kind.Fate => "노른의 카드", Room.Kind.Essence => "정수의 보고",
            Room.Kind.MidBoss => MidBossHallName,
            _ => hovered.HasMidBoss ? $"중간 보스({CurrentRealm.MidBossTitle})가 나오는 전투 구역" : "정리한 전투 구역" + HallShapeName(hovered),
        } + " — 눌러서 이동";
        if (stageHover != null)
            info = stageHover + " — 눌러서 이동";
        GUI.Label(new Rect(inner.x, inner.yMax - 70f, inner.width, 22f), info, centerStyleBold);
        DrawRuneRow(new Vector2(inner.x + 8f, inner.yMax - 44f));
        GUI.Label(new Rect(inner.x, inner.yMax - 20f, inner.width, 20f), "[Tab] 또는 [Esc] 닫기", smallCenterStyle);

        // The gold-and-wood frame over the window's edge (nine-sliced so its raven-head corners keep their shape),
        // the realm's name on its plaque
        if (mapFrame != null)
        {
            mapFrameStyle ??= new GUIStyle { border = new RectOffset(100, 100, 88, 92), normal = { background = mapFrame } };
            if (Event.current.type == EventType.Repaint)
                mapFrameStyle.Draw(frame, false, false, false, false);
            GUI.color = new Color(0.25f, 0.15f, 0.05f);
            GUI.Label(new Rect(frame.center.x - 140f, frame.y + 21f, 280f, 34f), $"{CurrentRealm.name} 지도", mapPlaqueStyle);
            GUI.color = Color.white;
        }
        else
            GUI.Label(new Rect(panel.x, panel.y + 12f, panel.width, 32f), $"{CurrentRealm.name} 지도", titleStyle);
    }

    // The mid-boss's hall: Týr's Hall of Oaths in Asgard, the realm's own guardian's lair elsewhere
    private string MidBossHallName => CurrentRealm.palace ? "맹세의 전당" : CurrentRealm.midTwins != null ? "서리 거인의 결투장" : CurrentRealm.midBoss != null ? CurrentRealm.midBoss.name.Split(' ')[^1] + "의 둥지" : "중간 보스의 방";

    // A scene realm's rooms (StageHost.Map) drawn the way this game draws its halls: gold paths, marble tiles, icons.
    // Returns the node clicked (a reachable one, on the large map), or -1.
    private int DrawStageNodes(Rect area, float cellMax, float fill, bool small, out string hoveredName)
    {
        hoveredName = null;
        var nodes = StageHost.Map?.Invoke();
        if (nodes == null || nodes.Count == 0)
            return -1;
        // One rule for every stage's map, as this game's own halls: a room shows once it has been entered, or when it
        // lies behind a door of one that has (whatever the realm hands over)
        bool[] shown = VisibleStageNodes(nodes);
        var visible = Enumerable.Range(0, nodes.Count).Where(i => shown[i]).ToList();
        if (visible.Count == 0)
            return -1;
        int minX = visible.Min(i => nodes[i].cell.x), maxX = visible.Max(i => nodes[i].cell.x);
        int minY = visible.Min(i => nodes[i].cell.y), maxY = visible.Max(i => nodes[i].cell.y);
        float cell = Mathf.Min(cellMax, Mathf.Min(area.width / (maxX - minX + 1), area.height / (maxY - minY + 1)) * fill);
        float step = cell / fill;
        Vector2 origin = new Vector2(area.center.x - (maxX - minX + 1) * step / 2f, area.center.y - (maxY - minY + 1) * step / 2f);
        Rect CellRect(Vector2Int c) => new Rect(origin.x + (c.x - minX) * step + (step - cell) / 2f, origin.y + (maxY - c.y) * step + (step - cell) / 2f, cell, cell);

        for (int i = 0; i < nodes.Count; i++)
            if (shown[i] && nodes[i].links != null)
                foreach (int j in nodes[i].links)
                    if (j > i && j < nodes.Count && shown[j] && (nodes[i].visited || nodes[j].visited))
                        MapPath(CellRect(nodes[i].cell).center, CellRect(nodes[j].cell).center, small ? 2.5f : Mathf.Max(2.5f, cell * 0.06f), nodes[i].visited && nodes[j].visited);

        int picked = -1;
        Vector2 mouse = Event.current.mousePosition;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!shown[i])
                continue;
            var n = nodes[i];
            Rect r = CellRect(n.cell);
            int icon = n.boss ? IconBoss : n.midBoss ? IconOath : n.visited ? IconFight : IconUnknown;
            bool hover = !small && n.reachable && r.Contains(mouse);
            if (hover)
                hoveredName = n.name;
            MapTile(r, icon, n.visited, n.boss || n.midBoss, n.current, hover, small);
            if (!small && n.current)
            {
                GUI.color = new Color(1f, 0.88f, 0.5f);
                GUI.Label(new Rect(r.x - 20f, r.yMax + 1f, r.width + 40f, 16f), "현재 위치", smallCenterStyle);
                GUI.color = Color.white;
            }
            if (!small && n.reachable && UiSound.Button(r, GUIContent.none, GUIStyle.none))
                picked = i;
        }
        return picked;
    }

    // Which of a stage's rooms the map shows: entered ones, the one the player is in, and those behind their doors
    private static bool[] VisibleStageNodes(IReadOnlyList<StageHost.MapNode> nodes)
    {
        var shown = new bool[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!nodes[i].visited && !nodes[i].current)
                continue;
            shown[i] = true;
            if (nodes[i].links != null)
                foreach (int j in nodes[i].links)
                    if (j >= 0 && j < nodes.Count) shown[j] = true;
        }
        // (links may be listed one way only: a room linked to an entered one shows too)
        for (int i = 0; i < nodes.Count; i++)
            if (!shown[i] && nodes[i].links != null)
                foreach (int j in nodes[i].links)
                    if (j >= 0 && j < nodes.Count && (nodes[j].visited || nodes[j].current)) { shown[i] = true; break; }
        return shown;
    }

    // What shape a fight hall is, for the map's caption
    private static string HallShapeName(Room room) => room.Shape switch
    {
        "Gallery" => " · 긴 회랑",
        "Cross" => " · 십자 전당",
        "Cloister" => " · 별빛 회랑",
        "Bridge" => " · 무지개 다리",
        "Colonnade" => " · 기둥의 숲",
        _ => "",
    };

    // Fade out, appear in the middle of a room already won (beside anything standing there), fade in
    private void WarpTo(Room room)
    {
        Resume();
        StartCoroutine(WarpRoutine(room));
    }

    private IEnumerator WarpRoutine(Room room)
    {
        travelling = true;
        GameAudio.Play("portal_enter", null, 0.7f);
        for (; fade < 1f; fade += Time.unscaledDeltaTime / 0.25f)
            yield return null;
        fade = 1f;

        dungeon.Activate(room);
        currentRoom = room;
        Teleport(FreeSpotNear(room.transform.position), Quaternion.identity);
        yield return null;

        for (; fade > 0f; fade -= Time.unscaledDeltaTime / 0.3f)
            yield return null;
        fade = 0f;
        travelling = false;
        room.Enter();
    }

    // The room's centre, or the nearest clear spot around it (a rune stone or the throne may stand there)
    private static Vector3 FreeSpotNear(Vector3 center)
    {
        for (float radius = 0f; radius <= 7f; radius += 1.5f)
            for (int i = 0; i < (radius == 0f ? 1 : 10); i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                Vector3 p = center + new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a)) * radius;   // try the near (south) side first
                if (!Physics.CheckCapsule(p + Vector3.up * 0.6f, p + Vector3.up * 1.8f, 0.5f, ~0, QueryTriggerInteraction.Ignore))
                    return p + Vector3.up * 1.1f;
            }
        return center + Vector3.up * 1.1f;
    }

    // The zone seen from above: sealed exits, the player and every enemy still standing
    private void DrawZoneMap(Room room)
    {
        LoadMapArt();
        // Sized to the hall's own proportions (a long gallery shows long)
        float scale = Mathf.Min(150f / room.Size.x, 115f / room.Size.y);
        float width = room.Size.x * scale, height = room.Size.y * scale;
        Rect area = new Rect(Screen.width - width - 12f, 12f, width, height);
        MapPlate(area, 0.45f);

        // The hall's floor as marble on the star chart: a shaped hall shows its own outline, the cosmos open between
        Color marble = new Color(0.6f, 0.58f, 0.55f, 1f);   // (opaque, so overlapping pieces of floor don't show their seams)
        if (room.Radius > 0f)
            DrawDisc(area.center, room.Radius * scale, marble);
        else if (room.Floor != null)
            foreach (Rect f in room.Floor)
                UiFont.Fill(new Rect(area.x + (f.xMin + room.Size.x / 2f) * scale, area.y + (room.Size.y / 2f - f.yMax) * scale, f.width * scale, f.height * scale), marble);
        else
            UiFont.Fill(area, marble);

        Vector2 ToMap(Vector3 world)
        {
            Vector3 local = world - room.transform.position;
            float u = Mathf.Clamp01(local.x / room.Size.x + 0.5f);
            float v = Mathf.Clamp01(0.5f - local.z / room.Size.y);
            return new Vector2(area.x + u * area.width, area.y + v * area.height);
        }
        void Dot(Vector2 at, float size, Color color) => UiFont.Fill(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), color);

        // Exits, shown sealed while the fight lasts
        Color seal = new Color(1f, 0.35f, 0.3f);
        foreach (Vector2Int side in room.Exits.Keys)
        {
            Vector2 at = ToMap(room.transform.position + new Vector3(side.x * room.Size.x / 2f, 0f, side.y * room.Size.y / 2f));
            Vector2 size = side.x != 0 ? new Vector2(3f, 11f) : new Vector2(11f, 3f);
            UiFont.Fill(new Rect(at.x - size.x / 2f, at.y - size.y / 2f, size.x, size.y), seal);
        }

        int count = 0;
        foreach (Enemy enemy in Enemy.Alive)
        {
            if (enemy == null || enemy.IsDead || !room.Contains(enemy.transform.position, -2f))
                continue;
            count++;
            Vector2 at = ToMap(enemy.transform.position);
            float size = enemy.IsBoss ? 9f : 5f;
            Dot(at, size + 2f, new Color(0.1f, 0.02f, 0.02f));
            Dot(at, size, enemy.IsBoss ? new Color(0.75f, 0.25f, 0.9f) : new Color(1f, 0.25f, 0.2f));
        }

        // The player: the compass star
        Vector2 me = ToMap(player.transform.position);
        if (mapIcons != null)
            DrawMapIcon(IconHere, new Rect(me.x - 8f, me.y - 8f, 16f, 16f), Color.white);
        else
        {
            Dot(me, 8f, Color.black);
            Dot(me, 6f, new Color(1f, 0.85f, 0.35f));
        }

        GUI.Label(new Rect(area.x - 20f, area.yMax + 4f, area.width + 40f, 16f), $"남은 적 {count}", smallCenterStyle);
    }

    // A scene realm's room in a fight, drawn like DrawZoneMap: its floor as marble on the star chart, the monsters still
    // standing in it, the player's compass star and how many are left
    private void DrawHostedZoneMap(StageHost.Zone zone)
    {
        LoadMapArt();
        Bounds b = zone.area;
        Vector2 size = new Vector2(Mathf.Max(4f, b.size.x), Mathf.Max(4f, b.size.z));
        float scale = Mathf.Min(150f / size.x, 115f / size.y);
        float width = size.x * scale, height = size.y * scale;
        Rect area = new Rect(Screen.width - width - 12f, 12f, width, height);
        MapPlate(area, 0.45f);
        UiFont.Fill(area, new Color(0.6f, 0.58f, 0.55f, 1f));

        Vector2 ToMap(Vector3 world)
        {
            float u = Mathf.Clamp01((world.x - b.min.x) / size.x);
            float v = Mathf.Clamp01((b.max.z - world.z) / size.y);
            return new Vector2(area.x + u * area.width, area.y + v * area.height);
        }
        void Dot(Vector2 at, float s, Color color) => UiFont.Fill(new Rect(at.x - s / 2f, at.y - s / 2f, s, s), color);

        int count = 0;
        foreach (Enemy enemy in Enemy.Alive)
        {
            if (enemy == null || enemy.IsDead || !enemy.isActiveAndEnabled)
                continue;
            Vector3 p = enemy.transform.position;
            if (p.x < b.min.x - 1f || p.x > b.max.x + 1f || p.z < b.min.z - 1f || p.z > b.max.z + 1f)
                continue;
            count++;
            Vector2 at = ToMap(p);
            float s = enemy.IsBoss ? 9f : 5f;
            Dot(at, s + 2f, new Color(0.1f, 0.02f, 0.02f));
            Dot(at, s, enemy.IsBoss ? new Color(0.75f, 0.25f, 0.9f) : new Color(1f, 0.25f, 0.2f));
        }

        Vector2 me = ToMap(player.transform.position);
        if (mapIcons != null)
            DrawMapIcon(IconHere, new Rect(me.x - 8f, me.y - 8f, 16f, 16f), Color.white);
        else
        {
            Dot(me, 8f, Color.black);
            Dot(me, 6f, new Color(1f, 0.85f, 0.35f));
        }
        string caption = string.IsNullOrEmpty(zone.name) ? $"남은 적 {count}" : $"{zone.name} · 남은 적 {count}";
        GUI.Label(new Rect(area.x - 60f, area.yMax + 4f, area.width + 60f, 16f), caption, smallCenterStyle);
    }

    // The boss bar sits at the top centre of the screen (Jotunheim and Muspelheim teams' feedback), its name just above it
    private const float BossBarTop = 44f;

    private void DrawBossBar()
    {
        // Heimsight: the world slows and dims blue around the player
        if (PlayerEffects.Slowed)
            UiFont.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.2f, 0.35f, 0.8f, 0.18f));

        // Every floor's bosses get the same bar — Asgard's: top centre, the name just above it (twin guardians: two of
        // them, one under the other)
        Enemy boss = bossRoom != null ? bossRoom.Boss : StageHost.Boss?.Invoke();
        Enemy second = bossRoom != null ? bossRoom.SecondBoss : null;
        if (second != null)
        {
            float top = BossBarTop;
            foreach (Enemy b in new[] { boss, second })
                if (b != null && !b.IsDead)
                {
                    AsgardBossBar(b, top);
                    top += 42f;
                }
            return;
        }
        if (boss == null || boss.IsDead)
            return;
        Rect bar = AsgardBossBar(boss, BossBarTop);

        // Týr's two phase pips
        if (boss is TyrBoss tyr)
            for (int i = 0; i < 2; i++)
                UiFont.Fill(new Rect(bar.xMax + 8f + i * 12f, bar.y + 3f, 9f, 9f), i < tyr.Phase ? new Color(1f, 0.55f, 0.3f) : new Color(1f, 1f, 1f, 0.25f));

        // Heimdall's phase pips and, during his long charge, how close the player is to breaking it
        if (boss is HeimdallBoss heimdall)
        {
            for (int i = 0; i < 3; i++)
                UiFont.Fill(new Rect(bar.xMax + 8f + i * 12f, bar.y + 3f, 9f, 9f), i < heimdall.Phase ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 1f, 1f, 0.25f));
            if (heimdall.UltimateCharging)
            {
                // (under the bar, now that the bar sits at the top of the screen)
                Rect gauge = new Rect(bar.x + 60f, bar.yMax + 10f, bar.width - 120f, 10f);
                UiFont.Fill(gauge, new Color(0f, 0f, 0f, 0.7f));
                UiFont.Fill(new Rect(gauge.x + 1f, gauge.y + 1f, (gauge.width - 2f) * heimdall.BreakProgress, gauge.height - 2f), new Color(1f, 1f, 1f));
                GUI.Label(new Rect(bar.x - 80f, gauge.yMax + 4f, bar.width + 160f, 18f), "비프로스트 충전 저지 — 강하게 때리거나 넘어뜨리세요", centerStyleBold);
            }
        }
    }

    // Asgard's boss bar: 420 wide at the top centre (on a narrow screen kept clear of the health bar and the test-mode
    // label at the top left), the name above, violet health
    private Rect AsgardBossBar(Enemy boss, float top)
    {
        Rect bar = new Rect(Mathf.Max((Screen.width - 420f) / 2f, 410f), top, 420f, 16f);
        GUI.Label(new Rect(bar.x, bar.y - 24f, bar.width, 22f), boss.DisplayName, centerStyleBold);
        UiFont.Fill(bar, new Color(0f, 0f, 0f, 0.7f));
        UiFont.Fill(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * Mathf.Clamp01(boss.Health / (float)boss.MaxHealth), bar.height - 4f), new Color(0.6f, 0.15f, 0.7f));
        return bar;
    }

    // (no longer drawn — every boss uses AsgardBossBar) A boss's bar across the very top of the screen: thick and blood red, its name
    // on the left and its health on the right, with a pale trail showing the health just lost draining after it
    private float topBarTrail = 1f;
    private Enemy topBarBoss;
    private GUIStyle topBarNameStyle, topBarHpStyle;

    private void DrawTopBossBar(Enemy boss)
    {
        if (topBarNameStyle == null)
        {
            topBarNameStyle = UiFont.Style(15, TextAnchor.MiddleLeft, FontStyle.Bold);
            topBarNameStyle.normal.textColor = new Color(1f, 0.93f, 0.85f);
            topBarHpStyle = UiFont.Style(14, TextAnchor.MiddleRight, FontStyle.Bold);
            topBarHpStyle.normal.textColor = Color.white;
        }
        float ratio = Mathf.Clamp01(boss.Health / (float)boss.MaxHealth);
        if (topBarBoss != boss)
        {
            topBarBoss = boss;
            topBarTrail = ratio;
        }
        topBarTrail = ratio >= topBarTrail ? ratio : Mathf.MoveTowards(topBarTrail, ratio, Time.unscaledDeltaTime * 0.35f);

        // clear of the HP panel on the left and the minimap on the right, as wide as the screen allows
        float margin = Mathf.Min(270f, Mathf.Max(12f, (Screen.width - 320f) / 2f));
        Rect frame = new Rect(margin, 8f, Screen.width - margin * 2f, 30f);
        Rect bar = new Rect(frame.x + 3f, frame.y + 3f, frame.width - 6f, frame.height - 6f);
        UiFont.Fill(frame, new Color(0.12f, 0.02f, 0.02f, 0.92f));                          // dark rim
        UiFont.Fill(new Rect(frame.x, frame.yMax - 2f, frame.width, 2f), new Color(0.75f, 0.15f, 0.1f, 0.9f));
        UiFont.Fill(bar, new Color(0.22f, 0.03f, 0.03f, 0.95f));                            // empty part
        UiFont.Fill(new Rect(bar.x, bar.y, bar.width * topBarTrail, bar.height), new Color(1f, 0.75f, 0.55f, 0.85f));   // health just lost
        UiFont.Fill(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), new Color(0.78f, 0.06f, 0.06f));
        UiFont.Fill(new Rect(bar.x, bar.y, bar.width * ratio, bar.height * 0.4f), new Color(1f, 0.3f, 0.25f, 0.55f));   // sheen on top
        GUI.Label(new Rect(bar.x + 10f, bar.y, bar.width - 20f, bar.height), boss.DisplayName, topBarNameStyle);
        GUI.Label(new Rect(bar.x + 10f, bar.y, bar.width - 20f, bar.height), $"{boss.Health} / {boss.MaxHealth}", topBarHpStyle);
    }

    // The top of an enemy's model (its highest renderer bound), for bars hung over its head
    private static Vector3 HeadTop(Enemy e)
    {
        float top = e.transform.position.y + 2f;
        bool any = false;
        foreach (Renderer r in e.GetComponentsInChildren<Renderer>())
        {
            if (!(r is SkinnedMeshRenderer || r is MeshRenderer))
                continue;
            top = any ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y;
            any = true;
        }
        Vector3 p = e.transform.position;
        return new Vector3(p.x, Mathf.Min(top, p.y + 8f), p.z);
    }

    // Two-step gauge over the player's head while a charged attack builds up
    private void DrawChargeGauge()
    {
        Camera cam = Camera.main;
        if (cam == null || !miner.IsCharging)
            return;
        Vector3 screen = cam.WorldToScreenPoint(player.transform.position + Vector3.up * 1.6f);
        if (screen.z <= 0f)
            return;

        // 대지의 심판: its own gauge — three segments, one per gathering, each filling in turn
        if (miner.JudgementCharge)
        {
            DrawJudgementGauge(new Vector2(screen.x, Screen.height - screen.y - 24f));
            return;
        }
        const float width = 64f, height = 8f;
        Rect bar = new Rect(screen.x - width / 2f, Screen.height - screen.y - 24f, width, height);
        UiFont.Fill(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f), new Color(0f, 0f, 0f, 0.7f));
        float fill = Mathf.Clamp01(miner.ChargeHeld / miner.StageTwoTime);
        int stage = miner.ChargeStage;
        Color color = stage == 2 ? new Color(0.55f, 0.85f, 1f) : stage == 1 ? new Color(1f, 0.6f, 0.15f) : new Color(0.75f, 0.7f, 0.6f);
        UiFont.Fill(new Rect(bar.x, bar.y, bar.width * fill, bar.height), color);
        // Tick where the first stage begins
        float tick = bar.x + bar.width * miner.StageOneTime / miner.StageTwoTime;
        UiFont.Fill(new Rect(tick - 1f, bar.y - 2f, 2f, bar.height + 4f), Color.white);
        if (stage > 0)
            GUI.Label(new Rect(bar.x - 20f, bar.y - 20f, bar.width + 40f, 18f), stage == 2 ? "최대 차지!" : "차지 1단계", smallCenterStyle);
    }

    private static readonly Color[] JudgementColors =
    {
        new Color(1f, 0.6f, 0.15f), new Color(0.55f, 0.85f, 1f), new Color(1f, 0.88f, 0.45f),
    };

    // Three gold-framed cells side by side over the head: the first fills to stage one, the second to the full charge,
    // the third to the third gathering; a filled cell glows, and with all three lit the whole gauge pulses
    private void DrawJudgementGauge(Vector2 centre)
    {
        const float cell = 26f, gap = 5f, height = 10f;
        float total = cell * 3f + gap * 2f;
        float x = centre.x - total / 2f, y = centre.y;
        float[] ends = { miner.StageOneTime, miner.StageTwoTime, miner.StageThreeTime };
        float held = miner.ChargeHeld, start = 0f;
        bool all = held >= ends[2];
        float pulse = all ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 12f) : 0f;
        UiFont.Fill(new Rect(x - 4f, y - 4f, total + 8f, height + 8f), new Color(0f, 0f, 0f, 0.72f));
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect(x + i * (cell + gap), y, cell, height);
            float k = Mathf.Clamp01((held - start) / Mathf.Max(0.01f, ends[i] - start));
            start = ends[i];
            Color gold = new Color(0.85f, 0.68f, 0.3f, 1f);
            UiFont.Fill(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), k >= 1f ? Color.Lerp(gold, Color.white, pulse * 0.6f) : gold * 0.6f);
            UiFont.Fill(r, new Color(0.08f, 0.07f, 0.06f, 1f));
            Color c = JudgementColors[i];
            if (k >= 1f) c = Color.Lerp(c, Color.white, 0.25f + pulse * 0.35f);
            UiFont.Fill(new Rect(r.x, r.y, r.width * k, r.height), c);
        }
        int lit = held >= ends[2] ? 3 : held >= ends[1] ? 2 : held >= ends[0] ? 1 : 0;
        if (lit > 0)
            GUI.Label(new Rect(x - 40f, y - 20f, total + 80f, 18f), lit == 3 ? "대지의 심판!" : $"기 모으기 {lit}/3", smallCenterStyle);
    }

    private void DrawFloaters()
    {
        Camera cam = Camera.main;
        floaters.RemoveAll(f => Time.time - f.born > 0.8f);
        if (cam == null)
            return;
        foreach (var (world, text, color, born) in floaters)
        {
            float age = Time.time - born;
            Vector3 screen = cam.WorldToScreenPoint(world + Vector3.up * age * 1.2f);
            if (screen.z <= 0f) continue;
            GUI.color = new Color(color.r, color.g, color.b, 1f - age / 0.8f);
            GUI.Label(new Rect(screen.x - 50f, Screen.height - screen.y - 14f, 100f, 28f), text, floaterStyle);
        }
        GUI.color = Color.white;
    }

    private void DrawPrompts()
    {
        string prompt = null;
        if (nearbyPedestal != null)
        {
            Rune r = nearbyPedestal.Rune;
            string cost = nearbyPedestal.Price > 0 ? $" · {nearbyPedestal.Price} 황금" : "";
            prompt = $"[F] {r.name} ({RuneLibrary.GodNames[(int)r.god]}) — {r.description}{cost}";
        }
        else if (AtRouteGate)
            prompt = Route.NextIsBoss ? "[C] 갈림길 지도 — 수문장에게 가는 길이 열렸습니다"
                : Route.NextIsMidBoss ? "[C] 갈림길 지도 — 다음 구역에는 중간 보스가 나타납니다"
                : "[C] 갈림길 지도 — 다음으로 갈 구역을 셋 중 하나 고르세요";
        else if (currentRoom != null && currentRoom.RoomKind == Room.Kind.Exit && !TrainingRoom.Active)
            prompt = "빛나는 룬 고리 안에 서서 [C]를 누르면 다음 길을 고릅니다";
        else if (currentRoom != null && currentRoom.RoomKind == Room.Kind.Start && realmIndex == 0 && RunState.RoomsCleared == 0 && !TrainingRoom.Active)
            prompt = "황금 관문을 지나면 다음 구역 · 적을 모두 쓰러뜨려야 관문이 열립니다";

        if (prompt == null)
            return;
        float width = Mathf.Min(centerStyleBold.CalcSize(new GUIContent(prompt)).x + 28f, Screen.width - 16f);
        // (the route gate's prompt sits higher, clear of the skill hints along the bottom)
        bool routeHint = currentRoom != null && currentRoom.RoomKind == Room.Kind.Exit && nearbyPedestal == null;
        Rect r2 = new Rect((Screen.width - width) / 2f, Screen.height * (routeHint ? 0.4f : 0.64f), width, 32f);
        UiFont.Fill(r2, new Color(0f, 0f, 0f, 0.7f));
        GUI.color = nearbyPedestal != null && !nearbyPedestal.CanAfford ? new Color(1f, 0.6f, 0.55f) : Color.white;
        GUI.Label(r2, prompt, centerStyleBold);
        GUI.color = Color.white;
    }

    // The weapon from the armory in hand (another realm's spear, axes or hammer), if any
    private static ArmoryWeapon Armory => ArmoryWeapon.Current != null && ArmoryWeapon.Current.IsActive ? ArmoryWeapon.Current : null;

    // The skill tree of the weapon in hand (null: an armory weapon whose skills came without one)
    private SkillTree CurrentTree => Armory != null ? Armory.Tree : blades != null && blades.IsActive ? DualBladeSkills.Tree : miner.CanCharge ? GreatswordSkills.Tree : StormSkills.Tree;
    private string CurrentTreeName => Armory != null ? Armory.TreeName : blades != null && blades.IsActive ? "쌍검 스킬트리" : miner.CanCharge ? "대검 스킬트리" : "번개 망치 스킬트리";

    // Every weapon's tree: all are reset each run and all gain a point each level
    private static IEnumerable<SkillTree> AllTrees
    {
        get
        {
            yield return GreatswordSkills.Tree;
            yield return DualBladeSkills.Tree;
            yield return StormSkills.Tree;
            foreach (SkillTree tree in ArmoryWeapon.Trees)
                yield return tree;
        }
    }

    // The tree on show in the [K] panel (null: the one of the weapon in hand, picked when the panel opens)
    private SkillTree shownTree;

    // [K]: the skill trees, one tab each — the greatsword's and the hammer's side by side (and the tree of any other
    // weapon in hand). It opens on the weapon in hand; points go into whichever tab is shown.
    private void DrawSkillTree()
    {
        bool twin = blades != null && blades.IsActive;
        SkillTree inHand = Armory != null ? Armory.Tree : twin ? DualBladeSkills.Tree : miner.CanCharge ? GreatswordSkills.Tree : StormSkills.Tree;
        // 번개 and 망치 are one tree now (StormSkills.Tree: the plain fighter's Q/E/R and 묠니르's)
        var tabs = new List<KeyValuePair<string, SkillTree>>
        {
            new KeyValuePair<string, SkillTree>("번개 망치", StormSkills.Tree),
            new KeyValuePair<string, SkillTree>("대검", GreatswordSkills.Tree),
        };
        if (inHand != null && inHand != GreatswordSkills.Tree && inHand != StormSkills.Tree)
            tabs.Insert(0, new KeyValuePair<string, SkillTree>(twin ? "쌍검" : inHand == GauntletSkills.Tree ? "건틀릿" : Armory.TreeName ?? "무기", inHand));
        if (shownTree == null || !tabs.Exists(t => t.Value == shownTree))
            shownTree = inHand != null ? inHand : tabs[0].Value;

        GUI.color = Color.white;   // (whatever the HUD under it left the colour at)
        DrawTree(shownTree);

        // The tabs, over the tree's top left corner
        // (the same rect DrawTree's panel takes; worked out here, not drawn again over the tree)
        float w = Mathf.Min(1040f, Screen.width - 30f), h = Mathf.Min(780f, Screen.height - 22f);
        w = Mathf.Min(w, Screen.width - 20f);
        Rect panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        // tabs up on the points line's left (its text is centred), the note in the middle of the zoom row below
        float k = Mathf.Clamp(h / 760f, 0.6f, 1f);
        float rim = 40f;
        float tx = panel.x + rim + 12f, ty = panel.y + rim - 2f;
        float noteY = panel.y + rim + 34f + 30f * k - 27f;
        foreach (var tab in tabs)
        {
            bool on = tab.Value == shownTree;
            var r = new Rect(tx, ty, 92f, 28f);
            UiFont.Fill(r, on ? new Color(0.85f, 0.62f, 0.2f, 0.95f) : new Color(0.08f, 0.06f, 0.05f, 0.85f));
            UiFont.Fill(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), on ? new Color(0.3f, 0.2f, 0.08f, 0.95f) : new Color(0.16f, 0.12f, 0.1f, 0.9f));
            GUI.color = on ? new Color(1f, 0.88f, 0.55f) : new Color(0.8f, 0.75f, 0.7f);
            string label = tab.Value.Points > 0 ? $"{tab.Key} ({tab.Value.Points})" : tab.Key;
            if (UiSound.Button(r, label, smallCenterStyle))
                shownTree = tab.Value;
            GUI.color = Color.white;
            tx += 98f;
        }

        // A tree whose weapon is not in hand can still be filled in; it counts once that weapon is picked up
        string note = null;
        if (shownTree == GreatswordSkills.Tree && (twin || Armory != null || !miner.CanCharge))
            note = "대검을 들었을 때 적용됩니다 (마을 무기고에서 무기를 바꿀 수 있습니다)";
        else if (shownTree == StormSkills.Tree && inHand != StormSkills.Tree)
            note = "기본 무기나 묠니르(망치)를 들었을 때 적용됩니다 (마을 무기고에서 무기를 바꿀 수 있습니다)";
        if (note != null)
        {
            GUI.color = new Color(1f, 0.6f, 0.5f);
            GUI.Label(new Rect(panel.center.x - 260f, noteY, 520f, 20f), note, smallCenterStyle);
            GUI.color = Color.white;
        }
    }

    // ───────────── Skill trees ─────────────

    private static readonly Color AttackColor = new Color(1f, 0.38f, 0.36f);
    private static readonly Color SurvivalColor = new Color(0.45f, 0.9f, 0.5f);
    private static readonly Color SpeedColor = new Color(0.45f, 0.75f, 1f);
    private static readonly Color Wood = new Color(0.34f, 0.23f, 0.14f);
    private static Texture2D discTexture;
    private readonly Dictionary<string, Texture2D> skillIcons = new Dictionary<string, Texture2D>();
    private GUIStyle glyphStyle, infoTitleStyle, infoTextStyle, branchLabelStyle, plaqueStyle, frameStyle;

    // The tree's painted dress (VARCO, Resources/UI/skilltree_*): Yggdrasil under the night sky behind it, a carved
    // Norse frame round the window, a gold rune medallion round every node, a sheet of 4x4 stat icons and the golden
    // leaves that grow on whatever has been learned
    private Texture2D treeBg, treeFrame, treeRing, treeIcons, treeLeaf;
    private bool treeArtLoaded;

    // The tree's canvas (its own units, laid out roomy enough that no nodes touch) and the zoomable view onto it
    private const float TreeRow = 84f, TreeTop = 90f, TreeBottom = 110f;
    private float TreeWidth = 2000f;   // the canvas's width: a smaller tree (the spear's) narrows it, so the panel draws it larger
    private float treeNodeScale = 1f;
    private float treeZoom = 1f, treeCanvasH = 1000f;
    private Vector2 treePan, treeViewSize;
    private string treeViewKey;
    private bool treeDragging;

    // The whole tree in view
    private void FitTreeView(Rect area, float canvasH, float fit)
    {
        treeCanvasH = canvasH;
        treeViewSize = area.size;
        treeZoom = fit;
        treePan = new Vector2((area.width - TreeWidth * fit) / 2f, (area.height - canvasH * fit) / 2f);
    }

    // Zoom by a factor, keeping the point under the cursor (in view coordinates) where it is
    private void ZoomTreeAt(Vector2 at, float factor, float fit)
    {
        float zoom = Mathf.Clamp(treeZoom * factor, fit * 0.9f, Mathf.Max(1.6f, fit * 5f));
        Vector2 world = (at - treePan) / treeZoom;
        treeZoom = zoom;
        treePan = at - world * zoom;
        ClampTreePan(treeViewSize, treeCanvasH);
    }

    // Keep the canvas from sliding out of view: centred along an axis where it fits, otherwise edge to edge
    private void ClampTreePan(Vector2 view, float canvasH)
    {
        treeViewSize = view;
        treeCanvasH = canvasH;
        Vector2 size = new Vector2(TreeWidth, canvasH) * treeZoom;
        const float slack = 60f;
        treePan.x = size.x <= view.x ? (view.x - size.x) / 2f : Mathf.Clamp(treePan.x, view.x - size.x - slack, slack);
        treePan.y = size.y <= view.y ? (view.y - size.y) / 2f : Mathf.Clamp(treePan.y, view.y - size.y - slack, slack);
    }

    private void LoadTreeArt()
    {
        if (treeArtLoaded)
            return;
        treeArtLoaded = true;
        treeBg = Resources.Load<Texture2D>("UI/skilltree_bg");
        treeFrame = Resources.Load<Texture2D>("UI/skilltree_frame");
        treeRing = Resources.Load<Texture2D>("UI/skilltree_ring");
        treeIcons = Resources.Load<Texture2D>("UI/skilltree_icons");
        treeLeaf = Resources.Load<Texture2D>("UI/skilltree_leaf");
    }

    // Which cell of the stat icon sheet each stat node shows (reading order: sword, eye, crystal, spear, daggers,
    // heart, shield, droplet, thorns, ward, boot, afterimage, gold, lightning, fist, wind)
    private static readonly Dictionary<string, int> StatArt = BuildStatArt();

    private static Dictionary<string, int> BuildStatArt()
    {
        var art = new Dictionary<string, int>();
        void Set(int cell, params string[] ids) { foreach (string id in ids) art[id] = cell; }
        Set(0, "edge", "gs_heft", "warmonger", "gs_warmonger");
        Set(1, "vital", "hunter", "gs_crush", "gs_hunter");
        Set(2, "slayer", "gs_slayer", "hone", "gs_hone");
        Set(3, "gs_reach");
        Set(4, "gust", "quick", "gs_quick", "swift_tip", "gs_swift");
        Set(5, "sturdy", "giant", "tenacity", "gs_sturdy", "gs_giant");
        Set(6, "hardened", "undying", "gs_hardened", "gs_undying");
        Set(7, "thirst", "breath", "gs_thirst", "gs_breath");
        Set(8, "thorns", "gs_thorns");
        Set(9, "veil", "gs_bulwark");
        Set(10, "light", "sprint", "gs_light", "gs_sprint");
        Set(11, "afterimage", "evasion", "shadowlord", "gs_after", "gs_evasion", "gs_stormlord");
        Set(12, "goldhand", "gs_goldhand");
        Set(13, "thunder", "gs_thunder");
        Set(14, "basics", "gs_basics");
        Set(15, "breath_in", "gs_breath_in", "windstep", "gs_wind", "windlord", "gs_windlord");
        return art;
    }

    // One cell of the icon sheet
    private void DrawStatIcon(int cell, Rect r)
    {
        const float u = 0.25f;
        int col = cell % 4, row = cell / 4;
        GUI.DrawTextureWithTexCoords(r, treeIcons, new Rect(col * u, 1f - (row + 1) * u, u, u));
    }

    // A golden leaf at an angle (degrees, 0 = pointing up)
    private void DrawLeaf(Vector2 at, float size, float angle, Color tint)
    {
        if (treeLeaf == null)
            return;
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, at);
        GUI.color = tint;
        GUI.DrawTexture(new Rect(at.x - size / 2f, at.y - size, size, size), treeLeaf);
        GUI.color = Color.white;
        GUI.matrix = m;
    }

    private static Color BranchColor(SkillTree.Branch branch) => branch switch
    {
        SkillTree.Branch.Attack => AttackColor,
        SkillTree.Branch.Survival => SurvivalColor,
        SkillTree.Branch.Speed => SpeedColor,
        _ => new Color(1f, 0.82f, 0.4f),
    };

    // A soft-edged white disc, tinted for every node of the tree
    private static Texture2D Disc()
    {
        if (discTexture != null)
            return discTexture;
        const int n = 64;
        discTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f;
                byte a = (byte)(Mathf.Clamp01(n / 2f - Mathf.Sqrt(dx * dx + dy * dy)) * 255f);
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
        discTexture.SetPixels32(px);
        discTexture.Apply();
        return discTexture;
    }

    private static void DrawDisc(Vector2 center, float radius, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), Disc());
        GUI.color = Color.white;
    }

    // A straight bough from one point to another
    private static void DrawBough(Vector2 from, Vector2 to, float width, Color color)
    {
        Vector2 d = to - from;
        float length = d.magnitude;
        if (length < 1f)
            return;
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, from);
        UiFont.Fill(new Rect(from.x, from.y - width / 2f, length, width), color);
        GUI.matrix = m;
        DrawDisc(from, width / 2f, color);   // rounded joints
    }

    // The tree grows from a trunk of basics at the bottom, then splits: 공격 leans left, 생존 climbs straight up,
    // 이속 leans right, each forking again and again into a crown. Hover a node to read it, click to learn it
    // (stat nodes take several points).
    private void DrawTree(SkillTree tree)
    {
        TreeWidth = tree.CanvasWidth;
        treeNodeScale = tree.NodeScale;
        float w = Mathf.Min(1040f, Screen.width - 30f), h = Mathf.Min(780f, Screen.height - 22f);
        Rect panel = CenteredPanel(w, h);
        float k = Mathf.Clamp(h / 760f, 0.6f, 1f);
        LoadTreeArt();
        glyphStyle ??= UiFont.Style(13, TextAnchor.MiddleCenter, FontStyle.Bold);
        infoTitleStyle ??= UiFont.Style(16, TextAnchor.UpperLeft, FontStyle.Bold);
        infoTextStyle ??= UiFont.Style(13, TextAnchor.UpperLeft);
        branchLabelStyle ??= UiFont.Style(15, TextAnchor.MiddleCenter, FontStyle.Bold);
        plaqueStyle ??= UiFont.Style(16, TextAnchor.MiddleCenter, FontStyle.Bold);
        glyphStyle.fontSize = Mathf.RoundToInt(13 * k);

        // Yggdrasil under the night sky, darkened so the tree drawn over it reads
        if (treeBg != null)
        {
            GUI.DrawTexture(panel, treeBg, ScaleMode.ScaleAndCrop);
            UiFont.Fill(panel, new Color(0.02f, 0.01f, 0.05f, 0.45f));
        }
        // The carved frame's edge, drawn first so its inner rim is known (the frame itself goes on last, over everything)
        const float frameOut = 10f;
        Rect frame = new Rect(panel.x - frameOut, panel.y - frameOut, panel.width + frameOut * 2f, panel.height + frameOut * 2f);
        float rim = treeFrame != null ? 40f : 0f;   // how far the frame's bars reach in over the panel

        int points = tree.Points;
        if (treeFrame == null)
            GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 30f), tree.title, titleStyle);   // (with the frame it sits on the plaque)
        GUI.color = inHub ? new Color(0.6f, 0.85f, 1f) : points > 0 ? new Color(1f, 0.85f, 0.35f) : Color.white;
        GUI.Label(new Rect(panel.x, panel.y + rim + 4f, panel.width, 20f), inHub
            ? "미리보기 · 비프로스트로 여정을 떠나면 포인트를 받아 배울 수 있습니다 · 죽으면 초기화"
            : $"기술 포인트 {points}  ·  투자 {tree.Spent}  ·  레벨 업마다 +1 (Lv {RunState.Level}, {RunState.Xp}/{RunState.XpToNext})  ·  죽으면 초기화", smallCenterStyle);
        GUI.color = Color.white;

        int rows = tree.TopRow;
        float infoH = 70f * k;
        float topGap = rim + 34f + 30f * k;
        var area = new Rect(panel.x + rim + 90f, panel.y + topGap, panel.width - rim * 2f - 120f, panel.height - topGap - infoH - rim - 30f * k);

        // The tree is laid out on a large canvas of its own, roomy enough that no two nodes touch, and the window
        // looks at it through a view that zooms (mouse wheel, + / - / 맞춤) and pans (drag)
        const float trunkStep = 1.2f;   // the trunk's nodes stand in one column, so its rows get a little more room
        float Height(float row) => row <= SkillTree.TrunkRows - 1 ? row * trunkStep : (SkillTree.TrunkRows - 1) * trunkStep + (row - (SkillTree.TrunkRows - 1));
        float canvasH = TreeTop + Height(rows) * TreeRow + TreeBottom;
        Vector2 World(float x, float row) => new Vector2(TreeWidth / 2f + x * TreeWidth / 2f, TreeTop + (Height(rows) - Height(row)) * TreeRow);
        float fit = Mathf.Min(area.width / TreeWidth, area.height / canvasH);
        string viewKey = $"{tree.title}|{area.width:0}x{area.height:0}";
        if (treeViewKey != viewKey)
        {
            treeViewKey = viewKey;
            FitTreeView(area, canvasH, fit);
        }

        // View controls over the canvas's top right corner
        var zoomIn = new Rect(area.xMax - 112f, area.y - 30f, 28f, 24f);
        var zoomOut = new Rect(area.xMax - 80f, area.y - 30f, 28f, 24f);
        var fitButton = new Rect(area.xMax - 48f, area.y - 30f, 48f, 24f);
        if (UiSound.Button(zoomIn, "+", buttonStyle)) ZoomTreeAt(area.size / 2f, 1.25f, fit);
        if (UiSound.Button(zoomOut, "-", buttonStyle)) ZoomTreeAt(area.size / 2f, 0.8f, fit);
        if (UiSound.Button(fitButton, "맞춤", buttonStyle)) FitTreeView(area, canvasH, fit);
        GUI.Label(new Rect(area.xMax - 230f, area.y - 28f, 112f, 20f), $"확대 {treeZoom / fit * 100f:0}%", smallStyleLeft);
        GUI.Label(new Rect(area.x, area.y - 28f, 360f, 20f), "휠: 확대·축소   드래그: 이동", smallStyleLeft);

        float z = treeZoom;
        Vector2 S(Vector2 world) => treePan + world * z;                   // canvas to view (inside the clipped group)
        Vector2 Pos(SkillTree.Node n) => S(World(n.x, n.row));
        float Radius(SkillTree.Node n) => z * treeNodeScale * (n.kind == SkillTree.Kind.Skill ? 34f : n.kind == SkillTree.Kind.Combo ? 29f : n.kind == SkillTree.Kind.Upgrade ? 25f : 22f);
        glyphStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(17f * z), 7, 30);
        branchLabelStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(22f * z), 9, 34);

        // The trunk's basics, then each row of the branches opening once enough points are in the tree: its step
        // is marked down the left side, beside wherever that row is in view
        GUI.color = new Color(1f, 0.85f, 0.5f, 0.6f);
        float basicsY = area.y + S(World(0f, 1.5f)).y;
        if (basicsY > area.y && basicsY < area.yMax)
            GUI.Label(new Rect(panel.x + rim + 8f, basicsY - 10f, 70f, 20f), "기본기", smallStyleLeft);
        for (int row = SkillTree.TrunkRows; row <= rows; row++)
        {
            float y = area.y + S(World(0f, row)).y;
            if (y < area.y || y > area.yMax)
                continue;
            int need = tree.PointsToOpenRow(row);
            bool open = tree.Spent >= need;
            int step = row - SkillTree.TrunkRows + 1;
            UiFont.Fill(new Rect(area.x - 4f, y, area.width + 8f, 1f), new Color(1f, 1f, 1f, open ? 0.05f : 0.03f));
            GUI.color = open ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 0.6f, 0.5f, 0.75f);
            GUI.Label(new Rect(panel.x + rim + 8f, y - 10f, 110f, 20f), open ? $"{step}단계" : $"{step}단계 · 투자 {need}", smallStyleLeft);
        }
        GUI.color = Color.white;

        // Everything on the canvas is drawn clipped to the view
        GUI.BeginGroup(area);
        Event e = Event.current;
        Vector2 mouse = e.mousePosition;
        bool mouseInView = new Rect(0f, 0f, area.width, area.height).Contains(mouse);
        if (e.type == EventType.ScrollWheel && mouseInView)
        {
            ZoomTreeAt(mouse, e.delta.y > 0f ? 1f / 1.15f : 1.15f, fit);
            e.Use();
            z = treeZoom;
        }

        // The trunk rising out of the ground to the first node, then every bough from parent to child: bark first,
        // the branch's colour along the ones already grown into. The trunk is thickest; the boughs thin as they climb.
        SkillTree.Node root = tree.Nodes[0];
        Vector2 rootPos = Pos(root);
        Vector2 ground = S(World(0f, -0.6f));
        DrawBough(new Vector2(rootPos.x, ground.y), rootPos, 34f * z, Wood);
        UiFont.Fill(new Rect(rootPos.x - 100f * z, ground.y - 4f * z, 200f * z, 8f * z), new Color(0.25f, 0.18f, 0.12f));
        foreach (float side in new[] { -1f, 1f })   // a couple of roots spreading into the ground
            DrawBough(new Vector2(rootPos.x, ground.y - 6f * z), new Vector2(rootPos.x + side * 70f * z, ground.y + 6f * z), 10f * z, Wood);
        foreach (SkillTree.Node node in tree.Nodes)
        {
            if (node.parent == null)
                continue;
            SkillTree.Node parent = tree.Find(node.parent);
            float thickness = (node.branch == SkillTree.Branch.Trunk ? 32f
                : Mathf.Lerp(20f, 6f, (node.row - SkillTree.TrunkRows) / (float)(rows - SkillTree.TrunkRows))) * z;
            DrawBough(Pos(parent), Pos(node), thickness, Wood);
            if (tree.Has(node.id))
                DrawBough(Pos(parent), Pos(node), thickness * 0.45f, BranchColor(node.branch));
        }

        // Each branch's name beside the node at its top
        foreach (var (branch, label) in new[] { (SkillTree.Branch.Attack, tree.BranchLabels[0]), (SkillTree.Branch.Survival, tree.BranchLabels[1]), (SkillTree.Branch.Speed, tree.BranchLabels[2]) })
        {
            SkillTree.Node top = tree.Nodes.Where(n => n.branch == branch).OrderByDescending(n => n.row).FirstOrDefault();
            if (top == null)
                continue;   // (a weapon's own tree, like the spear's, has no such branch)
            Vector2 p = Pos(top);
            float side = branch == SkillTree.Branch.Attack ? -1f : 1f;
            GUI.color = BranchColor(branch);
            GUI.Label(new Rect(p.x + side * (Radius(top) + 60f * z) - 60f, p.y - 14f, 120f, 28f), label, branchLabelStyle);
        }
        GUI.color = Color.white;

        // The nodes
        SkillTree.Node hovered = null;
        float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5f);
        foreach (SkillTree.Node node in tree.Nodes)
        {
            Vector2 c = Pos(node);
            float r = Radius(node);
            int rank = tree.Rank(node.id);
            bool maxed = tree.IsMaxed(node);
            bool unlocked = tree.IsUnlocked(node);
            bool learnable = !inHub && tree.CanLearn(node);
            Color tone = BranchColor(node.branch);
            if (mouseInView && (mouse - c).magnitude <= r + 3f)
                hovered = node;

            Color ring = maxed ? new Color(1f, 0.82f, 0.35f) : learnable ? Color.Lerp(tone * 0.6f, Color.white, pulse * 0.5f) : rank > 0 ? tone : unlocked ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.25f, 0.24f, 0.24f);

            // Learned nodes have leaves: a pair at the trunk, more as a stat node's ranks fill
            if (rank > 0 && treeLeaf != null)
            {
                Color leaf = node.branch == SkillTree.Branch.Trunk ? new Color(1f, 0.85f, 0.45f) : Color.Lerp(tone, new Color(1f, 0.85f, 0.45f), 0.45f);
                int leaves = 1 + Mathf.Min(rank, 2);
                float lean = node.x < -0.05f ? -1f : node.x > 0.05f ? 1f : (node.row % 2 == 0 ? 1f : -1f);
                for (int i = 0; i < leaves; i++)
                    DrawLeaf(c, r * 2.2f, lean * (40f + i * 38f) * (i % 2 == 0 ? 1f : -1f) + (i == 2 ? lean * 15f : 0f), leaf);
            }

            if (hovered == node || learnable)
                DrawDisc(c, r * (hovered == node ? 1.45f : 1.3f), new Color(ring.r, ring.g, ring.b, hovered == node ? 0.45f : 0.22f * pulse));
            if (treeRing == null)
                DrawDisc(c, r, ring);
            DrawDisc(c, r * (treeRing != null ? 0.97f : 0.85f), rank > 0 ? Color.Lerp(tone, Color.black, 0.45f) : unlocked ? new Color(0.17f, 0.16f, 0.15f) : new Color(0.1f, 0.1f, 0.1f));

            if (node.kind == SkillTree.Kind.Stat && treeIcons != null && StatArt.TryGetValue(node.id, out int cell))
            {
                float s = r * 1.5f;
                GUI.color = rank > 0 ? Color.white : unlocked ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.32f, 0.32f, 0.32f);
                DrawStatIcon(cell, new Rect(c.x - s / 2f, c.y - s / 2f, s, s));
                GUI.color = Color.white;
            }
            else if (node.icon != null)
            {
                if (!skillIcons.TryGetValue(node.icon, out Texture2D icon))
                    skillIcons[node.icon] = icon = Resources.Load<Texture2D>("UI/" + node.icon);
                if (icon != null)
                {
                    float s = r * (node.icon.StartsWith("SpearTree/") ? 1.9f : 1.25f);   // (the spear's round pictures fill the medallion)
                    GUI.color = rank > 0 ? Color.white : unlocked ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.35f, 0.35f, 0.35f);
                    GUI.DrawTexture(new Rect(c.x - s / 2f, c.y - s / 2f, s, s), icon);
                    GUI.color = Color.white;
                }
            }
            else
            {
                GUI.color = rank > 0 ? Color.white : unlocked ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.45f, 0.45f, 0.45f);
                GUI.Label(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), node.glyph, glyphStyle);
                GUI.color = Color.white;
            }

            // The gold rune medallion round it, tinted by its state
            if (treeRing != null)
            {
                float s = r * 1.15f * 2.3f;
                GUI.color = maxed ? new Color(1f, 0.95f, 0.75f) : rank > 0 ? Color.Lerp(Color.white, tone, 0.25f)
                    : learnable ? Color.Lerp(new Color(0.75f, 0.7f, 0.6f), tone, 0.4f + 0.3f * pulse)
                    : unlocked ? new Color(0.62f, 0.6f, 0.56f) : new Color(0.3f, 0.29f, 0.28f);
                GUI.DrawTexture(new Rect(c.x - s / 2f, c.y - s / 2f, s, s), treeRing);
                GUI.color = Color.white;
            }

            // Ranks of a stat node: a pip each under it
            if (node.maxRank > 1)
            {
                float pip = Mathf.Max(2f, 5f * z), gap = 13f * z;
                float x0 = c.x - (node.maxRank - 1) * gap / 2f;
                for (int i = 0; i < node.maxRank; i++)
                    DrawDisc(new Vector2(x0 + i * gap, c.y + r * 1.35f + 4f * z), pip, i < rank ? new Color(1f, 0.82f, 0.35f) : new Color(1f, 1f, 1f, 0.25f));
            }

            if (learnable && mouseInView && UiSound.Button(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), GUIContent.none, GUIStyle.none) && tree.Learn(node))
            {
                string ranked = node.maxRank > 1 ? $" ({tree.Rank(node.id)}/{node.maxRank})" : "";
                Toast($"스킬트리: {node.name}{ranked} — {node.effect}", BranchColor(node.branch));
            }
        }

        // Dragging anywhere off the nodes pans the view (a press on a node goes to its button above)
        if (e.type == EventType.MouseDown && mouseInView)
        {
            treeDragging = true;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && treeDragging)
        {
            treePan += e.delta;
            ClampTreePan(area.size, canvasH);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && treeDragging)
        {
            treeDragging = false;
            e.Use();
        }
        GUI.EndGroup();

        // The hovered node's card along the bottom
        var info = new Rect(panel.x + rim + 12f, panel.yMax - infoH - rim - 6f, panel.width - rim * 2f - 24f, infoH);
        UiFont.Fill(info, new Color(0.02f, 0.01f, 0.04f, 0.7f));
        UiFont.Fill(new Rect(info.x, info.y, info.width, 1f), new Color(1f, 0.8f, 0.4f, 0.5f));
        if (hovered != null)
        {
            int rank = tree.Rank(hovered.id);
            string kind = hovered.kind == SkillTree.Kind.Skill ? "기술" : hovered.kind == SkillTree.Kind.Combo ? "연속 베기" : hovered.kind == SkillTree.Kind.Upgrade ? "강화" : "능력치";
            GUI.color = BranchColor(hovered.branch);
            GUI.Label(new Rect(info.x + 12f, info.y + 6f, info.width - 24f, 22f), $"{hovered.name}   <{kind}>   {(hovered.maxRank > 1 ? $"{rank}/{hovered.maxRank}" : rank > 0 ? "습득" : "")}", infoTitleStyle);
            GUI.color = Color.white;
            string effect = hovered.maxRank > 1 ? $"단계마다 {hovered.effect}" : hovered.effect;
            GUI.Label(new Rect(info.x + 12f, info.y + 30f * k, info.width - 24f, 22f), effect, infoTextStyle);
            string status;
            if (tree.IsMaxed(hovered)) status = "모두 배웠습니다";
            else if (!tree.ParentReady(hovered))
            {
                var need = new List<string>();
                if (hovered.parent != null && !tree.Has(hovered.parent)) need.Add($"'{tree.Find(hovered.parent).name}'");
                if (hovered.also != null && !tree.Has(hovered.also)) need.Add($"'{tree.Find(hovered.also).name}'");
                status = $"먼저 {string.Join(", ", need)}을(를) 배워야 합니다";
            }
            else if (!tree.RowOpen(hovered)) status = $"{hovered.row}단계는 트리에 포인트 {tree.PointsToOpenRow(hovered.row)}개를 투자하면 열립니다 (현재 {tree.Spent})";
            else if (inHub) status = "여정 중에 배울 수 있습니다";
            else if (points <= 0) status = "기술 포인트가 없습니다 · 레벨 업으로 얻습니다";
            else status = "클릭해서 배우기 (포인트 1)";
            GUI.color = tree.CanLearn(hovered) && !inHub ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0.7f);
            GUI.Label(new Rect(info.x + 12f, info.y + 52f * k, info.width - 24f, 22f), status, infoTextStyle);
            GUI.color = Color.white;
        }
        else
            GUI.Label(info, $"노드에 마우스를 올리면 설명이 나옵니다 · 클릭해서 배우기   ·   {(tree.MaxCombo > 1 ? $"연속 베기 {tree.ComboLength}/{tree.MaxCombo}타   ·   " : "")}[K] 또는 [Esc] 닫기", smallCenterStyle);

        // The carved Norse frame over the window's edge (nine-sliced so its dragon-head corners keep their shape)
        if (treeFrame != null)
        {
            frameStyle ??= new GUIStyle { border = new RectOffset(88, 88, 74, 74), normal = { background = treeFrame } };
            if (Event.current.type == EventType.Repaint)
                frameStyle.Draw(frame, false, false, false, false);
            GUI.color = new Color(1f, 0.86f, 0.5f);
            GUI.Label(new Rect(panel.center.x - 120f, frame.y + 10f, 240f, 28f), tree.title, plaqueStyle);   // over the plaque
            GUI.color = Color.white;
        }
        else
            GUI.Label(new Rect(panel.x, panel.yMax - 24f, panel.width, 20f), "[K] 또는 [Esc] 닫기", smallCenterStyle);
    }

    private static readonly Color TokenColor = new Color(0.55f, 0.85f, 1f);
    private static readonly Color ForgeGold = new Color(1f, 0.78f, 0.35f);

    // A forge token: a small blue-steel diamond
    private static void TokenIcon(Vector2 center, float size)
    {
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, center);
        UiFont.Fill(new Rect(center.x - size / 2f, center.y - size / 2f, size, size), new Color(0.2f, 0.35f, 0.5f));
        UiFont.Fill(new Rect(center.x - size / 2f + 2f, center.y - size / 2f + 2f, size - 4f, size - 4f), TokenColor);
        GUI.matrix = m;
    }

    // The village HUD: tokens, what the forge has given so far, and what's nearby
    private void DrawHubInfo(Rect bar)
    {
        float y = bar.yMax + 6f;
        TokenIcon(new Vector2(bar.x + 7f, y + 11f), 11f);
        GUI.color = TokenColor;
        GUI.Label(new Rect(bar.x + 20f, y, 220f, 22f), $"토큰 {MetaProgress.Tokens}", textStyleBold);
        GUI.color = Color.white;
        GUI.Label(new Rect(bar.x, y + 24f, 300f, 20f), "미드가르드 · 시작의 마을", smallStyleLeft);

        var forged = MetaProgress.Upgrades.Where(u => MetaProgress.LevelOf(u.id) > 0).Select(MetaProgress.Current).ToList();
        if (forged.Count > 0)
            GUI.Label(new Rect(bar.x, y + 44f, 420f, 20f), "대장간 강화: " + string.Join(" · ", forged), smallStyleLeft);

        string prompt = nearForge ? "[F] 브로크의 대장간 — 토큰으로 장비 강화"
            : nearArmory ? "[F] 무기고 — 무기 바꾸기"
            : nearTraining ? "[F] 훈련장 — 허수아비로 스킬·데미지 시험 (나가면 마을로)"
            : hub.NearGate(player.transform.position) ? "비프로스트에 들어서면 여정이 시작됩니다"
            : null;
        if (prompt == null)
            return;
        float width = Mathf.Min(centerStyleBold.CalcSize(new GUIContent(prompt)).x + 28f, Screen.width - 16f);
        Rect r = new Rect((Screen.width - width) / 2f, Screen.height * 0.64f, width, 32f);
        UiFont.Fill(r, new Color(0f, 0f, 0f, 0.7f));
        GUI.color = nearForge ? ForgeGold : nearArmory ? new Color(0.85f, 0.75f, 1f) : nearTraining ? new Color(0.6f, 0.85f, 1f) : TokenColor;
        GUI.Label(r, prompt, centerStyleBold);
        GUI.color = Color.white;
    }

    private static readonly Color ArmoryViolet = new Color(0.85f, 0.75f, 1f);
    private readonly Dictionary<CharacterProfile.WeaponKind, Texture2D> armoryIcons = new Dictionary<CharacterProfile.WeaponKind, Texture2D>();
    private GUIStyle armoryTextStyle;

    private Texture2D ArmoryIcon(CharacterProfile.WeaponKind weapon)
    {
        // every weapon's own picture now (WeaponIcons); the old skill art stands in for the twin blades if theirs is missing
        if (!armoryIcons.TryGetValue(weapon, out Texture2D icon) || icon == null)
            armoryIcons[weapon] = icon = WeaponIcons.For(weapon) ??
                (weapon == CharacterProfile.WeaponKind.DualBlades ? Resources.Load<Texture2D>("UI/dual_flurry") : null);
        return icon;
    }

    // The village armory: take up a different weapon (each weapon is carried by its own fighter)
    private void DrawArmory()
    {
        if (switcher == null || switcher.Characters == null)
            return;
        var fighters = switcher.Characters;
        // (more than four weapons — the other realms' too — sit in two columns)
        int columns = fighters.Count > 4 ? 2 : 1;
        int rows = (fighters.Count + columns - 1) / columns;
        float cardWidth = Mathf.Min(652f, (Screen.width - 80f - (columns - 1) * 16f) / columns);
        Rect panel = CenteredPanel(48f + cardWidth * columns + (columns - 1) * 16f, 150f + rows * 104f);
        UiFont.Fill(new Rect(panel.x, panel.y, panel.width, 3f), ArmoryViolet);
        GUI.color = ArmoryViolet;
        GUI.Label(new Rect(panel.x, panel.y + 14f, panel.width, 34f), "무기고", titleStyle);
        GUI.color = new Color(0.85f, 0.8f, 0.72f);
        GUI.Label(new Rect(panel.x, panel.y + 50f, panel.width, 20f), "손에 맞는 무기를 골라 여정에 나서세요 · 무기마다 다른 전사가 들고 갑니다", smallCenterStyle);
        GUI.color = Color.white;

        float top = panel.y + 84f;
        for (int i = 0; i < fighters.Count; i++)
        {
            CharacterProfile fighter = fighters[i];
            if (fighter == null)
                continue;
            bool equipped = fighter == switcher.Current;
            Rect card = new Rect(panel.x + 24f + (i % columns) * (cardWidth + 16f), top + (i / columns) * 104f, cardWidth, 94f);
            UiFont.Fill(card, equipped ? ArmoryViolet : new Color(0.32f, 0.29f, 0.26f));
            UiFont.Fill(new Rect(card.x + 2f, card.y + 2f, card.width - 4f, card.height - 4f), equipped ? new Color(0.2f, 0.15f, 0.28f) : new Color(0.15f, 0.14f, 0.13f));

            float textX = card.x + 14f;
            Texture2D icon = ArmoryIcon(fighter.Weapon);
            if (icon != null)
            {
                GUI.DrawTexture(new Rect(card.x + 10f, card.y + 10f, 74f, 74f), icon);
                textX = card.x + 96f;
            }
            armoryTextStyle ??= new GUIStyle(skillTextStyle) { alignment = TextAnchor.UpperLeft, wordWrap = true };
            GUI.Label(new Rect(textX, card.y + 8f, card.xMax - textX - 120f, 24f), $"{fighter.WeaponName}  ·  {fighter.DisplayName}", textStyleBold);
            GUI.Label(new Rect(textX, card.y + 36f, card.xMax - textX - 130f, 52f), fighter.WeaponDescription, armoryTextStyle);
            Rect button = new Rect(card.xMax - 118f, card.center.y - 18f, 104f, 36f);
            if (equipped)
            {
                GUI.color = ArmoryViolet;
                GUI.Label(button, "장착 중", centerStyleBold);
                GUI.color = Color.white;
            }
            else if (!MetaProgress.IsUnlocked(fighter, i))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                GUI.Label(button, "잠김\n(대장간 해금)", centerStyleBold);
                GUI.color = Color.white;
            }
            else if (UiSound.Button(button, "장착", buttonStyle))
            {
                switcher.Equip(i);
                Toast($"무기 장착: {fighter.WeaponName} — {fighter.DisplayName}", ArmoryViolet);
            }
        }
        GUI.Label(new Rect(panel.x, panel.yMax - 28f, panel.width, 20f), "[F] 또는 [Esc] 닫기", smallCenterStyle);
    }

    // Brokkr's forge: spend tokens on upgrades that last across every journey — four tabs, and 초기화 (all back)
    private int forgeTab;
    private bool confirmMetaReset;

    private void DrawForge()
    {
        Rect panel = CenteredPanel(700f, Mathf.Min(560f, Screen.height - 12f));
        UiFont.Fill(new Rect(panel.x, panel.y, panel.width, 3f), ForgeGold);
        UiFont.Fill(new Rect(panel.x, panel.yMax - 3f, panel.width, 3f), new Color(0.75f, 0.35f, 0.1f));
        GUI.color = ForgeGold;
        GUI.Label(new Rect(panel.x, panel.y + 10f, panel.width, 34f), "브로크의 대장간", titleStyle);
        GUI.color = Color.white;

        // Tokens on hand
        Rect purse = new Rect(panel.center.x - 70f, panel.y + 46f, 140f, 26f);
        UiFont.Fill(purse, new Color(0f, 0f, 0f, 0.45f));
        TokenIcon(new Vector2(purse.x + 20f, purse.center.y), 12f);
        GUI.color = TokenColor;
        GUI.Label(new Rect(purse.x + 34f, purse.y + 1f, purse.width - 40f, 24f), $"토큰 {MetaProgress.Tokens}", textStyleBold);
        GUI.color = Color.white;

        // Tabs
        for (int g = 0; g < MetaProgress.GroupNames.Length; g++)
        {
            GUI.color = forgeTab == g ? ForgeGold : Color.white;
            if (UiSound.Button(new Rect(panel.x + 24f + g * 166f, panel.y + 80f, 158f, 30f), MetaProgress.GroupNames[g], buttonStyle)) { forgeTab = g; confirmMetaReset = false; }
        }
        GUI.color = Color.white;

        const float cardW = 316f, cardH = 76f, gap = 14f;
        float left = panel.center.x - cardW - gap / 2f;
        float top = panel.y + 120f;
        if (forgeTab == (int)MetaProgress.Group.Weapons)
            DrawWeaponUnlocks(left, top, cardW, cardH, gap);
        else
        {
            int n = 0;
            foreach (MetaProgress.Upgrade u in MetaProgress.Upgrades)
            {
                if ((int)u.group != forgeTab) continue;
                Rect card = new Rect(left + (n % 2) * (cardW + gap), top + (n / 2) * (cardH + 8f), cardW, cardH);
                n++;
                int level = MetaProgress.LevelOf(u.id);
                bool maxed = MetaProgress.IsMaxed(u);
                bool affordable = MetaProgress.CanBuy(u);
                UiFont.Fill(card, affordable ? ForgeGold : new Color(0.32f, 0.29f, 0.26f));
                UiFont.Fill(new Rect(card.x + 2f, card.y + 2f, card.width - 4f, card.height - 4f), level > 0 ? new Color(0.24f, 0.18f, 0.12f) : new Color(0.17f, 0.15f, 0.13f));
                GUI.Label(new Rect(card.x + 10f, card.y + 4f, 200f, 22f), u.name, textStyleBold);
                GUI.Label(new Rect(card.x + 10f, card.y + 26f, 200f, 18f), $"단계마다 {u.effect}", smallStyleLeft);
                GUI.color = level > 0 ? ForgeGold : new Color(1f, 1f, 1f, 0.45f);
                GUI.Label(new Rect(card.x + 10f, card.y + 44f, 200f, 18f), level > 0 ? $"현재 {MetaProgress.Current(u)} ({level}/{u.maxLevel})" : "아직 강화하지 않음", smallStyleLeft);
                GUI.color = Color.white;
                Rect button = new Rect(card.xMax - 104f, card.y + 20f, 94f, 34f);
                GUI.enabled = affordable;
                if (UiSound.Button(button, maxed ? "최대" : $"강화 · {MetaProgress.CostOf(u)}", buttonStyle) && MetaProgress.Buy(u))
                {
                    Toast($"{u.name} {MetaProgress.LevelOf(u.id)}단계 — {MetaProgress.Current(u)}", ForgeGold);
                    player.RefreshMaxHealth();
                }
                GUI.enabled = true;
            }
        }

        // 초기화: everything back, tokens returned (asked twice)
        Rect reset = new Rect(panel.x + 24f, panel.yMax - 46f, 250f, 32f);
        GUI.color = confirmMetaReset ? new Color(1f, 0.5f, 0.4f) : Color.white;
        if (UiSound.Button(reset, confirmMetaReset ? "한 번 더 누르면 초기화" : "초기화 (토큰 환급)", buttonStyle))
        {
            if (!confirmMetaReset) confirmMetaReset = true;
            else
            {
                confirmMetaReset = false;
                int refund = MetaProgress.ResetAll(switcher);
                player.RefreshMaxHealth();
                // (a weapon now locked can't stay in hand)
                if (switcher != null && switcher.Current != null)
                {
                    int cur = -1;
                    for (int i = 0; i < switcher.Characters.Count; i++) if (switcher.Characters[i] == switcher.Current) cur = i;
                    if (cur > 0 && !MetaProgress.IsUnlocked(switcher.Current, cur)) switcher.Equip(0);
                }
                Toast($"모든 강화를 초기화했습니다 — 토큰 {refund}개 환급", ForgeGold);
            }
        }
        GUI.color = Color.white;
        GUI.Label(new Rect(panel.x + 284f, panel.yMax - 44f, panel.width - 300f, 30f), "토큰은 여정이 끝날 때 도달한 레벨만큼 얻고, 죽어도 남습니다  ·  [F]/[Esc] 닫기", smallStyleLeft);
    }

    // 무기 해금: the first fighter's weapon is always open; the rest for tokens
    private void DrawWeaponUnlocks(float left, float top, float cardW, float cardH, float gap)
    {
        if (switcher == null) return;
        MetaProgress.UnlockAllIfPending(switcher);
        var fighters = switcher.Characters;
        for (int i = 0; i < fighters.Count; i++)
        {
            CharacterProfile f = fighters[i];
            Rect card = new Rect(left + (i % 2) * (cardW + gap), top + (i / 2) * (cardH + 8f), cardW, cardH);
            bool open = MetaProgress.IsUnlocked(f, i);
            UiFont.Fill(card, open ? ForgeGold : new Color(0.32f, 0.29f, 0.26f));
            UiFont.Fill(new Rect(card.x + 2f, card.y + 2f, card.width - 4f, card.height - 4f), open ? new Color(0.24f, 0.18f, 0.12f) : new Color(0.17f, 0.15f, 0.13f));
            Texture2D icon = ArmoryIcon(f.Weapon);
            if (icon != null) GUI.DrawTexture(new Rect(card.x + 8f, card.y + 8f, 60f, 60f), icon);
            GUI.Label(new Rect(card.x + 76f, card.y + 8f, 150f, 22f), f.WeaponName, textStyleBold);
            GUI.Label(new Rect(card.x + 76f, card.y + 32f, 150f, 20f), f.DisplayName, smallStyleLeft);
            Rect button = new Rect(card.xMax - 104f, card.y + 20f, 94f, 34f);
            if (open) GUI.Label(button, i == 0 ? "기본 무기" : "해금됨", centerStyleBold);
            else
            {
                GUI.enabled = MetaProgress.Tokens >= MetaProgress.WeaponUnlockCost;
                if (UiSound.Button(button, $"해금 · {MetaProgress.WeaponUnlockCost}", buttonStyle) && MetaProgress.UnlockWeapon(f))
                    Toast($"무기 해금: {f.WeaponName}", ForgeGold);
                GUI.enabled = true;
            }
        }
    }
    private void DrawPause()
    {
        Rect panel = CenteredPanel(360f, 250f);
        GUI.Label(new Rect(panel.x, panel.y + 18f, panel.width, 34f), "일시정지", titleStyle);
        if (UiSound.Button(new Rect(panel.center.x - 110f, panel.y + 74f, 220f, 44f), "계속하기", buttonStyle))
            Resume();

        // Giving up ends the journey like a death (tokens included); in the village there's nothing to give up
        string label = confirmGiveUp ? "정말 포기할까요?" : "이번 여정 포기";
        GUI.color = confirmGiveUp ? new Color(1f, 0.55f, 0.5f) : Color.white;
        if (!inHub && UiSound.Button(new Rect(panel.center.x - 110f, panel.y + 130f, 220f, 44f), label, buttonStyle))
        {
            if (confirmGiveUp) { state = State.Playing; EndRun(State.Dead); }
            else confirmGiveUp = true;
        }
        GUI.color = Color.white;
        GUI.Label(new Rect(panel.x, panel.yMax - 34f, panel.width, 20f), "[Esc] 계속하기", smallCenterStyle);
    }

    // F1: adjust the player's stats and jump around the run for testing
    private void DrawTestPanel()
    {
        Rect box = CenteredPanel(760f, Mathf.Min(560f, Screen.height - 12f));
        GUI.Label(new Rect(box.x, box.y + 10f, box.width, 34f), "테스트 모드", titleStyle);

        float y = box.y + 52f;
        float sx = box.center.x - 280f;   // the sliders, centred
        float Slider(string label, float value, float min, float max, string shown)
        {
            GUI.Label(new Rect(sx + 30f, y, 170f, 24f), label, textStyle);
            float result = GUI.HorizontalSlider(new Rect(sx + 200f, y + 7f, 240f, 16f), value, min, max);
            GUI.Label(new Rect(sx + 452f, y, 90f, 24f), shown, textStyleBold);
            y += 30f;
            return result;
        }

        float oldMax = DebugCheats.MaxHealthBonus;
        DebugCheats.MaxHealthBonus = Mathf.Round(Slider("최대 체력 보너스", DebugCheats.MaxHealthBonus, -50f, 500f, $"{DebugCheats.MaxHealthBonus:+0;-0;0}") / 10f) * 10f;
        DebugCheats.DamageBonus = Mathf.RoundToInt(Slider("공격력 보너스", DebugCheats.DamageBonus, 0f, 50f, $"+{DebugCheats.DamageBonus}  ({player.Damage})"));
        DebugCheats.AttackSpeedMultiplier = Mathf.Round(Slider("공격 속도", DebugCheats.AttackSpeedMultiplier, 0.5f, 3f, $"x{DebugCheats.AttackSpeedMultiplier:0.0}") * 10f) / 10f;
        DebugCheats.MoveSpeedMultiplier = Mathf.Round(Slider("이동 속도", DebugCheats.MoveSpeedMultiplier, 0.5f, 3f, $"x{DebugCheats.MoveSpeedMultiplier:0.0}") * 10f) / 10f;
        DebugCheats.DamageTakenMultiplier = Mathf.Round(Slider("받는 피해", DebugCheats.DamageTakenMultiplier, 0f, 2f, $"x{DebugCheats.DamageTakenMultiplier:0.0}") * 10f) / 10f;
        if (DebugCheats.MaxHealthBonus != oldMax)
            player.RefreshMaxHealth();

        GUI.color = DebugCheats.Invincible ? new Color(1f, 0.75f, 0.35f) : Color.white;
        if (UiSound.Button(new Rect(box.x + 30f, y - 4f, 150f, 30f), DebugCheats.Invincible ? "무적 켜짐" : "무적 꺼짐", buttonStyle))
            DebugCheats.Invincible = !DebugCheats.Invincible;
        // Whether runs (and the village) start with both skill trees fully learned
        GUI.color = DebugCheats.AllSkillsOnStart ? new Color(1f, 0.75f, 0.35f) : Color.white;
        if (UiSound.Button(new Rect(box.x + 190f, y - 4f, 270f, 30f), DebugCheats.AllSkillsOnStart ? "시작 시 스킬 전부 습득: 켜짐" : "시작 시 스킬 전부 습득: 꺼짐", buttonStyle))
        {
            DebugCheats.AllSkillsOnStart = !DebugCheats.AllSkillsOnStart;
            if (DebugCheats.AllSkillsOnStart)
                LearnAllSkillsForTesting();
        }
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x + 480f, y, 260f, 24f), $"HP {Mathf.CeilToInt(player.Health)} / {Mathf.CeilToInt(player.MaxHealth)}", textStyle);
        y += 38f;

        // Three columns of one-shot actions
        float bw = 232f;
        int slot = 0;
        bool Button(string label)
        {
            int col = slot % 3;
            Rect r = new Rect(box.center.x - bw * 1.5f - 8f + col * (bw + 8f), y + slot / 3 * 42f, bw, 34f);
            slot++;
            if (!UiSound.Button(r, label, buttonStyle))
                return false;
            DebugCheats.UsedThisRun = true;
            return true;
        }

        // The weapon in hand's skill tree: fill it, empty it (points handed back), more points
        SkillTree tree = CurrentTree;
        // (the skill tree is gone, and its test buttons with it)
        void SkillButtons() { }

        if (inHub)
        {
            // In the village there's no run to bend: tokens for the forge, or straight into a journey
            SkillButtons();
            if (Button("토큰 +10")) MetaProgress.AddTokens(10);
            if (Button("바로 여정 시작"))
            {
                Time.timeScale = 1f;
                StartRun();
            }
            if (Button("오프닝 보기"))
            {
                Resume();
                PlayOpening();
            }
            if (Button(OpeningCutscene.AutoPlay ? "시작 시 오프닝: 켜짐" : "시작 시 오프닝: 꺼짐"))
                OpeningCutscene.AutoPlay = !OpeningCutscene.AutoPlay;
            if (Button("능력치 초기화"))
            {
                DebugCheats.Reset();
                player.RefreshMaxHealth();
            }
            FloorButtons(box, y + (slot + 2) / 3 * 42f + 4f);
            GUI.Label(new Rect(box.x, box.yMax - 22f, box.width, 20f), "[F1] 또는 [Esc] 닫기", smallCenterStyle);
            return;
        }
        SkillButtons();
        if (Button("체력 전부 회복")) player.Heal(99999f);
        if (Button("황금 +100")) RunState.AddGold(100);
        if (Button("레벨 업")) RunState.AddXp(RunState.XpToNext - RunState.Xp);
        if (Button("무작위 룬 획득"))
        {
            Rune rune = RuneLibrary.RandomUnowned(RunState.Runes);
            if (rune != null) { RunState.AddRune(rune); Toast($"룬 획득: {rune.name}", RuneLibrary.GodColors[(int)rune.god]); }
            else Toast("더 얻을 룬이 없습니다", Color.white);
        }
        if (Button("구역의 적 모두 처치"))
        {
            // this game's zone, or a realm scene's room being fought in (or, failing that, everything close by)
            StageHost.Zone? zone = StageHost.Hosted ? StageHost.Fight?.Invoke() : null;
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy.IsDead) continue;
                Vector3 p = enemy.transform.position;
                bool inZone = !StageHost.Hosted ? enemy.HomeRoom == currentRoom
                    : zone.HasValue ? p.x >= zone.Value.area.min.x - 1f && p.x <= zone.Value.area.max.x + 1f && p.z >= zone.Value.area.min.z - 1f && p.z <= zone.Value.area.max.z + 1f
                    : Vector3.Distance(p, player.transform.position) < 40f;
                if (inZone) enemy.TakeHit(p, 99999);
            }
            // a scene realm's boss doesn't take plain hits (Surtr only through his core, his warrior form changes phase
            // instead of falling): the realm fells it itself
            StageHost.TestKillBoss?.Invoke();
        }
        // 훈련장: every skill against training dummies (damage numbers, DPS)
        if (Button(TrainingRoom.Active ? "훈련장 나가기" : "훈련장 (스킬·데미지 테스트)"))
        {
            Resume();
            if (TrainingRoom.Active) TrainingRoom.Exit();
            else if (CanTrain) TrainingRoom.Enter();
            else Toast("전투 중에는 훈련장에 갈 수 없습니다", new Color(1f, 0.6f, 0.5f));
        }
        if (Button("수문장 구역으로 이동")) TravelToZone(Room.Kind.Boss);
        if (Button("중간보스 구역으로 이동")) TravelToZone(Room.Kind.MidBoss);
        if (Button(realmIndex < realms.Length - 1 ? "다음 세계로" : "마지막 세계입니다") && realmIndex < realms.Length - 1)
        {
            Time.timeScale = 1f;
            EnterRealm(realmIndex + 1);
        }
        if (Button("능력치 초기화"))
        {
            DebugCheats.Reset();
            player.RefreshMaxHealth();
        }
        FloorButtons(box, y + (slot + 2) / 3 * 42f + 4f);

        GUI.Label(new Rect(box.x, box.yMax - 28f, box.width, 20f), "[F1] 또는 [Esc] 닫기 · 테스트 기능을 쓴 여정은 기록에 남지 않습니다", smallCenterStyle);
    }

    // Skip straight to the guardian's (or the mid-boss's) zone, arriving through the exit from a neighbouring zone
    private void TravelToZone(Room.Kind kind)
    {
        // A realm played as its own scene: its own jump to the guardian / mid-boss (the same buttons on every floor)
        if (StageHost.Hosted)
        {
            System.Action jump = kind ==Room.Kind.Boss ? StageHost.TestJumpToBoss : StageHost.TestJumpToMidBoss;
            Resume();
            Time.timeScale = 1f;
            if (jump != null) jump();
            else Toast(kind == Room.Kind.Boss ? "이 층에서는 수문장으로 바로 갈 수 없습니다" : "이 층에는 중간보스가 없습니다", Color.white);
            return;
        }
        if (travelling)
            return;
        Room boss = dungeon.Rooms.FirstOrDefault(r => kind == Room.Kind.MidBoss ? r.IsMidBossFight : r.RoomKind == kind);
        // Asgard is built one area of its route at a time: the mid-boss and the guardian only exist in the route's last
        // area. Skip the route on to that area, build it, then go on into the hall asked for.
        if (boss == null && Routed(CurrentRealm))
        {
            Resume();
            Time.timeScale = 1f;
            StartCoroutine(TestJumpToFinalArea(kind));
            return;
        }
        string zoneName = kind == Room.Kind.Boss ? "수문장" : "중간보스";
        if (boss == null)
        {
            Toast($"이 층에는 {zoneName} 구역이 없습니다", Color.white);
            return;
        }
        if (boss == currentRoom)
        {
            Toast($"이미 {zoneName} 구역에 있습니다", Color.white);
            return;
        }
        Room neighbour = boss.Neighbours.FirstOrDefault(n => n.Exits.ContainsValue(boss));
        if (neighbour == null)
            return;
        Vector2Int side = neighbour.Exits.First(e => e.Value == boss).Key;
        Resume();
        StartCoroutine(TravelTo(boss, -side));
    }

    // Test panel, Asgard: on along the route to the area asked for (one of the last pick's areas, whose fight hall the
    // mid-boss joins, or the guardian's area after it), then into that hall
    private IEnumerator TestJumpToFinalArea(Room.Kind kind)
    {
        if (kind == Room.Kind.Boss)
        {
            Route.SkipToFinal(Route.ThemePicks);
            yield return EnterRouteArea(RouteTheme.Boss);
        }
        else
        {
            Route.SkipToFinal();
            yield return EnterRouteArea(Route.Layers[Route.ThemePicks][0].theme);
        }
        yield return null;
        if (dungeon.Rooms.Any(r => kind == Room.Kind.MidBoss ? r.IsMidBossFight : r.RoomKind == kind))
            TravelToZone(kind);
    }

    private void DrawRunOver()
    {
        bool won = state == State.Victory;
        Rect panel = CenteredPanel(480f, 500f);
        GUI.color = won ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.5f, 0.45f);
        GUI.Label(new Rect(panel.x, panel.y + 18f, panel.width, 36f), won ? "발할라에 입성했다!" : "쓰러졌다… 발키리가 너를 데려간다", titleStyle);
        GUI.color = Color.white;
        GUI.Label(new Rect(panel.x, panel.y + 58f, panel.width, 22f), won ? "마지막 스테이지, 아스가르드의 수문장을 쓰러뜨렸습니다" : "룬과 황금은 잃었지만, 쌓은 경험은 토큰이 되어 남습니다", centerStyle);

        // The tokens this journey earned for Brokkr's forge
        Rect earned = new Rect(panel.center.x - 190f, panel.y + 90f, 380f, 44f);
        UiFont.Fill(earned, new Color(0.1f, 0.18f, 0.26f));
        UiFont.Fill(new Rect(earned.x, earned.y, earned.width, 2f), TokenColor);
        TokenIcon(new Vector2(earned.x + 26f, earned.center.y), 16f);
        GUI.color = TokenColor;
        GUI.Label(new Rect(earned.x + 44f, earned.y + 3f, earned.width - 50f, 22f),
            tokensEarned > 0 ? $"토큰 +{tokensEarned}  (보유 {MetaProgress.Tokens})" : "테스트 기능을 써서 토큰이 없습니다", textStyleBold);
        GUI.color = Color.white;
        GUI.Label(new Rect(earned.x + 44f, earned.y + 23f, earned.width - 50f, 18f),
            won ? $"Lv {RunState.Level} + 승리 보너스 5" : $"도달한 레벨 Lv {RunState.Level}", smallStyleLeft);

        float y = panel.y + 150f;
        void Line(string label, string value)
        {
            GUI.Label(new Rect(panel.x + 70f, y, 140f, 24f), label, textStyle);
            GUI.Label(new Rect(panel.x + 210f, y, 250f, 24f), value, textStyleBold);
            y += 28f;
        }
        Line("도달한 세계", StageLabel(CurrentRealm));
        Line("정리한 방", RunState.RoomsCleared.ToString());
        Line("쓰러뜨린 적", RunState.Kills.ToString());
        Line("남은 황금", RunState.Gold.ToString());
        Line("최고 기록", $"{PlayerPrefs.GetInt(BestRealmKey, 1)}층  (승리 {PlayerPrefs.GetInt(WinsKey, 0)}회)");

        GUI.Label(new Rect(panel.x + 70f, y + 4f, 300f, 20f), $"룬 {RunState.Runes.Count}개", smallStyleLeft);
        float x = panel.x + 70f;
        foreach (Rune rune in RunState.Runes.OrderBy(r => r.god))
        {
            if (x > panel.xMax - 90f) break;
            UiFont.Fill(new Rect(x, y + 28f, 18f, 18f), RuneLibrary.GodColors[(int)rune.god]);
            x += 22f;
        }

        if (UiSound.Button(new Rect(panel.center.x - 100f, panel.yMax - 62f, 200f, 44f), hub != null ? "마을로 돌아가기" : "새 여정 시작", buttonStyle))
        {
            if (hub != null) EnterHub();
            else StartRun();
        }
    }

    private Rect CenteredPanel(float width, float height)
    {
        UiFont.Fill(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
        width = Mathf.Min(width, Screen.width - 20f);
        Rect panel = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        UiFont.Fill(panel, new Color(0.11f, 0.1f, 0.1f, 0.97f));
        return panel;
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;
        titleStyle = UiFont.Style(26, TextAnchor.MiddleCenter, FontStyle.Bold);
        textStyle = UiFont.Style(16);
        textStyleBold = UiFont.Style(16, TextAnchor.MiddleLeft, FontStyle.Bold);
        smallStyle = UiFont.Style(12, TextAnchor.MiddleLeft, FontStyle.Bold);
        smallStyleLeft = UiFont.Style(12);
        centerStyle = UiFont.Style(14, TextAnchor.MiddleCenter);
        centerStyleBold = UiFont.Style(14, TextAnchor.MiddleCenter, FontStyle.Bold);
        smallCenterStyle = UiFont.Style(12, TextAnchor.MiddleCenter);
        skillTextStyle = UiFont.Style(11, TextAnchor.UpperCenter);
        skillTextStyle.wordWrap = true;
        floaterStyle = UiFont.Style(20, TextAnchor.MiddleCenter, FontStyle.Bold);
        mapStyle = UiFont.Style(10, TextAnchor.MiddleCenter, FontStyle.Bold);
        buttonStyle = new GUIStyle(GUI.skin.button) { font = UiFont.Get(), fontSize = 16, fontStyle = FontStyle.Bold };
    }
}
