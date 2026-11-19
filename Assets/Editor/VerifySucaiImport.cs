using System.IO;
using UnityEditor;
using UnityEngine;

public static class VerifySucaiImport
{
    public static void Verify()
    {
        string[] mustExist =
        {
            "Assets/Scenes/Game/Scene 2 - InGame.unity",
            "Assets/Scenes/Game/Scene 3 - WeTogether.unity",
            "Assets/Scenes/Game/Scene 4 - StartSecret.unity",
            "Assets/Resources/mita/FaceLayer.prefab",
            "Assets/Resources/hands/Hand Left.prefab",
            "Assets/Resources/hands/Hand Right.prefab",
            "Assets/AnimatorController/Animator.controller",
            "Assets/AnimatorController/Mita.controller",
            "Assets/AnimatorController/Mita Alternative.controller",
            "Assets/AnimatorController/MitaDialogue.controller",
            "Assets/AnimatorController/Hand.controller",
            "Assets/AnimatorController/PlayerHandAnimations.controller",
            "Assets/AnimatorController/PlayerAnimations.controller",
            "Assets/Avatar/PlayerAvatar.asset",
            "Assets/_PoiyomiShaders",
            "Assets/Shader",
            "Assets/Kino",
            "Assets/Colorful",
            "Assets/Resources/lofipalettes",
            "Assets/Plugins/RootMotion/FinalIK",
            "Assets/Plugins/EPOOutline",
            "Assets/MagicaCloth",
        };

        var missing = 0;
        foreach (var p in mustExist)
        {
            var ok = Directory.Exists(p) || AssetDatabase.LoadMainAssetAtPath(p) != null || File.Exists(p);
            if (!ok)
            {
                Debug.LogError("[VerifySucai] MISSING: " + p);
                missing++;
            }
            else
            {
                Debug.Log("[VerifySucai] OK: " + p);
            }
        }

        if (Directory.Exists("Assets/Scripts"))
        {
            Debug.LogError("[VerifySucai] Assets/Scripts should not exist");
            missing++;
        }

        if (missing > 0)
        {
            Debug.LogError("[VerifySucai] FAILED missing=" + missing);
            EditorApplication.Exit(2);
            return;
        }

        Debug.Log("[VerifySucai] SUCCESS");
        EditorApplication.Exit(0);
    }
}
