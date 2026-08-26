using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Threading;

#if UNITY_EDITOR
namespace Network
{
    public class NetworkSceneEditor
    {
        private static NetworkSceneEditor instance;
        public static NetworkSceneEditor Instance { get { return instance; } }

        private static void OnSceneSaving(Scene scene, string path)
        {
            int counter = 0;
            foreach (NetworkObject inst in NetworkSceneManager.FindObjectsOfType<NetworkObject>(scene))
            {
                SerializedObject so = new SerializedObject(inst);
                so.FindProperty("scenePos").intValue = counter;
                so.FindProperty("isScenePlaced").boolValue = true;
                so.ApplyModifiedProperties();
                counter++;
            }
        }

        public static void Instantiate()
        {
            NetworkSceneEditor inst = new NetworkSceneEditor();
            NetworkSceneEditor result = Interlocked.CompareExchange(ref instance, inst, null);
            if(result == null && !EditorApplication.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += OnSceneSaving;
        }
    }
}
#endif