using Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Bosses
{
    /// <summary>
    /// Things that happen TO the player that the player scripts do not know about: a heavy slow, a push (knockback)
    /// and a white screen (a build-up that closes in from the edges, then a bright flash that clears slowly). Added to
    /// the player automatically by whatever needs it (the boss), so nothing needs setting up.
    ///
    /// The slow works on top of PlayerController.SpeedMultiplier without breaking the other scripts that set it (the
    /// photo camera does): whenever someone else changes the value, that value becomes the new base.
    /// </summary>
    public class PlayerStatus : MonoBehaviour
    {
        private PlayerController _player;
        private CharacterController _controller;
        private Rigidbody _body;

        // Slow
        private float _slowFactor = 1f;
        private float _slowUntil;
        private bool _slowActive;
        private float _base = 1f;
        private float _written = -1f;

        // Knockback (character controller case)
        private Vector3 _push;

        // Whiteout: a build-up while the boss charges (SetBlind), then the flash and a slow recovery
        private RawImage _vignette;
        private Image _solid;
        private Canvas _canvas;
        private bool _flashing;
        private float _flashHold;
        private float _flashFade;
        private float _flashTime;
        private float _blindTarget;
        private float _blindShown;

        /// <summary>How white the screen gets at the very end of a build-up (the flash itself is always 1).</summary>
        private const float BuildupMax = 0.85f;

        /// <summary>The PlayerStatus of this player, added if it does not exist yet.</summary>
        public static PlayerStatus Of(GameObject player)
        {
            if (!player.TryGetComponent<PlayerStatus>(out var status)) status = player.AddComponent<PlayerStatus>();
            return status;
        }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            _controller = GetComponent<CharacterController>();
            _body = GetComponent<Rigidbody>();
        }

        /// <summary>Moves at 'factor' of the normal speed (0.3 = 30%) for 'seconds'. A stronger slow wins.</summary>
        public void Slow(float factor, float seconds)
        {
            if (Time.time < _slowUntil && factor > _slowFactor) factor = _slowFactor;

            _slowFactor = Mathf.Clamp(factor, 0.05f, 1f);
            _slowUntil = Mathf.Max(_slowUntil, Time.time + seconds);
        }

        /// <summary>Pushes the player. 'velocity' is the starting speed in units per second, it fades out by itself.</summary>
        public void Knockback(Vector3 velocity)
        {
            if (_controller && _controller.enabled) _push = velocity;
            else if (_body && !_body.isKinematic) _body.AddForce(velocity, ForceMode.VelocityChange);
        }

        /// <summary>
        /// The build-up: how white the screen is getting, 0..1. The white closes in from the edges towards the centre.
        /// The boss sets it every frame while charging (0 when the player looks away), and it follows smoothly.
        /// </summary>
        public void SetBlind(float amount)
        {
            _blindTarget = Mathf.Clamp01(amount);
            if (_blindTarget > 0f) EnsureFlashOverlay();
        }

        /// <summary>The flash: the whole screen is white for 'hold' seconds, then the vision comes back slowly over 'fade' seconds.</summary>
        public void Flash(float hold, float fade)
        {
            EnsureFlashOverlay();

            _flashHold = hold;
            _flashFade = Mathf.Max(0.01f, fade);
            _flashTime = 0f;
            _flashing = true;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateSlow();
            UpdatePush(dt);
            UpdateFlash(dt);
        }

        private void UpdateSlow()
        {
            if (!_player) return;

            var slowed = Time.time < _slowUntil;

            if (slowed)
            {
                if (!_slowActive)
                {
                    _slowActive = true;
                    _base = _player.SpeedMultiplier;
                    _written = -1f;
                }
                else if (!Mathf.Approximately(_player.SpeedMultiplier, _written))
                {
                    _base = _player.SpeedMultiplier; // somebody else changed it: that is the new base
                }

                _written = _base * _slowFactor;
                _player.SpeedMultiplier = _written;
            }
            else if (_slowActive)
            {
                _slowActive = false;
                _slowFactor = 1f;
                _player.SpeedMultiplier = _base;
            }
        }

        private void UpdatePush(float dt)
        {
            if (_push.sqrMagnitude < 0.04f) return;

            if (_controller && _controller.enabled) _controller.Move(_push * dt);
            _push *= Mathf.Exp(-5f * dt);
        }

        private void UpdateFlash(float dt)
        {
            if (!_canvas) return;

            var flash = 0f;
            if (_flashing)
            {
                _flashTime += dt;
                if (_flashTime <= _flashHold)
                {
                    flash = 1f;
                }
                else
                {
                    var left = 1f - (_flashTime - _flashHold) / _flashFade;
                    flash = left <= 0f ? 0f : Mathf.Pow(left, 1.3f); // lingers a little, then clears
                    if (left <= 0f) _flashing = false;
                }
            }

            // Closes in a bit faster than it opens up again.
            _blindShown = Mathf.MoveTowards(_blindShown, _blindTarget, dt * (_blindTarget > _blindShown ? 4f : 2f));

            ApplyScreen(Mathf.Max(_blindShown * BuildupMax, flash));
        }

        /// <summary>coverage 0 = clear, 1 = all white. The white is a soft ring whose hole shrinks towards the centre.</summary>
        private void ApplyScreen(float coverage)
        {
            var visible = coverage > 0.001f;
            if (_canvas.enabled != visible) _canvas.enabled = visible;
            if (!visible) return;

            // First 70%: the white ring closes in from the edges. The ring image never gets smaller than the screen
            // (a smaller one showed its square edge), so it stops with a small hole in the middle.
            var closing = Mathf.Clamp01(coverage / 0.7f);
            var scale = Mathf.Lerp(1.6f, 0.4f, closing);
            _vignette.rectTransform.localScale = new Vector3(scale, scale, 1f);

            // The last 30%: a uniform white veil fills the middle too. On the way out it lifts first, then the ring opens up.
            var veil = Mathf.Clamp01((coverage - 0.7f) / 0.3f);
            _solid.color = new Color(1f, 1f, 1f, veil * veil * (3f - 2f * veil));
        }

        private void EnsureFlashOverlay()
        {
            if (_canvas) return;

            var canvasGo = new GameObject("PlayerFlashCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 200; // over everything, the HUD included

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Solid white over the whole screen, for the end of the build-up and the flash.
            var solidGo = new GameObject("Solid", typeof(RectTransform), typeof(Image));
            solidGo.transform.SetParent(canvasGo.transform, false);
            _solid = solidGo.GetComponent<Image>();
            _solid.raycastTarget = false;
            _solid.color = new Color(1f, 1f, 1f, 0f);
            var solidRect = _solid.rectTransform;
            solidRect.anchorMin = Vector2.zero;
            solidRect.anchorMax = Vector2.one;
            solidRect.offsetMin = solidRect.offsetMax = Vector2.zero;

            // A huge ring: clear in the middle, soft edge, solid white outside. Scaling it down closes the white in.
            var vignetteGo = new GameObject("Vignette", typeof(RectTransform), typeof(RawImage));
            vignetteGo.transform.SetParent(canvasGo.transform, false);
            _vignette = vignetteGo.GetComponent<RawImage>();
            _vignette.raycastTarget = false;
            _vignette.texture = MakeVignetteTexture();
            var rect = _vignette.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(8000f, 8000f);

            _canvas.enabled = false;
        }

        private static Texture2D MakeVignetteTexture()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = (x + 0.5f) / size * 2f - 1f;
                    var dy = (y + 0.5f) / size * 2f - 1f;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy);
                    var a = Mathf.Clamp01((distance - 0.2f) / 0.25f);
                    a = a * a * (3f - 2f * a);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}