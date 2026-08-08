using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using RingSport.Core;

namespace RingSport.UI
{
    /// <summary>
    /// Bakes small RenderTexture portraits of hat prefabs for the selector
    /// boxes - one 256px texture per browsed hat, cached for the session.
    /// The stage sits far below the track at the world origin's XZ so the
    /// ArcEffect displacement is zero, and the model only exists for the
    /// single manual Render call. Locked hats bake with a flat black
    /// URP/Unlit override, so the silhouette teases the shape.
    /// </summary>
    internal static class HatThumbnails
    {
        private const int Size = 256;
        private static readonly Vector3 StagePosition = new Vector3(0f, -60f, 0f);

        private static readonly Dictionary<string, RenderTexture> cache = new Dictionary<string, RenderTexture>();
        private static Material silhouetteMaterial;

        // Statics survive play sessions when domain reload is disabled; the
        // RenderTextures themselves die with the session, so drop the stale refs
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cache.Clear();
            silhouetteMaterial = null;
        }

        public static Texture Get(string hatId, bool locked)
        {
            if (string.IsNullOrEmpty(hatId))
                return null;

            string key = locked ? hatId + ":locked" : hatId;
            if (cache.TryGetValue(key, out RenderTexture cached) && cached != null)
                return cached;

            RenderTexture baked = Bake(hatId, locked);
            cache[key] = baked;
            return baked;
        }

        private static RenderTexture Bake(string hatId, bool locked)
        {
            GameObject prefab = HatManager.LoadHatPrefab(hatId);
            if (prefab == null)
                return null;

            GameObject model = Object.Instantiate(prefab);
            GameObject cameraObject = null;
            try
            {
                // Neutral pose, 3/4 front view. The prefab root's rotation is
                // part of its head fit (some models need a facing correction),
                // so the view yaw composes with it instead of replacing it.
                model.transform.SetPositionAndRotation(StagePosition,
                    Quaternion.Euler(0f, 205f, 0f) * prefab.transform.localRotation);

                Bounds bounds = ComputeBounds(model);

                if (locked)
                    ApplySilhouette(model);

                cameraObject = new GameObject("HatThumbnailCamera");
                var cam = cameraObject.AddComponent<Camera>();
                cam.enabled = false; // single manual render only
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 10f;

                float radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                cam.orthographicSize = Mathf.Max(0.05f, radius * 1.3f);

                Vector3 cameraPos = bounds.center + new Vector3(0f, radius * 0.9f, -3f);
                cam.transform.position = cameraPos;
                cam.transform.rotation = Quaternion.LookRotation(bounds.center - cameraPos, Vector3.up);

                var extraData = cam.GetUniversalAdditionalCameraData();
                if (extraData != null)
                {
                    extraData.renderPostProcessing = false;
                    extraData.renderShadows = false;
                    extraData.antialiasing = AntialiasingMode.None;
                }

                var rt = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32)
                {
                    name = $"HatThumb_{hatId}{(locked ? "_locked" : "")}"
                };

                // Camera.Render() is unsupported under URP - a render request
                // is the SRP-safe way to render one camera on demand
                var request = new RenderPipeline.StandardRequest();
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    request.destination = rt;
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }
                return rt;
            }
            finally
            {
                if (cameraObject != null)
                    Object.Destroy(cameraObject);
                Object.Destroy(model);
            }
        }

        private static Bounds ComputeBounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(model.transform.position, Vector3.one * 0.3f);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void ApplySilhouette(GameObject model)
        {
            if (silhouetteMaterial == null)
            {
                // URP/Unlit is the one always-included shader in builds, and
                // unlit black stays a true silhouette under any lighting
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    return;

                silhouetteMaterial = new Material(shader);
                if (silhouetteMaterial.HasProperty("_BaseColor"))
                    silhouetteMaterial.SetColor("_BaseColor", Color.black);
                silhouetteMaterial.color = Color.black;
            }

            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = silhouetteMaterial;
                renderer.sharedMaterials = materials;
            }
        }
    }
}
