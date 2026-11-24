using System.Collections;
using UnityEngine;

// 「신들의 문지기」 — the conversation before Heimdall's fight, as the design doc lays it out, told as a visual novel over
// his arena (VisualNovel): the hero on the left, Heimdall on the right, the lines in the box below.
//  1 최후의 관문     the light fades; a voice out of the dark (no face yet)
//  2 헤임달의 등장   he steps in on the right, full figure, then the camera closes on his face; cold and still
//  3 엇갈린 신념     the two of them at odds; a black light passes through his eyes
//  4 헤임달의 본심   black cracks run over him, a moment of pain, then the mask again; "닥쳐라" up close
//  5 최후의 경고     he takes his stance, the runes round the arena burn red
//  6 보스전 시작     he raises the horn — the box goes, and HeimdallBoss blows it for real and the fight begins
public static class HeimdallDialogue
{
    private static readonly Color HeimdallGold = new Color(1f, 0.82f, 0.4f);
    private static readonly Color HeroBlue = new Color(0.55f, 0.78f, 1f);
    private static readonly Color Narration = new Color(0.75f, 0.75f, 0.75f);
    private static readonly Color Blight = new Color(0.45f, 0.1f, 0.65f);

    // riderModel: Heimdall's own figure (the rider, with his animator); arena: where the runes light up.
    // Played as a staged 3D scene with the VARCO cast (the corrupted Heimdall acting it out, cut shot by shot like an
    // anime, the lines in the dialogue box); the portrait version below if that cast isn't in the project.
    public static IEnumerator Run(GameObject riderModel, Vector3 arena)
    {
        if (StoryStage.Has("HeimdallCorrupt"))
            return HeimdallStage.Run();
        return RunPortraits(riderModel, arena);
    }

