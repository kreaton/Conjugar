namespace Conjugar.Data;

public sealed record VerbSummary(string Infinitive, bool HasConjugation, bool? IsIrregular);

public sealed record FormHit(string Infinitive, string TenseName, int Person);

public sealed record VerbForm(int TenseId, int Person, string Value, string? Note, bool? Irregular)
{
    /// <summary>A voseo form (hablás) or one the source labels with a region (Argentina, Spain).</summary>
    public bool Regional => Note is { } n && (n.Contains("vos") || n.Contains("argentina") || n.Contains("uruguay")
        || n.Contains("spain") || n.Contains("latin-america") || n.Contains("chile") || n.Contains("mexico"));
}

public sealed class VerbDetail
{
    public int Id { get; init; }
    public string Infinitive { get; init; } = "";
    /// <summary>KaikkiVerbs' class: the ending and Wiktionary's stem and spelling changes ("ar, e-ie").</summary>
    public string? ConjugationClass { get; init; }
    public bool Reflexive { get; init; }
    public bool HasConjugation { get; init; }
    public bool? IsIrregular { get; init; }
    public int? IrregularForms { get; init; }
    /// <summary>lavarse → lavar, when that verb is in the dictionary.</summary>
    public string? BaseInfinitive { get; init; }
    public string? LemmaInfinitive { get; init; }
    public string? Etymology { get; init; }
    public string? Url { get; init; }
    public List<string> Definitions { get; } = [];
    public List<string> Pronunciations { get; } = [];
    public List<VerbForm> Forms { get; } = [];

    /// <summary>A verb with no stem change where most forms miss the model: the Wiktionary table is most likely broken.</summary>
    public bool LooksUnreliable => IrregularForms >= 30 && ConjugationClass is "ar" or "er" or "ir";
}

/// <summary>The KaikkiVerbs tense ids of the Spanish database.</summary>
public static class TenseId
{
    public const int Infinitive = 1, Gerund = 2, PastParticiple = 4,
        Present = 10, Imperfect = 11, Preterite = 12, Future = 13, Conditional = 20,
        SubjPresent = 30, SubjImperfectRa = 31, SubjImperfectSe = 32, SubjFuture = 33,
        Imperative = 40, NegativeImperative = 41;
}
