using Core;
using Events;
using UnityEngine;
using UnityEngine.UI;

namespace Items
{
    /// <summary>
    /// The small text under the crosshair: "E  Bandage x2" while you look at a pickup, and short notices
    /// ("Inventory full", "Picked up Bandage x2"). It only listens to the event bus. Put it on any object, it builds
    /// its own canvas.
    /// </summary>
    public class InteractPromptView : MonoBehaviour
    {
        [SerializeField] private float noticeSeconds = 2f;
        [SerializeField] private float promptOffsetY = -90f;
        [SerializeField] private float noticeOffsetY = -140f;

        private EventBus _bus;
        private Text _prompt;
        private Text _notice;
        private float _noticeUntil;

        private void Awake()
        {
            var canvasGo = new GameObject("InteractPromptCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _prompt = MakeText(canvasGo.transform, font, 34, promptOffsetY);
            _notice = MakeText(canvasGo.transform, font, 28, noticeOffsetY);
            _prompt.gameObject.SetActive(false);
            _notice.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (!GameServices.TryGet(out _bus)) return;

            _bus.Subscribe<InteractPromptEvent>(OnPrompt);
            _bus.Subscribe<NoticeEvent>(OnNotice);
            _bus.Subscribe<ItemCollectedEvent>(OnCollected);
        }

        private void OnDisable()
        {
            if (_bus == null) return;

            _bus.Unsubscribe<InteractPromptEvent>(OnPrompt);
            _bus.Unsubscribe<NoticeEvent>(OnNotice);
            _bus.Unsubscribe<ItemCollectedEvent>(OnCollected);
            _bus = null;
        }

        private void Update()
        {
            // Unscaled: notices must also run out while the game is paused.
            if (_notice.gameObject.activeSelf && Time.unscaledTime >= _noticeUntil) _notice.gameObject.SetActive(false);
        }

        private void OnPrompt(InteractPromptEvent e)
        {
            _prompt.gameObject.SetActive(e.Visible);
            if (e.Visible) _prompt.text = "[E]  " + e.Text;
        }

        private void OnNotice(NoticeEvent e) => ShowNotice(e.Message);

        private void OnCollected(ItemCollectedEvent e)
        {
            ShowNotice(e.Count > 1 ? $"Picked up {e.DisplayName}  x{e.Count}" : $"Picked up {e.DisplayName}");
        }

        private void ShowNotice(string message)
        {
            _notice.text = message;
            _notice.gameObject.SetActive(true);
            _noticeUntil = Time.unscaledTime + noticeSeconds;
        }

        private static Text MakeText(Transform parent, Font font, int size, float offsetY)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, offsetY);
            rect.sizeDelta = new Vector2(1000f, 50f);

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;

            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            return text;
        }
    }
}