namespace LootSingles.Application.Auth;

/// <summary>
/// The single implementation of FR-008's PIN shape rule (exactly 4 numeric digits), shared by
/// every caller that accepts a raw PIN so the rule is not re-implemented per controller
/// (Constitution XII — shared validation rules MUST NOT be duplicated).
/// </summary>
public static class PinFormat
{
    public static bool IsValid(string? pin) => pin is { Length: 4 } && pin.All(char.IsAsciiDigit);
}
