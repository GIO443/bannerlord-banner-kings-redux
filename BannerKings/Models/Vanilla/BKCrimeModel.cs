using BannerKings.Behaviours.Criminality;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace BannerKings.Models.Vanilla
{
    /// <summary>
    /// Banner Kings used to override <see cref="DefaultCrimeModel.GetDailyCrimeRatingChange"/>
    /// to zero out crime rating change while the Outlaw start debuff is active.
    /// That override moved to a Harmony Postfix in
    /// <see cref="BannerKings.Patches.VanillaModelTweakPatches"/> so vanilla math runs
    /// unchanged and the BK adjustment shows up in the tooltip alongside vanilla factors.
    ///
    /// The class itself is kept (rather than deleted) because
    /// <see cref="BannerKingsConfig.Instance"/>'s CrimeModel field is typed
    /// <see cref="BKCrimeModel"/>. The remaining <see cref="GetMonetaryFine"/> stub is
    /// referenced elsewhere in the BK code, so it stays here as a placeholder until
    /// the full crime/fine system is wired in.
    /// </summary>
    public class BKCrimeModel : DefaultCrimeModel
    {
        public ExplainedNumber GetMonetaryFine(Crime crime, bool explanations = false)
        {
            return new ExplainedNumber(0, explanations);
        }
    }
}
