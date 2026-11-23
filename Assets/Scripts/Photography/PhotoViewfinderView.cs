using Core;
using Events;
using Menus;
using UnityEngine;
using UnityEngine.UI;

namespace Photography
{
    /// <summary>
    /// The viewfinder on screen: a dimmed screen with a clear square frame while the camera is up, the white flash
    /// when a photo is taken, and the score popup. It only listens to the event bus. Put it on any object (the HUD);
    /// it builds its own canvas.
    /// </summary>
    public class PhotoViewfinderView : MonoBehaviour
    {
        [SerializeField] private float popupSeconds = 3f;
        [Tooltip("How dark everything outside the frame gets while the camera is up (0 = clear, 1 = pitch black).")]
        [SerializeField, Range(0f, 1f)] private float lensOutsideDarkness = 1f;
        [Tooltip("How dark everything outside the frame is while night vision (Reveal) is on. 1 = pitch black, so only the lens shows the green view.")]
        [SerializeField, Range(0f, 1f)] private float nightOutsideDarkness = 1f;
        [Tooltip("Shows the equipped flash ability and its cooldown. Empty = found automatically.")]
        [SerializeField] private CameraController photoCamera;
        [Tooltip("Currency Suffix")] //Let's go with руб. for now, as in the 1970s the ₽ sign did not exist yet. Might change later.
        [SerializeField] private string currencySuffix = " руб.";

        private EventBus _bus;
        private GameObject _viewfinder;
        private RectTransform _dimTop, _dimBottom, _dimLeft, _dimRight;
        private RectTransform _lineTop, _lineBottom, _lineLeft, _lineRight;
        private Image _flash;
        private Text _popup;
        private Text _abilityText;
        private Image[] _dims;
        private float _flashAlpha;
        private float _popupUntil;

