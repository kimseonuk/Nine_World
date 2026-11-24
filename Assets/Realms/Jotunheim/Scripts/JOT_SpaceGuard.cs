using UnityEngine;
using UnityEngine.InputSystem;

namespace NineWorlds.Jotunheim
{
    // On its own (no armory weapon to tell it Space is taken), the shared PlayerMovement dashes on every Space press —
    // on top of the axe's own Space skill, even while that is cooling down. The shared code reads the key itself, so on
    // the frame Space goes down its movement sits that one frame out (before it runs), and is handed back after.
    // Added by JOT_SkillSet when its weapon has a Space skill.
    [DefaultExecutionOrder(-200)]
    public class JOT_SpaceGuard : MonoBehaviour
    {
        private PlayerMovement movement;
        private JOT_SkillSet skills;
        private bool held;

        // This frame's switch-off is the guard's own (the controls are not really taken)
        public bool Holding => held;

        public static void AddTo(JOT_SkillSet owner)
        {
            if (StageHost.Hosted || owner.GetComponent<JOT_SpaceGuard>() != null) return;
            var g = owner.gameObject.AddComponent<JOT_SpaceGuard>();
            g.skills = owner;
        }

        private void Awake() => movement = GetComponent<PlayerMovement>();

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || movement == null || skills == null || !skills.isActiveAndEnabled) return;
            if (kb.spaceKey.wasPressedThisFrame && movement.enabled)
            {
                movement.enabled = false;
                held = true;
            }
        }

        private void LateUpdate()
        {
            if (!held) return;
            held = false;
            // (unless the Space skill itself has just taken the controls)
            if (!skills.Acting && movement != null)
                movement.enabled = true;
        }
    }
}
