using MonoMod;

static class patch_RandomTable {
    [MonoModIgnore]
    [PatchRandom]
    public extern static bool TrySelectValue<Ty>(this Ty[] items, out Ty value) where Ty : WeightedItem;
}