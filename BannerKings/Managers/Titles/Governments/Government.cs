using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BannerKings.Managers.Titles.Governments
{
    // How a government staffs its kingdom council. Part of the politics
    // rework: government type is a ruleset, not a label.
    public enum CouncilControlType
    {
        Appointed,  // ruler appoints council seats freely (Feudal, Imperial)
        Elected,    // peers elect; the ruler cannot hand-appoint (Republic)
        Might,      // the strongest clans claim seats (Tribal)
    }

    // The political layer the player primarily contends with under a
    // government — individual magnates, or blocs.
    public enum PoliticalLayerType
    {
        Chiefs,       // near-equal clan chiefs, might-based (Tribal)
        Vassals,      // a hierarchy of landed vassals who govern (Feudal)
        Governors,    // appointed administrators of themes (Imperial)
        Parliament,   // parliamentary blocs, no land-magnates (Republic)
        Dictatorship, // a senate that still sits, cowed under a strongman
    }

    public class Government : ContractAspect
    {
        private Func<Kingdom, ValueTuple<bool, TextObject>> isAdequate;

        public Government(string stringId) : base(stringId)
        {
        }

        public void Initialize(TextObject name, TextObject description, TextObject effects, float mercantilism,
            float authoritarian, float oligarchic, float egalitarian,
            List<PolicyObject> prohibitedPolicies,
            List<Succession> successions,
            int crownAuthorityFloor = 0,
            int crownAuthorityCeiling = 2,
            CouncilControlType councilControl = CouncilControlType.Appointed,
            PoliticalLayerType politicalLayer = PoliticalLayerType.Vassals,
            Func<Kingdom, ValueTuple<bool, TextObject>> isAdequate = null)
        {
            Initialize(name, description);
            Authoritarian = authoritarian;
            Oligarchic = oligarchic;
            Egalitarian = egalitarian;
            Effects = effects;
            Mercantilism = mercantilism;
            ProhibitedPolicies = prohibitedPolicies;
            Successions = successions;
            CrownAuthorityFloor = crownAuthorityFloor;
            CrownAuthorityCeiling = crownAuthorityCeiling;
            CouncilControl = councilControl;
            PoliticalLayer = politicalLayer;
            this.isAdequate = isAdequate;
        }

        public override void PostInitialize()
        {
            Government g = DefaultGovernments.Instance.GetById(this);
            Initialize(g.name, g.description, g.Effects, g.Mercantilism,
                g.Authoritarian, g.Oligarchic, g.Egalitarian,
                g.ProhibitedPolicies,
                g.Successions,
                g.CrownAuthorityFloor, g.CrownAuthorityCeiling,
                g.CouncilControl, g.PoliticalLayer,
                g.isAdequate);
        }

        public float Mercantilism { get; private set; }
        public List<PolicyObject> ProhibitedPolicies { get; private set; }
        public List<Succession> Successions { get; private set; }
        public TextObject Effects { get; private set; }

        // Politics rework — government as ruleset.
        public int CrownAuthorityFloor { get; private set; }
        public int CrownAuthorityCeiling { get; private set; }
        public CouncilControlType CouncilControl { get; private set; }
        public PoliticalLayerType PoliticalLayer { get; private set; }

        public bool IsKingdomAdequate(Kingdom kingdom)
        {
            if (isAdequate != null) isAdequate(kingdom);
            return true;
        }

        public override bool Equals(object obj)
        {
            if (obj is Government)
            {
                return (obj as Government).StringId == StringId;
            }
            return base.Equals(obj);
        }
    }
}
