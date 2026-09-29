namespace AdminForge.Tools.Email.MailAuth;

/// <summary>
/// Reads an SPF record term by term (RFC 7208), rather than searching it as a string —
/// a substring test cannot tell <c>-all</c> from <c>include:spf-all.example</c>, and
/// misses a bare <c>all</c>, which means pass.
/// </summary>
public static class SpfRecord
{
    /// <summary>RFC 7208 section 4.6.4: more DNS-querying terms than this is a permerror.</summary>
    public const int LookupLimit = 10;

    /// <summary>Mechanisms and modifiers that each cost one DNS lookup.</summary>
    private static readonly string[] LookupTerms = ["include", "a", "mx", "ptr", "exists", "redirect"];

    /// <summary>Upper bound on records fetched while following includes, so a looping or fan-out record cannot run away.</summary>
    private const int MaxRecordsFetched = 40;

    /// <summary>True when a TXT string is an SPF record: exactly <c>v=spf1</c>, then a space or the end.</summary>
    /// <param name="txt">The TXT record's text.</param>
    public static bool IsSpf(string? txt) =>
        txt is not null
        && txt.StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase)
        && (txt.Length == 6 || txt[6] == ' ');

    /// <summary>
    /// The qualifier of the <c>all</c> mechanism: '-', '~', '?' or '+'. A bare
    /// <c>all</c> is '+'. Null when the record has no <c>all</c>.
    /// </summary>
    /// <param name="record">The SPF record.</param>
    public static char? AllQualifier(string record)
    {
        foreach ((char qualifier, string name, _) in Terms(record))
        {
            // Evaluation stops at all, so the first one is the one that applies.
            if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return qualifier;
            }
        }

        return null;
    }

    /// <summary>Human description of <see cref="AllQualifier"/>.</summary>
    /// <param name="qualifier">The qualifier, or null for no <c>all</c>.</param>
    public static string Describe(char? qualifier) => qualifier switch
    {
        '-' => "-all (fail)",
        '~' => "~all (softfail)",
        '?' => "?all (neutral)",
        '+' => "+all (pass anything)",
        _ => "none (defaults to neutral)",
    };

    /// <summary>Lookups this record costs on its own, not counting what its includes cost.</summary>
    /// <param name="record">The SPF record.</param>
    public static int CountOwnLookups(string record)
    {
        bool hasAll = AllQualifier(record) is not null;

        return Terms(record).Count(t =>
            LookupTerms.Contains(t.Name, StringComparer.OrdinalIgnoreCase)

            // RFC 7208 section 6.1: redirect is ignored when the record has an all.
            && !(hasAll && t.Name.Equals("redirect", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Domains whose SPF records are evaluated as part of this one: every include, and
    /// the redirect when it applies. Targets built from macros cannot be resolved
    /// without a message to expand them against, so they are skipped.
    /// </summary>
    /// <param name="record">The SPF record.</param>
    public static IReadOnlyList<string> NestedTargets(string record)
    {
        bool hasAll = AllQualifier(record) is not null;
        var targets = new List<string>();

        foreach ((_, string name, string? argument) in Terms(record))
        {
            bool follows = name.Equals("include", StringComparison.OrdinalIgnoreCase)
                || (!hasAll && name.Equals("redirect", StringComparison.OrdinalIgnoreCase));

            if (follows && !string.IsNullOrWhiteSpace(argument) && !argument.Contains('%', StringComparison.Ordinal))
            {
                targets.Add(argument.TrimEnd('.'));
            }
        }

        return targets;
    }

    /// <summary>
    /// Total lookups the record needs, following includes and redirects the way a
    /// receiver does. Stops counting once the limit is passed, since the answer is
    /// already "too many".
    /// </summary>
    /// <param name="record">The SPF record at the domain being checked.</param>
    /// <param name="resolveSpf">Fetches the SPF record published at a domain, or null when there is none.</param>
    /// <param name="cancellationToken">Cancellation for the lookups.</param>
    public static async Task<int> CountLookupsAsync(
        string record,
        Func<string, CancellationToken, Task<string?>> resolveSpf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolveSpf);

        int fetched = 0;
        int total = CountOwnLookups(record);
        var frontier = new List<string>(NestedTargets(record));

        // Breadth-first, one concurrent round of queries per level of nesting.
        while (frontier.Count > 0 && total <= LookupLimit && fetched < MaxRecordsFetched)
        {
            string[] level = frontier.Take(MaxRecordsFetched - fetched).ToArray();
            fetched += level.Length;
            frontier.Clear();

            string?[] records = await Task
                .WhenAll(level.Select(domain => resolveSpf(domain, cancellationToken)))
                .ConfigureAwait(false);

            foreach (string? nested in records)
            {
                if (nested is null)
                {
                    continue;
                }

                total += CountOwnLookups(nested);
                frontier.AddRange(NestedTargets(nested));
            }
        }

        return total;
    }

    /// <summary>Split a record into qualifier, name and argument, skipping the version.</summary>
    private static IEnumerable<(char Qualifier, string Name, string? Argument)> Terms(string record)
    {
        foreach (string term in record.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            char qualifier = term[0] is '+' or '-' or '~' or '?' ? term[0] : '+';
            string body = term[0] is '+' or '-' or '~' or '?' ? term[1..] : term;

            // A name ends at ':' or '=' (argument) or '/' (CIDR length, as in a/24).
            int end = body.IndexOfAny([':', '=', '/']);

            if (end < 0)
            {
                yield return (qualifier, body, null);
            }
            else
            {
                yield return (qualifier, body[..end], body[end] == '/' ? null : body[(end + 1)..]);
            }
        }
    }
}
