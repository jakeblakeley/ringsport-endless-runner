#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    public class MenuCreateHuman
    {
        private readonly static string HumanPlayer_Guid = "51089675ba972e5448cb7e8b015fc52c";
        private readonly static string HumanAI_Guid = "29170ec19142ecc469c419b73013aad5";

        [MenuItem("GameObject/Malbers Animations/Create Human Player", false, -100)]
        static void CreatePlayer(MenuCommand menuCommand)
        {
            var gameObject = menuCommand.context as GameObject;

            if (gameObject != null)
            {
                DoHuman(gameObject, HumanPlayer_Guid);
                Debug.Log("Human Player Created!. Please save your the new Created Player as a Prefab Variant on your project", gameObject);
            }
        }

        [MenuItem("GameObject/Malbers Animations/Create Human AI", false, -100)]
        static void CreateAI(MenuCommand menuCommand)
        {
            var gameObject = menuCommand.context as GameObject;

            if (gameObject != null)
            {
                DoHuman(gameObject, HumanAI_Guid);
                Debug.Log("Human AI Created!. Please save your the new Created Player as a Prefab Variant on your project", gameObject);
            }
        }

        private static void DoHuman(GameObject gameObject, string guid)
        {
            gameObject.transform.ResetLocal(); //important to reset the transform Local

            if (!gameObject.TryGetComponent<Animator>(out var animator))
                animator = gameObject.AddComponent<Animator>();

            var currentAvatar = animator.avatar;
            var AvatarRoot = animator.avatarRoot;
            var humanPrefab = LoadByGUID(guid);

            if (humanPrefab != null)
            {
                var sceneObj = (GameObject)PrefabUtility.InstantiatePrefab(humanPrefab);
                sceneObj.GetComponent<Animator>().avatar = currentAvatar; //Set the Avatar to the new Player

                gameObject.transform.parent = sceneObj.transform;
                sceneObj.transform.ResetLocal();

                var animal = sceneObj.GetComponent<MAnimal>();
                animal.RootBone = AvatarRoot; //Set the Root Bone on the Animal Controller
                sceneObj.name = gameObject.name;

                Selection.activeGameObject = sceneObj; //Select the new Player
                GameObject.DestroyImmediate(animator); //Remove the animator
            }
        }

        private static GameObject LoadByGUID(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning($"Asset not found for GUID: {guid}");
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
    }
}
#endif