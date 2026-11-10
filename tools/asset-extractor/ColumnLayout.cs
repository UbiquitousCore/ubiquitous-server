using System.Reflection;

namespace Ubiquitous.AssetExtractor;

// A named column, or a run of them read together, at its place in the
// layout's reference format. Types lists the format letters it may hold.
public sealed record LayoutColumn(string Name, IReadOnlyList<int> Indices, string Types, bool Required)
{
    public static LayoutColumn Must(string name, int index, string types) => new(name, [index], types, true);

    public static LayoutColumn May(string name, int index, string types) => new(name, [index], types, false);

    public static LayoutColumn MustRun(string name, string types, params int[] indices) =>
        new(name, indices, types, true);

    public static LayoutColumn MayRun(string name, string types, params int[] indices) =>
        new(name, indices, types, false);
}

// How another format differs from the reference, in reference column numbers.
public abstract record LayoutEdit
{
    // Reference columns [At, At + Count) are not in this format.
    public sealed record Lacks(int At, int Count = 1) : LayoutEdit;

    // This format carries Count columns of its own ahead of reference column Before.
    public sealed record Adds(int Before, int Count = 1) : LayoutEdit;
}

public sealed record LayoutRevision(string Format, params LayoutEdit[] Edits)
{
    // Columns this format holds differently: placed in its own column
    // numbers, each replacing the reference column of the same name.
    public IReadOnlyList<LayoutColumn> Own { get; init; } = [];

    // Where a reference column sits in this format, or -1 when it is not there.
    public int Locate(int referenceIndex)
    {
        int at = referenceIndex;
        foreach (var edit in Edits)
        {
            switch (edit)
            {
                case LayoutEdit.Lacks lacks when referenceIndex >= lacks.At + lacks.Count:
                    at -= lacks.Count;
                    break;
                case LayoutEdit.Lacks lacks when referenceIndex >= lacks.At:
                    return -1;
                case LayoutEdit.Adds adds when referenceIndex >= adds.Before:
                    at += adds.Count;
                    break;
            }
        }

        return at;
    }
}

public enum LayoutBasis
{
    // The format is one this layout was measured against.
    Known,

    // A known format with further columns after it; the known part is read where it was.
    Extended,

    // No known format fits: columns are read at their reference places where the type still agrees.
    Unrecognised,
}

// One table's columns by name: where each sits in the reference format, and
// what each other known format moved. Columns carry no names in the files,
// so every format is told apart by its type string alone.
public sealed class ColumnLayout
{
    public ColumnLayout(
        string table, string referenceFormat, IReadOnlyList<LayoutColumn> columns,
        params LayoutRevision[] revisions)
    {
        Table = table;
        ReferenceFormat = referenceFormat;
        Columns = columns;
        Revisions = revisions;
    }

    public string Table { get; }

    public string ReferenceFormat { get; }

    public IReadOnlyList<LayoutColumn> Columns { get; }

    public IReadOnlyList<LayoutRevision> Revisions { get; }

    public IEnumerable<string> KnownFormats => Revisions.Select(r => r.Format).Prepend(ReferenceFormat);

    public ResolvedColumns Reference => Resolve(ReferenceFormat);

    public ResolvedColumns Resolve(FTableDocument document) => Resolve(document.Header.ColumnTypes);

