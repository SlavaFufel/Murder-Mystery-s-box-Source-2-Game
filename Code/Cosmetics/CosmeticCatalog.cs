using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Статический реестр косметических предметов. Расширять — добавь запись в All.
/// Локализация — через NameKey (ключ в Localization.cs).
/// </summary>
public static class CosmeticCatalog
{
	public enum Slot { Knife, Back, Hat, Nickname, Currency, Emote, Gun }

	public enum Rarity { Common, Rare, SuperRare, Legendary }

	public enum ApplyMethod
	{
		ToggleObject, // несколько GameObject под ItemVisibilityController/AccessoryController, активируем нужный
		Tint,         // используем дефолтную модель и красим SkinnedModelRenderer.Tint
		Clothing,     // через ClothingContainer (s&box clothing item)
		Nickname,     // ник над головой — ResourceRef = CSS-класс из NameplatesUI
		Currency,     // не предмет — ResourceRef = число кристаллов которые выдаются при выпадении из кейса
		Emote,        // эмоция: ResourceRef = emoji-символ или путь к PNG, показывается над головой 5 сек
		Accessory     // bone-attached модель (крылья, плащ, рюкзак) — обходит ClothingContainer, парентится к кости
	}

	public class Item
	{
		public string Id;
		public Slot Slot;
		public string NameKey;
		public int Price;
		public ApplyMethod Method;
		public string ResourceRef; // hex для Tint, clothing path, CSS-класс для Nickname
		public Rarity Rarity = Rarity.Common;
		public string IconPath = "";

		// ── Method = Accessory ──────────────────────────────────────────────
		// Используются только когда Method == Accessory. Игнорируются для других.
		public string AccessoryModelPath = "";  // путь к .vmdl модели (extract из Model.ResourcePath)
		public string AccessoryBone = "spine_2"; // имя кости citizen-скелета к которой крепится
		public Vector3 AccessoryLocalPosition = Vector3.Zero;
		public Angles  AccessoryLocalRotation = Angles.Zero;
		public float   AccessoryLocalScale    = 1f;
	}

