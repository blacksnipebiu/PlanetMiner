using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace PlanetMiner;

[BepInPlugin("crecheng.PlanetMiner", "PlanetMiner", "3.1.8")]
public class PlanetMiner : BaseUnityPlugin
{
    public const float DefaultMaxPlanetRadius = 100f;
    public const int uesEnergy = 20000000;

    public const float DefaultEnergyCostMultiplier = 1f;

    public enum EnergyCostMode
    {
        /// <summary>20 MJ per station slot per mining pass, however many items the pass yields.</summary>
        Fixed,
        /// <summary>Each mined item costs a multiple of what a vanilla Mining Machine spends per item.</summary>
        MiningMachine
    }

    private static volatile bool _enablePlanetRadiusLimit = false;
    private static volatile float _maxPlanetRadius = DefaultMaxPlanetRadius;
    private static volatile bool _energyPerItem = false;
    private static volatile float _energyCostMultiplier = DefaultEnergyCostMultiplier;

    private ConfigEntry<bool> _enablePlanetRadiusLimitConfig;
    private ConfigEntry<float> _maxPlanetRadiusConfig;
    private ConfigEntry<EnergyCostMode> _energyCostModeConfig;
    private ConfigEntry<float> _energyCostMultiplierConfig;

    internal static bool PlanetRadiusLimitEnabled
    {
        get { return _enablePlanetRadiusLimit; }
    }

    internal static float MaxPlanetRadius
    {
        get { return _maxPlanetRadius; }
    }

    internal static bool EnergyPerItemEnabled
    {
        get { return _energyPerItem; }
    }

    internal static float EnergyCostMultiplier
    {
        get { return _energyCostMultiplier; }
    }

    private void Start()
    {
        _enablePlanetRadiusLimitConfig = Config.Bind(
            "Gameplay",
            "EnablePlanetRadiusLimit",
            false,
            "Whether PlanetMiner is limited by planet radius.");

        _maxPlanetRadiusConfig = Config.Bind(
            "Gameplay",
            "MaxPlanetRadius",
            DefaultMaxPlanetRadius,
            "PlanetMiner works only when planet.radius is smaller than this value.");

        _energyCostModeConfig = Config.Bind(
            "Energy",
            "EnergyCostMode",
            EnergyCostMode.Fixed,
            "How mining ore veins is charged to the station. "
            + "Fixed: 20 MJ per slot per mining pass, however many items it yields. "
            + "MiningMachine: every mined item costs EnergyCostMultiplier times what a vanilla Mining Machine "
            + "(420 kW, covering 7 veins) spends per item. "
            + "Oil-like veins and water always use the fixed cost.");

        _energyCostMultiplierConfig = Config.Bind(
            "Energy",
            "EnergyCostMultiplier",
            DefaultEnergyCostMultiplier,
            "MiningMachine mode: how many times the energy of a vanilla Mining Machine each mined item costs.");

        ApplyConfigValues();
        _enablePlanetRadiusLimitConfig.SettingChanged += OnConfigSettingChanged;
        _maxPlanetRadiusConfig.SettingChanged += OnConfigSettingChanged;
        _energyCostModeConfig.SettingChanged += OnConfigSettingChanged;
        _energyCostMultiplierConfig.SettingChanged += OnConfigSettingChanged;

        Harmony.CreateAndPatchAll(typeof(MinerPatch), null);
    }

    private void OnConfigSettingChanged(object sender, EventArgs e)
    {
        ApplyConfigValues();
    }

    private void ApplyConfigValues()
    {
        _enablePlanetRadiusLimit = _enablePlanetRadiusLimitConfig != null && _enablePlanetRadiusLimitConfig.Value;
        float configuredRadius = _maxPlanetRadiusConfig?.Value ?? DefaultMaxPlanetRadius;
        _maxPlanetRadius = configuredRadius > 0f ? configuredRadius : DefaultMaxPlanetRadius;

        _energyPerItem = _energyCostModeConfig != null && _energyCostModeConfig.Value == EnergyCostMode.MiningMachine;
        float configuredMultiplier = _energyCostMultiplierConfig?.Value ?? DefaultEnergyCostMultiplier;
        _energyCostMultiplier = configuredMultiplier > 0f ? configuredMultiplier : DefaultEnergyCostMultiplier;
    }
}
