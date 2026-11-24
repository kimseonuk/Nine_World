#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// Editor-only balance probe: stands the twin-blade fighter at a training dummy and fights it for a set time —
// the combo on every frame, and (if asked) the skills in a fixed order as soon as they are ready — then reports the
// damage dealt per second. Driven from the editor while playing (balance measurements); never in a build.
public class DpsProbe : MonoBehaviour
{
    public static string Result = "";
    public static bool Running { get; private set; }

    private TrainingDummy target;
    private float seconds, started;
    private DualBlades.Skill[] rotation;
    private DualBlades blades;
    private CharacterController controller;
    private long dealt;
    private int lastHealth;
    private readonly Dictionary<DualBlades.Skill, int> uses = new Dictionary<DualBlades.Skill, int>();

    public static void Run(TrainingDummy target, float seconds, params DualBlades.Skill[] rotation)
    {
        var probe = new GameObject("DpsProbe").AddComponent<DpsProbe>();
        probe.target = target;
        probe.seconds = seconds;
        probe.rotation = rotation;
        Result = "running";
        Running = true;
    }

    private void Start()
    {
        blades = FindAnyObjectByType<DualBlades>();
        controller = blades != null ? blades.GetComponent<CharacterController>() : null;
        lastHealth = target != null ? target.Health : 0;
        started = Time.time;
    }

    private void Update()
    {
        if (target == null || blades == null || !blades.IsActive)
        {
            Finish("stopped: no dummy or the twin blades aren't the active weapon");
            return;
        }
        // damage since last frame (the dummy fills up again when emptied)
        int hp = target.Health;
        dealt += hp <= lastHealth ? lastHealth - hp : lastHealth + (target.MaxHealth - hp);
        lastHealth = hp;

        // stay in reach (a dash may carry the fighter through and past it)
        Vector3 at = target.transform.position;
        Vector3 to = at - blades.transform.position;
        to.y = 0f;
        if (to.magnitude > 2.2f && !blades.EditorTestBusy)
        {
            Vector3 stand = at - (to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward) * 1.5f;
            stand.y = blades.transform.position.y;
            if (controller != null) controller.enabled = false;
            blades.transform.position = stand;
            if (controller != null) controller.enabled = true;
        }
        SkillCast.AimOverride = at;

        // a skill that is learned, off cooldown and affordable (기력) goes next: the combo is let finish its blow first
        bool skillNext = false;
        if (rotation != null)
            foreach (var s in rotation)
                if (blades.EditorTestLearned(s) && blades.Ready(s) && SkillSystem.Stamina >= SkillSystem.CostOf(MainOf(s)))
                {
                    skillNext = true;
                    if (!blades.EditorTestBusy)
                    {
                        blades.EditorTestUse(s);
                        if (!blades.Ready(s))
                            uses[s] = uses.TryGetValue(s, out int n) ? n + 1 : 1;
                    }
                    break;
                }
        if (!skillNext)
            blades.Tap(at);

        if (Time.time - started >= seconds)
        {
            var parts = new List<string>();
            foreach (var kv in uses) parts.Add(kv.Key + "×" + kv.Value);
            Finish($"{dealt} damage in {seconds:0}s = {dealt / seconds:0.0} DPS  (skills: {string.Join(", ", parts)})");
        }
    }

    private static int MainOf(DualBlades.Skill s) => s == DualBlades.Skill.Flurry ? 0 : s == DualBlades.Skill.Dash ? 1 : s == DualBlades.Skill.Berserk ? 2 : 3;

    private void Finish(string text)
    {
        Result = text;
        Running = false;
        SkillCast.AimOverride = null;
        Destroy(gameObject);
    }
}
#endif
