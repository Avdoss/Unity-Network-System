using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using Unity.VisualScripting;
using System.Security.Cryptography;
using System;

#if UNITY_EDITOR
namespace Network
{
    [CustomEditor(typeof(NetworkAnimator))]
    public class NetworkAnimatorEditor : Editor
    {
        Animator animator;
        SerializedProperty parametersInterpolation;
        SerializedProperty parametersList;
        SerializedProperty hashCode;

        private void OnEnable()
        {
            animator = target.GetComponent<Animator>();
            parametersInterpolation = serializedObject.FindProperty("parametersInterpolationInfo");
            parametersList = parametersInterpolation.FindPropertyRelative("parameters");
            hashCode = parametersInterpolation.FindPropertyRelative("hashCode");

            serializedObject.Update();
            int newHash = GetParametersHash();
            if (hashCode.intValue != newHash)
            {
                parametersList.ClearArray();
                int counter = 0;
                foreach (var param in animator.parameters)
                {
                    parametersList.InsertArrayElementAtIndex(counter);
                    SerializedProperty paramInfo = parametersList.GetArrayElementAtIndex(counter);
                    paramInfo.FindPropertyRelative("paramType").intValue = (int)param.type;
                    paramInfo.FindPropertyRelative("name").stringValue = param.name;
                    paramInfo.FindPropertyRelative("method").intValue = (int)NetworkAnimatorParameter.InterpolateMethod.NONE;
                    paramInfo.FindPropertyRelative("correctionSpeed").floatValue = 5.0f;
                    counter++;
                }
                hashCode.intValue = newHash;
            }
            serializedObject.ApplyModifiedProperties();
        }

        private int GetParametersHash()
        {
            int hashCode = 1234567891;
            foreach (var param in animator.parameters)
                hashCode = hashCode * 41 ^ param.name.GetHashCode() * 13 ^ (int)param.type;
            return hashCode;
        }
    }
}
#endif