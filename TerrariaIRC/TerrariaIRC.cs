using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Hooks;
using Meebey.SmartIrc4net;
using TShockAPI;
using Terraria;
using ErrorEventArgs = Meebey.SmartIrc4net.ErrorEventArgs;

// TerrariaIRC *****************************************************************
namespace TerrariaIRC
{

  // TerrariaIRC ***************************************************************
  [APIVersion( 1, 11 )]
  public class TerrariaIRC : TerrariaPlugin
  {
    #region Plugin Vars
    public  static IrcClient irc           = new IrcClient();
    public  static string    settingsfile  = Path.Combine( TShock.SavePath, "irc", "settings.txt" );
    public  static bool      stayConnected = true;
    private static Settings  settings      = new Settings();
    private static int maxAttempts  = 3;
    private static int attemptCount = 0;
    private static int sleepDelay   = 15 * 60 * 1000; // 15 minutes
    #endregion


    #region Plugin overrides
    // Initialize ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    public override void Initialize()
    {
      ServerHooks.Chat  += OnChat;
      ServerHooks.Join  += OnJoin;
      ServerHooks.Leave += OnLeave;
      SetupIRC();
      if ( !settings.Load() )
      {
        Log.Error( "Settings failed to load, aborting IRC connection." );
        return;
      } // if
      Commands.Init();
      new Thread( Connect ).Start();
    } // Initialize ------------------------------------------------------------


    // Dispose +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    protected override void Dispose( bool disposing )
    {
      if ( disposing )
      {
        stayConnected = false;
        if ( irc.IsConnected )
          irc.Disconnect();
        ServerHooks.Chat  -= OnChat;
        ServerHooks.Join  -= OnJoin;
        ServerHooks.Leave -= OnLeave;
        base.Dispose( disposing );
      } // if
    } // Dispose ---------------------------------------------------------------


    // SetupIRC ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    private void SetupIRC() 
    {
      irc.Encoding             = System.Text.Encoding.ASCII;
      irc.SendDelay            = 300;
      irc.ActiveChannelSyncing = true;
      irc.AutoRejoinOnKick     = true;
      irc.OnError             += OnError;
      irc.OnChannelMessage    += OnChannelMessage;
      irc.OnRawMessage        += OnRawMessage;
    } // SetupIRC --------------------------------------------------------------


    // resetConnectionSettings +++++++++++++++++++++++++++++++++++++++++++++++++
    public static void resetConnectionSettings()
    {
      attemptCount  = 0;
      stayConnected = true;
    } // resetConnectionSettings -----------------------------------------------
    #endregion


    #region IRC methods
    // OnChannelMessage ++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    void OnChannelMessage( object sender, IrcEventArgs ircEvent )
    {
      var message = ircEvent.Data.Message;
      if ( message.StartsWith( "!" ) )
      {
        if ( message.ToLower() == "!players" )
        {
          ActionPlayers( sender, ircEvent );
        } // if
        else
        {
          ActionCommand( sender, ircEvent );
        } // else
      } // if
      else
      {
        TShock.Utils.Broadcast( string.Format( "(IRC)<{0}> {1}", ircEvent.Data.Nick,
            TShock.Utils.SanitizeString( Regex.Replace( message, (char) 3 + "[0-9]{1,2}(,[0-9]{1,2})?", String.Empty ) ) ), Color.Green );
      } // else
    } // OnChannelMessage ------------------------------------------------------


    // OnRawMessage ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    void OnRawMessage( object sender, IrcEventArgs e )
    {
      Debug.Write( e.Data.RawMessage );
    } // OnRawMessage ----------------------------------------------------------


