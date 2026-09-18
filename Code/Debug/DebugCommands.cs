using Sandbox;
using System.Linq;

/// <summary>
/// Дев-команды для быстрой настройки визуала предметов в руке без полноценного
/// раунда. Вводятся в консоли s&box (` или ~ открывает консоль).
///
///   mm_give_knife   — выдать локальному игроку нож (роль Murderer)
///   mm_give_gun     — выдать пистолет (роль Detective)
///   mm_give_coins N — добавить N монет (по умолчанию 5)
///   mm_reset_role   — вернуть роль Innocent и обнулить монеты
/// </summary>
public static class DebugCommands
{
	private static PlayerStats LocalStats()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return null;

		return scene.GetAllComponents<PlayerStats>()
			.FirstOrDefault( p => !p.IsProxy );
	}

	private static bool RequireAdmin()
	{
		if ( AdminList.IsLocalAdmin() ) return true;
		Log.Warning( "[mm] Эту команду может использовать только админ (см. AdminList.cs)." );
		return false;
	}

	[ConCmd( "mm_give_knife" )]
	public static void GiveKnife()
	{
		if ( !RequireAdmin() ) return;
		var p = LocalStats();
		if ( p == null ) { Log.Warning( "[mm] Локальный PlayerStats не найден." ); return; }
		p.Role = PlayerStats.PlayerRole.Murderer;
		p.HasWeapon = true;
		Log.Info( "[mm] Выдан нож (роль Murderer). Нажми 1 чтобы взять в руку." );
	}

	[ConCmd( "mm_give_gun" )]
	public static void GiveGun()
	{
		if ( !RequireAdmin() ) return;
		var p = LocalStats();
		if ( p == null ) { Log.Warning( "[mm] Локальный PlayerStats не найден." ); return; }
		p.Role = PlayerStats.PlayerRole.Detective;
		p.HasWeapon = true;
		Log.Info( "[mm] Выдан пистолет (роль Detective). Нажми 1 чтобы взять в руку." );
	}

	[ConCmd( "mm_give_coins" )]
	public static void GiveCoins( int amount = 5 )
	{
		if ( !RequireAdmin() ) return;
		var p = LocalStats();
		if ( p == null ) { Log.Warning( "[mm] Локальный PlayerStats не найден." ); return; }
		p.Coins += amount;
		Log.Info( $"[mm] +{amount} монет → всего {p.Coins}. Нажми 2 чтобы взять монету в руку." );
	}

	[ConCmd( "mm_reset_role" )]
	public static void ResetRole()
	{
		if ( !RequireAdmin() ) return;
		var p = LocalStats();
		if ( p == null ) { Log.Warning( "[mm] Локальный PlayerStats не найден." ); return; }
		p.Role = PlayerStats.PlayerRole.Innocent;
		p.Coins = 0;
		p.HasWeapon = false;
		Log.Info( "[mm] Роль и монеты сброшены." );
	}

	// ── Адм-команды для теста ────────────────────────────────────────────────

	/// <summary>mm_admin — открыть/закрыть панель администратора (только админам).</summary>
	[ConCmd( "mm_admin" )]
	public static void ToggleAdminPanel()
	{
		if ( !RequireAdmin() ) return;
		AdminPanelState.Toggle();
	}

	/// <summary>
	/// mm_admin_give_case &lt;имяИгрока&gt; &lt;caseId&gt; [количество]
	/// Пример: mm_admin_give_case Vasya start_case 3
	/// </summary>
	[ConCmd( "mm_admin_give_case" )]
	public static void AdminGiveCase( string targetName, string caseId, int count = 1 )
	{
		if ( !RequireAdmin() ) return;
		var target = FindPlayerByName( targetName );
		if ( target == null ) { Log.Warning( $"[mm_admin] Игрок '{targetName}' не найден." ); return; }
		target.AdminGrantCase( caseId, count );
	}

	/// <summary>
	/// mm_admin_give_cosmetic &lt;имяИгрока&gt; &lt;slot&gt; &lt;itemId&gt;
	/// Пример: mm_admin_give_cosmetic Vasya nickname nick_dawn
	/// </summary>
	[ConCmd( "mm_admin_give_cosmetic" )]
	public static void AdminGiveCosmetic( string targetName, string slot, string itemId )
	{
		if ( !RequireAdmin() ) return;
		var target = FindPlayerByName( targetName );
		if ( target == null ) { Log.Warning( $"[mm_admin] Игрок '{targetName}' не найден." ); return; }
		target.AdminGrantCosmetic( slot, itemId );
	}

	/// <summary>
	/// mm_admin_give_crystals &lt;имяИгрока&gt; &lt;количество&gt;
	/// Пример: mm_admin_give_crystals Vasya 5000
	/// </summary>
	[ConCmd( "mm_admin_give_crystals" )]
	public static void AdminGiveCrystals( string targetName, int amount )
	{
		if ( !RequireAdmin() ) return;
		var target = FindPlayerByName( targetName );
		if ( target == null ) { Log.Warning( $"[mm_admin] Игрок '{targetName}' не найден." ); return; }
		// bypassDailyCap=true — админская консольная команда игнорирует анти-фарм cap.
		target.GrantCrystals( amount, bypassDailyCap: true );
	}

	/// <summary>mm_my_steamid — выводит твой Steam ID, удобно чтобы добавить себя в AdminList.</summary>
	[ConCmd( "mm_my_steamid" )]
	public static void PrintMySteamId()
	{
		try
		{
			var c = Connection.Local;
			if ( c == null ) { Log.Warning( "[mm] Нет локального соединения." ); return; }
			Log.Info( $"[mm] Your SteamId: {c.SteamId}  ({c.DisplayName})" );
		}
		catch ( System.Exception e ) { Log.Warning( $"[mm] {e.Message}" ); }
	}

	/// <summary>
	/// mm_admin_list_players — распечатать всех PlayerStats в консоль с индексом.
	/// Удобно смотреть точные имена перед выдачей.
	/// </summary>
	[ConCmd( "mm_admin_list_players" )]
	public static void AdminListPlayers()
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) { Log.Warning( "[mm_admin] Нет активной сцены." ); return; }
		var all = scene.GetAllComponents<PlayerStats>().ToList();
		if ( all.Count == 0 ) { Log.Info( "[mm_admin] Нет игроков." ); return; }
		for ( int i = 0; i < all.Count; i++ )
			Log.Info( $"[mm_admin] [{i}] '{all[i].DisplayName}'  crystals={all[i].Crystals}" );
	}

	private static PlayerStats FindPlayerByName( string name )
	{
		var scene = Game.ActiveScene;
		if ( scene == null ) return null;
		name = name.ToLower();
		return scene.GetAllComponents<PlayerStats>()
			.FirstOrDefault( p => ( p.DisplayName ?? "" ).ToLower().Contains( name ) );
	}
}
