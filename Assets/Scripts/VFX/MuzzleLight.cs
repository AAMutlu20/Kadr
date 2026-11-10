using UnityEngine;

namespace VFX
{
    [RequireComponent(typeof(Light))]
    public class MuzzleLight : MonoBehaviour
    {
        [SerializeField] private float duration = 0.06f;

        private Light _lightSource;
        private float _baseIntensity;
        private float _timer;

        private void Awake()
        {
            _lightSource = GetComponent<Light>();
            _baseIntensity = _lightSource.intensity;
            _lightSource.enabled = false;
        }

        public void Flash()
        {
            _timer = duration;
            _lightSource.intensity = _baseIntensity;
            _lightSource.enabled = true;
        }

        private void Update()
        {
            if (!_lightSource.enabled) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _lightSource.enabled = false;
                return;
            }

            _lightSource.intensity = _baseIntensity * (_timer / duration); // quickly fade out
        }
    }
}