using BannerKings.Behaviours.Mercenary;
using BannerKings.CampaignContent.Economy.Markets;
using BannerKings.Managers.Skills;
using BannerKings.Models.BKModels.Abstract;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace BannerKings.Models.BKModels
{
    public class BKMercenaryModel : MercenaryModel
    {

        public override ExplainedNumber GetDailyCareerPointsGain(Clan clan, MercenaryCareer career, bool explanations = false)
        {
            var result = new ExplainedNumber(1f, explanations);
            result.Add(clan.Tier / 2f, GameTexts.FindText("str_clan_tier_bonus"));
            result.Add(career.Reputation * 2f, new TaleWorlds.Localization.TextObject("{=bLLovmn9}Reputation"));

            foreach (var party in clan.WarPartyComponents)
            {
                if (party.MobileParty.Army != null)
                {
                    result.Add(1f, party.Name);
                    if (party.MobileParty.Army.LeaderParty == party.MobileParty)
                    {
                        result.AddFactor(0.2f, new TaleWorlds.Localization.TextObject("{=oV2MhyoO}Leading an Army"));
                    }
                }
            }

            Utils.Helpers.ApplyPerk(BKPerks.Instance.LordshipSellswordCareer, clan.Leader, ref result);
            return result;
        }
        public override IEnumerable<ItemCategory> GetLevyCategories()
        {
            // v1.9.10.31 — widened: previously only tier-2 weapons +
            // LightArmor + Shield2 + Horse(1-2). Users picking
            // equipment for a Levy mercenary saw almost nothing in
            // weapon slots because tier-1 categories were excluded
            // entirely and there's no Polearms2 in the original list.
            // Include tier-1 melee / ranged / shield + Polearms 1-2.
            yield return DefaultItemCategories.Garment;
            yield return DefaultItemCategories.LightArmor;
            yield return DefaultItemCategories.MeleeWeapons1;
            yield return DefaultItemCategories.MeleeWeapons2;
            yield return DefaultItemCategories.RangedWeapons1;
            yield return DefaultItemCategories.RangedWeapons2;
            yield return DefaultItemCategories.Horse;
            yield return DefaultItemCategories.Shield2;
            yield return DefaultItemCategories.HorseEquipment;
            yield return DefaultItemCategories.HorseEquipment2;
            yield return DefaultItemCategories.Arrows;
        }

        public override IEnumerable<ItemCategory> GetNonCulturalCategories()
        {
            yield return DefaultItemCategories.Arrows;
            yield return DefaultItemCategories.RangedWeapons1;
            yield return DefaultItemCategories.RangedWeapons2;
            yield return DefaultItemCategories.RangedWeapons3;
            yield return DefaultItemCategories.RangedWeapons4;
            yield return DefaultItemCategories.RangedWeapons5;
        }

        public override IEnumerable<ItemCategory> GetProCategories()
        {
            // v1.9.10.31 — widened: previously only MediumArmor +
            // LightArmor; HeavyArmor missing entirely so Pro troops
            // couldn't equip top-tier body armor. Polearm and tier-5
            // weapons also missing. HorseEquipment5 added for
            // completeness.
            yield return DefaultItemCategories.HeavyArmor;
            yield return DefaultItemCategories.MediumArmor;
            yield return DefaultItemCategories.LightArmor;
            yield return DefaultItemCategories.Garment;
            yield return DefaultItemCategories.MeleeWeapons3;
            yield return DefaultItemCategories.MeleeWeapons4;
            yield return DefaultItemCategories.MeleeWeapons5;
            yield return DefaultItemCategories.RangedWeapons3;
            yield return DefaultItemCategories.RangedWeapons4;
            yield return DefaultItemCategories.RangedWeapons5;
            yield return DefaultItemCategories.Horse;
            yield return DefaultItemCategories.WarHorse;
            yield return DefaultItemCategories.Shield3;
            yield return DefaultItemCategories.Shield4;
            yield return DefaultItemCategories.HorseEquipment2;
            yield return DefaultItemCategories.HorseEquipment3;
            yield return DefaultItemCategories.HorseEquipment4;
            yield return DefaultItemCategories.HorseEquipment5;
            yield return DefaultItemCategories.Arrows;
        }

        public override bool IsEquipmentAdequate(ItemObject item, CharacterObject troop, bool levy)
        {
            MarketGroup group = DefaultMarketGroups.Instance.GetMarket(troop.Culture);
            if (item.IsCraftedByPlayer) return false;

            if (item.Culture == null) return true;

            bool culture = false;
            bool adequate = false;
            if (GetNonCulturalCategories().Contains(item.ItemCategory)) culture = true; 
            else
            {
                if (item.Culture == troop.Culture) culture = true;
                else if (group != null && group.GetSpawn((CultureObject)item.Culture) > 0f) culture = true; 
            }
            
            if (levy && GetLevyCategories().Contains(item.ItemCategory)) adequate = true;
            else if (!levy && GetProCategories().Contains(item.ItemCategory)) adequate = true;

            return adequate && culture;
        }
    }
}
