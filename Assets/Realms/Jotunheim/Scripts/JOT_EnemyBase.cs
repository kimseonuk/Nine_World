using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // Shared plumbing for Jotunheim's enemies: health, gravity and knockback on a CharacterController,
    // spotting the player, hit flash, stun and a crumbling death. Each enemy decides what to do in Think().
    [RequireComponent(typeof(CharacterController))]
    public abstract class JOT_EnemyBase : Enemy
    {
        [SerializeField] protected Transform visual;
        [SerializeField] protected int maxHealth = 5;
        [SerializeField] protected float detectRange = 10f;
        [SerializeField] protected float moveSpeed = 3f;
        [SerializeField] protected float turnSpeed = 360f;
        [SerializeField] private float gravity = -9.81f;

        [Header("Getting hit")]
        [SerializeField] protected float knockbackSpeed = 4f;
        [SerializeField] private float knockbackDamping = 10f;
        [SerializeField] private Color hitTint = new Color(1f, 0.4f, 0.4f);

        [Header("Animation (rigged SD models)")]
        [SerializeField] private bool driveAnimator;     // walk/idle and attacks from here (the guardian drives its own)
        [SerializeField] private Animator rigAnimator;   // JOT_ActorBase-style: Speed, Attack, Swing, AttackSpeed

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int SwingHash = Animator.StringToHash("Swing");
        private static readonly int AttackSpeedHash = Animator.StringToHash("AttackSpeed");
        private const float AttackStateSpeed = 1.4f;   // the Attack / Swing states' own speeds in JOT_ActorBase
        private const float SwingStateSpeed = 1.5f;

        protected CharacterController controller;
        protected Transform player;
        protected PlayerStats playerStats;
        protected int health;
        protected bool dead;
        protected bool aware;   // has spotted the player
        protected bool busy;    // in the middle of an attack (or similar): Think() is skipped
        protected Vector3 visualScale = Vector3.one;
        private float stunnedUntil;
        private float verticalVelocity;
        private Vector3 knockback;
        private readonly List<GameObject> warnings = new List<GameObject>();

        public override int Health => Mathf.Max(0, health);
        public override int MaxHealth => maxHealth;
        protected bool Stunned => Time.time < stunnedUntil;
        protected float DistanceToPlayer => player != null ? FlatDistance(player.position, transform.position) : float.MaxValue;
        protected Vector3 TowardPlayer => player != null ? Flat(player.position - transform.position).normalized : transform.forward;

        // True while the enemy moves itself (a dash): Update leaves its position alone
        protected virtual bool ControlsOwnMovement => false;

        protected virtual void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (visual == null && transform.childCount > 0)
                visual = transform.GetChild(0);
            if (visual != null)
                visualScale = visual.localScale;
            if (!driveAnimator)
                rigAnimator = null;
            else if (rigAnimator == null)
                rigAnimator = GetComponentInChildren<Animator>();
            health = maxHealth;
        }

        protected virtual void Start()
        {
            PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
            if (movement != null)
            {
                player = movement.transform;
                playerStats = movement.GetComponent<PlayerStats>();
            }
        }

        public override void Scale(float healthMultiplier, float damageMultiplier)
        {
            maxHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * healthMultiplier));
            health = maxHealth;
            ScaleDamage(damageMultiplier);
        }

        protected abstract void ScaleDamage(float multiplier);

        // Called every frame while free to act and aware of the player; returns the walking velocity
        protected abstract Vector3 Think();

        private void Update()
        {
            if (dead)
                return;

            Vector3 move = Vector3.zero;
            // (a boss cutscene holds everyone still, on its own as well as inside the full game)
            if (!busy && !Stunned && player != null && GameManager.IsRunActive && !BossCutscene.Playing)
            {
                if (!aware && DistanceToPlayer <= detectRange)
                    aware = true;
                if (aware)
                    move = Think();
            }

            knockback = Vector3.Lerp(knockback, Vector3.zero, knockbackDamping * Time.deltaTime);
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f;
            verticalVelocity += gravity * Time.deltaTime;
            if (!ControlsOwnMovement)
                controller.Move((move + knockback + Vector3.up * verticalVelocity) * Time.deltaTime);
            // 0 = idle, 0.5 = walk (its own move speed), 1 = run
            if (rigAnimator != null)
                rigAnimator.SetFloat(SpeedHash, Mathf.Clamp01(Flat(move).magnitude / Mathf.Max(0.01f, moveSpeed)) * 0.5f, 0.1f, Time.deltaTime);
        }

        // The style toggle (F2) swaps which model under the visual is shown and animated
        public void SetRigAnimator(Animator rig)
        {
            if (driveAnimator)
                rigAnimator = rig;
        }

        // Plays the model's attack (or its second move, the Swing slot) timed so the blow,
        // clipImpact seconds into the clip, lands as the telegraph runs out
        protected void PlayAttack(bool swing, float clipImpact, float windup)
        {
            if (rigAnimator == null)
                return;
            float stateSpeed = swing ? SwingStateSpeed : AttackStateSpeed;
            rigAnimator.SetFloat(AttackSpeedHash, Mathf.Clamp(clipImpact / (stateSpeed * Mathf.Max(0.05f, windup)), 0.25f, 3f));
            rigAnimator.SetTrigger(swing ? SwingHash : AttackHash);
        }

        public override void TakeHit(Vector3 from, int damage)
        {
            if (dead)
                return;

            health -= Mathf.Max(1, ModifyDamage(damage));
            if (!ControlsOwnMovement)
                knockback = Flat(transform.position - from).normalized * knockbackSpeed;
            aware = true;
            StartCoroutine(Flash());

            if (health <= 0)
                Die();
        }

        // Lets an enemy soften blows (a gargoyle in its shell)
        protected virtual int ModifyDamage(int damage) => damage;

        // Knocked flat: whatever it was doing is cancelled
        public override void Knockdown(float seconds)
        {
            if (dead)
                return;
            StopAllCoroutines();
            ClearWarnings();
            ClearFlash();
            busy = false;
            stunnedUntil = Time.time + seconds;
            OnInterrupted();
        }

        protected virtual void OnInterrupted() { }

        // Player skills: pushed away (a spin), thrown up (a ground smash), drawn to the player (a roar)
        public void Shove(Vector3 velocity)
        {
            if (!dead && !ControlsOwnMovement)
                knockback = Flat(velocity);
        }

        public void Launch(float height)
        {
            if (!dead && !IsBoss)
                verticalVelocity = Mathf.Sqrt(2f * -gravity * height);
        }

        public void Taunt() => aware = true;

        // Deactivation (e.g. the room is hidden) kills running coroutines: don't leave a warning
        // on the ground or come back stuck mid-attack
        protected override void OnDisable()
        {
            base.OnDisable();
            ClearWarnings();
            if (dead)
                return;
            busy = false;
            ClearFlash();
            OnInterrupted();
        }

        protected void HurtPlayer(float amount)
        {
            if (playerStats == null || JOT_Parry.TryParry(transform.position, this))
                return;
            playerStats.TakeDamage(amount, this);
        }

        // Ground warnings are tracked so an interrupted or dead enemy never leaves one behind
        protected GameObject Warn(GameObject telegraph)
        {
            warnings.Add(telegraph);
            return telegraph;
        }

        protected void ClearWarnings()
        {
            foreach (GameObject w in warnings)
                if (w != null)
                {
                    // A warning cleared as its blow lands: the blow's painted effect in its shape
                    // (an interrupted attack clears early, before its fill has run: nothing then)
                    if (w.TryGetComponent(out JOT_Warning warn) && warn.Landed)
                        ImpactFx(w.transform, warn);
                    Destroy(w);
                }
            warnings.Clear();
        }

        private static void ImpactFx(Transform at, JOT_Warning warn)
        {
            Color color = warn.tint;
            Vector3 ground = at.position;
            if (warn.IsRadial && warn.Arc >= 359f)
            {
                if (warn.Inner > 0.05f || warn.Outer >= 1.2f)
                    JOT_SheetFx.Roar(ground, warn.Outer, color, 0.5f);
                JOT_SheetFx.Hit(ground + Vector3.up * 0.5f, Mathf.Clamp(warn.Outer * 1.2f, 1.4f, 4f), color);
            }
            else if (warn.IsRadial)
            {
                // a sweep: a crescent across the slice, at about chest height
                JOT_SheetFx.Crescent(ground + at.forward * warn.Outer * 0.45f + Vector3.up * 0.8f, at.forward, warn.Outer * 1.7f, color, Random.Range(-20f, 20f));
            }
            else if (warn.Length > 0.1f)
            {
                JOT_SheetFx.Streak(ground + Vector3.up * 0.45f, ground + at.forward * warn.Length + Vector3.up * 0.45f, color, 0.35f);
            }
        }

        // ---- Moving like a pack, not a queue: closes in on a slight weave, keeps a little apart from the others,
        // and once in reach but not ready to strike, circles the player instead of standing still
        private float strafeSign = 1f, strafeFlipAt, weaveSeed = -1f;

        protected Vector3 Chase(float reach, float circleSpeed = 0.55f)
        {
            if (weaveSeed < 0f)
            {
                weaveSeed = Random.Range(0f, 100f);
                strafeSign = Random.value < 0.5f ? -1f : 1f;
            }
            if (Time.time >= strafeFlipAt)
            {
                strafeFlipAt = Time.time + Random.Range(1.6f, 3.2f);
                if (Random.value < 0.4f) strafeSign = -strafeSign;
            }
            Vector3 to = TowardPlayer;
            Vector3 side = Vector3.Cross(Vector3.up, to) * strafeSign;
            float distance = DistanceToPlayer;
            Vector3 v;
            float speed = moveSpeed;
            if (distance > reach)
                v = to + side * 0.3f * Mathf.Sin(Time.time * 1.4f + weaveSeed);
            else
            {
                v = side + to * (distance < reach * 0.6f ? -0.4f : 0.1f);   // circle, backing off a bit if too close
                speed *= circleSpeed;
            }
            v += Separation() * 0.9f;
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized * speed : Vector3.zero;
        }

        private Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            float mine = controller != null ? controller.radius : 0.5f;
            foreach (Enemy other in Enemy.Alive)
            {
                if (other == null || other == this || other.IsDead || other.IsBoss) continue;
                Vector3 away = Flat(transform.position - other.transform.position);
                float gap = mine + 0.9f;
                float d = away.magnitude;
                if (d < 0.01f || d > gap) continue;
                push += away / d * (1f - d / gap);
            }
            return push;
        }

        protected void Face(Vector3 point, float degreesPerSecond)
        {
            Vector3 to = Flat(point - transform.position);
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), degreesPerSecond * Time.deltaTime);
        }

        // Is the player inside an arc in front of this enemy?
        protected bool PlayerInArc(float range, float arcDegrees)
        {
            if (player == null)
                return false;
            Vector3 to = Flat(player.position - transform.position);
            return to.magnitude <= range && Vector3.Angle(transform.forward, to) <= arcDegrees / 2f;
        }

        private IEnumerator Flash()
        {
            FlashTint(hitTint);
            yield return new WaitForSeconds(0.12f);
            if (!dead)
                ClearFlash();
        }

        private void Die()
        {
            dead = true;
            busy = true;
            StopAllCoroutines();
            ClearWarnings();
            ClearFlash();
            OnInterrupted();
            controller.enabled = false;
            RaiseDied();
            StartCoroutine(Crumble());
        }

        // Temporary death: the stone body sinks and shrinks away
        private IEnumerator Crumble()
        {
            Vector3 start = visual != null ? visual.localScale : Vector3.one;
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                float k = t / 0.5f;
                if (visual != null)
                {
                    visual.localScale = start * (1f - k * 0.8f);
                    visual.localPosition = Vector3.down * k * 0.4f;
                }
                yield return null;
            }
            Destroy(gameObject);
        }

        protected virtual void OnDestroy() => ClearWarnings();

        protected static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
