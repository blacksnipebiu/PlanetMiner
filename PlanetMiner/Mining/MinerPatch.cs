using System.Collections.Generic;
using HarmonyLib;

namespace PlanetMiner
{
    /// <summary>
    /// The mining pass itself: for every station slot that supplies a mined item, take what the
    /// station can pay for out of the planet's veins.
    /// </summary>
    public static class MinerPatch
    {
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

            if (PlanetMiner.PlanetRadiusLimitEnabled && planet.radius >= PlanetMiner.MaxPlanetRadius)
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

            if (MiningCompat.IsOptimizedByWeaver(__instance))
            {
                return;
            }

            PlanetTransport transport = factory.transport;
            if (transport == null || transport.stationPool == null)
            {
                return;
            }

            FactoryState state = FactoryStates.Get(factoryIndex);
            lock (state.SyncRoot)
            {
                FactoryStates.Prepare(state, factory, __instance.planet.id);
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
                long energyPerItem = PlanetMiner.EnergyPerItemEnabled
                    ? EnergyCost.GetEnergyPerItem(PlanetMiner.EnergyCostMultiplier, miningSpeedScale)
                    : 0L;
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
                            VeinMining.ClearOilFraction(state, s, k);
                            continue;
                        }

                        if (itemId <= 0)
                        {
                            VeinMining.ClearOilFraction(state, s, k);
                            continue;
                        }

                        bool hasVeins = state.VeinsByItem.TryGetValue(itemId, out List<int> indices);
                        bool isOil = hasVeins && MiningCompat.IsOilItem(itemId);
                        if (!isOil)
                        {
                            VeinMining.ClearOilFraction(state, s, k);
                        }

                        if (slot.max <= slot.count)
                        {
                            continue;
                        }

                        if (hasVeins)
                        {
                            // Ore veins in MiningMachine mode pay per mined item; everything else pays the fixed cost.
                            bool payPerItem = energyPerItem > 0L && !isOil;
                            long energyWanted = payPerItem ? indices.Count * energyPerItem : PlanetMiner.uesEnergy;
                            long energyNeeded = payPerItem ? energyPerItem : PlanetMiner.uesEnergy;

                            if (station.energy < energyWanted && !generatedEnergy)
                            {
                                StationEnergy.Generate(station);
                                generatedEnergy = true;
                            }

                            if (station.energy < energyNeeded)
                            {
                                continue;
                            }

                            int addInt = 0;
                            if (isOil)
                            {
                                double produced = VeinMining.SumOilOutput(indices, veinPool, itemId, out bool cacheStale);
                                if (cacheStale)
                                {
                                    FactoryStates.RebuildVeinCache(state, veinPool);
                                    if (!state.VeinsByItem.TryGetValue(itemId, out indices))
                                    {
                                        VeinMining.ClearOilFraction(state, s, k);
                                        continue;
                                    }

                                    produced = VeinMining.SumOilOutput(indices, veinPool, itemId, out cacheStale);
                                    if (cacheStale)
                                    {
                                        state.RebuildRequested = true;
                                        continue;
                                    }
                                }

                                addInt = VeinMining.AccumulateOilOutput(state, s, k, itemId, produced);
                            }
                            else
                            {
                                int total = indices.Count;
                                int count = total;
                                int first = 0;
                                if (payPerItem && station.energy < energyWanted)
                                {
                                    // Not enough energy for every vein: mine what the station can pay for,
                                    // starting at a different vein each pass so they deplete evenly.
                                    count = (int)(station.energy / energyPerItem);
                                    first = (int)((tick / interval) % total);
                                }

                                bool cacheStale = false;
                                for (int i = 0; i < count; i++)
                                {
                                    if (VeinMining.TryMine(veinPool, indices[(first + i) % total], itemId, miningCostRate, factory, ref costFrac, out bool changedCache))
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
                                    FactoryStates.RebuildVeinCache(state, veinPool);
                                }
                            }

                            if (addInt <= 0)
                            {
                                continue;
                            }

                            storage[k].count += addInt;
                            VeinMining.AddProductStat(productRegister, itemId, addInt);
                            station.energy -= payPerItem ? addInt * energyPerItem : PlanetMiner.uesEnergy;
                        }
                        else if (itemId == waterItemId)
                        {
                            if (station.energy < PlanetMiner.uesEnergy && !generatedEnergy)
                            {
                                StationEnergy.Generate(station);
                                generatedEnergy = true;
                            }

                            if (station.energy < PlanetMiner.uesEnergy)
                            {
                                continue;
                            }

                            storage[k].count += 100;
                            VeinMining.AddProductStat(productRegister, itemId, 100);
                            station.energy -= PlanetMiner.uesEnergy;
                        }
                    }
                }

                state.CostFrac = costFrac;
            }
        }
    }
}
