using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Asgard's route (갈림길): the realm is walked as a chain of areas. Each area is a few ordinary fight halls and one
// themed hall (강화 / 상점 / 보조마법 / 회복 / 황금), ending in a waystone hall where [C] opens the route map and the
// next area's theme is picked from three. The fifth pick's areas are themed like the rest, but one of their fight
// halls also holds the mid-boss (fought among the usual enemies); after it the route goes on to the guardian.
public enum RouteTheme { Start, Forge, Shop, Supports, Heal, Vault, MidBoss, Boss, Vigor, Ascend, Fate, Essence }

public static class Route
{
    public const int ThemePicks = 5;
    public static readonly RouteTheme[] Pickable = { RouteTheme.Forge, RouteTheme.Shop, RouteTheme.Supports, RouteTheme.Heal, RouteTheme.Vault,
        RouteTheme.Vigor, RouteTheme.Ascend, RouteTheme.Fate, RouteTheme.Essence };

    // One spot on the route map: its layer (0 the start, 1..5 the picks, then the mid-boss and the guardian), what
    // waits there, where it floats on the map (x in layers, y across), and whether the player has been
    public class Node
    {
        public int layer, index;
        public RouteTheme theme;
        public Vector2 pos;
        public bool visited;
        public bool midBoss;   // one of this area's fight halls also holds the mid-boss (shown under its mark)
    }

    // The whole route, laid out when the realm begins (so the map shows the ways ahead)
    public static readonly List<List<Node>> Layers = new List<List<Node>>();
    public static int LastLayer => Layers.Count - 1;

    public static int Step { get; private set; }                 // the layer the player is in (0: the starting area)
    public static RouteTheme Current { get; private set; } = RouteTheme.Start;
    public static readonly List<RouteTheme> Taken = new List<RouteTheme>();
    public static bool NextIsMidBoss => Step == ThemePicks - 1;   // the next pick's areas hold the mid-boss
    public static bool NextIsBoss => Step >= ThemePicks;          // the mid-boss is behind: on to the guardian
    public static bool Finished => Current == RouteTheme.Boss;

    // The spot the player stands on now
    public static Node CurrentNode => Step < Layers.Count ? Layers[Step].FirstOrDefault(n => n.visited) : null;

    // What can be picked at the waystone now: the next layer (the guardian's, last of all)
    public static RouteTheme[] Offers => Step + 1 <= LastLayer ? Layers[Step + 1].Select(n => n.theme).ToArray() : new RouteTheme[0];

    public static void Reset()
    {
        Step = 0;
        Current = RouteTheme.Start;
        Taken.Clear();
        Layers.Clear();
        Layers.Add(new List<Node> { new Node { layer = 0, theme = RouteTheme.Start, pos = new Vector2(0f, 0f), visited = true } });
        RouteTheme[] previous = null;
        for (int l = 1; l <= ThemePicks; l++)
        {
            // three different themes, never quite the same three as the layer before
            RouteTheme[] picked;
            int guard = 20;
            do picked = Pickable.OrderBy(_ => Random.value).Take(3).ToArray();
            while (previous != null && picked.All(previous.Contains) && guard-- > 0);
            previous = picked;
            var layer = new List<Node>();
            for (int i = 0; i < 3; i++)
                layer.Add(new Node
                {
                    layer = l, index = i, theme = picked[i],
                    // on a neat grid: rows one layer apart, columns one step apart (the map makes the two the same size)
                    pos = new Vector2(l, i - 1),
                    // the last pick: every way holds the mid-boss in one of its fight halls
                    midBoss = l == ThemePicks,
                });
            Layers.Add(layer);
        }
        Layers.Add(new List<Node> { new Node { layer = ThemePicks + 1, theme = RouteTheme.Boss, pos = new Vector2(ThemePicks + 1, 0f) } });
    }

    public static void Advance(RouteTheme theme)
    {
        if (Step + 1 <= LastLayer)
        {
            Node node = Layers[Step + 1].FirstOrDefault(n => n.theme == theme) ?? Layers[Step + 1][0];
            node.visited = true;
        }
        Step++;
        Current = theme;
        Taken.Add(theme);
    }

