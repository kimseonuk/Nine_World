using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Asgard's route map (갈림길): at the waystone that ends each area, [C] opens a star chart of the whole route - islands
// of light adrift in the dark, joined by thin threads. Places already walked are red marks with a tick, where the player
// stands has a red ring, and the next three ways glow (hover for what waits there, click to go). It opens zoomed in on
// here and the next places; the mouse wheel slides the view on toward the mid-boss and the guardian.
// Every place floats on its own island (VARCO, Resources/UI/Route/rm_isle_*) with its mark on top. Each mark is its
// area's theme; the last pick's areas also carry the mid-boss's mark underneath (the mid-boss joins one of their halls).
public partial class GameManager
{
    // Realms walked as a route: Asgard (the generated palace realm)
    private static bool Routed(Realm realm) => realm.palace && realm.scenePath == null;

    private bool AtRouteGate => !inHub && !StageHost.Hosted && Routed(CurrentRealm) && !travelling
        && currentRoom != null && currentRoom.RoomKind == Room.Kind.Exit && RouteGate.Near(player.transform.position);

    public void RouteToast(string text) => Toast(text, new Color(1f, 0.85f, 0.45f));

    // ── art (VARCO, Resources/UI/Route) ──
    private readonly Dictionary<string, Texture2D> routeArt = new Dictionary<string, Texture2D>();
    private GUIStyle routeTipTitle, routeTipText, routeNameStyle, routeHintStyle, routeTipPanel;
    private float routeUi = 1f;   // the route map's text scale

    private Texture2D RouteArt(string name)
    {
        if (!routeArt.TryGetValue(name, out Texture2D tex))
        {
            tex = Resources.Load<Texture2D>("UI/Route/" + name);
            routeArt[name] = tex;
        }
        return tex;
    }

    // ── the view: which layer sits in the middle of the screen (eased toward where the wheel asks) ──
    private float routeView, routeViewTarget;
    private List<(Vector2 at, float length, float angle, float alpha)> routeShards;

    private void OpenRouteMap()
    {
        routeView = routeViewTarget = Mathf.Min(Route.Step + 0.5f, Route.LastLayer - 0.5f);
        Pause(Panel.Route);
    }

    // (tests only: stands in for the mouse, so the hover note can be checked without one)
    internal static Vector2? RouteMouseOverride;

    private static readonly Color RouteRed = new Color(0.78f, 0.07f, 0.12f);

    private void DrawRouteMap()
    {
        LoadMapArt();
        routeTipTitle ??= UiFont.Style(16, TextAnchor.UpperLeft, FontStyle.Bold);
        if (routeTipText == null) { routeTipText = UiFont.Style(14, TextAnchor.UpperLeft); routeTipText.wordWrap = true; }
        routeNameStyle ??= UiFont.Style(13, TextAnchor.UpperCenter, FontStyle.Bold);
        routeHintStyle ??= UiFont.Style(13, TextAnchor.MiddleCenter);
        // the map's own text grows with the screen (sized for 720p, up to 1.6x)
        routeUi = Mathf.Clamp(Screen.height / 720f, 1f, 1.6f);
        routeTipTitle.fontSize = Mathf.RoundToInt(16 * routeUi);
        routeTipText.fontSize = Mathf.RoundToInt(14 * routeUi);
        routeNameStyle.fontSize = Mathf.RoundToInt(14 * routeUi);
        routeHintStyle.fontSize = Mathf.RoundToInt(13 * routeUi);

        Event e = Event.current;
        // the wheel: on toward the ways ahead (down) or back (up)
        if (e.type == EventType.ScrollWheel)
        {
            routeViewTarget = Mathf.Clamp(routeViewTarget + Mathf.Sign(e.delta.y) * 0.75f, 0.5f, Mathf.Max(0.5f, Route.LastLayer - 0.45f));
            e.Use();
        }
        if (e.type == EventType.Repaint)
            routeView = Mathf.Lerp(routeView, routeViewTarget, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 9f));

        float w = Screen.width, h = Screen.height;
        // zoomed in: about two layers fill the screen. The route runs down the screen, each layer's ways side by side
        // (sized from the height left under the title banner, so here and the next ways both fit on screen)
        float layerPx = Mathf.Clamp((h - 150f) * 0.5f, 120f, 500f);
        // (columns a little wider apart than rows, narrower only when the window is)
        float acrossPx = Mathf.Min(layerPx * 1.05f, w * 0.3f);
        Vector2 ToScreen(Vector2 p) => new Vector2(w / 2f + p.y * acrossPx, h / 2f + 52f + (p.x - routeView) * layerPx);
        float baseR = Mathf.Clamp(layerPx * 0.09f, 16f, 44f);
        // every place floats on its own island (VARCO); the islands keep a clear gap between them, across and down
        float isleSize = Mathf.Min(layerPx * 0.78f, acrossPx * 0.84f);

        DrawRouteBackdrop(w, h, layerPx);

        int here = Route.Step;
        float t = Time.unscaledTime;

        // a great rune compass turning slowly under where the player stands
        Texture2D compass = RouteArt("rm_compass");
        Route.Node hereNode = Route.Layers[here].FirstOrDefault(n => n.visited);
        if (compass != null && hereNode != null)
            DrawTexRot(compass, ToScreen(hereNode.pos), Vector2.one * layerPx * 1.7f, t * 3f, new Color(0.8f, 0.9f, 1f, 0.2f));

