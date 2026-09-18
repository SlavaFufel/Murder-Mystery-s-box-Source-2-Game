using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Локальный менеджер торговых сессий. Singleton-static: каждый клиент держит
/// своё состояние (CurrentSession + IncomingInvite), а связь с партнёром
/// проходит через RPC на PlayerStats. Сетевая авторизация — каждая сторона
/// мутирует только свой инвентарь, как и для промокодов / покупок.
///
/// Поток данных:
///   1. Игрок A → SendInvite( B )  → PlayerStats.TradeInvite (broadcast)
///   2. B принимает → PlayerStats.TradeInviteResponse → A создаёт сессию.
///   3. Каждое изменение оффера → TradeUpdateOffer → партнёр обновляет
///      TheirItems/TheirCrystals/TheirReady и СБРАСЫВАЕТ свой Ready,
///      чтобы исключить «подсунутые» в последний момент изменения.
///   4. Когда оба Ready — каждая сторона у себя:
///        a) удаляет свои отданные предметы/кристаллы (TradeCommitOutgoing),
///        b) добавляет полученные от партнёра (TradeApplyIncoming),
///        c) показывает success-экран и закрывает окно через пару секунд.
/// </summary>
public static class TradeManager
{
	public static TradeSession CurrentSession { get; private set; }

	/// <summary>Входящее приглашение, на которое игрок ещё не ответил.</summary>
	public static string IncomingInviteFromSteamId { get; private set; }
	public static string IncomingInviteFromName { get; private set; }

	/// <summary>Исходящее приглашение, ждущее ответа.</summary>
	public static string OutgoingInviteToSteamId { get; private set; }
	public static string OutgoingInviteToName { get; private set; }

	/// <summary>Версия — UI слушает её через BuildHash, чтобы реактивно перерисовываться.</summary>
	public static int Version { get; private set; } = 0;

	/// <summary>Последний фидбек ("trade.bad.busy" / "trade.bad.no_target" и т.п.).</summary>
	public static string LastFeedbackKey { get; private set; } = "";
	public static int FeedbackCounter { get; private set; } = 0;

	private static void Bump() => Version++;

	private static void SetFeedback( string key )
	{
		LastFeedbackKey = key ?? "";
		FeedbackCounter++;
	}

	private static void SyncIsTrading( bool trading )
	{
		var me = LocalPlayer();
		if ( me != null && !me.IsProxy )
			me.IsTrading = trading;
	}

	private static PlayerStats LocalPlayer()
	{
		return Game.ActiveScene?.GetAllComponents<PlayerStats>().FirstOrDefault( p => !p.IsProxy );
	}

	private static PlayerStats FindPlayerBySteamId( string steamId )
	{
		if ( string.IsNullOrEmpty( steamId ) ) return null;
		return Game.ActiveScene?.GetAllComponents<PlayerStats>()
			.FirstOrDefault( p => p.PublicSteamId == steamId );
	}

	/// <summary>
	/// Список потенциальных партнёров — все игроки в хабе, кроме локального.
	/// </summary>
	public static IEnumerable<PlayerStats> ListHubPlayers()
	{
		var local = LocalPlayer();
		if ( local == null ) yield break;
		foreach ( var p in Game.ActiveScene.GetAllComponents<PlayerStats>() )
		{
			if ( p == local ) continue;
			if ( p.CurrentRoomId != System.Guid.Empty ) continue;       // только хаб
			if ( string.IsNullOrEmpty( p.PublicSteamId ) ) continue;    // ещё не загрузился
			yield return p;
		}
	}

	// ── Outgoing actions (UI → сеть) ────────────────────────────────────────

	public static void SendInvite( PlayerStats target )
	{
		var me = LocalPlayer();
		if ( me == null || target == null ) { SetFeedback( "trade.bad.no_target" ); Bump(); return; }
		if ( me.CurrentRoomId != System.Guid.Empty ) { SetFeedback( "trade.bad.not_in_hub" ); Bump(); return; }
		if ( target.CurrentRoomId != System.Guid.Empty ) { SetFeedback( "trade.bad.target_not_in_hub" ); Bump(); return; }
		if ( target.IsTrading ) { SetFeedback( "trade.bad.target_busy" ); Bump(); return; }
		if ( CurrentSession != null || OutgoingInviteToSteamId != null )
		{
			SetFeedback( "trade.bad.busy" ); Bump(); return;
		}

		OutgoingInviteToSteamId = target.PublicSteamId;
		OutgoingInviteToName    = target.DisplayName;
		me.TradeInvite( me.PublicSteamId, me.DisplayName, target.PublicSteamId );
		SetFeedback( "trade.info.invite_sent" );
		Bump();
	}

