namespace Conjugar.Data;

/// <summary>One displayed piece of text; irregular pieces are highlighted.</summary>
public sealed record Piece(string Text, bool Irregular);

public sealed class Row
{
    public string Pronoun { get; init; } = "";
    public List<Piece> Primary { get; } = [];
    public List<VerbForm> Variants { get; } = [];
}

public sealed class TenseCard
{
    /// <summary>The anchor in the tense guide: "indicativo-preterito-perfecto".</summary>
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string English { get; init; } = "";
    public bool Compound { get; init; }
    public List<Row> Rows { get; } = [];
    public string? Footnote { get; set; }
}

public sealed class MoodSection
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<TenseCard> Cards { get; } = [];
}

public sealed class NonFinite
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool Compound { get; init; }
    public List<Piece> Primary { get; } = [];
    public List<VerbForm> Variants { get; } = [];
}

public sealed class ConjugationView
{
    public List<NonFinite> NonFinite { get; } = [];
    public List<MoodSection> Moods { get; } = [];
}

/// <summary>
/// Arranges the stored simple tenses into tables and builds the compound ones (not in the source
/// data) from haber and the past participle, with the reflexive pronoun in front (me he lavado).
/// </summary>
public static class ConjugationBuilder
{
    private static readonly string[] Plain = ["", "yo", "tú", "él/ella/usted", "nosotros", "vosotros", "ellos/ellas/ustedes"];
    private static readonly string[] Imper = ["", "", "(tú)", "(usted)", "(nosotros)", "(vosotros)", "(ustedes)"];
    private static readonly string[] Clitic = ["", "me", "te", "se", "nos", "os", "se"];

