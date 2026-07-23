using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [Serializable]
    public class MemberValueSetter
    {
        [TypeFilter(typeof(Component))]
        public SerializableType targetType;

        public string memberName;
        public float setValue;
        public int setValueInt;
        public bool setValueBool;
        public string setValueString; // MWC – string value storage
        public Object setValueObject; // MWC – UnityEngine.Object reference (Transform, GameObject, ScriptableObject, …)

        // ─── Runtime cache (not serialized) ──────────────────────────────────────

        private Component _resolvedTarget;
        private PropertyInfo _cachedProperty;
        private FieldInfo _cachedField;
        private MethodInfo _cachedMethod;
        private string _cachedMemberName;
        private bool _cachedIsBool;
        private bool _cachedIsInt;
        private bool _cachedIsString; // MWC – string cache flag
        private bool _cachedIsObject; // MWC – UnityEngine.Object cache flag
        private bool _cachedIsVoid;   // MWC – parameterless void method (action trigger) cache flag

        // ─── Public API ───────────────────────────────────────────────────────────

        public void SetTarget(Object target)
        {
            _resolvedTarget = null;
            _cachedProperty = null;
            _cachedField = null;
            _cachedMethod = null;
            _cachedMemberName = null;

            if (target == null || targetType?.Type == null) return;

            var go = (target as GameObject) ?? (target as Component)?.gameObject;
            if (go == null) return;

            _resolvedTarget = go.FindComponent(targetType.Type);
            RebuildMemberCache();
        }

        public void Apply()
        {
            if (_resolvedTarget == null) return;

            if (_cachedMemberName != memberName) RebuildMemberCache();

            if (_cachedProperty == null && _cachedField == null && _cachedMethod == null) return;

            // MWC – parameterless void method: just invoke it, there is no value to set
            if (_cachedIsVoid) { _cachedMethod.Invoke(_resolvedTarget, null); return; }

            object value = _cachedIsBool ? setValueBool
                         : _cachedIsInt ? setValueInt
                         : _cachedIsString ? setValueString // MWC – string branch
                         : _cachedIsObject ? setValueObject         // MWC – Object branch
                         : Convert.ChangeType(setValue, GetTargetType());

            if (_cachedProperty != null) _cachedProperty.SetValue(_resolvedTarget, value);
            else if (_cachedField != null) _cachedField.SetValue(_resolvedTarget, value);
            else _cachedMethod.Invoke(_resolvedTarget, new[] { value });
        }

        public void Apply(Object target)
        {
            SetTarget(target);
            Apply();
        }

        public string Description()
        {
            string typeName = targetType?.Type?.Name ?? "?";
            string memberText = string.IsNullOrEmpty(memberName) ? "?" : memberName;

            if (IsBoolMember()) return $"{typeName}.{memberText} = {setValueBool}";
            if (IsIntMember()) return $"{typeName}.{memberText} = {setValueInt}";
            if (IsStringMember()) return $"{typeName}.{memberText} = \"{setValueString}\""; // MWC – string description
            if (IsObjectMember()) return $"{typeName}.{memberText} = {(setValueObject != null ? setValueObject.name : "null")}"; // MWC – Object description
            if (IsVoidMember()) return $"{typeName}.{memberText}()"; // MWC – parameterless void method description
            return $"{typeName}.{memberText} = {setValue}";
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private void RebuildMemberCache()
        {
            _cachedProperty = null;
            _cachedField = null;
            _cachedMethod = null;
            _cachedMemberName = memberName;
            _cachedIsBool = false;
            _cachedIsInt = false;
            _cachedIsString = false; // MWC – reset string flag
            _cachedIsObject = false; // MWC – reset Object flag
            _cachedIsVoid = false;   // MWC – reset void flag

            if (_resolvedTarget == null || string.IsNullOrEmpty(memberName) || targetType?.Type == null) return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite && (MemberValueCompare.IsSupported(prop.PropertyType) || MemberValueCompare.IsObjectType(prop.PropertyType))) // MWC – include Object types
            {
                _cachedProperty = prop;
                _cachedIsBool = prop.PropertyType == typeof(bool);
                _cachedIsInt = IsIntType(prop.PropertyType);
                _cachedIsString = prop.PropertyType == typeof(string); // MWC
                _cachedIsObject = MemberValueCompare.IsObjectType(prop.PropertyType); // MWC
                return;
            }

            var field = type.GetField(memberName, flags);
            if (field != null && !field.IsInitOnly && !field.IsLiteral && (MemberValueCompare.IsSupported(field.FieldType) || MemberValueCompare.IsObjectType(field.FieldType))) // MWC – include Object types
            {
                _cachedField = field;
                _cachedIsBool = field.FieldType == typeof(bool);
                _cachedIsInt = IsIntType(field.FieldType);
                _cachedIsString = field.FieldType == typeof(string); // MWC
                _cachedIsObject = MemberValueCompare.IsObjectType(field.FieldType); // MWC
                return;
            }

            foreach (var method in type.GetMethods(flags))
            {
                if (method.IsSpecialName) continue;
                if (method.Name != memberName) continue;
                if (method.ReturnType != typeof(void)) continue;
                var parms = method.GetParameters();
                if (parms.Length != 1) continue;
                if (!MemberValueCompare.IsSupported(parms[0].ParameterType) && !MemberValueCompare.IsObjectType(parms[0].ParameterType)) continue; // MWC – include Object types
                _cachedMethod = method;
                _cachedIsBool = parms[0].ParameterType == typeof(bool);
                _cachedIsInt = IsIntType(parms[0].ParameterType);
                _cachedIsString = parms[0].ParameterType == typeof(string); // MWC
                _cachedIsObject = MemberValueCompare.IsObjectType(parms[0].ParameterType); // MWC
                return;
            }

            // MWC – fall back to a parameterless void method (action trigger). Value-bearing
            // members above take priority, so existing setters keep their behaviour.
            var voidMethod = type.GetMethod(memberName, flags, null, Type.EmptyTypes, null);
            if (voidMethod != null && !voidMethod.IsSpecialName && voidMethod.ReturnType == typeof(void))
            {
                _cachedMethod = voidMethod;
                _cachedIsVoid = true;
            }
        }

        private Type GetTargetType()
        {
            if (_cachedProperty != null) return _cachedProperty.PropertyType;
            if (_cachedField != null) return _cachedField.FieldType;
            if (_cachedMethod != null) return _cachedMethod.GetParameters()[0].ParameterType;
            return typeof(float);
        }

        private bool IsBoolMember()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite) return prop.PropertyType == typeof(bool);

            var field = type.GetField(memberName, flags);
            if (field != null && !field.IsInitOnly) return field.FieldType == typeof(bool);

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != memberName) continue;
                if (m.ReturnType != typeof(void)) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && MemberValueCompare.IsSupported(p[0].ParameterType))
                    return p[0].ParameterType == typeof(bool);
            }

            return false;
        }

        private static bool IsIntType(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) ||
            t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) ||
            t == typeof(sbyte) || t == typeof(ushort);

        private bool IsIntMember()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite) return IsIntType(prop.PropertyType);

            var field = type.GetField(memberName, flags);
            if (field != null && !field.IsInitOnly) return IsIntType(field.FieldType);

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != memberName) continue;
                if (m.ReturnType != typeof(void)) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && MemberValueCompare.IsSupported(p[0].ParameterType))
                    return IsIntType(p[0].ParameterType);
            }

            return false;
        }

        // MWC – detects whether the selected member accepts a string
        private bool IsStringMember()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite) return prop.PropertyType == typeof(string);

            var field = type.GetField(memberName, flags);
            if (field != null && !field.IsInitOnly) return field.FieldType == typeof(string);

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != memberName) continue;
                if (m.ReturnType != typeof(void)) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && MemberValueCompare.IsSupported(p[0].ParameterType))
                    return p[0].ParameterType == typeof(string);
            }

            return false;
        }

        // MWC – detects whether the selected member accepts a UnityEngine.Object subtype
        private bool IsObjectMember()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite) return MemberValueCompare.IsObjectType(prop.PropertyType);

            var field = type.GetField(memberName, flags);
            if (field != null && !field.IsInitOnly) return MemberValueCompare.IsObjectType(field.FieldType);

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != memberName) continue;
                if (m.ReturnType != typeof(void)) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && (MemberValueCompare.IsSupported(p[0].ParameterType) || MemberValueCompare.IsObjectType(p[0].ParameterType)))
                    return MemberValueCompare.IsObjectType(p[0].ParameterType);
            }

            return false;
        }

        // MWC – detects whether the selected member is a parameterless void method (action trigger)
        private bool IsVoidMember()
        {
            if (targetType?.Type == null || string.IsNullOrEmpty(memberName)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = targetType.Type;

            // Value-bearing members take priority and are reported by the other Is*Member helpers.
            if (type.GetProperty(memberName, flags) != null) return false;
            if (type.GetField(memberName, flags) != null) return false;

            var m = type.GetMethod(memberName, flags, null, Type.EmptyTypes, null);
            return m != null && !m.IsSpecialName && m.ReturnType == typeof(void);
        }
    }


