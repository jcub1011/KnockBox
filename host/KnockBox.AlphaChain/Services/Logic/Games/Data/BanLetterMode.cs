namespace KnockBox.AlphaChain.Services.Logic.Games.Data
{
    /// <summary>
    /// Which class of letters the round's banned-letter mechanic draws from. Consumed
    /// by the chain/word logic starting in M2; defined here so the settings record has
    /// a single source of truth for the option.
    /// </summary>
    public enum BanLetterMode
    {
        Vowels,
        Consonants,
        All
    }
}
