using Core;
using Events;
using Gameplay;
using UnityEngine;

namespace Enemies
{
    /// <summary>
    /// Makes an enemy look hurt. Added automatically to every EnemyController, nothing to set up.
    ///
    ///  - A quick red flash on every hit (shots, fire ticks, other enemies).
    ///  - A lasting wounded look: the more health it has lost, the darker and redder the skin.
    ///
    /// It tints the material's base colour through a MaterialPropertyBlock, so it keeps every texture and needs no new
    /// shader or material. That works with any material that has a _BaseColor (URP Lit) or _Color property. A material
    /// with neither is skipped. A real wound shader (blood and torn skin that fade in by health) needs a mask texture
    /// and UVs on the model, so it is a separate job.
    /// </summary>
    public class EnemyHurtVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("The colour the skin is multiplied by at the moment of a hit. Values above 1 make it brighter.")]
        [SerializeField] private Color flashColor = new(2.2f, 0.35f, 0.35f);
        [SerializeField, Min(0.05f)] private float flashSeconds = 0.2f;
        [Tooltip("The skin tint at 0 health. It fades in as health drops.")]
        [SerializeField] private Color woundTint = new(0.7f, 0.42f, 0.42f);

        private struct Slot
        {
            public Renderer Renderer;
            public int Index;
            public Color BaseColor;
            public bool UsesBaseColor;
        }

        private Slot[] _slots;
        private Health _health;
        private EventBus _bus;
        private MaterialPropertyBlock _block;
        private float _flash;
        private float _wound;
        private bool _tinted;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _block = new MaterialPropertyBlock();

            var list = new System.Collections.Generic.List<Slot>();
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue;

                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (!material) continue;

                    if (material.HasProperty(BaseColorId))
                        list.Add(new Slot { Renderer = renderer, Index = i, BaseColor = material.GetColor(BaseColorId), UsesBaseColor = true });
                    else if (material.HasProperty(ColorId))
                        list.Add(new Slot { Renderer = renderer, Index = i, BaseColor = material.GetColor(ColorId) });
                }
            }

            _slots = list.ToArray();
        }

        private void OnEnable()
        {
            if (GameServices.TryGet(out _bus)) _bus.Subscribe<HealthChangedEvent>(OnHealthChanged);
        }

        private void OnDisable()
        {
            if (_bus != null) _bus.Unsubscribe<HealthChangedEvent>(OnHealthChanged);
            _bus = null;
        }

        private void OnHealthChanged(HealthChangedEvent e)
        {
            if (e.Entity == gameObject && e.Delta < 0f) _flash = 1f;
        }

        private void Update()
        {
            var lost = _health.Max > 0f ? 1f - Mathf.Clamp01(_health.Current / _health.Max) : 0f;
            _wound = Mathf.MoveTowards(_wound, lost, Time.deltaTime * 2f);
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime / flashSeconds);

            if (_wound <= 0.001f && _flash <= 0f)
            {
                if (_tinted) Apply(Color.white, false);
                return;
            }

            var tint = Color.Lerp(Color.white, woundTint, _wound);
            tint = Color.Lerp(tint, flashColor, _flash);
            Apply(tint, true);
        }

        private void Apply(Color tint, bool on)
        {
            _tinted = on;

            foreach (var slot in _slots)
            {
                if (!slot.Renderer) continue;

                if (!on)
                {
                    slot.Renderer.SetPropertyBlock(null, slot.Index);
                    continue;
                }

                _block.Clear();
                var colour = slot.BaseColor * tint;
                colour.a = slot.BaseColor.a;
                _block.SetColor(slot.UsesBaseColor ? BaseColorId : ColorId, colour);
                slot.Renderer.SetPropertyBlock(_block, slot.Index);
            }
        }
    }
}