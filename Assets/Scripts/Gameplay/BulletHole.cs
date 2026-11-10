using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Gameplay
{
    [RequireComponent(typeof(DecalProjector))]
    public class BulletHole : MonoBehaviour
    {
        [SerializeField] private float lifetime = 15f;
        [SerializeField] private float fadeDuration = 1.5f;
        [SerializeField] private Vector2 sizeRange = new Vector2(0.18f, 0.26f);
        [SerializeField] private int maxHoles = 60;

        private static readonly List<BulletHole> ActiveHoles = new();

        private DecalProjector _projector;
        private float _age;

        // Clears the list when entering Play mode (in case domain reload is turned off)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ActiveHoles.Clear();
        }

        private void Awake()
        {
            _projector = GetComponent<DecalProjector>();

            var size = Random.Range(sizeRange.x, sizeRange.y);
            _projector.size = new Vector3(size, size, _projector.size.z);
            _projector.fadeFactor = 1f;

            ActiveHoles.Add(this);

            // Remove the oldest holes when over the cap
            while (ActiveHoles.Count > maxHoles)
            {
                var oldest = ActiveHoles[0];
                ActiveHoles.RemoveAt(0);
                if (oldest) Destroy(oldest.gameObject);
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;

            var fadeStart = lifetime - fadeDuration;
            if (_age >= fadeStart)
                _projector.fadeFactor = Mathf.Clamp01(1f - (_age - fadeStart) / fadeDuration);

            if (_age >= lifetime)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            ActiveHoles.Remove(this);
        }
    }
}