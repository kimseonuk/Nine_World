#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// Portfolio video (temporary): copies the mixed game audio (stereo) while recording
public class AudioTap : MonoBehaviour
{
    public static bool Recording;
    private static readonly List<float> buffer = new List<float>();
    private static readonly object gate = new object();

    public static float[] Drain()
    {
        lock (gate) { var a = buffer.ToArray(); buffer.Clear(); return a; }
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!Recording) return;
        lock (gate)
        {
            if (channels == 2) buffer.AddRange(data);
            else
                for (int i = 0; i < data.Length; i += channels)
                {
                    float l = data[i], r = channels > 1 ? data[i + 1] : data[i];
                    buffer.Add(l); buffer.Add(r);
                }
        }
    }
}
#endif
