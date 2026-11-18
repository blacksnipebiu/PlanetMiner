using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace PlanetMiner
{
    /// <summary>
    /// Adapters for optional content: rate-based ("oil-like") veins and the Weaver optimization mod.
    /// </summary>
    internal static class MiningCompat
    {
        private static volatile bool _weaverResolved;
        private static bool _weaverAvailable;
        private static MethodInfo _getOptimizedPlanetMethod;
        private static PropertyInfo _statusProperty;
        private static object _runningEnumValue;
        private static readonly object _weaverInitLock = new object();

        private static readonly ConcurrentDictionary<int, bool> _oilItemCache = new ConcurrentDictionary<int, bool>();

        public static bool IsOptimizedByWeaver(FactorySystem factorySystem)
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

        public static bool IsOilItem(int itemId)
        {
            if (_oilItemCache.TryGetValue(itemId, out bool value))
            {
                return value;
            }

            // Vein type 7 = Oil (vanilla)
            // Vein type 16 = DeepMagma, 19 = Ice (OrbitalRing-MOD)
            // These are all rate-based veins (oil-like) whose amount represents
            // production speed, not a finite stockpile.
            int veinType = (int)LDB.veins.GetVeinTypeByItemId(itemId);
            value = veinType == 7 || veinType == 16 || veinType == 19;
            _oilItemCache.TryAdd(itemId, value);
            return value;
        }

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
    }
}
