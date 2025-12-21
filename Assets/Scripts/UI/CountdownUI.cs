using UnityEngine;
using TMPro;
using System;
using System.Collections;

namespace RingSport.UI
{
    /// <summary>
    /// Reusable countdown UI component. Displays animated countdown (3, 2, 1...) and invokes callback on complete.
    /// </summary>
    public class CountdownUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private GameObject panel;
        [SerializeField] private TextMeshProUGUI countdownText;

        [Header("Countdown Settings")]
        [SerializeField] private float duration = 3f;
        [SerializeField] private string[] numbers = { "3", "2", "1" };
        [SerializeField] private AnimationCurve scaleAnimation = AnimationCurve.EaseInOut(0f, 1.5f, 1f, 1f);

        private Coroutine countdownCoroutine;
        private Action onComplete;

        // Note: Removed Awake() that called Hide() - this was interfering with UIManager's
        // countdown system which directly manages this panel's active state.

        private void OnDisable()
        {
            if (countdownCoroutine != null)
            {
                Debug.LogWarning("[CountdownUI] GameObject disabled while countdown was running! Stack trace:");
                Debug.Log(System.Environment.StackTrace);
            }
        }

        /// <summary>
        /// Starts the countdown and invokes callback when complete
        /// </summary>
        public void StartCountdown(Action onCompleteCallback)
        {
            onComplete = onCompleteCallback;

            // Ensure this component's gameObject is enabled so coroutines can run
            gameObject.SetActive(true);

            if (panel != null)
                panel.SetActive(true);

            if (countdownCoroutine != null)
                StopCoroutine(countdownCoroutine);

            Debug.Log("[CountdownUI] Starting countdown coroutine");
            countdownCoroutine = StartCoroutine(CountdownRoutine());
        }

        /// <summary>
        /// Stops and hides the countdown
        /// </summary>
        public void Hide()
        {
            if (countdownCoroutine != null)
            {
                Debug.Log("[CountdownUI] Hide() called - stopping active coroutine. Stack trace:");
                Debug.Log(System.Environment.StackTrace);
                StopCoroutine(countdownCoroutine);
                countdownCoroutine = null;
            }

            if (panel != null)
                panel.SetActive(false);
        }

        private IEnumerator CountdownRoutine()
        {
            float timePerNumber = duration / numbers.Length;
            Debug.Log($"[CountdownUI] Countdown starting - {numbers.Length} numbers, {timePerNumber}s each");

            for (int i = 0; i < numbers.Length; i++)
            {
                if (countdownText != null)
                    countdownText.text = numbers[i];

                float elapsed = 0f;
                while (elapsed < timePerNumber)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float normalizedTime = elapsed / timePerNumber;
                    float scale = scaleAnimation.Evaluate(normalizedTime);

                    if (countdownText != null)
                        countdownText.transform.localScale = Vector3.one * scale;

                    yield return null;
                }
            }

            Debug.Log("[CountdownUI] Countdown complete, invoking callback");
            countdownCoroutine = null;
            Hide();
            onComplete?.Invoke();
        }
    }
}
