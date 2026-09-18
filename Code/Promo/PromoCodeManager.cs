using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Менеджер промокодов. Положи компонент на любой scene-static GameObject
/// (например на тот же где сидит GameManager) и заполни список Codes в
/// инспекторе. Каждая запись — один промокод и его награда.
///
/// Игрок вводит код в PromoCodeUI, локальный (owner) RPC применяет награду
/// у себя и пишет код в список «использованных» (StatsPersistence). Хост
/// тут не нужен — экономика на стороне owner'а, как и для покупок косметики.
/// </summary>
public sealed class PromoCodeManager : Component
{
	public enum RewardKind
	{
		Crystals,   // дать N кристаллов
		Case,       // дать N кейсов с указанным id (CaseId)
		Cosmetic,   // дать предмет косметики (Slot + ItemId)
	}

	[System.Serializable]
	public class PromoCode
	{
		[Property] public string Code { get; set; } = "";

		[Property] public RewardKind Kind { get; set; } = RewardKind.Crystals;

		// Кристаллы — сколько добавить (для Kind=Crystals).
		[Property, Group( "Reward · Crystals" )] public int CrystalsAmount { get; set; } = 100;

		// Кейс — какой и сколько (для Kind=Case). CaseId — это id из CaseDefinition
		// (StarterCase.Id или id любого кейса у CosmeticVendorNPC).
		[Property, Group( "Reward · Case" )] public string CaseId { get; set; } = "";
		[Property, Group( "Reward · Case" )] public int CaseCount { get; set; } = 1;

		// Косметика — слот ("knife"/"hat"/"back"/"nickname") и id из CosmeticCatalog.
		[Property, Group( "Reward · Cosmetic" )] public string CosmeticSlot { get; set; } = "knife";
		[Property, Group( "Reward · Cosmetic" )] public string CosmeticId { get; set; } = "";

		// Один раз на игрока (по умолчанию). Сними — и код можно вводить
		// сколько угодно раз (например для бесконечного бонуса в день, но
		// тогда экономику соблюдай сам).
		[Property] public bool OneTimePerPlayer { get; set; } = true;

		// Включён ли код. Удобно временно отключать без удаления.
		[Property] public bool Enabled { get; set; } = true;
	}

	[Property] public List<PromoCode> Codes { get; set; } = new();

	public static PromoCodeManager Instance { get; private set; }

	protected override void OnAwake()
	{
		Instance = this;
	}

	/// <summary>
	/// Ищет промокод по введённой строке (сравнение нечувствительно к регистру
	/// и пробелам по краям). Возвращает null если не найден или Enabled=false.
	/// </summary>
	public PromoCode FindCode( string raw )
	{
		if ( string.IsNullOrWhiteSpace( raw ) ) return null;
		var key = raw.Trim();
		return Codes?.FirstOrDefault( c =>
			c != null && c.Enabled
			&& !string.IsNullOrEmpty( c.Code )
			&& string.Equals( c.Code.Trim(), key, System.StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>Каноничный «ключ» кода для сохранения в _redeemedPromos.</summary>
	public static string NormalizeKey( string code )
	{
		return (code ?? "").Trim().ToLowerInvariant();
	}
}
