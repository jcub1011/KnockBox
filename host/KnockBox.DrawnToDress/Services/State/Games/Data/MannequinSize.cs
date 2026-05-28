namespace KnockBox.DrawnToDress.Services.State.Games.Data
{
    /// <summary>
    /// Pixel dimensions of the mannequin reference image (width = <see cref="X"/>,
    /// height = <see cref="Y"/>). A record struct rather than a tuple so it survives the
    /// JSON round-trip used to persist <see cref="DrawnToDressSettings"/> to localStorage —
    /// System.Text.Json (Web defaults) does not serialize a ValueTuple's backing fields.
    /// </summary>
    public readonly record struct MannequinSize(int X, int Y);
}
