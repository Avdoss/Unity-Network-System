using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Network;
using static Network.NetworkAnimator;


[CustomPropertyDrawer(typeof(NetworkAnimator.ParametersInterpolationInfo))]
public class ParametersInterpolationInfoDrawer : PropertyDrawer
{
    private static readonly float ITEM_HEIGHT = 20.0f;
    private static readonly float CHILD_OFFSET_WIDTH = 30.0f;
    private static readonly float SPACE = 10.0f;
    private float offset;
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty parametersList = property.FindPropertyRelative("parameters");
        EditorGUI.BeginProperty(position, label, property);
        position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
        var indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        offset = ITEM_HEIGHT;

        int size = parametersList.arraySize;
        for (int i = 0; i < size; i++)
        {
            SerializedProperty paramInfo = parametersList.GetArrayElementAtIndex(i);

            var typeRect = new Rect(CHILD_OFFSET_WIDTH, position.y + offset, position.width / 4.0f, ITEM_HEIGHT);
            EditorGUI.LabelField(typeRect, ((AnimatorControllerParameterType)paramInfo.FindPropertyRelative("paramType").intValue).ToString());

            var nameRect = new Rect(CHILD_OFFSET_WIDTH + position.width / 4.0f + SPACE, position.y + offset, position.width / 4.0f, ITEM_HEIGHT);
            EditorGUI.LabelField(nameRect, paramInfo.FindPropertyRelative("name").stringValue);

            SerializedProperty method = paramInfo.FindPropertyRelative("method");
            var methodRect = new Rect(CHILD_OFFSET_WIDTH + (position.width / 4.0f + SPACE) * 2, position.y + offset, position.width / 4.0f, ITEM_HEIGHT);
            EditorGUI.PropertyField(methodRect, method, GUIContent.none);

            NetworkAnimatorParameter.InterpolateMethod method_enum = (NetworkAnimatorParameter.InterpolateMethod)method.intValue;
            if (method_enum == NetworkAnimatorParameter.InterpolateMethod.LINEAR || method_enum == NetworkAnimatorParameter.InterpolateMethod.ACCELERATED)
            {
                if ((AnimatorControllerParameterType)paramInfo.FindPropertyRelative("paramType").intValue == AnimatorControllerParameterType.Float)
                {
                    var correctionRect = new Rect(CHILD_OFFSET_WIDTH + (position.width / 4.0f + SPACE) * 3, position.y + offset, position.width / 4.0f, ITEM_HEIGHT);
                    EditorGUI.PropertyField(correctionRect, paramInfo.FindPropertyRelative("correctionSpeed"), GUIContent.none);
                }
                else
                    method.intValue = (int)NetworkAnimatorParameter.InterpolateMethod.TIME_SYNC_WITHOUT_INTERPOLATION;
            }
            offset += ITEM_HEIGHT;
        }
        EditorGUI.indentLevel = indent;
        EditorGUI.EndProperty();
    }
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return offset;
    }
}