        private void Awake()
        {
            var canvasGo = new GameObject("ViewfinderCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _viewfinder = new GameObject("Viewfinder", typeof(RectTransform));
            _viewfinder.transform.SetParent(canvasGo.transform, false);
            Stretch(_viewfinder.GetComponent<RectTransform>());

            var dim = new Color(0f, 0f, 0f, lensOutsideDarkness);
            _dimTop = MakeImage(_viewfinder.transform, "DimTop", dim);
            _dimBottom = MakeImage(_viewfinder.transform, "DimBottom", dim);
            _dimLeft = MakeImage(_viewfinder.transform, "DimLeft", dim);
            _dimRight = MakeImage(_viewfinder.transform, "DimRight", dim);

            _dims = new[] { _dimTop.GetComponent<Image>(), _dimBottom.GetComponent<Image>(), _dimLeft.GetComponent<Image>(), _dimRight.GetComponent<Image>() };

            var line = new Color(1f, 1f, 1f, 0.9f);
            _lineTop = MakeImage(_viewfinder.transform, "LineTop", line);
            _lineBottom = MakeImage(_viewfinder.transform, "LineBottom", line);
            _lineLeft = MakeImage(_viewfinder.transform, "LineLeft", line);
            _lineRight = MakeImage(_viewfinder.transform, "LineRight", line);
            _viewfinder.SetActive(false);

            var flashRect = MakeImage(canvasGo.transform, "Flash", new Color(1f, 1f, 1f, 0f));
            Stretch(flashRect);
            _flash = flashRect.GetComponent<Image>();
            _flash.raycastTarget = false;

            var font = KadrFont.Body;
            var popupGo = new GameObject("Popup", typeof(RectTransform), typeof(Text), typeof(Outline));
            popupGo.transform.SetParent(canvasGo.transform, false);
            var popupRect = popupGo.GetComponent<RectTransform>();
            popupRect.anchorMin = popupRect.anchorMax = new Vector2(0.5f, 0.12f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.sizeDelta = new Vector2(1100f, 260f);

            _popup = popupGo.GetComponent<Text>();
            _popup.font = font;
            _popup.fontSize = 34;
            _popup.alignment = TextAnchor.MiddleCenter;
            _popup.color = Color.white;
            _popup.supportRichText = true;
            _popup.raycastTarget = false;
            popupGo.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            _popup.gameObject.SetActive(false);

            // The equipped flash ability and its cooldown, top centre while the camera is up.
            var abilityGo = new GameObject("Ability", typeof(RectTransform), typeof(Text), typeof(Outline));
            abilityGo.transform.SetParent(_viewfinder.transform, false);
            var abilityRect = abilityGo.GetComponent<RectTransform>();
            abilityRect.anchorMin = abilityRect.anchorMax = new Vector2(0.5f, 0.93f);
            abilityRect.pivot = new Vector2(0.5f, 0.5f);
            abilityRect.sizeDelta = new Vector2(700f, 50f);

            _abilityText = abilityGo.GetComponent<Text>();
            _abilityText.font = font;
            _abilityText.fontSize = 28;
            _abilityText.alignment = TextAnchor.MiddleCenter;
            _abilityText.raycastTarget = false;
            abilityGo.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
        }

        private void OnEnable()
        {
            if (!GameServices.TryGet(out _bus)) return;

            _bus.Subscribe<CameraRaisedEvent>(OnRaised);
            _bus.Subscribe<PhotoTakenEvent>(OnPhoto);
            _bus.Subscribe<NightVisionEvent>(OnNightVision);
        }

        private void OnDisable()
        {
            if (_bus == null) return;

            _bus.Unsubscribe<CameraRaisedEvent>(OnRaised);
            _bus.Unsubscribe<PhotoTakenEvent>(OnPhoto);
            _bus.Unsubscribe<NightVisionEvent>(OnNightVision);
            _bus = null;
        }

        private void Update()
        {
            // Unscaled, so the flash and popup also finish while the game is paused.
            if (_flashAlpha > 0f)
            {
                _flashAlpha = Mathf.Max(0f, _flashAlpha - Time.unscaledDeltaTime * 3f);
                _flash.color = new Color(1f, 1f, 1f, _flashAlpha);
            }

            if (_popup.gameObject.activeSelf && Time.unscaledTime >= _popupUntil) _popup.gameObject.SetActive(false);

            if (_viewfinder.activeSelf) UpdateAbilityLabel();
        }

        private void UpdateAbilityLabel()
        {
            if (!photoCamera) photoCamera = FindAnyObjectByType<CameraController>();

            var ability = photoCamera ? photoCamera.EquippedAbility : CameraAbility.None;
            var data = ability == CameraAbility.None || !CameraUpgradeTree.Active ? null : CameraUpgradeTree.Active.Ability(ability);
            if (data == null)
            {
                _abilityText.text = "";
                return;
            }

            var left = photoCamera.AbilityCooldownLeft;
            _abilityText.text = left <= 0f
                ? $"{data.title}  <color=#80e090>READY</color>"
                : $"{data.title}  <color=#aaaaaa>{left:0.0} s</color>";
            _abilityText.supportRichText = true;
            _abilityText.color = Color.white;
        }

        private void OnNightVision(NightVisionEvent e)
        {
            var colour = new Color(0f, 0f, 0f, e.Active ? nightOutsideDarkness : lensOutsideDarkness);
            foreach (var dim in _dims) dim.color = colour;
        }

        private void OnRaised(CameraRaisedEvent e)
        {
            _viewfinder.SetActive(e.Raised);
            if (e.Raised) LayoutFrame(e.FrameFraction);
        }

        private void OnPhoto(PhotoTakenEvent e)
        {
            _flashAlpha = 0.85f;

            var photo = e.Photo;
            string text;

            if (photo.Score <= 0f)
            {
                text = "<color=#bbbbbb>Nothing worth a photo</color>";
            }
            else
            {
                var lines = new System.Text.StringBuilder();
                foreach (var s in photo.Subjects)
                {
                    lines.Append(s.Name).Append("   ").Append(Mathf.RoundToInt(s.Score)).Append(currencySuffix);
                    if (s.Coverage < 0.98f) lines.Append("   <color=#a0c0e0>").Append(Mathf.RoundToInt(s.Coverage * 100f)).Append("% in frame</color>");
                    if (s.Special > 1f) lines.Append("   <color=#a0e0a0>bonus x").Append(s.Special.ToString("0.##")).Append("</color>");
                    if (s.Repeat < 1f) lines.Append("   <color=#e0a060>seen before x").Append(s.Repeat.ToString("0.##")).Append("</color>");
                    lines.Append('\n');
                }

                lines.Append("<b>PHOTO   ").Append(Mathf.RoundToInt(photo.Score)).Append(currencySuffix).Append("</b>");
                if (!e.Kept) lines.Append("\n<color=#e06060>Not kept: the album holds better photos</color>");
                text = lines.ToString();
            }

            _popup.text = text;
            _popup.gameObject.SetActive(true);
            _popupUntil = Time.unscaledTime + popupSeconds;
        }

        /// <summary>Places the dimming panels and the frame lines around a centred square of the given height.</summary>
        private void LayoutFrame(float fraction)
        {
            var width = fraction * Screen.height / Screen.width;
            float l = 0.5f - width * 0.5f, r = 0.5f + width * 0.5f;
            float b = 0.5f - fraction * 0.5f, t = 0.5f + fraction * 0.5f;

            Place(_dimTop, new Vector2(0f, t), new Vector2(1f, 1f), Vector2.zero);
            Place(_dimBottom, new Vector2(0f, 0f), new Vector2(1f, b), Vector2.zero);
            Place(_dimLeft, new Vector2(0f, b), new Vector2(l, t), Vector2.zero);
            Place(_dimRight, new Vector2(r, b), new Vector2(1f, t), Vector2.zero);

            Place(_lineTop, new Vector2(l, t), new Vector2(r, t), new Vector2(0f, 3f));
            Place(_lineBottom, new Vector2(l, b), new Vector2(r, b), new Vector2(0f, 3f));
            Place(_lineLeft, new Vector2(l, b), new Vector2(l, t), new Vector2(3f, 0f));
            Place(_lineRight, new Vector2(r, b), new Vector2(r, t), new Vector2(3f, 0f));
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        private static RectTransform MakeImage(Transform parent, string objectName, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}