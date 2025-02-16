using SaveLoadSystem.Utility.NonReset;
using UnityEditor;
using UnityEngine;

namespace SaveLoadSystem.Utility.PreventReset.Editor
{
    [CustomPropertyDrawer(typeof(NonResetable<>))]
    public class NonResetableDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            
            var valueProperty = property.FindPropertyRelative(nameof(NonResetable<bool>.value));
            EditorGUI.PropertyField(position, valueProperty, label);

            EditorGUI.EndProperty();
        }
    }
}
