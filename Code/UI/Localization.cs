using Sandbox;
using System;
using System.Collections.Generic;

/// <summary>
/// Минималистичная клиент-локальная локализация. Язык хранится в FileSystem.Data
/// (lang.txt), так что у каждого игрока — своя настройка. Дефолт — English.
/// Использование в razor: @L.T("key"), либо L.T("key").
/// </summary>
public static class L
{
	public enum Lang { EN, RU }

	private const string FileName = "lang.txt";

	private static Lang _current = Lang.EN;
	private static bool _loaded = false;

	// Bumped on language change so razor BuildHash() can react.
	public static int Version { get; private set; } = 0;

	public static Lang Current
	{
		get { EnsureLoaded(); return _current; }
	}

	public static void SetLanguage( Lang lang )
	{
		EnsureLoaded();
		if ( _current == lang ) return;
		_current = lang;
		Version++;
		try { FileSystem.Data.WriteAllText( FileName, lang.ToString() ); }
		catch ( Exception e ) { Log.Warning( $"[L] save failed: {e.Message}" ); }
	}

	private static void EnsureLoaded()
	{
		if ( _loaded ) return;
		_loaded = true;
		try
		{
			if ( FileSystem.Data.FileExists( FileName ) )
			{
				var raw = FileSystem.Data.ReadAllText( FileName )?.Trim();
				if ( Enum.TryParse<Lang>( raw, true, out var parsed ) )
					_current = parsed;
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"[L] load failed: {e.Message}" );
		}
	}

	public static string T( string key )
	{
		EnsureLoaded();
		var dict = _current == Lang.RU ? _ru : _en;
		if ( dict.TryGetValue( key, out var v ) ) return v;
		// Fallback: try EN, otherwise return the key.
		if ( _current != Lang.EN && _en.TryGetValue( key, out var ev ) ) return ev;
		return key;
	}

	public static string T( string key, params object[] args )
	{
		var fmt = T( key );
		try { return string.Format( fmt, args ); }
		catch { return fmt; }
	}

	/// <summary>
	/// Регистрирует/перезаписывает пару ключ-значение в обоих языках на лету.
	/// Используется компонентами вроде CustomEmotesComponent для динамической
	/// регистрации имён предметов прямо из инспектора. Если EN или RU пустой —
	/// для этого языка ничего не пишется.
	/// </summary>
	public static void Register( string key, string en, string ru )
	{
		if ( string.IsNullOrEmpty( key ) ) return;
		if ( !string.IsNullOrEmpty( en ) ) _en[key] = en;
		if ( !string.IsNullOrEmpty( ru ) ) _ru[key] = ru;
		Version++;
	}

