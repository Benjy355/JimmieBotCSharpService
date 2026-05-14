using Discord;
using Discord.Net;
using Discord.WebSocket;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Images;
using OpenAI.Realtime;
using OpenAI.Responses;
using OpenAI.VectorStores;
using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


#pragma warning disable OPENAI001

namespace JimmieBot_CSharpService
{
    internal class JimmieBot
    {
        private static DiscordSocketClient dClient;
        private static OpenAIClient OAIClient;
        private static ChatClient CClient;
        private static ChatTool ImageInPaint;

        public async static void InPaintImage(ChatToolCall toolCall, SocketUserMessage message, IMessage replyMessage)
        {
            JsonDocument argJson = JsonDocument.Parse(toolCall.FunctionArguments);
            String editInstructions = argJson.RootElement.GetProperty("editInstructions").GetString();
            List<Uri> imageUris = new List<Uri>();

            // Put all the image URLs into a  list to pass them over
            foreach (Discord.Attachment attachment in message.Attachments)
            {
                if (attachment.ContentType != null && (attachment.ContentType.StartsWith("image/")))
                {
                    Uri uri;
                    if (Uri.TryCreate(attachment.Url, UriKind.Absolute, out uri))
                    {
                        imageUris.Add(uri);
                    }
                }
            }
            if (replyMessage != null)
            {
                foreach (Discord.Attachment attachment in replyMessage.Attachments)
                {
                    if (attachment.ContentType != null && (attachment.ContentType.StartsWith("image/")))
                    {
                        Uri uri;
                        if (Uri.TryCreate(attachment.Url, UriKind.Absolute, out uri))
                        {
                            imageUris.Add(uri);
                        }
                    }
                }
            }
            if (imageUris.Count == 0)
            {
                await message.ReplyAsync(text: "No images were detected attached");
                return;
            }

            IUserMessage botReply = await message.ReplyAsync("Working on it...");

            // Download the attachment to a temporary location, and then pass the file path to the image edit endpoint.
            HttpClient httpClient = new HttpClient();
            //List<Stream> imageDownloadStreams = new List<Stream>(); // await httpClient.GetStreamAsync(imageUris[0]);
            //await imageDownloadStream.CopyToAsync(imageDownloadMemoryStream);
            
            var formData = new MultipartFormDataContent();
            formData.Add(new StringContent("gpt-image-2"), "model");

            //Try running it with a straight HTTP request
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {RegConfig.GetConfig(RegConfigItems.OpenAIToken, "NULL").ToString()}");
            httpClient.Timeout = TimeSpan.FromMinutes(15);
            int i = 0;
            foreach (Uri uri in imageUris)
            {
                Stream imageDownloadStream = await httpClient.GetStreamAsync(uri);
                MemoryStream tempMemStream = new MemoryStream();
                await imageDownloadStream.CopyToAsync(tempMemStream);
                tempMemStream.Position = 0;

                formData.Add(
                    new StreamContent(tempMemStream),
                    "image[]",
                    $"user_content_{i}.png"
                );
                i++;
            }

            formData.Add(new StringContent(editInstructions), "prompt");

            String formAsString = await formData.ReadAsStringAsync();

            HttpResponseMessage APICallResult;

            try
            {
                APICallResult = await httpClient.PostAsync("https://api.openai.com/v1/images/edits", formData);
            } catch (TaskCanceledException e)
            {
                await botReply.ModifyAsync(msg =>
                {
                    msg.Content = "The image edit took too long and timed out.";
                });
                return;
            }

            if (APICallResult == null)
            {
                return;
            }

            if (APICallResult.IsSuccessStatusCode)
            {
                String responseString = await APICallResult.Content.ReadAsStringAsync();
                var jsonData = JsonDocument.Parse(responseString);
                var base64Image = jsonData.RootElement
                                .GetProperty("data")[0]
                                .GetProperty("b64_json")
                                .GetString();

                byte[] imageBytes = Convert.FromBase64String(base64Image);

                //await message.Channel.SendFileAsync(new MemoryStream(imageBytes), $"edited_image_{message.Id.ToString()}.png");
                await botReply.ModifyAsync(msg =>
                {
                    msg.Content = "";
                    msg.Attachments = new Optional<IEnumerable<FileAttachment>>(new List<FileAttachment> { new FileAttachment(new MemoryStream(imageBytes), $"edited_image_{message.Id.ToString()}.png") });
                });
            }
            else
            {
                await message.ReplyAsync(text: $"API call failed with status code {APICallResult.StatusCode} and response:\n{await APICallResult.Content.ReadAsStringAsync()}");
            }
        }
        public static async Task Start(CancellationToken ct)
        {
#if DEBUG
            await LoggingHandler.LogAsync("JimmieBot is starting in DEBUG mode");
#else
            await LoggingHandler.LogAsync("JimmieBot is starting");
#endif

            // Initialize OpenAI client
            OAIClient = new OpenAIClient(RegConfig.GetConfig(RegConfigItems.OpenAIToken, "NULL").ToString());
            CClient = OAIClient.GetChatClient(RegConfig.GetConfig(RegConfigItems.OpenAIModel, "NULL").ToString());

            // Continue!

            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
            };

