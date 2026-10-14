using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>
/// Checks a request's data annotations and, when they pass, <see cref="IValidatableObject"/>, down through nested
/// objects and collections of them (<c>address.street</c>, <c>items[0].name</c>). Attributes on the parameters of a
/// positional record count as well as those on its properties, since <c>record R([Required] string Name)</c> puts the
/// attribute on the parameter. A reference the code declares non-nullable is required, as in MVC: JSON <c>null</c>, or
/// a missing value, for <c>string Name</c> is an error rather than a null the handler must guard against.
/// </summary>
internal static class RequestValidator
{
    private const int MaxDepth = 32;

    private static readonly ConcurrentDictionary<Type, Member[]> Members = new();

    // Not null, but may be empty: "" is a valid value for a non-nullable string.
    private static readonly RequiredAttribute NotNull = new() { AllowEmptyStrings = true };

    /// <summary>Messages per field, keyed by the field's JSON path; <c>null</c> when the request is valid.</summary>
    public static Dictionary<string, string[]>? Validate(object request)
    {
        var errors = new Dictionary<string, List<string>>();
        Validate(request, "", errors, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        return errors.Count == 0 ? null : errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    private static void Validate(object instance, string prefix, Dictionary<string, List<string>> errors, HashSet<object> visited, int depth)
    {
        if (depth > MaxDepth || !visited.Add(instance))
            return;

        void Add(string field, string? message)
        {
            if (!errors.TryGetValue(field, out var messages))
                errors[field] = messages = [];
            messages.Add(message ?? "The value is not valid.");
        }

        var members = Members.GetOrAdd(instance.GetType(), Find);
        var valid = true;
        var context = new ValidationContext(instance);
        foreach (var member in members)
        {
            var value = member.Property.GetValue(instance);
            context.MemberName = member.Property.Name;
            context.DisplayName = member.Name;
            foreach (var attribute in member.Attributes)
            {
                if (attribute.GetValidationResult(value, context) is { } result && result != ValidationResult.Success)
                {
                    Add(prefix + member.Name, result.ErrorMessage);
                    valid = false;
                }
            }

            if (value is null)
                continue;
            if (value is IEnumerable items and not string && member.Property.PropertyType != typeof(string))
            {
                var index = 0;
                foreach (var item in items)
                {
                    if (item is not null && IsNested(item.GetType()))
                        Validate(item, $"{prefix}{member.Name}[{index}].", errors, visited, depth + 1);
                    index++;
                }
            }
            else if (IsNested(value.GetType()))
            {
                Validate(value, $"{prefix}{member.Name}.", errors, visited, depth + 1);
            }
        }

        // As Validator does: the object's own rules run once its fields are valid.
        if (valid && instance is IValidatableObject validatable)
        {
            foreach (var result in validatable.Validate(new ValidationContext(instance)))
            {
                var fields = result.MemberNames.ToArray();
                if (fields.Length == 0)
                    Add(prefix.TrimEnd('.'), result.ErrorMessage);
                foreach (var field in fields)
                    Add(prefix + (members.FirstOrDefault(m => m.Property.Name == field)?.Name ?? JsonName(field)), result.ErrorMessage);
            }
        }
    }

    // An object of the app's own types, to check inside; not a value, text, framework type or uploaded file.
    private static bool IsNested(Type type) =>
        type is { IsPrimitive: false, IsEnum: false, IsPointer: false }
        && type != typeof(string) && type != typeof(decimal)
        && !typeof(IFormFile).IsAssignableFrom(type) && !typeof(Stream).IsAssignableFrom(type)
        && type.Namespace is not null && !type.Namespace.StartsWith("System") && !type.Namespace.StartsWith("Microsoft");

    private static Member[] Find(Type type)
    {
        var parameters = type.GetConstructors().SelectMany(c => c.GetParameters()).ToArray();
        var nullability = new NullabilityInfoContext();
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(property =>
            {
                var attributes = property.GetCustomAttributes<ValidationAttribute>(inherit: true).ToList();
                var fromParameters = parameters
                    .Where(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase) && p.ParameterType == property.PropertyType)
                    .SelectMany(p => p.GetCustomAttributes<ValidationAttribute>())
                    .Where(a => attributes.All(existing => existing.GetType() != a.GetType()))
                    .GroupBy(a => a.GetType()).Select(g => g.First());
                attributes.AddRange(fromParameters);
                if (!property.PropertyType.IsValueType && !attributes.OfType<RequiredAttribute>().Any()
                    && nullability.Create(property).ReadState == NullabilityState.NotNull)
                    attributes.Insert(0, NotNull);
                var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonName(property.Name);
                return new Member(property, name, attributes.ToArray());
            })
            .ToArray();
    }

    /// <summary><paramref name="errors"/> keyed by the JSON names of their fields, e.g. from <see cref="RequestException.Errors"/>.</summary>
    public static Dictionary<string, string[]> ByJsonName(IReadOnlyDictionary<string, string[]> errors) =>
        errors.ToDictionary(e => JsonName(e.Key), e => e.Value);

    /// <summary>The name a property has in JSON, as Reiho reads and writes it.</summary>
    public static string JsonName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonName(property.Name);

    private static string JsonName(string name) => ReihoJson.Options.PropertyNamingPolicy?.ConvertName(name) ?? name;

    private sealed record Member(PropertyInfo Property, string Name, ValidationAttribute[] Attributes);
}
