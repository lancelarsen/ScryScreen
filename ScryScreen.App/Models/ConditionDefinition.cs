using System;

namespace ScryScreen.App.Models;

public sealed record ConditionDefinition(
    Guid Id,
    string Name,
    string ShortTag,
    string ColorHex,
    bool IsBuiltIn,
    bool IsManualOnly)
{
    public bool IsCustom => !IsBuiltIn;

    public string DisplayName => IsBuiltIn ? Name : $"*{Name}";

    public ConditionDefinition ValidateOrThrow()
    {
        if (Id == Guid.Empty)
        {
            throw new ArgumentException("ConditionDefinition.Id cannot be empty", nameof(Id));
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("ConditionDefinition.Name cannot be blank", nameof(Name));
        }

        if (string.IsNullOrWhiteSpace(ShortTag))
        {
            throw new ArgumentException("ConditionDefinition.ShortTag cannot be blank", nameof(ShortTag));
        }

        if (string.IsNullOrWhiteSpace(ColorHex))
        {
            throw new ArgumentException("ConditionDefinition.ColorHex cannot be blank", nameof(ColorHex));
        }

        return this;
    }
}
