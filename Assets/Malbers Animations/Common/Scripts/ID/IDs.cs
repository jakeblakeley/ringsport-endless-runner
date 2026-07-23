using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

#if UNITY_EDITOR
using UnityEditor;
using System;
#endif


namespace MalbersAnimations
{
    public abstract class IDs : ScriptableObject
    {
        [Tooltip("Display name on the ID Selection Context Button")]
        public string DisplayName;

        [Tooltip("Integer value to Identify IDs")]
        public int ID;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator int(IDs reference) => reference != null ? reference.ID : 0;

        [HideInInspector, Tooltip("Debug purpose only, not used for anything else")]
        public virtual Color IDColor => new(0.2f, 0.5f, 1f, 1f);

        /// <summary> Returns if an ID is inside a list (Include or Exclude) </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Included<T>(ICollection<T> list, bool include) where T : IDs
        {
            bool isIncluded = list.Contains(this);
            return include ? isIncluded : !isIncluded;
        }

        public bool Included(ICollection<IDs> list) => Included(list, true);
        public bool Excluded(ICollection<IDs> list) => Included(list, false);

        protected virtual void OnValidate()
        {
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(DisplayName)) DisplayName = name;
#endif
        }

#if UNITY_EDITOR
        [ContextMenu("Get ID <Hash>")]
        private void GetIDHash()
        {
            ID = Animator.StringToHash(name);
            MTools.SetDirty(this);
        }


        [ContextMenu("Set Name")]
        private void SetName() => DisplayName = name;

        protected void FindID<T>() where T : IDs
        {
            int newID = 0;
            var allAdd = MTools.GetAllInstances<T>();
            bool Found = true;

            while (Found)
            {
                newID++;
                Found = allAdd.Exists(x => (x.ID == newID && x != this));
            }
            ID = newID;
            DisplayName = name;
            MTools.SetDirty(this);
        }
#endif
    }