    // (test panel) Every pick up to layer upTo not yet made is made — the first way of each layer. By default the
    // next pick is then the mid-boss's layer; with upTo = ThemePicks the next step is the guardian.
    public static void SkipToFinal(int upTo = ThemePicks - 1)
    {
        if (Layers.Count == 0)
            Reset();
        for (int l = Step + 1; l <= upTo && l < Layers.Count; l++)
        {
            Node node = Layers[l][0];
            node.visited = true;
            Taken.Add(node.theme);
            Current = node.theme;
        }
        Step = Mathf.Max(Step, upTo);
    }

    // The halls of an area, in walking order. midBossHall: which hall of the chain also holds the mid-boss (-1: none)
    public static List<Room.Kind> ChainFor(RouteTheme theme) => ChainFor(theme, false, out _);

    public static List<Room.Kind> ChainFor(RouteTheme theme, bool midBoss, out int midBossHall)
    {
        midBossHall = -1;
        var chain = new List<Room.Kind> { Room.Kind.Start };
        if (theme == RouteTheme.Boss)
        {
            // the guardian's area: straight on into its arena
            chain.Add(Room.Kind.Boss);
            return chain;
        }
        // Fight halls on the way through: at least 5 everywhere (the starting area and a mid-boss area 5, the mid-boss
        // joining the last; else 5-7). (side branches of more fight halls are added by DungeonGenerator.GenerateArea)
        int fights = theme == RouteTheme.Start || midBoss ? 5 : Random.Range(5, 8);
        for (int i = 0; i < fights; i++)
            chain.Add(Room.Kind.Combat);
        if (theme != RouteTheme.Start)
        {
            // the themed hall stands anywhere after the first fight
            chain.Insert(Random.Range(2, chain.Count + 1), KindOf(theme));
        }
        if (midBoss)
            midBossHall = chain.LastIndexOf(Room.Kind.Combat);
        chain.Add(Room.Kind.Exit);
        return chain;
    }

    public static Room.Kind KindOf(RouteTheme theme) => theme switch
    {
        RouteTheme.Forge => Room.Kind.Forge,
        RouteTheme.Shop => Room.Kind.Shop,
        RouteTheme.Supports => Room.Kind.Supports,
        RouteTheme.Heal => Room.Kind.Heal,
        RouteTheme.Vault => Room.Kind.Vault,
        RouteTheme.Vigor => Room.Kind.Vigor,
        RouteTheme.Ascend => Room.Kind.Ascend,
        RouteTheme.Fate => Room.Kind.Fate,
        RouteTheme.Essence => Room.Kind.Essence,
        RouteTheme.MidBoss => Room.Kind.MidBoss,
        RouteTheme.Boss => Room.Kind.Boss,
        _ => Room.Kind.Start,
    };

    public static string NameOf(RouteTheme theme) => theme switch
    {
        RouteTheme.Start => "출발점",
        RouteTheme.Forge => "강화방",
        RouteTheme.Shop => "상점",
        RouteTheme.Supports => "보조마법의 제단",
        RouteTheme.Heal => "치유의 샘",
        RouteTheme.Vault => "황금 보물고",
        RouteTheme.Vigor => "생명의 결정",
        RouteTheme.Ascend => "승천의 신전",
        RouteTheme.Fate => "노른의 카드",
        RouteTheme.Essence => "정수의 보고",
        RouteTheme.MidBoss => "중간 보스",
        RouteTheme.Boss => "수문장",
        _ => "",
    };

