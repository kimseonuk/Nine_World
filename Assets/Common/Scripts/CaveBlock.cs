using UnityEngine;

// A diggable rock (gold ore in the dungeon rooms). Takes a few hits (fewer with more power).
public class CaveBlock : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] private bool indestructible;
    [SerializeField] private int maxHealth = 2;

    private int damageTaken;
    private MaterialPropertyBlock propertyBlock;

    public bool Indestructible
    {
        get => indestructible;
        set => indestructible = value;
    }

    public int MaxHealth
    {
        get => maxHealth;
        set => maxHealth = Mathf.Max(1, value);
    }

    public void Hit(int damage)
    {
        if (indestructible)
            return;

        damageTaken += Mathf.Max(1, damage);
        if (damageTaken >= maxHealth)
        {
            Dig();
            return;
        }

        // Darken the block as it cracks
        var r = GetComponent<Renderer>();
        if (r == null) return;
        propertyBlock ??= new MaterialPropertyBlock();
        float shade = 1f - 0.35f * damageTaken / maxHealth;
        Color baseColor = r.sharedMaterial.GetColor(BaseColorId);
        propertyBlock.SetColor(BaseColorId, new Color(baseColor.r * shade, baseColor.g * shade, baseColor.b * shade, baseColor.a));
        r.SetPropertyBlock(propertyBlock);
    }

    // Gold spilled when the block breaks (gold ore)
    public int GoldValue { get; set; }

    public void Dig()
    {
        if (indestructible)
            return;
        if (GoldValue > 0)
            Pickup.SpawnGold(transform.position, GoldValue);
        Destroy(gameObject);
    }
}
