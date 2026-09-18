/// <summary>
/// Глобальный флаг — открыто ли окно ввода промокода.
/// </summary>
public static class PromoCodeUIState
{
	public static bool IsOpen { get; set; } = false;
	public static void Toggle() => IsOpen = !IsOpen;
}
