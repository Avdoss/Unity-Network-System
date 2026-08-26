using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
namespace Network
{
    [CustomEditor(typeof(NetworkSceneManager))]
    public class NetworkSceneManagerEditor: Editor
    {
        private SerializedProperty registeredScenes;
        private SerializedProperty networkScenesPaths;
        private SerializedProperty registeredPrefabs;
        private List<NetworkObject> registeredPrefabsPrev;

        private static void SerializeNetworkObjectPrefab(SerializedObject obj, int regId)
        {
            obj.Update();
            obj.FindProperty("scenePos").intValue = -1;
            obj.FindProperty("isScenePlaced").boolValue = false;
            obj.FindProperty("id").intValue = -1;
            obj.FindProperty("host").intValue = -1;
            obj.FindProperty("regId").intValue = regId;
            obj.FindProperty("sceneId").intValue = -1;
            obj.FindProperty("owner").boolValue = false;
            obj.ApplyModifiedProperties();
        }

        private void OnEnable()
        {
            registeredScenes = serializedObject.FindProperty("registeredScenes");
            networkScenesPaths = serializedObject.FindProperty("networkScenesPaths");
            registeredPrefabs = serializedObject.FindProperty("registeredPrefabs");
            registeredPrefabsPrev = new List<NetworkObject>(registeredPrefabs.arraySize);
            for (int i = 0; i < registeredPrefabs.arraySize; i++)
                registeredPrefabsPrev.Add(registeredPrefabs.GetArrayElementAtIndex(i).objectReferenceValue as NetworkObject);
        }

        private void OnSceneGUI()
        {
            serializedObject.Update();

            // Add scenes to networkScenesPath
            networkScenesPaths.ClearArray();
            networkScenesPaths.InsertArrayElementAtIndex(0);
            networkScenesPaths.GetArrayElementAtIndex(0).stringValue = "DontDestroyOnLoad";
            int i;
            for ( i = 0; i < registeredScenes.arraySize; i++)
            {
                networkScenesPaths.InsertArrayElementAtIndex(i + 1);
                Object sceneAsset = registeredScenes.GetArrayElementAtIndex(i).objectReferenceValue;
                if (sceneAsset != null)
                {
                    string path = AssetDatabase.GetAssetPath(sceneAsset);
                    networkScenesPaths.GetArrayElementAtIndex(i + 1).stringValue = path;
                }
                else
                    networkScenesPaths.GetArrayElementAtIndex(i + 1).stringValue = "";
            }

            // Set regId to register prefabs
            for ( i = 0 ; i < registeredPrefabs.arraySize; i++)
            {
                if (registeredPrefabsPrev.Count < registeredPrefabs.arraySize)
                    registeredPrefabsPrev.Add(null);
                NetworkObject no = registeredPrefabs.GetArrayElementAtIndex(i).objectReferenceValue as NetworkObject;
                if(no != null)
                {
                    if (!PrefabUtility.IsPartOfPrefabAsset(no.gameObject))
                    {
                        registeredPrefabs.GetArrayElementAtIndex(i).objectReferenceValue = null;
                        registeredPrefabsPrev[i] = null;
                        Debug.LogError("Only prefab can be registered");
                    }
                    else if (no.regId != -1 && no.regId != i)
                    {
                        registeredPrefabs.GetArrayElementAtIndex(i).objectReferenceValue = null;
                        registeredPrefabsPrev[i] = null;
                        Debug.LogError("Prefab is already registered");
                    }
                    else
                        SerializeNetworkObjectPrefab(new SerializedObject(no), i);

                    if(registeredPrefabsPrev[i] != no)
                    {
                        if (registeredPrefabsPrev[i] != null)
                            SerializeNetworkObjectPrefab(new SerializedObject(registeredPrefabsPrev[i]), -1);
                        registeredPrefabsPrev[i] = no;
                    }
                }
            }
            for(; i < registeredPrefabsPrev.Count;)
            {
                if (registeredPrefabsPrev[i] != null)
                    SerializeNetworkObjectPrefab(new SerializedObject(registeredPrefabsPrev[i]), -1);
                registeredPrefabsPrev.RemoveAt(i);
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
