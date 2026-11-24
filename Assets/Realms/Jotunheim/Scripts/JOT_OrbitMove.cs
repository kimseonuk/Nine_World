using UnityEngine;
using UnityEngine.InputSystem;

namespace NineWorlds.Jotunheim
{
    // While the camera swings round a giant fixed in the middle of the stage (JOT_CameraRig.Orbit), WASD follows the
    // screen instead of the world: W toward the giant, S away, A/D round it. The shared PlayerMovement (shared code, not
    // changed) stays on and keeps doing everything else — gravity, the animator, and above all being switched off by
    // skills, cutscenes and menus to hold the player — and this, running right after it each frame, takes back the
    // world-axis step it made and makes the screen-axis step instead. When PlayerMovement is off, this does nothing.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CharacterController))]
    public class JOT_OrbitMove : MonoBehaviour
    {
        private PlayerMovement movement;
        private CharacterController controller;
        private PlayerMiner miner;
        private Transform view;
        private float walkSpeed = 2.5f, runSpeed = 5f, rotationSpeed = 720f;
        private Quaternion lastRotation;
        private bool settled;

        [System.Serializable]
        private class MovementSettings { public float walkSpeed = 2.5f, runSpeed = 5f, rotationSpeed = 720f; }

        public static void Begin(GameObject player)
        {
            if (player == null || player.GetComponent<JOT_OrbitMove>() != null) return;
            player.AddComponent<JOT_OrbitMove>();
        }

        public static void End(GameObject player)
        {
            if (player == null) return;
            JOT_OrbitMove m = player.GetComponent<JOT_OrbitMove>();
            if (m != null) Destroy(m);
        }

        private void Awake()
        {
            movement = GetComponent<PlayerMovement>();
            controller = GetComponent<CharacterController>();
            miner = GetComponent<PlayerMiner>();
            // The shared movement's own speeds (its serialized fields, read through Unity's serializer)
            if (movement != null)
            {
                var s = JsonUtility.FromJson<MovementSettings>(JsonUtility.ToJson(movement));
                if (s != null) { walkSpeed = s.walkSpeed; runSpeed = s.runSpeed; rotationSpeed = s.rotationSpeed; }
            }
            lastRotation = transform.rotation;
        }

        private void Update()
        {
            if (!settled || movement == null || !movement.enabled || !controller.enabled)
            {
                settled = true;
                return;
            }
            Camera cam = Camera.main;
            view = cam != null ? cam.transform : null;
            if (view == null || movement.IsDashing) return;

            // The step the shared movement just took (same rules as it: held, speed), and the same step along the
            // screen instead (W toward where the camera looks). Only the difference is applied: whatever else moved
            // the player this frame (shoves, skills, teleports) stays.
            ArmoryWeapon armory = ArmoryWeapon.Current != null && ArmoryWeapon.Current.IsActive ? ArmoryWeapon.Current : null;
            bool whirling = miner != null && miner.Whirling;
            bool held = (miner != null && miner.IsSwinging && !whirling) || (armory != null && armory.Rooted) || !GameManager.IsFreeRoam;
            Vector2 input = held ? Vector2.zero : ReadWasd();
            Vector3 world = new Vector3(input.x, 0f, input.y);
            if (world.sqrMagnitude > 1f) world.Normalize();
            if (world.sqrMagnitude < 0.0001f) return;
            Vector3 forward = view.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) return;
            Vector3 move = Quaternion.LookRotation(forward.normalized) * world;

            Keyboard kb = Keyboard.current;
            bool running = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            float speed = (running ? runSpeed : walkSpeed) * RunState.Stats.moveSpeedMultiplier * MetaProgress.MoveSpeedMultiplier
                          * DebugCheats.MoveSpeedMultiplier * PlayerEffects.Slow * PlayerEffects.Bind
                          * (whirling ? miner.WhirlwindMoveSpeed : 1f) * (armory != null ? armory.MoveMultiplier : 1f);
            controller.Move((move - world) * speed * Time.deltaTime);
            // (turn from where it faced last frame: the shared movement just turned it toward the world-axis step)
            transform.rotation = Quaternion.RotateTowards(lastRotation, Quaternion.LookRotation(move, Vector3.up), rotationSpeed * Time.deltaTime);
        }

        // How it faced at the end of the frame: the turn starts from here next frame
        private void LateUpdate() => lastRotation = transform.rotation;

        private static Vector2 ReadWasd()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            float x = 0f, y = 0f;
            if (kb.aKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed) x += 1f;
            if (kb.sKey.isPressed) y -= 1f;
            if (kb.wKey.isPressed) y += 1f;
            return new Vector2(x, y);
        }
    }
}
