#if UNITY_EDITOR
using UnityEngine;

// Editor test helper: takes screenshots at set moments after it is started (Temp/<prefix>_<n>.png), so a boss
// pattern can be looked at frame by frame while it plays
public class BossTestShots : MonoBehaviour
{
    private float[] times;
    private string prefix;
    private float t;
    private int next;

    public static void Take(string prefix, params float[] times)
    {
        var go = new GameObject("BossTestShots");
        var s = go.AddComponent<BossTestShots>();
        s.prefix = prefix;
        s.times = times;
    }

    private void Update()
    {
        t += Time.unscaledDeltaTime;
        if (next < times.Length && t >= times[next])
        {
            string dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Temp");
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, prefix + "_" + next + ".png"));
            next++;
        }
        if (next >= times.Length)
            Destroy(gameObject, 0.5f);
    }
}
#endif
