using UnityEngine;
using UnityEngine.InputSystem;

namespace NineWorlds.Jotunheim
{
    // Lets the player parry with the right mouse button: they turn toward the cursor and guard for a moment.
    // A hit that arrives from the front during that moment does no damage; the attacker, if close, is staggered
    // (the guardian is left groggy), and a creeping vine is cut. Falling things (trees, dropped weapons, debris) can't be parried.
    // Jotunheim's own attacks ask TryParry before they hurt the player; the shared player code is untouched.
    [RequireComponent(typeof(PlayerStats))]
    public class JOT_Parry : MonoBehaviour
    {
        [SerializeField] private float window = 0.35f;        // how long the guard is up
        [SerializeField] private float cooldown = 1.0f;
        [SerializeField] private float arc = 150f;            // degrees in front that the guard covers
        [SerializeField] private float staggerReach = 6f;     // attackers this close are knocked off balance
        [SerializeField] private float staggerSeconds = 0.8f;
        [SerializeField] private float bossGroggySeconds = 1.5f; // a parried guardian is left properly dazed (and takes double damage)

        private static readonly Color GuardColor = new Color(1f, 0.85f, 0.4f);
        private static JOT_Parry active;

        private PlayerStats stats;
        private float guardUntil;
        private float readyAt;
        private Transform guardFx;

        public bool Guarding => Time.time < guardUntil;

        // Called by Jotunheim's attacks just before they would hurt the player: true means parried (no damage)
        public static bool TryParry(Vector3 from, Enemy attacker)
        {
            // Mid-roll the hit simply misses (no stagger, no parry effects)
            if (JOT_PlayerControlPlus.Invulnerable || JOT_SkillSet.Untouchable)
                return true;
            if (active == null || !active.Guarding || active.stats.IsDead)
                return false;
            Vector3 to = from - active.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f && Vector3.Angle(active.transform.forward, to) > active.arc / 2f)
                return false;
            active.Parried(attacker);
            return true;
        }

        private void Awake() => stats = GetComponent<PlayerStats>();
        private void OnEnable() => active = this;

        private void OnDisable()
        {
            if (active == this)
                active = null;
            if (guardFx != null)
                guardFx.gameObject.SetActive(false);
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (SkillSystem.RightPressed(mouse) && Time.time >= readyAt && !stats.IsDead && Time.timeScale > 0f)
            {
                FaceCursor(mouse);
                guardUntil = Time.time + window;
                readyAt = Time.time + cooldown;
            }

            if (guardFx == null)
                guardFx = Mats.Shape(PrimitiveType.Cylinder, transform, Vector3.zero, Vector3.one,
                    Mats.Get("JOT_fx_guard", GuardColor, 0.8f, GuardColor * 2f), false, "JOT_ParryGuard").transform;
            guardFx.gameObject.SetActive(Guarding);
            if (Guarding)
            {
                // A bright shield disc standing in front of the player
                guardFx.localPosition = new Vector3(0f, 0.1f, 0.8f);
                guardFx.localRotation = Quaternion.Euler(90f, 0f, 0f);
                float pulse = 1f + Mathf.Sin(Time.time * 40f) * 0.05f;
                guardFx.localScale = new Vector3(1.4f * pulse, 0.04f, 1.4f * pulse);
            }
        }

        // Turn toward the point on the ground under the cursor, the same way a click aims an attack
        private void FaceCursor(Mouse mouse)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            var ground = new Plane(Vector3.up, new Vector3(0f, transform.position.y - 1f, 0f));
            if (!ground.Raycast(ray, out float d))
                return;
            Vector3 to = ray.GetPoint(d) - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(to);
        }

        private void Parried(Enemy attacker)
        {
            guardUntil = 0f;
            readyAt = Time.time + 0.2f; // a clean parry lets you guard again almost at once
            Vector3 front = transform.position + transform.forward * 0.8f;
            JOT_PlayerFeedback.Popup(transform.position + Vector3.up * 1.3f, "패링!", GuardColor, 1.3f);
            Shockwave.Spawn(front, 1.4f, GuardColor, 14);
            CameraShake.Shake(0.12f, 0.12f);

            if (attacker == null || attacker.IsDead)
                return;
            if (attacker is JOT_Vine)
            {
                attacker.TakeHit(transform.position, 1); // the vine is cut
                return;
            }
            Vector3 offset = attacker.transform.position - transform.position;
            offset.y = 0f;
            var body = attacker.GetComponent<CharacterController>();
            if (offset.magnitude > staggerReach + (body != null ? body.radius : 0f))
                return;
            if (attacker is JOT_LastGuardian guardian)
            {
                guardian.Groggy(bossGroggySeconds);
                JOT_PlayerFeedback.Popup(attacker.transform.position + Vector3.up * (body != null ? body.height * 0.8f : 3f), "그로기!", GuardColor, 1.4f);
            }
            else
                attacker.Knockdown(staggerSeconds);
        }
    }
}
