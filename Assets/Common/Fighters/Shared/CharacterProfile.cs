using UnityEngine;

// One playable character's model (a child of the Player) and how its swings are timed.
public class CharacterProfile : MonoBehaviour
{
    // (Spear: Niflheim's 창지기 · Greataxe, TwinAxes: Jotunheim's 늑대 부족 야만전사 · Hammer: Muspelheim's 묠니르 — see ArmoryWeapon)
    // (numbered as saved: 0 was the dwarf miner's pickaxe, removed)
    // (Bow: Niflheim's 울르의 활, the second Niflheim weapon - see UllrBow)
    public enum WeaponKind { Greatsword = 1, DualBlades = 2, Spear = 3, Greataxe = 4, TwinAxes = 5, Hammer = 6, Gauntlets = 7, Bow = 8 }

    [SerializeField] private string displayName = "캐릭터";
    [SerializeField] private Animator animator;

    [Header("Weapon (as the village armory shows it)")]
    [SerializeField] private WeaponKind weapon = WeaponKind.Greatsword;
    [SerializeField] private string weaponName = "대검";
    [SerializeField, TextArea] private string weaponDescription = "";
    [SerializeField] private GameObject heldWeapon;   // the weapon model in hand (hidden while it is thrown)

    [Header("Mining (blocks)")]
    [SerializeField] private float impactDelay = 0.73f;
    [SerializeField] private float swingDuration = 0.93f;

    [Header("Attacking (monsters)")]
    [SerializeField] private float attackImpactDelay = 0.7f;
    [SerializeField] private float attackDuration = 1.0f;

    [Header("Charged attack (hold the button; uses the mining swing as a raise-and-slam)")]
    [SerializeField] private bool chargeAttack;
    [SerializeField, Range(0f, 1f)] private float chargeHoldPoint = 0.58f;   // swing progress where the weapon is raised and held
    [SerializeField, Range(0f, 1f)] private float chargeImpactPoint = 0.78f; // swing progress where the blow lands
    [SerializeField] private float swingStateSeconds = 0.93f;                // the swing state's length at normal speed

    public string DisplayName => displayName;
    public WeaponKind Weapon => weapon;
    public string WeaponName => weaponName;
    public string WeaponDescription => weaponDescription;
    public GameObject HeldWeapon => heldWeapon;
    public bool ChargeAttack => chargeAttack;
    public float ChargeHoldPoint => chargeHoldPoint;
    public float ChargeImpactPoint => chargeImpactPoint;
    public float SwingStateSeconds => swingStateSeconds;
    public Animator Animator => animator != null ? animator : animator = GetComponentInChildren<Animator>(true);
    public float ImpactDelay => impactDelay;
    public float SwingDuration => swingDuration;
    public float AttackImpactDelay => attackImpactDelay;
    public float AttackDuration => attackDuration;
}
