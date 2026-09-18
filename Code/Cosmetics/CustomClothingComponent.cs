using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Компонент для добавления clothing-косметики (шляпы/спина) из инспектора.
/// Положи на scene-static объект (тот же `scripts`, где сидят CosmeticOverrides
/// / CustomEmotesComponent). Заполни список — для каждой записи:
///   • Drag-drop'ом перетащи `.clothing`-ассет в поле Clothing Asset.
///   • Заполни Id, имена EN/RU, редкость, IconPath (PNG для инвентаря).
///   • Если нужно подкинуть в кейс — укажи Id кейса и Weight.
///
/// На OnAwake компонент:
///   1) Регистрирует предмет в CosmeticCatalog.All (если такого Id ещё нет).
///   2) Прописывает имена в L (Localization).
///   3) Добавляет drop-запись в указанный кейс у CosmeticVendorNPC.
///
/// Применение к модели игрока уже реализовано в PlayerStats.ApplyClothing() —
/// он автоматически грузит ассет по `item.ResourceRef` через `ResourceLibrary.Get<Clothing>`.
/// </summary>
public sealed class CustomClothingComponent : Component
{
	[System.Serializable]
	public class Entry
	{
		/// <summary>Уникальный id, например "hat_fedora", "back_wings". Должен быть unique по каталогу.</summary>
		[Property] public string Id { get; set; } = "";

		/// <summary>Слот — только Hat или Back имеют смысл для clothing.</summary>
		[Property] public CosmeticCatalog.Slot Slot { get; set; } = CosmeticCatalog.Slot.Hat;

		/// <summary>Сам clothing-ассет — перетащи .clothing-файл из Asset Browser.</summary>
		[Property] public Clothing ClothingAsset { get; set; }

		/// <summary>Иконка инвентаря (PNG в Assets/images/cosmetics/...). Пусто — fallback на slot-эмодзи.</summary>
		[Property] public string IconPath { get; set; } = "";

		/// <summary>Отображаемое имя на английском.</summary>
		[Property] public string DisplayNameEN { get; set; } = "";

		/// <summary>Отображаемое имя на русском.</summary>
		[Property] public string DisplayNameRU { get; set; } = "";

		/// <summary>Редкость — цвет рамки в инвентаре и группа в превью кейса.</summary>
		[Property] public CosmeticCatalog.Rarity Rarity { get; set; } = CosmeticCatalog.Rarity.Rare;

		/// <summary>Цена в магазине (если хочешь продавать). 0 — только через дроп.</summary>
		[Property] public int Price { get; set; } = 0;

		/// <summary>Id кейса в который добавить drop ("main_case", "start_case" и т.п.). Пусто — без дропа.</summary>
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

			// Слот должен быть «одежным». Currency/Nickname/Emote/Knife — отбрасываем.
			if ( entry.Slot != CosmeticCatalog.Slot.Hat && entry.Slot != CosmeticCatalog.Slot.Back )
			{
				Log.Warning( $"[CustomClothing] '{entry.Id}': слот {entry.Slot} не подходит для clothing — нужен Hat или Back." );
				continue;
			}

			// Из drag-n-drop'нутого ассета достаём строковый путь, который потом
			// PlayerStats.ApplyClothing() будет грузить через ResourceLibrary.Get<Clothing>.
			var resPath = entry.ClothingAsset?.ResourcePath;
			if ( string.IsNullOrEmpty( resPath ) )
			{
				Log.Warning( $"[CustomClothing] '{entry.Id}': не задан Clothing Asset (drag-drop файл .clothing)." );
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
					Method      = CosmeticCatalog.ApplyMethod.Clothing,
					ResourceRef = resPath,
					IconPath    = entry.IconPath ?? "",
					Rarity      = entry.Rarity,
				} );
			}
			else
			{
				// Hot-reload — обновим поля без задвоения записи.
				existing.Slot        = entry.Slot;
				existing.NameKey     = nameKey;
				existing.Price       = entry.Price;
				existing.Method      = CosmeticCatalog.ApplyMethod.Clothing;
				existing.ResourceRef = resPath;
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
			Log.Warning( $"[CustomClothing] нет CosmeticVendorNPC в сцене — drop '{itemId}' в кейс '{caseId}' не добавлен." );
			return;
		}

		var caseDef = vendor.Cases?.FirstOrDefault( c => c != null && c.Id == caseId );
		if ( caseDef == null )
		{
			Log.Warning( $"[CustomClothing] кейс '{caseId}' не найден — drop '{itemId}' пропущен." );
			return;
		}

		caseDef.Drops ??= new List<CaseDropEntry>();

		// Защита от двойной регистрации (например при hot-reload).
		if ( caseDef.Drops.Any( d => d != null && d.CosmeticId == itemId ) ) return;

		caseDef.Drops.Add( new CaseDropEntry
		{
			CosmeticId = itemId,
			Weight     = weight,
		} );
	}
}
