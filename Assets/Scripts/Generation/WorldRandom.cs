using UnityEngine;

namespace Generation
{
    /// <summary>
    /// Random numbers that come out the same every time the same floor is built. Crates and loot racks used to roll with
    /// UnityEngine.Random, so loading a telephone save gave the same rooms with different loot. Now every crate and rack
    /// rolls from the floor's seed and its own place, whatever else happened in between.
    ///
    /// The LevelGenerator sets Seed while it builds a floor and clears it when it is destroyed. Outside a generated floor
    /// (the Lobby) there is no seed and everything rolls randomly, as before.
    /// </summary>
    public static class WorldRandom
    {
        public static int? Seed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Seed = null;

        /// <summary>A roller for something at this place, or an unseeded one outside a generated floor.</summary>
        public static Roller For(Vector3 position, int salt = 0)
        {
            if (!Seed.HasValue) return new Roller(null);

            var hash = unchecked(
                Seed.Value * 73856093
                ^ Mathf.RoundToInt(position.x * 4f) * 19349663
                ^ Mathf.RoundToInt(position.y * 4f) * 83492791
                ^ Mathf.RoundToInt(position.z * 4f) * 40503
                ^ salt * 2654435);
            return new Roller(new System.Random(hash));
        }

        public sealed class Roller
        {
            private readonly System.Random _rng;

            public Roller(System.Random rng) => _rng = rng;

            /// <summary>0 to 1.</summary>
            public float Value() => _rng == null ? Random.value : (float)_rng.NextDouble();

            /// <summary>min inclusive, max exclusive (like UnityEngine.Random.Range for ints).</summary>
            public int Range(int min, int max) => _rng == null ? Random.Range(min, max) : (max <= min ? min : _rng.Next(min, max));

            public float Range(float min, float max) => min + (max - min) * Value();

            public Vector2 InsideUnitCircle()
            {
                if (_rng == null) return Random.insideUnitCircle;

                var angle = Value() * Mathf.PI * 2f;
                var radius = Mathf.Sqrt(Value());
                return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
        }
    }
}