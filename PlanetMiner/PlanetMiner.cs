using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Security.Policy;
using UnityEngine;

namespace PlanetMiner
{
    [BepInPlugin("crecheng.PlanetMiner", "PlanetMiner", Version)]
    public class PlanetMiner : BaseUnityPlugin
    {

        public const string Version = "3.1.4";
        public const int uesEnergy = 20_000_000;
        private void Start()
        {
            Harmony.CreateAndPatchAll(typeof(PlanetMiner), null);
        }
        private void Update()
        {
            frame += 1L;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FactorySystem), "GameTickLabResearchMode")]
        private static void Miner(FactorySystem __instance)
        {
            GameHistoryData history = GameMain.history;
            float miningSpeedScale = history.miningSpeedScale;
            if (miningSpeedScale <= 0)
            {
                return;
            }
            int baseSpeed = (int)(120f / miningSpeedScale);
            baseSpeed = baseSpeed <= 0 ? 1 : baseSpeed;
            if (frame % baseSpeed != 0) return;
            VeinData[] veinPool = __instance.factory.veinPool;
            Dictionary<int, List<int>> veins = new Dictionary<int, List<int>>();
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
                if (sc?.storage == null) continue;
                for (int k = 0; k < sc.storage.Length; k++)
                {
                    StationStore stationStore = sc.storage[k];
                    int itemID = stationStore.itemId;
                    if (stationStore.count < 0) sc.storage[k].count = 0;
                    if (stationStore.localLogic != ELogisticStorage.Demand)
                    {
                        continue;
                    }
                    GenerateEnergy(sc);

                    if (stationStore.max <= stationStore.count)
                    {
                        continue;
                    }
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

        private static void GenerateEnergy(StationComponent sc)
        {
            var SecondtolastIndex = sc.storage.Length - 2;
            //当能量不足一半时
            if (SecondtolastIndex<0 || SecondtolastIndex >= sc.storage.Length || sc.energyMax / 2 <= sc.energy)
            {
                return;
            }
            //获取倒数第二个物品栏

            StationStore fuelStore = sc.storage[SecondtolastIndex];
            int fuelitemId = fuelStore.itemId;
            int fuelcount = fuelStore.count;
            if (fuelitemId <= 0 || fuelcount <= 0)
            {
                return;
            }
            //获取物品的能量值
            long heatValue = LDB.items.Select(fuelitemId)?.HeatValue ?? 0;
            if (heatValue <= 0)
            {
                return;
            }
            //获取需要充电的能量
            int needcount = Math.Min((int)((sc.energyMax - sc.energy) / heatValue), fuelcount); ;
            int usedInc = split_inc_level(ref fuelStore.count, ref fuelStore.inc, needcount);
            double num = 1;
            if (needcount > 0 && usedInc > 0)
            {
                int inclevel = usedInc / needcount;
                if (inclevel >= 0 && inclevel < Cargo.incTableMilli.Length)
                {
                    num += Cargo.incTableMilli[inclevel];
                }
            }
            sc.energy += (long)(needcount * heatValue * num);
            sc.storage[SecondtolastIndex].inc = fuelStore.inc;
            sc.storage[SecondtolastIndex].count = fuelStore.count;
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
                if (miningRate > 0)
                {
                    costFrac += miningRate;
                    flag = (int)costFrac > 0;
                    costFrac -= flag?1:0;
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

        private static long frame = 0L;

        private static double costFrac = 0;
        private static int split_inc_level(ref int count, ref int totalinc, int requireCount)
        {
            int usedInc = totalinc / count;
            int num2 = totalinc - usedInc * count;
            count -= requireCount;
            num2 -= count;
            usedInc = ((num2 > 0) ? (usedInc * requireCount + num2) : (usedInc * requireCount));
            totalinc -= usedInc;
            return usedInc;
        }
    }
}