        // ── the threads between places ──
        for (int l = 0; l < Route.LastLayer; l++)
            foreach (Route.Node a in Route.Layers[l])
                foreach (Route.Node b in Route.Layers[l + 1])
                {
                    Vector2 pa = ToScreen(a.pos), pb = ToScreen(b.pos);
                    if (Mathf.Max(pa.y, pb.y) < -40f || Mathf.Min(pa.y, pb.y) > h + 40f) continue;
                    bool walked = a.visited && b.visited;
                    bool live = a.visited && a.layer == here && b.layer == here + 1;
                    Color c = walked ? new Color(0.75f, 0.9f, 1f, 0.9f)
                        : live ? new Color(0.85f, 0.95f, 1f, 0.75f)
                        : a.layer < here ? new Color(0.5f, 0.6f, 0.9f, 0.12f)
                        : new Color(0.55f, 0.68f, 1f, 0.28f);
                    Vector2 d = (pb - pa).normalized;
                    float ra = NodeRadius(a, baseR) * 0.9f, rb = NodeRadius(b, baseR) * 0.9f;
                    Vector2 from = pa + d * ra, to = pb - d * rb;
                    // the way walked and the ways open now are beams of Bifrost light; the rest thin threads
                    Texture2D beam = RouteArt("rm_path");
                    if (beam != null && (walked || live))
                        DrawBeam(beam, from, to, walked ? 16f : 12f, walked ? new Color(0.85f, 0.95f, 1f, 1f)
                            : new Color(0.8f, 0.92f, 1f, 0.55f + 0.25f * Mathf.Sin(t * 3f + b.index)));
                    else
                        DrawBough(from, to, walked ? 2.6f : live ? 2.2f : 1.4f, c);
                    // light running along the ways that can be taken now
                    if (live)
                    {
                        float k = (t * 0.55f + b.index * 0.33f) % 1f;
                        DrawDisc(Vector2.Lerp(from, to, k), 3.2f, new Color(0.8f, 0.95f, 1f, 0.9f));
                    }
                }

        // ── the places ──
        Vector2 mouse = RouteMouseOverride ?? e.mousePosition;
#if UNITY_EDITOR
        if (routeTestHover is Vector2Int th && th.x < Route.Layers.Count && th.y < Route.Layers[th.x].Count)
            mouse = ToScreen(Route.Layers[th.x][th.y].pos) + new Vector2(0f, 2f);
#endif
        Route.Node hovered = null;
        Route.Node clicked = null;
        Vector2 hoveredAt = Vector2.zero;
        foreach (var layer in Route.Layers)
            foreach (Route.Node n in layer)
            {
                Vector2 c = ToScreen(n.pos);
                float r = NodeRadius(n, baseR);
                float isle = IsleSize(n, isleSize);
                if (c.y < -isle || c.y > h + isle) continue;
                bool isHere = n.visited && n.layer == here;
                bool past = n.visited && n.layer < here;
                bool choice = n.layer == here + 1 && !travelling;
                // the island sits a little below the mark, so the mark stands on its grass
                Vector2 isleAt = c + new Vector2(0f, isle * 0.1f);
                Vector2 badgeAt = c + new Vector2(0f, r * 1.6f);
                bool hover = (mouse - c).magnitude < r * 1.15f || (mouse - isleAt).magnitude < isle * 0.4f
                    || (n.midBoss && (mouse - badgeAt).magnitude < r * 0.75f);
                if (hover) { hovered = n; hoveredAt = c; }

                DrawIsle(isleAt, isle, n, past, isHere, choice, hover, t);
                if (past)
                    DrawPassedMark(c, r, n);
                else if (isHere)
                    DrawHereMark(c, r, n, t);
                else
                    DrawPlaceMark(c, r, n, choice, hover, t);
                // the mid-boss's mark, hung under the area's own
                if (n.midBoss)
                    DrawMidBossBadge(badgeAt, r * 0.66f, past ? 0.5f : choice || isHere ? 1f : 0.7f, hover && choice, t);

                // the names of here and the ways that can be taken
                if (isHere || choice || n.midBoss || n.theme == RouteTheme.Boss)
                {
                    string label = isHere ? "현재 위치" : Route.NameOf(n.theme);
                    GUI.color = isHere ? new Color(1f, 0.55f, 0.55f) : choice ? Color.white : new Color(0.8f, 0.85f, 1f, 0.75f);
                    // under the island, clear of the art
                    float labelY = Mathf.Max(n.midBoss ? badgeAt.y + r * 0.75f : c.y + r * 1.05f, isleAt.y + isle * 0.36f);
                    // (kept above the hint line at the foot of the screen: the names of here and the ways open now are
                    // pulled up to it, the rest left out when they would fall under it)
                    float labelFloor = h - 46f * routeUi - 24f * routeUi;
                    // (no room under it on a short screen: the name goes over the island instead, clear of its marks)
                    if (labelY > labelFloor && (isHere || choice))
                        labelY = isleAt.y - isle * 0.46f - 22f * routeUi;
                    if (labelY <= labelFloor)
                        DrawShadowedLabel(new Rect(c.x - 120f * routeUi, labelY, 240f * routeUi, 24f * routeUi), label, routeNameStyle);
                    GUI.color = Color.white;
                }

                if (choice && hover && e.type == EventType.MouseDown && e.button == 0)
                {
                    clicked = n;
                    e.Use();
                }
            }