    private static IEnumerator RunPortraits(GameObject riderModel, Vector3 arena)
    {
        var switcher = Object.FindAnyObjectByType<CharacterSwitcher>();
        CharacterProfile heroProfile = switcher != null ? switcher.Current : null;
        string hero = heroProfile != null ? heroProfile.DisplayName : "전사";
        const string Heimdall = "헤임달";
        const string Unknown = "???";
        const VisualNovel.Side H = VisualNovel.Side.Right, P = VisualNovel.Side.Left;

        VisualNovel vn = VisualNovel.Open("heimdall_temple");

        // ── 1. 최후의 관문: the light dies away; only footsteps; a voice from the dark ──
        vn.StartCoroutine(Ramp(v => vn.Dim = v, 0f, 0.82f, 1.6f));
        yield return vn.Beat(0.8f);
        yield return vn.Say(null, "주변의 빛이 서서히 사라지고… 텅 빈 신전에 발걸음 소리만이 울려 퍼진다.", Narration);
        yield return vn.Say(Unknown, "여기까지 도달한 인간이 있었군.", HeimdallGold);
        VNPortrait me = vn.Portrait(P, "hero", heroProfile != null ? heroProfile.gameObject : null, HeroBlue);
        me.AlphaTarget = 1f;
        me.ZoomTarget = me.Zoom = 0.25f;
        yield return vn.Say(hero, "인간이라고? 나는 아스가르드의 전사다.", HeroBlue, P);
        yield return vn.Say(Unknown, "……그렇다면 더욱더 이곳에 있어서는 안 되겠군.", HeimdallGold);

        // ── 2. 헤임달의 등장: in on the right, the whole of him, then his face; cold ──
        VNPortrait him = vn.Portrait(H, "heimdall", riderModel, new Color(0.55f, 0.75f, 1f));
        him.Alpha = 0f;
        him.Zoom = him.ZoomTarget = 0f;
        him.Lights(new Color(0.85f, 0.9f, 1f), 2f, new Color(0.5f, 0.75f, 1f), 3.5f);
        him.Speaking = true;
        me.Speaking = false;
        vn.StartCoroutine(Ramp(v => vn.Dim = v, 0.82f, 0.6f, 1f));
        yield return vn.Beat(1.4f);
        him.ZoomTarget = 1f;
        yield return vn.Say(Heimdall, "나는 아스가르드의 수호자, 헤임달.", HeimdallGold, H);
        yield return vn.Say(Heimdall, "이곳을 지키는 것이 나의 사명이다.", HeimdallGold, H);
        him.ZoomTarget = 0.4f;
        yield return vn.Say(hero, "아스가르드가 무너지고 있다. 너도 그 사실을 알고 있잖아!", HeroBlue, P);
        yield return vn.Say(Heimdall, "알고 있다. 그렇기에 더욱 누구도 이곳을 지나갈 수 없다.", HeimdallGold, H);

        // ── 3. 엇갈린 신념: at odds; a black light flickers through his eyes ──
        yield return vn.Say(hero, "세계수를 되살리기 위해 이곳까지 왔다. 길을 비켜!", HeroBlue, P);
        yield return vn.Say(Heimdall, "세계수를 되살린다고?", HeimdallGold, H);
        him.ZoomTarget = 0.85f;
        him.EyeFlash(Blight, 1.6f);
        vn.StartCoroutine(Wash(vn, new Color(0.12f, 0f, 0.18f), 0.35f, 1.2f));
        yield return vn.Say(Heimdall, "이미 늦었다. 세계수는 썩어가고 있으며, 아스가르드는 돌이킬 수 없는 길에 들어섰다.", HeimdallGold, H);
        him.ZoomTarget = 0.45f;
        yield return vn.Say(hero, "그렇다고 포기할 수는 없어!", HeroBlue, P, true);
        yield return vn.Say(Heimdall, "포기? 나는 단 한 번도 나의 사명을 포기한 적이 없다.", HeimdallGold, H);

        // ── 4. 헤임달의 본심: the blight shows through him; pain, then the mask again ──
        yield return vn.Say(hero, "너도 타락한 건가……?", HeroBlue, P);
        him.Speaking = true;
        me.Speaking = false;
        him.Play("Hit", 0.1f);
        him.Smoke(true);
        him.Shake = 1.5f;
        vn.Shake = 7f;
        vn.StartCoroutine(Ramp(v => him.Darken(new Color(0.25f, 0.12f, 0.35f), v), 0f, 1f, 0.6f));
        vn.StartCoroutine(Wash(vn, Blight * 0.3f, 0.4f, 1.6f));
        him.EyeFlash(Blight, 2f);
        yield return vn.Beat(1.4f);
        him.Play("Locomotion", 0.5f);
        him.Smoke(false);
        vn.StartCoroutine(Ramp(v => him.Darken(new Color(0.25f, 0.12f, 0.35f), v), 1f, 0f, 0.8f));
        him.ZoomTarget = 1f;
        vn.StartCoroutine(Ramp(v => vn.Dim = v, 0.6f, 0.85f, 0.4f));
        yield return vn.Say(Heimdall, "닥쳐라.", HeimdallGold, H, true);
        him.EyeFlash(HeimdallGold, 1.4f);
        yield return vn.Say(Heimdall, "내 눈은 아홉 세계의 모든 것을 꿰뚫어 본다. 너의 운명 또한 이미 보았다.", HeimdallGold, H);
        him.ZoomTarget = 0.4f;
        vn.StartCoroutine(Ramp(v => vn.Dim = v, 0.85f, 0.6f, 0.6f));
        yield return vn.Say(hero, "그렇다면 내가 이곳에 도달할 것도 알고 있었겠군.", HeroBlue, P);
        yield return vn.Say(Heimdall, "……그래. 그렇기에 네가 이곳에 도달하기를 기다렸다.", HeimdallGold, H);

        // ── 5. 최후의 경고: he takes his stance; the runes round the arena burn red ──
        him.Play("Charge", 0.25f);
        him.Lights(new Color(1f, 0.85f, 0.7f), 2.4f, new Color(1f, 0.25f, 0.15f), 4.5f);
        VfxPiece runes = Vfx.Decal(arena + Vector3.up * 0.06f, 14f, new Color(1f, 0.2f, 0.12f), "gs_runecircle", 18f);
        vn.StartCoroutine(Pulse(vn, new Color(0.5f, 0.05f, 0.02f), 0.22f));
        yield return vn.Say(hero, "마지막으로 묻겠다. 정말 나와 싸우겠다는 건가?", HeroBlue, P);
        yield return vn.Say(Heimdall, "나는 아스가르드의 문지기다. 그 누구도 나를 넘어설 수 없다.", HeimdallGold, H);
        yield return vn.Say(Heimdall, "돌아가라. 이것이 너에게 주는 마지막 기회다.", HeimdallGold, H);
        yield return vn.Say(hero, "미안하지만…… 여기까지 와서 돌아갈 수는 없어.", HeroBlue, P);
        him.ZoomTarget = 0.9f;
        yield return vn.Say(Heimdall, "그렇다면 증명해라. 네가 세계를 구할 자격이 있는지.", HeimdallGold, H);

        // ── 6. 보스전 시작: he raises the horn ──
        him.Play("Horn", 0.2f);
        him.ZoomTarget = 0.15f;
        yield return vn.Say(Heimdall, "와라. 네 운명을 내 눈앞에서 증명해라!", HeimdallGold, H, true);
        vn.Close();
        if (runes != null) Object.Destroy(runes.gameObject, 3f);
        yield return new WaitForSeconds(0.4f);
    }

    private static IEnumerator Ramp(System.Action<float> set, float from, float to, float seconds)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
        {
            set(Mathf.Lerp(from, to, t));
            yield return null;
        }
        set(to);
    }

    // A colour washes over the screen and fades
    private static IEnumerator Wash(VisualNovel vn, Color color, float strength, float seconds)
    {
        for (float t = 0f; t < 1f && vn != null; t += Time.unscaledDeltaTime / seconds)
        {
            vn.Tint = new Color(color.r, color.g, color.b, strength * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }
        if (vn != null) vn.Tint = Color.clear;
    }

    // The red of the runes beating on the screen until the talk ends
    private static IEnumerator Pulse(VisualNovel vn, Color color, float strength)
    {
        for (float t = 0f; vn != null; t += Time.unscaledDeltaTime)
        {
            vn.Tint = new Color(color.r, color.g, color.b, strength * (0.55f + 0.45f * Mathf.Sin(t * 3f)));
            yield return null;
        }
    }
}
