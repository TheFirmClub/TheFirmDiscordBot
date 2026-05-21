using Discord;
using Discord.WebSocket;
using Discord.Net;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MySqlConnector;

public class StaffLoaCommand : ISlashCommand
{
    // ====== CONFIG ======
    private readonly ulong _guildId   = 1393589436402634874;   // your guild
    private readonly ulong _loaRoleId = 1394453715250974771;   // LOA role

    // Senior Management override role (can do everything incl. approve/decline across divisions)
    private const ulong OverrideRoleId = 1393590761953558608;

    // 🔔 Log channel for LOA events
    private const ulong LoaLogChannelId = 1434214545534226563;

    // OVH MySQL (converted from your URL)
    private readonly string _mysql =
        "Server=nw26472-001.eu.clouddb.ovh.net;Port=35666;Database=thefirm_qbcore2;User ID=thefirmprod;Password=edr6BYZqmq7eud0mwm;SslMode=Required;AllowPublicKeyRetrieval=True;Character Set=utf8mb4;";

    // ✅ Allowed initiator roles (can run /staffloa, /loaremove)
    private static readonly HashSet<ulong> AllowedInitiatorRoleIds = new()
    {
        1420512528395665569, // MET Command
        1420512797191704616, // NHS Command
        1420513009729802260, // Civil Command
        1403466391499313263, // PR Team
        1398306711311224862, // Media Team
        1399174001275965520, // Area Command
        1393623589122736238, // Discord Mod
        1393729574537396355, // Game Mod
        1393638449709584434, // Senior Mod
        1393590761953558608  // Senior Management (override)
    };
    
    // ✅ Extra roles that can *view* /staffloalist but NOT approve/decline
    private static readonly HashSet<ulong> LoaListViewerRoleIds = new()
    {
        1394459419156418730, // Response Inspector
        1394650413361533009, // RPU Inspector
        1394465316817473646  // TFU Inspector
    };

    // Division map: key -> (targetChannelId, approverRoleId, label)
    private static readonly Dictionary<string, (ulong ChannelId, ulong ApproverRoleId, string Label)> DivisionRoutes =
        new()
        {
            ["police_response_staff"] = (1475222924301439148, 1394649657644290078, "Police Response Staff"),
            ["police_roads_staff"]    = (1475222924301439148, 1394649657644290078, "Police Roads Staff"),
            ["police_firearms_staff"] = (1475222924301439148, 1394649657644290078, "Police Firearms Staff"),
            ["police_senior_command"] = (1475222924301439148, 1394458024503935006, "Police Senior Command"),
            ["tfhs_staff"]            = (1404782091199053847, 1398308435795251302, "TFHS Staff"),
            ["civilian_affairs"]      = (1406298760866172958, 1406295299311272006, "Civilian Affairs Staff"),
            ["creative_media_staff"]  = (1398309451697750106, 1394651533253017671, "Creative Media Staff"),
            ["media_team"]            = (1398309451697750106, 1394651533253017671, "Media Team"),
            ["moderation"]            = (1496169956700721413, 1393638449709584434, "Moderation"),
        };

    // Pending division (per-user) — include timestamp to expire stale selections
    private static readonly Dictionary<ulong, (string DivKey, DateTime StoredAtUtc)> _pendingDivisionByUser = new();

    private DiscordSocketClient _client;
    
    public void AttachHandlers(DiscordSocketClient client)
    {
        _client = client;

        _client.InteractionCreated -= OnInteractionCreated;
        _client.InteractionCreated += OnInteractionCreated;
    }

    // ========= ISlashCommand contract =========
    public string Name => "staffloa"; // main command used by Program.cs
    public string Description => "Submit a Staff Leave Of Absence (LOA) request.";

    // Sibling command names
    private const string LoaRemoveCommandName = "loaremove";
    private const string LoaListCommandName   = "staffloalist";

