using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace NineWorlds.Jotunheim
{
    // A weapon's four skills on right click, Q, E and R: input, cooldowns, the skill bar above the HP bar,
    // and the shared plumbing the skills use (aiming at the cursor, moving the hero, hitting enemies).
    // The shared player code is untouched: movement and attacks are switched off while a skill moves the hero.
    [RequireComponent(typeof(PlayerStats))]
    public abstract class JOT_SkillSet : MonoBehaviour
    {
        protected class Skill
        {
            public string key, name, icon;
            public float cooldown;
            public float readyAt;
            public Func<bool> use;              // true = it went off (the cooldown starts in the skill itself)
            public Func<string> note;           // a short line under the slot (a buff timer, "E again")
            public bool usableWhileActing;
        }

        // Jotunheim's attacks check these: super armour shrugs off pushes and binds, untouchable dodges the hit
        public static bool SuperArmor { get; protected set; }
        public static bool Untouchable { get; protected set; }

        protected static readonly Color HitColor = new Color(1f, 0.85f, 0.5f);

        protected Skill[] skills;
        protected PlayerStats stats;
        protected CharacterController body;
        protected bool acting;              // a skill is moving the hero
        private Behaviour[] controls;

        protected abstract Skill[] Build();

        protected virtual void Awake()
        {
            stats = GetComponent<PlayerStats>();
            body = GetComponent<CharacterController>();
            controls = new Behaviour[] { GetComponent<PlayerMovement>(), FindAnyObjectByType<ClickToDig>() };
            miner = GetComponent<PlayerMiner>();
            skills = Build();
            if (skills.Length > 4)
                JOT_SpaceGuard.AddTo(this);
            spaceGuard = GetComponent<JOT_SpaceGuard>();
        }

        private JOT_SpaceGuard spaceGuard;

        public bool Acting => acting;

        protected virtual void OnDisable()
        {
            SuperArmor = false;
            Untouchable = false;
            IgnoreEnemyBodies(false);
        }

        protected virtual void Update()
        {
            if (stats.IsDead || Time.timeScale <= 0f || !GameManager.IsRunActive || BossCutscene.Playing)
                return;
            // A menu, the map or a bind has the controls
            Behaviour move = controls[0];
            if (!acting && move != null && !move.enabled && !(spaceGuard != null && spaceGuard.Holding))
                return;

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (SkillSystem.RightPressed(mouse)) TryUse(0);
            if (kb == null)
                return;
            // (Q / E / R / right click are skill orbs now: fired from whichever slot holds them)
            if (SkillSystem.KeyDown(kb, 'Q')) TryUse(1);
            if (SkillSystem.KeyDown(kb, 'E')) TryUse(2);
            if (SkillSystem.KeyDown(kb, 'R')) TryUse(3);
            // Space: the weapon's evasive step (not while the click's charge is held: Space is its lunge then)
            if (skills.Length > 4 && (miner == null || !miner.IsCharging) && SkillSystem.DodgeDown(kb)) TryUse(4);
        }

        private PlayerMiner miner;

        // Which way the keys are pushing (world space, as the shared movement reads them); the facing when none
        protected Vector3 MoveKeysDirection()
        {
            Keyboard kb = Keyboard.current;
            Vector3 dir = Vector3.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) dir.z += 1f;
                if (kb.sKey.isPressed) dir.z -= 1f;
                if (kb.dKey.isPressed) dir.x += 1f;
                if (kb.aKey.isPressed) dir.x -= 1f;
            }
            if (CameraModeController.FirstPersonActive)
                dir = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * dir;
            else if (GetComponent<JOT_OrbitMove>() != null && Camera.main != null)
            {
                // (the camera swings round a giant: the keys follow the screen — JOT_OrbitMove)
                Vector3 f = Flat(Camera.main.transform.forward);
                if (f.sqrMagnitude > 0.0001f)
                    dir = Quaternion.LookRotation(f.normalized) * dir;
            }
            return dir.sqrMagnitude > 0.01f ? dir.normalized : Flat(transform.forward).normalized;
        }

        // Slot 0..3 = right click, Q, E, R; 4 = Space (also for tests and on-screen buttons)
        public void Use(int index) => TryUse(index);

        private void TryUse(int index)
        {
            Skill skill = skills[index];
            if (Time.time < skill.readyAt || (acting && !skill.usableWhileActing))
                return;
            skill.use();
        }

        protected void StartCooldown(Skill skill, float scale = 1f) => skill.readyAt = Time.time + skill.cooldown * scale;

        // The hero's own controls off while a skill moves them, back on after
        protected void Lock(bool on)
        {
            acting = on;
            foreach (Behaviour b in controls)
                if (b != null) b.enabled = !on;
        }

        // ---- Aiming

        // For automated tests only: a ground point used instead of the mouse (null in play)
        public static Vector3? TestAimPoint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearTestAim() => TestAimPoint = null;

        protected bool CursorPoint(out Vector3 point)
        {
            point = transform.position + transform.forward * 4f;
            if (TestAimPoint.HasValue)
            {
                point = TestAimPoint.Value;
                return true;
            }
            Mouse mouse = Mouse.current;
            Camera view = Camera.main;
            if (view == null)
                return false;
            // Third person: the mouse cursor. First person (V): the crosshair in the middle of the screen
            bool firstPerson = CameraModeController.FirstPersonActive;
            if (!firstPerson && mouse == null)
                return false;
            Ray ray = firstPerson ? view.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : view.ScreenPointToRay(mouse.position.ReadValue());

            // Pointing right at a creature's body: that creature's spot (tall bodies hide the ground behind them)
            float nearest = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance >= nearest || hit.collider.transform.IsChildOf(transform))
                    continue;
                nearest = hit.distance;
                Enemy enemy = hit.collider.GetComponentInParent<Enemy>();
                point = enemy != null && IsCreature(enemy) ? enemy.transform.position : hit.point;
            }

            // Otherwise the floor the hero stands on, exactly where the cursor sits on the ground
            var ground = new Plane(Vector3.up, new Vector3(0f, JOT_SkillFx.GroundY(transform.position), 0f));
            if (ground.Raycast(ray, out float distance) && distance < nearest)
                point = ray.GetPoint(distance);
            else if (nearest == float.MaxValue)
            {
                // Looking level in first person with nothing ahead: straight on along the view
                Vector3 ahead = Flat(view.transform.forward);
                point = transform.position + (ahead.sqrMagnitude > 0.001f ? ahead.normalized : transform.forward) * 8f;
            }
            return true;
        }

        // Toward the cursor on the ground, or straight ahead
        protected Vector3 AimDirection()
        {
            CursorPoint(out Vector3 point);
            Vector3 to = Flat(point - transform.position);
            return to.sqrMagnitude > 0.04f ? to.normalized : transform.forward;
        }

        protected void Face(Vector3 direction)
        {
            direction = Flat(direction);
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(direction);
        }

        // How close to the cursor (beyond the body's own edge) an enemy has to be to count as the one aimed at
        protected const float CursorPick = 1.5f;

        // The living creature (not a prop or a vine) under the cursor, within reach of the hero; null if the cursor
        // points at empty ground, so skills go where the cursor is instead of snapping to some other enemy
        protected Enemy TargetNearCursor(float reach)
        {
            CursorPoint(out Vector3 point);
            Enemy best = null;
            float bestScore = float.MaxValue;
            foreach (Enemy enemy in Enemy.Alive)
            {
                if (!IsCreature(enemy) || Flat(enemy.transform.position - transform.position).magnitude > reach)
                    continue;
                var shape = enemy.GetComponent<CharacterController>();
                float size = shape != null ? shape.radius * enemy.transform.lossyScale.x : 0.5f;
                float score = Flat(enemy.transform.position - point).magnitude - size;
                if (score > CursorPick)
                    continue;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = enemy;
                }
            }
            return best;
        }

        protected static bool IsCreature(Enemy enemy) =>
            enemy != null && !enemy.IsDead && !(enemy is JOT_HittableProp) && !(enemy is JOT_Vine) && enemy.isActiveAndEnabled;

        // Everything hittable (creatures and breakable props) whose body (not just its centre) is within radius of a point: big bosses count from their edge
        protected static List<Enemy> EnemiesNear(Vector3 center, float radius)
        {
            var list = new List<Enemy>();
            foreach (Enemy enemy in Enemy.Alive.ToArray())
            {
                if (enemy == null || enemy.IsDead || !enemy.isActiveAndEnabled)
                    continue;
                var body = enemy.GetComponent<CharacterController>();
                float size = body != null ? body.radius * enemy.transform.lossyScale.x : 0f;
                if (Flat(enemy.transform.position - center).magnitude - size <= radius)
                    list.Add(enemy);
            }
            return list;
        }

        // ---- Hitting

        // One blow of multiplier × the hero's damage, with criticals and life steal like a normal hit
        protected int Strike(Enemy enemy, float multiplier, Color color, float lift = 0f)
        {
            if (enemy == null || enemy.IsDead)
                return 0;
            bool critical = Random.value < RunState.Stats.critChance;
            float total = stats.Damage * multiplier * (critical ? RunState.Stats.critMultiplier : 1f);
            int amount = Mathf.Max(1, Mathf.RoundToInt(total));
            enemy.TakeHit(transform.position, amount);
            JOT_LifeSteal steal = GetComponent<JOT_LifeSteal>();
            if (steal != null)
                steal.Credit(enemy, amount);
            JOT_PlayerFeedback.Popup(enemy.transform.position + Vector3.up * (1.6f + lift), critical ? amount + "!" : amount.ToString(), color, critical ? 1.3f : 1.1f);
            return amount;
        }

        // Stunned on the spot; bosses (the guardian, a mid-boss) aren't
        protected static void Stun(Enemy enemy, float seconds)
        {
            if (IsCreature(enemy) && !enemy.IsBoss)
                enemy.Knockdown(seconds);
        }

        // Bosses get dazed instead: the guardian properly groggy, others just interrupted
        protected static void Daze(Enemy enemy, float seconds)
        {
            if (!IsCreature(enemy) || !enemy.IsBoss)
                return;
            if (enemy is JOT_LastGuardian guardian)
                guardian.Groggy(seconds);
            else
                enemy.Knockdown(seconds);
        }

        // ---- Moving the hero

        // Slides the hero along a path through the character controller (walls stop them), calling step with each new spot
        protected IEnumerator Slide(Vector3 direction, float distance, float seconds, Action<Vector3> step = null, float height = 0f)
        {
            direction = Flat(direction).normalized;
            float t = 0f, travelled = 0f, lifted = 0f;
            // A leap passes over bodies instead of climbing onto them (a giant boss's collider is many metres tall)
            bool leap = height > 0f;
            float startY = transform.position.y;
            if (leap) IgnoreEnemyBodies(true);
            while (t < seconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / seconds);
                float along = (1f - (1f - k) * (1f - k)) * distance - travelled;   // fast start, soft end
                travelled += along;
                float up = Mathf.Sin(k * Mathf.PI) * height - lifted;
                lifted += up;
                body.Move(direction * along + Vector3.up * up + (height > 0f ? Vector3.zero : Vector3.down * 2f * Time.deltaTime));
                step?.Invoke(transform.position);
                yield return null;
            }
            body.Move(Vector3.down * (lifted + 0.5f));
            if (leap)
            {
                StepOutOfEnemyBodies();
                IgnoreEnemyBodies(false);
                StepOffHighGround(startY);
            }
        }

        // A leap came down on top of something (a root ridge, a rock): set down beside it, on the floor it left from
        private void StepOffHighGround(float floorY)
        {
            if (transform.position.y < floorY + 0.5f)
                return;
            Vector3 here = transform.position;
            Vector3 back = -Flat(transform.forward).normalized;
            float feet = FeetY(floorY);
            float half = Mathf.Max(0f, body.height * 0.5f - body.radius);
            body.enabled = false;   // (its own capsule out of the checks)
            Vector3 found = here;
            for (float r = 1f; r <= 7f && found == here; r += 0.75f)
                for (int i = 0; i < 16; i++)
                {
                    // (tried first behind, the side it came from)
                    Vector3 dir = Quaternion.Euler(0f, (i % 2 == 0 ? 1 : -1) * (i / 2) * 22.5f, 0f) * back;
                    Vector3 c = new Vector3(here.x, floorY, here.z) + dir * r;
                    // the floor there (not the top of something)
                    if (!Physics.Raycast(c + Vector3.up * 8f, Vector3.down, out RaycastHit hit, 14f, ~0, QueryTriggerInteraction.Ignore)
                        || hit.point.y > feet + 0.35f)
                        continue;
                    Vector3 mid = c + body.center + Vector3.up * 0.15f;
                    if (Physics.CheckCapsule(mid - Vector3.up * half, mid + Vector3.up * half, body.radius * 0.95f, ~0, QueryTriggerInteraction.Ignore))
                        continue;
                    found = c;
                    break;
                }
            transform.position = found;
            body.enabled = true;
        }

        private float FeetY(float rootY) => rootY + body.center.y - body.height * 0.5f;

        private readonly List<Collider> passedThrough = new List<Collider>();

        private void IgnoreEnemyBodies(bool on)
        {
            if (body == null) return;
            if (on)
            {
                foreach (Enemy e in Enemy.Alive)
                {
                    if (e == null) continue;
                    var c = e.GetComponent<CharacterController>();
                    if (c == null || !c.enabled) continue;
                    Physics.IgnoreCollision(body, c, true);
                    passedThrough.Add(c);
                }
                return;
            }
            foreach (Collider c in passedThrough)
                if (c != null) Physics.IgnoreCollision(body, c, false);
            passedThrough.Clear();
        }

        // Landed inside a body: set down at its edge, on the side the player came from
        private void StepOutOfEnemyBodies()
        {
            foreach (Enemy e in Enemy.Alive)
            {
                if (e == null) continue;
                var c = e.GetComponent<CharacterController>();
                if (c == null || !c.enabled) continue;
                Vector3 away = Flat(transform.position - e.transform.position);
                float need = c.radius * Mathf.Max(e.transform.lossyScale.x, e.transform.lossyScale.z) + body.radius + 0.25f;
                if (away.magnitude >= need) continue;
                Vector3 dir = away.sqrMagnitude > 0.01f ? away.normalized : -Flat(transform.forward).normalized;
                Teleport(e.transform.position + dir * need);
            }
        }

        // Straight to a spot (behind an enemy): the controller is switched off for the jump
        protected void Teleport(Vector3 position)
        {
            body.enabled = false;
            transform.position = new Vector3(position.x, transform.position.y, position.z);
            body.enabled = true;
        }

        // A clear line on the ground from here to there (no wall or rock in between)
        protected bool ClearPath(Vector3 to)
        {
            Vector3 from = transform.position + Vector3.up * 0.6f;
            Vector3 target = new Vector3(to.x, from.y, to.z);
            foreach (RaycastHit hit in Physics.RaycastAll(from, (target - from).normalized, Vector3.Distance(from, target), ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.GetComponentInParent<Enemy>() == null && hit.collider.GetComponentInParent<PlayerStats>() == null)
                    return false;
            return true;
        }

        // ---- Looks

        protected Animator ActiveAnimator()
        {
            foreach (Animator a in GetComponentsInChildren<Animator>())
                if (a.isActiveAndEnabled)
                    return a;
            return null;
        }

        protected void PlayState(string state)
        {
            Animator a = ActiveAnimator();
            if (a != null && a.HasState(0, Animator.StringToHash(state)))
                a.CrossFadeInFixedTime(state, 0.05f);
        }

        // A plain axe from primitives: a handle and a wedge head (low poly, no new model needed)
        protected static GameObject MakeAxe(Transform parent, float size, Color metal)
        {
            var axe = new GameObject("JOT_AxeFx");
            axe.transform.SetParent(parent, false);
            Mats.Shape(PrimitiveType.Cylinder, axe.transform, Vector3.zero, new Vector3(0.07f, 0.45f, 0.07f) * size,
                Mats.Get("JOT_fx_axe_handle", new Color(0.35f, 0.22f, 0.12f)));
            Mats.Shape(PrimitiveType.Cube, axe.transform, new Vector3(0.14f, 0.32f, 0f) * size, new Vector3(0.28f, 0.22f, 0.04f) * size,
                Mats.Get("JOT_fx_axe_" + ColorUtility.ToHtmlStringRGB(metal), metal, 0.7f, metal * 0.6f));
            return axe;
        }

        // ---- Skill bar, centred above the HP bar

        private void OnGUI()
        {
            if (skills == null || SkillBar.Hidden)
                return;
            // the game's one skill bar (the twin blades' layout), in its order: Q E R, right click, Space
            var slots = new List<SkillBar.Slot>(skills.Length);
            foreach (Skill s in skills.OrderBy(k => SkillBar.Order(k.key)))
                slots.Add(new SkillBar.Slot
                {
                    key = s.key, name = s.name, icon = Icon(s), learned = true,
                    left = s.readyAt - Time.time, total = s.cooldown, note = s.note != null ? s.note() : null,
                });
            SkillBar.Draw(slots);
        }

        // Icons live in Resources/JOT_SkillIcons, named by the skill's icon id; none yet = a plain slot
        private readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();

        private Texture2D Icon(Skill s)
        {
            if (string.IsNullOrEmpty(s.icon))
                return null;
            if (!icons.TryGetValue(s.icon, out Texture2D tex))
            {
                tex = Resources.Load<Texture2D>("JOT_SkillIcons/" + s.icon);
                icons[s.icon] = tex;
            }
            return tex;
        }

        protected static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
