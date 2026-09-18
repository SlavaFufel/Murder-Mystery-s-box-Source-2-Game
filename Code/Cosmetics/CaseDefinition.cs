using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Описание одного кейса. Дизайнер заполняет в инспекторе у CosmeticVendorNPC.
/// Дроп пока используется только декоративно (показ списка возможных предметов
/// в карточке кейса) — реальная механика открытия будет добавлена позже.
/// </summary>
[System.Serializable]
public class CaseDefinition
{
	/// <summary>Уникальный идентификатор кейса. Например "starter_case", "premium_case".</summary>
	[Property] public string Id { get; set; } = "";

	/// <summary>Ключ локализации для названия кейса (см. Localization.cs).</summary>
	[Property] public string NameKey { get; set; } = "";

	/// <summary>Цена в кристаллах.</summary>
	[Property] public int Price { get; set; } = 100;

	/// <summary>
	/// Путь к картинке кейса (закрытого) — отображается в магазине и инвентаре.
	/// </summary>
	[Property] public string IconPath { get; set; } = "";

	/// <summary>
	/// Путь к картинке открытого кейса (внутри пусто, лучи света и т.п.).
	/// Показывается во время прокрутки в CaseOpeningUI. Если пусто — берётся IconPath.
	/// </summary>
	[Property] public string OpenedIconPath { get; set; } = "";

	/// <summary>
	/// Список возможных дропов — id косметики из CosmeticCatalog с весами.
	/// Можно оставить пустым для v1; реальная механика рола будет позже.
	/// </summary>
	[Property] public List<CaseDropEntry> Drops { get; set; } = new();
}

[System.Serializable]
public class CaseDropEntry
{
	/// <summary>
	/// Id косметического предмета из CosmeticCatalog (knife_katana, hat_crown и т.п.).
	/// Оставь пустым и заполни CrystalsAmount, если хочешь currency-дроп
	/// произвольного номинала (не из фиксированных crystals_50/100/etc).
	/// </summary>
	[Property] public string CosmeticId { get; set; } = "";

	/// <summary>
	/// Если > 0 — выпадает столько кристаллов (CosmeticId игнорируется).
	/// Удобно когда хочешь, например, 250 крист — а не один из фиксированных
	/// номиналов из каталога.
	/// </summary>
	[Property] public int CrystalsAmount { get; set; } = 0;

	/// <summary>Вес — чем больше, тем чаще выпадает (относительно других в этом кейсе).</summary>
	[Property] public float Weight { get; set; } = 1f;
}
