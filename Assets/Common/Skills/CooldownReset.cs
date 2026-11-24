using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// 훈련장: every skill ready again at once, whatever weapon is in hand. Each weapon keeps its cooldowns its own way (the
// greatsword's whirlwindReadyAt…, the twin blades' readyAt table, the axes' Skill.readyAt, the spear's lungeReadyAt…), so
// this walks the player's components and puts back everything that follows the "…ReadyAt" naming the weapons share:
// a float time is set to 0, a table of them is emptied, and objects a weapon holds (its skill entries) are looked into too.
// The twin blades' 그림자 질주 charges are filled up again, the supports' own cooldowns cleared and 기력 refilled.
public static class CooldownReset
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private const int MaxDepth = 2;

    // Per type, the fields worth touching (found once)
    private class Plan
    {
        public readonly List<FieldInfo> times = new List<FieldInfo>();     // float …ReadyAt
        public readonly List<FieldInfo> tables = new List<FieldInfo>();    // IDictionary …readyAt
        public readonly List<FieldInfo> nested = new List<FieldInfo>();    // plain objects / lists that may hold more
        public FieldInfo charges, recharge;                                // the twin blades' dash charges
        public int chargesMax;
    }
    private static readonly Dictionary<Type, Plan> plans = new Dictionary<Type, Plan>();

    private static bool IsCooldown(string name)
    {
        string l = name.ToLowerInvariant();
        return l.EndsWith("readyat") || l == "nextdashtime";
    }

    private static Plan PlanFor(Type type)
    {
        if (plans.TryGetValue(type, out Plan plan)) return plan;
        plan = new Plan();
        for (Type t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(object); t = t.BaseType)
            foreach (FieldInfo f in t.GetFields(Fields))
            {
                Type ft = f.FieldType;
                if (ft == typeof(float) && !f.IsInitOnly && IsCooldown(f.Name)) plan.times.Add(f);
                else if (typeof(IDictionary).IsAssignableFrom(ft) && IsCooldown(f.Name)) plan.tables.Add(f);
                else if (ft == typeof(int) && f.Name == "dashCharges" && !f.IsInitOnly)
                {
                    plan.charges = f;
                    FieldInfo max = t.GetField("DashCharges", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    plan.chargesMax = max != null && max.IsLiteral ? (int)max.GetRawConstantValue() : 3;
                }
                else if (f.Name == "dashRecharge" && typeof(IEnumerable).IsAssignableFrom(ft)) plan.recharge = f;
                else if (ft.IsClass && ft != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(ft)
                         && !typeof(Delegate).IsAssignableFrom(ft) && !typeof(IDictionary).IsAssignableFrom(ft)
                         && (ft.Namespace == null || !ft.Namespace.StartsWith("UnityEngine")))
                    plan.nested.Add(f);
            }
        plans[type] = plan;
        return plan;
    }

    // Returns how many cooldowns it put back
    public static int ResetAll(GameObject player)
    {
        int n = 0;
        if (player != null)
        {
            var seen = new HashSet<object>();
            foreach (MonoBehaviour mb in player.GetComponents<MonoBehaviour>())
                if (mb != null) n += Reset(mb, 0, seen);
        }
        SkillSystem.ReadyAllForTraining();
        return n;
    }

    private static int Reset(object o, int depth, HashSet<object> seen)
    {
        if (o == null || depth > MaxDepth || !seen.Add(o)) return 0;
        Plan plan = PlanFor(o.GetType());
        int n = 0;
        foreach (FieldInfo f in plan.times)
        {
            f.SetValue(o, 0f);
            n++;
        }
        foreach (FieldInfo f in plan.tables)
            if (f.GetValue(o) is IDictionary table && table.Count > 0) { table.Clear(); n++; }
        if (plan.charges != null) plan.charges.SetValue(o, plan.chargesMax);
        if (plan.recharge != null)
        {
            object q = plan.recharge.GetValue(o);
            q?.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(q, null);
        }
        if (depth < MaxDepth)
            foreach (FieldInfo f in plan.nested)
            {
                object v = f.GetValue(o);
                if (v is IEnumerable list)
                {
                    foreach (object item in list)
                        if (item != null && !(item is UnityEngine.Object) && !(item is string) && item.GetType().IsClass)
                            n += Reset(item, depth + 1, seen);
                }
                else if (v != null)
                    n += Reset(v, depth + 1, seen);
            }
        return n;
    }
}
