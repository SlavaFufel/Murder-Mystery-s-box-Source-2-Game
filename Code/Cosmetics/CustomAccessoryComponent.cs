using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Компонент-конструктор bone-attached аксессуаров (крылья, плащи, рюкзаки,
/// нимбы, хвосты) через инспектор. Аналог CustomClothingComponent, но обходит
/// ClothingContainer.SlotsOver/Under-логику — модели парентятся напрямую к
/// костям citizen-скелета, поэтому не конфликтуют с одеждой игрока.
///
/// Положи на тот же scene-static объект `scripts` (рядом с CustomClothingComponent /
/// CosmeticOverrides). Для каждой записи:
///   • Drag-drop`.vmdl` модель в Model.
///   • Укажи кость (по умолчанию "spine_2" — середина позвоночника).
///   • Подгони Local Position / Rotation / Scale если модель смещена/неправильно
///     ориентирована относительно кости.
///   • Заполни имена EN/RU, редкость, кейс + Weight.
///
/// На OnAwake:
///   1. Регистрирует в CosmeticCatalog.All с Method = Accessory.
///   2. Прописывает локализацию.
///   3. Добавляет drop в указанный кейс.
///
/// Сам рендер аксессуара делает AccessoryController на префабе игрока —
/// он каждый раз когда меняется экипировка пересоздаёт детей нужных bone GO.
///
/// Базовые имена костей Citizen:
///   spine_0, spine_1, spine_2 — позвоночник (низ → верх).
///   neck_0, head — шея/голова.
///   clavicle_L/R, arm_upper_L/R, arm_lower_L/R — руки.
///   leg_upper_L/R, leg_lower_L/R — ноги.
/// Полный список: https://github.com/Facepunch/sbox-docs/blob/master/docs/assets/ready-to-use-assets/citizen-characters.md
/// </summary>
public sealed class CustomAccessoryComponent : Component
{
	[System.Serializable]
	public class Entry
	{
		/// <summary>Уникальный id (например "wings_devil", "cape_red"). Должен быть unique по каталогу.</summary>
		[Property] public string Id { get; set; } = "";

		/// <summary>Слот: Hat (на голове) или Back (на спине). Влияет только на то, в какой вкладке инвентаря отображается.</summary>
		[Property] public CosmeticCatalog.Slot Slot { get; set; } = CosmeticCatalog.Slot.Back;

		/// <summary>
		/// 3D-модель аксессуара. Drag-drop .vmdl из Asset Browser.
		/// Если не задано — попытаемся вытащить модель из ClothingAsset (см. ниже).
		/// </summary>
		[Property] public Model Model { get; set; }

		/// <summary>
		/// Альтернатива Model — drag-drop .clothing-ассет, мы вытащим из него
		/// поле .Model автоматически. Удобно если у тебя уже есть `.clothing`
		/// для крыльев/короны и не хочется искать оригинальный `.vmdl` в ассетах.
		/// Используется только если Model выше не задан.
		/// </summary>
		[Property] public Clothing ClothingAsset { get; set; }

		/// <summary>Иконка инвентаря (PNG в Assets/images/cosmetics/...). Пусто — fallback на slot-эмодзи.</summary>
		[Property] public string IconPath { get; set; } = "";

		/// <summary>Имя на английском.</summary>
		[Property] public string DisplayNameEN { get; set; } = "";

		/// <summary>Имя на русском.</summary>
		[Property] public string DisplayNameRU { get; set; } = "";

		/// <summary>Редкость — цвет рамки в инвентаре и группа в превью кейса.</summary>
		[Property] public CosmeticCatalog.Rarity Rarity { get; set; } = CosmeticCatalog.Rarity.Rare;

		/// <summary>Цена в магазине (0 — только дроп).</summary>
		[Property] public int Price { get; set; } = 0;

		// ── Bone attachment ─────────────────────────────────────────────────

		/// <summary>
		/// Имя кости citizen-скелета. По умолчанию "spine_2" (середина-верх позвоночника
		/// — подходит для крыльев/плащей). Для шляп — "head". Для рюкзака — "spine_1".
		/// </summary>
		[Property] public string AttachBone { get; set; } = "spine_2";

		/// <summary>Смещение модели относительно кости (в локальных координатах).</summary>
		[Property] public Vector3 LocalPosition { get; set; } = Vector3.Zero;

		/// <summary>Поворот модели (Pitch=X / Yaw=Y / Roll=Z в градусах).</summary>
		[Property] public Angles LocalRotation { get; set; } = Angles.Zero;

		/// <summary>Масштаб модели (1 = оригинал). Для уменьшения/увеличения относительно кости.</summary>
		[Property] public float LocalScale { get; set; } = 1f;

		// ── Drop ────────────────────────────────────────────────────────────

		/// <summary>Id кейса в который подкинуть drop ("main_case" и т.п.). Пусто — без дропа.</summary>
		[Property] public string AddToCaseId { get; set; } = "";

		/// <summary>Вес drop'а (Common ~30, Rare ~6, SuperRare ~3, Legendary ~1).</summary>
		[Property] public float DropWeight { get; set; } = 1f;
	}

	[Property] public List<Entry> Items { get; set; } = new();

	protected override void OnAwake()
	{
		Apply();
	}

	/// <summary>
	/// Каждый кадр копируем transform-поля и имя кости из инспектора в
	/// CosmeticCatalog.Item. AccessoryController прочитает их и применит к
	/// заспавненной модели в том же кадре → live-edit в инспекторе без
	/// перезапуска Play.
	/// </summary>
	protected override void OnUpdate()
	{
		if ( Items == null || Items.Count == 0 ) return;

		foreach ( var entry in Items )
		{
			if ( entry == null || string.IsNullOrEmpty( entry.Id ) ) continue;
			var item = CosmeticCatalog.Get( entry.Id );
			if ( item == null || item.Method != CosmeticCatalog.ApplyMethod.Accessory ) continue;

			item.AccessoryLocalPosition = entry.LocalPosition;
			item.AccessoryLocalRotation = entry.LocalRotation;
			item.AccessoryLocalScale    = entry.LocalScale > 0 ? entry.LocalScale : 1f;
			item.AccessoryBone          = string.IsNullOrEmpty( entry.AttachBone ) ? "spine_2" : entry.AttachBone;
		}
	}

	public void Apply()
	{
		if ( Items == null || Items.Count == 0 ) return;

		foreach ( var entry in Items )
		{
			if ( entry == null || string.IsNullOrEmpty( entry.Id ) ) continue;

			if ( entry.Slot != CosmeticCatalog.Slot.Hat && entry.Slot != CosmeticCatalog.Slot.Back )
			{
				Log.Warning( $"[CustomAccessory] '{entry.Id}': слот {entry.Slot} не подходит для аксессуара — нужен Hat или Back." );
				continue;
			}

			// Источник модели — либо прямой Model, либо вытаскиваем из Clothing.
			// У Clothing.Model тип `string` (путь к .vmdl) — берём напрямую.
			var modelPath = entry.Model?.ResourcePath;
			if ( string.IsNullOrEmpty( modelPath ) && entry.ClothingAsset != null )
				modelPath = entry.ClothingAsset.Model;

			if ( string.IsNullOrEmpty( modelPath ) )
			{
				Log.Warning( $"[CustomAccessory] '{entry.Id}': не задана ни Model (.vmdl), ни Clothing Asset (.clothing)." );
				continue;
			}

			var nameKey = $"cosmetic.{entry.Id}";

			// 1) Регистрируем (или обновляем) запись в каталоге.
			var existing = CosmeticCatalog.Get( entry.Id );
			if ( existing == null )
			{
				CosmeticCatalog.All.Add( new CosmeticCatalog.Item
				{
					Id          = entry.Id,
					Slot        = entry.Slot,
					NameKey     = nameKey,
					Price       = entry.Price,
					Method      = CosmeticCatalog.ApplyMethod.Accessory,
					ResourceRef = "", // не используется для Accessory
					IconPath    = entry.IconPath ?? "",
					Rarity      = entry.Rarity,
					AccessoryModelPath    = modelPath,
					AccessoryBone         = entry.AttachBone ?? "spine_2",
					AccessoryLocalPosition = entry.LocalPosition,
					AccessoryLocalRotation = entry.LocalRotation,
					AccessoryLocalScale    = entry.LocalScale,
				} );
			}
			else
			{
				// Hot-reload — обновим поля без задвоения записи.
				existing.Slot        = entry.Slot;
				existing.NameKey     = nameKey;
				existing.Price       = entry.Price;
				existing.Method      = CosmeticCatalog.ApplyMethod.Accessory;
				existing.IconPath    = entry.IconPath ?? "";
				existing.Rarity      = entry.Rarity;
				existing.AccessoryModelPath    = modelPath;
				existing.AccessoryBone         = entry.AttachBone ?? "spine_2";
				existing.AccessoryLocalPosition = entry.LocalPosition;
				existing.AccessoryLocalRotation = entry.LocalRotation;
				existing.AccessoryLocalScale    = entry.LocalScale;
			}

			// 2) Локализация.
			L.Register( nameKey, entry.DisplayNameEN, entry.DisplayNameRU );

			// 3) Drop в кейс.
			if ( !string.IsNullOrEmpty( entry.AddToCaseId ) )
				AddDropToCase( entry.AddToCaseId, entry.Id, entry.DropWeight );
		}
	}

	private void AddDropToCase( string caseId, string itemId, float weight )
	{
		var vendor = Scene?.GetAllComponents<CosmeticVendorNPC>().FirstOrDefault();
		if ( vendor == null )
		{
			Log.Warning( $"[CustomAccessory] нет CosmeticVendorNPC в сцене — drop '{itemId}' пропущен." );
			return;
		}

		var caseDef = vendor.Cases?.FirstOrDefault( c => c != null && c.Id == caseId );
		if ( caseDef == null )
		{
			Log.Warning( $"[CustomAccessory] кейс '{caseId}' не найден — drop '{itemId}' пропущен." );
			return;
		}

		caseDef.Drops ??= new List<CaseDropEntry>();
		if ( caseDef.Drops.Any( d => d != null && d.CosmeticId == itemId ) ) return;

		caseDef.Drops.Add( new CaseDropEntry
		{
			CosmeticId = itemId,
			Weight     = weight,
		} );
	}
}
