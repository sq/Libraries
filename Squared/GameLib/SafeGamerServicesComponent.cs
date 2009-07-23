using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using System.Runtime.Remoting.Messaging;
using Microsoft.Xna.Framework.Storage;

namespace Squared.Game.GamerServices {
    public class SafeGamerServicesComponent : GameComponent {
        public SafeGamerServicesComponent (Microsoft.Xna.Framework.Game game) 
            : base (game) {
        }

#if XBOX
        GamerServicesComponent _Component;
#endif

        public override void Initialize () {
#if XBOX
            _Component = new GamerServicesComponent(Game);
#endif
        }

        public override void Update (GameTime gameTime) {
#if XBOX
            _Component.Update(gameTime);
#endif
        }
    }

    public interface IGuideProvider {
        IAsyncResult BeginShowKeyboardInput(PlayerIndex player, string title, string description, string defaultText, AsyncCallback callback, object state);
        IAsyncResult BeginShowMessageBox(string title, string text, IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon, AsyncCallback callback, object state);
        IAsyncResult BeginShowMessageBox(PlayerIndex player, string title, string text, IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon, AsyncCallback callback, object state);
        IAsyncResult BeginShowStorageDeviceSelector(AsyncCallback callback, object state);
        IAsyncResult BeginShowStorageDeviceSelector(PlayerIndex player, AsyncCallback callback, object state);
        IAsyncResult BeginShowStorageDeviceSelector(int sizeInBytes, int directoryCount, AsyncCallback callback, object state);
        IAsyncResult BeginShowStorageDeviceSelector(PlayerIndex player, int sizeInBytes, int directoryCount, AsyncCallback callback, object state);
        void DelayNotifications(TimeSpan delay);
        string EndShowKeyboardInput(IAsyncResult result);
        int? EndShowMessageBox(IAsyncResult result);
        StorageDevice EndShowStorageDeviceSelector(IAsyncResult result);
        void ShowComposeMessage(PlayerIndex player, string text, IEnumerable<Gamer> recipients);
        void ShowFriendRequest(PlayerIndex player, Gamer gamer);
        void ShowFriends(PlayerIndex player);
        void ShowGameInvite(PlayerIndex player, IEnumerable<Gamer> recipients);
        void ShowGamerCard(PlayerIndex player, Gamer gamer);
        void ShowMarketplace(PlayerIndex player);
        void ShowMessages(PlayerIndex player);
        void ShowParty(PlayerIndex player);
        void ShowPartySessions(PlayerIndex player);
        void ShowPlayerReview(PlayerIndex player, Gamer gamer);
        void ShowPlayers(PlayerIndex player);
        void ShowSignIn(int paneCount, bool onlineOnly);

        bool IsScreenSaverEnabled { get; set; }
        bool IsTrialMode { get; }
        bool IsVisible { get; }
        NotificationPosition NotificationPosition { get; set; }
        bool SimulateTrialMode { get; set; } 
    }

    public class WindowsSafeGuideProvider : IGuideProvider {
        public class AsyncResult : IAsyncResult {
            public object AsyncState {
                get { return null; }
            }

            public System.Threading.WaitHandle AsyncWaitHandle {
                get { throw new NotImplementedException(); }
            }

            public bool CompletedSynchronously {
                get { return true; }
            }

            public bool IsCompleted {
                get { return true; }
            }
        }

        public IAsyncResult BeginShowKeyboardInput (PlayerIndex player, string title, string description, string defaultText, AsyncCallback callback, object state) {
            return new AsyncResult();
        }

        public IAsyncResult BeginShowMessageBox (string title, string text, IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon, AsyncCallback callback, object state) {
            return new AsyncResult();
        }

        public IAsyncResult BeginShowMessageBox (PlayerIndex player, string title, string text, IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon, AsyncCallback callback, object state) {
            return new AsyncResult();
        }

