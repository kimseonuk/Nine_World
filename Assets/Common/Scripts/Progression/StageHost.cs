using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// A realm built as its own scene by its team (Jotunheim's Ironwood dungeon, Muspelheim's fire road) is played as it was
// made — its rooms, monsters and boss arena unchanged — but with this game's player, camera and UI. The scene is loaded
// on top of the main one; everything in it that the main scene already has (their player, camera, audio listener,
// event system) is taken out before it starts, so their scripts find ours instead.
//
// The realm's own scripts talk back through the hooks here: banners go to the HUD, scene changes inside the realm
// (dungeon to boss arena) go through Load, the map draws from Map, and Cleared opens the way to the next realm.
public static class StageHost
{
    // True while a realm scene is being played inside this game (its scripts then leave the HUD, death and restart to us)
    public static bool Hosted { get; private set; }
    public static Scene Current { get; private set; }

    public static Action<string, string> Banner;     // (title, subtitle) shown on our banner
    public static Action Cleared;                    // the realm's guardian has fallen
    public static Action InnerLoaded;                // the realm moved on to another of its scenes (dungeon -> arena)
    public static Func<IReadOnlyList<MapNode>> Map;  // the realm's rooms for our map (null: no map)
    public static Action<int> MapJump;               // a room picked on our map
    public static Action TestJumpToBoss;             // test panel: put the player right in front of the realm's boss
    public static Action TestKillBoss;               // test panel: fell the realm's boss (or its current phase) at once
    public static Action TestJumpToMidBoss;          // test panel: put the player at the realm's mid-boss
    public static Func<Enemy> Boss;                  // the boss (or mid-boss) whose bar our HUD shows, if any
    public static Vector3? Spawn { get; private set; }   // where the realm's own player stood (for scenes that don't place one)
    public static string RoomName;                   // the room the player is in, for the HUD
    // The room the player is fighting in (null: not fighting — the map shows all the rooms). Like this game's own halls,
    // the corner map then shows just that room with the monsters still standing in it, until they are all down.
    public static Func<Zone?> Fight;

    public struct Zone
    {
        public Bounds area;   // the room's floor, seen from above (y ignored)
        public string name;
    }

    // A room on the map, as the realm reports it
    public struct MapNode
    {
        public Vector2Int cell;
        public string name;
        public bool visited, cleared, current, boss, midBoss, reachable;
        public int[] links;   // indexes of the nodes it has a door to
    }

    private static MonoBehaviour runner;
    private static Scene main;
    private static string loading;

    public static void Begin(MonoBehaviour host)
    {
        runner = host;
        main = host.gameObject.scene;
        Hosted = true;
    }

    // Swaps the realm scene shown (the first, or the next one inside the realm); done is called once it has started
    public static void Load(string path, Action done = null)
    {
        if (runner == null || loading != null)
            return;
        runner.StartCoroutine(Swap(path, done));
    }

    private static IEnumerator Swap(string path, Action done)
    {
        loading = path;
        Map = null;
        Fight = null;
        MapJump = null;
        TestJumpToBoss = null;
        TestJumpToMidBoss = null;
        TestKillBoss = null;
        Boss = null;
        RoomName = null;
        Spawn = null;
        if (Current.IsValid() && Current.isLoaded)
        {
            SceneManager.SetActiveScene(main);
            yield return SceneManager.UnloadSceneAsync(Current);
        }
        SceneManager.sceneLoaded += Sanitize;
        AsyncOperation op;
#if UNITY_EDITOR
        op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
#else
        op = SceneManager.LoadSceneAsync(System.IO.Path.GetFileNameWithoutExtension(path), LoadSceneMode.Additive);
#endif
        yield return op;
        SceneManager.sceneLoaded -= Sanitize;
        Current = SceneManager.GetSceneByPath(path);
        // Its fog, ambient light and sky are the realm's look
        if (Current.IsValid())
            SceneManager.SetActiveScene(Current);
        loading = null;
        done?.Invoke();
    }

    // Before the realm's scripts start: out with what the main scene already provides
    private static void Sanitize(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != loading)
            return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<PlayerStats>(true) != null || root.GetComponentInChildren<PlayerMovement>(true) != null
                || root.CompareTag("Player"))
            {
                Spawn = root.transform.position;
                UnityEngine.Object.DestroyImmediate(root);
                continue;
            }
            foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
                if (cam != null) UnityEngine.Object.DestroyImmediate(cam.gameObject);
            if (root == null)
                continue;
            foreach (AudioListener ear in root.GetComponentsInChildren<AudioListener>(true))
                if (ear != null) UnityEngine.Object.DestroyImmediate(ear);
            foreach (var es in root.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true))
                if (es != null) UnityEngine.Object.DestroyImmediate(es.gameObject);
        }
    }

    // Leaving the realm (on to the next, back to the village, or the run over)
    public static void End()
    {
        Hosted = false;
        Boss = null;
        RoomName = null;
        Map = null;
        Fight = null;
        MapJump = null;
        TestJumpToBoss = null;
        TestJumpToMidBoss = null;
        TestKillBoss = null;
        Banner = null;
        Cleared = null;
        if (Current.IsValid() && Current.isLoaded)
        {
            SceneManager.SetActiveScene(main);
            SceneManager.UnloadSceneAsync(Current);
        }
        Current = default;
    }
}
