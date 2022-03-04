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

namespace GameplayTwists {
	public class GameplayTwists : Mod {
        public static GameplayTwists Instance { get; private set; }
		public CompiledMethod[] ItemDisableConditions => itemDisableConditions;
		public CompiledMethod[] EquipDisableConditions => equipDisableConditions;
		public CompiledMethod[] EventDamageTaken => eventDamageTaken;
		internal CompiledMethod[] itemDisableConditions, equipDisableConditions, eventDamageTaken;
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
				Evaluator.Run("using GameplayTwists;");
			} catch (Exception e) {
				Logger.Error("error while importing: "+e);
			}
            this.AddConfig(typeof(TwistConfig).Name, new TwistConfig());
			On.Terraria.UI.ItemSlot.PickItemMovementAction += ItemSlot_PickItemMovementAction;
			On.Terraria.UI.ItemSlot.SwapEquip_ItemArray_int_int += ItemSlot_SwapEquip_ItemArray_int_int;
		}

		private void ItemSlot_SwapEquip_ItemArray_int_int(On.Terraria.UI.ItemSlot.orig_SwapEquip_ItemArray_int_int orig, Item[] inv, int context, int slot) {
			TwistEnvironment.item = inv[slot];
			TwistEnvironment.player = Main.LocalPlayer;
			if (GameplayTwists.Instance.EquipDisableConditions.CombineBoolReturns()) {
				return;
			}
			orig(inv, context, slot);
		}

		private int ItemSlot_PickItemMovementAction(On.Terraria.UI.ItemSlot.orig_PickItemMovementAction orig, Item[] inv, int context, int slot, Item checkItem) {
			switch (context) {
				case ItemSlot.Context.EquipArmor:
				case ItemSlot.Context.EquipArmorVanity:
				case ItemSlot.Context.EquipAccessory:
				case ItemSlot.Context.EquipAccessoryVanity:
				case ItemSlot.Context.EquipDye:
				case ItemSlot.Context.EquipLight:
				case ItemSlot.Context.EquipMinecart:
				case ItemSlot.Context.EquipMount:
				case ItemSlot.Context.EquipPet:
				case ItemSlot.Context.EquipGrapple: {
					TwistEnvironment.item = checkItem;
					TwistEnvironment.player = Main.LocalPlayer;
					if (GameplayTwists.Instance.EquipDisableConditions.CombineBoolReturns()) {
						return -1;
					}
				}
				break;
			}
			return orig(inv, context, slot, checkItem);
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

		[Label("Variables")]
		public TwistVars vars;

		public override void OnChanged() {
			GameplayTwists.Instance.RefreshItemRestrictions(itemUseDisableConditions, out GameplayTwists.Instance.itemDisableConditions);
			GameplayTwists.Instance.RefreshItemRestrictions(equipDisableConditions, out GameplayTwists.Instance.equipDisableConditions);
			GameplayTwists.Instance.RefreshItemRestrictions(eventDamageTaken, out GameplayTwists.Instance.eventDamageTaken);
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
		public override void Hurt(bool pvp, bool quiet, double damage, int hitDirection, bool crit) {
			TwistEnvironment.player = player;
			TwistEnvironment.damage = damage;
			GameplayTwists.Instance.EventDamageTaken.ExecuteAll();
		}
	}
	public class TwistGlobalItem : GlobalItem {
		public override bool CanUseItem(Item item, Player player) {
			TwistEnvironment.item = item;
			TwistEnvironment.player = player;
			return !GameplayTwists.Instance.ItemDisableConditions.CombineBoolReturns();
		}
		public override bool CanEquipAccessory(Item item, Player player, int slot) {
			TwistEnvironment.item = item;
			TwistEnvironment.player = player;
			return !GameplayTwists.Instance.EquipDisableConditions.CombineBoolReturns();
		}
		public override void ModifyTooltips(Item item, List<TooltipLine> tooltips) {
			TwistEnvironment.item = item;
			bool disabled = false;
			if (item.useStyle != 0) {
				disabled = disabled || GameplayTwists.Instance.ItemDisableConditions.CombineBoolReturns();
			}
			if (item.accessory) {
				disabled = disabled || GameplayTwists.Instance.EquipDisableConditions.CombineBoolReturns();
			}
			if (disabled) {
				tooltips.Add(new TooltipLine(mod, "conditional", "[c/ff0000:Restricted]"));
			}
		}
	}
	public static class TwistEnvironment {
		public static Player player { get; internal set; }
		public static Item item { get; internal set; }
		public static double damage { get; internal set; }
		public static VariableSet vars { get; internal set; }
		public static string Process(string text) {
			return Regex.Replace(
					Regex.Replace(text, "vars\\[([^\"]+)\\]", "vars[\"$1\"]"),
					"(vars|item|player|damage)", "GameplayTwists.TwistEnvironment.$1"
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
		public static bool CombineBoolReturns(this CompiledMethod[] methods, bool useOr = true) {
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
		public static void ExecuteAll(this CompiledMethod[] methods) {
			object value = null;
			for (int i = 0; i < methods.Length; i++) {
				try {
					methods[i](ref value);
				} catch (Exception) { }
			}
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