        // ── the frame: Valhalla's runed pillars either side (Asgard), else the corners; the title on its banner, and hints ──
        bool hallFrame = DrawHallPillars(w, h);
        Texture2D corner = hallFrame ? null : RouteArt("rm_corner");
        if (corner != null)
        {
            float cs = Mathf.Clamp(Mathf.Min(w, h) * 0.17f, 70f, 170f);
            GUI.DrawTextureWithTexCoords(new Rect(4f, 4f, cs, cs), corner, new Rect(0f, 0f, 1f, 1f));
            GUI.DrawTextureWithTexCoords(new Rect(w - cs - 4f, 4f, cs, cs), corner, new Rect(1f, 0f, -1f, 1f));
            GUI.DrawTextureWithTexCoords(new Rect(4f, h - cs - 4f, cs, cs), corner, new Rect(0f, 1f, 1f, -1f));
            GUI.DrawTextureWithTexCoords(new Rect(w - cs - 4f, h - cs - 4f, cs, cs), corner, new Rect(1f, 1f, -1f, -1f));
        }
        string head = Route.NextIsBoss ? "수문장에게 가는 길이 열렸습니다"
            : Route.NextIsMidBoss ? $"다음 지역에는 중간 보스가 나타납니다  ·  갈림길 {Route.Step + 1}/{Route.ThemePicks}"
            : $"다음 지역을 고르세요  ·  갈림길 {Route.Step + 1}/{Route.ThemePicks}";
        Texture2D banner = RouteArt("rm_banner");
        float bannerH = 0f;
        if (banner != null)
        {
            float bw = Mathf.Min(w * 0.42f, 520f);
            bannerH = bw * banner.height / banner.width;
            GUI.DrawTexture(new Rect((w - bw) / 2f, 4f, bw, bannerH), banner);
        }
        float titleY = banner != null ? 4f + bannerH / 2f - 15f : 18f;
        GUI.color = new Color(1f, 0.92f, 0.7f);
        GUI.Label(new Rect(0f, titleY, w, 30f), $"{CurrentRealm.name} 갈림길", titleStyle);
        GUI.color = new Color(0.85f, 0.92f, 1f);
        GUI.Label(new Rect(0f, banner != null ? bannerH + 2f : 50f, w, 22f), head, centerStyle);
        bool more = routeViewTarget < Route.LastLayer - 0.5f;
        // the hint line on a dark band fading up from the foot of the screen, so it reads over the islands
        float bandH = 46f * routeUi;
        for (int i = 0; i < 10; i++)
            UiFont.Fill(new Rect(0f, h - bandH * (i + 1) / 10f, w, bandH / 10f), new Color(0.01f, 0.015f, 0.04f, 0.85f * (1f - i / 10f)));
        GUI.color = new Color(0.75f, 0.82f, 1f, 0.9f);
        GUI.Label(new Rect(0f, h - 34f * routeUi, w, 20f * routeUi), (more ? "[마우스 휠] 앞쪽 지역 보기  ·  " : "[마우스 휠] 돌아보기  ·  ") + "빛나는 곳을 눌러 이동  ·  [C] 또는 [Esc] 닫기", routeHintStyle);
        GUI.color = Color.white;
        if (more)
        {
            // a soft arrow at the bottom edge: there is more ahead
            float pulse = 0.5f + 0.3f * Mathf.Sin(t * 3f);
            Texture2D arrow = RouteArt("rm_arrow");
            if (arrow != null)
            {
                // (at the right edge, clear of the islands in the middle)
                float ah = 40f * routeUi, aw = ah * arrow.width / arrow.height, bob = Mathf.Sin(t * 3f) * 4f;
                GUI.color = new Color(1f, 1f, 1f, 0.55f + pulse * 0.5f);
                // (inside the pillar on the right when Valhalla's frame stands there)
                float ax = hallFrame ? w - w * 0.15f - aw - 18f : w - Mathf.Clamp(Mathf.Min(w, h) * 0.17f, 70f, 170f) * 0.5f - aw / 2f - 12f;
                GUI.DrawTexture(new Rect(ax, h * 0.5f + bob, aw, ah), arrow);
                GUI.color = Color.white;
            }
            else
            {
                Vector2 tip = new Vector2(w - 60f, h * 0.5f + 20f);
                DrawBough(tip + new Vector2(-14f, -10f), tip, 3f, new Color(0.7f, 0.85f, 1f, pulse));
                DrawBough(tip + new Vector2(14f, -10f), tip, 3f, new Color(0.7f, 0.85f, 1f, pulse));
            }
        }

        if (hovered != null)
        {
            DrawRouteTip(hovered, hoveredAt, NodeRadius(hovered, baseR), hovered.layer == here + 1 && !travelling);
            if (e.type == EventType.Repaint) UiSound.Hovered(1000 + hovered.layer * 31 + hovered.GetHashCode() % 997);
        }
        else if (e.type == EventType.Repaint) UiSound.Clear();

