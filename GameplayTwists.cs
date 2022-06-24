using System.Collections.Generic;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using Mono.CSharp;
using System.IO;
using System.Text;
using log4net;
using System;
using Terraria;
using System.Text.RegularExpressions;
using Terraria.ModLoader.Config.UI;
using Terraria.GameContent.UI.Elements;
using Terraria.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameInput;
using MonoMod.RuntimeDetour.HookGen;
using System.Reflection;
using Terraria.ID;
using GameplayTwists.UI;
using Newtonsoft.Json;
using Terraria.ModLoader.IO;
using System.Linq;

namespace GameplayTwists {
	public class GameplayTwists : Mod {
        public static GameplayTwists Instance { get; private set; }
		public static CompiledMethod[] ItemDisableConditions => Instance.itemDisableConditions;
		public static CompiledMethod[] EquipDisableConditions => Instance.equipDisableConditions;
		public static CompiledMethod[] EventDamageTaken => Instance.eventDamageTaken;
		public static CompiledMethod[] EventNPCKilled => Instance.eventNPCKilled;
		internal CompiledMethod[] itemDisableConditions, equipDisableConditions, eventDamageTaken, eventNPCKilled;
        public static Evaluator Evaluator { get; private set; }
        public override void Load() {
            if(Instance!=null) Logger.Info("GameplayTwists Instance already loaded at Load()");
            Instance = this;
			PatchworkUI.DoHook();
            //Main.Achievements.OnAchievementCompleted += OnAchievementCompleted;
			CompilerContext compilerContext = new CompilerContext(new CompilerSettings(), new ConsoleReportPrinter(new ILogTextWriter(Logger)));
			Evaluator = new Evaluator(compilerContext);
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
				if (assembly == null) {
					continue;
				}
				try {
					if (!assembly.IsDynamic &&
						!assembly.FullName.Contains("mscorlib") &&
						!assembly.FullName.Contains("System.Core,") &&
						!assembly.FullName.Contains("System,") &&
						!assembly.FullName.Contains("System")) {
						Evaluator.ReferenceAssembly(assembly);
						Logger.Info("Referenced assembly \""+assembly+'"');
					} else {
						Logger.Info("Ignored assembly \""+assembly+'"');
					}
				} catch (NullReferenceException) {}
			}
			try {
				Evaluator.Run("using Terraria;");
				Evaluator.Run("using Terraria.ID;");
				Evaluator.Run("using Microsoft.Xna.Framework;");
				Evaluator.Run("using GameplayTwists;");
			} catch (Exception e) {
				Logger.Error("error while importing: "+e);
			}
            this.AddConfig(typeof(TwistConfig).Name, new TwistConfig());
            this.AddConfig(typeof(PerPlayerTwistConfig).Name, new PerPlayerTwistConfig());
			On.Terraria.UI.ItemSlot.PickItemMovementAction += ItemSlot_PickItemMovementAction;
			On.Terraria.UI.ItemSlot.SwapEquip_ItemArray_int_int += ItemSlot_SwapEquip_ItemArray_int_int;
			On.Terraria.Collision.DrownCollision += Collision_DrownCollision;
		}
		internal static bool forceDrown = false;
		private bool Collision_DrownCollision(On.Terraria.Collision.orig_DrownCollision orig, Vector2 Position, int Width, int Height, float gravDir, bool includeSlopes) {
			return forceDrown || orig(Position, Width, Height, gravDir, includeSlopes);
		}

		private void ItemSlot_SwapEquip_ItemArray_int_int(On.Terraria.UI.ItemSlot.orig_SwapEquip_ItemArray_int_int orig, Item[] inv, int context, int slot) {
			if (IsEquipDisabled(inv[slot])) {
				return;
			}
			orig(inv, context, slot);
		}

