namespace Conjugar.Data;

/// <summary>
/// Generates the regular forms of a Spanish verb (-ar: hablar, -er: comer, -ir: vivir), so that every
/// form from Wiktionary can be marked regular or irregular (the source has no such flag). The spelling
/// changes grammars count as regular are applied: c → qu, g → gu, z → c, gu → gü before e (busqué,
/// pagué, empecé, averigüé); c → z, g → j, gu → g before a and o (venzo, cojo, distingo); unstressed i
/// → y between vowels (leyó, leyendo); the written accent on a stressed i or u (envío, actúo, reúno).
/// Stem changes (e → ie, o → ue, e → i, c → zc) are what makes a verb irregular, so they come out red.
/// </summary>
public static class RegularConjugator
{
    public readonly record struct Slot(int Tense, int Person);

    private static readonly string[] Proclitics = ["me ", "te ", "se ", "nos ", "os "];

    /// <summary>"lavarse" → "lavar", "reírse" → "reír".</summary>
    public static (string Base, bool Reflexive) SplitReflexive(string infinitive) =>
        infinitive.Length > 5 && infinitive.EndsWith("se", StringComparison.Ordinal)
            && infinitive[..^2] is var b && (b.EndsWith("ar") || b.EndsWith("er") || b.EndsWith("ir") || b.EndsWith("ír"))
            ? (b, true) : (infinitive, false);

    /// <summary>"me lavo" → "lavo".</summary>
    public static string StripProclitic(string form)
    {
        foreach (var p in Proclitics)
            if (form.StartsWith(p, StringComparison.Ordinal)) return form[p.Length..];
        return form;
    }

    /// <summary>
    /// lávate → lavate. A plain character map: string.Normalize doesn't decompose in the browser
    /// (Blazor WebAssembly ships without the normalization data).
    /// </summary>
    public static string RemoveAccents(string s) => string.Create(s.Length, s, static (span, src) =>
    {
        for (var i = 0; i < src.Length; i++)
            span[i] = src[i] switch
            {
                'á' => 'a', 'é' => 'e', 'í' => 'i', 'ó' => 'o', 'ú' => 'u', 'ü' => 'u',
                var c => c,
            };
    });

    public static Dictionary<Slot, HashSet<string>>? Generate(string infinitive)
    {
        if (infinitive.Length < 3 || !infinitive.All(char.IsLetter)) return null;
        var ending = RemoveAccents(infinitive[^2..]);
        if (ending is not ("ar" or "er" or "ir")) return null;
        var conj = ending;
        var stem = infinitive[..^2];
        var plainInf = stem + ending; // reír → reir for the future

        var map = new Dictionary<Slot, HashSet<string>>();
        void Add(int tense, int person, IEnumerable<string> forms)
        {
            var key = new Slot(tense, person);
            if (!map.TryGetValue(key, out var set)) map[key] = set = new(StringComparer.Ordinal);
            set.UnionWith(forms);
        }

        string[] P(params string[] e) => e;
        var ar = conj == "ar";
        var present = ar ? P("o", "as", "a", "amos", "áis", "an") : conj == "er" ? P("o", "es", "e", "emos", "éis", "en") : P("o", "es", "e", "imos", "ís", "en");
        var subjPresent = ar ? P("e", "es", "e", "emos", "éis", "en") : P("a", "as", "a", "amos", "áis", "an");
        var imperfect = ar ? P("aba", "abas", "aba", "ábamos", "abais", "aban") : P("ía", "ías", "ía", "íamos", "íais", "ían");
        var preterite = ar ? P("é", "aste", "ó", "amos", "asteis", "aron") : P("í", "iste", "ió", "imos", "isteis", "ieron");
        var ra = ar ? P("ara", "aras", "ara", "áramos", "arais", "aran") : P("iera", "ieras", "iera", "iéramos", "ierais", "ieran");
        var se = ar ? P("ase", "ases", "ase", "ásemos", "aseis", "asen") : P("iese", "ieses", "iese", "iésemos", "ieseis", "iesen");
        var subjFuture = ar ? P("are", "ares", "are", "áremos", "areis", "aren") : P("iere", "ieres", "iere", "iéremos", "iereis", "ieren");

        for (var p = 1; p <= 6; p++)
        {
            var stressed = p is 1 or 2 or 3 or 6;
            Add(TenseId.Present, p, J(stem, present[p - 1], conj, stressed));
            Add(TenseId.SubjPresent, p, J(stem, subjPresent[p - 1], conj, stressed));
            Add(TenseId.Imperfect, p, J(stem, imperfect[p - 1], conj, false));
            Add(TenseId.Preterite, p, J(stem, preterite[p - 1], conj, false));
            Add(TenseId.SubjImperfectRa, p, J(stem, ra[p - 1], conj, false));
            Add(TenseId.SubjImperfectSe, p, J(stem, se[p - 1], conj, false));
            Add(TenseId.SubjFuture, p, J(stem, subjFuture[p - 1], conj, false));
            Add(TenseId.Future, p, [plainInf + P("é", "ás", "á", "emos", "éis", "án")[p - 1]]);
            Add(TenseId.Conditional, p, [plainInf + P("ía", "ías", "ía", "íamos", "íais", "ían")[p - 1]]);
        }

        // vos (Argentina, Uruguay, Central America): hablás, comés, vivís; subjunctive hablés, comás.
        Add(TenseId.Present, 2, J(stem, ar ? "ás" : conj == "er" ? "és" : "ís", conj, false));
        Add(TenseId.SubjPresent, 2, J(stem, ar ? "és" : "ás", conj, false));
        Add(TenseId.Imperative, 2, J(stem, ar ? "á" : conj == "er" ? "é" : "í", conj, false));

        Add(TenseId.Imperative, 2, map[new(TenseId.Present, 3)]);
        foreach (var p in new[] { 3, 4, 6 }) Add(TenseId.Imperative, p, map[new(TenseId.SubjPresent, p)]);
        Add(TenseId.Imperative, 5, [plainInf[..^1] + "d"]);
        for (var p = 2; p <= 6; p++) Add(TenseId.NegativeImperative, p, map[new(TenseId.SubjPresent, p)]);

        Add(TenseId.Infinitive, 0, [infinitive]);
        Add(TenseId.Gerund, 0, J(stem, ar ? "ando" : "iendo", conj, false));
        Add(TenseId.PastParticiple, 0, J(stem, ar ? "ado" : "ido", conj, false));
        return map;
    }

