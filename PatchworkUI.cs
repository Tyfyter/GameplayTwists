using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoMod.RuntimeDetour.HookGen;
using ReLogic.Graphics;
using ReLogic.OS;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ModLoader.Config;
using Terraria.ModLoader.Config.UI;
using Terraria.UI;

namespace GameplayTwists.UI {
	public static class PatchworkUI {
		internal static Type UIModConfig => typeof(ConfigElement).Assembly.GetType("Terraria.ModLoader.Config.UI.UIModConfig");
		internal static void DoHook() {
            HookEndpointManager.Add(UIModConfig.GetMethod("WrapIt", BindingFlags.Public|BindingFlags.Static), (hook_WrapIt)Impl_WrapIt);
		}
		internal static Tuple<UIElement, UIElement> Impl_WrapIt(orig_WrapIt orig, UIElement parent, ref int top, PropertyFieldWrapper memberInfo, object item, int order, object list = null, Type arrayType = null, int index = -1) {
			if (list is object) {
				int num = 0;
				Type type = memberInfo.Type;
				if (arrayType != null) {
					type = arrayType;
				}
				CustomModConfigItemListAttribute customAttribute = ConfigManager.GetCustomAttribute<CustomModConfigItemListAttribute>(memberInfo, null, null);
				if (customAttribute != null) {
					UIElement uIElement;
					Type t = customAttribute.t;
					if (typeof(ConfigElement).IsAssignableFrom(t)) {
						ConstructorInfo constructor = t.GetConstructor(new Type[0]);
						uIElement = ((!(constructor != null)) ? new UIText(t.Name + " specified via CustomModConfigItem for " + memberInfo.Name + " does not have an empty constructor.") : (constructor.Invoke(new object[0]) as UIElement));
					} else {
						uIElement = new UIText(t.Name + " specified via CustomModConfigItem for " + memberInfo.Name + " does not inherit from ConfigElement.");
					}
					if (uIElement is ConfigElement configElement) {
						configElement.Bind(memberInfo, item, (IList)list, index);
						configElement.OnBind();
					}
					uIElement.Recalculate();
					num = (int)uIElement.GetOuterDimensions().Height;
					UIElement container = (UIElement)UIModConfig.GetMethod("GetContainer", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[]{uIElement, (index == -1) ? order : index});
					container.Height.Pixels = num;
					if (parent is UIList uIList) {
						uIList.Add(container);
						uIList.GetTotalHeight();
					} else {
						container.Top.Pixels = top;
						container.Width.Pixels = -20f;
						container.Left.Pixels = 20f;
						top += num + 4;
						parent.Append(container);
						parent.Height.Set(top, 0f);
					}
					Tuple<UIElement, UIElement> tuple = new Tuple<UIElement, UIElement>(container, uIElement);
					return tuple;
				}
			}
			return orig(parent, ref top, memberInfo, item, order, list, arrayType, index);
		}
        internal delegate Tuple<UIElement, UIElement> orig_WrapIt(UIElement parent, ref int top, PropertyFieldWrapper memberInfo, object item, int order, object list = null, Type arrayType = null, int index = -1);
        internal delegate Tuple<UIElement, UIElement> hook_WrapIt(orig_WrapIt orig, UIElement parent, ref int top, PropertyFieldWrapper memberInfo, object item, int order, object list = null, Type arrayType = null, int index = -1);
	}
	internal class LargeStringInputElement : ConfigElement<string> {
		StyleDimension perLineHeight;
		public override void OnBind() {
			base.OnBind();
			perLineHeight = Height;
			drawLabel = false;
			UIPanel uIPanel = new UIPanel();
			uIPanel.SetPadding(0f);
			UIFocusInputTextField uIInputTextField = new UIFocusInputTextField("Type here");
			uIPanel.Top.Set(0f, 0f);
			uIPanel.Left.Set(0f, 0f);
			uIPanel.Width.Set(0f, 1f);
			uIPanel.Height.Set(30f, 1f);
			Append(uIPanel);
			uIInputTextField.SetText(Value);
			uIInputTextField.Top.Set(5f, 0f);
			uIInputTextField.Left.Set(10f, 0f);
			uIInputTextField.Width.Set(-52f, 1f);
			uIInputTextField.Height.Set(-10f, 1f);
			UIModConfigHoverImage expandButton = new UIModConfigHoverImage(expandedTexture, "Collapse");
			expandButton.Top.Set(4, 0f); // 10, -25: 4, -52
			expandButton.Left.Set(-30, 1f);
			bool expanded = true;
			expandButton.OnClick += (a, b) => {
				expanded = !expanded;
				if (expanded) {
					expandButton.HoverText = "Collapse";
					expandButton.SetImage(expandedTexture);
				} else {
					expandButton.HoverText = "Expand";
					expandButton.SetImage(collapsedTexture);
				}
				RecalculateHeight(expanded?null:(int?)1);
			};
			RecalculateHeight();
			uIInputTextField.OnTextChange += delegate {
				Value = uIInputTextField.CurrentString;
				RecalculateHeight();
			};
			uIPanel.Append(uIInputTextField);
			uIPanel.Append(expandButton);
		}
		public void RecalculateHeight(int? lines = null) {
			int lineCount = lines??Value.Split('\n').Length;
			float oldHeight = this.Height.Pixels;
			this.Height.Pixels = perLineHeight.Pixels * lineCount;
			if (this.Height.Pixels != oldHeight && !(Parent is null)) {
				Parent.Height.Pixels += this.Height.Pixels - oldHeight;
				Parent.Recalculate();
			}
		}
	}
	internal class UIFocusInputTextField : UIElement {
		public delegate void EventHandler(object sender, EventArgs e);

