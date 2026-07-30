using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace RingSport.UI
{
    /// <summary>
    /// Full-screen celebration overlay revealed when the player crosses the
    /// finish line of the LAST level: a big love note with the secret word,
    /// popping in under a shower of confetti. Lives on its own always-on-top
    /// canvas so it can appear over the reward screen (game flow) or the home
    /// screen (debug menu). Everything animates on unscaled time because the
    /// reward screen freezes Time.timeScale. Scene object built by
    /// Tools > RingSport > Setup Secret Note.
    /// </summary>
    public class SecretNotePanel : MonoBehaviour
    {
        [Header("Wired by SecretNoteSetup")]
        [SerializeField] private RectTransform noteRoot;
        [SerializeField] private Image scrim;
        [SerializeField] private RectTransform confettiRoot;
        [SerializeField] private TextMeshProUGUI hintText;
        [SerializeField] private Button dismissButton;

        [Header("Note Entrance")]
        [Tooltip("Resting hand-placed tilt of the note, in degrees.")]
        [SerializeField] private float noteRestTilt = -4f;
        [SerializeField] private float notePopDuration = 0.55f;
        [SerializeField] private float scrimFadeDuration = 0.3f;
        [Tooltip("Taps this early are ignored so finish-line jump mashing can't skip the reveal.")]
        [SerializeField] private float minOpenTimeToDismiss = 0.6f;
        [SerializeField] private float hintDelay = 1.1f;

        [Header("Audio")]
        [Tooltip("Reveal sting as the note pops in (temporary clip - see SOUND_EFFECTS.md).")]
        [SerializeField] private AudioClip revealSound;
        [SerializeField] [Range(0f, 1f)] private float revealVolume = 0.9f;

        [Header("Confetti")]
        [Tooltip("Pieces that pop outward from the note when it lands.")]
        [SerializeField] private int burstPieceCount = 36;
        [Tooltip("Pieces that rain in from the top while the panel is open.")]
        [SerializeField] private int rainPieceCount = 44;
        [SerializeField] private Vector2 fallSpeedRange = new Vector2(280f, 620f);
        [SerializeField] private Vector2 burstSpeedRange = new Vector2(500f, 1150f);
        [SerializeField] private Color[] confettiColors =
        {
            new Color(0.91f, 0.30f, 0.24f), // red
            new Color(0.96f, 0.65f, 0.14f), // orange
            new Color(0.96f, 0.90f, 0.35f), // yellow
            new Color(0.42f, 0.80f, 0.42f), // green
            new Color(0.29f, 0.56f, 0.89f), // blue
            new Color(0.78f, 0.44f, 0.86f), // purple
            new Color(0.95f, 0.55f, 0.75f), // pink
            Color.white,
        };

        private class ConfettiPiece
        {
            public RectTransform rect;
            public Image image;
            public Vector2 position;
            public Vector2 velocity;       // damps toward (0, -fallSpeed)
            public float fallSpeed;
            public float swayVelocity;     // px/s sideways sine
            public float swayFrequency;
            public float swayPhase;
            public float spinSpeed;        // deg/s
            public float spin;
            public float flutterFrequency;
            public float flutterPhase;
        }

        private readonly List<ConfettiPiece> pieces = new List<ConfettiPiece>();
        private float openTime;
        private float scrimTargetAlpha = 0.94f;
        private AudioSource audioSource;

        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            if (scrim != null)
                scrimTargetAlpha = scrim.color.a;

            if (dismissButton != null)
                dismissButton.onClick.AddListener(OnDismissPressed);

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
        }

        public void Open()
        {
            gameObject.SetActive(true);
            openTime = 0f;
            ApplyEntranceFrame();

            if (revealSound != null && audioSource != null)
                audioSource.PlayOneShot(revealSound, revealVolume);

            EnsureConfettiPieces();
            Rect area = ConfettiArea();
            for (int i = 0; i < pieces.Count; i++)
                ResetPiece(pieces[i], area, burst: i < burstPieceCount);
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void OnDismissPressed()
        {
            if (openTime >= minOpenTimeToDismiss)
                Close();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            openTime += dt;

            UpdateEntrance();
            UpdateConfetti(dt);
        }

        /// <summary>First-frame pose so the fully-open state never flashes before the pop-in.</summary>
        private void ApplyEntranceFrame()
        {
            if (noteRoot != null)
            {
                noteRoot.localScale = Vector3.zero;
                noteRoot.localRotation = Quaternion.Euler(0f, 0f, noteRestTilt - 14f);
            }

            if (scrim != null)
            {
                Color c = scrim.color;
                c.a = 0f;
                scrim.color = c;
            }

            if (hintText != null)
            {
                Color c = hintText.color;
                c.a = 0f;
                hintText.color = c;
            }
        }

        private void UpdateEntrance()
        {
            if (noteRoot != null)
            {
                float t = notePopDuration > 0f ? Mathf.Clamp01(openTime / notePopDuration) : 1f;
                float eased = EaseOutBack(t);
                noteRoot.localScale = Vector3.one * Mathf.Max(0.0001f, eased);
                // Settle from an exaggerated tilt into the resting one, riding
                // the same overshoot as the scale
                float tilt = Mathf.LerpUnclamped(noteRestTilt - 14f, noteRestTilt, eased);
                noteRoot.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }

            if (scrim != null)
            {
                float fade = scrimFadeDuration > 0f ? Mathf.Clamp01(openTime / scrimFadeDuration) : 1f;
                Color c = scrim.color;
                c.a = scrimTargetAlpha * fade;
                scrim.color = c;
            }

            if (hintText != null)
            {
                float show = Mathf.Clamp01((openTime - hintDelay) / 0.4f);
                float pulse = 0.5f + 0.5f * Mathf.Sin((openTime - hintDelay) * 3.2f);
                Color c = hintText.color;
                c.a = show * Mathf.Lerp(0.35f, 0.9f, pulse);
                hintText.color = c;
            }
        }

        // ------------------------------------------------------------------
        // Confetti
        // ------------------------------------------------------------------

        private void EnsureConfettiPieces()
        {
            if (confettiRoot == null)
                return;

            int total = burstPieceCount + rainPieceCount;
            while (pieces.Count < total)
            {
                var go = new GameObject("Confetti", typeof(RectTransform));
                go.layer = gameObject.layer;
                var rect = (RectTransform)go.transform;
                rect.SetParent(confettiRoot, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                // Sprite-less Image renders a plain tinted rectangle
                var image = go.AddComponent<Image>();
                image.raycastTarget = false;

                pieces.Add(new ConfettiPiece { rect = rect, image = image });
            }
        }

        /// <summary>
        /// Spawn/travel bounds in the container's local space. Falls back to the
        /// 1080x1920 design rect on the first frame, before layout has run.
        /// </summary>
        private Rect ConfettiArea()
        {
            Rect area = confettiRoot != null ? confettiRoot.rect : default;
            if (area.width < 1f || area.height < 1f)
                area = new Rect(-540f, -960f, 1080f, 1920f);
            return area;
        }

        private void ResetPiece(ConfettiPiece piece, Rect area, bool burst)
        {
            piece.fallSpeed = Random.Range(fallSpeedRange.x, fallSpeedRange.y);
            piece.swayVelocity = Random.Range(30f, 120f);
            piece.swayFrequency = Random.Range(1.2f, 3.5f);
            piece.swayPhase = Random.Range(0f, Mathf.PI * 2f);
            piece.spinSpeed = Random.Range(-260f, 260f);
            piece.spin = Random.Range(0f, 360f);
            piece.flutterFrequency = Random.Range(4f, 9f);
            piece.flutterPhase = Random.Range(0f, Mathf.PI * 2f);

            if (burst)
            {
                // Firework pop from the note, damping into a normal fall
                Vector2 origin = noteRoot != null ? noteRoot.anchoredPosition : Vector2.zero;
                piece.position = origin + Random.insideUnitCircle * 40f;
                Vector2 direction = Random.insideUnitCircle.normalized;
                direction.y = Mathf.Abs(direction.y) * 1.35f;
                piece.velocity = direction.normalized * Random.Range(burstSpeedRange.x, burstSpeedRange.y);
            }
            else
            {
                // Rain in from above the top edge over the next couple of seconds
                piece.position = new Vector2(
                    Random.Range(area.xMin, area.xMax),
                    area.yMax + Random.Range(30f, 900f));
                piece.velocity = new Vector2(0f, -piece.fallSpeed);
            }

            piece.rect.sizeDelta = new Vector2(Random.Range(16f, 34f), Random.Range(10f, 20f));
            piece.rect.anchoredPosition = piece.position;
            piece.rect.localRotation = Quaternion.Euler(0f, 0f, piece.spin);

            if (piece.image != null && confettiColors.Length > 0)
                piece.image.color = confettiColors[Random.Range(0, confettiColors.Length)];
        }

        private void UpdateConfetti(float dt)
        {
            if (pieces.Count == 0)
                return;

            Rect area = ConfettiArea();
            float recycleY = area.yMin - 80f;

            foreach (ConfettiPiece piece in pieces)
            {
                // Burst velocity decays into a steady terminal fall
                Vector2 terminal = new Vector2(0f, -piece.fallSpeed);
                piece.velocity = Vector2.Lerp(piece.velocity, terminal, 1f - Mathf.Exp(-2.6f * dt));

                float sway = piece.swayVelocity * Mathf.Sin(openTime * piece.swayFrequency + piece.swayPhase);
                piece.position += new Vector2(piece.velocity.x + sway, piece.velocity.y) * dt;
                piece.spin += piece.spinSpeed * dt;

                if (piece.position.y < recycleY)
                {
                    // Re-enter from the top so the shower lasts while the panel is open
                    piece.position = new Vector2(
                        Random.Range(area.xMin, area.xMax),
                        area.yMax + Random.Range(20f, 240f));
                    piece.velocity = new Vector2(0f, -piece.fallSpeed);
                }

                piece.rect.anchoredPosition = piece.position;
                piece.rect.localRotation = Quaternion.Euler(0f, 0f, piece.spin);

                // Tumbling-card flutter: the width squashes as the piece "turns over"
                float flutter = 0.25f + 0.75f * Mathf.Abs(Mathf.Sin(openTime * piece.flutterFrequency + piece.flutterPhase));
                piece.rect.localScale = new Vector3(flutter, 1f, 1f);
            }
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            t -= 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }
    }
}