        public IAsyncResult BeginShowStorageDeviceSelector (AsyncCallback callback, object state) {
            return Guide.BeginShowStorageDeviceSelector(callback, state);
        }

        public IAsyncResult BeginShowStorageDeviceSelector (PlayerIndex player, AsyncCallback callback, object state) {
            return Guide.BeginShowStorageDeviceSelector(player, callback, state);
        }

        public IAsyncResult BeginShowStorageDeviceSelector (int sizeInBytes, int directoryCount, AsyncCallback callback, object state) {
            return Guide.BeginShowStorageDeviceSelector(sizeInBytes, directoryCount, callback, state);
        }

        public IAsyncResult BeginShowStorageDeviceSelector (PlayerIndex player, int sizeInBytes, int directoryCount, AsyncCallback callback, object state) {
            return Guide.BeginShowStorageDeviceSelector(player, sizeInBytes, directoryCount, callback, state);
        }

        public void DelayNotifications (TimeSpan delay) {
        }

        public string EndShowKeyboardInput (IAsyncResult result) {
            return null;
        }

        public int? EndShowMessageBox (IAsyncResult result) {
            return null;
        }

        public StorageDevice EndShowStorageDeviceSelector (IAsyncResult result) {
            return Guide.EndShowStorageDeviceSelector(result);
        }

        public void ShowComposeMessage (PlayerIndex player, string text, IEnumerable<Gamer> recipients) {
        }

        public void ShowFriendRequest (PlayerIndex player, Gamer gamer) {
        }

        public void ShowFriends (PlayerIndex player) {
        }

        public void ShowGameInvite (PlayerIndex player, IEnumerable<Gamer> recipients) {
        }

        public void ShowGamerCard (PlayerIndex player, Gamer gamer) {
        }

        public void ShowMarketplace (PlayerIndex player) {
        }

        public void ShowMessages (PlayerIndex player) {
        }

        public void ShowParty (PlayerIndex player) {
        }

        public void ShowPartySessions (PlayerIndex player) {
        }

        public void ShowPlayerReview (PlayerIndex player, Gamer gamer) {
        }

        public void ShowPlayers (PlayerIndex player) {
        }

        public void ShowSignIn (int paneCount, bool onlineOnly) {
        }

        // Properties
        public bool IsScreenSaverEnabled { get; set; }
        public bool IsTrialMode {
            get {
                return SimulateTrialMode;
            }
        }
        public bool IsVisible {
            get {
                return false;
            }
        }
        public NotificationPosition NotificationPosition { get; set; }
        public bool SimulateTrialMode { get; set; }
    }

    public class RealGuideProvider : IGuideProvider {

        public IAsyncResult BeginShowKeyboardInput (PlayerIndex player, string title, string description, string defaultText, AsyncCallback callback, object state) {
            return Guide.BeginShowKeyboardInput(player, title, description, defaultText, callback, state);
        }

        public IAsyncResult BeginShowMessageBox (string title, string text, IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon, AsyncCallback callback, object state) {
            return Guide.BeginShowMessageBox(title, text, buttons, focusButton, icon, callback, state);
        }

        public IAsyncResult BeginShowMessageBox (PlayerIndex player, string title, string text, IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon, AsyncCallback callback, object state) {
            return Guide.BeginShowMessageBox(player, title, text, buttons, focusButton, icon, callback, state);
        }

        public IAsyncResult BeginShowStorageDeviceSelector (AsyncCallback callback, object state) {
            return Guide.BeginShowStorageDeviceSelector(callback, state);
        }

        public IAsyncResult BeginShowStorageDeviceSelector (PlayerIndex player, AsyncCallback callback, object state) {
            return Guide.BeginShowStorageDeviceSelector(player, callback, state);
        }

        public IAsyncResult BeginShowStorageDeviceSelector (int sizeInBytes, int directoryCount, AsyncCallback callback, object state) {
            return Guide.BeginShowStorageDeviceSelector(sizeInBytes, directoryCount, callback, state);
        }

