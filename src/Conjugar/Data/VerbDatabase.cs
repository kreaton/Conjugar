using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace Conjugar.Data;

/// <summary>
/// Read-only access to verbs.sqlite, the Spanish database built by KaikkiVerbs from Wiktionary
/// (kaikki.org). The gzipped file is downloaded once per page load, inflated into the in-memory
/// Emscripten file system and opened with Microsoft.Data.Sqlite. Blazor WebAssembly is
/// single-threaded, so one shared connection is enough.
/// </summary>
/// <remarks>
/// KaikkiVerbs stores what Wiktionary has, in a layout shared by all its languages. The Spanish
/// specifics are worked out here: reflexive verbs (lavarse), whose forms carry the pronoun (me lavo,
/// lávate), and the regular/irregular marking from <see cref="RegularConjugator"/>.
/// </remarks>
public sealed class VerbDatabase(HttpClient http)
{
    private const string DataUrl = "data/verbs.sqlite.gz";
    private const string LocalPath = "/verbs.sqlite";

    public static readonly IReadOnlyDictionary<int, string> TenseNames = new Dictionary<int, string>
    {
        [TenseId.Infinitive] = "infinitivo", [TenseId.Gerund] = "gerundio", [TenseId.PastParticiple] = "participio",
        [TenseId.Present] = "presente", [TenseId.Imperfect] = "pretérito imperfecto", [TenseId.Preterite] = "pretérito perfecto simple",
        [TenseId.Future] = "futuro", [TenseId.Conditional] = "condicional", [TenseId.SubjPresent] = "presente de subjuntivo",
        [TenseId.SubjImperfectRa] = "imperfecto de subjuntivo", [TenseId.SubjImperfectSe] = "imperfecto de subjuntivo (-se)",
        [TenseId.SubjFuture] = "futuro de subjuntivo", [TenseId.Imperative] = "imperativo", [TenseId.NegativeImperative] = "imperativo negativo",
    };

    private Task? _loading;
    private SqliteConnection? _db;
    private Dictionary<(string Aux, int Tense, int Person), string>? _auxForms;
    private readonly Dictionary<int, Analysis> _analyses = [];

    public int VerbCount { get; private set; }

    public Task EnsureLoadedAsync() => _loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        var bytes = await http.GetByteArrayAsync(DataUrl);

        // GitHub Pages serves the .gz as-is; a dev server may already have decoded it.
        var isGzip = bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b;
        await using (var file = File.Create(LocalPath))
        {
            if (isGzip)
            {
                await using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
                await gz.CopyToAsync(file);
            }
            else
            {
                await file.WriteAsync(bytes);
            }
        }