		private int ItemSlot_PickItemMovementAction(On.Terraria.UI.ItemSlot.orig_PickItemMovementAction orig, Item[] inv, int context, int slot, Item checkItem) {
			switch (context) {
				case ItemSlot.Context.EquipArmor:
				case ItemSlot.Context.EquipAccessory:
				case ItemSlot.Context.EquipLight:
				case ItemSlot.Context.EquipMinecart:
				case ItemSlot.Context.EquipMount:
				case ItemSlot.Context.EquipPet:
				case ItemSlot.Context.EquipGrapple: {
					if (IsEquipDisabled(checkItem)) {
						return -1;
					}
				}
				break;
			}
			return orig(inv, context, slot, checkItem);
		}
		bool IsEquipDisabled(Item item) {
			TwistEnvironment.item = item;
			TwistEnvironment.player = Main.LocalPlayer;
			if (GameplayTwists.EquipDisableConditions.CombineBoolReturns(isStatic:true)) {
				return true;
			}
			TwistPlayer twistPlayer = Main.LocalPlayer.GetModPlayer<TwistPlayer>();
			if (twistPlayer.equipDisableConditions.CombineBoolReturns(isStatic:false)) {
				return true;
			}
			return false;
		}

		internal void RefreshItemRestrictions(List<string> values, out CompiledMethod[] output) {
			output = null;
			if (!(values is null || Evaluator is null)) {
				List<CompiledMethod> compiledRestrictions = new List<CompiledMethod>(values.Count);
				for (int i = 0; i < values.Count; i++) {
					try {
						CompiledMethod _output = Evaluator.Compile(TwistEnvironment.Process(values[i]));
						if (_output is null) {
							Logger.Error("occurred while compiling " + values[i]);
						}
						compiledRestrictions.Add(_output);
					} catch (Exception) { }
				}
				compiledRestrictions.RemoveAll(v => v is null);
				output = compiledRestrictions.ToArray();
			}
		}
        public override void Unload() {
            if(Instance==null) Logger.Info("WorldTwists Instance already unloaded at Unload()");
            Instance = null;
        }
	}
	[Label("Settings")]
    public class TwistConfig : ModConfig {
        public static TwistConfig Instance;
        public override ConfigScope Mode => ConfigScope.ClientSide;
        public override bool Autoload(ref string name) {
            return false;
        }

        [Header("Item Disable Conditions")]
        
		[CustomModConfigItemList(typeof(LargeStringInputElement))]
        [Label("Item Use Disable Conditions")]
        public List<string> itemUseDisableConditions;
        
		[CustomModConfigItemList(typeof(LargeStringInputElement))]
        [Label("Equipment Disable Conditions")]
        public List<string> equipDisableConditions;

        [Header("Events")]

		[CustomModConfigItemList(typeof(LargeStringInputElement))]
        [Label("Damage Taken")]
        public List<string> eventDamageTaken;

		[CustomModConfigItemList(typeof(LargeStringInputElement))]
        [Label("Killed Enemy")]
        public List<string> eventNPCKilled;

		[Label("Variables")]
		public TwistVars vars;

		public override void OnChanged() {
			GameplayTwists.Instance.RefreshItemRestrictions(itemUseDisableConditions, out GameplayTwists.Instance.itemDisableConditions);
			GameplayTwists.Instance.RefreshItemRestrictions(equipDisableConditions, out GameplayTwists.Instance.equipDisableConditions);
			GameplayTwists.Instance.RefreshItemRestrictions(eventDamageTaken, out GameplayTwists.Instance.eventDamageTaken);
			GameplayTwists.Instance.RefreshItemRestrictions(eventNPCKilled, out GameplayTwists.Instance.eventNPCKilled);
		}
	}
	[Label("Per-Player Settings")]
    public class PerPlayerTwistConfig : ModConfig {
        public static PerPlayerTwistConfig Instance;
        public override ConfigScope Mode => ConfigScope.ClientSide;
        public override bool Autoload(ref string name) {
            return false;
        }

        [Header("Item Disable Conditions")]

		[JsonIgnore]
		[CustomModConfigItemList(typeof(LargeStringInputElement))]
		[Label("Item Use Disable Conditions")]
		public List<string> itemUseDisableConditions {
			get {
				if (Main.LocalPlayer?.active??false) {
					return Main.LocalPlayer.GetModPlayer<TwistPlayer>().itemUseDisableSource;
				}
				return null;
			}
			set {
				if (Main.LocalPlayer?.active??false) {
					if (Main.LocalPlayer.GetModPlayer<TwistPlayer>() is TwistPlayer twistPlayer) {
						twistPlayer.itemUseDisableSource = value;
					}
				}
			}
		}
        
		[JsonIgnore]
		[CustomModConfigItemList(typeof(LargeStringInputElement))]
        [Label("Equipment Disable Conditions")]
        public List<string> equipDisableConditions {
			get {
				if (Main.LocalPlayer?.active??false) {
					return Main.LocalPlayer.GetModPlayer<TwistPlayer>().equipDisableSource;
				}
				return null;
			}
			set {
				if (Main.LocalPlayer?.active??false) {
					if (Main.LocalPlayer.GetModPlayer<TwistPlayer>() is TwistPlayer twistPlayer) {
						twistPlayer.equipDisableSource = value;
					}
				}
			}
		}

        [Header("Events")]
		
		[JsonIgnore]
		[CustomModConfigItemList(typeof(LargeStringInputElement))]
        [Label("Damage Taken")]
        public List<string> eventDamageTaken {
			get {
				if (Main.LocalPlayer?.active??false) {
					return Main.LocalPlayer.GetModPlayer<TwistPlayer>().damageTakenSource;
				}
				return null;
			}
			set {
				if (Main.LocalPlayer?.active??false) {
					if (Main.LocalPlayer.GetModPlayer<TwistPlayer>() is TwistPlayer twistPlayer) {
						twistPlayer.damageTakenSource = value;
					}
				}
			}
		}
		
		[JsonIgnore]
		[CustomModConfigItemList(typeof(LargeStringInputElement))]
        [Label("Killed Enemy")]
        public List<string> eventNPCKilled {
			get {
				if (Main.LocalPlayer?.active??false) {
					return Main.LocalPlayer.GetModPlayer<TwistPlayer>().NPCKilledSource;
				}
				return null;
			}
			set {
				if (Main.LocalPlayer?.active??false) {
					if (Main.LocalPlayer.GetModPlayer<TwistPlayer>() is TwistPlayer twistPlayer) {
						twistPlayer.NPCKilledSource = value;
					}
				}
			}
		}

		public override void OnChanged() {
			if (Main.LocalPlayer?.active??false) {
				if (Main.LocalPlayer.GetModPlayer<TwistPlayer>() is TwistPlayer twistPlayer) {
					GameplayTwists.Instance.RefreshItemRestrictions(itemUseDisableConditions, out twistPlayer.itemDisableConditions);
					GameplayTwists.Instance.RefreshItemRestrictions(equipDisableConditions, out twistPlayer.equipDisableConditions);
					GameplayTwists.Instance.RefreshItemRestrictions(eventDamageTaken, out twistPlayer.eventDamageTaken);
					GameplayTwists.Instance.RefreshItemRestrictions(eventNPCKilled, out twistPlayer.eventNPCKilled);
				}
			}
		}
	}
	public class TwistVars : ModConfig {
		public override ConfigScope Mode => ConfigScope.ClientSide;
        public override bool Autoload(ref string name) {
            return false;
        }
		[CustomModConfigItem(typeof(VariableSetElement))]
		public VariableSet variables {
			get {
				return TwistEnvironment.vars = TwistEnvironment.vars ?? new VariableSet();
			}
			set {
				TwistEnvironment.vars = value ?? new VariableSet();
			}
		}
	}
	public class TwistPlayer : ModPlayer {
		internal List<string> itemUseDisableSource, equipDisableSource, damageTakenSource, NPCKilledSource;
		internal CompiledMethod[] itemDisableConditions = new CompiledMethod[0], equipDisableConditions = new CompiledMethod[0], eventDamageTaken = new CompiledMethod[0], eventNPCKilled = new CompiledMethod[0];
		private VariableSet vars;
		public VariableSet variables {
			get {
				return vars = vars ?? new VariableSet();
			}
			set {
				vars = value ?? new VariableSet();
			}
		}
		public override void Hurt(bool pvp, bool quiet, double damage, int hitDirection, bool crit) {
			TwistEnvironment.player = Player;
			TwistEnvironment.damage = damage;
			GameplayTwists.EventDamageTaken.ExecuteAll(isStatic:true);
			eventDamageTaken.ExecuteAll(isStatic:false);
		}
		public override void OnHitNPC(Item item, NPC target, int damage, float knockback, bool crit) {
			if (target.life <= 0) {
				TwistEnvironment.item = item;
				OnKillNPC(target);
			}
		}
		public override void OnHitNPCWithProj(Projectile proj, NPC target, int damage, float knockback, bool crit) {
			if (target.life <= 0) {
				OnKillNPC(target);
			}
		}
		public override void ResetEffects() {
			/*if (TwistConfig.Instance.KeepAnvil && TwistWorld.pairings is not null) {
				bool[] oldAdjTile = Player.adjTile.ToArray();
				KeyValuePair<ushort, ushort>[] pairings = TwistWorld.pairings.ToArray();
				for (int i = 0; i < pairings.Length; i++) {
					Player.adjTile[pairings[i].Key] = oldAdjTile[pairings[i].Value];
				}
			}*/
		}
		void OnKillNPC(NPC target) {
			TwistEnvironment.npc = target;
			TwistEnvironment.player = Player;
			GameplayTwists.EventNPCKilled.ExecuteAll(isStatic:true);
			eventNPCKilled.ExecuteAll(isStatic:false);
		}
		public override void SaveData(TagCompound tag){
			tag.Add("itemUseDisableSource", itemUseDisableSource);
			tag.Add("equipDisableSource", equipDisableSource);
			tag.Add("damageTakenSource", damageTakenSource);
			tag.Add("NPCKilledSource", NPCKilledSource);
		}
		public override void LoadData(TagCompound tag) {
			itemUseDisableSource = tag.ContainsKey("itemUseDisableSource") ? (List<string>)tag.GetList<string>("itemUseDisableSource") : new List<string>();
			equipDisableSource = tag.ContainsKey("equipDisableSource") ? (List<string>)tag.GetList<string>("equipDisableSource") : new List<string>();
			damageTakenSource = tag.ContainsKey("damageTakenSource") ? (List<string>)tag.GetList<string>("damageTakenSource") : new List<string>();
			NPCKilledSource = tag.ContainsKey("NPCKilledSource") ? (List<string>)tag.GetList<string>("NPCKilledSource") : new List<string>();
		}
		public override void OnEnterWorld(Player player) {
			PerPlayerTwistConfig.Instance.OnChanged();
		}
	}
	public class TwistGlobalItem : GlobalItem {
		public override bool CanUseItem(Item item, Player player) {
			TwistEnvironment.item = item;
			TwistEnvironment.player = player;
			if (GameplayTwists.ItemDisableConditions.CombineBoolReturns(isStatic:true)) {
				return false;
			}
			return !Main.LocalPlayer.GetModPlayer<TwistPlayer>().itemDisableConditions.CombineBoolReturns(isStatic:false);
		}
		public override void ModifyTooltips(Item item, List<TooltipLine> tooltips) {
			TwistEnvironment.item = item;
			bool disabled = false;
			if (item.useStyle != 0) {
				disabled = disabled ||
					GameplayTwists.ItemDisableConditions.CombineBoolReturns(isStatic:true)||
					Main.LocalPlayer.GetModPlayer<TwistPlayer>().itemDisableConditions.CombineBoolReturns(isStatic:false);
			}
			if (item.accessory || item.headSlot != -1 || item.bodySlot != -1 || item.legSlot != -1 || item.mountType != -1) {
				disabled = disabled ||
					GameplayTwists.EquipDisableConditions.CombineBoolReturns(isStatic:true)||
					Main.LocalPlayer.GetModPlayer<TwistPlayer>().equipDisableConditions.CombineBoolReturns(isStatic:false);
			}
			if (disabled) {
				tooltips.Add(new TooltipLine(Mod, "conditional", "[c/ff0000:Restricted]"));
			}
		}
	}
	public static class TwistEnvironment {
		public static Player player { get; internal set; }
		public static Item item { get; internal set; }
		public static double damage { get; internal set; }
		public static NPC npc { get; internal set; }
		public static VariableSet staticVars { get; internal set; }
		public static bool IsStatic { get; internal set; } = true;
		public static VariableSet vars {
			get {
				if (IsStatic) return staticVars;
				if (Main.LocalPlayer?.active??false) {
					return Main.LocalPlayer.GetModPlayer<TwistPlayer>().variables;
				}
				return null;
			}
			internal set {
				if (IsStatic) staticVars = value;
				if (Main.LocalPlayer?.active??false) {
					Main.LocalPlayer.GetModPlayer<TwistPlayer>().variables = value;
				}
			}
		}
		public static string Process(string text) {
			return Regex.Replace(
					Regex.Replace(text, "vars\\[([^\"]+)\\]", "vars[\"$1\"]"),
					"(?<!\\w|\\.)(vars|item|player|damage|npc)(?!\\w)", "TwistEnvironment.$1"
				);
		}
	}
	public class VariableSet : Dictionary<string, object> {
		public new object this[string key] {
			get => ContainsKey(key)?base[key]:null;
			set {
				if (value is null) {
					Remove(key);
				} else {
					base[key] = value;
				}
			}
		}
		public T GetVar<T>(string key) {
			return (T)(this[key]??default(T));
		}
	}
	public static class TwistExtensions {
		public static bool CombineBoolReturns(this CompiledMethod[] methods, bool useOr = true, bool isStatic = true) {
			TwistEnvironment.IsStatic = isStatic;
			bool ret = !useOr;
			for (int i = 0; i < methods.Length; i++) {
				if (useOr) {
					object value = false;
					methods[i](ref value);
					if (value is bool v) {
						ret = ret || v;
					}
				} else {
					object value = true;
					methods[i](ref value);
					if (value is bool v) {
						ret = ret && v;
					}
				}
			}
			return ret;
		}
		public static void ExecuteAll(this CompiledMethod[] methods, bool isStatic = true) {
			TwistEnvironment.IsStatic = isStatic;
			object value = null;
			for (int i = 0; i < methods.Length; i++) {
				try {
					methods[i](ref value);
				} catch (Exception) { }
			}
		}
		public static string GetMultilineClipboard(this ReLogic.OS.Platform platform) {
			string clipboard = (string)typeof(ReLogic.OS.Platform).GetMethod("GetClipboard", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(platform, new object[0]);
			StringBuilder stringBuilder = new StringBuilder(clipboard.Length);
			for (int i = 0; i < clipboard.Length; i++) {
				switch (clipboard[i]) {
					case '\n':
					stringBuilder.Append(clipboard[i]);
					break;
					case '\u007f':
					break;
					default:
					if (clipboard[i] >= ' ') {
						stringBuilder.Append(clipboard[i]);
					}
					break;
				}
			}
			return stringBuilder.ToString();
		}
	}
	public class ILogTextWriter : TextWriter {
		public override Encoding Encoding => Encoding.UTF8;
		readonly ILog log;
		private string buffer = "";
		public ILogTextWriter(ILog logger) {
			log = logger;
		}
		public override void Write(char value) {
			if (value == '\n') {
				if (!Main.gameMenu) {
					Main.NewText(buffer);
				}
				log.Info(buffer);
				buffer = "";
			} else {
				buffer += value;
			}
		}
	}
}