        public IAsyncResult BeginShowStorageDeviceSelector (PlayerIndex player, int sizeInBytes, int directoryCount, AsyncCallback callback, object state) {
            return Guide.BeginShowStorageDeviceSelector(player, sizeInBytes, directoryCount, callback, state);
        }

        public void DelayNotifications (TimeSpan delay) {
            Guide.DelayNotifications(delay);
        }

        public string EndShowKeyboardInput (IAsyncResult result) {
            return Guide.EndShowKeyboardInput(result);
        }

        public int? EndShowMessageBox (IAsyncResult result) {
            return Guide.EndShowMessageBox(result);
        }

        public StorageDevice EndShowStorageDeviceSelector (IAsyncResult result) {
            return Guide.EndShowStorageDeviceSelector(result);
        }

        public void ShowComposeMessage (PlayerIndex player, string text, IEnumerable<Gamer> recipients) {
            Guide.ShowComposeMessage(player, text, recipients);
        }

        public void ShowFriendRequest (PlayerIndex player, Gamer gamer) {
            Guide.ShowFriendRequest(player, gamer);
        }

        public void ShowFriends (PlayerIndex player) {
            Guide.ShowFriends(player);
        }

        public void ShowGameInvite (PlayerIndex player, IEnumerable<Gamer> recipients) {
            Guide.ShowGameInvite(player, recipients);
        }

        public void ShowGamerCard (PlayerIndex player, Gamer gamer) {
            Guide.ShowGamerCard(player, gamer);
        }

        public void ShowMarketplace (PlayerIndex player) {
            Guide.ShowMarketplace(player);
        }

        public void ShowMessages (PlayerIndex player) {
            Guide.ShowMessages(player);
        }

        public void ShowParty (PlayerIndex player) {
            Guide.ShowParty(player);
        }

        public void ShowPartySessions (PlayerIndex player) {
            Guide.ShowPartySessions(player);
        }

        public void ShowPlayerReview (PlayerIndex player, Gamer gamer) {
            Guide.ShowPlayerReview(player, gamer);
        }

        public void ShowPlayers (PlayerIndex player) {
            Guide.ShowPlayers(player);
        }

        public void ShowSignIn (int paneCount, bool onlineOnly) {
            Guide.ShowSignIn(paneCount, onlineOnly);
        }

        public bool IsScreenSaverEnabled {
            get {
                return Guide.IsScreenSaverEnabled;
            }
            set {
                Guide.IsScreenSaverEnabled = value;
            }
        }

        public bool IsTrialMode {
            get {
                return Guide.IsTrialMode;
            }
        }

        public bool IsVisible {
            get {
                return Guide.IsVisible;
            }
        }

        public NotificationPosition NotificationPosition {
            get {
                return Guide.NotificationPosition;
            }
            set {
                Guide.NotificationPosition = value;
            }
        }

        public bool SimulateTrialMode {
            get {
                return Guide.SimulateTrialMode;
            }
            set {
                Guide.SimulateTrialMode = value;
            }
        }
    }

    public static class PluggableGuide {
        public static IGuideProvider Provider;

        static PluggableGuide () {
#if XBOX
            Provider = new RealGuideProvider();
#else
            Provider = new WindowsSafeGuideProvider();
#endif
        }

        public static IAsyncResult BeginShowKeyboardInput (PlayerIndex player, string title, string description, string defaultText, AsyncCallback callback, object state) {
            return Provider.BeginShowKeyboardInput(player, title, description, defaultText, callback, state);
        }

        public static IAsyncResult BeginShowMessageBox (string title, string text, IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon, AsyncCallback callback, object state) {
            return Provider.BeginShowMessageBox(title, text, buttons, focusButton, icon, callback, state);
        }

