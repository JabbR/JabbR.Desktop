using System;
using Eto.Forms;
using Eto.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JabbR.Desktop.Model;
using System.Diagnostics;

namespace JabbR.Desktop.Interface
{
    public class ChannelSection : MessageSection
    {
        bool noHistory;
        bool retrievingHistory;
        const string JOIN_ROOM_PREFIX = "?join-room=";
        const string LOAD_HISTORY_PREFIX = "?load-history";

        public UserList UserList { get; private set; }

        public Channel Channel { get; private set; }

        public Server Server { get { return Channel.Server; } }

        public override bool SupportsAutoComplete { get { return true; } }

        public override bool AllowNotificationCollapsing { get { return true; } }

        public override string TitleLabel
        {
            get
            {
                return "#" + Channel.Name;
            }
        }

        public ChannelSection(Channel channel)
        {
            this.Channel = channel;
            this.Channel.MessageReceived += HandleMessageReceived;
            this.Channel.UserJoined += HandleUserJoined;
            this.Channel.UserLeft += HandleUserLeft;
            this.Channel.UserIconChanged += HandleUserIconChanged;
            this.Channel.OwnerAdded += HandleOwnerAdded;
            this.Channel.OwnerRemoved += HandleOwnerRemoved;
            this.Channel.UsersActivityChanged += HandleUsersActivityChanged;
            this.Channel.MessageContent += HandleMessageContent;
            this.Channel.TopicChanged += HandleTopicChanged;
            this.Channel.MeMessageReceived += HandleMeMessageReceived;
            this.Channel.UsernameChanged += HandleUsernameChanged;
        }

        void HandleUsernameChanged(object sender, UsernameChangedEventArgs e)
        {
            Application.Instance.AsyncInvoke(delegate
            {
                UserList.UsernameChanged(e.OldUsername, e.User);
            });
            AddNotification(new NotificationMessage(DateTimeOffset.Now, "{0}'s nick has changed to {1}.", e.OldUsername, e.User.Name));
        }

        void HandleMeMessageReceived(object sender, MeMessageEventArgs e)
        {
            MeMessage(e.Message);
        }

        void HandleTopicChanged(object sender, EventArgs e)
        {
            AddNotification(new NotificationMessage(DateTimeOffset.Now, "Topic was changed to \"{0}\".", Channel.Topic));
            SetTopic(Channel.Topic);
        }

        void HandleMessageContent(object sender, MessageContentEventArgs e)
        {
            Application.Instance.AsyncInvoke(delegate
            {
                AddMessageContent(e.Content);
            });
        }

        void HandleUsersActivityChanged(object sender, UsersEventArgs e)
        {
            Application.Instance.AsyncInvoke(delegate
            {
                UserList.UsersActivityChanged(e.Users);
            });
        }

        void HandleOwnerRemoved(object sender, UserEventArgs e)
        {
            Application.Instance.AsyncInvoke(delegate
            {
                UserList.OwnerRemoved(e.User);
            });
            AddNotification(new NotificationMessage(
                DateTimeOffset.Now,
                string.Format("{0} was removed as an owner", e.User.Name)
            ));
        }

        void HandleOwnerAdded(object sender, UserEventArgs e)
        {
            Application.Instance.AsyncInvoke(delegate
            {
                UserList.OwnerAdded(e.User);
            });
            AddNotification(new NotificationMessage(
                DateTimeOffset.Now,
                string.Format("{0} was added as an owner", e.User.Name)
            ));
        }

        void HandleUserIconChanged(object sender, UserImageEventArgs e)
        {
            Application.Instance.AsyncInvoke(delegate
            {
                UserList.UserIconChanged(e.User, e.Image);
            });
        }

        void HandleUserLeft(object sender, UserEventArgs e)
        {
            Application.Instance.AsyncInvoke(delegate
            {
                UserList.UserLeft(e.User);
            });
            AddNotification(new NotificationMessage(
                DateTimeOffset.Now,
                string.Format("{0} left {1}", e.User.Name, Channel.Name)
            ));
        }

        void HandleUserJoined(object sender, UserEventArgs e)
        {
            Application.Instance.AsyncInvoke(delegate
            {
                UserList.UserJoined(e.User);
            });
            AddNotification(new NotificationMessage(
                DateTimeOffset.Now,
                string.Format("{0} just entered {1}", e.User.Name, Channel.Name)
            ));
        }

