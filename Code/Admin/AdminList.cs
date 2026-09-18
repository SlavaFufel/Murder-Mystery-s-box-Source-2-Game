using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Whitelist админов по Steam ID. Тот, кто хост — НЕ автоматически админ;
/// права выдаются только тем, чей SteamId перечислен ниже.
///
/// Чтобы добавить админа: вставь его SteamId64 (число вида 76561198xxxxxxxxx)
/// в массив Admins. Можно посмотреть свой ID через консольную команду
/// `mm_my_steamid` (выводит твой SteamId в лог).
///
/// Список одинаковый на хосте и клиентах — он зашит в сборку, так что
/// и серверная проверка (RPC) и клиентская (UI/команды) дают одинаковый
/// результат.
/// </summary>
public static class AdminList
{
	// ⬇️ ДОБАВЛЯЙ STEAM ID ЗДЕСЬ.
	// Список пуст в публичном репозитории — заполни своим SteamId64, иначе
	// админ-панель будет недоступна никому.
	//
	// Пример:
	//   private static readonly HashSet<ulong> Admins = new()
	//   {
	//       76561198000000001,
	//       76561198000000002,
	//   };
	private static readonly HashSet<ulong> Admins = new()
	{
	};

	/// <summary>True если SteamId есть в whitelist админов.</summary>
	public static bool IsAdmin( ulong steamId )
	{
		if ( steamId == 0 ) return false;
		return Admins.Contains( steamId );
	}

	/// <summary>True если данное соединение принадлежит админу.</summary>
	public static bool IsAdmin( Connection conn )
	{
		if ( conn == null ) return false;
		try { return IsAdmin( conn.SteamId ); } catch { return false; }
	}

	/// <summary>True если локальный игрок — админ.</summary>
	public static bool IsLocalAdmin()
	{
		try
		{
			var me = Sandbox.Connection.Local;
			return me != null && IsAdmin( me.SteamId );
		}
		catch { return false; }
	}

	/// <summary>True если владелец данного PlayerStats — админ.</summary>
	public static bool IsOwnerAdmin( PlayerStats player )
	{
		if ( player == null ) return false;
		try { return IsAdmin( player.Network.Owner ); } catch { return false; }
	}
}