	public static void RespondToIncomingInvite( bool accept )
	{
		var me = LocalPlayer();
		if ( me == null || string.IsNullOrEmpty( IncomingInviteFromSteamId ) ) return;
		string from = IncomingInviteFromSteamId;
		string fromName = IncomingInviteFromName;

		if ( accept )
		{
			// Если уже занят чем-то — отклоняем.
			if ( CurrentSession != null )
			{
				me.TradeInviteResponse( me.PublicSteamId, from, false, me.DisplayName );
				IncomingInviteFromSteamId = null;
				IncomingInviteFromName = null;
				SetFeedback( "trade.bad.busy" );
				Bump();
				return;
			}

			CurrentSession = new TradeSession
			{
				PartnerSteamId = from,
				PartnerName    = fromName,
			};
			SyncIsTrading( true );
		}

		me.TradeInviteResponse( me.PublicSteamId, from, accept, me.DisplayName );
		IncomingInviteFromSteamId = null;
		IncomingInviteFromName = null;
		Bump();
	}

	public static void CancelOutgoingInvite()
	{
		var me = LocalPlayer();
		if ( me == null || string.IsNullOrEmpty( OutgoingInviteToSteamId ) ) return;
		me.TradeCancel( me.PublicSteamId, OutgoingInviteToSteamId, "trade.end.cancelled_by_inviter" );
		OutgoingInviteToSteamId = null;
		OutgoingInviteToName = null;
		Bump();
	}

	public static void CancelSession( string reasonKey = "trade.end.cancelled" )
	{
		var s = CurrentSession;
		if ( s == null ) return;
		var me = LocalPlayer();
		if ( me != null )
			me.TradeCancel( me.PublicSteamId, s.PartnerSteamId, reasonKey );
		s.Cancelled = true;
		s.EndReasonKey = reasonKey;
		CurrentSession = null;
		SyncIsTrading( false );
		Bump();
	}

	public static void AddItemToOffer( string slot, string id )
	{
		var s = CurrentSession;
		if ( s == null ) return;
		if ( s.MyItems.Count >= TradeSession.MaxItemsPerSide ) return;
		if ( string.IsNullOrEmpty( slot ) || string.IsNullOrEmpty( id ) ) return;
		// Дефолтные «пустые» состояния не отдаются.
		if ( id == "knife_default" || id == "gun_default" || id == "back_none" || id == "hat_none" || id == "nick_default" ) return;
		// Базовые эмоции — у всех всегда есть, торговать ими нет смысла.
		if ( IsDefaultEmote( id ) ) return;

		var me = LocalPlayer();
		if ( me == null ) return;
		// Нельзя положить больше штук, чем владеешь.
		int alreadyOffered = s.MyItems.Count( p => p.slot == slot && p.id == id );
		if ( me.OwnedCountOf( slot, id ) <= alreadyOffered ) return;

		s.MyItems.Add( (slot, id) );
		s.MyReady = false;
		s.TheirReady = false;
		PushOffer();
	}

	public static void RemoveItemFromOffer( int index )
	{
		var s = CurrentSession;
		if ( s == null ) return;
		if ( index < 0 || index >= s.MyItems.Count ) return;
		s.MyItems.RemoveAt( index );
		s.MyReady = false;
		s.TheirReady = false;
		PushOffer();
	}

	public static void SetMyCrystals( int amount )
	{
		var s = CurrentSession;
		if ( s == null ) return;
		var me = LocalPlayer();
		if ( me == null ) return;
		amount = System.Math.Clamp( amount, 0, me.Crystals );
		if ( s.MyCrystals == amount ) return;
		s.MyCrystals = amount;
		s.MyReady = false;
		s.TheirReady = false;
		PushOffer();
	}

	public static void ToggleMyReady()
	{
		var s = CurrentSession;
		if ( s == null ) return;
		s.MyReady = !s.MyReady;
		PushOffer();

		// Если оба готовы — запускаем commit одновременно с обеих сторон.
		if ( s.MyReady && s.TheirReady )
			TryCommit();
	}

	private static float _lastChatTime = -999f;
	public const float TradeChatCooldown = 2f;
	private const int TradeChatMaxLength = 120;

