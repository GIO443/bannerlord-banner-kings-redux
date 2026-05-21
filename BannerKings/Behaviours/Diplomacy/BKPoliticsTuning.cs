namespace BannerKings.Behaviours.Diplomacy
{
    // Politics rework — Phase 7. Every numeric knob for the AI political
    // disposition lives here, named, in one place. The disposition model
    // (BKPoliticalDisposition) is a pure weighted sum of normalised signals;
    // this file is the weight table. Structured so it can be lifted to an
    // XML tuning file wholesale later without touching the model code.
    //
    // Signals are normalised to roughly [-1, 1] (personality, prosperity,
    // strength) or [0, 1] (foreign-pop share, at-war) before weighting; the
    // final per-axis sum is clamped to [-1, 1].
    public static class BKPoliticsTuning
    {
        // Personality trait levels run roughly -2..+2; divide to normalise.
        public const float TraitScale = 2f;

        // Town prosperity benchmark — a town at this value reads as
        // economically "neutral" (prosperity index 0); double reads +1.
        public const float ProsperityReference = 5000f;

        // --- Centralism: +centralist .. -autonomist -------------------------
        public const float CentralismAuthoritarian = 0.35f;
        public const float CentralismEgalitarian = -0.35f;
        public const float CentralismOligarchic = -0.25f;
        // Clan culture matches the realm's ruling culture (insider) vs not.
        public const float CentralismRealmInsider = 0.25f;
        // Ruling foreign-majority pops — only an insider clan reads this as
        // "I want a strong crown behind me"; handled in the model.
        public const float CentralismForeignPops = 0.40f;
        // Wealthy magnates resist central taxation / levy.
        public const float CentralismProsperity = -0.30f;

        // --- Ambition: +ambitious .. -content -------------------------------
        public const float AmbitionValor = 0.30f;
        public const float AmbitionCalculating = 0.20f;
        public const float AmbitionHonor = -0.30f;
        // Strong clan held at a low standing — frustrated power.
        public const float AmbitionFrustration = 0.50f;
        // Prosperous and secure holdings — much to lose, stays content.
        public const float AmbitionContentment = -0.40f;

        // --- Militarism: +militarist .. -developer --------------------------
        // Restless foreign-majority land needs troops to hold.
        public const float MilitarismForeignPops = 0.45f;
        public const float MilitarismAtWar = 0.35f;
        public const float MilitarismValor = 0.20f;
        // Secure and rich — invest in growth, not levies.
        public const float MilitarismProsperity = -0.40f;

        // --- Phase 7c — vassal-to-vassal politics ---------------------------
        // Minimum Ambition for a clan to scheme to climb at all.
        public const float VassalPoliticsAmbitionFloor = 0.15f;
        // Sensed ruler-weakness above which an ambitious clan turns from the
        // loyal climb to the treacherous one.
        public const float VassalTreacheryThreshold = 0.50f;
        // Ambition still required to take the treacherous path given weakness.
        public const float VassalTreacheryAmbition = 0.25f;
        // Rivalry score above which a realm peer is treated as a true rival.
        public const float VassalRivalryThreshold = 0.60f;
    }
}
