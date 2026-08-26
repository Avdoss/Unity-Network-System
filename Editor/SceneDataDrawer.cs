/*using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

//[Serializable]
//public class SceneData
//{
//    public bool enable;
//    public SceneAsset[] networkScenes;
//    public NetworkObject[] networkObjects;
//}

[CustomPropertyDrawer(typeof(SceneData))]
public class SceneDataDrawer : PropertyDrawer
{
    private static readonly float ITEM_HEIGHT = 20.0f;
    private static readonly float CHILD_OFFSET = 50.0f;
    private float offset;
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        bool enable = property.FindPropertyRelative("enable").boolValue;
        SerializedProperty networkScenesProp = property.FindPropertyRelative("networkScenes");
        SerializedProperty networkObjectsProp = property.FindPropertyRelative("networkObjects");

        float width = position.width;
        EditorGUI.BeginProperty(position, label, property);
        position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

        var indent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        var enableRect = new Rect(position.x, position.y, 20, ITEM_HEIGHT);
        EditorGUI.PropertyField(enableRect, property.FindPropertyRelative("enable"), GUIContent.none);

        offset = ITEM_HEIGHT;
        if (enable)
        {
            var scenesRect = new Rect(CHILD_OFFSET, position.y + offset, width- CHILD_OFFSET, position.height);
            EditorGUI.PropertyField(scenesRect, networkScenesProp);
            offset += networkScenesProp.isExpanded ? 77 + (networkScenesProp.arraySize != 0 ? (networkScenesProp.arraySize - 1) * ITEM_HEIGHT : 0) : ITEM_HEIGHT;
            var objectsRect = new Rect(CHILD_OFFSET, position.y + offset, width- CHILD_OFFSET, position.height);
            EditorGUI.PropertyField(objectsRect, networkObjectsProp);
            offset += networkObjectsProp.isExpanded ? 70 + (networkObjectsProp.arraySize != 0 ? (networkObjectsProp.arraySize - 1) * ITEM_HEIGHT : 0) : ITEM_HEIGHT;
        }
        EditorGUI.indentLevel = indent;
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return offset;
    }
}*/

