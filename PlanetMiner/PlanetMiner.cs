using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace PlanetMiner
{
    [BepInPlugin("crecheng.PlanetMiner", "PlanetMiner", "3.0.1")]
    public class PlanetMiner : BaseUnityPlugin
    {
        private void Start()
        {
            Harmony.CreateAndPatchAll(typeof(PlanetMiner), null);
        }

        private void Update()
        {
            frame += 1L;
        }

        private void Init()
        {
            isRun = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FactorySystem), "GameTickLabResearchMode")]
        private static void Miner(FactorySystem __instance)
        {
            GameHistoryData history = GameMain.history;
            float miningSpeedScale = history.miningSpeedScale;
            if (miningSpeedScale > 0)
            {
                int baseSpeed = (int)(120f / miningSpeedScale);
                baseSpeed = baseSpeed <= 0 ? 1 : baseSpeed;
                if (frame % baseSpeed != 0) return;
                VeinData[] veinPool = __instance.factory.veinPool;
                Dictionary<int, List<int>> veins = new Dictionary<int, List<int>>();
                if (__instance.minerPool[0].seed == 0)
                {
                    System.Random random = new System.Random();
                    __instance.minerPool[0].seed = (uint)(__instance.planet.id * 100000 + random.Next(1, 9999));
                }
                else
                {
                    seed = __instance.minerPool[0].seed;
                }
                for (int i = 0; i < veinPool.Length; i++)
                {
                    VeinData veinData = veinPool[i];
                    if (veinData.amount > 0 && veinData.productId > 0)
                    {
                        AddVeinData(veins, veinData.productId, i);
                    }
                }
                float miningRate = history.miningCostRate;
                PlanetTransport transport = __instance.planet.factory.transport;
                int[] productRegister = null;
                FactoryProductionStat factoryProductionStat = GameMain.statistics.production.factoryStatPool[__instance.factory.index];
                productRegister = factoryProductionStat != null ? factoryProductionStat.productRegister : productRegister;
                for (int j = 1; j < transport.stationCursor; j++)
                {
                    StationComponent stationComponent = transport.stationPool[j];
                    if (stationComponent == null || stationComponent.storage == null) continue;
                    for (int k = 0; k < stationComponent.storage.Length; k++)
                    {
                        StationStore stationStore = stationComponent.storage[k];
                        if (stationStore.localLogic == ELogisticStorage.Demand && stationStore.max > stationStore.count)
                        {
                            if (veins.ContainsKey(stationStore.itemId) || stationStore.itemId == __instance.planet.waterItemId)
                            {
                                //当能量不足一半时
                                if (stationComponent.energyMax / 2 > stationComponent.energy)
                                {
                                    //获取倒数第二个物品栏
                                    StationStore stationStore2 = stationComponent.storage[stationComponent.storage.Length - 2];
                                    if (stationStore2.count > 0)
                                    {
                                        //获取物品的能量值
                                        long heatValue = LDB.items.Select(stationStore2.itemId).HeatValue;
                                        if (heatValue > 0)
                                        {
                                            //获取需要充电的能量
                                            int needcount = (int)((stationComponent.energyMax - stationComponent.energy) / heatValue);
                                            if (needcount > stationStore2.count)
                                            {
                                                needcount = stationComponent.storage[stationComponent.storage.Length - 2].count;
                                            }
                                            StationStore[] storage = stationComponent.storage;
                                            int num4 = stationComponent.storage.Length - 2;
                                            storage[num4].count = storage[num4].count - needcount;
                                            stationComponent.energy += needcount * heatValue;
                                        }
                                    }
                                }
                            }
                            if (veins.ContainsKey(stationStore.itemId))
                            {
                                if (stationComponent.energy < 20000000L) return;
                                if (veinPool[veins[stationStore.itemId].First()].type == EVeinType.Oil)
                                {
                                    float count = 0;
                                    foreach (int index in veins[stationStore.itemId])
                                    {
                                        count += veinPool.Length > index && veinPool[index].productId > 0 ? veinPool[index].amount / 6000f : 0;
                                    }
                                    StationStore[] storage2 = stationComponent.storage;
                                    storage2[k].count = storage2[k].count + (int)count;
                                    productRegister[stationStore.itemId] += factoryProductionStat != null ? (int)count : 0;
                                    stationComponent.energy -= 20000000L;
                                }
                                else
                                {
                                    int count = 0;
                                    foreach (int index in veins[stationStore.itemId])
                                    {
                                        count += GetMine(veinPool, index, miningRate, __instance.planet.factory) ? 1 : 0;
                                    }
                                    StationStore[] storage3 = stationComponent.storage;
                                    storage3[k].count = storage3[k].count + count;
                                    productRegister[stationStore.itemId] += factoryProductionStat != null ? count : 0;
                                    stationComponent.energy -= 20000000L;
                                }
                            }
                            else
                            {
                                if (stationStore.itemId == __instance.planet.waterItemId)
                                {
                                    StationStore[] storage4 = stationComponent.storage;
                                    storage4[k].count += 100;
                                    productRegister[stationStore.itemId] += factoryProductionStat != null ? 100 : 0;
                                    stationComponent.energy -= 20000000L;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static void AddVeinData(Dictionary<int, List<int>> veins, int item, int index)
        {
            bool flag = !veins.ContainsKey(item);
            if (flag)
            {
                veins.Add(item, new List<int>());
            }
            veins[item].Add(index);
        }

        public static bool GetMine(VeinData[] veinDatas, int index, float miningRate, PlanetFactory factory)
        {
            if (veinDatas.Length <= 0 || veinDatas[index].productId <= 0) return false;
            if (veinDatas[index].amount > 0)
            {
                bool flag = true;
                if (miningRate < 0.99999f)
                {
                    seed = (uint)((seed % 2147483646U + 1U) * 48271UL % 2147483647UL) - 1U;
                    flag = (seed / 2147483646.0 < (double)miningRate);
                }
                if (flag)
                {
                    veinDatas[index].amount = veinDatas[index].amount - 1;
                    factory.veinGroups[veinDatas[index].groupIndex].amount -= 1;
                    if (veinDatas[index].amount <= 0)
                    {
                        short groupIndex = veinDatas[index].groupIndex;
                        factory.veinGroups[groupIndex].count -= 1;
                        factory.RemoveVeinWithComponents(index);
                        factory.RecalculateVeinGroup(groupIndex);
                    }
                }
                return true;
            }
            else
            {
                short groupIndex2 = veinDatas[index].groupIndex;
                factory.veinGroups[groupIndex2].count -= 1;
                factory.RemoveVeinWithComponents(index);
                factory.RecalculateVeinGroup(groupIndex2);
                return false;
            }
        }

        public const string Version = "3.0.4";

        public static bool isRun = false;

        private static long frame = 0L;

        private static uint seed = 100000U;
    }
}
