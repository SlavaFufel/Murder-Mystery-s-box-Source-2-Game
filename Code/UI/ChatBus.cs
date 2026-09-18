using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Простая шина сообщений чата. Сообщения хранятся локально на каждом клиенте,
/// рассылка между клиентами идёт через [Rpc.Broadcast] на компоненте
/// ChatRelay (он должен висеть на сцене в одном экземпляре).
///
/// Чат изолирован по руме игрока (CurrentRoomId) и по флагу IsSpectator —
/// это даёт раздельные каналы:
///   • Хаб (Guid.Empty)            — все висящие в хабе видят друг друга
///   • Комната X / живые           — обычный игровой чат раунда
///   • Комната X / спектаторы      — мёртвые/наблюдатели общаются между собой
/// Сообщения других каналов в Append не попадают.
/// </summary>
public static class ChatBus
{
	public struct Message
	{
		public string From;
		public string Text;
		public float Time;
	}

	public static List<Message> Messages { get; } = new();
	public static int Version { get; private set; } = 0;

	private const int MaxMessages = 30;

	/// <summary>Локально показать сообщение, без рассылки в сеть (для system-фидбека).</summary>
	public static void SendLocal( string from, string text )
	{
		Append( from, text );
	}

	/// <summary>
	/// Послать сообщение всем игрокам в сети. На приёме каждый клиент
	/// фильтрует по своей руме / спектатор-статусу — попадёт только тем
	/// в том же канале что и отправитель.
	/// </summary>
	public static void Send( string from, string text )
	{
		var local = Game.ActiveScene?.GetAllComponents<PlayerStats>()
			.FirstOrDefault( p => !p.IsProxy );

		var roomId  = local?.CurrentRoomId ?? System.Guid.Empty;
		var isSpec  = local?.IsSpectator   ?? false;

		var relay = ChatRelay.FindOrNull();
		if ( relay != null )
			relay.RelayChat( from, text, roomId, isSpec );
		else
			Append( from, text );
	}

	internal static void Append( string from, string text )
	{
		Messages.Add( new Message { From = from, Text = text, Time = Sandbox.Time.Now } );
		while ( Messages.Count > MaxMessages ) Messages.RemoveAt( 0 );
		Version++;
	}

	/// <summary>Очистить всю историю — вызывается при старте новой сессии.</summary>
	public static void Clear()
	{
		Messages.Clear();
		Version++;
	}
}

/// <summary>
/// Сетевой-релэй для чата. Один экземпляр на сцене (пустой GameObject + этот компонент).
/// </summary>
public sealed class ChatRelay : Component
{
	public static ChatRelay FindOrNull()
	{
		return Game.ActiveScene?.GetAllComponents<ChatRelay>().FirstOrDefault();
	}

	/// <summary>
	/// senderRoomId / senderIsSpec — канал отправителя. Получатель добавит
	/// сообщение в локальную историю только если его собственная рума /
	/// спек-статус совпадает с отправителем.
	/// </summary>
	[Rpc.Broadcast]
	public void RelayChat( string from, string text, System.Guid senderRoomId, bool senderIsSpec )
	{
		var local = Game.ActiveScene?.GetAllComponents<PlayerStats>()
			.FirstOrDefault( p => !p.IsProxy );
		if ( local == null )
		{
			// Нет локального игрока — не показываем (например ещё не заспаунились).
			return;
		}

		// Канал = (roomId, isSpec). Не совпадает — отбрасываем.
		if ( local.CurrentRoomId != senderRoomId ) return;
		if ( local.IsSpectator   != senderIsSpec ) return;

		ChatBus.Append( from, text );
	}
}
