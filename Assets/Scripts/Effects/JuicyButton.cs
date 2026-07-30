using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RingSport.Effects
{
    /// <summary>
    /// Press feedback for UI buttons: quick scale-down on pointer down, springy
    /// OutBack release, soft click, and an optional idle attention pulse (the
    /// START / Retry buttons). Added to scene buttons by
    /// Tools > RingSport > Setup Juice Polish.
    ///
    /// This component OWNS its button's localScale (it writes every frame while
    /// enabled) so press, release and idle pulse never fight each other -
    /// don't scale the same transform from elsewhere (rotation wiggles are
    /// fine).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class JuicyButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("Soft click on press (temporary clip - see SOUND_EFFECTS.md).")]
        [SerializeField] private AudioClip clickSound;
        [SerializeField] [Range(0f, 1f)] private float clickVolume = 0.35f;
        [SerializeField] private float pressedScale = 0.94f;
        [SerializeField] private float pressInSeconds = 0.06f;
        [SerializeField] private float releaseSeconds = 0.15f;
        [Tooltip("Gentle 'look at me' pulse while idle - reserved for the primary action (START, Retry).")]
        [SerializeField] private bool idlePulse;
        [SerializeField] private float idlePulseAmount = 0.045f;
        [SerializeField] private float idlePulseHz = 1.1f;

        private static AudioSource sharedSource;

        private Selectable selectable;
        private Vector3 baseScale = Vector3.one;
        private bool baseCaptured;
        private bool pressed;
        private float pressBlend;      // 0 = rest, 1 = fully pressed
        private bool releasing;
        private float releaseElapsed;

        private static AudioSource SharedSource
        {
            get
            {
                if (sharedSource == null)
                {
                    var go = new GameObject("JuicyButtonAudio");
                    sharedSource = go.AddComponent<AudioSource>();
                    sharedSource.playOnAwake = false;
                }
                return sharedSource;
            }
        }

        private void Awake()
        {
            selectable = GetComponent<Selectable>();
        }

        private void OnEnable()
        {
            if (!baseCaptured)
            {
                baseScale = transform.localScale;
                baseCaptured = true;
            }
            pressed = false;
            releasing = false;
            pressBlend = 0f;
        }

        private void OnDisable()
        {
            if (baseCaptured)
                transform.localScale = baseScale;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            float pressFactor = 1f;
            if (pressed)
            {
                pressBlend = Mathf.MoveTowards(pressBlend, 1f, dt / Mathf.Max(0.01f, pressInSeconds));
                pressFactor = Mathf.Lerp(1f, pressedScale, pressBlend);
            }
            else if (releasing)
            {
                releaseElapsed += dt;
                float k = Juice.OutBack(Mathf.Clamp01(releaseElapsed / Mathf.Max(0.01f, releaseSeconds)));
                pressFactor = Mathf.LerpUnclamped(pressedScale, 1f, k);
                if (releaseElapsed >= releaseSeconds)
                {
                    releasing = false;
                    pressBlend = 0f;
                    pressFactor = 1f;
                }
            }

            float pulse = 1f;
            if (idlePulse && !pressed && !releasing)
                pulse = 1f + idlePulseAmount * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * idlePulseHz);

            transform.localScale = baseScale * (pressFactor * pulse);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (selectable != null && !selectable.IsInteractable())
                return;

            pressed = true;
            releasing = false;
            pressBlend = 0f;

            if (clickSound != null)
            {
                var source = SharedSource;
                source.pitch = Random.Range(0.97f, 1.03f);
                source.PlayOneShot(clickSound, clickVolume);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Release();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Release();
        }

        private void Release()
        {
            if (!pressed)
                return;
            pressed = false;
            releasing = true;
            releaseElapsed = 0f;
        }
    }
}
