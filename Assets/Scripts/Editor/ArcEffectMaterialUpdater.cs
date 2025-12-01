using UnityEngine;
using UnityEditor;

namespace RingSport.Editor
{
    /// <summary>
    /// Editor utility to apply the ArcEffect shader to game materials.
    /// This tool helps quickly update all environment materials to use the arc effect.
    /// </summary>
    public class ArcEffectMaterialUpdater : EditorWindow
    {
        private Shader arcEffectShader;
        private bool updateGround = true;
        private bool updateObstacles = true;
        private bool updateCollectibles = true;
        private bool preserveColors = true;
        private bool preserveTextures = true;

        [MenuItem("Tools/RingSport/Update Materials with Arc Effect")]
        public static void ShowWindow()
        {
            GetWindow<ArcEffectMaterialUpdater>("Arc Effect Material Updater");
        }

        private void OnEnable()
        {
            // Try to find the ArcEffect shader
            arcEffectShader = Shader.Find("Custom/Mobile/ArcEffect");
        }

        private void OnGUI()
        {
            GUILayout.Label("Arc Effect Material Updater", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Shader reference
            EditorGUILayout.HelpBox(
                "This tool updates your game materials to use the ArcEffect shader. " +
                "It will preserve existing colors and textures.",
                MessageType.Info
            );

            EditorGUILayout.Space();

            arcEffectShader = EditorGUILayout.ObjectField(
                "Arc Effect Shader",
                arcEffectShader,
                typeof(Shader),
                false
            ) as Shader;

            if (arcEffectShader == null)
            {
                EditorGUILayout.HelpBox(
                    "Arc Effect shader not found! Make sure Assets/Shaders/ArcEffect.shader exists.",
                    MessageType.Warning
                );
            }

            EditorGUILayout.Space();
            GUILayout.Label("Material Categories to Update", EditorStyles.boldLabel);

            updateGround = EditorGUILayout.Toggle("Ground Materials", updateGround);
            updateObstacles = EditorGUILayout.Toggle("Obstacle Materials", updateObstacles);
            updateCollectibles = EditorGUILayout.Toggle("Collectible Materials", updateCollectibles);

            EditorGUILayout.Space();
            GUILayout.Label("Preservation Options", EditorStyles.boldLabel);

            preserveColors = EditorGUILayout.Toggle("Preserve Existing Colors", preserveColors);
            preserveTextures = EditorGUILayout.Toggle("Preserve Existing Textures", preserveTextures);

            EditorGUILayout.Space();

            GUI.enabled = arcEffectShader != null;

            if (GUILayout.Button("Update Materials", GUILayout.Height(30)))
            {
                UpdateMaterials();
            }

            if (GUILayout.Button("Create Test Material", GUILayout.Height(25)))
            {
                CreateTestMaterial();
            }

            GUI.enabled = true;
        }

        private void UpdateMaterials()
        {
            if (arcEffectShader == null)
            {
                EditorUtility.DisplayDialog(
                    "Error",
                    "Arc Effect shader is not assigned!",
                    "OK"
                );
                return;
            }

            int updatedCount = 0;

            // Find all materials in the Materials folder
            string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials" });

            foreach (string guid in materialGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null || !ShouldUpdateMaterial(material.name))
                {
                    continue;
                }

                // Store existing properties
                Color baseColor = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
                Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                Texture baseTexture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                float smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0.5f;
                float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0.0f;

                // Apply new shader
                material.shader = arcEffectShader;

                // Restore properties if preservation is enabled
                if (preserveColors)
                {
                    if (material.HasProperty("_BaseColor"))
                    {
                        // Use _Color if _BaseColor was white (URP default behavior)
                        material.SetColor("_BaseColor", baseColor.Equals(Color.white) ? color : baseColor);
                    }
                }

                if (preserveTextures && baseTexture != null)
                {
                    if (material.HasProperty("_BaseMap"))
                    {
                        material.SetTexture("_BaseMap", baseTexture);
                    }
                }

                // Set default arc effect properties
                if (material.HasProperty("_ArcStrength"))
                {
                    material.SetFloat("_ArcStrength", 1.0f);
                }

                if (material.HasProperty("_ArcDistance"))
                {
                    material.SetFloat("_ArcDistance", 50.0f);
                }

                if (material.HasProperty("_TintColor"))
                {
                    material.SetColor("_TintColor", new Color(0.8f, 0.9f, 1.0f, 1.0f));
                }

                if (material.HasProperty("_TintStrength"))
                {
                    material.SetFloat("_TintStrength", 0.5f);
                }

                if (material.HasProperty("_Smoothness"))
                {
                    material.SetFloat("_Smoothness", smoothness);
                }

                if (material.HasProperty("_Metallic"))
                {
                    material.SetFloat("_Metallic", metallic);
                }

                EditorUtility.SetDirty(material);
                updatedCount++;

                Debug.Log($"Updated material: {material.name}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Update Complete",
                $"Successfully updated {updatedCount} material(s) with the Arc Effect shader.",
                "OK"
            );
        }

        private bool ShouldUpdateMaterial(string materialName)
        {
            // Skip the player material
            if (materialName.Contains("Player"))
            {
                return false;
            }

            // Check category filters
            if (updateGround && materialName.Contains("Ground"))
            {
                return true;
            }

            if (updateObstacles && (materialName.Contains("Obstacle") ||
                                   materialName.Contains("Avoid") ||
                                   materialName.Contains("Pylon")))
            {
                return true;
            }

            if (updateCollectibles && (materialName.Contains("Coin") ||
                                      materialName.Contains("Mega") ||
                                      materialName.Contains("eclair")))
            {
                return true;
            }

            return false;
        }

        private void CreateTestMaterial()
        {
            if (arcEffectShader == null)
            {
                EditorUtility.DisplayDialog(
                    "Error",
                    "Arc Effect shader is not assigned!",
                    "OK"
                );
                return;
            }

            Material testMaterial = new Material(arcEffectShader);
            testMaterial.name = "ArcEffectTest";

            // Set default properties
            testMaterial.SetColor("_BaseColor", Color.white);
            testMaterial.SetColor("_TintColor", new Color(0.8f, 0.9f, 1.0f, 1.0f));
            testMaterial.SetFloat("_ArcStrength", 1.0f);
            testMaterial.SetFloat("_ArcDistance", 50.0f);
            testMaterial.SetFloat("_TintStrength", 0.5f);
            testMaterial.SetFloat("_Smoothness", 0.5f);
            testMaterial.SetFloat("_Metallic", 0.0f);

            string path = "Assets/Materials/ArcEffectTest.mat";
            AssetDatabase.CreateAsset(testMaterial, path);
            AssetDatabase.SaveAssets();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = testMaterial;

            Debug.Log($"Created test material at: {path}");
        }
    }
}
