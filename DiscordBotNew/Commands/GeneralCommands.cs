﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using DiscordBotNew.CommandLoader;
using DiscordBotNew.CommandLoader.CommandContext;
using DiscordBotNew.CommandLoader.CommandResult;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json.Linq;

namespace DiscordBotNew.Commands
{
    public static class GeneralCommands
    {
        private static Random random = new Random();

        [Command("hello", "test"), HelpText("Says hi")]
        public static ICommandResult Hello(ICommandContext context) => new SuccessResult("Hello there! :hand_splayed:");

        [Command("echo", "say"), HelpText("Repeats the provided text back to you")]
        public static ICommandResult Echo(ICommandContext context, [JoinRemainingParameters, HelpText("The message to repeat")] string text) => new SuccessResult(text);

        [Command("8ball"), HelpText("It knows your future")]
        public static ICommandResult Magic8Ball(ICommandContext context, [JoinRemainingParameters, HelpText("A yes or no question")] string question)
        {
            string[] responses = {
                "It is certain",
                "It is decidedly so",
                "Without a doubt",
                "Yes, definitely",
                "You may rely on it",
                "As I see it, yes",
                "Most likely",
                "Outlook good",
                "Yes",
                "Signs point to yes",
                "Reply hazy try again",
                "Ask again later",
                "Better not tell you now",
                "Cannot predict now",
                "Concentrate and ask again",
                "Don't count on it",
                "My reply is no",
                "My sources say no",
                "Outlook not so good",
                "Very doubtful"
            };

            var messageContext = context as DiscordMessageContext;
            if (messageContext != null)
                return new SuccessResult($"<@{messageContext.MessageAuthor.Id}>: ***{question}***\n" + responses[random.Next(responses.Length)]);
            return new SuccessResult(responses[random.Next(responses.Length)]);
        }

        [Command("setprefix"), HelpText("Sets the command prefix for this DM channel or server"), Permissions(guildPermissions: new[] { GuildPermission.ManageGuild })]
        public static ICommandResult SetPrefix(DiscordUserMessageContext context, [JoinRemainingParameters, HelpText("The new prefix for this channel or server (up to 16 characters)")] string prefix)
        {
            if (prefix.Length > 16)
            {
                return new ErrorResult("Prefix must be no more than 16 characters in length");
            }

            bool server = false;
            ulong id;
            if (context.ChannelType == ChannelType.Text)
            {
                id = context.Guild?.Id ?? context.Channel.Id;
                server = context.Guild != null;
            }
            else
            {
                id = context.Channel.Id;
            }

            if (context.Bot.Settings.GetSetting("customPrefixes", out Dictionary<ulong, string> prefixes))
            {
                if (prefixes.ContainsKey(id))
                {
                    prefixes[id] = prefix;
                }
                else
                {
                    prefixes.Add(id, prefix);
                }

                context.Bot.Settings.AddSetting("customPrefixes", prefixes);
            }
            else
            {
                context.Bot.Settings.AddSetting("customPrefixes", new Dictionary<ulong, string>
                {
                    { id, prefix }
                });
            }

            context.Bot.Settings.SaveSettings();

            return new SuccessResult($"Prefix set to `{prefix}` for this {(server ? "server" : "channel")}");
        }

