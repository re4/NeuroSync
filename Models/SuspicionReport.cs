namespace NeuroSync.Models;

public sealed class SuspicionReport
{
    public ulong UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public int TotalScore { get; set; }
    public List<SuspicionFlag> Flags { get; } = [];
    public DateTimeOffset AccountCreated { get; init; }
    public DateTimeOffset JoinedServer { get; init; }
    public bool HasAvatar { get; init; }
    public bool HasBanner { get; init; }
    public int RoleCount { get; init; }
    public bool IsBoosting { get; init; }
    public string Verdict => TotalScore switch
    {
        >= 80 => "CRITICAL",
        >= 65 => "HIGH",
        >= 45 => "MEDIUM",
        _ => "LOW"
    };
}

public sealed class SuspicionFlag
{
    public string Rule { get; init; } = string.Empty;
    public int Points { get; init; }
    public string Detail { get; init; } = string.Empty;
}
