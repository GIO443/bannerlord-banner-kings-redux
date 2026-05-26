using BannerKings.Managers.Skills;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours.Diplomacy.Groups.Demands
{
    public abstract class Demand : BannerKingsObject
    {
        public Demand(string stringId) : base(stringId)
        {
        }

        public abstract void SetTexts();

        public abstract Demand GetCopy(DiplomacyGroup group);

        [SaveableProperty(9)] public DiplomacyGroup Group { get; set; }
        [SaveableProperty(10)] public CampaignTime DueDate { get; protected set; }

        public bool IsDueDate => Active && ((DueDate.GetDayOfYear == CampaignTime.Now.GetDayOfYear && DueDate.GetYear == CampaignTime.Now.GetYear)
            || DueDate.IsPast);

        public abstract DemandResponse PositiveAnswer { get; }
        public abstract DemandResponse NegativeAnswer { get; }

        protected abstract TextObject PlayerPromptText { get; }
        protected abstract TextObject PlayerAnswersText { get; }

        public void ShowPlayerPrompt()
        {
            SetTexts();
            InformationManager.ShowInquiry(new InquiryData(Name.ToString(),
                PlayerPromptText.ToString(),
                true,
                true,
                new TextObject("{=j90Aa0xG}Resolve").ToString(),
                new TextObject("{=sbwMaTwx}Postpone").ToString(),
                () =>
                {
                    ShowPlayerDemandAnswers();
                },
                () =>
                {
                    DueDate = CampaignTime.DaysFromNow(7f);
                },
                Utils.Helpers.GetKingdomDecisionSound()),
                true,
                true);
        }

        public void ShowPlayerDemandAnswers()
        {
            List<InquiryElement> options = new List<InquiryElement>();
            foreach (var answer in DemandResponses)
            {
                options.Add(new InquiryElement(answer,
                    answer.Name.ToString(),
                    null,
                    answer.IsAdequate(Hero.MainHero),
                    answer.Description.ToString()));
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(Name.ToString(),
                PlayerAnswersText.ToString(),
                options,
                false,
                1,
                1,
                GameTexts.FindText("str_accept").ToString(),
                String.Empty,
                (List<InquiryElement> list) =>
                {
                    DemandResponse response = (DemandResponse)list[0].Identifier;
                    Fulfill(response, Hero.MainHero);
                },
                null,
                Utils.Helpers.GetKingdomDecisionSound()),
                true);
        }

        public DemandResponse DisputeLordship => new DemandResponse(new TextObject("{=RYmV2PEY}Dispute (Lordship)"),
                   new TextObject("{=RYmV2PEY}Dispute on legal terms the efficacy of this demand. {LEADER} may or not be compelled to conceded, based on their Lordship ({LEADER_LORDSHIP}) against yours ({PLAYER_LORDSHIP}). Whether the group is satisfied or not depends on the leader being convinced.\nMinimum Lordship: {LORDSHIP}")
                   .SetTextVariable("LEADER", Group.Leader.Name)
                   .SetTextVariable("LEADER_LORDSHIP", Group.Leader.GetSkillValue(BKSkills.Instance.Lordship))
                   .SetTextVariable("PLAYER_LORDSHIP", Hero.MainHero.GetSkillValue(BKSkills.Instance.Lordship))
                   .SetTextVariable("LORDSHIP", 100),
                   new TextObject("{=Zmrzar4v}On {DATE}, the {GROUP} were defeated on legal grounds over the {DEMAND} demand.")
                   .SetTextVariable("GROUP", Group.Name)
                   .SetTextVariable("DEMAND", Name),
                   -5,
                   1000,
                   100,
                   (Hero fulfiller) =>
                   {
                       return fulfiller.GetSkillValue(BKSkills.Instance.Lordship) > 100;
                   },
                   (Hero fulfiller) =>
                   {
                       return 1f;
                   },
                   (Hero fulfiller) =>
                   {
                       float factor = fulfiller.GetSkillValue(BKSkills.Instance.Lordship) /
                       (Group.Leader.GetSkillValue(BKSkills.Instance.Lordship) + 1);

                       if (MBRandom.RandomFloat <= factor * 0.5f)
                       {
                           if (fulfiller == Hero.MainHero)
                           {
                               InformationManager.DisplayMessage(new InformationMessage(
                                   new TextObject("{=cQv03vke}{LEADER} was appeased! The {GROUP} back down on their demand.")
                                   .SetTextVariable("GROUP", Group.Name)
                                   .SetTextVariable("LEADER", Group.Leader.Name)
                                   .ToString(),
                                   Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
                           }

                           return true;
                       }
                       else
                       {
                           if (fulfiller == Hero.MainHero)
                           {
                               InformationManager.DisplayMessage(new InformationMessage(
                                   new TextObject("{=cQv03vke}{LEADER} was appeased! The {GROUP} back down on their demand.")
                                   .SetTextVariable("GROUP", Group.Name)
                                   .SetTextVariable("LEADER", Group.Leader.Name)
                                   .ToString(),
                                   Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
                           }

                           return false;
                       }
                   });

        public DemandResponse AppeaseCharm => new DemandResponse(new TextObject("{=Uxjzd09j}Appease (Charm)"),
                   new TextObject("{=SHDJYFt7}Use your diplomatic and charm skills to convince {LEADER} out of this idea. This option may or may not work. The chance of working depends on your Charm and how much {LEADER} likes you - its unlikely you will charm an enemy. Whether the group is satisfied or not depends on the leader being convinced.\nMinimum Charm: {CHARM}")
                   .SetTextVariable("LEADER", Group.Leader.Name)
                   .SetTextVariable("CHARM", 150),
                   new TextObject("{=Wd5ZRzuQ}On {DATE}, the {GROUP} were appeased to forfeit their {DEMAND} demand.")
                   .SetTextVariable("GROUP", Group.Name)
                   .SetTextVariable("DEMAND", Name),
                   0,
                   500,
                   100,
                   (Hero fulfiller) =>
                   {
                       return fulfiller.GetSkillValue(DefaultSkills.Charm) > 150;
                   },
                   (Hero fulfiller) =>
                   {
                       return 1f;
                   },
                   (Hero fulfiller) =>
                   {
                       int relation = Group.Leader.GetRelation(fulfiller);
                       int skill = (int)(fulfiller.GetSkillValue(DefaultSkills.Charm) / 3f);
                       float chance = (relation + skill) / 200f;
                       if (MBRandom.RandomFloat <= chance)
                       {
                           if (fulfiller == Hero.MainHero)
                           {
                               InformationManager.DisplayMessage(new InformationMessage(
                                   new TextObject("{=cQv03vke}{LEADER} was appeased! The {GROUP} back down on their demand.")
                                   .SetTextVariable("GROUP", Group.Name)
                                   .SetTextVariable("LEADER", Group.Leader.Name)
                                   .ToString(),
                                   Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
                           }

                           ChangeRelationAction.ApplyRelationChangeBetweenHeroes(fulfiller, Group.Leader, 6);
                           return true;
                       }
                       else
                       {
                           if (fulfiller == Hero.MainHero)
                           {
                               InformationManager.DisplayMessage(new InformationMessage(
                                   new TextObject("{=tEY3J9Pn}{LEADER} was not convinced... The {GROUP} is not satisfied with this outcome.")
                                   .SetTextVariable("GROUP", Group.Name)
                                   .SetTextVariable("LEADER", Group.Leader.Name)
                                   .ToString(),
                                   Color.FromUint(Utils.TextHelper.COLOR_LIGHT_RED)));
                           }

                           ChangeRelationAction.ApplyRelationChangeBetweenHeroes(fulfiller, Group.Leader, -9);
                           return false;
                       }
                   });

        public DemandResponse LeverageInfluence => new DemandResponse(new TextObject("{=GkWMyggj}Leverage Influence"),
                   new TextObject("{=mGhTovWX}Use your political influence to deny this demand. {LEADER} will not be pleased with this option, but the group will accept it. They will be satisfied with this outcome.")
                   .SetTextVariable("LEADER", Group.Leader.Name)
                   .SetTextVariable("INFLUENCE", MBRandom.RoundRandomized(BannerKingsConfig.Instance.InterestGroupsModel
                   .CalculateLeverageInfluenceCost(Hero.MainHero, 100, 1f).ResultNumber)),
                   new TextObject("{=hxeZ0LNf}On {DATE}, the {GROUP} were politically influenced to abandon their {DEMAND} demand.")
                   .SetTextVariable("GROUP", Group.Name)
                   .SetTextVariable("DEMAND", Name),
                   -8,
                   500,
                   200,
                   (Hero fulfiller) =>
                   {
                       return fulfiller.Clan.Influence >=
                       BannerKingsConfig.Instance.InterestGroupsModel.CalculateLeverageInfluenceCost(fulfiller, 100, 1f).ResultNumber;
                   },
                   (Hero fulfiller) =>
                   {
                       return 1f;
                   },
                   (Hero fulfiller) =>
                   {
                       if (fulfiller == Hero.MainHero)
                       {
                           InformationManager.DisplayMessage(new InformationMessage(
                               new TextObject("{=CUGYcFbS}The {GROUP} back down on their demand! {LEADER} was politically compelled to give up.")
                               .SetTextVariable("GROUP", Group.Name)
                               .SetTextVariable("LEADER", Group.Leader.Name)
                               .ToString(),
                               Color.FromUint(Utils.TextHelper.COLOR_LIGHT_BLUE)));
                       }

                       ChangeClanInfluenceAction.Apply(fulfiller.Clan, -BannerKingsConfig.Instance.InterestGroupsModel
                           .CalculateLeverageInfluenceCost(fulfiller, 100, 1f).ResultNumber);
                       return true;
                   });

        public DemandResponse FinancialCompromise => new DemandResponse(new TextObject("{=2bBytgK0}Financial Compromise"),
                   new TextObject("{=6VxLRan7}Negotiate with {LEADER} a financial compromise to appease the group's demand. A sum of denars based on your income will be paied out to the group, mostly to the leader. They will be satisfied with this outcome.")
                   .SetTextVariable("LEADER", Group.Leader.Name)
                   .SetTextVariable("DENARS", MBRandom.RoundRandomized(BannerKingsConfig.Instance.InterestGroupsModel
                   .CalculateFinancialCompromiseCost(Hero.MainHero, 10000, 1f).ResultNumber)),
                   new TextObject("{=sAWVfzx6}On {DATE}, a financial compromise was made with {GROUP} due to their {DEMAND} demand.")
                   .SetTextVariable("GROUP", Group.Name)
                   .SetTextVariable("DEMAND", Name),
                   5,
                   300,
                   300,
                   (Hero fulfiller) =>
                   {
                       return true;
                   },
                   (Hero fulfiller) =>
                   {
                       return 1f;
                   },
                   (Hero fulfiller) =>
                   {
                       int denars = MBRandom.RoundRandomized(BannerKingsConfig.Instance.InterestGroupsModel
                       .CalculateFinancialCompromiseCost(fulfiller, 
                       10000, 
                       1f - (0.2f * Group.Leader.GetTraitLevel(DefaultTraits.Generosity)))
                       .ResultNumber);
                       fulfiller.ChangeHeroGold(-denars);

                       LoseRelationsWithGroup(fulfiller, -6, 0.2f);
                       if (fulfiller == Hero.MainHero)
                       {
                           InformationManager.DisplayMessage(new InformationMessage(
                               new TextObject("{=Y1KdZHHg}The {GROUP} accepts the compromise... However, some criticize it as bribery.")
                               .SetTextVariable("GROUP", Group.Name)
                               .ToString(),
                               Color.FromUint(Utils.TextHelper.COLOR_LIGHT_YELLOW)));
                       }

                       return true;
                   });

        public abstract float MinimumGroupInfluence { get; }

        public abstract bool Active { get; }

        public void SetUp()
        {
            SetUpInternally();

            if (Active)
            {
                if (Group.Leader != Hero.MainHero) InviteMembers();

                if (Group.Leader == Hero.MainHero)
                {
                    ShowPlayerDemandOptions();
                }

                if (Group.FactionLeader == Hero.MainHero)
                {
                    ShowPlayerPrompt();
                }
            }
            else Finish();
        }

        private void InviteMembers()
        {
            if (Group.IsRadicalGroup)
            {
                List<Hero> heroes = new List<Hero>(30);
                foreach (Hero hero in Group.KingdomDiplomacy.Kingdom.Heroes)
                {
                    if (Group.CanHeroJoin(hero, Group.KingdomDiplomacy) &&
                        BannerKingsConfig.Instance.InterestGroupsModel.CalculateHeroJoinChance(hero, Group, Group.KingdomDiplomacy).ResultNumber > 0f)
                        heroes.Add(hero);
                }

                float influenceCap = BannerKingsConfig.Instance.InfluenceModel.CalculateInfluenceCap(Group.Leader.Clan).ResultNumber;
                foreach (Hero hero in heroes)
                {
                    float cost = BannerKingsConfig.Instance.InterestGroupsModel.InviteToGroupInfluenceCost(Group, hero, Group.KingdomDiplomacy).ResultNumber;
                    float influence = Group.Leader.Clan.Influence;
                    if (influence > cost && influence > influenceCap * 0.1f)
                    {
                        if (hero != Hero.MainHero)
                        {
                            Group.AddMember(hero);
                            ChangeClanInfluenceAction.Apply(Group.Leader.Clan, -cost);
                        }
                        else InformationManager.ShowInquiry(new InquiryData(
                            Name.ToString(),
                            new TextObject("{=!}{DESCRIPTION}{newline}{newline}{LEADER} invites you to join them. Will you accept?")
                            .SetTextVariable("DESCRIPTION", Description)
                            .SetTextVariable("LEADER", Group.Leader.Name)
                            .ToString(),
                            true,
                            true,
                            GameTexts.FindText("str_accept").ToString(),
                            GameTexts.FindText("str_cancel").ToString(),
                            () =>
                            {
                                Group.AddMember(hero);
                                ChangeClanInfluenceAction.Apply(Group.Leader.Clan, -cost);
                            },
                            null,
                            Utils.Helpers.GetKingdomDecisionSound()),
                            true,
                            true);
                    }
                }
            }
        }

        protected abstract void SetUpInternally();
        protected abstract bool IsFulfilled();
        public abstract void ShowPlayerDemandOptions();
        public abstract ValueTuple<bool, TextObject> IsDemandCurrentlyAdequate();
        public abstract IEnumerable<DemandResponse> DemandResponses { get; }
        public void Tick()
        {
            if (Group.IsRadicalGroup)
            {
                if (IsFulfilled()) Fulfill(PositiveAnswer, Group.FactionLeader);
            }

            if (IsDueDate)
            {
                if (IsFulfilled()) Fulfill(PositiveAnswer, Group.FactionLeader);
                else PushForDemand();
            }
        }
        public abstract void Finish();

        public void PushForDemand()
        {
            if (Group.KingdomDiplomacy.Kingdom.Leader == Hero.MainHero)
            {
                ShowPlayerDemandAnswers();
            }
            else
            {
                DoAiChoice();
            }
        }

        protected void LoseRelationsWithGroup(Hero fulfiller, int maxLoss, float chance)
        {
            foreach (Hero member in Group.Members)
            {
                if (member != Group.Leader && MBRandom.RandomFloat < chance)
                {
                    int loss = MBRandom.RandomInt(maxLoss, -1);
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(fulfiller, member, loss);
                }
            }
        }

        protected string GetHeroRoleText(Hero hero)
        {
            if (hero.Clan != null)
            {
                if (hero.Clan == Clan.PlayerClan)
                {
                    return new TextObject("{=qQbNuioA}, a member of your household").ToString();
                }

                if (hero.IsClanLeader) new TextObject("{=ABnd5G9h}, head of the {CLAN}").SetTextVariable("CLAN", hero.Clan.Name).ToString();

                return new TextObject("{=npdeYCOm} of the {CLAN}").SetTextVariable("CLAN", hero.Clan.Name).ToString();
            }

            if (hero.IsNotable)
            {
                return new TextObject("{=sJrbtFwD}, a local dignatary").ToString();
            }

            return string.Empty;
        }

        public void Fulfill(DemandResponse response, Hero fulfiller)
        {
            SetTexts();
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(fulfiller, Group.Leader, response.Relation);
            bool success = response.Fulfill(fulfiller);

            Group.Leader.AddSkillXp(BKSkills.Instance.Lordship, success ? response.LoserXp : response.SuccessXp);
            fulfiller.AddSkillXp(BKSkills.Instance.Lordship, success ? response.SuccessXp : response.LoserXp);
            
            if (!Group.IsInterestGroup)
            {
                if (!success)
                {
                    (Group as RadicalGroup).TriggerRevolt();
                }

                //Game.Current.GameStateManager.PopState(0);
                //UISoundsHelper.PlayUISound("event:/ui/default");
            }
            else
            {
                (Group as InterestGroup).AddOutcome(this, response, success);
            }

            Finish();
        }

        public void DoAiChoice()
        {
            Hero ruler = Group.FactionLeader;
            List<ValueTuple<DemandResponse, float>> options = new List<(DemandResponse, float)>();
            foreach (DemandResponse response in DemandResponses)
            {
                // Mirror the player UI's IsAdequate gate (ShowPlayerDemandAnswers,
                // line 66). The base AI loop ignored adequacy, so AI rulers could
                // pick e.g. DisputeLordship at Lordship<100 or LeverageInfluence
                // they can't afford — both wasted picks. The Fulfill lambda then
                // ran and silently failed-or-succeeded with no scoring backing.
                if (!response.IsAdequate(ruler)) continue;
                float baseScore = response.CalculateAiLikelihood(ruler);
                float personality = ScoreResponseByPersonality(ruler, response);
                options.Add(new (response, MathF.Max(0.01f, baseScore * personality)));
            }

            // Safety net: if every response was inadequate (e.g. low-skill ruler
            // with poor relations + no influence + no gold), fall back to the
            // legacy weighting so the demand still resolves and the Tick loop
            // doesn't spin forever on an unresolved due date.
            if (options.Count == 0)
            {
                foreach (DemandResponse response in DemandResponses)
                {
                    options.Add(new (response, MathF.Max(0.01f, response.CalculateAiLikelihood(ruler))));
                }
            }

            DemandResponse result = MBRandom.ChooseWeighted(options);
            Fulfill(result, ruler);
        }

        // Personality + interests modifier on the AI ruler's response choice.
        // The shipped per-response CalculateAiLikelihood is mostly a constant
        // 1.0f (see DisputeLordship, AppeaseCharm, LeverageInfluence,
        // FinancialCompromise above), so without this every AI ruler resolves
        // demands as a uniform coin-flip over adequate options regardless of
        // who they are. The signal sources used here are all vanilla / BK
        // state — traits, relations, group membership, IsRadicalGroup — so
        // nothing new persists.
        //
        // The classification axis is the response's Relation field: positive
        // Relation means the response concedes (group gets what it wants);
        // negative means the response refuses; zero is neutral compromise.
        // Each axis is then biased by the ruler's traits and the stakes
        // (radical group => revolt on refusal raises the stakes asymmetrically).
        private float ScoreResponseByPersonality(Hero ruler, DemandResponse response)
        {
            if (ruler == null) return 1f;

            // Trait levels are integers in [-2, +2]. Convert to ~[-1, +1] floats.
            float honor       = ruler.GetTraitLevel(DefaultTraits.Honor)      * 0.5f;
            float mercy       = ruler.GetTraitLevel(DefaultTraits.Mercy)      * 0.5f;
            float generosity  = ruler.GetTraitLevel(DefaultTraits.Generosity) * 0.5f;
            float calculating = ruler.GetTraitLevel(DefaultTraits.Calculating) * 0.5f;
            float valor       = ruler.GetTraitLevel(DefaultTraits.Valor)      * 0.5f;

            // Stakes asymmetry: a radical group revolts on refusal. A calculating
            // ruler weighs that consequence; a valorous one welcomes the fight.
            float radicalRefusalPenalty = Group != null && Group.IsRadicalGroup
                ? (calculating * 0.4f) - (valor * 0.3f)
                : 0f;

            // Relation between ruler and group leader skews the entire grant
            // axis: rulers favour their friends and refuse their rivals.
            int relationInt = Group != null && Group.Leader != null
                ? Group.Leader.GetRelation(ruler)
                : 0;
            float relationFactor = MathF.Clamp(relationInt / 50f, -1f, 1f);

            // Interest alignment: if the ruler is also a member of the demanding
            // group (or, for a radical group, its parent InterestGroup), they
            // share its agenda and should lean toward conceding. We don't have
            // a direct radical->parent link in DiplomacyGroup, so membership in
            // the same Members list is the available proxy.
            float interestAlignment = (Group != null && Group.Members != null && Group.Members.Contains(ruler))
                ? 0.6f
                : 0f;

            // Axis selection.
            //   Relation >  0  : conceding response  (positive answers, financial compromise)
            //   Relation <  0  : refusing response   (leverage influence, denial-style answers)
            //   Relation == 0  : neutral / mechanical (appease charm, dispute lordship)
            float multiplier;
            if (response.Relation > 0)
            {
                // Grant / compromise. Generous + Honor + aligned interest + good
                // relation push toward yes. Calculating without alignment pulls
                // back (calculating ruler doesn't pay unless aligned).
                multiplier = 1f
                    + 0.5f * generosity
                    + 0.4f * honor
                    + 0.3f * relationFactor
                    + interestAlignment
                    - 0.3f * calculating * (1f - interestAlignment)
                    + 0.5f * radicalRefusalPenalty;  // grant defuses radical revolt
            }
            else if (response.Relation < 0)
            {
                // Refuse / suppress. Calculating + Cruel (=-Mercy) + bad relation
                // + valor push toward no. Honor + Mercy + aligned interest pull
                // back (won't crush legitimate grievance or own faction).
                multiplier = 1f
                    + 0.5f * calculating
                    - 0.5f * mercy
                    + 0.3f * valor
                    - 0.3f * relationFactor
                    - 0.4f * honor
                    - interestAlignment
                    - 0.5f * radicalRefusalPenalty; // calculating ruler avoids refusal when radical
            }
            else
            {
                // Neutral / mechanical (charm appeal, lordship dispute).
                // Mild calculating bias — efficient rulers favour the cheap
                // procedural way out over either capitulation or confrontation.
                multiplier = 1f + 0.25f * calculating + 0.15f * honor;
            }

            // Floor at 0.05 — even a maximally trait-opposed response stays
            // non-zero so the weighted random can still pick it occasionally,
            // preserving the existing stochastic feel of the system.
            return MathF.Max(0.05f, multiplier);
        }

        public override bool Equals(object obj)
        {
            if (obj is Demand)
            {
                Demand d = obj as Demand;
                return d.StringId == StringId && d.Group == Group;
            }
            return base.Equals(obj);
        }

        public class DemandResponse
        {
            private Func<Hero, bool> fulfill;
            private Func<Hero, bool> isAdequate;
            private Func<Hero, float> calculateAiLikelihood;

            public DemandResponse(TextObject name, TextObject description, TextObject explanation, int relation, int fulfillerXp,
                int groupLeaderXp, Func<Hero, bool> isAdequate, Func<Hero, float> calculateAiLikelihood, 
                Func<Hero, bool> fulfill)
            {
                this.fulfill = fulfill;
                this.isAdequate = isAdequate;
                this.calculateAiLikelihood = calculateAiLikelihood;
                Name = name;
                LoserXp = groupLeaderXp;
                SuccessXp = fulfillerXp;
                Description = description;
                Relation = relation;
                Explanation = explanation;
            }

            public TextObject Name { get; private set; }
            public TextObject Description { get; private set; }
            public TextObject Explanation { get; private set; }
            public int Relation { get; private set; }
            public int SuccessXp { get; private set; }
            public int LoserXp { get; private set; }
            public float CalculateAiLikelihood(Hero fulfiller) => calculateAiLikelihood(fulfiller);
            public bool IsAdequate(Hero fulfiller) => isAdequate(fulfiller);
            public bool Fulfill(Hero fulfiller) => fulfill(fulfiller);
        }
    }
}