        if (clicked != null)
        {
            GameAudio.Play("ui_mapsel");
            Resume();
            StartCoroutine(EnterRouteArea(clicked.theme));
        }
    }

    // How big a place's island is drawn: the guardian's is the largest, the start's the smallest
    private static float IsleSize(Route.Node n, float size) =>
        n.theme == RouteTheme.Boss ? size * 1.25f : n.theme == RouteTheme.Start ? size * 0.85f : size;

    // The island a place floats on (VARCO, Resources/UI/Route/rm_isle_<theme>): bright for the ways open now and where
    // the player stands, dim for the places further on, faded for those walked. It bobs gently, each to its own time.
    private void DrawIsle(Vector2 c, float size, Route.Node n, bool past, bool isHere, bool choice, bool hover, float t)
    {
        Texture2D isle = RouteArt(IsleArt(n.theme)) ?? RouteArt(n.theme == RouteTheme.Boss ? "mk_island_big" : "mk_island");
        if (isle == null) return;
        float bob = Mathf.Sin(t * 0.9f + n.layer * 1.3f + n.index * 2.1f) * size * 0.012f;
        float grow = choice && hover ? 1.04f : 1f;
        float a = past ? 0.45f : isHere || choice ? 1f : 0.62f;
        float k = past ? 0.6f : isHere || choice ? 1f : 0.78f;   // (dimmed toward the cosmos' blue, not grey)
        Color tint = new Color(k, k, Mathf.Min(1f, k + 0.12f), a);
        float hgt = size * grow, wid = hgt * isle.width / isle.height;
        GUI.color = tint;
        GUI.DrawTexture(new Rect(c.x - wid / 2f, c.y - hgt / 2f + bob, wid, hgt), isle, ScaleMode.ScaleToFit);
        GUI.color = Color.white;
    }

    private static string IsleArt(RouteTheme theme) => theme switch
    {
        RouteTheme.Start => "rm_isle_start",
        RouteTheme.Forge => "rm_isle_forge",
        RouteTheme.Shop => "rm_isle_shop",
        RouteTheme.Supports => "rm_isle_supports",
        RouteTheme.Heal => "rm_isle_heal",
        RouteTheme.Vault => "rm_isle_vault",
        RouteTheme.Vigor => "rm_isle_vigor",
        RouteTheme.Ascend => "rm_isle_ascend",
        RouteTheme.Fate => "rm_isle_fate",
        RouteTheme.Essence => "rm_isle_essence",
        RouteTheme.Boss => "rm_isle_boss",
        _ => "rm_isle_start",
    };

    // A label with a dark outline, readable over the painted map
    private static void DrawShadowedLabel(Rect r, string text, GUIStyle style)
    {
        Color c = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.85f * c.a);
        foreach (var o in new[] { new Vector2(1.5f, 1.5f), new Vector2(-1.5f, 1.5f), new Vector2(1.5f, -1.5f), new Vector2(-1.5f, -1.5f) })
            GUI.Label(new Rect(r.x + o.x, r.y + o.y, r.width, r.height), text, style);
        GUI.color = c;
        GUI.Label(r, text, style);
    }

    // The mid-boss's mark under an area's mark: a small dark disc with the mark, a thin red ring round it
    private void DrawMidBossBadge(Vector2 c, float r, float alpha, bool hover, float t)
    {
        Texture2D glow = RouteArt("rm_glow");
        if (glow != null && alpha >= 1f)
            DrawTexRot(glow, c, Vector2.one * r * (hover ? 3.4f : 2.8f) * (0.92f + 0.08f * Mathf.Sin(t * 4f)), 0f, new Color(1f, 0.3f, 0.25f, 0.45f));
        DrawDisc(c, r * 1.05f, new Color(0.08f, 0.02f, 0.03f, 0.9f * alpha));
        DrawRing(c, r * 1.08f, 2f, new Color(1f, 0.35f, 0.3f, 0.9f * alpha));
        DrawRouteMarkIcon(c, r * 0.85f, RouteTheme.MidBoss, alpha);
    }

    private static float NodeRadius(Route.Node n, float baseR) =>
        n.theme == RouteTheme.Boss ? baseR * 1.45f : n.theme == RouteTheme.MidBoss ? baseR * 1.3f : n.theme == RouteTheme.Start ? baseR * 0.8f : baseR;

    // The dark of the map: deep blue, a faint grid sliding with the view, the star chart very dim, and blue shards
    // of light drifting round the edges
    private void DrawRouteBackdrop(float w, float h, float layerPx)
    {
        UiFont.Fill(new Rect(0f, 0f, w, h), new Color(0.012f, 0.02f, 0.06f, 0.97f));
        if (DrawHallBackdrop(w, h))
            return;
        float drift = Time.unscaledTime;
        Texture2D backdrop = RouteArt("rm_backdrop");
        if (backdrop != null)
        {
            // the painted cosmos (Yggdrasil's boughs faint across the top), sliding a little as the view moves on
            GUI.DrawTexture(new Rect(-20f, -routeView * 26f - 20f, w + 40f, h + 200f), backdrop, ScaleMode.ScaleAndCrop);
            UiFont.Fill(new Rect(0f, 0f, w, h), new Color(0.01f, 0.015f, 0.05f, 0.25f));
            // nebulae drifting slowly over it
            Texture2D na = RouteArt("rm_nebula_a"), nb = RouteArt("rm_nebula_b");
            if (na != null) DrawTexRot(na, new Vector2(w * 0.16f + Mathf.Sin(drift * 0.05f) * 30f, h * 0.7f - routeView * 40f % h), Vector2.one * h * 0.75f, drift * 1.5f, new Color(1f, 1f, 1f, 0.32f));
            if (nb != null) DrawTexRot(nb, new Vector2(w * 0.84f + Mathf.Cos(drift * 0.04f) * 30f, h * 0.38f - routeView * 30f % h), new Vector2(h * 0.7f, h * 0.75f), -drift, new Color(1f, 1f, 1f, 0.3f));
            if (na != null) DrawTexRot(na, new Vector2(w * 0.78f, h * 1.05f - routeView * 55f % (h * 1.2f)), Vector2.one * h * 0.55f, -drift * 2f, new Color(0.7f, 0.85f, 1f, 0.22f));
        }
        else if (mapBg != null)
        {
            GUI.color = new Color(0.35f, 0.5f, 1f, 0.16f);
            GUI.DrawTexture(new Rect(0f, -routeView * 30f - 60f, w, h + 120f), mapBg, ScaleMode.ScaleAndCrop);
            GUI.color = Color.white;
        }
        // (the grid and the shards slide up as the view moves on down the route)
        const float cell = 54f;
        float oy = -(routeView * layerPx * 0.35f) % cell;
        Color grid = new Color(0.4f, 0.55f, 1f, 0.045f);
        for (float x = 0f; x < w; x += cell) UiFont.Fill(new Rect(x, 0f, 1f, h), grid);
        for (float y = oy; y < h; y += cell) UiFont.Fill(new Rect(0f, y, w, 1f), grid);

        if (routeShards == null)
        {
            routeShards = new List<(Vector2, float, float, float)>();
            var rng = new System.Random(5);
            while (routeShards.Count < 46)
            {
                var p = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                // round the edges, leaving the middle clear
                if (Mathf.Abs(p.x - 0.5f) < 0.36f && Mathf.Abs(p.y - 0.5f) < 0.33f) continue;
                routeShards.Add((p, 6f + (float)rng.NextDouble() * 22f, (float)rng.NextDouble() * 180f, 0.35f + (float)rng.NextDouble() * 0.5f));
            }
        }
        Texture2D shard = RouteArt("rm_shard"), star = RouteArt("rm_star");
        int i = 0;
        foreach (var (at, length, angle, alpha) in routeShards)
        {
            i++;
            Vector2 c = new Vector2(at.x * w + Mathf.Sin(drift * 0.6f + angle) * 4f, (at.y * h - routeView * 18f) % h);
            if (c.y < 0f) c.y += h;
            if (shard != null)
            {
                // every third is a twinkling star instead of a crystal shard
                if (star != null && i % 3 == 0)
                {
                    float tw = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(drift * (0.8f + alpha) + angle));
                    DrawTexRot(star, c, Vector2.one * length * 1.6f, 0f, new Color(1f, 1f, 1f, tw));
                }
                else
                    DrawTexRot(shard, c, new Vector2(length * 0.55f, length * 1.1f), angle + drift * 6f, new Color(1f, 1f, 1f, alpha));
                continue;
            }
            float a = (angle + drift * 6f) * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * length / 2f;
            DrawBough(c - d, c + d, length * 0.42f, new Color(0.15f, 0.42f, 1f, alpha * 0.55f));
            DrawBough(c - d * 0.5f, c + d * 0.5f, length * 0.16f, new Color(0.55f, 0.8f, 1f, alpha * 0.6f));
        }
    }

    // ── Asgard's map is seen from Valhalla: down the great hall to Yggdrasil in the mist, the ways laid over it ──
    // (VARCO paintings in Resources/UI/Route: rm_hall, rm_steps, rm_pillar_l, rm_pillar_r; any realm without them, or
    // any missing piece, keeps the cosmos)
    private bool HallArt => CurrentRealm != null && CurrentRealm.palace && RouteArt("rm_hall") != null;

    private bool DrawHallBackdrop(float w, float h)
    {
        if (!HallArt)
            return false;
        float t = Time.unscaledTime;
        Texture2D hall = RouteArt("rm_hall");
        // the hall, breathing very slightly and sliding a little as the view moves down the route
        float zoom = 1.04f + Mathf.Sin(t * 0.12f) * 0.01f;
        float hw = w * zoom, hh = h * zoom;
        GUI.DrawTexture(new Rect((w - hw) / 2f, (h - hh) / 2f - 10f - routeView * 8f, hw, hh + 20f), hall, ScaleMode.ScaleAndCrop);
        // a cool veil over it so the islands and their names read, darker toward the edges
        UiFont.Fill(new Rect(0f, 0f, w, h), new Color(0.02f, 0.05f, 0.09f, 0.32f));
        for (int i = 0; i < 12; i++)
        {
            float k = i / 12f, band = w * 0.18f / 12f;
            Color edge = new Color(0.01f, 0.02f, 0.05f, 0.4f * (1f - k));
            UiFont.Fill(new Rect(i * band, 0f, band, h), edge);
            UiFont.Fill(new Rect(w - (i + 1) * band, 0f, band, h), edge);
        }
        // mist drifting slowly across the hall
        Texture2D na = RouteArt("rm_nebula_a"), nb = RouteArt("rm_nebula_b");
        if (na != null) DrawTexRot(na, new Vector2(w * (0.3f + 0.05f * Mathf.Sin(t * 0.05f)), h * 0.55f), new Vector2(h * 1.2f, h * 0.5f), Mathf.Sin(t * 0.03f) * 6f, new Color(0.85f, 0.95f, 1f, 0.12f));
        if (nb != null) DrawTexRot(nb, new Vector2(w * (0.7f + 0.05f * Mathf.Cos(t * 0.04f)), h * 0.42f), new Vector2(h * 1.1f, h * 0.45f), -Mathf.Sin(t * 0.025f) * 6f, new Color(0.9f, 0.95f, 1f, 0.1f));
        // the steps up to the hall across the foot of the screen (behind the islands)
        Texture2D steps = RouteArt("rm_steps");
        if (steps != null)
        {
            // (kept low: the ledge and a few steps, no more than a fifth of the screen)
            float sh = Mathf.Min(w * steps.height / steps.width, h * 0.2f);
            GUI.color = new Color(1f, 1f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(0f, h - sh, w, sh), steps, ScaleMode.StretchToFill);
            GUI.color = Color.white;
        }
        // gold motes rising slowly through the air
        Texture2D star = RouteArt("rm_star");
        if (star != null)
            for (int i = 0; i < 26; i++)
            {
                float seed = i * 12.9898f;
                float x = Mathf.Repeat(Mathf.Sin(seed) * 43758.5f, 1f) * w;
                float speed = 10f + Mathf.Repeat(seed * 7.1f, 1f) * 18f;
                float y = h - Mathf.Repeat(t * speed + i * 97f, h + 60f);
                float tw = 0.35f + 0.5f * Mathf.Abs(Mathf.Sin(t * (0.6f + i * 0.07f) + i));
                DrawTexRot(star, new Vector2(x + Mathf.Sin(t * 0.5f + i) * 12f, y), Vector2.one * (6f + (i % 4) * 3f), 0f, new Color(1f, 0.85f, 0.5f, tw));
            }
        return true;
    }

    // The two great pillars standing either side of the map, in front of it; returns whether they were drawn
    private bool DrawHallPillars(float w, float h)
    {
        if (!HallArt)
            return false;
        Texture2D left = RouteArt("rm_pillar_l"), right = RouteArt("rm_pillar_r");
        if (left == null && right == null)
            return false;
        float ph = h * 1.04f;
        // (no wider than about a seventh of the screen each, so the islands keep the middle)
        void Pillar(Texture2D tex, bool onLeft)
        {
            if (tex == null)
                return;
            float pw = Mathf.Min(ph * tex.width / tex.height, w * 0.15f);
            float x = onLeft ? -pw * 0.08f : w - pw * 0.92f;
            GUI.DrawTexture(new Rect(x, -h * 0.02f, pw, ph), tex, ScaleMode.StretchToFill);
        }
        Pillar(left, true);
        Pillar(right ?? left, false);
        return true;
    }

    // A picture turned about its middle
    private static void DrawTexRot(Texture2D tex, Vector2 c, Vector2 size, float degrees, Color color)
    {
        Matrix4x4 m = GUI.matrix;
        if (degrees != 0f) GUIUtility.RotateAroundPivot(degrees, c);
        GUI.color = color;
        GUI.DrawTexture(new Rect(c.x - size.x / 2f, c.y - size.y / 2f, size.x, size.y), tex);
        GUI.color = Color.white;
        GUI.matrix = m;
    }

    // A beam picture stretched from one point to another
    private static void DrawBeam(Texture2D tex, Vector2 from, Vector2 to, float thickness, Color color)
    {
        Vector2 d = to - from;
        if (d.magnitude < 1f) return;
        DrawTexRot(tex, (from + to) / 2f, new Vector2(d.magnitude + thickness * 0.5f, thickness), Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, color);
    }

    // A place already walked: a red blot with a tick
    private void DrawPassedMark(Vector2 c, float r, Route.Node n)
    {
        // a red wax seal with a gold tick (VARCO), a little askew
        Texture2D seal = RouteArt("rm_visited");
        if (seal != null)
        {
            DrawTexRot(seal, c, Vector2.one * r * 2.1f, (n.layer * 37 + n.index * 53) % 30 - 15f, Color.white);
            return;
        }
        for (int k = 0; k < 4; k++)
        {
            float a = (n.layer * 1.7f + n.index * 2.3f + k * 1.6f);
            Vector2 off = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 0.22f;
            DrawDisc(c + off, r * (0.78f - k * 0.04f), new Color(RouteRed.r, RouteRed.g, RouteRed.b, 0.75f));
        }
        Color tick = new Color(0.82f, 1f, 0.55f);
        float s = r * 0.42f;
        DrawBough(c + new Vector2(-s, 0f), c + new Vector2(-s * 0.25f, s * 0.7f), 3.2f, tick);
        DrawBough(c + new Vector2(-s * 0.25f, s * 0.7f), c + new Vector2(s, -s * 0.75f), 3.2f, tick);
    }

    // Where the player stands: a red ring round its mark
    private void DrawHereMark(Vector2 c, float r, Route.Node n, float t)
    {
        Texture2D glow = RouteArt("rm_glow"), runes = RouteArt("rm_here_ring");
        if (glow != null) DrawTexRot(glow, c, Vector2.one * r * 3.6f, 0f, new Color(1f, 0.2f, 0.2f, 0.35f + 0.1f * Mathf.Sin(t * 3f)));
        DrawDisc(c, r * 1.05f, new Color(0.05f, 0.02f, 0.06f, 0.85f));
        DrawRouteMarkIcon(c, r * 0.72f, n.theme, 1f);
        if (runes != null)
        {
            // the red rune circle turning round it
            DrawTexRot(runes, c, Vector2.one * r * (2.75f + 0.08f * Mathf.Sin(t * 3f)), t * 18f, Color.white);
            return;
        }
        float ring = r * (1.12f + 0.05f * Mathf.Sin(t * 3f));
        DrawRing(c, ring, 4f, new Color(1f, 0.12f, 0.15f, 0.95f));
        DrawRing(c, ring + 4f, 2f, new Color(1f, 0.3f, 0.3f, 0.35f));
    }

    // A place not yet reached: its mark, glowing when it is one of the next three ways
    private void DrawPlaceMark(Vector2 c, float r, Route.Node n, bool choice, bool hover, float t)
    {
        Color theme = Route.ColorOf(n.theme);
        Texture2D glow = RouteArt("rm_glow"), ring = RouteArt("rm_node_ring");
        if (choice)
        {
            float pulse = 0.85f + 0.12f * Mathf.Sin(t * 3f + n.index);
            if (glow != null)
                DrawTexRot(glow, c, Vector2.one * r * (hover ? 4.2f : 3.6f) * pulse, 0f, new Color(theme.r, theme.g, theme.b, hover ? 0.75f : 0.5f));
            else
                DrawDisc(c, r * (hover ? 1.6f : 1.4f) * pulse, new Color(theme.r, theme.g, theme.b, hover ? 0.35f : 0.2f));
        }
        DrawDisc(c, r * 0.98f, new Color(0.03f, 0.04f, 0.1f, 0.8f));
        float alpha = choice ? 1f : 0.55f;
        DrawRouteMarkIcon(c, r * (hover && choice ? 1.05f : 0.95f), n.theme, alpha);
        // the gold rune ring round every place still ahead (bright round the ways open now)
        if (ring != null)
            DrawTexRot(ring, c, Vector2.one * r * (hover && choice ? 2.6f : 2.4f), 0f,
                new Color(1f, 1f, 1f, choice ? (hover ? 1f : 0.9f) : 0.4f));
        else if (hover && choice)
            DrawRing(c, r * 1.12f, 2.5f, new Color(1f, 1f, 1f, 0.9f));
    }

    private void DrawRouteMarkIcon(Vector2 c, float r, RouteTheme theme, float alpha)
    {
        Texture2D mark = theme == RouteTheme.Start ? null : RouteArt(Route.MarkOf(theme));
        GUI.color = new Color(1f, 1f, 1f, alpha);
        if (mark != null)
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), mark, ScaleMode.ScaleToFit);
        else if (theme == RouteTheme.Start)
            DrawMapIcon(IconHere, new Rect(c.x - r * 0.8f, c.y - r * 0.8f, r * 1.6f, r * 1.6f), new Color(1f, 1f, 1f, alpha));
        else
            DrawDisc(c, r * 0.8f, Route.ColorOf(theme) * new Color(1f, 1f, 1f, alpha));
        GUI.color = Color.white;
    }

    private static void DrawRing(Vector2 c, float radius, float width, Color color)
    {
        const int segments = 40;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            DrawBough(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius, width, color);
        }
    }

    // The note beside a place: what waits there, and (for the next ways) that a click goes there
    private void DrawRouteTip(Route.Node n, Vector2 at, float r, bool canGo)
    {
        string title = n.midBoss ? $"{Route.NameOf(n.theme)}  +  중간 보스" : Route.NameOf(n.theme);
        string body = Route.TipOf(n.theme);
        if (n.theme == RouteTheme.Boss)
            body = $"{CurrentRealm.bossName}과(와) 싸웁니다.\n" + body;
        else if (n.theme != RouteTheme.Start)
            body = (n.midBoss ? "전투 구역 다섯 곳을 지나며 " : "전투 구역 여러 곳을 지나며 ") + $"{Route.NameOf(n.theme)}에 들릅니다.\n" + body;
        if (n.midBoss)
            body += $"\n\n[중간 보스] {CurrentRealm.MidBossTitle}\n마지막 전투 구역에 잡몹과 함께 나타납니다. 쓰러뜨리면 체력을 회복하고 보스 특수 강화를 얻습니다. 이 구역 다음은 수문장입니다.";
        if (n.visited && n.layer < Route.Step) body = "지나온 곳입니다.";
        else if (n.layer == Route.Step) body = "지금 있는 곳입니다.\n" + body;
        float k = routeUi;
        float width = (n.midBoss ? 320f : 280f) * k;
        float bodyH = routeTipText.CalcHeight(new GUIContent(body), width - 22f);
        float height = bodyH + 40f * k + (canGo ? 24f * k : 0f);
        // (room round it for the panel's frame)
        Rect box = new Rect(at.x - width - r * 0.6f - 20f, at.y - height - r * 0.4f - 16f, width, height);
        if (box.x < 34f) box.x = at.x + r * 0.6f + 20f;
        if (box.y < 30f) box.y = at.y + r * 0.6f + 16f;
        Color c = Route.ColorOf(n.theme);
        Texture2D panel = RouteArt("rm_tooltip");
        if (panel != null)
        {
            // the gold-framed Norse panel, nine-sliced so its corner knots keep their shape
            routeTipPanel ??= new GUIStyle { border = new RectOffset(26, 26, 24, 24), normal = { background = panel } };
            if (Event.current.type == EventType.Repaint)
                routeTipPanel.Draw(new Rect(box.x - 18f, box.y - 14f, box.width + 36f, box.height + 28f), false, false, false, false);
        }
        else
        {
            UiFont.Fill(new Rect(box.x - 1.5f, box.y - 1.5f, box.width + 3f, box.height + 3f), new Color(0.75f, 0.82f, 0.95f, 0.55f));
            UiFont.Fill(box, new Color(0.03f, 0.04f, 0.09f, 0.96f));
            Color corner = new Color(0.85f, 0.9f, 1f, 0.9f);
            foreach (var p in new[] { new Vector2(box.x, box.y), new Vector2(box.xMax - 6f, box.y), new Vector2(box.x, box.yMax - 6f), new Vector2(box.xMax - 6f, box.yMax - 6f) })
                UiFont.Fill(new Rect(p.x, p.y, 6f, 6f), corner);
        }
        GUI.color = c;
        GUI.Label(new Rect(box.x + 11f, box.y + 9f, width - 22f, 22f * k), title, routeTipTitle);
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x + 11f, box.y + 33f * k, width - 22f, bodyH), body, routeTipText);
        if (canGo)
        {
            // a little mouse, and 이동
            float mx = box.x + 13f, my = box.yMax - 22f * k;
            Texture2D mouseIcon = RouteArt("rm_mouse");
            if (mouseIcon != null)
                GUI.DrawTexture(new Rect(mx, my - 2f, 11f * k, 18f * k), mouseIcon, ScaleMode.ScaleToFit);
            else
            {
                UiFont.Fill(new Rect(mx, my, 10f, 15f), new Color(0.8f, 0.9f, 1f, 0.9f));
                UiFont.Fill(new Rect(mx + 1.5f, my + 1.5f, 7f, 12f), new Color(0.03f, 0.04f, 0.09f, 1f));
                UiFont.Fill(new Rect(mx + 4.25f, my + 2.5f, 1.5f, 4f), new Color(0.8f, 0.9f, 1f, 0.9f));
            }
            GUI.Label(new Rect(mx + 16f * k, my - 3f, 120f * k, 20f * k), "이동", routeTipText);
        }
    }

    // On to the chosen area: fade, gather what's left on the floor, build the area, arrive at its waystone
    private IEnumerator EnterRouteArea(RouteTheme theme)
    {
        travelling = true;
        for (; fade < 1f; fade += Time.unscaledDeltaTime / 0.35f)
            yield return null;
        fade = 1f;
        Pickup.CollectAll();
        foreach (Enemy enemy in Enemy.Alive.ToArray())
            if (enemy != null) Destroy(enemy.gameObject);
        bossRoom = null;

        Route.Advance(theme);
        bool midBoss = Route.CurrentNode != null && Route.CurrentNode.midBoss;
        var chain = Route.ChainFor(theme, midBoss, out int midBossHall);
        dungeon.GenerateArea(CurrentRealm, realmIndex, player.transform, chain, Route.Step, midBossHall);
        currentRoom = dungeon.StartRoom;
        Teleport(currentRoom.transform.position + new Vector3(0f, 1.1f, -2f), Quaternion.identity);
        yield return null;

        for (; fade > 0f; fade -= Time.unscaledDeltaTime / 0.45f)
            yield return null;
        fade = 0f;
        travelling = false;
        currentRoom.Enter();
        if (theme == RouteTheme.Boss)
            ShowBanner(Route.NameOf(theme), $"{CurrentRealm.bossName}이(가) 기다린다");
        else if (midBoss)
            ShowBanner($"갈림길 {Route.Step}/{Route.ThemePicks} · {Route.NameOf(theme)}", $"전투 구역 하나에 {CurrentRealm.MidBossTitle}이(가) 잡몹과 함께 나타난다 · 그 다음은 수문장");
        else
            ShowBanner($"갈림길 {Route.Step}/{Route.ThemePicks} · {Route.NameOf(theme)}", $"전투 구역 너머에 {Route.NameOf(theme)} · 끝의 룬 고리에서 [C]로 다음 길");
    }

