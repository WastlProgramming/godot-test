using Godot;

namespace JumpRun.Characters;

/// <summary>Visuelle Grundform eines Charakters (steuert Modellbau und Sonderanimationen).</summary>
public enum CharacterStyle
{
    Bottle = 0,
    Trashcan = 1,
    Superhero = 2,
    Knight = 3,
}

/// <summary>Reine Charakterdaten (CONTRACT_V2 §5.4). Rein kosmetisch – Physik ist für alle gleich.</summary>
public sealed class CharacterDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    /// <summary>Modellhöhe Füße → höchster Punkt in Metern (1,6–2,0).</summary>
    public float ModelHeight { get; }
    public Color Primary { get; }
    public Color Secondary { get; }
    public Color Accent { get; }
    public CharacterStyle Style { get; }
    /// <summary>Hüfthöhe (Pivot der Beine) in Metern.</summary>
    public float HipHeight { get; }

    public CharacterDefinition(string id, string displayName, string description, CharacterStyle style,
        float modelHeight, float hipHeight, Color primary, Color secondary, Color accent)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Style = style;
        ModelHeight = modelHeight;
        HipHeight = hipHeight;
        Primary = primary;
        Secondary = secondary;
        Accent = accent;
    }
}