	/// <summary>Сколько секунд осталось до возможности отправить следующее сообщение в трейд-чат (0 если можно слать).</summary>
	public static float ChatCooldownRemaining
	{
		get
		{
			// Time.Now сбрасывается при смене сцены (хаб → раунд), поэтому
			// _lastChatTime может оказаться больше текущего Time.Now — тогда
			// разница отрицательная и кулдаун «вечный». Сбрасываем такой случай.
			float dt = Time.Now - _lastChatTime;
			if ( dt < 0f ) { _lastChatTime = -999f; return 0f; }
			return System.Math.Max( 0f, TradeChatCooldown - dt );
		}
	}

	public static void SendChatLine( string text )
	{
		var s = CurrentSession;
		var me = LocalPlayer();
		if ( s == null || me == null ) return;
		if ( string.IsNullOrWhiteSpace( text ) ) return;
		text = text.Trim();
		if ( text.Length > TradeChatMaxLength ) text = text.Substring( 0, TradeChatMaxLength );
		float dt = Time.Now - _lastChatTime;
		if ( dt >= 0f && dt < TradeChatCooldown ) return;
		_lastChatTime = Time.Now;

		// Локально показываем сразу.
		s.Chat.Add( new TradeChatMessage { From = me.DisplayName, Text = text, Time = Time.Now } );
		me.TradeChat( me.PublicSteamId, s.PartnerSteamId, me.DisplayName, text );
		Bump();
	}

	private static void PushOffer()
	{
		var s = CurrentSession;
		var me = LocalPlayer();
		if ( s == null || me == null ) return;
		var serialized = SerializeItems( s.MyItems );
		me.TradeUpdateOffer( me.PublicSteamId, s.PartnerSteamId, serialized, s.MyCrystals, s.MyReady );
		Bump();
	}

	private static void TryCommit()
	{
		var s = CurrentSession;
		var me = LocalPlayer();
		if ( s == null || me == null ) return;
		if ( s.Completed || s.Cancelled ) return;
		if ( !s.MyReady || !s.TheirReady ) return;

		// Снимаем своё.
		bool ok = me.TradeCommitOutgoing( s.MyItems, s.MyCrystals );
		if ( !ok )
		{
			// Не хватает того, что мы сами выставили (могли потерять между
			// моментом «Готов» и commit'ом). Отменяем сделку.
			CancelSession( "trade.end.missing_items" );
			SetFeedback( "trade.bad.missing_items" );
			Bump();
			return;
		}

		// Применяем входящее.
		me.TradeApplyIncoming( s.TheirItems, s.TheirCrystals );

		s.Completed = true;
		s.EndReasonKey = "trade.end.success";
		Bump();
	}

	// ── Incoming events (приходят из PlayerStats RPC) ───────────────────────

	public static void OnIncomingInvite( string fromSteamId, string fromName )
	{
		// Уже в сделке или с уже висящим приглашением — авто-отказ.
		if ( CurrentSession != null || !string.IsNullOrEmpty( IncomingInviteFromSteamId ) )
		{
			var me = LocalPlayer();
			if ( me != null )
				me.TradeInviteResponse( me.PublicSteamId, fromSteamId, false, me.DisplayName );
			return;
		}
		IncomingInviteFromSteamId = fromSteamId;
		IncomingInviteFromName    = fromName;
		Bump();
	}

	public static void OnInviteResponse( string fromSteamId, string fromName, bool accepted )
	{
		if ( OutgoingInviteToSteamId != fromSteamId ) return;
		OutgoingInviteToSteamId = null;
		OutgoingInviteToName = null;

		if ( !accepted )
		{
			SetFeedback( "trade.info.declined" );
			Bump();
			return;
		}

		if ( CurrentSession != null )
		{
			Bump();
			return;
		}
		CurrentSession = new TradeSession
		{
			PartnerSteamId = fromSteamId,
			PartnerName    = fromName,
		};
		SyncIsTrading( true );
		Bump();
	}

