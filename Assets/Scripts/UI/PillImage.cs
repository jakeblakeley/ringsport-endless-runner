using UnityEngine;
using UnityEngine.UI;

namespace RingSport.UI
{
    /// <summary>
    /// Keeps a 9-sliced pill background capped at exactly half its own height,
    /// however that height ends up being decided.
    ///
    /// PillButtonSetup can bake the right pixelsPerUnitMultiplier for buttons
    /// with a fixed rect, but the Simon Says row is sized by a layout group on
    /// a panel that is inactive in the scene - so the serialized rect there is
    /// whatever it was before the layout last ran, and a baked number is wrong
    /// the moment the panel switches on. This recomputes it from the live rect
    /// instead, which is also what makes the cap survive any future re-layout.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Image))]
    [DisallowMultipleComponent]
    public class PillImage : MonoBehaviour
    {
        private Image image;

        private void OnEnable() => Apply();

        private void OnRectTransformDimensionsChange() => Apply();

        private void Apply()
        {
            if (image == null)
                image = GetComponent<Image>();

            var sprite = image != null ? image.sprite : null;
            if (sprite == null || image.type != Image.Type.Sliced)
                return;

            float height = ((RectTransform)transform).rect.height;
            if (height <= 0f)
                return;

            // Image draws a border of borderPx * (referencePPU / spritePPU) / multiplier,
            // and a pill wants that drawn border to be exactly half the height.
            float referencePixelsPerUnit = image.canvas != null ? image.canvas.referencePixelsPerUnit : 100f;
            float unscaledBorder = sprite.border.x * referencePixelsPerUnit / sprite.pixelsPerUnit;
            if (unscaledBorder <= 0f)
                return; // not a 9-sliced sprite - nothing to cap

            float multiplier = unscaledBorder / (height * 0.5f);

            // Only write when it actually moved: the setter dirties the vertices,
            // and in the editor it would otherwise dirty the scene every rebuild.
            if (Mathf.Abs(image.pixelsPerUnitMultiplier - multiplier) > 0.0001f)
                image.pixelsPerUnitMultiplier = multiplier;
        }
    }
}
