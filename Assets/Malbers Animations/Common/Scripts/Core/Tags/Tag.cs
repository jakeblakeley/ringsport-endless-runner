using UnityEngine;
using System.Collections.Generic;
using System.Linq;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [CreateAssetMenu(menuName = "Malbers Animations/Tag", fileName = "New Tag", order = 3000)]
    public class Tag : IDs
    {
        public HashSet<GameObject> gameObjects = new();
        public bool ValidObjects => gameObjects != null && gameObjects.Count > 0;

        [Tooltip("Color to identify tags in the inspector")]
        public Color Color = new(0.2f, 0.5f, 1f, 1f);
        public override Color IDColor => Color;

        public virtual void Clear() => gameObjects.Clear();

        /// <summary>  Adds a GameObject to the list of tagged GameObjects. This method should be called by the MalbersTag component when a GameObject is tagged with this Tag.
        public virtual void Add(GameObject go)
        {
            gameObjects ??= new HashSet<GameObject>();
            gameObjects.Add(go);
        }

        /// <summary>Finds a GameObject that is a child of the given parent Transform  </summary>
        public virtual GameObject FindInHierarchy(Transform parent)
        {
            foreach (var go in gameObjects)
            {
                if (go != null && go.transform.SameHierarchy(parent))
                {
                    return go;
                }
            }
            return null;
        }

        public virtual GameObject FindFirst() => gameObjects != null && gameObjects.Count > 0 ? gameObjects.First() : null;
        public virtual Transform FindFirstT() => gameObjects != null && gameObjects.Count > 0 ? gameObjects.First().transform : null;
        public virtual GameObject FindFirst(Transform parent)
        {
            return gameObjects != null && gameObjects.Count > 0 ? gameObjects.First(x => x.transform.SameHierarchy(parent)) : gameObjects.First();
        }

        /// <summary>Finds a Transform that is a child of the given parent Transform  </summary>
        public virtual Transform FindInHierarchyT(Transform parent)
        {
            foreach (var go in gameObjects)
            {
                if (go != null && go.transform.SameHierarchy(parent))
                {
                    return go.transform;
                }
            }
            return null;
        }

        public virtual bool Contains(GameObject go) => gameObjects != null && gameObjects.Contains(go);


        /// <summary> Removes a GameObject from the list of tagged GameObjects. This method should be called by the MalbersTag component when a GameObject is untagged or destroyed  </summary>
        public virtual void Remove(GameObject go) => gameObjects.Remove(go);
        public virtual void Remove(Transform t) => gameObjects.Remove(t.gameObject);

        protected virtual void OnEnable() => ID = name.GetHashCode();

        //implicit operator to allow using the Tag directly as an int (the ID) in conditions and other places where an int is expected
        public static implicit operator int(Tag tag) => tag != null ? tag.ID : 0;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(Tag)), CanEditMultipleObjects]
    public class TagEditor : Editor
    {
        SerializedProperty ID, Color,
            DisplayName;

        Tag M;

        void OnEnable()
        {
            ID = serializedObject.FindProperty("ID");
            Color = serializedObject.FindProperty(nameof(M.Color));
            DisplayName = serializedObject.FindProperty("DisplayName");
            M = (Tag)target;

            if (!Application.isPlaying)
            {
                ID.intValue = target.name.GetHashCode();
                serializedObject.ApplyModifiedProperties();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MalbersEditor.Foldout_Bold(true, target.name, MTools.MBlue * 2, true);
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(DisplayName);
                    EditorGUILayout.PropertyField(Color, GUIContent.none, GUILayout.Width(50));

                }

                using (new EditorGUI.DisabledGroupScope(true))
                    EditorGUILayout.PropertyField(ID);
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"Tagged GameObjects [{M.gameObjects.Count}] [RUNTIME ONLY]", EditorStyles.boldLabel);

                    if (Application.isPlaying && M.gameObjects != null)
                    {
                        var index = 0;
                        foreach (var item in M.gameObjects)
                        {
                            EditorGUILayout.ObjectField($"Tagged GameObject [{index}]", item, typeof(GameObject), true);
                            index++;
                        }
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}