using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace BannerKings.Behaviours.Diplomacy.Dilemmas
{
    /// <summary>
    /// Which side of a dilemma a clan stands on. Stored on
    /// <see cref="SideCommitment"/> as a plain int so the save system never
    /// needs an enum-type definition.
    /// </summary>
    public enum DilemmaSide
    {
        Neutral = 0,
        For = 1,
        Against = 2,
    }

    /// <summary>
    /// The CODE half of the data/code split (see <see cref="DilemmaType"/> for the
    /// XML half). One handler per <c>behavior</c> key in <c>bk_dilemmas.xml</c>,
    /// registered in <see cref="DilemmaRegistry"/>. Everything here is game logic
    /// that can't live in XML: which side a clan picks, what each resolution band
    /// actually DOES, whether the dilemma may exist, how much an AI clan cares,
    /// and the player's negotiation palette.
    ///
    /// Mirrors <c>CasusBelliBehavior</c> / <c>SuccessionBehavior</c>. A data-only
    /// mod reuses an existing key and just retunes the XML; a code mod registers
    /// a new handler.
    /// </summary>
    public sealed class DilemmaHandler
    {
        /// <summary>Which side this clan stands on (or Neutral to abstain).</summary>
        public Func<Dilemma, Clan, DilemmaSide> AssignSide;

        /// <summary>R ≥ t_strong — overwhelming, resolved before the timer.</summary>
        public Action<Dilemma> OnStrong;
        /// <summary>t_win ≤ R &lt; t_strong — the motion carries at the timer.</summary>
        public Action<Dilemma> OnWin;
        /// <summary>t_partial ≤ R &lt; t_win — a compromise outcome.</summary>
        public Action<Dilemma> OnPartial;
        /// <summary>t_fail ≤ R &lt; t_partial — the motion fails, status quo holds.</summary>
        public Action<Dilemma> OnFail;
        /// <summary>R &lt; t_fail — backfire; the initiator loses standing.</summary>
        public Action<Dilemma> OnBackfire;

        /// <summary>Whether the dilemma may start / continue (initiator alive, etc.).</summary>
        public Func<Dilemma, ValueTuple<bool, TextObject>> IsAdequate;

        /// <summary>Per-instance display name (e.g. "Claim on Duchy of X"). When
        /// null the UI falls back to the static <see cref="DilemmaType"/> name.</summary>
        public Func<Dilemma, TextObject> DisplayName;

        /// <summary>How much this clan cares (0..1) — scales AI lever spend.</summary>
        public Func<Dilemma, Clan, float> CareFactor;

        /// <summary>Player negotiation offers targeting a specific clan. Null/empty
        /// is fine — Phase 1 ships only the money/influence levers; the rich
        /// offer palette (quest / fief / faction-join) lands in a later phase.</summary>
        public Func<Dilemma, Clan, IEnumerable<DilemmaOffer>> Offers;
    }

    /// <summary>
    /// A single negotiation offer the player can extend to a clan to move it
    /// toward their side. Phase-1 scaffold — only <see cref="Name"/> +
    /// <see cref="Apply"/> are wired; deferred-obligation offers (quests, fief
    /// grants, faction-join) flesh this out later.
    /// </summary>
    public sealed class DilemmaOffer
    {
        public TextObject Name;
        public TextObject Description;
        public Func<Dilemma, Clan, bool> IsAvailable;
        public Action<Dilemma, Clan> Apply;
    }
}
