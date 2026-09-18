/// <summary>
/// Shared static state for AdminPanelUI — lives in game code so DebugCommands
/// (a regular .cs file) can toggle the panel without referencing the Razor component.
/// </summary>
public static class AdminPanelState
{
	public static bool IsOpen { get; set; } = false;
	public static void Toggle() => IsOpen = !IsOpen;
}
