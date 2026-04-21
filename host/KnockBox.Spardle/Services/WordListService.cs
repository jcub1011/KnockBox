namespace KnockBox.Spardle.Services;

public class WordListService
{
    private readonly HashSet<string> _nytStandard;
    private readonly HashSet<string> _fullDictionary;

    public WordListService()
    {
        // In a real scenario, this would load massive lists from embedded resources.
        _nytStandard = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "apple", "brave", "crane", "drift", "eager", "flame", "ghost", "haste"
        };

        _fullDictionary = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "apple", "brave", "crane", "drift", "eager", "flame", "ghost", "haste",
            "pneumonoultramicroscopicsilicovolcanoconiosis",
            "busy", "waiting", "busywaiting"
        };
    }

    public bool IsValidWord(string word, Models.WordPoolMode poolMode)
    {
        return poolMode switch
        {
            Models.WordPoolMode.NytStandard => _nytStandard.Contains(word),
            _ => _fullDictionary.Contains(word)
        };
    }
    
    public IReadOnlySet<string> GetFullDictionary() => _fullDictionary;
    public IReadOnlySet<string> GetNytStandard() => _nytStandard;
}
