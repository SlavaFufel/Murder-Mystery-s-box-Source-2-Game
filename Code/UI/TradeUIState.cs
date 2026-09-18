/// <summary>
/// Локальные флаги UI трейда. Сама сессия живёт в TradeManager — здесь только
/// «открыто ли окно списка игроков» (его нельзя вывести из состояния сессии).
/// Окно сделки открывается автоматически когда TradeManager.CurrentSession != null,
/// а инвайт-попап — когда есть IncomingInvite.
/// </summary>
public static class TradeUIState
{
	public static bool IsListOpen { get; set; } = false;

	public static bool AnyTradeUiOpen =>
		IsListOpen
		|| TradeManager.CurrentSession != null
		|| !string.IsNullOrEmpty( TradeManager.IncomingInviteFromSteamId )
		|| !string.IsNullOrEmpty( TradeManager.OutgoingInviteToSteamId );
}
