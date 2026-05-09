namespace BannerKings.Utils
{
    /// <summary>
    /// Coarse-grained feature gates for BK subsystems that are being temporarily
    /// disabled while we redesign them. Save data persists; behaviors and UI
    /// surfaces tied to a disabled gate skip their work and render empty.
    ///
    /// Today's only consumer is the estate system — under Economy Overhaul
    /// Framework we cede settlement-economy ownership to EOF, and the BK estate
    /// loop (income, retinue, AI policy, management UI) is paused until the
    /// rebuilt estate-as-village-workshop design lands. Flipping the gate back
    /// on (in code or via a future MCM toggle) restores estates without save
    /// migration because the underlying EstateData is never touched.
    /// </summary>
    public static class BKFeatureGates
    {
        /// <summary>
        /// True if BK's estate gameplay loop is active: daily income, retinue
        /// militia, AI policy decisions, management UI, clan-finance entries,
        /// visit-panel options, and the EOF village-workforce postfix.
        ///
        /// Defaults to off when Economy Overhaul Framework is detected (EOF
        /// owns settlement economy and the current estate→village production
        /// coupling is part of the redesign work). Otherwise on.
        /// </summary>
        public static bool EstatesEnabled => !ModCompat.EconomyOverhaul;
    }
}
