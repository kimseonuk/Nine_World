using UnityEngine;
using UnityEngine.InputSystem;

// Left-click a rock in reach to dig it out; any other click swings an attack.
// The greatsword's 차징 강타 is on the right button: hold to charge, let go to slam.
public class ClickToDig : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField] private PlayerMiner miner;
    [SerializeField] private float maxRayDistance = 200f;

    private bool holding; // the right button is down for a charge
    private bool armoryHolding; // the left button is down for an armory weapon
    private DualBlades blades;

    // Where the cursor points on the ground, for weapons that aim their own skills
    public Vector3 CursorGroundPoint()
    {
        Mouse mouse = Mouse.current;
        return mouse != null && viewCamera != null && miner != null ? AimPoint(mouse) : transform.position;
    }

    private void Awake()
    {
        if (viewCamera == null)
            viewCamera = Camera.main;
        if (miner == null)
            miner = FindAnyObjectByType<PlayerMiner>();
        if (miner != null)
            blades = miner.GetComponent<DualBlades>();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || viewCamera == null || miner == null)
            return;

        // A weapon from the armory (another realm's) takes the button its own way: press, hold, let go
        ArmoryWeapon armory = ArmoryWeapon.Current;
        if (armory != null && !armory.IsActive)
            armory = null;
        if (armoryHolding)
        {
            if (!mouse.leftButton.isPressed || !GameManager.IsRunActive || armory == null)
            {
                armoryHolding = false;
                if (armory != null)
                    armory.Release(AimPoint(mouse));
            }
            else
                armory.Hold(AimPoint(mouse));
            return;
        }

        bool greatsword = miner.CanCharge && (blades == null || !blades.IsActive) && armory == null;
        if (holding)
        {
            // Let go (or a menu took over): the charge comes down, otherwise it keeps building toward the cursor
            if (!SkillSystem.RightHeld(mouse) || !GameManager.IsRunActive || !greatsword)
            {
                holding = false;
                miner.ReleaseCharge(AimPoint(mouse));
            }
            else
            {
                miner.HoldCharge(AimPoint(mouse));
                // 돌진: Space dashes while the charge builds
                Keyboard keyboard = Keyboard.current;
                if (SkillSystem.DodgeDown(keyboard))
                    miner.Rush(AimPoint(mouse));
            }
        }
        else if (greatsword && SkillSystem.RightPressed(mouse) && GameManager.IsRunActive)
        {
            holding = true;
            miner.HoldCharge(AimPoint(mouse));
        }

        if (!mouse.leftButton.wasPressedThisFrame || !GameManager.IsRunActive)
            return;

        // A rock in reach is mined; any other click is an attack toward that spot,
        // hitting whatever enemy is in front when the blade lands
        if (Physics.Raycast(ScreenRay(mouse), out RaycastHit hit, maxRayDistance))
        {
            CaveBlock block = hit.collider.GetComponent<CaveBlock>();
            bool mineable = block != null && !block.Indestructible && hit.collider.GetComponentInParent<Enemy>() == null;
            if (mineable && miner.CanReach(block))
            {
                miner.SwingAt(block);
                return;
            }
        }

        if (armory != null)
        {
            armory.Press(AimPoint(mouse));
            armoryHolding = true;
            return;
        }

        // The twin blades take their own attack (a fast three-blow combo)
        if (blades != null && blades.IsActive)
        {
            blades.Tap(AimPoint(mouse));
            return;
        }
        miner.PressAttack(AimPoint(mouse));
    }

    // With a locked cursor (first person) aim through the crosshair at the screen center
    private Ray ScreenRay(Mouse mouse)
    {
        Vector2 screenPoint = Cursor.lockState == CursorLockMode.Locked
            ? new Vector2(Screen.width / 2f, Screen.height / 2f)
            : mouse.position.ReadValue();
        return viewCamera.ScreenPointToRay(screenPoint);
    }

    // Where the cursor points on the ground (straight ahead in first person or when aiming at the sky)
    private Vector3 AimPoint(Mouse mouse)
    {
        Transform body = miner.transform;
        if (Cursor.lockState == CursorLockMode.Locked)
            return body.position + body.forward * 3f;
        Ray ray = ScreenRay(mouse);
        var ground = new Plane(Vector3.up, new Vector3(0f, body.position.y - 1f, 0f));
        return ground.Raycast(ray, out float distance) ? ray.GetPoint(distance) : body.position + body.forward;
    }
}
