using System;
using System.Collections.Generic;
using BannerKings.Managers.Titles.Governments;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Diplomacy.Dilemmas
{
    /// <summary>
    /// The DATA half of a dilemma (the XML row). Declarative + tunable: timer,
    /// resolution band cutoffs, the per-government military-weight coefficient,
    /// eligibility, lever palette, and the three cooldowns. Loaded by
    /// <see cref="DefaultDilemmas"/> from <c>ModuleData/BKData/bk_dilemmas.xml</c>
    /// across every installed module (last-writer-wins), so a balance mod can
    /// retune any dilemma without recompiling. The matching CODE lives in a
    /// <see cref="DilemmaHandler"/> resolved by <see cref="HandlerKey"/>.
    /// </summary>
    public class DilemmaType : BannerKingsObject
    {
        public DilemmaType(string stringId) : base(stringId) { }

        public string HandlerKey { get; private set; }
        public DilemmaHandler Handler { get; private set; }

        public int TimerDays { get; private set; } = 30;

        // The contest cannot resolve early (strong short-circuit / backfire /
        // uncontested-fizzle) until this many days have elapsed since it became
        // active. Guarantees a join + lever window so a lopsided opening snapshot
        // can't auto-complete on day one. The timer always backstops resolution.
        public int MinDeliberationDays { get; private set; } = 7;

        public float MinInitiatorInfluence { get; private set; }

        // Resolution band cutoffs on the For-side ratio R.
        public float StrongThreshold { get; private set; } = 0.75f;
        public float WinThreshold { get; private set; } = 0.65f;
        public float PartialThreshold { get; private set; } = 0.50f;
        public float FailThreshold { get; private set; } = 0.25f;

        // Cooldowns (in-game days).
        public int CooldownDays { get; private set; } = 120;
        public int PairCooldownDays { get; private set; } = 365;
        public int BackfireCooldownDays { get; private set; } = 730;

        // Lever palette.
        public bool LeverMoney { get; private set; } = true;
        public bool LeverInfluence { get; private set; } = true;
        public bool LeverNegotiation { get; private set; } = true;

        // Eligibility. Empty governments list = all governments allowed.
        public List<string> EligibleGovernments { get; private set; } = new List<string>();
        public bool PeersOnly { get; private set; } = true;

        // Per-government military coefficient m in weight = clout·(1−m) + military·m.
        // Keyed by Government StringId; DefaultMilitary used when unlisted.
        public float DefaultMilitary { get; private set; } = 0.3f;
        private readonly Dictionary<string, float> _govMilitary = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public void Configure(
            TextObject name, TextObject description, string handlerKey, DilemmaHandler handler,
            int timerDays, int minDeliberationDays, float minInitiatorInfluence,
            float tStrong, float tWin, float tPartial, float tFail,
            int cooldownDays, int pairCooldownDays, int backfireCooldownDays,
            bool leverMoney, bool leverInfluence, bool leverNegotiation,
            List<string> eligibleGovernments, bool peersOnly,
            float defaultMilitary, Dictionary<string, float> govMilitary)
        {
            Initialize(name, description);
            HandlerKey = handlerKey;
            Handler = handler;
            TimerDays = timerDays;
            MinDeliberationDays = minDeliberationDays;
            MinInitiatorInfluence = minInitiatorInfluence;
            StrongThreshold = tStrong;
            WinThreshold = tWin;
            PartialThreshold = tPartial;
            FailThreshold = tFail;
            CooldownDays = cooldownDays;
            PairCooldownDays = pairCooldownDays;
            BackfireCooldownDays = backfireCooldownDays;
            LeverMoney = leverMoney;
            LeverInfluence = leverInfluence;
            LeverNegotiation = leverNegotiation;
            EligibleGovernments = eligibleGovernments ?? new List<string>();
            PeersOnly = peersOnly;
            DefaultMilitary = defaultMilitary;
            _govMilitary.Clear();
            if (govMilitary != null)
                foreach (var kv in govMilitary) _govMilitary[kv.Key] = kv.Value;
        }

        /// <summary>The military coefficient m for a given government, or the
        /// default when that government isn't explicitly listed.</summary>
        public float GetMilitaryCoefficient(Government government)
        {
            if (government != null && government.StringId != null &&
                _govMilitary.TryGetValue(government.StringId, out var m))
                return m;
            return DefaultMilitary;
        }

        public bool AllowsGovernment(Government government)
        {
            if (EligibleGovernments == null || EligibleGovernments.Count == 0) return true;
            if (government == null || government.StringId == null) return true;
            return EligibleGovernments.Contains(government.StringId);
        }
    }
}
