using System.Collections.Concurrent;

namespace PlanetMiner
{
    /// <summary>
    /// Fills a station's energy buffer by burning the fuel stacked in its fuel slot.
    /// </summary>
    internal static class StationEnergy
    {
        private static readonly ConcurrentDictionary<int, long> _heatValueCache = new ConcurrentDictionary<int, long>();

        public static void Generate(StationComponent station)
        {
            int fuelIndex = station.storage.Length - 2;
            if (fuelIndex < 0 || fuelIndex >= station.storage.Length)
            {
                return;
            }

            if (station.energyMax / 2 <= station.energy)
            {
                return;
            }

            StationStore fuel = station.storage[fuelIndex];
            int itemId = fuel.itemId;
            int count = fuel.count;
            if (itemId <= 0 || count <= 0)
            {
                return;
            }

            long heat = GetHeatValue(itemId);
            if (heat <= 0)
            {
                return;
            }

            int need = (int)((station.energyMax - station.energy) / heat);
            if (need > count)
            {
                need = count;
            }
            if (need <= 0)
            {
                return;
            }

            int usedInc = SplitIncLevel(ref fuel.count, ref fuel.inc, need);
            double multiplier = 1.0;
            if (need > 0 && usedInc > 0)
            {
                int level = usedInc / need;
                if (level >= 0 && level < Cargo.incTableMilli.Length)
                {
                    multiplier += Cargo.incTableMilli[level];
                }
            }

            station.energy += (long)((double)(need * heat) * multiplier);
            station.storage[fuelIndex].inc = fuel.inc;
            station.storage[fuelIndex].count = fuel.count;
        }

        public static long GetHeatValue(int itemId)
        {
            if (_heatValueCache.TryGetValue(itemId, out long value))
            {
                return value;
            }

            ItemProto proto = ((ProtoSet<ItemProto>)(object)LDB.items).Select(itemId);
            value = proto?.HeatValue ?? 0L;
            _heatValueCache.TryAdd(itemId, value);
            return value;
        }

        private static int SplitIncLevel(ref int count, ref int totalinc, int requireCount)
        {
            int a = totalinc / count;
            int b = totalinc - a * count;
            count -= requireCount;
            b -= count;
            a = b > 0 ? (a * requireCount + b) : (a * requireCount);
            totalinc -= a;
            return a;
        }
    }
}