	public static readonly List<Item> All = new()
	{
		// ── Knife ─────────────────────────────────────────────────────────────
		// IconPath — путь к PNG-снимку модели для отображения в карточках инвентаря,
		// strip кейса и shop preview. Положи файлы в Assets/images/cosmetics/<id>.png.
		// Если PNG отсутствует — UI fallback на emoji 🔪 (см. CaseOpeningUI/HubInventoryUI).
		new Item { Id = "knife_default", Slot = Slot.Knife, NameKey = "cosmetic.knife_default", Price = 0,    Method = ApplyMethod.ToggleObject, Rarity = Rarity.Common,    IconPath = "images/cosmetics/knife_default.png" },

		// ── Gun (детективский пистолет) ─────────────────────────────────────
		// gun_default — стандартный пистолет, всегда есть у всех (см. IsOwned).
		// Доп. варианты добавляются через CustomGunComponent в инспекторе.
		// IconPath не задаём — fallback на 🔫. Если хочешь иконку — положи
		// PNG в Assets/images/cosmetics/gun_default.png и пропиши путь.
		new Item { Id = "gun_default",  Slot = Slot.Gun,   NameKey = "cosmetic.gun_default",  Price = 0,    Method = ApplyMethod.ToggleObject, Rarity = Rarity.Common },
		new Item { Id = "knife_katana",  Slot = Slot.Knife, NameKey = "cosmetic.knife_katana",  Price = 500,  Method = ApplyMethod.ToggleObject, Rarity = Rarity.Rare,      IconPath = "images/cosmetics/knife_katana.png" },
		// knife_golden — отдельный GameObject в ItemVisibilityController.KnifeSkins
		// (с золотой моделью и Tint, заданным в инспекторе у CosmeticSkin), поэтому
		// Method = ToggleObject. Не использовать Tint-метод — он включал бы дефолтную
		// модель и красил её ResourceRef'ом, игнорируя отдельный golden-GameObject.
		new Item { Id = "knife_golden",  Slot = Slot.Knife, NameKey = "cosmetic.knife_golden",  Price = 1500, Method = ApplyMethod.ToggleObject, Rarity = Rarity.Legendary, IconPath = "images/cosmetics/knife_golden.png" },
		new Item { Id = "knife_katana2", Slot = Slot.Knife, NameKey = "cosmetic.knife_katana2", Price = 500,  Method = ApplyMethod.ToggleObject, Rarity = Rarity.Rare,      IconPath = "images/cosmetics/knife_katana2.png" },

		// ── Nickname ──────────────────────────────────────────────────────────
		// "default" — дефолтный белый стиль (не показывать в шопе/инвентаре).
		new Item { Id = "nick_default",       Slot = Slot.Nickname, NameKey = "cosmetic.nick_default",       Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-default",       Rarity = Rarity.Common },

		// Common
		new Item { Id = "nick_chalk",         Slot = Slot.Nickname, NameKey = "cosmetic.nick_chalk",         Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-chalk",         Rarity = Rarity.Common },
		new Item { Id = "nick_typewriter",    Slot = Slot.Nickname, NameKey = "cosmetic.nick_typewriter",    Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-typewriter",    Rarity = Rarity.Common },
		new Item { Id = "nick_cinema",        Slot = Slot.Nickname, NameKey = "cosmetic.nick_cinema",        Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-cinema",        Rarity = Rarity.Common },

		// Rare
		new Item { Id = "nick_neon_blue",     Slot = Slot.Nickname, NameKey = "cosmetic.nick_neon_blue",     Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-neon-blue",     Rarity = Rarity.Rare },
		new Item { Id = "nick_neon_pink",     Slot = Slot.Nickname, NameKey = "cosmetic.nick_neon_pink",     Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-neon-pink",     Rarity = Rarity.Rare },
		new Item { Id = "nick_police",        Slot = Slot.Nickname, NameKey = "cosmetic.nick_police",        Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-police",        Rarity = Rarity.Rare },

		// Super Rare
		new Item { Id = "nick_glitch",        Slot = Slot.Nickname, NameKey = "cosmetic.nick_glitch",        Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-glitch",        Rarity = Rarity.SuperRare },
		new Item { Id = "nick_blood",         Slot = Slot.Nickname, NameKey = "cosmetic.nick_blood",         Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-blood",         Rarity = Rarity.SuperRare },
		new Item { Id = "nick_ghost",         Slot = Slot.Nickname, NameKey = "cosmetic.nick_ghost",         Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-ghost",         Rarity = Rarity.SuperRare },

		// Super Rare — новый кейс (case.main): 6 крутых ников.
		new Item { Id = "nick_cyber",         Slot = Slot.Nickname, NameKey = "cosmetic.nick_cyber",         Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-cyber",         Rarity = Rarity.SuperRare },
		new Item { Id = "nick_emerald",       Slot = Slot.Nickname, NameKey = "cosmetic.nick_emerald",       Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-emerald",       Rarity = Rarity.SuperRare },
		new Item { Id = "nick_inferno",       Slot = Slot.Nickname, NameKey = "cosmetic.nick_inferno",       Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-inferno",       Rarity = Rarity.SuperRare },
		new Item { Id = "nick_frost",         Slot = Slot.Nickname, NameKey = "cosmetic.nick_frost",         Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-frost",         Rarity = Rarity.SuperRare },
		new Item { Id = "nick_void",          Slot = Slot.Nickname, NameKey = "cosmetic.nick_void",          Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-void",          Rarity = Rarity.SuperRare },
		new Item { Id = "nick_acid",          Slot = Slot.Nickname, NameKey = "cosmetic.nick_acid",          Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-acid",          Rarity = Rarity.SuperRare },

		// Legendary
		new Item { Id = "nick_suspect",       Slot = Slot.Nickname, NameKey = "cosmetic.nick_suspect",       Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-suspect",       Rarity = Rarity.Legendary },
		new Item { Id = "nick_justice",       Slot = Slot.Nickname, NameKey = "cosmetic.nick_justice",       Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-justice",       Rarity = Rarity.Legendary },
		new Item { Id = "nick_ash",           Slot = Slot.Nickname, NameKey = "cosmetic.nick_ash",           Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-ash",           Rarity = Rarity.Legendary },

		// Legendary — топовый ник из нового кейса, с rainbow-shimmer + glow + scale pulse.
		new Item { Id = "nick_godlike",       Slot = Slot.Nickname, NameKey = "cosmetic.nick_godlike",       Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-godlike",       Rarity = Rarity.Legendary },

		// ── Starter exclusives — выпадают ТОЛЬКО из start_case ────────────────
		new Item { Id = "nick_rookie",        Slot = Slot.Nickname, NameKey = "cosmetic.nick_rookie",        Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-rookie",        Rarity = Rarity.Common },
		new Item { Id = "nick_origin",        Slot = Slot.Nickname, NameKey = "cosmetic.nick_origin",        Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-origin",        Rarity = Rarity.Rare },
		new Item { Id = "nick_dawn",          Slot = Slot.Nickname, NameKey = "cosmetic.nick_dawn",          Price = 0, Method = ApplyMethod.Nickname, ResourceRef = "nick-dawn",          Rarity = Rarity.Legendary },

		// ── Currency drops — при выпадении конвертируются в кристаллы ────────
		new Item { Id = "crystals_50",        Slot = Slot.Currency, NameKey = "cosmetic.crystals_50",        Price = 0, Method = ApplyMethod.Currency, ResourceRef = "50",  Rarity = Rarity.Common },
		new Item { Id = "crystals_100",       Slot = Slot.Currency, NameKey = "cosmetic.crystals_100",       Price = 0, Method = ApplyMethod.Currency, ResourceRef = "100", Rarity = Rarity.Common },
		new Item { Id = "crystals_150",       Slot = Slot.Currency, NameKey = "cosmetic.crystals_150",       Price = 0, Method = ApplyMethod.Currency, ResourceRef = "150", Rarity = Rarity.Rare },
		new Item { Id = "crystals_300",       Slot = Slot.Currency, NameKey = "cosmetic.crystals_300",       Price = 0, Method = ApplyMethod.Currency, ResourceRef = "300", Rarity = Rarity.SuperRare },
		new Item { Id = "crystals_500",       Slot = Slot.Currency, NameKey = "cosmetic.crystals_500",       Price = 0, Method = ApplyMethod.Currency, ResourceRef = "500", Rarity = Rarity.Legendary },

		// ── Emotes (default) — встроенные эмоции, у всех всегда «owned» (см. IsOwned).
		// ResourceRef = emoji-символ для built-in, для drop-PNG будет путь к файлу.
		new Item { Id = "emote_smile",        Slot = Slot.Emote, NameKey = "cosmetic.emote_smile",        Price = 0, Method = ApplyMethod.Emote, ResourceRef = "😀", Rarity = Rarity.Common },
		new Item { Id = "emote_laugh",        Slot = Slot.Emote, NameKey = "cosmetic.emote_laugh",        Price = 0, Method = ApplyMethod.Emote, ResourceRef = "😂", Rarity = Rarity.Common },
		new Item { Id = "emote_sad",          Slot = Slot.Emote, NameKey = "cosmetic.emote_sad",          Price = 0, Method = ApplyMethod.Emote, ResourceRef = "😢", Rarity = Rarity.Common },
		new Item { Id = "emote_angry",        Slot = Slot.Emote, NameKey = "cosmetic.emote_angry",        Price = 0, Method = ApplyMethod.Emote, ResourceRef = "😡", Rarity = Rarity.Common },
		new Item { Id = "emote_heart",        Slot = Slot.Emote, NameKey = "cosmetic.emote_heart",        Price = 0, Method = ApplyMethod.Emote, ResourceRef = "❤️", Rarity = Rarity.Common },
		new Item { Id = "emote_shocked",      Slot = Slot.Emote, NameKey = "cosmetic.emote_shocked",      Price = 0, Method = ApplyMethod.Emote, ResourceRef = "😲", Rarity = Rarity.Common },

		// ── Emotes (drops) ── добавляются через компонент CustomEmotesComponent
		// в сцене (Inspector → список Emotes). Там же задаются картинка, имя
		// (EN/RU), редкость, и в какой кейс с каким Weight подкинуть.
	};

	public static string SlotEmoji( Slot s ) => s switch
	{
		Slot.Knife    => "🔪",
		Slot.Back     => "🎒",
		Slot.Hat      => "🎩",
		Slot.Nickname => "🏷️",
		Slot.Currency => "💎",
		Slot.Emote    => "😀",
		Slot.Gun      => "🔫",
		_ => "❔"
	};

	public static Item Get( string id ) => All.FirstOrDefault( x => x.Id == id );
	public static IEnumerable<Item> InSlot( Slot s ) => All.Where( x => x.Slot == s );

	// ── Baseline для переопределений (см. CosmeticOverrides) ────────────────
	// Один раз снимаем снапшот дефолтных редкостей из BaseItems. Если кто-то
	// удалит запись из CosmeticOverrides.Items — мы возвращаемся к этому
	// снапшоту, а не к «последнему override», которое осталось бы навсегда.
	private static Dictionary<string, Rarity> _baselineRarities;

	private static void EnsureRarityBaseline()
	{
		if ( _baselineRarities != null ) return;
		_baselineRarities = new Dictionary<string, Rarity>( All.Count );
		foreach ( var item in All )
			_baselineRarities[item.Id] = item.Rarity;
	}

	/// <summary>
	/// Сбросить редкость всех предметов к дефолту из BaseItems. Используется
	/// CosmeticOverrides перед применением своих overrides.
	/// </summary>
	public static void ResetRaritiesToBaseline()
	{
		EnsureRarityBaseline();
		foreach ( var item in All )
			if ( _baselineRarities.TryGetValue( item.Id, out var r ) )
				item.Rarity = r;
	}

	public static string SlotKey( Slot s ) => s switch
	{
		Slot.Knife    => "knife",
		Slot.Back     => "back",
		Slot.Hat      => "hat",
		Slot.Nickname => "nickname",
		Slot.Currency => "currency",
		Slot.Emote    => "emote",
		Slot.Gun      => "gun",
		_ => ""
	};

	public static Slot? ParseSlot( string key ) => key switch
	{
		"knife"    => Slot.Knife,
		"back"     => Slot.Back,
		"hat"      => Slot.Hat,
		"nickname" => Slot.Nickname,
		"currency" => Slot.Currency,
		"emote"    => Slot.Emote,
		"gun"      => Slot.Gun,
		_ => null
	};

	public static string DefaultIdForSlot( Slot s ) => s switch
	{
		Slot.Knife    => "knife_default",
		Slot.Back     => "back_none",
		Slot.Hat      => "hat_none",
		Slot.Nickname => "nick_default",
		Slot.Gun      => "gun_default",
		_ => null
	};

	/// <summary>
	/// 6 базовых эмоций которые лежат в дефолтном wheel'е каждого нового игрока.
	/// Все Common, у всех всегда «owned» (см. PlayerStats.IsOwned). Из кейсов
	/// будут падать дополнительные emote-предметы с PNG-картинками.
	/// </summary>
	public static readonly string[] DefaultEmoteWheel = new string[]
	{
		"emote_smile",
		"emote_laugh",
		"emote_sad",
		"emote_angry",
		"emote_heart",
		"emote_shocked",
	};

	/// <summary>Цвет рамки/подсветки для редкости в UI магазина и кейс-опенинга.</summary>
	public static string RarityHexColor( Rarity r ) => r switch
	{
		Rarity.Common    => "#bbbbbb",
		Rarity.Rare      => "#4a90e2",
		Rarity.SuperRare => "#a020f0",
		Rarity.Legendary => "#ffd700",
		_ => "#bbbbbb"
	};

	public static string RarityKey( Rarity r ) => r switch
	{
		Rarity.Common    => "rarity.common",
		Rarity.Rare      => "rarity.rare",
		Rarity.SuperRare => "rarity.super_rare",
		Rarity.Legendary => "rarity.legendary",
		_ => "rarity.common"
	};
}
