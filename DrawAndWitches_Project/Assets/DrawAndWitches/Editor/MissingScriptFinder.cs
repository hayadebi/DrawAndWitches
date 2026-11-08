using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class MissingScriptFinder : EditorWindow
{
    private Vector2 scrollPosition;
    private List<GameObject> missingObjects = new List<GameObject>();

    [MenuItem("Tools/Find Missing Scripts")]
    public static void ShowWindow()
    {
        GetWindow<MissingScriptFinder>("Missing Scripts");
    }

    private void OnGUI()
    {
        if (GUILayout.Button("現在のシーンからMissingスクリプトを検索", GUILayout.Height(30)))
        {
            FindMissingScriptsInScene();
        }

        EditorGUILayout.Space();

        if (missingObjects.Count == 0)
        {
            EditorGUILayout.HelpBox("Missingスクリプトは見つかりませんでした、または検索していません。", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"発見数: {missingObjects.Count} 件", EditorStyles.boldLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        for (int i = 0; i < missingObjects.Count; i++)
        {
            GameObject obj = missingObjects[i];
            if (obj == null) continue;

            EditorGUILayout.BeginHorizontal();

            // オブジェクト名を表示（クリックでHierarchy上のオブジェクトを選択）
            if (GUILayout.Button(obj.name, EditorStyles.label))
            {
                Selection.activeGameObject = obj;
                EditorGUIUtility.PingObject(obj);
            }

            // pingボタン
            if (GUILayout.Button("選択", GUILayout.Width(60)))
            {
                Selection.activeGameObject = obj;
                EditorGUIUtility.PingObject(obj);
            }

            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private void FindMissingScriptsInScene()
    {
        missingObjects.Clear();

        // アクティブなシーンの全ルートオブジェクトを取得
        GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (var root in rootObjects)
        {
            // 子オブジェクトも含めてすべてチェック
            Transform[] allChildren = root.GetComponentsInChildren<Transform>(true);
            foreach (var child in allChildren)
            {
                GameObject go = child.gameObject;
                Component[] components = go.GetComponents<Component>();

                for (int i = 0; i < components.Length; i++)
                {
                    // コンポーネントがnull＝スクリプトが外れている、またはロードできない
                    if (components[i] == null)
                    {
                        if (!missingObjects.Contains(go))
                        {
                            missingObjects.Add(go);
                        }
                        break;
                    }
                }
            }
        }
    }
}