		internal bool Focused;

		internal string CurrentString = "";

		private readonly string _hintText;

		private int _textBlinkerCount;

		private bool _textBlinkerState;

		private float hScroll = 0f;
		
		private int leftScrollTime = 0;

		private int rightScrollTime = 0;
		public bool UnfocusOnTab { get; internal set; }

		public event EventHandler OnTextChange;

		public event EventHandler OnUnfocus;

		public event EventHandler OnTab;

		public UIFocusInputTextField(string hintText) {
			_hintText = hintText;
		}

		public void SetText(string text) {
			if (text == null) {
				text = "";
			}
			if (CurrentString != text) {
				CurrentString = text;
				this.OnTextChange?.Invoke(this, new EventArgs());
			}
		}

		public override void Click(UIMouseEvent evt) {
			Main.clrInput();
			Focused = true;
		}

		public override void Update(GameTime gameTime) {
			Vector2 point = new Vector2(Main.mouseX, Main.mouseY);
			if (!ContainsPoint(point) && Main.mouseLeft) {
				Focused = false;
				this.OnUnfocus?.Invoke(this, new EventArgs());
			}
			base.Update(gameTime);
		}

		private static bool JustPressed(Keys key) {
			if (Main.inputText.IsKeyDown(key)) {
				return !Main.oldInputText.IsKeyDown(key);
			}
			return false;
		}

