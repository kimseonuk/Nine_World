using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // Bleeding from the twin axes: stacks on an enemy, each second it takes damage by the stack count.
    // A new stack refreshes the timer; with no new stacks for a while it stops.
    public class JOT_Bleed : MonoBehaviour
    {
        public const int MaxStacks = 10;
        private const float Duration = 5f;
        private const float TickSeconds = 1f;
        private const float PerStack = 0.25f;   // × the hero's damage per stack per tick

        private static readonly Color BleedColor = new Color(0.85f, 0.1f, 0.15f);

        private Enemy enemy;
        private int stacks;
        private int heroDamage;
        private float until;
        private float nextTick;

        public int Stacks => stacks;

        public static int StacksOn(Enemy enemy)
        {
            if (enemy == null)
                return 0;
            JOT_Bleed bleed = enemy.GetComponent<JOT_Bleed>();
            return bleed != null ? bleed.stacks : 0;
        }

        public static void Add(Enemy enemy, int count, int damage)
        {
            if (enemy == null || enemy.IsDead || enemy is JOT_HittableProp || enemy is JOT_Vine)
                return;
            JOT_Bleed bleed = enemy.GetComponent<JOT_Bleed>();
            if (bleed == null)
            {
                bleed = enemy.gameObject.AddComponent<JOT_Bleed>();
                bleed.enemy = enemy;
                bleed.nextTick = Time.time + TickSeconds;
            }
            bleed.stacks = Mathf.Min(MaxStacks, bleed.stacks + count);
            bleed.heroDamage = Mathf.Max(1, damage);
            bleed.until = Time.time + Duration;
            JOT_PlayerFeedback.Popup(enemy.transform.position + Vector3.up * 2.3f, $"출혈 {bleed.stacks}", BleedColor, 0.9f);
        }

        private void Update()
        {
            if (enemy == null || enemy.IsDead || Time.time >= until)
            {
                Destroy(this);
                return;
            }
            if (Time.time >= nextTick)
            {
                nextTick += TickSeconds;
                int amount = Mathf.Max(1, Mathf.RoundToInt(stacks * PerStack * heroDamage * JOT_AxeTrees.BleedMultiplier));
                enemy.TakeHit(enemy.transform.position, amount);
                JOT_PlayerFeedback.Popup(enemy.transform.position + Vector3.up * 1.4f, amount.ToString(), BleedColor, 0.9f);
            }
        }

        // ---- Over the head: a blood drop icon and "×n", with a thin bar for the time left

        private static Texture2D dropIcon;
        private static GUIStyle countStyle, countShadow;

        private void OnGUI()
        {
            if (enemy == null || enemy.IsDead || Event.current.type != EventType.Repaint)
                return;
            Camera view = Camera.main;
            if (view == null)
                return;
            var body = enemy.GetComponent<CharacterController>();
            float top = body != null ? (body.center.y + body.height * 0.5f) * enemy.transform.lossyScale.y : 2f;
            Vector3 screen = view.WorldToScreenPoint(enemy.transform.position + Vector3.up * (top + 0.5f));
            if (screen.z <= 0f)
                return;
            if (dropIcon == null)
                dropIcon = MakeDrop();
            if (countStyle == null)
            {
                countStyle = UiFont.Style(22, TextAnchor.MiddleLeft, FontStyle.Bold);
                countShadow = new GUIStyle(countStyle);
                countStyle.normal.textColor = Color.white;
                countShadow.normal.textColor = new Color(0.25f, 0f, 0f, 0.9f);
            }

            // Bigger and brighter as it stacks up; at 5+ (the phantom execution bonus) it pulses
            bool deep = stacks >= 5;
            float size = 28f + stacks * 1.2f;
            float pulse = deep ? 1f + Mathf.Sin(Time.time * 10f) * 0.08f : 1f;
            float w = size * 0.8f * pulse, h = size * pulse;
            float x = screen.x - 18f, y = Screen.height - screen.y - h / 2f;
            if (enemy.IsBoss)
            {
                // A big boss's head can be above the screen: keep the drop in view, under the boss HP bar
                y = Mathf.Clamp(y, 64f, Screen.height - 120f);
                x = Mathf.Clamp(x, 30f, Screen.width - 80f);
            }
            else if (x < -40f || x > Screen.width + 40f || y < -40f || y > Screen.height)
                return;   // an ordinary enemy off screen shows nothing
            GUI.DrawTexture(new Rect(x - w / 2f, y, w, h), dropIcon);
            string label = "×" + stacks;
            var r = new Rect(x + w / 2f + 2f, y + h / 2f - 12f, 60f, 24f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), label, countShadow);
            Color old = GUI.color;
            GUI.color = deep ? new Color(1f, 0.55f, 0.55f) : Color.white;
            GUI.Label(r, label, countStyle);
            GUI.color = old;

            // Time left before it wears off
            float left = Mathf.Clamp01((until - Time.time) / Duration);
            UiFont.Fill(new Rect(x - w / 2f, y + h + 2f, 40f, 3f), new Color(0f, 0f, 0f, 0.6f));
            UiFont.Fill(new Rect(x - w / 2f, y + h + 2f, 40f * left, 3f), BleedColor);
        }

        // A red drop (round bottom, pointed top) with a dark rim and a small highlight, drawn once
        private static Texture2D MakeDrop()
        {
            const int W = 32, H = 40;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "JOT_BleedDrop", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            Color fill = new Color(0.85f, 0.08f, 0.12f), rim = new Color(0.35f, 0f, 0.03f), shine = new Color(1f, 0.75f, 0.75f);
            for (int j = 0; j < H; j++)
                for (int i = 0; i < W; i++)
                {
                    float u = (i + 0.5f) / W * 2f - 1f;    // -1..1 across
                    float v = (j + 0.5f) / H;              // 0 bottom .. 1 top
                    float d = Drop(u, v);                  // < 0 inside
                    Color c = Color.clear;
                    if (d < 0f)
                    {
                        c = d > -0.08f ? rim : fill;
                        float hx = u + 0.3f, hy = v - 0.38f;   // highlight up and to the left
                        if (hx * hx + hy * hy * 1.6f < 0.018f)
                            c = shine;
                    }
                    float edge = Mathf.Clamp01(-d / 0.04f);    // soft edge
                    c.a = d < 0f ? edge : 0f;
                    px[j * W + i] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // Signed distance-ish: a circle at the bottom joined to a point at the top
        private static float Drop(float u, float v)
        {
            const float cy = 0.34f, r = 0.32f;
            float circle = Mathf.Sqrt(u * u * 0.64f + (v - cy) * (v - cy)) - r;
            if (v <= cy)
                return circle;
            float width = r * Mathf.Pow(Mathf.Clamp01((0.97f - v) / (0.97f - cy)), 0.9f);
            float cone = Mathf.Abs(u) * 0.8f - width;
            return Mathf.Min(circle, Mathf.Max(cone, v - 0.97f));
        }
    }
}
