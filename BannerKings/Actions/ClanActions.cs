using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace BannerKings.Actions
{
    public static class ClanActions
    {
        public static TextObject CanCreateNewClan(CultureObject culture, Settlement settlement, TextObject name = null)
        {
            if (name == null)
            {
                name = GetRandomAvailableName(culture, settlement);
            }

            var names = new List<string>();
            foreach (var existingClan in Clan.All.Where(x => x.Culture == culture))
            {
                names.Add(existingClan.Name.ToString());
            }

            if (name == null || names.Any(x => x.Contains(name.ToString()) || x.ToString().Equals(name.ToString())))
            {
                return null;
            }

            return name;
        }

        public static Clan CreateNewClan(Hero hero, Settlement settlement, string id, TextObject name = null,
            float renown = 150f, bool removeGold = false)
        {
            if (name == null)
            {
                name = CanCreateNewClan(hero.Culture, settlement);
            }

            if (name == null)
            {
                return null;
            }

            var originalClan = hero.Clan;
            var clan = Clan.CreateClan(id);

            hero.Clan = null;
            hero.CompanionOf = null;
            clan.ChangeClanName(name, name);
            clan.Culture = hero.Culture;
            clan.Banner = Banner.CreateOneColoredBannerWithOneIcon(
                settlement.MapFaction.Banner.GetFirstIconColor(), settlement.MapFaction.Banner.GetPrimaryColor(),
                hero.Culture.PossibleClanBannerIconsIDs.GetRandomElement());
            clan.AddRenown(renown);
            hero.Clan = clan;
            clan.SetLeader(hero);
            clan.SetInitialHomeSettlement(settlement);

            // v1.9.10.10 — re-parent any lord party the hero is
            // leading. Setting hero.Clan doesn't touch
            // WarPartyComponents; the source of truth is
            // MobileParty.ActualClan, which fires
            // WarPartyComponent.OnClanChange(old, new) to remove the
            // entry from the old clan's WarPartyComponents collection
            // and add it to the new clan's. Without this, the knight
            // still leads their party but the party stays registered
            // in the original clan — players saw it in BOTH clans on
            // the management screen ("they remain in your clan
            // incorrectly as well as being in the new clan").
            //
            // v1.9.10.17 — only reassign LORD parties. If the hero
            // happens to lead a caravan or a villager party (unusual
            // but possible), those carry ActualClan = owning-clan,
            // which legitimately differs from leader-Clan and should
            // not be force-moved on the leader's clan change.
            if (hero.PartyBelongedTo is { } mpNew
                && mpNew.PartyComponent is TaleWorlds.CampaignSystem.Party.PartyComponents.LordPartyComponent
                && mpNew.ActualClan != clan)
            {
                mpNew.ActualClan = clan;
            }

            if (hero.Spouse != null && !Utils.Helpers.IsClanLeader(hero.Spouse))
            {
                JoinClan(hero.Spouse, clan);
            }

            if (hero.Children.Count > 0)
            {
                foreach (var child in hero.Children)
                {
                    if (child.IsChild)
                    {
                        JoinClan(child, clan);
                    }
                }
            }

            if (originalClan != null)
            {
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, originalClan.Kingdom, default(CampaignTime), false);
                // v1.9.10.11 — full severance: clear lingering BK refs in
                // the original clan that would otherwise keep the now-
                // knighted hero visible there. Vanilla Hero.Clan setter
                // moves the hero's _lords entry but doesn't touch BK
                // CourtManager council seats or vanilla party role
                // assignments (Engineer/Surgeon/Quartermaster/Scout) the
                // hero held on someone else's party in the old clan.
                try
                {
                    var oldCouncil = BannerKingsConfig.Instance?.CourtManager?.GetCouncil(originalClan);
                    if (oldCouncil != null)
                    {
                        foreach (var pos in oldCouncil.GetHeroPositions(hero))
                        {
                            pos.SetMember(null);
                        }
                    }
                }
                catch { /* defensive — never break clan creation on a court hiccup */ }

                try
                {
                    foreach (var wpc in originalClan.WarPartyComponents)
                    {
                        var mp = wpc?.MobileParty;
                        if (mp == null) continue;
                        if (mp.EffectiveEngineer == hero) mp.SetPartyEngineer(null);
                        if (mp.EffectiveSurgeon == hero) mp.SetPartySurgeon(null);
                        if (mp.EffectiveQuartermaster == hero) mp.SetPartyQuartermaster(null);
                        if (mp.EffectiveScout == hero) mp.SetPartyScout(null);
                    }
                }
                catch { /* defensive */ }
            }

            BannerKingsConfig.Instance.TitleManager.RemoveKnights(hero);
            if (removeGold)
            {
                hero.ChangeHeroGold(-50000);
            }

            return clan;
        }

        public static void JoinClan(Hero hero, Clan clan)
        {
            hero.Clan = null;
            hero.CompanionOf = null;
            hero.SetNewOccupation(Occupation.Lord);
            hero.Clan = clan;
            // Re-parent any LORD party the joining hero leads — same
            // duplicate-source-of-truth fix as CreateNewClan. Rare via
            // this path (spouses/children typically don't lead parties
            // at the moment of clan-join) but cheap and correct.
            //
            // v1.9.10.17 — only LordPartyComponent. Caravans/villager
            // parties carry ActualClan = owning-clan independent of
            // leader-Clan and should not be force-moved.
            if (hero.PartyBelongedTo is { } mpJoin
                && mpJoin.PartyComponent is TaleWorlds.CampaignSystem.Party.PartyComponents.LordPartyComponent
                && mpJoin.ActualClan != clan)
            {
                mpJoin.ActualClan = clan;
            }
        }

        public static TextObject GetRandomAvailableName(CultureObject culture, Settlement settlement)
        {
            TextObject random;
            if (culture.ClanNameList.Count > 1)
            {
                random = culture.ClanNameList.FirstOrDefault(x => IsAvailable(x, culture));
            }
            else
            {
                random = culture.ClanNameList[0];
                random.SetTextVariable("ORIGIN_SETTLEMENT", settlement.Name);
            }

            return random;
        }

        private static bool IsAvailable(TextObject name, CultureObject culture)
        {
            var names = new List<string>();
            foreach (var existingClan in Clan.All.Where(x => x.Culture == culture))
            {
                names.Add(existingClan.Name.ToString());
            }

            if (names.Any(x => x.Contains(name.ToString()) || x.ToString().Equals(name.ToString())))
            {
                return false;
            }

            return true;
        }
    }
}