using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Менеджер ежедневок. Положи компонент на любой scene-static GameObject
/// (рядом с GameManager / PromoCodeManager). Заполни список Rewards 7-ю
/// записями — по одной на каждый день недели.
///
/// Дни 1-6 обычно — кристаллы (по нарастающей). День 7 — финальный
/// «джекпот»: можно поставить Kind=Case или Kind=Cosmetic чтобы выдать
/// кейс или предмет вместо кристаллов.
///
/// Логика «стрика»:
/// • Игрок забирает награду → streak += 1, LastClaimDate = сегодня (UTC).
/// • Если зашёл на следующий день — следующий день стрика.
/// • Если пропустил день — стрик сбрасывается обратно на 1.
/// • После 7-го дня — снова с 1.
/// </summary>
public sealed class DailyRewardsManager : Component
{
	public enum RewardKind
	{
		Crystals,
		Case,
		Cosmetic,
	}

	[System.Serializable]
	public class DailyReward
	{
		[Property] public RewardKind Kind { get; set; } = RewardKind.Crystals;

		[Property, Group( "Crystals" )] public int CrystalsAmount { get; set; } = 100;

		[Property, Group( "Case" )] public string CaseId { get; set; } = "";
		[Property, Group( "Case" )] public int CaseCount { get; set; } = 1;

		[Property, Group( "Cosmetic" )] public string CosmeticSlot { get; set; } = "knife";
		[Property, Group( "Cosmetic" )] public string CosmeticId { get; set; } = "";
	}

	[Property] public List<DailyReward> Rewards { get; set; } = new()
	{
		new() { Kind = RewardKind.Crystals, CrystalsAmount = 100 },
		new() { Kind = RewardKind.Crystals, CrystalsAmount = 200 },
		new() { Kind = RewardKind.Crystals, CrystalsAmount = 300 },
		new() { Kind = RewardKind.Crystals, CrystalsAmount = 400 },
		new() { Kind = RewardKind.Crystals, CrystalsAmount = 500 },
		new() { Kind = RewardKind.Crystals, CrystalsAmount = 750 },
		// День 7 — джекпот. По умолчанию 1500 💎, но можно поменять Kind на Case/Cosmetic.
		new() { Kind = RewardKind.Crystals, CrystalsAmount = 1500 },
	};

	public static DailyRewardsManager Instance { get; private set; }

	protected override void OnAwake()
	{
		Instance = this;
	}

	/// <summary>Награда за день 1..7 (индекс 0..6). Возвращает null если индекс вне диапазона.</summary>
	public DailyReward GetReward( int dayIndex )
	{
		if ( Rewards == null ) return null;
		if ( dayIndex < 0 || dayIndex >= Rewards.Count ) return null;
		return Rewards[dayIndex];
	}

	public int RewardCount => Rewards?.Count ?? 0;
}
