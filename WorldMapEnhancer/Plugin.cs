using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using System;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace WorldMapFixer
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "World Map Enhancer";

        private const string commandName = "/wme";

        private DalamudPluginInterface PluginInterface { get; init; }
        private CommandManager CommandManager { get; init; }
        private Configuration Configuration { get; init; }
        private PluginUI PluginUi { get; init; }
        public Hook<OnMouseChange> MouseDelegateHook { get; private set; }
        public long rcStartTime { get; private set; }

        public bool alertedDebugWindow = false;
        private bool areaMapVisible;

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager,
            [RequiredVersion("1.0")] Dalamud.Game.Gui.GameGui gameGui)


        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;

            pluginInterface.Create<Service>(Array.Empty<object>());

            Service.Framework.Update += Framework_Update;

            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);


    
            this.PluginUi = new PluginUI(this.Configuration);

            this.CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Configuration for the right click behaviour"
            });

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

            this.InstallMouseHook();

        }

        private void InstallMouseHook()
        {
            var Signature = "48 89 5C 24 10 48 89 74 24 ?? 57 48 83 EC 20 49 8B F8 C6 05";

            var mouseClickAddress = Service.SigScanner.ScanText(Signature);

            this.MouseDelegateHook = new Hook<OnMouseChange>(mouseClickAddress, this.Detour);
        }

        public delegate void OnMouseChange(int param1, int param2, int param3);


        private unsafe void Detour(int param1, int param2, int param3)
        {
            if (param2 == 516 && param3 == 2)
            {
                this.rcStartTime = Environment.TickCount64;
            }

            if (param2 == 123 && param3 > 0)
            {
                if (Environment.TickCount64 - this.rcStartTime < this.Configuration.RightClickDelay && this.Configuration.Enabled && Service.ClientState.IsLoggedIn)
                {
                    var mapParams = GetMapParams();

                    var mousePosition = ImGui.GetMousePos();

                    if ((mousePosition.X >= mapParams.X && mousePosition.X <= mapParams.X + mapParams.Z) && (mousePosition.Y >= mapParams.Y && mousePosition.Y <= mapParams.Y + mapParams.W))
                    {
                        this.ZoomOutMap();
                    }
                }
            }
            this.MouseDelegateHook.Original(param1, param2, param3);
        }

        public unsafe Vector4 GetMapParams()
        {
            if (this.areaMapVisible)
            {
                var mapPtr = GetAreaMapPointer();

                return new Vector4(mapPtr->RootNode->X, mapPtr->RootNode->Y, mapPtr->RootNode->GetWidth(), mapPtr->RootNode->GetHeight());
            }

            return new Vector4(float.MinValue, float.MinValue, float.MinValue, float.MinValue);
        }

        public unsafe static void GenerateCallback(AtkUnitBase* unitBase, params object[] values)
        {
            AtkValue* atkValues = (AtkValue*)((void*)Marshal.AllocHGlobal(values.Length * sizeof(AtkValue)));
            if (atkValues == null)
            {
                return;
            }
            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    object v = values[i];
                    if (v is uint)
                    {
                        uint uintValue = (uint)v;
                        atkValues[i].Type = ValueType.UInt;
                        atkValues[i].UInt = uintValue;
                    }
                    else if (v is int)
                    {
                        int intValue = (int)v;
                        atkValues[i].Type = ValueType.Int;
                        atkValues[i].Int = intValue;
                    }
                    else if (v is float)
                    {
                        float floatValue = (float)v;
                        atkValues[i].Type = ValueType.Float;
                        atkValues[i].Float = floatValue;
                    }
                    else if (v is bool)
                    {
                        bool boolValue = (bool)v;
                        atkValues[i].Type = ValueType.Bool;
                        atkValues[i].Byte = ((byte)(boolValue ? 1 : 0));
                    }
                    else
                    {
                        string stringValue = v as string;
                        if (stringValue == null)
                        {
                            throw new ArgumentException(string.Format("Unable to convert type {0} to AtkValue", v.GetType()));
                        }
                        atkValues[i].Type = ValueType.String;
                        byte[] stringBytes = Encoding.UTF8.GetBytes(stringValue);
                        IntPtr stringAlloc = Marshal.AllocHGlobal(stringBytes.Length + 1);
                        Marshal.Copy(stringBytes, 0, stringAlloc, stringBytes.Length);
                        Marshal.WriteByte(stringAlloc, stringBytes.Length, 0);
                        atkValues[i].String = (byte*)((void*)stringAlloc);
                    }
                }
                unitBase->FireCallback(values.Length, atkValues, null);
            }
            finally
            {
                for (int j = 0; j < values.Length; j++)
                {
                    if (atkValues[j].Type == ValueType.String)
                    {
                        Marshal.FreeHGlobal(new IntPtr((void*)atkValues[j].String));
                    }
                }
                Marshal.FreeHGlobal(new IntPtr((void*)atkValues));
            }
        }



        private unsafe AtkUnitBase* GetAreaMapPointer()
        {
            IntPtr addonPtr = Service.GameGui.GetAddonByName("AreaMap", 1);
            if (!(addonPtr == IntPtr.Zero))
            {
                AtkUnitBase* ptr = (AtkUnitBase*)((void*)addonPtr);
                if (ptr->RootNode != null)
                {
                    return ptr;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        private unsafe void CheckForMap()
        {
            IntPtr addonPtr = Service.GameGui.GetAddonByName("AreaMap", 1);
            if (!(addonPtr == IntPtr.Zero))
            {
                AtkUnitBase* ptr = (AtkUnitBase*)((void*)addonPtr);
                if (ptr->RootNode != null)
                {
                    if (ptr->IsVisible)
                    {
                        if (!this.areaMapVisible)
                        {
                            PluginLog.LogDebug($"AreaMap visible, X: {ptr->X}, Y: {ptr->Y}, Scale: {ptr->GetScale()}, W: {ptr->RootNode->GetWidth()}, H: {ptr->RootNode->GetHeight()}");
                            this.areaMapVisible = true;
                            if (this.MouseDelegateHook == null)
                            {
                                this.InstallMouseHook();
                            }
                            this.MouseDelegateHook.Enable();
                        }
                    }
                    else
                    {
                        if (this.areaMapVisible)
                        {
                            PluginLog.LogDebug("AreaMap invisible");
                            this.areaMapVisible = false;
                            if (this.MouseDelegateHook == null)
                            {
                                this.InstallMouseHook();
                            }
                            this.MouseDelegateHook.Disable();
                        }
                    }
                }
            }
        }

        private void Framework_Update(Dalamud.Game.Framework framework)
        {
            if (this.Configuration.Enabled && Service.ClientState.IsLoggedIn)
            CheckForMap();
        }

        public void Dispose()
        {
            this.PluginUi.Dispose();
            this.CommandManager.RemoveHandler(commandName);
            Service.Framework.Update -= Framework_Update;
            this.MouseDelegateHook.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            // in response to the slash command, just display our main ui
            this.PluginUi.SettingsVisible = true;
        }



        private void DrawUI()
        {
            this.PluginUi.Draw();
        }


        private unsafe void ZoomOutMap()
        {
            var ptr = this.GetAreaMapPointer();
            GenerateCallback((AtkUnitBase*)ptr, new object[] { 5 });
        }

        private void DrawConfigUI()
        {
            this.PluginUi.SettingsVisible = true;
        }
    }
}
