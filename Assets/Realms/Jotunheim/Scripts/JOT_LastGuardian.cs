using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace NineWorlds.Jotunheim
{
    // Jotunheim's boss, the Last Guardian: a giant of rock overgrown with moss and vines. Six patterns:
    //   땅 강타 (pound: rings of force with safe gaps), 휩쓸기 (sweep: a wide arc in front), 바위 투척 (a rock lobbed
    //   where the player stands), 넝쿨 추적 (a creeping vine that binds; cut it to cancel), 돌진 (a straight charge —
    //   crashing into a wall or a rock spire leaves it dazed), 뿌리 폭발 (a line of roots bursting toward the player).
    // Phase 2 (60%): a cutscene, then everything harder — the sweep comes back the other way, two charges, three lines
    //   of roots, three rocks at once — and if a giant's weapon is still chained to a wall it tears it down: carried in
    //   both arms it hits harder, and its next throw is the weapon itself.
    // Phase 3 (25%): a cutscene and 대지의 분노 once — it strides to the middle and the whole arena erupts, but for a
    //   few glowing rune circles; then it fights on, quicker still.
    // Hurting it shakes debris loose from above. A falling ancient tree or a parry up close leaves it groggy.
    public class JOT_LastGuardian : JOT_EnemyBase
    {
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int SwingHash = Animator.StringToHash("Swing");

        [SerializeField] private Animator animator;
        [SerializeField] private Transform hand;               // rocks and the weapon are held here
        [SerializeField] private GameObject rockPrefab;        // visual only; colliders are stripped when thrown
        [SerializeField, Range(0f, 1f)] private float phaseTwoAt = 0.6f;
        [SerializeField, Range(0f, 1f)] private float phaseThreeAt = 0.25f;
        [SerializeField] private float attackCooldown = 1.4f;
        [SerializeField] private float recoverTime = 0.9f;     // stands still after an attack: the player's opening
        [SerializeField] private float attackScale = 1f;       // grows the pound rings and reach with the guardian's size

        [Header("Ground pound (rings)")]
        [SerializeField] private float poundRange = 6f;
        [SerializeField] private float poundWarn = 1.0f;
        [SerializeField] private float poundInterval = 0.45f;
        [SerializeField] private float poundDamage = 18f;

        [Header("Rock throw (player far away)")]
        [SerializeField] private float throwMinRange = 9f;
        [SerializeField] private float throwWindup = 0.8f;
        [SerializeField] private float throwFlight = 1.1f;
        [SerializeField] private float rockRadius = 1.6f;
        [SerializeField] private float rockDamage = 15f;

        [Header("Vine chase")]
        [SerializeField] private float vineMinRange = 4f;
        [SerializeField] private float vineMaxRange = 13f;
        [SerializeField] private float vineCooldown = 18f;

        [Header("Phase 2: the giant's weapon")]
        [SerializeField] private float armedDamageMultiplier = 1.5f;
        [SerializeField] private float weaponRadius = 2.6f;
        [SerializeField] private float weaponDamage = 26f;
        [SerializeField] private float heaveTime = 1.8f;       // the weapon is so heavy it takes both arms and a while to hoist
        [SerializeField] private float weaponMinRange = 4f;    // closer than this it pounds with the weapon instead of throwing it

        [Header("Sweep (휩쓸기)")]
        [SerializeField] private float sweepRadius = 3.6f;     // (times attackScale)
        [SerializeField] private float sweepArc = 160f;
        [SerializeField] private float sweepWindup = 0.9f;
        [SerializeField] private float sweepDamage = 16f;

        [Header("Charge (돌진)")]
        [SerializeField] private float chargeMinRange = 6f;
        [SerializeField] private float chargeLength = 16f;
        [SerializeField] private float chargeWindup = 1.0f;
        [SerializeField] private float chargeSpeed = 15f;
        [SerializeField] private float chargeDamage = 20f;
        [SerializeField] private float chargeCrashDaze = 1.8f;  // ran into a wall or a rock spire

        [Header("Root burst (뿌리 폭발)")]
        [SerializeField] private float rootSpacing = 2.4f;
        [SerializeField] private float rootRadius = 1.3f;
        [SerializeField] private float rootWindup = 0.9f;
        [SerializeField] private float rootStep = 0.2f;
        [SerializeField] private float rootDamage = 14f;

        [Header("Phase 3: 대지의 분노")]
        [SerializeField] private float quakeRadius = 19f;
        [SerializeField] private float quakeWindup = 3.2f;
        [SerializeField] private float quakeDamage = 35f;
        [SerializeField] private int safeSpots = 3;
        [SerializeField] private float safeRadius = 2.4f;

        [Header("Weak spot")]
        [SerializeField] private float groggyDamageMultiplier = 2f; // knocked silly by a tree or a weapon, it takes double damage

        [Header("Debris shaken loose when hurt")]
        [SerializeField, Range(0.01f, 0.5f)] private float debrisEveryFraction = 0.1f;   // of max health: every 10% lost
        [SerializeField] private float debrisMinGap = 4f;            // seconds between showers at the most
        [SerializeField] private int debrisCount = 2;
        [SerializeField] private float debrisRadius = 1.3f;
        [SerializeField] private float debrisDamage = 10f;

        private static readonly Vector2[] PoundBands = { new Vector2(0f, 2.5f), new Vector2(3.5f, 5f), new Vector2(6f, 7.5f) };
        private static readonly Vector2[] ArmedPoundBands = { new Vector2(0f, 3f), new Vector2(4f, 5.5f), new Vector2(6.5f, 8f), new Vector2(9f, 10.5f) };

        private float attackReadyAt;
        private float vineReadyAt;
        private JOT_Vine vine;
        private JOT_RingWaves poundWaves;   // the pound's rings, cut off if it is interrupted
        private GameObject heldRock;
        private Transform heldWeapon;
        private bool wantsWeapon;
        private int damageSinceDebris;
        private float debrisReadyAt;
        private Quaternion uprightPose = Quaternion.identity;
        private string lastAttack;
        private bool unstoppable;   // 대지의 분노 can't be knocked out of (it must be escaped instead)
        private Vector3 arenaCenter;
        private CharacterController playerBody;

        private static readonly Color StoneColor = new Color(0.8f, 0.9f, 0.65f);

        public int Phase { get; private set; } = 1;
        public float PhaseTwoAt => phaseTwoAt;
        public float PhaseThreeAt => phaseThreeAt;
        public bool Armed => heldWeapon != null;
        public event Action<int> PhaseChanged;

        private float DamageScale => Armed ? armedDamageMultiplier : 1f;

        protected override void Awake()
        {
            base.Awake();
            IsBoss = true;
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (visual != null)
                uprightPose = visual.localRotation;
            attackReadyAt = Time.time + 1.5f;
            vineReadyAt = Time.time + 4f;
        }

        protected override void Start()
        {
            base.Start();
            WadeThroughSmallProps();
            if (player != null)
                playerBody = player.GetComponent<CharacterController>();
            // The arena's middle: the middle of its walls, or failing that where the guardian stands
            GameObject walls = GameObject.Find("JOT_ArenaWalls");
            arenaCenter = transform.position;
            if (walls != null && walls.scene == gameObject.scene)
            {
                Renderer[] parts = walls.GetComponentsInChildren<Renderer>();
                if (parts.Length > 0)
                {
                    Bounds b = parts[0].bounds;
                    foreach (Renderer r in parts) b.Encapsulate(r.bounds);
                    arenaCenter = new Vector3(b.center.x, transform.position.y, b.center.z);
                }
            }
        }

        // A giant this size steps over rocks, rune stones, pillars and trees instead of snagging on them;
        // walls, rock spires and the floor (anything wider or taller than itself) still stop it
        private void WadeThroughSmallProps()
        {
            float maxWidth = controller.radius * 2f;
            foreach (Collider c in FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
            {
                if (!c.enabled || c.isTrigger || c is CharacterController)
                    continue;
                Vector3 size = c.bounds.size;
                if (Mathf.Max(size.x, size.z) <= maxWidth && size.y <= controller.height)
                    Physics.IgnoreCollision(controller, c);
            }
        }

        protected override void ScaleDamage(float multiplier)
        {
            poundDamage *= multiplier;
            rockDamage *= multiplier;
            weaponDamage *= multiplier;
        }

        protected override Vector3 Think()
        {
            if (Phase == 1 && health <= maxHealth * phaseTwoAt)
            {
                StartCoroutine(EnterPhaseTwo());
                return Vector3.zero;
            }
            if (Phase == 2 && health <= maxHealth * phaseThreeAt)
            {
                StartCoroutine(EnterPhaseThree());
                return Vector3.zero;
            }
            if (wantsWeapon && !Armed && FindWeapon() != null)
            {
                StartCoroutine(FetchWeapon());
                return Vector3.zero;
            }

            float distance = DistanceToPlayer;
            Face(player.position, turnSpeed);
            if (Time.time >= attackReadyAt)
            {
                IEnumerator attack = PickAttack(distance);
                if (attack != null)
                {
                    StartCoroutine(attack);
                    return Vector3.zero;
                }
            }

            Vector3 move = distance > poundRange * 0.7f ? TowardPlayer * moveSpeed : Vector3.zero;
            SetSpeed(move.sqrMagnitude > 0.01f ? 0.5f : 0f);
            return move;
        }

        // Every attack that fits the distance gets an equal chance, so no pattern crowds the others out, and never the
        // same one twice running when there is another. Holding a giant's weapon, it heaves that next (unless the player
        // is right at its feet).
        private IEnumerator PickAttack(float distance)
        {
            if (Armed && distance >= weaponMinRange)
                return Throw();
            var options = new List<(string name, Func<IEnumerator> start)>();
            if (distance <= sweepRadius * attackScale * 0.95f)
                options.Add(("sweep", Sweep));
            if (distance <= poundRange)
                options.Add(("pound", Pound));
            // Past the pound's reach there is always a throw: no stretch of the arena where it only plods after the player
            if (distance >= Mathf.Min(throwMinRange, poundRange))
                options.Add(("throw", Throw));
            if (vine == null && Time.time >= vineReadyAt && distance >= vineMinRange && distance <= vineMaxRange)
                options.Add(("vine", SendVine));
            if (distance >= chargeMinRange)
                options.Add(("charge", Charge));
            if (distance >= 3f && distance <= 18f)
                options.Add(("roots", RootBurst));
            if (options.Count > 1)
                options.RemoveAll(o => o.name == lastAttack);
            if (options.Count == 0)
                return null;
            var pick = options[Random.Range(0, options.Count)];
            lastAttack = pick.name;
            return pick.start();
        }

        // Tests: start one pattern by name now (sweep, pound, throw, vine, charge, roots, wrath)
        public bool TestAttack(string name)
        {
            if (busy || dead || player == null)
                return false;
            IEnumerator run = name switch
            {
                "sweep" => Sweep(), "pound" => Pound(), "throw" => Throw(), "vine" => SendVine(),
                "charge" => Charge(), "roots" => RootBurst(), "wrath" => EarthsWrath(), _ => null,
            };
            if (run == null)
                return false;
            StartCoroutine(run);
            return true;
        }

        public bool Busy => busy;

        // 휩쓸기: a wide arc in front, read off the fan on the ground; from phase 2 it comes straight back the other way
        private IEnumerator Sweep()
        {
            busy = true;
            SetSpeed(0f);
            transform.rotation = Quaternion.LookRotation(TowardPlayer);
            float radius = sweepRadius * attackScale;
            int blows = Phase >= 2 ? 2 : 1;
            for (int i = 0; i < blows; i++)
            {
                Vector3 dir = i == 0 ? transform.forward : -transform.forward;
                float windup = i == 0 ? sweepWindup : sweepWindup * 0.65f;
                GameObject warning = Warn(JOT_Fx.Fan(transform.position, dir, radius, sweepArc, windup));
                Trigger(SwingHash);
                Quaternion from = transform.rotation, to = Quaternion.LookRotation(dir);
                for (float t = 0f; t < windup; t += Time.deltaTime)
                {
                    transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / (windup * 0.6f)));
                    yield return null;
                }
                if (warning != null) Destroy(warning);
                JOT_Area.HurtPlayer(JOT_Area.Fan(transform.position, dir, radius, sweepArc), sweepDamage * DamageScale, this);
                Vector3 chest = transform.position + Vector3.up * controller.height * 0.3f;
                JOT_SkillFx.TiltedSlash(chest, dir, 0f, radius * 0.9f, 1.2f, sweepArc, StoneColor, 0.35f);
                JOT_SkillFx.Dust(transform.position + dir * radius * 0.6f, 1.6f, 18);
                CameraShake.Shake(0.15f, 0.18f);
                yield return new WaitForSeconds(0.15f);
            }
            yield return Recover();
        }

        // 돌진: lowers its head and charges in a straight line; a wall or rock spire in the way stops it dazed.
        // From phase 2 it turns and charges a second time.
        private IEnumerator Charge()
        {
            busy = true;
            SetSpeed(0f);
            int runs = Phase >= 2 ? 2 : 1;
            for (int i = 0; i < runs; i++)
            {
                Vector3 dir = TowardPlayer;
                transform.rotation = Quaternion.LookRotation(dir);
                float length = ChargeReach(dir);
                float width = controller.radius * 2f + 0.4f;
                float windup = i == 0 ? chargeWindup : chargeWindup * 0.6f;
                GameObject warning = Warn(JOT_Fx.Line(transform.position, dir, length + controller.radius, width, windup));
                Trigger(SwingHash);
                for (float t = 0f; t < windup; t += Time.deltaTime)
                {
                    // Rears back, shaking
                    if (visual != null)
                        visual.localRotation = uprightPose * Quaternion.Euler(-10f * Mathf.Clamp01(t / windup), 0f, Mathf.Sin(t * 40f) * 1.5f);
                    yield return null;
                }
                if (warning != null) Destroy(warning);

                // The run: straight through the player (it doesn't stop for them), not through walls
                if (playerBody != null) Physics.IgnoreCollision(controller, playerBody, true);
                if (visual != null) visual.localRotation = uprightPose * Quaternion.Euler(14f, 0f, 0f);
                SetSpeed(1f);
                bool struck = false, crashed = false;
                float moved = 0f, dustAt = 0f;
                while (moved < length)
                {
                    float step = chargeSpeed * Time.deltaTime;
                    Vector3 before = transform.position;
                    CollisionFlags flags = controller.Move(dir * step);
                    float gained = Vector3.Dot(Flat(transform.position - before), dir);
                    moved += Mathf.Max(gained, 0.001f);
                    if (!struck && JOT_Area.HurtPlayer(JOT_Area.Circle(transform.position, controller.radius + 0.7f), chargeDamage * DamageScale, this))
                        struck = true;
                    if ((dustAt -= Time.deltaTime) <= 0f)
                    {
                        dustAt = 0.08f;
                        JOT_SkillFx.Dust(transform.position, 1.2f, 8);
                    }
                    if ((flags & CollisionFlags.Sides) != 0 && gained < step * 0.3f)
                    {
                        crashed = true;
                        break;
                    }
                    // Rams an ancient tree: it stops dead, and the tree comes down on top of it
                    JOT_AncientTree rammed = TreeAhead(dir);
                    if (rammed != null)
                    {
                        rammed.ToppleOnto(transform.position);
                        crashed = true;
                        break;
                    }
                    yield return null;
                }
                if (playerBody != null) Physics.IgnoreCollision(controller, playerBody, false);
                SetSpeed(0f);
                if (visual != null) visual.localRotation = uprightPose;
                if (crashed)
                {
                    // Into the wall headfirst: rubble flies and it stands there dazed (double damage while it lasts)
                    Vector3 front = transform.position + dir * controller.radius;
                    CameraShake.Shake(0.4f, 0.35f);
                    JOT_EarthBurst.Spawn(front, 0f, 2.5f, 14, 1.1f);
                    JOT_SkillFx.Dust(front, 2.2f, 24);
                    Groggy(chargeCrashDaze);   // (ends this pattern)
                    yield break;
                }
                JOT_SkillFx.Dust(transform.position, 1.8f, 16);
                yield return new WaitForSeconds(0.35f);
            }
            yield return Recover();
        }

        // A standing ancient tree just in front of a charge
        private JOT_AncientTree TreeAhead(Vector3 dir)
        {
            foreach (JOT_AncientTree tree in FindObjectsByType<JOT_AncientTree>())
            {
                if (tree == null || tree.Broken)
                    continue;
                Vector3 to = Flat(tree.transform.position - transform.position);
                if (to.magnitude <= controller.radius + 1.2f && Vector3.Dot(to, dir) > 0f)
                    return tree;
            }
            return null;
        }

        // How far a charge goes: its full length, or up to the first wall or rock spire in the way
        private float ChargeReach(Vector3 dir)
        {
            float reach = chargeLength;
            Vector3 origin = transform.position + Vector3.up * controller.height * 0.4f;
            foreach (RaycastHit hit in Physics.SphereCastAll(origin, controller.radius * 0.8f, dir, chargeLength, ~0, QueryTriggerInteraction.Ignore))
            {
                Collider c = hit.collider;
                if (c == controller || c is CharacterController || c.transform.IsChildOf(transform))
                    continue;
                Vector3 size = c.bounds.size;
                bool small = Mathf.Max(size.x, size.z) <= controller.radius * 2f && size.y <= controller.height;
                if (small || hit.distance <= 0f)
                    continue;
                reach = Mathf.Min(reach, hit.distance);
            }
            return Mathf.Max(2f, reach);
        }

        // 뿌리 폭발: roots burst up one after another along a line toward the player; three lines from phase 2
        private IEnumerator RootBurst()
        {
            busy = true;
            SetSpeed(0f);
            Trigger(AttackHash);
            Vector3 toward = TowardPlayer;
            int lines = Phase >= 2 ? 3 : 1;
            float reach = Mathf.Clamp(DistanceToPlayer + 3f, 8f, 18f);
            int count = Mathf.Max(3, Mathf.CeilToInt(reach / rootSpacing));
            // One strip per line on the ground (not a row of circles): it counts down to the first burst, then its fill
            // runs down the strip at the pace of the roots, so where they are about to come up is the bright end
            var spots = new List<Vector3>[count];
            for (int k = 0; k < count; k++)
                spots[k] = new List<Vector3>();
            float startAt = controller.radius + 1.2f;
            float stripLength = (count - 1) * rootSpacing + rootRadius * 2f;
            for (int l = 0; l < lines; l++)
            {
                Vector3 dir = Quaternion.Euler(0f, (l - (lines - 1) / 2f) * 28f, 0f) * toward;
                for (int k = 0; k < count; k++)
                    spots[k].Add(transform.position + dir * (startAt + k * rootSpacing));
                Warn(JOT_Fx.Line(transform.position + dir * (startAt - rootRadius), dir, stripLength, rootRadius * 2f,
                    stripLength / rootSpacing * rootStep, rootWindup));
            }
            yield return new WaitForSeconds(rootWindup);
            bool struck = false;
            for (int k = 0; k < count; k++)
            {
                foreach (Vector3 spot in spots[k])
                {
                    JOT_EarthBurst.Spawn(spot, 0f, rootRadius, 7, 0.7f);
                    JOT_GroundHeave.Spawn(spot, 0f, rootRadius);
                    if (!struck && JOT_Area.HurtPlayer(JOT_Area.Circle(spot, rootRadius), rootDamage * DamageScale, this))
                        struck = true;
                }
                CameraShake.Shake(0.08f, 0.1f);
                yield return new WaitForSeconds(rootStep);
            }
            ClearWarnings();
            yield return Recover();
        }

        // Rings of force spread out from the guardian one after another; the gaps between them are safe
        private IEnumerator Pound()
        {
            busy = true;
            SetSpeed(0f);
            Trigger(Armed ? AttackHash : SwingHash);
            Vector2[] bands = (Armed ? ArmedPoundBands : PoundBands).Select(b => b * attackScale).ToArray();
            poundWaves = JOT_RingWaves.Spawn(transform.position, bands, poundWarn, poundInterval, poundDamage * DamageScale, 0, this);
            yield return new WaitForSeconds(poundWarn + poundInterval * (Armed ? 4 : 3));
            yield return Recover();
        }

        // Picks up a rock and lobs it where the player stands. Once armed, it heaves the giant's weapon instead.
        private IEnumerator Throw()
        {
            busy = true;
            SetSpeed(0f);
            if (Armed)
            {
                yield return HeaveWeapon();
                yield return Recover();
                yield break;
            }

            heldRock = rockPrefab != null ? Instantiate(rockPrefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            heldRock.name = "JOT_ThrownRock";
            heldRock.transform.localScale *= 1.2f * attackScale;
            heldRock.transform.position = HandPosition;
            if (hand != null) heldRock.transform.SetParent(hand, true);
            Trigger(AttackHash);
            yield return new WaitForSeconds(throwWindup);

            if (heldRock != null)
            {
                heldRock.transform.SetParent(null, true);
                Vector3 from = heldRock.transform.position;
                JOT_Projectile.Launch(heldRock, from, player.position, throwFlight, 4f, rockRadius, rockDamage * DamageScale, 0, this);
                heldRock = null;
                // From phase 2 it hurls a handful: two more rocks land either side of the player
                if (Phase >= 2)
                {
                    Vector3 across = Vector3.Cross(Vector3.up, TowardPlayer).normalized * (rockRadius * 2.2f);
                    foreach (Vector3 side in new[] { across, -across })
                    {
                        GameObject extra = rockPrefab != null ? Instantiate(rockPrefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        extra.name = "JOT_ThrownRock";
                        extra.transform.localScale *= 0.9f * attackScale;
                        JOT_Projectile.Launch(extra, from, player.position + side, throwFlight * 1.1f, 4.5f, rockRadius * 0.85f, rockDamage * DamageScale, 0, this);
                    }
                }
            }
            yield return new WaitForSeconds(0.4f);
            yield return Recover();
        }

        // Both arms, straining: the weapon goes up over its head, shaking, then comes crashing down where the player is
        private IEnumerator HeaveWeapon()
        {
            Transform weapon = heldWeapon;
            Vector3 carry = weapon.localPosition;
            Vector3 overhead = new Vector3(carry.x, controller.height * 1.05f, -0.4f);
            Trigger(SwingHash);
            for (float t = 0f; t < heaveTime; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / heaveTime);
                Vector3 strain = Random.insideUnitSphere * 0.15f * (0.3f + k);
                weapon.localPosition = Vector3.Lerp(carry, overhead, k) + strain;
                Face(player.position, turnSpeed * 0.4f);
                yield return null;
            }
            CameraShake.Shake(0.25f, 0.2f);

            Trigger(AttackHash);
            heldWeapon = null;
            weapon.SetParent(null, true);
            // No source on purpose: a falling giant's weapon is too heavy to parry, it has to be dodged
            JOT_Projectile.Launch(weapon.gameObject, weapon.position, player.position, throwFlight * 1.4f, 6f, weaponRadius, weaponDamage, 0, null, stayOnGround: true);
            yield return new WaitForSeconds(0.6f);
        }

        private IEnumerator SendVine()
        {
            busy = true;
            SetSpeed(0f);
            Trigger(SwingHash);
            yield return new WaitForSeconds(0.5f);
            vine = JOT_Vine.Launch(transform.position + transform.forward * (1.5f * attackScale), player.position, this);
            vineReadyAt = Time.time + vineCooldown;
            yield return Recover(); // the vine counts as an attack: the same pause follows it
        }

        private IEnumerator Recover()
        {
            yield return new WaitForSeconds(recoverTime);
            attackReadyAt = Time.time + attackCooldown * Random.Range(0.8f, 1.2f);
            busy = false;
        }

        private IEnumerator EnterPhaseTwo()
        {
            busy = true;
            Phase = 2;
            SetSpeed(0f);
            wantsWeapon = FindWeapon() != null;
            attackCooldown *= wantsWeapon ? 0.8f : 0.7f; // angrier (more so with no weapon left to take)
            PhaseChanged?.Invoke(Phase);
            // A short cutscene: it rears up and roars, the arena shakes, and the new phase is named
            JOT_Cutscene.Play(transform, PhaseTwoScene(), true);
            yield return null;
            while (BossCutscene.Playing)
                yield return null;
            attackReadyAt = Time.time + 0.6f;
            busy = false;
        }

        private IEnumerator PhaseTwoScene()
        {
            yield return new WaitForSeconds(0.5f);
            Trigger(AttackHash);
            yield return new WaitForSeconds(0.35f);
            CameraShake.Shake(0.5f, 0.35f);
            Shockwave.Spawn(transform.position, 6f, JOT_Cutscene.GuardianColor, 30);
            JOT_EarthBurst.Spawn(transform.position, 1f, 4f * attackScale, 18, 1.3f);
            BossCutscene.Flash(0.35f);
            JOT_Cutscene.Title("분노한 수호자", wantsWeapon ? "사슬에 묶인 거인의 무기를 노린다" : "더 빠르고 거칠게 몰아친다", JOT_Cutscene.GuardianColor);
            BossCutscene.Dolly(0.85f);
            yield return new WaitForSeconds(1.4f);
        }

        // Phase 3: a cutscene, then 대지의 분노 once, then the fight goes on at a quicker pace
        private IEnumerator EnterPhaseThree()
        {
            busy = true;
            Phase = 3;
            SetSpeed(0f);
            attackCooldown *= 0.75f;
            PhaseChanged?.Invoke(Phase);
            JOT_Cutscene.Play(transform, PhaseThreeScene(), true);
            yield return null;
            while (BossCutscene.Playing)
                yield return null;
            yield return EarthsWrath();
        }

        private IEnumerator PhaseThreeScene()
        {
            yield return new WaitForSeconds(0.4f);
            Trigger(AttackHash);
            yield return new WaitForSeconds(0.35f);
            CameraShake.Shake(0.6f, 0.4f);
            Shockwave.Spawn(transform.position, 8f, new Color(1f, 0.45f, 0.2f), 36);
            JOT_EarthBurst.Spawn(transform.position, 1f, 5f * attackScale, 22, 1.4f);
            JOT_SkillFx.Cracks(transform.position, 6f * attackScale, 10, new Color(1f, 0.4f, 0.15f), 2.5f);
            BossCutscene.Flash(0.5f);
            JOT_Cutscene.Title("최후의 저항", "대지가 분노한다 — 빛나는 룬 안으로 피하라", new Color(1f, 0.5f, 0.25f));
            BossCutscene.Dolly(0.8f);
            yield return new WaitForSeconds(1.5f);
        }

        // 대지의 분노: it strides to the middle of the arena, raises both fists, and the whole floor erupts —
        // all but a few glowing rune circles, which are safe
        private IEnumerator EarthsWrath()
        {
            busy = true;
            unstoppable = true;
            // To the middle
            float giveUp = Time.time + 3f;
            while (Time.time < giveUp && FlatDistance(transform.position, arenaCenter) > 1.5f)
            {
                Vector3 to = Flat(arenaCenter - transform.position).normalized;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), turnSpeed * Time.deltaTime);
                controller.Move(to * moveSpeed * 2.5f * Time.deltaTime);
                SetSpeed(1f);
                yield return null;
            }
            SetSpeed(0f);
            Face(player.position, 9999f);
            Trigger(AttackHash);

            // The safe circles: spread around the arena, at least one within reach of the player
            var safe = new List<Vector3>();
            Vector3 nearPlayer = player.position + Flat(Random.insideUnitSphere).normalized * 4f;
            if (FlatDistance(nearPlayer, arenaCenter) > quakeRadius - safeRadius - 3f || FlatDistance(nearPlayer, arenaCenter) < 6f)
                nearPlayer = arenaCenter + Flat(player.position - arenaCenter).normalized * 9f;
            safe.Add(new Vector3(nearPlayer.x, arenaCenter.y, nearPlayer.z));
            for (int tries = 0; safe.Count < safeSpots && tries < 60; tries++)
            {
                Vector2 r = Random.insideUnitCircle * (quakeRadius - safeRadius - 3f);
                Vector3 spot = arenaCenter + new Vector3(r.x, 0f, r.y);
                if (FlatDistance(spot, arenaCenter) < 6f || safe.Any(s => FlatDistance(s, spot) < 7f))
                    continue;
                safe.Add(spot);
            }
            Warn(JOT_Fx.Circle(arenaCenter, quakeRadius, quakeWindup, JOT_Fx.CrackDecal));
            foreach (Vector3 s in safe)
                Warn(JOT_Fx.Safe(s, safeRadius, quakeWindup));

            // Fists raised, the ground trembling more and more
            for (float t = 0f; t < quakeWindup; t += Time.deltaTime)
            {
                float k = t / quakeWindup;
                if (visual != null)
                    visual.localRotation = uprightPose * Quaternion.Euler(-12f * k, 0f, Mathf.Sin(t * 50f) * 2f * k);
                if (Random.value < Time.deltaTime * 6f)
                    CameraShake.Shake(0.1f, 0.05f + 0.15f * k);
                yield return null;
            }
            if (visual != null)
                visual.localRotation = uprightPose;
            ClearWarnings();

            // Down they come: everywhere but the rune circles erupts. (No source: it can't be parried, only escaped.)
            Trigger(SwingHash);
            CameraShake.Shake(0.7f, 0.5f);
            JOT_Area.HurtPlayer(p => FlatDistance(p, arenaCenter) <= quakeRadius && !safe.Any(s => FlatDistance(p, s) <= safeRadius), quakeDamage);
            Shockwave.Spawn(arenaCenter, quakeRadius, new Color(1f, 0.45f, 0.2f), 60);
            for (int i = 0; i < 4; i++)
                JOT_GroundHeave.Spawn(arenaCenter, i * quakeRadius / 4f, (i + 1) * quakeRadius / 4f - 0.5f);
            for (int i = 0; i < 14; i++)
            {
                Vector2 r = Random.insideUnitCircle * quakeRadius;
                Vector3 at = arenaCenter + new Vector3(r.x, 0f, r.y);
                if (!safe.Any(s => FlatDistance(at, s) <= safeRadius + 1f))
                    JOT_EarthBurst.Spawn(at, 0f, 2.2f, 6, 1f);
            }
            foreach (Vector3 s in safe)
                JOT_SkillFx.Ring(s, safeRadius * 0.5f, safeRadius * 1.3f, 0.3f, JOT_Fx.SafeColor, 0.5f);
            yield return new WaitForSeconds(1.2f);
            unstoppable = false;
            attackReadyAt = Time.time + 0.8f;
            busy = false;
        }

        // Walks up to the nearest giant's weapon still chained to a wall and tears it down
        private IEnumerator FetchWeapon()
        {
            busy = true;
            JOT_ChainedWeapon target = FindWeapon();
            float giveUp = Time.time + 8f;
            while (target != null && target.Available && Time.time < giveUp &&
                   FlatDistance(transform.position, target.LandingPoint) > 1.5f * attackScale)
            {
                Vector3 to = Flat(target.LandingPoint - transform.position).normalized;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), turnSpeed * Time.deltaTime);
                controller.Move(to * moveSpeed * 1.8f * Time.deltaTime);
                SetSpeed(1f);
                yield return null;
            }
            SetSpeed(0f);
            if (target != null && target.Available)
            {
                Trigger(SwingHash);
                yield return new WaitForSeconds(0.5f);
                heldWeapon = target.TakeBy(transform);
                if (heldWeapon != null)
                {
                    // Too big for one hand: carried across the chest in both arms (VARCO props run along their local z, pommel at 0)
                    var mesh = heldWeapon.GetComponentInChildren<MeshFilter>();
                    float length = mesh != null ? mesh.sharedMesh.bounds.size.z * mesh.transform.lossyScale.z : 4f;
                    heldWeapon.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.forward);
                    heldWeapon.localPosition = new Vector3(-length / 2f, controller.height * 0.5f, controller.radius + 0.6f);
                    JOT_EarthBurst.Spawn(transform.position, 1f, 3f * attackScale, 16, 1.3f);
                }
            }
            wantsWeapon = false;
            attackReadyAt = Time.time + 0.5f;
            busy = false;
        }

        private JOT_ChainedWeapon FindWeapon() =>
            FindObjectsByType<JOT_ChainedWeapon>()
                .Where(w => w.Available)
                .OrderBy(w => FlatDistance(w.transform.position, transform.position))
                .FirstOrDefault();

        public override void TakeHit(Vector3 from, int damage)
        {
            int before = health;
            if (Stunned)
                damage = Mathf.RoundToInt(damage * groggyDamageMultiplier);
            base.TakeHit(from, damage);
            if (dead)
                return;
            damageSinceDebris += before - health;
            // A shower every tenth of its health lost, and never more than one every few seconds
            if (damageSinceDebris >= Mathf.Max(1f, maxHealth * debrisEveryFraction) && Time.time >= debrisReadyAt)
            {
                damageSinceDebris = 0;
                debrisReadyAt = Time.time + debrisMinGap;
                DropDebris();
            }
        }

        // Chunks of rock shaken loose fall around the player
        private void DropDebris()
        {
            if (player == null)
                return;
            for (int i = 0; i < debrisCount; i++)
            {
                Vector2 offset = Random.insideUnitCircle * 4f;
                Vector3 spot = player.position + new Vector3(offset.x, 0f, offset.y);
                GameObject rock = rockPrefab != null ? Instantiate(rockPrefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = "JOT_Debris";
                rock.transform.localScale *= 0.7f;
                JOT_Projectile.Launch(rock, spot + Vector3.up * 12f, spot, 1.1f + i * 0.25f, 0f, debrisRadius, debrisDamage);
            }
        }

        // A full-charge slam from the player only staggers a boss
        public override void Knockdown(float seconds)
        {
            if (!unstoppable)
                base.Knockdown(Mathf.Min(seconds, 0.5f));
        }

        // Crushed by a tree (or similar): properly dazed
        public void Groggy(float seconds)
        {
            if (unstoppable)
                return;
            base.Knockdown(seconds);
            StartCoroutine(Dazed(seconds));
        }

        private IEnumerator Dazed(float seconds)
        {
            SetSpeed(0f);
            if (visual == null)
                yield break;
            Transform body = visual;
            Quaternion upright = uprightPose;
            Quaternion slumped = upright * Quaternion.Euler(-18f, 0f, 8f);
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                float k = Mathf.Clamp01((end - Time.time) / 0.4f) * Mathf.Clamp01((Time.time - (end - seconds)) / 0.3f);
                body.localRotation = Quaternion.Slerp(upright, slumped, k);
                yield return null;
            }
            body.localRotation = upright;
        }

        protected override void OnInterrupted()
        {
            if (heldRock != null)
                Destroy(heldRock);
            heldRock = null;
            // Rings still to come from an interrupted pound go with it (a war cry or a slam cuts the pattern off at once)
            if (poundWaves != null)
                Destroy(poundWaves.gameObject);
            poundWaves = null;
            if (visual != null)
                visual.localRotation = uprightPose;
            // (cut off mid-charge: it may no longer run through the player)
            if (playerBody != null && controller != null)
                Physics.IgnoreCollision(controller, playerBody, false);
            SetSpeed(0f);
            // The attack it lost still counts: the usual opening follows before the next one
            attackReadyAt = Mathf.Max(attackReadyAt, Time.time + recoverTime + 0.5f);
        }

        // The style toggle (F2) swaps the model: its animator and the hand that holds rocks and the weapon
        public void SetRig(Animator rig, Transform rigHand)
        {
            animator = rig;
            hand = rigHand;
        }

        private Vector3 HandPosition => hand != null ? hand.position : transform.position + (transform.right * 1.2f + Vector3.up * 2.5f) * attackScale;

        private void SetSpeed(float value)
        {
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetFloat(SpeedHash, value, 0.1f, Time.deltaTime);
        }

        private void Trigger(int hash)
        {
            if (animator != null && animator.runtimeAnimatorController != null)
                animator.SetTrigger(hash);
        }
    }
}
