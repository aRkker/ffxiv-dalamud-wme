using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldMapFixer
{
    public class Service
    {
        [PluginService]
        public static ClientState ClientState { get; private set; } = null;

		[PluginService]
		public static Framework Framework { get; private set; } = null;

		[PluginService]
		public static GameGui GameGui { get; private set; } = null;

		[PluginService]
		public static SigScanner SigScanner { get; private set; } = null;
	}
}
