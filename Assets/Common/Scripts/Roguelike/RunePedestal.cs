using UnityEngine;

// A rune stone on a pedestal: free in treasure rooms and after bosses, bought with gold in the shop.
// The player walks up to it and presses F.
public class RunePedestal : MonoBehaviour
{
    private const float Reach = 1.8f;

    private Transform gem;
    private GameObject halo;
    private float phase;
    private AudioSource hum;

    public Rune Rune { get; private set; }
    public int Price { get; private set; }
    public bool Taken { get; private set; }

    public static RunePedestal Create(Transform parent, Vector3 localPosition, Rune rune, int price)
    {
        var root = new GameObject($"Rune_{rune.id}");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        // A marble plinth banded in gold, the rune floating above it in a halo of its god's colour
        Material marble = Mats.Get("pedestal_marble", new Color(0.9f, 0.9f, 0.93f), 0.6f);
        Material gold = Mats.Get("pedestal_gold", new Color(1f, 0.78f, 0.35f), 0.75f, new Color(0.35f, 0.25f, 0.05f));
        Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(1.15f, 0.08f, 1.15f), gold);
        Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.8f, 0.42f, 0.8f), marble, true);
        Mats.Shape(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.95f, 0f), new Vector3(1f, 0.05f, 1f), gold);
        Color c = RuneLibrary.GodColors[(int)rune.god];
        Material glow = Mats.Get("rune_" + rune.god, c, 0.7f, c * 1.6f);
        GameObject gem = Mats.Shape(PrimitiveType.Cube, root.transform, new Vector3(0f, 1.45f, 0f), new Vector3(0.35f, 0.5f, 0.12f), glow);
        GameObject halo = Vfx.Glow(root.transform, new Vector3(0f, 1.45f, 0f), 1.3f, c * 0.8f);

        var pedestal = root.AddComponent<RunePedestal>();
        pedestal.Rune = rune;
        pedestal.Price = price;
        pedestal.gem = gem.transform;
        pedestal.halo = halo;
        pedestal.phase = Random.Range(0f, 10f);
        // the stone hums softly while it waits
        pedestal.hum = GameAudio.Loop(gem, "pedestal_hum", 0.35f, 7f);
        return pedestal;
    }

    public bool InReach(Vector3 playerPosition)
    {
        Vector3 d = playerPosition - transform.position;
        d.y = 0f;
        return !Taken && d.magnitude <= Reach;
    }

    public bool CanAfford => Price <= 0 || RunState.Gold >= Price;

    public bool TryTake()
    {
        if (Taken || (Price > 0 && !RunState.SpendGold(Price)))
            return false;
        Taken = true;
        GameAudio.Play("reveal_sparkle", transform.position, 0.8f);
        RunState.AddRune(Rune);
        Destroy(gem.gameObject);
        if (halo != null)
            Destroy(halo);
        return true;
    }

    private void Update()
    {
        if (gem == null)
            return;
        phase += Time.deltaTime;
        gem.localPosition = new Vector3(0f, 1.45f + Mathf.Sin(phase * 2f) * 0.1f, 0f);
        gem.localRotation = Quaternion.Euler(0f, phase * 60f, 0f);
    }
}
