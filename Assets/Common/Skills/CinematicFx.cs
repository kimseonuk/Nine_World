using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// A moment of drama for a big blow (일섬): the world drains to grey and time slows for an instant, then both come back.
// A global post-processing volume of its own (saturation -100, a touch darker) faded in and out in real time.
public class CinematicFx : MonoBehaviour
{
    private static CinematicFx instance;
    private Volume volume;
    private ColorAdjustments color;
    private float greyUntil, slowUntil, restoreScale = 1f, restoreFixed = 0.02f;
    private bool slowing;
    private bool postWasOn = true;
    private UniversalAdditionalCameraData camData;

    private static CinematicFx Instance
    {
        get
        {
            if (instance != null) return instance;
            var go = new GameObject("CinematicFx");
            instance = go.AddComponent<CinematicFx>();
            instance.volume = go.AddComponent<Volume>();
            instance.volume.isGlobal = true;
            instance.volume.priority = 500f;
            instance.volume.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            instance.color = profile.Add<ColorAdjustments>(true);
            instance.color.saturation.Override(-100f);
            instance.color.postExposure.Override(-0.25f);
            instance.color.contrast.Override(25f);
            instance.volume.profile = profile;
            return instance;
        }
    }

    // The world goes grey for `seconds` (real time)
    public static void Grey(float seconds)
    {
        CinematicFx fx = Instance;
        fx.greyUntil = Time.unscaledTime + seconds;
        fx.EnsurePost();
    }

    // Time runs at `scale` for `seconds` (real time), then back to what it was
    public static void SlowMotion(float scale, float seconds)
    {
        CinematicFx fx = Instance;
        if (!fx.slowing)
        {
            fx.restoreScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            fx.restoreFixed = Time.fixedDeltaTime;
            fx.slowing = true;
        }
        Time.timeScale = scale;
        Time.fixedDeltaTime = fx.restoreFixed * scale;
        fx.slowUntil = Time.unscaledTime + seconds;
    }

    // Held each frame while something is gathered (일섬's charge): grey and slowed for as long as it lasts —
    // never over a pause (a menu that stopped time keeps it stopped)
    public static void Hold(float scale)
    {
        if (Time.timeScale <= 0f) return;
        Grey(0.15f);
        SlowMotion(scale, 0.15f);
    }

    // Let go: time back at once, the grey fading out softly
    public static void Release()
    {
        if (instance == null) return;
        instance.greyUntil = 0f;
        if (instance.slowing) instance.EndSlow();
    }

    // Everything back at once (a skill cut short, the fighter changed)
    public static void Clear()
    {
        if (instance == null) return;
        instance.greyUntil = 0f;
        if (instance.slowing) instance.EndSlow();
        instance.volume.weight = 0f;
        instance.RestorePost();
    }

    private void EnsurePost()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        camData = cam.GetUniversalAdditionalCameraData();
        if (camData != null && !camData.renderPostProcessing)
        {
            postWasOn = false;
            camData.renderPostProcessing = true;
        }
    }

    private void RestorePost()
    {
        if (!postWasOn && camData != null) camData.renderPostProcessing = false;
        postWasOn = true;
    }

    private void EndSlow()
    {
        slowing = false;
        // (a menu may have paused the game meanwhile: only undo our own slowing)
        if (Time.timeScale > 0f) Time.timeScale = restoreScale;
        Time.fixedDeltaTime = restoreFixed;
    }

    private void Update()
    {
        if (slowing && Time.unscaledTime >= slowUntil) EndSlow();
        float want = Time.unscaledTime < greyUntil ? 1f : 0f;
        float speed = want > volume.weight ? 14f : 3f;   // (in sharply, out softly)
        volume.weight = Mathf.MoveTowards(volume.weight, want, speed * Time.unscaledDeltaTime);
        if (volume.weight <= 0f && want <= 0f) RestorePost();
    }

    private void OnDestroy()
    {
        if (slowing) EndSlow();
        RestorePost();
    }
}