    public static string TipOf(RouteTheme theme) => theme switch
    {
        RouteTheme.Start => "여정을 시작한 곳입니다.",
        RouteTheme.Forge => "대장장이의 모루에서 보조스킬을 강화할 수 있습니다.",
        RouteTheme.Shop => "상인과 거래할 수 있습니다.",
        RouteTheme.Supports => "룬 제단에서 보조마법 셋 중 하나를 얻습니다.\n황금을 내면 다시 뽑을 수 있습니다.",
        RouteTheme.Heal => $"치유의 샘에서 최대 체력의 {HealSpring.Fraction * 100f:0}%를 회복합니다.",
        RouteTheme.Vault => "보물 상자에서 황금을 잔뜩 얻습니다.",
        RouteTheme.Vigor => $"붉은 생명의 결정이 최대 체력을 {RouteBoon.VigorHealth:0} 올려 줍니다.\n(이번 여정 동안, 늘어난 만큼 회복도 합니다)",
        RouteTheme.Ascend => "황금 신전의 빛이 레벨을 하나 올려 줍니다.\n(레벨 업 보상도 고릅니다)",
        RouteTheme.Fate => $"운명의 카드가 레벨 업 보상 리롤권 {RouteBoon.FateRerolls}장을 줍니다.",
        RouteTheme.Essence => $"고유 능력치 강화 토큰 {RouteBoon.EssenceTokens}개를 얻습니다.\n(마을에서 능력치를 올리는 토큰, 바로 저장됩니다)",
        RouteTheme.MidBoss => "전투 구역 하나에 잡몹과 함께 중간 보스가 나타납니다.",
        RouteTheme.Boss => "아스가르드의 수문장이 지키는 곳입니다.\n쓰러뜨리면 비프로스트가 다음 세계로 열립니다.",
        _ => "",
    };

    public static string MarkOf(RouteTheme theme) => theme switch
    {
        RouteTheme.Forge => "mk_forge",
        RouteTheme.Shop => "mk_shop",
        RouteTheme.Supports => "mk_supports",
        RouteTheme.Heal => "mk_heal",
        RouteTheme.Vault => "mk_vault",
        RouteTheme.Vigor => "mk_vigor",
        RouteTheme.Ascend => "mk_ascend",
        RouteTheme.Fate => "mk_fate",
        RouteTheme.Essence => "mk_essence",
        RouteTheme.MidBoss => "mk_midboss",
        RouteTheme.Boss => "mk_boss",
        _ => "mk_combat",
    };

    public static Color ColorOf(RouteTheme theme) => theme switch
    {
        RouteTheme.Forge => new Color(1f, 0.6f, 0.25f),
        RouteTheme.Shop => new Color(0.45f, 0.95f, 0.55f),
        RouteTheme.Supports => new Color(0.75f, 0.5f, 1f),
        RouteTheme.Heal => new Color(1f, 0.45f, 0.5f),
        RouteTheme.Vault => new Color(1f, 0.82f, 0.3f),
        RouteTheme.Vigor => new Color(1f, 0.35f, 0.45f),
        RouteTheme.Ascend => new Color(1f, 0.9f, 0.5f),
        RouteTheme.Fate => new Color(0.45f, 0.9f, 1f),
        RouteTheme.Essence => new Color(1f, 0.75f, 0.35f),
        RouteTheme.MidBoss => new Color(0.95f, 0.85f, 0.55f),
        RouteTheme.Boss => new Color(1f, 0.35f, 0.3f),
        _ => new Color(0.9f, 0.85f, 0.75f),
    };

    // A route prop (VARCO, Resources/Props): null when it hasn't been made
    public static GameObject Prop(string name) => Resources.Load<GameObject>("Props/" + name);
}

// The waystone at the end of an area: standing in its ring, [C] opens the route map
public class RouteGate : MonoBehaviour
{
    public const float Reach = 3.2f;
    private static readonly List<RouteGate> all = new List<RouteGate>();

    public static RouteGate Create(Transform parent, Vector3 local, float yaw)
    {
        var go = new GameObject("RouteGate");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var gate = go.AddComponent<RouteGate>();
        Color bifrost = new Color(0.65f, 0.85f, 1f);
        Vfx.Decal(go.transform.position + Vector3.up * 0.05f, Reach * 2f, bifrost, "runes", 25f).transform.SetParent(go.transform, true);
        SkillCast.Fx("sk_field", go.transform.position + Vector3.up * 0.07f, Reach * 2.3f, bifrost * 0.55f, 0f, Quaternion.Euler(90f, 0f, 0f), true, 0.4f, 1f, 1f, 12f).transform.SetParent(go.transform, true);
        Vfx.Glow(go.transform, Vector3.up * 1.4f, 4f, bifrost * 0.7f, "glow", 0f);
        var light = new GameObject("GateLight").AddComponent<Light>();
        light.transform.SetParent(go.transform, false);
        light.transform.localPosition = new Vector3(0f, 3f, 0f);
        light.type = LightType.Point;
        light.color = bifrost;
        light.range = 9f;
        light.intensity = 2.2f;
        return gate;
    }

