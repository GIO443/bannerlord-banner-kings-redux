using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Criminality
{
    public class CriminalSentence : BannerKingsObject
    {
        private Func<Crime, bool> isAdequateForHero;
        private Action<Crime, Hero, CriminalSentence> executeSentence;
        private Func<Crime, Hero, bool> isSentenceTyranical;

        public CriminalSentence(string stringId) : base(stringId)
        {
            SentenceDate = CampaignTime.Never;
        }

        public void Initialize(TextObject name, TextObject description, Func<Crime, bool> isAdequateForHero,
            Func<Crime, Hero, bool> isSentenceTyranical, Action<Crime, Hero, CriminalSentence> executeSentence)
        {
            Initialize(name, description);
            this.isAdequateForHero = isAdequateForHero;
            this.isSentenceTyranical = isSentenceTyranical;
            this.executeSentence = executeSentence;
        }

        public CampaignTime SentenceDate { get; private set; }

        public bool IsSentenceTyranical(Crime crime, Hero executor) => isSentenceTyranical(crime, executor);

        public bool IsAdequateForHero(Crime crime) => isAdequateForHero(crime);

        public void ExecuteSentence(Crime crime, Hero executor)
        {
            SentenceDate = CampaignTime.Now;
            executeSentence(crime, executor, this);
            if (IsSentenceTyranical(crime, executor))
            {
                foreach (var clan in crime.Kingdom.Clans)
                {
                    if (!clan.IsEliminated && !clan.IsBanditFaction && clan != Clan.PlayerClan && clan.Leader != null
                        && executor != null && clan.Leader != executor)
                    {
                        int relationChangeForExecutingHero = ExecutionRelations.GetRelationChangeForExecutingHero(
                            crime.Hero, clan.Leader, out bool showNotification);
                        if (relationChangeForExecutingHero != 0)
                        {
                            // Penalise the hero who actually carried out the sentence.
                            // Player: unchanged 1.4 call (spreads to the leader's
                            // relatives). AI: was wrongly ApplyPlayerRelation too,
                            // charging the player for an AI ruler's sentence.
                            if (executor == Hero.MainHero)
                                ChangeRelationAction.ApplyPlayerRelation(clan.Leader,
                                    relationChangeForExecutingHero, showNotification, true);
                            else
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(executor, clan.Leader,
                                    relationChangeForExecutingHero, false);
                        }
                    }
                }
            }

            BKCriminalityBehavior behavior = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<BKCriminalityBehavior>();
            behavior.FinishCrime(crime);
        }
    }
}
