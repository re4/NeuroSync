namespace NeuroSync.Config;

public sealed class NeuroConfig
{
    public string Token { get; set; } = string.Empty;
    public int ScanIntervalHours { get; set; } = 24;
    public int SuspicionThreshold { get; set; } = 65;
    public bool DryRun { get; set; } = true;
    public PunishAction Action { get; set; } = PunishAction.Kick;
    public int MuteDurationHours { get; set; } = 24;
    public bool ScanOnJoin { get; set; } = true;
    public string LogChannelName { get; set; } = "neuro-log";
    public ulong[] WhitelistedRoleIds { get; set; } = [];
    public int MinAccountAgeDays { get; set; } = 7;
    public string ActionMessage { get; set; } = "Your account was flagged by our protection system. If this was a mistake, please rejoin and contact a moderator.";
}

public enum PunishAction
{
    Kick,
    Mute,
    Ban
}