    /// <summary>
    /// The forms a reflexive verb has with the pronoun attached (lavándose, lávate, lavémonos, lavaos),
    /// without accents: the attached pronoun moves the written accent.
    /// </summary>
    public static IEnumerable<string> WithEnclitic(int tense, int person, IEnumerable<string> forms) => forms.Select(f =>
        RemoveAccents(tense switch
        {
            TenseId.Infinitive => f,
            TenseId.Gerund => f + "se",
            _ => person switch
            {
                2 => f + "te",
                4 => f[..^1] + "nos",   // lavemos → lavémonos
                5 => f[..^1] + "os",    // lavad → lavaos
                _ => f + "se",
            },
        }));

    /// <summary>Joins stem + ending with Spanish spelling rules.</summary>
    private static IEnumerable<string> J(string stem, string ending, string conj, bool stressed)
    {
        var e0 = ending.Length > 0 ? ending[0] : ' ';
        var s = stem;
        if (conj == "ar" && e0 is 'e' or 'é')
        {
            if (s.EndsWith("gu")) s = s[..^2] + "gü";          // averiguar → averigüe
            else if (s.EndsWith('c')) s = s[..^1] + "qu";      // buscar → busqué
            else if (s.EndsWith('g')) s = s + "u";             // pagar → pagué
            else if (s.EndsWith('z')) s = s[..^1] + "c";       // empezar → empecé
        }
        else if (conj != "ar" && e0 is 'a' or 'o' or 'á')
        {
            if (s.EndsWith("gu")) s = s[..^2] + "g";           // distinguir → distingo
            else if (s.EndsWith("qu")) s = s[..^2] + "c";      // delinquir → delinco
            else if (s.EndsWith('c')) s = s[..^1] + "z";       // vencer → venzo
            else if (s.EndsWith('g')) s = s[..^1] + "j";       // coger → cojo
        }

        var e = ending;
        var last = s.Length > 0 ? s[^1] : ' ';
        var silentU = s.EndsWith("gu") || s.EndsWith("qu");
        if (conj != "ar" && e.Length > 0 && e[0] == 'i')
        {
            if (last is 'ñ' || s.EndsWith("ll"))
            {
                if (e.Length > 1 && e[1] is 'e' or 'é' or 'ó') e = e[1..];   // tañer → tañó, bullir → bullendo, gruñéramos
            }
            else if (last is 'a' or 'e' or 'o' or 'u' && !silentU)
            {
                if (e.Length > 1 && e[1] is 'e' or 'ó' or 'é') e = "y" + e[1..];   // leer → leyó, construir → construyó
                else if (last != 'u') e = "í" + e[1..];                             // leer → leíste, leído
            }
        }
        yield return s + e;

        // A stressed i or u in the stem takes a written accent in some verbs: enviar → envío, reunir → reúno.
        if (stressed)
        {
            var i = s.Length - 1;
            while (i >= 0 && s[i] is not ('a' or 'e' or 'i' or 'o' or 'u')) i--;
            if (i >= 0 && s[i] is 'i' or 'u' && !(s[i] == 'u' && i > 0 && s[i - 1] is 'g' or 'q'))
                yield return s[..i] + (s[i] == 'i' ? "í" : "ú") + s[(i + 1)..] + e;
        }
    }
}
