namespace Sandbox;

/// <summary>
/// Single source of truth for the Modern Minimal Dark design tokens.
/// CSS values are duplicated inside each razor &lt;style&gt; block for readability,
/// but THIS file is authoritative — update here first, then sync to razor blocks.
/// </summary>
public static class Design
{
	// ── Backgrounds (depth 0→3) ──
	public const string Bg0 = "#0b0d10";
	public const string Bg1 = "#101317";
	public const string Bg2 = "#161a20";
	public const string Bg3 = "#1d222a";

	// ── Borders ──
	public const string BorderSubtle  = "rgba(255,255,255,0.06)";
	public const string BorderDefault = "rgba(255,255,255,0.10)";
	public const string BorderStrong  = "rgba(255,255,255,0.18)";

	// ── Text (4 levels of hierarchy) ──
	public const string TextPrimary   = "#e7eaee";
	public const string TextSecondary = "#a8b0bb";
	public const string TextTertiary  = "#6b727d";
	public const string TextDisabled  = "#3f4550";

	// ── Accent (cyan-blue, single accent for the whole system) ──
	public const string Accent       = "#5ea3ff";
	public const string AccentHover  = "#7eb6ff";
	public const string AccentActive = "#3f8ce8";
	public const string AccentBg     = "rgba(94,163,255,0.10)";
	public const string AccentBorder = "rgba(94,163,255,0.45)";

	// ── Semantic ──
	public const string Success   = "#4ec07a";
	public const string SuccessBg = "rgba(78,192,122,0.10)";
	public const string Warning   = "#e6b454";
	public const string WarningBg = "rgba(230,180,84,0.12)";
	public const string Danger    = "#e05c5c";
	public const string DangerBg  = "rgba(224,92,92,0.12)";

	// ── Role colors ──
	public const string RoleInnocent  = "#a8b0bb";
	public const string RoleDetective = "#5ea3ff";
	public const string RoleMurderer  = "#e05c5c";
	public const string RoleSpectator = "#6b727d";

	// ── Radii ──
	public const string RSm = "6px";
	public const string RMd = "12px";

	// ── Shadows ──
	public const string Shadow1 = "0 1px 2px rgba(0,0,0,0.4)";
	public const string Shadow2 = "0 8px 24px rgba(0,0,0,0.4)";

	// ── Typography ──
	public const string FontFamily = "Inter, Roboto, sans-serif";

	/// <summary>Maps a player role to its canonical hex color.</summary>
	public static string RoleColor( PlayerStats.PlayerRole role ) => role switch
	{
		PlayerStats.PlayerRole.Murderer  => RoleMurderer,
		PlayerStats.PlayerRole.Detective => RoleDetective,
		_                                => RoleInnocent
	};
}