        [Command("quote", "byid"), HelpText("Quotes the message with the provided ID number"), CommandScope(ChannelType.Text)]
        public static async Task<ICommandResult> Quote(DiscordMessageContext context, [DisplayName("message ID"), HelpText("The message to quote")] ulong id, [DisplayName("channel mention"), HelpText("The channel to search")] string channel = null)
        {
            IMessageChannel messageChannel = context.Channel;
            if (context.Message.MentionedChannelIds.Count != 0)
            {
                messageChannel = (IMessageChannel)context.Bot.Client.GetChannel(context.Message.MentionedChannelIds.First());
            }

            if (!((IGuildUser)context.MessageAuthor).GetPermissions((IGuildChannel)messageChannel).ReadMessageHistory)
            {
                return new ErrorResult("You do not have permission to read messages in that channel", "Permissions Error");
            }

            IMessage msg = await messageChannel.GetMessageAsync(id);

            if (msg == null) return new ErrorResult("Message not found");

            var builder = new EmbedBuilder
            {
                Author = new EmbedAuthorBuilder
                {
                    Name = msg.Author.NicknameOrUsername(),
                    IconUrl = msg.Author.AvatarUrlOrDefaultAvatar()
                },
                Timestamp = msg.Timestamp,
                Footer = new EmbedFooterBuilder
                {
                    Text = $"#{messageChannel.Name}"
                }
            };

            switch (msg.Type)
            {
                case MessageType.Default:
                    builder.Description = msg.Content;
                    break;
                case MessageType.RecipientAdd:
                    builder.Description = $"{msg.Author.Username} added a user to the group";
                    break;
                case MessageType.RecipientRemove:
                    builder.Description = $"{msg.Author.Username} removed a user from the group";
                    break;
                case MessageType.Call:
                    builder.Description = $"{msg.Author.Username} started a call";
                    break;
                case MessageType.ChannelNameChange:
                    builder.Description = $"{msg.Author.Username} changed the name of the channel";
                    break;
                case MessageType.ChannelIconChange:
                    builder.Description = $"{msg.Author.Username} changed the channel icon";
                    break;
                case MessageType.ChannelPinnedMessage:
                    builder.Description = $"{msg.Author.Username} pinned a message to the channel";
                    break;
            }

            if (msg.Attachments.Count > 0)
            {
                builder.ImageUrl = msg.Attachments.First().Url;
                builder.Title = msg.Attachments.First().Filename;
                builder.Url = builder.ImageUrl;
            }

            foreach (Embed embed in msg.Embeds.Where(x => x.Type == EmbedType.Rich))
            {
                await context.Channel.SendMessageAsync("", embed: embed);
            }

            return new SuccessResult(embed: builder);
        }

        [Command("countdown"), HelpText("Creates, edits, or deletes a countdown")]
        public static ICommandResult Countdown(ICommandContext context, CountdownAction action, string name, [HelpText("The time to count down to")] DateTime? date = null, [DisplayName("Windows TimeZone ID"), JoinRemainingParameters] string timezone = "Pacific Standard Time")
        {
            IGuildChannel channel;
            switch (context)
            {
                case DiscordMessageContext message:
                    channel = (IGuildChannel)message.Channel;
                    break;
                case DiscordChannelDescriptionContext desc:
                    channel = desc.Channel;
                    break;
                default:
                    return new ErrorResult($"The `countdown` command is not valid in the context `{context.GetType().Name}`");
            }

            var countdowns = context.Bot.Countdowns.GetSetting(channel.GuildId.ToString(), out Dictionary<string, DateTimeOffset> cd) ? cd : new Dictionary<string, DateTimeOffset>();
            switch (action)
            {
                case CountdownAction.Create:
                    if (countdowns.ContainsKey(name))
                    {
                        return new ErrorResult($"The countdown with the name {name} already exists");
                    }
                    break;
                case CountdownAction.Edit:
                    if (!countdowns.ContainsKey(name))
                    {
                        return new ErrorResult($"The countdown with the name {name} does not exist");
                    }
                    break;
                case CountdownAction.Delete:
                    if (!countdowns.ContainsKey(name)) return new ErrorResult($"The countdown with the name {name} does not exist");
                    countdowns.Remove(name);
                    context.Bot.Countdowns.AddSetting(channel.GuildId.ToString(), countdowns);
                    context.Bot.Countdowns.SaveSettings();
                    return new SuccessResult($"Successfully deleted countdown {name}");
            }

            if (!date.HasValue) return new ErrorResult("Please provide a date when creating or editing a countdown");
            var convertedTime = new DateTimeOffset(date.Value, TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.Now, timezone).Offset);
            if (countdowns.ContainsKey(name))
            {
                countdowns[name] = convertedTime;
            }
            else
            {
                countdowns.Add(name, convertedTime);
            }
            context.Bot.Countdowns.AddSetting(channel.GuildId.ToString(), countdowns);
            context.Bot.Countdowns.SaveSettings();

            TimeSpan difference = countdowns[name] - DateTimeOffset.Now;
            return new SuccessResult($"{difference.ToLongString()} until {name}");
        }

