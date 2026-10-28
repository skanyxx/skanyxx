using Npgsql;

namespace Skanyxx.Module.Identity.Features.AuditExplorer;

/// <summary>
/// The WHERE clause of an <see cref="AuditFilter"/> over <c>identity_audit a</c>. Every value is a parameter; field names
/// come from <see cref="AuditFields"/>. <see cref="Parameter"/> adds more values for the query around it.
/// </summary>
internal sealed class AuditWhere
{
    private readonly List<(string Name, object Value)> _values = [];
    private readonly List<string> _clauses = [];

    public AuditWhere(AuditFilter filter)
    {
        _clauses.Add($"""a."AtUtc" >= {Parameter(filter.From.ToUniversalTime())} AND a."AtUtc" < {Parameter(filter.To.ToUniversalTime())}""");
        foreach (var chip in filter.Fields)
            _clauses.Add(Matches(chip));
        if (!string.IsNullOrWhiteSpace(filter.Q))
            _clauses.Add(Text(filter.Q.Trim()));
    }

    public string Sql => string.Join(" AND ", _clauses);

    /// <summary>A placeholder for <paramref name="value"/>.</summary>
    public string Parameter(object value)
    {
        var name = "@w" + _values.Count;
        _values.Add((name, value));
        return name;
    }

    /// <summary>New parameter objects for each command: one cannot belong to two.</summary>
    public object[] Parameters() => [.. _values.Select(v => new NpgsqlParameter(v.Name, v.Value))];

    private string Matches(AuditFieldFilter chip)
    {
        if (chip.Field == AuditFields.Role)
        {
            var roles = chip.Value is null
                ? """EXISTS (SELECT 1 FROM identity_user_roles r WHERE r."UserId" = a."ActorId")"""
                : $"""EXISTS (SELECT 1 FROM identity_user_roles r WHERE r."UserId" = a."ActorId" AND r."RoleId" = {Parameter(chip.Value)})""";
            return chip.Exclude == (chip.Value is not null) ? $"NOT {roles}" : roles;
        }
        var column = AuditFields.Column(chip.Field);
        if (chip.Value is null)
            return chip.Exclude ? $"{column} IS NOT NULL" : $"{column} IS NULL";
        // Excluding a value keeps the rows where the field is empty, as Kibana does.
        var value = Parameter(chip.Value);
        return chip.Exclude ? $"({column} IS NULL OR {column} <> {value})" : $"{column} = {value}";
    }

    private string Text(string q)
    {
        var pattern = Parameter("%" + q.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%");
        var emails = $"""SELECT u."Id" FROM identity_users u WHERE u."Email" ILIKE {pattern}""";
        return $"""
            (a."Action" ILIKE {pattern} OR a."ActorId" ILIKE {pattern} OR a."TargetId" ILIKE {pattern} OR a."RemoteIp" ILIKE {pattern}
             OR a."Details"::text ILIKE {pattern} OR a."ActorId" IN ({emails}) OR a."TargetId" IN ({emails}))
            """;
    }
}
