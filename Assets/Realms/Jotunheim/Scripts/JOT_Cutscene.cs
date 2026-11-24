using System.Collections;
using UnityEngine;

namespace NineWorlds.Jotunheim
{
    // Jotunheim's boss and mid-boss cutscenes, run through the team's BossCutscene (letterbox, camera swung down to the
    // boss, name card). Inside the full game the game already holds the player still while one plays; on its own
    // (no GameManager) this holds the player's controls itself, and clears away Jotunheim's own attacks in flight.
    public static class JOT_Cutscene
    {
        public static readonly Color GuardianColor = new Color(0.4f, 0.85f, 1f);
        public static readonly Color MossColor = new Color(0.55f, 0.85f, 0.35f);

        // A boss steps up: its name comes up while it stands its ground
        public static void Intro(Transform boss, string title, string subtitle, Color accent) =>
            Play(boss, NameCard(title, subtitle, accent), false);

        private static IEnumerator NameCard(string title, string subtitle, Color accent)
        {
            yield return new WaitForSeconds(0.7f);
            Title(title, subtitle, accent);
            BossCutscene.Dolly(0.85f);
            yield return new WaitForSeconds(1.6f);
        }

        // The name of a boss or of its new phase, high in the middle of the screen (the shared card sits low, under the
        // skill bar). The shared cutscene is still told a title came up — an empty one, so it draws nothing — so it holds
        // the shot long enough to read it.
        public static void Title(string title, string subtitle, Color accent)
        {
            BossCutscene.Title("", "", accent);
            TitleCard.Show(title, subtitle, accent);
        }

        private class TitleCard : MonoBehaviour
        {
            private const float Seconds = 3.4f;
            private string title, subtitle;
            private Color accent;
            private float shownAt;

            public static void Show(string title, string subtitle, Color accent)
            {
                foreach (TitleCard old in FindObjectsByType<TitleCard>())
                    Destroy(old.gameObject);
                var card = new GameObject("JOT_TitleCard").AddComponent<TitleCard>();
                card.title = title;
                card.subtitle = subtitle;
                card.accent = accent;
                card.shownAt = Time.unscaledTime;
            }

            private void Update()
            {
                if (Time.unscaledTime - shownAt > Seconds + 0.6f)
                    Destroy(gameObject);
            }

            private void OnGUI()
            {
                GUI.depth = -20;   // over the letterbox
                float w = Screen.width, h = Screen.height;
                float t = Time.unscaledTime - shownAt;
                float alpha = Mathf.Clamp01(t / 0.35f) * Mathf.Clamp01((Seconds + 0.6f - t) / 0.6f);
                if (alpha <= 0f) return;
                // just under the upper letterbox bar (12% of the screen)
                float size = Mathf.Lerp(64f, 54f, Mathf.Clamp01(t / 0.5f)) * h / 720f;
                float y = Mathf.Max(h * 0.15f, 108f);   // (clear of the boss bar and what's under it, ~44..100 from the top)
                var titleStyle = UiFont.Style(Mathf.RoundToInt(size), TextAnchor.MiddleCenter, FontStyle.Bold);
                var subStyle = UiFont.Style(Mathf.RoundToInt(20f * h / 720f), TextAnchor.MiddleCenter);
                for (int i = 0; i < 4; i++)
                {
                    titleStyle.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.5f);
                    GUI.Label(new Rect((i % 2 * 2 - 1) * 2f, (i / 2 * 2 - 1) * 2f + y, w, size * 1.2f), title, titleStyle);
                }
                titleStyle.normal.textColor = new Color(Mathf.Lerp(1f, accent.r, 0.45f), Mathf.Lerp(1f, accent.g, 0.45f), Mathf.Lerp(1f, accent.b, 0.45f), alpha);
                GUI.Label(new Rect(0f, y, w, size * 1.2f), title, titleStyle);
                float lineY = y + size * 1.25f;
                float lineHalf = Mathf.Lerp(0f, w * 0.28f, Mathf.SmoothStep(0f, 1f, t / 0.6f));
                UiFont.Fill(new Rect(w / 2f - lineHalf, lineY, lineHalf * 2f, 2f), new Color(accent.r, accent.g, accent.b, alpha));
                if (string.IsNullOrEmpty(subtitle)) return;
                for (int i = 0; i < 4; i++)
                {
                    subStyle.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.5f * Mathf.Clamp01((t - 0.3f) / 0.4f));
                    GUI.Label(new Rect((i % 2 * 2 - 1) * 1.5f, lineY + 8f + (i / 2 * 2 - 1) * 1.5f, w, 28f * h / 720f), subtitle, subStyle);
                }
                subStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f, alpha * Mathf.Clamp01((t - 0.3f) / 0.4f));
                GUI.Label(new Rect(0f, lineY + 8f, w, 28f * h / 720f), subtitle, subStyle);
            }
        }

        // Any scene of the boss's own (a phase change); hitStop opens on a beat of slow motion
        public static void Play(Transform boss, IEnumerator scene, bool hitStop)
        {
            if (boss == null || BossCutscene.Playing)
                return;
            foreach (JOT_Projectile p in Object.FindObjectsByType<JOT_Projectile>())
                if (p != null) Object.Destroy(p.gameObject);
            foreach (JOT_RingWaves r in Object.FindObjectsByType<JOT_RingWaves>())
                if (r != null) Object.Destroy(r.gameObject);
            foreach (JOT_RingFill r in Object.FindObjectsByType<JOT_RingFill>())
                if (r != null) Object.Destroy(r.gameObject);
            BossCutscene.Play(boss, BossCutscene.HeightOf(boss), scene, hitStop);
            if (!StageHost.Hosted)
                new GameObject("JOT_CutsceneHold").AddComponent<Hold>();
        }

        // On its own: the player's movement and attack are switched off until the cutscene hands back
        private class Hold : MonoBehaviour
        {
            private Behaviour[] held;

            private void Start()
            {
                PlayerStats player = FindAnyObjectByType<PlayerStats>();
                held = new Behaviour[]
                {
                    player != null ? player.GetComponent<PlayerMovement>() : null,
                    FindAnyObjectByType<ClickToDig>(),
                };
                // Only what was on (a menu may already have taken some away)
                for (int i = 0; i < held.Length; i++)
                {
                    if (held[i] != null && held[i].enabled)
                        held[i].enabled = false;
                    else
                        held[i] = null;
                }
            }

            private void Update()
            {
                if (!BossCutscene.Playing)
                    Destroy(gameObject);
            }

            private void OnDestroy()
            {
                if (held == null)
                    return;
                foreach (Behaviour b in held)
                    if (b != null) b.enabled = true;
            }
        }
    }
}
