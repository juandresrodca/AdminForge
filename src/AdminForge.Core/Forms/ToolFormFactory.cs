using System.Globalization;
using System.Reflection;

namespace AdminForge.Core.Forms;

/// <summary>
/// Turns an input model type into a <see cref="ToolFormDescriptor"/> by reading its
/// <see cref="ToolFieldAttribute"/>s. Runs once per tool at startup — never per request.
/// </summary>
public static class ToolFormFactory
{
    /// <summary>Build the form descriptor for an input model type.</summary>
    /// <param name="inputType">The model type, or null for a tool with no input model.</param>
    public static ToolFormDescriptor Create(Type? inputType)
    {
        if (inputType is null)
        {
            return ToolFormDescriptor.Empty;
        }

        // A default instance gives us each field's starting value, so the rendered form
        // matches what the handler would see for an untouched submission.
        object? defaults = TryCreateDefaults(inputType);

        var fields = inputType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Property: p, Attribute: p.GetCustomAttribute<ToolFieldAttribute>()))
            .Where(x => x.Attribute is not null && x.Property.CanRead && x.Property.CanWrite)
            .Select((x, declarationIndex) => (x.Property, Attribute: x.Attribute!, declarationIndex))
            .OrderBy(x => x.Attribute.Order)
            .ThenBy(x => x.declarationIndex)
            .Select(x => Describe(x.Property, x.Attribute, defaults))
            .ToList();

        return new ToolFormDescriptor(inputType, fields);
    }

    private static object? TryCreateDefaults(Type inputType)
    {
        if (inputType.GetConstructor(Type.EmptyTypes) is null)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(inputType);
        }
        catch (TargetInvocationException)
        {
            // A model whose constructor throws is a bug in the tool, but it must not
            // take down form generation for every other tool at startup.
            return null;
        }
    }

    private static ToolFieldDescriptor Describe(PropertyInfo property, ToolFieldAttribute attribute, object? defaults)
    {
        Type underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        FieldKind kind = ResolveKind(attribute.Kind, underlying);

        return new ToolFieldDescriptor
        {
            Name = property.Name,
            Label = attribute.Label,
            Kind = kind,
            Placeholder = attribute.Placeholder,
            Help = attribute.Help,
            Required = attribute.Required,
            Options = kind == FieldKind.Select ? ResolveOptions(attribute, underlying) : [],
            MaxLength = attribute.MaxLength,
            Min = attribute.Min,
            Max = attribute.Max,
            Rows = attribute.Rows,
            Half = attribute.Half,
            DefaultValue = ReadDefault(property, defaults, kind),
        };
    }

    private static FieldKind ResolveKind(FieldKind declared, Type underlying)
    {
        if (declared != FieldKind.Auto)
        {
            return declared;
        }

        if (underlying == typeof(bool))
        {
            return FieldKind.Checkbox;
        }

        if (underlying.IsEnum)
        {
            return FieldKind.Select;
        }

        return IsNumeric(underlying) ? FieldKind.Number : FieldKind.Text;
    }

    private static bool IsNumeric(Type type) =>
        Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte
            or TypeCode.Int16 or TypeCode.UInt16
            or TypeCode.Int32 or TypeCode.UInt32
            or TypeCode.Int64 or TypeCode.UInt64
            or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    private static IReadOnlyList<FieldOption> ResolveOptions(ToolFieldAttribute attribute, Type underlying)
    {
        // An explicit list always wins, so an enum-typed field can still be relabelled.
        if (!string.IsNullOrWhiteSpace(attribute.Options))
        {
            return attribute.Options
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseOption)
                .ToList();
        }

        if (underlying.IsEnum)
        {
            return Enum.GetNames(underlying)
                .Select(name => new FieldOption(name, Humanise(name)))
                .ToList();
        }

        return [];
    }

    private static FieldOption ParseOption(string raw)
    {
        int separator = raw.IndexOf('|');
        return separator < 0
            ? new FieldOption(raw, raw)
            : new FieldOption(raw[..separator].Trim(), raw[(separator + 1)..].Trim());
    }

    /// <summary>Turns <c>NoRedirects</c> into <c>No redirects</c> for enum option labels.</summary>
    private static string Humanise(string pascalCase)
    {
        if (pascalCase.Length == 0)
        {
            return pascalCase;
        }

        var builder = new System.Text.StringBuilder(pascalCase.Length + 8);
        builder.Append(pascalCase[0]);

        for (int i = 1; i < pascalCase.Length; i++)
        {
            char current = pascalCase[i];

            // Split on a lower-to-upper boundary, but keep runs of capitals (DNS, TLS) intact.
            bool startsNewWord = char.IsUpper(current)
                && (!char.IsUpper(pascalCase[i - 1])
                    || (i + 1 < pascalCase.Length && char.IsLower(pascalCase[i + 1])));

            if (startsNewWord)
            {
                builder.Append(' ').Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }

    private static string? ReadDefault(PropertyInfo property, object? defaults, FieldKind kind)
    {
        if (defaults is null || kind == FieldKind.Password)
        {
            return null;
        }

        object? value = property.GetValue(defaults);

        return value switch
        {
            null => null,
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }
}
