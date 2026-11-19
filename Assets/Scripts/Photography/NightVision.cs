using Core;
using Events;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Photography
{
    /// <summary>
    /// The Reveal ability: a green night vision look that also lifts the darkness. Lives next to the CameraController
    /// (added automatically). Made of a global post processing Volume (green colour filter, brighter exposure,
    /// vignette) plus a temporary flat ambient light, so dark rooms show their shapes. It fades in and out, and ends
    /// by itself after its time or when the camera is lowered.
    ///
    /// It needs Post Processing on the main camera (the Camera component, Rendering > Post Processing). The camera
    /// controller switches that on when it starts night vision.
    /// </summary>
    public class NightVision : MonoBehaviour
    {
        [SerializeField] private float postExposure = 2.4f;
        [SerializeField] private Color tint = new(0.45f, 1f, 0.5f);
        [SerializeField] private float vignette = 0.35f;
        [Tooltip("Ambient light while it is on. This is what lets you see in a dark room.")]
        [SerializeField] private Color ambient = new(0.22f, 0.32f, 0.22f);
        [SerializeField] private float fadeSeconds = 0.25f;

        private Volume _volume;
        private VolumeProfile _profile;
        private AmbientMode _oldMode;
        private Color _oldAmbient;
        private bool _saved;

        private bool _active;
        private float _until;
        private float _weight;

        public bool IsActive => _active;

        /// <summary>Starts (or extends) night vision for this many game seconds.</summary>
        public void Begin(float seconds)
        {
            _until = Time.time + seconds;
            if (_active) return;

            _active = true;
            EnsureVolume();
            Save();
            Publish(true);
        }

        public void End()
        {
            if (!_active) return;

            _active = false;
            Publish(false);
        }

        private void Update()
        {
            if (_active && Time.time >= _until) End();

            var target = _active ? 1f : 0f;
            if (Mathf.Approximately(_weight, target) && !_saved) return;

            _weight = Mathf.MoveTowards(_weight, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));

            if (_volume) _volume.weight = _weight;
            if (_saved) RenderSettings.ambientLight = Color.Lerp(_oldAmbient, ambient, _weight);

            if (_weight <= 0f && !_active) Restore();
        }

        private void OnDisable()
        {
            End();
            _weight = 0f;
            Restore();
        }

        private void OnDestroy()
        {
            if (_volume) Destroy(_volume.gameObject);
            if (_profile) Destroy(_profile);
        }

        private void EnsureVolume()
        {
            if (_volume) return;

            var go = new GameObject("NightVisionVolume");
            go.transform.SetParent(transform, false);

            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;
            _volume.weight = 0f;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var colour = _profile.Add<ColorAdjustments>(true);
            colour.postExposure.Override(postExposure);
            colour.colorFilter.Override(tint);
            colour.contrast.Override(15f);

            var vig = _profile.Add<Vignette>(true);
            vig.intensity.Override(vignette);

            _volume.sharedProfile = _profile;
        }

        private void Save()
        {
            if (_saved) return;

            _oldMode = RenderSettings.ambientMode;
            _oldAmbient = RenderSettings.ambientLight;
            _saved = true;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = _oldAmbient;
        }

        private void Restore()
        {
            if (!_saved) return;

            RenderSettings.ambientMode = _oldMode;
            RenderSettings.ambientLight = _oldAmbient;
            _saved = false;
        }

        private static void Publish(bool active)
        {
            if (GameServices.TryGet(out EventBus bus)) bus.Publish(new NightVisionEvent(active));
        }
    }
}