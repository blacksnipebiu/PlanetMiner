using System.Collections.Generic;

namespace PlanetMiner
{
    /// <summary>
    /// Vein-level mining: taking one item out of a vein, and the fractional accounting for
    /// rate-based veins and their per-slot remainder.
    /// </summary>
    internal static class VeinMining
    {
        public static bool TryMine(VeinData[] veinPool, int index, int itemId, float miningRate, PlanetFactory factory, ref double costFrac, out bool cacheStale)
        {
            cacheStale = false;
            if (veinPool == null || index < 0 || index >= veinPool.Length)
            {
                cacheStale = true;
                return false;
            }

            if (veinPool[index].productId != itemId)
            {
                cacheStale = true;
                return false;
            }

            if (veinPool[index].amount > 0)
            {
                bool consume = false;
                if (miningRate > 0f)
                {
                    costFrac += miningRate;
                    consume = (int)costFrac > 0;
                    if (consume)
                    {
                        costFrac -= 1.0;
                    }
                }

                if (consume)
                {
                    veinPool[index].amount = veinPool[index].amount - 1;
                    factory.veinGroups[veinPool[index].groupIndex].amount--;
                    if (veinPool[index].amount <= 0)
                    {
                        short groupIndex = veinPool[index].groupIndex;
                        factory.veinGroups[groupIndex].count--;
                        factory.RemoveVeinWithComponents(index);
                        factory.RecalculateVeinGroup(groupIndex);
                        cacheStale = true;
                    }
                }

                return true;
            }

            short emptyGroupIndex = veinPool[index].groupIndex;
            factory.veinGroups[emptyGroupIndex].count--;
            factory.RemoveVeinWithComponents(index);
            factory.RecalculateVeinGroup(emptyGroupIndex);
            cacheStale = true;
            return false;
        }

        public static double SumOilOutput(List<int> indices, VeinData[] veinPool, int itemId, out bool cacheStale)
        {
            cacheStale = false;
            if (veinPool == null)
            {
                return 0.0;
            }

            double produced = 0.0;
            int count = indices.Count;
            for (int i = 0; i < count; i++)
            {
                int index = indices[i];
                if (index < 0 || index >= veinPool.Length)
                {
                    cacheStale = true;
                    break;
                }

                VeinData vein = veinPool[index];
                if (vein.productId != itemId || vein.amount <= 0)
                {
                    cacheStale = true;
                    break;
                }

                produced += vein.amount / 6000.0;
            }

            return produced;
        }

        public static int AccumulateOilOutput(FactoryState state, int stationIndex, int slotIndex, int itemId, double produced)
        {
            if (produced <= 0.0)
            {
                return 0;
            }

            long key = MakeSlotKey(stationIndex, slotIndex);
            double total = produced;
            if (state.OilFractions.TryGetValue(key, out OilFractionState saved) && saved.ItemId == itemId)
            {
                total += saved.Fraction;
            }

            int addInt = (int)total;
            double remain = total - addInt;
            if (remain > 0.0)
            {
                state.OilFractions[key] = new OilFractionState(itemId, remain);
            }
            else
            {
                state.OilFractions.Remove(key);
            }

            return addInt;
        }

        public static void ClearOilFraction(FactoryState state, int stationIndex, int slotIndex)
        {
            state.OilFractions.Remove(MakeSlotKey(stationIndex, slotIndex));
        }

        public static void AddProductStat(int[] productRegister, int itemId, int amount)
        {
            if (productRegister == null || amount <= 0)
            {
                return;
            }

            if ((uint)itemId < (uint)productRegister.Length)
            {
                productRegister[itemId] += amount;
            }
        }

        private static long MakeSlotKey(int stationIndex, int slotIndex)
        {
            return ((long)stationIndex << 32) | (uint)slotIndex;
        }
    }
}
