using UnityEngine;

// The upgrade room's moving pieces: a blade hovering in its ring, the broken stone ring turning overhead,
// and the beam of light that breathes between them
public class ForgeFloat : MonoBehaviour
{
    private float bob, bobSpeed, spin, phase;
    public Vector3 axis = Vector3.up;   // what it turns round (its own)
    private bool pulse;
    private Vector3 home, baseScale;

    public static ForgeFloat Add(GameObject go, float bob, float bobSpeed, float spin, bool pulse = false)
    {
        var f = go.AddComponent<ForgeFloat>();
        f.bob = bob;
        f.bobSpeed = bobSpeed;
        f.spin = spin;
        f.pulse = pulse;
        return f;
    }

    private void Start()
    {
        home = transform.localPosition;
        baseScale = transform.localScale;
        phase = Random.value * 10f;
    }

    private void Update()
    {
        float time = Time.time + phase;
        if (bob > 0f)
            transform.localPosition = home + Vector3.up * Mathf.Sin(time * bobSpeed) * bob;
        if (spin != 0f)
            transform.Rotate(axis, spin * Time.deltaTime, Space.Self);
        if (pulse)
        {
            float w = 1f + Mathf.Sin(time * 2.3f) * 0.25f;
            transform.localScale = new Vector3(baseScale.x * w, baseScale.y, baseScale.z * w);
        }
    }
}

// The Norns' cards (노른의 카드): lightning crackles between them and up into the dark now and then
public class FateSparks : MonoBehaviour
{
    public System.Collections.Generic.List<GameObject> cards;
    private float next;

    private void Update()
    {
        if (cards == null || Time.time < next)
            return;
        next = Time.time + Random.Range(0.5f, 1.4f);
        var live = cards.FindAll(c => c != null);
        if (live.Count == 0)
            return;
        Vector3 a = live[Random.Range(0, live.Count)].transform.position;
        Vector3 b = live.Count > 1 && Random.value < 0.6f
            ? live[Random.Range(0, live.Count)].transform.position
            : a + new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(1.5f, 3f), Random.Range(-1f, 1f));
        if ((a - b).sqrMagnitude > 0.1f)
            LightningFx.Bolt(a, b);
    }
}

// A card taken: it whirls up into the light and is gone
public class FateCardFly : MonoBehaviour
{
    private float t;
    private Vector3 start, startScale;

    private void Start()
    {
        start = transform.position;
        startScale = transform.localScale;
        var f = GetComponent<ForgeFloat>();
        if (f != null) Destroy(f);
    }

    private void Update()
    {
        t += Time.deltaTime;
        float k = t / 1.1f;
        transform.position = start + Vector3.up * (k * k * 5f);
        transform.Rotate(0f, 720f * Time.deltaTime, 0f, Space.World);
        transform.localScale = startScale * Mathf.Max(0f, 1f - k);
        if (k >= 1f)
            Destroy(gameObject);
    }
}