		protected override void DrawSelf(SpriteBatch spriteBatch) {
			if (Focused) {
				PlayerInput.WritingText = true;
				Main.instance.HandleIME();
				string inputText = CurrentString;
				if (JustPressed(Keys.V) && (Main.inputText.IsKeyDown(Keys.LeftControl) || Main.inputText.IsKeyDown(Keys.RightControl))) {
					inputText += Platform.Current.GetMultilineClipboard();
					Main.oldInputText = Main.inputText;
					Main.inputText = Keyboard.GetState();
				} else {
					inputText = Main.GetInputText(CurrentString);
				}
				if (JustPressed(Keys.Enter)) {
					inputText += '\n';
					Main.drawingPlayerChat = false;
					Main.chatRelease = false;
				}
				if (!inputText.Equals(CurrentString)) {
					CurrentString = inputText;
					this.OnTextChange?.Invoke(this, new EventArgs());
				} else {
					CurrentString = inputText;
				}
				if (JustPressed(Keys.Tab)) {
					if (UnfocusOnTab) {
						Focused = false;
						this.OnUnfocus?.Invoke(this, new EventArgs());
					}
					this.OnTab?.Invoke(this, new EventArgs());
				}
				if (leftScrollTime > 0) leftScrollTime--;
				if (rightScrollTime > 0) rightScrollTime--;
				if (Main.inputText.IsKeyDown(Keys.Left)) {
					bool shouldScroll = leftScrollTime <= 0;
					int setScroll = 4;
					if (!Main.oldInputText.IsKeyDown(Keys.Left)) {
						shouldScroll = true;
						setScroll = 15;
					}
					if (shouldScroll) {
						hScroll -= 10;
						if (hScroll < 0) {
							hScroll = 0;
						}
						leftScrollTime = setScroll;
					}
				}
				if (Main.inputText.IsKeyDown(Keys.Right)) {
					bool shouldScroll = rightScrollTime <= 0;
					int setScroll = 4;
					if (!Main.oldInputText.IsKeyDown(Keys.Right)) {
						shouldScroll = true;
						setScroll = 15;
					}
					if (shouldScroll) {
						hScroll += 10;
						rightScrollTime = setScroll;
					}
				}
				if (++_textBlinkerCount >= 20) {
					_textBlinkerState = !_textBlinkerState;
					_textBlinkerCount = 0;
				}
			}
			string text = CurrentString;
			if (_textBlinkerState && Focused) {
				text += "|";
			}
			CalculatedStyle dimensions = GetDimensions();
			if (CurrentString.Length == 0 && !Focused) {
				Utils.DrawBorderString(spriteBatch, _hintText, new Vector2(dimensions.X, dimensions.Y), Color.Gray);
			} else {
				float maxDisplayWidth = this.GetOuterDimensions().Width;
				if (Main.fontMouseText.MeasureString(text).X < maxDisplayWidth) {
					hScroll = 0f;
				} else {
					StringBuilder displayedText = new StringBuilder();
					float currentWidth = -hScroll;
					for (int i = 0; i < text.Length; i++) {
						if (text[i] == '\n') {
							displayedText.Append('\n');
							currentWidth = -hScroll;
							continue;
						}
						currentWidth += Main.fontMouseText.MeasureString(text[i].ToString()).X;
						if (currentWidth >= 0 && currentWidth <= maxDisplayWidth + 40) {
							displayedText.Append(text[i]);
						}
					}
					text = displayedText.ToString();
				}
				Utils.DrawBorderString(spriteBatch, text, new Vector2(dimensions.X, dimensions.Y), Color.White);
			}
		}
	}
	internal class UIModConfigHoverImage : UIImage {
		internal string HoverText;

		public UIModConfigHoverImage(Texture2D texture, string hoverText) : base(texture) {
			HoverText = hoverText;
		}

		protected override void DrawSelf(SpriteBatch spriteBatch) {
			base.DrawSelf(spriteBatch);
			if (IsMouseHovering) {
				PatchworkUI.UIModConfig.GetField("tooltip", BindingFlags.Public | BindingFlags.Static).SetValue(null, HoverText);
			}
		}
	}
	public class VariableSetElement : ConfigElement<VariableSet> {
		public override void OnBind() {
			base.OnBind();
			drawLabel = false;
			Width = new StyleDimension(0f, 1f);
			Height = new StyleDimension(30f, 0f);
		}
		public override void Draw(SpriteBatch spriteBatch) {
			base.Draw(spriteBatch);
			Height = new StyleDimension(30f * Value.Count, 0f);
			Vector2 position = this.GetInnerDimensions().ToRectangle().TopLeft();
			float y = 4;
			foreach (var item in Value) {
				spriteBatch.DrawString(Main.fontMouseText, item.Key+": "+item.Value, position + new Vector2(8, y), Color.White);
				y += 30f;
			}
		}
	}
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Property | AttributeTargets.Field)]
	public class CustomModConfigItemListAttribute : System.Attribute {
		public Type t;
		public CustomModConfigItemListAttribute(Type t) {
			this.t = t;
		}
	}
}
