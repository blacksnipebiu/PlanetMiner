using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace PlanetMiner
{
    [BepInPlugin("crecheng.PlanetMiner", "PlanetMiner", Version)]
    public class PlanetMiner : BaseUnityPlugin
    {

        public const string Version = "3.0.8";
        public const int uesEnergy = 20000000;
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
                FactoryProductionStat factoryProductionStat = GameMain.statistics.production.factoryStatPool[__instance.factory.index];
                int[] productRegister = factoryProductionStat?.productRegister;
                foreach (var sc in transport.stationPool)
                {
                    if (sc == null || sc.storage == null) continue;
                    for (int k = 0; k < sc.storage.Length; k++)
                    {
                        StationStore stationStore = sc.storage[k];
                        int itemID = stationStore.itemId;
                        if (stationStore.count < 0) sc.storage[k].count = 0;
                        if (stationStore.localLogic == ELogisticStorage.Demand)
                        {
                            //当能量不足一半时
                            if (sc.energyMax / 2 > sc.energy)
                            {
                                //获取倒数第二个物品栏
                                StationStore stationStore2 = sc.storage[sc.storage.Length - 2];
                                int itemId2 = stationStore2.itemId;
                                int count2 = stationStore2.count;
                                if (itemId2 > 0 && count2 > 0)
                                {
                                    //获取物品的能量值
                                    long heatValue = LDB.items.Select(itemId2)?.HeatValue ?? 0;
                                    if (heatValue > 0)
                                    {
                                        //获取需要充电的能量
                                        int needcount = Math.Min((int)((sc.energyMax - sc.energy) / heatValue), count2); ;
                                        sc.storage[sc.storage.Length - 2].count -= needcount;
                                        sc.energy += needcount * heatValue;
                                    }
                                }
                            }

                            if (stationStore.max > stationStore.count)
                            {
                                if (veins.ContainsKey(itemID))
                                {
                                    if (sc.energy < uesEnergy) continue;
                                    float count = 0;
                                    bool isoil = LDB.veins.GetVeinTypeByItemId(itemID) == EVeinType.Oil;
                                    foreach (int index in veins[itemID])
                                    {
                                        if (isoil)
                                        {
                                            count += veinPool.Length > index && veinPool[index].productId > 0 ? veinPool[index].amount / 6000f : 0;
                                        }
                                        else
                                        {
                                            count += GetMine(veinPool, index, miningRate, __instance.planet.factory) ? 1 : 0;
                                        }
                                    }
                                    sc.storage[k].count += (int)count;
                                    productRegister[itemID] += factoryProductionStat != null ? (int)count : 0;
                                    sc.energy -= uesEnergy;
                                }
                                else
                                {
                                    if (itemID == __instance.planet.waterItemId)
                                    {
                                        sc.storage[k].count += 100;
                                        productRegister[itemID] += factoryProductionStat != null ? 100 : 0;
                                        sc.energy -= uesEnergy;
                                    }
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
                    flag = seed / 2147483646.0 < (double)miningRate;
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

        public static bool isRun = false;

        private static long frame = 0L;

        private static uint seed = 100000U;
    }
}