        [Command("countdown"), HelpText("Views the status of a countdown timer")]
        public static ICommandResult Countdown(ICommandContext context, string name)
        {
            IGuildChannel channel;
            switch (context)
            {
                case DiscordMessageContext message:
                    channel = (IGuildChannel)message.Channel;
                    break;
                case DiscordChannelDescriptionContext desc:
                    channel = desc.Channel;
                    break;
                default:
                    return new ErrorResult($"The `countdown` command is not valid in the context `{context.GetType().Name}`");
            }

            var countdowns = context.Bot.Countdowns.GetSetting(channel.GuildId.ToString(), out Dictionary<string, DateTimeOffset> cd) ? cd : new Dictionary<string, DateTimeOffset>();

            if (!countdowns.ContainsKey(name))
            {
                return new ErrorResult($"No countdown with the name {name} was found. Try creating it.");
            }

            TimeSpan difference = countdowns[name] - DateTimeOffset.Now;

            return new SuccessResult($"{difference.ToLongString()} until {name}");
        }

        [Command("back"), HelpText("Creates a backronym from the given text")]
        public static ICommandResult Back(ICommandContext context, string acronym, [HelpText("The number of backronyms to generate (up to 10)")]byte count = 1, [HelpText("Whether or not to use the larger English dictionary")] bool useComplexWords = false)
        {
            count = count < 1 ? (byte)1 : count > 10 ? (byte)10 : count;

            string backronym = "";
            for (int i = 0; i < count; i++)
            {
                backronym += acronym.ToUpper() + ": ";
                foreach (char c in acronym)
                {
                    if (File.Exists($"{(useComplexWords ? "" : "simple")}words\\{char.ToLower(c)}.txt"))
                    {
                        string[] words = File.ReadAllLines($"{(useComplexWords ? "" : "simple")}words\\{char.ToLower(c)}.txt");
                        string word = words[random.Next(words.Length)];
                        if (word.Length > 1)
                            backronym += char.ToUpper(word[0]) + word.Substring(1) + " ";
                        else
                            backronym += char.ToUpper(word[0]) + " ";
                    }
                }

                backronym += "\n";
            }

            return new SuccessResult(backronym);
        }

        [Command("status"), HelpText("Gets when a user was last online")]
        public static async Task<ICommandResult> Status(ICommandContext context, [DisplayName("username | @ mention"), JoinRemainingParameters] string user)
        {
            IUser targetUser;

            switch (context)
            {
                case DiscordMessageContext discordContext:
                    if (discordContext.Message.MentionedUserIds.Count > 0)
                    {
                        targetUser = await discordContext.Channel.GetUserAsync(discordContext.Message.MentionedUserIds.First());
                    }
                    else if (discordContext.Message.Tags.Count > 0 && discordContext.Message.Tags.First().Type == TagType.UserMention && (targetUser = context.Bot.Client.GetUser(discordContext.Message.Tags.First().Key)) != null)
                    {
                    }
                    else
                    {
                        targetUser = (SocketUser)await CommandTools.GetUserByUsername(user, discordContext.Channel);
                    }
                    break;
                case DiscordChannelDescriptionContext channelDescription:
                    targetUser = (SocketUser)await CommandTools.GetUserByUsername(user, (IMessageChannel)channelDescription.Channel);
                    break;
                default:
                    return new ErrorResult($"The `status` command is not valid in the context `{context.GetType().Name}`");
            }

            if (targetUser == null || !context.Bot.UserStatuses.GetSetting("statuses", out Dictionary<ulong, UserStatusInfo> statuses))
            {
                return new ErrorResult("No status history data found");
            }

            if (statuses.ContainsKey(targetUser.Id))
            {
                var statusInfo = statuses[targetUser.Id];

                Color? color;
                switch (targetUser.Status)
                {
                    case UserStatus.Online:
                        color = new Color(76, 175, 80);
                        break;
                    case UserStatus.Idle:
                    case UserStatus.AFK:
                        color = new Color(255, 235, 59);
                        break;
                    case UserStatus.DoNotDisturb:
                        color = new Color(244, 67, 54);
                        break;
                    default:
                        color = null;
                        break;
                }

                EmbedBuilder statusEmbed = new EmbedBuilder
                {
                    Author = new EmbedAuthorBuilder
                    {
                        Name = targetUser.NicknameOrUsername(),
                        IconUrl = targetUser.AvatarUrlOrDefaultAvatar()
                    },
                    Color = color,
                    Description = targetUser.Status.ToString()
                };

                statusEmbed.AddInlineField($"{targetUser.Status} For", (DateTimeOffset.Now - statusInfo.StatusLastChanged).ToLongString());
                if (targetUser.Status != UserStatus.Online)
                    statusEmbed.AddInlineField("Last Online", $"{(DateTimeOffset.Now - statusInfo.LastOnline).ToLongString()} ago");
                else if (statusInfo.Game != null)
                    statusEmbed.AddInlineField($"In Game {statusInfo.Game}", (DateTimeOffset.Now - statusInfo.StartedPlaying)?.ToLongString() ?? "Unknown amount of time");

                return new SuccessResult(embed: statusEmbed);
            }

            return new ErrorResult("User status history not found");
        }