	// ====== EN (default) ======
	private static readonly Dictionary<string, string> _en = new()
	{
		// Settings menu
		["settings.title"]      = "MENU",
		["settings.language"]   = "Language",
		["settings.close"]      = "Close",
		["settings.lang.en"]    = "English",
		["settings.lang.ru"]    = "Русский",
		["settings.hint"]       = "Press ESC to toggle",
		["settings.resume"]     = "Resume",
		["settings.leave"]      = "Leave to Hub",
		["settings.players"]    = "Players",
		["settings.players_n"]  = "Players ({0})",
		["settings.back"]       = "Back",
		["settings.no_players"] = "No players",
		["settings.disconnect"] = "Server List",
		["settings.quit"]       = "Quit Game",
		["settings.exit_hub"]   = "Exit to Server Hub",
		["settings.audio"]      = "Audio",
		["settings.music"]      = "Music",
		["settings.sfx"]        = "Sound effects",
		["settings.voice"]      = "Voice chat",
		["settings.mute"]       = "Mute",
		["settings.reset"]      = "Reset",
		["settings.keybinds"]   = "Keybindings (rebind chat/trade/etc.)",
		["settings.engine"]     = "Game Settings (s&box)",
		["settings.engine_menu"] = "Open s&box Menu",
		["lobby.title"]         = "SERVER LIST",
		["lobby.refresh"]       = "Refresh",
		["lobby.refreshing"]    = "Loading...",
		["lobby.empty"]         = "No active lobbies",
		["lobby.join"]          = "Join",
		["lobby.you"]           = "(this server)",

		// Chat
		["chat.placeholder"]    = "type /hub to leave...",
		["chat.return_hub"]     = "Returning to hub...",
		["chat.unknown_cmd"]    = "Unknown command: /{0}",
		["chat.hint"]           = "open chat",
		["chat.cooldown"]       = "wait {0}s before next message",
		["voice.hint"]          = "hold to talk",
		["chat.cmd.guide.in_room"] = "⚠ /guide is only available in the hub. Finish the round first!",

		// Matchmaking HUD
		["mm.searching"]        = "SEARCHING FOR GAME",
		["mm.players"]          = "PLAYERS",
		["mm.players_needed"]   = "players needed",
		["mm.more_needed"]      = "more needed to start",
		["mm.starting"]         = "match found — entering the room",
		["mm.cancel"]           = "Cancel",
		["mm.cancel_hint"]      = "← walk to the portal to cancel",
		["mm.tir_hint"]         = "💡 you can enter the shooting range — search continues",
		["tir.board.title"]     = "🎯 TOP STREAKS",
		["tir.board.empty"]     = "no streaks yet — go score one!",
		["tir.streak"]          = "STREAK",
		["tir.board.my_record"] = "your best",
		["portal.cancel_search"]    = "✕ Cancel matchmaking",
		["portal.searching_title"]  = "MATCHMAKING IN PROGRESS",
		["portal.searching_sub"]    = "you can keep moving · cancel below",

		// Hub portal menu
		["portal.title"]        = "GAME SELECTION",
		["portal.quickplay"]    = "⚡ Quick Play",
		["portal.quickplay_hint"] = "find a match · {0} searching ({1} needed)",
		["portal.no_rooms"]     = "no rooms available",
		["portal.players"]      = "players",
		["portal.enter"]        = "Join",
		["portal.spectate"]     = "Spectate",
		["portal.full"]         = "Full",
		["portal.close"]        = "Close",
		["portal.state.waiting"]      = "waiting · need {0}",
		["portal.state.starting_in"]  = "start in {0}s",
		["portal.state.starting"]     = "round starting...",
		["portal.state.playing"]      = "in progress",
		["portal.state.roundend"]     = "round ending",

		// Banners / HUD
		["hud.waiting_title"]   = "WAITING FOR PLAYERS",
		["hud.choose_in_menu"]  = "Pick a game in the portal menu",
		["hud.players_of"]      = "Players: {0} / {1} (need {2})",
		["hud.before_start"]    = "Until round start",
		["hud.waiting_more"]    = "Waiting for players...",
		["hud.starting"]        = "GAME STARTING",
		["hud.roles_dealing"]   = "Dealing roles...",
		["hud.you_dead"]        = "YOU ARE DEAD",
		["hud.spectate_round"]  = "Spectate the round",
		["hud.cooldown"]        = "COOLDOWN",
		["hud.ready_strike"]    = "READY TO STRIKE",
		["hud.next_round_in"]   = "Next round in {0}s",

		// Murderer radar (catch-up)
		["radar.target"]             = "Target",
		["radar.murderer.active"]    = "Hunter Vision",

		// Daily rewards
		["daily.btn.hint"]           = "Daily",
		["daily.title"]              = "Daily rewards",
		["daily.day"]                = "Day {0}",
		["daily.streak"]             = "Streak: {0}",
		["daily.today"]              = "Today",
		["daily.claim"]              = "Claim",
		["daily.claimed"]            = "Claimed",
		["daily.come_back"]          = "Come back tomorrow",
		["daily.success"]            = "Reward: {0}",
		["daily.bad.already"]        = "You already claimed today's reward",
		["daily.bad.unavailable"]    = "Reward is not configured",
		["daily.bad.no_game"]        = "Play one match today to unlock the reward",
		["daily.need_game"]          = "Play a match to unlock",

		// Trade
		["trade.btn.hint"]                  = "Trade",
		["trade.list.title"]                = "Trade with player",
		["trade.list.hint"]                 = "Pick a player in the hub to send a trade request.",
		["trade.list.empty"]                = "No other players in the hub right now.",
		["trade.list.invite"]               = "Invite",
		["trade.list.waiting"]              = "Waiting for {0} to respond…",
		["trade.list.cancel_invite"]        = "Cancel",
		["trade.invite.title"]              = "Trade request",
		["trade.invite.from"]               = "{0} wants to trade with you",
		["trade.invite.accept"]             = "Accept",
		["trade.invite.decline"]            = "Decline",
		["trade.window.title"]              = "Trade with {0}",
		["trade.window.you"]                = "You",
		["trade.window.crystals"]           = "Crystals",
		["trade.window.crystals.set"]       = "Set",
		["trade.window.ready"]              = "Ready",
		["trade.window.unready"]            = "Unready",
		["trade.window.cancel"]             = "Cancel",
		["trade.window.close"]              = "Close",
		["trade.window.chat.placeholder"]   = "Discuss the deal…",
		["trade.window.chat.send"]          = "Send",
		["trade.picker.title"]              = "Pick an item to offer",
		["trade.picker.empty"]              = "You don't have any tradeable items.",
		["trade.bad.no_target"]             = "No target selected",
		["trade.bad.busy"]                  = "You're already in a trade",
		["trade.bad.not_in_hub"]            = "You can only trade in the hub",
		["trade.bad.target_not_in_hub"]     = "That player is not in the hub",
		["trade.bad.target_busy"]           = "That player is already trading",
		["trade.bad.missing_items"]         = "Trade aborted: you no longer own all offered items",
		["trade.info.invite_sent"]          = "Invite sent",
		["trade.info.declined"]             = "Trade declined",
		["trade.end.success"]               = "Trade complete!",
		["trade.end.autoclose"]             = "auto-closing in {0}s…",
		["trade.end.cancelled"]             = "Trade cancelled",
		["trade.end.cancelled_by_self"]     = "You cancelled the trade",
		["trade.end.cancelled_by_partner"]  = "Partner cancelled the trade",
		["trade.end.cancelled_by_inviter"]  = "Invite was cancelled",
		["trade.end.missing_items"]         = "Trade aborted: items missing",

		// Promo codes
		["settings.promo"]           = "Promo code",
		["promo.btn.hint"]           = "Promo",
		["promo.title"]              = "Promo code",
		["promo.hint"]               = "Enter a promo code to claim your reward",
		["promo.placeholder"]        = "PROMO-CODE",
		["promo.btn.redeem"]         = "Redeem",
		["promo.btn.close"]          = "Close",
		["promo.bad.empty"]          = "Enter a code first",
		["promo.bad.unknown"]        = "Invalid or expired code",
		["promo.bad.used"]           = "Code already redeemed",
		["promo.success"]            = "Reward: {0}",

		// Spectator
		["spec.you_dead"]            = "YOU ARE DEAD",
		["spec.mode"]                = "SPECTATOR MODE",
		["spec.watching"]            = "👁 Watching: {0}",
		["spec.free_camera"]         = "Free camera",
		["spec.hint.next"]           = "[Space] Next",
		["spec.hint.prev"]           = "[R] Previous",
		["spec.hint.free_camera"]    = "[RMB] Free camera",
		["spec.hint.move"]           = "[WASD] Fly",
		["spec.hint.boost"]          = "[Shift] Speed boost",

		// Roles
		["role.murderer"]       = "MURDERER",
		["role.detective"]      = "DETECTIVE",
		["role.innocent"]       = "INNOCENT",
		["role.spectator"]      = "SPECTATOR",

		// Scoreboard / status
		["status.spectator"]    = "👁 Spectator",
		["status.dead"]         = "💀 Dead",
		["status.alive_hidden"] = "❔ Alive",
		["status.murderer"]     = "🔪 Murderer",
		["status.detective"]    = "🔍 Detective",
		["status.innocent"]     = "🧑 Innocent",

		// Hub board
		["board.stats_title"]   = "STATS",
		["board.wins"]          = "Wins",
		["board.kills"]         = "Kills",
		["board.survived"]      = "Rounds survived",
		["board.detective"]     = "Times detective",
		["board.catches"]       = "Murderers caught",
		["board.empty_player"]  = "Join a game to see stats",
		["board.empty_top"]     = "Nobody has played yet",
		["board.top_wins"]      = "TOP WINS",
		["board.top_kills"]     = "TOP KILLS",
		["board.top_survived"]  = "TOP SURVIVED",
		["board.top_detective"] = "TOP DETECTIVES",
		["board.top_catches"]   = "TOP CATCHES",

		// Winners
		["win.innocents"]       = "✅ Innocents win!",
		["win.murderer"]        = "🔪 Murderer wins!",
		["win.timeout"]         = "⏰ Time's up! Murderer wins.",

		// Round-end screen
		["roundend.your_role"]   = "Your role",
		["roundend.coins"]       = "Coins collected",
		["roundend.kills"]       = "Kills",
		["roundend.survived"]    = "Survived",
		["roundend.alive"]       = "Yes",
		["roundend.dead"]        = "No",
		["roundend.time_lived"]  = "Time alive",
		["roundend.outcome"]     = "Result",
		["roundend.victory"]     = "VICTORY",
		["roundend.defeat"]      = "DEFEAT",
		["roundend.return_lobby"] = "Return to Lobby",
		["roundend.stay"]        = "Stay in Game",

		// Round-end rewards breakdown
		["roundend.rewards_title"]            = "Round Rewards",
		["roundend.reward.match_complete"]    = "Match completed",
		["roundend.reward.alive_time"]        = "Alive time ({0})",
		["roundend.reward.coins_count"]       = "Coins collected ({0})",
		["roundend.reward.murderer_kills_count"] = "Murderer takedowns ({0})",
		["roundend.reward.team_win_alive"]    = "Team win (survived)",
		["roundend.reward.team_win_dead"]     = "Team win (died)",
		["roundend.reward.team_win_lost"]     = "Team lost",
		["roundend.reward.kills_count"]       = "Kills ({0})",
		["roundend.reward.murderer_perfect"]  = "Murderer victory",
		["roundend.reward.total"]             = "TOTAL",

		// Shop
		["shop.title"]          = "SHOPKEEPER",
		["shop.offer"]          = "Trade 10 coins for a pistol?",
		["shop.bad.murderer"]   = "The shopkeeper smells you. Leave.",
		["shop.bad.has_weapon"] = "You already have a weapon.",
		["shop.bad.no_coins"]   = "Not enough coins ({0} / {1})",
		["shop.good.press_e"]   = "Press [E] to trade",

		// Cosmetic vendor
		["shop2.title"]              = "CASES",
		["shop2.press_e"]            = "Press [E] to browse cases",
		["shop2.balance"]            = "💎 {0}",
		["shop2.tab.shop"]           = "Shop",
		["shop2.tab.inventory"]      = "Inventory",
		["shop2.tab.knife"]          = "Knife",
		["shop2.tab.back"]           = "Back",
		["shop2.tab.hat"]            = "Hat",
		["shop2.btn.buy"]            = "Buy",
		["shop2.btn.equip"]          = "Equip",
		["shop2.btn.equipped"]       = "Equipped",
		["shop2.btn.unequip"]        = "Unequip",
		["shop2.btn.locked"]         = "Not enough",
		["shop2.btn.open_soon"]      = "Open (soon)",
		["shop2.btn.open"]           = "Open",
		["shop2.section.cases"]      = "CASES",
		["shop2.section.cosmetics"]  = "COSMETICS",
		["shop2.empty_shop"]         = "This vendor has no cases configured",
		["shop2.empty_inventory"]    = "Your inventory is empty",
		["shop2.btn.inspect"]        = "👁 Inspect",
		["shop2.preview.subtitle"]   = "CASE CONTENTS",
		["shop2.toast.bought"]       = "🎁 Bought: {0}",
		["case_open.btn.spin"]       = "Spin",
		["case_open.btn.rolling"]    = "Rolling…",
		["case_open.btn.claim"]      = "Claim",
		["case_open.btn.open_another"] = "Open another",
		["case_open.empty"]          = "No cases of this type",
		["case_open.hint.have_n"]    = "You own: {0}",
		["case_open.drops_title"]    = "By opening this case you'll receive one of the following items:",
		["cosmetic.knife_default"]   = "Default knife",
		["cosmetic.knife_katana"]    = "Katana",
		["cosmetic.knife_katana2"]   = "Katana II",
		["cosmetic.knife_golden"]    = "Golden knife",
		["cosmetic.gun_default"]     = "Default pistol",
		["cosmetic.back_none"]       = "None",
		["cosmetic.back_wings_angel"] = "Devil wings",
		["cosmetic.back_cape_red"]   = "Hero cape",
		["cosmetic.hat_none"]        = "None",
		["cosmetic.hat_top"]         = "Axolotl hat",
		["cosmetic.hat_crown"]       = "Crown",
		// Nickname cosmetics
		["cosmetic.nick_default"]    = "Default",
		["cosmetic.nick_chalk"]      = "Chalk",
		["cosmetic.nick_typewriter"] = "Typewriter",
		["cosmetic.nick_cinema"]     = "Cinematic",
		["cosmetic.nick_neon_blue"]  = "Neon Sign Blue",
		["cosmetic.nick_neon_pink"]  = "Neon Sign Pink",
		["cosmetic.nick_police"]     = "Police Lights",
		["cosmetic.nick_glitch"]     = "Glitch",
		["cosmetic.nick_blood"]      = "Fresh Blood",
		["cosmetic.nick_ghost"]      = "Ghost",
		// Super Rare — main_case drops
		["cosmetic.nick_cyber"]      = "Cyber",
		["cosmetic.nick_emerald"]    = "Emerald",
		["cosmetic.nick_inferno"]    = "Inferno",
		["cosmetic.nick_frost"]      = "Frost",
		["cosmetic.nick_void"]       = "Void",
		["cosmetic.nick_acid"]       = "Acid",
		["cosmetic.nick_suspect"]    = "Suspect №1",
		["cosmetic.nick_justice"]    = "Justice",
		["cosmetic.nick_ash"]        = "Ashes",
		// Legendary — main_case top drop
		["cosmetic.nick_godlike"]    = "Godlike",
		// Starter exclusives
		["cosmetic.nick_rookie"]     = "Rookie",
		["cosmetic.nick_origin"]     = "Origin",
		["cosmetic.nick_dawn"]       = "Dawn",
		// Currency drops (case rolls)
		["cosmetic.crystals_50"]     = "+50 Crystals",
		["cosmetic.crystals_100"]    = "+100 Crystals",
		["cosmetic.crystals_150"]    = "+150 Crystals",
		["cosmetic.crystals_300"]    = "+300 Crystals",
		["cosmetic.crystals_500"]    = "+500 Crystals",
		// Emotes (default wheel)
		["cosmetic.emote_smile"]     = "Smile",
		["cosmetic.emote_laugh"]     = "Laugh",
		["cosmetic.emote_sad"]       = "Sad",
		["cosmetic.emote_angry"]     = "Angry",
		["cosmetic.emote_heart"]     = "Heart",
		["cosmetic.emote_shocked"]   = "Shocked",
		["emote.wheel.hint"]         = "Release to play",
		["emote.btn.hint"]           = "Hold for emotes",
		// Rarity
		["rarity.common"]            = "Common",
		["rarity.rare"]              = "Rare",
		["rarity.super_rare"]        = "Super Rare",
		["rarity.legendary"]         = "Legendary",
		// Cases
		["hud.crystals"]             = "💎 {0}",
		["match.reward.crystals"]    = "+{0} 💎",
        ["case.premium"]             = "Premium",
        ["case.starter"]             = "Starter",
        ["case.nicknames"]           = "Business Card Case",
        ["case.start"]               = "Welcome Case",
		// Inventory (hub-only)
		["inventory.title"]          = "INVENTORY",
		["inventory.tab.cases"]      = "Cases",
		["inventory.tab.cosmetics"]  = "Cosmetics",
		["inventory.empty_cases"]    = "No cases yet — buy one from the vendor",
		["inventory.empty_cosmetics"] = "No cosmetics — open cases to get them",
		["inventory.slot.all"]       = "All",
		["inventory.slot.nickname"]  = "Nickname",
		["inventory.slot.knife"]     = "Knife",
		["inventory.slot.gun"]       = "Gun",
		["inventory.slot.hat"]       = "Hat",
		["inventory.slot.back"]      = "Back",
		["inventory.slot.emote"]     = "Emotes",
		["inventory.emote.wheel_title"]     = "Emote wheel",
		["inventory.emote.hint_pick_slot"]  = "Pick a slot above, then click an emote to assign",
		["inventory.emote.hint_pick_emote"] = "Now click an emote below to put it in the selected slot",
		["inventory.emote.in_slot"]         = "IN SLOT",
		["inventory.emote.pick_slot_first"] = "PICK SLOT FIRST",
		["inventory.emote.assign"]          = "ASSIGN TO SLOT",
		["inventory.btn.hint"]       = "Inventory",

		// Hotbar slots
		["hotbar.coins"]             = "Coins",
		["hotbar.knife"]             = "Knife",
		["hotbar.pistol"]            = "Pistol",

		// Stamina HUD (#7)
		["stamina.label"]            = "STAMINA",
		["stamina.exhausted"]        = "Tired!",

		// Tutorial (#24)
		["tutorial.title"]           = "How to Play",
		["tutorial.skip"]            = "Skip",
		["tutorial.next"]            = "Next →",
		["tutorial.done"]            = "Let's Play!",
		["tutorial.step_n"]          = "{0} / {1}",
		["tutorial.s1.title"]        = "Welcome to Murder Mystery!",
		["tutorial.s1.body"]         = "A Murderer is hiding among the players. Survive, investigate, and stop them — or eliminate everyone if you're the killer.",
		["tutorial.s2.title"]        = "Three Roles",
		["tutorial.s2.b1"]           = "🧑 Innocent — collect coins, survive, and expose the Murderer.",
		["tutorial.s2.b2"]           = "🔪 Murderer — kill everyone before you're caught.",
		["tutorial.s2.b3"]           = "🔍 Detective — you start with a gun. Find and eliminate the Murderer!",
		["tutorial.s3.title"]        = "Coins & Weapons",
		["tutorial.s3.body"]         = "Coins appear around the map. Collect 10 and trade them at the Shop NPC for a pistol. Innocents can arm themselves!",
		["tutorial.s4.title"]        = "Sprint & Stamina",
		["tutorial.s4.body"]         = "Hold Shift to sprint, but you only have 3 seconds before you're forced to walk. Stamina regens automatically — manage it wisely!",
		["tutorial.s5.title"]        = "Chat & Voice",
		["tutorial.s5.body"]         = "Press Y to open text chat. Hold V to use voice chat. Only living players hear each other — spectators are isolated.",
	};