	public static void OnPartnerOfferUpdated( string fromSteamId, string offerSerialized, int crystals, bool ready )
	{
		var s = CurrentSession;
		if ( s == null || s.PartnerSteamId != fromSteamId ) return;

		var newItems = DeserializeItems( offerSerialized );
		var newCrystals = System.Math.Max( 0, crystals );
		bool offerChanged = !ItemListEqual( s.TheirItems, newItems ) || s.TheirCrystals != newCrystals;

		s.TheirItems = newItems;
		s.TheirCrystals = newCrystals;
		s.TheirReady = ready;
		// Сбрасываем мой Ready ТОЛЬКО если содержимое оффера изменилось —
		// чтобы партнёр не «отменял» мою готовность простым нажатием Ready.
		if ( offerChanged ) s.MyReady = false;
		Bump();

		if ( s.MyReady && s.TheirReady )
			TryCommit();
	}

	private static bool ItemListEqual( List<(string slot, string id)> a, List<(string slot, string id)> b )
	{
		if ( a == null && b == null ) return true;
		if ( a == null || b == null ) return false;
		if ( a.Count != b.Count ) return false;
		for ( int i = 0; i < a.Count; i++ )
		{
			if ( a[i].slot != b[i].slot ) return false;
			if ( a[i].id != b[i].id ) return false;
		}
		return true;
	}

	// 6 базовых эмоций (CosmeticCatalog.DefaultEmoteWheel) есть у всех всегда —
	// торговать ими нет смысла, и попытка передать их завершится ошибкой на
	// сервер-стороне (RemoveOwnedLocal). Блокируем заранее в UI.
	private static bool IsDefaultEmote( string id )
	{
		var defaults = CosmeticCatalog.DefaultEmoteWheel;
		for ( int i = 0; i < defaults.Length; i++ )
			if ( defaults[i] == id ) return true;
		return false;
	}

	/// <summary>Закрывает завершённую/отменённую сессию (UI вызывает после показа результата).</summary>
	public static void CloseFinishedSession()
	{
		var s = CurrentSession;
		if ( s == null ) return;
		if ( !s.Completed && !s.Cancelled ) return;
		CurrentSession = null;
		SyncIsTrading( false );
		Bump();
	}

	public static void OnPartnerCancelled( string fromSteamId, string reasonKey )
	{
		// Партнёр мог отменить либо приглашение, либо активную сессию.
		if ( OutgoingInviteToSteamId == fromSteamId )
		{
			OutgoingInviteToSteamId = null;
			OutgoingInviteToName = null;
			SetFeedback( !string.IsNullOrEmpty( reasonKey ) ? reasonKey : "trade.info.declined" );
			Bump();
			return;
		}
		if ( IncomingInviteFromSteamId == fromSteamId )
		{
			IncomingInviteFromSteamId = null;
			IncomingInviteFromName = null;
			Bump();
			return;
		}
		var s = CurrentSession;
		if ( s != null && s.PartnerSteamId == fromSteamId )
		{
			s.Cancelled = true;
			s.EndReasonKey = !string.IsNullOrEmpty( reasonKey ) ? reasonKey : "trade.end.cancelled_by_partner";
			CurrentSession = null;
			Bump();
		}
	}

	public static void OnPartnerChat( string fromSteamId, string fromName, string text )
	{
		var s = CurrentSession;
		if ( s == null || s.PartnerSteamId != fromSteamId ) return;
		if ( string.IsNullOrWhiteSpace( text ) ) return;
		s.Chat.Add( new TradeChatMessage { From = fromName, Text = text, Time = Time.Now } );
		Bump();
	}

	// ── Сериализация offer-предметов ────────────────────────────────────────

	public static string SerializeItems( List<(string slot, string id)> items )
	{
		if ( items == null || items.Count == 0 ) return "";
		var sb = new System.Text.StringBuilder();
		foreach ( var (slot, id) in items )
		{
			if ( string.IsNullOrEmpty( slot ) || string.IsNullOrEmpty( id ) ) continue;
			if ( sb.Length > 0 ) sb.Append( ';' );
			sb.Append( slot ).Append( ':' ).Append( id );
		}
		return sb.ToString();
	}

	public static List<(string slot, string id)> DeserializeItems( string s )
	{
		var result = new List<(string, string)>();
		if ( string.IsNullOrEmpty( s ) ) return result;
		foreach ( var pair in s.Split( ';', System.StringSplitOptions.RemoveEmptyEntries ) )
		{
			var parts = pair.Split( ':', 2 );
			if ( parts.Length != 2 ) continue;
			if ( string.IsNullOrEmpty( parts[0] ) || string.IsNullOrEmpty( parts[1] ) ) continue;
			result.Add( (parts[0], parts[1]) );
		}
		return result;
	}
}
