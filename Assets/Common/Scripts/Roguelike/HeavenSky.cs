using UnityEngine;

// While Heimdall's arena is the zone being shown, the sky round it is day: a bright pale haze over a sea of clouds
// instead of the night of the cosmos. Whatever the sky was is put back when the zone is hidden or gone.
// (Heimdall's own staged scene saves and restores the sky itself, so it comes back to this one afterwards.)
public class HeavenSky : MonoBehaviour
{
    public static readonly Color Haze = new Color(0.8f, 0.84f, 0.93f);
    // the first hall's sky: the same clouds in the warm light of a sunset
    public static readonly Color SunsetHaze = new Color(1f, 0.84f, 0.68f);

    private bool sunset;

    public void Sunset()
    {
        sunset = true;
        if (applied)
            Apply();
    }

    private void Apply()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = sunset ? SunsetHaze : Haze;
        RenderSettings.fogStartDistance = sunset ? 26f : 22f;
        RenderSettings.fogEndDistance = sunset ? 90f : 80f;
        RenderSettings.ambientLight = Color.Lerp(ambient, sunset ? new Color(0.95f, 0.82f, 0.72f) : new Color(0.85f, 0.84f, 0.88f), 0.5f);
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = sunset ? SunsetHaze : Haze;
        }
    }

    private bool applied;
    private bool fog;
    private FogMode fogMode;
    private Color fogColor, ambient, background;
    private float fogStart, fogEnd;
    private CameraClearFlags clear;

    private void OnEnable()
    {
        if (applied)
            return;
        applied = true;
        fog = RenderSettings.fog;
        fogMode = RenderSettings.fogMode;
        fogColor = RenderSettings.fogColor;
        fogStart = RenderSettings.fogStartDistance;
        fogEnd = RenderSettings.fogEndDistance;
        ambient = RenderSettings.ambientLight;
        Camera cam = Camera.main;
        if (cam != null)
        {
            clear = cam.clearFlags;
            background = cam.backgroundColor;
        }
        Apply();
    }

    // The realm's own sky may be laid down after this zone is shown (the run's first hall is shown before the realm
    // has finished arriving): whatever was put there is kept as the sky to give back, and the day is laid again
    private void LateUpdate()
    {
        if (!applied)
            return;
        Color want = sunset ? SunsetHaze : Haze;
        if (RenderSettings.fogColor == want)
            return;
        fog = RenderSettings.fog;
        fogMode = RenderSettings.fogMode;
        fogColor = RenderSettings.fogColor;
        fogStart = RenderSettings.fogStartDistance;
        fogEnd = RenderSettings.fogEndDistance;
        ambient = RenderSettings.ambientLight;
        Camera cam = Camera.main;
        if (cam != null)
        {
            clear = cam.clearFlags;
            background = cam.backgroundColor;
        }
        Apply();
    }

    private void OnDisable()
    {
        if (!applied)
            return;
        applied = false;
        RenderSettings.fog = fog;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
        RenderSettings.ambientLight = ambient;
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = clear;
            cam.backgroundColor = background;
        }
    }
}
