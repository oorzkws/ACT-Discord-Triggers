using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.VoiceNext;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Threading.Tasks;

namespace DiscordAPI {
  public static class DiscordApiClient {
    private static DSharpPlus.DiscordClient bot;
    private static VoiceTransmitSink voiceStream;
    private static VoiceNextConnection voiceChannel;

    public delegate void BotLoaded();
    public static BotLoaded BotReady;

    public delegate void BotMessage(string message);
    public static BotMessage Log;

    public async static void Init(string logintoken) {
      try {
        bot = new DSharpPlus.DiscordClient(new DiscordConfiguration() {
          Token = logintoken,
          TokenType = TokenType.Bot,
          Intents = DiscordIntents.Guilds | DiscordIntents.GuildVoiceStates,
          MinimumLogLevel = LogLevel.Debug
        });
        bot.UseVoiceNext();
      } catch (NotSupportedException ex) {
        Log?.Invoke("Unsupported Operating System.");
        Log?.Invoke(ex.Message);
      }

      try {
        bot.Ready += Bot_Ready;
        bot.GuildDownloadCompleted += Bot_Downloaded;
        bot.VoiceStateUpdated += Bot_VoiceStateChanged;
        await bot.ConnectAsync();
      } catch (Exception ex) {
        Log?.Invoke(ex.Message);
        Log?.Invoke("Error connecting to Discord.");
      }
    }

    public async static Task deInIt() {
      bot.Ready -= Bot_Ready;
      bot.GuildDownloadCompleted -= Bot_Downloaded;
      bot.VoiceStateUpdated -= Bot_VoiceStateChanged;
      await bot.DisconnectAsync();
    }

    public static bool IsConnected() {

      return bot?.ConnectionLock.IsSet ?? false;
    }

    //TODO: Catch stdout for ACT log

    async private static Task Bot_Ready(DSharpPlus.DiscordClient client, ReadyEventArgs e) {
      Log?.Invoke("Bot in ready state, awaiting server metadata...");
    }
    
    async private static Task Bot_Downloaded(DSharpPlus.DiscordClient client, GuildDownloadCompletedEventArgs e) {
      Log?.Invoke("Server metadata downloaded, populating lists...");
      BotReady?.Invoke();
    }

    async private static Task Bot_VoiceStateChanged(DSharpPlus.DiscordClient client, VoiceStateUpdateEventArgs e) {
      if (voiceChannel != null && client.CurrentUser == e.User && e.After.Channel == null){
        Log?.Invoke("Voice channel disconnect received.");
        LeaveChannel();
      }
    }

    public static string[] getServers() {
      List<string> servers = new List<string>();

      try {
        foreach (var kv in bot.Guilds) {
          var g = kv.Value;
          servers.Add(g.Name);
        }
      } catch (Exception ex) {
        Log?.Invoke("Error loading servers in DiscordAPI#getServers().");
        Log?.Invoke(ex.Message);
      }

      return servers.ToArray();
    }
    
    public static string[] getChannelNames(string server) {
      List<string> discordchannels = new List<string>();

      foreach (var kv in bot.Guilds) {
        var g = kv.Value;
        if (g.Name == server) {
          var channels = new List<DiscordChannel>(g.Channels.Select(pair => pair.Value).Where(c => c.Type == ChannelType.Voice));
          channels.Sort((x, y) => x.Position.CompareTo(y.Position));
          foreach (DiscordChannel channel in channels)
            discordchannels.Add(channel.Name);
          break;
        }
      }

      return discordchannels.ToArray();
    }

    private static DiscordChannel[] getChannels(string server) {
      List<DiscordChannel> discordchannels = new List<DiscordChannel>();

      foreach (var kv in bot.Guilds) {
        var g = kv.Value;
        if (g.Name == server) {
          var channels = new List<DiscordChannel>(g.Channels.Select(pair => pair.Value).Where(c => c.Type == ChannelType.Voice));
          channels.Sort((x, y) => x.Position.CompareTo(y.Position));
          foreach (DiscordChannel channel in channels)
            discordchannels.Add(channel);
          break;
        }
      }

      return discordchannels.ToArray();
    }

    public static void SetGameAsync(string text) {
      //bot.SetGameAsync(text);
    }

    public async static Task<bool> JoinChannel(string server, string channel) {
      DiscordChannel chan = null;

      foreach (var vchannel in getChannels(server))
        if (vchannel.Name == channel)
          chan = vchannel;

      if (chan != null) {
        try {
          voiceChannel = await chan.ConnectAsync();
          voiceStream = voiceChannel.GetTransmitSink();
          Log?.Invoke("Joined channel: " + chan.Name);
        } catch (Exception ex) {
          Log?.Invoke("Error joining channel.");
          Log?.Invoke($"Inner exception: {ex.InnerException?.Message ?? "null"}");
          Log?.Invoke($"Exception: {ex.Message}");
          return false;
        }
      }
      return true;
    }

    public static void LeaveChannel() {
      try {
        voiceStream = null;
        voiceChannel?.Disconnect();
        voiceChannel = null;
      } catch (Exception ex) {
        Log?.Invoke("Error leaving channel.");
        Log?.Invoke($"Inner exception: {ex.InnerException?.Message ?? "null"}");
        Log?.Invoke($"Exception: {ex.Message}");
      }
    }

    private static object speaklock = new object();
    private static SpeechAudioFormatInfo formatInfo = new SpeechAudioFormatInfo(48000, AudioBitsPerSample.Sixteen, AudioChannel.Stereo);

    public static void Speak(string text, string voice, int vol, int speed) {
      lock (speaklock) {
        SpeechSynthesizer tts = new SpeechSynthesizer();
        tts.SelectVoice(voice);
        tts.Volume = vol * 5;
        tts.Rate = speed - 10;
        MemoryStream ms = new MemoryStream();
        tts.SetOutputToAudioStream(ms, formatInfo);

        tts.Speak(text);
        ms.Seek(0, SeekOrigin.Begin);
        ms.CopyToAsync(voiceStream).GetAwaiter().GetResult();
      }
    }

    public static void SpeakFile(string path) {
      lock (speaklock) {
        try {
          WaveFileReader wav = new WaveFileReader(path);
          WaveFormat waveFormat = new WaveFormat(48000, 16, 2);
          WaveStream pcm = WaveFormatConversionStream.CreatePcmStream(wav);
          WaveFormatConversionStream output = new WaveFormatConversionStream(waveFormat, pcm);
          output.CopyToAsync(voiceStream).GetAwaiter().GetResult();
        } catch (Exception ex) {
          Log?.Invoke("Unable to read file: " + ex.Message);
        }
      }
    }
  }
}