#if UNITY_EDITOR

    // ─── Property Drawer ─────────────────────────────────────────────────────────

    [CustomPropertyDrawer(typeof(IDs), true)]
    public class IDDrawer : PropertyDrawer
    {
        protected GUIStyle popupStyle;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            popupStyle ??= new(GUI.skin.GetStyle("PaneOptions"))
            {
                imagePosition = ImagePosition.ImageOnly
            };

            label = EditorGUI.BeginProperty(position, label, property);

            if (property.objectReferenceValue)
                label.tooltip += $"\n ID Value: [{(property.objectReferenceValue as IDs).ID}]";

            if (label.text.Contains("Element"))
            {
                position.x += 12;
                position.width -= 12;
            }
            else
                position = EditorGUI.PrefixLabel(position, label);

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            Rect buttonRect = new(position);
            buttonRect.yMin += popupStyle.margin.top;
            buttonRect.width = popupStyle.fixedWidth + popupStyle.margin.right;
            buttonRect.x -= 20;
            buttonRect.height = EditorGUIUtility.singleLineHeight;

            if (EditorGUI.DropdownButton(buttonRect, GUIContent.none, FocusType.Passive, popupStyle))
            {
                var nameOfType = GetPropertyType(property);
                string[] guids = AssetDatabase.FindAssets("t:" + nameOfType);

                var instances = new List<IDs>();
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var inst = AssetDatabase.LoadAssetAtPath<IDs>(path);
                    if (inst != null) instances.Add(inst);
                }
                instances = instances.OrderBy(x => x.ID).ToList();

                var capturedProp = property;
                var capturedSO = property.serializedObject;
                var current = property.objectReferenceValue as IDs;

                PopupWindow.Show(buttonRect, new IDPickerPopup(instances, current, chosen =>
                {
                    capturedSO.Update();
                    capturedProp.objectReferenceValue = chosen;
                    capturedSO.ApplyModifiedProperties();
                }));
            }

            position.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(position, property, GUIContent.none, false);
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        protected static string GetPropertyType(SerializedProperty property)
        {
            var type = property.type;
            var match = System.Text.RegularExpressions.Regex.Match(type, @"PPtr<\$(.*?)>");
            if (match.Success) type = match.Groups[1].Value;
            return type;
        }
    }


    // ─── ID Picker Popup ─────────────────────────────────────────────────────────
    //
    //  Browse mode  →  Root shows child groups first (with › chevron), then
    //                  ungrouped entries.  Clicking a group drills in.
    //                  Header shows back-button (‹) + breadcrumb path.
    //  Search mode  →  Flat filtered list; group path shown as mini-separator
    //                  whenever it changes, matching SerializableType pattern.
    //  Keyboard     →  Up/Down navigate, Enter selects/drills, Escape goes back.

    public class IDPickerPopup : PopupWindowContent
    {
        // ─── Layout ──────────────────────────────────────────────────────────────

        private const float WIN_W = 280f;
        private const float WIN_H = 340f;
        private const float HEADER_H = 26f;
        private const float SEARCH_H = 22f;
        private const float ROW_H = 24f;
        private const float GROUP_H = 26f;   // drill segment row height
        private const float COUNT_H = 16f;
        private const float SEP_H = 18f;   // flat-list group separator height

        private static readonly Color C_HEADER_BG = new(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color C_ROW_SEL = new(0.18f, 0.37f, 0.73f, 0.55f);
        private static readonly Color C_ROW_HOVER = new(1f, 1f, 1f, 0.07f);
        private static readonly Color C_BACK_HOVER = new(1f, 1f, 1f, 0.09f);
        private static readonly Color C_CHEVRON = new(0.6f, 0.6f, 0.6f, 1f);
        private static readonly Color C_ID_TEXT = new(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color C_GROUP_TEXT = new(0.70f, 0.85f, 1.00f, 1f);
        private static readonly Color C_SEP_BG = new(0f, 0f, 0f, 1f);

        // ─── Entry ───────────────────────────────────────────────────────────────

        private readonly struct IDEntry
        {
            /// <summary> Full group path, e.g. "Animals/Mammals" </summary>
            public readonly string Group;
            /// <summary> Leaf display label, e.g. "Dog" </summary>
            public readonly string Label;
            /// <summary> "[3] " prefix – empty for Tag subtypes </summary>
            public readonly string IDStr;
            public readonly IDs Asset;

            public IDEntry(IDs asset, string group, string label, string idStr)
            { Asset = asset; Group = group; Label = label; IDStr = idStr; }
        }

        // ─── State ───────────────────────────────────────────────────────────────

        private readonly List<IDEntry> _all;
        private readonly IDs _current;
        private readonly Action<IDs> _onSelected;

        private readonly List<string> _path = new();   // drill-down breadcrumb

        private string _search = string.Empty;
        private List<IDEntry> _filtered;
        private int _hoveredIndex = -1;       // -1 = None row
        private Vector2 _scroll;

        // ─── Styles ──────────────────────────────────────────────────────────────

        private GUIStyle _sRow, _sRowSel, _sRowID, _sGroupRow,
                         _sChevron, _sHeader, _sBack, _sCount, _sGrpSep;

        // ─── Constructor ─────────────────────────────────────────────────────────

        public IDPickerPopup(List<IDs> instances, IDs current, Action<IDs> onSelected)
        {
            _current = current;
            _onSelected = onSelected;
            _all = BuildEntries(instances);
            _filtered = _all;
        }

        // ─── PopupWindowContent ───────────────────────────────────────────────────

        public override Vector2 GetWindowSize() => new(WIN_W, WIN_H);

        public override void OnGUI(Rect rect)
        {
            InitStyles();
            DrawHeader();
            DrawSearchBar();
            HandleKeyboard();

            if (!string.IsNullOrWhiteSpace(_search))
                DrawFlatList();
            else
                DrawDrillLevel();
        }

        // ─── Header ──────────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            var headerRect = EditorGUILayout.GetControlRect(GUILayout.Height(HEADER_H));
            EditorGUI.DrawRect(headerRect, C_HEADER_BG);

            if (_path.Count > 0)
            {
                var backRect = new Rect(headerRect.x + 4f, headerRect.y + 3f, 22f, headerRect.height - 6f);
                var ev = Event.current;
                bool backHov = backRect.Contains(ev.mousePosition);

                if (backHov) EditorGUI.DrawRect(backRect, C_BACK_HOVER);
                EditorGUI.LabelField(backRect, "‹", _sBack);

                if (ev.type == EventType.MouseDown && backHov)
                {
                    _path.RemoveAt(_path.Count - 1);
                    _hoveredIndex = -1;
                    _scroll = Vector2.zero;
                    editorWindow.Repaint();
                    ev.Use();
                }

                var titleRect = new Rect(headerRect.x + 28f, headerRect.y, headerRect.width - 32f, headerRect.height);
                EditorGUI.LabelField(titleRect, string.Join("  ›  ", _path), _sHeader);
            }
            else
            {
                var titleRect = new Rect(headerRect.x + 8f, headerRect.y, headerRect.width - 8f, headerRect.height);
                EditorGUI.LabelField(titleRect, "Select ID", _sHeader);
            }
        }

        // ─── Search Bar ──────────────────────────────────────────────────────────

        private void DrawSearchBar()
        {
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("IDPickerSearch");
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(SEARCH_H));
            _search = EditorGUI.TextField(r, _search, EditorStyles.toolbarSearchField);

            if (EditorGUI.EndChangeCheck())
            {
                _hoveredIndex = -1;
                _scroll = Vector2.zero;
                RebuildFilter();
            }

            EditorGUI.FocusTextInControl("IDPickerSearch");
        }

        // ─── Drill-down level ─────────────────────────────────────────────────────

        private void DrawDrillLevel()
        {
            string prefix = CurrentPrefix();
            List<string> childSegs = GetChildSegments(prefix);
            List<IDEntry> localEntries = GetEntriesAt(prefix);

            EditorGUILayout.LabelField(
                $"{childSegs.Count} group{(childSegs.Count != 1 ? "s" : "")}  ·  " +
                $"{localEntries.Count} entr{(localEntries.Count != 1 ? "ies" : "y")}",
                _sCount, GUILayout.Height(COUNT_H));

            float listH = WIN_H - HEADER_H - SEARCH_H - COUNT_H - 4f;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(listH));

            // ── None row ─────────────────────────────────────────────────────────
            DrawNoneRow();

            // ── Child group segments – always first ───────────────────────────────
            for (int i = 0; i < childSegs.Count; i++)
            {
                string seg = childSegs[i];
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(GROUP_H));
                bool isHov = i == _hoveredIndex;

                if (isHov) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                var labelRect = new Rect(rowRect.x + 10f, rowRect.y, rowRect.width - 28f, rowRect.height);
                var chevRect = new Rect(rowRect.xMax - 18f, rowRect.y + 4f, 14f, rowRect.height - 4f);
                EditorGUI.LabelField(labelRect, seg, _sGroupRow);
                EditorGUI.LabelField(chevRect, "›", _sChevron);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = i; editorWindow.Repaint(); }

                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                {
                    _path.Add(seg);
                    _hoveredIndex = -1;
                    _scroll = Vector2.zero;
                    editorWindow.Repaint();
                    ev.Use();
                }
            }

            // ── Entries living directly at this level ─────────────────────────────
            for (int i = 0; i < localEntries.Count; i++)
            {
                var entry = localEntries[i];
                int flatIdx = childSegs.Count + i;
                bool isCur = entry.Asset == _current;
                bool isHov = flatIdx == _hoveredIndex;
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));

                if (isCur) EditorGUI.DrawRect(rowRect, C_ROW_SEL);
                else if (isHov) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                DrawEntryLabel(rowRect, entry, isCur);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = flatIdx; editorWindow.Repaint(); }

                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                { SelectEntry(entry.Asset); ev.Use(); }
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── Flat search list ─────────────────────────────────────────────────────

        private void DrawFlatList()
        {
            EditorGUILayout.LabelField(
                $"{_filtered.Count} result{(_filtered.Count != 1 ? "s" : "")}",
                _sCount, GUILayout.Height(COUNT_H));

            float listH = WIN_H - HEADER_H - SEARCH_H - COUNT_H - 4f;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(listH));

            DrawNoneRow();

            string lastGroup = null;
            int flatIdx = 0;

            foreach (var entry in _filtered)
            {
                // Group separator on change
                if (entry.Group != lastGroup)
                {
                    lastGroup = entry.Group;
                    if (!string.IsNullOrEmpty(lastGroup))
                    {
                        var sep = EditorGUILayout.GetControlRect(GUILayout.Height(SEP_H));
                        EditorGUI.DrawRect(sep, C_SEP_BG);
                        EditorGUI.LabelField(
                            new Rect(sep.x + 6f, sep.y, sep.width - 6f, sep.height),
                            lastGroup, _sGrpSep);
                    }
                }

                bool isCur = entry.Asset == _current;
                bool isHov = flatIdx == _hoveredIndex;
                var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));

                if (isCur) EditorGUI.DrawRect(rowRect, C_ROW_SEL);
                else if (isHov) EditorGUI.DrawRect(rowRect, C_ROW_HOVER);

                DrawEntryLabel(rowRect, entry, isCur);

                var ev = Event.current;
                if (ev.type == EventType.MouseMove && rowRect.Contains(ev.mousePosition))
                { _hoveredIndex = flatIdx; editorWindow.Repaint(); }

                if (ev.type == EventType.MouseDown && rowRect.Contains(ev.mousePosition))
                { SelectEntry(entry.Asset); ev.Use(); }

                flatIdx++;
            }

            EditorGUILayout.EndScrollView();
        }

        // ─── Shared row helpers ───────────────────────────────────────────────────

        private void DrawNoneRow()
        {
            bool isCur = _current == null;
            bool isHov = _hoveredIndex == -1;
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(ROW_H));

            if (isCur) EditorGUI.DrawRect(r, C_ROW_SEL);
            else if (isHov) EditorGUI.DrawRect(r, C_ROW_HOVER);

            EditorGUI.LabelField(new Rect(r.x + 4f, r.y, r.width - 8f, r.height),
                "None", isCur ? _sRowSel : _sRow);

            var ev = Event.current;
            if (ev.type == EventType.MouseMove && r.Contains(ev.mousePosition))
            { _hoveredIndex = -1; editorWindow.Repaint(); }

            if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition))
            { SelectEntry(null); ev.Use(); }
        }

        private void DrawEntryLabel(Rect rowRect, IDEntry entry, bool isCur)
        {
            var style = isCur ? _sRowSel : _sRow;

            float idW = _sRowID.CalcSize(new GUIContent(entry.IDStr)).x + 4f;
            var lblR = new Rect(rowRect.x + 4f, rowRect.y, rowRect.width - idW - 8f, rowRect.height);
            var idR = new Rect(rowRect.xMax - idW - 4f, rowRect.y, idW, rowRect.height);
            EditorGUI.LabelField(lblR, entry.Label, style);
            EditorGUI.LabelField(idR, entry.IDStr, _sRowID);
        }

        private void SelectEntry(IDs asset)
        {
            _onSelected?.Invoke(asset);
            editorWindow.Close();
        }

        // ─── Keyboard ────────────────────────────────────────────────────────────

        private void HandleKeyboard()
        {
            var ev = Event.current;
            if (ev.type != EventType.KeyDown) return;

            bool inSearch = !string.IsNullOrWhiteSpace(_search);

            var childSegs = inSearch ? null : GetChildSegments(CurrentPrefix());
            var localEntries = inSearch ? _filtered : GetEntriesAt(CurrentPrefix());
            int totalRows = (childSegs?.Count ?? 0) + localEntries.Count;

            switch (ev.keyCode)
            {
                case KeyCode.DownArrow:
                    _hoveredIndex = Mathf.Min(_hoveredIndex + 1, totalRows - 1);
                    ev.Use(); editorWindow.Repaint(); break;

                case KeyCode.UpArrow:
                    _hoveredIndex = Mathf.Max(_hoveredIndex - 1, -1);
                    ev.Use(); editorWindow.Repaint(); break;

                case KeyCode.Return:
                    if (_hoveredIndex == -1)
                    {
                        SelectEntry(null);
                    }
                    else if (!inSearch && childSegs != null && _hoveredIndex < childSegs.Count)
                    {
                        // Drill into group
                        _path.Add(childSegs[_hoveredIndex]);
                        _hoveredIndex = -1;
                        _scroll = Vector2.zero;
                        editorWindow.Repaint();
                    }
                    else
                    {
                        int entryIdx = inSearch ? _hoveredIndex : _hoveredIndex - (childSegs?.Count ?? 0);
                        if (entryIdx >= 0 && entryIdx < localEntries.Count)
                            SelectEntry(localEntries[entryIdx].Asset);
                    }
                    ev.Use(); break;

                case KeyCode.Escape:
                    if (!inSearch && _path.Count > 0)
                    { _path.RemoveAt(_path.Count - 1); _hoveredIndex = -1; _scroll = Vector2.zero; editorWindow.Repaint(); }
                    else
                        editorWindow.Close();
                    ev.Use(); break;
            }
        }

        // ─── Data helpers ─────────────────────────────────────────────────────────

        private void RebuildFilter()
        {
            if (string.IsNullOrWhiteSpace(_search)) { _filtered = _all; return; }

            string q = _search.Trim();
            _filtered = _all.FindAll(e =>
                e.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.Group.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.IDStr.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private string CurrentPrefix() =>
            _path.Count == 0 ? string.Empty : string.Join("/", _path);

        /// <summary>
        /// Returns the immediate child group segments under <paramref name="prefix"/>,
        /// sorted alphabetically.
        /// </summary>
        private List<string> GetChildSegments(string prefix)
        {
            var segs = new List<string>(8);

            foreach (var e in _all)
            {
                if (string.IsNullOrEmpty(e.Group)) continue;

                string child;
                if (string.IsNullOrEmpty(prefix))
                {
                    int slash = e.Group.IndexOf('/');
                    child = slash < 0 ? e.Group : e.Group[..slash];
                }
                else
                {
                    if (!e.Group.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    if (e.Group.Length == prefix.Length) continue;  // lives here, not below
                    if (e.Group[prefix.Length] != '/') continue;

                    string rest = e.Group.Substring(prefix.Length + 1);
                    int slash = rest.IndexOf('/');
                    child = slash < 0 ? rest : rest.Substring(0, slash);
                }

                if (!segs.Contains(child)) segs.Add(child);
            }

            segs.Sort(StringComparer.OrdinalIgnoreCase);
            return segs;
        }

        /// <summary>
        /// Returns entries whose group path exactly matches <paramref name="prefix"/>,
        /// sorted alphabetically by label.
        /// </summary>
        private List<IDEntry> GetEntriesAt(string prefix)
        {
            var result = new List<IDEntry>(16);
            foreach (var e in _all)
                if (string.Equals(e.Group, prefix, StringComparison.Ordinal)) result.Add(e);
            result.Sort((a, b) => a.Asset.ID.CompareTo(b.Asset.ID)); // MWC — sort by ID value ascending instead of alphabetically
            return result;
        }

        private static List<IDEntry> BuildEntries(List<IDs> instances)
        {
            var list = new List<IDEntry>(instances.Count);

            foreach (var inst in instances)
            {
                if (inst == null) continue;

                string idStr = $"[{inst.ID}]";
                string displayName = !string.IsNullOrEmpty(inst.DisplayName) ? inst.DisplayName : inst.name;

                string group = string.Empty;
                string label = displayName;

                int slash = displayName.LastIndexOf('/');
                if (slash >= 0)
                {
                    group = displayName.Substring(0, slash);
                    label = displayName.Substring(slash + 1);
                }

                list.Add(new(inst, group, label, idStr));
            }

            return list;
        }

        // ─── Styles ───────────────────────────────────────────────────────────────

        private void InitStyles()
        {
            _sRow ??= new(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            _sRowSel ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            _sRowID ??= new(EditorStyles.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = C_ID_TEXT }
            };
            _sGroupRow ??= new(EditorStyles.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = C_GROUP_TEXT }
            };
            _sChevron ??= new(EditorStyles.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = C_CHEVRON }
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
            _sGrpSep ??= new(EditorStyles.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = C_GROUP_TEXT }
            };
        }
    }

#endif
}