    public ResolvedColumns Resolve(string format)
    {
        var (revision, basis) = Match(format);
        var notes = new List<string>();
        if (basis == LayoutBasis.Extended)
        {
            notes.Add($"{Table}: {format.Length - revision.Format.Length} column(s) past the known layout are not read.");
        }
        else if (basis == LayoutBasis.Unrecognised)
        {
            notes.Add($"{Table}: format \"{format}\" matches no known layout; columns are read where the reference has them.");
        }

        var found = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        var own = revision.Own.ToDictionary(c => c.Name, StringComparer.Ordinal);
        foreach (var declared in Columns)
        {
            bool replaced = own.TryGetValue(declared.Name, out var held);
            var column = replaced ? held! : declared;
            var places = replaced ? column.Indices.ToList() : column.Indices.Select(revision.Locate).ToList();
            string? problem = places
                .Select(place => Mismatch(format, place, column.Types))
                .FirstOrDefault(p => p is not null);
            if (problem is null)
            {
                found[column.Name] = places;
                continue;
            }

            if (column.Required)
            {
                throw new FTableFormatException(
                    $"{Table}: required column '{column.Name}' {problem} (format \"{format}\"). " +
                    $"Fingerprint the build and add its layout to {nameof(ColumnLayout)} for this table.");
            }

            found[column.Name] = [];
            notes.Add($"{Table}: column '{column.Name}' {problem}; left unread.");
        }

        return new ResolvedColumns(Table, format, basis, found, notes);
    }

    private (LayoutRevision Revision, LayoutBasis Basis) Match(string format)
    {
        var known = Revisions.Prepend(new LayoutRevision(ReferenceFormat)).ToList();
        if (known.FirstOrDefault(r => r.Format == format) is { } exact) return (exact, LayoutBasis.Known);
        var extended = known
            .Where(r => format.StartsWith(r.Format, StringComparison.Ordinal))
            .MaxBy(r => r.Format.Length);
        return extended is not null
            ? (extended, LayoutBasis.Extended)
            : (known[0], LayoutBasis.Unrecognised);
    }

    private static string? Mismatch(string format, int place, string types)
    {
        if (place < 0) return "is not in this layout";
        if (place >= format.Length) return $"would sit at {place}, past the last column";
        return types.Contains(format[place])
            ? null
            : $"is '{format[place]}' at {place}, not one of \"{types}\"";
    }
}

// Where one table's named columns sit in the format actually read. A column
// that is not there resolves to -1, a run to no columns.
public sealed class ResolvedColumns
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<int>> _columns;

    public ResolvedColumns(
        string table, string format, LayoutBasis basis,
        IReadOnlyDictionary<string, IReadOnlyList<int>> columns, IReadOnlyList<string> notes)
    {
        Table = table;
        Format = format;
        Basis = basis;
        _columns = columns;
        Notes = notes;
    }

    public string Table { get; }

    public string Format { get; }

    public LayoutBasis Basis { get; }

    public IReadOnlyList<string> Notes { get; }

    public int this[string name] => _columns[name] is { Count: 1 } one ? one[0] : -1;

    public IReadOnlyList<int> Run(string name) => _columns[name];

    public bool Has(string name) => _columns[name].Count > 0;

    // The typed view a mapper takes: each constructor parameter of T is filled
    // from the column of the same name. A parameter the layout does not name
    // keeps its default, or reads as absent when it has none.
    public T As<T>()
    {
        var constructor = typeof(T).GetConstructors().Single();
        var arguments = constructor.GetParameters().Select(Bind).ToArray();
        return (T)constructor.Invoke(arguments);
    }

    private object? Bind(ParameterInfo parameter)
    {
        string name = parameter.Name!;
        if (parameter.ParameterType == typeof(int))
        {
            if (_columns.ContainsKey(name)) return this[name];
            return parameter.HasDefaultValue ? parameter.DefaultValue : -1;
        }

        if (parameter.ParameterType.IsAssignableFrom(typeof(IReadOnlyList<int>)))
        {
            if (_columns.TryGetValue(name, out var run)) return run;
            return parameter.HasDefaultValue ? parameter.DefaultValue : Array.Empty<int>();
        }

        if (parameter.ParameterType.IsAssignableFrom(typeof(IReadOnlyList<IReadOnlyList<int>>)))
        {
            var groups = new List<IReadOnlyList<int>>();
            while (_columns.TryGetValue($"{name}[{groups.Count}]", out var group) && group.Count > 0)
            {
                groups.Add(group);
            }

            return groups;
        }

        throw new InvalidOperationException(
            $"{parameter.Member.DeclaringType?.Name}.{name} is a {parameter.ParameterType.Name}, which no column fills.");
    }
}
