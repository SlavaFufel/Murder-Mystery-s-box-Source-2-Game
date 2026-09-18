using System.Collections.Generic;

/// <summary>
/// Ран-тайм состояние одной активной торговой сессии у локального игрока.
/// На каждом клиенте живёт максимум одна сессия (CurrentSession в TradeManager).
/// Не сериализуется и не синхронизируется — всё состояние пересылается через
/// RPC на PlayerStats (TradeUpdateOffer / TradeChat / TradeCancel).
/// </summary>
public sealed class TradeSession
{
	/// <summary>SteamId партнёра по обмену.</summary>
	public string PartnerSteamId;
	/// <summary>Display-name партнёра (для подписей в UI).</summary>
	public string PartnerName;

	// Моя сторона.
	public List<(string slot, string id)> MyItems = new();
	public int MyCrystals = 0;
	public bool MyReady = false;

	// Сторона партнёра — приходит к нам через RPC TradeUpdateOffer.
	public List<(string slot, string id)> TheirItems = new();
	public int TheirCrystals = 0;
	public bool TheirReady = false;

	/// <summary>Лог локального чата сделки.</summary>
	public List<TradeChatMessage> Chat = new();

	/// <summary>Финал — обмен совершён, окно показывает «успех».</summary>
	public bool Completed = false;
	/// <summary>Сделка отменена (одной из сторон, либо из-за дисконнекта).</summary>
	public bool Cancelled = false;
	/// <summary>Локализационный ключ причины отмены / завершения.</summary>
	public string EndReasonKey = "";

	public const int MaxItemsPerSide = 6;
}

public struct TradeChatMessage
{
	public string From;
	public string Text;
	public float Time;
}
