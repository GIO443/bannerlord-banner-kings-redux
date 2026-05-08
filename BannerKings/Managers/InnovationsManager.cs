using System.Collections.Generic;
using System.Linq;
using BannerKings.Managers.Innovations;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.SaveSystem;

namespace BannerKings.Managers
{
    public class InnovationsManager
    {
        public InnovationsManager()
        {
            Innovations = new Dictionary<CultureObject, InnovationData>();
        }

        [SaveableProperty(1)] private Dictionary<CultureObject, InnovationData> Innovations { get; set; }

        // Innovations is read on the UI thread (EncyclopediaUnitPageMixin,
        // CultureTabVM, UIManager) and written from the campaign thread
        // (PostInitialize, yearly UpdateInnovations). Plain Dictionary<,>
        // resize freezes the reader inside FindEntry.
        private object _cacheLock;
        private object CacheLock
        {
            get
            {
                if (_cacheLock == null)
                    System.Threading.Interlocked.CompareExchange(ref _cacheLock, new object(), null);
                return _cacheLock;
            }
        }

        private void InitializeInnovations()
        {
            // Caller holds CacheLock.
            if (Innovations == null) Innovations = new Dictionary<CultureObject, InnovationData>();
            foreach (var culture in Game.Current.ObjectManager.GetObjectTypeList<CultureObject>().Where(culture => !culture.IsBandit && culture.CanHaveSettlement))
            {
                if (!Innovations.ContainsKey(culture))
                {
                    InnovationData data = new InnovationData(new List<Innovation>(), culture);
                    Innovations.Add(culture, data);
                    foreach (var innovation in DefaultInnovations.Instance.All)
                        data.AddInnovation(innovation.GetCopy(culture));
                    data.Update();
                }
            }
        }

        public void PostInitialize()
        {
            List<InnovationData> snapshot;
            lock (CacheLock)
            {
                if (Innovations == null) Innovations = new Dictionary<CultureObject, InnovationData>();
                InitializeInnovations();
                snapshot = Innovations.Values.ToList();
            }
            foreach (var data in snapshot)
            {
                foreach (var innovation in DefaultInnovations.Instance.All)
                {
                    data.AddInnovation(innovation);
                }

                data.PostInitialize();
            }
        }

        public void UpdateInnovations()
        {
            List<InnovationData> snapshot;
            lock (CacheLock)
            {
                if (Innovations == null) return;
                snapshot = Innovations.Values.ToList();
            }
            foreach (var data in snapshot)
            {
                data.Update();
            }
        }

        public InnovationData GetInnovationData(CultureObject culture)
        {
            if (culture == null) return null;
            lock (CacheLock)
            {
                if (Innovations != null && Innovations.TryGetValue(culture, out var data))
                {
                    return data;
                }
            }
            return null;
        }
    }
}