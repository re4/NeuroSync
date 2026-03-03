using Discord;
using Discord.WebSocket;
using NeuroSync.Config;
using Microsoft.Extensions.Logging;

namespace NeuroSync.Services;

/// <summary>
/// Registers and handles slash commands for manual user inspection and bot configuration.
/// </summary>
public sealed class SlashCommandHandler(
    DiscordSocketClient client,
    SuspicionAnalyzer analyzer,
    NeuroConfig config,
    ILogger<SlashCommandHandler> log)
{
    public async Task RegisterAsync()
    {
        client.Ready += OnReady;
        client.SlashCommandExecuted += OnSlashCommand;
        await Task.CompletedTask;
    }

    private async Task OnReady()
    {
        var scanCmd = new SlashCommandBuilder()
            .WithName("neuro-scan")
            .WithDescription("Manually trigger a suspicion scan on a user")
            .AddOption("user", ApplicationCommandOptionType.User, "The user to scan", isRequired: true)
            .WithDefaultMemberPermissions(GuildPermission.KickMembers)
            .Build();

        var statusCmd = new SlashCommandBuilder()
            .WithName("neuro-status")
            .WithDescription("Show current NeuroSync configuration")
            .WithDefaultMemberPermissions(GuildPermission.KickMembers)
            .Build();

        var toggleCmd = new SlashCommandBuilder()
            .WithName("neuro-dryrun")
            .WithDescription("Toggle dry-run mode on or off")
            .AddOption("enabled", ApplicationCommandOptionType.Boolean, "Enable dry-run?", isRequired: true)
            .WithDefaultMemberPermissions(GuildPermission.Administrator)
            .Build();

        try
        {
            await client.BulkOverwriteGlobalApplicationCommandsAsync([scanCmd, statusCmd, toggleCmd]);
            log.LogInformation("Slash commands registered");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to register slash commands");
        }
    }

    private async Task OnSlashCommand(SocketSlashCommand cmd)
    {
        switch (cmd.CommandName)
        {
            case "neuro-scan":
                await HandleScan(cmd);
                break;
            case "neuro-status":
                await HandleStatus(cmd);
                break;
            case "neuro-dryrun":
                await HandleDryRun(cmd);
                break;
        }
    }

    private async Task HandleScan(SocketSlashCommand cmd)
    {
        var target = (SocketGuildUser)cmd.Data.Options.First().Value;
        var report = analyzer.Analyze(target);

        var flags = report.Flags.Count > 0
            ? string.Join("\n", report.Flags.Select(f => $"`{f.Rule}` (+{f.Points}) — {f.Detail}"))
            : "_No flags triggered_";

        var embed = new EmbedBuilder()
            .WithColor(report.TotalScore >= config.SuspicionThreshold ? Color.Red : Color.Green)
            .WithTitle($"Scan Result: {report.Verdict}")
            .WithDescription($"**{report.Username}** — Score: **{report.TotalScore}/100**\nThreshold: {config.SuspicionThreshold}")
            .AddField("Account Created", $"<t:{report.AccountCreated.ToUnixTimeSeconds()}:R>", true)
            .AddField("Joined Server", $"<t:{report.JoinedServer.ToUnixTimeSeconds()}:R>", true)
            .AddField("Avatar", report.HasAvatar ? "Custom" : "**Default**", true)
            .AddField("Roles", report.RoleCount.ToString(), true)
            .AddField("Boosting", report.IsBoosting ? "Yes" : "No", true)
            .AddField("Flags", flags)
            .WithFooter($"Requested by {cmd.User.Username}")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await cmd.RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandleStatus(SocketSlashCommand cmd)
    {
        var actionLabel = config.Action switch
        {
            PunishAction.Kick => "Kick",
            PunishAction.Mute => $"Mute ({config.MuteDurationHours}h)",
            PunishAction.Ban => "Ban",
            _ => config.Action.ToString()
        };

        var embed = new EmbedBuilder()
            .WithColor(Color.Blue)
            .WithTitle("NeuroSync Status")
            .AddField("Scan Interval", $"{config.ScanIntervalHours}h", true)
            .AddField("Threshold", $"{config.SuspicionThreshold}/100", true)
            .AddField("Dry-Run", config.DryRun ? "ON (no action)" : "**OFF (active)**", true)
            .AddField("Action", actionLabel, true)
            .AddField("Scan on Join", config.ScanOnJoin ? "ON" : "OFF", true)
            .AddField("Log Channel", $"#{config.LogChannelName}", true)
            .AddField("Min Account Age", $"{config.MinAccountAgeDays} days", true)
            .AddField("Whitelisted Roles", config.WhitelistedRoleIds.Length > 0
                ? string.Join(", ", config.WhitelistedRoleIds.Select(id => $"<@&{id}>"))
                : "_None_", true)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await cmd.RespondAsync(embed: embed, ephemeral: true);
    }

    private async Task HandleDryRun(SocketSlashCommand cmd)
    {
        var enabled = (bool)cmd.Data.Options.First().Value;
        config.DryRun = enabled;
        log.LogWarning("Dry-run toggled to {State} by {User}", enabled, cmd.User.Username);
        await cmd.RespondAsync($"Dry-run is now **{(enabled ? "ON" : "OFF")}**.", ephemeral: true);
    }
}
