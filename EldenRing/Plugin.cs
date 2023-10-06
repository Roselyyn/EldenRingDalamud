using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Network;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Animation;
using Dalamud.Interface.Animation.EasingFunctions;
using Dalamud.Memory;
using Dalamud.Utility;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;

using Dalamud.Game.Command;
using Dalamud.Plugin;
using EldenRing.Audio;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;

namespace EldenRing
{
    public class EldenRing : IDalamudPlugin
    {
        public string Name => "Elden Ring April Fools";

        private const string commandName = "/eldenring";


        private DalamudPluginInterface PluginInterface { get; init; }

        private ICommandManager CommandManager { get; init; }

        private Configuration Configuration { get; init; }

        private PluginUI PluginUi { get; init; }

        private IDataManager DataManager { get; init; }

        private IFramework framework { get; init; }

        private IChatGui chatGui { get; init; }

        private IGameNetwork gameNetwork { get; init; }

        private ICondition condition { get; init; }

        private IPluginLog pluginLog { get; init; }


        private readonly IDalamudTextureWrap erDeathBgTexture;
        private readonly IDalamudTextureWrap erNormalDeathTexture;
        private readonly IDalamudTextureWrap erCraftFailedTexture;
        private readonly IDalamudTextureWrap erEnemyFelledTexture;

        private readonly string synthesisFailsMessage;

        private readonly Stopwatch time = new Stopwatch();

        private AudioHandler audioHandler { get; init; }


        private bool assetsReady = false;

        private AnimationState currentState = AnimationState.NotPlaying;
        private DeathType currentDeathType = DeathType.Death;

        private Easing alphaEasing;
        private Easing scaleEasing;

        private bool lastFrameUnconscious = false;

        private int msFadeInTime = 1000;
        private int msFadeOutTime = 2000;
        private int msWaitTime = 1600;

        private IDalamudTextureWrap TextTexture => this.currentDeathType switch
        {
            DeathType.Death => this.erNormalDeathTexture,
            DeathType.CraftFailed => this.erCraftFailedTexture,
            DeathType.EnemyFelled => this.erEnemyFelledTexture,
        };

        private enum AnimationState
        {
            NotPlaying,
            FadeIn,
            Wait,
            FadeOut,
        }

        private enum DeathType
        {
            Death,
            CraftFailed,
            EnemyFelled,
        }