#if UNITY_EDITOR
    // ── editor tests (driven from the editor while playing; not in builds) ──
    private Vector2Int? routeTestHover;

    // Starts a run on the routed realm if not on it, makes the first `step` picks, and opens the route map
    // (hover: the place the mouse is held over, or null)
    public string EditorTestRouteMap(int step, Vector2Int? hover)
    {
        var title = FindAnyObjectByType<TitleScreen>();
        if (title != null) Destroy(title.gameObject);
        int index = System.Array.FindIndex(realms, Routed);
        if (index < 0) return "no routed realm";
        if (inHub || !Routed(CurrentRealm)) StartRunAt(index);
        Route.Reset();
        if (step > 0) Route.SkipToFinal(Mathf.Min(step, Route.ThemePicks));
        routeTestHover = hover;
        OpenRouteMap();
        return $"realm {index} step {Route.Step} state {state}";
    }

    // Closes the map and walks into a place of the next layer, as a click on it would
    public string EditorTestEnterNext(int indexInLayer)
    {
        routeTestHover = null;
        var layer = Route.Layers[Mathf.Min(Route.Step + 1, Route.LastLayer)];
        RouteTheme theme = layer[Mathf.Clamp(indexInLayer, 0, layer.Count - 1)].theme;
        Resume();
        StartCoroutine(EnterRouteArea(theme));
        return "entering " + theme;
    }

    // The halls of the area built now, in order
    public string EditorTestRooms() => string.Join(" > ", dungeon.Rooms.Select(r => r.RoomKind + (r.HasMidBoss ? "(+중간보스)" : "")))
        + $"  | here {(currentRoom != null ? currentRoom.RoomKind.ToString() : "-")} fighting {(currentRoom != null && currentRoom.IsFighting)}"
        + $" boss {(currentRoom != null && currentRoom.Boss != null ? currentRoom.Boss.name : "-")} alive {Enemy.Alive.Count()}";

    public void EditorTestGoToMidBossHall() => TravelToZone(Room.Kind.MidBoss);
    public void EditorTestGoToBossHall() => TravelToZone(Room.Kind.Boss);
    public Room EditorTestCurrentRoom => currentRoom;
#endif
}