    public async Task RegisterAsync(DiscordSocketClient client)
    {
        _client = client;

        _client.InteractionCreated -= OnInteractionCreated; // avoid double-subscribe
        _client.InteractionCreated += OnInteractionCreated;

        try
        {
            // Register /staffloa
            await client.Rest.CreateGuildCommand(new SlashCommandBuilder()
                .WithName(Name)
                .WithDescription(Description)
                .Build(), _guildId);

            // Register /loaremove (self-service, no options)
            var loaremove = new SlashCommandBuilder()
                .WithName(LoaRemoveCommandName)
                .WithDescription("Remove the LOA role from yourself (requires senior/command roles).")
                .Build();
            await client.Rest.CreateGuildCommand(loaremove, _guildId);

            // Register /staffloalist (no options)
            var loalist = new SlashCommandBuilder()
                .WithName(LoaListCommandName)
                .WithDescription("List all active LOAs (Discord ID + Return Date). Only approvers.")
                .Build();
            await client.Rest.CreateGuildCommand(loalist, _guildId);
        }
        catch (HttpException e)
        {
            Console.WriteLine($"Failed to register commands: {e}");
        }
    }

    // ---------- Role gate helpers ----------
    private static bool IsInitiatorAllowed(SocketGuildUser? user)
        => user != null && user.Roles.Any(r => AllowedInitiatorRoleIds.Contains(r.Id));

    // ✅ Approver = has any division approver role OR override
    private static bool IsApprover(SocketGuildUser? user)
    {
        if (user == null) return false;
        if (user.Roles.Any(r => r.Id == OverrideRoleId)) return true;
        var approverRoles = DivisionRoutes.Values.Select(v => v.ApproverRoleId).ToHashSet();
        return user.Roles.Any(r => approverRoles.Contains(r.Id));
    }

    // ---------- DM helper ----------
    private async Task NotifyApplicantDm(ulong applicantId, bool approved, DateTime startUtc, DateTime endUtc, SocketUser approver)
    {
        try
        {
            var user = _client.GetUser(applicantId);
            if (user == null) return;

            var eb = new EmbedBuilder()
                .WithTitle(approved ? "✅ LOA Approved" : "❌ LOA Declined")
                .WithColor(approved ? Color.Green : Color.Red)
                .AddField("LOA Period", $"**From:** {startUtc:yyyy-MM-dd}\n**To:** {endUtc:yyyy-MM-dd}", inline: true)
                .AddField("Decision By", $"{approver.Username} (`{approver.Id}`)", inline: true)
                .WithTimestamp(DateTimeOffset.UtcNow);

            if (approved)
                eb.WithFooter("If this is unexpected or incorrect, contact Senior Management.");
            else
                eb.WithFooter("Contact your leadership team for more info.");

            await user.SendMessageAsync(embed: eb.Build());
        }
        catch
        {
            // DMs may be closed; ignore silently
        }
    }

