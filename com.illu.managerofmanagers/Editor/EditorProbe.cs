using UnityEngine;
using UnityEditor;
namespace ManagerOfManagers.Editor
{                          
    public class EditorProbe
    {
        public static void Ping() => Debug.Log(AssetDatabase.GetAssetPath(Selection.activeObject));
    }
}
