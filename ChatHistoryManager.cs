using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JimmieBot_CSharpService {

    enum ChatMessageHistoryType {
        User,
        Assistant
    }

    internal class ChatMessageHistoryItem
    {
        public DateTime MessageCreated { get; }
        public String Content { get; }
        public ulong AuthorID { get; }
        public ChatMessageHistoryType MessageType;

        public ChatMessageHistoryItem(DateTime messageCreated, String content, ulong authorID, ChatMessageHistoryType type)
        {
            MessageCreated = messageCreated;
            Content = content;
            AuthorID = authorID;
            MessageType = type;
        }
    }

    internal static class ChatHistoryManager
    {
        private static Dictionary<ulong,List<ChatMessageHistoryItem>> chatMessageHistoryItems = new Dictionary<ulong, List<ChatMessageHistoryItem>> { }; // Key is Message Author ID, value is list of ChatMessageHistoryItems

        // Provides list of messages from user after sanitizing.
        public static List<ChatMessageHistoryItem> GetChatMessageHistoryItems(ulong userID)
        {
            List<ChatMessageHistoryItem> result;
            int messageTimeoutSeconds = Convert.ToInt32(RegConfig.GetConfig(RegConfigItems.HistoryTimeOut, 600));

            // The time when a message would be considered expired.
            DateTime messageExpiredTime = DateTime.Now.Subtract(TimeSpan.FromSeconds(messageTimeoutSeconds));

            // Check our most recent message, see if it's older than our timeout time. If it is, start fresh. Also check and see if we have a history at all.
            if (!chatMessageHistoryItems.TryGetValue(userID, out result) || (result.Count > 0 && result.Last().MessageCreated < messageExpiredTime))
            {
                chatMessageHistoryItems[userID] =  new List<ChatMessageHistoryItem>();
                result = chatMessageHistoryItems[userID];
#if DEBUG
                LoggingHandler.LogAsync($"Clearing history for {userID} or creating new blank history.");
#endif
            }

            int maxHistory = Convert.ToInt32(RegConfig.GetConfig(RegConfigItems.HistoryMessageMax, 20));

            // Trim the message count (keep the oldest!)
            if (result.Count > maxHistory)
            {
                result.RemoveRange(0, result.Count - maxHistory);
            }

            return result;
        }

        public static void AddChatMessageHistoryItem(ulong userID, ChatMessageHistoryItem item)
        {
            List<ChatMessageHistoryItem> userHistory = GetChatMessageHistoryItems(userID);
            userHistory.Add(item);
        }
    }
}
