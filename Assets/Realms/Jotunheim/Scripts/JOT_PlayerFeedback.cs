using System.Collections.Generic;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // On the player: makes getting hurt obvious. The character flashes red, the edges of the screen pulse red,
    // the camera jolts and the damage pops up over their head. Dodges, wards and parries pop up too.
    // A small health bar rides over their head all the time.
    [RequireComponent(typeof(PlayerStats))]
    public class JOT_PlayerFeedback : MonoBehaviour
    {
        [SerializeField] private Color hitTint = new Color(1f, 0.3f, 0.25f);
        [SerializeField] private float flashSeconds = 0.15f;
        [SerializeField] private float headHeight = 1.2f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static JOT_PlayerFeedback instance;

        private readonly List<(Vector3 world, string text, Color color, float born, float size)> pops = new List<(Vector3, string, Color, float, float)>();
        private PlayerStats stats;
        private MaterialPropertyBlock block;
        private float lastHealth;
        private float flashUntil;
        private float vignette;
        private bool flashing;
        private GUIStyle popStyle;

        // Text that floats up from a point in the world and fades (damage, "패링!", ...)
        public static void Popup(Vector3 world, string text, Color color, float size = 1f)
        {
            if (instance != null)
                instance.pops.Add((world, text, color, Time.unscaledTime, size));
            else
                DualBlades.Say(world, text, color);   // in the full game (the armory's axes): the game's own floating words
        }

        private void Awake()
        {
            stats = GetComponent<PlayerStats>();
            block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            instance = this;
            stats.Damaged += OnDamaged;
            stats.Avoided += OnAvoided;
            lastHealth = stats.Health;
        }

        private void OnDisable()
        {
            stats.Damaged -= OnDamaged;
            stats.Avoided -= OnAvoided;
            if (instance == this)
                instance = null;
            SetTint(Color.white);
        }

        private void OnDamaged()
        {
            float amount = lastHealth - stats.Health;
            lastHealth = stats.Health;
            flashUntil = Time.unscaledTime + flashSeconds;
            vignette = Mathf.Clamp01(0.35f + amount / 60f);
            CameraShake.Shake(0.18f, Mathf.Clamp(amount / 120f, 0.08f, 0.3f));
            Popup(transform.position + Vector3.up * headHeight, $"-{Mathf.Max(1f, amount):0}", new Color(1f, 0.35f, 0.3f), amount >= 20f ? 1.4f : 1f);
        }

        private void OnAvoided(string text) =>
            Popup(transform.position + Vector3.up * headHeight, text, new Color(0.7f, 0.9f, 1f));

        private void Update()
        {
            if (stats.Health > lastHealth)
                lastHealth = stats.Health; // healed (runes, life steal): the next hit counts from here

            bool shouldFlash = Time.unscaledTime < flashUntil;
            if (shouldFlash != flashing)
            {
                SetTint(shouldFlash ? hitTint : Color.white);
                flashing = shouldFlash;
            }
            vignette = Mathf.MoveTowards(vignette, 0f, Time.unscaledDeltaTime * 1.4f);
        }

        // Tints every renderer of the character (and leaves effects parented to it alone)
        private void SetTint(Color color)
        {
            block ??= new MaterialPropertyBlock(); // lost if scripts reload mid-play in the editor
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (r is SkinnedMeshRenderer == false && r.GetComponentInParent<Animator>() == null)
                    continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                r.SetPropertyBlock(block);
            }
        }

        private void OnGUI()
        {
            // Red pulse around the edges of the screen
            if (vignette > 0.01f)
            {
                float w = Screen.width, h = Screen.height, edge = Mathf.Min(w, h) * 0.12f;
                Color c = new Color(0.85f, 0.05f, 0.05f, vignette * 0.6f);
                UiFont.Fill(new Rect(0f, 0f, w, edge), c);
                UiFont.Fill(new Rect(0f, h - edge, w, edge), c);
                UiFont.Fill(new Rect(0f, edge, edge, h - edge * 2f), c);
                UiFont.Fill(new Rect(w - edge, edge, edge, h - edge * 2f), c);
            }

            Camera cam = Camera.main;
            if (cam == null)
                return;

            // A small health bar over the head
            Vector3 head = cam.WorldToScreenPoint(transform.position + Vector3.up * (headHeight + 0.2f));
            if (head.z > 0f && !stats.IsDead)
            {
                Rect bar = new Rect(head.x - 30f, Screen.height - head.y - 4f, 60f, 8f);
                UiFont.Fill(bar, new Color(0f, 0f, 0f, 0.65f));
                float k = Mathf.Clamp01(stats.Health / stats.MaxHealth);
                Color fill = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.35f, 0.85f, 0.3f), k);
                UiFont.Fill(new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * k, bar.height - 2f), fill);
            }

            popStyle ??= UiFont.Style(22, TextAnchor.MiddleCenter, FontStyle.Bold);
            pops.RemoveAll(p => Time.unscaledTime - p.born > 0.9f);
            foreach (var (world, text, color, born, size) in pops)
            {
                float age = Time.unscaledTime - born;
                Vector3 screen = cam.WorldToScreenPoint(world + Vector3.up * age * 1.3f);
                if (screen.z <= 0f)
                    continue;
                popStyle.fontSize = Mathf.RoundToInt(22 * size);
                GUI.color = new Color(0f, 0f, 0f, 0.6f * (1f - age / 0.9f));
                GUI.Label(new Rect(screen.x - 79f, Screen.height - screen.y - 19f, 160f, 40f), text, popStyle);
                GUI.color = new Color(color.r, color.g, color.b, 1f - age / 0.9f);
                GUI.Label(new Rect(screen.x - 80f, Screen.height - screen.y - 20f, 160f, 40f), text, popStyle);
            }
            GUI.color = Color.white;
        }
    }
}
