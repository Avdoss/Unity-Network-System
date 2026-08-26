using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;


[CustomPropertyDrawer(typeof(Network.AsyncData))]
public class AsyncDataDrawer : PropertyDrawer
{
    private static readonly float ITEM_HEIGHT = 20.0f;
    private static readonly float CHILD_OFFSET = 35.0f;
    private float offset;
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        bool enable = property.FindPropertyRelative("enable").boolValue;
        EditorGUI.BeginProperty(position, label, property);
        position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
        var indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        var enableRect = new Rect(position.x, position.y, 20, ITEM_HEIGHT);
        EditorGUI.PropertyField(enableRect, property.FindPropertyRelative("enable"), GUIContent.none);

        offset = ITEM_HEIGHT;
        if (enable)
        {
            var jobsLabelRect = new Rect(CHILD_OFFSET, position.y + offset, position.width, ITEM_HEIGHT);
            EditorGUI.LabelField(jobsLabelRect, "Jobs");
            var jobsRect = new Rect(position.x, position.y + offset, position.width, ITEM_HEIGHT);
            EditorGUI.PropertyField(jobsRect, property.FindPropertyRelative("jobs"), GUIContent.none);
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

