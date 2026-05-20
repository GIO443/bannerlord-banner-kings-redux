using System;
using System.Collections.Generic;
using BannerKings.Behaviours.Mercenary;
using BannerKings.Managers;
using BannerKings.Managers.Court;
using BannerKings.Managers.Titles;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BannerKings.Utils.BKData
{
    /// <summary>
    /// One mercenary privilege's behaviour: an availability predicate and an
    /// on-granted effect. Game logic — stays in C#.
    /// </summary>
    public sealed class MercenaryPrivilegeBehavior
    {
        public Func<MercenaryCareer, bool> IsAvailable;
        public Func<MercenaryCareer, bool> OnAdded;
    }

    /// <summary>
    /// Named-key → <see cref="MercenaryPrivilegeBehavior"/> registry.
    /// <c>bk_mercenary_privileges.xml</c> carries each privilege's data (name,
    /// description, point cost, max level, unavailable-hint text) and a
    /// <c>behavior</c> key; the loader pairs the data row with the behaviour
    /// resolved here. Group-B pattern — see SuccessionRegistry.
    /// </summary>
    public static class MercenaryPrivilegeRegistry
    {
        private static readonly Dictionary<string, MercenaryPrivilegeBehavior> _behaviors
            = new Dictionary<string, MercenaryPrivilegeBehavior>(StringComparer.OrdinalIgnoreCase);

        public static void Register(string key, MercenaryPrivilegeBehavior behavior)
        {
            if (string.IsNullOrEmpty(key) || behavior == null) return;
            _behaviors[key] = behavior;
        }

        public static MercenaryPrivilegeBehavior Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return _behaviors.TryGetValue(key, out var b) ? b : null;
        }

        public static bool IsKnown(string key)
            => !string.IsNullOrEmpty(key) && _behaviors.ContainsKey(key);

        static MercenaryPrivilegeRegistry()
        {
            _behaviors["IncreasedPay"] = new MercenaryPrivilegeBehavior
            {
                IsAvailable = career => true,
                OnAdded = career => true,
            };

            _behaviors["WorkshopGrant"] = new MercenaryPrivilegeBehavior
            {
                IsAvailable = career => MercenaryCareer.GetWorkshopPrivilege(career) != null,
                OnAdded = career =>
                {
                    var workshop = MercenaryCareer.GetWorkshopPrivilege(career);
                    if (workshop != null)
                    {
                        ChangeOwnerOfWorkshopAction.ApplyByDeath(workshop, career.Clan.Leader);
                        MBInformationManager.AddQuickInformation(new TextObject("{=7OaTLJKt}You are now the owner of {WORKSHOP} at {TOWN}!")
                            .SetTextVariable("WORKSHOP", workshop.Name)
                            .SetTextVariable("TOWN", workshop.Settlement.Name),
                            0,
                            null,
                            null, Utils.Helpers.GetRelationDecisionSound());
                        return true;
                    }

                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=kQDtk9bU}Your contractor was not able to secure a Workshop.")
                        .ToString()));
                    return false;
                },
            };

            _behaviors["EstateGrant"] = new MercenaryPrivilegeBehavior
            {
                IsAvailable = career => MercenaryCareer.GetEstatePrivilege(career) != null,
                OnAdded = career =>
                {
                    var estate = MercenaryCareer.GetEstatePrivilege(career);
                    if (estate != null)
                    {
                        var action = BannerKingsConfig.Instance.EstatesModel.GetGrant(estate, estate.Owner, career.Clan.Leader);
                        action.TakeAction();
                        return true;
                    }

                    InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=YV3Df2q9}Your contractor was not able to secure an Estate.")
                        .ToString()));
                    return false;
                },
            };

            _behaviors["CustomTroop3"] = new MercenaryPrivilegeBehavior
            {
                IsAvailable = career => true,
                OnAdded = career => true,
            };

            _behaviors["CustomTroop5"] = new MercenaryPrivilegeBehavior
            {
                IsAvailable = career => true,
                OnAdded = career => true,
            };

            _behaviors["BaronyGrant"] = new MercenaryPrivilegeBehavior
            {
                IsAvailable = career => MercenaryCareer.GetBaronyPrivilege(career) != null,
                OnAdded = career =>
                {
                    var barony = MercenaryCareer.GetBaronyPrivilege(career);
                    if (barony != null)
                    {
                        var title = BannerKingsConfig.Instance.TitleManager.GetTitle(barony);
                        var action = BannerKingsConfig.Instance.TitleModel.GetAction(ActionType.Grant,
                            title, title.deJure, career.Clan.Leader);
                        action.TakeAction(career.Clan.Leader);
                        return true;
                    }

                    return false;
                },
            };

            _behaviors["FullPeerage"] = new MercenaryPrivilegeBehavior
            {
                IsAvailable = career =>
                {
                    var council = BannerKingsConfig.Instance.CourtManager.GetCouncil(career.Clan);
                    Peerage peerage = council.Peerage;
                    return peerage == null || !peerage.IsFullPeerage;
                },
                OnAdded = career =>
                {
                    var council = BannerKingsConfig.Instance.CourtManager.GetCouncil(Clan.PlayerClan);
                    if (council.Peerage == null || !council.Peerage.IsFullPeerage)
                    {
                        council.SetPeerage(new Peerage(new TextObject("{=9OhMK2Wk}Full Peerage"), true,
                                        true, true, true, true, false));
                        if (career.Clan == Clan.PlayerClan)
                        {
                            var peerage = council.Peerage;
                            InformationManager.ShowInquiry(new InquiryData(
                                peerage.Name.ToString(),
                                new TextObject("{=NgoW3jRZ}A a privilege for your loyal service, the {CLAN} is now considered to have {PEERAGE}. {TEXT}")
                                .SetTextVariable("CLAN", Clan.PlayerClan.Name)
                                .SetTextVariable("PEERAGE", peerage.Name)
                                .SetTextVariable("TEXT", peerage.PeerageGrantedText())
                                .ToString(),
                                true,
                                false,
                                GameTexts.FindText("str_ok").ToString(),
                                String.Empty,
                                null,
                                null));
                        }

                        return true;
                    }
                    return false;
                },
            };
        }
    }
}
