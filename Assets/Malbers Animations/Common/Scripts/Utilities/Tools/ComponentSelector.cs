using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations
{
    [AddComponentMenu("Malbers/Utilities/Tools/Component Selector")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/utilities/component-selector")]
    public class ComponentSelector : MonoBehaviour
    {
        public List<ComponentSet> internalComponents;
        public bool edit = true;

        public ComponentSet this[int index] => internalComponents[index];


        [ContextMenu("Show|Hide Editor")]
        private void ShowHideEditor()
        {
            edit ^= true;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        private void Reset()
        {
            internalComponents = new List<ComponentSet>();
        }

    }

    [System.Serializable]
    public class ComponentSet
    {
        public string name = "Description Here";
        [TextArea] public string tooltip;
        public bool active = true;
        public GameObject[] gameObjects;
        public MonoBehaviour[] monoBehaviours;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ComponentSelector))]
    public class SelectComponentsEditor : Editor
    {
        private static GUIContent _icon_Add;
        public static GUIContent Icon_Add
        {
            get
            {
                if (_icon_Add == null)
                {
                    _icon_Add = EditorGUIUtility.IconContent("d_ViewToolOrbit", "Enable/Disable");
                    _icon_Add.tooltip = "Enable/Disable";
                }

                return _icon_Add;
            }
        }


        SerializedProperty internalComponents, edit;
        ComponentSelector M;
        ReorderableList ReoInternalComponents;

        private void OnEnable()
        {
            M = (ComponentSelector)target;
            internalComponents = serializedObject.FindProperty("internalComponents");
            edit = serializedObject.FindProperty("edit");
            _flowBtnLeft = null; _flowBtnRight = null;


            ReoInternalComponents = new ReorderableList(serializedObject, internalComponents)
            {
                drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
                {
                    var element = internalComponents.GetArrayElementAtIndex(index);
                    var active = element.FindPropertyRelative("active");
                    var name = element.FindPropertyRelative("name");

                    var activeRect1 = new Rect(rect.x, rect.y - 1, 20, rect.height);
                    var IDRect = new Rect(rect.x + 20, rect.y, rect.width - 20, EditorGUIUtility.singleLineHeight);


                    active.boolValue = EditorGUI.Toggle(activeRect1, GUIContent.none, active.boolValue);
                    EditorGUI.PropertyField(IDRect, name, GUIContent.none);

                },
                drawHeaderCallback = (Rect rect) =>
                {
                    var r = new Rect(rect) { x = rect.x + 30, width = 60 };
                    var a = new Rect(rect) { width = 65 };

                    EditorGUI.LabelField(a, new GUIContent("Act", "Is the Component Selection ON or OFF"));
                    EditorGUI.LabelField(r, new GUIContent("Name", "Name of the Button"));
                },
                onAddCallback = (ReorderableList list) =>
                {
                    M.internalComponents.Add(new ComponentSet());
                    EditorUtility.SetDirty(M);
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (edit.boolValue)
            {
                ReoInternalComponents.DoLayoutList();

                if (ReoInternalComponents.index != -1)
                {
                    var elem = internalComponents.GetArrayElementAtIndex(ReoInternalComponents.index);

                    var gos = elem.FindPropertyRelative("gameObjects");
                    var monoBehaviours = elem.FindPropertyRelative("monoBehaviours");
                    var tooltip = elem.FindPropertyRelative("tooltip");
                    EditorGUILayout.PropertyField(gos, true);
                    EditorGUILayout.PropertyField(monoBehaviours, true);
                    EditorGUILayout.Space();

                    EditorGUILayout.PropertyField(tooltip);
                }
            }
            else
            {
                if (internalComponents.arraySize > 0)
                    DrawFlowButtons(); // MWC: flow/wrap layout replaces fixed 2-column
            }
            serializedObject.ApplyModifiedProperties();
        }

        // MWC: flow layout constants — eye icon embedded on the right of each name button
        private const float EyeButtonWidth = 24f;
        private const float FlowPadding = 3f;
        private const float FlowRowHeight = 25f;   // MWC: taller button height (+4 from user request)
        private const float MinNameWidth = 40f;
        private const float TextPadding = 10f;    // MWC: 3 units each side

        private static GUIStyle _flowBtnLeft;
        private static GUIStyle _flowBtnRight;

        // MWC: drag-and-drop reorder state
        private int  _dragIndex  = -1;
        private int  _dropIndex  = -1;
        private bool _isDragging = false;

        // MWC: fixedHeight = 0 lets the Rect drive the actual button height instead of the miniButton default (16 px)
        private static GUIStyle FlowBtnLeft => _flowBtnLeft ??= new GUIStyle(EditorStyles.miniButtonLeft) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 0 };
        private static GUIStyle FlowBtnRight => _flowBtnRight ??= new GUIStyle(EditorStyles.miniButtonRight) { fontSize = 13, fixedHeight = 0 };

        private float GetFlowButtonWidth(string label)
        {
            float calcWidth = FlowBtnLeft.CalcSize(new GUIContent(label)).x + TextPadding; // MWC: +3 px each side
            return Mathf.Max(calcWidth, MinNameWidth) + EyeButtonWidth;
        }

        // MWC: flow layout with drag-to-reorder; drop indicator shown as a blue vertical bar
        private void DrawFlowButtons()
        {
            int   count          = internalComponents.arraySize;
            float availableWidth = EditorGUIUtility.currentViewWidth - 22f;
            Event evt            = Event.current;

            // Build per-button widths
            var bws = new float[count];
            for (int i = 0; i < count; i++)
                bws[i] = GetFlowButtonWidth(internalComponents.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);

            // Count rows to reserve the correct total height
            int rows = 1; float rx = 0;
            for (int i = 0; i < count; i++)
            {
                if (rx > 0 && rx + bws[i] > availableWidth) { rx = 0; rows++; }
                rx += bws[i] + FlowPadding;
            }

            Rect area = GUILayoutUtility.GetRect(availableWidth, rows * FlowRowHeight + (rows - 1) * FlowPadding);

            // Compute final rects for every button
            var nameRects = new Rect[count];
            var eyeRects  = new Rect[count];
            float x = 0, y = 0;
            for (int i = 0; i < count; i++)
            {
                if (x > 0 && x + bws[i] > availableWidth) { x = 0; y += FlowRowHeight + FlowPadding; }
                float nw     = bws[i] - EyeButtonWidth;
                nameRects[i] = new Rect(area.x + x, area.y + y, nw,            FlowRowHeight);
                eyeRects[i]  = new Rect(nameRects[i].xMax, area.y + y, EyeButtonWidth, FlowRowHeight);
                x += bws[i] + FlowPadding;
            }

            // ── Drag state machine ──────────────────────────────────────────────────
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    if (FullRect(nameRects[i], eyeRects[i]).Contains(evt.mousePosition))
                    { _dragIndex = i; _dropIndex = i; _isDragging = false; break; }
                }
            }
            else if (evt.type == EventType.MouseDrag && _dragIndex >= 0)
            {
                _isDragging = true;
                _dropIndex  = NearestDropIndex(evt.mousePosition, nameRects, eyeRects, count);
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                if (_isDragging && _dragIndex >= 0 && _dropIndex != _dragIndex && _dropIndex != _dragIndex + 1)
                {
                    int dst = _dropIndex > _dragIndex ? _dropIndex - 1 : _dropIndex;
                    internalComponents.MoveArrayElement(_dragIndex, dst);
                    serializedObject.ApplyModifiedProperties();
                    evt.Use();
                    Repaint();
                }
                _dragIndex = -1; _dropIndex = -1; _isDragging = false;
            }

            // ── Draw buttons ────────────────────────────────────────────────────────
            for (int i = 0; i < count; i++)
            {
                var element    = internalComponents.GetArrayElementAtIndex(i);
                var nameProp   = element.FindPropertyRelative("name");
                var tipProp    = element.FindPropertyRelative("tooltip");
                var activeProp = element.FindPropertyRelative("active");

                bool beingDragged = _isDragging && i == _dragIndex;
                var  saved        = GUI.color;
                if (beingDragged) GUI.color = new Color(saved.r, saved.g, saved.b, 0.35f);

                if (!_isDragging)
                {
                    // Normal interactive mode
                    if (GUI.Button(nameRects[i], new GUIContent(nameProp.stringValue, tipProp.stringValue), FlowBtnLeft))
                    {
                        if (M[i].gameObjects.Length > 0)
                            Selection.objects = M[i].gameObjects;
                        else if (M[i].monoBehaviours.Length > 0)
                            Selection.objects = new Object[1] { M[i].monoBehaviours[0].gameObject };
                    }
                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        GUI.color = activeProp.boolValue ? (saved + Color.green) / 2 : (saved + Color.black) / 2;
                        bool newVal = GUI.Toggle(eyeRects[i], activeProp.boolValue, Icon_Add, FlowBtnRight);
                        GUI.color = saved;
                        if (cc.changed)
                        {
                            activeProp.boolValue = newVal;
                            foreach (var item in M[i].gameObjects)
                                if (item) { item.SetActive(newVal); EditorUtility.SetDirty(item); }
                            foreach (var item in M[i].monoBehaviours)
                                if (item) { item.enabled = newVal; EditorUtility.SetDirty(item); }
                            Undo.RecordObject(target, "Component Selector");
                        }
                    }
                }
                else
                {
                    // Drag mode: render visuals only, no click interaction
                    GUI.Label(nameRects[i], new GUIContent(nameProp.stringValue, tipProp.stringValue), FlowBtnLeft);
                    GUI.color = beingDragged
                        ? new Color(0, 0, 0, 0.35f)
                        : (activeProp.boolValue ? (saved + Color.green) / 2 : (saved + Color.black) / 2);
                    GUI.Label(eyeRects[i], Icon_Add, FlowBtnRight);
                }

                GUI.color = saved;
            }

            // ── Drop indicator: blue vertical bar in the gap before _dropIndex ──────
            if (_isDragging && _dropIndex >= 0 && _dropIndex != _dragIndex && _dropIndex != _dragIndex + 1)
                EditorGUI.DrawRect(DropIndicatorRect(_dropIndex, nameRects, eyeRects, count),
                    new Color(0.25f, 0.65f, 1f, 0.9f));
        }

        // Rect spanning the full button (name + eye)
        private static Rect FullRect(Rect nr, Rect er) =>
            new(nr.x, nr.y, nr.width + er.width, nr.height);

        // MWC: returns insert-before index (0..count) nearest to the mouse position
        private static int NearestDropIndex(Vector2 mouse, Rect[] nameRects, Rect[] eyeRects, int count)
        {
            int best = count; float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Rect  f    = FullRect(nameRects[i], eyeRects[i]);
                float dist = Vector2.Distance(mouse, new Vector2(f.x + f.width * .5f, f.y + f.height * .5f));
                if (dist < bestDist) { bestDist = dist; best = mouse.x <= f.x + f.width * .5f ? i : i + 1; }
            }
            return best;
        }

        // MWC: 3-px bar placed in the FlowPadding gap before dropIndex
        private static Rect DropIndicatorRect(int dropIndex, Rect[] nameRects, Rect[] eyeRects, int count)
        {
            const float w = 3f;
            if (dropIndex >= count)
            {
                Rect last = FullRect(nameRects[count - 1], eyeRects[count - 1]);
                return new Rect(last.xMax + FlowPadding * .5f - w * .5f, last.y, w, last.height);
            }
            Rect r = FullRect(nameRects[dropIndex], eyeRects[dropIndex]);
            return new Rect(r.x - FlowPadding * .5f - w * .5f, r.y, w, r.height);
        }
    }
#endif
}