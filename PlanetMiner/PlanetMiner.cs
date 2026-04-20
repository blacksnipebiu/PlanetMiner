using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace PlanetMiner
{
    [BepInPlugin("crecheng.PlanetMiner", "PlanetMiner", "3.1.4")]
    public class PlanetMiner : BaseUnityPlugin
    {
        public const string Version = "3.1.4";
        public const float DefaultMaxPlanetRadius = 100f;
        public const int uesEnergy = 20000000;

        private static volatile bool _enablePlanetRadiusLimit = true;
        private static volatile float _maxPlanetRadius = DefaultMaxPlanetRadius;

        private ConfigEntry<bool> _enablePlanetRadiusLimitConfig;
        private ConfigEntry<float> _maxPlanetRadiusConfig;

        private sealed class FactoryState
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

        private struct OilFractionState
        {
            public readonly int ItemId;
            public readonly double Fraction;

            public OilFractionState(int itemId, double fraction)
            {
                ItemId = itemId;
                Fraction = fraction;
            }
        }

        private static volatile bool _weaverResolved;
        private static bool _weaverAvailable;
        private static MethodInfo _getOptimizedPlanetMethod;
        private static PropertyInfo _statusProperty;
        private static object _runningEnumValue;
        private static readonly object _weaverInitLock = new object();

        private static readonly ConcurrentDictionary<int, long> _heatValueCache = new ConcurrentDictionary<int, long>();
        private static readonly ConcurrentDictionary<int, bool> _oilItemCache = new ConcurrentDictionary<int, bool>();
        private static readonly ConcurrentDictionary<int, FactoryState> _stateByFactory = new ConcurrentDictionary<int, FactoryState>();


        private static void EnsureWeaverResolved()
        {
            if (_weaverResolved)
            {
                return;
            }

            lock (_weaverInitLock)
            {
                if (_weaverResolved)
                {
                    return;
                }

                try
                {
                    Type iOptimizedPlanet = Type.GetType("Weaver.Optimizations.IOptimizedPlanet, DSP_Weaver");
                    if (iOptimizedPlanet == null)
                    {
                        return;
                    }

                    Type starCluster = Type.GetType("Weaver.Optimizations.OptimizedStarCluster, DSP_Weaver");
                    if (starCluster == null)
                    {
                        return;
                    }

                    MethodInfo method = starCluster.GetMethod("GetOptimizedPlanet", BindingFlags.Static | BindingFlags.Public);
                    if (method == null)
                    {
                        return;
                    }

                    PropertyInfo property = iOptimizedPlanet.GetProperty("Status");
                    if (property == null)
                    {
                        return;
                    }

                    Type statusEnum = Type.GetType("Weaver.Optimizations.OptimizedPlanetStatus, DSP_Weaver");
                    if (statusEnum == null)
                    {
                        return;
                    }

                    _getOptimizedPlanetMethod = method;
                    _statusProperty = property;
                    _runningEnumValue = Enum.Parse(statusEnum, "Running");
                    _weaverAvailable = true;
                }
                catch
                {
                    _weaverAvailable = false;
                }
                finally
                {
                    _weaverResolved = true;
                }
            }
        }

        private static bool IsOptimizedByWeaver(FactorySystem factorySystem)
        {
            EnsureWeaverResolved();
            if (!_weaverAvailable)
            {
                return false;
            }

            try
            {
                object[] args = new object[] { factorySystem.planet };
                object optimizedPlanet = _getOptimizedPlanetMethod.Invoke(null, args);
                if (optimizedPlanet == null)
                {
                    return false;
                }

                object status = _statusProperty.GetValue(optimizedPlanet);
                return status != null && status.Equals(_runningEnumValue);
            }
            catch
            {
                return false;
            }
        }

        private static long GetHeatValue(int itemId)
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

        private static bool IsOilItem(int itemId)
        {
            if (_oilItemCache.TryGetValue(itemId, out bool value))
            {
                return value;
            }

            value = (int)LDB.veins.GetVeinTypeByItemId(itemId) == 7;
            _oilItemCache.TryAdd(itemId, value);
            return value;
        }

        private static long MakeSlotKey(int stationIndex, int slotIndex)
        {
            return ((long)stationIndex << 32) | (uint)slotIndex;
        }

        private static void ClearOilFraction(FactoryState state, int stationIndex, int slotIndex)
        {
            state.OilFractions.Remove(MakeSlotKey(stationIndex, slotIndex));
        }

        private static int AccumulateOilOutput(FactoryState state, int stationIndex, int slotIndex, int itemId, double produced)
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

        private static void AddProductStat(int[] productRegister, int itemId, int amount)
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

        private static void RebuildVeinCache(FactoryState state, VeinData[] veinPool)
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

        private static void PrepareFactoryState(FactoryState state, PlanetFactory factory, int planetId)
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

        private static double SumOilOutput(List<int> indices, VeinData[] veinPool, int itemId, out bool cacheStale)
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

        private static bool TryMine(VeinData[] veinPool, int index, int itemId, float miningRate, PlanetFactory factory, ref double costFrac, out bool cacheStale)
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

        private void Start()
        {
            _enablePlanetRadiusLimitConfig = Config.Bind(
                "Gameplay",
                "EnablePlanetRadiusLimit",
                true,
                "Whether PlanetMiner is limited by planet radius.");

            _maxPlanetRadiusConfig = Config.Bind(
                "Gameplay",
                "MaxPlanetRadius",
                DefaultMaxPlanetRadius,
                "PlanetMiner works only when planet.radius is smaller than this value.");

            ApplyConfigValues();
            _enablePlanetRadiusLimitConfig.SettingChanged += OnConfigSettingChanged;
            _maxPlanetRadiusConfig.SettingChanged += OnConfigSettingChanged;

            Harmony.CreateAndPatchAll(typeof(PlanetMiner), null);
        }

        private void OnConfigSettingChanged(object sender, EventArgs e)
        {
            ApplyConfigValues();
        }

        private void ApplyConfigValues()
        {
            _enablePlanetRadiusLimit = _enablePlanetRadiusLimitConfig == null || _enablePlanetRadiusLimitConfig.Value;
            float configuredRadius = _maxPlanetRadiusConfig?.Value ?? DefaultMaxPlanetRadius;
            _maxPlanetRadius = configuredRadius > 0f ? configuredRadius : DefaultMaxPlanetRadius;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FactorySystem), "GameTickLabResearchMode")]
        private static void Miner(FactorySystem __instance)
        {
            if (__instance == null || __instance.planet == null)
            {
                return;
            }

            GameHistoryData history = GameMain.history;
            if (history == null)
            {
                return;
            }

            float miningSpeedScale = history.miningSpeedScale;
            if (miningSpeedScale <= 0f)
            {
                return;
            }

            PlanetFactory factory = __instance.factory;
            if (factory == null)
            {
                return;
            }

            PlanetData planet = __instance.planet;
            if (planet == null)
            {
                return;
            }

            if (_enablePlanetRadiusLimit && planet.radius >= _maxPlanetRadius)
            {
                return;
            }

            int interval = (int)(120f / miningSpeedScale);
            if (interval <= 0)
            {
                interval = 1;
            }

            int factoryIndex = factory.index;
            long tick = GameMain.gameTick;
            if ((tick + factoryIndex) % interval != 0)
            {
                return;
            }

            if (IsOptimizedByWeaver(__instance))
            {
                return;
            }

            PlanetTransport transport = factory.transport;
            if (transport == null || transport.stationPool == null)
            {
                return;
            }

            FactoryState state = _stateByFactory.GetOrAdd(factoryIndex, _ => new FactoryState());
            lock (state.SyncRoot)
            {
                PrepareFactoryState(state, factory, __instance.planet.id);
                VeinData[] veinPool = state.VeinPool;

                int[] productRegister = null;
                if (GameMain.statistics != null && GameMain.statistics.production != null)
                {
                    FactoryProductionStat[] factoryStatPool = GameMain.statistics.production.factoryStatPool;
                    if (factoryStatPool != null && (uint)factoryIndex < (uint)factoryStatPool.Length)
                    {
                        FactoryProductionStat stat = factoryStatPool[factoryIndex];
                        productRegister = stat?.productRegister;
                    }
                }

                int waterItemId = __instance.planet.waterItemId;
                float miningCostRate = history.miningCostRate;
                StationComponent[] stationPool = transport.stationPool;
                double costFrac = state.CostFrac;

                for (int s = 0; s < stationPool.Length; s++)
                {
                    StationComponent station = stationPool[s];
                    if (station?.storage == null)
                    {
                        continue;
                    }

                    StationStore[] storage = station.storage;
                    bool generatedEnergy = false;

                    for (int k = 0; k < storage.Length; k++)
                    {
                        StationStore slot = storage[k];
                        if (slot.count < 0)
                        {
                            slot.count = 0;
                            storage[k].count = 0;
                        }

                        int itemId = slot.itemId;
                        if ((int)slot.localLogic != 2)
                        {
                            ClearOilFraction(state, s, k);
                            continue;
                        }

                        if (itemId <= 0)
                        {
                            ClearOilFraction(state, s, k);
                            continue;
                        }

                        bool hasVeins = state.VeinsByItem.TryGetValue(itemId, out List<int> indices);
                        bool isOil = hasVeins && IsOilItem(itemId);
                        if (!isOil)
                        {
                            ClearOilFraction(state, s, k);
                        }

                        if (slot.max <= slot.count)
                        {
                            continue;
                        }

                        if (hasVeins)
                        {
                            if (station.energy < uesEnergy && !generatedEnergy)
                            {
                                GenerateEnergy(station);
                                generatedEnergy = true;
                            }

                            if (station.energy < uesEnergy)
                            {
                                continue;
                            }

                            int addInt = 0;
                            if (isOil)
                            {
                                double produced = SumOilOutput(indices, veinPool, itemId, out bool cacheStale);
                                if (cacheStale)
                                {
                                    RebuildVeinCache(state, veinPool);
                                    if (!state.VeinsByItem.TryGetValue(itemId, out indices))
                                    {
                                        ClearOilFraction(state, s, k);
                                        continue;
                                    }

                                    produced = SumOilOutput(indices, veinPool, itemId, out cacheStale);
                                    if (cacheStale)
                                    {
                                        state.RebuildRequested = true;
                                        continue;
                                    }
                                }

                                addInt = AccumulateOilOutput(state, s, k, itemId, produced);
                            }
                            else
                            {
                                int count = indices.Count;
                                bool cacheStale = false;
                                for (int i = 0; i < count; i++)
                                {
                                    if (TryMine(veinPool, indices[i], itemId, miningCostRate, factory, ref costFrac, out bool changedCache))
                                    {
                                        addInt++;
                                    }

                                    if (changedCache)
                                    {
                                        cacheStale = true;
                                        break;
                                    }
                                }

                                if (cacheStale)
                                {
                                    RebuildVeinCache(state, veinPool);
                                }
                            }

                            if (addInt <= 0)
                            {
                                continue;
                            }

                            storage[k].count += addInt;
                            AddProductStat(productRegister, itemId, addInt);
                            station.energy -= uesEnergy;
                        }
                        else if (itemId == waterItemId)
                        {
                            if (station.energy < uesEnergy && !generatedEnergy)
                            {
                                GenerateEnergy(station);
                                generatedEnergy = true;
                            }

                            if (station.energy < uesEnergy)
                            {
                                continue;
                            }

                            storage[k].count += 100;
                            AddProductStat(productRegister, itemId, 100);
                            station.energy -= uesEnergy;
                        }
                    }
                }

                state.CostFrac = costFrac;
            }
        }

        private static void GenerateEnergy(StationComponent station)
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

            int usedInc = split_inc_level(ref fuel.count, ref fuel.inc, need);
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

        private static int split_inc_level(ref int count, ref int totalinc, int requireCount)
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

    public static class MyPluginInfo
    {
        public const string PLUGIN_GUID = "crecheng.PlanetMiner";
        public const string PLUGIN_NAME = "PlanetMiner";
        public const string PLUGIN_VERSION = "3.1.4";
    }
}