    private void OnEnable() => all.Add(this);
    private void OnDisable() => all.Remove(this);

    public static bool Near(Vector3 p)
    {
        foreach (RouteGate g in all)
        {
            if (g == null) continue;
            Vector3 d = g.transform.position - p;
            d.y = 0f;
            if (d.magnitude < Reach) return true;
        }
        return false;
    }
}

// The healing spring (회복방): walking up to it heals half the health, once
public class HealSpring : MonoBehaviour
{
    public const float Fraction = 0.5f;   // of max health
    public float reach = 2.6f;            // how near the player must come (a wide pool reaches further)
    private bool used;
    private Transform player;
    private GameObject glow;

    public static HealSpring Create(Transform parent, Vector3 local)
    {
        var go = new GameObject("HealSpring");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var s = go.AddComponent<HealSpring>();
        Color rose = new Color(1f, 0.45f, 0.5f);
        Vfx.Decal(go.transform.position + Vector3.up * 0.05f, 4.6f, rose, "runes", 18f).transform.SetParent(go.transform, true);
        s.glow = Vfx.Glow(go.transform, Vector3.up * 1.3f, 3.6f, rose * 0.9f, "glow", 0f);
        return s;
    }

    private void Update()
    {
        if (used) return;
        if (player == null)
        {
            var stats = FindAnyObjectByType<PlayerStats>();
            if (stats == null) return;
            player = stats.transform;
        }
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        if (d.magnitude > reach) return;
        var ps = player.GetComponent<PlayerStats>();
        if (ps == null || ps.IsDead) return;
        used = true;
        ps.Heal(ps.MaxHealth * Fraction);
        GameAudio.Play("boon");
        Color rose = new Color(1f, 0.5f, 0.55f);
        Shockwave.Spawn(transform.position, 3f, rose, 20);
        SkillCast.Fx("sk_pillar", player.position + Vector3.up * 1.6f, 3.4f, rose * 0.9f, 0.9f, null, true, 0.3f, 1f, 1f);
        Pickup.SpawnHeal(transform.position + Vector3.up, 1);   // a little splash of mead
        if (GameManager.Instance != null) GameManager.Instance.RouteToast($"치유의 샘 — 체력을 {Fraction * 100f:0}% 회복했습니다");
        if (glow != null) glow.SetActive(false);
    }
}

// The vault's treasure chest (황금방): walking up to it bursts it open in a fountain of gold
public class VaultChest : MonoBehaviour
{
    public int gold = 200;
    private bool opened;
    private Transform player;

    public static VaultChest Create(Transform parent, Vector3 local, int gold)
    {
        var go = new GameObject("VaultChest");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var c = go.AddComponent<VaultChest>();
        c.gold = gold;
        Color goldC = new Color(1f, 0.8f, 0.3f);
        Vfx.Decal(go.transform.position + Vector3.up * 0.05f, 4.2f, goldC, "runes", 22f).transform.SetParent(go.transform, true);
        Vfx.Glow(go.transform, Vector3.up * 1f, 3.4f, goldC * 0.8f, "glow", 0f);
        return c;
    }

    private void Update()
    {
        if (opened) return;
        if (player == null)
        {
            var stats = FindAnyObjectByType<PlayerStats>();
            if (stats == null) return;
            player = stats.transform;
        }
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        if (d.magnitude > 2.8f) return;
        opened = true;
        GameAudio.Play("chest_open");
        GameAudio.Play("coins_spill", null, 0.8f);
        StartCoroutine(Burst());
    }

    private IEnumerator Burst()
    {
        // the chest jolts, then bursts in gold
        Transform model = transform.childCount > 0 ? transform.GetChild(transform.childCount - 1) : null;
        Vector3 baseScale = model != null ? model.localScale : Vector3.one;
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            if (model != null)
            {
                float k = 1f + Mathf.Sin(t * 60f) * 0.04f;
                model.localScale = new Vector3(baseScale.x * k, baseScale.y / k, baseScale.z * k);
            }
            yield return null;
        }
        if (model != null) model.localScale = baseScale;
        Color goldC = new Color(1f, 0.82f, 0.3f);
        Shockwave.Spawn(transform.position, 3.2f, goldC, 22);
        SkillCast.Fx("sk_pillar", transform.position + Vector3.up * 2f, 4f, goldC * 0.9f, 0.8f, null, true, 0.3f, 1f, 1f);
        int bursts = 8;
        for (int i = 0; i < bursts; i++)
        {
            Pickup.SpawnGold(transform.position + Vector3.up * 1.2f, gold / bursts + (i < gold % bursts ? 1 : 0));
            yield return new WaitForSeconds(0.06f);
        }
        if (GameManager.Instance != null) GameManager.Instance.RouteToast($"보물 상자 — 황금 {gold}");
    }
}

