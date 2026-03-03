using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using NeuroSync.Config;
using NeuroSync.Models;

namespace NeuroSync.Services;

/// <summary>
/// Scores guild members 0–100 on suspicion heuristics derived from real Discord account data.
/// </summary>
public sealed partial class SuspicionAnalyzer(NeuroConfig config)
{
    private static readonly DateTimeOffset DiscordEpoch = new(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Runs all heuristic checks against a guild member and produces a scored report.
    /// </summary>
    public SuspicionReport Analyze(SocketGuildUser user)
    {
        var report = new SuspicionReport
        {
            UserId = user.Id,
            Username = $"{user.Username}#{user.Discriminator}",
            AccountCreated = user.CreatedAt,
            JoinedServer = user.JoinedAt ?? DateTimeOffset.UtcNow,
            HasAvatar = user.AvatarId is not null,
            HasBanner = user.GetGuildBannerUrl() is not null,
            RoleCount = user.Roles.Count(r => !r.IsEveryone),
            IsBoosting = user.PremiumSince.HasValue,
        };

        CheckAccountAge(user, report);
        CheckJoinProximity(user, report);
        CheckAvatar(user, report);
        CheckUsername(user, report);
        CheckRolesAndActivity(user, report);
        CheckUserFlags(user, report);
        CheckGuildMemberFlags(user, report);
        CheckNitroAndBoosting(user, report);
        CheckCollectibles(user, report);
        CheckProfileCustomization(user, report);
        CheckPresence(user, report);
        CheckMutualGuilds(user, report);
        CheckBulkCreationPattern(user, report);

        report.TotalScore = Math.Clamp(report.Flags.Sum(f => f.Points), 0, 100);
        return report;
    }

    /// <summary>
    /// Flags accounts below configurable age thresholds. Bulk-created accounts are typically minutes old.
    /// </summary>
    private void CheckAccountAge(SocketGuildUser user, SuspicionReport report)
    {
        var age = DateTimeOffset.UtcNow - user.CreatedAt;

        if (age.TotalDays < 1)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "ACCOUNT_AGE_CRITICAL",
                Points = 30,
                Detail = $"Account is {age.TotalHours:F1} hours old — extremely likely automated"
            });
        }
        else if (age.TotalDays < 3)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "ACCOUNT_AGE_VERY_NEW",
                Points = 22,
                Detail = $"Account is {age.TotalDays:F1} days old"
            });
        }
        else if (age.TotalDays < config.MinAccountAgeDays)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "ACCOUNT_AGE_NEW",
                Points = 15,
                Detail = $"Account is {age.TotalDays:F1} days old (threshold: {config.MinAccountAgeDays}d)"
            });
        }
        else if (age.TotalDays < 30)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "ACCOUNT_AGE_YOUNG",
                Points = 8,
                Detail = $"Account is {age.TotalDays:F0} days old — still relatively new"
            });
        }
    }

    /// <summary>
    /// Flags accounts that joined the server very shortly after being created.
    /// </summary>
    private static void CheckJoinProximity(SocketGuildUser user, SuspicionReport report)
    {
        if (user.JoinedAt is not { } joined) return;

        var gap = joined - user.CreatedAt;

        if (gap.TotalMinutes < 30)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "JOIN_PROXIMITY_IMMEDIATE",
                Points = 25,
                Detail = $"Joined {gap.TotalMinutes:F0} min after account creation"
            });
        }
        else if (gap.TotalHours < 6)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "JOIN_PROXIMITY_FAST",
                Points = 15,
                Detail = $"Joined {gap.TotalHours:F1}h after account creation"
            });
        }
        else if (gap.TotalHours < 24)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "JOIN_PROXIMITY_SAME_DAY",
                Points = 8,
                Detail = $"Joined same day as account creation ({gap.TotalHours:F0}h gap)"
            });
        }
    }

    /// <summary>
    /// Flags users with no custom avatar. ~95% of real users set one within hours of account creation.
    /// </summary>
    private static void CheckAvatar(SocketGuildUser user, SuspicionReport report)
    {
        if (user.AvatarId is null && user.GetGuildAvatarUrl() is null)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "NO_AVATAR",
                Points = 15,
                Detail = "Default avatar — real users set one within hours of creating an account"
            });
        }
    }

    /// <summary>
    /// Flags usernames matching bot generation patterns: random hex, excessive digits, spam keywords.
    /// </summary>
    private void CheckUsername(SocketGuildUser user, SuspicionReport report)
    {
        var name = user.DisplayName ?? user.Username;

        if (HexLikePattern().IsMatch(name))
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "USERNAME_RANDOM_HEX",
                Points = 20,
                Detail = $"\"{name}\" matches random hex/base64 generation pattern"
            });
        }
        else if (ExcessiveDigitsPattern().IsMatch(name))
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "USERNAME_EXCESSIVE_DIGITS",
                Points = 10,
                Detail = $"\"{name}\" has excessive trailing digits — common in batch-created accounts"
            });
        }

        if (SpamKeywordPattern().IsMatch(name))
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "USERNAME_SPAM_KEYWORDS",
                Points = 20,
                Detail = $"\"{name}\" contains known spam/phishing keywords"
            });
        }

        if (name.Length <= 2)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "USERNAME_TOO_SHORT",
                Points = 5,
                Detail = $"\"{name}\" is suspiciously short"
            });
        }
    }

    /// <summary>
    /// Flags users with zero roles besides @everyone, indicating no community engagement.
    /// </summary>
    private static void CheckRolesAndActivity(SocketGuildUser user, SuspicionReport report)
    {
        var realRoles = user.Roles.Count(r => !r.IsEveryone);

        if (realRoles == 0)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "NO_ROLES",
                Points = 10,
                Detail = "No roles assigned — no community engagement"
            });
        }
    }

    /// <summary>
    /// Grants trust bonus for Discord-issued account flags that are impossible to fake at scale.
    /// ActiveDeveloper badge was retired in 2026.
    /// </summary>
    private static void CheckUserFlags(SocketGuildUser user, SuspicionReport report)
    {
        var flags = user.PublicFlags ?? Discord.UserProperties.None;

        bool hasTrustFlag =
            flags.HasFlag(Discord.UserProperties.HypeSquadBravery) ||
            flags.HasFlag(Discord.UserProperties.HypeSquadBrilliance) ||
            flags.HasFlag(Discord.UserProperties.HypeSquadBalance) ||
            flags.HasFlag(Discord.UserProperties.HypeSquadEvents) ||
            flags.HasFlag(Discord.UserProperties.EarlySupporter) ||
            flags.HasFlag(Discord.UserProperties.BugHunterLevel1) ||
            flags.HasFlag(Discord.UserProperties.BugHunterLevel2) ||
            flags.HasFlag(Discord.UserProperties.EarlyVerifiedBotDeveloper) ||
            flags.HasFlag(Discord.UserProperties.DiscordCertifiedModerator) ||
            flags.HasFlag(Discord.UserProperties.Partner) ||
            flags.HasFlag(Discord.UserProperties.Staff);

        if (hasTrustFlag)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "HAS_TRUST_FLAGS",
                Points = -20,
                Detail = $"User has Discord-granted flags ({flags}) — strong trust signal"
            });
        }
    }

    /// <summary>
    /// Evaluates guild-level member flags set by Discord: automod quarantine, onboarding, guest status.
    /// </summary>
    private static void CheckGuildMemberFlags(SocketGuildUser user, SuspicionReport report)
    {
        var guildFlags = user.Flags;

        if (guildFlags.HasFlag(GuildUserFlags.AutomodQuarantinedUsername))
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "AUTOMOD_QUARANTINED",
                Points = 30,
                Detail = "Username was quarantined by Discord's own Automod — extremely suspicious"
            });
        }

        if (guildFlags.HasFlag(GuildUserFlags.CompletedOnboarding))
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "COMPLETED_ONBOARDING",
                Points = -5,
                Detail = "Completed server onboarding — engaged with the community"
            });
        }

        if (guildFlags.HasFlag(GuildUserFlags.IsGuest))
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "IS_GUEST",
                Points = 5,
                Detail = "User is a guest — limited server access, not a full member"
            });
        }

        if (guildFlags.HasFlag(GuildUserFlags.BypassesVerification))
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "BYPASSES_VERIFICATION",
                Points = -10,
                Detail = "Manually approved to bypass verification — admin-trusted"
            });
        }
    }

    /// <summary>
    /// Grants trust for financial commitment: server boosting and Nitro (detected via custom banner).
    /// </summary>
    private static void CheckNitroAndBoosting(SocketGuildUser user, SuspicionReport report)
    {
        if (user.PremiumSince.HasValue)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "IS_BOOSTING",
                Points = -25,
                Detail = $"Server booster since {user.PremiumSince.Value:yyyy-MM-dd} — extremely unlikely to be a bot"
            });
        }

        if (user.GetGuildBannerUrl() is not null)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "HAS_BANNER",
                Points = -15,
                Detail = "Custom profile banner — requires Nitro subscription"
            });
        }
    }

    /// <summary>
    /// Grants trust for avatar decorations and collectible SKUs, which require real purchases.
    /// </summary>
    private static void CheckCollectibles(SocketGuildUser user, SuspicionReport report)
    {
        if (user.AvatarDecorationHash is not null || user.GetAvatarDecorationUrl() is not null)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "HAS_AVATAR_DECORATION",
                Points = -20,
                Detail = "User has avatar decoration (orb/collectible) — requires purchase or Nitro"
            });
        }

        if (user.AvatarDecorationSkuId.HasValue)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "HAS_COLLECTIBLE_SKU",
                Points = -10,
                Detail = $"Owns collectible SKU {user.AvatarDecorationSkuId.Value} — paid cosmetic item"
            });
        }
    }

    /// <summary>
    /// Evaluates profile personalization: display name, guild avatar, nickname, primary guild badge.
    /// The "About Me" bio is not exposed to bots via the Discord gateway API.
    /// </summary>
    private static void CheckProfileCustomization(SocketGuildUser user, SuspicionReport report)
    {
        bool hasGlobalName = !string.IsNullOrWhiteSpace(user.GlobalName);
        bool hasNickname = !string.IsNullOrWhiteSpace(user.Nickname);
        bool hasGuildAvatar = user.GuildAvatarId is not null;
        bool hasPrimaryGuild = user.PrimaryGuild is not null;

        if (!hasGlobalName)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "NO_DISPLAY_NAME",
                Points = 8,
                Detail = "No global display name set — most real users customize this"
            });
        }

        if (hasGuildAvatar)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "HAS_GUILD_AVATAR",
                Points = -10,
                Detail = "Set a server-specific avatar — high engagement signal"
            });
        }

        if (hasNickname)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "HAS_NICKNAME",
                Points = -5,
                Detail = "Set a server nickname — actively engaged with this community"
            });
        }

        if (hasPrimaryGuild)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "HAS_PRIMARY_GUILD",
                Points = -10,
                Detail = "Has a primary guild badge — newer profile feature showing server pride"
            });
        }
    }

    /// <summary>
    /// Grants trust for active Discord presence: game activity, custom status, multi-client usage.
    /// </summary>
    private static void CheckPresence(SocketGuildUser user, SuspicionReport report)
    {
        if (user.Activities.Count > 0)
        {
            var activityTypes = user.Activities
                .Select(a => a.Type.ToString())
                .Distinct();

            report.Flags.Add(new SuspicionFlag
            {
                Rule = "HAS_ACTIVITY",
                Points = -10,
                Detail = $"Active presence: {string.Join(", ", activityTypes)} — real client behaviour"
            });

            bool hasCustomStatus = user.Activities.Any(a => a is CustomStatusGame);
            if (hasCustomStatus)
            {
                report.Flags.Add(new SuspicionFlag
                {
                    Rule = "HAS_CUSTOM_STATUS",
                    Points = -5,
                    Detail = "Set a custom status — deliberate profile personalization"
                });
            }
        }

        if (user.ActiveClients?.Count > 1)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "MULTI_CLIENT",
                Points = -5,
                Detail = $"Active on {user.ActiveClients.Count} clients ({string.Join(", ", user.ActiveClients)}) — cross-device = human"
            });
        }
    }

    /// <summary>
    /// Grants trust for users present in 3+ guilds visible to the bot.
    /// </summary>
    private static void CheckMutualGuilds(SocketGuildUser user, SuspicionReport report)
    {
        var mutualCount = user.MutualGuilds.Count;

        if (mutualCount >= 3)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "MANY_MUTUAL_GUILDS",
                Points = -10,
                Detail = $"Present in {mutualCount} mutual guilds — established Discord presence"
            });
        }
    }

    /// <summary>
    /// Detects snowflake ID timestamp drift, which can indicate bulk creation or ID manipulation.
    /// </summary>
    private static void CheckBulkCreationPattern(SocketGuildUser user, SuspicionReport report)
    {
        var snowflakeTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(
            (long)(user.Id >> 22) + 1420070400000);

        var drift = Math.Abs((snowflakeTimestamp - user.CreatedAt).TotalSeconds);
        if (drift > 60)
        {
            report.Flags.Add(new SuspicionFlag
            {
                Rule = "SNOWFLAKE_DRIFT",
                Points = 15,
                Detail = $"Snowflake timestamp drifts {drift:F0}s from CreatedAt — possible ID manipulation"
            });
        }
    }

    [GeneratedRegex(@"^[a-f0-9]{8,}$", RegexOptions.IgnoreCase)]
    private static partial Regex HexLikePattern();

    [GeneratedRegex(@"\d{4,}$")]
    private static partial Regex ExcessiveDigitsPattern();

    [GeneratedRegex(@"(free\s*nitro|gift|airdrop|crypto|nft\s*drop|click\s*here|steam\s*gift|earn\s*money)", RegexOptions.IgnoreCase)]
    private static partial Regex SpamKeywordPattern();
}
