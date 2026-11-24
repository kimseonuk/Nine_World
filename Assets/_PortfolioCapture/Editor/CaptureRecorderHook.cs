using Unity.Collections;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

// Portfolio video (temporary): encodes CaptureDirector's frames and AudioTap's sound into an MP4 with MediaEncoder.
[InitializeOnLoad]
public static class CaptureRecorderHook
{
    private static MediaEncoder encoder;
    private static bool wasMuted;
    public static string Status = "idle";
    public static long Samples;

    static CaptureRecorderHook()
    {
        CaptureDirector.Sink = Encode;
        CaptureDirector.AudioSink = EncodeAudio;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingPlayMode) Close(); };
    }

    private static void Encode(Texture2D frame)
    {
        if (encoder == null)
        {
            var video = new VideoTrackAttributes
            {
                frameRate = new MediaRational(30),
                width = (uint)frame.width,
                height = (uint)frame.height,
                includeAlpha = false,
                bitRateMode = UnityEditor.VideoBitrateMode.High
            };
            var audio = new AudioTrackAttributes
            {
                sampleRate = new MediaRational(AudioSettings.outputSampleRate),
                channelCount = 2,
                language = "ko"
            };
            encoder = new MediaEncoder(CaptureDirector.OutputPath + ".mp4", video, audio);
            wasMuted = EditorUtility.audioMasterMute;
            EditorUtility.audioMasterMute = false;
            Samples = 0;
            Status = $"encoding {frame.width}x{frame.height} @{AudioSettings.outputSampleRate}";
        }
        encoder.AddFrame(frame);
    }

    private static void EncodeAudio(float[] samples)
    {
        if (encoder == null) return;
        using (var na = new NativeArray<float>(samples, Allocator.Temp))
            encoder.AddSamples(na);
        Samples += samples.Length / 2;
    }

    private static void Tick()
    {
        if (encoder != null && !CaptureDirector.WantRecord) Close();
    }

    private static void Close()
    {
        if (encoder == null) return;
        encoder.Dispose();
        encoder = null;
        EditorUtility.audioMasterMute = wasMuted;
        Status = "closed " + CaptureDirector.Frames + " frames, " + Samples + " samples";
    }
}
