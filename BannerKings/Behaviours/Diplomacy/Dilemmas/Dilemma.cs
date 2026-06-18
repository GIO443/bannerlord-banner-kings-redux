using BannerKings.Managers.Titles;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace BannerKings.Behaviours.Diplomacy.Dilemmas
{
    /// <summary>
    /// One clan's stake in a dilemma: which side, the baseline weight derived
    /// from its standing (clout×military, government-modulated), and any extra
    /// weight committed through levers (spent gold / influence / negotiation).
    /// Side is a plain int (0 Neutral / 1 For / 2 Against) to avoid an enum save
    /// definition.
    /// </summary>
    public class SideCommitment
    {
        public SideCommitment() { }

        public SideCommitment(DilemmaSide side, float baseWeight)
        {
            Side = (int)side;
            BaseWeight = baseWeight;
            LeverWeight = 0f;
        }

        [SaveableProperty(1)] public int Side { get; set; }
        [SaveableProperty(2)] public float BaseWeight { get; set; }
        [SaveableProperty(3)] public float LeverWeight { get; set; }

        public DilemmaSide SideEnum => (DilemmaSide)Side;
        public float Total => BaseWeight + LeverWeight;
    }

    /// <summary>
    /// A live, in-progress dilemma in a kingdom — the runtime instance of a
    /// <see cref="DilemmaType"/>. Saveable; held on <c>KingdomDiplomacy</c> in its
    /// active list or pending queue. The two-sided push/pull resolves on the
    /// For/Against weight ratio at the timer, with short-circuit/backfire bands.
    ///
    /// The contest is computed at the CLAN level (one commitment per clan) — far
    /// cheaper than per-hero and consistent with how vanilla kingdom votes and
    /// BK's fief-ownership vote already work. Actors (initiator/target) stay
    /// heroes; the electorate is clans.
    /// </summary>
    public class Dilemma
    {
        public Dilemma() { }

        public Dilemma(string typeId, Kingdom kingdom, Hero initiator, Hero target, FeudalTitle title = null,
            string subjectId = null)
        {
            TypeId = typeId;
            Kingdom = kingdom;
            Initiator = initiator;
            Target = target;
            Title = title;
            SubjectId = subjectId;
            State = (int)DilemmaState.Pending;
            EnqueuedAt = CampaignTime.Now;
            DueDate = CampaignTime.Never;
            Commitments = new Dictionary<Clan, SideCommitment>();
        }

        [SaveableProperty(1)] public string TypeId { get; private set; }
        [SaveableProperty(2)] public Kingdom Kingdom { get; private set; }
        [SaveableProperty(3)] public Hero Initiator { get; private set; }
        [SaveableProperty(4)] public Hero Target { get; private set; }
        [SaveableProperty(5)] public CampaignTime DueDate { get; set; }
        [SaveableProperty(6)] public CampaignTime EnqueuedAt { get; private set; }
        [SaveableProperty(7)] public int State { get; set; }
        [SaveableProperty(8)] public Dictionary<Clan, SideCommitment> Commitments { get; private set; }
        [SaveableProperty(9)] public bool SidesAssigned { get; set; }
        [SaveableProperty(10)] public CampaignTime ActivatedAt { get; set; }
        // Optional subject of the dilemma — e.g. the contested title for a claim
        // press. Null for dilemmas that don't concern a specific title.
        [SaveableProperty(11)] public FeudalTitle Title { get; private set; }
        // Optional non-title subject, carried as a StringId so it round-trips
        // cleanly (a saved object reference to a BannerKingsObject may reload as a
        // fresh instance, not the registry singleton — see the language/book
        // re-key trap). Used by the "law" handler to name the DemesneLaw being
        // pushed; resolved against DefaultDemesneLaws by StringId at use time.
        // Null for dilemmas without a non-title subject.
        [SaveableProperty(12)] public string SubjectId { get; private set; }

        public DilemmaState StateEnum => (DilemmaState)State;

        /// <summary>The template this instance runs on, resolved by id. Null if the
        /// type was removed from data since the save was made — callers guard.</summary>
        public DilemmaType Type => DefaultDilemmas.Instance.GetById(TypeId);

        public bool IsActive => StateEnum == DilemmaState.Active;
        public bool IsPending => StateEnum == DilemmaState.Pending;

        // The commitments dict is read on the UI thread (the dilemmas panel) and
        // written on the campaign thread (engine participation/levers). Static
        // lock — always initialised, never null on a deserialised instance —
        // serialises every read/write so an enumeration can't race a structural
        // add. Same discipline as Truces/TradePacts.
        private static readonly object Sync = new object();

        public float ForWeight => SumSide(DilemmaSide.For);
        public float AgainstWeight => SumSide(DilemmaSide.Against);

        /// <summary>For-side fraction of committed (non-neutral) weight, 0.5 when
        /// nobody has committed yet so a fresh contest reads as a dead heat.</summary>
        public float Ratio
        {
            get
            {
                lock (Sync)
                {
                    float f = SumSideUnlocked(DilemmaSide.For);
                    float total = f + SumSideUnlocked(DilemmaSide.Against);
                    return total > 0f ? f / total : 0.5f;
                }
            }
        }

        private float SumSide(DilemmaSide side)
        {
            lock (Sync) { return SumSideUnlocked(side); }
        }

        private float SumSideUnlocked(DilemmaSide side)
        {
            if (Commitments == null) return 0f;
            float sum = 0f;
            foreach (var c in Commitments.Values)
                if (c != null && c.SideEnum == side) sum += MathF.Max(0f, c.Total);
            return sum;
        }

        public bool HasCommitment(Clan clan)
        {
            if (clan == null) return false;
            lock (Sync) { return Commitments != null && Commitments.ContainsKey(clan); }
        }

        /// <summary>Set (or replace) a clan's side at its baseline weight.</summary>
        public void SetCommitment(Clan clan, DilemmaSide side, float baseWeight)
        {
            if (clan == null) return;
            lock (Sync)
            {
                if (Commitments == null) Commitments = new Dictionary<Clan, SideCommitment>();
                Commitments[clan] = new SideCommitment(side, baseWeight);
            }
        }

        /// <summary>Add committed lever weight to a clan, creating the commitment
        /// on the given side if it isn't on a side yet.</summary>
        public void AddLeverWeight(Clan clan, DilemmaSide sideIfNew, float baseWeightIfNew, float leverWeight)
        {
            if (clan == null) return;
            lock (Sync)
            {
                if (Commitments == null) Commitments = new Dictionary<Clan, SideCommitment>();
                if (!Commitments.TryGetValue(clan, out var c) || c == null)
                {
                    c = new SideCommitment(sideIfNew, baseWeightIfNew);
                    Commitments[clan] = c;
                }
                c.LeverWeight += leverWeight;
            }
        }

        /// <summary>Lock-guarded snapshot for the UI / for iterating without
        /// racing a concurrent structural mutation.</summary>
        public List<KeyValuePair<Clan, SideCommitment>> SnapshotCommitments()
        {
            lock (Sync)
            {
                return Commitments != null
                    ? new List<KeyValuePair<Clan, SideCommitment>>(Commitments)
                    : new List<KeyValuePair<Clan, SideCommitment>>();
            }
        }

        public int CommitmentCount
        {
            get { lock (Sync) { return Commitments != null ? Commitments.Count : 0; } }
        }

        public void PostInitialize()
        {
            if (Commitments == null) Commitments = new Dictionary<Clan, SideCommitment>();
        }
    }

    public enum DilemmaState
    {
        Pending = 0,
        Active = 1,
        Resolved = 2,
    }
}
