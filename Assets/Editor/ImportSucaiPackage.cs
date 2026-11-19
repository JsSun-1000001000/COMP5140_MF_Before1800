using System.IO;
using UnityEditor;
using UnityEngine;

public static class ImportSucaiPackage
{
    private static bool s_Done;

    // Unity -batchmode -nographics -quit -projectPath ... -executeMethod ImportSucaiPackage.Import
    public static void Import()
    {
        var packagePath = @"H:\P_UnityProject\sucai\MiSide_Sucai_Core.unitypackage";
        if (!File.Exists(packagePath))
        {
            Debug.LogError("[ImportSucai] Package not found: " + packagePath);
            EditorApplication.Exit(1);
            return;
        }

        s_Done = false;
        AssetDatabase.importPackageCompleted += OnCompleted;
        AssetDatabase.importPackageFailed += OnFailed;
        AssetDatabase.importPackageCancelled += OnCancelled;

        Debug.Log("[ImportSucai] Importing " + packagePath);
        AssetDatabase.ImportPackage(packagePath, false);

        // Fallback if callbacks never fire (some batchmode builds finish sync)
        EditorApplication.delayCall += () =>
        {
            if (!s_Done)
            {
                Debug.Log("[ImportSucai] delayCall fallback verify");
                VerifyAndExit();
            }
        };
    }

    private static void OnCompleted(string packageName)
    {
        Debug.Log("[ImportSucai] importPackageCompleted: " + packageName);
        VerifyAndExit();
    }

    private static void OnFailed(string packageName, string error)
    {
        Debug.LogError("[ImportSucai] importPackageFailed: " + packageName + " err=" + error);
        s_Done = true;
        EditorApplication.Exit(3);
    }

    private static void OnCancelled(string packageName)
    {
        Debug.LogError("[ImportSucai] importPackageCancelled: " + packageName);
        s_Done = true;
        EditorApplication.Exit(4);
    }

    private static void VerifyAndExit()
    {
        if (s_Done) return;
        s_Done = true;

        AssetDatabase.Refresh();

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
        };

        var missing = 0;
        foreach (var p in mustExist)
        {
            if (AssetDatabase.LoadMainAssetAtPath(p) == null)
            {
                Debug.LogError("[ImportSucai] Missing after import: " + p);
                missing++;
            }
            else
            {
                Debug.Log("[ImportSucai] OK: " + p);
            }
        }

        // Ensure Scripts were not pulled in
        if (Directory.Exists("Assets/Scripts"))
        {
            Debug.LogWarning("[ImportSucai] Assets/Scripts exists after import — removing per export rules");
            FileUtil.DeleteFileOrDirectory("Assets/Scripts");
            FileUtil.DeleteFileOrDirectory("Assets/Scripts.meta");
            AssetDatabase.Refresh();
        }

        if (missing > 0)
        {
            Debug.LogError("[ImportSucai] FAILED missing=" + missing);
            EditorApplication.Exit(2);
            return;
        }

        Debug.Log("[ImportSucai] SUCCESS");
        EditorApplication.Exit(0);
    }
}
