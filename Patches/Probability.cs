using System.Collections.Generic;
using MonoMod;

static class patch_Probability {
    [MonoModIgnore]
    [PatchRandom]
    public extern static Probability.ProbabilityBase<TVal> GetRandomItemRootByProbability<TProb, TVal>(IReadOnlyList<TProb> array, out int chosenIndex, float[] overrideProbabilities = null, IReadOnlyList<bool> conditions = null)
    where TProb : Probability.ProbabilityBase<TVal>;
}