    // Connect +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    public static void Connect()
    {
      while ( stayConnected && (attemptCount < maxAttempts) )
      {
        Log.Info( "Connecting to " + settings["server"] + ":" + settings["port"] + "..." );
        try
        {
          irc.Connect( settings["server"], int.Parse( settings["port"] ) );
          irc.ListenOnce();
          Log.Info( "Connected to IRC server." );
        } // try
        catch ( Exception exception )
        {
          Log.Error( "Error connecting to IRC server." );
          Log.Error( exception.Message );
          Thread.Sleep( sleepDelay );
        } // catch
        try
        {
          irc.Login( settings["botname"], "TerrariaIRC" );
          irc.ListenOnce();
          Log.Info( "Trying to join " + settings["channel"] + "..." );
          irc.RfcJoin( settings["channel"] );
          irc.ListenOnce();
          Log.Info( "Joined " + settings["channel"] );
          if ( settings.ContainsKey( "nickserv" ) && settings.ContainsKey( "password" ) )
          {
            irc.RfcPrivmsg( settings["nickserv"], settings["password"] );
            irc.ListenOnce();
          } // if
          irc.Listen();
          Log.Error( "Disconnected from IRC... Attempting to reconnect: " + attemptCount );
          attemptCount++;
        } // try
        catch ( Exception exception )
        {
          Log.Error( "Error communicating with IRC server." );
          Log.Error( exception.Message );
          return;
        } // catch
      } // while
    } // Connect ---------------------------------------------------------------


    // ActionCommand +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    private void ActionCommand( object       sender, 
                                IrcEventArgs ircEvent)
    {
      var message = ircEvent.Data.Message;
      if ( !message.ToLower().Contains( "superadmin" ) )
      {
        if ( IsAllowed( ircEvent.Data.Nick ) )
        {
          var user = new IRCPlayer( ircEvent.Data.Nick ) { Group = new SuperAdminGroup() };
          String conCommand = "/" + message.TrimStart( '!' );
          Log.Info( user + " invoked command: " + conCommand );
          TShockAPI.Commands.HandleCommand( user, conCommand );
          foreach ( var outputMessage in user.Output )
          {
            irc.RfcPrivmsg( ircEvent.Data.Nick, outputMessage );
          } // for
        } // if
        else
        {
          Log.Warn( ircEvent.Data.Nick + " attempted to invoked command: " + message );
          irc.RfcPrivmsg( ircEvent.Data.Nick, "You are not authorized to perform commands on the server." );
        } // else
      } // if
      else
      {
        Log.Warn( ircEvent.Data.Nick + " attempted to invoked command: " + message );
        irc.RfcPrivmsg( ircEvent.Data.Nick, "Command not allowed through irc." );
      } // else
    } // ActionCommand ---------------------------------------------------------