        public static IAsyncResult BeginShowMessageBox (PlayerIndex player, string title, string text, IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon, AsyncCallback callback, object state) {
            return Provider.BeginShowMessageBox(player, title, text, buttons, focusButton, icon, callback, state);
        }

        public static IAsyncResult BeginShowStorageDeviceSelector (AsyncCallback callback, object state) {
            return Provider.BeginShowStorageDeviceSelector(callback, state);
        }

        public static IAsyncResult BeginShowStorageDeviceSelector (PlayerIndex player, AsyncCallback callback, object state) {
            return Provider.BeginShowStorageDeviceSelector(player, callback, state);
        }

        public static IAsyncResult BeginShowStorageDeviceSelector(int sizeInBytes, int directoryCount, AsyncCallback callback, object state) {
            return Provider.BeginShowStorageDeviceSelector(sizeInBytes, directoryCount, callback, state);
        }

        public static IAsyncResult BeginShowStorageDeviceSelector(PlayerIndex player, int sizeInBytes, int directoryCount, AsyncCallback callback, object state) {
            return Provider.BeginShowStorageDeviceSelector(player, sizeInBytes, directoryCount, callback, state);
        }

        public static void DelayNotifications(TimeSpan delay) {
            Provider.DelayNotifications(delay);
        }

        public static string EndShowKeyboardInput(IAsyncResult result) {
            return Provider.EndShowKeyboardInput(result);
        }

        public static int? EndShowMessageBox(IAsyncResult result) {
            return Provider.EndShowMessageBox(result);
        }

        public static StorageDevice EndShowStorageDeviceSelector(IAsyncResult result) {
            return Provider.EndShowStorageDeviceSelector(result);
        }

        public static void ShowComposeMessage(PlayerIndex player, string text, IEnumerable<Gamer> recipients) {
            Provider.ShowComposeMessage(player, text, recipients);
        }

        public static void ShowFriendRequest(PlayerIndex player, Gamer gamer) {
            Provider.ShowFriendRequest(player, gamer);
        }

        public static void ShowFriends(PlayerIndex player) {
            Provider.ShowFriends(player);
        }

        public static void ShowGameInvite(PlayerIndex player, IEnumerable<Gamer> recipients) {
            Provider.ShowGameInvite(player, recipients);
        }

        public static void ShowGamerCard(PlayerIndex player, Gamer gamer) {
            Provider.ShowGamerCard(player, gamer);
        }

        public static void ShowMarketplace(PlayerIndex player) {
            Provider.ShowMarketplace(player);
        }

        public static void ShowMessages(PlayerIndex player) {
            Provider.ShowMessages(player);
        }

        public static void ShowParty(PlayerIndex player) {
            Provider.ShowParty(player);
        }

        public static void ShowPartySessions(PlayerIndex player) {
            Provider.ShowPartySessions(player);
        }

        public static void ShowPlayerReview(PlayerIndex player, Gamer gamer) {
            Provider.ShowPlayerReview(player, gamer);
        }

        public static void ShowPlayers(PlayerIndex player) {
            Provider.ShowPlayers(player);
        }

        public static void ShowSignIn(int paneCount, bool onlineOnly) {
            Provider.ShowSignIn(paneCount, onlineOnly);
        }

        public static bool IsScreenSaverEnabled {
            get {
                return Provider.IsScreenSaverEnabled;
            }
            set {
                Provider.IsScreenSaverEnabled = value;
            }
        }

        public static bool IsTrialMode { 
            get {
                return Provider.IsTrialMode;
            } 
        }

        public static bool IsVisible {
            get {
                return Provider.IsVisible;
            }
        }

        public static NotificationPosition NotificationPosition {
            get {
                return Provider.NotificationPosition;
            }
            set {
                Provider.NotificationPosition = value;
            }
        }

        public static bool SimulateTrialMode {
            get {
                return Provider.SimulateTrialMode;
            }
            set {
                Provider.SimulateTrialMode = value;
            }
        }
    }
}
