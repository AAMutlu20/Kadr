using System.Collections.Generic;
using Menus;
using UnityEngine;
using UnityEngine.UI;

namespace Bosses
{
    /// <summary>
    /// All the boss's attack visuals, drawn in code so nothing has to be made or assigned: the telegraph on the floor
    /// (how far an attack reaches, and when it lands), the stomp shockwave, the stun charge and the stun flash.
    /// Any of them can be replaced by your own prefab on the BossController. Every effect owns its materials and meshes
    /// and cleans up after itself. It only needs the built-in "Sprites/Default" shader, so it works in URP.
    /// </summary>
    public static class BossVfx
    {
        private static Texture2D _soft;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _soft = null;

        /// <summary>A white dot with soft edges, for glows and dust.</summary>
        public static Texture2D Soft
        {
            get
            {
                if (_soft) return _soft;

                const int size = 64;
                _soft = new Texture2D(size, size, TextureFormat.RGBA32, false)
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
                        var a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        a = a * a * (3f - 2f * a);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }

                _soft.SetPixels32(pixels);
                _soft.Apply();
                return _soft;
            }
        }

        /// <summary>An unlit, see-through material. 'color' includes the alpha.</summary>
        public static Material MakeMaterial(Color color, bool soft)
        {
            var shader = Shader.Find("Sprites/Default");
            if (!shader) shader = Shader.Find("UI/Default");

            var material = new Material(shader) { color = color, hideFlags = HideFlags.HideAndDontSave };
            if (soft) material.mainTexture = Soft;
            return material;
        }

        // ---------- meshes (unit size, so a transform scale sets the real size) ----------

