using Discord;
using Discord.WebSocket;
using NeuroSync.Config;
using NeuroSync.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NeuroSync.Services;

/// <summary>
/// Background service that scans guild members on a timer and optionally on join, then punishes suspicious accounts.
/// </summary>
public sealed class ProtectionService(
    DiscordSocketClient client,
    SuspicionAnalyzer analyzer,
    NeuroConfig config,
    ILogger<ProtectionService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Ready += () => { ready.TrySetResult(); return Task.CompletedTask; };

        if (client.LoginState == LoginState.LoggedIn && client.ConnectionState == ConnectionState.Connected)
            ready.TrySetResult();

        if (config.ScanOnJoin)
            client.UserJoined += OnUserJoined;

        await ready.Task.WaitAsync(ct);
        log.LogInformation("ProtectionService online — interval={Hours}h, threshold={T}, action={Action}, dry-run={Dry}, scan-on-join={Join}",
            config.ScanIntervalHours, config.SuspicionThreshold, config.Action, config.DryRun, config.ScanOnJoin);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(config.ScanIntervalHours));
        await RunScan(ct);

        while (await timer.WaitForNextTickAsync(ct))
            await RunScan(ct);
    }

    private Task OnUserJoined(SocketGuildUser member)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (member.IsBot) return;
                if (IsWhitelisted(member)) return;

                var report = analyzer.Analyze(member);
                if (report.TotalScore < config.SuspicionThreshold) return;

                var logChannel = member.Guild.TextChannels
                    .FirstOrDefault(c => c.Name.Equals(config.LogChannelName, StringComparison.OrdinalIgnoreCase));

                await LogReport(logChannel, report);
                await ExecuteAction(member, report);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error scanning joined user {User}", member.Username);
            }
        });

        return Task.CompletedTask;
    }

    private async Task RunScan(CancellationToken ct)
    {
        foreach (var guild in client.Guilds)
        {
            log.LogInformation("Scanning guild {Guild} ({Id}) — {Count} members",
                guild.Name, guild.Id, guild.MemberCount);

            await guild.DownloadUsersAsync();

            var reports = new List<SuspicionReport>();

            foreach (var member in guild.Users)
            {
                if (ct.IsCancellationRequested) return;
                if (member.IsBot) continue;
                if (IsWhitelisted(member)) continue;

                var report = analyzer.Analyze(member);
                if (report.TotalScore >= config.SuspicionThreshold)
                    reports.Add(report);
            }

            log.LogInformation("Guild {Guild}: {Flagged}/{Total} members above threshold",
                guild.Name, reports.Count, guild.Users.Count(u => !u.IsBot));

            reports.Sort((a, b) => b.TotalScore.CompareTo(a.TotalScore));

            var logChannel = guild.TextChannels
                .FirstOrDefault(c => c.Name.Equals(config.LogChannelName, StringComparison.OrdinalIgnoreCase));

            foreach (var report in reports)
            {
                var member = guild.GetUser(report.UserId);
                if (member is null) continue;

                await LogReport(logChannel, report);
                await ExecuteAction(member, report);
            }
        }
    }

    /// <summary>
    /// Executes the configured punishment action (kick/mute/ban) or logs a dry-run.
    /// </summary>
    private async Task ExecuteAction(SocketGuildUser member, SuspicionReport report)
    {
        var reason = $"NeuroSync — suspicion score {report.TotalScore}/100";

        if (config.DryRun)
        {
            log.LogWarning("[DRY-RUN] Would {Action} {User} (score {Score})",
                config.Action, report.Username, report.TotalScore);
            return;
        }

        try
        {
            await TryDmUser(member, config.ActionMessage);

            switch (config.Action)
            {
                case PunishAction.Kick:
                    await member.KickAsync(reason);
                    log.LogWarning("Kicked {User} from {Guild} (score {Score})",
                        report.Username, member.Guild.Name, report.TotalScore);
                    break;

                case PunishAction.Mute:
                    var duration = TimeSpan.FromHours(config.MuteDurationHours);
                    await member.SetTimeOutAsync(duration);
                    log.LogWarning("Muted {User} in {Guild} for {Hours}h (score {Score})",
                        report.Username, member.Guild.Name, config.MuteDurationHours, report.TotalScore);
                    break;

                case PunishAction.Ban:
                    await member.BanAsync(0, reason);
                    log.LogWarning("Banned {User} from {Guild} (score {Score})",
                        report.Username, member.Guild.Name, report.TotalScore);
                    break;
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to {Action} {User}", config.Action, report.Username);
        }
    }

    private bool IsWhitelisted(SocketGuildUser member)
    {
        return member.Roles.Any(r => config.WhitelistedRoleIds.Contains(r.Id));
    }

    private static async Task TryDmUser(SocketGuildUser member, string message)
    {
        try
        {
            var dm = await member.CreateDMChannelAsync();
            await dm.SendMessageAsync(message);
        }
        catch { }
    }

    private static async Task LogReport(ITextChannel? channel, SuspicionReport report)
    {
        if (channel is null) return;

        var flags = string.Join("\n",
            report.Flags.Select(f => $"  `{f.Rule}` (+{f.Points}) — {f.Detail}"));

        var embed = new EmbedBuilder()
            .WithColor(report.Verdict switch
            {
                "CRITICAL" => Color.Red,
                "HIGH" => Color.Orange,
                "MEDIUM" => Color.Gold,
                _ => Color.LightGrey
            })
            .WithTitle($"Suspicious Account — {report.Verdict}")
            .WithDescription($"**{report.Username}** (<@{report.UserId}>)")
            .AddField("Score", $"{report.TotalScore}/100", inline: true)
            .AddField("Account Age", FormatAge(DateTimeOffset.UtcNow - report.AccountCreated), inline: true)
            .AddField("Join Age", FormatAge(DateTimeOffset.UtcNow - report.JoinedServer), inline: true)
            .AddField("Avatar", report.HasAvatar ? "Yes" : "**No** (default)", inline: true)
            .AddField("Roles", report.RoleCount == 0 ? "**None**" : report.RoleCount.ToString(), inline: true)
            .AddField("Boosting", report.IsBoosting ? "Yes" : "No", inline: true)
            .AddField("Flags Triggered", flags)
            .WithFooter($"ID: {report.UserId}")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(embed: embed);
    }

    private static string FormatAge(TimeSpan ts) => ts.TotalDays switch
    {
        < 1 => $"{ts.TotalHours:F1} hours",
        < 30 => $"{ts.TotalDays:F0} days",
        < 365 => $"{ts.TotalDays / 30:F0} months",
        _ => $"{ts.TotalDays / 365:F1} years"
    };
}