        [Command("dynamicmessage"), HelpText("Creates a message that runs a command automatically every specified number of minutes"), CommandScope(ChannelType.Text), Permissions(channelPermissions: new[] { ChannelPermission.ManageMessages })]
        public static async Task<ICommandResult> DynamicMessage(DiscordUserMessageContext context, [DisplayName("interval (minutes)"), HelpText("How often to run the command")] ulong interval, [JoinRemainingParameters, HelpText("The command to run")] string command)
        {
            if (interval == 0) return new ErrorResult("Interval must be greater than zero");

            var message = await context.Channel.SendMessageAsync($"Loading dynamic message with command '{command}'");

            List<DynamicMessage> dynamicMessages = context.Bot.DynamicMessages.GetSetting("messages", out dynamicMessages) ? dynamicMessages : new List<DynamicMessage>();
            dynamicMessages.Add(new DynamicMessage
            {
                GuildId = context.Guild.Id,
                ChannelId = message.Channel.Id,
                MessageId = message.Id,
                UpdateInterval = interval,
                CommandText = command
            });
            context.Bot.DynamicMessages.AddSetting("messages", dynamicMessages);
            context.Bot.DynamicMessages.SaveSettings();

            await message.ModifyAsync(msg => msg.Content = "Loading complete, this message will be updated with dynamic content Soon:tm:");

            var dynamicMessageContext = new DiscordDynamicMessageContext(message, context.Bot, command);
            await CommandRunner.Run(command, dynamicMessageContext, CommandTools.GetCommandPrefix(dynamicMessageContext, message.Channel), false);

            return new SuccessResult();
        }

        [Command("cat", "floof"), HelpText("Cat.")]
        public static async Task<ICommandResult> Cat(ICommandContext context)
        {
            JObject obj;
            string catUrl;
            var client = new HttpClient();
            do
            {
                string json = await client.GetStringAsync("http://random.cat/meow");
                obj = JObject.Parse(json);
            } while ((catUrl = obj["file"].Value<string>()).EndsWith(".gif"));
            return new SuccessResult(catUrl);
        }

        [Command("lmgtfy"), HelpText("For when people forget how to use a search engine")]
        public static ICommandResult Lmgtfy(ICommandContext context, [JoinRemainingParameters] string query) => new SuccessResult("http://lmgtfy.com/?q=" + Uri.EscapeDataString(query));

        [Command("ping"), HelpText("Displays the bot's latency")]
        public static ICommandResult Ping(ICommandContext context) => new SuccessResult($"{context.Bot.Client.Latency} ms estimated round trip latency");

        [Command("coin", "flip", "coinflip", "decide"), HelpText("Flips a coin")]
        public static ICommandResult Coin(ICommandContext context, string side1 = "Heads", string side2 = "Tails") => new SuccessResult(random.Next(2) == 0 ? side1 : side2);

        [Command("timestamp"), HelpText("Displays the timestamp of a message"), CommandScope(ChannelType.Text)]
        public static async Task<ICommandResult> Timestamp(DiscordMessageContext context, [DisplayName("message ID")] ulong messageId, [DisplayName("channel mention")] string channel, [DisplayName("Windows TimeZone ID")] string timeZone = "Pacific Standard Time")
        {
            IMessage message;
            if (context.Message.MentionedChannelIds.Count < 1)
            {
                message = await context.Channel.GetMessageAsync(messageId);
            }
            else
            {
                message = await ((IMessageChannel)await context.Guild.GetChannelAsync(context.Message.MentionedChannelIds.First())).GetMessageAsync(messageId);
            }

            DateTimeOffset timestamp = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(message.Timestamp, timeZone);
            return new SuccessResult(timestamp.ToString());
        }

        public enum CountdownAction
        {
            [HelpText("Creates a new countdown")] Create,
            [HelpText("Changes the time of an existing countdown")] Edit,
            [HelpText("Deletes an existing countdown")] Delete
        }
    }
}