	// ====== RU ======
	private static readonly Dictionary<string, string> _ru = new()
	{
		["settings.title"]      = "МЕНЮ",
		["settings.language"]   = "Язык",
		["settings.close"]      = "Закрыть",
		["settings.lang.en"]    = "English",
		["settings.lang.ru"]    = "Русский",
		["settings.hint"]       = "Нажмите ESC чтобы открыть/закрыть",
		["settings.resume"]     = "Продолжить",
		["settings.leave"]      = "Выйти в хаб",
		["settings.players"]    = "Игроки",
		["settings.players_n"]  = "Игроки ({0})",
		["settings.back"]       = "Назад",
		["settings.no_players"] = "Нет игроков",
		["settings.disconnect"] = "Список серверов",
		["settings.quit"]       = "Выйти из игры",
		["settings.exit_hub"]   = "Выйти в хаб серверов",
		["settings.audio"]      = "Звук",
		["settings.music"]      = "Музыка",
		["settings.sfx"]        = "Звуковые эффекты",
		["settings.voice"]      = "Голосовой чат",
		["settings.mute"]       = "Выкл",
		["settings.reset"]      = "Сброс",
		["settings.keybinds"]   = "Управление (перепривязка чата/трейда и т.д.)",
		["settings.engine"]     = "Системные настройки (s&box)",
		["settings.engine_menu"] = "Открыть меню s&box",
		["lobby.title"]         = "СПИСОК СЕРВЕРОВ",
		["lobby.refresh"]       = "Обновить",
		["lobby.refreshing"]    = "Загрузка...",
		["lobby.empty"]         = "Нет активных лобби",
		["lobby.join"]          = "Войти",
		["lobby.you"]           = "(этот сервер)",

		["chat.placeholder"]    = "введи /hub чтобы выйти...",
		["chat.return_hub"]     = "Возвращаемся в хаб...",
		["chat.unknown_cmd"]    = "Неизвестная команда: /{0}",
		["chat.hint"]           = "открыть чат",
		["chat.cooldown"]       = "подожди {0} сек перед следующим сообщением",
		["voice.hint"]          = "удерживай — голос",
		["chat.cmd.guide.in_room"] = "⚠ /guide доступен только в хабе. Сначала заверши раунд!",

		// Matchmaking HUD
		["mm.searching"]        = "ПОИСК ИГРЫ",
		["mm.players"]          = "ИГРОКОВ",
		["mm.players_needed"]   = "игроков нужно",
		["mm.more_needed"]      = "осталось до старта",
		["mm.starting"]         = "матч найден — заходим в комнату",
		["mm.cancel"]           = "Отмена",
		["mm.cancel_hint"]      = "← подойди к порталу чтобы отменить",
		["mm.tir_hint"]         = "💡 можно зайти в тир — поиск продолжится",
		["tir.board.title"]     = "🎯 ТОП СТРИКОВ",
		["tir.board.empty"]     = "пока ни одного стрика — будь первым!",
		["tir.streak"]          = "СТРИК",
		["tir.board.my_record"] = "твой рекорд",
		["portal.cancel_search"]    = "✕ Отменить поиск",
		["portal.searching_title"]  = "ИДЁТ ПОИСК ИГРЫ",
		["portal.searching_sub"]    = "можно бегать по хабу · отмена ниже",

		["portal.title"]        = "ВЫБОР ИГРЫ",
		["portal.quickplay"]    = "⚡ Быстрая игра",
		["portal.quickplay_hint"] = "поиск матча · {0} ищет ({1} нужно)",
		["portal.no_rooms"]     = "нет доступных комнат",
		["portal.players"]      = "игроков",
		["portal.enter"]        = "Войти",
		["portal.spectate"]     = "Наблюдать",
		["portal.full"]         = "Полная",
		["portal.close"]        = "Закрыть",
		["portal.state.waiting"]      = "ожидание · нужно {0}",
		["portal.state.starting_in"]  = "старт через {0}с",
		["portal.state.starting"]     = "раунд начинается...",
		["portal.state.playing"]      = "игра идёт",
		["portal.state.roundend"]     = "конец раунда",

		["hud.waiting_title"]   = "ОЖИДАНИЕ ИГРОКОВ",
		["hud.choose_in_menu"]  = "Выберите игру в меню портала",
		["hud.players_of"]      = "Игроков: {0} / {1} (нужно {2})",
		["hud.before_start"]    = "До старта раунда",
		["hud.waiting_more"]    = "Ждём игроков...",
		["hud.starting"]        = "ИГРА НАЧИНАЕТСЯ",
		["hud.roles_dealing"]   = "Роли раздаются...",
		["hud.you_dead"]        = "ВЫ МЕРТВЫ",
		["hud.spectate_round"]  = "Наблюдайте за раундом",
		["hud.cooldown"]        = "ПЕРЕЗАРЯДКА",
		["hud.ready_strike"]    = "ГОТОВ К УДАРУ",
		["hud.next_round_in"]   = "Новый раунд через {0}с",

		// Murderer radar (catch-up)
		["radar.target"]             = "Цель",
		["radar.murderer.active"]    = "Зрение охотника",

		// Daily rewards
		["daily.btn.hint"]           = "Награды дня",
		["daily.title"]              = "Ежедневные награды",
		["daily.day"]                = "День {0}",
		["daily.streak"]             = "Стрик: {0}",
		["daily.today"]              = "Сегодня",
		["daily.claim"]              = "Получить",
		["daily.claimed"]            = "Получено",
		["daily.come_back"]          = "Возвращайся завтра",
		["daily.success"]            = "Награда: {0}",
		["daily.bad.already"]        = "Награда за сегодня уже получена",
		["daily.bad.unavailable"]    = "Награда не настроена",
		["daily.bad.no_game"]        = "Сыграй одну партию сегодня, чтобы получить награду",
		["daily.need_game"]          = "Сыграй партию, чтобы получить",

		// Trade
		["trade.btn.hint"]                  = "Обмен",
		["trade.list.title"]                = "Обмен с игроком",
		["trade.list.hint"]                 = "Выбери игрока в хабе, чтобы отправить ему приглашение.",
		["trade.list.empty"]                = "Сейчас в хабе нет других игроков.",
		["trade.list.invite"]               = "Пригласить",
		["trade.list.waiting"]              = "Ждём ответа от {0}…",
		["trade.list.cancel_invite"]        = "Отменить",
		["trade.invite.title"]              = "Приглашение на обмен",
		["trade.invite.from"]               = "{0} хочет обменяться с тобой",
		["trade.invite.accept"]             = "Принять",
		["trade.invite.decline"]            = "Отклонить",
		["trade.window.title"]              = "Обмен с {0}",
		["trade.window.you"]                = "Ты",
		["trade.window.crystals"]           = "Кристаллы",
		["trade.window.crystals.set"]       = "Установить",
		["trade.window.ready"]              = "Готов",
		["trade.window.unready"]            = "Не готов",
		["trade.window.cancel"]             = "Отменить",
		["trade.window.close"]              = "Закрыть",
		["trade.window.chat.placeholder"]   = "Обсуди сделку…",
		["trade.window.chat.send"]          = "Послать",
		["trade.picker.title"]              = "Выбери предмет для обмена",
		["trade.picker.empty"]              = "Нет предметов, которые можно обменять.",
		["trade.bad.no_target"]             = "Игрок не выбран",
		["trade.bad.busy"]                  = "Ты уже в сделке",
		["trade.bad.not_in_hub"]            = "Обмен доступен только в хабе",
		["trade.bad.target_not_in_hub"]     = "Этот игрок не в хабе",
		["trade.bad.target_busy"]           = "Этот игрок уже в сделке",
		["trade.bad.missing_items"]         = "Обмен отменён: каких-то предметов уже нет",
		["trade.info.invite_sent"]          = "Приглашение отправлено",
		["trade.info.declined"]             = "Приглашение отклонено",
		["trade.end.success"]               = "Обмен совершён!",
		["trade.end.autoclose"]             = "автозакрытие через {0} сек…",
		["trade.end.cancelled"]             = "Обмен отменён",
		["trade.end.cancelled_by_self"]     = "Ты отменил обмен",
		["trade.end.cancelled_by_partner"]  = "Партнёр отменил обмен",
		["trade.end.cancelled_by_inviter"]  = "Приглашение отменено",
		["trade.end.missing_items"]         = "Обмен прерван: нет нужных предметов",

		// Promo codes
		["settings.promo"]           = "Промокод",
		["promo.btn.hint"]           = "Промокод",
		["promo.title"]              = "Промокод",
		["promo.hint"]               = "Введите промокод чтобы получить награду",
		["promo.placeholder"]        = "ПРОМОКОД",
		["promo.btn.redeem"]         = "Активировать",
		["promo.btn.close"]          = "Закрыть",
		["promo.bad.empty"]          = "Сначала введите код",
		["promo.bad.unknown"]        = "Неверный или просроченный код",
		["promo.bad.used"]           = "Код уже был использован",
		["promo.success"]            = "Награда: {0}",

		// Spectator
		["spec.you_dead"]            = "ВЫ МЕРТВЫ",
		["spec.mode"]                = "РЕЖИМ НАБЛЮДАТЕЛЯ",
		["spec.watching"]            = "👁 Наблюдаем: {0}",
		["spec.free_camera"]         = "Свободная камера",
		["spec.hint.next"]           = "[Space] Следующий",
		["spec.hint.prev"]           = "[R] Предыдущий",
		["spec.hint.free_camera"]    = "[ПКМ] Свободная камера",
		["spec.hint.move"]           = "[WASD] Лететь",
		["spec.hint.boost"]          = "[Shift] Ускорение",

		["role.murderer"]       = "УБИЙЦА",
		["role.detective"]      = "ДЕТЕКТИВ",
		["role.innocent"]       = "МИРНЫЙ",
		["role.spectator"]      = "НАБЛЮДАТЕЛЬ",

		["status.spectator"]    = "👁 Наблюдатель",
		["status.dead"]         = "💀 Мёртв",
		["status.alive_hidden"] = "❔ Жив",
		["status.murderer"]     = "🔪 Убийца",
		["status.detective"]    = "🔍 Детектив",
		["status.innocent"]     = "🧑 Мирный",

		["board.stats_title"]   = "СТАТИСТИКА",
		["board.wins"]          = "Побед",
		["board.kills"]         = "Убийств",
		["board.survived"]      = "Выжил раундов",
		["board.detective"]     = "Был детективом",
		["board.catches"]       = "Поймал убийц",
		["board.empty_player"]  = "Зайди в игру чтобы увидеть статы",
		["board.empty_top"]     = "Никто ещё не играл",
		["board.top_wins"]      = "ТОП ПО ПОБЕДАМ",
		["board.top_kills"]     = "ТОП ПО УБИЙСТВАМ",
		["board.top_survived"]  = "ТОП ВЫЖИВАЕМОСТИ",
		["board.top_detective"] = "ТОП ДЕТЕКТИВОВ",
		["board.top_catches"]   = "ТОП ПО ПОИМКАМ",

		["win.innocents"]       = "✅ Мирные победили!",
		["win.murderer"]        = "🔪 Убийца победил!",
		["win.timeout"]         = "⏰ Время вышло! Убийца победил.",

		["roundend.your_role"]   = "Ваша роль",
		["roundend.coins"]       = "Монет собрано",
		["roundend.kills"]       = "Убийств",
		["roundend.survived"]    = "Выжил",
		["roundend.alive"]       = "Да",
		["roundend.dead"]        = "Нет",
		["roundend.time_lived"]  = "Прожил",
		["roundend.outcome"]     = "Результат",
		["roundend.victory"]     = "ПОБЕДА",
		["roundend.defeat"]      = "ПОРАЖЕНИЕ",
		["roundend.return_lobby"] = "Вернуться в лобби",
		["roundend.stay"]        = "Остаться в игре",

		// Round-end rewards breakdown
		["roundend.rewards_title"]            = "Награды раунда",
		["roundend.reward.match_complete"]    = "Завершение матча",
		["roundend.reward.alive_time"]        = "Время жизни ({0})",
		["roundend.reward.coins_count"]       = "Монет собрано ({0})",
		["roundend.reward.murderer_kills_count"] = "Убийств убийцы ({0})",
		["roundend.reward.team_win_alive"]    = "Победа команды (жив)",
		["roundend.reward.team_win_dead"]     = "Победа команды (мёртв)",
		["roundend.reward.team_win_lost"]     = "Команда проиграла",
		["roundend.reward.kills_count"]       = "Убийств ({0})",
		["roundend.reward.murderer_perfect"]  = "Победа убийцы",
		["roundend.reward.total"]             = "ИТОГО",

		["shop.title"]          = "ТОРГОВЕЦ",
		["shop.offer"]          = "Обменять 10 монет на пистолет?",
		["shop.bad.murderer"]   = "Торговец вас чует. Уходите.",
		["shop.bad.has_weapon"] = "У вас уже есть оружие.",
		["shop.bad.no_coins"]   = "Не хватает монет ({0} / {1})",
		["shop.good.press_e"]   = "Нажмите [E] для обмена",

		// Cosmetic vendor
		["shop2.title"]              = "КЕЙСЫ",
		["shop2.press_e"]            = "Нажмите [E] чтобы открыть магазин",
		["shop2.balance"]            = "💎 {0}",
		["shop2.tab.shop"]           = "Магазин",
		["shop2.tab.inventory"]      = "Инвентарь",
		["shop2.tab.knife"]          = "Нож",
		["shop2.tab.back"]           = "Спина",
		["shop2.tab.hat"]            = "Шляпа",
		["shop2.btn.buy"]            = "Купить",
		["shop2.btn.equip"]          = "Надеть",
		["shop2.btn.equipped"]       = "Надето",
		["shop2.btn.unequip"]        = "Снять",
		["shop2.btn.locked"]         = "Не хватает",
		["shop2.btn.open_soon"]      = "Открыть (скоро)",
		["shop2.btn.open"]           = "Открыть",
		["shop2.section.cases"]      = "КЕЙСЫ",
		["shop2.section.cosmetics"]  = "КОСМЕТИКА",
		["shop2.empty_shop"]         = "У этого торговца пока нет кейсов",
		["shop2.empty_inventory"]    = "Инвентарь пуст",
		["shop2.btn.inspect"]        = "👁 Осмотреть",
		["shop2.preview.subtitle"]   = "СОДЕРЖИМОЕ КЕЙСА",
		["shop2.toast.bought"]       = "🎁 Куплено: {0}",
		["case_open.btn.spin"]       = "Прокрутить",
		["case_open.btn.rolling"]    = "Крутится…",
		["case_open.btn.claim"]      = "Забрать",
		["case_open.btn.open_another"] = "Открыть ещё",
		["case_open.empty"]          = "Нет кейсов этого типа",
		["case_open.hint.have_n"]    = "В наличии: {0}",
		["case_open.drops_title"]    = "Открыв этот набор, вы получите один из предметов:",
		["cosmetic.knife_default"]   = "Обычный нож",
		["cosmetic.knife_katana"]    = "Катана",
		["cosmetic.knife_katana2"]   = "Катана II",
		["cosmetic.knife_golden"]    = "Золотой нож",
		["cosmetic.gun_default"]     = "Обычный пистолет",
		["cosmetic.back_none"]       = "Ничего",
		["cosmetic.back_wings_angel"] = "Крылья дьявола",
		["cosmetic.back_cape_red"]   = "Плащ героя",
		["cosmetic.hat_none"]        = "Ничего",
		["cosmetic.hat_top"]         = "Шляпа аксолотля",
		["cosmetic.hat_crown"]       = "Корона",
		// Nickname cosmetics
		["cosmetic.nick_default"]    = "Обычный",
		["cosmetic.nick_chalk"]      = "Мел",
		["cosmetic.nick_typewriter"] = "Печатная машинка",
		["cosmetic.nick_cinema"]     = "Кинематограф",
		["cosmetic.nick_neon_blue"]  = "Неоновая вывеска (синяя)",
		["cosmetic.nick_neon_pink"]  = "Неоновая вывеска (розовая)",
		["cosmetic.nick_police"]     = "Полицейская мигалка",
		["cosmetic.nick_glitch"]     = "Глитч",
		["cosmetic.nick_blood"]      = "Свежая кровь",
		["cosmetic.nick_ghost"]      = "Призрак",
		// Super Rare — main_case
		["cosmetic.nick_cyber"]      = "Кибер",
		["cosmetic.nick_emerald"]    = "Изумруд",
		["cosmetic.nick_inferno"]    = "Инферно",
		["cosmetic.nick_frost"]      = "Мороз",
		["cosmetic.nick_void"]       = "Пустота",
		["cosmetic.nick_acid"]       = "Кислота",
		["cosmetic.nick_suspect"]    = "Подозреваемый №1",
		["cosmetic.nick_justice"]    = "Правосудие",
		["cosmetic.nick_ash"]        = "Пепел",
		// Legendary — main_case top
		["cosmetic.nick_godlike"]    = "Богоподобный",
		// Starter exclusives
		["cosmetic.nick_rookie"]     = "Новичок",
		["cosmetic.nick_origin"]     = "Истоки",
		["cosmetic.nick_dawn"]       = "Рассвет",
		// Currency drops (case rolls)
		["cosmetic.crystals_50"]     = "+50 кристаллов",
		["cosmetic.crystals_100"]    = "+100 кристаллов",
		["cosmetic.crystals_150"]    = "+150 кристаллов",
		["cosmetic.crystals_300"]    = "+300 кристаллов",
		["cosmetic.crystals_500"]    = "+500 кристаллов",
		// Emotes (default wheel)
		["cosmetic.emote_smile"]     = "Улыбка",
		["cosmetic.emote_laugh"]     = "Смех",
		["cosmetic.emote_sad"]       = "Грусть",
		["cosmetic.emote_angry"]     = "Злость",
		["cosmetic.emote_heart"]     = "Сердце",
		["cosmetic.emote_shocked"]   = "Шок",
		["emote.wheel.hint"]         = "Отпусти для активации",
		["emote.btn.hint"]           = "Зажми — эмоции",
		// Rarity
		["rarity.common"]            = "Обычный",
		["rarity.rare"]              = "Редкий",
		["rarity.super_rare"]        = "Сверхредкий",
		["rarity.legendary"]         = "Легендарный",
		// Cases
		["hud.crystals"]             = "💎 {0}",
		["match.reward.crystals"]    = "+{0} 💎",
        ["case.premium"]             = "Премиум",
        ["case.starter"]             = "Стартовый",
        ["case.nicknames"]           = "Кейс «Визитная карточка»",
        ["case.start"]               = "Кейс новичка",
		// Inventory (hub-only)
		["inventory.title"]          = "ИНВЕНТАРЬ",
		["inventory.tab.cases"]      = "Кейсы",
		["inventory.tab.cosmetics"]  = "Косметика",
		["inventory.empty_cases"]    = "Кейсов нет — купи у торговца",
		["inventory.empty_cosmetics"] = "Косметики нет — открывай кейсы",
		["inventory.slot.all"]       = "Все",
		["inventory.slot.nickname"]  = "Ник",
		["inventory.slot.knife"]     = "Нож",
		["inventory.slot.gun"]       = "Пистолет",
		["inventory.slot.hat"]       = "Шляпа",
		["inventory.slot.back"]      = "Спина",
		["inventory.slot.emote"]     = "Эмоции",
		["inventory.emote.wheel_title"]     = "Колесо эмоций",
		["inventory.emote.hint_pick_slot"]  = "Выбери слот выше, потом кликни по эмоции",
		["inventory.emote.hint_pick_emote"] = "Теперь кликни по эмоции ниже — она встанет в выбранный слот",
		["inventory.emote.in_slot"]         = "В СЛОТЕ",
		["inventory.emote.pick_slot_first"] = "СНАЧАЛА СЛОТ",
		["inventory.emote.assign"]          = "В СЛОТ",
		["inventory.btn.hint"]       = "Инвентарь",

		// Hotbar slots
		["hotbar.coins"]             = "Монеты",
		["hotbar.knife"]             = "Нож",
		["hotbar.pistol"]            = "Пистолет",

		// Stamina HUD (#7)
		["stamina.label"]            = "СТАМИНА",
		["stamina.exhausted"]        = "Устал!",

		// Tutorial (#24)
		["tutorial.title"]           = "Как играть",
		["tutorial.skip"]            = "Пропустить",
		["tutorial.next"]            = "Далее →",
		["tutorial.done"]            = "Поехали!",
		["tutorial.step_n"]          = "{0} / {1}",
		["tutorial.s1.title"]        = "Добро пожаловать в Murder Mystery!",
		["tutorial.s1.body"]         = "Среди игроков скрывается Убийца. Выживи, расследуй — или уничтожь всех, если это ты.",
		["tutorial.s2.title"]        = "Три роли",
		["tutorial.s2.b1"]           = "🧑 Мирный — собирай монеты, выживай, найди Убийцу.",
		["tutorial.s2.b2"]           = "🔪 Убийца — устрани всех, пока тебя не поймали.",
		["tutorial.s2.b3"]           = "🔍 Детектив — у тебя есть пистолет с самого начала. Найди и останови Убийцу!",
		["tutorial.s3.title"]        = "Монеты и оружие",
		["tutorial.s3.body"]         = "Монеты разбросаны по карте. Собери 10 и обменяй у Торговца на пистолет. Мирные могут вооружиться!",
		["tutorial.s4.title"]        = "Спринт и стамина",
		["tutorial.s4.body"]         = "Удерживай Shift для бега, но стамины хватает лишь на 3 секунды — потом только шаг. Восстанавливается автоматически!",
		["tutorial.s5.title"]        = "Чат и голос",
		["tutorial.s5.body"]         = "Нажми Y чтобы открыть чат. Удерживай V для голосового. Живые слышат только живых — спектаторы изолированы.",
	};
}