            // Init our Chat Tool(s)
            ImageInPaint = ChatTool.CreateFunctionTool(nameof(InPaintImage),
                "Edit the attached image based on the provided instructions.",
                BinaryData.FromBytes(Encoding.UTF8.GetBytes("{\"type\":\"object\",\"properties\":{\"editInstructions\":{\"type\":\"string\",\"description\":\"The changes the user requested to make to the image. DO NOT PARAPHRASE\"}}}")));

            dClient = new DiscordSocketClient(config);

            dClient.Log += LoggingHandler.Discord_LogAsync;
            dClient.Ready += ReadyASync;
            dClient.MessageReceived += MessageReceivedAsync;

            await dClient.LoginAsync(TokenType.Bot, RegConfig.GetConfig(RegConfigItems.DiscordToken, "").ToString());

            await dClient.StartAsync();

            while (true)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(500);
                }
                catch (TaskCanceledException)
                {
                    await LoggingHandler.LogAsync("Service is shutting down, disconnecting bot");
                    await dClient.LogoutAsync();
                    await dClient.StopAsync();
                    await LoggingHandler.LogAsync("Bot disconnected");
                }
            }
        }

        private static readonly String defaultInstructions = "You are JimmieBot, a Discord bot that can use markdown and emojis. Users you chat with are Canadian so their questions should be answered in metric units and using Canadian spelling. Also questions about laws or norms would be specific to Canada.";

        // Injects a couple bits of info to the prompt
        private static String GetModelInstructions(SocketMessage message)
        {

            String result = RegConfig.GetConfig(RegConfigItems.ModelInstructions, defaultInstructions) as String;
            if (message.Author is SocketGuildUser guildUser && guildUser.DisplayName != null)
            {
                result += $"\nUser you are chatting with has the nickname: {guildUser.DisplayName}\n";
            } else 
            { 
                result += $"\nUser you are chatting with: {message.Author.GlobalName}\n";
            }


             return result;
        }

        public static bool IsChannelAllowedForAI(SocketMessage message)
        {
            String allowedChannelsConfig = RegConfig.GetConfig(RegConfigItems.AllowedAIChannels, "").ToString();
            if (String.IsNullOrEmpty(allowedChannelsConfig))
            {
                // Default is no.
                return false;
            }
            var allowedChannelIds = allowedChannelsConfig.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                         .Select(idStr => idStr.Trim())
                                                         .Where(idStr => ulong.TryParse(idStr, out _))
                                                         .Select(idStr => ulong.Parse(idStr))
                                                         .ToHashSet();
            if (message.Channel is SocketGuildChannel guildChannel)
            {
                return allowedChannelIds.Contains(guildChannel.Id);
            }
            else if (message.Channel is SocketDMChannel)
            {
                // DMs are always allowed
                return true;
            }
            return false;
        }

        private static async Task ReadyASync()
        {
            await LoggingHandler.LogAsync($"{dClient.CurrentUser} is connected!");

            try
            {
                var allowAICommand = new SlashCommandBuilder();
                allowAICommand.WithName("allowaichannel");
                allowAICommand.WithDescription("Allow this channel to use AI features.");

                await dClient.CreateGlobalApplicationCommandAsync(allowAICommand.Build());
            }
            catch (HttpException e)
            {
                await LoggingHandler.LogAsync($"Failed to create slash command: {e.Message}", EventLogEntryType.Error);
            }
            catch (Exception ex) {
                await LoggingHandler.LogAsync($"Failed to build slash command: {ex.Message}", EventLogEntryType.Error);
            }

            try { 
                var disallowAICommand = new SlashCommandBuilder();
                disallowAICommand.WithName("disallowaichannel");
                disallowAICommand.WithDescription("Disallow this channel from using AI features.");
                await dClient.CreateGlobalApplicationCommandAsync(disallowAICommand.Build());
            }
            catch (HttpException e)
            {
                await LoggingHandler.LogAsync($"Failed to create slash command: {e.Message}", EventLogEntryType.Error);
            }
            catch (Exception ex)
            {
                await LoggingHandler.LogAsync($"Failed to build slash command: {ex.Message}", EventLogEntryType.Error);
            }

            try
            {
                var timeCommand = new SlashCommandBuilder();
                timeCommand.WithName("time");
                timeCommand.WithDescription("Gives you a time stamp code from your date time input (MM/DD/YYYY and/or # AM/PM)");
                timeCommand.AddOption("datetime", ApplicationCommandOptionType.String, "The date and/or time you want to convert to a timestamp code.", isRequired: true);
                timeCommand.AddOption("format", ApplicationCommandOptionType.String, "The format code you want to use (see Discord docs for options). Default is 's' (short date/time).", isRequired: false);

                await dClient.CreateGlobalApplicationCommandAsync(timeCommand.Build());
            }
            catch (HttpException e)
            {
                await LoggingHandler.LogAsync($"Failed to create slash command: {e.Message}", EventLogEntryType.Error);
            }
            catch (Exception ex)
            {
                await LoggingHandler.LogAsync($"Failed to build slash command: {ex.Message}", EventLogEntryType.Error);
            }

            try
            {
                var getPromptCommand = new SlashCommandBuilder();
                getPromptCommand.WithName("getprompt");
                getPromptCommand.WithDescription("Get the current instructions prompt that the AI is using.");
                await dClient.CreateGlobalApplicationCommandAsync(getPromptCommand.Build());
            }
            catch (HttpException e)
            {
                await LoggingHandler.LogAsync($"Failed to create slash command: {e.Message}", EventLogEntryType.Error);
            }
            catch (Exception ex)
            {
                await LoggingHandler.LogAsync($"Failed to build slash command: {ex.Message}", EventLogEntryType.Error);
            }

            try
            {
                var setPromptComand = new SlashCommandBuilder();
                setPromptComand.WithName("setprompt");
                setPromptComand.WithDescription("Set the instructions prompt that the AI uses.");
                setPromptComand.AddOption("prompt", ApplicationCommandOptionType.String, "The instructions prompt to use for the AI.", isRequired: true);
                await dClient.CreateGlobalApplicationCommandAsync(setPromptComand.Build());
            }
            catch (HttpException e)
            {
                await LoggingHandler.LogAsync($"Failed to create slash command: {e.Message}", EventLogEntryType.Error);
            }
            catch (Exception ex)
            {
                await LoggingHandler.LogAsync($"Failed to build slash command: {ex.Message}", EventLogEntryType.Error);
            }


            dClient.SlashCommandExecuted += SlashCommandHandler;
        }

        public static async Task SlashCommandHandler(SocketSlashCommand command)
        {
            switch (command.Data.Name)
            {
                case "allowaichannel":
                    await allowAICommand_Handler(command);
                    break;
                case "disallowaichannel":
                    await disallowAICommand_Handler(command);
                    break;
                case "time":
                    await timeCommand_Handler(command);
                    break;
                case "getprompt":
                    await getPrompt_Handler(command);
                    break;
                case "setprompt":
                    await setPrompt_Handler(command);
                    break;
                default:
                    await command.RespondAsync($"Unknown command: {command.Data.Name}", ephemeral: true);
                    break;
            }
        }

        public static async Task allowAICommand_Handler(SocketSlashCommand command)
        {
            var user = command.User as SocketGuildUser;
            if (user == null)
            {
                return;
            }
            if (!user.GuildPermissions.Administrator)
            {
                await command.RespondAsync("You must be an administrator to use this command.", ephemeral: true);
                return;
            }

            String allowedChannelsConfig = RegConfig.GetConfig(RegConfigItems.AllowedAIChannels, "").ToString();
            var allowedChannelIds = allowedChannelsConfig.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                         .Select(idStr => idStr.Trim())
                                                         .Where(idStr => ulong.TryParse(idStr, out _))
                                                         .Select(idStr => ulong.Parse(idStr))
                                                         .ToHashSet();

            if (allowedChannelIds.Contains(command.InteractionChannel.Id))
            {
                await command.RespondAsync("This channel is already allowed for AI features.", ephemeral: true);
                return;
            } else
            {
                String newAllowedChannelsConfig = allowedChannelsConfig += $",{command.InteractionChannel.Id.ToString()}";
                RegConfig.SetConfig(RegConfigItems.AllowedAIChannels, newAllowedChannelsConfig);
                await command.RespondAsync("Done!", ephemeral: true);
            }
        }
        public static async Task disallowAICommand_Handler(SocketSlashCommand command)
        {
            var user = command.User as SocketGuildUser;
            if (user == null)
            {
                return;
            }
            if (!user.GuildPermissions.Administrator)
            {
                await command.RespondAsync("You must be an administrator to use this command.", ephemeral: true);
                return;
            }

            String allowedChannelsConfig = RegConfig.GetConfig(RegConfigItems.AllowedAIChannels, "").ToString();
            var allowedChannelIds = allowedChannelsConfig.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                         .Select(idStr => idStr.Trim())
                                                         .Where(idStr => ulong.TryParse(idStr, out _))
                                                         .Select(idStr => ulong.Parse(idStr))
                                                         .ToHashSet();

            if (!allowedChannelIds.Contains(command.InteractionChannel.Id))
            {
                await command.RespondAsync("This channel is not currently allowed for AI features.", ephemeral: true);
                return;
            }
            else
            {
                allowedChannelIds.Remove(command.InteractionChannel.Id);
                String newAllowedChannelsConfig = String.Join(",", allowedChannelIds.Select(id => id.ToString()));
                RegConfig.SetConfig(RegConfigItems.AllowedAIChannels, newAllowedChannelsConfig);
                await command.RespondAsync("Done!", ephemeral: true);
            }
        }

        public static async Task getPrompt_Handler(SocketSlashCommand command)
        {
            var user = command.User as SocketGuildUser;
            if (user == null)
            {
                return;
            }
            if (!user.GuildPermissions.Administrator)
            {
                await command.RespondAsync("You must be an administrator to use this command.", ephemeral: true);
                return;
            }

            await command.RespondAsync(RegConfig.GetConfig(RegConfigItems.ModelInstructions, defaultInstructions).ToString(), ephemeral: true);
        }

        public static async Task setPrompt_Handler(SocketSlashCommand command)
        {
            var user = command.User as SocketGuildUser;
            if (user == null)
            {
                return;
            }
            if (!user.GuildPermissions.Administrator)
            {
                await command.RespondAsync("You must be an administrator to use this command.", ephemeral: true);
                return;
            }
            var promptOption = command.Data.Options.FirstOrDefault(opt => opt.Name == "prompt");
            if (promptOption == null || promptOption.Value == null)
            {
                await command.RespondAsync("You must provide a valid prompt string.", ephemeral: true);
                return;
            }
            String promptString = promptOption.Value.ToString();
            RegConfig.SetConfig(RegConfigItems.ModelInstructions, promptString);
            await command.RespondAsync("Done!", ephemeral: true);
        }

        public static async Task timeCommand_Handler(SocketSlashCommand command)
        {
            // Try to parse their datetime from the command option, if it fails just let them know.
            var dateTimeOption = command.Data.Options.FirstOrDefault(opt => opt.Name == "datetime");
            if (dateTimeOption == null || dateTimeOption.Value == null)
            {
                await command.RespondAsync("You must provide a valid date/time string.", ephemeral: true);
                return;
            }
            String dateTimeString = dateTimeOption.Value.ToString();
            DateTime parsedDateTime;
            if (!DateTime.TryParse(dateTimeString, out parsedDateTime))
            {
                await command.RespondAsync("Failed to parse the date/time string you provided. Please use a valid format (e.g., MM/DD/YYYY HH:MM AM/PM).", ephemeral: true);
                return;
            }
            // Get the format option, default to 's' if not provided.
            var formatOption = command.Data.Options.FirstOrDefault(opt => opt.Name == "format");
            String formatString = "s"; // Default
            if (formatOption != null && formatOption.Value != null)
            {
                formatString = formatOption.Value.ToString();
            }
            // Create the Discord timestamp code
            String timestampCode = $"<t:{new DateTimeOffset(parsedDateTime).ToUnixTimeSeconds()}:{formatString}>";
            await command.RespondAsync($"Here is your timestamp code: ```{timestampCode}```\nIt will look like this {timestampCode}", ephemeral: true);
        }

        private static Task MessageReceivedAsync(SocketMessage message)
        {
            // Ignore bots
            if (!(message is SocketUserMessage userMessage))
                return Task.CompletedTask;
            if (message.Author.IsBot)
                return Task.CompletedTask;

            var botId = dClient.CurrentUser?.Id ?? 0UL;
            bool isMentioned = message.MentionedUserIds != null && message.MentionedUserIds.Contains(botId);

            // Is the message a reply to us?
            bool isReply = message.Reference != null && message.Reference.MessageId.IsSpecified;
            IMessage replyMessage = null;
            if (isReply)
            {
                replyMessage = message.Channel.GetMessageAsync(message.Reference.MessageId.Value).Result;
                if (replyMessage == null || replyMessage.Author.Id != botId)
                {
                    isReply = false;
                }
            }

            if (message.Channel is SocketDMChannel)
            {
                // DMs are always allowed
                isMentioned = true;
            }

            if ((isMentioned || isReply) && IsChannelAllowedForAI(message))
            {
                // Start OpenAI response asynchronously
                _ = Task.Run( async () => { await StartOpenAIResponseASync(userMessage, isReply, replyMessage); });
            }

            return Task.CompletedTask;
        }

        private static List<String> BreakStringIntoChunks(String input)
        {
            const int MaxChunkSize = 2000;
            var result = new List<string>();

            if (input == null)
            {
                // Return one empty item to match expectation of a single-item list when "less than 2000 characters"
                result.Add(string.Empty);
                return result;
            }

            if (input.Length <= MaxChunkSize)
            {
                result.Add(input);
                return result;
            }

            int start = 0;
            int totalLength = input.Length;

            while (start < totalLength)
            {
                int remaining = totalLength - start;
                if (remaining <= MaxChunkSize)
                {
                    // final chunk
                    result.Add(input.Substring(start, remaining));
                    break;
                }

                // candidate end index (exclusive)
                int end = start + MaxChunkSize;
                // Try to find last newline within the chunk to split at paragraph boundaries
                int lastNewline = input.LastIndexOf('\n', end - 1, MaxChunkSize);
                if (lastNewline > start)
                {
                    int len = lastNewline - start + 1; // include the newline char
                    result.Add(input.Substring(start, len));
                    start += len;
                    continue;
                }

                // Try to find last space to avoid breaking words
                int lastSpace = input.LastIndexOf(' ', end - 1, MaxChunkSize);
                if (lastSpace > start)
                {
                    int len = lastSpace - start + 1; // include the space
                    result.Add(input.Substring(start, len));
                    start += len;
                    continue;
                }

                // No good split point found; hard split at MaxChunkSize
                result.Add(input.Substring(start, MaxChunkSize));
                start += MaxChunkSize;
            }

            return result;
        }

        private static async Task StartOpenAIResponseASync(SocketUserMessage message, bool isReply, IMessage replyMessage = null)
        {
            // Set a typing status
            try
            {
                await message.Channel.TriggerTypingAsync().ConfigureAwait(false);
            }
            catch { }
            
            // The message we're sending to OpenAI as the user.
            String openAIUserInput = "";
            // Is the message a reply? If so, grab the message they replied to and add to our outgoing message.
            if (isReply && replyMessage != null)
            {
                var channel = message.Channel as SocketTextChannel;
                if (replyMessage != null)
                {
                    openAIUserInput += "User quoted this message:\n```" + replyMessage.Content + "```\n\n";
                }
            }

            // Break out the latest message into parts so we can include image urls that we find.
            openAIUserInput += message.Content;
            List<ChatMessageContentPart> latestUserInputParts = new List<ChatMessageContentPart>();
            latestUserInputParts.Add(ChatMessageContentPart.CreateTextPart(openAIUserInput));
            
            foreach (Discord.Attachment attachment in message.Attachments)
            {
                if (attachment.ContentType != null && (attachment.ContentType.StartsWith("image/")))
                {
                    Uri uri;
                    if (Uri.TryCreate(attachment.Url, UriKind.Absolute, out uri)) {
                        latestUserInputParts.Add(ChatMessageContentPart.CreateImagePart(uri, ChatImageDetailLevel.High));
                    }
                }
            }
            if (replyMessage != null)
            {
                foreach (Discord.Attachment attachment in replyMessage.Attachments)
                {
                    if (attachment.ContentType != null && (attachment.ContentType.StartsWith("image/")))
                    {
                        Uri uri;
                        if (Uri.TryCreate(attachment.Url, UriKind.Absolute, out uri))
                        {
                            latestUserInputParts.Add(ChatMessageContentPart.CreateImagePart(uri, ChatImageDetailLevel.High));
                        }
                    }
                }
            }


            // Combine the message parts.
            ChatMessage latestUserInput = ChatMessage.CreateUserMessage(latestUserInputParts);


            List<ChatMessageHistoryItem> chatHistory;
            // Get our history
            try
            {
                chatHistory = ChatHistoryManager.GetChatMessageHistoryItems(message.Author.Id);
            }
            catch (Exception e)
            {
                await LoggingHandler.LogAsync($"Failed to get chat history for user {message.Author.Id}\n {e.Source} {e.Message}", EventLogEntryType.Error);
                chatHistory = new List<ChatMessageHistoryItem>();
            }


            List<ChatMessage> input_messages = new List<ChatMessage>
            {
                ChatMessage.CreateSystemMessage(GetModelInstructions(message))
            };

            // Add all our history, and then finally, our latest message.
            foreach (ChatMessageHistoryItem msg in chatHistory)
            {
                if (msg.MessageType == ChatMessageHistoryType.User)
                {
                    input_messages.Add(ChatMessage.CreateUserMessage(msg.Content));
                }
                else if (msg.MessageType == ChatMessageHistoryType.Assistant)
                {
                    input_messages.Add(ChatMessage.CreateAssistantMessage(msg.Content));
                }
            }
            input_messages.Add(latestUserInput);

            // TODO: Figure out how to enable web searching?
            ChatCompletionOptions options = new ChatCompletionOptions();
            options.EndUserId = message.Author.ToString();
            options.MaxOutputTokenCount = 8196; // I don't want it writing infinite fucking essays
            //options.ToolChoice = ChatToolChoice.CreateNoneChoice();
            options.Tools.Add(ImageInPaint);


            // Send it to OpenAI
            try
            {
                var aiResult = await CClient.CompleteChatAsync(input_messages, options);

                if (aiResult.Value.FinishReason != ChatFinishReason.ContentFilter)
                {
                    if (aiResult.Value.FinishReason == ChatFinishReason.Stop)
                    {
                        // Cut our response into >2000 length sized chunks for Discord
#if DEBUG
                    if (aiResult.Value.Content.Count > 1)
                    {
                        await LoggingHandler.LogAsync("WE GOT MORE THAN ONE RESPONSE ON A PROMPT RESULT!", EventLogEntryType.Error);
                    }
#endif

                        // Since we have a response and the message, add to our history before we write it back.
                        ChatHistoryManager.AddChatMessageHistoryItem(message.Author.Id, new ChatMessageHistoryItem(DateTime.Now, message.Content, message.Author.Id, ChatMessageHistoryType.User));

                        // Also add the response
                        ChatHistoryManager.AddChatMessageHistoryItem(message.Author.Id, new ChatMessageHistoryItem(DateTime.Now, aiResult.Value.Content[0].Text, dClient.CurrentUser.Id, ChatMessageHistoryType.Assistant));

                        List<String> messageChunks = BreakStringIntoChunks(aiResult.Value.Content[0].Text);
                        foreach (String chunk in messageChunks)
                        {
                            await message.ReplyAsync(text: chunk);
                            await Task.Delay(100); // Just for rate limiting
                        }
                    } 
                    else if (aiResult.Value.FinishReason == ChatFinishReason.ToolCalls)
                    {
                        foreach (ChatToolCall toolCall in aiResult.Value.ToolCalls)
                        {
                            switch (toolCall.FunctionName)
                            {
                                case nameof(InPaintImage):
                                    InPaintImage(toolCall, message, replyMessage);
                                    break;
                                default:
                                    await LoggingHandler.LogAsync($"Received call for unknown tool: {toolCall.FunctionName}", EventLogEntryType.Error);
                                    break;
                            }
                        }
                    }
                }
                else
                {
                    await message.ReplyAsync(text: "NOPE; DINK DONK OPENAI DIDN'T LIKE THAT:\n" + aiResult.Value.Refusal);
                }
            }
            catch (Exception e)
            {
                await LoggingHandler.LogAsync($"Failed to finish OpenAI call\n {e.Message}", EventLogEntryType.Error);
            }
        }
    }
}
