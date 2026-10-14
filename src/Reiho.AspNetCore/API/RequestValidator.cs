using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>
/// Checks a request's data annotations and, when they pass, <see cref="IValidatableObject"/>. Attributes on the
/// parameters of a positional record count as well as those on its properties, since <c>record R([Required] string Name)</c>
/// puts the attribute on the parameter. Only the request's own properties are checked, not nested objects.
/// </summary>
internal static class RequestValidator
{
    private static readonly ConcurrentDictionary<Type, Member[]> Members = new();

    /// <summary>Messages per field, keyed by the field's JSON name; <c>null</c> when the request is valid.</summary>
    public static Dictionary<string, string[]>? Validate(object request)
    {
        var members = Members.GetOrAdd(request.GetType(), Find);
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string? message)
        {
            if (!errors.TryGetValue(field, out var messages))
                errors[field] = messages = [];
            messages.Add(message ?? "The value is not valid.");
        }

        var context = new ValidationContext(request);
        foreach (var member in members)
        {
            if (member.Attributes.Length == 0)
                continue;
            var value = member.Property.GetValue(request);
            context.MemberName = member.Property.Name;
            context.DisplayName = member.Name;
            foreach (var attribute in member.Attributes)
            {
                if (attribute.GetValidationResult(value, context) is { } result && result != ValidationResult.Success)
                    Add(member.Name, result.ErrorMessage);
            }
        }

        // As Validator does: the object's own rules run once its fields are valid.
        if (errors.Count == 0 && request is IValidatableObject validatable)
        {
            foreach (var result in validatable.Validate(new ValidationContext(request)))
            {
                var fields = result.MemberNames.ToArray();
                if (fields.Length == 0)
                    Add("", result.ErrorMessage);
                foreach (var field in fields)
                    Add(members.FirstOrDefault(m => m.Property.Name == field)?.Name ?? JsonName(field), result.ErrorMessage);
            }
        }

        return errors.Count == 0 ? null : errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    private static Member[] Find(Type type)
    {
        var parameters = type.GetConstructors().SelectMany(c => c.GetParameters()).ToArray();
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
                var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonName(property.Name);
                return new Member(property, name, attributes.ToArray());
            })
            .ToArray();
    }

    // The naming of the web defaults Reiho serializes with.
    private static string JsonName(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private sealed record Member(PropertyInfo Property, string Name, ValidationAttribute[] Attributes);
}
