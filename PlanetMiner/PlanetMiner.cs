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
				int num = (int)(120f / miningSpeedScale);
				num = num <=0? 1 : num;
				if (frame % num != 0) return;
				VeinData[] veinPool = __instance.factory.veinPool;
				Dictionary<int, List<int>> dictionary = new Dictionary<int, List<int>>();
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
						AddVeinData(dictionary, veinData.productId, i);
					}
				}
				float miningRate = history.miningCostRate;
				PlanetTransport planetTransport = null;
				int[] array = null;
				planetTransport = __instance.planet.factory.transport;
				FactoryProductionStat factoryProductionStat = GameMain.statistics.production.factoryStatPool[__instance.factory.index];
				array = factoryProductionStat != null ? factoryProductionStat.productRegister : array;
				for (int j = 1; j < planetTransport.stationCursor; j++)
				{
					StationComponent stationComponent = planetTransport.stationPool[j];
					if (stationComponent == null || stationComponent.storage == null) continue;
					for (int k = 0; k < stationComponent.storage.Length; k++)
					{
						StationStore stationStore = stationComponent.storage[k];
						if (stationStore.localLogic == ELogisticStorage.Demand && stationStore.max > stationStore.count)
						{
							if (dictionary.ContainsKey(stationStore.itemId) || stationStore.itemId == __instance.planet.waterItemId)
							{
								if (stationComponent.energyMax / 2L > stationComponent.energy)
								{
									StationStore stationStore2 = stationComponent.storage[stationComponent.storage.Length - 2];
									if (stationStore2.count > 0)
									{
										long heatValue = LDB.items.Select(stationStore2.itemId).HeatValue;
										if (heatValue > 0)
										{
											int num3 = (int)((stationComponent.energyMax - stationComponent.energy) / heatValue);
											if (num3 > stationStore2.count)
											{
												num3 = stationComponent.storage[stationComponent.storage.Length - 2].count;
											}
											StationStore[] storage = stationComponent.storage;
											int num4 = stationComponent.storage.Length - 2;
											storage[num4].count = storage[num4].count - num3;
											stationComponent.energy += num3 * heatValue;
										}
									}
								}
							}
							if (dictionary.ContainsKey(stationStore.itemId))
							{
								if (stationComponent.energy < 20000000L) return;
								if (veinPool[dictionary[stationStore.itemId].First()].type == EVeinType.Oil)
								{
									float num6 = 0;
									foreach (int index in dictionary[stationStore.itemId])
									{
										num6 += veinPool.Length > index && veinPool[index].productId > 0 ? veinPool[index].amount / 6000f : 0;
									}
									StationStore[] storage2 = stationComponent.storage;
									storage2[k].count = storage2[k].count + (int)num6;
									array[stationStore.itemId] += factoryProductionStat != null ? (int)num6 : 0;
									stationComponent.energy -= 20000000L;
								}
								else
								{
									int num9 = 0;
									foreach (int index in dictionary[stationStore.itemId])
									{
										num9 += GetMine(veinPool, index, miningRate, __instance.planet.factory) ? 1 : 0;
									}
									StationStore[] storage3 = stationComponent.storage;
									storage3[k].count = storage3[k].count + num9;
									array[stationStore.itemId] += factoryProductionStat != null ? num9 : 0;
									stationComponent.energy -= 20000000L;
								}
							}
							else
							{
								if (stationStore.itemId == __instance.planet.waterItemId)
								{
									StationStore[] storage4 = stationComponent.storage;
									storage4[k].count += 100;
									array[stationStore.itemId] += factoryProductionStat != null ? 100 : 0;
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
			if(veinDatas.Length <= 0|| veinDatas[index].productId <= 0) return false;
			if (veinDatas[index].amount > 0)
			{
				bool flag3 = true;
				if (miningRate < 0.99999f)
				{
					seed = (uint)((seed % 2147483646U + 1U) * 48271UL % 2147483647UL) - 1U;
					flag3 = (seed / 2147483646.0 < (double)miningRate);
				}
				if (flag3)
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

		private const int uesEnergy = 20000000;

		private const int waterSpeed = 100;

		private static long frame = 0L;

		private static uint seed = 100000U;
	}
}
