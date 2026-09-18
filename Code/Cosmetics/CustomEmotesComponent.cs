using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Компонент-конструктор кастомных эмоций прямо из инспектора. Положи его на
/// scene-static объект (например тот же `scripts`, где сидят CosmeticOverrides
/// / PromoCodeManager) и заполни список Emotes.
///
/// Для каждой записи: задай Id, путь к PNG, отображаемые имена EN/RU, редкость,
/// и id кейса в который её надо подкинуть (плюс Weight). На OnAwake компонент:
///   1) Регистрирует эмоцию в CosmeticCatalog.All (если такого Id ещё нет).
///   2) Прописывает имена в L (Localization) на лету.
///   3) Добавляет drop-запись в указанный кейс (CosmeticVendorNPC.Cases).
///
/// Применяется до открытия UI/кейсов. Если в инспекторе изменил список —
/// перезапусти сцену, чтобы повторно применилось без задвоений.
/// </summary>
public sealed class CustomEmotesComponent : Component
{
	[System.Serializable]
	public class Entry
	{
		/// <summary>Уникальный id эмоции, например "emote_skull". Должен быть unique по всему каталогу.</summary>
		[Property] public string Id { get; set; } = "";

		/// <summary>Путь к PNG относительно Assets, например "images/emotes/emote_skull.png".</summary>
		[Property] public string IconPath { get; set; } = "";

		/// <summary>Отображаемое имя на английском.</summary>
		[Property] public string DisplayNameEN { get; set; } = "";

		/// <summary>Отображаемое имя на русском.</summary>
		[Property] public string DisplayNameRU { get; set; } = "";

		/// <summary>Редкость — влияет на цвет рамки в инвентаре и шансы в кейсе.</summary>
		[Property] public CosmeticCatalog.Rarity Rarity { get; set; } = CosmeticCatalog.Rarity.Rare;

		/// <summary>
		/// Id кейса в который добавить drop (например "main_case"). Пусто — не
		/// подкидывать (эмоция всё равно попадёт в каталог).
		/// </summary>
		[Property] public string AddToCaseId { get; set; } = "";

		/// <summary>
		/// Вес drop'а в кейсе. Чем больше относительно других — тем чаще
		/// выпадает. Игнорится если AddToCaseId пуст.
		/// </summary>
		[Property] public float DropWeight { get; set; } = 1f;
	}

	[Property] public List<Entry> Emotes { get; set; } = new();

	protected override void OnAwake()
	{
		Apply();
	}

	public void Apply()
	{
		if ( Emotes == null || Emotes.Count == 0 ) return;

		foreach ( var entry in Emotes )
		{
			if ( entry == null || string.IsNullOrEmpty( entry.Id ) ) continue;

			var nameKey = $"cosmetic.{entry.Id}";

			// 1) Регистрируем (или обновляем) запись в каталоге.
			var existing = CosmeticCatalog.Get( entry.Id );
			if ( existing == null )
			{
				CosmeticCatalog.All.Add( new CosmeticCatalog.Item
				{
					Id          = entry.Id,
					Slot        = CosmeticCatalog.Slot.Emote,
					NameKey     = nameKey,
					Price       = 0,
					Method      = CosmeticCatalog.ApplyMethod.Emote,
					ResourceRef = entry.IconPath,   // путь к PNG → используется и над головой, и в колесе
					IconPath    = entry.IconPath,   // тот же путь для иконки инвентаря
					Rarity      = entry.Rarity,
				} );
			}
			else
			{
				// Если запись уже есть (например после hot-reload) — обновим поля.
				existing.ResourceRef = entry.IconPath;
				existing.IconPath    = entry.IconPath;
				existing.Rarity      = entry.Rarity;
				existing.NameKey     = nameKey;
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

	private void AddDropToCase( string caseId, string emoteId, float weight )
	{
		// Ищем кейс у любого CosmeticVendorNPC в сцене.
		var vendor = Scene?.GetAllComponents<CosmeticVendorNPC>().FirstOrDefault();
		if ( vendor == null )
		{
			Log.Warning( $"[CustomEmotes] нет CosmeticVendorNPC в сцене — drop '{emoteId}' в кейс '{caseId}' не добавлен." );
			return;
		}

		var caseDef = vendor.Cases?.FirstOrDefault( c => c != null && c.Id == caseId );
		if ( caseDef == null )
		{
			Log.Warning( $"[CustomEmotes] кейс '{caseId}' не найден у CosmeticVendorNPC — drop '{emoteId}' пропущен." );
			return;
		}

		caseDef.Drops ??= new List<CaseDropEntry>();

		// Защита от двойной регистрации (например при hot-reload).
		if ( caseDef.Drops.Any( d => d != null && d.CosmeticId == emoteId ) ) return;

		caseDef.Drops.Add( new CaseDropEntry
		{
			CosmeticId = emoteId,
			Weight     = weight,
		} );
	}
}
