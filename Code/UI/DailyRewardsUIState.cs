/// <summary>
/// Глобальный флаг — открыто ли окно ежедневных наград.
/// </summary>
public static class DailyRewardsUIState
{
	public static bool IsOpen { get; set; } = false;
	public static void Toggle() => IsOpen = !IsOpen;
}
