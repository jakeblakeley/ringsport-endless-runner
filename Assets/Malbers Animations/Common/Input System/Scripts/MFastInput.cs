using MalbersAnimations.Events;
using UnityEngine;
using UnityEngine.Events;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations.InputSystem
{
#if ENABLE_INPUT_SYSTEM
    [System.Serializable]
    public struct FastInput
    {
        public string name;
        public bool debug;
        public InputAction input;
        [Space]
        public UnityEvent OnInputDown;
        public UnityEvent OnInputUp;
        public BoolEvent OnInputPressed;

        public readonly void InputAction(InputAction.CallbackContext context)
        {
            if (context.started || context.performed)
            {
                OnInputDown.Invoke();
                OnInputPressed.Invoke(true);

                if (debug) Debug.Log($"Input: {name} Pressed");

            }
            else if (context.canceled)
            {
                OnInputUp.Invoke();
                OnInputPressed.Invoke(false);

                if (debug) Debug.Log($"Input: {name} Released");
            }
        }
    }

    [AddComponentMenu("Malbers/Input/Fast Input")]
    public class MFastInput : MonoBehaviour
    {
        public FastInput[] inputs;

        private void OnEnable()
        {
            if (inputs != null || inputs.Length > 0)
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i].input.Enable();
                    inputs[i].input.started += inputs[i].InputAction;
                    inputs[i].input.canceled += inputs[i].InputAction;

                    if (inputs[i].OnInputPressed == null) inputs[i].OnInputPressed = new();
                    if (inputs[i].OnInputDown == null) inputs[i].OnInputDown = new();
                    if (inputs[i].OnInputUp == null) inputs[i].OnInputUp = new();
                }
            }
        }

        private void OnDisable()
        {
            if (inputs != null || inputs.Length > 0)
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i].input.started -= inputs[i].InputAction;
                    inputs[i].input.canceled -= inputs[i].InputAction;
                    inputs[i].input.Disable();
                }
            }
        }

        private void Reset()
        {
            //Create a new InputAction with Key 'Space' and add it to the list #inputs
            if (inputs == null || inputs.Length == 0)
            {
                inputs = new FastInput[1];
                inputs[0] = new FastInput
                {
                    name = "Space",
                    input = new InputAction("Space", InputActionType.Button, "<Keyboard>/space")
                };
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MFastInput))]
    public class MFastInputEditor : Editor
    {
        private ReorderableList inputList;
        private SerializedProperty inputs;

        private void OnEnable()
        {
            inputs = serializedObject.FindProperty("inputs");
            BuildList();
        }

        private void BuildList()
        {
            // displayRemoveButton = false: we draw a per-element [x] button instead
            inputList = new ReorderableList(serializedObject, inputs, true, true, true, false)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Input Name"),

                elementHeightCallback = index =>
                {
                    if (index >= inputs.arraySize) return EditorGUIUtility.singleLineHeight + 6;
                    var inputProp = inputs.GetArrayElementAtIndex(index).FindPropertyRelative("input");
                    float inputH = EditorGUI.GetPropertyHeight(inputProp, GUIContent.none, true);
                    return EditorGUIUtility.singleLineHeight               // row 1
                         + EditorGUIUtility.standardVerticalSpacing        // gap
                         + inputH                                          // row 2 (actual)
                         + EditorGUIUtility.standardVerticalSpacing * 3;  // top + bottom pad
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index >= inputs.arraySize) return;

                    var element   = inputs.GetArrayElementAtIndex(index);
                    var nameProp  = element.FindPropertyRelative("name");
                    var debugProp = element.FindPropertyRelative("debug");
                    var inputProp = element.FindPropertyRelative("input");

                    float pad     = EditorGUIUtility.standardVerticalSpacing;
                    float lineH   = EditorGUIUtility.singleLineHeight;
                    const float debugW  = 28f;
                    const float removeW = 20f;
                    float nameW   = rect.width - debugW - removeW - 6;

                    float y = rect.y + pad;

                    // Row 1: [Name ............][Debug][X]
                    var nameRect   = new Rect(rect.x,                      y, nameW,   lineH);
                    var debugRect  = new Rect(rect.x + nameW + 2,          y, debugW,  lineH);
                    var removeRect = new Rect(rect.x + nameW + debugW + 4, y, removeW, lineH);

                    EditorGUI.PropertyField(nameRect, nameProp, GUIContent.none);
                    MalbersEditor.DrawDebugIcon(debugRect, debugProp);

                    if (GUI.Button(removeRect, EditorGUIUtility.IconContent("d_TreeEditor.Trash"), EditorStyles.miniButtonRight))
                    {
                        inputs.DeleteArrayElementAtIndex(index);
                        serializedObject.ApplyModifiedProperties();
                        return;
                    }

                    // Row 2: [---- InputAction ----]
                    y += lineH + pad;
                    float inputH  = EditorGUI.GetPropertyHeight(inputProp, GUIContent.none, true);
                    var inputRect = new Rect(rect.x, y, rect.width, inputH);
                    EditorGUI.PropertyField(inputRect, inputProp, GUIContent.none);
                },
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            inputList.DoLayoutList();

            int index = inputList.index;
            if (index >= 0 && index < inputs.arraySize)
            {
                var element   = inputs.GetArrayElementAtIndex(index);
                var onDown    = element.FindPropertyRelative("OnInputDown");
                var onUp      = element.FindPropertyRelative("OnInputUp");
                var onPressed = element.FindPropertyRelative("OnInputPressed");

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var name = element.FindPropertyRelative("name").stringValue;
                    onDown.isExpanded = MalbersEditor.Foldout(onDown.isExpanded, $"Events  [{name}]");

                    if (onDown.isExpanded)
                    {
                        EditorGUILayout.PropertyField(onDown);
                        EditorGUILayout.PropertyField(onUp);
                        EditorGUILayout.PropertyField(onPressed);
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif

#endif
}
