# NeuroSync — AI Protection Bot

Automated Discord bot that scans server members every 24 hours and kicks accounts that score above a suspicion threshold based on real Discord account signals.

## Suspicion Heuristics

Each member is scored 0–100 based on these real Discord data points:

| Signal | Points | Rationale |
|---|---|---|
| Account < 1 day old | +30 | Bulk-created accounts are minutes old |
| Account < 3 days old | +22 | Still extremely suspicious |
| Account < 7 days old | +15 | Below minimum age threshold |
| Account < 30 days old | +8 | Young but less risky |
| Joined < 30 min after creation | +25 | Account made specifically to target server |
| Joined < 6h after creation | +15 | Very fast join after creation |
| Default avatar | +15 | ~95% of real users set a custom avatar |
| Random hex/base64 username | +20 | Bot generation pattern |
| Spam keywords in username | +20 | "Free Nitro", "Gift", etc. |
| Excessive trailing digits | +10 | Batch account naming |
| No roles | +10 | Zero community engagement |
| Snowflake timestamp mismatch | +15 | Possible ID manipulation |
| Automod quarantined username | +30 | Discord's own automod flagged this user |
| No display name (bio proxy) | +8 | Most real users set a global display name |
| Is guest | +5 | Limited server access, not full member |
| Has Discord trust flags | **-20** | HypeSquad, CertifiedModerator, EarlySupporter, etc. |
| Server booster | **-25** | Financial commitment = human |
| Custom banner | **-15** | Requires Nitro subscription |
| Avatar decoration (orb/collectible) | **-20** | Requires purchase or Nitro |
| Owns collectible SKU | **-10** | Paid cosmetic item |
| Completed onboarding | **-5** | Engaged with server setup flow |
| Bypasses verification | **-10** | Admin manually approved |
| Guild-specific avatar | **-10** | High engagement — customized per server |
| Has nickname | **-5** | Actively engaged with this server |
| Primary guild badge | **-10** | Newer profile feature, shows server pride |
| Active presence (games/Spotify) | **-10** | Real client activity |
| Custom status set | **-5** | Deliberate profile personalization |
| Multi-client (Desktop + Mobile) | **-5** | Cross-device = human |
| 3+ mutual guilds | **-10** | Established Discord presence |

Default kick threshold: **65/100**

## Setup

### 1. Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A Discord Bot with these **Privileged Gateway Intents** enabled:
  - Server Members Intent
  - Presence Intent

### 2. Create Your Bot
1. Go to [Discord Developer Portal](https://discord.com/developers/applications)
2. Create a new Application → Bot
3. Enable **Server Members Intent** and **Presence Intent** under Bot settings
4. Copy the bot token

### 3. Configure
Edit `appsettings.json`:
```json
{
  "Discord": {
    "Token": "YOUR_BOT_TOKEN_HERE",
    "ScanIntervalHours": 24,
    "SuspicionThreshold": 65,
    "DryRun": true,
    "Action": "kick",
    "MuteDurationHours": 24,
    "ScanOnJoin": true,
    "LogChannelName": "neuro-log",
    "WhitelistedRoleIds": [123456789012345678, 987654321098765432],
    "MinAccountAgeDays": 7
  }
}
```

| Setting | Values | Description |
|---|---|---|
| `Action` | `kick`, `mute`, `ban` | What to do when a user exceeds the threshold |
| `MuteDurationHours` | any integer | How long to timeout when action is `mute` |
| `ScanOnJoin` | `true` / `false` | Instantly scan new members when they join |

### 4. Invite the Bot
Use this URL template (replace `CLIENT_ID`):
```
https://discord.com/oauth2/authorize?client_id=CLIENT_ID&permissions=8198&scope=bot%20applications.commands
```
Permissions needed: Kick Members, Ban Members, Moderate Members, Send Messages, View Channels, Use Slash Commands.

### 5. Create Log Channel
Create a text channel named `neuro-log` (or whatever you set in config) for the bot to post scan reports.

### 6. Run
```bash
dotnet restore
dotnet run
```

## Slash Commands

| Command | Permission | Description |
|---|---|---|
| `/neuro-scan @user` | Kick Members | Manually scan a specific user |
| `/neuro-status` | Kick Members | View current bot configuration |
| `/neuro-dryrun true/false` | Administrator | Toggle dry-run mode |

## Safety

The bot starts in **dry-run mode** by default — it logs what it *would* kick but doesn't actually kick anyone. Review the logs in `#neuro-log` before setting `DryRun` to `false`.

Accounts with whitelisted roles are always skipped.

## Project Structure

```
├── Program.cs                      # Entry point, DI, bot startup
├── appsettings.json                # Bot token and configuration
├── Config/
│   └── NeuroConfig.cs              # Strongly-typed config model
├── Models/
│   └── SuspicionReport.cs          # Scan result and flag models
├── Services/
│   ├── SuspicionAnalyzer.cs        # Scoring engine (13 heuristic checks)
│   ├── ProtectionService.cs        # 24h scan loop + kick logic
│   └── SlashCommandHandler.cs      # Slash command registration & handling
```
