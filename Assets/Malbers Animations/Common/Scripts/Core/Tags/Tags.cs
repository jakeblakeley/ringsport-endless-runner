using System.Collections.Generic;
using UnityEngine;
using System.Linq;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/scriptables/tags")]
    [AddComponentMenu("Malbers/Utilities/Tools/Tags"), DefaultExecutionOrder(-500)]
    public class Tags : MonoBehaviour
    {
        /// <summary>Keep a Track of the game objects that has this component</summary>
        public static List<Tags> TagsHolders;

        /// <summary>List of tags for this component</summary>
        public List<Tag> tags = new();

        /// <summary>Convert the list to a Hash Set  (So the element does not repeat and is faster to find)</summary>
        private readonly HashSet<int> HashTag = new();


        void OnEnable()
        {
            TagsHolders ??= new List<Tags>();
            TagsHolders.Add(this);
            foreach (var tag in tags)
            {
                if (tag) tag.Add(gameObject); // When the component is enabled, it adds itself to the list of GameObject on each tag.
            }
        }

        void OnDisable()
        {
            TagsHolders.Remove(this);
            foreach (var tag in tags)
            {
                if (tag) tag.Remove(gameObject); // When the component is disabled, it removes itself from the list of GameObject on each tag.
            }
        }

        public void Awake()
        {
            var x = new HashSet<Tag>(tags);
            x.Remove(null);
            foreach (var item in x) HashTag.Add(item.ID);
        }

        public bool HasTag(Tag tag) => HasTag(tag.ID);

        public bool HasTag(List<Tag> Tag)
        {
            foreach (var tag in Tag)
                if (HashTag.Contains(tag)) return true;
            return false;
        }

        public bool HasAllTag(List<Tag> Tag)
        {
            foreach (var tag in Tag)
                if (!HashTag.Contains(tag)) return false;
            return true;
        }

        public bool HasTag(int key) => HashTag.Contains(key);

        public bool HasTag(params Tag[] enteringTags)
        {
            foreach (var tag in enteringTags)
                if (HashTag.Contains(tag)) return true;
            return false;
        }

        public bool HasAllTags(params Tag[] enteringTags)
        {
            foreach (var tag in enteringTags)
                if (!HashTag.Contains(tag)) return false;
            return true;
        }



        public bool HasTag(params int[] enteringTags)
        {
            foreach (var tag in enteringTags)
                if (HashTag.Contains(tag)) return true;
            return false;
        }

        public bool HasAllTags(params int[] enteringTags)
        {
            foreach (var tag in enteringTags)
                if (!HashTag.Contains(tag)) return false;
            return true;
        }

        public void AddTag(Tag t)
        {
            if (!HashTag.Contains(t.ID))
            {
                tags.Add(t);
                HashTag.Add(t.ID);
                t.Add(gameObject);
            }
        }

        public void RemoveTag(Tag t)
        {
            if (HashTag.Contains(t))
            {
                tags.Remove(t);
                HashTag.Remove(t.ID);
                t.Remove(gameObject);
            }
        }

        #region STATIC METHODS Retun GameObjects by Tag

        public static GameObject GameObjectbyTagFirst(Tag tag)
        {
            if (TagsHolders == null || TagsHolders.Count == 0) return null;
            foreach (var item in TagsHolders)
                if (item.HasTag(tag)) return item.gameObject;
            return null;
        }

        public static List<GameObject> GameObjectbyTag(int tag)
        {
            var go = new List<GameObject>();
            if (TagsHolders == null || TagsHolders.Count == 0) return null;
            foreach (var item in TagsHolders)
                if (item.HasTag(tag)) go.Add(item.gameObject);
            if (go.Count == 0) return null;
            return go;
        }
        public static List<GameObject> GameObjectbyTag(Tag tag) => tag.gameObjects.ToList();

        public static List<GameObject> GameObjectbyTag(Tag[] tags)
        {
            var go = new List<GameObject>();

            //append all the gameobjects of each tag into the list
            foreach (var tag in tags)
            {
                go = go.Union(tag.gameObjects).ToList();
            }
            return go;
        }


        public static List<GameObject> GameObjectbyTag(MTags tags)
        {
            var go = new List<GameObject>();

            //append all the gameobjects of each tag into the list
            foreach (var tag in tags.tags)
            {
                go = go.Union(tag.gameObjects).ToList();
            }
            return go;
        }

        #endregion


#if UNITY_EDITOR
        [ContextMenu("Set Player Tag")]
        private void SetPlayerTag()
        {
            tags ??= new();

            var playerTag = MTools.GetInstance<Tag>("Player");

            if (playerTag == null) return;

            if (!tags.Contains(playerTag))
                tags.Add(playerTag);
            else
                Debug.LogWarning("The Player Tag is already added to this GameObject", this);
        }
#endif
    }

    public static class Tag_Transform_Extension
    {
        public static bool HasMalbersTag(this Transform t, Tag tag) => tag.FindInHierarchy(t);

        public static bool HasMalbersTag(this Transform t, params Tag[] tags)
            => Tags.TagsHolders.Exists(x => x.transform == t && x.HasTag(tags));



        public static bool HasMalbersTag(this GameObject t, Tag tag) => HasMalbersTag(t.transform, tag);

        /// <summary> Checks if the GameObject has any of the specified Malbers Tags. It searches for a Tags component in the GameObject and checks if it contains any of the provided tags. </summary>
        /// <param name="t"></param>
        /// <param name="tag"></param>
        /// <returns></returns>
        public static bool HasMalbersTag(this Transform t, List<Tag> tag)
        {
            foreach (var item in tag)
                if (HasMalbersTag(t.transform, item)) return true;
            return false;
        }


        /// <summary> Checks if the GameObject has any of the specified Malbers Tags. It searches for a Tags component in the GameObject and checks if it contains any of the provided tags. </summary>
        /// <param name="t"></param>
        /// <param name="tag"></param>
        /// <returns></returns>
        public static bool HasMalbersTag(this GameObject t, List<Tag> tag)
        {
            foreach (var item in tag)
                if (HasMalbersTag(t.transform, item)) return true;
            return false;
        }

        public static bool HasAllMalbersTag(this GameObject t, List<Tag> tag)
        {
            foreach (var item in tag)
                if (!HasMalbersTag(t.transform, item)) return false;
            return true;
        }

        public static bool HasAllMalbersTagInParent(this GameObject go, List<Tag> tag)
        {
            var c = GetTagInParent(go);
            if (c == null) return false;
            foreach (var t in tag)
                if (!c.HasTag(t)) return false;
            return true;

        }

        public static bool HasMalbersTag(this Component t, Tag tag) => HasMalbersTag(t.transform, tag);
        public static bool HasMalbersTag(this GameObject t, params Tag[] tags) => HasMalbersTag(t.transform, tags);

        private static Tags GetTagInParent(GameObject t) => t.GetComponentInParent<Tags>(false);

        public static GameObject FindWithMalbersTag(this GameObject t, Tag tag)
        {
            var allTags = t.GetComponentsInChildren<Tags>(false);
            if (allTags != null)
                foreach (var item in allTags)
                    if (item.HasTag(tag)) return item.gameObject;
            return null;
        }

        public static Transform FindWithMalbersTag(this Transform t, Tag tag)
        {
            if (t == null) return null;
            var allTags = t.GetComponentsInChildren<Tags>(false);
            if (allTags != null)
                foreach (var item in allTags)
                    if (item.HasTag(tag)) return item.transform;
            return null;
        }

        public static bool HasMalbersTagInParent(this Transform t, Tag tag) { var c = GetTagInParent(t.gameObject); return c != null && c.HasTag(tag); }
        public static bool HasMalbersTagInParent(this Transform t, params Tag[] tags) { var c = GetTagInParent(t.gameObject); return c != null && c.HasTag(tags); }
        public static bool HasMalbersTagInParent(this GameObject t, Tag tag) { var c = GetTagInParent(t); return c != null && c.HasTag(tag); }
        public static bool HasMalbersTagInParent(this GameObject t, List<Tag> tag) { var c = GetTagInParent(t); return c != null && c.HasTag(tag); }
        public static bool HasMalbersTagInParent(this GameObject t, params Tag[] tags) { var c = GetTagInParent(t); return c != null && c.HasTag(tags); }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(Tags)), CanEditMultipleObjects]
    public class TagsEd : Editor
    {
        SerializedProperty tags;

        private GUIStyle pillStyle;
        private GUIStyle pillLabelStyle;
        private GUIStyle pillBgStyle;
        private GUIStyle xButtonStyle;
        private GUIStyle addButtonStyle;

        // Pill texture cache: avoids creating a new Texture2D every repaint
        private readonly Dictionary<Color, Texture2D> pillTextureCache = new();

        // Cached tag lists — rebuilt only on enable or when the assigned count changes
        private readonly List<Tag> cachedAllProjectTags = new();
        private readonly List<Tag> cachedAvailableTags = new();
        private int lastKnownTagCount = -1;

        protected virtual void OnEnable()
        {
            tags = serializedObject.FindProperty("tags");
            RebuildTagCache();
        }

        private void OnDisable()
        {
            // Clean up cached textures when inspector is closed
            foreach (var tex in pillTextureCache.Values)
                if (tex != null) DestroyImmediate(tex);
            pillTextureCache.Clear();
        }

        // ── Style initialisation ───────────────────────────────────────
        // Called every OnInspectorGUI but returns instantly once ready.
        // The null-check includes GUI.skin so it retries if skin wasn't
        // available yet, without permanently locking out initialization.
        private bool TryInitStyles()
        {
            // GUI.skin can be null on early frames — bail and retry next repaint
            if (GUI.skin == null) return false;

            // Each style is checked individually so a domain reload (which nulls them
            // all) forces a full rebuild even if the method previously succeeded.
            if (pillStyle != null &&
                pillLabelStyle != null &&
                pillBgStyle != null &&
                xButtonStyle != null &&
                addButtonStyle != null) return true;

            pillStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 3, 3),
                margin = new RectOffset(2, 2, 2, 2),
                fixedHeight = 20,
            };

            pillLabelStyle = new GUIStyle(pillStyle);

            // 9-slice style for pill backgrounds — only normal.background is swapped per pill
            pillBgStyle = new GUIStyle(GUIStyle.none)
            {
                border = new RectOffset(10, 10, 10, 10),
            };

            xButtonStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 1),
            };

            Texture2D addBG = BuildPillTexture(10, 20, Color.white);
            addButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 0, 2),
                margin = new RectOffset(2, 2, 2, 2),
                fixedHeight = 20,
                fixedWidth = 22,
                border = new RectOffset(8, 8, 8, 8),
            };
            addButtonStyle.normal.background = addBG;
            addButtonStyle.hover.background = addBG;
            addButtonStyle.active.background = addBG;

            return true;
        }

        // ── Tag cache ──────────────────────────────────────────────────
        private void RebuildTagCache()
        {
            cachedAllProjectTags.Clear();
            string[] guids = AssetDatabase.FindAssets("t:Tag");
            foreach (string guid in guids)
            {
                Tag t = AssetDatabase.LoadAssetAtPath<Tag>(AssetDatabase.GUIDToAssetPath(guid));
                if (t != null) cachedAllProjectTags.Add(t);
            }
            RebuildAvailableCache();
            lastKnownTagCount = tags.arraySize;
        }

        private void RebuildAvailableCache()
        {
            cachedAvailableTags.Clear();
            foreach (Tag t in cachedAllProjectTags)
            {
                bool found = false;
                for (int i = 0; i < tags.arraySize; i++)
                {
                    if (tags.GetArrayElementAtIndex(i).objectReferenceValue == t) { found = true; break; }
                }
                if (!found) cachedAvailableTags.Add(t);
            }
        }

        // ── Inspector GUI ──────────────────────────────────────────────
        public override void OnInspectorGUI()
        {
            // Bail silently until Unity's GUI skin is ready
            if (!TryInitStyles()) return;

            serializedObject.Update();

            // Rebuild available-tags cache only when the assigned count changes
            if (tags.arraySize != lastKnownTagCount)
            {
                RebuildAvailableCache();
                lastKnownTagCount = tags.arraySize;
            }

            float inspectorWidth = EditorGUIUtility.currentViewWidth - 26f;
            float x = 0f;
            int removeIndex = -1;
            float addBtnW = addButtonStyle.fixedWidth
                                 + addButtonStyle.margin.left
                                 + addButtonStyle.margin.right;

            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();

            for (int i = 0; i < tags.arraySize; i++)
            {
                Tag tag = tags.GetArrayElementAtIndex(i).objectReferenceValue as Tag;
                if (tag == null) continue;

                string label = !string.IsNullOrEmpty(tag.DisplayName) ? tag.DisplayName : tag.name;
                float xBtnSize = 14f;
                float xPad = 6f;
                float pillW = pillStyle.CalcSize(new GUIContent(label)).x + xBtnSize + xPad + 4f;

                // Wrap row when pill + add button won't fit
                if (x + pillW > inspectorWidth - addBtnW && x > 0f)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    x = 0f;
                }

                // Reserve rect — no Button, so no hover state
                Rect pillRect = GUILayoutUtility.GetRect(
                    new GUIContent(label), pillStyle,
                    GUILayout.Width(pillW), GUILayout.Height(20f));

                float brightness = tag.Color.r * 0.299f + tag.Color.g * 0.587f + tag.Color.b * 0.114f;
                Color textColor = brightness > 0.70f ? Color.black : Color.white;

                // Draw pill background with 9-slice so rounded corners aren't stretched
                // Must only be called during Repaint — other events (e.g. MouseDown) will throw
                if (Event.current.type == EventType.Repaint)
                {
                    pillBgStyle.normal.background = GetPillTexture(tag.Color);
                    pillBgStyle.Draw(pillRect, GUIContent.none, false, false, false, false);
                }

                // Draw label
                Rect labelRect = new(
                    pillRect.x + 8f, pillRect.y,
                    pillRect.width - xBtnSize - xPad, pillRect.height);
                pillLabelStyle.normal.textColor = textColor;
                GUI.Label(labelRect, label, pillLabelStyle);

                // Double-click pill to select the Tag asset in the Project window
                if (Event.current.type == EventType.MouseDown &&
                    Event.current.clickCount == 2 &&
                    pillRect.Contains(Event.current.mousePosition))
                {
                    EditorGUIUtility.PingObject((Object)tag);
                    Selection.activeObject = tag;
                    Event.current.Use();
                }
                Rect xRect = new(
                    pillRect.xMax - xBtnSize - 4f,
                    pillRect.y + (pillRect.height - xBtnSize) * 0.5f,
                    xBtnSize, xBtnSize);
                xButtonStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.65f);
                if (GUI.Button(xRect, "✕", xButtonStyle))
                    removeIndex = i;

                x += pillW + 4f;
            }

            // "+" button — inline after last pill
            using (new EditorGUI.DisabledGroupScope(cachedAvailableTags.Count == 0))
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 1f);
                GUI.contentColor = Color.white;

                // Pre-capture rect so PopupWindow.Show has a valid anchor position
                Rect addBtnRect = GUILayoutUtility.GetRect(
                    new GUIContent("+"), addButtonStyle,
                    GUILayout.Width(addButtonStyle.fixedWidth),
                    GUILayout.Height(addButtonStyle.fixedHeight));

                if (GUI.Button(addBtnRect, "+", addButtonStyle))
                {
                    var tagInstances = new List<IDs>(cachedAvailableTags.Count);
                    foreach (var t in cachedAvailableTags) tagInstances.Add(t);

                    PopupWindow.Show(addBtnRect, new IDPickerPopup(tagInstances, null, chosen =>
                    {
                        if (chosen == null) return;
                        var captured = chosen as Tag;
                        Undo.RecordObject(target, "Add Tag");
                        serializedObject.Update();
                        tags.InsertArrayElementAtIndex(tags.arraySize);
                        tags.GetArrayElementAtIndex(tags.arraySize - 1).objectReferenceValue = captured;
                        serializedObject.ApplyModifiedProperties();
                    }));
                }

                GUI.backgroundColor = prevBg;
                GUI.contentColor = Color.white;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            if (removeIndex >= 0)
            {
                Undo.RecordObject(target, "Remove Tag");
                tags.DeleteArrayElementAtIndex(removeIndex);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ── Texture helpers ────────────────────────────────────────────

        /// <summary>Returns a cached pill texture for the given color, creating one if needed.</summary>
        private Texture2D GetPillTexture(Color color)
        {
            if (!pillTextureCache.TryGetValue(color, out Texture2D tex) || tex == null)
            {
                tex = BuildPillTexture(10, 20, color);
                pillTextureCache[color] = tex;
            }
            return tex;
        }

        /// <summary>Creates a rounded-rectangle Texture2D.</summary>
        private static Texture2D BuildPillTexture(int cornerRadius, int height, Color color)
        {
            int width = Mathf.Max(cornerRadius * 2 + 4, 32);
            Texture2D tex = new(width, height, TextureFormat.ARGB32, false);
            tex.hideFlags = HideFlags.DontSave;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    float cx = Mathf.Min(px, width - 1 - px);
                    float cy = Mathf.Min(py, height - 1 - py);

                    if (cx < cornerRadius && cy < cornerRadius)
                    {
                        float dist = Vector2.Distance(new Vector2(cx, cy),
                                                       new Vector2(cornerRadius - 1, cornerRadius - 1));
                        float alpha = Mathf.Clamp01(cornerRadius - dist);
                        tex.SetPixel(px, py, new Color(color.r, color.g, color.b, color.a * alpha));
                    }
                    else
                    {
                        tex.SetPixel(px, py, color);
                    }
                }
            }

            tex.Apply();
            return tex;
        }
    }
#endif
}