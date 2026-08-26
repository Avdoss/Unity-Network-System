using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Network;


[CustomPropertyDrawer(typeof(Network.NetworkTransform.InterpolateData))]
public class InterpolateDataDrawer : PropertyDrawer
{
    private static readonly float ITEM_HEIGHT = 20.0f;
    private static readonly float CHILD_OFFSET = 35.0f;
    private float offset;
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        NetworkTransform.INTERPOLATION_METHOD method = (NetworkTransform.INTERPOLATION_METHOD)property.FindPropertyRelative("method").enumValueIndex;
        EditorGUI.BeginProperty(position, label, property);
        position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
        var indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        var methodRect = new Rect(position.x, position.y, position.width, ITEM_HEIGHT);
        EditorGUI.PropertyField(methodRect, property.FindPropertyRelative("method"), GUIContent.none);

        offset = ITEM_HEIGHT;
        if (method != NetworkTransform.INTERPOLATION_METHOD.NONE)
        {
            var posCorrectLabelRect = new Rect(CHILD_OFFSET, position.y + offset, position.width, ITEM_HEIGHT);
            EditorGUI.LabelField(posCorrectLabelRect, "Pos correction speed");
            var posCorrectRect = new Rect(position.x, position.y + offset, position.width, ITEM_HEIGHT);
            EditorGUI.PropertyField(posCorrectRect, property.FindPropertyRelative("posCorrectionSpeed"), GUIContent.none);
            offset += ITEM_HEIGHT;
            var rotCorrectLabelRect = new Rect(CHILD_OFFSET, position.y + offset, position.width, ITEM_HEIGHT);
            EditorGUI.LabelField(rotCorrectLabelRect, "Rot correction speed");
            var rotCorrectRect = new Rect(position.x, position.y + offset, position.width, ITEM_HEIGHT);
            EditorGUI.PropertyField(rotCorrectRect, property.FindPropertyRelative("rotCorrectionSpeed"), GUIContent.none);
            offset += ITEM_HEIGHT;
            var scaleCorrectLabelRect = new Rect(CHILD_OFFSET, position.y + offset, position.width, ITEM_HEIGHT);
            EditorGUI.LabelField(scaleCorrectLabelRect, "Scale correction speed");
            var scaleCorrectRect = new Rect(position.x, position.y + offset, position.width, ITEM_HEIGHT);
            EditorGUI.PropertyField(scaleCorrectRect, property.FindPropertyRelative("scaleCorrectionSpeed"), GUIContent.none);
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