// The new themed halls' gift (생명의 결정 · 승천의 신전 · 노른의 카드 · 정수의 보고): walking up to its centrepiece
// grants it, once
public class RouteBoon : MonoBehaviour
{
    public enum Kind { Vigor, Ascend, Fate, Essence }
    public const float VigorHealth = 20f;   // max health a life crystal adds (and heals)
    public const int FateRerolls = 2;       // level-up reward rerolls the Norns' cards give
    public const int EssenceTokens = 3;     // forge tokens (고유 능력치 강화 토큰) the treasury gives
    public const float Reach = 2.8f;

    public Kind kind;
    public float reach = Reach;   // (a wide centrepiece reaches further)
    private bool used;
    private Transform player;
    private GameObject glow;

    public static Color ColorOf(Kind kind) => kind switch
    {
        Kind.Vigor => new Color(1f, 0.35f, 0.45f),
        Kind.Ascend => new Color(1f, 0.85f, 0.45f),
        Kind.Fate => new Color(0.45f, 0.9f, 1f),
        _ => new Color(1f, 0.75f, 0.35f),
    };

    public static RouteBoon Create(Transform parent, Vector3 local, Kind kind, float decal = 4.6f)
    {
        var go = new GameObject("RouteBoon_" + kind);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        var b = go.AddComponent<RouteBoon>();
        b.kind = kind;
        Color c = ColorOf(kind);
        Vfx.Decal(go.transform.position + Vector3.up * 0.05f, decal, c, "runes", 18f).transform.SetParent(go.transform, true);
        b.glow = Vfx.Glow(go.transform, Vector3.up * 1.4f, 3.6f, c * 0.85f, "glow", 0f);
        return b;
    }

    private void Update()
    {
        if (used) return;
        if (player == null)
        {
            var stats = FindAnyObjectByType<PlayerStats>();
            if (stats == null) return;
            player = stats.transform;
        }
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        if (d.magnitude > reach) return;
        var ps = player.GetComponent<PlayerStats>();
        if (ps == null || ps.IsDead) return;
        used = true;
        string toast;
        switch (kind)
        {
            case Kind.Vigor:
                RunState.AddVigorHealth(VigorHealth);
                ps.Heal(VigorHealth);
                toast = $"생명의 결정 — 최대 체력 +{VigorHealth:0}";
                break;
            case Kind.Ascend:
                RunState.AddXp(Mathf.Max(1, RunState.XpToNext - RunState.Xp));
                toast = "승천의 신전 — 레벨이 올랐습니다 ([G] 보상 고르기)";
                break;
            case Kind.Fate:
                SkillSystem.AddRewardReroll(FateRerolls);
                toast = $"노른의 카드 — 레벨 업 보상 리롤권 +{FateRerolls}";
                break;
            default:
                MetaProgress.AddTokens(EssenceTokens);
                toast = $"정수의 보고 — 고유 능력치 강화 토큰 +{EssenceTokens} (보유 {MetaProgress.Tokens})";
                break;
        }
        Color c = ColorOf(kind);
        GameAudio.Play("boon");
        Shockwave.Spawn(transform.position, 3f, c, 20);
        SkillCast.Fx("sk_pillar", player.position + Vector3.up * 1.6f, 3.4f, c * 0.9f, 0.9f, null, true, 0.3f, 1f, 1f);
        if (GameManager.Instance != null) GameManager.Instance.RouteToast(toast);
        if (glow != null) glow.SetActive(false);
        Used?.Invoke();
    }

    public event System.Action Used;   // (the hall's own show: the cards fly off, the crystal dims...)
}
