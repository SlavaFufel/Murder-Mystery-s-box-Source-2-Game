using System.Collections.Generic;

/// <summary>
/// Стартовый кейс — выдаётся каждому игроку один раз при первом заходе и
/// больше никаким способом не получается. Содержит кристаллы и три
/// эксклюзивных стартовых ника, которые НЕ выпадают из обычного nicknames-кейса.
///
/// Определён в коде, а не в инспекторе CosmeticVendorNPC — иначе игрок мог бы
/// купить его у торговца. UI ищет определение кейса сначала у вендоров, потом
/// у этого статика как fallback.
/// </summary>
public static class StarterCase
{
	public const string Id = "start_case";

	public static readonly CaseDefinition Definition = new CaseDefinition
	{
		Id             = Id,
		NameKey        = "case.start",
		Price          = 0,
		IconPath       = "images/case_start_unopened.png",
		OpenedIconPath = "images/case_start_opened.png",
		Drops = new List<CaseDropEntry>
		{
			// Кристаллы — обычные награды (чем больше, тем реже).
			new() { CosmeticId = "crystals_50",  Weight = 30f },
			new() { CosmeticId = "crystals_100", Weight = 25f },
			new() { CosmeticId = "crystals_150", Weight = 18f },
			new() { CosmeticId = "crystals_300", Weight = 10f },
			new() { CosmeticId = "crystals_500", Weight = 5f  },

			// Эксклюзивные стартовые ники — редкие награды.
			new() { CosmeticId = "nick_rookie",  Weight = 6f },
			new() { CosmeticId = "nick_origin",  Weight = 4f },
			new() { CosmeticId = "nick_dawn",    Weight = 2f },
		}
	};

	/// <summary>Возвращает определение, если caseId — стартовый. Иначе null.</summary>
	public static CaseDefinition Get( string caseId )
		=> caseId == Id ? Definition : null;
}
