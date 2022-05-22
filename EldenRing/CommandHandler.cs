using Dalamud.Game.Gui;
using Dalamud.Logging;
using EldenRing.Audio;
using System;

namespace EldenRing
{
    public enum CommandType
    {
        Volume,
        Help,
        Invalid
    }
    internal class CommandHandler
    {
        private ChatGui ChatGui { get; init; }
        private AudioHandler AudioHandler { get; init; }
        private Configuration Configuration { get; init; }

        private readonly string VolumeError = "Please use a number between 0-100";

        public static CommandType GetCommandType(string arg)
        {
            return arg switch
            {
                "v" or "vol" => CommandType.Volume,
                "h" or "help" => CommandType.Help,
                _ => CommandType.Invalid,
            };
        }

        private void PrintCommandHelp()
        {
            ChatGui.Print(String.Join(Environment.NewLine, "/eldenring vol <0-100> - Sets sound volume", "/eldenring help - Print this help text"));
        }

        public void SetVolume(string vol)
        {
            try
            {
                var newVol = Math.Min(100f, float.Parse(vol)) / 100f;
                PluginLog.Debug($"Elden: Setting volume to {newVol}");
                AudioHandler.Volume = newVol;
                Configuration.Volume = newVol;
                ChatGui.Print($"Volume set to {(int)(newVol * 100f)}%");
            }
            catch (Exception e)
            {
                PluginLog.Error($"Elden: Got exception {e.Message} - {e.StackTrace}");
                ChatGui.PrintError(VolumeError);
            }
        }

        public void ProcessCommand(string command, string args)
        {
            if (string.IsNullOrEmpty(args))
            {
                // TODO: In future we might want to default to opening the config window here instead.
                PrintCommandHelp();
                return;
            }

            PluginLog.Debug("{Command} - {Args}", command, args);

            var argList = args.Split(' ');
            var commandType = GetCommandType(argList[0]);


            switch (commandType)
            {
                case CommandType.Volume:
                    if (argList.Length == 2)
                    {
                        SetVolume(argList[1]);
                    }
                    else
                    {
                        ChatGui.Print($"Volume is {(int)(AudioHandler.Volume * 100f)}%");
                    }
                    break;
                case CommandType.Help:
                    PrintCommandHelp();
                    break;
                case CommandType.Invalid:
                    ChatGui.PrintError("Invalid command");
                    break;
            }
        }

        public CommandHandler(ChatGui chatGui, AudioHandler audioHandler, Configuration configuration)
        {
            ChatGui = chatGui;
            AudioHandler = audioHandler;
            Configuration = configuration;
        }
    }
}
