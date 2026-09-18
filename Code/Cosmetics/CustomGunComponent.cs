using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Компонент для добавления вариантов детективского пистолета через инспектор.
/// Положи на тот же scene-static объект `scripts`.
///
/// Для каждой записи: Id (например "gun_golden"), имена EN/RU, редкость, кейс
/// + Weight. Модель самой пушки — отдельный GameObject под HeldItems в префабе
/// игрока. Его id нужно повторить в ItemVisibilityController → Gun Skins →
/// Cosmetic Skin с тем же Id и привязанной Model.
///
/// На OnAwake компонент:
///   1) Регистрирует предмет в CosmeticCatalog.All с Slot=Gun (если такого Id ещё нет).
///   2) Прописывает имена в L (Localization).
///   3) Добавляет drop-запись в указанный кейс у CosmeticVendorNPC.
///
/// Визуальное переключение моделей делает ItemVisibilityController.ApplyGunSkins
/// — оно автоматически подхватывает GetEquipped("gun") и активирует нужный
/// GameObject из GunSkins.
/// </summary>
public sealed class CustomGunComponent : Component
{
	[System.Serializable]
	public class Entry
	{
		/// <summary>Уникальный id, например "gun_golden", "gun_revolver". Должен совпадать с Id в ItemVisibilityController → GunSkins.</summary>
		[Property] public string Id { get; set; } = "";

		/// <summary>Иконка инвентаря (PNG в Assets/images/cosmetics/...). Пусто — fallback на 🔫.</summary>
		[Property] public string IconPath { get; set; } = "";

		/// <summary>Отображаемое имя на английском.</summary>
		[Property] public string DisplayNameEN { get; set; } = "";

		/// <summary>Отображаемое имя на русском.</summary>
		[Property] public string DisplayNameRU { get; set; } = "";

		/// <summary>Редкость — цвет рамки в инвентаре и группа в превью кейса.</summary>
		[Property] public CosmeticCatalog.Rarity Rarity { get; set; } = CosmeticCatalog.Rarity.Rare;

		/// <summary>
		/// Метод применения:
		/// ToggleObject — отдельная модель (нужно создать GameObject в префабе и привязать к GunSkins[Id].Model).
		/// Tint — красим дефолтную модель в HEX-цвет из TintHex.
		/// </summary>
		[Property] public CosmeticCatalog.ApplyMethod Method { get; set; } = CosmeticCatalog.ApplyMethod.ToggleObject;

		/// <summary>Только если Method=Tint. HEX-цвет вида "#ffaa00" — этим красится дефолтная модель.</summary>
		[Property] public string TintHex { get; set; } = "";

		/// <summary>Цена в магазине. 0 — только через дроп.</summary>
		[Property] public int Price { get; set; } = 0;

		/// <summary>Id кейса в который добавить drop. Пусто — без дропа.</summary>
		[Property] public string AddToCaseId { get; set; } = "";

		/// <summary>Вес drop'а в кейсе (Common ~30, Rare ~6, SuperRare ~3, Legendary ~1).</summary>
		[Property] public float DropWeight { get; set; } = 1f;
	}

	[Property] public List<Entry> Items { get; set; } = new();

	protected override void OnAwake()
	{
		Apply();
	}

	public void Apply()
	{
		if ( Items == null || Items.Count == 0 ) return;

		foreach ( var entry in Items )
		{
			if ( entry == null || string.IsNullOrEmpty( entry.Id ) ) continue;

			var nameKey = $"cosmetic.{entry.Id}";

			// Для Tint-метода ResourceRef = HEX-цвет, для ToggleObject он не используется.
			string resourceRef = entry.Method == CosmeticCatalog.ApplyMethod.Tint
				? (entry.TintHex ?? "")
				: "";

			// 1) Регистрируем (или обновляем) запись в каталоге.
			var existing = CosmeticCatalog.Get( entry.Id );
			if ( existing == null )
			{
				CosmeticCatalog.All.Add( new CosmeticCatalog.Item
				{
					Id          = entry.Id,
					Slot        = CosmeticCatalog.Slot.Gun,
					NameKey     = nameKey,
					Price       = entry.Price,
					Method      = entry.Method,
					ResourceRef = resourceRef,
					IconPath    = entry.IconPath ?? "",
					Rarity      = entry.Rarity,
				} );
			}
			else
			{
				existing.Slot        = CosmeticCatalog.Slot.Gun;
				existing.NameKey     = nameKey;
				existing.Price       = entry.Price;
				existing.Method      = entry.Method;
				existing.ResourceRef = resourceRef;
				existing.IconPath    = entry.IconPath ?? "";
				existing.Rarity      = entry.Rarity;
			}

			// 2) Локализация.
			L.Register( nameKey, entry.DisplayNameEN, entry.DisplayNameRU );

			// 3) Добавляем drop в кейс если задано.
			if ( !string.IsNullOrEmpty( entry.AddToCaseId ) )
			{
				AddDropToCase( entry.AddToCaseId, entry.Id, entry.DropWeight );
			}
		}
	}

	private void AddDropToCase( string caseId, string itemId, float weight )
	{
		var vendor = Scene?.GetAllComponents<CosmeticVendorNPC>().FirstOrDefault();
		if ( vendor == null )
		{
			Log.Warning( $"[CustomGun] нет CosmeticVendorNPC в сцене — drop '{itemId}' в кейс '{caseId}' не добавлен." );
			return;
		}

		var caseDef = vendor.Cases?.FirstOrDefault( c => c != null && c.Id == caseId );
		if ( caseDef == null )
		{
			Log.Warning( $"[CustomGun] кейс '{caseId}' не найден — drop '{itemId}' пропущен." );
			return;
		}

		caseDef.Drops ??= new List<CaseDropEntry>();

		// Защита от двойной регистрации (hot-reload).
		if ( caseDef.Drops.Any( d => d != null && d.CosmeticId == itemId ) ) return;

		caseDef.Drops.Add( new CaseDropEntry
		{
			CosmeticId = itemId,
			Weight     = weight,
		} );
	}
}
