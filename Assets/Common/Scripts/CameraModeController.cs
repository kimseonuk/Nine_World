using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// Switches the main camera between third person (follow camera) and
// first person (mouse look from the player's eyes). Press V to toggle.
[RequireComponent(typeof(Camera))]
public class CameraModeController : MonoBehaviour
{
    public static bool FirstPersonActive { get; private set; }

    [SerializeField] private Transform player;
    [SerializeField] private CameraFollow thirdPersonFollow;
    [SerializeField] private bool startInFirstPerson;

    [Header("First person")]
    [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 0.65f, 0.15f); // from the player's center
    [SerializeField] private float mouseSensitivity = 0.12f;                   // degrees per pixel
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;
    [SerializeField] private float firstPersonNearClip = 0.05f;

    private Camera cam;
    private Renderer[] playerRenderers;
    private float thirdPersonNearClip;
    private float pitch;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (thirdPersonFollow == null)
            thirdPersonFollow = GetComponent<CameraFollow>();
        if (player == null)
        {
            PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
            if (movement != null)
                player = movement.transform;
        }
        // Record the near clip before anything calls SetFirstPerson, or it would be overwritten with 0
        thirdPersonNearClip = cam.nearClipPlane;
        RefreshPlayerRenderers();
    }

    // Every character model under the player (including inactive ones), so first person hides whichever is shown
    public void RefreshPlayerRenderers()
    {
        playerRenderers = player != null ? player.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        if (cam != null)
            SetFirstPerson(FirstPersonActive);
    }

    private void Start()
    {
        SetFirstPerson(startInFirstPerson);
    }

    private void OnDisable()
    {
        FirstPersonActive = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        // Menus (inventory, results, shop) own the cursor: no view switching or mouse look
        if (!GameManager.IsFreeRoam)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.vKey.wasPressedThisFrame)
            SetFirstPerson(!FirstPersonActive);

        if (!FirstPersonActive || player == null)
            return;

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        // Yaw turns the whole player, pitch only tilts the camera
        Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
        player.Rotate(0f, delta.x, 0f, Space.World);
        pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
    }

    private void LateUpdate()
    {
        if (!FirstPersonActive || player == null)
            return;

        transform.position = player.TransformPoint(eyeOffset);
        transform.rotation = Quaternion.Euler(pitch, player.eulerAngles.y, 0f);
    }

    public void SetFirstPerson(bool firstPerson)
    {
        FirstPersonActive = firstPerson;
        pitch = 0f;

        if (thirdPersonFollow != null)
            thirdPersonFollow.enabled = !firstPerson;
        cam.nearClipPlane = firstPerson ? firstPersonNearClip : thirdPersonNearClip;

        // Keep the body's shadow but don't render it into our own eyes
        foreach (Renderer r in playerRenderers)
            r.shadowCastingMode = firstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

        bool lockCursor = firstPerson && GameManager.IsFreeRoam;
        Cursor.lockState = lockCursor ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !lockCursor;
    }

    private void OnGUI()
    {
        if (OpeningCutscene.Playing || StoryStage.Cinematic)
            return;
        GUI.Label(new Rect(10f, 10f, 300f, 24f), FirstPersonActive ? "[V] 1st person" : "[V] 3rd person");

        if (FirstPersonActive)
        {
            // Crosshair: clicks aim at the screen center in first person
            Vector2 c = new Vector2(Screen.width / 2f, Screen.height / 2f);
            GUI.Box(new Rect(c.x - 1f, c.y - 8f, 2f, 16f), GUIContent.none);
            GUI.Box(new Rect(c.x - 8f, c.y - 1f, 16f, 2f), GUIContent.none);
        }
    }
}
