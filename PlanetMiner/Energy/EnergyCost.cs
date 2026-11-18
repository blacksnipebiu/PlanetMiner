namespace PlanetMiner
{
    /// <summary>
    /// Converts "how expensive should mining be" into energy per mined item.
    /// </summary>
    internal static class EnergyCost
    {
        // A vanilla Mining Machine draws 420 kW and mines 0.5 items per second from each vein it covers
        // (before mining speed research), so it spends 840 kJ per item divided by the veins it covers.
        // The reference machine is taken to cover 7 veins: 120 kJ per item.
        private const int MiningMachineVeins = 7;
        private const double MiningMachineEnergyPerItem = 840000.0 / MiningMachineVeins;

        /// <summary>
        /// Energy one mined item costs in MiningMachine mode. Mining speed research makes a vanilla miner
        /// faster at the same power, so its energy per item drops by the same factor.
        /// </summary>
        public static long GetEnergyPerItem(float multiplier, float miningSpeedScale)
        {
            double perItem = MiningMachineEnergyPerItem * multiplier / miningSpeedScale;
            return perItem < 1.0 ? 1L : (long)perItem;
        }
    }
}