    public static ConjugationView Build(VerbDetail verb, IReadOnlyDictionary<(string Aux, int Tense, int Person), string> auxForms)
    {
        var view = new ConjugationView();
        var cells = verb.Forms.GroupBy(f => (f.TenseId, f.Person)).ToDictionary(g => g.Key, g => g.ToList());
        List<VerbForm> Cell(int tense, int person) => cells.GetValueOrDefault((tense, person)) ?? [];

        var participle = Cell(TenseId.PastParticiple, 0).FirstOrDefault();
        var ppIrregular = participle?.Irregular == true;

        // Non-finite forms.
        view.NonFinite.Add(Simple("infinitivo", "infinitivo", Cell(TenseId.Infinitive, 0)));
        if (participle is not null)
            view.NonFinite.Add(Compound("infinitivo-compuesto", "infinitivo compuesto", verb.Reflexive ? "haberse" : "haber", participle.Value, ppIrregular));
        view.NonFinite.Add(Simple("gerundio", "gerundio", Cell(TenseId.Gerund, 0)));
        if (participle is not null)
            view.NonFinite.Add(Compound("gerundio-compuesto", "gerundio compuesto", verb.Reflexive ? "habiéndose" : "habiendo", participle.Value, ppIrregular));
        view.NonFinite.Add(Simple("participio", "participio", Cell(TenseId.PastParticiple, 0)));
        view.NonFinite.RemoveAll(n => n.Primary.Count == 0);

        TenseCard SimpleCard(string id, int tense, string name, string english, string[] pronouns, string prefix = "")
        {
            var card = new TenseCard { Id = id, Name = name, English = english };
            for (var p = 1; p <= 6; p++)
            {
                if (tense is TenseId.Imperative or TenseId.NegativeImperative && p == 1) continue;
                var forms = Cell(tense, p);
                var row = new Row { Pronoun = pronouns[p] };
                if (forms.Count > 0)
                {
                    if (prefix.Length > 0) row.Primary.Add(new(prefix, false));
                    row.Primary.Add(new(forms[0].Value, forms[0].Irregular == true));
                    row.Variants.AddRange(forms.Skip(1));
                }
                card.Rows.Add(row);
            }
            return card;
        }

        TenseCard? CompoundCard(string id, int auxTense, string name, string english, string[] pronouns)
        {
            if (participle is null) return null;
            var card = new TenseCard { Id = id, Name = name, English = english, Compound = true };
            for (var p = 1; p <= 6; p++)
            {
                var row = new Row { Pronoun = pronouns[p] };
                // Defective verbs (llover: only llueve) get compounds only where the simple tense exists.
                if (Cell(TenseId.Present, p).Count > 0 && auxForms.TryGetValue(("haber", auxTense, p), out var a))
                {
                    row.Primary.Add(new((verb.Reflexive ? Clitic[p] + " " : "") + a + " ", false));
                    row.Primary.Add(new(participle.Value, ppIrregular));
                }
                card.Rows.Add(row);
            }
            return card.Rows.Any(r => r.Primary.Count > 0) ? card : null;
        }

        var indicative = new MoodSection { Id = "indicativo", Name = "Indicativo" };
        indicative.Cards.Add(SimpleCard("indicativo-presente", TenseId.Present, "presente", "present", Plain));
        indicative.Cards.AddIfNotNull(CompoundCard("indicativo-preterito-perfecto", TenseId.Present, "pretérito perfecto", "present perfect", Plain));
        indicative.Cards.Add(SimpleCard("indicativo-preterito-indefinido", TenseId.Preterite, "pretérito indefinido", "preterite", Plain));
        indicative.Cards.AddIfNotNull(CompoundCard("indicativo-preterito-anterior", TenseId.Preterite, "pretérito anterior", "past anterior", Plain));
        indicative.Cards.Add(SimpleCard("indicativo-preterito-imperfecto", TenseId.Imperfect, "pretérito imperfecto", "imperfect", Plain));
        indicative.Cards.AddIfNotNull(CompoundCard("indicativo-pluscuamperfecto", TenseId.Imperfect, "pluscuamperfecto", "pluperfect", Plain));
        indicative.Cards.Add(SimpleCard("indicativo-futuro", TenseId.Future, "futuro", "future", Plain));
        indicative.Cards.AddIfNotNull(CompoundCard("indicativo-futuro-perfecto", TenseId.Future, "futuro perfecto", "future perfect", Plain));

        var subjunctive = new MoodSection { Id = "subjuntivo", Name = "Subjuntivo" };
        subjunctive.Cards.Add(SimpleCard("subjuntivo-presente", TenseId.SubjPresent, "presente", "present", Plain));
        subjunctive.Cards.AddIfNotNull(CompoundCard("subjuntivo-preterito-perfecto", TenseId.SubjPresent, "pretérito perfecto", "present perfect", Plain));
        subjunctive.Cards.Add(SimpleCard("subjuntivo-imperfecto", TenseId.SubjImperfectRa, "pretérito imperfecto (-ra)", "imperfect", Plain));
        subjunctive.Cards.AddIfNotNull(CompoundCard("subjuntivo-pluscuamperfecto", TenseId.SubjImperfectRa, "pluscuamperfecto (-ra)", "pluperfect", Plain));
        subjunctive.Cards.Add(SimpleCard("subjuntivo-imperfecto-se", TenseId.SubjImperfectSe, "pretérito imperfecto (-se)", "imperfect", Plain));
        subjunctive.Cards.AddIfNotNull(CompoundCard("subjuntivo-pluscuamperfecto-se", TenseId.SubjImperfectSe, "pluscuamperfecto (-se)", "pluperfect", Plain));
        subjunctive.Cards.Add(SimpleCard("subjuntivo-futuro", TenseId.SubjFuture, "futuro", "future", Plain));
        subjunctive.Cards.AddIfNotNull(CompoundCard("subjuntivo-futuro-perfecto", TenseId.SubjFuture, "futuro perfecto", "future perfect", Plain));

        var conditional = new MoodSection { Id = "condicional", Name = "Condicional" };
        conditional.Cards.Add(SimpleCard("condicional-simple", TenseId.Conditional, "condicional simple", "conditional", Plain));
        conditional.Cards.AddIfNotNull(CompoundCard("condicional-compuesto", TenseId.Conditional, "condicional compuesto", "conditional perfect", Plain));

        var imperative = new MoodSection { Id = "imperativo", Name = "Imperativo" };
        imperative.Cards.Add(SimpleCard("imperativo-afirmativo", TenseId.Imperative, "afirmativo", "affirmative", Imper));
        imperative.Cards.Add(SimpleCard("imperativo-negativo", TenseId.NegativeImperative, "negativo", "negative", Imper, "no "));

        view.Moods.AddRange([indicative, subjunctive, conditional, imperative]);
        foreach (var mood in view.Moods)
            mood.Cards.RemoveAll(c => c.Rows.All(r => r.Primary.Count == 0));
        view.Moods.RemoveAll(m => m.Cards.Count == 0);
        return view;
    }

    private static NonFinite Simple(string id, string name, List<VerbForm> forms)
    {
        var n = new NonFinite { Id = id, Name = name };
        if (forms.Count > 0)
        {
            n.Primary.Add(new(forms[0].Value, forms[0].Irregular == true));
            n.Variants.AddRange(forms.Skip(1));
        }
        return n;
    }

    private static NonFinite Compound(string id, string name, string aux, string pp, bool irregular)
    {
        var n = new NonFinite { Id = id, Name = name, Compound = true };
        n.Primary.Add(new(aux + " ", false));
        n.Primary.Add(new(pp, irregular));
        return n;
    }

    private static void AddIfNotNull<T>(this List<T> list, T? item) where T : class
    {
        if (item is not null) list.Add(item);
    }
}
