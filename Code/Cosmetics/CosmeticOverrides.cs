using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Дизайнерские переопределения параметров косметики (редкость и т.п.)
/// без правки кода. Положи компонент на любой scene-static GameObject
/// (например на тот же где сидит PromoCodeManager — `scripts`), и в
/// инспекторе заполни Items: для каждого Id из CosmeticCatalog можно
/// задать свою редкость.
///
/// Применяется в OnAwake — то есть до первого открытия инвентаря/кейса.
/// Если запись из списка удалена/изменена, редкость возвращается к
/// дефолтному значению из CosmeticCatalog.BaseItems (см. ResetRaritiesToBaseline).
/// </summary>
public sealed class CosmeticOverrides : Component
{
	[System.Serializable]
	public class Entry
	{
		/// <summary>Id предмета из CosmeticCatalog (knife_katana, knife_golden, ...).</summary>
		[Property] public string Id { get; set; } = "";

		/// <summary>Новая редкость для этого предмета.</summary>
		[Property] public CosmeticCatalog.Rarity Rarity { get; set; } = CosmeticCatalog.Rarity.Common;
	}

	[Property] public List<Entry> Items { get; set; } = new();

	protected override void OnAwake()
	{
		Apply();
	}

	/// <summary>
	/// Применить overrides поверх каталога. Сначала сбрасываем все Item.Rarity
	/// на дефолт (CosmeticCatalog.BaseItems), потом накладываем что есть в Items.
	/// </summary>
	public void Apply()
	{
		CosmeticCatalog.ResetRaritiesToBaseline();
		if ( Items == null ) return;

		foreach ( var entry in Items )
		{
			if ( entry == null || string.IsNullOrEmpty( entry.Id ) ) continue;
			var item = CosmeticCatalog.Get( entry.Id );
			if ( item == null )
			{
				Log.Warning( $"[CosmeticOverrides] не нашёл предмет '{entry.Id}' в каталоге — пропускаю." );
				continue;
			}
			item.Rarity = entry.Rarity;
		}
	}
}