        public EldenRing(DalamudPluginInterface pluginInterface, IDataManager dataManager, IFramework frameworkP, IChatGui chat, IGameNetwork game, ICondition Condition, ICommandManager commandManager, IPluginLog Log)
        {
            PluginInterface = pluginInterface;
            DataManager = dataManager;
            framework = frameworkP;
            chatGui = chat;
            gameNetwork = game;
            condition = Condition;
            CommandManager = commandManager;
            pluginLog = Log;

            Configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(pluginInterface);

            //var dalamud = Service<Dalamud>.Get();
            //var interfaceManager = Service<InterfaceManager>.Get();
            //var framework = Service<Framework>.Get();
            //var chatGui = Service<ChatGui>.Get();
            //var dataMgr = Service<DataManager>.Get();
            //var gameNetwork = Service<GameNetwork>.Get();

            erDeathBgTexture = PluginInterface.UiBuilder.LoadImage(Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "er_death_bg.png"))!;
            erNormalDeathTexture = PluginInterface.UiBuilder.LoadImage(Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "er_normal_death.png"))!;
            erCraftFailedTexture = PluginInterface.UiBuilder.LoadImage(Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "er_craft_failed.png"))!;
            erEnemyFelledTexture = PluginInterface.UiBuilder.LoadImage(Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "er_enemy_felled.png"))!;

            audioHandler = new(Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "snd_death_er.wav"));

            if (erDeathBgTexture == null || erNormalDeathTexture == null || erCraftFailedTexture == null)
            {
                pluginLog.Error("Elden: Failed to load images");
                return;
            }

            audioHandler.Volume = Configuration.Volume;
            int vol = (int)(Configuration.Volume * 100f);
            pluginLog.Debug($"Volume set to {vol}%");


            CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Used to control the volume of the audio using \"vol 0-100\""
            });


            synthesisFailsMessage = DataManager.GetExcelSheet<LogMessage>()!.GetRow(1160)!.Text.ToDalamudString().TextValue;

            assetsReady = true;

            PluginInterface.UiBuilder.Draw += Draw;
            framework.Update += FrameworkOnUpdate;
            chatGui.ChatMessage += ChatGuiOnChatMessage;
            gameNetwork.NetworkMessage += GameNetworkOnNetworkMessage;
        }

        private unsafe void GameNetworkOnNetworkMessage(IntPtr dataptr, ushort opcode, uint sourceactorid, uint targetactorid, NetworkMessageDirection direction)
        {
            // The opcode used is the one triggered by the func at this sig: 48 89 5C 24 ?? 48 89 6C 24 ?? 56 48 83 EC 20 83 3D ?? ?? ?? ?? ?? 41 0F B6 E8 48 8B DA 8B F1 0F 84 ?? ?? ?? ?? 48 89 7C 24 ??
            if (opcode != 559)
                return;
            

            var cat = *(ushort*)(dataptr + 0x00);
            var updateType = *(uint*)(dataptr + 0x08);

            if (cat == 0x6D && updateType == 0x40000003)
            {
                Task.Delay(1000).ContinueWith(t =>
                {
                    this.PlayAnimation(DeathType.EnemyFelled);
                });
            }
        }

        private void ChatGuiOnChatMessage(XivChatType type, uint senderid, ref SeString sender, ref SeString message, ref bool ishandled)
        {
            if (message.TextValue.Contains(this.synthesisFailsMessage))
            {
                this.PlayAnimation(DeathType.CraftFailed);
                pluginLog.Verbose("Elden: Craft failed");
            }
        }

        private void FrameworkOnUpdate(IFramework framework)
        {
            //var condition = Service<Condition>.Get();
            var isUnconscious = condition[ConditionFlag.Unconscious];

            if (isUnconscious && !this.lastFrameUnconscious)
            {
                this.PlayAnimation(DeathType.Death);
                pluginLog.Verbose($"Elden: Player died {isUnconscious}");
            }

            this.lastFrameUnconscious = isUnconscious;
        }

        private void Draw()
        {
//#if DEBUG
//            if (ImGui.Begin("fools test"))
//            {
//                if (ImGui.Button("play death"))
//                {
//                    this.PlayAnimation(DeathType.Death);
//                }

//                if (ImGui.Button("play craft failed"))
//                {
//                    this.PlayAnimation(DeathType.CraftFailed);
//                }

//                if (ImGui.Button("play enemy felled"))
//                {
//                    Task.Delay(1000).ContinueWith(t =>
//                    {
//                        this.PlayAnimation(DeathType.EnemyFelled);
//                    });
//                }

//                ImGui.InputInt("fade in time", ref this.msFadeInTime);
//                ImGui.InputInt("fade out time", ref this.msFadeOutTime);
//                ImGui.InputInt("wait time", ref this.msWaitTime);

//                ImGui.TextUnformatted("state: " + this.currentState);
//                ImGui.TextUnformatted("time: " + this.time.ElapsedMilliseconds);

//                ImGui.TextUnformatted("scale: " + this.scaleEasing?.EasedPoint.X);
//            }

//            ImGui.End();
//#endif
            var vpSize = ImGuiHelpers.MainViewport.Size;

            ImGui.SetNextWindowPos(new Vector2(0, 0), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(vpSize.X, vpSize.Y), ImGuiCond.Always);
            ImGuiHelpers.ForceNextWindowMainViewport();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0, 0, 0, 0));
            ImGui.PushStyleColor(ImGuiCol.BorderShadow, new Vector4(0, 0, 0, 0));

            if (ImGui.Begin("fools22", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus
                                       | ImGuiWindowFlags.NoMouseInputs | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoScrollbar))
            {
                if (this.currentState != AnimationState.NotPlaying)
                {
                    this.alphaEasing?.Update();
                    this.scaleEasing?.Update();
                }

                switch (this.currentState)
                {
                    case AnimationState.FadeIn:
                        this.FadeIn(vpSize);
                        break;
                    case AnimationState.Wait:
                        this.Wait(vpSize);
                        break;
                    case AnimationState.FadeOut:
                        this.FadeOut(vpSize);
                        break;
                }
            }

            ImGui.End();

            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(3);
        }

        private static void AdjustCursorAndDraw(Vector2 vpSize, IDalamudTextureWrap tex, float scale = 1.0f)
        {
            ImGui.SetCursorPos(new Vector2(0, 0));

            var width = vpSize.X;
            var height = tex.Height / (float)tex.Width * width;

            if (height < vpSize.Y)
            {
                height = vpSize.Y;
                width = tex.Width / (float)tex.Height * height;
            }

            var scaledSize = new Vector2(width, height) * scale;
            var difference = scaledSize - vpSize;

            var cursor = ImGui.GetCursorPos();
            ImGui.SetCursorPos(cursor - (difference / 2));

            ImGui.Image(tex.ImGuiHandle, scaledSize);
        }

        private void FadeIn(Vector2 vpSize)
        {
            if (this.time.ElapsedMilliseconds > this.msFadeInTime)
                this.currentState = AnimationState.Wait;

            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, (float)this.alphaEasing.Value);

            AdjustCursorAndDraw(vpSize, this.erDeathBgTexture);
            AdjustCursorAndDraw(vpSize, this.TextTexture, this.scaleEasing.EasedPoint.X);

            ImGui.PopStyleVar();
        }

        private void Wait(Vector2 vpSize)
        {
            if (this.time.ElapsedMilliseconds > this.msFadeInTime + this.msWaitTime)
            {
                this.currentState = AnimationState.FadeOut;
                this.alphaEasing = new InOutCubic(TimeSpan.FromMilliseconds(this.msFadeOutTime));
                this.alphaEasing.Start();
            }

            AdjustCursorAndDraw(vpSize, this.erDeathBgTexture);
            AdjustCursorAndDraw(vpSize, this.TextTexture, this.scaleEasing.EasedPoint.X);
        }

        private void FadeOut(Vector2 vpSize)
        {
            if (this.time.ElapsedMilliseconds > this.msFadeInTime + this.msWaitTime + this.msFadeOutTime)
            {
                this.currentState = AnimationState.NotPlaying;
                this.time.Stop();
            }

            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1 - (float)this.alphaEasing.Value);

            AdjustCursorAndDraw(vpSize, this.erDeathBgTexture);
            AdjustCursorAndDraw(vpSize, this.TextTexture, this.scaleEasing.EasedPoint.X);

            ImGui.PopStyleVar();
        }

        private void PlayAnimation(DeathType type)
        {

            if (this.currentState != AnimationState.NotPlaying)
                return;

            this.currentDeathType = type;

            this.currentState = AnimationState.FadeIn;
            this.alphaEasing = new InOutCubic(TimeSpan.FromMilliseconds(this.msFadeInTime));
            this.alphaEasing.Start();

            this.scaleEasing = new OutCubic(TimeSpan.FromMilliseconds(this.msFadeInTime + this.msWaitTime + this.msFadeOutTime))
            {
                Point1 = new Vector2(0.95f, 0.95f),
                Point2 = new Vector2(1.05f, 1.05f),
            };
            this.scaleEasing.Start();

            this.time.Reset();
            this.time.Start();

            if (this.CheckIsSfxEnabled())
            {
                audioHandler.PlaySound(AudioTrigger.Death);
                
            }
        }

        private unsafe bool CheckIsSfxEnabled()
        {
            try
            {
                var framework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance();
                var configBase = framework->SystemConfig.CommonSystemConfig.ConfigBase;

                var seEnabled = false;
                var masterEnabled = false;

                for (var i = 0; i < configBase.ConfigCount; i++)
                {
                    var entry = configBase.ConfigEntry[i];

                    if (entry.Name != null)
                    {
                        var name = MemoryHelper.ReadStringNullTerminated(new IntPtr(entry.Name));

                        if (name == "IsSndSe")
                        {
                            var value = entry.Value.UInt;
                            pluginLog.Verbose("Elden: {Name} - {Type} - {Value}", name, entry.Type, value);

                            seEnabled = value == 0;
                        }

                        if (name == "IsSndMaster")
                        {
                            var value = entry.Value.UInt;
                            pluginLog.Verbose("Elden: {Name} - {Type} - {Value}", name, entry.Type, value);

                            masterEnabled = value == 0;
                        }
                    }
                }

                return seEnabled && masterEnabled;
            }
            catch (Exception ex)
            {
                pluginLog.Error(ex, "Elden: Error checking if sfx is enabled");
                return true;
            }
        }

        public void Dispose()
        {
            PluginInterface.UiBuilder.Draw -= Draw;
            framework.Update -= FrameworkOnUpdate;
            chatGui.ChatMessage -= ChatGuiOnChatMessage;
            gameNetwork.NetworkMessage -= GameNetworkOnNetworkMessage;

            erDeathBgTexture.Dispose();
            erNormalDeathTexture.Dispose();
            erCraftFailedTexture.Dispose();
            Configuration.Save();

            CommandManager.RemoveHandler(commandName);
        }

        private void SetVolume(string vol)
        {
            try
            {
                var newVol = int.Parse(vol) / 100f;
                pluginLog.Debug($"{Name}: Setting volume to {newVol}");
                audioHandler.Volume = newVol;
                Configuration.Volume = newVol;
                chatGui.Print($"Volume set to {vol}%");
            }
            catch (Exception)
            {
                chatGui.PrintError("Please use a number between 0-100");
            }
        }

        private void OnCommand(string command, string args)
        {
            pluginLog.Debug("{Command} - {Args}", command, args);
            var argList = args.Split(' ');

            pluginLog.Debug(argList.Length.ToString());

            if (argList.Length == 0)
                return;


            // TODO: This is super rudimentary (garbage) argument parsing. Make it better
            switch (argList[0])
            {
                case "vol":
                    if (argList.Length != 2) return;
                    SetVolume(argList[1]);
                    break;
                case "":
                    // in response to the slash command, just display our main ui
                    //this.PluginUi.Visible = true;
                    chatGui.PrintError("Please use \"/eldenring vol <num>\" to control volume");
                    break;
                default:
                    break;
            }
        }
    }
}
