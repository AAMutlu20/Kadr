namespace Generation
{
    /// <summary>
    /// Gives every node of a LevelGraph an EncounterType. Runs on the abstract graph, before any room is chosen.
    ///
    /// Fixed rules:
    ///   start = Safe, boss = Boss, the room before the boss = Safe.
    /// Weighted, per depth rules:
    ///   path and branch-middle nodes pick from Transit / Combat / Ambush / Patrol / Elite,
    ///   branch ends (leaves) pick from Safe / Combat / Ambush / Elite.
    /// Sequencing rules:
    ///   a node never repeats its parent's tag, except Combat may repeat;
    ///   the first room after the start is never Elite or Ambush.
    /// Weight tables below represent the pacing; each row is one depth.
    /// </summary>
    public static class EncounterTagger
    {
        // Column order: Transit, Combat, Ambush, Patrol, Elite
        private static readonly EncounterType[] MainPool =
        {
            EncounterType.Transit, EncounterType.Combat, EncounterType.Ambush, EncounterType.Patrol, EncounterType.Elite
        };

        private static readonly float[][] MainWeights =
        {
            new[] { 3f, 3f, 1f, 1f, 0f }, // depth 1
            new[] { 2f, 3f, 2f, 2f, 1f }, // depth 2
            new[] { 1f, 3f, 3f, 2f, 2f }  // depth 3
        };

        // Column order: Safe, Combat, Ambush, Elite
        private static readonly EncounterType[] LeafPool =
        {
            EncounterType.Safe, EncounterType.Combat, EncounterType.Ambush, EncounterType.Elite
        };

        private static readonly float[][] LeafWeights =
        {
            new[] { 4f, 1f, 0f, 0f }, // depth 1
            new[] { 3f, 1f, 1f, 1f }, // depth 2
            new[] { 3f, 1f, 1f, 2f }  // depth 3
        };

        public static void Tag(LevelGraph graph, int depth, System.Random rng)
        {
            var row = System.Math.Max(1, System.Math.Min(3, depth)) - 1;

            // Ids are in parent-before-child order, so the parent's tag is always known.
            foreach (var node in graph.Nodes)
            {
                if (node.Id == LevelGraph.StartId)
                {
                    node.Encounter = EncounterType.Safe;
                }
                else if (node.Id == graph.BossId)
                {
                    node.Encounter = EncounterType.Boss;
                }
                else if (node.Id == graph.PreBossId)
                {
                    node.Encounter = EncounterType.Safe;
                }
                else
                {
                    var parentTag = graph.Nodes[node.Parent].Encounter;
                    var afterStart = node.Parent == LevelGraph.StartId;

                    node.Encounter = node.IsLeaf
                        ? Pick(LeafPool, LeafWeights[row], parentTag, afterStart, rng)
                        : Pick(MainPool, MainWeights[row], parentTag, afterStart, rng);
                }
            }
        }

        private static EncounterType Pick(
            EncounterType[] pool,
            float[] baseWeights,
            EncounterType parentTag,
            bool afterStart,
            System.Random rng)
        {
            var weights = (float[])baseWeights.Clone();

            for (var i = 0; i < pool.Length; i++)
            {
                // Never repeat the parent's tag, except plain Combat.
                if (pool[i] == parentTag && pool[i] != EncounterType.Combat)
                {
                    weights[i] = 0f;
                }

                // Ease the player in right after the elevator.
                if (afterStart && (pool[i] == EncounterType.Elite || pool[i] == EncounterType.Ambush))
                {
                    weights[i] = 0f;
                }
            }

            var index = RngUtil.PickWeighted(weights, rng);
            return index >= 0 ? pool[index] : EncounterType.Combat;
        }
    }
}