        void HandleMessageReceived(object sender, MessageEventArgs e)
        {
            AddMessage(e.Message);
        }

        protected override Control CreateLayout()
        {
            var split = new Splitter
            {
                FixedPanel = SplitterFixedPanel.Panel2
            };
            
            split.Panel1 = base.CreateLayout();
            split.Panel2 = UserList = new UserList(Channel);
            
            return split;
        }

        void LoadError(Exception ex, string message)
        {
            Debug.Print("{0} {1}", message, ex);
            StartLive();
            AddNotification(new NotificationMessage("{0} {1}", message, ex != null ? ex.GetBaseException().Message : null));
            SetMarker();
            ReplayDelayedCommands();
            FinishLoad();
        }

        protected override async void HandleDocumentLoaded(object sender, WebViewLoadedEventArgs e)
        {
            if (Channel != null && Server.IsConnected)
            {
                BeginLoad();
                try
                {
                    var channel = await Channel.GetChannelInfo();
                    if (channel != null)
                    {
                        SetTopic(channel.Topic);
                        UserList.SetUsers(channel.Users);
                        var history = await channel.GetHistory(LastHistoryMessageId);
                        if (history != null)
                        {
                            StartLive();
                            AddHistory(history, true);
                            AddNotification(new NotificationMessage(DateTimeOffset.Now, string.Format("You just entered {0}", Channel.Name)));
                            SetMarker();
                            ReplayDelayedCommands();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoadError(ex, "Error getting channel info");
                }
                FinishLoad();
            }
            else
            {
                StartLive();
                ReplayDelayedCommands();
                AddNotification(new NotificationMessage(DateTimeOffset.Now, "Disconnected"));
            }
        }

        protected override void HandleAction(WebViewLoadingEventArgs e)
        {
            var historyIndex = e.Uri.LocalPath.IndexOf(LOAD_HISTORY_PREFIX, StringComparison.Ordinal);
            if (historyIndex >= 0)
            {
                LoadHistory();
                return;
            }

            var joinRoomIndex = e.Uri.LocalPath.IndexOf(JOIN_ROOM_PREFIX, StringComparison.Ordinal);
            if (joinRoomIndex >= 0)
            {
                Channel.Server.JoinChannel(e.Uri.PathAndQuery.Substring(joinRoomIndex + JOIN_ROOM_PREFIX.Length));
                return;
            }
            
            base.HandleAction(e);
        }

        protected async void LoadHistory()
        {
            if (!noHistory && !retrievingHistory)
            {
                retrievingHistory = true;
                try
                {
                    var history = await Channel.GetHistory(LastHistoryMessageId);
                    if (history != null)
                    {
                        noHistory = !history.Any();
                        AddHistory(history);
                    }
                }
                finally
                {
                    FinishLoad();
                    retrievingHistory = false;
                }
            }
            else if (noHistory)
                FinishLoad();
                
        }

        public void MeMessage(MeMessage message)
        {
            SendCommand("addMeMessage", message);
        }

        public override void UserTyping()
        {
            Channel.UserTyping();
        }

        public override void ProcessCommand(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return;
            
            Channel.SendMessage(command);
        }

        protected override async Task<IEnumerable<string>> GetAutoCompleteNames(string search)
        {
            if (Channel.Server.IsConnected)
            {
                if (search.StartsWith("#", StringComparison.Ordinal))
                {
                    search = search.TrimStart('#');
                    var channels = await Channel.Server.GetCachedChannels();
                    return channels.Where(r => r.Name.StartsWith(search, StringComparison.CurrentCultureIgnoreCase)).Select(r => r.Name);
                }
                search = search.TrimStart('@');
                return Channel.Users.Where(r => r.Name.StartsWith(search, StringComparison.CurrentCultureIgnoreCase)).Select(r => r.Name);
            }
            return Enumerable.Empty<string>();
        }

        public override string TranslateAutoCompleteText(string selection, string search)
        {
            if (search.StartsWith("#", StringComparison.Ordinal))
                return '#' + base.TranslateAutoCompleteText(selection, search) + ' ';
            return '@' + base.TranslateAutoCompleteText(selection, search) + ' ';
        }
    }
}

