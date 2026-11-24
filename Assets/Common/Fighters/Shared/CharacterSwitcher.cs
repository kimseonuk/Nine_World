using UnityEngine;
using UnityEngine.InputSystem;

// Swaps the player's character with the numeric keypad (Num 1, Num 2, ...).
// Only one character model is active; movement, swings and the camera follow it.
public class CharacterSwitcher : MonoBehaviour
{
    private const string SaveKey = "character_selected";

    [SerializeField] private CharacterProfile[] characters;

    private PlayerMovement movement;
    private PlayerMiner miner;
    private string toast;
    private float toastUntil;

    public CharacterProfile Current { get; private set; }
    public System.Collections.Generic.IReadOnlyList<CharacterProfile> Characters => characters;

    // Taking up a weapon at the village armory
    public void Equip(int index)
    {
        if (characters != null && index >= 0 && index < characters.Length && characters[index] != Current)
            Select(index, true);
    }

    private void Awake()
    {
        // A realm scene's own player (Jotunheim, Muspelheim) wakes for a moment before StageHost takes it out: it must not
        // write its own, shorter roster's index over the game's saved choice
        if (GameManager.Instance != null && GameManager.Instance.gameObject.scene != gameObject.scene)
        {
            enabled = false;
            return;
        }
        movement = GetComponent<PlayerMovement>();
        miner = GetComponent<PlayerMiner>();
        Select(Mathf.Clamp(PlayerPrefs.GetInt(SaveKey, 0), 0, Mathf.Max(0, characters.Length - 1)), false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !GameManager.IsFreeRoam || (miner != null && miner.IsSwinging))
            return;

        for (int i = 0; i < characters.Length && i < 9; i++)
            if (keyboard[Key.Numpad1 + i].wasPressedThisFrame && characters[i] != Current)
                Select(i, true);
    }

    private void Select(int index, bool announce)
    {
        if (characters == null || characters.Length == 0)
            return;

        for (int i = 0; i < characters.Length; i++)
            characters[i].gameObject.SetActive(i == index);
        Current = characters[index];
        PlayerPrefs.SetInt(SaveKey, index);
        PlayerPrefs.Save();   // written at once, so a stopped play session or editor restart keeps the choice

        if (movement != null) movement.SetAnimator(Current.Animator);
        if (miner != null) miner.SetCharacter(Current);
        if (TryGetComponent(out DualBlades blades)) blades.SetCharacter(Current);
        ArmoryWeapon.SetAll(gameObject, Current);   // the other realms' weapons (spear, axes, hammer)
        var cameraMode = FindAnyObjectByType<CameraModeController>();
        if (cameraMode != null) cameraMode.RefreshPlayerRenderers();

        if (announce)
        {
            toast = $"캐릭터 변경: {Current.DisplayName}";
            toastUntil = Time.unscaledTime + 2f;
        }
    }

    private void OnGUI()
    {
        if (Time.unscaledTime > toastUntil || string.IsNullOrEmpty(toast) || OpeningCutscene.Playing || StoryStage.Cinematic)
            return;
        Rect r = new Rect((Screen.width - 280f) / 2f, Screen.height * 0.3f, 280f, 36f);
        UiFont.Fill(r, new Color(0f, 0f, 0f, 0.7f));
        GUI.Label(r, toast, UiFont.Style(16, TextAnchor.MiddleCenter, FontStyle.Bold));
    }
}
