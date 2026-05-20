using System.Collections.Generic;
using BannerKings.Utils.BKData;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Mercenary
{
    /// <summary>
    /// Mercenary privileges are loaded from
    /// <c>ModuleData/BKData/bk_mercenary_privileges.xml</c> across every
    /// installed module (last writer wins per id). Each row's data (name,
    /// description, point cost, max level, unavailable-hint) is XML; its
    /// availability/grant logic is C# resolved by the <c>behavior</c> key
    /// against <see cref="MercenaryPrivilegeRegistry"/>. Named properties
    /// resolve by id.
    /// </summary>
    internal class DefaultMercenaryPrivileges : DefaultTypeInitializer<DefaultMercenaryPrivileges, MercenaryPrivilege>
    {
        private readonly List<MercenaryPrivilege> _loaded = new List<MercenaryPrivilege>();

        public MercenaryPrivilege IncreasedPay => GetById("privilege_increased_pay");
        public MercenaryPrivilege EstateGrant => GetById("privilege_estate_grant");
        public MercenaryPrivilege WorkshopGrant => GetById("privilege_workshop_grant");
        public MercenaryPrivilege CustomTroop3 => GetById("privilege_levy_troop");
        public MercenaryPrivilege CustomTroop5 => GetById("privilege_professional_troop");
        public MercenaryPrivilege BaronyGrant => GetById("privilege_barony_grant");
        public MercenaryPrivilege FullPeerage => GetById("privilege_full_peerage");

        public override IEnumerable<MercenaryPrivilege> All
        {
            get
            {
                foreach (var p in _loaded) yield return p;
                foreach (var item in ModAdditions) yield return item;
            }
        }

        public override void Initialize()
        {
            _loaded.Clear();
            foreach (var row in BKDataStore.Instance.GetRows("mercenary_privileges"))
            {
                var id = BKXml.Attr(row, "id");
                if (string.IsNullOrEmpty(id)) continue;

                var behaviorKey = BKXml.Attr(row, "behavior");
                var behavior = MercenaryPrivilegeRegistry.Get(behaviorKey);
                if (behavior == null)
                {
                    BKDataStore.Instance.AddDiagnostic(
                        "[BKData] mercenary_privilege '" + id + "': unknown behavior '" + behaviorKey + "'");
                    continue;
                }

                var points = BKXml.Float(row, "points", 0f);
                var maxLevel = BKXml.Int(row, "max_level", 1);

                var name = BKXml.LocText(row, "mercenary_privilege", id, "name", fallbackIfMissing: id);
                var description = BKXml.LocText(row, "mercenary_privilege", id, "description", fallbackIfMissing: string.Empty);
                var unavailableHint = BKXml.LocText(row, "mercenary_privilege", id, "unavailable_hint", fallbackIfMissing: string.Empty)
                    .SetTextVariable("POINTS", points)
                    .SetTextVariable("LEVEL", maxLevel);

                var privilege = new MercenaryPrivilege(id);
                privilege.Initialize(name, description, unavailableHint, points, maxLevel,
                    behavior.IsAvailable, behavior.OnAdded);
                _loaded.Add(privilege);
            }
        }
    }
}
