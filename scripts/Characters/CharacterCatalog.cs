using System.Collections.Generic;
using Godot;

namespace JumpRun.Characters;

/// <summary>Alle spielbaren Charaktere (CONTRACT_V2 §5.4). Reihenfolge verbindlich.</summary>
public static class CharacterCatalog
{
    public const string DefaultId = "superhero";

    private static readonly CharacterDefinition[] Defs =
    {
        new("bottle", "Flasche",
            "Eine flinke PET-Flasche mit Schraubdeckel. Leicht, spritzig und immer gut gelaunt.",
            CharacterStyle.Bottle, 1.74f, 0.55f,
            new Color("8FE6DE"), new Color("FF7AA2"), new Color("E8A317")),
        new("trashcan", "Mülleimer",
            "Die rostfreie Tonne lüftet beim Springen den Deckel – und schaut neugierig durch den Spalt.",
            CharacterStyle.Trashcan, 1.72f, 0.52f,
            new Color("45A866"), new Color("2C7A47"), new Color("E8A317")),
        new("superhero", "Superheld",
            "Maske auf, Umhang im Wind: Der Superheld fliegt am liebsten mit dem Arm voraus.",
            CharacterStyle.Superhero, 1.87f, 0.86f,
            new Color("3D5AFE"), new Color("E8A317"), new Color("F2C8A0")),
        new("knight", "Ritter",
            "Helm mit Federbusch, Schild links, Schwert rechts – jede Kiste zittert vor ihm.",
            CharacterStyle.Knight, 1.96f, 0.84f,
            new Color("C9D1DC"), new Color("2F6FA8"), new Color("E8A317")),
    };

    public static IReadOnlyList<CharacterDefinition> All => Defs;

    /// <summary>Unbekannte/leere Id → Default (Superheld).</summary>
    public static CharacterDefinition Get(string? id)
    {
        int i = IndexOf(id);
        return i >= 0 ? Defs[i] : Defs[IndexOf(DefaultId)];
    }

    public static bool IsValid(string? id) => IndexOf(id) >= 0;

    /// <summary>Index im Katalog oder −1.</summary>
    public static int IndexOf(string? id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        for (int i = 0; i < Defs.Length; i++)
            if (Defs[i].Id == id) return i;
        return -1;
    }
}
