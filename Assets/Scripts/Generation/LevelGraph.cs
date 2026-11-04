using System.Collections.Generic;
using System.Linq;

namespace Generation
{
    /// <summary>One room-to-be in the abstract level layout. No geometry yet, only connections and a tag.</summary>
    public class LevelNode
    {
        public int Id;
        public int Parent = -1;
        public readonly List<int> Children = new();
        public bool OnCriticalPath;
        public EncounterType Encounter = EncounterType.Transit;

        /// <summary>Number of doorways this room needs: its parent plus its children.</summary>
        public int Degree => Children.Count + (Parent >= 0 ? 1 : 0);

        public bool IsLeaf => Children.Count == 0;
    }

    /// <summary>
    /// A tree of nodes. Node 0 is the elevator landing. Parents always have a lower id than their
    /// children, so processing nodes in id order always places a parent before its children.
    /// </summary>
    public class LevelGraph
    {
        public readonly List<LevelNode> Nodes = new();

        public static int StartId => 0;
        public int PreBossId;
        public int BossId;

        public LevelNode AddNode(int parent, bool onCriticalPath)
        {
            var node = new LevelNode
            {
                Id = Nodes.Count,
                Parent = parent,
                OnCriticalPath = onCriticalPath
            };

            Nodes.Add(node);

            if (parent >= 0)
            {
                Nodes[parent].Children.Add(node.Id);
            }

            return node;
        }
    }

    /// <summary>
    /// Builds the abstract topology: one critical path from start to boss, plus short dead-end branches.
    /// </summary>
    public static class LevelGraphBuilder
    {
        /// <summary>
        /// Largest socket count in the room kit. No node may need more doorways than this.
        /// </summary>
        public const int MaxDegree = 3;

        public static LevelGraph Build(
            System.Random rng,
            int pathMin,
            int pathMax,
            int branchMin,
            int branchMax,
            int branchLengthMax)
        {
            // Start, at least one room, pre-boss, boss.
            pathMin = System.Math.Max(pathMin, 4);
            pathMax = System.Math.Max(pathMax, pathMin);

            var graph = new LevelGraph();
            var length = rng.Next(pathMin, pathMax + 1);

            graph.AddNode(-1, true);
            for (var i = 1; i < length; i++)
            {
                graph.AddNode(i - 1, true);
            }

            graph.BossId = length - 1;
            graph.PreBossId = length - 2;

            // Branches hang off middle path nodes only (so, not the start, pre-boss or boss).
            // Each such node has degree 2 on the path, so one branch makes it 3 = MaxDegree.
            var hosts = new List<int>();
            for (var i = 1; i <= length - 3; i++)
            {
                hosts.Add(i);
            }

            RngUtil.Shuffle(hosts, rng);

            branchMin = System.Math.Max(0, branchMin);
            branchMax = System.Math.Max(branchMin, branchMax);
            var branchCount = System.Math.Min(rng.Next(branchMin, branchMax + 1), hosts.Count);

            for (var b = 0; b < branchCount; b++)
            {
                var previous = hosts[b];
                var branchLength = rng.Next(1, System.Math.Max(1, branchLengthMax) + 1);

                for (var k = 0; k < branchLength; k++)
                {
                    previous = graph.AddNode(previous, false).Id;
                }
            }

            return graph;
        }
    }

    /// <summary>
    /// Small helpers so all randomness goes through one seeded System.Random.
    /// </summary>
    public static class RngUtil
    {
        public static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// Returns an index chosen in proportion to its weight, or -1 if every weight is zero.
        /// </summary>
        public static int PickWeighted(IList<float> weights, System.Random rng)
        {
            var total = weights.Sum();

            if (total <= 0f)
            {
                return -1;
            }

            var roll = rng.NextDouble() * total;
            var running = 0.0;

            for (var i = 0; i < weights.Count; i++)
            {
                running += weights[i];
                if (weights[i] > 0f && roll < running)
                {
                    return i;
                }
            }

            // Float rounding, fall back to the last positive weight.
            for (var i = weights.Count - 1; i >= 0; i--)
            {
                if (weights[i] > 0f)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}