        /// <summary>A pie slice on the floor, radius 1, pointing along +Z. Half angle 180 = a full disc.</summary>
        public static Mesh Fan(float halfAngleDegrees, int segments)
        {
            var vertices = new List<Vector3> { Vector3.zero };
            var triangles = new List<int>();

            for (var i = 0; i <= segments; i++)
            {
                var angle = Mathf.Lerp(-halfAngleDegrees, halfAngleDegrees, i / (float)segments) * Mathf.Deg2Rad;
                vertices.Add(new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)));
                if (i < segments)
                {
                    triangles.Add(0);
                    triangles.Add(i + 1);
                    triangles.Add(i + 2);
                }
            }

            return Build(vertices, triangles);
        }

        /// <summary>A flat ring, outer radius 1. Lying on the floor, or standing up (vertical) to face the camera.</summary>
        public static Mesh Annulus(float inner, float outer, int segments, bool vertical)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (var i = 0; i <= segments; i++)
            {
                var angle = i / (float)segments * Mathf.PI * 2f;
                var c = Mathf.Cos(angle);
                var s = Mathf.Sin(angle);
                vertices.Add(vertical ? new Vector3(c * outer, s * outer, 0f) : new Vector3(s * outer, 0f, c * outer));
                vertices.Add(vertical ? new Vector3(c * inner, s * inner, 0f) : new Vector3(s * inner, 0f, c * inner));

                if (i < segments)
                {
                    var a = i * 2;
                    triangles.Add(a);
                    triangles.Add(a + 1);
                    triangles.Add(a + 2);
                    triangles.Add(a + 1);
                    triangles.Add(a + 3);
                    triangles.Add(a + 2);
                }
            }

            return Build(vertices, triangles);
        }

        /// <summary>A 1x1 square in the XY plane, for glows (rotate it to the camera).</summary>
        public static Mesh Quad()
        {
            var vertices = new List<Vector3>
            {
                new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f)
            };
            var mesh = Build(vertices, new List<int> { 0, 2, 1, 0, 3, 2 });
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            return mesh;
        }

        private static Mesh Build(List<Vector3> vertices, List<int> triangles)
        {
            var colors = new Color[vertices.Count];
            for (var i = 0; i < colors.Length; i++) colors[i] = Color.white;

            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.colors = colors;
            mesh.RecalculateBounds();
            return mesh;
        }

        public static MeshRenderer AddMesh(GameObject go, Mesh mesh, Material material)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        // ---------- ready made effects ----------

        /// <summary>The ground stomp lands: a ring that races out to the edge of the damage radius, plus dust.</summary>
        public static void Shockwave(Vector3 groundPoint, float radius, float scale)
        {
            groundPoint.y += 0.15f;

            Pulse.Create(groundPoint, Annulus(0.82f, 1f, 64, false), false, new Color(1f, 0.55f, 0.25f, 0.95f), radius * 0.1f, radius, 0.5f, false);
            Pulse.Create(groundPoint, Fan(180f, 48), false, new Color(1f, 0.2f, 0.1f, 0.35f), radius * 0.1f, radius, 0.4f, false);
            Dust(groundPoint, radius, scale);
        }

        /// <summary>The stun attack fires: a white burst and an expanding ring at the head.</summary>
        public static void StunFlash(Vector3 head, float scale)
        {
            Pulse.Create(head, Quad(), true, new Color(1f, 1f, 1f, 1f), 2f * scale, 16f * scale, 0.3f, true);
            Pulse.Create(head, Annulus(0.88f, 1f, 48, true), false, new Color(1f, 0.95f, 0.7f, 0.9f), 1f * scale, 18f * scale, 0.45f, true);
        }

        /// <summary>The light shot itself: a bright beam from the head to the player that thins out and fades.</summary>
        public static void StunBeam(Vector3 head, Vector3 target, float scale)
        {
            LightBeam.Fire(head, target, 1.2f * scale, 0.3f);
        }

        /// <summary>A puff of dust racing outwards along the floor.</summary>
        public static void Dust(Vector3 groundPoint, float radius, float scale)
        {
            var go = new GameObject("BossDust");
            go.transform.position = groundPoint;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // the circle shape lies in the floor

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 0.5f, radius * 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(1f * scale, 2.5f * scale);
            main.startColor = new Color(0.55f, 0.5f, 0.45f, 0.55f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)60) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.5f;

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = fade;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 1.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var material = MakeMaterial(Color.white, true);
            renderer.sharedMaterial = material;

            ps.Play();
            var cleanup = go.AddComponent<DestroyWithMaterial>();
            cleanup.Material = material;
            Object.Destroy(go, 4f);
        }
    }

    /// <summary>Frees a material made in code when its object goes.</summary>
    public class DestroyWithMaterial : MonoBehaviour
    {
        public Material Material;

        private void OnDestroy()
        {
            if (Material) Destroy(Material);
        }
    }

    /// <summary>
    /// The attack warning on the floor. A coloured outline shows how far the attack reaches (a pie slice for a swing or
    /// kick, a full circle for the stomp), and a fill grows from the boss outwards. When the fill touches the outline the
    /// attack lands. When the boss stops turning to aim (Track Until) the outline goes white: from then on it is locked.
    /// </summary>
    public class GroundTelegraph : MonoBehaviour
    {
        private const float FlashSeconds = 0.12f;

        private Transform _boss;
        private float _radius;
        private float _half;
        private float _duration;
        private float _lockAt;
        private Color _color;
        private float _time;
        private bool _follow;
        private Vector3 _origin;

        private Transform _fill;
        private Mesh _mesh;
        private Material _fillMaterial;
        private Material _lineMaterial;
        private LineRenderer _line;
        private Vector3[] _local;
        private Vector3[] _world;

        public static GroundTelegraph Create(Transform boss, float radius, float halfAngle, Color color, float duration,
            float lockAt, float y, bool followBoss = true)
        {
            var go = new GameObject("BossTelegraph");
            go.transform.position = new Vector3(boss.position.x, y, boss.position.z);

            var telegraph = go.AddComponent<GroundTelegraph>();
            telegraph._boss = boss;
            telegraph._radius = radius;
            telegraph._half = halfAngle;
            telegraph._duration = Mathf.Max(0.05f, duration);
            telegraph._lockAt = lockAt;
            telegraph._color = color;
            telegraph._follow = followBoss;
            telegraph._origin = go.transform.position;
            telegraph.Build();
            telegraph.Refresh();
            return telegraph;
        }

        private void Build()
        {
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(transform, false);
            _fill = fillGo.transform;
            _mesh = BossVfx.Fan(_half, Mathf.Max(8, Mathf.CeilToInt(_half / 4f)));
            _fillMaterial = BossVfx.MakeMaterial(Color.white, false);
            BossVfx.AddMesh(fillGo, _mesh, _fillMaterial);

            // The line lies flat: a Transform Z line faces along the object's Z, so turn that to point down.
            var lineGo = new GameObject("Outline");
            lineGo.transform.SetParent(transform, false);
            lineGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.alignment = LineAlignment.TransformZ;
            _line.numCornerVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _lineMaterial = BossVfx.MakeMaterial(Color.white, false);
            _line.sharedMaterial = _lineMaterial;

            var points = new List<Vector3>();
            var full = _half >= 179f;
            if (!full) points.Add(Vector3.zero);

            var segments = full ? 64 : Mathf.Max(8, Mathf.CeilToInt(_half / 3f));
            for (var i = 0; i <= segments; i++)
            {
                var angle = Mathf.Lerp(-_half, _half, i / (float)segments) * Mathf.Deg2Rad;
                points.Add(new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * _radius);
            }

            if (!full) points.Add(Vector3.zero);

            _local = points.ToArray();
            _world = new Vector3[_local.Length];
            _line.positionCount = _local.Length;
        }

        private void Update()
        {
            if (!_boss)
            {
                Destroy(gameObject);
                return;
            }

            _time += Time.deltaTime;
            Refresh();

            if (_time >= _duration + FlashSeconds) Destroy(gameObject);
        }

        private void Refresh()
        {
            var t = Mathf.Clamp01(_time / _duration);
            var yaw = Quaternion.Euler(0f, _boss.eulerAngles.y, 0f);

            // Stays where the attack started when the boss lunges out of it.
            transform.position = _follow ? new Vector3(_boss.position.x, transform.position.y, _boss.position.z) : _origin;

            for (var i = 0; i < _local.Length; i++) _world[i] = transform.position + yaw * _local[i];
            _line.SetPositions(_world);

            _fill.localRotation = yaw;
            _fill.localScale = Vector3.one * Mathf.Max(0.001f, t * _radius);

            var locked = t >= _lockAt;
            var landed = _time >= _duration;
            var flash = landed ? 1f - Mathf.Clamp01((_time - _duration) / FlashSeconds) : 0f;

            // The fill darkens towards red as it fills; it flashes white at the moment of the hit.
            var fill = Color.Lerp(_color, Color.white, flash);
            fill.a = landed ? Mathf.Lerp(0f, 0.8f, flash) : Mathf.Lerp(0.18f, 0.5f, t);
            _fillMaterial.color = fill;

            var pulse = 0.85f + 0.15f * Mathf.Sin(_time * (8f + 10f * t));
            var line = locked ? Color.Lerp(_color, Color.white, 0.75f) : _color;
            line.a = landed ? flash : pulse;
            _line.startColor = line;
            _line.endColor = line;
            _line.widthMultiplier = locked ? 0.55f : 0.35f;
        }

        private void OnDestroy()
        {
            if (_mesh) Destroy(_mesh);
            if (_fillMaterial) Destroy(_fillMaterial);
            if (_lineMaterial) Destroy(_lineMaterial);
        }
    }

    /// <summary>A flat mesh that grows and fades: a shockwave ring, a flash. Faces the camera when asked.</summary>
    public class Pulse : MonoBehaviour
    {
        private Mesh _mesh;
        private Material _material;
        private Color _color;
        private float _from;
        private float _to;
        private float _duration;
        private float _time;
        private bool _face;

        public static Pulse Create(Vector3 position, Mesh mesh, bool soft, Color color, float from, float to,
            float duration, bool faceCamera)
        {
            var go = new GameObject("BossPulse");
            go.transform.position = position;

            var pulse = go.AddComponent<Pulse>();
            pulse._mesh = mesh;
            pulse._material = BossVfx.MakeMaterial(color, soft);
            pulse._color = color;
            pulse._from = from;
            pulse._to = to;
            pulse._duration = Mathf.Max(0.05f, duration);
            pulse._face = faceCamera;
            BossVfx.AddMesh(go, mesh, pulse._material);
            pulse.Apply(0f);
            return pulse;
        }

        private void Update()
        {
            _time += Time.deltaTime;
            var t = Mathf.Clamp01(_time / _duration);
            Apply(t);
            if (t >= 1f) Destroy(gameObject);
        }

        private void Apply(float t)
        {
            var eased = 1f - (1f - t) * (1f - t); // fast at first, then settling
            var size = Mathf.Lerp(_from, _to, eased);
            transform.localScale = new Vector3(size, size, size);

            var color = _color;
            color.a *= 1f - t;
            _material.color = color;

            var cam = Camera.main;
            if (_face && cam) transform.rotation = cam.transform.rotation;
        }

        private void OnDestroy()
        {
            if (_mesh) Destroy(_mesh);
            if (_material) Destroy(_material);
        }
    }

    /// <summary>The light shot: a bright beam from the boss's head to the player that thins out and fades.</summary>
    public class LightBeam : MonoBehaviour
    {
        private LineRenderer _line;
        private Material _material;
        private float _width;
        private float _duration;
        private float _time;

        public static void Fire(Vector3 from, Vector3 to, float width, float duration)
        {
            var go = new GameObject("BossLightBeam");
            var beam = go.AddComponent<LightBeam>();
            beam._width = width;
            beam._duration = Mathf.Max(0.05f, duration);

            beam._line = go.AddComponent<LineRenderer>();
            beam._line.useWorldSpace = true;
            beam._line.positionCount = 2;
            beam._line.SetPosition(0, from);
            beam._line.SetPosition(1, to);
            beam._line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam._material = BossVfx.MakeMaterial(Color.white, false);
            beam._line.sharedMaterial = beam._material;
            beam.Apply(0f);
        }

        private void Update()
        {
            _time += Time.deltaTime;
            var t = Mathf.Clamp01(_time / _duration);
            Apply(t);
            if (t >= 1f) Destroy(gameObject);
        }

        private void Apply(float t)
        {
            _line.widthMultiplier = Mathf.Lerp(_width, 0f, t);
            var color = new Color(1f, 0.97f, 0.85f, 1f - t);
            _line.startColor = color;
            _line.endColor = color;
        }

        private void OnDestroy()
        {
            if (_material) Destroy(_material);
        }
    }

    /// <summary>
    /// The stun charge at the boss's head: sparks pulled in from all sides and a glow that swells and flickers until
    /// the shot. SetAmount goes 0 to 1 over the wind-up. Follows the head without being its child (the head bone may
    /// be scaled).
    /// </summary>
    public class ChargeGlow : MonoBehaviour
    {
        private Transform _target;
        private Vector3 _fixedPoint;
        private float _scale;
        private float _amount;

        private Mesh _mesh;
        private Material _material;
        private ParticleSystem _sparks;
        private Material _sparkMaterial;
        private Transform _glow;

        public static ChargeGlow Create(Transform target, Vector3 fallbackPoint, float scale)
        {
            var go = new GameObject("BossChargeGlow");
            var glow = go.AddComponent<ChargeGlow>();
            glow._target = target;
            glow._fixedPoint = fallbackPoint;
            glow._scale = scale;
            go.transform.position = target ? target.position : fallbackPoint;
            glow.Build();
            return glow;
        }

        public void SetAmount(float amount) => _amount = Mathf.Clamp01(amount);

        private void Build()
        {
            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(transform, false);
            _glow = glowGo.transform;
            _mesh = BossVfx.Quad();
            _material = BossVfx.MakeMaterial(new Color(1f, 0.97f, 0.8f, 0f), true);
            BossVfx.AddMesh(glowGo, _mesh, _material);

            var sparksGo = new GameObject("Sparks");
            sparksGo.transform.SetParent(transform, false);
            _sparks = sparksGo.AddComponent<ParticleSystem>();
            _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            const float life = 0.6f;
            var radius = 3.5f * _scale;

            var main = _sparks.main;
            main.loop = true;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f * _scale, 0.6f * _scale);
            main.startColor = new Color(1f, 0.95f, 0.7f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 200;

            var shape = _sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            shape.radiusThickness = 0f; // from the surface of the sphere

            // Pulled to the middle: reaches it at the end of its life. All three axes must be set when radial is used.
            var velocity = _sparks.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);
            velocity.radial = new ParticleSystem.MinMaxCurve(-radius / life);

            var size = _sparks.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

            var emission = _sparks.emission;
            emission.rateOverTime = 0f;

            _sparkMaterial = BossVfx.MakeMaterial(Color.white, true);
            _sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial = _sparkMaterial;
            _sparks.Play();
        }

        private void Update()
        {
            transform.position = _target ? _target.position : _fixedPoint;

            var cam = Camera.main;
            if (cam) _glow.rotation = cam.transform.rotation;

            var a = _amount * _amount;
            var flicker = 1f + 0.1f * Mathf.Sin(Time.time * 40f) + 0.05f * Mathf.Sin(Time.time * 97f);
            var size = Mathf.Lerp(0.3f, 4f, a) * _scale * flicker;
            _glow.localScale = new Vector3(size, size, size);
            _material.color = new Color(1f, 0.97f, 0.8f, Mathf.Lerp(0.15f, 1f, _amount));

            var emission = _sparks.emission;
            emission.rateOverTime = Mathf.Lerp(10f, 140f, _amount);
        }

        private void OnDestroy()
        {
            if (_mesh) Destroy(_mesh);
            if (_material) Destroy(_material);
            if (_sparkMaterial) Destroy(_sparkMaterial);
        }
    }

    /// <summary>Keeps an object at a transform's position without becoming its child (so the bone's scale never leaks in).</summary>
    public class FollowPoint : MonoBehaviour
    {
        public Transform Target;

        private void LateUpdate()
        {
            if (Target) transform.position = Target.position;
        }
    }

    /// <summary>"LOOK AWAY!" at the top of the screen while the stun charges and the player is looking at the head.</summary>
    public class LookAwayWarning : MonoBehaviour
    {
        private Text _text;
        private bool _show;
        private float _alpha;

        public static LookAwayWarning Create(Transform parent)
        {
            var go = new GameObject("LookAwayWarning");
            go.transform.SetParent(parent, false);
            var warning = go.AddComponent<LookAwayWarning>();
            warning.Build();
            return warning;
        }

        private void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(canvasGo.transform, false);
            _text = textGo.GetComponent<Text>();
            _text.font = KadrFont.Body;
            _text.fontSize = 64;
            _text.fontStyle = FontStyle.Bold;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.text = "LOOK AWAY!";
            _text.raycastTarget = false;
            _text.color = new Color(1f, 0.2f, 0.15f, 0f);

            var rect = _text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -140f);
            rect.sizeDelta = new Vector2(900f, 100f);
        }

        public void Show(bool show) => _show = show;

        private void Update()
        {
            _alpha = Mathf.MoveTowards(_alpha, _show ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            var blink = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 18f);
            _text.color = new Color(1f, 0.2f, 0.15f, _alpha * blink);
        }
    }
}