    // ActionPlayers +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    private void ActionPlayers( object       sender, 
                                IrcEventArgs ircEvent )
    {
      var reply = TShock.Players.Where( player => player != null )
                                .Where( player => player.RealPlayer )
                                .Aggregate( "", ( current, player ) => current + (current == "" ? player.Name : ", " + player.Name) );
      irc.SendMessage( SendType.Message, settings["channel"], "Current Players: " + reply );
    } // ActionPlayers ---------------------------------------------------------


    // IsAllowed +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    public static bool IsAllowed( string nick )
    {
      // DMax: Theres an issue with IsOp, if the ircd has anything higher like +a, IsOp will return false;
      if ( bool.Parse( settings["allowop"] ) )
      {
        return (from user in (from DictionaryEntry channeluser in irc.GetChannel( settings["channel"] ).Users select (ChannelUser) channeluser.Value) where user.Nick == nick select user.IsOp).FirstOrDefault();
      } // if
      return false;
    } // IsAllowed -------------------------------------------------------------


    // CompareIrcUser ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    public static bool CompareIrcUser( IrcUser user1, IrcUser user2 )
    {
      return (user1.Host == user2.Host && user1.Ident == user2.Ident && user1.Realname == user2.Realname);
    } // CompareIrcUser --------------------------------------------------------


    // sendIRCMessage ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    public static void sendIRCMessage( string message )
    {
      irc.SendMessage( SendType.Message, settings["channel"], message );
    } // sendIRCMessage --------------------------------------------------------
    #endregion


    /***************************************************************************
     * Plugin Hooks                                                            *
     **************************************************************************/ 
    #region Plugin hooks
    // OnChat ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    void OnChat( messageBuffer   message, 
                int              playerId, 
                string           text, 
                HandledEventArgs eventArgs )
    {
      if ( !irc.IsConnected ) return;
      var player = TShock.Players[message.whoAmI];
      if ( player == null ) return;
      if ( !TShock.Utils.ValidString( text ) ) return;
      if ( player.mute ) return;

      //if ( text.StartsWith( "/" ) ) return;
      if ( text.StartsWith( "/" ) ) 
        text = ScrubCommand( text );

      irc.SendMessage( SendType.Message, settings["channel"], string.Format( "({0}){1}: {2}",
                                         player.Group.Name, player.Name, text ) );

    } // OnChat ----------------------------------------------------------------


    // OnJoin ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    void OnJoin( int player, HandledEventArgs e )
    {
      if ( !irc.IsConnected ) return;
      if ( e.Handled ) return;

      irc.SendMessage( SendType.Message, settings["channel"], 
                       string.Format( "Joined[{0}]: {1} ({2}/{3}) - {4}({5})", 
                                      CountPlayers() + 1,
                                      Main.player[player].name,
                                      Main.player[player].statLifeMax,
                                      Main.player[player].statManaMax,
                                      Main.player[player].inventory[0].name,
                                      Main.player[player].inventory[0].stack ) );

    } // OnJoin ----------------------------------------------------------------


    // OnLeave +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    void OnLeave( int player )
    {
      if ( !irc.IsConnected ) return;

      irc.SendMessage( SendType.Message, settings["channel"],
                        string.Format( "Left[{0}]: {1}",
                        CountPlayers(),
                        Main.player[player].name ) );

    } // OnLeave ---------------------------------------------------------------

    
    // OnError +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    public static void OnError( object sender, ErrorEventArgs e )
    {
      Log.Error( "IRC Error: " + e.Data.RawMessage );
    } // OnError ---------------------------------------------------------------
    #endregion


    /***************************************************************************
     * Data Handlers                                                           *
     **************************************************************************/ 
    // CountPlayers ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    private int CountPlayers()
    {
      int result = 0;
      foreach ( TSPlayer player in TShock.Players ) 
      {
        if ( player != null && player.Active ) { result++; }
      } // foreach

      return result;
    } // CountPlayers ----------------------------------------------------------


    // ScrubCommand ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    // filter out command arguments with password info
    private string ScrubCommand( string text ) {
      string result = text.Remove( 0, 1 ).ToLower().Trim();

      if ( result.StartsWith( "register" ) ) result = "register";
      if ( result.StartsWith( "login"    ) ) result = "login";
      if ( result.StartsWith( "password" ) ) result = "password";

      result = "command: " + result;

      return result;
    } // ScrubCommand ----------------------------------------------------------

    
    /***************************************************************************
     * Plugin Properties                                                       *
     **************************************************************************/ 
    #region Plugin Properties
    // Name ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    public override string Name
    {
      get { return "TerrariaIRC"; }
    } // Name ------------------------------------------------------------------


    // Author ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    public override string Author
    {
      get { return "Deathmax, _Jon"; }
    } // Author ----------------------------------------------------------------


    // Description +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    public override string Description
    {
      get { return "Provides an interface between IRC and Terraria.\n" +
                   "Also provides player from commands in IRC channel (/iInfo)"; }
    } // Description -----------------------------------------------------------


    // Version +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
   public override Version Version
    {
      get { return new Version( 1, 2, 3, 0 ); }
    } // Versin ----------------------------------------------------------------

    
    // TerrariaIRC +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
    public TerrariaIRC( Main game ) : base( game )
    {
      Order = -9;
    } // TerrariaIRC -----------------------------------------------------------
    #endregion

  } // TerrariaIRC -------------------------------------------------------------



    // _Jon : not functional 03-03-12 -> moved to "OnChannelMessage"
    /*      
            public static void OnQueryMessage(object sender, IrcEventArgs e)
            {
              var message = e.Data.Message;
              if ( IsAllowed( e.Data.Nick ) )
              {
                if ( message.StartsWith( "!" ) )
                {
                  //Console.WriteLine( "~ ! : " + message );
                  var user = new IRCPlayer( e.Data.Nick ) { Group = new SuperAdminGroup() };
                  String conCommand = "/" + message.TrimStart( '!' );
                  Log.Info( user + " invoked command: " + conCommand );
                  TShockAPI.Commands.HandleCommand( user, conCommand );
                  foreach ( var t in user.Output )
                  {
                    irc.RfcPrivmsg( e.Data.Nick, t );
                  } // for
                }
              }
              else
              {
                Log.Warn( e.Data.Nick + " attempted to invoked command: " + message );
                irc.RfcPrivmsg( e.Data.Nick, "You are not allowed to perform that action." );
              }
            }
    */
} // TerrariaIRC ===============================================================