#if UNITY_EDITOR

    // ─── Property Drawer ─────────────────────────────────────────────────────────

    [CustomPropertyDrawer(typeof(MemberValueSetter))]
    public class MemberValueSetterDrawer : PropertyDrawer
    {
        private const float ROW_H = 18f;
        private const float PAD = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => (ROW_H + PAD) * (IsVoidMember(property) ? 2f : 3f); // MWC – void methods need no value row

        // MWC – static resolver for the drawer: is the selected member a parameterless void method?
        private static bool IsVoidMember(SerializedProperty property)
        {
            var asmQnProp = property.FindPropertyRelative("targetType").FindPropertyRelative("assemblyQualifiedName");
            Type type = Type.GetType(asmQnProp.stringValue);
            if (type == null) return false;

            string name = property.FindPropertyRelative("memberName").stringValue;
            if (string.IsNullOrEmpty(name)) return false;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            if (type.GetProperty(name, flags) != null) return false;
            if (type.GetField(name, flags) != null) return false;

            var m = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
            return m != null && !m.IsSpecialName && m.ReturnType == typeof(void);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            Rect Row(int i) => new(position.x, position.y + i * (ROW_H + PAD), position.width, ROW_H);

            // Row 0 – Target Type
            EditorGUI.PropertyField(Row(0), property.FindPropertyRelative("targetType"), new GUIContent("Target Type"));

            // Row 1 – Member picker
            var memberProp = property.FindPropertyRelative("memberName");
            (bool isBool, bool isInt, bool isString, bool isObject, bool isVoid) = DrawMemberPicker(Row(1), property, memberProp); // MWC – added isObject, isVoid

            // MWC – parameterless void method: no value row to draw
            if (isVoid) return;

            // Row 2 – Set value
            if (isBool)
            {
                var boolProp = property.FindPropertyRelative("setValueBool");
                var row = Row(2);
                float lw = EditorGUIUtility.labelWidth;
                EditorGUI.LabelField(new Rect(row.x, row.y, lw, row.height), "Value");
                boolProp.boolValue = EditorGUI.Toggle(
                    new Rect(row.x + lw - 15, row.y, 28, row.height),
                    boolProp.boolValue);
            }
            else if (isInt)
            {
                EditorGUI.PropertyField(Row(2), property.FindPropertyRelative("setValueInt"), new GUIContent("Value"));
            }
            else if (isString)
            {
                EditorGUI.PropertyField(Row(2), property.FindPropertyRelative("setValueString"), new GUIContent("Value")); // MWC – string field
            }
            else if (isObject) // MWC – Object field with proper type constraint
            {
                Type objType = ResolveObjectMemberType(property, memberProp.stringValue);
                var objProp = property.FindPropertyRelative("setValueObject");
                EditorGUI.BeginChangeCheck();
                var picked = EditorGUI.ObjectField(Row(2), new GUIContent("Value"), objProp.objectReferenceValue, objType ?? typeof(Object), true);
                if (EditorGUI.EndChangeCheck()) objProp.objectReferenceValue = picked;
            }
            else
            {
                EditorGUI.PropertyField(Row(2), property.FindPropertyRelative("setValue"), new GUIContent("Value"));
            }
        }

        private static (bool isBool, bool isInt, bool isString, bool isObject, bool isVoid) DrawMemberPicker(Rect r, SerializedProperty parentProp, SerializedProperty memberProp) // MWC – added isObject, isVoid
        {
            float lw = EditorGUIUtility.labelWidth;
            var labelRect = new Rect(r.x, r.y, lw, r.height);
            var buttonRect = new Rect(r.x + lw, r.y, r.width - lw, r.height);

            EditorGUI.LabelField(labelRect, "Member");

            var asmQnProp = parentProp.FindPropertyRelative("targetType")
                                       .FindPropertyRelative("assemblyQualifiedName");
            Type type = Type.GetType(asmQnProp.stringValue);

            bool isBool = false, isInt = false, isString = false, isObject = false, isVoid = false; // MWC
            if (type != null && !string.IsNullOrEmpty(memberProp.stringValue))
                (isBool, isInt, isString, isObject, isVoid) = ResolveKind(type, memberProp.stringValue);

            string current = memberProp.stringValue;
            string display = string.IsNullOrEmpty(current) ? "Select Member…" : current;

            using (new EditorGUI.DisabledGroupScope(type == null))
            {
                if (EditorGUI.DropdownButton(buttonRect, new GUIContent(display), FocusType.Keyboard, EditorStyles.popup))
                {
                    if (type == null) return (isBool, isInt, isString, isObject, isVoid);

                    var capturedProp = memberProp;
                    var capturedSO = parentProp.serializedObject;

                    PopupWindow.Show(buttonRect, new MemberSetterPickerPopup(type, current, chosen =>
                    {
                        capturedSO.Update();
                        capturedProp.stringValue = chosen;
                        capturedSO.ApplyModifiedProperties();
                    }));
                }
            }

            return (isBool, isInt, isString, isObject, isVoid);
        }

        // MWC – resolves the exact UnityEngine.Object subtype of the selected member for the ObjectField constraint
        private static Type ResolveObjectMemberType(SerializedProperty property, string memberName)
        {
            var asmQnProp = property.FindPropertyRelative("targetType").FindPropertyRelative("assemblyQualifiedName");
            Type type = Type.GetType(asmQnProp.stringValue);
            if (type == null || string.IsNullOrEmpty(memberName)) return typeof(Object);

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            var prop = type.GetProperty(memberName, flags);
            if (prop != null && prop.CanWrite && MemberValueCompare.IsObjectType(prop.PropertyType)) return prop.PropertyType;

            var field = type.GetField(memberName, flags);
            if (field != null && MemberValueCompare.IsObjectType(field.FieldType)) return field.FieldType;

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != memberName || m.ReturnType != typeof(void)) continue;
                var parms = m.GetParameters();
                if (parms.Length == 1 && MemberValueCompare.IsObjectType(parms[0].ParameterType)) return parms[0].ParameterType;
            }

            return typeof(Object);
        }

        // MWC – added isObject and isVoid return values
        private static (bool isBool, bool isInt, bool isString, bool isObject, bool isVoid) ResolveKind(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            var prop = type.GetProperty(name, flags);
            if (prop != null && prop.CanWrite)
                return (prop.PropertyType == typeof(bool), IsIntType(prop.PropertyType), prop.PropertyType == typeof(string), MemberValueCompare.IsObjectType(prop.PropertyType), false);

            var field = type.GetField(name, flags);
            if (field != null && !field.IsInitOnly)
                return (field.FieldType == typeof(bool), IsIntType(field.FieldType), field.FieldType == typeof(string), MemberValueCompare.IsObjectType(field.FieldType), false);

            foreach (var m in type.GetMethods(flags))
            {
                if (m.IsSpecialName || m.Name != name) continue;
                if (m.ReturnType != typeof(void)) continue;
                var p = m.GetParameters();
                if (p.Length == 1 && (MemberValueCompare.IsSupported(p[0].ParameterType) || MemberValueCompare.IsObjectType(p[0].ParameterType)))
                    return (p[0].ParameterType == typeof(bool), IsIntType(p[0].ParameterType), p[0].ParameterType == typeof(string), MemberValueCompare.IsObjectType(p[0].ParameterType), false);
            }

            // MWC – parameterless void method (action trigger)
            var voidMethod = type.GetMethod(name, flags, null, Type.EmptyTypes, null);
            if (voidMethod != null && !voidMethod.IsSpecialName && voidMethod.ReturnType == typeof(void))
                return (false, false, false, false, true);

            return (false, false, false, false, false);
        }

        private static bool IsIntType(Type t) =>
            t == typeof(int) || t == typeof(long) || t == typeof(short) ||
            t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) ||
            t == typeof(sbyte) || t == typeof(ushort);
    }

    // ─── Member Setter Picker Popup (drill-down) ──────────────────────────────────
    //
    //  Depth 0 → pick Type group  (bool / int / float)  – search always visible
    //  Depth 1 → pick Member name (filtered by group)   – search always visible

    public class MemberSetterPickerPopup : PopupWindowContent
    {
        // ─── Layout ──────────────────────────────────────────────────────────────

        private const float WIN_W = 280f;
        private const float WIN_H = 320f;
        private const float HEADER_H = 26f;
        private const float SEARCH_H = 22f;
        private const float ROW_H = 26f;
        private const float COUNT_H = 16f;

        private static readonly Color C_HEADER_BG = new(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color C_ROW_SEL = new(0.18f, 0.37f, 0.73f, 0.55f);
        private static readonly Color C_ROW_HOVER = new(1f, 1f, 1f, 0.06f);
        private static readonly Color C_BACK_HOVER = new(1f, 1f, 1f, 0.08f);
        private static readonly Color C_CHEVRON = new(0.6f, 0.6f, 0.6f, 1f);

        // ─── Type → group ─────────────────────────────────────────────────────────

        private static string TypeGroup(Type t)
        {
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string"; // MWC – string group
            if (t == typeof(int) || t == typeof(long) || t == typeof(short) ||
                t == typeof(byte) || t == typeof(uint) || t == typeof(ulong) ||
                t == typeof(sbyte) || t == typeof(ushort)) return "int";
            if (MemberValueCompare.IsObjectType(t)) return "Object"; // MWC – UnityEngine.Object group
            return "float";
        }

        // ─── State ───────────────────────────────────────────────────────────────

        private readonly string _current;
        private readonly Action<string> _onSelected;
        private readonly List<MemberEntry> _all;

        private int _depth;
        private string _selGroup;

        private string _search = string.Empty;
        private List<MemberEntry> _filtered;
        private int _hoveredIndex = -1;
        private Vector2 _scroll;

        private GUIStyle _sRow;
        private GUIStyle _sRowSel;
        private GUIStyle _sHeader;
        private GUIStyle _sBack;
        private GUIStyle _sCount;
        private GUIStyle _sChevron;

        // ─── Constructor ─────────────────────────────────────────────────────────

        public MemberSetterPickerPopup(Type type, string current, Action<string> onSelected)
        {
            _current = current;
            _onSelected = onSelected;
            _all = BuildAll(type);
            RebuildFilter();
        }

        // ─── PopupWindowContent ───────────────────────────────────────────────────

        public override Vector2 GetWindowSize() => new(WIN_W, WIN_H);

        public override void OnGUI(Rect rect)
        {
            InitStyles();
            if (_depth == 0) DrawGroupLevel();
            else DrawNameLevel();
        }

        // ─── Depth 0 – Group ─────────────────────────────────────────────────────

        private void DrawGroupLevel()
        {
            DrawHeader("Set Member");
            DrawSearchBar();
            HandleKeyboard();

            if (!string.IsNullOrWhiteSpace(_search)) { DrawNameList(); return; }

            var groups = new List<string>(3);
            foreach (var e in _all)
                if (!groups.Contains(e.Group)) groups.Add(e.Group);

            for (int i = 0; i < groups.Count; i++)
            {
                string group = groups[i];
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));
                bool isHov = i == _hoveredIndex;

                if (isHov) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                var labelRect = new Rect(rowRect.x + 10f, rowRect.y + 4f, rowRect.width - 28f, ROW_H - 4f);
                EditorGUI.LabelField(labelRect, group, _sRow);

                int cnt = _all.FindAll(e => e.Group == group).Count;
                var cntRect = new Rect(rowRect.xMax - 48f, rowRect.y + 4f, 28f, ROW_H - 4f);
                EditorGUI.LabelField(cntRect, cnt.ToString(), _sCount);

                var chevRect = new Rect(rowRect.xMax - 18f, rowRect.y + 4f, 14f, ROW_H - 4f);
                EditorGUI.LabelField(chevRect, "›", _sChevron);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = i; editorWindow.Repaint(); }
                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                {
                    _selGroup = group;
                    _depth = 1;
                    _search = string.Empty;
                    _hoveredIndex = -1;
                    _scroll = Vector2.zero;
                    RebuildFilter();
                    editorWindow.Repaint();
                }
            }
        }

        // ─── Depth 1 – Names ─────────────────────────────────────────────────────

        private void DrawNameLevel()
        {
            DrawHeader(_selGroup, showBack: true);
            DrawSearchBar();
            HandleKeyboard();
            DrawNameList();
        }

        // ─── Shared list ─────────────────────────────────────────────────────────

        private void DrawNameList()
        {
            EditorGUILayout.LabelField(
                $"{_filtered.Count} member{(_filtered.Count != 1 ? "s" : "")}",
                _sCount, GUILayout.Height(COUNT_H));

            float listH = WIN_H - HEADER_H - SEARCH_H - COUNT_H - 4f;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(listH));

            int flatIndex = 0;
            foreach (var entry in _filtered)
            {
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));
                bool isCurrent = entry.Name == _current;
                bool isHovered = flatIndex == _hoveredIndex;

                if (isCurrent) EditorGUI.DrawRect(rowRect, C_ROW_SEL);
                else if (isHovered) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                var nameRect = new Rect(rowRect.x + 10f, rowRect.y + 4f, rowRect.width - 10f, ROW_H - 4f);
                EditorGUI.LabelField(nameRect, entry.Name, isCurrent ? _sRowSel : _sRow);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = flatIndex; editorWindow.Repaint(); }
                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                    Confirm(entry.Name);

                flatIndex++;
            }
            EditorGUILayout.EndScrollView();
        }

        // ─── Header ───────────────────────────────────────────────────────────────

        private void DrawHeader(string title, bool showBack = false)
        {
            var headerRect = EditorGUILayout.GetControlRect(GUILayout.Height(HEADER_H));
            EditorGUI.DrawRect(headerRect, C_HEADER_BG);

            if (showBack)
            {
                var backRect = new Rect(headerRect.x + 4f, headerRect.y + 3f, 22f, headerRect.height - 6f);
                var ev = Event.current;
                bool backHov = backRect.Contains(ev.mousePosition);

                if (backHov) EditorGUI.DrawRect(backRect, C_BACK_HOVER);
                EditorGUI.LabelField(backRect, "‹", _sBack);

                if (ev.type == EventType.MouseDown && backHov)
                {
                    _depth--;
                    _hoveredIndex = -1;
                    _search = string.Empty;
                    RebuildFilter();
                    editorWindow.Repaint();
                    ev.Use();
                }

                var titleRect = new Rect(headerRect.x + 28f, headerRect.y, headerRect.width - 32f, headerRect.height);
                EditorGUI.LabelField(titleRect, title, _sHeader);
            }
            else
            {
                var titleRect = new Rect(headerRect.x + 8f, headerRect.y, headerRect.width - 8f, headerRect.height);
                EditorGUI.LabelField(titleRect, title, _sHeader);
            }
        }

        // ─── Search bar ───────────────────────────────────────────────────────────

        private void DrawSearchBar()
        {
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("MemberSetterSearch");
            var searchRect = EditorGUILayout.GetControlRect(GUILayout.Height(SEARCH_H));
            _search = EditorGUI.TextField(searchRect, _search, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck()) { _hoveredIndex = -1; RebuildFilter(); }
            EditorGUI.FocusTextInControl("MemberSetterSearch");
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private void RebuildFilter()
        {
            List<MemberEntry> source = _depth == 1
                ? _all.FindAll(e => e.Group == _selGroup)
                : _all;

            if (string.IsNullOrWhiteSpace(_search)) { _filtered = source; return; }

            string q = _search.Trim();
            _filtered = source.FindAll(e => e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void Confirm(string name) { _onSelected?.Invoke(name); editorWindow.Close(); }

        private void HandleKeyboard()
        {
            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;

            bool nameListVisible = _depth == 1 || !string.IsNullOrWhiteSpace(_search);
            if (nameListVisible)
            {
                if (ev.keyCode == KeyCode.DownArrow)
                { _hoveredIndex = Mathf.Min(_hoveredIndex + 1, _filtered.Count - 1); ev.Use(); }
                if (ev.keyCode == KeyCode.UpArrow)
                { _hoveredIndex = Mathf.Max(_hoveredIndex - 1, 0); ev.Use(); }
                if (ev.keyCode == KeyCode.Return && _hoveredIndex >= 0 && _hoveredIndex < _filtered.Count)
                { Confirm(_filtered[_hoveredIndex].Name); ev.Use(); }
            }

            if (ev.keyCode == KeyCode.Escape)
            {
                if (_depth > 0) { _depth--; _search = string.Empty; _hoveredIndex = -1; RebuildFilter(); editorWindow.Repaint(); }
                else editorWindow.Close();
                ev.Use();
            }
        }

        // ─── Data builder ─────────────────────────────────────────────────────────

        private static List<MemberEntry> BuildAll(Type type)
        {
            var list = new List<MemberEntry>(32);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var prop in type.GetProperties(flags))
            {
                if (!prop.CanWrite) continue;
                if (!MemberValueCompare.IsSupported(prop.PropertyType) && !MemberValueCompare.IsObjectType(prop.PropertyType)) continue; // MWC – include Object types
                list.Add(new(TypeGroup(prop.PropertyType), prop.Name));
            }

            foreach (var field in type.GetFields(flags))
            {
                if (field.IsInitOnly || field.IsLiteral) continue;
                if (!MemberValueCompare.IsSupported(field.FieldType) && !MemberValueCompare.IsObjectType(field.FieldType)) continue; // MWC – include Object types
                list.Add(new(TypeGroup(field.FieldType), field.Name));
            }

            foreach (var method in type.GetMethods(flags))
            {
                if (method.IsSpecialName) continue;
                if (method.ReturnType != typeof(void)) continue;
                var parms = method.GetParameters();
                if (parms.Length == 0) // MWC – parameterless void method (action trigger)
                {
                    list.Add(new("void", method.Name));
                    continue;
                }
                if (parms.Length != 1) continue;
                if (!MemberValueCompare.IsSupported(parms[0].ParameterType) && !MemberValueCompare.IsObjectType(parms[0].ParameterType)) continue; // MWC – include Object types
                list.Add(new(TypeGroup(parms[0].ParameterType), method.Name));
            }

            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        // ─── Styles ──────────────────────────────────────────────────────────────

        private void InitStyles()
        {
            _sRow ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                normal = { textColor = Color.white }
            };
            _sRowSel ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _sHeader ??= new(EditorStyles.boldLabel)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };
            _sBack ??= new(EditorStyles.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            _sCount ??= new(EditorStyles.centeredGreyMiniLabel);
            _sChevron ??= new(EditorStyles.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_CHEVRON }
            };
        }
    }

#endif
}