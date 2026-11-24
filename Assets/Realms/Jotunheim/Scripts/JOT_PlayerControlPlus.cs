using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NineWorlds.Jotunheim
{
    // Extra handling on top of the shared player controls, without touching them:
    // - standing still, the hero turns to face the mouse cursor (attacks and parries already swing toward it)
    // - Space rolls a few metres in the move direction (or facing), dodging Jotunheim attacks while rolling
    [RequireComponent(typeof(CharacterController))]
    public class JOT_PlayerControlPlus : MonoBehaviour
    {
        [SerializeField] private float aimTurnSpeed = 900f;     // degrees per second when turning to the cursor
        [SerializeField] private float rollDistance = 4.2f;
        [SerializeField] private float rollSeconds = 0.42f;
        [SerializeField] private float rollCooldown = 0.55f;
        [SerializeField] private float invulnerableSeconds = 0.36f;

        private static readonly int RollState = Animator.StringToHash("Roll");

        private CharacterController controller;
        private PlayerMovement movement;
        private PlayerMiner miner;
        private PlayerStats stats;
        private float readyAt;
        private float invulnerableUntil;

        public static bool IsRolling { get; private set; }
        // Jotunheim's attacks treat a roll like a dodge (see JOT_Parry.TryParry)
        public static bool Invulnerable => instance != null && Time.time < instance.invulnerableUntil;

        private static JOT_PlayerControlPlus instance;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            movement = GetComponent<PlayerMovement>();
            miner = GetComponent<PlayerMiner>();
            stats = GetComponent<PlayerStats>();
        }

        private void OnEnable() => instance = this;

        private void OnDisable()
        {
            if (instance == this)
                instance = null;
            IsRolling = false;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || IsRolling || Time.time < readyAt || !GameManager.IsFreeRoam || Time.timeScale <= 0f)
                return;
            if (stats != null && stats.IsDead)
                return;
            if (keyboard.spaceKey.wasPressedThisFrame && (miner == null || !miner.IsSwinging))
                StartCoroutine(Roll(MoveInput(keyboard)));
        }

        // After the shared movement has turned the hero toward the walk direction: when standing still, face the cursor
        private void LateUpdate()
        {
            if (IsRolling || (miner != null && miner.IsSwinging) || CameraModeController.FirstPersonActive || !GameManager.IsFreeRoam)
                return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || MoveInput(keyboard).sqrMagnitude > 0.01f)
                return;
            if (!CursorPoint(out Vector3 point))
                return;
            Vector3 to = point - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.25f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), aimTurnSpeed * Time.deltaTime);
        }

        private IEnumerator Roll(Vector3 direction)
        {
            IsRolling = true;
            if (direction.sqrMagnitude < 0.01f)
                direction = transform.forward;
            direction.y = 0f;
            direction.Normalize();
            transform.rotation = Quaternion.LookRotation(direction);
            invulnerableUntil = Time.time + invulnerableSeconds;
            if (movement != null)
                movement.enabled = false;
            Animator animator = ActiveAnimator();
            if (animator != null && animator.HasState(0, RollState))
                animator.CrossFadeInFixedTime(RollState, 0.05f);

            // Fast start, soft landing
            float t = 0f, travelled = 0f;
            while (t < rollSeconds)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / rollSeconds), 2f);
                float step = k * rollDistance - travelled;
                travelled += step;
                controller.Move(direction * step + Vector3.down * 2f * Time.deltaTime);
                yield return null;
            }

            if (movement != null)
                movement.enabled = true;
            IsRolling = false;
            readyAt = Time.time + rollCooldown;
        }

        private Animator ActiveAnimator()
        {
            foreach (Animator a in GetComponentsInChildren<Animator>())
                if (a.isActiveAndEnabled)
                    return a;
            return null;
        }

        // WASD in world axes, as the shared movement reads it in third person
        private static Vector3 MoveInput(Keyboard keyboard)
        {
            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            return new Vector3(x, 0f, z);
        }

        private bool CursorPoint(out Vector3 point)
        {
            point = Vector3.zero;
            Mouse mouse = Mouse.current;
            Camera view = Camera.main;
            if (mouse == null || view == null || Cursor.lockState == CursorLockMode.Locked)
                return false;
            Ray ray = view.ScreenPointToRay(mouse.position.ReadValue());
            var ground = new Plane(Vector3.up, transform.position);
            if (!ground.Raycast(ray, out float distance))
                return false;
            point = ray.GetPoint(distance);
            return true;
        }
    }
}