    // ---------- Log helper ----------
    private async Task LogLoaEventAsync(string title, Color color, Action<EmbedBuilder> buildFields)
    {
        try
        {
            var guild = _client.GetGuild(_guildId);
            var logChannel = guild?.GetTextChannel(LoaLogChannelId);
            if (logChannel == null) return;

            var eb = new EmbedBuilder()
                .WithTitle(title)
                .WithColor(color)
                .WithTimestamp(DateTimeOffset.UtcNow);

            buildFields(eb);

            await logChannel.SendMessageAsync(embed: eb.Build());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StaffLoa] LogLoaEventAsync failed: {ex}");
        }
    }

    public async Task ExecuteAsync(SocketSlashCommand command)
    {
        // Route between /staffloa, /loaremove, /staffloalist
        var cmdName = command.Data.Name?.ToLowerInvariant();

        if (cmdName == Name) // /staffloa
        {
            var guser = command.User as SocketGuildUser;
            if (!IsInitiatorAllowed(guser))
            {
                await command.RespondAsync("You don’t have permission to use this command.", ephemeral: true);
                return;
            }

            // Ephemeral UI with division select + open modal button
            var menu = new SelectMenuBuilder()
                .WithCustomId("loa:division_select")
                .WithPlaceholder("Select your primary division / department")
                .WithMinValues(1).WithMaxValues(1);

            foreach (var kv in DivisionRoutes)
                menu.AddOption(kv.Value.Label, kv.Key);

            var openButton = new ButtonBuilder()
                .WithCustomId("loa:open_modal")
                .WithLabel("Open LOA Form")
                .WithStyle(ButtonStyle.Primary)
                .WithEmote(new Emoji("📝"));

            var comps = new ComponentBuilder()
                .WithSelectMenu(menu)
                .WithButton(openButton);

            await command.RespondAsync(
                text: "Pick your division, then click **Open LOA Form**.",
                components: comps.Build(),
                ephemeral: true);
            return;
        }
        else if (cmdName == LoaRemoveCommandName) // /loaremove (self only)
        {
            var guser = command.User as SocketGuildUser;
            if (!IsInitiatorAllowed(guser))
            {
                await command.RespondAsync("You don’t have permission to use this command.", ephemeral: true);
                return;
            }

            var guild = _client.GetGuild(_guildId);
            var loaRole = guild?.GetRole(_loaRoleId);
            if (loaRole == null)
            {
                await command.RespondAsync("LOA role not found. Contact admins.", ephemeral: true);
                return;
            }

            if (!guser.Roles.Any(r => r.Id == _loaRoleId))
            {
                await command.RespondAsync("You don’t currently have the LOA role.", ephemeral: true);
                return;
            }

            try
            {
                await guser.RemoveRoleAsync(loaRole);
                await command.RespondAsync("✅ Removed the LOA role from you.", ephemeral: true);

                // 🔔 Log: LOA role removed (division N/A for self-removal)
                await LogLoaEventAsync(
                    title: "LOA Role Removed",
                    color: Color.LightGrey,
                    buildFields: eb =>
                    {
                        eb.AddField("User", $"{guser.Username} (<@{guser.Id}>)", true)
                          .AddField("User ID", $"{guser.Id}", true)
                          .AddField("By", $"{guser.Username} (<@{guser.Id}>) — self-serve", true)
                          .AddField("Division", "N/A", true);
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StaffLoa] Self RemoveRole failed: {ex}");
                await command.RespondAsync("I couldn't remove your LOA role (check permissions/role order).", ephemeral: true);
            }
            return;
        }
        else if (cmdName == LoaListCommandName) // /staffloalist
        {
            var guser = command.User as SocketGuildUser;

            bool canViewList =
                guser != null &&
                (
                    IsApprover(guser) || 
                    guser.Roles.Any(r => LoaListViewerRoleIds.Contains(r.Id))
                );

            if (!canViewList)
            {
                await command.RespondAsync("You don’t have permission to view the active LOA list.", ephemeral: true);
                return;
            }

            List<(ulong ApplicantId, DateTime EndUtc)> rows = new();

            try
            {
                using var conn = new MySqlConnection(_mysql);
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT applicant_discord_id, end_date
                    FROM loa_request
                    WHERE status = 'APPROVED'
                      AND start_date <= UTC_TIMESTAMP()
                      AND end_date   >= UTC_TIMESTAMP()
                    ORDER BY end_date ASC
                    LIMIT 200;";
                using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var id = (ulong)r.GetInt64(0);
                    var end = r.GetDateTime(1); // assumed UTC in DB
                    rows.Add((id, DateTime.SpecifyKind(end, DateTimeKind.Utc)));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StaffLoa] staffloalist query failed: {ex}");
                await command.RespondAsync("Failed to fetch active LOAs. Try again later.", ephemeral: true);
                return;
            }

            if (rows.Count == 0)
            {
                await command.RespondAsync("No active LOAs at the moment.", ephemeral: true);
                return;
            }

            var lines = rows.Select(x => $"<@{x.ApplicantId}> — returns **{x.EndUtc:yyyy-MM-dd}**");
            var desc = string.Join("\n", lines);

            if (desc.Length > 3900)
            {
                var reduced = new List<string>();
                int total = 0, shown = 0;
                foreach (var line in lines)
                {
                    var len = line.Length + 1;
                    if (total + len > 3800) break;
                    reduced.Add(line);
                    total += len;
                    shown++;
                }
                desc = string.Join("\n", reduced) + $"\n…and {rows.Count - shown} more.";
            }

            var eb = new EmbedBuilder()
                .WithTitle($"Active LOAs ({rows.Count})")
                .WithDescription(desc)
                .WithColor(Color.Blue)
                .WithFooter("Dates shown in UTC (YYYY-MM-DD)")
                .WithTimestamp(DateTimeOffset.UtcNow);

            await command.RespondAsync(embed: eb.Build(), ephemeral: true);
            return;
        }
        else
        {
            await command.RespondAsync("Unknown command.", ephemeral: true);
        }
    }

    // ========= Interaction Router =========
    private async Task OnInteractionCreated(SocketInteraction arg)
    {
        try
        {
            switch (arg)
            {
                case SocketMessageComponent component:
                    if (component.Data.CustomId == "loa:open_modal")
                        await HandleOpenModal(component);
                    else if (component.Data.CustomId == "loa:division_select")
                        await HandleDivisionSelect(component);
                    else if (component.Data.CustomId.StartsWith("loa:approve:"))
                        await HandleApprove(component);
                    else if (component.Data.CustomId.StartsWith("loa:decline:"))
                        await HandleDecline(component);
                    break;

                case SocketModal modal:
                    if (modal.Data.CustomId == "loa:modal_submit")
                        await HandleModalSubmit(modal);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StaffLoa] Interaction error: {ex}");
        }
    }

    // ========= Division Select =========
    private async Task HandleDivisionSelect(SocketMessageComponent comp)
    {
        var guser = comp.User as SocketGuildUser;
        if (!IsInitiatorAllowed(guser))
        {
            await comp.RespondAsync("You don’t have permission to use this.", ephemeral: true);
            return;
        }

        var values = comp.Data.Values?.ToArray() ?? Array.Empty<string>();
        if (values.Length == 0 || !DivisionRoutes.ContainsKey(values[0]))
        {
            await comp.RespondAsync("Invalid division selection.", ephemeral: true);
            return;
        }

        _pendingDivisionByUser[comp.User.Id] = (values[0], DateTime.UtcNow);
        await comp.RespondAsync($"Division set to **{DivisionRoutes[values[0]].Label}**. Now click **Open LOA Form**.", ephemeral: true);
    }

    // ========= Open Modal =========
    private async Task HandleOpenModal(SocketMessageComponent comp)
    {
        var guser = comp.User as SocketGuildUser;
        if (!IsInitiatorAllowed(guser))
        {
            await comp.RespondAsync("You don’t have permission to use this.", ephemeral: true);
            return;
        }

        if (!_pendingDivisionByUser.TryGetValue(comp.User.Id, out var entry)
            || (DateTime.UtcNow - entry.StoredAtUtc) > TimeSpan.FromMinutes(5))
        {
            await comp.RespondAsync("Please re-select your division.", ephemeral: true);
            return;
        }

        var modal = new ModalBuilder()
            .WithTitle("Staff LOA Request")
            .WithCustomId("loa:modal_submit")
            .AddTextInput("Your Discord ID (numbers)", "discord_id", TextInputStyle.Short, placeholder: "123456789012345678", required: true, maxLength: 32)
            .AddTextInput("When are you going on LOA? (YYYY-MM-DD)", "start_date", TextInputStyle.Short, placeholder: "2025-11-05", required: true, maxLength: 32)
            .AddTextInput("When will you be returning? (YYYY-MM-DD)", "end_date", TextInputStyle.Short, placeholder: "2025-11-20", required: true, maxLength: 32)
            .AddTextInput("Reason (optional; basic only)", "reason", TextInputStyle.Paragraph, placeholder: "Holiday / vacation / family", required: false, maxLength: 1000);

        await comp.RespondWithModalAsync(modal.Build());
    }

    // ========= Modal Submit =========
    private async Task HandleModalSubmit(SocketModal modal)
    {
        var guser = modal.User as SocketGuildUser;
        if (!IsInitiatorAllowed(guser))
        {
            await modal.RespondAsync("You don’t have permission to use this.", ephemeral: true);
            return;
        }

        if (!_pendingDivisionByUser.TryGetValue(modal.User.Id, out var entry)
            || (DateTime.UtcNow - entry.StoredAtUtc) > TimeSpan.FromMinutes(5)
            || !DivisionRoutes.ContainsKey(entry.DivKey))
        {
            await modal.RespondAsync("Division not found/expired. Please re-run `/staffloa`.", ephemeral: true);
            return;
        }

        var divKey = entry.DivKey;
        var label = DivisionRoutes[divKey].Label;

        var dict = modal.Data.Components.ToDictionary(c => c.CustomId, c => (c.Value ?? "").Trim());
        dict.TryGetValue("discord_id", out var discordIdStr);
        dict.TryGetValue("start_date", out var startStr);
        dict.TryGetValue("end_date", out var endStr);
        dict.TryGetValue("reason", out var reason);

        if (!ulong.TryParse(discordIdStr, out var applicantId))
        {
            await modal.RespondAsync("Discord ID must be numbers only.", ephemeral: true);
            return;
        }
        if (!DateTime.TryParseExact(startStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            await modal.RespondAsync("Start date must be `YYYY-MM-DD`.", ephemeral: true);
            return;
        }
        if (!DateTime.TryParseExact(endStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            await modal.RespondAsync("End date must be `YYYY-MM-DD`.", ephemeral: true);
            return;
        }
        if (end < start)
        {
            await modal.RespondAsync("End date cannot be before start date.", ephemeral: true);
            return;
        }

        var (channelId, approverRoleId, _) = DivisionRoutes[divKey];

        // Insert DB row (PENDING)
        long requestId;
        using (var conn = new MySqlConnection(_mysql))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS loa_request (
                  id BIGINT PRIMARY KEY AUTO_INCREMENT,
                  applicant_discord_id BIGINT NOT NULL,
                  applicant_username VARCHAR(64) NOT NULL,
                  division_key VARCHAR(64) NOT NULL,
                  start_date DATETIME NOT NULL,
                  end_date DATETIME NOT NULL,
                  reason TEXT NULL,
                  status ENUM('PENDING','APPROVED','DECLINED') NOT NULL DEFAULT 'PENDING',
                  approver_discord_id BIGINT NULL,
                  approver_username VARCHAR(64) NULL,
                  approved_at DATETIME NULL,
                  guild_id BIGINT NOT NULL,
                  target_channel_id BIGINT NOT NULL,
                  target_message_id BIGINT NULL,
                  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                
                INSERT INTO loa_request
                (applicant_discord_id, applicant_username, division_key, start_date, end_date, reason, status, guild_id, target_channel_id)
                VALUES (@aid, @auser, @div, @start, @end, @reason, 'PENDING', @gid, @chan);
                SELECT LAST_INSERT_ID();";
            cmd.Parameters.AddWithValue("@aid", (long)applicantId);
            cmd.Parameters.AddWithValue("@auser", modal.User.Username);
            cmd.Parameters.AddWithValue("@div", divKey);
            cmd.Parameters.AddWithValue("@start", DateTime.SpecifyKind(start, DateTimeKind.Utc));
            cmd.Parameters.AddWithValue("@end",   DateTime.SpecifyKind(end,   DateTimeKind.Utc));
            cmd.Parameters.AddWithValue("@reason", string.IsNullOrWhiteSpace(reason) ? (object)DBNull.Value : reason);
            cmd.Parameters.AddWithValue("@gid", (long)_guildId);
            cmd.Parameters.AddWithValue("@chan", (long)channelId);

            var scalar = await cmd.ExecuteScalarAsync();
            requestId = Convert.ToInt64(scalar);
        }

        // Build embed
        var reqEmbed = new EmbedBuilder()
            .WithTitle($"Staff LOA Request #{requestId}")
            .WithColor(Color.Orange)
            .AddField("Applicant", $"<@{applicantId}> (`{applicantId}`)", true)
            .AddField("Division", label, true)
            .AddField("LOA Period", $"**From:** {start:yyyy-MM-dd}\n**To:** {end:yyyy-MM-dd}", true)
            .AddField("Reason (basic)", string.IsNullOrWhiteSpace(reason) ? "_Not provided_" : reason)
            .WithTimestamp(DateTimeOffset.UtcNow);

        var buttons = new ComponentBuilder()
            .WithButton("Approve", $"loa:approve:{requestId}", ButtonStyle.Success)
            .WithButton("Decline", $"loa:decline:{requestId}", ButtonStyle.Danger);

        // Allowed mentions (role only)
        var allowedMentions = new AllowedMentions();
        allowedMentions.RoleIds.Add(approverRoleId);

        // Send to target channel (positional parameters, for older Discord.NET)
        var guild = _client.GetGuild(_guildId);
        var channel = guild?.GetTextChannel(channelId);
        if (channel == null)
        {
            await modal.RespondAsync("Target channel not found; contact admins.", ephemeral: true);
            return;
        }

        var message = await channel.SendMessageAsync(
            $"<@&{approverRoleId}> New LOA request pending review.",
            false,
            reqEmbed.Build(),
            null,
            allowedMentions,
            null,
            buttons.Build()
        );

        // Save message id
        using (var conn = new MySqlConnection(_mysql))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE loa_request SET target_message_id=@mid WHERE id=@id";
            cmd.Parameters.AddWithValue("@mid", (long)message.Id);
            cmd.Parameters.AddWithValue("@id", requestId);
            await cmd.ExecuteNonQueryAsync();
        }

        await modal.RespondAsync("Your LOA request has been submitted for review. You’ll be notified once decided.", ephemeral: true);
    }

    // ========= Approve =========
    private async Task HandleApprove(SocketMessageComponent comp)
    {
        var parts = comp.Data.CustomId.Split(':'); // loa:approve:{id}
        if (parts.Length != 3 || !long.TryParse(parts[2], out var requestId))
        {
            await comp.RespondAsync("Invalid request id.", ephemeral: true);
            return;
        }

        // Load pending request (include dates for DM)
        (ulong applicantId, string divisionKey, ulong channelId, ulong messageId, DateTime startUtc, DateTime endUtc) req;
        using (var conn = new MySqlConnection(_mysql))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT applicant_discord_id, division_key, target_channel_id, target_message_id, start_date, end_date
                                FROM loa_request WHERE id=@id AND status='PENDING' LIMIT 1";
            cmd.Parameters.AddWithValue("@id", requestId);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                await comp.RespondAsync("Request not found or already actioned.", ephemeral: true);
                return;
            }
            req.applicantId = (ulong)r.GetInt64(0);
            req.divisionKey = r.GetString(1);
            req.channelId   = (ulong)r.GetInt64(2);
            req.messageId   = (ulong)r.GetInt64(3);
            req.startUtc    = DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc);
            req.endUtc      = DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc);
        }

        // 🚫 Prevent approving own request
        if (comp.User.Id == req.applicantId)
        {
            await comp.RespondAsync("You can’t approve your own LOA request.", ephemeral: true);
            return;
        }

        if (!DivisionRoutes.TryGetValue(req.divisionKey, out var route))
        {
            await comp.RespondAsync("Division route not found.", ephemeral: true);
            return;
        }
        var divisionLabel = route.Label;

        // Approver role gate (allow division approver OR override)
        if (comp.User is SocketGuildUser guser)
        {
            bool hasDivisionApprover = guser.Roles.Any(x => x.Id == route.ApproverRoleId);
            bool hasOverride = guser.Roles.Any(x => x.Id == OverrideRoleId);
            if (!hasDivisionApprover && !hasOverride)
            {
                await comp.RespondAsync("You don’t have permission to approve this.", ephemeral: true);
                return;
            }
        }

        var guild = _client.GetGuild(_guildId);
        var applicant = guild?.GetUser(req.applicantId);
        var loaRole = guild?.GetRole(_loaRoleId);

        bool roleAssigned = false;
        try
        {
            if (applicant != null && loaRole != null && !applicant.Roles.Any(r => r.Id == _loaRoleId))
            {
                await applicant.AddRoleAsync(loaRole);
                roleAssigned = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[StaffLoa] AddRole failed: {ex}");
            // continue; we still mark as approved, but log outcome below
        }

        // Update DB
        using (var conn = new MySqlConnection(_mysql))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE loa_request
                SET status='APPROVED',
                    approver_discord_id=@uid,
                    approver_username=@uname,
                    approved_at=UTC_TIMESTAMP()
                WHERE id=@id AND status='PENDING'";
            cmd.Parameters.AddWithValue("@uid", (long)comp.User.Id);
            cmd.Parameters.AddWithValue("@uname", comp.User.Username);
            cmd.Parameters.AddWithValue("@id", requestId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Update message (green + decision note)
        var channel = guild?.GetTextChannel(route.ChannelId);
        if (channel != null)
        {
            if (await channel.GetMessageAsync(req.messageId) is IUserMessage msg)
            {
                var eb = (msg.Embeds.FirstOrDefault()?.ToEmbedBuilder() ?? new EmbedBuilder())
                    .WithTitle($"Staff LOA Request #{requestId}")
                    .WithColor(Color.Green)
                    .AddField("Decision", $"✅ Approved by <@{comp.User.Id}>", inline: false);

                await msg.ModifyAsync(m =>
                {
                    m.Content = msg.Content;
                    m.Embed = eb.Build();
                    m.Components = new ComponentBuilder().Build(); // remove buttons
                });
            }
        }

        // 🔔 Log: LOA Approved (with Division)
        await LogLoaEventAsync(
            title: "LOA Approved",
            color: Color.Green,
            buildFields: eb =>
            {
                eb.AddField("Applicant", $"<@{req.applicantId}> ({req.applicantId})", true)
                  .AddField("Approved By", $"<@{comp.User.Id}> ({comp.User.Id})", true)
                  .AddField("Division", divisionLabel, true)
                  .AddField("Period", $"**From:** {req.startUtc:yyyy-MM-dd}\n**To:** {req.endUtc:yyyy-MM-dd}", true);
            });

        // 🔔 Log: LOA Role Assigned / or failed (with Division)
        if (roleAssigned)
        {
            await LogLoaEventAsync(
                title: "LOA Role Assigned",
                color: Color.Blue,
                buildFields: eb =>
                {
                    eb.AddField("User", $"<@{req.applicantId}> ({req.applicantId})", true)
                      .AddField("By", $"<@{comp.User.Id}> ({comp.User.Id})", true)
                      .AddField("Division", divisionLabel, true)
                      .AddField("Role", $"<@&{_loaRoleId}>", true);
                });
        }
        else
        {
            await LogLoaEventAsync(
                title: "LOA Role Assignment Failed",
                color: Color.DarkRed,
                buildFields: eb =>
                {
                    eb.AddField("User", $"<@{req.applicantId}> ({req.applicantId})", true)
                      .AddField("Tried By", $"<@{comp.User.Id}> ({comp.User.Id})", true)
                      .AddField("Division", divisionLabel, true)
                      .AddField("Note", "Check bot permissions and role order.", true);
                });
        }

        // DM applicant (best-effort)
        await NotifyApplicantDm(req.applicantId, approved: true, startUtc: req.startUtc, endUtc: req.endUtc, approver: comp.User);

        await comp.RespondAsync("Approved. LOA role processed and request updated.", ephemeral: true);
    }

    // ========= Decline =========
    private async Task HandleDecline(SocketMessageComponent comp)
    {
        var parts = comp.Data.CustomId.Split(':'); // loa:decline:{id}
        if (parts.Length != 3 || !long.TryParse(parts[2], out var requestId))
        {
            await comp.RespondAsync("Invalid request id.", ephemeral: true);
            return;
        }

        // Load pending request (include dates for DM)
        (ulong applicantId, string divisionKey, ulong channelId, ulong messageId, DateTime startUtc, DateTime endUtc) req;
        using (var conn = new MySqlConnection(_mysql))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT applicant_discord_id, division_key, target_channel_id, target_message_id, start_date, end_date
                                FROM loa_request WHERE id=@id AND status='PENDING' LIMIT 1";
            cmd.Parameters.AddWithValue("@id", requestId);
            using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync())
            {
                await comp.RespondAsync("Request not found or already actioned.", ephemeral: true);
                return;
            }
            req.applicantId = (ulong)r.GetInt64(0);
            req.divisionKey = r.GetString(1);
            req.channelId   = (ulong)r.GetInt64(2);
            req.messageId   = (ulong)r.GetInt64(3);
            req.startUtc    = DateTime.SpecifyKind(r.GetDateTime(4), DateTimeKind.Utc);
            req.endUtc      = DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc);
        }

        // 🚫 Prevent declining own request
        if (comp.User.Id == req.applicantId)
        {
            await comp.RespondAsync("You can’t decline your own LOA request.", ephemeral: true);
            return;
        }

        if (!DivisionRoutes.TryGetValue(req.divisionKey, out var route))
        {
            await comp.RespondAsync("Division route not found.", ephemeral: true);
            return;
        }
        var divisionLabel = route.Label;

        // Approver role gate (allow division approver OR override)
        if (comp.User is SocketGuildUser guser)
        {
            bool hasDivisionApprover = guser.Roles.Any(x => x.Id == route.ApproverRoleId);
            bool hasOverride = guser.Roles.Any(x => x.Id == OverrideRoleId);
            if (!hasDivisionApprover && !hasOverride)
            {
                await comp.RespondAsync("You don’t have permission to decline this.", ephemeral: true);
                return;
            }
        }

        // Update DB
        using (var conn = new MySqlConnection(_mysql))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE loa_request
                SET status='DECLINED',
                    approver_discord_id=@uid,
                    approver_username=@uname,
                    approved_at=UTC_TIMESTAMP()
                WHERE id=@id AND status='PENDING'";
            cmd.Parameters.AddWithValue("@uid", (long)comp.User.Id);
            cmd.Parameters.AddWithValue("@uname", comp.User.Username);
            cmd.Parameters.AddWithValue("@id", requestId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Update message (red + decision note)
        var guild = _client.GetGuild(_guildId);
        var channel = guild?.GetTextChannel(route.ChannelId);
        if (channel != null)
        {
            if (await channel.GetMessageAsync(req.messageId) is IUserMessage msg)
            {
                var eb = (msg.Embeds.FirstOrDefault()?.ToEmbedBuilder() ?? new EmbedBuilder())
                    .WithTitle($"Staff LOA Request #{requestId}")
                    .WithColor(Color.Red)
                    .AddField("Decision", $"❌ Declined by <@{comp.User.Id}>", inline: false);

                await msg.ModifyAsync(m =>
                {
                    m.Content = msg.Content;
                    m.Embed = eb.Build();
                    m.Components = new ComponentBuilder().Build();
                });
            }
        }

        // 🔔 Log: LOA Declined (with Division)
        await LogLoaEventAsync(
            title: "LOA Declined",
            color: Color.Red,
            buildFields: eb =>
            {
                eb.AddField("Applicant", $"<@{req.applicantId}> ({req.applicantId})", true)
                  .AddField("Declined By", $"<@{comp.User.Id}> ({comp.User.Id})", true)
                  .AddField("Division", divisionLabel, true)
                  .AddField("Requested Period", $"**From:** {req.startUtc:yyyy-MM-dd}\n**To:** {req.endUtc:yyyy-MM-dd}", true);
            });

        // DM applicant (best-effort) with leadership note
        await NotifyApplicantDm(req.applicantId, approved: false, startUtc: req.startUtc, endUtc: req.endUtc, approver: comp.User);

        await comp.RespondAsync("Declined. Request updated.", ephemeral: true);
    }
}
