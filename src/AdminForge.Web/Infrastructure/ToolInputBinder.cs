using System.Globalization;
using System.Reflection;
using AdminForge.Core.Forms;
using AdminForge.Core.Registry;

namespace AdminForge.Web.Infrastructure;

/// <summary>The outcome of binding a posted form to a tool's input model.</summary>
/// <param name="Model">The populated model, or null when binding failed.</param>
/// <param name="Errors">Field name to error message, for fields that failed validation.</param>
/// <param name="Submitted">The raw submitted values, so the form can be re-rendered as the user left it.</param>
public sealed record ToolInputBinding(
    object? Model,
    IReadOnlyDictionary<string, string> Errors,
    IReadOnlyDictionary<string, string> Submitted)
{
    /// <summary>True when the model is populated and every field validated.</summary>
    public bool IsValid => Model is not null && Errors.Count == 0;
}

/// <summary>
/// Binds and validates a posted form against a tool's generated
/// <see cref="ToolFormDescriptor"/>.
/// <para>
/// The descriptor is the single source of truth for both the rendered form and its
/// validation, so a tool cannot drift into accepting something the UI never offered.
/// </para>
/// </summary>
public sealed class ToolInputBinder
{
    /// <summary>Bind a form collection to the tool's input model.</summary>
    /// <param name="tool">The tool being executed.</param>
    /// <param name="form">The posted form values.</param>
    public ToolInputBinding Bind(ToolDescriptor tool, IFormCollection form)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(form);

        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        var submitted = new Dictionary<string, string>(StringComparer.Ordinal);

        if (tool.InputType is null)
        {
            return new ToolInputBinding(null, errors, submitted);
        }

        object model = Activator.CreateInstance(tool.InputType)!;

        foreach (ToolFieldDescriptor field in tool.Form.Fields)
        {
            PropertyInfo? property = tool.InputType.GetProperty(field.Name);

            if (property is null || !property.CanWrite)
            {
                continue;
            }

            string raw = ReadRaw(form, field);

            // A password field is never echoed back into the re-rendered form.
            if (field.Kind != FieldKind.Password)
            {
                submitted[field.Name] = raw;
            }

            if (field.Required && string.IsNullOrWhiteSpace(raw))
            {
                errors[field.Name] = $"{field.Label} is required.";
                continue;
            }

            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            if (field.MaxLength > 0 && raw.Length > field.MaxLength)
            {
                errors[field.Name] = $"{field.Label} must be {field.MaxLength:N0} characters or fewer.";
                continue;
            }

            if (field.Kind == FieldKind.Select
                && field.Options.Count > 0
                && !field.Options.Any(o => string.Equals(o.Value, raw, StringComparison.OrdinalIgnoreCase)))
            {
                errors[field.Name] = $"{raw} is not a valid choice for {field.Label}.";
                continue;
            }

            if (!TryConvert(raw, property.PropertyType, out object? value))
            {
                errors[field.Name] = $"{field.Label} is not in the expected format.";
                continue;
            }

            if (!TryValidateRange(field, value, out string? rangeError))
            {
                errors[field.Name] = rangeError!;
                continue;
            }

            property.SetValue(model, value);
        }

        return new ToolInputBinding(model, errors, submitted);
    }

    private static string ReadRaw(IFormCollection form, ToolFieldDescriptor field)
    {
        if (field.Kind != FieldKind.Checkbox)
        {
            return form[field.Name].ToString().Trim();
        }

        // An unchecked box posts nothing at all, so absence is the value.
        string value = form[field.Name].ToString();

        return value.Contains("true", StringComparison.OrdinalIgnoreCase) || value == "on"
            ? "true"
            : "false";
    }

    private static bool TryConvert(string raw, Type targetType, out object? value)
    {
        Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        value = null;

        try
        {
            if (underlying == typeof(string))
            {
                value = raw;
                return true;
            }

            if (underlying.IsEnum)
            {
                if (!Enum.TryParse(underlying, raw, ignoreCase: true, out object? parsed))
                {
                    return false;
                }

                value = parsed;
                return true;
            }

            if (underlying == typeof(bool))
            {
                value = raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "on";
                return true;
            }

            value = Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            return false;
        }
    }

    private static bool TryValidateRange(ToolFieldDescriptor field, object? value, out string? error)
    {
        error = null;

        if (value is not IConvertible convertible
            || (double.IsNaN(field.Min) && double.IsNaN(field.Max))
            || value is string or bool)
        {
            return true;
        }

        double number;

        try
        {
            number = convertible.ToDouble(CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return true;
        }

        if (!double.IsNaN(field.Min) && number < field.Min)
        {
            error = $"{field.Label} must be at least {field.Min:G}.";
            return false;
        }

        if (!double.IsNaN(field.Max) && number > field.Max)
        {
            error = $"{field.Label} must be at most {field.Max:G}.";
            return false;
        }

        return true;
    }
}
