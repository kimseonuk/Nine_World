using System;
using System.Collections.Generic;
using UnityEngine;

// Anything the player can hit and kill: slimes, draugr, giants and the realm bosses.
public abstract class Enemy : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public static readonly List<Enemy> Alive = new List<Enemy>();
    public static event Action<Enemy> AnyDied;

    [SerializeField] private string displayName = "적";
    [SerializeField] private int goldDrop = 2;
    [SerializeField] private int xpDrop = 3;

    private Renderer[] tintRenderers;
    private MaterialPropertyBlock tintBlock;
    private Color baseTint = Color.white;

    public string DisplayName { get => displayName; set => displayName = value; }
    public int GoldDrop { get => goldDrop; set => goldDrop = value; }
    public int XpDrop { get => xpDrop; set => xpDrop = value; }
    public bool IsBoss { get; set; }
    public Room HomeRoom { get; set; }        // the zone it was spawned in
    public Color BaseTint => baseTint;
    public abstract int Health { get; }
    public abstract int MaxHealth { get; }
    public bool IsDead => Health <= 0;

    public event Action<Enemy> Died;

    // 주술 강화 (a seidr mage's buff): everything it deals is multiplied for a while
    private float empoweredUntil;
    private float empoweredScale = 1f;
    public bool Empowered => Time.time < empoweredUntil;
    public float OutgoingDamageMultiplier => (Empowered ? empoweredScale : 1f) * PhaseDamageMultiplier;

    // A boss's second phase (BossAwakening): it hits harder and shrugs off part of every blow
    public float PhaseDamageMultiplier { get; set; } = 1f;
    public float IncomingDamageMultiplier { get; set; } = 1f;
    protected int Mitigate(int damage) =>
        Mathf.Approximately(IncomingDamageMultiplier, 1f) ? damage : Mathf.Max(1, Mathf.RoundToInt(damage * IncomingDamageMultiplier));

    public void Empower(float factor, float seconds)
    {
        if (IsDead)
            return;
        empoweredScale = factor;
        empoweredUntil = Time.time + seconds;
        EmpowerMark.Attach(this);
    }

    public abstract void TakeHit(Vector3 from, int damage);

    // Knocked off its feet (full-charge shockwave): no moving or attacking for a while
    public abstract void Knockdown(float seconds);

    // Its attack in progress is broken off (단절)
    public virtual void Interrupt() { }

    // Realm/floor scaling, applied right after spawning
    public abstract void Scale(float healthMultiplier, float damageMultiplier);

    protected virtual void OnEnable()
    {
        Alive.Add(this);
        // every monster, the ones others split off or summon too, stays where the player can reach it
        if (GetComponent<ZoneLeash>() == null)
            gameObject.AddComponent<ZoneLeash>();
        HitFeel.Attach(this);
        FeetOnFloor.Attach(gameObject);   // (its feet on the floor, not hovering its controller's skin width above it)
        CrowdSpacing.Attach(this);        // (spread out over the hall, not piled into one lump)
        EnemyVoice.Attach(this); // its voice: humanoid, beast, bird, giant, machine or rune   // 타격감: flash, sparks, numbers and sound on every blow it takes
    }
    protected virtual void OnDisable() => Alive.Remove(this);

    protected void RaiseDied()
    {
        Alive.Remove(this);
        Died?.Invoke(this);
        AnyDied?.Invoke(this);
        if (goldDrop > 0)
            Pickup.SpawnGold(transform.position + Vector3.up * 0.5f, goldDrop);
        if (xpDrop > 0)
            Pickup.SpawnXp(transform.position + Vector3.up * 0.8f, xpDrop);
    }

    // A colour multiplied over the model's own texture (realm variants: frost, fire, hel)
    public void SetBaseTint(Color color)
    {
        baseTint = color;
        SetTint(color);
    }

    protected void FlashTint(Color color) => SetTint(color);

    // A blow landing: the body flashes white for an instant (real time, so a hit-stop doesn't hold it)
    private float hitFlashUntil;
    private Coroutine hitFlash;
    public void HitFlash(float seconds)
    {
        if (!isActiveAndEnabled || IsDead) return;
        hitFlashUntil = Time.unscaledTime + seconds;
        SetTint(new Color(3.2f, 3f, 2.7f));
        if (hitFlash == null) hitFlash = StartCoroutine(EndHitFlash());
    }

    // The windup's warning: a red flush over the body while it gathers (a blow landing on it takes over)
    public void WarnFlash(float seconds)
    {
        if (!isActiveAndEnabled || IsDead || hitFlash != null) return;
        hitFlashUntil = Time.unscaledTime + seconds;
        SetTint(new Color(2.4f, 0.55f, 0.45f));
        hitFlash = StartCoroutine(EndHitFlash());
    }

    private System.Collections.IEnumerator EndHitFlash()
    {
        while (Time.unscaledTime < hitFlashUntil) yield return null;
        SetTint(baseTint);
        hitFlash = null;
    }
    protected void ClearFlash() => SetTint(baseTint);

    private void SetTint(Color color)
    {
        // The body's meshes only: glows, trails and sparks carried on it keep their own colours
        tintRenderers ??= Array.FindAll(GetComponentsInChildren<Renderer>(),
            r => (r is SkinnedMeshRenderer || r is MeshRenderer) && !r.name.StartsWith("Vfx") && !r.name.StartsWith("Sprite_"));
        tintBlock ??= new MaterialPropertyBlock();
        foreach (Renderer r in tintRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(tintBlock);
            tintBlock.SetColor(BaseColorId, color);
            r.SetPropertyBlock(tintBlock);
        }
    }

    protected static float FlatDistance(Vector3 a, Vector3 b)
    {
        return new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
