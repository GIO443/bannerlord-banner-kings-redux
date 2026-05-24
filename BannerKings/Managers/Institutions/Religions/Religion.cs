using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Institutions.Religions.Doctrines;
using BannerKings.Managers.Institutions.Religions.Faiths;
using BannerKings.Managers.Institutions.Religions.Faiths.Rites;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade.Diamond.Ranked;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers.Institutions.Religions
{
    public class Religion : MBObjectBase
    {
        [SaveableField(4)] private Dictionary<Settlement, Clergyman> clergy; 
        [field: SaveableField(3)] public Faith Faith { get; private set; }

        public Religion(string id) : base(id)
        {
            clergy = new Dictionary<Settlement, Clergyman>();
        }

        public void Initialize(Faith faith, 
            List<CultureObject> favoredCultures)
        {
            Faith = faith;
            FavoredCultures = favoredCultures;
        }

        internal void PostInitialize()
        {
            Faith faith = DefaultFaiths.Instance.GetById(Faith?.StringId);
            if (clergy == null) clergy = new Dictionary<Settlement, Clergyman>();
            // DefaultReligions.GetById may legitimately return null when the
            // saved religion was dropped this run (e.g. Osfeyd loaded from a
            // save where War Sails is no longer present). Keep the saved
            // FavoredCultures rather than NRE on rel.FavoredCultures.
            Religion rel = DefaultReligions.Instance.GetById(this);
            if (rel != null) FavoredCultures = rel.FavoredCultures;
            else if (FavoredCultures == null) FavoredCultures = new List<CultureObject>();
            Faith = faith;

            if (faith != null)
            {
                var presets = CharacterObject.All.Where(x => x.Occupation == Occupation.Preacher && x.IsTemplate && x.StringId.Contains("bannerkings") && x.StringId.Contains(faith.GetId()));
                foreach (var preset in presets)
                {
                    var number = int.Parse(preset.StringId[preset.StringId.Length - 1].ToString());
                    faith.AddPreset(number, preset);
                }
            }
        }

        public MBReadOnlyDictionary<Settlement, Clergyman> Clergy => clergy.GetReadOnlyDictionary();
        public ExplainedNumber Fervor => BannerKingsConfig.Instance.ReligionModel.CalculateFervor(this);
        public List<CultureObject> FavoredCultures { get; private set; }
        // v1.9.6.0 sweep: Faith can legitimately be null on a save-restored
        // Religion whose StringId no longer resolves in DefaultFaiths (e.g.
        // a War Sails faith loaded into a save without War Sails). The
        // PostInitialize fallback sets Faith = null in that case (line 47);
        // these accessors then must not dereference it. Return empty
        // collection / null leader instead of NRE-ing the UI / daily tick.
        public MBReadOnlyList<Rite> Rites => Faith?.Rites != null
            ? new MBReadOnlyList<Rite>(Faith.Rites)
            : new MBReadOnlyList<Rite>(new List<Rite>());
        // FavoredCultures can legitimately be empty — PostInitialize falls a
        // saved religion whose id no longer resolves in DefaultReligions (e.g.
        // a War Sails faith on a save without War Sails) back to an empty
        // list. Return null instead of indexing [0] and throwing.
        public CultureObject MainCulture => (FavoredCultures != null && FavoredCultures.Count > 0)
            ? FavoredCultures[0]
            : null;
        public Hero FaithLeader => Faith?.FaithGroup?.Leader;

        public bool HasDoctrine(Doctrine doctrine)
        {
            if (Faith != null && Faith.Doctrines != null) return Faith.Doctrines.Contains(doctrine);
            return false;
        }

        public FaithStance GetStance(Faith otherFaith)
        {
            if (HasDoctrine(DefaultDoctrines.Instance.Tolerant)) return FaithStance.Tolerated;
            // v1.9.6.0 sweep: Faith may be null on a save-restored stub
            // whose StringId no longer resolves. A null-faith stub
            // shouldn't be picking fights — return the same Tolerated
            // value as the doctrine branch above so downstream judgement
            // logic treats the broken religion as passive instead of
            // an active negative.
            if (Faith == null) return FaithStance.Tolerated;
            return Faith.GetStance(otherFaith);
        }

        public void ChangeClergymanRank(Clergyman clergyman, int newRank)
        {
            if (clergyman?.Hero == null || Faith == null) return;
            var firstName = clergyman.Hero.FirstName;
            var fullName = new TextObject("{=6MHqUBXt}{RELIGIOUS_TITLE} {NAME}")
                .SetTextVariable("RELIGIOUS_TITLE", Faith.GetRankTitle(newRank))
                .SetTextVariable("NAME", firstName);
            clergyman.Hero.SetName(fullName, firstName);
            clergyman.Rank = newRank;
        }

        public void RemoveClergyman(Settlement settlement)
        {
            if (settlement == null) return;
            if (!clergy.TryGetValue(settlement, out var clergyman) || clergyman == null) return;
            clergy.Remove(settlement);
            if (clergyman.Hero == null) return;
            List<Hero> notables = (List<Hero>)AccessTools.Field(settlement.GetType(), "_notablesCache").GetValue(settlement);
            if (notables != null && notables.Contains(clergyman.Hero))
            {
                notables.Remove(clergyman.Hero);
                KillCharacterAction.ApplyByRemove(clergyman.Hero);
            }
        }

        public void AddClergyman(Settlement settlement, Hero hero)
        {
            var clergyman = new Clergyman(hero, Faith.GetIdealRank(settlement));
            clergy[settlement] = clergyman;
        }

        public Clergyman GetClergyman(Settlement settlement)
        {
            if (settlement == null) return null;
            if (clergy.TryGetValue(settlement, out var existing))
            {
                // The variable is a Clergyman (not a Hero) — naming was
                // misleading in the original. Treat null Clergyman OR null
                // inner Hero OR dead Hero as "regenerate".
                if (existing == null || existing.Hero == null || existing.Hero.IsDead)
                {
                    var fresh = GenerateClergyman(settlement);
                    clergy[settlement] = fresh;
                    return fresh;
                }
                return existing;
            }
            return GenerateClergyman(settlement);
        }
        
        public Clergyman GenerateClergyman(Settlement settlement)
        {
            // v1.9.6.0 sweep: save-restored Religion can have Faith == null
            // when its StringId no longer resolves in DefaultFaiths (mod
            // uninstall mid-campaign). No faith → no clergy generation;
            // bail before touching Faith.X.
            if (Faith == null) return null;
            var rank = Faith.GetIdealRank(settlement);
            if (rank <= 0)
            {
                return null;
            }

            var character = Faith.GetPreset(rank);
            var title = Faith.GetRankTitle(rank);
            Hero preacher = settlement.HeroesWithoutParty.FirstOrDefault(x => x.IsPreacher && x.Name.ToString().Contains(title.ToString()));
            if (preacher != null)
            {
                var clergyman = new Clergyman(preacher, rank);
                if (!clergy.ContainsKey(settlement))
                {
                    clergy.Add(settlement, clergyman);
                }
                else
                {
                    clergy[settlement] = clergyman;
                }
                BannerKingsConfig.Instance.ReligionsManager.ExecuteAddToReligion(preacher, this);
                return clergyman;
            }

            if (character != null)
            {
                var hero = GenerateClergymanHero(character, settlement, rank);
                EnterSettlementAction.ApplyForCharacterOnly(hero, settlement);
                var clergyman = new Clergyman(hero, rank);
                if (!clergy.ContainsKey(settlement))
                {
                    clergy.Add(settlement, clergyman);
                } else
                {
                    clergy[settlement] = clergyman;
                }
                hero.Culture = character.Culture;
                BannerKingsConfig.Instance.ReligionsManager.AddToReligion(hero, this);
                return clergyman;
            }

            throw new BannerKingsException(string.Format("No preset found for faith with id [{0}] at clergy rank [{1}]",
                Faith.GetId(), rank));
        }

        public void SetClergyName(Hero hero, TextObject title)
        {
            var firstName = hero.FirstName;
            var fullName = new TextObject("{=6MHqUBXt}{RELIGIOUS_TITLE} {NAME}")
                .SetTextVariable("RELIGIOUS_TITLE", title)
                .SetTextVariable("NAME", firstName);
            hero.SetName(fullName, firstName);
        }

        private Hero GenerateClergymanHero(CharacterObject preset, Settlement settlement, int rank)
        {
            if (preset == null) return null;
            Settlement culturalSettlement = Settlement.All.GetRandomElementWithPredicate(x => x.Culture == preset.Culture);
            var bornAt = culturalSettlement ?? settlement ?? Settlement.All.FirstOrDefault();
            if (bornAt == null) return null;
            var hero = HeroCreator.CreateSpecialHero(preset, bornAt);
            if (hero == null) return null;
            SetClergyName(hero, Faith.GetRankTitle(rank));
            return hero;
        }

        public override bool Equals(object obj)
        {
            if (obj is Religion rel)
            {
                // Use StringId — set once at construction and never mutated.
                // Was Faith.GetId(): Faith is reassigned during PostInitialize
                // (`Faith = faith;` after DefaultFaiths.GetById lookup), so
                // any hash derived from Faith.GetId() is volatile. A
                // Religion held as a Dictionary key would land in one
                // bucket on Add and a different bucket on lookup once Faith
                // got swapped → KeyNotFoundException at Religions[key].
                return StringId == rel.StringId;
            }

            return base.Equals(obj);
        }

        // GetHashCode MUST be derived from a stable field. StringId is set
        // by MBObjectBase's ctor and never mutates; Religion.id == Faith.GetId()
        // by construction in DefaultReligions.Build, so this matches the
        // historical Faith.GetId()-based comparison without the
        // mutable-hashcode trap that crashed BalanceReligions on saves
        // where PostInitialize had swapped Faith between Add and lookup.
        public override int GetHashCode()
        {
            return StringId != null ? StringId.GetHashCode() : 0;
        }
    }
}