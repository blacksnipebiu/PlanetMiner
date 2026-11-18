using System.Collections.Concurrent;
using System.Collections.Generic;

namespace PlanetMiner
{
    /// <summary>
    /// Per-factory mining state: the cached vein index, the fractional cost accumulator and the oil fractions.
    /// </summary>
    internal sealed class FactoryState
    {
        public readonly object SyncRoot = new object();
        public readonly Dictionary<int, List<int>> VeinsByItem = new Dictionary<int, List<int>>(64);
        public readonly Stack<List<int>> ListPool = new Stack<List<int>>();
        public readonly Dictionary<long, OilFractionState> OilFractions = new Dictionary<long, OilFractionState>();

        public PlanetFactory Factory;
        public VeinData[] VeinPool;
        public int PlanetId;
        public double CostFrac;
        public bool RebuildRequested = true;
    }

    internal struct OilFractionState
    {
        public readonly int ItemId;
        public readonly double Fraction;

        public OilFractionState(int itemId, double fraction)
        {
            ItemId = itemId;
            Fraction = fraction;
        }
    }

    /// <summary>
    /// Registry of <see cref="FactoryState"/> and the builder for its vein index.
    /// </summary>
    internal static class FactoryStates
    {
        private static readonly ConcurrentDictionary<int, FactoryState> States = new ConcurrentDictionary<int, FactoryState>();

        public static FactoryState Get(int factoryIndex)
        {
            return States.GetOrAdd(factoryIndex, _ => new FactoryState());
        }

        public static void Prepare(FactoryState state, PlanetFactory factory, int planetId)
        {
            VeinData[] veinPool = factory.veinPool;
            bool factoryChanged = !ReferenceEquals(state.Factory, factory)
                || !ReferenceEquals(state.VeinPool, veinPool)
                || state.PlanetId != planetId;

            if (factoryChanged)
            {
                state.Factory = factory;
                state.VeinPool = veinPool;
                state.PlanetId = planetId;
                state.CostFrac = 0.0;
                state.OilFractions.Clear();
                state.RebuildRequested = true;
            }

            if (state.RebuildRequested)
            {
                RebuildVeinCache(state, veinPool);
            }
        }

        public static void RebuildVeinCache(FactoryState state, VeinData[] veinPool)
        {
            RecycleVeinLists(state);

            if (veinPool != null)
            {
                for (int i = 0; i < veinPool.Length; i++)
                {
                    VeinData vein = veinPool[i];
                    if (vein.amount > 0 && vein.productId > 0)
                    {
                        if (!state.VeinsByItem.TryGetValue(vein.productId, out List<int> list))
                        {
                            list = AcquireVeinList(state);
                            state.VeinsByItem[vein.productId] = list;
                        }
                        list.Add(i);
                    }
                }
            }

            state.RebuildRequested = false;
        }

        private static void RecycleVeinLists(FactoryState state)
        {
            foreach (KeyValuePair<int, List<int>> pair in state.VeinsByItem)
            {
                pair.Value.Clear();
                state.ListPool.Push(pair.Value);
            }
            state.VeinsByItem.Clear();
        }

        private static List<int> AcquireVeinList(FactoryState state)
        {
            return state.ListPool.Count > 0 ? state.ListPool.Pop() : new List<int>(8);
        }
    }
}
