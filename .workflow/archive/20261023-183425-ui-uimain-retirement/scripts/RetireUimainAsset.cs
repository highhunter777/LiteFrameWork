using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using LiteCodeGen;

public static class RetireUimainAsset
{
    public static string Run()
    {
        const string path = "Assets/ShareResource/UI/Screens/Tmp_UiTestPage.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null) return "missing:" + path;
        try
        {
            root.name = "Tmp_UiTestPage";

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            if (scaler != null) Object.DestroyImmediate(scaler, true);

            int renamed = 0;
            foreach (BindNode bind in root.GetComponentsInChildren<BindNode>(true))
            {
                if (bind.BindName == "BtnClose") { bind.BindName = "Button_Close"; renamed++; }
                else if (bind.BindName == "Label") { bind.BindName = "Txt_Label"; renamed++; }
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
            return "ok root=" + root.name + " bindRenamed=" + renamed;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