        _db = new SqliteConnection($"Data Source={LocalPath};Mode=ReadOnly;Pooling=False");
        _db.Open();
        VerbCount = Scalar<long>("SELECT count(*) FROM verb WHERE has_conjugation = 1") is var n ? (int)n : 0;
    }

    private SqliteConnection Db => _db ?? throw new InvalidOperationException("Database not loaded; call EnsureLoadedAsync first.");

    public List<VerbSummary> SearchInfinitives(string prefix, int limit = 12)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = """
            SELECT id, infinitive, has_conjugation FROM verb
            WHERE infinitive LIKE $p || '%' ESCAPE '\'
            ORDER BY infinitive = $exact DESC, has_conjugation DESC, length(infinitive), infinitive
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$p", EscapeLike(prefix));
        cmd.Parameters.AddWithValue("$exact", prefix);
        cmd.Parameters.AddWithValue("$limit", limit);
        var rows = new List<(int Id, string Infinitive, bool HasConjugation)>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) rows.Add((r.GetInt32(0), r.GetString(1), r.GetBoolean(2)));
        return rows.Select(x => new VerbSummary(x.Infinitive, x.HasConjugation,
            x.HasConjugation ? Analyze(x.Id, x.Infinitive).IsIrregular : null)).ToList();
    }

    /// <summary>Reverse lookup: which verbs have this exact conjugated form ("tengo" → tener). A full scan.</summary>
    public List<FormHit> FindForm(string value, int limit = 8)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = """
            SELECT v.infinitive, f.tense_id, f.person
            FROM form f JOIN verb v ON v.id = f.verb_id
            WHERE f.value IN ($v, 'me ' || $v, 'te ' || $v, 'se ' || $v, 'nos ' || $v, 'os ' || $v)
              AND f.tense_id NOT IN (1, 41)
            GROUP BY v.id, f.tense_id, f.person
            -- Rare and old spellings have few definitions, so the verb with more comes first.
            ORDER BY v.reflexive, min(f.variant), (SELECT count(*) FROM definition d WHERE d.verb_id = v.id) DESC,
                     v.infinitive, f.tense_id, f.person
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<FormHit>();
        while (r.Read())
            if (TenseNames.TryGetValue(r.GetInt32(1), out var name))
                list.Add(new(r.GetString(0), name, r.GetInt32(2)));
        return list;
    }

    public string? RandomInfinitive()
    {
        // Skip tables that are probably broken in the source (almost nothing matches the regular model).
        for (var attempt = 0; attempt < 20; attempt++)
        {
            int id;
            string infinitive;
            string? cls;
            using (var cmd = Db.CreateCommand())
            {
                cmd.CommandText = "SELECT id, infinitive, conjugation_class FROM verb WHERE has_conjugation = 1 ORDER BY random() LIMIT 1";
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                (id, infinitive, cls) = (r.GetInt32(0), r.GetString(1), Str(r, 2));
            }
            if ((Analyze(id, infinitive).IrregularForms ?? 0) < 30 || cls is not ("ar" or "er" or "ir")) return infinitive;
        }
        return null;
    }

    public VerbDetail? GetVerb(string infinitive)
    {
        int id;
        string? etymology, url, lemma, cls;
        bool hasConjugation, reflexive;
        using (var cmd = Db.CreateCommand())
        {
            cmd.CommandText = """
                SELECT v.id, v.infinitive, v.has_conjugation, l.infinitive, v.etymology, v.url,
                       v.conjugation_class, v.reflexive
                FROM verb v LEFT JOIN verb l ON l.id = v.lemma_id
                WHERE v.infinitive = $inf
                """;
            cmd.Parameters.AddWithValue("$inf", infinitive);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            id = r.GetInt32(0);
            infinitive = r.GetString(1);
            hasConjugation = r.GetBoolean(2);
            lemma = Str(r, 3);
            etymology = Str(r, 4);
            url = Str(r, 5);
            cls = Str(r, 6);
            reflexive = r.GetBoolean(7);
        }

        var baseInfinitive = RegularConjugator.SplitReflexive(infinitive).Base;
        var analysis = hasConjugation ? Analyze(id, infinitive) : null;
        var verb = new VerbDetail
        {
            Id = id,
            Infinitive = infinitive,
            ConjugationClass = cls,
            Reflexive = reflexive,
            HasConjugation = hasConjugation,
            IsIrregular = analysis?.IsIrregular,
            IrregularForms = analysis?.IrregularForms,
            BaseInfinitive = reflexive && baseInfinitive != infinitive && Exists(baseInfinitive) ? baseInfinitive : null,
            LemmaInfinitive = lemma,
            Etymology = etymology,
            Url = url,
        };
        if (analysis is not null) verb.Forms.AddRange(analysis.Forms);

        ReadTexts("SELECT text FROM definition WHERE verb_id = $id ORDER BY seq", id, verb.Definitions);
        ReadTexts("SELECT ipa FROM pronunciation WHERE verb_id = $id ORDER BY seq", id, verb.Pronunciations);
        return verb;
    }

    /// <summary>Primary forms of haber, used to build the compound tenses.</summary>
    public IReadOnlyDictionary<(string Aux, int Tense, int Person), string> AuxiliaryForms()
    {
        if (_auxForms is not null) return _auxForms;
        _auxForms = [];
        using var cmd = Db.CreateCommand();
        cmd.CommandText = """
            SELECT v.infinitive, f.tense_id, f.person, f.value FROM form f JOIN verb v ON v.id = f.verb_id
            WHERE v.infinitive = 'haber' AND f.variant = 0
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            _auxForms[(r.GetString(0), r.GetInt32(1), r.GetInt32(2))] = r.GetString(3);
        return _auxForms;
    }

    private sealed record Analysis(List<VerbForm> Forms, bool? IsIrregular, int? IrregularForms);

    /// <summary>A verb's forms, each compared with the regular forms (cached per verb).</summary>
    private Analysis Analyze(int id, string infinitive)
    {
        if (_analyses.TryGetValue(id, out var cached)) return cached;

        var (baseInf, reflexive) = RegularConjugator.SplitReflexive(infinitive);
        var model = RegularConjugator.Generate(baseInf);
        var forms = new List<VerbForm>();
        var irregularPrimary = 0;
        using (var cmd = Db.CreateCommand())
        {
            cmd.CommandText = "SELECT tense_id, person, variant, value, note FROM form WHERE verb_id = $id ORDER BY tense_id, person, variant";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var (tense, person, variant, value, note) = (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), Str(r, 4));
                if (!TenseNames.ContainsKey(tense)) continue;
                bool? irregular = null;
                if (model is not null && model.TryGetValue(new(tense, person), out var expected))
                {
                    // lavarse: the pronoun comes first (me lavo, no te laves) or is attached (lavándose, lávate).
                    if (!reflexive) irregular = !expected.Contains(value);
                    else if (tense is TenseId.Infinitive) irregular = value != infinitive;
                    else if (tense is TenseId.Gerund or TenseId.Imperative)
                        irregular = !RegularConjugator.WithEnclitic(tense, person, expected).Contains(RegularConjugator.RemoveAccents(value));
                    else irregular = !expected.Contains(RegularConjugator.StripProclitic(value));
                }
                if (variant == 0 && irregular == true && tense != TenseId.Infinitive) irregularPrimary++;
                forms.Add(new VerbForm(tense, person, value, note, irregular));
            }
        }
        return _analyses[id] = new Analysis(forms, model is null ? null : irregularPrimary > 0, model is null ? null : irregularPrimary);
    }

    private bool Exists(string infinitive)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM verb WHERE infinitive = $w";
        cmd.Parameters.AddWithValue("$w", infinitive);
        return cmd.ExecuteScalar() is not null;
    }

    private void ReadTexts(string sql, int verbId, List<string> target)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", verbId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) target.Add(r.GetString(0));
    }

    private T? Scalar<T>(string sql)
    {
        using var cmd = Db.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() is T t ? t : default;
    }

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
