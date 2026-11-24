using UnityEditor;
using UnityEditor.SceneManagement;

// Pressing Play in the editor always starts the game the way a build does: from the first scene in Build Settings,
// which opens on the title screen (and the story after it), whichever scene happens to be open for editing.
[InitializeOnLoad]
internal static class PlayFromTitle
{
    static PlayFromTitle()
    {
        EditorApplication.delayCall += Apply;
        EditorBuildSettings.sceneListChanged += Apply;
    }

    private static void Apply()
    {
        var scenes = EditorBuildSettings.scenes;
        SceneAsset first = null;
        foreach (var s in scenes)
            if (s.enabled) { first = AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path); break; }
        EditorSceneManager.playModeStartScene = first;
    }
}
