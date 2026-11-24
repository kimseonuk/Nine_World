using System;
using System.Collections.Generic;
using System.Linq;

// A weapon's skill tree for a run. It starts as a single trunk of basics (the combo's second blow, the dash, the first
// stat training) and then splits into three branches — 공격 (attack), 생존 (survival) and 이속 (speed) — that fork
// again and again as they climb into a crown. The weapon's combo and skills are learned here, and in between many
// small stat nodes can be taken more than once. Rows above the trunk only open once enough points have gone into the
// tree, so it fills in a step at a time. Points come from levelling up; like runes, everything learned is lost on death.
// Each weapon has one (DualBladeSkills.Tree, GreatswordSkills.Tree); a tree's stats only count while its weapon is in hand.
public class SkillTree
{
    public enum Branch { Trunk, Attack, Survival, Speed }
    public enum Kind { Combo, Skill, Upgrade, Stat }

    public class Node
    {
        public string id;
        public string name;
        public string effect;         // per rank for a stat node
        public string parent;         // needs at least one rank of it (null for the trunk)
        public string also;           // another node it needs too, from elsewhere in the tree (not drawn as a bough)
        public Branch branch;
        public Kind kind;
        public int row;               // 0 = the foot of the trunk, climbing up
        public float x;               // across the panel: -1 left edge, 0 the trunk, 1 right edge
        public int maxRank = 1;
        public string icon;           // Resources/UI texture for a skill node
        public string glyph;          // the short label drawn in a node without an icon
        public Action<RunStats, int> stats;   // what its ranks add (stat nodes)
    }

    // A node, briefly: a stat node when it has stats, otherwise a skill with an icon or an upgrade/combo with a glyph
    public static Node Stat(string id, string name, string effect, string parent, Branch branch, int row, float x, string glyph, int maxRank,
        Action<RunStats, int> stats) =>
        new Node { id = id, name = name, effect = effect, parent = parent, branch = branch, kind = Kind.Stat, row = row, x = x, glyph = glyph, maxRank = maxRank, stats = stats };

    public static Node Skill(string id, string name, string effect, string parent, Branch branch, int row, float x, string icon) =>
        new Node { id = id, name = name, effect = effect, parent = parent, branch = branch, kind = Kind.Skill, row = row, x = x, icon = icon };

    public static Node Upgrade(string id, string name, string effect, string parent, Branch branch, int row, float x, string glyph, string also = null) =>
        new Node { id = id, name = name, effect = effect, parent = parent, also = also, branch = branch, kind = Kind.Upgrade, row = row, x = x, glyph = glyph };

    public static Node ComboBlow(string id, string name, string effect, string parent, Branch branch, int row, float x, string glyph) =>
        new Node { id = id, name = name, effect = effect, parent = parent, branch = branch, kind = Kind.Combo, row = row, x = x, glyph = glyph };

    public const int Rows = 10;        // the top row
    public const int TrunkRows = 4;    // rows 0-3 are the trunk; the branches start at row 4

    // Points that must already be in the tree before a row opens (the trunk and the branches' first row need none)
    public static int PointsToOpen(int row) => row <= TrunkRows ? 0 : 2 * (row - 3);

    public readonly string title;
    public readonly Node[] Nodes;
    // The names written beside the Attack, Survival and Speed branches (a weapon may call its branches its own way)
    public string[] BranchLabels { get; set; } = { "공격", "생존", "이속" };
    // A smaller tree can stop lower (so the panel draws it larger) and open its rows on its own scale
    public int TopRow { get; set; } = Rows;
    public float CanvasWidth { get; set; } = 2000f;   // the K panel's canvas width for this tree
    public float NodeScale { get; set; } = 1f;        // how large its nodes are drawn
    public Func<int, int> OpenRule { get; set; }
    public int PointsToOpenRow(int row) => OpenRule != null ? OpenRule(row) : PointsToOpen(row);
    private readonly string[] comboNodes;   // the combo's 2nd, 3rd... blows, in order
    private readonly Dictionary<string, int> ranks = new Dictionary<string, int>();
    private bool active;

    public SkillTree(string title, Node[] nodes, params string[] comboNodes)
    {
        this.title = title;
        Nodes = nodes;
        this.comboNodes = comboNodes;
    }

    public int Points { get; private set; }
    public int Spent { get; private set; }   // ranks in the tree (what opens its rows)
    private int paid;                        // points actually paid for them

    public void AddPoints(int amount)
    {
        Points += amount;
        Changed?.Invoke();
    }
    public event Action Changed;

    // Whether this tree's weapon is the one in hand: only then do its stats count
    public bool Active
    {
        get => active;
        set
        {
            if (active == value)
                return;
            active = value;
            RunState.Refresh();
        }
    }

    public int Rank(string id) => ranks.TryGetValue(id, out int r) ? r : 0;
    public bool Has(string id) => Rank(id) > 0;
    public Node Find(string id) => Nodes.First(n => n.id == id);
    public bool IsMaxed(Node node) => Rank(node.id) >= node.maxRank;
    public bool ParentReady(Node node) => (node.parent == null || Has(node.parent)) && (node.also == null || Has(node.also));
    public bool RowOpen(Node node) => Spent >= PointsToOpenRow(node.row);
    public bool IsUnlocked(Node node) => ParentReady(node) && RowOpen(node);
    public bool CanLearn(Node node) => Points > 0 && !IsMaxed(node) && IsUnlocked(node);

    // How many blows the combo has: 1, plus one for each combo node learned in order
    public int ComboLength
    {
        get
        {
            int length = 1;
            foreach (string id in comboNodes)
            {
                if (!Has(id))
                    break;
                length++;
            }
            return length;
        }
    }
    public int MaxCombo => comboNodes.Length + 1;

    public void Reset(int startingPoints)
    {
        ranks.Clear();
        Points = startingPoints;
        Spent = paid = 0;
        RunState.Refresh();
        Changed?.Invoke();
    }

    public void AddPoint()
    {
        Points++;
        Changed?.Invoke();
    }

    public bool Learn(Node node)
    {
        if (!CanLearn(node))
            return false;
        ranks[node.id] = Rank(node.id) + 1;
        Points--;
        Spent++;
        paid++;
        GameAudio.Play("skill_learn");
        if (node.stats != null)
            RunState.Refresh();
        Changed?.Invoke();
        return true;
    }

    // Test mode: every node taken to its last rank at once, free (the points already held are kept)
    public void LearnAll()
    {
        foreach (Node node in Nodes)
        {
            Spent += node.maxRank - Rank(node.id);
            ranks[node.id] = node.maxRank;
        }
        RunState.Refresh();
        Changed?.Invoke();
    }

    // Everything unlearned and every point actually paid for it handed back (test mode's free ranks give none)
    public void Refund()
    {
        ranks.Clear();
        GameAudio.Play("skill_refund");
        Points += paid;
        paid = 0;
        Spent = 0;
        RunState.Refresh();
        Changed?.Invoke();
    }

    // The stat nodes' share of the run's stats (RunState adds it while the weapon is in hand)
    public void ApplyStats(RunStats stats)
    {
        if (!active)
            return;
        foreach (Node node in Nodes)
            if (node.stats != null && Rank(node.id) > 0)
                node.stats(stats, Rank(node.id));
    }
}
